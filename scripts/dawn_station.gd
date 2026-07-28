class_name DawnStation
extends RefCounted
## 初代环带空间站「曙光」共享构件（docs/RETURN_HOME_CINEMATIC.md §0.2 几何 / §1.1 虚影变换）。
## 纯静态工厂：build() 返回中心在原点的 Node2D，调用方负责 position/scale 与入树。
## 三处复用：开场镜头 1（DESTROYED 实体毁灭态）、返航镜头 2/3/4（PHANTOM 全息虚影态）、
## 基地 UI 背景层（PHANTOM，自行压 modulate.a）。粒子发射器 ≤96/个，与过场性能预算一致。

enum Mode {
	DESTROYED,  # 实体毁灭态：冷钢蓝灰 + 破口残骸（开场镜头 1 现状配色，提取自 intro_cinematic._build_shot1）
	PHANTOM,  # 全息虚影态：ADD 青蓝 + 慢呼吸 + 扫描带 + 数据流粒子 + 破口能量网格（§1.1 四层变换）
}

const RING_RADIUS := 260.0  # 环体主弧半径
const BREACH_START := 0.5  # 破口起角（rad，右下象限，识别锚点不可移动）
const BREACH_END := 1.2  # 破口止角


class _Dot:
	extends Node2D
	var radius := 8.0
	var dot_color := Color.WHITE

	func _draw() -> void:
		draw_circle(Vector2.ZERO, radius, dot_color)


static func _dot(radius: float, color: Color, additive := true) -> Node2D:
	var dot := _Dot.new()
	dot.radius = radius
	dot.dot_color = color
	if additive:
		var mat := CanvasItemMaterial.new()
		mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
		dot.material = mat
	return dot


static func _line(points: PackedVector2Array, color: Color, width: float = 2.0) -> Line2D:
	var l := Line2D.new()
	l.points = points
	l.default_color = color
	l.width = width
	return l


static func _rect_poly(w: float, h: float, color: Color) -> Polygon2D:
	var p := Polygon2D.new()
	p.polygon = PackedVector2Array([
		Vector2(-w * 0.5, -h * 0.5), Vector2(w * 0.5, -h * 0.5),
		Vector2(w * 0.5, h * 0.5), Vector2(-w * 0.5, h * 0.5),
	])
	p.color = color
	return p


static func _additive(item: CanvasItem) -> CanvasItem:
	var mat := CanvasItemMaterial.new()
	mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	item.material = mat
	return item


static func _particles(cfg: Dictionary) -> GPUParticles2D:
	var p := GPUParticles2D.new()
	p.amount = mini(int(cfg.get("amount", 32)), 96)  # 硬性上限：每发射器 ≤96
	p.lifetime = cfg.get("lifetime", 1.0)
	p.explosiveness = cfg.get("explosiveness", 0.0)
	var mat := ParticleProcessMaterial.new()
	mat.direction = cfg.get("direction", Vector3(0.0, -1.0, 0.0))
	mat.spread = cfg.get("spread", 180.0)
	mat.initial_velocity_min = cfg.get("vel_min", 100.0)
	mat.initial_velocity_max = cfg.get("vel_max", 200.0)
	mat.gravity = cfg.get("gravity", Vector3.ZERO)
	mat.damping_min = cfg.get("damping_min", 0.0)
	mat.damping_max = cfg.get("damping_max", 0.0)
	mat.scale_min = cfg.get("scale_min", 2.0)
	mat.scale_max = cfg.get("scale_max", 4.0)
	mat.color = cfg.get("color", Color(0.0, 0.83, 1.0, 0.35))
	if cfg.has("emission_ring_radius"):
		mat.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_RING
		mat.emission_ring_axis = Vector3(0.0, 0.0, 1.0)
		mat.emission_ring_radius = cfg.get("emission_ring_radius", 260.0)
		mat.emission_ring_inner_radius = cfg.get("emission_ring_inner_radius", 0.0)
		mat.emission_ring_height = 0.0
	p.process_material = mat
	if cfg.get("additive", true):
		_additive(p)
	return p


