using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>辅助瞄准目标的几何判定：框半宽、框包含、框沿距、锥角。这些算式原先内联在
/// AimFrameLayer 的每帧扫描里，坏了只表现为「辅瞄偏弱/偏强」，无任何运行信号。</summary>
public sealed class AimTargetingTests
{
    [Fact]
    public void FrameHalfSize_AddsPadToCollisionRadius()
    {
        // 碰撞半径 26 × ws + pad 24（high 档高对比框），两边同族口径
        Assert.Equal(50.0f, AimTargeting.FrameHalfSize(26.0f, 24.0f));
        Assert.Equal(24.0f, AimTargeting.FrameHalfSize(0.0f, 24.0f));

        // 负值（配置损坏）不放大也不缩小到负：半宽为负会让框判定恒不通过
        Assert.Equal(24.0f, AimTargeting.FrameHalfSize(-5.0f, 24.0f));
        Assert.Equal(26.0f, AimTargeting.FrameHalfSize(26.0f, -5.0f));
    }

    [Fact]
    public void InFrame_BoundaryCounts()
    {
        Assert.True(AimTargeting.InFrame(0.0f, 0.0f, 0.0f, 0.0f, 10.0f));
        Assert.True(AimTargeting.InFrame(10.0f, -10.0f, 0.0f, 0.0f, 10.0f));
        Assert.False(AimTargeting.InFrame(10.01f, 0.0f, 0.0f, 0.0f, 10.0f));
        Assert.False(AimTargeting.InFrame(9.0f, 11.0f, 0.0f, 0.0f, 10.0f));
    }

    [Fact]
    public void InCircle_BoundaryAndRoughReject()
    {
        // 3-4-5：恰在圆周上算含（含边界），框内但圆外不算
        Assert.True(AimTargeting.InCircle(3.0f, 4.0f, 0.0f, 0.0f, 5.0f));
        Assert.False(AimTargeting.InCircle(3.0f, 4.0f, 0.0f, 0.0f, 4.9f));

        // 圆含于方框：框内但出圆的角点必须排除（坏实现只做方框粗筛即此红）
        Assert.False(AimTargeting.InCircle(4.4f, 4.4f, 0.0f, 0.0f, 5.0f));

        // 数据损坏（负/NaN 半径）恒不含，不得抛出或误含
        Assert.False(AimTargeting.InCircle(0.0f, 0.0f, 0.0f, 0.0f, -1.0f));
        Assert.False(AimTargeting.InCircle(0.0f, 0.0f, 0.0f, 0.0f, float.NaN));
    }

    [Fact]
    public void SquareOverlapsCircle_CoversOnTouch()
    {
        // 圆心在方内：覆盖
        Assert.True(AimTargeting.SquareOverlapsCircle(0.0f, 0.0f, 14.0f, 5.0f, -3.0f, 10.0f));

        // 恰在方沿外相切（14 + 10 = 24）：含相切；再远一丝即不覆盖
        Assert.True(AimTargeting.SquareOverlapsCircle(0.0f, 0.0f, 14.0f, 24.0f, 0.0f, 10.0f));
        Assert.False(AimTargeting.SquareOverlapsCircle(0.0f, 0.0f, 14.0f, 24.01f, 0.0f, 10.0f));

        // 角部方向同样按轴向钳取最近点：方角 (14,14) 到圆心 (20,20) 距 √72 ≈ 8.49
        Assert.True(AimTargeting.SquareOverlapsCircle(0.0f, 0.0f, 14.0f, 20.0f, 20.0f, 9.0f));
        Assert.False(AimTargeting.SquareOverlapsCircle(0.0f, 0.0f, 14.0f, 20.0f, 20.0f, 8.0f));

        // 判别式（与「准心点入圆」的分界）：圆心距 23 > r 10，点入圆不成立；
        // 方沿距 23 − 14 = 9 < 10，方与圆相交 → 盖住成立（人类口径：盖住即可，不必准心点入）
        Assert.False(AimTargeting.InCircle(23.0f, 0.0f, 0.0f, 0.0f, 10.0f));
        Assert.True(AimTargeting.SquareOverlapsCircle(0.0f, 0.0f, 14.0f, 23.0f, 0.0f, 10.0f));

        // 数据损坏（负/NaN 半径）恒不相交
        Assert.False(AimTargeting.SquareOverlapsCircle(0.0f, 0.0f, 14.0f, 0.0f, 0.0f, -1.0f));
        Assert.False(AimTargeting.SquareOverlapsCircle(0.0f, 0.0f, 14.0f, 0.0f, 0.0f, float.NaN));
    }

    [Fact]
    public void FrameEdgeDistance_UsesOnlyOutsideComponent()
    {
        // 单轴出框（x 出 5，y 在框内 → y 的分量为负）：只计框外分量，长度 = 5
        Assert.Equal(5.0f, AimTargeting.FrameEdgeDistance(15.0f, 0.0f, 0.0f, 0.0f, 10.0f));

        // 双轴出框（3-4-5）
        Assert.Equal(5.0f, AimTargeting.FrameEdgeDistance(13.0f, 14.0f, 0.0f, 0.0f, 10.0f));

        // 框内为 0（框内归粘滞，不磁吸）
        Assert.Equal(0.0f, AimTargeting.FrameEdgeDistance(1.0f, 2.0f, 0.0f, 0.0f, 10.0f));

        // 判别式：坏实现把负分量计入（sqrt((15-10)² + (0-10)²) = 11.18），磁吸会误判在 range 之外
        var insideComponentIncluded = (float)System.Math.Sqrt(25.0 + 100.0);
        Assert.True(insideComponentIncluded > 10.0f);
        Assert.True(AimTargeting.FrameEdgeDistance(15.0f, 0.0f, 0.0f, 0.0f, 10.0f) < 10.0f);
    }

    [Fact]
    public void InCone_ThresholdIsInclusiveAndUnitDirection()
    {
        // 8° 锥（medium）：0° 在内、7° 在内、9° 在外
        var threshold = AimCone.CosFromHalfAngleDeg(8.0f);
        Assert.True(AimTargeting.InCone(0.0f, -1.0f, 0.0f, -100.0f, threshold));
        Assert.True(AimTargeting.InCone(0.0f, -1.0f, 100.0f * Sin(7.0f), -100.0f * Cos(7.0f), threshold));
        Assert.False(AimTargeting.InCone(0.0f, -1.0f, 100.0f * Sin(9.0f), -100.0f * Cos(9.0f), threshold));

        // 恰在边界：点积相等 → 在内（判定用「不排除」的取反形式）
        Assert.True(AimTargeting.InCone(0.0f, -1.0f, 100.0f * Sin(8.0f), -100.0f * Cos(8.0f), threshold));
    }

    [Fact]
    public void InCone_NanThresholdDoesNotExclude()
    {
        // 锥阈值/目标方向为 NaN 时不得把目标排除（既有语义，见 AimCone 的 360° 边界）
        Assert.True(AimTargeting.InCone(0.0f, -1.0f, 10.0f, -10.0f, float.NaN));
        Assert.True(AimTargeting.InCone(0.0f, -1.0f, 0.0f, 0.0f, 0.99f));
    }

    private static float Sin(float deg) => (float)System.Math.Sin(deg * System.Math.PI / 180.0);

    private static float Cos(float deg) => (float)System.Math.Cos(deg * System.Math.PI / 180.0);
}
