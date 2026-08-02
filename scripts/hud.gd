extends CanvasLayer
## HUD：分数/击杀（左上）、难度（右上）、生命（左下）、Boss 血条（顶部，
## 带 70%/30% 阶段刻度线与阶段切换短闪，逃跑最后 10s 血条下方倒计时）。
## Buff 收起态为右下角单行图标坞（最新 4 个 + 溢出 +N），L 键展开右缘滚动明细栏
## （Esc 经 BackNavigator 优先关栏），与左下状态区、底部居中蓄力提示、左中通讯浮层分角隔离。

const FONT: FontFile = preload("res://assets/fonts/NotoSansSC.ttf")

@onready var _score_label: Label = $ScoreLabel
@onready var _kills_label: Label = $KillsLabel
@onready var _difficulty_label: Label = $DifficultyLabel
@onready var _lives_label: Label = $LivesLabel
@onready var _hp_bar: SegmentedBar = $HpBar
@onready var _boss_bar: SegmentedBar = $BossBar
@onready var _fuel_bar: SegmentedBar = $FuelBar
@onready var _dash_bar: SegmentedBar = $DashBar
@onready var _fuel_tag: Label = $FuelTag
@onready var _dash_tag: Label = $DashTag
@onready var _dock_tag: Label = $DockTag

var _banner_plate: ChamferedPanel
var _banner_label: Label

var _mag_box: HBoxContainer
var _mag_cells_nodes: Array[ColorRect] = []
var _home_charge_label: Label
var _give_up_label: Label
var _early_leave_box: VBoxContainer
var _early_leave_label: Label
var _early_leave_fill: ColorRect
var _main: Node = null
var _poll_timer: float = 0.0
var _last_dock_text: String = ""
var _last_mag_cells: int = -1
var _tag_labels: Array[Label] = []
var _tag_keys: Array[StringName] = []
var _event_box: VBoxContainer
var _event_bar: SegmentedBar
var _event_title: Label
var _event_turrets_label: Label
var _last_event_alive: int = -1
var _boss: Boss = null  # 当前血条绑定的 Boss（逃跑倒计时轮询用；died 时清空）
var _boss_countdown: Label
var _boss_name: Label  # Boss 名牌（型号 + 阶段），血条子节点随其显隐
var _boss_phase: int = Boss.FightPhase.P1
var POLL_INTERVAL := 0.1  # 仪表类刷新降频（信号驱动的文本不受影响）
# 受击/低血屏幕反馈（effects.hit_flash / effects.low_hp，_ready 缓存）
var HIT_FLASH_ALPHA := 0.55
var HIT_FLASH_TIME := 0.25
var LOW_HP_RATIO := 0.2
var LOW_HP_PULSE_MIN := 0.15
var LOW_HP_PULSE_MAX := 0.3
var LOW_HP_PULSE_PERIOD := 1.2
var _vignette: TextureRect
var _hit_flash: float = 0.0
var _hit_tween: Tween = null
var _last_hp_value: float = -1.0
var _pulse_time: float = 0.0
var _cached_max_hp: float = 100.0  # 缓存 max_health()（extra_life 层数驱动，buffs_changed 刷新；D08）
var _buff_dock_wrap: Control  # 右下角锚定包装（meta_jitter 抖动对象，避免直接动自动生长的网格）
var _buff_dock: GridContainer
var _buff_tag: Label
var _buff_overflow_label: Label = null  # 收起态溢出计数（">4 个 buff 时 +N"）
var _buff_panel: ChamferedPanel  # L 键展开的 buff 滚动栏
var _buff_panel_title: Label
var _buff_rows: VBoxContainer
var _last_buff_signature: String = ""
var _info_plate: ChamferedPanel
var _info_label: Label
var _info_tween: Tween = null
# Meta HUD DYING 抖动（D9）：仅 _hp_bar 与 buff 坞两控件的静止位与补间
var _hp_bar_rest: Vector2
var _buff_dock_rest: Vector2
var _jitter_tween: Tween = null

const BUFF_DOCK_MAX_TILES := 4  # 收起态最多展示的瓦片数（最新 4 个），超出折叠为 +N 溢出格


## Boss 血条阶段刻度线（70%/30%，§4.2）：随血条显隐的覆盖层
class _BossBarTicks:
	extends Control
	var ratios: PackedFloat32Array = [0.7, 0.3]

	func _draw() -> void:
		for r in ratios:
			var x := size.x * r
			draw_line(Vector2(x, -2.0), Vector2(x, size.y + 2.0), Color(1.0, 1.0, 1.0, 0.55), 2.0)


