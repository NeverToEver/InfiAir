class_name StartBackdrop
extends Control
## 开始页装饰背景（StartBackdrop）：全屏静态星空 + 两条全息扫描装饰线。
## 与对局 Starfield 无关的纯界面元素（种子固定、不滚动），配合全遮光罩把
## 开始页与实际游玩画面完全隔开，避免「暂停后继续玩」的错觉。

const COLOR := Color(0.0, 0.83, 1.0)


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	set_anchors_preset(Control.PRESET_FULL_RECT)


func _draw() -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = 20260731
	var rect := get_rect()
	# 三层静态星点：暗底噪 / 中亮 / 少量亮星带十字微光
	for i in 160:
		var p := Vector2(rng.randf() * rect.size.x, rng.randf() * rect.size.y)
		draw_circle(p, rng.randf_range(0.6, 1.4), Color(1.0, 1.0, 1.0, rng.randf_range(0.05, 0.16)))
	for i in 60:
		var p := Vector2(rng.randf() * rect.size.x, rng.randf() * rect.size.y)
		draw_circle(p, rng.randf_range(1.0, 1.8), Color(0.75, 0.92, 1.0, rng.randf_range(0.15, 0.3)))
	for i in 8:
		var p := Vector2(rng.randf() * rect.size.x, rng.randf() * rect.size.y)
		var a := rng.randf_range(0.3, 0.5)
		draw_circle(p, 2.0, Color(0.85, 0.95, 1.0, a))
		draw_line(p + Vector2(-5.0, 0.0), p + Vector2(5.0, 0.0), Color(COLOR, a * 0.6), 1.0, true)
		draw_line(p + Vector2(0.0, -5.0), p + Vector2(0.0, 5.0), Color(COLOR, a * 0.6), 1.0, true)
	# 上下两条全息细线（标题页框感）
	draw_line(Vector2(0.0, 96.0), Vector2(rect.size.x, 96.0), Color(COLOR, 0.10), 1.0, true)
	draw_line(Vector2(0.0, rect.size.y - 80.0), Vector2(rect.size.x, rect.size.y - 80.0), Color(COLOR, 0.08), 1.0, true)
