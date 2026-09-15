using InfiAir.Core.Talent;
using Xunit;

namespace InfiAir.Core.Tests.Talent;

/// <summary>TalentFanGeometry 契约测试：卡位钳制的边界行为——整张卡必须落在扇形视口内，
/// 右缘不得越过详情栏左缘。守的是「看得见、点不到」：越界部分被详情卡（MouseFilter=Stop）
/// 遮住并吃掉点击，鼠标只剩卡的一小条边，无头冒烟不执行布局也不判命中区。</summary>
public sealed class TalentFanGeometryTests
{
    [Fact]
    public void ProductionConstants_AreTheAssemblyValues()
    {
        // 这三个数是宿主装配与构件布局域的共同来源；改装配必须改这里（单测与实机同域）
        Assert.Equal(900.0, TalentFanGeometry.ViewportWidth);
        Assert.Equal(720.0, TalentFanGeometry.ViewportHeight);
        Assert.Equal(128.0, TalentFanGeometry.CardSize);
        Assert.Equal(910.0, TalentFanGeometry.DetailColumnLeft);
    }

    [Fact]
    public void ClampCardCenter_LeavesInteriorCentersUntouched()
    {
        // 扇形中部的卡不受钳制影响（钳制只收拢越界端，不重排设计）
        var (x, y) = TalentFanGeometry.ClampCardCenter(450.0, 400.0);
        Assert.Equal(450.0, x, 6);
        Assert.Equal(400.0, y, 6);
    }

    [Fact]
    public void ClampCardCenter_PullsRightOverflowToCardFullyInsideViewport()
    {
        var (x, y) = TalentFanGeometry.ClampCardCenter(958.9, 176.8);
        Assert.Equal(TalentFanGeometry.ViewportWidth - TalentFanGeometry.CardSize / 2.0, x, 6);
        Assert.Equal(176.8, y, 6); // 纵向未越界即不动
        Assert.Equal(TalentFanGeometry.ViewportWidth, TalentFanGeometry.CardRight(x), 6);
    }

    [Fact]
    public void ClampCardCenter_PullsLeftAndVerticalOverflow()
    {
        var (x, _) = TalentFanGeometry.ClampCardCenter(-45.3, 0.0);
        Assert.Equal(TalentFanGeometry.CardSize / 2.0, x, 6);

        var (_, top) = TalentFanGeometry.ClampCardCenter(0.0, 10.0);
        Assert.Equal(TalentFanGeometry.CardSize / 2.0, top, 6);

        var (_, bottom) = TalentFanGeometry.ClampCardCenter(0.0, 1e6);
        Assert.Equal(TalentFanGeometry.ViewportHeight - TalentFanGeometry.CardSize / 2.0, bottom, 6);
    }

    [Fact]
    public void ClampCardCenter_AllRealCardsClearTheDetailColumn()
    {
        // 端到端判据（生产两步：布局 → 钳制）：全部大类所有节点的卡右缘 ≤ 详情栏左缘。
        // 布局域退回 1240（旧口径）时 defense.second_wind 的卡右缘 1022.9 > 910，本用例会红。
        foreach (var cat in TalentTree.Categories)
        {
            var layout = new TalentFanLayout
            {
                Width = TalentFanGeometry.ViewportWidth,
                Height = TalentFanGeometry.ViewportHeight,
            };
            var lines = cat.Lines.Select(l => l.NodeIds).ToList();
            foreach (var p in layout.Compute(lines))
            {
                var (x, _) = TalentFanGeometry.ClampCardCenter(p.X, p.Y);
                Assert.True(TalentFanGeometry.CardRight(x) <= TalentFanGeometry.DetailColumnLeft + 1e-6,
                    $"{p.NodeId} 卡右缘 {TalentFanGeometry.CardRight(x)} 越过详情栏左缘 {TalentFanGeometry.DetailColumnLeft}");
            }
        }
    }

    [Fact]
    public void ClampCardCenter_NonFiniteOrDegenerateViewport_StaysFinite()
    {
        var (x, y) = TalentFanGeometry.ClampCardCenter(double.NaN, double.PositiveInfinity);
        Assert.True(double.IsFinite(x) && double.IsFinite(y));

        // 视口窄于一张卡：收敛到中线而不是负区间
        var (narrow, _) = TalentFanGeometry.ClampCardCenter(40.0, 0.0, 60.0, 60.0, 128.0);
        Assert.Equal(30.0, narrow, 6);
    }
}
