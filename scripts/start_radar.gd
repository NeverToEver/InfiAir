class_name StartRadar
extends Control
## 开始页右侧装饰雷达（StartRadar）：同心圆环 + 刻度 + 慢速扫描线 + 扫描余晖。
## 纯装饰（mouse_filter ignore），StartPanel process_mode=Always 下暂停态也持续扫描；
## 低透明度青色，与 UITheme 全息青同一视觉语言，不抢菜单层级。

const COLOR := Color(0.0, 0.83, 1.0)
const SWEEP_SPEED := 0.45  # 扫描角速度 rad/s
const TRAIL := 1.1  # 余晖弧长 rad

var _angle := 0.0


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE


func _process(delta: float) -> void:
	# C27：隐藏（StartPanel 关闭进对局）时不再每帧重绘
	if not is_visible_in_tree():
		return
	_angle = fmod(_angle + SWEEP_SPEED * delta, TAU)
	queue_redraw()


func _draw() -> void:
	var c := size / 2.0
	var r := minf(size.x, size.y) / 2.0
	# 同心圆环 ×4（由外向内渐隐）
	for i in 4:
		var rr := r * (1.0 - 0.22 * i)
		draw_arc(c, rr, 0.0, TAU, 96, Color(COLOR, 0.16 - 0.025 * i), 1.5, true)
	# 外圈刻度（每 30°，主刻度更长）
	for i in 12:
		var a := TAU * i / 12.0
		var inner := r * (0.94 if i % 3 == 0 else 0.965)
		draw_line(
			c + Vector2.RIGHT.rotated(a) * inner,
			c + Vector2.RIGHT.rotated(a) * r * 0.995,
			Color(COLOR, 0.28), 1.5, true
		)
	# 十字轴线
	var axis := Color(COLOR, 0.08)
	draw_line(c + Vector2(-r, 0.0), c + Vector2(r, 0.0), axis, 1.0, true)
	draw_line(c + Vector2(0.0, -r), c + Vector2(0.0, r), axis, 1.0, true)
	# 扫描余晖（分段渐隐扇形弧）+ 扫描线
	for i in 8:
		var t := float(i) / 8.0
		draw_arc(c, r * 0.78, _angle - TRAIL * t - 0.06, _angle - TRAIL * t, 4, Color(COLOR, 0.20 * (1.0 - t)), r * 0.35, true)
	draw_line(c, c + Vector2.RIGHT.rotated(_angle) * r * 0.995, Color(COLOR, 0.5), 2.0, true)
	# 固定光点（模拟目标回波）
	for blip in [Vector2(0.35, -0.42), Vector2(-0.5, 0.18), Vector2(0.12, 0.55)]:
		draw_circle(c + blip * r * 0.7, 3.0, Color(COLOR, 0.45))
