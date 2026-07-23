extends Node
## 模拟人工游玩探针：实例化真实对局（scenes/main.tscn），从开始面板走真实 UI 路径开新局，
## 用合成输入（Input.action_press + 合成鼠标移动）像真人一样游玩一整局，全程日志化。
##
## 日志统一 "[AUTOPLAY]" 前缀；不变量异常用 printerr("[ANOMALY] ...") 记录但不中断。
## 探针性质：除崩溃/硬断言外不以 FAIL 结束，结尾打印 SUMMARY 供人工/脚本分析。
## 引擎层 ERROR/WARNING 统计需在外部对 stderr 计数（进程内读不到自身 stderr）。
##
## 运行：godot --headless --path . res://test/autoplay_test.tscn [-- --autoplay-seconds=480]

const TIME_SCALE := 2.0  # 加速倍率（狂暴子弹时间期间让行给 main 编排，结束后恢复）
const DEFAULT_RUN_SECONDS := 480.0  # 真实时间预算（≈16 分钟游戏时间 @2x）
const SNAPSHOT_INTERVAL_MS := 5000
const CHECK_INTERVAL_MS := 500
const MOVE_DECISION_MS := 120
const AIM_INTERVAL_MS := 250
const RESTART_MIN_REMAINING_S := 60.0  # 剩余预算不足则死亡后不再重开

# 卡死阈值（真实毫秒，已按 time_scale 放宽）
const BUFF_STUCK_MS := 10000
const BOSS_TIMEOUT_MS := 120000
const HOME_STUCK_MS := 8000
const BASE_STUCK_MS := 20000
const SCORE_STAGNANT_MS := 60000
# 母舰各状态预期最长停留（State 枚举序 -> ms）
const MS_STATE_TIMEOUTS: Array[int] = [20000, 10000, 10000, 10000, 70000, 10000, 30000]
const MS_STATE_NAMES: Array[String] = ["DESCEND", "HOVER", "DOCKING", "RESUPPLY", "STAY", "RELEASE", "DEPART"]
# 实体爆增阈值
const MAX_PLAYER_BULLETS := 300
const MAX_ENEMY_BULLETS := 800
const MAX_ENEMIES := 120

const MOVE_ACTIONS: Array[StringName] = [&"move_left", &"move_right", &"move_up", &"move_down"]

var _run_seconds: float = DEFAULT_RUN_SECONDS
var _started := false
var _finished := false
var _t0_msec: int = 0
var _last_snap_msec: int = 0
var _last_check_msec: int = 0

# 对局引用（重开时刷新）
var _main = null  # main.gd 无 class_name，保持动态访问
var _player: Player = null
var _spawner = null
var _buff_ui: CanvasLayer = null
var _boss: Boss = null

# bot 状态
var _move_target := Vector2.ZERO
var _next_move_decision: int = 0
var _next_aim: int = 0
var _dash_release_at: int = 0
var _next_dash_try: int = 0
var _dock_holding := false
var _dock_hold_until: int = 0
var _next_dock_consider: int = 0
var _stay_since: int = 0
var _early_leave_at: int = 0
var _early_holding := false
var _early_hold_until: int = 0
var _home_holding := false
var _home_hold_until: int = 0
var _next_home_consider: int = 0
var _restart_at: int = 0

# 事件/异常 episode 状态
var _buff_open_since: int = 0
var _buff_pick_at: int = 0
var _buff_stuck_reported := false
var _boss_since: int = 0
var _boss_timeout_reported := false
var _ms_last_state: int = -1
var _ms_state_since: int = 0
var _ms_stuck_reported := false
var _homecoming_since: int = 0
var _home_stuck_reported := false
var _base_since: int = 0
var _base_repaired := false
var _base_stuck_reported := false
var _last_score: int = -1
var _score_change_msec: int = 0
var _score_stag_reported := false
var _node_baseline: int = 0
var _node_prev: int = 0
var _node_rise_streak: int = 0
var _node_leak_armed := true
var _last_hp: float = -1.0
var _last_hit_log_msec: int = 0
var _anomaly_rl_last: Dictionary = {}  # category -> last msec（数值类异常 10s 限频）

