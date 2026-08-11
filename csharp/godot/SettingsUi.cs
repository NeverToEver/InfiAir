using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 设置界面：左侧导航三页——「控制」（可改键表 + 恢复默认）、
/// 「操作模式」（Ctrl/Shift 按住切换、语言、视角缩放、窗口大小）、「关于」（版本与操作速查）。
/// 改键：点「改键」进入捕获态，下一按键即绑定（Esc 取消），冲突键从占用者移除。
/// M5 全量迁移（2026-08-08 自 scripts/settings_ui.gd）：CanvasLayer 子类。
/// UITheme/ChamferedPanel 为 C# 类 typed 直调；GameState（GDScript autoload，M7 迁移）
/// </summary>
public partial class SettingsUi : CanvasLayer
{
    // ---------------- GDScript 常量（C# 无法访问 GDScript const，硬编码等价副本，来源 autoload/game_state.gd） ----------------
    private static readonly StringName[] RebindableActions =
    {
        new("move_up"), new("move_down"), new("move_left"), new("move_right"),
        new("boost"), new("fine_move"), new("dash"), new("dock"),
        new("homecoming"), new("give_up"), new("buff_panel"), new("parry"),
    };
    private static readonly StringName[] AimAssistOrder = { new("low"), new("medium"), new("high") };
    private static readonly StringName[] ViewZoomOrder = { new("small"), new("medium"), new("large") };
    private static readonly StringName[] WindowSizeOrder = { new("small"), new("medium"), new("large") };
    private static readonly StringName[] PageIds = { new("controls"), new("modes"), new("about") };
    private static readonly StringName PageControls = new("controls");
    private static readonly StringName PageModes = new("modes");
    private static readonly StringName PageAbout = new("about");
    private static readonly StringName LayoutPs = new("ps");

    /// <summary>关闭信号（设置页已关闭）：生产侧暂无消费方（BackNavigator 以可见态路由），
    /// E13 先例——保留 API 供外部/未来 UI 连接，勿当死代码删除。</summary>
    [Signal]
    public delegate void BackPressedEventHandler();

    private Button _ctrlHold = null!;
    private Button _ctrlToggle = null!;
    private Button _shiftHold = null!;
    private Button _shiftToggle = null!;
    private readonly ButtonGroup _ctrlGroup = new();
    private readonly ButtonGroup _shiftGroup = new();
    private readonly ButtonGroup _langGroup = new();
    private Button _langZh = null!;
    private Button _langEn = null!;
    private readonly ButtonGroup _zoomGroup = new();
    private readonly Godot.Collections.Dictionary _zoomButtons = new(); // 视角档位 -> Button
    private readonly ButtonGroup _aimGroup = new();
    private readonly Godot.Collections.Dictionary _aimButtons = new(); // 瞄准辅助强度档位 -> Button
    private readonly ButtonGroup _windowGroup = new();
    private readonly Godot.Collections.Dictionary _windowButtons = new(); // 窗口尺寸档位 -> Button
    private Button _reduceFlashBtn = null!; // 无障碍·减少闪光开关
    private Button _touchBtn = null!; // 触控·虚拟控件开关（mobile touch）
    private Button _mouseLockBtn = null!; // 显示·鼠标锁定窗口内开关
    private HSlider _joySpeedSlider = null!; // 手柄·右摇杆瞄准灵敏度
    private HSlider _joyDeadzoneSlider = null!; // 手柄·摇杆死区
    private Label _joyLayoutLabel = null!; // 手柄·当前布局指示（Xbox/PS）
    private Label _versionLabel = null!;
    private Label _cheatsheetLabel = null!;
    private ChamferedPanel _plate = null!;
    private ColorRect _dim = null!;

    private readonly Godot.Collections.Dictionary _pages = new(); // 页名 -> Control
    private readonly Godot.Collections.Dictionary _navButtons = new();
    private readonly Godot.Collections.Dictionary _rebindRows = new(); // action -> {"keys": Label, "button": Button, "name": Label}
    private Label _hintLabel = null!;
    private StringName _capturingAction = new StringName();
    private Label _titleLabel = null!;
    private Button _backButton = null!;
    private Button _resetButton = null!;
    private readonly ButtonGroup _navGroup = new();
    private CanvasLayer? _opener; // 打开者（开始/暂停面板），返回时恢复其可见

    private readonly Callable _onKeyBindingsChanged;
    private readonly Callable _onLocaleChanged;
    private readonly Callable _onJoyLayoutChanged;

