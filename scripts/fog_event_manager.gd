class_name FogEventManager
extends Node
## 迷雾事件效果层 + API 门面（docs/EVENT_MANAGER.md §4 迁移图）。
## 挂载：GameState autoload 子节点（GameState.fog_events 全局访问口）。
##
## 2026-08-05 统一事件管理器收敛：迷雾事件的注册（EVENT_FACTORIES）/触发策略/生命周期/
## 计时/冷却/信号广播全部由 GameEventManager（GameState.events）统一接管（fog 组）；
## 本类保留：
##   - 迷雾专属视觉基座（伪敌机容器/精神错乱覆盖层/事件横幅，经 context 注入事件类）；
##   - 迷雾信号 fog_event_started/ended/fog_direction_shift（监听统一管理器信号重发，
##     player 侧消费面不变：输入反转/子弹参数/方向脉冲）；
##   - 公开 API 与配置 var（测试/诊断/player 引用面不变）——全部转发/代理到 GameState.events
##     fog 组配置与状态。
## 接线：GameState._ready 经 wire(events) 调用（activate_fog + 信号连接）。
## 新增迷雾事件 = 新建 FogEvent 子类 + EVENT_FACTORIES 一行注册（与遭遇事件同注册表）。

## 迷雾事件开始信号（统一管理器 event_started 的 fog 组重发；player 输入反转/子弹参数）
signal fog_event_started(event_id: StringName, duration: float)
## 迷雾事件结束信号（统一管理器 event_ended 的 fog 组重发；效果复位）
signal fog_event_ended(event_id: StringName)
## 方向强制偏转脉冲（direction_shift 事件周期发射；DirectionShiftEvent 经 context 回调转发）
signal fog_direction_shift(dir: Vector2, hold: float)

## 事件工厂注册表代理（唯一事实源在 GameState.events；测试可增删注册以走通生命周期）
var EVENT_FACTORIES: Dictionary:
	get:
		return GameState.events.EVENT_FACTORIES
	set(value):
		GameState.events.EVENT_FACTORIES = value

## 迷雾组配置代理（balance.json fog_events.* 由统一管理器读取；脚本值为缺键回退）
var ENABLED: bool:
	get:
		return GameState.events.FOG_ENABLED
	set(value):
		GameState.events.FOG_ENABLED = value
var TRIGGER_CHANCE: float:
	get:
		return GameState.events.FOG_TRIGGER_CHANCE
	set(value):
		GameState.events.FOG_TRIGGER_CHANCE = value
var CHECK_INTERVAL: float:
	get:
		return GameState.events.FOG_CHECK_INTERVAL
	set(value):
		GameState.events.FOG_CHECK_INTERVAL = value
var MIN_INTERVAL: float:
	get:
		return GameState.events.FOG_MIN_INTERVAL
	set(value):
		GameState.events.FOG_MIN_INTERVAL = value
var FIRST_DELAY: float:
	get:
		return GameState.events.FOG_FIRST_DELAY
	set(value):
		GameState.events.FOG_FIRST_DELAY = value
var WEIGHTS: Dictionary:
	get:
		return GameState.events.FOG_WEIGHTS
	set(value):
		GameState.events.FOG_WEIGHTS = value
var EVENT_DURATIONS: Dictionary:
	get:
		return GameState.events.FOG_EVENT_DURATIONS
	set(value):
		GameState.events.FOG_EVENT_DURATIONS = value

## 伪敌机容器（z_index=10：世界之上、HUD 之下；autoload 先于 Main 入树，层 0 内 z 排序兜底）
var _fake_container: Node2D = null
## 精神错乱全屏变色覆盖层（layer=2，与 HUD 同层、先入树 → 绘制于 HUD 之下，数值保持可读）
var _confusion_layer: CanvasLayer = null
var _confusion_rect: ColorRect = null
## 事件横幅（layer=30，对齐狂暴泛红层级）
var _banner_layer: CanvasLayer = null
var _banner_label: Label = null
var _banner_tween: Tween = null


func _ready() -> void:
	_build_visual_layers()


## 接线（GameState._ready 调用）：开启统一管理器 fog 组驱动 + 连接统一信号（重发/横幅）
func wire(p_events: GameEventManager) -> void:
	p_events.activate_fog()
	p_events.event_started.connect(_on_event_started)
	p_events.event_ended.connect(_on_event_ended)


## 视觉层构建（伪敌机容器 / 变色覆盖层 / 事件横幅；覆盖层显隐由 ConfusionEvent 驱动）
func _build_visual_layers() -> void:
	_fake_container = Node2D.new()
	_fake_container.z_index = 10
	add_child(_fake_container)
	_confusion_layer = CanvasLayer.new()
	_confusion_layer.layer = 2
	_confusion_layer.visible = false
	add_child(_confusion_layer)
	_confusion_rect = ColorRect.new()
	_confusion_rect.color = Color(0.45, 0.2, 0.9, 0.0)
	_confusion_rect.set_anchors_preset(Control.PRESET_FULL_RECT)
	_confusion_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_confusion_layer.add_child(_confusion_rect)
	_banner_layer = CanvasLayer.new()
	_banner_layer.layer = 30
	_banner_layer.visible = false
	add_child(_banner_layer)
	_banner_label = UITheme.make_label("", 26, UITheme.WARN_YELLOW)
	_banner_label.set_anchors_preset(Control.PRESET_CENTER_TOP)
	_banner_label.offset_top = 30.0
	_banner_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_banner_layer.add_child(_banner_label)


