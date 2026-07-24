class_name Boss
extends Area2D
## Boss：3 种轮换（1 重装 / 2 游击 / 3 母舰），血量 <30% 触发狂暴阶段。
## 狂暴为完整序列（对齐原作 EnrageSubMachine）：TRANSITION 0.9s（子弹时间 + 蓄力抖动
## 滑入轨道）→ ACTIVE（绕触发时玩家位置快照走方形→圆形轨道 + 高速波次开火，
## 期间锁血在 30% 检查点、冻结玩家移动）→ RELEASE_HOLD 0.7s（解血锁/解玩家锁，
## 每 0.1s 密集慢速弹幕）→ RETURN 0.8s 飞回战斗位 → 回到常规狂暴循环（射速/移速倍率）。
## 进入战斗 50s 未被击杀则逃跑：最后 3s 逃跑警告 + 上飘，随后加速离场
## （无击杀奖励：不触发 add_boss_kill、不加分、不升难度、轮换计数不推进）。

signal health_changed(current: float, maximum: float)
signal died
signal enraged
## 逃跑离场时发出（击毁不会发）；died 在击毁与逃跑离场时都会发出，
## 用于血条隐藏与生成器重排，击杀奖励只在 _die() 结算。
signal escaped

## 狂暴子状态机（对齐原作 BossState 的 4 个 ENRAGE_* 子状态）
enum EnragePhase { NONE, TRANSITION, ACTIVE, RELEASE_HOLD, RETURN }

