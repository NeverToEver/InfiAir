extends CanvasLayer
## 设置界面：左侧导航三页——「控制」（可改键表 + 恢复默认）、
## 「操作模式」（Ctrl/Shift 按住切换、语言、视角缩放、窗口大小）、「关于」（版本与操作速查）。
## 改键：点「改键」进入捕获态，下一按键即绑定（Esc 取消），冲突键从占用者移除。

signal back_pressed

var _ctrl_hold: Button
var _ctrl_toggle: Button
var _shift_hold: Button
var _shift_toggle: Button
var _ctrl_group := ButtonGroup.new()
var _shift_group := ButtonGroup.new()
var _lang_group := ButtonGroup.new()
var _lang_zh: Button
var _lang_en: Button
var _zoom_group := ButtonGroup.new()
var _zoom_buttons: Dictionary = {}  # 视角档位 -> Button
var _aim_group := ButtonGroup.new()
var _aim_buttons: Dictionary = {}  # 瞄准辅助强度档位 -> Button
var _window_group := ButtonGroup.new()
var _window_buttons: Dictionary = {}  # 窗口尺寸档位 -> Button
var _reduce_flash_btn: Button  # 无障碍·减少闪光开关
var _version_label: Label
var _cheatsheet_label: Label
var _plate: ChamferedPanel

var _pages: Dictionary = {}  # 页名 -> Control
var _nav_buttons: Dictionary = {}
var _rebind_rows: Dictionary = {}  # action -> {"keys": Label, "button": Button}
var _hint_label: Label
var _capturing_action: StringName = &""
var _title_label: Label
var _back_button: Button
var _reset_button: Button
var _nav_group := ButtonGroup.new()
var _opener: CanvasLayer  # 打开者（开始/暂停面板），返回时恢复其可见


func _ready() -> void:
	add_to_group("settings_ui")
	visible = false
	process_mode = Node.PROCESS_MODE_ALWAYS
	var dim := ColorRect.new()
	dim.color = UITheme.DIM_BG
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(dim)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	_plate = ChamferedPanel.new()
	_plate.custom_minimum_size = Vector2(1000.0, 700.0)
	_plate.brackets = true
	center.add_child(_plate)

	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left", 20)
	margin.add_theme_constant_override("margin_right", 20)
	margin.add_theme_constant_override("margin_top", 16)
	margin.add_theme_constant_override("margin_bottom", 16)
	_plate.add_child(margin)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 16)
	margin.add_child(vbox)

	_title_label = UITheme.make_label(tr("SET_TITLE"), UITheme.FONT_TITLE, UITheme.ACCENT)
	vbox.add_child(_title_label)

	var body := HBoxContainer.new()
	body.add_theme_constant_override("separation", 20)
	vbox.add_child(body)

	# 左侧导航
	var nav := VBoxContainer.new()
	nav.add_theme_constant_override("separation", 8)
	body.add_child(nav)
	for page_id in [&"controls", &"modes", &"about"]:
		var b := UITheme.make_toggle_button("", _nav_group)
		b.custom_minimum_size = Vector2(140.0, 48.0)
		b.pressed.connect(_show_page.bind(page_id))
		nav.add_child(b)
		_nav_buttons[page_id] = b

	# 内容区
	var content := VBoxContainer.new()
	content.custom_minimum_size = Vector2(760.0, 480.0)
	content.add_theme_constant_override("separation", 12)
	body.add_child(content)
	_pages[&"controls"] = _build_controls_page()
	_pages[&"modes"] = _build_modes_page()
	_pages[&"about"] = _build_about_page()
	_refresh_nav_labels()
	for p in _pages.values():
		content.add_child(p)
		p.visible = false

	_hint_label = UITheme.make_label("", UITheme.FONT_CAPTION, UITheme.ACCENT_GOLD)
	vbox.add_child(_hint_label)

	_back_button = UITheme.make_button(tr("SET_BACK"))
	_back_button.custom_minimum_size = Vector2(200.0, 52.0)
	_back_button.pressed.connect(_on_back_pressed)
	vbox.add_child(_back_button)

	GameState.key_bindings_changed.connect(_refresh_rebind_rows)
	GameState.locale_changed.connect(_on_locale_changed)


# ---------------- 控制（改键） ----------------

