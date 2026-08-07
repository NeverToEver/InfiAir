class_name Mothership
extends Area2D
## 母舰补给平台：长按 H 蓄力召唤（main 管理蓄力）→ 机库小窗演出（main 编排）→
## 穿梭门打开，母舰 DESCEND 穿出减速（缩放+ease-out 滑入停驻点）→ 到位释放减速带
## （冲击波短时减速敌人）并立即以加特林+导弹火力掩护，DOCKING 牵引回收玩家进保护舱
## （隐藏+关受击判定）→ RESUPPLY 补给 → STAY 驻留 20s（弹匣 10 格，2s/格；≤4 格警告，
## 警告 5s 后强制离舰；可长按 H 2s 提前离舰，冷却双机制折扣：时长 max(0.6, 1-0.4×剩余比例)
## + 进度预填 min(0.3, 0.5×剩余比例)）→ RELEASE 释放（玩家出舱恢复显示）→ DEPART 加速离场。
## 无敌窗口：演出/对接开始即无敌（锁输入），弹射结束才解除（释放后 2s 为重制版 QoL）。
## STAY 期间 WASD 直接驾驶母舰，加特林双塔向上 80° 扫射 + 导弹齐射（≤5 目标）。
## 母舰弹丸/导弹击毁只给 1/3 分（score_scale 标记，结算时向下取整）。

signal departed(cooldown: float)

enum State { DESCEND, DOCKING, RESUPPLY, STAY, RELEASE, DEPART }

## G032：母舰贴图基线缩放设计值（tscn 同存 1.25，脚本幂等覆盖 ×ws）
var SHIP_SCALE := 1.25

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
# 穿梭入场（effects.mothership_summon）
var WARP_IN_TIME := 0.8
var WARP_IN_DROP := 260.0  # 穿出下落行程设计值（× world_scale 生效，运行时缓存为已缩放值）
var SLOW_RADIUS := 900.0  # 减速带冲击波扩散半径
var SLOW_DURATION := 2.0  # 敌人减速持续秒数
var SLOW_FACTOR := 0.4  # 敌人位移速度乘区
var SLOW_RING_TIME := 0.5  # 扩散环视觉时长
var SHAKE_SLOW := 4.0
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
## G030：导弹得分系数独立命名（与加特林同为 1/3，分别调参时不误改）
var MISSILE_SCORE_SCALE := 1.0 / 3.0
# 2026-08-04 母舰扩展：火力随里程碑升级（阈值/伤害/射速倍率；默认值与 balance.json 双写）
var _upgrade_threshold: int = 5
var _upgrade_damage_mult: float = 1.5
var _upgrade_interval_mult: float = 0.8
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
var _hud_cache: Node = null  # A5 收敛：HUD 延迟缓存（驻留期每帧刷新进度条用）
var _cooldown_factor: float = 1.0
var _prefill: float = 0.0
# 穿梭入场
var _warp_gate: WarpGate = null
var _warp_from: Vector2 = Vector2.ZERO
var _warp_target: Vector2 = Vector2.ZERO
var _ws: float = 1.0  # world_scale 缓存（_ready 写入，帧内复用）
# 演出附件（_ready 预建，帧内仅属性写，零分配）
var _engine_glow: Sprite2D = null  # 引擎光晕（DESCEND 巨大→常态，DEPART 随加速增大）
var _engine_glow_base: Vector2 = Vector2.ONE  # 「常态」基准缩放
var _descend_trail: GPUParticles2D = null  # 穿出期上冲气流尾迹
var _depart_trail: GPUParticles2D = null  # 离场下喷尾迹
var _beam_fx: Node2D = null  # 牵引光束附件容器（随 _beam.visible 同步显隐）
var _beam_rings: Array[Line2D] = []  # 捕获流环 ×3（自上而下循环）
var _beam_ring_u: PackedFloat32Array = PackedFloat32Array([0.0, 1.0 / 3.0, 2.0 / 3.0])
var _beam_edges: Array[Line2D] = []  # 光束两侧描边（微闪）
var _beam_dust: GPUParticles2D = null  # 光束下端上升尘粒

@onready var _beam: Polygon2D = $TractorBeam
@onready var _turrets: Array[Node2D] = [$TurretL, $TurretR]
## C24 修复：MuzzleFlash 缓存（与 _turrets 同序），开火不再每次 get_node
var _muzzles: Array[GPUParticles2D] = []


