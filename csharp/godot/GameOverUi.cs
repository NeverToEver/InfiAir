using Godot;
using InfiAir.Core;
using InfiAir.Core.Progression;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 死亡结算面板：击杀统计 + 左缘轮盘菜单
/// （重新出击/返回标题/退出；R 快捷键保留）。
/// </summary>
public partial class GameOverUi : RadialMenuLayer
{
    // 统计两行错峰淡入 + 面板轻微强调（缩放属运动脉冲，受 ReduceFlash 约束）
    private const float StatsPunchAmount = 1.02f;
    private const float StatsPunchTime = 0.2f;

    private Label _killsLabel = null!;
    private Label _bossKillsLabel = null!;
    private Label _goalLabel = null!;
    private Label _bestLabel = null!;
    private VBoxContainer _statsBox = null!;
    private Label _titleLabel = null!;
    private ChamferedPanel _plate = null!;
    private ColorRect _dim = null!;
    private VBoxContainer _content = null!;

    /// <summary>练习设置面板（轮盘「练习模式」就地打开；打开期间 R 重开键屏蔽，见 _UnhandledInput）。</summary>
    private PracticePanel? _practicePanel;

    private readonly Callable _onPlayerDied;
    private readonly Callable _onLocaleChanged;

    public GameOverUi()
    {
        _onPlayerDied = Callable.From(OnPlayerDied);
        _onLocaleChanged = Callable.From(OnLocaleChanged);
    }

    public override void _Ready()
    {
        Visible = false;
        BuildChrome();
        SetContentAnchor(() => _plate);
        BuildMenu();
        Wheel.Confirmed += OnWheelConfirmed;
        var shell = UITheme.MakePageShell("GO_TITLE");
        AddChild((Node)shell["root"].AsGodotObject());
        RaiseWheel(); // shell 自带全屏遮罩：轮盘必须保持在遮罩之上，否则卡片被压暗
        _dim = (ColorRect)shell["dim"].AsGodotObject();
        _plate = (ChamferedPanel)shell["panel"].AsGodotObject();
        _plate.CustomMinimumSize = new Vector2(640.0f, 380.0f);
        _titleLabel = (Label)shell["title"].AsGodotObject();
        _content = (VBoxContainer)shell["content"].AsGodotObject();

        _statsBox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _statsBox.AddThemeConstantOverride("separation", 8);
        _content.AddChild(_statsBox);
        // 两行统计分列：错峰淡入作用在行上，整体格式与合并成一行的旧观感一致
        _killsLabel = UITheme.MakeLabel("", UITheme.FontBody, UITheme.Text);
        _statsBox.AddChild(_killsLabel);
        _bossKillsLabel = UITheme.MakeLabel("", UITheme.FontBody, UITheme.Text);
        _statsBox.AddChild(_bossKillsLabel);
        // 本局目标结果（达成/未达成）：必死曲线必须有「打到哪算赢」的落点，
        // 结算页是玩家复盘时唯一会细看的地方（Brotato 的「打过 wave20 算胜」同款锚点）。
        _goalLabel = UITheme.MakeLabel("", UITheme.FontBody, UITheme.AccentGold);
        _statsBox.AddChild(_goalLabel);
        // 跨局最好成绩：必死曲线上「比上次打得久吗」只在这里有读数（计分显示已裁，
        // 本记录不含分数——口径见 DESIGN_BASELINE §1.16）
        _bestLabel = UITheme.MakeLabel("", UITheme.FontBody, UITheme.TextDim);
        _statsBox.AddChild(_bestLabel);

        var gs = GameState.Instance;
        // IsConnected 守卫：未走 _ExitTree 的重入树路径会重复订阅，
        // 结算回调双跑（SettleRun 双执行）
        if (!gs.IsConnected(GameState.SignalName.PlayerDied, _onPlayerDied))
        {
            gs.Connect(GameState.SignalName.PlayerDied, _onPlayerDied);
        }

        if (!gs.IsConnected(GameState.SignalName.LocaleChanged, _onLocaleChanged))
        {
            gs.Connect(GameState.SignalName.LocaleChanged, _onLocaleChanged);
        }
    }

    public override void _ExitTree()
    {
        // 显式断开 GameState 信号连接（C# Connect 连接不随接收方释放自动断开）
        // autoload 可能先于本节点释放（非常规拆树序），Instance getter 会抛异常，故安全取值
        var gs = GameState.TryGetInstance();
        if (gs == null)
        {
            return;
        }

        if (gs.IsConnected(GameState.SignalName.PlayerDied, _onPlayerDied))
        {
            gs.Disconnect(GameState.SignalName.PlayerDied, _onPlayerDied);
        }

        if (gs.IsConnected(GameState.SignalName.LocaleChanged, _onLocaleChanged))
        {
            gs.Disconnect(GameState.SignalName.LocaleChanged, _onLocaleChanged);
        }
    }

    private void OnLocaleChanged()
    {
        _titleLabel.Text = Tr("GO_TITLE");
        RefreshStats();
    }

