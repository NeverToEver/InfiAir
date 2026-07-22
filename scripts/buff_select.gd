extends CanvasLayer
## 里程碑 Buff 三选一：达到里程碑阈值触发，暂停游戏并弹出 3 张卡片。

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")

const BUFF_POOL: Array[Dictionary] = [
	{
		"id": &"power_shot",
		"name": "强力射击",
		"desc": "子弹伤害 ×1.25\n（可无限叠加）",
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
		"desc": "最大生命 +50，立即回复 30\n（可无限叠加）",
		"max": 99,
	},
	{
		"id": &"regen",
		"name": "自我修复",
		"desc": "每秒回复 2 点生命",
		"max": 1,
	},
	{
		"id": &"piercing",
		"name": "穿透弹",
		"desc": "子弹可穿透 1 个敌人\n（最多 2 层）",
		"max": 2,
	},
	{
		"id": &"explosive",
		"name": "爆炸弹",
		"desc": "命中产生 50px 爆炸\n（固定 30 点溅射伤害）",
		"max": 1,
	},
	{
		"id": &"lifesteal",
		"name": "吸血",
		"desc": "击毁敌人回复 10% 最大生命",
		"max": 1,
	},
	{
		"id": &"armor",
		"name": "护甲",
		"desc": "受到的伤害 ×85%",
		"max": 1,
	},
	{
		"id": &"evasion",
		"name": "闪避",
		"desc": "20% 概率完全闪避",
		"max": 1,
	},
	{
		"id": &"phase_dash",
		"name": "相位冲刺",
		"desc": "解锁空格冲刺（无敌 0.25s）\n（再选冷却 -20%，最多 2 次）",
		"max": 3,
	},
	{
		"id": &"slow_field",
		"name": "慢速力场",
		"desc": "敌机与 Boss 移速 -20%",
		"max": 1,
	},
	{
		"id": &"efficient_boost",
		"name": "高效推进",
		"desc": "燃料消耗 -25%\n（可叠 2 层，乘算）",
		"max": 2,
	},
	{
		"id": &"laser_beam",
		"name": "激光束",
		"desc": "周期性释放 3 秒穿透激光\n（线上每 0.1 秒 10 伤害，冷却 10 秒）",
		"max": 1,
	},
	{
		"id": &"boost_recovery",
		"name": "燃料再生",
		"desc": "燃料恢复速度 ×1.5\n（可叠 2 层，乘算）",
		"max": 2,
	},
	{
		"id": &"mothership_recall",
		"name": "母舰召回",
		"desc": "母舰冷却时间减半\n（60s→30s→15s，最多 2 层）",
		"max": 2,
	},
]

var _center: CenterContainer
var _cards: HBoxContainer
var _title_label: Label
var _current_available: Array = []


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
	title.text = tr("BUFF_TITLE")
	_title_label = title
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_override("font", FONT)
	title.add_theme_font_size_override("font_size", 40)
	title.add_theme_color_override("font_color", UITheme.ACCENT_GOLD)
	vbox.add_child(title)

	_cards = HBoxContainer.new()
	_cards.add_theme_constant_override("separation", 24)
	_cards.alignment = BoxContainer.ALIGNMENT_CENTER
	vbox.add_child(_cards)

	GameState.milestone_reached.connect(_on_milestone_reached)
	GameState.locale_changed.connect(_on_locale_changed)


## 抽卡候选池：未满层 + 未被路线锁定；explosive 需 boss_kills>=3 解锁（原作 gating）。
## 层数上限可用 balance.json 的 buffs.<id>.max_stacks 覆盖（缺省用池内值）。
func _available_buffs() -> Array[Dictionary]:
	return BUFF_POOL.filter(
		func(b: Dictionary) -> bool: return (
			GameState.buff_count(b["id"]) < int(GameState.cfg("buffs.%s.max_stacks" % String(b["id"]), b["max"]))
			and not GameState.is_buff_locked(b["id"])
			and (b["id"] != &"explosive" or GameState.boss_kills >= 3)
		)
	)


func _on_milestone_reached(_milestone_score: int) -> void:
	if visible or GameState.health <= 0.0:
		return
	var available := _available_buffs()
	# 所有 buff 已满层：直接跳过本次里程碑
	if available.is_empty():
		return
	available.shuffle()
	_current_available = available.slice(0, 3)  # slice end 排他：取前 3 张候选
	_build_cards()
	get_tree().paused = true
	visible = true
	UITheme.animate_open(_center)


func _build_cards() -> void:
	for child in _cards.get_children():
		child.queue_free()
	for buff in _current_available:
		_cards.add_child(_make_card(buff))


func _on_locale_changed() -> void:
	_title_label.text = tr("BUFF_TITLE")
	if visible:
		_build_cards()


func _make_card(buff: Dictionary) -> Control:
	var card := ChamferedPanel.new()
	card.custom_minimum_size = Vector2(340.0, 200.0)
	card.brackets = true

	var margin := MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 20)
	margin.add_theme_constant_override("margin_right", 20)
	margin.add_theme_constant_override("margin_top", 16)
	margin.add_theme_constant_override("margin_bottom", 16)
	card.add_child(margin)

	var vbox := VBoxContainer.new()
	margin.add_child(vbox)
	vbox.add_theme_constant_override("separation", 12)

	var stacks := GameState.buff_count(buff["id"])
	var buff_name := tr("BUFF_%s_NAME" % String(buff["id"]).to_upper())
	var name_label := Label.new()
	name_label.text = buff_name if stacks == 0 else tr("BUFF_LV_FMT") % [buff_name, stacks]
	name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	name_label.add_theme_font_override("font", FONT)
	name_label.add_theme_font_size_override("font_size", 30)
	name_label.add_theme_color_override("font_color", UITheme.ACCENT)
	vbox.add_child(name_label)

	var desc_label := Label.new()
	desc_label.text = tr("BUFF_%s_DESC" % String(buff["id"]).to_upper())
	desc_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	desc_label.add_theme_font_override("font", FONT)
	desc_label.add_theme_font_size_override("font_size", 22)
	desc_label.add_theme_color_override("font_color", UITheme.TEXT_DIM)
	vbox.add_child(desc_label)

	card.gui_input.connect(_on_card_gui_input.bind(buff["id"]))
	return card


func _on_card_gui_input(event: InputEvent, id: StringName) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		GameState.play_sfx(GameState.SFX_BUFF_PICK)
		GameState.add_buff(id)
		if id == &"extra_life":
			# 对齐原作：选取瞬时 +30 HP（上限 +50 由 max_health() 按层数自动生效）
			GameState.heal(GameState.cfg("buffs.extra_life.heal_on_pick", 30))
		visible = false
		get_tree().paused = false
