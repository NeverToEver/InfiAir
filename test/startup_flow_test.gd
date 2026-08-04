extends Node
## 启动流程测试（2026-08-04 账户系统 T4 重写）：welcome 主场景入口状态、
## 存档/档案损坏隔离提示、教程按钮门控、main 启动自动读档继续。

const WELCOME_SCENE: PackedScene = preload("res://scenes/welcome.tscn")
const MAIN_SCENE: PackedScene = preload("res://scenes/main.tscn")

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _wipe_user_files() -> void:
	for f in [
		"user://users.json",
		"user://users.json.corrupt",
		"user://profile.json",
		"user://profile.json.corrupt",
		"user://savegame.json",
		"user://savegame.json.corrupt",
	]:
		if FileAccess.file_exists(f):
			DirAccess.remove_absolute(f)


func _wipe_user_saves() -> void:
	var dir := DirAccess.open("user://")
	if dir == null:
		return
	dir.list_dir_begin()
	var name := dir.get_next()
	while name != "":
		if name.begins_with("savegame_") or name.ends_with(".corrupt"):
			DirAccess.remove_absolute("user://" + name)
		name = dir.get_next()
	dir.list_dir_end()


func _ready() -> void:
	GameState.logout_user()
	GameState.delete_save()
	_wipe_user_files()
	_wipe_user_saves()
	GameState.high_score = 0
	GameState.profile_corrupt = false
	GameState.save_corrupt = false

	# 1. 注册 + 登录（无存档）：主区显示「开始游戏」且无「继续对局」
	var welcome: CanvasLayer = WELCOME_SCENE.instantiate() as CanvasLayer
	add_child(welcome)
	await get_tree().process_frame
	_check(not welcome.main_zone_visible(), "未登录时处于登录阶段")
	_check(GameState.create_user("flow", "pass123"), "注册测试用户")
	welcome.username_line().text = "flow"
	welcome.password_line().text = "pass123"
	welcome.press_login()
	await get_tree().process_frame
	_check(welcome.main_zone_visible(), "登录放行进入主区")
	_check(not GameState.has_save(), "无存档时 has_save 为 false")
	_check(not welcome.continue_button().visible, "无存档时不显示继续对局")
	welcome.queue_free()
	await get_tree().process_frame

	# 2. 有存档：主区显示「继续对局」+ 开始游戏为次按钮
	GameState.login_user("flow")
	GameState.save_run(50.0, 10.0)
	_check(GameState.has_save(), "登录用户存档存在")
	welcome = WELCOME_SCENE.instantiate() as CanvasLayer
	add_child(welcome)
	await get_tree().process_frame
	welcome.username_line().text = "flow"
	welcome.password_line().text = "pass123"
	welcome.press_login()
	await get_tree().process_frame
	_check(welcome.continue_button().visible, "有存档时显示继续对局")
	_check(welcome.new_button().text != "", "开始游戏按钮存在")
	welcome.queue_free()
	await get_tree().process_frame

	# 3. 损坏存档：隔离备份 + 继续按钮隐藏 + 损坏提示可见
	var save_path := GameState.user_db_savefile_for("flow")
	var f := FileAccess.open(save_path, FileAccess.WRITE)
	f.store_string("{broken json")
	f.close()
	GameState.login_user("flow")
	_check(GameState.load_run_data().is_empty() and GameState.save_corrupt, "损坏存档隔离并按无存档处理")
	_check(not GameState.has_save(), "损坏存档隔离后 has_save 为 false")
	welcome = WELCOME_SCENE.instantiate() as CanvasLayer
	add_child(welcome)
	await get_tree().process_frame
	welcome.username_line().text = "flow"
	welcome.password_line().text = "pass123"
	welcome.press_login()
	await get_tree().process_frame
	_check(not welcome.continue_button().visible, "损坏存档后继续对局隐藏")
	_check(welcome.corrupt_label().visible, "损坏存档提示可见")
	_check(welcome.corrupt_label().text != "START_SAVE_CORRUPT", "损坏提示文案已翻译（tr 命中）")
	welcome.queue_free()
	await get_tree().process_frame

	# 4. 损坏档案（profile.json）：损坏提示可见且文案为档案口径
	GameState.logout_user()
	var legacy := FileAccess.open("user://profile.json", FileAccess.WRITE)
	legacy.store_string("{broken profile")
	legacy.close()
	GameState.profile_corrupt = false
	GameState.load_profile()
	_check(GameState.profile_corrupt, "损坏档案隔离置 profile_corrupt")
	welcome = WELCOME_SCENE.instantiate() as CanvasLayer
	add_child(welcome)
	await get_tree().process_frame
	welcome.username_line().text = "flow"
	welcome.password_line().text = "pass123"
	welcome.press_login()
	await get_tree().process_frame
	_check(welcome.corrupt_label().visible, "损坏档案提示可见")
	_check(welcome.corrupt_label().text != "START_SAVE_CORRUPT", "损坏档案文案区分于存档")
	welcome.queue_free()
	await get_tree().process_frame

	# 5. 教程门控：进行中存档禁用教程；通关且无存档时放行
	GameState.login_user("flow")
	GameState.save_run(50.0, 10.0)
	welcome = WELCOME_SCENE.instantiate() as CanvasLayer
	add_child(welcome)
	await get_tree().process_frame
	welcome.username_line().text = "flow"
	welcome.password_line().text = "pass123"
	welcome.press_login()
	await get_tree().process_frame
	_check(welcome.tutorial_button().disabled, "进行中存档时教程按钮禁用（防删档）")
	welcome.queue_free()
	await get_tree().process_frame
	GameState.delete_save()
	GameState.tutorial_done = true
	GameState.save_profile()
	welcome = WELCOME_SCENE.instantiate() as CanvasLayer
	add_child(welcome)
	await get_tree().process_frame
	welcome.username_line().text = "flow"
	welcome.password_line().text = "pass123"
	welcome.press_login()
	await get_tree().process_frame
	_check(not welcome.tutorial_button().disabled, "通关且无存档时教程按钮放行")
	welcome.queue_free()
	await get_tree().process_frame

	# 6. main 启动自动继续：登录用户有档 → 实例化 main → 分数/对局恢复（T3 新逻辑）
	GameState.login_user("flow")
	GameState.score = 12345
	GameState.kills = 7
	GameState.save_run(50.0, 10.0)
	GameState.score = 0
	var main: Node2D = MAIN_SCENE.instantiate() as Node2D
	add_child(main)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(GameState.score == 12345, "main 启动自动读档恢复分数")
	_check(GameState.kills == 7, "main 启动自动读档恢复击杀")
	_check(not get_tree().paused, "继续对局不冻结（无开始面板暂停门控）")
	main.queue_free()
	await get_tree().process_frame

	print("STARTUP FLOW TEST DONE, failures = ", _failures)
	GameState.logout_user()
	GameState.delete_save()
	GameState.reset_run()
	_wipe_user_files()
	_wipe_user_saves()
	get_tree().quit(_failures)
