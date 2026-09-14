using InfiAir.Core.Progression;
using Xunit;

namespace InfiAir.Core.Tests.Progression;

/// <summary>奖励缩放测试。核心不变量：奖励随难度单调不减、擦弹能吃到连击、
/// 非法输入不倒扣。原实现击杀分与擦弹都是常量——D 涨而收入不涨，是「越后期越难攒点」的成因。</summary>
public sealed class RewardScalingTests
{
    private static RewardScalingConfig Cfg() => new();

    [Fact]
    public void KillScoreFactor_IsIdentityAtDifficultyOne()
    {
        Assert.Equal(1.0, RewardScaling.KillScoreFactor(1.0, Cfg()), 6);
    }

    [Fact]
    public void KillScoreFactor_GrowsMonotonicallyWithDifficulty()
    {
        var cfg = Cfg();
        var prev = 0.0;
        for (var d = 1.0; d <= 30.0; d += 0.5)
        {
            var f = RewardScaling.KillScoreFactor(d, cfg);
            Assert.True(f >= prev, $"D={d} 处回退");
            prev = f;
        }

        // D=5：1 + 0.15×4 = 1.6
        Assert.Equal(1.6, RewardScaling.KillScoreFactor(5.0, cfg), 6);
    }

    [Fact]
    public void KillScoreFactor_DoesNotPenalizeLowDifficulty()
    {
        // D < 1（异常输入）不得倒扣收益
        Assert.Equal(1.0, RewardScaling.KillScoreFactor(0.4, Cfg()), 6);
        Assert.Equal(1.0, RewardScaling.KillScoreFactor(double.NaN, Cfg()), 6);
    }

    [Fact]
    public void GrazeScore_ScalesWithComboWhenWeightEnabled()
    {
        var cfg = Cfg();
        var baseGraze = RewardScaling.GrazeScore(30.0, 1.0, 1.0, cfg);
        var comboed = RewardScaling.GrazeScore(30.0, 2.0, 1.0, cfg);
        Assert.Equal(30.0, baseGraze, 6);
        Assert.Equal(60.0, comboed, 6); // 连击 ×2 → 擦弹也 ×2（weight=1）
    }

    [Fact]
    public void GrazeScore_WeightZero_IgnoresCombo()
    {
        var cfg = Cfg();
        cfg.GrazeComboWeight = 0.0;
        Assert.Equal(27.0, RewardScaling.GrazeScore(27.0, 5.0, 1.0, cfg), 6);
    }

    [Fact]
    public void GrazeScore_ScalesWithDifficulty()
    {
        var cfg = Cfg();
        // D=5：1 + 0.15×4 = 1.6 → 30×1.6 = 48
        Assert.Equal(48.0, RewardScaling.GrazeScore(30.0, 1.0, 5.0, cfg), 6);
    }

    [Fact]
    public void GrazeScore_InvalidInputs_AreSafe()
    {
        var cfg = Cfg();
        Assert.Equal(0.0, RewardScaling.GrazeScore(0.0, 1.0, 5.0, cfg), 6);
        Assert.Equal(0.0, RewardScaling.GrazeScore(-5.0, 1.0, 5.0, cfg), 6);
        Assert.Equal(0.0, RewardScaling.GrazeScore(double.NaN, 1.0, 5.0, cfg), 6);
        // 连击乘区非法（<1 或 NaN）按 ×1 处理，不得倒扣
        Assert.Equal(30.0, RewardScaling.GrazeScore(30.0, double.NaN, 1.0, cfg), 6);
        Assert.Equal(30.0, RewardScaling.GrazeScore(30.0, 0.5, 1.0, cfg), 6);
    }

    [Fact]
    public void GrazeScore_IsMonotonicInCombo()
    {
        var cfg = Cfg();
        var prev = 0.0;
        for (var combo = 1.0; combo <= 12.0; combo += 1.0)
        {
            var v = RewardScaling.GrazeScore(20.0, combo, 3.0, cfg);
            Assert.True(v >= prev, $"连击 {combo} 处擦弹分回退");
            prev = v;
        }
    }
}