func _ready() -> void:
	# L13：注册在场组——事件（精英炮塔/编队）can_trigger 据此互斥：
	# 母舰在场期事件不触发（母舰自动火力会摧毁事件单位并全额发奖，玩家进舱零参与挂机）
	add_to_group("mothership")
	# 数值配置缓存（启动一次读入）
	HOVER_Y = GameState.cfg("mothership.hover_y", HOVER_Y)
	RELEASE_INVINCIBLE = GameState.cfg("mothership.release_invincible", RELEASE_INVINCIBLE)
	DOCK_TWEEN_TIME = GameState.cfg("mothership.dock_tween_time", DOCK_TWEEN_TIME)
	DOCK_OFFSET_Y = GameState.cfg("mothership.dock_offset_y", DOCK_OFFSET_Y) * GameState.world_scale
	RESUPPLY_DELAY = GameState.cfg("mothership.resupply_delay", RESUPPLY_DELAY)
	RELEASE_TIME = GameState.cfg("mothership.release_time", RELEASE_TIME)
	RELEASE_DROP = GameState.cfg("mothership.release_drop", RELEASE_DROP) * GameState.world_scale
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
	# B11 口径澄清：DRIVE_MARGIN_* 乘 world_scale 是有意例外——margin 语义是「舰体边缘到屏边
	# 视觉距离恒定」（舰体缩放后边缘保持同屏距），归类为机体偏移族（乘 ws），
	# 区别于 boss.strafe/hover_band/fight_y 等「中心坐标」屏幕边界族（不乘）。
	DRIVE_MARGIN_X = GameState.cfg("mothership.drive.margin_x", DRIVE_MARGIN_X) * GameState.world_scale
	DRIVE_MARGIN_TOP = GameState.cfg("mothership.drive.margin_top", DRIVE_MARGIN_TOP) * GameState.world_scale
	DRIVE_MARGIN_BOTTOM = GameState.cfg("mothership.drive.margin_bottom", DRIVE_MARGIN_BOTTOM) * GameState.world_scale
	# 2026-08-04 母舰扩展：升级档位配置（阈值/伤害/射速倍率）
	_upgrade_threshold = int(GameState.cfg("mothership.upgrade.threshold", _upgrade_threshold))
	_upgrade_damage_mult = float(GameState.cfg("mothership.upgrade.damage_mult", _upgrade_damage_mult))
	_upgrade_interval_mult = float(GameState.cfg("mothership.upgrade.interval_mult", _upgrade_interval_mult))
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
	WARP_IN_TIME = GameState.cfg("effects.mothership_summon.warp_in_time", WARP_IN_TIME)
	WARP_IN_DROP = GameState.cfg("effects.mothership_summon.warp_in_drop", WARP_IN_DROP) * GameState.world_scale
	SLOW_RADIUS = GameState.cfg("effects.mothership_summon.slow.radius", SLOW_RADIUS)
	SLOW_DURATION = GameState.cfg("effects.mothership_summon.slow.duration", SLOW_DURATION)
	SLOW_FACTOR = GameState.cfg("effects.mothership_summon.slow.factor", SLOW_FACTOR)
	SLOW_RING_TIME = GameState.cfg("effects.mothership_summon.slow.ring_time", SLOW_RING_TIME)
	SHAKE_SLOW = GameState.cfg("effects.mothership_summon.shake_slow", SHAKE_SLOW)
	_mag_cells = MAG_CELLS
	_depart_speed = DEPART_START_SPEED
	# 机体尺寸族：设计值 × 全局缩放（tscn 存母舰基线 1.25，此处幂等覆盖——G032：注释与实际一致）
	var ws: float = GameState.world_scale
	($Sprite2D as Sprite2D).scale = Vector2.ONE * SHIP_SCALE * ws
	var beam_pts := _beam.polygon
	for i in beam_pts.size():
		beam_pts[i] *= ws
	_beam.polygon = beam_pts
	($TurretL as Node2D).position = Vector2(-170.0, 80.0) * ws
	($TurretR as Node2D).position = Vector2(170.0, 80.0) * ws
	_muzzles.clear()
	for turret in _turrets:
		var muzzle := turret.get_node("MuzzleFlash") as GPUParticles2D
		_muzzles.append(muzzle)
		muzzle.position = Vector2(24.0, 0.0) * ws
		var muzzle_mat := muzzle.process_material as ParticleProcessMaterial
		muzzle_mat.scale_min = 1.5 * ws
		muzzle_mat.scale_max = 3.0 * ws
	# 直接实例化（测试/教程，未经 begin_warp_in）：穿梭参数按当前位置补默认
	if _warp_target == Vector2.ZERO:
		_warp_target = Vector2(position.x, HOVER_Y)
		_warp_from = _warp_target + Vector2(0.0, -WARP_IN_DROP)
	_ws = ws
	_build_fx()


