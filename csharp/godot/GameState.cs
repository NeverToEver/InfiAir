
using System.Collections.Generic;
using Godot;

namespace InfiAir;

/// <summary>
/// 全局状态与信号总线门面：分数、击杀、生命、难度乘数、天赋缓存。
/// 唯一 autoload（project.godot：*res://csharp/godot/GameState.cs）；组合 8 个基础服务与
/// 8 个域服务，partial 文件负责各域门面转发，业务实现在对应服务类。
/// 公开 API 为 PascalCase typed；[Signal] 以 PascalCase 注册（ScoreChanged 等）。
/// 数值精度：纯标量（health/difficulty_multiplier 等）用 double 逐位等价 GDScript float；
/// 仅与引擎 32 位 API（Vector2/Rect2/音量等）交互处显式 (float) 转换。
/// </summary>
public partial class GameState : Node
{
    // ---------------- 信号（C# [Signal] PascalCase 注册名） ----------------

    /// <summary>得分变化。</summary>
    [Signal]
    public delegate void ScoreChangedEventHandler(int newScore);

    /// <summary>击杀连击变化（combo：当前连击数，0 = 已断连）。</summary>
    [Signal]
    public delegate void ComboChangedEventHandler(int combo);

    [Signal]
    public delegate void HealthChangedEventHandler(float newHealth);

    [Signal]
    public delegate void DifficultyChangedEventHandler(float newMultiplier);

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

    /// <summary>世界层画面增强开关（辉光/分级）变更广播；WorldPostFx 据此显隐全屏增强层</summary>
    [Signal]
    public delegate void WorldPostFxChangedEventHandler(bool enabled);

    /// <summary>性能/显示设置（帧率上限 / 垂直同步）变更广播（设置页即时生效，UI 可据此刷新）</summary>
    [Signal]
    public delegate void DisplaySettingsChangedEventHandler();

    [Signal]
    public delegate void MouseLockChangedEventHandler(bool enabled);

    /// <summary>P0-1 手柄设置：右摇杆瞄准灵敏度 + 摇杆死区（settings.json 持久化；变更时广播供 Player 重读）</summary>
    [Signal]
    public delegate void JoySettingsChangedEventHandler(double aimSpeed, double deadzone);

    /// <summary>buff 层数任何变动（选取/路线合并/重开清空）后发出，驱动外观刷新</summary>
    [Signal]
    public delegate void AugmentsChangedEventHandler();

    /// <summary>实体注册信号转发（新功能订阅口，监听 EntityManager）</summary>
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

    // ---------------- 全局数值配置中心 ----------------
    // A2 阶段 1：balance.json 的加载/查询/纯数值 ramp 已剥离到 BalanceService（组合委托）。
    // 缺失/损坏时全部回退脚本默认值；访问统一走 GameState.cfg("分层.路径", 默认值)；
    // 热路径在各自 _ready 缓存进成员变量。

    private const string BalancePathValue = "res://data/balance.json";
    public string BALANCE_PATH => BalancePathValue;

    /// <summary>组合服务（均非 autoload，保持"唯一 autoload：GameState"约定；GameState 委托，
    /// C# 侧 typed 直调）。</summary>
    private readonly BalanceService _balanceService = new();

    /// <summary>V 系列（U19 注释失实清理）：M7 已 typed 直调（原「GDScript 薄壳」描述删除）。</summary>
    private readonly SaveManager _saveManager = new();

    private readonly SfxPlayer _sfxPlayer = new();

    /// <summary>实体注册信号转发（新功能订阅口，监听 EntityManager）。</summary>
    /// <summary>统一实体管理器（C# typed）。</summary>
    private readonly EntityManager _registry = new();

    /// <summary>迷雾事件管理器（2026-08-05 任务轮换/迷雾事件系统）：全局单例，挂 GameState 下
    /// 维持唯一 autoload 约定；对局中概率触发干扰事件（触发纪律/信号解耦见脚本头注释）</summary>
    private readonly FogEventManager _fogEvents = new();

