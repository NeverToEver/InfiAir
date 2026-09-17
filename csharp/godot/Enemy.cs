using Godot;
using InfiAir.Core.GameFeel;

namespace InfiAir;

/// <summary>
/// 普通/精英敌机：straight/sine/zigzag/
/// dive/spiral/noise/hover/aggressive 八种移动策略；single/spread/laser 弹种；入场两阶段
/// （下降→悬停机动）；寿命离场（不给分不计击杀）；分裂者；体碰信号事件驱动；
/// 慢速力场/母舰减速带；辅助瞄准标记；受击闪白；尾焰软光点。
/// 语义保持：cfg 热路径缓存、DDA 拉长开火间隔、可见区域经 FrameCache 每物理帧共享。
/// 实现 IDamageable/ISlowable：伤害统一分派与母舰减速场经接口直达，新增单位无需改分派器。
/// 实现 IAimTarget：辅助瞄准扫描按契约判型（与遭遇单位同路径）。
/// </summary>
public partial class Enemy : Area2D, IDamageable, ISlowable, IAimTarget
{
    [Signal]
    public delegate void DiedEventHandler(Enemy enemy);

    // 开火热路径 StringName 静态缓存（不得每发敌弹 new StringName）
    private static readonly StringName BulletTypeSingle = new("single");

    /// <summary>组名静态缓存（体碰热路径 IsInGroup 字符串字面量逐次转换）。</summary>
    private static readonly StringName GroupPlayerHitbox = new("player_hitbox");

    /// <summary>「未指定弹种」哨兵：spawn 每敌复用，替代每次调用 new StringName()（空间换时间）。</summary>
    internal static readonly StringName NoBulletType = new();
    private static readonly StringName BulletTypeSpread = new("spread");
    private static readonly StringName BulletTypeLaser = new("laser");

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
    // ---- 尾焰软光点 ----
    private const float TailGlowRadius = 26.0f;
    private const float TailGlowRadiusElite = 36.0f;
    private static readonly Color TailGlowColor = new(1.0f, 0.22f, 0.38f, 0.32f);
    private static readonly Color TailGlowColorElite = new(1.0f, 0.25f, 0.42f, 0.46f);

    // ---- 机体背光轮廓（阵营染色加法剪影，替代平面贴图的"平"感；随机体旋转/缩放） ----
    private static readonly Color RimGlowColor = new(1.0f, 0.30f, 0.46f, 0.34f);
    private static readonly Color RimGlowColorElite = new(1.0f, 0.34f, 0.52f, 0.46f);
    private Sprite2D? _rimGlow;

    // ---- 能量发光层（tscn GlowLayer，ship_energy shader 叠加；遮罩随主贴图变体同步） ----
    private Sprite2D? _glowLayer;
    // 阵营分档懒加载一次（effects.ship_energy.*；静态标量/结构体，非 Godot 对象）
    private static bool _glowCfgLoaded;
    // 回退值取 balance.json effects.ship_energy.tint_enemy / tint_elite 的定稿色（hex 与 json 逐位一致），
    // 防「键缺失/写错 → 落到与设计值有色差的旧常量」；正常路径由下方 CfgColor 覆盖
    private static Color _glowTintEnemy = new(0xc77fe6ff); // 紫晶
    private static Color _glowTintElite = new(0xff64bfff); // 淡品红
    private static float _glowIntEnemy = 0.30f;
    private static float _glowIntElite = 0.40f;

    // ---- 本局状态（Setup/Reactivate 写入；公开属性直读写） ----
    public StringName Strategy { get; set; } = "straight";
    public bool IsElite { get; private set; }
    public int Hp { get; set; } = 2;
    public float Speed { get; set; } = 140.0f;
    public bool CanShoot { get; set; }
    /// <summary>首发射延迟提示（秒；&lt;0 = 按随机初相）。教程弹反靶机用它把三机的开火错峰成
    /// 匀速弹流——随机初相若相近，齐射会永久同步（间隔恒定、初相差固定），弹幕成簇抵达。</summary>
    public float FireDelayHint { get; set; } = -1.0f;
    public int ScoreValue { get; set; } = 100;
    public StringName BulletType { get; private set; } = "single";
    /// <summary>悬停锚点 y（spawner 分配；&lt;0 时按悬停带自取）。</summary>
    public float AnchorY { get; set; } = -1.0f;
    /// <summary>辅助瞄准「强辅助」标记（池化 deactivate 复位）。</summary>
    public bool AimMarked
    {
        get => _aimMarked;
        private set
        {
            // 成对维护在屏标记计数（AimFrameLayer 零标记跳过扫描/重绘）；同值赋值幂等
            if (_aimMarked == value)
            {
                return;
            }

            _aimMarked = value;
            AimMarkedCount += value ? 1 : -1;
        }
    }

    /// <summary>在屏辅助标记敌计数（AimMarked setter 成对维护；AimFrameLayer 零标记门控）。
    /// 遭遇单位的标记另计（AimTargetCount），本计数只含普通敌机，语义不变。</summary>
    public static int AimMarkedCount { get; private set; }

    /// <summary>辅助框半径缓存（setup 写入，已含 world_scale；替代 aim_frame_radius meta 的
    /// HasMeta/GetMeta——经 <see cref="AimCollisionRadius"/> 直读）。&lt;0 = 未初始化（兼容路径回退读形状）。</summary>
    public float AimFrameRadius { get; internal set; } = -1.0f;

