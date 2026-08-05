class_name BossMovement
extends RefCounted
## A3 拆分：Boss 走位策略（docs/AUDIT_VAULT.md A3）。
## 三型移动（strafe / dash / bulwark 纵向下压）与移动状态；写 boss.position（Node2D 公开属性），
## 经 boss 公开查询（slow_factor/strafe_range/is_enraged/fight_phase）交互，不访问私有字段（A1 约束）。
## boss 参数声明为无类型（Variant）以允许动态成员访问，从 boss 取值处显式标注类型。

## 对齐 Boss.FightPhase.P1（enum FightPhase { P1, P2, ENRAGE }）
const FIGHT_P1 := 0

var _strafe_dir: float = 1.0
var _move_timer: float = 0.0
var _dashing: bool = false
var _press_timer: float = 6.0
var _press_offset: float = 0.0
var _bob_phase: float = 0.0  # 纵向正弦相位累计（段切换归零，sin 0 = 0 无跳变）
var _band_timer: float = 0.0  # 三型 P1 下压周期计时（独立于 press）
var _band_offset: float = 0.0  # 三型 P1 下压偏移（target 从 0 起步，无初始跳变）
# L14：段切换 y 平滑过渡——P1 增量式下压（一型 press / 三型 band）的当前偏移未补偿，
# P2 绝对赋值锚线会瞬间跳变（三型可达 ~280px）；切换后从当前 y 平滑追锚线（ease-out）
var _bob_smooth_t: float = 0.0
var _bob_smooth_from: float = 0.0
const BOB_SMOOTH_TIME := 0.6


## 同步下压周期初始值（Boss._ready 在 PRESS_INTERVAL 从 balance 覆盖后调用，保持精确一致）
func sync_press_timer(interval: float) -> void:
	_press_timer = interval


## C11 + L14：段切换（P1→P2）时归零下压偏移——若切换恰落在下压窗口内，
## _press_offset/_band_offset 保留非零值而 _update_press/_move_band 不再被调用，
## 机身会以偏移永久留在锚线下方（C11 原只清 press，L14 补清三型 band）
func reset_press() -> void:
	_press_offset = 0.0  # 仅清偏移，保留下压周期相位（_press_timer 不动）
	_band_offset = 0.0  # L14：三型 band 同族清理
	_bob_phase = 0.0  # D05：段切换归零纵向正弦（sin 0 = 0 平滑衔接锚线）


## L14：段切换入口——记录当前 y 作为平滑过渡起点（由 boss._enter_phase 在切换帧调用）。
## 不在此处直接写 y（走位由各 mover 每帧驱动），过渡在 _move_bob 内收敛到锚线正弦轨迹。
func begin_bob_smooth(current_y: float) -> void:
	_bob_smooth_t = BOB_SMOOTH_TIME
	_bob_smooth_from = current_y


## A3 收敛：机型移动器注册表（boss_type → 移动策略方法，_init 装配）。
## 新增机型只需注册一行 + 一个策略方法，不再改 update 的 match（O 原则达成）。
var _movers: Dictionary = {}


func _init() -> void:
	_movers = {
		1: _move_type1,
		2: _move_type2,
		3: _move_type3,
		4: _move_type4,
	}


func update(delta: float, boss) -> void:
	var mover: Variant = _movers.get(int(boss.boss_type))
	if mover is Callable:
		(mover as Callable).call(delta, boss)
	else:
		# K13：非法 boss_type（防御，正常路径恒 1..3）回退一型走位，防非法值下完全静止
		_move_type1(delta, boss)


## 注册表完整性查询（A3 架构断言测试经公开接口访问）
func has_mover(type: int) -> bool:
	return _movers.has(type)


