extends Node
## 模拟人工游玩探针：实例化真实对局（scenes/main.tscn），登录固定探针用户真实开局，
## 用合成输入（Input.action_press + 合成鼠标移动）像真人一样游玩一整局，全程日志化。
##
## 行为覆盖：随机走位/规避、Buff 三选一（优先未拥有过的种类，争取覆盖 19 种）、
## 低血返航 B（基地 4 模块全操作：维修/补给/天赋路线/任务领奖）、召唤母舰 H
## （含蓄力主动取消、非驻留态乱按、驻留驾驶、提前离舰、驻留到超时强制弹射）、
## 狂暴子弹时间高频冲刺、随机暂停/恢复 + 暂停菜单「保存进度」、
## 主动重进 main 走启动自动读档恢复（StartPanel 已退役后的「继续对局」路径）、
## 对局中轮换设置（视角/窗口/语言/难度，数秒后切回）、
## 暂停菜单退出确认窗「打开→取消」探针（永不确认退出，避免杀掉测试进程）。
## 监控维度：每 5s 快照记录 Performance 监控器（对象/节点/孤儿节点/静态内存/FPS）、
## 节点数与对象数上涨趋势、GameState.enemies 注册表 vs enemy 组场景集合双向差集一致性、
## player_ref/对象池引用有效性、池规模上界、帧耗时恶化趋势，外加既有的
## 卡死/数值越界/实体爆增/UI 状态一致性检查。异常用 printerr("[ANOMALY] ...") 记录但不中断。
##
## 探针性质：除崩溃/硬断言外不以 FAIL 结束，结尾打印 SUMMARY 供人工/脚本分析。
## 引擎层 ERROR/WARNING 统计需在外部对 stderr 计数（进程内读不到自身 stderr）。
##
## 运行：godot --headless --path . res://test/autoplay_test.tscn [-- --autoplay-seconds=480] [-- --seed=N]

const _FIGHT_P2 := 1  # M3d：Boss.FightPhase.P2 镜像（P1=0/P2=1/ENRAGE=2）

# M4：迁移类脚本引用（C# 类不可类名引用，经脚本资源调静态枚举访问器）
const MOTHER := preload("res://csharp/godot/Mothership.cs")
const ELITE := preload("res://csharp/godot/EliteTurretEvent.cs")
const FORMATION := preload("res://csharp/godot/FormationStrikeEvent.cs")

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
const SLOW_STUCK_MS := 15000  # 狂暴减速残留判定（无狂暴 Boss 但 _enrage_slow 未复位）
# 母舰各状态预期最长停留（State 枚举序 -> ms）
# L01（2026-08-03 审查）：母舰状态表与 Mothership.State 枚举对齐（6 态，无 HOVER——
# 原 7 项表把 DOCKING/RESUPPLY/STAY/RELEASE/DEPART 整体错位一档：真实 STAY 被配 10s
# 阈值导致驻留期必误报 mothership_stuck，RELEASE 被配 70s 卡死漏报）
const MS_STATE_TIMEOUTS: Array[int] = [20000, 10000, 10000, 70000, 10000, 30000]
const MS_STATE_NAMES: Array[String] = ["DESCEND", "DOCKING", "RESUPPLY", "STAY", "RELEASE", "DEPART"]
# 实体爆增阈值
const MAX_PLAYER_BULLETS := 300
const MAX_ENEMY_BULLETS := 800
const MAX_ENEMIES := 120
# 池规模上界（闲置实例数应稳定在峰值并发附近；持续超过即疑似复用再次失效）
const MAX_BULLET_POOL := 150
const MAX_ENEMY_POOL := 100
# 行为节奏
const PAUSE_GAP_MS := 20000  # 随机暂停最小间隔
const SETTING_GAP_MS := 25000  # 设置切换最小间隔
const SETTING_RESTORE_MS := 4500  # 切出后多久切回
const MENU_RETURN_DELAY_MS := 1200  # 暂停存档后重进 main 的延迟
# 对象数泄漏判定（快照连续上涨次数与相对基线倍数）
const OBJECT_LEAK_STREAK := 4
const OBJECT_LEAK_RATIO := 1.8

const MOVE_ACTIONS: Array[StringName] = [&"move_left", &"move_right", &"move_up", &"move_down"]
const SETTING_KINDS: Array[StringName] = [
	&"view_zoom",
	&"window_size",
	&"locale",
	&"difficulty",
	&"aim_assist",
	&"reduce_flash",
	&"ctrl_toggle",
	&"shift_toggle",
]
const BUFF_POOL_SIZE := 19  # buff_select.gd BUFF_POOL 种类数（覆盖率统计分母；2026-08-05 P4 修正 16→19）

var _run_seconds: float = DEFAULT_RUN_SECONDS
var _seed := 20260722
var _started := false
var _finished := false
var _t0_msec: int = 0
var _last_snap_msec: int = 0
var _last_check_msec: int = 0

# 对局引用（重开时刷新）
var _main = null  # main.gd 无 class_name，保持动态访问
var _player = null
var _spawner = null
var _buff_ui: CanvasLayer = null
var _boss = null
var _bullet_script: Script = load("res://csharp/godot/Bullet.cs")  # 随批次 D 重定型：Bullet 已迁 C#，as Bullet 改经脚本引用判定
var _enemy_script := load("res://csharp/godot/Enemy.cs")  # M3b：Enemy 迁 C#，as Enemy 改经脚本引用判定

# bot 状态
var _move_target := Vector2.ZERO
var _next_move_decision: int = 0
var _next_aim: int = 0
var _dash_release_at: int = 0
var _next_dash_try: int = 0
var _dock_holding := false
var _dock_hold_until: int = 0
var _dock_cancel_episode := false  # 本次蓄力是"主动取消"探针
var _next_dock_consider: int = 0
var _stay_since: int = 0
var _stay_until_eject := false  # 本次驻留等到超时强制弹射
var _early_leave_at: int = 0
var _early_holding := false
var _early_hold_until: int = 0
var _home_holding := false
var _home_hold_until: int = 0
var _next_home_consider: int = 0
var _restart_at: int = 0
# 暂停链路
var _next_pause_consider: int = 0
var _pause_open_since: int = 0
var _pause_stage: int = 0
var _settings_open_since: int = 0  # 暂停菜单内打开设置页的时刻
var _menu_return_at: int = 0  # >0：到点重进 main 走自动读档恢复
# B 梯队（2026-08-03 fair plan §8）+ Phase 0 L13 探针更新：
# DDA 降档状态跟踪 / 死亡回放节点泄漏 / 母舰×事件互斥
var _last_damaged_msec: int = -1  # 最近一次玩家受击（player_damaged 信号）
var _dda_trigger_count: int = 0  # DDA 降档触发次数（受击且此前未激活）
var _dda_stuck_reported := false
var _replay_node: Node = null  # 当前死亡回放演出节点（DeathReplayPlayer）
var _replay_since: int = 0
var _replay_stuck_reported := false
var _replay_seen_count: int = 0  # 死亡回放演出观察次数
const DDA_STUCK_MS := 9000  # dda.duration=5s；无受击超 9s 仍激活 = 降档卡死
const REPLAY_STUCK_MS := 5000  # 重放 3s 播完；超 5s 未自毁 = 泄漏
# 设置轮换
var _next_setting_at: int = 0
var _setting_restore_at: int = 0
var _setting_restore: Dictionary = {}  # {kind, old}

