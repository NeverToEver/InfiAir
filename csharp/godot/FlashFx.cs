using Godot;

namespace InfiAir;

/// <summary>受击闪白共享实现（P1-2 手动衰减替代 Tween；线性 lerp 回本色，零分配）。
/// 4 处同构收敛（Enemy/Boss/TurretBattery/FormationCraft）；恢复色/时长由调用方注入，
/// 兼容 Boss 狂暴态 BaseModulate() 与 HitFlashByType 表驱动时长。调用方保留 timer 早退与判空守卫。</summary>
public static class FlashFx
{
    /// <summary>受击闪白触发（四处原值一致：Modulate 2x 白 + 计时器复位）。</summary>
    public static void Hit(Sprite2D sprite, ref float timer, float total)
    {
        sprite.Modulate = new Color(2.0f, 2.0f, 2.0f); // 受击闪白
        timer = total;
    }

    /// <summary>闪白逐帧衰减（lerp 回 baseColor；timer ≤ 0 归位 baseColor）。调用方先判 timer > 0。</summary>
    public static void Update(Sprite2D sprite, ref float timer, float delta, float total, Color baseColor)
    {
        timer -= delta;
        sprite.Modulate = timer <= 0.0f ? baseColor : sprite.Modulate.Lerp(baseColor, delta / total);
    }
}
