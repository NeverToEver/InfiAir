extends CanvasLayer
## 设置界面：左侧导航三页——「控制」（可改键表 + 恢复默认）、
## 「操作模式」（Ctrl/Shift 按住切换）、「关于」（版本与操作速查）。
## 改键：点「改键」进入捕获态，下一按键即绑定（Esc 取消），冲突键从占用者移除。

signal back_pressed

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")

var _ctrl_hold: Button
var _ctrl_toggle: Button
var _shift_hold: Button
var _shift_toggle: Button
var _ctrl_group := ButtonGroup.new()
var _shift_group := ButtonGroup.new()
var _lang_group := ButtonGroup.new()
var _lang_zh: Button
var _lang_en: Button
var _version_label: Label
var _cheatsheet_label: Label

var _pages: Dictionary = {}  # 页名 -> Control
var _nav_buttons: Dictionary = {}
var _rebind_rows: Dictionary = {}  # action -> {"keys": Label, "button": Button}
var _hint_label: Label
var _capturing_action: StringName = &""
var _title_label: Label
var _back_button: Button
var _reset_button: Button
var _nav_group := ButtonGroup.new()


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

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 16)
	center.add_child(vbox)

	var title := _make_label(tr("SET_TITLE"), 44)
	_title_label = title
	title.add_theme_color_override("font_color", UITheme.ACCENT)
	vbox.add_child(title)

	var body := HBoxContainer.new()
	body.add_theme_constant_override("separation", 20)
	vbox.add_child(body)

	# 左侧导航
	var nav := VBoxContainer.new()
	nav.add_theme_constant_override("separation", 8)
	body.add_child(nav)
	for page_id in [&"controls", &"modes", &"about"]:
		var b := Button.new()
		b.toggle_mode = true
		b.button_group = _nav_group
		b.custom_minimum_size = Vector2(140.0, 48.0)
		b.add_theme_font_override("font", FONT)
		b.add_theme_font_size_override("font_size", 24)
		UITheme.apply_button(b)
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

	_hint_label = _make_label("", 20)
	_hint_label.add_theme_color_override("font_color", UITheme.ACCENT_GOLD)
	vbox.add_child(_hint_label)

	_back_button = Button.new()
	var back_button := _back_button
	back_button.text = tr("SET_BACK")
	back_button.custom_minimum_size = Vector2(200.0, 52.0)
	back_button.add_theme_font_override("font", FONT)
	back_button.add_theme_font_size_override("font_size", 26)
	UITheme.apply_button(back_button)
	back_button.pressed.connect(_on_back_pressed)
	vbox.add_child(back_button)

	GameState.key_bindings_changed.connect(_refresh_rebind_rows)
	GameState.locale_changed.connect(_on_locale_changed)


func _make_label(text: String, size: int) -> Label:
	var label := Label.new()
	label.text = text
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.add_theme_font_override("font", FONT)
	label.add_theme_font_size_override("font_size", size)
	return label


# ---------------- 控制（改键） ----------------

func _build_controls_page() -> VBoxContainer:
	var page := VBoxContainer.new()
	page.add_theme_constant_override("separation", 6)
	for action in GameState.REBINDABLE_ACTIONS:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 12)
		var name_label := _make_label(tr("ACT_" + String(action).to_upper()), 22)
		name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_LEFT
		name_label.custom_minimum_size = Vector2(180.0, 0.0)
		row.add_child(name_label)
		var keys_label := _make_label("", 22)
		keys_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_LEFT
		keys_label.custom_minimum_size = Vector2(280.0, 0.0)
		keys_label.add_theme_color_override("font_color", UITheme.TEXT_DIM)
		row.add_child(keys_label)
		var rebind_button := Button.new()
		rebind_button.text = tr("SET_REBIND")
		rebind_button.custom_minimum_size = Vector2(110.0, 40.0)
		rebind_button.add_theme_font_override("font", FONT)
		rebind_button.add_theme_font_size_override("font_size", 20)
		UITheme.apply_button(rebind_button)
		rebind_button.pressed.connect(_start_capture.bind(action))
		row.add_child(rebind_button)
		page.add_child(row)
		_rebind_rows[action] = {"keys": keys_label, "button": rebind_button, "name": name_label}
	_reset_button = Button.new()
	var reset_button := _reset_button
	reset_button.text = tr("SET_RESET")
	reset_button.custom_minimum_size = Vector2(220.0, 44.0)
	reset_button.add_theme_font_override("font", FONT)
	reset_button.add_theme_font_size_override("font_size", 22)
	UITheme.apply_button(reset_button)
	reset_button.pressed.connect(_on_reset_keys)
	page.add_child(reset_button)
	return page


