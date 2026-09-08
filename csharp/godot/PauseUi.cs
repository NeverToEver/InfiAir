using Godot;
using InfiAir.Core;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// Esc 暂停页（2026-09-08 圆盘 UI 全覆盖）：左缘轮盘菜单（继续/保存/设置/重开/退出）
/// + 右区聚焦项说明卡。ui_cancel（Esc/手柄 B）的全局返回路由统一在 BackNavigator，
/// 本页只提供 open()/close() 供其调用；「退出游戏」走 ExitConfirm 战斗模式二次确认。
/// 「保存进度」仍是全局唯一主动存档入口。轮盘方向键/Enter 导航（页面无焦点控件，全时接管）。
/// （process_mode=Always/layer=15 仍在 scenes/main.tscn 设置。）
/// </summary>
public partial class PauseUi : RadialMenuLayer
{
    private Label _titleLabel = null!;
    private Label _hintTitle = null!;
    private Label _hintBody = null!;
    private Label _saveStateLabel = null!;
    private ChamferedPanel _hintPlate = null!;
    private SettingsUi? _settingsUi; // 惰性绑定（SettingsUI 的 _ready 晚于本节点）
    private bool _saved; // 保存态标志（跨语言文本比较判保存态会误判）
    private Godot.Timer? _saveTimer; // 缓存单实例：连按时 Start 重启计时，避免旧 Timer 提前打回本次文案/状态

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
        if (gs != null && !gs.IsConnected("LocaleChanged", _onLocaleChanged))
        {
            gs.Connect("LocaleChanged", _onLocaleChanged);
        }

        BuildChrome();
        BuildRightArea();
        Wheel.Confirmed += OnWheelConfirmed;
        Wheel.Drilled += _ => RefreshHint();
        Wheel.Backed += RefreshHint;
    }

    public override void _ExitTree()
    {
        var gs = GameState.Instance;
        if (gs != null && gs.IsConnected("LocaleChanged", _onLocaleChanged))
        {
            gs.Disconnect("LocaleChanged", _onLocaleChanged);
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
        _saveStateLabel = UITheme.MakeLabel("", UITheme.FontCaption, UITheme.Success, HorizontalAlignment.Left);
        vbox.AddChild(_saveStateLabel);
    }

    /// <summary>装配根级选项（每次打开重装，复位轮盘导航态）。</summary>
    private void RebuildMenu()
    {
        LoadMenu(
            new List<RadialWheelOption>
            {
                new() { Id = "resume", Label = Tr("PAUSE_RESUME"), Glyph = RadialGlyph.Triangle },
                new() { Id = "save", Label = Tr("PAUSE_SAVE"), Glyph = RadialGlyph.Ring },
                new() { Id = "settings", Label = Tr("PAUSE_SETTINGS"), Glyph = RadialGlyph.Cross },
                new() { Id = "restart", Label = Tr("GO_MENU_RESTART"), Glyph = RadialGlyph.Bolt },
                new() { Id = "quit", Label = Tr("PAUSE_QUIT"), Glyph = RadialGlyph.Star },
            },
            string.Empty);
        RefreshHint();
    }

    private void OnWheelConfirmed(RadialWheelOption option)
    {
        switch (option.Id)
        {
            case "resume":
                Close();
                break;
            case "save":
                OnSavePressed();
                RefreshHint();
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
        _saveStateLabel.Text = focused.Id == "save" && _saved ? Tr("PAUSE_SAVED") : string.Empty;
    }

    private void OnLocaleChanged()
    {
        _titleLabel.Text = Tr("PAUSE_TITLE");
        RebuildMenu();
    }

    public void Open()
    {
        _saved = false;
        GetTree().Paused = true;
        Visible = true;
        SetWheelActive(true);
        RebuildMenu();
        PlayWheelEntrance();
    }

    public void Close()
    {
        Visible = false;
        SetWheelActive(false);
        GetTree().Paused = false;
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

    /// <summary>主按钮重获焦点（设置页返回时由 SettingsUI 调用，与开始面板 grab_primary_focus 同约定）。
    /// 圆盘版无焦点控件：保留入口为兼容 SettingsUI 的 typed 回派发。</summary>
    public void GrabPrimaryFocus()
    {
    }

    private SettingsUi? GetSettingsUi()
    {
        if (_settingsUi == null)
        {
            _settingsUi = GetTree().GetFirstNodeInGroup("settings_ui") as SettingsUi;
        }
        return _settingsUi;
    }

    /// <summary>A7：测试/诊断经公开接口（动作包装）</summary>
    public void OpenSettings() => OnSettingsPressed();

    public void Save() => OnSavePressed();

    public void Quit() => OnQuitPressed();

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

    private void OnSavePressed()
    {
        GameState.Instance.SaveRun(); // Y 系列：编排下沉（内部取 Fuel/Elapsed，缺节点兜底 100/0）
        _saved = true;
        // 用信号连接而非协程：退出时挂起的协程函数状态会泄漏
        // 缓存单个 Timer——原实现每次按下新建，1s 内连按时旧 Timer 会提前打回文案/状态
        if (_saveTimer == null)
        {
            _saveTimer = new Godot.Timer { OneShot = true };
            AddChild(_saveTimer); // 本节点 process_mode=Always，暂停中仍计时
            _saveTimer.Connect(Godot.Timer.SignalName.Timeout, Callable.From(ResetSaveLabel));
        }

        _saveTimer.Start(1.0); // 重复保存重启计时
        RefreshHint();
    }

    private void ResetSaveLabel()
    {
        _saved = false;
        RefreshHint();
    }

    private void OnQuitPressed()
    {
        // 战斗中退出：ExitConfirm 战斗模式二次确认（带进度损失警告）
        // C17：get_node_or_null + 判空，测试场景缺该节点不崩溃
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
        // AB13：确认退出淡出窗口内忽略 R——删档后 ReloadCurrentScene 会杀淡出 tween 使 Quit 永不执行
        // （档删、未退出、静默重开新局的静默数据丢失路径）
        var exitConfirm = GetParent().GetNodeOrNull("ExitConfirm") as ExitConfirm;
        if (exitConfirm != null && exitConfirm.Exiting())
        {
            return;
        }

        GetTree().Paused = false;
        // R 重开=弃局（对齐 ExitConfirm 战斗退出语义：删档、不结算 TechPoints）。
        // 不删档则 main._Ready 的 HasSave() 自动续局，重开退化为回滚到返航检查点。
        GameState.Instance.DeleteSave();
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
