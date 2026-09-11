using Godot;
using InfiAir.Core;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 设置界面：左缘圆盘导航三页——「控制」（可改键表 + 恢复默认）、
/// 「操作模式」（难度、跳过过场、开火方式、Ctrl/Shift 按住切换、语言、视角缩放、窗口大小）、「关于」（版本与操作速查）；
/// 面板内芯片行保留焦点链可达性（改键/滑杆等控件页，方向键让位焦点导航）。
/// 改键：点「改键」进入捕获态，下一按键即绑定（右键撤销 / Esc 取消），冲突键从占用者移除。
/// </summary>
public partial class SettingsUi : RadialMenuLayer
{
    // ---------------- 可改键动作清单（硬编码表，须与 GameState.REBINDABLE_ACTIONS 保持一致） ----------------
    private static readonly StringName[] RebindableActions =
    {
        new("move_up"), new("move_down"), new("move_left"), new("move_right"),
        new("boost"), new("fine_move"), new("dash"), new("dock"),
        new("homecoming"), new("give_up"), new("augment_panel"), new("parry"),
    };
    private static readonly StringName[] AimAssistOrder = { new("low"), new("medium"), new("high") };
    private static readonly StringName[] ViewZoomOrder = { new("small"), new("medium"), new("large") };
    private static readonly StringName[] FpsCapOrder = { new("fps60"), new("fps120"), new("fps144"), new("fps165"), new("fps180"), new("fps240") };
    private static readonly StringName[] PageIds = { new("controls"), new("modes"), new("about") };
    private static readonly StringName PageControls = new("controls");
    private static readonly StringName PageModes = new("modes");
    private static readonly StringName PageAbout = new("about");
    private static readonly StringName LayoutPs = new("ps");

    /// <summary>关闭信号（设置页已关闭）：生产侧暂无消费方（BackNavigator 以可见态路由），
    /// 保留 API 供外部/未来 UI 连接，勿当死代码删除。</summary>
    [Signal]
    public delegate void BackPressedEventHandler();

    private Button _ctrlHold = null!;
    private Button _ctrlToggle = null!;
    private Button _shiftHold = null!;
    private Button _shiftToggle = null!;
    private Button _fireHold = null!;
    private Button _fireToggle = null!;
    private readonly ButtonGroup _ctrlGroup = new();
    private readonly ButtonGroup _shiftGroup = new();
    private readonly ButtonGroup _fireGroup = new();
    private readonly ButtonGroup _langGroup = new();
    private Button _langZh = null!;
    private Button _langEn = null!;
    private readonly ButtonGroup _zoomGroup = new();
    private readonly Godot.Collections.Dictionary _zoomButtons = new(); // 视角档位 -> Button
    private readonly ButtonGroup _aimGroup = new();
    private readonly Godot.Collections.Dictionary _aimButtons = new(); // 瞄准辅助强度档位 -> Button
    private readonly ButtonGroup _modeGroup = new();
    private readonly Godot.Collections.Dictionary _modeButtons = new(); // 窗口模式（windowed/borderless）-> Button
    private readonly ButtonGroup _resolutionGroup = new() { AllowUnpress = true };
    private readonly Godot.Collections.Dictionary _resolutionButtons = new(); // 分辨率档 -> Button
    private Label _resolutionInfoLabel = null!; // 分辨率说明/当前尺寸（custom/无边框时提示）
    private readonly ButtonGroup _diffGroup = new();
    private readonly Godot.Collections.Dictionary _diffButtons = new(); // 难度档位 -> Button
    private Button _skipIntroBtn = null!; // 流程·默认跳过入场动画开关
    private Button _reduceFlashBtn = null!; // 无障碍·减少闪光开关
    private Button _mouseLockBtn = null!; // 显示·鼠标锁定窗口内开关
    private Button _worldPostFxBtn = null!; // 画面·世界层增强（辉光/分级）开关
    private readonly ButtonGroup _fpsGroup = new();
    private readonly Godot.Collections.Dictionary _fpsButtons = new(); // 帧率上限档位 -> Button
    private Button _vsyncBtn = null!; // 性能·垂直同步开关
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
    private readonly Callable _onResolutionChanged;
    private readonly Callable _onWindowModeChanged;

