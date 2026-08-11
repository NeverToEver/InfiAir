using System.Collections.Generic;
using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 母舰补给平台（M4 全量迁移，2026-08-08 自 scripts/mothership.gd 迁移）：长按 H 蓄力召唤
/// （main 管理蓄力）→ 机库小窗演出（main 编排）→ 穿梭门打开，母舰 DESCEND 穿出减速
/// （缩放+ease-out 滑入停驻点）→ 到位释放减速带（冲击波短时减速敌人）并立即以加特林+导弹
/// 火力掩护，DOCKING 牵引回收玩家进保护舱（隐藏+关受击判定）→ RESUPPLY 补给 → STAY 驻留
/// 20s（弹匣 10 格，2s/格；≤4 格警告，警告 5s 后强制离舰；可长按 H 2s 提前离舰，冷却双机制
/// 折扣：时长 max(0.6, 1-0.4×剩余比例) + 进度预填 min(0.3, 0.5×剩余比例)）→ RELEASE 释放
/// （玩家出舱恢复显示）→ DEPART 加速离场。
/// 无敌窗口：演出/对接开始即无敌（锁输入），弹射结束才解除（释放后 2s 为重制版 QoL）。
/// STAY 期间 WASD 直接驾驶母舰，加特林双塔向上 80° 扫射 + 导弹齐射（≤5 目标）。
/// 母舰弹丸/导弹击毁只给 1/3 分（score_scale 标记，结算时向下取整）。
/// 语义保持：穿梭入场/驻留驾驶/弹匣警告/提前离舰折扣、火力升级档（阈值 5，伤害 ×1.5 /
/// 射速 ×0.8）、牵引光束附件组帧驱动零分配、注册表批量遍历（for_each_enemy 语义等价直迭代）。
/// M7 后调用方全部 C# typed（Enemy/Boss/Bullet/Player/BulletPool 类型化调用）；
/// 少量 snake_case 兼容桥因测试/动态派发调用方保留（桥段见文件底部）。
/// </summary>
public partial class Mothership : Area2D
{
    [Signal]
    public delegate void DepartedEventHandler(float cooldown);

    /// <summary>状态机：DESCEND 穿出 → DOCKING 牵引回收 → RESUPPLY 补给 → STAY 驻留 →
    /// RELEASE 出舱 → DEPART 离场（数值对齐原 GDScript 枚举，GDScript 调用方按序数比较）。</summary>
    public enum State { DESCEND, DOCKING, RESUPPLY, STAY, RELEASE, DEPART }

    // U07：静态 Godot 资源改实例字段（退出 segfault 实测教训，UITheme.cs:53）
    private readonly AudioStream _gatlingSfx = GD.Load<AudioStream>("res://assets/audio/bullet_fire_b.wav");

    // ---- 数值配置（_ready 从 balance.json 覆盖；与脚本默认值一致） ----
    /// <summary>G032：母舰贴图基线缩放设计值（tscn 同存 1.25，脚本幂等覆盖 ×ws）。</summary>
    public float ShipScale { get; private set; } = 1.25f;

    public float HoverY { get; private set; } = 270.0f;

    /// <summary>释放后保护（重制版 QoL，原作弹射后无保护）。</summary>
    public float ReleaseInvincible { get; private set; } = 2.0f;

    public float DockTweenTime { get; private set; } = 1.5f;
    public float DockOffsetY { get; private set; } = 140.0f;
    public float ResupplyDelay { get; private set; } = 0.5f;
    public float ReleaseTime { get; private set; } = 0.5f;
    public float ReleaseDrop { get; private set; } = 140.0f;

    /// <summary>弹匣：10 格 × 2s = 20s 驻留（实际被警告强制离舰截断为 ≈17s）。</summary>
    public int MagCells { get; private set; } = 10;

    public float MagCellTime { get; private set; } = 2.0f;
    public int MagWarnCells { get; private set; } = 4;

    /// <summary>警告后强制离舰延迟（对齐原作警告横幅 5s 播完强制弹射）。</summary>
    public float WarnEjectDelay { get; private set; } = 5.0f;

    public float EarlyHoldTime { get; private set; } = 2.0f;

    /// <summary>冷却时长折扣系数：mult = max(0.6, 1-0.4×剩余比例)。</summary>
    public float EarlyMaxDiscount { get; private set; } = 0.4f;

    /// <summary>冷却进度预填上限（仅提前离舰）。</summary>
    public float EarlyPrefillMax { get; private set; } = 0.3f;

    public float EarlyPrefillRatio { get; private set; } = 0.5f;
    public float DepartCooldown { get; private set; } = 60.0f;
    public float DepartStartSpeed { get; private set; }
    public float DepartAccel { get; private set; } = 540.0f;

    // 穿梭入场（effects.mothership_summon）
    public float WarpInTime { get; private set; } = 0.8f;

    /// <summary>穿出下落行程设计值（× world_scale 生效，运行时缓存为已缩放值）。</summary>
    public float WarpInDrop { get; private set; } = 260.0f;

    /// <summary>减速带冲击波扩散半径。</summary>
    public float SlowRadius { get; private set; } = 900.0f;

    /// <summary>敌人减速持续秒数。</summary>
    public float SlowDuration { get; private set; } = 2.0f;

    /// <summary>敌人位移速度乘区。</summary>
    public float SlowFactor { get; private set; } = 0.4f;

    /// <summary>扩散环视觉时长。</summary>
    public float SlowRingTime { get; private set; } = 0.5f;

    public float ShakeSlow { get; private set; } = 4.0f;

    // 母舰驾驶（STAY 期间 WASD，对齐原作 mother_ship_motion）
    public float DriveAccel { get; private set; } = 900.0f;
    public float DriveMaxSpeed { get; private set; } = 180.0f;
    public float DriveMarginX { get; private set; } = 130.0f;
    public float DriveMarginTop { get; private set; } = 80.0f;
    public float DriveMarginBottom { get; private set; } = 150.0f;

    // 加特林扫射（向上半球，对齐原作；双塔异周期异相位）
    public float GatlingInterval { get; private set; } = 0.1333f;
    public float GatlingBulletSpeed { get; private set; } = 1080.0f;
    public int GatlingDamage { get; private set; } = 8;
    public float GatlingScoreScale { get; private set; } = 1.0f / 3.0f;

    /// <summary>G030：导弹得分系数独立命名（与加特林同为 1/3，分别调参时不误改）。</summary>
    public float MissileScoreScale { get; private set; } = 1.0f / 3.0f;

    public float GatlingSweepLeftMin { get; private set; } = -60.0f;
    public float GatlingSweepLeftMax { get; private set; } = 20.0f;
    public float GatlingSweepRightMin { get; private set; } = -20.0f;
    public float GatlingSweepRightMax { get; private set; } = 60.0f;
    public float GatlingSweepLeftPeriod { get; private set; } = 1.6f;
    public float GatlingSweepRightPeriod { get; private set; } = 1.8f;
    public float GatlingSweepRightPhase { get; private set; } = 0.35f;

