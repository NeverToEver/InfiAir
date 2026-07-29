class_name Mothership
extends Area2D
## 母舰补给平台（对齐原作）：长按 H 蓄力召唤（main 管理蓄力）→
## DESCEND 缓动降入 → 到位自动 DOCKING 牵引对接（原作无区域判定，点吸附补间）→
## RESUPPLY 补给 → STAY 驻留 20s（弹匣 10 格，2s/格；≤4 格警告，警告 5s 后强制离舰，
## 对齐原作"横幅播完强制弹射"，自然 20s 到期不可达；可长按 H 2s 提前离舰，
## 冷却双机制折扣：时长 max(0.6, 1-0.4×剩余比例) + 进度预填 min(0.3, 0.5×剩余比例)）→
## RELEASE 释放 → DEPART 加速离场。
## 无敌窗口对齐原作：对接吸附开始即无敌（锁输入），弹射结束才解除（释放后 2s 为重制版 QoL）。
## STAY 期间 WASD 直接驾驶母舰（原作特性），加特林双塔向上 80° 扫射 + 导弹齐射（≤5 目标）。
## 母舰弹丸/导弹击毁只给 1/3 分（score_scale 标记，结算时向下取整）。

signal departed(cooldown: float)

enum State { DESCEND, HOVER, DOCKING, RESUPPLY, STAY, RELEASE, DEPART }

var HOVER_Y := 270.0
var RELEASE_INVINCIBLE := 2.0  # 释放后保护（重制版 QoL，原作弹射后无保护）
var DOCK_TWEEN_TIME := 1.5
var DOCK_OFFSET_Y := 140.0
var RESUPPLY_DELAY := 0.5
var RELEASE_TIME := 0.5
var RELEASE_DROP := 140.0
# 弹匣：10 格 × 2s = 20s 驻留（实际被警告强制离舰截断为 ≈17s）
var MAG_CELLS := 10
var MAG_CELL_TIME := 2.0
var MAG_WARN_CELLS := 4
var WARN_EJECT_DELAY := 5.0  # 警告后强制离舰延迟（对齐原作警告横幅 5s 播完强制弹射）
var EARLY_HOLD_TIME := 2.0
var EARLY_MAX_DISCOUNT := 0.4  # 冷却时长折扣系数：mult = max(0.6, 1-0.4×剩余比例)
var EARLY_PREFILL_MAX := 0.3  # 冷却进度预填上限（仅提前离舰）
var EARLY_PREFILL_RATIO := 0.5
var DEPART_COOLDOWN := 60.0
var DEPART_START_SPEED := 0.0
var DEPART_ACCEL := 540.0
# 母舰驾驶（STAY 期间 WASD，对齐原作 mother_ship_motion）
var DRIVE_ACCEL := 900.0
var DRIVE_MAX_SPEED := 180.0
var DRIVE_MARGIN_X := 130.0
var DRIVE_MARGIN_TOP := 80.0
var DRIVE_MARGIN_BOTTOM := 150.0
# 加特林扫射（向上半球，对齐原作；双塔异周期异相位）
var GATLING_INTERVAL := 0.1333
var GATLING_BULLET_SPEED := 1080.0
var GATLING_DAMAGE := 8
var GATLING_SCORE_SCALE := 1.0 / 3.0
var GATLING_SWEEP_LEFT_MIN := -60.0
var GATLING_SWEEP_LEFT_MAX := 20.0
var GATLING_SWEEP_RIGHT_MIN := -20.0
var GATLING_SWEEP_RIGHT_MAX := 60.0
var GATLING_SWEEP_LEFT_PERIOD := 1.6
var GATLING_SWEEP_RIGHT_PERIOD := 1.8
var GATLING_SWEEP_RIGHT_PHASE := 0.35
# 导弹（对齐原作：0.3s/波、≤5 最近目标、发射定向直线弹 + 溅射）
var MISSILE_INTERVAL := 0.3
var MISSILE_DAMAGE := 80
var MISSILE_SPEED := 600.0
var MISSILE_TARGET_COUNT := 5
var MISSILE_SPLASH_DAMAGE := 20
var MISSILE_SPLASH_RADIUS := 80.0
const GATLING_SFX: AudioStream = preload("res://assets/audio/bullet_fire_b.wav")

