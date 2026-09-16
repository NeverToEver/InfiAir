using System;
using System.Collections.Generic;
using Godot;
using InfiAir.Core;
using InfiAir.Core.Combat;
using InfiAir.Core.Text;

namespace InfiAir;

/// <summary>
/// HUD：击杀（左上）、难度（右上）、生命（左下）、Boss 血条（顶部，
/// 带 70%/30% 阶段刻度线与阶段切换短闪，逃跑最后 10s 血条下方倒计时）。
/// 增幅 收起态为右下角单行图标坞（最新 4 个 + 溢出 +N），L 键展开右缘滚动明细栏
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

    // 集成仪表盘（左下）：形态按各元素真实特性选——生命＝主读数分段条、燃料＝消耗预算量槽、
    // 冲刺/弹反＝循环充能槽（同一构件两个实例）、弹仓＝离散格、母舰＝枚举状态指示灯。
    // 不用指针表盘：本盘没有任何「需要读速率的连续量」，指针会读成模拟量测而诱导读数而非判断可用性。
    private FuelTank _fuelTank = null!;
    private AbilitySocket _dashSocket = null!;
    private AbilitySocket _parrySocket = null!;
    private CartridgeStrip _magStrip = null!;
    private AnnunciatorLamp _dockLamp = null!;
    private Label _dockTag = null!;

    private ChamferedPanel _bannerPlate = null!;
    private Label _bannerLabel = null!;

    private Main _main = null!; // typed
    private float _pollTimer;
    /// <summary>累计模拟时间（秒，_Process delta）：倒计时闪烁等表现相位的基准，替代墙钟
    /// （帧率/机器性能无关）。</summary>
    private float _simTime;
    private string _lastDockText = "";
    private AnnunciatorLamp.Lamp _lastDockLamp = AnnunciatorLamp.Lamp.Off; // 灯态缓存（每轮只写变化）
    private int _lastMagCells = -1;
    private int _lastFuelWarn = -1; // 燃料警戒态缓存（0/1），未翻转跳过液色写入
    private Label[] _tagLabels = System.Array.Empty<Label>();
    private StringName[] _tagKeys = System.Array.Empty<StringName>();
    private VBoxContainer _eventBox = null!;
    private SegmentedBar _eventBar = null!;
    private Label _eventTitle = null!;
    private Label _eventCounterLabel = null!;
    /// <summary>事件条当前文案（标题键 + 计数行键与三个参数）：本地化切换与「数值未变不重写」
    /// 共用一份缓存——调用方只给键与数字，排版与翻译都归 HUD。</summary>
    private string _eventTitleKey = "";
    private string _eventCounterKey = "";
    private readonly int[] _eventCounter = { int.MinValue, int.MinValue, int.MinValue };
    /// <summary>当前血条绑定的 Boss（逃跑倒计时轮询用；died 时清空）。Boss 为 C# typed。</summary>
    private Boss? _boss;
    private Label _bossCountdown = null!;
    private Label _bossName = null!; // Boss 名牌（型号 + 阶段），血条子节点随其显隐
    private ChamferedPanel _bossPlate = null!; // Boss 血条 + 名牌的切角背板（随血条显隐）
    private BossBarTicks _bossTicks = null!; // 阶段刻度线覆盖层（比例由阶段阈值派生注入）
    /// <summary>Boss.cs 的 C# 枚举 FightPhase { P1, P2, ENRAGE }（P1=0/P2=1 与
    /// GetFightPhaseTransition/Active 一致；ENRAGE=2 由声明顺序确定）——值镜像。</summary>
    private const int FightPhaseP1 = 0;
    private const int FightPhaseP2 = 1;
    private const int FightPhaseEnrage = 2;
    private int _bossPhase = FightPhaseP1;
    /// <summary>仪表类刷新降频（信号驱动的文本不受影响）。≤0 节流失效。</summary>
    private float _pollInterval = 0.1f;
    /// <summary>分段血条：段权与段界**由阶段阈值派生**（boss.phase2_hp_ratio / boss.enrage.hp_ratio，
    /// 在 ShowBossBar 时从活着的 Boss 实例读，见 BossBarSegments），段色按段序给
    /// （P1 琥珀 / P2 橙 / ENRAGE 红，已消耗段暗化、当前段高亮）。
    /// 段数恒由权重数组决定，HUD 不再另存一份阈值或段权常量——否则改 balance 后 Boss 按新阈值
    /// 转阶段而血条段界/刻度仍在旧值，玩家读到的阶段边界是假的。</summary>
    // 静态 Godot 集合在引擎退出后被 .NET finalize 触碰 native → segfault（实测），改实例字段
    private readonly Godot.Collections.Array BossSegWeights = new() { 0.0f, 0.0f, 0.0f };
    private readonly Godot.Collections.Array BossSegColors = new()
    {
        UITheme.HudBossHp,
        UITheme.HudBossHpP2,
        UITheme.Danger,
    };
    // 受击/低血屏幕反馈（effects.hit_flash / effects.low_hp，_ready 缓存）
    private float _hitFlashAlpha = 0.55f;
    private float _hitFlashTime = 0.25f;
    private float _lowHpRatio = 0.2f;
    // 血条变红阈值——与低血脉动（ratio）分档：条先于脉动变红，各读各的键
    private float _lowHpBarRatio = 0.3f;
    private float _lowHpPulseMin = 0.15f;
    private float _lowHpPulseMax = 0.3f;
    private float _lowHpPulsePeriod = 1.2f;
    private TextureRect _vignette = null!;
    /// <summary>受击红闪 alpha（tween 衰减）；公开属性供 TweenProperty 字符串路径驱动（原 _hit_flash 脚本属性）。</summary>
    public float HitFlash { get; set; }

    /// <summary>信息横幅当前不透明度（探针读口）：横幅触发后前 1.6s 应恒为 1。
    /// 停留被并行语义吞掉时此处提前归零——无头下既不崩也不报错，只能靠读口断言。</summary>
    public float InfoBannerAlpha => _infoLabel.Modulate.A;

    /// <summary>Boss 血条当前 modulate（探针读口）：ReduceFlash 下阶段切换后应为 Colors.White
    /// （无提亮脉冲），否则为 2.2 倍亮度峰值。</summary>
    public Color BossBarModulate => _bossBar.Modulate;
    private Tween? _hitTween;
    private float _lastHpValue = -1.0f;
    private float _pulseTime;
    private float _cachedMaxHp = 100.0f; // 缓存 max_health()（extra_life 层数驱动，augments_changed 刷新）
    private Control _augmentDockWrap = null!; // 右下角锚定包装（meta_jitter 抖动对象，避免直接动自动生长的网格）
    private GridContainer _augmentDock = null!;
    private Label _augmentTag = null!;
    private Label? _augmentOverflowLabel; // 收起态溢出计数（">4 个 buff 时 +N"）
    private ChamferedPanel _augmentPanel = null!; // L 键展开的增幅滚动栏
    private Label _augmentPanelTitle = null!;
    private VBoxContainer _augmentRows = null!;
    private string _lastAugmentSignature = "";
    private ChamferedPanel _infoPlate = null!;
    private Label _infoLabel = null!;
    private Tween? _infoTween;
    private Tween? _warningTween; // 警告横幅闪烁 tween 互斥缓存
    // Meta HUD DYING 抖动：仅 _hp_bar 与增幅 坞两控件的静止位与补间
    private Vector2 _hpBarRest;
    private Vector2 _augmentDockRest;
    private Tween? _jitterTween;
    // ---------------- 视觉反馈（纯显示层：不写任何本局状态，判定仍归 core） ----------------
    /// <summary>受击方向指示（屏幕边缘弧）：驻留顶层，无弧时自停 _process。</summary>
    private HudDamageArcs _damageArcs = null!;
    /// <summary>仪表提示相位（仅低燃料警戒期推进；无常驻逐帧开销）。</summary>
    private float _cuePhase;
    /// <summary>低燃料警戒脉冲是否活跃（poll 翻转，_process 按帧驱动）。</summary>
    private bool _fuelWarnActive;
    /// <summary>dash / 弹反 满格态缓存（-1 = 未初始化，抑制初帧上升沿）。</summary>
    private int _lastDashFull = -1;
    private int _lastParryFull = -1;
    private Tween? _eventTween;
    private Tween? _bossTween;
    private Tween? _bossPlateTween;
    /// <summary>已展示过的增幅层数（重建时比对，只给真正新增/叠层的瓦片播入场动效）。</summary>
    private readonly Dictionary<string, int> _knownAugmentStacks = new();
    /// <summary>单次重建的增量键集合（复用实例，重建非逐帧，无热路径分配压力）。</summary>
    private readonly HashSet<string> _gainKeys = new();
    /// <summary>GameState 信号连接（保存在字段，连接/断开共用同一 Callable，
    /// 不依赖现场重建 Callable 的委托相等语义）。</summary>
    private Callable _onScoreChanged;
    private Callable _onHealthChanged;
    private Callable _onDifficultyChanged;
    private Callable _onDifficultySelected;
    private Callable _onLocaleChanged;
    private Callable _onAugmentsChanged;
    private Callable _onKeyBindingsChanged;
    private Callable _onTalentCacheChanged;
    private Callable _onMilestoneReached;
    private Callable _onReduceFlashChanged;

    /// <summary>收起态最多展示的瓦片数（最新 4 个），超出折叠为 +N 溢出格。</summary>
    private const int AugmentDockMaxTiles = 4;

    /// <summary>Boss 逃跑倒计时明暗闪烁半周期（ms）：取模翻转透明度，快于人眼追踪的告警节奏。</summary>
    private const long CountdownBlinkHalfPeriodMs = 500;

    /// <summary>燃料低量警戒线（比例）的唯一来源在 <see cref="FuelGauge.WarnRatio"/>：液色警戒
    /// 与量槽刻度警示区共用一份判据，HUD 不再另存常量（两处各写一份会出半红量槽）。</summary>

    /// <summary>冲刺/弹反「已满」判定线（比例）：径向仪表满格即就绪，留 0.5% 余量吸收充能取整误差。</summary>
    private const float DashFullRatio = 0.995f;

    private const float ParryFullRatio = 0.995f;

    // ---------------- 仪表/事件条视觉提示常量（新调参不落 balance：纯观感，无玩法判据） ----------------
    /// <summary>低燃料警戒脉冲频率（Hz）与最暗 alpha（亮度泵动，ReduceFlash 下静止）。</summary>
    private const float FuelPulseHz = 2.4f;
    private const float FuelPulseMinAlpha = 0.62f;
    /// <summary>事件条开合时长（秒）。</summary>
    private const float EventBarFadeTime = 0.18f;
    /// <summary>Boss 血条开合时长（秒）。</summary>
    private const float BossBarFadeTime = 0.22f;
    /// <summary>新增幅瓦片入场提亮峰值（仅视觉，不改瓦片数据）。</summary>
    private const float AugmentGainBoost = 1.5f;

    /// <summary>信息横幅停留与淡出时长（秒）：「奖励节奏可被感知」靠的正是这段停留。</summary>
    private const float InfoBannerHoldSeconds = 1.6f;
    private const float InfoBannerFadeSeconds = 0.4f;

    /// <summary>
    /// Boss 血条阶段刻度线（§4.2）：随血条显隐的覆盖层，比例由 <see cref="BossBarSegments.Ticks"/>
    /// 从阶段阈值派生后注入（段界即阈值；本控件不持有阈值，也不另存刻度常量）。
    /// </summary>
    public partial class BossBarTicks : Control
    {
        /// <summary>刻度比例（自血条左端量起）；实例级复用，重绑 Boss 时才改写。</summary>
        private float[] _ratios = System.Array.Empty<float>();

        /// <summary>登记刻度比例（调用方给自 <see cref="BossBarSegments.Ticks"/> 的数组，本控件不复制）。</summary>
        public void SetRatios(float[] ratios)
        {
            _ratios = ratios;
            QueueRedraw();
        }

        public override void _Draw()
        {
            foreach (var r in _ratios)
            {
                var x = Size.X * r;
                DrawLine(new Vector2(x, -2.0f), new Vector2(x, Size.Y + 2.0f), UITheme.TickWhite, 2.0f);
            }
        }
    }

    public override void _Ready()
    {
        AddToGroup("hud");
        _main = GetParent<Main>(); // HUD 是 main 子节点，_ready 直接缓存，不靠 0.1s 轮询现找
        _killsLabel = GetNode<Label>("KillsLabel");
        _difficultyLabel = GetNode<Label>("DifficultyLabel");
        _livesLabel = GetNode<Label>("LivesLabel");
        _hpBar = GetNode<SegmentedBar>("HpBar");
        _bossBar = GetNode<SegmentedBar>("BossBar");
        _dockTag = GetNode<Label>("DockTag");
        BuildInstrumentCluster();
        _pollInterval = Mathf.Max((float)GameState.Instance.Cfg("effects.hud_poll_interval", _pollInterval).AsDouble(), 0.01f); // ≤0 节流失效
        _hitFlashAlpha = (float)GameState.Instance.Cfg("effects.hit_flash.alpha", _hitFlashAlpha).AsDouble();
        _hitFlashTime = (float)GameState.Instance.Cfg("effects.hit_flash.time", _hitFlashTime).AsDouble();
        _lowHpRatio = (float)GameState.Instance.Cfg("effects.low_hp.ratio", _lowHpRatio).AsDouble();
        _lowHpBarRatio = Mathf.Clamp((float)GameState.Instance.Cfg("effects.low_hp.bar_ratio", _lowHpBarRatio).AsDouble(), 0.0f, 1.0f);
        _lowHpPulseMin = (float)GameState.Instance.Cfg("effects.low_hp.pulse_min", _lowHpPulseMin).AsDouble();
        _lowHpPulseMax = (float)GameState.Instance.Cfg("effects.low_hp.pulse_max", _lowHpPulseMax).AsDouble();
        _lowHpPulsePeriod = Mathf.Max((float)GameState.Instance.Cfg("effects.low_hp.pulse_period", _lowHpPulsePeriod).AsDouble(), 0.01f); // =0 sin NaN
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
        _dockTag.AddThemeFontOverride("font", Font);
        _dockTag.AddThemeFontSizeOverride("font_size", UITheme.FontSmall);
        _dockTag.AddThemeColorOverride("font_color", UITheme.Accent);
        _hpBar.FillColor = UITheme.Accent;
        _hpBar.EnableGhost = true; // 血条带受伤残影（其余分段条保持无残影）
        // 仪表盘配色与语义：燃料琥珀（低量转危红）、冲刺琥珀、弹反金；两枚充能槽满格即就绪
        _dashSocket.Configure(AbilitySocket.Glyph.Dash, UITheme.Accent);
        _parrySocket.Configure(AbilitySocket.Glyph.Parry, UITheme.AccentGold);
        // 无障碍开关初始化：设置档读入是直写字段、不发 ReduceFlashChanged（改键那次才发），
        // 而三个构件自己的 _reduceFlash 默认 false——不补这一次，settings.json 里
        // reduce_flash=true 的玩家（正是为降频闪才开它的人）整局仍看到液面起伏/就绪脉冲/坞态灯呼吸，
        // 直到进设置页再拨一次开关。判据与实时读 GameState.Instance.ReduceFlash 的点同源。
        var reduceFlashNow = GameState.Instance.ReduceFlash;
        _fuelTank.SetReduceFlash(reduceFlashNow);
        _dashSocket.SetReduceFlash(reduceFlashNow);
        _parrySocket.SetReduceFlash(reduceFlashNow);
        _dockLamp.SetReduceFlash(reduceFlashNow);
        // HpBar 全息化：底盘更透 + 填充段 ADD 伪泛光
        _hpBar.EmptyColor = new Color(UITheme.SlotDark, 0.25f);
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
        // 里程碑达成给出可见反馈：原实现只写缓存池不发提示，玩家无法把「打得好」与
        // 「点数变多」关联，奖励节奏不可感知（学习曲线断裂）。
        _onMilestoneReached = Callable.From<int>(OnMilestoneReached);
        ConnectGs(GameState.SignalName.MilestoneReached, _onMilestoneReached);
        OnScoreChanged(GameState.Instance.Score);
        OnHealthChanged((float)GameState.Instance.Health);
        _hpBar.SnapGhost(); // 读档续局残血开局：残影对齐实值，不播一次假掉血
        RefreshDifficultyLabel();
        BuildBackplates();
        BuildBanner();
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
        // Boss 血条阶段刻度线（覆盖在血条上随其显隐；比例在 ShowBossBar 由阶段阈值派生注入）
        _bossTicks = new BossBarTicks();
        _bossTicks.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _bossTicks.MouseFilter = Control.MouseFilterEnum.Ignore;
        _bossBar.AddChild(_bossTicks);
        // Boss 血条背板：名牌 + 血条整体纳入切角面板（与角落板块同一语系，随血条显隐；
        // 名牌 abs y 12..42、血条 46..74 → 背板 y 4..92 上下留白）。顶位/高度单源在 core HudLayout：
        // 逃跑倒计时的顶位由同模块推出，改背板高必然带动倒计时位（原实现两处各写字面量，倒计时压在背板上）
        _bossPlate = new ChamferedPanel
        {
            Position = new Vector2(-320.0f, HudLayout.BossPlateTop),
            Size = new Vector2(640.0f, HudLayout.BossPlateHeight),
            Brackets = true,
            EdgeRivets = true,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _bossPlate.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        AddChild(_bossPlate);
        MoveChild(_bossPlate, _bossBar.GetIndex()); // 绘制序压在血条之下
        // Boss 逃跑倒计时（背板下缘之外，剩余 ≤10s 起显示，红色闪烁）
        _bossCountdown = new Label
        {
            Position = new Vector2(-100.0f, HudLayout.BossCountdownTop),
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
        _damageArcs = new HudDamageArcs();
        AddChild(_damageArcs); // 屏幕边缘受击方向弧（显示层，无弧时不参与逐帧）
        BuildAugmentDock();
        BuildCacheIndicator();
        BuildInfoBanner();
        _onAugmentsChanged = Callable.From(RebuildAugmentDock);
        _onKeyBindingsChanged = Callable.From(RefreshAugmentTag);
        _onTalentCacheChanged = Callable.From<double, int>(OnTalentCacheChanged);
        _onReduceFlashChanged = Callable.From<bool>(OnReduceFlashChanged);
        ConnectGs(GameState.SignalName.AugmentsChanged, _onAugmentsChanged);
        ConnectGs(GameState.SignalName.KeyBindingsChanged, _onKeyBindingsChanged);
        ConnectGs(GameState.SignalName.TalentCacheChanged, _onTalentCacheChanged);
        ConnectGs(GameState.SignalName.ReduceFlashChanged, _onReduceFlashChanged);
        RebuildAugmentDock();
        RefreshCacheIndicator();
        _hpBarRest = _hpBar.Position;
        _augmentDockRest = _augmentDockWrap.Position;
    }

    /// <summary>事件条（顶部居中，Boss 血条下方）：标题 + 分段进度 + 计数行。
    /// 标题/计数文案与身份色由调用事件给（精英炮塔＝品红、轰炸编队＝琥珀），HUD 只负责排版、
    /// 翻译与本地化重绘——两个遭遇事件共用一份实现，新增事件不再各建一套计时条。</summary>
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
        _eventCounterLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _eventCounterLabel.AddThemeFontOverride("font", Font);
        _eventCounterLabel.AddThemeFontSizeOverride("font_size", UITheme.FontSmall);
        _eventCounterLabel.AddThemeColorOverride("font_color", UITheme.TextDim);
        _eventBox.AddChild(_eventCounterLabel);
    }

    /// <summary>事件条开启：标题键 + 计数行键（%d 参数由后续 update 给）+ 事件身份色。</summary>
    public void ShowEventBar(string titleKey, string counterKey, Color accent, int counterA, int counterB = 0, int counterC = 0)
    {
        _eventTitleKey = titleKey;
        _eventCounterKey = counterKey;
        _eventTitle.Text = (string)Tr(titleKey);
        _eventTitle.AddThemeColorOverride("font_color", accent);
        _eventBar.FillColor = accent;
        _eventBar.Value = 100.0f;
        _eventBox.Visible = true;
        FadeInTracked(_eventBox, ref _eventTween, EventBarFadeTime);
        SetEventCounter(counterA, counterB, counterC);
    }

    /// <summary>事件进行：剩余进度填充 + 计数行（约 0.1s 节流由调用侧控制）。
    /// 计数行只在数字真的变了时重写（逐帧 Format + 字形重排是白烧）。</summary>
    public void UpdateEventBar(float fill01, int counterA, int counterB = 0, int counterC = 0)
    {
        if (!_eventBox.Visible)
        {
            return;
        }

        _eventBar.Value = Mathf.Clamp(fill01, 0.0f, 1.0f) * 100.0f;
        SetEventCounter(counterA, counterB, counterC);
    }

    public void HideEventBar()
    {
        // 数据缓存立即复位（下一场首帧必须重写文本）；控件先淡出再隐藏，
        // 淡出期间 Visible 仍为 true，但事件侧同帧已不再 Update，视觉上只剩残影渐隐
        _eventCounter[0] = int.MinValue;
        _eventCounter[1] = int.MinValue;
        _eventCounter[2] = int.MinValue;
        if (!_eventBox.Visible)
        {
            return;
        }

        FadeOutTracked(_eventBox, ref _eventTween, EventBarFadeTime);
    }

    /// <summary>计数行写入（数值未变即跳过；GdFormat 忽略多余参数，故单参数文案键可复用同一接口）。
    /// force 用于换语言这类「数值没变但文案必须重排」的路径。</summary>
    private void SetEventCounter(int a, int b, int c, bool force = false)
    {
        if (!force && a == _eventCounter[0] && b == _eventCounter[1] && c == _eventCounter[2])
        {
            return;
        }

        _eventCounter[0] = a;
        _eventCounter[1] = b;
        _eventCounter[2] = c;
        _eventCounterLabel.Text = GdFormat.Format((string)Tr(_eventCounterKey), a, b, c);
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
        [ChargeChannel.MothershipSummon] = ("MS_CHARGING", UITheme.ChargeAccent, -268.0f),
        [ChargeChannel.Homecoming] = ("HOME_CHARGE", UITheme.ChargeAccent, -120.0f),
        [ChargeChannel.GiveUp] = ("GIVE_UP_CHARGE", UITheme.Danger, -164.0f),
        [ChargeChannel.EarlyLeave] = ("MS_EARLY_LEAVE", UITheme.WarnYellow, -220.0f),
        [ChargeChannel.TalentPanel] = ("TALENT_CHARGE_FMT", UITheme.ChargeAccent, -96.0f),
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

    /// <summary>当前仍在显示的蓄力通道（探针读口，零引用保留）：终局路径（死亡 → 树暂停）的
    /// 判据是「结算页上不该残留任何蓄力条」，而只有 HUD 知道哪条通道还在显示；逐通道读私有
    /// 进度需要 5 次往返，这里一次给全。下一 wave 的死亡蓄力趟据此断言，见 ROADMAP 零引用口径。</summary>
    public List<ChargeChannel> VisibleChargeChannels()
    {
        var visible = new List<ChargeChannel>();
        foreach (var kv in _chargeBars)
        {
            if (kv.Value.Visible)
            {
                visible.Add(kv.Key);
            }
        }

        return visible;
    }

    /// <summary>集成仪表盘装配（左下）：单一紧凑面板，两排——上排生命（主读数）＋坞态指示灯，
    /// 下排燃料量槽＋两枚充能槽＋弹仓格。所有方形构件共用 UITheme.ChamferPoints 的切角语汇，
    /// 不出现两套形状语言（这是「像展示品」的主要来源）。
    /// 位置在此单源给出，改布局只动这一处；小标题复用既有翻译键，不新增玩家可见文案。</summary>
    private void BuildInstrumentCluster()
    {
        // 下排：燃料量槽（窄高，液位即读数）
        _fuelTank = new FuelTank();
        PlaceBottomLeft(_fuelTank, 24.0f, -104.0f, 58.0f, -52.0f);
        AddChild(_fuelTank);

        // 下排：冲刺 / 弹反充能槽（同一构件、只换字形与身份色）
        _dashSocket = new AbilitySocket();
        _dashSocket.Configure(AbilitySocket.Glyph.Dash, UITheme.Accent);
        PlaceBottomLeft(_dashSocket, 66.0f, -104.0f, 114.0f, -52.0f);
        AddChild(_dashSocket);

        _parrySocket = new AbilitySocket();
        _parrySocket.Configure(AbilitySocket.Glyph.Parry, UITheme.AccentGold);
        PlaceBottomLeft(_parrySocket, 122.0f, -104.0f, 170.0f, -52.0f);
        AddChild(_parrySocket);

        AddGaugeCaption(UI_FUEL_KEY, 24.0f, 58.0f, -34.0f);
        AddGaugeCaption(UI_DASH_KEY, 66.0f, 114.0f, -34.0f);
        AddGaugeCaption(UI_PARRY_KEY, 122.0f, 170.0f, -34.0f);

        // 下排：弹仓格（母舰驻留时才显；与充能槽同一行）
        _magStrip = new CartridgeStrip { Visible = false };
        PlaceBottomLeft(_magStrip, 184.0f, -90.0f, 284.0f, -76.0f);
        AddChild(_magStrip);

        // 下排右：坞态指示灯 + 状态文本（与充能槽同一行）
        _dockLamp = new AnnunciatorLamp();
        PlaceBottomLeft(_dockLamp, 300.0f, -98.0f, 316.0f, -82.0f);
        AddChild(_dockLamp);
        PlaceBottomLeft(_dockTag, 322.0f, -104.0f, 444.0f, -76.0f);
    }

    private static readonly StringName UI_FUEL_KEY = new("UI_FUEL");

    private static readonly StringName UI_DASH_KEY = new("UI_DASH");

    private static readonly StringName UI_PARRY_KEY = new("UI_PARRY");

    /// <summary>仪表小标题（复用既有文案键，随语言切换刷新）：置于各仪表正下方，字小色淡不压读数。
    /// 纵向位置由调用方给（不同仪表底边不同），宽度与仪表对齐。</summary>
    private void AddGaugeCaption(StringName key, float left, float right, float bottom)
    {
        var caption = new Label
        {
            Text = (string)Tr(key),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        PlaceBottomLeft(caption, left, bottom - 16.0f, right, bottom);
        caption.AddThemeFontOverride("font", Font);
        caption.AddThemeFontSizeOverride("font_size", UITheme.FontSmall);
        caption.AddThemeColorOverride("font_color", UITheme.TextDim);
        AddChild(caption);
        _gaugeCaptions.Add((caption, key));
    }

    /// <summary>仪表小标题（控件 + 键）：语言切换时按键重写文案。</summary>
    private readonly List<(Label Label, StringName Key)> _gaugeCaptions = new();

    /// <summary>按左下锚点摆放（坐标语义与 tscn 的 offset_* 一致，便于与场景节点并置）。</summary>
    private static void PlaceBottomLeft(Control control, float left, float top, float right, float bottom)
    {
        control.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        control.OffsetLeft = left;
        control.OffsetTop = top;
        control.OffsetRight = right;
        control.OffsetBottom = bottom;
    }

    /// <summary>L（augment_panel）切换增幅 滚动栏；暂停态下 HUD 不处理输入（process 继承）。</summary>
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

        // GameState 信号显式断开（GameState 为 autoload 恒存于 root，
        // 本节点释放后存活期信号回调仍指向已释放的 Hud 可致退出崩溃；断开与连接共用
        // 同一字段 Callable，八个信号全部配对（含 TalentCacheChanged）
        DisconnectGs(GameState.SignalName.ScoreChanged, _onScoreChanged);
        DisconnectGs(GameState.SignalName.HealthChanged, _onHealthChanged);
        DisconnectGs(GameState.SignalName.DifficultyChanged, _onDifficultyChanged);
        DisconnectGs(GameState.SignalName.DifficultySelected, _onDifficultySelected);
        DisconnectGs(GameState.SignalName.LocaleChanged, _onLocaleChanged);
        DisconnectGs(GameState.SignalName.MilestoneReached, _onMilestoneReached);
        DisconnectGs(GameState.SignalName.AugmentsChanged, _onAugmentsChanged);
        DisconnectGs(GameState.SignalName.KeyBindingsChanged, _onKeyBindingsChanged);
        DisconnectGs(GameState.SignalName.TalentCacheChanged, _onTalentCacheChanged);
        DisconnectGs(GameState.SignalName.ReduceFlashChanged, _onReduceFlashChanged);
    }

    /// <summary>ReduceFlash 切换：立即清掉仪表可能的残留变暗（下帧按新态重算泵动）；
    /// 开启时同时掐断缓存芯片的呼吸（其门控只在启动时判一次，不切断会带亮闪残留）。</summary>
    private void OnReduceFlashChanged(bool enabled)
    {
        _fuelTank.Modulate = Colors.White;
        _cuePhase = 0.0f;
        // 仪表侧的持续动效（液面起伏、充能槽就绪脉冲、坞态灯呼吸）同样按无障碍开关递减
        _fuelTank.SetReduceFlash(enabled);
        _dashSocket.SetReduceFlash(enabled);
        _parrySocket.SetReduceFlash(enabled);
        _dockLamp.SetReduceFlash(enabled);
        if (enabled)
        {
            if (_cachePulseTween != null)
            {
                _cachePulseTween.Kill();
                _cachePulseTween = null;
            }

            _cacheChip.Modulate = Colors.White;
        }
        else
        {
            _lastCacheRaw = -1; // 关闭后重启呼吸脉冲
            RefreshCacheIndicator();
        }
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
        // autoload 可能先于本节点释放（非常规拆树序），Instance getter 会抛异常，故安全取值
        var gs = GameState.TryGetInstance();
        if (gs != null && gs.IsConnected(signal, callable))
        {
            gs.Disconnect(signal, callable);
        }
    }

    public override void _Process(double delta)
    {
        // 晕影/受击红闪每帧更新（需连续衰减与脉动）；其余仪表类按 POLL_INTERVAL（0.1s）降频，
        // 文本类由信号驱动（见 _ready 连接）
        var d = (float)delta;
        _simTime += d;
        UpdateVignette(d);
        UpdateFuelPulse(d); // 低燃料警戒亮度泵动（空闲时无逐帧写）
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
                cm.A = (long)(_simTime * 1000.0f) / CountdownBlinkHalfPeriodMs % 2 == 0 ? 1.0f : 0.45f;
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

        // 目标进度按本局时钟推进（存活型目标不触发任何信号，只有轮询能捕获到达成线）
        RefreshGoalLabel();

        var player = GameState.Instance.PlayerRef as Player;
        if (player == null)
        {
            return;
        }

        var fuel = player.FuelRatio(); // Player 为 C# typed，动态调用显式 float 型
        // 液罐自身的液位追赶/晃动/气泡全在其 _Process 内，HUD 侧只推目标值（不逐帧碰它）
        _fuelTank.SetRatio(fuel);

        // 警戒态未翻转跳过液色写入（阈值与低燃料脉冲共用同一态缓存；阈值单一来源 FuelGauge）
        var fuelWarn = FuelGauge.IsLow(fuel) ? 1 : 0;
        if (fuelWarn != _lastFuelWarn)
        {
            _lastFuelWarn = fuelWarn;
            _fuelTank.SetWarn(fuelWarn == 1);
            _fuelWarnActive = fuelWarn == 1;
            if (fuelWarn == 0)
            {
                _cuePhase = 0.0f; // 退出警戒即停泵动并复位 alpha
                _fuelTank.Modulate = Colors.White;
            }
        }

        // 冲刺：未取增幅时整槽锁定（与「充能中」区分），已解锁则推充能进度与就绪态
        var dashUnlocked = player.DashUnlocked();
        _dashSocket.SetLocked(!dashUnlocked);
        var dashRatio = dashUnlocked ? player.DashReadyRatio() : 0.0f;
        _dashSocket.SetRatio(dashRatio);
        // 就绪态直接写（SetReady 内部只在翻转时动作，故每轮直调无副作用、无需调用侧去重）
        _lastDashFull = dashUnlocked && dashRatio >= DashFullRatio ? 1 : 0;
        _dashSocket.SetReady(_lastDashFull == 1);

        // 弹反：满格＝可用（二进制语义），其余时间按冷却充能
        var parryRatio = player.ParryEnergyRatio();
        _parrySocket.SetRatio(parryRatio);
        _lastParryFull = parryRatio >= ParryFullRatio ? 1 : 0;
        _parrySocket.SetReady(_lastParryFull == 1);

        if (_main != null)
        {
            var dockText = _main.DockStatusText();
            if (dockText != _lastDockText)
            {
                _dockTag.Text = dockText;
                _lastDockText = dockText;
            }

            UpdateDockLamp(_main.DockStateValue);
            UpdateMagazineBar(_main);
        }
    }

    private void UpdateMagazineBar(Main main)
    {
        Mothership? ms = main.Mothership();
        if (ms != null && (int)ms.GetState() == Mothership.GetStateStay())
        {
            _magStrip.Visible = true;
            _magStrip.SetCapacity(ms.MagCells);
            if (ms.GetMagCells() == _lastMagCells)
            {
                return;
            }

            _lastMagCells = ms.GetMagCells();
            _magStrip.SetFilled(_lastMagCells);
        }
        else
        {
            _magStrip.Visible = false;
            _lastMagCells = -1;
        }
    }

    /// <summary>母舰坞态 → 警示灯态（语义取自 14 CFR 29.1322 三级分类）：
    /// 就绪待命＝绿（安全操作）、蓄力/下降/在场/冷却＝琥珀（进行中或注意）、弹仓不足＝红（须处置）。</summary>
    private void UpdateDockLamp(Main.DockState state)
    {
        var lamp = state == Main.DockState.Ready
            ? AnnunciatorLamp.Lamp.Ready
            : AnnunciatorLamp.Lamp.Busy;

        // 弹仓不足是唯一「须处置」状态：覆盖在场态的琥珀，转红示警（弹药见底时玩家需决策撤离）
        if (state == Main.DockState.Present && _lastMagCells == 0)
        {
            lamp = AnnunciatorLamp.Lamp.Warning;
        }

        if (lamp != _lastDockLamp)
        {
            _lastDockLamp = lamp;
            _dockLamp.SetLamp(lamp);
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
            Position = new Vector2(10.0f, -150.0f),
            Size = new Vector2(446.0f, 136.0f),
            EdgeRivets = true,
        };
        statusPlate.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        AddChild(statusPlate);
        MoveChild(statusPlate, 0);
        var livesTag = MakeCornerTag((string)Tr("UI_LIVES_TAG"));
        livesTag.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        livesTag.Position = new Vector2(24.0f, -172.0f);
        AddChild(livesTag);
        // 上排＝生命（主读数：分段横条 + 数值，占满整行宽度；最常扫视故最大最亮），
        // 下排＝仪表带（燃料/充能/弹仓/坞态）。上下两排，避免与坞态文本压字。
        _hpBar.OffsetTop = -144.0f;
        _hpBar.OffsetBottom = -116.0f;
        _hpBar.OffsetRight = 300.0f;
        _livesLabel.OffsetLeft = 312.0f;
        _livesLabel.OffsetTop = -146.0f;
        _livesLabel.OffsetRight = 436.0f;
        _livesLabel.OffsetBottom = -112.0f;
        // 上下排之间的横向分隔线（分区结构感）
        var statusDivider = new ColorRect
        {
            Color = UITheme.AccentDim,
            Position = new Vector2(24.0f, -112.0f),
            Size = new Vector2(418.0f, 1.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        statusDivider.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        AddChild(statusDivider);
        // 右上难度背板：与分数块同语系（原浮空文字难以在亮背景上阅读）
        // 背板宽度须容纳难度标签三段文案（「难度 x2.50 · 第四档 · 危险 · 中」）；
        // 原 230 宽在加入档名后被文字压过（实测中文约 296px / 英文约 362px）
        var diffPlate = new ChamferedPanel
        {
            Position = new Vector2(-400.0f, 24.0f),
            Size = new Vector2(390.0f, 44.0f),
            EdgeRivets = true,
        };
        diffPlate.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        AddChild(diffPlate);
        MoveChild(diffPlate, 0);
        _difficultyLabel.OffsetLeft = -388.0f;
        _difficultyLabel.OffsetTop = 24.0f;
        _difficultyLabel.OffsetRight = -22.0f;
        _difficultyLabel.OffsetBottom = 68.0f;
        _difficultyLabel.VerticalAlignment = VerticalAlignment.Center;
        var diffTag = MakeCornerTag((string)Tr("UI_DIFF_TAG"));
        diffTag.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        diffTag.Position = new Vector2(-396.0f, 6.0f);
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

    /// <summary>对外公开接口：Boss 逃跑警告经公开入口触发。</summary>
    public void ShowWarning(string text)
    {
        // 互斥缓存——旧警告 tween 仍在跑时 kill 再建，防同属性竞争与 hide 竞态。
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
        // 淡出必须移出循环——把淡出+hide 也包进循环时，首轮末尾 hide 即永久隐藏
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

    /// <summary>绑定 Boss 血条（spawner 经 boss_spawned 信号调用）：从该 Boss 的阶段阈值派生
    /// 段权/段界与刻度线，再连接 Boss 信号。阈值只从活着的 Boss 实例读（其单源是 balance
    /// boss.phase2_hp_ratio / boss.enrage.hp_ratio），HUD 不再另存一份。</summary>
    public void ShowBossBar(Boss boss)
    {
        _bossBar.FillColor = UITheme.Accent; // 重置上一只 Boss 狂暴留下的红色
        // 分段血条：段权由阶段阈值派生（段界＝转阶段点），段数 = 段权数 = 3；
        // 刻度线取同一份派生的段界，两者不可能分叉。取值本身是防御性的域钳（Boss 侧已保证 p2>e）。
        var weights = BossBarSegments.Weights(boss.Phase2HpRatio, boss.EnrageHpRatio);
        var ticks = BossBarSegments.Ticks(boss.Phase2HpRatio, boss.EnrageHpRatio);
        for (var i = 0; i < BossSegWeights.Count; i++)
        {
            BossSegWeights[i] = weights[i];
        }

        _bossTicks.SetRatios(ticks);
        _bossBar.Segments = BossSegWeights.Count;
        _bossBar.SegWeights = BossSegWeights;
        _bossBar.SegColors = BossSegColors;
        _bossBar.Visible = true;
        _bossPlate.Visible = true;
        _bossBar.Value = 100.0f;
        // 血条与背板各自淡入（跨帧缓存的 tween 由 FadeInTracked kill 续接）
        FadeInTracked(_bossBar, ref _bossTween, BossBarFadeTime);
        FadeInTracked(_bossPlate, ref _bossPlateTween, BossBarFadeTime);
        // 换绑前断开上一只 Boss 的四信号（配对连接/断开）——
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
        _bossPhase = FightPhaseP1; // Boss.FightPhase.P1（C# 枚举经常量，见顶部注释）
        boss.HealthChanged += OnBossHealthChanged; // C# [Signal] 以 PascalCase 注册
        boss.Died += OnBossDied;
        boss.Enraged += OnBossEnraged;
        boss.PhaseChanged += OnBossPhaseChanged;
        RefreshBossName();
    }

    private int _lastKills = -1;

    /// <summary>击杀计数标签（不显示分数/连击，内部计分引擎只驱动里程碑/解锁）。
    /// ScoreChanged 伴随击杀/擦弹等高频来源，按计数变化节流格式化。</summary>
    private void OnScoreChanged(int _newScore)
    {
        RefreshKills();
        RefreshGoalLabel();
    }

    private void RefreshKills()
    {
        var kills = GameState.Instance.Kills;
        if (kills == _lastKills)
        {
            return;
        }

        // 计数增加时短促弹跳（首帧 -1 只记态不播；语言切换强制重写不播）
        var gained = _lastKills >= 0 && kills > _lastKills;
        _lastKills = kills;
        _killsLabel.Text = GdFormat.Format((string)Tr("UI_KILLS"), kills);
        if (gained)
        {
            UITheme.PunchScale(_killsLabel, 1.12f, 0.16f);
        }
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
        _hpBar.FillColor = newHealth / maxHp < _lowHpBarRatio ? UITheme.Danger : UITheme.Accent;
        // 回血每帧触发信号，仅整数档位/上限变化时才格式化（连续帧 HP 小数差异不刷新文本）
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
    private int _lastHpInt = -1; // 整数档位守卫，回血逐帧信号时跳过无变化格式化
    private int _lastMaxInt = -1; // 上限整数守卫（extra_life 叠加改变 max_hp 时强制刷新）

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
        _lastDockText = ""; // 强制重写坞态文本（仪表无读数数字，仅小标题与坞态需随语言刷新）
        foreach (var (label, key) in _gaugeCaptions)
        {
            label.Text = (string)Tr(key);
        }

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
            _eventTitle.Text = (string)Tr(_eventTitleKey);
            SetEventCounter(_eventCounter[0], _eventCounter[1], _eventCounter[2], force: true);
        }

        RebuildAugmentDock(true);
        if (_bossBar.Visible)
        {
            RefreshBossName();
        }
    }

    /// <summary>难度标签：难度乘数 + 命名档位 + 难度档设置（如「难度 x2.50 · 第四档 · 危险 · 中」）。
    /// 命名档位让连续爬升可读、可讨论（原只有一个数字，玩家读不出「到哪个阶段了」）。</summary>
    private void RefreshDifficultyLabel()
    {
        // 档名取键区间上限用运行期档位数，不在 HUD 复制一份常量（档位表扩增时不再静默显示错档名）
        var tierMax = Math.Max(GameState.Instance.DifficultyTierCount() - 1, 0);
        var tier = Mathf.Clamp(GameState.Instance.DifficultyTierIndex(), 0, tierMax);
        var tierText = Tr($"DIFF_TIER_{tier}");
        _difficultyLabel.Text = GdFormat.Format(
            (string)Tr("UI_DIFF_FMT"),
            (float)GameState.Instance.DifficultyMultiplier,
            tierText,
            (string)GameState.Instance.DifficultyLabel());
    }

    private void OnBossHealthChanged(float current, float maximum)
    {
        var ratio = Mathf.Clamp(current / maximum, 0.0f, 1.0f);
        var previous = _bossBar.Value / 100.0f;
        _bossBar.Value = ratio * 100.0f;
        // 掉血：给刚被吃掉的那段一次短闪（阶段内段序从左到右，定位方式同 SegmentFill）
        if (ratio < previous - 0.0001f)
        {
            _bossBar.FlashSegment(SegmentedBar.SegmentIndexAt(ratio, BossSegWeights));
        }
    }

    private void OnBossDied()
    {
        // 淡出后隐藏（背板与血条同步）；随后清空绑定
        if (_bossBar.Visible)
        {
            FadeOutTracked(_bossBar, ref _bossTween, BossBarFadeTime);
            FadeOutTracked(_bossPlate, ref _bossPlateTween, BossBarFadeTime);
        }

        _bossCountdown.Visible = false;
        _boss = null;
    }

    private void OnBossEnraged()
    {
        _bossBar.FillColor = UITheme.Danger;
        RefreshBossName();
    }

    /// <summary>阶段切换瞬间血条短闪（§4.2）。ReduceFlash 下只刷新名牌——整条 600px 血条被抬到
    /// 2.2 倍亮度再回落正是该开关要挡的光敏脉冲（同血条的掉段闪已在 SegmentedBar 内门控）。</summary>
    private void OnBossPhaseChanged(int phase)
    {
        _bossPhase = phase;
        RefreshBossName();
        if (GameState.Instance.ReduceFlash)
        {
            _bossBar.Modulate = Colors.White; // 峰值 1.0 即等同无闪（并清掉上一次可能残留的提亮）
            return;
        }

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
        if (_bossPhase == FightPhaseP2) // Boss.FightPhase.P2（C# 枚举经常量）
        {
            phaseText = (string)Tr("BOSS_PHASE_P2");
        }
        else if (_bossPhase == FightPhaseEnrage) // Boss.FightPhase.ENRAGE（C# 枚举经常量）
        {
            phaseText = (string)Tr("BOSS_PHASE_ENRAGE");
        }
        else
        {
            phaseText = (string)Tr("BOSS_PHASE_P1");
        }

        _bossName.Text = GdFormat.Format("%s · %s", (string)Tr($"BOSS_TYPE_{_boss.BossType}"), phaseText);
        _bossName.AddThemeColorOverride("font_color", _bossPhase == FightPhaseEnrage ? UITheme.Danger : UITheme.Text);
    }

    /// <summary>受击/低血屏幕反馈：全屏径向渐变（无新资产，GradientTexture2D 程序化）。</summary>
    private void BuildVignette()
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(UITheme.DangerVignette, 0.0f));
        gradient.SetColor(1, UITheme.DangerVignette);
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

        // LOD0 移交 MetaFX 后处理，旧晕影恒 0；非 0（回退/MetaFX 离场）保留现状
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

    /// <summary>低燃料警戒亮度泵动：仅在警戒态推进（正弦调 _fuelTank.Modulate.a）；
    /// 退出/ReduceFlash 时静置为全亮——不做闪烁，避免无障碍下持续明暗。</summary>
    private void UpdateFuelPulse(float delta)
    {
        if (!_fuelWarnActive)
        {
            return;
        }

        var m = _fuelTank.Modulate;
        if (GameState.Instance.ReduceFlash)
        {
            if (m.A != 1.0f)
            {
                m.A = 1.0f;
                _fuelTank.Modulate = m;
            }

            return;
        }

        _cuePhase += delta;
        var s = (Enemy.SinFast(_cuePhase * Mathf.Tau * FuelPulseHz) + 1.0f) * 0.5f;
        m.A = Mathf.Lerp(FuelPulseMinAlpha, 1.0f, s);
        _fuelTank.Modulate = m;
    }

    /// <summary>淡入并记录 tween（互斥：kill 旧 tween 后再入场，防与残留淡出争同一 modulate.a）。
    /// 语义对齐 UITheme.FadeIn（只动 modulate.a，不动 position——容器布局会覆盖 position）。</summary>
    private static void FadeInTracked(Control control, ref Tween? cache, float time)
    {
        if (cache != null && cache.IsValid())
        {
            cache.Kill();
        }

        control.Modulate = new Color(control.Modulate, 0.0f);
        cache = control.CreateTween();
        cache.TweenProperty(control, "modulate:a", 1.0f, time)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    /// <summary>淡出后隐藏并记录 tween；再次入场时由 FadeInTracked kill 续接（不留半透明残态）。</summary>
    private static void FadeOutTracked(Control control, ref Tween? cache, float time)
    {
        if (cache != null && cache.IsValid())
        {
            cache.Kill();
        }

        cache = control.CreateTween();
        cache.TweenProperty(control, "modulate:a", 0.0f, time)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        cache.TweenCallback(Callable.From(() =>
        {
            control.Visible = false;
            control.Modulate = new Color(control.Modulate, 1.0f); // 复位，供下次淡入
        }));
    }

    /// <summary>Meta HUD DYING 抖动：只抖 _hp_bar 与增幅 坞包装两个控件（±px，80ms burst）。</summary>
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
    /// 右下增幅 区：收起态单行瓦片（最新 4 个 + 溢出 +N，标签带 [L] 快捷键提示），
    /// L 键展开右侧滚动栏（全部增幅 明细，不暂停本局）；与左下状态区/底部蓄力提示分角隔离。
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
    private Label _cacheReadyHint = null!;
    private Label _goalLabel = null!;
    private bool _goalBannerShown;
    private string _goalText = string.Empty;
    private bool _goalWasAchieved;
    private Tween? _cachePulseTween;
    private int _lastCacheRaw = -1;
    private bool _lastAffordable;

    /// <summary>缓存指示器芯片（难度块下方）：点数 + 状态光晕；点击/G 键开天赋面板。
    /// 状态阈值读设置域 SafeThreshold（空闲 0 灰 / 可升级金 / 可用 ≤safe 青+呼吸 /
    /// 溢出警告 ≤safe+safe/3 橙 / 以上红），与衰减起点同源；呼吸脉冲受 ReduceFlash 约束。</summary>
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

        // 「可升级」提示（点数够点亮任一节点时可见；芯片下方一行小字，比只变色更难错过）。
        // 停靠 y=212：让开悬停提示那一行（178），两者可同屏不重叠。
        _cacheReadyHint = UITheme.MakeLabel((string)Tr("TALENT_READY_HINT"), UITheme.FontSmall, UITheme.AccentGold, HorizontalAlignment.Right);
        _cacheReadyHint.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _cacheReadyHint.Position = new Vector2(-192.0f, 212.0f);
        _cacheReadyHint.CustomMinimumSize = new Vector2(172.0f, 0.0f);
        _cacheReadyHint.Visible = false;
        _cacheReadyHint.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_cacheReadyHint);

        _cacheTooltip = UITheme.MakeLabel("", UITheme.FontSmall, UITheme.Text, HorizontalAlignment.Right);
        _cacheTooltip.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _cacheTooltip.Position = new Vector2(-360.0f, 178.0f);
        _cacheTooltip.CustomMinimumSize = new Vector2(340.0f, 0.0f);
        _cacheTooltip.Visible = false;
        _cacheTooltip.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_cacheTooltip);

        // 常驻目标进度（芯片下方一行）：让「打到哪算赢」在达成前就可见——
        // 必死曲线由此从纯挫败变成有终点的挑战（Boss 击杀数或存活时长，任一满足即达成）。
        _goalLabel = UITheme.MakeLabel("", UITheme.FontSmall, UITheme.TextDim, HorizontalAlignment.Right);
        _goalLabel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        // 向左生长：文案（「目标 Boss 3/10 · 里程碑 45%」等）长于最小宽时会向右溢出被视口切掉
        _goalLabel.GrowHorizontal = Control.GrowDirection.Begin;
        _goalLabel.Position = new Vector2(-20.0f, 236.0f);
        _goalLabel.CustomMinimumSize = new Vector2(172.0f, 0.0f);
        _goalLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_goalLabel);
        // 初刷延后到信息横幅构建之后（ShowInfoBanner 依赖 _infoLabel；达成态下同帧调用会空引用）
        Callable.From(RefreshGoalLabel).CallDeferred();

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
        // 可升级态与衰减溢出正交：点数够点亮任一可选节点时，即使未溢出也给出「可升级」提示
        // （原实现只在溢出时才变色，玩家攒够点却无从得知能加点了）。
        var affordable = raw > 0 && gs.Talent.HasAffordableUpgrade();
        // 阈值与衰减起点同源（读设置域 SafeThreshold）：原实现硬编码 20/29，
        // 安全阈值改为 30 后 HUD 说「已溢出」而面板说「未衰减」，玩家可见地互相矛盾
        var safe = gs.Talent.Config.SafeThreshold;
        var warn = safe + Math.Max(safe / 3, 1); // 溢出警告带上界：安全阈值后约三分之一区间
        var (color, border, breathing) = raw switch
        {
            0 => (UITheme.TextDim, new Color(UITheme.PanelBorder, 0.4f), false),
            _ when raw <= safe && affordable => (UITheme.AccentGold, new Color(UITheme.AccentGold, 0.95f), true),
            _ when raw <= safe => (UITheme.Accent, new Color(UITheme.Accent, 0.8f), true),
            _ when raw <= warn => (UITheme.WarnYellow, new Color(UITheme.WarnYellow, 0.9f), false),
            _ => (UITheme.Danger, new Color(UITheme.Danger, 1.0f), false),
        };
        _cacheCount.Text = raw.ToString();
        _cacheCount.AddThemeColorOverride("font_color", color);
        _cacheChip.BorderColor = border;
        _cacheReadyHint.Visible = affordable;
        if (affordable && !_lastAffordable)
        {
            // 点数首次够用：弹一条横幅（与指示器变色同时发生，玩家不看 HUD 也能被通知到）；
            // 之后持续可升级不重复弹，避免每点入账刷屏。
            ShowInfoBanner((string)Tr("TALENT_READY_BANNER"));
        }

        _lastAffordable = affordable;
        if (raw != _lastCacheRaw || !breathing)
        {
            _lastCacheRaw = raw;
            RestartCachePulse(breathing);
        }
    }

    /// <summary>目标进度刷新：未达成显示「离达成还有多少」，达成后显示已达成并（一次性）横幅提示。
    /// 刷新时机＝分数变化（Boss 击杀会加分）与本局时钟轮询（存活型目标需按时间推进）。</summary>
    private void RefreshGoalLabel()
    {
        if (_goalLabel == null)
        {
            return;
        }

        var gs = GameState.Instance;
        // 距下一里程碑的百分比并排显示（同一行）：奖励节奏在达成前就可预期——
        // 原实现只在里程碑达成那一刻给横幅，玩家看不到「还差多少」。
        var milestonePct = (int)Mathf.Round(gs.MilestoneProgress() * 100.0);
        var milestoneText = GdFormat.Format((string)Tr("MILESTONE_PROGRESS"), milestonePct);
        if (gs.GoalAchieved())
        {
            if (!_goalWasAchieved)
            {
                _goalWasAchieved = true;
                _goalText = string.Empty; // 强制文本分支重写（达成态与未达成态各自只写一次）
            }

            _goalLabel.Text = GdFormat.Format((string)Tr("GOAL_PROGRESS"),
                (string)Tr("GO_BOSS_ACHIEVED") + "  ·  " + milestoneText);
            _goalLabel.AddThemeColorOverride("font_color", UITheme.AccentGold);
            if (!_goalBannerShown)
            {
                _goalBannerShown = true;
                ShowInfoBanner((string)Tr("GOAL_ACHIEVED_BANNER"));
            }

            return;
        }

        // 展示「更接近达成」的那个条件。判定走 core RunGoal.CloserKind（经 GameState 门面）——
        // 原实现自己写了一遍并列比较，与 core 的口径只是「碰巧一致」：单支未配置时内联会把 −1
        // 当比例参与比较，core 则直接返回已配置的那一支。改由 core 判定后两处不可能再分叉。
        var killTarget = gs.GoalBossKills();
        var surviveTarget = gs.GoalSurviveSeconds();
        string detail;
        if (gs.GoalCloserKind() != InfiAir.Core.Progression.RunGoalKind.Survive)
        {
            detail = GdFormat.Format((string)Tr("GOAL_BOSS_KILLS"), gs.BossKills, killTarget);
        }
        else
        {
            detail = GdFormat.Format((string)Tr("GOAL_SURVIVE"),
                (int)(gs.RunTime / 60.0), (int)(surviveTarget / 60.0));
        }

        var text = GdFormat.Format((string)Tr("GOAL_PROGRESS"), detail) + "  ·  " + milestoneText;
        if (text == _goalText && !_goalWasAchieved)
        {
            return; // 文本未变即早退（本方法被 0.1s 轮询 × 每次得分双路驱动，避免无谓重写）
        }

        _goalText = text;
        _goalWasAchieved = false;
        _goalLabel.Text = text;
        _goalLabel.AddThemeColorOverride("font_color", UITheme.TextDim);
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

    /// <summary>L 展开的增幅 滚动栏：右缘居中面板（标题 + 分隔线 + 滚动明细行），不暂停本局。</summary>
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
            (string)Tr($"AUG_{id.ToString().ToUpperInvariant()}_NAME"), UITheme.FontHud, UITheme.Text, HorizontalAlignment.Left);
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(nameLabel);
        if (stacks > 1)
        {
            row.AddChild(UITheme.MakeLabel(GdFormat.Format("×%d", stacks), UITheme.FontHud, UITheme.AccentGold, HorizontalAlignment.Right));
        }

        return row;
    }

    /// <summary>收起态溢出格：46×46 同尺寸瓦片（与增幅 socket 同一套：淡色底 + 内框），中央 "+N"。</summary>
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
        _cachedMaxHp = (float)GameState.Instance.MaxHealth(); // 增幅变化（extra_life 层数）时刷新缓存，热路径免查 JSON
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
            _knownAugmentStacks.Clear();
            return;
        }

        // 增量检测：本帧真正新增/叠层的增幅才播入场（旧瓦片原样重建，不重播）
        _gainKeys.Clear();
        foreach (var entryVariant in active)
        {
            var entry = entryVariant.AsGodotArray();
            var key = entry[0].AsStringName().ToString();
            var stacks = (int)entry[1].AsInt64();
            if (!_knownAugmentStacks.TryGetValue(key, out var known) || stacks > known)
            {
                _gainKeys.Add(key);
            }
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
            var tile = UITheme.MakeAugmentTile(entry[0].AsStringName(), (int)entry[1].AsInt64());
            _augmentDock.AddChild(tile);
            if (_gainKeys.Contains(entry[0].AsStringName().ToString()))
            {
                PlayAugmentGain(tile);
            }
        }

        // 同步基线：已入坞（含折叠为溢出格、未展示）的层数一并记住，
        // 否则它们日后滚入可视窗时会被误判为「新获得」而重播
        _knownAugmentStacks.Clear();
        foreach (var entryVariant in active)
        {
            var entry = entryVariant.AsGodotArray();
            _knownAugmentStacks[entry[0].AsStringName().ToString()] = (int)entry[1].AsInt64();
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

    /// <summary>新增/叠层增幅瓦片的入场：pivot 对齐瓦片中心（否则从父左缘弹出、位移错位），
    /// ReduceFlash 下只保留轻缩放，不做亮度泵动。</summary>
    private void PlayAugmentGain(Control tile)
    {
        if (!GodotObject.IsInstanceValid(tile))
        {
            return;
        }

        // 瓦片刚入容器、Size 未排序：以 CustomMinimumSize 定中心（46 方片 → 23,23），
        // 否则 pivot 停在原点，缩放从父槽左上角扩散、视觉错位
        var size = tile.Size;
        if (size.X <= 0.0f || size.Y <= 0.0f)
        {
            size = tile.CustomMinimumSize;
        }

        tile.PivotOffset = size * 0.5f;
        // 缩放入场（暗聚到亮）+ 亮度回落；ReduceFlash 下退化为不缩放的稳定显示
        if (GameState.Instance.ReduceFlash)
        {
            return;
        }

        tile.Scale = new Vector2(0.72f, 0.72f);
        tile.Modulate = new Color(AugmentGainBoost, AugmentGainBoost, AugmentGainBoost);
        var tween = tile.CreateTween().SetParallel(true);
        tween.TweenProperty(tile, "scale", Vector2.One, 0.28)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(tile, "modulate", Colors.White, 0.32)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    /// <summary>增幅 滚动栏开关（L 键路由至此；无增幅 时不展开）。</summary>
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

    /// <summary>信息横幅：显示 ~1.6s 后淡出（位于警告横幅下方，不与其重叠）。
    /// 顺序语义：先停留 TweenInterval，再第一条属性补间，第二条以 Parallel() 与之并行，
    /// 最后 Chain() 收尾 Hide——SetParallel(true) 是「把后续补间与前一步并行」，
    /// 直接跟在 TweenInterval 后会从 t=0 就开始淡出、停留被吞掉（奖励节奏是这条横幅的唯一载体）。</summary>
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
        _infoTween.TweenInterval(InfoBannerHoldSeconds);
        _infoTween.TweenProperty(_infoPlate, "modulate:a", 0.0f, InfoBannerFadeSeconds);
        _infoTween.Parallel().TweenProperty(_infoLabel, "modulate:a", 0.0f, InfoBannerFadeSeconds);
        _infoTween.Chain().TweenCallback(Callable.From(_infoPlate.Hide));
        _infoTween.TweenCallback(Callable.From(_infoLabel.Hide));
    }

    /// <summary>里程碑达成横幅：提示获得的天赋点数（横幅已有复用设施，不新增 HUD 布局）。
    /// 参数是达成时的分数，仅作签名对齐用，不在文案里显示分数（本作分数不显示）。</summary>
    private void OnMilestoneReached(int _score)
    {
        ShowInfoBanner(GdFormat.Format((string)Tr("MILESTONE_BANNER"),
            (int)GameState.Instance.Cfg("talent.grant.points_per_milestone", 2).AsInt64()));
    }

    // ---------------- snake_case 兼容桥（meta_jitter 由 MetaHealthFX 经 CallGroup("hud", "meta_jitter", ...) 动态派发——
    // CallGroup 走方法名字符串，保留原名避免调用点失效） ----------------

    public void meta_jitter(float px) => MetaJitter(px);
}
