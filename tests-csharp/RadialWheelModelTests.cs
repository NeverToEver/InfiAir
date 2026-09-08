using InfiAir.Core;
using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>RadialWheelModel 层级导航 / 弧面滚动 / 命中与渐隐纯逻辑单测。</summary>
public sealed class RadialWheelModelTests
{
    private static RadialWheelOption Leaf(string id) => new() { Id = id, Label = id };

    private static RadialWheelOption Node(string id, params RadialWheelOption[] children) => new()
    {
        Id = id,
        Label = id,
        Children = children,
    };

    /// <summary>根 8 项（27° 槽距 × 8 = 216° > 132° 半幅×2 → 必然溢出需滚动）；其中 A 带子层。</summary>
    private static RadialWheelModel MakeOverflowModel()
    {
        var roots = Enumerable.Range(0, 8).Select(i => Leaf($"OPT{i}")).ToList();
        roots[0] = Node("A", Leaf("A1"), Leaf("A2"));
        return new RadialWheelModel(roots);
    }

    [Fact]
    public void Init_RootLevelWithRootOptions()
    {
        var m = MakeOverflowModel();
        Assert.Equal(1, m.Depth);
        Assert.Equal(8, m.OptionCount);
        Assert.Equal("A", m.Current[0].Id);
    }

    [Fact]
    public void Drill_EntersChildLevel_AndBackReturns()
    {
        var m = MakeOverflowModel();
        Assert.True(m.Drill(0));
        Assert.Equal(2, m.Depth);
        Assert.Equal(2, m.OptionCount);
        Assert.Equal("A1", m.Current[0].Id);
        Assert.True(m.Back());
        Assert.Equal(1, m.Depth);
        Assert.Equal(8, m.OptionCount);
    }

    [Fact]
    public void Drill_LeafOrBadIndex_ReturnsFalse()
    {
        var m = MakeOverflowModel();
        Assert.False(m.Drill(1)); // 叶子
        Assert.False(m.Drill(-1));
        Assert.False(m.Drill(8));
        Assert.Equal(1, m.Depth);
    }

    [Fact]
    public void Back_AtRoot_ReturnsFalse()
    {
        var m = MakeOverflowModel();
        Assert.False(m.Back());
    }

    [Fact]
    public void Drill_DepthCapped_AtMaxDepth()
    {
        // 链式 5 层树：root → A → B → C → D → E；MaxDepth=4 只允许 3 次下钻
        var tree = Node("A", Node("B", Node("C", Node("D", Node("E", Leaf("E1"))))));
        var m = new RadialWheelModel(new[] { tree, Leaf("X") });
        Assert.True(m.Drill(0));
        Assert.True(m.Drill(0));
        Assert.True(m.Drill(0));
        Assert.Equal(RadialWheelModel.MaxDepth, m.Depth);
        Assert.False(m.Drill(0)); // 第 4 次下钻被层深上限挡下
        Assert.Equal("D", m.Current[0].Id);
    }

    [Fact]
    public void Scroll_Clamped_ToItemRange_WhenOverflow()
    {
        var m = MakeOverflowModel();
        m.ScrollBy(99);
        Assert.Equal(7, m.EffectiveScroll, 9);
        m.ScrollTo(-5);
        Assert.Equal(0, m.EffectiveScroll, 9);
    }

    [Fact]
    public void Scroll_CenterBand_WhenAllFit()
    {
        // 3 项 × 27° = 81° < 132° → 装得下，滚动位恒为居中 (n-1)/2，输入滚动无效
        var m = new RadialWheelModel(new[] { Leaf("A"), Leaf("B"), Leaf("C") });
        m.ScrollBy(10);
        Assert.Equal(1.0, m.EffectiveScroll, 9);
        Assert.Equal(1, m.FocusedIndex); // B 居中
    }

    [Fact]
    public void FocusedIndex_RoundsScroll()
    {
        var m = MakeOverflowModel();
        m.ScrollTo(2.4);
        Assert.Equal(2, m.FocusedIndex);
        m.ScrollTo(2.6);
        Assert.Equal(3, m.FocusedIndex);
        Assert.Equal("OPT3", m.FocusedOption?.Id);
    }

