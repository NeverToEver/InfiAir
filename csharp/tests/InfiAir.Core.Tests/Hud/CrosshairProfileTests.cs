using InfiAir.Core.Hud;
using Xunit;

namespace InfiAir.Core.Tests.Hud;

/// <summary>准星档案：默认值＝现版观感定稿（DESIGN_BASELINE 字段表）；越界/非法值一律钳回
/// 范围——设置档与准星码两条读入口都经同一归一口，坏数据静默回界内，不得污染绘制。</summary>
public sealed class CrosshairProfileTests
{
    [Fact]
    public void Default_MatchesCurrentLook()
    {
        var p = CrosshairProfile.Default;
        Assert.Equal(CrosshairShape.Bracket, p.Shape);
        Assert.Equal(1.0f, p.Size);
        Assert.Equal(2, p.Thickness);
        Assert.Equal(0.0f, p.Gap);
        Assert.Equal(0.95f, p.Alpha);
        Assert.Equal(0, p.RotationDeg);
        Assert.True(p.CenterDot);
        Assert.Equal(2, p.DotSize);
        Assert.False(p.TShape);
        Assert.False(p.Outline);
        Assert.True(p.StateTint);
        Assert.Equal(255, p.R);
        Assert.Equal(194, p.G);
        Assert.Equal(77, p.B);
        Assert.Equal(242, p.A);
    }

    [Fact]
    public void Normalized_ClampsEveryFieldIntoRange()
    {
        var p = new CrosshairProfile
        {
            Shape = (CrosshairShape)99,
            Size = 99.0f,
            Thickness = 0,
            Gap = -3.0f,
            Alpha = 0.0f,
            RotationDeg = 100,
            DotSize = 0,
            R = 1,
            G = 2,
            B = 3,
            A = 4,
        }.Normalized();

        Assert.Equal(CrosshairShape.Bracket, p.Shape);
        Assert.Equal(CrosshairProfile.SizeMax, p.Size);
        Assert.Equal(1, p.Thickness);
        Assert.Equal(0.0f, p.Gap);
        Assert.Equal(CrosshairProfile.AlphaMin, p.Alpha);
        Assert.Equal(90, p.RotationDeg);
        Assert.Equal(1, p.DotSize);

        var low = new CrosshairProfile { Size = 0.0f, Thickness = 9, Gap = 50.0f, Alpha = 2.0f, DotSize = 9 }.Normalized();
        Assert.Equal(CrosshairProfile.SizeMin, low.Size);
        Assert.Equal(6, low.Thickness);
        Assert.Equal(20.0f, low.Gap);
        Assert.Equal(1.0f, low.Alpha);
        Assert.Equal(6, low.DotSize);
    }

    [Fact]
    public void Normalized_SnapsRotationToStep()
    {
        Assert.Equal(0, new CrosshairProfile { RotationDeg = 7 }.Normalized().RotationDeg);
        Assert.Equal(15, new CrosshairProfile { RotationDeg = 22 }.Normalized().RotationDeg);
        Assert.Equal(45, new CrosshairProfile { RotationDeg = 45 }.Normalized().RotationDeg);
        Assert.Equal(90, new CrosshairProfile { RotationDeg = 90 }.Normalized().RotationDeg);
    }

    [Fact]
    public void ShapeName_RoundTrips()
    {
        foreach (var shape in new[] { CrosshairShape.Bracket, CrosshairShape.Cross, CrosshairShape.Circle, CrosshairShape.Dot })
        {
            Assert.Equal(shape, CrosshairProfile.ShapeFromName(CrosshairProfile.ShapeName(shape)));
        }

        // 未知名（手改档）回 Bracket，不抛
        Assert.Equal(CrosshairShape.Bracket, CrosshairProfile.ShapeFromName("sigma"));
        Assert.Equal(CrosshairShape.Cross, CrosshairProfile.ShapeFromName("cross"));
    }
}