const TEXTURES: Array[Texture2D] = [
	preload("res://assets/sprites/boss_ship_1.png"),
	preload("res://assets/sprites/boss_ship_2.png"),
	preload("res://assets/sprites/boss_ship_3.png"),
]
var ENTER_SPEED := 140.0
var FIGHT_Y := 230.0
var STRAFE_MIN_X := 300.0
var STRAFE_MAX_X := 1620.0
## HP 基底（× 类型系数 × 难度乘数；对齐原作首发 Boss ≈12s TTK 量级）
var HP_BASE := 800.0
## 各类型移动速度 / 开火间隔 / 弹速（读 balance.json boss 段，AGENTS.md 调参约定）
var STRAFE_SPEEDS: Array = [150.0, 400.0, 60.0]
var FIRE_INTERVALS: Array = [1.6, 1.8, 0.9]
var FAN_BULLET_SPEED := 380.0
var HOMING_BULLET_SPEED := 300.0
var SNIPER_BULLET_SPEED := 650.0
var CROSS_BULLET_SPEED := 260.0
var ENRAGE_HP_RATIO := 0.3
var ENRAGE_RATE_MULT := 1.5
var ENRAGE_SPEED_MULT := 1.3
## 狂暴快照弹幕（子弹时间结束后由 main 统一触发的一次性齐射）：4 激光向弹 + 8 方向环形慢弹
var ENRAGE_SNAPSHOT_LASERS := 4
var ENRAGE_SNAPSHOT_RING := 8
var ENRAGE_LASER_SPEED := 820.0  # 高速长弹（表现复用敌弹 laser 型）
var ENRAGE_RING_SPEED := 240.0  # 环形慢弹
## 狂暴序列时序（对齐原作 EnrageConstants @60fps：360/54/42/24/6/42/48 帧）
var ENRAGE_DURATION := 6.0  # TRANSITION+ACTIVE 总时长（360 帧）
var ENRAGE_TRANSITION_DURATION := 0.9  # 54 帧
var ENRAGE_ATTACK_INTERVAL := 0.7  # ACTIVE 每波间隔（42 帧）
var ENRAGE_ATTACK_WINDUP := 0.4  # ACTIVE 起手延迟（24 帧）
var ENRAGE_RELEASE_INTERVAL := 0.1  # RELEASE_HOLD 每波间隔（6 帧）
var ENRAGE_RELEASE_HOLD_DURATION := 0.7  # 42 帧
var ENRAGE_RETURN_DURATION := 0.8  # 48 帧
## 轨道：半径 = max(机体宽,高)×1.5 受屏幕边界约束（原作 PATH_RADIUS_SCALE/MIN_Y 钳制）
var ENRAGE_PATH_RADIUS_SCALE := 1.5
var ENRAGE_SQUARE_PATH_RATIO := 0.48  # 前 48% 方形路径，后 52% 圆形路径
## RELEASE 弹速 = ACTIVE 弹速 × 原作释放比例（1.35/3.7≈0.365、1.55/3.2≈0.484）
var ENRAGE_RELEASE_LASER_SPEED := 300.0
var ENRAGE_RELEASE_RING_SPEED := 120.0
## 逃跑：进入战斗 50s 未击杀触发，最后 3s 警告 + 上飘（对齐原作 3000/180 帧@60fps）
var ESCAPE_TIME := 50.0
var ESCAPE_WARNING := 3.0
var ESCAPE_DRIFT := 26.0
var ESCAPE_START_SPEED := 120.0
var ESCAPE_ACCEL := 420.0
## 各弹种伤害（对齐原作 boss_attack.py phase-1：spread 12+2=14 / aim 18+3=21 / wave 12 /
## 快照激光 18+3=21 / 快照环弹 12；homing 为本版弹种取 wave 同档 12）
var BULLET_DAMAGE_FAN := 14
var BULLET_DAMAGE_HOMING := 12
var BULLET_DAMAGE_SNIPER := 21
var BULLET_DAMAGE_CROSS := 12
var BULLET_DAMAGE_SNAPSHOT_LASER := 21
var BULLET_DAMAGE_SNAPSHOT_RING := 12
## 身体撞击伤害（对齐原作 BOSS_COLLISION_DAMAGE=30）
var COLLISION_DAMAGE := 30
## 慢速力场：机体移速 ×0.8（对齐原作 boss 移动 slow_factor）
var SLOW_FIELD_FACTOR := 0.8

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
# 狂暴序列状态（计时单位均为游戏秒，随 time_scale 缩放）
var _enrage_phase: int = EnragePhase.NONE
var _enrage_timer: float = 0.0  # TRANSITION+ACTIVE 剩余（progress 驱动轨道）
var _enrage_transition_timer: float = 0.0
var _enrage_release_hold_timer: float = 0.0
var _enrage_return_timer: float = 0.0
var _enrage_attack_timer: float = 0.0
var _enrage_attack_index: int = 0
## 锁血：触发→RELEASE_HOLD 开始前 HP 锁定在 30% 检查点（任何伤害不掉血不死）
var _enrage_health_lock: bool = false
var _enrage_snapshot_target := Vector2.ZERO  # 触发时玩家位置快照（轨道中心）
var _enrage_transition_origin := Vector2.ZERO
var _enrage_return_origin := Vector2.ZERO
var _enrage_return_target := Vector2.ZERO
var _locked_player: Player = null  # 被冻结移动的玩家（用于精确解锁）
var _boss_size := Vector2(328.0, 328.0)  # 贴图有效尺寸（_ready 实测更新，算轨道半径）

@onready var _sprite: Sprite2D = $Sprite2D


