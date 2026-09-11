using Godot;
using InfiAir.Core.Input;

namespace InfiAir;

/// <summary>
/// 玩家战机：WASD 平滑移动、朝准星旋转、
/// 手动开火（鼠标左键：按住连发 / 切换模式闩定，见 GameState.FireToggleMode）、
/// Shift 加速、Ctrl 微调、空格相位冲刺（需增幅，耗 25% 燃料）。
/// 组合委托：PlayerDamage/PlayerDash/PlayerParry/PlayerVisuals（纯 C# 类）+ PlayerAugmentVisuals（Node2D）。
/// 语义保持：声明式 AUG_EFFECTS 表、辅助瞄准（追踪/锥形/磁吸）、入场动画、迷雾事件。
/// 公开 API 为 PascalCase。
/// </summary>
public partial class Player : CharacterBody2D
{
    [Signal]
    public delegate void EntryFinishedEventHandler();

    // 静态 Godot 资源改实例字段（退出 segfault 实测教训，UITheme.cs:53）
    // 射击音效：FireA..FireC 三采样轮换（资源装载/音量/抖动/复音统一在 SfxPlayer 目录表）
    private const int FireSoundVariants = 3;

    private readonly Script _bulletScript = GD.Load<Script>("res://csharp/godot/Bullet.cs");

    // 热路径每帧禁 StringName/string 字面量构造——增幅 名与输入 action 名静态缓存
    private static readonly StringName AugCritShot = new("crit_shot");
    private static readonly StringName AugRapidFire = new("rapid_fire");
    private static readonly StringName AugPowerShot = new("power_shot");
    private static readonly StringName AugBulletSpeed = new("bullet_speed");
    private static readonly StringName AugPhaseDash = new("phase_dash");
    private static readonly StringName AugEfficientBoost = new("efficient_boost");
    private static readonly StringName AugBoostRecovery = new("boost_recovery");
    private static readonly StringName AugSpreadShot = new("spread_shot");
    private static readonly StringName AugPiercing = new("piercing");
    private static readonly StringName AugExplosive = new("explosive");
    private static readonly StringName AugHoming = new("homing");
    private static readonly StringName AugSalvo = new("salvo");
    private static readonly StringName AugDeflector = new("deflector");
    private static readonly StringName AugGrazeField = new("graze_field");
    private static readonly StringName AugDashStrike = new("dash_strike");
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
    private static readonly StringName ActFire = new("fire");

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

    // ---- 声明式增幅 效果表（增幅 id → 效果定义；单一事实源） ----
    private static readonly Godot.Collections.Dictionary AugmentEffects = new()
    {
        ["rapid_fire"] = new Godot.Collections.Dictionary { ["kind"] = "pow", ["cfg"] = "augments.rapid_fire.factor", ["default"] = 0.75 },
        ["power_shot"] = new Godot.Collections.Dictionary { ["kind"] = "pow", ["cfg"] = "augments.power_shot.factor", ["default"] = 1.25 },
        ["efficient_boost"] = new Godot.Collections.Dictionary { ["kind"] = "pow", ["cfg"] = "augments.efficient_boost.factor", ["default"] = 0.75 },
        ["boost_recovery"] = new Godot.Collections.Dictionary { ["kind"] = "pow", ["cfg"] = "augments.boost_recovery.factor", ["default"] = 1.5 },
        ["phase_dash"] = new Godot.Collections.Dictionary { ["kind"] = "pow", ["cfg"] = "player.dash.cooldown_stack_factor", ["default"] = 0.8 },
        ["spread_shot"] = new Godot.Collections.Dictionary { ["kind"] = "cap", ["cfg"] = "augments.spread_shot.max_stacks", ["default"] = 2 },
        ["piercing"] = new Godot.Collections.Dictionary { ["kind"] = "cap", ["cfg"] = "augments.piercing.max_stacks", ["default"] = 2 },
        ["explosive"] = new Godot.Collections.Dictionary { ["kind"] = "bool" },
        ["bullet_speed"] = new Godot.Collections.Dictionary { ["kind"] = "pow", ["cfg"] = "augments.bullet_speed.factor", ["default"] = 1.2 },
    };

    /// <summary>声明式增幅 效果表公开访问口（供外部按 id 遍历效果定义）。</summary>
    public Godot.Collections.Dictionary GetAugmentEffects() => AugmentEffects;

    private readonly Godot.Collections.Dictionary _augmentValues = new();

    /// <summary>crit_shot 暴击参数缓存（augments_changed 刷新；bullet 命中经 player_ref 读取）。</summary>
    public float CritChance { get; private set; }

    /// <summary>crit_shot 暴击倍率（同缓存）。</summary>
    public float CritMultiplierValue { get; private set; } = 1.0f;

    public float FuelDrain { get; private set; } = 35.0f;
    public float FuelRegen { get; private set; } = 20.0f;
    public float FuelRestart { get; private set; } = 30.0f;

    /// <summary>尾焰染色乘区（增幅 外观反馈）。</summary>
    public Color EngineTint { get; set; } = Colors.White;

    /// <summary>推进器三态强度 (speedScale, amountRatio, alpha)：加速（冲刺/Boost 共用一条曲线）/巡航/待机；
    /// 逐帧调用的视觉调参，语义见 PlayerVisuals.SetThruster。静态只读免热路径分配。</summary>
    private static readonly (float Speed, float Amount, float Alpha) ThrusterBoost = (1.7f, 1.0f, 1.0f);
    private static readonly (float Speed, float Amount, float Alpha) ThrusterCruise = (1.0f, 0.8f, 0.85f);
    private static readonly (float Speed, float Amount, float Alpha) ThrusterIdle = (0.6f, 0.35f, 0.6f);

    private static readonly Color BodyTintBase = new(1.42f, 1.34f, 1.24f);

    public float DashDistance { get; private set; } = 200.0f;
    public float DashTime { get; private set; } = 0.25f;
    public float DashCooldownMaxValue { get; private set; } = 4.0f;
    public float AfterimageInterval { get; private set; } = 0.08f;
    public float DashFuelRatio { get; private set; } = 0.25f;

    /// <summary>擦弹环基础值（balance 注入；graze_field 乘区后的运行值见 GrazeRadius）。</summary>
    private float GrazeRadiusBase = 20.0f;
    private int GrazeScoreBase = 10;

    public float GrazeRadius { get; private set; } = 20.0f;
    public int GrazeScore { get; private set; } = 10;
    public float GrazeFlashTime { get; private set; } = 0.12f;
    private float _hitboxRadius = 2.8f;

    public float HomingTime { get; private set; } = 1.2f;
    private float _homingTurnRate = 5.5f;
    private float _aimStickFactor = 0.5f;
    private float _aimJoySpeed = 1400.0f;
    private float _aimJoyExpo = 2.2f;
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

    /// <summary>开火门：外部系统（入场序列 / 激光光束）临时屏蔽普通子弹发射。
    /// 玩家侧的开火意愿另见 ActFire 与 GameState.FireToggleMode——门关闭时不发射，
    /// 门打开后仍须玩家按下开火键（手动开火）。</summary>
    private bool _fireGateEnabled = true;

    /// <summary>切换开火模式下的闩定态（按住模式恒不读；开火键按下时翻转）。</summary>
    private bool _fireToggleOn;

    /// <summary>_fireToggleOn 所属的开火方式（设置域切换时清闩定，防残留 true 变自持连发）。</summary>
    private bool _fireToggleModeLatched;

