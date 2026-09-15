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
    /// <summary>台词停留时长（秒）——与 CommOverlay.HoldTime 同值，非可调手感值。</summary>
    public const float HoldTime = 3.5f;

    /// <summary>打字机字间隔（秒）——与 CommOverlay.CharInterval 同值。</summary>
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
    /// 顺延值就是「该句结束时刻」，不再因子句叠加而二次顺延——进度台词一次事件至多一句。</summary>
    public static float NextIntelAt(
        float earliestAt, float now, int lineStage, float lineStartedAt, float lineOnScreenTime)
    {
        if (lineStage == LineStageNone)
        {
            return earliestAt;
        }

        var lineEnds = lineStartedAt + lineOnScreenTime;
        return lineEnds > now ? lineEnds : earliestAt;
    }

    /// <summary>到点当帧的是否允许播提示：顺延只保证「排程那一刻」该句已结束，
    /// 逐帧到点时台词若仍在场上（长句、或判断被改成只算一次）就不播，下一帧再判。
    /// 提示是排程入口，不会因此丢失。</summary>
    public static bool IntelAllowed(int lineStage, float lineStartedAt, float lineOnScreenTime, float now)
        => lineStage == LineStageNone || lineStartedAt + lineOnScreenTime <= now;
}