func setup(p_difficulty: float, p_type: int) -> void:
	boss_type = p_type
	max_hp = (
		float(GameState.cfg("boss.hp_base", HP_BASE))
		* float(GameState.cfg("boss.hp_mults", [1.3, 0.7, 1.6])[p_type - 1])
		* p_difficulty
	)
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
	ENRAGE_DURATION = GameState.cfg("boss.enrage.duration", ENRAGE_DURATION)
	ENRAGE_TRANSITION_DURATION = GameState.cfg("boss.enrage.transition_duration", ENRAGE_TRANSITION_DURATION)
	ENRAGE_ATTACK_INTERVAL = GameState.cfg("boss.enrage.attack_interval", ENRAGE_ATTACK_INTERVAL)
	ENRAGE_ATTACK_WINDUP = GameState.cfg("boss.enrage.attack_windup", ENRAGE_ATTACK_WINDUP)
	ENRAGE_RELEASE_INTERVAL = GameState.cfg("boss.enrage.release_interval", ENRAGE_RELEASE_INTERVAL)
	ENRAGE_RELEASE_HOLD_DURATION = GameState.cfg("boss.enrage.release_hold_duration", ENRAGE_RELEASE_HOLD_DURATION)
	ENRAGE_RETURN_DURATION = GameState.cfg("boss.enrage.return_duration", ENRAGE_RETURN_DURATION)
	ENRAGE_PATH_RADIUS_SCALE = GameState.cfg("boss.enrage.path_radius_scale", ENRAGE_PATH_RADIUS_SCALE)
	ENRAGE_SQUARE_PATH_RATIO = GameState.cfg("boss.enrage.square_path_ratio", ENRAGE_SQUARE_PATH_RATIO)
	ENRAGE_RELEASE_LASER_SPEED = GameState.cfg("boss.enrage.release_laser_speed", ENRAGE_RELEASE_LASER_SPEED)
	ENRAGE_RELEASE_RING_SPEED = GameState.cfg("boss.enrage.release_ring_speed", ENRAGE_RELEASE_RING_SPEED)
	_boss_size = _sprite.texture.get_size() * _sprite.scale
	ESCAPE_TIME = GameState.cfg("boss.escape.time", ESCAPE_TIME)
	ESCAPE_WARNING = GameState.cfg("boss.escape.warning", ESCAPE_WARNING)
	ESCAPE_DRIFT = GameState.cfg("boss.escape.drift", ESCAPE_DRIFT)
	ESCAPE_START_SPEED = GameState.cfg("boss.escape.start_speed", ESCAPE_START_SPEED)
	ESCAPE_ACCEL = GameState.cfg("boss.escape.accel", ESCAPE_ACCEL)
	HP_BASE = GameState.cfg("boss.hp_base", HP_BASE)
	STRAFE_SPEEDS = GameState.cfg("boss.strafe_speeds", STRAFE_SPEEDS)
	FIRE_INTERVALS = GameState.cfg("boss.fire_intervals", FIRE_INTERVALS)
	FAN_BULLET_SPEED = GameState.cfg("boss.fan_bullet_speed", FAN_BULLET_SPEED)
	HOMING_BULLET_SPEED = GameState.cfg("boss.homing_bullet_speed", HOMING_BULLET_SPEED)
	SNIPER_BULLET_SPEED = GameState.cfg("boss.sniper_bullet_speed", SNIPER_BULLET_SPEED)
	CROSS_BULLET_SPEED = GameState.cfg("boss.cross_bullet_speed", CROSS_BULLET_SPEED)
	COLLISION_DAMAGE = GameState.cfg("boss.collision_damage", COLLISION_DAMAGE)
	SLOW_FIELD_FACTOR = GameState.cfg("buffs.slow_field.factor", SLOW_FIELD_FACTOR)
	BULLET_DAMAGE_FAN = GameState.cfg("boss.bullet_damage.fan", BULLET_DAMAGE_FAN)
	BULLET_DAMAGE_HOMING = GameState.cfg("boss.bullet_damage.homing", BULLET_DAMAGE_HOMING)
	BULLET_DAMAGE_SNIPER = GameState.cfg("boss.bullet_damage.sniper", BULLET_DAMAGE_SNIPER)
	BULLET_DAMAGE_CROSS = GameState.cfg("boss.bullet_damage.cross", BULLET_DAMAGE_CROSS)
	BULLET_DAMAGE_SNAPSHOT_LASER = GameState.cfg("boss.bullet_damage.snapshot_laser", BULLET_DAMAGE_SNAPSHOT_LASER)
	BULLET_DAMAGE_SNAPSHOT_RING = GameState.cfg("boss.bullet_damage.snapshot_ring", BULLET_DAMAGE_SNAPSHOT_RING)


func _exit_tree() -> void:
	GameState.unregister_enemy(self)
	_unlock_player_movement()  # 兜底：离场必解除玩家移动冻结，不留死锁


func _base_fire_interval() -> float:
	return float(FIRE_INTERVALS[clampi(boss_type - 1, 0, FIRE_INTERVALS.size() - 1)])


