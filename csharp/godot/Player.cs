using Godot;

namespace InfiAir;

/// <summary>
/// 玩家战机（M3c 全量迁移，2026-08-08 自 scripts/player.gd 迁移）：WASD 平滑移动、朝准星旋转、
/// 全自动开火、Shift 加速、Ctrl 微调、空格相位冲刺（需 buff，耗 25% 燃料）。
/// A8 组合：PlayerDamage/PlayerDash/PlayerParry/PlayerVisuals（纯 C# 类）+ PlayerBuffVisuals（Node2D）。
/// 语义保持：声明式 BUFF_EFFECTS 表、辅助瞄准（P1-1/P1-3 追踪/锥形/磁吸）、入场动画、迷雾事件。
/// 公开 API 为 PascalCase；少量 snake_case 兼容桥因 C# 动态派发/测试调用方保留（桥段见文件底部）。
/// </summary>
public partial class Player : CharacterBody2D
{
    [Signal]
    public delegate void EntryFinishedEventHandler();

    // U07：静态 Godot 资源改实例字段（退出 segfault 实测教训，UITheme.cs:53）
    private readonly AudioStream[] _fireSounds =
    {
        GD.Load<AudioStream>("res://assets/audio/bullet_fire.wav"),
        GD.Load<AudioStream>("res://assets/audio/bullet_fire_b.wav"),
        GD.Load<AudioStream>("res://assets/audio/bullet_fire_c.wav"),
    };

    private readonly Script _bulletScript = GD.Load<Script>("res://csharp/godot/Bullet.cs");

    // U14（2026-08-09 审计）：热路径每帧禁 StringName/string 字面量构造——buff 名与输入 action 名静态缓存
    private static readonly StringName BuffCritShot = new("crit_shot");
    private static readonly StringName BuffRapidFire = new("rapid_fire");
    private static readonly StringName BuffPowerShot = new("power_shot");
    private static readonly StringName BuffBulletSpeed = new("bullet_speed");
    private static readonly StringName BuffPhaseDash = new("phase_dash");
    private static readonly StringName BuffEfficientBoost = new("efficient_boost");
    private static readonly StringName BuffBoostRecovery = new("boost_recovery");
    private static readonly StringName BuffSpreadShot = new("spread_shot");
    private static readonly StringName BuffPiercing = new("piercing");
    private static readonly StringName BuffExplosive = new("explosive");
    private static readonly StringName ActMoveLeft = new("move_left");
    private static readonly StringName ActMoveRight = new("move_right");
    private static readonly StringName ActMoveUp = new("move_up");
    private static readonly StringName ActMoveDown = new("move_down");
    private static readonly StringName ActParry = new("parry");
    private static readonly StringName ActDash = new("dash");
    private static readonly StringName ActBoost = new("boost");
    private static readonly StringName ActFineMove = new("fine_move");
    private static readonly StringName ActAimLeft = new("aim_left");
    private static readonly StringName ActAimRight = new("aim_right");
    private static readonly StringName ActAimUp = new("aim_up");
    private static readonly StringName ActAimDown = new("aim_down");

    // ---- 入场动画（balance.json player.entry） ----
    public float EntryLandRatio { get; private set; } = 0.74f;
    public float EntryRushTime { get; private set; } = 0.55f;
    public float EntryRetreatSpeed { get; private set; } = 90.0f;
    public float EntryRetreatTime { get; private set; } = 1.1f;
    public float EntryInvincible { get; private set; } = 2.1f;
    public float EntrySpawnClearance { get; private set; } = 90.0f;
    public float EntryRushHsRatio { get; private set; } = 0.6f;

    // ---- 移动/火力/生存数值（balance.json 覆盖） ----
    public float MaxSpeed { get; private set; } = 420.0f;
    public float Accel { get; private set; } = 2400.0f;
    public float Decel { get; private set; } = 1800.0f;
    public float BoostMult { get; private set; } = 1.8f;
    public float BaseFireInterval { get; private set; } = 0.15f;
    public float BulletSpeed { get; private set; } = 1800.0f;
    public float CritChanceBase { get; private set; } = 0.12f;
    public float CritMultiplier { get; private set; } = 2.0f;
    public float BulletSpreadDeg { get; private set; } = 15.0f;
    public int BulletDamage { get; private set; } = 10;
    public float InvincibleTime { get; private set; } = 1.5f;
    public float SpawnInvincibleTime { get; private set; } = 1.0f;
    public float BulletClearRadius { get; private set; } = 250.0f;
    public float ArmorMult { get; private set; } = 0.85f;
    public float EvasionChance { get; private set; } = 0.2f;
    public float RegenPerSec { get; private set; } = 2.0f;
    public float ShakeHit { get; private set; } = 12.0f;

    // ---- A4：声明式 buff 效果表（buff id → 效果定义；单一事实源） ----
    private static readonly Godot.Collections.Dictionary BuffEffects = new()
    {
        ["rapid_fire"] = new Godot.Collections.Dictionary { ["kind"] = "pow", ["cfg"] = "buffs.rapid_fire.factor", ["default"] = 0.75 },
        ["power_shot"] = new Godot.Collections.Dictionary { ["kind"] = "pow", ["cfg"] = "buffs.power_shot.factor", ["default"] = 1.25 },
        ["efficient_boost"] = new Godot.Collections.Dictionary { ["kind"] = "pow", ["cfg"] = "buffs.efficient_boost.factor", ["default"] = 0.75 },
        ["boost_recovery"] = new Godot.Collections.Dictionary { ["kind"] = "pow", ["cfg"] = "buffs.boost_recovery.factor", ["default"] = 1.5 },
        ["phase_dash"] = new Godot.Collections.Dictionary { ["kind"] = "pow", ["cfg"] = "player.dash.cooldown_stack_factor", ["default"] = 0.8 },
        ["spread_shot"] = new Godot.Collections.Dictionary { ["kind"] = "cap", ["cfg"] = "buffs.spread_shot.max_stacks", ["default"] = 2 },
        ["piercing"] = new Godot.Collections.Dictionary { ["kind"] = "cap", ["cfg"] = "buffs.piercing.max_stacks", ["default"] = 2 },
        ["explosive"] = new Godot.Collections.Dictionary { ["kind"] = "bool" },
        ["bullet_speed"] = new Godot.Collections.Dictionary { ["kind"] = "pow", ["cfg"] = "buffs.bullet_speed.factor", ["default"] = 1.2 },
    };

    /// <summary>声明式 buff 效果表公开访问（测试 A4 架构断言读取）。</summary>
    public Godot.Collections.Dictionary GetBuffEffects() => BuffEffects;

    private readonly Godot.Collections.Dictionary _buffValues = new();

    /// <summary>crit_shot 暴击参数缓存（buffs_changed 刷新；bullet 命中经 player_ref 读取）。</summary>
    public float CritChance { get; private set; }

    /// <summary>crit_shot 暴击倍率（同缓存）。</summary>
    public float CritMultiplierValue { get; private set; } = 1.0f;

    public float FuelDrain { get; private set; } = 35.0f;
    public float FuelRegen { get; private set; } = 20.0f;
    public float FuelRestart { get; private set; } = 30.0f;

    /// <summary>尾焰染色乘区（Buff 外观反馈）。</summary>
    public Color EngineTint { get; set; } = Colors.White;

    private static readonly Color BodyTintBase = new(1.35f, 1.4f, 1.55f);

    public float DashDistance { get; private set; } = 200.0f;
    public float DashTime { get; private set; } = 0.25f;
    public float DashCooldownMaxValue { get; private set; } = 4.0f;
    public float AfterimageInterval { get; private set; } = 0.08f;
    public float DashFuelRatio { get; private set; } = 0.25f;

    public float GrazeRadius { get; private set; } = 20.0f;
    public int GrazeScore { get; private set; } = 10;
    public float GrazeFlashTime { get; private set; } = 0.12f;
    private float _hitboxRadius = 2.8f;

    public float HomingTime { get; private set; } = 1.2f;
    private float _homingTurnRate = 5.5f;
    private float _aimStickFactor = 0.5f;
    private float _aimJoySpeed = 1400.0f;
    private float _coneAngleDeg = 6.0f;
    private float _coneCos = 0.9945f;
    private float _coneStrength = 0.45f;
    private float _magnetRange = 100.0f;
    private float _magnetStrength = 6.0f;
    private float _magnetMaxSpeed = 8.0f;
    private float _magnetInputMin = 2.0f;
    private float _magnetInputFull = 40.0f;
    private float _falloffPeak = 400.0f;
    private float _falloffEnd = 1400.0f;
    private float _falloffMin = 0.3f;
    private Vector2 _aimSmooth;
    private Vector2 _aimLastRaw;
    private ulong _aimSmoothedFrame = ulong.MaxValue;
    private bool _aimInitialized;
    public float FineMoveMult { get; private set; } = 0.35f;