    // ---- 受击/回血与冲刺/弹反/视觉组件（组合委托，纯 C# 类） ----
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
    private bool _entryPrevFireGate = true;
    private Tween? _entryTween;

    // 组合组件属性转发（PlayerDamage/PlayerDash 状态经 Player 门面读写）
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
    private Area2D? _hitbox;
    private GpuParticles2D? _thruster;

    // ---- 受击帧纹理（按 HP 阈值切换） ----
    private readonly Texture2D _texNormal = GD.Load<Texture2D>("res://assets/sprites/player_ship.png");
    private readonly Texture2D _texHit1 = GD.Load<Texture2D>("res://assets/sprites/player_ship_hit_1.png");
    private readonly Texture2D _texHit2 = GD.Load<Texture2D>("res://assets/sprites/player_ship_hit_2.png");
    private int _damageLevel; // 0=正常, 1=轻伤, 2=重伤
    private double _cachedMaxHp = 100.0; // MaxHealth 热路径缓存（extra_life 随 buff 变化，AugmentsChanged 时刷新）
    private float _damageLightRatio = 0.7f; // effects.player_damage_frame.light_ratio
    private float _damageHeavyRatio = 0.4f; // effects.player_damage_frame.heavy_ratio
    private Sprite2D? _glow;
    private Sprite2D? _muzzleGlow;
    private float _muzzleGlowA; // 枪口辉光剩余强度（FireInternal 置 1，_Process 指数衰减）

    private readonly Callable _onRefreshAugmentFactors;
    private readonly Callable _onAimAssistLevelChanged;
    private readonly Callable _onJoySettingsChanged;
    private readonly Callable _onFogEventStarted;
    private readonly Callable _onFogEventEnded;
    private readonly Callable _onFogDirectionShift;
    private readonly Callable _onGrazeEntered;
    private readonly Callable _onParryShieldEntered;

    public Player()
    {
        _onRefreshAugmentFactors = Callable.From(RefreshAugmentFactors);
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
        RefreshAugmentFactors();
        var gs = GameState.Instance;
        if (!gs.IsConnected(GameState.SignalName.AugmentsChanged, _onRefreshAugmentFactors))
        {
            gs.Connect(GameState.SignalName.AugmentsChanged, _onRefreshAugmentFactors);
        }

        if (!gs.IsConnected(GameState.SignalName.JoySettingsChanged, _onJoySettingsChanged))
        {
            gs.Connect(GameState.SignalName.JoySettingsChanged, _onJoySettingsChanged);
        }

        if (!gs.IsConnected(GameState.SignalName.AimAssistChanged, _onAimAssistLevelChanged))
        {
            gs.Connect(GameState.SignalName.AimAssistChanged, _onAimAssistLevelChanged);
        }

        // 迷雾事件：管理器信号驱动效果（解耦：Player 侧只应用）
        var fogEvents = gs.FogEvents;
        if (!fogEvents.IsConnected(FogEventManager.SignalName.FogEventStarted, _onFogEventStarted))
        {
            fogEvents.Connect(FogEventManager.SignalName.FogEventStarted, _onFogEventStarted);
        }

        if (!fogEvents.IsConnected(FogEventManager.SignalName.FogEventEnded, _onFogEventEnded))
        {
            fogEvents.Connect(FogEventManager.SignalName.FogEventEnded, _onFogEventEnded);
        }

        if (!fogEvents.IsConnected(FogEventManager.SignalName.FogDirectionShift, _onFogDirectionShift))
        {
            fogEvents.Connect(FogEventManager.SignalName.FogDirectionShift, _onFogDirectionShift);
        }
    }

    /// <summary>残影逐帧淡出（渲染帧）——委托 PlayerVisuals。</summary>
    public override void _Process(double delta)
    {
        _visuals.UpdateAfterimages((float)delta);
        UpdateDamageFrame();
        // 枪口辉光指数衰减（半衰 ~60ms，急促闪光感）
        if (_muzzleGlowA > 0.01f && _muzzleGlow != null)
        {
            _muzzleGlowA *= Mathf.Exp(-12.0f * (float)delta);
            _muzzleGlow.Modulate = new Color(1.0f, 0.85f, 0.5f, 0.85f * _muzzleGlowA);
        }
        else if (_muzzleGlow != null && _muzzleGlow.Modulate.A > 0.0f)
        {
            _muzzleGlow.Modulate = new Color(1.0f, 0.85f, 0.5f, 0.0f);
        }
    }

    /// <summary>按 HP 百分比切换受击帧（0=正常, ≤light_ratio=轻伤, ≤heavy_ratio=重伤；阈值经 effects.player_damage_frame 配置）。</summary>
    private void UpdateDamageFrame()
    {
        if (_sprite == null) return;
        var hp = GameState.Instance.Health;
        var maxHp = _cachedMaxHp;
        if (maxHp <= 0.0) return;
        var ratio = hp / maxHp;
        var level = ratio > _damageLightRatio ? 0 : ratio > _damageHeavyRatio ? 1 : 2;
        if (level == _damageLevel) return;
        _damageLevel = level;
        _sprite.Texture = level switch
        {
            1 => _texHit1,
            2 => _texHit2,
            _ => _texNormal,
        };
        if (_glow != null) _glow.Texture = _sprite.Texture;
    }

