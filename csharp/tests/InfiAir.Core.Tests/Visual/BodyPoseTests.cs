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
}
