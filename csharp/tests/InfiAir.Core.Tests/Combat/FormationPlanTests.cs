using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>FormationPlan 契约测试：楔形槽位几何、投弹名次、投弹时刻表。
/// 时刻表的「单调不减」是消费侧的前提——事件按 _stateTime 贪心消费，表一旦乱序，
/// 后面的波次会被提前到同一帧全部投出（表现为同帧轰炸风暴）。</summary>
public sealed class FormationPlanTests
{
    [Fact]
    public void Wedge_PlacesLeaderAtAnchorAndWingsSweptBackAlternately()
    {
        var slots = FormationPlan.Wedge(5, 55.0f);
        Assert.Equal(5, slots.Length);
        Assert.Equal(0.0f, slots[0].X);
        Assert.Equal(0.0f, slots[0].Y);
        // 左右交替、级差按对递增（每两架向后退一档）
        Assert.Equal(-55.0f, slots[1].X);
        Assert.Equal(55.0f, slots[1].Y);
        Assert.Equal(55.0f, slots[2].X);
        Assert.Equal(55.0f, slots[2].Y);
        Assert.Equal(-110.0f, slots[3].X);
        Assert.Equal(110.0f, slots[3].Y);
        Assert.Equal(110.0f, slots[4].X);
        Assert.Equal(110.0f, slots[4].Y);
    }

    [Fact]
    public void Wedge_IsSymmetricAndHandlesDegenerateCounts()
    {
        var slots = FormationPlan.Wedge(4, 55.0f);
        for (var i = 1; i + 1 < slots.Length; i += 2)
        {
            Assert.Equal(-slots[i].X, slots[i + 1].X);
            Assert.Equal(slots[i].Y, slots[i + 1].Y);
        }

        Assert.Single(FormationPlan.Wedge(1, 55.0f));
        Assert.Empty(FormationPlan.Wedge(0, 55.0f));
        Assert.Empty(FormationPlan.Wedge(-3, 55.0f));
    }