    /// <summary>统一游戏事件管理器：批量管理全部随机游戏事件（迷雾 +
    /// 遭遇）；fog 组经迷雾门面接线，encounter 组由 Main 注册——实现见 GameEventManager.cs</summary>
    private readonly GameEventManager _events = new();

    /// <summary>第四轮拆域（2026-08-11）：RP 经济/基地任务/天赋路线职责域——原 GameState.Missions.cs
    /// 全部职责迁入 MissionsService，GameState.Missions.cs 为门面转发；与 ScoreService 等组合服务同构。
    /// 无构造依赖（跨域访问统一经 GameState.Instance，运行期单例已就绪），构造器直接实例化。</summary>
    private readonly MissionsService _missions;

    /// <summary>第五轮拆域（2026-08-11）：计分域服务——Score/Kills/BossKills/Combo 状态、连击系统与
    /// 里程碑推进迁入 ScoreService（GameState.State.cs 为门面转发；跨域经 Instance）。无构造依赖。</summary>
    private readonly ScoreService _score = new();

    /// <summary>第五轮拆域（2026-08-11）：健康/Buff 战斗状态域服务——Health/Augments 状态与生命上限/
    /// 受击/治疗/吸血/选 buff 逻辑迁入 CombatStateService（GameState.Settings.cs C 簇为门面转发；
    /// 跨域经 Instance；PlayerDied 由 Player.DieInternal 在死亡结算后发射）。无构造依赖。</summary>
    private readonly CombatStateService _combat = new();

    /// <summary>第五轮拆域（2026-08-11）：对局进程域服务——难度档位/倍率缓存/DDA 降档/进程 ramp/
    /// 里程碑曲线求值迁入 RunProgressionService（GameState.Difficulty.cs 为门面转发；
    /// _balanceService 经构造注入，与 SettingsService 构造注入 EntityManager 同构）。</summary>
    private readonly RunProgressionService _runProg;

    /// <summary>第六轮拆域收官（2026-08-12）：设置+视图域服务——设置 setter 簇/视图簇/状态字段/
    /// 设置域持久化桥迁入 SettingsService（GameState.Settings.cs/State.cs 为门面转发；
    /// 跨域经 Instance；_registry 经构造注入）。</summary>
    private readonly SettingsService _settings;

    /// <summary>第七轮拆域收官（2026-08-12）：键位+手柄域服务——可改键系统/手柄装配与
    /// JOYPAD_ACTIONS/PS/XBOX_BUTTON_LABELS/JoyLayout 迁入 InputBindingsService
    /// （GameState.Input.cs/State.cs 为门面转发；跨域经 Instance；SaveSettings/JoyDeadzone
    /// 经门面，无构造依赖）。</summary>
    private readonly InputBindingsService _input;

    /// <summary>天赋缓存域（2026-09-07 重构）：里程碑/Boss 点数入缓存池 + 树状加点/路线契约/
    /// 风险加点（TalentService；GameState.Talent.cs 为门面）。无构造依赖（跨域经 Instance）。</summary>
    private readonly TalentService _talent = new();

    public GameState()
    {
        _missions = new MissionsService();
        _runProg = new RunProgressionService(_balanceService);
        _settings = new SettingsService(_registry);
        _input = new InputBindingsService();
    }

    /// <summary>启动计时基准（autoload 最早生命周期点；--startup-time 时由 main 打印分段耗时）</summary>
    public int BootTicksMsec { get; set; } = 0;

    /// <summary>母舰召唤窗口（H 蓄力中或机库小窗演出中）。遭遇事件触发门控读取——窗口期玩家
    /// 锁输入 + 999s 无敌，事件掷签命中会被母舰自动火力白拿奖励（L13 反向漏出）。
    /// Main._Process 逐帧维护，Main._Ready/_ExitTree 复位（场景切换不残留）。</summary>
    public bool SummonInProgress { get; set; }

    /// <summary>树暂停单口：暂停/恢复统一经此（原六类节点各自直写 GetTree().Paused 十六处，
    /// 新增退出路径漏写复位即全局卡死；autoload 场景无关，拆树期 GetTree() 判空防线集中一处）。</summary>
    public void SetTreePaused(bool paused)
    {
        var tree = (SceneTree?)Engine.GetMainLoop();
        if (tree != null)
        {
            tree.Paused = paused;
        }
    }

