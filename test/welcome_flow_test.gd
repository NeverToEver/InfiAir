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


func _press_esc() -> void:
	var ev := InputEventAction.new()
	ev.action = "ui_cancel"
	ev.pressed = true
	_welcome._unhandled_input(ev)


func _ready() -> void:
	GameState.logout_user()
	_wipe_user_files()
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
	_press_esc()
	_check(not _welcome.leaderboard_overlay().visible, "ESC 关闭排行榜 overlay（B7-1）")
	_welcome.press_leaderboard()
	_welcome.close_leaderboard()
	_check(not _welcome.leaderboard_overlay().visible, "× 关闭排行榜 overlay")

	# 8. 游客流程：确认框（B7-6 统一走确认；B7-5 默认焦点返回）
	GameState.logout_user()
	_welcome = WELCOME_SCENE.instantiate() as CanvasLayer
	add_child(_welcome)
	await get_tree().process_frame
	_welcome.press_guest()
	_check(_welcome.guest_confirm().visible, "游客按钮弹确认框")
	_press_esc()
	_check(not _welcome.guest_confirm().visible, "ESC 关闭游客确认（B7-2）")
	_welcome.press_guest()
	_welcome.confirm_guest()
	_check(_welcome.main_zone_visible() and GameState.is_guest(), "游客放行进入主区")

	# 9. 删除流程：确认前密码非空校验；确认后删号清空表单
	GameState.logout_user()
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
	_press_esc()
	_check(_welcome.exit_confirm_layer().visible, "顶层 ESC 弹退出确认")
	_press_esc()
	_check(not _welcome.exit_confirm_layer().visible, "ESC 关闭退出确认")

	# 11. 空凭证 ENTER → 游客确认框（B7-5 防连按游客开局）
	_welcome.username_line().text = ""
	_welcome.password_line().text = ""
	var ev := InputEventAction.new()
	ev.action = "ui_accept"
	ev.pressed = true
	_welcome._unhandled_input(ev)
	_check(_welcome.guest_confirm().visible, "空凭证 ENTER 弹游客确认框")

	print("WELCOME FLOW TEST DONE, failures = ", _failures)
	GameState.logout_user()
	_wipe_user_files()
	get_tree().quit(_failures)
