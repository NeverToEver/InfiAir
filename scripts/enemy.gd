class_name Enemy
extends Area2D
## 普通/精英敌机：straight / sine / zigzag / dive / spiral / noise / hover / aggressive。
## 弹种：single（单发瞄准）/ spread（五向扇形）/ laser（细长高亮快速弹）。
## 出生 15s 寿命到期后向上或侧方加速离场（不给分、不计击杀）。
## 数值由 spawner 的机型配置表驱动（setup 传入 config Dictionary）。

signal died(enemy: Enemy)

var ENEMY_BULLET_SPEED := 420.0
var SPREAD_BULLET_SPEED := 340.0  # 扇形弹稍慢
var LASER_BULLET_SPEED := 720.0  # laser 简化表现：细长高亮快速弹，伤害同普通弹
var SPREAD_FAN_STEP := 0.314159  # 五向扇形步进角（18°）
var LIFETIME := 15.0  # 出生后寿命（对齐原作 900 帧@60fps）
var EXIT_ACCEL := 520.0  # 寿命离场加速度
var AGGR_CHASE_SPEED := 170.0  # aggressive 持续偏向玩家 x 的速度
var FIRE_INTERVAL := 2.2
var HOVER_Y := 320.0
var SPIRAL_RADIUS := 50.0

var strategy: StringName = &"straight"
var is_elite: bool = false
var hp: int = 2
var speed: float = 140.0
var can_shoot: bool = false
var score_value: int = 100
var fire_interval: float = FIRE_INTERVAL
var bullet_type: StringName = &"single"

var _time: float = 0.0
var _spawn_x: float = 0.0
var _zig_dir: float = 1.0
var _zig_timer: float = 0.7
var _dive_target: Vector2 = Vector2.ZERO
var _dive_timer: float = 0.0
var _fire_timer: float = FIRE_INTERVAL
var _center: Vector2 = Vector2.ZERO  # spiral 绕转中心
var _pool: Node = null

# 三角函数查表（2048 项循环表 + 线性插值，全敌机共享一份）
const TRIG_SIZE := 2048
static var _sin_table: PackedFloat32Array = []


static func sin_fast(x: float) -> float:
	if _sin_table.is_empty():
		_sin_table.resize(TRIG_SIZE + 1)
		for i in TRIG_SIZE + 1:
			_sin_table[i] = sin(TAU * float(i) / float(TRIG_SIZE))
	var t := fposmod(x, TAU) / TAU * TRIG_SIZE
	var i := int(t)
	return lerpf(_sin_table[i], _sin_table[i + 1], t - i)


static func cos_fast(x: float) -> float:
	return sin_fast(x + PI / 2.0)
var _hovering: bool = false
var _hover_done: bool = false
var _hover_timer: float = 0.0
var _life_timer: float = 0.0
var _exiting: bool = false
var _exit_dir: Vector2 = Vector2.UP
var _exit_speed: float = 0.0

@onready var _sprite: Sprite2D = $Sprite2D
@onready var _shape: CollisionShape2D = $CollisionShape2D


## config 字段：texture, hp(Vector2i), speed(Vector2), score, fire(开火概率),
## fire_interval, scale, radius, bullet_types(弹种池), elite(可选)。
## p_bullet_type 为空时从弹种池随机抽取（spawner 传入已做同屏上限控制的结果）。
## HP 与速度按难度档位缩放（easy ×0.75/×0.85，medium ×1，hard ×1.5/×1.2）。
func setup(
	config: Dictionary,
	p_strategy: StringName,
	p_difficulty: float,
	p_bullet_type: StringName = &""
) -> void:
	strategy = p_strategy
	is_elite = config.get("elite", false)
	hp = maxi(
		1,
		int(roundf(randf_range(config["hp"].x, config["hp"].y) * GameState.enemy_hp_multiplier()))
	)
	score_value = config["score"]
	can_shoot = randf() < config["fire"]
	fire_interval = config.get("fire_interval", FIRE_INTERVAL)
	var pool: Array = config.get("bullet_types", [&"single"])
	bullet_type = p_bullet_type if p_bullet_type != &"" else pool[randi() % pool.size()]
	speed = (
		randf_range(config["speed"].x, config["speed"].y)
		* (1.0 + 0.1 * (p_difficulty - 1.0))
		* GameState.enemy_speed_multiplier()
	)
	# setup() 在 _ready() 之前调用，不能用 @onready 变量
	var sprite: Sprite2D = $Sprite2D
	var shape_node: CollisionShape2D = $CollisionShape2D
	sprite.texture = config["texture"]
	var sc: float = config.get("scale", 0.45)
	sprite.scale = Vector2(sc, sc)
	(shape_node.shape as CircleShape2D).radius = config.get("radius", 30.0)


