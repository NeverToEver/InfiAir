extends Node
## 召唤序列视觉截图工具（窗口模式运行，headless 为 dummy 渲染截不到画面）：
##   godot --path . res://test/summon_capture.tscn
## 驱动真实时轴逐段截图到 /tmp/summon_*.png：蓄力中段 → 机库小窗 3 镜头 →
## 穿梭门/DESCEND → 牵引光束 DOCKING → 驻留 STAY。
## 不写 user:// 存档/档案（与并行运行的其他测试隔离）；setup 对齐 mothership_summon_test。


func _shot(path: String) -> void:
	await get_tree().process_frame  # 让渲染推进一帧再取帧缓冲
	var img := get_viewport().get_texture().get_image()
	img.save_png(path)
	print("capture saved: ", path)


func _ready() -> void:
	GameState.reset_run()
	GameState.welcome_seen = true  # 跳过欢迎页（内存标记，不落盘）
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	await get_tree().process_frame
	await get_tree().process_frame

	var main := get_node("Main")
	var spawner := main.get_node("Spawner")
	(main.get_node("StartPanel") as CanvasLayer).hide()
	get_tree().paused = false
	# 全程禁用刷怪与随机事件编排：只拍召唤序列自身
	spawner.set_process(false)
	main.event().set_process(false)
	main.formation().set_process(false)
	main.player().set_auto_fire(false)

	# 靶机一台：DOCKING 火力掩护的目标（不死、不开火）
	var tgt := load("res://scenes/enemy.tscn").instantiate() as Enemy
	tgt.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	tgt.can_shoot = false
	tgt.hp = 9999
	tgt.position = Vector2(1200.0, 500.0)
	main.add_child(tgt)
	await get_tree().process_frame

	# ---------- 1. 蓄力中段（虚影 + 蓄力特效） ----------
	Input.action_press("dock")
	main.set_charge_time(main.DOCK_CHARGE_TIME * 0.55  )# 预填到中段，让环收缩/背光可读
	await get_tree().create_timer(0.35).timeout
	await _shot("/tmp/summon_charge.png")
	Input.action_release("dock")
	main.stop_charging()

	# ---------- 2. 机库小窗 3 镜头（真实时轴 ~2.6s） ----------
	main.summon_mothership()
	await get_tree().create_timer(0.65).timeout  # 镜头 1 中段（充能管线断开）
	await _shot("/tmp/summon_window1.png")
	await get_tree().create_timer(0.8).timeout  # 镜头 2 中段（维护臂收回）
	await _shot("/tmp/summon_window2.png")
	await get_tree().create_timer(0.6).timeout  # 镜头 3 中段（弹射出仓）
	await _shot("/tmp/summon_window3.png")
	for i in 40:  # 等播完：小窗自毁 + 穿梭门/母舰创建
		await get_tree().create_timer(0.05).timeout
		if main.summon_window() == null:
			break

	# ---------- 3. 穿梭门 + 母舰 DESCEND 前段（舰体尚在门心，前唇遮挡读「穿门」） ----------
	await get_tree().create_timer(0.14).timeout
	await _shot("/tmp/summon_gate.png")

	# ---------- 4. DOCKING 牵引光束（流环/描边/尘粒 + 火力掩护） ----------
	await get_tree().create_timer(1.05).timeout  # DESCEND 剩余 + DOCKING 前 0.4s
	await _shot("/tmp/summon_beam.png")

	# ---------- 5. 驻留 STAY（玩家已进保护舱） ----------
	var ms: Mothership = main.mothership()
	for i in 60:
		await get_tree().create_timer(0.05).timeout
		if ms == null or not is_instance_valid(ms) or ms._state == Mothership.State.STAY:
			break
	await get_tree().create_timer(0.3).timeout
	await _shot("/tmp/summon_stay.png")

	# 清理：收回母舰（_exit_tree 恢复玩家出舱），靶机随场景退出
	if main.mothership() != null:
		main.mothership().queue_free()
	print("[DONE] summon capture finished")
	get_tree().quit(0)
