using System;
using InfiAir.Core.Visual;
using Xunit;

namespace InfiAir.Core.Tests.Visual;

/// <summary>机体姿态算式契约测试（<c>DESIGN_BASELINE</c> §2.13）：侧倾、后坐力、推挤与朝向跟随。
/// 守的静默错误＝护栏缺失：NaN 顺着变换链污染整棵贴图子树（引擎零报错、画面整块消失）；
/// 超速不钳制会让加速档下的侧倾越转越大；指数逼近写成帧率相关时无头固定步长与实机观感分叉。</summary>
public sealed class BodyPoseTests
{
    // ---------------- BankTarget ----------------

    [Fact]
    public void BankTarget_ScalesWithLateralSpeed_AndClampsPastMax()
    {
        const double maxRad = 0.14;
        // 静止无侧倾；半速半角；满速满角
        Assert.Equal(0.0, BodyPose.BankTarget(0.0, 420.0, maxRad), 9);
        Assert.Equal(maxRad / 2.0, BodyPose.BankTarget(210.0, 420.0, maxRad), 9);
        Assert.Equal(maxRad, BodyPose.BankTarget(420.0, 420.0, maxRad), 9);
        // 加速档超速（1.25×）按满强度钳制，不越界
        Assert.Equal(maxRad, BodyPose.BankTarget(525.0, 420.0, maxRad), 9);
        // 左移对称取负
        Assert.Equal(-maxRad / 2.0, BodyPose.BankTarget(-210.0, 420.0, maxRad), 9);
    }

    [Fact]
    public void BankTarget_InvalidInputs_ReturnZero()
    {
        Assert.Equal(0.0, BodyPose.BankTarget(double.NaN, 420.0, 0.14));
        Assert.Equal(0.0, BodyPose.BankTarget(100.0, double.NaN, 0.14));
        Assert.Equal(0.0, BodyPose.BankTarget(100.0, 420.0, double.NaN));
        Assert.Equal(0.0, BodyPose.BankTarget(100.0, 0.0, 0.14)); // 满速为 0：无归一化基准
        Assert.Equal(0.0, BodyPose.BankTarget(100.0, -420.0, 0.14));
        Assert.Equal(0.0, BodyPose.BankTarget(double.PositiveInfinity, 420.0, 0.14));
    }

    // ---------------- Approach ----------------

    [Fact]
    public void Approach_ConvergesExponentially_AndIsFrameRateIndependent()
    {
        // rate=12：一步 60fps×1/60s 与一步 30fps×1/30s 的结果一致（指数的帧率无关性）
        var a = BodyPose.Approach(0.0, 1.0, 12.0, 1.0 / 60.0);
        var b = BodyPose.Approach(0.0, 1.0, 12.0, 1.0 / 30.0);
        Assert.Equal(1.0 - Math.Exp(-12.0 / 60.0), a, 9);
        Assert.Equal(1.0 - Math.Exp(-12.0 / 30.0), b, 9);
        Assert.NotEqual(a, b);

        // 连续两小步 ＝ 一个大步（同一积分语义）
        var half = BodyPose.Approach(0.0, 1.0, 12.0, 1.0 / 120.0);
        var two = BodyPose.Approach(half, 1.0, 12.0, 1.0 / 120.0);
        Assert.Equal(BodyPose.Approach(0.0, 1.0, 12.0, 1.0 / 60.0), two, 9);

        // 单调逼近不过冲、有限步内不越过目标
        var cur = 0.0;
        for (var i = 0; i < 120; i++)
        {
            var next = BodyPose.Approach(cur, 1.0, 12.0, 1.0 / 60.0);
            Assert.True(next > cur && next < 1.0);
            cur = next;
        }
    }

    [Fact]
    public void Approach_InvalidDelta_KeepsCurrent_AndHealsNaN()
    {
        Assert.Equal(0.3, BodyPose.Approach(0.3, 1.0, 12.0, 0.0)); // 停帧不动
        Assert.Equal(0.3, BodyPose.Approach(0.3, 1.0, 12.0, -0.1));
        Assert.Equal(0.3, BodyPose.Approach(0.3, 1.0, double.NaN, 0.016));
        // current 坏成 NaN：自愈到目标，而不是把 NaN 往下传
        Assert.Equal(1.0, BodyPose.Approach(double.NaN, 1.0, 12.0, 0.016));
    }

    // ---------------- RecoilFactor / PushFactor ----------------

