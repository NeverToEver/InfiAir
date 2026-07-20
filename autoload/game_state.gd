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
const SFX_POOL_SIZE := 6

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


func add_boss_kill() -> void:
	boss_kills += 1
	add_score(500)
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
