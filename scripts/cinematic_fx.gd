## 过场/演出共享特效工具：软径向光晕、带纹理粒子、冲击波环、分层能量束、速度线/放射线场。
## 供 intro_cinematic / return_cinematic / mothership_summon_window / warp_gate / mothership 复用，
## 替代此前在多处复制的硬边 GlowDot 与无纹理粒子工厂；全部零依赖、代码程序化构建。
class_name CinematicFx
extends RefCounted

const SOFT_TEX_SIZE := 64
const PARTICLE_AMOUNT_CAP := 96  # 硬性上限：每发射器 ≤96（性能预算：总存活 ≤400）

static var _soft_tex: ImageTexture = null


## 静态缓存的 64×64 径向渐变软点贴图（白色，alpha pow 衰减）：
## 粒子与光晕共用，消除硬边实心圆的廉价感；颜色经 modulate/process_material 乘算。
static func soft_texture() -> ImageTexture:
	if _soft_tex != null:
		return _soft_tex
	var img := Image.create(SOFT_TEX_SIZE, SOFT_TEX_SIZE, false, Image.FORMAT_RGBA8)
	var half := SOFT_TEX_SIZE * 0.5
	for y in SOFT_TEX_SIZE:
		for x in SOFT_TEX_SIZE:
			var d := Vector2(x + 0.5 - half, y + 0.5 - half).length() / half
			img.set_pixel(x, y, Color(1.0, 1.0, 1.0, pow(clampf(1.0 - d, 0.0, 1.0), 2.2)))
	_soft_tex = ImageTexture.create_from_image(img)
	return _soft_tex


## 软径向光晕：Sprite2D 承载软点贴图，scale/modulate 语义与旧 GlowDot 一致（可直接 tween）。
## G022：additive material 静态共享（N 机 N 份相同材质 → 1 份，材质只读属性无实例差异）
static var _additive_mat: CanvasItemMaterial = null
static func additive_material() -> CanvasItemMaterial:
	if _additive_mat == null:
		_additive_mat = CanvasItemMaterial.new()
		_additive_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	return _additive_mat


static func soft_glow(radius: float, color: Color, additive := true) -> Sprite2D:
	var s := Sprite2D.new()
	s.texture = soft_texture()
	s.scale = Vector2.ONE * (radius / (SOFT_TEX_SIZE * 0.5))
	s.modulate = color
	if additive:
		s.material = additive_material()
	return s


## 与既有 _particles(cfg) 同契约的粒子工厂（键：amount/lifetime/explosiveness/one_shot/
## direction/spread/vel_min/vel_max/gravity/damping_min/damping_max/scale_min/scale_max/color/additive）。
## 默认挂软点贴图（"textured": false 关闭）；cfg 的 scale 语义保持"像素直径"，内部换算到 64px 贴图。
static func particles(cfg: Dictionary) -> GPUParticles2D:
	var p := GPUParticles2D.new()
	p.amount = mini(int(cfg.get("amount", 32)), PARTICLE_AMOUNT_CAP)
	p.lifetime = cfg.get("lifetime", 1.0)
	p.explosiveness = cfg.get("explosiveness", 0.0)
	p.one_shot = cfg.get("one_shot", false)
	var mat := ParticleProcessMaterial.new()
	mat.direction = cfg.get("direction", Vector3(0.0, -1.0, 0.0))
	mat.spread = cfg.get("spread", 180.0)
	mat.initial_velocity_min = cfg.get("vel_min", 100.0)
	mat.initial_velocity_max = cfg.get("vel_max", 200.0)
	mat.gravity = cfg.get("gravity", Vector3.ZERO)
	mat.damping_min = cfg.get("damping_min", 0.0)
	mat.damping_max = cfg.get("damping_max", 0.0)
	var tex_scale := 1.0
	if cfg.get("textured", true):
		p.texture = soft_texture()
		tex_scale = 1.0 / SOFT_TEX_SIZE
	mat.scale_min = cfg.get("scale_min", 2.0) * tex_scale
	mat.scale_max = cfg.get("scale_max", 4.0) * tex_scale
	mat.color = cfg.get("color", Color(1.0, 0.6, 0.15))
	p.process_material = mat
	if cfg.get("additive", true):
		p.material = additive_material()
	return p


## 闭合椭圆环点集（Line2D 用；ry_ratio 压扁做透视门洞/光圈）。
static func ring_points(n: int, r: float, ry_ratio := 1.0) -> PackedVector2Array:
	var pts := PackedVector2Array()
	pts.resize(n + 1)
	for i in n:
		var a := TAU * float(i) / float(n)
		pts[i] = Vector2(cos(a), sin(a) * ry_ratio) * r
	pts[n] = pts[0]
	return pts