## 一型「堡垒」：慢速 strafe + P1 每 6s 纵向下压 80px 再回（§5.1）
func _move_type1(delta: float, boss) -> void:
	var phase: int = boss.fight_phase()
	if phase == FIGHT_P1:
		_move_strafe(delta, boss, float(boss.STRAFE_SPEEDS[0]))
		_update_press(delta, boss)
	elif phase == 1:  # FightPhase.P2
		# D05：strafe 提速 + 纵向正弦往复
		_move_strafe(delta, boss, float(boss.TYPE1_P2_STRAFE))
		_move_bob(delta, boss, float(boss.TYPE1_P2_BOB_AMP), float(boss.TYPE1_P2_BOB_PERIOD))
	else:  # ENRAGE：狂暴轨道由 enrage_sequence 接管，走位维持现状
		_move_strafe(delta, boss, float(boss.STRAFE_SPEEDS[0]))


## 二型「游击」：周期性冲刺换向（偏向屏幕中心，避免长期贴边）
func _move_type2(delta: float, boss) -> void:
	_move_dash(delta, boss)


## 4 型「月蚀」（2026-08-04）：中心悬停微摆——不 strafe，纵向小振幅正弦（相位归零平滑衔接锚线）
## Q27（2026-08-05）：正弦峰值速度（AMP×TAU/PERIOD ≈ 78.5px/s）> 原 MOVE4_SPEED 40 时，
## move_toward 速度上限把振幅压到 ±15px 且波形低通失真——与 _move_bob 同款直接绝对赋值
## （战斗与逃跑警告期独占 y，入场/逃跑/狂暴序列均早退不干扰；MOVE4_SPEED 键已随修复移除）
func _move_type4(delta: float, boss) -> void:
	_bob_phase += delta * TAU / float(boss.MOVE4_BOB_PERIOD)
	boss.position.y = boss.fight_anchor_y() + float(boss.MOVE4_BOB_AMP) * Enemy.sin_fast(_bob_phase)


## 三型「母舰」：P1 缓慢下压/回升 + P2 提速正弦（§5.3）
func _move_type3(delta: float, boss) -> void:
	var phase: int = boss.fight_phase()
	if phase == FIGHT_P1:
		# D05：三型 P1 缓慢下压/回升（锚线下 [lo, hi] 区间，周期 9s）
		_move_strafe(delta, boss, float(boss.STRAFE_SPEEDS[2]))
		_move_band(delta, boss, float(boss.TYPE3_P1_BOB_MIN), float(boss.TYPE3_P1_BOB_MAX), float(boss.TYPE3_P1_BOB_PERIOD))
	elif phase == 1:  # FightPhase.P2
		# D05：strafe 提速 + 纵向正弦往复
		_move_strafe(delta, boss, float(boss.TYPE3_P2_STRAFE))
		_move_bob(delta, boss, float(boss.TYPE3_P2_BOB_AMP), float(boss.TYPE3_P2_BOB_PERIOD))
	else:  # ENRAGE 现状
		_move_strafe(delta, boss, float(boss.STRAFE_SPEEDS[2]))


## 一型「堡垒」：慢速 strafe + P1 每 6s 纵向下压 80px 再回（§5.1）
## 纵向下压：周期最后 1.6s 窗口内正弦下压再回升（增量式施加，不覆盖逃跑上飘）
func _update_press(delta: float, boss) -> void:
	_press_timer -= delta
	if _press_timer <= 0.0:
		_press_timer = float(boss.PRESS_INTERVAL)
	const PRESS_WINDOW := 1.6
	var elapsed: float = float(boss.PRESS_INTERVAL) - _press_timer
	var target := 0.0
	if elapsed >= float(boss.PRESS_INTERVAL) - PRESS_WINDOW:
		target = float(boss.PRESS_DEPTH) * Enemy.sin_fast(PI * (elapsed - (float(boss.PRESS_INTERVAL) - PRESS_WINDOW)) / PRESS_WINDOW)
	boss.position.y += target - _press_offset
	_press_offset = target


