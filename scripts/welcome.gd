extends CanvasLayer
## welcome 主场景（2026-08-04 账户系统 T3；规格 PORTING_PARITY 附录 B B1-B3/B6/B7 去 bug 清单）。
## 登录阶段：左栏账号面板（注册/登录/游客/删除 + 下拉）+ 右栏难度/教程/设置/排行榜/最高分；
## 登录/游客放行后切主区：继续对局（有档）/ 开始游戏 + 新游戏。进入 main 后由 main 依存档自动继续。
## ESC 层级（对齐 B3/B7-1/2/3）：关排行榜 → 关游客/删除确认 → 关下拉 → 退出确认（welcome 是首场景）。

enum Stage { LOGIN, MAIN }

const DROPDOWN_MAX := 4
const USERNAME_MAX := 16
const PASSWORD_MAX := 16

var _stage: int = Stage.LOGIN
var _dim: ColorRect
var _login_panel: ChamferedPanel
var _username_line: LineEdit
var _password_line: LineEdit
var _msg_label: Label
var _dropdown: Panel
var _dropdown_buttons: Array[Button] = []
var _main_zone: VBoxContainer
var _continue_button: Button
var _new_button: Button
var _tutorial_button: Button
var _leaderboard_button: Button
var _settings_button: Button
var _diff_buttons: Dictionary = {}
var _diff_group := ButtonGroup.new()
var _high_score_label: Label
var _board_label: Label
var _corrupt_label: Label
var _leaderboard_overlay: CanvasLayer
var _leaderboard_rows: VBoxContainer
## 模态结构：{"layer": CanvasLayer, "ok": Button, "cancel": Button}
var _guest_confirm: Dictionary = {}
var _delete_confirm: Dictionary = {}
var _exit_confirm: Dictionary = {}
var _msg_timer: SceneTreeTimer


func _ready() -> void:
	visible = true
	# 全遮光标题屏（不透明对局背景；welcome 是独立场景，无冻结背景语义）
	_dim = ColorRect.new()
	_dim.color = Color(0.018, 0.03, 0.055, 1.0)
	_dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(_dim)
	add_child(StartBackdrop.new())

	# 左上品牌区
	var hero := VBoxContainer.new()
	hero.set_anchors_preset(Control.PRESET_TOP_LEFT)
	hero.position = Vector2(140.0, 130.0)
	hero.custom_minimum_size = Vector2(800.0, 0.0)
	hero.add_theme_constant_override("separation", 10)
	add_child(hero)
	var title := UITheme.make_label("InfiAir", UITheme.FONT_DISPLAY, UITheme.ACCENT, HORIZONTAL_ALIGNMENT_LEFT)
	hero.add_child(title)
	var accent := ColorRect.new()
	accent.color = UITheme.ACCENT
	accent.custom_minimum_size = Vector2(120.0, 4.0)
	accent.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	hero.add_child(accent)
	_high_score_label = UITheme.make_label("", UITheme.FONT_BODY, UITheme.ACCENT_GOLD, HORIZONTAL_ALIGNMENT_LEFT)
	hero.add_child(_high_score_label)
	_board_label = UITheme.make_label("", UITheme.FONT_CAPTION, UITheme.TEXT_DIM, HORIZONTAL_ALIGNMENT_LEFT)
	hero.add_child(_board_label)
	_corrupt_label = UITheme.make_label("", UITheme.FONT_CAPTION, UITheme.DANGER, HORIZONTAL_ALIGNMENT_LEFT)
	hero.add_child(_corrupt_label)

	_build_login_panel()
	_build_main_zone()
	_build_overlays()
	_build_esc_hint()

	GameState.locale_changed.connect(_refresh_texts)
	_refresh_texts()
	_prefill_last_login()


# ---------------- 登录面板（左栏） ----------------


