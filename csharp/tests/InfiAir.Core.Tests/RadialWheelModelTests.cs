using Xunit;

namespace InfiAir.Core.Tests;

/// <summary>RadialWheelModel 契约测试：下钻/回退状态迁移、滚动钳制、焦点序、角度命中。
/// 标题 / 设置 / 暂停三处导航共用这一份状态机——焦点错位或下钻栈泄漏时
/// 玩家看到的是「轮盘选不中条目」，引擎冒烟与构建零警告都跑不到。</summary>
public sealed class RadialWheelModelTests
{
    private static RadialWheelOption Leaf(string id) => new() { Id = id, Label = id };

    private static RadialWheelOption Dir(string id, params RadialWheelOption[] children) =>
        new() { Id = id, Label = id, Children = children };

    /// <summary>n 个叶子的平铺轮盘（默认 SlotAngle 27 / HalfSpan 66 下 n≤4 全容、n≥5 需滚动）。</summary>
    private static RadialWheelModel Flat(int n)
    {
        var roots = new List<RadialWheelOption>();
        for (var i = 0; i < n; i++)
        {
            roots.Add(Leaf($"r{i}"));
        }

        return new RadialWheelModel(roots);
    }

    [Fact]
    public void Ctor_EmptyRoots_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RadialWheelModel(Array.Empty<RadialWheelOption>()));
    }

    [Fact]
    public void Drill_DirectoryNode_PushesLevelAndResetsFocus()
    {
        var wheel = new RadialWheelModel(new[]
        {
            Dir("audio", Leaf("bgm"), Leaf("sfx")),
            Leaf("quit"),
        });
        wheel.MoveFocus(1); // 下钻前焦点偏移必须随新层清零，否则子层焦点带着父层残影

        Assert.True(wheel.Drill(0));
        Assert.Equal(2, wheel.Depth);
        Assert.Equal(new[] { "bgm", "sfx" }, wheel.Current.Select(o => o.Id));
        Assert.Equal(0.0, wheel.Scroll);
        Assert.Equal(0, wheel.FocusBias);
    }

    [Fact]
    public void Drill_LeafOrOutOfRange_ReturnsFalse()
    {
        var wheel = Flat(3);
        Assert.False(wheel.Drill(0)); // 叶子语义由调用方触发确认，状态机不入栈
        Assert.False(wheel.Drill(-1));
        Assert.False(wheel.Drill(3));
        Assert.Equal(1, wheel.Depth);
    }

    [Fact]
    public void Drill_AtMaxDepth_RefusesEvenWithChildren()
    {
        // 层深上限防深树无限下钻——栈到底即视为叶子
        var deep = Dir("d1", Dir("d2", Dir("d3", Dir("d4", Leaf("x")))));
        var wheel = new RadialWheelModel(new[] { deep });
        Assert.True(wheel.Drill(0));
        Assert.True(wheel.Drill(0));
        Assert.True(wheel.Drill(0));
        Assert.Equal(RadialWheelModel.MaxDepth, wheel.Depth);
        Assert.False(wheel.CanDrill(0));
        Assert.False(wheel.Drill(0));
    }

    [Fact]
    public void Back_AtRoot_ReturnsFalse()
    {
        Assert.False(Flat(2).Back());
    }

    [Fact]
    public void Back_AfterDrill_PopsLevelAndResetsFocus()
    {
        var wheel = new RadialWheelModel(new[] { Dir("d", Leaf("a"), Leaf("b")), Leaf("q") });
        wheel.Drill(0);
        wheel.MoveFocus(1);

        Assert.True(wheel.Back());
        Assert.Equal(1, wheel.Depth);
        Assert.Equal("d", wheel.Current[0].Id);
        Assert.Equal(0, wheel.FocusBias);
    }

    [Fact]
    public void EffectiveScroll_AllVisible_CentersAndIgnoresRawScroll()
    {
        var wheel = Flat(4); // 4×27=108 ≤ 132 全容
        wheel.ScrollTo(3.0);
        Assert.Equal(1.5, wheel.EffectiveScroll); // (n-1)/2 居中，滚动位失效
    }

    [Fact]
    public void EffectiveScroll_Overflow_ClampsRawScroll()
    {
        var wheel = Flat(6); // 6×27=162 > 132 需滚动
        wheel.ScrollTo(10.0);
        Assert.Equal(5.0, wheel.EffectiveScroll);
        wheel.ScrollTo(-2.0);
        Assert.Equal(0.0, wheel.EffectiveScroll);
    }

    [Fact]
    public void FocusedIndex_RoundsAwayFromZero()
    {
        var wheel = Flat(6);
        wheel.ScrollTo(2.5);
        Assert.Equal(3, wheel.FocusedIndex);
        wheel.ScrollTo(2.4);
        Assert.Equal(2, wheel.FocusedIndex);
    }

    [Fact]
    public void MoveFocus_ClampsAtEdges()
    {
        var low = Flat(4); // 全容态居中 1.5 → 四舍五入基准 2
        low.MoveFocus(-10);
        Assert.Equal(0, low.FocusedIndex);
        var high = Flat(4);
        high.MoveFocus(10);
        Assert.Equal(3, high.FocusedIndex);
    }

    [Fact]
    public void AngleOf_IndexMinusEffectiveScrollTimesSlot()
    {
        var wheel = Flat(6);
        wheel.ScrollTo(2.0);
        Assert.Equal(54.0, wheel.AngleOf(4), 6);
        Assert.Equal(-54.0, wheel.AngleOf(0), 6);
    }

    [Fact]
    public void AlphaAt_CenterFullEdgeZeroFadeSmoothstep()
    {
        var wheel = Flat(2);
        Assert.Equal(1.0, wheel.AlphaAt(0.0));
        Assert.Equal(0.0, wheel.AlphaAt(66.0)); // |弧角| ≥ HalfSpan 不可见
        Assert.Equal(0.0, wheel.AlphaAt(70.0));
        // 渐隐带内 smoothstep：|60| → t=(66-60)/18=1/3 → t²(3-2t)=7/27
        Assert.Equal(7.0 / 27.0, wheel.AlphaAt(60.0), 6);
    }

    [Fact]
    public void IndexAtAngle_OutsideSpan_ReturnsNull()
    {
        Assert.Null(Flat(6).IndexAtAngle(67.0));
    }

    [Fact]
    public void IndexAtAngle_HitsNearestSlot()
    {
        var wheel = Flat(6);
        wheel.ScrollTo(2.0); // 槽 4 在 +54°
        Assert.Equal(4, wheel.IndexAtAngle(54.0));
        Assert.Equal(4, wheel.IndexAtAngle(49.0)); // 半槽距内吸附
    }

    [Fact]
    public void IndexAtAngle_NoCandidateInRange_ReturnsNull()
    {
        var wheel = Flat(3); // 全容居中 1.0，+55° 落到第 3 槽之外
        Assert.Null(wheel.IndexAtAngle(55.0));
    }

    [Fact]
    public void Easing_EndpointsPinned()
    {
        Assert.Equal(0.0, RadialWheelModel.BounceOut(0.0), 9);
        Assert.Equal(1.0, RadialWheelModel.BounceOut(1.0), 9);
        Assert.Equal(0.0, RadialWheelModel.EaseOutCubic(0.0), 9);
        Assert.Equal(1.0, RadialWheelModel.EaseOutCubic(1.0), 9);
        Assert.Equal(0.0, RadialWheelModel.EaseOutBack(0.0), 9);
        Assert.Equal(1.0, RadialWheelModel.EaseOutBack(1.0), 9);
    }

    [Fact]
    public void EaseOutBack_OvershootsAboveOne()
    {
        // 弹性入场的峰值过冲（≈1.1）是观感契约——钳回 1.0 卡片入场就变死
        Assert.True(RadialWheelModel.EaseOutBack(0.6) > 1.0);
    }

    [Fact]
    public void EaseOutCubic_ClampsOutOfRangeInput()
    {
        Assert.Equal(0.0, RadialWheelModel.EaseOutCubic(-1.0), 9);
        Assert.Equal(1.0, RadialWheelModel.EaseOutCubic(2.0), 9);
    }
}
