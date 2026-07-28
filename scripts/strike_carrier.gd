class_name StrikeCarrier
extends Node2D
## 精英炮塔事件·打击航母（docs/ELITE_TURRET_EVENT.md 第 2 节）：
## 背景式巨型单位（不可被攻击，无碰撞层），自屏幕上方深空降入悬停，
## 作为炮台展开的舞台；事件结束按胜负两种姿态撤离（受创慢速 / 完整加速）。
## 基座环即状态灯：待命暗红 → 升起充能品红高亮 → 炮台被毁对应环熄灭。

signal entered
signal exited

const TEXTURE: Texture2D = preload("res://assets/sprites/strike_carrier.png")

## 基座相对偏移（与生成器 TURRET_WELLS 对齐：贴图坐标 - (600, 350)）
const SOCKETS: Array[Vector2] = [
	Vector2(-170.0, 120.0),  # 左翼台内
	Vector2(170.0, 120.0),   # 右翼台内
	Vector2(-310.0, 80.0),   # 左翼台外
	Vector2(310.0, 80.0),    # 右翼台外
	Vector2(0.0, 170.0),     # 中央前甲板
]

enum State { ENTER, HOVER, RETREAT }

## 撤退参数（复用 Boss escape 参数族量级）
var RETREAT_START_SPEED := 120.0
var RETREAT_ACCEL := 420.0

var _state: State = State.ENTER
var _enter_t: float = 0.0
var _enter_duration: float = 2.0
var _start_y: float = 0.0
var _hover_y: float = 300.0
var _retreat_speed: float = 0.0
var _retreat_factor: float = 1.0  # 受创撤离放慢
var _hover_time: float = 0.0
var _rings: Array[Line2D] = []
var _sprite: Sprite2D


func _init() -> void:
	_sprite = Sprite2D.new()
	_sprite.texture = TEXTURE
	add_child(_sprite)


func _ready() -> void:
	RETREAT_START_SPEED = GameState.cfg("elite_turret_event.carrier.retreat_start_speed", RETREAT_START_SPEED)
	RETREAT_ACCEL = GameState.cfg("elite_turret_event.carrier.retreat_accel", RETREAT_ACCEL)
	# 深空淡入
	modulate.a = 0.0
	_build_rings()


## 八角基座环（状态灯；默认隐藏，事件按启用基座逐个点亮）
func _build_rings() -> void:
	for socket in SOCKETS:
		var ring := Line2D.new()
		var pts := PackedVector2Array()
		for i in 9:
			var a := PI / 8.0 + float(i) * PI / 4.0
			pts.append(socket + Vector2(cos(a), sin(a)) * 40.0)
		ring.points = pts
		ring.width = 3.0
		ring.default_color = Color(0.47, 0.12, 0.24, 0.9)  # 待命暗红
		ring.visible = false
		add_child(ring)
		_rings.append(ring)


## 降入悬停：自屏幕上方深空下压到 hover_y（enter_duration 秒，缓出 + 淡入）
func enter(hover_y: float, duration: float) -> void:
	_hover_y = hover_y
	_enter_duration = duration
	_start_y = position.y
	_state = State.ENTER
	_enter_t = 0.0


## 启用基座环（升起充能品红高亮）
func set_socket_charging(index: int) -> void:
	if index >= 0 and index < _rings.size():
		_rings[index].visible = true
		_rings[index].default_color = Color(1.0, 0.25, 0.75, 1.0)  # 精英品红


## 炮台被毁：对应环熄灭
func set_socket_destroyed(index: int) -> void:
	if index >= 0 and index < _rings.size():
		_rings[index].visible = false


## 撤离：victorious=true 完整加速上升淡出；false 受创慢速（冒烟 + 变暗）
func retreat(victorious: bool) -> void:
	_state = State.RETREAT
	_retreat_speed = RETREAT_START_SPEED
	_retreat_factor = 1.0 if victorious else 0.55
	if not victorious:
		_sprite.modulate = Color(0.7, 0.6, 0.65)
		# 受创冒烟：甲板几处爆点
		for i in 3:
			Explosion.spawn_at(get_parent(), global_position + SOCKETS[i] + Vector2(randf_range(-30.0, 30.0), 0.0), 0.8)
	var tween := create_tween()
	tween.tween_property(self, "modulate:a", 0.0, 2.2)


func _physics_process(delta: float) -> void:
	match _state:
		State.ENTER:
			_enter_t += delta
			var t := clampf(_enter_t / _enter_duration, 0.0, 1.0)
			var eased := 1.0 - pow(1.0 - t, 3.0)
			position.y = lerpf(_start_y, _hover_y, eased)
			modulate.a = maxf(modulate.a, t)
			if t >= 1.0:
				position.y = _hover_y
				modulate.a = 1.0
				_state = State.HOVER
				entered.emit()
		State.HOVER:
			# 悬停轻微浮动（质量感：慢速小幅）
			_hover_time += delta
			position.y = _hover_y + sin(_hover_time * 0.8) * 6.0
		State.RETREAT:
			_retreat_speed += RETREAT_ACCEL * _retreat_factor * delta
			position.y -= _retreat_speed * delta
			if position.y < GameState.view_world_rect().position.y - 500.0:
				exited.emit()
				queue_free()
