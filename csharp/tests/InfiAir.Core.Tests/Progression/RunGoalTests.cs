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
    public void Kind_BothConditionsMet_ReportsBossKillsByFixedPriority()
    {
        var cfg = Cfg();
        // 固定优先级（非「先到者」——纯函数不持历史，无法知道哪个先满足）：两条件都满足时恒报 Boss 击杀。
        // 原名 EarliestWins 名不副实（只测了优先级，从未测「先后」），已按真实语义改名。
        Assert.Equal(RunGoalKind.BossKills, RunGoal.Kind(10, 5000.0, cfg));
        Assert.Equal(RunGoalKind.BossKills, RunGoal.Kind(11, 1200.0, cfg));
        // 只满足存活时仍报存活（优先级不掩盖单条件结果）
        Assert.Equal(RunGoalKind.Survive, RunGoal.Kind(3, 1200.0, cfg));
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

    [Fact]
    public void CloserKind_PicksTheNearerBranch()
    {
        var cfg = Cfg();
        // 5/10 杀 = 0.5；300/1200 秒 = 0.25 → 击杀更近
        Assert.Equal(RunGoalKind.BossKills, RunGoal.CloserKind(5, 300.0, cfg));
        // 2/10 = 0.2；600/1200 = 0.5 → 存活更近
        Assert.Equal(RunGoalKind.Survive, RunGoal.CloserKind(2, 600.0, cfg));
        // 并列（0.5 vs 0.5）取 Boss 击杀——与 Kind 的固定优先级同向
        Assert.Equal(RunGoalKind.BossKills, RunGoal.CloserKind(5, 600.0, cfg));
    }

    [Fact]
    public void CloserKind_SingleBranchConfigured_ReturnsThatBranch()
    {
        // 单支未配置时不得把「未配置」当比例参与比较（HUD 原内联实现用 −1 占位，
        // 在 kill 未配置、survive 已配置且进度为 0 时会把 −1 判成更近的一支）。
        var onlySurvive = new RunGoalConfig { BossKillsTarget = 0, SurviveSeconds = 1200.0 };
        Assert.Equal(RunGoalKind.Survive, RunGoal.CloserKind(0, 0.0, onlySurvive));

        var onlyKills = new RunGoalConfig { BossKillsTarget = 10, SurviveSeconds = 0.0 };
        Assert.Equal(RunGoalKind.BossKills, RunGoal.CloserKind(0, 0.0, onlyKills));

        var none = new RunGoalConfig { BossKillsTarget = 0, SurviveSeconds = 0.0 };
        Assert.Equal(RunGoalKind.None, RunGoal.CloserKind(3, 600.0, none));
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
