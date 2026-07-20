extends Node
## 全局状态与信号总线：分数、击杀、生命、难度乘数、已选 buff。

signal score_changed(new_score: int)
signal lives_changed(new_lives: float)
signal difficulty_changed(new_multiplier: float)
signal difficulty_selected(difficulty: StringName)
signal milestone_reached(score: int)
signal player_died
signal screen_shake(strength: float)
signal rp_changed(new_rp: int)
signal mission_completed(id: StringName)
signal route_chosen(line: StringName, buff_id: StringName)

## 难度档位表（开始面板选择，profile 持久化；对齐原作 settings.py DIFFICULTY_SETTINGS）
## hp/speed/spawn 为敌机数值与刷怪间隔倍率；score 为分数倍率（add_score 统一乘算）；
## spread_cap 为 spread 弹种敌机同屏上限；milestone 为里程碑阈值倍率
## （原作阈值与分数同倍 ×1/×2/×3，此处按设计取 ×1/×1/×1.5，避免高难 Buff 节奏过稀）。
const DIFFICULTY_DEFS: Dictionary = {
	&"easy": {
		"label": "易", "hp": 0.75, "speed": 0.85, "spawn": 1.25,
		"score": 1, "spread_cap": 1, "milestone": 1.0,
	},
	&"medium": {
		"label": "中", "hp": 1.0, "speed": 1.0, "spawn": 1.0,
		"score": 2, "spread_cap": 2, "milestone": 1.0,
	},
	&"hard": {
		"label": "难", "hp": 1.5, "speed": 1.2, "spawn": 0.8,
		"score": 3, "spread_cap": 3, "milestone": 1.5,
	},
}
const DIFFICULTY_ORDER: Array[StringName] = [&"easy", &"medium", &"hard"]

# 里程碑阈值曲线（对齐原作 constants.py GameBalanceConstants 算法）：
# 首循环 8 档基础阈值，之后每循环的档差按 ×1.35^cycle 放大（阈值单调不回退）。
const MILESTONE_BASE: Array[int] = [3000, 8000, 15000, 25000, 40000, 55000, 70000, 80000]
const MILESTONE_CYCLE_MULT := 1.35

# RP（征用点数）经济：对齐原作 RequisitionConstants
const RP_BOSS_KILL := 5
const RP_MISSION_REWARD := 3
const RP_REPAIR_COST := 2
const RP_RECHARGE_COST := 2

# 常驻基地任务（对齐原作 base_talent_console 三任务）
const MISSION_DEFS: Array[Dictionary] = [
	{"id": &"kill_5", "name": "战场清扫", "desc": "击杀 5 个敌人", "goal": 5},
	{"id": &"survive_180", "name": "战场生存", "desc": "存活 180 秒", "goal": 180},
	{"id": &"boss_1", "name": "主宰之战", "desc": "击杀 1 个 Boss", "goal": 1},
]

# 互斥天赋路线：line -> 两个候选 buff（对齐原作 talent_balance_manager）
const ROUTE_LINES: Dictionary = {
	&"offense": [&"spread_shot", &"laser_beam"],
	&"mobility": [&"phase_dash", &"mothership_recall"],
}

const SFX_EXPLOSION: AudioStream = preload("res://assets/audio/explosion.wav")
const SFX_EXPLOSION_BIG: AudioStream = preload("res://assets/audio/explosion_big.wav")
const SFX_PLAYER_HIT: AudioStream = preload("res://assets/audio/player_hit.wav")
const SFX_BUFF_PICK: AudioStream = preload("res://assets/audio/buff_pick.wav")
const SFX_DASH: AudioStream = preload("res://assets/audio/dash.wav")
const SFX_RESUPPLY: AudioStream = preload("res://assets/audio/resupply.wav")
const SFX_POOL_SIZE := 6

const SAVE_PATH := "user://savegame.json"
const PROFILE_PATH := "user://profile.json"
const PERSIST_VERSION := 1

var high_score: int = 0

var _sfx_players: Array[AudioStreamPlayer] = []
var _sfx_index: int = 0

var score: int = 0
var kills: int = 0
var boss_kills: int = 0
var lives: float = 3.0
var difficulty_multiplier: float = 1.0
## 难度档位（profile 持久化，默认 medium）
var difficulty: StringName = &"medium"
## 设置项：Ctrl 微调 / Shift 加速的模式（false=按住，true=切换；player.gd 侧接入由集成阶段完成）
var ctrl_toggle_mode: bool = false
var shift_toggle_mode: bool = false
## buff id -> 已选层数
var buffs: Dictionary = {}
## 征用点数（基地经济）
var rp: int = 0
## 对局存活秒数（survive_180 任务进度来源）
var run_time: float = 0.0
## 任务 id -> {"progress": int, "claimed": bool}
var missions: Dictionary = {}
## 天赋路线 line -> 所选 buff id
var chosen_routes: Dictionary = {}
## 天赋路线 line -> 被锁定的未选 buff id（不进奖励池）
var locked_routes: Dictionary = {}