# ---------------- 对外公开接口（A1 约定：测试/诊断经公开接口；转发到统一管理器 fog 组） ----------------


func is_run_active() -> bool:
	return GameState.events.is_run_active()


## 对局活跃开关（main._ready/_exit_tree 设置；非活跃时强制结束进行中的迷雾事件）
func set_run_active(active: bool) -> void:
	GameState.events.set_run_active(active)


func active_id() -> StringName:
	return GameState.events.active_id(GameEventManager.GROUP_FOG)


## 进行中的事件对象（测试/诊断与事件类 getter；无事件返回 null）
func active_event() -> GameEvent:
	return GameState.events.active_event(GameEventManager.GROUP_FOG) as GameEvent


## 迷雾组已注册事件 id 列表（统一注册表按 fog 组过滤；测试断言 4 种）
func event_ids() -> Array[StringName]:
	var out: Array[StringName] = []
	for id in GameState.events.event_ids():
		if GameState.events.group_of(id) == GameEventManager.GROUP_FOG:
			out.append(id)
	return out


func cooldown_left() -> float:
	return GameState.events.cooldown_left()


## 测试/诊断：直接设定迷雾组冷却剩余（压缩时长确定性测试，不动 balance.json）
func set_cooldown_left(seconds: float) -> void:
	GameState.events.set_cooldown_left(seconds)


## 测试/诊断：直接设定迷雾组开局保护剩余
func set_first_delay_left(seconds: float) -> void:
	GameState.events.set_first_delay_left(seconds)


## 当前迷雾事件剩余时长（无事件返回 0）
func active_remaining() -> float:
	return GameState.events.active_remaining()


## 迷雾组触发资格（自动触发路径用；force_trigger 不受 run_active 门控，供测试直调）
func can_trigger() -> bool:
	return GameState.events.can_trigger_group(GameEventManager.GROUP_FOG)


## 迷雾组自动触发路径单步检查：资格满足则按权重掷签并启动事件。返回是否触发。
func try_trigger() -> bool:
	return GameState.events.try_trigger_group(GameEventManager.GROUP_FOG)


## 测试/诊断：强制启动指定事件（进行中/未注册 id 返回 false；不受概率与冷却门控）
func force_trigger(p_event_id: StringName) -> bool:
	return GameState.events.force_trigger(p_event_id)


## 立即结束进行中的迷雾事件（返航/死亡/离场清理；效果随 fog_event_ended 一并复位）
func end_active() -> void:
	GameState.events.end_active(GameEventManager.GROUP_FOG)


## 已生成的伪敌机（测试插桩；委托进行中的 FakeEnemiesEvent，无则空）
func spawned_fakes() -> Array:
	var e := GameState.events.active_event(GameEventManager.GROUP_FOG) as FakeEnemiesEvent
	if e == null:
		return []
	return e.spawned_fakes()


## 伪敌机容器（FogEvent 子类挂接点）
func fake_container() -> Node2D:
	return _fake_container


## 方向偏转脉冲转发（DirectionShiftEvent 经 context 回调调用；信号在本类声明/发射保持一致）
func emit_direction_shift(dir: Vector2, hold: float) -> void:
	fog_direction_shift.emit(dir, hold)


## 精神错乱覆盖层（FogEvent 子类挂接点）
func overlay_layer() -> CanvasLayer:
	return _confusion_layer


func overlay_rect() -> ColorRect:
	return _confusion_rect


## 迷雾事件 context 构建（统一管理器 _start_fog 调用）：注入视觉基座 + 方向脉冲回调 +
## 提前结束回调（request_end 指向管理器，事件主动 request_end 保持信号所有者一致）。
## 键约定见 FogEvent 访问器；通用键 "request_end" 由 GameEvent.request_end 使用。
func build_fog_context(request_end: Callable) -> Dictionary:
	return {
		"fake_container": _fake_container,
		"overlay_layer": _confusion_layer,
		"overlay_rect": _confusion_rect,
		"emit_direction_shift": emit_direction_shift,
		"request_end": request_end,
	}


# ---------------- 统一管理器信号转发（fog 组重发 + 事件横幅） ----------------


func _on_event_started(event_id: StringName, duration: float) -> void:
	if GameState.events.group_of(event_id) != GameEventManager.GROUP_FOG:
		return
	_show_banner(event_id, duration)
	fog_event_started.emit(event_id, duration)


func _on_event_ended(event_id: StringName) -> void:
	if GameState.events.group_of(event_id) != GameEventManager.GROUP_FOG:
		return
	_hide_banner()
	fog_event_ended.emit(event_id)


## 事件横幅（短暂提示当前事件名）
func _show_banner(event_id: StringName, duration: float) -> void:
	_banner_layer.visible = true
	_banner_label.text = tr("FOG_EVENT_%s_NAME" % String(event_id).to_upper())
	if _banner_tween != null and _banner_tween.is_valid():
		_banner_tween.kill()
	_banner_label.modulate.a = 0.0
	_banner_tween = create_tween()
	_banner_tween.tween_property(_banner_label, "modulate:a", 1.0, 0.2)
	_banner_tween.tween_interval(maxf(duration - 0.7, 0.0))
	_banner_tween.tween_property(_banner_label, "modulate:a", 0.0, 0.5)


func _hide_banner() -> void:
	if _banner_tween != null and _banner_tween.is_valid():
		_banner_tween.kill()
	_banner_tween = null
	_banner_label.modulate.a = 1.0
	_banner_layer.visible = false
