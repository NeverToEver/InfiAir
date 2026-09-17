using Godot;
using InfiAir.Core;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// Esc 暂停页：左缘轮盘菜单（继续/保存/设置/重开/退出）
/// + 右区聚焦项说明卡。ui_cancel（Esc/手柄 B）的全局返回路由统一在 BackNavigator，
/// 本页只提供 open()/close() 供其调用；「退出游戏」走 ExitConfirm 战斗模式二次确认。
/// 轮盘方向键/Enter 导航（页面无焦点控件，全时接管）。
/// （process_mode=Always/layer=15 仍在 scenes/main.tscn 设置。）
/// </summary>
public partial class PauseUi : RadialMenuLayer
{
    // 说明卡动效：聚焦切换走文本交叉淡化，显隐走整体淡入淡出（硬切 Visible 会闪跳）
    private const float HintSwapOutTime = 0.08f;
    private const float HintSwapInTime = 0.12f;
    private const float HintAppearTime = 0.16f;
    private const float HintHideTime = 0.10f;
    private const float HintPunchAmount = 1.03f;
    private const float HintPunchTime = 0.16f;

    private Label _titleLabel = null!;
    private Label _hintTitle = null!;
    private Label _hintBody = null!;
    private ChamferedPanel _hintPlate = null!;
    private SettingsUi? _settingsUi; // 惰性绑定（SettingsUI 的 _ready 晚于本节点）
    private Tween? _hintTween; // 说明卡交叉淡化/显隐的唯一 tween，重入前 kill 防属性竞争

    private readonly Callable _onLocaleChanged;

    public PauseUi()
    {
        _onLocaleChanged = Callable.From(OnLocaleChanged);
    }

