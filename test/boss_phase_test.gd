extends Node
## Boss 阶段框架测试（BOSS_REDESIGN §4，阶段 A）：
## 场景1（一型）：P1→P2→ENRAGE 阈值依次到达、模式表循环推进、段切换清计时/锁血语义不变；
## 场景2（二型）：狙击 telegraph 时序（先瞄准线、≥0.3s 后才出弹、3 连发、线用完即毁）；
## 场景3（三型）：旋转 cross + 召唤填表验证；
## 场景4：血条阶段刻度线存在、逃跑倒计时显示与随 Boss 死亡隐藏。

# M3b：Enemy 迁 C#，is 判定经脚本资源引用（GDScript 不能 is C# 类）
var _enemy_script := load("res://csharp/godot/Enemy.cs")

var _failures: int = 0
var _phase_signal: int = -1  # 最近收到的 phase_changed


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## 真实时间等待（不受 time_scale 影响；process_always 保证暂停时也走时）
func _wait_real(sec: float) -> void:
	await get_tree().create_timer(sec, true, false, true).timeout


## 在场敌弹（玩家弹排除）
func _enemy_bullets() -> Array:
	var out: Array = []
	for child: Variant in get_node("Main").get_children():
		# M3a：Bullet 为 C# 类——GDScript 不能 is Bullet/作类型注解，has_method("TryGraze") 鸭子识别；属性 PascalCase
		if child.has_method("TryGraze") and not child.IsPlayerBullet:
			out.append(child)
	return out