func _build_login_panel() -> void:
	_login_panel = ChamferedPanel.new()
	_login_panel.custom_minimum_size = Vector2(520.0, 560.0)
	_login_panel.brackets = true
	_login_panel.set_anchors_preset(Control.PRESET_CENTER_LEFT)
	_login_panel.position = Vector2(140.0, -20.0)
	add_child(_login_panel)

	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left", 28)
	margin.add_theme_constant_override("margin_right", 28)
	margin.add_theme_constant_override("margin_top", 24)
	margin.add_theme_constant_override("margin_bottom", 24)
	_login_panel.add_child(margin)

	var content := VBoxContainer.new()
	content.add_theme_constant_override("separation", 14)
	margin.add_child(content)

	content.add_child(UITheme.make_section_header(tr("WELCOME_ACCOUNT")))
	_username_line = _make_line_edit(tr("WELCOME_USERNAME"), false)
	content.add_child(_username_line)
	_password_line = _make_line_edit(tr("WELCOME_PASSWORD"), true)
	content.add_child(_password_line)

	var actions := HBoxContainer.new()
	actions.add_theme_constant_override("separation", 12)
	content.add_child(actions)
	var login_button := UITheme.make_button(tr("WELCOME_LOGIN"), true)
	login_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	login_button.pressed.connect(_do_login)
	actions.add_child(login_button)
	var register_button := UITheme.make_button(tr("WELCOME_REGISTER"))
	register_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	register_button.pressed.connect(_do_register)
	actions.add_child(register_button)

	var second_row := HBoxContainer.new()
	second_row.add_theme_constant_override("separation", 12)
	content.add_child(second_row)
	var guest_button := UITheme.make_button(tr("WELCOME_GUEST"))
	guest_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	guest_button.pressed.connect(_show_guest_confirm)
	second_row.add_child(guest_button)
	var delete_button := UITheme.make_button(tr("WELCOME_DELETE"))
	delete_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	delete_button.pressed.connect(_show_delete_confirm)
	second_row.add_child(delete_button)

	_msg_label = UITheme.make_label("", UITheme.FONT_CAPTION, UITheme.DANGER, HORIZONTAL_ALIGNMENT_LEFT)
	_msg_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	content.add_child(_msg_label)

	_username_line.text_changed.connect(func(_t: String) -> void: _open_dropdown())
	_username_line.focus_entered.connect(_open_dropdown)
	_username_line.focus_exited.connect(_close_dropdown)
	_username_line.max_length = USERNAME_MAX
	_password_line.max_length = PASSWORD_MAX
	# 密码框获得焦点时关闭下拉（B3）
	_password_line.focus_entered.connect(_close_dropdown)


func _make_line_edit(placeholder: String, secret: bool) -> LineEdit:
	var line := LineEdit.new()
	line.placeholder_text = placeholder
	line.secret = secret
	line.custom_minimum_size = Vector2(0.0, 52.0)
	line.add_theme_font_override("font", UITheme.FONT)
	line.add_theme_font_size_override("font_size", UITheme.FONT_BODY)
	return line


## 用户下拉：list_usernames 前 4 项（B7-13 修复：选中即关闭；点密码框/失焦关闭）
func _open_dropdown() -> void:
	_close_dropdown()
	var names := GameState.list_usernames()
	if names.is_empty():
		return
	var shown := names.slice(0, DROPDOWN_MAX)
	_dropdown = Panel.new()
	_dropdown.position = _username_line.global_position + Vector2(0.0, _username_line.size.y + 4.0)
	_dropdown.size = Vector2(_username_line.size.x, shown.size() * 44.0 + 8.0)
	_dropdown.z_index = 50
	add_child(_dropdown)
	var list := VBoxContainer.new()
	list.set_anchors_preset(Control.PRESET_FULL_RECT)
	list.add_theme_constant_override("separation", 0)
	_dropdown.add_child(list)
	_dropdown_buttons.clear()
	for name in shown:
		var b := Button.new()
		b.text = name
		b.alignment = HORIZONTAL_ALIGNMENT_LEFT
		b.add_theme_font_override("font", UITheme.FONT)
		b.add_theme_font_size_override("font_size", UITheme.FONT_BODY)
		UITheme.apply_button(b)
		b.pressed.connect(_on_dropdown_pick.bind(name))
		list.add_child(b)
		_dropdown_buttons.append(b)


func _close_dropdown() -> void:
	if _dropdown != null:
		_dropdown.queue_free()
		_dropdown = null
	_dropdown_buttons.clear()


func _on_dropdown_pick(name: String) -> void:
	_username_line.text = name
	_password_line.clear()
	_close_dropdown()
	_password_line.grab_focus()  # B3：选中填入 → 焦点到密码框


## 最近登录用户预填（B3）：last-login 预填用户名、焦点落密码框
func _prefill_last_login() -> void:
	var last := GameState.get_last_login_user()
	if last != "":
		_username_line.text = last
		_password_line.grab_focus()
	else:
		_username_line.grab_focus()