    // 导弹（对齐原作：0.3s/波、≤5 最近目标、发射定向直线弹 + 溅射）
    public float MissileInterval { get; private set; } = 0.3f;
    public int MissileDamage { get; private set; } = 80;
    public float MissileSpeed { get; private set; } = 600.0f;
    public int MissileTargetCount { get; private set; } = 5;
    public int MissileSplashDamage { get; private set; } = 20;
    public float MissileSplashRadius { get; private set; } = 80.0f;

    // ---- 对局状态 ----
    private State _state = State.DESCEND;
    private float _stateTimer;
    private float _departSpeed;
    private Player? _player; // M3c：Player 迁 C#，player_ref 恒为 Player
    private float _gatlingTimer;
    private float _sweepTime;
    private float _missileTimer;
    private Vector2 _driveVel = Vector2.Zero;
    private int _magCells = 10;
    private float _magCellTimer;
    private bool _magWarned;
    private float _warnEjectTimer;
    private float _earlyTimer;
    private Hud? _hudCache; // A5 收敛：HUD 延迟缓存（驻留期每帧刷新进度条用）
    private float _cooldownFactor = 1.0f;
    private float _prefill;
    private WarpGate? _warpGate; // U13：typed
    private Vector2 _warpFrom;
    private Vector2 _warpTarget;
    private float _ws = 1.0f; // world_scale 缓存（_ready 写入，帧内复用）
    // 演出附件（_ready 预建，帧内仅属性写，零分配）
    private Sprite2D _engineGlow = null!; // 引擎光晕（DESCEND 巨大→常态，DEPART 随加速增大）
    private Vector2 _engineGlowBase = Vector2.One; // 「常态」基准缩放
    private GpuParticles2D _descendTrail = null!; // 穿出期上冲气流尾迹
    private GpuParticles2D _departTrail = null!; // 离场下喷尾迹
    private Node2D _beamFx = null!; // 牵引光束附件容器（随 _beam.visible 同步显隐）
    private readonly List<Line2D> _beamRings = new(); // 捕获流环 ×3（自上而下循环）
    private float[] _beamRingU = new[] { 0.0f, 1.0f / 3.0f, 2.0f / 3.0f };
    private readonly List<Line2D> _beamEdges = new(); // 光束两侧描边（微闪）
    private GpuParticles2D _beamDust = null!; // 光束下端上升尘粒
    private Polygon2D _beam = null!;
    private readonly List<Node2D> _turrets = new();
    private readonly List<GpuParticles2D> _muzzles = new(); // C24 修复：MuzzleFlash 缓存（与 _turrets 同序）
    private Sprite2D _sprite = null!;

    /// <summary>P2：目标数组输出缓冲（_targetsBuf 复用，免每次调用分配新 List）。</summary>
    private readonly List<Node2D> _targetsBuf = new();


    // 导弹目标按「距对接点距离」排序的比较器（复用委托，避免每次齐射捕获闭包分配）
    private readonly Comparison<Node2D> _compareByDock;
    private Vector2 _dockSortAnchor;

    public Mothership()
    {
        _compareByDock = CompareByDock;
    }

    public override void _Ready()
    {
        // L13：注册在场组——事件（精英炮塔/编队）can_trigger 据此互斥：
        // 母舰在场期事件不触发（母舰自动火力会摧毁事件单位并全额发奖，玩家进舱零参与挂机）
        AddToGroup("mothership");
        _beam = GetNode<Polygon2D>("TractorBeam");
        _turrets.Add(GetNode<Node2D>("TurretL"));
        _turrets.Add(GetNode<Node2D>("TurretR"));
        LoadBalance();
        _magCells = MagCells;
        _departSpeed = DepartStartSpeed;
        // 机体尺寸族：设计值 × 全局缩放（tscn 存母舰基线 1.25，此处幂等覆盖——G032：注释与实际一致）
        var ws = (float)GameState.Instance.WorldScale;
        _sprite = GetNode<Sprite2D>("Sprite2D");
        _sprite.Scale = Vector2.One * ShipScale * ws;
        var beamPts = _beam.Polygon;
        for (var i = 0; i < beamPts.Length; i++)
        {
            beamPts[i] *= ws;
        }

        _beam.Polygon = beamPts;
        var turretL = _turrets[0];
        var turretR = _turrets[1];
        turretL.Position = new Vector2(-170.0f, 80.0f) * ws;
        turretR.Position = new Vector2(170.0f, 80.0f) * ws;
        _muzzles.Clear();
        foreach (var turret in _turrets)
        {
            var muzzle = turret.GetNode<GpuParticles2D>("MuzzleFlash");
            _muzzles.Add(muzzle);
            muzzle.Position = new Vector2(24.0f, 0.0f) * ws;
            if (muzzle.ProcessMaterial is ParticleProcessMaterial muzzleMat)
            {
                muzzleMat.ScaleMin = 1.5f * ws;
                muzzleMat.ScaleMax = 3.0f * ws;
            }
        }

        // 直接实例化（测试/教程，未经 begin_warp_in）：穿梭参数按当前位置补默认
        if (_warpTarget == Vector2.Zero)
        {
            _warpTarget = new Vector2(Position.X, HoverY);
            _warpFrom = _warpTarget + new Vector2(0.0f, -WarpInDrop);
        }

        _ws = ws;
        BuildFx();
    }

