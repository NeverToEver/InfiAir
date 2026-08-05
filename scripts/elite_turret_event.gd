class_name EliteTurretEvent
extends Node
## 精英炮塔事件编排（docs/ELITE_TURRET_EVENT.md）：
## IDLE → CARRIER_ENTER（航母降入 2s）→ 炮塔升起充能 1.5s → TURRET_ACTIVE（30s 倒计时）
## → 成功（全歼，+500 基础分）/失败（超时撤退）→ CARRIER_EXIT → BOSS_DELAY（4s）→ IDLE。
## 与 Boss 互斥：进入 CARRIER_ENTER 冻结 Boss 调度（到期记 _boss_pending 一次，不累积），
## BOSS_DELAY 结束时解冻并补触发一次。事件期间普通波次暂停（CARRIER_EXIT 起恢复）。

const TURRET_SCENE: PackedScene = preload("res://scenes/turret.tscn")
const COMM_OVERLAY_SCRIPT: GDScript = preload("res://scripts/comm_overlay.gd")

enum State { IDLE, CARRIER_ENTER, TURRET_ACTIVE, CARRIER_EXIT, BOSS_DELAY }

## 配置（读 balance.json elite_turret_event 段，脚本值为缺键回退）
var DURATION := 30.0
var ENTER_TIME := 2.0
var RISE_TIME := 1.5
var BOSS_RESUME_DELAY := 4.0
var TURRET_HP_BASE := 80
var TURRET_COUNTS: Dictionary = {"easy": 3, "medium": 4, "hard": 5}
var FIRE_INTERVAL := Vector2(2.0, 2.4)
var WEAK_LOCK: Dictionary = {
	"turn_rate": 2.0,
	"homing_turn_rate": 1.5,
	"homing_time": 0.6,
	"spread_deg": 7.0,
}
var AMMO_SEQUENCES: Dictionary = {
	"easy": [&"single", &"spread3", &"single"],
	"medium": [&"single", &"spread3", &"laser", &"weak_homing"],
	"hard": [&"spread5", &"laser", &"weak_homing", &"sniper", &"single"],
}
var REWARD_SCORE := 500
var HOVER_Y := 300.0
var COOLDOWN := 60.0

var _state: State = State.IDLE
## A5：spawner 依赖注入（main._ready 经 set_spawner 设置；替代 group 现找）
var _spawner: Node = null
var _carrier: StrikeCarrier = null


## A5：spawner 依赖注入（main._ready 调用；替代 group 现找）
func set_spawner(spawner: Node) -> void:
	_spawner = spawner


## A7：测试/诊断白盒断言经公开接口
func state() -> State:
	return _state


func lines() -> Array:
	return _lines


func turrets() -> Array:
	return _turrets


func total() -> int:
	return _total


func line_stage() -> int:
	return _line_stage


func comm() -> CommOverlay:
	return _comm


func set_cooldown_left(seconds: float) -> void:
	_cooldown_left = seconds


func set_state(p_state: State) -> void:
	_state = p_state


func cooldown_left() -> float:
	return _cooldown_left


var _turrets: Array[TurretBattery] = []
var _turret_sockets: Dictionary = {}  # turret -> 基座环索引
var _timer: float = 0.0
var _hud_poll: float = 0.0
var _total: int = 0
var _destroyed: int = 0
var _line_stage: int = 0  # 台词节点：0 未播 / 1 已播第1句 / 2 已播第2句
var _lines: Array[String] = []
var _cooldown_left: float = 0.0
var _comm: CommOverlay = null
var _hud: CanvasLayer = null


