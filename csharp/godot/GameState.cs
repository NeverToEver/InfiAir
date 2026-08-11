
using System.Collections.Generic;
using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 全局状态与信号总线：分数、击杀、生命、难度乘数、已选 buff。
/// M7 全量迁移（2026-08-09 自 autoload/game_state.gd）：唯一 autoload（project.godot 由主代理
/// 切换为 *res://csharp/godot/GameState.cs）。
/// 公开 API：PascalCase 主体；UPPER_SNAKE 常量同时提供同名实例属性（GDScript 经实例零适配
/// 可读）与静态 GetXxx() 访问器（规则 19：静态字段禁持 Godot 对象——集合常量访问器每次
/// 内部服务全部 C# typed 直调（U13：SaveManager/UserDB/TaskPool 的 snake 动态派发已清零，
/// snake 桥已删除——原"M7 过渡"注记失效）。
/// 信号迁移：C# [Signal] 注册名为 PascalCase（ScoreChanged 等），GDScript/csharp 连接方
/// （test/*.gd 的 connect 与 csharp 侧 Connect("snake")）需改连 PascalCase 名（主代理集中适配）。
/// 数值精度：GDScript float 为 64 位——纯标量（health/difficulty_multiplier 等）用 double
/// 逐位等价；仅与引擎 32 位 API（Vector2/Rect2/音量等）交互处显式 (float) 转换。
/// </summary>
public partial class GameState : Node
{
    // ---------------- 信号（原 GDScript snake 名 → C# PascalCase 注册名） ----------------

    /// <summary>得分变化。</summary>
    [Signal]
    public delegate void ScoreChangedEventHandler(int newScore);

    /// <summary>击杀连击变化（combo：当前连击数，0 = 已断连）。</summary>
    [Signal]
    public delegate void ComboChangedEventHandler(int combo);

    [Signal]
    public delegate void HealthChangedEventHandler(double newHealth);

    [Signal]
    public delegate void DifficultyChangedEventHandler(double newMultiplier);

    [Signal]
    public delegate void DifficultySelectedEventHandler(StringName difficulty);

    [Signal]
    public delegate void MilestoneReachedEventHandler(int score);

    [Signal]
    public delegate void PlayerDiedEventHandler();

    /// <summary>玩家实际结算受击（无敌/闪避/单帧守卫未结算不发）：Meta HUD 受击层数据源。
    /// U16：参数统一 float（发射/监听均为 float，原 double 声明不一致）。</summary>
    [Signal]
    public delegate void PlayerDamagedEventHandler(float amount, Vector2 fromPos);

    [Signal]
    public delegate void ScreenShakeEventHandler(double strength);

    /// <summary>RP 经济/任务/路线信号：暂无消费方（base_console 拉取驱动），保留 API 供未来事件驱动</summary>
    [Signal]
    public delegate void RpChangedEventHandler(int newRp);

    [Signal]
    public delegate void MissionCompletedEventHandler(StringName id);

    [Signal]
    public delegate void RouteChosenEventHandler(StringName line, StringName buffId);

    [Signal]
    public delegate void KeyBindingsChangedEventHandler();

    [Signal]
    public delegate void LocaleChangedEventHandler();

    [Signal]
    public delegate void ViewZoomChangedEventHandler(double factor);

    [Signal]
    public delegate void WindowSizeChangedEventHandler(StringName level);

    [Signal]
    public delegate void AimAssistChangedEventHandler(StringName level);

    [Signal]
    public delegate void ReduceFlashChangedEventHandler(bool enabled);

    [Signal]
    public delegate void MouseLockChangedEventHandler(bool enabled);

    /// <summary>P0-1 手柄设置：右摇杆瞄准灵敏度 + 摇杆死区（profile 持久化；变更时广播供 player 重读）</summary>
    [Signal]
    public delegate void JoySettingsChangedEventHandler(double aimSpeed, double deadzone);

    /// <summary>buff 层数任何变动（选取/路线合并/存档恢复/重开清空）后发出，驱动外观刷新</summary>
    [Signal]
    public delegate void BuffsChangedEventHandler();

    /// <summary>实体注册信号转发（docs/ENTITY_MANAGER.md：新功能订阅口，监听 EntityManager）</summary>
    [Signal]
    public delegate void EntityRegisteredEventHandler(Node node);