## 构建站体（中心在原点，未定位）。DESTROYED = 开场镜头 1 现状视觉（纯提取，行为不变）；
## PHANTOM = §1.1 四层虚影变换全开（全息基底/扫描带/数据流/破口能量网格）。
static func build(mode: Mode = Mode.DESTROYED) -> Node2D:
	var station := Node2D.new()
	station.name = "DawnStation"
	if mode == Mode.DESTROYED:
		_build_destroyed(station)
	else:
		_build_phantom(station)
	return station


## 站体共享几何：环体主弧 + 内外廓细节环 + 舱段刻线 + 8 舱段 + 辐条 + 中心毂。
## palette 键：ring/detail/tick/seg/seg_edge/spoke/hub/hub_ring；additive=true 时全构件改叠加态。
static func _build_body(station: Node2D, palette: Dictionary, additive: bool) -> void:
	var ring_points := PackedVector2Array()
	for i in 48:
		var a := TAU * float(i) / 48.0
		ring_points.append(Vector2(cos(a), sin(a)) * RING_RADIUS)
	var ring := _line(ring_points, palette["ring"], 26.0)
	ring.closed = true
	if additive:
		_additive(ring)
	station.add_child(ring)
	for r_detail in [232.0, 288.0]:
		var detail_points := PackedVector2Array()
		for i in 64:
			var a := TAU * float(i) / 64.0
			detail_points.append(Vector2(cos(a), sin(a)) * r_detail)
		var detail := _line(detail_points, palette["detail"], 2.0)
		detail.closed = true
		if additive:
			_additive(detail)
		station.add_child(detail)
	# 舱段分块刻线：16 条径向短刻线跨环体
	for i in 16:
		var a := TAU * float(i) / 16.0
		var tick := _line(
			PackedVector2Array([Vector2(cos(a), sin(a)) * 244.0, Vector2(cos(a), sin(a)) * 276.0]),
			palette["tick"], 3.0
		)
		if additive:
			_additive(tick)
		station.add_child(tick)
	# 舱段矩形 ×8 + 描边 + 辐条（破损舱段缺失：0.4–1.4 rad）
	for i in 8:
		var a := TAU * float(i) / 8.0
		if a > 0.4 and a < 1.4:
			continue
		var seg := _rect_poly(64.0, 40.0, palette["seg"])
		seg.position = Vector2(cos(a), sin(a)) * RING_RADIUS
		seg.rotation = a + PI * 0.5
		if additive:
			_additive(seg)
		station.add_child(seg)
		var seg_edge := _rect_poly(70.0, 46.0, palette["seg_edge"])
		seg_edge.position = seg.position
		seg_edge.rotation = seg.rotation
		if additive:
			_additive(seg_edge)
		station.add_child(seg_edge)
		station.move_child(seg_edge, seg.get_index())
		var spoke := _line(
			PackedVector2Array([Vector2(cos(a), sin(a)) * 70.0, Vector2(cos(a), sin(a)) * 240.0]),
			palette["spoke"], 8.0
		)
		if additive:
			_additive(spoke)
		station.add_child(spoke)
	station.add_child(_dot(66.0, palette["hub"], additive))
	# 中心毂细节环
	var hub_ring_points := PackedVector2Array()
	for i in 24:
		var a := TAU * float(i) / 24.0
		hub_ring_points.append(Vector2(cos(a), sin(a)) * 46.0)
	var hub_ring := _line(hub_ring_points, palette["hub_ring"], 2.5)
	hub_ring.closed = true
	if additive:
		_additive(hub_ring)
	station.add_child(hub_ring)


