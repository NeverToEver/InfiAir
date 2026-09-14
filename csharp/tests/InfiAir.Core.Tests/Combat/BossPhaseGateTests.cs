using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>Boss 阶段门控测试。核心不变量是「受击后血量单调不增、只钳 0」——
/// 原实现把跌破狂暴线的血量抬回该线，既违反「血条只降」的可读性，也让致死一击跳过整个狂暴段。</summary>
public sealed class BossPhaseGateTests
{
    [Fact]
    public void ApplyDamage_NeverRaisesHp()
    {
        // 1000 血、狂暴线 30%（=300）：打到 250 必须停在 250，不得抬回 300
        var after = BossPhaseGate.ApplyDamage(1000.0, 750.0);
        Assert.Equal(250.0, after, 6);
    }

    [Fact]
    public void ApplyDamage_ClampsAtZeroOnly()
    {
        Assert.Equal(0.0, BossPhaseGate.ApplyDamage(100.0, 500.0), 6);
        Assert.Equal(0.0, BossPhaseGate.ApplyDamage(100.0, 100.0), 6);
    }

    [Fact]
    public void ApplyDamage_MonotonicOverManyHits()
    {
        var hp = 1000.0;
        var prev = hp;
        // 覆盖从满血到致死的一整段：任何一击后血量都不得高于命中前
        for (var i = 0; i < 200; i++)
        {
            hp = BossPhaseGate.ApplyDamage(hp, 7.0);
            Assert.True(hp <= prev, $"第 {i} 击后血量上抬：{hp} > {prev}");
            prev = hp;
        }

        Assert.Equal(0.0, hp, 6);
    }

    [Fact]
    public void ShouldEnrage_TriggersAtOrBelowLineExactlyOnce()
    {
        // 1000 血、线 30%：301 不触发，300 触发，0 不触发（已死），二次不再触发
        Assert.False(BossPhaseGate.ShouldEnrage(301.0, 1000.0, 0.3, false));
        Assert.True(BossPhaseGate.ShouldEnrage(300.0, 1000.0, 0.3, false));
        Assert.False(BossPhaseGate.ShouldEnrage(0.0, 1000.0, 0.3, false));
        Assert.False(BossPhaseGate.ShouldEnrage(250.0, 1000.0, 0.3, true));
    }

    [Fact]
    public void ShouldEnrage_InvalidInputs_DoNotTrigger()
    {
        Assert.False(BossPhaseGate.ShouldEnrage(double.NaN, 1000.0, 0.3, false));
        Assert.False(BossPhaseGate.ShouldEnrage(100.0, 0.0, 0.3, false)); // 上限 0 → 线为 0，不触发
        Assert.False(BossPhaseGate.ShouldEnrage(100.0, 1000.0, double.NaN, false)); // 线退化为 0
    }

    [Fact]
    public void SingleHitAcrossBothLines_EnrageAtTrueHp_NoHeal()
    {
        // 场景：1000 血、P2 线 70%（=700）、狂暴线 30%（=300）
        // 一发 780 从满血打到 220 —— 必须停在 220（不得抬回 300），且同时满足 P2 与狂暴
        var after = BossPhaseGate.ApplyDamage(1000.0, 780.0);
        Assert.Equal(220.0, after, 6);
        Assert.True(BossPhaseGate.ShouldEnterPhase2(after, 1000.0, 0.7, true));
        Assert.True(BossPhaseGate.ShouldEnrage(after, 1000.0, 0.3, false));
    }

    [Fact]
    public void LethalHit_FromAboveLine_IsLethal()
    {
        // 致死一击必须致死（不再被「抬回线」救活，也不再绕过死亡结算）
        var after = BossPhaseGate.ApplyDamage(1000.0, 1000.0);
        Assert.Equal(0.0, after, 6);
        Assert.False(BossPhaseGate.ShouldEnrage(after, 1000.0, 0.3, false)); // 已死，不转狂暴
    }

    [Fact]
    public void ShouldEnterPhase2_OnlyFromPhase1AndAlive()
    {
        Assert.True(BossPhaseGate.ShouldEnterPhase2(700.0, 1000.0, 0.7, true));
        Assert.False(BossPhaseGate.ShouldEnterPhase2(700.0, 1000.0, 0.7, false)); // 已非一阶段
        Assert.False(BossPhaseGate.ShouldEnterPhase2(0.0, 1000.0, 0.7, true));    // 已死
        Assert.False(BossPhaseGate.ShouldEnterPhase2(701.0, 1000.0, 0.7, true));
    }
}
