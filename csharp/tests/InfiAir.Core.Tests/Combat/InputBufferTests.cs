using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>InputBuffer 契约测试：输入缓冲的时序判定（§2.14③，窗口 0.1s、balance.json
/// player.input_buffer_window）。这套判定若留在 Player 节点里，坏法不崩不报错——
/// 窗口边界差一帧会让「就绪前一帧的按下」被丢或被提前一帧触发；消费不清会让一次
/// 缓冲补出多次触发；暂存寿命长于冷却剩余会让被外部编排取消的按下在解锁后「隔空触发」。</summary>
public sealed class InputBufferTests
{
    /// <summary>物理帧步长（无头固定步长 60，帧数＝模拟时长）。</summary>
    private const float Frame = 1.0f / 60.0f;

    /// <summary>生产取值（balance.json player.input_buffer_window）。</summary>
    private const float Window = 0.1f;

    private static InputBuffer NewBuffer() => new();

    [Fact]
    public void Arm_WithinWindow_Pends()
    {
        var buffer = NewBuffer();
        buffer.Arm(0.08f, Window);

        Assert.True(buffer.PendingRemaining > 0.0f);
    }

    [Fact]
    public void Arm_OutsideWindow_DoesNotPend()
    {
        var buffer = NewBuffer();
        buffer.Arm(0.11f, Window);

        Assert.Equal(0.0f, buffer.PendingRemaining);
    }

    [Fact]
    public void Arm_AlreadyReady_DoesNotPend()
    {
        // 恰好就绪（剩余 ≤ 0）不缓冲：直接触发的语义不由缓冲替代
        var buffer = NewBuffer();
        buffer.Arm(0.0f, Window);

        Assert.Equal(0.0f, buffer.PendingRemaining);
    }

    [Fact]
    public void Arm_NegativeWindow_DoesNotPend()
    {
        // 非法窗口按不缓冲处理（与 CfgFx 钳制 ≥0 的取值口径一致）
        var buffer = NewBuffer();
        buffer.Arm(0.08f, -1.0f);

        Assert.Equal(0.0f, buffer.PendingRemaining);
    }

    [Fact]
    public void ConsumeIfReady_ReadyFrame_TriggersOnce()
    {
        var buffer = NewBuffer();
        buffer.Arm(2.0f * Frame, Window);

        Assert.False(buffer.ConsumeIfReady(actionReady: false)); // 未就绪不触发、不消费
        buffer.Tick(Frame); // 冷却与暂存同帧长推进，此刻双双归零
        Assert.True(buffer.ConsumeIfReady(actionReady: true));
        Assert.False(buffer.ConsumeIfReady(actionReady: true)); // 消费即清：一次缓冲只补一次触发
    }

    [Fact]
    public void Pending_ConsumableAtReadinessFrame()
    {
        // 反竞态契约：暂存必须**活过冷却最早归零的那一帧**——帧内次序是消费判定先于
        // Tick，就绪帧上消费判定看到的寿命还剩整整一帧（与浮点残渣无关），早按不得被丢
        var buffer = NewBuffer();
        buffer.Arm(3.0f * Frame, Window);
        buffer.Tick(Frame);
        buffer.Tick(Frame);
        buffer.Tick(Frame); // 冷却此刻归零（就绪帧）

        Assert.True(buffer.ConsumeIfReady(actionReady: true));
        Assert.False(buffer.ConsumeIfReady(actionReady: true)); // 消费即清
    }

    [Fact]
    public void Pending_LockedThroughReadiness_ExpiresShortlyAfter()
    {
        // 就绪帧没消费（被外部编排锁住）→ 暂存在其后一两帧内作废，不无限期等下去、
        // 不「隔空触发」；过期帧随寿命读数的浮点残渣漂移一帧，故只断「数帧内必死」
        var buffer = NewBuffer();
        buffer.Arm(3.0f * Frame, Window);
        for (var i = 0; i < 3; i++)
        {
            buffer.Tick(Frame); // 走到就绪帧（未消费）
        }

        for (var i = 0; i < 3; i++)
        {
            buffer.Tick(Frame);
            if (buffer.PendingRemaining == 0.0f)
            {
                break;
            }
        }

        Assert.Equal(0.0f, buffer.PendingRemaining);
        Assert.False(buffer.ConsumeIfReady(actionReady: true));
    }

    [Fact]
    public void Clear_DropsPending()
    {
        var buffer = NewBuffer();
        buffer.Arm(0.05f, Window);
        buffer.Clear();

        Assert.Equal(0.0f, buffer.PendingRemaining);
        Assert.False(buffer.ConsumeIfReady(actionReady: true));
    }
}
