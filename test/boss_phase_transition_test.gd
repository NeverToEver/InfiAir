extends Node
## Boss 阶段转场公平感测试（2026-08-03 机制三，docs/archive/2026-08-03-combat-fairness-plan.md §4）：
## P1→P2 / ENRAGE 切换清弹 + 玩家转场无敌（只增不减）、转场瞬间受击不结算、
## 逃跑期不清弹不给无敌（回归既有逃跑流程）、分段血条段权/段色登记与绘制语义
## （segment_fill 纯函数）、非 Boss 条默认等分回归、清弹为单次遍历无逐帧轮询。

var _failures: int = 0
var _phase_signal: int = -1


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## 真实时间等待（不受 time_scale 影响；process_always 保证暂停时也走时）
func _wait_real(sec: float) -> void:
	await get_tree().create_timer(sec, true, false, true).timeout


## 当前场内敌弹（玩家弹排除）
func _enemy_bullets() -> Array[Bullet]:
	var out: Array[Bullet] = []
	for child in get_node("Main").get_children():
		if child is Bullet and not child.is_player_bullet:
			out.append(child)
	return out


func _free_enemy_bullets() -> void:
	for b in _enemy_bullets():
		b.queue_free()
	await get_tree().process_frame


## 生成 Boss 并跳过降入；调用方负责击杀/清理
func _spawn_test_boss(p_type: int) -> Boss:
	var spawner: Node = get_node("Main/Spawner")
	spawner.spawn_boss(p_type)
	await get_tree().process_frame
	var boss: Boss = null
	for child in get_node("Main").get_children():
		if child is Boss:
			boss = child
	boss.position.y = boss.fight_anchor_y()  # 跳过降入（锚线 = view 顶缘 + FIGHT_Y），下一物理帧进入战斗
	return boss


