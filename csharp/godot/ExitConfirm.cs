using Godot;

namespace InfiAir;

/// <summary>
/// 全局退出确认窗（复用组件）。
/// normal/battle 双模式：battle 模式显示进度损失警告（战斗中退出路径：
/// 暂停 →「退出游戏」→ 本窗，构成二次确认）。确认后统一执行退出前清理：
/// 设置落盘 → 资源 hook → 淡出 0.3s → quit。
/// Esc/手柄 B 取消由 BackNavigator 路由到 cancel()；取消（按钮/Esc 同路径）经
/// Canceled 事件通知打开者恢复（暂停页在弹确认窗前隐藏了自己，不恢复则树保持
/// 暂停且无任何可见 UI——软锁）。
/// UITheme/ChamferedPanel typed 直调；（process_mode=Always/layer=40）仍在 scenes/main.tscn 设置。
/// </summary>
public partial class ExitConfirm : CanvasLayer
{
    /// <summary>取消退出时发出（按钮点击与 BackNavigator 路由的 Esc 同一出口）；打开者据此恢复自身。</summary>
    public event System.Action? Canceled;
    private Label _msgLabel = null!;
    private Button _okButton = null!;
    private Button _saveQuitButton = null!;
    private Button _noSaveQuitButton = null!;
    private Button _cancelButton = null!;
    private ChamferedPanel _plate = null!;
    private ColorRect _dim = null!;
    private Label _titleLabel = null!;
    private bool _battle;
    private bool _exiting;

    private readonly Callable _onLocaleChanged;

    public ExitConfirm()
    {
        _onLocaleChanged = Callable.From(RefreshTexts);
    }

    public override void _Ready()
    {
        Visible = false;
        var shell = UITheme.MakePageShell("EXIT_TITLE");
        AddChild((Node)shell["root"].AsGodotObject());
        _dim = (ColorRect)shell["dim"].AsGodotObject();
        _plate = (ChamferedPanel)shell["panel"].AsGodotObject();
        _plate.CustomMinimumSize = new Vector2(560.0f, 320.0f);
        _titleLabel = (Label)shell["title"].AsGodotObject();
        var content = (VBoxContainer)shell["content"].AsGodotObject();

        _msgLabel = UITheme.MakeLabel("", UITheme.FontBody, UITheme.Text);
        content.AddChild(_msgLabel);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 20);
        content.AddChild(row);

        _cancelButton = MakeButton(Tr("EXIT_CANCEL"));
        _cancelButton.Pressed += Cancel;
        row.AddChild(_cancelButton);

        // 战斗模式专属：存档并退出 / 不保存退出（正常模式只有「确认退出 / 取消」两键）
        _saveQuitButton = MakeButton(Tr("EXIT_SAVE_QUIT"));
        _saveQuitButton.Pressed += OnSaveQuitPressed;
        row.AddChild(_saveQuitButton);
        _noSaveQuitButton = MakeButton(Tr("EXIT_NOSAVE_QUIT"));
        _noSaveQuitButton.AddThemeColorOverride("font_color", UITheme.Danger);
        _noSaveQuitButton.AddThemeColorOverride("font_hover_color", UITheme.Danger);
        _noSaveQuitButton.Pressed += OnOkPressed;
        row.AddChild(_noSaveQuitButton);

        _okButton = MakeButton(Tr("EXIT_OK"));
        _okButton.AddThemeColorOverride("font_color", UITheme.Danger);
        _okButton.AddThemeColorOverride("font_hover_color", UITheme.Danger);
        _okButton.Pressed += OnOkPressed;
        row.AddChild(_okButton);

        var gs = GameState.Instance;
        if (!gs.IsConnected(GameState.SignalName.LocaleChanged, _onLocaleChanged))
        {
            gs.Connect(GameState.SignalName.LocaleChanged, _onLocaleChanged);
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

    private Button MakeButton(string text)
    {
        var button = UITheme.MakeButton(text);
        button.CustomMinimumSize = new Vector2(200.0f, 56.0f);
        return button;
    }

    /// <summary>打开确认窗；battle=true 时显示进度损失警告（战斗中退出路径）</summary>
    public void ShowConfirm() => ShowConfirm(false);

    public void ShowConfirm(bool battle)
    {
        _battle = battle;
        RefreshTexts();
        Visible = true;
        UITheme.AnimateModalOpen(_dim, _plate);
        // 默认焦点在「取消」（安全侧），防止误按 Enter 直接退出
        _cancelButton.GrabFocus();
    }

    private void RefreshTexts()
    {
        _titleLabel.Text = Tr("EXIT_TITLE");
        _msgLabel.Text = _battle ? Tr("EXIT_BATTLE_MSG") : Tr("EXIT_MSG");
        _msgLabel.AddThemeColorOverride("font_color", _battle ? UITheme.Danger : UITheme.Text);
        _okButton.Text = Tr("EXIT_OK");
        _saveQuitButton.Text = Tr("EXIT_SAVE_QUIT");
        _noSaveQuitButton.Text = Tr("EXIT_NOSAVE_QUIT");
        _cancelButton.Text = Tr("EXIT_CANCEL");
        // 战斗模式三键（存档并退出 / 不保存退出 / 取消）；正常模式仅「确认退出 / 取消」
        _saveQuitButton.Visible = _battle;
        _noSaveQuitButton.Visible = _battle;
        _okButton.Visible = !_battle;
    }

    /// <summary>取消退出（Esc/手柄 B 由 BackNavigator 路由到这里）</summary>
    public void Cancel()
    {
        if (_exiting)
        {
            return;
        }
        Visible = false;
        Canceled?.Invoke();
    }

    /// <summary>退出确认已受理（_exiting），调用方（GameState.RestartRun）须屏蔽冲突快捷键。</summary>
    public bool Exiting() => _exiting;

    private void OnOkPressed()
    {
        if (_exiting)
        {
            return;
        }
        _exiting = true;
        ExecuteExitCleanup(_battle);
        FadeAndQuit();
    }

    /// <summary>「存档并退出」：先落盘本局进度再走统一退出清理（战斗模式专属）。</summary>
    private void OnSaveQuitPressed()
    {
        if (_exiting)
        {
            return;
        }
        _exiting = true;
        GameState.Instance.SaveRun();
        ExecuteExitCleanup(_battle);
        FadeAndQuit();
    }

    private void ExecuteExitCleanup(bool battle)
    {
        GameState.Instance.SaveSettings();
        OnExitCleanup();
    }

    /// <summary>退出前资源/连接清理 hook：本项目无网络代码；停止未播完的音效，避免退出时播放实例泄漏</summary>
    private void OnExitCleanup()
    {
        GameState.Instance.StopAllSfx();
    }

    /// <summary>短暂过渡动画（淡出黑屏 0.3s）后退出，避免突兀切进程</summary>
    private void FadeAndQuit()
    {
        var fadeLayer = new CanvasLayer { Layer = 90 };
        AddChild(fadeLayer);
        var fade = new ColorRect { Color = new Color(0.0f, 0.0f, 0.0f, 0.0f) };
        fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        fadeLayer.AddChild(fade);
        var tween = CreateTween();
        tween.TweenProperty(fade, "color:a", 1.0, 0.3);
        // tween_callback 替代 await——淡出期间场景卸载/双退出时
        // tween 随节点释放自动取消，不留挂起协程
        tween.TweenCallback(Callable.From(() => GetTree().Quit()));
    }

}
