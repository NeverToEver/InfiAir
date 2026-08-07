using InfiAir.Core.Progression;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>
/// MilestoneCurve / DifficultyCurve（2026-08-07 迁移）单测：逐条对齐原 GDScript
/// milestone_threshold / _recompute_difficulty 语义；抽查值与 test/difficulty_test.gd、
/// test/balance_test.gd 的断言值一致（3000/8000/84050/90800/188000/4500/120000/126075）。
/// </summary>
public sealed class ProgressionCurvesTests
{
    // 对齐 autoload/game_state.gd MILESTONE_BASE 与 MILESTONE_CYCLE_MULT
    private static readonly long[] Base = [3000, 8000, 15000, 25000, 40000, 55000, 70000, 80000];
    private const double CycleMult = 1.35;

    [Fact]
    public void Threshold_FirstCycle_MatchesBaseline()
    {
        Assert.Equal(3000, MilestoneCurve.Threshold(0, Base, CycleMult, 1.0));
        Assert.Equal(8000, MilestoneCurve.Threshold(1, Base, CycleMult, 1.0));
        Assert.Equal(80000, MilestoneCurve.Threshold(7, Base, CycleMult, 1.0));
    }

    [Fact]
    public void Threshold_SecondCycle_GrowsByCycleMultiplier()
    {
        // 80000 + 3000×1.35 / +5000×1.35 / 第二循环末 80000+80000×1.35（difficulty_test 抽查值）
        Assert.Equal(84050, MilestoneCurve.Threshold(8, Base, CycleMult, 1.0));
        Assert.Equal(90800, MilestoneCurve.Threshold(9, Base, CycleMult, 1.0));
        Assert.Equal(188000, MilestoneCurve.Threshold(15, Base, CycleMult, 1.0));
    }

    [Fact]
    public void Threshold_DifficultyMultiplier_ScalesThreshold()
    {
        // hard ×1.5：首档 4500 / 末档 120000 / 循环档 126075（difficulty_test 抽查值）
        Assert.Equal(4500, MilestoneCurve.Threshold(0, Base, CycleMult, 1.5));
        Assert.Equal(120000, MilestoneCurve.Threshold(7, Base, CycleMult, 1.5));
        Assert.Equal(126075, MilestoneCurve.Threshold(8, Base, CycleMult, 1.5));
    }

    [Fact]
    public void Threshold_MonotoneNonDecreasing()
    {
        long prev = -1;
        for (int i = 0; i < 64; i++)
        {
            long v = MilestoneCurve.Threshold(i, Base, CycleMult, 1.0);
            Assert.True(v >= prev, $"threshold({i}) 单调不回退");
            prev = v;
        }
    }

    [Fact]
    public void Threshold_EmptyBase_ReturnsZero()
    {
        Assert.Equal(0, MilestoneCurve.Threshold(5, [], CycleMult, 1.0));
        // 负 index 走 maxi(index, 0) 语义（原 GDScript 同款）：视为首档
        Assert.Equal(3000, MilestoneCurve.Threshold(-3, Base, CycleMult, 1.0));
    }

    [Fact]
    public void Threshold_HugeIndex_ClampsInsteadOfOverflow()
    {
        // A 审计：极大 index 不溢出 UB（原 GDScript 断言 ≥0 且非 int32 哨兵值）；C# 侧显式钳制
        long v = MilestoneCurve.Threshold(99999, Base, CycleMult, 1.0);
        Assert.True(v >= 0);
        Assert.NotEqual(2147483647, v);
        Assert.Equal(long.MaxValue, v);
    }

    [Fact]
    public void CountThresholdsUpTo_MatchesThresholdLoop()
    {
        foreach (long score in new long[] { 0, 2999, 3000, 3001, 80000, 84049, 84050, 126075, 1_000_000_000 })
        {
            int expected = 0;
            while (expected < MilestoneCurve.MaxIterations
                && MilestoneCurve.Threshold(expected, Base, CycleMult, 1.0) <= score)
            {
                expected++;
            }
            Assert.Equal(expected, MilestoneCurve.CountThresholdsUpTo(score, Base, CycleMult, 1.0));
        }
    }

    [Fact]
    public void CountThresholdsUpTo_EmptyBase_ReturnsZero()
    {
        Assert.Equal(0, MilestoneCurve.CountThresholdsUpTo(9999, [], CycleMult, 1.0));
    }

    [Fact]
    public void CountThresholdsUpTo_FlatCurve_HitsCap()
    {
        // 手改 base 使曲线增量收敛（单档 base=[100]，threshold(i)=100×(i+1)）：大分数下
        // 逐档推进永不越过 score → 挂死守卫封顶 MaxIterations（原 GDScript ms_cap 语义）
        Assert.Equal(MilestoneCurve.MaxIterations, MilestoneCurve.CountThresholdsUpTo(1_000_000, [100], 1.0, 1.0));
        Assert.Equal(0, MilestoneCurve.CountThresholdsUpTo(99, [100], 1.0, 1.0));
    }

    [Fact]
    public void DifficultyCurve_MatchesGdscriptFormula()
    {
        // 1 + per_boss_kill×kills + step×step_sec/600×per_ten_minutes（step = floor(run_time/step_sec)）
        Assert.Equal(1.0, DifficultyCurve.Compute(0, 30.0, 1.5, 0.6, 0));
        Assert.Equal(1.0, DifficultyCurve.Compute(29.9, 30.0, 1.5, 0.6, 0));
        Assert.Equal(1.15, DifficultyCurve.Compute(61.0, 30.0, 1.5, 0.6, 0));   // 2 档 → +0.15
        Assert.Equal(2.95, DifficultyCurve.Compute(300.0, 30.0, 1.5, 0.6, 2));  // 2 Boss + 10 档
    }
}
