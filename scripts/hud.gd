extends CanvasLayer
## HUD：分数/击杀（左上）、难度（右上）、生命（左下）、Boss 血条（顶部）。

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")

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
var _main: Node = null
var _poll_timer: float = 0.0
var _last_dock_text: String = ""
var _last_mag_cells: int = -1
var _tag_labels: Array[Label] = []
var POLL_INTERVAL := 0.1  # 仪表类刷新降频（信号驱动的文本不受影响）


func _ready() -> void:
	add_to_group("hud")
	POLL_INTERVAL = GameState.cfg("effects.hud_poll_interval", POLL_INTERVAL)
	for label: Label in [_score_label, _kills_label, _difficulty_label, _lives_label]:
		label.add_theme_font_override("font", FONT)
	_score_label.add_theme_font_size_override("font_size", 32)
	_score_label.add_theme_color_override("font_color", UITheme.TEXT)
	_kills_label.add_theme_font_size_override("font_size", 20)
	_kills_label.add_theme_color_override("font_color", UITheme.TEXT_DIM)
	_difficulty_label.add_theme_font_size_override("font_size", 20)
	_difficulty_label.add_theme_color_override("font_color", UITheme.ACCENT)
	_lives_label.add_theme_font_size_override("font_size", 22)
	_lives_label.add_theme_color_override("font_color", UITheme.TEXT)
	for tag: Label in [_fuel_tag, _dash_tag, _dock_tag]:
		tag.add_theme_font_override("font", FONT)
		tag.add_theme_font_size_override("font_size", 16)
	_hp_bar.fill_color = UITheme.ACCENT
	_fuel_bar.fill_color = UITheme.ACCENT
	_dash_bar.fill_color = UITheme.ACCENT
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
	if _tag_labels.size() == 2:
		_tag_labels[0].text = tr("UI_SCORE_TAG")
		_tag_labels[1].text = tr("UI_LIVES_TAG")
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
	_home_charge_label.add_theme_color_override("font_color", Color(0.5, 0.9, 1.0))
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
	_give_up_label.add_theme_color_override("font_color", Color(1.0, 0.45, 0.35))
	_give_up_label.visible = false
	add_child(_give_up_label)


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


func _process(delta: float) -> void:
	# 仪表类刷新降频到 0.1s（文本类由信号驱动，见 _ready 连接）
	_poll_timer -= delta
	if _poll_timer > 0.0:
		return
	_poll_timer = POLL_INTERVAL
	var player := GameState.player_ref as Player
	if player == null:
		return
	var fuel := player.fuel_ratio()
	_fuel_bar.value = fuel * 100.0
	_fuel_bar.fill_color = UITheme.DANGER if fuel < 0.3 else UITheme.ACCENT
	_dash_bar.value = player.dash_ready_ratio() * 100.0
	if _main == null:
		_main = get_tree().get_first_node_in_group("main")
	if _main != null:
		var dock_text: String = _main.dock_status_text()
		if dock_text != _last_dock_text:
			_dock_tag.text = dock_text
			_last_dock_text = dock_text
		_update_magazine_bar(_main)


func _update_magazine_bar(main: Node) -> void:
	var ms: Mothership = main._mothership
	if ms != null and ms._state == Mothership.State.STAY:
		_mag_box.visible = true
		if ms._mag_cells == _last_mag_cells:
			return
		_last_mag_cells = ms._mag_cells
		for i in _mag_cells_nodes.size():
			_mag_cells_nodes[i].color = (
				UITheme.ACCENT if i < ms._mag_cells else Color(0.05, 0.09, 0.14, 0.8)
			)
	else:
		_mag_box.visible = false
		_last_mag_cells = -1


## 左上分数块与左下状态块的切角背板 + 小标签（标签置于背板上方外侧，不与边框/数值重叠）
func _build_backplates() -> void:
	var score_plate := ChamferedPanel.new()
	score_plate.position = Vector2(10.0, 24.0)
	score_plate.size = Vector2(230.0, 92.0)
	add_child(score_plate)
	move_child(score_plate, 0)
	# 大数值下移，给标签行留位
	_score_label.position = Vector2(24.0, 30.0)
	_kills_label.position = Vector2(24.0, 72.0)
	var score_tag := Label.new()
	score_tag.text = tr("UI_SCORE_TAG")
	score_tag.position = Vector2(22.0, 2.0)
	score_tag.add_theme_font_override("font", FONT)
	score_tag.add_theme_font_size_override("font_size", 16)
	score_tag.add_theme_color_override("font_color", UITheme.ACCENT)
	add_child(score_tag)
	var status_plate := ChamferedPanel.new()
	status_plate.set_anchors_preset(Control.PRESET_BOTTOM_LEFT)
	status_plate.position = Vector2(10.0, -82.0)
	status_plate.size = Vector2(560.0, 76.0)
	add_child(status_plate)
	move_child(status_plate, 0)
	var lives_tag := Label.new()
	lives_tag.text = tr("UI_LIVES_TAG")
	lives_tag.set_anchors_preset(Control.PRESET_BOTTOM_LEFT)
	lives_tag.position = Vector2(22.0, -102.0)
	lives_tag.add_theme_font_override("font", FONT)
	lives_tag.add_theme_font_size_override("font_size", 16)
	lives_tag.add_theme_color_override("font_color", UITheme.ACCENT)
	add_child(lives_tag)
	# 刷新时同步小标签语言
	_tag_labels = [score_tag, lives_tag]


## 世界坐标处的飘字提示（补给完成、里程碑等）。
func show_popup(text: String, world_pos: Vector2) -> void:
	var label := Label.new()
	label.text = text
	label.add_theme_font_override("font", FONT)
	label.add_theme_font_size_override("font_size", 22)
	label.add_theme_color_override("font_color", UITheme.TEXT)
	label.position = world_pos - Vector2(40.0, 40.0)
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
	_banner_plate.bg_color = Color(0.35, 0.06, 0.10, 0.7)
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
	_boss_bar.visible = true
	_boss_bar.value = 100.0
	boss.health_changed.connect(_on_boss_health_changed)
	boss.died.connect(_on_boss_died)
	boss.enraged.connect(_on_boss_enraged)


func _on_score_changed(new_score: int) -> void:
	_score_label.text = tr("UI_SCORE") % new_score
	_kills_label.text = tr("UI_KILLS") % GameState.kills


var _last_hp_text: String = ""


func _on_health_changed(new_health: float) -> void:
	var max_hp := GameState.max_health()
	_hp_bar.value = clampf(new_health / max_hp, 0.0, 1.0) * 100.0
	_hp_bar.fill_color = UITheme.DANGER if new_health / max_hp < 0.3 else UITheme.ACCENT
	var text := "%d/%d" % [ceili(new_health), int(max_hp)]
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
	if _tag_labels.size() == 2:
		_tag_labels[0].text = tr("UI_SCORE_TAG")
		_tag_labels[1].text = tr("UI_LIVES_TAG")


## 难度标签：Boss 击杀乘数 + 难度档位（如「难度 x1.00 · 中」）
func _refresh_difficulty_label() -> void:
	_difficulty_label.text = (
		tr("UI_DIFF_FMT") % [GameState.difficulty_multiplier, GameState.difficulty_label()]
	)


func _on_boss_health_changed(current: float, maximum: float) -> void:
	_boss_bar.value = clampf(current / maximum, 0.0, 1.0) * 100.0


func _on_boss_died() -> void:
	_boss_bar.visible = false


func _on_boss_enraged() -> void:
	_boss_bar.fill_color = UITheme.DANGER
