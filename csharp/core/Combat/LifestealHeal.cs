namespace InfiAir.Core.Combat;

/// <summary>
/// 吸血回复的定额化（纯逻辑，零 Godot 依赖）：单杀回复 = 基础生命上限 × 比例，截断取整、下限 1。
/// 基准必须取基础上限（player.max_health），不得取含 extra_life 加成的当前上限——
/// 敌方伤害是定额（弹 10–21），治疗若按当前上限取比例，extra_life 叠层会同步放大单杀治疗
/// （上限 600 时单杀回 60），生存投入与续航投入复合成对死亡惩罚的完全中和。
/// </summary>
public static class LifestealHeal
{
    /// <summary>fraction ≤0（坏配置）时保底 1：注入侧已钳 ≥0，此处防负治疗；
    /// 取整用截断（与调用方 (int) 历史语义一致）。</summary>
    public static int PerKill(double baseMaxHp, double fraction)
        => Math.Max(1, (int)(baseMaxHp * fraction));
}
