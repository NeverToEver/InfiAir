extends Node
## HUD 布局巡检截图：常态（2 个 buff）与极端（已解锁 buff 满层：BUFF_POOL_SIZE=19
## 池中当前 15 种 distinct 加满，R07 注释修正）两种形态，
## 每屏截图存 /tmp/hud_<name>.png。需窗口模式运行（headless 为 dummy 渲染截不到画面）：
##   godot --path . res://test/hud_capture.tscn
## 结束恢复现场：删除测试产生的存档，profile 按备份内容还原落盘（R07：注释修正——
## 原「原始值还原」措辞失实，实际为删除测试存档 + save_profile 落盘当前值）。

const SETTLE_SECONDS := 0.6  # 等重建/淡入动效播完（真实时间）


func _ready() -> void:
	GameState.delete_save()

	var main_scene: PackedScene = load("res://scenes/main.tscn")
	GameState.login_guest()  # T4：游客会话直接开局（StartPanel 已退役）
	add_child(main_scene.instantiate())
	await get_tree().process_frame
	await get_tree().process_frame

	# 屏蔽里程碑触发，避免 Buff UI 叠屏（确定性截图）
	GameState.set_milestone_override(999999999)
	GameState.add_score(12340)
	GameState.kills = 57
	GameState.score_changed.emit(GameState.score)

	# 1. 常态：2 个 buff（单/多层各一）
	GameState.add_buff(&"power_shot")
	GameState.add_buff(&"power_shot")
	GameState.add_buff(&"armor")
	await _settle()
	_shot("normal")

	# 2. 极端：全部已解锁 buff 叠层拉满（BUFF_POOL_SIZE=19 池中 15 种 distinct，R07 修正）
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

	# 3. L 展开 buff 滚动栏（已解锁 15 种 distinct 明细行，R07 修正）
	get_node("Main/HUD").toggle_buff_panel()
	await _settle()
	_shot("panel")

	# 恢复现场：删测试存档 + 还原 profile 原始值落盘
	GameState.delete_save()
	GameState.save_profile()
	print("hud capture done")
	load("res://csharp/godot/TestExit.cs").Quit(0)


func _settle() -> void:
	# 真实时间等待，与帧率无关
	await get_tree().create_timer(SETTLE_SECONDS).timeout


func _shot(name: String) -> void:
	var path := "/tmp/hud_%s.png" % name
	get_viewport().get_texture().get_image().save_png(path)
	print("capture saved: ", path)
