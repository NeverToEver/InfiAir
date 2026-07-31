extends Node
## 轰炸编队事件测试（docs/FORMATION_STRIKE_EVENT.md 第 8 节）：
## 场景1 触发门槛：Boss 激活 / 精英炮塔事件 active / 冷却中 / 分数不足 → can_trigger() false。
## 场景2 状态推进：ENTER→TURN→BOMBING_RUN→EXIT→IDLE；战机注册；转向后才有炸弹；
##   投弹数 = 存活机 × bombs_per_craft（击坠机跳过）。
## 场景3 炸弹：预警环存在且随引信收缩；引爆后节点释放；半径内玩家掉血（无敌不掉）。
## 场景4 击坠：致死 → 注销注册表 + 得分；全歼 → 奖励分 + 提前 EXIT。
## 场景5 打断：abort() → 实体清理、回 IDLE、冷却生效。
## 场景6 无 Timer/节点残留；清理 user:// 持久化。

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
func _wait_event_state(event: FormationStrikeEvent, p_state: int, timeout: float = 8.0) -> bool:
	var left := timeout
	while left > 0.0:
		if event._state == p_state:
			return true
		await _wait_real(0.05)
		left -= 0.05
	return event._state == p_state


func _count_crafts() -> int:
	var n := 0
	for child in get_node("Main").get_children():
		if child is FormationCraft:
			n += 1
	return n


func _count_bombs() -> int:
	var n := 0
	for child in get_node("Main").get_children():
		if child is FormationBomb:
			n += 1
	return n


func _count_registered_crafts() -> int:
	var n := 0
	for node in GameState.enemies:
		if node is FormationCraft:
			n += 1
	return n


## 启动一次压缩时长的事件（实例 var 覆盖，不动 balance.json）
func _start_fast_event(event: FormationStrikeEvent) -> void:
	event.APPROACH_SPEED = 2000.0  # 进场 ~0.2s
	event.TURN_TIME = 0.3
	event.RUN_SPEED = 400.0
	event.BOMB_INTERVAL = 0.2
	event.BOMB_FUSE = 3.0  # 炸弹存续到场景结束统一清理
	event._cooldown_left = 0.0
	event.start()