func _ready() -> void:
	add_to_group("hud")
	_main = get_parent()  # A5：HUD 是 main 子节点，_ready 直接缓存，替代 0.1s 轮询现找
	POLL_INTERVAL = GameState.cfg("effects.hud_poll_interval", POLL_INTERVAL)
	HIT_FLASH_ALPHA = GameState.cfg("effects.hit_flash.alpha", HIT_FLASH_ALPHA)
	HIT_FLASH_TIME = GameState.cfg("effects.hit_flash.time", HIT_FLASH_TIME)
	LOW_HP_RATIO = GameState.cfg("effects.low_hp.ratio", LOW_HP_RATIO)
	LOW_HP_PULSE_MIN = GameState.cfg("effects.low_hp.pulse_min", LOW_HP_PULSE_MIN)
	LOW_HP_PULSE_MAX = GameState.cfg("effects.low_hp.pulse_max", LOW_HP_PULSE_MAX)
	LOW_HP_PULSE_PERIOD = GameState.cfg("effects.low_hp.pulse_period", LOW_HP_PULSE_PERIOD)
	for label: Label in [_score_label, _kills_label, _difficulty_label, _lives_label]:
		label.add_theme_font_override("font", FONT)
	_score_label.add_theme_font_size_override("font_size", UITheme.FONT_SCORE)
	_score_label.add_theme_color_override("font_color", UITheme.TEXT)
	_kills_label.add_theme_font_size_override("font_size", UITheme.FONT_HUD)
	_kills_label.add_theme_color_override("font_color", UITheme.TEXT_DIM)
	_difficulty_label.add_theme_font_size_override("font_size", UITheme.FONT_HUD)
	_difficulty_label.add_theme_color_override("font_color", UITheme.ACCENT)
	_lives_label.add_theme_font_size_override("font_size", UITheme.FONT_HUD_L)
	_lives_label.add_theme_color_override("font_color", UITheme.TEXT)
	for tag: Label in [_fuel_tag, _dash_tag, _dock_tag]:
		tag.add_theme_font_override("font", FONT)
		tag.add_theme_font_size_override("font_size", UITheme.FONT_SMALL)
		tag.add_theme_color_override("font_color", UITheme.TEXT_DIM)
	_dock_tag.add_theme_color_override("font_color", UITheme.ACCENT)
	_hp_bar.fill_color = UITheme.ACCENT
	_fuel_bar.fill_color = UITheme.ACCENT
	_dash_bar.fill_color = UITheme.ACCENT
	# HpBar 全息化（META_HUD_DESIGN §4.3/§6 明示层）：底盘更透 + 填充段 ADD 伪泛光
	_hp_bar.empty_color = Color(0.05, 0.09, 0.14, 0.25)
	var hp_holo := CanvasItemMaterial.new()
	hp_holo.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	_hp_bar.material = hp_holo
	GameState.score_changed.connect(_on_score_changed)
	GameState.health_changed.connect(_on_health_changed)
	GameState.difficulty_changed.connect(_on_difficulty_changed)
	GameState.difficulty_selected.connect(_on_difficulty_selected)
	GameState.locale_changed.connect(_on_locale_changed)
	_on_score_changed(GameState.score)
	_on_health_changed(GameState.health)
	_refresh_difficulty_label()
	_fuel_tag.text = tr("UI_FUEL")
	_dash_tag.text = tr("UI_DASH")
	_build_backplates()
	_build_banner()
	_build_magazine_bar()
	# 返航蓄力提示（底部居中）
	_home_charge_label = Label.new()
	_home_charge_label.set_anchors_preset(Control.PRESET_CENTER_BOTTOM)
	_home_charge_label.position = Vector2(-140.0, -120.0)
	_home_charge_label.custom_minimum_size = Vector2(280.0, 0.0)
	_home_charge_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_home_charge_label.add_theme_font_override("font", FONT)
	_home_charge_label.add_theme_font_size_override("font_size", 24)
	_home_charge_label.add_theme_color_override("font_color", UITheme.CHARGE_CYAN)
	_home_charge_label.visible = false
	add_child(_home_charge_label)
	# 放弃出击蓄力提示（底部居中，返航提示上方，红色警示）
	_give_up_label = Label.new()
	_give_up_label.set_anchors_preset(Control.PRESET_CENTER_BOTTOM)
	_give_up_label.position = Vector2(-140.0, -164.0)
	_give_up_label.custom_minimum_size = Vector2(280.0, 0.0)
	_give_up_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_give_up_label.add_theme_font_override("font", FONT)
	_give_up_label.add_theme_font_size_override("font_size", 24)
	_give_up_label.add_theme_color_override("font_color", UITheme.DANGER)
	_give_up_label.visible = false
	add_child(_give_up_label)
	# 提前离舰蓄力进度条（驻留母舰时长按 H，底部居中，放弃提示上方）
	_early_leave_box = VBoxContainer.new()
	_early_leave_box.set_anchors_preset(Control.PRESET_CENTER_BOTTOM)
	_early_leave_box.position = Vector2(-140.0, -220.0)
	_early_leave_box.custom_minimum_size = Vector2(280.0, 0.0)
	_early_leave_box.add_theme_constant_override("separation", 6)
	_early_leave_box.visible = false
	add_child(_early_leave_box)
	_early_leave_label = Label.new()
	_early_leave_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_early_leave_label.add_theme_font_override("font", FONT)
	_early_leave_label.add_theme_font_size_override("font_size", 24)
	_early_leave_label.add_theme_color_override("font_color", UITheme.WARN_YELLOW)
	_early_leave_box.add_child(_early_leave_label)
	var bar_bg := ColorRect.new()
	bar_bg.color = Color(1.0, 1.0, 1.0, 0.15)
	bar_bg.custom_minimum_size = Vector2(280.0, 10.0)
	_early_leave_box.add_child(bar_bg)
	_early_leave_fill = ColorRect.new()
	_early_leave_fill.color = UITheme.WARN_YELLOW
	_early_leave_fill.set_anchors_preset(Control.PRESET_LEFT_WIDE)
	_early_leave_fill.anchor_right = 0.0
	bar_bg.add_child(_early_leave_fill)
	# 名牌行占位：血条整体下移 30px，上方留出一行型号 + 阶段标签
	_boss_bar.offset_top += 30.0
	_boss_bar.offset_bottom += 30.0
	# Boss 名牌（型号 + 阶段，血条子节点随其显隐；事件与 Boss 互斥不会同屏）
	# 深色底衬保证叠在 Boss 机体/辉光上时可读
	var name_plate := PanelContainer.new()
	name_plate.set_anchors_preset(Control.PRESET_CENTER_TOP)
	name_plate.position = Vector2(-300.0, -34.0)
	name_plate.custom_minimum_size = Vector2(600.0, 0.0)
	name_plate.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var plate_style := StyleBoxFlat.new()
	plate_style.bg_color = Color(0.02, 0.05, 0.09, 0.6)
	name_plate.add_theme_stylebox_override("panel", plate_style)
	_boss_name = Label.new()
	_boss_name.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_boss_name.add_theme_font_override("font", FONT)
	_boss_name.add_theme_font_size_override("font_size", UITheme.FONT_HUD)
	_boss_name.add_theme_color_override("font_color", UITheme.TEXT)
	_boss_name.mouse_filter = Control.MOUSE_FILTER_IGNORE
	name_plate.add_child(_boss_name)
	_boss_bar.add_child(name_plate)
	# Boss 血条阶段刻度线（70%/30%，覆盖在血条上随其显隐）
	var ticks := _BossBarTicks.new()
	ticks.set_anchors_preset(Control.PRESET_FULL_RECT)
	ticks.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_boss_bar.add_child(ticks)
	# Boss 逃跑倒计时（血条下方，剩余 ≤10s 起显示，红色闪烁）
	_boss_countdown = Label.new()
	_boss_countdown.set_anchors_preset(Control.PRESET_CENTER_TOP)
	_boss_countdown.position = Vector2(-100.0, 78.0)
	_boss_countdown.custom_minimum_size = Vector2(200.0, 0.0)
	_boss_countdown.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_boss_countdown.add_theme_font_override("font", FONT)
	_boss_countdown.add_theme_font_size_override("font_size", UITheme.FONT_HUD_L)
	_boss_countdown.add_theme_color_override("font_color", UITheme.DANGER)
	_boss_countdown.visible = false
	add_child(_boss_countdown)
	_build_event_bar()
	_build_vignette()
	_build_buff_dock()
	_build_info_banner()
	GameState.buffs_changed.connect(_rebuild_buff_dock)
	GameState.key_bindings_changed.connect(_refresh_buff_tag)
	_rebuild_buff_dock()
	_hp_bar_rest = _hp_bar.position
	_buff_dock_rest = _buff_dock_wrap.position


