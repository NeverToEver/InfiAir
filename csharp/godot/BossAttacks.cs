using System.Collections.Generic;
using Godot;

namespace InfiAir;

/// <summary>
/// Boss 攻击状态机（A3 拆分，docs/AUDIT_VAULT.md A3；2026-08-08 全量迁移，自 scripts/boss_attacks.gd）。
/// 承载持续型攻击（狙击 telegraph / 蓄力重炮 / 冲刺掠过 / 编队齐射）的时序状态与轮询；
/// 一次性攻击（fan/homing/cross/bullet_wall）在 execute 内直接委托 BossFire。
/// 配置字段经 boss 动态访问（无类型参数），弹幕发射经注入的 BossFire，避免跨类私有访问（A1 约束）。
/// 迁移说明：RefCounted 纯逻辑组件 → 纯 C# 类（无信号/导出）；BossFire 已随本批迁 C#
/// （主代理并行，csharp/godot/BossFire.cs），经 GodotObject.Call PascalCase 动态派发；
/// boss 为 C# Boss（主代理并行迁移），常量/属性经 Get 原标识符名、方法经 Call PascalCase；
/// Enemy 迁 C# 后直接 Enemy.SinFast/CosFast。
/// </summary>
public partial class BossAttacks : RefCounted
{
    // ---- 对齐 Boss.SweepState（enum { NONE, AIM, DASH, RETURN }） ----
    public const int SweepNone = 0;
    public const int SweepAim = 1;
    public const int SweepDash = 2;
    public const int SweepReturn = 3;

    // B 梯队（fair plan §8）：每攻击独特 tell——起手音效变体 + 视觉前兆冲击环。
    // 玩家凭音效/闪光区分「来的是什么」；音效复用现有资源变体（缺专属资产，登记后续音频项）。
    private static readonly AudioStream TellFireA = GD.Load<AudioStream>("res://assets/audio/bullet_fire.wav");
    private static readonly AudioStream TellFireB = GD.Load<AudioStream>("res://assets/audio/bullet_fire_b.wav");
    private static readonly AudioStream TellFireC = GD.Load<AudioStream>("res://assets/audio/bullet_fire_c.wav");
    private static readonly AudioStream TellDash = GD.Load<AudioStream>("res://assets/audio/dash.wav");
    private static readonly AudioStream TellExplosion = GD.Load<AudioStream>("res://assets/audio/explosion.wav");

    /// <summary>attack id → tell 配置（sfx 变体/音高/视觉环色）；缺失键 = 该攻击无 tell（新攻击须补配）。</summary>
    private sealed class TellInfo
    {
        public AudioStream? Sfx;
        public float Pitch;
        public Color Color;
    }

    private static readonly Dictionary<StringName, TellInfo> AttackTells = new()
    {
        [new StringName("fan5")] = new TellInfo { Sfx = TellFireA, Pitch = 1.0f, Color = new Color(1.0f, 0.6f, 0.2f, 0.55f) },
        [new StringName("fan7")] = new TellInfo { Sfx = TellFireA, Pitch = 1.15f, Color = new Color(1.0f, 0.6f, 0.2f, 0.55f) },
        [new StringName("homing")] = new TellInfo { Sfx = TellFireB, Pitch = 1.0f, Color = new Color(1.0f, 0.3f, 0.3f, 0.55f) },
        [new StringName("sniper3")] = new TellInfo { Sfx = TellFireC, Pitch = 1.0f, Color = new Color(0.95f, 0.95f, 1.0f, 0.6f) },
        [new StringName("cross")] = new TellInfo { Sfx = TellFireA, Pitch = 1.25f, Color = new Color(0.8f, 0.4f, 1.0f, 0.55f) },
        [new StringName("charged_cannon")] = new TellInfo { Sfx = TellDash, Pitch = 0.8f, Color = new Color(1.0f, 0.85f, 0.3f, 0.6f) },
        [new StringName("dash_sweep")] = new TellInfo { Sfx = TellExplosion, Pitch = 0.7f, Color = new Color(0.4f, 0.9f, 1.0f, 0.55f) },
        [new StringName("minion_volley")] = new TellInfo { Sfx = TellFireC, Pitch = 0.8f, Color = new Color(0.5f, 1.0f, 0.5f, 0.55f) },
        [new StringName("bullet_wall")] = new TellInfo { Sfx = TellFireB, Pitch = 1.2f, Color = new Color(0.4f, 0.6f, 1.0f, 0.55f) },
        [new StringName("ring_burst")] = new TellInfo { Sfx = TellFireA, Pitch = 1.4f, Color = new Color(1.0f, 0.3f, 0.9f, 0.55f) },
    };
    /// <summary>攻击 tell 表公开访问（boss_registry_test 校验用）。</summary>
    public static Godot.Collections.Dictionary GetAttackTells()
    {
        var result = new Godot.Collections.Dictionary();
        foreach (var kv in AttackTells)
        {
            result[kv.Key] = true;
        }

        return result;
    }