## 演出附件预建（帧内只写属性，零分配）：引擎光晕 + 双向尾迹 + 牵引光束附件组
func _build_fx() -> void:
	# 引擎光晕（舰底喷口位）：DESCEND 巨大→常态收敛，DEPART 随加速增大
	_engine_glow = CinematicFx.soft_glow(70.0 * _ws, Color(0.45, 0.85, 1.0, 0.0))
	_engine_glow.position = Vector2(0.0, 85.0 * _ws)
	_engine_glow_base = _engine_glow.scale
	add_child(_engine_glow)
	# 穿出期上冲气流（相对舰体向上冲刷）
	_descend_trail = (
		CinematicFx
		. particles(
			{
				"amount": 48,
				"lifetime": 0.55,
				"direction": Vector3(0.0, -1.0, 0.0),
				"spread": 28.0,
				"vel_min": 320.0 * _ws,
				"vel_max": 640.0 * _ws,
				"scale_min": 8.0 * _ws,
				"scale_max": 18.0 * _ws,
				"color": Color(0.5, 0.85, 1.0, 0.6),
			}
		)
	)
	_descend_trail.position = Vector2(0.0, 30.0 * _ws)
	_descend_trail.emitting = false
	add_child(_descend_trail)
	# 离场下喷尾迹
	_depart_trail = (
		CinematicFx
		. particles(
			{
				"amount": 48,
				"lifetime": 0.6,
				"direction": Vector3(0.0, 1.0, 0.0),
				"spread": 22.0,
				"vel_min": 340.0 * _ws,
				"vel_max": 640.0 * _ws,
				"scale_min": 8.0 * _ws,
				"scale_max": 20.0 * _ws,
				"color": Color(0.55, 0.9, 1.0, 0.65),
			}
		)
	)
	_depart_trail.position = Vector2(0.0, 90.0 * _ws)
	_depart_trail.emitting = false
	add_child(_depart_trail)
	# 牵引光束附件组（随 _beam.visible 同步显隐）
	_beam_fx = Node2D.new()
	_beam_fx.visible = false
	add_child(_beam_fx)
	# 捕获流环 ×3：预建最大半径椭圆点集，帧内仅缩放/位移/透明度
	for i in 3:
		var ring := Line2D.new()
		ring.width = 2.5
		ring.default_color = Color(0.55, 0.95, 1.0)
		ring.points = CinematicFx.ring_points(28, 90.0 * _ws * 0.92, 0.35)
		ring.material = CinematicFx.additive_material()
		_beam_fx.add_child(ring)
		_beam_rings.append(ring)
	# 光束两侧描边（与 TractorBeam 斜边同位，微闪强化轮廓）
	for sx in [-1.0, 1.0]:
		var edge := Line2D.new()
		edge.width = 2.0
		edge.default_color = Color(0.6, 0.95, 1.0)
		edge.points = PackedVector2Array(
			[
				Vector2(40.0 * sx, 60.0) * _ws,
				Vector2(90.0 * sx, 200.0) * _ws,
			]
		)
		edge.material = CinematicFx.additive_material()
		_beam_fx.add_child(edge)
		_beam_edges.append(edge)
	# 光束下端上升尘粒（回收吸附感）
	_beam_dust = (
		CinematicFx
		. particles(
			{
				"amount": 30,
				"lifetime": 0.8,
				"direction": Vector3(0.0, -1.0, 0.0),
				"spread": 35.0,
				"vel_min": 50.0 * _ws,
				"vel_max": 120.0 * _ws,
				"scale_min": 3.5 * _ws,
				"scale_max": 7.0 * _ws,
				"color": Color(0.6, 0.95, 1.0, 0.65),
			}
		)
	)
	_beam_dust.position = Vector2(0.0, 190.0 * _ws)
	_beam_dust.emitting = false
	_beam_fx.add_child(_beam_dust)