# 统计
var _run_index: int = 0
var _deaths: int = 0
var _total_kills: int = 0
var _total_boss_kills: int = 0
var _run_scores: Array[int] = []
var _buff_picks: int = 0
var _ms_summons: int = 0
var _homecomings: int = 0
var _max_nodes: int = 0
var _max_enemy_bullets: int = 0
var _max_player_bullets: int = 0
var _max_enemies: int = 0
var _anomaly_counts: Dictionary = {}
var _anomaly_first: Dictionary = {}
var _prev_difficulty: StringName = &"medium"


func _elapsed_s() -> float:
	if _t0_msec == 0:
		return 0.0
	return (Time.get_ticks_msec() - _t0_msec) / 1000.0


func _log(msg: String) -> void:
	print("[AUTOPLAY] [%7.1fs] %s" % [_elapsed_s(), msg])


func _anomaly(category: String, msg: String) -> void:
	_anomaly_counts[category] = int(_anomaly_counts.get(category, 0)) + 1
	if not _anomaly_first.has(category):
		_anomaly_first[category] = msg
	printerr("[ANOMALY] [%7.1fs] [%s] %s" % [_elapsed_s(), category, msg])


## 数值类异常限频（同类 10s 至多一条，避免刷屏）
func _anomaly_rl(category: String, msg: String, now: int) -> void:
	if now - int(_anomaly_rl_last.get(category, -100000)) < 10000:
		return
	_anomaly_rl_last[category] = now
	_anomaly(category, msg)


func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS  # 暂停（Buff/结算/基地）时也要继续驱动 bot
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--autoplay-seconds="):
			_run_seconds = float(arg.split("=")[1])
	seed(20260722)
	# 确定性：清残留存档；固定 medium 难度（结束恢复原档位，同 smoke_test 做法）
	GameState.delete_save()
	_prev_difficulty = GameState.difficulty
	GameState.set_difficulty(&"medium")
	GameState.milestone_reached.connect(_on_milestone)
	GameState.player_died.connect(_on_player_died)
	GameState.health_changed.connect(_on_health_changed)
	_t0_msec = Time.get_ticks_msec()
	_last_snap_msec = _t0_msec
	_last_check_msec = _t0_msec
	_log("START budget=%.0fs real (time_scale=%.1f) seed=20260722" % [_run_seconds, TIME_SCALE])
	_start_run()


## 实例化 main 并走真实 UI 路径开新局（欢迎页 → 开始面板「新游戏」）
func _start_run() -> void:
	_run_index += 1
	_log("=== RUN %d START ===" % _run_index)
	_main = (load("res://scenes/main.tscn") as PackedScene).instantiate()
	# 本测试根节点必须是 ALWAYS（暂停期间继续驱动 bot），但 Main 继承根节点的模式
	# 会导致整个对局在暂停时照跑——显式把 Main 钉回 PAUSABLE，还原真实暂停语义。
	_main.process_mode = Node.PROCESS_MODE_PAUSABLE
	add_child(_main)
	await get_tree().process_frame
	await get_tree().process_frame
	var welcome: CanvasLayer = _main.get_node("WelcomeScreen")
	if welcome.visible:
		welcome.dismiss()
		_log("欢迎页已关闭")
	# 开始面板可能晚一帧显示，轮询若干帧
	var start_panel: CanvasLayer = _main.get_node("StartPanel")
	for i in 10:
		if start_panel.visible:
			break
		await get_tree().process_frame
	if start_panel.visible:
		start_panel._on_new_game_pressed()
		_log("开始面板：新游戏")
	await get_tree().process_frame
	_player = _main.get_node("Player")
	_spawner = _main.get_node("Spawner")
	_buff_ui = _main.get_node("BuffUI")
	_spawner.boss_spawned.connect(_on_boss_spawned)
	_move_target = _player.position
	_last_hp = GameState.health
	_last_score = GameState.score
	_score_change_msec = Time.get_ticks_msec()
	Engine.time_scale = TIME_SCALE
	_started = true


