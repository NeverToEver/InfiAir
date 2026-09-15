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
}
