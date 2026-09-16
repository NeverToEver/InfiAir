using Godot;
using InfiAir.Core.Text;

// 探针宿主整体条件编译：仅编辑器/调试构建（Debug 定义 TOOLS、ExportDebug 定义 DEBUG）编入，
// 发布导出（ExportRelease 两者皆无）整类不进 InfiAir.dll——测试设施不随发布包分发。
// scene 侧 scenes/probe_host.tscn 已在 export_presets.cfg 排除，二者一致。
#if DEBUG || TOOLS
namespace InfiAir;

/// <summary>
/// 无头探针宿主：门禁冒烟的测试开关与驱动全部收在这里，生产 main.tscn 与 Main 不再读任何测试开关
/// （AGENTS §5「测试/探测设施不进生产路径」）。仅由 <c>scenes/probe_host.tscn</c> 经 <c>--scene</c> 启动，
/// 以子节点嵌入 main.tscn——_EnterTree 里经 <c>Main.MarkHostDriven</c> 显式声明宿主身份（不入场、
/// 不进标题屏、不落盘）；宿主判定由本类主动注入，Main 不反推场景结构、也不依赖本类符号。身份就位后
/// 由本节点显式开启本局可驱动，再经生产触发链请求遭遇（资格/门槛/门控仍由生产判定，探针不得绕过）。
/// 开关、帧数与日志口径的单源在 scripts/ci/check_smoke.sh。
/// </summary>
public partial class ProbeHost : Node
{
    /// <summary>自然全周期探针的玩家无敌时长（s）：无头局玩家不操作，炮塔与炸弹会打死玩家使事件
    /// 中途打断——探针的绿不得依赖「玩家恰好活过事件时长」；死亡路径另行显式击杀。</summary>
    private const float ProbeInvincibleSeconds = 9999.0f;

    /// <summary>死亡探针在遭遇激活后等待的帧数（60 帧＝1s）：等事件推进到可清理状态再击杀，
    /// 覆盖打断的清理分支（精英炮塔升起到位后的炮塔回收），而非入场即打断。</summary>
    private const int DeathProbeDelayFrames = 240;

    /// <summary>迷雾探针强制指定的事件 id：fake_enemies 是无伤害/无碰撞的纯视觉干扰，
    /// 不会改动玩家血量/输入/子弹参数，避免其它三类迷雾把全周期判定搅成「玩家状态被打断」。</summary>
    private static readonly StringName FogProbeEventId = new("fake_enemies");

    /// <summary>全周期判定的帧级容差（s）：EventEnded 由管理器 Timer 在 autoload _Process 内发出，
    /// 早于探针本帧自增的帧计数，逐帧换算会出现不足一帧的差；容差取 0.1s（6 帧）兜住时序差，
    /// 同时远小于任何真实截断（截断至少丢掉整段事件时长）。</summary>
    private const double FogCycleToleranceSeconds = 0.1;

    /// <summary>返航宽限探针蓄力触发返航的帧预算：effects.home_charge_time 1.5s = 90 帧，留 3 倍余量。</summary>
    private const int ReturnProbeChargeFrames = 300;

    /// <summary>返航宽限探针在过场开播后推进的帧数（60 帧＝1 模拟秒）。取 90 帧（1.5 模拟秒 &gt; 默认宽限
    /// 1.2s）作「模拟时间足够越界、真实时间尚未越界」的判别窗口——见 TickReturnProbe 的说明。</summary>
    private const int ReturnProbeGraceWindowFrames = 90;

    /// <summary>返航宽限探针判定用的墙钟余量（ms）：越过宽限后多等一点，抵消边界抖动。</summary>
    private const ulong ReturnProbeGraceMarginMs = 250;

    /// <summary>返航蓄力动作名（与 project.godot 的 homecoming 映射一致，生产判定读同一动作）。</summary>
    private static readonly StringName ActHomecoming = new("homecoming");

    /// <summary>母舰坞态探针的蓄力动作名（与 project.godot 的 dock 映射一致，生产判定读同一动作）。</summary>
    private static readonly StringName ActDock = new("dock");

    /// <summary>Boss 探针入场等待帧上限（入场动画 1.65s + 余量）：超时即判失败，不静默空转到退出。</summary>
    private const int BossProbeEntryWaitFrames = 300;

    /// <summary>Boss 探针两次「拉近阶段阈值」之间的间隔帧数：先让生产链推进出敌弹/开火，
    /// 再注入伤害——这样转场清弹与 P1→P2 在真实受击链上各有可观测的前后态。</summary>
    private const int BossProbeApproachIntervalFrames = 90;

    /// <summary>Boss 探针等待生产触发链请出 Boss 的帧上限：入场 1.65s + <c>boss_min_interval</c>
    /// （实测 80s = 4800 帧）+ 余量。超时即判失败，不静默空转到退出。</summary>
    private const int BossProbeSpawnTimeoutFrames = 5400;

    /// <summary>Boss 探针等入场的上限（帧）：入场动画 1.65s 的余量；超时即判失败——
    /// 入场速度域归零时 IsInFight 恒假、本局被永久冻结且零报错。</summary>
    private const int BossProbeFightTimeoutFrames = 600;

    /// <summary>Boss 逃跑倒计时门控的采样窗口（帧）：闪烁半周期 0.5s（30 帧）+ 0.1s 轮询余量。</summary>
    private const int BossCountdownWindowFrames = 40;

    /// <summary>Boss 探针狂暴锁血等待上限（帧）：狂暴序列 transition 0.9 + active 6 + hold 0.7 +
    /// return 0.8 ≈ 8.4s，取 20s 余量。超时即判失败（锁血残留＝Boss 永久无敌）。</summary>
    private const int BossProbeLockTimeoutFrames = 1200;

    /// <summary>Boss 探针狂暴后推进的帧数（=1 模拟秒）：等主场景主循环走完子弹时间并自行复位
    /// 时间缩放，然后才开始击杀链（否则击杀发生在慢放演出里，时序不可读）。</summary>
    private const int BossProbeEnrageWaitFrames = 60;

    /// <summary>Boss 探针击杀后的收尾帧数：Die() → Died 信号 → 生成器轮换/休整推进。</summary>
    private const int BossProbeKillSettleFrames = 10;

    /// <summary>击杀型遭遇探针在激活后等待的帧数（精英炮塔：等升起到位进入可攻击态；
    /// 生产 rise_time 1.5s + 余量）。</summary>
    private const int KillAllProbeEliteDelayFrames = 240;

    /// <summary>击杀型遭遇探针（编队）等待投弹的帧数上限：等实战投出炸弹后再收场，
    /// 确保「全歼」以外的投弹/拦截路径也被走到（超时即判失败，不静默降档）。</summary>
    private const int KillAllProbeFormationWaitFrames = 1200;

    /// <summary>击杀型遭遇探针连杀编队机的帧间隔：每帧击杀会让同一帧多次结算，隔几帧更贴近实战节奏。</summary>
    private const int KillAllProbeKillIntervalFrames = 3;

    /// <summary>击杀型遭遇探针收尾帧数：等事件收场（结算/台词）后判定。</summary>
    private const int KillAllProbeSettleFrames = 180;

    /// <summary>击杀型遭遇探针连续未命中事件单位的容忍次数：超过即判失败（单位没进注册表）。
    /// 单位在收尾段本就清空，故只在「一次都没杀到」时才据此判红。</summary>
    private const int KillAllProbeMissAttempts = 60;

    /// <summary>击杀型遭遇探针的整趟帧上限：超时即判失败（事件收场链断线），不静默空转到退出。
    /// 精英侧收场要走航母撤离 + BOSS_DELAY 4s，编队侧要走离场 1.5s，取 30s 余量。</summary>
    private const int KillAllProbeTimeoutFrames = 1800;

    /// <summary>击杀型遭遇探针串的事件顺序（先精英炮塔、后轰炸编队）：两事件各自独立触发、
    /// 独立判定，任一不过即整趟失败。</summary>
    private static readonly string[] KillAllProbeIds = { "elite_turret", "formation_strike" };

    /// <summary>母舰坞态探针的长按蓄力帧预算：mothership.dock_charge_time 3s = 180 帧，留 2 倍余量。</summary>
    private const int DockProbeChargeFrames = 360;

    /// <summary>坞态探针的锁输入残留断言：蓄力将满时提前这么多帧经生产输入路径触发冲刺与弹反。
    /// 冲刺 0.25s（15 帧）很短、弹反 0.8s（48 帧）才是覆盖锁定时刻的可靠载体，故两路都触发、
    /// 前置守卫只要求「至少一路在进行中」（任一被吞都要显式报错，不静默降档）。</summary>
    private const int LockProbeTriggerLeadFrames = 12;

    private static readonly StringName ProbeActDash = new("dash");
    private static readonly StringName ProbeActParry = new("parry");

    /// <summary>母舰坞态探针逐阶段等待上限（帧）：任一状态下超过它仍未推进即判失败。</summary>
    private const int DockProbeStageTimeoutFrames = 600;

    /// <summary>母舰坞态探针在警告档之后等提前离舰的帧上限：生产 early_hold_time 2s = 120 帧，
    /// 取 2 倍余量。这个上限同时是**提前离舰与警告到期强制离舰的判别式**：警告横幅 5s 到点会走
    /// 强制离舰（同一段 StartReleaseInternal），若闸门断线、只剩强制路径，离舰会晚到 5s ——
    /// 越过本上限即红。上限也须显著小于整趟帧预算，否则坏法会先撞 --quit-after 的缺标记。</summary>
    private const int DockProbeEjectTimeoutFrames = 240;

    /// <summary>母舰坞态探针等弹匣警告的帧上限：10 格 − 警告档 4 格 = 6 格 × 2s = 12 模拟秒，
    /// 取 15s（余量 25%）。上限同时须留足后续段（提前离舰 + 释放 + 离场）的帧预算——超过整趟
    /// --quit-after 时，「弹匣不耗」的坏法会先撞帧上限而只表现为缺完成标记，探针自己的定位信息
    /// 就丢了，排查时少一条线索。</summary>
    private const int DockProbeMagWarnTimeoutFrames = 900;

    /// <summary>恶意 run.json：语法合法、根为对象、version 合法，但字段类型全错——
    /// 非字典（missions / talent_levels）、非字符串（talent_route / augments）、非数值
    /// （health / score / run_time）、非数组（talent_cache_values）、子表非字典（rp）。</summary>
    private const string HostileRunJson =
        """{"version":1,"missions":5,"talent_levels":[],"talent_overcharged":{},"talent_cache_values":{},"talent_route":7,"augments":3,"last_kind_value":"none","health":"full","score":"lots","run_time":[],"rp":{}}""";

    /// <summary>恶意 settings.json：同样语法合法而字段类型全错，覆盖设置域全部字符串档
    /// （locale / difficulty / view_zoom / window_mode / resolution / aim_assist / fps_cap）
    /// 与数值档（version / custom_* / joy_* / 音量 / 顿帧与震动强度）读点。</summary>
    private const string HostileSettingsJson =
        """{"version":"four","locale":123,"difficulty":[],"view_zoom":{},"window_mode":5,"resolution":true,"aim_assist":[],"fps_cap":{},"ctrl_toggle_mode":"yes","custom_width":"wide","custom_height":[],"joy_aim_speed":"fast","joy_deadzone":[],"vsync":"on","reduce_flash":0,"world_post_fx":"no","mouse_lock":1,"master_volume":"loud","music_volume":{},"sfx_volume":[],"shake_scale":"lots","hit_stop_scale":[],"tutorial_done":"yes","joy_vibration":"off"}""";

    /// <summary>正常 run.json：与下方断言逐项对齐（只判恶意档会让「守卫一律回退」的实现照样绿）。
    /// augments 与 talent_levels 必须一致——两者都来自同一局的写出，生产档里 extra_life 两侧都有
    /// （talent 层级决定生命上限，combat 步的 augments 是被消耗盾层等运行态）。</summary>
    private const string ValidRunJson =
        """{"version":1,"score":900,"kills":7,"boss_kills":2,"combo":5,"milestone_count":4,"run_time":300.0,"difficulty_multiplier":1.6,"dda_timer":12.5,"difficulty_time_step":2,"health":42.5,"augments":{"extra_life":1},"talent_levels":{"extra_life":1},"talent_overcharged":[],"talent_route":"","talent_reset_tokens":1,"talent_bonus_overcharge_slots":1,"talent_cache_values":[],"rp":6,"refresh_points":2,"missions":{"kill_15":{"progress":3,"claimed":false,"goal":15,"baseline":1}},"last_kind_value":{"kill":12}}""";

    /// <summary>正常 settings.json：同前，取值刻意全非默认（回退实现会把它们全部读成默认而判红）。</summary>
    private const string ValidSettingsJson =
        """{"version":4,"locale":"en","difficulty":"hard","view_zoom":"large","window_mode":"windowed","resolution":"1280x720","custom_width":1000,"custom_height":700,"aim_assist":"high","fps_cap":"fps30","vsync":false,"joy_aim_speed":3000.0,"joy_deadzone":0.7,"joy_vibration":false,"master_volume":0.5,"music_volume":0.4,"sfx_volume":0.3,"shake_scale":0.2,"hit_stop_scale":0.1,"tutorial_done":true,"ctrl_toggle_mode":true,"shift_toggle_mode":true,"fire_toggle_mode":true,"reduce_flash":true,"world_post_fx":false,"mouse_lock":false}""";

    /// <summary>正常档还原断言里 health 的期望值（ValidRunJson 的 health 字段）。</summary>
    private const float AssertedHealth = 42.5f;

    /// <summary>存档还原探针写入 run.json 的进度定值（与断言逐项对齐：难度信号值与
    /// ScoreChanged 回调里读到的时间）。</summary>
    private const double AssertedRunTime = 300.0;

    private const double AssertedDifficulty = 1.6;

    /// <summary>恶意 run.json·白名单与上限：missions 含池外 id（查池得 goal 0 → IsMissionDone 恒真）
    /// 与池内 id 但 goal 被改小；augments 含未知键与超上限层级。三处都是手改档可注入的静默错值。</summary>
    private const string HostileWhitelistRunJson =
        """{"version":1,"missions":{"ghost_task":{"progress":5,"claimed":false,"goal":0,"baseline":0},"kill_15":{"progress":3,"claimed":false,"goal":1,"baseline":0}},"talent_levels":{"extra_life":1},"augments":{"ghost_augment":9,"extra_life":99}}""";

    /// <summary>恶意 run.json·任务子表类型错：missions 是字典，但条目内 progress/claimed 类型不符。
    /// 覆盖「子条目判型」这一支（顶层 missions 非字典由上一条恶意档覆盖）。</summary>
    private const string HostileMissionEntryJson =
        """{"version":1,"missions":{"kill_15":{"progress":"lots","claimed":"no","goal":{},"baseline":[]}}}""";

    /// <summary>正常档还原断言里手柄两项的期望值（ValidSettingsJson 的 joy_* 字段）。</summary>
    private const double AssertedJoyAimSpeed = 3000.0;

    private const double AssertedJoyDeadzone = 0.7;

    /// <summary>冒烟每趟传入的隔离根（＝ APPDATA/XDG_DATA_HOME/HOME 的取值，正斜杠分隔）。
    /// 单源在 scripts/ci/check_smoke.sh：门禁侧另有「本趟用户目录下必须出现引擎写出的日志」判据，
    /// 这里断的是「引擎实际用的 user:// 就在本趟临时目录内」——隔离失效必须显式失败。</summary>
    private const string ExpectUserDirPrefix = "--expect-user-dir=";

    /// <summary>读档补发信号的观测值：健康 -1 = 未收到（正常血量恒 &gt; 0）。</summary>
    private float _probeHealthSeen = -1.0f;

    private double _probeJoyAimSpeedSeen = -1.0;
    private double _probeJoyDeadzoneSeen = -1.0;
    private Callable _onProbeHealthChanged;
    private Callable _onProbeJoySettingsChanged;
    private bool _probeSubscribed;
    private bool _hostileProbe;
    private bool _deathGateProbe;

    /// <summary>读档补发信号的观测值：难度信号收到的乘数（-1 = 未收到）与 ScoreChanged 回调里
    /// 读到的 RunTime（-1 = 未收到）。读档直写字段必须把还原值推给消费域——HUD 难度读数与目标行
    /// 都是「_Ready 读一次 + 信号刷新」的缓存型，漏发则读档后停在复位值。</summary>
    private float _probeDifficultySeen = -1.0f;

    private double _probeRunTimeAtScore = -1.0;
    private Callable _onProbeDifficultyChanged;
    private Callable _onProbeScoreChanged;
    private bool _probeRunSubscribed;

    /// <summary>「全部恢复默认」的信号补发观测（设置版本探针）：四个缓存型设置项各计一次。</summary>
    private int _probeViewZoomSignals;

    private int _probeAimAssistSignals;
    private int _probeReduceFlashSignals;
    private int _probeWorldPostFxSignals;
    private Callable _onProbeViewZoom;
    private Callable _onProbeAimAssist;
    private Callable _onProbeReduceFlash;
    private Callable _onProbeWorldPostFx;
    private bool _probeSettingsSubscribed;

    /// <summary>存档还原探针（--save-restore-probe）的临时用户目录由 check_smoke.sh 隔离。</summary>
    private bool _saveRestoreProbe;

    /// <summary>设置版本探针（--settings-version-probe）。</summary>
    private bool _settingsVersionProbe;

    /// <summary>设置页探针里两处频闪的采样窗口（帧）：轮盘开机物化 0.62s、横幅闪烁半周期 ≤0.25s，
    /// 取 30 帧（0.5s）覆盖至少一个完整明暗周期，留出轮询与帧序余量。</summary>
    private const int SettingsFlashWindowFrames = 30;

    private const int SettingsBannerWindowFrames = 45;

    /// <summary>设置页探针的阶段与观测态（0 起 → 4 收尾；sawBlink 是正对照的判据）。</summary>
    private int _settingsProbeStep;

    private int _settingsProbeFrame;
    private bool _settingsProbeSawBlink;
    private RadialWheel? _settingsWheel;

    private Main _main = null!;
    private Player _player = null!;
    private Spawner _spawner = null!;
    private GameEventManager _events = null!;

    private bool _settingsProbe;
    private bool _startupProbe;
    private bool _fuelProbe;
    private bool _shotProbe;
    private bool _feelProbe;
    private bool _longProbe;
    private bool _fogProbe;
    /// <summary>迷雾打断探针：迷雾进行中主动打断，断「存活补偿不发」（与 --fog-probe 的自然到期
    /// 发奖互补；只判一侧会让「一律不发」或「一律发」的实现照样绿）。</summary>
    private bool _fogInterruptProbe;
    private bool _fogInterruptCut;
    private int _fogScoreAtInterrupt;
    private int _fogScoreAtStart;
    /// <summary>增幅缓存连接态探针：池化敌机复用后，慢速力场缓存必须仍接在 AugmentsChanged 上。</summary>
    private bool _augmentCacheProbe;
    private HashSet<ulong> _augmentCachePrevLive = new();
    private readonly HashSet<ulong> _augmentCacheRetired = new();
    private int _augmentCacheReuseFrame = -1;
    private bool _returnProbe;
    private string _shotDir = "";
    private string _eventId = "";
    private bool _deathProbe;
    private bool _startupPrinted;
    private bool _triggerPosted;
    private bool _sawActive;
    private bool _killed;

    /// <summary>遭遇收场后的判定等待帧（QueueFree 在帧末出树、注销登记也在那一刻）：
    /// 收场当帧就断「标记计数回落」会读到尚未出树的单位，属假红。</summary>
    private const int EventEndSettleFrames = 3;

    private int _eventEndSettle;

    /// <summary>辅助瞄准契约断言（遭遇单位）：是否已观测到可瞄准的遭遇单位，避免断言从未执行
    /// （扫描按 is Enemy 判型的坏实现在「从未找到单位」时同样不打失败）。</summary>
    private bool _aimProbeChecked;

    private bool _aimProbeTried;

    /// <summary>辅助瞄准断言的准星注入目标与等待帧（注入后等两帧，让帧缓存的查询点落到单位坐标上）。</summary>
    private IAimTarget? _aimProbeTarget;

    private int _aimHoldFrames;

    /// <summary>死亡打断收尾期的活跃 id 断言（任务：打断后 ActiveId 立即为空）：
    /// 只在事件 FSM 尚未回 IDLE 的窗口内逐帧判——回 IDLE 之后再判空转即过（空转假绿）。</summary>
    private bool _interruptIdChecked;

    /// <summary>编队投放点可见域：上一帧在册的炸弹实例 id（池化复用时同一实例重新入场＝一次新投放，
    /// 故按「本帧在册而上一帧不在册」判定投放，而不是按实例首见）。</summary>
    private HashSet<ulong> _bombIdsPrev = new();

    private bool _bombDomainChecked;

    /// <summary>弹反拆弹路径（IParryable 入口）：是否已发起、发起前的连击/分数、奖励下界与等待帧。</summary>
    private bool _parryProbeFired;
    /// <summary>已反射的那枚弹（命中即入池释放，实例失效即「已消耗」）。</summary>
    private FormationBomb? _parryProbeBomb;

    private bool _parryProbeVerified;

    private int _parryComboBefore;
    private int _parryScoreBefore;
    private int _parryScoreFloor;
    private int _parryWaitFrames;

    /// <summary>弹反路径等待上限（帧）：反射弹寻敌转向 + 命中编队机的余量。</summary>
    private const int ParryProbeTimeoutFrames = 600;
    private bool _fogActive;
    private bool _fogSubscribed;
    private int _fogStartFrame;
    private double _fogDuration;
    private int _returnStage;
    private int _returnChargeFrames;
    private int _returnStartFrame;
    private ulong _returnWallStart;
    private float _returnGrace;
    private int _frame;
    private int _activeFrame;
    private int _fuelStep;
    private int _shotStep;
    private int _feelStep;
    private bool _feelSawHitStop;
    private bool _feelSawTrauma;
    private double _feelEnrageScale = 1.0;

    /// <summary>磁吸测量的注入位移（px，逻辑/视口坐标）：取 20 落在窗口 [2,40) 的中段——
    /// 换算若吃了缩放帧长（TS=0.24 时 ×4.17），量立刻越窗。</summary>
    private const float MagnetProbeDeltaPx = 20.0f;

    /// <summary>磁吸测量的换点重试上限（准星压在标记目标上时走粘滞分支、不换算窗口）。</summary>
    private const int MagnetProbeMaxAttempts = 8;