    /// <summary>数值配置缓存（启动一次读入，避免每帧 Dictionary 路径查找）。</summary>
    private void LoadBalance()
    {
        // 运动/速度族钳 ≥0——负值致反向移动/反向加速
        MaxSpeed = CfgFx.Float("player.max_speed", MaxSpeed, 0.0f);
        Accel = CfgFx.Float("player.accel", Accel, 0.0f);
        Decel = CfgFx.Float("player.decel", Decel, 0.0f);
        BoostMult = CfgFx.Float("player.boost_mult", BoostMult, 0.0f);
        FineMoveMult = CfgFx.Float("player.fine_move_mult", FineMoveMult, 0.0f);
        // base_fire_interval 钳 0.05 下限（同 laser tick_interval 族）——≤0 时每物理帧开火
        BaseFireInterval = CfgFx.Float("player.base_fire_interval", BaseFireInterval, CfgFx.IntervalFloor);
        BulletSpeed = CfgFx.Float("player.bullet_speed", BulletSpeed, 0.0f);
        // crit_shot.chance 钳 [0,1]——>1 刀刀暴击；multiplier 钳 ≥0——负暴击倍数致回血
        CritChanceBase = CfgFx.Float("augments.crit_shot.chance", CritChanceBase, 0.0f, 1.0f);
        CritMultiplier = CfgFx.Float("augments.crit_shot.multiplier", CritMultiplier, 0.0f);
        BulletSpreadDeg = CfgFx.Float("player.bullet_spread_deg", BulletSpreadDeg, 0.0f);
        // bullet_damage 钳 ≥0（CfgFx.Int 统一判型 + 域钳）——负伤害给敌机回血
        BulletDamage = CfgFx.Int("player.bullet_damage", BulletDamage, 0);
        InvincibleTime = CfgFx.Float("player.invincible_time", InvincibleTime, 0.0f);
        SpawnInvincibleTime = CfgFx.Float("player.spawn_invincible_time", SpawnInvincibleTime, 0.0f);
        BulletClearRadius = CfgFx.Float("player.bullet_clear_radius", BulletClearRadius, 0.0f);
        // entry.* 钳 ≥0——负值入场时序/位移反向
        EntryLandRatio = CfgFx.Float("player.entry.land_ratio", EntryLandRatio, 0.0f);
        EntryRushTime = CfgFx.Float("player.entry.rush_time", EntryRushTime, 0.0f);
        EntryRetreatSpeed = CfgFx.Float("player.entry.retreat_speed", EntryRetreatSpeed, 0.0f);
        EntryRetreatTime = CfgFx.Float("player.entry.retreat_time", EntryRetreatTime, 0.0f);
        EntryInvincible = CfgFx.Float("player.entry.invincible", EntryInvincible, 0.0f);
        EntrySpawnClearance = CfgFx.Float("player.entry.spawn_clearance", EntrySpawnClearance, 0.0f);
        EntryRushHsRatio = CfgFx.Float("player.entry.rush_hspeed_ratio", EntryRushHsRatio, 0.0f);
        // armor.multiplier 钳 [0,1]——≤0 受击回血（GameState.Settings 乘算）；
        // evasion.chance 钳 [0,1]——≥1 软无敌；regen.heal_per_sec 钳 ≥0——负值逐秒扣血
        ArmorMult = CfgFx.Float("augments.armor.multiplier", ArmorMult, 0.0f, 1.0f);
        EvasionChance = CfgFx.Float("augments.evasion.chance", EvasionChance, 0.0f, 1.0f);
        RegenPerSec = CfgFx.Float("augments.regen.heal_per_sec", RegenPerSec, 0.0f);
        ShakeHit = CfgFx.Float("effects.shake.player_hit", ShakeHit, 0.0f);
        // 受击帧阈值钳 [0,1]——ratio 为百分比；配置非法（light<=heavy）回退默认 0.7/0.4
        _damageLightRatio = CfgFx.Float("effects.player_damage_frame.light_ratio", _damageLightRatio, 0.0f, 1.0f);
        _damageHeavyRatio = CfgFx.Float("effects.player_damage_frame.heavy_ratio", _damageHeavyRatio, 0.0f, 1.0f);
        if (_damageLightRatio <= _damageHeavyRatio)
        {
            _damageLightRatio = 0.7f;
            _damageHeavyRatio = 0.4f;
        }
        Invincible = SpawnInvincibleTime; // 出生保护
        // fuel.max 钳下限——0 时 FuelRatio() 的 _fuel/FuelMax 除零得 NaN
        //（燃料条显示 NaN；SetFuel 的 Clamp 上下界同为 0 致燃料机制失效）
        FuelMax = CfgFx.Float("player.fuel.max", FuelMax, 1.0f);
        _fuel = FuelMax;
        // fuel.drain/regen/restart 钳 ≥0——负值反转充能/消耗方向
        FuelDrain = CfgFx.Float("player.fuel.drain", FuelDrain, 0.0f);
        FuelRegen = CfgFx.Float("player.fuel.regen", FuelRegen, 0.0f);
        FuelRestart = CfgFx.Float("player.fuel.restart", FuelRestart, 0.0f);
        // dash.distance/fuel_ratio/afterimage_interval 钳 ≥0——负值冲刺反向
        DashDistance = CfgFx.Float("player.dash.distance", DashDistance, 0.0f);
        // dash.time 钳 0.05 下限——0/负值时 UpdateMove 的 DashDistance/DashTime 除零得 inf → 位置 NaN
        DashTime = CfgFx.Float("player.dash.time", DashTime, CfgFx.IntervalFloor);
        // dash.cooldown 钳 0.05 下限（与 fuel.max/dash.time 同族）——配 0
        // 且无 phase_dash 层数时 DashReadyRatio() 的 CooldownRemaining()/DashCooldownMax() = 0/0
        // = NaN（Mathf.Clamp 不拦 NaN），渗入 HUD 充能条
        DashCooldownMaxValue = CfgFx.Float("player.dash.cooldown", DashCooldownMaxValue, CfgFx.IntervalFloor);
        DashFuelRatio = CfgFx.Float("player.dash.fuel_ratio", DashFuelRatio, 0.0f);
        AfterimageInterval = CfgFx.Float("player.dash.afterimage_interval", AfterimageInterval, 0.0f);
        // graze_radius 钳 ≥0——负值擦弹环失效；graze_score 钳 ≥0——负分被连击乘区倒扣
        GrazeRadiusBase = CfgFx.Float("player.graze_radius", GrazeRadiusBase, 0.0f);
        GrazeScoreBase = CfgFx.Int("player.graze_score", GrazeScoreBase, 0);
        GrazeRadius = GrazeRadiusBase;
        GrazeScore = GrazeScoreBase;
        // parry.* 钳 ≥0——负半径/负角度致弹反扇形判定异常
        ParryArcDeg = CfgFx.Float("player.parry.arc_deg", ParryArcDeg, 0.0f);
        ParryRadius = CfgFx.Float("player.parry.radius", ParryRadius, 0.0f);
        _parryCooldownBase = CfgFx.Float("player.parry.cooldown", 3.0f, 0.0f);
        _parry.Configure(
            CfgFx.Float("player.parry.duration", 0.8f, 0.0f),
            CfgFx.Float("player.parry.active_time", 0.5f, 0.0f),
            _parryCooldownBase);
        _damage.Configure(
            InvincibleTime,
            ArmorMult,
            EvasionChance,
            RegenPerSec,
            ShakeHit,
            CfgFx.Float("augments.second_wind.duration", 3.0f, 0.0f),
            CfgFx.Float("augments.second_wind.heal_per_sec", 3.0f, 0.0f));
        _dash.Configure(DashDistance, DashTime, DashCooldownMaxValue, AfterimageInterval);
        // aim_assist.input/falloff 钳 ≥0——负值磁吸力/衰减域反转
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
        // 擦弹环（GrazeArea）——游戏性范围族运行值，不乘 world_scale
        var grazeArea = GetNode<Area2D>("GrazeArea");
        if (grazeArea.GetNode<CollisionShape2D>("CollisionShape2D").Shape is CircleShape2D grazeCircle)
        {
            grazeCircle.Radius = GrazeRadius;
        }

        grazeArea.Connect(Area2D.SignalName.AreaEntered, _onGrazeEntered);
        // 弹反盾——tscn 占位节点；圆盘 shape 触发进入检测，回调内精确扇形过滤
        _parryShield = GetNode<Area2D>("ParryShield");
        _parryShield.Connect(Area2D.SignalName.AreaEntered, _onParryShieldEntered);
        // tscn 占位无 shape——新建圆盘判定形状
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
        // 尾焰质感：默认方粒（无纹理）是"廉价"观感主源之一——改软点贴图 + 白热→琥珀→暗橙渐隐色阶
        _thruster.Texture = CinematicFx.SoftTexture();
        if (_thruster.ProcessMaterial is ParticleProcessMaterial thrusterMat)
        {
            // 软点 64px 基准换算：cfg scale 语义 = 像素直径（同 CinematicFx.Particles），
            // 设计直径 18–44px(×ws) → 贴图比例；软衰减使亮芯更小
            thrusterMat.ScaleMin = 18.0f * ws / CinematicFx.SoftTexSize;
            thrusterMat.ScaleMax = 44.0f * ws / CinematicFx.SoftTexSize;
            thrusterMat.Color = new Color(1.0f, 0.72f, 0.30f);
            thrusterMat.ColorRamp = ThrusterRamp();
        }

        _muzzleOffset = 50.0f * ws;
        // 枪口辉光：常驻软点精灵（64px 软点 → 直径换算 scale），FireInternal 点亮、_Process 逐帧衰减（零分配）
        // additive 混合喂 world_grade 辉光，开火瞬间形成能量爆点
        _muzzleGlow = new Sprite2D
        {
            Texture = CinematicFx.SoftTexture(),
            Scale = Vector2.One * (30.0f * ws / 64.0f),
            Modulate = new Color(1.0f, 0.85f, 0.5f, 0.0f),
            Material = CinematicFx.AdditiveMaterial(),
            ZIndex = 1,
        };
        AddChild(_muzzleGlow);
        // 鼠标跟随准星：top_level 世界坐标节点
        _crosshair = new AimCrosshair();
        _crosshair.Init(this);
        AddChild(_crosshair);
        // 可视性增强：机体提亮 + 青色描边辉光
        _sprite.Modulate = BodyTintBase;
        _glow = new Sprite2D
        {
            Texture = _sprite.Texture,
            Scale = new Vector2(1.2f, 1.2f),
            Modulate = new Color(1.0f, 0.72f, 0.32f, 0.42f),
            ZIndex = -1,
        };
        _sprite.AddChild(_glow);
        // 能量发光层（GlowLayer，tscn 挂载于主贴图下）：琥珀 tint 呼应尾焰，强度克制入配置；
        // 受击帧切换时遮罩不动（遮罩只含能量图元，不含损伤结构）
        ShipEnergyFx.Apply(
            _sprite.GetNodeOrNull<Sprite2D>(ShipEnergyFx.NodeName),
            ShipEnergyFx.CfgColor("effects.ship_energy.tint_player", new Color(1.0f, 0.65f, 0.18f)),
            CfgFx.Float("effects.ship_energy.intensity_player", 0.45f, 0.0f));
        // 碰撞点指示：受击判定点闪烁小光点 + 淡色光圈
        var dotPts = new Vector2[10];
        for (var i = 0; i < 10; i++)
        {
            var a = Mathf.Tau * i / 10.0f;
            dotPts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 3.5f;
        }

        var hitboxDot = new Polygon2D { Polygon = dotPts, Color = new Color(1.0f, 0.86f, 0.52f) };
        AddChild(hitboxDot);
        var hitboxHalo = new Line2D { Width = 1.5f, DefaultColor = new Color(1.0f, 0.72f, 0.32f, 0.45f), Closed = true };
        for (var i = 0; i < 16; i++)
        {
            var a = Mathf.Tau * i / 16.0f;
            hitboxHalo.AddPoint(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 8.0f);
        }

        AddChild(hitboxHalo);
        // 增幅 外观反馈附件（augments_changed 信号驱动）
        var augmentVisuals = new PlayerAugmentVisuals
        {
            Scale = _sprite.Scale / PlayerAugmentVisuals.BaseShipScale,
        };
        AddChild(augmentVisuals);
        augmentVisuals.Init(_sprite, this);
        // 视觉组件初始化（残影池预建；Main 场景构建期 add_child 报 busy，延迟到帧末）
        _visuals.Init(_sprite, _thruster, hitboxDot, parryArc, parryRim, parryShine, parryPulse, GetParent());
    }

