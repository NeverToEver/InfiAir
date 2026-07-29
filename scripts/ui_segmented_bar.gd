class_name SegmentedBar
extends Control
## 分段条（Sci-Fi FUI）：N 段小切角块，填充主强调色，空段暗色。
## 兼容旧 ProgressBar 用法：value / max_value（0-100）。

@export var segments: int = 10:
	set(v):
		segments = v
		queue_redraw()
@export var fill_color: Color = UITheme.ACCENT:
	set(v):
		fill_color = v
		queue_redraw()
@export var empty_color: Color = Color(0.05, 0.09, 0.14, 0.8):
	set(v):
		empty_color = v
		queue_redraw()
@export var frame_color: Color = UITheme.PANEL_BORDER:
	set(v):
		frame_color = v
		queue_redraw()

var max_value: float = 100.0:
	set(v):
		max_value = v
		queue_redraw()
var value: float = 100.0:
	set(v):
		value = v
		queue_redraw()


func _draw() -> void:
	if segments <= 0 or size.y <= 0.0:
		return
	var gap := 2.0
	var seg_w := (size.x - gap * (segments + 1)) / segments
	# 平滑填充：满格数取 floor，最后一段按小数部分宽度部分填充
	var exact := clampf(value / max_value, 0.0, 1.0) * segments
	var filled := int(floor(exact))
	var partial := exact - filled
	for i in segments:
		var x := gap + i * (seg_w + gap)
		var rect := Rect2(x, gap, seg_w, size.y - gap * 2.0)
		if i < filled:
			draw_rect(rect, fill_color)
		elif i == filled and partial > 0.0:
			draw_rect(rect, empty_color)
			draw_rect(Rect2(rect.position, Vector2(seg_w * partial, rect.size.y)), fill_color)
		else:
			draw_rect(rect, empty_color)
	draw_rect(Rect2(Vector2.ZERO, size), frame_color, false, 1.0, true)