# 事件/异常 episode 状态
var _buff_open_since: int = 0
var _buff_pick_at: int = 0
var _buff_stuck_reported := false
var _boss_since: int = 0
var _boss_timeout_reported := false
var _ms_last_state: int = -1
var _ms_state_since: int = 0
var _ms_stuck_reported := false
var _homecoming_pending_since_ms: int = 0  # 返航过场结束后才开始计时（过场期每检查点顺延）
var _home_stuck_reported := false
var _slow_since: int = 0  # 玩家狂暴减速但无狂暴 Boss 的持续起点
var _slow_reported := false
var _base_since: int = 0
var _base_stage: int = 0
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
# 引擎级监控
var _obj_baseline: float = 0.0
var _obj_prev: float = 0.0
var _obj_rise_streak: int = 0
var _obj_leak_armed := true
var _frame_count: int = 0
var _frame_snap_msec: int = 0
var _frame_ms_baseline: float = 0.0
var _frame_snaps: int = 0
var _frame_slow_streak: int = 0
var _max_frame_ms: float = 0.0

# 统计
var _run_index: int = 0
var _deaths: int = 0
var _total_kills: int = 0
var _total_boss_kills: int = 0
var _run_scores: Array[int] = []
var _buff_picks: int = 0
var _buff_animated_picks: int = 0  # 走三参确认动效路径的选取次数
var _buffs_seen: Dictionary = {}  # id -> true（跨局累计，覆盖率统计）
var _ms_summons: int = 0
var _charge_cancels: int = 0
var _early_leaves: int = 0
var _forced_ejects: int = 0
var _homecomings: int = 0
var _pause_saves: int = 0
var _settings_opens: int = 0
var _continue_resumes: int = 0
var _exit_probes: int = 0
var _setting_switches: int = 0
var _milestones: int = 0
var _boss_escapes: int = 0
var _base_repairs: int = 0
var _base_recharges: int = 0
var _route_choices: int = 0
var _mission_claims: int = 0
var _boss_p2_count: int = 0
var _boss_enrage_count: int = 0
var _turret_event_count: int = 0
var _formation_event_count: int = 0
var _event_was_active := false
var _formation_was_active := false
var _max_nodes: int = 0
var _max_enemy_bullets: int = 0
var _max_player_bullets: int = 0
var _max_enemies: int = 0
var _max_orphans: float = 0.0
var _max_bullet_pool: int = 0
var _max_enemy_pool: int = 0
var _anomaly_counts: Dictionary = {}
var _anomaly_first: Dictionary = {}
# 进程初始设置（结束恢复，同难度既有做法）
var _prev_difficulty: StringName = &"medium"
var _prev_view_zoom: StringName = &"medium"
var _prev_window_size: StringName = &"large"
var _prev_locale: String = "zh"
var _prev_aim_assist: StringName = &"medium"
var _prev_reduce_flash := false
var _prev_ctrl_toggle := false
var _prev_shift_toggle := false


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
		elif arg.begins_with("--seed="):
			_seed = int(arg.split("=")[1])
	seed(_seed)
	# 确定性：清残留存档；固定 medium 难度（结束恢复原档位，同 smoke_test 做法）
	GameState.delete_save()
	_prev_difficulty = GameState.difficulty
	_prev_view_zoom = GameState.view_zoom
	_prev_window_size = GameState.window_size
	_prev_locale = GameState.locale
	_prev_aim_assist = GameState.aim_assist_level
	_prev_reduce_flash = GameState.reduce_flash
	_prev_ctrl_toggle = GameState.ctrl_toggle_mode
	_prev_shift_toggle = GameState.shift_toggle_mode
	GameState.set_difficulty(&"medium")
	GameState.milestone_reached.connect(_on_milestone)
	GameState.player_died.connect(_on_player_died)
	GameState.health_changed.connect(_on_health_changed)
	# B 梯队：DDA 降档由受击信号驱动（同 Meta HUD 受击层同源）
	GameState.player_damaged.connect(_on_player_damaged)
	_t0_msec = Time.get_ticks_msec()
	_last_snap_msec = _t0_msec
	_last_check_msec = _t0_msec
	_frame_snap_msec = _t0_msec
	_next_setting_at = _t0_msec + SETTING_GAP_MS
	_next_pause_consider = _t0_msec + PAUSE_GAP_MS
	_log("START budget=%.0fs real (time_scale=%.1f) seed=%d" % [_run_seconds, TIME_SCALE, _seed])
	_start_run()


## 实例化 main 走真实开局路径（登录固定探针用户；有档时 main 启动自动读档继续——T4 适配）
func _start_run(p_continue: bool = false) -> void:
	_run_index += 1
	_log("=== RUN %d START（%s） ===" % [_run_index, "继续对局" if p_continue else "新游戏"])
	if not GameState.user_exists("autoplay_runner"):
		GameState.create_user("autoplay_runner", "autoplay_pass")
	GameState.login_user("autoplay_runner")
	if not p_continue:
		GameState.delete_save()  # 新局清该用户存档
	_main = (load("res://scenes/main.tscn") as PackedScene).instantiate()
	# 本测试根节点必须是 ALWAYS（暂停期间继续驱动 bot），但 Main 继承根节点的模式
	# 会导致整个对局在暂停时照跑——显式把 Main 钉回 PAUSABLE，还原真实暂停语义。
	_main.process_mode = Node.PROCESS_MODE_PAUSABLE
	add_child(_main)
	await get_tree().process_frame
	await get_tree().process_frame
	# 退出确认窗探针（暂停→退出→取消，绝不确认，避免 quit 杀进程）
	if randf() < 0.3:
		_probe_exit_confirm()
		await get_tree().process_frame
		await get_tree().process_frame
	if p_continue:
		_continue_resumes += 1
		_log("继续对局：main 启动自动读档恢复（第 %d 次，存档 score=%d）" % [_continue_resumes, GameState.score])
	await get_tree().process_frame
	_player = _main.get_node("Player")
	_spawner = _main.get_node("Spawner")
	_buff_ui = _main.get_node("BuffUI")
	_spawner.BossSpawned.connect(_on_boss_spawned)
	_move_target = _player.position
	_last_hp = GameState.health
	_last_score = GameState.score
	_score_change_msec = Time.get_ticks_msec()
	Engine.time_scale = TIME_SCALE
	_started = true


## 退出确认窗「打开→取消」探针：覆盖 BackNavigator CANCEL_EXIT 分支（暂停→退出游戏→取消）
func _probe_exit_confirm() -> void:
	var exit_confirm: CanvasLayer = _main.get_node("ExitConfirm")
	var pause_ui: CanvasLayer = _main.get_node("PauseUI")
	pause_ui.open()
	pause_ui.quit()
	if not exit_confirm.visible:
		_anomaly("exit_confirm_no_show", "暂停→退出游戏未弹出确认窗")
		pause_ui.close()
		return
	_main.get_node("BackNavigator").go_back()  # 确认窗可见 → CANCEL_EXIT
	if exit_confirm.visible:
		_anomaly("exit_confirm_stuck", "退出确认窗取消后仍可见")
		return
	pause_ui.close()
	_exit_probes += 1
	_log("退出确认窗 打开→取消 探针通过（第 %d 次）" % _exit_probes)


func _process(_delta: float) -> void:
	if not _started or _finished:
		return
	var now := Time.get_ticks_msec()
	_frame_count += 1
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
	if _main.bullet_time() <= 0.0 and _main.time_scale_ramp() < 0.0 and Engine.time_scale != TIME_SCALE:
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
	# 主动重进 main（存档保留，启动自动读档恢复）
	if _menu_return_at > 0 and now >= _menu_return_at:
		_menu_return_at = 0
		_do_menu_return()
		return
	_handle_buff_ui(now)
	_handle_base_ui(now)
	_handle_pause_ui(now)
	var playing = not get_tree().paused and _player != null and not _player.is_dead()
	if playing:
		_update_movement(now)
		_update_aim(now)
		_update_dash(now)
		_update_dock(now)
		_update_homecoming(now)
		_update_pause(now)
		_update_settings(now)
	_track_mothership(now)