    // ---- 对外公开接口 ----

    public bool IsDead() => _dead;

    public bool IsInputLocked() => _inputLocked;

    public void SetInvincible(float seconds) => _damage.SetInvincible(seconds);

    public float InvincibleRemaining() => _damage.InvincibleRemaining();

    public float EnrageSlow() => _enrageSlow;

    public void SetDead(bool dead) => _dead = dead;

    public void SetDashCooldown(float seconds) => _dash.DashCooldown = seconds;

    public void SetSinceDamage(float seconds) => _damage.SinceDamage = seconds;

    public void SetLastHitFrame(int frame) => _damage.LastHitFrame = frame;

    public float DashCooldownRemaining() => _dash.CooldownRemaining();

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

    /// <summary>距离衰减曲线（开火弱追踪与 AimFrameLayer 磁吸共用）。</summary>
    public float AimDistFalloff(float d) => DistFalloffCurve(d, _falloffPeak, _falloffEnd, _falloffMin);

    /// <summary>距离衰减分段纯函数（单实现）。</summary>
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

    public void LockInput() => _inputLocked = true;

    public void UnlockInput() => _inputLocked = false;

    public void SetFuel(float value) => _fuel = Mathf.Clamp(value, 0.0f, FuelMax);

    public float FuelAmount() => _fuel;

    public void Die() => DieInternal();

    public void ApplyEnrageSlow(float factor) => _enrageSlow = factor;

    /// <summary>开火门开关（外部系统用：激光光束发射期间屏蔽普通子弹）。</summary>
    public void SetFireGate(bool enabled) => _fireGateEnabled = enabled;

    /// <summary>入场序列期间外部系统（LaserWeapon.EndBeam）恢复开火门时同步覆盖捕获值，
    /// 防 FinishEntry 把激光恢复的 true 踩回 false（返航暂停冻结激光 active 的孪生路径）。</summary>
    public void OverrideEntryFireGate(bool value)
    {
        if (_entryPhase != 0)
        {
            _entryPrevFireGate = value;
        }
    }

    /// <summary>当前开火门状态（LaserWeapon 捕获-恢复用）。</summary>
    public bool FireGateEnabled() => _fireGateEnabled;

    public bool IsDashing() => _dash.IsDashing();

