using Godot;

namespace InfiAir;

/// <summary>
/// 普通/精英敌机（M3b 全量迁移，2026-08-08 自 scripts/enemy.gd 迁移）：straight/sine/zigzag/
/// dive/spiral/noise/hover/aggressive 八种移动策略；single/spread/laser 弹种；入场两阶段
/// （下降→悬停机动）；寿命离场（不给分不计击杀）；分裂者；体碰信号事件驱动（P0-2）；
/// 慢速力场/母舰减速带；辅助瞄准标记；受击闪白；尾焰软光点（P0-5）。
/// 语义保持：cfg 热路径缓存、DDA 拉长开火间隔、view_world_rect 每物理帧缓存（静态共享）。
/// 迁移期动态访问：GameState 经 GameStateBridge；Player 为 GDScript 类（M3c 前）鸭子调用；
/// CinematicFx 为 GDScript 静态（经脚本资源 Call/Get）。
/// </summary>
public partial class Enemy : Area2D
{
    [Signal]
    public delegate void DiedEventHandler(Enemy enemy);

    // ---- 数值配置（_ready 从 balance.json 覆盖；与脚本默认值一致） ----
    public float EnemyBulletSpeed { get; private set; } = 420.0f;
    public float SpreadBulletSpeed { get; private set; } = 340.0f;
    public float LaserBulletSpeed { get; private set; } = 720.0f;
    public int BulletDamageSingle { get; private set; } = 12;
    public int BulletDamageSpread { get; private set; } = 10;
    public int BulletDamageLaser { get; private set; } = 20;
    public int CollisionDamage { get; private set; } = 20;
    public float SlowFieldFactor { get; private set; } = 0.8f;
    public float SpreadFanStep { get; private set; } = 0.314159f;
    public float Lifetime { get; private set; } = 15.0f;
    public float ExitAccel { get; private set; } = 520.0f;
    public float AggrChaseSpeed { get; private set; } = 140.0f;
    public float FireInterval { get; private set; } = 2.2f;
    /// <summary>悬停带：锚点 anchor_y 的取值范围（相对可见区域顶缘偏移）。</summary>
    public Vector2 HoverBand { get; private set; } = new(150.0f, 430.0f);
    public float HoverBobAmp { get; private set; } = 12.0f;
    public float HoverBobFreq { get; private set; } = 2.0f;
    public float HoverSwayAmp { get; private set; } = 34.0f;
    public float HoverSwayFreq { get; private set; } = 1.2f;
    public float SpiralDriftAmp { get; private set; } = 56.0f;
    public float SpiralDriftFreq { get; private set; } = 0.7f;
    public float SpiralRadius { get; private set; } = 50.0f;
    public float HpRampFactor { get; private set; } = 0.25f;
    public float SpeedRampFactor { get; private set; } = 0.1f;

    // ---- 尾焰软光点（P0-5 副轨） ----
    private const float TailGlowRadius = 26.0f;
    private const float TailGlowRadiusElite = 36.0f;
    private static readonly Color TailGlowColor = new(1.0f, 0.22f, 0.38f, 0.32f);
    private static readonly Color TailGlowColorElite = new(1.0f, 0.25f, 0.42f, 0.46f);

    // ---- 对局状态（setup/reactivate 写入；GDScript 调用方/测试读写） ----
    public StringName Strategy { get; set; } = "straight";
    public bool IsElite { get; private set; }
    public int Hp { get; set; } = 2;
    public float Speed { get; set; } = 140.0f;
    public bool CanShoot { get; set; }
    public int ScoreValue { get; set; } = 100;
    public StringName BulletType { get; private set; } = "single";
    /// <summary>悬停锚点 y（spawner 分配；&lt;0 时按悬停带自取）。</summary>
    public float AnchorY { get; set; } = -1.0f;
    /// <summary>辅助瞄准「强辅助」标记（P1-1；池化 deactivate 复位）。</summary>
    public bool AimMarked { get; private set; }

    private bool _split;
    private float _difficulty = 1.0f;
    private Godot.Collections.Dictionary _typeConfig = new();
    private float _time;
    private float _phase;
    private float _spawnX;
    private float _fireTimer = 2.2f;
    private EnemyMoveStrategy? _strategy;
    private readonly MoveCtx _moveCtx = new();
    private EnemyPool? _pool;
    private bool _active;
    private bool _repooling;
    private bool _bodyContact;
    private bool _hovering;
    private float _lifeTimer;
    private bool _exiting;
    private Vector2 _exitDir = Vector2.Up;
    private float _exitSpeed;
    private float _summonSlowTimer;
    private float _summonSlowFactor = 1.0f;
    private bool _slowFieldOn;
    private Sprite2D? _sprite;
    private CollisionShape2D? _shape;
    private Sprite2D? _tailGlow;
    private float _scoreScale = 1.0f;
    private float _flashTimer;
    private const float FlashTime = 0.1f;
    private float _shakeDieNormal = 5.0f;
    private float _shakeDieElite = 9.0f;

