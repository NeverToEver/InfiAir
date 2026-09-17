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

    [Fact]
    public void HalfAngleRadFromFullAngle_MatchesEngineMath_BitForBit()
    {
        // 扇形判定/绘制按角度比较，要的是半角弧度：与 Mathf.DegToRad(v) * 0.5f 逐位一致
        foreach (var deg in new[] { 0.0f, 45.0f, 90.0f, 180.0f, 270.0f, 360.0f })
        {
            Assert.Equal(EngineDegToRad(deg) * 0.5f, AimCone.HalfAngleRadFromFullAngleDeg(deg));
        }

        // 360° 整角 ⇒ 半角 π（整圆），这是默认弹反接受域
        Assert.Equal(System.MathF.PI, AimCone.HalfAngleRadFromFullAngleDeg(360.0f));
    }

    [Fact]
    public void ConeStrength_LinearInsideCone_AndSaturatesOutside()
    {
        // 接受域 ±8°：与瞄准方向夹角 8° 贴边（0）、0° 正对（1）；锥外恒 0，不减成负值
        var coneCos = AimCone.CosFromHalfAngleDeg(8.0f);
        Assert.Equal(0.0f, AimCone.ConeStrength(coneCos, coneCos));
        Assert.Equal(1.0f, AimCone.ConeStrength(1.0f, coneCos));
        Assert.Equal(0.0f, AimCone.ConeStrength(EngineCos(20.0f), coneCos));
        Assert.Equal(0.0f, AimCone.ConeStrength(EngineCos(180.0f), coneCos));

        // 线性是对**点积**说的（不是对夹角）：锥边界与正对的中点恰在半程
        // （float 域上式子的分子/分母各自舍入，实测 0.49999693——不钉死到 0.5 以免变成浮点噪声测试）
        Assert.InRange(AimCone.ConeStrength((coneCos + 1.0f) * 0.5f, coneCos), 0.499f, 0.501f);

        // 夹角越小强度越高，且不越界
        var s2 = AimCone.ConeStrength(EngineCos(2.0f), coneCos);
        var s4 = AimCone.ConeStrength(EngineCos(4.0f), coneCos);
        var s6 = AimCone.ConeStrength(EngineCos(6.0f), coneCos);
        Assert.True(s2 > s4 && s4 > s6 && s6 > 0.0f);
        Assert.True(s2 < 1.0f);
    }

    [Fact]
    public void ConeStrength_FullCircleCone_IsFullInsteadOfNaN()
    {
        // 判别用例：coneCos＝1（半角 360°）时旧算式 (dot-1)/0 在正对方向得 0/0＝NaN，
        // 调用端 NaN 守卫把它当成「无目标」⇒ 弱追踪被静默关掉。全向锥应当恒为满强度。
        var full = AimCone.CosFromHalfAngleDeg(360.0f);
        Assert.Equal(1.0f, full);

        var aligned = AimCone.ConeStrength(1.0f, full);
        Assert.False(float.IsNaN(aligned));
        Assert.Equal(1.0f, aligned);

        // 全向锥下任意方向都在锥内：强度不随点积变化
        Assert.Equal(1.0f, AimCone.ConeStrength(0.0f, full));
        Assert.Equal(1.0f, AimCone.ConeStrength(-1.0f, full));
    }

    private static float EngineDegToRad(float deg) => (float)(deg * (System.Math.PI / 180.0));
}
