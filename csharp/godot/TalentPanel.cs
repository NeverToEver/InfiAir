using Godot;
using InfiAir.Core;
using InfiAir.Core.Talent;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 天赋缓存面板（天赋缓存系统重构，2026-09-07，替代旧 BuffSelect 三选一弹窗）：
/// 左右分栏——左 35% 为 RadialWheel 路径导航（左缘 1/4 弧，大类→支线→节点下钻，仅指示不承载加点），
/// 右 65% 为平铺区（根层 = 大类概览；下钻后 = TalentFanView 树状扇形）+ 节点详情加点卡 + 底部经济栏
/// （有效缓存/衰减警示/路线契约/重置代币/风险加点/专注折扣/互斥警告）。
/// 开合编排（减少硬切割裂感）：进入需蓄力——按住 G/按住 HUD 指示器（talent.panel.charge_time，
/// HUD 底部进度条，松开/受击/其他模态打断即取消），满格后 dim 淡入 → 轮盘滑入（轻微过冲）→
/// 标题/读数/概览卡逐级 stagger → 底栏上浮收尾；退出反序加速（内容先走、dim 最后收），
/// 完成才恢复对局。概览↔扇形切换同款「旧内容退场 → 新内容进场」编排；详情卡右侧滑入。
/// Esc/右键经 BackNavigator 路由关闭（动画版）；CloseNow 为即时关闭端口（跳过退场编排）。
/// 打开时暂停对局（与 PauseUI/BaseConsole 同款模态语义）；process_mode=Always（场景内配置，
/// 暂停中 tween 照常推进，HoloBoot 先例）。
/// </summary>
public partial class TalentPanel : CanvasLayer
{
    private Main _main = null!;
    private Node2D _wheelHolder = null!;
    private RadialWheel _wheel = null!;
    private ColorRect _dim = null!;
    private Control _rightRoot = null!;
    private Label _titleLabel = null!;
    private Label _cacheLabel = null!;
    private Label _cacheHintLabel = null!;
    private HBoxContainer _overviewBox = null!;
    private TalentFanView _fan = null!;
    private ChamferedPanel _detail = null!;
    private Label _detailName = null!;
    private Label _detailCaption = null!;
    private Label _detailLevel = null!;
    private Label _detailDesc = null!;
    private Label _detailEffect = null!;
    private Label _detailStatus = null!;
    private Button _upgradeButton = null!;
    private Button _overchargeButton = null!;
    private ChamferedPanel _footer = null!;
    private HBoxContainer _footerBox = null!;
    private StringName? _selectedNode;
    private string? _category;

    // ---- 蓄力进入（缓速语义：满格才进）----
    private bool _charging;
    private float _chargeT;
    private double _chargeStartHealth;
    private float _chargeDuration = 0.55f;

    // ---- 开合编排 ----
    private bool _closing;
    private Tween? _viewTween;

    private readonly Callable _onLocaleChanged;
    private readonly Callable _onTalentsChanged;
    private readonly Callable _onCacheChanged;

    /// <summary>右区几何（1080p 设计坐标；UI 不走 world_scale）。</summary>
    private const float RightLeft = 560f;
    private const float FanTop = 170f;
    private const float FanHeight = 720f;

    /// <summary>轮盘圆心（holder 静止位；入场/退场动画位移加在 holder 上，与轮盘自身视差解耦）。
    /// y=480 与端点角钳 36°（RadialWheel.SlotAngleFor）同口径：卡片不压左下生命 HUD 区。</summary>
    private static readonly Vector2 WheelRest = new(-160f, 480f);

    public TalentPanel()
    {
        _onLocaleChanged = Callable.From(OnLocaleChanged);
        _onTalentsChanged = Callable.From(RefreshAll);
        _onCacheChanged = Callable.From<double, int>(OnCacheChanged);
    }

    public override void _Ready()
    {
        Visible = false;
        _main = GetParent<Main>();
        _chargeDuration = Mathf.Max((float)GameState.Instance.Cfg("talent.panel.charge_time", 0.55).AsDouble(), CfgFx.IntervalFloor); // H15：=0 除零
        BuildDim();
        BuildWheel();
        BuildRightArea();
        var gs = GameState.Instance;
        if (gs != null)
        {
            if (!gs.IsConnected(GameState.SignalName.LocaleChanged, _onLocaleChanged))
            {
                gs.Connect(GameState.SignalName.LocaleChanged, _onLocaleChanged);
            }

            if (!gs.IsConnected(GameState.SignalName.TalentsChanged, _onTalentsChanged))
            {
                gs.Connect(GameState.SignalName.TalentsChanged, _onTalentsChanged);
            }

            if (!gs.IsConnected(GameState.SignalName.TalentCacheChanged, _onCacheChanged))
            {
                gs.Connect(GameState.SignalName.TalentCacheChanged, _onCacheChanged);
            }
        }
    }

