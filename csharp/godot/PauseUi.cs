using Godot;
using InfiAir.Core;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// Esc 暂停页（2026-09-08 圆盘 UI 全覆盖）：左缘轮盘菜单（继续/保存/设置/重开/退出）
/// + 右区聚焦项说明卡。ui_cancel（Esc/手柄 B）的全局返回路由统一在 BackNavigator，
/// 本页只提供 open()/close() 供其调用；「退出游戏」走 ExitConfirm 战斗模式二次确认。
/// 轮盘方向键/Enter 导航（页面无焦点控件，全时接管）。
/// （process_mode=Always/layer=15 仍在 scenes/main.tscn 设置。）
/// </summary>
public partial class PauseUi : RadialMenuLayer
{
    private Label _titleLabel = null!;
    private Label _hintTitle = null!;
    private Label _hintBody = null!;
    private ChamferedPanel _hintPlate = null!;
    private SettingsUi? _settingsUi; // 惰性绑定（SettingsUI 的 _ready 晚于本节点）

    private readonly Callable _onLocaleChanged;

    public PauseUi()
    {
        _onLocaleChanged = Callable.From(OnLocaleChanged);
    }

    public override void _Ready()
    {
        Visible = false;
        // C22：is_connected 守卫，场景重载（reload_current_scene）后重进树不重复连接
        var gs = GameState.Instance;
        if (!gs.IsConnected(GameState.SignalName.LocaleChanged, _onLocaleChanged))
        {
            gs.Connect(GameState.SignalName.LocaleChanged, _onLocaleChanged);
        }

        BuildChrome();
        BuildRightArea();
        SetContentAnchor(() => _hintPlate);
        Wheel.Confirmed += OnWheelConfirmed;
        Wheel.Drilled += _ => RefreshHint();
        Wheel.Backed += RefreshHint;
        Wheel.FocusChanged += _ => RefreshHint(); // 说明卡实时跟随聚焦项（方向键/滚轮移动即刷新）
        // 退出确认取消 → 恢复本页（OnQuitPressed 弹确认窗前隐藏了本页并停用轮盘；
        // 不恢复则树保持暂停且无任何可见/可操作 UI——软锁）
        var exitConfirm = GetParent().GetNodeOrNull("ExitConfirm") as ExitConfirm;
        if (exitConfirm != null)
        {
            // C# 事件订阅：两者同树同生命周期，随对方消亡，无需退订（C22 仅针对 Connect）
            exitConfirm.Canceled += OnExitCanceled;
        }
    }

    public override void _ExitTree()
    {
        var gs = GameState.Instance;
        if (gs.IsConnected(GameState.SignalName.LocaleChanged, _onLocaleChanged))
        {
            gs.Disconnect(GameState.SignalName.LocaleChanged, _onLocaleChanged);
        }
    }

    private void BuildRightArea()
    {
        _titleLabel = UITheme.MakeLabel(Tr("PAUSE_TITLE"), UITheme.FontTitle, UITheme.Accent);
        _titleLabel.Position = new Vector2(560f, 46f);
        AddChild(_titleLabel);
        var titleLine = new ColorRect { Color = UITheme.Accent, CustomMinimumSize = new Vector2(96f, 3f), Position = new Vector2(562f, 104f) };
        titleLine.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(titleLine);

        // 聚焦项说明卡（右区光学居中）：随轮盘聚焦项联动刷新
        _hintPlate = new ChamferedPanel
        {
            Position = new Vector2(700f, 330f),
            Size = new Vector2(620f, 260f),
            Brackets = true,
        };
        _hintPlate.Resized += () => _hintPlate.PivotOffset = _hintPlate.Size / 2f;
        AddChild(_hintPlate);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_right", 28);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        _hintPlate.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        margin.AddChild(vbox);

        _hintTitle = UITheme.MakeLabel("", UITheme.FontHeader, UITheme.AccentGold, HorizontalAlignment.Left);
        vbox.AddChild(_hintTitle);
        _hintBody = UITheme.MakeLabel("", UITheme.FontBody, UITheme.TextDim, HorizontalAlignment.Left);
        _hintBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hintBody.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddChild(_hintBody);
    }

