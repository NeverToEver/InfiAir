using System;
using System.Collections.Generic;
using Godot;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// HUD：击杀（左上）、难度（右上）、生命（左下）、Boss 血条（顶部，
/// 带 70%/30% 阶段刻度线与阶段切换短闪，逃跑最后 10s 血条下方倒计时）。
/// Buff 收起态为右下角单行图标坞（最新 4 个 + 溢出 +N），L 键展开右缘滚动明细栏
/// （Esc 经 BackNavigator 优先关栏），与左下状态区、底部居中蓄力提示、左中通讯浮层分角隔离。
/// </summary>
public partial class Hud : CanvasLayer
{
    private readonly FontFile Font = UITheme.Font;

    // ---------------- @onready 节点（_ready 内 GetNode 赋值） ----------------
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
    /// <summary>分段血条（2026-08-03 机制三）：段权 [P1 0.3 / P2 0.4 / ENRAGE 0.3]
    /// （段界 = 阶段阈值 [0.7, 0.3] 的宽占比，与 phase2/enrage_hp_ratio 默认一致、解耦）+ 段色
    /// （P1 琥珀 / P2 橙 / ENRAGE 红，已消耗段暗化、当前段高亮）。
    /// AB22：段数恒由权重数组决定（hud.boss_bar_segments 配置键已删除——加权分支只迭代
    /// SegWeights.Count，原键改 5/7 无任何视觉变化，名实不符）。</summary>
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
    private float _cachedMaxHp = 100.0f; // 缓存 max_health()（extra_life 层数驱动，augments_changed 刷新；D08）
    private Control _augmentDockWrap = null!; // 右下角锚定包装（meta_jitter 抖动对象，避免直接动自动生长的网格）
    private GridContainer _augmentDock = null!;
    private Label _augmentTag = null!;
    private Label? _augmentOverflowLabel; // 收起态溢出计数（">4 个 buff 时 +N"）
    private ChamferedPanel _augmentPanel = null!; // L 键展开的 buff 滚动栏
    private Label _augmentPanelTitle = null!;
    private VBoxContainer _augmentRows = null!;
    private string _lastAugmentSignature = "";
    private ChamferedPanel _infoPlate = null!;
    private Label _infoLabel = null!;
    private Tween? _infoTween;
    private Tween? _warningTween; // H09：警告横幅闪烁 tween 互斥缓存
    // Meta HUD DYING 抖动（D9）：仅 _hp_bar 与 buff 坞两控件的静止位与补间
    private Vector2 _hpBarRest;
    private Vector2 _augmentDockRest;
    private Tween? _jitterTween;
    /// <summary>GameState 信号连接（C22：保存在字段，连接/断开共用同一 Callable，
    /// 不依赖现场重建 Callable 的委托相等语义）。</summary>
    private Callable _onScoreChanged;
    private Callable _onHealthChanged;
    private Callable _onDifficultyChanged;
    private Callable _onDifficultySelected;
    private Callable _onLocaleChanged;
    private Callable _onAugmentsChanged;
    private Callable _onKeyBindingsChanged;
    private Callable _onTalentCacheChanged;

    /// <summary>收起态最多展示的瓦片数（最新 4 个），超出折叠为 +N 溢出格。</summary>
    private const int AugmentDockMaxTiles = 4;

    /// <summary>Boss 逃跑倒计时明暗闪烁半周期（ms）：取模翻转透明度，快于人眼追踪的告警节奏。</summary>
    private const long CountdownBlinkHalfPeriodMs = 500;

    /// <summary>仪表条回写 epsilon：0.1s 轮询下值未变不写 ProgressBar（setter 内部 queue_redraw）。</summary>
    private const float BarWriteEpsilon = 0.001f;

