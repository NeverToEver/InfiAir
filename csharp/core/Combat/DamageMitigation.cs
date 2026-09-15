namespace InfiAir.Core.Combat;

/// <summary>受击结算的结果档（决定调用方的副作用归属）。</summary>
public enum DamageOutcome
{
    /// <summary>完全拦截：已死 / 无敌剩余 / 冲刺中 / 本帧已结算过——无任何副作用。</summary>
    Blocked,

    /// <summary>闪避增幅掷中：完全免伤，不置无敌、不写单帧守卫、盾层不消耗、子弹照常销毁。</summary>
    Evaded,

    /// <summary>护盾增幅吸收：每层吸收一次全额伤害，扣层并销毁子弹，不置无敌、不写单帧守卫、不掉血。</summary>
    ShieldAbsorbed,

    /// <summary>实际结算：按护甲减免后扣血，写单帧守卫 + 无敌。</summary>
    Applied,
}

/// <summary>受击减免的输出（Outcome 与最终伤害额；拦下/吸收档的 Amount 原样返还，仅供日志读取）。</summary>
public readonly record struct DamageMitigationResult(DamageOutcome Outcome, float Amount);

/// <summary>
/// 玩家受击减免的两段式顺序判定（纯逻辑，零 Godot 依赖）：
/// 拦截守卫 → 闪避 → 盾吸收 → 护甲 → 结算。数值与增幅层数由引擎侧注入（balance.json augments.*），
/// core 只定顺序与归属。原先整块留在 PlayerDamage 节点里，引擎冒烟只判「不崩」；三种坏法都静默——
/// 顺序写反（护甲先于盾）会白扣一层盾、盾吸收误占单帧守卫会让同帧第二发免费、
/// 闪避层数为 0 时仍掷签会整体偏移全局随机序列。
/// </summary>
public static class DamageMitigation
{
    /// <summary>第一段：拦截守卫（任一为真即不进入减免）。
    /// 单帧守卫（alreadyHitThisFrame）由调用方核算帧号——它必须与调用方「同一物理帧至多结算一次」的
    /// 记账同源，不做成 core 状态，否则两份帧号会分叉。
    /// 抽取时机契约：本判定必须能先于掷签求值（原实现靠 <c>&amp;&amp;</c> 短路达成），
    /// 掷签前移会让被拦下的每一发都多消耗一个随机数。</summary>
    public static bool Blocks(bool dead, bool invincibleRemaining, bool dashing, bool alreadyHitThisFrame)
        => dead || invincibleRemaining || dashing || alreadyHitThisFrame;

    /// <summary>第二段：减免顺序（调用方已确认未被 <see cref="Blocks"/> 拦下）。
    /// evasionRoll 只在 <paramref name="hasEvasion"/> 为真时由调用方掷出（语序与随机消耗见 <see cref="Blocks"/>）；
    /// 判定为严格小于（<c>&lt;</c>）：恰好等于概率不闪避。</summary>
    public static DamageMitigationResult Resolve(
        bool hasEvasion,
        float evasionRoll,
        float evasionChance,
        bool hasShield,
        bool hasArmor,
        float armorMult,
        float amount)
    {
        if (hasEvasion && evasionRoll < evasionChance)
        {
            return new DamageMitigationResult(DamageOutcome.Evaded, amount);
        }

        // 盾吸收优先于护甲：吸收的是全额伤害（先打折再吸层＝每层挡得更少，盾的实际价值对不上文案）
        if (hasShield)
        {
            return new DamageMitigationResult(DamageOutcome.ShieldAbsorbed, amount);
        }

        return new DamageMitigationResult(
            DamageOutcome.Applied, hasArmor ? amount * armorMult : amount);
    }

    /// <summary>单帧守卫记账：只有实际结算才写下当前帧号（返回新的 LastHitFrame）。
    /// 盾吸收**有意**不占用单帧守卫——同帧多弹命中时每层吸收一发（「每层吸收一次」语义优先）；
    /// 若计入守卫则同帧第二弹被免费拦下，盾层数与弹数消耗不对称。</summary>
    public static int RecordHitFrame(DamageOutcome outcome, int currentFrame, int lastHitFrame)
        => outcome == DamageOutcome.Applied ? currentFrame : lastHitFrame;
}
