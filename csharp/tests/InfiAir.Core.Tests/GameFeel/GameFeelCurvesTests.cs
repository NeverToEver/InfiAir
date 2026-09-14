using InfiAir.Core.GameFeel;
using Xunit;

namespace InfiAir.Core.Tests.GameFeel;

/// <summary>手感曲线核心测试：命中顿帧时序与屏幕震动 trauma。
/// 两者都是「看不见但坏了会让整局手感发平或卡死」的纯逻辑——顿帧推进用真实时间（缩放域会冻死）、
/// trauma 用幂次映射（线性映射会让高频小事件糊成持续抖动），公式回归在这里先炸。</summary>
public sealed class GameFeelCurvesTests
{
    // ---------------- HitStopTimeline ----------------

    [Fact]
    public void DurationForTier_MapsEachTierMonotonically()
    {
        var normal = HitStopTimeline.DurationForTier(HitStopTier.Normal, 0.04, 0.09, 0.16, 0.20);
        var crit = HitStopTimeline.DurationForTier(HitStopTier.Crit, 0.04, 0.09, 0.16, 0.20);
        var kill = HitStopTimeline.DurationForTier(HitStopTier.Kill, 0.04, 0.09, 0.16, 0.20);
        var heavy = HitStopTimeline.DurationForTier(HitStopTier.Heavy, 0.04, 0.09, 0.16, 0.20);

        Assert.Equal(0.04, normal, 6);
        Assert.Equal(0.09, crit, 6);
        Assert.Equal(0.16, kill, 6);
        Assert.Equal(0.20, heavy, 6);
        Assert.True(normal < crit && crit < kill && kill < heavy);
    }

    [Fact]
    public void DurationForTier_NonPositiveOrNonFinite_IsZero()
    {
        Assert.Equal(0.0, HitStopTimeline.DurationForTier(HitStopTier.Normal, 0.0, 0.09, 0.16, 0.20));
        Assert.Equal(0.0, HitStopTimeline.DurationForTier(HitStopTier.Crit, 0.04, -1.0, 0.16, 0.20));
        Assert.Equal(0.0, HitStopTimeline.DurationForTier(HitStopTier.Kill, 0.04, 0.09, double.NaN, 0.20));
        Assert.Equal(0.0, HitStopTimeline.DurationForTier((HitStopTier)99, 0.04, 0.09, 0.16, 0.20));
    }

    [Fact]
    public void Request_TakesMaxNotSum()
    {
        var timeline = new HitStopTimeline();
        timeline.Request(0.04);
        timeline.Request(0.09);
        Assert.Equal(0.09, timeline.Remaining, 6); // 取大者，不叠加

        timeline.Request(0.04); // 更小者不缩短已有时长
        Assert.Equal(0.09, timeline.Remaining, 6);
    }

    [Fact]
    public void Advance_RealDeltaExpiresAndClampsAtZero()
    {
        var timeline = new HitStopTimeline();
        timeline.Request(0.05);
        timeline.Advance(0.02);
        Assert.True(timeline.Active);
        Assert.Equal(0.03, timeline.Remaining, 6);

        timeline.Advance(1.0);
        Assert.False(timeline.Active);
        Assert.Equal(0.0, timeline.Remaining, 6); // 不给负数
    }

    [Fact]
    public void Advance_ZeroDelta_DoesNotExpire()
    {
        // 顿帧把时间缩放压到 0 时引擎帧长为 0——若误用缩放 delta 推进，此处会永远 Active（冻死）
        var timeline = new HitStopTimeline();
        timeline.Request(0.05);
        for (var i = 0; i < 100; i++)
        {
            timeline.Advance(0.0);
        }

        Assert.True(timeline.Active);
        Assert.Equal(0.05, timeline.Remaining, 6);
    }

    [Fact]
    public void Request_NegativeOrNonFinite_Ignored()
    {
        var timeline = new HitStopTimeline();
        timeline.Request(-1.0);
        timeline.Request(double.NaN);
        timeline.Request(double.PositiveInfinity);
        Assert.False(timeline.Active);
        Assert.Equal(0.0, timeline.Remaining, 6); // 只判 !Active 抓不到「NaN 被存进 _remaining」

        // 坏输入不得污染后续合法请求（NaN 若入内，`seconds > _remaining` 恒假 → 合法请求被吞）
        timeline.Request(0.05);
        Assert.True(timeline.Active);
        Assert.Equal(0.05, timeline.Remaining, 6);
    }

    [Fact]
    public void Clear_ResetsResidual()
    {
        var timeline = new HitStopTimeline();
        timeline.Request(0.16);
        timeline.Clear();
        Assert.Equal(0.0, timeline.Remaining, 6); // 只判 !Active 时把 Clear 写成 _remaining=-1 也过
    }

    // ---------------- TraumaShake ----------------

    [Fact]
    public void Add_AccumulatesAndSaturatesAtOne()
    {
        var shake = new TraumaShake();
        shake.Add(0.4);
        Assert.Equal(0.4, shake.Trauma, 6);
        shake.Add(0.4);
        Assert.Equal(0.8, shake.Trauma, 6);
        shake.Add(0.9);
        Assert.Equal(TraumaShake.MaxTrauma, shake.Trauma, 6); // 饱和，不爆表
    }

    [Fact]
    public void Advance_DecaysLinearlyToZero()
    {
        var shake = new TraumaShake();
        shake.Add(0.8);
        shake.Advance(0.2, 1.5);
        Assert.Equal(0.5, shake.Trauma, 6);

        shake.Advance(1.0, 1.5);
        Assert.Equal(0.0, shake.Trauma, 6);
    }

    [Fact]
    public void Magnitude_IsSquared_SoSmallImpactsStaySubtle()
    {
        var shake = new TraumaShake();
        shake.Add(0.5);
        // 幂次映射：半创伤只给四分之一位移——这是「小事件几乎无感」的机制本身
        Assert.Equal(0.25, shake.Magnitude(), 6);
        Assert.Equal(6.0, shake.Offset(24.0), 6);
    }

    [Fact]
    public void Magnitude_ExponentIsClampedToPositive()
    {
        var shake = new TraumaShake(0.0); // 非法指数回退 2.0，不得退化成线性
        shake.Add(0.5);
        Assert.Equal(0.25, shake.Magnitude(), 6);
    }

    [Fact]
    public void Advance_NonPositiveRecovery_DoesNotSilentlyClear()
    {
        // 速率 0 视为「不衰减」（与震源配置缺省语义一致），不得静默清零掩盖配置错误
        var shake = new TraumaShake();
        shake.Add(0.6);
        shake.Advance(1.0, 0.0);
        Assert.Equal(0.6, shake.Trauma, 6);
        shake.Advance(1.0, double.NaN);
        Assert.Equal(0.6, shake.Trauma, 6);
    }

    [Fact]
    public void Add_NonPositiveStress_Ignored()
    {
        var shake = new TraumaShake();
        shake.Add(0.0);
        shake.Add(-0.5);
        shake.Add(double.NaN);
        Assert.Equal(0.0, shake.Trauma, 6);
        Assert.False(shake.Active);
    }

    [Fact]
    public void Clear_ResetsTrauma()
    {
        var shake = new TraumaShake();
        shake.Add(1.0);
        shake.Clear();
        Assert.Equal(0.0, shake.Trauma, 6); // 只判 !Active 时把 Clear 写成 trauma=-1 也过
        Assert.Equal(0.0, shake.Magnitude(), 6);
    }
}
