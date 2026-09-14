namespace InfiAir.Core.Combat;

/// <summary>
/// Boss 阶段门控纯判定：受击后的血量推进与「阶段转换 / 狂暴」触发条件。
/// 纯 .NET、零 Godot 依赖，可独立单测。
///
/// 不变量（单测钉死）：**受击后血量单调不增，且只钳下界 0**。
/// 原实现「血量跌破狂暴线就抬回该线」是两处坏点的共同根因——血量被抬回违反「血条只降」的可读性，
/// 且 `Hp &gt; 0` 前置使致死一击绕过整个狂暴段（少一次清弹与转场，玩家侧不可读）。
/// 狂暴的「锁血」是**狂暴序列期间**的免疫（EnrageSequence），不是「受击时把血抬回」。
/// </summary>
public static class BossPhaseGate
{
    /// <summary>受击后血量：<paramref name="hp"/> − <paramref name="amount"/>，钳下界 0，永不上抬。
    /// 非有限/非正伤害视为无效受击（原样返回）。</summary>
    public static double ApplyDamage(double hp, double amount)
    {
        if (!double.IsFinite(hp) || !double.IsFinite(amount) || amount <= 0.0)
        {
            return hp;
        }

        var next = hp - amount;
        return next > 0.0 ? next : 0.0;
    }

    /// <summary>是否应进入狂暴：未触发过、已进入存活域（&gt;0）、且血量不高于狂暴线。
    /// 狂暴线由 <paramref name="maxHp"/> × <paramref name="ratio"/> 给出。</summary>
    public static bool ShouldEnrage(double hp, double maxHp, double ratio, bool alreadyEnraged)
    {
        if (alreadyEnraged || !double.IsFinite(hp) || hp <= 0.0)
        {
            return false;
        }

        if (!double.IsFinite(maxHp) || maxHp <= 0.0)
        {
            return false;
        }

        var line = maxHp * (double.IsFinite(ratio) && ratio > 0.0 ? ratio : 0.0);
        return hp <= line;
    }

    /// <summary>是否应转入二阶段：仍在一阶段、已进入存活域、且血量不高于二阶段线。
    /// 与狂暴判定同源（单发跨双线时由调用方先转阶段再判狂暴）。</summary>
    public static bool ShouldEnterPhase2(double hp, double maxHp, double ratio, bool inPhase1)
    {
        if (!inPhase1 || !double.IsFinite(hp) || hp <= 0.0)
        {
            return false;
        }

        if (!double.IsFinite(maxHp) || maxHp <= 0.0)
        {
            return false;
        }

        var line = maxHp * (double.IsFinite(ratio) && ratio > 0.0 ? ratio : 0.0);
        return hp <= line;
    }
}