    public float FuelMax { get; private set; } = 100.0f;
    private bool _inputLocked;
    public bool MovementLocked { get; set; }
    private float _enrageSlow = 1.0f;
    private bool _autoFireEnabled = true;

    // ---- A8：受击/回血与冲刺/弹反/视觉组件（组合委托，纯 C# 类） ----
    private readonly PlayerDamage _damage = new();
    private readonly PlayerDash _dash = new();
    private readonly PlayerParry _parry = new();
    private readonly PlayerVisuals _visuals = new();

    public float ParryArcDeg { get; private set; } = 360.0f;
    public float ParryRadius { get; private set; } = 60.0f;
    private Area2D? _parryShield;

    private float _fireCooldown;
    private int _soundIndex;
    private int _entryPhase;
    private float _entryRetreatLeft;
    private bool _entryPrevAutoFire = true;
    private Tween? _entryTween;

    // A8 转发（测试白盒语法兼容）
    public float Invincible { get => _damage.Invincible; set => _damage.Invincible = value; }
    public int LastHitFrame { get => _damage.LastHitFrame; set => _damage.LastHitFrame = value; }
    public float SinceDamage { get => _damage.SinceDamage; set => _damage.SinceDamage = value; }
    public bool Dashing { get => _dash.Dashing; set => _dash.Dashing = value; }
    public float DashTimer { get => _dash.DashTimer; set => _dash.DashTimer = value; }
    public Vector2 DashDir { get => _dash.DashDir; set => _dash.DashDir = value; }
    public float DashCooldown { get => _dash.DashCooldown; set => _dash.DashCooldown = value; }
    public float AfterimageTimer { get => _dash.AfterimageTimer; set => _dash.AfterimageTimer = value; }

    private bool _dead;
    private float _fuel = 100.0f;
    private bool _fuelLocked;

    public Vector2 AimPointOverride { get; set; } = new(float.PositiveInfinity, float.PositiveInfinity);
    private AimCrosshair? _crosshair;
    private float _muzzleOffset;
    private bool _boostToggleOn;
    private bool _fineToggleOn;

    // 迷雾事件效果状态（FogEventManager 信号驱动）
    private bool _fogInvertInput;
    private float _fogBulletJitterDeg;
    private float _fogMisfireChance;
    private float _fogIntervalJitter;
    private Vector2 _fogForcedDir;
    private float _fogForcedHold;

    private Sprite2D? _sprite;
    private AudioStreamPlayer2D? _audio;
    private Area2D? _hitbox;
    private GpuParticles2D? _thruster;

    private readonly Callable _onRefreshBuffFactors;
    private readonly Callable _onAimAssistLevelChanged;
    private readonly Callable _onJoySettingsChanged;
    private readonly Callable _onFogEventStarted;
    private readonly Callable _onFogEventEnded;
    private readonly Callable _onFogDirectionShift;
    private readonly Callable _onGrazeEntered;
    private readonly Callable _onParryShieldEntered;

    public Player()
    {
        _onRefreshBuffFactors = Callable.From(RefreshBuffFactors);
        _onAimAssistLevelChanged = Callable.From<StringName>(OnAimAssistLevelChanged);
        _onJoySettingsChanged = Callable.From<float, float>(OnJoySettingsChanged);
        _onFogEventStarted = Callable.From<string, float>(OnFogEventStarted);
        _onFogEventEnded = Callable.From<string>(OnFogEventEnded);
        _onFogDirectionShift = Callable.From<Vector2, float>(OnFogDirectionShift);
        _onGrazeEntered = Callable.From<Area2D>(OnGrazeEntered);
        _onParryShieldEntered = Callable.From<Area2D>(OnParryShieldEntered);
    }

    public override void _Ready()
    {
        GameState.Instance.PlayerRef = this;
        _hitbox = GetNode<Area2D>("Hitbox");
        GameState.Instance.PlayerHitbox = _hitbox;
        LoadBalance();
        RefreshBuffFactors();
        var gs = GameState.Instance;
        if (gs != null)
        {
            if (!gs.IsConnected("BuffsChanged", _onRefreshBuffFactors))
            {
                gs.Connect("BuffsChanged", _onRefreshBuffFactors);
            }

            if (!gs.IsConnected("JoySettingsChanged", _onJoySettingsChanged))
            {
                gs.Connect("JoySettingsChanged", _onJoySettingsChanged);
            }

            if (!gs.IsConnected("AimAssistChanged", _onAimAssistLevelChanged))
            {
                gs.Connect("AimAssistChanged", _onAimAssistLevelChanged);
            }

            // 迷雾事件：管理器信号驱动效果（解耦：Player 侧只应用）
            var fogEvents = gs.FogEvents;
            if (!fogEvents.IsConnected("FogEventStarted", _onFogEventStarted))
            {
                fogEvents.Connect("FogEventStarted", _onFogEventStarted);
            }

            if (!fogEvents.IsConnected("FogEventEnded", _onFogEventEnded))
            {
                fogEvents.Connect("FogEventEnded", _onFogEventEnded);
            }

            if (!fogEvents.IsConnected("FogDirectionShift", _onFogDirectionShift))
            {
                fogEvents.Connect("FogDirectionShift", _onFogDirectionShift);
            }
        }
    }

    /// <summary>A8：残影逐帧淡出（渲染帧）——委托 PlayerVisuals。</summary>
    public override void _Process(double delta)
    {
        _visuals.UpdateAfterimages((float)delta);
    }

