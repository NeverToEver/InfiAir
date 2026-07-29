extends CanvasLayer
## 里程碑 Buff 三选一：达到里程碑阈值触发，暂停游戏并弹出 3 张卡片。

const BUFF_POOL: Array[Dictionary] = [
	{
		"id": &"power_shot",
		"name": "强力射击",
		"max": 5,
	},
	{
		"id": &"rapid_fire",
		"name": "急速射击",
		"max": 4,
	},
	{
		"id": &"spread_shot",
		"name": "散射弹道",
		"max": 3,
	},
	{
		"id": &"extra_life",
		"name": "额外生命",
		"max": 10,
	},
	{
		"id": &"regen",
		"name": "自我修复",
		"max": 1,
	},
	{
		"id": &"piercing",
		"name": "穿透弹",
		"max": 2,
	},
	{
		"id": &"explosive",
		"name": "爆炸弹",
		"max": 1,
	},
	{
		"id": &"lifesteal",
		"name": "吸血",
		"max": 1,
	},
	{
		"id": &"armor",
		"name": "护甲",
		"max": 1,
	},
	{
		"id": &"evasion",
		"name": "闪避",
		"max": 1,
	},
	{
		"id": &"phase_dash",
		"name": "相位冲刺",
		"max": 3,
	},
	{
		"id": &"slow_field",
		"name": "慢速力场",
		"max": 1,
	},
	{
		"id": &"efficient_boost",
		"name": "高效推进",
		"max": 2,
	},
	{
		"id": &"laser_beam",
		"name": "激光束",
		"max": 1,
	},
	{
		"id": &"boost_recovery",
		"name": "燃料再生",
		"max": 2,
	},
	{
		"id": &"mothership_recall",
		"name": "母舰召回",
		"max": 2,
	},
]

var _center: CenterContainer
var _cards: HBoxContainer
var _title_label: Label
var _current_available: Array = []
var _closing: bool = false  # 选取确认动效播放中：屏蔽再次点选


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

	var title := UITheme.make_label(tr("BUFF_TITLE"), UITheme.FONT_TITLE, UITheme.ACCENT_GOLD)
	_title_label = title
	vbox.add_child(title)

	_cards = HBoxContainer.new()
	_cards.add_theme_constant_override("separation", 24)
	_cards.alignment = BoxContainer.ALIGNMENT_CENTER
	vbox.add_child(_cards)

	GameState.milestone_reached.connect(_on_milestone_reached)
	GameState.locale_changed.connect(_on_locale_changed)