func _build_controls_page() -> VBoxContainer:
	var page := VBoxContainer.new()
	page.add_theme_constant_override("separation", 6)
	for action in GameState.REBINDABLE_ACTIONS:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 12)
		var name_label := UITheme.make_label(
			tr("ACT_" + String(action).to_upper()), UITheme.FONT_BODY, UITheme.TEXT, HORIZONTAL_ALIGNMENT_LEFT
		)
		name_label.custom_minimum_size = Vector2(180.0, 0.0)
		row.add_child(name_label)
		var keys_label := UITheme.make_label("", UITheme.FONT_BODY, UITheme.TEXT_DIM, HORIZONTAL_ALIGNMENT_LEFT)
		keys_label.custom_minimum_size = Vector2(280.0, 0.0)
		row.add_child(keys_label)
		var rebind_button := UITheme.make_button(tr("SET_REBIND"))
		rebind_button.custom_minimum_size = Vector2(110.0, 40.0)
		rebind_button.add_theme_font_size_override("font_size", UITheme.FONT_CAPTION)
		rebind_button.pressed.connect(_start_capture.bind(action))
		row.add_child(rebind_button)
		page.add_child(row)
		_rebind_rows[action] = {"keys": keys_label, "button": rebind_button, "name": name_label}
	_reset_button = UITheme.make_button(tr("SET_RESET"))
	_reset_button.custom_minimum_size = Vector2(220.0, 44.0)
	_reset_button.pressed.connect(_on_reset_keys)
	page.add_child(_reset_button)
	return page


func _refresh_rebind_rows() -> void:
	for action in _rebind_rows:
		(_rebind_rows[action]["keys"] as Label).text = GameState.action_keys_text(action)
		(_rebind_rows[action]["name"] as Label).text = tr("ACT_" + String(action).to_upper())


func _start_capture(action: StringName) -> void:
	_capturing_action = action
	_hint_label.text = tr("SET_CAPTURE") % tr("ACT_" + String(action).to_upper())


## 对外公开接口（A1 修复）：BackNavigator 决策查询改键捕获态
func capturing_action() -> StringName:
	return _capturing_action


func _on_reset_keys() -> void:
	GameState.reset_key_bindings()
	_hint_label.text = tr("SET_RESET_DONE")


func _unhandled_input(event: InputEvent) -> void:
	if not visible or _capturing_action == &"":
		return
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_ESCAPE:
			_hint_label.text = tr("SET_CANCELLED")
		else:
			GameState.rebind_action(_capturing_action, event.keycode)
			_hint_label.text = tr("SET_BOUND") % [
				tr("ACT_" + String(_capturing_action).to_upper()),
				OS.get_keycode_string(event.keycode),
			]
		_capturing_action = &""
		get_viewport().set_input_as_handled()


# ---------------- 操作模式 ----------------