## 纵向正弦（P2 通用，D05）：围绕锚线 ±amp 正弦往复。
## 直接设置 y（_in_fight 后才被调用，入场/逃跑/狂暴序列均早退不干扰；fight_anchor_y()
## 逐帧求值支持战斗中切视角档）。相位累计驱动，Enemy.sin_fast 查表零分配。
## L14：段切换后 BOB_SMOOTH_TIME 内从切换前 y 平滑收敛到锚线正弦轨迹（ease-out），
## 消除 P1 增量式下压（press/band）残留偏移的瞬间跳变。
func _move_bob(delta: float, boss, amp: float, period: float) -> void:
	_bob_phase += TAU * delta / maxf(period, 0.01)
	var target: float = boss.fight_anchor_y() + Enemy.sin_fast(_bob_phase) * amp
	if _bob_smooth_t > 0.0:
		_bob_smooth_t -= delta
		var k := 1.0 - _bob_smooth_t / BOB_SMOOTH_TIME
		k = 1.0 - pow(1.0 - k, 3.0)  # ease-out：先快后慢追锚线（视觉上「回落」而非「漂移」）
		target = lerpf(_bob_smooth_from, target, k)
	boss.position.y = target


## 三型 P1「缓慢下压/回升」（§5.3）：周期内正弦下压到锚线下 [y_lo, y_hi] 区间再回升。
## 与 _update_press 同构（target 为纯偏移、从 0 起步无初始跳变）；wob 慢相位使下压轨迹
## 在 [lo, hi] 邻域摆动（9s 慢周期，与模式循环错开）。
func _move_band(delta: float, boss, y_lo: float, y_hi: float, period: float) -> void:
	if _band_timer <= 0.0:
		_band_timer = period
	_band_timer -= delta
	var elapsed: float = period - _band_timer
	var u := clampf(elapsed / period, 0.0, 1.0)
	var depth := (y_lo + y_hi) * 0.5
	var wob := (y_hi - y_lo) * 0.5
	var target: float = depth * Enemy.sin_fast(PI * u) + wob * Enemy.sin_fast(TAU * u * 0.5) * Enemy.sin_fast(PI * u)
	boss.position.y += target - _band_offset
	_band_offset = target


func _move_strafe(delta: float, boss, p_speed: float) -> void:
	boss.position.x += _strafe_dir * p_speed * float(boss.slow_factor()) * _enrage_speed_mult(boss) * delta
	var bounds: Vector2 = boss.strafe_range()
	if boss.position.x < bounds.x or boss.position.x > bounds.y:
		_strafe_dir = -_strafe_dir
		boss.position.x = clampf(boss.position.x, bounds.x, bounds.y)


## 二型「游击」：周期性冲刺换向（偏向屏幕中心，避免长期贴边）
func _move_dash(delta: float, boss) -> void:
	_move_timer -= delta
	if _move_timer <= 0.0:
		_dashing = not _dashing
		# D05：P2 冲刺更频（0.4s/0.5s）；P1 与 ENRAGE 维持现状（0.5s/0.7s）
		var dash_t: float = float(boss.TYPE2_P2_DASH_TIME) if boss.fight_phase() == 1 else 0.5
		var rest_t: float = float(boss.TYPE2_P2_REST_TIME) if boss.fight_phase() == 1 else 0.7
		_move_timer = dash_t if _dashing else rest_t
		if _dashing:
			# 偏向屏幕中心方向冲刺，避免长期贴边（C14：中心取可见世界，不写死 960）
			var center_x: float = GameState.view_world_rect().get_center().x
			_strafe_dir = signf(center_x - boss.position.x) if randf() < 0.6 else (-_strafe_dir)
			if _strafe_dir == 0.0:
				_strafe_dir = 1.0
	if _dashing:
		boss.position.x += _strafe_dir * float(boss.STRAFE_SPEEDS[1]) * float(boss.slow_factor()) * _enrage_speed_mult(boss) * delta
		var bounds: Vector2 = boss.strafe_range()
		if boss.position.x < bounds.x or boss.position.x > bounds.y:
			_strafe_dir = -_strafe_dir
			boss.position.x = clampf(boss.position.x, bounds.x, bounds.y)


## 狂暴「余怒」移速倍率（未狂暴 = 1.0）
func _enrage_speed_mult(boss) -> float:
	return float(boss.ENRAGE_SPEED_MULT) if boss.is_enraged() else 1.0