func _show_msg(text: String, is_error: bool) -> void:
	_msg_label.text = text
	_msg_label.add_theme_color_override("font_color", UITheme.DANGER if is_error else UITheme.ACCENT_GOLD)
	# 2s 自动清除（B3 对齐 120 帧）
	if _msg_timer != null and _msg_timer.time_left > 0.0:
		_msg_timer.time_left = 0.0
	_msg_timer = get_tree().create_timer(2.0)
	_msg_timer.timeout.connect(func() -> void: _msg_label.text = "")


# ---------------- 登录/注册/游客/删除 动作 ----------------


## ENTER 登录路径（B7-5 修复：任一字段为空 → 游客确认框，默认焦点「返回」）
func _do_login() -> void:
	var name := _username_line.text.strip_edges()
	var password := _password_line.text
	if name == "" or password == "":
		_show_guest_confirm()
		return
	if not GameState.verify_user(name, password):
		_show_msg(tr("WELCOME_MSG_BAD_CRED"), true)
		return
	_enter_main_zone(true, name)


func _do_register() -> void:
	var name := _username_line.text.strip_edges()
	var password := _password_line.text
	if name == "" or password == "":
		_show_msg(tr("WELCOME_MSG_EMPTY_CRED"), true)
		return
	if name.length() < UserDB.NAME_MIN or name.length() > UserDB.NAME_MAX:
		_show_msg(tr("WELCOME_MSG_NAME_LEN"), true)
		return
	if password.length() < UserDB.PASSWORD_MIN or password.length() > UserDB.PASSWORD_MAX:
		_show_msg(tr("WELCOME_MSG_PASS_LEN"), true)
		return
	if not GameState.create_user(name, password):
		_show_msg(tr("WELCOME_MSG_NAME_TAKEN"), true)
		return
	_show_msg(tr("WELCOME_MSG_REGISTER_OK"), false)
	_password_line.clear()  # B7-9：注册成功保留刚注册的用户名，只清密码


func _show_guest_confirm() -> void:
	_guest_confirm["layer"].visible = true
	(_guest_confirm["cancel"] as Button).grab_focus()  # B7-5 默认焦点「返回」


func _confirm_guest() -> void:
	_guest_confirm["layer"].visible = false
	_enter_main_zone(false, "")


func _show_delete_confirm() -> void:
	_close_dropdown()
	var name := _username_line.text.strip_edges()
	if name == "":
		_show_msg(tr("WELCOME_MSG_DELETE_EMPTY"), true)
		return
	if _password_line.text == "":
		_show_msg(tr("WELCOME_MSG_DELETE_PASS"), true)  # B7-13：确认时先验密码非空
		return
	_delete_confirm["layer"].visible = true
	(_delete_confirm["cancel"] as Button).grab_focus()  # 默认焦点「取消」


func _confirm_delete() -> void:
	_delete_confirm["layer"].visible = false
	var name := _username_line.text.strip_edges()
	if GameState.delete_user(name, _password_line.text):
		_username_line.clear()
		_password_line.clear()
		_close_dropdown()
		_show_msg(tr("WELCOME_MSG_DELETED"), false)
	else:
		_show_msg(tr("WELCOME_MSG_BAD_CRED"), true)


## 登录/游客放行：隐藏登录面板，显示主区（B1）
func _enter_main_zone(is_user: bool, name: String) -> void:
	if is_user:
		GameState.login_user(name)
	else:
		GameState.login_guest()
	_stage = Stage.MAIN
	_login_panel.visible = false
	_main_zone.visible = true
	_refresh_texts()
	_grab_main_focus()


# ---------------- 主区（登录后） ----------------