    [Signal]
    public delegate void EntityUnregisteredEventHandler(Node node);

    /// <summary>触屏虚拟控件开关变化（mobile touch；Main 联动 VirtualControls 启用）</summary>
    [Signal]
    public delegate void TouchControlsChangedEventHandler(bool enabled);

    /// <summary>PS 布局适配（P0-1 延伸）：SDL 标准位置（JOY_BUTTON_A=底部等）跨 Xbox/PS 一致，
    /// 仅物理标签不同——按已连接手柄 GUID/名称检测布局，供 UI/文档显示对应标签</summary>
    [Signal]
    public delegate void JoyLayoutChangedEventHandler(StringName layout);

    /// <summary>刷新点数（RefreshPoints）变化。</summary>
    [Signal]
    public delegate void RefreshPointsChangedEventHandler(int points);

    /// <summary>科技点变化（死亡结算入账/升级消费）；研究所 UI 数据源（局外成长，2026-08-09）</summary>
    [Signal]
    public delegate void TechPointsChangedEventHandler(long points);

    // ---------------- 全局数值配置中心 ----------------
    // A2 阶段 1：balance.json 的加载/查询/纯数值 ramp 已剥离到 BalanceService（组合委托）。
    // 缺失/损坏时全部回退脚本默认值；访问统一走 GameState.cfg("分层.路径", 默认值)；
    // 热路径在各自 _ready 缓存进成员变量。

    private const string BalancePathValue = "res://data/balance.json";
    public string BALANCE_PATH => BalancePathValue;

    /// <summary>A2 组合服务（均非 autoload，保持"唯一 autoload：GameState"约定；GameState 委托）
    /// M2（2026-08-08）：BalanceService/SfxPlayer/EntityManager 迁 C#，C# 侧 typed 直调
    /// （原 GDScript 经脚本资源实例化）。</summary>
    private readonly BalanceService _balanceService = new();

    /// <summary>V 系列（U19 注释失实清理）：M7 已 typed 直调（原「GDScript 薄壳」描述删除）。</summary>
    private readonly SaveManager _saveManager = new();

    private readonly SfxPlayer _sfxPlayer = new();

    /// <summary>实体注册信号转发（docs/ENTITY_MANAGER.md：新功能订阅口，监听 EntityManager）。</summary>
    /// <summary>统一实体管理器（C# typed；docs/ENTITY_MANAGER.md）。</summary>
    private readonly EntityManager _registry = new();

    /// <summary>迷雾事件管理器（2026-08-05 任务轮换/迷雾事件系统）：全局单例，挂 GameState 下
    /// 维持唯一 autoload 约定；对局中概率触发干扰事件（触发纪律/信号解耦见脚本头注释）</summary>
    private readonly FogEventManager _fogEvents = new();

    /// <summary>统一游戏事件管理器（docs/EVENT_MANAGER.md）：批量管理全部随机游戏事件（迷雾 +
    /// 遭遇）；fog 组经迷雾门面接线，encounter 组由 main 注册——见 scripts/event_manager.gd</summary>
    private readonly GameEventManager _events = new();

    /// <summary>2026-08-04 账户系统：本地用户数据库（M7 已 typed 直调；原「GDScript 薄壳」描述删除）。</summary>
    private readonly UserDB _userDb = new();

    /// <summary>2026-08-07：进程曲线 C# 桥（ProgressionInterop → InfiAir.Core.Progression 纯函数）——
    /// milestone_threshold / _recompute_difficulty / apply_run_save 批量推进转发，语义逐位等价</summary>
    private readonly ProgressionInterop _progression = new();

    /// <summary>第三轮拆域试点（2026-08-11）：局外成长 Meta 职责域——原 GameState.Meta.cs 全部职责
    /// （科技点结算/升级消费/开局 buff 预置）迁入 MetaService，GameState.Meta.cs 为门面转发；
    /// 与 BalanceService/SaveManager 等组合服务同构，保持唯一 autoload：GameState 约定。
    /// _userDb 经构造注入（字段初始化器不可引用实例字段，故在构造器赋值）。</summary>
    private readonly MetaService _meta;

    public GameState()
    {
        _meta = new MetaService(_userDb);
    }