    /// <summary>终止本局回标题屏单口（结算页「返回标题」/暂停/教程 Esc/BackNavigator 同路由）：
    /// 子弹时间与暂停复位 + 全新一局 + 切场景。新增退出入口一律复用本出口，防漏复位软锁。</summary>
    public void ExitToTitle()
    {
        Engine.TimeScale = 1.0f; // 狂暴子弹时间残留复位（无残留时无副作用）
        SetTreePaused(false);
        ResetRun(); // 保证下次开局为全新一局，不污染正常对局
        ((SceneTree?)Engine.GetMainLoop())?.ChangeSceneToFile("res://scenes/title.tscn");
    }

    /// <summary>弃局静默重开单口（结算页「重新出击」/暂停页 R 重开共用）：解除暂停 + 本局终结清档
    /// + 全新一局 + 重载当前场景（main 重建）。不含 AB13 退出确认守卫——暂停页 R 重开由
    /// PauseUi.RestartRun 本地守卫后转调本单口，其他入口直接调用即可。
    /// 清档理由：重开 = 主动放弃本局（结算页本就是死亡后入口），检查点随之作废——
    /// 否则「读档 → 暂停重开 → 再退出重进」可反复读回同一份进度（无限回滚）。</summary>
    public void RestartRun()
    {
        SetTreePaused(false);
        DeleteRunSave(); // 本局终结：检查点作废（与死亡删档同口径）
        ResetRun();
        ((SceneTree?)Engine.GetMainLoop())?.ReloadCurrentScene();
    }

    /// <summary>弹体共享纹理缓存（Bullet 首次应用外观时惰性生成，全实例共用）。
    /// 实例字段而非静态——静态字段持 Godot 对象为退出 segfault 实测根因（Main/Spawner 同规），
    /// 本 autoload 与引擎同生命周期，承担跨对局缓存职责。</summary>
    public Texture2D? BulletPlayerTex { get; set; }
    public Texture2D? BulletEnemyTex { get; set; }

    /// <summary>爆炸粒子池可用队列（Explosion.OnFinished 回池 / SpawnAt 取用；元素失效由
    /// 取用方 IsInstanceValid 过滤）。实例字段而非静态——同上铁律。</summary>
    public Godot.Collections.Array<Explosion> ExplosionStock { get; } = new();

    private Node? _explosionPoolHost;

    /// <summary>爆炸回池统一宿主（挂 current_scene 下维持绘制序在场景内容之上；跨场景
    /// 重载随旧场景销毁，此处检测失效并重建）。</summary>
    public Node? ExplosionPoolHost()
    {
        if (_explosionPoolHost != null && GodotObject.IsInstanceValid(_explosionPoolHost))
        {
            return _explosionPoolHost;
        }

        var tree = (SceneTree?)Engine.GetMainLoop();
        if (tree == null || tree.CurrentScene == null)
        {
            return null;
        }

        _explosionPoolHost = new Node { Name = "ExplosionPool" };
        tree.CurrentScene.AddChild(_explosionPoolHost);
        return _explosionPoolHost;
    }

    /// <summary>静态缓存——autoload 在 root 下恒存在，缓存失效（场景树已拆）时重查，取不到抛异常。</summary>
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
    /// 批量操作 API。属性转发保持外部语法不变；M2 起内部改 C# PascalCase）。
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

    /// <summary>子弹对象池实例（由 BulletPool 在 _Ready 时登记）</summary>
    public BulletPool? BulletPool
    {
        get => _registry.BulletPool;
        set => _registry.BulletPool = value;
    }

    /// <summary>敌机对象池实例（由 EnemyPool 在 _Ready 时登记）</summary>
    public GodotObject? EnemyPool
    {
        get => _registry.EnemyPool;
        set => _registry.EnemyPool = value;
    }

    /// <summary>辅助瞄准框覆盖层实例（Main/Tutorial 运行时创建，自身 _Ready 登记；Player 开火时查询框内标记敌）</summary>
    public GodotObject? AimFrameLayer
    {
        get => _registry.AimFrameLayer;
        set => _registry.AimFrameLayer = value;
    }