func _process(_delta: float) -> void:
	if not _started or _finished:
		return
	var now := Time.get_ticks_msec()
	if _elapsed_s() >= _run_seconds:
		_finish()
		return
	if _main == null or not is_instance_valid(_main):
		return  # 重开过渡帧
	_reassert_time_scale()
	_bot_tick(now)
	if now - _last_snap_msec >= SNAPSHOT_INTERVAL_MS:
		_last_snap_msec = now
		_snapshot(now)
	if now - _last_check_msec >= CHECK_INTERVAL_MS:
		_last_check_msec = now
		_checks(now)


## 狂暴子弹时间（main.gd 直接写 Engine.time_scale=0.24→恢复 1.0）结束后恢复加速倍率
func _reassert_time_scale() -> void:
	if _main._bullet_time_left <= 0.0 and _main._time_scale_ramp < 0.0 and Engine.time_scale != TIME_SCALE:
		Engine.time_scale = TIME_SCALE


# ---------------- bot 行为 ----------------

func _bot_tick(now: int) -> void:
	# 死亡重开
	if _restart_at > 0 and now >= _restart_at:
		_restart_at = 0
		if _run_seconds - _elapsed_s() > RESTART_MIN_REMAINING_S:
			_do_restart()
		else:
			_finish()
		return
	_handle_buff_ui(now)
	_handle_base_ui(now)
	var playing := not get_tree().paused and _player != null and not _player._dead
	if playing:
		_update_movement(now)
		_update_aim(now)
		_update_dash(now)
		_update_dock(now)
		_update_homecoming(now)
	_track_mothership(now)


func _handle_buff_ui(now: int) -> void:
	if _buff_ui == null or not is_instance_valid(_buff_ui):
		return
	if _buff_ui.visible:
		if _buff_open_since == 0:
			_buff_open_since = now
			_buff_stuck_reported = false
			_buff_pick_at = now + 400 + randi() % 600  # 模拟真人看牌时间
			var ids: Array = []
			for b in _buff_ui._current_available:
				ids.append(b["id"])
			_log("Buff 三选一弹出 candidates=%s" % [str(ids)])
		elif now >= _buff_pick_at:
			var avail: Array = _buff_ui._current_available
			if not avail.is_empty():
				var pick: Dictionary = avail[randi() % avail.size()]
				var ev := InputEventMouseButton.new()
				ev.pressed = true
				ev.button_index = MOUSE_BUTTON_LEFT
				_buff_ui._on_card_gui_input(ev, pick["id"])
				_buff_picks += 1
				_log("Buff 选择: %s（层数 %d）" % [pick["id"], GameState.buff_count(pick["id"])])
			_buff_open_since = 0
	else:
		_buff_open_since = 0


func _handle_base_ui(now: int) -> void:
	if _main == null or not is_instance_valid(_main):
		return
	var base_ui: CanvasLayer = _main._base_ui
	if base_ui.visible:
		if _base_since == 0:
			_base_since = now
			_base_repaired = false
			_base_stuck_reported = false
			_log("进入基地整备（RP=%d HP=%.0f）" % [GameState.rp, GameState.health])
		elif not _base_repaired and now - _base_since >= 600:
			_base_repaired = true
			if GameState.rp >= 2 and GameState.health < GameState.max_health():
				base_ui._on_repair_pressed()
				_log("基地维修：HP -> %.0f" % GameState.health)
		elif now - _base_since >= 1500:
			base_ui._on_resume_pressed()
			_log("继续出击，返回对局")
			_base_since = 0
	else:
		_base_since = 0