    // ---- 注入：弹幕发射器（Boss._ready 经 configure 传入）与机体缩放 ----
    private GodotObject _fire = null!;

    /// <summary>机体缩放（configure 注入；charge_glow 默认辉光半径 / 拖弹偏移共用）。</summary>
    public float WorldScale { get; set; } = 1.0f;

    /// <summary>难度分档弹数增量（Boss._apply_difficulty_scaling 写入，供 fan/homing 取用）。</summary>
    public int FanDelta { get; set; }

    /// <summary>难度分档弹数增量（同 FanDelta，供 homing 取用）。</summary>
    public int HomingDelta { get; set; }

    /// <summary>4 型环弹难度分档绝对值（counts.ring_burst = [10,12,14]，Q01）。</summary>
    public int RingDelta { get; set; }

    // 狙击 telegraph（游击型）
    private Line2D? _aimLine;
    private float _sniperAimElapsed = -1.0f; // <0 = 无进行中的 telegraph
    private Vector2 _sniperDir = Vector2.Down;
    private int _burstLeft;
    private float _burstTimer;
    private Vector2 _burstDir = Vector2.Zero; // 非零 = telegraph 锁定方向的固定方向爆发
    // 蓄力重炮（一型 P2）
    private float _cannonElapsed = -1.0f; // <0 = 无进行中的蓄力
    private int _cannonShotsLeft;
    private float _cannonTimer;
    private bool _cannonFlashed;
    // 冲刺掠过（二型 P2）
    private int _sweepState = SweepNone;
    private float _sweepTimer;
    private float _sweepDir = 1.0f;
    private Vector2 _sweepOrigin = Vector2.Zero;
    private Vector2 _sweepReturnTarget = Vector2.Zero;
    private float _sweepDashY; // 冲刺横穿高度（AIM 开始时玩家高度快照，与预警线同语义）
    private readonly Godot.Collections.Array<float> _sweepDropX = new();
    private Line2D? _sweepLine;
    // 编队齐射（三型 P2）
    private readonly Godot.Collections.Array _volleyMinions = new();
    private float _volleyTimer;

    // A3 收敛：攻击处理器注册表（attack id → 处理器，_init 装配）。
    // 新增攻击只需注册一行 + 模式表加 id，不再改 execute 分发本身（O 原则达成）。
    private readonly Godot.Collections.Dictionary<StringName, Callable> _attackHandlers = new();


    // 热路径缓存：view_world_rect / player_ref 每物理帧一次动态调用（与 Enemy.cs 同款）。
    private static ulong _frame = ulong.MaxValue;
    private static Rect2 _frameView;
    private static Variant _framePlayer;

    private static Rect2 CachedView()
    {
        var f = Engine.GetPhysicsFrames();
        if (f != _frame)
        {
            _frame = f;
            _frameView = GameStateBridge.Call("view_world_rect").AsRect2();
            _framePlayer = GameStateBridge.Get("player_ref");
        }

        return _frameView;
    }

    private static Variant CachedPlayer() => _frame == Engine.GetPhysicsFrames() ? _framePlayer : GameStateBridge.Get("player_ref");

    public BossAttacks()
    {
        _attackHandlers[new StringName("fan5")] = Callable.From<Node2D>(HandleFan5);
        _attackHandlers[new StringName("fan7")] = Callable.From<Node2D>(HandleFan7);
        _attackHandlers[new StringName("homing")] = Callable.From<Node2D>(HandleHoming);
        _attackHandlers[new StringName("sniper3")] = Callable.From<Node2D>(HandleSniper3);
        _attackHandlers[new StringName("cross")] = Callable.From<Node2D>(HandleCross);
        _attackHandlers[new StringName("charged_cannon")] = Callable.From<Node2D>(HandleChargedCannon);
        _attackHandlers[new StringName("dash_sweep")] = Callable.From<Node2D>(HandleDashSweep);
        _attackHandlers[new StringName("minion_volley")] = Callable.From<Node2D>(HandleMinionVolley);
        _attackHandlers[new StringName("bullet_wall")] = Callable.From<Node2D>(HandleBulletWall);
        _attackHandlers[new StringName("ring_burst")] = Callable.From<Node2D>(HandleRingBurst);
    }