var _state: State = State.DESCEND
var _state_timer: float = 0.0
var _depart_speed: float = 0.0
var _player: Player = null
# 加特林
var _gatling_timer: float = 0.0
var _sweep_time: float = 0.0
# 导弹
var _missile_timer: float = 0.0
# 母舰驾驶
var _drive_vel: Vector2 = Vector2.ZERO
# 驻留弹匣
var _mag_cells: int = MAG_CELLS
var _mag_cell_timer: float = 0.0
var _mag_warned: bool = false
var _warn_eject_timer: float = 0.0
# 提前离舰
var _early_timer: float = 0.0
var _hud: Node = null  # 延迟缓存（驻留期每帧刷新进度条用）
var _cooldown_factor: float = 1.0
var _prefill: float = 0.0

@onready var _beam: Polygon2D = $TractorBeam
@onready var _turrets: Array[Node2D] = [$TurretL, $TurretR]


func _ready() -> void:
	# 数值配置缓存（启动一次读入）
	HOVER_Y = GameState.cfg("mothership.hover_y", HOVER_Y)
	RELEASE_INVINCIBLE = GameState.cfg("mothership.release_invincible", RELEASE_INVINCIBLE)
	DOCK_TWEEN_TIME = GameState.cfg("mothership.dock_tween_time", DOCK_TWEEN_TIME)
	DOCK_OFFSET_Y = GameState.cfg("mothership.dock_offset_y", DOCK_OFFSET_Y)
	RESUPPLY_DELAY = GameState.cfg("mothership.resupply_delay", RESUPPLY_DELAY)
	RELEASE_TIME = GameState.cfg("mothership.release_time", RELEASE_TIME)
	RELEASE_DROP = GameState.cfg("mothership.release_drop", RELEASE_DROP)
	MAG_CELLS = GameState.cfg("mothership.mag_cells", MAG_CELLS)
	MAG_CELL_TIME = GameState.cfg("mothership.mag_cell_time", MAG_CELL_TIME)
	MAG_WARN_CELLS = GameState.cfg("mothership.mag_warn_cells", MAG_WARN_CELLS)
	WARN_EJECT_DELAY = GameState.cfg("mothership.warn_eject_delay", WARN_EJECT_DELAY)
	EARLY_HOLD_TIME = GameState.cfg("mothership.early_hold_time", EARLY_HOLD_TIME)
	EARLY_MAX_DISCOUNT = GameState.cfg("mothership.early_max_discount", EARLY_MAX_DISCOUNT)
	EARLY_PREFILL_MAX = GameState.cfg("mothership.early_prefill_max", EARLY_PREFILL_MAX)
	EARLY_PREFILL_RATIO = GameState.cfg("mothership.early_prefill_ratio", EARLY_PREFILL_RATIO)
	DEPART_COOLDOWN = GameState.cfg("mothership.depart_cooldown", DEPART_COOLDOWN)
	DEPART_START_SPEED = GameState.cfg("mothership.depart_start_speed", DEPART_START_SPEED)
	DEPART_ACCEL = GameState.cfg("mothership.depart_accel", DEPART_ACCEL)
	DRIVE_ACCEL = GameState.cfg("mothership.drive.accel", DRIVE_ACCEL)
	DRIVE_MAX_SPEED = GameState.cfg("mothership.drive.max_speed", DRIVE_MAX_SPEED)
	DRIVE_MARGIN_X = GameState.cfg("mothership.drive.margin_x", DRIVE_MARGIN_X)
	DRIVE_MARGIN_TOP = GameState.cfg("mothership.drive.margin_top", DRIVE_MARGIN_TOP)
	DRIVE_MARGIN_BOTTOM = GameState.cfg("mothership.drive.margin_bottom", DRIVE_MARGIN_BOTTOM)
	GATLING_INTERVAL = GameState.cfg("mothership.gatling.interval", GATLING_INTERVAL)
	GATLING_BULLET_SPEED = GameState.cfg("mothership.gatling.bullet_speed", GATLING_BULLET_SPEED)
	GATLING_DAMAGE = GameState.cfg("mothership.gatling.damage", GATLING_DAMAGE)
	GATLING_SCORE_SCALE = GameState.cfg("mothership.gatling.score_scale", GATLING_SCORE_SCALE)
	GATLING_SWEEP_LEFT_MIN = GameState.cfg("mothership.gatling.sweep_left_min", GATLING_SWEEP_LEFT_MIN)
	GATLING_SWEEP_LEFT_MAX = GameState.cfg("mothership.gatling.sweep_left_max", GATLING_SWEEP_LEFT_MAX)
	GATLING_SWEEP_RIGHT_MIN = GameState.cfg("mothership.gatling.sweep_right_min", GATLING_SWEEP_RIGHT_MIN)
	GATLING_SWEEP_RIGHT_MAX = GameState.cfg("mothership.gatling.sweep_right_max", GATLING_SWEEP_RIGHT_MAX)
	GATLING_SWEEP_LEFT_PERIOD = GameState.cfg("mothership.gatling.sweep_left_period", GATLING_SWEEP_LEFT_PERIOD)
	GATLING_SWEEP_RIGHT_PERIOD = GameState.cfg("mothership.gatling.sweep_right_period", GATLING_SWEEP_RIGHT_PERIOD)
	GATLING_SWEEP_RIGHT_PHASE = GameState.cfg("mothership.gatling.sweep_right_phase", GATLING_SWEEP_RIGHT_PHASE)
	MISSILE_INTERVAL = GameState.cfg("mothership.missile.interval", MISSILE_INTERVAL)
	MISSILE_DAMAGE = GameState.cfg("mothership.missile.damage", MISSILE_DAMAGE)
	MISSILE_SPEED = GameState.cfg("mothership.missile.speed", MISSILE_SPEED)
	MISSILE_TARGET_COUNT = GameState.cfg("mothership.missile.target_count", MISSILE_TARGET_COUNT)
	MISSILE_SPLASH_DAMAGE = GameState.cfg("mothership.missile.splash_damage", MISSILE_SPLASH_DAMAGE)
	MISSILE_SPLASH_RADIUS = GameState.cfg("mothership.missile.splash_radius", MISSILE_SPLASH_RADIUS)
	_mag_cells = MAG_CELLS
	_depart_speed = DEPART_START_SPEED


