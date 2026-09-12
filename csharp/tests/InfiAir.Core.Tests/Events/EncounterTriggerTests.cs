using InfiAir.Core.Events;
using Xunit;

namespace InfiAir.Core.Tests.Events;

/// <summary>EncounterTrigger 契约测试：遭遇事件的「能不能触发」与「计时怎么走」。
/// 这两件事实原先散在 GameEventManager 的逐帧循环里，写了也没法验——
/// 分数门槛被绕过、资格不足时计时仍在推进，都只会表现为「事件来得莫名其妙」。</summary>
public sealed class EncounterTriggerTests
{
    private const int MinScore = 500;

    [Fact]
    public void Eligible_RequiresSelfReadyAndNoBossAndNoOtherEncounterAndScore()
    {
        Assert.True(EncounterTrigger.Eligible(true, false, false, MinScore, MinScore));
        Assert.False(EncounterTrigger.Eligible(false, false, false, MinScore, MinScore));
        Assert.False(EncounterTrigger.Eligible(true, true, false, MinScore, MinScore));
        Assert.False(EncounterTrigger.Eligible(true, false, true, MinScore, MinScore));
        Assert.False(EncounterTrigger.Eligible(true, false, false, MinScore - 1, MinScore));
    }

    [Fact]
    public void Eligible_ScoresExactlyAtThresholdPass()
    {
        // 门槛是「达到」而非「超过」：分数刚好压线必须放行（旧口径 >=）
        Assert.True(EncounterTrigger.Eligible(true, false, false, 500, 500));
        Assert.False(EncounterTrigger.Eligible(true, false, false, 499, 500));
    }

    [Fact]
    public void Advance_NotEligibleFreezesTimer()
    {
        // 资格不足（Boss 在场/同类事件在跑/事件自身冷却中）时计时必须冻结：
        // 否则「等了一会儿才排队」会变成 Boss 一结束立刻触发
        var step = EncounterTrigger.Advance(30.0f, 1.0f, 40.0f, eligible: false, score: 10_000, minScore: MinScore);
        Assert.Equal(30.0f, step.Remaining, 3);
        Assert.False(step.Due);
    }

    [Fact]
    public void Advance_BelowMinScoreFreezesTimer()
    {
        var step = EncounterTrigger.Advance(30.0f, 1.0f, 40.0f, eligible: true, score: MinScore - 1, minScore: MinScore);
        Assert.Equal(30.0f, step.Remaining, 3);
        Assert.False(step.Due);
    }

    [Fact]
    public void Advance_CountsDownWhileEligible()
    {
        var step = EncounterTrigger.Advance(30.0f, 1.5f, 40.0f, eligible: true, score: MinScore, minScore: MinScore);
        Assert.Equal(28.5f, step.Remaining, 3);
        Assert.False(step.Due);
    }

    [Fact]
    public void Advance_DueResetsToFullIntervalBeforeRolling()
    {
        // 到点先把计时复位到整段间隔再掷签：掷签失败从整段重新计，
        // 不能停在 0 附近每帧重掷（那等于把 trigger_chance 变成「迟早必中」）
        var step = EncounterTrigger.Advance(0.2f, 0.5f, 40.0f, eligible: true, score: MinScore, minScore: MinScore);
        Assert.True(step.Due);
        Assert.Equal(40.0f, step.Remaining, 3);
    }

    [Fact]
    public void Advance_DueWhenTimerHitsZeroExactly()
    {
        var step = EncounterTrigger.Advance(1.0f, 1.0f, 40.0f, eligible: true, score: MinScore, minScore: MinScore);
        Assert.True(step.Due);
        Assert.Equal(40.0f, step.Remaining, 3);
    }

    [Fact]
    public void Advance_OverdueTimerStillYieldsSingleDue()
    {
        // 巨帧（钳制前可达 1.4s）一次性跨过整段间隔也只掷一次签
        var step = EncounterTrigger.Advance(0.1f, 5.0f, 40.0f, eligible: true, score: MinScore, minScore: MinScore);
        Assert.True(step.Due);
        Assert.Equal(40.0f, step.Remaining, 3);
    }
}
