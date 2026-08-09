using Godot;

namespace InfiAir;

/// <summary>
/// 统一伤害分派（Y 系列收敛，2026-08-09）：合并 Bullet 直击/溅射与 LaserWeapon 三处
/// 「注册表实体按类型分派 TakeDamage」重复 switch 为单一入口（Bullet.cs:433/378、LaserWeapon.cs:258）。
/// 目标集合 = 注册表四类（Enemy/Boss/TurretBattery/FormationCraft），未知类型静默跳过
/// （与三处原 switch 逐位等价）。
/// 注意：scoreScale 默认 1.0f——激光路径原实现不传（击杀不加分缩放）为既有语义，
/// 调用方按各自语义决定是否传参，禁止顺手修正；爆炸路径的 `is not Enemy` 排除 Boss
/// 过滤在调用侧保留（见 Bullet._explode），不并入本分派。
/// </summary>
public static class EntityDamage
{
    public static void Dispatch(GodotObject target, int damage, float scoreScale = 1.0f)
    {
        switch (target)
        {
            case Enemy enemy:
                enemy.TakeDamage(damage, scoreScale);
                break;
            case Boss boss:
                boss.TakeDamage(damage, scoreScale);
                break;
            case TurretBattery turret:
                turret.TakeDamage(damage, scoreScale);
                break;
            case FormationCraft craft:
                craft.TakeDamage(damage, scoreScale);
                break;
        }
    }
}
