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
/// 开关：HUD 缓存指示器点击 / G 键（talent_panel action）；Esc/右键经 BackNavigator 路由关闭。
/// 打开时暂停对局（与 PauseUI/BaseConsole 同款模态语义）；process_mode=Always（场景内配置）。
/// </summary>
public partial class TalentPanel : CanvasLayer
{
    private Main _main = null!;
    private RadialWheel _wheel = null!;
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
    private HBoxContainer _footerBox = null!;
    private StringName? _selectedNode;
    private string? _category;
    private readonly Callable _onLocaleChanged;
    private readonly Callable _onTalentsChanged;
    private readonly Callable _onCacheChanged;

    /// <summary>右区几何（1080p 设计坐标；UI 不走 world_scale）。</summary>
    private const float RightLeft = 560f;
    private const float FanTop = 170f;
    private const float FanHeight = 720f;

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
        BuildDim();
        BuildWheel();
        BuildRightArea();
        var gs = GameState.Instance;
        if (gs != null)
        {
            if (!gs.IsConnected("LocaleChanged", _onLocaleChanged))
            {
                gs.Connect("LocaleChanged", _onLocaleChanged);
            }

            if (!gs.IsConnected("TalentsChanged", _onTalentsChanged))
            {
                gs.Connect("TalentsChanged", _onTalentsChanged);
            }

            if (!gs.IsConnected("TalentCacheChanged", _onCacheChanged))
            {
                gs.Connect("TalentCacheChanged", _onCacheChanged);
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

        if (gs.IsConnected("LocaleChanged", _onLocaleChanged))
        {
            gs.Disconnect("LocaleChanged", _onLocaleChanged);
        }

        if (gs.IsConnected("TalentsChanged", _onTalentsChanged))
        {
            gs.Disconnect("TalentsChanged", _onTalentsChanged);
        }

        if (gs.IsConnected("TalentCacheChanged", _onCacheChanged))
        {
            gs.Disconnect("TalentCacheChanged", _onCacheChanged);
        }
    }

    // ---------------- 开关 ----------------

    public bool CanOpen() =>
        Visible == false
        && !GetTree().Paused
        && !_main.IsGameOver()
        && !_main.IsIntroPlaying()
        && !_main.IsReturnPlaying()
        && !_main.IsHomecoming();

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
        SyncRightView();
        RefreshAll();
        UITheme.StaggerOpen(_rightRoot);
    }

    public void Close()
    {
        if (!Visible)
        {
            return;
        }

        Visible = false;
        GetTree().Paused = false;
    }

    /// <summary>G 键（talent_panel）：关闭态在无其他模态时打开，打开态关闭。
    /// 暂停态守卫覆盖 开场/返航过场/基地/暂停/设置（均持树暂停），死亡结算单独判。</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("talent_panel"))
        {
            return;
        }

        if (Visible)
        {
            Close();
        }
        else if (CanOpen())
        {
            Open();
        }
        else
        {
            return;
        }

