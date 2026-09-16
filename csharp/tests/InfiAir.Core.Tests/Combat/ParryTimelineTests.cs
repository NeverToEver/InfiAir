using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>ParryTimeline 契约测试：弧光弹反盾的相位时间轴与硬冷却起点（生产值 duration 0.8 /
/// active_time 0.5 / cooldown 3.0，balance.json player.parry）。
/// 这套判定原先整块留在 PlayerParry 节点里，引擎冒烟只跑「不崩」；两种坏法都不崩不报错——
/// 相位边界少了 epsilon 容差会永久卡在边界相位（盾再也进不了有效窗口），冷却起点从「流程结束」
/// 挪到「流程开始」则占空比翻倍（盾变常驻资源）。</summary>
public sealed class ParryTimelineTests
{
    /// <summary>物理帧步长（无头固定步长 60，帧数＝模拟时长）。</summary>
    private const float Frame = 1.0f / 60.0f;

    private const float Duration = 0.8f;
    private const float ActiveTime = 0.5f;
    private const float CooldownMax = 3.0f;

    /// <summary>相位时长下限（引擎层 CfgFx.IntervalFloor，由调用方注入而非 core 自带常量）。</summary>
    private const float Floor = 0.05f;

    private static ParryTimeline Production()
    {
        var timeline = new ParryTimeline();
        timeline.Configure(Duration, ActiveTime, CooldownMax, Floor);
        return timeline;
    }

    /// <summary>空转到流程结束（RECOVER 完成回 IDLE 的那一帧）。</summary>
    private static void CompleteFlow(ParryTimeline timeline)
    {
        while (timeline.IsFlowing())
        {
            timeline.Tick(Frame);
        }
    }

    [Fact]
    public void FlowSpendsExactPhaseLengthsAtProductionValues()
    {
        var timeline = Production();
        Assert.True(timeline.TryStart());
        var windup = 0;
        var active = 0;
        var recover = 0;
        while (timeline.IsFlowing())
        {
            switch (timeline.Phase)
            {
                case ParryPhase.Windup:
                    windup++;
                    break;
                case ParryPhase.Active:
                    active++;
                    break;
                case ParryPhase.Recover:
                    recover++;
                    break;
                default:
                    break;
            }

            timeline.Tick(Frame);
        }

        Assert.Equal(9, windup); // 前摇 0.15s
        Assert.Equal(30, active); // 有效窗口恰好 0.5s
        Assert.Equal(9, recover); // 后摇 0.15s
        Assert.Equal(48, windup + active + recover); // 完整周期 0.8s
    }

    [Fact]
    public void BoundaryEpsilonAdvancesWhenAccumulatedTimeLandsJustShort()
    {
        // 10 帧 × 0.014995 = 0.14995：比 WINDUP 边界 0.15 低 5e-5，落在容差内必须放行
        // （容差为 0 时这里永久停在 WINDUP——盾永远进不了有效窗口）
        var inside = Production();
        Assert.True(inside.TryStart());
        for (var i = 0; i < 10; i++)
        {
            inside.Tick(0.014995f);
        }

        Assert.Equal(ParryPhase.Active, inside.Phase);

        // 容差不是「随便多给几帧」：低出容差之外的累加值必须留在原相位
        var outside = Production();
        Assert.True(outside.TryStart());
        for (var i = 0; i < 10; i++)
        {
            outside.Tick(0.01498f);
        }

        Assert.Equal(ParryPhase.Windup, outside.Phase);
    }

    [Fact]
    public void CooldownStartsAtFlowEndNotFlowStart()
    {
        var timeline = Production();
        Assert.True(timeline.TryStart());
        while (timeline.IsFlowing())
        {
            Assert.Equal(0.0f, timeline.Cooldown); // 流程期该字段一动不动
            timeline.Tick(Frame);
        }

        // 结束那一帧恰好满冷却：完整周期 0.8 + 3.0 = 3.8s，占空比约 21%（盾是决策性资源）
        Assert.Equal(CooldownMax, timeline.Cooldown);
        timeline.Tick(Frame);
        Assert.Equal(CooldownMax - Frame, timeline.Cooldown, 4);
    }

    [Fact]
    public void CooldownGatesRestartAndNeverGoesNegative()
    {
        var timeline = Production();
        Assert.True(timeline.TryStart());
        CompleteFlow(timeline);
        Assert.False(timeline.TryStart()); // 冷却中不可启动

        // 冷却走满 3.0s（60 帧/秒 → 180 帧，浮点累加允许 ±1 帧）才放行
        var frames = 0;
        while (timeline.Cooldown > 0.0f && frames < 300)
        {
            timeline.Tick(Frame);
            frames++;
        }

        Assert.Equal(0.0f, timeline.Cooldown);
        Assert.InRange(frames, 179, 181);
        Assert.True(timeline.TryStart());

        var idle = Production();
        for (var i = 0; i < 600; i++)
        {
            idle.Tick(Frame);
        }

        Assert.Equal(0.0f, idle.Cooldown); // 空转不产生负冷却（负值会让冷却提前结束）
        Assert.False(idle.IsFlowing());
    }