    public override void _ExitTree()
    {
        // C22 模式：显式断开 GameState 信号连接（对齐 PauseUi/Hud）
        var gs = GameState.Instance;
        if (gs == null)
        {
            return;
        }

        if (gs.IsConnected(GameState.SignalName.LocaleChanged, _onLocaleChanged))
        {
            gs.Disconnect(GameState.SignalName.LocaleChanged, _onLocaleChanged);
        }

        if (gs.IsConnected(GameState.SignalName.TalentsChanged, _onTalentsChanged))
        {
            gs.Disconnect(GameState.SignalName.TalentsChanged, _onTalentsChanged);
        }

        if (gs.IsConnected(GameState.SignalName.TalentCacheChanged, _onCacheChanged))
        {
            gs.Disconnect(GameState.SignalName.TalentCacheChanged, _onCacheChanged);
        }
    }

    // ---------------- 蓄力进入（缓速：满格才进） ----------------

    /// <summary>蓄力开面板（按住 G / 按住 HUD 指示器）：进度条走 HUD 底部（提前离舰同款视觉），
    /// 松开/受击/其他模态打断即取消。满格在 _Process 内自动 Open。</summary>
    public void BeginCharge()
    {
        if (_charging || Visible || _closing || !CanOpen())
        {
            return;
        }

        _charging = true;
        _chargeT = 0f;
        _chargeStartHealth = GameState.Instance.Health;
        _main.Hud().SetCharge(Hud.ChargeChannel.TalentPanel, 0f);
        _main.Hud().SetCacheChipCharging(true);
    }

    /// <summary>触发源松开（G 回弹 / 指示器左键松开或移出）：蓄力中则取消。</summary>
    public void NotifyTriggerReleased()
    {
        if (_charging)
        {
            CancelCharge();
        }
    }

    public void CancelCharge()
    {
        if (!_charging)
        {
            return;
        }

        _charging = false;
        _main.Hud().SetCharge(Hud.ChargeChannel.TalentPanel, -1f);
        _main.Hud().SetCacheChipCharging(false);
    }

    /// <summary>蓄力中（HUD 呼吸指示与缓存芯片提亮联动读取）。</summary>
    public bool IsCharging => _charging;

    public override void _Process(double delta)
    {
        if (!_charging)
        {
            return;
        }

        // 打断守卫：对局被其他模态暂停 / 死亡结算 / 开场返航过场抢占 / 蓄力期间受击
        var interrupted = GetTree().Paused
            || Visible
            || _main.IsGameOver()
            || _main.IsIntroPlaying()
            || _main.IsReturnPlaying()
            || GameState.Instance.Health < _chargeStartHealth;
        if (interrupted)
        {
            CancelCharge();
            return;
        }

        _chargeT += (float)delta;
        if (_chargeT >= _chargeDuration)
        {
            _charging = false;
            _main.Hud().SetCharge(Hud.ChargeChannel.TalentPanel, -1f);
            _main.Hud().SetCacheChipCharging(false);
            Open();
        }
        else
        {
            _main.Hud().SetCharge(Hud.ChargeChannel.TalentPanel, _chargeT / _chargeDuration);
        }
    }

    // ---------------- 开关 ----------------

    public bool CanOpen() =>
        Visible == false
        && !_closing
        && !GetTree().Paused
        && !_main.IsGameOver()
        && !_main.IsIntroPlaying()
        && !_main.IsReturnPlaying()
        && !_main.IsHomecoming();

    /// <summary>打开（同步置态 + 异步入场编排）。生产入口 = 蓄力满格；亦可直调。</summary>
    public void Open()
    {
        if (!CanOpen())
        {
            return;
        }

        GetTree().Paused = true;
        Visible = true;
        _selectedNode = null;
        RebuildWheelOptions();
        ApplyView(false);
        RefreshAll();
        PlayEntrance();
    }

    /// <summary>关闭（动画版，生产入口：G/Esc/右键）：内容反序加速退场，dim 最后收，
    /// 完成后才恢复对局——避免「元素未走完就露出对局」的二次割裂。</summary>
    public void Close() => BeginClose();

    /// <summary>即时关闭（诊断端口）：跳过退场编排。</summary>
    public void CloseNow() => FinishClose();

    private void BeginClose()
    {
        if (!Visible || _closing)
        {
            return;
        }

        _closing = true;
        CancelCharge();
        PlayExit();
    }

    private void FinishClose()
    {
        _closing = false;
        Visible = false;
        GetTree().Paused = false;
    }

