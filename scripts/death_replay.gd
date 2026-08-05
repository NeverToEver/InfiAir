class_name DeathReplay
extends RefCounted
## B 梯队（fair plan §8）：死亡回放——环形缓冲录制最近 RECORD_SECONDS 秒的敌弹位置轨迹，
## 玩家死亡后以幽灵弹幕重放（死因可见，最强公平感信号；只重放不结算，零碰撞）。
## 录制在 main._process（存活期渲染帧采样；死亡后树暂停，main._process 冻结自然停止）；
## 重放演出节点 process_mode=ALWAYS，暂停树中照常播放，播完自毁。
## P0-1（2026-08-05 审计）：录制数据源从 main.get_children() 改为 GameState.enemy_bullets
## 注册表（零 cast 遍历）；帧缓冲固定容量环形缓冲（索引取模写入，删除 pop_front O(n) 整表
## 移位）；内层 [x,y] 改 PackedFloat32Array 交错存储（槽复用 clear 保留容量，录制循环零分配）。

const RECORD_SECONDS := 3.0
const RECORD_FPS := 60.0
const MAX_FRAMES := int(RECORD_SECONDS * RECORD_FPS)

## 重放演出帧间隔（重放时钟与录制 60fps 对齐，独立于引擎帧率）
const FRAME_DURATION := 1.0 / RECORD_FPS
## 幽灵弹池大小（敌弹场上峰值上限；超出部分不显示——回放只求死因可见）
const GHOST_COUNT := 200

## 环形缓冲：固定 MAX_FRAMES 槽，每槽 PackedFloat32Array（[x0,y0,x1,y1,...] 交错）；
## _write_idx = 下一写槽，_frame_count = 已录制帧数（< MAX_FRAMES 时从头读，写满后最老帧在 _write_idx）
var _frames: Array[PackedFloat32Array] = []
var _write_idx: int = 0
var _frame_count: int = 0
var _recording := false


## 开始录制（main 新对局入口调用；幂等——重复调用清缓冲重录）
func begin() -> void:
	_recording = true
	_frame_count = 0
	_write_idx = 0
	if _frames.size() != MAX_FRAMES:
		_frames.resize(MAX_FRAMES)
		for i in MAX_FRAMES:
			_frames[i] = PackedFloat32Array()


## 停止录制（死亡/结算后调用；之后 record 零开销早退）
func stop() -> void:
	_recording = false


## 每渲染帧采样（main._process 存活期调用）：从敌弹注册表录制位置轨迹（环形覆盖最旧帧）。
## 帧槽 clear 复用（PackedFloat32Array 容量保留），录制循环内零分配。
func record() -> void:
	if not _recording:
		return
	var frame := _frames[_write_idx]
	frame.clear()
	for b in GameState.enemy_bullets:
		if b == null or not is_instance_valid(b):
			continue  # 注销延迟/销毁竞态的悬空引用防御
		frame.append(b.global_position.x)
		frame.append(b.global_position.y)
	_write_idx = (_write_idx + 1) % MAX_FRAMES
	if _frame_count < MAX_FRAMES:
		_frame_count += 1


## 生成重放演出节点（main 死亡流程调用；节点由调用方挂树，播完自毁）
func play() -> Node2D:
	_recording = false
	var player := DeathReplayPlayer.new()
	# 环形缓冲顺序化：未写满从头读，写满从最老帧（_write_idx）起环绕读——引用传递零拷贝
	var ordered: Array[PackedFloat32Array] = []
	ordered.resize(_frame_count)
	var start := 0
	if _frame_count == MAX_FRAMES:
		start = _write_idx
	for i in _frame_count:
		ordered[i] = _frames[(start + i) % MAX_FRAMES]
	player.setup(ordered)
	return player


## 已录制帧数（测试观测）
func frame_count() -> int:
	return _frame_count


## 死亡回放演出：暂停树中以 process_mode=ALWAYS 重放录制的敌弹轨迹——
## 幽灵红点按快照逐帧出现/移动（与录制时弹生成/销毁一致），播完自毁。纯演出无碰撞。
class DeathReplayPlayer:
	extends Node2D

	var _frames: Array[PackedFloat32Array] = []
	var _frame_idx: int = 0
	var _accum: float = 0.0
	var _ghosts: Array[Polygon2D] = []

	func setup(frames: Array[PackedFloat32Array]) -> void:
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

	func _apply_frame(frame: PackedFloat32Array) -> void:
		for i in _ghosts.size():
			var j := i * 2
			if j + 1 < frame.size():
				_ghosts[i].global_position = Vector2(frame[j], frame[j + 1])
				_ghosts[i].visible = true
			else:
				_ghosts[i].visible = false
