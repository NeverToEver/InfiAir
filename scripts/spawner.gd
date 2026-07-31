extends Node
## 敌机生成器：波次化刷新（普通波成组均布入场、按分数阶段解锁机型）+ 特殊槽调度
## （每 3~4 个普通波一个精英波；Boss/精英/事件占用特殊槽，精英/Boss 击杀后追加休整波次）
## + Boss 触发（3 种轮换）。

signal boss_spawned(boss: Boss)
signal boss_warning

const ENEMY_SCENE: PackedScene = preload("res://scenes/enemy.tscn")
const BOSS_SCENE: PackedScene = preload("res://scenes/boss.tscn")

## 普通机型配置表（贴图即机型，数值差异化；弹种池仅 single/spread）
## scale 为纯视觉缩放：以锁定环/碰撞提示等指示器尺寸为锚（不动指示器），舰船视觉应明显大于指示器
## HP 定标（A11）：玩家弹伤 10、射速 0.15s 下 TTK≈1.2s（对齐原作 DPS 平衡器稳态）
static var ENEMY_TYPES: Array[Dictionary] = [
	{  # 1 型 均衡
		"texture": preload("res://assets/sprites/enemy_ship_1.png"),
		"strategies": [&"straight", &"sine"] as Array[StringName],
		"hp": Vector2i(65, 72), "speed": Vector2(115, 145), "score": 100,
		"fire": 0.25, "fire_interval": 2.2, "scale": 0.62, "radius": 34.0,
		"bullet_types": [&"single", &"spread"] as Array[StringName],
	},
	{  # 2 型 高速低 HP
		"texture": preload("res://assets/sprites/enemy_ship_2.png"),
		"strategies": [&"zigzag", &"dive"] as Array[StringName],
		"hp": Vector2i(48, 56), "speed": Vector2(175, 225), "score": 150,
		"fire": 0.3, "fire_interval": 2.4, "scale": 0.62, "radius": 34.0,
		"bullet_types": [&"single", &"spread"] as Array[StringName],
	},
	{  # 3 型 高 HP 慢速
		"texture": preload("res://assets/sprites/enemy_ship_3.png"),
		"strategies": [&"spiral", &"hover"] as Array[StringName],
		"hp": Vector2i(95, 112), "speed": Vector2(75, 100), "score": 200,
		"fire": 0.4, "fire_interval": 2.0, "scale": 0.68, "radius": 38.0,
		"bullet_types": [&"spread", &"single"] as Array[StringName],
	},
	{  # 4 型 高分开火狂
		"texture": preload("res://assets/sprites/enemy_ship_4.png"),
		"strategies": [&"noise", &"hover", &"aggressive"] as Array[StringName],
		"hp": Vector2i(56, 66), "speed": Vector2(120, 155), "score": 250,
		"fire": 0.8, "fire_interval": 1.8, "scale": 0.62, "radius": 34.0,
		"bullet_types": [&"spread", &"single"] as Array[StringName],
	},
]

## 精英机型配置表（弹种池仅 spread/laser）
## HP ≈ 普通均值 ×2.5（对齐原作精英倍率）；radius 与普通机同档
## （A10：原作精英碰撞盒不大于普通机，"精英更大"为疑似 bug 不移植）
static var ELITE_TYPES: Array[Dictionary] = [
	{  # 重甲
		"texture": preload("res://assets/sprites/elite_ship_1.png"),
		"strategies": [&"straight", &"sine"] as Array[StringName],
		"hp": Vector2i(190, 210), "speed": Vector2(75, 95), "score": 400,
		"fire": 0.5, "fire_interval": 2.2, "scale": 0.9, "radius": 38.0, "elite": true,
		"bullet_types": [&"spread"] as Array[StringName],
	},
	{  # 游击
		"texture": preload("res://assets/sprites/elite_ship_2.png"),
		"strategies": [&"zigzag", &"dive", &"noise"] as Array[StringName],
		"hp": Vector2i(135, 155), "speed": Vector2(195, 245), "score": 350,
		"fire": 0.6, "fire_interval": 2.0, "scale": 0.68, "radius": 34.0, "elite": true,
		"bullet_types": [&"laser", &"spread"] as Array[StringName],
	},
	{  # 炮艇
		"texture": preload("res://assets/sprites/elite_ship_3.png"),
		"strategies": [&"hover", &"spiral"] as Array[StringName],
		"hp": Vector2i(170, 190), "speed": Vector2(90, 115), "score": 500,
		"fire": 1.0, "fire_interval": 1.5, "scale": 0.78, "radius": 38.0, "elite": true,
		"bullet_types": [&"spread", &"laser"] as Array[StringName],
	},
]

