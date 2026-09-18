using System;
using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>准星交战反馈状态机：可攻击变色混合、锁定一次性整圈旋转（ease-out）与锁定视觉
/// 进出。这些曲线原先只存在于表现层，坏了只表现为「变色时机不对 / 旋转停不到回正位」，
/// 无任何运行信号——尤其旋转终点必须落在四折对称的四分圈上，否则准星歪着停。</summary>
public sealed class CrosshairStateTests
{
    private const float LockTime = 0.25f;
    private const float ColorBlendTime = 0.08f;
    private const float ReleaseFadeTime = 0.12f;
    private const float Tau = MathF.PI * 2.0f;

    private static CrosshairState Make() => new(LockTime, ColorBlendTime, ReleaseFadeTime);

    [Fact]
    public void Initial_IsIdleAndUpright()
    {
        var fx = Make();
        Assert.Equal(CrosshairState.Phase.Idle, fx.Current);
        Assert.Equal(0.0f, fx.HostileBlend);
        Assert.Equal(0.0f, fx.LockContract);
        Assert.Equal(0.0f, fx.SpinAngleRad);
    }

    [Fact]
    public void HostileInReach_RampsBlendOverColorBlendTime()
    {
        var fx = Make();
        fx.Update(ColorBlendTime / 2.0f, hostileInReach: true, markedInFrame: false);
        Assert.Equal(CrosshairState.Phase.Hostile, fx.Current);
        Assert.Equal(0.5f, fx.HostileBlend, 2.0f);

        fx.Update(ColorBlendTime / 2.0f, true, false);
        Assert.Equal(1.0f, fx.HostileBlend, 3.0f);

        // 离开目标：混合按同一时长退回
        fx.Update(ColorBlendTime / 2.0f, false, false);
        Assert.Equal(0.5f, fx.HostileBlend, 2.0f);
    }

    [Fact]
    public void Locked_CompletesFullTurnAfterLockTime()
    {
        var fx = Make();
        Step(fx, LockTime, hostile: true, marked: true);
        Assert.Equal(CrosshairState.Phase.Locked, fx.Current);
        Assert.Equal(1.0f, fx.LockedBlend, 3.0f);
        Assert.Equal(1.0f, fx.LockContract, 3.0f);
        // 整圈结束且恰为一整圈（不是两圈或 0.9 圈）
        Assert.Equal(Tau, fx.SpinAngleRad, 2.0f);
    }

    [Fact]
    public void Spin_IsEaseOut_MostlyEarly()
    {
        var fx = Make();
        Step(fx, LockTime * 0.2f, hostile: true, marked: true);
        var progress = fx.SpinAngleRad / Tau;
        // ease-out cubic 在 20% 时长走到 1-(0.8)³ ≈ 0.488：先快后慢，钉住「旋转一下就稳」的手感
        Assert.True(progress > 0.40f, $"20% 时长应转过约半圈，实际 {progress}");
        Assert.True(progress < 0.60f, $"ease-out 不应慢于线性，实际 {progress}");
    }

    [Fact]
    public void LockedSteady_AngleDoesNotDrift()
    {
        var fx = Make();
        Step(fx, LockTime, hostile: true, marked: true);
        Step(fx, 2.0f, hostile: true, marked: true);
        Assert.Equal(Tau, fx.SpinAngleRad, 2.0f);
        Assert.Equal(1.0f, fx.LockedBlend, 3.0f);
    }

    [Fact]
    public void Release_FadesLockVisuals_KeepsUprightAngle()
    {
        var fx = Make();
        Step(fx, LockTime, hostile: true, marked: true);
        fx.Update(ReleaseFadeTime / 2.0f, hostileInReach: false, markedInFrame: false);
        Assert.Equal(0.5f, fx.LockedBlend, 2.0f);
        fx.Update(ReleaseFadeTime / 2.0f, false, false);
        Assert.Equal(0.0f, fx.LockedBlend, 3.0f);
        Assert.Equal(CrosshairState.Phase.Idle, fx.Current);
        // 旋转是一次性承诺：目标离开后转完即停，不停在歪角上
        Assert.Equal(Tau, fx.SpinAngleRad, 2.0f);
    }

    [Fact]
    public void ReacquireAfterSettle_RunsAnotherFullTurn()
    {
        var fx = Make();
        Step(fx, LockTime, hostile: true, marked: true);
        Step(fx, ReleaseFadeTime, hostile: false, marked: false);
        Step(fx, LockTime, hostile: true, marked: true);
        // 上一圈的终点（2π）是四分圈整数倍，新的一圈从它起算：终点恰为 4π
        Assert.Equal(Tau * 2.0f, fx.SpinAngleRad, 2.0f);
    }

    [Fact]
    public void RetriggerMidSpin_DoesNotRestart_EndsOriginalTurn()
    {
        var fx = Make();
        Step(fx, LockTime * 0.4f, hostile: true, marked: true);    // 旋转进行中
        fx.Update(LockTime * 0.2f, hostileInReach: false, markedInFrame: false); // 短暂滑出框
        Step(fx, LockTime, hostile: true, marked: true);           // 回框（旋转尚未转完）：不得重转
        // 框沿抖动若把旋转打成连转会直接红（终点 ≠ 2π）；「回正」由精确等角蕴含
        Assert.Equal(Tau, fx.SpinAngleRad, 2.0f);
    }

    [Fact]
    public void Locked_AlsoDrivesHostileBlend()
    {
        // 锁定 ⊃ 可攻击：调用方只喂 marked 也必须看到变色混合推满（两层反馈不得互相遮蔽）
        var fx = Make();
        Step(fx, ColorBlendTime, hostile: false, marked: true);
        Assert.Equal(1.0f, fx.HostileBlend, 3.0f);
    }

    [Fact]
    public void ZeroDelta_IsNoop()
    {
        var fx = Make();
        fx.Update(0.0f, hostileInReach: true, markedInFrame: true);
        Assert.Equal(0.0f, fx.SpinAngleRad);
        Assert.True(float.IsFinite(fx.SpinAngleRad));
    }

    [Fact]
    public void DegenerateTimes_DoNotPoisonWithNaN()
    {
        // 配置损坏（0 时长）不得把 NaN 灌进角度/混合：构造器兜底钳正
        var fx = new CrosshairState(0.0f, 0.0f, 0.0f);
        Step(fx, 1.0f, hostile: true, marked: true);
        Assert.True(float.IsFinite(fx.SpinAngleRad));
        Assert.True(float.IsFinite(fx.HostileBlend));
        Assert.True(float.IsFinite(fx.LockedBlend));
    }

    /// <summary>按固定步长推进模拟（无头确定性口径：时长全走 delta 累计）。</summary>
    private static void Step(CrosshairState fx, float duration, bool hostile, bool marked)
    {
        const float dt = 1.0f / 240.0f;
        var remaining = duration;
        while (remaining > 0.0f)
        {
            var step = MathF.Min(dt, remaining);
            fx.Update(step, hostile, marked);
            remaining -= step;
        }
    }
}
