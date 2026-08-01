extends CanvasLayer
## 基地控制台（返航中场整备）：战机库 / 武器挂载（天赋路线）/ 维修补给 / 任务规划。
## 顶部 RP 余额，底部「继续出击」返回同一局。
## 视觉为「虚影皮肤」（docs/RETURN_HOME_CINEMATIC.md §3）：虚影站背景层 + 全息面板，
## 全部信号/回调/GameState 数据接口零改动。

signal resume_requested


## 面板扫描线叠加层（§3.2）：单节点自绘每 4px 一条 1px 横线，1 draw call
class _Scanlines:
	extends Control

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_IGNORE
		resized.connect(queue_redraw)

	func _draw() -> void:
		var y := 2.0
		while y < size.y:
			draw_line(Vector2(0.0, y), Vector2(size.x, y), UITheme.PHANTOM_SCAN, 1.0)
			y += 4.0


## 16×16 程序化线性发光图标（§3.2）：极简折线，青色双层描边模拟辉光
class _GlyphIcon:
	extends Control
	var strokes: Array = []  # Array[PackedVector2Array]

	func _ready() -> void:
		custom_minimum_size = Vector2(16.0, 16.0)
		mouse_filter = Control.MOUSE_FILTER_IGNORE
		size_flags_vertical = Control.SIZE_SHRINK_CENTER

	func _draw() -> void:
		for stroke in strokes:
			draw_polyline(stroke, Color(UITheme.ACCENT, 0.3), 3.0, true)
		for stroke in strokes:
			draw_polyline(stroke, UITheme.ACCENT, 1.5, true)

const ROUTE_BUFF_NAMES: Dictionary = {
	&"spread_shot": "BUFF_SPREAD_SHOT_NAME",
	&"laser_beam": "BUFF_LASER_BEAM_NAME",
	&"phase_dash": "BUFF_PHASE_DASH_NAME",
	&"mothership_recall": "BUFF_MOTHERSHIP_RECALL_NAME",
}
const ROUTE_LINE_NAMES: Dictionary = {&"offense": "ROUTE_OFFENSE", &"mobility": "ROUTE_MOBILITY"}

var _rp_label: Label
var _title_label: Label
var _status_label: Label
var _repair_button: Button
var _recharge_button: Button
var _routes_box: VBoxContainer
var _missions_box: VBoxContainer
var _title_labels: Dictionary = {}
var _columns: HBoxContainer
var _route_hint_label: Label
var _panels: Array[ChamferedPanel] = []
var _glow_texture: GradientTexture2D


## 虚影面板底后径向辉光垫（近似毛玻璃，§3.2）：四面板共享一张径向渐变纹理
func _make_glow_texture() -> GradientTexture2D:
	if _glow_texture != null:
		return _glow_texture
	var gradient := Gradient.new()
	gradient.set_color(0, Color(0.0, 0.83, 1.0, 0.12))
	gradient.set_color(1, Color(0.0, 0.83, 1.0, 0.0))
	_glow_texture = GradientTexture2D.new()
	_glow_texture.gradient = gradient
	_glow_texture.fill = GradientTexture2D.FILL_RADIAL
	_glow_texture.fill_from = Vector2(0.5, 0.5)
	_glow_texture.fill_to = Vector2(1.0, 0.5)
	_glow_texture.width = 128
	_glow_texture.height = 128
	return _glow_texture


## 数据抖动装饰（§3.2）：3Hz 正弦 α0.92–1.0 + 每 2.7s 一次 0.06s 的 1px 横向错位闪
##（tween 循环，不加 _process；本层 process_mode=Always，暂停态照常播放）
func _apply_data_flicker(label: Label) -> void:
	var tween := create_tween().set_loops()
	for i in 8:  # 8 × 0.334s ≈ 2.67s
		tween.tween_property(label, "modulate:a", 0.92, 0.167).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
		tween.tween_property(label, "modulate:a", 1.0, 0.167).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	tween.tween_property(label, "position:x", 1.0, 0.03)
	tween.tween_interval(0.03)
	tween.tween_property(label, "position:x", 0.0, 0.0)