## 机型 i 在分数 >= UNLOCK_SCORES[i] 时解锁
var UNLOCK_SCORES: Array = [0, 300, 800, 1500]

## 波次节奏：普通波成组刷新，间隔/规模随对局时间 ramp
var WAVE_INTERVAL_START := 7.0
var WAVE_INTERVAL_END := 4.0
var RAMP_TIME := 300.0
var DIFFICULTY_FACTOR := 0.15  # Boss 击杀难度乘数对波次间隔的影响系数
var INTERVAL_MIN := 2.5
var WAVE_SIZE_START := 3
var WAVE_SIZE_END := 5
## 特殊槽：每 SPECIAL_GAP_MIN~MAX 个普通波出一个精英波；Boss/事件触发同样占用并清零计数
var SPECIAL_GAP_MIN := 3
var SPECIAL_GAP_MAX := 4
## 休整：精英/Boss 击杀后追加的普通波次数（计数置负，拉长下一个特殊槽间隔）
var REST_WAVES_AFTER_KILL := 2
var ELITE_WAVE_SIZE := 1
var BOSS_SCORE_STEP := 1500
## Boss 触发最小间隔：分数步进触发需同时满足该时间门（防分数暴涨期连出 Boss）
var BOSS_MIN_INTERVAL := 80.0
var BOSS_TIME_LIMIT := 120.0
## 精英炮塔事件触发参数（docs/ELITE_TURRET_EVENT.md 第 6 节）
var ETV_MIN_SCORE := 800
var ETV_TRIGGER_INTERVAL := 45.0
var ETV_TRIGGER_CHANCE := 0.35
## 轰炸编队事件触发参数（docs/FORMATION_STRIKE_EVENT.md 第 5 节；最低优先级，其余条件由事件 can_trigger 检查）
var FS_TRIGGER_INTERVAL := 40.0
var FS_TRIGGER_CHANCE := 0.30

var _wave_timer: float = 1.5
var _elapsed: float = 0.0
## 特殊槽计数：普通波 +1；精英波/Boss/事件触发清零；精英/Boss 击杀置负（休整）
var _waves_since_special: int = 0
## 本周期特殊槽间隔（计数清零/休整时重抽，SPECIAL_GAP_MIN~MAX）
var _next_special_gap: int = 3
## 敌机悬停带缓存（与 Enemy.HOVER_BAND 同源，相对可见区域顶缘的偏移，供波次锚点分配）
var _hover_band := Vector2(150.0, 430.0)
var _boss_timer: float = 0.0
var _next_boss_score: int = BOSS_SCORE_STEP
var _boss_active: bool = false
## 精英炮塔事件互斥：事件期间 Boss 触发被冻结（到期记 _boss_pending 一次，不累积）
var _boss_frozen: bool = false
var _boss_pending: bool = false
## 事件期间普通波次暂停
var _waves_paused: bool = false
## 事件编排节点（main 在 _ready 登记）
var _event: EliteTurretEvent = null
## 轰炸编队事件编排节点（main 在 _ready 登记；与 Boss/精英炮塔事件按优先级链互斥）
var _formation: FormationStrikeEvent = null
## A4b：事件触发策略统一骨架（定时 + 概率 + 分数门槛，替代原 _event_check_timer 内联）
var _elite_trigger := ScheduledEventTrigger.new(ETV_TRIGGER_INTERVAL, ETV_TRIGGER_CHANCE, ETV_MIN_SCORE)
var _formation_trigger := ScheduledEventTrigger.new(FS_TRIGGER_INTERVAL, FS_TRIGGER_CHANCE)


func _ready() -> void:
	add_to_group("spawner")
	_apply_balance()