    // ---- IAimTarget 契约（辅助瞄准扫描只读量；判定算式在 AimFrameLayer 与 core AimTargeting） ----

    /// <summary>恒 true：注册表成员资格已表达「存活且在册」（Die → Deactivate → Unregister 同路径
    /// 移除），再叠一道存活判据会改普通敌机的既有辅瞄语义（同帧已死未注销的敌机原本仍是弱追踪目标）。
    /// 遭遇单位的可打判定另在各自实现里（升起/收回期不可打）。</summary>
    public bool AimTargetable => true;

    /// <summary>世界坐标（框心与锥角/距离判定基准）。</summary>
    public Vector2 AimWorldPosition => GlobalPosition;

    /// <summary>碰撞半径（已含 world_scale）：辅助框半宽 = 本值 + 档位 frame_pad（pad 单源在 AimFrameLayer）。
    /// 未经 setup 的兼容路径回退读碰撞形状并回填——原在 AimFrameLayer.FrameHalfSize 内，收归数据属主。</summary>
    public float AimCollisionRadius
    {
        get
        {
            var r = AimFrameRadius;
            if (r < 0.0f)
            {
                var shapeNode = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
                r = 0.0f;
                if (shapeNode != null && shapeNode.Shape is CircleShape2D circle)
                {
                    r = circle.Radius;
                }

                AimFrameRadius = r;
            }

            return r;
        }
    }

    private bool _aimMarked;

    private bool _split;
    private float _difficulty = 1.0f;
    private Godot.Collections.Dictionary _typeConfig = new();
    private float _time;
    private float _phase;
    private float _spawnX;
    private float _fireTimer = 2.2f;
    private EnemyMoveStrategy? _strategy;
    /// <summary>缓存策略实例对应的 Strategy 键（键未变则复用实例，免每 spawn 重建）。</summary>
    private StringName _strategyKey = new();
    private readonly MoveCtx _moveCtx = new();

    /// <summary>空间换时间：MakeStrategy 复用同一参数字典（Clear + 重填），替代每 spawn new Dictionary。</summary>
    private readonly Godot.Collections.Dictionary _strategyParams = new();
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
    /// <summary>slow_field 增幅 名（信号驱动 Refresh 用；静态 StringName 口径）。</summary>
    private static readonly StringName SlowFieldId = new("slow_field");
    /// <summary>slow_field 布尔缓存（AugmentsChanged 信号事件驱动，热路径禁字典）。</summary>
    private readonly AugmentBoolCache _slowCache;
    private Sprite2D? _sprite;
    private CollisionShape2D? _shape;
    private Sprite2D? _tailGlow;
    private float _scoreScale = 1.0f;
    private float _flashTimer;
    private Vector2 _flashBaseScale = Vector2.One; // 受击缩放回弹基准（非闪白期捕获，FlashFx 回位用）
    private const float FlashTime = 0.1f;
    /// <summary>寿命离场出屏判定余量（px）：顶/左/右三边对称，底边不入判定（离场方向向上/侧向）。</summary>
    private const float ExitDespawnMargin = 150.0f;
    private float _shakeDieNormal = 5.0f;
    private float _shakeDieElite = 9.0f;

    public Enemy()
    {
        _slowCache = new AugmentBoolCache(SlowFieldId);
    }