## 双层扩散冲击环（粗辉光环 + 细亮芯环 + 可选低 alpha 填充盘），_ready 起 tween，播完自毁。
class Shockwave:
	extends Node2D
	var radius := 300.0
	var time := 0.6
	var color := Color(1.0, 0.7, 0.3, 0.5)
	var core_color := Color(1.0, 0.95, 0.8, 0.9)
	var width := 12.0
	var ry_ratio := 1.0
	var fill := false
	var start_scale := 0.15

	func _ready() -> void:
		var pts := CinematicFx.ring_points(48, radius, ry_ratio)
		var glow := Line2D.new()
		glow.points = pts
		glow.default_color = color
		glow.width = width
		glow.material = CinematicFx.additive_material()
		add_child(glow)
		var core := Line2D.new()
		core.points = pts
		core.default_color = core_color
		core.width = maxf(width * 0.3, 1.5)
		core.material = CinematicFx.additive_material()
		add_child(core)
		if fill:
			var disk := Polygon2D.new()
			disk.polygon = pts
			disk.color = Color(color.r, color.g, color.b, 0.18)
			disk.material = CinematicFx.additive_material()
			add_child(disk)
			var ftw := disk.create_tween()
			ftw.tween_property(disk, "modulate:a", 0.0, time * 0.5).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
		scale = Vector2.ONE * start_scale
		var tw := create_tween().set_parallel(true)
		tw.tween_property(self, "scale", Vector2.ONE, time).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
		tw.tween_property(glow, "modulate:a", 0.0, time).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)
		tw.tween_property(core, "modulate:a", 0.0, time * 0.8).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)
		tw.chain().tween_callback(queue_free)


## cfg 构造 Shockwave（add_child 后自动播放并自毁）。
static func shockwave(cfg: Dictionary) -> Node2D:
	var sw := Shockwave.new()
	sw.radius = cfg.get("radius", 300.0)
	sw.time = cfg.get("time", 0.6)
	sw.color = cfg.get("color", Color(1.0, 0.7, 0.3, 0.5))
	sw.core_color = cfg.get("core_color", Color(1.0, 0.95, 0.8, 0.9))
	sw.width = cfg.get("width", 12.0)
	sw.ry_ratio = cfg.get("ry_ratio", 1.0)
	sw.fill = cfg.get("fill", false)
	sw.start_scale = cfg.get("start_scale", 0.15)
	return sw


## 分层能量束（宽低 alpha 辉光层 + 窄亮芯层）+ 沿线循环流光软点；
## _process 只推进参数 u 并回写光点 position，零分配。
class BeamFlow:
	extends Node2D
	var _samples: PackedVector2Array
	var _dots: Array[Sprite2D] = []
	var _dot_u: PackedFloat32Array
	var _dot_speed := 0.45
	var _dot_dir := 1.0

	func setup(points: PackedVector2Array, cfg: Dictionary) -> void:
		var color: Color = cfg.get("color", Color(0.4, 0.9, 1.0))
		var width: float = cfg.get("width", 14.0)
		_samples = _resample(points, 24)
		var glow := Line2D.new()
		glow.points = points
		glow.default_color = Color(color, 0.25)
		glow.width = width
		glow.material = CinematicFx.additive_material()
		add_child(glow)
		var core := Line2D.new()
		core.points = points
		core.default_color = Color(color.lightened(0.5), 0.75)
		core.width = maxf(width * 0.22, 1.5)
		core.material = CinematicFx.additive_material()
		add_child(core)
		_dot_speed = cfg.get("dot_speed", 0.45)
		_dot_dir = cfg.get("dot_dir", 1.0)
		var dot_count: int = cfg.get("dot_count", 2)
		_dot_u = PackedFloat32Array()
		_dot_u.resize(dot_count)
		for i in dot_count:
			_dot_u[i] = float(i) / float(maxi(dot_count, 1))
			var dot := CinematicFx.soft_glow(cfg.get("dot_radius", 10.0), cfg.get("dot_color", Color(0.8, 1.0, 1.0)))
			add_child(dot)
			_dots.append(dot)

	static func _resample(points: PackedVector2Array, n: int) -> PackedVector2Array:
		var out := PackedVector2Array()
		out.resize(n)
		var segs := points.size() - 1
		for i in n:
			var f := float(i) / float(n - 1) * segs
			var idx := mini(int(f), segs - 1)
			out[i] = points[idx].lerp(points[idx + 1], f - idx)
		return out

	func _sample_at(u: float) -> Vector2:
		var n := _samples.size()
		var f := clampf(u, 0.0, 1.0) * float(n - 1)
		var idx := mini(int(f), n - 2)
		return _samples[idx].lerp(_samples[idx + 1], f - idx)

	func _process(delta: float) -> void:
		for i in _dots.size():
			_dot_u[i] = fposmod(_dot_u[i] + delta * _dot_speed * _dot_dir, 1.0)
			_dots[i].position = _sample_at(_dot_u[i])


## cfg 构造 BeamFlow（需在 add_child 前调用，setup 在内部完成建层）。
static func beam(points: PackedVector2Array, cfg: Dictionary = {}) -> Node2D:
	var b := BeamFlow.new()
	b.setup(points, cfg)
	return b


