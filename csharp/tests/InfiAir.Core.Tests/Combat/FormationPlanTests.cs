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
}
