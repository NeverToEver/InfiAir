class_name AimFrameLayer
extends Node2D
## 辅助瞄准框覆盖层（P1-1）：世界坐标单节点，main.gd _ready 运行时创建挂 Main 下。
## 每帧一次 _draw 遍历 GameState.enemies 中带 aim_marked 的 Enemy 统一画四角 bracket 框
## （单节点零逐敌节点开销）；框半径 = 碰撞半径 + frame_pad（指示器族，frame_pad 不乘
## world_scale）。青色强对比 + 低频频闪；准星入框的个体框转金色高亮（即时反馈
## 「追踪已生效」）。Boss/炮塔/编队战机非 Enemy 类，cast 天然排除；精英纳入。

const ARM_RATIO := 0.45  # bracket 单臂长占框半宽比例
const WIDTH := 2.0
const COLOR := Color(0.35, 0.95, 1.0)
const COLOR_HOVER := Color(1.0, 0.85, 0.35)

## 当前档位辅助框内边距（balance.json player.aim_assist.levels，信号联动刷新）
var _frame_pad := 16.0
## P1-3：准星磁吸档位参数（同 levels 前缀、同信号刷新）
var _magnet_range := 100.0
var _magnet_strength := 6.0
var _magnet_max_speed := 8.0
## P1-3：磁吸输入阈值与距离衰减全局参数（player.aim_assist.input / falloff，_ready 一次缓存）
var _magnet_input_min := 2.0
var _magnet_input_full := 40.0
var _falloff_peak := 400.0
var _falloff_end := 1400.0
var _falloff_min := 0.3
var _hover: Enemy = null  # 本帧准星入框的标记敌（高亮显示用）
## P1-3：marked_target_at 渲染帧缓存（player.aim_point 与 aim_frame._process 同帧同点各调一次，
## 命中缓存免重复 O(enemies) 扫描；point 变化即失效）
var _target_cache_frame: int = -1
var _target_cache_point: Vector2 = Vector2.INF
var _target_cache_result: Enemy = null


func _ready() -> void:
	z_index = 9  # 世界实体之上、准星（10）之下
	GameState.aim_frame_layer = self
	_load_level_params()
	_magnet_input_min = float(GameState.cfg("player.aim_assist.input.magnet_input_min", _magnet_input_min))
	_magnet_input_full = float(GameState.cfg("player.aim_assist.input.magnet_input_full", _magnet_input_full))
	_falloff_peak = float(GameState.cfg("player.aim_assist.falloff.peak", _falloff_peak))
	_falloff_end = float(GameState.cfg("player.aim_assist.falloff.end", _falloff_end))
	_falloff_min = float(GameState.cfg("player.aim_assist.falloff.min", _falloff_min))
	GameState.aim_assist_changed.connect(_on_aim_assist_level_changed)


func _exit_tree() -> void:
	# G016：显式断开档位信号（对齐 player.gd C22 模式），节点未 free 重新入树不重复连接
	if GameState.aim_assist_changed.is_connected(_on_aim_assist_level_changed):
		GameState.aim_assist_changed.disconnect(_on_aim_assist_level_changed)
	if GameState.aim_frame_layer == self:
		GameState.aim_frame_layer = null


func _load_level_params() -> void:
	var base := "player.aim_assist.levels." + String(GameState.aim_assist_level) + "."
	_frame_pad = GameState.cfg(base + "frame_pad", _frame_pad)
	_magnet_range = float(GameState.cfg(base + "magnet_range", _magnet_range))
	_magnet_strength = float(GameState.cfg(base + "magnet_strength", _magnet_strength))
	_magnet_max_speed = float(GameState.cfg(base + "magnet_max_speed", _magnet_max_speed))


func _on_aim_assist_level_changed(_level: StringName) -> void:
	_load_level_params()


func _process(_delta: float) -> void:
	var p := GameState.player_ref as Player
	_hover = marked_target_at(p.aim_point()) if p != null else null
	queue_redraw()


## 框半宽：碰撞半径（机体尺寸族，setup 已 ×ws 写入 meta）+ frame_pad
## A7：测试/诊断白盒断言经公开接口
func frame_pad() -> float:
	return _frame_pad


func frame_half_size(e: Enemy) -> float:
	# C23：碰撞半径经 meta 缓存——setup 后恒定（仅 scale.x 随缩放变化），
	# 避免 _draw/扫描路径每帧 get_node_or_null("CollisionShape2D")。
	# 2026-08-03 审计：meta 值已在 enemy.setup 乘过 world_scale，此处不得再乘 e.scale.x
	#（scale.x 同样含 ws，再乘即 ws 平方，0.5 钳制恰好掩盖；ws 上调时框尺寸非线性暴涨）
	var r := 0.0
	if not e.has_meta("aim_frame_radius"):
		var shape_node := e.get_node_or_null("CollisionShape2D") as CollisionShape2D
		var r_base := 0.0
		if shape_node != null and shape_node.shape is CircleShape2D:
			r_base = (shape_node.shape as CircleShape2D).radius
		e.set_meta("aim_frame_radius", r_base)
	r = float(e.get_meta("aim_frame_radius"))
	return r + _frame_pad


