extends Node
## 母舰召唤序列测试：机库小窗（弹出/字幕镜头/finished 自毁）→ 穿梭门创建与关闭 →
## 母舰穿梭穿出减速入场（缩放/位置收敛）→ 减速带命中敌机 → DOCKING 火力掩护 →
## 回收玩家进保护舱（隐藏+关判定）→ STAY 隐藏保持 → RELEASE 出舱恢复。
## 小窗用真实时轴（~2.6s），穿梭段快进 _state_timer。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _ready() -> void:
	GameState.delete_save()
	GameState.reset_run()
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	await get_tree().process_frame
	await get_tree().process_frame

	var main := get_node("Main")
	var spawner := main.get_node("Spawner")
	(main.get_node("StartPanel") as CanvasLayer).hide()
	get_tree().paused = false  # 开始面板路径可能带暂停态；小窗 process_mode 跟随树，需非暂停
	# 全程禁用刷怪与随机事件编排：只验证召唤序列自身
	spawner.set_process(false)
	main.event().set_process(false)
	main.formation().set_process(false)
	main.player().set_auto_fire(false)

	# ---------- 1. 布置一台靶机（验证减速带与火力目标） ----------
	var tgt := load("res://scenes/enemy.tscn").instantiate() as Enemy
	tgt.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	tgt.can_shoot = false
	tgt.hp = 9999  # 不死，保证场内始终有目标
	tgt.position = Vector2(1200.0, 500.0)
	main.add_child(tgt)
	await get_tree().process_frame

	# ---------- 2. 机库小窗 ----------
	main.summon_mothership()
	await get_tree().process_frame
	var window: MothershipSummonWindow = main.summon_window()
	_check(window != null, "小窗：蓄力完成后弹出机库小窗")
	_check(main.mothership() == null, "小窗：播放期间母舰尚未创建")
	_check(main.player().is_input_locked(), "小窗：演出期玩家锁输入")
	_check(main.player().invincible_remaining() > 100.0, "小窗：演出期事件驱动无敌")
	main.summon_mothership()  # 幂等：播放中重复触发不叠加
	await get_tree().process_frame
	_check(main.summon_window() == window, "小窗：重复触发不叠加第二个窗口")
	_check(window.subtitle().text == tr("MS_SEQ_CHARGE"), "小窗：镜头 1 字幕（充能管线断开）")
	# 字幕镜头轮替（真实时轴：0.25 开场 + 0.8/0.6/0.7 三镜头）
	await get_tree().create_timer(1.2).timeout
	_check(window.subtitle().text == tr("MS_SEQ_ARMS"), "小窗：镜头 2 字幕（维护臂解除链接）")
	await get_tree().create_timer(0.8).timeout
	_check(window.subtitle().text == tr("MS_SEQ_LAUNCH"), "小窗：镜头 3 字幕（弹射·穿梭器启动）")
	# 播完 finished：小窗自毁 + 穿梭门/母舰创建
	for i in 40:
		await get_tree().create_timer(0.05).timeout
		if main.summon_window() == null:
			break
	_check(main.summon_window() == null and not is_instance_valid(window), "小窗：播完自毁并释放引用")
	await get_tree().process_frame

	# ---------- 3. 穿梭门与穿梭入场 ----------
	var gate: WarpGate = null
	for child in main.get_children():
		if child is WarpGate:
			gate = child
	_check(gate != null, "穿梭门：小窗结束后创建")
	var ms: Mothership = main.mothership()
	_check(ms != null, "穿梭门：母舰已创建")
	_check(ms.state() == Mothership.State.DESCEND, "穿梭入场：DESCEND 态")
	_check(ms.scale.x < 1.0, "穿梭入场：穿出期缩放小于 1（%.2f）" % ms.scale.x)
	ms.set_state_timer(ms.WARP_IN_TIME)  # 快进穿梭入场
	await get_tree().create_timer(0.3).timeout
	_check(ms.state() == Mothership.State.DOCKING, "穿梭入场：到位后自动对接")
	_check(ms.scale.is_equal_approx(Vector2.ONE), "穿梭入场：到位缩放收敛为 1")
	_check(ms.position.distance_to(Vector2(GameState.view_world_rect().get_center().x, ms.HOVER_Y)) < 5.0, "穿梭入场：停驻点收敛")
	_check(gate == null or not is_instance_valid(gate) or gate.phase() == WarpGate.Phase.CLOSING, "穿梭门：母舰穿出后关闭")
	_check(tgt.summon_slow_timer() > 0.0, "减速带：敌机被施加短时减速")

	# ---------- 4. DOCKING 火力掩护 + 回收进保护舱 ----------
	await get_tree().create_timer(0.4).timeout
	var dock_fire := false
	for child in main.get_children():
		if child is Bullet and child.is_player_bullet and child.score_scale < 1.0:
			dock_fire = true
	_check(dock_fire, "火力掩护：DOCKING 态即开火（不耗弹匣）")
	_check(ms.mag_cells() == ms.MAG_CELLS, "火力掩护：DOCKING 不耗驻留弹匣")
	for i in 40:
		await get_tree().create_timer(0.05).timeout
		if not main.player().visible:
			break
	_check(not main.player().visible, "保护舱：回收完成玩家隐藏")
	await get_tree().process_frame
	await get_tree().process_frame
	_check(not main.player().hitbox_enabled(), "保护舱：受击判定关闭")
	_check(ms.beam().visible == false, "保护舱：牵引光束回收后隐藏")

	# ---------- 5. STAY 隐藏保持 → RELEASE 出舱 ----------
	for i in 40:
		await get_tree().create_timer(0.05).timeout
		if ms.state() == Mothership.State.STAY:
			break
	_check(ms.state() == Mothership.State.STAY, "驻留：进入 STAY")
	_check(not main.player().visible, "驻留：玩家保持隐藏（保护舱）")
	# E05：H 按住时进度条可见；强制离舰（start_release）必须清掉——修复前 H 按住被强制
	# 离舰（警告到期/弹匣耗尽）进度条残留可见
	var hud := get_tree().get_first_node_in_group("hud")
	if hud != null:
		hud.set_early_leave_charge(0.5)
		_check(hud.early_leave_box().visible, "E05：前置：提前离舰进度条可见（模拟 H 按住）")
	ms.start_release()
	await get_tree().process_frame
	_check(main.player().visible, "释放：玩家出舱恢复显示")
	if hud != null and is_instance_valid(hud):
		_check(not hud.early_leave_box().visible, "E05：start_release 清除提前离舰进度条")
	for i in 40:
		await get_tree().create_timer(0.05).timeout
		if not main.player().is_input_locked():
			break
	_check(not main.player().is_input_locked(), "释放：输入解锁")
	_check(main.player().invincible_remaining() <= 2.0, "释放：无敌重置为 2s 保护")
	if main.mothership() != null:
		main.mothership().queue_free()

	# ---------- 5b. G011：母舰提前回收（_exit_tree，返航路径）须清除提前离舰进度条 ----------
	var hud_g011 := get_tree().get_first_node_in_group("hud")
	if hud_g011 != null:
		hud_g011.set_early_leave_charge(0.5)
		_check(hud_g011.early_leave_box().visible, "G011：前置：提前离舰进度条可见")
		var ms2 := (load("res://scenes/mothership.tscn") as PackedScene).instantiate()
		main.add_child(ms2)
		await get_tree().process_frame
		ms2.queue_free()
		await get_tree().process_frame
		_check(not hud_g011.early_leave_box().visible, "G011：母舰回收（_exit_tree）清除提前离舰进度条")

	GameState.delete_save()
	GameState.save_profile()
	print("[DONE] failures=%d" % _failures)
	get_tree().quit(1 if _failures > 0 else 0)