## 精英炮塔事件计时条（顶部居中，Boss 血条下方；与 Boss 互斥不会同屏）
func _build_event_bar() -> void:
	_event_box = VBoxContainer.new()
	_event_box.set_anchors_preset(Control.PRESET_CENTER_TOP)
	_event_box.position = Vector2(-300.0, 52.0)
	_event_box.custom_minimum_size = Vector2(600.0, 0.0)
	_event_box.add_theme_constant_override("separation", 4)
	_event_box.visible = false
	add_child(_event_box)
	_event_title = Label.new()
	_event_title.text = tr("ETV_TITLE")
	_event_title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_event_title.add_theme_font_override("font", FONT)
	_event_title.add_theme_font_size_override("font_size", 18)
	_event_title.add_theme_color_override("font_color", UITheme.EVENT_MAGENTA)
	_event_box.add_child(_event_title)
	_event_bar = SegmentedBar.new()
	_event_bar.custom_minimum_size = Vector2(600.0, 12.0)
	_event_bar.segments = 30
	_event_bar.fill_color = UITheme.EVENT_MAGENTA
	_event_box.add_child(_event_bar)
	_event_turrets_label = Label.new()
	_event_turrets_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_event_turrets_label.add_theme_font_override("font", FONT)
	_event_turrets_label.add_theme_font_size_override("font_size", UITheme.FONT_SMALL)
	_event_turrets_label.add_theme_color_override("font_color", UITheme.TEXT_DIM)
	_event_box.add_child(_event_turrets_label)


## 事件倒计时开始：显示计时条（total = 炮台总数）
func show_event_bar(total: int) -> void:
	_event_title.text = tr("ETV_TITLE")
	_event_bar.value = 100.0
	_last_event_alive = -1
	_event_turrets_label.text = tr("ETV_TURRETS") % total
	_event_box.visible = true


## 事件进行：剩余时间填充 + 剩余炮台数（约 0.1s 节流由调用侧控制）
func update_event_bar(time_left: float, duration: float, alive: int) -> void:
	if not _event_box.visible:
		return
	_event_bar.value = clampf(time_left / maxf(duration, 0.01), 0.0, 1.0) * 100.0
	if alive != _last_event_alive:
		_last_event_alive = alive
		_event_turrets_label.text = tr("ETV_TURRETS") % alive


func hide_event_bar() -> void:
	_event_box.visible = false


## 放弃出击蓄力进度：ratio < 0 隐藏，否则显示百分比
func set_give_up_charge(ratio: float) -> void:
	if ratio < 0.0:
		_give_up_label.visible = false
	else:
		_give_up_label.visible = true
		_give_up_label.text = tr("GIVE_UP_CHARGE") % int(clampf(ratio, 0.0, 1.0) * 100.0)


## 返航蓄力进度：ratio < 0 隐藏，否则显示百分比
func set_home_charge(ratio: float) -> void:
	if ratio < 0.0:
		_home_charge_label.visible = false
	else:
		_home_charge_label.visible = true
		_home_charge_label.text = tr("HOME_CHARGE") % int(clampf(ratio, 0.0, 1.0) * 100.0)


## 提前离舰蓄力进度条（驻留母舰时长按 H）：ratio < 0 隐藏，否则显示百分比 + 进度条
func set_early_leave_charge(ratio: float) -> void:
	if ratio < 0.0:
		_early_leave_box.visible = false
	else:
		var r := clampf(ratio, 0.0, 1.0)
		_early_leave_box.visible = true
		_early_leave_label.text = tr("MS_EARLY_LEAVE") % int(r * 100.0)
		_early_leave_fill.anchor_right = r


