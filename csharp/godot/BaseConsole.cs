using System.Collections.Generic;
using Godot;
using InfiAir.Core;
using InfiAir.Core.Talent;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 基地控制台（返航中场整备）：
/// 左缘轮盘目录（战机库 / 维修补给 / 路线契约 / 任务规划 + 「继续出击」叶）→
/// 右区单面板内容切换（旧双列五面板一屏堆叠退役；顶部分类芯片行保留键盘/手柄可达性）。
/// 补给面板承载基地↔增幅系统联动：RP 购置「增幅缓存点」（直接入天赋缓存池）与
/// 「超载槽」（本局风险加点上限扩容）。顶部 RP 余额，路线契约 = 机制 C（TalentTree.Routes）。
/// 视觉延续「虚影皮肤」：虚影站背景层 + 全息面板。
/// </summary>
public partial class BaseConsole : RadialMenuLayer
{
    /// <summary>继续出击：返回同一局（Main/Tutorial 经 ResumeRequested 信号连接）。</summary>
    [Signal]
    public delegate void ResumeRequestedEventHandler();

    /// <summary>轮盘目录 id（与根级选项/面板表同键）。</summary>
    private static readonly string[] CategoryIds = { "hangar", "supply", "routes", "missions" };

    private readonly Callable _localeChanged;

    public BaseConsole()
    {
        _localeChanged = Callable.From(OnLocaleChanged);
    }

    private Label _rpLabel = null!;
    private Label _titleLabel = null!;
    private Label _statusLabel = null!;
    private Button _repairButton = null!;
    private Button _rechargeButton = null!;
    private Button _buyCacheButton = null!;
    private Button _buyOverchargeButton = null!;
    private VBoxContainer _routesBox = null!;
    private VBoxContainer _missionsBox = null!;
    private Button _refreshButton = null!; // 任务轮换——刷新任务按钮
    private Label _refreshPointsLabel = null!;
    private Label _refreshHintLabel = null!; // 点数不足提示（临时显示，2s 后隐藏）
    private Godot.Timer? _refreshHintTimer;
    private readonly Dictionary<string, Label> _titleLabels = new();
    private Label _routeHintLabel = null!;
    private readonly Dictionary<string, ChamferedPanel> _pages = new();

    private readonly Dictionary<string, Button> _categoryChips = new();
    private readonly ButtonGroup _chipGroup = new();
    private string _currentCategory = "hangar";
    private Label _categoryLabel = null!;
    private Control _pageHolder = null!;
    private GradientTexture2D? _glowTexture;

