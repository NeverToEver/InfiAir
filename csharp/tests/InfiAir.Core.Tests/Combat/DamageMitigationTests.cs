using InfiAir.Core.Combat;
using Xunit;

namespace InfiAir.Core.Tests.Combat;

/// <summary>DamageMitigation 契约测试：受击减免顺序（闪避 → 盾吸收 → 护甲 → 结算）与单帧守卫语义
/// （生产值 armor 0.85 / evasion 0.2，balance.json augments.*）。
/// 这套判定原先整块留在 PlayerDamage 节点里，引擎冒烟只判「不崩」；三种坏法都静默——
/// 顺序写反（护甲先于盾）会白扣一层盾、盾吸收误写 LastHitFrame 会让同帧第二发免费、
/// 闪避层数为 0 时仍掷签会整体偏移全局随机序列。</summary>
public sealed class DamageMitigationTests
{
    private const float ArmorMult = 0.85f;
    private const float EvasionChance = 0.2f;
    private const float Amount = 10.0f;

    [Fact]
    public void GuardsBlockBeforeAnyMitigationStage()
    {
        Assert.True(DamageMitigation.Blocks(true, false, false, false)); // 死亡
        Assert.True(DamageMitigation.Blocks(false, true, false, false)); // 无敌剩余
        Assert.True(DamageMitigation.Blocks(false, false, true, false)); // 冲刺中
        Assert.True(DamageMitigation.Blocks(false, false, false, true)); // 本帧已结算过
        Assert.False(DamageMitigation.Blocks(false, false, false, false));
    }

    [Fact]
    public void ShieldAbsorbsBeforeArmorTouchesAmount()
    {
        var result = DamageMitigation.Resolve(
            hasEvasion: false, evasionRoll: 0.0f, evasionChance: EvasionChance,
            hasShield: true, hasArmor: true, armorMult: ArmorMult, amount: Amount);
        Assert.Equal(DamageOutcome.ShieldAbsorbed, result.Outcome);
        Assert.Equal(Amount, result.Amount); // 盾吸全额：护甲不得先打折
    }

    [Fact]
    public void ArmorMultipliesAmountWhenShieldAbsent()
    {
        var result = DamageMitigation.Resolve(
            hasEvasion: false, evasionRoll: 0.0f, evasionChance: EvasionChance,
            hasShield: false, hasArmor: true, armorMult: ArmorMult, amount: Amount);
        Assert.Equal(DamageOutcome.Applied, result.Outcome);
        Assert.Equal(8.5f, result.Amount, 3);

        var bare = DamageMitigation.Resolve(
            hasEvasion: false, evasionRoll: 0.0f, evasionChance: EvasionChance,
            hasShield: false, hasArmor: false, armorMult: ArmorMult, amount: Amount);
        Assert.Equal(DamageOutcome.Applied, bare.Outcome);
        Assert.Equal(Amount, bare.Amount);
    }

    [Fact]
    public void EvasionWinsOverShieldAndBelowChanceIsStrict()
    {
        var evaded = DamageMitigation.Resolve(
            hasEvasion: true, evasionRoll: EvasionChance - 0.0001f, evasionChance: EvasionChance,
            hasShield: true, hasArmor: true, armorMult: ArmorMult, amount: Amount);
        Assert.Equal(DamageOutcome.Evaded, evaded.Outcome); // 闪避优先于盾：盾层不得消耗

        // 恰好等于概率不闪避（原实现是 < 而非 <=；改成 <= 会让闪避率悄悄高一点）
        var boundary = DamageMitigation.Resolve(
            hasEvasion: true, evasionRoll: EvasionChance, evasionChance: EvasionChance,
            hasShield: true, hasArmor: true, armorMult: ArmorMult, amount: Amount);
        Assert.Equal(DamageOutcome.ShieldAbsorbed, boundary.Outcome);
    }

    [Fact]
    public void NoEvasionLevelNeverEvadesEvenWithLowestRoll()
    {
        var result = DamageMitigation.Resolve(
            hasEvasion: false, evasionRoll: 0.0f, evasionChance: EvasionChance,
            hasShield: false, hasArmor: true, armorMult: ArmorMult, amount: Amount);
        Assert.Equal(DamageOutcome.Applied, result.Outcome);
    }

    [Fact]
    public void OnlyAppliedRecordsHitFrame()
    {
        Assert.Equal(4200, DamageMitigation.RecordHitFrame(DamageOutcome.Applied, 4200, 7));
        Assert.Equal(7, DamageMitigation.RecordHitFrame(DamageOutcome.Blocked, 4200, 7));
        Assert.Equal(7, DamageMitigation.RecordHitFrame(DamageOutcome.Evaded, 4200, 7));
        // 盾吸收有意不写单帧守卫——写进去则同帧第二发被免费拦下，盾层数与弹数消耗不对称
        Assert.Equal(7, DamageMitigation.RecordHitFrame(DamageOutcome.ShieldAbsorbed, 4200, 7));
    }