    /// <summary>数值配置缓存（启动一次读入，避免每帧 Dictionary 路径查找）。</summary>
    private void LoadBalance()
    {
        HoverY = (float)GameState.Instance.Cfg("mothership.hover_y", HoverY).AsDouble();
        ReleaseInvincible = (float)GameState.Instance.Cfg("mothership.release_invincible", ReleaseInvincible).AsDouble();
        DockTweenTime = (float)GameState.Instance.Cfg("mothership.dock_tween_time", DockTweenTime).AsDouble();
        DockOffsetY = (float)GameState.Instance.Cfg("mothership.dock_offset_y", DockOffsetY).AsDouble()
            * (float)GameState.Instance.WorldScale;
        ResupplyDelay = (float)GameState.Instance.Cfg("mothership.resupply_delay", ResupplyDelay).AsDouble();
        ReleaseTime = (float)GameState.Instance.Cfg("mothership.release_time", ReleaseTime).AsDouble();
        ReleaseDrop = (float)GameState.Instance.Cfg("mothership.release_drop", ReleaseDrop).AsDouble()
            * (float)GameState.Instance.WorldScale;
        // 2026-08-10 健壮性审查：mag_cells 钳下限 1——0 时 EarlyDepart/StartReleaseInternal 的
        // _magCells/MagCells 除零得 NaN，经 _prefill 传入 DepartCooldown 冷却信号使母舰冷却静默失效
        MagCells = Mathf.Max((int)GameState.Instance.Cfg("mothership.mag_cells", MagCells).AsInt64(), 1);
        // AB7：mag_cell_time 钳下限 0.05（MagCells 同批孪生遗漏）——≤0 时 _magCellTimer ≥ 恒真
        // 每帧耗 1 格，STAY 驻留瞬结、警告/提前离舰路径失效
        MagCellTime = Mathf.Max((float)GameState.Instance.Cfg("mothership.mag_cell_time", MagCellTime).AsDouble(), 0.05f);
        MagWarnCells = (int)GameState.Instance.Cfg("mothership.mag_warn_cells", MagWarnCells).AsInt64();
        WarnEjectDelay = (float)GameState.Instance.Cfg("mothership.warn_eject_delay", WarnEjectDelay).AsDouble();
        // 2026-08-10 健壮性审查：early_hold_time 钳下限——0 时 HUD 蓄力进度 _earlyTimer/早期离舰
        // 除零得 inf，且 _earlyTimer >= 0 恒真致长按 H 第一帧即触发离舰
        EarlyHoldTime = Mathf.Max((float)GameState.Instance.Cfg("mothership.early_hold_time", EarlyHoldTime).AsDouble(), 0.01f);
        EarlyMaxDiscount = (float)GameState.Instance.Cfg("mothership.early_max_discount", EarlyMaxDiscount).AsDouble();
        EarlyPrefillMax = (float)GameState.Instance.Cfg("mothership.early_prefill_max", EarlyPrefillMax).AsDouble();
        EarlyPrefillRatio = (float)GameState.Instance.Cfg("mothership.early_prefill_ratio", EarlyPrefillRatio).AsDouble();
        DepartCooldown = (float)GameState.Instance.Cfg("mothership.depart_cooldown", DepartCooldown).AsDouble();
        DepartStartSpeed = (float)GameState.Instance.Cfg("mothership.depart_start_speed", DepartStartSpeed).AsDouble();
        DepartAccel = (float)GameState.Instance.Cfg("mothership.depart_accel", DepartAccel).AsDouble();
        DriveAccel = (float)GameState.Instance.Cfg("mothership.drive.accel", DriveAccel).AsDouble();
        DriveMaxSpeed = (float)GameState.Instance.Cfg("mothership.drive.max_speed", DriveMaxSpeed).AsDouble();
        // B11 口径澄清：DRIVE_MARGIN_* 乘 world_scale 是有意例外——margin 语义是「舰体边缘到屏边
        // 视觉距离恒定」（舰体缩放后边缘保持同屏距），归类为机体偏移族（乘 ws），
        // 区别于 boss.strafe/hover_band/fight_y 等「中心坐标」屏幕边界族（不乘）。
        DriveMarginX = (float)GameState.Instance.Cfg("mothership.drive.margin_x", DriveMarginX).AsDouble()
            * (float)GameState.Instance.WorldScale;
        DriveMarginTop = (float)GameState.Instance.Cfg("mothership.drive.margin_top", DriveMarginTop).AsDouble()
            * (float)GameState.Instance.WorldScale;
        DriveMarginBottom = (float)GameState.Instance.Cfg("mothership.drive.margin_bottom", DriveMarginBottom).AsDouble()
            * (float)GameState.Instance.WorldScale;
        // 2026-08-04 母舰扩展：升级档位配置（阈值/伤害/射速倍率）
        _upgradeThreshold = (int)GameState.Instance.Cfg("mothership.upgrade.threshold", _upgradeThreshold).AsInt64();
        _upgradeDamageMult = (float)GameState.Instance.Cfg("mothership.upgrade.damage_mult", _upgradeDamageMult).AsDouble();
        _upgradeIntervalMult = (float)GameState.Instance.Cfg("mothership.upgrade.interval_mult", _upgradeIntervalMult).AsDouble();
        GatlingInterval = (float)GameState.Instance.Cfg("mothership.gatling.interval", GatlingInterval).AsDouble();
        GatlingBulletSpeed = (float)GameState.Instance.Cfg("mothership.gatling.bullet_speed", GatlingBulletSpeed).AsDouble();
        GatlingDamage = (int)GameState.Instance.Cfg("mothership.gatling.damage", GatlingDamage).AsInt64();
        GatlingScoreScale = (float)GameState.Instance.Cfg("mothership.gatling.score_scale", GatlingScoreScale).AsDouble();
        GatlingSweepLeftMin = (float)GameState.Instance.Cfg("mothership.gatling.sweep_left_min", GatlingSweepLeftMin).AsDouble();
        GatlingSweepLeftMax = (float)GameState.Instance.Cfg("mothership.gatling.sweep_left_max", GatlingSweepLeftMax).AsDouble();
        GatlingSweepRightMin = (float)GameState.Instance.Cfg("mothership.gatling.sweep_right_min", GatlingSweepRightMin).AsDouble();
        GatlingSweepRightMax = (float)GameState.Instance.Cfg("mothership.gatling.sweep_right_max", GatlingSweepRightMax).AsDouble();
        // 2026-08-10 健壮性审查：扫掠周期钳下限（对齐 Enemy.SinFast 注释口径）——0 时
        // _sweepTime * Tau / period 除零得 inf → SinFast 返回 NaN → 炮塔/弹方向 NaN（弹被
        // HasPoint(NaN) 恒 false 立即回收，每发开火空耗且无伤害）
        GatlingSweepLeftPeriod = Mathf.Max((float)GameState.Instance.Cfg("mothership.gatling.sweep_left_period", GatlingSweepLeftPeriod)
            .AsDouble(), 0.05f);
        GatlingSweepRightPeriod = Mathf.Max((float)GameState.Instance.Cfg("mothership.gatling.sweep_right_period", GatlingSweepRightPeriod)
            .AsDouble(), 0.05f);
        GatlingSweepRightPhase = (float)GameState.Instance.Cfg("mothership.gatling.sweep_right_phase", GatlingSweepRightPhase)
            .AsDouble();
        MissileInterval = (float)GameState.Instance.Cfg("mothership.missile.interval", MissileInterval).AsDouble();
        MissileDamage = (int)GameState.Instance.Cfg("mothership.missile.damage", MissileDamage).AsInt64();
        MissileSpeed = (float)GameState.Instance.Cfg("mothership.missile.speed", MissileSpeed).AsDouble();
        MissileTargetCount = (int)GameState.Instance.Cfg("mothership.missile.target_count", MissileTargetCount).AsInt64();
        MissileSplashDamage = (int)GameState.Instance.Cfg("mothership.missile.splash_damage", MissileSplashDamage).AsInt64();
        MissileSplashRadius = (float)GameState.Instance.Cfg("mothership.missile.splash_radius", MissileSplashRadius).AsDouble();
        WarpInTime = (float)GameState.Instance.Cfg("effects.mothership_summon.warp_in_time", WarpInTime).AsDouble();
        WarpInDrop = (float)GameState.Instance.Cfg("effects.mothership_summon.warp_in_drop", WarpInDrop).AsDouble()
            * (float)GameState.Instance.WorldScale;
        SlowRadius = (float)GameState.Instance.Cfg("effects.mothership_summon.slow.radius", SlowRadius).AsDouble();
        SlowDuration = (float)GameState.Instance.Cfg("effects.mothership_summon.slow.duration", SlowDuration).AsDouble();
        SlowFactor = (float)GameState.Instance.Cfg("effects.mothership_summon.slow.factor", SlowFactor).AsDouble();
        SlowRingTime = (float)GameState.Instance.Cfg("effects.mothership_summon.slow.ring_time", SlowRingTime).AsDouble();
        ShakeSlow = (float)GameState.Instance.Cfg("effects.mothership_summon.shake_slow", ShakeSlow).AsDouble();
    }