func _build_modes_page() -> VBoxContainer:
	var page := VBoxContainer.new()
	page.add_theme_constant_override("separation", 14)
	# 按键模式（Ctrl/Shift）
	page.add_child(UITheme.make_section_header(tr("SET_MODES")))
	var ctrl_pair := _make_mode_row(page, tr("SET_CTRL_MODE"), _ctrl_group)
	_ctrl_hold = ctrl_pair[0]
	_ctrl_toggle = ctrl_pair[1]
	_ctrl_hold.pressed.connect(_on_ctrl_mode.bind(false))
	_ctrl_toggle.pressed.connect(_on_ctrl_mode.bind(true))
	var shift_pair := _make_mode_row(page, tr("SET_SHIFT_MODE"), _shift_group)
	_shift_hold = shift_pair[0]
	_shift_toggle = shift_pair[1]
	_shift_hold.pressed.connect(_on_shift_mode.bind(false))
	_shift_toggle.pressed.connect(_on_shift_mode.bind(true))
	# 语言 / Language
	page.add_child(UITheme.make_section_header(tr("SET_LANGUAGE")))
	var lang_row := HBoxContainer.new()
	lang_row.add_theme_constant_override("separation", 16)
	page.add_child(lang_row)
	_lang_zh = UITheme.make_toggle_button("中文", _lang_group)
	_lang_en = UITheme.make_toggle_button("English", _lang_group)
	lang_row.add_child(_lang_zh)
	lang_row.add_child(_lang_en)
	_lang_zh.pressed.connect(GameState.set_locale.bind("zh"))
	_lang_en.pressed.connect(GameState.set_locale.bind("en"))
	# 辅助瞄准强度（常驻不可关，仅弱/中/强三档）
	page.add_child(UITheme.make_section_header(tr("SET_AIM_ASSIST")))
	var aim_row := HBoxContainer.new()
	aim_row.add_theme_constant_override("separation", 16)
	page.add_child(aim_row)
	_aim_buttons.clear()
	for level in GameState.AIM_ASSIST_ORDER:
		var ab := UITheme.make_toggle_button(tr("SET_AIM_" + String(level).to_upper()), _aim_group)
		ab.pressed.connect(GameState.set_aim_assist_level.bind(level))
		aim_row.add_child(ab)
		_aim_buttons[level] = ab
	# 机制说明（P1-1 新语义）：准星入标记框 → 出膛弹追踪该敌；档位调节框大小与追踪速度
	page.add_child(UITheme.make_label(tr("SET_AIM_ASSIST_DESC"), UITheme.FONT_CAPTION, UITheme.TEXT_DIM, HORIZONTAL_ALIGNMENT_LEFT))
	# 显示：视角缩放 + 窗口大小
	page.add_child(UITheme.make_section_header(tr("SET_DISPLAY")))
	var zoom_row := HBoxContainer.new()
	zoom_row.add_theme_constant_override("separation", 16)
	page.add_child(zoom_row)
	var zoom_label := UITheme.make_label(tr("SET_VIEW_ZOOM"), UITheme.FONT_BODY, UITheme.TEXT, HORIZONTAL_ALIGNMENT_LEFT)
	zoom_label.custom_minimum_size = Vector2(140.0, 0.0)
	zoom_row.add_child(zoom_label)
	_zoom_buttons.clear()
	for level in GameState.VIEW_ZOOM_ORDER:
		var b := UITheme.make_toggle_button(tr("SET_VIEW_" + String(level).to_upper()), _zoom_group)
		b.pressed.connect(GameState.set_view_zoom.bind(level))
		zoom_row.add_child(b)
		_zoom_buttons[level] = b
	# 窗口大小（按钮含分辨率文本，加宽）
	var win_row := HBoxContainer.new()
	win_row.add_theme_constant_override("separation", 16)
	page.add_child(win_row)
	var win_label := UITheme.make_label(tr("SET_WINDOW_SIZE"), UITheme.FONT_BODY, UITheme.TEXT, HORIZONTAL_ALIGNMENT_LEFT)
	win_label.custom_minimum_size = Vector2(140.0, 0.0)
	win_row.add_child(win_label)
	_window_buttons.clear()
	for level in GameState.WINDOW_SIZE_ORDER:
		var b := UITheme.make_toggle_button(tr("SET_WINDOW_" + String(level).to_upper()), _window_group)
		b.custom_minimum_size = Vector2(210.0, 48.0)
		b.pressed.connect(GameState.set_window_size.bind(level))
		win_row.add_child(b)
		_window_buttons[level] = b
	# 无障碍（Meta HUD）：减少闪光（色差 ×0.4、禁呼吸/抖动/心跳视觉脉冲，音效保留）
	page.add_child(UITheme.make_section_header(tr("SET_ACCESSIBILITY")))
	var rf_row := HBoxContainer.new()
	rf_row.add_theme_constant_override("separation", 16)
	page.add_child(rf_row)
	# 单开关 ButtonGroup 需 allow_unpress，否则按下后无法再取消
	var rf_group := ButtonGroup.new()
	rf_group.allow_unpress = true
	_reduce_flash_btn = UITheme.make_toggle_button(tr("SET_REDUCE_FLASH"), rf_group)
	_reduce_flash_btn.custom_minimum_size = Vector2(160.0, 48.0)
	_reduce_flash_btn.pressed.connect(_on_reduce_flash)
	rf_row.add_child(_reduce_flash_btn)
	return page


func _make_mode_row(parent: Container, label_text: String, group: ButtonGroup) -> Array[Button]:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 16)
	parent.add_child(row)
	var label := UITheme.make_label(label_text, UITheme.FONT_BODY, UITheme.TEXT, HORIZONTAL_ALIGNMENT_LEFT)
	label.custom_minimum_size = Vector2(240.0, 0.0)
	row.add_child(label)
	var hold := UITheme.make_toggle_button(tr("SET_HOLD"), group)
	var toggle := UITheme.make_toggle_button(tr("SET_TOGGLE"), group)
	row.add_child(hold)
	row.add_child(toggle)
	return [hold, toggle]


# ---------------- 关于 ----------------

func _build_about_page() -> VBoxContainer:
	var page := VBoxContainer.new()
	page.add_theme_constant_override("separation", 10)
	_version_label = UITheme.make_label(
		tr("SET_VERSION") % Engine.get_version_info().string, UITheme.FONT_BODY, UITheme.ACCENT_GOLD
	)
	page.add_child(_version_label)
	_cheatsheet_label = UITheme.make_label(tr("SET_CHEATSHEET"), UITheme.FONT_CAPTION, UITheme.TEXT_DIM)
	page.add_child(_cheatsheet_label)
	return page