    private readonly Callable _onBuffsChanged;
    private readonly GDScript _cinematicFx;

    /// <summary>热路径缓存：view_world_rect / player_ref 每物理帧一次动态调用（全敌机共享）。</summary>
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

    public Enemy()
    {
        _onBuffsChanged = Callable.From(OnBuffsChanged);
        _cinematicFx = GD.Load<GDScript>("res://scripts/cinematic_fx.gd");
    }

    public override void _Ready()
    {
        GameStateBridge.Call("bind_enemy", this); // 统一绑定：add_to_group("enemy") + 注册 + entity_registered
        EnemyBulletSpeed = (float)GameStateBridge.Call("cfg", "enemies.bullet_speed", EnemyBulletSpeed).AsDouble();
        SpreadBulletSpeed = (float)GameStateBridge.Call("cfg", "enemies.spread_bullet_speed", SpreadBulletSpeed).AsDouble();
        LaserBulletSpeed = (float)GameStateBridge.Call("cfg", "enemies.laser_bullet_speed", LaserBulletSpeed).AsDouble();
        BulletDamageSingle = (int)GameStateBridge.Call("cfg", "enemies.bullet_damage.single", BulletDamageSingle).AsInt64();
        BulletDamageSpread = (int)GameStateBridge.Call("cfg", "enemies.bullet_damage.spread", BulletDamageSpread).AsInt64();
        BulletDamageLaser = (int)GameStateBridge.Call("cfg", "enemies.bullet_damage.laser", BulletDamageLaser).AsInt64();
        CollisionDamage = (int)GameStateBridge.Call("cfg", "enemies.collision_damage", CollisionDamage).AsInt64();
        SlowFieldFactor = (float)GameStateBridge.Call("cfg", "buffs.slow_field.factor", SlowFieldFactor).AsDouble();
        SpreadFanStep = (float)GameStateBridge.Call("cfg", "enemies.spread_fan_step", SpreadFanStep).AsDouble();
        Lifetime = (float)GameStateBridge.Call("cfg", "enemies.lifetime", Lifetime).AsDouble();
        ExitAccel = (float)GameStateBridge.Call("cfg", "enemies.exit_accel", ExitAccel).AsDouble();
        AggrChaseSpeed = (float)GameStateBridge.Call("cfg", "enemies.aggressive_chase_speed", AggrChaseSpeed).AsDouble();
        FireInterval = (float)GameStateBridge.Call("cfg", "enemies.fire_interval", FireInterval).AsDouble();
        // H19：hover_band 判型回退（防非数组 _ready 崩溃）
        var band = GameStateBridge.Call("cfg", "enemies.hover_band", new Godot.Collections.Array { HoverBand.X, HoverBand.Y });
        if (band.VariantType == Variant.Type.Array)
        {
            var arr = band.AsGodotArray();
            if (arr.Count >= 2)
            {
                HoverBand = new Vector2((float)arr[0].AsDouble(), (float)arr[1].AsDouble());
            }
        }

        HoverBobAmp = (float)GameStateBridge.Call("cfg", "enemies.hover_bob_amp", HoverBobAmp).AsDouble();
        HoverBobFreq = (float)GameStateBridge.Call("cfg", "enemies.hover_bob_freq", HoverBobFreq).AsDouble();
        HoverSwayAmp = (float)GameStateBridge.Call("cfg", "enemies.hover_sway_amp", HoverSwayAmp).AsDouble();
        HoverSwayFreq = (float)GameStateBridge.Call("cfg", "enemies.hover_sway_freq", HoverSwayFreq).AsDouble();
        SpiralDriftAmp = (float)GameStateBridge.Call("cfg", "enemies.spiral_drift_amp", SpiralDriftAmp).AsDouble();
        SpiralDriftFreq = (float)GameStateBridge.Call("cfg", "enemies.spiral_drift_freq", SpiralDriftFreq).AsDouble();
        SpiralRadius = (float)GameStateBridge.Call("cfg", "enemies.spiral_radius", SpiralRadius).AsDouble();
        // 每个实例独立形状，避免共享 sub_resource 半径互相影响
        _shape = GetNode<CollisionShape2D>("CollisionShape2D");
        if (_shape.Shape != null)
        {
            _shape.Shape = (Shape2D)_shape.Shape.Duplicate();
        }

        _spawnX = Position.X;
        _phase = GD.Randf() * Mathf.Tau;
        _fireTimer = (float)GD.RandRange(1.0, Mathf.Max(FireInterval, 1.0));
        _strategy = MakeStrategy();
        _strategy.Reset(this);
        // 尾焰软光点（P0-5 副轨）
        var glowRadius = IsElite ? TailGlowRadiusElite : TailGlowRadius;
        _tailGlow = (Sprite2D)_cinematicFx.Call("soft_glow", glowRadius * (float)GameStateBridge.Get("world_scale").AsDouble(), TailGlowColor);
        _tailGlow.ShowBehindParent = true;
        AddChild(_tailGlow);
        UpdateTailGlow();
        _shakeDieNormal = (float)GameStateBridge.Call("cfg", "effects.shake.enemy_die", _shakeDieNormal).AsDouble();
        _shakeDieElite = (float)GameStateBridge.Call("cfg", "effects.shake.elite_die", _shakeDieElite).AsDouble();
        _slowFieldOn = (int)GameStateBridge.Call("buff_count", new StringName("slow_field")) > 0;
        var gs = GameStateBridge.Instance;
        if (gs != null && !gs.IsConnected("buffs_changed", _onBuffsChanged))
        {
            gs.Connect("buffs_changed", _onBuffsChanged);
        }

        AreaEntered += OnAreaEntered;
        AreaExited += OnAreaExited;
    }