    /// <summary>
    /// 按声明式效果表刷新增幅 值缓存（_ready 初始 + augments_changed 信号驱动）。
    /// 乘算类（pow）效果用 TalentEffLevel 浮点有效层级——收益递减/路线加成/专注折扣
    /// 在有效层级内折算；cap/bool 类保持整数 Augments 口径（盾层/穿透/散射语义不变）。
    /// </summary>
    private void RefreshAugmentFactors()
    {
        foreach (var id in AugmentEffects.Keys)
        {
            var effect = (Godot.Collections.Dictionary)AugmentEffects[id];
            var kind = (string)(StringName)effect["kind"];
            if (kind == "bool")
            {
                continue;
            }

            var value = GameState.Instance.Cfg((StringName)effect["cfg"], effect["default"]);
            _augmentValues[id] = kind == "cap" ? (int)value.AsInt64() : (float)value.AsDouble();
        }

        var critEff = (float)GameState.Instance.TalentEffLevel(AugCritShot);
        CritChance = critEff <= 0f ? 0.0f : CritChanceBase * critEff;
        CritMultiplierValue = CritMultiplier;
        // 燃油速率缓存（LaserWeapon.OnAugmentsChanged 同款）——避免每物理帧
        // AugmentLevel 字典查找 + Pow（_physics_process 每帧两次）。
        // 开火/冲刺路径同口径缓存，空间换时间（见字段注释）。
        _fuelDrainRate = AugmentScale(AugEfficientBoost, FuelDrain, (float)GameState.Instance.TalentEffLevel(AugEfficientBoost));
        _fuelRegenRate = AugmentScale(AugBoostRecovery, FuelRegen, (float)GameState.Instance.TalentEffLevel(AugBoostRecovery));
        _fireIntervalValue = AugmentScale(AugRapidFire, BaseFireInterval, (float)GameState.Instance.TalentEffLevel(AugRapidFire));
        _bulletDamageValue = Mathf.Max(1, (int)AugmentScale(AugPowerShot, BulletDamage, (float)GameState.Instance.TalentEffLevel(AugPowerShot)));
        _bulletSpeedValue = AugmentScale(AugBulletSpeed, BulletSpeed, (float)GameState.Instance.TalentEffLevel(AugBulletSpeed));
        _spreadShotCount = AugmentCap(AugSpreadShot);
        _pierceCount = AugmentCap(AugPiercing);
        _explosiveEnabled = AugmentEnabled(AugExplosive);
        var dashStacks = (int)GameState.Instance.AugmentLevel(AugPhaseDash);
        _dashUnlocked = dashStacks > 0;
        _dashCooldownMax = AugmentScale(AugPhaseDash, DashCooldownMaxValue, Mathf.Max((float)GameState.Instance.TalentEffLevel(AugPhaseDash) - 1f, 0f));
        // ---- 作战增幅扩展（乘算走 EffLevel 浮点层级；整数语义走层数）----
        var homingEff = (float)GameState.Instance.TalentEffLevel(AugHoming);
        if (homingEff > 0f)
        {
            _homingAugTurnRate = CfgFx.Float("augments.homing.turn_rate_deg", 150.0f, 0.0f) * homingEff;
            _homingLockTime = CfgFx.Float("augments.homing.lock_time", 8.0f, CfgFx.IntervalFloor);
            _homingLockRange = CfgFx.Float("augments.homing.lock_range", 900.0f, 0.0f);
            var coneDeg = CfgFx.Float("augments.homing.lock_cone_deg", 44.0f, 0.0f);
            _homingLockConeCos = Mathf.Cos(Mathf.DegToRad(coneDeg * 0.5f));
        }
        else
        {
            _homingAugTurnRate = 0f;
        }

        var salvoLevel = (int)GameState.Instance.AugmentLevel(AugSalvo);
        _salvoInterval = salvoLevel > 0
            ? Mathf.Max((int)CfgFx.Float("augments.salvo.every_base", 7.0f, 1.0f) - (int)CfgFx.Float("augments.salvo.every_step", 2.0f, 0.0f) * salvoLevel, 3)
            : 0;
        _salvoDamageMult = CfgFx.Float("augments.salvo.damage_mult", 3.0f, 1.0f);

        var deflectorEff = (float)GameState.Instance.TalentEffLevel(AugDeflector);
        _deflectorCooldownFactor = deflectorEff > 0f ? Mathf.Pow(CfgFx.Float("augments.deflector.cooldown_factor", 0.78f, 0.05f, 1.0f), deflectorEff) : 1.0f;
        _deflectorReflectMult = deflectorEff > 0f ? Mathf.Pow(CfgFx.Float("augments.deflector.reflect_mult", 1.6f, 1.0f), deflectorEff) : 1.0f;
        // 弹反冷却 = 基值（_load_balance 定值一次）× 当前偏转乘区；乘区变化时整体重设组件
        _parry.Configure(_parry.Duration, _parry.ActiveTime, _parryCooldownBase * _deflectorCooldownFactor);

        var grazeEff = (float)GameState.Instance.TalentEffLevel(AugGrazeField);
        GrazeRadius = GrazeRadiusBase * (grazeEff > 0f ? Mathf.Pow(CfgFx.Float("augments.graze_field.radius_factor", 1.2f, 1.0f), grazeEff) : 1.0f);
        GrazeScore = GrazeScoreBase + CfgFx.Int("augments.graze_field.score_per_level", 5, 0) * (int)GameState.Instance.AugmentLevel(AugGrazeField);
        RefreshGrazeShape();

        _dashStrikeLevel = (int)GameState.Instance.AugmentLevel(AugDashStrike);
        _dashStrikeRadius = CfgFx.Float("augments.dash_strike.radius", 80.0f, 0.0f);
        _dashStrikeDamage = Mathf.Max(1, CfgFx.Int("augments.dash_strike.damage_per_level", 35, 0));
        _dashStrikeTick = 0f;

        // MaxHealth 热路径缓存（extra_life 随天赋层级变化才变，
        // 由本方法（_Ready 首调 + AugmentsChanged 驱动）刷新，避免 _Process 每帧 Dictionary 查找。
        _cachedMaxHp = GameState.Instance.MaxHealth();
    }

    /// <summary>graze_field 改变擦弹环半径后同步碰撞形状（增减层时刷新；非热路径）。</summary>
    private void RefreshGrazeShape()
    {
        var grazeArea = GetNodeOrNull<Area2D>("GrazeArea");
        if (grazeArea?.GetNode<CollisionShape2D>("CollisionShape2D").Shape is CircleShape2D grazeCircle)
        {
            grazeCircle.Radius = GrazeRadius;
        }
    }

    /// <summary>乘算因子求值——base × factor^effLevel（effLevel 可为分数：收益递减/路线/专注折算）。</summary>
    private float AugmentScale(StringName id, float baseValue, float effLevel) => baseValue * Mathf.Pow((float)_augmentValues[id].AsDouble(), effLevel);

    /// <summary>堆叠上限截断——min(count, max_stacks)。</summary>
    private int AugmentCap(StringName id) => Mathf.Min((int)GameState.Instance.AugmentLevel(id), (int)_augmentValues[id]);

    /// <summary>布尔启用——count &gt; 0。</summary>
    private bool AugmentEnabled(StringName id) => (int)GameState.Instance.AugmentLevel(id) > 0;

    public float FireIntervalValue() => _fireIntervalValue;

    public int BulletDamageValue() => _bulletDamageValue;

    /// <summary>bullet_speed 增幅 后的当前弹速（augments_changed 时缓存）。</summary>
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

    /// <summary>燃油速率缓存（RefreshAugmentFactors 刷新：_ready 初始 + augments_changed 信号驱动；
    /// 默认值 = 无增幅 时的 FuelDrain/FuelRegen 脚本默认，直实例化未 _ready 路径语义不变）。</summary>
    private float _fuelDrainRate = 35.0f;
    private float _fuelRegenRate = 20.0f;

    /// <summary>空间换时间：射速/伤害/弹速/冲刺解锁与冲刺冷却上限随增幅 变化一次性缓存，
    /// 避免 _PhysicsProcess 每帧与每发 Fire 调用 AugmentLevel 字典查找 + Pow。</summary>
    private float _fireIntervalValue = 0.15f;
    private int _bulletDamageValue = 10;
    private float _bulletSpeedValue = 1800.0f;
    private int _spreadShotCount;
    private int _pierceCount;
    private bool _explosiveEnabled;
    private bool _dashUnlocked;
    private float _dashCooldownMax = 4.0f;

    // ---- 作战增幅扩展（homing/salvo/deflector/graze_field/dash_strike）----
    /// <summary>homing 制导：0 = 未购；>0 = 出膛弹追踪角速率（deg/s，随有效层级放大）。</summary>
    private float _homingAugTurnRate;
    private float _homingLockTime = 8.0f;
    private float _homingLockRange = 900.0f;
    private float _homingLockConeCos;
    /// <summary>salvo 齐射重弹：0 = 未购；>0 = 每 N 发触发一次重弹。</summary>
    private int _salvoInterval;
    private int _salvoCounter;
    private float _salvoDamageMult = 3.0f;
    /// <summary>deflector 偏转：弹反冷却乘区（<1 = 已购生效）与反射伤害乘区（>1 = 已购生效）。</summary>
    private float _deflectorCooldownFactor = 1.0f;
    private float _deflectorReflectMult = 1.0f;
    private float _parryCooldownBase = 3.0f;
    /// <summary>dash_strike 冲刺打击：0 层 = 未购；触发半径/伤害/节流缓存。</summary>
    private int _dashStrikeLevel;
    private float _dashStrikeRadius = 80.0f;
    private int _dashStrikeDamage = 35;
    private float _dashStrikeTick;
    private const float DashStrikeDefaultInterval = 0.12f;

