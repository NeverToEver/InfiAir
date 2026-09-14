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
    public void Threshold_SaturationHugeIndex_TerminatesAndClamps()
    {
        // tamper 存档可把 milestone_count 传到 int 上限：曲线在百余圈内即饱和，
        // 逐圈推进若不做饱和早退就是 O(index) 的主线程挂死（旧实现上本用例跑不完即红）。
        var base8 = new long[] { 3000, 8000, 15000, 25000, 40000, 55000, 70000, 80000 };
        Assert.Equal(long.MaxValue, MilestoneCurve.Threshold(int.MaxValue, base8, 1.35, 1.0));
    }

    [Fact]
    public void ThresholdInt_HugeIndex_ClampsInsteadOfWrappingNegative()
    {
        var base8 = new long[] { 3000, 8000, 15000, 25000, 40000, 55000, 70000, 80000 };
        // 先钉住危险：long 阈值确实越过 int 上限（旧实现直接 (int) 转换会回绕成负）
        Assert.True(MilestoneCurve.Threshold(int.MaxValue, base8, 1.35, 1.5) > int.MaxValue);
        Assert.Equal(int.MaxValue, MilestoneCurve.ThresholdInt(int.MaxValue, base8, 1.35, 1.5));
    }

    [Fact]
    public void Threshold_LargeIndexWithinIntDomain_ReturnsTrueValue()
    {
        // 平坦曲线（cycle_mult=1）不饱和：旧实现 MaxCycles=100_000 硬停会把 index=200_000 的门槛
        // 静默截到第 100_001 圈（返回约一半的真值），里程碑因此提前触发——int 索引必须拿到真值。
        Assert.Equal(1000L * 200001L, MilestoneCurve.Threshold(200000, new long[] { 1000 }, 1.0, 1.0));
    }

    [Fact]
    public void Threshold_FlatCurveHugeIndex_DoesNotHang()
    {
        // 挂死保护：平坦曲线不饱和，逐圈推进在 int.MaxValue 索引上是 O(2^31) 的主线程挂死。
        // 正确实现走闭式（O(1)）；退化回逐圈时本用例会超时而非静默给出错值。
        Assert.Equal(
            ((long)int.MaxValue * 1000L) + 1000L,
            MilestoneCurve.Threshold(int.MaxValue, new long[] { 1000 }, 1.0, 1.0));
    }

    [Fact]
    public void Threshold_ShrinkingMultiplier_ConvergesWithoutHanging()
    {
        // 异常配置（cycle_mult<1，生产已钳 ≥1.0）：pow 下溢到 0 后每圈贡献恒为 0，必须立即收束。
        var t = MilestoneCurve.Threshold(int.MaxValue, new long[] { 1000, 2000 }, 0.5, 1.0);
        Assert.True(t > 0);
        Assert.Equal(MilestoneCurve.Threshold(5000, new long[] { 1000, 2000 }, 0.5, 1.0), t);
    }

    [Fact]
    public void Compute_NaNRuntime_TreatedAsNoTimeProgress()
    {
        // NaN 使 runTime<=0 与 >1e6 皆假，(long)Math.Floor(NaN) 得 long.MinValue → 难度巨负。
        // 前置 IsFinite：按无时间累进处理，只留 Boss 项。
        Assert.Equal(2.2, DifficultyCurve.Compute(double.NaN, 30, 1.5, 0.6, 2), 12);
        var t = DifficultyCurve.Compute(double.NaN, 30, 1.5, 1.0, 0);
        Assert.True(double.IsFinite(t), $"NaN 运行时产出非有限值：{t}");
        Assert.Equal(1.0, t, 12);
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

    [Fact]
    public void Compute_NonPositiveTimeStep_DropsTimeTerm()
    {
        // 步长非正 → 除零得 inf、double→long 转换越界；只留 Boss 项且保持有限
        Assert.Equal(2.2, DifficultyCurve.Compute(30, 0, 1.5, 0.6, 2), 12);
        Assert.Equal(2.2, DifficultyCurve.Compute(30, -5, 1.5, 0.6, 2), 12);
        Assert.Equal(1.0, DifficultyCurve.Compute(30, double.NaN, 1.5, 0.0, 0), 12);
    }
}
