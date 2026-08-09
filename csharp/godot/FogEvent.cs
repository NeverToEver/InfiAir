using Godot;

namespace InfiAir;

/// <summary>
/// 迷雾事件基类：GameEvent 通用接口的迷雾专门化（中间层）。
/// 通用生命周期（start/tick/end + 幂等守卫 + context 浅拷贝）继承自 GameEvent；
/// 本类只提供迷雾上下文访问器（context 键约定，由 FogEventManager 注入）：
///   fake_container / overlay_layer / overlay_rect / emit_direction_shift
/// 玩家侧状态效果（输入反转/子弹参数）仍走 manager 统一信号（fog_event_started/ended），
/// 事件类不触碰 Player。
/// 健壮性（2026-08-05 审计）：访问器对缺失/类型不符键返回 null，不抛错；
/// 子类应在 _on_start 缓存访问器结果并判空（缺键时降级空转，见各事件实现）。
/// 迁移期：GDScript 侧不得继承本类（跨语言继承禁令）——测试子类须改 C# 侧定义。
/// </summary>
public partial class FogEvent : GameEvent
{
    /// <summary>伪敌机容器（缺键/类型不符返回 null）。</summary>
    public Node2D? FakeContainer()
        => Context.GetValueOrDefault("fake_container", new Variant()).AsGodotObject() as Node2D;

    /// <summary>精神错乱覆盖层（缺键/类型不符返回 null）。</summary>
    public CanvasLayer? OverlayLayer()
        => Context.GetValueOrDefault("overlay_layer", new Variant()).AsGodotObject() as CanvasLayer;

    /// <summary>覆盖层矩形（缺键/类型不符返回 null）。</summary>
    public ColorRect? OverlayRect()
        => Context.GetValueOrDefault("overlay_rect", new Variant()).AsGodotObject() as ColorRect;

    /// <summary>方向偏转脉冲转发（DirectionShiftEvent 经此发出 fog_direction_shift 信号；
    /// 回调由 FogEventManager 注入，信号仍由 manager 声明/发射保持一致；
    /// 回调缺失/无效时静默（事件降级为无脉冲，不抛错））。</summary>
    public void EmitDirectionShift(Vector2 dir, float hold)
    {
        var cb = Context.GetValueOrDefault("emit_direction_shift", new Variant());
        if (cb.VariantType == Variant.Type.Callable && cb.AsCallable().Target is not null)
        {
            cb.AsCallable().Call(dir, hold);
        }
    }
}
