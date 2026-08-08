class_name BossAttacks
extends RefCounted
## A3 拆分：Boss 攻击状态机（docs/AUDIT_VAULT.md A3）。
## 承载持续型攻击（狙击 telegraph / 蓄力重炮 / 冲刺掠过 / 编队齐射）的时序状态与轮询；
## 一次性攻击（fan/homing/cross/bullet_wall）在 execute 内直接委托 BossFire。
## 配置字段经 boss 动态访问（无类型参数），弹幕发射经注入的 BossFire，避免跨类私有访问（A1 约束）。

## 对齐 Boss.SweepState（enum { NONE, AIM, DASH, RETURN }）
const SWEEP_NONE := 0
const SWEEP_AIM := 1
const SWEEP_DASH := 2
const SWEEP_RETURN := 3

## 注入：弹幕发射器（Boss._ready 经 configure 传入）与机体缩放
var _fire: BossFire = null
var world_scale: float = 1.0
## 难度分档弹数增量（Boss._apply_difficulty_scaling 写入，供 fan/homing 取用）
var fan_delta: int = 0
var homing_delta: int = 0
var ring_delta: int = 0  # 4 型环弹难度分档绝对值（counts.ring_burst = [10,12,14]，Q01）

# 狙击 telegraph（游击型）
var _aim_line: Line2D = null
var _sniper_aim_elapsed: float = -1.0  # <0 = 无进行中的 telegraph
var _sniper_dir: Vector2 = Vector2.DOWN
var _burst_left: int = 0
var _burst_timer: float = 0.0
var _burst_dir: Vector2 = Vector2.ZERO  # 非零 = telegraph 锁定方向的固定方向爆发
# 蓄力重炮（一型 P2）
var _cannon_elapsed: float = -1.0  # <0 = 无进行中的蓄力
var _cannon_shots_left: int = 0
var _cannon_timer: float = 0.0
var _cannon_flashed: bool = false
# 冲刺掠过（二型 P2）
var _sweep_state: int = SWEEP_NONE
var _sweep_timer: float = 0.0
var _sweep_dir: float = 1.0
var _sweep_origin := Vector2.ZERO
var _sweep_return_target := Vector2.ZERO
var _sweep_dash_y: float = 0.0  # 冲刺横穿高度（AIM 开始时玩家高度快照，与预警线同语义）
var _sweep_drop_x: Array[float] = []
var _sweep_line: Line2D = null
# 编队齐射（三型 P2）
var _volley_minions: Array = []
var _volley_timer: float = 0.0


## 蓄力辉光圆点（过场 _glow 配方：叠加态圆点 + scale/alpha tween）
class GlowDot:
	extends Node2D
	var radius := 8.0
	var dot_color := Color.WHITE

	func _draw() -> void:
		draw_circle(Vector2.ZERO, radius, dot_color)


## 注入发射器与机体缩放（Boss._ready 调用；模式循环重置回调在 Boss 侧）
func configure(fire: BossFire, ws: float) -> void:
	_fire = fire
	world_scale = ws


## 面向玩家的方向（player 为空回退 Vector2.DOWN）
static func _player_dir(from: Node2D) -> Vector2:
	if GameState.player_ref != null:
		return (GameState.player_ref.global_position - from.global_position).normalized()
	return Vector2.DOWN


## A3 收敛：攻击处理器注册表（attack id → 处理器，_init 装配）。
## 新增攻击只需注册一行 + 模式表加 id，不再改 execute 分发本身（O 原则达成）。
var _attack_handlers: Dictionary = {}