## 穿梭入场（召唤序列入口，由 main 在实例化后调用）：母舰从穿梭门门心穿出，
## 缩放 0.25→1 + ease-out 减速滑入停驻点；gate_pos 即最终停驻点。
## 注意：main 在 add_child 前调用本方法（先于 _ready 配置缓存），行程须内联读配置。
func begin_warp_in(gate_pos: Vector2, gate: WarpGate) -> void:
	_warp_gate = gate
	_warp_target = gate_pos
	var drop: float = GameState.cfg("effects.mothership_summon.warp_in_drop", WARP_IN_DROP)
	_warp_from = gate_pos + Vector2(0.0, -drop) * GameState.world_scale
	position = _warp_from
	scale = Vector2.ONE * 0.25
	modulate = Color(1.8, 1.8, 2.2)


func state_text() -> String:
	match _state:
		State.DESCEND:
			return tr("MS_DESCEND")
		State.DOCKING, State.RESUPPLY:
			return tr("MS_DOCKING")
		State.STAY:
			var stay := tr("MS_STAY") % ceili(_mag_cells * MAG_CELL_TIME - _mag_cell_timer)
			if tier() == 1:
				stay += "  " + tr("MS_UPGRADED")
			return stay
		State.RELEASE, State.DEPART:
			return tr("MS_LEAVE")
	return ""


## 对外公开接口（A1 修复）：HUD 轮询读取状态/弹匣，禁止跨类直接写 _ 私有字段
func state() -> State:
	return _state


func mag_cells() -> int:
	return _mag_cells


func set_state_timer(seconds: float) -> void:
	_state_timer = seconds


func mag_cell_timer() -> float:
	return _mag_cell_timer


func mag_warned() -> bool:
	return _mag_warned


func warn_eject_timer() -> float:
	return _warn_eject_timer


func beam() -> Polygon2D:
	return _beam


func set_mag_cell_timer(seconds: float) -> void:
	_mag_cell_timer = seconds


func set_mag_cells(count: int) -> void:
	_mag_cells = count


func set_warn_eject_timer(seconds: float) -> void:
	_warn_eject_timer = seconds


func _enter_state(p_state: State) -> void:
	_state = p_state
	_state_timer = 0.0


## 对接点（舰腹正中，玩家/导弹的锚点）
func _dock_point() -> Vector2:
	return global_position + Vector2(0.0, DOCK_OFFSET_Y)


