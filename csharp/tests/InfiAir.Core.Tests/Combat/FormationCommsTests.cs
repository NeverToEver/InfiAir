using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>编队通讯序列契约：战术提示不得顶掉进度台词。
/// 原实现在引擎层只比较「提示到点与否」，进度台词（首次战损/首次拆弹触发，可早于提示）只闪
/// 0.5~2s 就被舰桥提示顶掉，与声明的序列（接敌警告 → 战术提示 → 至多一条进度台词 → 结算）相反。
/// 时序判定下沉后由单测钉住「顺延到该句结束」，顺延值本身不再回头改 _lineStage 触发点。</summary>
public sealed class FormationCommsTests
{
    // 台词在场上限：打字 CharInterval × 字数 + 停留 HoldTime（与 balance/CommOverlay 同口径）
    private const float LineWindow = 3.9f;

    [Fact]
    public void NextIntelAt_LineStillPlayingAtHintTime_DefersPastLineEnd()
    {
        // 提示定在 4.0s，进度台词 2.0s 起播、占场 3.9s（到 5.9s）：
        // 提示必须顺延到该句结束，否则刚播的台词被顶掉
        Assert.Equal(5.9f, FormationComms.NextIntelAt(4.0f, 4.0f, 1, 2.0f, LineWindow));
    }

    [Fact]
    public void NextIntelAt_NoProgressLineYet_KeepsEarliestAt()
    {
        // 尚未开播（_lineStage == 0）不得凭空顺延
        Assert.Equal(2.5f, FormationComms.NextIntelAt(2.5f, 1.0f, 0, 0.0f, LineWindow));
    }

    [Fact]
    public void NextIntelAt_LineStartedAfterHintTime_StillDefers()
    {
        // 台词在提示时刻之后才开播（先到的提示尚未播，战损发生在提示前一瞬）：
        // 仍以台词为准顺延——两个槽位互斥，谁都不许打断已在场的那句
        Assert.Equal(3.5f + LineWindow, FormationComms.NextIntelAt(4.0f, 3.0f, 1, 3.5f, LineWindow));
    }

    [Fact]
    public void NextIntelAt_LineAlreadyFinishedByHintTime_KeepsEarliestAt()
    {
        // 台词 1.0s 起播、4.9s 结束；提示定在 6.0s——两者本就不重叠，不顺延
        Assert.Equal(6.0f, FormationComms.NextIntelAt(6.0f, 6.0f, 1, 1.0f, LineWindow));
    }

    [Fact]
    public void NextIntelAt_LineEndsExactlyAtHintTime_KeepsEarliestAt()
    {
        // 边界：台词恰在提示时刻播完（开播 0 + 占场 4.0 = 4.0）不判冲突，
        // 否则同一刻的紧邻两句会被无谓地推后一整句
        Assert.Equal(4.0f, FormationComms.NextIntelAt(4.0f, 4.0f, 1, 0.0f, LineWindow));
    }

    [Fact]
    public void NextIntelAt_StageClearBeforeHint_KeepsEarliestAt()
    {
        // 进度台词槽位未占（_lineStage == 0）：提示按最早时刻正常播
        Assert.Equal(4.0f, FormationComms.NextIntelAt(4.0f, 2.0f, 0, 0.0f, 3.9f));
    }

    [Fact]
    public void NextIntelAt_LineEndsBeforeEarliestAt_NeverSchedulesEarlierThanFloor()
    {
        // 台词占场窗口整个落在 [now, earliestAt) 内：该句 0s 起播、3.9s 结束，提示定 4.0s。
        // 只返回「该句结束」会给出 3.9s —— 早于设计下限 4.0s，等于绕过「投弹后 4s」的门。
        // 顺延的语义是「只推后、不提前」，下界仍由 earliestAt 把住。
        Assert.Equal(4.0f, FormationComms.NextIntelAt(4.0f, 0.0f, 1, 0.0f, LineWindow));
        // 台词结束恰在 now 与 earliestAt 之间的任意点同样不得给出早于 4.0 的时刻
        Assert.Equal(4.0f, FormationComms.NextIntelAt(4.0f, 1.0f, 1, 2.0f, 1.5f));
    }

    [Fact]
    public void NextIntelAt_DeferredTimeIsNotDeferredAgain()
    {
        // 顺延值即「该句结束」本身：以它为 now 再算一次不得再顺延一句
        // （引擎侧只在 BeginRun 算一次，此用例防的是判定被改成回头重排时自锁）
        var deferred = FormationComms.NextIntelAt(4.0f, 4.0f, 1, 2.0f, LineWindow);
        Assert.Equal(deferred, FormationComms.NextIntelAt(deferred, deferred, 1, 2.0f, LineWindow));
    }

    [Fact]
    public void IntelAllowed_LineStillOnScreen_IsFalse()
    {
        // 到点当帧的守卫：顺延只保证「计算时」该句已结束；到点时若仍有台词在场就不播
        Assert.False(FormationComms.IntelAllowed(1, 4.0f, LineWindow, 4.0f));
        Assert.False(FormationComms.IntelAllowed(2, 0.0f, LineWindow, 1.0f));
    }

    [Fact]
    public void IntelAllowed_NoLineOrLineFinished_IsTrue()
    {
        Assert.True(FormationComms.IntelAllowed(0, 0.0f, LineWindow, 1.0f));
        Assert.True(FormationComms.IntelAllowed(1, 0.0f, LineWindow, LineWindow));
        Assert.True(FormationComms.IntelAllowed(1, 0.0f, LineWindow, 9.0f));
    }

    [Fact]
    public void LineOnScreenTime_TypingPlusHold()
    {
        // 10 字：0.03 × 10 + 3.5（打字时长按字数算，停留是定稿常量）
        Assert.Equal(3.8f, FormationComms.LineOnScreenTime(10), 3);
        // 空串/坏字数退化到只算停留，不产生负值窗口
        Assert.Equal(3.5f, FormationComms.LineOnScreenTime(0));
        Assert.Equal(3.5f, FormationComms.LineOnScreenTime(-3));
    }
}
