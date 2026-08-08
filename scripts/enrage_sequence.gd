class_name EnrageSequence
extends RefCounted
## M3b：Enemy 迁 C#，经脚本资源调用静态方法/判型（gdlint class-load-variable-name：snake_case）
var _enemy_script := load("res://csharp/godot/Enemy.cs")
## A3 拆分：Boss 狂暴状态机（docs/AUDIT_VAULT.md A3）。
## 狂暴 5 子状态机（TRANSITION→ACTIVE→RELEASE_HOLD→RETURN→NONE）+ 四型差异化 ACTIVE +
## 轨道路径计算 + 锁血/玩家减速。经 boss 动态访问配置与位置（无类型参数），
## 弹幕发射经注入 BossFire/BossAttacks，避免跨类私有访问（A1 约束）。

## 对齐 Boss.EnragePhase（enum { NONE, TRANSITION, ACTIVE, RELEASE_HOLD, RETURN }）
const ENRAGE_NONE := 0
const ENRAGE_TRANSITION := 1
const ENRAGE_ACTIVE := 2
const ENRAGE_RELEASE_HOLD := 3
const ENRAGE_RETURN := 4

## 猎杀环绕瞬停点（右→上→左→下→右→上，共 6 点；末点为顶部，RELEASE 回底部）
const STALKER_POINT_ANGLES_DEG: Array[float] = [0.0, -90.0, 180.0, 90.0, 0.0, -90.0]

## 注入：弹幕发射器 / 攻击状态机 / 机体缩放（Boss._ready 经 configure 传入）
var _fire: BossFire = null
var _attacks: BossAttacks = null
var world_scale: float = 1.0

# 狂暴序列状态（计时单位均为游戏秒，随 time_scale 缩放）
var _phase: int = ENRAGE_NONE
var _timer: float = 0.0  # TRANSITION+ACTIVE 剩余（progress 驱动轨道）
var _transition_timer: float = 0.0
var _release_hold_timer: float = 0.0
var _return_timer: float = 0.0
var _attack_timer: float = 0.0
var _attack_index: int = 0
## 锁血：触发→RELEASE_HOLD 开始前 HP 锁定在 30% 检查点（任何伤害不掉血不死）
var _health_lock: bool = false
var _snapshot_target := Vector2.ZERO  # 触发时玩家位置快照（轨道中心）
var _transition_origin := Vector2.ZERO
var _return_origin := Vector2.ZERO
var _return_target := Vector2.ZERO
var _slowed_player = null  # 被施加狂暴减速的玩家（用于精确复位；M3c：Player 迁 C#，注解 untyped）
var _boss_size := Vector2(328.0, 328.0)  # 贴图有效尺寸（begin 传入，算轨道半径）
# 差异化狂暴各型状态
var _ring_angle: float = 0.0  # 1 型环弹起始角（随波次进动）
var _summon_timer: float = 0.0  # 3 型倾巢召唤计时
var _summon_waves: int = 0  # 3 型已放小怪波数
var _aim_elapsed: float = -1.0  # 2 型逐点瞄准计时（<0 = 未瞄准）
var _release_salvo_done: bool = false  # 1/2 型 RELEASE 一次性收尾已结算
var _release_origin := Vector2.ZERO  # 2 型 RELEASE 回轨道底部起点
var _aim_line: Line2D = null
var _sniper_dir: Vector2 = Vector2.DOWN

## A3 收敛：狂暴各阶段机型处理器注册表（boss_type → 处理器，_init 装配）。
## 新增机型只需注册一行 + 一个处理器方法，不再改 update/_begin_release_hold 的 match（O 原则达成）。
var _active_handlers: Dictionary = {}
var _release_handlers: Dictionary = {}
var _release_begin_handlers: Dictionary = {}

## A3 机型参数表：TRANSITION 阶段悬停原地不滑入轨道（1 型「旋转堡垒」专属）
const TRANSITION_HOVER_TYPES: Dictionary = {1: true, 4: true}


func _init() -> void:
	_active_handlers = {
		1: _active_bulwark,
		2: _active_stalker,
		3: _active_hive,
		4: _active_eclipse,
	}
	_release_handlers = {
		1: _release_bulwark,
		2: _release_stalker,
		3: _release_hive,
		4: _release_eclipse,
	}
	_release_begin_handlers = {
		1: _release_begin_bulwark,
		2: _release_begin_stalker,
		3: _release_begin_hive,
		4: _release_begin_eclipse,
	}