func _build_magazine_bar() -> void:
	# 弹匣格子条（驻留时显示）：10 格分段
	_mag_box = HBoxContainer.new()
	_mag_box.set_anchors_preset(Control.PRESET_BOTTOM_LEFT)
	_mag_box.position = Vector2(340.0, -54.0)
	_mag_box.add_theme_constant_override("separation", 3)
	_mag_box.visible = false
	for i in 10:
		var cell := ColorRect.new()
		cell.custom_minimum_size = Vector2(18.0, 14.0)
		cell.color = UITheme.ACCENT
		_mag_box.add_child(cell)
		_mag_cells_nodes.append(cell)
	add_child(_mag_box)


## L（buff_panel）切换 buff 滚动栏；暂停态下 HUD 不处理输入（process 继承）
func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed(&"buff_panel"):
		_toggle_buff_panel()
		get_viewport().set_input_as_handled()


func _process(delta: float) -> void:
	_update_vignette(delta)	# 仪表类刷新降频到 0.1s（文本类由信号驱动，见 _ready 连接）
	_poll_timer -= delta
	if _poll_timer > 0.0:
		return
	_poll_timer = POLL_INTERVAL
	# Boss 逃跑倒计时（约 0.1s 节流轮询，§4.5）：血条存在且剩余 ≤10s 起显示
	if _boss != null and is_instance_valid(_boss) and _boss_bar.visible:
		var remaining: float = _boss.escape_remaining()
		if _boss.is_in_fight() and not _boss.is_escaping() and remaining <= _boss.ESCAPE_COUNTDOWN_FROM and remaining > 0.0:
			_boss_countdown.visible = true
			_boss_countdown.text = "%d" % ceili(remaining)
			_boss_countdown.modulate.a = 1.0 if int(Time.get_ticks_msec() / 500) % 2 == 0 else 0.45
		else:
			_boss_countdown.visible = false
	else:
		_boss_countdown.visible = false
	var player := GameState.player_ref as Player
	if player == null:
		return
	var fuel := player.fuel_ratio()
	_fuel_bar.value = fuel * 100.0
	_fuel_bar.fill_color = UITheme.DANGER if fuel < 0.3 else UITheme.ACCENT
	_dash_bar.value = player.dash_ready_ratio() * 100.0
	if _main != null:
		var dock_text: String = _main.dock_status_text()
		if dock_text != _last_dock_text:
			_dock_tag.text = dock_text
			_last_dock_text = dock_text
		_update_magazine_bar(_main)


func _update_magazine_bar(main: Node) -> void:
	var ms: Mothership = main.mothership()
	if ms != null and ms.state() == Mothership.State.STAY:
		_mag_box.visible = true
		if ms.mag_cells() == _last_mag_cells:
			return
		_last_mag_cells = ms.mag_cells()
		for i in _mag_cells_nodes.size():
			_mag_cells_nodes[i].color = (
				UITheme.ACCENT if i < ms.mag_cells() else Color(0.05, 0.09, 0.14, 0.8)
			)
	else:
		_mag_box.visible = false
		_last_mag_cells = -1


## 左上分数块、左下状态块与右上难度块的切角背板 + 小标签（标签置于背板上方外侧，不与边框/数值重叠）
func _build_backplates() -> void:
	var score_plate := ChamferedPanel.new()
	score_plate.position = Vector2(10.0, 24.0)
	score_plate.size = Vector2(230.0, 92.0)
	add_child(score_plate)
	move_child(score_plate, 0)
	# 大数值下移，给标签行留位
	_score_label.position = Vector2(24.0, 30.0)
	_kills_label.position = Vector2(24.0, 72.0)
	var score_tag := _make_corner_tag(tr("UI_SCORE_TAG"))
	score_tag.position = Vector2(22.0, 2.0)
	add_child(score_tag)
	var status_plate := ChamferedPanel.new()
	status_plate.set_anchors_preset(Control.PRESET_BOTTOM_LEFT)
	status_plate.position = Vector2(10.0, -120.0)
	status_plate.size = Vector2(560.0, 110.0)
	add_child(status_plate)
	move_child(status_plate, 0)
	var lives_tag := _make_corner_tag(tr("UI_LIVES_TAG"))
	lives_tag.set_anchors_preset(Control.PRESET_BOTTOM_LEFT)
	lives_tag.position = Vector2(22.0, -140.0)
	add_child(lives_tag)
	# 仪表区与母舰状态区之间的竖分隔线（分区结构感）
	var status_divider := ColorRect.new()
	status_divider.color = UITheme.ACCENT_DIM
	status_divider.set_anchors_preset(Control.PRESET_BOTTOM_LEFT)
	status_divider.position = Vector2(374.0, -92.0)
	status_divider.size = Vector2(1.0, 56.0)
	status_divider.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(status_divider)
	# 右上难度背板：与分数块同语系（原浮空文字难以在亮背景上阅读）
	var diff_plate := ChamferedPanel.new()
	diff_plate.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	diff_plate.position = Vector2(-240.0, 24.0)
	diff_plate.size = Vector2(230.0, 44.0)
	add_child(diff_plate)
	move_child(diff_plate, 0)
	_difficulty_label.offset_left = -228.0
	_difficulty_label.offset_top = 24.0
	_difficulty_label.offset_right = -22.0
	_difficulty_label.offset_bottom = 68.0
	_difficulty_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	var diff_tag := _make_corner_tag(tr("UI_DIFF_TAG"))
	diff_tag.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	diff_tag.position = Vector2(-238.0, 2.0)
	add_child(diff_tag)
	# 刷新时同步小标签语言
	_tag_labels = [score_tag, lives_tag, diff_tag]
	_tag_keys = [&"UI_SCORE_TAG", &"UI_LIVES_TAG", &"UI_DIFF_TAG"]