## B 梯队（fair plan §8）：每攻击独特 tell——起手音效变体 + 视觉前兆冲击环。
## 玩家凭音效/闪光区分「来的是什么」；音效复用现有资源变体（缺专属资产，登记后续音频项）。
const TELL_FIRE_A: AudioStream = preload("res://assets/audio/bullet_fire.wav")
const TELL_FIRE_B: AudioStream = preload("res://assets/audio/bullet_fire_b.wav")
const TELL_FIRE_C: AudioStream = preload("res://assets/audio/bullet_fire_c.wav")
const TELL_DASH: AudioStream = preload("res://assets/audio/dash.wav")
const TELL_EXPLOSION: AudioStream = preload("res://assets/audio/explosion.wav")
## attack id → tell 配置（sfx 变体/音高/视觉环色）；缺失键 = 该攻击无 tell（新攻击须补配）
const ATTACK_TELLS: Dictionary = {
	&"fan5": {"sfx": TELL_FIRE_A, "pitch": 1.0, "color": Color(1.0, 0.6, 0.2, 0.55)},
	&"fan7": {"sfx": TELL_FIRE_A, "pitch": 1.15, "color": Color(1.0, 0.6, 0.2, 0.55)},
	&"homing": {"sfx": TELL_FIRE_B, "pitch": 1.0, "color": Color(1.0, 0.3, 0.3, 0.55)},
	&"sniper3": {"sfx": TELL_FIRE_C, "pitch": 1.0, "color": Color(0.95, 0.95, 1.0, 0.6)},
	&"cross": {"sfx": TELL_FIRE_A, "pitch": 1.25, "color": Color(0.8, 0.4, 1.0, 0.55)},
	&"charged_cannon": {"sfx": TELL_DASH, "pitch": 0.8, "color": Color(1.0, 0.85, 0.3, 0.6)},
	&"dash_sweep": {"sfx": TELL_EXPLOSION, "pitch": 0.7, "color": Color(0.4, 0.9, 1.0, 0.55)},
	&"minion_volley": {"sfx": TELL_FIRE_C, "pitch": 0.8, "color": Color(0.5, 1.0, 0.5, 0.55)},
	&"bullet_wall": {"sfx": TELL_FIRE_B, "pitch": 1.2, "color": Color(0.4, 0.6, 1.0, 0.55)},
	&"ring_burst": {"sfx": TELL_FIRE_A, "pitch": 1.4, "color": Color(1.0, 0.3, 0.9, 0.55)},
}


func _init() -> void:
	_attack_handlers = {
		&"fan5": _handle_fan5,
		&"fan7": _handle_fan7,
		&"homing": _handle_homing,
		&"sniper3": _handle_sniper3,
		&"cross": _handle_cross,
		&"charged_cannon": _handle_charged_cannon,
		&"dash_sweep": _handle_dash_sweep,
		&"minion_volley": _handle_minion_volley,
		&"bullet_wall": _handle_bullet_wall,
		&"ring_burst": _handle_ring_burst,
	}


## 攻击分发：查表委托（原 10 分支 match；模式表只存 attack id）
func execute(attack: StringName, boss) -> void:
	var handler: Variant = _attack_handlers.get(attack)
	if handler is Callable:
		# B 梯队：起手 tell（音效变体 + 视觉前兆环），玩家可区分「来的是什么」
		_play_tell(attack, boss)
		(handler as Callable).call(boss)
	else:
		push_warning("[BOSS] 未知攻击 id: %s" % attack)


## 起手 tell：音效（独特变体 + 音高）+ 低频视觉冲击环（起手一次性事件，直接实例化可接受）
func _play_tell(attack: StringName, boss) -> void:
	var tell: Variant = ATTACK_TELLS.get(attack)
	if tell == null:
		return
	GameState.play_sfx(tell["sfx"], -8.0, tell["pitch"])
	var ring := (
		CinematicFx
		. shockwave(
			{
				"radius": 26.0,
				"time": 0.22,
				"color": tell["color"],
				"core_color": (tell["color"] as Color).lightened(0.4),
				"width": 5.0,
			}
		)
	)
	ring.position = boss.position
	boss.get_parent().add_child(ring)


## 注册表完整性查询（A3 架构断言测试经公开接口访问）
func has_attack(id: StringName) -> bool:
	return _attack_handlers.has(id)


func attack_ids() -> Array:
	return _attack_handlers.keys()


func _handle_fan5(boss) -> void:
	_fire.fire_fan(boss, maxi(3, 5 + fan_delta), float(boss.FAN_BULLET_SPEED), int(boss.BULLET_DAMAGE_FAN))


func _handle_fan7(boss) -> void:
	_fire.fire_fan(boss, maxi(3, 7 + fan_delta), float(boss.FAN_BULLET_SPEED), int(boss.BULLET_DAMAGE_FAN))


