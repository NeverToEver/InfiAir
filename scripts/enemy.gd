class_name Enemy
extends Area2D
## 普通/精英敌机：straight / sine / zigzag / dive / spiral / noise / hover。
## 数值由 spawner 的机型配置表驱动（setup 传入 config Dictionary）。

signal died(enemy: Enemy)

const BULLET_SCENE: PackedScene = preload("res://scenes/bullet.tscn")
const ENEMY_BULLET_SPEED := 420.0
const FIRE_INTERVAL := 2.2
const HOVER_Y := 320.0
const SPIRAL_RADIUS := 50.0

var strategy: StringName = &"straight"
var is_elite: bool = false
var hp: int = 2
var speed: float = 140.0
var can_shoot: bool = false
var score_value: int = 100
var fire_interval: float = FIRE_INTERVAL

var _time: float = 0.0
var _spawn_x: float = 0.0
var _zig_dir: float = 1.0
var _zig_timer: float = 0.7
var _dive_target: Vector2 = Vector2.ZERO
var _dive_timer: float = 0.0
var _fire_timer: float = FIRE_INTERVAL
var _center: Vector2 = Vector2.ZERO  # spiral 绕转中心
var _hovering: bool = false
var _hover_done: bool = false
var _hover_timer: float = 0.0

@onready var _sprite: Sprite2D = $Sprite2D
@onready var _shape: CollisionShape2D = $CollisionShape2D


## config 字段：texture, hp(Vector2i), speed(Vector2), score, fire(开火概率),
## fire_interval, scale, radius, elite(可选)
func setup(config: Dictionary, p_strategy: StringName, p_difficulty: float) -> void:
	strategy = p_strategy
	is_elite = config.get("elite", false)
	hp = randi_range(config["hp"].x, config["hp"].y)
	score_value = config["score"]
	can_shoot = randf() < config["fire"]
	fire_interval = config.get("fire_interval", FIRE_INTERVAL)
	speed = randf_range(config["speed"].x, config["speed"].y) * (1.0 + 0.1 * (p_difficulty - 1.0))
	# setup() 在 _ready() 之前调用，不能用 @onready 变量
	var sprite: Sprite2D = $Sprite2D
	var shape_node: CollisionShape2D = $CollisionShape2D
	sprite.texture = config["texture"]
	var sc: float = config.get("scale", 0.45)
	sprite.scale = Vector2(sc, sc)
	(shape_node.shape as CircleShape2D).radius = config.get("radius", 30.0)


func _ready() -> void:
	add_to_group("enemy")
	# 每个实例独立形状，避免共享 sub_resource 半径互相影响
	_shape.shape = _shape.shape.duplicate()
	_spawn_x = position.x
	_center = position
	_fire_timer = randf_range(1.0, fire_interval)
	if strategy == &"dive":
		_dive_timer = 1.2
		var players := get_tree().get_nodes_in_group("player")
		if players.size() > 0:
			_dive_target = (players[0] as Node2D).global_position
		else:
			_dive_target = Vector2(position.x, 1200.0)
	elif strategy == &"hover":
		_hover_timer = randf_range(3.0, 5.0)
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
		&"spiral":
			# 绕转中心匀速下压，机身绕中心小半径转圈
			_center.y += speed * delta
			position = _center + Vector2(cos(_time * 4.0), sin(_time * 4.0)) * SPIRAL_RADIUS
		&"noise":
			# 正弦叠加伪噪声驱动横向飘移
			var vx := (
				(sin(_time * 1.7) + sin(_time * 2.9 + 1.3) + sin(_time * 4.3 + 2.1))
				/ 3.0 * speed * 1.2
			)
			position += Vector2(vx, speed) * delta
			position.x = clampf(position.x, 40.0, 1880.0)
		&"hover":
			if _hover_done:
				position.y += speed * delta
			elif _hovering:
				_hover_timer -= delta
				position.y = HOVER_Y + sin(_time * 2.0) * 6.0  # 停驻轻微浮动
				if _hover_timer <= 0.0:
					_hover_done = true
			else:
				position.y += speed * delta
				if position.y >= HOVER_Y:
					_hovering = true

	if can_shoot:
		_fire_timer -= delta
		if _fire_timer <= 0.0:
			# 悬停期间更高频率点射
			_fire_timer = fire_interval * (0.5 if _hovering else 1.0)
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


var _score_scale: float = 1.0


func take_damage(amount: int, score_scale: float = 1.0) -> void:
	hp -= amount
	_score_scale = score_scale
	_sprite.modulate = Color(2.0, 2.0, 2.0)  # 受击闪白
	var tween := create_tween()
	tween.tween_property(_sprite, "modulate", Color.WHITE, 0.1)
	if hp <= 0:
		die()


func die() -> void:
	# 母舰弹丸击毁只给 1/3 分（向下取整）
	GameState.add_score(int(score_value * _score_scale))
	GameState.add_kill()
	# 吸血 buff：击毁 10% 概率回 0.5 命，每层 +5%
	var lifesteal := GameState.buff_count(&"lifesteal")
	if lifesteal > 0 and randf() < 0.10 + 0.05 * (lifesteal - 1):
		GameState.heal(0.5)
	GameState.play_sfx(GameState.SFX_EXPLOSION_BIG if is_elite else GameState.SFX_EXPLOSION)
	GameState.shake(9.0 if is_elite else 5.0)
	Explosion.spawn_at(get_parent(), global_position, 1.5 if is_elite else 1.0)
	died.emit(self)
	queue_free()


func _on_area_entered(area: Area2D) -> void:
	# 撞击玩家：自毁且不加分
	if area.is_in_group("player_hitbox"):
		area.get_parent().take_damage()
		GameState.play_sfx(GameState.SFX_EXPLOSION)
		GameState.shake(4.0)
		Explosion.spawn_at(get_parent(), global_position, 1.0)
		died.emit(self)
		queue_free()