func _ready() -> void:
	visible = false
	GameState.locale_changed.connect(_on_locale_changed)
	var dim := ColorRect.new()
	dim.color = UITheme.PHANTOM_BG
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(dim)

	# 虚影站内部概念背景层（§3.3.1）：PHANTOM 站体 r≈520 以 (960,540) 为圆心，
	# 父容器压 α≈0.12（站体自呼吸写自身 modulate:a，不能直接在站体上压 alpha）+ 8s/趟全屏慢扫描带。
	# 该层放在 dim 之后、CenterContainer 之前。
	var bg_wrap := Control.new()
	bg_wrap.mouse_filter = Control.MOUSE_FILTER_IGNORE
	bg_wrap.set_anchors_preset(Control.PRESET_FULL_RECT)
	bg_wrap.modulate.a = 0.12
	add_child(bg_wrap)
	var station := DawnStation.build(DawnStation.Mode.PHANTOM)
	station.position = Vector2(960.0, 540.0)
	station.scale = Vector2.ONE * 2.0
	bg_wrap.add_child(station)
	var slow_scan := ColorRect.new()
	slow_scan.color = UITheme.PHANTOM_SCAN
	slow_scan.mouse_filter = Control.MOUSE_FILTER_IGNORE
	slow_scan.size = Vector2(1920.0, 140.0)
	slow_scan.position = Vector2(0.0, -140.0)
	add_child(slow_scan)
	var scan_tween := create_tween().set_loops()
	scan_tween.tween_property(slow_scan, "position:y", 1080.0, 8.0).set_trans(Tween.TRANS_LINEAR)
	scan_tween.tween_property(slow_scan, "position:y", -140.0, 0.0)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 14)
	center.add_child(vbox)

	_title_label = _make_label(tr("BASE_TITLE"), 44)
	vbox.add_child(_title_label)
	_apply_data_flicker(_title_label)
	_rp_label = _make_label("", 26)
	_rp_label.add_theme_color_override("font_color", UITheme.ACCENT_GOLD)
	vbox.add_child(_rp_label)
	_apply_data_flicker(_rp_label)

	_columns = HBoxContainer.new()
	var columns := _columns
	columns.add_theme_constant_override("separation", 140)  # §3.3.2：露出中央环体轴心，容器层级/热区/焦点链不变
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

	var resume_button := UITheme.make_button(tr("BASE_RESUME"), true)
	resume_button.custom_minimum_size = Vector2(280.0, 52.0)
	resume_button.pressed.connect(_on_resume_pressed)
	# 底部 2px 投影线 + 1.5s 呼吸辉光（§3.3.4，只动 alpha；尺寸/位置/回调不变）
	var shadow_line := ColorRect.new()
	shadow_line.color = UITheme.ACCENT_DIM
	shadow_line.mouse_filter = Control.MOUSE_FILTER_IGNORE
	shadow_line.anchor_left = 0.15
	shadow_line.anchor_right = 0.85
	shadow_line.anchor_top = 1.0
	shadow_line.anchor_bottom = 1.0
	shadow_line.offset_top = 2.0
	shadow_line.offset_bottom = 4.0
	shadow_line.modulate.a = 0.3
	resume_button.add_child(shadow_line)
	var glow_breathe := create_tween().set_loops()
	glow_breathe.tween_property(shadow_line, "modulate:a", 1.0, 0.75).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	glow_breathe.tween_property(shadow_line, "modulate:a", 0.3, 0.75).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	vbox.add_child(resume_button)


func _make_label(text: String, size: int) -> Label:
	return UITheme.make_label(text, size)