func _handle_buff_ui(now: int) -> void:
	if _buff_ui == null or not is_instance_valid(_buff_ui):
		return
	if _buff_ui.visible:
		if _buff_ui.closing():
			# 确认动效播放中：不重复 pick（动效结束才 visible=false）；动效期也纳入卡死计时
			if _buff_open_since == 0:
				_buff_open_since = now
				_buff_stuck_reported = false
			return
		if _buff_open_since == 0:
			_buff_open_since = now
			_buff_stuck_reported = false
			_buff_pick_at = now + 400 + randi() % 600  # 模拟真人看牌时间
			var ids: Array = []
			for b in _buff_ui.current_available():
				ids.append(b["id"])
			_log("Buff 三选一弹出 candidates=%s" % [str(ids)])
		elif now >= _buff_pick_at:
			var avail: Array = _buff_ui.current_available()
			if not avail.is_empty():
				# 优先选本进程尚未拥有过的种类，争取覆盖全部 buff 效果代码
				var unseen: Array = avail.filter(func(b: Dictionary) -> bool: return not _buffs_seen.has(b["id"]))
				var pool: Array = unseen if not unseen.is_empty() else avail
				var pick: Dictionary = pool[randi() % pool.size()]
				var pick_idx := -1  # 候选卡在 _cards 中的索引（顺序与 _current_available 对应）
				for i in avail.size():
					if avail[i]["id"] == pick["id"]:
						pick_idx = i
						break
				var ev := InputEventMouseButton.new()
				ev.pressed = true
				ev.button_index = MOUSE_BUTTON_LEFT
				# 10% 走真实三参确认动效路径：合成鼠标左键事件发给目标卡片
				# （card.gui_input → _on_card_gui_input 的 card!=null 分支 → _closing 动效，
				# ~200ms 后 _on_pick_close_finished 才关闭面板并恢复对局）；其余走两参立即关闭
				var card: Control = null
				var animated := false
				if randf() < 0.10 and pick_idx >= 0 and _buff_ui.cards().get_child_count() > pick_idx:
					card = _buff_ui.cards().get_child(pick_idx) as Control
				if card != null:
					card.gui_input.emit(ev)
					animated = true
				else:
					_buff_ui.pick_buff(pick["id"])
				_buff_picks += 1
				if animated:
					_buff_animated_picks += 1
				_buffs_seen[pick["id"]] = true
				# 选取后立即校验层数上限（口径同 buff_select.gd：cfg 覆盖池内默认）
				var pool_max := 1
				for b in _buff_ui.BUFF_POOL:
					if b["id"] == pick["id"]:
						pool_max = int(b["max"])
						break
				var cap := int(GameState.cfg("buffs.%s.max_stacks" % pick["id"], pool_max))
				if GameState.buff_count(pick["id"]) > cap:
					_anomaly_rl("buff_over_cap", "Buff %s 层数 %d 超过上限 %d" % [pick["id"], GameState.buff_count(pick["id"]), cap], now)
				_log(
					(
						"Buff 选择: %s（层数 %d，已覆盖种类 %d/%d%s）"
						% [pick["id"], GameState.buff_count(pick["id"]), _buffs_seen.size(), BUFF_POOL_SIZE, "，动效路径" if animated else ""]
					)
				)
			_buff_open_since = 0
	else:
		_buff_open_since = 0


## 基地控制台全模块：维修 → 补给燃料 → 天赋路线选择 → 任务领奖 → 继续出击
func _handle_base_ui(now: int) -> void:
	if _main == null or not is_instance_valid(_main):
		return
	var base_ui: CanvasLayer = _main.base_ui()
	if base_ui.visible:
		if _base_since == 0:
			_base_since = now
			_base_stage = 0
			_base_stuck_reported = false
			_log("进入基地整备（RP=%d HP=%.0f）" % [GameState.rp, GameState.health])
			return
		var t := now - _base_since
		if _base_stage == 0 and t >= 600:
			_base_stage = 1
			if GameState.rp >= GameState.RP_REPAIR_COST and GameState.health < GameState.max_health():
				base_ui.repair()
				_base_repairs += 1
				_log("基地维修：HP -> %.0f（RP=%d）" % [GameState.health, GameState.rp])
		elif _base_stage == 1 and t >= 1000:
			_base_stage = 2
			if GameState.rp >= GameState.RP_RECHARGE_COST and _player != null and _player.fuel_amount() < _player.fuel_max - 1.0:
				base_ui.recharge()
				_base_recharges += 1
				_log("基地补给燃料：-> %.0f（RP=%d）" % [_player.fuel_amount(), GameState.rp])
		elif _base_stage == 2 and t >= 1400:
			_base_stage = 3
			for line in GameState.ROUTE_LINES:
				if GameState.chosen_routes.has(line):
					continue  # 每线每局限选一次
				var options: Array = GameState.ROUTE_LINES[line]
				var total := GameState.buff_count(options[0]) + GameState.buff_count(options[1])
				if total == 0:
					continue
				var opt: StringName = options[randi() % options.size()]
				if GameState.is_buff_locked(opt):
					opt = options[0] if options[1] == opt else options[1]
				if GameState.is_buff_locked(opt):
					continue
				base_ui.choose_route(line, opt)
				_route_choices += 1
				_log("天赋路线选择：%s -> %s（合并后 %d 层）" % [line, opt, GameState.buff_count(opt)])
		elif _base_stage == 3 and t >= 1800:
			_base_stage = 4
			# 任务轮换：领取在场已完成任务（active_mission_ids，非固定 MISSION_DEFS）
			for id in GameState.active_mission_ids():
				if GameState.is_mission_done(id) and not GameState.is_mission_claimed(id):
					base_ui.claim_mission(id)
					_mission_claims += 1
					_log("领取任务奖励：%s（RP=%d）" % [id, GameState.rp])
		elif _base_stage == 4 and t >= 2600:
			base_ui.resume()
			_log("继续出击，返回对局")
			_base_since = 0
	else:
		_base_since = 0


## 随机暂停：Esc 打开暂停菜单（走 BackNavigator 真实路由）
func _update_pause(now: int) -> void:
	if now < _next_pause_consider:
		return
	_next_pause_consider = now + PAUSE_GAP_MS + randi() % 20000
	if randf() < 0.6:
		_main.get_node("BackNavigator").go_back()  # 战斗中 → OPEN_PAUSE
		if _main.pause_ui().visible:
			_pause_open_since = now
			_pause_stage = 0
			_log("暂停：Esc 打开暂停菜单")


