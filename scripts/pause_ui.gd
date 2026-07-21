extends CanvasLayer
## Esc 暂停面板：继续 / 保存进度 / 设置 / 重开提示。
## 「保存进度」是全局唯一主动存档入口；「设置」打开 Ctrl/Shift 模式面板。

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")

var _save_button: Button
var _settings_button: Button
var _title_label: Label
var _hint_label: Label
var _plate: ChamferedPanel
var _settings_ui: CanvasLayer  # 惰性绑定（SettingsUI 的 _ready 晚于本节点）


func _ready() -> void:
	visible = false
	GameState.locale_changed.connect(_on_locale_changed)
	var dim := ColorRect.new()
	dim.color = Color(0.0, 0.0, 0.0, 0.5)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(dim)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	_plate = ChamferedPanel.new()
	_plate.custom_minimum_size = Vector2(560.0, 420.0)
	_plate.brackets = true
	center.add_child(_plate)

	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	_plate.add_child(margin)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 24)
	vbox.alignment = BoxContainer.ALIGNMENT_CENTER
	margin.add_child(vbox)

	var title := Label.new()
	title.text = tr("PAUSE_TITLE")
	_title_label = title
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_override("font", FONT)
	title.add_theme_font_size_override("font_size", 48)
	vbox.add_child(title)

	_save_button = Button.new()
	_save_button.text = tr("PAUSE_SAVE")
	_save_button.custom_minimum_size = Vector2(240.0, 52.0)
	_save_button.add_theme_font_override("font", FONT)
	_save_button.add_theme_font_size_override("font_size", 26)
	UITheme.apply_button(_save_button)
	_save_button.pressed.connect(_on_save_pressed)
	vbox.add_child(_save_button)

	_settings_button = Button.new()
	var settings_button := _settings_button
	settings_button.text = tr("PAUSE_SETTINGS")
	settings_button.custom_minimum_size = Vector2(240.0, 52.0)
	settings_button.add_theme_font_override("font", FONT)
	settings_button.add_theme_font_size_override("font_size", 26)
	settings_button.pressed.connect(_on_settings_pressed)
	vbox.add_child(settings_button)

	var hint := Label.new()
	hint.text = tr("PAUSE_HINT")
	_hint_label = hint
	hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	hint.add_theme_font_override("font", FONT)
	hint.add_theme_font_size_override("font_size", 26)
	vbox.add_child(hint)


func _on_locale_changed() -> void:
	_title_label.text = tr("PAUSE_TITLE")
	_hint_label.text = tr("PAUSE_HINT")
	_settings_button.text = tr("PAUSE_SETTINGS")
	if _save_button.text != tr("PAUSE_SAVED"):
		_save_button.text = tr("PAUSE_SAVE")


func toggle() -> void:
	# 设置面板打开时，Esc = 返回（由设置面板恢复其打开者：本面板或开始面板）
	if _get_settings_ui() != null and _settings_ui.visible:
		_settings_ui._on_back_pressed()
		return
	if visible:
		visible = false
		get_tree().paused = false
	else:
		_save_button.text = tr("PAUSE_SAVE")
		get_tree().paused = true
		visible = true
		UITheme.animate_open(_plate)


func _get_settings_ui() -> CanvasLayer:
	if _settings_ui == null:
		_settings_ui = get_tree().get_first_node_in_group("settings_ui") as CanvasLayer
	return _settings_ui


func _on_settings_pressed() -> void:
	if _get_settings_ui() == null:
		return
	visible = false
	_settings_ui.show_settings(self)


func _on_save_pressed() -> void:
	var player := get_tree().get_first_node_in_group("player")
	var spawner := get_tree().get_first_node_in_group("spawner")
	var fuel: float = player._fuel if player != null else 100.0
	var elapsed: float = spawner._elapsed if spawner != null else 0.0
	GameState.save_run(fuel, elapsed)
	_save_button.text = tr("PAUSE_SAVED")
	await get_tree().create_timer(1.0).timeout
	_save_button.text = tr("PAUSE_SAVE")


func _unhandled_input(event: InputEvent) -> void:
	# Esc 暂停/恢复路由必须挂在本节点（process_mode=Always）：
	# 树暂停后 main 等 INHERIT 节点的 _unhandled_input 不再被调用
	if event.is_action_pressed("ui_cancel"):
		var main := get_tree().get_first_node_in_group("main")
		if main != null and not main._game_over and not main._homecoming and not main._buff_ui.visible:
			toggle()
			get_viewport().set_input_as_handled()
		return
	if visible and event.is_action_pressed("restart"):
		get_tree().paused = false
		GameState.reset_run()
		get_tree().reload_current_scene()
