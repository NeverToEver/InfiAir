extends CanvasLayer
## Esc 暂停面板：继续 / 保存进度 / 重开提示。
## 「保存进度」是全局唯一主动存档入口。

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")

var _save_button: Button


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

	_save_button = Button.new()
	_save_button.text = "保存进度"
	_save_button.custom_minimum_size = Vector2(240.0, 52.0)
	_save_button.add_theme_font_override("font", FONT)
	_save_button.add_theme_font_size_override("font_size", 26)
	_save_button.pressed.connect(_on_save_pressed)
	vbox.add_child(_save_button)

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
		_save_button.text = "保存进度"
		get_tree().paused = true
		visible = true


func _on_save_pressed() -> void:
	var player := get_tree().get_first_node_in_group("player")
	var spawner := get_tree().get_first_node_in_group("spawner")
	var fuel: float = player._fuel if player != null else 100.0
	var elapsed: float = spawner._elapsed if spawner != null else 0.0
	GameState.save_run(fuel, elapsed)
	_save_button.text = "已保存 ✓"
	await get_tree().create_timer(1.0).timeout
	_save_button.text = "保存进度"


func _unhandled_input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("restart"):
		get_tree().paused = false
		GameState.reset_run()
		get_tree().reload_current_scene()