func _physics_process(delta: float) -> void:
	# 牵引光束附件随 _beam.visible 同步显隐（_start_docking/start_release/对接完成均走此同步）
	if _beam_fx.visible != _beam.visible:
		_beam_fx.visible = _beam.visible
		_beam_dust.emitting = _beam.visible
	if _beam.visible:
		# 淡光束：低调脉动，不刺眼（P2：查表 sin）；时钟取一次，脉动与附件共用
		var now_s := Time.get_ticks_msec() / 1000.0
		_beam.modulate.a = 0.55 + 0.45 * Enemy.sin_fast(now_s * 8.0)
		_update_beam_fx(delta, now_s)
	_state_timer += delta
	match _state:
		State.DESCEND:
			# 穿梭门穿出：缩放 0.25→1 + ease-out 减速滑入停驻点
			var p := clampf(_state_timer / WARP_IN_TIME, 0.0, 1.0)
			var e := 1.0 - pow(1.0 - p, 3.0)
			position = _warp_from.lerp(_warp_target, e)
			scale = Vector2.ONE * lerpf(0.25, 1.0, e)
			modulate = Color(1.8, 1.8, 2.2).lerp(Color.WHITE, e)
			# 引擎制动光晕随同一 ease-out 从巨大收到常态；上冲气流全程伴随
			_descend_trail.emitting = p < 1.0
			_engine_glow.modulate.a = 0.85 * (1.0 - e)
			_engine_glow.scale = _engine_glow_base * lerpf(2.4, 0.8, e)
			if p >= 1.0:
				position = _warp_target
				scale = Vector2.ONE
				modulate = Color.WHITE
				_engine_glow.modulate.a = 0.0
				if _warp_gate != null:
					# H14（健壮性审核）：穿梭门可能先于母舰释放（场景卸载时序不定），防悬挂引用
					if is_instance_valid(_warp_gate):
						_warp_gate.close()
					_warp_gate = null
				_deploy_slow_field()
				var hud := _hud()
				if hud != null:
					hud.show_info_banner(tr("BANNER_MOTHERSHIP_ARRIVED"))
				_start_docking(GameState.player_ref as Player)
		State.DOCKING:
			# 回收牵引期间火力掩护（加特林+导弹，不耗驻留弹匣）
			_update_gatling(delta)
			_update_missiles(delta)
			if _state_timer >= DOCK_TWEEN_TIME:
				_beam.visible = false  # 对接完成即隐藏牵引光束，否则驻留期一直闪烁
				# 回收完成：玩家进保护舱（隐藏+关受击判定，驻留全程保持，RELEASE 出舱）
				if is_instance_valid(_player) and not _player.is_dead():
					_player.enter_pod()
					# 进舱捕获反馈：对接点小冲击环 + 短促软闪
					var sw := (
						CinematicFx
						. shockwave(
							{
								"radius": 120.0 * _ws,
								"time": 0.5,
								"ry_ratio": 0.6,
								"color": Color(0.5, 0.95, 1.0, 0.5),
								"core_color": Color(0.9, 1.0, 1.0, 0.9),
								"width": 8.0,
							}
						)
					)
					sw.position = _dock_point()
					get_parent().add_child(sw)
					_soft_flash(_dock_point(), 70.0 * _ws, Color(0.8, 1.0, 1.0, 0.9))
					var hud := _hud()
					if hud != null:
						hud.show_popup(tr("POD_SECURED"), global_position + Vector2(0.0, 120.0) * GameState.world_scale)
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
					var hud := _hud()
					if hud != null:
						hud.show_magazine_warning()
			# 警告横幅播完（5s）强制离舰（对齐原作；自然 20s 到期因此不可达）
			if _mag_warned:
				_warn_eject_timer -= delta
				if _warn_eject_timer <= 0.0:
					start_release()
			# 提前离舰：长按 H 2s（蓄力进度条经 HUD 显示，松手清零隐藏）
			if Input.is_action_pressed("dock"):
				_early_timer += delta
				var hud := _hud()
				if hud != null:
					hud.set_early_leave_charge(_early_timer / EARLY_HOLD_TIME)
			else:
				if _early_timer > 0.0:
					var hud := _hud()
					if hud != null:
						hud.set_early_leave_charge(-1.0)
				_early_timer = 0.0
			if _early_timer >= EARLY_HOLD_TIME:
				_early_depart()
			elif _mag_cells <= 0:
				start_release()
		State.RELEASE:
			if _state_timer >= RELEASE_TIME:
				if is_instance_valid(_player) and not _player.is_dead():
					_player.unlock_input()
					_player.set_invincible(RELEASE_INVINCIBLE)
				_enter_state(State.DEPART)
				departed.emit(DEPART_COOLDOWN * _cooldown_factor * (1.0 - _prefill))
		State.DEPART:
			_depart_speed += DEPART_ACCEL * delta
			position.y -= _depart_speed * delta
			# 离场加速：引擎光晕随速度增大，下喷尾迹全程伴随
			_depart_trail.emitting = true
			var sp := clampf(_depart_speed / 500.0, 0.0, 1.0)
			_engine_glow.modulate.a = 0.15 + 0.75 * sp
			_engine_glow.scale = _engine_glow_base * (0.7 + 1.6 * sp)
			if position.y < GameState.view_world_rect().position.y - 200.0:
				queue_free()


## 减速带冲击波（穿梭入场到位帧）：短时减速全场敌人（仅位移乘区，duck-typing
## 仅 Enemy/Boss 响应）；视觉为双环冲击波（主环满半径+填充盘，副环尾随），播完自毁
func _deploy_slow_field() -> void:
	GameState.shake(SHAKE_SLOW)
	GameState.play_sfx(GameState.SFX_EXPLOSION_BIG, -10.0, 0.6)
	# 统一实体管理器批量 API：duck-typing 仅 Enemy/Boss 响应（docs/ENTITY_MANAGER.md）
	GameState.for_each_enemy(
		func(e: Node) -> void: e.apply_slow(SLOW_DURATION, SLOW_FACTOR), func(e: Node) -> bool: return e.has_method("apply_slow")
	)
	var sw := (
		CinematicFx
		. shockwave(
			{
				"radius": SLOW_RADIUS,
				"time": SLOW_RING_TIME,
				"color": Color(0.32, 0.93, 0.85, 0.45),
				"core_color": Color(0.75, 1.0, 0.95, 0.85),
				"width": 14.0,
				"fill": true,
			}
		)
	)
	sw.position = position
	get_parent().add_child(sw)
	# 副环：起点比例更大 + 时长更长，读作主环之后的内侧余波
	var echo := (
		CinematicFx
		. shockwave(
			{
				"radius": SLOW_RADIUS,
				"time": SLOW_RING_TIME * 1.4,
				"start_scale": 0.45,
				"color": Color(0.32, 0.93, 0.85, 0.3),
				"core_color": Color(0.7, 1.0, 0.95, 0.6),
				"width": 7.0,
			}
		)
	)
	echo.position = position
	get_parent().add_child(echo)


