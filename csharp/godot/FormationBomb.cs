using Godot;

namespace InfiAir;

/// <summary>
/// 轰炸编队事件·下落炸弹（可交互威胁，对象池复用——归属事件 FormationStrikeEvent 管理）：
/// 引信制下落弹——投放时继承编队水平速度 ×0.35 + 垂直下落，引信到期在**弹体当前位置**引爆，
/// 对 player_hitbox 做距离判定并按边缘衰减结算伤害（中心满伤、边缘 floor 倍，见
/// balance formation_strike_event.bomb_edge_falloff）。
/// 三种结局都由玩家操作决定：
///   1) 命中/引信到期引爆 —— 地面爆炸 + 范围伤害；
///   2) 空中被击落（IDamageable，bomb_hp 点）—— 静默引爆（小爆炸、无地面伤害）并给分；
///   3) 被弧光弹反盾反射（IParryable）—— 反向上升转为对编队的攻击，命中编队机按
///      bomb_reflect_damage 结算（「原样奉还」语义）。
/// 可读性：落点圈（完整伤害半径的实心轮廓 + 收缩倒计时弧）标出「会炸到哪、还剩多久」，
/// 圈随引信收缩但**亮度递增**（最危险的一刻最显眼，与「越缩越暗」相反）。
/// 与敌弹语义差异：不注册进敌弹注册表（不是弹幕流），也不触发玩家受击宽限——爆炸是即时判定。
/// 池化契约：外观在 _Ready 一次性构建；Activate 重置全部运行态与外观（含 _Ready 不再重跑的
/// 回收复用路径）；终局路径一律 ReturnToPool（池失效时 QueueFree 兜底）。
/// </summary>
public partial class FormationBomb : Area2D, IDamageable, IParryable
{
    private const int RingSegments = 32;

    /// <summary>反射弹寻敌转向加速度（px/s²）——够快以咬住横穿的编队，又不至于瞬间掉头。</summary>
    private const float ReflectTurnAccel = 1600.0f;

    /// <summary>弹头本色（_Ready 初值；弹反改冰蓝，Activate 复位）。</summary>
    private static readonly Color WarheadColor = new(1.0f, 0.42f, 0.14f);

    /// <summary>投放参数（事件 Setup 注入；数值源 formation_strike_event.*）。</summary>
    public Vector2 Velocity { get; set; } = new(0.0f, 300.0f);
    public float Fuse { get; set; } = 1.2f;
    public int Damage { get; set; } = 20;
    public float AoeRadius { get; set; } = 120.0f;
    public float EdgeFalloff { get; set; } = 0.35f;
    public int BombScore { get; set; } = 50;
    public int ReflectDamage { get; set; } = 45;

    public int MaxHp { get; set; } = 8;
    public int Hp { get; set; } = 8;

    /// <summary>已被弹反（反射后不再可被再次弹反，且对编队机造成伤害）。</summary>
    public bool IsReflected { get; private set; }

    /// <summary>是否已引爆/被击落（防同帧双路径重入结算）。</summary>
    private bool _spent;

    /// <summary>已回池（防回收路径重入；Activate 复位）。IsParked 供池侧停放步骤跳过已复活的弹。</summary>
    private bool _parked;

    public bool IsParked() => _parked;

    /// <summary>归属池（事件侧；事件节点与 Main 同生命周期，一般不失效）。</summary>
    private FormationStrikeEvent? _pool;

    /// <summary>是否在引爆前就被拆掉（空中击落 / 弹反命中）——拦截计奖的唯一依据；
    /// 引信到期引爆的弹**不算**拦截（否则「没躲开」也能领拦截奖）。</summary>
    public bool Intercepted { get; private set; }

    private float _fuseLeft;
    private float _t; // 相位
    private Node2D _body = null!;
    private Polygon2D _warhead = null!;
    private Line2D _trail = null!;
    private Line2D _ring = null!;
    private Line2D _fuseArc = null!;
    private readonly Vector2[] _arcPoints = new Vector2[RingSegments + 2];
    private readonly Vector2[] _unitRing = new Vector2[RingSegments + 1];

    /// <summary>setup() 在入树/_Ready() 之前调用。</summary>
    public void Setup(Vector2 pVelocity, float pFuse, int pDamage, float pRadius)
    {
        Velocity = pVelocity;
        Fuse = pFuse;
        Damage = pDamage;
        AoeRadius = pRadius;
    }

    /// <summary>登记归属池（事件侧 Acquire 首次创建时调用）。</summary>
    public void SetPool(FormationStrikeEvent pool) => _pool = pool;

