using InfiAir.Core.Progression;
using Xunit;

namespace InfiAir.Core.Tests.Progression;

/// <summary>本局达成判定与难度命名档位测试。达成判定是「必死曲线」的收束锚点
/// （Brotato 的「打过 wave20 死亡算胜」同款思路），判错了玩家就不知道打到哪算赢。</summary>
public sealed class RunGoalTests
{
    private static RunGoalConfig Cfg() => new() { BossKillsTarget = 10, SurviveSeconds = 1200.0 };

    [Fact]
    public void NotAchieved_BeforeEitherTarget()
    {
        var cfg = Cfg();
        Assert.False(RunGoal.Achieved(9, 1199.0, cfg));
        Assert.Equal(RunGoalKind.None, RunGoal.Kind(9, 1199.0, cfg));
    }

    [Fact]
    public void Achieved_ByBossKills()
    {
        var cfg = Cfg();
        Assert.True(RunGoal.Achieved(10, 0.0, cfg));
        Assert.Equal(RunGoalKind.BossKills, RunGoal.Kind(10, 0.0, cfg));
    }

    [Fact]
    public void Achieved_BySurvival()
    {
        var cfg = Cfg();
        Assert.True(RunGoal.Achieved(0, 1200.0, cfg));
        Assert.Equal(RunGoalKind.Survive, RunGoal.Kind(0, 1200.0, cfg));
    }

    [Fact]
    public void Achieved_EarliestWins_RegardlessOfOrder()
    {
        var cfg = Cfg();
        // 两个条件同时满足时，Boss 击杀优先（它更「主动」，是玩家操作的直接结果）
        Assert.Equal(RunGoalKind.BossKills, RunGoal.Kind(10, 5000.0, cfg));
    }

    [Fact]
    public void DisabledCondition_IsIgnored()
    {
        var cfg = new RunGoalConfig { BossKillsTarget = 0, SurviveSeconds = 1200.0 };
        Assert.False(RunGoal.Achieved(999, 100.0, cfg));
        Assert.True(RunGoal.Achieved(0, 1200.0, cfg));

        var onlyKills = new RunGoalConfig { BossKillsTarget = 3, SurviveSeconds = 0.0 };
        Assert.True(RunGoal.Achieved(3, 0.0, onlyKills));
        Assert.False(RunGoal.Achieved(0, 99999.0, onlyKills));
    }

    [Fact]
    public void BothDisabled_NeverAchieved_ButProgressFull()
    {
        var cfg = new RunGoalConfig { BossKillsTarget = 0, SurviveSeconds = 0.0 };
        Assert.False(RunGoal.Achieved(50, 99999.0, cfg));
        // 无可追求目标时进度视为满（否则会显示一个永远填不满的条）
        Assert.Equal(1.0, RunGoal.Progress(0, 0.0, cfg), 6);
    }

    [Fact]
    public void Progress_TakesWhicheverIsCloser()
    {
        var cfg = Cfg();
        // 5/10 杀 = 0.5；300/1200 秒 = 0.25 → 取 0.5
        Assert.Equal(0.5, RunGoal.Progress(5, 300.0, cfg), 6);
        // 2/10 = 0.2；600/1200 = 0.5 → 取 0.5
        Assert.Equal(0.5, RunGoal.Progress(2, 600.0, cfg), 6);
    }

    [Fact]
    public void Progress_ClampsAndHandlesInvalidInput()
    {
        var cfg = Cfg();
        Assert.Equal(1.0, RunGoal.Progress(99, 99999.0, cfg), 6);
        Assert.Equal(0.0, RunGoal.Progress(-5, -100.0, cfg), 6);
        Assert.Equal(0.0, RunGoal.Progress(0, double.NaN, cfg), 6);
    }

    // ---------------- DifficultyTier ----------------

    private static readonly double[] Tiers = { 1.0, 1.6, 2.4, 3.6, 5.5, 8.0 };

    [Fact]
    public void DifficultyTier_IndexFollowsThresholds()
    {
        Assert.Equal(0, DifficultyTier.IndexFor(1.0, Tiers));
        Assert.Equal(0, DifficultyTier.IndexFor(1.59, Tiers));
        Assert.Equal(1, DifficultyTier.IndexFor(1.6, Tiers));
        Assert.Equal(2, DifficultyTier.IndexFor(2.4, Tiers));
        Assert.Equal(3, DifficultyTier.IndexFor(5.4, Tiers));
        Assert.Equal(4, DifficultyTier.IndexFor(5.5, Tiers));
        Assert.Equal(5, DifficultyTier.IndexFor(100.0, Tiers));
    }

    [Fact]
    public void DifficultyTier_IsMonotonicNonDecreasing()
    {
        var prev = -1;
        for (var d = 0.5; d <= 30.0; d += 0.1)
        {
            var idx = DifficultyTier.IndexFor(d, Tiers);
            Assert.True(idx >= prev, $"D={d:0.0} 处档位回退");
            prev = idx;
        }
    }

    [Fact]
    public void DifficultyTier_EmptyTableOrInvalidInput_ReturnsZero()
    {
        Assert.Equal(0, DifficultyTier.IndexFor(99.0, System.Array.Empty<double>()));
        Assert.Equal(0, DifficultyTier.IndexFor(double.NaN, Tiers));
    }
}