## 注册表完整性查询（A3 架构断言测试经公开接口访问）
func has_active_handler(type: int) -> bool:
	return _active_handlers.has(type)


func has_release_handler(type: int) -> bool:
	return _release_handlers.has(type)


func has_release_begin_handler(type: int) -> bool:
	return _release_begin_handlers.has(type)


func configure(fire: BossFire, attacks: BossAttacks, ws: float) -> void:
	_fire = fire
	_attacks = attacks
	world_scale = ws


func is_active() -> bool:
	return _phase != ENRAGE_NONE


## 状态查询（测试/诊断白盒断言经公开接口，A3）
func phase() -> int:
	return _phase


## 1/2 型 RELEASE 一次性收尾是否已结算（测试观测；不依赖弹在场时序——
## 8 路齐射向上路 ~0.27s 即出屏，场上计数在慢 runner 上与发射时刻竞争失败）
func release_salvo_done() -> bool:
	return _release_salvo_done


func attack_index() -> int:
	return _attack_index


func ring_angle() -> float:
	return _ring_angle


func summon_waves() -> int:
	return _summon_waves


func snapshot_target() -> Vector2:
	return _snapshot_target


func aim_line() -> Line2D:
	return _aim_line


## 释放本类持有的瞄准线（B1 修复）。`BossAttacks.make_aim_line` 创建的 Line2D
## 只由本类 `_aim_line` 持有，`BossAttacks.cancel_aim_line()` 仅清其自身 `_aim_line`，
## 到不了这里——不显式清理会残留静态瞄准线并泄漏节点（每次 2 型狂暴约 6 个）。
func _free_aim_line() -> void:
	if _aim_line != null:
		_aim_line.queue_free()
		_aim_line = null


## 狂暴触发初始化（Boss._enrage 调用：数据 + 锁血 + 玩家减速；表现由 Boss 侧负责）
func begin(boss, snapshot_pos: Vector2, boss_size: Vector2) -> void:
	_snapshot_target = snapshot_pos
	_boss_size = boss_size
	_ring_angle = 0.0
	_summon_waves = 0
	_summon_timer = float(boss.E3_SUMMON_INTERVAL)
	_aim_elapsed = -1.0
	_release_salvo_done = false
	_health_lock = true
	_phase = ENRAGE_TRANSITION
	_timer = float(boss.ENRAGE_DURATION)
	_transition_timer = float(boss.ENRAGE_TRANSITION_DURATION)
	_transition_origin = boss.position
	_lock_player_movement(boss)


## 序列中断（逃跑/死亡/离场/教程收尾）：清状态 + 解血锁 + 复位减速 + 清 telegraph，幂等
func abort() -> void:
	_phase = ENRAGE_NONE
	_health_lock = false
	_attacks.cancel_aim_line()
	_free_aim_line()
	_aim_elapsed = -1.0
	_unlock_player_movement()


## 兜底解锁（Boss._exit_tree 调用：任何离场路径不留玩家减速残留）
func unlock_player() -> void:
	_unlock_player_movement()


## 狂暴进行中是否锁血（Boss.take_damage 查询）
func is_health_locked() -> bool:
	return _health_lock


