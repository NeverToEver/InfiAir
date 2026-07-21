extends CanvasLayer
## 基地控制台（返航中场整备）：战机库 / 武器挂载（天赋路线）/ 维修补给 / 任务规划。
## 顶部 RP 余额，底部「继续出击」返回同一局。

signal resume_requested

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")
const ROUTE_BUFF_NAMES: Dictionary = {
	&"spread_shot": "BUFF_SPREAD_SHOT_NAME",
	&"laser_beam": "BUFF_LASER_BEAM_NAME",
	&"phase_dash": "BUFF_PHASE_DASH_NAME",
	&"mothership_recall": "BUFF_MOTHERSHIP_RECALL_NAME",
}
const ROUTE_LINE_NAMES: Dictionary = {&"offense": "ROUTE_OFFENSE", &"mobility": "ROUTE_MOBILITY"}
var LIVES_CAP := 6.0

var _rp_label: Label
var _title_label: Label
var _status_label: Label
var _repair_button: Button
var _recharge_button: Button
var _routes_box: VBoxContainer
var _missions_box: VBoxContainer
var _title_labels: Dictionary = {}
var _route_hint_label: Label


func _ready() -> void:
	visible = false
	GameState.locale_changed.connect(_on_locale_changed)
	LIVES_CAP = GameState.cfg("mothership.lives_cap", LIVES_CAP)
	var dim := ColorRect.new()
	dim.color = Color(0.02, 0.03, 0.08, 0.95)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(dim)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 14)
	center.add_child(vbox)

	_title_label = _make_label(tr("BASE_TITLE"), 44)
	vbox.add_child(_title_label)
	_rp_label = _make_label("", 26)
	_rp_label.add_theme_color_override("font_color", UITheme.ACCENT_GOLD)
	vbox.add_child(_rp_label)

	var columns := HBoxContainer.new()
	columns.add_theme_constant_override("separation", 20)
	vbox.add_child(columns)

	# 左列：战机库 + 维修补给
	var left := VBoxContainer.new()
	left.add_theme_constant_override("separation", 20)
	columns.add_child(left)
	left.add_child(_build_hangar())
	left.add_child(_build_supply())

	# 右列：武器挂载 + 任务规划
	var right := VBoxContainer.new()
	right.add_theme_constant_override("separation", 20)
	columns.add_child(right)
	right.add_child(_build_routes())
	right.add_child(_build_missions())

	var resume_button := Button.new()
	resume_button.text = tr("BASE_RESUME")
	resume_button.custom_minimum_size = Vector2(280.0, 52.0)
	resume_button.add_theme_font_override("font", FONT)
	resume_button.add_theme_font_size_override("font_size", 26)
	resume_button.pressed.connect(_on_resume_pressed)
	vbox.add_child(resume_button)


func _make_label(text: String, size: int) -> Label:
	var label := Label.new()
	label.text = text
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.add_theme_font_override("font", FONT)
	label.add_theme_font_size_override("font_size", size)
	return label


func _make_panel(title_key: String) -> PanelContainer:
	var panel := PanelContainer.new()
	panel.custom_minimum_size = Vector2(560.0, 0.0)
	panel.add_theme_stylebox_override("panel", UITheme.make_panel_style())
	var vbox := VBoxContainer.new()
	vbox.name = "Body"
	vbox.add_theme_constant_override("separation", 8)
	panel.add_child(vbox)
	var title_label := _make_label(tr(title_key), 24)
	_title_labels[title_key] = title_label
	title_label.add_theme_color_override("font_color", UITheme.ACCENT)
	vbox.add_child(title_label)
	return panel


func _make_button(text: String) -> Button:
	var button := Button.new()
	button.text = text
	button.add_theme_font_override("font", FONT)
	button.add_theme_font_size_override("font_size", 20)
	UITheme.apply_button(button)
	return button


func _build_hangar() -> PanelContainer:
	var panel := _make_panel("BASE_HANGAR")
	_status_label = _make_label("", 20)
	_status_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_LEFT
	panel.get_node("Body").add_child(_status_label)
	return panel


func _build_supply() -> PanelContainer:
	var panel := _make_panel("BASE_SUPPLY")
	var body := panel.get_node("Body")
	_repair_button = _make_button("")
	_repair_button.pressed.connect(_on_repair_pressed)
	body.add_child(_repair_button)
	_recharge_button = _make_button("")
	_recharge_button.pressed.connect(_on_recharge_pressed)
	body.add_child(_recharge_button)
	return panel


func _build_routes() -> PanelContainer:
	var panel := _make_panel("BASE_ROUTES")
	var body := panel.get_node("Body")
	_routes_box = VBoxContainer.new()
	_routes_box.add_theme_constant_override("separation", 8)
	body.add_child(_routes_box)
	var hint := _make_label(tr("BASE_ROUTE_HINT"), 16)
	_route_hint_label = hint
	body.add_child(hint)
	return panel


func _build_missions() -> PanelContainer:
	var panel := _make_panel("BASE_MISSIONS")
	_missions_box = VBoxContainer.new()
	_missions_box.add_theme_constant_override("separation", 8)
	panel.get_node("Body").add_child(_missions_box)
	return panel


func show_base() -> void:
	_refresh()
	visible = true