    /// <summary>数值配置缓存（启动一次读入，避免每帧 Dictionary 路径查找）。</summary>
    private void LoadBalance()
    {
        // AC2（2026-08-11 审计）：运动/速度族钳 ≥0——负值致反向移动/反向加速
        MaxSpeed = CfgFx.Float("player.max_speed", MaxSpeed, 0.0f);
        Accel = CfgFx.Float("player.accel", Accel, 0.0f);
        Decel = CfgFx.Float("player.decel", Decel, 0.0f);
        BoostMult = CfgFx.Float("player.boost_mult", BoostMult, 0.0f);
        FineMoveMult = CfgFx.Float("player.fine_move_mult", FineMoveMult, 0.0f);
        // AC2：base_fire_interval 钳 0.05 下限（同 laser tick_interval 族）——≤0 时每物理帧开火
        BaseFireInterval = CfgFx.Float("player.base_fire_interval", BaseFireInterval, 0.05f);
        BulletSpeed = CfgFx.Float("player.bullet_speed", BulletSpeed, 0.0f);
        // AC2：crit_shot.chance 钳 [0,1]——>1 刀刀暴击；multiplier 钳 ≥0——负暴击倍数致回血
        CritChanceBase = CfgFx.Float("buffs.crit_shot.chance", CritChanceBase, 0.0f, 1.0f);
        CritMultiplier = CfgFx.Float("buffs.crit_shot.multiplier", CritMultiplier, 0.0f);
        BulletSpreadDeg = CfgFx.Float("player.bullet_spread_deg", BulletSpreadDeg, 0.0f);
        // AC2：bullet_damage 钳 ≥0（CfgFx.Int 统一判型 + 域钳）——负伤害给敌机回血
        BulletDamage = CfgFx.Int("player.bullet_damage", BulletDamage, 0);
        InvincibleTime = CfgFx.Float("player.invincible_time", InvincibleTime, 0.0f);
        SpawnInvincibleTime = CfgFx.Float("player.spawn_invincible_time", SpawnInvincibleTime, 0.0f);
        BulletClearRadius = CfgFx.Float("player.bullet_clear_radius", BulletClearRadius, 0.0f);
        // AC2：entry.* 钳 ≥0——负值入场时序/位移反向
        EntryLandRatio = CfgFx.Float("player.entry.land_ratio", EntryLandRatio, 0.0f);
        EntryRushTime = CfgFx.Float("player.entry.rush_time", EntryRushTime, 0.0f);
        EntryRetreatSpeed = CfgFx.Float("player.entry.retreat_speed", EntryRetreatSpeed, 0.0f);
        EntryRetreatTime = CfgFx.Float("player.entry.retreat_time", EntryRetreatTime, 0.0f);
        EntryInvincible = CfgFx.Float("player.entry.invincible", EntryInvincible, 0.0f);
        EntrySpawnClearance = CfgFx.Float("player.entry.spawn_clearance", EntrySpawnClearance, 0.0f);
        EntryRushHsRatio = CfgFx.Float("player.entry.rush_hspeed_ratio", EntryRushHsRatio, 0.0f);
        // AC2：armor.multiplier 钳 [0,1]——≤0 受击回血（GameState.Settings 乘算）；
        // evasion.chance 钳 [0,1]——≥1 软无敌；regen.heal_per_sec 钳 ≥0——负值逐秒扣血
        ArmorMult = CfgFx.Float("buffs.armor.multiplier", ArmorMult, 0.0f, 1.0f);
        EvasionChance = CfgFx.Float("buffs.evasion.chance", EvasionChance, 0.0f, 1.0f);
        RegenPerSec = CfgFx.Float("buffs.regen.heal_per_sec", RegenPerSec, 0.0f);
        ShakeHit = CfgFx.Float("effects.shake.player_hit", ShakeHit, 0.0f);
        Invincible = SpawnInvincibleTime; // 出生保护
        // 2026-08-10 健壮性审查：fuel.max 钳下限——0 时 FuelRatio() 的 _fuel/FuelMax 除零得 NaN
        //（燃料条显示 NaN；SetFuel 的 Clamp 上下界同为 0 致燃料机制失效）
        FuelMax = CfgFx.Float("player.fuel.max", FuelMax, 1.0f);
        _fuel = FuelMax;
        // AC2：fuel.drain/regen/restart 钳 ≥0——负值反转充能/消耗方向
        FuelDrain = CfgFx.Float("player.fuel.drain", FuelDrain, 0.0f);
        FuelRegen = CfgFx.Float("player.fuel.regen", FuelRegen, 0.0f);
        FuelRestart = CfgFx.Float("player.fuel.restart", FuelRestart, 0.0f);
        // AC2：dash.distance/fuel_ratio/afterimage_interval 钳 ≥0——负值冲刺反向
        DashDistance = CfgFx.Float("player.dash.distance", DashDistance, 0.0f);
        // V 系列：dash.time 钳 0.05 下限——0/负值时 UpdateMove 的 DashDistance/DashTime 除零得 inf → 位置 NaN
        DashTime = CfgFx.Float("player.dash.time", DashTime, 0.05f);
        // 2026-08-10 健壮性审查：dash.cooldown 钳 0.05 下限（与 fuel.max/dash.time 同族）——配 0
        // 且无 phase_dash 层数时 DashReadyRatio() 的 CooldownRemaining()/DashCooldownMax() = 0/0
        // = NaN（Mathf.Clamp 不拦 NaN），渗入 HUD 充能条
        DashCooldownMaxValue = CfgFx.Float("player.dash.cooldown", DashCooldownMaxValue, 0.05f);
        DashFuelRatio = CfgFx.Float("player.dash.fuel_ratio", DashFuelRatio, 0.0f);
        AfterimageInterval = CfgFx.Float("player.dash.afterimage_interval", AfterimageInterval, 0.0f);
        // AC2：graze_radius 钳 ≥0——负值擦弹环失效；graze_score 钳 ≥0——负分被连击乘区倒扣
        GrazeRadius = CfgFx.Float("player.graze_radius", GrazeRadius, 0.0f);
        GrazeScore = CfgFx.Int("player.graze_score", GrazeScore, 0);
        // AC2：parry.* 钳 ≥0——负半径/负角度致弹反扇形判定异常
        ParryArcDeg = CfgFx.Float("player.parry.arc_deg", ParryArcDeg, 0.0f);
        ParryRadius = CfgFx.Float("player.parry.radius", ParryRadius, 0.0f);
        _parry.Configure(
            CfgFx.Float("player.parry.duration", 0.8f, 0.0f),
            CfgFx.Float("player.parry.active_time", 0.5f, 0.0f),
            CfgFx.Float("player.parry.cooldown", 3.0f, 0.0f));
        _damage.Configure(InvincibleTime, ArmorMult, EvasionChance, RegenPerSec, ShakeHit);
        _dash.Configure(DashDistance, DashTime, DashCooldownMaxValue, AfterimageInterval);
        // AC2：aim_assist.input/falloff 钳 ≥0——负值磁吸力/衰减域反转
        _magnetInputMin = CfgFx.Float("player.aim_assist.input.magnet_input_min", _magnetInputMin, 0.0f);
        _magnetInputFull = CfgFx.Float("player.aim_assist.input.magnet_input_full", _magnetInputFull, 0.0f);
        _falloffPeak = CfgFx.Float("player.aim_assist.falloff.peak", _falloffPeak, 0.0f);
        _falloffEnd = CfgFx.Float("player.aim_assist.falloff.end", _falloffEnd, 0.0f);
        _falloffMin = CfgFx.Float("player.aim_assist.falloff.min", _falloffMin, 0.0f);
        LoadAimAssistParams();
        // 机体尺寸族：tscn 存设计值，统一乘全局缩放并幂等覆盖
        var ws = (float)GameState.Instance.WorldScale;
        _sprite = GetNode<Sprite2D>("Sprite2D");
        _sprite.Scale = Vector2.One * 0.65f * ws;
        if (GetNode<CollisionShape2D>("CollisionShape2D").Shape is CircleShape2D bodyCircle)
        {
            bodyCircle.Radius = 22.0f * ws;
        }

        _hitbox = GetNode<Area2D>("Hitbox");
        if (_hitbox.GetNode<CollisionShape2D>("CollisionShape2D").Shape is CircleShape2D hitCircle)
        {
            hitCircle.Radius = 7.0f * ws;
        }

        _hitboxRadius = 7.0f * ws;
        // 机制二：擦弹环（GrazeArea）——游戏性范围族运行值，不乘 world_scale
        var grazeArea = GetNode<Area2D>("GrazeArea");
        if (grazeArea.GetNode<CollisionShape2D>("CollisionShape2D").Shape is CircleShape2D grazeCircle)
        {
            grazeCircle.Radius = GrazeRadius;
        }

        grazeArea.Connect(Area2D.SignalName.AreaEntered, _onGrazeEntered);
        // 机制四：弹反盾——tscn 占位节点；圆盘 shape 触发进入检测，回调内精确扇形过滤
        _parryShield = GetNode<Area2D>("ParryShield");
        _parryShield.Connect(Area2D.SignalName.AreaEntered, _onParryShieldEntered);
        // 机制四：tscn 占位无 shape——新建圆盘判定形状（原 GDScript CircleShape2D.new() 语义）
        _parryShield.GetNode<CollisionShape2D>("CollisionShape2D").Shape = new CircleShape2D { Radius = ParryRadius };

        // 盾视觉三层（程序化，零 shader，全 ADD 混合出辉光）：淡金填充扇面 + 亮金分段盾缘
        // （7 枚独立能量格 Polygon2D 挂容器，段间留缝）+ 珍珠流光高光带
        var addBlend = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        var parryArc = new Polygon2D
        {
            Polygon = ParrySectorPoints(ParryRadius, 12),
            Color = new Color(1.0f, 0.8f, 0.3f, 0.13f),
            Material = addBlend,
            Visible = false,
        };
        AddChild(parryArc);
        var parryRim = new Node2D { Visible = false };
        // 段数随盾角：全周盾 14 格保持能量格密度，扇形（<360°）7 格
        foreach (var segVariant in ParryRimSegments(ParryRadius, ParryArcDeg >= 360.0f ? 14 : 7))
        {
            parryRim.AddChild(new Polygon2D
            {
                Polygon = segVariant.AsVector2Array(),
                Color = new Color(1.0f, 0.82f, 0.35f, 0.95f),
                Material = addBlend,
            });
        }

        AddChild(parryRim);
        var parryShine = new Polygon2D
        {
            Color = new Color(1.0f, 0.95f, 0.6f, 0.45f),
            Material = addBlend,
            Visible = false,
        };
        AddChild(parryShine);
        // 激活金光一闪：白金圆环自机体 0.45× 扩张到 1.5× 并淡出（0.32s，ACTIVE 入场一次性）
        var parryPulse = new Line2D
        {
            Points = CirclePoints(ParryRadius, 28),
            Closed = true,
            Width = 5.0f,
            DefaultColor = new Color(1.0f, 0.92f, 0.55f, 0.95f),
            Material = addBlend,
            Visible = false,
        };
        AddChild(parryPulse);
        _thruster = GetNode<GpuParticles2D>("Thruster");
        _thruster.Position = new Vector2(0.0f, 70.0f * ws);
        if (_thruster.ProcessMaterial is ParticleProcessMaterial thrusterMat)
        {
            thrusterMat.ScaleMin = 2.5f * ws;
            thrusterMat.ScaleMax = 5.5f * ws;
        }

        _muzzleOffset = 50.0f * ws;
        // 鼠标跟随准星（P1-1）：top_level 世界坐标节点
        _crosshair = new AimCrosshair();
        _crosshair.Init(this);
        AddChild(_crosshair);
        // 可视性增强：机体提亮 + 青色描边辉光
        _sprite.Modulate = BodyTintBase;
        var glow = new Sprite2D
        {
            Texture = _sprite.Texture,
            Scale = new Vector2(1.2f, 1.2f),
            Modulate = new Color(0.45f, 0.9f, 1.0f, 0.45f),
            ZIndex = -1,
        };
        _sprite.AddChild(glow);
        // 碰撞点指示：受击判定点闪烁小光点 + 淡色光圈
        var dotPts = new Vector2[10];
        for (var i = 0; i < 10; i++)
        {
            var a = Mathf.Tau * i / 10.0f;
            dotPts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 3.5f;
        }

        var hitboxDot = new Polygon2D { Polygon = dotPts, Color = new Color(0.65f, 0.95f, 1.0f) };
        AddChild(hitboxDot);
        var hitboxHalo = new Line2D { Width = 1.5f, DefaultColor = new Color(0.5f, 0.9f, 1.0f, 0.45f), Closed = true };
        for (var i = 0; i < 16; i++)
        {
            var a = Mathf.Tau * i / 16.0f;
            hitboxHalo.AddPoint(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 8.0f);
        }

        AddChild(hitboxHalo);
        // Buff 外观反馈附件（buffs_changed 信号驱动）
        var buffVisuals = new PlayerBuffVisuals
        {
            Scale = _sprite.Scale / PlayerBuffVisuals.BaseShipScale,
        };
        AddChild(buffVisuals);
        buffVisuals.Init(_sprite, this);
        // A8：视觉组件初始化（残影池预建；Main 场景构建期 add_child 报 busy，延迟到帧末）
        _visuals.Init(_sprite, _thruster, hitboxDot, parryArc, parryRim, parryShine, parryPulse, GetParent());
    }

