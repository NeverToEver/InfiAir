class_name Boss
extends Area2D
## Boss：3 种轮换（1 重装 / 2 游击 / 3 母舰），血量 <30% 触发狂暴阶段。

signal health_changed(current: float, maximum: float)
signal died
signal enraged

const TEXTURES: Array[Texture2D] = [
	preload("res://assets/sprites/boss_ship_1.png"),
	preload("res://assets/sprites/boss_ship_2.png"),
	preload("res://assets/sprites/boss_ship_3.png"),
]
const BULLET_SCENE: PackedScene = preload("res://scenes/bullet.tscn")
const ENTER_SPEED := 140.0
const FIGHT_Y := 230.0
const STRAFE_MIN_X := 300.0
const STRAFE_MAX_X := 1620.0
## 各类型 HP 系数（基础 30 × 难度乘数）
const HP_MULTS: Array[float] = [1.3, 0.7, 1.6]
const ENRAGE_HP_RATIO := 0.3
const ENRAGE_RATE_MULT := 1.5
const ENRAGE_SPEED_MULT := 1.3

var boss_type: int = 1
var max_hp: float = 30.0
var hp: float = 30.0

var _in_fight: bool = false
var _enraged: bool = false
var _score_scale: float = 1.0
var _strafe_dir: float = 1.0
var _fire_timer: float = 1.6
var _fan_next: bool = true
# 游击型
var _dashing: bool = false
var _move_timer: float = 0.0
var _burst_left: int = 0
var _burst_timer: float = 0.0
# 母舰型
var _summon_timer: float = 6.0
var _cross_angle: float = 0.0

@onready var _sprite: Sprite2D = $Sprite2D


func setup(p_difficulty: float, p_type: int) -> void:
	boss_type = p_type
	max_hp = 30.0 * HP_MULTS[p_type - 1] * p_difficulty
	hp = max_hp
	# setup() 在 _ready() 之前调用，不能用 @onready 变量
	($Sprite2D as Sprite2D).texture = TEXTURES[p_type - 1]


func _ready() -> void:
	add_to_group("enemy")


func _base_fire_interval() -> float:
	match boss_type:
		1:
			return 1.6
		2:
			return 1.8
		_:
			return 0.9


func _base_modulate() -> Color:
	return Color(1.5, 0.65, 0.65) if _enraged else Color.WHITE


func _physics_process(delta: float) -> void:
	if not _in_fight:
		position.y += ENTER_SPEED * delta
		if position.y >= FIGHT_Y:
			_in_fight = true
			health_changed.emit(hp, max_hp)
		return

	match boss_type:
		1:
			_move_strafe(delta, 150.0)
		2:
			_move_dash(delta)
		3:
			_move_strafe(delta, 60.0)

	# 狂暴：射速 ×1.5（计时器流速加快）
	_fire_timer -= delta * (ENRAGE_RATE_MULT if _enraged else 1.0)
	if _fire_timer <= 0.0:
		_fire_timer = _base_fire_interval()
		match boss_type:
			1:
				if _fan_next:
					_fire_fan()
				else:
					_fire_homing()
				_fan_next = not _fan_next
			2:
				_burst_left = 3
				_burst_timer = 0.0
			3:
				_fire_cross()

	# 游击型 3 连发狙击
	if _burst_left > 0:
		_burst_timer -= delta
		if _burst_timer <= 0.0:
			_burst_timer = 0.12
			_burst_left -= 1
			_fire_sniper()

	# 母舰型召唤小怪
	if boss_type == 3:
		_summon_timer -= delta
		if _summon_timer <= 0.0:
			_summon_timer = 6.0
			_summon_minions()


func _move_strafe(delta: float, p_speed: float) -> void:
	position.x += _strafe_dir * p_speed * (ENRAGE_SPEED_MULT if _enraged else 1.0) * delta
	if position.x < STRAFE_MIN_X or position.x > STRAFE_MAX_X:
		_strafe_dir = -_strafe_dir
		position.x = clampf(position.x, STRAFE_MIN_X, STRAFE_MAX_X)


func _move_dash(delta: float) -> void:
	_move_timer -= delta
	if _move_timer <= 0.0:
		_dashing = not _dashing
		_move_timer = 0.5 if _dashing else 0.7
		if _dashing:
			# 偏向屏幕中心方向冲刺，避免长期贴边
			_strafe_dir = signf(960.0 - position.x) if randf() < 0.6 else (-_strafe_dir)
			if _strafe_dir == 0.0:
				_strafe_dir = 1.0
	if _dashing:
		position.x += _strafe_dir * 400.0 * (ENRAGE_SPEED_MULT if _enraged else 1.0) * delta
		if position.x < STRAFE_MIN_X or position.x > STRAFE_MAX_X:
			_strafe_dir = -_strafe_dir
			position.x = clampf(position.x, STRAFE_MIN_X, STRAFE_MAX_X)


func _player_dir() -> Vector2:
	var players := get_tree().get_nodes_in_group("player")
	if players.size() > 0:
		return ((players[0] as Node2D).global_position - global_position).normalized()
	return Vector2.DOWN


func _fire_fan() -> void:
	var base_dir := _player_dir()
	for i in 5:
		var dir := base_dir.rotated(deg_to_rad(20.0 * (float(i) - 2.0)))
		var b := BULLET_SCENE.instantiate()
		b.setup(dir, 380.0, 1, false)
		b.position = position + dir * 100.0
		get_parent().add_child(b)


func _fire_homing() -> void:
	var b := BULLET_SCENE.instantiate()
	b.setup(Vector2.DOWN, 300.0, 1, false, true, 1.5)
	b.position = position + Vector2(0.0, 100.0)
	get_parent().add_child(b)


func _fire_sniper() -> void:
	var dir := _player_dir()
	var b := BULLET_SCENE.instantiate()
	b.setup(dir, 650.0, 1, false)
	b.position = position + dir * 100.0
	get_parent().add_child(b)


func _fire_cross() -> void:
	for i in 4:
		var dir := Vector2.RIGHT.rotated(_cross_angle + float(i) * PI / 2.0)
		var b := BULLET_SCENE.instantiate()
		b.setup(dir, 260.0, 1, false)
		b.position = position + dir * 100.0
		get_parent().add_child(b)
	_cross_angle += deg_to_rad(15.0)


func _summon_minions() -> void:
	var spawner := get_tree().get_first_node_in_group("spawner")
	if spawner == null:
		return
	for i in randi_range(2, 3):
		spawner.spawn_minion(position + Vector2(randf_range(-80.0, 80.0), 110.0))


func take_damage(amount: int, score_scale: float = 1.0) -> void:
	hp -= float(amount)
	_score_scale = score_scale
	health_changed.emit(hp, max_hp)
	_sprite.modulate = Color(2.0, 2.0, 2.0)
	var tween := create_tween()
	# 游击型受击硬直（闪白）更短
	tween.tween_property(_sprite, "modulate", _base_modulate(), 0.05 if boss_type == 2 else 0.1)
	if hp <= 0.0:
		_die()
	elif not _enraged and hp <= max_hp * ENRAGE_HP_RATIO:
		_enrage()


func _enrage() -> void:
	_enraged = true
	_sprite.modulate = _base_modulate()
	GameState.shake(16.0)
	GameState.play_sfx(GameState.SFX_EXPLOSION_BIG, -6.0)
	enraged.emit()


func _die() -> void:
	GameState.add_boss_kill(_score_scale)
	Explosion.spawn_boss_sequence(get_parent(), global_position)
	died.emit()
	queue_free()
