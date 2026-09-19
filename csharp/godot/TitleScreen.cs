using Godot;
using InfiAir.Core.Progression;
using InfiAir.Core.Storage;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 标题屏：程序化星空 + 远景实况战场（敌机编队/远处爆炸/Boss 剪影，
/// 见 Warzone 部分）+ 玩家机自远处跃迁飞入悬挂展示（轮廓背光/尾焰怠速，见 ShipDisplay 部分）+
/// 左侧标题区。开场演出全程不阻塞输入，任意时刻可开局。
/// 输入路由不变：任意键/点击 → main.tscn 开局；T → tutorial.tscn；Esc 不消费。
/// 0.5s 输入守卫：上一场景残留按键不误触发开局。
/// </summary>
public partial class TitleScreen : CanvasLayer
{
    private const ulong InputGuardMs = 500;

    // 开局确认动效：延迟即切场景等待时长（保持迅捷），ReduceFlash 时只留极轻缩放、不提亮
    private const float ConfirmDelay = 0.2f;
    private const float ConfirmZoom = 1.05f;
    private const float ConfirmZoomReduced = 1.015f;

    // 标题屏深空底色：色板 token 均为半透明面板底（BgDeep α0.92）且更偏暖，无同值不透明底色，故单源于此
    private static readonly Color TitleBgColor = new(0.020f, 0.018f, 0.015f);

    // 悬挂展示位与跃迁远点（1920×1080 设计坐标：机体停驻右三分之一，自右上远处飞入）
    private static readonly Vector2 ShipAnchorPos = new(1360.0f, 470.0f);
    private static readonly Vector2 ShipFarPos = new(1560.0f, 230.0f);

    /// <summary>标题屏就绪的真实时刻：输入守卫计时基准（残留在输入队列/玩家手里的上一场景按键，
    /// 属现实世界窗口，故用真实时间而非模拟时间，见 DESIGN_BASELINE「时间基准」）。</summary>
    private ulong _readyMs;
    /// <summary>开局/进教程一次性守卫：同帧多个按下事件（键+点击、T+其他键）会各触发一次
    /// ChangeSceneToFile（deferred 双倍执行），且 T 与其他键同帧时目的地由后调用者覆盖</summary>
    private bool _started;

    // 确认动效引用的标题构件（BuildTitleUi 装配，确认时提亮/缩放）
    private TextureRect? _logo;
    private ColorRect? _accentLine;
    private Label? _pressHint;

    /// <summary>存在本局存档（_Ready 缓存：标题屏 UI 与输入路由共用）。</summary>
    private bool _hasSave;

    /// <summary>练习设置面板（P 打开；打开期间标题屏不再消费按键——否则面板上按任意键会直接开新局）。</summary>
    private PracticePanel? _practicePanel;

    /// <summary>机型选择面板（M 打开；与练习面板同一条「打开期间标题屏不消费按键」的守卫）。</summary>
    private MachinePanel? _machinePanel;

    /// <summary>底部「教程」入口按钮（手柄可聚焦：dpad/摇杆移动焦点、A 确认，键盘 T 与点击照旧）。</summary>
    private Button _tutorialEntry = null!;

    /// <summary>底部「练习」入口按钮（同上；键盘 P 与点击照旧）。</summary>
    private Button _practiceEntry = null!;

    /// <summary>底部「机型」入口按钮（同上；键盘 M 与点击照旧）。</summary>
    private Button _machineEntry = null!;

    /// <summary>当前聚焦的入口（"tutorial" / "practice" / "machine"；无焦点空串）——探针断手柄导航的落位。</summary>
    public string FocusedEntryName()
    {
        var owner = GetViewport()?.GuiGetFocusOwner();
        if (owner == _tutorialEntry)
        {
            return "tutorial";
        }

        if (owner == _practiceEntry)
        {
            return "practice";
        }

        return owner == _machineEntry ? "machine" : "";
    }

    /// <summary>练习设置面板是否展开（探针断「手柄确认能进练习入口」）。</summary>
    public bool PracticePanelOpen() => _practicePanel != null;