    /// <summary>
    /// 池化激活：重置全部运行态与外观。新弹（_Ready 刚建完外观）重复调用幂等；
    /// 回收复用弹经此恢复——_Ready 不重跑，BindEnemy/碰撞层/引信/弹反态都在这里复位。
    /// </summary>
    public void Activate()
    {
        _parked = false;
        _spent = false;
        Intercepted = false;
        IsReflected = false;
        _t = 0.0f;
        _fuseLeft = Fuse;
        CollisionLayer = 8; // 复位敌弹语义层（弹反态改写过）
        CollisionMask = 1;
        if (_warhead != null)
        {
            _warhead.Color = WarheadColor;
            _warhead.Modulate = Colors.White; // 受击闪白复位
        }

        if (_ring != null)
        {
            _ring.Visible = true; // 弹反态隐藏的落点圈/倒计时弧复位
            _ring.Scale = Vector2.One * AoeRadius;
        }

        if (_fuseArc != null)
        {
            _fuseArc.Visible = true;
            _fuseArc.Scale = Vector2.One * AoeRadius;
            UpdateFuseArc();
        }

        SetProcess(true);
        Visible = true;
        GameState.Instance.BindEnemy(this); // 停放期 reparent 触发 _ExitTree 解绑，复用需重绑
    }

    /// <summary>池化停用：只停结算与可见性（可发生在物理信号分发中，不改树）；
    /// reparent 回池节点由事件侧帧末批量执行。</summary>
    public void Deactivate()
    {
        _spent = true; // 停掉 TakeDamage/Reflect/OnAreaEntered/_Process 全部结算路径
        SetProcess(false);
        Visible = false;
    }

    /// <summary>终局统一出口：回池（池失效时 QueueFree 兜底）。引信到期/被击落/弹反命中/
    /// 出界四条路径共用。</summary>
    public void ReturnToPool()
    {
        if (_parked)
        {
            return;
        }

        _parked = true;
        if (_pool != null && GodotObject.IsInstanceValid(_pool))
        {
            _pool.ReleaseBomb(this);
        }
        else
        {
            QueueFree();
        }
    }