## 暂停菜单链路：「保存进度」写档 →（50% 打开设置页再返回）→ 恢复对局（或重进 main 走自动读档）
func _handle_pause_ui(now: int) -> void:
	if _pause_open_since == 0:
		return
	var pause_ui: CanvasLayer = _main.pause_ui()
	var settings_ui: CanvasLayer = _main.get_node("SettingsUI")
	if not pause_ui.visible and not settings_ui.visible:
		_pause_open_since = 0  # 未打开成功或已被其他路径关闭
		return
	var t := now - _pause_open_since
	if _pause_stage == 0 and t >= 600:
		_pause_stage = 1
		if randf() < 0.75:
			pause_ui.save()
			_pause_saves += 1
			_log("暂停菜单：保存进度（第 %d 次）" % _pause_saves)
	elif _pause_stage == 1 and t >= 1300:
		# 50% 打开设置页（pause_ui 隐藏、SettingsUI 显示），随后 go_back 走
		# BackNavigator CLOSE_SETTINGS 真实路由返回（opener 恢复可见）
		if randf() < 0.5:
			pause_ui.open_settings()
			if settings_ui.visible:
				_settings_open_since = now
				_pause_stage = 2
				_log("暂停菜单：打开设置页")
			else:
				_pause_stage = 3
		else:
			_pause_stage = 3
	elif _pause_stage == 2 and now - _settings_open_since >= 900:
		_pause_stage = 3
		_settings_opens += 1
		_main.get_node("BackNavigator").go_back()  # 设置页 → CLOSE_SETTINGS
		_log("暂停菜单：设置页返回（第 %d 次）" % _settings_opens)
	elif _pause_stage == 3 and t >= 2400:
		_pause_stage = 4
		_pause_open_since = 0
		if randf() < 0.35 and GameState.has_save():
			# 重进 main → 启动自动读档恢复（与新游戏不同的代码路径）
			pause_ui.close()
			_menu_return_at = now + MENU_RETURN_DELAY_MS
			_log("暂停恢复，稍后重进 main 走自动读档继续")
		else:
			_main.get_node("BackNavigator").go_back()  # 暂停中 → RESUME_GAME
			_log("暂停：Esc 恢复对局")


## 对局中轮换设置项（视角/窗口/语言/难度），数秒后切回：压测热路径上的信号处理器
func _update_settings(now: int) -> void:
	if _setting_restore_at > 0:
		if now >= _setting_restore_at:
			var kind: StringName = _setting_restore["kind"]
			var old: Variant = _setting_restore["old"]
			_apply_setting(kind, old)
			_log("设置切回：%s -> %s" % [kind, str(old)])
			_setting_restore_at = 0
			_setting_restore = {}
		return
	if now < _next_setting_at:
		return
	_next_setting_at = now + SETTING_GAP_MS + randi() % 15000
	var kind: StringName = SETTING_KINDS[randi() % SETTING_KINDS.size()]
	var old: Variant
	var new_val: Variant
	match kind:
		&"view_zoom":
			old = GameState.view_zoom
			new_val = _pick_other(GameState.VIEW_ZOOM_LEVELS.keys(), old)
		&"window_size":
			old = GameState.window_size
			new_val = _pick_other(GameState.WINDOW_SIZE_LEVELS.keys(), old)
		&"locale":
			old = GameState.locale
			new_val = "en" if old == "zh" else "zh"
		&"difficulty":
			old = GameState.difficulty
			new_val = _pick_other(GameState.DIFFICULTY_ORDER, old)
		&"aim_assist":
			old = GameState.aim_assist_level
			new_val = _pick_other(GameState.AIM_ASSIST_ORDER, old)
		&"reduce_flash":
			old = GameState.reduce_flash
			new_val = not old
		&"ctrl_toggle":
			old = GameState.ctrl_toggle_mode
			new_val = not old
		&"shift_toggle":
			old = GameState.shift_toggle_mode
			new_val = not old
	if new_val == null or new_val == old:
		return
	_setting_restore = {"kind": kind, "old": old}
	_setting_restore_at = now + SETTING_RESTORE_MS
	_setting_switches += 1
	_apply_setting(kind, new_val)
	_log("设置切换：%s %s -> %s（%dms 后切回）" % [kind, str(old), str(new_val), SETTING_RESTORE_MS])


func _pick_other(options: Array, current: Variant) -> Variant:
	var others: Array = options.filter(func(o: Variant) -> bool: return o != current)
	if others.is_empty():
		return null
	return others[randi() % others.size()]


func _apply_setting(kind: StringName, value: Variant) -> void:
	match kind:
		&"view_zoom":
			GameState.set_view_zoom(value)
		&"window_size":
			GameState.set_window_size(value)
		&"locale":
			GameState.set_locale(value)
		&"difficulty":
			GameState.set_difficulty(value)
		&"aim_assist":
			GameState.set_aim_assist_level(value)
		&"reduce_flash":
			GameState.set_reduce_flash(value)
		&"ctrl_toggle":
			GameState.set_ctrl_toggle_mode(value)
		&"shift_toggle":
			GameState.set_shift_toggle_mode(value)


## 随机游走 + 远离密集敌弹/敌机的简单规避
func _update_movement(now: int) -> void:
	if now < _next_move_decision:
		return
	_next_move_decision = now + MOVE_DECISION_MS
	var view := GameState.view_world_rect()
	if _player.position.distance_to(_move_target) < 80.0 or randf() < 0.1:
		_move_target = Vector2(
			randf_range(view.position.x + 100.0, view.end.x - 100.0), randf_range(view.position.y + 100.0, view.end.y - 100.0)
		)
	var steer = _move_target - _player.position
	steer = steer.normalized() if steer.length() > 60.0 else Vector2.ZERO
	# 规避：240px 内敌弹/编队炸弹（同权重）+ 160px 内敌机的反加权和
	var dodge := Vector2.ZERO
	for child in _main.get_children():
		var b = child
		if is_instance_of(b, _bullet_script):
			if not b.IsPlayerBullet:
				var d: float = _player.position.distance_to(b.position)
				if d < 240.0 and d > 1.0:
					dodge += (_player.position - b.position) / d * (1.0 - d / 240.0) * 2.0
			continue
		var fb = child if is_instance_of(child, load("res://csharp/godot/FormationBomb.cs")) else null
		if fb != null:
			var d: float = _player.position.distance_to(fb.position)
			if d < 240.0 and d > 1.0:
				dodge += (_player.position - fb.position) / d * (1.0 - d / 240.0) * 2.0
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


## 周期性冲刺：敌弹密集时优先触发（需已解锁 phase_dash）；狂暴/子弹时间期间更频繁
func _update_dash(now: int) -> void:
	if _dash_release_at > 0 and now >= _dash_release_at:
		Input.action_release("dash")
		_dash_release_at = 0
	if now < _next_dash_try:
		return
	var enrage_active: bool = (
		(_boss != null and is_instance_valid(_boss) and _boss.is_enraged()) or _main.bullet_time() > 0.0 or _main.time_scale_ramp() >= 0.0
	)
	_next_dash_try = now + (250 if enrage_active else 500)
	if not _player.dash_unlocked() or _player.dash_cooldown() > 0.0 or _player.is_dashing():
		return
	var threat := 0.0
	for child in _main.get_children():
		var b = child
		if is_instance_of(b, _bullet_script) and not b.IsPlayerBullet:
			var d: float = _player.position.distance_to(b.position)
			if d < 240.0:
				threat += 1.0 - d / 240.0
	var threshold := 0.4 if enrage_active else 1.0
	var idle_chance := 0.25 if enrage_active else 0.05
	if threat > threshold or randf() < idle_chance:
		Input.action_press("dash")
		_dash_release_at = now + 150


