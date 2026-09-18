namespace InfiAir.Core.Tutorial;

/// <summary>阶段的目标形态：决定该阶段喂什么事件、达成判据是什么、目标行需要哪些补参。</summary>
public enum TutorialGoalKind
{
    /// <summary>训练靶（阶段 1）：击杀 <c>TargetCount</c> 个标记靶。</summary>
    Marksmanship,

    /// <summary>机动（阶段 2）：加速与相位突进各 <c>TargetCount</c> 次。</summary>
    Maneuver,

    /// <summary>实战（阶段 3）：击杀 <c>TargetCount</c> 架敌机。</summary>
    Combat,

    /// <summary>弹反（阶段 4）：在弹反窗口内成功弹反 <c>TargetCount</c> 发敌弹。</summary>
    Parry,

    /// <summary>母舰停靠（阶段 5）：长按蓄力触发一次召唤并完成对接。</summary>
    Dock,

    /// <summary>返航与基地（阶段 6）：长按蓄力触发一次返航，并实际打开一次增幅面板。</summary>
    Homecoming,

    /// <summary>首领（阶段 7）：逼首领进入狂暴。</summary>
    BossEnrage,
}

/// <summary>目标行文案的补参取值来源。顺序即占位符顺序——文案表只写占位符，取值一律来自
/// 本枚举在调用方的解析：数字来自 <see cref="TutorialProgress"/> 或引擎侧注入的平衡值，
/// 键名来自实际绑定（<c>GameState.ActionHintText</c>——按最近使用设备分档，键鼠档报键名、手柄档报按钮/扳机/摇杆标签）。</summary>
public enum TutorialArg
{
    /// <summary>移动四向的绑定键（四向各自的键拼成一段）。</summary>
    MoveKeys,

    /// <summary>瞄准的设备标签（键鼠＝鼠标，手柄＝右摇杆）。</summary>
    AimHint,

    /// <summary>开火的设备标签（键鼠＝鼠标左键，手柄＝RT）。</summary>
    FireHint,

    /// <summary>加速动作的绑定键。</summary>
    BoostKey,

    /// <summary>相位突进动作的绑定键。</summary>
    DashKey,

    /// <summary>召唤母舰动作的绑定键。</summary>
    DockKey,

    /// <summary>返航动作的绑定键。</summary>
    HomecomingKey,

    /// <summary>跳过本阶段所用动作（放弃出击）的绑定键。</summary>
    SkipKey,

    /// <summary>弹反动作的绑定键。</summary>
    ParryKey,

    /// <summary>增幅面板动作的绑定键。</summary>
    AugmentKey,

    /// <summary>天赋面板动作的绑定键（固定键，不可改）。</summary>
    TalentKey,

    /// <summary>已弹反次数。</summary>
    ParryCount,

    /// <summary>弹反目标次数。</summary>
    ParryGoal,

    /// <summary>加速已达成次数。</summary>
    BoostCount,

    /// <summary>加速目标次数。</summary>
    BoostGoal,

    /// <summary>相位突进已达成次数。</summary>
    DashCount,

    /// <summary>相位突进目标次数。</summary>
    DashGoal,

    /// <summary>已击杀数。</summary>
    KillCount,

    /// <summary>击杀目标数。</summary>
    KillGoal,

    /// <summary>蓄力进度百分比（进行中的替换行用）。</summary>
    ChargePercent,

    /// <summary>返航蓄力所需秒数（平衡值 `effects.home_charge_time`）。</summary>
    ChargeSeconds,

    /// <summary>相位突进的燃料门槛百分比（平衡值 `player.dash.fuel_ratio`）。</summary>
    DashFuelPercent,

    /// <summary>首领狂暴的血量阈值百分比（平衡值 `boss.enrage.hp_ratio`）。</summary>
    EnragePercent,
}

/// <summary>
/// 单个教程阶段：标题键 / 目标键 / 目标形态 / 目标计数 / 需要插值的动作与数值。
/// <paramref name="TargetCount"/> 的语义按形态分两种——计数型（训练靶 / 实战 / 弹反：需要达成的数量；
/// 机动：加速与突进各自需要的次数）与单次触发型（停靠 / 返航 / 首领：达成就完成，恒为 1）。
/// <paramref name="ChargeKey"/> 非空时表示该阶段另有「蓄力进行中」的替换行（百分比）；
/// <paramref name="FollowUpKey"/> 非空时表示达标过程中还有一行后续目标（补参见 <paramref name="FollowUpArgs"/>）。
/// </summary>
public sealed record TutorialStage(
    TutorialGoalKind Goal,
    string TitleKey,
    string ObjectiveKey,
    int TargetCount,
    TutorialArg[] ObjectiveArgs,
    string ChargeKey = "",
    TutorialArg[]? ChargeArgs = null,
    string FollowUpKey = "",
    TutorialArg[]? FollowUpArgs = null);

/// <summary>
/// 教程阶段表（纯逻辑，零 Godot 依赖）：七个阶段的顺序、标题与目标文案键、目标计数、
/// 目标行补参取值来源一处写定，节点、探针与文案三者共用。
///
/// 为什么要有这一份：此前六阶段的顺序、目标数（3 / 5）、补参形态与「哪个阶段要哪个键」全部
/// 散在 <c>Tutorial.cs</c> 的分支与玩家文案里——同一事实（目标击杀数）在代码常数与文案
/// 「(%d/5)」里各有一份，改一处即静默分叉：玩家看到的目标数与实际达成条件不一致，而编译、
/// 单测、冒烟都不会有任何信号。节奏参数（推进延迟、跳过长按、死亡重开延迟）同理放在此表。
///
/// 本表不含任何数值型游戏平衡取值：击杀数属教程节奏（定稿在此），蓄力秒数与狂暴阈值由
/// 引擎侧从 balance 注入（core 不产生数值）。
/// </summary>
public static class TutorialCurriculum
{
    /// <summary>阶段数（属性而非静态字段：静态字段按声明序初始化，写在表前会读到未初始化的表）。</summary>
    public static int StageCount => Stages.Length;

