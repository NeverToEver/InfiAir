namespace InfiAir.Core.Progression;

/// <summary>
/// 难度乘数线性 ramp 的唯一实现：×(1 + factor×(D−1))。
/// D ≤ 1 时不反向削弱（钳 1.0）；非有限 D、非有限/非正 factor 一律回退 1.0（坏配置不得把量放大成 NaN 或倒扣）。
///
/// DifficultyScaling 与 RewardScaling 原先各有一份逐字相同的私有实现——同一口径两处维护，
/// 改一处漏一处会让「敌方量」与「奖励量」的难度曲线静默分叉。集中到此，两边都只引用此处。
/// </summary>
internal static class DifficultyRamp
{
    /// <summary>线性 ramp：×(1 + factor×(D−1))。</summary>
    public static double Linear(double difficulty, double factor)
    {
        if (!double.IsFinite(difficulty) || !double.IsFinite(factor) || factor <= 0.0)
        {
            return 1.0;
        }

        var d = difficulty <= 1.0 ? 1.0 : difficulty;
        return 1.0 + factor * (d - 1.0);
    }
}
