using Godot;

namespace InfiAir;

/// <summary>
/// 直线子弹：玩家弹与敌弹共用
/// bullet.tscn，经 setup/activate 区分阵营。正常产弹走 GameState.bullet_pool（对象池复用）；
/// 直接实例化（不经对象池）走兼容路径。
/// 保持语义：碰撞半径唯一事实源；活跃计数；共享图集单 Sprite2D；
/// 公平机制一（受击宽限）/二（擦弹单次）/四（弹反）；敌弹注册表；辅助瞄准追踪；
/// 致死高亮；宽限/擦弹/反射的池化复位。
/// 命中结算经 EntityDamage 统一分派（IDamageable 契约）；生产调用方均为 C# typed。
/// </summary>
public partial class Bullet : Area2D
{
    /// <summary>碰撞半径唯一事实源（Player 擦弹环形带判定引用此常量）。</summary>
    public const float CollisionRadius = 6.0f;

    /// <summary>bullet_type meta 键静态缓存（Enemy/TurretBattery/BossFire 写入，
    /// 本类 ApplyFaction 复位消费；不得每发 SetMeta/HasMeta 字符串字面量转换）。</summary>
    internal static readonly StringName MetaBulletType = new("bullet_type");

    /// <summary>组名静态缓存（命中热路径 IsInGroup 字符串字面量逐次转换，MetaBulletType 同款）。</summary>
    private static readonly StringName GroupEnemy = new("enemy");
    private static readonly StringName GroupPlayerHitbox = new("player_hitbox");

    /// <summary>访问器（供 Player 擦弹环形带判定等调用方读取；常量唯一事实源不变）。</summary>
    public static float GetCollisionRadius() => CollisionRadius;

    public Vector2 Direction { get; set; } = Vector2.Down;
    public float Speed { get; set; } = 900.0f;
    public int Damage { get; set; } = 1;
    public bool IsPlayerBullet { get; set; } = true;
    public bool Homing { get; set; }
    public float HomingTime { get; set; }
    /// <summary>追踪转向速率（rad 级插值系数；精英炮台弱锁定追踪弹降为 1.5）。</summary>
    public float HomingTurnRate { get; set; } = 4.0f;
    /// <summary>辅助瞄准追踪目标（玩家弹专用）；池化 activate 复位为 null。</summary>
    public Node2D? HomingTarget { get; set; }
    /// <summary>穿透剩余次数（玩家弹，穿透弹增幅）。</summary>
    public int Pierce { get; set; }
    /// <summary>命中产生 AoE 爆炸（玩家弹，爆炸弹增幅）。</summary>
    public bool Explosive { get; set; }
    /// <summary>导弹溅射（母舰导弹）。</summary>
    public int SplashDamage { get; set; }
    public float SplashRadius { get; set; }
    /// <summary>击毁得分系数（母舰弹为 1/3）。</summary>
    public float ScoreScale { get; set; } = 1.0f;

    /// <summary>爆炸弹增幅 固定值（对齐原作单层取值：半径 50、伤害 30）。</summary>
    public float ExplosiveRadius { get; private set; } = 50.0f;
    public int ExplosiveDamage { get; private set; } = 30;
    /// <summary>弹体视觉缩放（设计值 × world_scale，碰撞半径不变）。</summary>
    public float VisualScale { get; private set; } = 1.3f;
    /// <summary>敌弹视觉缩放（设计值 × world_scale）。</summary>
    public float EnemyVisualScale { get; private set; } = 2.4f;
    /// <summary>辅助瞄准追踪近距收敛半径。</summary>
    public float HomingSnapRadius { get; private set; } = 36.0f;
    /// <summary>受击宽限帧窗口（秒），balance.json player.grace_period 钳制 (0, 0.15]。</summary>
    public float GracePeriod { get; private set; } = 0.05f;
    /// <summary>弹反倍率（player.parry.*）。</summary>
    public float ReflectSpeedMult { get; private set; } = 2.0f;
    public float ReflectDamageMult { get; private set; } = 1.5f;