## 牵引光束附件帧驱动（仅 _beam.visible 时调用，零分配）：
## 捕获流环自窄端向宽端循环流动，两侧描边微闪；now_s 由调用方帧首取一次共用
func _update_beam_fx(delta: float, now_s: float) -> void:
	for i in _beam_rings.size():
		_beam_ring_u[i] = fposmod(_beam_ring_u[i] + delta * 0.55, 1.0)
		var u := _beam_ring_u[i]
		var ring := _beam_rings[i]
		ring.position = Vector2(0.0, lerpf(60.0, 200.0, u) * _ws)
		var k := lerpf(40.0, 90.0, u) / 90.0
		ring.scale = Vector2(k, k)
		ring.modulate.a = 0.75 * Enemy.sin_fast(PI * u)
	for i in _beam_edges.size():
		_beam_edges[i].modulate.a = 0.5 + 0.25 * Enemy.sin_fast(now_s * 9.0 + float(i) * 2.1)


## 一次性软闪（进舱/释放等瞬时反馈）：软光晕快速淡出后自毁
func _soft_flash(pos: Vector2, radius: float, color: Color) -> void:
	var g := CinematicFx.soft_glow(radius, color)
	g.position = pos
	get_parent().add_child(g)
	var tw := g.create_tween()
	tw.tween_property(g, "modulate:a", 0.0, 0.35).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
	tw.tween_callback(g.queue_free)


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
		view.position + Vector2(DRIVE_MARGIN_X, DRIVE_MARGIN_TOP), view.end - Vector2(DRIVE_MARGIN_X, DRIVE_MARGIN_BOTTOM)
	)
	if is_instance_valid(_player) and not _player.is_dead():
		_player.global_position = _dock_point()


## P2：目标数组输出缓冲（_live_targets 复用，免每次调用分配新 Array）
var _targets_buf: Array[Node2D] = []


## 场上有效目标（敌机注册表筛掉离场中；Boss 筛掉逃跑中）
## P2：复用 _targets_buf，调用方仅当帧消费（is_empty/排序后不再保留引用）；
## 2026-08-05 统一实体管理器批量 API 迁移（方法引用零分配，docs/ENTITY_MANAGER.md）
func _live_targets() -> Array[Node2D]:
	_targets_buf.clear()
	GameState.for_each_enemy(_append_live_target, _is_live_target)
	return _targets_buf


## _live_targets 收集回调（for_each_enemy 动作；注册表已跳过失效实例）
func _append_live_target(e: Node) -> void:
	_targets_buf.append(e as Node2D)


## _live_targets 过滤谓词：非 Node2D/离场中的 Enemy/逃跑中的 Boss 排除
func _is_live_target(e: Node) -> bool:
	var n2d := e as Node2D
	if n2d == null:
		return false
	if e is Enemy and (e as Enemy).is_exiting():
		return false
	if e is Boss and (e as Boss).is_escaped:
		return false
	return true


## 2026-08-04 母舰扩展：升级档位——里程碑数 ≥ 阈值即升档（0 或 1）
func tier() -> int:
	return 1 if GameState.milestone_count() >= _upgrade_threshold else 0


func damage_mult() -> float:
	return _upgrade_damage_mult if tier() == 1 else 1.0


func interval_mult() -> float:
	return _upgrade_interval_mult if tier() == 1 else 1.0