    // ---------------- 对外公开接口（A1 修复） ----------------

    public bool IsDead() => _dead;

    public bool IsInputLocked() => _inputLocked;

    public void SetInvincible(float seconds) => _damage.SetInvincible(seconds);

    public float InvincibleRemaining() => _damage.InvincibleRemaining();

    public float EnrageSlow() => _enrageSlow;

    public void SetDead(bool dead) => _dead = dead;

    public void SetDashCooldown(float seconds) => _dash.DashCooldown = seconds;

    public void ResetCombatState()
    {
        _damage.LastHitFrame = -1;
        _damage.SinceDamage = 999.0f;
    }

    public void SetSinceDamage(float seconds) => _damage.SinceDamage = seconds;

    public void SetLastHitFrame(int frame) => _damage.LastHitFrame = frame;

    public float DashCooldownRemaining() => _dash.CooldownRemaining();

    public float SinceDamageValue() => _damage.SinceDamage;

    public void Fire(Vector2 aim) => FireInternal(aim);

    public void ResetFireCooldown() => _fireCooldown = 0.0f;

    public bool FogInvertActive() => _fogInvertInput;

    public float FogBulletJitter() => _fogBulletJitterDeg;

    public float FogMisfireChance() => _fogMisfireChance;

    public Vector2 FogForcedDir() => _fogForcedDir;

    public float FogForcedHold() => _fogForcedHold;

    private void OnFogEventStarted(string eventId, float duration)
    {
        if (eventId == "mental_confusion")
        {
            _fogInvertInput = true;
        }
        else if (eventId == "bullet_malfunction")
        {
            _fogBulletJitterDeg = (float)GameState.Instance.Cfg("fog_events.bullet_malfunction.jitter_deg", 20.0).AsDouble();
            _fogMisfireChance = (float)GameState.Instance.Cfg("fog_events.bullet_malfunction.misfire_chance", 0.15).AsDouble();
            _fogIntervalJitter = (float)GameState.Instance.Cfg("fog_events.bullet_malfunction.interval_jitter", 0.3).AsDouble();
        }
    }

    private void OnFogEventEnded(string eventId)
    {
        if (eventId == "mental_confusion")
        {
            _fogInvertInput = false;
        }
        else if (eventId == "bullet_malfunction")
        {
            _fogBulletJitterDeg = 0.0f;
            _fogMisfireChance = 0.0f;
            _fogIntervalJitter = 0.0f;
        }
        else if (eventId == "direction_shift")
        {
            _fogForcedHold = 0.0f;
        }
    }

    /// <summary>短间隔随机方向脉冲：hold 秒内移动向量被替换为 dir。</summary>
    private void OnFogDirectionShift(Vector2 dir, float hold)
    {
        _fogForcedDir = dir;
        _fogForcedHold = Mathf.Max(hold, 0.0f);
    }

    public bool BoostToggleActive() => _boostToggleOn;

    public bool FineToggleActive() => _fineToggleOn;

    public void SetBoostToggle(bool enabled) => _boostToggleOn = enabled;

    public void SetFineToggle(bool enabled) => _fineToggleOn = enabled;

    public Godot.Collections.Dictionary AimAssistParams() => new()
    {
        ["homing_turn_rate"] = _homingTurnRate,
        ["stick_factor"] = _aimStickFactor,
        ["magnet_range"] = _magnetRange,
        ["magnet_strength"] = _magnetStrength,
        ["magnet_max_speed"] = _magnetMaxSpeed,
        ["magnet_input_min"] = _magnetInputMin,
        ["magnet_input_full"] = _magnetInputFull,
        ["cone_angle_deg"] = _coneAngleDeg,
        ["cone_strength"] = _coneStrength,
        ["falloff_peak"] = _falloffPeak,
        ["falloff_end"] = _falloffEnd,
        ["falloff_min"] = _falloffMin,
    };

    /// <summary>P1-3 距离衰减曲线（公开供测试；_fire 弱追踪与 AimFrameLayer 磁吸共用）。</summary>
    public float AimDistFalloff(float d) => DistFalloffCurve(d, _falloffPeak, _falloffEnd, _falloffMin);

    /// <summary>G018：距离衰减分段纯函数（单实现）。</summary>
    public static float DistFalloffCurve(float d, float peak, float end, float minV)
    {
        if (d <= peak)
        {
            return 1.0f;
        }

        if (d >= end)
        {
            return minV;
        }

        return Mathf.Lerp(1.0f, minV, (d - peak) / (end - peak));
    }

    public bool HitboxEnabled() => _hitbox != null && _hitbox.Monitoring;

    public void LockInput() => _inputLocked = true;

    public void UnlockInput() => _inputLocked = false;

    public void SetFuel(float value) => _fuel = Mathf.Clamp(value, 0.0f, FuelMax);

    public float FuelAmount() => _fuel;

    public void Die() => DieInternal();

    public void ApplyEnrageSlow(float factor) => _enrageSlow = factor;

    public void SetAutoFire(bool enabled) => _autoFireEnabled = enabled;

    /// <summary>AB1：入场序列期间外部系统（LaserWeapon.EndBeam）恢复 autofire 时同步覆盖捕获值，
    /// 防 FinishEntry 把激光恢复的 true 踩回 false（返航暂停冻结激光 active 的孪生路径）。</summary>
    public void OverrideEntryAutoFire(bool value)
    {
        if (_entryPhase != 0)
        {
            _entryPrevAutoFire = value;
        }
    }

    public bool AutoFireEnabled() => _autoFireEnabled;

    public bool IsDashing() => _dash.IsDashing();

