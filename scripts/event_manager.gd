class_name GameEventManager
extends Node
## 统一游戏事件管理器（docs/EVENT_MANAGER.md）：批量管理全部随机游戏事件。
## 挂载：GameState autoload 子节点（维持唯一 autoload 约定；经 GameState.events 全局访问）。
## 设计要点：
##   - 统一注册表 EVENT_FACTORIES（id -> 工厂 Callable，唯一事实源）：迷雾 4 事件默认注册，
##     遭遇事件（精英炮塔/轰炸编队）由 main._ready 经 register_encounter() 注入缓存单例；
##   - 分组并发：fog | encounter 两组，组内单事件并发、组间可并行（保持现状：迷雾事件
##     可与遭遇事件并行，遭遇事件彼此/Boss 互斥）；
##   - 统一触发策略：fog 组沿用 fog_events.* 配置（enabled/trigger_chance/check_interval/
##     min_interval/first_delay/weights/durations）；encounter 组沿用 elite_turret_event.* /
##     formation_strike_event.* 的 trigger_interval/trigger_chance/min_score（balance.json
##     key 零变化，仅读取方移动），替换 spawner._process 内联触发 + ScheduledEventTrigger；
##   - 统一生命周期与信号：event_started/event_ended；fog 事件走 GameEvent 契约
##     （start(ctx,duration) → tick → 到期 end，context 由迷雾门面构建）；遭遇事件保持
##     Node 形态（自驱 FSM），管理器调 start()/abort() 并轮询 is_active() 检测结束，
##     冷却/互斥经事件公开 API（can_trigger/cooldown_left，测试面不变）；
##   - 接线门控：_fog_wired 由迷雾门面 wire() 开启（阶段收敛），_run_active 由 main 设置
##     （current_scene == self）；遭遇触发门控 = 注入 spawner 处理中（set_process(false)
##     语义与现状一致）+ 事件 can_trigger。
## 新增事件：GameEvent 子类（纯效果）或 Node 事件（实体/FSM）+ 注册表一行 + 分组/触发配置。

## 统一事件信号：事件开始（duration 秒；遭遇事件为 0，FSM 自驱时长）
signal event_started(event_id: StringName, duration: float)
## 统一事件信号：事件结束（迷雾到期/提前结束；遭遇 FSM 回 IDLE）
signal event_ended(event_id: StringName)

## 分组常量（组内单事件并发，组间并行）
const GROUP_FOG := &"fog"
const GROUP_ENCOUNTER := &"encounter"

## 事件工厂注册表（唯一事实源；迷雾默认注册，遭遇经 register_encounter 注入）
var EVENT_FACTORIES: Dictionary = {
	&"fake_enemies": func() -> GameEvent: return FakeEnemiesEvent.new(),
	&"mental_confusion": func() -> GameEvent: return ConfusionEvent.new(),
	&"bullet_malfunction": func() -> GameEvent: return BulletMalfunctionEvent.new(),
	&"direction_shift": func() -> GameEvent: return DirectionShiftEvent.new(),
}

# ---------------- fog 组配置（balance.json fog_events.*；脚本值为缺键回退） ----------------

var FOG_ENABLED := true
var FOG_TRIGGER_CHANCE := 0.35
var FOG_CHECK_INTERVAL := 3.0
var FOG_MIN_INTERVAL := 12.0
var FOG_FIRST_DELAY := 25.0
var FOG_WEIGHTS: Dictionary = {
	&"fake_enemies": 1.0,
	&"mental_confusion": 1.0,
	&"bullet_malfunction": 1.0,
	&"direction_shift": 1.0,
}
var FOG_EVENT_DURATIONS: Dictionary = {
	&"fake_enemies": 8.0,
	&"mental_confusion": 6.0,
	&"bullet_malfunction": 7.0,
	&"direction_shift": 6.0,
}

# ---------------- encounter 组配置（balance.json elite_turret_event.* / formation_strike_event.*） ----------------

## 遭遇事件触发策略（id -> {interval, chance, min_score}；register_encounter 时读 balance）
var ENCOUNTER_CONFIG: Dictionary = {}

# ---------------- 运行时状态 ----------------

