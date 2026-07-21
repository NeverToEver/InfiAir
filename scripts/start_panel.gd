extends CanvasLayer
## 开始面板：难度三选一（易/中/难，profile 持久化）+ 继续对局 / 新游戏。
## 无论有无存档，面板显示期间一律暂停游戏（冻结背景，先选再玩）；
## 无存档时开场自显，有存档时由 main 调 show_panel()。

signal continue_chosen
signal new_game_chosen

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")

var _hint_label: Label
var _diff_label: Label
var _continue_button: Button
var _new_button: Button
var _diff_buttons: Dictionary = {}  # StringName -> Button
var _diff_group := ButtonGroup.new()
var _tutorial_button: Button
var _settings_button: Button
var _plate: ChamferedPanel


func _ready() -> void:
	visible = false
	var dim := ColorRect.new()
	dim.color = Color(0.0, 0.0, 0.0, 0.75)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(dim)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	_plate = ChamferedPanel.new()
	_plate.custom_minimum_size = Vector2(680.0, 660.0)
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
	title.text = "InfiAir"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_override("font", FONT)
	title.add_theme_font_size_override("font_size", 56)
	title.add_theme_color_override("font_color", UITheme.ACCENT)
	vbox.add_child(title)

	_hint_label = Label.new()
	_hint_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_hint_label.add_theme_font_override("font", FONT)
	_hint_label.add_theme_font_size_override("font_size", 24)
	vbox.add_child(_hint_label)

	# 难度三选一（互斥，当前选中高亮）
	var diff_row := HBoxContainer.new()
	diff_row.alignment = BoxContainer.ALIGNMENT_CENTER
	diff_row.add_theme_constant_override("separation", 12)
	vbox.add_child(diff_row)
	var diff_label := Label.new()
	diff_label.text = tr("START_DIFFICULTY")
	_diff_label = diff_label
	diff_label.add_theme_font_override("font", FONT)
	diff_label.add_theme_font_size_override("font_size", 24)
	diff_row.add_child(diff_label)
	for d in GameState.DIFFICULTY_ORDER:
		var b := Button.new()
		b.text = GameState.DIFFICULTY_DEFS[d]["label"]
		b.toggle_mode = true
		b.button_group = _diff_group
		b.custom_minimum_size = Vector2(90.0, 52.0)
		b.add_theme_font_override("font", FONT)
		b.add_theme_font_size_override("font_size", 26)
		UITheme.apply_button(b)
		b.pressed.connect(_on_difficulty_pressed.bind(d))
		diff_row.add_child(b)
		_diff_buttons[d] = b

	_continue_button = _make_button("继续对局")
	_continue_button.pressed.connect(_on_continue_pressed)
	vbox.add_child(_continue_button)

	_new_button = _make_button("新游戏")
	_new_button.pressed.connect(_on_new_game_pressed)
	vbox.add_child(_new_button)

	_tutorial_button = _make_button("教程")
	_tutorial_button.pressed.connect(_on_tutorial_pressed)
	GameState.locale_changed.connect(func() -> void: _refresh_texts())
	vbox.add_child(_tutorial_button)

	_settings_button = _make_button("")
	vbox.add_child(_settings_button)
	_settings_button.pressed.connect(_on_settings_pressed)

	# 无存档时开场自显；有存档时等 main 调 show_panel()
	if not GameState.has_save():
		show_panel()


func _make_button(text: String) -> Button:
	var button := Button.new()
	button.text = text
	button.custom_minimum_size = Vector2(280.0, 56.0)
	button.add_theme_font_override("font", FONT)
	button.add_theme_font_size_override("font_size", 28)
	UITheme.apply_button(button)
	return button


## 显示面板并暂停游戏：开场自显与存档恢复共用此路径
func show_panel() -> void:
	var has_save := GameState.has_save()
	_refresh_texts()
	_continue_button.visible = has_save
	_refresh_difficulty_buttons()
	get_tree().paused = true
	visible = true
	UITheme.animate_open(_plate)


func _dismiss() -> void:
	visible = false
	get_tree().paused = false


func _refresh_texts() -> void:
	_hint_label.text = tr("START_HAS_SAVE") if GameState.has_save() else tr("START_NO_SAVE")
	_continue_button.text = tr("START_CONTINUE")
	_new_button.text = tr("START_NEW") if GameState.has_save() else tr("START_BEGIN")
	_tutorial_button.text = tr("START_TUTORIAL_DONE") if GameState.tutorial_done else tr("START_TUTORIAL")
	_settings_button.text = tr("START_SETTINGS")
	_diff_label.text = tr("START_DIFFICULTY")


func _refresh_difficulty_buttons() -> void:
	for d in _diff_buttons:
		(_diff_buttons[d] as Button).set_pressed_no_signal(GameState.difficulty == d)


func _on_difficulty_pressed(d: StringName) -> void:
	GameState.set_difficulty(d)


func _on_continue_pressed() -> void:
	_dismiss()
	continue_chosen.emit()


func _on_new_game_pressed() -> void:
	GameState.delete_save()
	_dismiss()
	new_game_chosen.emit()


func _on_tutorial_pressed() -> void:
	get_tree().paused = false
	get_tree().change_scene_to_file("res://scenes/tutorial.tscn")


func _on_settings_pressed() -> void:
	var settings := get_tree().get_first_node_in_group("settings_ui")
	if settings != null:
		# 开始面板 layer 高于设置面板，必须先隐藏自己，否则会挡住设置页
		visible = false
		settings.show_settings(self)
