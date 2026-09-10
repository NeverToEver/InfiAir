using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 新手教程（对齐原作 6 阶段）：独立场景，脚本驱动检查点，复用现有实体。
/// 不启动正常 Spawner 波次；进场/出场各 ResetRun 隔离对局状态，出场保证 TimeScale=1。
/// 实体判定（Enemy/Boss/Mothership/Bullet）均为 C# 类，typed `is` 判型。
/// </summary>
public partial class Tutorial : Node2D
{
    // 静态 Godot 资源必须改实例字段——静态持 RefCounted/资源
    // 在引擎退出后被 .NET finalize 触碰 native 可致 segfault
    private readonly FontFile _font = UITheme.Font;
    private readonly PackedScene _enemyScene = GD.Load<PackedScene>("res://scenes/enemy.tscn");
    private readonly PackedScene _bossScene = GD.Load<PackedScene>("res://scenes/boss.tscn");
    private readonly PackedScene _mothershipScene = GD.Load<PackedScene>("res://scenes/mothership.tscn");

    public float HomeChargeTime = 1.5f;
    public float DockChargeTime = 3.0f; // 母舰召唤蓄力（mothership.dock_charge_time，对齐正局）

    private static readonly string[] StageTitles =
    {
        "TUT_S1_TITLE",
        "TUT_S2_TITLE",
        "TUT_S3_TITLE",
        "TUT_S4_TITLE",
        "TUT_S5_TITLE",
        "TUT_S6_TITLE",
    };

    private int _stage;
    private bool _advancing;
    private int _stageKills;
    /// <summary>训练靶阶段（case 0）目标击杀数：补刷兜底与进度判定共用。</summary>
    private const int AimTargetKillGoal = 3;
    /// <summary>实战阶段（case 2）目标击杀数：补刷兜底与进度判定共用。</summary>
    private const int CombatKillGoal = 5;
    /// <summary>蓄力百分比文本刷新节流（对齐 HUD 仪表约定）。</summary>
    private const float ObjectivePollInterval = 0.1f;
    private int _boostCount;
    private int _dashCount;
    private bool _prevDashing;
    private float _homeCharge;
    private float _dockCharge;
    private float _maxHp = 100.0f; // 阶段 2 锁血每物理帧用，_ready 缓存一次（教程内 buffs 不变）
    private float _objectivePoll; // 蓄力百分比文本 0.1s 节流计时（对齐 HUD 仪表约定）
    private BaseConsole? _baseUi; // typed 字段
    private TutorialEscRouter? _escRouter; // 基地开启窗口期（树暂停）的 Always 态 Esc 返回路由
    private Boss _boss = null!; // typed 字段
    private Mothership? _mothership;
    private bool _finished;
    private bool _failed;

    private Label _titleLabel = null!;
    private Label _objectiveLabel = null!;
    private string _objectiveKey = "";
    private Godot.Collections.Array _objectiveArgs = new();
    private PanelContainer _completePanel = null!;
    private CanvasLayer _hudLayer = null!;
    private Player _player = null!;

    private readonly Callable _onLocaleChanged;
    private readonly Callable _onPlayerDied;

    public Tutorial()
    {
        _onLocaleChanged = Callable.From(OnLocaleChanged);
        _onPlayerDied = Callable.From(OnPlayerDied);
    }







    public override void _Ready()
    {
        GameState.Instance.ResetRun();
        _maxHp = (float)GameState.Instance.MaxHealth(); // 热路径缓存（阶段 2 锁血每物理帧读）
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

        // 辅助瞄准框覆盖层：与 Main 同款运行时创建（登记 GameState.AimFrameLayer），
        // 教程内标记框与追踪弹行为与正局一致；随场景切换自动注销
        AddChild(new AimFrameLayer());
        _player = GetNode<Player>("Player");
        // 世界层画面增强（layer=1，世界之上、HUD 之下）：BuildHud 之前入树，
        // 与 HUD（layer=2）分层——教程画面与正局同款辉光/分级
        AddChild(new WorldPostFx());
        BuildHud();
        HomeChargeTime = (float)GameState.Instance.Cfg("effects.home_charge_time", HomeChargeTime).AsDouble();
        DockChargeTime = (float)GameState.Instance.Cfg("mothership.dock_charge_time", DockChargeTime).AsDouble();
        EnterStage(0);
    }

    public override void _ExitTree()
    {
        // 配对的信号断开——教程 Esc/完成退出后残留连接
        // 在正局死亡（PlayerDied 高频）或切语言时回调已释放实例
        var gs = GameState.Instance;
        if (gs.IsConnected(GameState.SignalName.LocaleChanged, _onLocaleChanged))
        {
            gs.Disconnect(GameState.SignalName.LocaleChanged, _onLocaleChanged);
        }

        if (gs.IsConnected(GameState.SignalName.PlayerDied, _onPlayerDied))
        {
            gs.Disconnect(GameState.SignalName.PlayerDied, _onPlayerDied);
        }
    }