var _run_active := false
## 迷雾组是否已接线（GameState 在迷雾门面 wire() 时开启；未接线则本组完全惰性，
## 保证分阶段迁移期间与旧 FogEventManager 驱动不重叠）
var _fog_wired := false
var _fog_active_id: StringName = &""
var _fog_active_event: GameEvent = null
var _fog_timer: Timer = null
var _fog_cooldown_left := 0.0
var _fog_first_delay_left := 0.0
var _fog_check_timer := 0.0
## 遭遇事件注册顺序（触发检查按注册序；main 先注册 elite 再 formation，保持原优先级）
var _encounter_order: Array[StringName] = []
## 遭遇触发策略计时器（id -> 剩余秒）
var _encounter_timers: Dictionary = {}
## 遭遇事件活跃快照（id -> bool；轮询检测结束发 event_ended）
var _encounter_active: Dictionary = {}
## Q13（2026-08-05）：遭遇结束信号待发集合——end_active 打断后 FSM 未立即回 IDLE 时
## 记 pending，由轮询在检测到回 IDLE 后统一补发（防双发/发在事件仍活跃时）
var _encounter_end_pending: Dictionary = {}
## 当前激活的遭遇事件 id（无则 &""）
var _encounter_active_id: StringName = &""
## spawner 依赖注入（main._ready 调用；遭遇触发门控 + 特殊槽通知，A5 依赖注入延续）
var _spawner: Node = null


func _ready() -> void:
	_load_balance()
	_fog_check_timer = FOG_CHECK_INTERVAL
	_fog_first_delay_left = FOG_FIRST_DELAY


func _load_balance() -> void:
	FOG_ENABLED = bool(GameState.cfg("fog_events.enabled", FOG_ENABLED))
	FOG_TRIGGER_CHANCE = float(GameState.cfg("fog_events.trigger_chance", FOG_TRIGGER_CHANCE))
	FOG_CHECK_INTERVAL = maxf(float(GameState.cfg("fog_events.check_interval", FOG_CHECK_INTERVAL)), 0.1)  # H15 族：≤0 每帧掷签
	FOG_MIN_INTERVAL = maxf(float(GameState.cfg("fog_events.min_interval", FOG_MIN_INTERVAL)), 0.0)
	FOG_FIRST_DELAY = maxf(float(GameState.cfg("fog_events.first_delay", FOG_FIRST_DELAY)), 0.0)
	var weights: Variant = GameState.cfg("fog_events.weights", FOG_WEIGHTS)
	if weights is Dictionary:
		FOG_WEIGHTS = weights
	var durations: Variant = GameState.cfg("fog_events.durations", FOG_EVENT_DURATIONS)
	if durations is Dictionary:
		FOG_EVENT_DURATIONS = durations
	# 遭遇触发策略（与 spawner 原读键一致，balance.json 零变化）
	ENCOUNTER_CONFIG = {
		&"elite_turret":
		{
			"interval": maxf(float(GameState.cfg("elite_turret_event.trigger_interval", 45.0)), 0.1),
			"chance": clampf(float(GameState.cfg("elite_turret_event.trigger_chance", 0.35)), 0.0, 1.0),
			"min_score": int(GameState.cfg("elite_turret_event.min_score", 800)),
		},
		&"formation_strike":
		{
			"interval": maxf(float(GameState.cfg("formation_strike_event.trigger_interval", 40.0)), 0.1),
			"chance": clampf(float(GameState.cfg("formation_strike_event.trigger_chance", 0.30)), 0.0, 1.0),
			"min_score": int(GameState.cfg("formation_strike_event.min_score", 500)),
		},
	}


## P4（2026-08-05）：配置重载公开入口（GameState.reload_balance 联动——原诊断/测试注入
## 路径只刷平衡缓存，事件触发策略/fog 配置停留旧值，与运行时不一致）
func reload_config() -> void:
	_load_balance()


# ---------------- 对外公开接口（A1 约定：测试/诊断经公开接口） ----------------


func is_run_active() -> bool:
	return _run_active