## 随机游走 + 远离密集敌弹/敌机的简单规避
func _update_movement(now: int) -> void:
	if now < _next_move_decision:
		return
	_next_move_decision = now + MOVE_DECISION_MS
	var view := GameState.view_world_rect()
	if _player.position.distance_to(_move_target) < 80.0 or randf() < 0.1:
		_move_target = Vector2(
			randf_range(view.position.x + 100.0, view.end.x - 100.0),
			randf_range(view.position.y + 100.0, view.end.y - 100.0)
		)
	var steer := (_move_target - _player.position)
	steer = steer.normalized() if steer.length() > 60.0 else Vector2.ZERO
	# 规避：240px 内敌弹 + 160px 内敌机的反加权和
	var dodge := Vector2.ZERO
	for child in _main.get_children():
		var b := child as Bullet
		if b != null and not b.is_player_bullet:
			var d: float = _player.position.distance_to(b.position)
			if d < 240.0 and d > 1.0:
				dodge += (_player.position - b.position) / d * (1.0 - d / 240.0) * 2.0
	for e in GameState.enemies:
		var n := e as Node2D
		if n == null:
			continue
		var d: float = _player.position.distance_to(n.global_position)
		if d < 160.0 and d > 1.0:
			dodge += (_player.position - n.global_position) / d * (1.0 - d / 160.0) * 3.0
	steer += dodge
	_set_move_actions(steer)


func _set_move_actions(steer: Vector2) -> void:
	var want: Dictionary = {
		&"move_right": steer.x > 0.35,
		&"move_left": steer.x < -0.35,
		&"move_down": steer.y > 0.35,
		&"move_up": steer.y < -0.35,
	}
	for a: StringName in want:
		if want[a]:
			Input.action_press(a)
		else:
			Input.action_release(a)


func _update_aim(now: int) -> void:
	if now < _next_aim:
		return
	_next_aim = now + AIM_INTERVAL_MS
	var target := Vector2.ZERO
	var best_sq := INF
	for e in GameState.enemies:
		var n := e as Node2D
		if n == null:
			continue
		var d_sq: float = _player.position.distance_squared_to(n.global_position)
		if d_sq < best_sq:
			best_sq = d_sq
			target = n.global_position
	if best_sq == INF:
		target = _player.position + Vector2(randf_range(-300.0, 300.0), -400.0)
	# 世界坐标 → canvas → 屏幕（无头模式 warp_mouse 无效，合成鼠标移动事件）
	var canvas_pos: Vector2 = _main.get_canvas_transform() * target
	var win: Vector2 = get_tree().root.get_screen_transform() * canvas_pos
	var mev := InputEventMouseMotion.new()
	mev.position = win
	mev.global_position = win
	Input.parse_input_event(mev)


## 周期性冲刺：敌弹密集时优先触发（需已解锁 phase_dash）
func _update_dash(now: int) -> void:
	if _dash_release_at > 0 and now >= _dash_release_at:
		Input.action_release("dash")
		_dash_release_at = 0
	if now < _next_dash_try:
		return
	_next_dash_try = now + 500
	if not _player.dash_unlocked() or _player._dash_cooldown > 0.0 or _player._dashing:
		return
	var threat := 0.0
	for child in _main.get_children():
		var b := child as Bullet
		if b != null and not b.is_player_bullet:
			var d: float = _player.position.distance_to(b.position)
			if d < 240.0:
				threat += 1.0 - d / 240.0
	if threat > 1.0 or randf() < 0.05:
		Input.action_press("dash")
		_dash_release_at = now + 150