    [Fact]
    public void SameFrameBulletsEachConsumeOneShieldLayer()
    {
        // 盾 2 层 + 同帧 3 发（无护甲）：前两发各吸收一层，第三发实际扣血并写下帧号
        var loop = new HitLoop { ShieldLayers = 2 };
        Assert.Equal(DamageOutcome.ShieldAbsorbed, loop.Hit(Amount, frame: 4200));
        Assert.Equal(DamageOutcome.ShieldAbsorbed, loop.Hit(Amount, frame: 4200));
        Assert.Equal(DamageOutcome.Applied, loop.Hit(Amount, frame: 4200));
        Assert.Equal(0, loop.ShieldLayers);
        Assert.Equal(4200, loop.LastHitFrame);
        Assert.Equal(100.0f - Amount, loop.Health, 3);
    }

    [Fact]
    public void AppliedHitBlocksRemainingBulletsInSameFrame()
    {
        var loop = new HitLoop();
        Assert.Equal(DamageOutcome.Applied, loop.Hit(Amount, frame: 4200));
        Assert.Equal(DamageOutcome.Blocked, loop.Hit(Amount, frame: 4200));
        Assert.Equal(DamageOutcome.Blocked, loop.Hit(Amount, frame: 4200));
        Assert.Equal(100.0f - Amount, loop.Health, 3);

        // 下一帧恢复结算（单帧守卫只护一帧）
        Assert.Equal(DamageOutcome.Applied, loop.Hit(Amount, frame: 4201));
    }

    [Fact]
    public void ArmorAppliesInsideHitLoopAndDeathDashingGuardsHold()
    {
        var armored = new HitLoop { HasArmor = true };
        Assert.Equal(DamageOutcome.Applied, armored.Hit(Amount, frame: 4200));
        Assert.Equal(100.0f - (Amount * ArmorMult), armored.Health, 3);

        var dashing = new HitLoop { Dashing = true };
        Assert.Equal(DamageOutcome.Blocked, dashing.Hit(Amount, frame: 4200));
        Assert.Equal(100.0f, dashing.Health);

        var dead = new HitLoop { Dead = true };
        Assert.Equal(DamageOutcome.Blocked, dead.Hit(Amount, frame: 4200));
        Assert.Equal(100.0f, dead.Health);
    }

    [Fact]
    public void BlockedHitsDrawNoRandomAndKeepGuardClear()
    {
        // 抽取时机契约：守卫不过就不掷签（原实现靠 && 短路；抽签前移会让无敌期内每发被拦的
        // 弹都多消耗一个随机数，暴击/闪避等抽样整体偏移）。这条钉的是两段式 API 形态——
        // 守卫必须能先于掷签求值，引擎适配层据此先调 Blocks 再取掷签值。
        var loop = new HitLoop { Invincible = true, HasEvasion = true };
        Assert.Equal(DamageOutcome.Blocked, loop.Hit(Amount, frame: 4200));
        Assert.Equal(0, loop.Draws);
        Assert.Equal(-1, loop.LastHitFrame);
        Assert.Equal(100.0f, loop.Health);

        loop.Invincible = false;
        Assert.Equal(DamageOutcome.Evaded, loop.Hit(Amount, frame: 4200, nextEvasionRoll: 0.0f));
        Assert.Equal(1, loop.Draws);
        Assert.Equal(100.0f, loop.Health); // 闪避零伤害
        Assert.Equal(-1, loop.LastHitFrame); // 闪避不置无敌、不写帧号
    }

    /// <summary>PlayerDamage.TakeDamage 的帧内调用序仿真：只复刻调用顺序与副作用归属，
    /// 判定全在 DamageMitigation。掷签用注入值（§5：随机走可注入取值源），另计掷签次数。</summary>
    private sealed class HitLoop
    {
        public bool Dead;
        public bool Invincible;
        public bool Dashing;
        public bool HasArmor;
        public bool HasEvasion;
        public int ShieldLayers;
        public float ArmorMult = 0.85f;
        public float EvasionChance = 0.2f;
        public float Health = 100.0f;
        public int LastHitFrame = -1;
        public int Draws;

        public DamageOutcome Hit(float amount, int frame, float nextEvasionRoll = 0.0f)
        {
            if (DamageMitigation.Blocks(Dead, Invincible, Dashing, LastHitFrame == frame))
            {
                return DamageOutcome.Blocked;
            }

            if (HasEvasion)
            {
                Draws++; // 原实现：AugmentLevel(evasion) > 0 才 GD.Randf()
            }

            var result = DamageMitigation.Resolve(
                HasEvasion,
                HasEvasion ? nextEvasionRoll : 0.0f,
                EvasionChance,
                ShieldLayers > 0,
                HasArmor,
                ArmorMult,
                amount);
            if (result.Outcome == DamageOutcome.ShieldAbsorbed)
            {
                ShieldLayers--; // ConsumeAugment(ShieldId) + 轻震屏
            }
            else if (result.Outcome == DamageOutcome.Applied)
            {
                Health -= result.Amount;
            }

            LastHitFrame = DamageMitigation.RecordHitFrame(result.Outcome, frame, LastHitFrame);
            return result.Outcome;
        }
    }
}