## 角落板块小标签（分数/生命/难度共用样式）
func _make_corner_tag(text: String) -> Label:
	var tag := Label.new()
	tag.text = text
	tag.add_theme_font_override("font", FONT)
	tag.add_theme_font_size_override("font_size", UITheme.FONT_SMALL)
	tag.add_theme_color_override("font_color", UITheme.ACCENT)
	return tag


## 语言切换时重刷全部角落小标签
func _refresh_tag_labels() -> void:
	for i in mini(_tag_labels.size(), _tag_keys.size()):
		_tag_labels[i].text = tr(_tag_keys[i])


## 世界坐标处的飘字提示（补给完成、里程碑等）。
func show_popup(text: String, world_pos: Vector2) -> void:
	var label := Label.new()
	label.text = text
	label.add_theme_font_override("font", FONT)
	label.add_theme_font_size_override("font_size", UITheme.FONT_HUD_L)
	label.add_theme_color_override("font_color", UITheme.TEXT)
	# 世界坐标 → CanvasLayer 屏幕坐标（修视角 zoom≠1 时错位）
	label.position = get_viewport().get_canvas_transform() * world_pos - Vector2(40.0, 40.0)
	add_child(label)
	var tween := create_tween()
	tween.set_parallel(true)
	tween.tween_property(label, "position:y", label.position.y - 50.0, 0.8)
	tween.tween_property(label, "modulate:a", 0.0, 0.8)
	tween.chain().tween_callback(label.queue_free)


func _build_banner() -> void:
	_banner_plate = ChamferedPanel.new()
	_banner_plate.set_anchors_preset(Control.PRESET_CENTER_TOP)
	_banner_plate.position = Vector2(-300.0, 140.0)
	_banner_plate.size = Vector2(600.0, 80.0)
	_banner_plate.brackets = true
	_banner_plate.bg_color = UITheme.BANNER_DANGER_BG
	_banner_plate.border_color = Color(UITheme.DANGER, 0.6)
	_banner_plate.bracket_color = UITheme.DANGER
	_banner_plate.visible = false
	add_child(_banner_plate)
	_banner_label = Label.new()
	_banner_label.set_anchors_preset(Control.PRESET_CENTER_TOP)
	_banner_label.position = Vector2(-300.0, 140.0)
	_banner_label.custom_minimum_size = Vector2(600.0, 80.0)
	_banner_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_banner_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_banner_label.add_theme_font_override("font", FONT)
	_banner_label.add_theme_font_size_override("font_size", 40)
	_banner_label.add_theme_color_override("font_color", UITheme.DANGER)
	_banner_label.visible = false
	add_child(_banner_label)


## Boss 出场警告：闪烁 2s（与 spawner 的 2s 预警同步），随后淡出。
func show_boss_banner() -> void:
	_show_warning(tr("WARN_BOSS"))


## 母舰弹匣不足警告（≤4 格时触发一次）。
func show_magazine_warning() -> void:
	GameState.play_sfx(GameState.SFX_PLAYER_HIT)
	_show_warning(tr("WARN_MAG"))


## 对外公开接口（A1 修复）：Boss 逃跑警告经公开入口触发
func show_warning(text: String) -> void:
	_show_warning(text)


## A7：测试/诊断白盒断言经公开接口（UI 节点引用 getter）
func boss_countdown() -> Label:
	return _boss_countdown


func buff_dock() -> GridContainer:
	return _buff_dock


func buff_tag() -> Label:
	return _buff_tag


func buff_overflow_label() -> Label:
	return _buff_overflow_label


func buff_rows() -> VBoxContainer:
	return _buff_rows


func buff_panel_title() -> Label:
	return _buff_panel_title


func event_box() -> VBoxContainer:
	return _event_box


## A7 遗留清理：提前离舰蓄力条节点公开查询（测试替代 _ 直读）
func early_leave_box() -> VBoxContainer:
	return _early_leave_box


func early_leave_fill() -> ColorRect:
	return _early_leave_fill


func give_up_label() -> Label:
	return _give_up_label


func vignette() -> TextureRect:
	return _vignette


func toggle_buff_panel() -> void:
	_toggle_buff_panel()


func _show_warning(text: String) -> void:
	_banner_label.text = text
	_banner_plate.visible = true
	_banner_label.visible = true
	_banner_plate.modulate.a = 1.0
	_banner_label.modulate.a = 1.0
	var t1 := create_tween()
	t1.set_loops(4)
	t1.tween_property(_banner_plate, "modulate:a", 0.25, 0.25)
	t1.tween_property(_banner_plate, "modulate:a", 1.0, 0.25)
	t1.tween_property(_banner_plate, "modulate:a", 0.0, 0.4)
	t1.tween_callback(_banner_plate.hide)
	var t2 := create_tween()
	t2.set_loops(4)
	t2.tween_property(_banner_label, "modulate:a", 0.25, 0.25)
	t2.tween_property(_banner_label, "modulate:a", 1.0, 0.25)
	t2.tween_property(_banner_label, "modulate:a", 0.0, 0.4)
	t2.tween_callback(_banner_label.hide)


func show_boss_bar(boss: Boss) -> void:
	_boss_bar.fill_color = UITheme.ACCENT  # 重置上一只 Boss 狂暴留下的红色
	_boss_bar.visible = true
	_boss_bar.value = 100.0
	_boss = boss
	_boss_countdown.visible = false
	_boss_phase = Boss.FightPhase.P1
	boss.health_changed.connect(_on_boss_health_changed)
	boss.died.connect(_on_boss_died)
	boss.enraged.connect(_on_boss_enraged)
	boss.phase_changed.connect(_on_boss_phase_changed)
	_refresh_boss_name()