    public override void _Ready()
    {
        CollisionLayer = 8; // 第 4 层：enemy_bullet（玩家子弹/弹反盾以此层命中本弹）
        CollisionMask = 1; // 纯语义文档：对 player_hitbox 走引信到期后的距离判定，无进入信号
        var ws = (float)GameState.Instance.WorldScale;
        _fuseLeft = Fuse;

        // ---- 弹体：弹头 + 尾迹，整体朝速度方向（俯视视角下「在往下掉」一眼可读）----
        _body = new Node2D();
        AddChild(_body);
        _trail = new Line2D
        {
            Points = new[] { new Vector2(0.0f, -26.0f) * ws, Vector2.Zero },
            Width = 3.0f * ws,
            DefaultColor = new Color(1.0f, 0.55f, 0.25f, 0.5f),
        };
        _trail.Gradient = TrailGradient();
        _body.AddChild(_trail);
        _warhead = new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(0.0f, -11.0f) * ws,
                new Vector2(7.0f, -2.0f) * ws,
                new Vector2(5.0f, 10.0f) * ws,
                new Vector2(-5.0f, 10.0f) * ws,
                new Vector2(-7.0f, -2.0f) * ws,
            },
            Color = WarheadColor,
        };
        _body.AddChild(_warhead);
        var shape = new CollisionShape2D { Shape = new CircleShape2D { Radius = 12.0f * ws } };
        AddChild(shape);
        // 命中只在反射态生效（见 OnAreaEntered）：未反射时区域判定为空跑，
        // 保留连接以免反射瞬间才连信号（信号时序不确定）
        AreaEntered += OnAreaEntered;

        // ---- 落点圈：伤害半径的完整轮廓（不随引信缩放，边界即实际生效边界）----
        for (var i = 0; i <= RingSegments; i++)
        {
            var a = Mathf.Tau * i / RingSegments;
            _unitRing[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        _ring = new Line2D
        {
            Points = _unitRing,
            Width = 4.0f,
            Closed = true,
            DefaultColor = new Color(1.0f, 0.35f, 0.15f, 0.55f),
        };
        _ring.Scale = Vector2.One * AoeRadius;
        _ring.ZIndex = -1; // 落点圈压在编队机/弹体之下，只当地面警示
        AddChild(_ring);

        // ---- 倒计时弧：随引信消耗变短，剩余越少越亮（与「越缩越暗」相反）----
        _fuseArc = new Line2D
        {
            Points = _unitRing,
            Width = 6.0f,
            DefaultColor = new Color(1.0f, 0.8f, 0.35f, 0.9f),
        };
        _fuseArc.Scale = Vector2.One * AoeRadius;
        _fuseArc.ZIndex = -1;
        AddChild(_fuseArc);
        UpdateFuseArc();

        GameState.Instance.BindEnemy(this); // 与编队机同口径：玩家子弹可命中、清场可销毁
    }

    public override void _ExitTree() => GameState.Instance.UnbindEnemy(this);

    /// <summary>尾迹渐隐（尾端透明、弹头端实）。</summary>
    private static Gradient TrailGradient()
    {
        var g = new Gradient();
        g.SetColor(0, new Color(1.0f, 0.55f, 0.25f, 0.0f));
        g.SetColor(1, new Color(1.0f, 0.7f, 0.35f, 0.75f));
        return g;
    }

    public override void _Process(double delta)
    {
        var d = (float)delta;
        if (_spent)
        {
            return;
        }

        _t += d;
        Position += Velocity * d;
        _body.Rotation = Velocity.Angle() - Mathf.Pi / 2.0f; // 弹头朝运动方向（机头朝上贴图语义）

        if (IsReflected)
        {
            SteerHome(d);
            if (!FrameCache.ViewRect().Grow(120.0f).HasPoint(Position))
            {
                ReturnToPool(); // 反射弹未命中即出界（无爆炸：它已经不再是威胁）
            }

            return;
        }

        // 引信越接近到期，弹体脉冲越快越亮——视听同源的紧迫感
        _fuseLeft -= d;
        var frac = Mathf.Clamp(_fuseLeft / Fuse, 0.0f, 1.0f);
        var urgency = 1.0f - frac;
        var pulse = 0.6f + 0.4f * Mathf.Abs(Enemy.SinFast(_t * Mathf.Pi * (5.0f + 9.0f * urgency)));
        _warhead.Modulate = new Color(1.0f, 1.0f, 1.0f, pulse);
        _ring.DefaultColor = new Color(1.0f, 0.35f + 0.45f * urgency, 0.15f, 0.45f + 0.5f * urgency);
        _fuseArc.DefaultColor = new Color(1.0f, 0.8f, 0.35f * (1.0f - urgency), 0.55f + 0.45f * urgency);
        UpdateFuseArc();

        if (_fuseLeft <= 0.0f)
        {
            Detonate();
            return;
        }

        // 出界即回收，不留悬空节点
        if (!FrameCache.ViewRect().Grow(120.0f).HasPoint(Position))
        {
            ReturnToPool();
        }
    }

    /// <summary>反射弹轻量寻敌：朝最近的敌方单位缓转（速率上限），保证「弹反成功」不靠运气——
    /// 编队横穿 + 弹体上抛的几何关系太容易擦身而过，纯直线反射会让最难的操作为零回报。</summary>
    private void SteerHome(float delta)
    {
        var nearest = NearestEnemy();
        if (nearest == null)
        {
            return;
        }

        var toTarget = nearest.GlobalPosition - GlobalPosition;
        if (toTarget.LengthSquared() < 1.0f)
        {
            return;
        }

        var speed = Velocity.Length();
        var desired = toTarget.Normalized() * speed;
        Velocity = Velocity.MoveToward(desired, ReflectTurnAccel * delta);
    }

    private Node2D? NearestEnemy()
    {
        Node2D? best = null;
        var bestDist = float.MaxValue;
        foreach (var node in GameState.Instance.Enemies)
        {
            if (node is not Node2D node2d || node is FormationBomb || !GodotObject.IsInstanceValid(node2d))
            {
                continue;
            }

            var dist = node2d.GlobalPosition.DistanceSquaredTo(GlobalPosition);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = node2d;
            }
        }

        return best;
    }

    /// <summary>倒计时弧：12 点起顺时针按剩余引信比例绘制（弧长 = 剩余时间）。</summary>
    private void UpdateFuseArc()
    {
        var frac = Fuse <= 0.0f ? 0.0f : Mathf.Clamp(_fuseLeft / Fuse, 0.0f, 1.0f);
        var count = Mathf.Clamp((int)Mathf.Ceil((RingSegments + 1) * frac), 1, RingSegments + 1);
        for (var i = 0; i < count; i++)
        {
            // 角度自 -90° 起顺时针：即从 12 点方向向右扫
            var a = -Mathf.Pi / 2.0f + Mathf.Tau * i / RingSegments;
            _arcPoints[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        _fuseArc.Points = _arcPoints[..count];
    }

    // ---------------- IDamageable：空中被击落 ----------------

    public void TakeDamage(int amount, float scoreScale)
    {
        if (_spent || IsReflected)
        {
            return; // 已反射的弹是「玩家弹」，不再吃玩家火力
        }

        Hp -= amount;
        if (_warhead != null)
        {
            _warhead.Modulate = new Color(2.2f, 2.2f, 2.2f);
        }

        if (Hp <= 0)
        {
            Intercept();
        }
    }

    public void TakeDamage(int amount) => TakeDamage(amount, 1.0f);

    /// <summary>空中拦截：静默引爆——只有小爆炸与拦截分，不产生地面范围伤害（玩家的回报是「拆掉了威胁」）。
    /// 走 AddKillScore 而非 AddScore：拦截是打断敌方行动，计入连击链（与被击落的编队机同族）。</summary>
    private void Intercept()
    {
        _spent = true;
        Intercepted = true;
        GameState.Instance.PlaySfx(SfxId.FireB, -6.0, 1.5);
        Explosion.SpawnAt(GetParent(), GlobalPosition, 0.45f);
        GameState.Instance.AddKillScore(BombScore);
        ReturnToPool();
    }

    // ---------------- IParryable：弧光弹反 ----------------

    /// <summary>弹反：反向上升并把伤害换成对编队的攻击（水平分量保留），此后与编队机碰撞结算。</summary>
    public bool Reflect()
    {
        if (_spent || IsReflected)
        {
            return false;
        }

        IsReflected = true;
        Velocity = new Vector2(-Velocity.X * 0.4f, -Mathf.Abs(Velocity.Y) * 1.25f);
        CollisionLayer = 4; // 转为 player_bullet 语义层：可命中 enemy 层（编队机）
        CollisionMask = 4; // 只探 enemy 层：反射弹不再威胁玩家，穿过玩家不受影响
        _warhead.Color = new Color(0.55f, 0.85f, 1.0f); // 冰蓝：一眼看出「这枚现在属于我」
        _ring.Visible = false;
        _fuseArc.Visible = false;
        SetProcess(false); // 反射弹不再引爆：命中即结算
        return true;
    }

    private void OnAreaEntered(Area2D area)
    {
        if (_spent || !IsReflected)
        {
            return;
        }

        // 反射弹只打敌方单位（enemy 层），命中后即消耗
        if (area is not IDamageable target)
        {
            return;
        }

        target.TakeDamage(ReflectDamage, 1.0f);
        _spent = true;
        Intercepted = true; // 弹反命中＝成功拆弹，计拦截奖
        GameState.Instance.PlaySfx(SfxId.Explosion, -4.0, 1.3);
        Explosion.SpawnAt(GetParent(), GlobalPosition, 0.7f);
        ReturnToPool();
    }

    // ---------------- 引爆 ----------------

    /// <summary>引信到期引爆：地面爆炸 + 距离判定（边缘衰减）。</summary>
    private void Detonate()
    {
        _spent = true;
        Explosion.SpawnAt(GetParent(), GlobalPosition, 1.1f);
        GameState.Instance.PlaySfx(SfxId.Explosion, -3.0, 0.85);
        GameState.Instance.Shake(GameState.Instance.Cfg("effects.shake.enemy_die", 5.0).AsDouble());
        var hitbox = GameState.Instance.PlayerHitbox;
        var player = GameState.Instance.PlayerRef;
        if (hitbox != null && GodotObject.IsInstanceValid(hitbox) && player != null)
        {
            var hitboxNode = (Node2D)hitbox;
            var dist = hitboxNode.GlobalPosition.DistanceTo(GlobalPosition);
            if (dist <= AoeRadius)
            {
                // 边缘衰减：中心满伤 → 边缘 floor 倍（硬边会让「擦到即满伤」不可读）
                var t = AoeRadius <= 0.0f ? 0.0f : Mathf.Clamp(dist / AoeRadius, 0.0f, 1.0f);
                var scaled = Damage * Mathf.Lerp(1.0f, Mathf.Clamp(EdgeFalloff, 0.0f, 1.0f), t);
                // 不得硬强转 hitbox.get_parent()：Player 节点结构变动即崩（与 Bullet 同口径）
                ((Player)player).TakeDamage(Mathf.Max(1.0f, scaled), GlobalPosition);
            }
        }

        ReturnToPool();
    }
}
