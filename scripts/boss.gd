class_name Boss
extends Area2D
## Boss：3 种轮换（1 重装 / 2 游击 / 3 母舰），血量 <30% 触发狂暴阶段。
## 进入战斗 50s 未被击杀则逃跑：最后 3s 逃跑警告 + 上飘，随后加速离场
## （无击杀奖励：不触发 add_boss_kill、不加分、不升难度、轮换计数不推进）。

signal health_changed(current: float, maximum: float)
signal died
signal enraged
## 逃跑离场时发出（击毁不会发）；died 在击毁与逃跑离场时都会发出，
## 用于血条隐藏与生成器重排，击杀奖励只在 _die() 结算。
signal escaped

const TEXTURES: Array[Texture2D] = [
	preload("res://assets/sprites/boss_ship_1.png"),
	preload("res://assets/sprites/boss_ship_2.png"),
	preload("res://assets/sprites/boss_ship_3.png"),
]
var ENTER_SPEED := 140.0
var FIGHT_Y := 230.0
var STRAFE_MIN_X := 300.0
var STRAFE_MAX_X := 1620.0
## 各类型 HP 系数（基础 30 × 难度乘数）
var ENRAGE_HP_RATIO := 0.3
var ENRAGE_RATE_MULT := 1.5
var ENRAGE_SPEED_MULT := 1.3
## 狂暴快照弹幕（子弹时间结束后由 main 统一触发的一次性齐射）：4 激光向弹 + 8 方向环形慢弹
var ENRAGE_SNAPSHOT_LASERS := 4
var ENRAGE_SNAPSHOT_RING := 8
var ENRAGE_LASER_SPEED := 820.0  # 高速长弹（表现复用敌弹 laser 型）
var ENRAGE_RING_SPEED := 240.0  # 环形慢弹
## 逃跑：进入战斗 50s 未击杀触发，最后 3s 警告 + 上飘（对齐原作 3000/180 帧@60fps）
var ESCAPE_TIME := 50.0
var ESCAPE_WARNING := 3.0
var ESCAPE_DRIFT := 26.0
var ESCAPE_START_SPEED := 120.0
var ESCAPE_ACCEL := 420.0

var boss_type: int = 1
var max_hp: float = 30.0
var hp: float = 30.0
var is_escaped: bool = false

var _in_fight: bool = false
var _enraged: bool = false
var _score_scale: float = 1.0
var _strafe_dir: float = 1.0
var _fire_timer: float = 1.6
var _fan_next: bool = true
var _survival: float = 0.0
var _escape_warned: bool = false
var _escaping: bool = false
var _escape_speed: float = 0.0
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
	max_hp = 30.0 * float(GameState.cfg("boss.hp_mults", [1.3, 0.7, 1.6])[p_type - 1]) * p_difficulty
	hp = max_hp
	# setup() 在 _ready() 之前调用，不能用 @onready 变量
	($Sprite2D as Sprite2D).texture = TEXTURES[p_type - 1]


func _ready() -> void:
	add_to_group("enemy")
	GameState.register_enemy(self)
	# 数值配置缓存（启动一次读入）
	ENTER_SPEED = GameState.cfg("boss.enter_speed", ENTER_SPEED)
	FIGHT_Y = GameState.cfg("boss.fight_y", FIGHT_Y)
	STRAFE_MIN_X = GameState.cfg("boss.strafe_min_x", STRAFE_MIN_X)
	STRAFE_MAX_X = GameState.cfg("boss.strafe_max_x", STRAFE_MAX_X)
	ENRAGE_HP_RATIO = GameState.cfg("boss.enrage.hp_ratio", ENRAGE_HP_RATIO)
	ENRAGE_RATE_MULT = GameState.cfg("boss.enrage.rate_mult", ENRAGE_RATE_MULT)
	ENRAGE_SPEED_MULT = GameState.cfg("boss.enrage.speed_mult", ENRAGE_SPEED_MULT)
	ENRAGE_SNAPSHOT_LASERS = GameState.cfg("boss.enrage.snapshot_lasers", ENRAGE_SNAPSHOT_LASERS)
	ENRAGE_SNAPSHOT_RING = GameState.cfg("boss.enrage.snapshot_ring", ENRAGE_SNAPSHOT_RING)
	ENRAGE_LASER_SPEED = GameState.cfg("boss.enrage.laser_speed", ENRAGE_LASER_SPEED)
	ENRAGE_RING_SPEED = GameState.cfg("boss.enrage.ring_speed", ENRAGE_RING_SPEED)
	ESCAPE_TIME = GameState.cfg("boss.escape.time", ESCAPE_TIME)
	ESCAPE_WARNING = GameState.cfg("boss.escape.warning", ESCAPE_WARNING)
	ESCAPE_DRIFT = GameState.cfg("boss.escape.drift", ESCAPE_DRIFT)
	ESCAPE_START_SPEED = GameState.cfg("boss.escape.start_speed", ESCAPE_START_SPEED)
	ESCAPE_ACCEL = GameState.cfg("boss.escape.accel", ESCAPE_ACCEL)


