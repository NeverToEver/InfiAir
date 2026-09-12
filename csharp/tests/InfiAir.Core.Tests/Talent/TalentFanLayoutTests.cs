using InfiAir.Core.Talent;
using Xunit;

namespace InfiAir.Core.Tests.Talent;

/// <summary>TalentFanLayout 几何契约测试：输出确定性、坐标唯一、扇形对称、
/// 节点落在设计域内。布局是纯函数——坏时玩家看到卡片重叠/飞出面板，
/// 无头冒烟不经过 GPU 管线，只有这里能钉住。</summary>
public sealed class TalentFanLayoutTests
{
    /// <summary>生产口径（TalentFanView._layout）：面板局部坐标 1240×720。</summary>
    private static TalentFanLayout Production() => new() { Width = 1240.0, Height = 720.0 };

    [Fact]
    public void Root_IsBottomCenter()
    {
        var (x, y) = Production().Root;
        Assert.Equal(620.0, x, 6);
        Assert.Equal(720.0 * 0.88, y, 6);
    }

    [Fact]
    public void SingleLine_NodesStraightUpWithRadialSteps()
    {
        var layout = Production();
        var (cx, cy) = layout.Root;
        var positions = layout.Compute(new[] { new[] { "a", "b", "c" } });

        Assert.Equal(3, positions.Count);
        // 绝对值钉住：RootGap 190、RadiusStep 165 是卡片不重叠的观感契约（注释自述 ≥178 约束）
        var expectedR = new[] { 190.0, 355.0, 520.0 };
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
    public void RealTree_AllNodesStayInsideDesignDomain()
    {
        // 以真实天赋树全部大类 + 生产布局参数验证：卡片中心不得飞出面板
        // （面板局部 [0,1240]×[0,720]，飞出即被相邻 UI 遮挡或不可见）。
        // 已知例外（ROADMAP 已登记，待窗口化过目收口）：奇数支线的中线竖直向上，
        // 其第 4 级节点中心 Y≈-51.4 越出面板顶缘（offense.firecontrol 与 special.logistics
        // 两条四节点支线的末端）——钉住精确值，偏离（变好或变坏）即红。
        var knownOverflow = new HashSet<string> { "salvo", "combo_guard" };
        foreach (var cat in TalentTree.Categories)
        {
            var lines = cat.Lines.Select(l => l.NodeIds).ToList();
            foreach (var p in Production().Compute(lines))
            {
                Assert.InRange(p.X, 0.0, 1240.0);
                if (knownOverflow.Contains(p.NodeId))
                {
                    Assert.Equal(-51.4, p.Y, 1);
                }
                else
                {
                    Assert.InRange(p.Y, 0.0, 720.0);
                }
            }
        }
    }
}
