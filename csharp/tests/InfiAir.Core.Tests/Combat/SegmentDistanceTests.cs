using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>
/// 点到线段距离的退化语义与逐位口径。钉住的是「零长线段不得产出 NaN」：原实现
/// （激光束、弹道擦边各写一份）只有一处带零长守卫，另一处 `0/0 = NaN`，而 NaN 与
/// 命中阈值的比较恒为 false —— 不抛错、不打日志，命中判定静默失效。
/// </summary>
public sealed class SegmentDistanceTests
{
    [Fact]
    public void ZeroLengthSegment_ReturnsDistanceToEndpoint_NeverNaN()
    {
        // 判别用例：旧算式（无守卫）在这里返回 NaN，`NaN <= 阈值` 恒 false
        var sq = SegmentDistance.PointToSegmentSq(3.0f, 4.0f, 5.0f, 5.0f, 5.0f, 5.0f);
        Assert.False(float.IsNaN(sq));
        Assert.Equal(5.0f, sq); // 到端点 (5,5)：dx=2、dy=1 → 4+1=5
    }

    [Fact]
    public void ZeroLengthSegment_SquaredAndLengthAgree()
    {
        // 平方版与开方版必须自洽（调用点各取所需：热路径平方比较、事件率开方）
        var sq = SegmentDistance.PointToSegmentSq(-2.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f);
        Assert.Equal(4.0f, sq);
        Assert.Equal(2.0f, SegmentDistance.PointToSegment(-2.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f));
    }

    [Fact]
    public void FootInsideSegment_UsesPerpendicularDistance()
    {
        // 垂足落在段内：距离为垂距（段水平，点在其上方 4）
        Assert.Equal(16.0f, SegmentDistance.PointToSegmentSq(0.0f, 4.0f, -10.0f, 0.0f, 10.0f, 0.0f));
        Assert.Equal(4.0f, SegmentDistance.PointToSegment(0.0f, 4.0f, -10.0f, 0.0f, 10.0f, 0.0f));
    }

    [Fact]
    public void FootOutsideSegment_ClampsToNearestEndpoint()
    {
        // 两侧出段：退化为更近的端点距离（不是到无限长直线的垂距）
        Assert.Equal(25.0f, SegmentDistance.PointToSegmentSq(15.0f, 0.0f, -10.0f, 0.0f, 10.0f, 0.0f));
        Assert.Equal(400.0f, SegmentDistance.PointToSegmentSq(-30.0f, 0.0f, -10.0f, 0.0f, 10.0f, 0.0f));
    }

    [Fact]
    public void EndpointOnSegment_IsClosedBoundary()
    {
        // 端点是闭区间：点在端点上方，距离 = 到该端点的距离（t=0 / t=1 都被夹住）
        Assert.Equal(9.0f, SegmentDistance.PointToSegmentSq(-10.0f, 3.0f, -10.0f, 0.0f, 10.0f, 0.0f));
        Assert.Equal(9.0f, SegmentDistance.PointToSegmentSq(10.0f, 3.0f, -10.0f, 0.0f, 10.0f, 0.0f));
    }

    [Fact]
    public void OriginCase_MatchesBulletGraceSemantics()
    {
        // 弹道擦边判据：查询点是命中框圆心（原点），线段两端是入口/当前相对位。
        // 轨迹穿过圆心 → 0；擦边 → 垂距；退化段 → 到入口点的距离（旧实现同值）。
        Assert.Equal(0.0f, SegmentDistance.PointToSegmentSq(0.0f, 0.0f, -5.0f, 0.0f, 5.0f, 0.0f));
        Assert.Equal(4.0f, SegmentDistance.PointToSegmentSq(0.0f, 0.0f, -5.0f, 2.0f, 5.0f, 2.0f));
        Assert.Equal(25.0f, SegmentDistance.PointToSegmentSq(0.0f, 0.0f, 3.0f, 4.0f, 3.0f, 4.0f));
    }

    [Fact]
    public void MatchesEngineFormulation_BitForBit()
    {
        // 逐位等价于引擎侧算式（Vector2.Dot / LengthSquared 的求值顺序）：
        // 换实现不得移动命中判定的边界（命中判据是 `距离² <= 半径²`，差 1 ulp 就换结果）
        var cases = new[]
        {
            (px: 0.0f, py: 4.0f, ax: -10.0f, ay: 0.0f, bx: 10.0f, by: 0.0f),
            (px: 1.0f, py: 1.0f, ax: 0.0f, ay: 0.0f, bx: 7.0f, by: 3.0f),
            (px: -3.5f, py: 2.25f, ax: 4.0f, ay: -1.0f, bx: -6.0f, by: 5.0f),
            (px: 0.0f, py: 0.0f, ax: 3.0f, ay: 4.0f, bx: 6.0f, by: 8.0f),
            (px: 100.0f, py: -100.0f, ax: 0.5f, ay: 0.25f, bx: 1.5f, by: 1.75f),
        };

        foreach (var c in cases)
        {
            Assert.Equal(EngineDistToSegmentSq(c.px, c.py, c.ax, c.ay, c.bx, c.by),
                SegmentDistance.PointToSegmentSq(c.px, c.py, c.ax, c.ay, c.bx, c.by));
        }
    }

    /// <summary>引擎侧原算式的逐字复刻（float 域，同一求值顺序），仅用于比对。</summary>
    private static float EngineDistToSegmentSq(float px, float py, float ax, float ay, float bx, float by)
    {
        var abx = bx - ax;
        var aby = by - ay;
        var crx = px - ax;
        var cry = py - ay;
        var t = crx * abx + cry * aby;
        var lenSq = abx * abx + aby * aby;
        var ratio = t / lenSq;
        var clamped = ratio < 0.0f ? 0.0f : (ratio > 1.0f ? 1.0f : ratio);
        var dx = px - (ax + abx * clamped);
        var dy = py - (ay + aby * clamped);
        return dx * dx + dy * dy;
    }
}