    public SettingsUi()
    {
        _onKeyBindingsChanged = Callable.From(RefreshRebindRows);
        _onLocaleChanged = Callable.From(OnLocaleChanged);
        _onJoyLayoutChanged = Callable.From(RefreshJoyLayoutLabel);
    }

    public override void _Ready()
    {
        AddToGroup("settings_ui");
        Visible = false;
        ProcessMode = Node.ProcessModeEnum.Always;
        var shell = UITheme.MakePageShell("SET_TITLE");
        AddChild((Node)shell["root"].AsGodotObject());
        _dim = (ColorRect)shell["dim"].AsGodotObject();
        _plate = (ChamferedPanel)shell["panel"].AsGodotObject();
        _plate.CustomMinimumSize = new Vector2(1000.0f, 700.0f);
        // L17：面板内容自适应高度钳制——modes 页 895px+ 曾把面板撑到 ~1150px 超屏；
        // 钳到 1040（1080p 留上下边距），超限内容由 _wrap_scroll 的滚动容器在内容区内滚动。
        _plate.MaxContentHeight = 1040.0f;
        _titleLabel = (Label)shell["title"].AsGodotObject();
        var vbox = (VBoxContainer)shell["content"].AsGodotObject();
        // 设置页内容从顶部排布（覆盖 shell 的居中），body 纵向填满
        vbox!.Alignment = BoxContainer.AlignmentMode.Begin;

        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 20);
        body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddChild(body);

        // 左侧导航
        var nav = new VBoxContainer();
        nav.AddThemeConstantOverride("separation", 8);
        body.AddChild(nav);
        foreach (var pageId in PageIds)
        {
            var b = UITheme.MakeToggleButton("", _navGroup);
            b.CustomMinimumSize = new Vector2(140.0f, 48.0f);
            b.Pressed += () => ShowPage(pageId);
            nav.AddChild(b);
            _navButtons[pageId] = Variant.From(b);
        }

        // 内容区
        var content = new VBoxContainer();
        content.CustomMinimumSize = new Vector2(760.0f, 480.0f);
        content.AddThemeConstantOverride("separation", 12);
        // L17：纵向填满 body（面板高度受限后由滚动容器在内容区内滚动，而非撑大面板）
        content.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body.AddChild(content);
        _pages[PageControls] = Variant.From(WrapScroll(BuildControlsPage()));
        _pages[PageModes] = Variant.From(WrapScroll(BuildModesPage()));
        _pages[PageAbout] = Variant.From(WrapScroll(BuildAboutPage()));
        RefreshNavLabels();
        foreach (var p in _pages.Values)
        {
            var page = p.AsGodotObject() as Control;
            content.AddChild(page);
            page!.Visible = false;
        }

        _hintLabel = UITheme.MakeLabel("", UITheme.FontCaption, UITheme.AccentGold);
        vbox.AddChild(_hintLabel);

        _backButton = UITheme.MakeButton(Tr("SET_BACK"));
        _backButton.CustomMinimumSize = new Vector2(240.0f, 52.0f);
        _backButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _backButton.Pressed += OnBackPressed;
        vbox.AddChild(_backButton);