    /// <summary>场上活跃子弹总数（activate/deactivate 成对维护；不经对象池直实例化的弹不计）。</summary>
    public static int ActiveCount { get; private set; }

    private float _homingElapsed;
    private BulletPool? _pool; // typed
    private bool _active;
    private bool _repooling;
    private Godot.Timer? _graceTimer;
    private Area2D? _graceHitbox;
    /// <summary>宽限入口位（相对命中框圆心；玩家移动经两端同减圆心自然抵消）——离场穿核判定用。</summary>
    private Vector2 _graceEntryRel;
    /// <summary>命中框核心半径缓存（= 形状半径，已含 world_scale；&lt;0 未读）。</summary>
    private float _graceHitboxR = -1.0f;
    private bool _grazeDone;
    private Sprite2D? _sprite;

    /// <summary>共享图集 Sprite2D（弹体+白芯光栅化进单张共享纹理）。</summary>
    private static readonly Vector2I TexSize = new(24, 8);
    private static readonly Vector2 TexOffset = new(11.0f, 4.0f);
    private static readonly Vector2[] ArrowBody =
    {
        new(-10, -3), new(4, -3), new(12, 0), new(4, 3), new(-10, 3),
    };
    private static readonly Vector2[] ArrowCore =
    {
        new(-4.5f, -1.5f), new(2, -1.5f), new(5.5f, 0), new(2, 1.5f), new(-4.5f, 1.5f),
    };

    // 弹体共享纹理缓存放 GameState 实例字段（BulletPlayerTex/BulletEnemyTex）——
    // 静态字段持 Godot 对象为退出 segfault 实测根因（Main/Spawner 同规）

    /// <summary>回池（Player 磁吸拾取等路径调用）。</summary>
    public void Despawn() => DespawnInternal();

    /// <summary>兼容路径：直接实例化时 setup() 后由 _ready 应用阵营外观（4 参便捷重载）。</summary>
    public void Setup(Vector2 pDirection, float pSpeed, int pDamage, bool pIsPlayer)
    {
        Setup(pDirection, pSpeed, pDamage, pIsPlayer, false, 0.0f);
    }

    public void Setup(Vector2 pDirection, float pSpeed, int pDamage, bool pIsPlayer, bool pHoming, float pHomingTime)
    {
        Direction = pDirection.Normalized();
        // 零方向弹回退 DOWN（防静止弹永驻场景）
        if (Direction == Vector2.Zero)
        {
            Direction = Vector2.Down;
        }

        // 零速钳制（0 速弹不位移不脱界，永驻场景；直写字段绕过 Setup 时也兜底）
        Speed = Mathf.Max(pSpeed, 1.0f);
        // 敌方子弹伤害随本局进程 ramp
        Damage = pIsPlayer ? pDamage : Mathf.Max(1, (int)Mathf.Round(pDamage * GameState.Instance.EnemyDamageRamp()));
        IsPlayerBullet = pIsPlayer;
        Homing = pHoming;
        HomingTime = pHomingTime;
    }

    /// <summary>池化路径：激活并重置全部状态（4 参便捷重载）。</summary>
    public void Activate(Vector2 pDirection, float pSpeed, int pDamage, bool pIsPlayer)
    {
        Activate(pDirection, pSpeed, pDamage, pIsPlayer, false, 0.0f);
    }

    public void Activate(Vector2 pDirection, float pSpeed, int pDamage, bool pIsPlayer, bool pHoming, float pHomingTime)
    {
        Setup(pDirection, pSpeed, pDamage, pIsPlayer, pHoming, pHomingTime);
        _active = true;
        ActiveCount++;
        _homingElapsed = 0.0f;
        HomingTarget = null;
        _grazeDone = false;
        Pierce = 0;
        Explosive = false;
        SplashDamage = 0;
        SplashRadius = 0.0f;
        ScoreScale = 1.0f;
        HomingTurnRate = 4.0f;
        Visible = true;
        Monitoring = true;
        SetPhysicsProcess(true); // 位移走物理帧，与 Area2D overlap 检测同步
        ApplyFaction();
    }

