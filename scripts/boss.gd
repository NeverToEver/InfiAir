class_name Boss
extends Area2D
## Boss：从顶部降入后在屏幕上部 1/3 区域巡航，扇形 5 发与追踪单发交替。

signal health_changed(current: float, maximum: float)
signal died

const TEXTURES: Array[Texture2D] = [
	preload("res://assets/sprites/boss_ship_1.png"),
	preload("res://assets/sprites/boss_ship_2.png"),
	preload("res://assets/sprites/boss_ship_3.png"),
]
const BULLET_SCENE: PackedScene = preload("res://scenes/bullet.tscn")
const ENTER_SPEED := 140.0
const STRAFE_SPEED := 150.0
const FIGHT_Y := 230.0
const STRAFE_MIN_X := 300.0
const STRAFE_MAX_X := 1620.0
const FIRE_INTERVAL := 1.6

var max_hp: float = 30.0
var hp: float = 30.0

var _in_fight: bool = false
var _strafe_dir: float = 1.0
var _fire_timer: float = FIRE_INTERVAL
var _fan_next: bool = true

@onready var _sprite: Sprite2D = $Sprite2D


func setup(p_difficulty: float) -> void:
	max_hp = 30.0 * p_difficulty
	hp = max_hp
	# setup() 在 _ready() 之前调用，不能用 @onready 变量
	($Sprite2D as Sprite2D).texture = TEXTURES[randi() % TEXTURES.size()]


func _ready() -> void:
	add_to_group("enemy")


func _physics_process(delta: float) -> void:
	if not _in_fight:
		position.y += ENTER_SPEED * delta
		if position.y >= FIGHT_Y:
			_in_fight = true
			health_changed.emit(hp, max_hp)
		return

	position.x += _strafe_dir * STRAFE_SPEED * delta
	if position.x < STRAFE_MIN_X or position.x > STRAFE_MAX_X:
		_strafe_dir = -_strafe_dir
		position.x = clampf(position.x, STRAFE_MIN_X, STRAFE_MAX_X)

	_fire_timer -= delta
	if _fire_timer <= 0.0:
		_fire_timer = FIRE_INTERVAL
		if _fan_next:
			_fire_fan()
		else:
			_fire_homing()
		_fan_next = not _fan_next


func _fire_fan() -> void:
	var base_dir := Vector2.DOWN
	var players := get_tree().get_nodes_in_group("player")
	if players.size() > 0:
		base_dir = ((players[0] as Node2D).global_position - global_position).normalized()
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


func take_damage(amount: int) -> void:
	hp -= float(amount)
	health_changed.emit(hp, max_hp)
	_sprite.modulate = Color(2.0, 2.0, 2.0)
	var tween := create_tween()
	tween.tween_property(_sprite, "modulate", Color.WHITE, 0.1)
	if hp <= 0.0:
		_die()


func _die() -> void:
	GameState.add_boss_kill()
	Explosion.spawn_boss_sequence(get_parent(), global_position)
	died.emit()
	queue_free()