    /// <summary>触屏虚拟输入层实例（mobile touch，由 Main 在 _Ready 时创建并登记；
    /// Player 瞄准点查询触屏瞄准基准）</summary>
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

    /// <summary>统一单位绑定样板：add_to_group("enemy") + 注册 + entity_registered</summary>
    public void BindEnemy(Node node) => _registry.BindEnemy(node);

    /// <summary>统一单位解绑（_exit_tree 调用；注销 + entity_unregistered）</summary>
    public void UnbindEnemy(Node node) => _registry.UnbindEnemy(node);

    /// <summary>计数（谓词可选过滤）。spread 上限/统计用。
    /// Callable 空判定（Godot C# Callable 无 IsValid 属性——空 callable 的 Method 为空 StringName，
    /// 替代 GDScript predicate.is_valid()）。</summary>
    public int CountEnemies(Variant predicate = default)
    {
        var count = 0;
        var hasPredicate = predicate.VariantType == Variant.Type.Callable
            && predicate.AsCallable().Method != new StringName();
        foreach (var node in _registry.Enemies)
        {
            if (!GodotObject.IsInstanceValid(node))
            {
                continue;
            }

            if (hasPredicate && !predicate.AsCallable().Call(node).AsBool())
            {
                continue;
            }

            count += 1;
        }

        return count;
    }

    /// <summary>P0-1：敌弹注册/注销转发（Bullet 激活/回收时维护）</summary>
    public void RegisterEnemyBullet(GodotObject b) => _registry.RegisterEnemyBullet(b);

    public void UnregisterEnemyBullet(GodotObject b) => _registry.UnregisterEnemyBullet(b);

    /// <summary>G010：注册表存在性判定 O(1)（追踪弹热路径，替代 enemies.has() 线性扫描）</summary>
    public bool EnemiesHas(Node node) => _registry.HasEnemy(node);

    public void UnregisterEnemy(Node node) => _registry.UnregisterEnemy(node);

    private void OnRegistryEntityRegistered(Node node) => EmitSignal(SignalName.EntityRegistered, node);

    private void OnRegistryEntityUnregistered(Node node) => EmitSignal(SignalName.EntityUnregistered, node);

    // Missions 域（第四轮拆域）：MissionsService C# 事件 → GameState 同名信号转发
    // （RpChanged/MissionCompleted/RefreshPointsChanged/RouteChosen——
    // 触发点均为运行期玩家操作/对局事件，晚于 _Ready 本订阅；ResetRun 直接赋值
    // 路径由 State.cs 直发同名信号，经此订阅的重发不与之重复）
    private void OnMissionsRpChanged(int v) => EmitSignal(SignalName.RpChanged, v);

    private void OnMissionsMissionCompleted(StringName id) => EmitSignal(SignalName.MissionCompleted, id);

    private void OnMissionsRefreshPointsChanged(int v) => EmitSignal(SignalName.RefreshPointsChanged, v);

    // 计分域（第五轮拆域）：ScoreService C# 事件 → GameState 同名信号转发
    // （ScoreChanged/MilestoneReached/ComboChanged；触发点均为运行期对局事件——AddScore/
    // AddKillScore/ResetCombo，晚于 _Ready 本订阅）
    /// <summary>计分域（第五轮拆域）：ScoreService C# 事件 → GameState 同名信号转发
    /// （ScoreChanged/MilestoneReached/ComboChanged；触发点均为运行期对局事件——AddScore/
    /// AddKillScore/ResetCombo，晚于 _Ready 本订阅）
    /// 里程碑同时是天赋点来源（天赋缓存系统重构）：入账在信号转发前，保证订阅方读到的
    /// 缓存余额已含本档点数。</summary>
    private void OnScoreScoreChanged(int v) => EmitSignal(SignalName.ScoreChanged, v);

    private void OnScoreMilestoneReached(int v)
    {
        _talent.GrantForMilestone();
        EmitSignal(SignalName.MilestoneReached, v);
    }

