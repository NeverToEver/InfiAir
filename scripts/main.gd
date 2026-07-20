extends Node2D
## 主场景：串联生成器、HUD 与各 UI 层，处理 Esc 暂停、母舰召唤（H）、
## 返航（B）、开始面板（继续对局/新游戏）与常驻 BGM。

const BGM_PATH := "res://assets/audio/bgm_loop.wav"
const MOTHERSHIP_SCENE: PackedScene = preload("res://scenes/mothership.tscn")
const DOCK_COOLDOWN := 90.0

@onready var _spawner: Node = $Spawner
@onready var _hud: CanvasLayer = $HUD
@onready var _buff_ui: CanvasLayer = $BuffUI
@onready var _pause_ui: CanvasLayer = $PauseUI
@onready var _start_panel: CanvasLayer = $StartPanel
@onready var _talent_ui: CanvasLayer = $TalentUI
@onready var _player: Player = $Player
@onready var _starfield: Starfield = $Starfield

var _game_over: bool = false
var _homecoming: bool = false
var _bgm_player: AudioStreamPlayer
var _dock_cooldown: float = 0.0
var _mothership: Mothership = null


func _ready() -> void:
	add_to_group("main")
	RenderingServer.set_default_clear_color(Color(0.02, 0.02, 0.06))
	_spawner.boss_spawned.connect(_hud.show_boss_bar)
	_spawner.boss_warning.connect(_hud.show_boss_banner)
	GameState.player_died.connect(_on_player_died)
	_start_panel.continue_chosen.connect(_on_continue_run)
	_start_panel.new_game_chosen.connect(_on_new_run)
	_start_bgm()
	# 有存档则先显示开始面板，否则直接开新局
	if GameState.has_save():
		_start_panel.show_panel()
	else:
		_apply_talents()


func _process(delta: float) -> void:
	if _dock_cooldown > 0.0:
		_dock_cooldown -= delta


func _start_bgm() -> void:
	var stream := ResourceLoader.load(BGM_PATH, "AudioStreamWAV", ResourceLoader.CACHE_MODE_IGNORE) as AudioStreamWAV
	# 只设 loop_mode 即可整段循环；显式写 loop_begin/loop_end 会在退出时泄漏播放实例
	stream.loop_mode = AudioStreamWAV.LOOP_FORWARD
	_bgm_player = AudioStreamPlayer.new()
	_bgm_player.stream = stream
	_bgm_player.volume_db = -18.0
	add_child(_bgm_player)
	_bgm_player.play()


## 新对局：应用已购天赋
func _apply_talents() -> void:
	var hull := GameState.talent_level(&"hull")
	if hull > 0:
		GameState.lives = 3.0 + hull
		GameState.lives_changed.emit(GameState.lives)
	var tank := GameState.talent_level(&"tank")
	if tank > 0:
		_player.fuel_max = 100.0 + 25.0 * tank
		_player.refill_fuel()
	if GameState.talent_level(&"plan") > 0:
		var available: Array = _buff_ui.BUFF_POOL.filter(
			func(b: Dictionary) -> bool: return GameState.buff_count(b["id"]) < b["max"]
		)
		if not available.is_empty():
			GameState.add_buff(available[randi() % available.size()]["id"])


func _on_continue_run() -> void:
	var data := GameState.load_run_data()
	if data.is_empty():
		_apply_talents()
		return
	GameState.apply_run_save(data)
	_player._fuel = float(data.get("fuel", _player.fuel_max))
	_spawner._elapsed = float(data.get("elapsed", 0.0))


func _on_new_run() -> void:
	_apply_talents()


func _on_player_died() -> void:
	_game_over = true


## 母舰状态文本（HUD 轮询）
func dock_status_text() -> String:
	if _mothership != null:
		return "母舰对接中"
	if _dock_cooldown > 0.0:
		return "母舰冷却 %ds" % ceili(_dock_cooldown)
	return "母舰就绪 [H]"


func _summon_mothership() -> void:
	_mothership = MOTHERSHIP_SCENE.instantiate() as Mothership
	_mothership.position = Vector2(960.0, -200.0)
	_mothership.resupplied.connect(_on_mothership_resupplied)
	_mothership.tree_exited.connect(func() -> void: _mothership = null)
	add_child(_mothership)


func _on_mothership_resupplied() -> void:
	_dock_cooldown = DOCK_COOLDOWN


## 返航：锁输入、清场、星光拉伸 + 白屏闪，然后进入基地整备
func _start_homecoming() -> void:
	_homecoming = true
	_player._input_locked = true
	_player.velocity = Vector2.ZERO
	_spawner.set_process(false)
	for child in get_children():
		if child is Enemy or child is Boss or child is Bullet or child is Mothership:
			child.queue_free()
	_starfield.warp(18.0)
	var flash_layer := CanvasLayer.new()
	flash_layer.layer = 40
	add_child(flash_layer)
	var flash := ColorRect.new()
	flash.color = Color(1.0, 1.0, 1.0, 0.0)
	flash.set_anchors_preset(Control.PRESET_FULL_RECT)
	flash_layer.add_child(flash)
	var tween := create_tween()
	tween.tween_property(flash, "color:a", 1.0, 0.5)
	tween.tween_interval(0.5)
	await tween.finished
	var earned := GameState.calc_homecoming_points()
	GameState.talent_points += earned
	var new_record := GameState.record_score()
	GameState.save_profile()
	# 主动善终：删除对局存档
	GameState.delete_save()
	flash_layer.queue_free()
	_talent_ui.show_summary(earned, new_record)
	get_tree().paused = true


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel") and not _game_over and not _homecoming and not _buff_ui.visible:
		_pause_ui.toggle()
	if get_tree().paused or _game_over or _homecoming:
		return
	if event.is_action_pressed("dock") and _dock_cooldown <= 0.0 and _mothership == null:
		_summon_mothership()
	if event.is_action_pressed("homecoming"):
		_start_homecoming()
