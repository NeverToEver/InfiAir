namespace InfiAir.Core;

/// <summary>
/// 燃料量槽的警戒口径（纯逻辑，零 Godot 依赖）：低量警戒线只有这一份，HUD 的液色/低量脉冲与
/// 量槽自身的刻度警示区都按它判定。
///
/// 为什么必须有这一份：原实现把 0.3 在 HUD（液色警戒）与 FuelTank（刻度区标红）各写一份，
/// 改一处即出「刻度先红而液色不红」的半红量槽——两个症状来自同一个比例，却互不知情，
/// 也没有任何门禁能判出分叉。
/// </summary>
public static class FuelGauge
{
    /// <summary>低量警戒线（比例）：低于此值液色转警示色，刻度警示区同步标红。</summary>
    public const float WarnRatio = 0.3f;

    /// <summary>比例是否进入低量警戒。NaN 与任何值比较恒假，按不警戒处理（不误报危态）。</summary>
    public static bool IsLow(float ratio) => ratio < WarnRatio;

    /// <summary>刻度是否落在警示区：档位比例低于警戒线，且当前处于警戒态（由外部下发——
    /// 量槽不自查液位，警示态的唯一来源是 SetWarn）。</summary>
    public static bool IsWarnTick(float ratio, bool warn) => warn && IsLow(ratio);
}
