extends CanvasLayer
## Esc 暂停面板：继续 / 重开提示。

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")


func _ready() -> void:
	visible = false
	var dim := ColorRect.new()
	dim.color = Color(0.0, 0.0, 0.0, 0.5)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(dim)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 24)
	center.add_child(vbox)

	var title := Label.new()
	title.text = "已暂停"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_override("font", FONT)
	title.add_theme_font_size_override("font_size", 48)
	vbox.add_child(title)

	var hint := Label.new()
	hint.text = "Esc 继续 · R 重新开始"
	hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	hint.add_theme_font_override("font", FONT)
	hint.add_theme_font_size_override("font_size", 26)
	vbox.add_child(hint)


func toggle() -> void:
	if visible:
		visible = false
		get_tree().paused = false
	else:
		get_tree().paused = true
		visible = true


func _unhandled_input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("restart"):
		get_tree().paused = false
		GameState.reset_run()
		get_tree().reload_current_scene()