## 母舰：Boss 战或 HP 偏低时概率蓄力召唤（含蓄力主动取消探针）；
## 驻留驾驶一段时间（WASD 已被移动驱动复用）后提前离舰，或驻留到超时强制弹射
func _update_dock(now: int) -> void:
	if _main.is_homecoming() or _main.is_game_over():
		if _dock_holding:
			Input.action_release("dock")
			_dock_holding = false
		return
	var ms = _main.mothership()
	if _dock_holding:
		if ms != null:
			Input.action_release("dock")
			_dock_holding = false
			_ms_summons += 1
			_log("母舰召唤成功（第 %d 次）" % _ms_summons)
		elif now >= _dock_hold_until:
			Input.action_release("dock")
			_dock_holding = false
			if _dock_cancel_episode:
				_charge_cancels += 1
				_log("母舰蓄力主动取消（第 %d 次）" % _charge_cancels)
			else:
				_log("母舰蓄力超时未召唤，松手")
			_dock_cancel_episode = false
	elif ms == null and _main.dock_cooldown() <= 0.0 and now >= _next_dock_consider:
		var hp_ratio: float = GameState.health / GameState.max_health()
		if _boss != null or hp_ratio < 0.7:
			var roll := randf()
			if roll < 0.15:
				# 蓄力取消探针：按住短时间后在蓄满前松手
				Input.action_press("dock")
				_dock_holding = true
				_dock_cancel_episode = true
				_dock_hold_until = now + 300 + randi() % 600
				_log("开始蓄力召唤母舰（计划中途取消）")
			elif roll < 0.6:
				Input.action_press("dock")
				_dock_holding = true
				_dock_cancel_episode = false
				_dock_hold_until = now + 8000  # 蓄力 3s + 机库小窗 ~2.6s，留足余量
				_log("开始蓄力召唤母舰（boss=%s hp=%.0f%%）" % [str(_boss != null), hp_ratio * 100.0])
			else:
				_next_dock_consider = now + 20000
		else:
			_next_dock_consider = now + 10000
	elif ms != null and ms.state() < MOTHER.GetStateStay() and randf() < 0.002:
		# 边界探针：非驻留态（降入/吸附/补给）乱按 H，应为无操作
		Input.action_press("dock")
		Input.action_release("dock")
	# 驻留驾驶一段时间后提前离舰；部分局驻留到超时强制弹射
	if _early_holding:
		if ms == null or ms.state() >= MOTHER.GetStateRelease() or now >= _early_hold_until:
			Input.action_release("dock")
			_early_holding = false
			if ms != null and ms.state() >= MOTHER.GetStateRelease():
				_early_leaves += 1
				_log("提前离舰（第 %d 次，弹匣 %d 格）" % [_early_leaves, ms.mag_cells()])
	elif ms != null and ms.state() == MOTHER.GetStateStay():
		if _stay_since == 0:
			_stay_since = now
			_stay_until_eject = randf() < 0.35
			_early_leave_at = now + (60000 if _stay_until_eject else 6000 + randi() % 8000)
			if _stay_until_eject:
				_log("本次驻留等到超时强制弹射")
		elif now >= _early_leave_at:
			Input.action_press("dock")
			_early_holding = true
			_early_hold_until = now + 4000
	else:
		_stay_since = 0


## 返航：血量低（或 Boss 战且半血以下）概率蓄力 B
func _update_homecoming(now: int) -> void:
	if _main.is_homecoming():
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
	if now < _next_home_consider or _main.is_game_over():
		return
	_next_home_consider = now + 8000
	var hp_ratio: float = GameState.health / GameState.max_health()
	var want = hp_ratio < 0.35 or (_boss != null and hp_ratio < 0.6)
	if want and randf() < 0.6:
		Input.action_press("homecoming")
		_home_holding = true
		_home_hold_until = now + 4000
		_log("开始蓄力返航（hp=%.0f%% boss=%s）" % [hp_ratio * 100.0, str(_boss != null)])


## 母舰状态变化日志 + 卡死 episode 跟踪
func _track_mothership(now: int) -> void:
	var ms = _main.mothership() if (_main != null and is_instance_valid(_main)) else null
	var state := -1 if ms == null else int(ms.state())
	if state != _ms_last_state:
		if _ms_last_state >= 0 or state >= 0:
			var from_s := "NONE" if _ms_last_state < 0 else MS_STATE_NAMES[_ms_last_state]
			var to_s := "NONE" if state < 0 else MS_STATE_NAMES[state]
			_log("母舰状态 %s -> %s" % [from_s, to_s])
		if _ms_last_state == MOTHER.GetStateStay() and state == MOTHER.GetStateRelease():
			if _stay_until_eject and not _early_holding:
				_forced_ejects += 1
				_log("驻留超时强制弹射（第 %d 次）" % _forced_ejects)
			_stay_until_eject = false
		_ms_last_state = state
		_ms_state_since = now
		_ms_stuck_reported = false
	elif state >= 0 and not _ms_stuck_reported:
		if state < MS_STATE_TIMEOUTS.size() and now - _ms_state_since > MS_STATE_TIMEOUTS[state]:
			_ms_stuck_reported = true
			@warning_ignore("integer_division")
			_anomaly("mothership_stuck", "母舰状态 %s 超过 %ds 未推进" % [MS_STATE_NAMES[state], MS_STATE_TIMEOUTS[state] / 1000])


# ---------------- 事件 ----------------


func _on_milestone(milestone_score: int) -> void:
	_milestones += 1
	_log("里程碑达成 score=%d（第 %d 次）" % [milestone_score, _milestones])


func _on_boss_spawned(boss) -> void:  # M3d：Boss 迁 C#，注解 untyped
	_boss = boss
	_boss_since = Time.get_ticks_msec()
	_boss_timeout_reported = false
	_log("Boss 出现 type=%d hp=%.0f" % [boss.boss_type, boss.max_hp])
	boss.enraged.connect(
		func() -> void:
			_boss_enrage_count += 1
			_log("Boss 狂暴 type=%d（第 %d 次）" % [boss.boss_type, _boss_enrage_count])
	)
	boss.phase_changed.connect(_on_boss_phase_changed)
	boss.died.connect(_on_boss_died.bind(boss))
	boss.escaped.connect(
		func() -> void:
			_boss_escapes += 1
			_log("Boss 逃跑 type=%d（第 %d 次）" % [boss.boss_type, _boss_escapes])
			_clear_boss(boss)
	)
	boss.tree_exited.connect(func() -> void: _clear_boss(boss))


func _on_boss_phase_changed(new_phase: int) -> void:
	if new_phase == _FIGHT_P2:  # M3d：Boss.FightPhase.P2（C# 枚举不可类名引用，0/1/2 镜像声明序）
		_boss_p2_count += 1
		_log("Boss 进入 P2（第 %d 次）" % _boss_p2_count)


func _on_boss_died(boss) -> void:  # M3d
	if boss.is_escaped:
		return  # 逃跑离场也会发 died（通知血条/生成器），非击杀
	_total_boss_kills += 1
	_log("Boss 击杀 type=%d（本进程累计 %d）" % [boss.boss_type, _total_boss_kills])
	_clear_boss(boss)


func _clear_boss(boss) -> void:  # M3d
	if _boss == boss:
		_boss = null


func _on_player_died() -> void:
	_deaths += 1
	_total_kills += GameState.kills
	_run_scores.append(GameState.score)
	_log("玩家死亡 run=%d score=%d kills=%d boss_kills=%d" % [_run_index, GameState.score, GameState.kills, GameState.boss_kills])
	_release_all_inputs()
	_menu_return_at = 0
	_restart_at = Time.get_ticks_msec() + 3000  # 留 3s 走到结算界面


func _on_health_changed(new_health: float) -> void:
	var now := Time.get_ticks_msec()
	if _last_hp >= 0.0 and new_health < _last_hp - 0.01 and now - _last_hit_log_msec > 1000:
		_last_hit_log_msec = now
		_log("玩家受击 HP %.1f -> %.1f" % [_last_hp, new_health])
	_last_hp = new_health


## B 梯队：受击触发 DDA 降档——记录时刻与触发次数（受击即计数：
## GameState 自连接先于本回调置位，用 dda_active 判触发会恒 false）
func _on_player_damaged(_amount: float, _from_pos: Vector2) -> void:
	_last_damaged_msec = Time.get_ticks_msec()
	_dda_trigger_count += 1


