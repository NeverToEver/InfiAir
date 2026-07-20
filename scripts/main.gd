extends Node2D
## 主场景：串联生成器、HUD 与各 UI 层，处理 Esc 暂停与游戏结束。

@onready var _spawner: Node = $Spawner
@onready var _hud: CanvasLayer = $HUD
@onready var _buff_ui: CanvasLayer = $BuffUI
@onready var _pause_ui: CanvasLayer = $PauseUI

var _game_over: bool = false


func _ready() -> void:
	RenderingServer.set_default_clear_color(Color(0.02, 0.02, 0.06))
	_spawner.boss_spawned.connect(_hud.show_boss_bar)
	GameState.player_died.connect(_on_player_died)


func _on_player_died() -> void:
	_game_over = true


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel") and not _game_over and not _buff_ui.visible:
		_pause_ui.toggle()