func _ready() -> void:
	add_to_group("elite_turret_event")
	DURATION = GameState.cfg("elite_turret_event.duration", DURATION)
	ENTER_TIME = GameState.cfg("elite_turret_event.enter_time", ENTER_TIME)
	RISE_TIME = GameState.cfg("elite_turret_event.rise_time", RISE_TIME)
	BOSS_RESUME_DELAY = GameState.cfg("elite_turret_event.boss_resume_delay", BOSS_RESUME_DELAY)
	TURRET_HP_BASE = GameState.cfg("elite_turret_event.turret_hp_base", TURRET_HP_BASE)
	# K14（H13 同族延续）：turret_counts/ammo_sequences 判型回退——非 Dictionary 时
	# 后续 .get() 在 Variant 上调用会运行时崩溃（G06 口径只覆盖了 fire_interval 等标量）
	var tc: Variant = GameState.cfg("elite_turret_event.turret_counts", TURRET_COUNTS)
	if tc is Dictionary:
		TURRET_COUNTS = tc
	var am: Variant = GameState.cfg("elite_turret_event.ammo_sequences", AMMO_SEQUENCES)
	if am is Dictionary:
		AMMO_SEQUENCES = am
	# H13（健壮性审核）：fire_interval 判型回退（G06 口径，防非数组/短数组 _ready 崩溃）
	var fi: Variant = GameState.cfg("elite_turret_event.fire_interval", [FIRE_INTERVAL.x, FIRE_INTERVAL.y])
	if fi is Array and fi.size() >= 2:
		FIRE_INTERVAL = Vector2(float(fi[0]), float(fi[1]))
	else:
		FIRE_INTERVAL = Vector2(FIRE_INTERVAL.x, FIRE_INTERVAL.y)
	# R07：WEAK_LOCK 判型（K14 同族延续）——非 Dictionary 时 :203 透传给
	# turret.setup 的弱锁参数会在消费方崩溃，与 TURRET_COUNTS 同口径回退
	var wl: Variant = GameState.cfg("elite_turret_event.weak_lock", WEAK_LOCK)
	if wl is Dictionary:
		WEAK_LOCK = wl
	REWARD_SCORE = GameState.cfg("elite_turret_event.reward_score", REWARD_SCORE)
	HOVER_Y = GameState.cfg("elite_turret_event.carrier.hover_y", HOVER_Y)
	COOLDOWN = GameState.cfg("elite_turret_event.cooldown", COOLDOWN)
	_comm = COMM_OVERLAY_SCRIPT.new() as CommOverlay
	add_child(_comm)


func is_active() -> bool:
	return _state != State.IDLE


## 触发条件：IDLE 且冷却结束（Boss 互斥由 spawner 侧检查）
func can_trigger() -> bool:
	if _state != State.IDLE or _cooldown_left > 0.0:
		return false
	# L13：母舰在场期不触发——母舰自动火力（玩家弹阵营）可摧毁事件单位并全额发奖，
	# 玩家进保护舱零参与挂机收益；在场判定经组查询（节点释放自动退组）
	if get_tree().get_first_node_in_group("mothership") != null:
		return false
	return true


## 事件启动（互斥检查通过后由 spawner 调用）
func start() -> void:
	if _state != State.IDLE:
		return
	_state = State.CARRIER_ENTER
	_destroyed = 0
	_line_stage = 0
	# 10 句台词无放回随机抽取 3 句，绑定三个进度节点
	var pool: Array[String] = []
	for i in 10:
		pool.append("ETQ_%d" % (i + 1))
	pool.shuffle()
	_lines = pool.slice(0, 3)
	# 冻结 Boss 调度 + 暂停普通波次（spawner 钩子；A5 注入 _spawner）
	if _spawner != null:
		_spawner.set_boss_frozen(true)
		_spawner.set_waves_paused(true)
	_carrier = StrikeCarrier.new()
	var ev_view := GameState.view_world_rect()  # D10：载体入场锚点统一 view 基线
	_carrier.position = Vector2(ev_view.get_center().x, ev_view.position.y - 450.0)
	_carrier.entered.connect(_on_carrier_entered)
	_carrier.exited.connect(_on_carrier_exited)
	get_parent().add_child(_carrier)
	_carrier.enter(HOVER_Y, ENTER_TIME)
	GameState.shake(GameState.cfg("elite_turret_event.carrier.shake", 4.0))
	_hud = get_tree().get_first_node_in_group("hud")