func _on_score_changed(new_score: int) -> void:
	_score_label.text = tr("UI_SCORE") % new_score
	_kills_label.text = tr("UI_KILLS") % GameState.kills


var _last_hp_text: String = ""
var _last_hp_int: int = -1  # P0-2：整数档位守卫，回血逐帧信号时跳过无变化格式化
var _last_max_int: int = -1  # P0-2：上限整数守卫（extra_life 叠加改变 max_hp 时强制刷新）


func _on_health_changed(new_health: float) -> void:
	var max_hp := _cached_max_hp
	# 受击红闪：HP 下降沿触发 alpha 脉冲（tween 衰减，低血脉动取两者较大值）
	if _last_hp_value >= 0.0 and new_health < _last_hp_value:
		_hit_flash = HIT_FLASH_ALPHA
		if _hit_tween != null and _hit_tween.is_valid():
			_hit_tween.kill()
		_hit_tween = create_tween()
		_hit_tween.tween_property(self, "_hit_flash", 0.0, HIT_FLASH_TIME)
	_last_hp_value = new_health
	_hp_bar.value = clampf(new_health / max_hp, 0.0, 1.0) * 100.0
	_hp_bar.fill_color = UITheme.DANGER if new_health / max_hp < 0.3 else UITheme.ACCENT
	# P0-2：回血每帧触发信号，仅整数档位/上限变化时才格式化（连续帧 HP 小数差异不刷新文本）
	var hp_int := ceili(new_health)
	var max_int := int(max_hp)
	if hp_int != _last_hp_int or max_int != _last_max_int:
		_last_hp_int = hp_int
		_last_max_int = max_int
		var text := "%d/%d" % [hp_int, max_int]
		if text != _last_hp_text:
			_lives_label.text = text
			_last_hp_text = text


func _on_difficulty_changed(_new_multiplier: float) -> void:
	_refresh_difficulty_label()


func _on_difficulty_selected(_difficulty: StringName) -> void:
	_refresh_difficulty_label()


func _on_locale_changed() -> void:
	_on_score_changed(GameState.score)
	_on_health_changed(GameState.health)
	_refresh_difficulty_label()
	_fuel_tag.text = tr("UI_FUEL")
	_dash_tag.text = tr("UI_DASH")
	_refresh_tag_labels()
	if _buff_tag != null:
		_refresh_buff_tag()
	if _buff_panel_title != null:
		_buff_panel_title.text = tr("UI_BUFFS_TITLE")
	if _event_box != null and _event_box.visible:
		_event_title.text = tr("ETV_TITLE")
		_event_turrets_label.text = tr("ETV_TURRETS") % maxi(_last_event_alive, 0)
	_rebuild_buff_dock(true)
	if _boss_bar.visible:
		_refresh_boss_name()


## 难度标签：Boss 击杀乘数 + 难度档位（如「难度 x1.00 · 中」）
func _refresh_difficulty_label() -> void:
	_difficulty_label.text = (
		tr("UI_DIFF_FMT") % [GameState.difficulty_multiplier, GameState.difficulty_label()]
	)


func _on_boss_health_changed(current: float, maximum: float) -> void:
	_boss_bar.value = clampf(current / maximum, 0.0, 1.0) * 100.0


func _on_boss_died() -> void:
	_boss_bar.visible = false
	_boss_countdown.visible = false
	_boss = null


func _on_boss_enraged() -> void:
	_boss_bar.fill_color = UITheme.DANGER
	_refresh_boss_name()


## 阶段切换瞬间血条短闪（§4.2）
func _on_boss_phase_changed(phase: int) -> void:
	_boss_phase = phase
	_refresh_boss_name()
	_boss_bar.modulate = Color(2.2, 2.2, 2.2)
	var tween := create_tween()
	tween.tween_property(_boss_bar, "modulate", Color.WHITE, 0.3)


## Boss 名牌：型号名 + 阶段标签（狂暴整行 DANGER）
func _refresh_boss_name() -> void:
	if _boss == null or not is_instance_valid(_boss):
		return
	var phase_text: String
	match _boss_phase:
		Boss.FightPhase.P2:
			phase_text = "P2"
		Boss.FightPhase.ENRAGE:
			phase_text = tr("BOSS_PHASE_ENRAGE")
		_:
			phase_text = "P1"
	_boss_name.text = "%s · %s" % [tr("BOSS_TYPE_%d" % _boss.boss_type), phase_text]
	_boss_name.add_theme_color_override(
		"font_color", UITheme.DANGER if _boss_phase == Boss.FightPhase.ENRAGE else UITheme.TEXT
	)


## 受击/低血屏幕反馈：全屏径向渐变（无新资产，GradientTexture2D 程序化）
func _build_vignette() -> void:
	var gradient := Gradient.new()
	gradient.set_color(0, Color(1.0, 0.2, 0.3, 0.0))
	gradient.set_color(1, Color(1.0, 0.2, 0.35, 1.0))
	var tex := GradientTexture2D.new()
	tex.gradient = gradient
	tex.fill = GradientTexture2D.FILL_RADIAL
	tex.fill_from = Vector2(0.5, 0.5)
	tex.fill_to = Vector2(1.0, 0.5)
	tex.width = 512
	tex.height = 512
	_vignette = TextureRect.new()
	_vignette.texture = tex
	_vignette.set_anchors_preset(Control.PRESET_FULL_RECT)
	_vignette.stretch_mode = TextureRect.STRETCH_SCALE
	_vignette.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_vignette.modulate.a = 0.0
	add_child(_vignette)
	move_child(_vignette, 0)


