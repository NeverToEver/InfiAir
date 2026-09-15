using InfiAir.Core.Progression;
using Xunit;

namespace InfiAir.Core.Tests.Progression;

/// <summary>难度映射测试：把「曲线是什么形状」变成可断言的事实。
/// 原先这些换算散在 Enemy/Boss/Bullet/Spawner，各自斜率只有读代码才知道——调整只能靠实机感受。
/// 关键不变量：单调不减、速度有顶、精英数有顶、Boss 斜率不再等于完整 D。</summary>
public sealed class DifficultyScalingTests
{
    private static DifficultyScalingConfig Cfg() => new();

    [Fact]
    public void Ramps_AtDifficultyOne_AreIdentity()
    {
        var cfg = Cfg();
        Assert.Equal(1.0, DifficultyScaling.EnemyHpRamp(1.0, cfg), 6);
        Assert.Equal(1.0, DifficultyScaling.EnemyDamageRamp(1.0, cfg), 6);
        Assert.Equal(1.0, DifficultyScaling.EnemySpeedRamp(1.0, cfg), 6);
        Assert.Equal(1.0, DifficultyScaling.BossHpRamp(1.0, cfg), 6);
    }

    [Fact]
    public void Ramps_DoNotGoBelowOne_ForDifficultyAtOrUnderOne()
    {
        var cfg = Cfg();
        // D < 1（手改配置/读档异常）不得把敌机削弱成负乘区——下钳 1.0
        Assert.Equal(1.0, DifficultyScaling.EnemyHpRamp(0.5, cfg), 6);
        Assert.Equal(1.0, DifficultyScaling.EnemyDamageRamp(0.0, cfg), 6);
    }

    [Fact]
    public void Ramps_AreMonotonicNonDecreasing()
    {
        var cfg = Cfg();
        var prevHp = 0.0;
        var prevDmg = 0.0;
        var prevSpeed = 0.0;
        for (var d = 1.0; d <= 20.0; d += 0.25)
        {
            var hp = DifficultyScaling.EnemyHpRamp(d, cfg);
            var dmg = DifficultyScaling.EnemyDamageRamp(d, cfg);
            var speed = DifficultyScaling.EnemySpeedRamp(d, cfg);
            Assert.True(hp >= prevHp, $"HP ramp 回退于 D={d}");
            Assert.True(dmg >= prevDmg, $"伤害 ramp 回退于 D={d}");
            Assert.True(speed >= prevSpeed, $"速度 ramp 回退于 D={d}");
            prevHp = hp;
            prevDmg = dmg;
            prevSpeed = speed;
        }
    }

    [Fact]
    public void SpeedRamp_CapBelowOne_NeverWeakensEnemiesBelowIdentity()
    {
        // 速度乘区只能「更快」：上限配置 < 1（手改 balance.json）时按 1.0 处理，
        // 否则 Math.Min 会把后期敌机乘到 1.0 以下——难度越高敌机越慢。
        var cfg = Cfg();
        cfg.SpeedRampCap = 0.5;
        Assert.Equal(1.0, DifficultyScaling.EnemySpeedRamp(10.0, cfg), 6);

        // 上限 ≤0 = 关闭上限（与原语义一致，仍是未钳的线性 ramp）
        cfg.SpeedRampCap = 0.0;
        Assert.Equal(1.0 + 0.10 * 9.0, DifficultyScaling.EnemySpeedRamp(10.0, cfg), 6);
    }

    [Fact]
    public void SpeedRamp_IsCapped()
    {
        var cfg = Cfg();
        // 速度必须有硬顶：它是唯一直接破坏可反应性的量（Brotato 的惯例是 ×2.75，本作取更保守的 1.8）
        var atTen = DifficultyScaling.EnemySpeedRamp(10.0, cfg);
        var atHundred = DifficultyScaling.EnemySpeedRamp(100.0, cfg);
        Assert.Equal(cfg.SpeedRampCap, atTen, 6);
        Assert.Equal(cfg.SpeedRampCap, atHundred, 6);
        // 未触顶区间仍线性
        Assert.Equal(1.0 + 0.10 * 4.0, DifficultyScaling.EnemySpeedRamp(5.0, cfg), 6);
    }