## 加特林扫射压制（对齐原作）：驻留（STAY）与回收牵引（DOCKING）期有目标时开火；
## 双塔向上半球各扫 80°，左塔 [-60°,+20°] 周期 1.6s，右塔 [-20°,+60°] 周期 1.8s 相位 +0.35s（总覆盖 120°）。
func _update_gatling(delta: float) -> void:
	_sweep_time += delta
	_gatling_timer -= delta
	if _gatling_timer > 0.0:
		return
	_gatling_timer = GATLING_INTERVAL * interval_mult()  # G027：先置位再判空——空目标不每物理帧分配数组+扫注册表
	if _live_targets().is_empty():
		return
	for i in _turrets.size():
		var turret := _turrets[i]
		var angle: float
		if i == 0:
			var center := deg_to_rad((GATLING_SWEEP_LEFT_MIN + GATLING_SWEEP_LEFT_MAX) * 0.5)
			var half := deg_to_rad((GATLING_SWEEP_LEFT_MAX - GATLING_SWEEP_LEFT_MIN) * 0.5)
			angle = center + half * Enemy.sin_fast(_sweep_time * TAU / GATLING_SWEEP_LEFT_PERIOD)
		else:
			var center := deg_to_rad((GATLING_SWEEP_RIGHT_MIN + GATLING_SWEEP_RIGHT_MAX) * 0.5)
			var half := deg_to_rad((GATLING_SWEEP_RIGHT_MAX - GATLING_SWEEP_RIGHT_MIN) * 0.5)
			angle = center + half * Enemy.sin_fast((_sweep_time + GATLING_SWEEP_RIGHT_PHASE) * TAU / GATLING_SWEEP_RIGHT_PERIOD)
		var dir := Vector2.UP.rotated(angle)
		turret.global_rotation = dir.angle()
		var b: Bullet = GameState.bullet_pool.fire(dir, GATLING_BULLET_SPEED, int(GATLING_DAMAGE * damage_mult()), true)
		b.score_scale = GATLING_SCORE_SCALE
		b.position = turret.global_position
		# 比玩家弹更细更亮（2026-08-06 审计：原 b.scale 连带缩放 Area2D 碰撞形状——
		# 命中半径 6→3.6×ws 判定变严；仅视觉缩放应作用于子 Sprite2D，池化复用自动复位）
		var b_sprite := b.sprite_node()
		if b_sprite != null:
			b_sprite.scale = Vector2(0.6, 0.6)
		b.modulate = Color(1.4, 1.4, 1.1)
		# C24：用缓存的 muzzle 引用（与 _turrets 同序），不再每次 get_node
		if i < _muzzles.size() and _muzzles[i] != null:
			_muzzles[i].restart()
	GameState.play_sfx(GATLING_SFX, -8.0)


## 导弹齐射（对齐原作）：驻留（STAY）与回收牵引（DOCKING）期，每 0.3s 一波，锁定距对接点最近的 ≤5 个目标
## （敌机+Boss 混合），发射瞬间定向的直线弹（无追踪），直击 80 + 80px 溅射 20。
func _update_missiles(delta: float) -> void:
	_missile_timer -= delta
	if _missile_timer > 0.0:
		return
	_missile_timer = MISSILE_INTERVAL * interval_mult()  # G027：先置位再判空——空目标不每物理帧扫描
	var targets := _live_targets()
	if targets.is_empty():
		return
	var dock := _dock_point()
	targets.sort_custom(
		func(a: Node2D, b: Node2D) -> bool: return a.global_position.distance_squared_to(dock) < b.global_position.distance_squared_to(dock)
	)
	for t in targets.slice(0, MISSILE_TARGET_COUNT):
		var dir: Vector2 = (t.global_position - dock).normalized()
		if dir == Vector2.ZERO:
			dir = Vector2.UP
		var b: Bullet = GameState.bullet_pool.fire(dir, MISSILE_SPEED, int(MISSILE_DAMAGE * damage_mult()), true)
		b.score_scale = MISSILE_SCORE_SCALE  # G030：独立常量（原复用 GATLING_SCORE_SCALE 语义混用）
		b.splash_damage = MISSILE_SPLASH_DAMAGE
		b.splash_radius = MISSILE_SPLASH_RADIUS
		b.position = dock
		# 橙体高亮（原作爆炸导弹视觉；精灵随速度方向旋转）
		b.modulate = Color(2.0, 1.1, 0.5)


## 对接开始：锁输入 + 即无敌（对齐原作无敌窗口起点，堵对接/补给空窗）+ 吸附补间
func _start_docking(player: Player) -> void:
	if player == null or player.is_dead():
		queue_free()  # 玩家不可用（死亡路径）：母舰直接离场，避免 HOVER 死态
		return
	_player = player
	_enter_state(State.DOCKING)
	_player.lock_input()
	_player.velocity = Vector2.ZERO
	# 无敌窗口起点 = 吸附动画开始（锁输入期间无敌帧不衰减，对齐原作事件驱动无敌）
	_player.set_invincible(999.0)
	_beam.visible = true
	# 牵引光束吸附到对接点（原作定长补间 1.5s）
	var tween := create_tween()
	tween.tween_property(_player, "global_position", _dock_point(), DOCK_TWEEN_TIME).set_trans(Tween.TRANS_CUBIC).set_ease(
		Tween.EASE_IN_OUT
	)