## 对局活跃开关（main._ready/_exit_tree 设置；非活跃时强制结束进行中的迷雾事件）
## Q10/Q12（2026-08-05）：激活时重置遭遇触发计时与 fog 开局保护/检查计时——
## 原实现两者仅注册/接线时初始化且挂 autoload，死亡重开/重进 main 继承上局剩余值
## （遭遇计时可 ≤0 → 新局开局即触发精英/编队；fog 每进程一次保护、第二局开局即触发）
func set_run_active(active: bool) -> void:
	if active == _run_active:
		return
	_run_active = active
	if not active:
		_end_fog()
		return
	for id in _encounter_timers:
		var cfg: Dictionary = ENCOUNTER_CONFIG.get(id, {})
		_encounter_timers[id] = maxf(float(cfg.get("interval", 45.0)), 0.1)
	_fog_first_delay_left = FOG_FIRST_DELAY
	_fog_check_timer = FOG_CHECK_INTERVAL
	# 2026-08-06 审计：fog 冷却重置（Q12 同族遗漏）——上局事件结束残留的
	# _fog_cooldown_left 会额外推迟新局首个迷雾事件（最晚 12s）
	_fog_cooldown_left = 0.0


## 迷雾组接线（GameState 在迷雾门面 wire() 时调用；开启后本组触发/生命周期由本管理器接管）
func activate_fog() -> void:
	_fog_wired = true
	_fog_check_timer = FOG_CHECK_INTERVAL
	_fog_first_delay_left = FOG_FIRST_DELAY


## 遭遇事件注册（main._ready 调用；实例由 main 创建并挂 Main 下，测试经 main.event()/
## main.formation() 访问；注册进统一注册表并初始化触发计时）
func register_encounter(p_id: StringName, p_event: Node) -> void:
	EVENT_FACTORIES[p_id] = func() -> Node: return p_event
	if not _encounter_order.has(p_id):
		_encounter_order.append(p_id)
	if not _encounter_timers.has(p_id):
		var cfg: Dictionary = ENCOUNTER_CONFIG.get(p_id, {})
		_encounter_timers[p_id] = maxf(float(cfg.get("interval", 45.0)), 0.1)


## spawner 依赖注入（main._ready 调用；遭遇触发门控 + 触发时占用特殊槽）
func set_spawner(spawner: Node) -> void:
	_spawner = spawner


## 已注册事件 id 列表（EVENT_FACTORIES 为唯一事实源）
func event_ids() -> Array[StringName]:
	var out: Array[StringName] = []
	for id in EVENT_FACTORIES:
		out.append(id)
	return out


## 指定事件实例（遭遇事件返回缓存单例；迷雾事件返回新实例——仅诊断用）
func event(p_id: StringName) -> Variant:
	var factory: Variant = EVENT_FACTORIES.get(p_id)
	if factory is Callable:
		return factory.call()
	return null


## 指定分组当前激活事件 id（无则 &""）
func active_id(p_group: StringName) -> StringName:
	if p_group == GROUP_FOG:
		return _fog_active_id
	if p_group == GROUP_ENCOUNTER:
		return _encounter_active_id
	return &""


## 指定分组当前激活事件对象（无则 null）
func active_event(p_group: StringName) -> Variant:
	if p_group == GROUP_FOG:
		return _fog_active_event
	if p_group == GROUP_ENCOUNTER:
		return _event_for(_encounter_active_id)
	return null


## 指定分组是否可触发（fog：启用 + run_active + 无进行中 + 开局保护/冷却结束）
func can_trigger_group(p_group: StringName) -> bool:
	if p_group == GROUP_FOG:
		return (
			_fog_wired
			and FOG_ENABLED
			and _run_active
			and _fog_active_id == &""
			and _fog_first_delay_left <= 0.0
			and _fog_cooldown_left <= 0.0
		)
	return false


## 指定分组自动触发路径单步检查（fog：资格满足则按权重掷签并启动；返回是否触发）
func try_trigger_group(p_group: StringName) -> bool:
	if p_group == GROUP_FOG:
		if not can_trigger_group(GROUP_FOG):
			return false
		if randf() >= FOG_TRIGGER_CHANCE:
			return false
		var id := _pick_fog_id()
		if id == &"":
			return false  # 空注册表防御
		return _start_fog(id)
	return false


