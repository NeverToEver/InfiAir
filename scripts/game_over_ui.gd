extends CanvasLayer
## 死亡结算面板：显示分数/击杀/Boss 击杀，按 R 重开。

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")

var _stats_label: Label


func _ready() -> void:
	visible = false
	var dim := ColorRect.new()
	dim.color = Color(0.0, 0.0, 0.0, 0.7)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(dim)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 24)
	center.add_child(vbox)

	var title := Label.new()
	title.text = "游戏结束"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_override("font", FONT)
	title.add_theme_font_size_override("font_size", 56)
	vbox.add_child(title)

	_stats_label = Label.new()
	_stats_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_stats_label.add_theme_font_override("font", FONT)
	_stats_label.add_theme_font_size_override("font_size", 30)
	vbox.add_child(_stats_label)

	var hint := Label.new()
	hint.text = "按 R 重新开始"
	hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	hint.add_theme_font_override("font", FONT)
	hint.add_theme_font_size_override("font_size", 26)
	vbox.add_child(hint)

	GameState.player_died.connect(_on_player_died)


func _on_player_died() -> void:
	_stats_label.text = (
		"分数：%d\n击杀：%d\nBoss 击杀：%d" % [GameState.score, GameState.kills, GameState.boss_kills]
	)
	get_tree().paused = true
	visible = true


func _unhandled_input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("restart"):
		get_tree().paused = false
		GameState.reset_run()
		get_tree().reload_current_scene()
