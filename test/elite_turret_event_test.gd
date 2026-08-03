extends Node
## 精英炮塔事件测试（docs/ELITE_TURRET_EVENT.md）：
## 场景1 成功流：航母入场 → 炮塔升起 → 30s 倒计时 → 弱锁定开火 → 三节点台词 →
##   全歼 +500 基础分（中难度 ×2）→ 受创撤离 → Boss 解冻。
## 场景2 互斥：事件期间 Boss 触发被冻结（_boss_pending 记一次不累积），
##   BOSS_DELAY 结束补触发一次且仅一次。
## 场景3 失败流：倒计时归零仍有炮台存活 → 撤退台词 + 无奖励 + 炮塔收回 + 回 IDLE 进冷却。
## 场景4 返航中止：TURRET_ACTIVE 中 _start_homecoming → abort 清炮塔/隐藏事件条/恢复波次/
##   航母完整撤离 → 继续出击注册表清场 → BOSS_DELAY 后回 IDLE 且 Boss 解冻。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## 真实时间等待（不受 time_scale 影响）
func _wait_real(sec: float) -> void:
	await get_tree().create_timer(sec, true, false, true).timeout


## 轮询等事件进入目标状态（最多 timeout 秒真实时间）
func _wait_event_state(event: EliteTurretEvent, p_state: int, timeout: float = 8.0) -> bool:
	var left := timeout
	while left > 0.0:
		if event.state() == p_state:
			return true
		await _wait_real(0.1)
		left -= 0.1
	return event.state() == p_state


func _count_bosses() -> int:
	var n := 0
	for child in get_node("Main").get_children():
		if child is Boss:
			n += 1
	return n


## 启动一次压缩时长的事件（实例 var 覆盖，不动 balance.json）
func _start_fast_event(event: EliteTurretEvent) -> void:
	event.ENTER_TIME = 0.2
	event.RISE_TIME = 0.2
	event.BOSS_RESUME_DELAY = 0.3
	event.FIRE_INTERVAL = Vector2(0.3, 0.4)
	event.set_cooldown_left(0.0)
	event.start()


## 击毁 n 座仍存活的炮台（返回实际击毁数）
func _kill_turrets(event: EliteTurretEvent, n: int) -> int:
	var killed := 0
	for turret in event.turrets().duplicate():
		if killed >= n:
			break
		if is_instance_valid(turret):
			turret.take_damage(9999)
			killed += 1
	return killed