## 强制启动指定事件（进行中/未注册返回 false；不受概率与冷却门控，测试/诊断直调）
func force_trigger(p_id: StringName) -> bool:
	var factory: Variant = EVENT_FACTORIES.get(p_id)
	if not factory is Callable:
		return false
	if group_of(p_id) == GROUP_FOG:
		# 迷雾组未接线（分阶段迁移期间）或已有进行中事件 → 拒触发
		if not _fog_wired or _fog_active_id != &"":
			return false
		return _start_fog(p_id)
	if group_of(p_id) == GROUP_ENCOUNTER:
		# 遭遇组单事件并发（含手动 start 兜底登记，_encounter_active_id 为准）
		if _encounter_active_id != &"":
			return false
		var ev: Node = factory.call() as Node
		if ev == null or not is_instance_valid(ev) or (ev.has_method("is_active") and ev.is_active()):
			return false
		_start_encounter(p_id, ev)
		return true
	return false


## 立即结束指定分组进行中的事件（fog：清理效果；encounter：abort 打断）
func end_active(p_group: StringName) -> void:
	if p_group == GROUP_FOG:
		_end_fog()
	elif p_group == GROUP_ENCOUNTER:
		var id := _encounter_active_id
		if id == &"":
			return
		var ev := _event_for(id)
		if ev != null and is_instance_valid(ev) and ev.has_method("abort"):
			ev.abort()
		_encounter_active_id = &""
		# Q13（2026-08-05）：event_ended 统一由轮询在 FSM 回 IDLE 后发——
		# 原实现此处即发 + 轮询再发 = 双发且第二次发在事件仍活跃时；
		# 同步回 IDLE 则本处补发，异步则记 pending 由轮询补发
		var still_active: bool = ev != null and is_instance_valid(ev) and (ev.has_method("is_active") and ev.is_active())
		if still_active:
			_encounter_end_pending[id] = true
		else:
			event_ended.emit(id)


## 全部事件终止（返航/死亡路径：迷雾清除 + 遭遇打断）
func end_all() -> void:
	_end_fog()
	end_active(GROUP_ENCOUNTER)


## 测试/诊断：直接设定 fog 组冷却剩余（压缩时长确定性测试，不动 balance.json）
func set_cooldown_left(seconds: float) -> void:
	_fog_cooldown_left = seconds


func cooldown_left() -> float:
	return _fog_cooldown_left


## 测试/诊断：直接设定 fog 组开局保护剩余
func set_first_delay_left(seconds: float) -> void:
	_fog_first_delay_left = seconds


## 测试/诊断：fog 开局保护剩余（Q12 断言用）
func first_delay_left() -> float:
	return _fog_first_delay_left


## 测试/诊断：直接设定 fog 检查计时剩余（压缩检查周期，确定性测试）
func set_check_timer_left(seconds: float) -> void:
	_fog_check_timer = maxf(seconds, 0.0)


## 测试/诊断：遭遇事件触发计时剩余（Q10 断言用）
func encounter_timer_remaining(p_id: StringName) -> float:
	return float(_encounter_timers.get(p_id, 0.0))


## 测试/诊断：直接设定遭遇事件触发计时剩余（压缩时长确定性测试）
func set_encounter_timer_remaining(p_id: StringName, seconds: float) -> void:
	_encounter_timers[p_id] = maxf(seconds, 0.0)


## 当前 fog 事件剩余时长（无事件返回 0）
func active_remaining() -> float:
	if _fog_timer == null or not is_instance_valid(_fog_timer):
		return 0.0
	return _fog_timer.time_left


# ---------------- 触发与编排 ----------------


