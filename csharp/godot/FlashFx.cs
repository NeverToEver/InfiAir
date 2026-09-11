using Godot;

namespace InfiAir;

/// <summary>受击闪白共享实现（手动衰减替代 Tween；线性 lerp 回本色，零分配）。
/// 4 处同构收敛（Enemy/Boss/TurretBattery/FormationCraft）；恢复色/时长由调用方注入，
/// 兼容 Boss 狂暴态 BaseModulate() 与 HitFlashByType 表驱动时长。调用方保留 timer 早退与判空守卫。
/// 缩放回弹：受击瞬时按 punch 放大、随闪白线性回位——四处调用方的 Sprite2D 缩放均只在
/// 初始化写一次（无人逐帧驱动），直接施加于 Sprite2D 安全；基准缩放在非闪白期捕获，
/// 由调用方持字段（静态字段禁持 Godot 对象，实例字段各机一份）。</summary>
public static class FlashFx
{
    /// <summary>受击缩放回弹幅度（effects.hit_flash.punch 一次性缓存；&lt;0 未读）。</summary>
    private static float _punch = -1.0f;

    /// <summary>受击闪白触发（四处原值一致：Modulate 2x 白 + 计时器复位）+ 瞬时缩放回弹。</summary>
    public static void Hit(Sprite2D sprite, ref float timer, float total, ref Vector2 punchBase)
    {
        if (_punch < 0.0f)
        {
            _punch = Mathf.Clamp((float)GameState.Instance.Cfg("effects.hit_flash.punch", 0.06).AsDouble(), 0.0f, 0.2f);
        }

        if (timer <= 0.0f)
        {
            punchBase = sprite.Scale; // 闪白中连击不重捕——基准被已放大值污染会让回位越垫越高
        }

        sprite.Modulate = new Color(2.0f, 2.0f, 2.0f); // 受击闪白
        sprite.Scale = punchBase * (1.0f + _punch);
        timer = total;
    }

    /// <summary>闪白逐帧衰减（lerp 回 baseColor；timer ≤ 0 归位 baseColor 与基准缩放）。调用方先判 timer > 0。</summary>
    public static void Update(Sprite2D sprite, ref float timer, float delta, float total, Color baseColor, ref Vector2 punchBase)
    {
        timer -= delta;
        sprite.Modulate = timer <= 0.0f ? baseColor : sprite.Modulate.Lerp(baseColor, delta / total);
        sprite.Scale = timer <= 0.0f ? punchBase : punchBase * (1.0f + _punch * (timer / total));
    }
}