    [Fact]
    public void RecoilFactor_PeaksAtZeroAge_DecaysAndCutsoff()
    {
        Assert.Equal(1.0, BodyPose.RecoilFactor(0.0, 0.09), 9);
        // 指数衰减单调
        Assert.True(BodyPose.RecoilFactor(0.03, 0.09) > BodyPose.RecoilFactor(0.06, 0.09));
        Assert.Equal(Math.Exp(-1.0 / 3.0), BodyPose.RecoilFactor(0.03, 0.09), 9);
        // 3τ 外截断为 0（消费方可停写 Position）
        Assert.Equal(0.0, BodyPose.RecoilFactor(0.27, 0.09), 9);
        Assert.Equal(0.0, BodyPose.RecoilFactor(1.0, 0.09), 9);
        // PushFactor 与 RecoilFactor 同口径
        Assert.Equal(BodyPose.RecoilFactor(0.04, 0.12), BodyPose.PushFactor(0.04, 0.12), 12);
    }

    [Fact]
    public void RecoilFactor_InvalidInputs_ReturnZero()
    {
        Assert.Equal(0.0, BodyPose.RecoilFactor(0.0, 0.0)); // tau=0：关闭
        Assert.Equal(0.0, BodyPose.RecoilFactor(-0.1, 0.09)); // 未来的年龄
        Assert.Equal(0.0, BodyPose.RecoilFactor(double.NaN, 0.09));
        Assert.Equal(0.0, BodyPose.RecoilFactor(0.05, double.NaN));
    }

    // ---------------- FaceTarget ----------------

    [Fact]
    public void FaceTarget_StraightDiveIsUpright_AndTurnsTowardMotion()
    {
        const double rootPi = Math.PI;
        // 敌机（根转 π）：垂直下压（世界角 π/2）时贴图回正（本地 0）
        Assert.Equal(0.0, BodyPose.FaceTarget(Math.PI / 2.0, 1.0, rootPi, 0.6), 9);
        // 世界向右（角 0）：本地目标 -π/2，钳在 ±0.6 内 → -0.6（机头部分朝右）
        Assert.Equal(-0.6, BodyPose.FaceTarget(0.0, 1.0, rootPi, 0.6), 9);
        // 世界向左（角 π）：本地目标 +π/2 → +0.6
        Assert.Equal(0.6, BodyPose.FaceTarget(Math.PI, 1.0, rootPi, 0.6), 9);
        // 低速按比例回正：speed01=0.5 时半角
        Assert.Equal(-0.3, BodyPose.FaceTarget(0.0, 0.5, rootPi, 0.6), 9);
        Assert.Equal(0.0, BodyPose.FaceTarget(0.0, 0.0, rootPi, 0.6), 9);
    }

    [Fact]
    public void FaceTarget_InvalidInputs_ReturnZero()
    {
        Assert.Equal(0.0, BodyPose.FaceTarget(double.NaN, 1.0, Math.PI, 0.6));
        Assert.Equal(0.0, BodyPose.FaceTarget(0.0, double.NaN, Math.PI, 0.6));
        Assert.Equal(0.0, BodyPose.FaceTarget(0.0, 1.0, Math.PI, double.NaN));
        Assert.Equal(0.0, BodyPose.FaceTarget(double.PositiveInfinity, 1.0, Math.PI, 0.6));
    }

    [Fact]
    public void FaceTarget_ClampsWithinMaxRad_ForAnyWorldAngle()
    {
        for (var i = 0; i < 64; i++)
        {
            var angle = i * 0.098 * Math.PI;
            var target = BodyPose.FaceTarget(angle, 1.0, Math.PI, 0.6);
            Assert.InRange(target, -0.6, 0.6);
            Assert.True(double.IsFinite(target));
        }
    }

    // ---------------- LagTargetPx（§2.16 运动滞后漂移） ----------------

    [Fact]
    public void LagTargetPx_OpposesAcceleration_AndClampsToMax()
    {
        const double maxPx = 4.0;
        // 满加速度反向漂满幅；半加速半幅；反向加速取反
        Assert.Equal(-maxPx, BodyPose.LagTargetPx(1.0, maxPx), 9);
        Assert.Equal(-maxPx / 2.0, BodyPose.LagTargetPx(0.5, maxPx), 9);
        Assert.Equal(maxPx, BodyPose.LagTargetPx(-1.0, maxPx), 9);
        // 超界钳制（帧间速度差在方向反转时会瞬时超参考上限）
        Assert.Equal(-maxPx, BodyPose.LagTargetPx(3.0, maxPx), 9);
        Assert.Equal(maxPx, BodyPose.LagTargetPx(-3.0, maxPx), 9);
        // 零加速度不漂
        Assert.Equal(0.0, BodyPose.LagTargetPx(0.0, maxPx), 9);
    }

