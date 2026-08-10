using System;
using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// HUD：分数/击杀（左上）、难度（右上）、生命（左下）、Boss 血条（顶部，
/// 带 70%/30% 阶段刻度线与阶段切换短闪，逃跑最后 10s 血条下方倒计时）。
/// Buff 收起态为右下角单行图标坞（最新 4 个 + 溢出 +N），L 键展开右缘滚动明细栏
/// （Esc 经 BackNavigator 优先关栏），与左下状态区、底部居中蓄力提示、左中通讯浮层分角隔离。
/// M5 全量迁移（2026-08-08 自 scripts/hud.gd）：CanvasLayer 子类；GameState/玩家/Main
/// （Enemy.SinFast/Mothership.GetStateStay 静态；Boss 信号经 C# event 连接）。
/// </summary>
public partial class Hud : CanvasLayer
{
    private readonly FontFile Font = UITheme.Font;

    // ---------------- @onready 节点（_ready 内 GetNode 赋值） ----------------
    private Label _scoreLabel = null!;
    private Label _killsLabel = null!;
    private Label _difficultyLabel = null!;
    private Label _livesLabel = null!;
    private SegmentedBar _hpBar = null!;
    private SegmentedBar _bossBar = null!;
    private SegmentedBar _fuelBar = null!;
    private SegmentedBar _dashBar = null!;
    private SegmentedBar _parryBar = null!;
    private Label _fuelTag = null!;
    private Label _dashTag = null!;
    private Label _parryTag = null!;
    private Label _dockTag = null!;

    private ChamferedPanel _bannerPlate = null!;
    private Label _bannerLabel = null!;

    private HBoxContainer _magBox = null!;
    private readonly Godot.Collections.Array<ColorRect> _magCellsNodes = new();
    private Label _homeChargeLabel = null!;
    private Label _giveUpLabel = null!;
    private VBoxContainer _earlyLeaveBox = null!;
    private Label _earlyLeaveLabel = null!;
    private ColorRect _earlyLeaveFill = null!;
    private Main _main = null!; // U13：typed
    private float _pollTimer;
    private string _lastDockText = "";
    private int _lastMagCells = -1;
    private Label[] _tagLabels = System.Array.Empty<Label>();
    private StringName[] _tagKeys = System.Array.Empty<StringName>();
    private VBoxContainer _eventBox = null!;
    private SegmentedBar _eventBar = null!;
    private Label _eventTitle = null!;
    private Label _eventTurretsLabel = null!;
    private int _lastEventAlive = -1;
    /// <summary>当前血条绑定的 Boss（逃跑倒计时轮询用；died 时清空）。M3d：Boss 迁 C#，直接 typed。</summary>
    private Boss? _boss;
    private Label _bossCountdown = null!;
    private Label _bossName = null!; // Boss 名牌（型号 + 阶段），血条子节点随其显隐
    private ChamferedPanel _bossPlate = null!; // Boss 血条 + 名牌的切角背板（随血条显隐）
    /// <summary>M3d：Boss.cs 的 C# 枚举 FightPhase { P1, P2, ENRAGE }（P1=0/P2=1 与
    /// GetFightPhaseTransition/Active 一致；ENRAGE=2 由声明顺序确定）——值镜像。</summary>
    private const int FightPhaseP1 = 0;
    private const int FightPhaseP2 = 1;
    private const int FightPhaseEnrage = 2;
    private int _bossPhase = FightPhaseP1;
    /// <summary>仪表类刷新降频（信号驱动的文本不受影响）。H15：≤0 节流失效。</summary>
    private float _pollInterval = 0.1f;
    /// <summary>分段血条（2026-08-03 机制三）：段数 + 段权 [P1 0.3 / P2 0.4 / ENRAGE 0.3]
    /// （段界 = 阶段阈值 [0.7, 0.3] 的宽占比，与 phase2/enrage_hp_ratio 默认一致、解耦）+ 段色
    /// （P1 琥珀 / P2 橙 / ENRAGE 红，已消耗段暗化、当前段高亮）。</summary>
    private int _bossBarSegments = 3;
    // M5：静态 Godot 集合在引擎退出后被 .NET finalize 触碰 native → segfault（实测），改实例字段
    private readonly Godot.Collections.Array BossSegWeights = new() { 0.3f, 0.4f, 0.3f };
    private readonly Godot.Collections.Array BossSegColors = new()
    {
        new Color(1.0f, 0.72f, 0.3f),
        new Color(1.0f, 0.5f, 0.15f),
        UITheme.Danger,
    };
    // 受击/低血屏幕反馈（effects.hit_flash / effects.low_hp，_ready 缓存）
    private float _hitFlashAlpha = 0.55f;
    private float _hitFlashTime = 0.25f;
    private float _lowHpRatio = 0.2f;
    private float _lowHpPulseMin = 0.15f;
    private float _lowHpPulseMax = 0.3f;
    private float _lowHpPulsePeriod = 1.2f;
    private TextureRect _vignette = null!;
    /// <summary>受击红闪 alpha（tween 衰减）；公开属性供 TweenProperty 字符串路径驱动（原 _hit_flash 脚本属性）。</summary>
    public float HitFlash { get; set; }
    private Tween? _hitTween;
    private float _lastHpValue = -1.0f;
    private float _pulseTime;
    private float _cachedMaxHp = 100.0f; // 缓存 max_health()（extra_life 层数驱动，buffs_changed 刷新；D08）
    private Control _buffDockWrap = null!; // 右下角锚定包装（meta_jitter 抖动对象，避免直接动自动生长的网格）
    private GridContainer _buffDock = null!;
    private Label _buffTag = null!;
    private Label? _buffOverflowLabel; // 收起态溢出计数（">4 个 buff 时 +N"）
    private ChamferedPanel _buffPanel = null!; // L 键展开的 buff 滚动栏
    private Label _buffPanelTitle = null!;
    private VBoxContainer _buffRows = null!;
    private string _lastBuffSignature = "";
    private ChamferedPanel _infoPlate = null!;
    private Label _infoLabel = null!;
    private Tween? _infoTween;
    private Tween? _warningTween; // H09：警告横幅闪烁 tween 互斥缓存
    // Meta HUD DYING 抖动（D9）：仅 _hp_bar 与 buff 坞两控件的静止位与补间
    private Vector2 _hpBarRest;
    private Vector2 _buffDockRest;
    private Tween? _jitterTween;

    /// <summary>收起态最多展示的瓦片数（最新 4 个），超出折叠为 +N 溢出格。</summary>
    private const int BuffDockMaxTiles = 4;

    /// <summary>
    /// Boss 血条阶段刻度线（70%/30%，§4.2）：随血条显隐的覆盖层。
    /// </summary>
    public partial class BossBarTicks : Control
    {
        private readonly float[] _ratios = { 0.7f, 0.3f };

        public override void _Draw()
        {
            foreach (var r in _ratios)
            {
                var x = Size.X * r;
                DrawLine(new Vector2(x, -2.0f), new Vector2(x, Size.Y + 2.0f), new Color(1.0f, 1.0f, 1.0f, 0.55f), 2.0f);
            }
        }
    }

