extends CanvasLayer
## HUD：分数/击杀（左上）、难度（右上）、生命（左下）、Boss 血条（顶部）。

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")

@onready var _score_label: Label = $ScoreLabel
@onready var _kills_label: Label = $KillsLabel
@onready var _difficulty_label: Label = $DifficultyLabel
@onready var _lives_label: Label = $LivesLabel
@onready var _boss_bar: ProgressBar = $BossBar
@onready var _fuel_bar: ProgressBar = $FuelBar
@onready var _dash_bar: ProgressBar = $DashBar
@onready var _fuel_tag: Label = $FuelTag
@onready var _dash_tag: Label = $DashTag
@onready var _dock_tag: Label = $DockTag

var _banner: PanelContainer
var _fuel_fill := StyleBoxFlat.new()
var _dash_fill := StyleBoxFlat.new()


func _ready() -> void:
	add_to_group("hud")
	for label: Label in [_score_label, _kills_label, _difficulty_label, _lives_label]:
		label.add_theme_font_override("font", FONT)
		label.add_theme_font_size_override("font_size", 28)
	for tag: Label in [_fuel_tag, _dash_tag, _dock_tag]:
		tag.add_theme_font_override("font", FONT)
		tag.add_theme_font_size_override("font_size", 16)
	_fuel_fill.bg_color = Color(0.25, 0.8, 0.9)
	_fuel_fill.set_corner_radius_all(3)
	_fuel_bar.add_theme_stylebox_override("fill", _fuel_fill)
	_dash_fill.bg_color = Color(0.95, 0.85, 0.3)
	_dash_fill.set_corner_radius_all(3)
	_dash_bar.add_theme_stylebox_override("fill", _dash_fill)
	GameState.score_changed.connect(_on_score_changed)
	GameState.lives_changed.connect(_on_lives_changed)
	GameState.difficulty_changed.connect(_on_difficulty_changed)
	_on_score_changed(GameState.score)
	_on_lives_changed(GameState.lives)
	_on_difficulty_changed(GameState.difficulty_multiplier)
	_build_banner()


func _process(_delta: float) -> void:
	var players := get_tree().get_nodes_in_group("player")
	if players.is_empty():
		return
	var player := players[0] as Player
	var fuel := player.fuel_ratio()
	_fuel_bar.value = fuel * 100.0
	_fuel_fill.bg_color = Color(0.9, 0.25, 0.2) if fuel < 0.3 else Color(0.25, 0.8, 0.9)
	_dash_bar.value = player.dash_ready_ratio() * 100.0
	var main := get_tree().get_first_node_in_group("main")
	if main != null:
		_dock_tag.text = main.dock_status_text()


## 拾取物等场景的飘字提示。
func show_popup(text: String, world_pos: Vector2) -> void:
	var label := Label.new()
	label.text = text
	label.add_theme_font_override("font", FONT)
	label.add_theme_font_size_override("font_size", 22)
	label.position = world_pos - Vector2(40.0, 40.0)
	add_child(label)
	var tween := create_tween()
	tween.set_parallel(true)
	tween.tween_property(label, "position:y", label.position.y - 50.0, 0.8)
	tween.tween_property(label, "modulate:a", 0.0, 0.8)
	tween.chain().tween_callback(label.queue_free)


func _build_banner() -> void:
	_banner = PanelContainer.new()
	_banner.set_anchors_preset(Control.PRESET_CENTER_TOP)
	_banner.position = Vector2(-280.0, 140.0)
	_banner.custom_minimum_size = Vector2(560.0, 0.0)
	_banner.visible = false
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.45, 0.04, 0.04, 0.8)
	style.set_content_margin_all(14.0)
	_banner.add_theme_stylebox_override("panel", style)
	var label := Label.new()
	label.text = "⚠ 警告：强敌接近 ⚠"
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.add_theme_font_override("font", FONT)
	label.add_theme_font_size_override("font_size", 44)
	label.add_theme_color_override("font_color", Color(1.0, 0.3, 0.25))
	_banner.add_child(label)
	add_child(_banner)


## Boss 出场警告：闪烁 2s（与 spawner 的 2s 预警同步），随后淡出。
func show_boss_banner() -> void:
	_banner.visible = true
	_banner.modulate.a = 1.0
	var tween := create_tween()
	tween.set_loops(4)
	tween.tween_property(_banner, "modulate:a", 0.25, 0.25)
	tween.tween_property(_banner, "modulate:a", 1.0, 0.25)
	tween.tween_property(_banner, "modulate:a", 0.0, 0.4)
	tween.tween_callback(_banner.hide)


func show_boss_bar(boss: Boss) -> void:
	_boss_bar.visible = true
	_boss_bar.value = 100.0
	boss.health_changed.connect(_on_boss_health_changed)
	boss.died.connect(_on_boss_died)


func _on_score_changed(new_score: int) -> void:
	_score_label.text = "分数：%d" % new_score
	_kills_label.text = "击杀：%d" % GameState.kills


func _on_lives_changed(new_lives: float) -> void:
	_lives_label.text = "生命：%d" % ceili(new_lives)


func _on_difficulty_changed(new_multiplier: float) -> void:
	_difficulty_label.text = "难度 x%.2f" % new_multiplier


func _on_boss_health_changed(current: float, maximum: float) -> void:
	_boss_bar.value = clampf(current / maximum, 0.0, 1.0) * 100.0


func _on_boss_died() -> void:
	_boss_bar.visible = false