## 破口锯齿轮廓顶点（两态共用：毁灭态=近黑填充，虚影态=亮线描边）
static func _jagged_points() -> PackedVector2Array:
	return PackedVector2Array([
		Vector2(cos(0.45), sin(0.45)) * 240.0, Vector2(cos(0.6), sin(0.6)) * 282.0,
		Vector2(cos(0.85), sin(0.85)) * 236.0, Vector2(cos(1.1), sin(1.1)) * 284.0,
		Vector2(cos(1.25), sin(1.25)) * 244.0, Vector2(cos(0.85), sin(0.85)) * 262.0,
	])


## 实体毁灭态：现状配色 + 破口暗弧覆盖/锯齿填充/3 块剥落碎片外飘翻滚
static func _build_destroyed(station: Node2D) -> void:
	_build_body(station, {
		"ring": Color(0.38, 0.45, 0.58),
		"detail": Color(0.55, 0.65, 0.8, 0.5),
		"tick": Color(0.18, 0.22, 0.3),
		"seg": Color(0.48, 0.56, 0.68),
		"seg_edge": Color(0.6, 0.7, 0.85, 0.35),
		"spoke": Color(0.3, 0.36, 0.48),
		"hub": Color(0.28, 0.34, 0.45),
		"hub_ring": Color(0.5, 0.6, 0.75, 0.6),
	}, false)
	# 破损段：暗色弧覆盖出缺口（0.5–1.2 rad）
	var broken_points := PackedVector2Array()
	for i in 7:
		var a := BREACH_START + (BREACH_END - BREACH_START) * float(i) / 6.0
		broken_points.append(Vector2(cos(a), sin(a)) * RING_RADIUS)
	station.add_child(_line(broken_points, Color(0.05, 0.05, 0.08), 30.0))
	# 破口锯齿边缘
	var jagged := Polygon2D.new()
	jagged.polygon = _jagged_points()
	jagged.color = Color(0.03, 0.03, 0.05)
	station.add_child(jagged)
	# 破口剥落碎片：小多边形缓慢外飘 + 翻滚
	for k in 3:
		var flake := Polygon2D.new()
		flake.polygon = PackedVector2Array([
			Vector2(-8.0, -5.0), Vector2(9.0, -3.0), Vector2(5.0, 7.0), Vector2(-6.0, 6.0)])
		flake.color = Color(0.3, 0.36, 0.46)
		var fa := 0.6 + 0.3 * k
		flake.position = Vector2(cos(fa), sin(fa)) * 265.0
		station.add_child(flake)
		var ft := station.create_tween().set_parallel(true).set_loops()
		ft.tween_property(flake, "position", flake.position + Vector2(cos(fa), sin(fa)) * 60.0, 2.0)
		ft.tween_property(flake, "rotation", flake.rotation + 2.5, 2.0)


