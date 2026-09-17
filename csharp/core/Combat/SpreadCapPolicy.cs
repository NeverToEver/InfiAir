namespace InfiAir.Core.Combat;

/// <summary>敌机弹种（spread 同屏上限判定的输入/输出域）。映射到引擎侧 StringName 由调用方负责。</summary>
public enum EnemyBulletKind
{
    /// <summary>单发。</summary>
    Single,

    /// <summary>扇形（受同屏上限约束的唯一弹种）。</summary>
    Spread,

    /// <summary>激光（精英降级目标）。</summary>
    Laser,

    /// <summary>其它弹种（不受上限约束，原样放行）。</summary>
    Other,
}

/// <summary>spread 弹种「同屏上限」判定（纯逻辑，零 Godot 依赖）。
/// 判据＝当前在册（在屏活跃）spread 敌机数 ≥ 难度档上限（easy 1 / medium 2 / hard 3）时降级，
/// 上限取值来自难度档、不在此处产生。判定必须在**实际入场处**执行：敌机延后进场（先抽签、
/// 0.6s 后再 Spawn），同一波敌机的抽签发生在同一帧、在册数彼此不变，只在抽签处判会让整波全部
/// 抽中 spread，上限形同虚设。</summary>
public static class SpreadCapPolicy
{
    /// <summary>候选弹种按上限收敛：非 spread 原样返回；spread 已满则退化（精英 → laser，其余 → single）。
    /// 上限 ≤0 表示不允许在场 spread（在册数 0 也已触顶）。</summary>
    public static EnemyBulletKind Resolve(EnemyBulletKind candidate, int activeSpreadCount, int cap, bool elite)
    {
        if (candidate != EnemyBulletKind.Spread || activeSpreadCount < cap)
        {
            return candidate;
        }

        return elite ? EnemyBulletKind.Laser : EnemyBulletKind.Single;
    }
}
