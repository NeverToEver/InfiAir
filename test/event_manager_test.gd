extends Node
## 统一事件管理器集成测试（docs/EVENT_MANAGER.md）：
## 场景1 注册表：遭遇事件经 main._ready 注册进统一注册表，6 事件齐全；
##    main.event()/main.formation() 与管理器注册表返回同一实例。
## 场景2 遭遇强制触发 + 统一信号：force_trigger 启动编队事件 → event_started 广播、
##    状态推进；遭遇组单并发（编队进行中 force_trigger 精英被拒）。
## 场景3 遭遇终止：end_active(GROUP_ENCOUNTER) → abort + event_ended 广播。
## 场景4 spawner 门控：spawner.set_process(false) 后遭遇自动触发被禁用
##    （计时不推进；手动 start 不受影响）。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _ready() -> void:
	GameState.delete_save()
	GameState.set_difficulty(&"medium")
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	GameState.login_guest()
	add_child(main_scene.instantiate())
	var player = get_node("Main/Player")
	player.set_auto_fire(false)
	player.set_invincible(999.0)
	player.position = Vector2(960.0, 800.0)
	GameState.set_milestone_override(999999)  # 防止得分跨越里程碑弹 Buff 三选一暂停树
	await get_tree().process_frame
	await get_tree().process_frame
	var spawner: Node = get_node("Main/Spawner")
	spawner.set_process(false)  # 场景 1-3 手动驱动，保证确定性
	var manager: GameEventManager = GameState.events
	var main := get_node("Main")

	# ================= 场景 1：注册表与实例同一性 =================
	var ids := manager.event_ids()
	_check(ids.has(&"elite_turret"), "场景1：精英炮塔事件已注册进统一注册表")
	_check(ids.has(&"formation_strike"), "场景1：轰炸编队事件已注册进统一注册表")
	_check(ids.has(&"fake_enemies") and ids.has(&"mental_confusion"), "场景1：迷雾事件保留在注册表")
	_check(ids.size() == 6, "场景1：统一注册表共 6 事件（迷雾 4 + 遭遇 2）")
	_check(manager.event(&"elite_turret") == main.event(), "场景1：main.event() 与注册表同实例")
	_check(manager.event(&"formation_strike") == main.formation(), "场景1：main.formation() 与注册表同实例")

	# ================= 场景 2：遭遇强制触发 + 统一信号 + 组内单并发 =================
	var started_ids: Array[StringName] = []
	var ended_ids: Array[StringName] = []
	manager.event_started.connect(func(id: StringName, _d: float) -> void: started_ids.append(id))
	manager.event_ended.connect(func(id: StringName) -> void: ended_ids.append(id))
	var formation: FormationStrikeEvent = main.formation()
	formation.MIN_SCORE = 0  # 压缩门槛（不动 balance.json）
	var ok := manager.force_trigger(&"formation_strike")
	_check(ok, "场景2：force_trigger 启动编队事件")
	_check(manager.active_id(GameEventManager.GROUP_ENCOUNTER) == &"formation_strike", "场景2：遭遇组 active_id 正确")
	_check(started_ids.has(&"formation_strike"), "场景2：event_started 已广播编队事件")
	_check(formation.state() != FormationStrikeEvent.State.IDLE, "场景2：编队 FSM 已推进")
	_check(not manager.force_trigger(&"elite_turret"), "场景2：遭遇组单并发——编队进行中拒触发精英")
	# 等待 FSM 自行结束（FORMATION_ENTER 后转 TURN 需 ~0.2s，投弹/离场 ~数秒）
	var ended := false
	for i in 120:  # 最多 ~12s 真实时间
		await get_tree().create_timer(0.1).timeout
		if formation.state() == FormationStrikeEvent.State.IDLE:
			ended = true
			break
	_check(ended, "场景2：编队 FSM 自然结束回 IDLE")
	await get_tree().process_frame
	_check(ended_ids.has(&"formation_strike"), "场景2：FSM 结束 → event_ended 已广播")
	_check(manager.active_id(GameEventManager.GROUP_ENCOUNTER) == &"", "场景2：遭遇组 active_id 复位")
	# 清理编队遗留炸弹
	for child in main.get_children():
		if child is FormationBomb:
			child.queue_free()
	await get_tree().process_frame

	# ================= 场景 3：end_active 终止 =================
	manager.force_trigger(&"formation_strike")
	_check(formation.state() != FormationStrikeEvent.State.IDLE, "场景3：事件再次启动")
	manager.end_active(GameEventManager.GROUP_ENCOUNTER)
	await get_tree().process_frame
	_check(formation.state() == FormationStrikeEvent.State.IDLE, "场景3：end_active → abort 回 IDLE")
	_check(ended_ids.has(&"formation_strike"), "场景3：终止广播 event_ended")
	_check(not spawner.waves_paused(), "场景3：终止后普通波次恢复")
	for child in main.get_children():
		if child is FormationBomb:
			child.queue_free()
	await get_tree().process_frame

	# ================= 场景 4：spawner 处理门控 =================
	# spawner.set_process(false) 下遭遇自动触发禁用；恢复处理后才生效（镜像原 spawner 语义）
	GameState.score = 10000  # 越过编队 min_score
	formation.set_cooldown_left(0.0)
	manager.ENCOUNTER_CONFIG[&"formation_strike"]["chance"] = 1.0  # 固定掷签（测试确定性）
	spawner.set_process(false)
	manager.set_encounter_timer_remaining(&"formation_strike", 0.05)
	await get_tree().create_timer(0.2).timeout
	_check(formation.state() == FormationStrikeEvent.State.IDLE, "场景4：spawner 停处理时遭遇自动触发被禁用")
	spawner.set_process(true)
	manager.set_encounter_timer_remaining(&"formation_strike", 0.05)
	var auto_triggered := false
	for i in 30:  # 最多 ~3s
		await get_tree().create_timer(0.1).timeout
		if formation.state() != FormationStrikeEvent.State.IDLE:
			auto_triggered = true
			break
	_check(auto_triggered, "场景4：spawner 恢复处理后遭遇自动触发生效")
	manager.end_active(GameEventManager.GROUP_ENCOUNTER)
	await get_tree().process_frame
	spawner.set_process(false)
	for child in main.get_children():
		if child is FormationBomb:
			child.queue_free()
	await get_tree().process_frame

	# ================= 场景 5：fog 组经统一管理器 + 跨组并发 =================
	# fog 组生命周期由统一管理器接管；fog 信号经门面重发（player 消费面不变）
	GameState.fog_events.set_run_active(true)
	var fog_seen: Array[StringName] = []
	GameState.fog_events.fog_event_started.connect(func(id: StringName, _d: float) -> void: fog_seen.append(id))
	ok = manager.force_trigger(&"fake_enemies")
	_check(ok, "场景5：统一管理器可启动迷雾事件")
	_check(manager.active_id(GameEventManager.GROUP_FOG) == &"fake_enemies", "场景5：fog 组 active_id 正确")
	_check(fog_seen.has(&"fake_enemies"), "场景5：fog_event_started 经门面重发")
	_check(GameState.fog_events.spawned_fakes().size() > 0, "场景5：门面 spawned_fakes 委托生效")
	# 跨组并发：fog 进行中遭遇仍可并行启动（保持现状行为）
	ok = manager.force_trigger(&"formation_strike")
	_check(ok, "场景5：fog 进行中遭遇组仍可并行启动")
	_check(manager.active_id(GameEventManager.GROUP_ENCOUNTER) == &"formation_strike", "场景5：遭遇组 active")
	_check(manager.active_id(GameEventManager.GROUP_FOG) == &"fake_enemies", "场景5：fog 组保持 active（并行）")
	# end_all 双组复位
	manager.end_all()
	await get_tree().process_frame
	_check(manager.active_id(GameEventManager.GROUP_FOG) == &"", "场景5：end_all fog 组复位")
	_check(manager.active_id(GameEventManager.GROUP_ENCOUNTER) == &"", "场景5：end_all 遭遇组复位")
	_check(formation.state() == FormationStrikeEvent.State.IDLE, "场景5：end_all 遭遇回 IDLE")
	for child in main.get_children():
		if child is FormationBomb:
			child.queue_free()
	await get_tree().process_frame

	# ================= 场景 6：Q07/Q10/Q12/Q13（2026-08-05 全量修复批次） =================
	# Q13：end_active 打断后 event_ended 恒只发一次（原实现 abort 处 + 轮询双发）。
	# 注：lambda 捕获 int 是值拷贝，计数用数组（引用类型）承载
	var ended_count: Array[int] = [0]
	manager.event_ended.connect(
		func(id: StringName) -> void:
			if id == &"formation_strike":
				ended_count[0] += 1
	)
	# Q07：enabled=false 时 _process 自动触发路径惰性（掷签前短路，原实现照常触发）
	manager.FOG_ENABLED = false
	manager.FOG_TRIGGER_CHANCE = 1.0  # 固定掷签（对照组用）
	manager.set_cooldown_left(0.0)
	manager.set_first_delay_left(0.0)
	manager.set_check_timer_left(0.0)
	manager.set_run_active(false)  # 先退出再激活 → 触发 Q10/Q12 重置路径
	manager.set_run_active(true)
	_check(
		is_equal_approx(manager.first_delay_left(), manager.FOG_FIRST_DELAY),
		"Q12：激活对局重置 fog first_delay = %.0f（实测 %.1f，原实现每进程一次、第二局开局即触发）" % [manager.FOG_FIRST_DELAY, manager.first_delay_left()]
	)
	_check(
		manager.encounter_timer_remaining(&"formation_strike") >= 39.0,
		"Q10：激活对局重置遭遇计时回 interval 40（实测 %.1f，原实现继承上局 ≤0 即触发）" % manager.encounter_timer_remaining(&"formation_strike")
	)
	await get_tree().create_timer(0.3).timeout  # 覆盖 ≥1 个检查周期（check_timer 已压零）
	_check(manager.active_id(GameEventManager.GROUP_FOG) == &"", "Q07：enabled=false 时自动触发不启动")
	# Q07 对照组：enabled=true 且条件就绪 → 自动触发正常启动
	manager.FOG_ENABLED = true
	manager.set_cooldown_left(0.0)
	manager.set_first_delay_left(0.0)
	manager.set_check_timer_left(0.0)
	var fog_auto := false
	for i in 10:
		await get_tree().create_timer(0.1).timeout
		if manager.active_id(GameEventManager.GROUP_FOG) != &"":
			fog_auto = true
			break
	_check(fog_auto, "Q07：enabled=true 且条件就绪时自动触发正常启动（对照组）")
	manager.end_all()
	await get_tree().process_frame
	# Q13：打断后信号恰好 1 次且 FSM 回 IDLE
	manager.force_trigger(&"formation_strike")
	manager.end_active(GameEventManager.GROUP_ENCOUNTER)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(ended_count[0] == 1, "Q13：end_active 打断后 event_ended 恰好 1 次（实测 %d，原实现双发）" % ended_count[0])
	_check(formation.state() == FormationStrikeEvent.State.IDLE, "Q13：打断后 FSM 回 IDLE")
	for child in main.get_children():
		if child is FormationBomb:
			child.queue_free()
	await get_tree().process_frame

	print("EVENT MANAGER TEST DONE, failures = ", _failures)
	# P4（2026-08-05）：还原直写配置（测试不污染内存配置表；A7 同族）
	manager.FOG_ENABLED = true
	manager.FOG_TRIGGER_CHANCE = 0.35
	manager.ENCOUNTER_CONFIG[&"formation_strike"]["chance"] = 0.30
	GameState.delete_save()
	get_tree().quit(_failures)
