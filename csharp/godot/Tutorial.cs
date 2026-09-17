using Godot;
using InfiAir.Core.Input;
using InfiAir.Core.Text;
using InfiAir.Core.Tutorial;

namespace InfiAir;

/// <summary>
/// 新手教程（对齐原作 6 阶段）：独立场景，脚本驱动检查点，复用现有实体。
/// 阶段顺序、目标计数、目标行补参与达成判据全部来自 core 阶段表（<see cref="TutorialCurriculum"/>
/// 与 <see cref="TutorialProgress"/>）——本节点只做适配：刷怪布局、信号接线、键位与平衡值取值。
/// 不启动正常 Spawner 波次；进场/出场各 ResetRun 隔离本局状态，出场保证 TimeScale=1。
/// 实体判定（Enemy/Boss/Mothership/Bullet）均为 C# 类，typed `is` 判型。
/// </summary>
public partial class Tutorial : Node2D
{
    // 静态 Godot 资源必须改实例字段——静态持 RefCounted/资源
    // 在引擎退出后被 .NET finalize 触碰 native 可致 segfault
    private readonly PackedScene _enemyScene = GD.Load<PackedScene>("res://scenes/enemy.tscn");
    private readonly PackedScene _bossScene = GD.Load<PackedScene>("res://scenes/boss.tscn");
    private readonly PackedScene _mothershipScene = GD.Load<PackedScene>("res://scenes/mothership.tscn");

    public float HomeChargeTime = 1.5f;
    public float DockChargeTime = 3.0f; // 母舰召唤蓄力（mothership.dock_charge_time，对齐正局）

    // 逐帧输入查询的动作名静态持有：免每帧把 C# 字符串转成 StringName 的原生 intern 开销
    private static readonly StringName ActDock = new("dock");
    private static readonly StringName ActHomecoming = new("homecoming");
    private static readonly StringName ActBoost = new("boost");
    private static readonly StringName ActDash = new("dash");
    private static readonly StringName ActGiveUp = new("give_up");
    private static readonly StringName ActParry = new("parry");
    private static readonly StringName ActAugmentPanel = new("augment_panel");
    private static readonly StringName ActTalentPanel = new("talent_panel");

    /// <summary>阶段进度与达成判据（core）。节点每帧只把事件喂进来，不在这里判达标。</summary>
    private readonly TutorialProgress _progress = new();

    /// <summary>当前阶段在阶段表里的索引（观测面与推进用；定义取自 <see cref="_progress"/>）。</summary>
    private int _stage;
    private bool _advancing;
    /// <summary>蓄力百分比文本刷新节流（对齐 HUD 仪表约定）。</summary>
    private const float ObjectivePollInterval = 0.1f;
    private bool _prevDashing;
    // 两段蓄力（阶段 4 召唤母舰 / 阶段 5 返航）用 core HoldCharge（按住累加 → 达阈值触发一次 → 松手复位）；
    // 阈值在 _Ready 按配置覆写。阶段切换与门控失效都靠 Reset 归零，不再各自维护累加字段。
    private readonly HoldCharge _homeCharge = new(1.5f);
    private readonly HoldCharge _dockCharge = new(3.0f);
    /// <summary>跳过本阶段的长按通道（教程是可选内容，卡住的玩家不该被某一步锁住）。</summary>
    private readonly HoldCharge _skipCharge = new(TutorialCurriculum.SkipHoldSeconds);
    private float _maxHp = 100.0f; // 阶段 3 锁血每物理帧用，_ready 缓存一次（教程内 buffs 不变）
    private float _objectivePoll; // 蓄力百分比文本 0.1s 节流计时（对齐 HUD 仪表约定）
    private BaseConsole? _baseUi; // typed 字段
    private TutorialEscRouter? _escRouter; // 基地开启窗口期（树暂停）的 Always 态 Esc 返回路由
    /// <summary>基地段的增幅触点面板（阶段 6）：读生产增幅数据，开合节奏与基地同款；
    /// 阶段推进时自动收起。构建见 <see cref="OpenAugmentPanel"/>。</summary>
    private ChamferedPanel? _augmentPanel;
    private Boss _boss = null!; // typed 字段
    private Mothership? _mothership;
    private bool _finished;
    private bool _failed;

    private Label _titleLabel = null!;
    private Label _objectiveLabel = null!;
    private Label _skipLabel = null!;
    private string _objectiveKey = "";
    private Godot.Collections.Array _objectiveArgs = new();
    private Control _completePanel = null!;
    private CanvasLayer _hudLayer = null!;
    private Player _player = null!;
    private Tween? _stageTween; // 阶段横幅编排（连过阶段时先杀旧）

    private readonly Callable _onLocaleChanged;
    private readonly Callable _onPlayerDied;
    private readonly Callable _onHintDeviceChanged;

    public Tutorial()
    {
        _onLocaleChanged = Callable.From(OnLocaleChanged);
        _onPlayerDied = Callable.From(OnPlayerDied);
        _onHintDeviceChanged = Callable.From(OnHintDeviceChanged);
    }

    // ---------------- 观测面（探针读取的玩家可见读数） ----------------

    /// <summary>当前阶段索引（0 起）——冒烟探针据此判阶段推进。</summary>
    public int StageIndex() => _stage;

    /// <summary>教程是否已进入完成态（完成结算面板已出）。</summary>
    public bool IsFinished() => _finished;

    /// <summary>阶段标题当前显示给玩家的文本（探针判文案键是否成形）。</summary>
    public string TitleText() => _titleLabel.Text;

    /// <summary>目标行当前显示给玩家的文本（探针判补参是否成形、键位是否跟随改键）。</summary>
    public string ObjectiveText() => _objectiveLabel.Text;

    /// <summary>跳过提示当前显示给玩家的文本（隐藏时为空串）：跳过是**没有其它入口**的能力，
    /// 提示缺失＝玩家永远发现不了它，而引擎侧零报错。</summary>
    public string SkipHintText() => _skipLabel.Visible ? _skipLabel.Text : "";