## 4 型「月蚀」ring_burst（2026-08-04）：360° 全圆环弹（难度分档弹数绝对值，counts.ring_burst）
## 2026-08-05 Q01：counts.ring_burst 是每档弹数绝对值（§5.6）——直接消费档值
##（原实现基准 12 上叠加增量 → easy 22/medium 24/hard 26 ≈ 2× 设计密度）
func _handle_ring_burst(boss) -> void:
	(
		_fire
		. fire_ring(
			boss,
			maxi(6, ring_delta),
			float(boss.RING_BURST_SPEED),
			int(boss.BULLET_DAMAGE_RING),
			0.0,
		)
	)


func _handle_homing(boss) -> void:
	# 2026-08-03 审计：难度分档弹数生效（原 homing_delta 只被已删除的死代码 homing2 消费，
	# easy/hard 追踪弹数恒 1；现并入单发路径，多弹横向 80px 散开；medium 档恒单发与原行为一致）
	var count := maxi(1, 1 + homing_delta)
	for i in count:
		(
			_fire
			. fire_homing(
				boss,
				Vector2((float(i) - float(count - 1) * 0.5) * 80.0, 100.0),
				float(boss.HOMING_BULLET_SPEED),
				int(boss.BULLET_DAMAGE_HOMING),
			)
		)


func _handle_sniper3(boss) -> void:
	_start_sniper_volley(boss)


func _handle_cross(boss) -> void:
	_fire.fire_cross(boss, float(boss.CROSS_BULLET_SPEED), int(boss.BULLET_DAMAGE_CROSS))


func _handle_charged_cannon(boss) -> void:
	_start_charged_cannon(boss)


func _handle_dash_sweep(boss) -> void:
	_start_dash_sweep(boss)


func _handle_minion_volley(boss) -> void:
	_start_minion_volley(boss)


func _handle_bullet_wall(boss) -> void:
	_fire.fire_bullet_wall(boss, int(boss.WALL_COUNT), float(boss.WALL_BULLET_SPEED), int(boss.WALL_DAMAGE), float(boss.WALL_ARC_DEG))


## 持续型攻击轮询（sniper telegraph / 3 连发 / 蓄力重炮 / 编队齐射 / 冲刺掠过），Boss._physics_process 调用
func update(delta: float, boss) -> void:
	# 狙击 telegraph：瞄准线前 0.2s 微跟踪玩家后固定，0.35s 到点沿线出弹（§4.2/§5.2）
	if _sniper_aim_elapsed >= 0.0:
		_sniper_aim_elapsed += delta
		if _sniper_aim_elapsed <= float(boss.SNIPER_TRACK_TIME):
			_sniper_dir = _player_dir(boss)
			if _aim_line != null:
				# C23：创建时已 add_point 预置 2 点，set_point_position 原地写（points[i]= 值语义不生效）
				_aim_line.set_point_position(0, _sniper_dir * float(boss.MUZZLE_OFFSET))
				_aim_line.set_point_position(1, _sniper_dir * 1200.0)
		if _aim_line != null:
			_aim_line.modulate.a = 0.18 + 0.18 * absf(Enemy.sin_fast(_sniper_aim_elapsed * 25.0))
		if _sniper_aim_elapsed >= float(boss.SNIPER_AIM_TIME):
			_cancel_aim_line()
			_sniper_aim_elapsed = -1.0
			_burst_left = 3
			_burst_timer = 0.0
			_burst_dir = _sniper_dir
	# 游击型 3 连发狙击（telegraph 锁定时沿固定方向，否则自机狙）
	if _burst_left > 0:
		_burst_timer -= delta
		if _burst_timer <= 0.0:
			_burst_timer = float(boss.SNIPER_BURST_INTERVAL)  # Q30：三连发间隔入库（原硬编码 0.12）
			_burst_left -= 1
			_fire.fire_sniper(boss, _burst_dir, float(boss.SNIPER_BULLET_SPEED), int(boss.BULLET_DAMAGE_SNIPER))
			if _burst_left == 0:
				_burst_dir = Vector2.ZERO
	# 蓄力重炮（一型 P2）：蓄力 0.6s 后 3 连重弹（每发 0.15s 短蓄力闪光）
	if _cannon_elapsed >= 0.0:
		_cannon_elapsed += delta
		if _cannon_elapsed >= float(boss.CANNON_CHARGE):
			_cannon_elapsed = -1.0
			_cannon_shots_left = int(boss.CANNON_SHOTS)
			_cannon_timer = 0.0
			_cannon_flashed = true  # 首发的 telegraph 即 0.6s 蓄力辉光
	if _cannon_shots_left > 0:
		_cannon_timer -= delta
		if not _cannon_flashed and _cannon_timer <= float(boss.CANNON_FLASH):
			_cannon_flashed = true
			charge_glow(boss, float(boss.CANNON_FLASH), 90.0 * world_scale, Color(1.0, 0.7, 0.3, 0.6))
		if _cannon_timer <= 0.0:
			_cannon_timer = float(boss.CANNON_INTERVAL)
			_cannon_shots_left -= 1
			_cannon_flashed = false
			_fire.fire_heavy(boss, _player_dir(boss), float(boss.CANNON_BULLET_SPEED), int(boss.CANNON_DAMAGE))
	# 编队齐射（三型 P2）：横队小怪 0.8s 后齐射一轮自机狙，随后恢复正常 AI
	if _volley_timer > 0.0:
		_volley_timer -= delta
		if _volley_timer <= 0.0:
			minion_volley_fire(boss, _volley_minions)
			_volley_minions.clear()
	_update_sweep(delta, boss)