    private int _magnetState;
    private int _magnetInjectFrame;
    private int _magnetAttempts;
    private Vector2 _magnetBase = new(960.0f, 540.0f);
    private float _magnetValueTs1 = -1.0f;
    private float _magnetValueTs24 = -1.0f;
    private WorldPostFx? _feelPostFx;

    private bool _bossProbe;
    private bool _bossSubscribed;
    private Boss? _boss;
    private int _bossStage;
    private bool _bossTriggerPosted;
    private int _bossTriggerFrame;
    private int _bossKillsBefore;
    private int _bossLastSpawnFrame;
    private int _bossLastTickFrame;
    private int _bossClearCheckFrame;
    private int _bossBulletsBeforeClear;
    private bool _bossSawPhase2BulletsBeforeClear;
    private float _bossPrevHp;
    private bool _bossHpMonotonic = true;
    private bool _bossSawPhase2Invincible;
    private bool _bossSawPhase2Clear;
    private bool _bossKillInjected;
    private bool _bossOutcomeChecked;

    /// <summary>逃跑倒计时门控断言的观测态（HUD 引用、阶段与正对照是否见到翻转）。</summary>
    private Hud? _bossHud;

    private int _bossCountdownStep;
    private int _bossCountdownFrame;
    private bool _bossCountdownSawFlip;
    private bool _bossCountdownDone;

    private bool _killAllProbe;
    private int _killAllIndex;
    private int _killAllPhase;
    private int _killAllActiveFrame;
    private int _killAllStageFrame;
    private int _killAllLastKillFrame;
    private int _killAllKillAttempts;
    private int _killAllScoreAtStart;
    private int _killAllScoreFloor;
    /// <summary>击杀口径的判别式需要**同时**观察击杀数与分数增量：只判分数会被「只加分不计杀」蒙过，
    /// 只判击杀会被「只计杀不加分」（炮塔原生缺陷形态）蒙过。</summary>
    private int _killAllKillsAtStart;

    private int _killAllKilledUnits;
    private bool _killAllSawKill;
    private bool _killAllSawLine;

    private bool _dockProbe;
    private int _dockStage;
    private int _dockChargeFrames;
    private int _dockStateFrame;
    private int _dockReached = -1;
    private int _dockMagCellsStart;
    private float _dockCooldownSeen;
    private bool _dockSawPod;
    private bool _dockSawMagConsume;
    private bool _dockSawMagWarn;

    /// <summary>锁输入残留断言的观测态：锁前逐帧记录两路是否在进行中（锁定当帧据此判判据是否取到），
    /// 锁定窗口内逐帧断已归位，解锁后第一帧再断一次。</summary>
    private bool _lockProbeDashGranted;

    private bool _lockProbeTriggered;

    private bool _lockSeen;

    private bool _lockCheckedAfterUnlock;

    private bool _lockWasDashing;

    private bool _lockWasParryFlowing;

    /// <summary>截图序列：帧号 → 先切到哪一页（空＝不切）→ 捕获名（空＝只切不捕）。
    /// 固定帧捕获——序列本身即「要覆盖哪些视觉面」的清单。切页与捕获隔开若干帧，
    /// 等新页构建并渲染完成（捕获取的是已渲染帧，同帧切页会捕到上一帧）。
    /// 切页与捕获间留 ~0.5s，避开设置页交叉淡入/入场动画（捕在转场中段画面未成形）。</summary>
    private static readonly (int Frame, string Page, string Shot)[] ShotPlan =
    {
        (90, "", "hud"),
        (100, "gameplay", ""),
        (130, "", "settings-gameplay"),
        (140, "display", ""),
        (170, "", "settings-display"),
        (180, "audio", ""),
        (210, "", "settings-audio"),
        (220, "about", ""),
        (250, "", "settings-about"),
        (260, "controls", ""),
        (290, "", "settings-controls"),
    };

    /// <summary>宿主身份注入点：_EnterTree 由父到子（本节点先于子节点 Main），_Ready 由子到父
    /// （Main 先于本节点），故这是**唯一**能赶在 Main._Ready 之前的位置（本类 _Ready 太晚）。
    /// 子节点在场景实例化时已挂上，此处按名字取得到；取不到即报错，不退回嗅探（宁可响，不可悄悄坏）。</summary>
    public override void _EnterTree()
    {
        var main = GetNodeOrNull<Main>("Main");
        if (main == null)
        {
            GD.PushError("[probe-host] 未找到子节点 Main，宿主身份未能注入——本趟将按生产语义启动");
            return;
        }

        main.MarkHostDriven();
    }

    public override void _Ready()
    {
        // 死亡打断会暂停整棵树（结算页接管）；宿主须继续推进才能观测打断收尾
        ProcessMode = ProcessModeEnum.Always;
        _main = GetNode<Main>("Main");
        _player = _main.GetNode<Player>("Player");
        _spawner = GetTree().GetFirstNodeInGroup("spawner") as Spawner ?? _main.GetNode<Spawner>("Spawner");
        _events = GameState.Instance.Events;

        var expectUserDir = "";
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--settings-probe")
            {
                _settingsProbe = true;
            }
            else if (arg == "--startup-time")
            {
                _startupProbe = true;
            }
            else if (arg == "--fuel-probe")
            {
                _fuelProbe = true;
            }
            else if (arg == "--shot-probe")
            {
                _shotProbe = true;
            }
            else if (arg == "--feel-probe")
            {
                _feelProbe = true;
            }
            else if (arg == "--long-probe")
            {
                _longProbe = true;
            }
            else if (arg == "--fog-probe")
            {
                _fogProbe = true;
            }
            else if (arg == "--fog-interrupt-probe")
            {
                _fogInterruptProbe = true;
            }
            else if (arg == "--return-probe")
            {
                _returnProbe = true;
            }
            else if (arg == "--boss-probe")
            {
                _bossProbe = true;
            }
            else if (arg == "--dock-probe")
            {
                _dockProbe = true;
            }
            else if (arg == "--hostile-save-probe")
            {
                _hostileProbe = true;
            }
            else if (arg == "--death-gate-probe")
            {
                _deathGateProbe = true;
            }
            else if (arg == "--save-restore-probe")
            {
                _saveRestoreProbe = true;
            }
            else if (arg == "--settings-version-probe")
            {
                _settingsVersionProbe = true;
            }
            else if (arg.StartsWith(ExpectUserDirPrefix, System.StringComparison.Ordinal))
            {
                expectUserDir = arg[ExpectUserDirPrefix.Length..];
            }
            else if (arg == "--augment-cache-probe")
            {
                _augmentCacheProbe = true;
            }
            else if (arg.StartsWith("--shot-dir=", System.StringComparison.Ordinal))
            {
                _shotDir = arg["--shot-dir=".Length..];
            }
            else if (arg == "--event-probe-killall")
            {
                _killAllProbe = true;
            }
            else if (arg.StartsWith("--event-probe-death=", System.StringComparison.Ordinal))
            {
                _eventId = arg["--event-probe-death=".Length..];
                _deathProbe = true;
            }
            else if (arg.StartsWith("--event-probe=", System.StringComparison.Ordinal))
            {
                _eventId = arg["--event-probe=".Length..];
            }
        }

        VerifyUserDirIsolation(expectUserDir);

