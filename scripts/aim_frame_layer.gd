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
var _hover: Enemy = null  # 本帧准星入框的标记敌（高亮显示用）


func _ready() -> void:
	z_index = 9  # 世界实体之上、准星（10）之下
	GameState.aim_frame_layer = self
	_load_level_params()
	GameState.aim_assist_changed.connect(_on_aim_assist_level_changed)


func _exit_tree() -> void:
	if GameState.aim_frame_layer == self:
		GameState.aim_frame_layer = null


func _load_level_params() -> void:
	_frame_pad = GameState.cfg(
		"player.aim_assist.levels." + String(GameState.aim_assist_level) + ".frame_pad", _frame_pad
	)


func _on_aim_assist_level_changed(_level: StringName) -> void:
	_load_level_params()


func _process(_delta: float) -> void:
	var p := GameState.player_ref as Player
	_hover = marked_target_at(p.aim_point()) if p != null else null
	queue_redraw()


## 框半宽：碰撞半径（机体尺寸族，setup 已 ×ws）× 实例缩放 + frame_pad
func frame_half_size(e: Enemy) -> float:
	var r := 0.0
	var shape_node := e.get_node_or_null("CollisionShape2D") as CollisionShape2D
	if shape_node != null and shape_node.shape is CircleShape2D:
		r = (shape_node.shape as CircleShape2D).radius * maxf(e.scale.x, 0.5)
	return r + _frame_pad


## 世界坐标点命中的标记敌：方形框包含判定，多重叠时取框心最近者；无命中返回 null
func marked_target_at(point: Vector2) -> Enemy:
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
	return best


func _draw() -> void:
	var flicker := 0.55 + 0.35 * sin(Time.get_ticks_msec() / 1000.0 * 4.0)
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