    /// <summary>燃料低量警戒线（比例）：低于此值油条转警示色。</summary>
    private const float FuelWarnRatio = 0.3f;

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
        // AB22：hud.boss_bar_segments 配置键已删除——段数恒由权重数组决定（见 ShowBossBar）
        _hitFlashAlpha = (float)GameState.Instance.Cfg("effects.hit_flash.alpha", _hitFlashAlpha).AsDouble();
        _hitFlashTime = (float)GameState.Instance.Cfg("effects.hit_flash.time", _hitFlashTime).AsDouble();
        _lowHpRatio = (float)GameState.Instance.Cfg("effects.low_hp.ratio", _lowHpRatio).AsDouble();
        _lowHpPulseMin = (float)GameState.Instance.Cfg("effects.low_hp.pulse_min", _lowHpPulseMin).AsDouble();
        _lowHpPulseMax = (float)GameState.Instance.Cfg("effects.low_hp.pulse_max", _lowHpPulseMax).AsDouble();
        _lowHpPulsePeriod = Mathf.Max((float)GameState.Instance.Cfg("effects.low_hp.pulse_period", _lowHpPulsePeriod).AsDouble(), 0.01f); // H15：=0 sin NaN
        foreach (var label in new[] { _killsLabel, _difficultyLabel, _livesLabel })
        {
            label.AddThemeFontOverride("font", Font);
        }

        _killsLabel.AddThemeFontSizeOverride("font_size", UITheme.FontScore);
        _killsLabel.AddThemeColorOverride("font_color", UITheme.Text);
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
        var gs = GameState.Instance;
        _onScoreChanged = Callable.From<int>(OnScoreChanged);
        _onHealthChanged = Callable.From<float>(OnHealthChanged);
        _onDifficultyChanged = Callable.From<float>(OnDifficultyChanged);
        _onDifficultySelected = Callable.From<StringName>(OnDifficultySelected);
        _onLocaleChanged = Callable.From(OnLocaleChanged);
        ConnectGs(GameState.SignalName.ScoreChanged, _onScoreChanged);
        ConnectGs(GameState.SignalName.HealthChanged, _onHealthChanged);
        ConnectGs(GameState.SignalName.DifficultyChanged, _onDifficultyChanged);
        ConnectGs(GameState.SignalName.DifficultySelected, _onDifficultySelected);
        ConnectGs(GameState.SignalName.LocaleChanged, _onLocaleChanged);
        OnScoreChanged(GameState.Instance.Score);
        OnHealthChanged((float)GameState.Instance.Health);
        RefreshDifficultyLabel();
        _fuelTag.Text = (string)Tr("UI_FUEL");
        _dashTag.Text = (string)Tr("UI_DASH");
        _parryTag.Text = (string)Tr("UI_PARRY");
        BuildBackplates();
        BuildBanner();
        BuildMagazineBar();
        BuildChargeBars();
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
        namePlate.AddThemeStyleboxOverride("panel", UITheme.MakeMetalPanelStyle());
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
            EdgeRivets = true,
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
        BuildAugmentDock();
        BuildCacheIndicator();
        BuildInfoBanner();
        _onAugmentsChanged = Callable.From(RebuildAugmentDock);
        _onKeyBindingsChanged = Callable.From(RefreshAugmentTag);
        _onTalentCacheChanged = Callable.From<double, int>(OnTalentCacheChanged);
        ConnectGs(GameState.SignalName.AugmentsChanged, _onAugmentsChanged);
        ConnectGs(GameState.SignalName.KeyBindingsChanged, _onKeyBindingsChanged);
        ConnectGs(GameState.SignalName.TalentCacheChanged, _onTalentCacheChanged);
        RebuildAugmentDock();
        RefreshCacheIndicator();
        _hpBarRest = _hpBar.Position;
        _augmentDockRest = _augmentDockWrap.Position;
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

    // ---------------- 长按蓄力通道（统一 HudChargeBar 组件注册表） ----------------

    /// <summary>全部长按触发功能的蓄力条通道：差异仅 提示文案 + 填充色（规格见 ChargeBarSpecs）。
    /// 新增长按功能 = 加一枚枚举 + 一行规格，UI 由 HudChargeBar 统一承载，不再各写一套。</summary>
    public enum ChargeChannel
    {
        /// <summary>长按 H 召唤母舰。</summary>
        MothershipSummon,
        /// <summary>长按 B 返航。</summary>
        Homecoming,
        /// <summary>长按 K 放弃出击。</summary>
        GiveUp,
        /// <summary>驻留母舰时长按 H 提前离舰。</summary>
        EarlyLeave,
        /// <summary>按住 G 调出天赋面板。</summary>
        TalentPanel,
    }

