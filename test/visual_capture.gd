extends Node
## 视觉验证：按 MODE 截图到 /tmp/infiair_capture.png。
## 需窗口模式运行（headless 为 dummy 渲染，截不到画面）：
##   godot --path . res://test/visual_capture.tscn
## MODE: gameplay（默认，Boss 警告画面）/ hud（常态对局 HUD：buff 芯片 + 低血晕影）/ boss_fight（Boss 名牌 + 血条 + 狂暴态）/ start_panel（存档开始面板）/ base（基地控制台）/ mothership（母舰驻留）/ summon（召唤机库小窗）/ settings（设置页）/ exit_confirm（暂停面板 + 战斗退出确认窗）

const FRAMES_BEFORE_SHOT := 100
const SHOT_PATH := "/tmp/infiair_capture.png"
const MODE := "gameplay"
const FORCE_LOCALE := ""  # "en" 时强制英文截图


func _ready() -> void:
	if FORCE_LOCALE != "":
		GameState.set_locale(FORCE_LOCALE)
	if MODE == "start_panel":
		GameState.save_run(50.0, 10.0)  # 伪造存档让开始面板出现
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	if MODE != "start_panel":
		# 关闭开始面板（无存档时它开场自显会遮挡画面）
		await get_tree().process_frame
		var sp: CanvasLayer = get_node("Main/StartPanel")
		if sp.visible:
			sp.press_new_game()
	match MODE:
		"exit_confirm":
			# 暂停面板 + 战斗退出确认窗（battle 模式进度损失警告）
			var pui: CanvasLayer = get_node("Main/PauseUI")
			pui.open()
			pui.quit()
			for i in 30:
				await get_tree().process_frame
		"gameplay":
			# 触发 Boss 警告横幅，便于截图覆盖该画面
			get_node("Main/Spawner").trigger_boss()
			for i in FRAMES_BEFORE_SHOT:
				await get_tree().process_frame
		"hud":
			# 常态对局 HUD：垫 buff 让芯片区可见 + 压低 HP 展示低血晕影脉动
			GameState.add_buff(&"power_shot")
			GameState.add_buff(&"power_shot")
			GameState.add_buff(&"spread_shot")
			GameState.add_buff(&"armor")
			GameState.add_buff(&"laser_beam")
			GameState.health = 18.0
			for i in FRAMES_BEFORE_SHOT:
				await get_tree().process_frame
		"boss_fight":
			# Boss 名牌 + 血条 + 狂暴态（打掉 75% HP 触发狂暴，名牌整行转红）
			get_node("Main/Spawner").trigger_boss()
			var boss: Boss = null
			for i in 1800:  # 等降入完成进入战斗（上限 30s，窗口低帧率冗余）
				await get_tree().process_frame
				for e in GameState.enemies:
					if e is Boss and (e as Boss).is_in_fight():
						boss = e
				if boss != null:
					break
			if boss != null:
				boss.take_damage(int(boss.max_hp * 0.75))
				for i in 120:  # 狂暴转场演出一段后截图
					await get_tree().process_frame
		"start_panel":
			for i in 30:
				await get_tree().process_frame
		"base":
			# 基地控制台界面
			await get_tree().process_frame
			GameState.add_rp(10)
			GameState.add_buff(&"spread_shot")
			get_node("Main").start_homecoming()
			await get_tree().create_timer(2.0).timeout
		"settings":
			# 设置页（控制分区改键表）
			get_tree().get_first_node_in_group("settings_ui").show_settings()
			for i in 30:
				await get_tree().process_frame
		"mothership":
			# 母舰召唤序列（小窗跳过）→ 穿梭入场快进 → 自动对接 + 敌机（驻留扫射/导弹）
			var main := get_node("Main")
			main.summon_mothership()
			main.summon_window().skip()
			await get_tree().process_frame
			var ms: Mothership = main.mothership()
			ms.set_state_timer(ms.WARP_IN_TIME  )# 快进穿梭入场，到位触发自动对接
			var spawner := get_node("Main/Spawner")
			var tgt := load("res://scenes/enemy.tscn").instantiate() as Enemy
			tgt.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
			tgt.position = Vector2(1200.0, 500.0)
			main.add_child(tgt)
			await get_tree().create_timer(2.8).timeout  # 对接 1.5s + 补给 0.5s → 驻留
		"summon":
			# 召唤机库小窗演出镜头 1（充能管线断开）
			var main := get_node("Main")
			main.summon_mothership()
			await get_tree().create_timer(0.65).timeout
	var img := get_viewport().get_texture().get_image()
	img.save_png(SHOT_PATH)
	print("capture saved: ", SHOT_PATH)
	GameState.delete_save()
	get_tree().quit()