    /// <summary>装配根级选项（每次打开重装，复位轮盘导航态）。</summary>
    private void RebuildMenu()
    {
        LoadMenu(
            new List<RadialWheelOption>
            {
                new() { Id = "resume", Label = Tr("PAUSE_RESUME"), Glyph = RadialGlyph.Triangle },
                new() { Id = "settings", Label = Tr("PAUSE_SETTINGS"), Glyph = RadialGlyph.Cross },
                new() { Id = "restart", Label = Tr("GO_MENU_RESTART"), Glyph = RadialGlyph.Bolt },
                new() { Id = "quit", Label = Tr("PAUSE_QUIT"), Glyph = RadialGlyph.Star },
            },
            string.Empty);
        Wheel.FocusOption(0); // 开页聚焦「继续」：默认弧面中点槽会停在「重新出击」（误 Enter 重开对局）
        RefreshHint();
    }

    private void OnWheelConfirmed(RadialWheelOption option)
    {
        switch (option.Id)
        {
            case "resume":
                Close();
                break;
            case "settings":
                OnSettingsPressed();
                break;
            case "restart":
                RestartRun();
                break;
            case "quit":
                OnQuitPressed();
                break;
        }
    }

    /// <summary>右区说明卡随轮盘聚焦项联动。</summary>
    private void RefreshHint()
    {
        var focused = Wheel.FocusedOption;
        if (focused == null)
        {
            _hintPlate.Visible = false;
            return;
        }

        _hintPlate.Visible = true;
        _hintTitle.Text = focused.Label;
        _hintBody.Text = Tr("MENU_HINT_" + focused.Id.ToUpperInvariant());
    }

    private void OnLocaleChanged()
    {
        _titleLabel.Text = Tr("PAUSE_TITLE");
        RebuildMenu();
    }

    public void Open()
    {
        GameState.Instance.SetTreePaused(true);
        Visible = true;
        SetWheelActive(true);
        RebuildMenu();
        PlayWheelEntrance();
    }

    public void Close()
    {
        Visible = false;
        SetWheelActive(false);
        GameState.Instance.SetTreePaused(false);
    }

    public void Toggle()
    {
        if (Visible)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    /// <summary>设置页返回/退出确认取消时的恢复入口：恢复可见 + 轮盘活性
    /// （OnSettingsPressed/OnQuitPressed 离开前均做了 Visible=false + SetWheelActive(false)，
    /// 只还 Visible 不还轮盘会让轮盘菜单键盘/鼠标全死）。</summary>
    public void GrabPrimaryFocus()
    {
        Visible = true;
        SetWheelActive(true);
    }

    private void OnExitCanceled() => GrabPrimaryFocus();

    private SettingsUi? GetSettingsUi()
    {
        if (_settingsUi == null)
        {
            _settingsUi = GetTree().GetFirstNodeInGroup("settings_ui") as SettingsUi;
        }
        return _settingsUi;
    }

    public void Restart() => RestartRun();

    private void OnSettingsPressed()
    {
        if (GetSettingsUi() == null)
        {
            return;
        }
        Visible = false;
        SetWheelActive(false);
        _settingsUi!.ShowSettings(this);
    }

    private void OnQuitPressed()
    {
        // 战斗中退出：ExitConfirm 战斗模式二次确认（带进度损失警告）
        // C17：GetNodeOrNull + 判空——宿主缺 ExitConfirm 节点时不崩溃（防御性，正常 main.tscn 必有）
        var exitConfirm = GetParent().GetNodeOrNull("ExitConfirm") as ExitConfirm;
        if (exitConfirm != null)
        {
            Visible = false;
            SetWheelActive(false);
            exitConfirm.ShowConfirm(true);
        }
    }

    /// <summary>R 重开主入口（轮盘「重新出击」与 _UnhandledInput 的 restart 动作共用）。</summary>
    private void RestartRun()
    {
        // AB13：确认退出淡出窗口内忽略 R——ReloadCurrentScene 会杀淡出 tween 使 Quit 永不执行
        // （未退出、静默重开新局的静默丢退出路径）
        var exitConfirm = GetParent().GetNodeOrNull("ExitConfirm") as ExitConfirm;
        if (exitConfirm != null && exitConfirm.Exiting())
        {
            return;
        }

        GameState.Instance.SetTreePaused(false);
        // R 重开=弃局重开（ReloadCurrentScene 重建 main → 全新一局）
        GameState.Instance.ResetRun();
        GetTree().ReloadCurrentScene();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // ui_cancel（Esc/手柄 B/Android 返回）的全局路由已移交 BackNavigator；
        // 此处只保留暂停中的 R 重开（轮盘 KeyboardEnabled 只接管方向键/Enter）
        if (!Visible || !@event.IsActionPressed("restart"))
        {
            return;
        }

        RestartRun();
    }
}