    [Fact]
    public void ConfigureClampsInjectedValuesToCallerFloor()
    {
        var shortActive = new ParryTimeline();
        shortActive.Configure(Duration, 0.01f, -1.0f, Floor);
        Assert.Equal(Floor, shortActive.ActiveTime); // 活跃窗口钳到相位下限
        Assert.Equal(Duration, shortActive.Duration);
        Assert.Equal(0.0f, shortActive.CooldownMax); // 负冷却钳 0

        var shortFlow = new ParryTimeline();
        shortFlow.Configure(0.02f, ActiveTime, CooldownMax, Floor);
        Assert.Equal(Floor, shortFlow.ActiveTime); // 上界 max(duration, floor) 吃同一地板
        Assert.Equal(Floor, shortFlow.Duration); // duration 不短于 ActiveTime
        Assert.Equal(CooldownMax, shortFlow.CooldownMax);
    }

    [Fact]
    public void EnergyRatioEmptiesWhileFlowingAndRefillsOverCooldown()
    {
        var timeline = Production();
        Assert.Equal(1.0f, timeline.EnergyRatio()); // 待机满格
        Assert.True(timeline.TryStart());
        Assert.Equal(0.0f, timeline.EnergyRatio()); // 流程期清空
        timeline.Tick(Frame);
        Assert.Equal(0.0f, timeline.EnergyRatio());
        CompleteFlow(timeline);
        Assert.Equal(0.0f, timeline.EnergyRatio()); // 流程刚结束：冷却满 = 空槽
        for (var i = 0; i < 90; i++)
        {
            timeline.Tick(Frame);
        }

        Assert.Equal(0.5f, timeline.EnergyRatio(), 3); // 冷却走完一半 = 半格
        Assert.Equal(timeline.Cooldown, timeline.CooldownRemaining()); // HUD 读口与字段同源

        var noCooldown = new ParryTimeline();
        noCooldown.Configure(Duration, ActiveTime, 0.0f, Floor);
        Assert.Equal(1.0f, noCooldown.EnergyRatio()); // 无冷却档不除零
    }

    [Fact]
    public void TintShieldAndShineRampsTrackPhaseProgress()
    {
        var timeline = Production();
        Assert.Equal(0.0f, timeline.TintStrength());
        Assert.Equal(0.0f, timeline.ShieldExpand());
        Assert.Equal(0.0f, timeline.ShineProgress());

        Assert.True(timeline.TryStart());
        timeline.Tick(0.075f); // 前摇半程（0.15s 的一半）
        Assert.Equal(0.5f, timeline.TintStrength(), 3);
        Assert.Equal(0.5f, timeline.ShieldExpand(), 3);
        Assert.Equal(0.0f, timeline.ShineProgress());

        timeline.Tick(0.075f); // 进入有效窗口
        Assert.Equal(ParryPhase.Active, timeline.Phase);
        Assert.Equal(1.0f, timeline.TintStrength());
        Assert.Equal(1.0f, timeline.ShieldExpand());
        Assert.Equal(0.0f, timeline.ShineProgress());

        timeline.Tick(0.25f); // 有效窗口半程
        Assert.Equal(0.5f, timeline.ShineProgress(), 3);
        Assert.Equal(1.0f, timeline.TintStrength());

        timeline.Tick(0.25f); // 进入后摇
        Assert.Equal(ParryPhase.Recover, timeline.Phase);
        Assert.Equal(1.0f, timeline.TintStrength()); // 后摇起点：1 - 0
        Assert.Equal(1.0f, timeline.ShieldExpand());

        timeline.Tick(0.15f); // 后摇走完
        Assert.Equal(ParryPhase.Idle, timeline.Phase);
        Assert.Equal(0.0f, timeline.TintStrength());
        Assert.Equal(0.0f, timeline.ShieldExpand());
        Assert.Equal(CooldownMax, timeline.Cooldown);
    }

    [Fact]
    public void CancelFromActive_LeavesNoGhostWindow()
    {
        // 输入锁打断（Tick 停摆 → 相位冻在中断点）：取消后不得留下残留判定，
        // 否则解锁第一帧会重新打开有效窗口（不按键出现整段盾 + 一次金光）
        var timeline = Production();
        Assert.True(timeline.TryStart());
        while (timeline.Phase != ParryPhase.Active)
        {
            timeline.Tick(Frame);
        }

        timeline.Cancel();
        Assert.Equal(ParryPhase.Idle, timeline.Phase);
        Assert.False(timeline.IsFlowing());
        Assert.Equal(0.0f, timeline.ShieldExpand(), 6); // 盾视觉归位（残留展开＝可见的幽灵盾）
        Assert.Equal(0.0f, timeline.TintStrength(), 6);
        Assert.True(timeline.TryStart(), "取消不追加冷却：解锁后应立即可用");
    }

    [Fact]
    public void CancelFromIdle_IsNoOp()
    {
        // 幂等：待机期取消不得改动相位与冷却进度（重复锁输入不得吞掉冷却）
        var timeline = Production();
        CompleteFlow(timeline);
        timeline.Tick(Frame);
        var cooldownBefore = timeline.Cooldown;
        timeline.Cancel();
        Assert.Equal(ParryPhase.Idle, timeline.Phase);
        Assert.Equal(cooldownBefore, timeline.Cooldown);
    }

    [Fact]
    public void PhaseIdsMatchEngineSideOrdinal()
    {
        // HUD/探针按 int 读相位（Player.ParryPhase() 返回 (int)Phase）：序数不得重排
        Assert.Equal(0, (int)ParryPhase.Idle);
        Assert.Equal(1, (int)ParryPhase.Windup);
        Assert.Equal(2, (int)ParryPhase.Active);
        Assert.Equal(3, (int)ParryPhase.Recover);
    }
}