## 慢速力场因子（全局机体移速 ×0.8；与狂暴移速倍率相乘）
func _slow_factor() -> float:
	return SLOW_FIELD_FACTOR if GameState.buff_count(&"slow_field") > 0 else 1.0


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
		position.y += ENTER_SPEED * _slow_factor() * delta
		if position.y >= FIGHT_Y:
			_in_fight = true
			health_changed.emit(hp, max_hp)
		return

	# 存活计时：50s 未被击杀则逃跑；最后 3s 警告 + 上飘
	_survival += delta
	if _survival >= ESCAPE_TIME:
		_begin_escape()
		return
	if _survival >= ESCAPE_TIME - ESCAPE_WARNING and not _escape_warned:
		_escape_warned = true
		_show_escape_warning()

	# 狂暴序列接管移动与开火（逃跑计时照常走，序列中到点照样逃跑；撞击判定保留）
	if _enrage_phase != EnragePhase.NONE:
		if _survival >= ESCAPE_TIME - ESCAPE_WARNING:
			_sprite.modulate = (
				Color(1.8, 1.3, 0.5) if int(_survival * 8.0) % 2 == 0 else _base_modulate()
			)
		_update_enrage_sequence(delta)
		_check_body_collision()
		return

	if _survival >= ESCAPE_TIME - ESCAPE_WARNING:
		position.y -= ESCAPE_DRIFT * delta
		_sprite.modulate = (
			Color(1.8, 1.3, 0.5) if int(_survival * 8.0) % 2 == 0 else _base_modulate()
		)

	match boss_type:
		1:
			_move_strafe(delta, float(STRAFE_SPEEDS[0]))
		2:
			_move_dash(delta)
		3:
			_move_strafe(delta, float(STRAFE_SPEEDS[2]))

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

	_check_body_collision()


## 巡航范围随可见世界区域收窄（zoom=1 时与配置值 STRAFE_MIN_X/MAX_X 一致）
func _strafe_range() -> Vector2:
	var view := GameState.view_world_rect()
	var lo := view.position.x + STRAFE_MIN_X
	var hi := maxf(view.end.x - (1920.0 - STRAFE_MAX_X), lo)
	return Vector2(lo, hi)


func _move_strafe(delta: float, p_speed: float) -> void:
	position.x += _strafe_dir * p_speed * _slow_factor() * (ENRAGE_SPEED_MULT if _enraged else 1.0) * delta
	var bounds := _strafe_range()
	if position.x < bounds.x or position.x > bounds.y:
		_strafe_dir = -_strafe_dir
		position.x = clampf(position.x, bounds.x, bounds.y)


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
		position.x += _strafe_dir * float(STRAFE_SPEEDS[1]) * _slow_factor() * (ENRAGE_SPEED_MULT if _enraged else 1.0) * delta
		var bounds := _strafe_range()
		if position.x < bounds.x or position.x > bounds.y:
			_strafe_dir = -_strafe_dir
			position.x = clampf(position.x, bounds.x, bounds.y)


func _player_dir() -> Vector2:
	if GameState.player_ref != null:
		return (GameState.player_ref.global_position - global_position).normalized()
	return Vector2.DOWN


func _fire_fan() -> void:
	var base_dir := _player_dir()
	for i in 5:
		var dir := base_dir.rotated(deg_to_rad(20.0 * (float(i) - 2.0)))
		var b: Bullet = GameState.bullet_pool.fire(dir, FAN_BULLET_SPEED, BULLET_DAMAGE_FAN, false)
		b.position = position + dir * 100.0


func _fire_homing() -> void:
	var b: Bullet = GameState.bullet_pool.fire(Vector2.DOWN, HOMING_BULLET_SPEED, BULLET_DAMAGE_HOMING, false, true, 1.5)
	b.position = position + Vector2(0.0, 100.0)


func _fire_sniper() -> void:
	var dir := _player_dir()
	var b: Bullet = GameState.bullet_pool.fire(dir, SNIPER_BULLET_SPEED, BULLET_DAMAGE_SNIPER, false)
	b.position = position + dir * 100.0


func _fire_cross() -> void:
	for i in 4:
		var dir := Vector2.RIGHT.rotated(_cross_angle + float(i) * PI / 2.0)
		var b: Bullet = GameState.bullet_pool.fire(dir, CROSS_BULLET_SPEED, BULLET_DAMAGE_CROSS, false)
		b.position = position + dir * 100.0
	_cross_angle += deg_to_rad(15.0)


