using InfiAir.Core.Talent;
using Xunit;

namespace InfiAir.Core.Tests.Talent;

/// <summary>TalentFanLayout 几何契约测试：输出确定性、坐标唯一、扇形对称、
/// 节点卡完整落在扇形视口内且不被详情栏压住。布局是纯函数——坏时玩家看到卡片重叠/飞出面板/
/// 后半张被详情卡遮住而点不到，无头冒烟不经过 GPU 管线，只有这里能钉住。
///
/// 定义域取自 <see cref="TalentFanGeometry"/>（生产装配与构件布局域的唯一来源）：
/// 曾用「面板局部 1240×720」当口径，而控件实际只有 900 宽——断言在实机不存在的域上判绿，
/// 真正的越界（`second_wind` 右缘 1022.9 &gt; 详情栏左缘 910）没人判。</summary>
public sealed class TalentFanLayoutTests
{
    /// <summary>生产口径（TalentPanel 装配 / TalentFanView 布局域）：局部坐标 900×720。</summary>
    private static TalentFanLayout Production()
        => new() { Width = TalentFanGeometry.ViewportWidth, Height = TalentFanGeometry.ViewportHeight };

    [Fact]
    public void Root_IsBottomCenter()
    {
        var (x, y) = Production().Root;
        Assert.Equal(TalentFanGeometry.ViewportWidth * 0.5, x, 6);
        Assert.Equal(TalentFanGeometry.ViewportHeight * 0.95, y, 6);
    }

    [Fact]
    public void SingleLine_NodesStraightUpWithRadialSteps()
    {
        var layout = Production();
        var (cx, cy) = layout.Root;
        var positions = layout.Compute(new[] { new[] { "a", "b", "c" } });

        Assert.Equal(3, positions.Count);
        // 绝对值钉住：RootGap 190、RadiusStep 140 是卡片不重叠且全入面板的观感契约
        var expectedR = new[] { 190.0, 330.0, 470.0 };
        for (var j = 0; j < 3; j++)
        {
            Assert.Equal(cx, positions[j].X, 6);
            Assert.Equal(cy - expectedR[j], positions[j].Y, 6);
            Assert.Equal(j + 1, positions[j].Depth);
            Assert.Equal(0, positions[j].LineIndex);
        }
    }

    [Fact]
    public void MultiLine_SpreadsSymmetricallyAboutVertical()
    {
        var positions = Production().Compute(new[] { new[] { "l" }, new[] { "m" }, new[] { "r" } });
        var (cx, _) = Production().Root;

        Assert.Equal(0.0, positions[1].X - cx, 6); // 中线竖直向上
        Assert.Equal(positions[0].X - cx, -(positions[2].X - cx), 6); // 左右镜像
        Assert.Equal(positions[0].Y, positions[2].Y, 6);
        Assert.True(positions[0].X < cx && positions[2].X > cx);
    }