    /// <summary>池化回收：停用但保留实例。</summary>
    public void Deactivate()
    {
        _active = false;
        ActiveCount--;
        Visible = false;
        SetPhysicsProcess(false);
        Position = new Vector2(-500.0f, -500.0f);
        // 回收弹移出敌弹注册表（death_replay 录制数据源）
        if (!IsPlayerBullet)
        {
            GameState.Instance.UnregisterEnemyBullet(this);
        }

        CancelGrace();
        // monitoring 关闭并入 BulletPool._Process 帧末批量停放（不得逐弹 CallDeferred，
        // 高频火力下原生消息队列 Variant 编组开销；idle 帧处理同样处于物理回调外，语义不变）
    }

    /// <summary>对象池协调的内部状态封装（禁止跨类直写 _ 私有字段）。</summary>
    public void SetPool(BulletPool pool) => _pool = pool;

    public bool IsActive() => _active;

    public void SetRepooling(bool value) => _repooling = value;

    /// <summary>视觉节点公开接口（单 Sprite2D，替代原 polygon_node/core_node 双节点）。</summary>
    public Sprite2D? SpriteNode()
    {
        if (_sprite == null)
        {
            _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
        }

        return _sprite;
    }

    /// <summary>机制二：擦弹单次计数——同一敌弹至多计 1 次（池化 activate 复位）。</summary>
    public bool TryGraze()
    {
        if (_grazeDone)
        {
            return false;
        }

        _grazeDone = true;
        return true;
    }

    /// <summary>机制四：弧光弹反——转玩家弹、镜面反射（盾法线=机头前方，direction.y 取反）、
    /// ×REFLECT_SPEED_MULT 返回、伤害 ×REFLECT_DAMAGE_MULT；追踪终止；取消受击宽限。</summary>
    public void Reflect()
    {
        IsPlayerBullet = true;
        Direction = new Vector2(Direction.X, -Direction.Y);
        Speed *= ReflectSpeedMult;
        Damage = Mathf.Max(1, (int)Mathf.Round(Damage * ReflectDamageMult));
        Homing = false;
        HomingTarget = null;
        CancelGrace();
        ApplyFaction();
    }

    public override void _Ready()
    {
        AreaEntered += OnAreaEntered;
        AreaExited += OnAreaExited;
        ExplosiveRadius = (float)GameState.Instance.Cfg("augments.explosive.radius_per_level", ExplosiveRadius).AsDouble();
        ExplosiveDamage = (int)GameState.Instance.Cfg("augments.explosive.damage_per_level", ExplosiveDamage).AsInt64();
        VisualScale = (float)GameState.Instance.Cfg("effects.bullet_visual_scale", VisualScale).AsDouble()
            * (float)GameState.Instance.WorldScale;
        EnemyVisualScale = (float)GameState.Instance.Cfg("effects.enemy_bullet_visual_scale", EnemyVisualScale).AsDouble()
            * (float)GameState.Instance.WorldScale;
        // 机制一：宽限窗口钳制 (0, 0.15]
        GracePeriod = Mathf.Clamp((float)GameState.Instance.Cfg("player.grace_period", GracePeriod).AsDouble(), 0.001f, 0.15f);
        // 机制四：弹反倍率
        ReflectSpeedMult = (float)GameState.Instance.Cfg("player.parry.reflect_speed_mult", ReflectSpeedMult).AsDouble();
        ReflectDamageMult = (float)GameState.Instance.Cfg("player.parry.reflect_damage_mult", ReflectDamageMult).AsDouble();
        // 碰撞半径：设计值 × 全局缩放（幂等赋值）
        var shape = GetNode<CollisionShape2D>("CollisionShape2D");
        if (shape.Shape is CircleShape2D circle)
        {
            circle.Radius = CollisionRadius * (float)GameState.Instance.WorldScale;
        }

        ApplyFaction();
    }