    /// <summary>机型选择面板是否展开（探针断「手柄确认能进机型入口」）。</summary>
    public bool MachinePanelOpen() => _machinePanel != null;

    public override void _Ready()
    {
        // 固定标记：开机交接契约的观测点——标记只在标题屏真正入树时打印，冒烟门禁据此断言
        // 「开机落到了标题屏」。切场景静默失败（路径错/资源缺失）时本行不会执行，无头也能判出来。
        GD.Print("[boot] 标题屏就绪");
        _readyMs = Time.GetTicksMsec();
        // 深空底色 + 程序化星空
        // Starfield._Ready 自置 ZIndex=-10，底色须再低一层否则星点被底色盖住
        var bg = CinematicFx.BgRect(TitleBgColor);
        bg.ZIndex = -20;
        AddChild(bg);
        AddChild(new Starfield());

        BuildWarzone();
        BuildShipDisplay();
        BuildTitleUi();

        // 世界层画面增强（layer=1，独立 CanvasLayer 于标题内容之上）：整屏辉光/分级/晕影，
        // 与正局同款视觉语言；黑场淡出在其后入树，保证淡出覆盖增强层
        AddChild(new WorldPostFx());

        // 黑场淡出：掩盖主场景 → 标题屏的场景硬切（0.5s）
        var fadeIn = CinematicFx.BgRect(new Color(0.0f, 0.0f, 0.0f, 1.0f));
        AddChild(fadeIn);
        var fadeTween = fadeIn.CreateTween();
        fadeTween.TweenProperty(fadeIn, "color:a", 0.0f, 0.5).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        fadeTween.TweenCallback(Callable.From(fadeIn.QueueFree));

        // 进标题屏先对一次表，避免停在这里期间文件已被改动却要到下次开机才生效
        GameState.Instance.ReloadBalanceIfChanged();
    }

    /// <summary>数值文件检查间隔（秒）。标题屏是唯一的安全点：此刻没有进行中的一局，
    /// 而下一局进 main 会重建 Spawner/Player/对象池等「启动读一次」的消费者实例——
    /// 局内重载只会得到一半新值一半旧值的世界（理由见 GameState.ReloadBalance）。</summary>
    private const double BalanceCheckInterval = 1.0;

    private double _balanceCheckTimer;

    /// <summary>由 Warzone 的 _Process（本类唯一的逐帧回调）转发进来，避免两个 partial 各写一个 _Process。</summary>
    private void TickBalanceReload(double delta)
    {
        _balanceCheckTimer -= delta;
        if (_balanceCheckTimer > 0.0)
        {
            return;
        }

        _balanceCheckTimer = BalanceCheckInterval;
        GameState.Instance.ReloadBalanceIfChanged();
    }

    private void BuildTitleUi()
    {
        // 标题后方呼吸辉光（原版保留，随标题区左移；外包一层节点做延迟淡入，与呼吸循环不抢同一属性）
        var glowWrap = new Node2D { Position = new Vector2(440.0f, 470.0f), Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f) };
        var glow = CinematicFx.SoftGlow(240.0f, new Color(UITheme.Accent, 0.06f));
        glow.Scale *= new Vector2(1.6f, 0.9f); // 横向拉扁成光带，避免圆形光球感
        glowWrap.AddChild(glow);
        AddChild(glowWrap);
        var glowTween = glow.CreateTween().SetLoops();
        glowTween.TweenProperty(glow, "modulate:a", 0.55f, 1.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        glowTween.TweenProperty(glow, "modulate:a", 1.0f, 1.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        var glowIn = glowWrap.CreateTween();
        glowIn.TweenProperty(glowWrap, "modulate:a", 1.0f, 0.5).SetDelay(1.0).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);

        // 标题区（左三分之一列，竖排居中）
        var vbox = new VBoxContainer
        {
            Position = new Vector2(140.0f, 330.0f),
            Size = new Vector2(600.0f, 330.0f),
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f),
        };
        AddChild(vbox);