func _build_main_zone() -> void:
	_main_zone = VBoxContainer.new()
	_main_zone.set_anchors_preset(Control.PRESET_CENTER_LEFT)
	_main_zone.position = Vector2(140.0, 260.0)
	_main_zone.custom_minimum_size = Vector2(520.0, 0.0)
	_main_zone.add_theme_constant_override("separation", 14)
	_main_zone.visible = false
	add_child(_main_zone)

	var diff_header := UITheme.make_section_header(tr("START_DIFFICULTY"))
	_main_zone.add_child(diff_header)
	var diff_row := HBoxContainer.new()
	diff_row.add_theme_constant_override("separation", 12)
	_main_zone.add_child(diff_row)
	for d in GameState.DIFFICULTY_ORDER:
		var b := Button.new()
		b.text = tr("DIFF_" + String(d).to_upper())
		b.toggle_mode = true
		b.button_group = _diff_group
		b.custom_minimum_size = Vector2(120.0, 52.0)
		b.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		b.add_theme_font_override("font", UITheme.FONT)
		b.add_theme_font_size_override("font_size", UITheme.FONT_BODY)
		UITheme.apply_button(b)
		b.pressed.connect(_on_difficulty_pressed.bind(d))
		diff_row.add_child(b)
		_diff_buttons[d] = b

	_continue_button = UITheme.make_button(tr("START_CONTINUE"), true)
	_continue_button.custom_minimum_size = Vector2(0.0, 64.0)
	_continue_button.pressed.connect(_on_continue_pressed)
	_main_zone.add_child(_continue_button)
	_new_button = UITheme.make_button(tr("START_BEGIN"))
	_new_button.custom_minimum_size = Vector2(0.0, 56.0)
	_new_button.pressed.connect(_on_new_game_pressed)
	_main_zone.add_child(_new_button)
	_tutorial_button = UITheme.make_button(tr("START_TUTORIAL"))
	_tutorial_button.custom_minimum_size = Vector2(0.0, 56.0)
	_tutorial_button.pressed.connect(_on_tutorial_pressed)
	_main_zone.add_child(_tutorial_button)
	_settings_button = UITheme.make_button(tr("START_SETTINGS"))
	_settings_button.custom_minimum_size = Vector2(0.0, 56.0)
	_settings_button.pressed.connect(_on_settings_pressed)
	_main_zone.add_child(_settings_button)
	_leaderboard_button = UITheme.make_button(tr("WELCOME_LEADERBOARD"))
	_leaderboard_button.custom_minimum_size = Vector2(0.0, 56.0)
	_leaderboard_button.pressed.connect(_open_leaderboard)
	_main_zone.add_child(_leaderboard_button)


func _grab_main_focus() -> void:
	if _continue_button.visible:
		_continue_button.grab_focus()
	else:
		_new_button.grab_focus()


## 主按钮重获焦点（设置返回/退出确认取消后）
func grab_primary_focus() -> void:
	if _stage == Stage.MAIN:
		_grab_main_focus()
	else:
		_username_line.grab_focus()


func _on_difficulty_pressed(d: StringName) -> void:
	GameState.set_difficulty(d)


func _on_continue_pressed() -> void:
	_goto_main()


func _on_new_game_pressed() -> void:
	GameState.delete_save()
	_goto_main()


func _on_tutorial_pressed() -> void:
	# E02/G03：存在进行中存档时禁入教程（UI 已禁用按钮，此处兜底）
	if GameState.has_save():
		return
	get_tree().change_scene_to_file("res://scenes/tutorial.tscn")


func _on_settings_pressed() -> void:
	var settings := get_tree().get_first_node_in_group("settings_ui")
	if settings == null:
		return
	visible = false  # 面板遮挡：先隐藏自己（对齐 StartPanel 行为）
	settings.show_settings(self)


func _goto_main() -> void:
	get_tree().change_scene_to_file("res://scenes/main.tscn")


# ---------------- Overlay：排行榜 / 游客确认 / 删除确认 / 退出确认 ----------------


func _build_overlays() -> void:
	# 排行榜（B6）：遮罩 + 520×580 面板 + 最多 10 行 + 页脚 + ×关闭；打开时重新读取（B7-13）
	_leaderboard_overlay = CanvasLayer.new()
	_leaderboard_overlay.layer = 50
	_leaderboard_overlay.visible = false
	add_child(_leaderboard_overlay)
	var shell := UITheme.make_page_shell("LEAD_TITLE")
	_leaderboard_overlay.add_child(shell["root"])
	(shell["panel"] as ChamferedPanel).custom_minimum_size = Vector2(520.0, 580.0)
	_leaderboard_rows = VBoxContainer.new()
	_leaderboard_rows.add_theme_constant_override("separation", 6)
	(shell["content"] as VBoxContainer).add_child(_leaderboard_rows)
	var footer := UITheme.make_label(tr("LEAD_FOOTER"), UITheme.FONT_CAPTION, UITheme.TEXT_DIM, HORIZONTAL_ALIGNMENT_CENTER)
	(shell["content"] as VBoxContainer).add_child(footer)
	var close_button := UITheme.make_button(tr("LEAD_CLOSE"))
	close_button.custom_minimum_size = Vector2(200.0, 48.0)
	close_button.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	close_button.pressed.connect(_close_leaderboard)
	(shell["content"] as VBoxContainer).add_child(close_button)
	shell["content"].add_theme_constant_override("separation", 12)

	# 游客确认（B7-6：游客按钮与 ENTER 路径统一走确认框）
	_guest_confirm = _make_modal(
		"WELCOME_GUEST_CONFIRM_TITLE", "WELCOME_GUEST_CONFIRM", "WELCOME_CONFIRM_GO", "WELCOME_CONFIRM_BACK", _confirm_guest
	)
	# 删除确认（B7-2/3：ESC 关闭确认框、鼠标键盘双模态）
	_delete_confirm = _make_modal(
		"WELCOME_DELETE_CONFIRM_TITLE", "WELCOME_DELETE_CONFIRM", "WELCOME_CONFIRM_YES", "WELCOME_CONFIRM_CANCEL", _confirm_delete
	)
	# 退出确认（welcome 是首场景，ESC=退出游戏；battle=false 保留存档）
	_exit_confirm = _make_modal("EXIT_TITLE", "WELCOME_EXIT_MSG", "EXIT_OK", "EXIT_CANCEL", _on_exit_ok)