## 蓄力辉光：叠加态圆点 scale/alpha tween，duration 后自毁（过场 _glow 配方）
func charge_glow(boss, duration: float, radius := -1.0, color := Color(1.0, 0.55, 0.3, 0.55)) -> Node2D:
	if radius < 0.0:
		radius = 70.0 * world_scale  # 默认辉光半径设计值 × 全局缩放
	var dot := GlowDot.new()
	dot.radius = radius
	dot.dot_color = color
	var mat := CanvasItemMaterial.new()
	mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	dot.material = mat
	dot.scale = Vector2.ONE * 0.3
	dot.modulate.a = 0.0
	boss.add_child(dot)
	var tween := dot.create_tween()
	tween.set_parallel(true)
	tween.tween_property(dot, "scale", Vector2.ONE, duration * 0.6)
	tween.tween_property(dot, "modulate:a", 1.0, duration * 0.4)
	tween.chain().tween_property(dot, "modulate:a", 0.0, duration * 0.4)
	tween.tween_callback(dot.queue_free)
	return dot


## 瞄准线：α0.3 闪烁细线（闪烁由 update 驱动），出弹/中断即毁
func _make_aim_line(boss, dir: Vector2, length: float, color := Color(1.0, 0.35, 0.3, 0.9)) -> Line2D:
	var line := Line2D.new()
	line.width = 2.0
	line.default_color = color
	line.modulate.a = 0.3
	line.add_point(dir * float(boss.MUZZLE_OFFSET))
	line.add_point(dir * length)
	boss.add_child(line)
	return line


func _cancel_aim_line() -> void:
	if _aim_line != null:
		_aim_line.queue_free()
		_aim_line = null


## 狙击 3 连发 telegraph 起手：瞄准线随玩家微跟踪 0.2s 后固定，0.35s 到点沿线出弹
func _start_sniper_volley(boss) -> void:
	if _sniper_aim_elapsed >= 0.0:
		return  # 已有进行中的 telegraph（间隔短于 telegraph 时不叠加）
	_sniper_aim_elapsed = 0.0
	_sniper_dir = _player_dir(boss)
	_aim_line = _make_aim_line(boss, _sniper_dir, 1200.0)


## 蓄力重炮（一型 P2）：0.6s 蓄力辉光起手，连发由 update 驱动
func _start_charged_cannon(boss) -> void:
	if _cannon_elapsed >= 0.0 or _cannon_shots_left > 0:
		return
	_cannon_elapsed = 0.0
	charge_glow(boss, float(boss.CANNON_CHARGE))


