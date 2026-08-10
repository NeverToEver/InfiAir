using System.Collections.Generic;
using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// 基地控制台（返航中场整备）：战机库 / 武器挂载（天赋路线）/ 维修补给 / 任务规划。
/// 顶部 RP 余额，底部「继续出击」返回同一局。
/// 视觉为「虚影皮肤」（docs/RETURN_HOME_CINEMATIC.md §3）：虚影站背景层 + 全息面板，
/// 全部信号/回调/GameState 数据接口零改动。
/// M5 全量迁移（2026-08-08 自 scripts/base_console.gd）：CanvasLayer 子类；
/// DawnStation 静态工厂 typed 直调；UITheme/ChamferedPanel 为 C# typed 直调。
/// 注：原 GDScript signal resume_requested 迁移为 C# [Signal] ResumeRequested。
/// </summary>
public partial class BaseConsole : CanvasLayer
{
    /// <summary>继续出击：返回同一局（main.gd `_resume_from_base` / tutorial.gd `_on_base_resume` 连接）。</summary>
    [Signal]
    public delegate void ResumeRequestedEventHandler();

    private static readonly Dictionary<string, string> RouteBuffNames = new()
    {
        { "spread_shot", "BUFF_SPREAD_SHOT_NAME" },
        { "laser_beam", "BUFF_LASER_BEAM_NAME" },
        { "phase_dash", "BUFF_PHASE_DASH_NAME" },
        { "mothership_recall", "BUFF_MOTHERSHIP_RECALL_NAME" },
    };

    private static readonly Dictionary<string, string> RouteLineNames = new()
    {
        { "offense", "ROUTE_OFFENSE" },
        { "mobility", "ROUTE_MOBILITY" },
    };

    /// <summary>虚影面板底后径向辉光垫（近似毛玻璃，§3.2）：四面板共享一张径向渐变纹理。</summary>

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
    private VBoxContainer _routesBox = null!;
    private VBoxContainer _missionsBox = null!;
    private Button _refreshButton = null!; // 2026-08-05：任务轮换——刷新任务按钮
    private Label _refreshPointsLabel = null!;
    private Label _refreshHintLabel = null!; // 点数不足提示（临时显示，2s 后隐藏）
    private Godot.Timer? _refreshHintTimer;
    private readonly Dictionary<string, Label> _titleLabels = new();
    private HBoxContainer _columns = null!;
    private Label _routeHintLabel = null!;
    private readonly List<ChamferedPanel> _panels = new();
    private Button _resumeButton = null!; // L08（2026-08-03 审查）：成员引用——焦点归还 + locale 刷新
    private GradientTexture2D? _glowTexture;

