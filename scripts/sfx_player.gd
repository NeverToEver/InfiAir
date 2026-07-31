class_name SfxPlayer
extends Node
## A2 阶段 3：常驻音效播放器池剥离（docs/AUDIT_VAULT.md A2）。
## 作为 GameState 的子节点挂在树（播放节点被 queue_free 时音效也不会中断）。
## 不持有具体音频资源（SFX_* 常量仍在 GameState 传入），只管理播放实例池。
## 行为与原 game_state.gd 逐字节等价。

var _sfx_players: Array[AudioStreamPlayer] = []
var _sfx_index: int = 0


## 构建播放器池（GameState._ready 在 add_child 本节点后调用）
func build_pool(size: int) -> void:
	for i in size:
		var p := AudioStreamPlayer.new()
		add_child(p)
		_sfx_players.append(p)


func play(stream: AudioStream, volume_db: float = 0.0, pitch_scale: float = 1.0) -> void:
	# headless dummy 音频驱动不混音：一次性 WAV 播放实例在退出时既不自然结束、
	# stop() 也不释放，必报 ObjectDB 泄漏噪音；无头路径直接不创建播放实例。
	if DisplayServer.get_name() == "headless":
		return
	var p := _sfx_players[_sfx_index]
	_sfx_index = (_sfx_index + 1) % _sfx_players.size()
	p.stream = stream
	p.volume_db = volume_db
	p.pitch_scale = pitch_scale  # 池化复用：每次播放都显式置位，避免上次变调残留
	p.play()


## 停止池内全部播放器（带播未停时 AudioStreamPlayback 会在退出时泄漏）
func stop_all() -> void:
	for p in _sfx_players:
		p.stop()