## 冲刺掠过（二型 P2）：0.5s 水平瞄准线（预警横穿玩家当前高度）起手
func _start_dash_sweep(boss) -> void:
	if _sweep_state != SWEEP_NONE:
		return
	_sweep_state = SWEEP_AIM
	_sweep_timer = float(boss.SWEEP_AIM)
	# C14：默认方向/高度取可见世界中心，不写死 960/300
	var view := GameState.view_world_rect()
	var player_x: float = view.get_center().x
	var dy: float = view.get_center().y - boss.position.y
	if GameState.player_ref != null:
		player_x = GameState.player_ref.global_position.x
		dy = GameState.player_ref.global_position.y - boss.position.y
	# 横穿高度 = 预警线所在高度（玩家 AIM 开始时的 y 快照，DASH 阶段据此落位）
	_sweep_dash_y = boss.position.y + dy
	_sweep_dir = signf(player_x - boss.position.x)
	if _sweep_dir == 0.0:
		_sweep_dir = 1.0
	_sweep_origin = boss.position
	_cancel_sweep_line()
	_sweep_line = Line2D.new()
	_sweep_line.width = 2.0
	_sweep_line.default_color = Color(1.0, 0.35, 0.3, 0.9)
	_sweep_line.modulate.a = 0.3
	# 预警线跨度覆盖可见区全宽（zoom 加宽也不露边）
	var span := view.size.x * 0.6
	_sweep_line.add_point(Vector2(-span, dy))
	_sweep_line.add_point(Vector2(span, dy))
	boss.add_child(_sweep_line)


## 冲刺掠过驱动：AIM（瞄准线闪烁）→ DASH（高速横穿 + 等距拖 3 枚减速弹）
## → RETURN（smoothstep 飞回巡航位，复用狂暴 RETURN 插值模式）
func _update_sweep(delta: float, boss) -> void:
	match _sweep_state:
		SWEEP_AIM:
			_sweep_timer -= delta
			if _sweep_line != null:
				_sweep_line.modulate.a = 0.18 + 0.18 * absf(Enemy.sin_fast(_sweep_timer * 25.0))
			if _sweep_timer <= 0.0:
				_cancel_sweep_line()
				_sweep_state = SWEEP_DASH
				# 横穿落位到玩家高度（AIM 开始时快照，与预警线同语义）；RETURN 复用锚线回位逻辑
				boss.position.y = _sweep_dash_y
				# 拖弹点：横穿路径 1/4、1/2、3/4 处
				var bounds: Vector2 = boss.strafe_range()
				var end_x := bounds.y if _sweep_dir > 0.0 else bounds.x
				_sweep_drop_x.clear()
				for i in int(boss.SWEEP_DROP_COUNT):
					_sweep_drop_x.append(lerpf(boss.position.x, end_x, float(i + 1) / float(int(boss.SWEEP_DROP_COUNT) + 1)))
		SWEEP_DASH:
			boss.position.x += _sweep_dir * float(boss.SWEEP_SPEED) * float(boss.slow_factor()) * delta
			while not _sweep_drop_x.is_empty():
				var drop_x: float = _sweep_drop_x[0]
				if (_sweep_dir > 0.0 and boss.position.x >= drop_x) or (_sweep_dir < 0.0 and boss.position.x <= drop_x):
					_sweep_drop_x.remove_at(0)
					var b = GameState.bullet_pool.Fire(Vector2.DOWN, float(boss.SWEEP_DROP_SPEED), int(boss.SWEEP_DROP_DAMAGE), false)
					if b == null:
						break  # P2-3：同屏敌弹硬上限——跳出本轮撒弹（cap 持续期剩余 drop 下轮重试，防死循环）
					b.position = boss.position + Vector2(0.0, 60.0) * world_scale
				else:
					break
			var bounds: Vector2 = boss.strafe_range()
			if (_sweep_dir > 0.0 and boss.position.x >= bounds.y) or (_sweep_dir < 0.0 and boss.position.x <= bounds.x):
				boss.position.x = clampf(boss.position.x, bounds.x, bounds.y)
				_sweep_state = SWEEP_RETURN
				_sweep_timer = float(boss.SWEEP_RETURN_DURATION)
				_sweep_origin = boss.position
				# C14：返回目标 x 取可见世界中心，不写死 960（zoom 加宽时仍居中）
				_sweep_return_target = Vector2(
					clampf(GameState.view_world_rect().get_center().x, bounds.x, bounds.y), boss.fight_anchor_y()
				)
		SWEEP_RETURN:
			_sweep_timer -= delta
			var t := clampf(1.0 - _sweep_timer / float(boss.SWEEP_RETURN_DURATION), 0.0, 1.0)
			var eased := t * t * (3.0 - 2.0 * t)
			boss.position = _sweep_origin.lerp(_sweep_return_target, eased)
			if _sweep_timer <= 0.0:
				_sweep_state = SWEEP_NONE
				boss.reset_fire_timer()


