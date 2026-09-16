namespace InfiAir.Core.Missions;

/// <summary>
/// 任务刷新经济判定（零 Godot 依赖，可单测）。
///
/// 存在理由：刷新先扣 RefreshPoints 再重抽，而已完成未领取的条目会全数保留（防吞待领奖励）。
/// 保留条目占满槽位时抽取结果必为空——只判「点数够不够」会放行一次「扣 2 点、面板零变化」的
/// 空刷新（按钮可用、成功音效照播），玩家看到的是刷新点数净损失而无任何产出。
/// 「能否刷新」必须同时含「至少一个空位」，界面禁用与扣费前判据共用这一份。
/// </summary>
public static class MissionRefresh
{
    /// <summary>刷新空位数 = 槽位总数 − 保留（已完成未领取）条目数，钳 ≥0
    /// （手改档条目多于槽位时不得算出负空位——负值在下游会被当成「还有空位」）。</summary>
    public static int FreeSlots(int slotCount, int keptCount) =>
        Math.Max(slotCount - Math.Max(keptCount, 0), 0);

    /// <summary>能否刷新：费用为正 **且** 点数足够 **且** 至少一个空位。
    /// 费用 ≤0（坏配置，费用档在配置侧钳 ≥1）判否：否则扣费变成加点，可无限刷新重抽。</summary>
    public static bool CanRefresh(int refreshPoints, int refreshCost, int slotCount, int keptCount) =>
        refreshCost > 0 && refreshPoints >= refreshCost && FreeSlots(slotCount, keptCount) > 0;
}
