class_name FogEventManager
extends Node
## 迷雾事件全局单例管理器（提升对局随机性的核心系统）。
## 挂载：GameState autoload 的子节点（维持项目「唯一 autoload：GameState」约定；
## 经 GameState.fog_events 全局访问，测试/诊断直接引用）。
##
## 触发纪律（全部可经 balance.json fog_events 段调参）：
##   1. 开局 first_delay 秒内不触发；
##   2. 每 check_interval 秒掷一次签（概率 trigger_chance，命中才触发）；
##   3. 事件间最小间隔 min_interval（上一事件结束起算，防事件过于频繁）；
##   4. 单事件并发：事件进行中不触发新事件；
##   5. 事件有明确持续时间 duration，到期自动清除效果并复位。
##
## 解耦（2026-08-05 事件类接口方向重构，docs/FOG_EVENTS.md §2.6）：
## 事件继承自通用事件基类 GameEvent（纯生命周期接口，零系统耦合）→ 迷雾专门化层
## FogEvent（聚合迷雾上下文访问器）→ 具体事件子类（scripts/fog_event_*.gd）；
## 本管理器只负责：注册（EVENT_FACTORIES）/加权选择/统一 duration 计时/结束清理/信号广播
##   fog_event_started / fog_event_ended：player（输入反转/子弹错误/方向偏转）
##   fog_direction_shift：player（短间隔随机方向脉冲）
## 共享视觉基座（伪敌机容器/覆盖层/事件横幅）由本管理器构建，经 context 注入事件。
## 新增迷雾事件 = 新建 FogEvent 子类 + EVENT_FACTORIES 一行注册，无需改动本类核心流程；
## 非迷雾系统的事件可直接继承 GameEvent 复用同一套生命周期编排。

signal fog_event_started(event_id: StringName, duration: float)
signal fog_event_ended(event_id: StringName)
## 方向强制偏转脉冲（direction_shift 事件周期发射）：dir 为随机单位方向，
## hold 秒内玩家移动向量被替换为该方向
signal fog_direction_shift(dir: Vector2, hold: float)

## 事件工厂注册表：id -> 构造 Callable（唯一事实源；event_ids() 由此派生）。
## 新增事件：新建 FogEvent 子类后在此注册一行（如 &"my_event": func() -> FogEvent: return MyEvent.new()）
var EVENT_FACTORIES: Dictionary = {
	&"fake_enemies": func() -> FogEvent: return FakeEnemiesEvent.new(),
	&"mental_confusion": func() -> FogEvent: return ConfusionEvent.new(),
	&"bullet_malfunction": func() -> FogEvent: return BulletMalfunctionEvent.new(),
	&"direction_shift": func() -> FogEvent: return DirectionShiftEvent.new(),
}

## 触发配置（balance.json fog_events 段；脚本值为缺键回退）
var ENABLED := true
var TRIGGER_CHANCE := 0.35
var CHECK_INTERVAL := 3.0
var MIN_INTERVAL := 12.0
var FIRST_DELAY := 25.0
var WEIGHTS: Dictionary = {
	&"fake_enemies": 1.0,
	&"mental_confusion": 1.0,
	&"bullet_malfunction": 1.0,
	&"direction_shift": 1.0,
}
var EVENT_DURATIONS: Dictionary = {
	&"fake_enemies": 8.0,
	&"mental_confusion": 6.0,
	&"bullet_malfunction": 7.0,
	&"direction_shift": 6.0,
}

var _run_active := false
var _active_id: StringName = &""
## 进行中的事件对象（生命周期由本管理器驱动：start/tick/end）
var _active_event: FogEvent = null
var _active_timer: Timer = null
var _cooldown_left := 0.0
var _first_delay_left := 0.0
var _check_timer := 0.0

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
	_load_balance()
	_check_timer = CHECK_INTERVAL
	_first_delay_left = FIRST_DELAY
	_build_visual_layers()