    public override void _ExitTree()
    {
        // Q19：池化 reparent 只注销注册表、不发 entity_unregistered
        if (_repooling)
        {
            GameStateBridge.Call("unregister_enemy", this);
        }
        else
        {
            GameStateBridge.Call("unbind_enemy", this);
        }

        // L02：buff 信号断开（C22 模式；池化 reparent 复用由 Reactivate 对称重连）
        var gs = GameStateBridge.Instance;
        if (gs != null && gs.IsConnected("buffs_changed", _onBuffsChanged))
        {
            gs.Disconnect("buffs_changed", _onBuffsChanged);
        }

        // 池内 reparent 也会经过此回调（_repooling 置位），不算离开池
        if (_pool != null && GodotObject.IsInstanceValid(_pool) && !_repooling)
        {
            _pool.Forget(this);
        }
    }

    /// <summary>setup：config 驱动数值/外观（_ready 之前调用，不用 @onready）。</summary>
    public void Setup(Godot.Collections.Dictionary config, StringName pStrategy, float pDifficulty, StringName pBulletType)
    {
        Strategy = pStrategy;
        _typeConfig = config;
        _split = (bool)config.GetValueOrDefault("split", false);
        _difficulty = pDifficulty;
        IsElite = (bool)config.GetValueOrDefault("elite", false);
        var hpRange = (Vector2)config["hp"];
        Hp = Mathf.Max(
            1,
            (int)Mathf.Round(
                GD.RandRange(hpRange.X, hpRange.Y)
                * (float)GameStateBridge.Call("enemy_hp_multiplier").AsDouble()
                * (1.0f + (float)GameStateBridge.Call("cfg", "enemies.hp_ramp_factor", HpRampFactor).AsDouble() * (pDifficulty - 1.0f))));
        ScoreValue = (int)config["score"].AsInt64();
        CanShoot = GD.Randf() < (float)config["fire"].AsDouble();
        FireInterval = (float)config.GetValueOrDefault("fire_interval", 2.2).AsDouble();
        var pool = (Godot.Collections.Array)config.GetValueOrDefault("bullet_types", new Godot.Collections.Array { new StringName("single") });
        if (pool.Count == 0)
        {
            pool = new Godot.Collections.Array { new StringName("single") }; // H07：空弹种池回退单发
        }

        BulletType = pBulletType != new StringName()
            ? pBulletType
            : (StringName)pool[(int)(GD.Randi() % (uint)pool.Count)];
        var speedRange = (Vector2)config["speed"];
        Speed = (float)GD.RandRange(speedRange.X, speedRange.Y)
            * (1.0f + (float)GameStateBridge.Call("cfg", "enemies.speed_ramp_factor", SpeedRampFactor).AsDouble() * (pDifficulty - 1.0f))
            * (float)GameStateBridge.Call("enemy_speed_multiplier").AsDouble();
        var sprite = GetNode<Sprite2D>("Sprite2D");
        var shapeNode = GetNode<CollisionShape2D>("CollisionShape2D");
        sprite.Texture = (Texture2D)config["texture"];
        AimMarked = GD.Randf() < (float)GameStateBridge.Call("cfg", "player.aim_assist.mark_ratio", 0.25).AsDouble();
        var sc = (float)config.GetValueOrDefault("scale", 0.85).AsDouble();
        sprite.Scale = new Vector2(sc, sc) * (float)GameStateBridge.Get("world_scale").AsDouble();
        var hitR = (float)config.GetValueOrDefault("radius", 30.0).AsDouble() * (float)GameStateBridge.Get("world_scale").AsDouble();
        if (shapeNode.Shape is CircleShape2D circle)
        {
            circle.Radius = hitR;
        }

        SetMeta("aim_frame_radius", hitR); // G07：辅助框半径缓存随 setup 刷新
    }

    public void Setup(Godot.Collections.Dictionary config, StringName pStrategy, float pDifficulty)
    {
        Setup(config, pStrategy, pDifficulty, new StringName());
    }

    /// <summary>分裂者标记（子机复用 config 后取消，防止无限分裂）。</summary>
    public void SetSplit(bool enabled) => _split = enabled;