    /// <summary>A4：按声明式效果表刷新 buff 值缓存（_ready 初始 + buffs_changed 信号驱动）。</summary>
    private void RefreshBuffFactors()
    {
        foreach (var id in BuffEffects.Keys)
        {
            var effect = (Godot.Collections.Dictionary)BuffEffects[id];
            var kind = (string)(StringName)effect["kind"];
            if (kind == "bool")
            {
                continue;
            }

            var value = GameState.Instance.Cfg((StringName)effect["cfg"], effect["default"]);
            _buffValues[id] = kind == "cap" ? (int)value.AsInt64() : (float)value.AsDouble();
        }

        var critStacks = (int)GameState.Instance.BuffCount(BuffCritShot);
        CritChance = critStacks == 0 ? 0.0f : CritChanceBase * critStacks;
        CritMultiplierValue = CritMultiplier;
        // 2026-08-10 审计 H6：燃油速率缓存（LaserWeapon.OnBuffsChanged 同款）——
        // 原每物理帧 BuffCount 字典查找 + Pow（_physics_process 每帧两次）。
        // 2026-08-16 扩展：开火/冲刺路径同口径缓存，空间换时间（见字段注释）。
        _fuelDrainRate = BuffScale(BuffEfficientBoost, FuelDrain, (int)GameState.Instance.BuffCount(BuffEfficientBoost));
        _fuelRegenRate = BuffScale(BuffBoostRecovery, FuelRegen, (int)GameState.Instance.BuffCount(BuffBoostRecovery));
        _fireIntervalValue = BuffScale(BuffRapidFire, BaseFireInterval, (int)GameState.Instance.BuffCount(BuffRapidFire));
        _bulletDamageValue = Mathf.Max(1, (int)BuffScale(BuffPowerShot, BulletDamage, (int)GameState.Instance.BuffCount(BuffPowerShot)));
        _bulletSpeedValue = BuffScale(BuffBulletSpeed, BulletSpeed, (int)GameState.Instance.BuffCount(BuffBulletSpeed));
        _spreadShotCount = BuffCap(BuffSpreadShot);
        _pierceCount = BuffCap(BuffPiercing);
        _explosiveEnabled = BuffEnabled(BuffExplosive);
        var dashStacks = (int)GameState.Instance.BuffCount(BuffPhaseDash);
        _dashUnlocked = dashStacks > 0;
        _dashCooldownMax = BuffScale(BuffPhaseDash, DashCooldownMaxValue, Mathf.Max(dashStacks - 1, 0));
    }

    /// <summary>A4：乘算因子求值——base × factor^count。</summary>
    private float BuffScale(StringName id, float baseValue, int count) => baseValue * Mathf.Pow((float)_buffValues[id].AsDouble(), count);

    /// <summary>A4：堆叠上限截断——min(count, max_stacks)。</summary>
    private int BuffCap(StringName id) => Mathf.Min((int)GameState.Instance.BuffCount(id), (int)_buffValues[id]);

    /// <summary>A4：布尔启用——count &gt; 0。</summary>
    private bool BuffEnabled(StringName id) => (int)GameState.Instance.BuffCount(id) > 0;

    public float FireIntervalValue() => _fireIntervalValue;

    public int BulletDamageValue() => _bulletDamageValue;

    /// <summary>bullet_speed buff 后的当前弹速（buffs_changed 时缓存）。</summary>
    public float BulletSpeedValue() => _bulletSpeedValue;

    public float FuelRatio() => _fuel / FuelMax;

    public void RefillFuel()
    {
        _fuel = FuelMax;
        _fuelLocked = false;
    }

    public bool DashUnlocked() => _dashUnlocked;

    public float DashCooldownMax() => _dashCooldownMax;

    public float DashFuelCost() => FuelMax * DashFuelRatio;

    public float DashReadyRatio()
    {
        if (!DashUnlocked())
        {
            return 0.0f;
        }

        return 1.0f - Mathf.Clamp(_dash.CooldownRemaining() / DashCooldownMax(), 0.0f, 1.0f);
    }

    /// <summary>H6：燃油速率缓存（RefreshBuffFactors 刷新：_ready 初始 + buffs_changed 信号驱动；
    /// 默认值 = 无 buff 时的 FuelDrain/FuelRegen 脚本默认，直实例化未 _ready 路径语义不变）。</summary>
    private float _fuelDrainRate = 35.0f;
    private float _fuelRegenRate = 20.0f;

    /// <summary>空间换时间：射速/伤害/弹速/冲刺解锁与冲刺冷却上限随 buff 变化一次性缓存，
    /// 避免 _PhysicsProcess 每帧与每发 Fire 调用 BuffCount 字典查找 + Pow。</summary>
    private float _fireIntervalValue = 0.15f;
    private int _bulletDamageValue = 10;
    private float _bulletSpeedValue = 1800.0f;
    private int _spreadShotCount;
    private int _pierceCount;
    private bool _explosiveEnabled;
    private bool _dashUnlocked;
    private float _dashCooldownMax = 4.0f;

    public float FuelDrainRate() => _fuelDrainRate;

    public float FuelRegenRate() => _fuelRegenRate;

    public override void _PhysicsProcess(double delta)
    {
        var d = (float)delta;
        // 帧首缓存 GameState 门面：本方法多次读取设置域，避免重复 Instance 判活/根节点查询。
        var gs = GameState.Instance;
        // A2(2026-08-11 审计):同帧双 GetTicksMsec 合并——帧首取一次,761/866 行复用(免同帧两次系统时钟查询)
        var nowMs = (long)Time.GetTicksMsec();
        if (_dead)
        {
            return;
        }

        if (_inputLocked)
        {
            // R07：锁输入期关闭弹反盾物理判定（原实现整体早退使 monitoring 停留在锁定前值）
            if (_parryShield != null && _parryShield.Monitoring)
            {
                _parryShield.Monitoring = false;
            }

            return;
        }

        if (_entryPhase != 0)
        {
            EntryPhysics(d);
            return;
        }

        var inputDir = Input.GetVector(ActMoveLeft, ActMoveRight, ActMoveUp, ActMoveDown);
        if (_fogInvertInput)
        {
            inputDir = -inputDir;
        }

        if (_fogForcedHold > 0.0f)
        {
            _fogForcedHold -= d;
            inputDir = _fogForcedDir;
        }

        if (MovementLocked)
        {
            inputDir = Vector2.Zero;
            Velocity = Vector2.Zero;
            Dashing = false;
        }

        _dash.TickCooldown(d);
        _parry.Tick(d);
        if (Input.IsActionJustPressed(ActParry))
        {
            _parry.TryStart();
        }

        var shieldOn = _parry.Phase == PlayerParry.ParryPhase.ACTIVE;
        if (_parryShield != null && _parryShield.Monitoring != shieldOn)
        {
            _parryShield.Monitoring = shieldOn;
            if (shieldOn)
            {
                _visuals.SetParryActivatePulse(); // 金光一闪：进入有效窗口瞬间的入场闪光环
            }
        }

        _visuals.UpdateParryVisuals(_parry.ShieldExpand(), _parry.ShineProgress(), ParryRadius, ParryArcDeg, d, nowMs);
        if (DashUnlocked()
            && !MovementLocked
            && Input.IsActionJustPressed(ActDash)
            && _dash.CooldownRemaining() <= 0.0f
            && !_dash.IsDashing()
            && _fuel >= DashFuelCost())
        {
            _dash.Start(inputDir, this);
        }

        if (_dash.IsDashing())
        {
            _dash.UpdateMove(d, this);
            _visuals.SetThruster(1.7f, 1.0f, 1.0f, EngineTint);
            return;
        }

        // 燃料与加速（shift_toggle_mode：按一下切换开/关）
        if ((bool)gs.ShiftToggleMode && Input.IsActionJustPressed(ActBoost))
        {
            _boostToggleOn = !_boostToggleOn;
        }

        var wantBoost = (bool)gs.ShiftToggleMode ? _boostToggleOn : Input.IsActionPressed(ActBoost);
        if (MovementLocked)
        {
            wantBoost = false;
        }

        if (_fuelLocked && _fuel >= FuelRestart)
        {
            _fuelLocked = false;
        }

        var boosting = wantBoost && !_fuelLocked && _fuel > 0.0f;
        if (boosting)
        {
            _fuel = Mathf.Max(_fuel - FuelDrainRate() * d, 0.0f);
            if (_fuel <= 0.0f)
            {
                _fuelLocked = true;
            }
        }
        else
        {
            _fuel = Mathf.Min(_fuel + FuelRegenRate() * d, FuelMax);
        }

        var boost = boosting ? BoostMult : 1.0f;
        // Ctrl 微调：移速 ×0.35（ctrl_toggle_mode：按一下切换开/关）
        if ((bool)gs.CtrlToggleMode && Input.IsActionJustPressed(ActFineMove))
        {
            _fineToggleOn = !_fineToggleOn;
        }

        var fineOn = (bool)gs.CtrlToggleMode ? _fineToggleOn : Input.IsActionPressed(ActFineMove);
        var fine = fineOn ? FineMoveMult : 1.0f;
        var target = inputDir * MaxSpeed * boost * fine * _enrageSlow;
        var rate = inputDir != Vector2.Zero ? Accel : Decel;
        Velocity = Velocity.MoveToward(target, rate * d);
        MoveAndSlide();
        Position = ClampToView(Position, gs.ViewWorldRect());

        if (boosting && inputDir != Vector2.Zero)
        {
            _visuals.SetThruster(1.7f, 1.0f, 1.0f, EngineTint);
        }
        else if (inputDir != Vector2.Zero)
        {
            _visuals.SetThruster(1.0f, 0.8f, 0.85f, EngineTint);
        }
        else
        {
            _visuals.SetThruster(0.6f, 0.35f, 0.6f, EngineTint);
        }

        var aim = AimPoint() - GlobalPosition;
        if (aim.Length() > 1.0f)
        {
            // 贴图机头朝上，需 +90° 偏移
            Rotation = aim.Angle() + Mathf.Pi / 2.0f;
        }

        _fireCooldown -= d;
        if (_autoFireEnabled && _fireCooldown <= 0.0f && aim.Length() > 1.0f)
        {
            FireInternal(aim.Normalized());
            var interval = FireIntervalValue();
            // 迷雾事件·子弹错误：开火间隔随机扰动
            if (_fogIntervalJitter > 0.0f)
            {
                interval *= (float)GD.RandRange(1.0 - _fogIntervalJitter, 1.0 + _fogIntervalJitter);
            }

            _fireCooldown = Mathf.Max(interval, 0.01f);
        }

        // 机身色调四源 + 受击点脉动（A8 委托 PlayerVisuals）
        if (Invincible > 0.0f)
        {
            Invincible -= d;
        }

        _visuals.UpdateFrame(d, _parry.TintStrength(), Invincible, nowMs);
        // 回血（A8 委托 PlayerDamage）
        _damage.HealTick(d);
    }

