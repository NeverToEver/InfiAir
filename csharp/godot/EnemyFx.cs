using Godot;

namespace InfiAir;

/// <summary>敌侧战斗共享工具（体碰伤害公式等；Enemy/Boss 同构收敛，2026-08-11 第二轮）。</summary>
public static class EnemyFx
{
    /// <summary>体碰伤害随对局进程 ramp（与 Boss 弹同一系数；≥1 保底，防 0/负倒扣）。</summary>
    public static int RampCollisionDamage(int baseDamage)
        => Mathf.Max(1, (int)Mathf.Round(baseDamage * (float)GameState.Instance.EnemyDamageRamp()));
}