    /// <summary>对外公开接口（A1 修复）：语义化类型查询（Boss override 返回 true）。</summary>
    public bool IsBoss() => false;

    public bool Hovering() => _hovering;

    public void SetFireTimer(float seconds) => _fireTimer = seconds;

    public void FireAtPlayer() => FireAtPlayerInternal();

    public void SetLifeTimer(float seconds) => _lifeTimer = seconds;

    public void SetPool(EnemyPool pool) => _pool = pool;

    public bool IsActive() => _active;

    public void SetRepooling(bool value) => _repooling = value;

    public bool IsExiting() => _exiting;

    /// <summary>母舰减速带剩余时长（A7 遗留清理：测试/诊断公开查询）。</summary>
    public float SummonSlowTimer() => _summonSlowTimer;

    /// <summary>池化复用：全状态重置（spawner 经 EnemyPool 调用；直接实例化走 _ready 初始化）。</summary>
    public void Reactivate(
        Godot.Collections.Dictionary config, StringName pStrategy, float pDifficulty, StringName pBulletType)
    {
        // L02：池化复用重连 buff 信号（_ready 只执行一次，_exit_tree 断开后必须重连）
        var gs = GameStateBridge.Instance;
        if (gs != null && !gs.IsConnected("buffs_changed", _onBuffsChanged))
        {
            gs.Connect("buffs_changed", _onBuffsChanged);
        }

        OnBuffsChanged();
        _active = true;
        _time = 0.0f;
        _hovering = false;
        _exiting = false;
        _lifeTimer = 0.0f;
        _exitSpeed = 0.0f;
        _summonSlowTimer = 0.0f;
        _summonSlowFactor = 1.0f;
        _scoreScale = 1.0f;
        Visible = true;
        Monitoring = true;
        SetPhysicsProcess(true);
        _bodyContact = false; // P0-2：重叠标记复位（池化复用防残留）
        _sprite ??= GetNodeOrNull<Sprite2D>("Sprite2D");
        if (_sprite != null)
        {
            _sprite.Modulate = Colors.White;
        }

        _flashTimer = 0.0f; // P1-2：闪白计时复位
        GameStateBridge.Call("register_enemy", this);
        Setup(config, pStrategy, pDifficulty, pBulletType);
        UpdateTailGlow();
        _spawnX = Position.X;
        _phase = GD.Randf() * Mathf.Tau;
        _fireTimer = (float)GD.RandRange(1.0, Mathf.Max(FireInterval, 1.0));
        AnchorY = -1.0f;
        _strategy = MakeStrategy();
        _strategy.Reset(this);
    }

    public void Reactivate(Godot.Collections.Dictionary config, StringName pStrategy, float pDifficulty)
    {
        Reactivate(config, pStrategy, pDifficulty, new StringName());
    }

    /// <summary>池化回收：停用但保留实例。</summary>
    public void Deactivate()
    {
        _active = false;
        AimMarked = false; // 辅助瞄准标记复位，防池残留串到下一任使用者
        Visible = false;
        SetPhysicsProcess(false);
        _bodyContact = false; // P0-2：回收后 area_exited 未必投递
        GameStateBridge.Call("unregister_enemy", this);
        // 断开 died 信号的全部连接（死亡回放等监听方；C# [Signal] 连接不随接收方自动断开）
        foreach (var conn in GetSignalConnectionList(SignalName.Died))
        {
            var dict = (Godot.Collections.Dictionary)conn;
            var callable = (Callable)dict["callable"];
            if (IsConnected(SignalName.Died, callable))
            {
                Disconnect(SignalName.Died, callable);
            }
        }

        Position = new Vector2(-500.0f, -500.0f);
        CallDeferred(MethodName.DeferredDisableMonitoring);
    }

    /// <summary>物理回调内不能直改 monitoring，延迟到帧末；若已被重激活（同帧复用）则跳过。
    /// public：CallDeferred 需引擎注册。</summary>
    public void DeferredDisableMonitoring()
    {
        if (!_active)
        {
            Monitoring = false;
        }
    }

    public void ApplySlow(float duration, float factor)
    {
        _summonSlowTimer = duration;
        _summonSlowFactor = factor;
    }

    public void TakeDamage(int amount)
    {
        TakeDamage(amount, 1.0f);
    }

    public void TakeDamage(int amount, float scoreScale)
    {
        if (Hp <= 0)
        {
            return; // 已死亡待回收（同帧多发命中防重复结算）
        }

        Hp -= amount;
        _scoreScale = scoreScale;
        _sprite ??= GetNodeOrNull<Sprite2D>("Sprite2D");
        if (_sprite != null)
        {
            _sprite.Modulate = new Color(2.0f, 2.0f, 2.0f); // 受击闪白
        }

        _flashTimer = FlashTime;
        if (Hp <= 0)
        {
            Die();
        }
    }

