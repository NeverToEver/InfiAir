using System.Text.Json;
using InfiAir.Core.Tests;
using InfiAir.Core.Visual;
using Xunit;

namespace InfiAir.Core.Tests.Visual;

/// <summary>损伤分级契约测试（`docs/REFERENCES.md` §4.7「敌人要能在中距离读出状态」）：
/// 比例 → 档位 → 能量层乘子这条链只有一份，档位必须随掉血单调变重、完好档必须逐位不改既有观感。
/// 守的静默错误＝阈值取反或比例算错（「一直显示完好」或「一出生就是重损」）：两者都不崩、不报错，
/// 只让损伤状态这一条读法整体消失——而玩法判定完全不受影响，任何自动门禁都看不见。</summary>
public sealed class DamageStateTests
{
    /// <summary>生产取值（balance `effects.motion.enemy_damage_*`，缺键时的代码回退同值）。</summary>
    private const float Mid = 0.7f;

    private const float Low = 0.4f;

    [Fact]
    public void Of_SplitsAtConfiguredRatios()
    {
        Assert.Equal(UnitDamageTier.Intact, DamageState.Of(100, 100, Mid, Low));
        Assert.Equal(UnitDamageTier.Intact, DamageState.Of(71, 100, Mid, Low));
        // 阈值取等号：比例正好等于阈值即进入该档（与弹反/擦弹的「半径取等号」同口径）
        Assert.Equal(UnitDamageTier.Damaged, DamageState.Of(70, 100, Mid, Low));
        Assert.Equal(UnitDamageTier.Damaged, DamageState.Of(41, 100, Mid, Low));
        Assert.Equal(UnitDamageTier.Critical, DamageState.Of(40, 100, Mid, Low));
        Assert.Equal(UnitDamageTier.Critical, DamageState.Of(0, 100, Mid, Low));
    }

    [Fact]
    public void Of_StaysMonotonicWhenRatiosAreReversed()
    {
        // 手改配置把两档写反：档位仍随掉血单调变重，不能出现「掉血反而变亮」
        Assert.Equal(UnitDamageTier.Intact, DamageState.Of(90, 100, Low, Mid));
        Assert.Equal(UnitDamageTier.Damaged, DamageState.Of(60, 100, Low, Mid));
        Assert.Equal(UnitDamageTier.Critical, DamageState.Of(30, 100, Low, Mid));
    }

    [Fact]
    public void Of_NonFiniteOrZeroMaxFallsBackToIntact()
    {
        // 无血量概念（maxHp ≤ 0）与坏配置：不改变外观，不是随便挑一档
        Assert.Equal(UnitDamageTier.Intact, DamageState.Of(5, 0, Mid, Low));
        Assert.Equal(UnitDamageTier.Intact, DamageState.Of(5, -3, Mid, Low));
        Assert.Equal(UnitDamageTier.Intact, DamageState.Of(5, 10, float.NaN, Low));
        Assert.Equal(UnitDamageTier.Intact, DamageState.Of(5, 10, Mid, float.PositiveInfinity));
    }

    [Fact]
    public void GlowMultiplier_KeepsIntactBitIdenticalAndDimsWithTier()
    {
        // 完好档恒 1.0：能量层调用点乘它之后逐位等于改造前（既有观感不变）
        Assert.Equal(1.0f, DamageState.GlowMultiplier(UnitDamageTier.Intact, 0.7f, 0.45f));
        Assert.Equal(0.7f, DamageState.GlowMultiplier(UnitDamageTier.Damaged, 0.7f, 0.45f));
        Assert.Equal(0.45f, DamageState.GlowMultiplier(UnitDamageTier.Critical, 0.7f, 0.45f));
        // 受损变暗是单向读法：>1 的坏配置钳回 1（受损机比完好机更亮与语义相反）
        Assert.Equal(1.0f, DamageState.GlowMultiplier(UnitDamageTier.Damaged, 1.8f, 0.45f));
        // 非有限回退到「不改变」，而不是 0（0 会让能量层整层消失，像坏在另一件事上）
        Assert.Equal(1.0f, DamageState.GlowMultiplier(UnitDamageTier.Damaged, float.NaN, 0.45f));
        Assert.Equal(0.7f, DamageState.GlowMultiplier(UnitDamageTier.Critical, 0.7f, float.NaN));
    }

    [Fact]
    public void Balance_TiersAreOrderedAndDim()
    {
        using var doc = JsonDocument.Parse(RepoFiles.Read("data/balance.json"));
        var motion = doc.RootElement.GetProperty("effects").GetProperty("motion");

        var midRatio = motion.GetProperty("enemy_damage_mid_ratio").GetDouble();
        var lowRatio = motion.GetProperty("enemy_damage_low_ratio").GetDouble();
        Assert.True(
            lowRatio < midRatio && lowRatio >= 0.0 && midRatio <= 1.0,
            $"损伤档阈值必须满足 0 ≤ low({lowRatio}) < mid({midRatio}) ≤ 1");

        var midGlow = motion.GetProperty("enemy_damage_glow_mid").GetDouble();
        var lowGlow = motion.GetProperty("enemy_damage_glow_low").GetDouble();
        // 低档不得比中档更亮（否则「掉得越狠反而越亮」），且两档都在 (0,1] 内
        Assert.True(
            lowGlow <= midGlow && lowGlow > 0.0 && midGlow <= 1.0,
            $"能量层乘子必须满足 0 < low({lowGlow}) ≤ mid({midGlow}) ≤ 1");
        Assert.True(motion.GetProperty("enemy_damage_fx_interval").GetDouble() > 0.0, "火花间隔必须为正");
    }
}
