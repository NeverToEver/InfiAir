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
## 内框线（嵌套切角细线，槽位/socket 质感）：默认关，buff 瓦片类开启
@export var inner_frame: bool = false:
	set(v):
		inner_frame = v
		queue_redraw()
## 内框颜色；alpha=0 时回退为 border_color 半透明
@export var inner_frame_color: Color = Color(0.0, 0.0, 0.0, 0.0):
	set(v):
		inner_frame_color = v
		queue_redraw()

## 内容自适应边距（面板尺寸 = max(custom_minimum_size, 内容最小尺寸 + padding)）
@export var padding: float = 28.0

## 内容自适应高度上限（0 = 不限）：内容超出时面板高度被钳制、不再撑超视口，
## 超限内容由页面自身的滚动容器接管（L17：设置页 modes 页 895px+ 曾把面板撑到 ~1150px）。
@export var max_content_height: float = 0.0

var _fit_check_timer: float = 0.0
## P2：切角几何缓存（尺寸/chamfer 不变即复用，避免布局变化时的重复构建与分配）
var _cached_pts: PackedVector2Array = PackedVector2Array()
var _cached_key_w: float = -1.0
var _cached_key_h: float = -1.0
var _cached_key_c: float = -1.0


func _process(delta: float) -> void:
	# C27：隐藏面板不做内容自适应（消除不可见实例的每帧空转）
	if not is_visible_in_tree():
		return
	# 0.1s 节流做内容自适应（只按内容放大，不缩小显式设定的尺寸——
	# 无子节点的纯背板保持原尺寸，否则会被压缩成小菱形）
	_fit_check_timer -= delta
	if _fit_check_timer > 0.0:
		return
	_fit_check_timer = 0.1
	var need := _content_min_size()
	var target := size.max(custom_minimum_size).max(need)
	# max_content_height：内容自适应只放大不缩小的前提下钳制高度上限（面板不超视口）
	if max_content_height > 0.0:
		target.y = minf(target.y, max_content_height)
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
	# P2：几何缓存——尺寸/chamfer 未变直接复用上次数组
	if _cached_pts.size() == 0 or w != _cached_key_w or h != _cached_key_h or c != _cached_key_c:
		_cached_pts = PackedVector2Array(
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
		_cached_key_w = w
		_cached_key_h = h
		_cached_key_c = c
	draw_colored_polygon(_cached_pts, bg_color)
	for i in _cached_pts.size():
		draw_line(_cached_pts[i], _cached_pts[(i + 1) % _cached_pts.size()], border_color, 2.0, true)
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
	if inner_frame:
		# 嵌套内框：外轮廓内缩 3px 的同款切角细线（socket 质感），角部随外框收小
		var d := 3.0
		var ic := maxf(c - d, 2.0)
		if w >= (d + ic) * 2.0 and h >= (d + ic) * 2.0:
			var col := inner_frame_color if inner_frame_color.a > 0.0 else Color(border_color, border_color.a * 0.5)
			var ipts := PackedVector2Array(
				[
					Vector2(d + ic, d),
					Vector2(w - d - ic, d),
					Vector2(w - d, d + ic),
					Vector2(w - d, h - d - ic),
					Vector2(w - d - ic, h - d),
					Vector2(d + ic, h - d),
					Vector2(d, h - d - ic),
					Vector2(d, d + ic),
				]
			)
			for i in ipts.size():
				draw_line(ipts[i], ipts[(i + 1) % ipts.size()], col, 1.0, true)
