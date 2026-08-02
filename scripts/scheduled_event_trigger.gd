class_name ScheduledEventTrigger
extends RefCounted
## A4b：事件触发策略统一骨架（docs/AUDIT_VAULT.md A4）。
## 精英炮塔/轰炸编队两事件的触发策略原本形状一致（interval 定时递减 → 归零重置 → 掷签），
## 内联在 spawner._process。本类统一该骨架：interval/chance/min_score 门槛 + 定时/掷签。
## 互斥与可触发条件由调用方门控（各事件 can_trigger() 自检 + spawner 侧 Boss/事件互斥），本类不感知。

var _interval: float
var _chance: float
var _min_score: int
var _timer: float


func _init(p_interval: float, p_chance: float, p_min_score: int = 0) -> void:
	configure(p_interval, p_chance, p_min_score)


## 配置刷新（spawner._apply_balance 在 balance 覆盖后调用；_timer 不重置，保持现有节奏）
func configure(p_interval: float, p_chance: float, p_min_score: int = 0) -> void:
	# H15（健壮性审核）：interval ≤0 时每帧掷签事件风暴，钳制下限
	_interval = maxf(p_interval, 0.1)
	_chance = p_chance
	_min_score = p_min_score
	if _timer <= 0.0:
		_timer = _interval


## 每帧调用（外部先门控条件）。score 低于门槛时定时不递减（对齐原 spawner 内联语义）。
## 返回 true = 触发（内部已重置定时器），由调用方 start() + 占用特殊槽。
func tick(delta: float, score: int) -> bool:
	if score < _min_score:
		return false
	_timer -= delta
	if _timer <= 0.0:
		_timer = _interval
		return randf() < _chance
	return false
