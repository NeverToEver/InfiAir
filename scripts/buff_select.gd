extends CanvasLayer
## 里程碑 Buff 三选一：每 500 分触发，暂停游戏并弹出 3 张卡片。

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")

const BUFF_POOL: Array[Dictionary] = [
	{
		"id": &"power_shot",
		"name": "强力射击",
		"desc": "子弹伤害 +1\n（可无限叠加）",
		"max": 99,
	},
	{
		"id": &"rapid_fire",
		"name": "急速射击",
		"desc": "射速提升 25%\n（可无限叠加）",
		"max": 99,
	},
	{
		"id": &"spread_shot",
		"name": "散射弹道",
		"desc": "+1 条散射弹道\n（最多 3 层）",
		"max": 3,
	},
	{
		"id": &"extra_life",
		"name": "额外生命",
		"desc": "生命 +1\n（可无限叠加）",
		"max": 99,
	},
	{
		"id": &"regen",
		"name": "自我修复",
		"desc": "每 2 秒回复 0.5 生命\n（可叠加，生命向上取整显示）",
		"max": 99,
	},
]

var _center: CenterContainer
var _cards: HBoxContainer


func _ready() -> void:
	visible = false
	var dim := ColorRect.new()
	dim.color = Color(0.0, 0.0, 0.0, 0.6)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(dim)

	_center = CenterContainer.new()
	_center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(_center)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 24)
	_center.add_child(vbox)

	var title := Label.new()
	title.text = "选择一项强化"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_override("font", FONT)
	title.add_theme_font_size_override("font_size", 40)
	vbox.add_child(title)

	_cards = HBoxContainer.new()
	_cards.add_theme_constant_override("separation", 24)
	_cards.alignment = BoxContainer.ALIGNMENT_CENTER
	vbox.add_child(_cards)

	GameState.milestone_reached.connect(_on_milestone_reached)


func _on_milestone_reached(_milestone_score: int) -> void:
	if visible or GameState.lives <= 0.0:
		return
	var available := BUFF_POOL.filter(
		func(b: Dictionary) -> bool: return GameState.buff_count(b["id"]) < b["max"]
	)
	available.shuffle()
	for child in _cards.get_children():
		child.queue_free()
	for i in mini(3, available.size()):
		_cards.add_child(_make_card(available[i]))
	get_tree().paused = true
	visible = true


func _make_card(buff: Dictionary) -> PanelContainer:
	var card := PanelContainer.new()
	card.custom_minimum_size = Vector2(320.0, 200.0)
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.10, 0.13, 0.22, 0.95)
	style.border_color = Color(0.3, 0.8, 0.9)
	style.set_border_width_all(2)
	style.set_corner_radius_all(8)
	style.set_content_margin_all(20.0)
	card.add_theme_stylebox_override("panel", style)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 12)
	card.add_child(vbox)

	var name_label := Label.new()
	name_label.text = buff["name"]
	name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	name_label.add_theme_font_override("font", FONT)
	name_label.add_theme_font_size_override("font_size", 30)
	vbox.add_child(name_label)

	var desc_label := Label.new()
	desc_label.text = buff["desc"]
	desc_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	desc_label.add_theme_font_override("font", FONT)
	desc_label.add_theme_font_size_override("font_size", 22)
	vbox.add_child(desc_label)

	card.gui_input.connect(_on_card_gui_input.bind(buff["id"]))
	return card


func _on_card_gui_input(event: InputEvent, id: StringName) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		GameState.play_sfx(GameState.SFX_BUFF_PICK)
		GameState.add_buff(id)
		if id == &"extra_life":
			GameState.heal(1.0)
		visible = false
		get_tree().paused = false