    private void RefreshStats()
    {
        _killsLabel.Text = GdFormat.Format(Tr("GO_KILLS"), GameState.Instance.Kills);
        _bossKillsLabel.Text = GdFormat.Format(Tr("GO_BOSS_KILLS"), GameState.Instance.BossKills);
        var achieved = GameState.Instance.GoalAchieved();
        _goalLabel.Text = GdFormat.Format(Tr("GO_GOAL_RESULT"),
            achieved ? Tr("GO_BOSS_ACHIEVED") : Tr("GO_BOSS_PENDING"));
        _goalLabel.AddThemeColorOverride("font_color", achieved ? UITheme.AccentGold : UITheme.TextDim);
        RefreshBestLine();
    }

    /// <summary>历史最好成绩一行：本局刷新时打「新纪录」，否则读上次最好；盘上记录读不出时
    /// 留空（显示「暂无记录」等于把「读不出」说成「没打过」）。</summary>
    private void RefreshBestLine()
    {
        var gs = GameState.Instance;
        if (!gs.BestKnown)
        {
            _bestLabel.Text = "";
            return;
        }

        var improved = gs.BestImprovedThisRun;
        _bestLabel.Text = gs.Best == BestRecord.Empty
            ? Tr("BEST_NONE")
            : GdFormat.Format(Tr(improved ? "BEST_NEW" : "BEST_LINE"), BestRecord.FormatArgs(gs.Best));
        _bestLabel.AddThemeColorOverride("font_color", improved ? UITheme.AccentGold : UITheme.TextDim);
    }

    /// <summary>装配结算菜单（打开时重装，复位轮盘导航态）。</summary>
    private void BuildMenu()
    {
        LoadMenu(
            new List<RadialWheelOption>
            {
                new() { Id = "restart", Label = Tr("GO_MENU_RESTART"), Glyph = RadialGlyph.Bolt },
                new() { Id = "practice", Label = Tr("GO_MENU_PRACTICE"), Glyph = RadialGlyph.Cross },
                new() { Id = "home", Label = Tr("GO_MENU_HOME"), Glyph = RadialGlyph.Ring },
                new() { Id = "quit", Label = Tr("GO_MENU_QUIT"), Glyph = RadialGlyph.Star },
            },
            string.Empty);
        Wheel.FocusOption(0); // 开页聚焦「重新出击」：默认弧面中点槽会停在「返回标题」
    }

    private void OnWheelConfirmed(RadialWheelOption option)
    {
        switch (option.Id)
        {
            case "restart":
                Restart();
                break;
            case "practice":
                OpenPracticePanel();
                break;
            case "home":
                // 结算后无进度可留：回标题屏（title.tscn），任意键重新开局
                GameState.Instance.ExitToTitle();
                break;
            case "quit":
                // 退出清理与 ExitConfirm 同口（设置落盘 + 停音效）：直接 Quit 会漏掉未播完的
                // 音效实例，退出期资源统计里表现为泄漏；本页无二次确认与淡出，故不走其演出层
                GameState.Instance.ExecuteExitCleanup();
                GetTree().Quit();
                break;
        }
    }

    /// <summary>练习入口（口径见 DESIGN_BASELINE §1.16）：就地开练习设置面板——死亡页是玩家
    /// 最想「换一场再练」的位置，跳到标题屏再开一次会多一次场景切换。
    /// 面板未退场前轮盘断供输入（否则同一次方向键既切面板选项又挪轮盘聚焦）；
    /// 面板退场（取消/开始）后还回来——开始练习会切场景，还回来也无妨（同帧内场景即被替换）。</summary>
    private void OpenPracticePanel()
    {
        if (_practicePanel != null)
        {
            return;
        }

        SetWheelActive(false);
        var panel = new PracticePanel();
        _practicePanel = panel;
        panel.StartRequested += setup => GameState.Instance.EnterPractice(setup);
        panel.Closed += () =>
        {
            _practicePanel = null;
            SetWheelActive(true);
        };
        AddChild(panel);
    }

    private void OnPlayerDied()
    {
        // 无分数记录/局外结算：死亡只呈现击杀统计（PlayerDied 订阅者角色不变）
        RefreshStats();
        GameState.Instance.SetTreePaused(true);
        Visible = true;
        SetWheelActive(true);
        BuildMenu();
        PlayWheelEntrance();
        UITheme.AnimateModalOpen(_dim, _plate);
        UITheme.StaggerOpen(_statsBox); // 两行统计错峰淡入
        if (!GameState.Instance.ReduceFlash)
        {
            UITheme.PunchScale(_plate, StatsPunchAmount, StatsPunchTime);
        }
    }

    /// <summary>重新出击（轮盘/R 快捷键共用）：结算完成后重开同一场景。</summary>
    private void Restart()
    {
        GameState.Instance.RestartRun();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // 练习面板打开期间屏蔽 R 重开：面板不消费 restart 动作（只收 Esc/左右方向键），
        // 不屏蔽的话在面板上按 R 会把本局重开掉、面板连同场景一起消失。
        if (Visible && _practicePanel == null && @event.IsActionPressed("restart"))
        {
            Restart();
        }
    }
}