    public SettingsUi()
    {
        _onKeyBindingsChanged = Callable.From(RefreshRebindRows);
        _onLocaleChanged = Callable.From(OnLocaleChanged);
        _onJoyLayoutChanged = Callable.From(RefreshJoyLayoutLabel);
        _onResolutionChanged = Callable.From((StringName _) => RefreshResolutionButtons());
        _onWindowModeChanged = Callable.From((StringName _) => RefreshWindowModeButtons());
    }

    public override void _Ready()
    {
        AddToGroup("settings_ui");
        Visible = false;
        ProcessMode = Node.ProcessModeEnum.Always;
        // 圆盘导航（chrome dim 弃用：本页遮罩由 page shell 提供，双遮罩会过压暗）；
        // 面板整体右移让出左缘轮盘弧面通航区
        BuildChrome();
        SetContentAnchor(() => _plate);
        // 混合页（面板内含焦点控件：改键/按钮行）：轮盘不接管方向键，留给页面焦点链
        // （键盘导航已移到 _Input 先 GUI 相位，不关会抢走整页键盘导航）
        Wheel.KeyboardEnabled = false;
        Dim.Visible = false;
        Wheel.Confirmed += OnWheelConfirmed;
        RebuildWheelMenu();
        var shell = UITheme.MakePageShell("SET_TITLE");
        AddChild((Node)shell["root"].AsGodotObject());
        RaiseWheel(); // shell 自带全屏遮罩：轮盘必须保持在遮罩之上
        _dim = (ColorRect)shell["dim"].AsGodotObject();
        // 面板右移：shell 的 CenterContainer（dim 首子节点）左缘内推，避让轮盘卡片
        if (shell["dim"].AsGodotObject() is Control shellRoot && shellRoot.GetChild(0) is Control center)
        {
            center.OffsetLeft = 580.0f; // 右移面板：左缘让出轮盘弧面通航区（卡片右缘 ≈570）
        }

        _plate = (ChamferedPanel)shell["panel"].AsGodotObject();
        _plate.CustomMinimumSize = new Vector2(1000.0f, 960.0f);
        // 面板内容自适应高度钳制——modes 页 895px+ 曾把面板撑到 ~1150px 超屏；
        // 钳到 1040（1080p 留上下边距），超限内容由 _wrap_scroll 的滚动容器在内容区内滚动。
        // min 高度 860：改键 12 行完整展示免滚动；modes 页仍走滚动。
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
        content.CustomMinimumSize = new Vector2(760.0f, 720.0f);
        content.AddThemeConstantOverride("separation", 12);
        // 纵向填满 body（面板高度受限后由滚动容器在内容区内滚动，而非撑大面板）
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

        // is_connected 守卫，场景重载（reload_current_scene）后重进树不重复连接
        var gs = GameState.Instance;
        if (!gs.IsConnected(GameState.SignalName.KeyBindingsChanged, _onKeyBindingsChanged))
        {
            gs.Connect(GameState.SignalName.KeyBindingsChanged, _onKeyBindingsChanged);
        }

        if (!gs.IsConnected(GameState.SignalName.LocaleChanged, _onLocaleChanged))
        {
            gs.Connect(GameState.SignalName.LocaleChanged, _onLocaleChanged);
        }

        if (!gs.IsConnected(GameState.SignalName.JoyLayoutChanged, _onJoyLayoutChanged))
        {
            gs.Connect(GameState.SignalName.JoyLayoutChanged, _onJoyLayoutChanged);
        }

        // 窗口分辨率档变化（含拖拽捕获为自定义）：设置页开着时同步选中态
        if (!gs.IsConnected(GameState.SignalName.ResolutionChanged, _onResolutionChanged))
        {
            gs.Connect(GameState.SignalName.ResolutionChanged, _onResolutionChanged);
        }

        if (!gs.IsConnected(GameState.SignalName.WindowModeChanged, _onWindowModeChanged))
        {
            gs.Connect(GameState.SignalName.WindowModeChanged, _onWindowModeChanged);
        }
    }