func _summon_minions() -> void:
	var spawner := get_tree().get_first_node_in_group("spawner")
	if spawner == null:
		return
	for i in randi_range(2, 3):
		spawner.spawn_minion(position + Vector2(randf_range(-80.0, 80.0), 110.0))


## 狂暴快照弹幕：狂暴进入时的一次性齐射（由 main 在子弹时间结束后统一触发），
## ACTIVE 阶段每 0.7s 一波也复用本函数。4 道激光向弹（高速长弹，复用敌弹 laser 型表现）
## + 8 方向环形慢弹。序列结束后的常规阶段接续射速 ×1.5（见 _physics_process）。
func fire_enrage_snapshot() -> void:
	_fire_enrage_wave(ENRAGE_LASER_SPEED, ENRAGE_RING_SPEED)


## RELEASE_HOLD 密集释放：同构弹幕但用慢速（原作 release 弹速比例 1.35/3.7、1.55/3.2）
func _fire_enrage_release() -> void:
	_fire_enrage_wave(ENRAGE_RELEASE_LASER_SPEED, ENRAGE_RELEASE_RING_SPEED)


func _fire_enrage_wave(laser_speed: float, ring_speed: float) -> void:
	if _escaping:
		return
	var aim := _player_dir()
	var side := aim.orthogonal()
	for i in ENRAGE_SNAPSHOT_LASERS:
		var laser: Bullet = GameState.bullet_pool.fire(aim, laser_speed, BULLET_DAMAGE_SNAPSHOT_LASER, false)
		laser.position = position + aim * 100.0 + side * (float(i) - 1.5) * 44.0
		laser.set_meta("bullet_type", &"laser")
		# 细长高亮快速弹（与敌机 laser 弹同表现，polygon 尖端朝 +x 即飞行方向）
		var poly := laser.get_node("Polygon2D") as Polygon2D
		poly.scale = Vector2(2.2, 0.55)
		poly.color = Color(1.0, 0.85, 0.35)
	for i in ENRAGE_SNAPSHOT_RING:
		var dir := Vector2.RIGHT.rotated(TAU * float(i) / float(ENRAGE_SNAPSHOT_RING))
		var b: Bullet = GameState.bullet_pool.fire(dir, ring_speed, BULLET_DAMAGE_SNAPSHOT_RING, false)
		b.position = position + dir * 100.0
		b.set_meta("bullet_type", &"enrage_ring")


## 狂暴序列驱动：TRANSITION（蓄力抖动滑入轨道）→ ACTIVE（轨道环绕 + 波次开火）
## → RELEASE_HOLD（原地密集慢速弹幕）→ RETURN（飞回战斗位）→ NONE（常规狂暴循环）
func _update_enrage_sequence(delta: float) -> void:
	match _enrage_phase:
		EnragePhase.TRANSITION:
			_enrage_timer = maxf(_enrage_timer - delta, 0.0)
			_enrage_transition_timer -= delta
			var t := clampf(1.0 - _enrage_transition_timer / ENRAGE_TRANSITION_DURATION, 0.0, 1.0)
			var eased := 1.0 - pow(1.0 - t, 3.0)
			var shake := Vector2(
				Enemy.sin_fast(t * TAU * 7.0) * (1.0 - t) * 13.0,
				Enemy.cos_fast(t * TAU * 5.0) * (1.0 - t) * 8.0
			)
			position = _enrage_transition_origin.lerp(_enrage_path_center(_enrage_progress()), eased) + shake
			if _enrage_transition_timer <= 0.0:
				_enrage_phase = EnragePhase.ACTIVE
				_enrage_attack_timer = ENRAGE_ATTACK_WINDUP
				_enrage_attack_index = 0
		EnragePhase.ACTIVE:
			_enrage_timer = maxf(_enrage_timer - delta, 0.0)
			position = _enrage_path_center(_enrage_progress())
			_enrage_attack_timer -= delta
			if _enrage_attack_timer <= 0.0:
				_enrage_attack_timer = ENRAGE_ATTACK_INTERVAL
				_enrage_attack_index += 1
				fire_enrage_snapshot()
			if _enrage_timer <= 0.0:
				_begin_release_hold()
		EnragePhase.RELEASE_HOLD:
			_enrage_release_hold_timer -= delta
			_enrage_attack_timer -= delta
			if _enrage_attack_timer <= 0.0:
				_enrage_attack_timer = ENRAGE_RELEASE_INTERVAL
				_fire_enrage_release()
			if _enrage_release_hold_timer <= 0.0:
				_begin_return()
		EnragePhase.RETURN:
			_enrage_return_timer -= delta
			var t := clampf(1.0 - _enrage_return_timer / ENRAGE_RETURN_DURATION, 0.0, 1.0)
			var eased := t * t * (3.0 - 2.0 * t)
			position = _enrage_return_origin.lerp(_enrage_return_target, eased)
			if _enrage_return_timer <= 0.0:
				_enrage_phase = EnragePhase.NONE


