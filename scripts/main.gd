extends Node2D
## 主场景：串联生成器、HUD 与各 UI 层，处理 Esc 暂停与游戏结束，常驻 BGM。

const BGM_PATH := "res://assets/audio/bgm_loop.wav"

@onready var _spawner: Node = $Spawner
@onready var _hud: CanvasLayer = $HUD
@onready var _buff_ui: CanvasLayer = $BuffUI
@onready var _pause_ui: CanvasLayer = $PauseUI

var _game_over: bool = false
var _bgm_player: AudioStreamPlayer


func _ready() -> void:
	RenderingServer.set_default_clear_color(Color(0.02, 0.02, 0.06))
	_spawner.boss_spawned.connect(_hud.show_boss_bar)
	_spawner.boss_warning.connect(_hud.show_boss_banner)
	GameState.player_died.connect(_on_player_died)
	_start_bgm()


func _start_bgm() -> void:
	var stream := ResourceLoader.load(BGM_PATH, "AudioStreamWAV", ResourceLoader.CACHE_MODE_IGNORE) as AudioStreamWAV
	# 只设 loop_mode 即可整段循环；显式写 loop_begin/loop_end 会在退出时泄漏播放实例
	stream.loop_mode = AudioStreamWAV.LOOP_FORWARD
	_bgm_player = AudioStreamPlayer.new()
	_bgm_player.stream = stream
	_bgm_player.volume_db = -18.0
	add_child(_bgm_player)
	_bgm_player.play()


func _on_player_died() -> void:
	_game_over = true


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel") and not _game_over and not _buff_ui.visible:
		_pause_ui.toggle()
