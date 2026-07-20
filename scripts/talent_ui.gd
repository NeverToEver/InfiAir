extends CanvasLayer
## 基地整备（天赋台）：返航后显示本局结算、天赋点余额与可购常驻增益。

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")

var _summary_label: Label
var _earned_label: Label
var _points_label: Label
var _rows: VBoxContainer


func _ready() -> void:
	visible = false
	var dim := ColorRect.new()
	dim.color = Color(0.02, 0.03, 0.08, 0.95)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(dim)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 18)
	center.add_child(vbox)

	var title := _make_label("基地整备", 48)
	vbox.add_child(title)

	_summary_label = _make_label("", 26)
	vbox.add_child(_summary_label)

	_earned_label = _make_label("", 26)
	vbox.add_child(_earned_label)

	_points_label = _make_label("", 28)
	vbox.add_child(_points_label)

	_rows = VBoxContainer.new()
	_rows.add_theme_constant_override("separation", 10)
	vbox.add_child(_rows)

	var launch_button := Button.new()
	launch_button.text = "再次出击"
	launch_button.custom_minimum_size = Vector2(280.0, 56.0)
	launch_button.add_theme_font_override("font", FONT)
	launch_button.add_theme_font_size_override("font_size", 28)
	launch_button.pressed.connect(_on_launch_pressed)
	vbox.add_child(launch_button)


func _make_label(text: String, size: int) -> Label:
	var label := Label.new()
	label.text = text
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.add_theme_font_override("font", FONT)
	label.add_theme_font_size_override("font_size", size)
	return label


func show_summary(earned: int, new_record: bool) -> void:
	_summary_label.text = "本局得分：%d\n最高分：%d" % [GameState.score, GameState.high_score]
	if new_record:
		_summary_label.text += "\n新纪录！"
		GameState.play_sfx(GameState.SFX_BUFF_PICK)
	_earned_label.text = "返航折算天赋点 +%d" % earned
	_refresh()
	visible = true


func _refresh() -> void:
	_points_label.text = "天赋点余额：%d" % GameState.talent_points
	for row in _rows.get_children():
		row.queue_free()
	for def in GameState.TALENT_DEFS:
		_rows.add_child(_make_talent_row(def))


func _make_talent_row(def: Dictionary) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 16)
	row.alignment = BoxContainer.ALIGNMENT_CENTER

	var level := GameState.talent_level(def["id"])
	var info := Label.new()
	info.text = "%s Lv.%d/%d — %s" % [def["name"], level, def["max"], def["desc"]]
	info.custom_minimum_size = Vector2(460.0, 0.0)
	info.add_theme_font_override("font", FONT)
	info.add_theme_font_size_override("font_size", 22)
	row.add_child(info)

	var buy_button := Button.new()
	buy_button.text = "购买（%d 点）" % def["cost"]
	buy_button.add_theme_font_override("font", FONT)
	buy_button.add_theme_font_size_override("font_size", 20)
	buy_button.disabled = level >= def["max"] or GameState.talent_points < def["cost"]
	buy_button.pressed.connect(_on_buy_pressed.bind(def["id"]))
	row.add_child(buy_button)
	return row


func _on_buy_pressed(id: StringName) -> void:
	if GameState.buy_talent(id):
		GameState.play_sfx(GameState.SFX_BUFF_PICK)
	_refresh()


func _on_launch_pressed() -> void:
	get_tree().paused = false
	GameState.reset_run()
	get_tree().reload_current_scene()