func _refresh_rebind_rows() -> void:
	for action in _rebind_rows:
		(_rebind_rows[action]["keys"] as Label).text = GameState.action_keys_text(action)
		(_rebind_rows[action]["name"] as Label).text = tr("ACT_" + String(action).to_upper())


func _start_capture(action: StringName) -> void:
	_capturing_action = action
	_hint_label.text = tr("SET_CAPTURE") % tr("ACT_" + String(action).to_upper())


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
	page.add_theme_constant_override("separation", 20)
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
	var lang_row := HBoxContainer.new()
	lang_row.add_theme_constant_override("separation", 16)
	page.add_child(lang_row)
	var lang_label := _make_label(tr("SET_LANGUAGE"), 26)
	lang_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_LEFT
	lang_label.custom_minimum_size = Vector2(240.0, 0.0)
	lang_row.add_child(lang_label)
	_lang_zh = _make_mode_button("中文", _lang_group)
	_lang_en = _make_mode_button("English", _lang_group)
	lang_row.add_child(_lang_zh)
	lang_row.add_child(_lang_en)
	_lang_zh.pressed.connect(GameState.set_locale.bind("zh"))
	_lang_en.pressed.connect(GameState.set_locale.bind("en"))
	return page


func _make_mode_row(parent: Container, label_text: String, group: ButtonGroup) -> Array[Button]:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 16)
	parent.add_child(row)
	var label := _make_label(label_text, 26)
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_LEFT
	label.custom_minimum_size = Vector2(240.0, 0.0)
	row.add_child(label)
	var hold := _make_mode_button(tr("SET_HOLD"), group)
	var toggle := _make_mode_button(tr("SET_TOGGLE"), group)
	row.add_child(hold)
	row.add_child(toggle)
	return [hold, toggle]


func _make_mode_button(text: String, group: ButtonGroup) -> Button:
	var b := Button.new()
	UITheme.apply_button(b)
	b.text = text
	b.toggle_mode = true
	b.button_group = group
	b.custom_minimum_size = Vector2(110.0, 48.0)
	b.add_theme_font_override("font", FONT)
	b.add_theme_font_size_override("font_size", 24)
	return b


# ---------------- 关于 ----------------

func _build_about_page() -> VBoxContainer:
	var page := VBoxContainer.new()
	page.add_theme_constant_override("separation", 10)
	_version_label = _make_label(tr("SET_VERSION") % Engine.get_version_info().string, 22)
	_version_label.add_theme_color_override("font_color", UITheme.ACCENT_GOLD)
	page.add_child(_version_label)
	_cheatsheet_label = _make_label(tr("SET_CHEATSHEET"), 20)
	_cheatsheet_label.add_theme_color_override("font_color", UITheme.TEXT_DIM)
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


## 打开面板并刷新选中态
func show_settings() -> void:
	_ctrl_hold.set_pressed_no_signal(not GameState.ctrl_toggle_mode)
	_ctrl_toggle.set_pressed_no_signal(GameState.ctrl_toggle_mode)
	_shift_hold.set_pressed_no_signal(not GameState.shift_toggle_mode)
	_shift_toggle.set_pressed_no_signal(GameState.shift_toggle_mode)
	_refresh_rebind_rows()
	_refresh_lang_buttons()
	_hint_label.text = ""
	_capturing_action = &""
	_show_page(&"controls")
	visible = true


func _refresh_lang_buttons() -> void:
	_lang_zh.set_pressed_no_signal(GameState.locale == "zh")
	_lang_en.set_pressed_no_signal(GameState.locale == "en")


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


func _on_ctrl_mode(toggle_mode: bool) -> void:
	GameState.set_ctrl_toggle_mode(toggle_mode)


func _on_shift_mode(toggle_mode: bool) -> void:
	GameState.set_shift_toggle_mode(toggle_mode)


func _on_back_pressed() -> void:
	_capturing_action = &""
	visible = false
	back_pressed.emit()