    private void OnScoreComboChanged(int v) => EmitSignal(SignalName.ComboChanged, v);

    // 战斗状态域（第五轮拆域）：CombatStateService C# 事件 → GameState 同名信号转发
    // （HealthChanged/AugmentsChanged；触发点均为运行期对局事件/玩家操作——LoseHealth/Heal/AddBuff/
    // ConsumeAugment，晚于 _Ready 本订阅；ResetRun/天赋路线（TalentService 层级写入）直发路径
    // 不经本事件，订阅重发不与之重复）
    private void OnCombatHealthChanged(double v) => EmitSignal(SignalName.HealthChanged, (float)v);

    private void OnCombatAugmentsChanged() => EmitSignal(SignalName.AugmentsChanged);

    // 设置/视图域（第六轮拆域收官）：SettingsService C# 事件 → GameState 同名信号转发
    // （TouchControlsChanged/ViewZoomChanged/WindowSizeChanged/AimAssistChanged/ReduceFlashChanged/
    // MouseLockChanged/JoySettingsChanged/LocaleChanged；触发点均为运行期玩家操作——设置页/手柄
    // 设置，晚于 _Ready 本订阅；LoadSettings 直写字段路径不发服务事件，重发不与之重复）
    private void OnSettingsTouchControlsChanged(bool v) => EmitSignal(SignalName.TouchControlsChanged, v);

    private void OnSettingsViewZoomChanged(double v) => EmitSignal(SignalName.ViewZoomChanged, v);

    private void OnSettingsWindowSizeChanged(StringName v) => EmitSignal(SignalName.WindowSizeChanged, v);

    private void OnSettingsAimAssistChanged(StringName v) => EmitSignal(SignalName.AimAssistChanged, v);

    private void OnSettingsReduceFlashChanged(bool v) => EmitSignal(SignalName.ReduceFlashChanged, v);

    private void OnSettingsWorldPostFxChanged(bool v) => EmitSignal(SignalName.WorldPostFxChanged, v);

    private void OnSettingsDisplaySettingsChanged() => EmitSignal(SignalName.DisplaySettingsChanged);

    private void OnSettingsMouseLockChanged(bool v) => EmitSignal(SignalName.MouseLockChanged, v);

    private void OnSettingsJoySettingsChanged(double aimSpeed, double deadzone) => EmitSignal(SignalName.JoySettingsChanged, aimSpeed, deadzone);

    private void OnSettingsLocaleChanged() => EmitSignal(SignalName.LocaleChanged);

    // 键位域（第七轮拆域收官）：InputBindingsService C# 事件 → GameState 同名信号转发
    // （KeyBindingsChanged/JoyLayoutChanged；触发点均为运行期玩家操作/手柄插拔——RebindAction/
    // ResetKeyBindings/DetectJoyLayout，晚于 _Ready 本订阅；下方启动装配 CaptureDefaultBindings/
    // BindJoypadDefaults 不发事件，DetectJoyLayout 无手柄不发射，有 PS 手柄时首帧发射一次经
    // 本订阅转发——订阅在启动装配之前，转发不丢）
    private void OnInputKeyBindingsChanged() => EmitSignal(SignalName.KeyBindingsChanged);

    private void OnInputJoyLayoutChanged(StringName v) => EmitSignal(SignalName.JoyLayoutChanged, v);

    // 对局进程域（第五轮拆域）：RunProgressionService C# 事件 → GameState 同名信号转发
    // （DifficultyChanged/DifficultySelected；触发点均为运行期对局事件/玩家操作——_Process
    // 时间档重算/SetDifficulty，晚于 _Ready 本订阅；AddBossKill 直发路径不重复）
    private void OnRunProgDifficultyChanged(double v) => EmitSignal(SignalName.DifficultyChanged, (float)v);

    private void OnRunProgDifficultySelected(StringName v) => EmitSignal(SignalName.DifficultySelected, v);

