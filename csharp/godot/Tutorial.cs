using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 新手教程（对齐原作 6 阶段）：独立场景，脚本驱动检查点，复用现有实体。
/// 不启动正常 spawner 波次；进场 reset_run + 删档隔离，出场再 reset 并保证 time_scale=1。
/// M5 全量迁移（2026-08-08 自 scripts/tutorial.gd）。
/// 实体判定（Enemy/Boss/Mothership/Bullet）均为 C# 类，typed `is` 判型——原 GDScript 的
/// 脚本资源判型（load() + is_instance_of）在 C# 侧不再需要。
/// </summary>
public partial class Tutorial : Node2D
{
    // U07（2026-08-09 审计）：静态 Godot 资源改实例字段——静态持 RefCounted/资源
    // 在引擎退出后被 .NET finalize 触碰 native 可致 segfault（UITheme.cs:53 实测教训）
    private readonly FontFile _font = UITheme.Font;
    private readonly PackedScene _enemyScene = GD.Load<PackedScene>("res://scenes/enemy.tscn");
    private readonly PackedScene _bossScene = GD.Load<PackedScene>("res://scenes/boss.tscn");
    private readonly PackedScene _mothershipScene = GD.Load<PackedScene>("res://scenes/mothership.tscn");
    private readonly AudioStream _sfxBuffPick = GD.Load<AudioStream>("res://assets/audio/buff_pick.wav");

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
    private int _boostCount;
    private int _dashCount;
    private bool _prevDashing;
    private float _homeCharge;
    private float _dockCharge;
    private float _maxHp = 100.0f; // G05：阶段 2 锁血每物理帧用，_ready 缓存一次（教程内 buffs 不变）
    private float _objectivePoll; // G015：蓄力百分比文本 0.1s 节流计时（对齐 HUD 仪表约定）
    private BaseConsole? _baseUi; // M5：BaseConsole 已迁 C#，typed 字段（原 GDScript set_script 不需要）
    private Boss _boss = null!; // M3d：Boss 迁 C#，typed 字段
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

    // A7：测试/诊断白盒断言经公开接口
    public int Stage() => _stage;

    public Boss? Boss() => _boss;

    public Mothership? Mothership() => _mothership;

    public int StageKills() => _stageKills;

    public int BoostCount() => _boostCount;

    public int DashCount() => _dashCount;

    public CanvasLayer? BaseUi() => _baseUi;

    public bool Finished() => _finished;

    public bool Failed() => _failed;

    public Label TitleLabel() => _titleLabel;

    public Label ObjectiveLabel() => _objectiveLabel;

    public PanelContainer? CompletePanel() => _completePanel;

    public override void _Ready()
    {
        // 存档隔离：教程不读写 savegame
        GameState.Instance.DeleteSave();
        GameState.Instance.ResetRun();
        _maxHp = (float)GameState.Instance.MaxHealth(); // G05：热路径缓存（阶段 2 锁血每物理帧读）
        RenderingServer.SetDefaultClearColor(new Color(0.02f, 0.02f, 0.06f));
        var gs = GameState.Instance;
        if (gs != null && !gs.IsConnected("LocaleChanged", _onLocaleChanged))
        {
            gs.Connect("LocaleChanged", _onLocaleChanged);
        }

        if (gs != null && !gs.IsConnected("PlayerDied", _onPlayerDied))
        {
            gs.Connect("PlayerDied", _onPlayerDied);
        }

        // 辅助瞄准框覆盖层：与 main.gd 同款运行时创建（登记 GameState.aim_frame_layer），
        // 教程内标记框与追踪弹行为与正局一致；随场景切换自动注销
        AddChild(new AimFrameLayer()); // M3c：AimFrameLayer 迁 C#，typed 实例化
        _player = GetNode<Player>("Player"); // M3c：Player 迁 C#，typed 字段
        BuildHud();
        HomeChargeTime = (float)GameState.Instance.Cfg("effects.home_charge_time", HomeChargeTime).AsDouble();
        DockChargeTime = (float)GameState.Instance.Cfg("mothership.dock_charge_time", DockChargeTime).AsDouble();
        EnterStage(0);
    }

