namespace InfiAir.Core.Combat;

/// <summary>精英炮塔「瞄准角 → 贴图 rotation」换算（纯逻辑，零 Godot 依赖）。
/// 贴图轴向来自生成器（elite_turret.png：炮身在画布上缘、炮口朝上），节点 rotation 的唯一正确取值
/// 是把该轴向旋到瞄准方向所需的角——写反 180° 的表现是炮口朝上、弹体从基座方向射出，
/// 玩家无法从转台朝向读出威胁方向，且无头下不崩不报错。</summary>
public static class TurretAim
{
    /// <summary>贴图炮口轴向（弧度）：炮口朝画布上缘，在 Godot 贴图/局部坐标里即 -Y（-π/2）。</summary>
    public const float MuzzleAxis = -MathF.PI / 2.0f;

    /// <summary>炮口应指向 aimAngle（世界方向角，与弹道方向同源）时，Sprite2D 的 rotation。</summary>
    public static float SpriteRotation(float aimAngle) => aimAngle - MuzzleAxis;

    /// <summary>已给定 rotation 时炮口的世界方向分量（判据的可断言形式：单测钉住它与弹道方向一致）。</summary>
    public static (float X, float Y) MuzzleDirection(float spriteRotation)
    {
        var angle = MuzzleAxis + spriteRotation;
        return (MathF.Cos(angle), MathF.Sin(angle));
    }
}