    public override void _Ready()
    {
        LoadBalance();
        ApplyBalance();
        // 实体生命周期信号转发（EntityManager 非 Node，无树内信号；GameState 收口转发）
        // C# [Signal] 以 PascalCase 注册
        _registry.EntityRegistered += OnRegistryEntityRegistered;
        _registry.EntityUnregistered += OnRegistryEntityUnregistered;
        // Missions 域（第四轮拆域）：MissionsService 事件 → 信号转发订阅（触发点均为运行期
        // 玩家操作/对局事件，晚于 _Ready 本订阅；下方 InitMissions 不发信号）
        _missions.RpChanged += OnMissionsRpChanged;
        _missions.MissionCompleted += OnMissionsMissionCompleted;
        _missions.RefreshPointsChanged += OnMissionsRefreshPointsChanged;
        // 天赋缓存域：TalentService 事件 → 信号转发订阅（触发点为运行期对局事件/玩家操作，
        // 晚于本订阅；ApplyBalance 的 LoadTalentConfig 只写配置缓存不发事件）
        _talent.CacheChanged += OnTalentCacheChanged;
        _talent.TalentsChanged += OnTalentsChanged;
        // 计分域（第五轮拆域）：ScoreService 事件 → 信号转发订阅（触发点均为运行期对局事件，
        // 晚于 _Ready 本订阅；下方 InitMilestones 不发信号）
        _score.ScoreChanged += OnScoreScoreChanged;
        _score.MilestoneReached += OnScoreMilestoneReached;
        _score.ComboChanged += OnScoreComboChanged;
        // 对局进程域（第五轮拆域）：RunProgressionService 事件 → 信号转发订阅（触发点均为运行期
        // 对局事件/玩家操作，晚于 _Ready 本订阅）
        _runProg.DifficultyChanged += OnRunProgDifficultyChanged;
        _runProg.DifficultySelected += OnRunProgDifficultySelected;
        // 战斗状态域（第五轮拆域）：CombatStateService 事件 → 信号转发订阅（触发点均为运行期
        // 对局事件/玩家操作，晚于 _Ready 本订阅；ResetRun/天赋路线（TalentService 层级写入）直发
        // 路径不经本事件，重发不与之重复）
        _combat.HealthChanged += OnCombatHealthChanged;
        _combat.AugmentsChanged += OnCombatAugmentsChanged;
        // 设置/视图域（第六轮拆域收官）：SettingsService 事件 → 信号转发订阅（触发点均为运行期
        // 玩家操作——设置页/手柄设置，晚于 _Ready 本订阅；LoadSettings
        // 直写字段路径不发服务事件，重发不与之重复）
        _settings.TouchControlsChanged += OnSettingsTouchControlsChanged;
        _settings.ViewZoomChanged += OnSettingsViewZoomChanged;
        _settings.WindowSizeChanged += OnSettingsWindowSizeChanged;
        _settings.AimAssistChanged += OnSettingsAimAssistChanged;
        _settings.ReduceFlashChanged += OnSettingsReduceFlashChanged;
        _settings.WorldPostFxChanged += OnSettingsWorldPostFxChanged;
        _settings.DisplaySettingsChanged += OnSettingsDisplaySettingsChanged;
        _settings.MouseLockChanged += OnSettingsMouseLockChanged;
        _settings.JoySettingsChanged += OnSettingsJoySettingsChanged;
        _settings.LocaleChanged += OnSettingsLocaleChanged;
        // 键位域（第七轮拆域收官）：InputBindingsService 事件 → 信号转发订阅（触发点均为运行期
        // 玩家操作/手柄插拔——RebindAction/ResetKeyBindings/DetectJoyLayout，晚于 _Ready 本订阅；
        // 下方启动装配 DetectJoyLayout 按需发射一次，订阅在前保证转发）
        _input.KeyBindingsChanged += OnInputKeyBindingsChanged;
        _input.JoyLayoutChanged += OnInputJoyLayoutChanged;
        // 常驻音效播放器池：播放节点被 queue_free 时音效也不会中断（SfxPlayer 子节点挂本节点）
        AddChild(_sfxPlayer);
        _sfxPlayer.BuildPool();
        // 迷雾事件管理器挂载（balance 已在 _apply_balance 就绪；管理器 _ready 读 cfg）
        AddChild(_fogEvents);
        // 统一事件管理器挂载（fog 组经迷雾门面 wire() 接线；encounter 组由 main._ready 注册）
        AddChild(_events);
        _fogEvents.Wire(_events);
        _input.CaptureDefaultBindings(); // 第七轮拆域：键位域启动快照（InputBindingsService）
        InitMissions();
        LoadSettings();
        _settings.ApplyDisplay(); // 帧率上限/垂直同步：无设置文件时 load 不应用，这里补一次默认档
        ApplyWindowSize(); // 无设置文件时 load 不会应用窗口尺寸，这里补一次默认档位
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
        _input.ApplyKeyBindings(); // 第七轮拆域：键位域 InputMap 应用（InputBindingsService）
        _input.BindJoypadDefaults(); // 第七轮拆域：键位域手柄装配（InputBindingsService）
        // PS 布局检测：监听手柄插拔并刷新布局（标签显示用）——第七轮拆域：服务公开方法接线。
        // 引擎静态事件 + 本 autoload 进程级恒存，无需退订（C22 仅针对 Connect）
        Input.JoyConnectionChanged += _input.OnJoyConnectionChanged;
        _input.DetectJoyLayout();
        _score.InitMilestones(); // 里程碑首档初始化（ScoreService；默认 3000 = MilestoneBase[0]）
        // B 梯队：受击触发 DDA 降档（player_damaged 为减免后信号，Meta HUD 受击层同源）
        PlayerDamaged += OnPlayerDamagedDda;
        // 本局存档：死亡即删档（不可读档回滚，保住必死曲线紧张感）。PlayerDied 由
        // Player.DieInternal 在死亡结算后发射；本 autoload 与引擎同生命周期，无需退订。
        PlayerDied += OnPlayerDiedDeleteRunSave;
    }