    /// <summary>虚影面板底后径向辉光垫（近似毛玻璃）：全面板共享一张径向渐变纹理。</summary>
    private GradientTexture2D MakeGlowTexture()
    {
        if (_glowTexture != null)
        {
            return _glowTexture;
        }

        var gradient = new Gradient();
        gradient.SetColor(0, new Color(UITheme.Accent, 0.12f));
        gradient.SetColor(1, new Color(UITheme.Accent, 0.0f));
        _glowTexture = new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1.0f, 0.5f),
            Width = 128,
            Height = 128,
        };
        return _glowTexture;
    }

    /// <summary>数据抖动装饰：3Hz 正弦 α0.92–1.0 + 每 2.7s 一次 0.06s 的 1px 横向错位闪
    /// （tween 循环，不加 _process；本层 process_mode=Always，暂停态照常播放）。
    /// 页面隐藏时经 VisibleChanged 暂停/恢复（关页后不再空转）。</summary>
    private void ApplyDataFlicker(Label label)
    {
        var tween = CreateTween().SetLoops();
        for (var i = 0; i < 8; i++) // 8 × 0.334s ≈ 2.67s
        {
            tween.TweenProperty(label, "modulate:a", 0.92f, 0.167).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            tween.TweenProperty(label, "modulate:a", 1.0f, 0.167).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }

        tween.TweenProperty(label, "position:x", 1.0f, 0.03);
        tween.TweenInterval(0.03);
        tween.TweenProperty(label, "position:x", 0.0f, 0.0);
        _dataFlickerTweens.Add(tween);
    }

    /// <summary>永续装饰 tween 缓存（页面隐藏期间暂停，防关页空转）。
    /// flicker 每分类页一条（BuildPages 时 MakePanel×4 各建一条），单字段装不下会漏出
    /// 三条不受暂停控制的孤儿循环 tween，故收进列表与 _scanTween 一并接管。</summary>
    private readonly List<Tween> _dataFlickerTweens = new();
    private Tween? _scanTween;

    /// <summary>页面隐藏时暂停装饰 tween、可见时恢复；树暂停期间照常播放
    /// （process_mode=Always，基地页本身开着时树就是暂停的，装饰语义不变）。</summary>
    private void OnVisibleChangedForFx()
    {
        void Apply(Tween? tween)
        {
            if (tween == null || !tween.IsValid())
            {
                return;
            }

            if (Visible)
            {
                tween.Play();
            }
            else
            {
                tween.Pause();
            }
        }

        foreach (var flicker in _dataFlickerTweens)
        {
            Apply(flicker);
        }

        Apply(_scanTween);
    }

    public override void _Ready()
    {
        Visible = false;
        OnVisibleChangedForFx(); // 初始隐藏态：装饰 tween 直接暂停
        var gs = GameState.Instance;
        if (!gs.IsConnected(GameState.SignalName.LocaleChanged, _localeChanged))
        {
            gs.Connect(GameState.SignalName.LocaleChanged, _localeChanged);
        }

        BuildChrome();
        SetContentAnchor(() => _categoryLabel); // 引线锚定「当前分类标题」：面板左缘中点恰与切角面板装饰性中位拼板缝（h*0.5）重合，落入装饰空域
        BuildBackdrop();
        RaiseWheel(); // 背景站体/扫描带在 chrome 之后入树：轮盘保持在其上
        BuildRightArea();
        BuildPages();
        Wheel.Confirmed += OnWheelConfirmed;
        // 混合页（右区面板含焦点控件：修复/充能/购买/领取按钮）：轮盘不接管方向键，
        // 留给页面焦点链（键盘导航已移到 _Input 先 GUI 相位，不关会抢走整页键盘导航）
        Wheel.KeyboardEnabled = false;
        OnVisibleChangedForFx(); // 初始隐藏态：装饰 tween 直接暂停
    }

    public override void _ExitTree()
    {
        // 配对断开——死亡重开场景重载后残留连接在切语言时回调已释放实例
        var gs = GameState.Instance;
        if (gs.IsConnected(GameState.SignalName.LocaleChanged, _localeChanged))
        {
            gs.Disconnect(GameState.SignalName.LocaleChanged, _localeChanged);
        }
    }

    /// <summary>虚影站内部概念背景层：PHANTOM 站体 + 全屏慢扫描带（绘制序在 dim 之后、内容之前）。</summary>
    private void BuildBackdrop()
    {
        var bgWrap = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        bgWrap.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var bgModulate = bgWrap.Modulate;
        bgModulate.A = 0.12f;
        bgWrap.Modulate = bgModulate;
        AddChild(bgWrap);
        var station = DawnStation.Build(DawnStation.Mode.Phantom);
        station.Position = new Vector2(960.0f, 540.0f);
        station.Scale = Vector2.One * 2.0f;
        bgWrap.AddChild(station);
        // 慢扫描带（纯装饰）：尺寸/行程取 viewport 可见区
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var scanH = 140.0f;
        var slowScan = new ColorRect { Color = UITheme.PhantomScan };
        slowScan.MouseFilter = Control.MouseFilterEnum.Ignore;
        slowScan.Size = new Vector2(viewportSize.X, scanH);
        slowScan.Position = new Vector2(0.0f, -scanH);
        AddChild(slowScan);
        var scanTween = CreateTween().SetLoops();
        _scanTween = scanTween;
        scanTween.TweenProperty(slowScan, "position:y", viewportSize.Y, 8.0).SetTrans(Tween.TransitionType.Linear);
        scanTween.TweenProperty(slowScan, "position:y", -scanH, 0.0);
    }

    /// <summary>右区：标题 + RP 余额 + 分类芯片行 + 当前分类标题 + 面板容器。</summary>
    private void BuildRightArea()
    {
        // 自由定位标签不做 ApplyDataFlicker——该动效末段会把 position.x 回写为 0
        // （容器管理布局时被布局覆盖无碍，自由定位标签会被拽到屏左缘，实测基地标题位移）
        _titleLabel = MakeLabel((string)Tr("BASE_TITLE"), 44);
        _titleLabel.Position = new Vector2(560f, 40f);
        _titleLabel.HorizontalAlignment = HorizontalAlignment.Left;
        AddChild(_titleLabel);

        _rpLabel = MakeLabel("", 26);
        _rpLabel.Position = new Vector2(560f, 106f);
        _rpLabel.AddThemeColorOverride("font_color", UITheme.AccentGold);
        _rpLabel.HorizontalAlignment = HorizontalAlignment.Left;
        AddChild(_rpLabel);

        // 分类芯片行（键盘/手柄可达的目录回退入口；与轮盘双向联动）
        var chipRow = new HBoxContainer();
        chipRow.AddThemeConstantOverride("separation", 10);
        chipRow.Position = new Vector2(560f, 152f);
        AddChild(chipRow);
        foreach (var id in CategoryIds)
        {
            var chip = UITheme.MakeToggleButton("", _chipGroup);
            chip.CustomMinimumSize = new Vector2(150.0f, 44.0f);
            chip.AddThemeFontSizeOverride("font_size", UITheme.FontCaption);
            var captured = id;
            chip.Pressed += () => SwitchCategory(captured, animate: true);
            chipRow.AddChild(chip);
            _categoryChips[id] = chip;
        }

        _categoryLabel = MakeLabel("", 28);
        _categoryLabel.Position = new Vector2(560f, 214f);
        _categoryLabel.HorizontalAlignment = HorizontalAlignment.Left;
        AddChild(_categoryLabel);

        // 面板容器（右区满幅；页板在 BuildPages 挂入，单页可见）
        _pageHolder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _pageHolder.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _pageHolder.OffsetLeft = 560f;
        _pageHolder.OffsetTop = 260f;
        _pageHolder.OffsetBottom = -40f;
        AddChild(_pageHolder);
    }

    private void BuildPages()
    {
        _pages["hangar"] = BuildHangar();
        _pages["supply"] = BuildSupply();
        _pages["routes"] = BuildRoutes();
        _pages["missions"] = BuildMissions();
        foreach (var kv in _pages)
        {
            kv.Value.Visible = kv.Key == _currentCategory;
        }

        _categoryLabel.Text = (string)Tr(ChipKey(_currentCategory));
    }

    private Label MakeLabel(string text, int size) => UITheme.MakeLabel(text, size);

    private ChamferedPanel MakePanel(string titleKey, Vector2[][] glyph)
    {
        var panel = new ChamferedPanel();
        UITheme.ApplyPhantomPanel(panel);
        panel.CustomMinimumSize = new Vector2(1260.0f, 0.0f);
        // 面板底后径向辉光垫：随面板尺寸自适应
        var glow = new TextureRect
        {
            Texture = MakeGlowTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ShowBehindParent = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        glow.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        panel.AddChild(glow);
        // 扫描线叠加层：绘于面板底之上、内容之下
        var scan = new BaseConsoleScanlines();
        scan.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        panel.AddChild(scan);
        var vbox = new VBoxContainer { Name = "Body" };
        vbox.AddThemeConstantOverride("separation", 8);
        vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vbox.OffsetLeft = 14.0f;
        vbox.OffsetTop = 14.0f;
        vbox.OffsetRight = -14.0f;
        vbox.OffsetBottom = -14.0f;
        panel.AddChild(vbox);
        // 标题行：16×16 线性发光图标 + section header
        var header = UITheme.MakeSectionHeader((string)Tr(titleKey));
        header.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _titleLabels[titleKey] = (Label)header.GetChild(0);
        ApplyDataFlicker(_titleLabels[titleKey]);
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 8);
        var icon = new BaseConsoleGlyphIcon { Strokes = glyph };
        headerRow.AddChild(icon);
        headerRow.AddChild(header);
        vbox.AddChild(headerRow);
        _pageHolder.AddChild(panel);
        return panel;
    }

    private Button MakeButton(string text)
    {
        var button = UITheme.MakeButton(text);
        button.AddThemeFontSizeOverride("font_size", 20);
        return button;
    }

    private ChamferedPanel BuildHangar()
    {
        // 战机极简折线图标
        var glyph = new Vector2[][]
        {
            new[] { new Vector2(8, 1), new Vector2(13, 14), new Vector2(8, 11), new Vector2(3, 14), new Vector2(8, 1) },
        };
        var panel = MakePanel("BASE_HANGAR", glyph);
        _statusLabel = MakeLabel("", 20);
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Left;
        ((VBoxContainer)panel.GetNode("Body")).AddChild(_statusLabel);
        return panel;
    }

    private ChamferedPanel BuildSupply()
    {
        // 扳手极简折线图标
        var glyph = new Vector2[][]
        {
            new[]
            {
                new Vector2(3, 13),
                new Vector2(9, 7),
                new Vector2(12, 8),
                new Vector2(14, 5),
                new Vector2(12, 3),
                new Vector2(9, 4),
                new Vector2(9, 7),
            },
        };
        var panel = MakePanel("BASE_SUPPLY", glyph);
        var body = (VBoxContainer)panel.GetNode("Body");
        _repairButton = MakeButton("");
        _repairButton.Pressed += OnRepairPressed;
        body.AddChild(_repairButton);
        _rechargeButton = MakeButton("");
        _rechargeButton.Pressed += OnRechargePressed;
        body.AddChild(_rechargeButton);
        // 基地↔增幅联动补给：RP 购置增幅缓存点 / 超载槽
        _buyCacheButton = MakeButton("");
        _buyCacheButton.Pressed += OnBuyCachePressed;
        body.AddChild(_buyCacheButton);
        _buyOverchargeButton = MakeButton("");
        _buyOverchargeButton.Pressed += OnBuyOverchargePressed;
        body.AddChild(_buyOverchargeButton);
        return panel;
    }

    private ChamferedPanel BuildRoutes()
    {
        // 交叉线极简折线图标
        var glyph = new Vector2[][]
        {
            new[] { new Vector2(3, 3), new Vector2(13, 13) },
            new[] { new Vector2(13, 3), new Vector2(3, 13) },
        };
        var panel = MakePanel("BASE_ROUTES", glyph);
        var body = (VBoxContainer)panel.GetNode("Body");
        _routesBox = new VBoxContainer();
        _routesBox.AddThemeConstantOverride("separation", 8);
        body.AddChild(_routesBox);
        var hint = MakeLabel((string)Tr("BASE_ROUTE_HINT"), 16);
        _routeHintLabel = hint;
        body.AddChild(hint);
        return panel;
    }

    private ChamferedPanel BuildMissions()
    {
        // 旗帜极简折线图标
        var glyph = new Vector2[][]
        {
            new[] { new Vector2(4, 2), new Vector2(4, 14) },
            new[] { new Vector2(4, 2), new Vector2(13, 4), new Vector2(4, 8) },
        };
        var panel = MakePanel("BASE_MISSIONS", glyph);
        _missionsBox = new VBoxContainer();
        _missionsBox.AddThemeConstantOverride("separation", 8);
        ((VBoxContainer)panel.GetNode("Body")).AddChild(_missionsBox);
        // 任务轮换：刷新点数 + 刷新按钮 + 点数不足提示
        var refreshRow = new HBoxContainer();
        refreshRow.AddThemeConstantOverride("separation", 10);
        _refreshPointsLabel = MakeLabel("", 18);
        _refreshPointsLabel.AddThemeColorOverride("font_color", UITheme.AccentGold);
        _refreshPointsLabel.CustomMinimumSize = new Vector2(150.0f, 0.0f);
        _refreshPointsLabel.HorizontalAlignment = HorizontalAlignment.Left;
        refreshRow.AddChild(_refreshPointsLabel);
        _refreshButton = MakeButton("");
        _refreshButton.Pressed += OnRefreshPressed;
        refreshRow.AddChild(_refreshButton);
        ((VBoxContainer)panel.GetNode("Body")).AddChild(refreshRow);
        _refreshHintLabel = MakeLabel("", 16);
        _refreshHintLabel.AddThemeColorOverride("font_color", UITheme.Danger);
        _refreshHintLabel.HorizontalAlignment = HorizontalAlignment.Left;
        _refreshHintLabel.Visible = false;
        ((VBoxContainer)panel.GetNode("Body")).AddChild(_refreshHintLabel);
        return panel;
    }

    /// <summary>装配轮盘目录（打开/locale 时重装）。</summary>
    private void RebuildMenu()
    {
        LoadMenu(
            new List<RadialWheelOption>
            {
                new() { Id = "hangar", Label = Tr("BASE_HANGAR"), Glyph = RadialGlyph.Triangle },
                new() { Id = "supply", Label = Tr("BASE_SUPPLY"), Glyph = RadialGlyph.Bolt },
                new() { Id = "routes", Label = Tr("BASE_ROUTES"), Glyph = RadialGlyph.Cross },
                new() { Id = "missions", Label = Tr("BASE_MISSIONS"), Glyph = RadialGlyph.Ring },
                new() { Id = "resume", Label = Tr("BASE_RESUME"), Glyph = RadialGlyph.Star },
            },
            string.Empty);
    }

    private void OnWheelConfirmed(RadialWheelOption option)
    {
        if (option.Id == "resume")
        {
            OnResumePressed();
            return;
        }

        SwitchCategory(option.Id, animate: true);
    }

    /// <summary>目录切换：轮盘确认/芯片行共用；单面板可见 + 轻量入场动效。</summary>
    private void SwitchCategory(string categoryId, bool animate)
    {
        if (!_pages.ContainsKey(categoryId))
        {
            return;
        }

        _currentCategory = categoryId;
        foreach (var kv in _pages)
        {
            kv.Value.Visible = kv.Key == categoryId;
        }

        if (_categoryChips.TryGetValue(categoryId, out var chip))
        {
            chip.SetPressedNoSignal(true);
        }

        _categoryLabel.Text = (string)Tr(ChipKey(categoryId));
        if (animate && _pages.TryGetValue(categoryId, out var page))
        {
            page.Modulate = new Color(page.Modulate, 0f);
            var tw = CreateTween();
            tw.TweenProperty(page, "modulate:a", 1.0f, 0.18);
        }
    }

    private void RefreshChipLabels()
    {
        foreach (var kv in _categoryChips)
        {
            kv.Value.Text = (string)Tr(ChipKey(kv.Key));
            kv.Value.SetPressedNoSignal(kv.Key == _currentCategory);
        }
    }

    private static string ChipKey(string id) => id switch
    {
        "supply" => "BASE_SUPPLY",
        "routes" => "BASE_ROUTES",
        "missions" => "BASE_MISSIONS",
        _ => "BASE_HANGAR",
    };

    /// <summary>进入基地（main 返航结束 / tutorial 过关调用）：发放刷新点数、重绘、全息启动。</summary>
    public void ShowBase()
    {
        // 任务轮换：进基地发放刷新点数（GRANT_PER_VISIT 档位，攒两次基地换一次刷新）
        GameState.Instance.GrantRefreshPoints();
        Refresh();
        Visible = true;
        OnVisibleChangedForFx();
        SetWheelActive(true);
        RebuildMenu();
        Wheel.FocusOption(0); // 开页聚焦「战机库」与默认目录对齐：默认弧面中点槽停在「任务规划」（聚焦/面板读法冲突）
        PlayWheelEntrance();
        SwitchCategory("hangar", animate: false);
        HoloBoot();
    }

    /// <summary>全息启动：当前面板 α0 + scale 0.98→1.0；pivot 设为中心（否则从左上角缩放）。</summary>
    private void HoloBoot()
    {
        if (!_pages.TryGetValue(_currentCategory, out var panel))
        {
            return;
        }

        panel.PivotOffset = panel.Size * 0.5f;
        var modulate = panel.Modulate;
        modulate.A = 0.0f;
        panel.Modulate = modulate;
        panel.Scale = Vector2.One * 0.98f;
        var tween = CreateTween();
        tween.TweenProperty(panel, "modulate:a", 1.0f, 0.25);
        tween.Parallel().TweenProperty(panel, "scale", Vector2.One, 0.25);
    }

    private void Refresh()
    {
        var rp = GameState.Instance.Rp;
        _rpLabel.Text = GdFormat.Format((string)Tr("BASE_RP"), rp);
        var playerV = GameState.Instance.PlayerRef;
        var player = playerV as Player; // Player 走注册表，as 对非 Player/null 均得 null
        // 战机库状态总览
        var augmentText = "";
        var augments = GameState.Instance.Augments;
        foreach (var key in augments.Keys)
        {
            var id = key.AsStringName();
            // 显示名走翻译键（与天赋面板/HUD 明细栏同源），不裸显内部 id
            augmentText += GdFormat.Format("%s×%d  ", (string)Tr($"AUG_{id.ToString().ToUpperInvariant()}_NAME"), augments[key].AsInt32());
        }

        if (augmentText.Length == 0)
        {
            augmentText = (string)Tr("BASE_NO_AUGMENT");
        }

        var fuelPct = 0;
        if (player != null)
        {
            fuelPct = (int)(player.FuelRatio() * 100.0f);
        }

        var health = (float)GameState.Instance.Health; // double 全程——(float) 截断会使维修 heal 差值无法精确回满
        var maxHealth = (float)GameState.Instance.MaxHealth();
        _statusLabel.Text = GdFormat.Format((string)Tr("BASE_STATUS_FMT"), Mathf.CeilToInt(health), fuelPct, augmentText);
        // 文案刷新
        _titleLabel.Text = (string)Tr("BASE_TITLE");
        foreach (var kv in _titleLabels)
        {
            kv.Value.Text = (string)Tr(kv.Key);
        }

        RefreshChipLabels();
        _routeHintLabel.Text = (string)Tr("BASE_ROUTE_HINT");
        _categoryLabel.Text = (string)Tr(ChipKey(_currentCategory));
        _repairButton.Text = (string)Tr("BASE_REPAIR");
        _rechargeButton.Text = (string)Tr("BASE_RECHARGE");
        // 维修 = 2RP 回满（对齐原作 repair_at_base：health = max_health，满血拒售）
        var rpRepairCost = GameState.Instance.RP_REPAIR_COST;
        var rpRechargeCost = GameState.Instance.RP_RECHARGE_COST;
        _repairButton.Disabled = rp < rpRepairCost || health >= maxHealth;
        _rechargeButton.Disabled = rp < rpRechargeCost || player == null || player.FuelAmount() >= player.FuelMax;
        // 增幅补给按钮（价格与售罄态随刷新更新）
        var cacheCost = SupplyCfg("cache_cost_rp", 4);
        _buyCacheButton.Text = GdFormat.Format((string)Tr("BASE_SUPPLY_CACHE_FMT"), cacheCost);
        _buyCacheButton.Disabled = rp < cacheCost || SupplyCfg("cache_points", 2) <= 0;
        var slotCost = SupplyCfg("overcharge_cost_rp", 8);
        var slotsMaxed = GameState.Instance.Talent.BonusOverchargeSlots >= SupplyCfg("overcharge_slot_max", 2);
        _buyOverchargeButton.Text = slotsMaxed
            ? (string)Tr("BASE_SUPPLY_OVERCHARGE_MAXED")
            : GdFormat.Format((string)Tr("BASE_SUPPLY_OVERCHARGE_FMT"), slotCost);
        _buyOverchargeButton.Disabled = slotsMaxed || rp < slotCost;
        // 任务轮换：刷新点数与按钮状态（点数不足禁用；提示在 OnRefreshPressed 内）
        _refreshPointsLabel.Text = GdFormat.Format((string)Tr("BASE_REFRESH_POINTS"), GameState.Instance.RefreshPoints);
        _refreshButton.Text = GdFormat.Format((string)Tr("BASE_REFRESH_FMT"), GameState.Instance.REFRESH_COST);
        _refreshButton.Disabled = !GameState.Instance.CanRefreshMissions();
        RefreshRoutes();
        RefreshMissions();
    }

    /// <summary>base.supply 档位读取（低频补给路径，直查免缓存）。</summary>
    private int SupplyCfg(string key, int fallback) =>
        Mathf.Max((int)GameState.Instance.Cfg("base.supply." + key, fallback).AsInt64(), 0);

    /// <summary>路线契约刷新（机制 C）：三条路线行（绑定/切换/生效中）+ 重置代币购置行。</summary>
    private void RefreshRoutes()
    {
        // Free() 同步删除——QueueFree 帧末才删，同帧 add_child 新旧行并存闪一帧
        foreach (var child in _routesBox.GetChildren())
        {
            child.Free();
        }

        var talent = GameState.Instance.Talent;
        foreach (var route in TalentTree.Routes)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var active = talent.Route == route.Id;
            var coreName = (string)Tr(TalentTree.Category(route.CoreCategoryId).NameKey);
            var label = MakeLabel(GdFormat.Format((string)Tr("BASE_ROUTE_ROW_FMT"), (string)Tr(route.NameKey), coreName), 20);
            label.CustomMinimumSize = new Vector2(300.0f, 0.0f);
            label.HorizontalAlignment = HorizontalAlignment.Left;
            row.AddChild(label);
            var button = MakeButton("");
            if (active)
            {
                button.Text = (string)Tr("BASE_ROUTE_ACTIVE");
                button.Disabled = true;
            }
            else if (talent.Route == "")
            {
                button.Text = (string)Tr("BASE_ROUTE_BIND");
            }
            else
            {
                // 切换契约消耗重置代币（无代币禁售；购置在下方代币行）
                button.Text = GdFormat.Format((string)Tr("BASE_ROUTE_SWITCH_FMT"), talent.ResetTokens);
                button.Disabled = talent.ResetTokens <= 0;
            }

            var routeId = route.Id;
            button.Pressed += () => OnRoutePressed(routeId);
            row.AddChild(button);
            _routesBox.AddChild(row);
        }

        // 重置代币购置行（RP 结算；切换路线的唯一来源）
        var tokenRow = new HBoxContainer();
        tokenRow.AddThemeConstantOverride("separation", 10);
        var tokenLabel = MakeLabel(GdFormat.Format((string)Tr("BASE_TOKEN_COUNT_FMT"), talent.ResetTokens), 18);
        tokenLabel.CustomMinimumSize = new Vector2(300.0f, 0.0f);
        tokenLabel.HorizontalAlignment = HorizontalAlignment.Left;
        tokenLabel.AddThemeColorOverride("font_color", UITheme.AccentGold);
        tokenRow.AddChild(tokenLabel);
        var buyButton = MakeButton(GdFormat.Format((string)Tr("BASE_TOKEN_BUY_FMT"), talent.ResetTokenCost));
        buyButton.Disabled = GameState.Instance.Rp < talent.ResetTokenCost;
        buyButton.Pressed += OnBuyTokenPressed;
        tokenRow.AddChild(buyButton);
        _routesBox.AddChild(tokenRow);
    }

    private void RefreshMissions()
    {
        // 同步删除防同帧并存闪一帧
        foreach (var child in _missionsBox.GetChildren())
        {
            child.Free();
        }

        // 任务轮换：渲染在场任务（active_mission_ids），非固定 MISSION_DEFS
        var ids = GameState.Instance.ActiveMissionIds();
        foreach (var idV in ids)
        {
            var id = idV;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var progress = GameState.Instance.MissionProgress(id);
            var goal = GameState.Instance.MissionGoal(id);
            var idUpper = id.ToString().ToUpperInvariant();
            // 任务行格式串走 tr()（BASE_MISSION_FMT），语言切换标点随 locale 变化
            var text = GdFormat.Format(
                (string)Tr("BASE_MISSION_FMT"),
                (string)Tr("MISSION_" + idUpper + "_NAME"),
                (string)Tr("MISSION_" + idUpper + "_DESC"),
                Mathf.Min(progress, goal),
                goal);
            if (GameState.Instance.IsMissionClaimed(id))
            {
                text += (string)Tr("BASE_CLAIMED");
            }
            else if (GameState.Instance.IsMissionDone(id))
            {
                text += (string)Tr("BASE_DONE");
            }

            var info = MakeLabel(text, 20);
            info.CustomMinimumSize = new Vector2(400.0f, 0.0f);
            info.HorizontalAlignment = HorizontalAlignment.Left;
            if (GameState.Instance.IsMissionDone(id))
            {
                info.AddThemeColorOverride("font_color", UITheme.Success);
            }

            row.AddChild(info);
            var claimButton = MakeButton((string)Tr("BASE_CLAIM"));
            claimButton.Disabled = !GameState.Instance.IsMissionDone(id) || GameState.Instance.IsMissionClaimed(id);
            claimButton.Pressed += () => OnClaimPressed(id);
            row.AddChild(claimButton);
            _missionsBox.AddChild(row);
        }
    }

    private void OnLocaleChanged()
    {
        Refresh();
        if (Visible)
        {
            RebuildMenu();
        }
    }




    public void Resume() => OnResumePressed();

    private void OnRepairPressed()
    {
        // 2RP 回满（对齐原作，不按缺口计价）
        var rpRepairCost = GameState.Instance.RP_REPAIR_COST;
        if (GameState.Instance.SpendRp(rpRepairCost))
        {
            // heal 量全程 double 计算——(float) 截断致差值不精确回满（smoke 维修 flake 根因）
            var health = GameState.Instance.Health;
            var maxHealth = GameState.Instance.MaxHealth();
            GameState.Instance.Heal(Mathf.Max(0.0, maxHealth - health)); // 防负治疗扣血
            GameState.Instance.PlaySfx(SfxId.Resupply);
            Refresh();
        }
    }

    private void OnRechargePressed()
    {
        var playerV = GameState.Instance.PlayerRef;
        var player = playerV as Player; // Player 走注册表，as 对非 Player/null 均得 null
        var rpRechargeCost = GameState.Instance.RP_RECHARGE_COST;
        if (player != null && GameState.Instance.SpendRp(rpRechargeCost))
        {
            player.RefillFuel();
            GameState.Instance.PlaySfx(SfxId.Resupply);
            Refresh();
        }
    }

    /// <summary>增幅缓存补给：RP → 天赋缓存点（点值经 Talent.Grant 入 LIFO 池，衰减口径与里程碑入账一致）。</summary>
    private void OnBuyCachePressed()
    {
        var cost = SupplyCfg("cache_cost_rp", 4);
        var points = SupplyCfg("cache_points", 2);
        if (points <= 0)
        {
            return;
        }

        if (GameState.Instance.SpendRp(cost))
        {
            GameState.Instance.Talent.Grant(points);
            GameState.Instance.PlaySfx(SfxId.AugmentPick);
        }

        Refresh();
    }

    /// <summary>超载槽补给：RP → 本局风险加点上限 +1（上限 base.supply.overcharge_slot_max，随存档保存）。</summary>
    private void OnBuyOverchargePressed()
    {
        var cost = SupplyCfg("overcharge_cost_rp", 8);
        var talent = GameState.Instance.Talent;
        if (talent.BonusOverchargeSlots >= SupplyCfg("overcharge_slot_max", 2))
        {
            return;
        }

        if (GameState.Instance.SpendRp(cost))
        {
            talent.AddOverchargeSlot();
            GameState.Instance.PlaySfx(SfxId.Resupply);
        }

        Refresh();
    }

    private void OnRoutePressed(StringName routeId)
    {
        // 路线契约（机制 C）：绑定免费、切换耗代币；核心大类增益/其余上限减半即时生效
        // （有效层级变化由服务侧直发 augments_changed，Player 缓存自动重算）
        GameState.Instance.Talent.ChooseRoute(routeId.ToString());
        Refresh();
    }

    private void OnBuyTokenPressed()
    {
        if (GameState.Instance.Talent.BuyResetToken())
        {
            GameState.Instance.PlaySfx(SfxId.Resupply);
        }

        Refresh();
    }

    private void OnClaimPressed(StringName id)
    {
        if (GameState.Instance.ClaimMission(id))
        {
            GameState.Instance.PlaySfx(SfxId.AugmentPick);
        }

        Refresh();
    }

    /// <summary>刷新任务：消耗 RefreshPoints 重抽（余额不足时提示；成功播音效并重绘任务面板）。</summary>
    private void OnRefreshPressed()
    {
        if (GameState.Instance.RefreshMissions())
        {
            GameState.Instance.PlaySfx(SfxId.AugmentPick);
            HideRefreshHint();
        }
        else
        {
            ShowRefreshHint((string)Tr("BASE_NO_REFRESH_POINTS"));
        }

        Refresh();
    }

    private void ShowRefreshHint(string text)
    {
        _refreshHintLabel.Text = text;
        _refreshHintLabel.Visible = true;
        if (_refreshHintTimer != null && GodotObject.IsInstanceValid(_refreshHintTimer))
        {
            _refreshHintTimer.Stop();
            _refreshHintTimer.QueueFree();
        }

        _refreshHintTimer = new Godot.Timer
        {
            OneShot = true,
            WaitTime = 2.0,
        };
        _refreshHintTimer.Timeout += HideRefreshHint;
        AddChild(_refreshHintTimer);
        _refreshHintTimer.Start();
    }

    private void HideRefreshHint()
    {
        _refreshHintLabel.Visible = false;
        // 一次性提示 Timer 触发后自清理（否则每次提示泄漏一个已触发 Timer）
        if (_refreshHintTimer != null && GodotObject.IsInstanceValid(_refreshHintTimer))
        {
            _refreshHintTimer.QueueFree();
            _refreshHintTimer = null;
        }
    }

    private void OnResumePressed()
    {
        Visible = false;
        OnVisibleChangedForFx();
        SetWheelActive(false);
        EmitSignal(SignalName.ResumeRequested);
    }
}

/// <summary>面板扫描线叠加层：单节点自绘每 4px 一条 1px 横线，1 draw call。</summary>
public partial class BaseConsoleScanlines : Control
{
    public override void _Ready()
    {
        MouseFilter = Control.MouseFilterEnum.Ignore;
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        var y = 2.0f;
        while (y < Size.Y)
        {
            DrawLine(new Vector2(0.0f, y), new Vector2(Size.X, y), UITheme.PhantomScan, 1.0f);
            y += 4.0f;
        }
    }
}

/// <summary>16×16 程序化线性发光图标：极简折线，青色双层描边模拟辉光。</summary>
public partial class BaseConsoleGlyphIcon : Control
{
    public Vector2[][] Strokes { get; set; } = System.Array.Empty<Vector2[]>();

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(16.0f, 16.0f);
        MouseFilter = Control.MouseFilterEnum.Ignore;
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
    }

    public override void _Draw()
    {
        foreach (var stroke in Strokes)
        {
            DrawPolyline(stroke, new Color(UITheme.Accent, 0.3f), 3.0f, true);
        }

        foreach (var stroke in Strokes)
        {
            DrawPolyline(stroke, UITheme.Accent, 1.5f, true);
        }
    }
}
