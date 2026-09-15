namespace InfiAir.Core.Combat;

/// <summary>
/// 同屏敌弹硬上限判据（纯逻辑，零 Godot 依赖）。
/// 判据只看**当前活跃敌弹数**：用「全场弹总数」（玩家弹 + 敌弹）当判据会让上限随玩家火力被吃掉，
/// 表现为玩家打得越猛敌弹越稀（部分敌机开火直接弃发的隐性难度漂移）；玩家弹永不受限。
/// </summary>
public static class EnemyBulletCap
{
    /// <summary>是否应弃发本发弹：只限敌弹，且仅当活跃敌弹数已达上限。</summary>
    public static bool ShouldDrop(bool isEnemyBullet, int activeEnemyCount, int maxActive)
        => isEnemyBullet && activeEnemyCount >= maxActive;
}