## 母舰：Boss 战或 HP 偏低时概率蓄力召唤；驻留一段时间后长按 H 提前离舰
func _update_dock(now: int) -> void:
	if _main._homecoming or _main._game_over:
		if _dock_holding:
			Input.action_release("dock")
			_dock_holding = false
		return
	var ms: Mothership = _main._mothership
	if _dock_holding:
		if ms != null:
			Input.action_release("dock")
			_dock_holding = false
			_ms_summons += 1
			_log("母舰召唤成功（第 %d 次）" % _ms_summons)
		elif now >= _dock_hold_until:
			Input.action_release("dock")
			_dock_holding = false
			_log("母舰蓄力超时未召唤，松手")
	elif ms == null and _main._dock_cooldown <= 0.0 and now >= _next_dock_consider:
		var hp_ratio: float = GameState.health / GameState.max_health()
		if _boss != null or hp_ratio < 0.7:
			if randf() < 0.5:
				Input.action_press("dock")
				_dock_holding = true
				_dock_hold_until = now + 5000
				_log("开始蓄力召唤母舰（boss=%s hp=%.0f%%）" % [str(_boss != null), hp_ratio * 100.0])
			else:
				_next_dock_consider = now + 20000
		else:
			_next_dock_consider = now + 10000
	# 驻留驾驶一段时间（WASD 已被移动驱动复用）后提前离舰
	if _early_holding:
		if ms == null or ms._state >= Mothership.State.RELEASE or now >= _early_hold_until:
			Input.action_release("dock")
			_early_holding = false
			if ms != null and ms._state >= Mothership.State.RELEASE:
				_log("提前离舰（弹匣 %d 格）" % ms._mag_cells)
	elif ms != null and ms._state == Mothership.State.STAY:
		if _stay_since == 0:
			_stay_since = now
			_early_leave_at = now + 6000 + randi() % 8000
		elif now >= _early_leave_at:
			Input.action_press("dock")
			_early_holding = true
			_early_hold_until = now + 4000
	else:
		_stay_since = 0


## 返航：血量低（或 Boss 战且半血以下）概率蓄力 B
func _update_homecoming(now: int) -> void:
	if _main._homecoming:
		if _home_holding:
			Input.action_release("homecoming")
			_home_holding = false
			_homecomings += 1
			_log("返航触发（第 %d 次）" % _homecomings)
		return
	if _home_holding:
		if now >= _home_hold_until:
			Input.action_release("homecoming")
			_home_holding = false
			_log("返航蓄力超时未触发，松手")
		return
	if now < _next_home_consider or _main._game_over:
		return
	_next_home_consider = now + 8000
	var hp_ratio: float = GameState.health / GameState.max_health()
	var want := hp_ratio < 0.35 or (_boss != null and hp_ratio < 0.6)
	if want and randf() < 0.6:
		Input.action_press("homecoming")
		_home_holding = true
		_home_hold_until = now + 4000
		_log("开始蓄力返航（hp=%.0f%% boss=%s）" % [hp_ratio * 100.0, str(_boss != null)])


## 母舰状态变化日志 + 卡死 episode 跟踪
func _track_mothership(now: int) -> void:
	var ms: Mothership = _main._mothership if (_main != null and is_instance_valid(_main)) else null
	var state := -1 if ms == null else int(ms._state)
	if state != _ms_last_state:
		if _ms_last_state >= 0 or state >= 0:
			var from_s := "NONE" if _ms_last_state < 0 else MS_STATE_NAMES[_ms_last_state]
			var to_s := "NONE" if state < 0 else MS_STATE_NAMES[state]
			_log("母舰状态 %s -> %s" % [from_s, to_s])
		_ms_last_state = state
		_ms_state_since = now
		_ms_stuck_reported = false
	elif state >= 0 and not _ms_stuck_reported:
		if now - _ms_state_since > MS_STATE_TIMEOUTS[state]:
			_ms_stuck_reported = true
			_anomaly("mothership_stuck", "母舰状态 %s 超过 %ds 未推进" % [MS_STATE_NAMES[state], MS_STATE_TIMEOUTS[state] / 1000])


# ---------------- 事件 ----------------

func _on_milestone(milestone_score: int) -> void:
	_log("里程碑达成 score=%d" % milestone_score)


func _on_boss_spawned(boss: Boss) -> void:
	_boss = boss
	_boss_since = Time.get_ticks_msec()
	_boss_timeout_reported = false
	_log("Boss 出现 type=%d hp=%.0f" % [boss.boss_type, boss.max_hp])
	boss.enraged.connect(func() -> void: _log("Boss 狂暴 type=%d" % boss.boss_type))
	boss.died.connect(_on_boss_died.bind(boss))
	boss.escaped.connect(func() -> void: _log("Boss 逃跑 type=%d" % boss.boss_type); _clear_boss(boss))
	boss.tree_exited.connect(func() -> void: _clear_boss(boss))