    /// <summary>基地段增幅触点面板当前是否展开（探针判「面板打开即推进」）。</summary>
    public bool AugmentPanelOpen() => _augmentPanel != null && GodotObject.IsInstanceValid(_augmentPanel) && _augmentPanel.Visible;

    public override void _Ready()
    {
        GameState.Instance.ResetRun();
        _maxHp = (float)GameState.Instance.MaxHealth(); // 热路径缓存（阶段 3 锁血每物理帧读）
        RenderingServer.SetDefaultClearColor(new Color(0.025f, 0.022f, 0.018f));
        var gs = GameState.Instance;
        if (!gs.IsConnected(GameState.SignalName.LocaleChanged, _onLocaleChanged))
        {
            gs.Connect(GameState.SignalName.LocaleChanged, _onLocaleChanged);
        }

        if (!gs.IsConnected(GameState.SignalName.PlayerDied, _onPlayerDied))
        {
            gs.Connect(GameState.SignalName.PlayerDied, _onPlayerDied);
        }

        if (!gs.IsConnected(GameState.SignalName.InputDeviceChanged, _onHintDeviceChanged))
        {
            gs.Connect(GameState.SignalName.InputDeviceChanged, _onHintDeviceChanged);
        }

        // 辅助瞄准框覆盖层：与 Main 同款运行时创建（登记 GameState.AimFrameLayer），
        // 教程内标记框与追踪弹行为与正局一致；随场景切换自动注销
        AddChild(new AimFrameLayer());
        _player = GetNode<Player>("Player");
        _player.ParryLanded += OnParryLanded; // 弹反段计数：成功反射一发算一次（其余阶段不消费）
        // 世界层画面增强（layer=1，世界之上、HUD 之下）：BuildHud 之前入树，
        // 与 HUD（layer=2）分层——教程画面与正局同款辉光/分级
        AddChild(new WorldPostFx());
        BuildHud();
        // 蓄力时长是百分比文本与「蓄满即过关」判定的除数（core HoldCharge 的 Progress / Threshold）：
        // 0/负值让蓄力段开按即过（非有限读数还会写进提示文案），钳制口径与主路径 Main 同源。
        HomeChargeTime = CfgFx.Float("effects.home_charge_time", HomeChargeTime, CfgFx.IntervalFloor);
        DockChargeTime = CfgFx.Float("mothership.dock_charge_time", DockChargeTime, CfgFx.IntervalFloor);
        _homeCharge.Threshold = HomeChargeTime;
        _dockCharge.Threshold = DockChargeTime;
        // 落点取续接检查点（越界由 core 归一）：中途退出的玩家从上次的阶段继续
        EnterStage(GameState.Instance.TutorialStage);
        // 固定标记：教程场景就绪观测点（冒烟门禁据此断言教程入场链路跑通）；
        // 场景加载/切场景失败时本行不执行，无头也能判出
        GD.Print("[tutorial] 场景就绪");
    }

    public override void _ExitTree()
    {
        // 配对的信号断开——教程 Esc/完成退出后残留连接
        // 在正局死亡（PlayerDied 高频）或切语言时回调已释放实例
        // autoload 可能先于本节点释放（非常规拆树序），Instance getter 会抛异常，故安全取值
        var gs = GameState.TryGetInstance();
        if (gs == null)
        {
            return;
        }

        if (gs.IsConnected(GameState.SignalName.LocaleChanged, _onLocaleChanged))
        {
            gs.Disconnect(GameState.SignalName.LocaleChanged, _onLocaleChanged);
        }

        if (gs.IsConnected(GameState.SignalName.PlayerDied, _onPlayerDied))
        {
            gs.Disconnect(GameState.SignalName.PlayerDied, _onPlayerDied);
        }

        if (gs.IsConnected(GameState.SignalName.InputDeviceChanged, _onHintDeviceChanged))
        {
            gs.Disconnect(GameState.SignalName.InputDeviceChanged, _onHintDeviceChanged);
        }
    }

    /// <summary>阶段标题基位（横幅滑入/滑出的归属位）。</summary>
    private static readonly Vector2 TitleBasePos = new(-400.0f, 24.0f);

    private void BuildHud()
    {
        _hudLayer = new CanvasLayer { Layer = 2 };
        AddChild(_hudLayer);
        // 字号走 UITheme 阶梯（原裸 34/22 不在梯度上）
        _titleLabel = UITheme.MakeLabel("", UITheme.FontScore, UITheme.AccentGold, HorizontalAlignment.Center);
        _titleLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _titleLabel.Position = TitleBasePos;
        _titleLabel.CustomMinimumSize = new Vector2(800.0f, 0.0f);
        _hudLayer.AddChild(_titleLabel);
        _objectiveLabel = UITheme.MakeLabel("", UITheme.FontHudL, UITheme.TextDim, HorizontalAlignment.Center);
        _objectiveLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _objectiveLabel.Position = new Vector2(-500.0f, 74.0f);
        _objectiveLabel.CustomMinimumSize = new Vector2(1000.0f, 0.0f);
        _objectiveLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hudLayer.AddChild(_objectiveLabel);
        // 跳过提示贴在屏底：与目标行分层，不挤占阶段说明的多行文本；完成/失败态下隐藏
        _skipLabel = UITheme.MakeLabel("", UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Center);
        _skipLabel.Modulate = new Color(_skipLabel.Modulate, 0.7f); // 比目标行更弱的次级提示
        _skipLabel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _skipLabel.Position = new Vector2(-300.0f, -64.0f);
        _skipLabel.CustomMinimumSize = new Vector2(600.0f, 0.0f);
        _hudLayer.AddChild(_skipLabel);
    }

    /// <summary>渲染跳过提示（键位取实际绑定；完成/失败态下不显示——那时已无可跳过的阶段）。</summary>
    private void RefreshSkipHint()
    {
        _skipLabel.Visible = !_finished && !_failed;
        if (!_skipLabel.Visible)
        {
            _skipLabel.Text = "";
            return;
        }

        // 补参经与目标行同一条解析口（SkipKey → 放弃出击的实际绑定键），不在这里另拼一份
        var args = Args(TutorialCurriculum.SkipHintArgs);
        _skipLabel.Text = GdFormat.Format((string)Tr(TutorialCurriculum.SkipHintKey), ToObjects(args));
    }