func state_text() -> String:
	match _state:
		State.DESCEND:
			return tr("MS_DESCEND")
		State.HOVER:
			return tr("MS_HOVER")
		State.DOCKING, State.RESUPPLY:
			return tr("MS_DOCKING")
		State.STAY:
			return tr("MS_STAY") % ceili(_mag_cells * MAG_CELL_TIME - _mag_cell_timer)
		State.RELEASE, State.DEPART:
			return tr("MS_LEAVE")
	return ""


func _enter_state(p_state: State) -> void:
	_state = p_state
	_state_timer = 0.0


## 对接点（舰腹正中，玩家/导弹的锚点）
func _dock_point() -> Vector2:
	return global_position + Vector2(0.0, DOCK_OFFSET_Y)


func _physics_process(delta: float) -> void:
	if _beam.visible:
		# 淡光束：低调脉动，不刺眼
		_beam.modulate.a = 0.55 + 0.45 * sin(Time.get_ticks_msec() / 1000.0 * 8.0)
	_state_timer += delta
	match _state:
		State.DESCEND:
			var remaining := HOVER_Y - position.y
			position.y += clampf(remaining * 2.0, 40.0, 160.0) * delta
			if remaining <= 2.0:
				position.y = HOVER_Y
				# 原作无"飞入区域"判定：到位即自动对接（点吸附补间）
				var hud := get_tree().get_first_node_in_group("hud")
				if hud != null:
					hud.show_info_banner(tr("BANNER_MOTHERSHIP_ARRIVED"))
				_start_docking(GameState.player_ref as Player)
		State.HOVER:
			pass  # 兼容保留：自动对接流程下不再经过（见 _start_docking 守卫）
		State.DOCKING:
			if _state_timer >= DOCK_TWEEN_TIME:
				_beam.visible = false  # 对接完成即隐藏牵引光束，否则驻留期一直闪烁
				_enter_state(State.RESUPPLY)
		State.RESUPPLY:
			if _state_timer >= RESUPPLY_DELAY:
				_do_resupply()
				_enter_state(State.STAY)
		State.STAY:
			_update_drive(delta)
			_update_gatling(delta)
			_update_missiles(delta)
			# 弹匣消耗
			_mag_cell_timer += delta
			if _mag_cell_timer >= MAG_CELL_TIME:
				_mag_cell_timer -= MAG_CELL_TIME
				_mag_cells -= 1
				if _mag_cells == MAG_WARN_CELLS and not _mag_warned:
					_mag_warned = true
					_warn_eject_timer = WARN_EJECT_DELAY
					var hud := get_tree().get_first_node_in_group("hud")
					if hud != null:
						hud.show_magazine_warning()
			# 警告横幅播完（5s）强制离舰（对齐原作；自然 20s 到期因此不可达）
			if _mag_warned:
				_warn_eject_timer -= delta
				if _warn_eject_timer <= 0.0:
					_start_release()
			# 提前离舰：长按 H 2s（蓄力进度条经 HUD 显示，松手清零隐藏）
			if Input.is_action_pressed("dock"):
				_early_timer += delta
				if _hud == null:
					_hud = get_tree().get_first_node_in_group("hud")
				if _hud != null:
					_hud.set_early_leave_charge(_early_timer / EARLY_HOLD_TIME)
			else:
				if _early_timer > 0.0 and _hud != null:
					_hud.set_early_leave_charge(-1.0)
				_early_timer = 0.0
			if _early_timer >= EARLY_HOLD_TIME:
				_early_depart()
			elif _mag_cells <= 0:
				_start_release()
		State.RELEASE:
			if _state_timer >= RELEASE_TIME:
				if is_instance_valid(_player) and not _player._dead:
					_player._input_locked = false
					_player._invincible = RELEASE_INVINCIBLE
				_enter_state(State.DEPART)
				departed.emit(DEPART_COOLDOWN * _cooldown_factor * (1.0 - _prefill))
		State.DEPART:
			_depart_speed += DEPART_ACCEL * delta
			position.y -= _depart_speed * delta
			if position.y < GameState.view_world_rect().position.y - 200.0:
				queue_free()