## 数值配置注入：机型表数值覆盖（贴图/策略/弹种池留在脚本），常量读入
func _apply_balance() -> void:
	WAVE_INTERVAL_START = GameState.cfg("spawner.wave_interval_start", WAVE_INTERVAL_START)
	WAVE_INTERVAL_END = GameState.cfg("spawner.wave_interval_end", WAVE_INTERVAL_END)
	RAMP_TIME = GameState.cfg("spawner.ramp_time", RAMP_TIME)
	BOSS_SCORE_STEP = GameState.cfg("spawner.boss_score_step", BOSS_SCORE_STEP)
	BOSS_MIN_INTERVAL = GameState.cfg("spawner.boss_min_interval", BOSS_MIN_INTERVAL)
	BOSS_TIME_LIMIT = GameState.cfg("spawner.boss_time_limit", BOSS_TIME_LIMIT)
	DIFFICULTY_FACTOR = GameState.cfg("spawner.difficulty_factor", DIFFICULTY_FACTOR)
	INTERVAL_MIN = GameState.cfg("spawner.interval_min", INTERVAL_MIN)
	UNLOCK_SCORES = GameState.cfg("spawner.unlock_scores", UNLOCK_SCORES)
	WAVE_SIZE_START = int(GameState.cfg("spawner.wave_size_start", WAVE_SIZE_START))
	WAVE_SIZE_END = int(GameState.cfg("spawner.wave_size_end", WAVE_SIZE_END))
	SPECIAL_GAP_MIN = int(GameState.cfg("spawner.special_gap_min", SPECIAL_GAP_MIN))
	SPECIAL_GAP_MAX = int(GameState.cfg("spawner.special_gap_max", SPECIAL_GAP_MAX))
	REST_WAVES_AFTER_KILL = int(GameState.cfg("spawner.rest_waves_after_kill", REST_WAVES_AFTER_KILL))
	ELITE_WAVE_SIZE = int(GameState.cfg("spawner.elite_wave_size", ELITE_WAVE_SIZE))
	var band: Array = GameState.cfg("enemies.hover_band", [_hover_band.x, _hover_band.y])
	_hover_band = Vector2(float(band[0]), float(band[1]))
	ETV_MIN_SCORE = GameState.cfg("elite_turret_event.min_score", ETV_MIN_SCORE)
	ETV_TRIGGER_INTERVAL = GameState.cfg("elite_turret_event.trigger_interval", ETV_TRIGGER_INTERVAL)
	ETV_TRIGGER_CHANCE = GameState.cfg("elite_turret_event.trigger_chance", ETV_TRIGGER_CHANCE)
	FS_TRIGGER_INTERVAL = GameState.cfg("formation_strike_event.trigger_interval", FS_TRIGGER_INTERVAL)
	FS_TRIGGER_CHANCE = GameState.cfg("formation_strike_event.trigger_chance", FS_TRIGGER_CHANCE)
	# A4b：balance 覆盖后同步触发策略配置（_timer 不重置，保持现有节奏）
	_elite_trigger.configure(ETV_TRIGGER_INTERVAL, ETV_TRIGGER_CHANCE, ETV_MIN_SCORE)
	_formation_trigger.configure(FS_TRIGGER_INTERVAL, FS_TRIGGER_CHANCE)
	var normal: Array = GameState.cfg("enemies.types", [])
	for i in mini(normal.size(), ENEMY_TYPES.size()):
		_merge_type(ENEMY_TYPES[i], normal[i])
	var elites: Array = GameState.cfg("elites.types", [])
	for i in mini(elites.size(), ELITE_TYPES.size()):
		_merge_type(ELITE_TYPES[i], elites[i])


func _merge_type(dst: Dictionary, src: Dictionary) -> void:
	if src.has("hp"):
		dst["hp"] = Vector2i(int(src["hp"][0]), int(src["hp"][1]))
	if src.has("speed"):
		dst["speed"] = Vector2(float(src["speed"][0]), float(src["speed"][1]))
	for k in ["score", "fire", "fire_interval", "scale", "radius"]:
		if src.has(k):
			dst[k] = src[k]


## 对外公开接口（A1 修复）：事件互斥/Boss 调度/计时状态封装，禁止跨类直接写 _ 私有字段
func set_elite_event(event: EliteTurretEvent) -> void:
	_event = event


func set_formation_event(event: FormationStrikeEvent) -> void:
	_formation = event


func set_elapsed(seconds: float) -> void:
	_elapsed = seconds


