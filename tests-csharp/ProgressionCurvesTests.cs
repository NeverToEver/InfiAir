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

    [Fact]
    public void DifficultyCurve_HugeOrNegativeRunTime_ClampedAndBounded()
    {
        // AB12：手改存档 elapsed 1e300 → 裸 (long)Math.Floor 未定义转换（实践得 long.MinValue）
        // 难度乘数巨负击穿单调防线；入口/曲线双保险后不溢出且返回有界正值
        // 上界 1e6 秒：step = floor(1e6/30) = 33333 → 1 + 33333×30/600×1.5 = 2500.975
        Assert.Equal(2500.975, DifficultyCurve.Compute(1e300, 30.0, 1.5, 0.6, 0), 3);
        Assert.Equal(2502.175, DifficultyCurve.Compute(1e300, 30.0, 1.5, 0.6, 2), 3); // +2 Boss → +1.2
        // 0/负值钳制：按 0 档计算（难度单调不减防线）
        Assert.Equal(1.0, DifficultyCurve.Compute(0.0, 30.0, 1.5, 0.6, 0));
        Assert.Equal(2.2, DifficultyCurve.Compute(-1.0, 30.0, 1.5, 0.6, 2)); // 1 + 0.6×2
        Assert.True(DifficultyCurve.Compute(-1e300, 30.0, 1.5, 0.6, 0) >= 1.0);
    }

    [Fact]
    public void Threshold_CycleMultiplierBelowOne_StaysMonotone()
    {
        // 2026-08-09 审计：cycle_mult<1（0.5 / 0.01 下限）仍单调——base 档差非负 × 正 mult
        foreach (double mult in new[] { 0.5, 0.01 })
        {
            long prev = -1;
            for (int i = 0; i < 32; i++)
            {
                long v = MilestoneCurve.Threshold(i, Base, mult, 1.0);
                Assert.True(v >= prev, $"cycleMult={mult} threshold({i}) 单调不回退");
                prev = v;
            }
        }
    }

    [Fact]
    public void Threshold_ZeroOrNegativeCycleMultiplier_DoesNotThrow()
    {
        // 2026-08-09 审计：core 不钳制 cycle_mult（钳制在 GameState 侧 0.01 下限）——
        // 0 值 pow(0,0)=1 首循环正常、后续循环增量为 0；负值域 pow 产生 NaN，
        // ToInt64 显式钳制保证确定性返回，不抛异常不挂死
        Assert.True(MilestoneCurve.Threshold(0, Base, 0.0, 1.0) > 0);
        Assert.Equal(80000, MilestoneCurve.Threshold(8, Base, 0.0, 1.0)); // 第二循环增量为 0
        _ = MilestoneCurve.Threshold(0, Base, -1.0, 1.0);
        _ = MilestoneCurve.Threshold(8, Base, -1.0, 1.0);
        _ = MilestoneCurve.Threshold(64, Base, -2.0, 1.0);
    }
}