        // C22：is_connected 守卫，场景重载（reload_current_scene）后重进树不重复连接
        var gs = GameState.Instance;
        if (gs != null)
        {
            if (!gs.IsConnected("KeyBindingsChanged", _onKeyBindingsChanged))
            {
                gs.Connect("KeyBindingsChanged", _onKeyBindingsChanged);
            }

            if (!gs.IsConnected("LocaleChanged", _onLocaleChanged))
            {
                gs.Connect("LocaleChanged", _onLocaleChanged);
            }

            if (!gs.IsConnected("JoyLayoutChanged", _onJoyLayoutChanged))
            {
                gs.Connect("JoyLayoutChanged", _onJoyLayoutChanged);
            }
        }
    }

    public override void _ExitTree()
    {
        var gs = GameState.Instance;
        if (gs == null)
        {
            return;
        }

        if (gs.IsConnected("KeyBindingsChanged", _onKeyBindingsChanged))
        {
            gs.Disconnect("KeyBindingsChanged", _onKeyBindingsChanged);
        }

        if (gs.IsConnected("LocaleChanged", _onLocaleChanged))
        {
            gs.Disconnect("LocaleChanged", _onLocaleChanged);
        }

        if (gs.IsConnected("JoyLayoutChanged", _onJoyLayoutChanged))
        {
            gs.Disconnect("JoyLayoutChanged", _onJoyLayoutChanged);
        }
    }

    // ---------------- 控制（改键） ----------------

    /// <summary>内容页统一包滚动容器（L17）：面板最大高度限制后，超限内容在内容区内滚动而非撑大面板。
    /// ScrollContainer 自动滚动到聚焦子控件（Godot 4 内置 ensure_visible），手柄/键盘焦点链
    /// （L08 全项目模态聚焦约定）不受影响；页内容横向填满、纵向保持自身高度以启用滚动。</summary>
    private ScrollContainer WrapScroll(Control page)
    {
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.AddThemeConstantOverride("scrollbar_margin", 4);
        page.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(page);
        return scroll;
    }

    private VBoxContainer BuildControlsPage()
    {
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 8);
        var actions = RebindableActions;
        for (var i = 0; i < actions.Length; i++)
        {
            var action = actions[i];
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            var nameLabel = UITheme.MakeLabel(
                Tr("ACT_" + action.ToString().ToUpper()), UITheme.FontBody, UITheme.Text, HorizontalAlignment.Left
            );
            nameLabel.CustomMinimumSize = new Vector2(180.0f, 0.0f);
            row.AddChild(nameLabel);
            var keysLabel = UITheme.MakeLabel("", UITheme.FontBody, UITheme.TextDim, HorizontalAlignment.Left);
            keysLabel.CustomMinimumSize = new Vector2(280.0f, 0.0f);
            row.AddChild(keysLabel);
            var rebindButton = UITheme.MakeButton(Tr("SET_REBIND"));
            rebindButton.CustomMinimumSize = new Vector2(110.0f, 40.0f);
            rebindButton.AddThemeFontSizeOverride("font_size", UITheme.FontCaption);
            rebindButton.Pressed += () => StartCapture(action);
            row.AddChild(rebindButton);
            page.AddChild(row);
            var info = new Godot.Collections.Dictionary
            {
                ["keys"] = Variant.From(keysLabel),
                ["button"] = Variant.From(rebindButton),
                ["name"] = Variant.From(nameLabel),
            };
            _rebindRows[action] = Variant.From(info);
            // 行间细分隔线（末行不加）：密集列表的视觉分组
            if (i < actions.Length - 1)
            {
                var sep = new ColorRect
                {
                    Color = new Color(UITheme.AccentDim, 0.4f),
                    CustomMinimumSize = new Vector2(0.0f, 1.0f),
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                };
                page.AddChild(sep);
            }
        }

        _resetButton = UITheme.MakeButton(Tr("SET_RESET"));
        _resetButton.CustomMinimumSize = new Vector2(220.0f, 44.0f);
        _resetButton.Pressed += OnResetKeys;
        page.AddChild(_resetButton);
        return page;
    }

    private void RefreshRebindRows()
    {
        foreach (var action in _rebindRows.Keys)
        {
            var row = _rebindRows[action].AsGodotDictionary();
            ((Label)row["keys"].AsGodotObject()).Text = GameState.Instance.ActionKeysText(action.AsStringName());
            ((Label)row["name"].AsGodotObject()).Text = Tr("ACT_" + action.AsStringName().ToString().ToUpper());
        }
    }

    /// <summary>A7：测试/诊断经公开接口</summary>
    public void StartCapture(StringName action)
    {
        _capturingAction = action;
        _hintLabel.Text = GdFormat.Format(Tr("SET_CAPTURE"), Tr("ACT_" + action.ToString().ToUpper()));
    }

    public CanvasLayer? Opener()
    {
        return _opener;
    }

    public void Back()
    {
        OnBackPressed();
    }

    /// <summary>对外公开接口（A1 修复）：BackNavigator 决策查询改键捕获态</summary>
    public StringName CapturingAction()
    {
        return _capturingAction;
    }

    public Godot.Collections.Dictionary ZoomButtons()
    {
        return _zoomButtons;
    }

    public Godot.Collections.Dictionary WindowButtons()
    {
        return _windowButtons;
    }

    private void OnResetKeys()
    {
        GameState.Instance.ResetKeyBindings();
        _hintLabel.Text = Tr("SET_RESET_DONE");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || _capturingAction == new StringName())
        {
            return;
        }

        // K06：手柄 B（ui_cancel）在捕获态同样取消捕获——BackNavigator 对捕获态放行不消费，
        // 事件会传到本节点；原实现只处理 InputEventKey，手柄 B 按下无人消费（唯一 B 失灵的界面态）
        if (@event.IsActionPressed("ui_cancel"))
        {
            _hintLabel.Text = Tr("SET_CANCELLED");
            _capturingAction = new StringName();
            GetViewport().SetInputAsHandled();
            return;
        }

        // 鼠标右键：与 Esc 同路由的固定取消触发器（BackNavigator 对捕获态放行不消费，事件传到本节点）
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Right)
        {
            _hintLabel.Text = Tr("SET_CANCELLED");
            _capturingAction = new StringName();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            // 2026-08-03 审计：删除不可达的 KEY_ESCAPE 分支——ui_cancel 不在 REBINDABLE_ACTIONS，
            // 捕获态下 Esc 必先命中上方 ui_cancel 取消分支并 return，到不了此处
            // 2026-08-10 健壮性审查：捕获对齐 GetActionKeycodes 的双键回退语义——非标准布局/
            // IME 键的 Keycode 为 Key.None 时裸绑定 0（KEY_NONE）致该动作永久无法触发
            var kc = key.Keycode != Key.None ? (int)key.Keycode : (int)key.PhysicalKeycode;
            // 双键回退后仍为 Key.None（RebindAction 无校验）：取消捕获不写绑定，防动作永久失效
            if (kc == (int)Key.None)
            {
                _hintLabel.Text = Tr("SET_CANCELLED");
                _capturingAction = new StringName();
                GetViewport().SetInputAsHandled();
                return;
            }

            GameState.Instance.RebindAction(_capturingAction, kc);
            var boundKey = OS.GetKeycodeString((Key)kc);
            _hintLabel.Text = GdFormat.Format(Tr("SET_BOUND"), Tr("ACT_" + _capturingAction.ToString().ToUpper()), boundKey);
            _capturingAction = new StringName();
            GetViewport().SetInputAsHandled();
        }
    }

    // ---------------- 操作模式 ----------------

    private VBoxContainer BuildModesPage()
    {
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 14);
        // 按键模式（Ctrl/Shift）
        page.AddChild(UITheme.MakeSectionHeader(Tr("SET_MODES")));
        var ctrlPair = MakeModeRow(page, Tr("SET_CTRL_MODE"), _ctrlGroup);
        _ctrlHold = ctrlPair[0];
        _ctrlToggle = ctrlPair[1];
        _ctrlHold.Pressed += () => OnCtrlMode(false);
        _ctrlToggle.Pressed += () => OnCtrlMode(true);
        var shiftPair = MakeModeRow(page, Tr("SET_SHIFT_MODE"), _shiftGroup);
        _shiftHold = shiftPair[0];
        _shiftToggle = shiftPair[1];
        _shiftHold.Pressed += () => OnShiftMode(false);
        _shiftToggle.Pressed += () => OnShiftMode(true);
        // 语言 / Language
        page.AddChild(UITheme.MakeSectionHeader(Tr("SET_LANGUAGE")));
        var langRow = new HBoxContainer();
        langRow.AddThemeConstantOverride("separation", 16);
        page.AddChild(langRow);
        _langZh = UITheme.MakeToggleButton("中文", _langGroup);
        _langEn = UITheme.MakeToggleButton("English", _langGroup);
        langRow.AddChild(_langZh);
        langRow.AddChild(_langEn);
        _langZh.Pressed += () => GameState.Instance.SetLocale("zh");
        _langEn.Pressed += () => GameState.Instance.SetLocale("en");
        // 辅助瞄准强度（常驻不可关，仅弱/中/强三档）
        page.AddChild(UITheme.MakeSectionHeader(Tr("SET_AIM_ASSIST")));
        var aimRow = new HBoxContainer();
        aimRow.AddThemeConstantOverride("separation", 16);
        page.AddChild(aimRow);
        _aimButtons.Clear();
        foreach (var level in AimAssistOrder)
        {
            var ab = UITheme.MakeToggleButton(Tr("SET_AIM_" + level.ToString().ToUpper()), _aimGroup);
            ab.Pressed += () => GameState.Instance.SetAimAssistLevel(level);
            aimRow.AddChild(ab);
            _aimButtons[level] = Variant.From(ab);
        }

        // 机制说明（P1-1 新语义）：准星入标记框 → 出膛弹追踪该敌；档位调节框大小与追踪速度
        page.AddChild(UITheme.MakeLabel(Tr("SET_AIM_ASSIST_DESC"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Left));
        // 显示：视角缩放 + 窗口大小
        page.AddChild(UITheme.MakeSectionHeader(Tr("SET_DISPLAY")));
        var zoomRow = new HBoxContainer();
        zoomRow.AddThemeConstantOverride("separation", 16);
        page.AddChild(zoomRow);
        var zoomLabel = UITheme.MakeLabel(Tr("SET_VIEW_ZOOM"), UITheme.FontBody, UITheme.Text, HorizontalAlignment.Left);
        zoomLabel.CustomMinimumSize = new Vector2(140.0f, 0.0f);
        zoomRow.AddChild(zoomLabel);
        _zoomButtons.Clear();
        foreach (var level in ViewZoomOrder)
        {
            var b = UITheme.MakeToggleButton(Tr("SET_VIEW_" + level.ToString().ToUpper()), _zoomGroup);
            b.Pressed += () => GameState.Instance.SetViewZoom(level);
            zoomRow.AddChild(b);
            _zoomButtons[level] = Variant.From(b);
        }

        // 窗口大小（按钮含分辨率文本，加宽）
        var winRow = new HBoxContainer();
        winRow.AddThemeConstantOverride("separation", 16);
        page.AddChild(winRow);
        var winLabel = UITheme.MakeLabel(Tr("SET_WINDOW_SIZE"), UITheme.FontBody, UITheme.Text, HorizontalAlignment.Left);
        winLabel.CustomMinimumSize = new Vector2(140.0f, 0.0f);
        winRow.AddChild(winLabel);
        _windowButtons.Clear();
        foreach (var level in WindowSizeOrder)
        {
            var b = UITheme.MakeToggleButton(Tr("SET_WINDOW_" + level.ToString().ToUpper()), _windowGroup);
            b.CustomMinimumSize = new Vector2(210.0f, 48.0f);
            b.Pressed += () => GameState.Instance.SetWindowSize(level);
            winRow.AddChild(b);
            _windowButtons[level] = Variant.From(b);
        }

        // 鼠标锁定窗口内（MouseTrap：窗口聚焦期间鼠标移出内容区即被拉回，防止准星失控；失焦放行）
        var lockGroup = new ButtonGroup { AllowUnpress = true };
        _mouseLockBtn = UITheme.MakeToggleButton(Tr("SET_MOUSE_LOCK"), lockGroup);
        _mouseLockBtn.CustomMinimumSize = new Vector2(200.0f, 48.0f);
        _mouseLockBtn.Pressed += OnMouseLock;
        page.AddChild(_mouseLockBtn);
        page.AddChild(UITheme.MakeLabel(Tr("SET_MOUSE_LOCK_DESC"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Left));
        // 手柄（P0-1）：右摇杆瞄准灵敏度 + 摇杆死区（InputMap 全局 deadzone）
        page.AddChild(UITheme.MakeSectionHeader(Tr("SET_JOY")));
        // PS 布局适配：按已连接手柄显示布局与按钮标签对照（Xbox A/B/X/Y vs PS ✕/○/□/△）
        _joyLayoutLabel = UITheme.MakeLabel("", UITheme.FontCaption, UITheme.AccentGold, HorizontalAlignment.Left);
        page.AddChild(_joyLayoutLabel);
        RefreshJoyLayoutLabel();
        _joySpeedSlider = MakeJoySlider(
            page,
            Tr("SET_JOY_AIM_SPEED"),
            200.0f,
            4000.0f,
            (float)GameState.Instance.JoyAimSpeed,
            "%.0f",
            v => GameState.Instance.SetJoyAimSpeed(v)
        );
        _joyDeadzoneSlider = MakeJoySlider(
            page,
            Tr("SET_JOY_DEADZONE"),
            5.0f,
            90.0f,
            (float)(GameState.Instance.JoyDeadzone * 100.0),
            "%.0f%%",
            v => GameState.Instance.SetJoyDeadzone(v / 100.0)
        );
        page.AddChild(UITheme.MakeLabel(Tr("SET_JOY_DESC"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Left));
        // 触控（mobile touch）：虚拟摇杆/按钮开关（触屏设备；桌面键鼠/手柄不受影响，默认关）
        page.AddChild(UITheme.MakeSectionHeader(Tr("SET_TOUCH")));
        var touchGroup = new ButtonGroup { AllowUnpress = true };
        _touchBtn = UITheme.MakeToggleButton(Tr("SET_TOUCH_CONTROLS"), touchGroup);
        _touchBtn.CustomMinimumSize = new Vector2(200.0f, 48.0f);
        _touchBtn.Pressed += OnTouchControls;
        page.AddChild(_touchBtn);
        page.AddChild(UITheme.MakeLabel(Tr("SET_TOUCH_DESC"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Left));
        // 无障碍（Meta HUD）：减少闪光（色差 ×0.4、禁呼吸/抖动/心跳视觉脉冲，音效保留）
        page.AddChild(UITheme.MakeSectionHeader(Tr("SET_ACCESSIBILITY")));
        var rfRow = new HBoxContainer();
        rfRow.AddThemeConstantOverride("separation", 16);
        page.AddChild(rfRow);
        // 单开关 ButtonGroup 需 allow_unpress，否则按下后无法再取消
        var rfGroup = new ButtonGroup { AllowUnpress = true };
        _reduceFlashBtn = UITheme.MakeToggleButton(Tr("SET_REDUCE_FLASH"), rfGroup);
        _reduceFlashBtn.CustomMinimumSize = new Vector2(160.0f, 48.0f);
        _reduceFlashBtn.Pressed += OnReduceFlash;
        rfRow.AddChild(_reduceFlashBtn);
        return page;
    }

    private Button[] MakeModeRow(Container parent, string labelText, ButtonGroup group)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        parent.AddChild(row);
        var label = UITheme.MakeLabel(labelText, UITheme.FontBody, UITheme.Text, HorizontalAlignment.Left);
        label.CustomMinimumSize = new Vector2(240.0f, 0.0f);
        row.AddChild(label);
        var hold = UITheme.MakeToggleButton(Tr("SET_HOLD"), group);
        var toggle = UITheme.MakeToggleButton(Tr("SET_TOGGLE"), group);
        row.AddChild(hold);
        row.AddChild(toggle);
        return new[] { hold, toggle };
    }

    /// <summary>P0-1：手柄参数滑杆行（标题 + HSlider + 数值标签；value_changed 实时回调并更新数值显示）</summary>
    private HSlider MakeJoySlider(
        Container parent, string title, float minValue, float maxValue, float value, string format, Action<float> onChanged
    )
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        parent.AddChild(row);
        var label = UITheme.MakeLabel(title, UITheme.FontBody, UITheme.Text, HorizontalAlignment.Left);
        label.CustomMinimumSize = new Vector2(200.0f, 0.0f);
        row.AddChild(label);
        var slider = new HSlider
        {
            MinValue = minValue,
            MaxValue = maxValue,
            Step = 1.0f,
            Value = value,
            CustomMinimumSize = new Vector2(240.0f, 0.0f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        row.AddChild(slider);
        var valueLabel = UITheme.MakeLabel(GdFormat.Format(format, value), UITheme.FontBody, UITheme.TextDim);
        valueLabel.CustomMinimumSize = new Vector2(70.0f, 0.0f);
        row.AddChild(valueLabel);
        slider.ValueChanged += v =>
        {
            valueLabel.Text = GdFormat.Format(format, (float)v);
            onChanged((float)v);
            // AB23：键盘焦点链方向键调整只走 ValueChanged（原仅 DragEnded 落盘，正常退出
            // 靠 SaveProfile 兜底，仅进程异常终止丢失调整值）——滑杆调整频率低，写盘直接可接受
            GameState.Instance.PersistJoySettings();
        };
        // K06：拖动结束同样持久化（与 ValueChanged 并存，拖动场景双保险）
        slider.DragEnded += _ => GameState.Instance.PersistJoySettings();
        return slider;
    }

    /// <summary>PS 布局适配：刷新手柄布局指示（joy_layout_changed / locale 重建时调用）</summary>
    private void RefreshJoyLayoutLabel()
    {
        if (_joyLayoutLabel == null)
        {
            return;
        }

        _joyLayoutLabel.Text = GameState.Instance.JoyLayout == LayoutPs ? Tr("SET_JOY_LAYOUT_PS") : Tr("SET_JOY_LAYOUT_XBOX");
    }

    // ---------------- 关于 ----------------

    private VBoxContainer BuildAboutPage()
    {
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 10);
        _versionLabel = UITheme.MakeLabel(GdFormat.Format(Tr("SET_VERSION"), Engine.GetVersionInfo()["string"].AsString()), UITheme.FontBody, UITheme.AccentGold);
        page.AddChild(_versionLabel);
        _cheatsheetLabel = UITheme.MakeLabel(Tr("SET_CHEATSHEET"), UITheme.FontCaption, UITheme.TextDim);
        page.AddChild(_cheatsheetLabel);
        return page;
    }

    // ---------------- 通用 ----------------

    private void RefreshNavLabels()
    {
        ((Button)_navButtons[PageControls].AsGodotObject()).Text = Tr("SET_CONTROLS");
        ((Button)_navButtons[PageModes].AsGodotObject()).Text = Tr("SET_MODES");
        ((Button)_navButtons[PageAbout].AsGodotObject()).Text = Tr("SET_ABOUT");
    }

    public void ShowPage(StringName pageName)
    {
        foreach (var key in _pages.Keys)
        {
            var k = key.AsStringName();
            (_pages[key].AsGodotObject() as Control)!.Visible = k == pageName;
            ((Button)_navButtons[key].AsGodotObject()).SetPressedNoSignal(k == pageName);
        }
    }

    /// <summary>打开面板并刷新选中态；opener 为打开者（开始/暂停面板），返回时恢复其可见</summary>
    public void ShowSettings()
    {
        ShowSettings(null);
    }

    public void ShowSettings(CanvasLayer? openerLayer)
    {
        _opener = openerLayer;
        _ctrlHold.SetPressedNoSignal(!GameState.Instance.CtrlToggleMode);
        _ctrlToggle.SetPressedNoSignal(GameState.Instance.CtrlToggleMode);
        _shiftHold.SetPressedNoSignal(!GameState.Instance.ShiftToggleMode);
        _shiftToggle.SetPressedNoSignal(GameState.Instance.ShiftToggleMode);
        RefreshRebindRows();
        RefreshLangButtons();
        RefreshZoomButtons();
        RefreshWindowButtons();
        RefreshAimButtons();
        _reduceFlashBtn.SetPressedNoSignal(GameState.Instance.ReduceFlash);
        _mouseLockBtn.SetPressedNoSignal(GameState.Instance.MouseLock);
        _touchBtn.SetPressedNoSignal(GameState.Instance.TouchControls);
        _hintLabel.Text = "";
        _capturingAction = new StringName();
        ShowPage(PageControls);
        Visible = true;
        UITheme.AnimateModalOpen(_dim, _plate);
        // 键盘/手柄链路：打开即有焦点（方向键在导航/行间遍历，Enter 触发）
        ((Button)_navButtons[PageControls].AsGodotObject()).GrabFocus();
    }

    private void RefreshLangButtons()
    {
        _langZh.SetPressedNoSignal(GameState.Instance.Locale == "zh");
        _langEn.SetPressedNoSignal(GameState.Instance.Locale == "en");
    }

    private void RefreshZoomButtons()
    {
        foreach (var level in _zoomButtons.Keys)
        {
            ((Button)_zoomButtons[level].AsGodotObject()).SetPressedNoSignal(level.AsStringName() == GameState.Instance.ViewZoom);
        }
    }

    private void RefreshWindowButtons()
    {
        foreach (var level in _windowButtons.Keys)
        {
            ((Button)_windowButtons[level].AsGodotObject()).SetPressedNoSignal(level.AsStringName() == GameState.Instance.WindowSize);
        }
    }

    private void RefreshAimButtons()
    {
        foreach (var level in _aimButtons.Keys)
        {
            ((Button)_aimButtons[level].AsGodotObject()).SetPressedNoSignal(level.AsStringName() == GameState.Instance.AimAssistLevel);
        }
    }

    private void OnLocaleChanged()
    {
        // H20（健壮性审核）：_pages 空（locale_changed 早于 _ready）时防御，必须最先执行——
        // 原守卫在后，前面的 _title_label 等节点 _ready 前为 null 会先空引用崩溃
        if (_pages.Count == 0)
        {
            return;
        }

        _titleLabel.Text = Tr("SET_TITLE");
        _backButton.Text = Tr("SET_BACK");
        _resetButton.Text = Tr("SET_RESET");
        _versionLabel.Text = GdFormat.Format(Tr("SET_VERSION"), Engine.GetVersionInfo()["string"].AsString());
        _cheatsheetLabel.Text = Tr("SET_CHEATSHEET");
        RefreshLangButtons();
        RefreshNavLabels();
        // 2026-08-03 审计：重建前记录当前页并恢复（原实现无条件跳回「控制」页）；
        // 旧行的一次冗余刷新随旧页一起销毁，统一由重建后 _refresh_rebind_rows 刷新
        var current = PageControls;
        foreach (var key in _pages.Keys)
        {
            if ((_pages[key].AsGodotObject() as Control)!.Visible)
            {
                current = key.AsStringName();
                break;
            }
        }

        // 重建内容区文本（重建代价低，保证全部文案换语言）
        // U16：Free() 同步删除——QueueFree 帧末才删，同帧 add_child 新旧页并存闪一帧
        //（Hud.cs:1194 同场景先例）
        var content = FirstPageParent();
        foreach (var p in _pages.Values)
        {
            (p.AsGodotObject() as Control)!.Free();
        }

        _pages[PageControls] = Variant.From(WrapScroll(BuildControlsPage()));
        _pages[PageModes] = Variant.From(WrapScroll(BuildModesPage()));
        _pages[PageAbout] = Variant.From(WrapScroll(BuildAboutPage()));
        foreach (var p in _pages.Values)
        {
            var page = p.AsGodotObject() as Control;
            content!.AddChild(page);
            page!.Visible = false;
        }

        RefreshRebindRows();
        ShowPage(current);
        // L08（2026-08-03 审查）：重建后归还焦点——旧按钮已 queue_free，焦点丢失使
        // 键盘 Tab 循环与手柄方向键导航中断（对齐 show_settings 的 grab_focus 约定）
        ((Button)_navButtons[current].AsGodotObject()).GrabFocus();
        // 操作模式按钮选中态刷新
        _ctrlHold.SetPressedNoSignal(!GameState.Instance.CtrlToggleMode);
        _ctrlToggle.SetPressedNoSignal(GameState.Instance.CtrlToggleMode);
        _shiftHold.SetPressedNoSignal(!GameState.Instance.ShiftToggleMode);
        _shiftToggle.SetPressedNoSignal(GameState.Instance.ShiftToggleMode);
        RefreshZoomButtons();
        RefreshWindowButtons();
        RefreshAimButtons();
        _reduceFlashBtn.SetPressedNoSignal(GameState.Instance.ReduceFlash);
        _mouseLockBtn.SetPressedNoSignal(GameState.Instance.MouseLock);
        _touchBtn.SetPressedNoSignal(GameState.Instance.TouchControls);
    }

    /// <summary>首个内容页的父容器（= shell content VBox；GDScript `_pages.values()[0].get_parent()`）。</summary>
    private Container? FirstPageParent()
    {
        foreach (var p in _pages.Values)
        {
            return ((Control)p.AsGodotObject()).GetParent() as Container;
        }

        return null;
    }

    private void OnCtrlMode(bool toggleMode)
    {
        GameState.Instance.SetCtrlToggleMode(toggleMode);
    }

    private void OnShiftMode(bool toggleMode)
    {
        GameState.Instance.SetShiftToggleMode(toggleMode);
    }

    private void OnReduceFlash()
    {
        GameState.Instance.SetReduceFlash(_reduceFlashBtn.ButtonPressed);
    }

    private void OnTouchControls()
    {
        GameState.Instance.SetTouchControls(_touchBtn.ButtonPressed);
    }

    private void OnMouseLock()
    {
        GameState.Instance.SetMouseLock(_mouseLockBtn.ButtonPressed);
    }

    /// <summary>测试/诊断经公开接口（对齐 window_buttons() 模式）</summary>
    public Button MouseLockButton()
    {
        return _mouseLockBtn;
    }

    private void OnBackPressed()
    {
        _capturingAction = new StringName();
        Visible = false;
        if (_opener != null && GodotObject.IsInstanceValid(_opener))
        {
            _opener.Visible = true;
            // 焦点还给打开者主按钮：键盘/手柄链路不因进出设置页而断
            // U13：typed 分派（打开者 = 开始面板 Welcome 或暂停面板 PauseUi，均有 GrabPrimaryFocus）
            switch (_opener)
            {
                case PauseUi p:
                    p.GrabPrimaryFocus();
                    break;
                case Welcome w:
                    w.GrabPrimaryFocus();
                    break;
            }
        }

        _opener = null;
        EmitSignal(SignalName.BackPressed);
    }
}