func _on_boss_died(boss: Boss) -> void:
	if boss.is_escaped:
		return  # 逃跑离场也会发 died（通知血条/生成器），非击杀
	_total_boss_kills += 1
	_log("Boss 击杀 type=%d（本进程累计 %d）" % [boss.boss_type, _total_boss_kills])
	_clear_boss(boss)


func _clear_boss(boss: Boss) -> void:
	if _boss == boss:
		_boss = null


func _on_player_died() -> void:
	_deaths += 1
	_total_kills += GameState.kills
	_run_scores.append(GameState.score)
	_log("玩家死亡 run=%d score=%d kills=%d boss_kills=%d" % [_run_index, GameState.score, GameState.kills, GameState.boss_kills])
	_release_all_inputs()
	_restart_at = Time.get_ticks_msec() + 3000  # 留 3s 走到结算界面


func _on_health_changed(new_health: float) -> void:
	var now := Time.get_ticks_msec()
	if _last_hp >= 0.0 and new_health < _last_hp - 0.01 and now - _last_hit_log_msec > 1000:
		_last_hit_log_msec = now
		_log("玩家受击 HP %.1f -> %.1f" % [_last_hp, new_health])
	_last_hp = new_health


func _do_restart() -> void:
	_log("重开新一局")
	_boss = null
	_ms_last_state = -1
	_stay_since = 0
	_buff_open_since = 0
	_homecoming_since = 0
	get_tree().paused = false
	_started = false
	_main.queue_free()
	await get_tree().process_frame
	await get_tree().process_frame
	GameState.delete_save()
	GameState.reset_run()
	_start_run()


func _release_all_inputs() -> void:
	for a: StringName in MOVE_ACTIONS:
		Input.action_release(a)
	Input.action_release("dash")
	Input.action_release("dock")
	Input.action_release("homecoming")
	_dock_holding = false
	_home_holding = false
	_early_holding = false
	_dash_release_at = 0


# ---------------- 快照与不变量检查 ----------------

func _snapshot(now: int) -> void:
	var p_bullets := 0
	var e_bullets := 0
	for child in _main.get_children():
		var b := child as Bullet
		if b != null:
			if b.is_player_bullet:
				p_bullets += 1
			else:
				e_bullets += 1
	var main_nodes: int = _main.get_child_count()
	var total_nodes := _count_nodes(get_tree().root)
	_max_nodes = maxi(_max_nodes, total_nodes)
	_max_enemy_bullets = maxi(_max_enemy_bullets, e_bullets)
	_max_player_bullets = maxi(_max_player_bullets, p_bullets)
	_max_enemies = maxi(_max_enemies, GameState.enemies.size())
	var boss_s := "none"
	if _boss != null and is_instance_valid(_boss):
		boss_s = "type%d hp=%.0f/%.0f%s" % [_boss.boss_type, _boss.hp, _boss.max_hp, "(enraged)" if _boss._enraged else ""]
	var ms_s := "none" if _main._mothership == null else MS_STATE_NAMES[int(_main._mothership._state)]
	_log(
		(
			"SNAP run=%d t_game=%.0fs score=%d hp=%.0f/%.0f kills=%d enemies=%d bullets(p=%d,e=%d) boss=%s ms=%s diff=%.2f elapsed=%.0fs nodes(main=%d,total=%d) ts=%.2f paused=%s"
			% [
				_run_index, GameState.run_time, GameState.score, GameState.health, GameState.max_health(),
				GameState.kills, GameState.enemies.size(), p_bullets, e_bullets, boss_s, ms_s,
				GameState.difficulty_multiplier, _spawner._elapsed, main_nodes, total_nodes,
				Engine.time_scale, str(get_tree().paused),
			]
		)
	)
	# 节点泄漏趋势：连续 3 个快照上涨且超过基线 3 倍
	if _node_baseline == 0:
		_node_baseline = total_nodes
		_node_prev = total_nodes
		return
	if total_nodes > _node_prev:
		_node_rise_streak += 1
	else:
		_node_rise_streak = 0
	_node_prev = total_nodes
	if total_nodes < _node_baseline * 2:
		_node_leak_armed = true
	if _node_leak_armed and _node_rise_streak >= 3 and total_nodes > _node_baseline * 3:
		_node_leak_armed = false
		_anomaly("node_leak", "节点数连续上涨 %d -> %d（基线 %d）" % [_node_baseline, total_nodes, _node_baseline])
		_dump_node_histogram()


