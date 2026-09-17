namespace InfiAir.Core.Combat;

/// <summary>弹体外观档（贴图选择的最小单位；纯表现，与碰撞 / 伤害无关）。</summary>
public enum BulletSkin
{
    /// <summary>玩家弹：白热芯 + 琥珀晕（无描边）。</summary>
    Player = 0,

    /// <summary>敌弹：红 / 品红弹体（默认档，与本文档上线前的实现逐位一致）。</summary>
    Enemy = 1,

    /// <summary>敌弹·高对比：加亮色轮廓（无障碍开关开启时）。</summary>
    EnemyContrast = 2,
}

/// <summary>
/// 弹体外观档判定（零 Godot 依赖，可单测）：给定阵营与高对比开关 → 该用哪张共享贴图。
///
/// 存在理由：这段映射写进 Godot 侧就是 Bullet 里的一行三元，而「关闭开关必须逐位等于原实现」
/// 这条中性态保证没有回归面——接线断掉（永远取描边档、或开关读成常量）的表现是弹幕观感变化，
/// 不崩不报错，冒烟与截图探针都判不到。判定放这里，Godot 侧只做「档位 → 贴图资源」的取用。
///
/// 高对比只作用于敌弹：玩家弹本就是白热芯，加轮廓反而与敌弹同形——两者正是要拉开的东西。
/// 轮廓用**亮色**（不是深色）：本作背景近黑，深色描边在可读像素上等于没画；无障碍编码必须是
/// 亮度编码——玩家弹一眼是"白芯"，敌弹一眼是"白边"，不依赖色相即可区分。
/// </summary>
public static class BulletAppearance
{
    /// <summary>阵营 + 高对比开关 → 外观档。</summary>
    public static BulletSkin SkinFor(bool playerBullet, bool highContrast)
    {
        if (playerBullet)
        {
            return BulletSkin.Player;
        }

        return highContrast ? BulletSkin.EnemyContrast : BulletSkin.Enemy;
    }
}
