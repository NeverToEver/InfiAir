namespace InfiAir.Core.Missions;

/// <summary>
/// 任务刷新经济判定（零 Godot 依赖，可单测）。
///
/// 存在理由：刷新先扣 RefreshPoints 再重抽，而已完成未领取的条目会全数保留（防吞待领奖励）。
/// 保留条目占满槽位时抽取结果必为空——只判「点数够不够」会放行一次「扣 2 点、面板零变化」的
/// 空刷新（按钮可用、成功音效照播），玩家看到的是刷新点数净损失而无任何产出。
/// 「能否刷新」必须同时含「至少一个空位」，界面禁用与扣费前判据共用这一份。
/// 受阻原因（<see cref="MissionRefresh.RefreshBlockReason"/>）与可刷新判定同源导出：
/// 按钮置灰时按不动，「按下才提示」对置灰玩家等于没有提示，原因只能由状态驱动常显。
/// </summary>
public static class MissionRefresh
{
    /// <summary>受阻原因：保留条目占满槽位（UI 据此选提示文案）。</summary>
    public const string ReasonSlots = "SLOTS";

    /// <summary>受阻原因：刷新点数不足。</summary>
    public const string ReasonPoints = "POINTS";

    /// <summary>刷新空位数 = 槽位总数 − 保留（已完成未领取）条目数，钳 ≥0
    /// （手改档条目多于槽位时不得算出负空位——负值在下游会被当成「还有空位」）。</summary>
    public static int FreeSlots(int slotCount, int keptCount) =>
        Math.Max(slotCount - Math.Max(keptCount, 0), 0);

    /// <summary>能否刷新：<see cref="RefreshBlockReason"/> 为空。
    /// 判据只有一处——另写一份比较会在改规则时分叉成「按钮可点但刷新被拒」。
    /// 费用 ≤0（坏配置，费用档在配置侧钳 ≥1）判否：否则扣费变成加点，可无限刷新重抽。</summary>
    public static bool CanRefresh(int refreshPoints, int refreshCost, int slotCount, int keptCount) =>
        RefreshBlockReason(refreshPoints, refreshCost, slotCount, keptCount).Length == 0;

    /// <summary>
    /// 刷新受阻原因（"" = 可刷新）：按钮置灰时玩家只能从提示区读出为什么，
    /// 故「为什么不能刷新」与「能不能刷新」必须是同一份判定。
    /// 优先级：无空位 &gt; 点数不足——无空位的自解动作只有「去领取已完成的任务」，
    /// 玩家从按钮置灰看不出这条规则（点数不足则点数读数就摆在旁边的标签上）；
    /// 两者同时成立时先给前者，领取后仍不足会自动改口成点数提示。
    /// </summary>
    public static string RefreshBlockReason(int refreshPoints, int refreshCost, int slotCount, int keptCount)
    {
        if (FreeSlots(slotCount, keptCount) <= 0)
        {
            return ReasonSlots;
        }

        return refreshCost > 0 && refreshPoints >= refreshCost ? "" : ReasonPoints;
    }
}
