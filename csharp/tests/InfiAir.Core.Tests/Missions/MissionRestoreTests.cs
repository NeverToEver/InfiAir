using InfiAir.Core.Missions;
using Xunit;

namespace InfiAir.Core.Tests.Missions;

/// <summary>任务条目读档规范化测试：任务进度与 RP 都从存档读回，读错一侧就是「玩家白领 RP」
/// 或「任务永远完不成」，两者在界面上都没有任何信号。</summary>
public sealed class MissionRestoreTests
{
    [Fact]
    public void Normalize_UnknownMissionId_IsDropped()
    {
        // 未知 id 的 goal 查池得 0 → IsMissionDone（progress >= goal）恒真 → 可反复领取 RP。
        // 白名单判据是「池内定稿 goal 存在」，<paramref name="poolGoal"/> ≤ 0 即未知。
        Assert.Null(MissionRestore.Normalize(poolGoal: 0, progress: 0, baseline: 0, claimed: false));
        Assert.Null(MissionRestore.Normalize(poolGoal: -3, progress: 99, baseline: 0, claimed: false));
    }

    [Fact]
    public void Normalize_GoalComesFromPool_NotFromSave()
    {
        // 手改 goal=0（或任意小值）不得让任务恒为「已完成」：goal 一律取池内定稿值，
        // 存档里的 goal 字段只是显示缓存的旧值，不可信。
        var entry = MissionRestore.Normalize(poolGoal: 15, progress: 3, baseline: 1, claimed: false);

        Assert.NotNull(entry);
        Assert.Equal(15, entry!.Value.Goal);
        Assert.Equal(3, entry.Value.Progress);
        Assert.Equal(1, entry.Value.Baseline);
        Assert.False(entry.Value.Claimed);
    }

    [Fact]
    public void Normalize_NegativeProgressAndBaseline_ClampToZero()
    {
        // baseline 是「绝对值 − 基线」的相对进度基准：负 baseline 会把进度凭空抬高，故钳 0
        var entry = MissionRestore.Normalize(poolGoal: 10, progress: -4, baseline: -7, claimed: true);

        Assert.NotNull(entry);
        Assert.Equal(0, entry!.Value.Progress);
        Assert.Equal(0, entry.Value.Baseline);
        Assert.True(entry.Value.Claimed); // 已领取标记原样保留（丢了会让同一任务可再领一次）
    }

    [Fact]
    public void Normalize_ProgressAboveGoal_IsKept()
    {
        // 进度可超过 goal（SetMissionProgress 只钳非负）：读档不得把超出的部分削掉，
        // 否则下一次刷新基线快照会算错相对进度
        var entry = MissionRestore.Normalize(poolGoal: 5, progress: 40, baseline: 2, claimed: true);

        Assert.Equal(40, entry!.Value.Progress);
        Assert.Equal(5, entry.Value.Goal);
    }
}