var _next_milestone: int = MILESTONE_BASE[0]
var _milestone_count: int = 0

## 实体注册表（热路径缓存，避免每帧 get_nodes_in_group 分配）：
## enemy/boss 在 _ready/_exit_tree 时注册/注销，player 单独缓存引用。
var enemies: Array[Node] = []
var player_ref: Node2D = null
## 子弹对象池实例（由 bullet_pool.gd 在 _ready 时登记）
var bullet_pool: BulletPool = null


func register_enemy(node: Node) -> void:
	enemies.append(node)


func unregister_enemy(node: Node) -> void:
	enemies.erase(node)


func _ready() -> void:
	# 常驻音效播放器池：播放节点被 queue_free 时音效也不会中断
	for i in SFX_POOL_SIZE:
		var p := AudioStreamPlayer.new()
		add_child(p)
		_sfx_players.append(p)
	_init_missions()
	load_profile()
	_next_milestone = milestone_threshold(0)


# 暂停（Buff/结算 UI）时不计存活时间
func _process(delta: float) -> void:
	run_time += delta
	_set_mission_progress(&"survive_180", int(run_time))


func play_sfx(stream: AudioStream, volume_db: float = 0.0) -> void:
	var p := _sfx_players[_sfx_index]
	_sfx_index = (_sfx_index + 1) % _sfx_players.size()
	p.stream = stream
	p.volume_db = volume_db
	p.play()


func shake(strength: float) -> void:
	screen_shake.emit(strength)


func reset_run() -> void:
	score = 0
	kills = 0
	boss_kills = 0
	lives = 3.0
	difficulty_multiplier = 1.0
	buffs.clear()
	rp = 0
	run_time = 0.0
	_init_missions()
	chosen_routes.clear()
	locked_routes.clear()
	_milestone_count = 0
	_next_milestone = milestone_threshold(0)


func add_score(points: int) -> void:
	# 难度分数倍率统一在此乘算（easy ×1 / medium ×2 / hard ×3，配置表里的分值不变）
	score += points * score_multiplier()
	score_changed.emit(score)
	if score >= _next_milestone:
		_milestone_count += 1
		_next_milestone = milestone_threshold(_milestone_count)
		milestone_reached.emit(score)


# ---------------- 难度档位 ----------------

## 切换难度档位（非法档位忽略），持久化到 profile 并广播
func set_difficulty(p_difficulty: StringName) -> void:
	if not DIFFICULTY_DEFS.has(p_difficulty) or p_difficulty == difficulty:
		return
	difficulty = p_difficulty
	difficulty_selected.emit(difficulty)
	save_profile()


func difficulty_label() -> String:
	return DIFFICULTY_DEFS[difficulty]["label"]


func score_multiplier() -> int:
	return int(DIFFICULTY_DEFS[difficulty]["score"])


func enemy_hp_multiplier() -> float:
	return float(DIFFICULTY_DEFS[difficulty]["hp"])


func enemy_speed_multiplier() -> float:
	return float(DIFFICULTY_DEFS[difficulty]["speed"])


func spawn_interval_multiplier() -> float:
	return float(DIFFICULTY_DEFS[difficulty]["spawn"])


## spread 弹种敌机同屏上限（easy 1 / medium 2 / hard 3）
func spread_enemy_cap() -> int:
	return int(DIFFICULTY_DEFS[difficulty]["spread_cap"])


# ---------------- 里程碑阈值曲线 ----------------

## 第 index 次（0 起）里程碑的分数阈值：8 档基础阈值循环，档差按 ×1.35^cycle 增长，
## 再乘难度阈值倍率（easy ×1 / medium ×1 / hard ×1.5）。
func milestone_threshold(index: int) -> int:
	var n := MILESTONE_BASE.size()
	var cycle := maxi(index, 0) / n
	var step := maxi(index, 0) % n
	var total := 0.0
	for c in cycle + 1:
		var mult := pow(MILESTONE_CYCLE_MULT, c)
		var last_step := step if c == cycle else n - 1
		var prev := 0.0
		for i in last_step + 1:
			total += (MILESTONE_BASE[i] - prev) * mult
			prev = MILESTONE_BASE[i]
	return int(roundf(total * float(DIFFICULTY_DEFS[difficulty]["milestone"])))