        // 标题 logo（离线生成 assets/sprites/ui/logo.png：琥珀渐变字标 + 切角菱形徽章，
        // 生成器 scripts/tools/generate_logo.py）；后方呼吸光带保留
        var title = new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/sprites/ui/logo.png"),
            CustomMinimumSize = new Vector2(560.0f, 162.0f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _logo = title;
        vbox.AddChild(title);

        var accentLine = new ColorRect
        {
            Color = new Color(UITheme.Accent, 0.4f),
            CustomMinimumSize = new Vector2(140.0f, 3.0f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _accentLine = accentLine;
        vbox.AddChild(accentLine);

        var spacer = new Control { CustomMinimumSize = new Vector2(0.0f, 28.0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        vbox.AddChild(spacer);

        var hint = UITheme.MakeLabel((string)Tr("TITLE_PRESS_ANY_KEY"), UITheme.FontBody, UITheme.TextDim, HorizontalAlignment.Center);
        hint.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f); // 落位后（2.2s）才启动闪烁
        _pressHint = hint;
        vbox.AddChild(hint);

        // 本局存档存在（可解析且含本局数据）时：额外一行「按 C 继续上次出击」提示。
        // 暂时读不出（IO/权限）时不显示继续项，但要告警——否则玩家只看到「不能继续」而无从知晓原因，
        // 且旧档未被隔离也未被删除，下次启动可重试。
        if (GameState.Instance.HasRunSave())
        {
            _hasSave = true;
            var contHint = UITheme.MakeLabel((string)Tr("TITLE_CONTINUE"), UITheme.FontBody, UITheme.Accent, HorizontalAlignment.Center);
            contHint.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
            vbox.AddChild(contHint);
            var contIn = contHint.CreateTween();
            contIn.TweenProperty(contHint, "modulate:a", 1.0f, 0.5).SetDelay(1.4).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        }
        else if (GameState.Instance.LastRunLoadStatus == SaveLoadStatus.Unreadable)
        {
            GD.PushWarning("InfiAir: 本局存档暂时不可读——本次不提供继续（旧档未被删除，可稍后重启重试）");
        }

        // 跨局最好成绩一行（口径见 DESIGN_BASELINE §1.16）：无可信记录或尚无成绩时不显示——
        // 空记录与「读不出」都不得写成一行「暂无记录」蒙混过去。
        var best = GameState.Instance.Best;
        if (GameState.Instance.BestKnown && best != BestRecord.Empty)
        {
            vbox.AddChild(UITheme.MakeLabel(
                GdFormat.Format(Tr("BEST_LINE"), BestRecord.FormatArgs(best)),
                UITheme.FontBody, UITheme.TextDim, HorizontalAlignment.Center));
        }

        // 标题块滑入淡入（1.0s 起，与机体飞入并行）
        var titleIn = vbox.CreateTween().SetParallel(true);
        titleIn.TweenProperty(vbox, "modulate:a", 1.0f, 0.5).SetDelay(1.0).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        titleIn.TweenProperty(vbox, "position:x", 140.0f, 0.5).From(104.0f).SetDelay(1.0).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);

        // 底部入口（1.8s 淡入）：教程 / 练习 / 机型并排一行——三处都是「不走本局」的入口，
        // 分开摆会让玩家以为练习是教程的下级
        var hintRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        hintRow.AddThemeConstantOverride("separation", 48);
        hintRow.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        hintRow.OffsetTop = -72.0f;
        hintRow.OffsetBottom = -40.0f;
        hintRow.OffsetLeft = -400.0f;
        hintRow.OffsetRight = 400.0f;
        hintRow.MouseFilter = Control.MouseFilterEnum.Ignore;
        hintRow.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
        // 教程入口的两种走法：有续接检查点＝「继续教程」，否则＝普通入口文案。
        // 未完成时用强调色把入口提出来（首局玩家在标题屏认不出该往哪走——这一步是唯一的引导面）；
        // 已完成且无检查点即回落次级色，不再打扰老玩家。
        var tutorialResume = GameState.Instance.TutorialStage > 0;
        var tutorialHighlighted = tutorialResume || !GameState.Instance.TutorialDone;
        // 两个键都写成字面量 Tr 调用（条件表达式里放键名时文案门禁扫不到，缺键会静默显示键名本身）
        var tutorialHint = tutorialResume ? (string)Tr("TITLE_TUTORIAL_RESUME") : (string)Tr("TITLE_TUTORIAL_HINT");
        _tutorialEntry = MakeEntryButton(tutorialHint, tutorialHighlighted ? UITheme.AccentGold : UITheme.TextDim);
        _tutorialEntry.Pressed += () => StartFromTitle("res://scenes/tutorial.tscn");
        hintRow.AddChild(_tutorialEntry);
        _practiceEntry = MakeEntryButton((string)Tr("TITLE_PRACTICE"), UITheme.TextDim);
        _practiceEntry.Pressed += OpenPracticePanel;
        hintRow.AddChild(_practiceEntry);
        _machineEntry = MakeEntryButton((string)Tr("TITLE_MACHINE"), UITheme.TextDim);
        _machineEntry.Pressed += OpenMachinePanel;
        hintRow.AddChild(_machineEntry);
        AddChild(hintRow);
        var tutIn = hintRow.CreateTween();
        tutIn.TweenProperty(hintRow, "modulate:a", 1.0f, 0.4).SetDelay(1.8).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);

        // 「按任意键」闪烁：演出落位后（2.2s）启动
        var blinkStarter = new Godot.Timer { OneShot = true, WaitTime = 2.2, Autostart = true };
        AddChild(blinkStarter);
        blinkStarter.Timeout += () =>
        {
            var blink = hint.CreateTween().SetLoops();
            blink.TweenProperty(hint, "modulate:a", 0.25f, 0.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            blink.TweenProperty(hint, "modulate:a", 0.9f, 0.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        };
    }

    /// <summary>底部入口按钮：透明底 + 说明字号文字，视觉与原标签一致；可聚焦（手柄 dpad/摇杆移动
    /// 焦点、A 确认），聚焦/悬停提亮成强调色——焦点态必须有可见反馈，否则手柄玩家看不见自己在哪。</summary>
    private static Button MakeEntryButton(string text, Color restColor)
    {
        var button = new Button
        {
            Text = text,
            Flat = true,
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        button.AddThemeFontOverride("font", UITheme.Font);
        button.AddThemeFontSizeOverride("font_size", UITheme.FontCaption);
        button.AddThemeColorOverride("font_color", restColor);
        button.AddThemeColorOverride("font_hover_color", UITheme.AccentGold);
        button.AddThemeColorOverride("font_focus_color", UITheme.AccentGold);
        button.AddThemeColorOverride("font_pressed_color", UITheme.AccentGold);
        // 焦点环走全库同一枚（UITheme）：引擎默认焦点盒是直角细框，与切角语汇不同族
        UITheme.ApplyFocusRing(button);
        return button;
    }

    /// <summary>经入口离开标题屏的统一出口（按钮点击 / 键盘 T / 手柄确认共用）：标输入已处理 +
    /// 置 _started（重复输入挡回）+ 确认动效切场景。练习入口不走这里（开面板不离开标题屏）。</summary>
    private void StartFromTitle(string scenePath)
    {
        GetViewport()?.SetInputAsHandled();
        _started = true;
        StartScene(scenePath);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_started || Time.GetTicksMsec() - _readyMs < InputGuardMs)
        {
            return;
        }

        // 面板（练习 / 机型）打开期间标题屏不消费任何输入：面板自己收 Esc/方向键，但「按任意键开局」若照旧生效，
        // 在面板上敲空格/回车会直接开一局（而不是切那一行选项）。
        if (_practicePanel != null || _machinePanel != null)
        {
            return;
        }

        if (@event.IsActionPressed("ui_cancel"))
        {
            return; // 标题屏无返回目标，Esc 不消费
        }

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            var kc = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
            // 练习入口（P）先判：它**不离开标题屏**（面板关掉后还能按 T/C/任意键），
            // 故不得置 _started——置了就再没有输入进得来（表现为关掉面板后标题屏死住）。
            if (kc == Key.P)
            {
                GetViewport().SetInputAsHandled();
                OpenPracticePanel();
                return;
            }

            // 机型入口（M）与 P 同类：它也不离开标题屏（面板关掉后还能按 T/C/任意键），
            // 故不得置 _started；必须排在「任意键开局」兜底之前，否则 M 会被兜底吃掉。
            if (kc == Key.M)
            {
                GetViewport().SetInputAsHandled();
                OpenMachinePanel();
                return;
            }

            if (kc == Key.T)
            {
                StartFromTitle("res://scenes/tutorial.tscn");
            }
            else if (kc == Key.C && _hasSave)
            {
                // 读取上次存档（仅在存在存档时消费 C；无档时 C 等同「任意键」新局）
                GetViewport().SetInputAsHandled();
                _started = true;
                // 本局不能换机：继续时以存档里的机型为准。Player._Ready 早于 Main 的读档，
                // 故必须在切场景前就落定——否则本局按标题屏的偏好起飞，与 run.json 记的那一型不一致
                // （贴图与乘区都会是「设置里写着 A、飞的是 B」那种不报错的坏法）。
                var runMachine = GameState.Instance.PeekRunMachineId();
                if (!string.IsNullOrEmpty(runMachine))
                {
                    GameState.Instance.SetMachine(runMachine);
                }

                GameState.Instance.PendingLoadRun = true;
                StartScene("res://scenes/main.tscn");
            }
            else
            {
                StartFromTitle("res://scenes/main.tscn");
            }
        }
        else if (@event is InputEventJoypadButton joyButton && joyButton.Pressed)
        {
            // dpad 导航：无焦点时聚焦教程入口（首个入口），有焦点时引擎焦点链已消费（到不了这里）
            if (joyButton.ButtonIndex is JoyButton.DpadUp or JoyButton.DpadDown or JoyButton.DpadLeft or JoyButton.DpadRight)
            {
                FocusFirstEntry();
                return;
            }

            // A 确认聚焦的入口（ui_accept 未必绑手柄 A，不依赖引擎默认——按下即激活，两条路等价）
            if (joyButton.ButtonIndex == JoyButton.A && FocusedEntryName().Length > 0)
            {
                GetViewport()?.SetInputAsHandled();
                ActivateFocusedEntry();
                return;
            }

            // 其余手柄键仍按「任意键开局」处理（手柄玩家不必刻意够入口）
            GetViewport()?.SetInputAsHandled();
            _started = true;
            StartScene("res://scenes/main.tscn");
        }
        else if (@event is InputEventMouseButton { Pressed: true })
        {
            GetViewport().SetInputAsHandled();
            _started = true;
            StartScene("res://scenes/main.tscn");
        }
        else if (@event is InputEventJoypadMotion joyMotion && Mathf.Abs(joyMotion.AxisValue) > 0.6f)
        {
            // 左摇杆导航（ui_* 已装配摇杆轴绑定）：无焦点时聚焦教程入口，有焦点时引擎焦点链已消费；
            // 右摇杆/扳机推过阈值仍能开始（手柄玩家无需刻意够按钮）
            if (joyMotion.Axis is JoyAxis.LeftX or JoyAxis.LeftY)
            {
                FocusFirstEntry();
                return;
            }

            GetViewport()?.SetInputAsHandled();
            _started = true;
            StartScene("res://scenes/main.tscn");
        }
    }

    /// <summary>首个手柄导航事件自动聚焦教程入口（首局玩家的引导面）；已有焦点时不打扰——
    /// 引擎焦点链（dpad/摇杆经 ui_* 绑定）自行移动。</summary>
    private void FocusFirstEntry()
    {
        if (GetViewport()?.GuiGetFocusOwner() == null && GodotObject.IsInstanceValid(_tutorialEntry))
        {
            _tutorialEntry.GrabFocus();
        }
    }

    /// <summary>激活当前聚焦的入口（手柄 A；与按钮点击/键盘 T/P/M 同一处理口）。</summary>
    private void ActivateFocusedEntry()
    {
        var owner = GetViewport()?.GuiGetFocusOwner();
        if (owner == _tutorialEntry)
        {
            StartFromTitle("res://scenes/tutorial.tscn");
        }
        else if (owner == _practiceEntry)
        {
            OpenPracticePanel();
        }
        else if (owner == _machineEntry)
        {
            OpenMachinePanel();
        }
    }

    /// <summary>确认动效 → 延迟切场景：_started 已由调用方置位（重复输入在入口即被挡回），
    /// 延迟内输入不再被消费，切场景仍走 ChangeSceneToFile 原生路径。</summary>
    private void StartScene(string scenePath)
    {
        PlayConfirmFlourish();
        var tw = CreateTween();
        tw.TweenInterval(ConfirmDelay);
        tw.TweenCallback(Callable.From(() => GetTree().ChangeSceneToFile(scenePath)));
    }

    /// <summary>打开练习设置面板（P）：面板自带遮罩与选项循环，确认后经生产单口
    /// <c>GameState.EnterPractice</c> 切到练习场景；取消（Esc）即自关。
    /// _started 不置位——开面板不是离开标题屏，关掉后还能按 T/C/任意键。</summary>
    private void OpenPracticePanel()
    {
        var panel = new PracticePanel();
        _practicePanel = panel;
        panel.StartRequested += setup => GameState.Instance.EnterPractice(setup);
        panel.Closed += () =>
        {
            _practicePanel = null;
            SetEntriesFocusable(true);
        };
        AddChild(panel);
        SetEntriesFocusable(false);
    }

    /// <summary>打开机型选择面板（M）：面板自带遮罩与逐行预览，选定即经生产单口
    /// <c>GameState.SetMachine</c> 落地（不必确认）；取消（Esc）即自关。
    /// _started 不置位——理由同练习面板。</summary>
    private void OpenMachinePanel()
    {
        var panel = new MachinePanel();
        _machinePanel = panel;
        panel.Closed += () =>
        {
            _machinePanel = null;
            SetEntriesFocusable(true);
        };
        AddChild(panel);
        SetEntriesFocusable(false);
    }

    /// <summary>底部入口的可聚焦性（面板打开期间关掉、退场时恢复）。面板与背后的入口同在一个视口里，
    /// 方向键的焦点搜索会跨过遮罩：焦点一旦走出面板落到入口上，回车/ A 就经 GUI 相位激活了它
    /// （面板之上再开一层、或直接切走场景），而标题屏的输入守卫只管自己那条 _UnhandledInput。
    /// 入口的手柄可达性本身不变——它只在模态期间让位。</summary>
    private void SetEntriesFocusable(bool on)
    {
        var mode = on ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
        _tutorialEntry.FocusMode = mode;
        _practiceEntry.FocusMode = mode;
        _machineEntry.FocusMode = mode;
    }

    /// <summary>开局确认：logo 放大回弹 + 受激提亮、标题短线提亮、按任意键提示弹一下。
    /// ReduceFlash 时压掉提亮与放大（无障碍），只留极轻缩放。</summary>
    private void PlayConfirmFlourish()
    {
        var reduce = GameState.Instance.ReduceFlash;
        var peak = reduce ? ConfirmZoomReduced : ConfirmZoom;
        if (_logo != null && GodotObject.IsInstanceValid(_logo))
        {
            _logo.PivotOffset = _logo.Size * 0.5f;
            var tw = _logo.CreateTween();
            tw.TweenProperty(_logo, "scale", new Vector2(peak, peak), 0.1).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            if (!reduce)
            {
                tw.Parallel().TweenProperty(_logo, "modulate", new Color(1.35f, 1.2f, 1.0f), 0.1).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            }

            tw.TweenProperty(_logo, "scale", Vector2.One, 0.12).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            if (!reduce)
            {
                tw.Parallel().TweenProperty(_logo, "modulate", Colors.White, 0.12).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            }
        }

        if (_accentLine != null && GodotObject.IsInstanceValid(_accentLine))
        {
            var baseCol = new Color(UITheme.Accent, 0.4f);
            var tw = _accentLine.CreateTween();
            tw.TweenProperty(_accentLine, "color", reduce ? baseCol : new Color(UITheme.AccentHot, 1.0f), 0.1);
            tw.TweenProperty(_accentLine, "color", baseCol, 0.14);
        }

        if (_pressHint != null && GodotObject.IsInstanceValid(_pressHint))
        {
            UITheme.PunchScale(_pressHint, reduce ? 1.02f : 1.08f, 0.16f);
        }
    }
}
