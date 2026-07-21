extends Node
## 敌机生成器：计时波次（按分数阶段解锁机型）+ Boss 触发（3 种轮换）。

signal boss_spawned(boss: Boss)
signal boss_warning

const ENEMY_SCENE: PackedScene = preload("res://scenes/enemy.tscn")
const BOSS_SCENE: PackedScene = preload("res://scenes/boss.tscn")

## 普通机型配置表（贴图即机型，数值差异化；弹种池仅 single/spread）
static var ENEMY_TYPES: Array[Dictionary] = [
	{  # 1 型 均衡
		"texture": preload("res://assets/sprites/enemy_ship_1.png"),
		"strategies": [&"straight", &"sine"] as Array[StringName],
		"hp": Vector2i(2, 3), "speed": Vector2(140, 180), "score": 100,
		"fire": 0.25, "fire_interval": 2.2, "scale": 0.45, "radius": 30.0,
		"bullet_types": [&"single", &"spread"] as Array[StringName],
	},
	{  # 2 型 高速低 HP
		"texture": preload("res://assets/sprites/enemy_ship_2.png"),
		"strategies": [&"zigzag", &"dive"] as Array[StringName],
		"hp": Vector2i(1, 2), "speed": Vector2(220, 280), "score": 150,
		"fire": 0.3, "fire_interval": 2.4, "scale": 0.45, "radius": 30.0,
		"bullet_types": [&"single", &"spread"] as Array[StringName],
	},
	{  # 3 型 高 HP 慢速
		"texture": preload("res://assets/sprites/enemy_ship_3.png"),
		"strategies": [&"spiral", &"hover"] as Array[StringName],
		"hp": Vector2i(5, 6), "speed": Vector2(90, 120), "score": 200,
		"fire": 0.4, "fire_interval": 2.0, "scale": 0.5, "radius": 34.0,
		"bullet_types": [&"spread", &"single"] as Array[StringName],
	},
	{  # 4 型 高分开火狂
		"texture": preload("res://assets/sprites/enemy_ship_4.png"),
		"strategies": [&"noise", &"hover", &"aggressive"] as Array[StringName],
		"hp": Vector2i(2, 3), "speed": Vector2(150, 190), "score": 250,
		"fire": 0.8, "fire_interval": 1.8, "scale": 0.45, "radius": 30.0,
		"bullet_types": [&"spread", &"single"] as Array[StringName],
	},
]

## 精英机型配置表（弹种池仅 spread/laser）
static var ELITE_TYPES: Array[Dictionary] = [
	{  # 重甲
		"texture": preload("res://assets/sprites/elite_ship_1.png"),
		"strategies": [&"straight", &"sine"] as Array[StringName],
		"hp": Vector2i(14, 16), "speed": Vector2(90, 110), "score": 400,
		"fire": 0.5, "fire_interval": 2.2, "scale": 0.7, "radius": 56.0, "elite": true,
		"bullet_types": [&"spread"] as Array[StringName],
	},
	{  # 游击
		"texture": preload("res://assets/sprites/elite_ship_2.png"),
		"strategies": [&"zigzag", &"dive", &"noise"] as Array[StringName],
		"hp": Vector2i(6, 8), "speed": Vector2(240, 300), "score": 350,
		"fire": 0.6, "fire_interval": 2.0, "scale": 0.5, "radius": 40.0, "elite": true,
		"bullet_types": [&"laser", &"spread"] as Array[StringName],
	},
	{  # 炮艇
		"texture": preload("res://assets/sprites/elite_ship_3.png"),
		"strategies": [&"hover", &"spiral"] as Array[StringName],
		"hp": Vector2i(9, 11), "speed": Vector2(110, 140), "score": 500,
		"fire": 1.0, "fire_interval": 1.5, "scale": 0.6, "radius": 48.0, "elite": true,
		"bullet_types": [&"spread", &"laser"] as Array[StringName],
	},
]

## 机型 i 在分数 >= UNLOCK_SCORES[i] 时解锁
const UNLOCK_SCORES: Array[int] = [0, 300, 800, 1500]
var ELITE_BONUS_SCORE := 1500  # 达到后精英率 +0.1

var SPAWN_INTERVAL_START := 1.2
var SPAWN_INTERVAL_END := 0.5
var RAMP_TIME := 300.0
var BOSS_SCORE_STEP := 1500
var BOSS_TIME_LIMIT := 90.0

var _spawn_timer: float = 1.5
var _elapsed: float = 0.0
var _boss_timer: float = 0.0
var _next_boss_score: int = BOSS_SCORE_STEP
var _boss_active: bool = false


func _ready() -> void:
	add_to_group("spawner")
	_apply_balance()


## 数值配置注入：机型表数值覆盖（贴图/策略/弹种池留在脚本），常量读入
func _apply_balance() -> void:
	SPAWN_INTERVAL_START = GameState.cfg("spawner.interval_start", SPAWN_INTERVAL_START)
	SPAWN_INTERVAL_END = GameState.cfg("spawner.interval_end", SPAWN_INTERVAL_END)
	RAMP_TIME = GameState.cfg("spawner.ramp_time", RAMP_TIME)
	BOSS_SCORE_STEP = GameState.cfg("spawner.boss_score_step", BOSS_SCORE_STEP)
	BOSS_TIME_LIMIT = GameState.cfg("spawner.boss_time_limit", BOSS_TIME_LIMIT)
	ELITE_BONUS_SCORE = GameState.cfg("spawner.elite_bonus_score", ELITE_BONUS_SCORE)
	var normal: Array = GameState.cfg("enemies.types", [])
	for i in mini(normal.size(), ENEMY_TYPES.size()):
		_merge_type(ENEMY_TYPES[i], normal[i])
	var elites: Array = GameState.cfg("elites.types", [])
	for i in mini(elites.size(), ELITE_TYPES.size()):
		_merge_type(ELITE_TYPES[i], elites[i])