    /// <summary>过关推进延迟（s）：达标后停顿一拍再进下一阶段（给结算音效与清场留出时间）。</summary>
    public const float PassDelaySeconds = 1.0f;

    /// <summary>跳过本阶段所需的长按时长（s）。</summary>
    public const float SkipHoldSeconds = 1.0f;

    /// <summary>死亡后重开本阶段的延迟（s）：先让玩家看清「战机已损毁」，再清场重来。</summary>
    public const float RetryDelaySeconds = 1.5f;

    /// <summary>跳过本阶段的长按提示文案键（常驻在目标行下方，与阶段无关）。</summary>
    public const string SkipHintKey = "TUT_SKIP_HINT";

    /// <summary>跳过提示的补参（长按所用动作的绑定键）。</summary>
    public static readonly TutorialArg[] SkipHintArgs = { TutorialArg.SkipKey };

    /// <summary>弹反段场上保有的射击型靶机数（被反射弹击落或寿命到期离场即按此数补刷；
    /// 弹反段的教学对象是「朝玩家飞来的敌弹」，场上无靶机＝无弹可教）。</summary>
    public const int ParryDummyCount = 3;

    /// <summary>七阶段阶段表（顺序即推进顺序）。目标行的补参顺序必须与文案表占位符逐位对齐，
    /// 由单测钉住——补参错位时玩家看到的是错位的数字/键名，不会报错。</summary>
    public static readonly TutorialStage[] Stages =
    {
        new(
            TutorialGoalKind.Marksmanship,
            "TUT_S1_TITLE",
            "TUT_S1_OBJ",
            TargetCount: 3,
            ObjectiveArgs: new[] { TutorialArg.MoveKeys, TutorialArg.AimHint, TutorialArg.FireHint, TutorialArg.KillCount, TutorialArg.KillGoal }
        ),
        new(
            TutorialGoalKind.Maneuver,
            "TUT_S2_TITLE",
            "TUT_S2_OBJ",
            TargetCount: 2,
            ObjectiveArgs: new[]
            {
                TutorialArg.BoostKey, TutorialArg.BoostCount, TutorialArg.BoostGoal,
                TutorialArg.DashKey, TutorialArg.DashFuelPercent, TutorialArg.DashCount, TutorialArg.DashGoal,
            }
        ),
        new(
            TutorialGoalKind.Combat,
            "TUT_S3_TITLE",
            "TUT_S3_OBJ",
            TargetCount: 5,
            ObjectiveArgs: new[] { TutorialArg.KillGoal, TutorialArg.KillCount, TutorialArg.KillGoal }
        ),
        new(
            TutorialGoalKind.Parry,
            "TUT_S4_TITLE",
            "TUT_S4_OBJ",
            TargetCount: 2,
            ObjectiveArgs: new[] { TutorialArg.ParryKey, TutorialArg.ParryCount, TutorialArg.ParryGoal }
        ),
        new(
            TutorialGoalKind.Dock,
            "TUT_S5_TITLE",
            "TUT_S5_OBJ",
            TargetCount: 1,
            ObjectiveArgs: new[] { TutorialArg.DockKey },
            ChargeKey: "TUT_S5_CHARGE",
            ChargeArgs: new[] { TutorialArg.ChargePercent },
            FollowUpKey: "TUT_S5_DOCK"
        ),
        new(
            TutorialGoalKind.Homecoming,
            "TUT_S6_TITLE",
            "TUT_S6_OBJ",
            TargetCount: 1,
            ObjectiveArgs: new[] { TutorialArg.HomecomingKey, TutorialArg.ChargeSeconds },
            ChargeKey: "TUT_S6_CHARGE",
            ChargeArgs: new[] { TutorialArg.ChargePercent },
            FollowUpKey: "TUT_S6_DOCK",
            FollowUpArgs: new[] { TutorialArg.AugmentKey, TutorialArg.TalentKey }
        ),
        new(
            TutorialGoalKind.BossEnrage,
            "TUT_S7_TITLE",
            "TUT_S7_OBJ",
            TargetCount: 1,
            ObjectiveArgs: new[] { TutorialArg.EnragePercent }
        ),
    };

    /// <summary>取阶段定义：索引越界一律钳回合法区间（读档残留、跳过推进等外部输入都不该
    /// 让「显示的阶段」与「跑的阶段」脱钩）。</summary>
    public static TutorialStage At(int index) => Stages[ClampStage(index)];

    /// <summary>索引归一：越界钳到 [0, StageCount-1]。</summary>
    public static int ClampStage(int index)
    {
        if (index < 0)
        {
            return 0;
        }

        return index >= StageCount ? StageCount - 1 : index;
    }

    /// <summary>续接起点归一：存档里的阶段索引（含越界值与负数）一律落到合法区间。</summary>
    public static int ResumeStage(int savedStage) => ClampStage(savedStage);

    /// <summary>是否最后一个阶段（过后进入完成结算，不再有下一阶段）。</summary>
    public static bool IsLast(int index) => ClampStage(index) >= StageCount - 1;

    /// <summary>下一阶段索引（已到末阶段时返回末阶段——调用方据 <see cref="IsLast"/> 分流完成）。</summary>
    public static int Next(int index) => ClampStage(ClampStage(index) + 1);
}