func _process(delta: float) -> void:
	_poll_encounters()
	# fog 组（未接线前惰性，避免与旧 FogEventManager 双驱动）
	if _fog_wired:
		if _fog_active_id != &"":
			# 事件进行中：逐帧驱动事件自持效果（duration 计时由 _fog_timer 负责；
			# 运行中事件不受 enabled 总开关关闭影响，跑完自然结束）
			if _fog_active_event != null:
				_fog_active_event.tick(delta)
		elif _run_active and FOG_ENABLED:  # Q07：总开关关闭时自动触发路径完全惰性（原仅 can_trigger_group 检查，生产无人调用）
			if _fog_first_delay_left > 0.0:
				_fog_first_delay_left -= delta
			elif _fog_cooldown_left > 0.0:
				_fog_cooldown_left -= delta
			else:
				_fog_check_timer -= delta
				if _fog_check_timer <= 0.0:
					_fog_check_timer = FOG_CHECK_INTERVAL
					if randf() < FOG_TRIGGER_CHANCE:
						_start_fog(_pick_fog_id())
	# encounter 组（门控：注入 spawner 处理中——set_process(false)/暂停语义与现状一致；
	# is_processing() 反映 set_process 维度，can_process() 反映树/暂停维度）
	if _spawner != null and is_instance_valid(_spawner) and _spawner.is_processing() and _spawner.can_process():
		_tick_encounter_triggers(delta)


## 遭遇事件触发检查（镜像 spawner._process 原逻辑 + ScheduledEventTrigger 语义）：
## 按注册序逐个——事件可触发（can_trigger + Boss 互斥 + 精英事件额外要求编队不在场）
## 且分数门槛通过才推进计时，计时归零按概率掷签启动
func _tick_encounter_triggers(delta: float) -> void:
	var score := GameState.score
	for id in _encounter_order:
		var ev := _event_for(id)
		if ev == null or not is_instance_valid(ev) or (ev.has_method("is_active") and ev.is_active()):
			continue
		if not _encounter_can_trigger(id, ev):
			continue
		var cfg: Dictionary = ENCOUNTER_CONFIG.get(id, {})
		var min_score: int = int(cfg.get("min_score", 0))
		if score < min_score:
			continue  # 分数门槛未过：计时不推进（镜像 ScheduledEventTrigger）
		var interval: float = maxf(float(cfg.get("interval", 45.0)), 0.1)
		var timer: float = float(_encounter_timers.get(id, interval))
		timer -= delta
		if timer <= 0.0:
			_encounter_timers[id] = interval
			if randf() < float(cfg.get("chance", 0.3)):
				_start_encounter(id, ev)
		else:
			_encounter_timers[id] = timer


## 遭遇事件触发资格：事件自身 can_trigger（冷却/分数/母舰）+ Boss 未激活 +
## 精英事件额外要求编队不在场（镜像 spawner 原互斥链）
func _encounter_can_trigger(p_id: StringName, ev: Node) -> bool:
	if ev.has_method("can_trigger") and not (ev.call("can_trigger") as bool):
		return false
	if _spawner != null and is_instance_valid(_spawner) and _spawner.has_method("is_boss_active") and _spawner.is_boss_active():
		return false
	if p_id == &"elite_turret":
		for other in _encounter_order:
			if other == p_id:
				continue
			var o := _event_for(other)
			if o != null and is_instance_valid(o) and (o.has_method("is_active") and o.is_active()):
				return false
	return true


## 遭遇事件启动：调事件 start()（事件内部处理波次/Boss 钩子），登记活跃并广播
func _start_encounter(p_id: StringName, ev: Node) -> void:
	ev.start()
	_encounter_active_id = p_id
	_encounter_active[p_id] = true
	event_started.emit(p_id, 0.0)
	# 事件占用特殊槽（镜像 spawner 原 _waves_since_special = 0）
	if _spawner != null and is_instance_valid(_spawner) and _spawner.has_method("notify_event_triggered"):
		_spawner.notify_event_triggered()


## 轮询遭遇事件结束（FSM 回 IDLE → 广播 event_ended；手动 start 亦被覆盖检测；
## Q13：pending 打断在检测到回 IDLE 后补发，信号恒在事件不活跃时发、恒只发一次）
func _poll_encounters() -> void:
	for id in _encounter_order:
		var ev := _event_for(id)
		var active: bool = ev != null and is_instance_valid(ev) and (ev.has_method("is_active") and ev.is_active())
		_encounter_active[id] = active
		if _encounter_end_pending.has(id) and not active:
			_encounter_end_pending.erase(id)
			_encounter_active_id = &""
			event_ended.emit(id)
		elif _encounter_active_id == id and not active:
			_encounter_active_id = &""
			event_ended.emit(id)
		elif active and _encounter_active_id == &"":
			_encounter_active_id = id  # 手动 start 兜底登记


