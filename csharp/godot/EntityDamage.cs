using Godot;

namespace InfiAir;

/// <summary>
/// 统一伤害分派（可扩展版）：合并 Bullet 直击/溅射与 LaserWeapon 的伤害入口。
/// 目标通过 <see cref="IDamageable"/> 契约匹配——Enemy/Boss/TurretBattery/FormationCraft
/// 均实现该接口；新增可受击单位只需实现接口，无需再修改本分派器。
/// 未知类型静默跳过（与历史按类型 switch 的语义一致）。
/// 注意：scoreScale 默认 1.0f——激光路径不传（击杀不加分缩放）为既有语义；
/// 爆炸路径排除 Boss 的过滤仍在调用侧（Bullet._explode），不并入本分派。
/// </summary>
public static class EntityDamage
{
    public static void Dispatch(GodotObject target, int damage, float scoreScale = 1.0f)
    {
        if (target is IDamageable damageable)
        {
            damageable.TakeDamage(damage, scoreScale);
        }
    }
}