    /// <summary>演出附件预建（帧内只写属性，零分配）：引擎光晕 + 双向尾迹 + 牵引光束附件组。</summary>
    private void BuildFx()
    {
        // 引擎光晕（舰底喷口位）：DESCEND 巨大→常态收敛，DEPART 随加速增大
        _engineGlow = (Sprite2D)CinematicFx.SoftGlow(70.0f * _ws, new Color(0.45f, 0.85f, 1.0f, 0.0f));
        _engineGlow.Position = new Vector2(0.0f, 85.0f * _ws);
        _engineGlowBase = _engineGlow.Scale;
        AddChild(_engineGlow);
        // 穿出期上冲气流（相对舰体向上冲刷）
        _descendTrail = (GpuParticles2D)CinematicFx.Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 48,
            ["lifetime"] = 0.55,
            ["direction"] = new Vector3(0.0f, -1.0f, 0.0f),
            ["spread"] = 28.0f,
            ["vel_min"] = 320.0f * _ws,
            ["vel_max"] = 640.0f * _ws,
            ["scale_min"] = 8.0f * _ws,
            ["scale_max"] = 18.0f * _ws,
            ["color"] = new Color(0.5f, 0.85f, 1.0f, 0.6f),
        });
        _descendTrail.Position = new Vector2(0.0f, 30.0f * _ws);
        _descendTrail.Emitting = false;
        AddChild(_descendTrail);
        // 离场下喷尾迹
        _departTrail = (GpuParticles2D)CinematicFx.Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 48,
            ["lifetime"] = 0.6,
            ["direction"] = new Vector3(0.0f, 1.0f, 0.0f),
            ["spread"] = 22.0f,
            ["vel_min"] = 340.0f * _ws,
            ["vel_max"] = 640.0f * _ws,
            ["scale_min"] = 8.0f * _ws,
            ["scale_max"] = 20.0f * _ws,
            ["color"] = new Color(0.55f, 0.9f, 1.0f, 0.65f),
        });
        _departTrail.Position = new Vector2(0.0f, 90.0f * _ws);
        _departTrail.Emitting = false;
        AddChild(_departTrail);
        // 牵引光束附件组（随 _beam.visible 同步显隐）
        _beamFx = new Node2D();
        _beamFx.Visible = false;
        AddChild(_beamFx);
        // 捕获流环 ×3：预建最大半径椭圆点集，帧内仅缩放/位移/透明度
        for (var i = 0; i < 3; i++)
        {
            var ring = new Line2D
            {
                Width = 2.5f,
                DefaultColor = new Color(0.55f, 0.95f, 1.0f),
                Points = CinematicFx.RingPoints(28, 90.0f * _ws * 0.92f, 0.35f),
                Material = (CanvasItemMaterial)CinematicFx.AdditiveMaterial(),
            };
            _beamFx.AddChild(ring);
            _beamRings.Add(ring);
        }

        // 光束两侧描边（与 TractorBeam 斜边同位，微闪强化轮廓）
        for (var sx = -1.0f; sx <= 1.0f; sx += 2.0f)
        {
            var edge = new Line2D
            {
                Width = 2.0f,
                DefaultColor = new Color(0.6f, 0.95f, 1.0f),
                Points = new[]
                {
                    new Vector2(40.0f * sx, 60.0f) * _ws,
                    new Vector2(90.0f * sx, 200.0f) * _ws,
                },
                Material = (CanvasItemMaterial)CinematicFx.AdditiveMaterial(),
            };
            _beamFx.AddChild(edge);
            _beamEdges.Add(edge);
        }

        // 光束下端上升尘粒（回收吸附感）
        _beamDust = (GpuParticles2D)CinematicFx.Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 30,
            ["lifetime"] = 0.8,
            ["direction"] = new Vector3(0.0f, -1.0f, 0.0f),
            ["spread"] = 35.0f,
            ["vel_min"] = 50.0f * _ws,
            ["vel_max"] = 120.0f * _ws,
            ["scale_min"] = 3.5f * _ws,
            ["scale_max"] = 7.0f * _ws,
            ["color"] = new Color(0.6f, 0.95f, 1.0f, 0.65f),
        });
        _beamDust.Position = new Vector2(0.0f, 190.0f * _ws);
        _beamDust.Emitting = false;
        _beamFx.AddChild(_beamDust);
    }

    /// <summary>穿梭入场（召唤序列入口，由 main 在实例化后调用）：母舰从穿梭门门心穿出，
    /// 缩放 0.25→1 + ease-out 减速滑入停驻点；gate_pos 即最终停驻点。
    /// 注意：main 在 add_child 前调用本方法（先于 _ready 配置缓存），行程须内联读配置。</summary>
    public void BeginWarpIn(Vector2 gatePos, WarpGate gate)
    {
        _warpGate = gate;
        _warpTarget = gatePos;
        var drop = (float)GameState.Instance.Cfg("effects.mothership_summon.warp_in_drop", WarpInDrop).AsDouble();
        _warpFrom = gatePos + new Vector2(0.0f, -drop) * (float)GameState.Instance.WorldScale;
        Position = _warpFrom;
        Scale = Vector2.One * 0.25f;
        Modulate = new Color(1.8f, 1.8f, 2.2f);
    }

    public string StateText()
    {
        switch (_state)
        {
            case State.DESCEND:
                return (string)Tr("MS_DESCEND");
            case State.DOCKING:
            case State.RESUPPLY:
                return (string)Tr("MS_DOCKING");
            case State.STAY:
                var stay = GdFormat.Format((string)Tr("MS_STAY"), Mathf.CeilToInt(_magCells * MagCellTime - _magCellTimer));
                if (Tier() == 1)
                {
                    stay += "  " + (string)Tr("MS_UPGRADED");
                }

                return stay;
            case State.RELEASE:
            case State.DEPART:
                return (string)Tr("MS_LEAVE");
        }

        return "";
    }

    // ---------------- 对外公开接口（A1 修复）：HUD 轮询读取状态/弹匣，禁止跨类直接写 _ 私有字段 ----------------

    public State GetState() => _state;

    public int GetMagCells() => _magCells;

    public void SetStateTimer(float seconds) => _stateTimer = seconds;

    public float MagCellTimer() => _magCellTimer;

    public bool MagWarned() => _magWarned;

    public float WarnEjectTimer() => _warnEjectTimer;

    public Polygon2D Beam() => _beam;

    public void SetMagCellTimer(float seconds) => _magCellTimer = seconds;

    public void SetMagCells(int count) => _magCells = count;

    public void SetWarnEjectTimer(float seconds) => _warnEjectTimer = seconds;

    /// <summary>2026-08-04 母舰扩展：升级档位——里程碑数 ≥ 阈值即升档（0 或 1）。</summary>
    public int Tier() => (int)GameState.Instance.MilestoneCount() >= _upgradeThreshold ? 1 : 0;

    public float DamageMult() => Tier() == 1 ? _upgradeDamageMult : 1.0f;

    public float IntervalMult() => Tier() == 1 ? _upgradeIntervalMult : 1.0f;

    public void StartRelease() => StartReleaseInternal();

    // ---------------- snake_case 兼容桥（M7 后保留：仍有 C# 动态派发/测试调用方；新代码直接调 PascalCase 主方法） ----------------

    public static int GetStateDescend() => (int)State.DESCEND;

    public static int GetStateDocking() => (int)State.DOCKING;

    public static int GetStateResupply() => (int)State.RESUPPLY;

    public static int GetStateStay() => (int)State.STAY;

    public static int GetStateRelease() => (int)State.RELEASE;

    public static int GetStateDepart() => (int)State.DEPART;

    public State state() => GetState();

    public int mag_cells() => GetMagCells();

    public float damage_mult() => DamageMult();

    public float interval_mult() => IntervalMult();

    // ---------------- snake_case 兼容桥（M7 后保留：仍有 C# 动态派发/测试调用方；新代码直接调 PascalCase 主方法） ----------------

    public float HOVER_Y { get => HoverY; set => HoverY = value; }

    public float DOCK_OFFSET_Y { get => DockOffsetY; set => DockOffsetY = value; }

    public int MAG_CELLS { get => MagCells; set => MagCells = value; }

    public float WARP_IN_TIME { get => WarpInTime; set => WarpInTime = value; }

    public float DRIVE_MARGIN_X { get => DriveMarginX; set => DriveMarginX = value; }

    public int GATLING_DAMAGE { get => GatlingDamage; set => GatlingDamage = value; }

    // ---------------- 内部实现 ----------------

    private void EnterState(State pState)
    {
        _state = pState;
        _stateTimer = 0.0f;
    }

    /// <summary>对接点（舰腹正中，玩家/导弹的锚点）。</summary>
    private Vector2 DockPoint() => GlobalPosition + new Vector2(0.0f, DockOffsetY);

    public override void _PhysicsProcess(double delta)
    {
        var d = (float)delta;
        // 牵引光束附件随 _beam.visible 同步显隐（_start_docking/start_release/对接完成均走此同步）
        if (_beamFx.Visible != _beam.Visible)
        {
            _beamFx.Visible = _beam.Visible;
            _beamDust.Emitting = _beam.Visible;
        }

        if (_beam.Visible)
        {
            // 淡光束：低调脉动，不刺眼（P2：查表 sin）；时钟取一次，脉动与附件共用
            var nowS = (float)(Time.GetTicksMsec() / 1000.0);
            var bm = _beam.Modulate;
            bm.A = 0.55f + 0.45f * Enemy.SinFast(nowS * 8.0f); // M3b：Enemy 迁 C#，静态直调
            _beam.Modulate = bm;
            UpdateBeamFx(d, nowS);
        }

        _stateTimer += d;
        switch (_state)
        {
            case State.DESCEND:
                {
                    // 穿梭门穿出：缩放 0.25→1 + ease-out 减速滑入停驻点
                    var p = Mathf.Clamp(_stateTimer / WarpInTime, 0.0f, 1.0f);
                    var e = 1.0f - Mathf.Pow(1.0f - p, 3.0f);
                    Position = _warpFrom.Lerp(_warpTarget, e);
                    Scale = Vector2.One * Mathf.Lerp(0.25f, 1.0f, e);
                    Modulate = new Color(1.8f, 1.8f, 2.2f).Lerp(Colors.White, e);
                    // 引擎制动光晕随同一 ease-out 从巨大收到常态；上冲气流全程伴随
                    _descendTrail.Emitting = p < 1.0f;
                    var eg = _engineGlow.Modulate;
                    eg.A = 0.85f * (1.0f - e);
                    _engineGlow.Modulate = eg;
                    _engineGlow.Scale = _engineGlowBase * Mathf.Lerp(2.4f, 0.8f, e);
                    if (p >= 1.0f)
                    {
                        Position = _warpTarget;
                        Scale = Vector2.One;
                        Modulate = Colors.White;
                        eg = _engineGlow.Modulate;
                        eg.A = 0.0f;
                        _engineGlow.Modulate = eg;
                        if (_warpGate != null)
                        {
                            // H14（健壮性审核）：穿梭门可能先于母舰释放（场景卸载时序不定），防悬挂引用
                            if (GodotObject.IsInstanceValid(_warpGate))
                            {
                                _warpGate!.Close();
                            }

                            _warpGate = null;
                        }

                        DeploySlowField();
                        var hud = Hud();
                        if (hud != null)
                        {
                            hud.ShowInfoBanner(Tr("BANNER_MOTHERSHIP_ARRIVED"));
                        }

                        StartDocking(GameState.Instance.PlayerRef); // M3c：player_ref 恒为 Player
                    }

                    break;
                }

            case State.DOCKING:
                {
                    // 回收牵引期间火力掩护（加特林+导弹，不耗驻留弹匣）
                    UpdateGatling(d);
                    UpdateMissiles(d);
                    if (_stateTimer >= DockTweenTime)
                    {
                        _beam.Visible = false; // 对接完成即隐藏牵引光束，否则驻留期一直闪烁
                                               // 回收完成：玩家进保护舱（隐藏+关受击判定，驻留全程保持，RELEASE 出舱）
                        if (GodotObject.IsInstanceValid(_player) && !_player.IsDead())
                        {
                            _player.EnterPod();
                            // 进舱捕获反馈：对接点小冲击环 + 短促软闪
                            var sw = (Node2D)CinematicFx.Shockwave(new Godot.Collections.Dictionary
                            {
                                ["radius"] = 120.0f * _ws,
                                ["time"] = 0.5,
                                ["ry_ratio"] = 0.6,
                                ["color"] = new Color(0.5f, 0.95f, 1.0f, 0.5f),
                                ["core_color"] = new Color(0.9f, 1.0f, 1.0f, 0.9f),
                                ["width"] = 8.0,
                            });
                            sw.Position = DockPoint();
                            GetParent()!.AddChild(sw);
                            SoftFlash(DockPoint(), 70.0f * _ws, new Color(0.8f, 1.0f, 1.0f, 0.9f));
                            var hud = Hud();
                            if (hud != null)
                            {
                                hud.Call(
                                    "show_popup",
                                    Tr("POD_SECURED"),
                                    GlobalPosition + new Vector2(0.0f, 120.0f) * (float)GameState.Instance.WorldScale);
                            }
                        }

                        EnterState(State.RESUPPLY);
                    }

                    break;
                }

            case State.RESUPPLY:
                if (_stateTimer >= ResupplyDelay)
                {
                    DoResupply();
                    EnterState(State.STAY);
                }

                break;
            case State.STAY:
                UpdateDrive(d);
                UpdateGatling(d);
                UpdateMissiles(d);
                // 弹匣消耗
                _magCellTimer += d;
                if (_magCellTimer >= MagCellTime)
                {
                    _magCellTimer -= MagCellTime;
                    _magCells -= 1;
                    if (_magCells == MagWarnCells && !_magWarned)
                    {
                        _magWarned = true;
                        _warnEjectTimer = WarnEjectDelay;
                        var hud = Hud();
                        if (hud != null)
                        {
                            hud.ShowMagazineWarning();
                        }
                    }
                }

                // 警告横幅播完（5s）强制离舰（对齐原作；自然 20s 到期因此不可达）
                if (_magWarned)
                {
                    _warnEjectTimer -= d;
                    if (_warnEjectTimer <= 0.0f)
                    {
                        StartReleaseInternal();
                    }
                }

                // 提前离舰：长按 H 2s（蓄力进度条经 HUD 显示，松手清零隐藏）
                if (Input.IsActionPressed("dock"))
                {
                    _earlyTimer += d;
                    var hud = Hud();
                    if (hud != null)
                    {
                        hud.SetEarlyLeaveCharge((float)(_earlyTimer / EarlyHoldTime));
                    }
                }
                else
                {
                    if (_earlyTimer > 0.0f)
                    {
                        var hud = Hud();
                        if (hud != null)
                        {
                            hud.SetEarlyLeaveCharge(-1.0f);
                        }
                    }

                    _earlyTimer = 0.0f;
                }

                if (_earlyTimer >= EarlyHoldTime)
                {
                    EarlyDepart();
                }
                else if (_magCells <= 0)
                {
                    StartReleaseInternal();
                }

                break;
            case State.RELEASE:
                if (_stateTimer >= ReleaseTime)
                {
                    if (GodotObject.IsInstanceValid(_player) && !_player.IsDead())
                    {
                        _player.UnlockInput();
                        _player.SetInvincible(ReleaseInvincible);
                    }

                    EnterState(State.DEPART);
                    EmitSignal(SignalName.Departed, DepartCooldown * _cooldownFactor * (1.0f - _prefill));
                }

                break;
            case State.DEPART:
                _departSpeed += DepartAccel * d;
                Position = new Vector2(Position.X, Position.Y - _departSpeed * d);
                // 离场加速：引擎光晕随速度增大，下喷尾迹全程伴随
                _departTrail.Emitting = true;
                var sp = Mathf.Clamp(_departSpeed / 500.0f, 0.0f, 1.0f);
                var dg = _engineGlow.Modulate;
                dg.A = 0.15f + 0.75f * sp;
                _engineGlow.Modulate = dg;
                _engineGlow.Scale = _engineGlowBase * (0.7f + 1.6f * sp);
                if (Position.Y < GameState.Instance.ViewWorldRect().Position.Y - 200.0f)
                {
                    QueueFree();
                }

                break;
        }
    }

    /// <summary>减速带冲击波（穿梭入场到位帧）：短时减速全场敌人（仅位移乘区，duck-typing
    /// 仅 Enemy/Boss 响应）；视觉为双环冲击波（主环满半径+填充盘，副环尾随），播完自毁。</summary>
    private void DeploySlowField()
    {
        GameState.Instance.Shake(ShakeSlow);
        GameState.Instance.PlaySfx(GameState.Instance.SFX_EXPLOSION_BIG, -10.0, 0.6);
        // 统一实体管理器批量 API 语义等价直迭代（docs/ENTITY_MANAGER.md）：失效实例跳过 +
        // W 系列（2026-08-09）：apply_slow 仅 Enemy 实现，typed 直调（U13 已收口动态派发）
        foreach (var item in GameState.Instance.Enemies)
        {
            var e = item;
            if (e == null || !GodotObject.IsInstanceValid(e))
            {
                continue;
            }

            if (e is Enemy enemy)
            {
                enemy.ApplySlow(SlowDuration, SlowFactor);
            }
            else if (e is Boss boss)
            {
                // 2026-08-09 审计：原 GDScript duck-typing（has_method("apply_slow")）含 Boss，
                // M4 typed 化遗漏——Boss.ApplySlow/_summonSlowTimer 因此成死代码，此处恢复语义
                boss.ApplySlow(SlowDuration, SlowFactor);
            }
        }

        var sw = (Node2D)CinematicFx.Shockwave(new Godot.Collections.Dictionary
        {
            ["radius"] = SlowRadius,
            ["time"] = SlowRingTime,
            ["color"] = new Color(0.32f, 0.93f, 0.85f, 0.45f),
            ["core_color"] = new Color(0.75f, 1.0f, 0.95f, 0.85f),
            ["width"] = 14.0,
            ["fill"] = true,
        });
        sw.Position = Position;
        GetParent()!.AddChild(sw);
        // 副环：起点比例更大 + 时长更长，读作主环之后的内侧余波
        var echo = (Node2D)CinematicFx.Shockwave(new Godot.Collections.Dictionary
        {
            ["radius"] = SlowRadius,
            ["time"] = SlowRingTime * 1.4f,
            ["start_scale"] = 0.45,
            ["color"] = new Color(0.32f, 0.93f, 0.85f, 0.3f),
            ["core_color"] = new Color(0.7f, 1.0f, 0.95f, 0.6f),
            ["width"] = 7.0,
        });
        echo.Position = Position;
        GetParent()!.AddChild(echo);
    }

    /// <summary>牵引光束附件帧驱动（仅 _beam.visible 时调用，零分配）：
    /// 捕获流环自窄端向宽端循环流动，两侧描边微闪；now_s 由调用方帧首取一次共用。</summary>
    private void UpdateBeamFx(float delta, float nowS)
    {
        for (var i = 0; i < _beamRings.Count; i++)
        {
            _beamRingU[i] = Mathf.PosMod(_beamRingU[i] + delta * 0.55f, 1.0f);
            var u = _beamRingU[i];
            var ring = _beamRings[i];
            ring.Position = new Vector2(0.0f, Mathf.Lerp(60.0f, 200.0f, u) * _ws);
            var k = Mathf.Lerp(40.0f, 90.0f, u) / 90.0f;
            ring.Scale = new Vector2(k, k);
            var rm = ring.Modulate;
            rm.A = 0.75f * Enemy.SinFast(Mathf.Pi * u); // M3b：Enemy 迁 C#，静态直调
            ring.Modulate = rm;
        }

        for (var i = 0; i < _beamEdges.Count; i++)
        {
            var em = _beamEdges[i].Modulate;
            em.A = 0.5f + 0.25f * Enemy.SinFast(nowS * 9.0f + i * 2.1f); // M3b：Enemy 迁 C#，静态直调
            _beamEdges[i].Modulate = em;
        }
    }

    /// <summary>一次性软闪（进舱/释放等瞬时反馈）：软光晕快速淡出后自毁。</summary>
    private void SoftFlash(Vector2 pos, float radius, Color color)
    {
        var g = (Sprite2D)CinematicFx.SoftGlow(radius, color);
        g.Position = pos;
        GetParent()!.AddChild(g);
        var tw = g.CreateTween();
        tw.TweenProperty(g, "modulate:a", 0.0, 0.35).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.TweenCallback(Callable.From(g.QueueFree));
    }

    /// <summary>驻留期间 WASD 驾驶母舰（对齐原作：加速 900、极速 180、松手即停、边界夹紧），
    /// 玩家机每帧钉在对接点。</summary>
    private void UpdateDrive(float delta)
    {
        var inputDir = Input.GetVector("move_left", "move_right", "move_up", "move_down");
        if (inputDir == Vector2.Zero)
        {
            _driveVel = Vector2.Zero;
        }
        else
        {
            _driveVel = _driveVel.MoveToward(inputDir * DriveMaxSpeed, DriveAccel * delta);
        }

        Position += _driveVel * delta;
        var view = GameState.Instance.ViewWorldRect();
        Position = Position.Clamp(
            view.Position + new Vector2(DriveMarginX, DriveMarginTop), view.End - new Vector2(DriveMarginX, DriveMarginBottom));
        if (GodotObject.IsInstanceValid(_player) && !_player.IsDead())
        {
            _player.GlobalPosition = DockPoint();
        }
    }

    /// <summary>场上有效目标（敌机注册表筛掉离场中；Boss 筛掉逃跑中）。
    /// P2：复用 _targetsBuf，调用方仅当帧消费（is_empty/排序后不再保留引用）。</summary>
    private List<Node2D> LiveTargets()
    {
        _targetsBuf.Clear();
        // 统一实体管理器批量 API 语义等价直迭代（docs/ENTITY_MANAGER.md）：
        // 失效实例跳过 + Node2D 判型 + Enemy 离场 / Boss 逃跑过滤
        foreach (var item in GameState.Instance.Enemies)
        {
            var e = item;
            if (e == null || !GodotObject.IsInstanceValid(e))
            {
                continue;
            }

            if (IsLiveTarget(e))
            {
                _targetsBuf.Add((Node2D)e);
            }
        }

        return _targetsBuf;
    }

    /// <summary>过滤谓词：非 Node2D/离场中的 Enemy/逃跑中的 Boss 排除。</summary>
    private bool IsLiveTarget(GodotObject e)
    {
        if (e is not Node2D)
        {
            return false;
        }

        if (e is Enemy enemy && enemy.IsExiting())
        {
            return false;
        }

        if (e is Boss boss && boss.IsEscaped)
        {
            return false;
        }

        return true;
    }

    /// <summary>导弹目标升序比较（GDScript sort_custom 语义：距对接点近者在前；锚点由调用方写）。</summary>
    private int CompareByDock(Node2D a, Node2D b)
        => a.GlobalPosition.DistanceSquaredTo(_dockSortAnchor).CompareTo(b.GlobalPosition.DistanceSquaredTo(_dockSortAnchor));

    /// <summary>加特林扫射压制（对齐原作）：驻留（STAY）与回收牵引（DOCKING）期有目标时开火；
    /// 双塔向上半球各扫 80°，左塔 [-60°,+20°] 周期 1.6s，右塔 [-20°,+60°] 周期 1.8s 相位 +0.35s（总覆盖 120°）。</summary>
    private void UpdateGatling(float delta)
    {
        _sweepTime += delta;
        _gatlingTimer -= delta;
        if (_gatlingTimer > 0.0f)
        {
            return;
        }

        _gatlingTimer = GatlingInterval * IntervalMult(); // G027：先置位再判空——空目标不每物理帧分配数组+扫注册表
        if (LiveTargets().Count == 0)
        {
            return;
        }

        var pool = GameState.Instance.BulletPool as BulletPool;
        if (pool == null)
        {
            return;
        }

        for (var i = 0; i < _turrets.Count; i++)
        {
            var turret = _turrets[i];
            float angle;
            if (i == 0)
            {
                var center = Mathf.DegToRad((GatlingSweepLeftMin + GatlingSweepLeftMax) * 0.5f);
                var half = Mathf.DegToRad((GatlingSweepLeftMax - GatlingSweepLeftMin) * 0.5f);
                angle = center + half * Enemy.SinFast(_sweepTime * Mathf.Tau / GatlingSweepLeftPeriod); // M3b：Enemy 静态直调
            }
            else
            {
                var center = Mathf.DegToRad((GatlingSweepRightMin + GatlingSweepRightMax) * 0.5f);
                var half = Mathf.DegToRad((GatlingSweepRightMax - GatlingSweepRightMin) * 0.5f);
                angle = center + half * Enemy.SinFast((_sweepTime + GatlingSweepRightPhase) * Mathf.Tau / GatlingSweepRightPeriod);
            }

            var dir = Vector2.Up.Rotated(angle);
            turret.GlobalRotation = dir.Angle();
            var b = pool.Fire(dir, GatlingBulletSpeed, (int)(GatlingDamage * DamageMult()), true);
            if (b == null)
            {
                continue; // 玩家弹无硬上限，理论上不可达（NRT 守卫）
            }

            b.ScoreScale = GatlingScoreScale;
            b.Position = turret.GlobalPosition;
            // 比玩家弹更细更亮（2026-08-06 审计：原 b.scale 连带缩放 Area2D 碰撞形状——
            // 命中半径 6→3.6×ws 判定变严；仅视觉缩放应作用于子 Sprite2D，池化复用自动复位）
            var bSprite = b.SpriteNode();
            if (bSprite != null)
            {
                bSprite.Scale = new Vector2(0.6f, 0.6f);
            }

            b.Modulate = new Color(1.4f, 1.4f, 1.1f);
            // C24：用缓存的 muzzle 引用（与 _turrets 同序），不再每次 get_node
            if (i < _muzzles.Count && _muzzles[i] != null)
            {
                _muzzles[i].Restart();
            }
        }

        GameState.Instance.PlaySfx(_gatlingSfx, -8.0);
    }

    /// <summary>导弹齐射（对齐原作）：驻留（STAY）与回收牵引（DOCKING）期，每 0.3s 一波，锁定距对接点最近的 ≤5 个目标
    /// （敌机+Boss 混合），发射瞬间定向的直线弹（无追踪），直击 80 + 80px 溅射 20。</summary>
    private void UpdateMissiles(float delta)
    {
        _missileTimer -= delta;
        if (_missileTimer > 0.0f)
        {
            return;
        }

        _missileTimer = MissileInterval * IntervalMult(); // G027：先置位再判空——空目标不每物理帧扫描
        var targets = LiveTargets();
        if (targets.Count == 0)
        {
            return;
        }

        var dock = DockPoint();
        _dockSortAnchor = dock;
        targets.Sort(_compareByDock);
        var pool = GameState.Instance.BulletPool as BulletPool;
        if (pool == null)
        {
            return;
        }

        for (var i = 0; i < Mathf.Min(MissileTargetCount, targets.Count); i++)
        {
            var t = targets[i];
            var dir = (t.GlobalPosition - dock).Normalized();
            if (dir == Vector2.Zero)
            {
                dir = Vector2.Up;
            }

            var b = pool.Fire(dir, MissileSpeed, (int)(MissileDamage * DamageMult()), true);
            if (b == null)
            {
                continue; // 玩家弹无硬上限，理论上不可达（NRT 守卫）
            }

            b.ScoreScale = MissileScoreScale; // G030：独立常量（原复用 GATLING_SCORE_SCALE 语义混用）
            b.SplashDamage = MissileSplashDamage;
            b.SplashRadius = MissileSplashRadius;
            b.Position = dock;
            // 橙体高亮（原作爆炸导弹视觉；精灵随速度方向旋转）
            b.Modulate = new Color(2.0f, 1.1f, 0.5f);
        }
    }

    /// <summary>对接开始：锁输入 + 即无敌（对齐原作无敌窗口起点，堵对接/补给空窗）+ 吸附补间。</summary>
    private void StartDocking(GodotObject? player)
    {
        // M3c：Player 迁 C#，player_ref 恒为 Player；null 或已死即不可用
        var p = player as Player;
        if (p == null || p.IsDead())
        {
            QueueFree(); // 玩家不可用（死亡路径）：母舰直接离场，避免 HOVER 死态
            return;
        }

        _player = p;
        EnterState(State.DOCKING);
        _player.LockInput();
        _player.Velocity = Vector2.Zero;
        // 无敌窗口起点 = 吸附动画开始（锁输入期间无敌帧不衰减，对齐原作事件驱动无敌）
        _player.SetInvincible(999.0f);
        _beam.Visible = true;
        // 牵引光束吸附到对接点（原作定长补间 1.5s）
        var tween = CreateTween();
        tween.TweenProperty(_player, "global_position", DockPoint(), DockTweenTime)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
    }

    /// <summary>A5 收敛（DESIGN_BASELINE §7.1）：HUD 引用统一经延迟缓存获取——hud 是 main.tscn
    /// 固定层，生命周期内恒定；8 处重复 group 查找收敛为单点缓存。行为与直接查找等价
    /// （is_instance_valid 守卫：极端重载时序下缓存失效则重新查找）。</summary>
    private Hud? Hud()
    {
        if (!GodotObject.IsInstanceValid(_hudCache))
        {
            _hudCache = GetTree().GetFirstNodeInGroup("hud") as Hud;
        }

        return _hudCache;
    }

    private void DoResupply()
    {
        if (!GodotObject.IsInstanceValid(_player) || _player.IsDead())
        {
            return;
        }

        // 回满生命与燃料（重制版增强：原作母舰无补给，回复在基地 RP 交易）
        GameState.Instance.Heal(GameState.Instance.MaxHealth() - GameState.Instance.Health);
        _player.RefillFuel();
        GameState.Instance.PlaySfx(GameState.Instance.SFX_RESUPPLY);
        GameState.Instance.Shake(GameState.Instance.Cfg("effects.shake.mothership", 4.0).AsDouble());
        var hud = Hud();
        if (hud != null)
        {
            hud.Call(
                "show_popup",
                Tr("POP_RESUPPLY"),
                GlobalPosition + new Vector2(0.0f, 120.0f) * (float)GameState.Instance.WorldScale);
        }
    }

    /// <summary>提前离舰（长按 H 2s）：冷却双机制折扣——时长 max(0.6, 1-0.4×剩余比例)
    /// + 进度预填 min(0.3, 0.5×剩余比例)（对齐原作；预填仅此路径）。
    /// 2026-08-10：ratio 补 Clamp（MagCells 已钳 ≥1，双保险防超范围渗入 _prefill）。</summary>
    private void EarlyDepart()
    {
        var ratio = Mathf.Clamp((float)_magCells / MagCells, 0.0f, 1.0f);
        _prefill = Mathf.Min(EarlyPrefillMax, EarlyPrefillRatio * ratio);
        var hud = Hud();
        if (hud != null)
        {
            hud.SetEarlyLeaveCharge(-1.0f);
            var factor = Mathf.Max(0.6f, 1.0f - EarlyMaxDiscount * ratio) * (1.0f - _prefill);
            hud.Call(
                "show_popup",
                GdFormat.Format((string)Tr("POP_EARLY_LEAVE"), (int)((1.0f - factor) * 100.0f)),
                GlobalPosition);
        }

        StartReleaseInternal();
    }

    private void StartReleaseInternal()
    {
        // P2：STAY 多入口（警告到期/弹匣耗尽/提前离舰 _early_depart）可能同帧二次触发，
        // 非 STAY 直接短路，令 start_release 幂等
        if (_state != State.STAY)
        {
            return;
        }

        // E05：所有强制离舰路径（警告到期/弹匣耗尽）统一清 HUD 提前离舰进度条——
        // H 按住时走本路径不复位，进度条残留可见（_early_depart 已有清理，此处兜底全部入口）
        var hud = Hud();
        if (hud != null)
        {
            hud.SetEarlyLeaveCharge(-1.0f);
        }

        _beam.Visible = false;
        // 时长折扣对所有离场路径生效（剩余比例越低折扣越小）
        var ratio = Mathf.Clamp((float)_magCells / MagCells, 0.0f, 1.0f);
        _cooldownFactor = Mathf.Max(0.6f, 1.0f - EarlyMaxDiscount * ratio);
        EnterState(State.RELEASE);
        // 出舱释放反馈：对接点小喷发（一次性，随母舰离场自毁）
        var burst = (GpuParticles2D)CinematicFx.Particles(new Godot.Collections.Dictionary
        {
            ["amount"] = 20,
            ["lifetime"] = 0.5,
            ["explosiveness"] = 0.9,
            ["one_shot"] = true,
            ["direction"] = new Vector3(0.0f, 1.0f, 0.0f),
            ["spread"] = 55.0f,
            ["vel_min"] = 70.0f * _ws,
            ["vel_max"] = 200.0f * _ws,
            ["scale_min"] = 2.0f * _ws,
            ["scale_max"] = 5.0f * _ws,
            ["color"] = new Color(0.55f, 0.95f, 1.0f, 0.75f),
        });
        burst.Position = new Vector2(0.0f, DockOffsetY);
        AddChild(burst);
        if (!GodotObject.IsInstanceValid(_player) || _player.IsDead())
        {
            return;
        }

        _player.ExitPod(); // 出舱恢复显示（抛下补间全程可见）
        var tween = CreateTween();
        tween.TweenProperty(_player, "global_position", _player.GlobalPosition + new Vector2(0.0f, ReleaseDrop), ReleaseTime);
    }

    public override void _ExitTree()
    {
        // 提前收回（返航/对局重置等）：穿梭门关闭兜底；玩家若仍在保护舱则恢复显示
        if (_warpGate != null)
        {
            // H14：穿梭门可能先于母舰释放（场景卸载时序不定），防悬挂引用
            if (GodotObject.IsInstanceValid(_warpGate))
            {
                _warpGate!.Close();
            }

            _warpGate = null;
        }

        // G011：隐藏 HUD 提前离舰蓄力进度条（E05 只覆盖 start_release 强制离舰路径，返航提前回收漏清）
        var hud = Hud();
        if (hud != null)
        {
            hud.SetEarlyLeaveCharge(-1.0f);
        }

        if (GodotObject.IsInstanceValid(_player) && !_player.IsDead() && !_player.Visible)
        {
            _player.ExitPod();
        }
    }

    /// <summary>GDScript 字符串 % 格式化单参语义（%s/%d 占位 + %% 转义；tr() 文案补参用，
    // 2026-08-04 母舰扩展：火力随里程碑升级（阈值/伤害/射速倍率；默认值与 balance.json 双写）
    private int _upgradeThreshold = 5;
    private float _upgradeDamageMult = 1.5f;
    private float _upgradeIntervalMult = 0.8f;
}