## 驻留期间 WASD 驾驶母舰（对齐原作：加速 900、极速 180、松手即停、边界夹紧），
## 玩家机每帧钉在对接点。
func _update_drive(delta: float) -> void:
	var input_dir := Input.get_vector("move_left", "move_right", "move_up", "move_down")
	if input_dir == Vector2.ZERO:
		_drive_vel = Vector2.ZERO
	else:
		_drive_vel = _drive_vel.move_toward(input_dir * DRIVE_MAX_SPEED, DRIVE_ACCEL * delta)
	position += _drive_vel * delta
	var view := GameState.view_world_rect()
	position = position.clamp(
		view.position + Vector2(DRIVE_MARGIN_X, DRIVE_MARGIN_TOP),
		view.end - Vector2(DRIVE_MARGIN_X, DRIVE_MARGIN_BOTTOM)
	)
	if is_instance_valid(_player) and not _player._dead:
		_player.global_position = _dock_point()


## 场上有效目标（敌机注册表筛掉离场中；Boss 筛掉逃跑中）
func _live_targets() -> Array[Node2D]:
	var out: Array[Node2D] = []
	for node in GameState.enemies:
		var e := node as Node2D
		if e == null:
			continue
		if e is Enemy and e._exiting:
			continue
		if e is Boss and e.is_escaped:
			continue
		out.append(e)
	return out


## 加特林扫射压制（对齐原作）：仅驻留且有目标时开火；双塔向上半球各扫 80°，
## 左塔 [-60°,+20°] 周期 1.6s，右塔 [-20°,+60°] 周期 1.8s 相位 +0.35s（总覆盖 120°）。
func _update_gatling(delta: float) -> void:
	_sweep_time += delta
	_gatling_timer -= delta
	if _gatling_timer > 0.0:
		return
	if _live_targets().is_empty():
		return
	_gatling_timer = GATLING_INTERVAL
	for i in _turrets.size():
		var turret := _turrets[i]
		var angle: float
		if i == 0:
			var center := deg_to_rad((GATLING_SWEEP_LEFT_MIN + GATLING_SWEEP_LEFT_MAX) * 0.5)
			var half := deg_to_rad((GATLING_SWEEP_LEFT_MAX - GATLING_SWEEP_LEFT_MIN) * 0.5)
			angle = center + half * sin(_sweep_time * TAU / GATLING_SWEEP_LEFT_PERIOD)
		else:
			var center := deg_to_rad((GATLING_SWEEP_RIGHT_MIN + GATLING_SWEEP_RIGHT_MAX) * 0.5)
			var half := deg_to_rad((GATLING_SWEEP_RIGHT_MAX - GATLING_SWEEP_RIGHT_MIN) * 0.5)
			angle = center + half * sin((_sweep_time + GATLING_SWEEP_RIGHT_PHASE) * TAU / GATLING_SWEEP_RIGHT_PERIOD)
		var dir := Vector2.UP.rotated(angle)
		turret.global_rotation = dir.angle()
		var b: Bullet = GameState.bullet_pool.fire(dir, GATLING_BULLET_SPEED, GATLING_DAMAGE, true)
		b.score_scale = GATLING_SCORE_SCALE
		b.position = turret.global_position
		# 比玩家弹更细更亮
		b.scale = Vector2(0.6, 0.6)
		b.modulate = Color(1.4, 1.4, 1.1)
		(turret.get_node("MuzzleFlash") as GPUParticles2D).restart()
	GameState.play_sfx(GATLING_SFX, -8.0)