func _ready() -> void:
	add_to_group("enemy")
	GameState.register_enemy(self)
	# 数值配置缓存（启动一次读入）
	ENEMY_BULLET_SPEED = GameState.cfg("enemies.bullet_speed", ENEMY_BULLET_SPEED)
	SPREAD_BULLET_SPEED = GameState.cfg("enemies.spread_bullet_speed", SPREAD_BULLET_SPEED)
	LASER_BULLET_SPEED = GameState.cfg("enemies.laser_bullet_speed", LASER_BULLET_SPEED)
	SPREAD_FAN_STEP = GameState.cfg("enemies.spread_fan_step", SPREAD_FAN_STEP)
	LIFETIME = GameState.cfg("enemies.lifetime", LIFETIME)
	EXIT_ACCEL = GameState.cfg("enemies.exit_accel", EXIT_ACCEL)
	AGGR_CHASE_SPEED = GameState.cfg("enemies.aggressive_chase_speed", AGGR_CHASE_SPEED)
	FIRE_INTERVAL = GameState.cfg("enemies.fire_interval", FIRE_INTERVAL)
	HOVER_Y = GameState.cfg("enemies.hover_y", HOVER_Y)
	SPIRAL_RADIUS = GameState.cfg("enemies.spiral_radius", SPIRAL_RADIUS)
	# 每个实例独立形状，避免共享 sub_resource 半径互相影响
	_shape.shape = _shape.shape.duplicate()
	_spawn_x = position.x
	_center = position
	_fire_timer = randf_range(1.0, fire_interval)
	if strategy == &"dive":
		_dive_timer = 1.2
		if GameState.player_ref != null:
			_dive_target = GameState.player_ref.global_position
		else:
			_dive_target = Vector2(position.x, 1200.0)
	elif strategy == &"hover":
		_hover_timer = randf_range(3.0, 5.0)
	area_entered.connect(_on_area_entered)


## 池化复用：全状态重置（spawner 经 EnemyPool 调用；直接实例化走 _ready 初始化）
func reactivate(config: Dictionary, p_strategy: StringName, p_difficulty: float) -> void:
	_time = 0.0
	_zig_dir = 1.0
	_zig_timer = 0.7
	_dive_target = Vector2.ZERO
	_dive_timer = 0.0
	_hovering = false
	_hover_done = false
	_hover_timer = 0.0
	_exiting = false
	_life_timer = 0.0
	_exit_speed = 0.0
	_score_scale = 1.0
	visible = true
	monitoring = true
	set_physics_process(true)
	_sprite.modulate = Color.WHITE
	GameState.register_enemy(self)
	setup(config, p_strategy, p_difficulty)
	_spawn_x = position.x
	_center = position
	_fire_timer = randf_range(1.0, fire_interval)
	if strategy == &"dive":
		_dive_timer = 1.2
		if GameState.player_ref != null:
			_dive_target = GameState.player_ref.global_position
		else:
			_dive_target = Vector2(position.x, 1200.0)
	elif strategy == &"hover":
		_hover_timer = randf_range(3.0, 5.0)


## 池化回收：停用但保留实例
func deactivate() -> void:
	visible = false
	set_deferred("monitoring", false)
	set_physics_process(false)
	GameState.unregister_enemy(self)
	for c in died.get_connections():
		died.disconnect(c["callable"])
	position = Vector2(-500.0, -500.0)


func _despawn() -> void:
	if _pool != null and is_instance_valid(_pool):
		_pool.release(self)
	else:
		queue_free()


func _exit_tree() -> void:
	GameState.unregister_enemy(self)
	if _pool != null:
		_pool.forget(self)