    public override void _ExitTree()
    {
        var gs = GameState.Instance;
        if (gs.IsConnected(GameState.SignalName.KeyBindingsChanged, _onKeyBindingsChanged))
        {
            gs.Disconnect(GameState.SignalName.KeyBindingsChanged, _onKeyBindingsChanged);
        }

        if (gs.IsConnected(GameState.SignalName.LocaleChanged, _onLocaleChanged))
        {
            gs.Disconnect(GameState.SignalName.LocaleChanged, _onLocaleChanged);
        }

        if (gs.IsConnected(GameState.SignalName.JoyLayoutChanged, _onJoyLayoutChanged))
        {
            gs.Disconnect(GameState.SignalName.JoyLayoutChanged, _onJoyLayoutChanged);
        }

        if (gs.IsConnected(GameState.SignalName.ResolutionChanged, _onResolutionChanged))
        {
            gs.Disconnect(GameState.SignalName.ResolutionChanged, _onResolutionChanged);
        }

        if (gs.IsConnected(GameState.SignalName.WindowModeChanged, _onWindowModeChanged))
        {
            gs.Disconnect(GameState.SignalName.WindowModeChanged, _onWindowModeChanged);
        }
    }

    // ---------------- 圆盘导航 ----------------

    /// <summary>装配页目录（打开/locale 时重装）。</summary>
    private void RebuildWheelMenu()
    {
        LoadMenu(
            new List<RadialWheelOption>
            {
                new() { Id = PageControls, Label = Tr("SET_CONTROLS"), Glyph = RadialGlyph.Cross },
                new() { Id = PageModes, Label = Tr("SET_MODES"), Glyph = RadialGlyph.Bolt },
                new() { Id = PageAbout, Label = Tr("SET_ABOUT"), Glyph = RadialGlyph.Ring },
            },
            string.Empty);
    }

    private void OnWheelConfirmed(RadialWheelOption option)
    {
        ShowPage(option.Id);
    }

    // ---------------- 控制（改键） ----------------

    /// <summary>内容页统一包滚动容器：面板最大高度限制后，超限内容在内容区内滚动而非撑大面板。
    /// ScrollContainer 自动滚动到聚焦子控件（Godot 4 内置 ensure_visible），手柄/键盘焦点链
    /// （全项目模态聚焦约定）不受影响；页内容横向填满、纵向保持自身高度以启用滚动。</summary>
    private ScrollContainer WrapScroll(Control page)
    {
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.AddThemeConstantOverride("scrollbar_margin", 4);
        UITheme.ApplyMetalScrollBar(scroll.GetVScrollBar());
        page.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(page);
        return scroll;
    }