    private void SetObjectiveTr(string key, Godot.Collections.Array args)
    {
        // 换行（新目标/新阶段）才做入场动效；同键只换数字的高频刷新（击杀计数、100ms 蓄力轮询）
        // 直接改文本——否则每 0.1s 起一条 tween，既抖又白分配
        var keyChanged = _objectiveKey != key;
        _objectiveKey = key;
        _objectiveArgs = args;
        // tr(key) % args if not args.is_empty() else tr(key)
        var text = args.Count > 0 ? GdFormat.Format((string)Tr(key), ToObjects(args)) : (string)Tr(key);
        if (_objectiveLabel.Text == text)
        {
            return;
        }

        _objectiveLabel.Text = text;
        if (!keyChanged)
        {
            return;
        }

        UITheme.FadeIn(_objectiveLabel, 0.18f);
        UITheme.PunchScale(_objectiveLabel, 1.02f, 0.14f);
    }

    /// <summary>按阶段表声明的补参顺序取值（顺序错位＝玩家看到错位的数字或键名，不会报错；
    /// 顺序由 core 单测与文案占位符对账钉住）。</summary>
    private Godot.Collections.Array Args(TutorialArg[] plan, float chargeProgress = 0.0f)
    {
        var args = new Godot.Collections.Array();
        foreach (var kind in plan)
        {
            args.Add(ResolveArg(kind, chargeProgress));
        }

        return args;
    }