func _close_buff_ui_if_open() -> void:
	var buff_ui: CanvasLayer = get_node("Main/BuffUI")
	if buff_ui.visible:
		var ev := InputEventMouseButton.new()
		ev.pressed = true
		ev.button_index = MOUSE_BUTTON_LEFT
		buff_ui.pick_buff(&"rapid_fire")
	get_tree().paused = false


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
	GameState.login_guest()  # T4：游客会话直接开局（StartPanel 已退役）
	add_child(main_scene.instantiate())
	var main := get_node("Main")
	# 开场面板自显即暂停（冻结背景），先关闭解除
	var player = get_node("Main/Player")
	player.set_auto_fire(false)  # 全程禁用全自动开火，避免误杀 Boss/触发里程碑
	player.set_invincible(999.0)  # 弹幕期间不被误伤
	await get_tree().process_frame
	await get_tree().process_frame
	var spawner: Node = get_node("Main/Spawner")
	spawner.set_process(false)  # 停掉自动刷怪/Boss 调度，保证确定性
	player.position = Vector2(960.0, 540.0)

	# ================= 场景 1：一型阶段阈值切换 + 模式表循环 =================
	var boss: Boss = await _spawn_test_boss(1)
	_check(boss != null, "场景1：Boss 已生成")
	boss.phase_changed.connect(func(p: int) -> void: _phase_signal = p)
	# 缩短模式表便于观测循环推进（实例 var 覆盖，不影响 balance.json）
	(
		boss
		. set_patterns(
			{
				"p1":
				[
					{"attack": &"fan5", "waves": 2, "interval": 0.25},
					{"attack": &"homing", "waves": 1, "interval": 0.25},
				],
				"p2": [{"attack": &"fan7", "waves": 2, "interval": 0.25}],
			}
		)
	)
	boss.set_pattern_index(0)
	boss.start_pattern()
	await _wait_real(0.3)
	_check(boss.fight_phase() == Boss.FightPhase.P1, "场景1：初始为 P1")
	# 模式循环推进：fan5 两波播完应切到 homing（index 0→1）
	var advanced := false
	for i in 20:
		await _wait_real(0.1)
		if not is_instance_valid(boss):
			break
		if boss.pattern_index() != 0:
			advanced = true
			break
	_check(advanced, "场景1：模式表波次播完推进到下一模式")
	_check(_enemy_bullets().size() >= 5, "场景1：模式攻击出弹（5 路扇形波次）")
	# P1→P2：打到 65%（≤70% 阈值）
	var y_before_phase: float = boss.position.y  # L14：段切换前 y（验证切换无跳变）
	boss.take_damage(int(boss.max_hp * 0.35))
	await get_tree().process_frame
	_check(boss.fight_phase() == Boss.FightPhase.P2, "场景1：HP ≤70% 进入 P2")
	_check(_phase_signal == Boss.FightPhase.P2, "场景1：段切换发出 phase_changed")
	_check(is_equal_approx(boss.hp, boss.max_hp * 0.65), "场景1：P2 阈值不钳血（锁血仅狂暴 30% 语义不变）")
	_check(not boss.enrage_sequence().is_health_locked(), "场景1：P2 段切换不触发锁血")
	_check(boss.pattern_index() == 0, "场景1：段切换重置模式表循环")
	# C11 + L14：段切换 y 平滑过渡——不再「立即回锚线」（原实现 P2 首帧绝对赋值，
	# 切换恰在下压窗口内会瞬间跳变）；切换后机身从当前 y 平滑追锚线，首帧不得跳变
	_check(absf(boss.position.y - y_before_phase) < 4.0, "场景1：P2 段切换瞬间机身无 y 跳变")
	# D05：P2 走位——strafe 提速 200 + 纵向正弦往复（采样 1s 物理帧）
	# L14：先等 0.7s 过渡收敛（BOB_SMOOTH_TIME 0.6s + 余量），再采样验证正弦轨迹
	# （过渡期 y 从切换前位置回落，混入采样会破坏「振幅在 ±amp 内」断言）
	for i in 7:
		await _wait_real(0.1)
		if not is_instance_valid(boss):
			break
	var y_min := INF
	var y_max := -INF
	var x_min := INF
	var x_max := -INF
	for i in 10:
		await _wait_real(0.1)
		if not is_instance_valid(boss):
			break
		y_min = minf(y_min, boss.position.y)
		y_max = maxf(y_max, boss.position.y)
		x_min = minf(x_min, boss.position.x)
		x_max = maxf(x_max, boss.position.x)
	var anchor_y: float = boss.fight_anchor_y()
	var amp: float = boss.TYPE1_P2_BOB_AMP
	# L14：采样窗口相位无关断言——1s 采样（60° 相位窗口）内最大偏离必 ≥20px（amp=40），
	# 用「偏离锚线」替代原「峰谷差」断言（原断言依赖切换后相位从 0 起步的上升段，
	# 过渡等待后采样窗口相位任意，峰谷差可能 <20px）
	_check(
		maxf(absf(y_max - anchor_y), absf(y_min - anchor_y)) > 10.0,
		"场景1：P2 纵向正弦偏离锚线（最大偏离 ≥10px，实测 %.1f）" % maxf(absf(y_max - anchor_y), absf(y_min - anchor_y))
	)
	_check(y_max <= anchor_y + amp + 4.0 and y_min >= anchor_y - amp - 4.0, "场景1：P2 纵向振幅在 ±amp 内（amp=%.0f）" % amp)
	_check(x_max - x_min > 30.0, "场景1：P2 横向 strafe 持续移动（采样期 x 位移 %.1fpx）" % (x_max - x_min))
	# P2→ENRAGE：打到 25%（钳 30% 触发狂暴；一击跨两段狂暴优先）
	boss.take_damage(int(boss.max_hp * 0.4))
	await get_tree().process_frame
	_check(boss.is_enraged() and boss.fight_phase() == Boss.FightPhase.ENRAGE, "场景1：HP <30% 进入 ENRAGE")
	_check(boss.enrage_sequence().is_health_locked(), "场景1：狂暴锁血语义不变")
	_check(is_equal_approx(player.enrage_slow(), 0.35), "场景1：TRANSITION 中玩家减速 ×0.35")
	# 快进 main 子弹时间等恢复
	main.set_bullet_time(0.05)
	for i in 40:
		await _wait_real(0.1)
		if is_equal_approx(Engine.time_scale, 1.0):
			break
	# 序列中断复位减速；击杀后保持 1.0
	boss.abort_enrage_sequence()
	_check(is_equal_approx(player.enrage_slow(), 1.0), "场景1：序列中断复位玩家减速")
	boss.take_damage(9999)
	await get_tree().process_frame
	_check(not is_instance_valid(boss), "场景1：解锁后可击杀")
	_check(is_equal_approx(player.enrage_slow(), 1.0), "场景1：Boss 被击杀后减速保持复位")
	_close_buff_ui_if_open()
	for b in _enemy_bullets():
		b.queue_free()
	await get_tree().process_frame

	# ================= 场景 2：二型狙击 telegraph 时序 =================
	var boss2: Boss = await _spawn_test_boss(2)
	_check(boss2 != null, "场景2：Boss 已生成")
	boss2.set_patterns(
		{"p1": [{"attack": &"sniper3", "waves": 1, "interval": 1.2}], "p2": [{"attack": &"sniper3", "waves": 1, "interval": 1.2}]}
	)
	boss2.set_pattern_index(0)
	boss2.start_pattern()
	boss2.set_fire_timer(0.1)  # 立即起手
	var line_appeared := false
	var line_tick := 0
	for i in 30:
		await _wait_real(0.05)
		if not is_instance_valid(boss2):
			break
		if boss2.attacks().aim_line() != null:
			line_appeared = true
			line_tick = Time.get_ticks_msec()
			break
	_check(line_appeared, "场景2：狙击先出现瞄准线 telegraph")
	_check(_enemy_bullets().is_empty(), "场景2：telegraph 期间未出弹")
	var fire_elapsed := -1
	for i in 40:
		await _wait_real(0.05)
		if not _enemy_bullets().is_empty():
			fire_elapsed = Time.get_ticks_msec() - line_tick
			break
	_check(fire_elapsed >= 300, "场景2：瞄准线出现 ≥0.3s 后才出弹（实测 %dms）" % fire_elapsed)
	_check(boss2.attacks().aim_line() == null, "场景2：出弹后瞄准线即毁")
	await _wait_real(0.4)  # 3 连发 0.12s 间隔
	_check(_enemy_bullets().size() == 3, "场景2：到点沿线 3 连发出弹")
	boss2.take_damage(9999)
	await get_tree().process_frame
	_close_buff_ui_if_open()
	for b in _enemy_bullets():
		b.queue_free()
	await get_tree().process_frame

	# ================= 场景 3：三型旋转 cross + 召唤 =================
	var boss3: Boss = await _spawn_test_boss(3)
	_check(boss3 != null, "场景3：Boss 已生成")
	boss3.set_fire_timer(0.1)
	boss3.set_summon_timer(0.3)
	await _wait_real(0.2)  # 首波 cross 出弹后立即断言（向上弹 0.6s 内即出屏消失）
	_check(_enemy_bullets().size() >= 4, "场景3：旋转 cross 出弹（一波 4 弹）")
	await _wait_real(0.4)
	var minion_found := false
	for child in get_node("Main").get_children():
		if is_instance_of(child, _enemy_script):  # M3b：Enemy 迁 C#，is 改脚本判定
			minion_found = true
	_check(minion_found, "场景3：召唤小怪独立计时保持")
	# D05：三型 P1 缓慢下压/回升——机身 y 从锚线压向锚线下 [min, max] 区间（采样 1s）
	var t3_y_min := INF
	var t3_y_max := -INF
	for i in 10:
		await _wait_real(0.1)
		if not is_instance_valid(boss3):
			break
		t3_y_min = minf(t3_y_min, boss3.position.y)
		t3_y_max = maxf(t3_y_max, boss3.position.y)
	var t3_anchor: float = boss3.fight_anchor_y()
	_check(t3_y_max - t3_y_min > 30.0, "场景3：P1 缓慢下压/回升（采样期 y 位移 ≥30px，实测 %.1f）" % (t3_y_max - t3_y_min))
	_check(
		t3_y_max <= t3_anchor + boss3.TYPE3_P1_BOB_MAX + 6.0,
		"场景3：P1 下压不超过锚线下 max（max=%.0f，实测 y_max=%.1f）" % [boss3.TYPE3_P1_BOB_MAX, t3_y_max - t3_anchor]
	)
	boss3.take_damage(9999)
	await get_tree().process_frame
	_close_buff_ui_if_open()
	for child: Variant in get_node("Main").get_children():
		if is_instance_of(child, _enemy_script) or (child.has_method("TryGraze") and not child.IsPlayerBullet):  # M3b：Enemy 迁 C#，is 改脚本判定
			child.queue_free()
	await get_tree().process_frame

	# ================= 场景 4：血条刻度线 + 逃跑倒计时 =================
	var hud: CanvasLayer = get_node("Main/HUD")
	_check(get_node("Main/HUD/BossBar").get_child_count() >= 1, "场景4：血条有阶段刻度线覆盖层")
	var boss4: Boss = await _spawn_test_boss(1)
	_check(boss4 != null, "场景4：Boss 已生成")
	boss4.set_fire_timer(999.0)  # 屏蔽开火，保持场内干净
	await _wait_real(0.3)
	_check(not hud.boss_countdown().visible, "场景4：剩余 >10s 不显示倒计时")
	boss4.set_survival(boss4.ESCAPE_TIME - 5.0)  # 剩余 5s ≤ countdown_visible_from(10s)
	await _wait_real(0.3)
	_check(hud.boss_countdown().visible and hud.boss_countdown().text != "", "场景4：剩余 ≤10s 血条下方显示逃跑倒计时")
	boss4.take_damage(9999)
	await get_tree().process_frame
	_close_buff_ui_if_open()
	await _wait_real(0.3)
	_check(not hud.boss_countdown().visible, "场景4：Boss 死亡后倒计时隐藏")

	# ================= 场景 5：Q02/Q03 配置损坏回退（4 型守卫，2026-08-05） =================
	var orig_balance: String = FileAccess.get_file_as_string(GameState.BALANCE_PATH)
	var bf := FileAccess.open(GameState.BALANCE_PATH, FileAccess.WRITE)
	bf.store_string(JSON.stringify({"boss": {"hp_mults": [1.3, 0.7, 1.6]}}))  # 3 元素截断 + type4 区块缺失
	bf.close()
	GameState.reload_balance()
	var boss5: Boss = await _spawn_test_boss(4)
	_check(boss5 != null, "场景5：损坏配置下月蚀已生成")
	# 2026-08-06 审计：null 守卫——原 _check 后无守卫直接解引用，生成失败（如 Q02 回退
	# 异常）时崩溃跳过下方 balance.json 恢复，仓库文件留损坏态；守卫内才断言/结算
	if boss5 != null:
		_check(boss5.max_hp > 0.0, "场景5：Q02 3 元素 hp_mults 回退 4 元素默认——type4 max_hp=%.0f > 0（原越界免疫伤害）" % boss5.max_hp)
		_check(
			boss5.patterns()["p1"][0]["attack"] == &"ring_burst",
			"场景5：Q03 type4 配置缺失回退脚本默认表（含 ring_burst，实测 %s，原钳 3 回退三型表）" % str(boss5.patterns()["p1"][0]["attack"])
		)
		boss5.take_damage(9999)
		await get_tree().process_frame
	# balance.json 恢复无条件执行（防损坏配置残留仓库）
	_close_buff_ui_if_open()
	bf = FileAccess.open(GameState.BALANCE_PATH, FileAccess.WRITE)
	bf.store_string(orig_balance)
	bf.close()
	GameState.reload_balance()

	# ================= 场景 6：M4（2026-08-06 审计）4 型狂暴分档表补齐 =================
	# type4 的 interval/speed/count 原不在 _apply_difficulty_scaling 三表内，
	# 狂暴参数三档恒定（easy 偏难、hard 偏易）；直改 difficulty 字段（不经 setter 不落盘）
	var saved_diff: StringName = GameState.difficulty
	GameState.difficulty = &"easy"
	var boss_easy: Boss = await _spawn_test_boss(4)
	GameState.difficulty = &"hard"
	var boss_hard: Boss = await _spawn_test_boss(4)
	GameState.difficulty = saved_diff
	_check(boss_easy != null and boss_hard != null, "M4：easy/hard 月蚀已生成")
	if boss_easy != null and boss_hard != null:
		_check(boss_easy.E4_RING_INTERVAL > boss_hard.E4_RING_INTERVAL, "M4：ring_interval 随难度分档（easy 1.15× / hard 0.85×）")
		_check(boss_easy.E4_RING_SPEED < boss_hard.E4_RING_SPEED, "M4：ring_speed 随难度分档（easy 0.9× / hard 1.1×）")
		_check(boss_easy.E4_RELEASE_RING_SPEED < boss_hard.E4_RELEASE_RING_SPEED, "M4：release_ring_speed 随难度分档")
		_check(boss_easy.E4_RING_COUNT < boss_hard.E4_RING_COUNT, "M4：ring_count 随难度分档（[-2,0,+2]）")
		_check(boss_easy.E4_RELEASE_RING_COUNT < boss_hard.E4_RELEASE_RING_COUNT, "M4：release_ring_count 随难度分档")
		_check(is_equal_approx(boss_easy.RING_BURST_SPEED, 340.0 * 0.9), "M4：普通阶段 ring_burst 弹速随难度分档（easy ×0.9）")
		boss_easy.queue_free()
		boss_hard.queue_free()
		await get_tree().process_frame

	_check(is_equal_approx(Engine.time_scale, 1.0), "收尾：退出前 time_scale = 1.0")
	_check(is_equal_approx(player.enrage_slow(), 1.0), "收尾：退出前玩家减速已复位")
	for child in get_node("Main").get_children():
		if child.has_method("TryGraze"):
			child.queue_free()
	await get_tree().process_frame
	await _wait_real(2.0)  # 演出 tween/爆炸序列播完，避免退出时对象泄漏
	# L15：还原用户最高分并落盘（收尾不污染用户 profile）
	GameState.high_score = orig_high_score
	GameState.save_profile()
	print("BOSS PHASE TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