## 返航中止（main._start_homecoming 调用）：IDLE 直接返回；清掉在场炮塔（queue_free
## 不触发 died 计分，自行清理注册清单）、隐藏 HUD 事件条、恢复普通波次，航母按完整
## 撤离处理；Boss 解冻/_boss_pending 补触发沿用现有 BOSS_DELAY → _on_boss_delay_end。
func abort() -> void:
	if _state == State.IDLE:
		return
	for turret in _turrets:
		if is_instance_valid(turret):
			turret.queue_free()
	_turrets.clear()
	_turret_sockets.clear()
	if _hud != null:
		_hud.hide_event_bar()
	if _comm != null:
		_comm.clear()  # B13：清掉已显台词，避免返航恢复后残留
	_resume_waves()
	if _state == State.CARRIER_ENTER or _state == State.TURRET_ACTIVE:
		_state = State.CARRIER_EXIT
		_carrier_retreat(true)  # 完整撤离（加速上升淡出）
	# CARRIER_EXIT/BOSS_DELAY：撤离/解冻流程已在推进，无需干预


## 航母悬停到位：基座盖板旋开、炮塔升起充能（不可被攻击）
func _on_carrier_entered() -> void:
	# Q16（2026-08-05）：turret_counts 上限钳制——配置 >5 时 SOCKETS[i] 越界崩溃
	#（StrikeCarrier.SOCKETS 固定 5 槽；R07：注释修正——负数经 clampi 钳 0，
	# GDScript for 负整数本不迭代，「防负循环」表述失实，钳制仅为与上限对称）
	var raw_total := int(TURRET_COUNTS.get(String(GameState.difficulty), 4))
	_total = clampi(raw_total, 0, StrikeCarrier.SOCKETS.size())
	# HP 三级乘算：基准 × 难度档 × 对局进程 ramp（与普通敌机同口径，避免后期退化为送分道具）
	var hp := maxi(1, int(roundf(TURRET_HP_BASE * GameState.enemy_hp_multiplier() * GameState.enemy_hp_ramp())))
	var ammo: Array = AMMO_SEQUENCES.get(String(GameState.difficulty), AMMO_SEQUENCES["medium"])
	for i in _total:
		var turret := TURRET_SCENE.instantiate() as TurretBattery
		turret.setup(hp, ammo, FIRE_INTERVAL, WEAK_LOCK)
		turret.position = _carrier.position + StrikeCarrier.SOCKETS[i] * GameState.world_scale
		turret.died.connect(_on_turret_died.bind(i))
		get_parent().add_child(turret)
		_turrets.append(turret)
		_turret_sockets[turret] = i
		_carrier.set_socket_charging(i)
		turret.rise(RISE_TIME)
	_schedule(RISE_TIME, _begin_countdown)


## 充能完毕：30s 倒计时开始，炮塔可被攻击并开火
func _begin_countdown() -> void:
	if _state != State.CARRIER_ENTER:
		return
	_state = State.TURRET_ACTIVE
	_timer = DURATION
	for turret in _turrets:
		if is_instance_valid(turret):
			turret.activate()
	if _hud != null:
		_hud.show_event_bar(_total)


func _process(delta: float) -> void:
	if _cooldown_left > 0.0 and _state == State.IDLE:
		_cooldown_left -= delta
	if _state != State.TURRET_ACTIVE:
		return
	_timer -= delta
	_hud_poll -= delta
	if _hud_poll <= 0.0:
		_hud_poll = 0.1
		if _hud != null:
			_hud.update_event_bar(_timer, DURATION, _total - _destroyed)
	if _timer <= 0.0:
		_on_event_timeout()