func _ready() -> void:
	# 清理持久化状态，保证测试确定性
	GameState.delete_save()
	GameState.high_score = 0
	GameState.save_profile()
	GameState.set_difficulty(&"medium")
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	var welcome: CanvasLayer = get_node("Main/WelcomeScreen")
	if welcome.visible:
		welcome.dismiss()
	var start_panel: CanvasLayer = get_node("Main/StartPanel")
	if start_panel.visible:
		start_panel._on_new_game_pressed()
	var player: Player = get_node("Main/Player")
	player.set_auto_fire(false  )# 禁用自动开火，击杀全部走断言路径
	player.set_invincible(999.0)
	player.position = Vector2(960.0, 800.0)
	await get_tree().process_frame
	await get_tree().process_frame
	var main := get_node("Main")
	var spawner: Node = get_node("Main/Spawner")
	var event: FormationStrikeEvent = main.formation()
	_check(event != null, "初始化：事件编排节点已登记到 main")
	_check(spawner.formation_event() == event, "初始化：spawner 持有事件引用（优先级链钩子）")
	spawner.set_process(false)  # 全程手动驱动，保证确定性
	GameState.score = 0
	GameState._set_milestone_override(999999)  # 防止得分跨越里程碑弹 Buff 三选一暂停树

	# ================= 场景 1：触发门槛 =================
	GameState.score = 1000
	event._cooldown_left = 0.0
	_check(event.can_trigger(), "场景1：常态（分数达标/无 Boss/无精英事件/非冷却）可触发")
	spawner.set_boss_active(true)
	_check(not event.can_trigger(), "场景1：Boss 激活时不可触发")
	spawner.set_boss_active(false)
	var fake_event := EliteTurretEvent.new()  # 不入树，仅置状态模拟精英事件 active
	fake_event._state = EliteTurretEvent.State.CARRIER_ENTER
	var real_event: EliteTurretEvent = spawner.elite_event()
	spawner.set_elite_event(fake_event)
	_check(not event.can_trigger(), "场景1：精英炮塔事件 active 时不可触发")
	spawner.set_elite_event(real_event)
	fake_event.free()
	event._cooldown_left = 5.0
	_check(not event.can_trigger(), "场景1：冷却中不可触发")
	event._cooldown_left = 0.0
	GameState.score = event.MIN_SCORE - 1
	_check(not event.can_trigger(), "场景1：分数不足不可触发")
	GameState.score = 1000

	# ================= 场景 2：状态推进 + 投弹计数（击坠机跳过） =================
	_start_fast_event(event)
	_check(event._state == FormationStrikeEvent.State.FORMATION_ENTER, "场景2：启动进入 FORMATION_ENTER")
	_check(spawner.waves_paused(), "场景2：事件启动即暂停普通波次（占用波次槽）")
	_check(event._crafts.size() == 4, "场景2：中难度 4 架编队")
	_check(_count_registered_crafts() == 4, "场景2：战机注册 GameState.enemies")
	if event._crafts.size() > 0:
		_check(event._crafts[0].max_hp == 60, "场景2：单机血量 60（60×中难度×1.0）")
	_check(await _wait_event_state(event, FormationStrikeEvent.State.FORMATION_TURN), "场景2：靠近后进入 FORMATION_TURN")
	# 转航向期间击落 4 号僚机（投弹前）：其投弹序列应被跳过
	var wingman: FormationCraft = event._crafts[3]
	var score0 := GameState.score
	wingman.take_damage(9999)
	await get_tree().process_frame
	_check(GameState.score - score0 == 400, "场景2：击坠得分 200×中难度倍率×2 = 400")
	_check(event._alive == 3, "场景2：剩余 3 架")
	_check(_count_bombs() == 0, "场景2：转向完成前无炸弹生成")
	_check(await _wait_event_state(event, FormationStrikeEvent.State.BOMBING_RUN), "场景2：转向后进入 BOMBING_RUN")
	_check(await _wait_event_state(event, FormationStrikeEvent.State.FORMATION_EXIT, 6.0), "场景2：投弹完毕进入 FORMATION_EXIT")
	_check(event._dropped == 6, "场景2：投弹数 = 存活 3 机 × 2 枚 = 6（击坠机跳过）")
	_check(_count_bombs() > 0, "场景2：引信未到时炸弹节点存续")
	_check(await _wait_event_state(event, FormationStrikeEvent.State.IDLE, 5.0), "场景2：离场结束回 IDLE")
	_check(not spawner.waves_paused(), "场景2：事件结束恢复普通波次")
	_check(event._cooldown_left > 0.0, "场景2：事件结束进入触发冷却")
	_check(_count_crafts() == 0, "场景2：离场后战机节点清理")
	_check(_count_registered_crafts() == 0, "场景2：离场后注册表无残留")
	# 统一清理本场景遗留炸弹（引爆音/粒子自然播完）
	for child in main.get_children():
		if child is FormationBomb:
			child.queue_free()
	await get_tree().process_frame

	# ================= 场景 3：炸弹引信/预警环/玩家伤害 =================
	player.position = Vector2(960.0, 800.0)
	player.set_invincible(0.0)
	GameState.health = 100.0
	var bomb := FormationBomb.new()
	bomb.setup(Vector2.ZERO, 0.5, 20, 120.0)
	bomb.position = player.position
	main.add_child(bomb)
	# add_child 同步触发 _ready（警示环半径已置位），此处先于首个 _process 帧检查初值
	_check(bomb._ring != null and bomb._ring.visible, "场景3：炸弹带警示环")
	var ring_r0: float = bomb._ring.scale.x
	_check(is_equal_approx(ring_r0, 108.0), "场景3：警示环初始半径 0.9×AoE = 108")
	await get_tree().process_frame
	await _wait_real(0.25)
	_check(is_instance_valid(bomb) and bomb._ring.scale.x < ring_r0, "场景3：警示环随引信收缩")
	var hp0 := GameState.health
	await _wait_real(0.4)  # 越过引信 0.5s 引爆
	_check(not is_instance_valid(bomb), "场景3：引爆后炸弹节点释放")
	_check(GameState.health < hp0, "场景3：玩家站半径内引爆掉血")
	# 无敌不掉血（血量只可能因被动回血上升，不允许下降）
	player.set_invincible(999.0)
	var bomb2 := FormationBomb.new()
	bomb2.setup(Vector2.ZERO, 0.3, 20, 120.0)
	bomb2.position = player.position
	main.add_child(bomb2)
	var hp1 := GameState.health
	await _wait_real(0.5)
	_check(not is_instance_valid(bomb2), "场景3：第二枚炸弹已引爆释放")
	_check(GameState.health >= hp1, "场景3：玩家无敌时引爆不掉血")

	# ================= 场景 4：全歼奖励 + 提前离场 =================
	_start_fast_event(event)
	await get_tree().process_frame
	_check(event._state == FormationStrikeEvent.State.FORMATION_ENTER, "场景4：事件再次启动")
	var score1 := GameState.score
	for i in event._crafts.size():
		var craft: FormationCraft = event._crafts[i]
		if craft != null and is_instance_valid(craft):
			craft.take_damage(9999)
	await get_tree().process_frame
	# 4 机击坠 200×4 + 全歼 200 = 1000 基础分 ×中难度×2 = 2000
	_check(GameState.score - score1 == 2000, "场景4：击坠分 + 全歼奖励入账（2000）")
	_check(event._state == FormationStrikeEvent.State.FORMATION_EXIT, "场景4：全歼立即提前离场")
	_check(_count_registered_crafts() == 0, "场景4：全歼后注册表无残留")
	_check(await _wait_event_state(event, FormationStrikeEvent.State.IDLE, 5.0), "场景4：提前离场后回 IDLE")

	# ================= 场景 5：abort 打断 =================
	_start_fast_event(event)
	await get_tree().process_frame
	_check(event.is_active(), "场景5：事件进行中")
	event.abort()
	await get_tree().process_frame  # queue_free 帧末生效后再断言清理
	_check(event._state == FormationStrikeEvent.State.IDLE, "场景5：abort 回 IDLE")
	_check(not spawner.waves_paused(), "场景5：abort 恢复普通波次")
	_check(_count_crafts() == 0, "场景5：abort 清理全部战机实体")
	_check(_count_registered_crafts() == 0, "场景5：abort 后注册表无残留")
	_check(event._cooldown_left > 0.0, "场景5：abort 后冷却照计")
	_check(not event.can_trigger(), "场景5：冷却期内不可再次触发")

	# ================= 场景 6：无残留 =================
	await _wait_real(0.5)
	_check(event.get_child_count() == 1, "场景6：事件节点无 Timer 残留（仅通讯浮层）")
	_check(_count_crafts() == 0 and _count_bombs() == 0, "场景6：Main 下无编队实体残留")
	_check(is_equal_approx(Engine.time_scale, 1.0), "收尾：time_scale = 1.0")
	print("FORMATION STRIKE EVENT TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