    public float FuelDrainRate() => _fuelDrainRate;

    public float FuelRegenRate() => _fuelRegenRate;

    /// <summary>推进器状态下发（统一注入 EngineTint；入场冲刺 ×2.0 强度为一次性演出，不走三态表）。</summary>
    private void ApplyThruster((float Speed, float Amount, float Alpha) state)
        => _visuals.SetThruster(state.Speed, state.Amount, state.Alpha, EngineTint);

    /// <summary>尾焰色阶：白热芯 → 琥珀 → 暗橙熄灭（GradientTexture1D 一次性构建，随粒子寿命采样）。</summary>
    private static GradientTexture1D ThrusterRamp()
    {
        var g = new Gradient
        {
            Offsets = new[] { 0.0f, 0.35f, 1.0f },
            Colors = new[]
            {
                new Color(1.0f, 0.96f, 0.86f, 1.0f),
                new Color(1.0f, 0.66f, 0.24f, 0.85f),
                new Color(0.62f, 0.22f, 0.06f, 0.0f),
            },
        };
        return new GradientTexture1D { Gradient = g };
    }

    public override void _PhysicsProcess(double delta)
    {
        var d = (float)delta;
        // 帧首缓存 GameState 门面：本方法多次读取设置域，避免重复 Instance 判活/根节点查询。
        var gs = GameState.Instance;
        // 同帧双 GetTicksMsec 合并：帧首取一次、本帧内复用（免同帧多次系统时钟查询）
        var nowMs = (long)Time.GetTicksMsec();
        if (_dead)
        {
            return;
        }

        if (_inputLocked)
        {
            // 锁输入期关闭弹反盾物理判定（整体早退会使 monitoring 停留在锁定前值）
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

        // 手柄摇杆读取侧整形：径向死区（设置域）+ 重标定（StickShaper，InputMap 侧只滤硬件噪声）。
        // 键盘输出满行程（长度 1），整形后是恒等映射——键鼠/手柄共用本路径不分叉；移动保持线性（expo 1.0）。
        var rawMove = Input.GetVector(ActMoveLeft, ActMoveRight, ActMoveUp, ActMoveDown);
        var shapedMove = StickShaper.Shape(rawMove.X, rawMove.Y, (float)gs.JoyDeadzone, 1.0f);
        var inputDir = new Vector2(shapedMove.X, shapedMove.Y);
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
            ApplyThruster(ThrusterBoost);
            TickDashStrike(d);
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
            ApplyThruster(ThrusterBoost);
        }
        else if (inputDir != Vector2.Zero)
        {
            ApplyThruster(ThrusterCruise);
        }
        else
        {
            ApplyThruster(ThrusterIdle);
        }

        var aim = AimPoint() - GlobalPosition;
        if (aim.Length() > 1.0f)
        {
            // 贴图机头朝上，需 +90° 偏移
            Rotation = aim.Angle() + Mathf.Pi / 2.0f;
        }

        // 开火意愿（设置域 fire_toggle_mode）：按住 = 开火键按下期间连发；
        // 切换 = 按一下闩定、再按一下解除（闩定值跨帧保留在 _fireToggleOn）。
        // 方式一改就立刻清闩定：按住模式期间 _fireToggleOn 不参与判定，残留的 true
        // 会让改选「切换」的当帧开始自持连发（鼠标此刻并未按下）。
        var fireToggleMode = gs.FireToggleMode;
        if (fireToggleMode != _fireToggleModeLatched)
        {
            _fireToggleModeLatched = fireToggleMode;
            _fireToggleOn = false;
        }

        if (fireToggleMode && Input.IsActionJustPressed(ActFire))
        {
            _fireToggleOn = !_fireToggleOn;
        }

        var wantFire = fireToggleMode ? _fireToggleOn : Input.IsActionPressed(ActFire);
        _fireCooldown -= d;
        if (_fireGateEnabled && wantFire && _fireCooldown <= 0.0f && aim.Length() > 1.0f)
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

        // 机身色调四源 + 受击点脉动（委托 PlayerVisuals）
        if (Invincible > 0.0f)
        {
            Invincible -= d;
        }

        _visuals.UpdateFrame(d, _parry.TintStrength(), Invincible, nowMs);
        // 回血（委托 PlayerDamage）
        _damage.HealTick(d);
    }

    /// <summary>屏幕边缘钳制：随可见世界区域收窄。</summary>
    public Vector2 ClampToView(Vector2 p) => ClampToView(p, GameState.Instance.ViewWorldRect());

    /// <summary>移动钳制视口内边距（px）：玩家位置保持在可见区域内侧此距离——「留在场内」的
    /// padding（语义区别于 Enemy/Boss 的出屏离场判定余量）。</summary>
    private const float ViewClampInset = 40.0f;

    /// <summary>准星钳制内边距（px）：仅防 bracket 半幅画出窗外，与移动钳制的「留在场内」语义不同。</summary>
    private const float AimClampInset = 4.0f;

    /// <summary>给定视野的屏幕边缘钳制（_PhysicsProcess 复用帧内已取视野，免重复 Instance/ViewWorldRect）。</summary>
    private static Vector2 ClampToView(Vector2 p, Rect2 view)
    {
        var inset = new Vector2(ViewClampInset, ViewClampInset);
        return p.Clamp(view.Position + inset, view.End - inset);
    }