    public override void _ExitTree()
    {
        // U02（2026-08-09 审计）：C22 模式配对断开——教程 Esc/完成退出后残留连接
        // 在正局死亡（PlayerDied 高频）或切语言时回调已释放实例
        var gs = GameState.Instance;
        if (gs == null)
        {
            return;
        }

        if (gs.IsConnected("LocaleChanged", _onLocaleChanged))
        {
            gs.Disconnect("LocaleChanged", _onLocaleChanged);
        }

        if (gs.IsConnected("PlayerDied", _onPlayerDied))
        {
            gs.Disconnect("PlayerDied", _onPlayerDied);
        }
    }

    private void BuildHud()
    {
        _hudLayer = new CanvasLayer();
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
                    var view0 = GameState.Instance.ViewWorldRect(); // G014：视口基线（D10 口径，去 960/600 硬编码）
                    for (int i = 0; i < 3; i++)
                    {
                        var e = SpawnEnemy(EnemyTypeConfig(), new StringName("straight"));
                        e.aim_marked = true; // 教学演示：强制标记（setup 已按比率掷点，此处覆盖保证确定性；AimMarked private set，经 snake 桥写）
                        e.Position = new Vector2(view0.GetCenter().X - 360.0f + 360.0f * i, view0.Position.Y + 280.0f);
                    }

                    break;
                }

