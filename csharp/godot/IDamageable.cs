namespace InfiAir;

/// <summary>
/// 对局实体受击契约（可扩展伤害管线）：任何新增可受击单位实现本接口，
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