func _load_balance() -> void:
	ENABLED = bool(GameState.cfg("fog_events.enabled", ENABLED))
	TRIGGER_CHANCE = float(GameState.cfg("fog_events.trigger_chance", TRIGGER_CHANCE))
	CHECK_INTERVAL = maxf(float(GameState.cfg("fog_events.check_interval", CHECK_INTERVAL)), 0.1)  # H15 族：≤0 每帧掷签
	MIN_INTERVAL = maxf(float(GameState.cfg("fog_events.min_interval", MIN_INTERVAL)), 0.0)
	FIRST_DELAY = maxf(float(GameState.cfg("fog_events.first_delay", FIRST_DELAY)), 0.0)
	var weights: Variant = GameState.cfg("fog_events.weights", WEIGHTS)
	if weights is Dictionary:
		WEIGHTS = weights
	var durations: Variant = GameState.cfg("fog_events.durations", EVENT_DURATIONS)
	if durations is Dictionary:
		EVENT_DURATIONS = durations


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


# ---------------- 对外公开接口（A1 约定：测试/诊断经公开接口） ----------------


func is_run_active() -> bool:
	return _run_active


## 对局活跃开关（main._ready/_exit_tree 设置；非活跃时强制结束进行中的事件）
func set_run_active(active: bool) -> void:
	if active == _run_active:
		return
	_run_active = active
	if not active:
		end_active()


func active_id() -> StringName:
	return _active_id


## 进行中的事件对象（测试/诊断与事件类 getter；无事件返回 null）
func active_event() -> FogEvent:
	return _active_event


## 已注册事件 id 列表（EVENT_FACTORIES 为唯一事实源）
func event_ids() -> Array[StringName]:
	var out: Array[StringName] = []
	for id in EVENT_FACTORIES:
		out.append(id)
	return out


func cooldown_left() -> float:
	return _cooldown_left


## 测试/诊断：直接设定冷却剩余（压缩时长确定性测试，不动 balance.json）
func set_cooldown_left(seconds: float) -> void:
	_cooldown_left = seconds


## 测试/诊断：直接设定开局保护剩余（压缩时长确定性测试）
func set_first_delay_left(seconds: float) -> void:
	_first_delay_left = seconds


## 当前事件剩余时长（无事件返回 0）
func active_remaining() -> float:
	if _active_timer == null or not is_instance_valid(_active_timer):
		return 0.0
	return _active_timer.time_left


## 触发资格（自动触发路径用；force_trigger 不受 run_active 门控，供测试直调）
func can_trigger() -> bool:
	return ENABLED and _run_active and _active_id == &"" and _first_delay_left <= 0.0 and _cooldown_left <= 0.0


## 自动触发路径单步检查：资格满足则按权重掷签并启动事件。返回是否触发。
## （注册表为空/抽签落空返回 false；不会出现「返回 true 但无事件」）
func try_trigger() -> bool:
	if not can_trigger():
		return false
	if randf() >= TRIGGER_CHANCE:
		return false
	var id := _pick_event_id()
	if id == &"":
		return false  # 空注册表防御（_pick_event_id 落空）
	_start_event(id)
	return true


## 测试/诊断：强制启动指定事件（进行中/未注册 id 返回 false；不受概率与冷却门控）
func force_trigger(p_event_id: StringName) -> bool:
	if not EVENT_FACTORIES.has(p_event_id) or _active_id != &"":
		return false
	_start_event(p_event_id)
	return true


## 立即结束进行中的事件（返航/死亡/离场清理；效果随 fog_event_ended 一并复位）
func end_active() -> void:
	if _active_id == &"":
		return
	_end_event()


## 已生成的伪敌机（测试插桩；委托进行中的 FakeEnemiesEvent，无则空）
func spawned_fakes() -> Array:
	var e := _active_event as FakeEnemiesEvent
	if e == null:
		return []
	return e.spawned_fakes()


## 伪敌机容器（FogEvent 子类挂接点）
func fake_container() -> Node2D:
	return _fake_container