func _exit_tree() -> void:
	GameState.unregister_enemy(self)


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
	if _escaping:
		# 逃跑离场：向上加速飘出屏幕（不再受弹、不再开火）
		_escape_speed += ESCAPE_ACCEL * delta
		position.y -= _escape_speed * delta
		if position.y < -280.0:
			escaped.emit()
			died.emit()  # 离场通知（血条/生成器重排）；非击毁，无击杀奖励
			queue_free()
		return
	if not _in_fight:
		position.y += ENTER_SPEED * delta
		if position.y >= FIGHT_Y:
			_in_fight = true
			health_changed.emit(hp, max_hp)
		return

	# 存活计时：50s 未被击杀则逃跑；最后 3s 警告 + 上飘
	_survival += delta
	if _survival >= ESCAPE_TIME:
		_begin_escape()
		return
	if _survival >= ESCAPE_TIME - ESCAPE_WARNING:
		if not _escape_warned:
			_escape_warned = true
			_show_escape_warning()
		position.y -= ESCAPE_DRIFT * delta
		_sprite.modulate = (
			Color(1.8, 1.3, 0.5) if int(_survival * 8.0) % 2 == 0 else _base_modulate()
		)

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
	if GameState.player_ref != null:
		return (GameState.player_ref.global_position - global_position).normalized()
	return Vector2.DOWN


func _fire_fan() -> void:
	var base_dir := _player_dir()
	for i in 5:
		var dir := base_dir.rotated(deg_to_rad(20.0 * (float(i) - 2.0)))
		var b: Bullet = GameState.bullet_pool.fire(dir, 380.0, 1, false)
		b.position = position + dir * 100.0


func _fire_homing() -> void:
	var b: Bullet = GameState.bullet_pool.fire(Vector2.DOWN, 300.0, 1, false, true, 1.5)
	b.position = position + Vector2(0.0, 100.0)


func _fire_sniper() -> void:
	var dir := _player_dir()
	var b: Bullet = GameState.bullet_pool.fire(dir, 650.0, 1, false)
	b.position = position + dir * 100.0


func _fire_cross() -> void:
	for i in 4:
		var dir := Vector2.RIGHT.rotated(_cross_angle + float(i) * PI / 2.0)
		var b: Bullet = GameState.bullet_pool.fire(dir, 260.0, 1, false)
		b.position = position + dir * 100.0
	_cross_angle += deg_to_rad(15.0)


func _summon_minions() -> void:
	var spawner := get_tree().get_first_node_in_group("spawner")
	if spawner == null:
		return
	for i in randi_range(2, 3):
		spawner.spawn_minion(position + Vector2(randf_range(-80.0, 80.0), 110.0))


## 狂暴快照弹幕：狂暴进入时的一次性齐射（由 main 在子弹时间结束后统一触发）。
## 4 道激光向弹（高速长弹，复用敌弹 laser 型表现）+ 8 方向环形慢弹，
## 之后接续常规狂暴循环（射速 ×1.5，见 _physics_process）。
func fire_enrage_snapshot() -> void:
	var aim := _player_dir()
	var side := aim.orthogonal()
	for i in ENRAGE_SNAPSHOT_LASERS:
		var laser: Bullet = GameState.bullet_pool.fire(aim, ENRAGE_LASER_SPEED, 1, false)
		laser.position = position + aim * 100.0 + side * (float(i) - 1.5) * 44.0
		laser.set_meta("bullet_type", &"laser")
		# 细长高亮快速弹（与敌机 laser 弹同表现，polygon 尖端朝 +x 即飞行方向）
		var poly := laser.get_node("Polygon2D") as Polygon2D
		poly.scale = Vector2(2.2, 0.55)
		poly.color = Color(1.0, 0.85, 0.35)
	for i in ENRAGE_SNAPSHOT_RING:
		var dir := Vector2.RIGHT.rotated(TAU * float(i) / float(ENRAGE_SNAPSHOT_RING))
		var b: Bullet = GameState.bullet_pool.fire(dir, ENRAGE_RING_SPEED, 1, false)
		b.position = position + dir * 100.0
		b.set_meta("bullet_type", &"enrage_ring")


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
	GameState.shake(GameState.cfg("effects.shake.enrage", 16.0))
	GameState.play_sfx(GameState.SFX_EXPLOSION_BIG, -6.0)
	enraged.emit()


func _die() -> void:
	GameState.add_boss_kill(_score_scale)
	Explosion.spawn_boss_sequence(get_parent(), global_position)
	died.emit()
	queue_free()


## 逃跑警告：复用 HUD 警告横幅（不可用时退化为 print），最后 3s 机身闪烁见 _physics_process
func _show_escape_warning() -> void:
	var hud := get_tree().get_first_node_in_group("hud")
	if hud != null and hud.has_method("_show_warning"):
		hud._show_warning("⚠ Boss 试图逃离战场 ⚠")
	else:
		print("[BOSS] 逃跑警告：Boss 即将逃离战场")


## 50s 未被击杀：逃跑（无 add_boss_kill / 加分 / 难度提升 / 轮换推进）
func _begin_escape() -> void:
	_escaping = true
	is_escaped = true
	_escape_speed = ESCAPE_START_SPEED
	collision_layer = 0  # 离场阶段不再受弹
	collision_mask = 0
	_sprite.modulate = _base_modulate()
	print("[BOSS] 存活 %ds 未被击杀，逃离战场（无击杀奖励）" % int(ESCAPE_TIME))