## 序列进度 0→1（TRANSITION 起算，ACTIVE 结束到 1；对齐原作 enrage_progress）
func _enrage_progress() -> float:
	return clampf(1.0 - _enrage_timer / ENRAGE_DURATION, 0.0, 1.0)


## 轨道半径：max(机体宽,高)×1.5，受屏幕边界约束（对齐原作 enrage_path_radius，下限 24）
func _enrage_path_radius() -> float:
	var base := maxf(_boss_size.x, _boss_size.y) * ENRAGE_PATH_RADIUS_SCALE
	var view := GameState.view_world_rect()
	var half := _boss_size * 0.5
	var max_radius := maxf(24.0, minf(
		minf(
			_enrage_snapshot_target.x - view.position.x - half.x,
			view.end.x - _enrage_snapshot_target.x - half.x
		),
		minf(
			_enrage_snapshot_target.y - view.position.y - half.y,
			view.end.y - _enrage_snapshot_target.y - half.y
		)
	))
	return minf(base, max_radius)


## 轨道中心：前 48% 方形路径（底→左→顶→右→底），后 52% 圆形路径（底部起顺接）
func _enrage_path_center(progress: float) -> Vector2:
	progress = clampf(progress, 0.0, 1.0)
	var radius := _enrage_path_radius()
	var c := _enrage_snapshot_target
	if progress <= ENRAGE_SQUARE_PATH_RATIO:
		var sp := progress / ENRAGE_SQUARE_PATH_RATIO
		var segment := mini(3, int(sp * 4.0))
		var local := sp * 4.0 - float(segment)
		var points: Array[Vector2] = [
			c + Vector2(0.0, radius),
			c + Vector2(-radius, 0.0),
			c + Vector2(0.0, -radius),
			c + Vector2(radius, 0.0),
			c + Vector2(0.0, radius),
		]
		return points[segment].lerp(points[segment + 1], local)
	var cp := (progress - ENRAGE_SQUARE_PATH_RATIO) / (1.0 - ENRAGE_SQUARE_PATH_RATIO)
	var angle := PI / 2.0 + cp * TAU
	return c + Vector2(Enemy.cos_fast(angle), Enemy.sin_fast(angle)) * radius


## ACTIVE 计时耗尽：进入释放阶段——解血锁、解玩家移动锁（对齐原作 begin_enrage_release_hold）
func _begin_release_hold() -> void:
	_enrage_phase = EnragePhase.RELEASE_HOLD
	_enrage_release_hold_timer = ENRAGE_RELEASE_HOLD_DURATION
	_enrage_attack_timer = 0.0  # 立即放第一波
	_enrage_health_lock = false
	_unlock_player_movement()


## RELEASE_HOLD 结束：0.8s 飞回战斗位（x 钳回巡航范围、y 回 FIGHT_Y）
func _begin_return() -> void:
	_enrage_phase = EnragePhase.RETURN
	_enrage_return_timer = ENRAGE_RETURN_DURATION
	_enrage_return_origin = position
	var bounds := _strafe_range()
	_enrage_return_target = Vector2(clampf(position.x, bounds.x, bounds.y), FIGHT_Y)