## 狂暴序列驱动：TRANSITION（蓄力抖动滑入轨道，1 型悬停原地）→ ACTIVE（各型差异化攻击）
## → RELEASE_HOLD（各型收尾爆发，§5.4 峰值）→ RETURN（飞回战斗位）→ NONE（常规「余怒」循环）
func update(delta: float, boss) -> void:
	match _phase:
		ENRAGE_TRANSITION:
			_timer = maxf(_timer - delta, 0.0)
			_transition_timer -= delta
			var t := clampf(1.0 - _transition_timer / float(boss.ENRAGE_TRANSITION_DURATION), 0.0, 1.0)
			var eased := 1.0 - pow(1.0 - t, 3.0)
			var shake := Vector2(
				_enemy_script.SinFast(t * TAU * 7.0) * (1.0 - t) * 13.0, _enemy_script.CosFast(t * TAU * 5.0) * (1.0 - t) * 8.0
			)  # M3b：Enemy 迁 C#，静态经脚本资源
			# 机型参数表：1 型「旋转堡垒」悬停原地，不滑入轨道
			var target_pos := _transition_origin if _hover_in_transition(boss) else _path_center(_progress(boss), boss)
			boss.position = _transition_origin.lerp(target_pos, eased) + shake
			if _transition_timer <= 0.0:
				_phase = ENRAGE_ACTIVE
				_attack_timer = float(boss.ENRAGE_ATTACK_WINDUP)
				_attack_index = 0
		ENRAGE_ACTIVE:
			_timer = maxf(_timer - delta, 0.0)
			var active: Variant = _active_handlers.get(int(boss.boss_type))
			if active is Callable:
				(active as Callable).call(delta, boss)
			else:
				_active_fallback(delta, boss)
			if _timer <= 0.0:
				_begin_release_hold(boss)
		ENRAGE_RELEASE_HOLD:
			_release_hold_timer -= delta
			var release: Variant = _release_handlers.get(int(boss.boss_type))
			if release is Callable:
				(release as Callable).call(delta, boss)
			else:
				_release_fallback(delta, boss)
			if _release_hold_timer <= 0.0:
				_begin_return(boss)
		ENRAGE_RETURN:
			_return_timer -= delta
			var t := clampf(1.0 - _return_timer / float(boss.ENRAGE_RETURN_DURATION), 0.0, 1.0)
			var eased := t * t * (3.0 - 2.0 * t)
			boss.position = _return_origin.lerp(_return_target, eased)
			if _return_timer <= 0.0:
				_phase = ENRAGE_NONE


## 1 型「旋转堡垒」ACTIVE：悬停原地，每 0.5s 一波 12 向环弹（起始角随波次进动）
func _active_bulwark(delta: float, boss) -> void:
	_attack_timer -= delta
	if _attack_timer <= 0.0:
		_attack_timer = float(boss.E1_RING_INTERVAL)
		_fire.fire_ring(boss, int(boss.E1_RING_COUNT), float(boss.E1_RING_SPEED), int(boss.BULLET_DAMAGE_SNAPSHOT_RING), _ring_angle)
		_ring_angle += deg_to_rad(float(boss.E1_RING_PRECESSION_DEG))
		_attack_index += 1


## 2 型「猎杀环绕」ACTIVE：轨道 4 象限 6 点依次瞬停，每点 0.3s 瞄准线 + 单发狙
func _active_stalker(delta: float, boss) -> void:
	if _aim_elapsed >= 0.0:
		_aim_elapsed += delta
		_sniper_dir = _player_dir(boss)
		if _aim_line != null:
			# C23：创建时已 add_point 预置 2 点，set_point_position 原地写（points[i]= 值语义不生效）
			_aim_line.set_point_position(0, _sniper_dir * float(boss.MUZZLE_OFFSET))
			_aim_line.set_point_position(1, _sniper_dir * 1200.0)
			_aim_line.modulate.a = 0.18 + 0.18 * absf(_enemy_script.SinFast(_aim_elapsed * 25.0))  # M3b：Enemy 迁 C#，静态经脚本资源
		if _aim_elapsed >= float(boss.E2_AIM):
			_free_aim_line()
			_aim_elapsed = -1.0
			_fire.fire_heavy(boss, _sniper_dir, float(boss.E2_SNIPER_SPEED), int(boss.E2_SNIPER_DAMAGE))
	_attack_timer -= delta
	if _attack_timer <= 0.0 and _attack_index < int(boss.E2_POINT_COUNT):
		var angle := deg_to_rad(STALKER_POINT_ANGLES_DEG[_attack_index % STALKER_POINT_ANGLES_DEG.size()])
		# M3b：Enemy 迁 C#，静态经脚本资源
		boss.position = (_snapshot_target + Vector2(_enemy_script.CosFast(angle), _enemy_script.SinFast(angle)) * _path_radius(boss))
		_attack_index += 1
		_attack_timer = float(boss.E2_POINT_INTERVAL)
		_attacks.cancel_aim_line()
		_free_aim_line()
		_aim_elapsed = 0.0
		_sniper_dir = _player_dir(boss)
		_aim_line = _attacks.make_aim_line(boss, _sniper_dir, 1200.0)


