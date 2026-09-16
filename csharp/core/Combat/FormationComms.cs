namespace InfiAir.Core.Combat;

/// <summary>
/// 编队遭遇的通讯时序（纯逻辑，零 Godot 依赖）：进度台词与战术提示共用一个台词槽位
/// （<c>CommOverlay.ShowLine</c> 是「新句顶掉旧句」），谁先占谁在场上，另一句必须让位。
/// 声明的序列是「接敌警告 → 战术提示 → 至多一条进度台词 → 结算」，而进度台词由事件驱动
/// （首次战损 / 首次拆弹，可在提示定刻之前发生），单看「提示到点与否」会让刚播的进度台词
/// 只闪零点几秒就被顶掉——顺序反过来，玩家听到的是「战术提示截断战损台词」。
/// 下沉的理由：冲突窗口是一串时刻比较（提示定刻 × 台词开播时刻 × 台词占场时长），
/// 引擎侧照抄一份必然漂移，且时序观感在无头探针里断不出来。
/// </summary>
public static class FormationComms
{
    /// <summary>台词停留时长（秒）——单源在此，引擎侧 <c>CommOverlay</c> 直接引用
    /// （引擎侧另存一份副本时，只调观感就会让本模块推算的「提示最早可播时刻」失真）。</summary>
    public const float HoldTime = 3.5f;

    /// <summary>打字机字间隔（秒）——单源在此，引擎侧 <c>CommOverlay</c> 直接引用
    /// （占场时长按字数算，副本分叉会让顺延量算错一整句）。</summary>
    public const float CharInterval = 0.03f;

    /// <summary>进度台词槽位空（未播任何进度台词）。</summary>
    public const int LineStageNone = 0;

    /// <summary>一句台词的总占场时长：打字（字间隔 × 字数）+ 停留。字数 ≤0 时只剩停留，
    /// 不产生负值窗口（负窗口会让冲突判定把「已在场」的台词当成已播完）。</summary>
    public static float LineOnScreenTime(int chars)
        => HoldTime + (CharInterval * (chars > 0 ? chars : 0));

    /// <summary>战术提示的播报时刻（<c>_elapsed</c> 口径）：
    /// 进度台词槽位为空时取 <paramref name="earliestAt"/>；
    /// 槽位已占时，若该句在 <paramref name="now"/> 仍未播完（含在 <paramref name="now"/> 之后
    /// 才开播的情形）则顺延到该句结束，否则照旧。
    /// 顺延值是 **max(earliestAt, 该句结束时刻)**：台词占场窗口落在 [now, earliestAt) 内时，
    /// 只返回该句结束会让提示**早于**设计口径的下限播出（earliestAt 是「投弹后 4s、警告台词
    /// 播完」的语义，早播等于空谈）。顺延含义是「只推后、不提前」，故下界仍由 earliestAt 把住。
    /// 顺延只顺「一句」——进度台词一次事件至多一句。</summary>
    public static float NextIntelAt(
        float earliestAt, float now, int lineStage, float lineStartedAt, float lineOnScreenTime)
    {
        if (lineStage == LineStageNone)
        {
            return earliestAt;
        }

        var lineEnds = lineStartedAt + lineOnScreenTime;
        return lineEnds > now ? MathF.Max(earliestAt, lineEnds) : earliestAt;
    }

    /// <summary>到点当帧的是否允许播提示（「现在是提示的窗口吗」的唯一判据）：
    /// 状态门——只允许在轰炸段（<paramref name="bombingRun"/>）内播出。排程点设在轰炸段起点，
    /// 但顺延可能把它推到离场段：离场只有 1.5s 且紧接着结算台词，提示要么被结算台词顶掉
    /// （单槽位），要么落在结算画面上；全歼路径更会让排程在离场被复位而永不播出。
    /// 台词槽位门——顺延只保证「排程那一刻」该句已结束，逐帧到点时台词若仍在场上（长句、
    /// 或判断被改成只算一次）就不播，下一帧再判。两半同属一个窗口判定，分开写在引擎侧必漏一条
    /// （原实现漏的正是状态门）。引擎侧的义务：离开轰炸段时显式作废排程，
    /// 让「不播」成为确定性结果而不是悬着的排程。</summary>
    public static bool IntelAllowed(
        bool bombingRun, int lineStage, float lineStartedAt, float lineOnScreenTime, float now)
        => bombingRun && (lineStage == LineStageNone || lineStartedAt + lineOnScreenTime <= now);
}