func _ready() -> void:
	# 清理持久化状态，保证测试确定性
	GameState.delete_save()
	# L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
	var orig_high_score: int = GameState.high_score
	GameState.high_score = 0
	GameState.save_profile()
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	var main := get_node("Main")
	var start_panel: CanvasLayer = get_node("Main/StartPanel")
	if start_panel.visible:
		start_panel.press_new_game()
	var player: Player = get_node("Main/Player")
	player.set_auto_fire(false)  # 全程禁用全自动开火，避免误杀 Boss/触发里程碑
	player.set_invincible(999.0)  # 用例间兜底（各用例自行重置）
	await get_tree().process_frame
	await get_tree().process_frame
	var spawner: Node = get_node("Main/Spawner")
	spawner.set_process(false)  # 停掉自动刷怪/Boss 调度，保证确定性
	player.position = Vector2(960.0, 540.0)

	# ================= 场景 1：P1→P2 切换清弹 + phase_changed + 持续攻击复位 + 转场无敌 =================
	var boss: Boss = await _spawn_test_boss(1)
	_check(boss != null, "场景1：Boss 已生成")
	boss.phase_changed.connect(func(p: int) -> void: _phase_signal = p)
	(
		boss
		. set_patterns(
			{
				"p1": [{"attack": &"sniper3", "waves": 1, "interval": 0.5}],
				"p2": [{"attack": &"fan5", "waves": 1, "interval": 1.6}],
			}
		)
	)
	boss.set_fire_timer(0.1)
	player.set_invincible(0.0)
	player.set_last_hit_frame(-1)
	# 等狙击瞄准线出现（持续攻击进行中）
	var line_seen := false
	for i in 40:
		await _wait_real(0.05)
		if not is_instance_valid(boss):
			break
		if boss.attacks().aim_line() != null:
			line_seen = true
			break
	_check(line_seen, "场景1：狙击瞄准线已出现（持续攻击进行中）")
	var pb1 := GameState.bullet_pool.fire(Vector2.DOWN, 0.0, 10, false)
	pb1.position = Vector2(400.0, 200.0)
	var pb2 := GameState.bullet_pool.fire(Vector2.DOWN, 0.0, 10, false)
	pb2.position = Vector2(600.0, 300.0)
	# P1→P2：打到 65%（≤70% 阈值）
	boss.take_damage(int(boss.max_hp * 0.35))
	await get_tree().process_frame
	_check(boss.fight_phase() == Boss.FightPhase.P2, "场景1：HP ≤70% 进入 P2")
	_check(_phase_signal == Boss.FightPhase.P2, "场景1：段切换发出 phase_changed")
	_check(_enemy_bullets().is_empty(), "场景1：转场清弹——活跃敌弹数归零")
	_check(boss.attacks().aim_line() == null, "场景1：持续攻击（狙击线）状态复位")
	_check(player.invincible_remaining() > 0.8, "场景1：转场给玩家短暂无敌（%ds）" % boss.TRANSITION_INVINCIBLE)

	# ================= 场景 2：转场瞬间玩家受击 → 无敌期内不结算 =================
	GameState.health = 100.0
	player.set_last_hit_frame(-1)
	var hit_b := GameState.bullet_pool.fire(Vector2.DOWN, 0.0, 12, false)
	hit_b.position = player.position
	await get_tree().create_timer(0.2).timeout
	_check(GameState.health == 100.0, "场景2：转场无敌期内受击不结算")
	_check(hit_b.visible, "场景2：无敌期弹不销毁（穿过语义）")
	await _free_enemy_bullets()

	# ================= 场景 3：ENRAGE 触发（<30%）→ 同样清弹 + 无敌 =================
	player.set_invincible(0.0)
	player.set_last_hit_frame(-1)
	GameState.health = 100.0
	var eb1 := GameState.bullet_pool.fire(Vector2.DOWN, 0.0, 10, false)
	eb1.position = Vector2(400.0, 200.0)
	var eb2 := GameState.bullet_pool.fire(Vector2.DOWN, 0.0, 10, false)
	eb2.position = Vector2(600.0, 300.0)
	boss.take_damage(int(boss.max_hp * 0.4))  # P2 内打到 25% → 钳 30% 触发狂暴
	await get_tree().process_frame
	_check(boss.is_enraged() and boss.fight_phase() == Boss.FightPhase.ENRAGE, "场景3：HP <30% 进入 ENRAGE")
	_check(_enemy_bullets().is_empty(), "场景3：ENRAGE 转场清弹")
	_check(player.invincible_remaining() > 0.8, "场景3：ENRAGE 转场给玩家无敌")
	# 快进 main 子弹时间等恢复（仿 boss_phase_test）
	main.set_bullet_time(0.05)
	for i in 40:
		await _wait_real(0.1)
		if is_equal_approx(Engine.time_scale, 1.0):
			break
	boss.abort_enrage_sequence()
	boss.take_damage(9999)
	await get_tree().process_frame
	_check(not is_instance_valid(boss), "场景3：狂暴序列解除后击杀清理")
	await _free_enemy_bullets()

	# ================= 场景 4：逃跑期（50s 超时）→ 不清弹、不给无敌 =================
	var boss4: Boss = await _spawn_test_boss(1)
	_check(boss4 != null, "场景4：Boss 已生成")
	boss4.set_fire_timer(999.0)  # 屏蔽开火，保持场内干净
	player.set_invincible(0.0)
	player.set_last_hit_frame(-1)
	var esc_b1 := GameState.bullet_pool.fire(Vector2.DOWN, 0.0, 10, false)
	esc_b1.position = Vector2(400.0, 200.0)
	boss4.set_survival(boss4.ESCAPE_TIME)  # 直接到 50s 点
	await get_tree().create_timer(0.2).timeout  # 等物理帧过降入 + 到点判定（position 同步延迟一帧）
	_check(boss4.is_escaping(), "场景4：50s 到点进入逃跑流程")
	_check(_enemy_bullets().size() == 1, "场景4：逃跑不清弹（敌弹保留）")
	_check(player.invincible_remaining() == 0.0, "场景4：逃跑不给玩家无敌")
	await get_tree().create_timer(1.5).timeout  # Boss 上飘离场（escaped/died 清理）
	await _free_enemy_bullets()

	# ================= 场景 5：分段血条——绘制语义（segment_fill 纯函数）+ HUD 登记 =================
	var w: Array = [0.3, 0.4, 0.3]
	_check(
		is_equal_approx(SegmentedBar.segment_fill(1.0, w, 0), 0.0) and is_equal_approx(SegmentedBar.segment_fill(1.0, w, 2), 0.0),
		"场景5：满血全部段未消耗（全亮）"
	)
	_check(is_equal_approx(SegmentedBar.segment_fill(0.85, w, 0), 0.5), "场景5：P1 段消耗度 (1-0.85)/0.3（P1 段消耗后暗化）")
	_check(is_equal_approx(SegmentedBar.segment_fill(0.85, w, 1), 0.0), "场景5：P2 段未消耗（当前段高亮语义）")
	_check(is_equal_approx(SegmentedBar.segment_fill(0.85, w, 2), 0.0), "场景5：ENRAGE 段未消耗")
	_check(is_equal_approx(SegmentedBar.segment_fill(0.7, w, 0), 1.0), "场景5：HP=70% P1 段全暗")
	_check(is_equal_approx(SegmentedBar.segment_fill(0.5, w, 1), 0.5), "场景5：P2 段消耗度 (0.7-0.5)/0.4")
	_check(is_equal_approx(SegmentedBar.segment_fill(0.2, w, 2), 1.0 / 3.0), "场景5：ENRAGE 段消耗度 (0.3-0.2)/0.3")
	_check(is_equal_approx(SegmentedBar.segment_fill(0.0, w, 2), 1.0), "场景5：HP=0 ENRAGE 段全暗")
	# HUD 登记：spawner 出场 Boss 时已调 show_boss_bar（场景 1-4 同路径）
	var boss5: Boss = await _spawn_test_boss(1)
	boss5.set_fire_timer(999.0)
	var bb := get_node("Main/HUD/BossBar") as SegmentedBar
	_check(
		bb.seg_weights.size() == 3 and is_equal_approx(float(bb.seg_weights[0]), 0.3) and is_equal_approx(float(bb.seg_weights[2]), 0.3),
		"场景5：BossBar 段权 [0.3,0.4,0.3] 登记"
	)
	_check(bb.seg_colors.size() == 3 and bb.segments == 3, "场景5：段色 3 段 + 段数 3 登记")
	boss5.take_damage(9999)
	await get_tree().process_frame
	await _free_enemy_bullets()

	# ================= 场景 6：非 Boss 场景——HP/燃料/dash 条分段不变（默认等分） =================
	var hp_bar := get_node("Main/HUD/HpBar") as SegmentedBar
	var fuel_bar := get_node("Main/HUD/FuelBar") as SegmentedBar
	var dash_bar := get_node("Main/HUD/DashBar") as SegmentedBar
	_check(
		hp_bar.seg_weights.is_empty() and fuel_bar.seg_weights.is_empty() and dash_bar.seg_weights.is_empty(),
		"场景6：非 Boss 条默认等分（seg_weights 空，绘制走既有逻辑）"
	)

	# ================= 场景 7：清弹为单次遍历——切换后新弹不被自动清（无逐帧轮询） =================
	var boss7: Boss = await _spawn_test_boss(1)
	boss7.set_fire_timer(999.0)
	player.set_invincible(0.0)
	player.set_last_hit_frame(-1)
	var n1 := GameState.bullet_pool.fire(Vector2.DOWN, 0.0, 10, false)
	n1.position = Vector2(400.0, 200.0)
	boss7.take_damage(int(boss7.max_hp * 0.35))  # P1→P2 转场清弹
	await get_tree().process_frame
	_check(_enemy_bullets().is_empty(), "场景7：切换瞬间清弹")
	var n2 := GameState.bullet_pool.fire(Vector2.DOWN, 0.0, 10, false)
	n2.position = Vector2(400.0, 200.0)
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(_enemy_bullets().size() == 1, "场景7：切换后新弹保留（清弹为单次遍历，无逐帧轮询）")
	boss7.take_damage(9999)
	await get_tree().process_frame
	await _free_enemy_bullets()

	_check(is_equal_approx(Engine.time_scale, 1.0), "收尾：退出前 time_scale = 1.0")
	for child in get_node("Main").get_children():
		if child is Bullet:
			child.queue_free()
	await get_tree().process_frame
	await _wait_real(2.0)  # 演出 tween/爆炸序列播完，避免退出时对象泄漏
	# L15：还原用户最高分并落盘（收尾不污染用户 profile）
	GameState.high_score = orig_high_score
	GameState.save_profile()
	print("BOSS PHASE TRANSITION TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