## 每帧算 vignette alpha：受击红闪衰减与低血正弦脉动取较大值，恢复后归 0
func _update_vignette(delta: float) -> void:
	if _vignette == null:
		return
	# LOD0 移交 MetaFX 后处理（D2），旧晕影恒 0；非 0（回退/MetaFX 离场）保留现状
	if GameState.meta_fx_lod == 0:
		if _vignette.modulate.a > 0.0:
			_vignette.modulate.a = 0.0
		return
	var alpha := _hit_flash
	var max_hp := _cached_max_hp
	if GameState.health > 0.0 and GameState.health < max_hp * LOW_HP_RATIO:
		_pulse_time += delta
		var s := (Enemy.sin_fast(_pulse_time * TAU / LOW_HP_PULSE_PERIOD) + 1.0) * 0.5
		alpha = maxf(alpha, lerpf(LOW_HP_PULSE_MIN, LOW_HP_PULSE_MAX, s))
	else:
		_pulse_time = 0.0
	_vignette.modulate.a = alpha


## Meta HUD DYING 抖动（D9）：只抖 _hp_bar 与 buff 坞包装两个控件（±px，80ms burst）
func meta_jitter(px: float = 2.0) -> void:
	if _jitter_tween != null and _jitter_tween.is_valid():
		_jitter_tween.kill()
	var off := Vector2(randf_range(-px, px), randf_range(-px, px))
	_hp_bar.position = _hp_bar_rest + off
	_buff_dock_wrap.position = _buff_dock_rest + off * 0.5
	_jitter_tween = create_tween()
	_jitter_tween.tween_property(_hp_bar, "position", _hp_bar_rest, 0.08)
	_jitter_tween.parallel().tween_property(_buff_dock_wrap, "position", _buff_dock_rest, 0.08)


## 右下 buff 区：收起态单行瓦片（最新 4 个 + 溢出 +N，标签带 [L] 快捷键提示），
## L 键展开右侧滚动栏（全部 buff 明细，不暂停对局）；与左下状态区/底部蓄力提示分角隔离。
func _build_buff_dock() -> void:
	_buff_dock_wrap = Control.new()
	_buff_dock_wrap.set_anchors_preset(Control.PRESET_BOTTOM_RIGHT)
	_buff_dock_wrap.position = Vector2(-16.0, -44.0)  # 底部留 28px 给标签行
	_buff_dock_wrap.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_buff_dock_wrap)
	_buff_dock = GridContainer.new()
	_buff_dock.columns = BUFF_DOCK_MAX_TILES + 1  # 瓦片 + 溢出格，恒单行
	_buff_dock.set_anchors_preset(Control.PRESET_BOTTOM_RIGHT)
	_buff_dock.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	_buff_dock.grow_vertical = Control.GROW_DIRECTION_BEGIN
	_buff_dock.add_theme_constant_override("h_separation", 6)
	_buff_dock.add_theme_constant_override("v_separation", 6)
	_buff_dock.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_buff_dock_wrap.add_child(_buff_dock)
	_buff_tag = Label.new()
	_buff_tag.set_anchors_preset(Control.PRESET_BOTTOM_RIGHT)
	_buff_tag.position = Vector2(-136.0, -24.0)
	_buff_tag.custom_minimum_size = Vector2(120.0, 18.0)
	_buff_tag.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_buff_tag.add_theme_font_override("font", FONT)
	_buff_tag.add_theme_font_size_override("font_size", UITheme.FONT_SMALL)
	_buff_tag.add_theme_color_override("font_color", UITheme.ACCENT)
	_buff_tag.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_buff_tag.visible = false
	add_child(_buff_tag)
	_refresh_buff_tag()
	_build_buff_panel()


## L 展开的 buff 滚动栏：右缘居中面板（标题 + 分隔线 + 滚动明细行），不暂停对局
func _build_buff_panel() -> void:
	_buff_panel = ChamferedPanel.new()
	_buff_panel.padding = 0.0
	_buff_panel.set_anchors_preset(Control.PRESET_CENTER_RIGHT)
	_buff_panel.position = Vector2(-356.0, -320.0)
	_buff_panel.size = Vector2(340.0, 640.0)
	_buff_panel.visible = false
	add_child(_buff_panel)
	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left", 14)
	margin.add_theme_constant_override("margin_right", 14)
	margin.add_theme_constant_override("margin_top", 12)
	margin.add_theme_constant_override("margin_bottom", 12)
	_buff_panel.add_child(margin)
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 10)
	margin.add_child(vbox)
	_buff_panel_title = UITheme.make_label(
		tr("UI_BUFFS_TITLE"), UITheme.FONT_HUD, UITheme.ACCENT, HORIZONTAL_ALIGNMENT_LEFT
	)
	vbox.add_child(_buff_panel_title)
	var divider := ColorRect.new()
	divider.color = UITheme.ACCENT_DIM
	divider.custom_minimum_size = Vector2(0.0, 1.0)
	divider.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	vbox.add_child(divider)
	var scroll := ScrollContainer.new()
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	vbox.add_child(scroll)
	_buff_rows = VBoxContainer.new()
	_buff_rows.add_theme_constant_override("separation", 6)
	_buff_rows.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(_buff_rows)


