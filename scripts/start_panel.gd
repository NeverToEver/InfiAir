extends CanvasLayer
## 开始面板：检测到存档时显示「继续对局 / 新游戏」。

signal continue_chosen
signal new_game_chosen

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")


func _ready() -> void:
	visible = false
	var dim := ColorRect.new()
	dim.color = Color(0.0, 0.0, 0.0, 0.75)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(dim)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 24)
	center.add_child(vbox)

	var title := Label.new()
	title.text = "InfiAir"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_override("font", FONT)
	title.add_theme_font_size_override("font_size", 56)
	vbox.add_child(title)

	var hint := Label.new()
	hint.text = "检测到未完成的对局"
	hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	hint.add_theme_font_override("font", FONT)
	hint.add_theme_font_size_override("font_size", 24)
	vbox.add_child(hint)

	var continue_button := _make_button("继续对局")
	continue_button.pressed.connect(_on_continue_pressed)
	vbox.add_child(continue_button)

	var new_button := _make_button("新游戏")
	new_button.pressed.connect(_on_new_game_pressed)
	vbox.add_child(new_button)


func _make_button(text: String) -> Button:
	var button := Button.new()
	button.text = text
	button.custom_minimum_size = Vector2(280.0, 56.0)
	button.add_theme_font_override("font", FONT)
	button.add_theme_font_size_override("font_size", 28)
	return button


func show_panel() -> void:
	get_tree().paused = true
	visible = true


func _on_continue_pressed() -> void:
	visible = false
	get_tree().paused = false
	continue_chosen.emit()


func _on_new_game_pressed() -> void:
	GameState.delete_save()
	visible = false
	get_tree().paused = false
	new_game_chosen.emit()
