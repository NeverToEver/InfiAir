class_name SpawnTelegraph
extends Node2D
## 敌机入场预告：可见区域顶部对应 x 位置的红色竖线 + 箭头，闪烁淡出后自毁。

const DURATION := 0.6
## P2：箭头三角几何静态复用（替代每帧构造 PackedVector2Array）
static var _arrow_triangle := PackedVector2Array([Vector2(-8.0, 70.0), Vector2(8.0, 70.0), Vector2(0.0, 86.0)])

## 实例视觉寿命（spawner 注入 balance.json spawner.telegraph_duration；默认与常量一致，
## 2026-08-06 审计：原硬编码 DURATION 与 spawner 调度计时两套时钟，调参时脱钩）
var duration: float = DURATION

var _t: float = 0.0


func _init(p_x: float, p_top: float = 0.0) -> void:
	position = Vector2(p_x, p_top)


func _process(delta: float) -> void:
	_t += delta
	if _t >= duration:
		queue_free()
	else:
		queue_redraw()


func _draw() -> void:
	var alpha := 0.8 * (1.0 - _t / duration) * (0.6 + 0.4 * Enemy.sin_fast(_t * 30.0))
	var color := Color(1.0, 0.2, 0.2, alpha)
	draw_rect(Rect2(-2.0, 0.0, 4.0, 70.0), color)
	draw_colored_polygon(_arrow_triangle, color)