## A7：测试/诊断白盒断言经公开接口（命名语义化）
func spawn_boss(p_type: int = 0) -> void:
	_spawn_boss(p_type)


func spawn_enemy() -> void:
	_spawn_enemy()


func spawn_normal_wave() -> void:
	_spawn_normal_wave()


func wave_size() -> int:
	return _wave_size()


func count_spread_enemies() -> int:
	return _count_spread_enemies()


func pick_bullet_type(config: Dictionary) -> StringName:
	return _pick_bullet_type(config)


func current_interval() -> float:
	return _current_interval()


func set_boss_timer(seconds: float) -> void:
	_boss_timer = seconds


func set_next_boss_score(score_value: int) -> void:
	_next_boss_score = score_value


func set_wave_timer(seconds: float) -> void:
	_wave_timer = seconds


func set_waves_since_special(count: int) -> void:
	_waves_since_special = count


func set_boss_active(active: bool) -> void:
	_boss_active = active


func boss_frozen() -> bool:
	return _boss_frozen


func waves_paused() -> bool:
	return _waves_paused


func boss_pending() -> bool:
	return _boss_pending


func set_boss_pending(pending: bool) -> void:
	_boss_pending = pending


func formation_event() -> FormationStrikeEvent:
	return _formation


func hover_band() -> Vector2:
	return _hover_band


func notify_special_killed() -> void:
	_on_special_killed()


func notify_boss_died() -> void:
	_on_boss_died()


func boss_timer() -> float:
	return _boss_timer


func wave_timer() -> float:
	return _wave_timer


func waves_since_special() -> int:
	return _waves_since_special


func elapsed() -> float:
	return _elapsed


func set_boss_frozen(frozen: bool) -> void:
	_boss_frozen = frozen


func set_waves_paused(paused: bool) -> void:
	_waves_paused = paused


func is_boss_active() -> bool:
	return _boss_active


func elite_event() -> EliteTurretEvent:
	return _event


## 读取并清除一次 Boss pending（事件解冻时若期间触发过 Boss 则补触发）
func consume_boss_pending() -> bool:
	var was := _boss_pending
	_boss_pending = false
	return was


func trigger_boss() -> void:
	_trigger_boss()


func _process(delta: float) -> void:
	_elapsed += delta
	# Boss 激活与事件暂停期间波次计时冻结（Boss/事件占用波次槽）
	if not _waves_paused and not _boss_active:
		_wave_timer -= delta
		if _wave_timer <= 0.0:
			_wave_timer = _current_interval()
			if _waves_since_special >= _next_special_gap:
				_spawn_elite_wave()
				_waves_since_special = 0
				_draw_special_gap()
			else:
				_spawn_normal_wave()
				_waves_since_special += 1

	_boss_timer += delta
	# 分数触发需同时越过最小间隔（防分数暴涨期战后连出 Boss）；时间兜底不受此限
	if not _boss_active and (
		(GameState.score >= _next_boss_score and _boss_timer >= BOSS_MIN_INTERVAL)
		or _boss_timer >= BOSS_TIME_LIMIT
	):
		# 精英炮塔事件期间 Boss 触发被冻结：只记录一次 pending（重复到期覆盖，不累积）
		if _boss_frozen:
			_boss_pending = true
		else:
			_trigger_boss()
	# 精英炮塔事件触发检查：Boss 优先（Boss 未预警/入场/战斗中且事件可触发时才允许启动；
	# 编队事件激活期间不启动，避免两事件的波次暂停钩子互相提前恢复）
	if (
		_event != null
		and not _boss_active
		and (_formation == null or not _formation.is_active())
		and _event.can_trigger()
	):
		# A4b：触发策略（定时/概率/分数门槛）委托 ScheduledEventTrigger
		if _elite_trigger.tick(delta, GameState.score):
			_event.start()
			_waves_since_special = 0  # 事件占用特殊槽
	# 轰炸编队事件触发检查（最低优先级，在精英炮塔事件检查之后；
	# Boss 激活/精英事件 active/冷却/分数门槛由事件 can_trigger 内检查）
	if _formation != null and _formation.can_trigger():
		if _formation_trigger.tick(delta, GameState.score):
			_formation.start()
			_waves_since_special = 0  # 事件占用特殊槽