    public override void _Ready()
    {
        Visible = false;
        // is_connected 守卫，场景重载（reload_current_scene）后重进树不重复连接
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
            // C# 事件订阅：两者同树同生命周期，随对方消亡，无需退订（仅 Connect 需守卫）
            exitConfirm.Canceled += OnExitCanceled;
        }
    }

    public override void _ExitTree()
    {
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
        // 初态隐藏：首次聚焦经 ShowHint 淡入，否则会以满 alpha 硬出现
        _hintPlate.Visible = false;
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
        Wheel.FocusOption(0); // 开页聚焦「继续」：默认弧面中点槽会停在「重新出击」（误 Enter 重开本局）
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

    /// <summary>右区说明卡随轮盘聚焦项联动。聚焦/选中逻辑本身保持同步，仅文本切换与显隐走淡入淡出；
    /// 缩放冲击属运动脉冲，受 ReduceFlash 无障碍约束。</summary>
    private void RefreshHint()
    {
        var focused = Wheel.FocusedOption;
        if (focused == null)
        {
            if (_hintPlate.Visible)
            {
                HideHint();
            }

            return;
        }

        var title = focused.Label;
        var body = Tr("MENU_HINT_" + focused.Id.ToUpperInvariant());
        if (_hintPlate.Visible)
        {
            SwapHintText(title, body);
        }
        else
        {
            ShowHint(title, body);
        }
    }

    /// <summary>说明卡首次出现：整体淡入（不硬切 Visible）。</summary>
    private void ShowHint(string title, string body)
    {
        KillHintTween();
        _hintTitle.Text = title;
        _hintBody.Text = body;
        _hintTitle.Modulate = new Color(_hintTitle.Modulate, 1.0f);
        _hintBody.Modulate = new Color(_hintBody.Modulate, 1.0f);
        _hintPlate.Visible = true;
        _hintPlate.Modulate = new Color(_hintPlate.Modulate, 0.0f);
        var tw = _hintPlate.CreateTween();
        _hintTween = tw;
        tw.TweenProperty(_hintPlate, "modulate:a", 1.0f, HintAppearTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        if (!GameState.Instance.ReduceFlash)
        {
            UITheme.PunchScale(_hintPlate, HintPunchAmount, HintPunchTime);
        }
    }

    /// <summary>说明卡消失：整体淡出后再隐藏（聚焦项为空时）。</summary>
    private void HideHint()
    {
        KillHintTween();
        var tw = _hintPlate.CreateTween();
        _hintTween = tw;
        tw.TweenProperty(_hintPlate, "modulate:a", 0.0f, HintHideTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tw.TweenCallback(Callable.From(() =>
        {
            _hintTween = null;
            if (GodotObject.IsInstanceValid(_hintPlate))
            {
                _hintPlate.Visible = false;
            }
        }));
    }

    /// <summary>说明卡已显示时的文本交叉淡化：先淡出旧文，再换文淡入；面板同时回正 alpha
    /// （可能在上一轮隐去的半途被重新聚焦）。</summary>
    private void SwapHintText(string title, string body)
    {
        KillHintTween();
        var tw = _hintPlate.CreateTween();
        _hintTween = tw;
        tw.SetParallel(true);
        tw.TweenProperty(_hintTitle, "modulate:a", 0.0f, HintSwapOutTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tw.TweenProperty(_hintBody, "modulate:a", 0.0f, HintSwapOutTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tw.TweenProperty(_hintPlate, "modulate:a", 1.0f, HintSwapOutTime);
        // chain 之后的回调在并行的淡出段结束才执行，故换文与淡入严格串行
        tw.Chain().TweenCallback(Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(_hintTitle))
            {
                return;
            }

            _hintTitle.Text = title;
            _hintBody.Text = body;
            _hintTitle.Modulate = new Color(_hintTitle.Modulate, 0.0f);
            _hintBody.Modulate = new Color(_hintBody.Modulate, 0.0f);
            FadeHintLabelsIn();
        }));
        if (!GameState.Instance.ReduceFlash)
        {
            UITheme.PunchScale(_hintPlate, HintPunchAmount, HintPunchTime);
        }
    }

    private void FadeHintLabelsIn()
    {
        var tw = _hintPlate.CreateTween();
        _hintTween = tw;
        tw.SetParallel(true);
        tw.TweenProperty(_hintTitle, "modulate:a", 1.0f, HintSwapInTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(_hintBody, "modulate:a", 1.0f, HintSwapInTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    /// <summary>kill 进行中的说明卡 tween（快速连切聚焦时防同属性竞争抖动）。</summary>
    private void KillHintTween()
    {
        if (_hintTween != null && _hintTween.IsValid())
        {
            _hintTween.Kill();
        }

        _hintTween = null;
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
        // 战斗中退出：ExitConfirm 战斗模式二次确认（带进度损失警告）。
        // 练习局没有进度可存，走非战斗模式（确认退出/取消）——不提供「保存并退出」：
        // 那个按钮在练习态下会落到 SaveRun 的早退守卫上，玩家按的是一件不存在的事。
        // GetNodeOrNull + 判空——宿主缺 ExitConfirm 节点时不崩溃（防御性，正常 main.tscn 必有）
        var exitConfirm = GetParent().GetNodeOrNull("ExitConfirm") as ExitConfirm;
        if (exitConfirm != null)
        {
            Visible = false;
            SetWheelActive(false);
            exitConfirm.ShowConfirm(!GameState.Instance.PracticeActive);
        }
    }

    /// <summary>R 重开主入口（轮盘「重新出击」与 _UnhandledInput 的 restart 动作共用）：转调单口
    /// （退出确认互斥守卫已在 GameState.RestartRun 内收口）。</summary>
    private void RestartRun()
    {
        GameState.Instance.RestartRun();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // ui_cancel（Esc/手柄 B）与鼠标右键的全局路由已移交 BackNavigator；
        // 此处只保留暂停中的 R 重开（轮盘 KeyboardEnabled 只接管方向键/Enter）
        if (!Visible || !@event.IsActionPressed("restart"))
        {
            return;
        }

        RestartRun();
    }
}