    public override void _Ready()
    {
        GameState.Instance.BindEnemy(this); // 统一绑定：add_to_group("enemy") + 注册 + entity_registered
        // 标量键判型回退 + 语义域钳（同文件 hover_band 判型）——坏类型（字符串/数组/字典）AsDouble/AsInt64 抛
        // InvalidCastException 崩溃；0/负值使敌弹静止或反向、伤害倒扣、slow_field 变加速场；
        // 当前默认数据全部在合法域内，行为零变化
        EnemyBulletSpeed = CfgFx.Float("enemies.bullet_speed", EnemyBulletSpeed, 0.0f);
        SpreadBulletSpeed = CfgFx.Float("enemies.spread_bullet_speed", SpreadBulletSpeed, 0.0f);
        LaserBulletSpeed = CfgFx.Float("enemies.laser_bullet_speed", LaserBulletSpeed, 0.0f);
        BulletDamageSingle = CfgFx.Int("enemies.bullet_damage.single", BulletDamageSingle, 0);
        BulletDamageSpread = CfgFx.Int("enemies.bullet_damage.spread", BulletDamageSpread, 0);
        BulletDamageLaser = CfgFx.Int("enemies.bullet_damage.laser", BulletDamageLaser, 0);
        CollisionDamage = CfgFx.Int("enemies.collision_damage", CollisionDamage, 0);
        // slow_field.factor 钳 [0,1]——>1 反而加速敌机、≤0 使慢速力场变加速场
        SlowFieldFactor = CfgFx.Float("augments.slow_field.factor", SlowFieldFactor, 0.0f, 1.0f);
        SpreadFanStep = CfgFx.Float("enemies.spread_fan_step", SpreadFanStep, 0.0f);
        // lifetime ≥0.05——≤0 使 _lifeTimer 首帧即达上限，敌机出生即寿命离场
        Lifetime = CfgFx.Float("enemies.lifetime", Lifetime, CfgFx.IntervalFloor);
        // exit_accel ≥0——负值使离场反向加速，寿命离场机永远离不开屏幕
        ExitAccel = CfgFx.Float("enemies.exit_accel", ExitAccel, 0.0f);
        AggrChaseSpeed = CfgFx.Float("enemies.aggressive_chase_speed", AggrChaseSpeed, 0.0f);
        // hover_band 判型回退（防非数组 _ready 崩溃）
        var band = GameState.Instance.Cfg("enemies.hover_band", new Godot.Collections.Array { HoverBand.X, HoverBand.Y });
        if (band.VariantType == Variant.Type.Array)
        {
            var arr = band.AsGodotArray();
            if (arr.Count >= 2)
            {
                HoverBand = new Vector2((float)arr[0].AsDouble(), (float)arr[1].AsDouble());
            }
        }

        // hover/spiral 几何参数同款判型 + 非负钳（负振幅/频率
        // 使机动轨迹反向、负半径绕转中心反向，属配置损坏语义；回退默认 + 钳制保底）
        HoverBobAmp = CfgFx.Float("enemies.hover_bob_amp", HoverBobAmp, 0.0f);
        HoverBobFreq = CfgFx.Float("enemies.hover_bob_freq", HoverBobFreq, 0.0f);
        HoverSwayAmp = CfgFx.Float("enemies.hover_sway_amp", HoverSwayAmp, 0.0f);
        HoverSwayFreq = CfgFx.Float("enemies.hover_sway_freq", HoverSwayFreq, 0.0f);
        SpiralDriftAmp = CfgFx.Float("enemies.spiral_drift_amp", SpiralDriftAmp, 0.0f);
        SpiralDriftFreq = CfgFx.Float("enemies.spiral_drift_freq", SpiralDriftFreq, 0.0f);
        SpiralRadius = CfgFx.Float("enemies.spiral_radius", SpiralRadius, 0.0f);
        // 每个实例独立形状，避免共享 sub_resource 半径互相影响
        _shape = GetNode<CollisionShape2D>("CollisionShape2D");
        if (_shape.Shape != null)
        {
            _shape.Shape = (Shape2D)_shape.Shape.Duplicate();
        }

        _spawnX = Position.X;
        _phase = GD.Randf() * Mathf.Tau;
        _fireTimer = FireDelayHint >= 0.0f ? FireDelayHint : (float)GD.RandRange(1.0, Mathf.Max(FireInterval, 1.0));
        EnsureStrategy();
        // 尾焰软光点
        var glowRadius = IsElite ? TailGlowRadiusElite : TailGlowRadius;
        _tailGlow = (Sprite2D)CinematicFx.SoftGlow(glowRadius * (float)GameState.Instance.WorldScale, TailGlowColor);
        _tailGlow.ShowBehindParent = true;
        AddChild(_tailGlow);
        UpdateTailGlow();
        // shake 幅度键判型 + 非负钳（负值传 GameState.Shake
        // 抖动幅度为负表现异常；坏类型回退默认）
        _shakeDieNormal = CfgFx.Float("effects.shake.enemy_die", _shakeDieNormal, 0.0f);
        _shakeDieElite = CfgFx.Float("effects.shake.elite_die", _shakeDieElite, 0.0f);
        _slowCache.Refresh();
        _slowCache.Connect(GameState.Instance);

        AreaEntered += OnAreaEntered;
        AreaExited += OnAreaExited;
    }

    public override void _ExitTree()
    {
        // 外部 queue_free（轨道打击清场等）不经 deactivate，离树兜底回标记计数（幂等；
        // 池化 reparent 豁免——回挂 Main 方向 Reactivate 已重掷标记，不得误清）
        if (!_repooling)
        {
            AimMarked = false;
        }

        // 池化 reparent 只注销注册表、不发 entity_unregistered
        // autoload 可能先于本节点释放（非常规拆树序），Instance getter 会抛异常，故安全取值
        var gs = GameState.TryGetInstance();
        if (gs != null)
        {
            if (_repooling)
            {
                gs.UnregisterEnemy(this);
            }
            else
            {
                gs.UnbindEnemy(this);
            }

            // 增幅 信号断开：只对真离树（外部 queue_free）执行。池化 reparent 也会触发本回调
            // （_repooling 置位），若在此断开，Spawn 期间 Reactivate 的 Connect 会被随后的回挂
            // 反手断掉——连断顺序倒置，缓存整个活跃期处于未连接态（中途加点 slow_field 对场上
            // 敌机无效，且无任何报错）。池化路径的连/断成对由 Reactivate/EnemyPool 回挂处保证。
            if (!_repooling)
            {
                _slowCache.Disconnect(gs);
            }
        }

        // 池内 reparent 也会经过此回调（_repooling 置位），不算离开池
        if (_pool != null && GodotObject.IsInstanceValid(_pool) && !_repooling)
        {
            _pool.Forget(this);
        }
    }

