class_name ChamferedPanel
extends Control
## 切角面板（Sci-Fi FUI）：四角斜切的矩形 + 1px 青色细边框，
## 可选四角 L 形括号标记（brackets=true，重要面板开启）。
## 直接作为容器使用：子节点绘制在面板底/边框之上。

@export var chamfer: float = 12.0:
	set(v):
		chamfer = v
		queue_redraw()
@export var brackets: bool = false:
	set(v):
		brackets = v
		queue_redraw()
@export var bg_color: Color = UITheme.PANEL_BG:
	set(v):
		bg_color = v
		queue_redraw()
@export var border_color: Color = UITheme.PANEL_BORDER:
	set(v):
		border_color = v
		queue_redraw()
@export var bracket_color: Color = UITheme.ACCENT:
	set(v):
		bracket_color = v
		queue_redraw()

## 内容自适应边距（面板尺寸 = max(custom_minimum_size, 内容最小尺寸 + padding)）
@export var padding: float = 28.0

var _fit_check_timer: float = 0.0


func _process(delta: float) -> void:
	# 0.1s 节流做内容自适应（子节点尺寸变化时同步面板）
	_fit_check_timer -= delta
	if _fit_check_timer > 0.0:
		return
	_fit_check_timer = 0.1
	var need := _content_min_size()
	var target := custom_minimum_size.max(need)
	if target != custom_minimum_size:
		custom_minimum_size = target
	if size != target:
		size = target


func _content_min_size() -> Vector2:
	var m := Vector2.ZERO
	for c in get_children():
		if c is Control and c.visible:
			m = m.max(c.get_combined_minimum_size())
	return m + Vector2(padding, padding)


func _draw() -> void:
	var c := chamfer
	var w := size.x
	var h := size.y
	if w < c * 2.0 or h < c * 2.0:
		return
	var pts := PackedVector2Array(
		[
			Vector2(c, 0),
			Vector2(w - c, 0),
			Vector2(w, c),
			Vector2(w, h - c),
			Vector2(w - c, h),
			Vector2(c, h),
			Vector2(0, h - c),
			Vector2(0, c),
		]
	)
	draw_colored_polygon(pts, bg_color)
	for i in pts.size():
		draw_line(pts[i], pts[(i + 1) % pts.size()], border_color, 1.0, true)
	if brackets:
		var b := 10.0
		var inset := 3.0
		var corners := [
			[Vector2(inset + b, inset), Vector2(inset, inset), Vector2(inset, inset + b)],
			[Vector2(w - inset - b, inset), Vector2(w - inset, inset), Vector2(w - inset, inset + b)],
			[Vector2(w - inset - b, h - inset), Vector2(w - inset, h - inset), Vector2(w - inset, h - inset - b)],
			[Vector2(inset + b, h - inset), Vector2(inset, h - inset), Vector2(inset, h - inset - b)],
		]
		for corner in corners:
			draw_polyline(PackedVector2Array(corner), bracket_color, 1.5, true)