func _cancel_sweep_line() -> void:
	if _sweep_line != null:
		_sweep_line.queue_free()
		_sweep_line = null


## 序列中断清理：瞄准线/拖弹点/状态复位（位置由调用方接管）
func _cancel_sweep() -> void:
	_cancel_sweep_line()
	_sweep_state = SWEEP_NONE
	_sweep_drop_x.clear()


## 编队齐射（三型 P2）：召唤 VOLLEY_COUNT 小怪列横队（meta 标记），0.8s 后齐射由 update 驱动
func _start_minion_volley(boss) -> void:
	if _volley_timer > 0.0:
		return  # R07：进行中守卫（L 系列防御缺口登记遗留）——待发期间重复触发清空重召
	_volley_minions.clear()
	for i in int(boss.VOLLEY_COUNT):
		var e: Enemy = boss.spawn_minion_at(
			boss.position + Vector2((float(i) - float(int(boss.VOLLEY_COUNT) - 1) * 0.5) * 100.0, 110.0) * world_scale
		)
		if e != null:
			e.set_meta("hive_volley", true)
			_volley_minions.append(e)
	_volley_timer = float(boss.VOLLEY_DELAY)


## 齐射一轮自机狙（普通敌弹口径；P2 编队与狂暴倾巢收尾共用）
func minion_volley_fire(boss, minions: Array) -> void:
	if GameState.player_ref == null:
		return
	for e in minions:
		if is_instance_valid(e) and e.is_active():
			var dir: Vector2 = (GameState.player_ref.global_position - e.global_position).normalized()
			# H10（健壮性审核）：玩家与僚机重合时零向量回退（防静止弹，G026 同族）
			if dir == Vector2.ZERO:
				dir = Vector2.DOWN
			var b = GameState.bullet_pool.Fire(dir, float(boss.VOLLEY_BULLET_SPEED), int(boss.VOLLEY_BULLET_DAMAGE), false)
			if b == null:
				continue  # P2-3：同屏敌弹硬上限，跳过该僚机本轮齐射
			b.position = e.position + dir * 40.0 * world_scale


## 冲刺掠过进行中（Boss._physics_process 据此决定移动接管）
func is_sweep_active() -> bool:
	return _sweep_state != SWEEP_NONE


## 状态查询（测试/诊断白盒断言经公开接口，A3）
func sweep_state() -> int:
	return _sweep_state


func cannon_elapsed() -> float:
	return _cannon_elapsed


func aim_line() -> Line2D:
	return _aim_line


func sweep_line() -> Line2D:
	return _sweep_line


## 单独取消瞄准线（狂暴 ACTIVE 各型复用；cancel_all 用于整体中断）
func cancel_aim_line() -> void:
	_cancel_aim_line()


## 创建瞄准线（狂暴 ACTIVE 2 型「猎杀环绕」复用）
func make_aim_line(boss, dir: Vector2, length: float) -> Line2D:
	return _make_aim_line(boss, dir, length)


## 常规攻击全部中断清理（Boss._enter_phase/_enrage/_abort 调用）
func cancel_all() -> void:
	_cancel_aim_line()
	_sniper_aim_elapsed = -1.0
	_burst_left = 0
	_burst_dir = Vector2.ZERO
	_cancel_sweep()
	_cannon_elapsed = -1.0
	_cannon_shots_left = 0
	_volley_timer = 0.0
	_volley_minions.clear()