    /// <summary>启动计时基准（autoload 最早生命周期点；--startup-time 时由 main 打印分段耗时）</summary>
    public int BootTicksMsec { get; set; } = 0;

    /// <summary>启动/读档时检测到损坏并已隔离备份（开始面板据此提示；读取正常后置回 false）</summary>
    public bool SaveCorrupt { get; set; } = false;

    public bool ProfileCorrupt { get; set; } = false;

    /// 静态缓存——autoload 在 root 下恒存在，测试进程同样适用）</summary>
    private static GameState? _instance;

    public static GameState Instance
    {
        get
        {
            if (_instance != null && GodotObject.IsInstanceValid(_instance))
            {
                return _instance;
            }

            var tree = (SceneTree?)Engine.GetMainLoop();
            _instance = tree?.Root?.GetNodeOrNull<GameState>("GameState");
            if (_instance == null)
            {
                throw new InvalidOperationException("GameState autoload 不可用");
            }

            return _instance;
        }
    }

    public override void _EnterTree()
    {
        BootTicksMsec = (int)Time.GetTicksMsec();
    }

    /// <summary>实体管理器（A2 阶段 4 起数据归 EntityManager，2026-08-05 演进：绑定样板/生命周期信号/
    /// 批量操作 API；docs/ENTITY_MANAGER.md。属性转发保持外部语法不变；M2 起内部改 C# PascalCase）。
    /// 热路径缓存，避免每帧 get_nodes_in_group 分配。
    /// enemy/boss 在 _ready/_exit_tree 时注册/注销，player 单独缓存引用。</summary>
    public Godot.Collections.Array<Node> Enemies => _registry.Enemies;

    /// <summary>P0-1（2026-08-05 审计）：敌弹注册表转发（death_replay 录制数据源，替代 get_children 遍历）</summary>
    public Godot.Collections.Array<GodotObject> EnemyBullets => _registry.EnemyBullets;

    public Node2D? PlayerRef
    {
        get => _registry.PlayerRef;
        set => _registry.PlayerRef = value;
    }

    /// <summary>玩家受击 Hitbox（player._ready/_exit_tree 维护；敌机/Boss 撞击逐帧轮询用）</summary>
    public Area2D? PlayerHitbox
    {
        get => _registry.PlayerHitbox;
        set => _registry.PlayerHitbox = value;
    }

    /// <summary>子弹对象池实例（由 bullet_pool.gd 在 _ready 时登记）</summary>
    public BulletPool? BulletPool
    {
        get => _registry.BulletPool;
        set => _registry.BulletPool = value;
    }

    /// <summary>敌机对象池实例（由 enemy_pool.gd 在 _ready 时登记）</summary>
    public GodotObject? EnemyPool
    {
        get => _registry.EnemyPool;
        set => _registry.EnemyPool = value;
    }

    /// <summary>辅助瞄准框覆盖层实例（由 aim_frame_layer.gd 在 _ready 时登记；player._fire 查询框内标记敌）</summary>
    public GodotObject? AimFrameLayer
    {
        get => _registry.AimFrameLayer;
        set => _registry.AimFrameLayer = value;
    }

    /// <summary>触屏虚拟输入层实例（mobile touch，由 main.gd 在 _ready 时创建并登记；
    /// player.aim_point 查询触屏瞄准基准）</summary>
    public GodotObject? VirtualControls
    {
        get => _registry.VirtualControls;
        set => _registry.VirtualControls = value;
    }

    /// <summary>迷雾事件管理器转发（全局单例访问口；挂本节点下，_ready 时 add_child）</summary>
    public FogEventManager FogEvents => _fogEvents;

    /// <summary>统一事件管理器转发（全局单例访问口；挂本节点下，_ready 时 add_child）</summary>
    public GameEventManager Events => _events;

    public void RegisterEnemy(Node node) => _registry.RegisterEnemy(node);

    /// <summary>统一单位绑定样板（docs/ENTITY_MANAGER.md）：add_to_group("enemy") + 注册 + entity_registered</summary>
    public void BindEnemy(Node node) => _registry.BindEnemy(node);

    /// <summary>统一单位解绑（_exit_tree 调用；注销 + entity_unregistered）</summary>
    public void UnbindEnemy(Node node) => _registry.UnbindEnemy(node);

