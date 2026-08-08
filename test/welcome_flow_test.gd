extends Node
## welcome 主场景流程测试（2026-08-04 账户系统 T3）：注册/登录/游客/删除、焦点环、
## ESC 层级（overlay→模态→退出确认）、排行榜 overlay、难度持久化、主区显隐。
## 场景切换路径（继续/新游戏 → main.tscn）由既有对局测试适配覆盖（T4），本测试不切换场景。

const WELCOME_SCENE: PackedScene = preload("res://scenes/welcome.tscn")

var _failures: int = 0
var _welcome: CanvasLayer


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _wipe_user_files() -> void:
	for f in ["user://users.json", "user://users.json.corrupt", "user://profile.json", "user://savegame.json"]:
		if FileAccess.file_exists(f):
			DirAccess.remove_absolute(f)


## Q23（2026-08-05）：开头备份、结尾还原用户文件——本地跑测试不再永久销毁开发者账户表
var _file_backups: Dictionary = {}


func _backup_user_files() -> void:
	_file_backups = {}
	for f in ["user://users.json", "user://users.json.corrupt", "user://profile.json", "user://savegame.json"]:
		var exists := FileAccess.file_exists(f)
		_file_backups[f] = {"exists": exists, "content": FileAccess.get_file_as_string(f) if exists else ""}


func _restore_user_files() -> void:
	for f in _file_backups:
		var b: Dictionary = _file_backups[f]
		if b["exists"]:
			var fh := FileAccess.open(f, FileAccess.WRITE)
			fh.store_string(b["content"])
			fh.close()
		elif FileAccess.file_exists(f):
			DirAccess.remove_absolute(f)


## Q24（2026-08-05）：真实按键事件走完整输入管线（原直调 _unhandled_input 绕过输入管线，
## C30 已修模式回归；esc_navigation_test 同款 InputEventKey + parse_input_event）
func _press_esc() -> void:
	var ev := InputEventKey.new()
	ev.keycode = KEY_ESCAPE
	ev.pressed = true
	Input.parse_input_event(ev)
	await get_tree().process_frame
	await get_tree().process_frame
	var up := InputEventKey.new()
	up.keycode = KEY_ESCAPE
	up.pressed = false
	Input.parse_input_event(up)
	await get_tree().process_frame


func _press_enter() -> void:
	var ev := InputEventKey.new()
	ev.keycode = KEY_ENTER
	ev.pressed = true
	Input.parse_input_event(ev)
	await get_tree().process_frame
	await get_tree().process_frame
	var up := InputEventKey.new()
	up.keycode = KEY_ENTER
	up.pressed = false
	Input.parse_input_event(up)
	await get_tree().process_frame


