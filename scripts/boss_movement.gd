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


## 同步下压周期初始值（Boss._ready 在 PRESS_INTERVAL 从 balance 覆盖后调用，保持精确一致）
func sync_press_timer(interval: float) -> void:
	_press_timer = interval


## C11 修复：段切换（P1→P2）时归零下压偏移——若切换恰落在下压窗口内，
## _press_offset 保留非零值而 _update_press 不再被调用，机身会以偏移永久留在锚线下方
func reset_press() -> void:
	_press_offset = 0.0
	_press_timer = _press_timer  # 保留下压周期相位，仅清偏移


func update(delta: float, boss) -> void:
	match int(boss.boss_type):
		1:
			_move_strafe(delta, boss, float(boss.STRAFE_SPEEDS[0]))
			if boss.fight_phase() == FIGHT_P1:
				_update_press(delta, boss)
		2:
			_move_dash(delta, boss)
		3:
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
		_move_timer = 0.5 if _dashing else 0.7
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