    [Fact]
    public void AngleOf_ScrollPositions_ItemsAlongArc()
    {
        var m = MakeOverflowModel();
        Assert.Equal(0, m.AngleOf(0), 9); // scroll=0 时第 0 项在弧面中点
        Assert.Equal(m.SlotAngle, m.AngleOf(1), 9);
        m.ScrollTo(2);
        Assert.Equal(-2 * m.SlotAngle, m.AngleOf(0), 9);
        Assert.Equal(0, m.AngleOf(2), 9);
    }

    [Fact]
    public void Alpha_FullInsideSpan_FadesToZeroAtEdge()
    {
        var m = MakeOverflowModel();
        Assert.Equal(1.0, m.AlphaAt(0), 9);
        Assert.Equal(1.0, m.AlphaAt(m.HalfSpan - m.FadeDeg), 9);
        Assert.Equal(0.0, m.AlphaAt(m.HalfSpan), 9);
        Assert.True(m.AlphaAt(m.HalfSpan - 1) is > 0.0 and < 1.0);
    }

    [Fact]
    public void IndexAtAngle_NearestWithinHalfSlot_OnlyVisible()
    {
        var m = MakeOverflowModel();
        m.ScrollTo(3); // 第 3 项转到弧面中点（scroll=0 时它在 81°、本就在可视弧外）
        Assert.Equal(3, m.IndexAtAngle(m.AngleOf(3)));
        Assert.Equal(3, m.IndexAtAngle(m.AngleOf(3) + m.SlotAngle * 0.49));
        // 相邻槽平铺弧面：距第 3 项超半槽距但落在第 4 项半槽距内 → 命中第 4 项
        Assert.Equal(4, m.IndexAtAngle(m.AngleOf(3) + m.SlotAngle * 0.51));
        Assert.Equal(1, m.IndexAtAngle(-m.HalfSpan)); // 弧端边缘（渐隐中）仍可命中
        // scroll=0 时弧面下端无 item（最近槽 = -2 越界）→ 无命中
        m.ScrollTo(0);
        Assert.Null(m.IndexAtAngle(-m.HalfSpan));
        Assert.Null(m.IndexAtAngle(m.HalfSpan + 1)); // 可视弧外一律无命中
    }

    [Fact]
    public void Easing_EndpointsAndBounce()
    {
        Assert.Equal(0.0, RadialWheelModel.BounceOut(0), 9);
        Assert.Equal(1.0, RadialWheelModel.BounceOut(1), 9);
        // 标准 bounce-out 从下方逼近目标（中途回落），永不越过 1——
        // 收缩的手感来自把进度乘回 [1→q] 区间后产生的「缩过头再弹回」
        Assert.True(RadialWheelModel.BounceOut(0.5) < RadialWheelModel.BounceOut(0.36)); // 回落非单调
        Assert.True(RadialWheelModel.BounceOut(0.5) is > 0.0 and < 1.0);
        Assert.Equal(1.0, RadialWheelModel.EaseOutCubic(1), 9);
        Assert.Equal(0.0, RadialWheelModel.EaseOutCubic(0), 9);
    }

    [Fact]
    public void Easing_BackOvershootsThenSettles()
    {
        Assert.Equal(0.0, RadialWheelModel.EaseOutBack(0), 9);
        Assert.Equal(1.0, RadialWheelModel.EaseOutBack(1), 9);
        Assert.True(RadialWheelModel.EaseOutBack(0.7) > 1.0); // 过冲是弹性入场手感的来源
        Assert.True(RadialWheelModel.EaseOutBack(0.3) is > 0.0 and < 1.0); // 过冲起点（≈0.4）之前仍在爬升
    }

    [Fact]
    public void Drill_ResetsChildScroll_ToFocusFirst()
    {
        var m = MakeOverflowModel();
        m.ScrollTo(3);
        Assert.True(m.Drill(0));
        Assert.Equal(0.5, m.EffectiveScroll, 9); // 子层 2 项装得下 → 居中 (n-1)/2
        Assert.Equal(1, m.FocusedIndex);
    }
}