func _ready() -> void:
	GameState.logout_user()
	_backup_user_files()  # Q23：快照用户文件，结尾还原
	_wipe_user_files()
	GameState.reload_user_db()  # 2026-08-06 审计：GameState._ready 迁移探测缓存了真实用户表，wipe 后须刷新
	GameState.high_score = 0
	GameState.difficulty = &"medium"

	_welcome = WELCOME_SCENE.instantiate() as CanvasLayer
	add_child(_welcome)
	await get_tree().process_frame

	# 1. 初始状态：登录阶段、登录面板可见、主区隐藏
	_check(not _welcome.main_zone_visible(), "初始为登录阶段")
	_check(_welcome.username_line() != null and _welcome.password_line() != null, "登录面板输入框存在")
	_check(_welcome.password_line().secret, "密码框掩码")

	# 2. 注册校验：短名/短密码/空 → 错误消息；保留名被拒
	_welcome.username_line().text = "ab"
	_welcome.password_line().text = "pass123"
	_welcome.press_register()
	_check(not GameState.user_exists("ab"), "短用户名注册被拒")
	_welcome.username_line().text = "carol"
	_welcome.password_line().text = "pw"
	_welcome.press_register()
	_check(not GameState.user_exists("carol"), "短密码注册被拒")
	_welcome.username_line().text = ""
	_welcome.password_line().text = ""
	_welcome.press_register()
	_check(not GameState.user_exists(""), "空凭证注册被拒")
	_welcome.username_line().text = "Guest"
	_welcome.password_line().text = "pass123"
	_welcome.press_register()
	_check(not GameState.user_exists("Guest"), "保留名 Guest 注册被拒")

	# 3. 注册成功：保留用户名、清空密码（B7-9）
	_welcome.username_line().text = "pilot"
	_welcome.password_line().text = "s3cret"
	_welcome.press_register()
	_check(GameState.user_exists("pilot"), "注册成功入库")
	_check(_welcome.username_line().text == "pilot", "注册成功保留用户名（B7-9）")
	_check(_welcome.password_line().text == "", "注册成功清空密码（B7-9）")

	# 4. 错误密码登录被拒，仍处登录阶段
	_welcome.username_line().text = "pilot"
	_welcome.password_line().text = "wrong"
	_welcome.press_login()
	_check(not _welcome.main_zone_visible(), "错误凭证登录被拒")

	# 5. 正确登录 → 主区显示、会话置位、难度按钮同步
	_welcome.password_line().text = "s3cret"
	_welcome.press_login()
	_check(_welcome.main_zone_visible(), "正确凭证登录放行")
	_check(GameState.current_user == "pilot", "登录置位当前用户")
	_check(GameState.is_guest() == false, "登录非游客")

	# 6. 难度单选：切换持久化（主区难度按钮）
	_check(GameState.difficulty == &"medium", "主区难度初始为档案值")

	# 7. 排行榜 overlay：打开（空榜显示占位）→ 关闭
	_welcome.press_leaderboard()
	_check(_welcome.leaderboard_overlay().visible, "排行榜 overlay 打开")
	await _press_esc()
	_check(not _welcome.leaderboard_overlay().visible, "ESC 关闭排行榜 overlay（B7-1）")
	_welcome.press_leaderboard()
	_welcome.close_leaderboard()
	_check(not _welcome.leaderboard_overlay().visible, "× 关闭排行榜 overlay")

	# 8. 游客流程：确认框（B7-6 统一走确认；B7-5 默认焦点返回）
	GameState.logout_user()
	_welcome.queue_free()  # 释放旧实例（Q09 修复：残留实例的 SettingsUI 抢占 group，press_settings 命中错误节点）
	await get_tree().process_frame
	_welcome = WELCOME_SCENE.instantiate() as CanvasLayer
	add_child(_welcome)
	await get_tree().process_frame
	_welcome.press_guest()
	_check(_welcome.guest_confirm().visible, "游客按钮弹确认框")
	await _press_esc()
	_check(not _welcome.guest_confirm().visible, "ESC 关闭游客确认（B7-2）")
	_welcome.press_guest()
	_welcome.confirm_guest()
	_check(_welcome.main_zone_visible() and GameState.is_guest(), "游客放行进入主区")

	# 9. 删除流程：确认前密码非空校验；确认后删号清空表单
	GameState.logout_user()
	_welcome.queue_free()  # 释放旧实例（Q09 修复，同场景 8）
	await get_tree().process_frame
	_welcome = WELCOME_SCENE.instantiate() as CanvasLayer
	add_child(_welcome)
	await get_tree().process_frame
	_welcome.username_line().text = "pilot"
	_welcome.password_line().text = ""
	_welcome.press_delete()
	_check(not _welcome.delete_confirm().visible, "密码为空时不弹删除确认（B7-13 先验密码）")
	_welcome.password_line().text = "s3cret"
	_welcome.press_delete()
	_check(_welcome.delete_confirm().visible, "删除确认框弹出")
	_welcome.confirm_delete()
	_check(not GameState.user_exists("pilot"), "删除确认后用户移除")
	_check(_welcome.username_line().text == "", "删除成功清空用户名（B7-9）")

	# 10. ESC 层级：无 overlay/模态时 → 退出确认；退出确认关闭
	await _press_esc()
	_check(_welcome.exit_confirm_layer().visible, "顶层 ESC 弹退出确认")
	await _press_esc()
	_check(not _welcome.exit_confirm_layer().visible, "ESC 关闭退出确认")

	# 11. 空凭证 ENTER → 游客确认框（B7-5 防连按游客开局）
	_welcome.username_line().text = ""
	_welcome.password_line().text = ""
	await _press_enter()
	_check(_welcome.guest_confirm().visible, "空凭证 ENTER 弹游客确认框")
	await _press_esc()
	_check(not _welcome.guest_confirm().visible, "ENTER 后的游客确认 ESC 关闭")

	# 12. Q09（2026-08-05）：设置页打开时 Esc 关闭设置页（原 Esc 落到隐藏层退出确认，
	# 设置页永远关不掉——与 EXIT_FLOW「settings back = Esc」矛盾）
	_welcome.press_settings()
	await get_tree().process_frame
	var settings_ui: CanvasLayer = _welcome.get_node("SettingsUI")
	_check(settings_ui.visible, "Q09：设置页打开")
	_check(not _welcome.visible, "Q09：welcome 主层随设置页打开隐藏")
	await _press_esc()
	_check(not settings_ui.visible, "Q09：设置页 Esc 关闭")
	_check(_welcome.visible, "Q09：welcome 主层恢复显示")

	print("WELCOME FLOW TEST DONE, failures = ", _failures)
	GameState.logout_user()
	_restore_user_files()  # Q23：还原用户文件快照
	load("res://csharp/godot/TestExit.cs").Quit(_failures)