    private void BuildHud()
    {
        _hudLayer = new CanvasLayer { Layer = 2 };
        AddChild(_hudLayer);
        _titleLabel = new Label();
        _titleLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _titleLabel.Position = new Vector2(-400.0f, 24.0f);
        _titleLabel.CustomMinimumSize = new Vector2(800.0f, 0.0f);
        _titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _titleLabel.AddThemeFontOverride("font", _font);
        _titleLabel.AddThemeFontSizeOverride("font_size", 34);
        _titleLabel.AddThemeColorOverride("font_color", UITheme.AccentGold);
        _hudLayer.AddChild(_titleLabel);
        _objectiveLabel = new Label();
        _objectiveLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _objectiveLabel.Position = new Vector2(-500.0f, 74.0f);
        _objectiveLabel.CustomMinimumSize = new Vector2(1000.0f, 0.0f);
        _objectiveLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _objectiveLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _objectiveLabel.AddThemeFontOverride("font", _font);
        _objectiveLabel.AddThemeFontSizeOverride("font_size", 22);
        _objectiveLabel.AddThemeColorOverride("font_color", UITheme.TextDim);
        _hudLayer.AddChild(_objectiveLabel);
    }

    private void SetObjectiveTr(string key) => SetObjectiveTr(key, new Godot.Collections.Array());

    private void SetObjectiveTr(string key, Godot.Collections.Array args)
    {
        _objectiveKey = key;
        _objectiveArgs = args;
        // tr(key) % args if not args.is_empty() else tr(key)
        _objectiveLabel.Text = args.Count > 0 ? GdFormat.Format((string)Tr(key), ToObjects(args)) : (string)Tr(key);
    }

    private void OnLocaleChanged()
    {
        _titleLabel.Text = (string)Tr(StageTitles[_stage]);
        SetObjectiveTr(_objectiveKey, _objectiveArgs);
    }

    private void EnterStage(int idx)
    {
        _stage = idx;
        _stageKills = 0;
        _titleLabel.Text = (string)Tr(StageTitles[idx]);
        switch (idx)
        {
            case 0:
                {
                    // 移动与瞄准：3 个辅助瞄准标记训练靶（正常速度，对齐正局追踪弹体验）
                    SetObjectiveTr("TUT_S1_OBJ", new Godot.Collections.Array { 0 });
                    SpawnAimTargets(3);
                    break;
                }

            case 1:
                {
                    // 加速与相位突进
                    // 教程授予相位冲刺（天赋域层级直写口，含 Augments 同步广播）
                    GameState.Instance.Talent.GrantLevel(new StringName("phase_dash"), 1);
                    _boostCount = 0;
                    _dashCount = 0;
                    _prevDashing = false;
                    UpdateBoostObjective();
                    break;
                }

            case 2:
                {
                    // 战斗基础：5 只 straight，锁血下限
                    SetObjectiveTr("TUT_S3_OBJ", new Godot.Collections.Array { 0 });
                    SpawnCombatWave(5);
                    break;
                }

            case 3:
                {
                    // 母舰召唤与停靠（对齐正局：长按 H 蓄力 → 穿梭门 → 母舰穿出 → 对接补给）
                    _dockCharge = 0.0f;
                    SetObjectiveTr("TUT_S4_OBJ");
                    break;
                }

            case 4:
                {
                    // 返航与基地
                    _homeCharge = 0.0f;
                    SetObjectiveTr("TUT_S5_OBJ");
                    break;
                }

            case 5:
                {
                    // 首领遭遇：低 HP Boss-1，触发狂暴即过关
                    SetObjectiveTr("TUT_S6_OBJ");
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
                    break;
                }
        }
    }

    /// <summary>玩家死亡：教程无法推进（阶段 4/5 依赖玩家存活操作），提示失败并等待 Esc 退出</summary>
    private void OnPlayerDied()
    {
        if (_finished || _failed)
        {
            return;
        }

        _failed = true;
        _titleLabel.Text = (string)Tr("TUT_FAIL_TITLE");
        SetObjectiveTr("TUT_FAIL_DESC");
    }

    /// <summary>阶段 6 软锁兜底：Boss 未触发狂暴即被击杀/逃跑离场（died 两种离场都会发）→ 重置阶段重刷</summary>
    private void OnBossGone()
    {
        if (_stage == 5 && !_finished && !_failed)
        {
            EnterStage(5);
        }
    }