    [Fact]
    public void LagTargetPx_InvalidInputs_ReturnZero()
    {
        Assert.Equal(0.0, BodyPose.LagTargetPx(double.NaN, 4.0));
        Assert.Equal(0.0, BodyPose.LagTargetPx(1.0, double.NaN));
        Assert.Equal(0.0, BodyPose.LagTargetPx(1.0, 0.0));
        Assert.Equal(0.0, BodyPose.LagTargetPx(1.0, -4.0));
        Assert.Equal(0.0, BodyPose.LagTargetPx(double.PositiveInfinity, 4.0));
    }

    // ---------------- SwayAfter（§2.16 转向跟随） ----------------

    [Fact]
    public void SwayAfter_AccumulatesTurn_ThenDecaysTowardZero()
    {
        const double maxRad = 0.18;
        // 一帧大甩角被钳在上限内
        var s = BodyPose.SwayAfter(0.0, 1.0, maxRad, 10.0, 1.0 / 60.0);
        Assert.InRange(s, 0.0, maxRad);
        Assert.True(s < 1.0); // 衰减已经生效，不是原样累积
        // 瞄准静止（turnDelta=0）时逐帧指数衰减回 0
        var decayed = BodyPose.SwayAfter(s, 0.0, maxRad, 10.0, 1.0 / 60.0);
        Assert.True(decayed < s);
        Assert.Equal(s * Math.Exp(-10.0 / 60.0), decayed, 9);
        // 负转向对称
        Assert.True(BodyPose.SwayAfter(0.0, -1.0, maxRad, 10.0, 1.0 / 60.0) < 0.0);
    }

    [Fact]
    public void SwayAfter_InvalidInputs_KeepFiniteCurrent_ElseZero()
    {
        // maxRad 非有限：保持现状免钳（Clamp 的 NaN 界会抛）
        Assert.Equal(0.05, BodyPose.SwayAfter(0.05, 1.0, double.NaN, 10.0, 1.0 / 60.0), 9);
        // current 非有限：自愈为 0 起步（一次坏值不永久污染），本帧转向量照常累积后钳制
        var healed = BodyPose.SwayAfter(double.NaN, 1.0, 0.18, 10.0, 1.0 / 60.0);
        Assert.True(double.IsFinite(healed));
        Assert.InRange(healed, 0.0, 0.18);
        // turnDelta 非有限按 0：只衰减不污染
        var s = BodyPose.SwayAfter(0.0, 0.06, 0.18, 0.0, 1.0 / 60.0);
        Assert.Equal(0.06, BodyPose.SwayAfter(s, double.NaN, 0.18, 0.0, 1.0 / 60.0), 9);
        Assert.Equal(0.06, BodyPose.SwayAfter(0.06, 0.06, 0.18, 10.0, 0.0), 9); // delta=0 保持现状
    }

    // ---------------- PopScale（§2.16 冲刺弹跳） ----------------

    [Fact]
    public void PopScale_ParabolicOvershoot_PeaksMidwayAndRestsOutside()
    {
        const double amp = 0.06;
        const double time = 0.16;
        Assert.Equal(1.0, BodyPose.PopScale(0.0, time, amp), 9);
        Assert.Equal(1.0 + amp, BodyPose.PopScale(time / 2.0, time, amp), 9);
        Assert.Equal(1.0, BodyPose.PopScale(time, time, amp), 9);
        Assert.Equal(1.0, BodyPose.PopScale(time + 1.0, time, amp), 9);
        Assert.Equal(1.0, BodyPose.PopScale(-0.01, time, amp), 9);
        // 对称性：半程两侧等高
        Assert.Equal(BodyPose.PopScale(time * 0.25, time, amp), BodyPose.PopScale(time * 0.75, time, amp), 9);
    }

    [Fact]
    public void PopScale_InvalidInputs_ReturnUnity()
    {
        Assert.Equal(1.0, BodyPose.PopScale(0.08, 0.0, 0.06));
        Assert.Equal(1.0, BodyPose.PopScale(0.08, 0.16, 0.0));
        Assert.Equal(1.0, BodyPose.PopScale(0.08, 0.16, -0.06));
        Assert.Equal(1.0, BodyPose.PopScale(double.NaN, 0.16, 0.06));
    }

    // ---------------- BobOffsetPx（§2.16 悬停浮动） ----------------

    [Fact]
    public void BobOffsetPx_SineFloat_AmplitudeAndPeriod()
    {
        const double amp = 1.6;
        const double hz = 0.6;
        Assert.Equal(0.0, BodyPose.BobOffsetPx(0.0, hz, amp), 9);
        Assert.Equal(amp, BodyPose.BobOffsetPx(1.0 / (4.0 * hz), hz, amp), 9); // 四分之一周期到峰
        Assert.Equal(-amp, BodyPose.BobOffsetPx(3.0 / (4.0 * hz), hz, amp), 9);
        Assert.Equal(0.0, BodyPose.BobOffsetPx(1.0 / hz, hz, amp), 9); // 整周期回零
    }