## 死亡重开：删档开新局
func _do_restart() -> void:
	_log("重开新一局")
	_reset_transition_state()
	await get_tree().process_frame
	await get_tree().process_frame
	GameState.delete_save()
	GameState.reset_run()
	_start_run()


## 主动重进 main：保留存档重新实例化 main（启动自动读档，等价于回主界面再进对局）
func _do_menu_return() -> void:
	_log("主动重进 main 读档继续（保留存档 score=%d）" % GameState.score)
	_reset_transition_state()
	await get_tree().process_frame
	await get_tree().process_frame
	_start_run(true)  # 不 delete_save、不 reset_run：读档恢复路径


func _reset_transition_state() -> void:
	_boss = null
	_ms_last_state = -1
	_stay_since = 0
	_stay_until_eject = false
	_buff_open_since = 0
	_homecoming_pending_since_ms = 0
	_slow_since = 0
	_slow_reported = false
	_event_was_active = false
	_formation_was_active = false
	_pause_open_since = 0
	get_tree().paused = false
	_started = false
	_release_all_inputs()
	_main.queue_free()


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
		var b = child
		if is_instance_of(b, _bullet_script):
			if b.IsPlayerBullet:
				p_bullets += 1
			else:
				e_bullets += 1
	var main_nodes: int = _main.get_child_count()
	var total_nodes := _count_nodes(get_tree().root)
	_max_nodes = maxi(_max_nodes, total_nodes)
	_max_enemy_bullets = maxi(_max_enemy_bullets, e_bullets)
	_max_player_bullets = maxi(_max_player_bullets, p_bullets)
	_max_enemies = maxi(_max_enemies, GameState.enemies.size())
	# 引擎级监控器
	var obj_count := Performance.get_monitor(Performance.OBJECT_COUNT)
	var node_count := Performance.get_monitor(Performance.OBJECT_NODE_COUNT)
	var orphans := Performance.get_monitor(Performance.OBJECT_ORPHAN_NODE_COUNT)
	var mem_static := Performance.get_monitor(Performance.MEMORY_STATIC)
	var fps := Performance.get_monitor(Performance.TIME_FPS)
	_max_orphans = maxf(_max_orphans, orphans)
	# 池规模
	var bullet_pool_n := -1
	var enemy_pool_n := -1
	if GameState.bullet_pool != null and is_instance_valid(GameState.bullet_pool):
		bullet_pool_n = (GameState.bullet_pool as Node).get_child_count()
		_max_bullet_pool = maxi(_max_bullet_pool, bullet_pool_n)
	if GameState.enemy_pool != null and is_instance_valid(GameState.enemy_pool):
		enemy_pool_n = (GameState.enemy_pool as Node).get_child_count()
		_max_enemy_pool = maxi(_max_enemy_pool, enemy_pool_n)
	# 帧耗时（真实 ms/帧，含 time_scale=2 的放大效应）
	var frame_ms := 0.0
	var frames := _frame_count
	_frame_count = 0
	if frames > 0:
		frame_ms = float(now - _frame_snap_msec) / float(frames)
	_frame_snap_msec = now
	_max_frame_ms = maxf(_max_frame_ms, frame_ms)
	var boss_s := "none"
	if _boss != null and is_instance_valid(_boss):
		boss_s = "type%d hp=%.0f/%.0f%s" % [_boss.boss_type, _boss.hp, _boss.max_hp, "(enraged)" if _boss.is_enraged() else ""]
	var ms_s := "none" if _main.mothership() == null else MS_STATE_NAMES[int(_main.mothership().state())]
	var dda_s := "on" if GameState.dda_active() else "-"  # B 梯队：DDA 降档状态
	_log(
		(
			(
				"SNAP run=%d t_game=%.0fs score=%d hp=%.0f/%.0f kills=%d enemies=%d "
				+ "bullets(p=%d,e=%d) boss=%s ms=%s dda=%s diff=%.2f elapsed=%.0fs "
				+ "nodes(main=%d,total=%d) ts=%.2f paused=%s "
				+ "perf(obj=%.0f,nodes=%.0f,orphan=%.0f,mem=%.1fMB,fps=%.0f,fms=%.2f) pool(b=%d,e=%d)"
			)
			% [
				_run_index,
				GameState.run_time,
				GameState.score,
				GameState.health,
				GameState.max_health(),
				GameState.kills,
				GameState.enemies.size(),
				p_bullets,
				e_bullets,
				boss_s,
				ms_s,
				dda_s,
				GameState.difficulty_multiplier,
				_spawner.elapsed(),
				main_nodes,
				total_nodes,
				Engine.time_scale,
				str(get_tree().paused),
				obj_count,
				node_count,
				orphans,
				mem_static / 1048576.0,
				fps,
				frame_ms,
				bullet_pool_n,
				enemy_pool_n,
			]
		)
	)
	# 孤儿节点：任何非零值都是泄漏信号（比节点直方图更灵敏）
	if orphans > 0.0:
		_anomaly_rl("orphan_nodes", "孤儿节点数 %.0f" % orphans, now)
	# 池规模上界
	if bullet_pool_n > MAX_BULLET_POOL:
		_anomaly_rl("pool_growth", "子弹池闲置实例 %d 超过 %d" % [bullet_pool_n, MAX_BULLET_POOL], now)
	if enemy_pool_n > MAX_ENEMY_POOL:
		_anomaly_rl("pool_growth", "敌机池闲置实例 %d 超过 %d" % [enemy_pool_n, MAX_ENEMY_POOL], now)
	# 节点泄漏趋势：连续 3 个快照上涨且超过基线 3 倍
	if _node_baseline == 0:
		_node_baseline = total_nodes
		_node_prev = total_nodes
		_obj_baseline = obj_count
		_obj_prev = obj_count
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
	# 对象数泄漏趋势（池复用失效时对象只进不出，比节点数更早显现）
	if obj_count > _obj_prev:
		_obj_rise_streak += 1
	else:
		_obj_rise_streak = 0
	_obj_prev = obj_count
	if obj_count < _obj_baseline * 1.3:
		_obj_leak_armed = true
	if _obj_leak_armed and _obj_rise_streak >= OBJECT_LEAK_STREAK and obj_count > _obj_baseline * OBJECT_LEAK_RATIO:
		_obj_leak_armed = false
		_anomaly("object_leak", "对象数连续上涨 %.0f -> %.0f（基线 %.0f）" % [_obj_baseline, obj_count, _obj_baseline])
		_dump_node_histogram()
	# 帧耗时恶化：前 5 个快照取最小值作基线（避免初始恰逢演出/弹幕高峰抬高基线
	# 而掩盖真实悬崖），持续 3 倍即报（难度升高后的性能悬崖）
	if frame_ms > 0.0:
		_frame_snaps += 1
		if _frame_snaps <= 5:
			_frame_ms_baseline = frame_ms if _frame_ms_baseline <= 0.0 else minf(_frame_ms_baseline, frame_ms)
		elif _frame_ms_baseline > 0.0 and frame_ms > _frame_ms_baseline * 3.0:
			_frame_slow_streak += 1
			if _frame_slow_streak >= 3:
				_frame_slow_streak = 0
				_anomaly(
					"frame_time",
					(
						"帧耗时恶化 %.2fms（基线 %.2fms，enemies=%d bullets=%d）"
						% [frame_ms, _frame_ms_baseline, GameState.enemies.size(), p_bullets + e_bullets]
					)
				)
		else:
			_frame_slow_streak = 0


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


## 注册表差集诊断用：节点类名（有脚本的取脚本文件名，同 _histogram_line 口径）
func _class_label(obj: Object) -> String:
	var n := obj as Node
	if n != null and n.get_script() != null:
		return (n.get_script() as Script).resource_path.get_file().get_basename()
	return obj.get_class()


