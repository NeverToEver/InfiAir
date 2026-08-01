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


## 攻击分发：模式表只存 attack id（原 Boss._execute_attack）
func execute(attack: StringName, boss) -> void:
	match attack:
		&"fan5":
			_fire.fire_fan(boss, maxi(3, 5 + fan_delta), float(boss.FAN_BULLET_SPEED), int(boss.BULLET_DAMAGE_FAN))
		&"fan7":
			_fire.fire_fan(boss, maxi(3, 7 + fan_delta), float(boss.FAN_BULLET_SPEED), int(boss.BULLET_DAMAGE_FAN))
		&"homing":
			_fire.fire_homing(boss, Vector2(0.0, 100.0), float(boss.HOMING_BULLET_SPEED), int(boss.BULLET_DAMAGE_HOMING))
		&"homing2":
			var homing_count: int = maxi(1, 2 + homing_delta)
			for i in homing_count:
				_fire.fire_homing(boss, Vector2((float(i) - float(homing_count - 1) * 0.5) * 80.0, 100.0), float(boss.HOMING_BULLET_SPEED), int(boss.BULLET_DAMAGE_HOMING))
		&"sniper3":
			_start_sniper_volley(boss)
		&"cross":
			_fire.fire_cross(boss, float(boss.CROSS_BULLET_SPEED), int(boss.BULLET_DAMAGE_CROSS))
		&"charged_cannon":
			_start_charged_cannon(boss)
		&"dash_sweep":
			_start_dash_sweep(boss)
		&"minion_volley":
			_start_minion_volley(boss)
		&"bullet_wall":
			_fire.fire_bullet_wall(boss, int(boss.WALL_COUNT), float(boss.WALL_BULLET_SPEED), int(boss.WALL_DAMAGE), float(boss.WALL_ARC_DEG))
		_:
			push_warning("[BOSS] 未知攻击 id: %s" % attack)


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
			_burst_timer = 0.12
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
					var b: Bullet = GameState.bullet_pool.fire(Vector2.DOWN, float(boss.SWEEP_DROP_SPEED), int(boss.SWEEP_DROP_DAMAGE), false)
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
				_sweep_return_target = Vector2(clampf(GameState.view_world_rect().get_center().x, bounds.x, bounds.y), boss.fight_anchor_y())
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
			var b: Bullet = GameState.bullet_pool.fire(dir, float(boss.VOLLEY_BULLET_SPEED), int(boss.VOLLEY_BULLET_DAMAGE), false)
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
