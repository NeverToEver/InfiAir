class_name Starfield
extends Node2D
## 程序化双层视差滚动星空背景。

const FAR_COUNT := 140
const NEAR_COUNT := 90
const FAR_SPEED := 60.0
const NEAR_SPEED := 140.0

var _far: Array[Vector2] = []
var _near: Array[Vector2] = []


func _ready() -> void:
	z_index = -10
	var rng := RandomNumberGenerator.new()
	rng.seed = 12345
	for i in FAR_COUNT:
		_far.append(Vector2(rng.randf() * 1920.0, rng.randf() * 1080.0))
	for i in NEAR_COUNT:
		_near.append(Vector2(rng.randf() * 1920.0, rng.randf() * 1080.0))


func _process(delta: float) -> void:
	for i in _far.size():
		_far[i] += Vector2(0.0, FAR_SPEED * delta)
		if _far[i].y > 1080.0:
			_far[i].y -= 1080.0
	for i in _near.size():
		_near[i] += Vector2(0.0, NEAR_SPEED * delta)
		if _near[i].y > 1080.0:
			_near[i].y -= 1080.0
	queue_redraw()


func _draw() -> void:
	for s in _far:
		draw_circle(s, 1.5, Color(0.7, 0.75, 0.9, 0.6))
	for s in _near:
		draw_circle(s, 2.5, Color(1.0, 1.0, 1.0, 0.9))