    private VBoxContainer BuildControlsPage()
    {
        var page = new VBoxContainer();
        // 行距收紧：12 行 + 恢复默认 + 规则说明要在内容视口（≈690px）内完整收纳，免滚动折叠
        page.AddThemeConstantOverride("separation", 4);
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
            rebindButton.CustomMinimumSize = new Vector2(110.0f, 36.0f);
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
        // 常驻改键规则说明（locale 重建时随页重建，无需单独刷新）
        page.AddChild(UITheme.MakeLabel(Tr("SET_REBIND_DESC"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Left));
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

    /// <summary>进入按键捕获态（重绑行按钮点击入口；Esc 由 _UnhandledKeyInput 取消）。</summary>
    public void StartCapture(StringName action)
    {
        _capturingAction = action;
        _hintLabel.Text = GdFormat.Format(Tr("SET_CAPTURE"), Tr("ACT_" + action.ToString().ToUpper()));
    }

    private void CancelCapture()
    {
        _hintLabel.Text = Tr("SET_CANCELLED");
        _capturingAction = new StringName();
    }


    public void Back()
    {
        OnBackPressed();
    }

    /// <summary>对外公开接口：BackNavigator 决策查询改键捕获态</summary>
    public StringName CapturingAction()
    {
        return _capturingAction;
    }



    private void OnResetKeys()
    {
        GameState.Instance.ResetKeyBindings();
        _hintLabel.Text = Tr("SET_RESET_DONE");
    }

    /// <summary>捕获态右键撤销走 _Input（先于 GUI/动作消费）：面板与按钮 MouseFilter=STOP 会吞掉
    /// 落在其上的右键，挂 _UnhandledInput 时右键点在面板内取消失灵。固定 UI 手势先手——
    /// 即便未来右键被绑进任何动作/输入映射，此处也优先保证撤销可达。</summary>
    public override void _Input(InputEvent @event)
    {
        if (!Visible || _capturingAction == new StringName())
        {
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        {
            CancelCapture();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || _capturingAction == new StringName())
        {
            return;
        }

        // 手柄 B（ui_cancel）在捕获态同样取消捕获——BackNavigator 对捕获态放行不消费，
        // 事件会传到本节点；只处理 InputEventKey 会使手柄 B 按下无人消费（唯一 B 失灵的界面态）
        if (@event.IsActionPressed("ui_cancel"))
        {
            CancelCapture();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            // Esc 由上方 ui_cancel 取消分支消费，此处不处理 KEY_ESCAPE——ui_cancel 不在
            // REBINDABLE_ACTIONS，捕获态下必先命中取消分支并 return。
            // 捕获对齐 GetActionKeycodes 的双键回退语义——非标准布局/
            // IME 键的 Keycode 为 Key.None 时裸绑定 0（KEY_NONE）致该动作永久无法触发
            var kc = key.Keycode != Key.None ? (int)key.Keycode : (int)key.PhysicalKeycode;
            // 双键回退后仍为 Key.None（RebindAction 无校验）：取消捕获不写绑定，防动作永久失效
            if (kc == (int)Key.None)
            {
                CancelCapture();
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
        // 难度档位（影响敌方数值/得分倍率，随设置持久化）
        page.AddChild(UITheme.MakeSectionHeader(Tr("SET_DIFFICULTY")));
        var diffRow = new HBoxContainer();
        diffRow.AddThemeConstantOverride("separation", 16);
        page.AddChild(diffRow);
        _diffButtons.Clear();
        foreach (var d in GameState.Instance.DIFFICULTY_ORDER)
        {
            var db = UITheme.MakeToggleButton(Tr("DIFF_" + d.ToString().ToUpper()), _diffGroup);
            db.CustomMinimumSize = new Vector2(120.0f, 48.0f);
            db.Pressed += () => GameState.Instance.SetDifficulty(d); // SetDifficulty 内部落盘
            diffRow.AddChild(db);
            _diffButtons[d] = Variant.From(db);
        }

        // 流程：默认跳过入场动画（开启后开机直达标题屏；只在下次启动生效）
        _skipIntroBtn = UITheme.MakeToggleButton(Tr("SET_SKIP_INTRO"), new ButtonGroup { AllowUnpress = true });
        _skipIntroBtn.CustomMinimumSize = new Vector2(280.0f, 48.0f);
        _skipIntroBtn.Pressed += OnSkipIntro;
        page.AddChild(_skipIntroBtn);
        page.AddChild(UITheme.MakeLabel(Tr("SET_SKIP_INTRO_DESC"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Left));
        // 开火方式（鼠标左键：按住连发 / 按一下切换）
        page.AddChild(UITheme.MakeSectionHeader(Tr("SET_FIRE")));
        var firePair = MakeModeRow(page, Tr("SET_FIRE_MODE"), _fireGroup);
        _fireHold = firePair[0];
        _fireToggle = firePair[1];
        _fireHold.Pressed += () => OnFireMode(false);
        _fireToggle.Pressed += () => OnFireMode(true);
        page.AddChild(UITheme.MakeLabel(Tr("SET_FIRE_MODE_DESC"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Left));
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

        // 机制说明：准星入标记框 → 出膛弹追踪该敌；档位调节框大小与追踪速度
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

        // 窗口模式（窗口化 / 无边框全屏）
        var modeRow = new HBoxContainer();
        modeRow.AddThemeConstantOverride("separation", 16);
        page.AddChild(modeRow);
        var modeLabel = UITheme.MakeLabel(Tr("SET_WINDOW_MODE"), UITheme.FontBody, UITheme.Text, HorizontalAlignment.Left);
        modeLabel.CustomMinimumSize = new Vector2(140.0f, 0.0f);
        modeRow.AddChild(modeLabel);
        _modeButtons.Clear();
        foreach (var mode in new[] { new StringName("windowed"), new StringName("borderless") })
        {
            var b = UITheme.MakeToggleButton(Tr("SET_WINDOW_MODE_" + mode.ToString().ToUpper()), _modeGroup);
            b.CustomMinimumSize = new Vector2(210.0f, 48.0f);
            b.Pressed += () => GameState.Instance.SetWindowMode(mode);
            modeRow.AddChild(b);
            _modeButtons[mode] = Variant.From(b);
        }

        // 渲染分辨率（预设档；超出当前屏的档位不列出——避免 1080p 屏出现 4K 无意义项）
        var resRow = new HBoxContainer();
        resRow.AddThemeConstantOverride("separation", 16);
        page.AddChild(resRow);
        var resLabel = UITheme.MakeLabel(Tr("SET_RESOLUTION"), UITheme.FontBody, UITheme.Text, HorizontalAlignment.Left);
        resLabel.CustomMinimumSize = new Vector2(140.0f, 0.0f);
        resRow.AddChild(resLabel);
        var resFlow = new HFlowContainer();
        resFlow.AddThemeConstantOverride("h_separation", 10);
        resFlow.AddThemeConstantOverride("v_separation", 8);
        resFlow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        resRow.AddChild(resFlow);
        _resolutionButtons.Clear();
        foreach (var preset in GameState.Instance.RESOLUTION_ORDER)
        {
            if (!ResolutionFitsScreen(preset))
            {
                continue;
            }

            var size = GameState.Instance.RESOLUTION_LEVELS[preset].AsVector2I();
            var b = UITheme.MakeToggleButton($"{size.X}×{size.Y}", _resolutionGroup);
            b.CustomMinimumSize = new Vector2(150.0f, 48.0f);
            b.Pressed += () => GameState.Instance.SetResolution(preset);
            resFlow.AddChild(b);
            _resolutionButtons[preset] = Variant.From(b);
        }

        page.AddChild(UITheme.MakeLabel(Tr("SET_WINDOW_MODE_DESC"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Left));
        _resolutionInfoLabel = UITheme.MakeLabel("", UITheme.FontCaption, UITheme.AccentGold, HorizontalAlignment.Left);
        page.AddChild(_resolutionInfoLabel);
        page.AddChild(UITheme.MakeLabel(Tr("SET_RESOLUTION_DESC"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Left));

        // 性能：帧率上限（六档）+ 垂直同步
        page.AddChild(UITheme.MakeSectionHeader(Tr("SET_PERFORMANCE")));
        var fpsRow = new HBoxContainer();
        fpsRow.AddThemeConstantOverride("separation", 14);
        page.AddChild(fpsRow);
        var fpsLabel = UITheme.MakeLabel(Tr("SET_FPS_CAP"), UITheme.FontBody, UITheme.Text, HorizontalAlignment.Left);
        fpsLabel.CustomMinimumSize = new Vector2(140.0f, 0.0f);
        fpsRow.AddChild(fpsLabel);
        _fpsButtons.Clear();
        foreach (var level in FpsCapOrder)
        {
            var b = UITheme.MakeToggleButton(Tr("SET_FPS_" + level.ToString().ToUpper()), _fpsGroup);
            b.CustomMinimumSize = new Vector2(96.0f, 48.0f);
            b.Pressed += () => GameState.Instance.SetFpsCap(level);
            fpsRow.AddChild(b);
            _fpsButtons[level] = Variant.From(b);
        }

        page.AddChild(UITheme.MakeLabel(Tr("SET_FPS_CAP_DESC"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Left));
        // 垂直同步（关闭可降输入延迟，配合帧率上限；开启时实际帧率再受显示器刷新率钳制）
        var vsyncGroup = new ButtonGroup { AllowUnpress = true };
        _vsyncBtn = UITheme.MakeToggleButton(Tr("SET_VSYNC"), vsyncGroup);
        _vsyncBtn.CustomMinimumSize = new Vector2(200.0f, 48.0f);
        _vsyncBtn.Pressed += OnVSync;
        page.AddChild(_vsyncBtn);
        page.AddChild(UITheme.MakeLabel(Tr("SET_VSYNC_DESC"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Left));
        // 鼠标锁定窗口内（MouseTrap：窗口聚焦期间鼠标移出内容区即被拉回，防止准星失控；失焦放行）
        var lockGroup = new ButtonGroup { AllowUnpress = true };
        _mouseLockBtn = UITheme.MakeToggleButton(Tr("SET_MOUSE_LOCK"), lockGroup);
        _mouseLockBtn.CustomMinimumSize = new Vector2(200.0f, 48.0f);
        _mouseLockBtn.Pressed += OnMouseLock;
        page.AddChild(_mouseLockBtn);
        page.AddChild(UITheme.MakeLabel(Tr("SET_MOUSE_LOCK_DESC"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Left));
        // 手柄：右摇杆瞄准灵敏度 + 摇杆死区（InputMap 全局 deadzone）
        page.AddChild(UITheme.MakeSectionHeader(Tr("SET_JOY")));
        // PS 布局适配：按已连接手柄显示布局与按钮标签对照（Xbox A/B/X/Y vs PS ✕/○/□/△）
        _joyLayoutLabel = UITheme.MakeLabel("", UITheme.FontCaption, UITheme.AccentGold, HorizontalAlignment.Left);
        page.AddChild(_joyLayoutLabel);
        RefreshJoyLayoutLabel();
        MakeJoySlider(
            page,
            Tr("SET_JOY_AIM_SPEED"),
            200.0f,
            4000.0f,
            (float)GameState.Instance.JoyAimSpeed,
            "%.0f",
            v => GameState.Instance.SetJoyAimSpeed(v)
        );
        MakeJoySlider(
            page,
            Tr("SET_JOY_DEADZONE"),
            5.0f,
            90.0f,
            (float)(GameState.Instance.JoyDeadzone * 100.0),
            "%.0f%%",
            v => GameState.Instance.SetJoyDeadzone(v / 100.0)
        );
        page.AddChild(UITheme.MakeLabel(Tr("SET_JOY_DESC"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Left));
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
        // 画面：世界层增强（辉光/色彩分级/晕影）开关——关闭走逐元素发光回退路径（低配机）
        page.AddChild(UITheme.MakeSectionHeader(Tr("SET_VIDEO")));
        var wpfRow = new HBoxContainer();
        wpfRow.AddThemeConstantOverride("separation", 16);
        page.AddChild(wpfRow);
        var wpfGroup = new ButtonGroup { AllowUnpress = true };
        _worldPostFxBtn = UITheme.MakeToggleButton(Tr("SET_WORLD_POST_FX"), wpfGroup);
        _worldPostFxBtn.CustomMinimumSize = new Vector2(200.0f, 48.0f);
        _worldPostFxBtn.Pressed += OnWorldPostFx;
        wpfRow.AddChild(_worldPostFxBtn);
        page.AddChild(UITheme.MakeLabel(Tr("SET_WORLD_POST_FX_DESC"), UITheme.FontCaption, UITheme.TextDim, HorizontalAlignment.Left));
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

    /// <summary>手柄参数滑杆行（标题 + HSlider + 数值标签；value_changed 实时回调并更新数值显示）</summary>
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
            // 键盘焦点链方向键调整只走 ValueChanged（仅 DragEnded 落盘会在正常退出
            // 靠 SaveSettings 兜底，进程异常终止则丢失调整值）——滑杆调整频率低，写盘直接可接受
            GameState.Instance.PersistJoySettings();
        };
        // 拖动结束同样持久化（与 ValueChanged 并存，拖动场景双保险）
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

    /// <summary>开关/按钮组选中态从 GameState 全量刷新（开页与切语言共用一套序列）。</summary>
    private void RefreshToggleStates()
    {
        _ctrlHold.SetPressedNoSignal(!GameState.Instance.CtrlToggleMode);
        _ctrlToggle.SetPressedNoSignal(GameState.Instance.CtrlToggleMode);
        _shiftHold.SetPressedNoSignal(!GameState.Instance.ShiftToggleMode);
        _shiftToggle.SetPressedNoSignal(GameState.Instance.ShiftToggleMode);
        _fireHold.SetPressedNoSignal(!GameState.Instance.FireToggleMode);
        _fireToggle.SetPressedNoSignal(GameState.Instance.FireToggleMode);
        RefreshZoomButtons();
        RefreshWindowModeButtons();
        RefreshResolutionButtons();
        RefreshDiffButtons();
        RefreshAimButtons();
        _reduceFlashBtn.SetPressedNoSignal(GameState.Instance.ReduceFlash);
        _worldPostFxBtn.SetPressedNoSignal(GameState.Instance.WorldPostFx);
        RefreshFpsButtons();
        _vsyncBtn.SetPressedNoSignal(GameState.Instance.VSync);
        _mouseLockBtn.SetPressedNoSignal(GameState.Instance.MouseLock);
        _skipIntroBtn.SetPressedNoSignal(GameState.Instance.SkipIntro);
    }

    /// <summary>打开面板并刷新选中态；opener 为打开者（开始/暂停面板），返回时恢复其可见</summary>
    public void ShowSettings(CanvasLayer? openerLayer)
    {
        _opener = openerLayer;
        RefreshRebindRows();
        RefreshLangButtons();
        RefreshToggleStates();
        _hintLabel.Text = "";
        _capturingAction = new StringName();
        ShowPage(PageControls);
        RebuildWheelMenu();
        Wheel.FocusOption(0); // 开页聚焦「控制」与默认页对齐：默认弧面中点槽停在「操作模式」（聚焦/面板读法冲突）
        Visible = true;
        SetWheelActive(true, dimActive: false); // 本页遮罩由 page shell 提供
        PlayWheelEntrance();
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

    private void RefreshWindowModeButtons()
    {
        foreach (var mode in _modeButtons.Keys)
        {
            ((Button)_modeButtons[mode].AsGodotObject()).SetPressedNoSignal(mode.AsStringName() == GameState.Instance.WindowMode);
        }

        RefreshResolutionButtons();
    }

    /// <summary>分辨率档与信息行刷新：custom 档时全部不选中（AllowUnpress）；无边框全屏/自定义时
    /// 在信息行显示实际生效尺寸，窗口化预设档时清空信息行。</summary>
    private void RefreshResolutionButtons()
    {
        var resolution = GameState.Instance.Resolution;
        foreach (var preset in _resolutionButtons.Keys)
        {
            ((Button)_resolutionButtons[preset].AsGodotObject()).SetPressedNoSignal(preset.AsStringName() == resolution);
        }

        var borderless = GameState.Instance.WindowMode == new StringName("borderless");
        var size = GameState.Instance.ResolutionPointSize();
        if (borderless)
        {
            _resolutionInfoLabel.Text = Tr("SET_RESOLUTION_DESKTOP");
        }
        else if (resolution == new StringName("custom") || !_resolutionButtons.ContainsKey(resolution))
        {
            // custom 或当前档位被显示器尺寸过滤掉（换到更小屏）：信息行显示实际尺寸
            _resolutionInfoLabel.Text = GdFormat.Format(Tr("SET_RESOLUTION_CUSTOM"), size.X, size.Y);
        }
        else
        {
            _resolutionInfoLabel.Text = "";
        }

        // 无边框全屏下分辨率档不适用（用桌面分辨率）：禁用按钮并保留选中态显示
        foreach (var preset in _resolutionButtons.Keys)
        {
            ((Button)_resolutionButtons[preset].AsGodotObject()).Disabled = borderless;
        }
    }

    /// <summary>预设档是否适配当前显示器（物理像素比较：逻辑尺寸 × 屏幕缩放 ≤ 可用区）。
    /// headless 或无窗口时一律放行（无头探针/CI 需要完整档位表）。</summary>
    private static bool ResolutionFitsScreen(StringName preset)
    {
        if (DisplayServer.GetName() == "headless")
        {
            return true;
        }

        var size = GameState.Instance.RESOLUTION_LEVELS[preset].AsVector2I();
        var screen = DisplayServer.WindowGetCurrentScreen();
        if (screen < 0)
        {
            return true;
        }

        var scale = DisplayServer.ScreenGetScale(screen);
        var usable = DisplayServer.ScreenGetUsableRect(screen).Size;
        return (int)(size.X * scale) <= usable.X && (int)(size.Y * scale) <= usable.Y;
    }

    private void RefreshDiffButtons()
    {
        foreach (var d in _diffButtons.Keys)
        {
            ((Button)_diffButtons[d].AsGodotObject()).SetPressedNoSignal(d.AsStringName() == GameState.Instance.Difficulty);
        }
    }

    private void RefreshAimButtons()
    {
        foreach (var level in _aimButtons.Keys)
        {
            ((Button)_aimButtons[level].AsGodotObject()).SetPressedNoSignal(level.AsStringName() == GameState.Instance.AimAssistLevel);
        }
    }

    private void RefreshFpsButtons()
    {
        foreach (var level in _fpsButtons.Keys)
        {
            ((Button)_fpsButtons[level].AsGodotObject()).SetPressedNoSignal(level.AsStringName() == GameState.Instance.FpsCap);
        }
    }

    private void OnLocaleChanged()
    {
        // _pages 空（locale_changed 早于 _ready）时防御，必须先于任何节点访问执行——
        // _titleLabel 等节点在 _ready 前为 null，守卫在后会先空引用崩溃
        if (_pages.Count == 0)
        {
            return;
        }

        _titleLabel.Text = Tr("SET_TITLE");
        RebuildWheelMenu();
        _backButton.Text = Tr("SET_BACK");
        _resetButton.Text = Tr("SET_RESET");
        _versionLabel.Text = GdFormat.Format(Tr("SET_VERSION"), Engine.GetVersionInfo()["string"].AsString());
        _cheatsheetLabel.Text = Tr("SET_CHEATSHEET");
        RefreshLangButtons();
        RefreshNavLabels();
        // 重建前记录当前页并恢复——否则无条件跳回「控制」页；
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
        // Free() 同步删除——QueueFree 帧末才删，同帧 add_child 新旧页并存闪一帧
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
        // 重建后归还焦点——旧按钮已销毁，焦点丢失使
        // 键盘 Tab 循环与手柄方向键导航中断（对齐 show_settings 的 grab_focus 约定）
        ((Button)_navButtons[current].AsGodotObject()).GrabFocus();
        // 操作模式按钮选中态刷新
        RefreshToggleStates();
    }

    /// <summary>首个内容页的父容器（= shell content VBox；取 _pages 首个值的父容器）。</summary>
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

    private void OnFireMode(bool toggleMode)
    {
        GameState.Instance.SetFireToggleMode(toggleMode);
    }

    private void OnReduceFlash()
    {
        GameState.Instance.SetReduceFlash(_reduceFlashBtn.ButtonPressed);
    }

    private void OnWorldPostFx()
    {
        GameState.Instance.SetWorldPostFx(_worldPostFxBtn.ButtonPressed);
    }

    private void OnVSync()
    {
        GameState.Instance.SetVSync(_vsyncBtn.ButtonPressed);
    }

    private void OnMouseLock()
    {
        GameState.Instance.SetMouseLock(_mouseLockBtn.ButtonPressed);
    }

    private void OnSkipIntro()
    {
        GameState.Instance.SetSkipIntro(_skipIntroBtn.ButtonPressed);
    }

    private void OnBackPressed()
    {
        _capturingAction = new StringName();
        Visible = false;
        SetWheelActive(false, dimActive: false);
        if (_opener != null && GodotObject.IsInstanceValid(_opener))
        {
            _opener.Visible = true;
            // 焦点还给打开者主按钮：键盘/手柄链路不因进出设置页而断
            // typed 分派（打开者 = 暂停面板 PauseUi，有 GrabPrimaryFocus）
            if (_opener is PauseUi p)
            {
                p.GrabPrimaryFocus();
            }
        }

        _opener = null;
        EmitSignal(SignalName.BackPressed);
    }
}
