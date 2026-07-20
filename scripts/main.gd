extends Node2D
## 主场景：串联生成器、HUD 与各 UI 层，处理 Esc 暂停、母舰召唤（H）、
## 返航（B）、开始面板（继续对局/新游戏）与常驻 BGM。

const BGM_PATH := "res://assets/audio/bgm_loop.wav"
const MOTHERSHIP_SCENE: PackedScene = preload("res://scenes/mothership.tscn")
const MOTHERSHIP_GHOST: Texture2D = preload("res://assets/sprites/boss_ship_3.png")
const DOCK_CHARGE_TIME := 3.0
const HOME_CHARGE_TIME := 1.5

@onready var _spawner: Node = $Spawner
@onready var _hud: CanvasLayer = $HUD
@onready var _buff_ui: CanvasLayer = $BuffUI
@onready var _pause_ui: CanvasLayer = $PauseUI
@onready var _start_panel: CanvasLayer = $StartPanel
@onready var _base_ui: CanvasLayer = $BaseUI
@onready var _player: Player = $Player
@onready var _starfield: Starfield = $Starfield

var _game_over: bool = false
var _homecoming: bool = false
var _bgm_player: AudioStreamPlayer
var _dock_cooldown: float = 0.0
var _mothership: Mothership = null
var _charging: bool = false
var _charge_time: float = 0.0
var _charge_ghost: Sprite2D
var _home_charge_time: float = 0.0


func _ready() -> void:
	add_to_group("main")
	RenderingServer.set_default_clear_color(Color(0.02, 0.02, 0.06))
	_spawner.boss_spawned.connect(_hud.show_boss_bar)
	_spawner.boss_warning.connect(_hud.show_boss_banner)
	GameState.player_died.connect(_on_player_died)
	_start_panel.continue_chosen.connect(_on_continue_run)
	_start_panel.new_game_chosen.connect(_apply_new_run)
	_base_ui.resume_requested.connect(_resume_from_base)
	_start_bgm()
	# 蓄力虚影（长按 H 蓄力期间显示）
	_charge_ghost = Sprite2D.new()
	_charge_ghost.texture = MOTHERSHIP_GHOST
	_charge_ghost.scale = Vector2(1.6, 1.6)
	_charge_ghost.rotation = PI
	_charge_ghost.position = Vector2(960.0, 270.0)
	_charge_ghost.modulate = Color(1.0, 1.0, 1.0, 0.15)
	_charge_ghost.visible = false
	add_child(_charge_ghost)
	# 有存档则先显示开始面板，否则直接开新局
	if GameState.has_save():
		_start_panel.show_panel()


func _process(delta: float) -> void:
	if _dock_cooldown > 0.0:
		_dock_cooldown -= delta
	# 长按 H 蓄力召唤母舰（松手取消，不进冷却）
	var can_charge := (
		_mothership == null and _dock_cooldown <= 0.0 and not _game_over and not _homecoming
	)
	if can_charge and Input.is_action_pressed("dock"):
		_charging = true
		_charge_time += delta
		_charge_ghost.visible = true
		_charge_ghost.modulate.a = 0.15 + 0.25 * clampf(_charge_time / DOCK_CHARGE_TIME, 0.0, 1.0)
		if _charge_time >= DOCK_CHARGE_TIME:
			_stop_charging()
			_summon_mothership()
	elif _charging:
		_stop_charging()
	# 长按 B 蓄力返航（松手取消）
	if not _game_over and not _homecoming and Input.is_action_pressed("homecoming"):
		_home_charge_time += delta
		_hud.set_home_charge(_home_charge_time / HOME_CHARGE_TIME)
		if _home_charge_time >= HOME_CHARGE_TIME:
			_home_charge_time = 0.0
			_hud.set_home_charge(-1.0)
			_start_homecoming()
	elif _home_charge_time > 0.0:
		_home_charge_time = 0.0
		_hud.set_home_charge(-1.0)


func _stop_charging() -> void:
	_charging = false
	_charge_time = 0.0
	_charge_ghost.visible = false