func _physics_process(delta: float) -> void:
	_time += delta
	if _exiting:
		# 寿命离场：向上或侧方加速，离场不给分、不计击杀
		_exit_speed += EXIT_ACCEL * delta
		position += _exit_dir * _exit_speed * delta
		if position.y < -150.0 or position.x < -150.0 or position.x > 2070.0:
			_despawn()
		return
	_life_timer += delta
	if _life_timer >= LIFETIME:
		_begin_lifetime_exit()
		return
	match strategy:
		&"straight":
			position.y += speed * delta
		&"sine":
			position.y += speed * delta
			position.x = _spawn_x + sin_fast(_time * 3.0) * 90.0
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
			position = _center + Vector2(cos_fast(_time * 4.0), sin_fast(_time * 4.0)) * SPIRAL_RADIUS
		&"noise":
			# 正弦叠加伪噪声驱动横向飘移
			var vx := (
				(sin_fast(_time * 1.7) + sin_fast(_time * 2.9 + 1.3) + sin_fast(_time * 4.3 + 2.1))
				/ 3.0 * speed * 1.2
			)
			position += Vector2(vx, speed) * delta
			position.x = clampf(position.x, 40.0, 1880.0)
		&"aggressive":
			# 追踪性噪声漂移：正弦叠加伪噪声扰动 + 持续偏向玩家 x 的下行
			var vx := (
				(sin_fast(_time * 2.1) + sin_fast(_time * 3.4 + 1.7) + sin_fast(_time * 5.3 + 0.6))
				/ 3.0 * speed * 1.1
			)
			var players := GameState.player_ref
			if players != null:
				var dx: float = players.global_position.x - position.x
				vx += clampf(dx, -1.0, 1.0) * AGGR_CHASE_SPEED
			position += Vector2(vx, speed * 0.9) * delta
			position.x = clampf(position.x, 40.0, 1880.0)
		&"hover":
			if _hover_done:
				position.y += speed * delta
			elif _hovering:
				_hover_timer -= delta
				position.y = HOVER_Y + sin_fast(_time * 2.0) * 6.0  # 停驻轻微浮动
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
		_despawn()


func _fire_at_player() -> void:
	if GameState.player_ref == null:
		return
	var base_dir := (GameState.player_ref.global_position - global_position).normalized()
	match bullet_type:
		&"spread":
			# 五向扇形弹：以瞄准方向为中心 ±2 步展开
			for i in 5:
				_spawn_enemy_bullet(
					base_dir.rotated(SPREAD_FAN_STEP * float(i - 2)), SPREAD_BULLET_SPEED, &"spread"
				)
		&"laser":
			_spawn_enemy_bullet(base_dir, LASER_BULLET_SPEED, &"laser")
		_:
			_spawn_enemy_bullet(base_dir, ENEMY_BULLET_SPEED, &"single")


func _spawn_enemy_bullet(dir: Vector2, bullet_speed: float, p_type: StringName) -> void:
	var b: Bullet = GameState.bullet_pool.fire(dir, bullet_speed, 1, false)
	b.position = position
	b.set_meta("bullet_type", p_type)
	if p_type == &"laser":
		# 细长高亮快速弹（polygon 尖端朝 +x，即飞行方向）
		var poly := b.get_node("Polygon2D") as Polygon2D
		poly.scale = Vector2(2.2, 0.55)
		poly.color = Color(1.0, 0.85, 0.35)


## 寿命到期：向上或侧方加速离场（停火，不给分、不计击杀）。
func _begin_lifetime_exit() -> void:
	_exiting = true
	can_shoot = false
	if randf() < 0.5:
		_exit_dir = Vector2(randf_range(-0.6, 0.6), -1.0).normalized()  # 向上
	else:
		# 就近侧方（略带上行），从较近的一侧离场
		_exit_dir = Vector2(1.0 if position.x < 960.0 else -1.0, randf_range(-0.4, 0.0)).normalized()
	_exit_speed = speed


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
	if lifesteal > 0 and randf() < GameState.cfg("buffs.lifesteal.base_chance", 0.1) + GameState.cfg("buffs.lifesteal.per_stack", 0.05) * (lifesteal - 1):
		GameState.heal(0.5)
	GameState.play_sfx(GameState.SFX_EXPLOSION_BIG if is_elite else GameState.SFX_EXPLOSION)
	GameState.shake(GameState.cfg("effects.shake.elite_die", 9.0) if is_elite else GameState.cfg("effects.shake.enemy_die", 5.0))
	Explosion.spawn_at(get_parent(), global_position, 1.5 if is_elite else 1.0)
	died.emit(self)
	_despawn()


func _on_area_entered(area: Area2D) -> void:
	# 撞击玩家：自毁且不加分
	if area.is_in_group("player_hitbox"):
		area.get_parent().take_damage()
		GameState.play_sfx(GameState.SFX_EXPLOSION)
		GameState.shake(GameState.cfg("effects.shake.ram", 4.0))
		Explosion.spawn_at(get_parent(), global_position, 1.0)
		died.emit(self)
		queue_free()
