extends Node
## 全局状态与信号总线：分数、击杀、生命、难度乘数、已选 buff。

signal score_changed(new_score: int)
signal lives_changed(new_lives: float)
signal difficulty_changed(new_multiplier: float)
signal milestone_reached(score: int)
signal player_died
signal screen_shake(strength: float)

const MILESTONE_STEP := 500

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

const TALENT_DEFS: Array[Dictionary] = [
	{"id": &"hull", "name": "强化机身", "desc": "开局生命 +1", "cost": 2, "max": 3},
	{"id": &"calibration", "name": "弹道校准", "desc": "开局射速 +8%", "cost": 2, "max": 3},
	{"id": &"tank", "name": "扩容油箱", "desc": "燃料上限 +25", "cost": 1, "max": 3},
	{"id": &"plan", "name": "出击预案", "desc": "开局随机送 1 个 Lv.1 buff", "cost": 5, "max": 1},
]

var high_score: int = 0
var talent_points: int = 0
## 天赋 id -> 等级（持久化到 profile.json）
var talents: Dictionary = {}

var _sfx_players: Array[AudioStreamPlayer] = []
var _sfx_index: int = 0

var score: int = 0
var kills: int = 0
var boss_kills: int = 0
var lives: float = 3.0
var difficulty_multiplier: float = 1.0
## buff id -> 已选层数
var buffs: Dictionary = {}

var _next_milestone: int = MILESTONE_STEP


func _ready() -> void:
	# 常驻音效播放器池：播放节点被 queue_free 时音效也不会中断
	for i in SFX_POOL_SIZE:
		var p := AudioStreamPlayer.new()
		add_child(p)
		_sfx_players.append(p)
	load_profile()


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
	_next_milestone = MILESTONE_STEP


func add_score(points: int) -> void:
	score += points
	score_changed.emit(score)
	if score >= _next_milestone:
		_next_milestone += MILESTONE_STEP
		milestone_reached.emit(score)


func add_kill() -> void:
	kills += 1


func add_boss_kill(score_scale: float = 1.0) -> void:
	boss_kills += 1
	add_score(int(500.0 * score_scale))
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
	_next_milestone = (score / MILESTONE_STEP) * MILESTONE_STEP + MILESTONE_STEP
	score_changed.emit(score)
	lives_changed.emit(lives)
	difficulty_changed.emit(difficulty_multiplier)


func delete_save() -> void:
	if has_save():
		DirAccess.remove_absolute(SAVE_PATH)


# ---------------- 局外档案（user://profile.json） ----------------

func load_profile() -> void:
	if not FileAccess.file_exists(PROFILE_PATH):
		return
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(PROFILE_PATH))
	if parsed is Dictionary:
		high_score = int(parsed.get("high_score", 0))
		talent_points = int(parsed.get("talent_points", 0))
		talents.clear()
		var saved_talents: Dictionary = parsed.get("talents", {})
		for key in saved_talents.keys():
			talents[StringName(key)] = int(saved_talents[key])


func save_profile() -> void:
	var data := {
		"version": PERSIST_VERSION,
		"high_score": high_score,
		"talent_points": talent_points,
		"talents": talents.duplicate(),
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


func talent_level(id: StringName) -> int:
	return int(talents.get(id, 0))


func buy_talent(id: StringName) -> bool:
	for def in TALENT_DEFS:
		if def["id"] == id:
			if talent_level(id) >= def["max"] or talent_points < def["cost"]:
				return false
			talent_points -= def["cost"]
			talents[id] = talent_level(id) + 1
			save_profile()
			return true
	return false


## 返航天赋点折算：buff 总层数 / 2（向下取整）+ Boss 击杀 × 2
func calc_homecoming_points() -> int:
	var stacks := 0
	for v in buffs.values():
		stacks += int(v)
	return stacks / 2 + boss_kills * 2