## 全息虚影态（§1.1）：四层变换——全息基底 / 扫描线光晕 / 数据流粒子 / 破口能量网格修补
static func _build_phantom(station: Node2D) -> void:
	# 第 1 层：全息基底——全构件 ADD 叠加，冷钢蓝灰映射到全息青蓝
	_build_body(station, {
		"ring": Color(0.0, 0.5, 1.0, 0.30),  # ACCENT_BLUE α0.30 主弧
		"detail": Color(0.0, 0.55, 1.0, 0.18),
		"tick": Color(0.0, 0.55, 1.0, 0.18),
		"seg": Color(0.0, 0.6, 1.0, 0.22),
		"seg_edge": Color(0.0, 0.6, 1.0, 0.22),
		"spoke": Color(0.0, 0.5, 1.0, 0.18),
		"hub": Color(0.0, 0.55, 1.0, 0.30),
		"hub_ring": Color(0.0, 0.6, 1.0, 0.18),
	}, true)
	# 整体容器 4s 慢呼吸（±0.08，投影不稳定感）
	var breathe := station.create_tween().set_loops()
	breathe.tween_property(station, "modulate:a", 0.77, 2.0
	).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	breathe.tween_property(station, "modulate:a", 0.93, 2.0
	).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	# 第 2 层：扫描线光晕——环体外缘常亮大半径辉光 + 40px 扫描带纵向 3.5s/趟
	station.add_child(_dot(360.0, Color(0.0, 0.83, 1.0, 0.12)))
	var scan_band := _rect_poly(680.0, 40.0, Color(0.0, 0.83, 1.0, 0.08))
	scan_band.position = Vector2(0.0, -340.0)
	_additive(scan_band)
	station.add_child(scan_band)
	var scan := station.create_tween().set_loops()
	scan.tween_property(scan_band, "position:y", 340.0, 3.5).set_trans(Tween.TRANS_LINEAR)
	scan.tween_property(scan_band, "position:y", -340.0, 0.0)
	# 第 3 层：数据流粒子——①边缘逸散（环轮廓切向慢飘）②内部结构流（环面内侧往返）
	var edge_flow := _particles({
		"amount": 48, "lifetime": 2.5, "vel_min": 15.0, "vel_max": 35.0,
		"scale_min": 1.0, "scale_max": 2.0, "color": Color(0.0, 0.83, 1.0, 0.35),
		"emission_ring_radius": 260.0, "emission_ring_inner_radius": 250.0,
	})
	station.add_child(edge_flow)
	var inner_flow := _particles({
		"amount": 40, "lifetime": 3.0, "vel_min": 15.0, "vel_max": 45.0,
		"scale_min": 1.0, "scale_max": 2.0, "color": Color(0.0, 0.7, 1.0, 0.25),
		"emission_ring_radius": 200.0, "emission_ring_inner_radius": 80.0,
	})
	station.add_child(inner_flow)
	# 第 4 层：破口能量网格修补——跨越缺口的经纬细格（青 α0.4），每格相位错开 0.13s
	# 低频闪烁 + 偶发整格掉线 0.1s；缺口锯齿轮廓以 α0.5 亮线描出
	var grid_lines: Array[Line2D] = []
	for g in 4:  # 经线：4 条径向跨 r240→280
		var a := BREACH_START + (BREACH_END - BREACH_START) * float(g) / 3.0
		var meridian := _line(
			PackedVector2Array([Vector2(cos(a), sin(a)) * 240.0, Vector2(cos(a), sin(a)) * 280.0]),
			Color(0.0, 0.83, 1.0, 0.4), 1.5)
		_additive(meridian)
		station.add_child(meridian)
		grid_lines.append(meridian)
	for g in 3:  # 纬线：3 条弧跨 0.5–1.2 rad
		var r_lat := 246.0 + 12.0 * g
		var lat_points := PackedVector2Array()
		for s in 8:
			var a := BREACH_START + (BREACH_END - BREACH_START) * float(s) / 7.0
			lat_points.append(Vector2(cos(a), sin(a)) * r_lat)
		var latitude := _line(lat_points, Color(0.0, 0.83, 1.0, 0.4), 1.5)
		_additive(latitude)
		station.add_child(latitude)
		grid_lines.append(latitude)
	for g in grid_lines.size():
		var gl := grid_lines[g]
		gl.modulate.a = 0.4
		var flicker := station.create_tween().set_loops()
		flicker.tween_interval(0.13 * g)  # 相位错开
		flicker.tween_property(gl, "modulate:a", 0.15, 0.3)
		flicker.tween_property(gl, "modulate:a", 0.55, 0.3)
		flicker.tween_interval(0.9 + 0.2 * (g % 3))
		# 偶发整格「掉线」0.1s 再亮起
		flicker.tween_property(gl, "modulate:a", 0.0, 0.05)
		flicker.tween_interval(0.1)
		flicker.tween_property(gl, "modulate:a", 0.4, 0.1)
	var jagged_outline := _line(_jagged_points(), Color(0.0, 0.83, 1.0, 0.5), 2.0)
	_additive(jagged_outline)
	station.add_child(jagged_outline)