## 节点泄漏诊断：打印 Main 子树及根节点的直接子节点类型分布
func _dump_node_histogram() -> void:
	_log("--- 节点直方图（泄漏诊断） ---")
	_histogram_line(_main, "  ")
	for child in _main.get_children():
		if child.get_child_count() > 3:
			_histogram_line(child, "    ")


func _histogram_line(node: Node, indent: String) -> void:
	var by_class: Dictionary = {}
	for child in node.get_children():
		var cls := child.get_class()
		if child.get_script() != null:
			cls = (child.get_script() as Script).resource_path.get_file()
		by_class[cls] = int(by_class.get(cls, 0)) + 1
	_log("%s%s <%s> children=%d %s" % [indent, node.name, node.get_class(), node.get_child_count(), str(by_class)])


func _count_nodes(root: Node) -> int:
	var n := 1
	for child in root.get_children():
		n += _count_nodes(child)
	return n


func _checks(now: int) -> void:
	# 数值越界
	if GameState.health < -0.01 or GameState.health > GameState.max_health() + 0.01:
		_anomaly_rl("hp_bounds", "HP 越界 %.2f（上限 %.2f）" % [GameState.health, GameState.max_health()], now)
	if GameState.score < 0:
		_anomaly_rl("negative_score", "分数为负 %d" % GameState.score, now)
	# 实体爆增
	var p_bullets := 0
	var e_bullets := 0
	for child in _main.get_children():
		var b := child as Bullet
		if b != null:
			if b.is_player_bullet:
				p_bullets += 1
			else:
				e_bullets += 1
	if p_bullets > MAX_PLAYER_BULLETS:
		_anomaly_rl("entity_explosion", "玩家子弹数 %d 超过 %d" % [p_bullets, MAX_PLAYER_BULLETS], now)
	if e_bullets > MAX_ENEMY_BULLETS:
		_anomaly_rl("entity_explosion", "敌方子弹数 %d 超过 %d" % [e_bullets, MAX_ENEMY_BULLETS], now)
	if GameState.enemies.size() > MAX_ENEMIES:
		_anomaly_rl("entity_explosion", "敌机注册数 %d 超过 %d" % [GameState.enemies.size(), MAX_ENEMIES], now)
	# 注册表悬空引用
	for e in GameState.enemies:
		if not is_instance_valid(e):
			_anomaly_rl("registry_stale", "GameState.enemies 含失效实例", now)
			break
	# Buff UI 卡死
	if _buff_ui != null and is_instance_valid(_buff_ui) and _buff_ui.visible:
		if _buff_open_since > 0 and now - _buff_open_since > BUFF_STUCK_MS and not _buff_stuck_reported:
			_buff_stuck_reported = true
			_anomaly("buff_ui_stuck", "Buff UI 可见超过 %ds 未关闭" % (BUFF_STUCK_MS / 1000))
	# Boss 超时
	if _boss != null and is_instance_valid(_boss) and not _boss_timeout_reported:
		if now - _boss_since > BOSS_TIMEOUT_MS:
			_boss_timeout_reported = true
			_anomaly("boss_timeout", "Boss type=%d 在场超过 %ds" % [_boss.boss_type, BOSS_TIMEOUT_MS / 1000])
	# 返航/基地卡死
	if _main._homecoming:
		if _homecoming_since == 0:
			_homecoming_since = now
			_home_stuck_reported = false
		elif not _home_stuck_reported and not _main._base_ui.visible and now - _homecoming_since > HOME_STUCK_MS:
			_home_stuck_reported = true
			_anomaly("homecoming_stuck", "返航触发 %ds 后基地 UI 仍未显示" % (HOME_STUCK_MS / 1000))
	else:
		_homecoming_since = 0
	if _main._base_ui.visible and _base_since > 0 and now - _base_since > BASE_STUCK_MS and not _base_stuck_reported:
		_base_stuck_reported = true
		_anomaly("base_ui_stuck", "基地 UI 可见超过 %ds 未关闭" % (BASE_STUCK_MS / 1000))
	# UI 状态一致性：结算面板与基地面板同显 / 玩家死亡但游戏未停且无结算面板
	var game_over_ui: CanvasLayer = _main.get_node("GameOverUI")
	if game_over_ui.visible and _main._base_ui.visible:
		_anomaly_rl("ui_overlap", "GameOverUI 与基地 UI 同时可见", now)
	if _player != null and _player._dead and not get_tree().paused and not game_over_ui.visible:
		_anomaly_rl("dead_no_gameover", "玩家已死亡、游戏未暂停且结算面板不可见", now)
	# 分数停滞 + 场上无敌机（疑似不刷怪/不结算）
	if GameState.score != _last_score:
		_last_score = GameState.score
		_score_change_msec = now
		_score_stag_reported = false
	elif (
		not _score_stag_reported
		and now - _score_change_msec > SCORE_STAGNANT_MS
		and GameState.enemies.is_empty()
		and _boss == null
		and not get_tree().paused
		and not _main._homecoming
		and not _main._game_over
	):
		_score_stag_reported = true
		_anomaly("score_stagnant", "分数 %ds 未增长且场上无敌机（疑似不刷怪）" % (SCORE_STAGNANT_MS / 1000))