    /// <summary>当前瞄准点（世界坐标）：外部注入点（AimPointOverride 非 +Inf 哨兵）优先；
    /// 键鼠/手柄下准星与系统光标逐像素绑定（见内注）。</summary>
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
            // 右摇杆虚拟准星（四向独立动作，差值驱动）：读取侧 StickShaper 整形——
            // 径向死区（设置域）+ 指数响应曲线（joy_expo，轻推精瞄/推满甩枪）
            var joyDelta = Vector2.Zero;
            var joy = Input.GetVector(ActAimLeft, ActAimRight, ActAimUp, ActAimDown);
            var joyShaped = StickShaper.Shape(joy.X, joy.Y, (float)GameState.Instance.JoyDeadzone, _aimJoyExpo);
            if (joyShaped.X != 0.0f || joyShaped.Y != 0.0f)
            {
                joyDelta = new Vector2(joyShaped.X, joyShaped.Y) * _aimJoySpeed * (float)GetProcessDeltaTime();
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
                    // 磁吸输入窗口：摇杆有输入时取摇杆增量（joyDelta 与鼠标增量同量纲 px/帧，
                    // 1400px/s ÷ 60fps ≈ 23px/帧 落在 magnet_input_min/full 窗口内），否则取鼠标
                    // 物理增量——两路二选一，避免同帧双输入叠加放大磁吸强度
                    magnet = aimLayer.MagnetPull(_aimSmooth, joyDelta != Vector2.Zero ? joyDelta : raw - _aimLastRaw);
                }
            }

            // 键鼠/手柄准星-光标绑定：物理增量（raw − _aimLastRaw）全量通过，粘滞(factor<1)/
            // 磁吸/摇杆偏移经 Viewport.WarpMouse 反写真实光标，下一帧 raw 即新锚点——准星始终与
            // 光标绑定（差值累积会在光标顶到屏幕边缘后物理增量归零、准星看似卡死）；
            // 目标钳制在可视世界域内（视角档自适应），准星/光标均不出窗。
            var desired = !_aimInitialized ? raw : _aimSmooth + (raw - _aimLastRaw) * factor + magnet + joyDelta;
            var view = GameState.Instance.ViewWorldRect();
            var inset = new Vector2(AimClampInset, AimClampInset);
            desired = desired.Clamp(view.Position + inset, view.End - inset);
            if ((desired - raw).LengthSquared() > 0.25f)
            {
                GetViewport().WarpMouse(GetCanvasTransform() * desired);
            }

            _aimSmooth = desired;
            _aimLastRaw = desired;
            _aimInitialized = true;
        }

        return _aimSmooth;
    }

    /// <summary>读取当前强度档位参数（balance.json player.aim_assist.levels.&lt;level&gt;）。</summary>
    private void LoadAimAssistParams()
    {
        var level = (string)(StringName)GameState.Instance.AimAssistLevel;
        var basePath = "player.aim_assist.levels." + level + ".";
        // 档位参数钳 ≥0——负值致追踪/磁吸反向
        _homingTurnRate = Mathf.Max((float)GameState.Instance.Cfg(basePath + "homing_turn_rate", _homingTurnRate).AsDouble(), 0.0f);
        _aimStickFactor = Mathf.Max((float)GameState.Instance.Cfg(basePath + "stick_factor", _aimStickFactor).AsDouble(), 0.0f);
        HomingTime = Mathf.Max((float)GameState.Instance.Cfg("player.aim_assist.homing_time", HomingTime).AsDouble(), 0.0f);
        // cone_angle_deg 钳 [0,360]——越界角度（负/超 360）致 coneCos 周期折叠，
        // 锥形弱追踪判定失真（360 时 cos=1 → angT 0/0=NaN，NaN 守卫兜底）
        _coneAngleDeg = Mathf.Clamp((float)GameState.Instance.Cfg(basePath + "cone_angle_deg", _coneAngleDeg).AsDouble(), 0.0f, 360.0f);
        _coneCos = Mathf.Cos(Mathf.DegToRad(_coneAngleDeg));
        _coneStrength = Mathf.Max((float)GameState.Instance.Cfg(basePath + "cone_strength", _coneStrength).AsDouble(), 0.0f);
        _magnetRange = Mathf.Max((float)GameState.Instance.Cfg(basePath + "magnet_range", _magnetRange).AsDouble(), 0.0f);
        _magnetStrength = Mathf.Max((float)GameState.Instance.Cfg(basePath + "magnet_strength", _magnetStrength).AsDouble(), 0.0f);
        _magnetMaxSpeed = Mathf.Max((float)GameState.Instance.Cfg(basePath + "magnet_max_speed", _magnetMaxSpeed).AsDouble(), 0.0f);
        // 摇杆瞄准响应曲线指数：钳 ≥1.0——<1 会变成轻推即满速的反曲线
        _aimJoyExpo = Mathf.Max((float)GameState.Instance.Cfg("player.aim_assist.joy_expo", _aimJoyExpo).AsDouble(), 1.0f);
    }

    private void OnAimAssistLevelChanged(StringName level) => LoadAimAssistParams();

    /// <summary>手柄设置变更（右摇杆灵敏度）重读。</summary>
    private void OnJoySettingsChanged(float aimSpeed, float deadzone) => _aimJoySpeed = aimSpeed;

    /// <summary>冲刺残影公开入口——委托 PlayerVisuals 池化生成。</summary>
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
        _entryPrevFireGate = _fireGateEnabled;
        _fireGateEnabled = false;
        Position = new Vector2(rect.GetCenter().X, rect.End.Y + EntrySpawnClearance);
        _visuals.SetThruster(2.0f, 1.0f, 1.0f, EngineTint);
        _entryTween = CreateTween();
        _entryTween.TweenProperty(this, "position:y", landY, EntryRushTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _entryTween.TweenCallback(Callable.From(OnEntryLanded));
    }

    /// <summary>入场动画进行中（Main/LaserWeapon 等流程查询）。</summary>
    public bool IsEntryPlaying() => _entryPhase != 0;

    /// <summary>中断入场动画（返航/自毁等流程接管时调用）：复位状态机并静默收尾。</summary>
    public void AbortEntry()
    {
        if (_entryPhase == 0)
        {
            return;
        }

        _entryPhase = 0;
        _fireGateEnabled = _entryPrevFireGate;
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
        _fireGateEnabled = _entryPrevFireGate;
        Velocity = Vector2.Zero;
        EmitSignal(SignalName.EntryFinished);
    }

    /// <summary>dash_strike 冲刺打击：冲刺期间按节流间隔对触及敌机结算伤害（未购零开销）。</summary>
    private void TickDashStrike(float d)
    {
        if (_dashStrikeLevel <= 0)
        {
            return;
        }

        _dashStrikeTick -= d;
        if (_dashStrikeTick > 0f)
        {
            return;
        }

        _dashStrikeTick = DashStrikeDefaultInterval;
        var radiusSq = _dashStrikeRadius * _dashStrikeRadius;
        var enemies = GameState.Instance.Enemies;
        for (var i = enemies.Count - 1; i >= 0; i--)
        {
            if (enemies[i] is Enemy e && GodotObject.IsInstanceValid(e)
                && e.GlobalPosition.DistanceSquaredTo(GlobalPosition) <= radiusSq)
            {
                EntityDamage.Dispatch(e, _dashStrikeDamage * _dashStrikeLevel);
                Explosion.SpawnAt(GetParent(), e.GlobalPosition, 0.4f);
            }
        }
    }

    /// <summary>homing 制导增幅的落靶搜索：锁定锥 + 射程内最近注册表敌机（开火频次路径，零分配）。</summary>
    private Enemy? NearestAugHomingTarget(Vector2 aimDir)
    {
        Enemy? best = null;
        var bestD = _homingLockRange;
        var enemies = GameState.Instance.Enemies;
        for (var i = 0; i < enemies.Count; i++)
        {
            if (enemies[i] is not Enemy e || !GodotObject.IsInstanceValid(e))
            {
                continue;
            }

            var to = e.GlobalPosition - GlobalPosition;
            var d = to.Length();
            if (d > bestD || d <= 0.0f)
            {
                continue;
            }

            if (aimDir.Dot(to / d) < _homingLockConeCos)
            {
                continue;
            }

            bestD = d;
            best = e;
        }

        return best;
    }

    private void FireInternal(Vector2 aim)
    {
        var spread = _spreadShotCount;
        var pierce = _pierceCount;
        var explosive = _explosiveEnabled;
        var gs = GameState.Instance;
        // 辅助瞄准：准星在某标记敌框内 → 追踪修正；框外锥内 → 弱追踪
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
                    // NaN 守卫——cone_angle_deg=360 时 _coneCos=1 使 angT 0/0 得
                    // NaN，homingRate=NaN 恒不满足 ≤0 守卫（NaN 比较 false），弱追踪修正失控
                    if (homingRate <= 0.0f || float.IsNaN(homingRate))
                    {
                        homingTarget = null;
                    }
                }
            }
        }

        // salvo 齐射重弹：每 N 发（层数越多间隔越短）末发伤害乘区，计数以 Fire 调用为单位
        var heavyShot = false;
        if (_salvoInterval > 0)
        {
            _salvoCounter += 1;
            if (_salvoCounter >= _salvoInterval)
            {
                _salvoCounter = 0;
                heavyShot = true;
            }
        }

        // homing 制导增幅：辅助瞄准未锁定时，自行在锁定锥/射程内取最近敌机（弱追踪、长时限）
        var augmentHoming = false;
        if (homingTarget == null && _homingAugTurnRate > 0f)
        {
            homingTarget = NearestAugHomingTarget(aim.Normalized());
            if (homingTarget != null)
            {
                homingRate = _homingAugTurnRate;
                augmentHoming = true;
            }
        }

        // 散射弹道数恒为奇数（1/3/5，每层 +2）：偶数弹数扇形无中心弹（准星方向落空 = 负提升），
        // 居中索引即层数（spread=1→3 弹 [-1,0,+1]，spread=2→5 弹 [-2..+2]）
        var count = 1 + spread * 2;
        // 循环不变量外提；增幅 变化时缓存，开火路径零字典/Pow。
        var loopSpeed = BulletSpeedValue();
        var loopDamage = BulletDamageValue();
        if (heavyShot)
        {
            loopDamage = Mathf.Max(1, (int)Mathf.Round(loopDamage * _salvoDamageMult));
        }
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
                b.HomingTime = augmentHoming ? _homingLockTime : HomingTime;
                b.HomingTurnRate = homingRate;
            }

            b.Position = Position + aimRot * _muzzleOffset;
        }

        // 枪口辉光：以末发弹方向点亮（散射时多方向只有一盏，视觉噪声可控）
        if (_muzzleGlow != null)
        {
            _muzzleGlow.Position = aim * _muzzleOffset;
            _muzzleGlowA = 1.0f;
        }

        // 射击音效走 SfxPlayer 目录（独立 AudioStreamPlayer2D 裸 0dB 直打 Master 在持续
        // 射击下是炸耳主源）；三采样轮换防同采样疲劳，音量/抖动/复音/冷却由目录统一管
        GameState.Instance.PlaySfx(SfxId.FireA + _soundIndex);
        _soundIndex = (_soundIndex + 1) % FireSoundVariants;
    }

    /// <summary>受击结算（100 HP 制）。返回 true = 本帧实际结算。委托 PlayerDamage。
    /// 单参重载默认 Vector2.Inf（C# 默认参数须编译期常量，Vector2.Inf 非常量，拆重载保留"无方向均匀环"语义）。</summary>
    public bool TakeDamage(float amount = 1.0f) => TakeDamage(amount, Vector2.Inf);

    public bool TakeDamage(float amount, Vector2 fromPos)
    {
        return _damage.TakeDamage(amount, fromPos, this);
    }

    /// <summary>受击连锁：清除 250px 内全部敌弹（无分无特效）。</summary>
    public void ClearNearbyEnemyBullets()
    {
        var bullets = GameState.Instance.EnemyBullets;
        var clearRadiusSq = BulletClearRadius * BulletClearRadius; // 平方距离比较免每弹 sqrt
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

    /// <summary>擦弹——敌弹进入 GrazeArea（受击盒外环形带）计 1 次分。</summary>
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
        GameState.Instance.PlaySfx(SfxId.AugmentPick);
    }

    public int ParryPhase() => (int)_parry.Phase;

    public float ParryEnergyRatio() => _parry.EnergyRatio();

    /// <summary>盾区弹反：圆盘 shape 触发进入检测后径向距离过滤（360° 全周盾，arc_deg 配置保留
    /// 角度过滤能力——&lt;360 时回退为机头前方扇形），O(1) 阵营翻转。</summary>
    private void OnParryShieldEntered(Area2D area)
    {
        if (_parry.Phase != PlayerParry.ParryPhase.ACTIVE)
        {
            return;
        }

        // 弹反契约：敌弹与轰炸编队的下落炸弹都实现 IParryable（盾只判定并转交反射语义）
        if (area is not IParryable target)
        {
            return;
        }

        var b = area as Bullet;
        if (b != null && b.IsPlayerBullet)
        {
            return; // 玩家自己的弹不进盾判定（反射态炸弹已换层，不会再触发本回调）
        }

        var rel = area.GlobalPosition - GlobalPosition;
        if (rel.Length() > ParryRadius)
        {
            return;
        }

        var arc = Mathf.DegToRad(ParryArcDeg) * 0.5f;
        // 过滤基准用机头方向（含机身 Rotation）——-π/2 全局上方在
        // arc_deg<360 时过滤轴与机头垂直，与「机头前方扇形」矛盾；AngleDifference 已处理 ±π wrap
        var noseAngle = Vector2.Up.Rotated(Rotation).Angle();
        if (Mathf.Abs(Mathf.AngleDifference(rel.Angle(), noseAngle)) > arc)
        {
            return;
        }

        if (b != null && _deflectorReflectMult > 1.0f)
        {
            b.Damage = Mathf.Max(1, (int)Mathf.Round(b.Damage * _deflectorReflectMult));
        }

        if (!target.Reflect())
        {
            return; // 已反射/已失活：不进闪屏与音效（盾白挥一下，不误导）
        }

        _visuals.SetParryFlash();
        RumbleService.Parry(); // 弹反成功震动
        Explosion.SpawnAt(GetParent(), area.GlobalPosition, 0.5f);
        GameState.Instance.PlaySfx(SfxId.Dash);
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
        if (_dead)
        {
            return; // 幂等：同帧二次致死不重复结算，PlayerDied 不双发
        }

        _dead = true;
        AbortEntry(); // 入场期间自毁复位入场状态机
        _enrageSlow = 1.0f; // 死亡/重生路径兜底
        Hide();
        if (_hitbox != null)
        {
            _hitbox.SetDeferred("monitoring", false);
        }

        // 死亡路径关闭擦弹环与弹反盾判定
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
        Explosion.SpawnAt(GetParent(), Position, 2.0f, true); // 玩家侧残骸：琥珀钢碎片
        // PlayerDied 在 _dead 置位、死亡结算完成后发射：订阅者回调内 IsDead() 恒为 true，无时序陷阱
        GameState.Instance.EmitSignal(GameState.SignalName.PlayerDied);
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
        // 显式断开 GameState 信号连接（重入树不重复连接）
        var gs = GameState.Instance;
        if (gs.IsConnected(GameState.SignalName.AugmentsChanged, _onRefreshAugmentFactors))
        {
            gs.Disconnect(GameState.SignalName.AugmentsChanged, _onRefreshAugmentFactors);
        }

        if (gs.IsConnected(GameState.SignalName.AimAssistChanged, _onAimAssistLevelChanged))
        {
            gs.Disconnect(GameState.SignalName.AimAssistChanged, _onAimAssistLevelChanged);
        }

        if (gs.IsConnected(GameState.SignalName.JoySettingsChanged, _onJoySettingsChanged))
        {
            gs.Disconnect(GameState.SignalName.JoySettingsChanged, _onJoySettingsChanged);
        }

        var fogEvents = gs.FogEvents;
        if (fogEvents.IsConnected(FogEventManager.SignalName.FogEventStarted, _onFogEventStarted))
        {
            fogEvents.Disconnect(FogEventManager.SignalName.FogEventStarted, _onFogEventStarted);
        }

        if (fogEvents.IsConnected(FogEventManager.SignalName.FogEventEnded, _onFogEventEnded))
        {
            fogEvents.Disconnect(FogEventManager.SignalName.FogEventEnded, _onFogEventEnded);
        }

        if (fogEvents.IsConnected(FogEventManager.SignalName.FogDirectionShift, _onFogDirectionShift))
        {
            fogEvents.Disconnect(FogEventManager.SignalName.FogDirectionShift, _onFogDirectionShift);
        }

        // 子节点信号断开
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
}