    public void Die()
    {
        // 分裂者：死亡生成 2 小机（子机独立结算，母体分数照常给）
        if (_split)
        {
            SpawnSplitMinis();
        }

        GameStateBridge.Call("add_score", (int)(ScoreValue * _scoreScale));
        GameStateBridge.Call("add_kill");
        GameStateBridge.Call("try_lifesteal");
        GameStateBridge.Call(
            "play_sfx", GameStateBridge.Get(IsElite ? "SFX_EXPLOSION_BIG" : "SFX_EXPLOSION"));
        GameStateBridge.Call("shake", IsElite ? _shakeDieElite : _shakeDieNormal);
        Explosion.SpawnAt(GetParent(), GlobalPosition, IsElite ? 1.5f : 1.0f);
        EmitSignal(SignalName.Died, this);
        DespawnInternal();
    }

    // ---------------- GDScript 鸭子调用兼容桥（M3b 过渡，M7 删除） ----------------
    // GDScript 调用方经动态派发以 snake_case 访问 C# 类；混合群体循环（Enemy + GDScript
    // Boss/TurretBattery 并存）无法按类分派不同方法名，故保留 snake_case 别名转发
    // （C# 内部调用一律 PascalCase）。M7 全量迁移后删除本段。

    public void take_damage(int amount, float scoreScale) => TakeDamage(amount, scoreScale);

    public void take_damage(int amount) => TakeDamage(amount);

    public void die() => Die();

    public bool is_boss() => IsBoss();

    public bool hovering() => Hovering();

    public void set_fire_timer(float seconds) => SetFireTimer(seconds);

    public void fire_at_player() => FireAtPlayer();

    public void set_life_timer(float seconds) => SetLifeTimer(seconds);

    public bool is_active() => IsActive();

    public void set_repooling(bool value) => SetRepooling(value);

    public bool is_exiting() => IsExiting();

    public float summon_slow_timer() => SummonSlowTimer();

    public void apply_slow(float duration, float factor) => ApplySlow(duration, factor);

    public void set_split(bool enabled) => SetSplit(enabled);

    public void set_pool(EnemyPool pool) => SetPool(pool);

    public void deactivate() => Deactivate();

    public void reactivate(Godot.Collections.Dictionary config, StringName pStrategy, float pDifficulty, StringName pBulletType)
        => Reactivate(config, pStrategy, pDifficulty, pBulletType);

    public void reactivate(Godot.Collections.Dictionary config, StringName pStrategy, float pDifficulty)
        => Reactivate(config, pStrategy, pDifficulty);

    public void setup(Godot.Collections.Dictionary config, StringName pStrategy, float pDifficulty, StringName pBulletType)
        => Setup(config, pStrategy, pDifficulty, pBulletType);

    public void setup(Godot.Collections.Dictionary config, StringName pStrategy, float pDifficulty)
        => Setup(config, pStrategy, pDifficulty);

    public int hp { get => Hp; set => Hp = value; }

    public float speed { get => Speed; set => Speed = value; }

    public int score_value { get => ScoreValue; set => ScoreValue = value; }

    public bool can_shoot { get => CanShoot; set => CanShoot = value; }

    public float fire_interval { get => FireInterval; set => FireInterval = value; }

    public float anchor_y { get => AnchorY; set => AnchorY = value; }

    public StringName strategy { get => Strategy; set => Strategy = value; }

    public StringName bullet_type { get => BulletType; set => BulletType = value; }

    public bool is_elite { get => IsElite; set => IsElite = value; }

    public bool aim_marked { get => AimMarked; set => AimMarked = value; }

    // 测试/调用方读取的配置常量别名（原 GDScript 公开 var 语义；M7 删除）
    public Vector2 HOVER_BAND { get => HoverBand; set => HoverBand = value; }

    public float HOVER_BOB_AMP { get => HoverBobAmp; set => HoverBobAmp = value; }

    public float HOVER_BOB_FREQ { get => HoverBobFreq; set => HoverBobFreq = value; }

    public float HOVER_SWAY_AMP { get => HoverSwayAmp; set => HoverSwayAmp = value; }

    public float HOVER_SWAY_FREQ { get => HoverSwayFreq; set => HoverSwayFreq = value; }

    public float SPIRAL_DRIFT_AMP { get => SpiralDriftAmp; set => SpiralDriftAmp = value; }

    public float SPIRAL_DRIFT_FREQ { get => SpiralDriftFreq; set => SpiralDriftFreq = value; }

    public float SPIRAL_RADIUS { get => SpiralRadius; set => SpiralRadius = value; }

    public float SPREAD_FAN_STEP { get => SpreadFanStep; set => SpreadFanStep = value; }

    public float ENEMY_BULLET_SPEED { get => EnemyBulletSpeed; set => EnemyBulletSpeed = value; }

    public float SPREAD_BULLET_SPEED { get => SpreadBulletSpeed; set => SpreadBulletSpeed = value; }

    public float LASER_BULLET_SPEED { get => LaserBulletSpeed; set => LaserBulletSpeed = value; }

