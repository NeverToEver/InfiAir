using InfiAir.Core.Progression;
using Xunit;

namespace InfiAir.Core.Tests.Progression;

/// <summary>进程曲线纯函数测试：里程碑阈值（分数门槛/天赋点来源）与难度乘数曲线。
/// 两条曲线驱动整局节奏，公式回归（比如幂次溢出、除零）在这里先炸。</summary>
public sealed class ProgressionCurvesTests
{
    private static readonly long[] Base = { 100, 200, 300 };

    [Fact]
    public void Threshold_FirstIndex_IsBaseTimesDifficulty()
    {
        Assert.Equal(100L, MilestoneCurve.Threshold(0, Base, 1.35, 1.0));
        Assert.Equal(200L, MilestoneCurve.Threshold(0, Base, 1.35, 2.0));
    }

    [Fact]
    public void Threshold_EmptyTable_ReturnsZero()
    {
        Assert.Equal(0L, MilestoneCurve.Threshold(5, System.Array.Empty<long>(), 1.35, 1.0));
    }

    [Fact]
    public void Threshold_FlatCycle_SumsWholeCycles()
    {
        // cycle_mult=1：第 4 档（index 3）= 首圈全和 300 + 下一步 100 = 400
        Assert.Equal(400L, MilestoneCurve.Threshold(3, Base, 1.0, 1.0));
    }

    [Fact]
    public void Threshold_IsMonotonicOverLongHorizon()
    {
        long prev = 0;
        for (var i = 0; i < 60; i++)
        {
            var t = MilestoneCurve.Threshold(i, Base, 1.35, 1.5);
            Assert.True(t >= prev, $"index {i} 回退：{t} < {prev}");
            prev = t;
        }
    }

    [Fact]
    public void Threshold_GiantIndex_ClampsWithoutOverflow()
    {
        // pow 指数增长到 inf 必须被钳有限值，不得溢出 long 或抛异常
        var t = MilestoneCurve.Threshold(100000, Base, 1.35, 1.0);
        Assert.True(t > 0);
        Assert.True(t <= long.MaxValue);
    }

    [Fact]
    public void Compute_ZeroRuntime_ReturnsBossTermOnly()
    {
        Assert.Equal(2.2, DifficultyCurve.Compute(0, 30, 1.5, 0.6, 2), 12);
    }

    [Fact]
    public void Compute_NegativeRuntime_TreatedAsZero()
    {
        Assert.Equal(1.6, DifficultyCurve.Compute(-5, 30, 1.5, 0.6, 1), 12);
    }

    [Fact]
    public void Compute_TimeTerm_QuantizesByStep()
    {
        // 30s 档量化：30s 与 59s 同档（1×30/600×1.5 = 0.075），29s 未进档
        Assert.Equal(1.0, DifficultyCurve.Compute(29, 30, 1.5, 0.0, 0), 12);
        Assert.Equal(1.075, DifficultyCurve.Compute(30, 30, 1.5, 0.0, 0), 12);
        Assert.Equal(1.075, DifficultyCurve.Compute(59, 30, 1.5, 0.0, 0), 12);
        // 60s 进第二档：2×30/600×1.5 = 0.15
        Assert.Equal(1.15, DifficultyCurve.Compute(60, 30, 1.5, 0.0, 0), 12);
    }

    [Fact]
    public void Compute_GiantRuntime_ClampsToFinite()
    {
        var t = DifficultyCurve.Compute(1e9, 30, 1.5, 0.0, 0);
        Assert.True(double.IsFinite(t));
        Assert.Equal(DifficultyCurve.Compute(1e6, 30, 1.5, 0.0, 0), t, 12);
    }

    [Fact]
    public void Compute_IsMonotonicInRuntime()
    {
        var prev = 0.0;
        for (var seconds = 0.0; seconds <= 3600; seconds += 300)
        {
            var v = DifficultyCurve.Compute(seconds, 30, 1.5, 0.6, 1);
            Assert.True(v >= prev);
            prev = v;
        }
    }
}