    /// <summary>注入发射器与机体缩放（Boss._ready 调用；模式循环重置回调在 Boss 侧）。</summary>
    public void Configure(GodotObject fire, float ws)
    {
        _fire = fire;
        WorldScale = ws;
    }

    /// <summary>面向玩家的方向（player 为空回退 Vector2.DOWN）。</summary>
    private static Vector2 PlayerDir(Node2D from)
    {
        var player = CachedPlayer();
        if (player.VariantType != Variant.Type.Nil)
        {
            var p = (Node2D)player;
            return (p.GlobalPosition - from.GlobalPosition).Normalized();
        }

        return Vector2.Down;
    }

    /// <summary>攻击分发：查表委托（原 10 分支 match；模式表只存 attack id）。</summary>
    public void Execute(StringName attack, Node2D boss)
    {
        if (_attackHandlers.ContainsKey(attack))
        {
            // B 梯队：起手 tell（音效变体 + 视觉前兆环），玩家可区分「来的是什么」
            PlayTell(attack, boss);
            _attackHandlers[attack].Call(boss);
        }
        else
        {
            GD.PushWarning($"[BOSS] 未知攻击 id: {attack}");
        }
    }

    /// <summary>起手 tell：音效（独特变体 + 音高）+ 低频视觉冲击环（起手一次性事件，直接实例化可接受）。</summary>
    private void PlayTell(StringName attack, Node2D boss)
    {
        if (!AttackTells.TryGetValue(attack, out var tell))
        {
            return;
        }

        GameStateBridge.Call("play_sfx", tell.Sfx!, -8.0, tell.Pitch);
        var ring = (Node2D)CinematicFx.Shockwave(
            new Godot.Collections.Dictionary
            {
                ["radius"] = 26.0,
                ["time"] = 0.22,
                ["color"] = tell.Color,
                ["core_color"] = tell.Color.Lightened(0.4f),
                ["width"] = 5.0,
            });
        ring.Position = boss.Position;
        boss.GetParent().AddChild(ring);
    }

    /// <summary>注册表完整性查询（A3 架构断言测试经公开接口访问）。</summary>
    public bool HasAttack(StringName id) => _attackHandlers.ContainsKey(id);

    /// <summary>全部已注册攻击 id（A3 架构断言测试经公开接口访问）。</summary>
    public Godot.Collections.Array AttackIds()
    {
        var ids = new Godot.Collections.Array();
        foreach (var id in _attackHandlers.Keys)
        {
            ids.Add(id);
        }

        return ids;
    }

    private void HandleFan5(Node2D boss)
    {
        _fire.Call(
            "Fire_fan", boss, Mathf.Max(3, 5 + FanDelta),
            (float)boss.Get("FAN_BULLET_SPEED").AsDouble(), (int)boss.Get("BULLET_DAMAGE_FAN").AsInt64());
    }

    private void HandleFan7(Node2D boss)
    {
        _fire.Call(
            "Fire_fan", boss, Mathf.Max(3, 7 + FanDelta),
            (float)boss.Get("FAN_BULLET_SPEED").AsDouble(), (int)boss.Get("BULLET_DAMAGE_FAN").AsInt64());
    }

    /// <summary>
    /// 4 型「月蚀」ring_burst（2026-08-04）：360° 全圆环弹（难度分档弹数绝对值，counts.ring_burst）。
    /// 2026-08-05 Q01：counts.ring_burst 是每档弹数绝对值（§5.6）——直接消费档值
    ///（原实现基准 12 上叠加增量 → easy 22/medium 24/hard 26 ≈ 2× 设计密度）。
    /// </summary>
    private void HandleRingBurst(Node2D boss)
    {
        _fire.Call(
            "Fire_ring", boss, Mathf.Max(6, RingDelta),
            (float)boss.Get("RING_BURST_SPEED").AsDouble(), (int)boss.Get("BULLET_DAMAGE_RING").AsInt64(), 0.0f);
    }

