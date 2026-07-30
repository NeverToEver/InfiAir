class_name WarpGate
extends Node2D
## 母舰召唤·穿梭门（世界坐标，挂 Main 下）：小窗演出结束后由 main 创建在母舰停驻点。
## 生命周期：OPENING 展开（gate.open_time）→ HOLD 保持（母舰穿出期间脉动，
## 由 Mothership.begin_warp_in 收尾时调 close()；超时自动关闭兜底）→
## CLOSING 收缩关闭（gate.close_time）→ 自销毁。
## 数值取 balance.json effects.mothership_summon.gate，脚本默认值须保持一致。

var OPEN_TIME := 0.35
var CLOSE_TIME := 0.4
var RADIUS := 190.0  # 门洞半径设计值（× world_scale 生效）
## HOLD 兜底时长：正常由母舰到达触发 close()，母舰被提前回收（返航）时自动关闭
var HOLD_MAX := 3.0

const CYAN := Color(0.32, 0.93, 0.85)
const WARP_BLUE := Color(0.35, 0.6, 1.0)
const ELLIPSE_RATIO := 0.55  # 竖向压扁（透视门洞）

enum Phase { OPENING, HOLD, CLOSING }

var _phase: Phase = Phase.OPENING
var _t := 0.0
var _ring: Line2D
var _ring_inner: Line2D
var _arcs: Array[Line2D] = []
var _mouth: Sprite2D  # 门心软光填充（椭圆压扁，随开合/呼吸同步）
var _mouth_base: Vector2 = Vector2.ONE
var _swirls: Array[Line2D] = []  # 内旋弧 ×2（反向旋转，能量漩涡感）
var _rim_fx: GPUParticles2D  # 门缘内吸粒子（环发射 + 负径向速度）
var _lip: Node2D  # 前唇层：下半环 z 压过母舰，读作「穿门而出」


func _ready() -> void:
	OPEN_TIME = GameState.cfg("effects.mothership_summon.gate.open_time", OPEN_TIME)
	CLOSE_TIME = GameState.cfg("effects.mothership_summon.gate.close_time", CLOSE_TIME)
	RADIUS = GameState.cfg("effects.mothership_summon.gate.radius", RADIUS) * GameState.world_scale
	z_index = -1  # 门洞衬在母舰之后
	_ring = _make_ring(4.0, CYAN)
	_ring_inner = _make_ring(2.0, WARP_BLUE)
	for i in 3:
		var arc := Line2D.new()
		arc.width = 3.0
		arc.default_color = Color(CYAN, 0.7)
		add_child(arc)
		_arcs.append(arc)
	# 门心软光填充（替代硬边圆盘）：软点贴图椭圆压扁，alpha 由 _layout 驱动
	_mouth = CinematicFx.soft_glow(RADIUS * 0.85, Color(WARP_BLUE, 0.0))
	_mouth.scale.y *= ELLIPSE_RATIO
	_mouth_base = _mouth.scale
	add_child(_mouth)
	# 内旋弧 ×2：预建点集，帧内仅旋转/缩放/透明度（零分配）
	for i in 2:
		var swirl := Line2D.new()
		swirl.width = 2.5 - 0.5 * float(i)
		swirl.default_color = Color(CYAN.lightened(0.25), 0.8)
		swirl.points = _arc_points(RADIUS * (0.55 + 0.15 * float(i)), 70.0, 12)
		swirl.material = CinematicFx.additive_material()
		add_child(swirl)
		_swirls.append(swirl)
	# 门缘内吸粒子：环上发射、负径向速度流向门心
	_rim_fx = CinematicFx.particles({
		"amount": 48, "lifetime": 0.55, "vel_min": 0.0, "vel_max": 0.0,
		"scale_min": 3.0, "scale_max": 6.0, "color": Color(CYAN, 0.7),
	})
	var rim_mat := _rim_fx.process_material as ParticleProcessMaterial
	rim_mat.direction = Vector3.ZERO
	rim_mat.spread = 0.0
	rim_mat.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_RING
	rim_mat.emission_ring_axis = Vector3(0.0, 0.0, 1.0)
	rim_mat.emission_ring_radius = RADIUS
	rim_mat.emission_ring_inner_radius = RADIUS * 0.97
	rim_mat.emission_ring_height = 0.0
	rim_mat.radial_velocity_min = -1.9 * RADIUS
	rim_mat.radial_velocity_max = -2.7 * RADIUS
	add_child(_rim_fx)
	# 前唇层：下半环（辉光 + 亮芯）z=5 压过母舰（-1+5=4 > 母舰 0），CLOSING 随整体收缩淡出
	_lip = Node2D.new()
	_lip.z_index = 5
	var lip_pts := _arc_points(RADIUS, 180.0, 25)
	var lip_glow := Line2D.new()
	lip_glow.width = 10.0
	lip_glow.default_color = Color(WARP_BLUE, 0.35)
	lip_glow.points = lip_pts
	lip_glow.material = CinematicFx.additive_material()
	_lip.add_child(lip_glow)
	var lip_core := Line2D.new()
	lip_core.width = 3.5
	lip_core.default_color = Color(CYAN.lightened(0.35), 0.95)
	lip_core.points = lip_pts
	lip_core.material = CinematicFx.additive_material()
	_lip.add_child(lip_core)
	add_child(_lip)
	GameState.play_sfx(GameState.SFX_DASH, -6.0, 0.5)