## 序列中断（逃跑/死亡/离场/教程收尾）：清状态 + 解血锁 + 解玩家移动锁，幂等
func _abort_enrage_sequence() -> void:
	_enrage_phase = EnragePhase.NONE
	_enrage_health_lock = false
	_unlock_player_movement()


## 冻结玩家移动（对齐原作 is_controls_locked：完全定身但可射击）；TRANSITION+ACTIVE 有效
func _lock_player_movement() -> void:
	var p := GameState.player_ref
	if p != null and not p._dead:
		_locked_player = p
		p.movement_locked = true


func _unlock_player_movement() -> void:
	if _locked_player != null:
		if is_instance_valid(_locked_player):
			_locked_player.movement_locked = false
		_locked_player = null


## 狂暴锁血（对齐原作 boss_sub_state.py compute_take_damage）：致死伤害直接击杀；
## 否则未狂暴时最多把 HP 打到阈值（触发狂暴）；锁血期（触发→RELEASE_HOLD 前）
## 任何伤害不掉血不死；RELEASE_HOLD 解锁后正常扣血可击杀。
func take_damage(amount: int, score_scale: float = 1.0) -> void:
	if hp <= 0.0:
		return  # 已死亡待释放（同帧多发命中防重复结算）
	if _enrage_health_lock:
		_flash_hit()  # 锁血期：仅受击闪白反馈，不掉血不死（致死也不死）
		return
	hp -= float(amount)
	_score_scale = score_scale
	if hp > 0.0 and not _enraged and hp < max_hp * ENRAGE_HP_RATIO:
		hp = max_hp * ENRAGE_HP_RATIO
	health_changed.emit(hp, max_hp)
	_flash_hit()
	if hp <= 0.0:
		_die()
	elif not _enraged and hp <= max_hp * ENRAGE_HP_RATIO:
		_enrage()


## 受击闪白（锁血期复用）
func _flash_hit() -> void:
	_sprite.modulate = Color(2.0, 2.0, 2.0)
	var tween := create_tween()
	# 游击型受击硬直（闪白）更短
	tween.tween_property(_sprite, "modulate", _base_modulate(), 0.05 if boss_type == 2 else 0.1)


## 身体撞击（对齐原作 boss_vs_player.py 逐帧轮询）：入场降入与逃跑离场阶段不判定；
## 玩家 -30 HP（受击无敌帧节流连撞，无敌结束仍重叠会再次命中），Boss 不掉血、不自毁。
func _check_body_collision() -> void:
	var hb := GameState.player_hitbox
	if hb != null and overlaps_area(hb):
		(GameState.player_ref as Player).take_damage(COLLISION_DAMAGE)


func _enrage() -> void:
	_enraged = true
	# 启动狂暴序列：锁血 30% 检查点 + 快照玩家位置 + 冻结玩家移动
	_enrage_health_lock = true
	_enrage_phase = EnragePhase.TRANSITION
	_enrage_timer = ENRAGE_DURATION
	_enrage_transition_timer = ENRAGE_TRANSITION_DURATION
	_enrage_transition_origin = position
	_enrage_snapshot_target = (
		GameState.player_ref.global_position
		if GameState.player_ref != null
		else GameState.view_world_rect().get_center()
	)
	_lock_player_movement()
	_sprite.modulate = _base_modulate()
	GameState.shake(GameState.cfg("effects.shake.enrage", 16.0))
	GameState.play_sfx(GameState.SFX_EXPLOSION_BIG, -6.0)
	enraged.emit()


func _die() -> void:
	_abort_enrage_sequence()
	GameState.add_boss_kill(_score_scale)
	# 吸血 buff：Boss 击杀同样触发（对齐原作 boss_manager 路径，每帧至多一次）
	GameState.try_lifesteal()
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
	_abort_enrage_sequence()  # 序列中断：解血锁 + 解玩家移动锁
	_escaping = true
	is_escaped = true
	_escape_speed = ESCAPE_START_SPEED
	collision_layer = 0  # 离场阶段不再受弹
	collision_mask = 0
	_sprite.modulate = _base_modulate()
	print("[BOSS] 存活 %ds 未被击杀，逃离战场（无击杀奖励）" % int(ESCAPE_TIME))
