class_name SegmentedBar
extends Control
## 分段条（Sci-Fi FUI）：N 段小切角块，填充主强调色，空段暗色。
## 兼容旧 ProgressBar 用法：value / max_value（0-100）。
## 分段血条（2026-08-03 机制三）：seg_weights 非空时按权重分格（段序 = 文档阶段顺序，
## P1→P2→ENRAGE 从左到右），每段对应一段 HP 区间、消耗从左端开始（P1 段先暗化），
## 段色按 seg_colors 逐段着色；未设置时保持既有等分语义（HP/燃料/dash 条零改动）。

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
## 分段血条段权（非空启用分段模式；空 = 既有等分）。段序从左到右 = 阶段顺序
## （P1→P2→ENRAGE），值变化才 queue_redraw（setter）。未类型化 Array：调用方
## （hud 配置数组/测试字面量）直接赋值，绘制时取 seg_weights[i]（须传 Color 元素，
## 与 seg_weights 的 float() 防御不对称——当前唯一调用方恒传 const Color 数组）
var seg_weights: Array = []:
	set(v):
		seg_weights = v
		queue_redraw()
## 分段血条段色（缺省回退 fill_color；按段序一一对应）
var seg_colors: Array = []:
	set(v):
		seg_colors = v
		queue_redraw()


## 分段模式下第 index 段的消耗度（0..1，纯函数供绘制与测试共用）：
## 段 i 对应 HP 区间 [hi, lo]（首段 hi=1.0 满血，段宽 = 权占比），ratio 低于段上界越多
## 消耗越多——消耗从血条左端（P1 段）开始，与既有「值高左端亮」的整体语义方向一致。
static func segment_fill(ratio: float, weights: Array, index: int) -> float:
	if index < 0 or index >= weights.size():
		return 1.0
	var total := 0.0
	for w in weights:
		total += float(w)
	if total <= 0.0:
		return 1.0
	var hi := 1.0
	var lo := 0.0
	for i in index + 1:
		lo = hi - float(weights[i]) / total
		if i < index:
			hi = lo
	return clampf((hi - ratio) / maxf(hi - lo, 0.0001), 0.0, 1.0)


func _draw() -> void:
	if segments <= 0 or size.y <= 0.0:
		return
	var gap := 2.0
	if not seg_weights.is_empty():
		_draw_weighted(gap)
		return
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


## 分段血条绘制：按权重分格，逐段按消耗度填充（未消耗全亮、部分消耗暗底+右侧亮区、
## 已消耗全暗）。段色按 seg_colors 逐段取色（当前消耗段的高亮 = 段内亮区）。
func _draw_weighted(gap: float) -> void:
	var ratio := clampf(value / max_value, 0.0, 1.0)
	var x := gap
	for i in seg_weights.size():
		var w := float(seg_weights[i]) / _weights_total() * (size.x - gap * (seg_weights.size() + 1))
		var consumed := segment_fill(ratio, seg_weights, i)
		var rect := Rect2(x, gap, w, size.y - gap * 2.0)
		var col: Color = seg_colors[i] if i < seg_colors.size() else fill_color
		if consumed <= 0.0:
			draw_rect(rect, col)
		elif consumed >= 1.0:
			draw_rect(rect, empty_color)
		else:
			draw_rect(rect, empty_color)
			var fill_w := rect.size.x * (1.0 - consumed)
			draw_rect(Rect2(Vector2(rect.position.x + rect.size.x - fill_w, rect.position.y), Vector2(fill_w, rect.size.y)), col)
		x += w + gap
	draw_rect(Rect2(Vector2.ZERO, size), frame_color, false, 1.0, true)


func _weights_total() -> float:
	var total := 0.0
	for w in seg_weights:
		total += float(w)
	return total if total > 0.0 else 1.0