## 母舰穿出完成（或提前收回）时调用：进入关闭段（幂等）
func close() -> void:
	if _phase == Phase.CLOSING:
		return
	_phase = Phase.CLOSING
	_t = 0.0
	_rim_fx.emitting = false  # 关闭段停止内吸粒子，存量随收缩消散


func _process(delta: float) -> void:
	_t += delta
	match _phase:
		Phase.OPENING:
			var p := clampf(_t / OPEN_TIME, 0.0, 1.0)
			_layout(1.0 - (1.0 - p) * (1.0 - p), 1.0)  # ease-out 展开
			if p >= 1.0:
				_phase = Phase.HOLD
				_t = 0.0
		Phase.HOLD:
			# 保持期：呼吸脉动 + 弧段旋转
			_layout(1.0 + 0.04 * sin(_t * 6.0), 1.0)
			if _t >= HOLD_MAX:
				close()
		Phase.CLOSING:
			var p := clampf(_t / CLOSE_TIME, 0.0, 1.0)
			_layout(1.0 - p, 1.0 - p)
			if p >= 1.0:
				queue_free()
	for i in _arcs.size():
		_arcs[i].rotation = _t * (1.5 + 0.4 * float(i)) * (1.0 if i % 2 == 0 else -1.0)
	# 内旋弧反向旋转（能量漩涡）
	for i in _swirls.size():
		_swirls[i].rotation = -_t * (2.2 + 0.7 * float(i)) * (1.0 if i % 2 == 0 else -1.0)


## scale_p：门洞开合比例；alpha_p：整体透明度
func _layout(scale_p: float, alpha_p: float) -> void:
	_ring.points = _ellipse_points(RADIUS * scale_p, 48)
	_ring.default_color = Color(CYAN, 0.9 * alpha_p)
	_ring_inner.points = _ellipse_points(RADIUS * 0.82 * scale_p, 48)
	_ring_inner.default_color = Color(WARP_BLUE, 0.7 * alpha_p)
	# 新增附件层：预建点集，仅缩放/透明度写（零分配）
	_mouth.scale = _mouth_base * scale_p
	_mouth.modulate.a = 0.4 * alpha_p * scale_p
	for i in _swirls.size():
		_swirls[i].scale = Vector2.ONE * scale_p
		_swirls[i].modulate.a = 0.8 * alpha_p
	_lip.scale = Vector2.ONE * scale_p
	_lip.modulate.a = alpha_p
	for i in _arcs.size():
		var arc := _arcs[i]
		var pts := PackedVector2Array()
		var a0 := TAU * float(i) / 3.0
		for j in 10:
			var a := a0 + deg_to_rad(50.0) * float(j) / 9.0
			pts.append(Vector2(cos(a), sin(a) * ELLIPSE_RATIO) * RADIUS * 1.12 * scale_p)
		arc.points = pts
		arc.default_color = Color(CYAN, 0.7 * alpha_p)


## 扇弧点集（起始角 0，随节点旋转动画；ry 压扁对齐门洞透视）
func _arc_points(radius: float, span_deg: float, count: int) -> PackedVector2Array:
	var pts := PackedVector2Array()
	pts.resize(count)
	for i in count:
		var a := deg_to_rad(span_deg) * float(i) / float(count - 1)
		pts[i] = Vector2(cos(a), sin(a) * ELLIPSE_RATIO) * radius
	return pts


func _make_ring(width: float, color: Color) -> Line2D:
	var ring := Line2D.new()
	ring.width = width
	ring.closed = true
	ring.default_color = color
	add_child(ring)
	return ring


func _ellipse_points(radius: float, count: int) -> PackedVector2Array:
	var pts := PackedVector2Array()
	pts.resize(count)
	for i in count:
		var a := TAU * float(i) / float(count)
		pts[i] = Vector2(cos(a), sin(a) * ELLIPSE_RATIO) * radius
	return pts
