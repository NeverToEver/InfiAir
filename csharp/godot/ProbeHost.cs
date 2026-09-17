using Godot;
using InfiAir.Core.Audio;
using InfiAir.Core.Missions;
using InfiAir.Core.Practice;
using InfiAir.Core.Progression;
using InfiAir.Core.Text;

// 探针宿主整体条件编译：仅编辑器/调试构建（Debug 定义 TOOLS、ExportDebug 定义 DEBUG）编入，
// 发布导出（ExportRelease 两者皆无）整类不进 InfiAir.dll——测试设施不随发布包分发。
// scene 侧 scenes/probe_host.tscn 与源码侧本文件/ProbeHost.Play.cs、以及测试工程 csharp/tests/* 已在
// export_presets.cfg 的 exclude_filter 排除，二者一致（实测导出日志：343 条打包条目中无探针/测试）。
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

    /// <summary>增幅缓存复用趟的诊断帧：实测复用首现于第 1200–1500 帧，故在 1800 帧上若仍
    /// 未见复用就打一条诊断（跑到哪、回收多少、场上几只），把「没打完成标记」变成可定位的信息。</summary>
    private const int ReuseDiagnosticFrame = 1800;

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

    /// <summary>返航探针「继续出击」后等曲目上下文切换的帧上限：轨道打击命中帧在
    /// impact_at 0.56 × effect duration 1.4s ≈ 0.78s（47 帧），留 2 倍余量。</summary>
    private const int ReturnProbeResumeFrames = 120;

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

    /// <summary>Boss 探针「转场清弹」的观察窗口（帧）：注册表移除挂在帧末，窗口只吸收这一帧级时序差。</summary>
    private const int BossProbeClearSettleFrames = 5;

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
    /// 与数值档（version / custom_* / joy_* / 音量 / 顿帧与震动强度）读点；
    /// 无障碍档的布尔的取值刻意用宽松转换会读成 true 的形态（"on"/"no"/1）——非 bool 一律不认，
    /// 手改档不得把开关打开。</summary>
    private const string HostileSettingsJson =
        """{"version":"four","locale":123,"difficulty":[],"view_zoom":{},"window_mode":5,"resolution":true,"aim_assist":[],"fps_cap":{},"ctrl_toggle_mode":"yes","custom_width":"wide","custom_height":[],"joy_aim_speed":"fast","joy_deadzone":[],"vsync":"on","high_contrast":"on","reduce_flash":0,"world_post_fx":"no","mouse_lock":1,"master_volume":"loud","music_volume":{},"sfx_volume":[],"shake_scale":"lots","hit_stop_scale":[],"tutorial_done":"yes","joy_vibration":"off"}""";

    /// <summary>正常 run.json：与下方断言逐项对齐（只判恶意档会让「守卫一律回退」的实现照样绿）。
    /// augments 与 talent_levels 必须一致——两者都来自同一局的写出，生产档里 extra_life 两侧都有
    /// （talent 层级决定生命上限，combat 步的 augments 是被消耗盾层等运行态）。</summary>
    private const string ValidRunJson =
        """{"version":1,"score":900,"kills":7,"boss_kills":2,"combo":5,"milestone_count":4,"run_time":300.0,"difficulty_multiplier":1.6,"dda_timer":12.5,"difficulty_time_step":2,"health":42.5,"augments":{"extra_life":1},"talent_levels":{"extra_life":1},"talent_overcharged":[],"talent_route":"","talent_reset_tokens":1,"talent_bonus_overcharge_slots":1,"talent_cache_values":[],"rp":6,"refresh_points":2,"missions":{"kill_15":{"progress":3,"claimed":false,"goal":15,"baseline":1}},"last_kind_value":{"kill":12}}""";

    /// <summary>正常 settings.json：同前，取值刻意全非默认（回退实现会把它们全部读成默认而判红）；
    /// 四个开关（减闪 / 高对比 / 画面增强 / 鼠标锁定）同样取非默认值——高对比进表也是
    /// 「全部恢复默认」补发信号那半判据的前提（值没变过就无从判漏发）。</summary>
    private const string ValidSettingsJson =
        """{"version":4,"locale":"en","difficulty":"hard","view_zoom":"large","window_mode":"windowed","resolution":"1280x720","custom_width":1000,"custom_height":700,"aim_assist":"high","fps_cap":"fps30","vsync":false,"joy_aim_speed":3000.0,"joy_deadzone":0.7,"joy_vibration":false,"master_volume":0.5,"music_volume":0.4,"sfx_volume":0.3,"shake_scale":0.2,"hit_stop_scale":0.1,"tutorial_done":true,"ctrl_toggle_mode":true,"shift_toggle_mode":true,"fire_toggle_mode":true,"reduce_flash":true,"high_contrast":true,"world_post_fx":false,"mouse_lock":false}""";

    /// <summary>正常档还原断言里 health 的期望值（ValidRunJson 的 health 字段）。</summary>
    private const float AssertedHealth = 42.5f;

    /// <summary>损坏的 best.json（截断的 JSON）：读入即被隔离，本趟断「隔离之后仍允许写记录」。
    /// 这是实测过的坏法——损坏档让记录写门槛为假，整场会话的记录再也不落盘（静默丢记录）。</summary>
    private const string CorruptBestJson = "{\"version\":1,\"survived_seconds\":";

    /// <summary>版本不符的 best.json：语法合法、字段齐全（与编解码器同键名），读数刻意远好于本趟——
    /// 实现若「认了」这本档，写盘要么被挡、要么把未来档的 9999s 当成玩家成绩，两个方向都在断言里判红。</summary>
    private static string FutureVersionBestJson() => GdFormat.Format(
        "{\"version\":%d,\"survived_seconds\":9999.0,\"boss_kills\":99,\"max_difficulty\":9.0,\"goal_achieved\":1}",
        BestRecordCodec.Version + 1);

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

    /// <summary>「全部恢复默认」的信号补发观测（设置版本探针）：五个「读一次 + 信号刷新」的缓存型
    /// 设置项各计一次（视角 / 辅瞄 / 减闪 / 高对比 / 画面增强）——高对比的消费方是 Main 的
    /// 在飞敌弹重挑贴图，漏发则复位后场上残留高对比外观。</summary>
    private int _probeViewZoomSignals;

    private int _probeAimAssistSignals;
    private int _probeReduceFlashSignals;
    private int _probeHighContrastSignals;
    private int _probeWorldPostFxSignals;
    private Callable _onProbeViewZoom;
    private Callable _onProbeAimAssist;
    private Callable _onProbeReduceFlash;
    private Callable _onProbeHighContrast;
    private Callable _onProbeWorldPostFx;
    private bool _probeSettingsSubscribed;

    /// <summary>存档还原探针（--save-restore-probe）的临时用户目录由 check_smoke.sh 隔离。</summary>
    private bool _saveRestoreProbe;

    /// <summary>设置版本探针（--settings-version-probe）。</summary>
    private bool _settingsVersionProbe;

    /// <summary>提前离舰蓄力的终局清理探针（--early-leave-clear-probe）。</summary>
    private bool _earlyProbe;

    private int _earlyStage;
    private int _earlyChargeFrames;
    private Hud? _earlyHud;

    /// <summary>提前离舰蓄力的观测上限（帧）：生产 early_hold_time 2s = 120 帧，取 2.5 倍余量。</summary>
    private const int EarlyLeaveProbeChargeBoundFrames = 300;

    /// <summary>设置页探针里两处频闪的采样窗口（帧）：轮盘开机物化 0.62s、横幅闪烁半周期 ≤0.25s，
    /// 取 30 帧（0.5s）覆盖至少一个完整明暗周期，留出轮询与帧序余量。</summary>
    private const int SettingsFlashWindowFrames = 30;

    private const int SettingsBannerWindowFrames = 45;

    /// <summary>设置页探针的阶段与观测态（0 起 → 4 收尾；sawBlink 是正对照的判据）。</summary>
    private int _settingsProbeStep;

    private int _settingsProbeFrame;
    private bool _settingsProbeSawBlink;
    private RadialWheel? _settingsWheel;

    /// <summary>模态退场鼠标命中断言的步序与取样按钮表（0 未开始 / 1 待退场回调 / 2 待重开复查）。</summary>
    private int _modalStep;

    private readonly List<Button> _modalButtons = new();
    private readonly List<Control.MouseFilterEnum> _modalBaseline = new();

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
    private bool _augmentCacheDiagnosed;
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

    /// <summary>homing 判定的准星偏角（度）：大于 high 档弱追踪半角 10°（弱追踪不得替玩家把弹拨过去）、
    /// 小于制导锁定锥半角 22°（制导仍能锁定）。</summary>
    private const float AugHomingProbeOffsetDeg = 15.0f;

    /// <summary>增幅判定的离散帧窗：等出膛弹飞完 / 等冲刺打击节拍落下（每级伤害按 0.12s 节流结算）。</summary>
    private const int AugProbeSettleFrames = 40;

    private static readonly StringName ProbeActFire = new("fire");
    private bool _fogActive;
    private bool _fogSubscribed;
    private int _fogStartFrame;
    private double _fogDuration;
    private int _returnStage;
    private int _returnChargeFrames;
    private int _returnStartFrame;

    /// <summary>「继续出击」发出的帧（段 5 等曲目上下文随轨道打击命中帧撤下）。</summary>
    private int _returnResumeFrame;
    private ulong _returnWallStart;
    private float _returnGrace;
    private int _frame;
    private int _activeFrame;
    private int _fuelStep;
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

    // ---------------- 练习模式探针（--practice-probe）与记录读出探针（--best-record-probe） ----------------

    /// <summary>练习局探针步序（0 面板自检 → 1 等入场结束 → 2 等直选 Boss 出场 → 3 等直选遭遇启动 →
    /// 4 击杀玩家 → 5 断不落盘）。显式步序而非一串布尔：任一步失败都打 ::error 且不打完成标记。</summary>
    private int _practiceStage;

    /// <summary>练习模式探针开关（--practice-probe[=boss,diff,enc]）。</summary>
    private bool _practiceProbe;

    /// <summary>记录读出探针开关（--best-record-probe）。</summary>
    private bool _bestRecordProbe;

    private int _practiceStageFrame;

    /// <summary>练习探针的直选设置（命令行给，默认 Ⅱ型 · 难 · 精英炮塔）。</summary>
    private PracticeSetup _practiceSetup = new(2, 2, 1);

    private Boss? _practiceBoss;

    /// <summary>面板确认键交出的设置（判据：与面板此刻显示的一致，见 RunPracticePanelCheck）。</summary>
    private PracticeSetup? _practicePanelConfirmed;

    /// <summary>练习探针预置的检查点路径（练习死亡后必须原封不动）。</summary>
    private string _practiceRunPath = "";

    /// <summary>练习探针的直选请求驱动（与练习宿主同一个类：探针覆盖到的就是生产落地路径）。</summary>
    private PracticeDriver? _driver;

    /// <summary>练习探针是否已订阅 BossSpawned（_ExitTree 配对退订）。</summary>
    private bool _practiceSubscribed;

    /// <summary>练习探针的等待上限（帧）：直选请求受理 / Boss 出场 / 直选遭遇启动 / 死亡结算落定。</summary>
    private const int PracticeProbeRequestFrames = 420;

    private const int PracticeProbeBossFrames = 600;

    private const int PracticeProbeEventFrames = 600;

    private const int PracticeProbeDeathSettleFrames = 30;

    /// <summary>记录探针每步的落定等待帧数（死亡会暂停树，删档/写记录的帧末簿记要跑完）。</summary>
    private const int BestRecordSettleFrames = 6;

    /// <summary>记录探针第一步写下的本局存档路径（记录与本局存档分区：死亡删档只动前者）。</summary>
    private string _runPathForBest = "";

    /// <summary>记录读出探针步序（0 置两局前提 → 1 第一局 → 2 断记录落盘 → 3 第二局更差 → 4 断不回退
    /// → 5/6 断「刷新记录」判定的两个复位点 → 7/8 断损坏档与版本不符档读入后仍允许写）。</summary>
    private int _bestStage;

    private int _bestStageFrame;

    /// <summary>第一局落定的读数（第二局的「更差」前提与「不回退」基准都对着它判）。</summary>
    private BestRecord _bestFirstRun = BestRecord.Empty;

    /// <summary>复位段里最后一次真实局刷新出的记录（练习终结段的「记录不受影响」基准）。</summary>
    private BestRecord _bestImprovedRun = BestRecord.Empty;

    /// <summary>记录文件绝对路径（存在性与读回判定）。</summary>
    private string _bestPath = "";

    /// <summary>截图序列的一拍在捕获前要做的事（页面切换之外的开关操作）。</summary>
    private enum ShotAction
    {
        /// <summary>不做额外动作。</summary>
        None,

        /// <summary>开练习设置面板（三行取面板此刻的取值＝刚打开的样子）。</summary>
        PracticeOpen,

        /// <summary>把练习面板三行切到本趟直选设置（按下开始键前的那一步，见 SelectPracticeRowsForShots）。</summary>
        PracticeSelect,

        /// <summary>按下面板的开始键（走生产单口 EnterPractice 换场到 scenes/practice.tscn）。</summary>
        PracticeConfirm,

        /// <summary>经生产单口回标题屏（入口聚焦拍的布景）。</summary>
        TitleReturn,

        /// <summary>注入手柄摇杆导航聚焦教程入口（焦点提亮是入口态的实拍面）。</summary>
        TitleFocus,
    }

    /// <summary>截图序列：帧号 → 先切到哪一页（空＝不切）→ 捕获名（空＝只切不捕）→ 本步是否击杀玩家
    /// → 本步的开关动作。固定帧捕获——序列本身即「要覆盖哪些视觉面」的清单。切页与捕获隔开若干帧，
    /// 等新页构建并渲染完成（捕获取的是已渲染帧，同帧切页会捕到上一帧）。
    /// 切页与捕获间留 ~0.5s，避开设置页交叉淡入/入场动画（捕在转场中段画面未成形）。
    ///
    /// 练习那两拍的落位与理由：面板只有行内文字随直选变化（刚打开的选择态与三行选定后的选定态），
    /// 两态之间的画面差**小于互异判据**（实测签名最大差 2 < MinPageDiff——两张同底图的面板图会被
    /// 判「画面几乎相同」）。故第二拍不取面板的另一个状态，而取按下开始键之后的落点：练习局开局
    /// 的画面（所选 Boss / 起始难度档 / 遭遇都已在局里）。两拍因此是「选择态面板」与「确认之后」，
    /// 互异判据判得到东西（实测最小 124），整条练习入口也照生产链走全了。</summary>
    private static readonly (int Frame, string Page, string Shot, bool Kill, ShotAction Act)[] ShotPlan =
    {
        (90, "", "hud", false, ShotAction.None),
        (100, "gameplay", "", false, ShotAction.None),
        (130, "", "settings-gameplay", false, ShotAction.None),
        (140, "display", "", false, ShotAction.None),
        (170, "", "settings-display", false, ShotAction.None),
        (180, "audio", "", false, ShotAction.None),
        (210, "", "settings-audio", false, ShotAction.None),
        (220, "about", "", false, ShotAction.None),
        (250, "", "settings-about", false, ShotAction.None),
        (260, "controls", "", false, ShotAction.None),
        (290, "", "settings-controls", false, ShotAction.None),
        // 死亡结算页（人工验收项「本局记录读出过目」的实拍面）：先击杀玩家，隔 ~0.6s 再捕——
        // 遮罩 150ms 淡入 + 面板 200ms 入场 + 轮盘 500ms 滑入，捕在中段刚好是页面成形后的样子。
        // 捕在死亡回放（3s 幽灵弹幕，ZIndex 在 HUD 之下）播完之前，属画面的一部分，不影响自检。
        (292, "", "", true, ShotAction.None),
        (330, "", "gameover", false, ShotAction.None),
        // 练习面板选择态：结算页轮盘「练习模式」那处生产入口的就地开面板背景（GameOverUi.
        // OpenPracticePanel），三行取面板初值（默认＝不指定 Boss / 中档 / 不指定遭遇）。
        // 捕在一片已成形、无动画的底图上——本图判的是面板自身排版与可读性，不判它压在哪张底图上。
        (340, "", "", false, ShotAction.PracticeOpen),
        (380, "", "practice-panel", false, ShotAction.None),
        // 选定三行后按确认（走生产单口换场），捕练习局开局的画面：所选 Boss 与遭遇由生产链
        // 逐帧请出，起始难度档也已落到 HUD 上——面板上选的东西有没有真的出现在局里，这一张是实拍面。
        (382, "", "", false, ShotAction.PracticeSelect),
        (384, "", "", false, ShotAction.PracticeConfirm),
        (534, "", "practice-run", false, ShotAction.None),
        // 标题屏入口聚焦态（手柄可达入口的实拍面）：回标题后注入摇杆导航——教程入口被聚焦提亮，
        // 焦点态若无可见反馈（手柄玩家看不见自己在哪）这张图一眼可见；两段间留足标题加载与输入守卫
        (560, "", "", false, ShotAction.TitleReturn),
        (650, "", "", false, ShotAction.TitleFocus),
        (678, "", "title-entries", false, ShotAction.None),
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

        // 练习探针要在 Main._Ready 之前注入练习口径（与 MarkHostDriven 同一时序约束），
        // 而开关解析在 _Ready——故这里先扫一遍用户参数。
        // 练习探针**不调 MarkHostDriven**：练习是生产模式（本局时钟、事件触发、结算页照走），
        // 探针只把它按直选条件起一局，其余判定仍由生产链做。
        var practiceArg = FindUserArg("--practice-probe");
        if (practiceArg != null)
        {
            _practiceSetup = ParsePracticeSetup(practiceArg);
            GameState.Instance.BeginPractice(_practiceSetup);
            main.MarkPracticeRun();
            return;
        }

        main.MarkHostDriven();
    }

    /// <summary>命令行用户参数里查开关：返回整条实参（形如 <c>--x</c> 或 <c>--x=a</c>），无则 null。</summary>
    private static string? FindUserArg(string prefix)
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == prefix || arg.StartsWith(prefix + "=", System.StringComparison.Ordinal))
            {
                return arg;
            }
        }

        return null;
    }

    /// <summary>解析练习直选设置（<c>--practice-probe=boss,diff,enc</c>）：三项索引为位置参数，
    /// 缺省/解析不出即用整条默认设置——索引越界由 core PracticeSetup 归一，不在此再写一套钳制。</summary>
    private static PracticeSetup ParsePracticeSetup(string arg)
    {
        var eq = arg.IndexOf('=');
        if (eq < 0)
        {
            return new PracticeSetup(2, 2, 1);
        }

        var parts = arg[(eq + 1)..].Split(',');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out var boss)
            || !int.TryParse(parts[1], out var difficulty)
            || !int.TryParse(parts[2], out var encounter))
        {
            GD.PushError($"[practice-probe] 开关取值解析失败（{arg}）——按默认设置跑");
            return new PracticeSetup(2, 2, 1);
        }

        return new PracticeSetup(boss, difficulty, encounter);
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
            else if (arg == "--autoplay-probe")
            {
                _autoplayProbe = true;
            }
            else if (arg.StartsWith("--autoplay-probe=", System.StringComparison.Ordinal))
            {
                // 模拟时长（秒）；解析不出正整数就按默认时长跑——不静默变成 0 帧空跑
                if (int.TryParse(arg["--autoplay-probe=".Length..], out var seconds) && seconds > 0)
                {
                    _autoplaySeconds = seconds;
                }

                _autoplayProbe = true;
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
            else if (arg == "--practice-probe" || arg.StartsWith("--practice-probe=", System.StringComparison.Ordinal))
            {
                // 练习口径已在 _EnterTree 注入（早于 Main._Ready）；这里只记下开关，
                // 取值同一处解析，避免两遍解析各写一套（第二遍只用于拿到设置，不再注入）。
                _practiceProbe = true;
                _practiceSetup = ParsePracticeSetup(arg);
            }
            else if (arg == "--best-record-probe")
            {
                _bestRecordProbe = true;
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
            else if (arg == "--early-leave-probe")
            {
                _earlyProbe = true;
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
            else if (arg == "--tutorial-probe")
            {
                _tutorialProbe = true;
            }
        }

        if (_autoplayProbe && expectUserDir.Length == 0)
        {
            // 安全互锁：模拟游玩真跑一局（会写设置、可能死亡删档），必须在隔离用户目录里跑——
            // 不带 --expect-user-dir 时拒绝启动，避免动到开发者真实的检查点与设置。
            GD.PushError("[autoplay-probe] 未给 --expect-user-dir：模拟游玩会写 user://（含死亡删档），"
                + "拒绝在开发者真实用户目录下开跑；用临时目录并把 APPDATA/XDG_DATA_HOME/HOME 一起指过去");
            _autoplayProbe = false;
        }

        if (_tutorialProbe && expectUserDir.Length == 0)
        {
            // 安全互锁（同 autoplay）：本趟会写 user://（教程检查点与改键落 settings.json），
            // 不带 --expect-user-dir 时拒绝启动，避免动到开发者真实设置。
            GD.PushError("[tutorial-probe] 未给 --expect-user-dir：本趟会写 user://（教程检查点与改键），"
                + "拒绝在开发者真实用户目录下开跑；用临时目录并把 APPDATA/XDG_DATA_HOME/HOME 一起指过去");
            _tutorialProbe = false;
        }

        VerifyUserDirIsolation(expectUserDir);

        if (_tutorialProbe)
        {
            // 教程自成一条生产入口（独立场景、不经 Main），故本趟不复用宿主里的 Main：
            // 驱动节点挂根后切到生产教程场景，本节点随场景切换释放（驱动跨重载存活）。
            // 必须延迟到本帧装载结束：_Ready 期间父节点仍在加子节点，此刻挂根/切场景会被引擎拒绝
            // （Parent node is busy adding/removing children），切场景失败而本趟静默跑空。
            Callable.From(StartTutorialProbe).CallDeferred();
            return;
        }

        if (_eventId.Length > 0 || _feelProbe || _longProbe || _fogProbe || _fogInterruptProbe || _returnProbe
            || _bossProbe || _dockProbe || _killAllProbe || _augmentCacheProbe || _earlyProbe || _autoplayProbe
            || _practiceProbe || _bestRecordProbe)
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
        if (_autoplayProbe || _autoplayFireHeld != 0 || _autoplayMoveX != 0 || _autoplayMoveY != 0)
        {
            // 模拟游玩注入的是生产输入动作：退出树必须收回，否则动作残留会传给后续场景（同进程重开一局）
            ReleaseAutoplayInputs();
        }

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

        if (_practiceSubscribed)
        {
            _practiceSubscribed = false;
            _spawner.BossSpawned -= OnPracticeProbeBossSpawned;
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
                DisconnectSettingSignal(gsSet, GameState.SignalName.HighContrastChanged, _onProbeHighContrast);
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

        if (_earlyProbe)
        {
            TickEarlyLeaveProbe();
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

        if (_autoplayProbe)
        {
            TickAutoplayProbe();
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

        if (_practiceProbe)
        {
            TickPracticeProbe();
            return;
        }

        if (_bestRecordProbe)
        {
            TickBestRecordProbe();
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

    /// <summary>练习面板：从开面板那拍起活到确认拍（面板那张图捕完还要切三行再按确认），故跨拍持有实例。</summary>
    private PracticePanel? _shotPracticePanel;

    /// <summary>截图驱动：按计划帧切页/开关/捕获；序列结束后做两条廉价自检（非空白、页面互异），
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

            switch (step.Act)
            {
                case ShotAction.PracticeOpen:
                    OpenPracticePanelForShots();
                    break;

                case ShotAction.PracticeSelect:
                    SelectPracticeRowsForShots();
                    break;

                case ShotAction.PracticeConfirm:
                    ConfirmPracticePanelForShots();
                    break;

                case ShotAction.TitleReturn:
                    GameState.Instance.ExitToTitle();
                    break;

                case ShotAction.TitleFocus:
                    InjectTitleNavEvent();
                    break;
            }

            if (step.Kill)
            {
                KillPlayerForShots();
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

    /// <summary>标题屏摇杆导航注入（与 TitleNavProbeDriver 同款：轴立即归零防状态残留）。</summary>
    private static void InjectTitleNavEvent()
    {
        Input.ParseInputEvent(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftY, AxisValue = 0.8f });
        Input.ParseInputEvent(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftY, AxisValue = 0.0f });
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

    /// <summary>截图趟的击杀（死亡结算页那张图的前置）：把本局置为活跃——宿主驱动默认非本局，
    /// 不置活跃则记录不会写、结算页的记录行只能显示「暂无记录」，而这一张图正是「本局记录读出」的
    /// 实拍面；随后经生产死亡路径击杀（结算页由 GameOverUi 的 PlayerDied 订阅打开，探针不直接建 UI）。
    /// 击杀后树被暂停，本宿主 ProcessMode=Always 照常推进；死亡回放 3s 未走完即捕，属画面的一部分。</summary>
    private void KillPlayerForShots()
    {
        GameState.Instance.SetRunActive(true);
        _player.Die();
    }

    /// <summary>练习面板那两拍的前置：挂生产面板类（与 --practice-probe 的
    /// <see cref="RunPracticePanelCheck"/> 同一条生产用法——面板没有独立场景，两处正式入口
    /// 都是就地 <c>new PracticePanel()</c> 加进树，本探针不另造测试专用面板）。
    /// 面板自己置 ProcessMode=Always，死亡把树暂停后照常入场与收输入。</summary>
    private void OpenPracticePanelForShots()
    {
        if (_shotPracticePanel != null)
        {
            return;
        }

        _shotPracticePanel = new PracticePanel();
        // 确认出口按两处生产入口同一行接法（TitleScreen / GameOverUi 的 OpenPracticePanel）：
        // 换场与复位口径收在 EnterPractice 单口里，本探针不另写一套。
        _shotPracticePanel.StartRequested += setup => GameState.Instance.EnterPractice(setup);
        AddChild(_shotPracticePanel);
    }

    /// <summary>按确认前把三行切到**文字最长的直选组合**（Ⅲ型 · 母舰级 / 难 / 精英炮塔阵地，取 core
    /// 映射下最长的那几条文案）：面板那张图上是三行初值，这一组则决定了练习局里出现什么，
    /// 也让「选的内容进不了局」这类坏法在练习局那张图上有实拍面。
    /// 走面板自己的环形切换口（行按钮与左右方向键同一出口），步长由面板此刻的取值反推，
    /// 不假定初值——面板会记住上一次用过的设置。</summary>
    private void SelectPracticeRowsForShots()
    {
        if (_shotPracticePanel == null)
        {
            // 没面板时本拍仍会捕到底图：张数入账、非空白与互异也都过得了（底图是成形页面），
            // 序列顺序写错会静默变成「多捕一张底图」——故这里显式报错（门禁按日志错误正则判红）
            GD.PushError("[shot-probe] 练习面板未建起来，选定态拍无面板可切——序列顺序写反了？");
            return;
        }

        var target = new PracticeSetup(3, 2, 1);
        var now = _shotPracticePanel.Current;
        _shotPracticePanel.Cycle(0, target.BossType - now.BossType);
        _shotPracticePanel.Cycle(1, target.DifficultyIndex - now.DifficultyIndex);
        _shotPracticePanel.Cycle(2, target.EncounterIndex - now.EncounterIndex);
    }

    /// <summary>练习面板退场：后续拍（设置页、结算页）不能被它盖住。不走面板的 Close 动效——
    /// 那会多留一段淡出中的画面（探针要的是「面板不在」的确定态，不是转场）。</summary>
    private void FreePracticePanelForShots()
    {
        _shotPracticePanel?.QueueFree();
        _shotPracticePanel = null;
    }

    /// <summary>按下练习面板的开始键：走面板自己的确认出口（StartRequested 由本探针按两处生产入口
    /// 同一行接法接到 GameState.EnterPractice），换场到 scenes/practice.tscn 开一局练习。
    ///
    /// 换场释放的是「当前场景」，而探针宿主正是当前场景（内嵌的 Main 是它的子节点）——不先交出这个
    /// 身份，宿主就随换场一起被释放，练习局开局的画面再没人捕。故先让出身份、并照生产语义让旧局退场
    /// （换场本来释放的就是它），宿主留在根上把计划走完。这是探针的取像需要，生产路径一字未改：
    /// Main 与练习场景都按生产语义跑，EnterPractice 也仍是那个单口。</summary>
    private void ConfirmPracticePanelForShots()
    {
        if (_shotPracticePanel == null)
        {
            GD.PushError("[shot-probe] 练习面板未建起来，确认拍按不下开始键——序列顺序写反了？");
            return;
        }

        GetTree().CurrentScene = null!;
        _main.QueueFree();
        _shotPracticePanel.Confirm();
        // 生产里面板是旧场景的子节点，随换场一起消失（面板自己不退场）；宿主不随换场消失，
        // 故这里替它做这件事，否则面板会一直盖在练习局画面上。
        FreePracticePanelForShots();
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
            settings.ShowSettings(null); // 每次开页都重放 AutoplayBoot
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

            _settingsProbeStep = 5;
            _modalStep = 0;
        }

        if (_settingsProbeStep == 5)
        {
            if (!VerifyHighContrastBullets())
            {
                _settingsProbe = false;
                return;
            }

            _settingsProbeStep = 6;
        }

        if (_settingsProbeStep == 6)
        {
            TickModalCloseProbe(settings);
        }
    }

    /// <summary>高对比弹体接线的两半判据（借设置趟顺带覆盖，不额外起趟）。
    ///
    /// 为什么必须判：开关置位只是把设置值改了，玩家看到的那张图由 Main 的重挑接线决定——
    /// 只判设置值会与实现同源（删掉重挑调用照样绿），而表现是场上的弹保持旧外观直到各自寿命
    /// 到期（半场两种外观）；「全部恢复默认」那条补发接线同理（无变化不发，变化过必须发）。
    /// 两半互补：置位后在飞敌弹必须换成高对比档（置位前先断原档作正对照——少了正对照，
    /// 「一律高对比」的实现照样绿），关回后必须退回原档（少了这半，关掉开关后残留高对比外观）。
    /// 判据对象是一枚经生产对象池发射的敌弹：在飞、已登记进敌弹注册表，贴图读的是实际贴的那张。</summary>
    private bool VerifyHighContrastBullets()
    {
        var gs = GameState.Instance;
        var pool = gs.BulletPool;
        if (pool == null)
        {
            GD.PushError("[settings-probe] 子弹对象池不存在——在飞敌弹的高对比贴图判据取不到");
            return false;
        }

        gs.SetHighContrast(false); // 起点＝原档（正对照）
        // 从视口左下角向上射一枚慢速敌弹：与玩家所在的中路拉开，无头局撞不到玩家（判据不该
        // 改变本趟其余部分的存活叙事）
        var bullet = pool.Fire(new Vector2(0.0f, -1.0f), 60.0f, 1, false);
        if (bullet == null || !GodotObject.IsInstanceValid(bullet) || !gs.EnemyBullets.Contains(bullet))
        {
            GD.PushError("[settings-probe] 敌弹未发射或未登记进敌弹注册表——高对比接线的判据对象取不到");
            return false;
        }

        var normal = gs.BulletEnemyTex;
        var contrast = gs.BulletEnemyContrastTex;
        var ok = true;
        if (normal == null || contrast == null || normal == contrast)
        {
            GD.PushError("[settings-probe] 敌弹两档贴图为空或同源——判据两边可能是同一张图，档位判不出来");
            ok = false;
        }
        else if (bullet.SpriteTexture() != normal)
        {
            GD.PushError("[settings-probe] 新发射的敌弹贴图不等于原档（开关未打开时）——外观档与设置不同源");
            ok = false;
        }

        gs.SetHighContrast(true);
        if (bullet.SpriteTexture() != contrast)
        {
            GD.PushError("[settings-probe] 打开高对比后在飞敌弹仍是原档贴图——开关只改了设置值，"
                + "场上的弹保持旧外观（要等各自寿命到期才换）");
            ok = false;
        }

        gs.SetHighContrast(false);
        if (bullet.SpriteTexture() != normal)
        {
            GD.PushError("[settings-probe] 关回高对比后在飞敌弹未退回原档贴图——关掉开关后场上残留高对比外观");
            ok = false;
        }

        // 这枚弹只为判据而放：走生产回收口归还对象池，不留到后续步骤（模态退场等）里
        pool.Release(bullet);
        gs.SetHighContrast(false);
        return ok;
    }

    /// <summary>模态退场期的鼠标命中断言（设置页趟尾段，借 <c>Back()</c> 的真实退场链）。
    ///
    /// 为什么必须判：退场动画期间的视觉残影与「谁在接鼠标」是两件事——父级置 Ignore 只让父节点
    /// 自己不返回，子按钮照旧被命中（Viewport 命中先递归子节点、后判父级），故摘除必须**递归整棵子树**；
    /// 只摘父级/只摘面板时，退场画面会截获已经交还给下一层的点击，且不崩不报错。
    /// 判据取**全部按钮**（不是「随便找一个」）：只摘面板的写法会让面板外的导航按钮照旧可命中，
    /// 取单个按钮时恰好取到面板内的那个就漏判（实测过这一形态）。
    /// 三段，缺一段都能被另一种坏法蒙过：退场当帧全部按钮即 Ignore、退场回调后逐个还原到基线、
    /// 重开后全部按钮即可被命中（还原漏写时第二次开页的按钮会永久不吃点击）。</summary>
    private bool TickModalCloseProbe(SettingsUi settings)
    {
        if (_modalStep == 0)
        {
            _modalButtons.Clear();
            _modalBaseline.Clear();
            CollectButtons(settings, _modalButtons);
            if (_modalButtons.Count == 0)
            {
                GD.PushError("[settings-probe] 设置页子树里找不到按钮——模态退场的鼠标命中断言取不到判据");
                _settingsProbe = false;
                return false;
            }

            foreach (var button in _modalButtons)
            {
                _modalBaseline.Add(button.MouseFilter);
                if (button.MouseFilter == Control.MouseFilterEnum.Ignore)
                {
                    GD.PushError("[settings-probe] 退场前已有按钮不吃鼠标命中（基线 Ignore）——退场摘除判据不成立");
                    _settingsProbe = false;
                    return false;
                }
            }

            settings.Back(); // 生产退场链（AnimateModalClose：整棵子树摘命中 + 0.15s 后还原并隐藏）
            for (var i = 0; i < _modalButtons.Count; i++)
            {
                if (_modalButtons[i].MouseFilter != Control.MouseFilterEnum.Ignore)
                {
                    GD.PushError($"[settings-probe] 退场当帧仍有按钮可被命中（第 {i} 个，MouseFilter="
                        + $"{_modalButtons[i].MouseFilter}）——退场期会截获已交还给下一层的点击"
                        + "（只摘父级/只摘面板时，面板外的按钮照旧命中）");
                    _settingsProbe = false;
                    return false;
                }
            }

            _modalStep = 1;
            return false;
        }

        if (_modalStep == 1)
        {
            // 等退场回调落地（根隐藏即回调已跑完，还原就在它之前一行）
            if (settings.Visible)
            {
                return false;
            }

            for (var i = 0; i < _modalButtons.Count; i++)
            {
                if (GodotObject.IsInstanceValid(_modalButtons[i]) && _modalButtons[i].MouseFilter != _modalBaseline[i])
                {
                    GD.PushError($"[settings-probe] 退场回调后按钮鼠标命中未还原（第 {i} 个，MouseFilter="
                        + $"{_modalButtons[i].MouseFilter}，基线 {_modalBaseline[i]}）——退场画面收走后按钮永久不吃点击");
                    _settingsProbe = false;
                    return false;
                }
            }

            settings.ShowSettings(null); // 重开：退场后重新打开应一切照旧
            _modalStep = 2;
            return false;
        }

        _modalButtons.Clear();
        CollectButtons(settings, _modalButtons);
        if (_modalButtons.Count == 0)
        {
            GD.PushError("[settings-probe] 重开设置页后找不到按钮——模态退场还原判据取不到");
            _settingsProbe = false;
            return false;
        }

        foreach (var button in _modalButtons)
        {
            if (button.MouseFilter == Control.MouseFilterEnum.Ignore)
            {
                GD.PushError("[settings-probe] 重开设置页后仍有按钮不吃鼠标命中——还原写坏时第二次开页的按钮永久失效");
                _settingsProbe = false;
                return false;
            }
        }

        GD.Print("[settings-probe] 五页切换完成");
        _settingsProbe = false;
        return true;
    }

    /// <summary>子树里的全部按钮（递归；退场摘除是递归的，判定也覆盖整棵子树）。</summary>
    private static void CollectButtons(Node node, List<Button> into)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Button button)
            {
                into.Add(button);
            }

            CollectButtons(child, into);
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

        // 与其余长跑趟同口径注入无敌：无头局玩家不操作，死亡会让刷怪停摆、复用永远等不到。
        // 这同时是**确定性**要求——顿帧按真实帧长推进（HitStopTimeline），无头局玩家挨打时
        // 「帧数＝模拟时长」会随机器负载漂移，而本趟的判据（复用出现在第几帧）正建立在这条等价上。
        _player.SetInvincible(ProbeInvincibleSeconds);

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
            return;
        }

        // 到点仍没等到复用就报一次诊断：该趟的绿靠「真的跑到复用」成立，缺了标记只会说
        // 「没打完成标记」——不把「跑到哪、回收了多少、场上还有几只」打出来，下一次红
        // 又得从头猜（此前那次偶发红就是这么来的）。实测复用首现于第 1200–1500 帧。
        if (_augmentCacheReuseFrame < 0 && !_augmentCacheDiagnosed && _frame >= ReuseDiagnosticFrame)
        {
            _augmentCacheDiagnosed = true;
            GD.Print($"[augment-cache-probe] 已到帧 {_frame} 仍未观察到池化复用"
                + $"（已回收实例 {_augmentCacheRetired.Count} 个、当前在场 {live.Count} 只）"
                + "——复用没跑到时不再静默");
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
    /// 段 2 的「仍被忽略」是真实时间基准的判别式；「是否用真实时间 API」另由 check_realtime_allowlist.sh 兜住。
    ///
    /// 另断曲目上下文（借这条本就会落基地/离站的路径顺带覆盖）：落基地后当前档位必须是基地休整曲、
    /// 继续出击后必须回默认战斗曲。两处漏写都只改听感（基地里播战斗曲 / 离站后基地曲一直播到
    /// 下次 Boss 定格），不崩不报错——此前零判据。</summary>
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

        // 段 4/5 在过场收尾之后（ReturnCinematic 已置空）——「过场意外消失」只在它应仍在播的
        // 阶段判，故此处带阶段条件；段 3 自己再判一次（那一段还要写过场上的宽限字段）。
        var cinematic = _main.ReturnCinematic();
        if (cinematic == null && _returnStage < 3)
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

        // 段 3（只跑一次）：宽限置 0 确定越过边界 → 跳过必须生效，收尾落基地且树保持暂停
        if (_returnStage == 3)
        {
            if (cinematic == null)
            {
                ReturnProbeFail("返航过场在越过宽限前已释放");
                return;
            }

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

            _returnStage = 4;
            return;
        }

        // 段 4（只跑一次）：落基地的曲目上下文——必须是基地休整曲。回基地那次 RefreshMusic
        // 漏写时基地里还播着战斗曲，而任何门禁都判不到（只有听感不同，不崩不报错）。
        if (_returnStage == 4)
        {
            if (_main.Music().Playing != MusicCue.Base)
            {
                ReturnProbeFail(GdFormat.Format(
                    "落基地后当前曲目不是基地休整曲（实为 %s）——回基地的曲目上下文没切"
                    + "（该档曲目资源未装载时也会停在这一档）",
                    _main.Music().Playing));
                return;
            }

            // 继续出击：走生产信号链（基地「继续出击」按钮发的是同一信号）→ Main 播轨道打击
            // → 命中帧 OnOrbitalStruck 撤基地上下文并恢复本局；不直调 Main 的私有编排。
            var baseUi = _main.GetNodeOrNull<BaseConsole>("BaseUI");
            if (baseUi == null)
            {
                ReturnProbeFail("基地 UI 在继续出击前消失");
                return;
            }

            baseUi.EmitSignal(BaseConsole.SignalName.ResumeRequested);
            _returnStage = 5;
            _returnResumeFrame = _frame;
            return;
        }

        // 段 5：离站必须撤下基地曲目——漏写时基地曲一直播到下次 Boss 定格（另一条会换掉它的
        // 路径），期间玩家听不到战斗曲，而门禁全绿。切换在轨道打击命中帧发生，故逐帧等到点。
        if (_main.Music().Playing != MusicCue.Default)
        {
            if (_frame - _returnResumeFrame > ReturnProbeResumeFrames)
            {
                ReturnProbeFail(GdFormat.Format(
                    "继续出击后 %d 帧曲目仍是 %s（期望默认战斗曲）——基地休整曲会一直播到下次 Boss 定格",
                    ReturnProbeResumeFrames, _main.Music().Playing));
                return;
            }

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
        // 才移除），随后**在有限窗口内**判「已清空」。前置 >0 让判据非空洞——否则「本来就没弹」也算清干净。
        // 窗口而非单帧：出树与注册表移除都挂在帧末，负载抖动下队列可能晚一帧落地——单帧判会把
        // 「清弹确实发生、只是晚一帧可见」判成失败（实测：并行冒烟满载时偶发红）。
        // 判据仍是「转场后弹数归零」，窗口只吸收帧末簿记的时序差，不是重试掩盖。
        if (_bossClearCheckFrame > 0 && _frame >= _bossClearCheckFrame)
        {
            if (!_bossSawPhase2Clear && GameState.Instance.EnemyBullets.Count == 0)
            {
                _bossSawPhase2Clear = true;
            }

            if (_bossSawPhase2Clear || _frame > _bossClearCheckFrame + BossProbeClearSettleFrames)
            {
                _bossClearCheckFrame = 0;
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
                // 狂暴子弹时间「自行复位」：子弹时间结束 + ramp 走完（TimeScaleRamp 回 -1）之后，
                // 引擎倍率必须回到 1。写坏的表现是整局（含 Always 状态的菜单/结算 UI）以 0.24 播放——
                // 不崩不报错、本趟原有五条断言（全读 Boss 自身状态）照旧成立；而锁血等待会被拉长
                // 四倍先撞超时，所以这条必须**在等锁血之前**判，否则报出的是「锁血未解」而非病根。
                // 判据读引擎真值：自算值说「复位了」而引擎仍被压死正是要抓的形态。
                if (_main.BulletTime() <= 0.0f && _main.TimeScaleRamp() < 0.0f
                    && !GameState.Instance.HitStopActive()
                    && !Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f))
                {
                    BossProbeFail($"狂暴子弹时间未自行复位：ramp 已结束但 Engine.TimeScale 卡在 "
                        + $"{(float)Engine.TimeScale:0.###}（第 {_frame} 帧）——"
                        + "整局（含 Always 状态的菜单/结算 UI）将以该倍率播放");
                    return;
                }

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
                if (key == new StringName("elite_turret"))
                {
                    TickAugmentContractProbe();
                }

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

    /// <summary>homing/dash_strike 契约判定的观测态（步序、靶、出血基线、是否见到制导落靶）。</summary>
    private int _augStep;

    private int _augFrame;
    private int _augFireHold;
    private TurretBattery? _augTarget;
    private TurretBattery? _augLockedTurret;
    private int _augHpBefore;
    private int _augLockedHpBefore;
    private float _augOffsetDeg;
    private bool _augDone;

    /// <summary>homing 落靶与 dash_strike 触达的契约判定（精英炮塔趟）。两条都属「按 is Enemy 判型时
    /// 屏上唯一可打目标整体吃不到增幅效果」的静默面——不崩不报错，只是打得没用。
    ///
    /// homing 的判据必须**排除弱追踪这条旁路**：炮台在航母上成排，相邻两座的角距可以小于弱追踪半角，
    /// 偏开一座的准星仍可能落进另一座的锥内（实测：偏 15° 时弱追踪锁上了邻座）。故先扫一个
    /// **干净的偏角**（用生产查询 <c>NearestConeTarget</c> 确认锥内没有任何目标，且目标仍在制导锁定锥内），
    /// 于是「出膛弹有落靶」只可能来自 homing 增幅这条路径。两半互补：
    ///   ① 未授 homing 的同一发：全部在场玩家弹的 <c>HomingTarget</c> 必须为空、炮塔 Hp 不变；
    ///   ② 授了 homing：出膛弹的 <c>HomingTarget</c> 指向炮台，且那座炮台 Hp 下降。
    /// dash_strike：把玩家摆进炮台触及半径内，经生产输入 <c>Input.ActionPress("dash")</c> 冲一次，
    /// 断 Hp 下降 ≥ 每级伤害；冲刺前同位置的一小段帧窗内必须不掉血（防「本来就挨了别的弹」假绿）。
    /// 两处都用生产直写口授层（<c>Talent.GrantLevel</c>，与加点同口径但不扣缓存）。</summary>
    private void TickAugmentContractProbe()
    {
        if (_augDone || !_aimProbeChecked)
        {
            return;
        }

        if (_augTarget == null || !GodotObject.IsInstanceValid(_augTarget))
        {
            _augTarget = FindAimableTurret();
            if (_augTarget == null)
            {
                return; // 炮台尚未升起到位（升起期不可打），下一帧再找
            }
        }

        TickFireHold();
        switch (_augStep)
        {
            case 0:
                if (!PickCleanOffset())
                {
                    GD.PushError("[event-probe] 找不到锥内无目标的干净偏角（炮台过于密集？）——homing 判据取不到");
                    _eventId = "";
                    return;
                }

                _augHpBefore = _augTarget.Hp;
                AimAtOffsetFrom(_augTarget.AimWorldPosition, _augOffsetDeg); // 对照段：未授 homing
                _augFireHold = 2;
                _augFrame = 0;
                _augStep = 1;
                return;

            case 1:
                if (++_augFrame < AugProbeSettleFrames)
                {
                    return;
                }

                if (AnyPlayerBulletHoming())
                {
                    GD.PushError("[event-probe] 未授 homing 的偏角射击出现了落靶（弱追踪把弹拨走了）——"
                        + "干净偏角的前提被破坏，homing 判据失效");
                    _eventId = "";
                    return;
                }

                if (_augTarget.Hp != _augHpBefore)
                {
                    GD.PushError($"[event-probe] 未授 homing 的偏角射击命中了炮塔（Hp {_augHpBefore} → {_augTarget.Hp}）"
                        + "——偏角过小或炮台不在预期位置，homing 判据失效");
                    _eventId = "";
                    return;
                }

                GameState.Instance.Talent.GrantLevel(new StringName("homing"), 1);
                AimAtOffsetFrom(_augTarget.AimWorldPosition, _augOffsetDeg);
                _augFireHold = 2;
                _augFrame = 0;
                _augStep = 2;
                return;

            case 2:
                // 出膛后逐帧枚举在场玩家弹：落靶必须是炮台（记下它锁的那座，按它判掉血）
                if (_augLockedTurret == null)
                {
                    _augLockedTurret = FirstHomingTurret();
                    if (_augLockedTurret != null)
                    {
                        _augLockedHpBefore = _augLockedTurret.Hp;
                    }
                }

                if (++_augFrame < AugProbeSettleFrames)
                {
                    return;
                }

                if (_augLockedTurret == null)
                {
                    GD.PushError("[event-probe] 已授 homing 但出膛弹的落靶未指向任何炮塔——"
                        + "制导落靶搜索未吃 IAimTarget 契约（遭遇单位整体吃不到制导）");
                    _eventId = "";
                    return;
                }

                if (!GodotObject.IsInstanceValid(_augLockedTurret) || _augLockedTurret.Hp >= _augLockedHpBefore)
                {
                    var now = _augLockedTurret == null || !GodotObject.IsInstanceValid(_augLockedTurret)
                        ? -1
                        : _augLockedTurret.Hp;
                    GD.PushError($"[event-probe] 制导已在炮塔上落靶，但 {AugProbeSettleFrames} 帧内该炮塔 Hp 未下降"
                        + $"（{_augLockedHpBefore} → {now}）——制导未把弹拨到靶上");
                    _eventId = "";
                    return;
                }

                AimAtOffsetFrom(_augTarget.AimWorldPosition, 0.0f);
                _augHpBefore = _augTarget.Hp;
                _augFrame = 0;
                _augStep = 3;
                return;

            case 3:
                // dash_strike 的对照段：先在触及半径内站定、不冲刺，Hp 不得变化
                if (++_augFrame == 1)
                {
                    PlacePlayerWithinReach(_augTarget.AimWorldPosition);
                    GameState.Instance.Talent.GrantLevel(new StringName("phase_dash"), 1);
                    GameState.Instance.Talent.GrantLevel(new StringName("dash_strike"), 1);
                }

                if (_augFrame < AugProbeSettleFrames)
                {
                    return;
                }

                if (_augTarget.Hp != _augHpBefore)
                {
                    GD.PushError($"[event-probe] 未冲刺时炮塔 Hp 已变化（{_augHpBefore} → {_augTarget.Hp}）"
                        + "——冲刺打击的判据被别的伤害源污染");
                    _eventId = "";
                    return;
                }

                _augHpBefore = _augTarget.Hp;
                Input.ActionPress(ProbeActDash);
                _augFrame = 0;
                _augStep = 4;
                return;

            default:
                if (_augFrame == 1)
                {
                    Input.ActionRelease(ProbeActDash);
                }

                if (++_augFrame < AugProbeSettleFrames)
                {
                    return;
                }

                var dashFloor = System.Math.Max(
                    (int)GameState.Instance.Cfg("augments.dash_strike.damage_per_level", 0).AsInt64(), 0);
                var dealt = _augHpBefore - _augTarget.Hp;
                if (dealt < dashFloor)
                {
                    GD.PushError($"[event-probe] 冲刺打击对炮塔的伤害 {dealt} < 每级伤害 {dashFloor}——"
                        + "触及判定的判型未吃 IAimTarget 契约（遭遇单位整体打不到）");
                    _eventId = "";
                    return;
                }

                _augDone = true;
                _player.AimPointOverride = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                return;
        }
    }

    /// <summary>选一个「弱追踪锥内无任何目标、且目标仍在制导锁定锥内」的偏角（确定性扫描：
    /// 先小后大、正负交替，命中即用）。返回 false = 没有可用偏角，判据取不到（显式失败）。</summary>
    private bool PickCleanOffset()
    {
        if (GameState.Instance.AimFrameLayer is not AimFrameLayer layer)
        {
            return false;
        }

        var target = _augTarget!.AimWorldPosition;
        var toTarget = (target - _player.GlobalPosition).Normalized();
        var coneCos = _player.ConeCos();
        // 生产只读口：探针自带键读取与默认值会与生产分叉（自带默认 0.0 vs 生产 44.0 时，
        // 探针会按 0° 锥挑偏角，判定的是错误的几何）
        var homingConeCos = _player.HomingLockConeCos();
        for (var step = 1; step <= 20; step++)
        {
            foreach (var sign in new[] { 1.0f, -1.0f })
            {
                var offset = (11.0f + step) * sign; // 11..31 度，跳过弱追踪半角内
                var dir = toTarget.Rotated(Mathf.DegToRad(offset));
                // 制导锁定锥（整角 44°）内必须仍有目标，否则 homing 也不该锁
                if (!Core.Combat.AimTargeting.InCone(dir.X, dir.Y, toTarget.X, toTarget.Y, homingConeCos))
                {
                    continue;
                }

                if (layer.NearestConeTarget(_player.GlobalPosition, dir, coneCos) != null)
                {
                    continue; // 弱追踪会抢锁：换一个偏角
                }

                _augOffsetDeg = offset;
                return true;
            }
        }

        return false;
    }

    /// <summary>在场玩家弹里是否有任何一枚带落靶（对照段要求全空：没有 homing 增幅时不该有落靶）。</summary>
    private bool AnyPlayerBulletHoming()
    {
        foreach (var child in _main.GetChildren())
        {
            if (child is Bullet bullet && GodotObject.IsInstanceValid(bullet) && bullet.IsPlayerBullet
                && bullet.IsActive() && bullet.HomingTarget != null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>在场玩家弹里第一枚落靶为炮台的弹所锁的那座炮台（没有则 null）。</summary>
    private TurretBattery? FirstHomingTurret()
    {
        foreach (var child in _main.GetChildren())
        {
            if (child is Bullet bullet && GodotObject.IsInstanceValid(bullet) && bullet.IsPlayerBullet
                && bullet.IsActive() && bullet.HomingTarget is TurretBattery turret)
            {
                return turret;
            }
        }

        return null;
    }

    /// <summary>把准星指到「以玩家为顶点、指向目标的方向偏 <paramref name="offsetDeg"/> 度」的延长线上
    /// （offset 0 = 正对目标）。900px 只表达方向，落在视野外无妨。</summary>
    private void AimAtOffsetFrom(Vector2 targetPos, float offsetDeg)
    {
        var dir = (targetPos - _player.GlobalPosition).Normalized();
        if (offsetDeg != 0.0f)
        {
            dir = dir.Rotated(Mathf.DegToRad(offsetDeg));
        }

        _player.AimPointOverride = _player.GlobalPosition + (dir * 900.0f);
    }

    /// <summary>把玩家摆到目标的触及半径内（并让机头方向指向目标）：冲刺方向取准星方向，
    /// 正对目标冲刺才能在打击节拍上保持在半径内。</summary>
    private void PlacePlayerWithinReach(Vector2 targetPos)
    {
        var radius = (float)GameState.Instance.Cfg("augments.dash_strike.radius", 80.0f).AsDouble();
        var below = targetPos + new Vector2(0.0f, Mathf.Max(radius * 0.5f, 8.0f));
        var view = GameState.Instance.ViewWorldRect();
        _player.GlobalPosition = below.Clamp(view.Position + new Vector2(8.0f, 8.0f), view.End - new Vector2(8.0f, 8.0f));
        _player.Velocity = Vector2.Zero;
        AimAtOffsetFrom(targetPos, 0.0f);
    }

    /// <summary>开火键按住两帧后松开（一发出膛；开火间隔远大于两帧）。</summary>
    private void TickFireHold()
    {
        if (_augFireHold == 2)
        {
            Input.ActionPress(ProbeActFire);
            _augFireHold = 1;
        }
        else if (_augFireHold == 1)
        {
            Input.ActionRelease(ProbeActFire);
            _augFireHold = 0;
        }
    }

    /// <summary>可打的炮台（升起到位后才纳入瞄准；升起期 AimTargetable=false，此时开火会漏判）。</summary>
    private static TurretBattery? FindAimableTurret()
    {
        foreach (var node in GameState.Instance.Enemies)
        {
            if (node is TurretBattery turret && GodotObject.IsInstanceValid(turret) && turret.AimTargetable)
            {
                return turret;
            }
        }

        return null;
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

        // 无障碍开关同样只认真 bool（宽松转换把 "on" 读成 true 时，手改档就能替玩家打开高对比）
        if (gs.HighContrast)
        {
            GD.PushError("[hostile-save-probe] 恶意设置档的 high_contrast 非 bool 却被读成开——"
                + "手改档可替玩家改无障碍档");
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
            || !gs.HighContrast || gs.CustomWindowWidth != 1000)
        {
            GD.PushError(GdFormat.Format(
                "[hostile-save-probe] 正常设置档未还原：locale=%s view_zoom=%s aim_assist=%s fps_cap=%s difficulty=%s "
                + "high_contrast=%s custom_width=%d",
                gs.Locale, gs.ViewZoom, gs.AimAssistLevel, gs.FpsCap, gs.Difficulty, gs.HighContrast,
                gs.CustomWindowWidth));
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

    /// <summary>提前离舰蓄力的终局清理探针（--early-leave-clear-probe）：走生产蓄力链进入驻留态、
    /// 长按 <c>dock</c> 把「提前离舰」蓄力条按出来，然后**先松手、再击杀玩家**，断结算页上不再有这条蓄力条。
    ///
    /// 为什么必须探：这条通道的推进在母舰的 <c>_PhysicsProcess</c> 里，而死亡终局会暂停整棵树——
    /// 母舰既不推进也不拆树，`_ExitTree` 的兜底清理不会发生，漏清的表现是**进度条以最后比例常驻结算页**
    /// （结算页 dim 只压暗、不清除），不崩不报错。
    /// 判定三段，缺一不可：① 蓄力条**确实在列**（先断在列，否则「死亡后为空」空转假绿）；
    /// ② 松手（不松手则树恢复后母舰重新蓄力、条子再现，判定反过来冤枉实现）；
    /// ③ 击杀玩家后不再含该通道。帧序只由 <c>--fixed-fps 60</c> 驱动，不看墙钟。</summary>
    private void TickEarlyLeaveProbe()
    {
        if (_earlyStage == 0)
        {
            if (_frame < 30 || _player.IsEntryPlaying() || !_spawner.IsProcessing())
            {
                return;
            }

            _player.SetInvincible(ProbeInvincibleSeconds); // 无头局玩家不操作，与存活解耦
            _earlyStage = 1;
            return;
        }

        if (_earlyStage == 1)
        {
            _earlyChargeFrames++;
            if (_earlyChargeFrames > DockProbeChargeFrames)
            {
                Input.ActionRelease(ActDock);
                _earlyProbe = false;
                GD.PushError($"[early-leave-probe] 长按坞蓄力 {DockProbeChargeFrames} 帧仍未召唤母舰（蓄力链断线）");
                return;
            }

            Input.ActionPress(ActDock);
            if (_main.Mothership() != null)
            {
                Input.ActionRelease(ActDock);
                _earlyStage = 2;
            }

            return;
        }

        var ms = _main.Mothership();
        if (ms == null || !GodotObject.IsInstanceValid(ms))
        {
            _earlyProbe = false;
            GD.PushError("[early-leave-probe] 母舰在驻留前离场——提前离舰蓄力判据取不到");
            return;
        }

        if (_earlyStage == 2)
        {
            // 等对接流程推进到驻留态（提前离舰蓄力只在 STAY 内累积）
            if (ms.GetState() == Mothership.State.STAY)
            {
                _earlyStage = 3;
                _earlyChargeFrames = 0;
            }
            else if (_earlyChargeFrames++ > DockProbeMagWarnTimeoutFrames)
            {
                _earlyProbe = false;
                GD.PushError($"[early-leave-probe] 驻留态 {DockProbeMagWarnTimeoutFrames} 帧内未到达（对接链卡死）");
            }

            return;
        }

        if (_earlyStage == 3)
        {
            _earlyChargeFrames++;
            if (_earlyChargeFrames > EarlyLeaveProbeChargeBoundFrames)
            {
                Input.ActionRelease(ActDock);
                _earlyProbe = false;
                GD.PushError($"[early-leave-probe] 长按 {EarlyLeaveProbeChargeBoundFrames} 帧仍未出现提前离舰蓄力通道"
                    + "（通道未推进或 HUD 未登记）——判据取不到");
                return;
            }

            Input.ActionPress(ActDock); // 生产蓄力链：进度由母舰 _PhysicsProcess 推进、HUD 读口可见
            _earlyHud ??= GetTree().GetFirstNodeInGroup("hud") as Hud;
            if (_earlyHud == null)
            {
                Input.ActionRelease(ActDock);
                _earlyProbe = false;
                GD.PushError("[early-leave-probe] 未找到 HUD 节点——蓄力通道读口取不到");
                return;
            }

            if (_earlyHud.VisibleChargeChannels().Contains(Hud.ChargeChannel.EarlyLeave))
            {
                // 松手与击杀必须**同帧**（先松手再击杀）：跨帧时母舰的 _PhysicsProcess 会先跑完松手
                // 分支把条子清掉，死亡清理的判据就变成空转（判的是松手路径而非终局路径）。
                Input.ActionRelease(ActDock);
                _player.Die(); // 显式击杀（绕过无敌）——死亡路径 = 同帧暂停树 + ClearAllCharge
                var leftover = _earlyHud.VisibleChargeChannels().Contains(Hud.ChargeChannel.EarlyLeave);
                _earlyProbe = false;
                if (leftover)
                {
                    GD.PushError("[early-leave-probe] 死亡终局后提前离舰蓄力条仍常驻（结算页 dim 只压暗、不清除）——"
                        + "ClearAllCharge 未覆盖这条通道");
                    return;
                }

                GD.Print("[early-leave-probe] 提前离舰蓄力随死亡清理");
            }

            return;
        }
    }

    /// <summary>存档还原探针（--save-restore-probe）：风险加点层级与补给超载槽的钳制、生命上限自洽、
    /// 满槽档的刷新受阻提示、读档补发信号。
    ///
    /// 为什么必须探：读档还原里的「层级上限」与「信号补发」都是静默面——层级被无条件钳回结构上限时，
    /// 玩家双倍价买的那一级静默消失（乘算消费端少一级效果）；还原直写字段不发信号时，HUD 难度读数
    /// 与目标行停在复位值（最长停到下一次 30s 量化步进）。六段，缺一不可：
    ///   ① 带风险加点标记的超结构层级：层级 == max+1，生命上限与层级自洽；
    ///   ② 反向对照（去掉标记）：同一存档层级钳回 max——只判 ① 会让「一律抬级」的实现照样绿；
    ///   ③ 对照（名单里塞池外节点名）：不抬高任何节点；
    ///   ④ 超档的补给超载槽（档位 +997）→ 钳回补给档位，且本局超载上限 == 配置档 + 补给档；
    ///   ⑤ 反向对照（合法档位 1）→ 原样还原——只判 ④ 时「一律归 0」的实现照样绿；
    ///   ⑥ 任务位占满档（三槽全已完成未领取 + 刷新点数不足）→ 受阻原因＝无空位，
    ///      且经生产入基地口 ShowBase 后面板显示的正是领取指引（置灰按钮按不动，
    ///      「按下才提示」的写法在置灰态根本走不到）。
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
        var slotMax = gs.Talent.BonusOverchargeSlotsMax;
        var cfgSlots = System.Math.Max((int)gs.Cfg("talent.overcharge.max_per_run", 3).AsInt64(), 0);
        var bonusSlotJson =
            $"{{\"version\":1,\"health\":42.5,\"run_time\":{runTimeJson},\"difficulty_multiplier\":{difficultyJson},"
            + $"\"talent_levels\":{{}},\"talent_overcharged\":[],\"talent_bonus_overcharge_slots\":{slotMax + 997}}}";
        var legalSlotJson =
            $"{{\"version\":1,\"health\":42.5,\"run_time\":{runTimeJson},\"difficulty_multiplier\":{difficultyJson},"
            + $"\"talent_levels\":{{}},\"talent_overcharged\":[],\"talent_bonus_overcharge_slots\":1}}";

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

        // ④ 超档的补给超载槽（slotMax+997）→ 钳回补给档位：这条字段决定本局风险加点名额
        // （OverchargeMaxPerRun），只钳 ≥0 时手改档写大数即让全树节点各白拿一级、且绕过补给售罄限制
        ok &= WriteUserFile("user://run.json", bonusSlotJson);
        ok &= LoadRunForProbe("超档补给超载槽档");
        if (gs.Talent.BonusOverchargeSlots != slotMax)
        {
            GD.PushError($"[save-restore-probe] 补给超载槽未钳回档位：{gs.Talent.BonusOverchargeSlots}"
                + $"（期望 {slotMax}）——手改档写大数即绕过补给限制多拿风险加点名额");
            ok = false;
        }

        if (gs.Talent.OverchargeMaxPerRun != cfgSlots + slotMax)
        {
            GD.PushError($"[save-restore-probe] 本局超载上限与钳制后的补给槽不自洽：{gs.Talent.OverchargeMaxPerRun}"
                + $"（期望 talent.overcharge.max_per_run {cfgSlots} + 补给档 {slotMax}）");
            ok = false;
        }

        // ⑤ 反向对照（合法档位 1）：只判 ④ 时「一律归 0」的实现照样绿——买到的槽必须留住
        ok &= WriteUserFile("user://run.json", legalSlotJson);
        ok &= LoadRunForProbe("合法补给超载槽档");
        if (gs.Talent.BonusOverchargeSlots != 1 || gs.Talent.OverchargeMaxPerRun != cfgSlots + 1)
        {
            GD.PushError($"[save-restore-probe] 合法补给超载槽未原样还原：槽 {gs.Talent.BonusOverchargeSlots}（期望 1）、"
                + $"本局超载上限 {gs.Talent.OverchargeMaxPerRun}（期望 {cfgSlots + 1}）——买到的风险加点名额读档即丢");
            ok = false;
        }

        // ⑥ 任务位占满档（三槽全「已完成未领取」且刷新点数不足）：刷新受阻原因必须是「无空位」，
        // 且基地面板真的把这条原因画出来——置灰按钮不派发 pressed，「按下才提示」的写法在置灰态
        // 走不到，玩家只能看到一颗没有原因的灰按钮。走生产入基地口 ShowBase（内部 Refresh 同步提示）
        var slots = gs.MISSION_SLOTS;
        var missionEntries = "";
        var filled = 0;
        foreach (var def in gs.MISSION_POOL)
        {
            if (filled >= slots)
            {
                break;
            }

            var defId = def["id"].AsStringName().ToString();
            var defGoal = (int)def["goal"].AsInt64();
            missionEntries += $"\"{defId}\":{{\"progress\":{defGoal},\"claimed\":false}},";
            filled += 1;
        }

        if (filled < slots)
        {
            // 构造不出满槽档：判据取不到，显式判红（不得静默跳过这一段的断言）
            GD.PushError($"[save-restore-probe] 任务池条目 {filled} 个不足以填满 {slots} 个槽位，"
                + "本趟无法构造满槽档");
            ok = false;
        }
        else
        {
            var fullSlotsJson =
                $"{{\"version\":1,\"run_time\":{runTimeJson},\"difficulty_multiplier\":{difficultyJson},"
                + $"\"talent_levels\":{{}},\"refresh_points\":0,"
                + $"\"missions\":{{{missionEntries.TrimEnd(',')}}}}}";
            ok &= WriteUserFile("user://run.json", fullSlotsJson);
            ok &= LoadRunForProbe("任务位占满档");
            if (gs.MissionRefreshBlockReason() != MissionRefresh.ReasonSlots)
            {
                GD.PushError($"[save-restore-probe] 满槽档的刷新受阻原因不是「无空位」："
                    + $"{gs.MissionRefreshBlockReason()}——点数不足提示会把玩家引向一个他已经满足的条件");
                ok = false;
            }

            var baseUi = _main.GetNodeOrNull<BaseConsole>("BaseUI");
            var slotHint = (string)Tr("BASE_NO_REFRESH_SLOTS");
            var pointsHint = (string)Tr("BASE_NO_REFRESH_POINTS");
            if (baseUi == null)
            {
                GD.PushError("[save-restore-probe] 基地面板不存在，无法断言置灰原因已显示");
                ok = false;
            }
            else
            {
                baseUi.ShowBase(); // 生产入基地口：内部 Refresh 同步提示区
                var shown = baseUi.RefreshHintText;
                if (shown != slotHint || shown.Length == 0 || shown == "BASE_NO_REFRESH_SLOTS")
                {
                    GD.PushError($"[save-restore-probe] 满槽档未显示领取指引：显示的是「{shown}」"
                        + $"（期望「{slotHint}」）——无空位与点数不足要选不同文案");
                    ok = false;
                }
            }

            if (slotHint == pointsHint)
            {
                GD.PushError("[save-restore-probe] 无空位与点数不足的文案相同——两条受阻原因分不开");
                ok = false;
            }
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
    ///   ③ 「全部恢复默认」的信号补发：值确实变化过时视角/辅瞄/减闪/高对比/画面增强五个信号各发一次，
    ///      无变化时一个都不发（消费方是「读一次 + 信号刷新」的缓存型，漏发则表现仍按旧值跑——
    ///      高对比的消费方是在飞敌弹的重挑贴图，漏发就让复位前的旧外观留在场上）。</summary>
    private void RunSettingsVersionProbe()
    {
        var gs = GameState.Instance;
        var ok = true;

        _probeViewZoomSignals = 0;
        _probeAimAssistSignals = 0;
        _probeReduceFlashSignals = 0;
        _probeHighContrastSignals = 0;
        _probeWorldPostFxSignals = 0;
        _onProbeViewZoom = Callable.From<double>(_ => _probeViewZoomSignals++);
        _onProbeAimAssist = Callable.From<StringName>(_ => _probeAimAssistSignals++);
        _onProbeReduceFlash = Callable.From<bool>(_ => _probeReduceFlashSignals++);
        _onProbeHighContrast = Callable.From<bool>(_ => _probeHighContrastSignals++);
        _onProbeWorldPostFx = Callable.From<bool>(_ => _probeWorldPostFxSignals++);
        gs.Connect(GameState.SignalName.ViewZoomChanged, _onProbeViewZoom);
        gs.Connect(GameState.SignalName.AimAssistChanged, _onProbeAimAssist);
        gs.Connect(GameState.SignalName.ReduceFlashChanged, _onProbeReduceFlash);
        gs.Connect(GameState.SignalName.HighContrastChanged, _onProbeHighContrast);
        gs.Connect(GameState.SignalName.WorldPostFxChanged, _onProbeWorldPostFx);
        _probeSettingsSubscribed = true;

        // 判据的基线必须是出厂档：高于当前的档「按逐字段默认值回退」等价于把整表当空档，
        // 而空档的语义是「保持当前值」——当前值非默认时，判据就落到读入的那份档上了（假绿）。
        gs.ResetAllSettings();
        ok &= WriteUserFile("user://settings.json",
            $"{{\"version\":{Core.Storage.SettingsMigration.CurrentVersion + 1},\"locale\":\"en\","
            + "\"difficulty\":\"hard\",\"view_zoom\":\"large\",\"resolution\":\"1280x720\","
            + "\"custom_width\":1000,\"custom_height\":700,\"aim_assist\":\"high\",\"fps_cap\":\"fps30\","
            + "\"vsync\":false,\"reduce_flash\":true,\"high_contrast\":true,\"world_post_fx\":false,\"mouse_lock\":false,"
            + "\"shake_scale\":0.2,\"hit_stop_scale\":0.1,\"master_volume\":0.5,\"music_volume\":0.4,"
            + "\"sfx_volume\":0.3,\"joy_aim_speed\":3000.0,\"joy_deadzone\":0.7,\"joy_vibration\":false}");
        ok &= LoadSettingsForProbe("更高版本档");
        ok &= VerifySettingsDefaults("更高版本档读入后");

        // ② 同版对照档：同一组非默认值必须逐项还原
        ok &= WriteUserFile("user://settings.json", ValidSettingsJson);
        ok &= LoadSettingsForProbe("同版对照档");
        ok &= VerifySettingsRestored("同版对照档读入后");

        // ③ 恢复默认的信号补发：当前值确实非默认 → 五个信号各发一次；再复位（无变化）→ 一个都不发
        _probeViewZoomSignals = 0;
        _probeAimAssistSignals = 0;
        _probeReduceFlashSignals = 0;
        _probeHighContrastSignals = 0;
        _probeWorldPostFxSignals = 0;
        gs.ResetAllSettings();
        if (_probeViewZoomSignals != 1 || _probeAimAssistSignals != 1
            || _probeReduceFlashSignals != 1 || _probeHighContrastSignals != 1
            || _probeWorldPostFxSignals != 1)
        {
            GD.PushError($"[settings-version-probe] 「全部恢复默认」的补发信号不全（视角 {_probeViewZoomSignals} / "
                + $"辅瞄 {_probeAimAssistSignals} / 减闪 {_probeReduceFlashSignals} / 高对比 {_probeHighContrastSignals} / "
                + $"画面增强 {_probeWorldPostFxSignals}，各期望 1）——消费方是「读一次 + 信号刷新」的缓存型，"
                + "漏发则表现仍按旧值跑");
            ok = false;
        }

        _probeViewZoomSignals = 0;
        _probeAimAssistSignals = 0;
        _probeReduceFlashSignals = 0;
        _probeHighContrastSignals = 0;
        _probeWorldPostFxSignals = 0;
        gs.ResetAllSettings();
        if (_probeViewZoomSignals + _probeAimAssistSignals + _probeReduceFlashSignals
            + _probeHighContrastSignals + _probeWorldPostFxSignals != 0)
        {
            GD.PushError($"[settings-version-probe] 值未变化时仍补发了设置信号（视角 {_probeViewZoomSignals} / "
                + $"辅瞄 {_probeAimAssistSignals} / 减闪 {_probeReduceFlashSignals} / 高对比 {_probeHighContrastSignals} / "
                + $"画面增强 {_probeWorldPostFxSignals}）——多余重建");
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
            || gs.HighContrast
            || !gs.WorldPostFx || gs.CustomWindowWidth != 1920 || gs.CustomWindowHeight != 1080)
        {
            GD.PushError($"[settings-version-probe] {label} 未逐项回出厂档：locale={gs.Locale} "
                + $"difficulty={gs.Difficulty} view_zoom={gs.ViewZoom} aim_assist={gs.AimAssistLevel} "
                + $"fps_cap={gs.FpsCap} vsync={gs.VSync} mouse_lock={gs.MouseLock} reduce_flash={gs.ReduceFlash} "
                + $"high_contrast={gs.HighContrast} "
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
            || !gs.HighContrast
            || gs.WorldPostFx || gs.CustomWindowWidth != 1000 || gs.CustomWindowHeight != 700)
        {
            GD.PushError($"[settings-version-probe] {label} 未逐项还原：locale={gs.Locale} "
                + $"difficulty={gs.Difficulty} view_zoom={gs.ViewZoom} aim_assist={gs.AimAssistLevel} "
                + $"fps_cap={gs.FpsCap} vsync={gs.VSync} mouse_lock={gs.MouseLock} reduce_flash={gs.ReduceFlash} "
                + $"high_contrast={gs.HighContrast} "
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

    // ---------------- 练习模式探针（--practice-probe） ----------------

    /// <summary>
    /// 练习局探针：判练习模式的四件承诺里无头下判得动的部分——
    ///   ① 面板开页与三行直选：控件文本是所选设置对应的**译文**（缺键时玩家看到的是键名本身）、
    ///      循环切换与 core 映射一致、确认键交出的设置等于面板此刻显示的东西；
    ///   ② 所选 Boss 经生产出场链按型别出场（探针不 instantiate，BossSpawned 是唯一观测面）；
    ///   ③ 所选遭遇走生产触发链启动（分数门槛由练习起始分数真正满足，不是绕过）；
    ///   ④ 练习局不落盘：死亡后预置的检查点原封不动、记录文件不出现。
    /// 面板的实拍观感（排版/文案语气）归窗口化过目，本探针只判「能开、选项对、键都翻得出来」。
    /// </summary>
    private void TickPracticeProbe()
    {
        switch (_practiceStage)
        {
            case 0:
                if (_frame < 2)
                {
                    return;
                }

                // 入场演出的断言必须在开局的头几帧读（演出全程 1.65s），故排在面板自检之前
                if (!VerifyPracticeEntrySequence() || !RunPracticePanelCheck() || !PreparePracticeProbeRun())
                {
                    _practiceProbe = false;
                    return;
                }

                _practiceStage = 1;
                _practiceStageFrame = _frame;
                return;

            case 1:
                if (_driver == null)
                {
                    var driverPlayer = _main.GetNode<Player>("Player");
                    _driver = new PracticeDriver(_practiceSetup, driverPlayer, _spawner, _events);
                    _driver.SeedScore(); // 与练习宿主同一条落地路径（补门槛分 → 请 Boss/事件）
                }

                // 难度断的是**实际生效值**（GameState.Difficulty），不是 Practice 里回显的设置：
                // 后者与探针传进去的是同一个对象，判据恒真——删掉 BeginPractice 的起始难度应用，
                // 面板选了难档而实际仍按上一档跑（命中/伤害/档位奖励全不同）也照样绿。
                if (GameState.Instance.Practice.BossType != _practiceSetup.BossType
                    || GameState.Instance.Difficulty != new StringName(_practiceSetup.DifficultyName))
                {
                    GD.PushError(GdFormat.Format(
                        "[practice-probe] 练习态与直选设置不符（局内 Boss=%d 生效难度=%s，期望 %d/%s）——"
                        + "面板选的东西没落到本局",
                        GameState.Instance.Practice.BossType, GameState.Instance.Difficulty,
                        _practiceSetup.BossType, _practiceSetup.DifficultyName));
                    _practiceProbe = false;
                    return;
                }

                if (!_driver.Tick())
                {
                    if (_frame - _practiceStageFrame > PracticeProbeRequestFrames)
                    {
                        GD.PushError(GdFormat.Format(
                            "[practice-probe] 入场 %d 帧后直选请求仍未被受理（Boss=%s 事件=%s）——"
                            + "练习局的直选内容送不到生产链",
                            PracticeProbeRequestFrames, _driver.BossRequested, _driver.EventRequested));
                        _practiceProbe = false;
                    }

                    return;
                }

                // 起始分与所选遭遇的生产门槛：分数没补到门槛，遭遇永远等不到（请求会被资格判据挡在门外）
                var minScore = _events.EncounterMinScore(new StringName(PracticeSetup.EliteTurretId));
                if (_practiceSetup.HasEncounter && !_practiceSetup.EncounterIsFog
                    && GameState.Instance.Score < minScore)
                {
                    GD.PushError(GdFormat.Format(
                        "[practice-probe] 练习起始分 %d 低于所选遭遇的生产门槛 %d——遭遇的门槛没有被满足",
                        GameState.Instance.Score, minScore));
                    _practiceProbe = false;
                    return;
                }

                _practiceStage = 2;
                _practiceStageFrame = _frame;
                return;

            case 2:
                if (_practiceBoss == null)
                {
                    if (_frame - _practiceStageFrame > PracticeProbeBossFrames)
                    {
                        GD.PushError(GdFormat.Format(
                            "[practice-probe] 请求受理后 %d 帧仍未等出 Boss（生产出场链断线？）",
                            PracticeProbeBossFrames));
                        _practiceProbe = false;
                    }

                    return;
                }

                if (!GodotObject.IsInstanceValid(_practiceBoss))
                {
                    GD.PushError("[practice-probe] Boss 实例在断言前释放（出场链异常收场）");
                    _practiceProbe = false;
                    return;
                }

                // 型别断言：这是「直选」二字的唯一判据——出场链走通了但型别是轮换出来的，
                // 玩家练的就不是自己选的那只，而失败表现只是「打着不对劲」。
                if (_practiceBoss.BossType != _practiceSetup.BossType)
                {
                    GD.PushError(GdFormat.Format(
                        "[practice-probe] 出场 Boss 型别 %d ≠ 直选 %d——直选型别没有传到出场链",
                        _practiceBoss.BossType, _practiceSetup.BossType));
                    _practiceProbe = false;
                    return;
                }

                // 让出 Boss 槽（走生产受击链致死）：遭遇组的互斥判据要求 Boss 不在场，
                // 不放它走就永远观测不到所选遭遇启动——那不是遭遇坏了，是探针没给机会。
                var amount = _practiceBoss.Hp + 1.0f;
                _practiceBoss.TakeDamage((int)System.Math.Ceiling(amount), 1.0f);
                _practiceBoss = null;
                _practiceStage = 3;
                _practiceStageFrame = _frame;
                return;

            case 3:
                if (_events.ActiveId(GameEventManager.GroupEncounter).ToString() == _practiceSetup.EncounterId)
                {
                    _practiceStage = 4;
                    _practiceStageFrame = _frame;
                    return;
                }

                if (_frame - _practiceStageFrame > PracticeProbeEventFrames)
                {
                    GD.PushError($"[practice-probe] Boss 让位后 {PracticeProbeEventFrames} 帧仍未等到直选的遭遇 "
                        + $"{_practiceSetup.EncounterId} 启动（分数门槛/生产触发链断线？）");
                    _practiceProbe = false;
                }

                return;

            case 4:
                // 死亡前先让遭遇跑起来一点（收尾期事件会被 Main 打断，但「启动过」已在上一步断掉）
                _player.Die(); // 走生产死亡链：PauseUi/结算页/删档钩子全按正局走
                _practiceStage = 5;
                _practiceStageFrame = _frame;
                return;

            case 5:
                if (_frame - _practiceStageFrame < PracticeProbeDeathSettleFrames)
                {
                    return;
                }

                _practiceStage = 6;
                if (!VerifyPracticeProbeNoWrites())
                {
                    _practiceProbe = false;
                    return;
                }

                // 恢复暂停：结算页把树暂停了，本帧之后本趟还要走标题屏与真实的练习入口（见下）
                GameState.Instance.SetTreePaused(false);
                GD.Print(GdFormat.Format("[practice-probe] 直选与不落盘语义成立（Boss 型别 %d，遭遇 %s）",
                    _practiceSetup.BossType, _practiceSetup.EncounterId));

                // 标题屏手柄可达段：驱动节点挂根（本节点随场景切换释放，导航判定跨两次切场景），
                // 导航全过后由它交棒真实练习入口；导航不过则不交棒——练习宿主的标记缺席即本趟红
                var navDriver = new TitleNavProbeDriver(_practiceSetup) { Name = "TitleNavProbeDriver" };
                GetTree().Root.AddChild(navDriver);
                _practiceProbe = false;
                return;
        }
    }

    /// <summary>练习开局的入场演出断言（DESIGN_BASELINE §1.16「练习局的 RunTime / 难度 / 里程碑照常在
    /// 内存里推进，**手感必须与正局一致**」）：练习局走的是与正局同一条入场路径，故开局三件事必须
    /// 与正局同口径——演出在进行中、无敌窗口是入场档（而不是只剩出生保护）、玩家位在可见域下沿之外
    /// （入场落点轨迹的起点，正局落点在域内约 0.74 屏高）。
    ///
    /// 为什么必须探：漏投入场序列时练习局照常开局、不崩不报错，只有两处手感不同（起始位与无敌
    /// 窗口），而练习局与正局共用同一套结算与难度推进——「练一个手感不同的游戏」没有任何门禁信号。
    /// 三条判据各防一种实现：只判「演出在进行中」会被「播了演出但没接无敌」蒙过；只判无敌会被
    /// 「按无敌时长直接开局」蒙过；故都在首帧读（三条各自独立失败）。</summary>
    private bool VerifyPracticeEntrySequence()
    {
        var view = GameState.Instance.ViewWorldRect();
        var ok = true;
        if (!_player.IsEntryPlaying())
        {
            GD.PushError(GdFormat.Format(
                "[practice-probe] 练习开局没有入场演出（首帧玩家位 %.0f,%.0f，无敌 %.2fs / 入场档 %.2fs）——"
                + "练习局的落点与入场无敌窗口与正局不同口径",
                _player.Position.X, _player.Position.Y, _player.Invincible, _player.EntryInvincible));
            ok = false;
        }

        // 入场无敌窗口：演出期间的无敌由入场档写入（不是出生保护——后者只有 1s，练习局靠它撑不过
        // 开局的头两秒）。容差 0.2s 吸收首帧与逐帧递减的读数差。
        if (Math.Abs(_player.Invincible - _player.EntryInvincible) > 0.2f)
        {
            GD.PushError(GdFormat.Format(
                "[practice-probe] 练习开局的无敌窗口不是入场档（实得 %.2fs，入场档 %.2fs，出生保护 %.2fs）——"
                + "练习开局少了入场无敌",
                _player.Invincible, _player.EntryInvincible, _player.SpawnInvincibleTime));
            ok = false;
        }

        // 入场起点在可见域下沿之外（正局同一式）：起点若留在场景初始位（域内），落点轨迹整段不存在。
        if (_player.Position.Y < view.End.Y)
        {
            GD.PushError(GdFormat.Format(
                "[practice-probe] 练习开局的玩家位不在可见域下沿之外（y=%.0f，域下沿 %.0f）——"
                + "没有入场落点，玩家从场景初始位开局",
                _player.Position.Y, view.End.Y));
            ok = false;
        }

        return ok;
    }

    /// <summary>面板开页自检：三行控件都建起来了、显示的是所选设置对应的译文（缺键时 Tr 返回键名本身、
    /// 玩家看到的就是键名）、环形切换与 core 映射一致、确认键交出的设置等于面板此刻显示的值。
    /// 三条判据都读面板自己的控件文本（与玩家看到的是同一批控件），不另算一份期望文本——
    /// 另算一份就成了「实现与判据各写一遍」，实现对不上时判据会跟着一起错。</summary>
    private bool RunPracticePanelCheck()
    {
        var panel = new PracticePanel();
        AddChild(panel); // 入树即跑 _Ready：控件在本帧内建好
        var initial = panel.RowTexts();
        if (initial.Length != PracticePanel.RowCount)
        {
            GD.PushError($"[practice-probe] 练习面板只建出 {initial.Length} 行（期望 {PracticePanel.RowCount} 行）");
            panel.QueueFree();
            return false;
        }

        var expectDefault = new[] { "PRACTICE_NONE", "DIFF_MEDIUM", "PRACTICE_NONE" };
        for (var i = 0; i < initial.Length; i++)
        {
            if (initial[i].Length == 0 || initial[i] == expectDefault[i])
            {
                // Tr 缺行时原样返回键名——这正是玩家会看到的东西，故按「显示内容 == 键名」判缺键
                GD.PushError(GdFormat.Format(
                    "[practice-probe] 练习面板第 %d 行显示为空或缺键（显示「%s」，键 %s）",
                    i, initial[i], expectDefault[i]));
                panel.QueueFree();
                return false;
            }
        }

        // 环形切换：一行单独切一步再切回来，必须回到出发点（越界归一口径在 core，此处验它被面板用上）
        panel.Cycle(0, 1);
        var afterBoss = panel.RowTexts()[0];
        panel.Cycle(0, -1);
        if (panel.RowTexts()[0] != initial[0])
        {
            GD.PushError(GdFormat.Format(
                "[practice-probe] 练习面板 Boss 行切换后未回到原值（切一步显示「%s」，切回来「%s」）",
                afterBoss, panel.RowTexts()[0]));
            panel.QueueFree();
            return false;
        }

        // 把面板调到本趟的直选设置，再走确认键：交出的设置必须与面板此刻显示的一致
        panel.Cycle(0, _practiceSetup.BossType);
        panel.Cycle(1, _practiceSetup.DifficultyIndex - 1); // 面板初值为中档（索引 1）
        panel.Cycle(2, _practiceSetup.EncounterIndex);
        if (panel.Current != _practiceSetup)
        {
            GD.PushError(GdFormat.Format(
                "[practice-probe] 面板环形切换后的当前设置（Boss {0}/难度 {1}/遭遇 {2}）≠ 本趟目标（Boss {3}/难度 {4}/遭遇 {5}）"
                + "——面板的循环与 core 映射不同源",
                panel.Current.BossType, panel.Current.DifficultyIndex, panel.Current.EncounterIndex,
                _practiceSetup.BossType, _practiceSetup.DifficultyIndex, _practiceSetup.EncounterIndex));
            panel.QueueFree();
            return false;
        }

        _practicePanelConfirmed = null;
        panel.StartRequested += setup => _practicePanelConfirmed = setup;
        panel.Confirm();
        var confirmed = _practicePanelConfirmed;
        if (confirmed == null || confirmed != _practiceSetup)
        {
            GD.PushError(GdFormat.Format(
                "[practice-probe] 练习面板确认键交出的设置（{0}）≠ 面板此刻显示的设置（{1}）——"
                + "面板上写的与点下去启动的不是同一件事",
                confirmed == null ? "null" : $"Boss {confirmed.BossType}/难度 {confirmed.DifficultyIndex}/遭遇 {confirmed.EncounterIndex}",
                $"Boss {_practiceSetup.BossType}/难度 {_practiceSetup.DifficultyIndex}/遭遇 {_practiceSetup.EncounterIndex}"));
            panel.QueueFree();
            return false;
        }

        panel.QueueFree();
        // 练习场景资源必须真的加载得起来：入口路径写错（场景改名/搬走）时 EnterPractice 会切到
        // 一个不存在的场景——面板一切正常、代码全绿，玩家却进不去练习，只有这一步判得出来。
        if (ResourceLoader.Load<PackedScene>("res://scenes/practice.tscn") == null)
        {
            GD.PushError("[practice-probe] 练习场景 res://scenes/practice.tscn 加载失败——练习入口会切到不存在的场景");
            return false;
        }

        _practiceRunPath = ProjectSettings.GlobalizePath("user://run.json");
        _bestPath = ProjectSettings.GlobalizePath("user://best.json");
        if (!WriteUserFile("user://run.json", ValidRunJson))
        {
            return false;
        }

        // 练习局按生产语义活跃（练习场景里 Main._Ready 的 SetRunActive(!_hostDriven) 为真），
        // 探针显式声明同一条语义：否则「不落盘」的判据会退化成「非本局本来就不写」，判不到守卫本身。
        GameState.Instance.SetRunActive(true);
        _spawner.BossSpawned += OnPracticeProbeBossSpawned;
        _practiceSubscribed = true;
        return true;
    }

    /// <summary>预置一局运行条件：本趟必须从「无记录」起判——隔离用户目录里若已有记录文件，
    /// 「练习不写记录」就成了读旧值（判据空洞），故取不到干净起点即显式失败而不是删掉它重来。
    /// check_smoke.sh 每趟清空重建用户目录，正常路径走不到这里。</summary>
    private bool PreparePracticeProbeRun()
    {
        if (Godot.FileAccess.FileExists(_bestPath))
        {
            GD.PushError($"[practice-probe] 隔离用户目录里已有 {_bestPath}——本趟须从「无记录」起判（用户目录未清空？）");
            return false;
        }

        if (!Godot.FileAccess.FileExists(_practiceRunPath))
        {
            GD.PushError($"[practice-probe] 预置检查点写出失败（{_practiceRunPath} 不存在）——不落盘判据取不到");
            return false;
        }

        return true;
    }

    /// <summary>Boss 出场观测（生产 BossSpawned 信号）：只记录实例，不驱动生成。</summary>
    private void OnPracticeProbeBossSpawned(Boss boss)
    {
        _practiceBoss ??= boss;
    }

    /// <summary>练习局「不落盘」断言：预置的检查点必须原封不动，记录文件必须没出现。</summary>
    private bool VerifyPracticeProbeNoWrites()
    {
        var ok = true;
        if (!Godot.FileAccess.FileExists(_practiceRunPath))
        {
            GD.PushError("[practice-probe] 练习中死亡删掉了本局检查点——玩家的真实存档会被练习抹掉");
            ok = false;
        }

        if (Godot.FileAccess.FileExists(_bestPath))
        {
            GD.PushError("[practice-probe] 练习中写下了跨局记录 best.json——练习读数污染了玩家的记录");
            ok = false;
        }

        return ok;
    }

    // ---------------- 本局记录读出探针（--best-record-probe） ----------------

    /// <summary>
    /// 本局记录读出探针：在隔离用户目录里连跑两局，判跨局记录的三件语义——
    ///   ① 死亡一局后记录确实落盘，且**写出后读回**逐字段核对（键集恰好等于 core 编解码器的字段集，
    ///      值经生产读档口 FromFields 回读后与内存记录逐项一致）；
    ///   ② 记录里不含分数：键集比对是结构判据（多写一个 score 键即判红），
    ///      而不是「grep 一下文件里没有 score 字样」这种能被改名绕过的形态；
    ///   ③ 第二局打得更差时记录不回退（内存与盘上都不动），且不误报「新纪录」。
    /// 另断结算页/标题屏那行文本确实能被格式化出来（译文里占位符数与实参不匹配时，玩家看到的
    /// 是原样的 %s/%d）——判据就是两处读出共用的那套实参。
    /// </summary>
    private void TickBestRecordProbe()
    {
        switch (_bestStage)
        {
            case 0:
                if (_frame < 2)
                {
                    return;
                }

                _bestPath = ProjectSettings.GlobalizePath("user://best.json");
                _runPathForBest = ProjectSettings.GlobalizePath("user://run.json");
                if (!WriteUserFile("user://run.json", ValidRunJson))
                {
                    _bestRecordProbe = false;
                    return;
                }

                if (Godot.FileAccess.FileExists(_bestPath))
                {
                    GD.PushError($"[best-record-probe] 起点已有 {_bestPath}——本趟需要从「无记录」起判（用户目录未清空？）");
                    _bestRecordProbe = false;
                    return;
                }

                if (!GameState.Instance.BestKnown || GameState.Instance.Best != BestRecord.Empty)
                {
                    GD.PushError("[best-record-probe] 无档时记录未按「可信的空记录」起步——三态口径坏了");
                    _bestRecordProbe = false;
                    return;
                }

                // 第一局：本局活跃（生产语义）+ 一组明确的读数（存活 300s、3 只 Boss）
                GameState.Instance.SetRunActive(true);
                GameState.Instance.RunTime = 300.0;
                GameState.Instance.BossKills = 3;
                _bestStage = 1;
                _bestStageFrame = _frame;
                return;

            case 1:
                // 等一帧让难度时间档按新读数重算（记录里的最高难度档取的就是重算后的乘数）
                if (_frame - _bestStageFrame < BestRecordSettleFrames)
                {
                    return;
                }

                _bestFirstRun = new BestRecord(
                    GameState.Instance.RunTime, GameState.Instance.BossKills,
                    GameState.Instance.DifficultyMultiplier, GameState.Instance.GoalAchieved());
                if (_bestFirstRun.SurvivedSeconds < 300.0 || _bestFirstRun.BossKills != 3
                    || _bestFirstRun.MaxDifficulty <= 1.0)
                {
                    GD.PushError(GdFormat.Format(
                        "[best-record-probe] 第一局读数不合预期（存活 %.1f / Boss %d / 难度 %.3f）——"
                        + "第二局的「更差」前提取不到",
                        _bestFirstRun.SurvivedSeconds, _bestFirstRun.BossKills, _bestFirstRun.MaxDifficulty));
                    _bestRecordProbe = false;
                    return;
                }

                GameState.Instance.EmitSignal(GameState.SignalName.PlayerDied);
                _bestStage = 2;
                _bestStageFrame = _frame;
                return;

            case 2:
                if (_frame - _bestStageFrame < BestRecordSettleFrames)
                {
                    return;
                }

                if (!VerifyBestRecordWritten())
                {
                    _bestRecordProbe = false;
                    return;
                }

                // 第二局：更差的一局（存活 5s、0 只 Boss → 难度档退回 1.0）
                GameState.Instance.SetTreePaused(false); // 死亡会把树暂停，不恢复则难度档不会被重算
                GameState.Instance.RunTime = 5.0;
                GameState.Instance.BossKills = 0;
                _bestStage = 3;
                _bestStageFrame = _frame;
                return;

            case 3:
                if (_frame - _bestStageFrame < BestRecordSettleFrames)
                {
                    return;
                }

                var worse = new BestRecord(
                    GameState.Instance.RunTime, GameState.Instance.BossKills,
                    GameState.Instance.DifficultyMultiplier, GameState.Instance.GoalAchieved());
                if (worse.SurvivedSeconds >= _bestFirstRun.SurvivedSeconds
                    || worse.MaxDifficulty >= _bestFirstRun.MaxDifficulty
                    || worse.BossKills >= _bestFirstRun.BossKills)
                {
                    GD.PushError(GdFormat.Format(
                        "[best-record-probe] 第二局读数未严格劣于第一局（%.1f/%.3f/%d vs %.1f/%.3f/%d）——"
                        + "「不回退」判据取不到，拒绝判 clean",
                        worse.SurvivedSeconds, worse.MaxDifficulty, worse.BossKills,
                        _bestFirstRun.SurvivedSeconds, _bestFirstRun.MaxDifficulty, _bestFirstRun.BossKills));
                    _bestRecordProbe = false;
                    return;
                }

                GameState.Instance.EmitSignal(GameState.SignalName.PlayerDied);
                _bestStage = 4;
                _bestStageFrame = _frame;
                return;

            case 4:
                if (_frame - _bestStageFrame < BestRecordSettleFrames)
                {
                    return;
                }

                if (!VerifyBestRecordKept())
                {
                    _bestRecordProbe = false;
                    return;
                }

                // 「刷新记录」判定属本局状态，判据两半都要真局读数当前提（摆不出前提即判红，
                // 不静默降级成空转）：① 再刷一次真实局把判定重新摆出来，供后两半使用；
                // ② 之后的 900/6 第二次刷新是「练习终结不得留下判定」那半的前提。
                GameState.Instance.SetTreePaused(false);
                GameState.Instance.RunTime = 600.0;
                GameState.Instance.BossKills = 5;
                _bestStage = 5;
                _bestStageFrame = _frame;
                return;

            case 5:
                if (_frame - _bestStageFrame < BestRecordSettleFrames)
                {
                    return;
                }

                GameState.Instance.EmitSignal(GameState.SignalName.PlayerDied);
                _bestImprovedRun = new BestRecord(
                    GameState.Instance.RunTime, GameState.Instance.BossKills,
                    GameState.Instance.DifficultyMultiplier, GameState.Instance.GoalAchieved());
                if (!GameState.Instance.BestImprovedThisRun)
                {
                    GD.PushError("[best-record-probe] 更差一局之后再刷一局更好的读数未能标记「刷新记录」——"
                        + "复位段的判据前提取不到，拒绝判 clean");
                    _bestRecordProbe = false;
                    return;
                }

                // 半 ①：新一局不得继承上一局的判定（ResetRun 是「全新一局」的唯一入口）
                GameState.Instance.ResetRun();
                if (GameState.Instance.BestImprovedThisRun)
                {
                    GD.PushError("[best-record-probe] ResetRun 未复位「刷新记录」判定——"
                        + "新一局的结算页会读到上一局的判定（练习局与正局共用结算页）");
                    _bestRecordProbe = false;
                    return;
                }

                GameState.Instance.SetTreePaused(false);
                GameState.Instance.RunTime = 900.0;
                GameState.Instance.BossKills = 6;
                _bestStage = 6;
                _bestStageFrame = _frame;
                return;

            case 6:
                if (_frame - _bestStageFrame < BestRecordSettleFrames)
                {
                    return;
                }

                GameState.Instance.EmitSignal(GameState.SignalName.PlayerDied);
                _bestImprovedRun = new BestRecord(
                    GameState.Instance.RunTime, GameState.Instance.BossKills,
                    GameState.Instance.DifficultyMultiplier, GameState.Instance.GoalAchieved());
                if (!GameState.Instance.BestImprovedThisRun)
                {
                    GD.PushError("[best-record-probe] 复位段之后的真实局未标记「刷新记录」——"
                        + "练习终结段的判据前提取不到，拒绝判 clean");
                    _bestRecordProbe = false;
                    return;
                }

                // 半 ②：练习局终结（不产生判定的终结）不得留下判定——练习面板与正局共用结算页，
                // 残留的 true 会让玩家在练习里死一次就看到自己上一局的成绩被当作本局「新纪录」。
                // 走生产练习入口 BeginPractice（练习宿主 _EnterTree 调的就是它）+ 生产死亡信号。
                GameState.Instance.BeginPractice(PracticeSetup.Default);
                GameState.Instance.EmitSignal(GameState.SignalName.PlayerDied);
                if (!VerifyPracticeTerminalClearsImproved())
                {
                    _bestRecordProbe = false;
                    return;
                }

                GameState.Instance.EndPractice();
                GameState.Instance.SetTreePaused(false);

                // 坏档（损坏 / 版本不符）走生产读档口复算写门槛：两种档都不构成「可能更好的
                // 在盘记录」，故都必须可写——挡住的坏法是整场会话的记录静默丢失（版本不符更是永久）。
                if (!WriteUserFile("user://best.json", CorruptBestJson))
                {
                    _bestRecordProbe = false;
                    return;
                }

                GameState.Instance.LoadBestRecord();
                if (!GameState.Instance.BestKnown || GameState.Instance.Best != BestRecord.Empty)
                {
                    GD.PushError(GdFormat.Format(
                        "[best-record-probe] 损坏档读入后的写门槛/基线不对（known=%s 记录=%.1f/%d）——"
                        + "损坏档已被隔离，必须按无档处理（可写、基线为空记录）",
                        GameState.Instance.BestKnown, GameState.Instance.Best.SurvivedSeconds,
                        GameState.Instance.Best.BossKills));
                    _bestRecordProbe = false;
                    return;
                }

                if (!Godot.FileAccess.FileExists(_bestPath + ".corrupt"))
                {
                    GD.PushError($"[best-record-probe] 损坏档未被隔离（{_bestPath}.corrupt 不存在）——"
                        + "损坏档占着原路径时，后续写盘与「按无档处理」的口径都对不上");
                    _bestRecordProbe = false;
                    return;
                }

                GameState.Instance.SetRunActive(true);
                GameState.Instance.RunTime = 1200.0;
                GameState.Instance.BossKills = 7;
                _bestStage = 7;
                _bestStageFrame = _frame;
                return;

            case 7:
                if (_frame - _bestStageFrame < BestRecordSettleFrames)
                {
                    return;
                }

                _bestImprovedRun = new BestRecord(
                    GameState.Instance.RunTime, GameState.Instance.BossKills,
                    GameState.Instance.DifficultyMultiplier, GameState.Instance.GoalAchieved());
                GameState.Instance.EmitSignal(GameState.SignalName.PlayerDied);
                if (!VerifyBestRecordWritable("损坏档读入后"))
                {
                    _bestRecordProbe = false;
                    return;
                }

                if (!WriteUserFile("user://best.json", FutureVersionBestJson()))
                {
                    _bestRecordProbe = false;
                    return;
                }

                GameState.Instance.SetTreePaused(false);
                GameState.Instance.LoadBestRecord();
                if (!GameState.Instance.BestKnown || GameState.Instance.Best != BestRecord.Empty)
                {
                    GD.PushError(GdFormat.Format(
                        "[best-record-probe] 版本不符档读入后的写门槛/基线不对（known=%s 记录=%.1f/%d）——"
                        + "版本不符按无档处理（可写、基线为空记录），不得把未来档读数认成玩家的成绩",
                        GameState.Instance.BestKnown, GameState.Instance.Best.SurvivedSeconds,
                        GameState.Instance.Best.BossKills));
                    _bestRecordProbe = false;
                    return;
                }

                GameState.Instance.SetRunActive(true);
                GameState.Instance.RunTime = 1500.0;
                GameState.Instance.BossKills = 8;
                _bestStage = 8;
                _bestStageFrame = _frame;
                return;

            case 8:
                if (_frame - _bestStageFrame < BestRecordSettleFrames)
                {
                    return;
                }

                _bestImprovedRun = new BestRecord(
                    GameState.Instance.RunTime, GameState.Instance.BossKills,
                    GameState.Instance.DifficultyMultiplier, GameState.Instance.GoalAchieved());
                GameState.Instance.EmitSignal(GameState.SignalName.PlayerDied);
                if (!VerifyBestRecordWritable("版本不符档读入后"))
                {
                    _bestRecordProbe = false;
                    return;
                }

                GameState.Instance.SetTreePaused(false);
                GD.Print(GdFormat.Format(
                    "[best-record-probe] 两局语义成立（记录 %.0fs / Boss %d 不回退，键集 %d 项无分数）",
                    _bestFirstRun.SurvivedSeconds, _bestFirstRun.BossKills, BestRecordCodec.ToFields(_bestFirstRun).Count));
                _bestRecordProbe = false;
                return;
        }
    }

    /// <summary>坏档之后的落盘断言（写门槛）：本局终结必须**真的写出记录**（文件在、内容等于本局读数），
    /// 而不是被「盘上档读不出」挡住。只断 BestKnown 会放过「门槛放行但写路径别处仍早退」的实现，
    /// 故断言落在盘上文件的内容上。</summary>
    private bool VerifyBestRecordWritable(string label)
    {
        if (GameState.Instance.Best != _bestImprovedRun)
        {
            GD.PushError(GdFormat.Format(
                "[best-record-probe] {0}本局终结未把读数并进内存记录（%.1f/%d，期望 %.1f/%d）",
                label, GameState.Instance.Best.SurvivedSeconds, GameState.Instance.Best.BossKills,
                _bestImprovedRun.SurvivedSeconds, _bestImprovedRun.BossKills));
            return false;
        }

        if (!TryReadBestFile(out var onDisk, out var why))
        {
            GD.PushError(GdFormat.Format("[best-record-probe] {0}本局终结未写出记录：%s"
                + "——坏档之后整场会话的记录静默丢失", label, why));
            return false;
        }

        if (onDisk != _bestImprovedRun)
        {
            GD.PushError(GdFormat.Format(
                "[best-record-probe] {0}盘上记录内容不符（%.1f/%d/%.3f，期望 %.1f/%d/%.3f）",
                label, onDisk.SurvivedSeconds, onDisk.BossKills, onDisk.MaxDifficulty,
                _bestImprovedRun.SurvivedSeconds, _bestImprovedRun.BossKills, _bestImprovedRun.MaxDifficulty));
            return false;
        }

        return true;
    }

    /// <summary>练习终结后的断言（半 ②）：判定必须当场清掉，且记录（内存与盘上）不受练习读数影响——
    /// 只判「判定为假」会被「练习顺手把记录也回退成空」蒙过，故记录两侧一并判。</summary>
    private bool VerifyPracticeTerminalClearsImproved()
    {
        var ok = true;
        if (GameState.Instance.BestImprovedThisRun)
        {
            GD.PushError("[best-record-probe] 练习局终结留下了上一局的「刷新记录」判定——"
                + "练习死亡的结算页会把上一局的最好读数当本局成绩打「新纪录」");
            ok = false;
        }

        if (GameState.Instance.Best != _bestImprovedRun)
        {
            GD.PushError(GdFormat.Format(
                "[best-record-probe] 练习局改动了跨局记录（内存 %.1f/%d/%.3f，期望 %.1f/%d/%.3f）",
                GameState.Instance.Best.SurvivedSeconds, GameState.Instance.Best.BossKills,
                GameState.Instance.Best.MaxDifficulty,
                _bestImprovedRun.SurvivedSeconds, _bestImprovedRun.BossKills, _bestImprovedRun.MaxDifficulty));
            ok = false;
        }

        if (!TryReadBestFile(out var onDisk, out var why))
        {
            GD.PushError("[best-record-probe] 练习局之后记录读不出：" + why);
            return false;
        }

        if (onDisk != _bestImprovedRun)
        {
            GD.PushError(GdFormat.Format(
                "[best-record-probe] 盘上记录被练习局覆写（%.1f/%d/%.3f，期望 %.1f/%d/%.3f）——"
                + "练一次就改掉玩家的跨局记录",
                onDisk.SurvivedSeconds, onDisk.BossKills, onDisk.MaxDifficulty,
                _bestImprovedRun.SurvivedSeconds, _bestImprovedRun.BossKills, _bestImprovedRun.MaxDifficulty));
            ok = false;
        }

        return ok;
    }

    /// <summary>第一局落盘后的断言：文件在、键集恰好等于编解码器字段集、逐字段回读一致、
    /// 内存记录等于期望、且本局被标为「刷新了记录」（结算页据此打「新纪录」）。</summary>
    private bool VerifyBestRecordWritten()
    {
        var ok = true;
        if (!TryReadBestFile(out var onDisk, out var why))
        {
            GD.PushError("[best-record-probe] 死亡后记录未落盘：" + why);
            return false;
        }

        if (onDisk != _bestFirstRun)
        {
            GD.PushError(GdFormat.Format(
                "[best-record-probe] 读回的记录（%.1f/%d/%.3f）≠ 本局读数（%.1f/%d/%.3f）——"
                + "写出与读回不是同一件事",
                onDisk.SurvivedSeconds, onDisk.BossKills, onDisk.MaxDifficulty,
                _bestFirstRun.SurvivedSeconds, _bestFirstRun.BossKills, _bestFirstRun.MaxDifficulty));
            ok = false;
        }

        if (GameState.Instance.Best != _bestFirstRun)
        {
            GD.PushError("[best-record-probe] 内存里的记录与本局读数不符——信号消费方（结算页）会读到别的数");
            ok = false;
        }

        if (!GameState.Instance.BestImprovedThisRun)
        {
            GD.PushError("[best-record-probe] 首局未标记「刷新记录」——结算页不会打「新纪录」");
            ok = false;
        }

        if (!VerifyBestLineFormat(improved: true, out var lineWhy))
        {
            GD.PushError("[best-record-probe] " + lineWhy);
            ok = false;
        }

        // 记录与本局存档分区：死亡删档只作用于 run.json
        if (Godot.FileAccess.FileExists(_runPathForBest))
        {
            GD.PushError("[best-record-probe] 死亡后本局存档仍在——删档钩子没走（与记录的分区判据一同失效）");
            ok = false;
        }

        return ok;
    }

    /// <summary>第二局（更差）之后的断言：内存与盘上的记录都必须还是第一局的读数，
    /// 且不误标「刷新记录」（误标会让结算页对一局更差的成绩打「新纪录」）。</summary>
    private bool VerifyBestRecordKept()
    {
        var ok = true;
        if (GameState.Instance.Best != _bestFirstRun || GameState.Instance.BestImprovedThisRun)
        {
            GD.PushError(GdFormat.Format(
                "[best-record-probe] 更差的一局改动了记录（内存 %.1f/%d/%.3f，improved=%s）——"
                + "记录会随每一局下滑",
                GameState.Instance.Best.SurvivedSeconds, GameState.Instance.Best.BossKills,
                GameState.Instance.Best.MaxDifficulty, GameState.Instance.BestImprovedThisRun));
            ok = false;
        }

        if (!TryReadBestFile(out var onDisk, out var why))
        {
            GD.PushError("[best-record-probe] 第二局后记录读不出：" + why);
            return false;
        }

        if (onDisk != _bestFirstRun)
        {
            GD.PushError(GdFormat.Format(
                "[best-record-probe] 盘上记录被更差的一局覆写（%.1f/%d/%.3f）——重启游戏后玩家会看到退步的成绩",
                onDisk.SurvivedSeconds, onDisk.BossKills, onDisk.MaxDifficulty));
            ok = false;
        }

        if (!VerifyBestLineFormat(improved: false, out var lineWhy))
        {
            GD.PushError("[best-record-probe] " + lineWhy);
            ok = false;
        }

        return ok;
    }

    /// <summary>读出行的格式化判据：结算页（新纪录/历史最好）与标题屏（历史最好）那行文本都用
    /// core <c>BestRecord.FormatArgs</c> 装配实参，此处用同一套实参走一遍 GdFormat——译文里的占位符
    /// 与实参不匹配时（多一个 %d、写成 %f），玩家看到的是原样的占位符，而任何门禁都不判这一层。</summary>
    private bool VerifyBestLineFormat(bool improved, out string why)
    {
        var template = Tr(improved ? "BEST_NEW" : "BEST_LINE");
        var line = GdFormat.Format(template, BestRecord.FormatArgs(_bestFirstRun));
        why = string.Empty;
        if (line.Contains('%'))
        {
            why = GdFormat.Format("读出行的占位符没被实参填完（%s → 「%s」）——玩家会看到原样的 %%s/%%d",
                improved ? "BEST_NEW" : "BEST_LINE", line);
            return false;
        }

        var duration = BestRecord.FormatDuration(_bestFirstRun.SurvivedSeconds);
        if (!line.Contains(duration))
        {
            why = GdFormat.Format("读出行里没有存活时长的格式化结果（期望含「%s」，实际「%s」）", duration, line);
            return false;
        }

        return true;
    }

    /// <summary>读回 user://best.json 并逐字段核对（写出后读回，判据不依赖写侧自述）：
    /// 键集必须**恰好等于** core <see cref="BestRecordCodec"/> 的字段集——多一个键就是「记录里混进了
    /// 别的东西」（分数/战力一旦写进去，这条记录就从局末读数变成局外成长），少一个键是写入残缺；
    /// 值再经 FromFields（生产读档同一个口）回读，供调用方与内存记录逐项比对。</summary>
    private bool TryReadBestFile(out BestRecord record, out string why)
    {
        record = BestRecord.Empty;
        why = string.Empty;
        if (!Godot.FileAccess.FileExists(_bestPath))
        {
            why = $"文件不存在（{_bestPath}）";
            return false;
        }

        using var file = Godot.FileAccess.Open(_bestPath, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            why = $"打不开（{Godot.FileAccess.GetOpenError()}）";
            return false;
        }

        var parsed = Json.ParseString(file.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            why = $"根不是对象（{parsed.VariantType}）";
            return false;
        }

        var raw = parsed.AsGodotDictionary();
        var want = BestRecordCodec.ToFields(BestRecord.Empty).Keys;
        if (raw.Count != want.Count)
        {
            why = $"字段数 {raw.Count} ≠ 编解码器字段数 {want.Count}——记录里混进了别的东西（如分数）";
            return false;
        }

        foreach (var key in raw.Keys)
        {
            if (!want.Contains(key.AsStringName().ToString()))
            {
                why = $"出现字段表之外的键 {key}——记录里混进了别的东西（如分数）";
                return false;
            }
        }

        if (!VariantBridge.TryToClr(parsed, out var clr, out var error) || clr is not Dictionary<string, object?> fields)
        {
            why = $"Variant 树转 CLR 失败（{error}）";
            return false;
        }

        record = BestRecordCodec.FromFields(fields);
        return true;
    }
}
#endif

