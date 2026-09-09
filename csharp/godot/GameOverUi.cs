using Godot;
using InfiAir.Core;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 死亡结算面板：击杀统计 + 左缘轮盘菜单
/// （重新出击/返回标题/退出；2026-09-08 圆盘 UI 全覆盖，R 快捷键保留）。
/// </summary>
public partial class GameOverUi : RadialMenuLayer
{
    private Label _statsLabel = null!;
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

        _statsLabel = UITheme.MakeLabel("", UITheme.FontBody, UITheme.Text);
        _content.AddChild(_statsLabel);

        var gs = GameState.Instance;
        if (gs != null)
        {
            // 2026-08-10 健壮性审查：C22 IsConnected 守卫（对齐 PauseUi/Hud）——未走
            // _ExitTree 的重入树路径会重复订阅，结算回调双跑（SettleRun 双执行）
            if (!gs.IsConnected(GameState.SignalName.PlayerDied, _onPlayerDied))
            {
                gs.Connect(GameState.SignalName.PlayerDied, _onPlayerDied);
            }

            if (!gs.IsConnected(GameState.SignalName.LocaleChanged, _onLocaleChanged))
            {
                gs.Connect(GameState.SignalName.LocaleChanged, _onLocaleChanged);
            }
        }
    }

    public override void _ExitTree()
    {
        // C22：显式断开 GameState 信号连接（C# Connect 连接不随接收方释放自动断开）
        var gs = GameState.Instance;
        if (gs != null)
        {
            if (gs.IsConnected(GameState.SignalName.PlayerDied, _onPlayerDied))
            {
                gs.Disconnect(GameState.SignalName.PlayerDied, _onPlayerDied);
            }

            if (gs.IsConnected(GameState.SignalName.LocaleChanged, _onLocaleChanged))
            {
                gs.Disconnect(GameState.SignalName.LocaleChanged, _onLocaleChanged);
            }
        }
    }

    private void OnLocaleChanged()
    {
        _titleLabel.Text = Tr("GO_TITLE");
        RefreshStats();
    }

    private void RefreshStats()
    {
        _statsLabel.Text = GdFormat.Format(
            Tr("GO_KILLS") + "\n" + Tr("GO_BOSS_KILLS"),
            GameState.Instance.Kills,
            GameState.Instance.BossKills);
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
                // 结算后无进度可留：ResetRun 后回标题屏（title.tscn），任意键重新开局
                GetTree().Paused = false;
                GameState.Instance.ResetRun();
                GetTree().ChangeSceneToFile("res://scenes/title.tscn");
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
        GetTree().Paused = true;
        Visible = true;
        SetWheelActive(true);
        BuildMenu();
        PlayWheelEntrance();
        UITheme.AnimateModalOpen(_dim, _plate, _content);
    }

    /// <summary>重新出击（轮盘/R 快捷键共用）：结算完成后重开同一场景。</summary>
    private void Restart()
    {
        GetTree().Paused = false;
        GameState.Instance.ResetRun();
        GetTree().ReloadCurrentScene();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Visible && @event.IsActionPressed("restart"))
        {
            Restart();
        }
    }
}
