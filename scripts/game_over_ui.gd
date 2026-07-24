extends CanvasLayer
## 死亡结算面板：DISPLAY 级大分数 + 新纪录标记 + 击杀统计，按 R 重开。

var _score_label: Label
var _score_tag_label: Label
var _stats_label: Label
var _record_label: Label
var _title_label: Label
var _hint_label: Label
var _plate: ChamferedPanel


func _ready() -> void:
	visible = false
	var dim := ColorRect.new()
	dim.color = Color(0.0, 0.0, 0.0, 0.7)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(dim)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	_plate = ChamferedPanel.new()
	_plate.custom_minimum_size = Vector2(640.0, 520.0)
	_plate.brackets = true
	center.add_child(_plate)

	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	_plate.add_child(margin)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 16)
	vbox.alignment = BoxContainer.ALIGNMENT_CENTER
	margin.add_child(vbox)

	_title_label = UITheme.make_label(tr("GO_TITLE"), UITheme.FONT_TITLE, UITheme.TEXT)
	vbox.add_child(_title_label)

	# 大分数：DISPLAY 级金色数字 + 小 caption 标签
	_score_tag_label = UITheme.make_label(tr("UI_SCORE_TAG"), UITheme.FONT_CAPTION, UITheme.TEXT_DIM)
	vbox.add_child(_score_tag_label)
	_score_label = UITheme.make_label("0", UITheme.FONT_DISPLAY, UITheme.ACCENT_GOLD)
	vbox.add_child(_score_label)

	_record_label = UITheme.make_label(tr("GO_RECORD"), UITheme.FONT_HEADER, UITheme.ACCENT_GOLD)
	_record_label.visible = false
	vbox.add_child(_record_label)

	_stats_label = UITheme.make_label("", UITheme.FONT_BODY, UITheme.TEXT)
	vbox.add_child(_stats_label)

	_hint_label = UITheme.make_label(tr("GO_RESTART"), UITheme.FONT_CAPTION, UITheme.TEXT_DIM)
	vbox.add_child(_hint_label)

	GameState.player_died.connect(_on_player_died)
	GameState.locale_changed.connect(_on_locale_changed)


func _on_locale_changed() -> void:
	_title_label.text = tr("GO_TITLE")
	_score_tag_label.text = tr("UI_SCORE_TAG")
	_record_label.text = tr("GO_RECORD")
	_hint_label.text = tr("GO_RESTART")
	if visible:
		_stats_label.text = (tr("GO_BEST") + "\n" + tr("GO_KILLS") + "\n" + tr("GO_BOSS_KILLS")) % [
			GameState.high_score, GameState.kills, GameState.boss_kills
		]


func _on_player_died() -> void:
	# 死亡删档：防止一死档永存
	GameState.delete_save()
	var new_record := GameState.record_score()
	_score_label.text = str(GameState.score)
	_stats_label.text = (tr("GO_BEST") + "\n" + tr("GO_KILLS") + "\n" + tr("GO_BOSS_KILLS")) % [
		GameState.high_score, GameState.kills, GameState.boss_kills
	]
	_record_label.visible = new_record
	if new_record:
		GameState.play_sfx(GameState.SFX_BUFF_PICK)
	get_tree().paused = true
	visible = true
	UITheme.animate_open(_plate)


func _unhandled_input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("restart"):
		get_tree().paused = false
		GameState.reset_run()
		get_tree().reload_current_scene()