# ---------------- 通用 ----------------

func _refresh_nav_labels() -> void:
	(_nav_buttons[&"controls"] as Button).text = tr("SET_CONTROLS")
	(_nav_buttons[&"modes"] as Button).text = tr("SET_MODES")
	(_nav_buttons[&"about"] as Button).text = tr("SET_ABOUT")


func _show_page(page_name: StringName) -> void:
	for p in _pages:
		_pages[p].visible = p == page_name
		(_nav_buttons[p] as Button).set_pressed_no_signal(p == page_name)


## 打开面板并刷新选中态；opener 为打开者（开始/暂停面板），返回时恢复其可见
func show_settings(opener: CanvasLayer = null) -> void:
	_opener = opener
	_ctrl_hold.set_pressed_no_signal(not GameState.ctrl_toggle_mode)
	_ctrl_toggle.set_pressed_no_signal(GameState.ctrl_toggle_mode)
	_shift_hold.set_pressed_no_signal(not GameState.shift_toggle_mode)
	_shift_toggle.set_pressed_no_signal(GameState.shift_toggle_mode)
	_refresh_rebind_rows()
	_refresh_lang_buttons()
	_refresh_zoom_buttons()
	_refresh_window_buttons()
	_refresh_aim_buttons()
	_reduce_flash_btn.set_pressed_no_signal(GameState.reduce_flash)
	_hint_label.text = ""
	_capturing_action = &""
	_show_page(&"controls")
	visible = true
	UITheme.animate_open(_plate)


func _refresh_lang_buttons() -> void:
	_lang_zh.set_pressed_no_signal(GameState.locale == "zh")
	_lang_en.set_pressed_no_signal(GameState.locale == "en")


func _refresh_zoom_buttons() -> void:
	for level in _zoom_buttons:
		(_zoom_buttons[level] as Button).set_pressed_no_signal(level == GameState.view_zoom)


func _refresh_window_buttons() -> void:
	for level in _window_buttons:
		(_window_buttons[level] as Button).set_pressed_no_signal(level == GameState.window_size)


func _refresh_aim_buttons() -> void:
	for level in _aim_buttons:
		(_aim_buttons[level] as Button).set_pressed_no_signal(level == GameState.aim_assist_level)


func _on_locale_changed() -> void:
	_title_label.text = tr("SET_TITLE")
	_back_button.text = tr("SET_BACK")
	_reset_button.text = tr("SET_RESET")
	_version_label.text = tr("SET_VERSION") % Engine.get_version_info().string
	_cheatsheet_label.text = tr("SET_CHEATSHEET")
	_refresh_rebind_rows()
	_refresh_lang_buttons()
	_refresh_nav_labels()
	# 重建内容区文本（重建代价低，保证全部文案换语言）
	var content: Container = _pages.values()[0].get_parent()
	for p in _pages.values():
		p.queue_free()
	_pages[&"controls"] = _build_controls_page()
	_pages[&"modes"] = _build_modes_page()
	_pages[&"about"] = _build_about_page()
	for p in _pages.values():
		content.add_child(p)
		p.visible = false
	_refresh_rebind_rows()
	_show_page(&"controls")
	# 操作模式按钮选中态刷新
	_ctrl_hold.set_pressed_no_signal(not GameState.ctrl_toggle_mode)
	_ctrl_toggle.set_pressed_no_signal(GameState.ctrl_toggle_mode)
	_shift_hold.set_pressed_no_signal(not GameState.shift_toggle_mode)
	_shift_toggle.set_pressed_no_signal(GameState.shift_toggle_mode)
	_refresh_zoom_buttons()
	_refresh_window_buttons()
	_refresh_aim_buttons()
	_reduce_flash_btn.set_pressed_no_signal(GameState.reduce_flash)


func _on_ctrl_mode(toggle_mode: bool) -> void:
	GameState.set_ctrl_toggle_mode(toggle_mode)


func _on_shift_mode(toggle_mode: bool) -> void:
	GameState.set_shift_toggle_mode(toggle_mode)


func _on_reduce_flash() -> void:
	GameState.set_reduce_flash(_reduce_flash_btn.button_pressed)


func _on_back_pressed() -> void:
	_capturing_action = &""
	visible = false
	if _opener != null and is_instance_valid(_opener):
		_opener.visible = true
	_opener = null
	back_pressed.emit()