    /// <summary>通道规格：提示翻译键（%d 占位）/ 通道色 / 底部居中槽位（沿用历史堆叠次序防互叠）。</summary>
    private static readonly Dictionary<ChargeChannel, (string PromptKey, Color Color, float SlotY)> ChargeBarSpecs = new()
    {
        [ChargeChannel.MothershipSummon] = ("MS_CHARGING", UITheme.ChargeCyan, -268.0f),
        [ChargeChannel.Homecoming] = ("HOME_CHARGE", UITheme.ChargeCyan, -120.0f),
        [ChargeChannel.GiveUp] = ("GIVE_UP_CHARGE", UITheme.Danger, -164.0f),
        [ChargeChannel.EarlyLeave] = ("MS_EARLY_LEAVE", UITheme.WarnYellow, -220.0f),
        [ChargeChannel.TalentPanel] = ("TALENT_CHARGE_FMT", UITheme.ChargeCyan, -96.0f),
    };

    private readonly Dictionary<ChargeChannel, HudChargeBar> _chargeBars = new();

    private void BuildChargeBars()
    {
        foreach (var kv in ChargeBarSpecs)
        {
            var bar = HudChargeBar.Create((string)Tr(kv.Value.PromptKey), kv.Value.Color, new Vector2(-140.0f, kv.Value.SlotY));
            _chargeBars[kv.Key] = bar;
            AddChild(bar);
        }
    }

    /// <summary>统一蓄力进度口（全部长按功能唯一入口）：ratio &lt; 0 隐藏该通道。</summary>
    public void SetCharge(ChargeChannel channel, float ratio) => _chargeBars[channel].SetRatio(ratio);

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

    /// <summary>L（augment_panel）切换 buff 滚动栏；暂停态下 HUD 不处理输入（process 继承）。</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("augment_panel"))
        {
            ToggleAugmentPanel();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        // Boss 四 [Signal] 配对断开——Godot 信号不随接收方释放自动断开，
        // Hud 先于 Boss 释放时存活期信号回调已释放 Hud
        if (_boss != null && GodotObject.IsInstanceValid(_boss))
        {
            _boss.HealthChanged -= OnBossHealthChanged;
            _boss.Died -= OnBossDied;
            _boss.Enraged -= OnBossEnraged;
            _boss.PhaseChanged -= OnBossPhaseChanged;
        }

        // C22 模式（M5）：GameState 信号显式断开——GameState 为 autoload 恒存于 root，
        // 本节点释放后存活期信号回调仍指向已释放的 Hud 可致退出崩溃；断开与连接共用
        // 同一字段 Callable，八个信号全部配对（含 TalentCacheChanged）
        DisconnectGs(GameState.SignalName.ScoreChanged, _onScoreChanged);
        DisconnectGs(GameState.SignalName.HealthChanged, _onHealthChanged);
        DisconnectGs(GameState.SignalName.DifficultyChanged, _onDifficultyChanged);
        DisconnectGs(GameState.SignalName.DifficultySelected, _onDifficultySelected);
        DisconnectGs(GameState.SignalName.LocaleChanged, _onLocaleChanged);
        DisconnectGs(GameState.SignalName.AugmentsChanged, _onAugmentsChanged);
        DisconnectGs(GameState.SignalName.KeyBindingsChanged, _onKeyBindingsChanged);
        DisconnectGs(GameState.SignalName.TalentCacheChanged, _onTalentCacheChanged);
    }

    /// <summary>GameState 信号连接（IsConnected 守卫防场景重载时序下重复连接）。</summary>
    private void ConnectGs(StringName signal, Callable callable)
    {
        var gs = GameState.Instance;
        if (!gs.IsConnected(signal, callable))
        {
            gs.Connect(signal, callable);
        }
    }