    public int BULLET_DAMAGE_SINGLE { get => BulletDamageSingle; set => BulletDamageSingle = value; }

    public int BULLET_DAMAGE_SPREAD { get => BulletDamageSpread; set => BulletDamageSpread = value; }

    public int BULLET_DAMAGE_LASER { get => BulletDamageLaser; set => BulletDamageLaser = value; }

    public int COLLISION_DAMAGE { get => CollisionDamage; set => CollisionDamage = value; }

    public float SLOW_FIELD_FACTOR { get => SlowFieldFactor; set => SlowFieldFactor = value; }

    public float LIFETIME { get => Lifetime; set => Lifetime = value; }

    public float EXIT_ACCEL { get => ExitAccel; set => ExitAccel = value; }

    public float AGGR_CHASE_SPEED { get => AggrChaseSpeed; set => AggrChaseSpeed = value; }

    public float FIRE_INTERVAL { get => FireInterval; set => FireInterval = value; }

    // pool_reuse_test 白盒断言访问（L02 信号保持连接 / slow_field 缓存复位）
    public Callable _on_buffs_changed => _onBuffsChanged;

    public bool _slow_field_on => _slowFieldOn;

    // ---------------- 三角函数查表（2048 项循环表 + 线性插值，全敌机共享一份） ----------------

    private const int TrigSize = 2048;
    private static float[]? _sinTable;

    public static float SinFast(float x)
    {
        if (_sinTable == null)
        {
            _sinTable = new float[TrigSize + 1];
            for (var i = 0; i <= TrigSize; i++)
            {
                _sinTable[i] = Mathf.Sin(Mathf.Tau * i / TrigSize);
            }
        }

        var t = Mathf.PosMod(x, Mathf.Tau) / Mathf.Tau * TrigSize;
        var idx = (int)t;
        return Mathf.Lerp(_sinTable[idx], _sinTable[idx + 1], t - idx);
    }

    public static float CosFast(float x) => SinFast(x + Mathf.Pi / 2.0f);

    // ---------------- 内部实现 ----------------

    /// <summary>slow_field 缓存刷新（热路径禁字典约定）。</summary>
    private void OnBuffsChanged()
    {
        _slowFieldOn = (int)GameStateBridge.Call("buff_count", new StringName("slow_field")) > 0;
    }

    /// <summary>A4a：按 strategy 构建移动策略实例（共享悬停常量注入；Q29 策略专属参数覆盖）。</summary>
    private EnemyMoveStrategy MakeStrategy()
    {
        var params_ = new Godot.Collections.Dictionary
        {
            ["hover_bob_amp"] = HoverBobAmp,
            ["hover_bob_freq"] = HoverBobFreq,
            ["hover_sway_amp"] = HoverSwayAmp,
            ["hover_sway_freq"] = HoverSwayFreq,
            ["spiral_drift_amp"] = SpiralDriftAmp,
            ["spiral_drift_freq"] = SpiralDriftFreq,
            ["spiral_radius"] = SpiralRadius,
            ["aggressive_chase_speed"] = AggrChaseSpeed,
        };
        var msRaw = GameStateBridge.Call("cfg", "enemies.move_strategies", new Godot.Collections.Dictionary());
        if (msRaw.VariantType == Variant.Type.Dictionary)
        {
            var strategyCfg = ((Godot.Collections.Dictionary)msRaw.AsGodotDictionary()).GetValueOrDefault(Strategy, new Godot.Collections.Dictionary());
            if (strategyCfg.VariantType == Variant.Type.Dictionary)
            {
                var sc = strategyCfg.AsGodotDictionary();
                foreach (var k in sc.Keys)
                {
                    params_[k] = sc[k]; // Q29：策略专属参数覆盖
                }
            }
        }

        var s = Strategy;
        if (s == "sine")
        {
            return new SineMove(params_);
        }

        if (s == "zigzag")
        {
            return new ZigzagMove(params_);
        }

        if (s == "dive")
        {
            return new DiveMove(params_);
        }

        if (s == "spiral")
        {
            return new SpiralMove(params_);
        }

        if (s == "noise")
        {
            return new NoiseMove(params_);
        }

        if (s == "aggressive")
        {
            return new AggressiveMove(params_);
        }

        return new HoverMove(params_); // straight / hover
    }

    /// <summary>尾焰光点同步：颜色/半径档按精英标记、位置贴纹理尾缘。</summary>
    private void UpdateTailGlow()
    {
        if (_tailGlow == null)
        {
            return;
        }

        _tailGlow.Modulate = IsElite ? TailGlowColorElite : TailGlowColor;
        var glowRadius = IsElite ? TailGlowRadiusElite : TailGlowRadius;
        var softTexSize = (float)_cinematicFx.Get("SOFT_TEX_SIZE").AsDouble();
        _tailGlow.Scale = Vector2.One * (glowRadius * (float)GameStateBridge.Get("world_scale").AsDouble() / (softTexSize * 0.5f));
        _sprite ??= GetNodeOrNull<Sprite2D>("Sprite2D");
        var texH = 190.0f;
        if (_sprite != null && _sprite.Texture != null)
        {
            texH = _sprite.Texture.GetHeight();
        }

        _tailGlow.Position = new Vector2(0.0f, texH * 0.5f * (_sprite?.Scale.Y ?? 1.0f) * 0.85f);
    }

