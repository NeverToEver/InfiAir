class_name Enemy
extends Area2D
## 普通/精英敌机：straight / sine / zigzag / dive 四种移动策略。

signal died(enemy: Enemy)

const BULLET_SCENE: PackedScene = preload("res://scenes/bullet.tscn")
const ENEMY_BULLET_SPEED := 420.0
const FIRE_INTERVAL := 2.2

var strategy: StringName = &"straight"
var is_elite: bool = false
var hp: int = 2
var speed: float = 140.0
var can_shoot: bool = false
var score_value: int = 100

var _time: float = 0.0
var _spawn_x: float = 0.0
var _zig_dir: float = 1.0
var _zig_timer: float = 0.7
var _dive_target: Vector2 = Vector2.ZERO
var _dive_timer: float = 0.0
var _fire_timer: float = FIRE_INTERVAL

@onready var _sprite: Sprite2D = $Sprite2D
@onready var _shape: CollisionShape2D = $CollisionShape2D


func setup(
	p_texture: Texture2D,
	p_strategy: StringName,
	p_elite: bool,
	p_difficulty: float,
	p_can_shoot: bool
) -> void:
	strategy = p_strategy
	is_elite = p_elite
	can_shoot = p_can_shoot or p_elite
	var hp_range := Vector2i(8, 10) if p_elite else Vector2i(2, 3)
	hp = randi_range(hp_range.x, hp_range.y)
	score_value = 300 if p_elite else 100
	speed = randf_range(130.0, 180.0) * (1.0 + 0.1 * (p_difficulty - 1.0))
	# setup() 在 _ready() 之前调用，不能用 @onready 变量
	var sprite: Sprite2D = $Sprite2D
	var shape_node: CollisionShape2D = $CollisionShape2D
	sprite.texture = p_texture
	if p_elite:
		sprite.scale = Vector2(0.6, 0.6)
		(shape_node.shape as CircleShape2D).radius = 48.0
	else:
		sprite.scale = Vector2(0.45, 0.45)
		(shape_node.shape as CircleShape2D).radius = 30.0


func _ready() -> void:
	add_to_group("enemy")
	# 每个实例独立形状，避免共享 sub_resource 半径互相影响
	_shape.shape = _shape.shape.duplicate()
	_spawn_x = position.x
	_fire_timer = randf_range(1.0, FIRE_INTERVAL)
	if strategy == &"dive":
		_dive_timer = 1.2
		var players := get_tree().get_nodes_in_group("player")
		if players.size() > 0:
			_dive_target = (players[0] as Node2D).global_position
		else:
			_dive_target = Vector2(position.x, 1200.0)
	area_entered.connect(_on_area_entered)


func _physics_process(delta: float) -> void:
	_time += delta
	match strategy:
		&"straight":
			position.y += speed * delta
		&"sine":
			position.y += speed * delta
			position.x = _spawn_x + sin(_time * 3.0) * 90.0
		&"zigzag":
			_zig_timer -= delta
			if _zig_timer <= 0.0:
				_zig_dir = -_zig_dir
				_zig_timer = 0.7
			position += Vector2(_zig_dir * speed * 0.9, speed) * delta
			if position.x < 40.0 or position.x > 1880.0:
				_zig_dir = -_zig_dir
				position.x = clampf(position.x, 40.0, 1880.0)
		&"dive":
			if _dive_timer > 0.0:
				_dive_timer -= delta
				var dir := (_dive_target - position).normalized()
				position += dir * speed * 1.7 * delta
			else:
				position.y += speed * delta

	if can_shoot:
		_fire_timer -= delta
		if _fire_timer <= 0.0:
			_fire_timer = FIRE_INTERVAL
			_fire_at_player()

	if position.y > 1140.0:
		queue_free()


func _fire_at_player() -> void:
	var players := get_tree().get_nodes_in_group("player")
	if players.size() == 0:
		return
	var dir := ((players[0] as Node2D).global_position - global_position).normalized()
	var b := BULLET_SCENE.instantiate()
	b.setup(dir, ENEMY_BULLET_SPEED, 1, false)
	b.position = position
	get_parent().add_child(b)


func take_damage(amount: int) -> void:
	hp -= amount
	_sprite.modulate = Color(2.0, 2.0, 2.0)  # 受击闪白
	var tween := create_tween()
	tween.tween_property(_sprite, "modulate", Color.WHITE, 0.1)
	if hp <= 0:
		die()


func die() -> void:
	GameState.add_score(score_value)
	GameState.add_kill()
	Explosion.spawn_at(get_parent(), global_position, 1.5 if is_elite else 1.0)
	died.emit(self)
	queue_free()


func _on_area_entered(area: Area2D) -> void:
	# 撞击玩家：自毁且不加分
	if area.is_in_group("player_hitbox"):
		area.get_parent().take_damage()
		Explosion.spawn_at(get_parent(), global_position, 1.0)
		died.emit(self)
		queue_free()
