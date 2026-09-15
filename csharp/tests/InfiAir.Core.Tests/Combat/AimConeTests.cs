using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>瞄准锥余弦阈值的换算口径（半角 vs 整角）。钉住两件事：函数名对应的接受域正确，
/// 且换算与引擎 Mathf.Cos(Mathf.DegToRad(v)) 逐位一致（边界命中判定依赖它）。</summary>
public class AimConeTests
{
    private static float EngineCos(float deg) => (float)System.Math.Cos((float)(deg * (System.Math.PI / 180.0)));

    [Fact]
    public void HalfAngle_TreatsValueAsAcceptanceAngle()
    {
        // 辅助瞄准档位口径：cone_angle_deg 就是接受域半角
        Assert.Equal(EngineCos(8.0f), AimCone.CosFromHalfAngleDeg(8.0f));

        // 接受域自证：与瞄准方向夹角 8° 的目标恰在边界（点积相等），7° 在内、9° 在外
        var threshold = AimCone.CosFromHalfAngleDeg(8.0f);
        Assert.True(EngineCos(7.0f) > threshold);
        Assert.True(EngineCos(9.0f) < threshold);
        Assert.Equal(EngineCos(8.0f), threshold);
    }

    [Fact]
    public void FullAngle_HalvesTheValue()
    {
        // homing 口径：lock_cone_deg 是完整张角，接受域是它的一半
        Assert.Equal(AimCone.CosFromHalfAngleDeg(22.0f), AimCone.CosFromFullAngleDeg(44.0f));

        // 44° 整角 ⇒ 接受域 ±22°：21° 在内、23° 在外
        var threshold = AimCone.CosFromFullAngleDeg(44.0f);
        Assert.True(EngineCos(21.0f) > threshold);
        Assert.True(EngineCos(23.0f) < threshold);
    }

    [Fact]
    public void TwoConventions_AreNotInterchangeable()
    {
        // 同值下两口径差一倍——这正是历史上「都叫锥角」会误判的根源，显式钉住防止再被混用
        Assert.NotEqual(AimCone.CosFromHalfAngleDeg(44.0f), AimCone.CosFromFullAngleDeg(44.0f));
        Assert.Equal(AimCone.CosFromHalfAngleDeg(22.0f), AimCone.CosFromFullAngleDeg(44.0f));
    }

    [Fact]
    public void MatchesEngineMath_BitForBit()
    {
        // 换算搬进 core 后不得改变命中边界：与引擎表达式逐位一致
        foreach (var deg in new[] { 0.0f, 6.0f, 8.0f, 10.0f, 22.0f, 44.0f, 180.0f, 360.0f })
        {
            Assert.Equal(EngineCos(deg), AimCone.CosFromHalfAngleDeg(deg));
            Assert.Equal(EngineCos(deg * 0.5f), AimCone.CosFromFullAngleDeg(deg));
        }
    }
}
