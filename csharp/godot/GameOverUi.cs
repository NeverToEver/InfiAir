using Godot;
using InfiAir.Core;
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
    private VBoxContainer _statsBox = null!;
    private Label _titleLabel = null!;
    private ChamferedPanel _plate = null!;
    private ColorRect _dim = null!;
    private VBoxContainer _content = null!;

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
        var gs = GameState.Instance;
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
    }

    /// <summary>装配结算菜单（打开时重装，复位轮盘导航态）。</summary>
    private void BuildMenu()
    {
        LoadMenu(
            new List<RadialWheelOption>
            {
                new() { Id = "restart", Label = Tr("GO_MENU_RESTART"), Glyph = RadialGlyph.Bolt },
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
            case "home":
                // 结算后无进度可留：回标题屏（title.tscn），任意键重新开局
                GameState.Instance.ExitToTitle();
                break;
            case "quit":
                GameState.Instance.SaveSettings();
                GetTree().Quit();
                break;
        }
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
        if (Visible && @event.IsActionPressed("restart"))
        {
            Restart();
        }
    }
}
