extends CanvasLayer
## HUD：分数/击杀（左上）、难度（右上）、生命（左下）、Boss 血条（顶部）。

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")

@onready var _score_label: Label = $ScoreLabel
@onready var _kills_label: Label = $KillsLabel
@onready var _difficulty_label: Label = $DifficultyLabel
@onready var _lives_label: Label = $LivesLabel
@onready var _boss_bar: ProgressBar = $BossBar


func _ready() -> void:
	for label: Label in [_score_label, _kills_label, _difficulty_label, _lives_label]:
		label.add_theme_font_override("font", FONT)
		label.add_theme_font_size_override("font_size", 28)
	GameState.score_changed.connect(_on_score_changed)
	GameState.lives_changed.connect(_on_lives_changed)
	GameState.difficulty_changed.connect(_on_difficulty_changed)
	_on_score_changed(GameState.score)
	_on_lives_changed(GameState.lives)
	_on_difficulty_changed(GameState.difficulty_multiplier)


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