        GetViewport().SetInputAsHandled();
    }

    // ---------------- 构建 ----------------

    private void BuildDim()
    {
        var dim = new ColorRect { Color = UITheme.DimBg };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);
    }

    private void BuildWheel()
    {
        _wheel = new RadialWheel { Position = new Vector2(-160f, 540f), BackLabel = Tr("TALENT_BACK") };
        AddChild(_wheel);
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
        var footer = new ChamferedPanel
        {
            Position = new Vector2(0f, 950f),
            Size = new Vector2(1300f, 84f),
            Padding = 0f,
        };
        _rightRoot.AddChild(footer);
        _footerBox = new HBoxContainer();
        _footerBox.AddThemeConstantOverride("separation", 26);
        _footerBox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _footerBox.OffsetLeft = 22f;
        _footerBox.OffsetRight = -22f;
        _footerBox.Alignment = BoxContainer.AlignmentMode.Center;
        footer.AddChild(_footerBox);
    }

    // ---------------- 轮盘数据与联动 ----------------

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
                        Label = Tr($"BUFF_{nodeId.ToUpperInvariant()}_NAME"),
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
        _wheel.Load(roots);
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

    private void OnWheelConfirmed(RadialWheelOption option) => SelectNode(option.Id);

    private void OnWheelDrilled(RadialWheelOption option)
    {
        if (_wheel.Depth == 2)
        {
            _category = option.Id;
        }

        SyncRightView();
    }

    private void OnWheelBacked()
    {
        if (_wheel.Depth == 1)
        {
            _category = null;
            _selectedNode = null;
        }

        SyncRightView();
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

    /// <summary>轮盘层深 ↔ 右区视图：根层 = 大类概览；下钻 = 树状扇形。</summary>
    private void SyncRightView()
    {
        var drilled = _wheel.Depth >= 2 && _category != null;
        _overviewBox.Visible = !drilled;
        _fan.Visible = drilled;
        _detail.Visible = drilled && _selectedNode != null;
        if (drilled)
        {
            _fan.SetCategory(_category);
            _fan.SetSelected(_selectedNode);
        }
        else
        {
            RebuildOverview();
        }
    }

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
            // 定宽包裹：MakeBuffSocket 自身是自增自适应 ChamferedPanel，直接作为 Fill 布局子项时
            // 「外板变宽 → socket 撑满 → socket 最小尺寸棘轮回写 → 外板更宽」正反馈撑爆整行概览卡
            var socketWrap = new Control { CustomMinimumSize = new Vector2(64.0f, 64.0f), MouseFilter = Control.MouseFilterEnum.Ignore };
            var socket = UITheme.MakeBuffSocket(firstId, 64.0f);
            socket.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            socketWrap.AddChild(socket);
            vbox.AddChild(socketWrap);
            vbox.AddChild(UITheme.MakeLabel(Tr(cat.NameKey), UITheme.FontHeader, BuffIcons.ColorFor(firstId), HorizontalAlignment.Center));
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

    // ---------------- A7：测试/截图端口 ----------------

    /// <summary>A7：测试/截图端口——按轮盘根层序号下钻大类（走与点击轮盘同一动画+联动路径）。</summary>
    public void TestDrillCategory(int rootIndex)
    {
        if (rootIndex >= 0 && rootIndex < _wheel.CurrentCount)
        {
            _wheel.TestDrill(rootIndex);
        }
    }

    /// <summary>A7：测试/截图端口——直选节点（详情卡与轮盘高亮联动）。</summary>
    public void TestSelectNode(string nodeId) => SelectNode(nodeId);

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
        _cacheLabel.Text = GdFormat.Format(Tr("TALENT_CACHE_FMT"), talent.EffectiveCache, talent.RawCache);
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
        _detail.Visible = true;
        _detailName.Text = Tr($"BUFF_{_selectedNode.ToString().ToUpperInvariant()}_NAME");
        _detailCaption.Text = GdFormat.Format(Tr("TALENT_PATH_FMT"), Tr(TalentTree.Category(def.CategoryId).NameKey), Tr(TalentTree.Line(def.LineId).NameKey));
        var level = talent.Level(idSn);
        var cap = talent.CapFor(idSn);
        var maxLevel = talent.MaxLevel(idSn);
        _detailLevel.Text = GdFormat.Format(Tr("TALENT_LV_FULL_FMT"), level, cap, maxLevel);
        _detailDesc.Text = Tr($"BUFF_{_selectedNode.ToString().ToUpperInvariant()}_DESC");

        // 效果读数：乘算节点给「当前累计 → 下一级预估」（5.2 UI 要求）
        var factorV = GameState.Instance.Cfg($"buffs.{_selectedNode}.factor", 0.0);
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
                ? GdFormat.Format(Tr("TALENT_STATUS_PREREQ_FMT"), Tr($"BUFF_{prev.ToUpperInvariant()}_NAME"))
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
        _footerBox.AddChild(MakeFooterChip(GdFormat.Format(Tr("TALENT_FOOTER_CACHE_FMT"), talent.EffectiveCache, talent.RawCache), UITheme.Accent));
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