    /// <summary>G 键（talent_panel）：按住蓄力（松开取消）、满格进入；打开态按 G 动画关闭。
    /// 暂停态守卫覆盖 开场/返航过场/基地/暂停/设置（均持树暂停），死亡结算单独判。</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("talent_panel"))
        {
            if (_closing)
            {
                return;
            }

            if (Visible)
            {
                BeginClose();
            }
            else if (CanOpen())
            {
                BeginCharge();
            }
            else
            {
                return;
            }

            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionReleased("talent_panel"))
        {
            NotifyTriggerReleased();
        }
    }

    // ---------------- 入场 / 退场编排（层次：dim → 轮盘 → 标题 → 内容 stagger → 底栏） ----------------

    /// <summary>同节点互斥 tween（meta 持旧引用先杀——BuffSelect hover tween 先例），
    /// 入场/退场/视图切换对同一节点不叠写。</summary>
    private static Tween SwapTween(Node node)
    {
        if (node.HasMeta("anim_tween"))
        {
            ((Tween)node.GetMeta("anim_tween").AsGodotObject()).Kill();
        }

        var tween = node.CreateTween();
        node.SetMeta("anim_tween", Variant.From(tween));
        return tween;
    }

    private void PlayEntrance()
    {
        // 初始态 + 分层进场：行业惯例 = 背景先行建立空间，主元素带轻微过冲滑入，
        // 内容逐级 stagger（ease-out 减速入位），底栏自下而上收尾
        _dim.Modulate = new Color(1f, 1f, 1f, 0f);
        SwapTween(_dim).TweenProperty(_dim, "modulate:a", 1.0f, 0.22);

        _wheelHolder.Position = new Vector2(WheelRest.X - 620f, WheelRest.Y);
        SwapTween(_wheelHolder).TweenProperty(_wheelHolder, "position", WheelRest, 0.55)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        _titleLabel.Modulate = new Color(_titleLabel.Modulate, 0f);
        _titleLabel.Position = new Vector2(40f, 70f);
        var titleTw = SwapTween(_titleLabel);
        titleTw.TweenInterval(0.10);
        titleTw.TweenProperty(_titleLabel, "modulate:a", 1.0f, 0.3);
        titleTw.Parallel().TweenProperty(_titleLabel, "position", new Vector2(40f, 46f), 0.3)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);

        _cacheLabel.Modulate = new Color(_cacheLabel.Modulate, 0f);
        _cacheHintLabel.Modulate = new Color(_cacheHintLabel.Modulate, 0f);
        var cacheTw = SwapTween(_cacheLabel);
        cacheTw.TweenInterval(0.18);
        cacheTw.TweenProperty(_cacheLabel, "modulate:a", 1.0f, 0.26);
        var hintTw = SwapTween(_cacheHintLabel);
        hintTw.TweenInterval(0.24);
        hintTw.TweenProperty(_cacheHintLabel, "modulate:a", 1.0f, 0.26);

        StaggerContent(_overviewBox, enter: true, delay: 0.14f);

        _footer.Modulate = new Color(_footer.Modulate, 0f);
        _footer.Position = new Vector2(0f, 996f);
        var footerTw = SwapTween(_footer);
        footerTw.TweenInterval(0.3);
        footerTw.TweenProperty(_footer, "modulate:a", 1.0f, 0.32);
        footerTw.Parallel().TweenProperty(_footer, "position", new Vector2(0f, 950f), 0.32)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    private void PlayExit()
    {
        // 反序加速退场（ease-in）：内容先走、轮盘随后、dim 最后收；总时长 ~0.42s
        if (_overviewBox.Visible)
        {
            StaggerContent(_overviewBox, enter: false, delay: 0f);
        }

        if (_fan.Visible)
        {
            var fanTw = SwapTween(_fan);
            fanTw.TweenProperty(_fan, "modulate:a", 0.0f, 0.18).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        }

        var titleTw = SwapTween(_titleLabel);
        titleTw.TweenInterval(0.04);
        titleTw.TweenProperty(_titleLabel, "modulate:a", 0.0f, 0.16);
        var cacheTw = SwapTween(_cacheLabel);
        cacheTw.TweenProperty(_cacheLabel, "modulate:a", 0.0f, 0.16);
        var hintTw = SwapTween(_cacheHintLabel);
        hintTw.TweenProperty(_cacheHintLabel, "modulate:a", 0.0f, 0.16);

        var footerTw = SwapTween(_footer);
        footerTw.TweenProperty(_footer, "modulate:a", 0.0f, 0.2);
        footerTw.Parallel().TweenProperty(_footer, "position", new Vector2(0f, 996f), 0.2)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);

        var wheelTw = SwapTween(_wheelHolder);
        wheelTw.TweenInterval(0.05);
        wheelTw.TweenProperty(_wheelHolder, "position", new Vector2(WheelRest.X - 620f, WheelRest.Y), 0.3)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);

        var dimTw = SwapTween(_dim);
        dimTw.TweenInterval(0.1);
        dimTw.TweenProperty(_dim, "modulate:a", 0.0f, 0.28);

        var master = CreateTween();
        master.TweenInterval(0.42);
        master.TweenCallback(Callable.From(FinishClose));
    }

    /// <summary>概览卡组分层进场/退场（HBox 容器管理 position——只动 modulate + scale，
    /// pivot 已随 Resized 设为中心；enter = 后进者晚 55ms 减速入位，exit = 反序加速离场）。</summary>
    private void StaggerContent(HBoxContainer box, bool enter, float delay)
    {
        var cards = new List<Control>();
        foreach (var child in box.GetChildren())
        {
            if (child is Control c && c.Visible)
            {
                cards.Add(c);
            }
        }

        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[enter ? i : cards.Count - 1 - i];
            var offset = delay + i * 0.055f;
            var tw = SwapTween(card);
            if (enter)
            {
                card.Modulate = new Color(card.Modulate, 0f);
                card.Scale = new Vector2(0.92f, 0.92f);
                tw.TweenInterval(offset);
                tw.TweenProperty(card, "modulate:a", 1.0f, 0.3);
                tw.Parallel().TweenProperty(card, "scale", Vector2.One, 0.3)
                    .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            }
            else
            {
                tw.TweenInterval(offset);
                tw.TweenProperty(card, "modulate:a", 0.0f, 0.16).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
                tw.Parallel().TweenProperty(card, "scale", new Vector2(0.94f, 0.94f), 0.16)
                    .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
            }
        }
    }

    // ---------------- 视图切换（概览 ↔ 扇形：旧内容退场 → 新内容进场，不硬切） ----------------

    /// <summary>轮盘层深 ↔ 右区视图（即时版：Open 初始装配用）。</summary>
    private void SyncRightView() => ApplyView(_wheel.Depth >= 2 && _category != null);

    /// <summary>下钻/回退的编排版：旧内容退场 → 中点切换装配 → 新内容进场。</summary>
    private void TransitionRightView(bool toFan)
    {
        if (_viewTween != null)
        {
            _viewTween.Kill();
            _viewTween = null;
        }

        Control? oldRoot = toFan ? _overviewBox : _fan;
        if (oldRoot.Visible)
        {
            if (oldRoot == _overviewBox)
            {
                StaggerContent(_overviewBox, enter: false, delay: 0f);
            }
            else
            {
                var fanTw = SwapTween(_fan);
                fanTw.TweenProperty(_fan, "modulate:a", 0.0f, 0.14).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
            }
        }

        _viewTween = CreateTween();
        _viewTween.TweenInterval(0.15);
        _viewTween.TweenCallback(Callable.From(() =>
        {
            ApplyView(toFan);
            EnterView(toFan);
        }));
    }

    /// <summary>视图状态装配（可见性 + 内容重建；不含动画）。</summary>
    private void ApplyView(bool fan)
    {
        _overviewBox.Visible = !fan;
        _fan.Visible = fan;
        _detail.Visible = false;
        if (fan && _category != null)
        {
            _fan.SetCategory(_category);
            _fan.SetSelected(_selectedNode);
        }
        else
        {
            RebuildOverview();
        }
    }

    /// <summary>视图内容进场：扇形 = 整区上浮淡入；概览 = 卡片 stagger（Scale+Fade）。</summary>
    private void EnterView(bool fan)
    {
        if (fan)
        {
            _fan.Modulate = new Color(_fan.Modulate, 0f);
            _fan.Position = new Vector2(24f, FanTop + 26f);
            var tw = SwapTween(_fan);
            tw.TweenProperty(_fan, "modulate:a", 1.0f, 0.26);
            tw.Parallel().TweenProperty(_fan, "position", new Vector2(0f, FanTop), 0.26)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }
        else
        {
            StaggerContent(_overviewBox, enter: true, delay: 0.02f);
        }
    }

    private void OnWheelConfirmed(RadialWheelOption option) => SelectNode(option.Id);

    private void OnWheelDrilled(RadialWheelOption option)
    {
        if (_wheel.Depth == 2)
        {
            _category = option.Id;
        }

        TransitionRightView(toFan: true);
    }

    private void OnWheelBacked()
    {
        if (_wheel.Depth == 1)
        {
            _category = null;
            _selectedNode = null;
        }

        TransitionRightView(toFan: false);
    }

    private void OnFanNodeActivated(StringName nodeId) => SelectNode(nodeId.ToString());

    /// <summary>联动（2.4）：右侧节点悬停 → 左侧轮盘对应卡亮起（当前层含该节点时）。</summary>
    private void OnFanNodeHovered(string? nodeId)
    {
        if (nodeId == null)
        {
            _wheel.HighlightOption(-1);
            return;
        }

        for (var i = 0; i < _wheel.CurrentCount; i++)
        {
            var opt = _wheel.CurrentOption(i);
            if (opt != null && opt.Id == nodeId)
            {
                _wheel.HighlightOption(i);
                return;
            }
        }

        _wheel.HighlightOption(-1);
    }

    private void SelectNode(string nodeId)
    {
        _selectedNode = nodeId;
        _fan.SetSelected(nodeId);
        RefreshDetail();
    }

    // ---------------- 构建 ----------------

    private void BuildDim()
    {
        _dim = new ColorRect { Color = UITheme.DimBg };
        _dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_dim);
    }

    private void BuildWheel()
    {
        // holder 承载入场/退场位移：轮盘自身 _Process 视差会写自己的 Position，两者解耦
        _wheelHolder = new Node2D { Position = WheelRest };
        _wheel = new RadialWheel { BackLabel = Tr("TALENT_BACK") };
        // 混合页（右区面板含焦点控件：升级/超载按钮与概览卡）：轮盘不接管方向键，
        // 留给页面焦点链（轮盘 _Input 先于 GUI 相位，不关会抢走整页键盘导航）
        _wheel.KeyboardEnabled = false;
        _wheelHolder.AddChild(_wheel);
        AddChild(_wheelHolder);
        _wheel.Confirmed += OnWheelConfirmed;
        _wheel.Drilled += OnWheelDrilled;
        _wheel.Backed += OnWheelBacked;
    }

    private void BuildRightArea()
    {
        _rightRoot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _rightRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _rightRoot.OffsetLeft = RightLeft;
        AddChild(_rightRoot);

        // 顶部：标题 + 缓存读数
        _titleLabel = UITheme.MakeLabel(Tr("TALENT_TITLE"), UITheme.FontTitle, UITheme.AccentGold);
        _titleLabel.Position = new Vector2(40f, 46f);
        _rightRoot.AddChild(_titleLabel);
        var titleLine = new ColorRect { Color = UITheme.AccentGold, CustomMinimumSize = new Vector2(96f, 3f), Position = new Vector2(42f, 104f) };
        titleLine.MouseFilter = Control.MouseFilterEnum.Ignore;
        _rightRoot.AddChild(titleLine);

        _cacheLabel = UITheme.MakeLabel("", UITheme.FontScore, UITheme.Accent, HorizontalAlignment.Right);
        _cacheLabel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _cacheLabel.OffsetLeft = -560f;
        _cacheLabel.OffsetTop = 46f;
        _cacheLabel.OffsetRight = -40f;
        _rightRoot.AddChild(_cacheLabel);

        _cacheHintLabel = UITheme.MakeLabel("", UITheme.FontSmall, UITheme.WarnYellow, HorizontalAlignment.Right);
        _cacheHintLabel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _cacheHintLabel.OffsetLeft = -560f;
        _cacheHintLabel.OffsetTop = 92f;
        _cacheHintLabel.OffsetRight = -40f;
        _rightRoot.AddChild(_cacheHintLabel);

        // 大类概览（轮盘根层）
        _overviewBox = new HBoxContainer();
        _overviewBox.AddThemeConstantOverride("separation", 28);
        _overviewBox.Alignment = BoxContainer.AlignmentMode.Center;
        _overviewBox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overviewBox.OffsetTop = 150f;
        _overviewBox.OffsetBottom = -180f;
        _rightRoot.AddChild(_overviewBox);

        // 树状扇形（下钻后）
        _fan = new TalentFanView
        {
            Position = new Vector2(0f, FanTop),
            Size = new Vector2(900f, FanHeight),
            Visible = false,
        };
        _rightRoot.AddChild(_fan);
        _fan.NodeActivated += OnFanNodeActivated;
        _fan.NodeHovered += OnFanNodeHovered;

        BuildDetail();
        BuildFooter();
    }

    private void BuildDetail()
    {
        _detail = new ChamferedPanel
        {
            Position = new Vector2(910f, 200f),
            Size = new Vector2(360f, 600f),
            Brackets = true,
            Visible = false,
        };
        _detail.Resized += () => _detail.PivotOffset = _detail.Size / 2f;
        _rightRoot.AddChild(_detail);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        _detail.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        margin.AddChild(vbox);

        _detailName = UITheme.MakeLabel("", UITheme.FontHeader, UITheme.Accent);
        vbox.AddChild(_detailName);
        _detailCaption = UITheme.MakeLabel("", UITheme.FontSmall, UITheme.TextDim);
        vbox.AddChild(_detailCaption);

        _detailLevel = UITheme.MakeLabel("", UITheme.FontBody, UITheme.AccentGold);
        vbox.AddChild(_detailLevel);

        _detailDesc = UITheme.MakeLabel("", UITheme.FontSmall, UITheme.TextDim);
        _detailDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detailDesc.CustomMinimumSize = new Vector2(0f, 84f);
        vbox.AddChild(_detailDesc);

        var divider = new ColorRect { Color = UITheme.AccentDim, CustomMinimumSize = new Vector2(0f, 1f) };
        vbox.AddChild(divider);

        _detailEffect = UITheme.MakeLabel("", UITheme.FontHud, UITheme.Text);
        _detailEffect.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(_detailEffect);

        var spacer = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        vbox.AddChild(spacer);

        _detailStatus = UITheme.MakeLabel("", UITheme.FontSmall, UITheme.Danger);
        _detailStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(_detailStatus);

        _upgradeButton = UITheme.MakeButton("", true);
        _upgradeButton.Pressed += OnUpgradePressed;
        vbox.AddChild(_upgradeButton);

        _overchargeButton = UITheme.MakeButton("", false);
        _overchargeButton.AddThemeColorOverride("font_color", UITheme.AccentGold);
        _overchargeButton.Pressed += OnUpgradePressed; // 同一入口：服务侧按上限档自动走风险加点
        vbox.AddChild(_overchargeButton);
    }

    private void BuildFooter()
    {
        _footer = new ChamferedPanel
        {
            Position = new Vector2(0f, 950f),
            Size = new Vector2(1300f, 84f),
            Padding = 0f,
        };
        _rightRoot.AddChild(_footer);
        _footerBox = new HBoxContainer();
        _footerBox.AddThemeConstantOverride("separation", 26);
        _footerBox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _footerBox.OffsetLeft = 22f;
        _footerBox.OffsetRight = -22f;
        _footerBox.Alignment = BoxContainer.AlignmentMode.Center;
        _footer.AddChild(_footerBox);
    }

    private void RebuildWheelOptions()
    {
        var roots = new List<RadialWheelOption>();
        foreach (var cat in TalentTree.Categories)
        {
            var lines = new List<RadialWheelOption>();
            foreach (var line in cat.Lines)
            {
                var leaves = new List<RadialWheelOption>();
                foreach (var nodeId in line.NodeIds)
                {
                    leaves.Add(new RadialWheelOption
                    {
                        Id = nodeId,
                        Label = Tr($"AUG_{nodeId.ToUpperInvariant()}_NAME"),
                        Glyph = GlyphForCategory(cat.Id),
                    });
                }

                lines.Add(new RadialWheelOption { Id = line.Id, Label = Tr(line.NameKey), Glyph = GlyphForLine(line.Id), Children = leaves });
            }

            roots.Add(new RadialWheelOption { Id = cat.Id, Label = Tr(cat.NameKey), Glyph = GlyphForCategory(cat.Id), Children = lines });
        }

        _category = null;
        _selectedNode = null;
        _wheel.BackLabel = Tr("TALENT_BACK");
        _wheel.Load(roots, RadialWheel.SlotAngleFor(roots.Count));
    }

    private static RadialGlyph GlyphForCategory(string categoryId) => categoryId switch
    {
        "offense" => RadialGlyph.Triangle,
        "defense" => RadialGlyph.Hex,
        "mobility" => RadialGlyph.Chevron,
        _ => RadialGlyph.Star,
    };

    private static RadialGlyph GlyphForLine(string lineId) => lineId switch
    {
        "gunnery" => RadialGlyph.Diamond,
        "firecontrol" => RadialGlyph.Ring,
        "ordnance" => RadialGlyph.Bolt,
        "armor" => RadialGlyph.Diamond,
        "vitality" => RadialGlyph.Ring,
        "assault" => RadialGlyph.Bolt,
        "field" => RadialGlyph.Cross,
        _ => RadialGlyph.Diamond,
    };

    private void RebuildOverview()
    {
        foreach (var child in _overviewBox.GetChildren())
        {
            child.Free();
        }

        var talent = GameState.Instance.Talent;
        foreach (var cat in TalentTree.Categories)
        {
            var panel = new ChamferedPanel
            {
                CustomMinimumSize = new Vector2(280f, 540f),
                Brackets = true,
                FocusMode = Control.FocusModeEnum.All,
            };
            panel.Resized += () => panel.PivotOffset = panel.Size / 2f;
            var catId = cat.Id;
            panel.GuiInput += ev => OnOverviewCardGuiInput(ev, catId);

            var margin = new MarginContainer();
            margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            margin.AddThemeConstantOverride("margin_left", 20);
            margin.AddThemeConstantOverride("margin_right", 20);
            margin.AddThemeConstantOverride("margin_top", 26);
            margin.AddThemeConstantOverride("margin_bottom", 20);
            panel.AddChild(margin);

            var vbox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            vbox.AddThemeConstantOverride("separation", 14);
            margin.AddChild(vbox);

            var invested = 0;
            var totalLevels = 0;
            foreach (var line in cat.Lines)
            {
                foreach (var nodeId in line.NodeIds)
                {
                    invested += talent.Level(nodeId);
                    totalLevels += talent.CapFor(nodeId);
                }
            }

            var firstId = new StringName(cat.Lines[0].NodeIds[0]);
            // 定宽包裹：MakeAugmentSocket 自身是自增自适应 ChamferedPanel，直接作为 Fill 布局子项时
            // 「外板变宽 → socket 撑满 → socket 最小尺寸棘轮回写 → 外板更宽」正反馈撑爆整行概览卡
            var socketWrap = new Control { CustomMinimumSize = new Vector2(64.0f, 64.0f), MouseFilter = Control.MouseFilterEnum.Ignore };
            var socket = UITheme.MakeAugmentSocket(firstId, 64.0f);
            socket.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            socketWrap.AddChild(socket);
            vbox.AddChild(socketWrap);
            vbox.AddChild(UITheme.MakeLabel(Tr(cat.NameKey), UITheme.FontHeader, AugmentIcons.ColorFor(firstId), HorizontalAlignment.Center));
            vbox.AddChild(UITheme.MakeLabel(GdFormat.Format(Tr("TALENT_CAT_INVESTED_FMT"), invested, totalLevels), UITheme.FontBody, UITheme.AccentGold, HorizontalAlignment.Center));
            var hintWrap = new Control { CustomMinimumSize = new Vector2(224.0f, 72.0f), MouseFilter = Control.MouseFilterEnum.Ignore };
            var hint = UITheme.MakeLabel(Tr("TALENT_CAT_DRILL_HINT"), UITheme.FontSmall, UITheme.TextDim, HorizontalAlignment.Center);
            // 中文长句按任意断点换行；外层定宽容器兜底——ChamferedPanel 自适应「只放大不缩小」，
            // 标签首帧未换行的最小宽度会把整行概览卡撑出屏（实测 280 → 380+，第 4 卡溢出）
            hint.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
            hint.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            hintWrap.AddChild(hint);
            vbox.AddChild(hintWrap);

            _overviewBox.AddChild(panel);
        }
    }

    private void OnOverviewCardGuiInput(InputEvent @event, string categoryId)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            DrillToCategory(categoryId);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>概览卡点击 → 轮盘下钻到对应大类（左 ↔ 右联动的反向路径）。</summary>
    private void DrillToCategory(string categoryId)
    {
        for (var i = 0; i < _wheel.CurrentCount; i++)
        {
            var opt = _wheel.CurrentOption(i);
            if (opt is { Id: { } id } && id == categoryId)
            {
                _wheel.TestDrill(i);
                return;
            }
        }
    }

    // ---------------- 详情与操作 ----------------

    private void OnUpgradePressed()
    {
        if (_selectedNode == null)
        {
            return;
        }

        if (GameState.Instance.TalentUpgrade(_selectedNode))
        {
            RefreshAll();
        }
    }

    private void OnCacheChanged(double _effective, int _raw) => RefreshCacheReadout();

    /// <summary>全量刷新（开面板/加点/路线变化/locale）：标题读数 + 右区 + 详情 + 底栏。</summary>
    private void RefreshAll()
    {
        RefreshCacheReadout();
        if (_wheel.Depth >= 2 && _category != null)
        {
            _fan.RefreshStates();
        }
        else
        {
            RebuildOverview();
        }

        RefreshDetail();
        RefreshFooter();
    }

    private void RefreshCacheReadout()
    {
        var talent = GameState.Instance.Talent;
        _cacheLabel.Text = GdFormat.Format(Tr("TALENT_CACHE_FMT"), talent.EffectiveCacheText, talent.RawCache);
        _cacheLabel.AddThemeColorOverride("font_color",
            talent.RawCache > talent.Config.SafeThreshold ? UITheme.WarnYellow : UITheme.Accent);
        _cacheHintLabel.Text = talent.RawCache > talent.Config.SafeThreshold
            ? Tr("TALENT_CACHE_DECAYING")
            : Tr("TALENT_CACHE_HINT");
    }

    private void RefreshDetail()
    {
        var talent = GameState.Instance.Talent;
        var drilled = _wheel.Depth >= 2 && _category != null;
        if (_selectedNode == null || !drilled)
        {
            _detail.Visible = false;
            return;
        }

        var def = TalentTree.Find(_selectedNode.ToString());
        if (def == null)
        {
            _detail.Visible = false;
            return;
        }

        var idSn = new StringName(_selectedNode);
        var wasHidden = !_detail.Visible;
        _detail.Visible = true;
        if (wasHidden)
        {
            // 详情卡从右滑入（不做对称退场——内容切换时直接替换更干净）
            _detail.Modulate = new Color(_detail.Modulate, 0f);
            _detail.Position = new Vector2(946f, 200f);
            var tw = SwapTween(_detail);
            tw.TweenProperty(_detail, "modulate:a", 1.0f, 0.2);
            tw.Parallel().TweenProperty(_detail, "position", new Vector2(910f, 200f), 0.2)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }

        _detailName.Text = Tr($"AUG_{_selectedNode.ToString().ToUpperInvariant()}_NAME");
        _detailCaption.Text = GdFormat.Format(Tr("TALENT_PATH_FMT"), Tr(TalentTree.Category(def.CategoryId).NameKey), Tr(TalentTree.Line(def.LineId).NameKey));
        var level = talent.Level(idSn);
        var cap = talent.CapFor(idSn);
        var maxLevel = talent.MaxLevel(idSn);
        _detailLevel.Text = GdFormat.Format(Tr("TALENT_LV_FULL_FMT"), level, cap, maxLevel);
        _detailDesc.Text = Tr($"AUG_{_selectedNode.ToString().ToUpperInvariant()}_DESC");

        // 效果读数：乘算节点给「当前累计 → 下一级预估」（5.2 UI 要求）
        var factorV = GameState.Instance.Cfg($"augments.{_selectedNode}.factor", 0.0);
        var hasFactor = factorV.VariantType is Variant.Type.Int or Variant.Type.Float && factorV.AsDouble() > 0.0;
        if (hasFactor)
        {
            var factor = factorV.AsDouble();
            var effNow = talent.EffLevel(idSn);
            var effNext = TalentEconomy.EffectiveLevel(talent.Config, level + 1, talent.Softcap(idSn), talent.RouteCoreFor(idSn), talent.FocusDiscounted(idSn), talent.FocusOver());
            _detailEffect.Text = GdFormat.Format(Tr("TALENT_EFFECT_POW_FMT"), Math.Pow(factor, effNow), Math.Pow(factor, effNext));
        }
        else
        {
            _detailEffect.Text = level > 0 ? Tr("TALENT_EFFECT_ACTIVE") : Tr("TALENT_EFFECT_INACTIVE");
        }

        var reason = talent.UpgradeBlockReason(idSn);
        var cost = talent.NextCost(idSn);
        var isOvercharge = talent.IsOverchargePurchase(idSn);
        if (reason == "OVERCHARGED")
        {
            _detailStatus.Text = Tr("TALENT_STATUS_OVERCHARGED");
            _upgradeButton.Visible = false;
            _overchargeButton.Visible = false;
        }
        else if (reason == "PREREQ")
        {
            var prev = TalentTree.Prerequisite(_selectedNode.ToString());
            _detailStatus.Text = prev != null
                ? GdFormat.Format(Tr("TALENT_STATUS_PREREQ_FMT"), Tr($"AUG_{prev.ToUpperInvariant()}_NAME"))
                : "";
            _upgradeButton.Visible = false;
            _overchargeButton.Visible = false;
        }
        else if (reason == "OVERCHARGE_LIMIT")
        {
            _detailStatus.Text = Tr("TALENT_STATUS_OVERCHARGE_LIMIT");
            _upgradeButton.Visible = false;
            _overchargeButton.Visible = false;
        }
        else if (reason == "CACHE")
        {
            _detailStatus.Text = "";
            _upgradeButton.Visible = !isOvercharge;
            _upgradeButton.Text = GdFormat.Format(Tr("TALENT_UPGRADE_FMT"), cost);
            _upgradeButton.Disabled = true;
            _overchargeButton.Visible = isOvercharge;
            _overchargeButton.Text = GdFormat.Format(Tr("TALENT_OVERCHARGE_FMT"), cost);
            _overchargeButton.Disabled = true;
        }
        else
        {
            _detailStatus.Text = cap < maxLevel && level >= cap
                ? Tr("TALENT_STATUS_SEALED")
                : isOvercharge ? Tr("TALENT_STATUS_OVERCHARGE_WARNING") : "";
            _upgradeButton.Visible = !isOvercharge;
            _upgradeButton.Text = GdFormat.Format(Tr("TALENT_UPGRADE_FMT"), cost);
            _upgradeButton.Disabled = false;
            _overchargeButton.Visible = isOvercharge;
            _overchargeButton.Text = GdFormat.Format(Tr("TALENT_OVERCHARGE_FMT"), cost);
            _overchargeButton.Disabled = false;
            _upgradeButton.GrabFocus();
        }
    }

    private void RefreshFooter()
    {
        foreach (var child in _footerBox.GetChildren())
        {
            child.Free();
        }

        var talent = GameState.Instance.Talent;
        _footerBox.AddChild(MakeFooterChip(GdFormat.Format(Tr("TALENT_FOOTER_CACHE_FMT"), talent.EffectiveCacheText, talent.RawCache), UITheme.Accent));
        if (talent.RawCache > talent.Config.SafeThreshold)
        {
            _footerBox.AddChild(MakeFooterChip(Tr("TALENT_FOOTER_DECAY_FMT"), UITheme.WarnYellow));
        }

        var routeName = "";
        foreach (var route in TalentTree.Routes)
        {
            if (route.Id == talent.Route)
            {
                routeName = Tr(route.NameKey);
            }
        }

        _footerBox.AddChild(MakeFooterChip(
            routeName == "" ? Tr("TALENT_FOOTER_ROUTE_NONE") : GdFormat.Format(Tr("TALENT_FOOTER_ROUTE_FMT"), routeName),
            routeName == "" ? UITheme.TextDim : UITheme.AccentGold));
        _footerBox.AddChild(MakeFooterChip(GdFormat.Format(Tr("TALENT_FOOTER_TOKEN_FMT"), talent.ResetTokens), UITheme.AccentGold));
        _footerBox.AddChild(MakeFooterChip(GdFormat.Format(Tr("TALENT_FOOTER_OVERCHARGE_FMT"), talent.OverchargeUsed, talent.Config.OverchargeMaxPerRun), UITheme.Text));
        if (talent.FocusOver() > 0)
        {
            var penalty = Math.Min(talent.Config.FocusPenaltyCap, talent.Config.FocusPenaltyPerLevel * talent.FocusOver());
            _footerBox.AddChild(MakeFooterChip(GdFormat.Format(Tr("TALENT_FOOTER_FOCUS_FMT"), (int)Math.Round(penalty * 100)), UITheme.Danger));
        }

        if (talent.MutexReductionFor(new StringName("offense")) > 0 || talent.MutexReductionFor(new StringName("defense")) > 0)
        {
            _footerBox.AddChild(MakeFooterChip(Tr("TALENT_FOOTER_MUTEX"), UITheme.Danger));
        }
    }

    private Label MakeFooterChip(string text, Color color)
    {
        var label = UITheme.MakeLabel(text, UITheme.FontCaption, color);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        return label;
    }

    // ---------------- locale ----------------

    private void OnLocaleChanged()
    {
        _titleLabel.Text = Tr("TALENT_TITLE");
        _wheel.BackLabel = Tr("TALENT_BACK");
        if (Visible)
        {
            RebuildWheelOptions();
            SyncRightView();
            RefreshAll();
        }
    }
}