    /// <summary>anchor_y 未由 spawner 分配时自取（惰性：取首个物理帧的最终出生位置）。</summary>
    private void ResolveAnchor()
    {
        if (AnchorY >= 0.0f)
        {
            return;
        }

        var view = CachedView();
        var bandTop = view.Position.Y + HoverBand.X;
        var bandBottom = view.Position.Y + HoverBand.Y;
        AnchorY = Position.Y > bandBottom
            ? 1.0e9f
            : Mathf.Clamp(Position.Y + (float)GD.RandRange(120.0, 240.0), bandTop, bandBottom);
    }

    /// <summary>撞击结算（P0-2 信号驱动版）：重叠标记置位期每物理帧调用。
    /// 无 _active 守卫：直实例化敌机 _active 恒 false（语义缺口），陈旧调用由 deactivate 防住。</summary>
    private void TryBodyCollision()
    {
        var player = CachedPlayer();
        if (player.VariantType == Variant.Type.Nil)
        {
            return;
        }

        var p = (GodotObject)player;
        p.Call(
            "take_damage",
            Mathf.Max(1, (int)Mathf.Round(CollisionDamage * (float)GameStateBridge.Call("enemy_damage_ramp").AsDouble())),
            GlobalPosition);
    }

    private void OnAreaEntered(Area2D area)
    {
        if (!area.IsInGroup("player_hitbox"))
        {
            return; // 玩家弹等其他 Area 忽略
        }

        _bodyContact = true;
        TryBodyCollision();
    }

    private void OnAreaExited(Area2D area)
    {
        if (area.IsInGroup("player_hitbox"))
        {
            _bodyContact = false;
        }
    }