    /// <summary>屏幕边缘钳制：随可见世界区域收窄。</summary>
    public Vector2 ClampToView(Vector2 p) => ClampToView(p, GameState.Instance.ViewWorldRect());

    /// <summary>给定视野的屏幕边缘钳制（_PhysicsProcess 复用帧内已取视野，免重复 Instance/ViewWorldRect）。</summary>
    private static Vector2 ClampToView(Vector2 p, Rect2 view)
    {
        return p.Clamp(view.Position + new Vector2(40.0f, 40.0f), view.End - new Vector2(40.0f, 40.0f));
    }

    /// <summary>当前瞄准点（世界坐标）：测试注入点优先，否则平滑鼠标位置（每渲染帧推进一次）。</summary>
    public Vector2 AimPoint()
    {
        if (AimPointOverride != new Vector2(float.PositiveInfinity, float.PositiveInfinity))
        {
            return AimPointOverride;
        }

        var frame = Engine.GetProcessFrames();
        if (frame != _aimSmoothedFrame)
        {
            _aimSmoothedFrame = frame;
            var raw = GetGlobalMousePosition();
            // U14：VirtualControls 已 C#，typed 直调（原每渲染帧动态派发 + Variant 装箱）
            var vc = GameState.Instance.VirtualControls as VirtualControls;
            if (vc != null && vc.IsEnabled())
            {
                raw = vc.BaseAimPosition();
            }

            // H01：右摇杆虚拟准星（四向独立动作，差值驱动）
            var joy = Input.GetVector(ActAimLeft, ActAimRight, ActAimUp, ActAimDown);
            if (joy.LengthSquared() > 0.01f)
            {
                raw += joy * _aimJoySpeed * (float)GetProcessDeltaTime();
            }

            var factor = 1.0f;
            var magnet = Vector2.Zero;
            if (_aimInitialized && GameState.Instance.AimFrameLayer != null)
            {
                var aimLayer = (AimFrameLayer)GameState.Instance.AimFrameLayer;
                var sticky = aimLayer.MarkedTargetAt(_aimSmooth);
                if (sticky != null)
                {
                    factor = _aimStickFactor;
                }
                else
                {
                    magnet = aimLayer.MagnetPull(_aimSmooth, raw - _aimLastRaw);
                }
            }

            _aimSmooth = !_aimInitialized ? raw : _aimSmooth + (raw - _aimLastRaw) * factor + magnet;
            _aimLastRaw = raw;
            _aimInitialized = true;
        }

        return _aimSmooth;
    }

    /// <summary>读取当前强度档位参数（balance.json player.aim_assist.levels.&lt;level&gt;）。</summary>
    private void LoadAimAssistParams()
    {
        var level = (string)(StringName)GameState.Instance.AimAssistLevel;
        var basePath = "player.aim_assist.levels." + level + ".";
        // AC2（2026-08-11 审计）：档位参数钳 ≥0——负值致追踪/磁吸反向
        _homingTurnRate = Mathf.Max((float)GameState.Instance.Cfg(basePath + "homing_turn_rate", _homingTurnRate).AsDouble(), 0.0f);
        _aimStickFactor = Mathf.Max((float)GameState.Instance.Cfg(basePath + "stick_factor", _aimStickFactor).AsDouble(), 0.0f);
        HomingTime = Mathf.Max((float)GameState.Instance.Cfg("player.aim_assist.homing_time", HomingTime).AsDouble(), 0.0f);
        // AC2：cone_angle_deg 钳 [0,360]——越界角度（负/超 360）致 coneCos 周期折叠，
        // 锥形弱追踪判定失真（360 时 cos=1 → angT 0/0=NaN，AC6 NaN 守卫兜底）
        _coneAngleDeg = Mathf.Clamp((float)GameState.Instance.Cfg(basePath + "cone_angle_deg", _coneAngleDeg).AsDouble(), 0.0f, 360.0f);
        _coneCos = Mathf.Cos(Mathf.DegToRad(_coneAngleDeg));
        _coneStrength = Mathf.Max((float)GameState.Instance.Cfg(basePath + "cone_strength", _coneStrength).AsDouble(), 0.0f);
        _magnetRange = Mathf.Max((float)GameState.Instance.Cfg(basePath + "magnet_range", _magnetRange).AsDouble(), 0.0f);
        _magnetStrength = Mathf.Max((float)GameState.Instance.Cfg(basePath + "magnet_strength", _magnetStrength).AsDouble(), 0.0f);
        _magnetMaxSpeed = Mathf.Max((float)GameState.Instance.Cfg(basePath + "magnet_max_speed", _magnetMaxSpeed).AsDouble(), 0.0f);
    }

    private void OnAimAssistLevelChanged(StringName level) => LoadAimAssistParams();

    /// <summary>P0-1：手柄设置变更（右摇杆灵敏度）重读。</summary>
    private void OnJoySettingsChanged(float aimSpeed, float deadzone) => _aimJoySpeed = aimSpeed;

    /// <summary>A8：冲刺残影公开入口——委托 PlayerVisuals 池化生成。</summary>
    public void SpawnAfterimage() => _visuals.SpawnAfterimage(_sprite!.Texture, _sprite.Scale, GlobalPosition, Rotation);