    /// <summary>虚影面板底后径向辉光垫（近似毛玻璃，§3.2）：四面板共享一张径向渐变纹理。</summary>
    private GradientTexture2D MakeGlowTexture()
    {
        if (_glowTexture != null)
        {
            return _glowTexture;
        }

        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.0f, 0.83f, 1.0f, 0.12f));
        gradient.SetColor(1, new Color(0.0f, 0.83f, 1.0f, 0.0f));
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

    /// <summary>数据抖动装饰（§3.2）：3Hz 正弦 α0.92–1.0 + 每 2.7s 一次 0.06s 的 1px 横向错位闪
    ///（tween 循环，不加 _process；本层 process_mode=Always，暂停态照常播放）。</summary>
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
    }

    public override void _Ready()
    {
        Visible = false;
        var gs = GameState.Instance;
        if (gs != null && !gs.IsConnected("LocaleChanged", _localeChanged))
        {
            gs.Connect("LocaleChanged", _localeChanged);
        }

        var dim = new ColorRect { Color = UITheme.PhantomBg };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        // 虚影站内部概念背景层（§3.3.1）：PHANTOM 站体 r≈520 以 (960,540) 为圆心，
        // 父容器压 α≈0.12（站体自呼吸写自身 modulate:a，不能直接在站体上压 alpha）+ 8s/趟全屏慢扫描带。
        // 该层放在 dim 之后、CenterContainer 之前。
        var bgWrap = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        bgWrap.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var bgModulate = bgWrap.Modulate;
        bgModulate.A = 0.12f;
        bgWrap.Modulate = bgModulate;
        AddChild(bgWrap);
        var station = DawnStation.Build(1); // M6：DawnStation 迁 C#，typed（Mode.PHANTOM）
        station.Position = new Vector2(960.0f, 540.0f);
        station.Scale = Vector2.One * 2.0f;
        bgWrap.AddChild(station);
        // 慢扫描带（纯装饰；2026-08-05 P4：尺寸/行程改 viewport 可见区——原硬编码 1920×1080）
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var scanH = 140.0f;
        var slowScan = new ColorRect { Color = UITheme.PhantomScan };
        slowScan.MouseFilter = Control.MouseFilterEnum.Ignore;
        slowScan.Size = new Vector2(viewportSize.X, scanH);
        slowScan.Position = new Vector2(0.0f, -scanH);
        AddChild(slowScan);
        var scanTween = CreateTween().SetLoops();
        scanTween.TweenProperty(slowScan, "position:y", viewportSize.Y, 8.0).SetTrans(Tween.TransitionType.Linear);
        scanTween.TweenProperty(slowScan, "position:y", -scanH, 0.0);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 14);
        center.AddChild(vbox);

        _titleLabel = MakeLabel((string)Tr("BASE_TITLE"), 44);
        vbox.AddChild(_titleLabel);
        ApplyDataFlicker(_titleLabel);
        _rpLabel = MakeLabel("", 26);
        _rpLabel.AddThemeColorOverride("font_color", UITheme.AccentGold);
        vbox.AddChild(_rpLabel);
        ApplyDataFlicker(_rpLabel);

        _columns = new HBoxContainer();
        var columns = _columns;
        columns.AddThemeConstantOverride("separation", 140); // §3.3.2：露出中央环体轴心，容器层级/热区/焦点链不变
        vbox.AddChild(columns);

        // 左列：战机库 + 维修补给 + 研究所
        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 20);
        columns.AddChild(left);
        left.AddChild(BuildHangar());
        left.AddChild(BuildSupply());
        left.AddChild(BuildLab()); // 局外成长：研究所（2026-08-09）

        // 右列：武器挂载 + 任务规划
        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 20);
        columns.AddChild(right);
        right.AddChild(BuildRoutes());
        right.AddChild(BuildMissions());

        var resumeButton = UITheme.MakeButton((string)Tr("BASE_RESUME"), true);
        _resumeButton = resumeButton;
        resumeButton.CustomMinimumSize = new Vector2(280.0f, 52.0f);
        resumeButton.Pressed += OnResumePressed;
        // 底部 2px 投影线 + 1.5s 呼吸辉光（§3.3.4，只动 alpha；尺寸/位置/回调不变）
        var shadowLine = new ColorRect { Color = UITheme.AccentDim };
        shadowLine.MouseFilter = Control.MouseFilterEnum.Ignore;
        shadowLine.AnchorLeft = 0.15f;
        shadowLine.AnchorRight = 0.85f;
        shadowLine.AnchorTop = 1.0f;
        shadowLine.AnchorBottom = 1.0f;
        shadowLine.OffsetTop = 2.0f;
        shadowLine.OffsetBottom = 4.0f;
        var shadowModulate = shadowLine.Modulate;
        shadowModulate.A = 0.3f;
        shadowLine.Modulate = shadowModulate;
        resumeButton.AddChild(shadowLine);
        var glowBreathe = CreateTween().SetLoops();
        glowBreathe.TweenProperty(shadowLine, "modulate:a", 1.0f, 0.75).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        glowBreathe.TweenProperty(shadowLine, "modulate:a", 0.3f, 0.75).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        vbox.AddChild(resumeButton);
    }

    private Label MakeLabel(string text, int size) => UITheme.MakeLabel(text, size);

    private ChamferedPanel MakePanel(string titleKey, Vector2[][] glyph)
    {
        var panel = new ChamferedPanel();
        UITheme.ApplyPhantomPanel(panel); // §3.3.3 虚影材质
        panel.CustomMinimumSize = new Vector2(560.0f, 0.0f);
        _panels.Add(panel);
        // 面板底后径向辉光垫（近似毛玻璃，§3.2）：绘于面板底之下，随面板尺寸自适应
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
        // 扫描线叠加层（§3.2）：单节点自绘，绘于面板底之上、内容之下
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
        // 标题行：16×16 线性发光图标 + section header（仅基地内组装，不影响其它页面的 make_section_header）
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
        return panel;
    }

    private Button MakeButton(string text)
    {
        var button = UITheme.MakeButton(text);
        button.AddThemeFontSizeOverride("font_size", 20);
        return button;
    }

    private Control BuildHangar()
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

    private Control BuildSupply()
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
        return panel;
    }

    private Control BuildLab()
    {
        // 六边形科技节点极简折线图标
        var glyph = new Vector2[][]
        {
            new[]
            {
                new Vector2(8, 1),
                new Vector2(14, 4),
                new Vector2(14, 11),
                new Vector2(8, 15),
                new Vector2(2, 11),
                new Vector2(2, 4),
                new Vector2(8, 1),
            },
        };
        var panel = MakePanel("META_TITLE", glyph);
        var body = (VBoxContainer)panel.GetNode("Body");
        body.AddChild(new ResearchLab());
        return panel;
    }

    private Control BuildRoutes()
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

    private Control BuildMissions()
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
        // 任务轮换（2026-08-05）：刷新点数 + 刷新按钮 + 点数不足提示
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

    /// <summary>进入基地（main.gd 返航结束 / tutorial.gd 过关调用）：发放刷新点数、重绘、全息启动、焦点落到「继续出击」。</summary>
    public void ShowBase()
    {
        // 任务轮换：进基地发放刷新点数（GRANT_PER_VISIT 档位，攒两次基地换一次刷新）
        GameState.Instance.GrantRefreshPoints();
        Refresh();
        Visible = true;
        HoloBoot();
        UITheme.AnimateOpen(_columns);
        // L08（2026-08-03 审查）：全项目模态页唯一无焦点初始化的页面——手柄/键盘玩家
        // 进入基地后方向键+Enter 无法操作（对齐 settings/pause/buff_select 的聚焦约定）
        _resumeButton.GrabFocus();
    }

    /// <summary>全息启动（§3.3.5）：四面板 α0 + scale 0.98→1.0，stagger 60ms；
    /// pivot 设为中心（否则从左上角缩放），tween 终值保证 scale 精确回 1.0。</summary>
    private void HoloBoot()
    {
        var i = 0;
        foreach (var panel in _panels)
        {
            panel.PivotOffset = panel.Size * 0.5f;
            var modulate = panel.Modulate;
            modulate.A = 0.0f;
            panel.Modulate = modulate;
            panel.Scale = Vector2.One * 0.98f;
            var tween = CreateTween();
            tween.TweenInterval(0.06 * i);
            tween.TweenProperty(panel, "modulate:a", 1.0f, 0.25);
            tween.Parallel().TweenProperty(panel, "scale", Vector2.One, 0.25);
            i += 1;
        }
    }

    private void Refresh()
    {
        var rp = GameState.Instance.Rp;
        _rpLabel.Text = GdFormat.Format((string)Tr("BASE_RP"), rp);
        var playerV = GameState.Instance.PlayerRef;
        var player = playerV != null ? playerV as Player : null; // M3c：Player 迁 C#  # A5：走注册表，替代 group 现找
        // 战机库状态总览
        var buffText = "";
        var buffs = GameState.Instance.Buffs;
        foreach (var key in buffs.Keys)
        {
            var id = key.AsStringName();
            // 显示名走翻译键（与 Buff 三选一/HUD 明细栏同源），不裸显内部 id
            buffText += GdFormat.Format("%s×%d  ", (string)Tr("BUFF_" + id.ToString().ToUpperInvariant() + "_NAME"), buffs[key].AsInt32());
        }

        if (buffText.Length == 0)
        {
            buffText = (string)Tr("BASE_NO_BUFF");
        }

        var fuelPct = 0;
        if (player != null)
        {
            fuelPct = (int)(player.FuelRatio() * 100.0f);
        }

        var health = (float)GameState.Instance.Health; // M5：AsSingle 精度损失致 heal 后 99.9999≠max（smoke flake 根因）
        var maxHealth = (float)GameState.Instance.MaxHealth();
        _statusLabel.Text = GdFormat.Format((string)Tr("BASE_STATUS_FMT"), Mathf.CeilToInt(health), fuelPct, buffText);
        // 维修补给按钮状态
        _titleLabel.Text = (string)Tr("BASE_TITLE");
        foreach (var kv in _titleLabels)
        {
            kv.Value.Text = (string)Tr(kv.Key);
        }

        _routeHintLabel.Text = (string)Tr("BASE_ROUTE_HINT");
        _repairButton.Text = (string)Tr("BASE_REPAIR");
        _rechargeButton.Text = (string)Tr("BASE_RECHARGE");
        _resumeButton.Text = (string)Tr("BASE_RESUME"); // L08：locale 刷新路径补齐（其余按钮均在此刷新）
        // 维修 = 2RP 回满（对齐原作 repair_at_base：health = max_health，满血拒售）
        var rpRepairCost = GameState.Instance.RP_REPAIR_COST;
        var rpRechargeCost = GameState.Instance.RP_RECHARGE_COST;
        _repairButton.Disabled = rp < rpRepairCost || health >= maxHealth;
        _rechargeButton.Disabled = rp < rpRechargeCost || player == null || player.FuelAmount() >= player.FuelMax;
        // 任务轮换：刷新点数与按钮状态（点数不足禁用；提示在 _on_refresh_pressed 内）
        _refreshPointsLabel.Text = GdFormat.Format((string)Tr("BASE_REFRESH_POINTS"), GameState.Instance.RefreshPoints);
        _refreshButton.Text = GdFormat.Format((string)Tr("BASE_REFRESH_FMT"), GameState.Instance.REFRESH_COST);
        _refreshButton.Disabled = !GameState.Instance.CanRefreshMissions();
        RefreshRoutes();
        RefreshMissions();
    }

    private void RefreshRoutes()
    {
        // U16：Free() 同步删除——QueueFree 帧末才删，同帧 add_child 新旧行并存闪一帧
        //（Hud.cs:1194 同场景先例）
        foreach (var child in _routesBox.GetChildren())
        {
            child.Free();
        }

        var routeLines = GameState.Instance.ROUTE_LINES;
        var chosenRoutes = GameState.Instance.ChosenRoutes;
        foreach (var lineKey in routeLines.Keys)
        {
            var line = lineKey.AsStringName();
            var options = routeLines[lineKey].AsGodotArray();
            var total = GameState.Instance.BuffCount(options[0].AsStringName()) + GameState.Instance.BuffCount(options[1].AsStringName());
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var lineNameKey = RouteLineNames.TryGetValue(line.ToString(), out var lineName) ? lineName : line.ToString();
            var lineLabel = MakeLabel(GdFormat.Format((string)Tr("BASE_LINE_FMT"), (string)Tr(lineNameKey), total), 20);
            lineLabel.CustomMinimumSize = new Vector2(170.0f, 0.0f);
            lineLabel.HorizontalAlignment = HorizontalAlignment.Left;
            row.AddChild(lineLabel);
            foreach (var optV in options)
            {
                var opt = optV.AsStringName();
                var chosen = chosenRoutes.ContainsKey(line) && chosenRoutes[line].AsStringName() == opt;
                var locked = GameState.Instance.IsBuffLocked(opt);
                var button = MakeButton("");
                var buffNameKey = RouteBuffNames.TryGetValue(opt.ToString(), out var mappedName) ? mappedName : opt.ToString(); // H20：表缺键兜底
                var buffName = (string)Tr(buffNameKey);
                if (chosen)
                {
                    button.Text = GdFormat.Format((string)Tr("BASE_CHOSEN_FMT"), buffName, GameState.Instance.BuffCount(opt));
                }
                else if (locked)
                {
                    button.Text = GdFormat.Format((string)Tr("BASE_LOCKED_FMT"), buffName);
                }
                else
                {
                    button.Text = GdFormat.Format((string)Tr("BUFF_LV_FMT"), buffName, GameState.Instance.BuffCount(opt));
                }

                button.Disabled = chosen || locked || total == 0;
                button.Pressed += () => OnRoutePressed(line, opt);
                row.AddChild(button);
            }

            _routesBox.AddChild(row);
        }
    }

    private void RefreshMissions()
    {
        // U16：同 RefreshRoutes（同步删除防同帧并存闪一帧）
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
            // C26：任务行格式串走 tr()（BASE_MISSION_FMT），语言切换标点随 locale 变化
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
    }

    public override void _ExitTree()
    {
        // U06（2026-08-09 审计）：C22 模式配对断开——死亡重开场景重载后残留连接
        // 在切语言时回调已释放实例
        var gs = GameState.Instance;
        if (gs == null)
        {
            return;
        }

        if (gs.IsConnected("LocaleChanged", _localeChanged))
        {
            gs.Disconnect("LocaleChanged", _localeChanged);
        }
    }

    /// <summary>A7：测试/诊断经公开接口（动作包装）。</summary>
    public void Repair() => OnRepairPressed();

    public void Recharge() => OnRechargePressed();

    public void ChooseRoute(StringName line, StringName buffId) => OnRoutePressed(line, buffId);

    public void ClaimMission(StringName id) => OnClaimPressed(id);

    public void Resume() => OnResumePressed();

    private void OnRepairPressed()
    {
        // 2RP 回满（对齐原作，不按缺口计价）
        var rpRepairCost = GameState.Instance.RP_REPAIR_COST;
        if (GameState.Instance.SpendRp(rpRepairCost))
        {
            // M5：heal 量全程 double 计算——(float) 截断致 59.2000004798174 + 40.7999992371
            // ≈ 99.9999997 ≠ max（smoke 维修 flake 根因，实测）；GDScript float=double，double 差值精确回满
            var health = GameState.Instance.Health;
            var maxHealth = GameState.Instance.MaxHealth();
            GameState.Instance.Heal(Mathf.Max(0.0, maxHealth - health)); // H20：防负治疗扣血
            GameState.Instance.PlaySfx(GameState.Instance.SFX_RESUPPLY);
            Refresh();
        }
    }

    private void OnRechargePressed()
    {
        var playerV = GameState.Instance.PlayerRef;
        var player = playerV != null ? playerV as Player : null; // M3c：Player 迁 C#  # A5：走注册表，替代 group 现找
        var rpRechargeCost = GameState.Instance.RP_RECHARGE_COST;
        if (player != null && GameState.Instance.SpendRp(rpRechargeCost))
        {
            player.RefillFuel();
            GameState.Instance.PlaySfx(GameState.Instance.SFX_RESUPPLY);
            Refresh();
        }
    }

    private void OnRoutePressed(StringName line, StringName buffId)
    {
        // choose_route 只改层数；玩家侧效果均实时读取 GameState.buff_count，无需额外重放（laser/recall 效果本体已在 3.3 实装）
        if (GameState.Instance.ChooseRoute(line, buffId))
        {
            GameState.Instance.PlaySfx(GameState.Instance.SFX_BUFF_PICK);
        }

        Refresh();
    }

    private void OnClaimPressed(StringName id)
    {
        if (GameState.Instance.ClaimMission(id))
        {
            GameState.Instance.PlaySfx(GameState.Instance.SFX_BUFF_PICK);
        }

        Refresh();
    }

    /// <summary>刷新任务：消耗 RefreshPoints 重抽（余额不足时提示；成功播音效并重绘任务面板）。</summary>
    private void OnRefreshPressed()
    {
        if (GameState.Instance.RefreshMissions())
        {
            GameState.Instance.PlaySfx(GameState.Instance.SFX_BUFF_PICK);
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
        // U16：一次性提示 Timer 触发后自清理（原触发后仍挂树，每次提示泄漏一个已触发 Timer）
        if (_refreshHintTimer != null && GodotObject.IsInstanceValid(_refreshHintTimer))
        {
            _refreshHintTimer.QueueFree();
            _refreshHintTimer = null;
        }
    }

    /// <summary>A7：测试/诊断经公开接口（动作包装）。</summary>
    public void RefreshTasks() => OnRefreshPressed();

    private void OnResumePressed()
    {
        Visible = false;
        EmitSignal(SignalName.ResumeRequested);
    }

    /// <summary>GDScript 字符串 % 格式化语义（%s/%d/%f 占位 + %% 转义；tr() 文案补参用，
}

/// <summary>面板扫描线叠加层（§3.2）：单节点自绘每 4px 一条 1px 横线，1 draw call。
/// 原 GDScript base_console.gd 内嵌类 _Scanlines，迁移为同文件顶层类（C# 源生成器不支持内嵌类）。</summary>
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

/// <summary>16×16 程序化线性发光图标（§3.2）：极简折线，青色双层描边模拟辉光。
/// 原 GDScript base_console.gd 内嵌类 _GlyphIcon，迁移为同文件顶层类。</summary>
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
