using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>BombReflectKinematics 契约测试：弹反初速形状 + 逐帧寻敌转向 + 追尾收敛性。
/// 收敛性那两条是「弹反有回报」的可执行判据：反射弹必须能在屏幕上咬住横穿的编队，
/// 咬不住就是弹反＝零回报（这条路径曾经整体不推进，人工过目两轮没看出来）。</summary>
public sealed class BombReflectKinematicsTests
{
    /// <summary>投弹继承的水平速度（编队 run_speed 340 × 0.35）与下落速度。</summary>
    private const float DropVelocityX = 119.0f;
    private const float DropVelocityY = 300.0f;

    /// <summary>编队横穿速度（balance formation_strike_event.run_speed）。</summary>
    private const float RunSpeed = 340.0f;

    /// <summary>接触判定距离（弹体半径 12 + 编队机半径 26）。</summary>
    private const float ContactDistance = 38.0f;

    private const float FrameStep = 1.0f / 60.0f;

    [Fact]
    public void Launch_KeepsHorizontalAndReversesVertical()
    {
        var (x, y) = BombReflectKinematics.Launch(DropVelocityX, DropVelocityY, 1.0f);
        Assert.Equal(47.6f, x, 3);
        Assert.Equal(-375.0f, y, 3);
    }

    [Fact]
    public void Launch_ScalesWholeVectorBySpeedMult()
    {
        var raw = BombReflectKinematics.Launch(DropVelocityX, DropVelocityY, 1.0f);
        var boosted = BombReflectKinematics.Launch(DropVelocityX, DropVelocityY, 1.6f);
        Assert.Equal(raw.X * 1.6f, boosted.X, 3);
        Assert.Equal(raw.Y * 1.6f, boosted.Y, 3);
    }

    [Fact]
    public void Launch_RisesEvenWhenVelocityIsAlreadyUpward()
    {
        // 反射语义是「反向上升」，输入垂直分量符号不该改变结果方向
        var (_, y) = BombReflectKinematics.Launch(0.0f, -123.0f, 1.0f);
        Assert.Equal(-153.75f, y, 3);
    }

    [Fact]
    public void SteerHome_TurnIsRateLimited()
    {
        // 目标在正右方：速度从正上方起转，单帧转过的角度受 accel 限制
        // （上限按「单帧最多平移 accel × dt」换算的圆弦角：2·asin(accel·dt / 2|v|)）
        const float speed = 375.0f;
        var (x, y) = BombReflectKinematics.SteerHome(0.0f, -speed, 100.0f, 0.0f, 1600.0f, FrameStep);
        var angle = MathF.Acos(Math.Clamp(((-speed * y) + (0.0f * x)) / (speed * speed), -1.0f, 1.0f));
        var limit = 2.0f * MathF.Asin(1600.0f * FrameStep / (2.0f * speed));
        Assert.InRange(angle, 0.0f, limit + 1e-4f);
        Assert.True(y < 0.0f, "单帧内不该越过目标方向");
    }

    [Fact]
    public void SteerHome_ConvergesOnTargetDirection()
    {
        // 反向起步（最坏入弯）也必须在 ~1 秒内对准目标：初速 375、accel 1600 的角速率约 4.3 rad/s
        float vx = 0.0f, vy = 375.0f;
        for (var i = 0; i < 60; i++)
        {
            var (nx, ny) = BombReflectKinematics.SteerHome(vx, vy, 100.0f, 0.0f, 1600.0f, FrameStep);
            vx = nx;
            vy = ny;
        }

        Assert.Equal(0.0f, vy, 1);
        Assert.True(vx > 0.0f, "60 帧后仍未对准目标方向");
    }

    [Fact]
    public void SteerHome_PreservesSpeedWhileTurning()
    {
        // 转向只改方向：掉头这一帧的速度大小必须与转向前一模一样——
        // 和弦式推进（MoveToward）会在大角度入弯时悄悄掉四成速度，追尾随即追不上
        var (x, y) = BombReflectKinematics.SteerHome(0.0f, -375.0f, 100.0f, 0.0f, 1600.0f, FrameStep);
        Assert.Equal(375.0f, MathF.Sqrt((x * x) + (y * y)), 2);
    }

    [Fact]
    public void SteerHome_SnapsWhenWithinOneStep()
    {
        // 差量小于单帧上限时直接对齐目标方向，不产生抖动（速率保持原大小）
        var (x, y) = BombReflectKinematics.SteerHome(374.0f, -1.0f, 100.0f, 0.0f, 1600.0f, FrameStep);
        Assert.Equal(0.0f, y, 3);
        Assert.Equal(MathF.Sqrt((374.0f * 374.0f) + 1.0f), x, 2);
    }

    [Fact]
    public void SteerHome_KeepsVelocityWhenTargetMerges()
    {
        var (x, y) = BombReflectKinematics.SteerHome(47.6f, -375.0f, 0.2f, 0.2f, 1600.0f, FrameStep);
        Assert.Equal(47.6f, x, 3);
        Assert.Equal(-375.0f, y, 3);
    }

    [Fact]
    public void Pursuit_ReachesFleeingFormationAtProductionSpeedMult()
    {
        // 生产值（balance bomb_reflect_speed_mult = 1.6）必须能从「弹体在编队后下方」的
        // 典型交战几何追上横穿的编队：追不上＝弹反零回报
        Assert.True(Chase(1.6f, 4.0f), "1.6 倍速在 4 秒内没咬住编队");
    }

    [Fact]
    public void Pursuit_CannotReachAtRawSpeed()
    {
        // 原样奉还（1.0）的初速只比编队横穿快一成——这条是速度旋钮存在的理由，
        // 它一旦通过说明参数已变（run_speed 下调或初速上调），该复核 bomb_reflect_speed_mult
        Assert.False(Chase(1.0f, 4.0f), "1.0 倍速反而追上了：编队速度或初速已变，请复核该旋钮");
    }

    /// <summary>追尾仿真：弹体在编队后 400px、下 120px 处弹反，编队以 run_speed 横穿；
    /// 逐帧用同一转向函数推进，返回 3 秒内是否进入接触距离。</summary>
    private static bool Chase(float speedMult, float seconds)
    {
        float targetX = 0.0f, targetY = 0.0f, bombX = -400.0f, bombY = 120.0f;
        var (vx, vy) = BombReflectKinematics.Launch(DropVelocityX, DropVelocityY, speedMult);
        var frames = (int)(seconds / FrameStep);
        for (var i = 0; i < frames; i++)
        {
            targetX += RunSpeed * FrameStep;
            var (nx, ny) = BombReflectKinematics.SteerHome(
                vx, vy, targetX - bombX, targetY - bombY, 1600.0f, FrameStep);
            vx = nx;
            vy = ny;
            bombX += vx * FrameStep;
            bombY += vy * FrameStep;
            var dx = targetX - bombX;
            var dy = targetY - bombY;
            if (MathF.Sqrt((dx * dx) + (dy * dy)) <= ContactDistance)
            {
                return true;
            }
        }

        return false;
    }
}