func _make_panel(title_key: String, glyph: Array) -> Control:
	var panel := ChamferedPanel.new()
	UITheme.apply_phantom_panel(panel)  # §3.3.3 虚影材质
	panel.custom_minimum_size = Vector2(560.0, 0.0)
	_panels.append(panel)
	# 面板底后径向辉光垫（近似毛玻璃，§3.2）：绘于面板底之下，随面板尺寸自适应
	var glow := TextureRect.new()
	glow.texture = _make_glow_texture()
	glow.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	glow.stretch_mode = TextureRect.STRETCH_SCALE
	glow.show_behind_parent = true
	glow.mouse_filter = Control.MOUSE_FILTER_IGNORE
	glow.set_anchors_preset(Control.PRESET_FULL_RECT)
	panel.add_child(glow)
	# 扫描线叠加层（§3.2）：单节点自绘，绘于面板底之上、内容之下
	var scan := _Scanlines.new()
	scan.set_anchors_preset(Control.PRESET_FULL_RECT)
	panel.add_child(scan)
	var vbox := VBoxContainer.new()
	vbox.name = "Body"
	vbox.add_theme_constant_override("separation", 8)
	vbox.set_anchors_preset(Control.PRESET_FULL_RECT)
	vbox.offset_left = 14.0
	vbox.offset_top = 14.0
	vbox.offset_right = -14.0
	vbox.offset_bottom = -14.0
	panel.add_child(vbox)
	# 标题行：16×16 线性发光图标 + section header（仅基地内组装，不影响其它页面的 make_section_header）
	var header := UITheme.make_section_header(tr(title_key))
	header.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_title_labels[title_key] = header.get_child(0) as Label
	_apply_data_flicker(_title_labels[title_key])
	var header_row := HBoxContainer.new()
	header_row.add_theme_constant_override("separation", 8)
	var icon := _GlyphIcon.new()
	icon.strokes = glyph
	header_row.add_child(icon)
	header_row.add_child(header)
	vbox.add_child(header_row)
	return panel


func _make_button(text: String) -> Button:
	var button := UITheme.make_button(text)
	button.add_theme_font_size_override("font_size", 20)
	return button


func _build_hangar() -> Control:
	# 战机极简折线图标
	var glyph: Array = [PackedVector2Array([Vector2(8, 1), Vector2(13, 14), Vector2(8, 11), Vector2(3, 14), Vector2(8, 1)])]
	var panel := _make_panel("BASE_HANGAR", glyph)
	_status_label = _make_label("", 20)
	_status_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_LEFT
	panel.get_node("Body").add_child(_status_label)
	return panel


func _build_supply() -> Control:
	# 扳手极简折线图标
	var glyph: Array = [PackedVector2Array([Vector2(3, 13), Vector2(9, 7), Vector2(12, 8), Vector2(14, 5), Vector2(12, 3), Vector2(9, 4), Vector2(9, 7)])]
	var panel := _make_panel("BASE_SUPPLY", glyph)
	var body := panel.get_node("Body")
	_repair_button = _make_button("")
	_repair_button.pressed.connect(_on_repair_pressed)
	body.add_child(_repair_button)
	_recharge_button = _make_button("")
	_recharge_button.pressed.connect(_on_recharge_pressed)
	body.add_child(_recharge_button)
	return panel


func _build_routes() -> Control:
	# 交叉线极简折线图标
	var glyph: Array = [
		PackedVector2Array([Vector2(3, 3), Vector2(13, 13)]),
		PackedVector2Array([Vector2(13, 3), Vector2(3, 13)]),
	]
	var panel := _make_panel("BASE_ROUTES", glyph)
	var body := panel.get_node("Body")
	_routes_box = VBoxContainer.new()
	_routes_box.add_theme_constant_override("separation", 8)
	body.add_child(_routes_box)
	var hint := _make_label(tr("BASE_ROUTE_HINT"), 16)
	_route_hint_label = hint
	body.add_child(hint)
	return panel