    private void HandleHoming(Node2D boss)
    {
        // 2026-08-03 审计：难度分档弹数生效（原 homing_delta 只被已删除的死代码 homing2 消费，
        // easy/hard 追踪弹数恒 1；现并入单发路径，多弹横向 80px 散开；medium 档恒单发与原行为一致）
        var count = Mathf.Max(1, 1 + HomingDelta);
        for (var i = 0; i < count; i++)
        {
            _fire.Call(
                "Fire_homing", boss,
                new Vector2(((float)i - (float)(count - 1) * 0.5f) * 80.0f, 100.0f),
                (float)boss.Get("HOMING_BULLET_SPEED").AsDouble(), (int)boss.Get("BULLET_DAMAGE_HOMING").AsInt64());
        }
    }

    private void HandleSniper3(Node2D boss) => StartSniperVolley(boss);

    private void HandleCross(Node2D boss)
    {
        _fire.Call(
            "Fire_cross", boss,
            (float)boss.Get("CROSS_BULLET_SPEED").AsDouble(), (int)boss.Get("BULLET_DAMAGE_CROSS").AsInt64());
    }

    private void HandleChargedCannon(Node2D boss) => StartChargedCannon(boss);

    private void HandleDashSweep(Node2D boss) => StartDashSweep(boss);

    private void HandleMinionVolley(Node2D boss) => StartMinionVolley(boss);

    private void HandleBulletWall(Node2D boss)
    {
        _fire.Call(
            "Fire_bullet_wall", boss,
            (int)boss.Get("WALL_COUNT").AsInt64(),
            (float)boss.Get("WALL_BULLET_SPEED").AsDouble(),
            (int)boss.Get("WALL_DAMAGE").AsInt64(),
            (float)boss.Get("WALL_ARC_DEG").AsDouble());
    }

    /// <summary>
    /// 持续型攻击轮询（sniper telegraph / 3 连发 / 蓄力重炮 / 编队齐射 / 冲刺掠过），Boss._physics_process 调用。
    /// </summary>
    public void Update(float delta, Node2D boss)
    {
        // 狙击 telegraph：瞄准线前 0.2s 微跟踪玩家后固定，0.35s 到点沿线出弹（§4.2/§5.2）
        if (_sniperAimElapsed >= 0.0f)
        {
            _sniperAimElapsed += delta;
            if (_sniperAimElapsed <= (float)boss.Get("SNIPER_TRACK_TIME").AsDouble())
            {
                _sniperDir = PlayerDir(boss);
                if (_aimLine != null)
                {
                    // C23：创建时已 add_point 预置 2 点，set_point_position 原地写（points[i]= 值语义不生效）
                    _aimLine.SetPointPosition(0, _sniperDir * (float)boss.Get("MUZZLE_OFFSET").AsDouble());
                    _aimLine.SetPointPosition(1, _sniperDir * 1200.0f);
                }
            }

            if (_aimLine != null)
            {
                _aimLine.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.18f + 0.18f * Mathf.Abs(Enemy.SinFast(_sniperAimElapsed * 25.0f)));
            }

            if (_sniperAimElapsed >= (float)boss.Get("SNIPER_AIM_TIME").AsDouble())
            {
                CancelAimLineInternal();
                _sniperAimElapsed = -1.0f;
                _burstLeft = 3;
                _burstTimer = 0.0f;
                _burstDir = _sniperDir;
            }
        }

        // 游击型 3 连发狙击（telegraph 锁定时沿固定方向，否则自机狙）
        if (_burstLeft > 0)
        {
            _burstTimer -= delta;
            if (_burstTimer <= 0.0f)
            {
                _burstTimer = (float)boss.Get("SNIPER_BURST_INTERVAL").AsDouble(); // Q30：三连发间隔入库（原硬编码 0.12）
                _burstLeft -= 1;
                _fire.Call(
                    "Fire_sniper", boss, _burstDir,
                    (float)boss.Get("SNIPER_BULLET_SPEED").AsDouble(), (int)boss.Get("BULLET_DAMAGE_SNIPER").AsInt64());
                if (_burstLeft == 0)
                {
                    _burstDir = Vector2.Zero;
                }
            }
        }

        // 蓄力重炮（一型 P2）：蓄力 0.6s 后 3 连重弹（每发 0.15s 短蓄力闪光）
        if (_cannonElapsed >= 0.0f)
        {
            _cannonElapsed += delta;
            if (_cannonElapsed >= (float)boss.Get("CANNON_CHARGE").AsDouble())
            {
                _cannonElapsed = -1.0f;
                _cannonShotsLeft = (int)boss.Get("CANNON_SHOTS").AsInt64();
                _cannonTimer = 0.0f;
                _cannonFlashed = true; // 首发的 telegraph 即 0.6s 蓄力辉光
            }
        }

