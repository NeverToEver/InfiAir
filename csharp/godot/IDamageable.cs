using Godot;

namespace InfiAir;

/// <summary>
/// 本局实体受击契约（可扩展伤害管线）：任何新增可受击单位实现本接口，
/// 即可被 <see cref="EntityDamage.Dispatch"/> 统一分派，无需修改分派器。
/// 实现类自身负责 Hp 守卫、同帧重复命中防御与死亡结算。
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// 结算一次伤害。amount 为已经过难度/暴击等乘区计算后的最终值；
    /// scoreScale 供击杀计分路径传递分数缩放（默认 1.0 语义由实现类保持）。
    /// </summary>
    void TakeDamage(int amount, float scoreScale);
}

/// <summary>
/// 受击推挤可选契约（§2.13，纯表现层）：实现者受击时沿 pushDir（弹道方向，世界系）做一次
/// 贴图级位移回弹。直击弹道明确的路径（玩家弹直击）经 EntityDamage 分派重载传入；
/// 溅射/激光等无明确单体弹道的伤害走既有 <see cref="IDamageable"/>（不推挤＝默认行为）。
/// </summary>
public interface IPushableDamage
{
    /// <summary>带推挤方向的伤害结算：先置位推挤（表现层）再走常规 TakeDamage 结算（玩法不变）。</summary>
    void TakeDamageWithPush(int amount, float scoreScale, Vector2 pushDir);
}
