using InfiAir.Core.Missions;
using Xunit;

namespace InfiAir.Core.Tests.Missions;

/// <summary>任务刷新经济判定测试：刷新先扣 RefreshPoints 再抽取，而没有空位时抽取结果为 0 ——
/// 只判「点数够不够」会放行一次「扣了 2 点、面板零变化」的空刷新（按钮可用、成功音效照播）。</summary>
public sealed class MissionRefreshTests
{
    [Fact]
    public void FreeSlots_SubtractsKeptEntriesAndClampsAtZero()
    {
        Assert.Equal(3, MissionRefresh.FreeSlots(slotCount: 3, keptCount: 0));
        Assert.Equal(2, MissionRefresh.FreeSlots(slotCount: 3, keptCount: 1));
        Assert.Equal(0, MissionRefresh.FreeSlots(slotCount: 3, keptCount: 3));
        // 手改存档条目多于槽位：不得算出负空位（负值会被下游当「还有空位」或算出负数抽取数）
        Assert.Equal(0, MissionRefresh.FreeSlots(slotCount: 3, keptCount: 5));
        Assert.Equal(0, MissionRefresh.FreeSlots(slotCount: 0, keptCount: 0));
        Assert.Equal(3, MissionRefresh.FreeSlots(slotCount: 3, keptCount: -2)); // 负保留数按 0 计（不得反算出更多空位之外的怪值）
    }

    [Fact]
    public void CanRefresh_RequiresPointsAndAtLeastOneFreeSlot()
    {
        Assert.True(MissionRefresh.CanRefresh(refreshPoints: 2, refreshCost: 2, slotCount: 3, keptCount: 0));

        // 点数不足
        Assert.False(MissionRefresh.CanRefresh(refreshPoints: 1, refreshCost: 2, slotCount: 3, keptCount: 0));

        // 三个槽位全是「已完成未领取」：保留条目占满槽位，抽取结果必为空——
        // 扣费后玩家看到的仍是同一张面板（成功音效已播），刷新点数是净损失
        Assert.False(MissionRefresh.CanRefresh(refreshPoints: 99, refreshCost: 2, slotCount: 3, keptCount: 3));

        // 坏配置（负费用）不得把「点数 0」判成可刷新
        Assert.False(MissionRefresh.CanRefresh(refreshPoints: 0, refreshCost: -2, slotCount: 3, keptCount: 0));
    }
}