        if (_cannonShotsLeft > 0)
        {
            _cannonTimer -= delta;
            if (!_cannonFlashed && _cannonTimer <= (float)boss.Get("CANNON_FLASH").AsDouble())
            {
                _cannonFlashed = true;
                ChargeGlow(boss, (float)boss.Get("CANNON_FLASH").AsDouble(), 90.0f * WorldScale, new Color(1.0f, 0.7f, 0.3f, 0.6f));
            }

            if (_cannonTimer <= 0.0f)
            {
                _cannonTimer = (float)boss.Get("CANNON_INTERVAL").AsDouble();
                _cannonShotsLeft -= 1;
                _cannonFlashed = false;
                _fire.Call(
                    "Fire_heavy", boss, PlayerDir(boss),
                    (float)boss.Get("CANNON_BULLET_SPEED").AsDouble(), (int)boss.Get("CANNON_DAMAGE").AsInt64());
            }
        }

        // 编队齐射（三型 P2）：横队小怪 0.8s 后齐射一轮自机狙，随后恢复正常 AI
        if (_volleyTimer > 0.0f)
        {
            _volleyTimer -= delta;
            if (_volleyTimer <= 0.0f)
            {
                MinionVolleyFire(boss, _volleyMinions);
                _volleyMinions.Clear();
            }
        }