# ---------------- 收尾 ----------------

func _finish() -> void:
	if _finished:
		return
	_finished = true
	# 收尾局（未死亡）也计入统计
	if _player != null and is_instance_valid(_player) and not _player._dead:
		_total_kills += GameState.kills
		_run_scores.append(GameState.score)
	_log("DONE")
	if _main != null and is_instance_valid(_main):
		_dump_node_histogram()
	print("")
	print("[AUTOPLAY] ==================== SUMMARY ====================")
	print("[AUTOPLAY] 真实时长 %.0fs | 对局数 %d | 死亡 %d 次" % [_elapsed_s(), _run_index, _deaths])
	print("[AUTOPLAY] 每局得分 %s | 总击杀 %d | Boss 击杀 %d" % [str(_run_scores), _total_kills, _total_boss_kills])
	print("[AUTOPLAY] Buff 选取 %d 次 | 母舰召唤 %d 次 | 返航 %d 次" % [_buff_picks, _ms_summons, _homecomings])
	print("[AUTOPLAY] 峰值: 节点 %d | 敌弹 %d | 玩家弹 %d | 敌机 %d" % [_max_nodes, _max_enemy_bullets, _max_player_bullets, _max_enemies])
	var total_anomalies := 0
	for k in _anomaly_counts:
		total_anomalies += int(_anomaly_counts[k])
	print("[AUTOPLAY] 异常总数 %d（%d 类）" % [total_anomalies, _anomaly_counts.size()])
	for k in _anomaly_counts:
		print("[AUTOPLAY]   - %s ×%d | 首例: %s" % [k, int(_anomaly_counts[k]), _anomaly_first[k]])
	if _anomaly_counts.is_empty():
		print("[AUTOPLAY]   （无异常）")
	print("[AUTOPLAY] ===================================================")
	# 清理：释放输入、恢复全局状态、删残留存档、恢复原难度
	_release_all_inputs()
	Engine.time_scale = 1.0
	get_tree().paused = false
	GameState.delete_save()
	if GameState.difficulty != _prev_difficulty:
		GameState.set_difficulty(_prev_difficulty)
	get_tree().quit(0)