        if (_eventId.Length > 0 || _feelProbe || _longProbe || _fogProbe || _fogInterruptProbe || _returnProbe
            || _bossProbe || _dockProbe || _killAllProbe || _augmentCacheProbe)
        {
            // Main 嵌入宿主时关闭了本局可驱动（防随机事件破坏宿主场景的确定性），
            // 探针即宿主，显式开启——遭遇触发链的资格/门槛/门控仍全部走生产判定。
            GameState.Instance.SetRunActive(true);
            // Boss/坞态探针要连跑 80 秒以上的模拟时长（Boss 最小间隔门；坞态全周期），
            // 而遭遇事件的自动掷签在这段窗口里会连中（精英 45s×35%、编队 40s×30%）：
            // 事件一开就冻结 Boss 调度、暂停波次，并挡下 Main 的坞蓄力（互斥）。
            // 探针不请求任何遭遇，也不缩短任何生产时长，故这两趟整体关掉遭遇驱动——
            // 红绿因此只取决于生产链本身，与随机掷签无关（AGENTS §5）。
            // 遭遇本身的覆盖由 --event-probe 各趟负责。
            _events.SetRunActive(!(_bossProbe || _dockProbe));
            // 遭遇/手感/长局探针一律关闭迷雾随机事件：首延迟（25s）过后被精英炮塔等长趟越过，
            // 此后每 3s 有 35% 概率触发迷雾（方向偏转会把无头局的玩家推入弹幕致死），
            // 探针红绿将取决于随机数——违反 §5「随机要么避免、要么走可注入取值源」。
            // 迷雾由 --fog-probe 专趟覆盖（该趟保持开启，仍走生产资格/门槛）。
            _events.FOG_ENABLED = _fogProbe || _fogInterruptProbe;
            if (_fogProbe || _fogInterruptProbe)
            {
                // 统一信号监听（完整周期判定：start→end）；退订在 _ExitTree 配对
                _events.EventStarted += OnFogEventStarted;
                _events.EventEnded += OnFogEventEnded;
                _fogSubscribed = true;
                if (!_events.RequestForcedFog(FogProbeEventId))
                {
                    GD.PushError("[fog-probe] 强制迷雾事件请求失败：id 未注册或非 fog 组");
                    _fogProbe = false;
                    _fogInterruptProbe = false;
                }
            }
        }
    }

    /// <summary>设置信号退订（存在性判断——探针可能在订阅前就失败退出）。</summary>
    private static void DisconnectSettingSignal(GameState gs, StringName signal, Callable callable)
    {
        if (gs.IsConnected(signal, callable))
        {
            gs.Disconnect(signal, callable);
        }
    }

    /// <summary>隔离判据（本类唯一的运行时口径）：本趟用户数据根由 check_smoke.sh 以
    /// <c>--expect-user-dir=</c> 传入（其取值＝ APPDATA/XDG_DATA_HOME/HOME，正斜杠分隔），
    /// 与引擎实际 <c>user://</c> 目录做前缀包含比较。不符即 PushError——被冒烟的错误正则按趟判红。
    ///
    /// 为什么判前缀而不是判相等：Windows 上 user:// 落在 <c>&lt;APPDATA&gt;/Godot/app_userdata/InfiAir</c>，
    /// 是隔离根的子路径；前缀以 `/` 收尾，避免 `/tmp/probe-x` 误配到 `/tmp/probe-xyz`（同前缀不同目录）。
    /// Windows 路径大小写不敏感，比较时忽略大小写。
    ///
    /// 未传参时只告警不报错：手动直跑探针（README/AGENTS 记的调试用法）不带该参数，
    /// 而门禁侧另有「本趟用户目录下必须出现引擎写出的 godot.log」判据兜底隔离失效。</summary>
    private static void VerifyUserDirIsolation(string expected)
    {
        if (expected.Length == 0)
        {
            GD.PushWarning("[probe-host] 未传 --expect-user-dir=，跳过用户目录隔离比对（门禁一律传参）");
            return;
        }

        var actual = ProjectSettings.GlobalizePath("user://").Replace('\\', '/').TrimEnd('/') + "/";
        var want = expected.Replace('\\', '/').TrimEnd('/') + "/";
        var cmp = OS.GetName() == "Windows"
            ? System.StringComparison.OrdinalIgnoreCase
            : System.StringComparison.Ordinal;
        if (actual.StartsWith(want, cmp))
        {
            return;
        }

        GD.PushError(GdFormat.Format(
            "[probe-host] 用户目录隔离失效：引擎实际 user:// 为 %s，不在本趟隔离根 %s 之内——"
            + "本趟可能已读写开发者本机存档/设置", actual, want));
    }

    public override void _ExitTree()
    {
        if (_fogSubscribed)
        {
            _fogSubscribed = false;
            _events.EventStarted -= OnFogEventStarted;
            _events.EventEnded -= OnFogEventEnded;
        }

        if (_bossSubscribed)
        {
            _bossSubscribed = false;
            _spawner.BossSpawned -= OnBossProbeSpawned;
        }

        if (_probeRunSubscribed)
        {
            _probeRunSubscribed = false;
            var gsRun = GameState.TryGetInstance();
            if (gsRun != null)
            {
                if (gsRun.IsConnected(GameState.SignalName.DifficultyChanged, _onProbeDifficultyChanged))
                {
                    gsRun.Disconnect(GameState.SignalName.DifficultyChanged, _onProbeDifficultyChanged);
                }

                if (gsRun.IsConnected(GameState.SignalName.ScoreChanged, _onProbeScoreChanged))
                {
                    gsRun.Disconnect(GameState.SignalName.ScoreChanged, _onProbeScoreChanged);
                }
            }
        }

        if (_probeSettingsSubscribed)
        {
            _probeSettingsSubscribed = false;
            var gsSet = GameState.TryGetInstance();
            if (gsSet != null)
            {
                DisconnectSettingSignal(gsSet, GameState.SignalName.ViewZoomChanged, _onProbeViewZoom);
                DisconnectSettingSignal(gsSet, GameState.SignalName.AimAssistChanged, _onProbeAimAssist);
                DisconnectSettingSignal(gsSet, GameState.SignalName.ReduceFlashChanged, _onProbeReduceFlash);
                DisconnectSettingSignal(gsSet, GameState.SignalName.WorldPostFxChanged, _onProbeWorldPostFx);
            }
        }

        if (_probeSubscribed)
        {
            _probeSubscribed = false;
            var gs = GameState.TryGetInstance();
            if (gs != null)
            {
                if (gs.IsConnected(GameState.SignalName.HealthChanged, _onProbeHealthChanged))
                {
                    gs.Disconnect(GameState.SignalName.HealthChanged, _onProbeHealthChanged);
                }

                if (gs.IsConnected(GameState.SignalName.JoySettingsChanged, _onProbeJoySettingsChanged))
                {
                    gs.Disconnect(GameState.SignalName.JoySettingsChanged, _onProbeJoySettingsChanged);
                }
            }
        }
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_startupProbe && !_startupPrinted)
        {
            _startupPrinted = true;
            GD.Print(GdFormat.Format("[startup] boot → first frame: %d ms",
                (long)Time.GetTicksMsec() - GameState.Instance.BootTicksMsec));
        }

        if (_hostileProbe && _frame >= 2)
        {
            _hostileProbe = false;
            RunHostileSaveProbe();
            return;
        }

        if (_saveRestoreProbe && _frame >= 2)
        {
            _saveRestoreProbe = false;
            RunSaveRestoreProbe();
            return;
        }

        if (_settingsVersionProbe && _frame >= 2)
        {
            _settingsVersionProbe = false;
            RunSettingsVersionProbe();
            return;
        }

        if (_deathGateProbe && _frame >= 2)
        {
            _deathGateProbe = false;
            RunDeathGateProbe();
            return;
        }

        if (_settingsProbe)
        {
            TickSettingsProbe();
            return;
        }

        if (_fuelProbe)
        {
            TickFuelProbe();
            return;
        }

        if (_feelProbe)
        {
            TickFeelProbe();
            return;
        }

        if (_longProbe)
        {
            TickLongProbe();
            return;
        }

        if (_fogProbe || _fogInterruptProbe)
        {
            TickFogProbe();
            return;
        }

        if (_returnProbe)
        {
            TickReturnProbe();
            return;
        }

        if (_bossProbe)
        {
            TickBossProbe();
            return;
        }

        if (_dockProbe)
        {
            TickDockProbe();
            return;
        }

        if (_shotProbe)
        {
            TickShotProbe();
            return;
        }

        if (_killAllProbe)
        {
            TickKillAllProbe();
            return;
        }

        if (_augmentCacheProbe)
        {
            TickAugmentCacheProbe();
            return;
        }

        if (_eventId.Length > 0)
        {
            TickEventProbe();
        }
    }

    /// <summary>签名网格（粗）：只判「有没有画面内容」与「各页是否不同」，
    /// 不做像素级比对——占位内容（帧率/垂直同步读出、背景星空动画）本就随环境变化，
    /// 跨渲染器比像素必然误报。细粒度退化归 core 单测与断言探针。</summary>
    private const int SigW = 16;
    private const int SigH = 9;

    /// <summary>非空白判定：签名通道极差下限。真界面明暗跨度大（面板/文字/高光），
    /// 黑屏或渲染全坏时极差趋 0。</summary>
    private const int MinSpread = 24;

    /// <summary>页面可区分判定：五张设置页两两签名最大差的下限。页面切换失效
    /// （始终同一页/空白）时各页趋同。</summary>
    private const int MinPageDiff = 12;

    private readonly System.Collections.Generic.Dictionary<string, int[]> _shotSigs = new();
    private readonly System.Collections.Generic.List<string> _shots = new();

    /// <summary>截图驱动：按计划帧切页/捕获；序列结束后做两条廉价自检（非空白、页面互异），
    /// 全过才打完成标记（缺标记即门禁红）。</summary>
    private void TickShotProbe()
    {
        foreach (var step in ShotPlan)
        {
            if (step.Frame != _frame)
            {
                continue;
            }

            if (step.Page.Length > 0)
            {
                var settings = GetTree().GetFirstNodeInGroup("settings_ui") as SettingsUi;
                settings?.ShowSettings(null);
                settings?.ShowPage(new StringName(step.Page));
            }

            if (step.Shot.Length > 0)
            {
                CaptureShot(step.Shot);
            }

            return;
        }

        if (_frame > ShotPlan[^1].Frame && _shotProbe)
        {
            _shotProbe = false;
            VerifyShots();
        }
    }

    /// <summary>截图自检：计划张数全部入账 + 每张非空白 + 五张设置页两两可区分。任一不过就不打完成标记，
    /// 由门禁按「缺标记」判红（与其它探针同一口径）。
    ///
    /// 张数必须单独判：`_shots` 只收成功写出的图（取像/写出失败会 PushError 后跳过），
    /// 少了图时以下两个循环只是少比几对、`ok` 仍为 true——全靠已入账的图自证，零张时循环空转、
    /// 照样打完成标记（假绿）。期望张数从 ShotPlan 派生，不另写一份。</summary>
    private void VerifyShots()
    {
        var ok = true;
        var expected = 0;
        foreach (var step in ShotPlan)
        {
            if (step.Shot.Length > 0)
            {
                expected++;
            }
        }

        if (_shots.Count < expected)
        {
            GD.PushError(GdFormat.Format(
                "[shot-probe] 实际捕获 %d 张，少于计划 %d 张——取像或写出失败（日志里应有对应的失败行）",
                _shots.Count, expected));
            ok = false;
        }

        foreach (var name in _shots)
        {
            var sig = _shotSigs[name];
            var min = int.MaxValue;
            var max = int.MinValue;
            foreach (var v in sig)
            {
                if (v < min) { min = v; }
                if (v > max) { max = v; }
            }

            if (max - min < MinSpread)
            {
                GD.PushError(GdFormat.Format("[shot-probe] %s 画面近乎空白（极差 %d）", name, max - min));
                ok = false;
            }
        }

        for (var i = 0; i < _shots.Count; i++)
        {
            for (var j = i + 1; j < _shots.Count; j++)
            {
                var a = _shotSigs[_shots[i]];
                var b = _shotSigs[_shots[j]];
                var worst = 0;
                for (var k = 0; k < a.Length; k++)
                {
                    var d = System.Math.Abs(a[k] - b[k]);
                    if (d > worst) { worst = d; }
                }

                if (worst < MinPageDiff)
                {
                    GD.PushError(GdFormat.Format(
                        "[shot-probe] %s 与 %s 画面几乎相同（最大差 %d）——页面切换可能失效",
                        _shots[i], _shots[j], worst));
                    ok = false;
                }
            }
        }

        if (ok)
        {
            GD.Print(GdFormat.Format("[shot-probe] 截图序列完成（%d 张）", _shots.Count));
        }
    }

    /// <summary>捕获当前视口：存 PNG（生成截图用） + 记下粗签名（供上面的自检）。
    /// 目录由 <c>--shot-dir=</c> 指定（门禁侧传绝对路径）。</summary>
    private void CaptureShot(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        if (image == null)
        {
            GD.PushError(GdFormat.Format("[shot-probe] 视口取像失败：%s", name));
            return;
        }

        var png = _shotDir.Length > 0
            ? GdFormat.Format("%s/%s.png", _shotDir, name)
            : GdFormat.Format("user://%s.png", name);
        var err = image.SavePng(png);
        if (err != Error.Ok)
        {
            GD.PushError(GdFormat.Format("[shot-probe] 写出失败 %s：%s", png, err));
            return;
        }

        // 缩到粗网格后逐格取平均灰阶：只用于「有内容/各页不同」两条自检
        image.Resize(SigW, SigH, Image.Interpolation.Bilinear);
        var sig = new int[SigW * SigH];
        for (var i = 0; i < sig.Length; i++)
        {
            var c = image.GetPixel(i % SigW, i / SigW);
            sig[i] = (int)Mathf.Round(((c.R + c.G + c.B) / 3.0f) * 255.0f);
        }

        _shotSigs[name] = sig;
        _shots.Add(name);
    }

    /// <summary>长局难度曲线探针：把生产换算出的各量与**行业对齐的预期带**逐点比对。
    ///
    /// 为什么不用「跑 30 分钟再看」：难度映射是 D 的纯函数，长跑只是采样同一函数。
    /// 这里直接取 t = 5/10/20/30 分钟对应的 D（用生产曲线算），断言：
    ///   ① HP 与伤害乘区单调不减且斜率比在预期内（HP 快于伤害，与行业「HP 缩放缓于伤害但都低于玩家成长」一致）；
    ///   ② 速度乘区有硬顶（不得随 D 无限增长）；
    ///   ③ 波次间隔触底后不再缩、且不越过地板；
    ///   ④ 精英数量随 D 增长且有上限；
    ///   ⑤ Boss HP 乘区**慢于**完整 D（修正前等于 D，是 Boss 成墙的根因）；
    ///   ⑥ 时间项软上限之后斜率折减（挂机不再等比推高必死点）。
    /// 任一条不成立即 PushError 且不打完成标记（门禁按缺标记判红）。</summary>
    private void TickLongProbe()
    {
        if (_frame < 30)
        {
            return;
        }

        var cfg = GameState.Instance.Scaling();

        // 用**生产入口**求 t 时刻的难度乘数，而不是在探针里复刻公式：
        // 复刻会让「曲线公式改坏」时本探针仍测旧公式的形状、标记照打（护栏与生产脱钩）。
        // DifficultyCurve.Compute 就是 RunProgressionService.RecomputeDifficultyInternal 的同一函数。
        var timeStep = GameState.Instance.Cfg("progression.time_step_seconds", 30.0).AsDouble();
        var perTen = GameState.Instance.Cfg("progression.per_ten_minutes", 1.5).AsDouble();
        var perBoss = GameState.Instance.Cfg("progression.per_boss_kill", 0.6).AsDouble();

        double DifficultyAt(double minutes)
        {
            var raw = Core.Progression.DifficultyCurve.Compute(minutes * 60.0, timeStep, perTen, perBoss, 0);
            // 时间项折减与生产同源（RunProgressionService 消费的同一函数）
            var rawTimeTerm = raw - 1.0;
            var capped = Core.Progression.DifficultyScaling.SoftCappedTimeTerm(rawTimeTerm, cfg);
            return 1.0 + capped;
        }

        double prevHp = 0.0;
        double prevDmg = 0.0;
        foreach (var minutes in new[] { 5.0, 10.0, 20.0, 30.0 })
        {
            var d = DifficultyAt(minutes);
            var hp = Core.Progression.DifficultyScaling.EnemyHpRamp(d, cfg);
            var dmg = Core.Progression.DifficultyScaling.EnemyDamageRamp(d, cfg);
            var speed = Core.Progression.DifficultyScaling.EnemySpeedRamp(d, cfg);
            var bossHp = Core.Progression.DifficultyScaling.BossHpRamp(d, cfg);
            var wave = Core.Progression.DifficultyScaling.WaveInterval(4.0, d, cfg);
            var elites = Core.Progression.DifficultyScaling.EliteCount(d, cfg);

            if (hp < prevHp || dmg < prevDmg)
            {
                GD.PushError($"[long-probe] 难度乘区回退于 {minutes}min（hp={hp:0.###} dmg={dmg:0.###}）");
                _longProbe = false;
                return;
            }

            if (hp <= dmg)
            {
                GD.PushError($"[long-probe] {minutes}min 的 HP 乘区未超过伤害乘区（hp={hp:0.###} dmg={dmg:0.###}）");
                _longProbe = false;
                return;
            }

            if (speed > cfg.SpeedRampCap + 1e-6)
            {
                GD.PushError($"[long-probe] {minutes}min 速度乘区 {speed:0.###} 越过硬顶 {cfg.SpeedRampCap:0.###}");
                _longProbe = false;
                return;
            }

            // 硬顶必须真的「咬得住」：只判「不越顶」在把上限配成极大值时同样通过（等于没有上限）。
            // 末尾取极高 D，速度乘区必须恰好等于上限。
            if (minutes >= 30.0)
            {
                var pinned = Core.Progression.DifficultyScaling.EnemySpeedRamp(1000.0, cfg);
                if (Math.Abs(pinned - cfg.SpeedRampCap) > 1e-6)
                {
                    GD.PushError($"[long-probe] 速度硬顶未生效：D=1000 时 {pinned:0.###} ≠ 上限 {cfg.SpeedRampCap:0.###}");
                    _longProbe = false;
                    return;
                }
            }

            if (wave < cfg.WaveIntervalFloor - 1e-6)
            {
                GD.PushError($"[long-probe] {minutes}min 波次间隔 {wave:0.###}s 跌破地板 {cfg.WaveIntervalFloor:0.###}s");
                _longProbe = false;
                return;
            }

            if (elites < 1 || elites > cfg.EliteCountCap)
            {
                GD.PushError($"[long-probe] {minutes}min 精英数量 {elites} 越界 [1, {cfg.EliteCountCap}]");
                _longProbe = false;
                return;
            }

            // Boss HP 必须慢于完整 D（修正前 = D，斜率是杂兵 4 倍）
            if (bossHp >= d)
            {
                GD.PushError($"[long-probe] {minutes}min Boss HP 乘区未低于完整难度乘数（boss={bossHp:0.###} d={d:0.###}）");
                _longProbe = false;
                return;
            }

            // Boss 攻击密度必须随 D 增长（后期压力要有「密度」这一轴，而不只是更肉更痛）
            var density = Core.Progression.DifficultyScaling.BossDensityBonus(d, cfg);
            if (minutes >= 30.0 && density <= 0)
            {
                GD.PushError($"[long-probe] {minutes}min Boss 攻击密度追加为 0（密度未接难度）");
                _longProbe = false;
                return;
            }

            if (density > cfg.BossDensityBonusCap)
            {
                GD.PushError($"[long-probe] {minutes}min Boss 密度追加 {density} 越过上限 {cfg.BossDensityBonusCap}");
                _longProbe = false;
                return;
            }

            prevHp = hp;
            prevDmg = dmg;
        }

        // 精英数量必须真的随时间增长（否则「后期靠密度」根本没接上）
        var eliteEarly = Core.Progression.DifficultyScaling.EliteCount(DifficultyAt(1.0), cfg);
        var eliteLate = Core.Progression.DifficultyScaling.EliteCount(DifficultyAt(30.0), cfg);
        if (eliteLate <= eliteEarly)
        {
            GD.PushError($"[long-probe] 精英数量不随难度增长（1min={eliteEarly} 30min={eliteLate}）");
            _longProbe = false;
            return;
        }

        // 时间项软上限：折减后必须仍单调不减、且慢于不折减
        var raw = 20.0;
        var capped = Core.Progression.DifficultyScaling.SoftCappedTimeTerm(raw, cfg);
        if (!(capped > 0.0) || capped >= raw)
        {
            GD.PushError($"[long-probe] 时间项软上限未生效（raw={raw} capped={capped:0.###}）");
            _longProbe = false;
            return;
        }

        // 里程碑进度：开局应为 0（未得分），且必须落在 [0,1]——算错会让 HUD 条骗人
        var mp = GameState.Instance.MilestoneProgress();
        if (mp < 0.0 || mp > 1.0)
        {
            GD.PushError($"[long-probe] 里程碑进度越界：{mp:0.###}（应在 [0,1]）");
            _longProbe = false;
            return;
        }

        // 本局达成判定与命名档位（生产配置）：达成线必须可达、档位必须随 D 单调推进。
        // 达成判定坏掉的表现是「打了很久也没算赢」，无头下不崩不报错，只能靠断言。
        var goalCfg = new Core.Progression.RunGoalConfig
        {
            BossKillsTarget = GameState.Instance.GoalBossKills(),
            SurviveSeconds = GameState.Instance.GoalSurviveSeconds(),
        };
        if (goalCfg.BossKillsTarget > 0 && Core.Progression.RunGoal.Achieved(goalCfg.BossKillsTarget, 0.0, goalCfg) == false)
        {
            GD.PushError("[long-probe] 达不成线不可达：Boss 击杀数达到目标仍判未达成");
            _longProbe = false;
            return;
        }

        if (goalCfg.SurviveSeconds > 0.0 && !Core.Progression.RunGoal.Achieved(0, goalCfg.SurviveSeconds, goalCfg))
        {
            GD.PushError("[long-probe] 达不成线不可达：存活到目标时长仍判未达成");
            _longProbe = false;
            return;
        }

        if (Core.Progression.RunGoal.Achieved(0, 0.0, goalCfg))
        {
            GD.PushError("[long-probe] 开局即判达成（目标线配置失效）");
            _longProbe = false;
            return;
        }

        var tierEarly = GameState.Instance.DifficultyTierIndex();
        if (tierEarly != 0)
        {
            GD.PushError($"[long-probe] 开局难度档位应为 0，实为 {tierEarly}");
            _longProbe = false;
            return;
        }

        GD.Print("[long-probe] 难度曲线落在预期带");
        _longProbe = false;
    }

    /// <summary>手感探针：请求四档顿帧与一次震动，断言「时间缩放确实被压低 + trauma 确实累加 +
    /// 顿帧在真实时间下会自行结束」。
    ///
    /// 为什么不能只判「不崩」：顿帧写的是 Engine.TimeScale——写错（倍率写成 0 或按缩放 delta 推进）
    /// 的表现是**画面永久定格**，无头下不崩、也不报错，只有完成标记能抓住。
    ///
    /// **断言必须读引擎真值**：只读手感域自己算出的 `FeelTimeScale()` 会漏掉「算了但没落笔」这一整类
    /// 故障（实测：删掉 `GameFeelService.ApplyTimeScale` 的赋值，自算值仍返回正常值、探针照样全绿）。
    /// 故冻结中断言 `Engine.TimeScale` 真的 &lt; 1，复位后断言它真的回到 1。震动的读口读 trauma——
    /// 位移采样在 `CameraShake._Process`，而相机不在 headless 探针宿主内，故只断言到「trauma 确实被累加/衰减」。
    /// 顺序固定：先等场景稳定，再请求顿帧/震动，顿帧自行结束、trauma 自行衰减后，上报演出倍率
    /// 并暂停一次——暂停是「清冻结 + 清演出倍率」的复位口，只清一半会让 Always UI 慢放。
    ///
    /// 另断两条与「同一帧长口径」相关的静默坏点：
    ///   ① 减少震动强度 25% 下重击泛光仍出现（震屏信号发**原始强度**，滑杆只管运动不连坐频闪）；
    ///   ② 磁吸输入窗口的**时间缩放不变性**（同一手部位移在 TS=1 与 TS=0.24 下进窗口的量相等且落窗内）。</summary>
    private void TickFeelProbe()
    {
        // 前 30 帧等入场与稳定（入场窗口内玩家不可驱动、GameState 时钟未起）
        if (_frame < 30)
        {
            return;
        }

        if (GameState.Instance.HitStopActive())
        {
            // 冻结中：**引擎**的时间缩放必须真被压低（读 Engine.TimeScale，不读自算值）。
            // 探针宿主 ProcessMode=Always 且本驱动每帧都在跑，故这里能看到冻结窗口。
            if ((float)Engine.TimeScale >= 1.0f)
            {
                GD.PushError($"[feel-probe] 顿帧激活但引擎时间缩放未压低（Engine.TimeScale={(float)Engine.TimeScale:0.###}）");
                _feelProbe = false;
                return;
            }

            // trauma 由震动请求累加，与顿帧独立——两者都断言，任一路坏都要红
            if (GameState.Instance.ShakeTrauma() > 0.0)
            {
                _feelSawTrauma = true;
            }

            _feelSawHitStop = true;
            return;
        }

        if (_feelStep == 0)
        {
            // 减少震动强度滑杆 ≤33% 时重击泛光不得连坐：震屏信号发折算值（24×0.25=6 < 泛光阈值 8）
            // 会让脉冲恒 0，而「屏幕震动强度」与「减少闪光」是两个独立的无障碍项。
            _feelPostFx = FindWorldPostFx();
            if (_feelPostFx == null)
            {
                GD.PushError("[feel-probe] 未找到 WorldPostFx 节点——重击泛光断言取不到判据");
                _feelProbe = false;
                return;
            }

            GameState.Instance.SetShakeScale(0.25);
            // 取值仍是生产配置里的最大档（生产无探针专用 API），保证强度足以触发泛光阈值
            GameState.Instance.Shake(GameState.Instance.Cfg("effects.shake.boss_seq_final", 24.0).AsDouble());
            if (_feelPostFx.HitPulse() <= 0.0f)
            {
                GD.PushError(GdFormat.Format(
                    "[feel-probe] 震动强度 0.25 下重击泛光未触发（HitPulse=%.3f）——"
                    + "震屏信号发的是折算值，滑杆把泛光一并关掉（两个无障碍项连坐）",
                    _feelPostFx.HitPulse()));
                _feelProbe = false;
                return;
            }

            GameState.Instance.RequestHitStop(Core.GameFeel.HitStopTier.Heavy);
            _feelStep = 1;
            return;
        }

        // 顿帧结束：确认已真正观察到冻结、trauma 已累加、时间缩放已复位
        if (_feelStep == 1)
        {
            if (!_feelSawHitStop)
            {
                GD.PushError("[feel-probe] 从未观测到顿帧激活（请求被吞或时序未推进）");
                _feelProbe = false;
                return;
            }

            if (!_feelSawTrauma)
            {
                GD.PushError("[feel-probe] 震动未累加 trauma（Shake 入口未接到手感域）");
                _feelProbe = false;
                return;
            }

            // 复位断言同样读引擎真值：自算值说「复位了」而引擎仍被压死，正是本探针要抓的定格
            if (!Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f))
            {
                GD.PushError($"[feel-probe] 顿帧已结束但引擎时间缩放未复位（Engine.TimeScale={(float)Engine.TimeScale:0.###}，画面将永久定格）");
                _feelProbe = false;
                return;
            }

            _feelEnrageScale = Mathf.Max((float)GameState.Instance.Cfg("boss.enrage.slow_scale", 0.24).AsDouble(), 0.01f);
            _feelStep = 2;
            return;
        }

        // 磁吸窗口·TS=1 基线：等 trauma 自行衰减至 0（衰减链路确实在跑）后测一次进窗口的量
        if (_feelStep == 2)
        {
            if (GameState.Instance.ShakeTrauma() > 1e-4)
            {
                return;
            }

            if (!TickMagnetProbe(ref _magnetValueTs1, Engine.TimeScale))
            {
                return;
            }

            _magnetState = 0;
            GameState.Instance.SetEnrageTimeScale(_feelEnrageScale); // 生产狂暴子弹时间（演出倍率）
            _feelStep = 3;
            return;
        }

        // 上报必须真落到引擎——否则下面的暂停断言会因为「倍率本来就是 1.0」而假绿
        if (_feelStep == 3)
        {
            if (!Mathf.IsEqualApprox((float)Engine.TimeScale, (float)_feelEnrageScale))
            {
                GD.PushError($"[feel-probe] 上报演出倍率后引擎时间缩放未压低（Engine.TimeScale={(float)Engine.TimeScale:0.###}，期望 {_feelEnrageScale:0.###}）");
                _feelProbe = false;
                return;
            }

            if (!TickMagnetProbe(ref _magnetValueTs24, Engine.TimeScale))
            {
                return;
            }

            var (min, full) = MagnetWindowBounds();
            if (Mathf.Abs(_magnetValueTs24 - _magnetValueTs1) > 0.01f)
            {
                GD.PushError(GdFormat.Format(
                    "[feel-probe] 同一手部位移在不同时间缩放下的进窗量不等（TS=1 → %.1f，TS=%.2f → %.1f）——"
                    + "鼠标路换算吃了缩放帧长，进窗口的量被放大 1/TimeScale 倍",
                    _magnetValueTs1, (float)_feelEnrageScale, _magnetValueTs24));
                _feelProbe = false;
                return;
            }

            if (_magnetValueTs24 < min || _magnetValueTs24 >= full)
            {
                GD.PushError(GdFormat.Format(
                    "[feel-probe] TS=%.2f 下进窗口的量 %.1f 越窗（窗口 [%.0f,%.0f)）——磁吸完全失效",
                    (float)_feelEnrageScale, _magnetValueTs24, min, full));
                _feelProbe = false;
                return;
            }

            GameState.Instance.SetTreePaused(true);
            _feelStep = 4;
            return;
        }

        // 暂停必须把冻结与演出倍率一并清掉：只清一半（顿帧清了、0.24 留着）时暂停页/设置页/
        // 天赋面板等 Always UI 会被拉到 24% 速度播放，玩家读作「菜单卡死」。同样读引擎真值。
        if (_feelStep == 4)
        {
            if (!Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f))
            {
                GD.PushError($"[feel-probe] 暂停未复位演出倍率，Always UI 将被慢放（Engine.TimeScale={(float)Engine.TimeScale:0.###}，期望 1）");
                _feelProbe = false;
                return;
            }

            GameState.Instance.SetTreePaused(false);
            GD.Print("[feel-probe] 顿帧与震动复位完成");
            _feelProbe = false;
        }
    }

    /// <summary>磁吸输入窗口的时间缩放不变性测量（一段一值）：先把注入手部位移落到基准点，再注入固定
    /// 像素位移，读 <see cref="Player.MagnetInputLastFrame"/>——**判据取自 Player 的实际换算出口**，
    /// 探针自己调 core 换算只测 core，接线坏了照绿。
    ///
    /// 为什么必须注入鼠标位移：headless 没有真实鼠标，视口鼠标位置恒定点（raw 增量恒 0），
    /// 磁吸窗口整段不被走到。注入经 <c>Input.ParseInputEvent</c>（视口鼠标位置的唯一可写路径：
    /// 事件位置按**窗口**坐标解释，故先经 <c>GetFinalTransform()</c> 从逻辑坐标换回窗口坐标）。
    /// 同一基准点 + 同一位移在两次测量里逐位相同，即「同一真实手速」。
    /// 返回 true 表示本段已测到值。</summary>
    private bool TickMagnetProbe(ref float slot, double timeScale)
    {
        var toWindow = GetViewport().GetFinalTransform();
        switch (_magnetState)
        {
            case 0:
                Input.ParseInputEvent(new InputEventMouseMotion
                {
                    Position = toWindow * _magnetBase,
                    GlobalPosition = toWindow * _magnetBase,
                });
                _magnetState = 1;
                return false;
            case 1:
                var target = _magnetBase + new Vector2(MagnetProbeDeltaPx, 0.0f);
                Input.ParseInputEvent(new InputEventMouseMotion
                {
                    Position = toWindow * target,
                    GlobalPosition = toWindow * target,
                });
                _magnetInjectFrame = _frame;
                _magnetState = 2;
                return false;
            default:
                if ((long)_player.MagnetInputFrame() < _magnetInjectFrame)
                {
                    // 换算未在本帧刷新：准星压在标记目标上时走的是粘滞分支（不换算窗口），
                    // 换一个基准点重来；连续失败即显式报错，不静默放过
                    _magnetAttempts++;
                    if (_magnetAttempts > MagnetProbeMaxAttempts)
                    {
                        GD.PushError(GdFormat.Format(
                            "[feel-probe] %d 次注入位移后仍未取到磁吸换算值（准星始终压在标记目标上？）"
                            + "——时间缩放不变性判据取不到", MagnetProbeMaxAttempts));
                        _feelProbe = false;
                        return false;
                    }

                    _magnetBase = MagnetProbeBase(_magnetAttempts);
                    _magnetState = 0;
                    return false;
                }

                slot = _player.MagnetInputLastFrame();
                _magnetState = 3;
                return true;
        }
    }

    /// <summary>磁吸测量的基准点候选（都落在可见域内，避开钳制）：逐个换点重试。</summary>
    private static Vector2 MagnetProbeBase(int attempt) => (attempt % 4) switch
    {
        0 => new Vector2(960.0f, 540.0f),
        1 => new Vector2(520.0f, 360.0f),
        2 => new Vector2(1400.0f, 360.0f),
        _ => new Vector2(960.0f, 820.0f),
    };

    /// <summary>磁吸窗口上下界（生产配置单源，与 Player/AimFrameLayer 读同一对键）。</summary>
    private static (float Min, float Full) MagnetWindowBounds()
    {
        var gs = GameState.Instance;
        var min = (float)gs.Cfg("player.aim_assist.input.magnet_input_min", 2.0).AsDouble();
        var full = (float)gs.Cfg("player.aim_assist.input.magnet_input_full", 40.0).AsDouble();
        return (min, full);
    }

    /// <summary>WorldPostFx 实例（Main 运行时创建，按型扫子节点——节点名不参与判定）。</summary>
    private WorldPostFx? FindWorldPostFx()
    {
        foreach (var child in _main.GetChildren())
        {
            if (child is WorldPostFx fx)
            {
                return fx;
            }
        }

        return null;
    }

    /// <summary>燃料量槽探针：把液位从满油扫到见底，逼 <c>FuelTank._Draw</c> 在每个液位各画一次
    /// （含每档掉液触发的晃动叠加＝最大波幅）。无头局玩家不操作不掉油，该绘制路径平时根本走不到。
    /// 判定靠冒烟错误正则捕获「Invalid polygon data」——自交/退化多边形整块不画且不崩，
    /// 只判「不崩」抓不到（低油量燃料槽整块消失即此类）。</summary>
    private void TickFuelProbe()
    {
        // 21 档 × 8 帧（> HUD 0.1s 轮询 + 液罐追赶/晃动），扫满一整段液位行程
        if (_frame % 8 != 0)
        {
            return;
        }

        _player.SetFuel(_player.FuelMax * (1.0f - _fuelStep / 20.0f));
        _fuelStep++;
        if (_fuelStep > 20)
        {
            GD.Print("[fuel-probe] 液位满扫完成");
            _fuelProbe = false;
        }
    }

    /// <summary>设置页探针：开页并逐页切过——五页内容都在 ShowSettings 之后才构建，
    /// 平时的 300 帧基线碰不到它们（玩家点开即崩的写法在这里暴露）。
    /// 找不到设置节点就不打完成标记——空转同样「零错误退出」，缺标记即红。
    ///
    /// 另断两处「减少闪光」门控（借开页顺带覆盖，不额外起趟）：轮盘开机物化的全息频闪
    /// （Modulate 按 0.028s 步进在 0.45/0.95 之间跳）与危险横幅的明暗闪烁循环。两者都是
    /// 「开关认了、两处频闪没认」的静默残留，且**两半互补**：必须先在没有减少闪光时观测到
    /// 闪烁（否则「根本没显示」会让减闪段退化成空转绿），再在减少闪光下断恒定全亮。</summary>
    private void TickSettingsProbe()
    {
        var settings = GetTree().GetFirstNodeInGroup("settings_ui") as SettingsUi;
        if (settings == null)
        {
            GD.PushError("[settings-probe] 未找到设置页节点，无法覆盖五页构建");
            _settingsProbe = false;
            return;
        }

        if (_settingsProbeStep == 0)
        {
            _settingsWheel = FindWheel(settings);
            if (_settingsWheel == null)
            {
                GD.PushError("[settings-probe] 未找到轮盘节点——开机频闪的减少闪光门控断言取不到判据");
                _settingsProbe = false;
                return;
            }

            GameState.Instance.SetReduceFlash(false);
            settings.ShowSettings(null); // 每次开页都重放 PlayBoot
            _settingsProbeStep = 1;
            _settingsProbeFrame = 0;
            _settingsProbeSawBlink = false;
            return;
        }

        if (_settingsProbeStep == 1)
        {
            if (_settingsWheel!.Modulate.A < 0.99f)
            {
                _settingsProbeSawBlink = true;
            }

            if (++_settingsProbeFrame < SettingsFlashWindowFrames)
            {
                return;
            }

            if (!_settingsProbeSawBlink)
            {
                GD.PushError($"[settings-probe] 无减少闪光的 {SettingsFlashWindowFrames} 帧内未观测到轮盘开机频闪"
                    + "——开机物化未触发，减闪段会退化成空转绿");
                _settingsProbe = false;
                return;
            }

            GameState.Instance.SetReduceFlash(true);
            settings.ShowSettings(null);
            _settingsProbeStep = 2;
            _settingsProbeFrame = 0;
            return;
        }

        if (_settingsProbeStep == 2)
        {
            if (_settingsWheel!.Modulate.A < 0.99f)
            {
                GD.PushError($"[settings-probe] 减少闪光下轮盘开机仍在频闪（alpha={_settingsWheel.Modulate.A:0.##}）"
                    + "——Modulate 按 0.028s 步进跳变的明暗未停用");
                _settingsProbe = false;
                return;
            }

            if (++_settingsProbeFrame < SettingsFlashWindowFrames)
            {
                return;
            }

            var hud = GetTree().GetFirstNodeInGroup("hud") as Hud;
            if (hud == null)
            {
                GD.PushError("[settings-probe] 未找到 HUD 节点——危险横幅的减少闪光门控断言取不到判据");
                _settingsProbe = false;
                return;
            }

            GameState.Instance.SetReduceFlash(false);
            hud.ShowWarning(Tr("WARN_BOSS"));
            _settingsProbeStep = 3;
            _settingsProbeFrame = 0;
            _settingsProbeSawBlink = false;
            return;
        }

        if (_settingsProbeStep == 3)
        {
            var hud = GetTree().GetFirstNodeInGroup("hud") as Hud;
            if (hud == null)
            {
                GD.PushError("[settings-probe] 未找到 HUD 节点——危险横幅的减少闪光门控断言取不到判据");
                _settingsProbe = false;
                return;
            }

            if (hud.WarningBannerAlpha < 0.99f)
            {
                _settingsProbeSawBlink = true;
            }

            if (++_settingsProbeFrame < SettingsBannerWindowFrames)
            {
                return;
            }

            if (!_settingsProbeSawBlink)
            {
                GD.PushError($"[settings-probe] 无减少闪光的 {SettingsBannerWindowFrames} 帧内未观测到危险横幅明暗循环"
                    + "——横幅未显示，减闪段会退化成空转绿");
                _settingsProbe = false;
                return;
            }

            GameState.Instance.SetReduceFlash(true);
            hud.ShowWarning(Tr("WARN_BOSS"));
            _settingsProbeStep = 4;
            _settingsProbeFrame = 0;
            return;
        }

        if (_settingsProbeStep == 4)
        {
            var hud = GetTree().GetFirstNodeInGroup("hud") as Hud;
            if (hud == null)
            {
                GD.PushError("[settings-probe] 未找到 HUD 节点——危险横幅的减少闪光门控断言取不到判据");
                _settingsProbe = false;
                return;
            }

            if (hud.WarningBannerAlpha < 0.99f)
            {
                GD.PushError($"[settings-probe] 减少闪光下危险横幅仍在明暗循环（alpha={hud.WarningBannerAlpha:0.##}）"
                    + "——频闪未停用（读数与淡出应保留、只停闪烁）");
                _settingsProbe = false;
                return;
            }

            if (++_settingsProbeFrame < SettingsBannerWindowFrames)
            {
                return;
            }

            GameState.Instance.SetReduceFlash(false);
            settings.ShowSettings(null);
            foreach (var page in new[] { "gameplay", "display", "audio", "about", "controls" })
            {
                settings.ShowPage(new StringName(page));
            }

            GD.Print("[settings-probe] 五页切换完成");
            _settingsProbe = false;
        }
    }

    /// <summary>按型递归找轮盘节点（<c>SettingsUi.Wheel</c> 是 protected，探针不为观测再开一个写口）。</summary>
    private static RadialWheel? FindWheel(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is RadialWheel wheel)
            {
                return wheel;
            }

            var found = FindWheel(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>迷雾探针驱动：在宿主里请求一次**强制指定**的迷雾事件（fake_enemies，无伤害/无碰撞，
    /// 不扰动玩家血量/输入/子弹参数），等该 id 的完整周期（EventStarted → EventEnded）跑完再打标记。
    ///
    /// 为什么不能只靠自动触发：迷雾走「首延迟 25s + 每 3s 掷 35%」的随机链，常规冒烟趟跑不到，
    /// 而迷雾注册/context 构建/生命周期/效果清理是高密度出错区。
    /// **仍走生产门控**：强制入口只替换掷签与权重选取，TryTriggerGroup 内先过 CanTriggerGroup
    /// （接线/启用/本局活跃/组内无进行中/首延迟到点/冷却到点）——门控断线时本探针一并发红。
    /// 帧数只由 --fixed-fps 60 驱动，不等真实时间；截断（提前 EndActive）不满足完整周期，不打标记。</summary>
    private void TickFogProbe()
    {
        // 前 30 帧等入场与稳定（入场窗口内玩家不可驱动、生产时钟未起）
        if (_frame < 30 || _player.IsEntryPlaying() || !_spawner.IsProcessing())
        {
            return;
        }

        _player.SetInvincible(ProbeInvincibleSeconds); // 无头局玩家不操作，与存活解耦
        // 迷雾趟暂停波次：无弹幕即无擦弹/击杀分，于是「分数增量」只可能来自迷雾存活补偿本身
        // （否则擦弹的随机得分会污染判据，把「补偿没发」淹没在噪声里）。
        _spawner.SetWavesPaused(true);

        if (!_fogActive)
        {
            // 门控未到点（首延迟/冷却）时 TryTriggerGroup 返回 false——下帧再试，不绕过
            _events.TryTriggerGroup(GameEventManager.GroupFog);
            return;
        }

        // 打断趟：迷雾跑满 1.0s（远低于生产 duration 6~8s）后走生产返航/死亡同一条 API 打断，
        // 断「存活补偿不发」。只判自然到期那侧会让「一律发」的实现照样绿，故必须成对。
        if (_fogInterruptProbe && !_fogInterruptCut && (_frame - _fogStartFrame) >= 60)
        {
            _fogInterruptCut = true;
            _fogScoreAtInterrupt = GameState.Instance.Score;
            GameState.Instance.FogEvents.EndActive();
        }
    }

    /// <summary>增幅缓存连接态探针：长跑到敌机池发生复用（同一实例离场回收后被下一波重新取出），
    /// 断言每个活跃敌机的 slow_field 缓存仍接在 AugmentsChanged 上。坏法静默——池化复用的
    /// reparent 会触发 `_ExitTree`，连/断错序时该敌机整个活跃期不再随加点刷新（「买了力场没感觉」），
    /// 不崩不报错。**未观察到复用则不打标记**（没走到复用路径等于没覆盖这个坏点），
    /// 且识别到复用后**再多判几帧**——回挂发生在帧末，断开要下一帧才可见。</summary>
    private void TickAugmentCacheProbe()
    {
        if (_frame < 30 || _player.IsEntryPlaying())
        {
            return;
        }

        var live = new HashSet<ulong>();
        foreach (var node in GameState.Instance.Enemies)
        {
            if (node is not Enemy enemy || !GodotObject.IsInstanceValid(enemy) || !enemy.IsActive())
            {
                continue;
            }

            live.Add(enemy.GetInstanceId());
            if (!enemy.IsAugmentCacheConnected())
            {
                _augmentCacheProbe = false;
                GD.PushError("[augment-cache-probe] 活跃敌机的 slow_field 缓存未连接——"
                    + "该敌机不随 AugmentsChanged 刷新（加点后力场对其无效，且无任何报错）");
                return;
            }
        }

        // 上一帧在场、本帧不在 ⇒ 已被回收；回收过的实例再露面即池化复用
        foreach (var id in _augmentCachePrevLive)
        {
            if (!live.Contains(id))
            {
                _augmentCacheRetired.Add(id);
            }
        }

        if (_augmentCacheReuseFrame < 0)
        {
            foreach (var id in live)
            {
                if (_augmentCacheRetired.Contains(id))
                {
                    _augmentCacheReuseFrame = _frame;
                    break;
                }
            }
        }

        _augmentCachePrevLive = live;

        // 复用发生后留 3 帧让帧末回挂与随后的断开（若有）落定，再判最终态
        if (_augmentCacheReuseFrame >= 0 && _frame >= _augmentCacheReuseFrame + 3)
        {
            _augmentCacheProbe = false;
            GD.Print("[augment-cache-probe] 池化复用后缓存连接态成立");
        }
    }

    /// <summary>迷雾事件开始：只认强制指定的 id，记下起始帧与生产下发的 duration
    /// （完整周期判定用；duration 由管理器从 balance 读出，探针不复刻配置）。</summary>
    private void OnFogEventStarted(StringName eventId, float duration)
    {
        if (!_fogProbe && !_fogInterruptProbe)
        {
            return;
        }

        if (eventId != FogProbeEventId)
        {
            GD.PushError(GdFormat.Format("[fog-probe] 启动了非指定迷雾事件 %s（强制入口应只启动 %s）",
                eventId, FogProbeEventId));
            _fogProbe = false;
            _fogInterruptProbe = false;
            return;
        }

        _fogActive = true;
        _fogStartFrame = _frame;
        _fogDuration = duration;
        _fogScoreAtStart = GameState.Instance.Score; // 自然到期的补偿入账判定基线
        _fogInterruptCut = false;
    }

    /// <summary>迷雾事件结束：自然到期趟（--fog-probe）判「跑满完整周期 + 存活补偿已入账」；
    /// 打断趟（--fog-interrupt-probe）判「打断后存活补偿不发放」。两者互补——只判一侧的话，
    /// 「一律发」或「一律不发」的坏实现都能混过。</summary>
    private void OnFogEventEnded(StringName eventId)
    {
        if ((!_fogProbe && !_fogInterruptProbe) || !_fogActive || eventId != FogProbeEventId)
        {
            return;
        }

        _fogActive = false;
        var elapsed = (_frame - _fogStartFrame) / 60.0;

        if (_fogInterruptProbe)
        {
            _fogInterruptProbe = false;
            var delta = GameState.Instance.Score - _fogScoreAtInterrupt;
            if (delta != 0)
            {
                GD.PushError(GdFormat.Format(
                    "[fog-interrupt-probe] 打断后仍发放存活补偿（分数增量 %d，期望 0）——"
                    + "「存活」补偿不该在没扛满整段时兑现", delta));
                return;
            }

            GD.Print("[fog-interrupt-probe] 打断不发存活补偿");
            return;
        }

        if (elapsed + FogCycleToleranceSeconds < _fogDuration)
        {
            GD.PushError(GdFormat.Format(
                "[fog-probe] %s 周期被截断（实际 %.2fs < 生产 duration %.2fs）", eventId, elapsed, _fogDuration));
            _fogProbe = false;
            return;
        }

        // 自然到期必须发奖，且金额等于生产口径（AddScore 里统一乘难度档倍率）：波次已暂停，
        // 分数增量只可能来自这一笔。不判这条的话，把奖励整段删掉（只留效果清理）也会绿——
        // 而那正是「迷雾成为纯负反馈」的原始缺陷形态。
        var expected = (int)System.Math.Round(
            _events.FOG_REWARD_SCORE * GameState.Instance.KillScoreFactor()) * GameState.Instance.ScoreMultiplier();
        var reward = GameState.Instance.Score - _fogScoreAtStart;
        if (reward != expected)
        {
            GD.PushError(GdFormat.Format(
                "[fog-probe] 自然到期的存活补偿不符（分数增量 %d，期望 %d＝reward_score×难度奖励因子×难度档倍率）"
                + "——迷雾将退回纯负反馈", reward, expected));
            _fogProbe = false;
            return;
        }

        GD.Print("[fog-probe] 迷雾全周期完成");
        _fogProbe = false;
    }

    /// <summary>返航宽限探针：走生产蓄力链触发返航（长按 homecoming 蓄满，不直调过场、不绕过输入判定），
    /// 再分三段确定性地覆盖输入宽限与跳过收尾。
    ///
    /// 为什么不能照搬旧测试「等 1.4s 真实时间越宽限」：`--fixed-fps 60` 下引擎不等真实时间，帧跑得远快于
    /// 墙钟（71e6324 记载过由此导致的静默跳过失效），等真实时间的写法在帧预算内既慢又不可靠。宽限本身
    /// 用真实时间是对的（见 DESIGN_BASELINE §2.10），故本探针**不靠等待**，改用两个确定性判据：
    ///   1) 开播即调跳过 → 必须被忽略（宽限存在）；
    ///   2) 推进 ReturnProbeGraceWindowFrames（90 帧 = 1.5 模拟秒 &gt; 生产宽限 1.2s）后仍必须被忽略——
    ///      若宽限被误改成模拟时间，此刻 1.5s &gt; 1.2s 会放行，判据即红。真实时间下这段墙钟远未越界，
    ///      探针另测墙钟做前置守卫（环境过慢则显式报错，绝不静默放过）。
    ///   3) 把宽限置 0（确定越过边界）后跳过必须生效，且收尾落在基地、树保持暂停。
    /// 段 2 的「仍被忽略」是真实时间基准的判别式；「是否用真实时间 API」另由 check_realtime_allowlist.sh 兜住。</summary>
    private void TickReturnProbe()
    {
        // 触发前的等待/蓄力阶段：等入场结束、spawner 可处理（生产蓄力链的前置）。
        // 注意这一段守卫**只**管「尚未开播的 stage 0」——过场一开播 Main 就 SetProcess(false) 停掉
        // spawner，若把 `!IsProcessing()` 继续套用会永远卡在 stage 0（实测 proc 会变 false）。
        if (_returnStage == 0 && !_main.IsReturnPlaying()
            && (_frame < 30 || _player.IsEntryPlaying() || !_spawner.IsProcessing()))
        {
            return;
        }

        _player.SetInvincible(ProbeInvincibleSeconds); // 无头局玩家不操作，与存活解耦

        if (_returnStage == 0)
        {
            if (_main.IsReturnPlaying())
            {
                Input.ActionRelease(ActHomecoming);
                var ret = _main.ReturnCinematic();
                if (ret == null)
                {
                    ReturnProbeFail("返航过场已开始但引用为空");
                    return;
                }

                _returnStartFrame = _frame;
                _returnWallStart = Time.GetTicksMsec();
                _returnGrace = ret.SKIP_GRACE; // 生产宽限值（判别与诊断用）
                _returnStage = 1;
                return;
            }

            _returnChargeFrames++;
            if (_returnChargeFrames > ReturnProbeChargeFrames)
            {
                Input.ActionRelease(ActHomecoming);
                ReturnProbeFail(GdFormat.Format("长按返航蓄力 %d 帧仍未触发过场", ReturnProbeChargeFrames));
                return;
            }

            Input.ActionPress(ActHomecoming); // 生产蓄力链：Main._Process 读同一动作
            return;
        }

        var cinematic = _main.ReturnCinematic();
        if (cinematic == null)
        {
            ReturnProbeFail("返航过场在收尾前意外消失");
            return;
        }

        if (_returnStage == 1)
        {
            _main.SkipReturn();
            if (!_main.IsReturnPlaying())
            {
                ReturnProbeFail("宽限期内跳过未被忽略（过场已销毁）");
                return;
            }

            _returnStage = 2;
            return;
        }

        if (_returnStage == 2)
        {
            if (_frame - _returnStartFrame < ReturnProbeGraceWindowFrames)
            {
                return;
            }

            var wallMs = Time.GetTicksMsec() - _returnWallStart;
            var graceMs = (ulong)(_returnGrace * 1000.0f);
            if (wallMs + ReturnProbeGraceMarginMs >= graceMs)
            {
                // 墙钟已追上宽限，本趟无法判别「真实时间 vs 模拟时间」——显式失败，不静默放过
                ReturnProbeFail(GdFormat.Format(
                    "环境过慢：%d 帧耗 %dms 已达生产宽限 %dms，无法判别时间基准",
                    ReturnProbeGraceWindowFrames, (long)wallMs, (long)graceMs));
                return;
            }

            _main.SkipReturn();
            if (!_main.IsReturnPlaying())
            {
                ReturnProbeFail(GdFormat.Format(
                    "推进 %d 帧后跳过被提前放行——宽限疑似按模拟时间计（应为真实时间）",
                    ReturnProbeGraceWindowFrames));
                return;
            }

            _returnStage = 3;
            return;
        }

        // 段 3：宽限置 0 确定越过边界 → 跳过必须生效，收尾落基地且树保持暂停
        cinematic.SKIP_GRACE = 0.0f;
        _main.SkipReturn();
        if (_main.IsReturnPlaying())
        {
            ReturnProbeFail("越过宽限后跳过未生效");
            return;
        }

        var baseUi = _main.GetNodeOrNull<BaseConsole>("BaseUI");
        if (baseUi == null || !baseUi.Visible)
        {
            ReturnProbeFail("跳过收尾未显示基地 UI");
            return;
        }

        if (!GetTree().Paused)
        {
            ReturnProbeFail("跳过收尾树未保持暂停（基地界面应为暂停态）");
            return;
        }

        GD.Print("[return-probe] 返航宽限与跳过收尾完成");
        _returnProbe = false;
    }

    /// <summary>返航宽限探针失败：报错并停探针（门禁按缺完成标记判红）。</summary>
    private void ReturnProbeFail(string reason)
    {
        GD.PushError("[return-probe] " + reason);
        _returnProbe = false;
    }

    /// <summary>Boss 阶段机探针：走生产触发链（补分数越过 <c>boss_score_step</c> 且等过
    /// <c>boss_min_interval</c>）请出 Boss，然后在真实受击链上逐段推进并断言。
    ///
    /// 为什么必须探这一趟：Boss 的阶段机（P1→P2→狂暴→击杀）在引擎侧此前零自动覆盖，
    /// 写坏的表现是「不崩、不报错、只是没那一段」——转场未发生（少一次清弹与喘息）、
    /// 锁血不解（Boss 无敌但画面只是打得久）、击杀没接轮换（下一只又是同一型）。
    /// 判定分五条，缺一条即不打完成标记（门禁按缺标记判红）：
    ///   ① 出场经生产门控：补分数到 <c>boss_score_step</c> 之上后等生产链自己触发，
    ///      并在超时前收到 BossSpawned——探针不直调 TriggerBoss/SpawnBoss；
    ///   ② 血量单调不增：逐帧采样 Boss.Hp，任何一次上抬即红（BossPhaseGate 不变量的真实链验证）；
    ///   ③ P1→P2 在真实受击链上发生（越线注入后 FightPhaseValue 当场就是 P2，
    ///      且玩家无敌被抬起——转场公平感清理不生效时玩家会在残留弹里被打死）；
    ///   ④ ENRAGE 在越过狂暴线后当场进入（IsEnraged + FightPhaseValue == ENRAGE）；
    ///   ⑤ 击杀走 Die()：致死一击经 TakeDamage 判定，随后 BossKills +1、生成器 Boss 槽复位、
    ///      实例释放（＝轮换已推进，下一只换型）。
    /// 另断狂暴锁血会在序列里自行解除（锁血残留的表现是 Boss 永久无敌，不崩不报错），
    /// 以及 P2 转场后一帧内活跃敌弹被清空（TransitionCleanup 的 QueueFree 在帧末出树）。
    ///
    /// 逃跑不在此趟：它只由存活计时触发且计满 <c>boss.escape.time</c>（实测 50s 模拟时长），
    /// 装进冒烟会吃掉一半时间预算（AGENTS §6 铁律 4）。轮换与「逃跑是否推进/是否休整」的判定
    /// 已下沉 core <see cref="Core.Combat.BossRotation"/> 并有单测钉住，引擎侧只做薄接线。</summary>
    private void TickBossProbe()
    {
        // 入场阶段：等入场结束与 spawner 可处理（生产触发链的前置）；超时即失败，不空转
        if (!_bossTriggerPosted)
        {
            if (_frame > BossProbeEntryWaitFrames)
            {
                BossProbeFail(GdFormat.Format(
                    "入场 %d 帧仍未就绪（入场动画未结束或 spawner 未处理）", BossProbeEntryWaitFrames));
                return;
            }

            if (_player.IsEntryPlaying() || !_spawner.IsProcessing())
            {
                return;
            }

            _player.SetInvincible(ProbeInvincibleSeconds); // 无头局玩家不操作，与存活解耦
            _bossSubscribed = true;
            _spawner.BossSpawned += OnBossProbeSpawned; // 观测量：出场时记录，不驱动、不绕过门控
            _bossKillsBefore = GameState.Instance.BossKills;
            // 分数门：补到第一档之上（再留一档余量）。时间门由生产链自己走满 boss_min_interval，
            // 探针不缩短、不直调触发——触发点与真实本局同一条路径。
            var step = GameState.Instance.Cfg("spawner.boss_score_step", 1500).AsInt64();
            var need = System.Math.Max(step, 1) * 2;
            GameState.Instance.AddScore((int)System.Math.Max(need - GameState.Instance.Score, 0));
            _bossTriggerPosted = true;
            _bossTriggerFrame = _frame;
            return;
        }

        if (_boss == null)
        {
            // 生产链到点即触发（分数门 + 最小间隔）。超时说明触发链本身坏了——报错，缺标记即红
            if (_frame - _bossTriggerFrame > BossProbeSpawnTimeoutFrames)
            {
                BossProbeFail(GdFormat.Format(
                    "补足分数门后 %d 帧仍未请出 Boss（分数门/最小间隔/波次冻结断线？）",
                    BossProbeSpawnTimeoutFrames));
            }

            return;
        }

        var boss = _boss;
        if (!GodotObject.IsInstanceValid(boss))
        {
            // 实例释放只应发生在击杀注入之后：未到击杀阶段就没了即失败（非击杀离场路径）。
            // 击杀路径的收尾判定在此进行——QueueFree 后实例当帧即失效，下面的逐帧分支不再可达。
            if (!_bossKillInjected)
            {
                BossProbeFail("Boss 实例在击杀前释放（非击杀离场路径）");
                return;
            }

            if (!_bossOutcomeChecked && _frame - _bossLastTickFrame >= BossProbeKillSettleFrames)
            {
                _bossOutcomeChecked = true;
                BossProbeCheckKillOutcome();
            }

            return;
        }

        // ② 血量单调不增：逐帧采样（血量只由受击链推进，探针只读）
        if (boss.Hp > _bossPrevHp + 1e-3f)
        {
            _bossHpMonotonic = false;
        }

        _bossPrevHp = boss.Hp;

        // ③ 转场清弹：越线注入当帧记下清弹前的活跃敌弹数（清弹走 QueueFree，注册表在帧末出树时
        // 才移除），下一帧判「已清空」。前置 >0 让判据非空洞——否则「本来就没弹」也算清干净。
        if (_bossClearCheckFrame > 0 && _frame >= _bossClearCheckFrame)
        {
            _bossClearCheckFrame = 0;
            if (GameState.Instance.EnemyBullets.Count == 0)
            {
                _bossSawPhase2Clear = true;
            }
        }

        switch (_bossStage)
        {
            case 1:
                // 速度域：入场/逃跑的推进速度与慢速力场乘区都是「写坏成 0 即整局冻死」的静默面——
                // 入场速度归零则永不到战斗锚线、_bossActive 恒 true 把波次与后续 Boss 一起冻住，
                // 且不崩不报错。故在等入场完成的同时把域判掉，超时即显式失败（不空转到退出）。
                if (boss.EnterSpeed < 1.0f || boss.EscapeStartSpeed < 1.0f || boss.EscapeAccel < 1.0f
                    || boss.SlowFactor() < 0.05f)
                {
                    BossProbeFail($"Boss 速度域越界：enter={boss.EnterSpeed:0.###} escape_start={boss.EscapeStartSpeed:0.###}"
                        + $" escape_accel={boss.EscapeAccel:0.###} slow_factor={boss.SlowFactor():0.###}"
                        + "——入场速度归零时永不到锚线，波次与后续 Boss 会被一起冻死");
                    return;
                }

                // 等入场降入完成（生产判定：IsInFight）再开始注入伤害
                if (!boss.IsInFight())
                {
                    if (_frame - _bossLastSpawnFrame > BossProbeFightTimeoutFrames)
                    {
                        BossProbeFail($"出场后 {BossProbeFightTimeoutFrames} 帧仍未进入战斗（IsInFight 恒假）"
                            + "——入场推进断线，本局将被永久冻结");
                    }

                    return;
                }

                _bossStage = 2;
                _bossLastTickFrame = _frame;
                _bossPrevHp = boss.Hp;
                return;

            case 2:
                // 逃跑倒计时的减少闪光门控（借在场 Boss 断）：先断无减闪时确有明暗翻转（正对照，
                // 防「倒计时根本没显示」被当成「不闪」），再断减闪下 alpha 恒为 1。
                if (!TickBossCountdownProbe(boss))
                {
                    return;
                }

                // P1 段：先让生产链跑一段（出弹/开火），再把血量压到二阶段线之上
                if (_frame - _bossLastTickFrame < BossProbeApproachIntervalFrames)
                {
                    return;
                }

                _bossLastTickFrame = _frame;
                if (BossProbeApproachLine(boss, boss.Phase2HpRatio))
                {
                    return;
                }

                // 越线注入：走生产 TakeDamage → 生产链自己判 P2 与转场
                BossProbeCrossLine(boss, boss.Phase2HpRatio);
                if (boss.FightPhaseValue() != (int)Boss.FightPhase.P2)
                {
                    BossProbeFail("血量已越过二阶段线但 FightPhase 仍不是 P2（阶段门控断线）");
                    return;
                }

                // 转场当帧的公平感清理：无敌被抬起（只加不减）；清弹下帧判
                if (_player.InvincibleRemaining() <= 0.0f)
                {
                    BossProbeFail("P1→P2 转场未给玩家短暂无敌（转场清弹的喘息缺失）");
                    return;
                }

                _bossSawPhase2Invincible = true;
                _bossBulletsBeforeClear = GameState.Instance.EnemyBullets.Count;
                _bossSawPhase2BulletsBeforeClear |= _bossBulletsBeforeClear > 0;
                _bossClearCheckFrame = _frame + 1;
                _bossStage = 3;
                return;

            case 3:
                // P2 段：等一段让 P2 模式表开火，再压到狂暴线之上
                if (_frame - _bossLastTickFrame < BossProbeApproachIntervalFrames)
                {
                    return;
                }

                _bossLastTickFrame = _frame;
                if (BossProbeApproachLine(boss, boss.EnrageHpRatio))
                {
                    return;
                }

                BossProbeCrossLine(boss, boss.EnrageHpRatio);
                if (!boss.IsEnraged() || boss.FightPhaseValue() != (int)Boss.FightPhase.ENRAGE)
                {
                    BossProbeFail("血量已越过狂暴线但未进入 ENRAGE（狂暴门控/序列断线）");
                    return;
                }

                _bossStage = 4;
                _bossLastTickFrame = _frame;
                return;

            case 4:
                // 狂暴序列期间锁血：先等子弹时间走完（主场景按真实帧长推进并自行复位时间缩放），
                // 再等血锁在 RELEASE_HOLD 起点解除。锁血不解的表现是 Boss 永久无敌。
                if (boss.IsHealthLocked())
                {
                    if (_frame - _bossLastTickFrame > BossProbeLockTimeoutFrames)
                    {
                        BossProbeFail(GdFormat.Format(
                            "狂暴序列 %d 帧仍未解血锁（Boss 将永久无敌）", BossProbeLockTimeoutFrames));
                    }

                    return;
                }

                if (_frame - _bossLastTickFrame < BossProbeEnrageWaitFrames)
                {
                    return;
                }

                _bossStage = 5;
                _bossKillInjected = true;
                _bossLastTickFrame = _frame;
                BossProbeInjectKill(boss); // 致死一击走生产受击链 → Die()
                return;

            case 5:
                if (!_bossOutcomeChecked && _frame - _bossLastTickFrame >= BossProbeKillSettleFrames)
                {
                    _bossOutcomeChecked = true;
                    BossProbeCheckKillOutcome();
                }

                return;
        }
    }

    /// <summary>把血量压到阶段线**上方一点**（生产受击链入口，一次到位）。返回 true 表示本帧
    /// 只是接近、还没到线上方——调用方下个间隔再判越线。是否越线、是否转阶段全由 Boss 自己判。</summary>
    private static bool BossProbeApproachLine(Boss boss, float ratio)
    {
        var target = boss.MaxHp * (ratio + 0.02f);
        if (boss.Hp <= target)
        {
            return false;
        }

        boss.TakeDamage((int)System.Math.Ceiling(boss.Hp - target), 1.0f);
        return true;
    }

    /// <summary>越线一击：把血量打到阶段线**下方 2 个点**（一次穿过，再靠 MaxHp 的往返误差
    /// 兜住 ceil 取整）。跨线判定与转场全部由生产链完成——探针不碰 Phase/Enrage 本身。</summary>
    private static void BossProbeCrossLine(Boss boss, float ratio)
    {
        var target = boss.MaxHp * (ratio - 0.02f);
        var amount = boss.Hp - target;
        boss.TakeDamage((int)System.Math.Ceiling(amount > 0.0f ? amount : 1.0f), 1.0f);
    }

    /// <summary>击杀注入：致死一击走生产受击链（TakeDamage 判定 Hp&lt;=0 → Die），
    /// 不直调 Die（Die 是结算出口，绕过受击链就测不到阶段门与受击编排）。</summary>
    private void BossProbeInjectKill(Boss boss)
    {
        var amount = boss.Hp + 1.0f;
        boss.TakeDamage((int)System.Math.Ceiling(amount), 1.0f);
    }

    /// <summary>击杀后的收尾断言：轮换推进（BossKills+1）、生成器占用复位、实例已释放。
    /// 三件都成立才打完成标记——只判「Boss 没了」会让逃跑/清场路径照样绿。</summary>
    private void BossProbeCheckKillOutcome()
    {
        var ok = true;
        if (GameState.Instance.BossKills != _bossKillsBefore + 1)
        {
            GD.PushError(GdFormat.Format(
                "[boss-probe] 击杀未推进 Boss 击杀数（%d → %d，期望 %d）——轮换/难度/奖励都不会动",
                _bossKillsBefore, GameState.Instance.BossKills, _bossKillsBefore + 1));
            ok = false;
        }

        if (_boss != null && GodotObject.IsInstanceValid(_boss))
        {
            GD.PushError("[boss-probe] 击杀后 Boss 实例仍在场（Die 未走 QueueFree）");
            ok = false;
        }

        if (_spawner.IsBossActive())
        {
            GD.PushError("[boss-probe] 击杀后生成器仍占用 Boss 槽——后续 Boss 永不再出");
            ok = false;
        }

        if (!_bossHpMonotonic)
        {
            GD.PushError("[boss-probe] 受击链上血量出现上抬——血条会可见回跳，且致死一击可能绕过转场");
            ok = false;
        }

        if (!_bossSawPhase2BulletsBeforeClear)
        {
            GD.PushError("[boss-probe] P1→P2 越线时场上没有活跃敌弹——清弹判据空洞（Boss 未开火？）");
            ok = false;
        }

        if (!_bossSawPhase2Clear)
        {
            GD.PushError("[boss-probe] 未观测到 P1→P2 转场的清弹（TransitionCleanup 未生效或转场根本没发生）");
            ok = false;
        }

        if (!_bossSawPhase2Invincible)
        {
            GD.PushError("[boss-probe] 未观测到 P1→P2 转场给玩家的短暂无敌（玩家会在转场里被残留弹打死）");
            ok = false;
        }

        if (!ok)
        {
            _bossProbe = false;
            return;
        }

        GD.Print(GdFormat.Format(
            "[boss-probe] 阶段机全周期完成（P1→P2→狂暴→击杀，BossKills=%d）", GameState.Instance.BossKills));
        _bossProbe = false;
    }

    /// <summary>Boss 出场观测（生产 BossSpawned 信号）：只记录实例与实例身份校验，不驱动生成。</summary>
    private void OnBossProbeSpawned(Boss boss)
    {
        if (!_bossProbe || _boss != null)
        {
            return;
        }

        if (_bossLastSpawnFrame > 0 && _frame - _bossLastSpawnFrame < 60)
        {
            GD.PushError("[boss-probe] 短时间内重复出场（Boss 槽/最小间隔门疑似失效）");
            _bossProbe = false;
            return;
        }

        _boss = boss;
        _bossLastSpawnFrame = _frame;
        _bossStage = 1;
        _bossPrevHp = boss.Hp;
        _bossLastTickFrame = _frame;
    }

    /// <summary>Boss 探针失败：报错并停探针（门禁按缺完成标记判红）。</summary>
    private void BossProbeFail(string reason)
    {
        GD.PushError("[boss-probe] " + reason);
        _bossProbe = false;
    }

    /// <summary>逃跑倒计时的减少闪光门控断言（借在场 Boss）：返回 true = 本段已完成。
    ///
    /// 两半互补，缺一条即假绿：**无减闪时必须有明暗翻转**（正对照——倒计时根本没显示时，
    /// 「减闪下恒定全亮」会被空转蒙过）、**减闪下 alpha 必须恒为 1**（频闪停用、读数保留）。
    /// 判据读 HUD 的只读口，不碰倒计时实现。倒计时只在「战斗期 + 剩余 ≤ countdown_visible_from」
    /// 显示，探针把该阈值临时放宽到整段存活时长（公开属性，探针侧观测便利，不改生产判定）。</summary>
    private bool TickBossCountdownProbe(Boss boss)
    {
        if (_bossCountdownDone)
        {
            return true;
        }

        if (_bossHud == null)
        {
            _bossHud = GetTree().GetFirstNodeInGroup("hud") as Hud;
            if (_bossHud == null)
            {
                BossProbeFail("未找到 HUD 节点——逃跑倒计时的减少闪光门控断言取不到判据");
                return false;
            }
        }

        switch (_bossCountdownStep)
        {
            case 0:
                boss.EscapeCountdownFrom = boss.EscapeTime;
                GameState.Instance.SetReduceFlash(false);
                _bossCountdownStep = 1;
                _bossCountdownFrame = _frame;
                _bossCountdownSawFlip = false;
                return false;
            case 1:
                if (_bossHud.BossCountdownModulate.A < 0.99f)
                {
                    _bossCountdownSawFlip = true;
                }

                if (_frame - _bossCountdownFrame < BossCountdownWindowFrames)
                {
                    return false;
                }

                if (!_bossCountdownSawFlip)
                {
                    BossProbeFail($"无减闪的 {BossCountdownWindowFrames} 帧内未观测到逃跑倒计时的明暗翻转"
                        + "——倒计时未显示或翻转停用，减闪段会退化成空转绿");
                    return false;
                }

                GameState.Instance.SetReduceFlash(true);
                _bossCountdownStep = 2;
                _bossCountdownFrame = _frame;
                return false;
            default:
                if (_bossHud.BossCountdownModulate.A < 0.99f)
                {
                    BossProbeFail($"减少闪光下逃跑倒计时仍在明暗翻转（第 {_frame} 帧 "
                        + $"alpha={_bossHud.BossCountdownModulate.A:0.##}）——频闪未停用");
                    return false;
                }

                if (_frame - _bossCountdownFrame < BossCountdownWindowFrames)
                {
                    return false;
                }

                GameState.Instance.SetReduceFlash(false);
                _bossCountdownDone = true;
                return true;
        }
    }

    /// <summary>母舰坞态探针：走生产蓄力链（长按 <c>dock</c> 蓄满，不直调召唤或坞态方法）请出母舰，
    /// 断 DESCEND → DOCKING → RESUPPLY → STAY → RELEASE → DEPART 六个状态按序到达，
    /// 并在 STAY 长按 <c>dock</c> 走**提前离舰**收尾（生产 early_hold_time 蓄力链 + 冷却折扣/预填）。
    ///
    /// 为什么必须探：坞态机在引擎侧此前零覆盖，写坏的表现是「卡死」——某个状态不再推进到下一个，
    /// 玩家只能看到母舰悬停不动（不崩、不报错、日志干净）。判定 = 六个状态按序到达 + RELEASE 前
    /// 玩家已在保护舱（进舱链生效）+ 离场信号真的给出冷却 + 母舰实例最终被释放。
    /// 帧序只由 <c>--fixed-fps 60</c> 驱动，不看墙钟（AGENTS §5）。
    ///
    /// 收尾只覆盖提前离舰这一条：强制离舰（弹匣警告到期）要先把 10 格弹匣耗到 4 格（12 模拟秒）
    /// 再等警告横幅 5 秒，且要走完 26~36 秒的坞冷却才能二次召唤——两条都要跑会吃掉小半时间预算
    /// （AGENTS §6 铁律 4）。两条收尾共用同一段 StartReleaseInternal，故共享代码已被本趟执行；
    /// 差异只在触发源（见报告「装不下的部分」）。</summary>
    private void TickDockProbe()
    {
        if (_dockStage == 0)
        {
            if (_frame < 30 || _player.IsEntryPlaying() || !_spawner.IsProcessing())
            {
                return;
            }

            _player.SetInvincible(ProbeInvincibleSeconds);
            if (_main.Mothership() != null)
            {
                DockProbeFail("本趟开局即有母舰在场（用户目录未隔离或残留状态）");
                return;
            }

            _dockStage = 1;
            _dockStateFrame = _frame;
            _dockReached = -1;
            return;
        }

        TickLockResidualProbe();

        // 蓄力段：长按 dock 直到生产链自己召唤（Main._Process 读同一动作；资格判定全在生产侧）
        if (_dockStage == 1)
        {
            _dockChargeFrames++;
            if (_dockChargeFrames > DockProbeChargeFrames)
            {
                Input.ActionRelease(ActDock);
                DockProbeFail(GdFormat.Format("长按坞蓄力 %d 帧仍未触发召唤（蓄力链断线）", DockProbeChargeFrames));
                return;
            }

            TriggerLockResidualInputs();
            Input.ActionPress(ActDock);
            if (_main.Mothership() != null)
            {
                Input.ActionRelease(ActDock);
                _dockStage = 2; // 母舰已入场：从 DESCEND 开始逐状态判
                _dockStateFrame = _frame;
                _dockReached = -1;
                _dockMagCellsStart = _main.Mothership()!.GetMagCells();
            }

            return;
        }

        // 离场收尾：母舰出界后 QueueFree，Main 侧引用清空 + 冷却入账（OnMothershipDepartedInternal）
        if (_dockStage == 8)
        {
            if (_main.Mothership() != null)
            {
                if (_frame - _dockStateFrame > DockProbeStageTimeoutFrames)
                {
                    DockProbeFail(GdFormat.Format("母舰离开 %d 帧后仍未释放/Main 引用未清",
                        DockProbeStageTimeoutFrames));
                }

                return;
            }

            if (_dockReached < (int)Mothership.State.DEPART)
            {
                DockProbeFail("母舰已释放但 DEPART 状态未被观测到（离场段被整段跳过）");
                return;
            }

            if (_dockCooldownSeen <= 0.0f)
            {
                DockProbeFail("离场未给出坞冷却（Depart 信号未接上，可无限连召）");
                return;
            }

            if (!_dockSawPod)
            {
                DockProbeFail("未观测到 RELEASE 前玩家进入保护舱（对接段编排断线）");
                return;
            }

            if (!_dockSawMagConsume)
            {
                DockProbeFail("未观测到 STAY 驻留期弹匣消耗（驻留逐帧驱动断线，玩家会永久驻留）");
                return;
            }

            GD.Print(GdFormat.Format(
                "[dock-probe] 坞态全周期完成（六态按序 + 提前离舰，冷却 %.1fs）", _dockCooldownSeen));
            _dockProbe = false;
            return;
        }

        var ms = _main.Mothership();
        // 离场冷却读数逐帧采样（Depart 信号 → Main.OnMothershipDepartedInternal 写入）：
        // 母舰自身出界后 QueueFree，故冷却必须在引用还在的帧里采到
        _dockCooldownSeen = System.Math.Max(_dockCooldownSeen, _main.DockCooldown());
        if (ms == null || !GodotObject.IsInstanceValid(ms))
        {
            if (_dockReached < (int)Mothership.State.DEPART)
            {
                DockProbeFail(GdFormat.Format("坞态推进到 %d 时母舰意外消失（提前离场）", _dockReached));
                return;
            }

            // 跑完 DEPART 后母舰出界自释放：进入收尾判定段（冷却/保护舱/弹匣三断）
            _dockStage = 8;
            _dockStateFrame = _frame;
            return;
        }

        var state = ms.GetState();
        var idx = (int)state;
        if (idx < _dockReached)
        {
            DockProbeFail(GdFormat.Format("坞态回退：%d → %d（状态机倒序推进）", _dockReached, idx));
            return;
        }

        if (idx > _dockReached)
        {
            // 状态只允许逐级前进（枚举序数即状态机顺序），跳跃意味着某段演出被整段跳过
            if (idx != _dockReached + 1)
            {
                DockProbeFail(GdFormat.Format("坞态跳跃：%d → %d（中间状态被跳过）", _dockReached, idx));
                return;
            }

            _dockReached = idx;
            _dockStateFrame = _frame;
        }

        if (!_dockSawMagConsume && _dockReached >= (int)Mothership.State.STAY && ms.GetMagCells() < _dockMagCellsStart)
        {
            // 弹匣真的在耗（STAY 驻留推进）；只判「状态到了 STAY」会让停在原地不耗弹的坏法假绿
            _dockSawMagConsume = true;
        }

        if (!_dockSawPod && !_player.Visible)
        {
            _dockSawPod = true; // 进保护舱：隐藏 + 关受击判定（DOCKING 完成时置位）
        }

        switch (state)
        {
            case Mothership.State.DESCEND:
            case Mothership.State.DOCKING:
            case Mothership.State.RESUPPLY:
                if (_frame - _dockStateFrame > DockProbeStageTimeoutFrames)
                {
                    DockProbeFail(GdFormat.Format("坞态 %s 停滞 %d 帧未推进（状态机卡死）",
                        state, DockProbeStageTimeoutFrames));
                }

                return;

            case Mothership.State.STAY:
                // STAY 前半段不操作：让弹匣自然耗到警告档（生产 mag_warn_cells），断言警告真的亮起
                // ——「驻留无限期」的坏法表现是弹匣不耗、警告不亮，玩家可以永久驻留白拿火力掩护。
                if (!_dockSawMagWarn)
                {
                    if (ms.MagWarned() && ms.WarnEjectTimer() > 0.0f)
                    {
                        _dockSawMagWarn = true;
                        _dockStateFrame = _frame;
                        return;
                    }

                    if (_frame - _dockStateFrame > DockProbeMagWarnTimeoutFrames)
                    {
                        DockProbeFail(GdFormat.Format(
                            "STAY 驻留 %d 帧仍未触弹匣警告（弹匣不耗/警告不亮，玩家可永久驻留）",
                            DockProbeMagWarnTimeoutFrames));
                    }

                    return;
                }

                // 警告横幅倒计时里长按提前离舰（生产 early_hold_time 蓄力链）——提前离舰优先于
                // 警告到期的强制离舰（同段 StartReleaseInternal，折扣/预填按剩余弹匣结算）
                if (_frame - _dockStateFrame > DockProbeEjectTimeoutFrames)
                {
                    Input.ActionRelease(ActDock);
                    DockProbeFail(GdFormat.Format(
                        "STAY 长按提前离舰 %d 帧未生效（early_hold_time 蓄力链或离舰路径断线）",
                        DockProbeEjectTimeoutFrames));
                    return;
                }

                Input.ActionPress(ActDock);
                return;

            case Mothership.State.RELEASE:
                Input.ActionRelease(ActDock);
                if (_frame - _dockStateFrame > DockProbeStageTimeoutFrames)
                {
                    DockProbeFail(GdFormat.Format("RELEASE 停滞 %d 帧未进入 DEPART", DockProbeStageTimeoutFrames));
                    return;
                }

                return;

            case Mothership.State.DEPART:
                Input.ActionRelease(ActDock);
                if (_frame - _dockStateFrame > DockProbeStageTimeoutFrames * 2)
                {
                    DockProbeFail(GdFormat.Format("DEPART 停滞 %d 帧未出界释放", DockProbeStageTimeoutFrames * 2));
                    return;
                }

                return;
        }
    }

    /// <summary>蓄力将满时经生产输入路径触发一次冲刺与弹反：母舰召唤（蓄力完成）当帧会 LockInput，
    /// 锁定期的物理早退把两者的时间轴一起冻结——缺 Cancel 的实现会让解锁后第一帧用旧方向跑完残余
    /// 冲刺（含残影）、弹反停在 ACTIVE 重新打开盾判定并闪一次金光。两路都要真的在跑过，
    /// 否则判据取不到（见 <see cref="TickLockResidualProbe"/> 的前置守卫）。
    /// 冲刺默认未解锁（phase_dash 需天赋层数），故先经生产入口给一层。</summary>
    private void TriggerLockResidualInputs()
    {
        if (!_lockProbeDashGranted)
        {
            _lockProbeDashGranted = true;
            GameState.Instance.Talent.GrantLevel(new StringName("phase_dash"), 1);
        }

        if (_lockProbeTriggered)
        {
            return;
        }

        var chargeFrames = Mathf.RoundToInt(
            (float)GameState.Instance.Cfg("mothership.dock_charge_time", 3.0).AsDouble() * 60.0f);
        var triggerFrame = Mathf.Max(chargeFrames - LockProbeTriggerLeadFrames, 1);
        if (_dockChargeFrames == triggerFrame)
        {
            _lockProbeTriggered = true;
            Input.ActionPress(ProbeActDash);
            Input.ActionPress(ProbeActParry);
        }
    }

    /// <summary>锁输入残留断言：锁定窗口内逐帧断「冲刺已中止且弹反已归位」，解锁后第一帧再断一次。
    /// 判据取不到（锁定时两路都没在跑 → 触发被吞或时机错位）即显式报错，不静默降档成空转绿。</summary>
    private void TickLockResidualProbe()
    {
        if (_lockCheckedAfterUnlock)
        {
            return;
        }

        var locked = _player.IsInputLocked();
        if (!locked)
        {
            if (_lockSeen)
            {
                // 解锁后第一帧：残留的冲刺/弹反会在这一帧继续推进（冻结解除后 Tick 才跑）
                _lockCheckedAfterUnlock = true;
                if (_player.Dashing)
                {
                    DockProbeFail(GdFormat.Format(
                        "解锁后第一帧冲刺仍在进行（第 %d 帧）——锁定期冻结的残余冲刺会在解锁后跑完", _frame));
                    return;
                }

                if (_player.ParryPhase() != 0)
                {
                    DockProbeFail(GdFormat.Format(
                        "解锁后第一帧弹反未归位（第 %d 帧，相位 %d）——残余流程会重新打开盾判定",
                        _frame, _player.ParryPhase()));
                }

                return;
            }

            // 锁前逐帧记录：锁定当帧据此判「判据是否真的取到了」
            _lockWasDashing = _player.Dashing;
            _lockWasParryFlowing = _player.ParryPhase() != 0;
            return;
        }

        if (!_lockSeen)
        {
            _lockSeen = true;
            if (!_lockWasDashing && !_lockWasParryFlowing)
            {
                DockProbeFail("锁定发生时冲刺与弹反都不在进行——锁输入残留判据取不到（生产输入路径未触发？）");
                return;
            }
        }

        if (_player.Dashing)
        {
            DockProbeFail(GdFormat.Format(
                "锁输入期冲刺仍在进行（第 %d 帧，Dashing=true）——解锁后会用旧方向跑完残余冲刺", _frame));
            return;
        }

        if (_player.ParryPhase() != 0)
        {
            DockProbeFail(GdFormat.Format(
                "锁输入期弹反流程未归位（第 %d 帧，相位 %d）——停在有效窗口会重新打开盾判定并闪金光",
                _frame, _player.ParryPhase()));
        }
    }

    /// <summary>母舰坞态探针失败：报错并停探针（门禁按缺完成标记判红）。</summary>
    private void DockProbeFail(string reason)
    {
        GD.PushError("[dock-probe] " + reason);
        _dockProbe = false;
    }

    /// <summary>击杀型遭遇探针：与 <see cref="TickEventProbe"/> 同一触发链（补分数 + 请求掷签必中，
    /// 资格/门槛/门控仍由生产判定），区别在激活后逐帧对事件单位施加致死伤害（走生产 TakeDamage 链），
    /// 把「击杀型」收尾跑到终点。**一趟串两个事件**（精英炮塔 → 轰炸编队，各自独立触发与判定）：
    /// 固定开销（引擎启动 + 场景加载）比帧数更贵，合趟是时间预算下的取舍。
    ///
    /// 为什么需要：常规 `--event-probe` 跑不到任何击杀——精英炮塔趟只走到 30s 超时（0 奖励），
    /// 编队趟让编队自然离场（只命中「清除」档）。于是 `Tier.AllClear`、精英全歼 reward_score、
    /// 结算台词的**节点侧发奖与播报**在 CI 里从未执行过：奖励键被改成 0、或发奖调用被删，
    /// 全绿发布而玩家打了全歼只拿「它自己走了」档。
    ///
    /// 每个事件判三条（缺一条即不打完成标记）：
    ///   ① 击杀确实发生，且以击杀型收场（编队另断存活机归零＝走到「全歼」档）；
    ///   ② 档位奖励确实入账：本趟分数增量 ≥ 生产配置的奖励下界（精英 reward_score；
    ///      编队 reward_all_clear + 编队机数 × craft_score，两笔都只乘下界为 1 的乘区）；
    ///   ③ 结算台词确实播报：击杀后通讯浮层的台词面板出现过（ShowLine 后可见，
    ///      3.5s 停留 + 0.5s 淡出）。只读节点可见性，不碰台词层实现。
    /// 奖励值一律不动，只断言「确实入了账」。</summary>
    private void TickKillAllProbe()
    {
        if (_killAllIndex >= KillAllProbeIds.Length)
        {
            GD.Print("[event-probe] 击杀型全周期完成（精英炮塔与轰炸编队）");
            _killAllProbe = false;
            return;
        }

        var key = new StringName(KillAllProbeIds[_killAllIndex]);
        if (_killAllPhase == 0)
        {
            if (_frame - _killAllStageFrame > KillAllProbeTimeoutFrames)
            {
                KillAllProbeFail(key, GdFormat.Format("%d 帧仍未激活（生产触发链断线？）", KillAllProbeTimeoutFrames));
                return;
            }

            if (_player.IsEntryPlaying() || !_spawner.IsProcessing())
            {
                return;
            }

            if (!_triggerPosted)
            {
                _player.SetInvincible(ProbeInvincibleSeconds);
                GameState.Instance.AddScore(System.Math.Max(_events.EncounterMinScore(key), 1));
                _triggerPosted = true;
                if (!_events.RequestForcedTrigger(key))
                {
                    GD.PushError($"[event-probe] 请求启动失败：{key} 未注册");
                    _killAllProbe = false;
                }

                return;
            }

            if (_events.EncounterInstance(key)?.IsActive() == true)
            {
                _killAllPhase = 1;
                _killAllActiveFrame = _frame;
                _killAllStageFrame = _frame;
                _killAllLastKillFrame = _frame;
                _killAllScoreAtStart = GameState.Instance.Score;
                _killAllKillsAtStart = GameState.Instance.Kills;
                _killAllKilledUnits = 0;
                _killAllScoreFloor = KillAllProbeRewardFloor(key);
            }

            return;
        }

        var ev = _events.EncounterInstance(key);
        if (ev == null)
        {
            KillAllProbeFail(key, "实例已失效，无法判定收场");
            return;
        }

        if (_frame - _killAllStageFrame > KillAllProbeTimeoutFrames)
        {
            KillAllProbeFail(key, GdFormat.Format("击杀趟 %d 帧仍未收场（击杀链或收场路径断线）",
                KillAllProbeTimeoutFrames));
            return;
        }

        // ③ 结算台词：击杀发生之后通讯浮层的台词面板出现过即记一笔（只读节点可见性）
        if (_killAllSawKill && _frame > _killAllLastKillFrame && KillAllProbeCommLive(ev))
        {
            _killAllSawLine = true;
        }

        if (ev.IsActive())
        {
            KillAllProbeKillUnits(key, ev);
            return;
        }

        // 已收场：再给几帧让结算/发奖落定，然后判本事件
        if (_frame - _killAllLastKillFrame < KillAllProbeSettleFrames)
        {
            return;
        }

        if (!KillAllProbeVerify(key, ev))
        {
            _killAllProbe = false;
            return;
        }

        // 本事件过了，接下一个（下一个的激活等待从本帧起算）
        _killAllIndex++;
        _killAllPhase = 0;
        _killAllStageFrame = _frame;
        _triggerPosted = false;
        _killAllSawKill = false;
        _killAllSawLine = false;
        _killAllKillAttempts = 0;
    }

    /// <summary>击杀趟失败：报错并停探针（门禁按缺完成标记判红）。</summary>
    private void KillAllProbeFail(StringName key, string reason)
    {
        GD.PushError(GdFormat.Format("[event-probe] %s 击杀趟：%s", key, reason));
        _killAllProbe = false;
    }

    /// <summary>通讯浮层的台词面板是否正在显示：事件节点下的 CommOverlay 首子节点即台词面板，
    /// ShowLine 后可见、3.5s 停留 + 0.5s 淡出后隐藏。
    /// 判据用 IsVisibleInTree 而非节点自身的 Visible——后者只反映本节点开关，浮层/祖先被隐藏
    /// （台词层整体不可见）时它仍为 true，是假绿；IsVisibleInTree 把祖先链一并算进来。
    /// 只读可见性，不碰台词层实现。</summary>
    private static bool KillAllProbeCommLive(IEncounterEvent ev)
    {
        if (ev is not Node eventNode || !GodotObject.IsInstanceValid(eventNode))
        {
            return false;
        }

        foreach (var child in eventNode.GetChildren())
        {
            if (child is CommOverlay comm && comm.GetChildCount() > 0 && comm.GetChild(0) is CanvasItem panel)
            {
                return panel.IsVisibleInTree();
            }
        }

        return false;
    }

    /// <summary>档位奖励的分数下界（读生产配置，不另抄一份常量；全部乘区只按**下界**取）：
    /// 精英 =（reward_score + 炮台数 × turret_score）× 难度分数倍率；编队 =（reward_all_clear
    /// + 编队机数 × craft_score）× 难度分数倍率——遭遇单位击毁的分数与档位奖励同源入账，两者都经
    /// AddEventScore/AddKillScore 乘「难度击杀分系数（≥1）」「连击倍率（≥1）」「难度分数倍率」，
    /// 故实际增量只会 ≥ 本下界。
    /// 下界必须含难度分数倍率：不含时，删掉 reward_all_clear 只少了 1/3 分，仍能越过松下界（假绿）。</summary>
    private static int KillAllProbeRewardFloor(StringName key)
    {
        var gs = GameState.Instance;
        var mult = System.Math.Max(gs.ScoreMultiplier(), 1);
        if (key == new StringName("elite_turret"))
        {
            var reward = System.Math.Max((int)gs.Cfg("elite_turret_event.reward_score", 0).AsInt64(), 0);
            var turretScore = System.Math.Max((int)gs.Cfg("elite_turret_event.turret_score", 0).AsInt64(), 0);
            var turretCounts = gs.Cfg("elite_turret_event.turret_counts", new Godot.Collections.Dictionary());
            return (reward + (KillAllProbeCount(turretCounts, gs) * turretScore)) * mult;
        }

        var allClear = System.Math.Max((int)gs.Cfg("formation_strike_event.reward_all_clear", 0).AsInt64(), 0);
        var craftScore = System.Math.Max((int)gs.Cfg("formation_strike_event.craft_score", 0).AsInt64(), 0);
        var craftCounts = gs.Cfg("formation_strike_event.craft_counts", new Godot.Collections.Dictionary());
        return (allClear + (KillAllProbeCount(craftCounts, gs) * craftScore)) * mult;
    }

    /// <summary>按当前难度档读「本档单位数」（turret_counts / craft_counts 同构）：坏值一律回 0，
    /// 下界只靠档位奖励兜底（判据不因配置损坏而失配）。计数表由调用方经 Cfg 取好传入——
    /// 键字面量留在调用点，包装器不藏 balance 键（balance 读取面门禁按实参判路径）。</summary>
    private static int KillAllProbeCount(Variant counts, GameState gs)
    {
        if (counts.VariantType != Variant.Type.Dictionary)
        {
            return 0;
        }

        var v = counts.AsGodotDictionary().GetValueOrDefault(gs.Difficulty.ToString(), new Variant());
        return v.VariantType is Variant.Type.Int or Variant.Type.Float
            ? System.Math.Max((int)v.AsInt64(), 0)
            : 0;
    }

    /// <summary>逐帧对事件在场的可击杀单位施加致死伤害（走生产 TakeDamage 链，不直调 Die）：
    /// 精英炮塔事件杀炮台，编队事件杀编队机。
    /// 精英先等升起到位（生产 rise_time），编队先等投出至少一枚炸弹——保证「全歼」之外，
    /// 投弹/拦截/战损台词这些分支也被走过。</summary>
    private void KillAllProbeKillUnits(StringName key, IEncounterEvent ev)
    {
        if (key == new StringName("elite_turret"))
        {
            if (_frame - _killAllActiveFrame < KillAllProbeEliteDelayFrames)
            {
                return; // 等炮塔升起到位（升起期不可被攻击）
            }
        }
        else if (ev is FormationStrikeEvent formation
            && formation.DroppedCount() < 1
            && _frame - _killAllActiveFrame < KillAllProbeFormationWaitFrames)
        {
            return; // 等实战投弹
        }

        if (_frame - _killAllLastKillFrame < KillAllProbeKillIntervalFrames)
        {
            return;
        }

        _killAllLastKillFrame = _frame;
        _killAllKillAttempts++;
        var killed = false;
        // 注册表内直接判型击杀；失效实例跳过（收场当帧可能正被清）
        foreach (var node in GameState.Instance.Enemies)
        {
            if (node == null || !GodotObject.IsInstanceValid(node))
            {
                continue;
            }

            if (node is TurretBattery turret)
            {
                if (turret.Hp > 0)
                {
                    turret.TakeDamage(turret.Hp + 1, 1.0f);
                    if (turret.Hp <= 0)
                    {
                        _killAllKilledUnits++; // 已死单位再受击会被守卫吃掉，按施击前血量判一次
                    }
                }

                killed = true;
            }
            else if (node is FormationCraft craft)
            {
                if (craft.Hp > 0)
                {
                    craft.TakeDamage(craft.Hp + 1, 1.0f);
                    if (craft.Hp <= 0)
                    {
                        _killAllKilledUnits++;
                    }
                }

                killed = true;
            }
        }

        if (!killed)
        {
            // 收尾段（精英的航母撤离 / 编队的离场）本就无单位可杀；只在**从未**命中过单位时判失败
            if (!_killAllSawKill && _killAllKillAttempts > KillAllProbeMissAttempts)
            {
                KillAllProbeFail(key, GdFormat.Format(
                    "连续 %d 次未命中任何事件单位（单位未绑定注册表？）", KillAllProbeMissAttempts));
            }

            return;
        }

        _killAllSawKill = true;
    }

    /// <summary>单个事件的击杀型收尾判定。返回 false 表示已报错（调用方停探针）。</summary>
    private bool KillAllProbeVerify(StringName key, IEncounterEvent ev)
    {
        var ok = true;
        if (!_killAllSawKill)
        {
            GD.PushError(GdFormat.Format("[event-probe] %s 击杀趟从未命中事件单位（击杀路径未执行）", key));
            ok = false;
        }

        if (key == new StringName("formation_strike") && ev is FormationStrikeEvent formation && formation.AliveCount() != 0)
        {
            GD.PushError(GdFormat.Format(
                "[event-probe] 编队击杀趟收场时仍有 %d 架在编（未走到「全歼」档，Tier.AllClear 分支未执行）",
                formation.AliveCount()));
            ok = false;
        }

        var delta = GameState.Instance.Score - _killAllScoreAtStart;
        if (delta < _killAllScoreFloor)
        {
            GD.PushError(GdFormat.Format(
                "[event-probe] %s 击杀趟档位奖励未入账：分数增量 %d < 生产下界 %d（发奖调用被删或奖励值被清零？）",
                key, delta, _killAllScoreFloor));
            ok = false;
        }

        // 击杀口径：遭遇单位必须与普通敌机同口推进击杀数（「击杀 N 架」任务与敌机解锁门读它），
        // 且逐单位给分。只判分数会被「只加分不计杀」蒙过，只判击杀会被「只计杀不加分」蒙过。
        var killDelta = GameState.Instance.Kills - _killAllKillsAtStart;
        if (killDelta != _killAllKilledUnits)
        {
            GD.PushError(GdFormat.Format(
                "[event-probe] %s 击杀趟击杀数未按击毁单位数推进（击杀增量 %d，实际击毁 %d 架）——"
                + "遭遇单位不计入击杀口径时，任务与解锁进度门对它无效",
                key, killDelta, _killAllKilledUnits));
            ok = false;
        }

        if (!_killAllSawLine)
        {
            GD.PushError(GdFormat.Format(
                "[event-probe] %s 击杀趟未观察到结算台词（通讯浮层击杀后未出现）", key));
            ok = false;
        }

        if (ok)
        {
            GD.Print(GdFormat.Format("[event-probe] %s 击杀型全周期完成", key));
            GD.Print(GdFormat.Format("[event-probe] %s 档位奖励入账 %d 分", key, delta));
        }

        return ok;
    }

    /// <summary>遭遇探针驱动：等可驱动 → 补分数 + 请求掷签必中（仍走生产触发链）→ 观测收场。
    /// 死亡探针在激活后延迟若干帧显式击杀玩家，走管理器 EndActive 与事件 Abort 的死亡路径；
    /// 收场以「事件回 IDLE」为据，死亡探针另断波次与 Boss 互斥已归还。</summary>
    private void TickEventProbe()
    {
        var key = new StringName(_eventId);
        if (!_sawActive)
        {
            if (_player.IsEntryPlaying() || !_spawner.IsProcessing())
            {
                return;
            }

            if (!_triggerPosted)
            {
                _player.SetInvincible(ProbeInvincibleSeconds); // 自然探针与玩家存活解耦（死亡探针另行显式击杀）
                GameState.Instance.AddScore(System.Math.Max(_events.EncounterMinScore(key), 1));
                _triggerPosted = true;
                if (!_events.RequestForcedTrigger(key))
                {
                    GD.PushError($"[event-probe] 请求启动失败：{_eventId} 未注册");
                    _eventId = "";
                }

                return;
            }

            if (_events.EncounterInstance(key)?.IsActive() == true)
            {
                _sawActive = true;
                _activeFrame = _frame;
            }

            return;
        }

        if (_deathProbe && !_killed && _frame - _activeFrame >= DeathProbeDelayFrames)
        {
            _killed = true;
            _player.Die(); // 显式击杀（绕过无敌）——死亡路径 = 管理器 EndActive + 事件 Abort
            GameState.Instance.SetTreePaused(false); // 结算页会冻住事件撤离，探针需观测打断完成
            return;
        }

        var ev = _events.EncounterInstance(key);
        if (ev == null)
        {
            GD.PushError($"[event-probe] {_eventId} 实例已失效，无法判定收场");
            _eventId = "";
            return;
        }

        if (ev.IsActive())
        {
            // 死亡打断后的收尾窗口（管理器已 EndActive、事件 FSM 尚未回 IDLE）：此刻活跃 id
            // 必须仍为空——收尾期被轮询重新登记回来时，「本局是否有遭遇在跑」的判据读反
            if (_killed)
            {
                TickInterruptActiveId(key);
            }

            if (!_deathProbe)
            {
                TickEncounterAimProbe();
                if (key == new StringName("formation_strike"))
                {
                    TickFormationBombDomain();
                    TickParryBombProbe();
                }
            }

            return;
        }

        // 收场后的等待：单位走 QueueFree（帧末出树）与注销登记，同帧就判会读到尚未出树的单位
        if (++_eventEndSettle < EventEndSettleFrames)
        {
            return;
        }

        if (_deathProbe && (_spawner.BossFrozen() || _spawner.WavesPaused()))
        {
            GD.PushError($"[event-probe] {_eventId} 死亡打断后波次/Boss 互斥未归还");
            _eventId = "";
            return;
        }

        if (_deathProbe && !_interruptIdChecked)
        {
            GD.PushError($"[event-probe] {_eventId} 死亡打断后未观测到事件收尾窗口——活跃 id 断言未执行（判据取不到即失败）");
            _eventId = "";
            return;
        }

        if (!_deathProbe && !VerifyEventProbeOutcome(key, ev))
        {
            _eventId = "";
            return;
        }

        GD.Print(GdFormat.Format(
            _deathProbe ? "[event-probe] %s 死亡打断完成" : "[event-probe] %s 全周期完成", _eventId));
        _eventId = "";
    }

    /// <summary>死亡打断收尾期的活跃 id 断言：只在事件 FSM 尚未回 IDLE 的窗口内逐帧判——
    /// 回 IDLE 之后再判「ActiveId 为空」是空转（坏实现也已把 id 清掉），等于没判。
    /// 发现被重新登记即报红并停探针（缺完成标记，双重红灯）。</summary>
    private void TickInterruptActiveId(StringName key)
    {
        _interruptIdChecked = true;
        var active = _events.ActiveId(GameEventManager.GroupEncounter).ToString();
        if (active.Length == 0)
        {
            return;
        }

        GD.PushError(GdFormat.Format(
            "[event-probe] %s 死亡打断后的收尾期（第 %d 帧，事件 FSM 尚未回 IDLE）又被登记为活跃（ActiveId=%s）"
            + "——收尾期的「本局是否有遭遇在跑」读作有遭遇，语义与事实相反",
            key, _frame, active));
        _eventId = "";
    }

    /// <summary>遭遇单位的辅助瞄准契约断言（炮塔/编队机）：扫描按 <c>is Enemy</c> 判型的坏实现，
    /// 遭遇期间屏上唯一可打目标整体不吃辅助框/强追踪/弱追踪，且**不崩不报错**，只有这条断言抓得到。
    /// 三条同时判（缺一条即可被另一种坏法蒙过）：
    ///   ① 标记计数 ≥1（登记口径）；
    ///   ② <c>MarkedTargetAt(单位真实世界坐标)</c> 命中该单位（框内强追踪）；
    ///   ③ <c>NearestConeTarget(单位正上方一点, 正下方, 该档 coneCos)</c> 命中该单位（弱追踪）。
    ///
    /// 为什么先把准星注入到单位坐标：<c>MarkedTargetAt</c> 是**同渲染帧缓存**（本帧首个调用方的
    /// 结果供全帧复用），同帧换一个查询点调用会拿到别人的结果——那是缓存的契约而非缺陷，
    /// 拿它判红会误伤。故经生产注入点 <c>AimPointOverride</c> 把准星指到单位坐标，让帧内所有
    /// 调用方查同一处，判据才落在「扫描口径」上。
    /// 收场另由 <see cref="VerifyEventProbeOutcome"/> 断标记登记回落（防登记/注销泄漏）。</summary>
    private void TickEncounterAimProbe()
    {
        if (_aimProbeChecked || GameState.Instance.AimFrameLayer is not AimFrameLayer layer)
        {
            return;
        }

        if (_aimHoldFrames > 0)
        {
            _aimHoldFrames--;
            return;
        }

        var target = FindEncounterAimTarget();
        if (target == null)
        {
            return;
        }

        if (!ReferenceEquals(target, _aimProbeTarget))
        {
            // 新目标：把准星注入到它的坐标，随后帧内所有查询点即此处
            _aimProbeTarget = target;
            _player.AimPointOverride = target.AimWorldPosition;
            _aimHoldFrames = 2;
            return;
        }

        _aimProbeTried = true;
        if (AimTargetCount.Marked < 1)
        {
            GD.PushError("[event-probe] 遭遇单位已标记但标记计数为 0——AimFrameLayer 的零标记早退会让它整体不吃辅助瞄准");
            _eventId = "";
            return;
        }

        var center = target.AimWorldPosition;
        var boxed = layer.MarkedTargetAt(center);
        if (!ReferenceEquals(boxed, target))
        {
            GD.PushError(GdFormat.Format(
                "[event-probe] 辅助框命中查询未命中遭遇单位（坐标 %.0f,%.0f 返回 %s）——"
                + "扫描按 is Enemy 判型时遭遇单位整体不吃框内强追踪",
                center.X, center.Y, boxed == null ? "null" : boxed.GetType().Name));
            _eventId = "";
            return;
        }

        var cone = layer.NearestConeTarget(center + new Vector2(0.0f, -2.0f), Vector2.Down, _player.ConeCos());
        if (!ReferenceEquals(cone, target))
        {
            GD.PushError(GdFormat.Format(
                "[event-probe] 锥形弱追踪未命中遭遇单位（坐标 %.0f,%.0f 返回 %s）——"
                + "弱追踪的判型口径与「覆盖全部可打目标」不符",
                center.X, center.Y, cone == null ? "null" : cone.GetType().Name));
            _eventId = "";
            return;
        }

        _player.AimPointOverride = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        _aimProbeChecked = true;
    }

    /// <summary>注册表里第一个可瞄准的遭遇单位（非 Enemy 的 IAimTarget 实现：炮塔/编队机）。</summary>
    private static IAimTarget? FindEncounterAimTarget()
    {
        foreach (var node in GameState.Instance.Enemies)
        {
            if (node is IAimTarget target && node is not Enemy && target.AimTargetable && target.AimMarked)
            {
                return target;
            }
        }

        return null;
    }

    /// <summary>收场判定：辅助瞄准断言确实执行过 + 标记登记无泄漏（遭遇单位收场后计数回落到普通敌机量）。
    /// 返回 false 表示已报错（调用方停探针，不打完成标记）。</summary>
    private bool VerifyEventProbeOutcome(StringName key, IEncounterEvent ev)
    {
        var ok = true;
        if (!_aimProbeTried)
        {
            GD.PushError(GdFormat.Format(
                "[event-probe] %s 全程未观测到可瞄准的遭遇单位——辅助瞄准断言未执行（覆盖空洞，判据取不到即失败）",
                key));
            ok = false;
        }

        if (AimTargetCount.Marked != Enemy.AimMarkedCount)
        {
            GD.PushError(GdFormat.Format(
                "[event-probe] %s 收场后标记计数未回落（总标记 %d，普通敌机 %d）——遭遇单位注销时漏减，"
                + "AimFrameLayer 将长期走「有标记」分支逐帧全表扫描",
                key, AimTargetCount.Marked, Enemy.AimMarkedCount));
            ok = false;
        }

        if (key == new StringName("formation_strike"))
        {
            if (!_bombDomainChecked)
            {
                GD.PushError("[event-probe] formation_strike 全程未观测到投放的炸弹——"
                    + "投放点可见域断言未执行（覆盖空洞，判据取不到即失败）");
                ok = false;
            }

            if (ev is FormationStrikeEvent formation && formation.InterceptedCount() > formation.DroppedCount())
            {
                GD.PushError(GdFormat.Format(
                    "[event-probe] formation_strike 拦截数多于投出数（拦截 %d > 投出 %d）——计数口径自相矛盾",
                    formation.InterceptedCount(), formation.DroppedCount()));
                ok = false;
            }

            if (!_parryProbeVerified)
            {
                GD.PushError("[event-probe] formation_strike 未走完弹反拆弹路径（连击 +1 与同额拆弹分断言未执行）"
                    + "——弹反与击落两条拆弹路径的收益口径无从判定");
                ok = false;
            }
        }

        return ok;
    }

    /// <summary>弹反拆弹路径（生产 <see cref="IParryable"/> 入口 <c>bomb.Reflect()</c>）：
    /// 两条拆弹路径必须同额同族——击落走 <c>AddKillScore</c>（吃连击与 score_amp），弹反命中原先前者
    /// 不给分，更难的应对收益反而更低。**判别式必须含连击**：bomb_score 与 reward_per_intercept
    /// 默认同为 50，只判分数增量分辨不出两条路径（逐枚拦截分走 AddEventScore、同样加 50 而不动连击）。
    /// 故断「连击 +1」+「分数增量 ≥ bomb_score × KillScoreFactor × 难度档倍率」。</summary>
    private void TickParryBombProbe()
    {
        var gs = GameState.Instance;
        if (!_parryProbeFired)
        {
            foreach (var node in gs.Enemies)
            {
                if (node is not FormationBomb bomb || !GodotObject.IsInstanceValid(bomb)
                    || bomb.IsReflected || bomb.IsParked())
                {
                    continue;
                }

                _parryProbeFired = true;
                _parryProbeBomb = bomb;
                _parryWaitFrames = 0;
                _parryComboBefore = gs.Combo;
                _parryScoreBefore = gs.Score;
                _parryScoreFloor = ParryProbeScoreFloor();
                if (!bomb.Reflect())
                {
                    GD.PushError("[event-probe] formation_strike 弹反入口拒绝了未拆封的在飞炸弹（IParryable 契约失效）");
                    _eventId = "";
                }

                return;
            }

            return;
        }

        if (_parryProbeVerified)
        {
            return;
        }

        if (gs.Combo > _parryComboBefore)
        {
            var comboDelta = gs.Combo - _parryComboBefore;
            var delta = gs.Score - _parryScoreBefore;
            if (comboDelta != 1)
            {
                GD.PushError(GdFormat.Format(
                    "[event-probe] formation_strike 反射弹命中后的连击增量 %d（期望 1）——拆弹分未走 AddKillScore",
                    comboDelta));
                _eventId = "";
                return;
            }

            if (delta < _parryScoreFloor)
            {
                GD.PushError(GdFormat.Format(
                    "[event-probe] formation_strike 反射弹命中后的分数增量 %d < 生产下界 %d"
                    + "（bomb_score × KillScoreFactor × 难度档倍率）——弹反路径的拆弹分缺失或低于击落路径",
                    delta, _parryScoreFloor));
                _eventId = "";
                return;
            }

            _parryProbeVerified = true;
            return;
        }

        // 反射弹已消耗（命中编队机）却仍无连击：直接指出是拆弹分这条路径断了——
        // 只报「等超时」会把定位信息丢给读日志的人（命中当帧即可判定，不必等满超时）
        if (_parryProbeBomb != null && GodotObject.IsInstanceValid(_parryProbeBomb)
            && _parryProbeBomb.IsParked() && _parryProbeBomb.Intercepted && gs.Combo <= _parryComboBefore)
        {
            GD.PushError("[event-probe] formation_strike 反射弹已命中编队机（Intercepted 已置位）但连击未 +1——"
                + "弹反路径的拆弹分未走 AddKillScore（更难的应对收益反而低于直接击落）");
            _eventId = "";
            return;
        }

        if (++_parryWaitFrames > ParryProbeTimeoutFrames)
        {
            GD.PushError($" 反射弹 {ParryProbeTimeoutFrames} 帧内未命中编队机——弹反拆弹路径断言未执行");
            _eventId = "";
        }
    }

    /// <summary>弹反命中编队机一笔的分数下界（读生产配置，不另抄常量）：bomb_score 经
    /// KillScoreFactor 放大后取整，再乘难度档倍率（连击乘区 ≥1，故实际只会 ≥ 本下界）。</summary>
    private static int ParryProbeScoreFloor()
    {
        var gs = GameState.Instance;
        var bombScore = System.Math.Max((int)gs.Cfg("formation_strike_event.bomb_score", 0).AsInt64(), 0);
        var scaled = (int)System.Math.Round(bombScore * gs.KillScoreFactor());
        return scaled * System.Math.Max(gs.ScoreMultiplier(), 1);
    }

    /// <summary>编队投放点可见域断言：投放点必须在可见世界域外扩
    /// <c>FormationBomb.BodyRadius × world_scale</c> 之内（生产裁剪口径同源，见
    /// FormationStrikeEvent.ProcessDrops）。越界的表现是「屏外弹」——既不可见也不可交互，
    /// 却计入「已投出」分母，令「全数拦截」结构性不可达，且不崩不报错。
    /// 逐帧遍历在场炸弹、按「本帧在册而上帧不在册」识别一次投放（池化复用同实例重新入场＝新投放），
    /// 与实例首次出现同判：越界即 PushError 并带上实测坐标。
    /// 收场再断 <c>InterceptedCount() &lt;= DroppedCount()</c>（计数的基本自洽）。</summary>
    private void TickFormationBombDomain()
    {
        var rect = GameState.Instance.ViewWorldRect();
        var margin = FormationBomb.BodyRadius * (float)GameState.Instance.WorldScale;
        var allowed = rect.Grow(margin);
        var live = new HashSet<ulong>();
        foreach (var node in GameState.Instance.Enemies)
        {
            if (node is not FormationBomb bomb || !GodotObject.IsInstanceValid(bomb))
            {
                continue;
            }

            live.Add(bomb.GetInstanceId());
            if (_bombIdsPrev.Contains(bomb.GetInstanceId()))
            {
                continue;
            }

            _bombDomainChecked = true;
            var p = bomb.GlobalPosition;
            if (!allowed.HasPoint(p))
            {
                GD.PushError(GdFormat.Format(
                    "[event-probe] formation_strike 投放点在可见域之外（第 %d 帧，落点 %.0f,%.0f；"
                    + "允许域 x∈[%.0f,%.0f] y∈[%.0f,%.0f]，外扩 %.1fpx）——屏外弹不可见也不可交互，"
                    + "却计入「已投出」分母，全数拦截将不可达",
                    _frame, p.X, p.Y, allowed.Position.X, allowed.End.X, allowed.Position.Y, allowed.End.Y, margin));
                _eventId = "";
                return;
            }
        }

        _bombIdsPrev = live;
    }

    /// <summary>恶意存档探针：读档链对「语法合法但字段类型不符」的手改档必须逐字段回默认。
    ///
    /// 为什么必须探这一趟：Godot 4.6 的 Variant.As* 是**宽松转换、不抛异常**（实测全矩阵：
    /// AsBool("no") 得 true、AsInt64("lots") 得 0、AsString(7) 得 "7"、AsGodotDictionary(5) 得空表），
    /// 于是裸取不会崩溃，只会把坏值静默读成合法值——比崩溃更安静：任务 claimed 写成 "no"
    /// 会被当成已领取，该任务奖励永久领不到，界面无任何信号。单测够不到（判型在引擎绑定层，
    /// xUnit 只引用 core），故用探针写档 + 调生产读档入口实跑。三段判定，缺一不可：
    ///   ① 恶意档（字段类型全错）读入不得抛，且各字段落回默认；
    ///   ② 恶意档·子表类型错：mission 条目的 claimed/progress 判型（宽松转换的真正判别式）；
    ///   ③ 正常档读入必须逐项还原——只判 ①② 会让「守卫一律回退」的实现照样绿（假绿）。
    /// 另断补发信号：读档直写字段必须把还原值推给消费域，否则界面/手感读的是复位值
    /// （健康值给 HUD 血条，手柄灵敏度给 Player 的摇杆积分）。</summary>
    private void RunHostileSaveProbe()
    {
        var gs = GameState.Instance;
        var ok = true;

        _onProbeHealthChanged = Callable.From<float>(OnProbeHealthChanged);
        _onProbeJoySettingsChanged = Callable.From<double, double>(OnProbeJoySettingsChanged);
        gs.Connect(GameState.SignalName.HealthChanged, _onProbeHealthChanged);
        gs.Connect(GameState.SignalName.JoySettingsChanged, _onProbeJoySettingsChanged);
        _probeSubscribed = true;

        // ① 恶意档：写盘 → 走生产读档入口；逐字段落默认（不抛，故异常分支实际抓的是意外）
        ok &= WriteUserFile("user://run.json", HostileRunJson);
        ok &= WriteUserFile("user://settings.json", HostileSettingsJson);
        try
        {
            if (!gs.LoadRun())
            {
                GD.PushError("[hostile-save-probe] 恶意 run.json（version 合法）未读入——应逐字段回默认，而不是判无档");
                ok = false;
            }
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[hostile-save-probe] 恶意 run.json 读入抛异常：{ex.GetType().Name} {ex.Message}");
            ok = false;
        }

        try
        {
            gs.LoadSettings();
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[hostile-save-probe] 恶意 settings.json 读入抛异常：{ex.GetType().Name} {ex.Message}");
            ok = false;
        }

        if (gs.Missions.Count != 0)
        {
            GD.PushError($"[hostile-save-probe] missions 非字典应回空表，实得 {gs.Missions.Count} 条");
            ok = false;
        }

        if (gs.Score != 0)
        {
            GD.PushError($"[hostile-save-probe] score 非数值应回 0，实得 {gs.Score}");
            ok = false;
        }

        if (!(gs.Health >= 1.0 && gs.Health <= gs.MaxHealth()))
        {
            GD.PushError($"[hostile-save-probe] health 非数值应回上限并钳，实得 {gs.Health}（上限 {gs.MaxHealth()}）");
            ok = false;
        }

        if (gs.Locale != "zh" || gs.ViewZoom != new StringName("small") || gs.AimAssistLevel != new StringName("medium"))
        {
            GD.PushError(GdFormat.Format(
                "[hostile-save-probe] 恶意设置档应保持出厂档，实得 locale=%s view_zoom=%s aim_assist=%s",
                gs.Locale, gs.ViewZoom, gs.AimAssistLevel));
            ok = false;
        }

        if (gs.JoyAimSpeed != 1400.0)
        {
            GD.PushError($"[hostile-save-probe] joy_aim_speed 非数值应保持默认 1400，实得 {gs.JoyAimSpeed}");
            ok = false;
        }

        // ② 恶意档·子表类型错：mission 条目内 progress/claimed 判型。
        // 这一段是本探针真正的判别式：Godot 4.6 的 Variant.As* 是**宽松转换**（不抛）——
        // AsBool("no") 得 true、AsInt64("lots") 得 0。裸取会把「字符串 claimed」静默读成已领取
        // （玩家领不到已完成任务的 RP，且界面无任何信号）。故此处断「非 Bool 一律回 false」。
        ok &= WriteUserFile("user://run.json", HostileMissionEntryJson);
        try
        {
            if (!gs.LoadRun())
            {
                GD.PushError("[hostile-save-probe] 恶意 run.json（任务子表类型错）未读入");
                ok = false;
            }
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[hostile-save-probe] 恶意 run.json（任务子表类型错）读入抛异常：{ex.GetType().Name} {ex.Message}");
            ok = false;
        }

        var probeMission = new StringName("kill_15");
        if (gs.IsMissionClaimed(probeMission))
        {
            GD.PushError("[hostile-save-probe] claimed 非 Bool 被判为已领取（AsBool 宽松转换把字符串读成 true）——"
                + "玩家将无法领取该任务奖励");
            ok = false;
        }

        if (gs.MissionProgress(probeMission) != 0)
        {
            GD.PushError(GdFormat.Format(
                "[hostile-save-probe] 恶意任务子表 progress 应回默认 0，实得 %d",
                gs.MissionProgress(probeMission)));
            ok = false;
        }

        // ④ 白名单与上限过滤：池外任务 id 整条丢弃（否则 goal 查池得 0 → IsMissionDone 恒真 →
        // 反复领取 RP）、池内条目的 goal 取池内定稿值、未知增幅键丢弃、超上限层级削平、
        // 白名单外改键动作名丢弃（收下会永久回写进设置档）
        var rebindable = gs.REBINDABLE_ACTIONS.Count > 0 ? gs.REBINDABLE_ACTIONS[0].ToString() : "";
        ok &= WriteUserFile("user://run.json", HostileWhitelistRunJson);
        ok &= WriteUserFile("user://settings.json", GdFormat.Format(
            "{\"version\":%d,\"key_bindings\":{\"%s\":[81],\"not_a_real_action\":[87]}}",
            Core.Storage.SettingsMigration.CurrentVersion, rebindable));
        try
        {
            if (!gs.LoadRun())
            {
                GD.PushError("[hostile-save-probe] 恶意 run.json（白名单/上限）未读入");
                ok = false;
            }
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[hostile-save-probe] 恶意 run.json（白名单/上限）读入抛异常：{ex.GetType().Name} {ex.Message}");
            ok = false;
        }

        try
        {
            gs.LoadSettings();
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[hostile-save-probe] 恶意 settings.json（改键白名单）读入抛异常：{ex.GetType().Name} {ex.Message}");
            ok = false;
        }

        var ghostTask = new StringName("ghost_task");
        if (gs.Missions.ContainsKey(ghostTask))
        {
            GD.PushError("[hostile-save-probe] 池外任务 id 被收下——goal 查池得 0 会让 IsMissionDone 恒真，可反复领 RP");
            ok = false;
        }

        if (gs.IsMissionDone(ghostTask))
        {
            GD.PushError("[hostile-save-probe] 池外任务被判为已完成（IsMissionDone 恒真）——该档可直接换 RP");
            ok = false;
        }

        var poolTask = new StringName("kill_15");
        var poolGoal = gs.MissionGoal(poolTask);
        if (gs.IsMissionDone(poolTask))
        {
            GD.PushError($"[hostile-save-probe] 池内任务用了存档里被改小的 goal（progress 3 ≥ 存档 goal 1 即判完成）"
                + $"——goal 必须取池内定稿值 {poolGoal}");
            ok = false;
        }

        if (gs.Missions.TryGetValue(poolTask, out var storedEntry) && storedEntry.VariantType == Variant.Type.Dictionary)
        {
            var storedGoal = (int)storedEntry.AsGodotDictionary().GetValueOrDefault("goal", 0).AsInt64();
            if (storedGoal != poolGoal)
            {
                GD.PushError($"[hostile-save-probe] 池内任务条目的 goal 未取池内定稿值（存档 {storedGoal}，池内 {poolGoal}）");
                ok = false;
            }
        }
        else
        {
            GD.PushError("[hostile-save-probe] 池内任务条目未被收下——白名单把合法条目一并滤掉了");
            ok = false;
        }

        var ghostAugment = new StringName("ghost_augment");
        if (gs.AugmentLevel(ghostAugment) != 0)
        {
            GD.PushError($"[hostile-save-probe] 未知增幅键被收下（层级 {gs.AugmentLevel(ghostAugment)}）"
                + "——手改档可凭空注入增幅效果");
            ok = false;
        }

        var extraLifeMax = (int)gs.Cfg("augments.extra_life.max_stacks", 0).AsInt64();
        var extraLifeProbe = new StringName("extra_life");
        if (gs.AugmentLevel(extraLifeProbe) != extraLifeMax)
        {
            GD.PushError($"[hostile-save-probe] 超上限增幅层级未削平（存档 99 → {gs.AugmentLevel(extraLifeProbe)}，"
                + $"结构上限 {extraLifeMax}）");
            ok = false;
        }

        if (gs.KeyBindings.ContainsKey(new StringName("not_a_real_action")))
        {
            GD.PushError("[hostile-save-probe] 白名单外的改键动作名被收下——会被永久回写进设置档，且 InputMap 不认它");
            ok = false;
        }

        if (InputMap.HasAction("not_a_real_action"))
        {
            GD.PushError("[hostile-save-probe] 白名单外的动作名进了 InputMap");
            ok = false;
        }

        if (rebindable.Length > 0 && !gs.KeyBindings.ContainsKey(new StringName(rebindable)))
        {
            GD.PushError($"[hostile-save-probe] 合法改键动作 {rebindable} 被白名单误丢——玩家改键会静默丢失");
            ok = false;
        }

        // ③ 正常档：还原值逐项对上；同时观测读档补发的两个信号
        _probeHealthSeen = -1.0f;
        _probeJoyAimSpeedSeen = -1.0;
        _probeJoyDeadzoneSeen = -1.0;
        ok &= WriteUserFile("user://run.json", ValidRunJson);
        ok &= WriteUserFile("user://settings.json", ValidSettingsJson);
        try
        {
            if (!gs.LoadRun())
            {
                GD.PushError("[hostile-save-probe] 正常 run.json 未读入（守卫把合法档也拒了？）");
                ok = false;
            }
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[hostile-save-probe] 正常 run.json 读入抛异常：{ex.GetType().Name} {ex.Message}");
            ok = false;
        }

        if (gs.Score != 900 || gs.Kills != 7 || gs.BossKills != 2 || gs.Rp != 6)
        {
            GD.PushError(GdFormat.Format(
                "[hostile-save-probe] 正常档标量未还原：score=%d kills=%d boss_kills=%d rp=%d（期望 900/7/2/6）",
                gs.Score, gs.Kills, gs.BossKills, gs.Rp));
            ok = false;
        }

        if (Math.Abs(gs.RunTime - 300.0) > 1e-6 || Math.Abs(gs.Health - AssertedHealth) > 1e-6)
        {
            GD.PushError(GdFormat.Format(
                "[hostile-save-probe] 正常档 run_time/health 未还原：run_time=%.3f health=%.3f（期望 300 / %.1f）",
                gs.RunTime, gs.Health, AssertedHealth));
            ok = false;
        }

        if (gs.MaxHealth() != 150.0)
        {
            GD.PushError($"[hostile-save-probe] 正常档 extra_life 层级未还原：生命上限 {gs.MaxHealth()}（期望 150）");
            ok = false;
        }

        if (gs.MissionProgress(new StringName("kill_15")) != 3)
        {
            GD.PushError($"[hostile-save-probe] 正常档任务进度未还原：{gs.MissionProgress(new StringName("kill_15"))}（期望 3）");
            ok = false;
        }

        if (Math.Abs(_probeHealthSeen - AssertedHealth) > 1e-6)
        {
            GD.PushError(GdFormat.Format(
                "[hostile-save-probe] 读档未补发健康信号（收到 %.3f，期望 %.1f）——HUD 血条会停在复位值",
                _probeHealthSeen, AssertedHealth));
            ok = false;
        }

        try
        {
            gs.LoadSettings();
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[hostile-save-probe] 正常 settings.json 读入抛异常：{ex.GetType().Name} {ex.Message}");
            ok = false;
        }

        if (gs.Locale != "en" || gs.ViewZoom != new StringName("large") || gs.AimAssistLevel != new StringName("high")
            || gs.FpsCap != new StringName("fps30") || gs.Difficulty != new StringName("hard")
            || gs.CustomWindowWidth != 1000)
        {
            GD.PushError(GdFormat.Format(
                "[hostile-save-probe] 正常设置档未还原：locale=%s view_zoom=%s aim_assist=%s fps_cap=%s difficulty=%s custom_width=%d",
                gs.Locale, gs.ViewZoom, gs.AimAssistLevel, gs.FpsCap, gs.Difficulty, gs.CustomWindowWidth));
            ok = false;
        }

        if (Math.Abs(gs.JoyAimSpeed - AssertedJoyAimSpeed) > 1e-6 || Math.Abs(gs.JoyDeadzone - AssertedJoyDeadzone) > 1e-6)
        {
            GD.PushError(GdFormat.Format(
                "[hostile-save-probe] 正常档手柄两项未还原：joy_aim_speed=%.1f joy_deadzone=%.2f（期望 %.1f / %.2f）",
                gs.JoyAimSpeed, gs.JoyDeadzone, AssertedJoyAimSpeed, AssertedJoyDeadzone));
            ok = false;
        }

        if (Math.Abs(_probeJoyAimSpeedSeen - AssertedJoyAimSpeed) > 1e-6
            || Math.Abs(_probeJoyDeadzoneSeen - AssertedJoyDeadzone) > 1e-6)
        {
            GD.PushError(GdFormat.Format(
                "[hostile-save-probe] 读档未补发手柄设置通知（收到 %.1f/%.2f）——Player 会按旧灵敏度跑",
                _probeJoyAimSpeedSeen, _probeJoyDeadzoneSeen));
            ok = false;
        }

        if (ok)
        {
            GD.Print("[hostile-save-probe] 恶意档读入未崩溃");
        }
    }

    /// <summary>存档还原探针（--save-restore-probe）：风险加点层级、生命上限自洽与读档补发信号。
    ///
    /// 为什么必须探：读档还原里的「层级上限」与「信号补发」都是静默面——层级被无条件钳回结构上限时，
    /// 玩家双倍价买的那一级静默消失（乘算消费端少一级效果）；还原直写字段不发信号时，HUD 难度读数
    /// 与目标行停在复位值（最长停到下一次 30s 量化步进）。三段，缺一不可：
    ///   ① 带风险加点标记的超结构层级：层级 == max+1，生命上限与层级自洽；
    ///   ② 反向对照（去掉标记）：同一存档层级钳回 max——只判 ① 会让「一律抬级」的实现照样绿；
    ///   ③ 对照（名单里塞池外节点名）：不抬高任何节点。
    /// 另断读档补发：DifficultyChanged 的值 == 存档 difficulty_multiplier、ScoreChanged 回调里读到的时间
    /// 已是存档 run_time（信号先于还原会让订阅方读到 0）。</summary>
    private void RunSaveRestoreProbe()
    {
        var gs = GameState.Instance;
        var ok = true;

        _onProbeDifficultyChanged = Callable.From<float>(OnProbeDifficultyChanged);
        _onProbeScoreChanged = Callable.From<int>(OnProbeScoreChanged);
        gs.Connect(GameState.SignalName.DifficultyChanged, _onProbeDifficultyChanged);
        gs.Connect(GameState.SignalName.ScoreChanged, _onProbeScoreChanged);
        _probeRunSubscribed = true;

        var maxLevel = System.Math.Max((int)gs.Cfg("augments.extra_life.max_stacks", 0).AsInt64(), 0);
        var baseHp = gs.Cfg("player.max_health", 100.0).AsDouble();
        var bonusHp = gs.Cfg("augments.extra_life.max_hp_bonus", 50.0).AsDouble();
        var extraLife = new StringName("extra_life");
        // JSON 里的数值一律用不变文化格式化：逗号小数点会被 JSON 解析拒掉（本地化环境的静默坏点）
        var runTimeJson = AssertedRunTime.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var difficultyJson = AssertedDifficulty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var overchargedJson =
            $"{{\"version\":1,\"health\":42.5,\"run_time\":{runTimeJson},\"difficulty_multiplier\":{difficultyJson},"
            + $"\"talent_levels\":{{\"extra_life\":{maxLevel + 1}}},\"talent_overcharged\":[\"extra_life\"],"
            + $"\"augments\":{{\"extra_life\":{maxLevel + 1}}}}}";
        var plainJson =
            $"{{\"version\":1,\"health\":42.5,\"run_time\":{runTimeJson},\"difficulty_multiplier\":{difficultyJson},"
            + $"\"talent_levels\":{{\"extra_life\":{maxLevel + 1}}},\"talent_overcharged\":[],"
            + $"\"augments\":{{\"extra_life\":{maxLevel + 1}}}}}";
        var plainLevel = System.Math.Clamp(maxLevel - 3, 1, System.Math.Max(maxLevel, 1));
        var ghostJson =
            $"{{\"version\":1,\"health\":42.5,\"run_time\":{runTimeJson},\"difficulty_multiplier\":{difficultyJson},"
            + $"\"talent_levels\":{{\"extra_life\":{plainLevel}}},\"talent_overcharged\":[\"ghost\"],"
            + $"\"augments\":{{\"extra_life\":{plainLevel}}}}}";

        // ① 超结构层级 + 风险加点标记：max+1 级须保留（双倍价买的那一级）
        _probeDifficultySeen = -1.0f;
        _probeRunTimeAtScore = -1.0;
        ok &= WriteUserFile("user://run.json", overchargedJson);
        ok &= LoadRunForProbe("超载档（max+1 + 风险加点标记）");
        if (gs.TalentLevel(extraLife) != maxLevel + 1)
        {
            GD.PushError($"[save-restore-probe] 风险加点的那一级未保留：层级 {gs.TalentLevel(extraLife)}"
                + $"（期望 {maxLevel + 1}）——双倍价买的层级读档即消失");
            ok = false;
        }

        var expectSuperHp = baseHp + (bonusHp * (maxLevel + 1));
        if (Math.Abs(gs.MaxHealth() - expectSuperHp) > 1e-6)
        {
            GD.PushError($"[save-restore-probe] 生命上限与层级不自洽：{gs.MaxHealth():0.###}"
                + $"（层级 {gs.TalentLevel(extraLife)} 应为 {expectSuperHp:0.###}）——读档后血上限与天赋读数分叉");
            ok = false;
        }

        if (Math.Abs(_probeDifficultySeen - AssertedDifficulty) > 1e-3)
        {
            GD.PushError($"[save-restore-probe] 读档未补发难度信号或值不符（收到 {_probeDifficultySeen:0.###}，"
                + $"存档 {AssertedDifficulty:0.###}）——HUD 难度读数会停在复位值");
            ok = false;
        }

        if (Math.Abs(_probeRunTimeAtScore - AssertedRunTime) > 1e-6)
        {
            GD.PushError($"[save-restore-probe] 分数信号回调里读到的时间是 {_probeRunTimeAtScore:0.###}"
                + $"（存档 {AssertedRunTime:0.###}）——RunTime 还原晚于信号补发时，订阅方读到复位值 0");
            ok = false;
        }

        // ② 反向对照：去掉风险加点标记 → 同一层级钳回结构上限（只判 ① 会让「一律抬级」照样绿）
        ok &= WriteUserFile("user://run.json", plainJson);
        ok &= LoadRunForProbe("无标记档（层级超上限）");
        if (gs.TalentLevel(extraLife) != maxLevel)
        {
            GD.PushError($"[save-restore-probe] 无风险加点标记的层级未钳回结构上限：{gs.TalentLevel(extraLife)}"
                + $"（期望 {maxLevel}）——存档是威胁模型，抬级得有对应的锁定标记");
            ok = false;
        }

        var expectPlainHp = baseHp + (bonusHp * maxLevel);
        if (Math.Abs(gs.MaxHealth() - expectPlainHp) > 1e-6)
        {
            GD.PushError($"[save-restore-probe] 钳回层级后生命上限未同步：{gs.MaxHealth():0.###}"
                + $"（层级 {gs.TalentLevel(extraLife)} 应为 {expectPlainHp:0.###}）");
            ok = false;
        }

        // ③ 对照：名单里塞池外节点名 → 不抬高任何节点
        ok &= WriteUserFile("user://run.json", ghostJson);
        ok &= LoadRunForProbe("池外风险加点档");
        if (gs.TalentLevel(extraLife) != plainLevel || gs.TalentLevel(new StringName("ghost")) != 0)
        {
            GD.PushError($"[save-restore-probe] 池外风险加点名抬高了节点（extra_life={gs.TalentLevel(extraLife)}，"
                + $"ghost={gs.TalentLevel(new StringName("ghost"))}）——未知 id 不该让任何节点多一级");
            ok = false;
        }

        if (ok)
        {
            GD.Print("[save-restore-probe] 层级与信号还原成立");
        }
    }

    /// <summary>本探针的读档（失败即记一条错，返回 false 供调用方累计）。</summary>
    private static bool LoadRunForProbe(string label)
    {
        try
        {
            if (GameState.Instance.LoadRun())
            {
                return true;
            }

            GD.PushError($"[save-restore-probe] {label} 未读入");
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[save-restore-probe] {label} 读入抛异常：{ex.GetType().Name} {ex.Message}");
        }

        return false;
    }

    /// <summary>设置版本探针（--settings-version-probe）：高于当前的版本档按逐字段默认值回退。
    ///
    /// DESIGN_BASELINE §1.15 承诺「高于当前按逐字段默认值回退（不猜未来字段语义）」——只告警照读已知键名
    /// 时，降级安装会把未来语义的同名键当当前语义读入，core 的版本判定被架空且没有任何信号。
    /// 三段，缺一不可：
    ///   ① 版本 +1 且关键项全非默认 → 读入后逐项等于默认（先复位到出厂档再读，默认值才是判据）；
    ///   ② 同版对照档（同值）→ 必须逐项还原——只判 ① 会让「一律回默认」的实现照样绿；
    ///   ③ 「全部恢复默认」的信号补发：值确实变化过时视角/辅瞄/减闪/画面增强四个信号各发一次，
    ///      无变化时一个都不发（消费方是「读一次 + 信号刷新」的缓存型，漏发则表现仍按旧值跑）。</summary>
    private void RunSettingsVersionProbe()
    {
        var gs = GameState.Instance;
        var ok = true;

        _probeViewZoomSignals = 0;
        _probeAimAssistSignals = 0;
        _probeReduceFlashSignals = 0;
        _probeWorldPostFxSignals = 0;
        _onProbeViewZoom = Callable.From<double>(_ => _probeViewZoomSignals++);
        _onProbeAimAssist = Callable.From<StringName>(_ => _probeAimAssistSignals++);
        _onProbeReduceFlash = Callable.From<bool>(_ => _probeReduceFlashSignals++);
        _onProbeWorldPostFx = Callable.From<bool>(_ => _probeWorldPostFxSignals++);
        gs.Connect(GameState.SignalName.ViewZoomChanged, _onProbeViewZoom);
        gs.Connect(GameState.SignalName.AimAssistChanged, _onProbeAimAssist);
        gs.Connect(GameState.SignalName.ReduceFlashChanged, _onProbeReduceFlash);
        gs.Connect(GameState.SignalName.WorldPostFxChanged, _onProbeWorldPostFx);
        _probeSettingsSubscribed = true;

        // 判据的基线必须是出厂档：高于当前的档「按逐字段默认值回退」等价于把整表当空档，
        // 而空档的语义是「保持当前值」——当前值非默认时，判据就落到读入的那份档上了（假绿）。
        gs.ResetAllSettings();
        ok &= WriteUserFile("user://settings.json",
            $"{{\"version\":{Core.Storage.SettingsMigration.CurrentVersion + 1},\"locale\":\"en\","
            + "\"difficulty\":\"hard\",\"view_zoom\":\"large\",\"resolution\":\"1280x720\","
            + "\"custom_width\":1000,\"custom_height\":700,\"aim_assist\":\"high\",\"fps_cap\":\"fps30\","
            + "\"vsync\":false,\"reduce_flash\":true,\"world_post_fx\":false,\"mouse_lock\":false,"
            + "\"shake_scale\":0.2,\"hit_stop_scale\":0.1,\"master_volume\":0.5,\"music_volume\":0.4,"
            + "\"sfx_volume\":0.3,\"joy_aim_speed\":3000.0,\"joy_deadzone\":0.7,\"joy_vibration\":false}");
        ok &= LoadSettingsForProbe("更高版本档");
        ok &= VerifySettingsDefaults("更高版本档读入后");

        // ② 同版对照档：同一组非默认值必须逐项还原
        ok &= WriteUserFile("user://settings.json", ValidSettingsJson);
        ok &= LoadSettingsForProbe("同版对照档");
        ok &= VerifySettingsRestored("同版对照档读入后");

        // ③ 恢复默认的信号补发：当前值确实非默认 → 四个信号各发一次；再复位（无变化）→ 一个都不发
        _probeViewZoomSignals = 0;
        _probeAimAssistSignals = 0;
        _probeReduceFlashSignals = 0;
        _probeWorldPostFxSignals = 0;
        gs.ResetAllSettings();
        if (_probeViewZoomSignals != 1 || _probeAimAssistSignals != 1
            || _probeReduceFlashSignals != 1 || _probeWorldPostFxSignals != 1)
        {
            GD.PushError($"[settings-version-probe] 「全部恢复默认」的补发信号不全（视角 {_probeViewZoomSignals} / "
                + $"辅瞄 {_probeAimAssistSignals} / 减闪 {_probeReduceFlashSignals} / 画面增强 {_probeWorldPostFxSignals}，"
                + "各期望 1）——消费方是「读一次 + 信号刷新」的缓存型，漏发则表现仍按旧值跑");
            ok = false;
        }

        _probeViewZoomSignals = 0;
        _probeAimAssistSignals = 0;
        _probeReduceFlashSignals = 0;
        _probeWorldPostFxSignals = 0;
        gs.ResetAllSettings();
        if (_probeViewZoomSignals + _probeAimAssistSignals + _probeReduceFlashSignals + _probeWorldPostFxSignals != 0)
        {
            GD.PushError($"[settings-version-probe] 值未变化时仍补发了设置信号（视角 {_probeViewZoomSignals} / "
                + $"辅瞄 {_probeAimAssistSignals} / 减闪 {_probeReduceFlashSignals} / 画面增强 {_probeWorldPostFxSignals}）"
                + "——多余重建");
            ok = false;
        }

        if (ok)
        {
            GD.Print("[settings-version-probe] 版本回退与复位信号成立");
        }
    }

    /// <summary>逐项断「已回出厂档」（更高版本档的判据；取值口径与 SettingsService.ResetToDefaults 同源）。</summary>
    private static bool VerifySettingsDefaults(string label)
    {
        var gs = GameState.Instance;
        var ok = true;
        if (gs.Locale != "zh" || gs.Difficulty != new StringName("medium")
            || gs.ViewZoom != new StringName("small") || gs.AimAssistLevel != new StringName("medium")
            || gs.FpsCap != new StringName("60") || !gs.VSync || !gs.MouseLock || gs.ReduceFlash
            || !gs.WorldPostFx || gs.CustomWindowWidth != 1920 || gs.CustomWindowHeight != 1080)
        {
            GD.PushError($"[settings-version-probe] {label} 未逐项回出厂档：locale={gs.Locale} "
                + $"difficulty={gs.Difficulty} view_zoom={gs.ViewZoom} aim_assist={gs.AimAssistLevel} "
                + $"fps_cap={gs.FpsCap} vsync={gs.VSync} mouse_lock={gs.MouseLock} reduce_flash={gs.ReduceFlash} "
                + $"world_post_fx={gs.WorldPostFx} custom={gs.CustomWindowWidth}x{gs.CustomWindowHeight}");
            ok = false;
        }

        if (Math.Abs(gs.MasterVolume - 0.8) > 1e-6 || Math.Abs(gs.ShakeScale - 1.0) > 1e-6
            || Math.Abs(gs.HitStopScale - 1.0) > 1e-6 || Math.Abs(gs.JoyAimSpeed - 1400.0) > 1e-6
            || Math.Abs(gs.JoyDeadzone - 0.2) > 1e-6)
        {
            GD.PushError($"[settings-version-probe] {label} 数值档未回出厂档：master={gs.MasterVolume:0.##} "
                + $"shake={gs.ShakeScale:0.##} hit_stop={gs.HitStopScale:0.##} joy_speed={gs.JoyAimSpeed:0.#} "
                + $"joy_deadzone={gs.JoyDeadzone:0.##}");
            ok = false;
        }

        return ok;
    }

    /// <summary>逐项断「等于同版对照档的取值」（对照段；只判更高版本段会让「一律回默认」照样绿）。</summary>
    private static bool VerifySettingsRestored(string label)
    {
        var gs = GameState.Instance;
        var ok = true;
        if (gs.Locale != "en" || gs.Difficulty != new StringName("hard")
            || gs.ViewZoom != new StringName("large") || gs.AimAssistLevel != new StringName("high")
            || gs.FpsCap != new StringName("fps30") || gs.VSync || gs.MouseLock || !gs.ReduceFlash
            || gs.WorldPostFx || gs.CustomWindowWidth != 1000 || gs.CustomWindowHeight != 700)
        {
            GD.PushError($"[settings-version-probe] {label} 未逐项还原：locale={gs.Locale} "
                + $"difficulty={gs.Difficulty} view_zoom={gs.ViewZoom} aim_assist={gs.AimAssistLevel} "
                + $"fps_cap={gs.FpsCap} vsync={gs.VSync} mouse_lock={gs.MouseLock} reduce_flash={gs.ReduceFlash} "
                + $"world_post_fx={gs.WorldPostFx} custom={gs.CustomWindowWidth}x{gs.CustomWindowHeight}");
            ok = false;
        }

        if (Math.Abs(gs.MasterVolume - 0.5) > 1e-6 || Math.Abs(gs.ShakeScale - 0.2) > 1e-6
            || Math.Abs(gs.HitStopScale - 0.1) > 1e-6 || Math.Abs(gs.JoyAimSpeed - 3000.0) > 1e-6
            || Math.Abs(gs.JoyDeadzone - 0.7) > 1e-6)
        {
            GD.PushError($"[settings-version-probe] {label} 数值档未还原：master={gs.MasterVolume:0.##} "
                + $"shake={gs.ShakeScale:0.##} hit_stop={gs.HitStopScale:0.##} joy_speed={gs.JoyAimSpeed:0.#} "
                + $"joy_deadzone={gs.JoyDeadzone:0.##}");
            ok = false;
        }

        return ok;
    }

    /// <summary>本探针的读设置（失败即记一条错，返回 false 供调用方累计）。</summary>
    private static bool LoadSettingsForProbe(string label)
    {
        try
        {
            GameState.Instance.LoadSettings();
            return true;
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[settings-version-probe] {label} 读入抛异常：{ex.GetType().Name} {ex.Message}");
            return false;
        }
    }

    /// <summary>死亡删档的本局门控探针：非本局（教程/标题屏）死亡不得删玩家真实检查点。
    ///
    /// 为什么必须探：删档钩子挂在全局 PlayerDied 上、且 PlayerDied 无场景上下文——教程死亡走同
    /// 一信号并把它当预期终态，无门控就是「玩教程顺手抹掉玩家存档」（C 继续上次出击凭空消失），
    /// 静默且不可逆。本趟跑在探针宿主里（_runActive 保持 false，同教程/标题屏语义），
    /// 写一份检查点后发 PlayerDied：文件必须原封不动；再把门控置真（同生产 Main 路径）发一次，
    /// 文件必须被删——前半段判「误伤」，后半段判「该删的仍然删」，缺一半都判不出。
    /// 直接发信号而非 Player.Die()：本探针只判删档钩子，不引入死亡结算/结算页的一串副作用。</summary>
    private void RunDeathGateProbe()
    {
        var gs = GameState.Instance;
        gs.SetRunActive(false); // 显式声明本趟语义：非本局（同教程/标题屏）
        var runPath = ProjectSettings.GlobalizePath("user://run.json");
        if (!WriteUserFile("user://run.json", ValidRunJson))
        {
            return;
        }

        // 非本局（探针宿主 _runActive 为假，同教程/标题屏）：死亡不得删档
        gs.EmitSignal(GameState.SignalName.PlayerDied);
        if (!System.IO.File.Exists(runPath))
        {
            GD.PushError("[death-gate-probe] 非本局死亡删掉了玩家检查点（教程/标题屏死亡会误伤存档）");
            return;
        }

        // 真实本局（生产 Main 路径置真）：死亡必须删档
        gs.SetRunActive(true);
        gs.EmitSignal(GameState.SignalName.PlayerDied);
        if (System.IO.File.Exists(runPath))
        {
            GD.PushError("[death-gate-probe] 本局死亡未删档——检查点可无限读回（必死曲线紧张感丢失）");
            return;
        }

        GD.Print("[death-gate-probe] 非本局不删档/本局删档均生效");
    }

    /// <summary>难度信号观测（读档补发；值应为存档 difficulty_multiplier）。</summary>
    private void OnProbeDifficultyChanged(float value) => _probeDifficultySeen = value;

    /// <summary>分数信号观测：回调里读到的时间须已是存档值（信号先于还原会让订阅方读到复位值 0）。</summary>
    private void OnProbeScoreChanged(int score) => _probeRunTimeAtScore = GameState.Instance.RunTime;

    /// <summary>健康值观测（读档补发信号）。</summary>
    private void OnProbeHealthChanged(float health) => _probeHealthSeen = health;

    /// <summary>手柄设置观测（读档补发通知）。</summary>
    private void OnProbeJoySettingsChanged(double aimSpeed, double deadzone)
    {
        _probeJoyAimSpeedSeen = aimSpeed;
        _probeJoyDeadzoneSeen = deadzone;
    }

    /// <summary>写用户目录文件（探针自备存档）；写不出即判失败，不静默跳过整个探针。</summary>
    private static bool WriteUserFile(string path, string content)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PushError($"[hostile-save-probe] 无法写出 {path}：{Godot.FileAccess.GetOpenError()}");
            return false;
        }

        file.StoreString(content);
        return true;
    }
}
#endif