    [Fact]
    public void BossHpRamp_IsSlowerThanFullDifficulty()
    {
        var cfg = Cfg();
        // 原实现 = 完整 D（factor 1.0）：D=10 时 Boss HP ×10、杂兵仅 ×3.25，比值从 16× 拉到 44×。
        // 修正后 Boss 斜率独立且低于 1，两轴差距不再随时间爆炸。
        var boss = DifficultyScaling.BossHpRamp(10.0, cfg);
        Assert.Equal(1.0 + 0.55 * 9.0, boss, 6);
        Assert.True(boss < 10.0, "Boss HP ramp 不应等于完整难度乘数");
    }

    [Fact]
    public void WaveInterval_ShrinksWithDifficultyAndHitsFloor()
    {
        var cfg = Cfg();
        Assert.Equal(4.0, DifficultyScaling.WaveInterval(4.0, 1.0, cfg), 6);
        // D=3：4 / (1+0.15×2) = 4/1.3
        Assert.Equal(4.0 / 1.3, DifficultyScaling.WaveInterval(4.0, 3.0, cfg), 6);
        // 极高 D 触底，不再缩
        Assert.Equal(cfg.WaveIntervalFloor, DifficultyScaling.WaveInterval(4.0, 1000.0, cfg), 6);
    }

    [Fact]
    public void FireInterval_ShrinksWithDifficultyButRespectsFloor()
    {
        var cfg = Cfg();
        Assert.Equal(2.2, DifficultyScaling.FireInterval(2.2, 1.0, cfg), 6);
        Assert.True(DifficultyScaling.FireInterval(2.2, 5.0, cfg) < 2.2);
        // 密度可升但射速不得突破可反应下限（否则后期变成「不更密但更毒」的反面——不可躲）
        Assert.Equal(cfg.FireIntervalFloor, DifficultyScaling.FireInterval(2.2, 1000.0, cfg), 6);
    }

    [Fact]
    public void FireInterval_InvalidBase_FallsBackToFloor()
    {
        var cfg = Cfg();
        Assert.Equal(cfg.FireIntervalFloor, DifficultyScaling.FireInterval(0.0, 5.0, cfg), 6);
        Assert.Equal(cfg.FireIntervalFloor, DifficultyScaling.FireInterval(double.NaN, 5.0, cfg), 6);
    }

    [Fact]
    public void EliteCount_GrowsWithDifficultyAndCaps()
    {
        var cfg = Cfg();
        Assert.Equal(1, DifficultyScaling.EliteCount(1.0, cfg));   // 开局恒 1
        Assert.Equal(1, DifficultyScaling.EliteCount(2.0, cfg));
        Assert.Equal(2, DifficultyScaling.EliteCount(3.0, cfg));   // 每 2.0 D 增 1
        Assert.Equal(2, DifficultyScaling.EliteCount(4.9, cfg));
        Assert.Equal(3, DifficultyScaling.EliteCount(5.0, cfg));
        Assert.Equal(3, DifficultyScaling.EliteCount(50.0, cfg));  // 触顶
        Assert.Equal(1, DifficultyScaling.EliteCount(double.NaN, cfg));
    }

    [Fact]
    public void EliteCount_ConfigOff_StaysOne()
    {
        var cfg = Cfg();
        cfg.ElitePerDifficulty = 0.0;
        Assert.Equal(1, DifficultyScaling.EliteCount(100.0, cfg));
        cfg.ElitePerDifficulty = 2.0;
        cfg.EliteCountCap = 1;
        Assert.Equal(1, DifficultyScaling.EliteCount(100.0, cfg));
    }