    [Fact]
    public void DropRank_OrdersByDistanceToLeaderAndBreaksTiesBySlot()
    {
        // 楔形里同名次的两架到长机距离完全相同：名次必须按槽位索引定序（结果与排序实现无关）
        var slots = FormationPlan.Wedge(5, 55.0f);
        var rank = FormationPlan.DropRank(slots);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, rank);
        Assert.Equal(0, rank[0]);
    }

    [Fact]
    public void DropRank_PutsNearerWingBeforeFartherOne()
    {
        // 手写乱序槽位：名次只由到长机距离决定，与槽位排列无关
        var slots = new[]
        {
            new FormationPlan.Slot(0.0f, 0.0f),
            new FormationPlan.Slot(0.0f, 300.0f),
            new FormationPlan.Slot(0.0f, 100.0f),
        };
        Assert.Equal(new[] { 0, 2, 1 }, FormationPlan.DropRank(slots));
    }

    [Fact]
    public void Schedule_SpacesCraftsWithinVolleyAndBatchesByGap()
    {
        var rank = new[] { 0, 1, 2 };
        var schedule = FormationPlan.Schedule(rank, 2, 2, 0.8f, 0.4f, 1.35f);
        Assert.Equal(12, schedule.Times.Length);
        // 名次 2 的第 2 枚在第二波：1.35 + 2×0.8 + 0.4
        Assert.Equal(3.35f, schedule.LastTime, 3);
        // 波内：长机先投，僚机按名次错开 bomb_interval，同机两枚错开 bomb_stagger
        Assert.Equal(0.0f, schedule.Times[0], 3);
        Assert.Equal(0.4f, schedule.Times[1], 3);
        Assert.Equal(0.8f, schedule.Times[2], 3);
    }

    [Fact]
    public void Schedule_IsMonotonicEvenWhenConfigInvertsTheNestingOrder()
    {
        // bomb_interval 被调到小于连投间隔时，嵌套生成的表本身是乱序的——
        // 排过序才保证消费侧贪心安全（否则同一帧把后续波次全部投出）
        var rank = new[] { 0, 1, 2, 3 };
        var schedule = FormationPlan.Schedule(rank, 3, 4, 0.05f, 0.4f, 0.1f);
        Assert.Equal(48, schedule.Times.Length);
        for (var i = 1; i < schedule.Times.Length; i++)
        {
            Assert.True(schedule.Times[i] >= schedule.Times[i - 1], $"第 {i} 项乱序");
        }
    }

    [Fact]
    public void Schedule_KeepsEveryCraftInEveryBatch()
    {
        var rank = new[] { 0, 1, 2, 3, 4 };
        var schedule = FormationPlan.Schedule(rank, 2, 3, 0.8f, 0.4f, 1.35f);
        var counts = new int[rank.Length];
        foreach (var craft in schedule.Crafts)
        {
            counts[craft] += 1;
        }

        Assert.All(counts, c => Assert.Equal(6, c));
        Assert.Equal(schedule.Times.Length, schedule.Crafts.Length);
    }

    [Fact]
    public void Schedule_CraftSlotsFollowRankNotIndex()
    {
        // 名次与槽位不同序时，先投的是名次 0 的那一架（长机先投，与阵位索引无关）
        var rank = new[] { 2, 1, 0 };
        var schedule = FormationPlan.Schedule(rank, 1, 1, 0.8f, 0.4f, 1.0f);
        Assert.Equal(2, schedule.Crafts[0]);
        Assert.Equal(1, schedule.Crafts[1]);
        Assert.Equal(0, schedule.Crafts[2]);
    }

    [Fact]
    public void Schedule_EmptyForDegenerateConfig()
    {
        var rank = new[] { 0, 1 };
        Assert.Empty(FormationPlan.Schedule(rank, 0, 2, 0.8f, 0.4f, 1.0f).Times);
        Assert.Empty(FormationPlan.Schedule(rank, 2, 0, 0.8f, 0.4f, 1.0f).Times);
        Assert.Empty(FormationPlan.Schedule(Array.Empty<int>(), 2, 2, 0.8f, 0.4f, 1.0f).Times);
    }

    [Fact]
    public void Schedule_ExtremeElementCount_RejectedInsteadOfThrowing()
    {
        // 元素总数用 int 相乘会溢出成负数 → new float[count] 抛异常击穿事件启动。
        // 超出正常量级的配置按「拒绝」处理（返回空表），不得抛。
        var rank = new[] { 0, 1, 2, 3 };
        Assert.Empty(FormationPlan.Schedule(rank, int.MaxValue, 2, 0.8f, 0.4f, 1.0f).Times);
        Assert.Empty(FormationPlan.Schedule(rank, int.MaxValue, int.MaxValue, 0.8f, 0.4f, 1.0f).Times);
        Assert.Empty(FormationPlan.Schedule(rank, 1_000_000, 4, 0.8f, 0.4f, 1.0f).Times);
    }

    [Fact]
    public void AnchorJitter_IsDeterministicAndBounded()
    {
        // 锚点抖动取代 GD 默认随机序列：同一次触发必须可复现，且落在 [0,1)
        Assert.Equal(FormationPlan.AnchorJitter(123.456), FormationPlan.AnchorJitter(123.456));
        foreach (var t in new[] { 0.0, 0.5, 7.7, 61.2, 3600.0, 98765.4321 })
        {
            var j = FormationPlan.AnchorJitter(t);
            // 文档口径 [0,1)：右端开区间必须钉死（InRange 含 1.0 会让「取整落到 1.0」的实现漏网）
            Assert.True(j >= 0.0f && j < 1.0f, $"锚点抖动越界：{j}");
        }

        // 相邻触发时刻应给出不同锚点（低差异序列不退化到常数）
        Assert.NotEqual(FormationPlan.AnchorJitter(60.0), FormationPlan.AnchorJitter(61.0));
    }

    [Fact]
    public void AnchorJitter_NonPositiveOrNonFinite_IsZero()
    {
        Assert.Equal(0.0f, FormationPlan.AnchorJitter(0.0));
        Assert.Equal(0.0f, FormationPlan.AnchorJitter(-5.0));
        Assert.Equal(0.0f, FormationPlan.AnchorJitter(double.NaN));
        Assert.Equal(0.0f, FormationPlan.AnchorJitter(double.PositiveInfinity));
    }

    [Fact]
    public void DropPointVisible_InsideOrWithinMargin_IsVisible()
    {
        // 视域 x ∈ [-100, 100]、y ∈ [0, 200]，余量 12：域内与其外一个余量内的投放点都算可见
        Assert.True(FormationPlan.DropPointVisible(0.0f, 100.0f, -100.0f, 0.0f, 200.0f, 200.0f, 12.0f));
        Assert.True(FormationPlan.DropPointVisible(-112.0f, 100.0f, -100.0f, 0.0f, 200.0f, 200.0f, 12.0f));
        Assert.True(FormationPlan.DropPointVisible(112.0f, -12.0f, -100.0f, 0.0f, 200.0f, 200.0f, 12.0f));
    }

    [Fact]
    public void DropPointVisible_BeyondMargin_IsCut()
    {
        // 编队横穿侧缘的形态：投放点越出右界一个余量以上即不生成
        // （屏外弹不可见不可交互，计进投出数会让「全数拦截」结构性不可达）
        Assert.False(FormationPlan.DropPointVisible(113.0f, 100.0f, -100.0f, 0.0f, 200.0f, 200.0f, 12.0f));
        Assert.False(FormationPlan.DropPointVisible(-113.0f, 100.0f, -100.0f, 0.0f, 200.0f, 200.0f, 12.0f));
        Assert.False(FormationPlan.DropPointVisible(0.0f, 213.0f, -100.0f, 0.0f, 200.0f, 200.0f, 12.0f));
        // 视界右缘 1920 的实测量级：2425 判不可见，1928（余量内）判可见
        Assert.False(FormationPlan.DropPointVisible(2425.0f, 700.0f, 0.0f, 0.0f, 1920.0f, 1080.0f, 12.0f));
        Assert.True(FormationPlan.DropPointVisible(1928.0f, 700.0f, 0.0f, 0.0f, 1920.0f, 1080.0f, 12.0f));
    }

    [Fact]
    public void DropPointVisible_NonFinitePoint_IsCut()
    {
        // 坏坐标（NaN 编队机位置）生成出来只会是 NaN 节点：判不可见即跳过这次投放
        Assert.False(FormationPlan.DropPointVisible(float.NaN, 100.0f, 0.0f, 0.0f, 1920.0f, 1080.0f, 12.0f));
        Assert.False(FormationPlan.DropPointVisible(100.0f, float.PositiveInfinity, 0.0f, 0.0f, 1920.0f, 1080.0f, 12.0f));
    }

    [Fact]
    public void DropPointVisible_NonFiniteView_DoesNotCut()
    {
        // 视域读数坏掉时不裁剪：裁剪不得成为「事件整个不投弹」的静默来源
        var nan = float.NaN;
        Assert.True(FormationPlan.DropPointVisible(5000.0f, 100.0f, nan, 0.0f, nan, nan, nan));
    }
}
