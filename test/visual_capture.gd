extends Node
## 视觉验证：按 MODE 截图到 /tmp/infiair_capture.png。
## 需窗口模式运行（headless 为 dummy 渲染，截不到画面）：
##   godot --path . res://test/visual_capture.tscn
## MODE: gameplay（默认，Boss 警告画面）/ welcome（欢迎页）/ start_panel（存档开始面板）/ base（基地控制台）/ mothership（母舰对接）/ settings（设置页）/ exit_confirm（暂停面板 + 战斗退出确认窗）

const FRAMES_BEFORE_SHOT := 100
const SHOT_PATH := "/tmp/infiair_capture.png"
const MODE := "gameplay"
const FORCE_LOCALE := ""  # "en" 时强制英文截图


func _ready() -> void:
	if FORCE_LOCALE != "":
		GameState.set_locale(FORCE_LOCALE)
	if MODE == "start_panel":
		GameState.save_run(50.0, 10.0)  # 伪造存档让开始面板出现
	if MODE == "welcome":
		GameState.welcome_seen = false  # 欢迎页仅首次启动显示，截图前强制复位
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	if MODE != "welcome":
		# 关闭欢迎页（进游戏首屏，会遮挡画面；关闭后进入开始面板）
		await get_tree().process_frame
		var welcome: CanvasLayer = get_node("Main/WelcomeScreen")
		if welcome.visible:
			welcome.dismiss()
	if MODE != "start_panel" and MODE != "welcome":
		# 关闭开始面板（无存档时它开场自显会遮挡画面）
		await get_tree().process_frame
		var sp: CanvasLayer = get_node("Main/StartPanel")
		if sp.visible:
			sp._on_new_game_pressed()
	match MODE:
		"welcome":
			for i in 30:
				await get_tree().process_frame
		"exit_confirm":
			# 暂停面板 + 战斗退出确认窗（battle 模式进度损失警告）
			var pui: CanvasLayer = get_node("Main/PauseUI")
			pui.open()
			pui._on_quit_pressed()
			for i in 30:
				await get_tree().process_frame
		"gameplay":
			# 触发 Boss 警告横幅，便于截图覆盖该画面
			get_node("Main/Spawner")._trigger_boss()
			for i in FRAMES_BEFORE_SHOT:
				await get_tree().process_frame
		"start_panel":
			for i in 30:
				await get_tree().process_frame
		"base":
			# 基地控制台界面
			await get_tree().process_frame
			GameState.add_rp(10)
			GameState.add_buff(&"spread_shot")
			get_node("Main")._start_homecoming()
			await get_tree().create_timer(2.0).timeout
		"settings":
			# 设置页（控制分区改键表）
			get_tree().get_first_node_in_group("settings_ui").show_settings()
			for i in 30:
				await get_tree().process_frame
		"mothership":
			# 母舰自动对接 + 敌机（驻留扫射/导弹）+ 玩家吸附驻留（光束 + 弹匣条）
			var main := get_node("Main")
			main._summon_mothership()
			var ms: Mothership = main._mothership
			ms.position = Vector2(960.0, 269.0)  # 到位触发自动对接
			var spawner := get_node("Main/Spawner")
			var tgt := load("res://scenes/enemy.tscn").instantiate() as Enemy
			tgt.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
			tgt.position = Vector2(1200.0, 500.0)
			main.add_child(tgt)
			await get_tree().create_timer(2.8).timeout  # 对接 1.5s + 补给 0.5s → 驻留
	var img := get_viewport().get_texture().get_image()
	img.save_png(SHOT_PATH)
	print("capture saved: ", SHOT_PATH)
	GameState.delete_save()
	get_tree().quit()
