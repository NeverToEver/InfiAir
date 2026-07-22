extends CanvasLayer
## Esc 暂停面板：继续 / 保存进度 / 设置 / 退出游戏 / 重开提示。
## 「保存进度」是全局唯一主动存档入口；「设置」打开 Ctrl/Shift 模式面板。
## ui_cancel（Esc/手柄 B）的全局返回路由统一在 BackNavigator（见 docs/EXIT_FLOW.md），
## 本面板只提供 open()/close() 供其调用；「退出游戏」走 ExitConfirm 战斗模式二次确认。

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")

var _save_button: Button
var _settings_button: Button
var _quit_button: Button
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

	_quit_button = Button.new()
	_quit_button.text = tr("PAUSE_QUIT")
	_quit_button.custom_minimum_size = Vector2(240.0, 52.0)
	_quit_button.add_theme_font_override("font", FONT)
	_quit_button.add_theme_font_size_override("font_size", 26)
	UITheme.apply_button(_quit_button)
	_quit_button.pressed.connect(_on_quit_pressed)
	vbox.add_child(_quit_button)

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
	_quit_button.text = tr("PAUSE_QUIT")
	if _save_button.text != tr("PAUSE_SAVED"):
		_save_button.text = tr("PAUSE_SAVE")


func open() -> void:
	_save_button.text = tr("PAUSE_SAVE")
	get_tree().paused = true
	visible = true
	UITheme.animate_open(_plate)


func close() -> void:
	visible = false
	get_tree().paused = false


func toggle() -> void:
	if visible:
		close()
	else:
		open()


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


func _on_quit_pressed() -> void:
	# 战斗中退出：ExitConfirm 战斗模式二次确认（带进度损失警告）
	var exit_confirm: CanvasLayer = get_parent().get_node("ExitConfirm")
	exit_confirm.show_confirm(true)


func _unhandled_input(event: InputEvent) -> void:
	# ui_cancel（Esc/手柄 B/Android 返回）的全局路由已移交 BackNavigator；
	# 此处只保留暂停中的 R 重开
	if visible and event.is_action_pressed("restart"):
		get_tree().paused = false
		GameState.reset_run()
		get_tree().reload_current_scene()