## 滚动栏明细行：字形 + 名称 + 层数（>1 时右侧 ×N）
func _make_buff_row(id: StringName, stacks: int) -> Control:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 10)
	row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_child(BuffIcons.make_glyph(id, BuffIcons.color_for(id), 24.0))
	var name_label := UITheme.make_label(
		tr("BUFF_%s_NAME" % String(id).to_upper()), UITheme.FONT_HUD, UITheme.TEXT, HORIZONTAL_ALIGNMENT_LEFT
	)
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(name_label)
	if stacks > 1:
		row.add_child(
			UITheme.make_label("×%d" % stacks, UITheme.FONT_HUD, UITheme.ACCENT_GOLD, HORIZONTAL_ALIGNMENT_RIGHT)
		)
	return row


## 收起态溢出格：46×46 同尺寸瓦片，中央 "+N"
func _make_overflow_tile(count: int) -> Control:
	var panel := ChamferedPanel.new()
	panel.chamfer = 7.0
	panel.padding = 0.0
	panel.custom_minimum_size = Vector2(46.0, 46.0)
	panel.border_color = Color(UITheme.ACCENT, 0.55)
	panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_buff_overflow_label = UITheme.make_label("+%d" % count, UITheme.FONT_CAPTION, UITheme.ACCENT)
	_buff_overflow_label.set_anchors_preset(Control.PRESET_FULL_RECT)
	_buff_overflow_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_buff_overflow_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	panel.add_child(_buff_overflow_label)
	return panel


## buffs_changed / locale_changed 驱动重建；内容签名不变不重建
func _rebuild_buff_dock(force: bool = false) -> void:
	_cached_max_hp = GameState.max_health()  # D08：buff 变化（extra_life 层数）时刷新缓存，热路径免查 JSON
	var signature := ""
	var active: Array = []  # [[id, stacks], ...] 按获得顺序
	for id: StringName in GameState.buffs:
		var stacks: int = GameState.buffs[id]
		if stacks > 0:
			signature += "%s:%d;" % [String(id), stacks]
			active.append([id, stacks])
	if not force and signature == _last_buff_signature:
		return
	_last_buff_signature = signature
	for tile: Control in _buff_dock.get_children():
		tile.queue_free()
	for row: Control in _buff_rows.get_children():
		row.queue_free()
	if active.is_empty():
		_buff_tag.visible = false
		_buff_panel.visible = false
		return
	_buff_tag.visible = true
	# 收起态单行：最新 BUFF_DOCK_MAX_TILES 个，更早的折叠为 +N 溢出格
	var shown: Array = active.slice(maxi(active.size() - BUFF_DOCK_MAX_TILES, 0))
	for entry: Array in shown:
		_buff_dock.add_child(UITheme.make_buff_tile(entry[0], entry[1]))
	var overflow := active.size() - shown.size()
	if overflow > 0:
		_buff_dock.add_child(_make_overflow_tile(overflow))
	# 滚动栏：全量明细行
	for entry: Array in active:
		_buff_rows.add_child(_make_buff_row(entry[0], entry[1]))


## buff 滚动栏开关（L 键路由至此；无 buff 时不展开）
func _toggle_buff_panel() -> void:
	if _buff_panel.visible:
		_buff_panel.visible = false
	elif not _last_buff_signature.is_empty():
		_buff_panel.visible = true


func is_buff_panel_open() -> bool:
	return _buff_panel.visible


## BackNavigator CLOSE_BUFF_PANEL 路由：Esc 先关栏再进暂停
func close_buff_panel() -> void:
	_buff_panel.visible = false


## 收起态标签：名称 + 当前绑定键提示（改键后同步刷新）
func _refresh_buff_tag() -> void:
	_buff_tag.text = "%s [%s]" % [tr("UI_BUFFS_TAG"), GameState.action_keys_text(&"buff_panel")]


## 信息横幅（母舰到达等）：切角板结构复用警告横幅，ACCENT 色系、不闪烁
func _build_info_banner() -> void:
	_info_plate = ChamferedPanel.new()
	_info_plate.set_anchors_preset(Control.PRESET_CENTER_TOP)
	_info_plate.position = Vector2(-300.0, 232.0)
	_info_plate.size = Vector2(600.0, 64.0)
	_info_plate.brackets = true
	_info_plate.bg_color = UITheme.BTN_PRIMARY_BG
	_info_plate.border_color = Color(UITheme.ACCENT, 0.6)
	_info_plate.bracket_color = UITheme.ACCENT
	_info_plate.visible = false
	add_child(_info_plate)
	_info_label = Label.new()
	_info_label.set_anchors_preset(Control.PRESET_CENTER_TOP)
	_info_label.position = Vector2(-300.0, 232.0)
	_info_label.custom_minimum_size = Vector2(600.0, 64.0)
	_info_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_info_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_info_label.add_theme_font_override("font", FONT)
	_info_label.add_theme_font_size_override("font_size", UITheme.FONT_TITLE)
	_info_label.add_theme_color_override("font_color", UITheme.ACCENT)
	_info_label.visible = false
	add_child(_info_label)


## 信息横幅：显示 ~1.6s 后淡出（位于警告横幅下方，不与其重叠）
func show_info_banner(text: String) -> void:
	_info_label.text = text
	_info_plate.visible = true
	_info_label.visible = true
	_info_plate.modulate.a = 1.0
	_info_label.modulate.a = 1.0
	if _info_tween != null and _info_tween.is_valid():
		_info_tween.kill()
	_info_tween = create_tween()
	_info_tween.tween_interval(1.6)
	_info_tween.set_parallel(true)
	_info_tween.tween_property(_info_plate, "modulate:a", 0.0, 0.4)
	_info_tween.tween_property(_info_label, "modulate:a", 0.0, 0.4)
	_info_tween.chain().tween_callback(_info_plate.hide)
	_info_tween.tween_callback(_info_label.hide)