func _merge_type(dst: Dictionary, src: Dictionary) -> void:
	if src.has("hp"):
		dst["hp"] = Vector2i(int(src["hp"][0]), int(src["hp"][1]))
	if src.has("speed"):
		dst["speed"] = Vector2(float(src["speed"][0]), float(src["speed"][1]))
	for k in ["score", "fire", "fire_interval", "scale", "radius"]:
		if src.has(k):
			dst[k] = src[k]


func _process(delta: float) -> void:
	_elapsed += delta
	_spawn_timer -= delta
	if _spawn_timer <= 0.0:
		_spawn_enemy()
		_spawn_timer = _current_interval()

	_boss_timer += delta
	if not _boss_active and (GameState.score >= _next_boss_score or _boss_timer >= BOSS_TIME_LIMIT):
		_trigger_boss()


func _current_interval() -> float:
	var base := lerpf(SPAWN_INTERVAL_START, SPAWN_INTERVAL_END, clampf(_elapsed / RAMP_TIME, 0.0, 1.0))
	# 难度倍率：easy ×1.25（更疏）/ medium ×1 / hard ×0.8（更密）
	var interval: float = (
		base
		* GameState.spawn_interval_multiplier()
		/ (1.0 + 0.15 * (GameState.difficulty_multiplier - 1.0))
	)
	return clampf(interval, 0.35, SPAWN_INTERVAL_START * GameState.spawn_interval_multiplier())


## 当前分数阶段已解锁的普通机型池
func unlocked_types() -> Array[Dictionary]:
	var pool: Array[Dictionary] = []
	for i in ENEMY_TYPES.size():
		if GameState.score >= UNLOCK_SCORES[i]:
			pool.append(ENEMY_TYPES[i])
	return pool


## 当前在屏的 spread 弹种敌机数（离场中的不计）
func _count_spread_enemies() -> int:
	var n := 0
	for node in get_tree().get_nodes_in_group("enemy"):
		var e := node as Enemy
		if e != null and e.bullet_type == &"spread" and not e._exiting:
			n += 1
	return n


## 从机型弹种池抽取弹种；spread 超同屏上限时退化（普通→single，精英→laser）。
## 同屏上限按难度取（GameState.spread_enemy_cap：easy 1 / medium 2 / hard 3）。
func _pick_bullet_type(config: Dictionary) -> StringName:
	var pool: Array = config.get("bullet_types", [&"single"])
	var btype: StringName = pool[randi() % pool.size()]
	if btype == &"spread" and _count_spread_enemies() >= GameState.spread_enemy_cap():
		btype = &"laser" if config.get("elite", false) else &"single"
	return btype


func _spawn_enemy() -> void:
	var elite_chance := clampf(GameState.cfg("spawner.elite_base_chance", 0.03) + GameState.score / GameState.cfg("spawner.elite_chance_per_score", 15000.0), 0.0, GameState.cfg("spawner.elite_chance_cap", 0.25))
	if GameState.score >= ELITE_BONUS_SCORE:
		elite_chance += GameState.cfg("spawner.elite_bonus_chance", 0.1)
	var config: Dictionary
	if randf() < elite_chance:
		config = ELITE_TYPES[randi() % ELITE_TYPES.size()]
	else:
		var pool := unlocked_types()
		config = pool[randi() % pool.size()]
	var strategies: Array[StringName] = config["strategies"]
	var strategy := strategies[randi() % strategies.size()]
	var btype := _pick_bullet_type(config)
	var view := GameState.view_world_rect()
	var x := randf_range(view.position.x + 60.0, view.end.x - 60.0)
	# 入场预告：0.6s 红色提示后敌机才进场
	get_parent().add_child(SpawnTelegraph.new(x, view.position.y))
	await get_tree().create_timer(GameState.cfg("spawner.telegraph_duration", SpawnTelegraph.DURATION), false).timeout
	var e := ENEMY_SCENE.instantiate() as Enemy
	e.setup(config, strategy, GameState.difficulty_multiplier, btype)
	e.position = Vector2(x, view.position.y - 60.0)
	get_parent().add_child(e)


## Boss-3 召唤的小怪（straight 型 1 型机），立即进场无预告。
## 作为 Main 的子节点，与正常敌机走同一套清场逻辑（返航/结算）。
func spawn_minion(pos: Vector2) -> void:
	GameState.enemy_pool.spawn(ENEMY_TYPES[0], &"straight", GameState.difficulty_multiplier, pos)


## Boss 出场流程：警告横幅 + 震动脉冲，2s 后 Boss 才降入。
func _trigger_boss() -> void:
	_boss_active = true
	boss_warning.emit()
	GameState.shake(GameState.cfg("effects.shake.boss_warning", 14.0))
	await get_tree().create_timer(2.0, false).timeout
	_spawn_boss()


## p_type <= 0 时按击杀数轮换：第 N 只 Boss = 第 (N-1)%3+1 种
func _spawn_boss(p_type: int = 0) -> void:
	_boss_active = true
	if p_type <= 0:
		p_type = GameState.boss_kills % 3 + 1
	var boss := BOSS_SCENE.instantiate() as Boss
	boss.setup(GameState.difficulty_multiplier, p_type)
	boss.position = Vector2(960.0, GameState.view_world_rect().position.y - 160.0)
	boss.died.connect(_on_boss_died)
	get_parent().add_child(boss)
	boss_spawned.emit(boss)


func _on_boss_died() -> void:
	_boss_active = false
	_boss_timer = 0.0
	_next_boss_score += BOSS_SCORE_STEP