## 直线速度线场：预建 Line2D 池，_process 仅位移节点 + 出界回卷，零分配。
class SpeedLineField:
	extends Node2D
	var _lines: Array[Line2D] = []
	var _speeds: PackedFloat32Array
	var _dir := Vector2.DOWN
	var _area := Rect2(0.0, 0.0, 1920.0, 1080.0)
	var _margin := 80.0

	func setup(cfg: Dictionary) -> void:
		_dir = (cfg.get("dir", Vector2.DOWN) as Vector2).normalized()
		_area = cfg.get("area", Rect2(0.0, 0.0, 1920.0, 1080.0))
		var count: int = cfg.get("count", 12)
		var speed_min: float = cfg.get("speed_min", 1600.0)
		var speed_max: float = cfg.get("speed_max", 2600.0)
		var len_min: float = cfg.get("len_min", 60.0)
		var len_max: float = cfg.get("len_max", 180.0)
		var color: Color = cfg.get("color", Color(0.7, 0.85, 1.0, 0.35))
		var width: float = cfg.get("width", 2.0)
		var mat := CinematicFx.additive_material() if cfg.get("additive", true) else null
		_speeds = PackedFloat32Array()
		_speeds.resize(count)
		for i in count:
			_speeds[i] = randf_range(speed_min, speed_max)
			var ln := Line2D.new()
			var len := randf_range(len_min, len_max)
			ln.points = PackedVector2Array([Vector2.ZERO, -_dir * len])
			ln.default_color = color
			ln.width = width
			if mat != null:
				ln.material = mat
			ln.position = Vector2(randf_range(_area.position.x, _area.end.x), randf_range(_area.position.y, _area.end.y))
			add_child(ln)
			_lines.append(ln)

	func _process(delta: float) -> void:
		for i in _lines.size():
			var ln := _lines[i]
			ln.position += _dir * _speeds[i] * delta
			# 沿运动方向越界即回卷到对侧，横向随机换位
			if _dir.y > 0.0 and ln.position.y - _margin > _area.end.y:
				ln.position.y = _area.position.y - _margin
				ln.position.x = randf_range(_area.position.x, _area.end.x)
			elif _dir.y < 0.0 and ln.position.y + _margin < _area.position.y:
				ln.position.y = _area.end.y + _margin
				ln.position.x = randf_range(_area.position.x, _area.end.x)
			elif _dir.x > 0.0 and ln.position.x - _margin > _area.end.x:
				ln.position.x = _area.position.x - _margin
				ln.position.y = randf_range(_area.position.y, _area.end.y)
			elif _dir.x < 0.0 and ln.position.x + _margin < _area.position.x:
				ln.position.x = _area.end.x + _margin
				ln.position.y = randf_range(_area.position.y, _area.end.y)


static func speed_lines(cfg: Dictionary) -> Node2D:
	var f := SpeedLineField.new()
	f.setup(cfg)
	return f


## 径向放射条纹场（跃迁隧道用）：软点贴图拉伸成条，从中心向外生长-淡出循环；
## _process 仅改写 position/scale/modulate，零分配。
class RadialStreaks:
	extends Node2D
	var _streaks: Array[Sprite2D] = []
	var _angles: PackedFloat32Array
	var _progress: PackedFloat32Array
	var _rates: PackedFloat32Array
	var _max_radius := 900.0
	var _color := Color(0.6, 0.85, 1.0, 0.5)

	func setup(cfg: Dictionary) -> void:
		var count: int = cfg.get("count", 28)
		_max_radius = cfg.get("max_radius", 900.0)
		_color = cfg.get("color", Color(0.6, 0.85, 1.0, 0.5))
		var cycle: float = cfg.get("cycle", 1.2)
		_angles = PackedFloat32Array()
		_progress = PackedFloat32Array()
		_rates = PackedFloat32Array()
		_angles.resize(count)
		_progress.resize(count)
		_rates.resize(count)
		for i in count:
			_angles[i] = randf() * TAU
			_progress[i] = randf()
			_rates[i] = randf_range(0.8, 1.3) / cycle
			var s := CinematicFx.soft_glow(32.0, _color)
			s.rotation = _angles[i]
			add_child(s)
			_streaks.append(s)

	func _process(delta: float) -> void:
		for i in _streaks.size():
			_progress[i] = fposmod(_progress[i] + delta * _rates[i], 1.0)
			var p: float = _progress[i]
			var r_head := p * _max_radius
			var r_tail := maxf(0.0, p - 0.3) * _max_radius
			var len := r_head - r_tail
			var s := _streaks[i]
			if len < 2.0:
				s.modulate.a = 0.0
				continue
			var dir := Vector2.from_angle(_angles[i])
			s.position = dir * ((r_head + r_tail) * 0.5)
			s.scale = Vector2(len / 32.0, 6.0 / 32.0)
			s.modulate = Color(_color.r, _color.g, _color.b, _color.a * sin(PI * p))


static func radial_streaks(cfg: Dictionary) -> Node2D:
	var r := RadialStreaks.new()
	r.setup(cfg)
	return r