    /// <summary>入场动画（开场/返航继续出击后由 main 调用）。</summary>
    public void PlayEntryAnimation()
    {
        if (_entryPhase != 0 || _dead)
        {
            return;
        }

        var rect = GameState.Instance.ViewWorldRect();
        var landY = rect.Position.Y + rect.Size.Y * EntryLandRatio;
        _entryPhase = 1;
        _entryRetreatLeft = EntryRetreatTime;
        SetInvincible(EntryInvincible);
        Velocity = Vector2.Zero;
        Dashing = false;
        _entryPrevAutoFire = _autoFireEnabled;
        _autoFireEnabled = false;
        Position = new Vector2(rect.GetCenter().X, rect.End.Y + EntrySpawnClearance);
        _visuals.SetThruster(2.0f, 1.0f, 1.0f, EngineTint);
        _entryTween = CreateTween();
        _entryTween.TweenProperty(this, "position:y", landY, EntryRushTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _entryTween.TweenCallback(Callable.From(OnEntryLanded));
    }

    /// <summary>入场动画进行中（main/测试查询）。</summary>
    public bool IsEntryPlaying() => _entryPhase != 0;

    /// <summary>中断入场动画（返航/自毁等流程接管时调用）：复位状态机并静默收尾。</summary>
    public void AbortEntry()
    {
        if (_entryPhase == 0)
        {
            return;
        }

        _entryPhase = 0;
        _autoFireEnabled = _entryPrevAutoFire;
        if (_entryTween != null && _entryTween.IsValid())
        {
            _entryTween.Kill();
            _entryTween = null;
        }

        Velocity = Vector2.Zero;
        if (_thruster != null)
        {
            _thruster.SpeedScale = 1.0f;
            _thruster.AmountRatio = 0.35f;
        }
    }

    private void OnEntryLanded()
    {
        if (_entryPhase == 1)
        {
            _entryPhase = 2;
        }
    }

    /// <summary>入场物理分支：阶段 1 由 tween 驱动不吃输入；阶段 2 仅左右可调。</summary>
    private void EntryPhysics(float delta)
    {
        if (Invincible > 0.0f)
        {
            Invincible -= delta;
        }

        if (_entryPhase == 1)
        {
            return;
        }

        var inputX = Input.GetAxis(ActMoveLeft, ActMoveRight);
        Velocity = new Vector2(inputX * MaxSpeed * EntryRushHsRatio, EntryRetreatSpeed);
        MoveAndSlide();
        Position = ClampToView(Position);
        _entryRetreatLeft -= delta;
        if (_thruster != null)
        {
            _thruster.SpeedScale = Mathf.MoveToward(_thruster.SpeedScale, 0.6f, delta * 2.0f);
            _thruster.AmountRatio = Mathf.MoveToward(_thruster.AmountRatio, 0.35f, delta * 1.2f);
        }

        if (_entryRetreatLeft <= 0.0f)
        {
            FinishEntry();
        }
    }

    private void FinishEntry()
    {
        _entryPhase = 0;
        _autoFireEnabled = _entryPrevAutoFire;
        Velocity = Vector2.Zero;
        EmitSignal(SignalName.EntryFinished);
    }

    private void FireInternal(Vector2 aim)
    {
        var spread = _spreadShotCount;
        var pierce = _pierceCount;
        var explosive = _explosiveEnabled;
        var gs = GameState.Instance;
        // 辅助瞄准（P1-1/P1-3）：准星在某标记敌框内 → 追踪修正；框外锥内 → 弱追踪
        Enemy? homingTarget = null;
        var homingRate = _homingTurnRate;
        if (gs.AimFrameLayer is AimFrameLayer aimLayer)
        {
            homingTarget = aimLayer.MarkedTargetAt(AimPoint());
            if (homingTarget == null)
            {
                var aimDir = aim.Normalized();
                homingTarget = aimLayer.NearestConeTarget(GlobalPosition, aimDir, _coneCos);
                if (homingTarget != null)
                {
                    var dot = aimDir.Dot((homingTarget.GlobalPosition - GlobalPosition).Normalized());
                    var angT = Mathf.Clamp((dot - _coneCos) / (1.0f - _coneCos), 0.0f, 1.0f);
                    homingRate = _homingTurnRate * _coneStrength * angT
                        * AimDistFalloff(GlobalPosition.DistanceTo(homingTarget.GlobalPosition));
                    // AC6（2026-08-11 审计）：NaN 守卫——cone_angle_deg=360 时 _coneCos=1 使 angT 0/0 得
                    // NaN，homingRate=NaN 恒不满足 ≤0 守卫（NaN 比较 false），弱追踪修正失控（Z 批次同族写法）
                    if (homingRate <= 0.0f || float.IsNaN(homingRate))
                    {
                        homingTarget = null;
                    }
                }
            }
        }

        // 散射弹道数恒为奇数（1/3/5，每层 +2）：偶数弹数扇形无中心弹（准星方向落空 = 负提升），
        // 居中索引即层数（spread=1→3 弹 [-1,0,+1]，spread=2→5 弹 [-2..+2]）
        var count = 1 + spread * 2;
        // P1-2：循环不变量外提；buff 变化时缓存，开火路径零字典/Pow。
        var loopSpeed = BulletSpeedValue();
        var loopDamage = BulletDamageValue();
        for (var i = 0; i < count; i++)
        {
            var offset = Mathf.DegToRad(BulletSpreadDeg * (i - spread));
            var aimRot = aim.Rotated(offset);
            var bspeed = loopSpeed;
            // 迷雾事件·子弹错误：随机角度偏移 + 偶发慢速失误弹
            if (_fogBulletJitterDeg > 0.0f)
            {
                aimRot = aimRot.Rotated(Mathf.DegToRad((float)GD.RandRange(-_fogBulletJitterDeg, _fogBulletJitterDeg)));
            }

            if (_fogMisfireChance > 0.0f && GD.Randf() < _fogMisfireChance)
            {
                bspeed *= 0.45f;
            }

            var pool = gs.BulletPool as BulletPool;
            if (pool == null)
            {
                continue;
            }

            var b = pool.Fire(aimRot, bspeed, loopDamage, true);
            if (b == null)
            {
                continue;
            }

            b.Pierce = pierce;
            b.Explosive = explosive;
            if (homingTarget != null)
            {
                b.HomingTarget = homingTarget;
                b.HomingTime = HomingTime;
                b.HomingTurnRate = homingRate;
            }

            b.Position = Position + aimRot * _muzzleOffset;
        }

        _audio ??= GetNodeOrNull<AudioStreamPlayer2D>("AudioStreamPlayer2D");
        if (_audio != null)
        {
            _audio.Stream = _fireSounds[_soundIndex];
            _soundIndex = (_soundIndex + 1) % _fireSounds.Length;
            _audio.Play();
        }
    }

    /// <summary>受击结算（100 HP 制）。返回 true = 本帧实际结算。A8 委托 PlayerDamage。
    /// U15：单参重载默认 Vector2.Inf（原 default(Zero) 与 GDScript INF 语义漂移——C# 默认
    /// 参数须编译期常量，Vector2.Inf 非常量，拆重载保留"无方向均匀环"语义）。</summary>
    public bool TakeDamage(float amount = 1.0f) => TakeDamage(amount, Vector2.Inf);

    public bool TakeDamage(float amount, Vector2 fromPos)
    {
        return _damage.TakeDamage(amount, fromPos, this);
    }

    /// <summary>受击连锁：清除 250px 内全部敌弹（无分无特效）。</summary>
    public void ClearNearbyEnemyBullets()
    {
        var bullets = GameState.Instance.EnemyBullets;
        var clearRadiusSq = BulletClearRadius * BulletClearRadius; // 2026-08-10 审计 H5：平方距离比较免每弹 sqrt
        for (var i = bullets.Count - 1; i >= 0; i--)
        {
            var b = (Bullet?)bullets[i];
            if (b != null && !b.IsPlayerBullet)
            {
                if (b.GlobalPosition.DistanceSquaredTo(GlobalPosition) <= clearRadiusSq)
                {
                    b.Despawn();
                }
            }
        }
    }

    /// <summary>机制二：擦弹——敌弹进入 GrazeArea（受击盒外环形带）计 1 次分。</summary>
    private void OnGrazeEntered(Area2D area)
    {
        var b = area.GetScript().AsGodotObject() == _bulletScript ? (Bullet)area : null;
        if (b == null || b.IsPlayerBullet || !b.IsActive())
        {
            return;
        }

        if (GlobalPosition.DistanceTo(area.GlobalPosition) <= _hitboxRadius
            + Bullet.GetCollisionRadius() * (float)GameState.Instance.WorldScale)
        {
            return;
        }

        if (!b.TryGraze())
        {
            return;
        }

        GameState.Instance.AddScore(GrazeScore);
        _visuals.SetGrazeFlash(GrazeFlashTime);
        Explosion.SpawnAt(GetParent(), GlobalPosition, 0.25f);
        GameState.Instance.PlaySfx(GameState.Instance.SFX_BUFF_PICK, -8.0);
    }

    /// <summary>机制四：弹反盾公开接口（测试/诊断与 HUD 读取）。</summary>
    public bool TryParry() => _parry.TryStart();

    public int ParryPhase() => (int)_parry.Phase;

    public float ParryEnergyRatio() => _parry.EnergyRatio();

    public float ParryCooldownRemaining() => _parry.CooldownRemaining();

    /// <summary>盾区弹反：圆盘 shape 触发进入检测后径向距离过滤（360° 全周盾，arc_deg 配置保留
    /// 角度过滤能力——&lt;360 时回退为机头前方扇形），O(1) 阵营翻转。</summary>
    private void OnParryShieldEntered(Area2D area)
    {
        if (_parry.Phase != PlayerParry.ParryPhase.ACTIVE)
        {
            return;
        }

        var b = area.GetScript().AsGodotObject() == _bulletScript ? (Bullet)area : null;
        if (b == null || b.IsPlayerBullet)
        {
            return;
        }

        var rel = area.GlobalPosition - GlobalPosition;
        if (rel.Length() > ParryRadius)
        {
            return;
        }

        var arc = Mathf.DegToRad(ParryArcDeg) * 0.5f;
        // 2026-08-10 健壮性审查：过滤基准改机头方向（含机身 Rotation）——原 -π/2 全局上方在
        // arc_deg<360 时过滤轴与机头垂直，与「机头前方扇形」矛盾；AngleDifference 已处理 ±π wrap
        var noseAngle = Vector2.Up.Rotated(Rotation).Angle();
        if (Mathf.Abs(Mathf.AngleDifference(rel.Angle(), noseAngle)) > arc)
        {
            return;
        }

        b.Reflect();
        _visuals.SetParryFlash();
        Explosion.SpawnAt(GetParent(), area.GlobalPosition, 0.5f);
        GameState.Instance.PlaySfx(GameState.Instance.SFX_DASH, -6.0);
    }

    /// <summary>盾扇区顶点（机头前方 ±arc，朝上）：圆心 + 弧上 count+1 点。</summary>
    private Vector2[] ParrySectorPoints(float radius, int count)
    {
        var arc = Mathf.DegToRad(ParryArcDeg) * 0.5f;
        var pts = new Vector2[count + 2];
        pts[0] = Vector2.Zero;
        for (var i = 0; i <= count; i++)
        {
            var a = -Mathf.Pi / 2.0f + arc - (2.0f * arc) * i / (float)count;
            pts[i + 1] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        return pts;
    }

    /// <summary>闭合圆环顶点（激活闪光环 Line2D 用）：count 等分整圆。</summary>
    private static Vector2[] CirclePoints(float radius, int count)
    {
        var pts = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            var a = -Mathf.Pi / 2.0f - Mathf.Tau * i / count;
            pts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        return pts;
    }

    /// <summary>盾缘分段顶点（以机头上方为中的 ±arc 环形亮边，360° 即整圆；count 段伪能量格、段间留缝）：
    /// 每段一个四边形（内弧两点 + 外弧两点），逐段子节点一次构建（热路径零分配）。</summary>
    private Godot.Collections.Array ParryRimSegments(float radius, int count)
    {
        var arc = Mathf.DegToRad(ParryArcDeg) * 0.5f;
        var segs = new Godot.Collections.Array();
        const float GapRatio = 0.22f; // 段间缝隙占段宽比例
        var inner = radius * 0.80f;
        for (var i = 0; i < count; i++)
        {
            var t0 = (float)i / count;
            var t1 = (float)(i + 1) / count;
            var pad = (t1 - t0) * GapRatio * 0.5f;
            var a0 = -Mathf.Pi / 2.0f - arc + 2.0f * arc * (t0 + pad);
            var a1 = -Mathf.Pi / 2.0f - arc + 2.0f * arc * (t1 - pad);
            segs.Add(new Vector2[]
            {
                new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * inner,
                new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius,
                new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius,
                new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * inner,
            });
        }

        return segs;
    }

    private void DieInternal()
    {
        _dead = true;
        AbortEntry(); // D06：入场期间自毁复位入场状态机
        _enrageSlow = 1.0f; // 死亡/重生路径兜底
        Hide();
        if (_hitbox != null)
        {
            _hitbox.SetDeferred("monitoring", false);
        }

        // L03/K03：死亡路径关闭擦弹环与弹反盾判定
        var grazeArea = GetNodeOrNull<Area2D>("GrazeArea");
        if (grazeArea != null)
        {
            grazeArea.Monitoring = false;
        }

        if (_parryShield != null)
        {
            _parryShield.Monitoring = false;
        }

        SetPhysicsProcess(false);
        Explosion.SpawnAt(GetParent(), Position, 2.0f);
    }

    /// <summary>进入母舰保护舱（召唤回收）：隐藏机体 + 关闭受击判定，不置 _dead。</summary>
    public void EnterPod()
    {
        Hide();
        if (_hitbox != null)
        {
            _hitbox.SetDeferred("monitoring", false);
        }

        var grazeArea = GetNodeOrNull<Area2D>("GrazeArea");
        if (grazeArea != null)
        {
            grazeArea.Monitoring = false;
        }

        if (_parryShield != null)
        {
            _parryShield.Monitoring = false;
        }
    }

    /// <summary>离开保护舱（释放抛下时调用）：恢复显示与受击判定。</summary>
    public void ExitPod()
    {
        if (_dead)
        {
            return;
        }

        Show();
        if (_hitbox != null)
        {
            _hitbox.SetDeferred("monitoring", true);
        }

        var grazeArea = GetNodeOrNull<Area2D>("GrazeArea");
        if (grazeArea != null)
        {
            grazeArea.Monitoring = true;
        }
    }

    public override void _ExitTree()
    {
        // C22：显式断开 GameState 信号连接（重入树不重复连接）
        var gs = GameState.Instance;
        if (gs != null)
        {
            if (gs.IsConnected("BuffsChanged", _onRefreshBuffFactors))
            {
                gs.Disconnect("BuffsChanged", _onRefreshBuffFactors);
            }

            if (gs.IsConnected("AimAssistChanged", _onAimAssistLevelChanged))
            {
                gs.Disconnect("AimAssistChanged", _onAimAssistLevelChanged);
            }

            if (gs.IsConnected("JoySettingsChanged", _onJoySettingsChanged))
            {
                gs.Disconnect("JoySettingsChanged", _onJoySettingsChanged);
            }

            var fogEvents = gs.FogEvents;
            if (fogEvents.IsConnected("FogEventStarted", _onFogEventStarted))
            {
                fogEvents.Disconnect("FogEventStarted", _onFogEventStarted);
            }

            if (fogEvents.IsConnected("FogEventEnded", _onFogEventEnded))
            {
                fogEvents.Disconnect("FogEventEnded", _onFogEventEnded);
            }

            if (fogEvents.IsConnected("FogDirectionShift", _onFogDirectionShift))
            {
                fogEvents.Disconnect("FogDirectionShift", _onFogDirectionShift);
            }
        }

        // 2026-08-03 审计（C22 补齐）：子节点信号断开
        var grazeArea = GetNodeOrNull<Area2D>("GrazeArea");
        if (grazeArea != null && grazeArea.IsConnected(Area2D.SignalName.AreaEntered, _onGrazeEntered))
        {
            grazeArea.Disconnect(Area2D.SignalName.AreaEntered, _onGrazeEntered);
        }

        if (_parryShield != null && _parryShield.IsConnected(Area2D.SignalName.AreaEntered, _onParryShieldEntered))
        {
            _parryShield.Disconnect(Area2D.SignalName.AreaEntered, _onParryShieldEntered);
        }

        if (GameState.Instance.PlayerRef == this)
        {
            GameState.Instance.PlayerRef = null;
        }

        if (GameState.Instance.PlayerHitbox == _hitbox)
        {
            GameState.Instance.PlayerHitbox = null;
        }
    }

    // ---------------- snake_case 兼容桥（M7 后保留：仍有 C# 动态派发/测试调用方；新代码直接调 PascalCase 主方法） ----------------

    public void fire(Vector2 aim) => Fire(aim);

    public float fire_interval() => FireIntervalValue();

    public int bullet_damage() => BulletDamageValue();

    public bool take_damage() => TakeDamage(1.0f, Vector2.Inf);

    public bool take_damage(float amount) => TakeDamage(amount, Vector2.Inf);

    public bool take_damage(float amount, Vector2 fromPos) => TakeDamage(amount, fromPos);

    // ---------------- snake_case 兼容桥（M7 后保留：仍有 C# 动态派发/测试调用方；新代码直接调 PascalCase 主方法） ----------------

    public float BULLET_SPEED
    {
        get => BulletSpeed;
        set
        {
            BulletSpeed = value;
            // 空间换时间缓存同步：测试/兼容桥运行期改基础弹速时，发射路径须立即生效。
            _bulletSpeedValue = BuffScale(BuffBulletSpeed, value, (int)GameState.Instance.BuffCount(BuffBulletSpeed));
        }
    }

    public float INVINCIBLE_TIME { get => InvincibleTime; set => InvincibleTime = value; }

    public float SPAWN_INVINCIBLE_TIME { get => SpawnInvincibleTime; set => SpawnInvincibleTime = value; }

    public float ENTRY_LAND_RATIO { get => EntryLandRatio; set => EntryLandRatio = value; }

    public float ENTRY_RETREAT_SPEED { get => EntryRetreatSpeed; set => EntryRetreatSpeed = value; }

    public float ENTRY_RETREAT_TIME { get => EntryRetreatTime; set => EntryRetreatTime = value; }

    public float ENTRY_INVINCIBLE { get => EntryInvincible; set => EntryInvincible = value; }
}