    /// <summary>setup：config 驱动数值/外观（_ready 之前调用，不用 @onready）。</summary>
    /// <summary>从敌机配置的弹种池随机取一种；缺键/空池/坏值回退 single。
    /// 不分配默认 Array——原 GetValueOrDefault 的默认实参每 spawn 求值一次（空间换时间）。
    /// 抽签与实际入场分离：spread 同屏上限在 Setup 的收敛点判定（见 ResolveSpreadCapInternal）。</summary>
    private static StringName PickConfiguredBulletType(Godot.Collections.Dictionary config)
    {
        var raw = config.GetValueOrDefault("bullet_types", new Variant());
        if (raw.VariantType == Variant.Type.Array)
        {
            var pool = raw.AsGodotArray();
            if (pool.Count > 0)
            {
                return (StringName)pool[(int)(GD.Randi() % (uint)pool.Count)];
            }
        }

        return BulletTypeSingle; // 空弹种池/坏值回退单发
    }

    /// <summary>弹种名 → core 判定域（映射只此一处，避免名字字符串散落）。</summary>
    private static Core.Combat.EnemyBulletKind ToKind(StringName type)
    {
        if (type == BulletTypeSpread)
        {
            return Core.Combat.EnemyBulletKind.Spread;
        }

        if (type == BulletTypeLaser)
        {
            return Core.Combat.EnemyBulletKind.Laser;
        }

        return type == BulletTypeSingle ? Core.Combat.EnemyBulletKind.Single : Core.Combat.EnemyBulletKind.Other;
    }

    private static StringName FromKind(Core.Combat.EnemyBulletKind kind)
    {
        return kind switch
        {
            Core.Combat.EnemyBulletKind.Spread => BulletTypeSpread,
            Core.Combat.EnemyBulletKind.Laser => BulletTypeLaser,
            // Other 只可能来自未登记的弹种名；收敛判定不降级非 spread，落到单发（同空弹种池回退）
            _ => BulletTypeSingle,
        };
    }

    /// <summary>当前在册（在屏活跃）spread 弹种敌机数（离场中的不计）。
    /// 遍历注册表（只含活跃敌机）而非 "enemy" 组——池化敌机回收时不 remove_from_group，
    /// 组遍历会把池中闲置实例计入、虚抬 spread 上限。直迭代托管注册表：谓词走原生 Callable 时
    /// 每元素一次闭包派发 + Variant 编组，同一波逐只生成时是 O(波长 × 在册数) 的白工。</summary>
    private static int CountActiveSpreadEnemies()
    {
        var count = 0;
        foreach (var node in GameState.Instance.Enemies)
        {
            if (node is Enemy enemy && enemy.BulletType == BulletTypeSpread && !enemy.IsExiting())
            {
                count += 1;
            }
        }

        return count;
    }

