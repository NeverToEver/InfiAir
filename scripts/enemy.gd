class_name Enemy
extends Area2D
## 普通/精英敌机：straight / sine / zigzag / dive / spiral / noise / hover / aggressive。
## 弹种：single（单发瞄准）/ spread（五向扇形）/ laser（细长高亮快速弹）。
## 出生 15s 寿命到期后向上或侧方加速离场（不给分、不计击杀）。
## 数值由 spawner 的机型配置表驱动（setup 传入 config Dictionary）。

signal died(enemy: Enemy)

var ENEMY_BULLET_SPEED := 420.0
var SPREAD_BULLET_SPEED := 340.0  # 扇形弹稍慢
var LASER_BULLET_SPEED := 720.0  # laser 简化表现：细长高亮快速弹
## 各弹种伤害（对齐原作 _ENEMY_BULLET_DAMAGE：single 12 / spread 10 / laser 20）
var BULLET_DAMAGE_SINGLE := 12
var BULLET_DAMAGE_SPREAD := 10
var BULLET_DAMAGE_LASER := 20
## 身体撞击伤害（对齐原作 ENEMY_COLLISION_DAMAGE=20；撞击后敌机不自毁继续飞）
var COLLISION_DAMAGE := 20
## 慢速力场：全局敌机移速 ×0.8（对齐原作 slow_factor；原作对普通敌机失效为疑似 bug，本版全生效）
var SLOW_FIELD_FACTOR := 0.8
var SPREAD_FAN_STEP := 0.314159  # 五向扇形步进角（18°）
var LIFETIME := 15.0  # 出生后寿命（对齐原作 900 帧@60fps）
var EXIT_ACCEL := 520.0  # 寿命离场加速度
var AGGR_CHASE_SPEED := 170.0  # aggressive 持续偏向玩家 x 的速度
var FIRE_INTERVAL := 2.2
var HOVER_Y := 320.0
var SPIRAL_RADIUS := 50.0
## 敌机 HP 对局进程 ramp 系数：HP ×(1 + 系数×(Boss 击杀难度乘数-1))，对齐同类游戏的敌 HP 线性成长惯例
var HP_RAMP_FACTOR := 0.12

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
## 池活跃标记：回收的延迟调用（monitoring=false / reparent）在重激活后必须失效
var _active: bool = false
## 回收 reparent 保护：4.6 实测 reparent 也会触发 _exit_tree，置位期间禁止 forget 误清池清单
var _repooling: bool = false

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
## HP 按难度档位缩放（easy ×0.75，medium ×1，hard ×1.5），并随对局进程 ramp：
## ×(1 + hp_ramp_factor ×(Boss 击杀难度乘数-1))；速度按难度档 ×0.85/×1/×1.2 与同一 ramp ×0.1 系数成长。
func setup(
	config: Dictionary,
	p_strategy: StringName,
	p_difficulty: float,
	p_bullet_type: StringName = &""
) -> void:
	strategy = p_strategy
	is_elite = config.get("elite", false)
	# HP 三级乘算：机型区间 × 难度档 × 对局进程 ramp（随 Boss 击杀线性成长）
	hp = maxi(
		1,
		int(roundf(
			randf_range(config["hp"].x, config["hp"].y)
			* GameState.enemy_hp_multiplier()
			* (1.0 + GameState.cfg("enemies.hp_ramp_factor", HP_RAMP_FACTOR) * (p_difficulty - 1.0))
		))
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
	BULLET_DAMAGE_SINGLE = GameState.cfg("enemies.bullet_damage.single", BULLET_DAMAGE_SINGLE)
	BULLET_DAMAGE_SPREAD = GameState.cfg("enemies.bullet_damage.spread", BULLET_DAMAGE_SPREAD)
	BULLET_DAMAGE_LASER = GameState.cfg("enemies.bullet_damage.laser", BULLET_DAMAGE_LASER)
	COLLISION_DAMAGE = GameState.cfg("enemies.collision_damage", COLLISION_DAMAGE)
	SLOW_FIELD_FACTOR = GameState.cfg("buffs.slow_field.factor", SLOW_FIELD_FACTOR)
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


## 撞击玩家（对齐原作逐帧轮询）：重叠期间每帧尝试结算——闪避逐帧重掷、
## 无敌结束仍重叠会再次命中；闪避/护甲/无敌/单帧守卫统一在 take_damage 内。
func _check_body_collision() -> void:
	var hb := GameState.player_hitbox
	if hb != null and overlaps_area(hb):
		(GameState.player_ref as Player).take_damage(COLLISION_DAMAGE)


## 池化复用：全状态重置（spawner 经 EnemyPool 调用；直接实例化走 _ready 初始化）
func reactivate(config: Dictionary, p_strategy: StringName, p_difficulty: float) -> void:
	_active = true
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
	_active = false
	visible = false
	set_physics_process(false)
	GameState.unregister_enemy(self)
	for c in died.get_connections():
		died.disconnect(c["callable"])
	position = Vector2(-500.0, -500.0)
	_deferred_disable_monitoring.call_deferred()


## 物理回调内不能直改 monitoring，延迟到帧末；若敌机已被重激活（同帧复用）则跳过
func _deferred_disable_monitoring() -> void:
	if not _active:
		monitoring = false


func _despawn() -> void:
	if _pool != null and is_instance_valid(_pool):
		_pool.release(self)
	else:
		queue_free()


func _exit_tree() -> void:
	GameState.unregister_enemy(self)
	# 池内 reparent 也会经过此回调（_repooling 置位），不算离开池
	if _pool != null and not _repooling:
		_pool.forget(self)


func _physics_process(delta: float) -> void:
	_time += delta
	if _exiting:
		# 寿命离场：向上或侧方加速，离场不给分、不计击杀
		_exit_speed += EXIT_ACCEL * delta
		position += _exit_dir * _exit_speed * delta
		var exit_view := GameState.view_world_rect()
		if (
			position.y < exit_view.position.y - 150.0
			or position.x < exit_view.position.x - 150.0
			or position.x > exit_view.end.x + 150.0
		):
			_despawn()
		return
	_life_timer += delta
	if _life_timer >= LIFETIME:
		_begin_lifetime_exit()
		return
	# 慢速力场：全局移速 ×0.8（仅移动位移，不影响射速/寿命/计时）
	var mdelta := delta * (SLOW_FIELD_FACTOR if GameState.buff_count(&"slow_field") > 0 else 1.0)
	match strategy:
		&"straight":
			position.y += speed * mdelta
		&"sine":
			position.y += speed * mdelta
			position.x = _spawn_x + sin_fast(_time * 3.0) * 90.0
		&"zigzag":
			_zig_timer -= delta
			if _zig_timer <= 0.0:
				_zig_dir = -_zig_dir
				_zig_timer = 0.7
			position += Vector2(_zig_dir * speed * 0.9, speed) * mdelta
			var view := GameState.view_world_rect()
			if position.x < view.position.x + 40.0 or position.x > view.end.x - 40.0:
				_zig_dir = -_zig_dir
				position.x = clampf(position.x, view.position.x + 40.0, view.end.x - 40.0)
		&"dive":
			if _dive_timer > 0.0:
				_dive_timer -= delta
				var dir := (_dive_target - position).normalized()
				position += dir * speed * 1.7 * mdelta
			else:
				position.y += speed * mdelta
		&"spiral":
			# 绕转中心匀速下压，机身绕中心小半径转圈
			_center.y += speed * mdelta
			position = _center + Vector2(cos_fast(_time * 4.0), sin_fast(_time * 4.0)) * SPIRAL_RADIUS
		&"noise":
			# 正弦叠加伪噪声驱动横向飘移
			var vx := (
				(sin_fast(_time * 1.7) + sin_fast(_time * 2.9 + 1.3) + sin_fast(_time * 4.3 + 2.1))
				/ 3.0 * speed * 1.2
			)
			position += Vector2(vx, speed) * mdelta
			var view := GameState.view_world_rect()
			position.x = clampf(position.x, view.position.x + 40.0, view.end.x - 40.0)
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
			position += Vector2(vx, speed * 0.9) * mdelta
			var view := GameState.view_world_rect()
			position.x = clampf(position.x, view.position.x + 40.0, view.end.x - 40.0)
		&"hover":
			if _hover_done:
				position.y += speed * mdelta
			elif _hovering:
				_hover_timer -= delta
				position.y = HOVER_Y + sin_fast(_time * 2.0) * 6.0  # 停驻轻微浮动
				if _hover_timer <= 0.0:
					_hover_done = true
			else:
				position.y += speed * mdelta
				if position.y >= HOVER_Y:
					_hovering = true

	if can_shoot:
		_fire_timer -= delta
		if _fire_timer <= 0.0:
			# 悬停期间更高频率点射
			_fire_timer = fire_interval * (0.5 if _hovering else 1.0)
			_fire_at_player()

	_check_body_collision()

	if position.y > GameState.view_world_rect().end.y + 60.0:
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
	var dmg := BULLET_DAMAGE_SINGLE
	if p_type == &"spread":
		dmg = BULLET_DAMAGE_SPREAD
	elif p_type == &"laser":
		dmg = BULLET_DAMAGE_LASER
	var b: Bullet = GameState.bullet_pool.fire(dir, bullet_speed, dmg, false)
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
	if hp <= 0:
		return  # 已死亡待回收（同帧多发命中防重复结算）
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
	# 吸血 buff：击毁回复 10% 生命上限（每帧至多一次，对齐原作 LIFESTEAL_FRACTION）
	GameState.try_lifesteal()
	GameState.play_sfx(GameState.SFX_EXPLOSION_BIG if is_elite else GameState.SFX_EXPLOSION)
	GameState.shake(GameState.cfg("effects.shake.elite_die", 9.0) if is_elite else GameState.cfg("effects.shake.enemy_die", 5.0))
	Explosion.spawn_at(get_parent(), global_position, 1.5 if is_elite else 1.0)
	died.emit(self)
	_despawn()