            case 1:
                {
                    // 加速与相位突进
                    GameState.Instance.AddBuff(new StringName("phase_dash"));
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
                    var view5 = GameState.Instance.ViewWorldRect(); // G014
                    _boss = _bossScene.Instantiate<Boss>(); // M3d：Boss 迁 C#，typed 实例化
                    _boss.Setup(1.0f, 1);
                    _boss.MaxHp = (float)GameState.Instance.Cfg("tutorial.boss_hp", 120.0).AsDouble();
                    _boss.Hp = _boss.MaxHp;
                    _boss.Position = new Vector2(view5.GetCenter().X, view5.Position.Y - 160.0f);
                    _boss.Enraged += OnBossEnraged; // M3d：C# [Signal] 以 PascalCase 注册
                    _boss.Died += OnBossGone; // M3d：C# [Signal] 以 PascalCase 注册
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
    private void SpawnCombatWave(int count)
    {
        var view = GameState.Instance.ViewWorldRect(); // G014：视口基线
        for (int i = 0; i < count; i++)
        {
            var e = SpawnEnemy(EnemyTypeConfig(), new StringName("straight"));
            e.Position = new Vector2(view.Position.X + 300.0f + 330.0f * i, view.Position.Y - 60.0f - 120.0f * (i % 2));
        }
    }

    /// <summary>场上存活敌机数（教程实体均为本节点子节点；U14：注册表迭代替代每物理帧
    /// GetChildren()——后者每次分配新 Array，注册表为零分配迭代；教程无池化/外部来源差异）</summary>
    private int AliveEnemyCount()
    {
        var n = 0;
        foreach (var node in GameState.Instance.Enemies)
        {
            if (node is Enemy) // M3b：Enemy 迁 C#，typed `is` 判型
            {
                n += 1;
            }
        }

        return n;
    }

    /// <summary>敌机配置取 spawner.ENEMY_TYPES[0]（教程只用 straight 基础型；static var 经脚本资源读取）</summary>
    private static Godot.Collections.Dictionary EnemyTypeConfig()
    {
        // M6：Spawner 迁 C#，ENEMY_TYPES 为实例属性——默认表静态构建（教程只用 straight 基础型）
        return Spawner.BuildEnemyTypes()[0];
    }

    private Enemy SpawnEnemy(Godot.Collections.Dictionary config, StringName strategy)
    {
        var e = _enemyScene.Instantiate<Enemy>(); // M3b：Enemy 迁 C#，typed 实例化
        e.Setup(config, strategy, 1.0f);
        e.CanShoot = _stage == 2; // 仅战斗阶段敌机开火
        var view = GameState.Instance.ViewWorldRect(); // G014：视口基线（去 960 硬编码）
        e.Position = new Vector2(view.GetCenter().X, view.Position.Y - 60.0f);
        e.Died += OnEnemyDied; // M3b：Enemy 迁 C#，[Signal] 以 PascalCase 注册
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
            if (_stageKills >= 3)
            {
                PassStage();
            }
        }
        else if (_stage == 2)
        {
            SetObjectiveTr("TUT_S3_OBJ", new Godot.Collections.Array { _stageKills });
            if (_stageKills >= 5)
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
        var gate = new WarpGate(); // M6：WarpGate 迁 C#，typed 实例化
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
        PlaySfxBuffPick();
        // 一次性 Timer 节点 + 信号回调（AGENTS：禁止 await create_timer 协程，退出时协程状态泄漏）
        var timer = new Godot.Timer();
        timer.OneShot = true;
        timer.ProcessMode = Node.ProcessModeEnum.Always; // 对齐原 SceneTreeTimer 暂停树仍计时
        timer.WaitTime = 1.0;
        timer.Timeout += FinishPassStage;
        AddChild(timer);
        timer.Start();
    }

    /// <summary>C01 修复：_pass_stage 的延迟推进改为 Timer 回调（原 await create_timer 在教程被释放时协程悬死）</summary>
    private void FinishPassStage()
    {
        // H20（健壮性审核）：失败/结束态防阶段推进（失败态下已挂起的推进 Timer 仍会触发）
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
                    // 注意：保持每帧检查（tutorial_test 依赖 queue_free 释放与检查窗口的即时性，
                    // 2026-08-03 曾尝试 0.25s 节流被测试证伪——释放帧与节流窗口交错会跳过补刷）
                    if (!_advancing && _stageKills < 5 && AliveEnemyCount() == 0)
                    {
                        SpawnCombatWave(5 - _stageKills);
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
                                _objectivePoll = 0.1f; // G015：百分比文本 0.1s 节流
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
                            _objectivePoll = 0.1f; // G015：百分比文本 0.1s 节流
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

        _baseUi = new BaseConsole(); // M5：BaseConsole 已迁 C#，typed 实例化（原 set_script 不再需要）
        _baseUi.ProcessMode = Node.ProcessModeEnum.Always;
        AddChild(_baseUi);
        _baseUi.ResumeRequested += OnBaseResume;
        _baseUi.ShowBase();
        GetTree().Paused = true;
        // 打开即过关：1s 后自动关闭进入下一阶段（玩家点继续出击同样推进）
        PassStage();
        // 一次性 Timer 节点 + 信号回调（AGENTS：禁止 await create_timer 协程）
        var timer = new Godot.Timer();
        timer.OneShot = true;
        timer.ProcessMode = Node.ProcessModeEnum.Always; // 树暂停（基地 UI）中仍需计时
        timer.WaitTime = 1.2;
        timer.Timeout += CloseBase;
        AddChild(timer);
        timer.Start();
    }

    private void CloseBase()
    {
        if (_baseUi == null)
        {
            return;
        }

        GetTree().Paused = false;
        _baseUi.QueueFree();
        _baseUi = null;
    }

    private void OnBaseResume() => CloseBase();

    private void Finish()
    {
        _finished = true;
        GameState.Instance.TutorialDone = true;
        GameState.Instance.SaveProfile();
        PlaySfxBuffPick();
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
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.10f, 0.13f, 0.22f, 0.95f);
        style.BorderColor = new Color(0.3f, 0.8f, 0.9f);
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(8);
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
        Engine.TimeScale = 1.0f; // 防御性复位
        GetTree().Paused = false;
        GameState.Instance.ResetRun(); // 不污染正常对局
        GetTree().ChangeSceneToFile("res://scenes/welcome.tscn");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // 教程中按 Esc 直接退出回开始面板（无暂停菜单）
        if (@event.IsActionPressed("ui_cancel"))
        {
            ExitTutorial();
        }
    }

    private void PlaySfxBuffPick() => GameState.Instance.PlaySfx(_sfxBuffPick);

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

    /// <summary>GDScript 字符串 % 格式化语义（%s/%d/%f 占位 + %% 转义；tr() 文案补参用，
    // ---------------- snake_case 兼容桥（M7 后保留：仍有 C# 动态派发/测试调用方；新代码直接调 PascalCase 主方法） ----------------

    public Boss? boss() => Boss();

    public Mothership? mothership() => Mothership();

    /// <summary>tutorial_test.gd:120 以 SCREAMING_SNAKE 直读蓄力时长（tut.DOCK_CHARGE_TIME）。</summary>
    public float DOCK_CHARGE_TIME => DockChargeTime;
}