    /// <summary>spread 同屏上限在**实际入场处**收敛（判定在 core SpreadCapPolicy）：敌机延后进场
    /// （先抽签、0.6s 后再 Spawn），同一波敌机的抽签发生在同一帧、在册数彼此不变，只在抽签处判会让
    /// 整波全部抽中 spread、上限形同虚设。本方法写 BulletType，是所有入场路径
    /// （波次预告超时 / Boss-3 召唤 / 分裂子机）的共同收口。池化复用与同帧逐只生成时，
    /// 前一任的弹种标记会随本机先注册（Reactivate）而被计入——故先落 single 再判定，
    /// 使「逐只递减配额」的语义与 core 单测一致。</summary>
    private void ApplyBulletTypeInternal(Godot.Collections.Dictionary config, StringName pBulletType)
    {
        var picked = pBulletType != NoBulletType ? pBulletType : PickConfiguredBulletType(config);
        if (picked != BulletTypeSpread)
        {
            BulletType = picked;
            return;
        }

        BulletType = BulletTypeSingle;
        var resolved = Core.Combat.SpreadCapPolicy.Resolve(
            ToKind(picked),
            CountActiveSpreadEnemies(),
            GameState.Instance.SpreadEnemyCap(),
            (bool)config.GetValueOrDefault("elite", false));
        BulletType = resolved == Core.Combat.EnemyBulletKind.Spread ? picked : FromKind(resolved);
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
                * (float)GameState.Instance.EnemyHpMultiplier()
                // hp_ramp 必须走 Load 时缓存的 API，不得直查 Cfg("enemies.hp_ramp_factor") 全链路
                // （path.Split+字典遍历+Variant 装箱）；改走显式难度重载——pDifficulty 为调用方快照
                // （分裂子机可传非全局 DifficultyMultiplier 值，须保持原参数语义）
                * (float)GameState.Instance.EnemyHpRamp(pDifficulty)));
        ScoreValue = (int)config["score"].AsInt64();
        CanShoot = GD.Randf() < (float)config["fire"].AsDouble();
        // 开火间隔随难度缩短但保有地板（密度可升、射速不得突破可反应下限）：
        // 原实现完全不吃难度，后期弹幕「不更密但更痛」，与弹幕系加压方式相悖。
        // 斜率/地板在 core DifficultyScaling；pDifficulty 保持调用方快照语义。
        FireInterval = (float)Core.Progression.DifficultyScaling.FireInterval(
            config.GetValueOrDefault("fire_interval", 2.2).AsDouble(), pDifficulty, GameState.Instance.Scaling());
        ApplyBulletTypeInternal(config, pBulletType);
        var speedRange = (Vector2)config["speed"];
        Speed = (float)GD.RandRange(speedRange.X, speedRange.Y)
            // speed_ramp 必须走 Load 时缓存的 ramp API，不得每 spawn 直查 Cfg 全链路
            // （上方 hp_ramp 同款模式；pDifficulty 保持调用方快照语义）
            * (float)GameState.Instance.EnemySpeedRamp(pDifficulty)
            * (float)GameState.Instance.EnemySpeedMultiplier();
        // 复用 _sprite/_shape 字段缓存（池化路径 _ready 已取；直实例化 setup 先于 _ready 时 ??= 回填）
        var sprite = _sprite ??= GetNode<Sprite2D>("Sprite2D");
        var shapeNode = _shape ??= GetNode<CollisionShape2D>("CollisionShape2D");
        sprite.Texture = (Texture2D)config["texture"];
        // mark_ratio 同款——走 Load 时缓存 API，免每 spawn Cfg 全链路
        AimMarked = GD.Randf() < (float)GameState.Instance.AimMarkRatio();
        // 二级回退（机型表本身缺键时才走）：取 balance.json enemies.types[0] 定稿值，
        // 与 Spawner 脚本默认同源，防「表损坏 → 落到与设计值无关的旧常量」
        var sc = (float)config.GetValueOrDefault("scale", 0.80).AsDouble();
        sprite.Scale = new Vector2(sc, sc) * (float)GameState.Instance.WorldScale;
        var hitR = (float)config.GetValueOrDefault("radius", 41.0).AsDouble() * (float)GameState.Instance.WorldScale;
        if (shapeNode.Shape is CircleShape2D circle)
        {
            circle.Radius = hitR;
        }

        AimFrameRadius = hitR; // 辅助框半径缓存随 setup 刷新（meta 改实例字段直读）
        UpdateRimGlow();
        UpdateGlowLayer(sprite);
    }

    /// <summary>能量发光层（GlowLayer）：遮罩随主贴图变体同步派生（同资源路径 "_glow.png"），
    /// tint/强度按 敌机/精英 分档；纯视觉叠加层，缺节点/缺遮罩静默保持默认。</summary>
    private void UpdateGlowLayer(Sprite2D sprite)
    {
        _glowLayer ??= sprite.GetNodeOrNull<Sprite2D>(ShipEnergyFx.NodeName);
        if (_glowLayer == null)
        {
            return;
        }

        var glowTex = ShipEnergyFx.GlowTextureFor(sprite.Texture);
        if (glowTex != null)
        {
            _glowLayer.Texture = glowTex;
        }

        if (!_glowCfgLoaded)
        {
            _glowTintEnemy = ShipEnergyFx.CfgColor("effects.ship_energy.tint_enemy", _glowTintEnemy);
            _glowTintElite = ShipEnergyFx.CfgColor("effects.ship_energy.tint_elite", _glowTintElite);
            _glowIntEnemy = CfgFx.Float("effects.ship_energy.intensity_enemy", _glowIntEnemy, 0.0f);
            _glowIntElite = CfgFx.Float("effects.ship_energy.intensity_elite", _glowIntElite, 0.0f);
            _glowCfgLoaded = true;
        }

        ShipEnergyFx.Apply(
            _glowLayer,
            IsElite ? _glowTintElite : _glowTintEnemy,
            IsElite ? _glowIntElite : _glowIntEnemy);
    }

    /// <summary>机体背光轮廓：贴图同源副本 + 加性材质 + 阵营染色，略放大垫在机体之下，
    /// 给平面精灵加一圈"受光剪影"（纯装饰子节点，随 Sprite2D 旋转/缩放自动跟随）。</summary>
    private void UpdateRimGlow()
    {
        _sprite ??= GetNodeOrNull<Sprite2D>("Sprite2D");
        if (_sprite == null || _sprite.Texture == null)
        {
            return;
        }

        if (_rimGlow == null)
        {
            _rimGlow = new Sprite2D
            {
                Texture = _sprite.Texture,
                Scale = new Vector2(1.16f, 1.16f),
                Material = CinematicFx.AdditiveMaterial(),
                ZIndex = -1,
                ShowBehindParent = true,
            };
            _sprite.AddChild(_rimGlow);
        }

        _rimGlow.Texture = _sprite.Texture;
        _rimGlow.Modulate = IsElite ? RimGlowColorElite : RimGlowColor;
    }

    public void Setup(Godot.Collections.Dictionary config, StringName pStrategy, float pDifficulty)
    {
        Setup(config, pStrategy, pDifficulty, NoBulletType);
    }

    /// <summary>分裂者标记（子机复用 config 后取消，防止无限分裂）。</summary>
    public void SetSplit(bool enabled) => _split = enabled;

    /// <summary>对外公开接口：语义化类型查询（Boss override 返回 true）。</summary>
    public bool IsBoss() => false;

    public bool Hovering() => _hovering;

    public void SetPool(EnemyPool pool) => _pool = pool;

    public bool IsActive() => _active;

    public void SetRepooling(bool value) => _repooling = value;

    /// <summary>池化回挂后重连增幅 缓存（幂等）。Spawn 期的 Reactivate 已连一次，但若后续发生
    /// reparent，_exit_tree 的断开（repooling 门控）与 Connect 的先后顺序必须由回挂处收口——
    /// 与同处的 RegisterEnemy 补偿同一理由，回挂后缓存必须处于已连接态。</summary>
    public void ReconnectAugmentCache()
    {
        _slowCache.Connect(GameState.Instance);
        _slowCache.Refresh();
    }

    public bool IsExiting() => _exiting;

    /// <summary>只读探针口：slow_field 缓存是否处于已连接态。池化复用的 reparent 会触发
    /// `_ExitTree`，连/断错序时该敌机整个活跃期不再随 AugmentsChanged 刷新（「买了力场没感觉」，
    /// 零报错）——故这条不变量需要可判定，由探针在池复用回挂后断言。</summary>
    public bool IsAugmentCacheConnected() => _slowCache.IsConnectedTo(GameState.Instance);

    /// <summary>池化复用：全状态重置（spawner 经 EnemyPool 调用；直接实例化走 _ready 初始化）。</summary>
    public void Reactivate(
        Godot.Collections.Dictionary config, StringName pStrategy, float pDifficulty, StringName pBulletType)
    {
        // 池化复用重连增幅 信号（_ready 只执行一次，_exit_tree 断开后必须重连）
        _slowCache.Connect(GameState.Instance);
        _slowCache.Refresh();
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
        _bodyContact = false; // 重叠标记复位（池化复用防残留）
        _sprite ??= GetNodeOrNull<Sprite2D>("Sprite2D");
        if (_sprite != null)
        {
            _sprite.Modulate = Colors.White;
        }

        _flashTimer = 0.0f; // 闪白计时复位
        GameState.Instance.RegisterEnemy(this);
        Setup(config, pStrategy, pDifficulty, pBulletType);
        UpdateTailGlow();
        _spawnX = Position.X;
        _phase = GD.Randf() * Mathf.Tau;
        _fireTimer = FireDelayHint >= 0.0f ? FireDelayHint : (float)GD.RandRange(1.0, Mathf.Max(FireInterval, 1.0));
        AnchorY = -1.0f;
        EnsureStrategy();
    }

    public void Reactivate(Godot.Collections.Dictionary config, StringName pStrategy, float pDifficulty)
    {
        Reactivate(config, pStrategy, pDifficulty, NoBulletType);
    }

    /// <summary>池化回收：停用但保留实例。</summary>
    public void Deactivate()
    {
        _active = false;
        AimMarked = false; // 辅助瞄准标记复位，防池残留串到下一任使用者
        Visible = false;
        SetPhysicsProcess(false);
        _bodyContact = false; // 回收后 area_exited 未必投递
        GameState.Instance.UnregisterEnemy(this);
        // 断开 died 信号的全部连接（死亡回放等监听方；C# [Signal] 连接不随接收方自动断开）
        // HasConnections 门控——零订阅常态下免 GetSignalConnectionList 数组分配
        if (HasConnections(SignalName.Died))
        {
            foreach (var conn in GetSignalConnectionList(SignalName.Died))
            {
                var dict = (Godot.Collections.Dictionary)conn;
                var callable = (Callable)dict["callable"];
                if (IsConnected(SignalName.Died, callable))
                {
                    Disconnect(SignalName.Died, callable);
                }
            }
        }

        Position = new Vector2(-500.0f, -500.0f);
        // monitoring 关闭并入 EnemyPool._Process 帧末批量停放（不得逐敌 CallDeferred；
        // idle 帧处理同样处于物理回调外，语义不变）
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
            FlashFx.Hit(_sprite, ref _flashTimer, FlashTime, ref _flashBaseScale); // 受击闪白 + 缩放回弹
        }
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
            // Bullet.OnAreaEntered（物理 flush）内同步生成会触发 area_set_shape_disabled 报错，
            // 同步快照分裂参数、延迟到空闲帧生成（本机随后被回收，参数已捕获）。
            ScheduleSplitSpawn();
        }

        GameState.Instance.AddKillScore((int)(ScoreValue * _scoreScale));
        GameState.Instance.AddKill();
        GameState.Instance.TryLifesteal();
        GameState.Instance.PlaySfx(IsElite ? SfxId.ExplosionBig : SfxId.Explosion);
        GameState.Instance.Shake(IsElite ? _shakeDieElite : _shakeDieNormal);
        // 击杀顿帧（表现层，结算已完成）：精英比杂兵长，给多目标击杀一个短促的节拍
        GameState.Instance.RequestHitStop(HitStopTier.Kill);
        Explosion.SpawnAt(GetParent(), GlobalPosition, IsElite ? 1.5f : 1.0f);
        EmitSignal(SignalName.Died, this);
        DespawnInternal();
    }

    // ---------------- snake_case 兼容桥（aim_marked：AimMarked private set，Tutorial.cs 经此桥写入） ----------------

    public bool aim_marked { get => AimMarked; set => AimMarked = value; }

    /// <summary>正弦查表（热路径禁 Mathf.Sin；表 256 项线性插值）。</summary>
    private const int TrigSize = 256;
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
        // NaN/Inf 相位（Boss.cs:549 已知族）或浮点舍入可致越界，钳制保命；NaN 输入仍输出 NaN，与 Mathf.Sin 语义一致
        var idx = Mathf.Clamp((int)t, 0, TrigSize - 1);
        return Mathf.Lerp(_sinTable[idx], _sinTable[idx + 1], t - idx);
    }


    public static float CosFast(float x) => SinFast(x + Mathf.Pi / 2.0f);

    // ---------------- 内部实现 ----------------

    /// <summary>O 原则：移动策略工厂注册表（strategy 名 → 构造器；新增策略注册一行即可，
    /// 不再改 MakeStrategy 本体；默认 HoverMove 兜底 straight/hover）。
    /// 键用 StringName——string 键每 spawn 查找时 Strategy 隐式转 managed string 分配。</summary>
    private static readonly Dictionary<StringName, Func<Godot.Collections.Dictionary, EnemyMoveStrategy>> StrategyFactories = new()
    {
        ["sine"] = d => new SineMove(d),
        ["zigzag"] = d => new ZigzagMove(d),
        ["dive"] = d => new DiveMove(d),
        ["spiral"] = d => new SpiralMove(d),
        ["noise"] = d => new NoiseMove(d),
        ["aggressive"] = d => new AggressiveMove(d),
    };

    /// <summary>Strategy 键未变复用策略实例（构造参数为本局常量、可变状态由 Reset 复位，
    /// 与新建+Reset 语义一致）；键变才重建。出生/重激活共用入口。</summary>
    private void EnsureStrategy()
    {
        if (_strategy == null || _strategyKey != Strategy)
        {
            _strategy = MakeStrategy();
            _strategyKey = Strategy;
        }

        _strategy.Reset(this);
    }

    /// <summary>按 strategy 构建移动策略实例（共享悬停常量注入；策略专属参数覆盖）。</summary>
    private EnemyMoveStrategy MakeStrategy()
    {
        var params_ = _strategyParams;
        params_.Clear();
        params_["hover_bob_amp"] = HoverBobAmp;
        params_["hover_bob_freq"] = HoverBobFreq;
        params_["hover_sway_amp"] = HoverSwayAmp;
        params_["hover_sway_freq"] = HoverSwayFreq;
        params_["spiral_drift_amp"] = SpiralDriftAmp;
        params_["spiral_drift_freq"] = SpiralDriftFreq;
        params_["spiral_radius"] = SpiralRadius;
        params_["aggressive_chase_speed"] = AggrChaseSpeed;
        // move_strategies 子树只读 Load 时缓存引用（不得每 spawn Cfg 深拷贝整棵子树）；
        // 此处只读（参数拷贝进 params_ 后不再触碰源表），缓存引用无别名污染风险
        // TryGetValue 替代 GetValueOrDefault——后者默认实参 new Dictionary() 每 spawn 白分配一次
        if (GameState.Instance.MoveStrategies().TryGetValue(Strategy, out var strategyCfg)
            && strategyCfg.VariantType == Variant.Type.Dictionary)
        {
            var sc = strategyCfg.AsGodotDictionary();
            foreach (var k in sc.Keys)
            {
                params_[k] = sc[k]; // 策略专属参数覆盖
            }
        }

        return StrategyFactories.GetValueOrDefault(Strategy)?.Invoke(params_) ?? new HoverMove(params_); // straight=直行 / hover=悬停
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
        var softTexSize = (float)(float)CinematicFx.SoftTexSize;
        _tailGlow.Scale = Vector2.One * (glowRadius * (float)GameState.Instance.WorldScale / (softTexSize * 0.5f));
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

        var view = FrameCache.ViewRect();
        var bandTop = view.Position.Y + HoverBand.X;
        var bandBottom = view.Position.Y + HoverBand.Y;
        AnchorY = Position.Y > bandBottom
            ? 1.0e9f
            : Mathf.Clamp(Position.Y + (float)GD.RandRange(120.0, 240.0), bandTop, bandBottom);
    }

    /// <summary>撞击结算（信号驱动版）：重叠标记置位期每物理帧调用。
    /// 无 _active 守卫：直实例化敌机 _active 恒 false（语义缺口），陈旧调用由 deactivate 防住。</summary>
    private void TryBodyCollision()
    {
        var player = FrameCache.Player();
        if (player == null)
        {
            return;
        }

        // typed 直调（player_ref 恒为 Player；Boss.CheckBodyCollision 同款），
        // 不得每物理帧 p.Call("take_damage", ...) 动态派发
        var p = player as Player;
        if (p == null || !GodotObject.IsInstanceValid(p))
        {
            return;
        }

        p.TakeDamage(
            EnemyFx.RampCollisionDamage(CollisionDamage),
            GlobalPosition);
    }

    private void OnAreaEntered(Area2D area)
    {
        if (!area.IsInGroup(GroupPlayerHitbox))
        {
            return; // 玩家弹等其他 Area 忽略
        }

        _bodyContact = true;
        TryBodyCollision();
    }

    private void OnAreaExited(Area2D area)
    {
        if (area.IsInGroup(GroupPlayerHitbox))
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
            var exitView = FrameCache.ViewRect();
            if (Position.Y < exitView.Position.Y - ExitDespawnMargin
                || Position.X < exitView.Position.X - ExitDespawnMargin
                || Position.X > exitView.End.X + ExitDespawnMargin)
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
        var slowMult = _slowCache.Value ? SlowFieldFactor : 1.0f;
        if (_summonSlowTimer > 0.0f)
        {
            _summonSlowTimer -= d;
            slowMult *= _summonSlowFactor;
        }

        var mdelta = d * slowMult;
        var view = FrameCache.ViewRect();
        // 复用 _moveCtx（字段原地更新，避免每帧分配）
        _moveCtx.View = view;
        _moveCtx.MDelta = mdelta;
        _moveCtx.Speed = Speed;
        _moveCtx.Time = _time;
        _moveCtx.Phase = _phase;
        _moveCtx.SpawnX = _spawnX;
        _moveCtx.AnchorY = AnchorY;
        _moveCtx.Hovering = _hovering;
        _moveCtx.Player = FrameCache.Player();
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
            // DDA 降档拉长开火间隔（只拉间隔不降收益）
            _fireTimer -= d / FrameCache.DdaFactor();
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

    /// <summary>弹种→发射规格单点映射（O 原则：新增弹种只改此处；速度/伤害为实例字段
    /// 由 _ready 从 balance.json 读入，不能入静态表）。</summary>
    private readonly record struct EnemyBulletSpec(float Speed, int Damage, int Count);

    private EnemyBulletSpec BulletSpec()
    {
        if (BulletType == BulletTypeSpread)
        {
            return new EnemyBulletSpec(SpreadBulletSpeed, BulletDamageSpread, 5);
        }

        if (BulletType == BulletTypeLaser)
        {
            return new EnemyBulletSpec(LaserBulletSpeed, BulletDamageLaser, 1);
        }

        return new EnemyBulletSpec(EnemyBulletSpeed, BulletDamageSingle, 1);
    }

    private void FireAtPlayerInternal()
    {
        var player = FrameCache.Player();
        if (player == null)
        {
            return;
        }

        var baseDir = (player.GlobalPosition - GlobalPosition).Normalized();
        if (baseDir == Vector2.Zero)
        {
            baseDir = Vector2.Down; // 与玩家圆心重合时回退，防零方向弹永不销毁
        }

        var spec = BulletSpec();
        for (var i = 0; i < spec.Count; i++)
        {
            var dir = spec.Count > 1 ? baseDir.Rotated(SpreadFanStep * (i - 2)) : baseDir;
            SpawnEnemyBullet(dir, spec.Speed, spec.Damage, BulletType);
        }
    }

    private void SpawnEnemyBullet(Vector2 dir, float bulletSpeed, int damage, StringName pType)
    {
        var pool = GameState.Instance.BulletPool;
        if (pool == null)
        {
            return;
        }

        var b = pool!.Fire(dir, bulletSpeed, damage, false);
        if (b == null)
        {
            return; // 同屏敌弹硬上限
        }

        b.Position = Position; // 敌方子弹出生在敌机位置（typed；Player 开火同款）
        if (pType == BulletTypeLaser)
        {
            // 细长高亮快速弹（Sprite2D 缓存引用）
            var poly = b.SpriteNode();
            if (poly != null)
            {
                poly.Scale = new Vector2(2.2f, 0.55f);
                poly.SelfModulate = new Color(1.0f, 0.85f, 0.35f); // Sprite2D 无 color，用 self_modulate
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
            // 侧向离场（向镜像侧离场：左半区向右、右半区向左）
            _exitDir = new Vector2(
                Position.X < FrameCache.ViewRect().GetCenter().X ? 1.0f : -1.0f, (float)GD.RandRange(-0.4, 0.0)).Normalized();
        }

        _exitSpeed = Speed;
    }

    /// <summary>分裂者死亡生成 2 小机：同步快照分裂参数，生成延迟到空闲帧
    /// （Bullet.OnAreaEntered 物理 flush 内直接建实体触发 area_set_shape_disabled 报错）。
    /// 缩放 ×0.6 / HP 半 / 无分数 / 不开火 / 不再分裂。</summary>
    private void ScheduleSplitSpawn()
    {
        // typed 取用（不得 pool.Call("Spawn") Variant 装箱动态派发；
        // EntityManager.EnemyPool 属性保持 GodotObject——GameState 桥未重定型，取用侧 cast 对齐 Spawner 口径）
        var pool = GameState.Instance.EnemyPool as EnemyPool;
        if (pool == null)
        {
            return;
        }

        CallDeferred(MethodName.SpawnSplitMinisDeferred, _typeConfig, Strategy, _difficulty, GlobalPosition, pool);
    }

    /// <summary>延迟执行的分裂生成（空闲帧）。public：CallDeferred 需引擎注册。</summary>
    public void SpawnSplitMinisDeferred(
        Godot.Collections.Dictionary config, StringName strategy, float diff, Vector2 pos, EnemyPool pool)
    {
        if (!GodotObject.IsInstanceValid(pool))
        {
            return;
        }

        for (var i = 0; i < 2; i++)
        {
            // typed Spawn 直调（Spawn 必返回有效实例，无需 Nil/判活守卫）
            var e = pool.Spawn(config, strategy, diff,
                pos + new Vector2(i == 0 ? 24.0f : -24.0f, 0.0f));
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

    /// <summary>受击闪白手动衰减（替代 Tween；线性 lerp 回本色，零分配）。</summary>
    private void UpdateFlash(float delta)
    {
        if (_flashTimer <= 0.0f)
        {
            return;
        }

        _sprite ??= GetNodeOrNull<Sprite2D>("Sprite2D");
        if (_sprite == null)
        {
            return;
        }

        FlashFx.Update(_sprite!, ref _flashTimer, delta, FlashTime, Colors.White, ref _flashBaseScale);
    }
}