## 轻量模态工厂：page_shell 风格 + 确认/取消行；返回 {"layer", "ok", "cancel"} 引用结构
func _make_modal(title_key: String, msg_key: String, ok_key: String, cancel_key: String, ok_cb: Callable) -> Dictionary:
	var layer := CanvasLayer.new()
	layer.layer = 60
	layer.visible = false
	add_child(layer)
	var shell := UITheme.make_page_shell(title_key)
	layer.add_child(shell["root"])
	(shell["panel"] as ChamferedPanel).custom_minimum_size = Vector2(560.0, 300.0)
	shell["content"].add_theme_constant_override("separation", 18)
	var msg := UITheme.make_label(tr(msg_key), UITheme.FONT_BODY, UITheme.TEXT)
	msg.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	shell["content"].add_child(msg)
	var row := HBoxContainer.new()
	row.alignment = BoxContainer.ALIGNMENT_CENTER
	row.add_theme_constant_override("separation", 24)
	shell["content"].add_child(row)
	var cancel_button := UITheme.make_button(tr(cancel_key))
	cancel_button.custom_minimum_size = Vector2(200.0, 56.0)
	row.add_child(cancel_button)
	var ok_button := UITheme.make_button(tr(ok_key))
	ok_button.custom_minimum_size = Vector2(200.0, 56.0)
	ok_button.pressed.connect(ok_cb)
	row.add_child(ok_button)
	var result := {"layer": layer, "ok": ok_button, "cancel": cancel_button}
	cancel_button.pressed.connect(func() -> void: _close_modal_ref(result))
	return result


func _close_modal_ref(modal: Dictionary) -> void:
	modal["layer"].visible = false
	# 焦点还给来源：主区主按钮 / 登录用户名框
	grab_primary_focus()


func _open_leaderboard() -> void:
	# B7-13 修复：overlay 每次打开重新读取榜单（不作 10s 缓存）
	for child in _leaderboard_rows.get_children():
		child.queue_free()
	var board := GameState.get_leaderboard()
	if board.is_empty():
		_leaderboard_rows.add_child(UITheme.make_label(tr("LEAD_EMPTY"), UITheme.FONT_BODY, UITheme.TEXT_DIM, HORIZONTAL_ALIGNMENT_CENTER))
	for i in mini(10, board.size()):
		var entry := board[i] as Dictionary
		var color := UITheme.TEXT
		if i == 0:
			color = UITheme.ACCENT_GOLD
		elif i == 1:
			color = UITheme.ACCENT
		elif i == 2:
			color = UITheme.TEXT_DIM
		var line := UITheme.make_label(
			"%d. %s  %d" % [i + 1, String(entry.get("player_name", "")), int(entry.get("score", 0))],
			UITheme.FONT_BODY,
			color,
			HORIZONTAL_ALIGNMENT_LEFT
		)
		_leaderboard_rows.add_child(line)
	_leaderboard_overlay.visible = true


func _close_leaderboard() -> void:
	_leaderboard_overlay.visible = false
	grab_primary_focus()


func _on_exit_ok() -> void:
	_exit_confirm["layer"].visible = false
	GameState.save_profile()  # 登录用户设置落盘（battle=false 保留存档）
	get_tree().quit()