    [Fact]
    public void EliteCount_HugeDifficulty_SaturatesInsteadOfWrappingNegative()
    {
        var cfg = Cfg();
        // 档数越过 int 域：(1e10−1)/2 ≈ 5e9 > int.MaxValue，double→int 直接转换回绕成负值 →
        // 精英数从触顶值回落到 1，D 越大精英越少（单调性反转）。
        Assert.Equal(cfg.EliteCountCap, DifficultyScaling.EliteCount(1e10, cfg));
        Assert.Equal(cfg.EliteCountCap, DifficultyScaling.EliteCount(1e12, cfg));
        Assert.True(
            DifficultyScaling.EliteCount(1e12, cfg) >= DifficultyScaling.EliteCount(1e6, cfg),
            "巨大 D 下精英数回退");
    }

    [Fact]
    public void BossDensityBonus_HugeDifficulty_SaturatesInsteadOfWrappingNegative()
    {
        var cfg = Cfg();
        // (1e12−1)/3 ≈ 3.3e11 > int.MaxValue：同样在取整处回绕成负值，追加量从触顶值回落到 0。
        Assert.Equal(cfg.BossDensityBonusCap, DifficultyScaling.BossDensityBonus(1e10, cfg));
        Assert.Equal(cfg.BossDensityBonusCap, DifficultyScaling.BossDensityBonus(1e12, cfg));
        Assert.True(
            DifficultyScaling.BossDensityBonus(1e12, cfg) >= DifficultyScaling.BossDensityBonus(1e6, cfg),
            "巨大 D 下 Boss 弹数追加回退");
    }

    [Fact]
    public void EliteCount_NaNPerDifficulty_StaysOne()
    {
        // 坏配置（NaN 档距）不得让档数取整产出负数
        var cfg = Cfg();
        cfg.ElitePerDifficulty = double.NaN;
        Assert.Equal(1, DifficultyScaling.EliteCount(100.0, cfg));
    }

    [Fact]
    public void BossDensityBonus_NaNPerDifficulty_StaysZero()
    {
        // 坏配置（NaN 档距）与 EliteCount 同款口径：契约是 0..上限。NaN 参与 <= 比较恒假会漏过
        // 关闭闸，随后 (difficulty−1)/NaN = NaN 经 Math.Floor 取整得 int.MinValue——负增量。
        var cfg = Cfg();
        cfg.BossDensityPerDifficulty = double.NaN;
        Assert.Equal(0, DifficultyScaling.BossDensityBonus(100.0, cfg));
    }

    [Fact]
    public void BossDensityBonus_ZeroUntilConfiguredStep()
    {
        var cfg = Cfg(); // 每 3.0 D 追加 1，上限 4
        Assert.Equal(0, DifficultyScaling.BossDensityBonus(1.0, cfg));
        Assert.Equal(0, DifficultyScaling.BossDensityBonus(3.9, cfg));
        Assert.Equal(1, DifficultyScaling.BossDensityBonus(4.0, cfg));
        Assert.Equal(1, DifficultyScaling.BossDensityBonus(6.9, cfg));
        Assert.Equal(2, DifficultyScaling.BossDensityBonus(7.0, cfg));
    }

    [Fact]
    public void BossDensityBonus_CapsAndHandlesConfigOff()
    {
        var cfg = Cfg();
        Assert.Equal(cfg.BossDensityBonusCap, DifficultyScaling.BossDensityBonus(1000.0, cfg));
        cfg.BossDensityPerDifficulty = 0.0;
        Assert.Equal(0, DifficultyScaling.BossDensityBonus(1000.0, cfg));
        cfg.BossDensityPerDifficulty = 3.0;
        cfg.BossDensityBonusCap = 0;
        Assert.Equal(0, DifficultyScaling.BossDensityBonus(1000.0, cfg));
        Assert.Equal(0, DifficultyScaling.BossDensityBonus(double.NaN, cfg));
    }

