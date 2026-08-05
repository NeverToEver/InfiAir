class_name GameEvent
extends RefCounted
## 通用游戏事件基类（纯生命周期接口，零系统耦合，2026-08-05 接口方向重构）。
## 这是「事件」的唯一基底：编排器（如 FogEventManager）负责注册/选择/统一计时/
## 结束清理；本类只定义事件自身的生命周期契约与上下文注入：
##   - context：执行上下文 Dictionary（编排器注入；本类存浅拷贝隔离，键约定由各系统
##     的中间层基类（如 FogEvent）定义，本类不解析任何键）；
##   - duration：编排器给定的持续时间（秒，≤0 钳制为 0）；
##   - is_active：生命周期守卫（start → tick… → end；end 幂等、tick 仅活跃期派发）。
## 子类实现点（模板方法）：event_id() 必实现；_on_start()/_on_tick()/_on_end() 可选。
##
## 契约（健壮性，2026-08-05 审计）：
##   - start()/tick()/end() 为模板方法，子类不得覆盖（实现点用 _on_* 钩子）；
##   - start() 可安全重复调用：先内部清理旧效果（_on_end）再注入新上下文重启，
##     不会叠加半状态（子类 _on_start 无需为重复调用特判）；
##   - end() 幂等；tick() 仅活跃期派发；context 浅拷贝隔离编排器后续修改；
##   - duration ≤ 0 钳制为 0（编排器侧另有下限，双保险）。
##
## 复用方式（先有事件类，系统走事件类接口）：
##   - 任意系统的事件继承本类即可获得统一生命周期；系统专属上下文经「中间层基类」
##     提供访问器——如迷雾系统的 FogEvent extends GameEvent，只聚合 fog 上下文取键。

## 执行上下文（编排器注入；start 时浅拷贝，事件持有的引用不受编排器后续修改影响）。
## 通用键约定（GameEvent 自身定义，各系统中间层另定义系统键）：
##   "request_end": Callable —— 编排器的提前结束回调（事件可主动 request_end()）
var context: Dictionary = {}
## 编排器给定的持续时间（秒）
var duration: float = 0.0
## 生命周期守卫（end 幂等、tick 仅活跃期派发）
var is_active := false


## 事件唯一 id（注册表键，如 &"fake_enemies"）；子类必须实现
func event_id() -> StringName:
	push_error("GameEvent.event_id() 未实现：%s" % get_script())
	return &""


## 通用 context 读取辅助（宽容性：简单事件无需了解系统访问器，一行读自定义数据；
## 键缺失/类型不符返回 default，不抛错）
func get_ctx(key: StringName, default: Variant = null) -> Variant:
	return context.get(key, default)


## 请求提前结束（宽容性：复杂事件在内部目标达成后可主动请求编排器结束；
## 简单事件无需理会）。经 context 的 "request_end" 回调（编排器注入）实现；
## 回调缺失/无效时降级警告，事件继续按 duration 自然结束。
## 注：从 _on_tick 内调用时，当前 tick 帧的剩余代码仍会执行完（end 幂等，
## 编排器结束信号随后发出）。
func request_end() -> void:
	var cb: Callable = context.get("request_end", Callable())
	if cb.is_valid():
		cb.call()
	else:
		push_warning("GameEvent.request_end：context 缺 request_end 回调，事件将按 duration 自然结束")


## 由编排器调用：注入上下文并启动。重复调用安全（先清理旧效果再重启）。
## 子类在 _on_start 应用效果，勿覆盖本方法。
func start(p_context: Dictionary, p_duration: float) -> void:
	if is_active:
		_on_end()  # 自愈：重复 start 先清理旧状态，防 _on_start 叠加
	context = p_context.duplicate()  # 浅拷贝：编排器复用/修改字典不污染本事件
	duration = maxf(p_duration, 0.0)
	is_active = true
	_on_start()


## 事件进行中每帧驱动（编排器调用；子类实现 _on_tick，勿覆盖本方法）
func tick(delta: float) -> void:
	if is_active:
		_on_tick(delta)


## 结束并清理（幂等：编排器可能在 duration 前因返航/死亡提前调用；子类实现 _on_end）
func end() -> void:
	if not is_active:
		return
	is_active = false
	_on_end()


# ---------------- 子类实现点（模板方法） ----------------


func _on_start() -> void:
	pass


func _on_tick(_delta: float) -> void:
	pass


func _on_end() -> void:
	pass