# ---------------- 输入 / 文本刷新 ----------------


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		if _leaderboard_overlay.visible:
			_close_leaderboard()
		elif _guest_confirm["layer"].visible:
			_close_modal_ref(_guest_confirm)
		elif _delete_confirm["layer"].visible:
			_close_modal_ref(_delete_confirm)
		elif _exit_confirm["layer"].visible:
			_close_modal_ref(_exit_confirm)
		elif _dropdown != null:
			_close_dropdown()
		else:
			_exit_confirm["layer"].visible = true
			(_exit_confirm["cancel"] as Button).grab_focus()
		get_viewport().set_input_as_handled()
		return
	# ENTER 分派（B7-13 修复：键盘 ENTER 按焦点分派；输入框 = 登录/游客路径，按钮自处理）
	if _stage == Stage.LOGIN and event.is_action_pressed("ui_accept"):
		if _username_line.has_focus() or _password_line.has_focus():
			_do_login()
			get_viewport().set_input_as_handled()


func _refresh_texts() -> void:
	var has_save := GameState.has_save()
	_high_score_label.visible = GameState.high_score > 0
	_high_score_label.text = tr("WELCOME_HIGH_SCORE") % GameState.high_score
	var board := GameState.highscores_text(3)
	_board_label.visible = board != ""
	_board_label.text = tr("START_BOARD") + "\n" + board
	_corrupt_label.visible = GameState.save_corrupt or GameState.profile_corrupt
	_corrupt_label.text = (
		tr("START_PROFILE_CORRUPT") if GameState.profile_corrupt and not GameState.save_corrupt else tr("START_SAVE_CORRUPT")
	)
	_continue_button.visible = _stage == Stage.MAIN and has_save
	_new_button.text = tr("START_NEW") if has_save else tr("START_BEGIN")
	if _stage == Stage.MAIN:
		# 主按钮层级：有存档=继续对局 primary；无存档=开始游戏 primary
		if has_save:
			UITheme.apply_primary_button(_continue_button)
			UITheme.apply_button(_new_button)
			_new_button.add_theme_font_size_override("font_size", UITheme.FONT_BODY)
		else:
			UITheme.apply_primary_button(_new_button)
	# E02/G03 + P1-6：进行中存档时禁用教程按钮（重进会删档）；已通关无存档时放行
	_tutorial_button.disabled = _stage == Stage.MAIN and has_save
	for d in _diff_buttons:
		(_diff_buttons[d] as Button).text = tr("DIFF_" + String(d).to_upper())
		(_diff_buttons[d] as Button).set_pressed_no_signal(GameState.difficulty == d)


func _build_esc_hint() -> void:
	var esc_hint := UITheme.make_label(
		tr("START_ESC_HINT") + "    " + tr("WELCOME_TAB_HINT"), UITheme.FONT_CAPTION, UITheme.TEXT_DIM, HORIZONTAL_ALIGNMENT_RIGHT
	)
	esc_hint.set_anchors_preset(Control.PRESET_BOTTOM_RIGHT)
	esc_hint.position = Vector2(-420.0, -50.0)
	esc_hint.custom_minimum_size = Vector2(360.0, 0.0)
	add_child(esc_hint)
	GameState.locale_changed.connect(func() -> void: esc_hint.text = tr("START_ESC_HINT") + "    " + tr("WELCOME_TAB_HINT"))


# ---------------- 测试/诊断公开接口（A7 约定） ----------------


func username_line() -> LineEdit:
	return _username_line


func corrupt_label() -> Label:
	return _corrupt_label


func continue_button() -> Button:
	return _continue_button


func new_button() -> Button:
	return _new_button


func tutorial_button() -> Button:
	return _tutorial_button


func password_line() -> LineEdit:
	return _password_line


func press_login() -> void:
	_do_login()


func press_register() -> void:
	_do_register()


func press_guest() -> void:
	_show_guest_confirm()


func confirm_guest() -> void:
	_confirm_guest()


func press_delete() -> void:
	_show_delete_confirm()


func confirm_delete() -> void:
	_confirm_delete()


func press_leaderboard() -> void:
	_open_leaderboard()


func close_leaderboard() -> void:
	_close_leaderboard()


func press_new_game() -> void:
	_on_new_game_pressed()


func press_continue() -> void:
	_on_continue_pressed()


func press_tutorial() -> void:
	_on_tutorial_pressed()


func press_settings() -> void:
	_on_settings_pressed()


func main_zone_visible() -> bool:
	return _stage == Stage.MAIN


func leaderboard_overlay() -> CanvasLayer:
	return _leaderboard_overlay


func guest_confirm() -> CanvasLayer:
	return _guest_confirm["layer"]


func delete_confirm() -> CanvasLayer:
	return _delete_confirm["layer"]


func exit_confirm_layer() -> CanvasLayer:
	return _exit_confirm["layer"]