## 方向偏转脉冲转发（DirectionShiftEvent 经此发出；信号在本类声明/发射保持一致）
func emit_direction_shift(dir: Vector2, hold: float) -> void:
	fog_direction_shift.emit(dir, hold)


## 精神错乱覆盖层（FogEvent 子类挂接点）
func overlay_layer() -> CanvasLayer:
	return _confusion_layer


func overlay_rect() -> ColorRect:
	return _confusion_rect


# ---------------- 触发与编排 ----------------


func _process(delta: float) -> void:
	# 事件进行中：逐帧驱动事件自持效果（duration 计时由 _active_timer 负责；暂停随树冻结）
	if _active_id != &"":
		if _active_event != null:
			_active_event.tick(delta)
		return
	if not _run_active:
		return
	if _first_delay_left > 0.0:
		_first_delay_left -= delta
		return
	if _cooldown_left > 0.0:
		_cooldown_left -= delta
		return
	_check_timer -= delta
	if _check_timer <= 0.0:
		_check_timer = CHECK_INTERVAL
		if randf() < TRIGGER_CHANCE:
			_start_event(_pick_event_id())


## 加权随机选事件（weights 缺键回退 1.0；注册表为空返回 &""）
func _pick_event_id() -> StringName:
	var ids := event_ids()
	if ids.is_empty():
		return &""  # 空注册表防御：调用方（try_trigger/_process）据此短路
	var total := 0.0
	for id in ids:
		total += maxf(float(WEIGHTS.get(id, 1.0)), 0.0)
	var roll := randf() * total
	for id in ids:
		roll -= maxf(float(WEIGHTS.get(id, 1.0)), 0.0)
		if roll <= 0.0:
			return id
	return ids[0]


func _start_event(event_id: StringName) -> void:
	# 注册表条目防御：id 未注册或条目非 Callable（外部改坏注册表）时短路
	var factory: Variant = EVENT_FACTORIES.get(event_id)
	if not factory is Callable:
		return
	_active_id = event_id
	_active_event = factory.call() as FogEvent
	if _active_event == null:
		_active_id = &""
		return
	var duration := maxf(float(EVENT_DURATIONS.get(event_id, 6.0)), 0.05)
	# 结束 Timer 先行启动（健壮性）：事件 start 若抛错（子类 bug），timer 仍会到期
	# 触发 _end_event 清理，保证事件不会因部分初始化而永久挂死
	if _active_timer != null and is_instance_valid(_active_timer):
		_active_timer.stop()  # 防御：异常时序下的旧 timer 残留
		_active_timer.queue_free()
	_active_timer = Timer.new()
	_active_timer.one_shot = true
	_active_timer.timeout.connect(_end_event)
	add_child(_active_timer)
	_active_timer.start(duration)
	# 注入迷雾上下文（键约定：系统键见 FogEvent 访问器；通用键 "request_end" 由
	# GameEvent.request_end 使用——事件可主动请求提前结束；回调经 manager 保持信号所有者）
	var ctx := {
		"fake_container": _fake_container,
		"overlay_layer": _confusion_layer,
		"overlay_rect": _confusion_rect,
		"emit_direction_shift": emit_direction_shift,
		"request_end": _end_event,
	}
	_active_event.start(ctx, duration)
	_show_banner(event_id, duration)
	fog_event_started.emit(event_id, duration)


func _end_event() -> void:
	var id := _active_id
	if id == &"":
		return  # 防御：重复结束（timer 回调 + 外部 end_active 竞态）
	_active_id = &""
	if _active_timer != null and is_instance_valid(_active_timer):
		_active_timer.stop()
		_active_timer.queue_free()
		_active_timer = null
	if _active_event != null:
		_active_event.end()  # 事件类清理自持效果（幂等）
	_active_event = null
	_hide_banner()
	_cooldown_left = MIN_INTERVAL
	_check_timer = CHECK_INTERVAL
	fog_event_ended.emit(id)


# ---------------- 事件横幅（短暂提示当前事件名） ----------------


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