func _start_bgm() -> void:
	var stream := ResourceLoader.load(BGM_PATH, "AudioStreamWAV", ResourceLoader.CACHE_MODE_IGNORE) as AudioStreamWAV
	# 只设 loop_mode 即可整段循环；显式写 loop_begin/loop_end 会在退出时泄漏播放实例
	stream.loop_mode = AudioStreamWAV.LOOP_FORWARD
	_bgm_player = AudioStreamPlayer.new()
	_bgm_player.stream = stream
	_bgm_player.volume_db = -18.0
	add_child(_bgm_player)
	_bgm_player.play()


## 新对局（无存档或开始面板选「新游戏」）：数据层已由 reset_run/读档就绪，无需额外处理
func _apply_new_run() -> void:
	pass


func _on_continue_run() -> void:
	var data := GameState.load_run_data()
	if data.is_empty():
		return
	GameState.apply_run_save(data)
	_player._fuel = float(data.get("fuel", _player.fuel_max))
	_spawner._elapsed = float(data.get("elapsed", 0.0))


func _on_player_died() -> void:
	_game_over = true


## 母舰状态文本（HUD 轮询）
func dock_status_text() -> String:
	if _charging:
		return "母舰蓄力 %d%%" % int(_charge_time / DOCK_CHARGE_TIME * 100.0)
	if _mothership != null:
		return _mothership.state_text()
	if _dock_cooldown > 0.0:
		return "母舰冷却 %ds" % ceili(_dock_cooldown)
	return "母舰就绪 [H]"


func _summon_mothership() -> void:
	_mothership = MOTHERSHIP_SCENE.instantiate() as Mothership
	_mothership.position = Vector2(960.0, -200.0)
	_mothership.departed.connect(_on_mothership_departed)
	_mothership.tree_exited.connect(func() -> void: _mothership = null)
	add_child(_mothership)


func _on_mothership_departed(cooldown: float) -> void:
	_dock_cooldown = cooldown


## 返航（局内中场整备）：锁输入、星光拉伸 + 白屏闪，进入基地控制台。
## 对局继续：不删档（反而更新存档）、Boss 保留、死亡才是唯一终局。
func _start_homecoming() -> void:
	_homecoming = true
	_home_charge_time = 0.0
	_hud.set_home_charge(-1.0)
	_player._input_locked = true
	_player.velocity = Vector2.ZERO
	_spawner.set_process(false)
	# 母舰若在对接/驻留中，直接收回（玩家由返航统一锁定，恢复时统一解锁）
	if _mothership != null:
		_mothership.queue_free()
	# 返航后存档保留更新，供「继续对局」使用
	GameState.save_run(_player._fuel, _spawner._elapsed)
	_starfield.warp(18.0)
	var flash := await _flash_white(0.5, 0.5)
	flash.queue_free()
	_base_ui.show_base()
	get_tree().paused = true


## 继续出击：轨道打击清屏（Boss 保留，清小怪与全部弹丸）→ 短白屏 → 恢复同一局
func _resume_from_base() -> void:
	for child in get_children():
		if child is Enemy or child is Bullet:
			child.queue_free()
	_player._input_locked = false
	# 驻留期无敌可能是 999，恢复时统一重置为短无敌
	_player._invincible = 1.5
	_spawner.set_process(true)
	_homecoming = false
	get_tree().paused = false
	var flash := await _flash_white(0.15, 0.25)
	flash.queue_free()


func _flash_white(fade_in: float, hold: float) -> CanvasLayer:
	var flash_layer := CanvasLayer.new()
	flash_layer.layer = 40
	add_child(flash_layer)
	var flash := ColorRect.new()
	flash.color = Color(1.0, 1.0, 1.0, 0.0)
	flash.set_anchors_preset(Control.PRESET_FULL_RECT)
	flash_layer.add_child(flash)
	var tween := create_tween()
	tween.tween_property(flash, "color:a", 1.0, fade_in)
	tween.tween_interval(hold)
	tween.tween_property(flash, "color:a", 0.0, 0.3)
	await tween.finished
	return flash_layer


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel") and not _game_over and not _homecoming and not _buff_ui.visible:
		_pause_ui.toggle()