## 3 型「倾巢」ACTIVE：共用轨道环绕 + 每 1.2s 一波 3 小怪（共 3 波）+ 每 0.9s 一圈 8 向环弹
func _active_hive(delta: float, boss) -> void:
	boss.position = _path_center(_progress(boss), boss)
	_attack_timer -= delta
	if _attack_timer <= 0.0:
		_attack_timer = float(boss.E3_RING_INTERVAL)
		_fire.fire_ring(boss, int(boss.E3_RING_COUNT), float(boss.E3_RING_SPEED), int(boss.BULLET_DAMAGE_SNAPSHOT_RING), 0.0)
		_attack_index += 1
	if _summon_waves < int(boss.E3_SUMMON_WAVES):
		_summon_timer -= delta
		if _summon_timer <= 0.0:
			_summon_timer = float(boss.E3_SUMMON_INTERVAL)
			_summon_waves += 1
			for i in int(boss.E3_SUMMON_COUNT):
				boss.spawn_minion_at(boss.position + Vector2(randf_range(-80.0, 80.0), 110.0) * world_scale)


## 4 型「月蚀」ACTIVE（2026-08-04）：中心悬停 + 双环反向进动——正环 + 反角环
## 交替成波（各环起始角 ±_ring_angle，每波进动 E4_PRECESSION_DEG）
func _active_eclipse(delta: float, boss) -> void:
	_attack_timer -= delta
	if _attack_timer <= 0.0:
		_attack_timer = float(boss.E4_RING_INTERVAL)
		var angle := deg_to_rad(float(boss.E4_PRECESSION_DEG)) * _attack_index
		_fire.fire_ring(boss, int(boss.E4_RING_COUNT), float(boss.E4_RING_SPEED), int(boss.BULLET_DAMAGE_SNAPSHOT_RING), angle)
		_fire.fire_ring(boss, int(boss.E4_RING_COUNT), float(boss.E4_RING_SPEED), int(boss.BULLET_DAMAGE_SNAPSHOT_RING), -angle)
		_attack_index += 1


## 4 型「月蚀」释放——无持续结算（环阵已在 RELEASE_BEGIN 一次性结算）
func _release_eclipse(_delta: float, _boss) -> void:
	pass


## 4 型「月蚀」释放起手——蓄力环阵（20 向快慢双环）
func _release_begin_eclipse(boss) -> void:
	_fire.fire_ring(boss, int(boss.E4_RELEASE_RING_COUNT), float(boss.E4_RELEASE_RING_SPEED), int(boss.BULLET_DAMAGE_SNAPSHOT_RING), 0.0)


## 序列进度 0→1（TRANSITION 起算，ACTIVE 结束到 1；对齐原作 enrage_progress）
func _progress(boss) -> float:
	return clampf(1.0 - _timer / float(boss.ENRAGE_DURATION), 0.0, 1.0)


## 轨道半径：max(机体宽,高)×1.5，受屏幕边界约束（对齐原作 enrage_path_radius，下限 24）
func _path_radius(boss) -> float:
	var base := maxf(_boss_size.x, _boss_size.y) * float(boss.ENRAGE_PATH_RADIUS_SCALE)
	var view := GameState.view_world_rect()
	var half := _boss_size * 0.5
	var max_radius := maxf(
		24.0,
		minf(
			minf(_snapshot_target.x - view.position.x - half.x, view.end.x - _snapshot_target.x - half.x),
			minf(_snapshot_target.y - view.position.y - half.y, view.end.y - _snapshot_target.y - half.y)
		)
	)
	return minf(base, max_radius)


## C10：方形路径角点（底→左→顶→右，index 4 循环回底），配合 _path_center 无数组求值
func _square_corner(index: int, radius: float) -> Vector2:
	match index % 4:
		0:
			return Vector2(0.0, radius)
		1:
			return Vector2(-radius, 0.0)
		2:
			return Vector2(0.0, -radius)
		_:
			return Vector2(radius, 0.0)


