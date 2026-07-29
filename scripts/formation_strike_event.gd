class_name FormationStrikeEvent
extends Node
## 轰炸编队事件编排（docs/FORMATION_STRIKE_EVENT.md）：最低优先级随机遭遇——
## IDLE → FORMATION_ENTER（自屏顶外靠近）→ FORMATION_TURN（90° 转航向）
## → BOMBING_RUN（横穿交错投弹）→ FORMATION_EXIT（加速离场）→ IDLE（冷却）。
## 不冻结 Boss 调度、不暂停普通波次；可被返航 abort() 打断（无结算，冷却照计）。
## 编队锚点运动与战机偏移/朝向由本节点 _process 驱动；状态计时全在 _process，
## 不产生 Timer 节点。动态实体（战机/炸弹）一律挂 Main 下（清场/测试遍历可见）。

const COMM_OVERLAY_SCRIPT: GDScript = preload("res://scripts/comm_overlay.gd")

enum State { IDLE, FORMATION_ENTER, FORMATION_TURN, BOMBING_RUN, FORMATION_EXIT }

## 离场段时长（设计文档 §3 状态机常量，不在 §5 可调表内）
const EXIT_TIME := 1.5
## 僚机楔形偏移步进（后掠 ±55px 递增）
const WING_STEP := 55.0
## 同机两次投弹间隔（设计文档 §3 常量）
const BOMB_STAGGER := 0.4

## 配置（读 balance.json formation_strike_event 段，脚本值为缺键回退，两者保持一致）
var MIN_SCORE := 500
var COOLDOWN := 50.0
var CRAFT_COUNTS: Dictionary = {"easy": 3, "medium": 4, "hard": 5}
var CRAFT_HP_BASE := 60
var CRAFT_SCORE := 200
var APPROACH_SPEED := 260.0
var APPROACH_Y := 260.0  # 接近高度（相对视野上缘偏移）
var TURN_TIME := 1.2
var RUN_SPEED := 340.0
var BOMB_INTERVAL := 0.35
var BOMBS_PER_CRAFT := 2
var BOMB_FALL_SPEED := 300.0
var BOMB_FUSE := 1.2
var BOMB_DAMAGE := 20
var BOMB_RADIUS := 120.0
var REWARD_ALL_CLEAR := 200

var _state: State = State.IDLE
var _state_time: float = 0.0
var _cooldown_left: float = 0.0
var _anchor: Vector2 = Vector2.ZERO
var _heading: float = PI / 2.0  # 编队航向角（Vector2.RIGHT.rotated 语义；初始 +y 下降）
var _turn_target: float = 0.0
var _speed: float = 0.0
var _exit_speed: float = 0.0
var _crafts: Array[FormationCraft] = []  # 稳定槽位：被击坠置 null，编队不收缩
var _offsets: Array[Vector2] = []
var _alive: int = 0
var _drop_times: Array[float] = []
var _drop_craft: Array[int] = []
var _drop_index: int = 0
## 已投弹计数（测试可观测）
var _dropped: int = 0
var _comm: CommOverlay = null
var _spawner: Node = null


func _ready() -> void:
	add_to_group("formation_strike_event")
	MIN_SCORE = GameState.cfg("formation_strike_event.min_score", MIN_SCORE)
	COOLDOWN = GameState.cfg("formation_strike_event.cooldown", COOLDOWN)
	CRAFT_COUNTS = GameState.cfg("formation_strike_event.craft_counts", CRAFT_COUNTS)
	CRAFT_HP_BASE = GameState.cfg("formation_strike_event.craft_hp_base", CRAFT_HP_BASE)
	CRAFT_SCORE = GameState.cfg("formation_strike_event.craft_score", CRAFT_SCORE)
	APPROACH_SPEED = GameState.cfg("formation_strike_event.approach_speed", APPROACH_SPEED)
	APPROACH_Y = GameState.cfg("formation_strike_event.approach_y", APPROACH_Y)
	TURN_TIME = GameState.cfg("formation_strike_event.turn_time", TURN_TIME)
	RUN_SPEED = GameState.cfg("formation_strike_event.run_speed", RUN_SPEED)
	BOMB_INTERVAL = GameState.cfg("formation_strike_event.bomb_interval", BOMB_INTERVAL)
	BOMBS_PER_CRAFT = GameState.cfg("formation_strike_event.bombs_per_craft", BOMBS_PER_CRAFT)
	BOMB_FALL_SPEED = GameState.cfg("formation_strike_event.bomb_fall_speed", BOMB_FALL_SPEED)
	BOMB_FUSE = GameState.cfg("formation_strike_event.bomb_fuse", BOMB_FUSE)
	BOMB_DAMAGE = GameState.cfg("formation_strike_event.bomb_damage", BOMB_DAMAGE)
	BOMB_RADIUS = GameState.cfg("formation_strike_event.bomb_radius", BOMB_RADIUS)
	REWARD_ALL_CLEAR = GameState.cfg("formation_strike_event.reward_all_clear", REWARD_ALL_CLEAR)
	_comm = COMM_OVERLAY_SCRIPT.new() as CommOverlay
	add_child(_comm)
	_spawner = get_tree().get_first_node_in_group("spawner")