    [Fact]
    public void Compute_Deterministic_SameInputSameOutput()
    {
        var lines = new[] { new[] { "a", "b" }, new[] { "c" }, new[] { "d", "e", "f", "g" } };
        var first = Production().Compute(lines);
        var second = Production().Compute(lines);

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].X, second[i].X);
            Assert.Equal(first[i].Y, second[i].Y);
            Assert.Equal(first[i].NodeId, second[i].NodeId);
        }
    }

    [Fact]
    public void Compute_EveryNodeGetsUniqueCoordinate()
    {
        var lines = new[] { new[] { "a", "b", "c", "d" }, new[] { "e", "f" }, new[] { "g", "h", "i" } };
        var positions = Production().Compute(lines);
        var coords = positions.Select(p => (Math.Round(p.X, 6), Math.Round(p.Y, 6))).ToList();
        Assert.Equal(coords.Count, coords.Distinct().Count());
    }

    [Fact]
    public void RealTree_CardsStayInsideViewport_AndLeftOfDetailColumn()
    {
        // 以真实天赋树全部大类 + 生产几何验证卡位：生产两步（TalentFanLayout.Compute →
        // TalentFanGeometry.ClampCardCenter，同 TalentFanView.LayoutCards）之后，整张 128px 卡
        // 必须完整落在扇形视口 [0,900]×[0,720] 内，且右缘不得越过详情栏左缘——
        // 越过的部分被详情卡遮挡（ChamferedPanel 的 MouseFilter 默认 Stop）即「看得见、点不到」。
        foreach (var cat in TalentTree.Categories)
        {
            var lines = cat.Lines.Select(l => l.NodeIds).ToList();
            foreach (var p in Production().Compute(lines))
            {
                Assert.InRange(p.X, 0.0, TalentFanGeometry.ViewportWidth);
                Assert.InRange(p.Y, 0.0, TalentFanGeometry.ViewportHeight);

                var (x, y) = TalentFanGeometry.ClampCardCenter(p.X, p.Y);
                var half = TalentFanGeometry.CardSize / 2.0;
                Assert.True(x - half >= -1e-6, $"{p.NodeId} 卡左缘探出视口：x={x}");
                Assert.True(x + half <= TalentFanGeometry.ViewportWidth + 1e-6, $"{p.NodeId} 卡右缘越过视口：x={x}");
                Assert.True(y - half >= -1e-6, $"{p.NodeId} 卡上缘探出视口：y={y}");
                Assert.True(y + half <= TalentFanGeometry.ViewportHeight + 1e-6, $"{p.NodeId} 卡下缘越过视口：y={y}");
                Assert.True(TalentFanGeometry.CardRight(x) <= TalentFanGeometry.DetailColumnLeft + 1e-6,
                    $"{p.NodeId} 卡被详情栏压住：右缘={TalentFanGeometry.CardRight(x)} > 详情栏左缘={TalentFanGeometry.DetailColumnLeft}");
            }
        }
    }

    [Fact]
    public void RealTree_RawLayoutFitsDetailColumn_UnderProductionDomain()
    {
        // 定义域判据：布局域取到真实控件宽度（900）后，径向展开的右端不再越进详情栏。
        // 布局域若退回 1240，second_wind 中心 x≈958.9、卡右缘≈1022.9 越进详情栏，本用例立刻红。
        foreach (var cat in TalentTree.Categories)
        {
            var lines = cat.Lines.Select(l => l.NodeIds).ToList();
            foreach (var p in Production().Compute(lines))
            {
                Assert.True(TalentFanGeometry.CardRight(p.X) <= TalentFanGeometry.DetailColumnLeft + 1e-6,
                    $"{p.NodeId} 卡右缘 {TalentFanGeometry.CardRight(p.X)} 越进详情栏（布局域是否退回 1240？）");
            }
        }
    }

    [Fact]
    public void RealTree_LeftTipNeedsClamp_EvenUnderProductionDomain()
    {
        // 左端末端仍越出扇形视口左缘（offense 四节点支线的 homing 中心 x≈18.7、卡左缘≈−45.3）：
        // 钳制不是死代码，纵向/左向收口在真实树上确实生效。
        var lines = TalentTree.Category("offense").Lines.Select(l => l.NodeIds).ToList();
        var raw = Production().Compute(lines).Single(p => p.NodeId == "homing");
        Assert.True(TalentFanGeometry.CardLeft(raw.X) < 0.0, $"前提失效（未越界）：x={raw.X}");

        var (x, _) = TalentFanGeometry.ClampCardCenter(raw.X, raw.Y);
        Assert.True(x > raw.X, "左端卡未被钳回可用区");
        Assert.Equal(0.0, TalentFanGeometry.CardLeft(x), 6);
    }

    [Fact]
    public void RealTree_ClampEngages_WhenViewportNarrows()
    {
        // 钳制不是死代码：视口收窄（或支线变长）时它把末端卡拉回可用区。
        // defense 的 second_wind 是四节点支线的右端末端——800 宽视口下未钳制中心 x≈738.9、
        // 卡右缘 802.9 已越界。
        const double narrow = 800.0;
        var layout = new TalentFanLayout { Width = narrow, Height = TalentFanGeometry.ViewportHeight };
        var lines = TalentTree.Category("defense").Lines.Select(l => l.NodeIds).ToList();
        var raw = layout.Compute(lines).Single(p => p.NodeId == "second_wind");
        Assert.True(TalentFanGeometry.CardRight(raw.X) > narrow, $"前提失效（未越界）：x={raw.X}");

        var (x, _) = TalentFanGeometry.ClampCardCenter(raw.X, raw.Y, narrow, TalentFanGeometry.ViewportHeight, TalentFanGeometry.CardSize);
        Assert.True(x < raw.X, "右端卡未被钳回可用区");
        Assert.Equal(narrow, TalentFanGeometry.CardRight(x), 6);
    }

    [Fact]
    public void SpecialTip_CardFullyInsideViewport()
    {
        // 竖直中线的四节点末端（combo_guard）：卡顶与卡底都须在视口内
        var special = TalentTree.Category("special");
        var tip = Production().Compute(special.Lines.Select(l => l.NodeIds).ToList())[^1];
        var (_, y) = TalentFanGeometry.ClampCardCenter(tip.X, tip.Y);
        var half = TalentFanGeometry.CardSize / 2.0;
        Assert.True(y - half >= 0.0, $"末端卡片卡顶探出视口：y={y}");
        Assert.True(y + half <= TalentFanGeometry.ViewportHeight, $"末端卡片下缘越出视口：y={y}");
    }
}
