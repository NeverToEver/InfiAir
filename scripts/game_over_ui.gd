extends CanvasLayer
## 死亡结算面板：显示分数/击杀/Boss 击杀，按 R 重开。

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")

var _stats_label: Label
var _record_label: Label
var _title_label: Label
var _hint_label: Label


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
	title.text = tr("GO_TITLE")
	_title_label = title
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_override("font", FONT)
	title.add_theme_font_size_override("font_size", 56)
	vbox.add_child(title)

	_stats_label = Label.new()
	_stats_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_stats_label.add_theme_font_override("font", FONT)
	_stats_label.add_theme_font_size_override("font_size", 30)
	vbox.add_child(_stats_label)

	_record_label = Label.new()
	_record_label.text = tr("GO_RECORD")
	_record_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_record_label.add_theme_font_override("font", FONT)
	_record_label.add_theme_font_size_override("font_size", 34)
	_record_label.add_theme_color_override("font_color", UITheme.ACCENT_GOLD)
	_record_label.visible = false
	vbox.add_child(_record_label)

	var hint := Label.new()
	hint.text = tr("GO_RESTART")
	_hint_label = hint
	hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	hint.add_theme_font_override("font", FONT)
	hint.add_theme_font_size_override("font_size", 26)
	vbox.add_child(hint)

	GameState.player_died.connect(_on_player_died)
	GameState.locale_changed.connect(_on_locale_changed)


func _on_locale_changed() -> void:
	_title_label.text = tr("GO_TITLE")
	_record_label.text = tr("GO_RECORD")
	_hint_label.text = tr("GO_RESTART")
	if visible:
		_stats_label.text = (
			tr("GO_SCORE") + "\n" + tr("GO_BEST") + "\n" + tr("GO_KILLS") + "\n" + tr("GO_BOSS_KILLS")
		) % [GameState.score, GameState.high_score, GameState.kills, GameState.boss_kills]


func _on_player_died() -> void:
	# 死亡删档：防止一死档永存
	GameState.delete_save()
	var new_record := GameState.record_score()
	_stats_label.text = (
		tr("GO_SCORE") + "\n" + tr("GO_BEST") + "\n" + tr("GO_KILLS") + "\n" + tr("GO_BOSS_KILLS")
	) % [GameState.score, GameState.high_score, GameState.kills, GameState.boss_kills]
	_record_label.visible = new_record
	if new_record:
		GameState.play_sfx(GameState.SFX_BUFF_PICK)
	get_tree().paused = true
	visible = true


func _unhandled_input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("restart"):
		get_tree().paused = false
		GameState.reset_run()
		get_tree().reload_current_scene()
