class_name DeathReplay
extends RefCounted
## B 梯队（fair plan §8）：死亡回放——环形缓冲录制最近 RECORD_SECONDS 秒的敌弹位置轨迹，
## 玩家死亡后以幽灵弹幕重放（死因可见，最强公平感信号；只重放不结算，零碰撞）。
## 录制在 main._process（存活期渲染帧采样；死亡后树暂停，main._process 冻结自然停止）；
## 重放演出节点 process_mode=ALWAYS，暂停树中照常播放，播完自毁。

const RECORD_SECONDS := 3.0
const RECORD_FPS := 60.0
const MAX_FRAMES := int(RECORD_SECONDS * RECORD_FPS)

## 重放演出帧间隔（重放时钟与录制 60fps 对齐，独立于引擎帧率）
const FRAME_DURATION := 1.0 / RECORD_FPS
## 幽灵弹池大小（敌弹场上峰值上限；超出部分不显示——回放只求死因可见）
const GHOST_COUNT := 200

var _frames: Array[Array] = []  # 每元素 = 该帧敌弹状态数组 [[x, y], ...]
var _recording := false


## 开始录制（main 新对局入口调用；幂等——重复调用清缓冲重录）
func begin() -> void:
	_recording = true
	_frames.clear()


## 停止录制（死亡/结算后调用；之后 record 零开销早退）
func stop() -> void:
	_recording = false


## 每渲染帧采样（main._process 存活期调用）：记录场上敌弹位置轨迹（环形覆盖最旧帧）
func record(main_children: Array[Node]) -> void:
	if not _recording:
		return
	var frame: Array = []
	for child in main_children:
		var b := child as Bullet
		if b == null or b.is_player_bullet:
			continue
		frame.append([b.global_position.x, b.global_position.y])
	if _frames.size() >= MAX_FRAMES:
		_frames.pop_front()
	_frames.append(frame)


## 生成重放演出节点（main 死亡流程调用；节点由调用方挂树，播完自毁）
func play() -> Node2D:
	_recording = false
	var player := DeathReplayPlayer.new()
	player.setup(_frames)
	return player


## 已录制帧数（测试观测）
func frame_count() -> int:
	return _frames.size()


## 死亡回放演出：暂停树中以 process_mode=ALWAYS 重放录制的敌弹轨迹——
## 幽灵红点按快照逐帧出现/移动（与录制时弹生成/销毁一致），播完自毁。纯演出无碰撞。
class DeathReplayPlayer:
	extends Node2D

	var _frames: Array[Array] = []
	var _frame_idx: int = 0
	var _accum: float = 0.0
	var _ghosts: Array[Polygon2D] = []

	func setup(frames: Array[Array]) -> void:
		_frames = frames
		process_mode = Node.PROCESS_MODE_ALWAYS
		z_index = 30  # 场景实体之上、HUD 之下（结算面板为 CanvasLayer 不受 z 影响）
		for i in GHOST_COUNT:
			var g := Polygon2D.new()
			var pts := PackedVector2Array()
			for k in 10:
				var a := TAU * float(k) / 10.0
				pts.append(Vector2(cos(a), sin(a)) * 6.0)
			g.polygon = pts
			g.color = Color(1.0, 0.25, 0.25, 0.7)
			g.visible = false
			add_child(g)
			_ghosts.append(g)

	func _process(delta: float) -> void:
		_accum += delta
		while _accum >= FRAME_DURATION and _frame_idx < _frames.size():
			_accum -= FRAME_DURATION
			_apply_frame(_frames[_frame_idx])
			_frame_idx += 1
		if _frame_idx >= _frames.size():
			queue_free()  # 播完自毁

	func _apply_frame(frame: Array) -> void:
		for i in _ghosts.size():
			if i < frame.size():
				var s: Array = frame[i]
				_ghosts[i].global_position = Vector2(s[0], s[1])
				_ghosts[i].visible = true
			else:
				_ghosts[i].visible = false