func is_active() -> bool:
	return _state != State.IDLE


## 触发条件（最低优先级）：自身 IDLE 且冷却结束、分数达标、Boss 未激活、精英炮塔事件未激活。
## 掷签间隔/概率由 spawner 侧持有（elite 事件在本事件之前检查，本 tick 先启动则 is_active 拦截）。
func can_trigger() -> bool:
	if _state != State.IDLE or _cooldown_left > 0.0 or GameState.score < MIN_SCORE:
		return false
	if _spawner != null and is_instance_valid(_spawner):
		if _spawner._boss_active:
			return false
		if _spawner._event != null and _spawner._event.is_active():
			return false
	return true


## 事件启动（互斥检查通过后由 spawner 调用）
func start() -> void:
	if _state != State.IDLE:
		return
	_state = State.FORMATION_ENTER
	_state_time = 0.0
	_heading = PI / 2.0
	_speed = APPROACH_SPEED
	_dropped = 0
	# 占用波次槽：事件期间暂停普通波次（结束/打断时恢复）
	if _spawner != null and is_instance_valid(_spawner):
		_spawner._waves_paused = true
	var view := GameState.view_world_rect()
	var x0 := randf_range(view.position.x + view.size.x * 0.4, view.position.x + view.size.x * 0.6)
	_anchor = Vector2(x0, view.position.y - 120.0)
	# 生成编队：长机居中，僚机后掠 ±55px 递增（楔形，槽位稳定）
	var count := int(CRAFT_COUNTS.get(String(GameState.difficulty), 4))
	# HP 三级乘算：基准 × 难度档 × 对局进程 ramp（与普通敌机同口径）
	var hp := maxi(1, int(roundf(CRAFT_HP_BASE * GameState.enemy_hp_multiplier() * GameState.enemy_hp_ramp())))
	_crafts.clear()
	_offsets.clear()
	_offsets.append(Vector2.ZERO)
	for i in range(1, count):
		var side := -1.0 if i % 2 == 1 else 1.0
		var step := float((i + 1) / 2)
		_offsets.append(Vector2(side * WING_STEP * step, WING_STEP * step))
	for i in count:
		var craft := FormationCraft.new()
		craft.setup(hp)
		craft.position = _anchor + _offsets[i]
		craft.rotation = _heading + PI / 2.0
		craft.died.connect(_on_craft_died.bind(i))
		get_parent().add_child(craft)
		_crafts.append(craft)
	_alive = count
	_comm.show_line("FBQ_WARN")


## 返航打断：编队立即解散离场，无结算，冷却照计（已投放的炸弹自然存续）
func abort() -> void:
	if _state == State.IDLE:
		return
	_free_crafts()
	_state = State.IDLE
	_cooldown_left = COOLDOWN
	_resume_waves()


func _process(delta: float) -> void:
	if _state == State.IDLE:
		if _cooldown_left > 0.0:
			_cooldown_left -= delta
		return
	_state_time += delta
	match _state:
		State.FORMATION_ENTER:
			_anchor.y += APPROACH_SPEED * delta
			if _anchor.y >= GameState.view_world_rect().position.y + APPROACH_Y:
				_begin_turn()
		State.FORMATION_TURN:
			var t := clampf(_state_time / TURN_TIME, 0.0, 1.0)
			_heading = lerp_angle(PI / 2.0, _turn_target, t)
			_speed = lerpf(APPROACH_SPEED, RUN_SPEED, t)
			_anchor += Vector2.RIGHT.rotated(_heading) * _speed * delta
			if t >= 1.0:
				_begin_run()
		State.BOMBING_RUN:
			_anchor += Vector2.RIGHT.rotated(_heading) * RUN_SPEED * delta
			_process_drops()
			var view := GameState.view_world_rect()
			if _drop_index >= _drop_times.size() or _anchor.x < view.position.x - 120.0 or _anchor.x > view.end.x + 120.0:
				_begin_exit()
		State.FORMATION_EXIT:
			_exit_speed += 420.0 * delta
			_anchor += Vector2.RIGHT.rotated(_heading) * _exit_speed * delta
			if _state_time >= EXIT_TIME:
				_finish()
	_update_crafts()