    [Fact]
    public void BobOffsetPx_InvalidInputs_ReturnZero()
    {
        Assert.Equal(0.0, BodyPose.BobOffsetPx(1.0, 0.0, 1.6));
        Assert.Equal(0.0, BodyPose.BobOffsetPx(1.0, -0.6, 1.6));
        Assert.Equal(0.0, BodyPose.BobOffsetPx(double.NaN, 0.6, 1.6));
        Assert.Equal(0.0, BodyPose.BobOffsetPx(1.0, double.NaN, 1.6));
        Assert.Equal(0.0, BodyPose.BobOffsetPx(1.0, 0.6, double.NaN));
    }

    // ---------------- StretchFactor / CounterScale（§2.17 速度伸缩） ----------------

    [Fact]
    public void StretchFactor_SignedByForwardSpeed_AndClamped()
    {
        const double maxStretch = 0.08;
        Assert.Equal(maxStretch, BodyPose.StretchFactor(1.0, maxStretch), 9);   // 全速前伸
        Assert.Equal(0.0, BodyPose.StretchFactor(0.0, maxStretch), 9);          // 静止无形变
        Assert.Equal(-maxStretch, BodyPose.StretchFactor(-1.0, maxStretch), 9); // 倒退压缩
        Assert.Equal(maxStretch / 2.0, BodyPose.StretchFactor(0.5, maxStretch), 9);
        // 加速档超速按满强度钳制
        Assert.Equal(maxStretch, BodyPose.StretchFactor(1.8, maxStretch), 9);
        Assert.Equal(-maxStretch, BodyPose.StretchFactor(-1.8, maxStretch), 9);
    }

    [Fact]
    public void StretchFactor_InvalidInputs_ReturnZero()
    {
        Assert.Equal(0.0, BodyPose.StretchFactor(double.NaN, 0.08));
        Assert.Equal(0.0, BodyPose.StretchFactor(1.0, double.NaN));
        Assert.Equal(0.0, BodyPose.StretchFactor(1.0, 0.0));
        Assert.Equal(0.0, BodyPose.StretchFactor(1.0, -0.08));
        Assert.Equal(0.0, BodyPose.StretchFactor(double.PositiveInfinity, 0.08));
    }

    [Fact]
    public void CounterScale_CompensatesCrossAxis_AndGuards()
    {
        // 拉伸 0.08 时交叉轴按 0.5 收缩 0.04
        Assert.Equal(0.96, BodyPose.CounterScale(0.08, 0.5), 9);
        Assert.Equal(1.0, BodyPose.CounterScale(0.0, 0.5), 9);
        // 负形变（压缩）时交叉轴放大——对称补偿
        Assert.Equal(1.04, BodyPose.CounterScale(-0.08, 0.5), 9);
        // ratio 1.0 = 全补偿
        Assert.Equal(0.92, BodyPose.CounterScale(0.08, 1.0), 9);
        // 非法：不补偿
        Assert.Equal(1.0, BodyPose.CounterScale(double.NaN, 0.5));
        Assert.Equal(1.0, BodyPose.CounterScale(0.08, double.NaN));
        Assert.Equal(1.0, BodyPose.CounterScale(0.08, 0.0));
        Assert.Equal(1.0, BodyPose.CounterScale(0.08, -0.5));
    }

    // ---------------- SputterFactor（§2.17 引擎喘振） ----------------

    [Fact]
    public void SputterFactor_DeterministicBoundedIrregular()
    {
        const double hz = 7.0;
        // 确定性：同相位同值（无随机源）
        Assert.Equal(BodyPose.SputterFactor(0.37, hz), BodyPose.SputterFactor(0.37, hz), 12);
        // 有界 [-1, 1] 且确实在变化（不规则——两个不可通约频率叠加）
        var min = double.MaxValue;
        var max = double.MinValue;
        for (var i = 0; i < 400; i++)
        {
            var v = BodyPose.SputterFactor(i * 0.01, hz);
            Assert.InRange(v, -1.0, 1.0);
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }

        Assert.True(max - min > 1.0, "喘振幅度不足：叠加项退化为常量");
        // t=0 起点为 0（两正弦同相起）
        Assert.Equal(0.0, BodyPose.SputterFactor(0.0, hz), 9);
    }

    [Fact]
    public void SputterFactor_InvalidInputs_ReturnZero()
    {
        Assert.Equal(0.0, BodyPose.SputterFactor(double.NaN, 7.0));
        Assert.Equal(0.0, BodyPose.SputterFactor(1.0, 0.0));
        Assert.Equal(0.0, BodyPose.SputterFactor(1.0, -7.0));
        Assert.Equal(0.0, BodyPose.SputterFactor(1.0, double.NaN));
    }
}