func _current_interval() -> float:
	var base := lerpf(WAVE_INTERVAL_START, WAVE_INTERVAL_END, clampf(_elapsed / RAMP_TIME, 0.0, 1.0))
	# 难度倍率：easy ×1.25（更疏）/ medium ×1 / hard ×0.8（更密）
	var interval: float = (
		base
		* GameState.spawn_interval_multiplier()
		/ (1.0 + DIFFICULTY_FACTOR * (GameState.difficulty_multiplier - 1.0))
	)
	return clampf(interval, INTERVAL_MIN, WAVE_INTERVAL_START * GameState.spawn_interval_multiplier())


## 当前波次规模：随对局时间 ramp（WAVE_SIZE_START → WAVE_SIZE_END）
func _wave_size() -> int:
	var t := clampf(_elapsed / RAMP_TIME, 0.0, 1.0)
	return maxi(1, int(roundf(lerpf(float(WAVE_SIZE_START), float(WAVE_SIZE_END), t))))


## 均分槽位取点：范围 [start, start+length] 均分 n 槽，取第 i 槽中心 ±25% 槽宽抖动
func _slot_pos(start: float, length: float, n: int, i: int) -> float:
	var slot := length / float(n)
	return start + slot * (float(i) + 0.5) + randf_range(-0.25, 0.25) * slot


## 当前分数阶段已解锁的普通机型池
func unlocked_types() -> Array[Dictionary]:
	var pool: Array[Dictionary] = []
	for i in ENEMY_TYPES.size():
		if GameState.score >= UNLOCK_SCORES[i]:
			pool.append(ENEMY_TYPES[i])
	return pool


## 当前在屏的 spread 弹种敌机数（离场中的不计）
func _count_spread_enemies() -> int:
	var n := 0
	for node in get_tree().get_nodes_in_group("enemy"):
		var e := node as Enemy
		if e != null and e.bullet_type == &"spread" and not e.is_exiting():
			n += 1
	return n


## 从机型弹种池抽取弹种；spread 超同屏上限时退化（普通→single，精英→laser）。
## 同屏上限按难度取（GameState.spread_enemy_cap：easy 1 / medium 2 / hard 3）。
func _pick_bullet_type(config: Dictionary) -> StringName:
	var pool: Array = config.get("bullet_types", [&"single"])
	var btype: StringName = pool[randi() % pool.size()]
	if btype == &"spread" and _count_spread_enemies() >= GameState.spread_enemy_cap():
		btype = &"laser" if config.get("elite", false) else &"single"
	return btype


## 普通波：成组均布入场（x 按视口均分槽 + 抖动；锚点在悬停带内均分槽 + 抖动，加 view 顶基线）
func _spawn_normal_wave() -> void:
	var pool := unlocked_types()
	var n := _wave_size()
	var view := GameState.view_world_rect()
	for i in n:
		var config: Dictionary = pool[randi() % pool.size()]
		var x := _slot_pos(view.position.x + 60.0, view.size.x - 120.0, n, i)
		var anchor := _slot_pos(view.position.y + _hover_band.x, _hover_band.y - _hover_band.x, n, i)
		_queue_enemy(config, x, anchor)


## 精英波：占用特殊槽，ELITE_WAVE_SIZE 个精英均布入场；击杀触发休整
func _spawn_elite_wave() -> void:
	var view := GameState.view_world_rect()
	for i in ELITE_WAVE_SIZE:
		var config: Dictionary = ELITE_TYPES[randi() % ELITE_TYPES.size()]
		var x := _slot_pos(view.position.x + 60.0, view.size.x - 120.0, ELITE_WAVE_SIZE, i)
		_queue_enemy(config, x, randf_range(view.position.y + _hover_band.x, view.position.y + _hover_band.y), true)


## 单机随机入口（兼容旧调用/测试）：随机 x + 悬停带内随机锚点
func _spawn_enemy() -> void:
	var pool := unlocked_types()
	var config: Dictionary = pool[randi() % pool.size()]
	var view := GameState.view_world_rect()
	_queue_enemy(
		config,
		randf_range(view.position.x + 60.0, view.end.x - 60.0),
		randf_range(view.position.y + _hover_band.x, view.position.y + _hover_band.y)
	)