## 「类名×n, ...」格式化（注册表差集消息明细）
func _fmt_class_counts(counts: Dictionary) -> String:
	var parts: Array[String] = []
	for k in counts:
		parts.append("%s×%d" % [k, int(counts[k])])
	return ", ".join(parts)


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
	var replay_found: Node = null  # B 梯队：死亡回放演出节点（同遍历检测）
	for child in _main.get_children():
		var b = child
		if is_instance_of(b, _bullet_script):
			if b.IsPlayerBullet:
				p_bullets += 1
			else:
				e_bullets += 1
		elif is_instance_of(child, load("res://csharp/godot/DeathReplayPlayer.cs")):
			replay_found = child
	if p_bullets > MAX_PLAYER_BULLETS:
		_anomaly_rl("entity_explosion", "玩家子弹数 %d 超过 %d" % [p_bullets, MAX_PLAYER_BULLETS], now)
	if e_bullets > MAX_ENEMY_BULLETS:
		_anomaly_rl("entity_explosion", "敌方子弹数 %d 超过 %d" % [e_bullets, MAX_ENEMY_BULLETS], now)
	if GameState.enemies.size() > MAX_ENEMIES:
		_anomaly_rl("entity_explosion", "敌机注册数 %d 超过 %d" % [GameState.enemies.size(), MAX_ENEMIES], now)
	# 注册表一致性：enemy 组集合与注册表双向差集比对
	# （四类注册者 Enemy/Boss/TurretBattery/FormationCraft 组语义与注册表一致；
	# 两侧都跳过 _active==false 的池化 Enemy——deactivate 同步注销、deferred reparent 亚帧窗口）
	var scene_set: Dictionary = {}  # Node -> true
	for n in get_tree().get_nodes_in_group("enemy"):
		var node := n as Node
		if node == null or not _main.is_ancestor_of(node):
			continue
		var en = node if is_instance_of(node, _enemy_script) else null  # M3b：Enemy 迁 C#，as Enemy 改脚本判定
		if en != null and not en.is_active():
			continue
		scene_set[node] = true
	var registry_set: Dictionary = {}  # 有效实例 -> true
	var stale_found := false
	for e in GameState.enemies:
		if not is_instance_valid(e):
			stale_found = true
			continue  # 失效实例归 registry_stale 管，不参与差集
		var re = e if is_instance_of(e, _enemy_script) else null  # M3b：Enemy 迁 C#，as Enemy 改脚本判定
		if re != null and not re.is_active():
			continue
		registry_set[e] = true
	if stale_found:
		_anomaly_rl("registry_stale", "GameState.enemies 含失效实例", now)
	var reg_extra: Dictionary = {}  # 类名 -> 计数
	for e in registry_set:
		if not scene_set.has(e):
			var k := _class_label(e)
			reg_extra[k] = int(reg_extra.get(k, 0)) + 1
	if not reg_extra.is_empty():
		_anomaly_rl(
			"registry_mismatch", "注册表多出: %s（注册表 %d vs 场景 %d）" % [_fmt_class_counts(reg_extra), registry_set.size(), scene_set.size()], now
		)
	var scene_extra: Dictionary = {}
	for node in scene_set:
		if not registry_set.has(node):
			var k := _class_label(node)
			scene_extra[k] = int(scene_extra.get(k, 0)) + 1
	if not scene_extra.is_empty():
		_anomaly_rl(
			"registry_mismatch", "场景多出: %s（注册表 %d vs 场景 %d）" % [_fmt_class_counts(scene_extra), registry_set.size(), scene_set.size()], now
		)
	# 引用有效性：player_ref / 对象池
	if _player != null and is_instance_valid(_player) and GameState.player_ref != _player:
		_anomaly_rl("player_ref_mismatch", "GameState.player_ref 未指向当前玩家", now)
	for pool_name in [&"bullet_pool", &"enemy_pool"]:
		var pool: Node = GameState.get(pool_name)
		if pool == null or not is_instance_valid(pool):
			_anomaly_rl("pool_ref_invalid", "GameState.%s 引用失效" % pool_name, now)
		elif pool.get_parent() != _main:
			_anomaly_rl("pool_ref_invalid", "GameState.%s 父节点不是当前 Main（残留旧对局池）" % pool_name, now)
	# Buff UI 卡死
	if _buff_ui != null and is_instance_valid(_buff_ui) and _buff_ui.visible:
		if _buff_open_since > 0 and now - _buff_open_since > BUFF_STUCK_MS and not _buff_stuck_reported:
			_buff_stuck_reported = true
			@warning_ignore("integer_division")
			_anomaly("buff_ui_stuck", "Buff UI 可见超过 %ds 未关闭" % (BUFF_STUCK_MS / 1000))
	# Boss 超时
	if _boss != null and is_instance_valid(_boss) and not _boss_timeout_reported:
		if now - _boss_since > BOSS_TIMEOUT_MS:
			_boss_timeout_reported = true
			@warning_ignore("integer_division")
			_anomaly("boss_timeout", "Boss type=%d 在场超过 %ds" % [_boss.boss_type, BOSS_TIMEOUT_MS / 1000])
	# 返航/基地卡死（返航过场播放期不计时：过场真实时长可达数十秒，计时起点顺延到结束）
	if _main.is_homecoming():
		if _main.return_cinematic() != null:
			_homecoming_pending_since_ms = now
			_home_stuck_reported = false
		elif _homecoming_pending_since_ms == 0:
			_homecoming_pending_since_ms = now
			_home_stuck_reported = false
		elif not _home_stuck_reported and not _main.base_ui().visible and now - _homecoming_pending_since_ms > HOME_STUCK_MS:
			_home_stuck_reported = true
			@warning_ignore("integer_division")
			_anomaly("homecoming_stuck", "返航过场结束 %ds 后基地 UI 仍未显示" % (HOME_STUCK_MS / 1000))
	else:
		_homecoming_pending_since_ms = 0
	if _main.base_ui().visible and _base_since > 0 and now - _base_since > BASE_STUCK_MS and not _base_stuck_reported:
		_base_stuck_reported = true
		@warning_ignore("integer_division")
		_anomaly("base_ui_stuck", "基地 UI 可见超过 %ds 未关闭" % (BASE_STUCK_MS / 1000))
	# 狂暴减速残留：玩家仍减速但无狂暴 Boss（Boss 离场/死亡后未复位），持续 15s episode 报一次
	var boss_enraged = _boss != null and is_instance_valid(_boss) and _boss.is_enraged()
	if _player != null and is_instance_valid(_player) and absf(_player.enrage_slow() - 1.0) > 0.001 and not boss_enraged:
		if _slow_since == 0:
			_slow_since = now
		elif not _slow_reported and now - _slow_since > SLOW_STUCK_MS:
			_slow_reported = true
			@warning_ignore("integer_division")
			_anomaly("enrage_slow_stuck", "玩家狂暴减速 %.2f 持续 %ds 但无狂暴 Boss" % [_player.enrage_slow(), SLOW_STUCK_MS / 1000])
	else:
		_slow_since = 0
		_slow_reported = false
	# 事件触发计数：非活跃 -> 活跃跃迁各 +1（500ms 轮询事件状态机）
	var turret_active: bool = _main.event() != null and _main.event().state() != ELITE.GetStateIdle()
	if turret_active and not _event_was_active:
		_turret_event_count += 1
		_log("精英炮塔事件触发（第 %d 次）" % _turret_event_count)
	_event_was_active = turret_active
	var formation_active: bool = _main.formation() != null and _main.formation().state() != FORMATION.GetStateIdle()
	if formation_active and not _formation_was_active:
		_formation_event_count += 1
		_log("轰炸编队事件触发（第 %d 次）" % _formation_event_count)
	_formation_was_active = formation_active
	# B 梯队：DDA 降档卡死——无受击超时仍激活（受击刷新计时，持续受击不算；恢复后复位）
	if GameState.dda_active():
		if _last_damaged_msec >= 0 and now - _last_damaged_msec > DDA_STUCK_MS and not _dda_stuck_reported:
			_dda_stuck_reported = true
			@warning_ignore("integer_division")
			_anomaly("dda_stuck", "DDA 降档激活超 %ds 无受击（未按时恢复）" % (DDA_STUCK_MS / 1000))
	else:
		_dda_stuck_reported = false
	# B 梯队：死亡回放演出节点跟踪——出现即计时，超时未自毁 = 泄漏（3s 播完、5s 兜底）
	if replay_found != null:
		if _replay_node != replay_found:
			_replay_node = replay_found
			_replay_since = now
			_replay_seen_count += 1
			_replay_stuck_reported = false
		elif not _replay_stuck_reported and now - _replay_since > REPLAY_STUCK_MS:
			_replay_stuck_reported = true
			@warning_ignore("integer_division")
			_anomaly("replay_stuck", "死亡回放演出节点存活超 %ds（未自毁）" % (REPLAY_STUCK_MS / 1000))
	elif _replay_node != null:
		_replay_node = null
		_replay_since = 0
	# Phase 0 L13：母舰×事件互斥——母舰在场期精英炮塔/编队事件不得触发
	# （can_trigger 组查询互斥；探针交叉验证事件状态机与母舰在场）
	var ms = _main.mothership()
	if ms != null and is_instance_valid(ms) and (turret_active or formation_active):
		_anomaly_rl("ms_event_mutex", "母舰在场期事件触发（elite=%s formation=%s）" % [turret_active, formation_active], now)
	# UI 状态一致性：结算面板与基地面板同显 / 玩家死亡但游戏未停且无结算面板
	var game_over_ui: CanvasLayer = _main.get_node("GameOverUI")
	if game_over_ui.visible and _main.base_ui().visible:
		_anomaly_rl("ui_overlap", "GameOverUI 与基地 UI 同时可见", now)
	if _player != null and _player.is_dead() and not get_tree().paused and not game_over_ui.visible:
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
		and not _main.is_homecoming()
		and not _main.is_game_over()
	):
		_score_stag_reported = true
		@warning_ignore("integer_division")
		_anomaly("score_stagnant", "分数 %ds 未增长且场上无敌机（疑似不刷怪）" % (SCORE_STAGNANT_MS / 1000))