## 转航向：朝较远侧缘方向 90° 转向
func _begin_turn() -> void:
	_state = State.FORMATION_TURN
	_state_time = 0.0
	var view := GameState.view_world_rect()
	_turn_target = 0.0 if _anchor.x < view.position.x + view.size.x * 0.5 else PI


## 横穿投弹：构建交错投弹时刻表（长机先投，僚机错开 bomb_interval，同机间隔 0.4s）
func _begin_run() -> void:
	_state = State.BOMBING_RUN
	_state_time = 0.0
	_drop_times.clear()
	_drop_craft.clear()
	_drop_index = 0
	for k in BOMBS_PER_CRAFT:
		for i in _crafts.size():
			_drop_times.append(float(i) * BOMB_INTERVAL + float(k) * BOMB_STAGGER)
			_drop_craft.append(i)


## 离场：沿当前航向加速穿出侧缘
func _begin_exit() -> void:
	_state = State.FORMATION_EXIT
	_state_time = 0.0
	_exit_speed = RUN_SPEED


## 离场结束：清理剩余战机，回 IDLE 进冷却
func _finish() -> void:
	_free_crafts()
	_state = State.IDLE
	_cooldown_left = COOLDOWN
	_resume_waves()


## 恢复普通波次（事件结束/打断时；精英炮塔事件可能同时持有暂停，以其自身恢复为准）
func _resume_waves() -> void:
	if _spawner != null and is_instance_valid(_spawner):
		_spawner._waves_paused = false


## 按时刻表投弹：投弹点即当前位置正下方；已毁机跳过（时刻表照走）
func _process_drops() -> void:
	while _drop_index < _drop_times.size() and _state_time >= _drop_times[_drop_index]:
		var idx := _drop_craft[_drop_index]
		_drop_index += 1
		var craft := _crafts[idx]
		if craft == null or not is_instance_valid(craft):
			continue
		var bomb := FormationBomb.new()
		var dir := Vector2.RIGHT.rotated(_heading)
		# 炸弹伤害随对局进程 ramp（与敌弹同一系数）
		bomb.setup(
			Vector2(dir.x * RUN_SPEED * 0.35, BOMB_FALL_SPEED),
			BOMB_FUSE,
			maxi(1, int(roundf(BOMB_DAMAGE * GameState.enemy_damage_ramp()))),
			BOMB_RADIUS
		)
		bomb.position = craft.position + Vector2(0.0, 18.0)
		get_parent().add_child(bomb)
		_dropped += 1


## 编队驱动：位置 = 锚点 + 随航向旋转的楔形偏移；机头朝航向
func _update_crafts() -> void:
	for i in _crafts.size():
		var craft := _crafts[i]
		if craft == null or not is_instance_valid(craft):
			continue
		craft.position = _anchor + _offsets[i].rotated(_heading - PI / 2.0)
		craft.rotation = _heading + PI / 2.0


## 击坠：单机得分（add_score 内乘难度倍率）；全歼 → 全歼奖励 + 提前离场
func _on_craft_died(craft: FormationCraft, index: int) -> void:
	if index >= 0 and index < _crafts.size() and _crafts[index] == craft:
		_crafts[index] = null
	_alive = maxi(0, _alive - 1)
	GameState.add_score(CRAFT_SCORE)
	if _alive == 0 and _state != State.IDLE and _state != State.FORMATION_EXIT:
		GameState.add_score(REWARD_ALL_CLEAR)
		_begin_exit()


func _free_crafts() -> void:
	for craft in _crafts:
		if craft != null and is_instance_valid(craft):
			craft.queue_free()
	_crafts.clear()
	_offsets.clear()
	_alive = 0