    /// <summary>批量遍历注册表（失效实例跳过；谓词可选过滤）。清场/索敌/冻结等非热路径统一入口
    /// M2 过渡：Callable 参数化迭代留在 GDScript facade 层（GDScript lambda 跨语言传参不可靠），
    /// 直接迭代 C# 侧注册表集合（_registry.Enemies）；随调用方迁移（M3-M6）后由 C# 实现</summary>
    /// <summary>Callable 空判定（Godot C# Callable 无 IsValid 属性——空 callable 的 Method 为空 StringName，
    /// 替代 GDScript predicate.is_valid()）。</summary>
    private static bool IsEmptyCallable(Variant v) => v.VariantType != Variant.Type.Callable || v.AsCallable().Method == new StringName();

    public void ForEachEnemy(Variant action, Variant predicate = default)
    {
        foreach (var node in _registry.Enemies)
        {
            if (!GodotObject.IsInstanceValid(node))
            {
                continue;
            }

            if (!IsEmptyCallable(predicate) && !predicate.AsCallable().Call(node).AsBool())
            {
                continue;
            }

            action.AsCallable().Call(node);
        }
    }

    /// <summary>批量清除注册表实体（predicate 为保留项过滤，如 Boss）；返回清除数</summary>
    public int ClearEnemies(Variant predicate = default)
    {
        var cleared = 0;
        // 泛型 Duplicate() 返回 Array&lt;Node&gt;（浅拷贝）
        foreach (var node in _registry.Enemies.Duplicate())
        {
            if (node == null || !GodotObject.IsInstanceValid(node))
            {
                continue;
            }

            if (!IsEmptyCallable(predicate) && predicate.AsCallable().Call(node).AsBool())
            {
                continue;
            }

            node.QueueFree();
            cleared += 1;
        }

        return cleared;
    }

    /// <summary>计数（谓词可选过滤）。spread 上限/统计用</summary>
    public int CountEnemies(Variant predicate = default)
    {
        var count = 0;
        foreach (var node in _registry.Enemies)
        {
            if (!GodotObject.IsInstanceValid(node))
            {
                continue;
            }

            if (!IsEmptyCallable(predicate) && !predicate.AsCallable().Call(node).AsBool())
            {
                continue;
            }

            count += 1;
        }

        return count;
    }

    /// <summary>P0-1：敌弹注册/注销转发（bullet.gd 维护；M3a 起 Bullet 为 C# 类）</summary>
    public void RegisterEnemyBullet(GodotObject b) => _registry.RegisterEnemyBullet(b);

    public void UnregisterEnemyBullet(GodotObject b) => _registry.UnregisterEnemyBullet(b);

    /// <summary>G010：注册表存在性判定 O(1)（追踪弹热路径，替代 enemies.has() 线性扫描）</summary>
    public bool EnemiesHas(Node node) => _registry.HasEnemy(node);

    public void UnregisterEnemy(Node node) => _registry.UnregisterEnemy(node);

    private void OnRegistryEntityRegistered(Node node) => EmitSignal(SignalName.EntityRegistered, node);

    private void OnRegistryEntityUnregistered(Node node) => EmitSignal(SignalName.EntityUnregistered, node);

    // 局外成长（第三轮拆域）：MetaService C# 事件 → GameState TechPointsChanged 信号转发
    // （ResearchLab 等仍连同名信号；LoadMeta 仅在登录/登出/游客切换时触发，均晚于本订阅）
    private void OnMetaTechPointsChanged(long v) => EmitSignal(SignalName.TechPointsChanged, v);