# ---------------- 收尾 ----------------


func _finish() -> void:
	if _finished:
		return
	_finished = true
	# 收尾局（未死亡）也计入统计
	if _player != null and is_instance_valid(_player) and not _player.is_dead():
		_total_kills += GameState.kills
		_run_scores.append(GameState.score)
	_log("DONE")
	if _main != null and is_instance_valid(_main):
		_dump_node_histogram()
	print("")
	print("[AUTOPLAY] ==================== SUMMARY ====================")
	print("[AUTOPLAY] 真实时长 %.0fs | 对局数 %d | 死亡 %d 次 | seed=%d" % [_elapsed_s(), _run_index, _deaths, _seed])
	print("[AUTOPLAY] 每局得分 %s | 总击杀 %d | Boss 击杀 %d" % [str(_run_scores), _total_kills, _total_boss_kills])
	print(
		(
			"[AUTOPLAY] Buff 选取 %d 次（覆盖种类 %d/%d）| 母舰召唤 %d 次 | 返航 %d 次"
			% [_buff_picks, _buffs_seen.size(), BUFF_POOL_SIZE, _ms_summons, _homecomings]
		)
	)
	print("[AUTOPLAY] 母舰边界：蓄力取消 %d | 提前离舰 %d | 强制弹射 %d" % [_charge_cancels, _early_leaves, _forced_ejects])
	print(
		(
			"[AUTOPLAY] 暂停存档 %d 次 | 暂停开设置页 %d 次 | 继续对局 %d 次 | 退出确认探针 %d 次 | 设置切换 %d 次"
			% [_pause_saves, _settings_opens, _continue_resumes, _exit_probes, _setting_switches]
		)
	)
	print("[AUTOPLAY] 基地：维修 %d | 补给 %d | 路线选择 %d | 任务领奖 %d" % [_base_repairs, _base_recharges, _route_choices, _mission_claims])
	print(
		(
			"[AUTOPLAY] Buff 动效路径选取 %d 次 | Boss P2 %d 次 | 狂暴 %d 次 | 逃跑 %d 次 | 里程碑 %d 次 | 炮塔事件 %d 次 | 编队事件 %d 次"
			% [
				_buff_animated_picks,
				_boss_p2_count,
				_boss_enrage_count,
				_boss_escapes,
				_milestones,
				_turret_event_count,
				_formation_event_count
			]
		)
	)
	print("[AUTOPLAY] B 梯队: DDA 降档触发 %d 次 | 死亡回放演出 %d 次（播完自毁，无泄漏）" % [_dda_trigger_count, _replay_seen_count])
	print(
		(
			"[AUTOPLAY] 峰值: 节点 %d | 敌弹 %d | 玩家弹 %d | 敌机 %d | 孤儿节点 %.0f | 池(b=%d,e=%d) | 帧耗时 %.2fms（基线 %.2fms）"
			% [
				_max_nodes,
				_max_enemy_bullets,
				_max_player_bullets,
				_max_enemies,
				_max_orphans,
				_max_bullet_pool,
				_max_enemy_pool,
				_max_frame_ms,
				_frame_ms_baseline
			]
		)
	)
	var total_anomalies := 0
	for k in _anomaly_counts:
		total_anomalies += int(_anomaly_counts[k])
	print("[AUTOPLAY] 异常总数 %d（%d 类）" % [total_anomalies, _anomaly_counts.size()])
	for k in _anomaly_counts:
		print("[AUTOPLAY]   - %s ×%d | 首例: %s" % [k, int(_anomaly_counts[k]), _anomaly_first[k]])
	if _anomaly_counts.is_empty():
		print("[AUTOPLAY]   （无异常）")
	print("[AUTOPLAY] ===================================================")
	# 清理：释放输入、恢复全局状态、删残留存档、恢复原设置档位
	_release_all_inputs()
	GameState.stop_all_sfx()
	Engine.time_scale = 1.0
	get_tree().paused = false
	GameState.delete_save()
	if GameState.difficulty != _prev_difficulty:
		GameState.set_difficulty(_prev_difficulty)
	if GameState.view_zoom != _prev_view_zoom:
		GameState.set_view_zoom(_prev_view_zoom)
	if GameState.window_size != _prev_window_size:
		GameState.set_window_size(_prev_window_size)
	if GameState.locale != _prev_locale:
		GameState.set_locale(_prev_locale)
	if GameState.aim_assist_level != _prev_aim_assist:
		GameState.set_aim_assist_level(_prev_aim_assist)
	if GameState.reduce_flash != _prev_reduce_flash:
		GameState.set_reduce_flash(_prev_reduce_flash)
	if GameState.ctrl_toggle_mode != _prev_ctrl_toggle:
		GameState.set_ctrl_toggle_mode(_prev_ctrl_toggle)
	if GameState.shift_toggle_mode != _prev_shift_toggle:
		GameState.set_shift_toggle_mode(_prev_shift_toggle)
	load("res://csharp/godot/TestExit.cs").Quit(0)
