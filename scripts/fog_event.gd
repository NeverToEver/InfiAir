class_name FogEvent
extends GameEvent
## 迷雾事件基类：GameEvent 通用接口的迷雾专门化（中间层）。
## 通用生命周期（start/tick/end + 幂等守卫 + context 浅拷贝）继承自 GameEvent；
## 本类只提供迷雾上下文访问器（context 键约定，由 FogEventManager 注入）：
##   fake_container / overlay_layer / overlay_rect / emit_direction_shift
## 玩家侧状态效果（输入反转/子弹参数）仍走 manager 统一信号（fog_event_started/ended），
## 事件类不触碰 Player。
## 健壮性（2026-08-05 审计）：访问器对缺失/类型不符键返回 null，不抛错；
## 子类应在 _on_start 缓存访问器结果并判空（缺键时降级空转，见各事件实现）。
## 新增迷雾事件的成本见 docs/FOG_EVENTS.md §2.6。


func fake_container() -> Node2D:
	return context.get("fake_container") as Node2D


func overlay_layer() -> CanvasLayer:
	return context.get("overlay_layer") as CanvasLayer


func overlay_rect() -> ColorRect:
	return context.get("overlay_rect") as ColorRect


## 方向偏转脉冲转发（DirectionShiftEvent 经此发出 fog_direction_shift 信号；
## 回调由 FogEventManager 注入，信号仍由 manager 声明/发射保持一致；
## 回调缺失/无效时静默（事件降级为无脉冲，不抛错））
func emit_direction_shift(dir: Vector2, hold: float) -> void:
	var cb: Callable = context.get("emit_direction_shift", Callable())
	if cb.is_valid():
		cb.call(dir, hold)