func _build_missions() -> Control:
	# 旗帜极简折线图标
	var glyph: Array = [
		PackedVector2Array([Vector2(4, 2), Vector2(4, 14)]),
		PackedVector2Array([Vector2(4, 2), Vector2(13, 4), Vector2(4, 8)]),
	]
	var panel := _make_panel("BASE_MISSIONS", glyph)
	_missions_box = VBoxContainer.new()
	_missions_box.add_theme_constant_override("separation", 8)
	panel.get_node("Body").add_child(_missions_box)
	return panel


func show_base() -> void:
	_refresh()
	visible = true
	_holo_boot()
	UITheme.animate_open(_columns)


## 全息启动（§3.3.5）：四面板 α0 + scale 0.98→1.0，stagger 60ms；
## pivot 设为中心（否则从左上角缩放），tween 终值保证 scale 精确回 1.0
func _holo_boot() -> void:
	var i := 0
	for panel in _panels:
		panel.pivot_offset = panel.size * 0.5
		panel.modulate.a = 0.0
		panel.scale = Vector2.ONE * 0.98
		var tween := create_tween()
		tween.tween_interval(0.06 * i)
		tween.tween_property(panel, "modulate:a", 1.0, 0.25)
		tween.parallel().tween_property(panel, "scale", Vector2.ONE, 0.25)
		i += 1


func _refresh() -> void:
	_rp_label.text = tr("BASE_RP") % GameState.rp
	var player := GameState.player_ref as Player  # A5：走注册表，替代 group 现找
	# 战机库状态总览
	var buff_text := ""
	for id in GameState.buffs:
		buff_text += "%s×%d  " % [String(id), int(GameState.buffs[id])]
	if buff_text.is_empty():
		buff_text = tr("BASE_NO_BUFF")
	var fuel_pct := 0
	if player != null:
		fuel_pct = int(player.fuel_ratio() * 100.0)
	_status_label.text = tr("BASE_STATUS_FMT") % [ceili(GameState.health), fuel_pct, buff_text]
	# 维修补给按钮状态
	_title_label.text = tr("BASE_TITLE")
	for k in _title_labels:
		(_title_labels[k] as Label).text = tr(k)
	_route_hint_label.text = tr("BASE_ROUTE_HINT")
	_repair_button.text = tr("BASE_REPAIR")
	_recharge_button.text = tr("BASE_RECHARGE")
	# 维修 = 2RP 回满（对齐原作 repair_at_base：health = max_health，满血拒售）
	_repair_button.disabled = GameState.rp < GameState.RP_REPAIR_COST or GameState.health >= GameState.max_health()
	_recharge_button.disabled = (
		GameState.rp < GameState.RP_RECHARGE_COST or player == null or player.fuel_amount() >= player.fuel_max
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
		# C26：任务行格式串走 tr()（BASE_MISSION_FMT），语言切换标点随 locale 变化
		var text := tr("BASE_MISSION_FMT") % [tr("MISSION_%s_NAME" % String(id).to_upper()), tr("MISSION_%s_DESC" % String(id).to_upper()), mini(progress, goal), goal]
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


## A7：测试/诊断经公开接口（动作包装）
func repair() -> void:
	_on_repair_pressed()


func recharge() -> void:
	_on_recharge_pressed()


func choose_route(line: StringName, buff_id: StringName) -> void:
	_on_route_pressed(line, buff_id)


func claim_mission(id: StringName) -> void:
	_on_claim_pressed(id)


func resume() -> void:
	_on_resume_pressed()


func _on_repair_pressed() -> void:
	# 2RP 回满（对齐原作，不按缺口计价）
	if GameState.spend_rp(GameState.RP_REPAIR_COST):
		GameState.heal(GameState.max_health() - GameState.health)
		GameState.play_sfx(GameState.SFX_RESUPPLY)
		_refresh()


func _on_recharge_pressed() -> void:
	var player := GameState.player_ref as Player  # A5：走注册表，替代 group 现找
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