## 测试钩子：直接设定下一个里程碑阈值（不动曲线计数，保证测试确定性）
func _set_milestone_override(threshold: int) -> void:
	_next_milestone = threshold


# ---------------- 设置项（Ctrl/Shift 模式） ----------------

## Ctrl 微调模式：false=按住生效，true=按一下切换；持久化到 profile
func set_ctrl_toggle_mode(enabled: bool) -> void:
	ctrl_toggle_mode = enabled
	save_profile()


## Shift 加速模式：false=按住生效，true=按一下切换；持久化到 profile
func set_shift_toggle_mode(enabled: bool) -> void:
	shift_toggle_mode = enabled
	save_profile()


func add_kill() -> void:
	kills += 1
	_set_mission_progress(&"kill_5", kills)


func add_boss_kill(score_scale: float = 1.0) -> void:
	boss_kills += 1
	add_score(int(500.0 * score_scale))
	add_rp(RP_BOSS_KILL)
	_set_mission_progress(&"boss_1", boss_kills)
	# 公式：base 1 + (2^min(kills,10) - 1) * 0.25，封顶 8x
	difficulty_multiplier = minf(1.0 + (pow(2.0, mini(boss_kills, 10)) - 1.0) * 0.25, 8.0)
	difficulty_changed.emit(difficulty_multiplier)


func lose_life(amount: float = 1.0) -> void:
	lives = maxf(lives - amount, 0.0)
	lives_changed.emit(lives)
	if lives <= 0.0:
		player_died.emit()


func heal(amount: float) -> void:
	lives += amount
	lives_changed.emit(lives)


func buff_count(id: StringName) -> int:
	return buffs.get(id, 0)


func add_buff(id: StringName) -> void:
	buffs[id] = buff_count(id) + 1


# ---------------- RP 经济 / 基地任务 / 天赋路线 ----------------

func add_rp(amount: int) -> void:
	rp += amount
	rp_changed.emit(rp)


## 余额不足返回 false 且不扣减
func spend_rp(amount: int) -> bool:
	if rp < amount:
		return false
	rp -= amount
	rp_changed.emit(rp)
	return true


func _init_missions() -> void:
	missions.clear()
	for def in MISSION_DEFS:
		missions[def["id"]] = {"progress": 0, "claimed": false}


func _set_mission_progress(id: StringName, value: int) -> void:
	if not missions.has(id):
		return
	var m: Dictionary = missions[id]
	var goal := mission_goal(id)
	var was_done: bool = int(m["progress"]) >= goal
	m["progress"] = value
	if not was_done and value >= goal:
		mission_completed.emit(id)


func mission_goal(id: StringName) -> int:
	for def in MISSION_DEFS:
		if def["id"] == id:
			return int(def["goal"])
	return 0


func mission_progress(id: StringName) -> int:
	return int(missions.get(id, {}).get("progress", 0))


func is_mission_done(id: StringName) -> bool:
	return missions.has(id) and mission_progress(id) >= mission_goal(id)


func is_mission_claimed(id: StringName) -> bool:
	return bool(missions.get(id, {}).get("claimed", false))


## 领取已完成任务的 +3RP，每任务每局限领一次
func claim_mission(id: StringName) -> bool:
	if not is_mission_done(id) or is_mission_claimed(id):
		return false
	missions[id]["claimed"] = true
	add_rp(RP_MISSION_REWARD)
	return true


## 选择天赋路线：该线两个 buff 的层数合并到所选 buff，另一个锁定不进奖励池。
## line/buff 非法或该线没有任何层数时返回 false。
func choose_route(line: StringName, buff_id: StringName) -> bool:
	if not ROUTE_LINES.has(line):
		return false
	var options: Array = ROUTE_LINES[line]
	if buff_id not in options:
		return false
	var other: StringName = options[0] if options[1] == buff_id else options[1]
	var total := buff_count(buff_id) + buff_count(other)
	if total <= 0:
		return false
	buffs[buff_id] = total
	buffs.erase(other)
	chosen_routes[line] = buff_id
	locked_routes[line] = other
	route_chosen.emit(line, buff_id)
	return true


## 奖励池抽取时排除锁定 buff
func is_buff_locked(buff_id: StringName) -> bool:
	return buff_id in locked_routes.values()


# ---------------- 对局存档（user://savegame.json） ----------------