    public override void _ExitTree()
    {
        // 被外部 queue_free 时通知池移除引用；池内 reparent 也经此回调（_repooling 置位不算离开池）
        if (_pool != null && GodotObject.IsInstanceValid(_pool) && !_repooling)
        {
            _pool!.Forget(this);
        }

        // 池化弹被外部销毁（未走 deactivate）时补减活跃计数
        if (_active)
        {
            _active = false;
            ActiveCount--;
        }

        // 外部销毁同步移出敌弹注册表（幂等）
        if (!IsPlayerBullet)
        {
            GameState.Instance.UnregisterEnemyBullet(this);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        var d = (float)delta;
        // 本帧位移：追踪命中段会把它钳到目标点上，故提为局部量
        var step = Speed * d;
        if (HomingTarget != null)
        {
            // 辅助瞄准追踪：优先于 homing 玩家追踪分支；目标失效/超时限即直行
            if (!GodotObject.IsInstanceValid(HomingTarget))
            {
                HomingTarget = null;
            }
            else if (!(bool)GameState.Instance.EnemiesHas(HomingTarget)) // 注册表 O(1) 判定
            {
                HomingTarget = null;
            }
            else if (_homingElapsed < HomingTime)
            {
                _homingElapsed += d;
                var toTarget = HomingTarget.GlobalPosition - GlobalPosition;
                var dist = toTarget.Length();
                if (dist > 0.0f && dist <= HomingSnapRadius + step)
                {
                    // 近距直取：对准目标，并把本帧位移钳到目标点——位移大于目标判定直径时
                    // （子弹 1800~3600px/s → 单帧 30~60px，超过 ~37px 的命中窗口），
                    // 只对准不钳位会整步跨过目标，下一帧再从背后对准跨回，形成永不命中的
                    // 来回穿越：高射速/多弹道下就是整屏乱飞。钳位后落点必在判定内，本帧即结算。
                    Direction = toTarget / dist;
                    Rotation = Direction.Angle();
                    step = dist;
                }
                else if (dist > 0.0f)
                {
                    // 距离越近转向越急：螺旋收敛
                    // AngleTo 一次 atan2 直接得带符号角差，角度空间线性推进
                    // ≡ LerpAngle(from, to, t)（= from + wrap差*t）；Rotation 与 Direction 恒同步
                    // （所有写入点成对赋值），以 Rotation 累进替代再次取角，省第二次 atan2
                    var rate = HomingTurnRate * (1.0f + HomingSnapRadius * 2.0f / dist);
                    var angleDiff = Direction.AngleTo(toTarget);
                    // 单帧转角钳到剩余夹角：rate 随距离收紧而放大，不钳位会一步转过头，
                    // 下一帧再反向修正 → 目标附近左右摆动（乱飞的另一半来源）
                    var turn = Mathf.Clamp(angleDiff * rate * d, -Mathf.Abs(angleDiff), Mathf.Abs(angleDiff));
                    var newAngle = Rotation + turn;
                    Direction = Vector2.Right.Rotated(newAngle);
                    Rotation = newAngle;
                }
            }
        }
        else if (Homing && _homingElapsed < HomingTime)
        {
            _homingElapsed += d;
            var playerRef = GameState.Instance.PlayerRef;
            if (playerRef != null)
            {
                var playerNode = (Node2D)playerRef;
                // 同上——AngleTo 单 atan2 + Rotation 累进，等价 LerpAngle 双 atan2 链
                var newAngle = Rotation
                    + Direction.AngleTo(playerNode.GlobalPosition - GlobalPosition) * (HomingTurnRate * d);
                Direction = Vector2.Right.Rotated(newAngle);
                Rotation = newAngle;
            }
        }

        Position += Direction * step;
        if (!FrameCache.ViewRect().Grow(80.0f).HasPoint(Position))
        {
            DespawnInternal();
        }
    }

    /// <summary>爆炸弹增幅：命中时对周围敌机造成固定 AoE 伤害（主目标同吃，Boss 除外）。</summary>
    private void Explode()
    {
        var arr = GameState.Instance.Enemies; // Array<Node>，避免 Variant 拆装箱
        var radiusSq = ExplosiveRadius * ExplosiveRadius; // 平方距离比较免每敌 sqrt
        for (var i = arr.Count - 1; i >= 0; i--)
        {
            // 爆炸 AoE 仅作用于普通敌机（Enemy 子类）；Boss/炮塔/编队机与失效实例跳过。
            if (arr[i] is not Enemy enemy || !GodotObject.IsInstanceValid(enemy))
            {
                continue;
            }

            if (enemy.GlobalPosition.DistanceSquaredTo(GlobalPosition) <= radiusSq)
            {
                EntityDamage.Dispatch(enemy, ExplosiveDamage);
            }
        }

        Explosion.SpawnAt(GetParent(), GlobalPosition, 0.6f);
        GameState.Instance.PlaySfx(SfxId.Explosion);
    }

    /// <summary>导弹溅射（母舰导弹）：半径内全部敌机（含主目标与 Boss）追加固定伤害。</summary>
    private void Splash()
    {
        var arr = GameState.Instance.Enemies; // Array<Node>，避免 Variant 拆装箱
        var radiusSq = SplashRadius * SplashRadius; // 平方距离比较免每敌 sqrt
        for (var i = arr.Count - 1; i >= 0; i--)
        {
            var node = arr[i];
            if (node == null || !GodotObject.IsInstanceValid(node) || node is not Node2D n2d || n2d is not IDamageable)
            {
                continue;
            }

            if (n2d.GlobalPosition.DistanceSquaredTo(GlobalPosition) <= radiusSq)
            {
                EntityDamage.Dispatch(n2d, SplashDamage, ScoreScale);
            }
        }

        Explosion.SpawnAt(GetParent(), GlobalPosition, 0.8f);
        GameState.Instance.PlaySfx(SfxId.Explosion);
    }

    private void OnAreaEntered(Area2D area)
    {
        // 同物理帧重复命中守卫：monitoring 关闭延迟到帧末，已回收弹不再结算
        if (!_active && (_pool != null || IsQueuedForDeletion()))
        {
            return;
        }

        if (IsPlayerBullet)
        {
            if (area.IsInGroup(GroupEnemy))
            {
                // crit_shot 暴击：层数 × 基础概率判定，命中 ×倍率伤害（玩家侧缓存经 player_ref）
                var hitDamage = Damage;
                var pRef = GameState.Instance.PlayerRef;
                if (pRef is Player p) // typed（Player.CritChance/CritMultiplierValue 为 buff 缓存属性）
                {
                    var critChance = p.CritChance;
                    if (critChance > 0.0f && GD.Randf() < critChance)
                    {
                        hitDamage = (int)(Damage * p.CritMultiplierValue);
                    }
                }

                // 统一分派；直击路径带 ScoreScale
                EntityDamage.Dispatch(area, hitDamage, ScoreScale);

                // 原作爆炸弹对 Boss 路径完全不触发（无爆炸视觉/溅射），仅直击；
                // is_boss 语义 = Boss 恒 true（Enemy/Turret/Formation 无该方法即视为 true）
                if (Explosive && area is not Boss)
                {
                    Explode();
                }

                if (SplashDamage > 0)
                {
                    Splash();
                }

                if (Pierce > 0)
                {
                    Pierce--;
                }
                else
                {
                    DespawnInternal();
                }
            }
        }
        else if (area.IsInGroup(GroupPlayerHitbox))
        {
            // 机制一：受击宽限帧——进入 Hitbox 不立即结算；窗口内擦边离场免伤，贯穿核心仍结算（见 OnAreaExited）
            StartGraceCheck(area);
        }
    }

    /// <summary>机制一：弹离开玩家 Hitbox——擦边入框（轨迹最近距 &gt; 核心半径）窗口内离场 = 免伤；
    /// 贯穿核心（视觉直击）则结算。敌弹 420px/s 穿越 2.8px 核心仅 ~25ms，
    /// 必在 0.05s 宽限内离场，故离场不得直接 CancelGrace——否则直击永不结算（玩家对弹近乎无敌）。</summary>
    private void OnAreaExited(Area2D area)
    {
        if (!area.IsInGroup(GroupPlayerHitbox))
        {
            return;
        }

        if (_graceHitbox == area && _graceTimer != null && !_graceTimer.IsStopped()
            && SegmentClosestToOrigin(_graceEntryRel, GlobalPosition - area.GlobalPosition) <= _graceHitboxR)
        {
            CancelGrace();
            SettleHit();
            return;
        }

        CancelGrace();
    }

    /// <summary>点到原点距离（弹心相对轨迹段 ab 与命中框圆心最近距；事件率，开方可接受）。</summary>
    private static float SegmentClosestToOrigin(Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lenSq = ab.LengthSquared();
        var t = lenSq > 0.0f ? Mathf.Clamp(-a.Dot(ab) / lenSq, 0.0f, 1.0f) : 0.0f;
        return (a + ab * t).Length();
    }

    /// <summary>机制一：启动宽限窗口（事件驱动；一次性 Timer 挂子弹下随场景释放）。</summary>
    private void StartGraceCheck(Area2D hitbox)
    {
        if (_graceTimer != null && !_graceTimer.IsStopped())
        {
            return;
        }

        _graceHitbox = hitbox;
        // 穿核判定采样：入口相对位 + 核心半径（形状半径已含 world_scale，见 Player _hitboxRadius）
        _graceEntryRel = GlobalPosition - hitbox.GlobalPosition;
        if (_graceHitboxR < 0.0f)
        {
            _graceHitboxR = hitbox.GetNodeOrNull<CollisionShape2D>("CollisionShape2D")?.Shape is CircleShape2D c
                ? c.Radius
                : 2.8f; // 兜底 = 7×0.4 设计值
        }

        if (_graceTimer == null)
        {
            _graceTimer = new Godot.Timer { OneShot = true };
            _graceTimer.Timeout += OnGraceTimeout;
            AddChild(_graceTimer);
        }

        _graceTimer.WaitTime = GracePeriod;
        _graceTimer.Start();
    }

    private void CancelGrace()
    {
        _graceTimer?.Stop();
        _graceHitbox = null; // 回收弹不携带旧 Hitbox 悬空引用
    }

    /// <summary>宽限到期：单次 overlaps 复核——仍与 Hitbox 重叠才结算。</summary>
    private void OnGraceTimeout()
    {
        // 不以 _active 作守卫：非池化直实例化弹（Setup 路径）_active 恒 false，
        // 以它守卫会让兼容路径的宽限复核永远短路（受击免伤）。池化弹停用路径
        // 已由 Deactivate→CancelGrace 停 Timer，不会走到这里；摘树守卫防外部销毁
        if (!IsInsideTree() || _graceHitbox == null || !GodotObject.IsInstanceValid(_graceHitbox))
        {
            return;
        }

        var hitbox = _graceHitbox;
        _graceHitbox = null;
        if (!OverlapsArea(hitbox))
        {
            return;
        }

        SettleHit();
    }

    /// <summary>受击结算（宽限到期仍在框内 / 离场穿核两路径共用）：
    /// 既有链路含无敌/闪避/单帧守卫、受击清弹、致死高亮。</summary>
    private void SettleHit()
    {
        var pRef = GameState.Instance.PlayerRef;
        if (pRef == null)
        {
            return;
        }

        var player = (Player)pRef; // typed
        if (player.TakeDamage((float)Damage, GlobalPosition))
        {
            // 致死一击弹体高亮残留
            if (player.IsDead())
            {
                LingerFatal();
            }
            else
            {
                DespawnInternal();
            }
        }
    }

    /// <summary>致死弹 0.5s 高亮残留（停位移/关碰撞/红闪高亮，一次性 Timer 到期回收）。</summary>
    private void LingerFatal(float duration = 0.5f)
    {
        SetPhysicsProcess(false);
        // 本方法经 OnAreaExited（信号分发中）到达：引擎锁定本 Area，直写 monitoring 被拒且不生效，必须延迟
        SetDeferred("monitoring", false);
        Modulate = new Color(2.0f, 0.7f, 0.7f);
        _sprite ??= GetNodeOrNull<Sprite2D>("Sprite2D");
        if (_sprite != null)
        {
            _sprite.SelfModulate = new Color(1.2f, 0.35f, 0.35f);
        }

        var t = new Godot.Timer { OneShot = true, WaitTime = duration };
        t.Timeout += () =>
        {
            DespawnInternal();
            t.QueueFree();
        };
        AddChild(t);
        t.Start();
    }

    private void DespawnInternal()
    {
        if (_pool != null && GodotObject.IsInstanceValid(_pool))
        {
            _pool!.Release(this);
        }
        else
        {
            QueueFree();
        }
    }

    private void ApplyFaction()
    {
        Rotation = Direction.Angle();
        // 重置外观（敌机/Boss 激光长弹、母舰弹的自定义外观）
        Scale = Vector2.One;
        Modulate = Colors.White;
        EnsureTextures(); // 共享图集惰性生成（缓存于 GameState 实例字段，首次调用）
        _sprite ??= GetNodeOrNull<Sprite2D>("Sprite2D");
        if (_sprite == null)
        {
            return;
        }

        _sprite.Texture = IsPlayerBullet ? GameState.Instance.BulletPlayerTex : GameState.Instance.BulletEnemyTex;
        _sprite.Scale = Vector2.One * (IsPlayerBullet ? VisualScale : EnemyVisualScale);
        // self_modulate 染色残留复位为白（laser 黄/Boss 重弹橙/致死高亮红）
        _sprite.SelfModulate = Colors.White;
        if (HasMeta(MetaBulletType))
        {
            RemoveMeta(MetaBulletType);
        }

        if (IsPlayerBullet)
        {
            CollisionLayer = 2; // 第 2 层：player_bullet
            CollisionMask = 4; // 命中第 3 层：enemy
        }
        else
        {
            CollisionLayer = 8; // 第 4 层：enemy_bullet
            CollisionMask = 1; // 命中第 1 层：player
        }

        // 敌弹注册表维护（幂等）——activate/_ready/reflect 均经此路径
        if (IsPlayerBullet)
        {
            GameState.Instance.UnregisterEnemyBullet(this);
        }
        else
        {
            GameState.Instance.RegisterEnemyBullet(this);
        }
    }

    /// <summary>共享纹理惰性生成（缓存于 GameState 实例字段，全实例共用；首次调用光栅化一次）。
    /// 弹体之下预铺椭圆辉光（横向拉长的能量拖尾感）；仅改共享贴图，碰撞半径/视觉缩放不受影响。</summary>
    private static void EnsureTextures()
    {
        var gs = GameState.Instance;
        if (gs.BulletPlayerTex != null)
        {
            return;
        }

        // 战术琥珀：玩家弹 = 白热芯 + 琥珀晕（正面辨识）；敌弹 = 更锐利的红/品红（不与琥珀 UI 混同）
        gs.BulletPlayerTex = _stampTexture(ArrowBody, new Color(1.0f, 0.70f, 0.24f), ArrowCore, Colors.White,
            new Color(1.0f, 0.74f, 0.34f, 0.42f));
        gs.BulletEnemyTex = _stampTexture(ArrowBody, new Color(1.0f, 0.28f, 0.34f), System.Array.Empty<Vector2>(), Colors.Transparent,
            new Color(1.0f, 0.24f, 0.42f, 0.42f));
    }

    /// <summary>把多边形（弹体 + 可选白芯）光栅化进共享纹理（像素级平移对齐，无缩放损失）；
    /// glowColor.A &gt; 0 时先铺椭圆径向辉光（横向拖尾感，pow 衰减）。</summary>
    private static ImageTexture _stampTexture(Vector2[] body, Color bodyColor, Vector2[] core, Color coreColor, Color? glowColor = null)
    {
        var img = Image.CreateEmpty(TexSize.X, TexSize.Y, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        if (glowColor is { A: > 0.0f } glow)
        {
            _addGlow(img, glow);
        }

        _fillPolygon(img, body, bodyColor);
        if (core.Length > 0)
        {
            _fillPolygon(img, core, coreColor);
        }

        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>椭圆径向辉光：中心取弹体几何中心，横轴覆盖整张弹贴图（拖尾）、纵轴压扁；
    /// 逐像素 pow 衰减叠加（乘算混合进已有像素，仅构建期执行一次）。</summary>
    private static void _addGlow(Image img, Color glow)
    {
        var cx = TexOffset.X + 1.0f;
        var cy = TexOffset.Y;
        var rx = 11.5f;
        var ry = 3.8f;
        for (var y = 0; y < img.GetHeight(); y++)
        {
            for (var x = 0; x < img.GetWidth(); x++)
            {
                var dx = (x + 0.5f - cx) / rx;
                var dy = (y + 0.5f - cy) / ry;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d >= 1.0f)
                {
                    continue;
                }

                var a = glow.A * Mathf.Pow(1.0f - d, 2.0f);
                var dst = img.GetPixel(x, y);
                var outA = a + dst.A * (1.0f - a);
                if (outA <= 0.0f)
                {
                    continue;
                }

                var r = (glow.R * a + dst.R * dst.A * (1.0f - a)) / outA;
                var g = (glow.G * a + dst.G * dst.A * (1.0f - a)) / outA;
                var b = (glow.B * a + dst.B * dst.A * (1.0f - a)) / outA;
                img.SetPixel(x, y, new Color(r, g, b, outA));
            }
        }
    }

    /// <summary>凸多边形扫描线填充（三角扇分解：以第一个点为公共顶点，全部顶点平移纹理偏移）。</summary>
    private static void _fillPolygon(Image img, Vector2[] pts, Color color)
    {
        for (var i = 1; i < pts.Length - 1; i++)
        {
            _fillTriangle(img, pts[0] + TexOffset, pts[i] + TexOffset, pts[i + 1] + TexOffset, color);
        }
    }

    /// <summary>三角形扫描线填充（按 y 排序，逐行求两条边交点的 x 区间）。</summary>
    private static void _fillTriangle(Image img, Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        var pts = new[] { a, b, c };
        System.Array.Sort(pts, (p, q) => p.Y.CompareTo(q.Y));
        var y0 = Mathf.Max(0, (int)Mathf.Ceil(pts[0].Y));
        var y1 = Mathf.Min(img.GetHeight() - 1, (int)Mathf.Floor(pts[2].Y));
        for (var y = y0; y <= y1; y++)
        {
            var fy = (float)y + 0.5f;
            var xA = _edgeIntersect(pts[0], pts[2], fy);
            var xB = fy < pts[1].Y ? _edgeIntersect(pts[0], pts[1], fy) : _edgeIntersect(pts[1], pts[2], fy);
            var xa = Mathf.Max(0, (int)Mathf.Ceil(Mathf.Min(xA, xB)));
            var xb = Mathf.Min(img.GetWidth() - 1, (int)Mathf.Floor(Mathf.Max(xA, xB)));
            for (var x = xa; x <= xb; x++)
            {
                img.SetPixel(x, y, color);
            }
        }
    }

    /// <summary>线段在指定 y 处的 x（水平边退化返回起点 x）。</summary>
    private static float _edgeIntersect(Vector2 p, Vector2 q, float y)
    {
        if (Mathf.Abs(q.Y - p.Y) < 1.0e-6f)
        {
            return p.X;
        }

        return p.X + (y - p.Y) * (q.X - p.X) / (q.Y - p.Y);
    }
}
