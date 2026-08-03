class_name PlayerParry
extends RefCounted
## 2026-08-03 公平感机制四：F 键弧光弹反盾组件（docs/2026-08-03-combat-fairness-plan.md §5）。
## 时间轴状态机 IDLE → WINDUP(前摇，无判定) → ACTIVE(有效弹反) → RECOVER(后摇，无判定) → IDLE；
## 硬冷却 3.0s 自 RECOVER 完成（进入 IDLE）起算——完整周期 0.8 + 3.0 = 3.8s，占空比约 21%，
## 盾是「决策性资源」而非常驻免伤。仅 ACTIVE 期由 Player 侧启用盾 Area2D 判定。
## 暂停随玩家 process_mode 冻结（流程/冷却计时同步暂停）。数值经 Player._load_balance 注入。

enum ParryPhase { IDLE, WINDUP, ACTIVE, RECOVER }

var phase: int = ParryPhase.IDLE
var flow_timer: float = 0.0
var cooldown: float = 0.0

var DURATION := 0.8  # 完整流程时长（秒）= 前摇 + 有效 + 后摇
var ACTIVE_TIME := 0.5  # 有效弹反窗口（秒，居中）
var COOLDOWN := 3.0  # 硬冷却（秒，自流程结束起算）


func configure(p_duration: float, p_active_time: float, p_cooldown: float) -> void:
	ACTIVE_TIME = clampf(p_active_time, 0.05, maxf(p_duration, 0.05))
	DURATION = maxf(p_duration, ACTIVE_TIME)
	COOLDOWN = maxf(p_cooldown, 0.0)


func is_flowing() -> bool:
	return phase != ParryPhase.IDLE


func cooldown_remaining() -> float:
	return cooldown


## HUD 能量槽比例：满格=可用；流程期清空；流程结束起按 COOLDOWN 匀速充能回满
func energy_ratio() -> float:
	if is_flowing():
		return 0.0
	if COOLDOWN <= 0.0:
		return 1.0
	return 1.0 - clampf(cooldown / COOLDOWN, 0.0, 1.0)


## 机身金色 tint 强度（0..1）：WINDUP 渐强 → ACTIVE 保持 → RECOVER 渐弱 → IDLE 0
func tint_strength() -> float:
	match phase:
		ParryPhase.WINDUP:
			return _phase_progress()
		ParryPhase.ACTIVE:
			return 1.0
		ParryPhase.RECOVER:
			return 1.0 - _phase_progress()
		_:
			return 0.0


## 盾视觉展开进度（WINDUP 小弧展开到全弧，ACTIVE/RECOVER 全弧，IDLE 0）
func shield_expand() -> float:
	match phase:
		ParryPhase.WINDUP:
			return _phase_progress()
		ParryPhase.ACTIVE, ParryPhase.RECOVER:
			return 1.0
		_:
			return 0.0


## 珍珠流光扫过进度（ACTIVE 期 0→1，自弧线左端扫至右端；其余阶段 0）
func shine_progress() -> float:
	if phase == ParryPhase.ACTIVE:
		return _phase_progress()
	return 0.0


## 尝试启动：仅 IDLE 且冷却结束可启动（Player 门面已校验输入）
func try_start() -> bool:
	if phase != ParryPhase.IDLE or cooldown > 0.0:
		return false
	phase = ParryPhase.WINDUP
	flow_timer = 0.0
	return true


## 流程推进（Player._physics_process 每帧调用）：IDLE 期冷却递减；流程期按相位时长推进。
## 相位边界用 epsilon 容差（浮点累加不精确落在 0.15/0.5 上会卡在边界相位）
func tick(delta: float) -> void:
	if phase == ParryPhase.IDLE:
		cooldown = maxf(cooldown - delta, 0.0)
		return
	flow_timer += delta
	var half := (DURATION - ACTIVE_TIME) / 2.0
	const EPS := 0.0001
	if phase == ParryPhase.WINDUP and flow_timer >= half - EPS:
		phase = ParryPhase.ACTIVE
		flow_timer = 0.0
	elif phase == ParryPhase.ACTIVE and flow_timer >= ACTIVE_TIME - EPS:
		phase = ParryPhase.RECOVER
		flow_timer = 0.0
	elif phase == ParryPhase.RECOVER and flow_timer >= half - EPS:
		phase = ParryPhase.IDLE
		flow_timer = 0.0
		cooldown = COOLDOWN  # 硬冷却自流程结束（RECOVER 完成）起算


## 当前阶段进度（0..1，按阶段时长归一）
func _phase_progress() -> float:
	match phase:
		ParryPhase.WINDUP, ParryPhase.RECOVER:
			var half := (DURATION - ACTIVE_TIME) / 2.0
			return clampf(flow_timer / maxf(half, 0.001), 0.0, 1.0)
		ParryPhase.ACTIVE:
			return clampf(flow_timer / maxf(ACTIVE_TIME, 0.001), 0.0, 1.0)
		_:
			return 0.0
