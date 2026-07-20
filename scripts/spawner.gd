extends Node
## 敌机生成器：计时波次 + Boss 触发（每 1500 分或每 90s，取先到者）。

signal boss_spawned(boss: Boss)

const ENEMY_SCENE: PackedScene = preload("res://scenes/enemy.tscn")
const BOSS_SCENE: PackedScene = preload("res://scenes/boss.tscn")
const ENEMY_TEXTURES: Array[Texture2D] = [
	preload("res://assets/sprites/enemy_ship_1.png"),
	preload("res://assets/sprites/enemy_ship_2.png"),
	preload("res://assets/sprites/enemy_ship_3.png"),
	preload("res://assets/sprites/enemy_ship_4.png"),
]
const ELITE_TEXTURES: Array[Texture2D] = [
	preload("res://assets/sprites/elite_ship_1.png"),
	preload("res://assets/sprites/elite_ship_2.png"),
	preload("res://assets/sprites/elite_ship_3.png"),
]
# 4 张贴图对应 4 种移动策略
const STRATEGIES: Array[StringName] = [&"straight", &"sine", &"zigzag", &"dive"]

const SPAWN_INTERVAL_START := 1.2
const SPAWN_INTERVAL_END := 0.5
const RAMP_TIME := 300.0
const BOSS_SCORE_STEP := 1500
const BOSS_TIME_LIMIT := 90.0

var _spawn_timer: float = 1.5
var _elapsed: float = 0.0
var _boss_timer: float = 0.0
var _next_boss_score: int = BOSS_SCORE_STEP
var _boss_active: bool = false


func _process(delta: float) -> void:
	_elapsed += delta
	_spawn_timer -= delta
	if _spawn_timer <= 0.0:
		_spawn_enemy()
		_spawn_timer = _current_interval()

	_boss_timer += delta
	if not _boss_active and (GameState.score >= _next_boss_score or _boss_timer >= BOSS_TIME_LIMIT):
		_spawn_boss()


func _current_interval() -> float:
	var base := lerpf(SPAWN_INTERVAL_START, SPAWN_INTERVAL_END, clampf(_elapsed / RAMP_TIME, 0.0, 1.0))
	return clampf(base / (1.0 + 0.15 * (GameState.difficulty_multiplier - 1.0)), 0.35, SPAWN_INTERVAL_START)


func _spawn_enemy() -> void:
	var elite_chance := clampf(0.03 + GameState.score / 15000.0, 0.0, 0.25)
	var is_elite := randf() < elite_chance
	var e := ENEMY_SCENE.instantiate() as Enemy
	var texture: Texture2D
	var strategy: StringName = &"straight"
	if is_elite:
		texture = ELITE_TEXTURES[randi() % ELITE_TEXTURES.size()]
		strategy = STRATEGIES[randi() % STRATEGIES.size()]
	else:
		var idx := randi() % ENEMY_TEXTURES.size()
		texture = ENEMY_TEXTURES[idx]
		strategy = STRATEGIES[idx]
	e.setup(texture, strategy, is_elite, GameState.difficulty_multiplier, randf() < 0.33)
	e.position = Vector2(randf_range(60.0, 1860.0), -60.0)
	get_parent().add_child(e)


func _spawn_boss() -> void:
	_boss_active = true
	var boss := BOSS_SCENE.instantiate() as Boss
	boss.setup(GameState.difficulty_multiplier)
	boss.position = Vector2(960.0, -160.0)
	boss.died.connect(_on_boss_died)
	get_parent().add_child(boss)
	boss_spawned.emit(boss)


func _on_boss_died() -> void:
	_boss_active = false
	_boss_timer = 0.0
	_next_boss_score += BOSS_SCORE_STEP
