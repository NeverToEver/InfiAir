extends Node2D
## 慢速力场视觉：以玩家为中心的半透明圆环，缓慢脉动。
## 挂在玩家节点下（pausable），暂停时自然停转。

var _t: float = 0.0


func _process(delta: float) -> void:
	_t += delta
	queue_redraw()


func _draw() -> void:
	var alpha := 0.18 + 0.08 * sin(_t * 3.0)
	draw_arc(Vector2.ZERO, 300.0, 0.0, TAU, 64, Color(0.4, 0.9, 1.0, alpha), 3.0)
