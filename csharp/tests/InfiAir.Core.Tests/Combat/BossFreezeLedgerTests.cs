using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>Boss 冻结/pending 记账契约：冻结期间的到期只记一次、解冻即兑现（不累积不丢失），
/// 打断路径（丢弃补触发）必须清除陈旧标记。原实现丢弃分支先 return、不清 pending——
/// 返航/死亡打断后 pending 残留，下一次任何遭遇正常收场都会命中陈旧标记、凭空补触发一只
/// 绕过分数门与最小间隔的 Boss，且无报错。</summary>
public sealed class BossFreezeLedgerTests
{
    [Fact]
    public void Release_TriggerPending_ConsumesDue()
    {
        var ledger = new BossFreezeLedger();
        ledger.Hold();
        ledger.NoteDue();
        Assert.True(ledger.Release(triggerPending: true));
        Assert.False(ledger.Pending);
        Assert.False(ledger.Frozen);
    }

    [Fact]
    public void Release_AbortDiscardsDue_StaleFlagNeverFiresLater()
    {
        // 复现缺陷形态：打断释放后 pending 必须清零，
        // 否则下一次正常收场（Hold→Release(true)，期间无到期）会凭陈旧标记补触发
        var ledger = new BossFreezeLedger();
        ledger.Hold();
        ledger.NoteDue();
        Assert.False(ledger.Release(triggerPending: false));
        Assert.False(ledger.Pending);

        ledger.Hold();
        Assert.False(ledger.Release(triggerPending: true));
    }

    [Fact]
    public void Release_OtherHolderStillFrozen_KeepsDueForLastHolder()
    {
        // 深度计数：先释放的事件不得消费共享冻结窗口里的到期，由最后一位持有者决定兑现或丢弃
        var ledger = new BossFreezeLedger();
        ledger.Hold();
        ledger.Hold();
        ledger.NoteDue();
        Assert.True(ledger.Frozen);
        Assert.False(ledger.Release(triggerPending: true));
        Assert.True(ledger.Pending);
        Assert.True(ledger.Frozen);
        Assert.True(ledger.Release(triggerPending: true));
        Assert.False(ledger.Pending);
    }

    [Fact]
    public void Release_AbortByLastHolder_ClearsDueEvenWithOtherHoldersEarlier()
    {
        var ledger = new BossFreezeLedger();
        ledger.Hold();
        ledger.Hold();
        ledger.NoteDue();
        Assert.False(ledger.Release(triggerPending: false));
        Assert.True(ledger.Pending);
        Assert.False(ledger.Release(triggerPending: false));
        Assert.False(ledger.Pending);
    }

    [Fact]
    public void Release_WithoutHold_IsNoOp()
    {
        // 释放必须与持有成对：未持有时释放不得改深度、不得凭空补触发
        var ledger = new BossFreezeLedger();
        Assert.False(ledger.Release(triggerPending: true));
        Assert.Equal(0, ledger.Depth);
        Assert.False(ledger.Frozen);
    }

    [Fact]
    public void NoteDue_WhileNotFrozen_DoesNotLatch()
    {
        // 未冻结时的到期由自然门控当帧直接触发，不该记 pending——否则下一次释放会重复补触发
        var ledger = new BossFreezeLedger();
        ledger.NoteDue();
        Assert.False(ledger.Pending);
        ledger.Hold();
        Assert.False(ledger.Release(triggerPending: true));
    }

    [Fact]
    public void NoteDue_RepeatedDues_LatchOnce()
    {
        // 一次冻结窗口内多次到期只兑现一次（Boss 不连出）
        var ledger = new BossFreezeLedger();
        ledger.Hold();
        ledger.NoteDue();
        ledger.NoteDue();
        ledger.NoteDue();
        Assert.True(ledger.Release(triggerPending: true));
        Assert.False(ledger.Release(triggerPending: true));
    }
}
