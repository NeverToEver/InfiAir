using Godot;

namespace InfiAir;

/// <summary>
/// Esc 暂停面板：继续 / 保存进度 / 设置 / 退出游戏 / 重开提示。
/// 「保存进度」是全局唯一主动存档入口；「设置」打开 Ctrl/Shift 模式面板。
/// ui_cancel（Esc/手柄 B）的全局返回路由统一在 BackNavigator（见 docs/EXIT_FLOW.md），
/// 本面板只提供 open()/close() 供其调用；「退出游戏」走 ExitConfirm 战斗模式二次确认。
/// M5 全量迁移（2026-08-08 自 scripts/pause_ui.gd）：UITheme/ChamferedPanel typed 直调；
/// （process_mode=Always/layer=15）仍在 scenes/main.tscn 设置。
/// </summary>
public partial class PauseUi : CanvasLayer
{
    private Button _resumeButton = null!;
    private Button _saveButton = null!;
    private Button _settingsButton = null!;
    private Button _quitButton = null!;
    private Label _titleLabel = null!;
    private Label _hintLabel = null!;
    private ChamferedPanel _plate = null!;
    private VBoxContainer _content = null!;
    private SettingsUi? _settingsUi; // 惰性绑定（SettingsUI 的 _ready 晚于本节点）
    private ColorRect _dim = null!;
    private bool _saved; // 保存态标志（2026-08-03 审计：跨语言文本比较判保存态会误判）
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

        var shell = UITheme.MakePageShell("PAUSE_TITLE");
        AddChild((Node)shell["root"].AsGodotObject());
        _dim = (ColorRect)shell["dim"].AsGodotObject();
        _plate = (ChamferedPanel)shell["panel"].AsGodotObject();
        _plate.CustomMinimumSize = new Vector2(560.0f, 480.0f);
        _titleLabel = (Label)shell["title"].AsGodotObject();
        _content = (VBoxContainer)shell["content"].AsGodotObject();

        _resumeButton = UITheme.MakeButton(Tr("PAUSE_RESUME"), true);
        _resumeButton.CustomMinimumSize = new Vector2(360.0f, 56.0f);
        _resumeButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _resumeButton.Pressed += Close;
        _content.AddChild(_resumeButton);

        _saveButton = UITheme.MakeButton(Tr("PAUSE_SAVE"));
        _saveButton.CustomMinimumSize = new Vector2(360.0f, 52.0f);
        _saveButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _saveButton.Pressed += OnSavePressed;
        _content.AddChild(_saveButton);

        _settingsButton = UITheme.MakeButton(Tr("PAUSE_SETTINGS"));
        _settingsButton.CustomMinimumSize = new Vector2(360.0f, 52.0f);
        _settingsButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _settingsButton.Pressed += OnSettingsPressed;
        _content.AddChild(_settingsButton);

        _quitButton = UITheme.MakeButton(Tr("PAUSE_QUIT"));
        _quitButton.CustomMinimumSize = new Vector2(360.0f, 52.0f);
        _quitButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _quitButton.Pressed += OnQuitPressed;
        _content.AddChild(_quitButton);

        _hintLabel = UITheme.MakeLabel(Tr("PAUSE_HINT"), UITheme.FontCaption, UITheme.TextDim);
        _content.AddChild(_hintLabel);
    }

    public override void _ExitTree()
    {
        var gs = GameState.Instance;
        if (gs != null && gs.IsConnected("LocaleChanged", _onLocaleChanged))
        {
            gs.Disconnect("LocaleChanged", _onLocaleChanged);
        }
    }

    private void OnLocaleChanged()
    {
        _titleLabel.Text = Tr("PAUSE_TITLE");
        _resumeButton.Text = Tr("PAUSE_RESUME");
        _hintLabel.Text = Tr("PAUSE_HINT");
        _settingsButton.Text = Tr("PAUSE_SETTINGS");
        _quitButton.Text = Tr("PAUSE_QUIT");
        // 2026-08-03 审计：按保存态标志选文案（跨语言文本比较在切换语言后会误判为未保存）
        _saveButton.Text = _saved ? Tr("PAUSE_SAVED") : Tr("PAUSE_SAVE");
    }

    public void Open()
    {
        _saved = false;
        _saveButton.Text = Tr("PAUSE_SAVE");
        GetTree().Paused = true;
        Visible = true;
        UITheme.AnimateModalOpen(_dim, _plate, _content);
        _resumeButton.GrabFocus();
    }

    public void Close()
    {
        Visible = false;
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

    /// <summary>主按钮重获焦点（设置页返回时由 SettingsUI 调用，与开始面板 grab_primary_focus 同约定）</summary>
    public void GrabPrimaryFocus()
    {
        _resumeButton.GrabFocus();
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

    private void OnSettingsPressed()
    {
        if (GetSettingsUi() == null)
        {
            return;
        }
        Visible = false;
        _settingsUi!.ShowSettings(this);
    }

    private void OnSavePressed()
    {
        GameState.Instance.SaveRun(); // 2026-08-09 Y 系列：编排下沉（内部取 Fuel/Elapsed，缺节点兜底 100/0）
        _saved = true;
        _saveButton.Text = Tr("PAUSE_SAVED");
        // 用信号连接而非协程：退出时挂起的协程函数状态会泄漏
        // 2026-08-10 健壮性审查：缓存单个 Timer——原实现每次按下新建，1s 内连按时
        // 第一个 Timer 的 ResetSaveLabel 会把第二次保存的文案/状态提前打回
        if (_saveTimer == null)
        {
            _saveTimer = new Godot.Timer { OneShot = true };
            AddChild(_saveTimer); // 本节点 process_mode=Always，暂停中仍计时
            _saveTimer.Connect(Godot.Timer.SignalName.Timeout, Callable.From(ResetSaveLabel));
        }

        _saveTimer.Start(1.0); // 重复保存重启计时
    }

    private void ResetSaveLabel()
    {
        _saved = false;
        _saveButton.Text = Tr("PAUSE_SAVE");
    }

    private void OnQuitPressed()
    {
        // 战斗中退出：ExitConfirm 战斗模式二次确认（带进度损失警告）
        // C17：get_node_or_null + 判空，测试场景缺该节点不崩溃
        var exitConfirm = GetParent().GetNodeOrNull("ExitConfirm") as ExitConfirm;
        if (exitConfirm != null)
        {
            exitConfirm.ShowConfirm(true);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // ui_cancel（Esc/手柄 B/Android 返回）的全局路由已移交 BackNavigator；
        // 此处只保留暂停中的 R 重开
        if (!Visible || !@event.IsActionPressed("restart"))
        {
            return;
        }

        // AB13：确认退出淡出窗口内忽略 R——删档后 ReloadCurrentScene 会杀淡出 tween 使 Quit 永不执行
        // （档删、未退出、静默重开新局的静默数据丢失路径）
        var exitConfirm = GetParent().GetNodeOrNull("ExitConfirm") as ExitConfirm;
        if (exitConfirm != null && exitConfirm.Exiting())
        {
            return;
        }

        GetTree().Paused = false;
        GameState.Instance.ResetRun();
        GetTree().ReloadCurrentScene();
    }

}