## A5 收敛（DESIGN_BASELINE §7.1）：HUD 引用统一经延迟缓存获取——hud 是 main.tscn
## 固定层，生命周期内恒定；8 处重复 group 查找收敛为单点缓存。行为与直接查找等价
## （is_instance_valid 守卫：极端重载时序下缓存失效则重新查找）。
func _hud() -> Node:
	if not is_instance_valid(_hud_cache):
		_hud_cache = get_tree().get_first_node_in_group("hud")
	return _hud_cache


func _do_resupply() -> void:
	if not is_instance_valid(_player) or _player.is_dead():
		return
	# 回满生命与燃料（重制版增强：原作母舰无补给，回复在基地 RP 交易）
	GameState.heal(GameState.max_health() - GameState.health)
	_player.refill_fuel()
	GameState.play_sfx(GameState.SFX_RESUPPLY)
	GameState.shake(GameState.cfg("effects.shake.mothership", 4.0))
	var hud := _hud()
	if hud != null:
		hud.show_popup(tr("POP_RESUPPLY"), global_position + Vector2(0.0, 120.0) * GameState.world_scale)


## 提前离舰（长按 H 2s）：冷却双机制折扣——时长 max(0.6, 1-0.4×剩余比例)
## + 进度预填 min(0.3, 0.5×剩余比例)（对齐原作；预填仅此路径）
func _early_depart() -> void:
	var ratio := float(_mag_cells) / float(MAG_CELLS)
	_prefill = minf(EARLY_PREFILL_MAX, EARLY_PREFILL_RATIO * ratio)
	var hud := _hud()
	if hud != null:
		hud.set_early_leave_charge(-1.0)
		var factor := maxf(0.6, 1.0 - EARLY_MAX_DISCOUNT * ratio) * (1.0 - _prefill)
		hud.show_popup(tr("POP_EARLY_LEAVE") % int((1.0 - factor) * 100.0), global_position)
	start_release()


func start_release() -> void:
	# P2：STAY 多入口（警告到期/弹匣耗尽/提前离舰 _early_depart）可能同帧二次触发，
	# 非 STAY 直接短路，令 start_release 幂等
	if _state != State.STAY:
		return
	# E05：所有强制离舰路径（警告到期/弹匣耗尽）统一清 HUD 提前离舰进度条——
	# H 按住时走本路径不复位，进度条残留可见（_early_depart 已有清理，此处兜底全部入口）
	var hud := _hud()
	if hud != null:
		hud.set_early_leave_charge(-1.0)
	_beam.visible = false
	# 时长折扣对所有离场路径生效（剩余比例越低折扣越小）
	var ratio := clampf(float(_mag_cells) / float(MAG_CELLS), 0.0, 1.0)
	_cooldown_factor = maxf(0.6, 1.0 - EARLY_MAX_DISCOUNT * ratio)
	_enter_state(State.RELEASE)
	# 出舱释放反馈：对接点小喷发（一次性，随母舰离场自毁）
	var burst := (
		CinematicFx
		. particles(
			{
				"amount": 20,
				"lifetime": 0.5,
				"explosiveness": 0.9,
				"one_shot": true,
				"direction": Vector3(0.0, 1.0, 0.0),
				"spread": 55.0,
				"vel_min": 70.0 * _ws,
				"vel_max": 200.0 * _ws,
				"scale_min": 2.0 * _ws,
				"scale_max": 5.0 * _ws,
				"color": Color(0.55, 0.95, 1.0, 0.75),
			}
		)
	)
	burst.position = Vector2(0.0, DOCK_OFFSET_Y)
	add_child(burst)
	if not is_instance_valid(_player) or _player.is_dead():
		return
	_player.exit_pod()  # 出舱恢复显示（抛下补间全程可见）
	var tween := create_tween()
	tween.tween_property(_player, "global_position", _player.global_position + Vector2(0.0, RELEASE_DROP), RELEASE_TIME)


func _exit_tree() -> void:
	# 提前收回（返航/对局重置等）：穿梭门关闭兜底；玩家若仍在保护舱则恢复显示
	if _warp_gate != null:
		# H14：穿梭门可能先于母舰释放（场景卸载时序不定），防悬挂引用
		if is_instance_valid(_warp_gate):
			_warp_gate.close()
		_warp_gate = null
	# G011：隐藏 HUD 提前离舰蓄力进度条（E05 只覆盖 start_release 强制离舰路径，返航提前回收漏清）
	var hud := _hud()
	if hud != null and hud.has_method("set_early_leave_charge"):
		hud.set_early_leave_charge(-1.0)
	if is_instance_valid(_player) and not _player.is_dead() and not _player.visible:
		_player.exit_pod()
