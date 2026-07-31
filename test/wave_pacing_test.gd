extends Node
## 波次化刷怪与悬停机动测试：普通波成组均布入场、敌机锚点悬停、精英特殊槽节奏
## （3~4 普通波一个精英波）、Boss 激活暂停普通波次、精英/Boss 击杀追加休整波次。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _enemies() -> Array[Enemy]:
	var out: Array[Enemy] = []
	for child in get_node("Main").get_children():
		if child is Enemy:
			out.append(child)
	return out


func _ready() -> void:
	# 清理持久化状态，保证测试确定性
	GameState.delete_save()
	GameState.high_score = 0
	GameState.save_profile()
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	var main := get_node("Main")
	# 开场欢迎页（首屏）暂停游戏，先关闭进入开始面板
	var welcome: CanvasLayer = get_node("Main/WelcomeScreen")
	if welcome.visible:
		welcome.dismiss()
	# 开场面板自显即暂停（冻结背景），先关闭解除
	var start_panel: CanvasLayer = get_node("Main/StartPanel")
	if start_panel.visible:
		start_panel._on_new_game_pressed()
	var player: Player = get_node("Main/Player")
	player.set_auto_fire(false  )# 禁用自动开火，避免误伤与意外得分里程碑
	await get_tree().process_frame
	await get_tree().process_frame
	var spawner: Node = get_node("Main/Spawner")
	spawner.set_process(false)  # 停掉自动刷怪/Boss 调度，保证确定性
	# 隔离 Boss 触发链（分数/时间门），本测试只关心波次节奏
	spawner.set_next_boss_score(999999999)
	spawner.BOSS_TIME_LIMIT = 999999.0

	# 1. 普通波成组刷出，x 落在各自均分槽位内，锚点在悬停带内
	var view := GameState.view_world_rect()
	var n: int = spawner.wave_size()
	spawner.spawn_normal_wave()
	await get_tree().create_timer(1.0).timeout  # 0.6s 预告后进场
	var wave := _enemies()
	_check(wave.size() == n, "普通波成组刷出（%d 架）" % n)
	var slot_w := (view.size.x - 120.0) / float(n)
	var band := Vector2(view.position.y + spawner.hover_band().x, view.position.y + spawner.hover_band().y)  # 悬停带为 view 顶缘偏移（2026-07-30 view 适配）
	var slots_ok := true
	var anchor_ok := true
	for e in wave:
		var rel := e.position.x - (view.position.x + 60.0)
		var idx := int(rel / slot_w)
		if idx < 0 or idx >= n:
			slots_ok = false
		if e.anchor_y < band.x or e.anchor_y > band.y:
			anchor_ok = false
	_check(slots_ok, "普通波 x 均匀分布（各机落在均分槽位内）")
	_check(anchor_ok, "普通波锚点位于悬停带内")

	# 2. 敌机到达锚点后悬停机动：y 不再净下降（绕锚点 ±HOVER_BOB_AMP 浮动）
	var hover_e: Enemy = null
	for e in wave:
		if e.strategy == &"straight" or e.strategy == &"hover":
			hover_e = e
			break
	if hover_e == null:
		hover_e = wave[0]
	var t_wait := 0.0
	while is_instance_valid(hover_e) and not hover_e._hovering and t_wait < 6.0:
		await get_tree().create_timer(0.2).timeout
		t_wait += 0.2
	_check(is_instance_valid(hover_e) and hover_e._hovering, "敌机到达锚点转入悬停")
	if is_instance_valid(hover_e) and hover_e._hovering:
		var max_dev := 0.0
		for i in 5:
			await get_tree().create_timer(0.2).timeout
			if is_instance_valid(hover_e):
				max_dev = maxf(max_dev, absf(hover_e.position.y - hover_e.anchor_y))
		_check(max_dev <= hover_e.HOVER_BOB_AMP + 1.0, "悬停期间绕锚点浮动（无净下降）")
	for e in wave:
		if is_instance_valid(e):
			e.queue_free()
	await get_tree().process_frame

	# 3. Boss 激活期间普通波次计时冻结（Boss 占用波次槽）
	spawner.set_boss_active(true)
	spawner.set_wave_timer(3.0)
	spawner.set_process(true)
	await get_tree().create_timer(0.5).timeout
	_check(spawner.wave_timer() == 3.0, "Boss 激活期间波次计时不推进")
	spawner.set_boss_active(false)
	spawner.set_process(false)

	# 4. 精英节奏：连续 3~4 个普通波后出现精英波（计数归零）
	spawner.WAVE_INTERVAL_START = 0.05
	spawner.WAVE_INTERVAL_END = 0.05
	spawner.INTERVAL_MIN = 0.05
	spawner.set_wave_timer(0.05)
	spawner.set_waves_since_special(0)
	var gap := -1
	var prev := 0
	spawner.set_process(true)
	var t4 := 0.0
	while gap < 0 and t4 < 10.0:
		await get_tree().process_frame
		t4 += get_process_delta_time()
		var cur: int = spawner.waves_since_special()
		if prev > 0 and cur == 0:
			gap = prev
		prev = cur
	spawner.set_process(false)  # 精英波已触发（计数归零），立即冻结节奏断言现场
	# 精英有 0.6s 入场预告，等其实际进场
	var elite_seen := false
	var t5 := 0.0
	while not elite_seen and t5 < 2.0:
		await get_tree().create_timer(0.1).timeout
		t5 += 0.1
		for e in _enemies():
			if e.is_elite:
				elite_seen = true
	_check(gap >= spawner.SPECIAL_GAP_MIN and gap <= spawner.SPECIAL_GAP_MAX,
		"精英节奏：%d 个普通波后出精英波" % gap)
	_check(elite_seen, "精英波产出精英敌机")
	_check(spawner.waves_since_special() == 0, "精英波后特殊槽计数归零")
	for e in _enemies():
		if is_instance_valid(e):
			e.queue_free()
	await get_tree().process_frame

	# 5. 休整：精英/Boss 击杀后追加 REST_WAVES_AFTER_KILL 个普通波（计数置负）
	spawner.notify_special_killed()
	_check(spawner.waves_since_special() == -spawner.REST_WAVES_AFTER_KILL, "精英击杀后进入休整波次")
	spawner.set_waves_since_special(0)
	spawner.notify_boss_died()
	_check(spawner.waves_since_special() == -spawner.REST_WAVES_AFTER_KILL, "Boss 击杀后进入休整波次")

	print("WAVE PACING TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
