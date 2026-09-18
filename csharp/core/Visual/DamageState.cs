namespace InfiAir.Core.Visual;

/// <summary>损伤外观档位（纯外观，不参与任何玩法判定，也不改碰撞与位置）。
/// 来历：`docs/REFERENCES.md` §4.7 的敌人可读性口径——敌人要能「在中距离读出状态与强弱」，
/// 而不是靠堆装饰。</summary>
public enum UnitDamageTier
{
    /// <summary>完好（血量比例高于中档阈值）：能量层按原强度。</summary>
    Intact = 0,

    /// <summary>中档受损：能量层减弱（走线/喷口变暗），机体底色与轮廓不动。</summary>
    Damaged = 1,

    /// <summary>低档受损：能量层进一步减弱，并叠加低频火花/烟。</summary>
    Critical = 2,
}

/// <summary>
/// 敌机损伤分级的取值算式（纯逻辑，零 Godot 依赖）：血量比例 → 外观档位 → 能量层强度乘子。
/// 生产侧（<c>Enemy</c>）只做「改档时才重写 GlowLayer 参数」的适配。
///
/// 为什么在 core：分档是阈值比较，写错的表现是「一直显示完好」（阈值取反）或
/// 「一出生就是重损」（比例算成 hp/max 之外的量）——画面上无从分辨是配置错还是算式错，
/// 而两者都不崩、不报错、不影响任何判定。
///
/// 全部入参非法时落到 <see cref="UnitDamageTier.Intact"/>（＝不改变外观）：外观异常时
/// 保持原样比随便挑一档安全，也与「档位只是附加读数」的定位一致。
/// </summary>
public static class DamageState
{
    /// <summary>按血量比例分档。midRatio 与 lowRatio 来自 balance
    /// （`effects.motion.enemy_damage_mid_ratio` / `enemy_damage_low_ratio`）：
    /// 比例 ≤ 低档阈值取 <see cref="UnitDamageTier.Critical"/>，≤ 中档阈值取
    /// <see cref="UnitDamageTier.Damaged"/>，其余完好。
    /// 两个阈值都钳到 0..1 并**按大小排序**：手改配置把两档写反时，档位仍单调（比例越低档位越重），
    /// 不会出现「掉血反而变亮」这种自相矛盾的读数。maxHp ≤ 0 视为无血量概念，一律完好。</summary>
    public static UnitDamageTier Of(int hp, int maxHp, float midRatio, float lowRatio)
    {
        if (maxHp <= 0 || !float.IsFinite(midRatio) || !float.IsFinite(lowRatio))
        {
            return UnitDamageTier.Intact;
        }

        var ratio = (float)hp / maxHp;
        if (!float.IsFinite(ratio))
        {
            return UnitDamageTier.Intact;
        }

        var mid = Math.Clamp(Math.Max(midRatio, lowRatio), 0.0f, 1.0f);
        var low = Math.Clamp(Math.Min(midRatio, lowRatio), 0.0f, 1.0f);
        if (ratio <= low)
        {
            return UnitDamageTier.Critical;
        }

        return ratio <= mid ? UnitDamageTier.Damaged : UnitDamageTier.Intact;
    }

    /// <summary>能量层强度乘子：完好恒 1.0（逐位不改既有观感），中档/低档取 balance 的乘子。
    /// 乘子钳到 0..1——分级语义是「受损变暗」，>1 会让受损机比完好机更亮（与读法相反）；
    /// 非有限值一律回退到不改变（1.0 / 中档值），不回退成 0（那会让能量层整层消失，像被打坏了另一件事）。</summary>
    public static float GlowMultiplier(UnitDamageTier tier, float midMultiplier, float lowMultiplier)
    {
        var mid = Math.Clamp(float.IsFinite(midMultiplier) ? midMultiplier : 1.0f, 0.0f, 1.0f);
        if (tier != UnitDamageTier.Critical)
        {
            return tier == UnitDamageTier.Damaged ? mid : 1.0f;
        }

        return Math.Clamp(float.IsFinite(lowMultiplier) ? lowMultiplier : mid, 0.0f, 1.0f);
    }
}
