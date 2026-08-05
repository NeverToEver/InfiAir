class_name DirectionShiftEvent
extends FogEvent
## 短间隔随机方向事件：周期发射强制方向脉冲（经 FogEvent.emit_direction_shift →
## manager.fog_direction_shift 信号），玩家 hold 秒内移动向量被替换为随机单位方向。
## tick 驱动（无自持 Timer——duration 计时由编排器统一负责，事件结束自动停止）。
## 配置：balance.json fog_events.direction_shift（shift_interval / hold_time）。

var _interval := 0.7
var _hold := 0.3
var _acc := 0.0


func event_id() -> StringName:
	return &"direction_shift"


func _on_start() -> void:
	_interval = maxf(float(GameState.cfg("fog_events.direction_shift.shift_interval", _interval)), 0.05)  # H15 族：≤0 每帧脉冲
	_hold = maxf(float(GameState.cfg("fog_events.direction_shift.hold_time", _hold)), 0.0)
	_acc = 0.0
	_emit_shift()  # 事件开始立即脉冲一次


func _on_tick(delta: float) -> void:
	_acc += delta
	if _acc >= _interval:
		_acc = 0.0
		_emit_shift()


func _emit_shift() -> void:
	var dir := Vector2(randf_range(-1.0, 1.0), randf_range(-1.0, 1.0))
	if dir.length_squared() < 1.0e-6:
		dir = Vector2.DOWN
	emit_direction_shift(dir.normalized(), _hold)