func _ready() -> void:
	# 清理持久化状态，保证测试确定性
	GameState.delete_save()
	# L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
	var orig_high_score: int = GameState.high_score
	GameState.high_score = 0
	GameState.save_profile()
	GameState.set_difficulty(&"medium")
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	var start_panel: CanvasLayer = get_node("Main/StartPanel")
	if start_panel.visible:
		start_panel.press_new_game()
	var player: Player = get_node("Main/Player")
	player.set_auto_fire(false)  # 禁用自动开火，炮台击杀全部走断言路径
	player.set_invincible(999.0)
	player.position = Vector2(960.0, 800.0)
	await get_tree().process_frame
	await get_tree().process_frame
	var spawner: Node = get_node("Main/Spawner")
	var hud: CanvasLayer = get_node("Main/HUD")
	var event: EliteTurretEvent = get_node("Main").event()
	_check(event != null, "初始化：事件编排节点已登记到 main")
	_check(spawner.elite_event() == event, "初始化：spawner 持有事件引用（互斥钩子）")
	spawner.set_process(false)  # 场景1 手动驱动，保证确定性
	GameState.score = 0

	# ================= 场景 1：成功流（全歼炮台） =================
	_start_fast_event(event)
	_check(event.state() == EliteTurretEvent.State.CARRIER_ENTER, "场景1：启动进入 CARRIER_ENTER")
	_check(spawner.boss_frozen(), "场景1：事件启动即冻结 Boss 调度")
	_check(spawner.waves_paused(), "场景1：事件启动即暂停普通波次")
	_check(event.lines().size() == 3, "场景1：无放回抽取 3 句绑定台词")
	var seen_lines: Array = []
	var dup_ok := true
	for key in event.lines():
		if key in seen_lines:
			dup_ok = false
		seen_lines.append(key)
	_check(dup_ok, "场景1：3 句台词不重复")
	# 等升起完成进入 30s 倒计时
	_check(await _wait_event_state(event, EliteTurretEvent.State.TURRET_ACTIVE), "场景1：入场+升起后进入 TURRET_ACTIVE")
	await get_tree().process_frame
	_check(event.turrets().size() == 4, "场景1：中难度 4 座炮台")
	_check(event.total() == 4, "场景1：炮台总数记录为 4")
	if event.turrets().size() > 0:
		var t0: TurretBattery = event.turrets()[0]
		_check(t0.max_hp == 80, "场景1：单台血量 80（80×中难度×1.0）")
		_check(t0.monitoring, "场景1：充能完毕后炮台可被攻击")
	_check(hud.event_box().visible, "场景1：HUD 事件计时条显示")
	# 弱锁定开火：等几轮射击，验证弹药与追踪参数
	var homing_ok := false
	var fired_ok := false
	for i in 40:  # 最多 ~4s 真实时间
		await _wait_real(0.1)
		for child in get_node("Main").get_children():
			if child is Bullet and not child.is_player_bullet:
				fired_ok = true
				if child.has_meta("bullet_type") and child.get_meta("bullet_type") == &"homing":
					if is_equal_approx(child.homing_turn_rate, 1.5) and is_equal_approx(child.homing_time, 0.6):
						homing_ok = true
		if fired_ok and homing_ok:
			break
	_check(fired_ok, "场景1：炮台按独立节奏开火")
	_check(homing_ok, "场景1：弱追踪弹转向速率 1.5 / 时限 0.6s")
	# 台词节点：⌈4/3⌉=2 → 第 1 句；⌈4×2/3⌉=3 → 第 2 句
	_kill_turrets(event, 1)
	await get_tree().process_frame
	_check(event.line_stage() == 0, "场景1：摧毁 1 座未达 ⌈4/3⌉=2，台词未播")
	_kill_turrets(event, 1)
	await get_tree().process_frame
	_check(event.line_stage() == 1, "场景1：摧毁 2 座（≥⌈总数/3⌉）播第 1 句")
	_check(event.comm().full_text() == tr(event.lines()[0]), "场景1：第 1 句为绑定台词第 1 条")
	_kill_turrets(event, 1)
	await get_tree().process_frame
	_check(event.line_stage() == 2, "场景1：摧毁 3 座（≥⌈总数×2/3⌉）播第 2 句")
	# 全歼 → 成功结算
	var score0 := GameState.score
	_kill_turrets(event, 1)
	await get_tree().process_frame
	_check(event.state() == EliteTurretEvent.State.CARRIER_EXIT, "场景1：全歼进入 CARRIER_EXIT")
	_check(GameState.score - score0 == 1000, "场景1：奖励 500×中难度倍率×2 = 1000 入账")
	_check(event.comm().full_text() == tr(event.lines()[2]), "场景1：全歼播第 3 句绑定台词")
	_check(not hud.event_box().visible, "场景1：结算后事件计时条隐藏")
	_check(not spawner.waves_paused(), "场景1：CARRIER_EXIT 起普通波次恢复")
	_check(event.turrets().is_empty(), "场景1：炮台清单已清空")
	# 航母受创撤离 → BOSS_DELAY → IDLE，Boss 解冻
	_check(await _wait_event_state(event, EliteTurretEvent.State.BOSS_DELAY, 10.0), "场景1：航母离场进入 BOSS_DELAY")
	_check(await _wait_event_state(event, EliteTurretEvent.State.IDLE, 5.0), "场景1：BOSS_DELAY 结束回 IDLE")
	_check(not spawner.boss_frozen(), "场景1：事件结束后 Boss 解冻")
	_check(event.cooldown_left() > 0.0, "场景1：事件结束进入触发冷却")

	# ================= 场景 2：Boss 冻结/恢复（单次不累积） =================
	spawner.set_boss_pending(false)
	spawner.set_next_boss_score(GameState.score)  # Boss 分数步进立即到期
	spawner.set_boss_timer(spawner.BOSS_MIN_INTERVAL)  # 越过最小间隔时间门（分数触发需同时满足）
	spawner.set_process(true)  # 恢复 spawner 主循环：pending 标记由它记录
	_start_fast_event(event)
	await _wait_real(0.5)
	_check(spawner.boss_pending(), "场景2：事件期间 Boss 到期只记 pending")
	_check(_count_bosses() == 0, "场景2：事件期间 Boss 未触发（冻结）")
	await _wait_real(1.0)  # 多帧重复到期：覆盖为同一标记，不累积
	_check(spawner.boss_pending(), "场景2：重复到期仍只有同一 pending 标记")
	_check(_count_bosses() == 0, "场景2：重复到期仍无 Boss")
	# 快速全歼结束事件
	await _wait_event_state(event, EliteTurretEvent.State.TURRET_ACTIVE)
	_kill_turrets(event, 99)
	await get_tree().process_frame
	_check(await _wait_event_state(event, EliteTurretEvent.State.IDLE, 12.0), "场景2：事件结束回 IDLE")
	# BOSS_DELAY 结束 → 立即补触发 Boss 一次（boss_warning 后 2s 降入）
	var boss_spawned := false
	for i in 40:  # 最多 ~4s
		await _wait_real(0.1)
		if _count_bosses() > 0:
			boss_spawned = true
			break
	_check(boss_spawned, "场景2：冻结的 Boss 在 BOSS_DELAY 后补触发")
	_check(not spawner.boss_pending(), "场景2：补触发后 pending 标记清除")
	await _wait_real(0.5)
	_check(_count_bosses() == 1, "场景2：Boss 仅补触发一次（不累积）")
	# 清理：直接释放 Boss，避免击杀奖励干扰后续断言
	for child in get_node("Main").get_children():
		if child is Boss:
			child.queue_free()
	spawner.set_boss_active(false)
	spawner.set_process(false)
	await get_tree().process_frame

	# ================= 场景 3：失败流（超时撤退） =================
	event.set_cooldown_left(0.0)
	event.DURATION = 0.8
	event.start()
	var rp1 := GameState.rp
	_check(await _wait_event_state(event, EliteTurretEvent.State.TURRET_ACTIVE), "场景3：事件进入倒计时")
	_check(await _wait_event_state(event, EliteTurretEvent.State.CARRIER_EXIT, 5.0), "场景3：倒计时归零进入 CARRIER_EXIT")
	_check(event.comm().full_text() == tr("ETQ_RETREAT"), "场景3：失败播放固定撤退台词")
	# 失败无奖励入账：RP 为事件奖励载体（炮台未击杀无击杀分）。
	# 注：玩家在弹幕中会自然擦弹得分（2026-08-03 机制二设计行为），score 不再恒等，
	# 故断言改为奖励载体 RP 不变
	_check(GameState.rp == rp1, "场景3：失败无奖励入账")
	var turrets_gone := true
	for turret in event.turrets():
		if is_instance_valid(turret) and not turret.ceased():
			turrets_gone = false
	_check(turrets_gone, "场景3：存活炮台停火收回盖板")
	_check(await _wait_event_state(event, EliteTurretEvent.State.IDLE, 12.0), "场景3：航母完整撤离后回 IDLE")
	_check(not event.can_trigger(), "场景3：冷却期内不可再次触发")
	# L13：母舰在场期事件不触发（组查询互斥）
	event.set_cooldown_left(0.0)
	var ms_probe := Node.new()
	add_child(ms_probe)
	ms_probe.add_to_group("mothership")
	_check(not event.can_trigger(), "场景3：母舰在场时不可触发")
	ms_probe.remove_from_group("mothership")
	ms_probe.queue_free()
	_check(event.can_trigger(), "场景3：母舰离场恢复可触发")

	# ================= 场景 4：返航中止（abort） =================
	var main := get_node("Main")
	event.set_cooldown_left(0.0)
	event.DURATION = 30.0  # 恢复倒计时（场景3 改过）
	_start_fast_event(event)
	_check(await _wait_event_state(event, EliteTurretEvent.State.TURRET_ACTIVE), "场景4：事件进入倒计时")
	_check(hud.event_box().visible, "场景4：中止前 HUD 事件条显示")
	# 返航触发：elite 事件应被 abort（清炮塔、隐藏事件条、恢复波次、航母完整撤离）
	main.start_homecoming()
	await get_tree().process_frame
	_check(event.state() == EliteTurretEvent.State.CARRIER_EXIT, "场景4：返航中止事件进入 CARRIER_EXIT")
	_check(event.turrets().is_empty(), "场景4：在场炮塔清单已清")
	_check(not hud.event_box().visible, "场景4：中止后 HUD 事件条隐藏")
	_check(not spawner.waves_paused(), "场景4：普通波次恢复")
	await get_tree().process_frame
	var turret_nodes_left := 0
	for child in main.get_children():
		if child is TurretBattery:
			turret_nodes_left += 1
	_check(turret_nodes_left == 0, "场景4：炮塔节点已释放（不走 died 计分）")
	# 过场越过 1.2s 输入宽限后跳过 → 基地 → 继续出击
	await _wait_real(1.4)
	main.skip_return()
	await get_tree().process_frame
	await get_tree().process_frame
	_check(main.base_ui().visible, "场景4：过场结束进入基地界面")
	main.base_ui().resume()
	await get_tree().process_frame
	# 轨道打击动画（orbital_strike_test 专测本体）：缩短时轴，等待命中清场并播完
	if main.strike() != null:
		main.strike().DURATION = 0.5
	var t_strike := 0.0
	while main.strike() != null and t_strike < 3.0:
		await get_tree().create_timer(0.1).timeout
		t_strike += 0.1
	# 注册表驱动清场：非 Boss 实体（含事件/波次残留）全清
	var registry_left := false
	for e in GameState.enemies:
		if is_instance_valid(e) and not (e is Boss):
			registry_left = true
	_check(not registry_left, "场景4：继续出击后注册表非 Boss 实体清空")
	# 航母完整撤离 → BOSS_DELAY → IDLE，Boss 解冻（沿用 _on_boss_delay_end）
	_check(await _wait_event_state(event, EliteTurretEvent.State.IDLE, 15.0), "场景4：航母撤离后回 IDLE")
	_check(not spawner.boss_frozen(), "场景4：Boss 冻结解除")
	_check(not spawner.boss_pending(), "场景4：无遗留 pending 标记")

	_check(is_equal_approx(Engine.time_scale, 1.0), "收尾：time_scale = 1.0")
	await _wait_real(1.5)  # 让撤退 tween/爆炸粒子播完，避免退出时对象泄漏
	# L15：还原用户最高分并落盘（收尾不污染用户 profile）
	GameState.high_score = orig_high_score
	GameState.save_profile()
	print("ELITE TURRET EVENT TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
