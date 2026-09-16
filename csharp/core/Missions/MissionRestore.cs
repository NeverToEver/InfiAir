namespace InfiAir.Core.Missions;

/// <summary>
/// 任务条目读档规范化（零 Godot 依赖，可单测）。
///
/// 存在理由：读档此前只做逐字段判型，不过任务池白名单——手改档写一个池外 id 并省略 goal 时，
/// goal 查池得 0，而「已完成」判据是 progress ≥ goal，于是 IsMissionDone 恒真，可反复领取 RP；
/// 同理存档里的 goal 值本身也不可信（写成 0 得到同一条漏洞）。两条一起收口：白名单命中才收，
/// goal 一律取池内定稿值。
///
/// 非法输入一律回退到「保守但可用」的一侧：进度/基线钳非负（负基线会把相对进度凭空抬高），
/// 白名单未命中即整条丢弃（调用方据此保持已有的合法态，而不是留下一条恒真的完成态）。
/// </summary>
public static class MissionRestore
{
    /// <summary>
    /// 规范化单条任务条目。返回 null = 丢弃（<paramref name="poolGoal"/> ≤ 0，即任务池里没有该 id）。
    /// goal 取自任务池（调用方查询），不读存档；progress/baseline 钳非负；claimed 原样保留
    /// （丢了会让同一任务可以再领一次奖励）。
    /// </summary>
    public static (int Progress, int Goal, int Baseline, bool Claimed)? Normalize(
        int poolGoal, int progress, int baseline, bool claimed)
    {
        if (poolGoal <= 0)
        {
            return null;
        }

        return (Math.Max(progress, 0), poolGoal, Math.Max(baseline, 0), claimed);
    }
}