    private Variant ResolveArg(TutorialArg kind, float chargeProgress) => kind switch
    {
        // 键位补参一律走设备感知取值口（ActionHintText/MoveHintText）：键鼠档＝键名（改键后跟变，
        // 既有判据保持），手柄档＝按钮/扳机/摇杆标签——同一处提示在两档下都不留占位符
        TutorialArg.MoveKeys => GameState.Instance.MoveHintText(),
        TutorialArg.AimHint => GameState.Instance.AimHintText(),
        TutorialArg.FireHint => GameState.Instance.ActionHintText(new StringName("fire")),
        TutorialArg.BoostKey => GameState.Instance.ActionHintText(ActBoost),
        TutorialArg.DashKey => GameState.Instance.ActionHintText(ActDash),
        TutorialArg.DockKey => GameState.Instance.ActionHintText(ActDock),
        TutorialArg.HomecomingKey => GameState.Instance.ActionHintText(ActHomecoming),
        TutorialArg.SkipKey => GameState.Instance.ActionHintText(ActGiveUp),
        TutorialArg.ParryKey => GameState.Instance.ActionHintText(ActParry),
        TutorialArg.AugmentKey => GameState.Instance.ActionHintText(ActAugmentPanel),
        TutorialArg.TalentKey => GameState.Instance.ActionHintText(ActTalentPanel),
        TutorialArg.BoostCount => _progress.BoostCount,
        TutorialArg.BoostGoal => _progress.BoostGoal,
        TutorialArg.DashCount => _progress.DashCount,
        TutorialArg.DashGoal => _progress.DashGoal,
        TutorialArg.KillCount => _progress.KillCount,
        TutorialArg.KillGoal => _progress.Goal,
        TutorialArg.ParryCount => _progress.ParryCount,
        TutorialArg.ParryGoal => _progress.Goal,
        TutorialArg.ChargePercent => (int)(chargeProgress * 100.0f),
        TutorialArg.ChargeSeconds => HomeChargeTime,
        TutorialArg.DashFuelPercent => DashFuelPercent(),
        TutorialArg.EnragePercent => BossEnragePercent(),
        // 未接线的补参一律显式抛错：此前回退空串时，新加一个补参忘在这里接线会静默渲染成
        // 「少一块」的文案（编译、单测、冒烟全绿）——抛错会被冒烟的错误正则抓红
        _ => throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "教程补参未接线"),
    };

    /// <summary>首领狂暴阈值百分比：取自 Boss 自身的装载值（同一份配置的同一份读数，
    /// 与 Boss 的狂暴判据同源），不在教程里再读一次配置。</summary>
    private int BossEnragePercent() => (int)Mathf.Round(_boss.EnrageHpRatio * 100.0f);

    /// <summary>相位突进的燃料门槛百分比：取自玩家自身的装载值（与冲刺的实际判定同一份读数）——
    /// 文案里写死 25% 时，改 `player.dash.fuel_ratio` 会让教程教一个按不出来的门槛。</summary>
    private int DashFuelPercent() => (int)Mathf.Round(_player.DashFuelRatio * 100.0f);

    /// <summary>阶段横幅：标题自左滑入淡入（目标文本由 SetObjectiveTr 换行时自行入场）。</summary>
    private void PlayStageBanner()
    {
        if (_stageTween != null && _stageTween.IsValid())
        {
            _stageTween.Kill();
        }

        _titleLabel.Modulate = new Color(_titleLabel.Modulate, 0f);
        _titleLabel.Position = new Vector2(TitleBasePos.X - 48.0f, TitleBasePos.Y);
        _stageTween = _titleLabel.CreateTween();
        _stageTween.TweenProperty(_titleLabel, "modulate:a", 1.0f, 0.22).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _stageTween.Parallel().TweenProperty(_titleLabel, "position", TitleBasePos, 0.3)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    private void OnLocaleChanged()
    {
        _titleLabel.Text = (string)Tr(_progress.Stage.TitleKey);
        SetObjectiveTr(_objectiveKey, _objectiveArgs);
        RefreshSkipHint();
    }

    /// <summary>设备档切换：键位补参按当前设备重取（键鼠 ⇄ 手柄），已显示的提示行即时换档。
    /// 返航段已返航时目标行是后续行（带两个键位补参），其余形态按阶段目标行重渲染；
    /// 蓄力行只有百分比补参，下一次 0.1s 节流轮询自然成形，不必在此特判。</summary>
    private void OnHintDeviceChanged()
    {
        if (_finished || _failed)
        {
            return;
        }

        if (_progress.Stage.Goal == TutorialGoalKind.Homecoming && _progress.Charged && !AugmentPanelOpen())
        {
            ShowFollowUpObjective();
        }
        else
        {
            SetStageObjective();
        }

        RefreshSkipHint();
    }

    private void EnterStage(int idx)
    {
        _stage = TutorialCurriculum.ClampStage(idx);
        _progress.EnterStage(TutorialCurriculum.At(_stage));
        _titleLabel.Text = (string)Tr(_progress.Stage.TitleKey);
        // 检查点：进入即写（中途退出/死亡重开都从这里落回同一阶段）
        GameState.Instance.TutorialStage = _stage;
        GameState.Instance.SaveSettings();
        // 跳过蓄力随入场归零：按住不放跨过阶段边界时，新阶段不得立刻被判成一次跳过
        _skipCharge.Reset();
        RefreshSkipHint();
        PlayStageBanner();
        switch (_progress.Stage.Goal)
        {
            case TutorialGoalKind.Marksmanship:
                {
                    // 移动与瞄准：3 个辅助瞄准标记训练靶（正常速度，对齐正局追踪弹体验）
                    SetStageObjective();
                    SpawnAimTargets(_progress.Remaining);
                    break;
                }

            case TutorialGoalKind.Maneuver:
                {
                    // 加速与相位突进
                    // 教程授予相位冲刺（天赋域层级直写口，含 Augments 同步广播）
                    GameState.Instance.Talent.GrantLevel(new StringName("phase_dash"), 1);
                    _prevDashing = false;
                    SetStageObjective();
                    break;
                }

            case TutorialGoalKind.Combat:
                {
                    // 战斗基础：5 只 straight，锁血下限
                    SetStageObjective();
                    SpawnCombatWave(_progress.Remaining);
                    break;
                }

            case TutorialGoalKind.Parry:
                {
                    // 弹反：段内保有射击型靶机（敌弹朝玩家发射、可弹反），成功弹反 N 次即过关；
                    // 无敌口径与首领段一致（教程不判负）——实战段的每帧回血挡不住静止玩家吃多发
                    // 齐射的单帧超额伤害（数发弹同帧结算 > 满血），死亡重开会把段内进度清零
                    SetStageObjective();
                    _player.SetInvincible(999.0f);
                    SpawnParryDummies(TutorialCurriculum.ParryDummyCount);
                    break;
                }

            case TutorialGoalKind.Dock:
                {
                    // 母舰召唤与停靠（对齐正局：长按蓄力 → 穿梭门 → 母舰穿出 → 对接补给）
                    _dockCharge.Reset();
                    SetStageObjective();
                    break;
                }

            case TutorialGoalKind.Homecoming:
                {
                    // 返航与基地
                    _homeCharge.Reset();
                    SetStageObjective();
                    break;
                }

            case TutorialGoalKind.BossEnrage:
                {
                    // 首领遭遇：低 HP Boss-1，触发狂暴即过关
                    _player.SetInvincible(999.0f); // 教程不判负
                    var view5 = GameState.Instance.ViewWorldRect();
                    _boss = _bossScene.Instantiate<Boss>(); // Boss 为 C# typed，typed 实例化
                    _boss.Setup(1.0f, 1);
                    _boss.MaxHp = (float)GameState.Instance.Cfg("tutorial.boss_hp", 120.0).AsDouble();
                    _boss.Hp = _boss.MaxHp;
                    _boss.Position = new Vector2(view5.GetCenter().X, view5.Position.Y - 160.0f);
                    _boss.Enraged += OnBossEnraged; // C# [Signal] 以 PascalCase 注册
                    _boss.Died += OnBossGone; // C# [Signal] 以 PascalCase 注册
                    AddChild(_boss);
                    // 目标行要写狂暴阈值百分比，取值来自 Boss 装载后的读数——故在入场之后再渲染
                    SetStageObjective();
                    break;
                }
        }
    }

    /// <summary>渲染当前阶段的目标行（补参按阶段表声明的顺序取值）。</summary>
    private void SetStageObjective() => SetObjectiveTr(_progress.Stage.ObjectiveKey, Args(_progress.Stage.ObjectiveArgs));

    /// <summary>渲染蓄力进行中的替换行（百分比按各阶段声明的补参取值）。</summary>
    private void SetChargeObjective(HoldCharge charge)
    {
        var stage = _progress.Stage;
        if (stage.ChargeKey.Length > 0 && stage.ChargeArgs != null)
        {
            SetObjectiveTr(stage.ChargeKey, Args(stage.ChargeArgs, charge.Progress));
        }
    }

    /// <summary>玩家死亡：教程不设死局——短暂提示后重开**本阶段**（清场与玩家重生都走场景重载，
    /// 落点由检查点决定），Esc 仍可随时退出。此前死亡只换一行「任务失败」并要求玩家自己退出，
    /// 再从第一阶段重来。</summary>
    private void OnPlayerDied()
    {
        if (_finished || _failed)
        {
            return;
        }

        _failed = true;
        _titleLabel.Text = (string)Tr("TUT_FAIL_TITLE");
        SetObjectiveTr("TUT_FAIL_DESC", new Godot.Collections.Array());
        RefreshSkipHint();
        TimerFx.OneShot(this, TutorialCurriculum.RetryDelaySeconds, ReloadStage, alwaysProcessing: true);
    }

    /// <summary>重开本阶段：整场景重载，落点由检查点给出。不另造一套「复活序列」——玩家死亡态在
    /// 引擎侧已隐藏机体并关掉受击/擦弹/弹反判定，原地复活要逐项反向复位，主路径（重开/继续出击）
    /// 同样用重载。</summary>
    private void ReloadStage()
    {
        if (_finished)
        {
            return;
        }

        GetTree().ReloadCurrentScene();
    }

    /// <summary>阶段 6 软锁兜底：Boss 未触发狂暴即被击杀/逃跑离场（died 两种离场都会发）→ 重置阶段重刷</summary>
    private void OnBossGone()
    {
        if (_stage == TutorialCurriculum.StageCount - 1 && !_finished && !_failed)
        {
            EnterStage(_stage);
        }
    }

    /// <summary>阶段 1 训练靶：辅助瞄准标记靶同款布局补刷（EnterStage 与 _PhysicsProcess 兜底共用，
    /// 防复制漂移；布局对齐正局追踪弹体验，强制 aim_marked 保证确定性）</summary>
    private void SpawnAimTargets(int count)
    {
        var view = GameState.Instance.ViewWorldRect(); // 视口基线（不得硬编码 960/600）
        for (int i = 0; i < count; i++)
        {
            var e = SpawnEnemy(EnemyTypeConfig(), new StringName("straight"));
            e.aim_marked = true; // 教学演示：强制标记（setup 已按比率掷点，此处覆盖保证确定性；AimMarked private set，经 snake 桥写）
            e.Position = new Vector2(view.GetCenter().X - 360.0f + 360.0f * i, view.Position.Y + 280.0f);
        }
    }

    /// <summary>阶段 3 战斗波次：刷 count 只 straight（过关补刷复用同一布局）</summary>
    private void SpawnCombatWave(int count)
    {
        var view = GameState.Instance.ViewWorldRect(); // 视口基线
        for (int i = 0; i < count; i++)
        {
            var e = SpawnEnemy(EnemyTypeConfig(), new StringName("straight"));
            e.Position = new Vector2(view.Position.X + 300.0f + 330.0f * i, view.Position.Y - 60.0f - 120.0f * (i % 2));
        }
    }

    /// <summary>阶段 4 弹反靶机：与实战段同款 straight 布局，但不开辅助瞄准标记——
    /// 本段教的是「时机」，标记框会把注意力引到射击上；被反射弹击落或寿命到期即按保有数补刷。</summary>
    private void SpawnParryDummies(int count)
    {
        var view = GameState.Instance.ViewWorldRect(); // 视口基线
        for (int i = 0; i < count; i++)
        {
            // 错峰首射（经 SpawnEnemy 在 Setup 前就位）：随机初相若相近，齐射间隔恒定、
            // 成簇抵达——匀速弹流更好学（时机可读）
            var e = SpawnEnemy(EnemyTypeConfig(), new StringName("straight"), 0.6f + 0.7f * i);
            e.Position = new Vector2(view.Position.X + 420.0f + 300.0f * i, view.Position.Y - 60.0f - 100.0f * (i % 2));
        }
    }

    /// <summary>场上存活敌机数（教程实体均为本节点子节点；注册表迭代替代每物理帧
    /// GetChildren()——后者每次分配新 Array，注册表为零分配迭代；教程无池化/外部来源差异）</summary>
    private int AliveEnemyCount()
    {
        var n = 0;
        foreach (var node in GameState.Instance.Enemies)
        {
            if (node is Enemy) // Enemy 为 C#，typed `is` 判型
            {
                n += 1;
            }
        }

        return n;
    }

    /// <summary>教程敌机配置（首项，教程只用 straight 基础型）。
    /// 经 Spawner 的共用 merge 入口，保证教程与正局同源——不得直读 BuildEnemyTypes 默认表。
    /// 整表构建含 5 张字典、5 次贴图加载与一次 cfg 合并，而刷怪是逐只调用，故取到后缓存
    /// （机型表在跑动中不变；Enemy 只读该表，不复用会写坏共享配置）。
    /// 实例字段而非静态：静态持 Godot 对象在退出期先于场景树释放时崩。</summary>
    private Godot.Collections.Dictionary? _enemyTypeConfig;

    private Godot.Collections.Dictionary EnemyTypeConfig()
    {
        return _enemyTypeConfig ??= Spawner.BuildMergedEnemyTypes()[0];
    }

    private Enemy SpawnEnemy(Godot.Collections.Dictionary config, StringName strategy, float fireDelayHint = -1.0f)
    {
        var e = _enemyScene.Instantiate<Enemy>(); // Enemy 为 C# typed，typed 实例化
        // 首射延迟提示必须先于 Setup 赋值：Setup 内部读它定首发射计时，之后才赋只对池化复用生效
        e.FireDelayHint = fireDelayHint;
        e.Setup(config, strategy, 1.0f);
        // 仅实战段与弹反段敌机开火：其余阶段的目标不在火力上，多发一波敌弹只会干扰教学
        e.CanShoot = _progress.Stage.Goal is TutorialGoalKind.Combat or TutorialGoalKind.Parry;
        var view = GameState.Instance.ViewWorldRect(); // 视口基线（不得硬编码 960）
        e.Position = new Vector2(view.GetCenter().X, view.Position.Y - 60.0f);
        e.Died += OnEnemyDied; // Enemy 为 C# typed，[Signal] 以 PascalCase 注册
        AddChild(e);
        return e;
    }

    private void OnEnemyDied(Enemy enemy)
    {
        if (_progress.Stage.Goal is not (TutorialGoalKind.Marksmanship or TutorialGoalKind.Combat))
        {
            return;
        }

        _progress.AddKill();
        SetStageObjective();
        if (_progress.IsComplete)
        {
            PassStage();
        }
    }

    /// <summary>弹反成功（玩家侧 ParryLanded 信号）：只在弹反段计数。冷却与窗口判定全在玩家侧
    /// （生产路径），本节点不判「算不算一次弹反」——只判「这一段还要不要数」。</summary>
    private void OnParryLanded()
    {
        if (_progress.Stage.Goal != TutorialGoalKind.Parry || _advancing || _failed || _finished)
        {
            return;
        }

        _progress.AddParry();
        SetStageObjective();
        if (_progress.IsComplete)
        {
            // 击杀段达标时目标本就已全部离场，弹反段达标时靶机还在场上开火——不清场会把
            // 火力带进母舰段（对接是长演出，靶机继续射击会真的打死玩家）
            ClearField();
            PassStage();
        }
    }

    /// <summary>母舰召唤（对齐 main._on_summon_window_finished 的实体路径：穿梭门 + begin_warp_in；
    /// 略去机库小窗演出保持教程节奏）</summary>
    private void SummonMothership()
    {
        var gatePos = new Vector2(
            GameState.Instance.ViewWorldRect().GetCenter().X,
            (float)GameState.Instance.Cfg("mothership.hover_y", 270.0).AsDouble());
        var gate = new WarpGate(); // WarpGate 为 C# typed，typed 实例化
        gate!.Position = gatePos;
        AddChild(gate);
        _mothership = _mothershipScene.Instantiate<Mothership>();
        var mothership = _mothership;
        mothership.BeginWarpIn(gatePos, gate);
        mothership.Departed += OnMothershipDeparted;
        // 对齐 main._on_summon_window_finished：树退出置空，防 _mothership 悬空引用（阶段 4 轮询判空依赖）
        // 旧实例离树只清自己：无条件置空会把已替换上的新实例引用一并抹掉
        mothership.TreeExited += () =>
        {
            if (ReferenceEquals(mothership, _mothership))
            {
                _mothership = null;
            }
        };
        AddChild(mothership);
        ShowFollowUpObjective();
    }

    private void OnMothershipDeparted(float cooldown)
    {
        // 按阶段形态判归属而非硬编码索引：弹反段插入后母舰段索引已移位，写死索引的收尾信号
        // 会被静默吞掉（母舰照常离场、阶段永不推进，引擎侧零报错）
        if (_progress.Stage.Goal != TutorialGoalKind.Dock)
        {
            return;
        }

        _progress.MarkCharged();
        if (_progress.IsComplete)
        {
            PassStage();
        }
    }

    private void OnBossEnraged()
    {
        if (_stage != TutorialCurriculum.StageCount - 1 || _finished)
        {
            return;
        }

        _boss.AbortEnrageSequence(); // 教程触发即过关：中止序列，不冻结玩家移动
        _progress.MarkEnraged();
        if (_progress.IsComplete)
        {
            Finish();
        }
    }

    private void PassStage()
    {
        if (_advancing || _failed)
        {
            return;
        }

        _advancing = true;
        PlaySfxAugmentPick();
        // 一次性 Timer 节点 + 信号回调（禁 await create_timer 协程，退出时协程状态泄漏）；
        // Always：树暂停中仍计时（对齐原 SceneTreeTimer 语义）
        TimerFx.OneShot(this, TutorialCurriculum.PassDelaySeconds, FinishPassStage, alwaysProcessing: true);
    }

    /// <summary>延迟推进必须走 Timer 回调（await create_timer 在教程被释放时协程悬死）</summary>
    private void FinishPassStage()
    {
        // 失败/结束态必须防阶段推进（失败态下已挂起的推进 Timer 仍会触发）
        if (_failed || _finished)
        {
            return;
        }

        // 增幅触点面板不跨阶段存活：推进即收起（下一阶段是首领战，面板压在 Boss 头上属教学事故）
        CloseAugmentPanel();
        _advancing = false;
        if (!TutorialCurriculum.IsLast(_stage))
        {
            EnterStage(TutorialCurriculum.Next(_stage));
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_finished || _failed)
        {
            return;
        }

        var d = (float)delta;
        // 跳过本阶段：长按放弃出击键。逐帧推进（推进窗口内也走，避免按住跨窗口时状态陈旧）；
        // 只在非推进窗口里落地，防与达标推进撞车。
        if (_skipCharge.Tick(d, Input.IsActionPressed(ActGiveUp)) == HoldChargePhase.Triggered && !_advancing)
        {
            SkipStage();
        }

        switch (_progress.Stage.Goal)
        {
            case TutorialGoalKind.Marksmanship:
            case TutorialGoalKind.Combat:
                {
                    if (_progress.Stage.Goal == TutorialGoalKind.Combat)
                    {
                        // 锁血下限：每帧补足，受伤不死
                        var health = GameState.Instance.Health;
                        if (health < _maxHp)
                        {
                            GameState.Instance.Heal(_maxHp - health);
                        }
                    }

                    // 补刷兜底（两阶段同口径）：目标走正常 Enemy 生命周期，15s 寿命到期/飞出屏底
                    // 静默 despawn 不发 Died，场上无目标且未达标时补足剩余数，防新手超时软锁。
                    // 保持每帧检查（不引入节流窗口：0.25s 节流会因释放帧与节流窗口交错而跳过补刷）
                    if (!_advancing && !_progress.IsComplete && AliveEnemyCount() == 0)
                    {
                        var remaining = _progress.Remaining;
                        if (_progress.Stage.Goal == TutorialGoalKind.Marksmanship)
                        {
                            SpawnAimTargets(remaining);
                        }
                        else
                        {
                            SpawnCombatWave(remaining);
                        }
                    }

                    break;
                }

            case TutorialGoalKind.Parry:
                {
                    // 靶机补刷：被反射弹击落或寿命到期离场都不算阶段目标，按保有数补足——
                    // 场上无靶机＝无弹可教（与击杀段的「补足剩余数」不同，这里补的是教具不是进度）
                    if (!_advancing && !_progress.IsComplete && AliveEnemyCount() < TutorialCurriculum.ParryDummyCount)
                    {
                        SpawnParryDummies(TutorialCurriculum.ParryDummyCount - AliveEnemyCount());
                    }

                    break;
                }

            case TutorialGoalKind.Maneuver:
                {
                    // 加速/冲刺输入计数（rising edge）
                    if (Input.IsActionJustPressed(ActBoost))
                    {
                        _progress.AddBoost();
                        SetStageObjective();
                    }

                    if (_player.IsDashing() && !_prevDashing)
                    {
                        _progress.AddDash();
                        SetStageObjective();
                    }

                    _prevDashing = _player.IsDashing();
                    if (_progress.IsComplete)
                    {
                        PassStage();
                    }

                    break;
                }

            case TutorialGoalKind.Dock:
                {
                    // 长按蓄力召唤母舰（对齐正局 dock_charge_time；母舰已在场不再重复触发）
                    if (_mothership == null && !_advancing)
                    {
                        switch (_dockCharge.Tick(d, Input.IsActionPressed(ActDock)))
                        {
                            case HoldChargePhase.Triggered:
                                SummonMothership();
                                break;
                            case HoldChargePhase.Charging:
                                _objectivePoll -= d;
                                if (_objectivePoll <= 0.0f)
                                {
                                    _objectivePoll = ObjectivePollInterval; // 百分比文本节流
                                    SetChargeObjective(_dockCharge);
                                }

                                break;
                            case HoldChargePhase.Released:
                                _objectivePoll = 0.0f;
                                SetStageObjective();
                                break;
                        }
                    }

                    break;
                }

            case TutorialGoalKind.Homecoming:
                {
                    // 推进窗口内不再受理蓄力与面板（同 Dock 分支的窗口门控）：跳过本阶段后的 1s 窗口里
                    // 阶段仍是返航，此时触发会把基地面板弹在下一阶段头上（树暂停 1.2s，Boss 在基地后面入场）
                    if (_advancing)
                    {
                        break;
                    }

                    // 增幅触点：面板开着时再按一次收起（与生产 L 键的展开/收起语义一致）
                    if (_augmentPanel != null && _augmentPanel.Visible)
                    {
                        if (Input.IsActionJustPressed(ActAugmentPanel))
                        {
                            CloseAugmentPanel();
                        }

                        break;
                    }

                    if (_progress.Charged)
                    {
                        // 已返航：等待玩家实际打开一次增幅面板（阶段达成条件之二，见 TutorialProgress）
                        if (Input.IsActionJustPressed(ActAugmentPanel))
                        {
                            OpenAugmentPanel();
                        }

                        break;
                    }

                    switch (_homeCharge.Tick(d, Input.IsActionPressed(ActHomecoming)))
                    {
                        case HoldChargePhase.Triggered:
                            _progress.MarkCharged();
                            OpenBase();
                            ShowFollowUpObjective();
                            break;
                        case HoldChargePhase.Charging:
                            _objectivePoll -= d;
                            if (_objectivePoll <= 0.0f)
                            {
                                _objectivePoll = ObjectivePollInterval; // 百分比文本节流
                                SetChargeObjective(_homeCharge);
                            }

                            break;
                        case HoldChargePhase.Released:
                            _objectivePoll = 0.0f;
                            SetStageObjective();
                            break;
                    }

                    break;
                }
        }

    }

    /// <summary>跳过本阶段：清场后走与达标同一条推进链；末阶段跳过即收尾（玩家到了这里仍选择
    /// 结束，教程不再留一条必须打完的尾巴）。跳过不改写完成度语义以外的任何状态。</summary>
    private void SkipStage()
    {
        ClearField();
        if (TutorialCurriculum.IsLast(_stage))
        {
            Finish();
            return;
        }

        PassStage();
    }

    /// <summary>清场：教程场上实体全部回收（完成收尾与跳过共用——跳过后上一阶段的目标仍在场，
    /// 既会撞伤玩家，也会混进下一阶段的判据与补刷口径）。
    /// 顺带解锁玩家输入：对接流程（母舰 DOCKING 起）是教程里唯一给玩家上输入锁的地方，而它的解锁点
    /// 在 RELEASE——母舰被中途回收时那一步永不执行，玩家会带着锁进入后续阶段（不能动、不能开火，
    /// 引擎侧零报错）。<see cref="Player.UnlockInput"/> 幂等，未上锁时调用无副作用。</summary>
    private void ClearField()
    {
        foreach (var child in GetChildren())
        {
            if (child is Enemy || child is Boss || child is Bullet || child is Mothership)
            {
                child.QueueFree();
            }
        }

        _player.UnlockInput();
    }

    private void OpenBase()
    {
        if (_baseUi != null)
        {
            return;
        }

        _baseUi = new BaseConsole(); // BaseConsole 为 C# typed，typed 实例化
        _baseUi.ProcessMode = Node.ProcessModeEnum.Always;
        AddChild(_baseUi);
        _baseUi.ResumeRequested += OnBaseResume;
        _baseUi.ShowBase();
        GameState.Instance.SetTreePaused(true);
        // Always 态 Esc 返回路由：基地开启的 ~1.2s 窗口期树暂停，本节点（Pausable）
        // 的 _UnhandledInput 收不到 Esc（窗口期 Esc 失灵）；路由节点 Always 态代收转发退出
        _escRouter = new TutorialEscRouter { ProcessMode = Node.ProcessModeEnum.Always, OnCancel = ExitTutorial };
        AddChild(_escRouter);
        // 打开即过关：1s 后自动关闭进入下一阶段（玩家点继续出击同样推进）；
        // Always：树暂停（基地 UI）中仍需计时
        if (_progress.IsComplete)
        {
            PassStage();
        }

        TimerFx.OneShot(this, 1.2, CloseBase, alwaysProcessing: true);
    }

    private void CloseBase()
    {
        if (_baseUi == null)
        {
            return;
        }

        GameState.Instance.SetTreePaused(false);
        if (_escRouter != null)
        {
            _escRouter.QueueFree();
            _escRouter = null;
        }

        _baseUi.QueueFree();
        _baseUi = null;
    }

    private void OnBaseResume() => CloseBase();

    /// <summary>渲染后续目标行（补参按阶段表声明；母舰段的后续行无补参，基地段带两个键位补参）。</summary>
    private void ShowFollowUpObjective()
    {
        var stage = _progress.Stage;
        if (stage.FollowUpKey.Length == 0)
        {
            return;
        }

        SetObjectiveTr(
            stage.FollowUpKey,
            stage.FollowUpArgs != null ? Args(stage.FollowUpArgs) : new Godot.Collections.Array());
    }

    /// <summary>基地段增幅触点：实际打开一次面板即达成本阶段（数据与行装配同生产 HUD——
    /// 读 <c>GameState.Augments</c>、键名走 <c>AUG_*_NAME</c>，教程内零经济链）。
    /// 布局对齐生产增幅滚动栏（右侧居中挂靠），无增幅时给一行空态说明——教程局拿不到增幅，
    /// 空面板会让「打开看看」变成「打开了寂寞」。</summary>
    private void OpenAugmentPanel()
    {
        CloseAugmentPanel();
        var panel = new ChamferedPanel
        {
            Padding = 0.0f,
            Position = new Vector2(-356.0f, -260.0f),
            Size = new Vector2(340.0f, 520.0f),
        };
        panel.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        panel.AddChild(margin);
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        margin.AddChild(vbox);
        vbox.AddChild(UITheme.MakeLabel((string)Tr("UI_AUGMENTS_TITLE"), UITheme.FontHud, UITheme.Accent, HorizontalAlignment.Left));
        var divider = new ColorRect
        {
            Color = UITheme.AccentDim,
            CustomMinimumSize = new Vector2(0.0f, 1.0f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        vbox.AddChild(divider);
        var augments = GameState.Instance.Augments;
        var listed = 0;
        foreach (var key in augments.Keys)
        {
            var stacks = (int)augments[key].AsInt64();
            if (stacks <= 0)
            {
                continue;
            }

            var id = key.AsStringName();
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            row.AddChild(AugmentIcons.MakeGlyph(id, AugmentIcons.ColorFor(id), 24.0f));
            var nameLabel = UITheme.MakeLabel(
                (string)Tr($"AUG_{id.ToString().ToUpperInvariant()}_NAME"), UITheme.FontHud, UITheme.Text, HorizontalAlignment.Left);
            nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(nameLabel);
            if (stacks > 1)
            {
                row.AddChild(UITheme.MakeLabel(GdFormat.Format("×%d", stacks), UITheme.FontHud, UITheme.AccentGold, HorizontalAlignment.Right));
            }

            vbox.AddChild(row);
            listed += 1;
        }

        if (listed == 0)
        {
            vbox.AddChild(UITheme.MakeLabel((string)Tr("TUT_AUGMENT_EMPTY"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Left));
        }

        _hudLayer.AddChild(panel);
        UITheme.AnimateOpen(panel);
        _augmentPanel = panel;
        _progress.MarkPanelOpened();
        if (_progress.IsComplete)
        {
            PassStage();
        }
    }

    /// <summary>收起增幅触点面板（玩家再按一次或阶段推进时；推进收起见 <see cref="FinishPassStage"/>）。</summary>
    private void CloseAugmentPanel()
    {
        if (_augmentPanel == null)
        {
            return;
        }

        // 局部捕获：字段马上置空，闭包再读字段会拿 null 而永不释放（退场动画播完面板悬死在树上）
        var panel = _augmentPanel;
        _augmentPanel = null;
        if (GodotObject.IsInstanceValid(panel))
        {
            UITheme.AnimateClose(panel, onDone: () =>
            {
                if (GodotObject.IsInstanceValid(panel))
                {
                    panel.QueueFree();
                }
            });
        }
    }

    private void Finish()
    {
        _finished = true;
        GameState.Instance.TutorialDone = true;
        GameState.Instance.TutorialStage = 0; // 检查点清零：下次进入从第一阶段起（完成度已记）
        GameState.Instance.SaveSettings();
        RefreshSkipHint();
        PlaySfxAugmentPick();
        ClearField();
        _titleLabel.Text = (string)Tr("TUT_DONE");
        SetObjectiveTr("TUT_DONE_DESC", new Godot.Collections.Array());
        // 完成面板走切角面板（与全站视觉语言一致），控件装配后 pivot 置中，入场缩放脉冲
        var panel = new ChamferedPanel { Brackets = true };
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Position = new Vector2(-160.0f, -40.0f);
        panel.CustomMinimumSize = new Vector2(320.0f, 0.0f);
        panel.Padding = 24.0f;
        var button = UITheme.MakeButton((string)Tr("TUT_BACK"), true);
        button.Pressed += ExitTutorial;
        panel.AddChild(button);
        _hudLayer.AddChild(panel);
        _completePanel = panel;
        // 首帧布局未定，pivot 尺寸要等布局完成后再取
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(panel))
            {
                UITheme.AnimateOpen(panel);
                UITheme.PunchScale(panel, 1.06f, 0.2f);
            }
        }).CallDeferred();
    }

    private void ExitTutorial()
    {
        GameState.Instance.ExitToTitle(); // 单口内含 TimeScale/暂停复位与 ResetRun（不污染正常本局）
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // 教程中按 Esc 直接退出回标题屏（无暂停菜单）
        if (@event.IsActionPressed("ui_cancel"))
        {
            ExitTutorial();
        }
    }

    private void PlaySfxAugmentPick() => GameState.Instance.PlaySfx(SfxId.AugmentPick);

    /// <summary>Variant 数组转 object[]（GDScript `%` 参数补参用；GodotSharp 无 Array.ToArray）。</summary>
    private static object[] ToObjects(Godot.Collections.Array args)
    {
        var objs = new object[args.Count];
        for (var i = 0; i < args.Count; i++)
        {
            objs[i] = args[i].Obj!; // 教程补参均为非空标量（int 计数/百分比/键名文本），Obj 不可能为 null
        }

        return objs;
    }
}

/// <summary>Always 态 Esc 返回路由：教程基地开启的 ~1.2s 窗口期树暂停，
/// Tutorial（Pausable）的 _UnhandledInput 收不到 Esc；本节点 Always 态代收并转发退出回调。</summary>
public partial class TutorialEscRouter : Node
{
    public Action? OnCancel { get; set; }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            // 先标输入再转发：转发回调（ExitTutorial）切场景摘树后 GetViewport() 为 null
            GetViewport().SetInputAsHandled();
            OnCancel?.Invoke();
        }
    }
}