func save_run(fuel: float, elapsed: float) -> void:
	var data := {
		"version": PERSIST_VERSION,
		"score": score,
		"kills": kills,
		"lives": lives,
		"fuel": fuel,
		"boss_kills": boss_kills,
		"difficulty_multiplier": difficulty_multiplier,
		"buffs": buffs.duplicate(),
		"elapsed": elapsed,
		"rp": rp,
		"missions": missions.duplicate(true),
		"chosen_routes": chosen_routes.duplicate(),
		"locked_routes": locked_routes.duplicate(),
		"ctrl_toggle_mode": ctrl_toggle_mode,
		"shift_toggle_mode": shift_toggle_mode,
	}
	var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	f.store_string(JSON.stringify(data))
	f.close()


func has_save() -> bool:
	return FileAccess.file_exists(SAVE_PATH)


func load_run_data() -> Dictionary:
	if not has_save():
		return {}
	var f := FileAccess.open(SAVE_PATH, FileAccess.READ)
	var parsed: Variant = JSON.parse_string(f.get_as_text())
	f.close()
	return parsed if parsed is Dictionary else {}


func apply_run_save(data: Dictionary) -> void:
	score = int(data.get("score", 0))
	kills = int(data.get("kills", 0))
	lives = float(data.get("lives", 3.0))
	boss_kills = int(data.get("boss_kills", 0))
	difficulty_multiplier = float(data.get("difficulty_multiplier", 1.0))
	buffs.clear()
	var saved_buffs: Dictionary = data.get("buffs", {})
	for key in saved_buffs.keys():
		buffs[StringName(key)] = int(saved_buffs[key])
	run_time = float(data.get("elapsed", 0.0))
	rp = int(data.get("rp", 0))
	_init_missions()
	var saved_missions: Dictionary = data.get("missions", {})
	for key in saved_missions.keys():
		if missions.has(StringName(key)):
			var m: Dictionary = saved_missions[key]
			missions[StringName(key)] = {
				"progress": int(m.get("progress", 0)),
				"claimed": bool(m.get("claimed", false)),
			}
	chosen_routes.clear()
	var saved_chosen: Dictionary = data.get("chosen_routes", {})
	for key in saved_chosen.keys():
		chosen_routes[StringName(key)] = StringName(saved_chosen[key])
	locked_routes.clear()
	var saved_locked: Dictionary = data.get("locked_routes", {})
	for key in saved_locked.keys():
		locked_routes[StringName(key)] = StringName(saved_locked[key])
	# 设置项随存档往返（旧存档无字段时保留当前值）
	ctrl_toggle_mode = bool(data.get("ctrl_toggle_mode", ctrl_toggle_mode))
	shift_toggle_mode = bool(data.get("shift_toggle_mode", shift_toggle_mode))
	# 里程碑曲线：恢复到大于当前分数的第一档
	_milestone_count = 0
	while milestone_threshold(_milestone_count) <= score:
		_milestone_count += 1
	_next_milestone = milestone_threshold(_milestone_count)
	score_changed.emit(score)
	lives_changed.emit(lives)
	difficulty_changed.emit(difficulty_multiplier)
	rp_changed.emit(rp)


func delete_save() -> void:
	if has_save():
		DirAccess.remove_absolute(SAVE_PATH)


# ---------------- 局外档案（user://profile.json） ----------------

## 局外档案：最高分 + 难度档位 + 设置项（旧版 talents/talent_points 字段读取时忽略；
## 旧档案缺少新字段时保留当前内存值，保证兼容）
func load_profile() -> void:
	if not FileAccess.file_exists(PROFILE_PATH):
		return
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(PROFILE_PATH))
	if parsed is Dictionary:
		high_score = int(parsed.get("high_score", 0))
		var saved_difficulty := StringName(parsed.get("difficulty", ""))
		if DIFFICULTY_DEFS.has(saved_difficulty):
			difficulty = saved_difficulty
		ctrl_toggle_mode = bool(parsed.get("ctrl_toggle_mode", ctrl_toggle_mode))
		shift_toggle_mode = bool(parsed.get("shift_toggle_mode", shift_toggle_mode))


func save_profile() -> void:
	var data := {
		"version": PERSIST_VERSION,
		"high_score": high_score,
		"difficulty": String(difficulty),
		"ctrl_toggle_mode": ctrl_toggle_mode,
		"shift_toggle_mode": shift_toggle_mode,
	}
	var f := FileAccess.open(PROFILE_PATH, FileAccess.WRITE)
	f.store_string(JSON.stringify(data))
	f.close()


## 记录最高分，破纪录返回 true
func record_score() -> bool:
	if score > high_score:
		high_score = score
		save_profile()
		return true
	return false