## 轨道中心：前 48% 方形路径（底→左→顶→右→底），后 52% 圆形路径（底部起顺接）
func _path_center(progress: float, boss) -> Vector2:
	progress = clampf(progress, 0.0, 1.0)
	var radius := _path_radius(boss)
	var c := _snapshot_target
	if progress <= float(boss.ENRAGE_SQUARE_PATH_RATIO):
		var sp := progress / float(boss.ENRAGE_SQUARE_PATH_RATIO)
		var segment := mini(3, int(sp * 4.0))
		var local := sp * 4.0 - float(segment)
		# C10：方形路径四角直接求两端点 lerp，避免每帧构建 5 元素数组（GC 压力）
		var from := c + _square_corner(segment, radius)
		var to := c + _square_corner(segment + 1, radius)
		return from.lerp(to, local)
	var cp := (progress - float(boss.ENRAGE_SQUARE_PATH_RATIO)) / (1.0 - float(boss.ENRAGE_SQUARE_PATH_RATIO))
	var angle := PI / 2.0 + cp * TAU
	return c + Vector2(_enemy_script.CosFast(angle), _enemy_script.SinFast(angle)) * radius  # M3b：Enemy 迁 C#，静态经脚本资源


## ACTIVE 计时耗尽：进入释放阶段——解血锁、复位玩家减速 + 各型收尾爆发起手（§5.4 峰值）
func _begin_release_hold(boss) -> void:
	_phase = ENRAGE_RELEASE_HOLD
	_release_hold_timer = float(boss.ENRAGE_RELEASE_HOLD_DURATION)
	_health_lock = false
	_unlock_player_movement()
	_attacks.cancel_aim_line()
	_free_aim_line()
	_aim_elapsed = -1.0
	_release_salvo_done = false
	var release_begin: Variant = _release_begin_handlers.get(int(boss.boss_type))
	if release_begin is Callable:
		(release_begin as Callable).call(boss)
	else:
		_attack_timer = 0.0  # 回退路径：立即放第一波


## 倾巢收尾：全部在场小怪齐射一轮自机狙
func _hive_volley_all_minions(boss) -> void:
	var minions: Array = []
	# 统一实体管理器批量 API（docs/ENTITY_MANAGER.md）：收集在场活跃小怪
	# M3b：Enemy 迁 C#，is/as 改 is_instance_of（过滤器 lambda 参数 untyped）
	GameState.for_each_enemy(
		func(e: Node) -> void: minions.append(e), func(e) -> bool: return is_instance_of(e, _enemy_script) and e.is_active()
	)
	_attacks.minion_volley_fire(boss, minions)


## RELEASE_HOLD 结束：0.8s 飞回战斗位（x 钳回巡航范围、y 回战斗锚线 view 顶 + FIGHT_Y）
func _begin_return(boss) -> void:
	_phase = ENRAGE_RETURN
	_return_timer = float(boss.ENRAGE_RETURN_DURATION)
	_return_origin = boss.position
	var bounds: Vector2 = boss.strafe_range()
	_return_target = Vector2(clampf(boss.position.x, bounds.x, bounds.y), boss.fight_anchor_y())


## 狂暴期玩家减速（替代原作 is_controls_locked 定身，§4.3）：移速 ×0.35，
## 仍可瞄准/射击/冲刺；TRANSITION+ACTIVE 有效
func _lock_player_movement(boss) -> void:
	var p = GameState.player_ref
	if p != null and not p.is_dead():
		_slowed_player = p
		p.apply_enrage_slow(float(boss.ENRAGE_PLAYER_SLOW))


func _unlock_player_movement() -> void:
	if _slowed_player != null:
		if is_instance_valid(_slowed_player):
			_slowed_player.apply_enrage_slow(1.0)
		_slowed_player = null


## 面向玩家的方向（player 为空回退 Vector2.DOWN）
static func _player_dir(from: Node2D) -> Vector2:
	if GameState.player_ref != null:
		return (GameState.player_ref.global_position - from.global_position).normalized()
	return Vector2.DOWN


## A3：TRANSITION 悬停判定（机型参数表驱动，取代散落的类型特判）
func _hover_in_transition(boss) -> bool:
	return bool(TRANSITION_HOVER_TYPES.get(int(boss.boss_type), false))