    [Fact]
    public void BossDensityBonus_IsMonotonicNonDecreasing()
    {
        var cfg = Cfg();
        var prev = 0;
        for (var d = 1.0; d <= 40.0; d += 0.25)
        {
            var bonus = DifficultyScaling.BossDensityBonus(d, cfg);
            Assert.True(bonus >= prev, $"D={d:0.00} 处密度追加回退");
            prev = bonus;
        }
    }

    [Fact]
    public void SoftCappedTimeTerm_FlattensTailOnly()
    {
        var cfg = Cfg();
        // 未过起点：原样
        Assert.Equal(5.0, DifficultyScaling.SoftCappedTimeTerm(5.0, cfg), 6);
        // 过起点：start + (term-start)×factor = 6 + (16-6)×0.5 = 11
        Assert.Equal(11.0, DifficultyScaling.SoftCappedTimeTerm(16.0, cfg), 6);
        // 折减后仍单调不减
        var a = DifficultyScaling.SoftCappedTimeTerm(16.0, cfg);
        var b = DifficultyScaling.SoftCappedTimeTerm(26.0, cfg);
        Assert.True(b > a);
        Assert.Equal(0.0, DifficultyScaling.SoftCappedTimeTerm(-1.0, cfg), 6);
    }

    [Fact]
    public void SoftCappedTimeTerm_NonHalfFactor_KeepsExactFractionOfOvershoot()
    {
        // 折减算术是 start + (超出量)×factor（保留 factor 比例），不是 start + 超出量×(1−factor)。
        // 默认 0.5 恰好等于 1−0.5，原先唯一的数值用例对两种式子同样成立——只有非 0.5 因子能分辨。
        var cfg = Cfg();
        cfg.DifficultyTailSpeedFactor = 0.25;
        Assert.Equal(6.0 + 10.0 * 0.25, DifficultyScaling.SoftCappedTimeTerm(16.0, cfg), 6);
        Assert.Equal(8.5, DifficultyScaling.SoftCappedTimeTerm(16.0, cfg), 6);

        cfg.DifficultyTailSpeedFactor = 0.75;
        Assert.Equal(6.0 + 100.0 * 0.75, DifficultyScaling.SoftCappedTimeTerm(106.0, cfg), 6);
    }

    [Fact]
    public void SoftCappedTimeTerm_ConfigOff_IsIdentity()
    {
        var cfg = Cfg();
        cfg.DifficultyTailSpeedFactor = 1.0; // 折减关闭
        Assert.Equal(16.0, DifficultyScaling.SoftCappedTimeTerm(16.0, cfg), 6);
        cfg.DifficultyTailSpeedFactor = 0.5;
        cfg.DifficultySoftCapStart = 0.0;
        Assert.Equal(16.0, DifficultyScaling.SoftCappedTimeTerm(16.0, cfg), 6);
    }

    [Fact]
    public void SoftCappedTimeTerm_NonPositiveTailFactor_DoesNotFlattenCurve()
    {
        // factor ≤ 0 是「关闭软上限」，不是「超出部分折减到 0」：若守卫漏掉 factor=0，
        // 返回 start + (term−start)×0 = start，曲线在 6.0 处彻底平台化——时间不再加难度，
        // 与长局探针断言的单调性直接冲突（factor<0 更会反向下降）。
        var cfg = Cfg();
        cfg.DifficultyTailSpeedFactor = 0.0;
        Assert.Equal(16.0, DifficultyScaling.SoftCappedTimeTerm(16.0, cfg), 6);
        Assert.Equal(100.0, DifficultyScaling.SoftCappedTimeTerm(100.0, cfg), 6);

        cfg.DifficultyTailSpeedFactor = -0.5;
        Assert.Equal(16.0, DifficultyScaling.SoftCappedTimeTerm(16.0, cfg), 6);
    }
}