    /// <summary>死亡即删档（本局存档单一钩子）。</summary>
    private void OnPlayerDiedDeleteRunSave() => DeleteRunSave();

    // 运行期时钟门控：仅真实对局（main 为 current_scene）累积 RunTime/推进 survive 任务/
    // 难度时间档/连击窗口——welcome 等非对局场景的停留时间不得污染下一局难度曲线。
    // 暂停不计（本节点 Pausable）。
    private bool _runActive;

    /// <summary>由 Main 依 current_scene 置位/复位（同事件管理器惯例）。</summary>
    public void SetRunActive(bool active) => _runActive = active;

    // 暂停（Buff/结算 UI）时不计存活时间
    public override void _Process(double delta)
    {
        if (!_runActive)
        {
            return;
        }

        RunTime += delta;
        // 整秒边界才推进任务（缓存秒值：避免每帧 int(run_time) + missions 字典访问——热路径禁字典约定）
        var surviveSec = (int)RunTime;
        if (surviveSec != _surviveSecCached)
        {
            _surviveSecCached = surviveSec;
            SetKindProgress("survive", surviveSec);
        }

        // 第五轮拆域：难度时间档重算 + DDA 计时（RunProgressionService.Tick；DifficultyChanged
        // 经订阅重发）→ 连击断连计时（ScoreService.Tick；ComboChanged 经订阅重发）——发射顺序与
        // 拆域前逐位一致（难度档信号先于连击信号）
        _runProg.Tick(delta);
        _score.Tick(delta);
    }

    /// <summary>volumeDb/pitchScale 缺省 = SfxPlayer 目录基准（音量基准/抖动/冷却/复音都在目录表）；
    /// 显式传入 = 精确覆盖（过场/tell 调音），显式音高不参与抖动。</summary>
    public void PlaySfx(SfxId id, double? volumeDb = null, double? pitchScale = null)
    {
        _sfxPlayer.Play(id, volumeDb, pitchScale);
    }

    /// <summary>退出前停止所有仍在播放的音效：带播未停时 AudioStreamPlayback 会在退出时泄漏</summary>
    public void StopAllSfx()
    {
        _sfxPlayer.StopAll();
    }

    public void Shake(double strength) => EmitSignal(SignalName.ScreenShake, strength);
}