## 世界坐标点命中的标记敌：方形框包含判定，多重叠时取框心最近者；无命中返回 null
## P1-3：同渲染帧同点缓存（aim_point 平滑推点与 _process 高亮各查一次，帧内结果一致）
func marked_target_at(point: Vector2) -> Enemy:
	var frame := Engine.get_process_frames()
	if frame == _target_cache_frame and point == _target_cache_point:
		return _target_cache_result
	var best: Enemy = null
	var best_sq := INF
	for node in GameState.enemies:
		var e := node as Enemy
		if e == null or not e.aim_marked:
			continue
		var half := frame_half_size(e)
		var d: Vector2 = (point - e.global_position).abs()
		if d.x > half or d.y > half:
			continue
		var d_sq := point.distance_squared_to(e.global_position)
		if d_sq < best_sq:
			best_sq = d_sq
			best = e
	_target_cache_frame = frame
	_target_cache_point = point
	_target_cache_result = best
	return best


## P1-3 准星磁吸修正向量：把准星轻微拉向最近框外标记敌（框内归 stickiness 管辖）。
## 静止/抖动（|delta| < input_min）与高速甩枪（>= input_full）直接返回 ZERO——输入优先，
## 静止无磁吸天然满足；强度 = strength × (1 - 框沿距/range) × 输入 smoothstep × 距离衰减，
## 钳到 max_speed 防瞬移。无标记敌返回 ZERO。热路径无 sin/cos、无 cfg。
func magnet_pull(point: Vector2, input_delta: Vector2) -> Vector2:
	var ilen := input_delta.length()
	if ilen < _magnet_input_min or ilen >= _magnet_input_full:
		return Vector2.ZERO
	var best: Enemy = null
	var best_d := INF
	for node in GameState.enemies:
		var e := node as Enemy
		if e == null or not e.aim_marked:
			continue
		var half := frame_half_size(e)
		# 矩形框沿距（0 = 框内，归 stickiness 不磁吸）
		var dx := absf(point.x - e.global_position.x) - half
		var dy := absf(point.y - e.global_position.y) - half
		if dx <= 0.0 and dy <= 0.0:
			continue
		if dx > _magnet_range or dy > _magnet_range:
			continue  # 轴距粗筛，省 sqrt
		var d := Vector2(dx, dy).length()
		if d >= best_d or d > _magnet_range:
			continue
		best_d = d
		best = e
	if best == null:
		return Vector2.ZERO
	var t := (ilen - _magnet_input_min) / (_magnet_input_full - _magnet_input_min)
	var input_scale := 1.0 - t * t * (3.0 - 2.0 * t)  # smoothstep：慢速精瞄全辅助，快速甩枪退出
	var p := GameState.player_ref
	var falloff := _dist_falloff(best.global_position.distance_to(p.global_position if p != null else point))
	var mag := _magnet_strength * (1.0 - best_d / _magnet_range) * input_scale * falloff
	return (best.global_position - point).normalized() * minf(mag, _magnet_max_speed)


## P1-3 锥形弱追踪查询：从 origin 沿 aim_dir（单位向量）锥角（cone_cos 余弦值）内的最近标记敌；
## 距离超过 falloff.end 硬截止（远距不误绑）；无命中返回 null。O(enemies) 与 marked_target_at 同级。
func nearest_cone_target(origin: Vector2, aim_dir: Vector2, cone_cos: float) -> Enemy:
	var best: Enemy = null
	var best_d := INF
	for node in GameState.enemies:
		var e := node as Enemy
		if e == null or not e.aim_marked:
			continue
		var to: Vector2 = e.global_position - origin
		var d := to.length()
		if d > _falloff_end or d >= best_d:
			continue
		if aim_dir.dot(to / d) < cone_cos:
			continue
		best_d = d
		best = e
	return best


## P1-3 距离衰减（G018：与 Player.aim_dist_falloff 共用 Player.dist_falloff_curve 单实现）
func _dist_falloff(d: float) -> float:
	return Player.dist_falloff_curve(d, _falloff_peak, _falloff_end, _falloff_min)


func _draw() -> void:
	var flicker := 0.55 + 0.35 * Enemy.sin_fast(Time.get_ticks_msec() / 1000.0 * 4.0)
	for node in GameState.enemies:
		var e := node as Enemy
		if e == null or not e.aim_marked:
			continue
		var c := (COLOR_HOVER if e == _hover else COLOR) * Color(1.0, 1.0, 1.0, flicker)
		_draw_bracket(e.global_position, frame_half_size(e), c)


func _draw_bracket(center: Vector2, half: float, c: Color) -> void:
	var arm := half * ARM_RATIO
	for sx in [-1.0, 1.0]:
		for sy in [-1.0, 1.0]:
			var corner := center + Vector2(sx * half, sy * half)
			draw_line(corner, corner - Vector2(sx * arm, 0.0), c, WIDTH, true)
			draw_line(corner, corner - Vector2(0.0, sy * arm), c, WIDTH, true)
