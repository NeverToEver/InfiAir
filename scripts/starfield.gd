class_name Starfield
extends Node2D
## 程序化双层视差滚动星空背景。

var FAR_COUNT := 140
var NEAR_COUNT := 90
var FAR_SPEED := 60.0
var NEAR_SPEED := 140.0

var _far: Array[Vector2] = []
var _near: Array[Vector2] = []
## 返航过场的星光拉伸倍率，随时间衰减回 1
var warp_factor: float = 1.0
## C07 修复：可见世界区域尺寸缓存（view_world_rect），替代硬编码 1920×1080
var _area_size: Vector2 = Vector2(1920.0, 1080.0)


func warp(factor: float) -> void:
	warp_factor = factor


func _ready() -> void:
	z_index = -10
	FAR_COUNT = GameState.cfg("effects.starfield.far_count", FAR_COUNT)
	NEAR_COUNT = GameState.cfg("effects.starfield.near_count", NEAR_COUNT)
	FAR_SPEED = GameState.cfg("effects.starfield.far_speed", FAR_SPEED)
	NEAR_SPEED = GameState.cfg("effects.starfield.near_speed", NEAR_SPEED)
	var rng := RandomNumberGenerator.new()
	rng.seed = 12345
	# C07：星点范围随可见世界区域（view_world_rect）而非写死 1920×1080
	_area_size = GameState.view_world_rect().size
	for i in FAR_COUNT:
		_far.append(Vector2(rng.randf() * _area_size.x, rng.randf() * _area_size.y))
	for i in NEAR_COUNT:
		_near.append(Vector2(rng.randf() * _area_size.x, rng.randf() * _area_size.y))


func _process(delta: float) -> void:
	warp_factor = lerpf(warp_factor, 1.0, 1.5 * delta)
	for i in _far.size():
		_far[i] += Vector2(0.0, FAR_SPEED * warp_factor * delta)
		if _far[i].y > _area_size.y:
			_far[i].y -= _area_size.y
	for i in _near.size():
		_near[i] += Vector2(0.0, NEAR_SPEED * warp_factor * delta)
		if _near[i].y > _area_size.y:
			_near[i].y -= _area_size.y
	queue_redraw()


func _draw() -> void:
	for s in _far:
		draw_circle(s, 1.5, Color(0.7, 0.75, 0.9, 0.6))
	for s in _near:
		draw_circle(s, 2.5, Color(1.0, 1.0, 1.0, 0.9))