## A3：ACTIVE 回退处理器（非法 boss_type，防御路径：轨道环绕 + 定时狂暴波）
func _active_fallback(delta: float, boss) -> void:
	boss.position = _path_center(_progress(boss), boss)
	_attack_timer -= delta
	if _attack_timer <= 0.0:
		_attack_timer = float(boss.ENRAGE_ATTACK_INTERVAL)
		_attack_index += 1
		(
			_fire
			. fire_enrage_wave(
				boss,
				float(boss.ENRAGE_LASER_SPEED),
				float(boss.ENRAGE_RING_SPEED),
				int(boss.BULLET_DAMAGE_SNAPSHOT_LASER),
				int(boss.BULLET_DAMAGE_SNAPSHOT_RING),
				int(boss.ENRAGE_SNAPSHOT_LASERS),
				int(boss.ENRAGE_SNAPSHOT_RING),
			)
		)


## A3：1 型「旋转堡垒」释放——8 路蓄力重炮齐射（蓄力辉光 telegraph 已在 _release_begin_bulwark 起手）
func _release_bulwark(delta: float, boss) -> void:
	if not _release_salvo_done:
		_attack_timer -= delta
		if _attack_timer <= 0.0:
			_release_salvo_done = true
			for i in int(boss.E1_SALVO_COUNT):
				var dir := Vector2.RIGHT.rotated(TAU * float(i) / float(boss.E1_SALVO_COUNT))
				_fire.fire_heavy(boss, dir, float(boss.E1_SALVO_SPEED), int(boss.E1_SALVO_DAMAGE))


## A3：2 型「猎杀环绕」释放——回轨道底部放 12 向慢速环弹
func _release_stalker(_delta: float, boss) -> void:
	var t := clampf(1.0 - _release_hold_timer / float(boss.ENRAGE_RELEASE_HOLD_DURATION), 0.0, 1.0)
	var eased := t * t * (3.0 - 2.0 * t)
	boss.position = _release_origin.lerp(_snapshot_target + Vector2(0.0, _path_radius(boss)), eased)
	if not _release_salvo_done and t >= 0.5:
		_release_salvo_done = true
		_fire.fire_ring(
			boss, int(boss.E2_RELEASE_RING_COUNT), float(boss.E2_RELEASE_RING_SPEED), int(boss.BULLET_DAMAGE_SNAPSHOT_RING), 0.0
		)


## A3：3 型「倾巢」释放——无持续结算（16 向环弹 + 小怪齐射已在 _release_begin_hive 一次性结算）
func _release_hive(_delta: float, _boss) -> void:
	pass


## A3：RELEASE_HOLD 回退处理器（非法 boss_type，防御路径：按固定间隔放狂暴波）
func _release_fallback(delta: float, boss) -> void:
	_attack_timer -= delta
	if _attack_timer <= 0.0:
		_attack_timer = float(boss.ENRAGE_RELEASE_INTERVAL)
		(
			_fire
			. fire_enrage_wave(
				boss,
				float(boss.ENRAGE_RELEASE_LASER_SPEED),
				float(boss.ENRAGE_RELEASE_RING_SPEED),
				int(boss.BULLET_DAMAGE_SNAPSHOT_LASER),
				int(boss.BULLET_DAMAGE_SNAPSHOT_RING),
				int(boss.ENRAGE_SNAPSHOT_LASERS),
				int(boss.ENRAGE_SNAPSHOT_RING),
			)
		)


## A3：1 型释放起手——蓄力辉光 telegraph
func _release_begin_bulwark(boss) -> void:
	_attack_timer = float(boss.E1_SALVO_CHARGE)
	_attacks.charge_glow(boss, float(boss.E1_SALVO_CHARGE))


## A3：2 型释放起手——记录当前位置为回轨道底部起点
func _release_begin_stalker(boss) -> void:
	_release_origin = boss.position


## A3：3 型释放起手——16 向环弹 + 全部在场小怪齐射（§5.4 峰值一次性结算）
func _release_begin_hive(boss) -> void:
	_fire.fire_ring(boss, int(boss.E3_RELEASE_RING_COUNT), float(boss.E3_RELEASE_RING_SPEED), int(boss.BULLET_DAMAGE_SNAPSHOT_RING), 0.0)
	_hive_volley_all_minions(boss)