    public override void _Ready()
    {
        AddToGroup("hud");
        _main = GetParent<Main>(); // A5：HUD 是 main 子节点，_ready 直接缓存，替代 0.1s 轮询现找
        _scoreLabel = GetNode<Label>("ScoreLabel");
        _killsLabel = GetNode<Label>("KillsLabel");
        _difficultyLabel = GetNode<Label>("DifficultyLabel");
        _livesLabel = GetNode<Label>("LivesLabel");
        _hpBar = GetNode<SegmentedBar>("HpBar");
        _bossBar = GetNode<SegmentedBar>("BossBar");
        _fuelBar = GetNode<SegmentedBar>("FuelBar");
        _dashBar = GetNode<SegmentedBar>("DashBar");
        _parryBar = GetNode<SegmentedBar>("ParryBar");
        _fuelTag = GetNode<Label>("FuelTag");
        _dashTag = GetNode<Label>("DashTag");
        _parryTag = GetNode<Label>("ParryTag");
        _dockTag = GetNode<Label>("DockTag");
        _pollInterval = Mathf.Max((float)GameState.Instance.Cfg("effects.hud_poll_interval", _pollInterval).AsDouble(), 0.01f); // H15：≤0 节流失效
        _bossBarSegments = Mathf.Max((int)GameState.Instance.Cfg("hud.boss_bar_segments", _bossBarSegments).AsInt64(), 1);
        _hitFlashAlpha = (float)GameState.Instance.Cfg("effects.hit_flash.alpha", _hitFlashAlpha).AsDouble();
        _hitFlashTime = (float)GameState.Instance.Cfg("effects.hit_flash.time", _hitFlashTime).AsDouble();
        _lowHpRatio = (float)GameState.Instance.Cfg("effects.low_hp.ratio", _lowHpRatio).AsDouble();
        _lowHpPulseMin = (float)GameState.Instance.Cfg("effects.low_hp.pulse_min", _lowHpPulseMin).AsDouble();
        _lowHpPulseMax = (float)GameState.Instance.Cfg("effects.low_hp.pulse_max", _lowHpPulseMax).AsDouble();
        _lowHpPulsePeriod = Mathf.Max((float)GameState.Instance.Cfg("effects.low_hp.pulse_period", _lowHpPulsePeriod).AsDouble(), 0.01f); // H15：=0 sin NaN
        foreach (var label in new[] { _scoreLabel, _killsLabel, _difficultyLabel, _livesLabel })
        {
            label.AddThemeFontOverride("font", Font);
        }

        _scoreLabel.AddThemeFontSizeOverride("font_size", UITheme.FontScore);
        _scoreLabel.AddThemeColorOverride("font_color", UITheme.Text);
        _killsLabel.AddThemeFontSizeOverride("font_size", UITheme.FontHud);
        _killsLabel.AddThemeColorOverride("font_color", UITheme.TextDim);
        _difficultyLabel.AddThemeFontSizeOverride("font_size", UITheme.FontHud);
        _difficultyLabel.AddThemeColorOverride("font_color", UITheme.Accent);
        _livesLabel.AddThemeFontSizeOverride("font_size", UITheme.FontHudL);
        _livesLabel.AddThemeColorOverride("font_color", UITheme.Text);
        foreach (var tag in new[] { _fuelTag, _dashTag, _dockTag })
        {
            tag.AddThemeFontOverride("font", Font);
            tag.AddThemeFontSizeOverride("font_size", UITheme.FontSmall);
            tag.AddThemeColorOverride("font_color", UITheme.TextDim);
        }

        _dockTag.AddThemeColorOverride("font_color", UITheme.Accent);
        _hpBar.FillColor = UITheme.Accent;
        _fuelBar.FillColor = UITheme.Accent;
        _dashBar.FillColor = UITheme.Accent;
        // 机制四：弹反能量槽（金色，DashBar 下方；满格=可用，流程清空，冷却匀速充能）
        _parryBar.FillColor = UITheme.AccentGold;
        _parryTag.AddThemeFontOverride("font", Font);
        _parryTag.AddThemeFontSizeOverride("font_size", UITheme.FontSmall);
        _parryTag.AddThemeColorOverride("font_color", UITheme.TextDim);
        // HpBar 全息化（META_HUD_DESIGN §4.3/§6 明示层）：底盘更透 + 填充段 ADD 伪泛光
        _hpBar.EmptyColor = new Color(0.05f, 0.09f, 0.14f, 0.25f);
        var hpHolo = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        _hpBar.Material = hpHolo;
        var gs = GameState.Instance!;
        gs.Connect("ScoreChanged", Callable.From<int>(OnScoreChanged));
        gs.Connect("HealthChanged", Callable.From<float>(OnHealthChanged));
        gs.Connect("DifficultyChanged", Callable.From<float>(OnDifficultyChanged));
        gs.Connect("DifficultySelected", Callable.From<StringName>(OnDifficultySelected));
        gs.Connect("LocaleChanged", Callable.From(OnLocaleChanged));
        OnScoreChanged(GameState.Instance.Score);
        OnHealthChanged((float)GameState.Instance.Health);
        RefreshDifficultyLabel();
        _fuelTag.Text = (string)Tr("UI_FUEL");
        _dashTag.Text = (string)Tr("UI_DASH");
        _parryTag.Text = (string)Tr("UI_PARRY");
        BuildBackplates();
        BuildBanner();
        BuildMagazineBar();
        // 返航蓄力提示（底部居中）
        _homeChargeLabel = new Label
        {
            Position = new Vector2(-140.0f, -120.0f),
            CustomMinimumSize = new Vector2(280.0f, 0.0f),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible = false,
        };
        _homeChargeLabel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _homeChargeLabel.AddThemeFontOverride("font", Font);
        _homeChargeLabel.AddThemeFontSizeOverride("font_size", 24);
        _homeChargeLabel.AddThemeColorOverride("font_color", UITheme.ChargeCyan);
        AddChild(_homeChargeLabel);
        // 放弃出击蓄力提示（底部居中，返航提示上方，红色警示）
        _giveUpLabel = new Label
        {
            Position = new Vector2(-140.0f, -164.0f),
            CustomMinimumSize = new Vector2(280.0f, 0.0f),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible = false,
        };
        _giveUpLabel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _giveUpLabel.AddThemeFontOverride("font", Font);
        _giveUpLabel.AddThemeFontSizeOverride("font_size", 24);
        _giveUpLabel.AddThemeColorOverride("font_color", UITheme.Danger);
        AddChild(_giveUpLabel);
        // 提前离舰蓄力进度条（驻留母舰时长按 H，底部居中，放弃提示上方）
        _earlyLeaveBox = new VBoxContainer
        {
            Position = new Vector2(-140.0f, -220.0f),
            CustomMinimumSize = new Vector2(280.0f, 0.0f),
            Visible = false,
        };
        _earlyLeaveBox.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _earlyLeaveBox.AddThemeConstantOverride("separation", 6);
        AddChild(_earlyLeaveBox);
        _earlyLeaveLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _earlyLeaveLabel.AddThemeFontOverride("font", Font);
        _earlyLeaveLabel.AddThemeFontSizeOverride("font_size", 24);
        _earlyLeaveLabel.AddThemeColorOverride("font_color", UITheme.WarnYellow);
        _earlyLeaveBox.AddChild(_earlyLeaveLabel);
        var barBg = new ColorRect
        {
            Color = new Color(1.0f, 1.0f, 1.0f, 0.15f),
            CustomMinimumSize = new Vector2(280.0f, 10.0f),
        };
        _earlyLeaveBox.AddChild(barBg);
        _earlyLeaveFill = new ColorRect { Color = UITheme.WarnYellow };
        _earlyLeaveFill.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        _earlyLeaveFill.AnchorRight = 0.0f;
        barBg.AddChild(_earlyLeaveFill);
        // 名牌行占位：血条整体下移 30px，上方留出一行型号 + 阶段标签
        _bossBar.OffsetTop += 30.0f;
        _bossBar.OffsetBottom += 30.0f;
        // Boss 名牌（型号 + 阶段，血条子节点随其显隐；事件与 Boss 互斥不会同屏）
        // 深色底衬保证叠在 Boss 机体/辉光上时可读
        var namePlate = new PanelContainer
        {
            Position = new Vector2(-300.0f, -34.0f),
            CustomMinimumSize = new Vector2(600.0f, 0.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        namePlate.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        var plateStyle = new StyleBoxFlat { BgColor = new Color(0.02f, 0.05f, 0.09f, 0.6f) };
        namePlate.AddThemeStyleboxOverride("panel", plateStyle);
        _bossName = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _bossName.AddThemeFontOverride("font", Font);
        _bossName.AddThemeFontSizeOverride("font_size", UITheme.FontHud);
        _bossName.AddThemeColorOverride("font_color", UITheme.Text);
        namePlate.AddChild(_bossName);
        _bossBar.AddChild(namePlate);
        // Boss 血条阶段刻度线（70%/30%，覆盖在血条上随其显隐）
        var ticks = new BossBarTicks();
        ticks.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ticks.MouseFilter = Control.MouseFilterEnum.Ignore;
        _bossBar.AddChild(ticks);
        // Boss 血条背板：名牌 + 血条整体纳入切角面板（与角落板块同一语系，随血条显隐；
        // 名牌 abs y 12..42、血条 46..74 → 背板 y 4..92 上下留白）
        _bossPlate = new ChamferedPanel
        {
            Position = new Vector2(-320.0f, 4.0f),
            Size = new Vector2(640.0f, 88.0f),
            Brackets = true,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _bossPlate.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        AddChild(_bossPlate);
        MoveChild(_bossPlate, _bossBar.GetIndex()); // 绘制序压在血条之下
        // Boss 逃跑倒计时（血条下方，剩余 ≤10s 起显示，红色闪烁）
        _bossCountdown = new Label
        {
            Position = new Vector2(-100.0f, 78.0f),
            CustomMinimumSize = new Vector2(200.0f, 0.0f),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible = false,
        };
        _bossCountdown.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _bossCountdown.AddThemeFontOverride("font", Font);
        _bossCountdown.AddThemeFontSizeOverride("font_size", UITheme.FontHudL);
        _bossCountdown.AddThemeColorOverride("font_color", UITheme.Danger);
        AddChild(_bossCountdown);
        BuildEventBar();
        BuildVignette();
        BuildBuffDock();
        BuildInfoBanner();
        gs.Connect("BuffsChanged", Callable.From(RebuildBuffDock));
        gs.Connect("KeyBindingsChanged", Callable.From(RefreshBuffTag));
        RebuildBuffDock();
        _hpBarRest = _hpBar.Position;
        _buffDockRest = _buffDockWrap.Position;
    }

    /// <summary>精英炮塔事件计时条（顶部居中，Boss 血条下方；与 Boss 互斥不会同屏）。</summary>
    private void BuildEventBar()
    {
        _eventBox = new VBoxContainer
        {
            Position = new Vector2(-300.0f, 52.0f),
            CustomMinimumSize = new Vector2(600.0f, 0.0f),
            Visible = false,
        };
        _eventBox.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _eventBox.AddThemeConstantOverride("separation", 4);
        AddChild(_eventBox);
        _eventTitle = new Label
        {
            Text = (string)Tr("ETV_TITLE"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _eventTitle.AddThemeFontOverride("font", Font);
        _eventTitle.AddThemeFontSizeOverride("font_size", 18);
        _eventTitle.AddThemeColorOverride("font_color", UITheme.EventMagenta);
        _eventBox.AddChild(_eventTitle);
        _eventBar = new SegmentedBar
        {
            CustomMinimumSize = new Vector2(600.0f, 12.0f),
            Segments = 30,
            FillColor = UITheme.EventMagenta,
        };
        _eventBox.AddChild(_eventBar);
        _eventTurretsLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _eventTurretsLabel.AddThemeFontOverride("font", Font);
        _eventTurretsLabel.AddThemeFontSizeOverride("font_size", UITheme.FontSmall);
        _eventTurretsLabel.AddThemeColorOverride("font_color", UITheme.TextDim);
        _eventBox.AddChild(_eventTurretsLabel);
    }

    /// <summary>事件倒计时开始：显示计时条（total = 炮台总数）。</summary>
    public void ShowEventBar(int total)
    {
        _eventTitle.Text = (string)Tr("ETV_TITLE");
        _eventBar.Value = 100.0f;
        _lastEventAlive = -1;
        _eventTurretsLabel.Text = GdFormat.Format((string)Tr("ETV_TURRETS"), total);
        _eventBox.Visible = true;
    }

    /// <summary>事件进行：剩余时间填充 + 剩余炮台数（约 0.1s 节流由调用侧控制）。</summary>
    public void UpdateEventBar(float timeLeft, float duration, int alive)
    {
        if (!_eventBox.Visible)
        {
            return;
        }

        _eventBar.Value = Mathf.Clamp(timeLeft / Mathf.Max(duration, 0.01f), 0.0f, 1.0f) * 100.0f;
        if (alive != _lastEventAlive)
        {
            _lastEventAlive = alive;
            _eventTurretsLabel.Text = GdFormat.Format((string)Tr("ETV_TURRETS"), alive);
        }
    }

    public void HideEventBar()
    {
        _eventBox.Visible = false;
    }

    /// <summary>放弃出击蓄力进度：ratio &lt; 0 隐藏，否则显示百分比。</summary>
    public void SetGiveUpCharge(float ratio)
    {
        if (ratio < 0.0f)
        {
            _giveUpLabel.Visible = false;
        }
        else
        {
            _giveUpLabel.Visible = true;
            _giveUpLabel.Text = GdFormat.Format((string)Tr("GIVE_UP_CHARGE"), (int)(Mathf.Clamp(ratio, 0.0f, 1.0f) * 100.0f));
        }
    }

    /// <summary>返航蓄力进度：ratio &lt; 0 隐藏，否则显示百分比。</summary>
    public void SetHomeCharge(float ratio)
    {
        if (ratio < 0.0f)
        {
            _homeChargeLabel.Visible = false;
        }
        else
        {
            _homeChargeLabel.Visible = true;
            _homeChargeLabel.Text = GdFormat.Format((string)Tr("HOME_CHARGE"), (int)(Mathf.Clamp(ratio, 0.0f, 1.0f) * 100.0f));
        }
    }

    /// <summary>提前离舰蓄力进度条（驻留母舰时长按 H）：ratio &lt; 0 隐藏，否则显示百分比 + 进度条。</summary>
    public void SetEarlyLeaveCharge(float ratio)
    {
        if (ratio < 0.0f)
        {
            _earlyLeaveBox.Visible = false;
        }
        else
        {
            var r = Mathf.Clamp(ratio, 0.0f, 1.0f);
            _earlyLeaveBox.Visible = true;
            _earlyLeaveLabel.Text = GdFormat.Format((string)Tr("MS_EARLY_LEAVE"), (int)(r * 100.0f));
            _earlyLeaveFill.AnchorRight = r;
        }
    }

    private void BuildMagazineBar()
    {
        // 弹匣格子条（驻留时显示）：10 格分段
        _magBox = new HBoxContainer
        {
            Position = new Vector2(340.0f, -54.0f),
            Visible = false,
        };
        _magBox.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _magBox.AddThemeConstantOverride("separation", 3);
        for (var i = 0; i < 10; i++)
        {
            var cell = new ColorRect
            {
                CustomMinimumSize = new Vector2(18.0f, 14.0f),
                Color = UITheme.Accent,
            };
            _magBox.AddChild(cell);
            _magCellsNodes.Add(cell);
        }

        AddChild(_magBox);
    }

    /// <summary>L（buff_panel）切换 buff 滚动栏；暂停态下 HUD 不处理输入（process 继承）。</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("buff_panel"))
        {
            ToggleBuffPanel();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        // U05（2026-08-09 审计）：Boss 四 [Signal] 配对断开——Godot 信号不随接收方释放
        // 自动断开，Hud 先于 Boss 释放时存活期信号回调已释放 Hud
        if (_boss != null && GodotObject.IsInstanceValid(_boss))
        {
            _boss.HealthChanged -= OnBossHealthChanged;
            _boss.Died -= OnBossDied;
            _boss.Enraged -= OnBossEnraged;
            _boss.PhaseChanged -= OnBossPhaseChanged;
        }

        // C22 模式（M5）：GameState 信号显式断开——本类此前缺 _ExitTree（其他 C# UI 均有），
        // 退出时 GameState 先于本节点释放的时序下连接悬空可致退出 segfault（实测定位）
        var gs = GameState.Instance;
        if (gs == null)
        {
            return;
        }

        var score = Callable.From<int>(OnScoreChanged);
        var health = Callable.From<float>(OnHealthChanged);
        var diff = Callable.From<float>(OnDifficultyChanged);
        var diffSel = Callable.From<StringName>(OnDifficultySelected);
        var locale = Callable.From(OnLocaleChanged);
        var buffs = Callable.From(RebuildBuffDock);
        var keybinds = Callable.From(RefreshBuffTag);
        if (gs.IsConnected("ScoreChanged", score))
        {
            gs.Disconnect("ScoreChanged", score);
        }

        if (gs.IsConnected("HealthChanged", health))
        {
            gs.Disconnect("HealthChanged", health);
        }

        if (gs.IsConnected("DifficultyChanged", diff))
        {
            gs.Disconnect("DifficultyChanged", diff);
        }

        if (gs.IsConnected("DifficultySelected", diffSel))
        {
            gs.Disconnect("DifficultySelected", diffSel);
        }

        if (gs.IsConnected("LocaleChanged", locale))
        {
            gs.Disconnect("LocaleChanged", locale);
        }

        if (gs.IsConnected("BuffsChanged", buffs))
        {
            gs.Disconnect("BuffsChanged", buffs);
        }

        if (gs.IsConnected("KeyBindingsChanged", keybinds))
        {
            gs.Disconnect("KeyBindingsChanged", keybinds);
        }
    }

    public override void _Process(double delta)
    {
        // 晕影/受击红闪每帧更新（需连续衰减与脉动）；其余仪表类按 POLL_INTERVAL（0.1s）降频，
        // 文本类由信号驱动（见 _ready 连接）
        var d = (float)delta;
        UpdateVignette(d);
        _pollTimer -= d;
        if (_pollTimer > 0.0f)
        {
            return;
        }

        _pollTimer = _pollInterval;
        // Boss 逃跑倒计时（约 0.1s 节流轮询，§4.5）：血条存在且剩余 ≤10s 起显示
        if (_boss != null && GodotObject.IsInstanceValid(_boss) && _bossBar.Visible)
        {
            var remaining = _boss.EscapeRemaining();
            if (_boss.IsInFight() && !_boss.IsEscaping() && remaining <= _boss.EscapeCountdownFrom && remaining > 0.0f)
            {
                _bossCountdown.Visible = true;
                _bossCountdown.Text = GdFormat.Format("%d", Mathf.CeilToInt(remaining));
                var cm = _bossCountdown.Modulate;
                cm.A = Time.GetTicksMsec() / 500 % 2 == 0 ? 1.0f : 0.45f;
                _bossCountdown.Modulate = cm;
            }
            else
            {
                _bossCountdown.Visible = false;
            }
        }
        else
        {
            _bossCountdown.Visible = false;
        }

        var player = GameState.Instance.PlayerRef as Player;
        if (player == null)
        {
            return;
        }

        var fuel = player.FuelRatio(); // M3c：Player 迁 C#，动态调用显式 float 型
        // P1-3（2026-08-05 审计）：值变化才写 setter（ProgressBar setter 内部 queue_redraw，
        // 0.1s 轮询下值未变也触发无意义重绘；epsilon 守卫只写变化帧）
        var fuelVal = fuel * 100.0f;
        if (Mathf.Abs(fuelVal - _fuelBar.Value) > 0.001f)
        {
            _fuelBar.Value = fuelVal;
        }

        _fuelBar.FillColor = fuel < 0.3f ? UITheme.Danger : UITheme.Accent;
        var dashVal = player.DashReadyRatio() * 100.0f;
        if (Mathf.Abs(dashVal - _dashBar.Value) > 0.001f)
        {
            _dashBar.Value = dashVal;
        }

        // 机制四：弹反能量槽（满格=可用；流程期清空；冷却匀速充能——player.parry_energy_ratio）
        var parryVal = player.ParryEnergyRatio() * 100.0f;
        if (Mathf.Abs(parryVal - _parryBar.Value) > 0.001f)
        {
            _parryBar.Value = parryVal;
        }

        if (_main != null)
        {
            var dockText = _main.DockStatusText();
            if (dockText != _lastDockText)
            {
                _dockTag.Text = dockText;
                _lastDockText = dockText;
            }

            UpdateMagazineBar(_main);
        }
    }

    private void UpdateMagazineBar(Main main)
    {
        Mothership? ms = main.Mothership();
        if (ms != null && (int)ms.GetState() == Mothership.GetStateStay())
        {
            _magBox.Visible = true;
            if (ms.GetMagCells() == _lastMagCells)
            {
                return;
            }

            _lastMagCells = ms.GetMagCells();
            for (var i = 0; i < _magCellsNodes.Count; i++)
            {
                _magCellsNodes[i].Color = i < ms.GetMagCells() ? UITheme.Accent : new Color(0.05f, 0.09f, 0.14f, 0.8f);
            }
        }
        else
        {
            _magBox.Visible = false;
            _lastMagCells = -1;
        }
    }

    /// <summary>左上分数块、左下状态块与右上难度块的切角背板 + 小标签（标签置于背板上方外侧，不与边框/数值重叠）。</summary>
    private void BuildBackplates()
    {
        var scorePlate = new ChamferedPanel
        {
            Position = new Vector2(10.0f, 24.0f),
            Size = new Vector2(230.0f, 92.0f),
        };
        AddChild(scorePlate);
        MoveChild(scorePlate, 0);
        // 大数值下移，给标签行留位
        _scoreLabel.Position = new Vector2(24.0f, 30.0f);
        _killsLabel.Position = new Vector2(24.0f, 72.0f);
        var scoreTag = MakeCornerTag((string)Tr("UI_SCORE_TAG"));
        scoreTag.Position = new Vector2(24.0f, 6.0f);
        AddChild(scoreTag);
        var statusPlate = new ChamferedPanel
        {
            Position = new Vector2(10.0f, -134.0f),
            Size = new Vector2(560.0f, 120.0f),
        };
        statusPlate.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        AddChild(statusPlate);
        MoveChild(statusPlate, 0);
        var livesTag = MakeCornerTag((string)Tr("UI_LIVES_TAG"));
        livesTag.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        livesTag.Position = new Vector2(24.0f, -156.0f);
        AddChild(livesTag);
        // 仪表区与母舰状态区之间的竖分隔线（分区结构感）
        var statusDivider = new ColorRect
        {
            Color = UITheme.AccentDim,
            Position = new Vector2(374.0f, -110.0f),
            Size = new Vector2(1.0f, 84.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        statusDivider.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        AddChild(statusDivider);
        // 右上难度背板：与分数块同语系（原浮空文字难以在亮背景上阅读）
        var diffPlate = new ChamferedPanel
        {
            Position = new Vector2(-240.0f, 24.0f),
            Size = new Vector2(230.0f, 44.0f),
        };
        diffPlate.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        AddChild(diffPlate);
        MoveChild(diffPlate, 0);
        _difficultyLabel.OffsetLeft = -228.0f;
        _difficultyLabel.OffsetTop = 24.0f;
        _difficultyLabel.OffsetRight = -22.0f;
        _difficultyLabel.OffsetBottom = 68.0f;
        _difficultyLabel.VerticalAlignment = VerticalAlignment.Center;
        var diffTag = MakeCornerTag((string)Tr("UI_DIFF_TAG"));
        diffTag.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        diffTag.Position = new Vector2(-236.0f, 6.0f);
        AddChild(diffTag);
        // 刷新时同步小标签语言
        _tagLabels = new[] { scoreTag, livesTag, diffTag };
        _tagKeys = new[] { new StringName("UI_SCORE_TAG"), new StringName("UI_LIVES_TAG"), new StringName("UI_DIFF_TAG") };
    }

    /// <summary>角落板块小标签（分数/生命/难度共用样式）。</summary>
    private Label MakeCornerTag(string text)
    {
        var tag = new Label { Text = text };
        tag.AddThemeFontOverride("font", Font);
        tag.AddThemeFontSizeOverride("font_size", UITheme.FontSmall);
        tag.AddThemeColorOverride("font_color", UITheme.Accent);
        return tag;
    }

    /// <summary>语言切换时重刷全部角落小标签。</summary>
    private void RefreshTagLabels()
    {
        for (var i = 0; i < Mathf.Min(_tagLabels.Length, _tagKeys.Length); i++)
        {
            _tagLabels[i].Text = (string)Tr(_tagKeys[i]);
        }
    }

    /// <summary>世界坐标处的飘字提示（补给完成、里程碑等）。</summary>
    public void ShowPopup(string text, Vector2 worldPos)
    {
        var label = new Label { Text = text };
        label.AddThemeFontOverride("font", Font);
        label.AddThemeFontSizeOverride("font_size", UITheme.FontHudL);
        label.AddThemeColorOverride("font_color", UITheme.Text);
        // 世界坐标 → CanvasLayer 屏幕坐标（修视角 zoom≠1 时错位）
        label.Position = GetViewport().GetCanvasTransform() * worldPos - new Vector2(40.0f, 40.0f);
        AddChild(label);
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(label, "position:y", label.Position.Y - 50.0f, 0.8);
        tween.TweenProperty(label, "modulate:a", 0.0f, 0.8);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    private void BuildBanner()
    {
        _bannerPlate = new ChamferedPanel
        {
            Position = new Vector2(-300.0f, 140.0f),
            Size = new Vector2(600.0f, 80.0f),
            Brackets = true,
            BgColor = UITheme.BannerDangerBg,
            BorderColor = new Color(UITheme.Danger, 0.6f),
            BracketColor = UITheme.Danger,
            Visible = false,
        };
        _bannerPlate.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        AddChild(_bannerPlate);
        _bannerLabel = new Label
        {
            Position = new Vector2(-300.0f, 140.0f),
            CustomMinimumSize = new Vector2(600.0f, 80.0f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visible = false,
        };
        _bannerLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _bannerLabel.AddThemeFontOverride("font", Font);
        _bannerLabel.AddThemeFontSizeOverride("font_size", 40);
        _bannerLabel.AddThemeColorOverride("font_color", UITheme.Danger);
        AddChild(_bannerLabel);
    }

    /// <summary>Boss 出场警告：闪烁 2s（与 spawner 的 2s 预警同步），随后淡出。</summary>
    public void ShowBossBanner()
    {
        ShowWarning((string)Tr("WARN_BOSS"));
    }

    /// <summary>母舰弹匣不足警告（≤4 格时触发一次）。</summary>
    public void ShowMagazineWarning()
    {
        GameState.Instance.PlaySfx(GameState.Instance.SFX_PLAYER_HIT);
        ShowWarning((string)Tr("WARN_MAG"));
    }

    /// <summary>对外公开接口（A1 修复）：Boss 逃跑警告经公开入口触发。</summary>
    public void ShowWarning(string text)
    {
        // H09（健壮性审核）：互斥缓存——旧警告 tween 仍在跑时 kill 再建，防同属性竞争与 hide 竞态。
        // 背板闪烁（t1）/label 闪烁（t2）与淡出全部纳入 _warning_tween：闪烁阶段缓存 blink，
        // 其 finished 后缓存 fade，任意时刻 kill 的都是当前活跃阶段（旧 fade 被杀不会再 hide 压制新警告）
        if (_warningTween != null && _warningTween.IsValid())
        {
            _warningTween.Kill();
        }

        _bannerLabel.Text = text;
        _bannerPlate.Visible = true;
        _bannerLabel.Visible = true;
        var pm = _bannerPlate.Modulate;
        pm.A = 1.0f;
        _bannerPlate.Modulate = pm;
        var lm = _bannerLabel.Modulate;
        lm.A = 1.0f;
        _bannerLabel.Modulate = lm;
        // 闪烁对（0.25→1.0）循环 4 次 ≈2s（与 spawner 预警同步）；set_loops 作用于整链，
        // 旧实现把淡出+hide 也包进循环，首轮末尾 hide 即永久隐藏——淡出移出循环外
        var blink = CreateTween();
        blink.TweenProperty(_bannerPlate, "modulate:a", 0.25f, 0.25);
        blink.Parallel().TweenProperty(_bannerLabel, "modulate:a", 0.25f, 0.25);
        blink.TweenProperty(_bannerPlate, "modulate:a", 1.0f, 0.25);
        blink.Parallel().TweenProperty(_bannerLabel, "modulate:a", 1.0f, 0.25);
        blink.SetLoops(4);
        _warningTween = blink;
        blink.Finished += () =>
        {
            var fade = CreateTween();
            fade.SetParallel(true);
            fade.TweenProperty(_bannerPlate, "modulate:a", 0.0f, 0.4);
            fade.TweenProperty(_bannerLabel, "modulate:a", 0.0f, 0.4);
            fade.Chain().TweenCallback(Callable.From(_bannerPlate.Hide));
            fade.TweenCallback(Callable.From(_bannerLabel.Hide));
            _warningTween = fade;
        };
    }

    /// <summary>绑定 Boss 血条（spawner 经 boss_spawned 信号调用）：登记分段参数并连接 Boss 信号。</summary>
    public void ShowBossBar(Boss boss)
    {
        _bossBar.FillColor = UITheme.Accent; // 重置上一只 Boss 狂暴留下的红色
        // 机制三：分段血条——段数/段权/段色按配置登记（段界 = 阶段阈值宽占比）
        _bossBar.Segments = _bossBarSegments;
        _bossBar.SegWeights = BossSegWeights;
        _bossBar.SegColors = BossSegColors;
        _bossBar.Visible = true;
        _bossPlate.Visible = true;
        _bossBar.Value = 100.0f;
        _boss = boss;
        _bossCountdown.Visible = false;
        _bossPhase = FightPhaseP1; // M3d：Boss.FightPhase.P1（C# 枚举经常量，见顶部注释）
        boss.HealthChanged += OnBossHealthChanged; // M3d：C# [Signal] 以 PascalCase 注册
        boss.Died += OnBossDied;
        boss.Enraged += OnBossEnraged;
        boss.PhaseChanged += OnBossPhaseChanged;
        RefreshBossName();
    }

    private void OnScoreChanged(int newScore)
    {
        _scoreLabel.Text = GdFormat.Format((string)Tr("UI_SCORE"), newScore);
        _killsLabel.Text = GdFormat.Format((string)Tr("UI_KILLS"), GameState.Instance.Kills);
    }

    private void OnHealthChanged(float newHealth)
    {
        var maxHp = _cachedMaxHp;
        // 受击红闪：HP 下降沿触发 alpha 脉冲（tween 衰减，低血脉动取两者较大值）
        if (_lastHpValue >= 0.0f && newHealth < _lastHpValue)
        {
            HitFlash = _hitFlashAlpha;
            if (_hitTween != null && _hitTween.IsValid())
            {
                _hitTween.Kill();
            }

            _hitTween = CreateTween();
            _hitTween.TweenProperty(this, "HitFlash", 0.0f, _hitFlashTime);
        }

        _lastHpValue = newHealth;
        _hpBar.Value = Mathf.Clamp(newHealth / maxHp, 0.0f, 1.0f) * 100.0f;
        _hpBar.FillColor = newHealth / maxHp < 0.3f ? UITheme.Danger : UITheme.Accent;
        // P0-2：回血每帧触发信号，仅整数档位/上限变化时才格式化（连续帧 HP 小数差异不刷新文本）
        var hpInt = Mathf.CeilToInt(newHealth);
        var maxInt = (int)maxHp;
        if (hpInt != _lastHpInt || maxInt != _lastMaxInt)
        {
            _lastHpInt = hpInt;
            _lastMaxInt = maxInt;
            var text = GdFormat.Format("%d/%d", hpInt, maxInt);
            if (text != _lastHpText)
            {
                _livesLabel.Text = text;
                _lastHpText = text;
            }
        }
    }

    private string _lastHpText = "";
    private int _lastHpInt = -1; // P0-2：整数档位守卫，回血逐帧信号时跳过无变化格式化
    private int _lastMaxInt = -1; // P0-2：上限整数守卫（extra_life 叠加改变 max_hp 时强制刷新）

    private void OnDifficultyChanged(float _newMultiplier)
    {
        RefreshDifficultyLabel();
    }

    private void OnDifficultySelected(StringName _difficulty)
    {
        RefreshDifficultyLabel();
    }

    private void OnLocaleChanged()
    {
        OnScoreChanged(GameState.Instance.Score);
        OnHealthChanged((float)GameState.Instance.Health);
        RefreshDifficultyLabel();
        _fuelTag.Text = (string)Tr("UI_FUEL");
        _dashTag.Text = (string)Tr("UI_DASH");
        _parryTag.Text = (string)Tr("UI_PARRY");
        RefreshTagLabels();
        if (_buffTag != null)
        {
            RefreshBuffTag();
        }

        if (_buffPanelTitle != null)
        {
            _buffPanelTitle.Text = (string)Tr("UI_BUFFS_TITLE");
        }

        if (_eventBox != null && _eventBox.Visible)
        {
            _eventTitle.Text = (string)Tr("ETV_TITLE");
            _eventTurretsLabel.Text = GdFormat.Format((string)Tr("ETV_TURRETS"), Mathf.Max(_lastEventAlive, 0));
        }

        RebuildBuffDock(true);
        if (_bossBar.Visible)
        {
            RefreshBossName();
        }
    }

    /// <summary>难度标签：Boss 击杀乘数 + 难度档位（如「难度 x1.00 · 中」）。</summary>
    private void RefreshDifficultyLabel()
    {
        _difficultyLabel.Text = GdFormat.Format(
            (string)Tr("UI_DIFF_FMT"),
            (float)GameState.Instance.DifficultyMultiplier,
            (string)GameState.Instance.DifficultyLabel());
    }

    private void OnBossHealthChanged(float current, float maximum)
    {
        _bossBar.Value = Mathf.Clamp(current / maximum, 0.0f, 1.0f) * 100.0f;
    }

    private void OnBossDied()
    {
        _bossBar.Visible = false;
        _bossPlate.Visible = false;
        _bossCountdown.Visible = false;
        _boss = null;
    }

    private void OnBossEnraged()
    {
        _bossBar.FillColor = UITheme.Danger;
        RefreshBossName();
    }

    /// <summary>阶段切换瞬间血条短闪（§4.2）。</summary>
    private void OnBossPhaseChanged(int phase)
    {
        _bossPhase = phase;
        RefreshBossName();
        _bossBar.Modulate = new Color(2.2f, 2.2f, 2.2f);
        var tween = CreateTween();
        tween.TweenProperty(_bossBar, "modulate", Colors.White, 0.3);
    }

    /// <summary>Boss 名牌：型号名 + 阶段标签（狂暴整行 DANGER）。</summary>
    private void RefreshBossName()
    {
        if (_boss == null || !GodotObject.IsInstanceValid(_boss))
        {
            return;
        }

        string phaseText;
        if (_bossPhase == FightPhaseP2) // M3d：Boss.FightPhase.P2（C# 枚举经常量）
        {
            phaseText = "P2";
        }
        else if (_bossPhase == FightPhaseEnrage) // M3d：Boss.FightPhase.ENRAGE（C# 枚举经常量）
        {
            phaseText = (string)Tr("BOSS_PHASE_ENRAGE");
        }
        else
        {
            phaseText = "P1";
        }

        _bossName.Text = GdFormat.Format("%s · %s", (string)Tr(GdFormat.Format("BOSS_TYPE_%d", _boss.BossType)), phaseText);
        _bossName.AddThemeColorOverride("font_color", _bossPhase == FightPhaseEnrage ? UITheme.Danger : UITheme.Text);
    }

    /// <summary>受击/低血屏幕反馈：全屏径向渐变（无新资产，GradientTexture2D 程序化）。</summary>
    private void BuildVignette()
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(1.0f, 0.2f, 0.3f, 0.0f));
        gradient.SetColor(1, new Color(1.0f, 0.2f, 0.35f, 1.0f));
        var tex = new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1.0f, 0.5f),
            Width = 512,
            Height = 512,
        };
        _vignette = new TextureRect
        {
            Texture = tex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _vignette.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var vm = _vignette.Modulate;
        vm.A = 0.0f;
        _vignette.Modulate = vm;
        AddChild(_vignette);
        MoveChild(_vignette, 0);
    }

    /// <summary>每帧算 vignette alpha：受击红闪衰减与低血正弦脉动取较大值，恢复后归 0。</summary>
    private void UpdateVignette(float delta)
    {
        if (_vignette == null)
        {
            return;
        }

        // LOD0 移交 MetaFX 后处理（D2），旧晕影恒 0；非 0（回退/MetaFX 离场）保留现状
        if (GameState.Instance.MetaFxLod == 0)
        {
            if (_vignette.Modulate.A > 0.0f)
            {
                var vm = _vignette.Modulate;
                vm.A = 0.0f;
                _vignette.Modulate = vm;
            }

            return;
        }

        var alpha = HitFlash;
        var maxHp = _cachedMaxHp;
        if ((float)GameState.Instance.Health > 0.0f && (float)GameState.Instance.Health < maxHp * _lowHpRatio)
        {
            _pulseTime += delta;
            var s = (Enemy.SinFast(_pulseTime * Mathf.Tau / _lowHpPulsePeriod) + 1.0f) * 0.5f;
            alpha = Mathf.Max(alpha, Mathf.Lerp(_lowHpPulseMin, _lowHpPulseMax, s));
        }
        else
        {
            _pulseTime = 0.0f;
        }

        var vm2 = _vignette.Modulate;
        vm2.A = alpha;
        _vignette.Modulate = vm2;
    }

    /// <summary>Meta HUD DYING 抖动（D9）：只抖 _hp_bar 与 buff 坞包装两个控件（±px，80ms burst）。</summary>
    public void MetaJitter(float px)
    {
        if (_jitterTween != null && _jitterTween.IsValid())
        {
            _jitterTween.Kill();
        }

        var off = new Vector2((float)GD.RandRange(-px, px), (float)GD.RandRange(-px, px));
        _hpBar.Position = _hpBarRest + off;
        _buffDockWrap.Position = _buffDockRest + off * 0.5f;
        _jitterTween = CreateTween();
        _jitterTween.TweenProperty(_hpBar, "position", _hpBarRest, 0.08);
        _jitterTween.Parallel().TweenProperty(_buffDockWrap, "position", _buffDockRest, 0.08);
    }

    /// <summary>
    /// 右下 buff 区：收起态单行瓦片（最新 4 个 + 溢出 +N，标签带 [L] 快捷键提示），
    /// L 键展开右侧滚动栏（全部 buff 明细，不暂停对局）；与左下状态区/底部蓄力提示分角隔离。
    /// </summary>
    private void BuildBuffDock()
    {
        _buffDockWrap = new Control
        {
            Position = new Vector2(-20.0f, -44.0f), // 底部留 28px 给标签行（右缘与标签同 20px 边距）
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _buffDockWrap.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        AddChild(_buffDockWrap);
        _buffDock = new GridContainer { Columns = BuffDockMaxTiles + 1 }; // 瓦片 + 溢出格，恒单行
        _buffDock.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _buffDock.GrowHorizontal = Control.GrowDirection.Begin;
        _buffDock.GrowVertical = Control.GrowDirection.Begin;
        _buffDock.AddThemeConstantOverride("h_separation", 6);
        _buffDock.AddThemeConstantOverride("v_separation", 6);
        _buffDock.MouseFilter = Control.MouseFilterEnum.Ignore;
        _buffDockWrap.AddChild(_buffDock);
        _buffTag = new Label
        {
            Position = new Vector2(-160.0f, -26.0f),
            CustomMinimumSize = new Vector2(140.0f, 18.0f),
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _buffTag.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _buffTag.AddThemeFontOverride("font", Font);
        _buffTag.AddThemeFontSizeOverride("font_size", UITheme.FontSmall);
        _buffTag.AddThemeColorOverride("font_color", UITheme.Accent);
        AddChild(_buffTag);
        RefreshBuffTag();
        BuildBuffPanel();
    }

    /// <summary>L 展开的 buff 滚动栏：右缘居中面板（标题 + 分隔线 + 滚动明细行），不暂停对局。</summary>
    private void BuildBuffPanel()
    {
        _buffPanel = new ChamferedPanel
        {
            Padding = 0.0f,
            Position = new Vector2(-356.0f, -320.0f),
            Size = new Vector2(340.0f, 640.0f),
            Visible = false,
        };
        _buffPanel.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        AddChild(_buffPanel);
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        _buffPanel.AddChild(margin);
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        margin.AddChild(vbox);
        _buffPanelTitle = UITheme.MakeLabel((string)Tr("UI_BUFFS_TITLE"), UITheme.FontHud, UITheme.Accent, HorizontalAlignment.Left);
        vbox.AddChild(_buffPanelTitle);
        var divider = new ColorRect
        {
            Color = UITheme.AccentDim,
            CustomMinimumSize = new Vector2(0.0f, 1.0f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        vbox.AddChild(divider);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        vbox.AddChild(scroll);
        _buffRows = new VBoxContainer();
        _buffRows.AddThemeConstantOverride("separation", 6);
        _buffRows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_buffRows);
    }

    /// <summary>滚动栏明细行：字形 + 名称 + 层数（&gt;1 时右侧 ×N）。</summary>
    private Control MakeBuffRow(StringName id, int stacks)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(BuffIcons.MakeGlyph(id, BuffIcons.ColorFor(id), 24.0f));
        var nameLabel = UITheme.MakeLabel(
            (string)Tr(GdFormat.Format("BUFF_%s_NAME", id.ToString().ToUpperInvariant())), UITheme.FontHud, UITheme.Text, HorizontalAlignment.Left);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(nameLabel);
        if (stacks > 1)
        {
            row.AddChild(UITheme.MakeLabel(GdFormat.Format("×%d", stacks), UITheme.FontHud, UITheme.AccentGold, HorizontalAlignment.Right));
        }

        return row;
    }

    /// <summary>收起态溢出格：46×46 同尺寸瓦片（与 buff socket 同一套：淡色底 + 内框），中央 "+N"。</summary>
    private Control MakeOverflowTile(int count)
    {
        var panel = new ChamferedPanel
        {
            Chamfer = 7.0f,
            Padding = 0.0f,
            CustomMinimumSize = new Vector2(46.0f, 46.0f),
            BgColor = UITheme.PanelBg.Lerp(new Color(UITheme.Accent, UITheme.PanelBg.A), 0.16f),
            BorderColor = new Color(UITheme.Accent, 0.7f),
            InnerFrame = true,
            InnerFrameColor = new Color(UITheme.Accent, 0.28f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _buffOverflowLabel = UITheme.MakeLabel(GdFormat.Format("+%d", count), UITheme.FontCaption, UITheme.Accent);
        _buffOverflowLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _buffOverflowLabel.VerticalAlignment = VerticalAlignment.Center;
        _buffOverflowLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.AddChild(_buffOverflowLabel);
        return panel;
    }

    /// <summary>buffs_changed / locale_changed 驱动重建；内容签名不变不重建。</summary>
    private void RebuildBuffDock() => RebuildBuffDock(false);

    private void RebuildBuffDock(bool force)
    {
        _cachedMaxHp = (float)GameState.Instance.MaxHealth(); // D08：buff 变化（extra_life 层数）时刷新缓存，热路径免查 JSON
        var signature = "";
        var active = new Godot.Collections.Array(); // [[id, stacks], ...] 按获得顺序
        var buffs = GameState.Instance.Buffs;
        foreach (var key in buffs.Keys)
        {
            var id = key.AsStringName();
            var stacks = (int)buffs[key].AsInt64();
            if (stacks > 0)
            {
                signature += GdFormat.Format("%s:%d;", id.ToString(), stacks);
                active.Add(new Godot.Collections.Array { id, stacks });
            }
        }

        if (!force && signature == _lastBuffSignature)
        {
            return;
        }

        _lastBuffSignature = signature;
        // 立即释放旧瓦片/行：queue_free 帧末才删除，同帧 add_child 新旧并存会闪一帧（P3）
        foreach (var child in _buffDock.GetChildren())
        {
            if (child is Control tile)
            {
                tile.Free();
            }
        }

        foreach (var child in _buffRows.GetChildren())
        {
            if (child is Control row)
            {
                row.Free();
            }
        }

        if (active.Count == 0)
        {
            _buffTag.Visible = false;
            _buffPanel.Visible = false;
            return;
        }

        _buffTag.Visible = true;
        // 收起态单行：最新 BUFF_DOCK_MAX_TILES 个，更早的折叠为 +N 溢出格
        var shown = new Godot.Collections.Array();
        for (var i = Mathf.Max(active.Count - BuffDockMaxTiles, 0); i < active.Count; i++)
        {
            shown.Add(active[i]);
        }

        foreach (var entryVariant in shown)
        {
            var entry = entryVariant.AsGodotArray();
            _buffDock.AddChild(UITheme.MakeBuffTile(entry[0].AsStringName(), (int)entry[1].AsInt64()));
        }

        var overflow = active.Count - shown.Count;
        if (overflow > 0)
        {
            _buffDock.AddChild(MakeOverflowTile(overflow));
        }

        // 滚动栏：全量明细行
        foreach (var entryVariant in active)
        {
            var entry = entryVariant.AsGodotArray();
            _buffRows.AddChild(MakeBuffRow(entry[0].AsStringName(), (int)entry[1].AsInt64()));
        }
    }

    /// <summary>buff 滚动栏开关（L 键路由至此；无 buff 时不展开）。</summary>
    public void ToggleBuffPanel()
    {
        if (_buffPanel.Visible)
        {
            _buffPanel.Visible = false;
        }
        else if (!string.IsNullOrEmpty(_lastBuffSignature))
        {
            _buffPanel.Visible = true;
        }
    }

    public bool IsBuffPanelOpen()
    {
        return _buffPanel.Visible;
    }

    /// <summary>BackNavigator CLOSE_BUFF_PANEL 路由：Esc 先关栏再进暂停。</summary>
    public void CloseBuffPanel()
    {
        _buffPanel.Visible = false;
    }

    /// <summary>收起态标签：名称 + 当前绑定键提示（改键后同步刷新）。</summary>
    private void RefreshBuffTag()
    {
        _buffTag.Text = GdFormat.Format("%s [%s]", (string)Tr("UI_BUFFS_TAG"), (string)GameState.Instance.ActionKeysText(new StringName("buff_panel")));
    }

    /// <summary>信息横幅（母舰到达等）：切角板结构复用警告横幅，ACCENT 色系、不闪烁。</summary>
    private void BuildInfoBanner()
    {
        _infoPlate = new ChamferedPanel
        {
            Position = new Vector2(-300.0f, 232.0f),
            Size = new Vector2(600.0f, 64.0f),
            Brackets = true,
            BgColor = UITheme.BtnPrimaryBg,
            BorderColor = new Color(UITheme.Accent, 0.6f),
            BracketColor = UITheme.Accent,
            Visible = false,
        };
        _infoPlate.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        AddChild(_infoPlate);
        _infoLabel = new Label
        {
            Position = new Vector2(-300.0f, 232.0f),
            CustomMinimumSize = new Vector2(600.0f, 64.0f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visible = false,
        };
        _infoLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _infoLabel.AddThemeFontOverride("font", Font);
        _infoLabel.AddThemeFontSizeOverride("font_size", UITheme.FontTitle);
        _infoLabel.AddThemeColorOverride("font_color", UITheme.Accent);
        AddChild(_infoLabel);
    }

    /// <summary>信息横幅：显示 ~1.6s 后淡出（位于警告横幅下方，不与其重叠）。</summary>
    public void ShowInfoBanner(string text)
    {
        _infoLabel.Text = text;
        _infoPlate.Visible = true;
        _infoLabel.Visible = true;
        var pm = _infoPlate.Modulate;
        pm.A = 1.0f;
        _infoPlate.Modulate = pm;
        var lm = _infoLabel.Modulate;
        lm.A = 1.0f;
        _infoLabel.Modulate = lm;
        if (_infoTween != null && _infoTween.IsValid())
        {
            _infoTween.Kill();
        }

        _infoTween = CreateTween();
        _infoTween.TweenInterval(1.6);
        _infoTween.SetParallel(true);
        _infoTween.TweenProperty(_infoPlate, "modulate:a", 0.0f, 0.4);
        _infoTween.TweenProperty(_infoLabel, "modulate:a", 0.0f, 0.4);
        _infoTween.Chain().TweenCallback(Callable.From(_infoPlate.Hide));
        _infoTween.TweenCallback(Callable.From(_infoLabel.Hide));
    }

    // ---------------- A7：测试/诊断白盒断言经公开接口（UI 节点引用 getter） ----------------

    public Label BossCountdown() => _bossCountdown;

    public GridContainer BuffDock() => _buffDock;

    public Label BuffTag() => _buffTag;

    public Label? BuffOverflowLabel() => _buffOverflowLabel;

    public VBoxContainer BuffRows() => _buffRows;

    public Label BuffPanelTitle() => _buffPanelTitle;

    public VBoxContainer EventBox() => _eventBox;

    /// <summary>A7 遗留清理：提前离舰蓄力条节点公开查询（测试替代 _ 直读）。</summary>
    public VBoxContainer EarlyLeaveBox() => _earlyLeaveBox;

    public ColorRect EarlyLeaveFill() => _earlyLeaveFill;

    public Label GiveUpLabel() => _giveUpLabel;

    public TextureRect Vignette() => _vignette;

    // ---------------- snake_case 兼容桥（M7 后保留：仍有 C# 动态派发/测试调用方；新代码直接调 PascalCase 主方法） ----------------

    public void show_popup(string text, Vector2 worldPos) => ShowPopup(text, worldPos);

    public void meta_jitter() => MetaJitter(2.0f);

    public void meta_jitter(float px) => MetaJitter(px);

    public TextureRect vignette() => Vignette();
}
