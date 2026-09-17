using InfiAir.Core.Input;
using Xunit;

namespace InfiAir.Core.Tests.Input;

/// <summary>
/// 长按蓄力的三条边界：不到阈值不触发、到了阈值只触发一次、松手即复位。
/// 钉住的是原实现六处各写一遍时最容易分叉的形态——按住期间逐帧重复触发（触发动作被反复执行）、
/// 松手不清 HUD 进度（条常驻屏幕）、进度不钳 1（超阈值的比例渗进表现层）。
/// </summary>
public sealed class HoldChargeTests
{
    [Fact]
    public void BelowThreshold_ChargesWithoutTriggering()
    {
        var charge = new HoldCharge(1.5f);
        Assert.Equal(HoldChargePhase.Charging, charge.Tick(0.5f, true));
        Assert.Equal(HoldChargePhase.Charging, charge.Tick(0.5f, true));

        Assert.True(charge.Holding);
        Assert.False(charge.Triggered);
        Assert.Equal(2.0f / 3.0f, charge.Progress, 5);
    }

    [Fact]
    public void AtThreshold_TriggersOncePerHold()
    {
        var charge = new HoldCharge(1.5f);
        Assert.Equal(HoldChargePhase.Charging, charge.Tick(1.0f, true));

        // 恰达阈值即触发（闭区间，与既有 `>=` 判据一致）
        Assert.Equal(HoldChargePhase.Triggered, charge.Tick(0.5f, true));

        // 关键：仍然按住时不得重复触发（否则触发动作每帧各跑一次）
        Assert.Equal(HoldChargePhase.Charging, charge.Tick(0.5f, true));
        Assert.Equal(HoldChargePhase.Charging, charge.Tick(0.5f, true));

        // 超阈值后进度不越界
        Assert.Equal(1.0f, charge.Progress);
    }

    [Fact]
    public void Release_ReportsReleasedOnceThenIdle()
    {
        var charge = new HoldCharge(1.0f);
        charge.Tick(1.0f, true);

        Assert.Equal(HoldChargePhase.Released, charge.Tick(0.01f, false));
        Assert.Equal(0.0f, charge.Elapsed);
        Assert.False(charge.Holding);

        // 已经松手过：不再重复报 Released（调用方不必每帧收条）
        Assert.Equal(HoldChargePhase.Idle, charge.Tick(0.01f, false));
    }

    [Fact]
    public void ReleaseThenPressAgain_RequiresFullThreshold()
    {
        var charge = new HoldCharge(1.0f);
        charge.Tick(1.0f, true);
        charge.Tick(0.01f, false);

        // 触发锁随松手解除：重新按住从头累计，半程不触发
        Assert.Equal(HoldChargePhase.Charging, charge.Tick(0.4f, true));
        Assert.False(charge.Triggered);
        Assert.Equal(HoldChargePhase.Triggered, charge.Tick(0.6f, true));
    }

    [Fact]
    public void GateClosedMidCharge_ResetsLikeRelease()
    {
        // 门控失效（事件进行中/演出期/已死亡）与松手同义：进度归零、HUD 条据此收起
        var charge = new HoldCharge(2.0f);
        charge.Tick(1.0f, true);
        Assert.Equal(HoldChargePhase.Released, charge.Tick(0.1f, false));
        Assert.Equal(0.0f, charge.Progress);
    }
}
