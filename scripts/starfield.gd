class_name Starfield
extends Node2D
## 程序化双层视差滚动星空背景。

var FAR_COUNT := 140
var NEAR_COUNT := 90
var FAR_SPEED := 60.0
var NEAR_SPEED := 140.0

var _far: Array[Vector2] = []
var _near: Array[Vector2] = []
## P1-4：预分配线段数组（draw_multiline 合批：每层 1 条指令替代逐星 draw_circle，
## 星点以 1px 短线段 + 线宽呈现，视觉等价于原圆点；_process 原地写，零每帧分配）
var _far_lines: PackedVector2Array = PackedVector2Array()
var _near_lines: PackedVector2Array = PackedVector2Array()
const LINE_LEN := 1.0
## 返航过场的星光拉伸倍率，随时间衰减回 1
var warp_factor: float = 1.0
## C07 修复：可见世界区域尺寸缓存（view_world_rect），替代硬编码 1920×1080
var _area_size: Vector2 = Vector2(1920.0, 1080.0)
## 2026-08-06 审计 M5：星点区域锚点（_ready 时可见区左上角）——原恒 (0,0) 铺
## [0,1920/zoom]×[0,1080/zoom]，zoom>1 时可见区右/下边缘 L 形带无星（C07 只改了
## 尺寸未改锚点，「恒覆盖」论证前提被自身破坏）；锚点随可见区平移，回绕同基线
var _origin: Vector2 = Vector2.ZERO


## A7：测试/诊断白盒断言经公开接口（M5 断言星空覆盖区域）
func origin() -> Vector2:
	return _origin


func area_size() -> Vector2:
	return _area_size


func warp(factor: float) -> void:
	warp_factor = factor


func _ready() -> void:
	z_index = -10
	# R07：判型 + 非负钳制（L 系列判型族登记遗留）——字符串/负数手改配置不崩、
	# 不做负尺寸 resize
	var fc: Variant = GameState.cfg("effects.starfield.far_count", FAR_COUNT)
	if fc is int and not fc is bool and fc >= 0:
		FAR_COUNT = fc
	var nc: Variant = GameState.cfg("effects.starfield.near_count", NEAR_COUNT)
	if nc is int and not nc is bool and nc >= 0:
		NEAR_COUNT = nc
	FAR_SPEED = GameState.cfg("effects.starfield.far_speed", FAR_SPEED)
	NEAR_SPEED = GameState.cfg("effects.starfield.near_speed", NEAR_SPEED)
	var rng := RandomNumberGenerator.new()
	rng.seed = 12345
	# C07：星点范围随可见世界区域（view_world_rect）而非写死 1920×1080；
	# M5：区域锚点 = 可见区左上角（zoom>1 时可见区缩小平移，锚 (0,0) 覆盖不到右/下边缘）
	var view := GameState.view_world_rect()
	_area_size = view.size
	_origin = view.position
	for i in FAR_COUNT:
		_far.append(Vector2(_origin.x + rng.randf() * _area_size.x, _origin.y + rng.randf() * _area_size.y))
	for i in NEAR_COUNT:
		_near.append(Vector2(_origin.x + rng.randf() * _area_size.x, _origin.y + rng.randf() * _area_size.y))
	# P1-4：线段数组一次性分配（每星 2 点：起点 + 1px 尾端）
	_far_lines.resize(FAR_COUNT * 2)
	_near_lines.resize(NEAR_COUNT * 2)


func _process(delta: float) -> void:
	warp_factor = lerpf(warp_factor, 1.0, 1.5 * delta)
	var wrap_y := _origin.y + _area_size.y  # M5：回绕基线随区域锚点（zoom>1 时非 0）
	for i in _far.size():
		var p: Vector2 = _far[i] + Vector2(0.0, FAR_SPEED * warp_factor * delta)
		if p.y > wrap_y:
			p.y -= _area_size.y
		_far[i] = p
		_far_lines[i * 2] = p
		_far_lines[i * 2 + 1] = p + Vector2(LINE_LEN, 0.0)
	for i in _near.size():
		var p: Vector2 = _near[i] + Vector2(0.0, NEAR_SPEED * warp_factor * delta)
		if p.y > wrap_y:
			p.y -= _area_size.y
		_near[i] = p
		_near_lines[i * 2] = p
		_near_lines[i * 2 + 1] = p + Vector2(LINE_LEN, 0.0)
	queue_redraw()


func _draw() -> void:
	# P1-4：每层单条 draw_multiline 合批（230 条绘制指令 → 2 条）；线宽对应原圆直径
	draw_multiline(_far_lines, Color(0.7, 0.75, 0.9, 0.6), 3.0)
	draw_multiline(_near_lines, Color(1.0, 1.0, 1.0, 0.9), 5.0)