    private void DespawnInternal()
    {
        if (_pool != null && GodotObject.IsInstanceValid(_pool))
        {
            _pool.Release(this);
        }
        else
        {
            QueueFree();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        var d = (float)delta;
        _time += d;
        UpdateFlash(d);
        if (_exiting)
        {
            // 寿命离场：向上或侧方加速，离场不给分、不计击杀
            _exitSpeed += ExitAccel * d;
            Position += _exitDir * _exitSpeed * d;
            var exitView = CachedView();
            if (Position.Y < exitView.Position.Y - 150.0f
                || Position.X < exitView.Position.X - 150.0f
                || Position.X > exitView.End.X + 150.0f)
            {
                DespawnInternal();
            }

            return;
        }

        _lifeTimer += d;
        if (_lifeTimer >= Lifetime)
        {
            BeginLifetimeExit();
            return;
        }

        if (AnchorY < 0.0f)
        {
            ResolveAnchor();
        }

        // 慢速力场 + 母舰减速带（仅位移，不影响射速/寿命/计时）
        var slowMult = _slowFieldOn ? SlowFieldFactor : 1.0f;
        if (_summonSlowTimer > 0.0f)
        {
            _summonSlowTimer -= d;
            slowMult *= _summonSlowFactor;
        }

        var mdelta = d * slowMult;
        var view = CachedView();
        // A4a/C06：复用 _moveCtx（字段原地更新，避免每帧分配）
        _moveCtx.View = view;
        _moveCtx.MDelta = mdelta;
        _moveCtx.Speed = Speed;
        _moveCtx.Time = _time;
        _moveCtx.Phase = _phase;
        _moveCtx.SpawnX = _spawnX;
        _moveCtx.AnchorY = AnchorY;
        _moveCtx.Hovering = _hovering;
        _moveCtx.Player = CachedPlayer().VariantType != Variant.Type.Nil ? (Node2D?)CachedPlayer() : null;
        _strategy?.Update(d, this, _moveCtx);
        // 到达锚点转入悬停机动（dive 冲刺期除外；spiral 以绕转中心为准）
        if (!_hovering)
        {
            var diving = _strategy != null && _strategy.IsDiving();
            var refY = _strategy != null ? _strategy.HoverReferenceY() : -1.0f;
            if (refY < 0.0f)
            {
                refY = Position.Y;
            }

            if (!diving && refY >= AnchorY)
            {
                _hovering = true;
            }
        }

        if (CanShoot)
        {
            // B 梯队：DDA 降档拉长开火间隔（只拉间隔不降收益）
            _fireTimer -= d / (float)GameStateBridge.Call("dda_factor").AsDouble();
            if (_fireTimer <= 0.0f)
            {
                _fireTimer = FireInterval;
                FireAtPlayerInternal();
            }
        }

        if (_bodyContact)
        {
            TryBodyCollision();
        }

        if (Position.Y > view.End.Y + 60.0f)
        {
            DespawnInternal();
        }
    }

    private void FireAtPlayerInternal()
    {
        var player = CachedPlayer();
        if (player.VariantType == Variant.Type.Nil)
        {
            return;
        }

        var playerNode = (Node2D)player;
        var baseDir = (playerNode.GlobalPosition - GlobalPosition).Normalized();
        if (baseDir == Vector2.Zero)
        {
            baseDir = Vector2.Down; // G026：与玩家圆心重合时回退，防零方向弹永不销毁
        }

        if (BulletType == "spread")
        {
            for (var i = 0; i < 5; i++)
            {
                SpawnEnemyBullet(baseDir.Rotated(SpreadFanStep * (i - 2)), SpreadBulletSpeed, new StringName("spread"));
            }
        }
        else if (BulletType == "laser")
        {
            SpawnEnemyBullet(baseDir, LaserBulletSpeed, new StringName("laser"));
        }
        else
        {
            SpawnEnemyBullet(baseDir, EnemyBulletSpeed, new StringName("single"));
        }
    }

    private void SpawnEnemyBullet(Vector2 dir, float bulletSpeed, StringName pType)
    {
        var dmg = BulletDamageSingle;
        if (pType == "spread")
        {
            dmg = BulletDamageSpread;
        }
        else if (pType == "laser")
        {
            dmg = BulletDamageLaser;
        }

        var pool = (GodotObject?)GameStateBridge.Get("bullet_pool");
        if (pool == null)
        {
            return;
        }

        var b = pool.Call("Fire", dir, bulletSpeed, dmg, false);
        if (b.VariantType == Variant.Type.Nil)
        {
            return; // P2-3：同屏敌弹硬上限
        }

        var bullet = (GodotObject)b;
        bullet.Set("position", Position); // b.position = position（敌方子弹出生在敌机位置）
        bullet.Call("set_meta", "bullet_type", pType); // 原生 Object 方法 snake_case（C# SetMeta 不注册进引擎表）
        if (pType == "laser")
        {
            // 细长高亮快速弹（Sprite2D 缓存引用）
            var poly = (Sprite2D?)bullet.Call("SpriteNode");
            if (poly != null)
            {
                poly.Scale = new Vector2(2.2f, 0.55f);
                poly.SelfModulate = new Color(1.0f, 0.85f, 0.35f); // P0-3：Sprite2D 无 color，用 self_modulate
            }
        }
    }

    /// <summary>寿命到期：向上或侧方加速离场（停火，不给分、不计击杀）。</summary>
    private void BeginLifetimeExit()
    {
        _exiting = true;
        CanShoot = false;
        if (GD.Randf() < 0.5f)
        {
            _exitDir = new Vector2((float)GD.RandRange(-0.6, 0.6), -1.0f).Normalized(); // 向上
        }
        else
        {
            // 就近侧方（略带上行），从较近的一侧离场（E06：960 硬编码改视口中心）
            _exitDir = new Vector2(
                Position.X < CachedView().GetCenter().X ? 1.0f : -1.0f, (float)GD.RandRange(-0.4, 0.0)).Normalized();
        }

        _exitSpeed = Speed;
    }

    /// <summary>分裂者死亡生成 2 小机：缩放 ×0.6 / HP 半 / 无分数 / 不开火 / 不再分裂。</summary>
    private void SpawnSplitMinis()
    {
        var pool = (GodotObject?)GameStateBridge.Get("enemy_pool");
        if (pool == null)
        {
            return;
        }

        for (var i = 0; i < 2; i++)
        {
            var mini = pool.Call(
                "Spawn", _typeConfig, Strategy, _difficulty,
                GlobalPosition + new Vector2(i == 0 ? 24.0f : -24.0f, 0.0f));
            if (mini.VariantType == Variant.Type.Nil || !GodotObject.IsInstanceValid((GodotObject)mini))
            {
                continue;
            }

            var e = (Enemy)mini;
            var miniSprite = e.GetNodeOrNull<Sprite2D>("Sprite2D");
            if (miniSprite != null)
            {
                miniSprite.Scale *= 0.6f;
            }

            e.Hp = Mathf.Max(1, (int)Mathf.Round(e.Hp * 0.5f));
            e.ScoreValue = 0;
            e.CanShoot = false;
            e.SetSplit(false);
        }
    }

    /// <summary>P1-2：受击闪白手动衰减（替代 Tween；线性 lerp 回本色，零分配）。</summary>
    private void UpdateFlash(float delta)
    {
        if (_flashTimer <= 0.0f)
        {
            return;
        }

        _flashTimer -= delta;
        _sprite ??= GetNodeOrNull<Sprite2D>("Sprite2D");
        if (_sprite == null)
        {
            return;
        }

        if (_flashTimer <= 0.0f)
        {
            _sprite.Modulate = Colors.White;
        }
        else
        {
            _sprite.Modulate = _sprite.Modulate.Lerp(Colors.White, delta / FlashTime);
        }
    }
}
