using InfiAir.Core;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>FuelGauge 契约测试：低量警戒线只有一份——液色（IsLow）与刻度警示区（IsWarnTick）
/// 必须由同一阈值决定。守的是「阈值两处各写一份」的半红量槽：改一处后刻度先红而液色不红，
/// 无门禁可判。</summary>
public sealed class FuelGaugeTests
{
    [Fact]
    public void WarnRatio_IsTheSingleThreshold()
    {
        Assert.Equal(0.3f, FuelGauge.WarnRatio, 5);
    }

    [Fact]
    public void IsLow_TogglesAtTheSameThresholdUsedByTicks()
    {
        // 阈值上下各取一点：液色警戒与刻度警示必须在同一点翻转（多任一偏移即半红）。
        Assert.True(FuelGauge.IsLow(FuelGauge.WarnRatio - 0.001f));
        Assert.False(FuelGauge.IsLow(FuelGauge.WarnRatio));
        Assert.False(FuelGauge.IsLow(FuelGauge.WarnRatio + 0.001f));
        // 刻度档位：低于阈值且处于警戒态才标红——与液色同一判据
        Assert.True(FuelGauge.IsWarnTick(FuelGauge.WarnRatio - 0.001f, warn: true));
        Assert.False(FuelGauge.IsWarnTick(FuelGauge.WarnRatio - 0.001f, warn: false));
        Assert.False(FuelGauge.IsWarnTick(FuelGauge.WarnRatio, warn: true));
    }

    [Fact]
    public void IsLow_NonFinite_DoesNotReportWarning()
    {
        Assert.False(FuelGauge.IsLow(float.NaN));
        Assert.False(FuelGauge.IsLow(float.PositiveInfinity));
        Assert.True(FuelGauge.IsLow(float.NegativeInfinity));
    }
}