func _refresh() -> void:
	_rp_label.text = tr("BASE_RP") % GameState.rp
	var player := get_tree().get_first_node_in_group("player") as Player
	# 战机库状态总览
	var buff_text := ""
	for id in GameState.buffs:
		buff_text += "%s×%d  " % [String(id), int(GameState.buffs[id])]
	if buff_text.is_empty():
		buff_text = tr("BASE_NO_BUFF")
	var fuel_pct := 0
	if player != null:
		fuel_pct = int(player.fuel_ratio() * 100.0)
	_status_label.text = tr("BASE_STATUS_FMT") % [ceili(GameState.lives), fuel_pct, buff_text]
	# 维修补给按钮状态
	_title_label.text = tr("BASE_TITLE")
	for k in _title_labels:
		(_title_labels[k] as Label).text = tr(k)
	_route_hint_label.text = tr("BASE_ROUTE_HINT")
	_repair_button.text = tr("BASE_REPAIR")
	_recharge_button.text = tr("BASE_RECHARGE")
	_repair_button.disabled = GameState.rp < GameState.RP_REPAIR_COST or GameState.lives >= LIVES_CAP
	_recharge_button.disabled = (
		GameState.rp < GameState.RP_RECHARGE_COST or player == null or player._fuel >= player.fuel_max
	)
	_refresh_routes()
	_refresh_missions()


func _refresh_routes() -> void:
	for child in _routes_box.get_children():
		child.queue_free()
	for line in GameState.ROUTE_LINES:
		var options: Array = GameState.ROUTE_LINES[line]
		var total := GameState.buff_count(options[0]) + GameState.buff_count(options[1])
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 10)
		var line_label := _make_label(tr("BASE_LINE_FMT") % [tr(ROUTE_LINE_NAMES.get(line, String(line))), total], 20)
		line_label.custom_minimum_size = Vector2(170.0, 0.0)
		line_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_LEFT
		row.add_child(line_label)
		for opt in options:
			var chosen: bool = GameState.chosen_routes.get(line) == opt
			var locked := GameState.is_buff_locked(opt)
			var button := _make_button("")
			var buff_name := tr(ROUTE_BUFF_NAMES[opt])
			if chosen:
				button.text = tr("BASE_CHOSEN_FMT") % [buff_name, GameState.buff_count(opt)]
			elif locked:
				button.text = tr("BASE_LOCKED_FMT") % buff_name
			else:
				button.text = tr("BUFF_LV_FMT") % [buff_name, GameState.buff_count(opt)]
			button.disabled = chosen or locked or total == 0
			button.pressed.connect(_on_route_pressed.bind(line, opt))
			row.add_child(button)
		_routes_box.add_child(row)


func _refresh_missions() -> void:
	for child in _missions_box.get_children():
		child.queue_free()
	for def in GameState.MISSION_DEFS:
		var id: StringName = def["id"]
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 10)
		var progress := GameState.mission_progress(id)
		var goal := GameState.mission_goal(id)
		var text := "%s：%s（%d/%d）" % [tr("MISSION_%s_NAME" % String(id).to_upper()), tr("MISSION_%s_DESC" % String(id).to_upper()), mini(progress, goal), goal]
		if GameState.is_mission_claimed(id):
			text += tr("BASE_CLAIMED")
		elif GameState.is_mission_done(id):
			text += tr("BASE_DONE")
		var info := _make_label(text, 20)
		info.custom_minimum_size = Vector2(400.0, 0.0)
		info.horizontal_alignment = HORIZONTAL_ALIGNMENT_LEFT
		if GameState.is_mission_done(id):
			info.add_theme_color_override("font_color", UITheme.SUCCESS)
		row.add_child(info)
		var claim_button := _make_button(tr("BASE_CLAIM"))
		claim_button.disabled = not GameState.is_mission_done(id) or GameState.is_mission_claimed(id)
		claim_button.pressed.connect(_on_claim_pressed.bind(id))
		row.add_child(claim_button)
		_missions_box.add_child(row)


func _on_locale_changed() -> void:
	_refresh()


func _on_repair_pressed() -> void:
	if GameState.spend_rp(GameState.RP_REPAIR_COST):
		GameState.heal(1.0)
		GameState.play_sfx(GameState.SFX_RESUPPLY)
		_refresh()


func _on_recharge_pressed() -> void:
	var player := get_tree().get_first_node_in_group("player") as Player
	if player != null and GameState.spend_rp(GameState.RP_RECHARGE_COST):
		player.refill_fuel()
		GameState.play_sfx(GameState.SFX_RESUPPLY)
		_refresh()


func _on_route_pressed(line: StringName, buff_id: StringName) -> void:
	# choose_route 只改层数；玩家侧效果均实时读取 GameState.buff_count，无需额外重放（laser/recall 效果本体已在 3.3 实装）
	if GameState.choose_route(line, buff_id):
		GameState.play_sfx(GameState.SFX_BUFF_PICK)
	_refresh()


func _on_claim_pressed(id: StringName) -> void:
	if GameState.claim_mission(id):
		GameState.play_sfx(GameState.SFX_BUFF_PICK)
	_refresh()


func _on_resume_pressed() -> void:
	visible = false
	resume_requested.emit()
