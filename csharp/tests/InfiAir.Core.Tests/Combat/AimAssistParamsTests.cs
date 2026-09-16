using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>辅助瞄准档位参数的域钳：负值/非有限量是配置损坏，必须回退默认而不是钳成 0——
/// 钳 0 对 magnet_strength 等于关掉磁吸、对 magnet_range 等于磁吸永不生效（护栏加在不生效的
/// 一侧就是原缺陷的形态）。0 本身合法（= 该机制关闭）。</summary>
public sealed class AimAssistParamsTests
{
    [Fact]
    public void NegativeStrength_FallsBackInsteadOfReversing()
    {
        // 负 strength 会让 MagnetPull 的 Normalized() * 负值 把准星朝远离目标方向推
        var v = AimAssistParams.NonNegativeOr(-6.0f, 6.0f);
        Assert.Equal(6.0f, v);
        Assert.True(v > 0.0f, "回退值必须为正，否则仍是「反向磁吸」");
    }

    [Fact]
    public void NegativeRange_FallsBackInsteadOfDisablingMagnet()
    {
        // 负 range 使框沿距粗筛恒不通过（dx > range 对任意非负 dx 为真）→ 磁吸整块失效
        var v = AimAssistParams.NonNegativeOr(-100.0f, 100.0f);
        Assert.Equal(100.0f, v);
        Assert.True(v > 0.0f, "回退值必须为正，否则磁吸仍不生效");
    }

    [Fact]
    public void LegitValues_AreKept()
    {
        Assert.Equal(0.0f, AimAssistParams.NonNegativeOr(0.0f, 100.0f));
        Assert.Equal(4.0f, AimAssistParams.NonNegativeOr(4.0f, 6.0f));
        Assert.Equal(130.0f, AimAssistParams.NonNegativeOr(130.0f, 100.0f));
    }

    [Fact]
    public void NonFinite_FallsBack()
    {
        Assert.Equal(6.0f, AimAssistParams.NonNegativeOr(float.NaN, 6.0f));
        Assert.Equal(6.0f, AimAssistParams.NonNegativeOr(float.PositiveInfinity, 6.0f));
        Assert.Equal(6.0f, AimAssistParams.NonNegativeOr(float.NegativeInfinity, 6.0f));
    }

    [Fact]
    public void MagnetWindow_KeepsFullAboveMin()
    {
        // 两键相等时 MagnetPull 的 t = 0/0 = NaN 会污染准星，上界必须抬到下界之上
        var (min, full) = AimAssistParams.MagnetWindow(40.0f, 40.0f, 2.0f, 40.0f);
        Assert.Equal(40.0f, min);
        Assert.True(full > min, "上界未抬升 → 磁吸权重 t 除零得 NaN");

        // 倒置（上界 < 下界）同样抬升
        var (min2, full2) = AimAssistParams.MagnetWindow(40.0f, 8.0f, 2.0f, 40.0f);
        Assert.Equal(40.0f, min2);
        Assert.Equal(40.0f + AimAssistParams.WindowEpsilon, full2, 6);

        // 损坏（负值）回退默认，且回退后仍满足上界 > 下界
        var (min3, full3) = AimAssistParams.MagnetWindow(-2.0f, -40.0f, 2.0f, 40.0f);
        Assert.Equal(2.0f, min3);
        Assert.Equal(40.0f, full3);
        Assert.True(full3 > min3);
    }
}