func _on_turret_died(turret: TurretBattery, socket: int) -> void:
	_destroyed += 1
	_turrets.erase(turret)
	_turret_sockets.erase(turret)
	if _carrier != null and is_instance_valid(_carrier):
		_carrier.set_socket_destroyed(socket)
	if _hud != null and _state == State.TURRET_ACTIVE:
		_hud.update_event_bar(_timer, DURATION, _total - _destroyed)
	# 进度台词节点：摧毁 ≥ ⌈总数/3⌉ → 第 1 句；≥ ⌈总数×2/3⌉ → 第 2 句；全歼 → 第 3 句
	if _line_stage == 0 and _destroyed >= maxi(1, ceili(_total / 3.0)):
		_line_stage = 1
		_comm.show_line(_lines[0])
	if _line_stage == 1 and _destroyed >= maxi(1, ceili(_total * 2.0 / 3.0)) and _destroyed < _total:
		_line_stage = 2
		_comm.show_line(_lines[1])
	if _destroyed >= _total:
		_on_all_turrets_destroyed()


## 成功结算：第 3 句台词 + 复用 Boss 击杀得分（基础 500，add_score 内乘难度倍率）
func _on_all_turrets_destroyed() -> void:
	if _state != State.TURRET_ACTIVE:
		return
	_state = State.CARRIER_EXIT
	_comm.show_line(_lines[2])
	GameState.add_score(REWARD_SCORE)
	if _hud != null:
		_hud.hide_event_bar()
	_resume_waves()
	_carrier_retreat(false)  # 受创撤离（冒烟+慢速）


## 失败结算：炮塔收回盖板，固定撤退台词，无奖励
func _on_event_timeout() -> void:
	_state = State.CARRIER_EXIT
	for turret in _turrets:
		if is_instance_valid(turret):
			turret.cease_fire_and_retract()
	# 2026-08-03 审计：收回中的炮塔已无 died 依赖（_ceased 守卫），立即清引用数组，
	# 消除最长 ~6s（BOSS_RESUME_DELAY 窗口）的失效引用驻留（_on_boss_delay_end 的 clear 幂等）
	_turrets.clear()
	_turret_sockets.clear()
	_comm.show_line("ETQ_RETREAT")
	if _hud != null:
		_hud.hide_event_bar()
	_resume_waves()
	_carrier_retreat(true)  # 完整撤离（加速上升淡出）


## 航母撤离（复用 Boss escape 参数族量级；存活敌弹自然出界销毁，不清屏）
func _carrier_retreat(victorious: bool) -> void:
	if _carrier != null and is_instance_valid(_carrier):
		_carrier.retreat(victorious)
	else:
		_on_carrier_exited()


## 航母离场后进入 Boss 恢复间隔
func _on_carrier_exited() -> void:
	_carrier = null
	if _state == State.CARRIER_EXIT:
		_state = State.BOSS_DELAY
		_schedule(BOSS_RESUME_DELAY, _on_boss_delay_end)


## BOSS_DELAY 结束：回 IDLE；若存在被冻结的 Boss 触发 → 立即触发一次（不累积）
func _on_boss_delay_end() -> void:
	_state = State.IDLE
	_cooldown_left = COOLDOWN
	_turrets.clear()
	_turret_sockets.clear()
	if _spawner != null:
		_spawner.set_boss_frozen(false)
		if _spawner.consume_boss_pending():
			_spawner.trigger_boss()


## 普通波次在 CARRIER_EXIT 起恢复（Boss 冻结保留到 BOSS_DELAY 结束）
func _resume_waves() -> void:
	if _spawner != null:
		_spawner.set_waves_paused(false)


## 一次性计时回调（同 spawner._schedule：Timer 节点 + 信号，避免协程泄漏）
func _schedule(seconds: float, callback: Callable) -> void:
	var timer := Timer.new()
	timer.one_shot = true
	add_child(timer)
	timer.timeout.connect(callback, CONNECT_ONE_SHOT)
	timer.timeout.connect(timer.queue_free, CONNECT_ONE_SHOT)
	timer.start(seconds)
