extends Node
## HUD 布局巡检截图：常态（2 个 buff）与极端（全 16 种 buff 满层）两种形态，
## 每屏截图存 /tmp/hud_<name>.png。需窗口模式运行（headless 为 dummy 渲染截不到画面）：
##   godot --path . res://test/hud_capture.tscn
## 结束恢复现场：删除测试产生的存档，profile 原始值（welcome_seen）还原落盘。

const SETTLE_SECONDS := 0.6  # 等重建/淡入动效播完（真实时间）


func _ready() -> void:
	var orig_welcome_seen: bool = GameState.welcome_seen
	GameState.delete_save()
	GameState.welcome_seen = true  # 跳过欢迎页遮挡（内存值，结尾还原）

	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	await get_tree().process_frame
	var welcome: CanvasLayer = get_node("Main/WelcomeScreen")
	if welcome.visible:
		welcome.dismiss()
	await get_tree().process_frame

	# 直接开新局（测试实例化 main 不是 current_scene，不播开场过场）
	var sp: CanvasLayer = get_node("Main/StartPanel")
	if sp.visible:
		sp._on_new_game_pressed()
		await get_tree().process_frame
	# 屏蔽里程碑触发，避免 Buff UI 叠屏（确定性截图）
	GameState._set_milestone_override(999999999)
	GameState.add_score(12340)
	GameState.kills = 57
	GameState.score_changed.emit(GameState.score)

	# 1. 常态：2 个 buff（单/多层各一）
	GameState.add_buff(&"power_shot")
	GameState.add_buff(&"power_shot")
	GameState.add_buff(&"armor")
	await _settle()
	_shot("normal")

	# 2. 极端：全 16 种 buff，叠层拉满（验证图标坞密度与分角隔离）
	for i in 3:
		GameState.add_buff(&"power_shot")  # 共 5 层
	for i in 4:
		GameState.add_buff(&"rapid_fire")
	for i in 3:
		GameState.add_buff(&"spread_shot")
	for i in 10:
		GameState.add_buff(&"extra_life")
	GameState.add_buff(&"regen")
	for i in 2:
		GameState.add_buff(&"piercing")
	GameState.add_buff(&"explosive")
	GameState.add_buff(&"lifesteal")
	GameState.add_buff(&"evasion")
	for i in 3:
		GameState.add_buff(&"phase_dash")
	GameState.add_buff(&"slow_field")
	GameState.add_buff(&"efficient_boost")
	GameState.add_buff(&"boost_recovery")
	GameState.add_buff(&"mothership_recall")
	GameState.add_buff(&"laser_beam")
	# 等首发激光束（获得即发，3s）播完再截，避免遮挡画面
	await get_tree().create_timer(3.4).timeout
	_shot("stress")

	# 3. L 展开 buff 滚动栏（全 16 种明细行）
	get_node("Main/HUD")._toggle_buff_panel()
	await _settle()
	_shot("panel")

	# 恢复现场：删测试存档 + 还原 profile 原始值落盘
	GameState.delete_save()
	GameState.welcome_seen = orig_welcome_seen
	GameState.save_profile()
	print("hud capture done")
	get_tree().quit()


func _settle() -> void:
	# 真实时间等待，与帧率无关
	await get_tree().create_timer(SETTLE_SECONDS).timeout


func _shot(name: String) -> void:
	var path := "/tmp/hud_%s.png" % name
	get_viewport().get_texture().get_image().save_png(path)
	print("capture saved: ", path)