        UpdateSweep(delta, boss);
    }

    private static readonly Color DefaultGlowColor = new(1.0f, 0.55f, 0.3f, 0.55f);

    /// <summary>蓄力辉光：叠加态圆点 scale/alpha tween，duration 后自毁（过场 _glow 配方）。</summary>
    public Node2D ChargeGlow(Node2D boss, float duration) => ChargeGlow(boss, duration, -1.0f, DefaultGlowColor);

    /// <summary>蓄力辉光重载（指定半径；颜色取默认）。</summary>
    public Node2D ChargeGlow(Node2D boss, float duration, float radius) => ChargeGlow(boss, duration, radius, DefaultGlowColor);

    /// <summary>蓄力辉光重载（指定半径与颜色）。</summary>
    public Node2D ChargeGlow(Node2D boss, float duration, float radius, Color color)
    {
        if (radius < 0.0f)
        {
            radius = 70.0f * WorldScale; // 默认辉光半径设计值 × 全局缩放
        }

        var dot = new GlowDot { Radius = radius, DotColor = color };
        var mat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        dot.Material = mat;
        dot.Scale = Vector2.One * 0.3f;
        dot.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
        boss.AddChild(dot);
        var tween = dot.CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(dot, "scale", Vector2.One, duration * 0.6f);
        tween.TweenProperty(dot, "modulate:a", 1.0f, duration * 0.4f);
        tween.Chain().TweenProperty(dot, "modulate:a", 0.0f, duration * 0.4f);
        tween.TweenCallback(Callable.From(dot.QueueFree));
        return dot;
    }

    /// <summary>瞄准线：α0.3 闪烁细线（闪烁由 update 驱动），出弹/中断即毁。</summary>
    private Line2D MakeAimLineInternal(Node2D boss, Vector2 dir, float length, Color color)
    {
        var line = new Line2D();
        line.Width = 2.0f;
        line.DefaultColor = color;
        line.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.3f);
        line.AddPoint(dir * (float)boss.Get("MUZZLE_OFFSET").AsDouble());
        line.AddPoint(dir * length);
        boss.AddChild(line);
        return line;
    }

    private void CancelAimLineInternal()
    {
        if (_aimLine != null)
        {
            _aimLine.QueueFree();
            _aimLine = null;
        }
    }

    /// <summary>狙击 3 连发 telegraph 起手：瞄准线随玩家微跟踪 0.2s 后固定，0.35s 到点沿线出弹。</summary>
    private void StartSniperVolley(Node2D boss)
    {
        if (_sniperAimElapsed >= 0.0f)
        {
            return; // 已有进行中的 telegraph（间隔短于 telegraph 时不叠加）
        }

        _sniperAimElapsed = 0.0f;
        _sniperDir = PlayerDir(boss);
        _aimLine = MakeAimLineInternal(boss, _sniperDir, 1200.0f, new Color(1.0f, 0.35f, 0.3f, 0.9f));
    }

    /// <summary>蓄力重炮（一型 P2）：0.6s 蓄力辉光起手，连发由 update 驱动。</summary>
    private void StartChargedCannon(Node2D boss)
    {
        if (_cannonElapsed >= 0.0f || _cannonShotsLeft > 0)
        {
            return;
        }

        _cannonElapsed = 0.0f;
        ChargeGlow(boss, (float)boss.Get("CANNON_CHARGE").AsDouble());
    }

    /// <summary>冲刺掠过（二型 P2）：0.5s 水平瞄准线（预警横穿玩家当前高度）起手。</summary>
    private void StartDashSweep(Node2D boss)
    {
        if (_sweepState != SweepNone)
        {
            return;
        }

        _sweepState = SweepAim;
        _sweepTimer = (float)boss.Get("SWEEP_AIM").AsDouble();
        // C14：默认方向/高度取可见世界中心，不写死 960/300
        var view = CachedView();
        var playerX = view.GetCenter().X;
        var dy = view.GetCenter().Y - boss.Position.Y;
        var player = CachedPlayer();
        if (player.VariantType != Variant.Type.Nil)
        {
            var p = (Node2D)player;
            playerX = p.GlobalPosition.X;
            dy = p.GlobalPosition.Y - boss.Position.Y;
        }

        // 横穿高度 = 预警线所在高度（玩家 AIM 开始时的 y 快照，DASH 阶段据此落位）
        _sweepDashY = boss.Position.Y + dy;
        _sweepDir = Mathf.Sign(playerX - boss.Position.X);
        if (_sweepDir == 0.0f)
        {
            _sweepDir = 1.0f;
        }

        _sweepOrigin = boss.Position;
        CancelSweepLine();
        _sweepLine = new Line2D();
        _sweepLine.Width = 2.0f;
        _sweepLine.DefaultColor = new Color(1.0f, 0.35f, 0.3f, 0.9f);
        _sweepLine.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.3f);
        // 预警线跨度覆盖可见区全宽（zoom 加宽也不露边）
        var span = view.Size.X * 0.6f;
        _sweepLine.AddPoint(new Vector2(-span, dy));
        _sweepLine.AddPoint(new Vector2(span, dy));
        boss.AddChild(_sweepLine);
    }

    /// <summary>
    /// 冲刺掠过驱动：AIM（瞄准线闪烁）→ DASH（高速横穿 + 等距拖 3 枚减速弹）
    /// → RETURN（smoothstep 飞回巡航位，复用狂暴 RETURN 插值模式）。
    /// </summary>
    private void UpdateSweep(float delta, Node2D boss)
    {
        switch (_sweepState)
        {
            case SweepAim:
                _sweepTimer -= delta;
                if (_sweepLine != null)
                {
                    _sweepLine.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.18f + 0.18f * Mathf.Abs(Enemy.SinFast(_sweepTimer * 25.0f)));
                }

                if (_sweepTimer <= 0.0f)
                {
                    CancelSweepLine();
                    _sweepState = SweepDash;
                    // 横穿落位到玩家高度（AIM 开始时快照，与预警线同语义）；RETURN 复用锚线回位逻辑
                    boss.Position = new Vector2(boss.Position.X, _sweepDashY);
                    // 拖弹点：横穿路径 1/4、1/2、3/4 处
                    var bounds = boss.Call("StrafeRange").AsVector2();
                    var endX = _sweepDir > 0.0f ? bounds.Y : bounds.X;
                    _sweepDropX.Clear();
                    var dropCount = (int)boss.Get("SWEEP_DROP_COUNT").AsInt64();
                    for (var i = 0; i < dropCount; i++)
                    {
                        _sweepDropX.Add(Mathf.Lerp(boss.Position.X, endX, (float)(i + 1) / (float)(dropCount + 1)));
                    }
                }

                break;
            case SweepDash:
                var sweepSpeed = (float)boss.Get("SWEEP_SPEED").AsDouble();
                var slowFactor = (float)boss.Call("SlowFactor").AsDouble();
                boss.Position = new Vector2(
                    boss.Position.X + _sweepDir * sweepSpeed * slowFactor * delta, boss.Position.Y);
                while (_sweepDropX.Count > 0)
                {
                    var dropX = _sweepDropX[0];
                    if ((_sweepDir > 0.0f && boss.Position.X >= dropX) || (_sweepDir < 0.0f && boss.Position.X <= dropX))
                    {
                        _sweepDropX.RemoveAt(0);
                        var b = FireFromPool(
                            Vector2.Down,
                            (float)boss.Get("SWEEP_DROP_SPEED").AsDouble(),
                            (int)boss.Get("SWEEP_DROP_DAMAGE").AsInt64());
                        if (b == null)
                        {
                            break; // P2-3：同屏敌弹硬上限——跳出本轮撒弹（cap 持续期剩余 drop 下轮重试，防死循环）
                        }

                        b.Set("position", boss.Position + new Vector2(0.0f, 60.0f) * WorldScale);
                    }
                    else
                    {
                        break;
                    }
                }

                var dashBounds = boss.Call("StrafeRange").AsVector2();
                if ((_sweepDir > 0.0f && boss.Position.X >= dashBounds.Y) || (_sweepDir < 0.0f && boss.Position.X <= dashBounds.X))
                {
                    boss.Position = new Vector2(Mathf.Clamp(boss.Position.X, dashBounds.X, dashBounds.Y), boss.Position.Y);
                    _sweepState = SweepReturn;
                    _sweepTimer = (float)boss.Get("SWEEP_RETURN_DURATION").AsDouble();
                    _sweepOrigin = boss.Position;
                    // C14：返回目标 x 取可见世界中心，不写死 960（zoom 加宽时仍居中）
                    _sweepReturnTarget = new Vector2(
                        Mathf.Clamp(CachedView().GetCenter().X, dashBounds.X, dashBounds.Y), (float)boss.Call("FightAnchorY").AsDouble());
                }

                break;
            case SweepReturn:
                _sweepTimer -= delta;
                var t = Mathf.Clamp(1.0f - _sweepTimer / (float)boss.Get("SWEEP_RETURN_DURATION").AsDouble(), 0.0f, 1.0f);
                var eased = t * t * (3.0f - 2.0f * t);
                boss.Position = _sweepOrigin.Lerp(_sweepReturnTarget, eased);
                if (_sweepTimer <= 0.0f)
                {
                    _sweepState = SweepNone;
                    boss.Call("ResetFireTimer");
                }

                break;
        }
    }

    private void CancelSweepLine()
    {
        if (_sweepLine != null)
        {
            _sweepLine.QueueFree();
            _sweepLine = null;
        }
    }

    /// <summary>序列中断清理：瞄准线/拖弹点/状态复位（位置由调用方接管）。</summary>
    private void CancelSweep()
    {
        CancelSweepLine();
        _sweepState = SweepNone;
        _sweepDropX.Clear();
    }

    /// <summary>编队齐射（三型 P2）：召唤 VOLLEY_COUNT 小怪列横队（meta 标记），0.8s 后齐射由 update 驱动。</summary>
    private void StartMinionVolley(Node2D boss)
    {
        if (_volleyTimer > 0.0f)
        {
            return; // R07：进行中守卫（L 系列防御缺口登记遗留）——待发期间重复触发清空重召
        }

        _volleyMinions.Clear();
        var volleyCount = (int)boss.Get("VOLLEY_COUNT").AsInt64();
        for (var i = 0; i < volleyCount; i++)
        {
            var e = (Enemy?)boss.Call(
                "SpawnMinionAt",
                boss.Position + new Vector2(((float)i - (float)(volleyCount - 1) * 0.5f) * 100.0f, 110.0f) * WorldScale);
            if (e != null)
            {
                e.SetMeta("hive_volley", true);
                _volleyMinions.Add(e);
            }
        }

        _volleyTimer = (float)boss.Get("VOLLEY_DELAY").AsDouble();
    }

    /// <summary>齐射一轮自机狙（普通敌弹口径；P2 编队与狂暴倾巢收尾共用）。</summary>
    public void MinionVolleyFire(Node2D boss, Godot.Collections.Array minions)
    {
        var playerV = CachedPlayer();
        if (playerV.VariantType == Variant.Type.Nil)
        {
            return;
        }

        var player = (Node2D)playerV;
        var volleySpeed = (float)boss.Get("VOLLEY_BULLET_SPEED").AsDouble();
        var volleyDamage = (int)boss.Get("VOLLEY_BULLET_DAMAGE").AsInt64();
        foreach (var raw in minions)
        {
            if (raw.VariantType == Variant.Type.Nil)
            {
                continue;
            }

            var e = (Node2D)raw;
            if (!GodotObject.IsInstanceValid(e) || e is not Enemy enemy || !enemy.IsActive())
            {
                continue;
            }

            var dir = (player.GlobalPosition - e.GlobalPosition).Normalized();
            // H10（健壮性审核）：玩家与僚机重合时零向量回退（防静止弹，G026 同族）
            if (dir == Vector2.Zero)
            {
                dir = Vector2.Down;
            }

            var b = FireFromPool(dir, volleySpeed, volleyDamage);
            if (b == null)
            {
                continue; // P2-3：同屏敌弹硬上限，跳过该僚机本轮齐射
            }

            b.Set("position", e.Position + dir * 40.0f * WorldScale);
        }
    }

    /// <summary>冲刺掠过进行中（Boss._physics_process 据此决定移动接管）。</summary>
    public bool IsSweepActive() => _sweepState != SweepNone;

    /// <summary>状态查询（测试/诊断白盒断言经公开接口，A3）。</summary>
    public int SweepState() => _sweepState;

    /// <summary>蓄力剩余计时查询（测试/诊断白盒断言经公开接口，A3）。</summary>
    public float CannonElapsed() => _cannonElapsed;

    /// <summary>狙击瞄准线引用查询（测试/诊断白盒断言经公开接口，A3）。</summary>
    public Line2D? AimLine() => _aimLine;

    /// <summary>冲刺预警线引用查询（测试/诊断白盒断言经公开接口，A3）。</summary>
    public Line2D? SweepLine() => _sweepLine;

    /// <summary>单独取消瞄准线（狂暴 ACTIVE 各型复用；cancel_all 用于整体中断）。</summary>
    public void CancelAimLine() => CancelAimLineInternal();

    /// <summary>创建瞄准线（狂暴 ACTIVE 2 型「猎杀环绕」复用）。</summary>
    public Line2D MakeAimLine(Node2D boss, Vector2 dir, float length)
        => MakeAimLineInternal(boss, dir, length, new Color(1.0f, 0.35f, 0.3f, 0.9f));

    /// <summary>常规攻击全部中断清理（Boss._enter_phase/_enrage/_abort 调用）。</summary>
    public void CancelAll()
    {
        CancelAimLineInternal();
        _sniperAimElapsed = -1.0f;
        _burstLeft = 0;
        _burstDir = Vector2.Zero;
        CancelSweep();
        _cannonElapsed = -1.0f;
        _cannonShotsLeft = 0;
        _volleyTimer = 0.0f;
        _volleyMinions.Clear();
    }

    /// <summary>敌弹池发射（P2-3：同屏敌弹硬上限——池满返回 null，调用方按语义跳过）。</summary>
    private GodotObject? FireFromPool(Vector2 dir, float speed, int damage)
    {
        var pool = (GodotObject?)GameStateBridge.Get("bullet_pool");
        if (pool == null)
        {
            return null;
        }

        var b = pool.Call("Fire", dir, speed, damage, false);
        return b.VariantType == Variant.Type.Nil ? null : (GodotObject)b;
    }

    // ---------------- GDScript 鸭子调用兼容桥（M 批次过渡，M7 删除） ----------------
    // 原公开 var/方法的 snake_case 别名（C# 内部调用一律 PascalCase；M7 全量迁移后删除本段）。

    public float world_scale { get => WorldScale; set => WorldScale = value; }

    public int fan_delta { get => FanDelta; set => FanDelta = value; }

    public int homing_delta { get => HomingDelta; set => HomingDelta = value; }

    public int ring_delta { get => RingDelta; set => RingDelta = value; }

    public void configure(GodotObject fire, float ws) => Configure(fire, ws);

    public void execute(StringName attack, Node2D boss) => Execute(attack, boss);

    public void update(float delta, Node2D boss) => Update(delta, boss);

    public bool has_attack(StringName id) => HasAttack(id);

    public Godot.Collections.Array attack_ids() => AttackIds();

    public Node2D charge_glow(Node2D boss, float duration) => ChargeGlow(boss, duration);

    public Node2D charge_glow(Node2D boss, float duration, float radius) => ChargeGlow(boss, duration, radius);

    public Node2D charge_glow(Node2D boss, float duration, float radius, Color color) => ChargeGlow(boss, duration, radius, color);

    public bool is_sweep_active() => IsSweepActive();

    public int sweep_state() => SweepState();

    public float cannon_elapsed() => CannonElapsed();

    public Line2D? aim_line() => AimLine();

    public Line2D? sweep_line() => SweepLine();

    public void cancel_aim_line() => CancelAimLine();

    public Line2D make_aim_line(Node2D boss, Vector2 dir, float length) => MakeAimLine(boss, dir, length);

    public void cancel_all() => CancelAll();

    public void minion_volley_fire(Node2D boss, Godot.Collections.Array minions) => MinionVolleyFire(boss, minions);
}