    public override void _Ready()
    {
        LoadBalance();
        ApplyBalance();
        // 实体生命周期信号转发（EntityManager 非 Node，无树内信号；GameState 收口转发）
        // M2：C# [Signal] 以 PascalCase 注册，GDScript 侧同名连接
        _registry.EntityRegistered += OnRegistryEntityRegistered;
        _registry.EntityUnregistered += OnRegistryEntityUnregistered;
        // 局外成长（第三轮拆域）：MetaService 入账/消费通知 → 信号转发订阅（LoadMeta 触发点
        // 均为用户操作——登录/登出/游客切换，晚于 _Ready 本订阅；LoadMetaConfig 不发信号）
        _meta.TechPointsChanged += OnMetaTechPointsChanged;
        // 常驻音效播放器池：播放节点被 queue_free 时音效也不会中断（SfxPlayer 子节点挂本节点）
        AddChild(_sfxPlayer);
        _sfxPlayer.BuildPool(SfxPoolSizeValue);
        // 迷雾事件管理器挂载（balance 已在 _apply_balance 就绪；管理器 _ready 读 cfg）
        AddChild(_fogEvents);
        // 统一事件管理器挂载（fog 组经迷雾门面 wire() 接线；encounter 组由 main._ready 注册）
        AddChild(_events);
        _fogEvents.Wire(_events);
        CaptureDefaultBindings();
        InitMissions();
        LoadProfile();
        MaybeMigrateLegacyProfile(); // 账户系统：旧 profile.json 迁移缓存（首个注册用户合并）
        ApplyWindowSize(); // 无 profile 时 load_profile 不会应用窗口尺寸，这里补一次默认档位
        var trZh = GD.Load<Translation>("res://data/translations.zh.translation");
        var trEn = GD.Load<Translation>("res://data/translations.en.translation");
        if (trZh != null)
        {
            TranslationServer.AddTranslation(trZh);
        }

        if (trEn != null)
        {
            TranslationServer.AddTranslation(trEn);
        }

        TranslationServer.SetLocale(Locale);
        ApplyKeyBindings();
        BindJoypadDefaults();
        // PS 布局检测：监听手柄插拔并刷新布局（标签显示用）
        Input.JoyConnectionChanged += OnJoyConnectionChanged;
        DetectJoyLayout();
        _nextMilestone = MilestoneThreshold(0);
        // B 梯队：受击触发 DDA 降档（player_damaged 为减免后信号，Meta HUD 受击层同源）
        PlayerDamaged += OnPlayerDamagedDda;
    }

    // 暂停（Buff/结算 UI）时不计存活时间
    public override void _Process(double delta)
    {
        RunTime += delta;
        // 整秒边界才推进任务（缓存秒值：避免每帧 int(run_time) + missions 字典访问——热路径禁字典约定）
        var surviveSec = (int)RunTime;
        if (surviveSec != _surviveSecCached)
        {
            _surviveSecCached = surviveSec;
            SetKindProgress("survive", surviveSec);
        }

        // 时间轴难度档：跨过量化步进边界时重算难度乘数（去硬顶曲线的时间分量）
        if ((int)Mathf.Floor(RunTime / _progTimeStepSeconds) != _difficultyTimeStep)
        {
            if (RecomputeDifficultyInternal())
            {
                EmitSignal(SignalName.DifficultyChanged, DifficultyMultiplier);
            }
        }

        // DDA 降档计时（受击触发；暂停时 process 冻结，与对局节奏一致）
        if (_ddaTimer > 0.0)
        {
            _ddaTimer -= delta;
        }

        // 连击窗口计时：窗口内无新击杀 → 超时断连（暂停时冻结，与对局节奏一致）
        if (_comboTimer > 0.0)
        {
            _comboTimer -= delta;
            if (_comboTimer <= 0.0)
            {
                ResetCombo();
            }
        }
    }

    public void PlaySfx(AudioStream stream, double volumeDb = 0.0, double pitchScale = 1.0)
    {
        // headless 短路与池化复用逻辑在 SfxPlayer（A2 阶段 3）
        _sfxPlayer.Play(stream, (float)volumeDb, (float)pitchScale);
    }

    /// <summary>退出前停止所有仍在播放的音效：带播未停时 AudioStreamPlayback 会在退出时泄漏</summary>
    public void StopAllSfx()
    {
        _sfxPlayer.StopAll();
        if (PlayerRef != null && GodotObject.IsInstanceValid(PlayerRef))
        {
            var audio = PlayerRef.GetNodeOrNull<AudioStreamPlayer2D>("AudioStreamPlayer2D");
            if (audio != null)
            {
                audio.Stop();
            }
        }
    }

    public void Shake(double strength) => EmitSignal(SignalName.ScreenShake, strength);

    // ---------------- snake → PascalCase 桥属性（M7d 后保留：测试契约与历史调用名） ----------------


}