## 单机入场：0.6s 红色入场预告后敌机才进场（波次与单机入口共用）
func _queue_enemy(config: Dictionary, x: float, anchor: float, special: bool = false) -> void:
	var strategies: Array[StringName] = config["strategies"]
	var strategy := strategies[randi() % strategies.size()]
	var btype := _pick_bullet_type(config)
	var view := GameState.view_world_rect()
	get_parent().add_child(SpawnTelegraph.new(x, view.position.y))
	_schedule(GameState.cfg("spawner.telegraph_duration", SpawnTelegraph.DURATION),
		_on_telegraph_timeout.bind(config, strategy, btype, x, anchor, special))


## 预告计时结束后敌机实际进场
func _on_telegraph_timeout(config: Dictionary, strategy: StringName, btype: StringName, x: float, anchor: float, special: bool) -> void:
	var e := ENEMY_SCENE.instantiate() as Enemy
	e.setup(config, strategy, GameState.difficulty_multiplier, btype)
	e.position = Vector2(x, GameState.view_world_rect().position.y - 60.0)
	e.anchor_y = anchor
	if special:
		e.died.connect(_on_special_killed)
	get_parent().add_child(e)


## 精英/Boss 击杀休整：追加 REST_WAVES_AFTER_KILL 个普通波才再出特殊槽
func _on_special_killed(_enemy: Enemy = null) -> void:
	_waves_since_special = -REST_WAVES_AFTER_KILL
	_draw_special_gap()


## 重抽本周期特殊槽间隔
func _draw_special_gap() -> void:
	_next_special_gap = randi_range(SPECIAL_GAP_MIN, SPECIAL_GAP_MAX)


## Boss-3 召唤的小怪（straight 型 1 型机），立即进场无预告。
## 作为 Main 的子节点，与正常敌机走同一套清场逻辑（返航/结算）。
## 返回实例供调用方标记/编排（Boss 编队齐射）；不需要时可忽略返回值。
func spawn_minion(pos: Vector2) -> Enemy:
	return GameState.enemy_pool.spawn(ENEMY_TYPES[0], &"straight", GameState.difficulty_multiplier, pos)


## Boss 出场流程：警告横幅 + 震动脉冲，2s 后 Boss 才降入。
func _trigger_boss() -> void:
	_boss_active = true
	_waves_since_special = 0  # Boss 占用特殊槽
	boss_warning.emit()
	GameState.shake(GameState.cfg("effects.shake.boss_warning", 14.0))
	_schedule(2.0, _spawn_boss)


## p_type <= 0 时按击杀数轮换：第 N 只 Boss = 第 (N-1)%3+1 种
func _spawn_boss(p_type: int = 0) -> void:
	_boss_active = true
	if p_type <= 0:
		p_type = GameState.boss_kills % 3 + 1
	var boss := BOSS_SCENE.instantiate() as Boss
	boss.setup(GameState.difficulty_multiplier, p_type)
	boss.set_spawner(self)  # A5：依赖注入，替代 Boss 侧 group 现找
	boss.position = Vector2(960.0, GameState.view_world_rect().position.y - 160.0)
	boss.died.connect(_on_boss_died)
	boss.escaped.connect(_on_boss_escaped)
	get_parent().add_child(boss)
	boss_spawned.emit(boss)


func _on_boss_died() -> void:
	_boss_active = false
	_boss_timer = 0.0
	_next_boss_score += BOSS_SCORE_STEP
	_on_special_killed()  # Boss 击杀休整


## Boss 逃跑：不推进轮换、不给休整，仅解除波次/事件占用（Boss 计时重置，之后按分数/时间门再触发同型）
func _on_boss_escaped() -> void:
	_boss_active = false
	_boss_timer = 0.0


## 一次性计时回调。不用协程 await（SceneTreeTimer 或 Timer 均可）：退出时挂起的
## 协程函数状态会泄漏并连带持有其引用的资源；信号连接随 Timer 节点一并释放。
func _schedule(seconds: float, callback: Callable) -> void:
	var timer := Timer.new()
	timer.one_shot = true
	add_child(timer)
	timer.timeout.connect(callback, CONNECT_ONE_SHOT)
	timer.timeout.connect(timer.queue_free, CONNECT_ONE_SHOT)
	timer.start(seconds)
