using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>磁吸输入窗口的帧长归一契约：窗口以「每 1/60s 位移」为单位定义，两路输入（摇杆增量
/// 与鼠标物理增量）都是本帧位移，必须换算到同一口径——否则帧率档（30..不限制）一变，同一手速
/// 进窗口的量就变，fps30 下摇杆满推会越过窗口上界导致磁吸完全失效。</summary>
public sealed class AimMagnetInputTests
{
    [Fact]
    public void FrameScale_AtSixtyFps_IsIdentity()
    {
        // 默认档逐位不变：窗口取值与既有实机手感按 60fps 定义
        Assert.Equal(1.0, AimMagnetInput.FrameScale(1.0 / 60.0), 6);
    }

    [Fact]
    public void FrameScale_MakesInputFrameRateInvariant()
    {
        // 同一手速（每秒位移相同）在不同帧率档下换算后必须相等：
        // speed×dt 是「本帧位移」，乘 FrameScale(dt) 后应恒等于 speed×(1/60)
        const double speed = 1400.0; // px/s（balance 默认摇杆灵敏度）
        var at30 = speed * (1.0 / 30.0) * AimMagnetInput.FrameScale(1.0 / 30.0);
        var at60 = speed * (1.0 / 60.0) * AimMagnetInput.FrameScale(1.0 / 60.0);
        var at144 = speed * (1.0 / 144.0) * AimMagnetInput.FrameScale(1.0 / 144.0);
        Assert.Equal(at60, at30, 6);
        Assert.Equal(at60, at144, 6);

        // 且落在窗口内（窗口 min 2 / full 40）：fps30 满推未归一前是 46.7（>40，磁吸失效）
        var rawAt30 = speed * (1.0 / 30.0);
        Assert.True(rawAt30 > 40.0, "前提：未归一前 fps30 满推确实越过窗口上界");
        Assert.True(at30 < 40.0);
        Assert.True(at30 > 2.0);
    }

    [Fact]
    public void FrameScale_NonPositiveOrNonFiniteDelta_IsIdentity()
    {
        // 暂停/异常帧返回 1.0（不缩放），不得除零把输入放大成无穷
        Assert.Equal(1.0, AimMagnetInput.FrameScale(0.0));
        Assert.Equal(1.0, AimMagnetInput.FrameScale(-0.016));
        Assert.Equal(1.0, AimMagnetInput.FrameScale(double.NaN));
        Assert.Equal(1.0, AimMagnetInput.FrameScale(double.PositiveInfinity));
    }

    [Fact]
    public void MousePath_IsTimeScaleInvariant()
    {
        // 判别式：同一手速（每秒 S 像素，手不随 Engine.TimeScale 变慢）在 TS=1 与
        // TS=0.24（Boss 狂暴子弹时间）下换算后进窗口的量必须相等。
        // 真实帧长在两条路上都是 1/60（--fixed-fps 60），缩放帧长 = 真实帧长 × TimeScale。
        const double handSpeed = 600.0; // px/s：60fps 下每真实帧 10px，落在窗口 [2, 40) 内
        const double realFrame = 1.0 / 60.0;
        const double scale = 0.24;

        var mouseDelta = handSpeed * realFrame; // 真实手部位移，与 TimeScale 无关
        var atFullSpeed = mouseDelta * AimMagnetInput.FrameScale(AimInputPath.Mouse, realFrame, realFrame);
        var atSlowMo = mouseDelta
            * AimMagnetInput.FrameScale(AimInputPath.Mouse, realFrame * scale, realFrame);

        Assert.Equal(atFullSpeed, atSlowMo, 6);
        Assert.Equal(handSpeed * AimMagnetInput.ReferenceFrame, atSlowMo, 6);

        // 反例自证：鼠标路若吃缩放帧长（实现坏掉的形态），换算比例是 1/TimeScale = 4.1667，
        // 进窗口的量被放大同样倍数并越过窗口上界 40 → MagnetPull 直接返回零向量、磁吸整体失效
        var brokenScale = AimMagnetInput.FrameScale(realFrame * scale);
        Assert.Equal(4.16667, brokenScale, 5);
        Assert.True(mouseDelta * brokenScale > 40.0, "前提：吃缩放帧长时 10px/帧 会被放大到 41.7，越过窗口上界");
    }

    [Fact]
    public void StickPath_UsesIntegratingDelta()
    {
        // 摇杆路：位移本身就由缩放帧长积分而来（speed × shaped × delta），乘回同一比例后
        // 与 TimeScale 无关——这是它「不受影响」的原因，也是两路不能共用一条换算式的理由。
        const float shaped = 1.0f;
        const float speed = 1400.0f;
        const double realFrame = 1.0 / 60.0;
        const double scale = 0.24;

        var deltaNormal = realFrame;
        var deltaSlowMo = realFrame * scale;
        var atNormal = shaped * speed * deltaNormal
            * AimMagnetInput.FrameScale(AimInputPath.Stick, deltaNormal, realFrame);
        var atSlowMo = shaped * speed * deltaSlowMo
            * AimMagnetInput.FrameScale(AimInputPath.Stick, deltaSlowMo, realFrame);

        Assert.Equal(atNormal, atSlowMo, 4);
        Assert.Equal(speed * AimMagnetInput.ReferenceFrame, atSlowMo, 4);
        Assert.NotEqual(
            AimMagnetInput.FrameScale(AimInputPath.Stick, deltaSlowMo, realFrame),
            AimMagnetInput.FrameScale(AimInputPath.Mouse, deltaSlowMo, realFrame));
    }

    [Fact]
    public void MousePath_NonFiniteRealDelta_FallsBackToIdentity()
    {
        // 真实帧长取不到（倍率异常）时不做缩放，避免除零把输入放大成无穷
        Assert.Equal(1.0, AimMagnetInput.FrameScale(AimInputPath.Mouse, 1.0 / 60.0, 0.0));
        Assert.Equal(1.0, AimMagnetInput.FrameScale(AimInputPath.Mouse, 1.0 / 60.0, double.NaN));
    }
}
