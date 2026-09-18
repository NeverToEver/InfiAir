using Godot;

namespace InfiAir;

/// <summary>受击闪白共享实现（手动衰减替代 Tween；线性 lerp 回本色，零分配）。
/// 4 处同构收敛（Enemy/Boss/TurretBattery/FormationCraft）；恢复色/时长由调用方注入，
/// 兼容 Boss 狂暴态 BaseModulate() 与 HitFlashByType 表驱动时长。调用方保留 timer 早退与判空守卫。
/// 缩放回弹：受击瞬时按 punch 放大、随闪白线性回位——四处调用方的 Sprite2D 缩放均只在
/// 初始化写一次（无人逐帧驱动），直接施加于 Sprite2D 安全；基准缩放在非闪白期捕获，
/// 由调用方持字段（静态字段禁持 Godot 对象，实例字段各机一份）。
/// 受击推挤（§2.13）：受击瞬时沿弹道方向微移、指数回位——与闪白共用触发时机但**独立计时**
/// （时长不同），只写贴图 Position（判定几何不动）；状态由调用方持字段，本类只出算式与换算。</summary>
public static class FlashFx
{
    /// <summary>受击缩放回弹幅度（effects.hit_flash.punch 一次性缓存；&lt;0 未读）。</summary>
    private static float _punch = -1.0f;

    /// <summary>推挤时长（effects.motion.hit_push_time）与位移（effects.motion.hit_push_px，
    /// 设计像素；一次性缓存，&lt;0 未读）。</summary>
    private static float _pushTime = -1.0f;
    private static float _pushPx = -1.0f;

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

    /// <summary>受击推挤置位（调用方在 FlashFx.Hit 之后调用；入场中的调用方自行门控不置位）。
    /// worldDir 为弹道方向（世界系）；位移量在此换算好（cfg × world_scale × 动效强度），
    /// 中途拖动强度滑杆影响下一发——与入场落位同粒度。</summary>
    public static void Push(Vector2 worldDir, ref float pushTimer, ref Vector2 pushDir, ref float pushPx)
    {
        if (_pushTime < 0.0f)
        {
            _pushTime = Mathf.Max((float)GameState.Instance.Cfg("effects.motion.hit_push_time", 0.12).AsDouble(), 0.02f);
            _pushPx = Mathf.Max((float)GameState.Instance.Cfg("effects.motion.hit_push_px", 3.0).AsDouble(), 0.0f);
        }

        pushDir = worldDir;
        pushPx = _pushPx * (float)GameState.Instance.WorldScale * (float)GameState.Instance.FxIntensity;
        pushTimer = _pushTime;
    }

    /// <summary>受击推挤逐帧推进（调用方每帧调用，先于闪白 Update）：指数回位包络
    /// （tau = 时长/3，边界与计时归零吻合），只写贴图 Position；方向从世界系换算到贴图本地
    /// （根节点旋转与贴图自身旋转一并抵消——敌机/Boss 根转 π、炮塔贴图带朝向角，同一式通用）。
    /// 结束帧写回零位并停写（避免残留偏移钉住贴图）。timer ≤ 0 早退，静止零成本。</summary>
    public static void PushUpdate(Sprite2D sprite, ref float pushTimer, Vector2 pushDir, float pushPx, float delta)
    {
        if (pushTimer <= 0.0f)
        {
            return;
        }

        pushTimer -= delta;
        var f = pushTimer <= 0.0f
            ? 0.0
            : Core.Visual.BodyPose.PushFactor(_pushTime - pushTimer, _pushTime / 3.0f);
        sprite.Position = f <= 0.0
            ? Vector2.Zero
            : pushDir.Rotated(-sprite.GlobalRotation) * ((float)f * pushPx);
    }

    /// <summary>闪白逐帧衰减（lerp 回 baseColor；timer ≤ 0 归位 baseColor 与基准缩放）。调用方先判 timer > 0。</summary>
    public static void Update(Sprite2D sprite, ref float timer, float delta, float total, Color baseColor, ref Vector2 punchBase)
    {
        timer -= delta;
        sprite.Modulate = timer <= 0.0f ? baseColor : sprite.Modulate.Lerp(baseColor, delta / total);
        sprite.Scale = timer <= 0.0f ? punchBase : punchBase * (1.0f + _punch * (timer / total));
    }
}