## 抽卡候选池：未满层 + 未被路线锁定；explosive 需 boss_kills 达 buffs.explosive.unlock_boss_kills 解锁（原作 gating）。
## 层数上限可用 balance.json 的 buffs.<id>.max_stacks 覆盖（缺省用池内值）。
func _available_buffs() -> Array[Dictionary]:
	return BUFF_POOL.filter(
		func(b: Dictionary) -> bool: return (
			GameState.buff_count(b["id"]) < int(GameState.cfg("buffs.%s.max_stacks" % String(b["id"]), b["max"]))
			and not GameState.is_buff_locked(b["id"])
			and (b["id"] != &"explosive" or GameState.boss_kills >= int(GameState.cfg("buffs.explosive.unlock_boss_kills", 3)))
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
	_closing = false
	_center.modulate.a = 1.0  # 复位选取动效留下的整体淡出
	# 三卡错峰淡入（标题直接可见）
	UITheme.stagger_open(_cards)
	# 键盘导航链路：焦点落在第一张卡（方向键切换，Enter 选取）
	if _cards.get_child_count() > 0:
		(_cards.get_child(0) as Control).grab_focus()


func _build_cards() -> void:
	for child in _cards.get_children():
		child.free()  # 立即释放：避免 stagger_open 把待释放旧卡计入错峰序号
	for buff in _current_available:
		_cards.add_child(_make_card(buff))


func _on_locale_changed() -> void:
	_title_label.text = tr("BUFF_TITLE")
	if visible:
		_build_cards()


func _make_card(buff: Dictionary) -> Control:
	var card := ChamferedPanel.new()
	card.custom_minimum_size = Vector2(340.0, 260.0)  # 三卡统一高度
	card.brackets = true
	card.focus_mode = Control.FOCUS_ALL
	# hover/focus 缩放以卡片中心为轴（ChamferedPanel 会按内容放大，resized 后重设中心）
	card.pivot_offset = card.custom_minimum_size / 2.0
	card.resized.connect(_on_card_resized.bind(card))

	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)  # 填满卡片，内容居中
	margin.add_theme_constant_override("margin_left", 20)
	margin.add_theme_constant_override("margin_right", 20)
	margin.add_theme_constant_override("margin_top", 16)
	margin.add_theme_constant_override("margin_bottom", 16)
	card.add_child(margin)

	var vbox := VBoxContainer.new()
	margin.add_child(vbox)
	vbox.add_theme_constant_override("separation", 10)
	vbox.alignment = BoxContainer.ALIGNMENT_CENTER

	var stacks := GameState.buff_count(buff["id"])
	# 上限口径与 _available_buffs() 相同：balance.json 可覆盖池内值
	var max_stacks := int(GameState.cfg("buffs.%s.max_stacks" % String(buff["id"]), buff["max"]))

	# 名称行：名称 + NEW! 徽标（首次获得时）
	var name_row := HBoxContainer.new()
	name_row.alignment = BoxContainer.ALIGNMENT_CENTER
	name_row.add_theme_constant_override("separation", 8)
	vbox.add_child(name_row)
	var name_label := UITheme.make_label(
		tr("BUFF_%s_NAME" % String(buff["id"]).to_upper()), UITheme.FONT_HEADER, UITheme.ACCENT
	)
	name_row.add_child(name_label)
	if stacks == 0:
		name_row.add_child(UITheme.make_label(tr("BUFF_NEW_BADGE"), UITheme.FONT_SMALL, UITheme.ACCENT_GOLD))

	# 层数 pip 点阵：●=已有层 ○=空位；上限 >8（如 extra_life 10）退化为原文字格式
	if max_stacks <= 8:
		var pip_row := HBoxContainer.new()
		pip_row.alignment = BoxContainer.ALIGNMENT_CENTER
		pip_row.add_theme_constant_override("separation", 2)
		vbox.add_child(pip_row)
		if stacks > 0:
			pip_row.add_child(UITheme.make_label("●".repeat(stacks), UITheme.FONT_CAPTION, UITheme.ACCENT_GOLD))
		if stacks < max_stacks:
			pip_row.add_child(UITheme.make_label("○".repeat(max_stacks - stacks), UITheme.FONT_CAPTION, UITheme.TEXT_DIM))
	elif stacks > 0:
		vbox.add_child(UITheme.make_label(
			tr("BUFF_STACKS_FMT") % stacks, UITheme.FONT_CAPTION, UITheme.ACCENT_GOLD
		))

	# 来源分类小标签（进攻/机动/通用），置于描述上方
	var kind_key := "BUFF_KIND_GENERAL"
	if buff["id"] in GameState.ROUTE_LINES[&"offense"]:
		kind_key = "BUFF_KIND_OFFENSE"
	elif buff["id"] in GameState.ROUTE_LINES[&"mobility"]:
		kind_key = "BUFF_KIND_MANEUVER"
	vbox.add_child(UITheme.make_label(tr(kind_key), UITheme.FONT_SMALL, UITheme.TEXT_DIM))

	var desc_label := UITheme.make_label(
		tr("BUFF_%s_DESC" % String(buff["id"]).to_upper()), UITheme.FONT_BODY, UITheme.TEXT_DIM
	)
	desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	vbox.add_child(desc_label)

	card.gui_input.connect(_on_card_gui_input.bind(buff["id"], card))
	# hover/focus 提亮边框（键盘导航焦点可见）
	card.mouse_entered.connect(_set_card_highlight.bind(card, true))
	card.mouse_exited.connect(_set_card_highlight.bind(card, false))
	card.focus_entered.connect(_set_card_highlight.bind(card, true))
	card.focus_exited.connect(_set_card_highlight.bind(card, false))
	# hover/focus 1.05 缩放（与边框高亮共存）
	card.mouse_entered.connect(_tween_card_scale.bind(card, true))
	card.mouse_exited.connect(_tween_card_scale.bind(card, false))
	card.focus_entered.connect(_tween_card_scale.bind(card, true))
	card.focus_exited.connect(_tween_card_scale.bind(card, false))
	return card


func _on_card_resized(card: ChamferedPanel) -> void:
	card.pivot_offset = card.size / 2.0


## hover/focus 缩放反馈：150ms tween 到 1.05，离开/失焦回 1.0（树暂停时 BuffUI 层 process_mode=Always 可播）
func _tween_card_scale(card: ChamferedPanel, on: bool) -> void:
	if card.has_meta("hover_tween"):
		(card.get_meta("hover_tween") as Tween).kill()
	var tween := card.create_tween()
	card.set_meta("hover_tween", tween)
	tween.tween_property(card, "scale", Vector2(1.05, 1.05) if on else Vector2.ONE, 0.15)


func _set_card_highlight(card: ChamferedPanel, on: bool) -> void:
	card.border_color = UITheme.ACCENT if on else UITheme.PANEL_BORDER
	card.bracket_color = UITheme.ACCENT_GOLD if on else UITheme.ACCENT


func _on_card_gui_input(event: InputEvent, id: StringName, card: Control = null) -> void:
	if _closing:
		return
	var picked: bool = event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT
	# 键盘导航：焦点卡上按 Enter/Space 选取
	if event is InputEventKey and event.pressed and not event.echo and event.is_action(&"ui_accept"):
		picked = true
	if picked:
		GameState.play_sfx(GameState.SFX_BUFF_PICK)
		GameState.add_buff(id)
		if id == &"extra_life":
			# 对齐原作：选取瞬时 +30 HP（上限 +50 由 max_health() 按层数自动生效）
			GameState.heal(GameState.cfg("buffs.extra_life.heal_on_pick", 30))
		if card == null:
			# 无卡片上下文（脚本/测试直调）：保持立即关闭语义
			visible = false
			get_tree().paused = false
			return
		# 选取确认动效：被选中卡缩至 0.95 + 金色提亮，随后整体 ~200ms 内淡出关闭（其余两卡随面板隐藏）
		_closing = true
		if card.has_meta("hover_tween"):
			(card.get_meta("hover_tween") as Tween).kill()
		var tween := card.create_tween()
		tween.tween_property(card, "scale", Vector2(0.95, 0.95), 0.1)
		tween.parallel().tween_property(card, "modulate", UITheme.ACCENT_GOLD.lightened(0.4), 0.1)
		tween.tween_property(_center, "modulate:a", 0.0, 0.1)
		tween.finished.connect(_on_pick_close_finished)


## 确认动效结束：关闭面板并恢复对局（与原收尾语义一致）
func _on_pick_close_finished() -> void:
	visible = false
	_center.modulate.a = 1.0
	_closing = false
	get_tree().paused = false