    /// <summary>阶段 3 战斗波次：刷 count 只 straight（过关补刷复用同一布局）</summary>
    /// <summary>阶段 0 训练靶：辅助瞄准标记靶同款布局补刷（EnterStage(0) 与 _PhysicsProcess 兜底共用，
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

    private void SpawnCombatWave(int count)
    {
        var view = GameState.Instance.ViewWorldRect(); // 视口基线
        for (int i = 0; i < count; i++)
        {
            var e = SpawnEnemy(EnemyTypeConfig(), new StringName("straight"));
            e.Position = new Vector2(view.Position.X + 300.0f + 330.0f * i, view.Position.Y - 60.0f - 120.0f * (i % 2));
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

    /// <summary>敌机配置取默认表首项（教程只用 straight 基础型）。</summary>
    private static Godot.Collections.Dictionary EnemyTypeConfig()
    {
        // Spawner.ENEMY_TYPES 为实例属性——默认表经静态工厂构建（教程只用 straight 基础型）
        return Spawner.BuildEnemyTypes()[0];
    }

    private Enemy SpawnEnemy(Godot.Collections.Dictionary config, StringName strategy)
    {
        var e = _enemyScene.Instantiate<Enemy>(); // Enemy 为 C# typed，typed 实例化
        e.Setup(config, strategy, 1.0f);
        e.CanShoot = _stage == 2; // 仅战斗阶段敌机开火
        var view = GameState.Instance.ViewWorldRect(); // 视口基线（不得硬编码 960）
        e.Position = new Vector2(view.GetCenter().X, view.Position.Y - 60.0f);
        e.Died += OnEnemyDied; // Enemy 为 C# typed，[Signal] 以 PascalCase 注册
        AddChild(e);
        return e;
    }

    private void OnEnemyDied(Enemy enemy)
    {
        if (_stage != 0 && _stage != 2)
        {
            return;
        }

        _stageKills += 1;
        if (_stage == 0)
        {
            SetObjectiveTr("TUT_S1_OBJ", new Godot.Collections.Array { _stageKills });
            if (_stageKills >= AimTargetKillGoal)
            {
                PassStage();
            }
        }
        else if (_stage == 2)
        {
            SetObjectiveTr("TUT_S3_OBJ", new Godot.Collections.Array { _stageKills });
            if (_stageKills >= CombatKillGoal)
            {
                PassStage();
            }
        }
    }

    private void UpdateBoostObjective()
    {
        SetObjectiveTr("TUT_S2_OBJ", new Godot.Collections.Array { _boostCount, _dashCount });
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
        _mothership.BeginWarpIn(gatePos, gate);
        _mothership.Departed += OnMothershipDeparted;
        // 对齐 main._on_summon_window_finished：树退出置空，防 _mothership 悬空引用（阶段 3 轮询判空依赖）
        _mothership.TreeExited += () => _mothership = null;
        AddChild(_mothership);
        SetObjectiveTr("TUT_S4_DOCK");
    }

    private void OnMothershipDeparted(float cooldown)
    {
        if (_stage == 3)
        {
            PassStage();
        }
    }

    private void OnBossEnraged()
    {
        if (_stage == 5 && !_finished)
        {
            _boss.AbortEnrageSequence(); // 教程触发即过关：中止序列，不冻结玩家移动
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
        TimerFx.OneShot(this, 1.0, FinishPassStage, alwaysProcessing: true);
    }

    /// <summary>_pass_stage 的延迟推进必须走 Timer 回调（await create_timer 在教程被释放时协程悬死）</summary>
    private void FinishPassStage()
    {
        // 失败/结束态必须防阶段推进（失败态下已挂起的推进 Timer 仍会触发）
        if (_failed || _finished)
        {
            return;
        }

        _advancing = false;
        if (_stage < StageTitles.Length - 1)
        {
            EnterStage(_stage + 1);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_finished || _failed)
        {
            return;
        }

        var d = (float)delta;
        switch (_stage)
        {
            case 0:
                {
                    // 补刷兜底（对齐 case 2 口径）：训练靶走正常 Enemy 生命周期，15s 寿命到期/飞出屏底
                    // 静默 despawn 不发 Died，场上无靶且 _stageKills 未达标时补足剩余数，防新手超时软锁。
                    // 保持每帧检查（与 case 2 同理，不引入节流窗口）
                    if (!_advancing && _stageKills < AimTargetKillGoal && AliveEnemyCount() == 0)
                    {
                        SpawnAimTargets(AimTargetKillGoal - _stageKills);
                    }

                    break;
                }

            case 1:
                {
                    // 加速/冲刺输入计数（rising edge）
                    if (Input.IsActionJustPressed("boost"))
                    {
                        _boostCount = Mathf.Min(_boostCount + 1, 2);
                        UpdateBoostObjective();
                    }

                    if (_player.IsDashing() && !_prevDashing)
                    {
                        _dashCount = Mathf.Min(_dashCount + 1, 2);
                        UpdateBoostObjective();
                    }

                    _prevDashing = _player.IsDashing();
                    if (_boostCount >= 2 && _dashCount >= 2)
                    {
                        PassStage();
                    }

                    break;
                }

            case 2:
                {
                    // 锁血下限：每帧补足，受伤不死
                    var health = GameState.Instance.Health;
                    if (health < _maxHp)
                    {
                        GameState.Instance.Heal(_maxHp - health);
                    }

                    // 补刷兜底：敌机飞出屏幕自毁不计击杀，场上无敌机且未达标时补足剩余数。
                    // 注意：必须保持每帧检查（queue_free 释放与检查窗口需即时生效；
                    // 0.25s 节流会因释放帧与节流窗口交错而跳过补刷）
                    if (!_advancing && _stageKills < CombatKillGoal && AliveEnemyCount() == 0)
                    {
                        SpawnCombatWave(CombatKillGoal - _stageKills);
                    }

                    break;
                }

            case 3:
                {
                    // 长按 H 蓄力召唤母舰（对齐正局 dock_charge_time；母舰已在场不再重复触发）
                    if (_mothership == null && !_advancing)
                    {
                        if (Input.IsActionPressed("dock"))
                        {
                            _dockCharge += d;
                            _objectivePoll -= d;
                            if (_objectivePoll <= 0.0f)
                            {
                                _objectivePoll = ObjectivePollInterval; // 百分比文本节流
                                SetObjectiveTr("TUT_S4_CHARGE", new Godot.Collections.Array { (int)(Mathf.Clamp(_dockCharge / DockChargeTime, 0.0f, 1.0f) * 100.0f) });
                            }

                            if (_dockCharge >= DockChargeTime)
                            {
                                SummonMothership();
                            }
                        }
                        else if (_dockCharge > 0.0f)
                        {
                            _dockCharge = 0.0f;
                            _objectivePoll = 0.0f;
                            SetObjectiveTr("TUT_S4_OBJ");
                        }
                    }

                    break;
                }

            case 4:
                {
                    if (Input.IsActionPressed("homecoming"))
                    {
                        _homeCharge += d;
                        _objectivePoll -= d;
                        if (_objectivePoll <= 0.0f)
                        {
                            _objectivePoll = ObjectivePollInterval; // 百分比文本节流
                            SetObjectiveTr("TUT_S5_CHARGE", new Godot.Collections.Array { (int)(Mathf.Clamp(_homeCharge / HomeChargeTime, 0.0f, 1.0f) * 100.0f) });
                        }

                        if (_homeCharge >= HomeChargeTime)
                        {
                            OpenBase();
                        }
                    }
                    else if (_homeCharge > 0.0f)
                    {
                        _homeCharge = 0.0f;
                        _objectivePoll = 0.0f;
                        SetObjectiveTr("TUT_S5_OBJ");
                    }

                    break;
                }
        }
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
        PassStage();
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

    private void Finish()
    {
        _finished = true;
        GameState.Instance.TutorialDone = true;
        GameState.Instance.SaveSettings();
        PlaySfxAugmentPick();
        // 清场
        foreach (var child in GetChildren())
        {
            if (child is Enemy || child is Boss || child is Bullet || child is Mothership)
            {
                child.QueueFree();
            }
        }

        _titleLabel.Text = (string)Tr("TUT_DONE");
        SetObjectiveTr("TUT_DONE_DESC");
        _completePanel = new PanelContainer();
        _completePanel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _completePanel.Position = new Vector2(-160.0f, -40.0f);
        _completePanel.CustomMinimumSize = new Vector2(320.0f, 0.0f);
        var style = UITheme.MakeMetalPanelStyle();
        style.SetContentMarginAll(20.0f);
        _completePanel.AddThemeStyleboxOverride("panel", style);
        var button = new Button();
        button.Text = (string)Tr("TUT_BACK");
        button.AddThemeFontOverride("font", _font);
        button.AddThemeFontSizeOverride("font_size", 26);
        UITheme.ApplyButton(button);
        button.Pressed += ExitTutorial;
        _completePanel.AddChild(button);
        _hudLayer.AddChild(_completePanel);
    }

    private void ExitTutorial()
    {
        GameState.Instance.ExitToTitle(); // 单口内含 TimeScale/暂停复位与 ResetRun（不污染正常对局）
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
        for (int i = 0; i < args.Count; i++)
        {
            objs[i] = args[i].Obj!; // 教程补参均为非空标量（int 计数/百分比），Obj 不可能为 null
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