    /// <summary>GameState 信号断开（IsConnected 守卫防未连先断报错）。</summary>
    private void DisconnectGs(StringName signal, Callable callable)
    {
        var gs = GameState.Instance;
        if (gs.IsConnected(signal, callable))
        {
            gs.Disconnect(signal, callable);
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
                cm.A = Time.GetTicksMsec() / CountdownBlinkHalfPeriodMs % 2 == 0 ? 1.0f : 0.45f;
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
        if (Mathf.Abs(fuelVal - _fuelBar.Value) > BarWriteEpsilon)
        {
            _fuelBar.Value = fuelVal;
        }

        _fuelBar.FillColor = fuel < FuelWarnRatio ? UITheme.Danger : UITheme.Accent;
        var dashVal = player.DashReadyRatio() * 100.0f;
        if (Mathf.Abs(dashVal - _dashBar.Value) > BarWriteEpsilon)
        {
            _dashBar.Value = dashVal;
        }

        // 机制四：弹反能量槽（满格=可用；流程期清空；冷却匀速充能——player.parry_energy_ratio）
        var parryVal = player.ParryEnergyRatio() * 100.0f;
        if (Mathf.Abs(parryVal - _parryBar.Value) > BarWriteEpsilon)
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

    /// <summary>左上击杀块、左下状态块与右上难度块的切角背板 + 小标签（标签置于背板上方外侧，不与边框/数值重叠）。</summary>
    private void BuildBackplates()
    {
        var killsPlate = new ChamferedPanel
        {
            Position = new Vector2(10.0f, 24.0f),
            Size = new Vector2(230.0f, 64.0f),
            EdgeRivets = true,
        };
        AddChild(killsPlate);
        MoveChild(killsPlate, 0);
        _killsLabel.Position = new Vector2(24.0f, 30.0f);
        var killsTag = MakeCornerTag((string)Tr("UI_KILLS_TAG"));
        killsTag.Position = new Vector2(24.0f, 6.0f);
        AddChild(killsTag);
        var statusPlate = new ChamferedPanel
        {
            Position = new Vector2(10.0f, -134.0f),
            Size = new Vector2(560.0f, 120.0f),
            EdgeRivets = true,
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
            EdgeRivets = true,
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
        _tagLabels = new[] { killsTag, livesTag, diffTag };
        _tagKeys = new[] { new StringName("UI_KILLS_TAG"), new StringName("UI_LIVES_TAG"), new StringName("UI_DIFF_TAG") };
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
        GameState.Instance.PlaySfx(SfxId.PlayerHit);
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
        // 机制三：分段血条——段权/段色按权重数组登记（AB22：段数 = 权重数，恒为 3）
        _bossBar.Segments = BossSegWeights.Count;
        _bossBar.SegWeights = BossSegWeights;
        _bossBar.SegColors = BossSegColors;
        _bossBar.Visible = true;
        _bossPlate.Visible = true;
        _bossBar.Value = 100.0f;
        // 2026-08-10 健壮性审查：换绑前断开上一只 Boss 的四信号（U05 同款配对）——
        // 旧 Boss 存活（异常时序下与下一只交错）时双连会以旧 Boss 血量事件驱动新血条
        if (_boss != null && GodotObject.IsInstanceValid(_boss))
        {
            _boss.HealthChanged -= OnBossHealthChanged;
            _boss.Died -= OnBossDied;
            _boss.Enraged -= OnBossEnraged;
            _boss.PhaseChanged -= OnBossPhaseChanged;
        }

        _boss = boss;
        _bossCountdown.Visible = false;
        _bossPhase = FightPhaseP1; // M3d：Boss.FightPhase.P1（C# 枚举经常量，见顶部注释）
        boss.HealthChanged += OnBossHealthChanged; // M3d：C# [Signal] 以 PascalCase 注册
        boss.Died += OnBossDied;
        boss.Enraged += OnBossEnraged;
        boss.PhaseChanged += OnBossPhaseChanged;
        RefreshBossName();
    }

    private int _lastKills = -1;

    /// <summary>击杀计数标签（分数/连击显示已移除，内部计分引擎保留驱动里程碑/解锁）。
    /// ScoreChanged 伴随击杀/擦弹等高频来源，按计数变化节流格式化。</summary>
    private void OnScoreChanged(int _newScore)
    {
        RefreshKills();
    }

    private void RefreshKills()
    {
        var kills = GameState.Instance.Kills;
        if (kills == _lastKills)
        {
            return;
        }

        _lastKills = kills;
        _killsLabel.Text = GdFormat.Format((string)Tr("UI_KILLS"), kills);
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
        _lastKills = -1; // 强制重写文本（语言切换）
        RefreshKills();
        OnHealthChanged((float)GameState.Instance.Health);
        RefreshDifficultyLabel();
        _fuelTag.Text = (string)Tr("UI_FUEL");
        _dashTag.Text = (string)Tr("UI_DASH");
        _parryTag.Text = (string)Tr("UI_PARRY");
        RefreshTagLabels();
        if (_augmentTag != null)
        {
            RefreshAugmentTag();
        }

        if (_augmentPanelTitle != null)
        {
            _augmentPanelTitle.Text = (string)Tr("UI_AUGMENTS_TITLE");
        }

        if (_eventBox != null && _eventBox.Visible)
        {
            _eventTitle.Text = (string)Tr("ETV_TITLE");
            _eventTurretsLabel.Text = GdFormat.Format((string)Tr("ETV_TURRETS"), Mathf.Max(_lastEventAlive, 0));
        }

        RebuildAugmentDock(true);
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
        _augmentDockWrap.Position = _augmentDockRest + off * 0.5f;
        _jitterTween = CreateTween();
        _jitterTween.TweenProperty(_hpBar, "position", _hpBarRest, 0.08);
        _jitterTween.Parallel().TweenProperty(_augmentDockWrap, "position", _augmentDockRest, 0.08);
    }

    /// <summary>
    /// 右下 buff 区：收起态单行瓦片（最新 4 个 + 溢出 +N，标签带 [L] 快捷键提示），
    /// L 键展开右侧滚动栏（全部 buff 明细，不暂停对局）；与左下状态区/底部蓄力提示分角隔离。
    /// </summary>
    private void BuildAugmentDock()
    {
        _augmentDockWrap = new Control
        {
            Position = new Vector2(-20.0f, -44.0f), // 底部留 28px 给标签行（右缘与标签同 20px 边距）
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _augmentDockWrap.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        AddChild(_augmentDockWrap);
        _augmentDock = new GridContainer { Columns = AugmentDockMaxTiles + 1 }; // 瓦片 + 溢出格，恒单行
        _augmentDock.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _augmentDock.GrowHorizontal = Control.GrowDirection.Begin;
        _augmentDock.GrowVertical = Control.GrowDirection.Begin;
        _augmentDock.AddThemeConstantOverride("h_separation", 6);
        _augmentDock.AddThemeConstantOverride("v_separation", 6);
        _augmentDock.MouseFilter = Control.MouseFilterEnum.Ignore;
        _augmentDockWrap.AddChild(_augmentDock);
        _augmentTag = new Label
        {
            Position = new Vector2(-160.0f, -26.0f),
            CustomMinimumSize = new Vector2(140.0f, 18.0f),
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _augmentTag.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _augmentTag.AddThemeFontOverride("font", Font);
        _augmentTag.AddThemeFontSizeOverride("font_size", UITheme.FontSmall);
        _augmentTag.AddThemeColorOverride("font_color", UITheme.Accent);
        AddChild(_augmentTag);
        RefreshAugmentTag();
        BuildAugmentPanel();
    }

    // ---------------- 天赋缓存指示器（右上角，第四章 4.2） ----------------

    private ChamferedPanel _cacheChip = null!;
    private Label _cacheCount = null!;
    private Label _cacheTooltip = null!;
    private Tween? _cachePulseTween;
    private int _lastCacheRaw = -1;

    /// <summary>缓存指示器芯片（难度块下方）：点数 + 状态光晕；点击/G 键开天赋面板。
    /// 状态：空闲(0) 灰 / 可用(1..20) 青+呼吸 / 溢出警告(21..29) 橙 / 严重溢出(30+) 红。
    /// 呼吸脉冲受 ReduceFlash 无障碍约束（开启后静止）。</summary>
    private void BuildCacheIndicator()
    {
        _cacheChip = new ChamferedPanel
        {
            CustomMinimumSize = new Vector2(172.0f, 56.0f),
            Brackets = true,
            Padding = 0.0f,
        };
        _cacheChip.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _cacheChip.Position = new Vector2(-192.0f, 118.0f);
        AddChild(_cacheChip);

        var box = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        box.OffsetLeft = 14.0f;
        box.OffsetRight = -14.0f;
        box.AddThemeConstantOverride("separation", 8);
        box.Alignment = BoxContainer.AlignmentMode.Center;
        _cacheChip.AddChild(box);

        var glyph = UITheme.MakeLabel("◆", UITheme.FontHud, UITheme.Accent);
        glyph.MouseFilter = Control.MouseFilterEnum.Ignore;
        box.AddChild(glyph);
        _cacheCount = UITheme.MakeLabel("0", UITheme.FontHudL, UITheme.TextDim, HorizontalAlignment.Right);
        _cacheCount.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _cacheCount.MouseFilter = Control.MouseFilterEnum.Ignore;
        box.AddChild(_cacheCount);
        var keyHint = UITheme.MakeLabel("G", UITheme.FontSmall, UITheme.TextDim);
        keyHint.MouseFilter = Control.MouseFilterEnum.Ignore;
        box.AddChild(keyHint);

        _cacheTooltip = UITheme.MakeLabel("", UITheme.FontSmall, UITheme.Text, HorizontalAlignment.Right);
        _cacheTooltip.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _cacheTooltip.Position = new Vector2(-360.0f, 178.0f);
        _cacheTooltip.CustomMinimumSize = new Vector2(340.0f, 0.0f);
        _cacheTooltip.Visible = false;
        _cacheTooltip.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_cacheTooltip);

        _cacheChip.GuiInput += OnCacheChipInput;
        _cacheChip.MouseEntered += () =>
        {
            var talent = GameState.Instance.Talent;
            _cacheTooltip.Text = GdFormat.Format((string)Tr("TALENT_CACHE_TIP"), talent.RawCache, talent.EffectiveCacheText);
            _cacheTooltip.Visible = true;
        };
        _cacheChip.MouseExited += () =>
        {
            _cacheTooltip.Visible = false;
            _main.TalentPanel().NotifyTriggerReleased(); // 按住蓄力中移出芯片 = 松开，取消蓄力
        };
    }

    /// <summary>指示器蓄力中提亮；结束（取消/进入）复原并交还呼吸脉冲。</summary>
    public void SetCacheChipCharging(bool charging)
    {
        if (_cachePulseTween != null)
        {
            _cachePulseTween.Kill();
            _cachePulseTween = null;
        }

        _cacheChip.Modulate = charging ? new Color(1.3f, 1.3f, 1.1f) : Colors.White;
        if (!charging)
        {
            _lastCacheRaw = -1; // 强制重启呼吸脉冲（否则同值早退不恢复）
            RefreshCacheIndicator();
        }
    }

    private void OnCacheChipInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            // 按住蓄力、松开取消（与按住 G 同一语义——满格自动进入，中途松手作废）
            if (mb.Pressed)
            {
                _main.TalentPanel().BeginCharge();
            }
            else
            {
                _main.TalentPanel().NotifyTriggerReleased();
            }

            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>指示器四状态刷新（TalentCacheChanged 信号驱动 + _Ready 初刷；签名对齐信号双参）。</summary>
    private void OnTalentCacheChanged(double _effective, int _raw) => RefreshCacheIndicator();

    private void RefreshCacheIndicator()
    {
        var gs = GameState.Instance;
        var raw = gs.TalentRawCache;
        var (color, border, breathing) = raw switch
        {
            0 => (UITheme.TextDim, new Color(UITheme.PanelBorder, 0.4f), false),
            <= 20 => (UITheme.Accent, new Color(UITheme.Accent, 0.8f), true),
            <= 29 => (UITheme.WarnYellow, new Color(UITheme.WarnYellow, 0.9f), false),
            _ => (UITheme.Danger, new Color(UITheme.Danger, 1.0f), false),
        };
        _cacheCount.Text = raw.ToString();
        _cacheCount.AddThemeColorOverride("font_color", color);
        _cacheChip.BorderColor = border;
        if (raw != _lastCacheRaw || !breathing)
        {
            _lastCacheRaw = raw;
            RestartCachePulse(breathing);
        }
    }

    /// <summary>呼吸脉冲（2s 周期明暗循环）；非呼吸态/ReduceFlash/蓄力中（芯片被提亮占用）不启动。</summary>
    private void RestartCachePulse(bool breathing)
    {
        if (_cachePulseTween != null)
        {
            _cachePulseTween.Kill();
            _cachePulseTween = null;
        }

        _cacheChip.Modulate = Colors.White;
        if (!breathing || GameState.Instance.ReduceFlash || _main.TalentPanel().IsCharging)
        {
            return;
        }

        _cachePulseTween = CreateTween().SetLoops();
        _cachePulseTween.TweenProperty(_cacheChip, "modulate", new Color(1f, 1f, 1f, 0.72f), 1.0)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _cachePulseTween.TweenProperty(_cacheChip, "modulate", Colors.White, 1.0)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    /// <summary>L 展开的 buff 滚动栏：右缘居中面板（标题 + 分隔线 + 滚动明细行），不暂停对局。</summary>
    private void BuildAugmentPanel()
    {
        _augmentPanel = new ChamferedPanel
        {
            Padding = 0.0f,
            Position = new Vector2(-356.0f, -320.0f),
            Size = new Vector2(340.0f, 640.0f),
            Visible = false,
        };
        _augmentPanel.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        AddChild(_augmentPanel);
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        _augmentPanel.AddChild(margin);
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        margin.AddChild(vbox);
        _augmentPanelTitle = UITheme.MakeLabel((string)Tr("UI_AUGMENTS_TITLE"), UITheme.FontHud, UITheme.Accent, HorizontalAlignment.Left);
        vbox.AddChild(_augmentPanelTitle);
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
        _augmentRows = new VBoxContainer();
        _augmentRows.AddThemeConstantOverride("separation", 6);
        _augmentRows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_augmentRows);
    }

    /// <summary>滚动栏明细行：字形 + 名称 + 层数（&gt;1 时右侧 ×N）。</summary>
    private HBoxContainer MakeAugmentRow(StringName id, int stacks)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(AugmentIcons.MakeGlyph(id, AugmentIcons.ColorFor(id), 24.0f));
        var nameLabel = UITheme.MakeLabel(
            (string)Tr(GdFormat.Format("AUG_%s_NAME", id.ToString().ToUpperInvariant())), UITheme.FontHud, UITheme.Text, HorizontalAlignment.Left);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(nameLabel);
        if (stacks > 1)
        {
            row.AddChild(UITheme.MakeLabel(GdFormat.Format("×%d", stacks), UITheme.FontHud, UITheme.AccentGold, HorizontalAlignment.Right));
        }

        return row;
    }

    /// <summary>收起态溢出格：46×46 同尺寸瓦片（与 buff socket 同一套：淡色底 + 内框），中央 "+N"。</summary>
    private ChamferedPanel MakeOverflowTile(int count)
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
        _augmentOverflowLabel = UITheme.MakeLabel(GdFormat.Format("+%d", count), UITheme.FontCaption, UITheme.Accent);
        _augmentOverflowLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _augmentOverflowLabel.VerticalAlignment = VerticalAlignment.Center;
        _augmentOverflowLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.AddChild(_augmentOverflowLabel);
        return panel;
    }

    /// <summary>augments_changed / locale_changed 驱动重建；内容签名不变不重建。</summary>
    private void RebuildAugmentDock() => RebuildAugmentDock(false);

    private void RebuildAugmentDock(bool force)
    {
        _cachedMaxHp = (float)GameState.Instance.MaxHealth(); // D08：buff 变化（extra_life 层数）时刷新缓存，热路径免查 JSON
        var signature = "";
        var active = new Godot.Collections.Array(); // [[id, stacks], ...] 按获得顺序
        var augments = GameState.Instance.Augments;
        foreach (var key in augments.Keys)
        {
            var id = key.AsStringName();
            var stacks = (int)augments[key].AsInt64();
            if (stacks > 0)
            {
                signature += GdFormat.Format("%s:%d;", id.ToString(), stacks);
                active.Add(new Godot.Collections.Array { id, stacks });
            }
        }

        if (!force && signature == _lastAugmentSignature)
        {
            return;
        }

        _lastAugmentSignature = signature;
        // 立即释放旧瓦片/行：queue_free 帧末才删除，同帧 add_child 新旧并存会闪一帧（P3）
        foreach (var child in _augmentDock.GetChildren())
        {
            if (child is Control tile)
            {
                tile.Free();
            }
        }

        foreach (var child in _augmentRows.GetChildren())
        {
            if (child is Control row)
            {
                row.Free();
            }
        }

        if (active.Count == 0)
        {
            _augmentTag.Visible = false;
            _augmentPanel.Visible = false;
            return;
        }

        _augmentTag.Visible = true;
        // 收起态单行：最新 AUG_DOCK_MAX_TILES 个，更早的折叠为 +N 溢出格
        var shown = new Godot.Collections.Array();
        for (var i = Mathf.Max(active.Count - AugmentDockMaxTiles, 0); i < active.Count; i++)
        {
            shown.Add(active[i]);
        }

        foreach (var entryVariant in shown)
        {
            var entry = entryVariant.AsGodotArray();
            _augmentDock.AddChild(UITheme.MakeAugmentTile(entry[0].AsStringName(), (int)entry[1].AsInt64()));
        }

        var overflow = active.Count - shown.Count;
        if (overflow > 0)
        {
            _augmentDock.AddChild(MakeOverflowTile(overflow));
        }

        // 滚动栏：全量明细行
        foreach (var entryVariant in active)
        {
            var entry = entryVariant.AsGodotArray();
            _augmentRows.AddChild(MakeAugmentRow(entry[0].AsStringName(), (int)entry[1].AsInt64()));
        }

        // 重建末尾重刷 HP 显示——天赋域（起始预置/加点/重置）经 AugmentsChanged 驱动本方法，
        // _cachedMaxHp 已刷新但 _hpBar/_livesLabel 仍用旧 max 显示失真（extra_life 开局）；
        // OnHealthChanged 幂等，整数档位守卫下值未变不重格式化
        OnHealthChanged((float)GameState.Instance.Health);
    }

    /// <summary>buff 滚动栏开关（L 键路由至此；无 buff 时不展开）。</summary>
    public void ToggleAugmentPanel()
    {
        if (_augmentPanel.Visible)
        {
            _augmentPanel.Visible = false;
        }
        else if (!string.IsNullOrEmpty(_lastAugmentSignature))
        {
            _augmentPanel.Visible = true;
        }
    }

    public bool IsAugmentPanelOpen()
    {
        return _augmentPanel.Visible;
    }

    /// <summary>BackNavigator CLOSE_AUG_PANEL 路由：Esc 先关栏再进暂停。</summary>
    public void CloseAugmentPanel()
    {
        _augmentPanel.Visible = false;
    }

    /// <summary>收起态标签：名称 + 当前绑定键提示（改键后同步刷新）。</summary>
    private void RefreshAugmentTag()
    {
        _augmentTag.Text = GdFormat.Format("%s [%s]", (string)Tr("UI_AUGMENTS_TAG"), (string)GameState.Instance.ActionKeysText(new StringName("augment_panel")));
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

    // ---------------- snake_case 兼容桥（meta_jitter 由 MetaHealthFX 经 CallGroup("hud", "meta_jitter", ...) 动态派发——
    // CallGroup 走方法名字符串，保留原名避免调用点失效） ----------------

    public void meta_jitter(float px) => MetaJitter(px);
}