## 导弹齐射（对齐原作）：仅驻留，每 0.3s 一波，锁定距对接点最近的 ≤5 个目标
## （敌机+Boss 混合），发射瞬间定向的直线弹（无追踪），直击 80 + 80px 溅射 20。
func _update_missiles(delta: float) -> void:
	_missile_timer -= delta
	if _missile_timer > 0.0:
		return
	var targets := _live_targets()
	if targets.is_empty():
		return
	_missile_timer = MISSILE_INTERVAL
	var dock := _dock_point()
	targets.sort_custom(func(a: Node2D, b: Node2D) -> bool: return a.global_position.distance_squared_to(dock) < b.global_position.distance_squared_to(dock))
	for t in targets.slice(0, MISSILE_TARGET_COUNT):
		var dir: Vector2 = (t.global_position - dock).normalized()
		if dir == Vector2.ZERO:
			dir = Vector2.UP
		var b: Bullet = GameState.bullet_pool.fire(dir, MISSILE_SPEED, MISSILE_DAMAGE, true)
		b.score_scale = GATLING_SCORE_SCALE
		b.splash_damage = MISSILE_SPLASH_DAMAGE
		b.splash_radius = MISSILE_SPLASH_RADIUS
		b.position = dock
		# 橙体高亮（原作爆炸导弹视觉；精灵随速度方向旋转）
		b.modulate = Color(2.0, 1.1, 0.5)


## 对接开始：锁输入 + 即无敌（对齐原作无敌窗口起点，堵对接/补给空窗）+ 吸附补间
func _start_docking(player: Player) -> void:
	if player == null or player._dead:
		queue_free()  # 玩家不可用（死亡路径）：母舰直接离场，避免 HOVER 死态
		return
	_player = player
	_enter_state(State.DOCKING)
	_player._input_locked = true
	_player.velocity = Vector2.ZERO
	# 无敌窗口起点 = 吸附动画开始（锁输入期间无敌帧不衰减，对齐原作事件驱动无敌）
	_player._invincible = 999.0
	_beam.visible = true
	# 牵引光束吸附到对接点（原作定长补间 1.5s）
	var tween := create_tween()
	tween.tween_property(_player, "global_position", _dock_point(), DOCK_TWEEN_TIME) \
		.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_IN_OUT)


func _do_resupply() -> void:
	if not is_instance_valid(_player) or _player._dead:
		return
	# 回满生命与燃料（重制版增强：原作母舰无补给，回复在基地 RP 交易）
	GameState.heal(GameState.max_health() - GameState.health)
	_player.refill_fuel()
	GameState.play_sfx(GameState.SFX_RESUPPLY)
	GameState.shake(GameState.cfg("effects.shake.mothership", 4.0))
	var hud := get_tree().get_first_node_in_group("hud")
	if hud != null:
		hud.show_popup(tr("POP_RESUPPLY"), global_position + Vector2(0.0, 120.0))


## 提前离舰（长按 H 2s）：冷却双机制折扣——时长 max(0.6, 1-0.4×剩余比例)
## + 进度预填 min(0.3, 0.5×剩余比例)（对齐原作；预填仅此路径）
func _early_depart() -> void:
	var ratio := float(_mag_cells) / float(MAG_CELLS)
	_prefill = minf(EARLY_PREFILL_MAX, EARLY_PREFILL_RATIO * ratio)
	var hud := get_tree().get_first_node_in_group("hud")
	if hud != null:
		hud.set_early_leave_charge(-1.0)
		var factor := maxf(0.6, 1.0 - EARLY_MAX_DISCOUNT * ratio) * (1.0 - _prefill)
		hud.show_popup(tr("POP_EARLY_LEAVE") % int((1.0 - factor) * 100.0), global_position)
	_start_release()


func _start_release() -> void:
	_beam.visible = false
	# 时长折扣对所有离场路径生效（剩余比例越低折扣越小）
	var ratio := clampf(float(_mag_cells) / float(MAG_CELLS), 0.0, 1.0)
	_cooldown_factor = maxf(0.6, 1.0 - EARLY_MAX_DISCOUNT * ratio)
	_enter_state(State.RELEASE)
	if not is_instance_valid(_player) or _player._dead:
		return
	var tween := create_tween()
	tween.tween_property(
		_player, "global_position", _player.global_position + Vector2(0.0, RELEASE_DROP), RELEASE_TIME
	)