## 加权随机选 fog 事件（weights 缺键回退 1.0；注册表为空返回 &""；
## P4：全零权重退化为均匀随机——原实现恒选首个（roll=0 立即命中第一项））
func _pick_fog_id() -> StringName:
	var ids: Array[StringName] = []
	for id in EVENT_FACTORIES:
		if group_of(id) == GROUP_FOG:
			ids.append(id)
	if ids.is_empty():
		return &""  # 空注册表防御
	var total := 0.0
	for id in ids:
		total += maxf(float(FOG_WEIGHTS.get(id, 1.0)), 0.0)
	if total <= 0.0:
		return ids[randi() % ids.size()]  # 全零权重：均匀回退
	var roll := randf() * total
	for id in ids:
		roll -= maxf(float(FOG_WEIGHTS.get(id, 1.0)), 0.0)
		if roll <= 0.0:
			return id
	return ids[0]


## 迷雾事件启动：实例化 → duration Timer 先行（健壮性，镜像 FogEventManager）→
## 迷雾门面构建 context 注入 → start + 广播。context 键约定见 FogEvent 访问器；
## 通用键 "request_end" 由 GameEvent.request_end 使用（事件可主动提前结束）。
func _start_fog(p_id: StringName) -> bool:
	var factory: Variant = EVENT_FACTORIES.get(p_id)
	if not factory is Callable:
		return false  # 注册表条目防御：id 未注册或条目非 Callable
	_fog_active_id = p_id
	var ev: GameEvent = factory.call() as GameEvent
	if ev == null:
		_fog_active_id = &""
		return false
	_fog_active_event = ev
	var duration := maxf(float(FOG_EVENT_DURATIONS.get(p_id, 6.0)), 0.05)
	if _fog_timer != null and is_instance_valid(_fog_timer):
		_fog_timer.stop()  # 防御：异常时序下的旧 timer 残留
		_fog_timer.queue_free()
	_fog_timer = Timer.new()
	_fog_timer.one_shot = true
	_fog_timer.timeout.connect(_end_fog)
	add_child(_fog_timer)
	_fog_timer.start(duration)
	# context：迷雾门面构建（视觉容器/覆盖层/方向脉冲回调），request_end 回调指向本管理器
	var layer := _fog_layer()
	var ctx: Dictionary = layer.build_fog_context(_end_fog) if layer != null else {"request_end": _end_fog}
	ev.start(ctx, duration)
	event_started.emit(p_id, duration)
	return true


func _end_fog() -> void:
	var id := _fog_active_id
	if id == &"":
		return  # 防御：重复结束（timer 回调 + 外部 end_active 竞态）
	_fog_active_id = &""
	if _fog_timer != null and is_instance_valid(_fog_timer):
		_fog_timer.stop()
		_fog_timer.queue_free()
		_fog_timer = null
	if _fog_active_event != null:
		_fog_active_event.end()  # 事件类清理自持效果（幂等）
	_fog_active_event = null
	_fog_cooldown_left = FOG_MIN_INTERVAL
	_fog_check_timer = FOG_CHECK_INTERVAL
	event_ended.emit(id)


# ---------------- 内部辅助 ----------------


## 注册表工厂取已注册实例（遭遇缓存单例；fog 事件返回 null 之外的新实例——仅 _event_for 用）
func _event_for(p_id: StringName) -> Node:
	var factory: Variant = EVENT_FACTORIES.get(p_id)
	if factory is Callable:
		var v: Variant = factory.call()
		return v as Node
	return null


## 事件所属分组（遭遇注册序表为准；其余按注册表默认 fog）
func group_of(p_id: StringName) -> StringName:
	if _encounter_order.has(p_id):
		return GROUP_ENCOUNTER
	return GROUP_FOG


## 迷雾门面（效果层/context 构建；GameState.fog_events）
func _fog_layer() -> FogEventManager:
	return GameState.fog_events as FogEventManager
