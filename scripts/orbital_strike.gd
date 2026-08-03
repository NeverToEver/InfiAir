class_name OrbitalStrike
extends CanvasLayer
## 轨道打击清场动画（对齐原作 homecoming ORBITAL_STRIKE 阶段，见 docs/PORTING_PARITY.md）。
## 从基地「继续出击」时由 main._resume_from_base() 触发；树保持暂停播放（process_mode=Always）。
## 时轴（进度 p = t / DURATION）：
##   [0, MISSILE_FROM)         瞄准具淡入：命中点脉冲环 ×3 + 十字线（青色）
##   [MISSILE_FROM, IMPACT_AT) 导弹自屏顶 ease-in 下落（拖尾线 + 三角弹体 + 辉光头）
##   IMPACT_AT                 struck 信号：main 在此刻清场（Boss 保留）并恢复对局；
##                             命中演出：全屏青闪 + 纵向光柱 + 扩散环/内环 + 侧向光线，衰减至结束
##   >= 1.0                    finished 信号并自销毁
## 数值取 balance.json effects.orbital_strike，脚本默认值须保持一致。

signal struck
signal finished

var DURATION := 1.4
var IMPACT_AT := 0.56
var MISSILE_FROM := 0.3
var RETICLE_RADIUS := 46.0
var IMPACT_Y_RATIO := 0.42

## 瞄准具/光柱主色（对齐原作青色 82,236,218 系）
const CYAN := Color(0.32, 0.93, 0.85)
const RING_POINTS := 48
const MISSILE_START_Y := -140.0

var _t := 0.0
var _impacted := false
var _impact_point := Vector2.ZERO

var _reticle: Node2D = null  # 瞄准具（3 脉冲环 + 十字线）
var _reticle_rings: Array[Line2D] = []
var _reticle_cross: Node2D = null
var _missile: Node2D = null  # 导弹容器（拖尾/弹体/辉光）
var _missile_trail: Line2D = null
var _flash: ColorRect = null
var _column: Polygon2D = null
var _ring_outer: Line2D = null
var _ring_inner: Line2D = null
var _rays: Array[Polygon2D] = []
var _screen := Vector2.ZERO  # _ready 缓存视口尺寸（D17：命中段热路径免每帧查询）


func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS
	layer = 24  # 对局世界与 HUD 之上、基地 UI（25）之下
	# H15（健壮性审核）：时轴序钳制——duration=0 首帧 finished、impact_at≥1.0 时 struck 不可达
	# （main 收不到 _on_orbital_struck，树保持暂停+锁输入软锁）、missile_from≥impact_at 时瞄准段除零
	DURATION = maxf(GameState.cfg("effects.orbital_strike.duration", DURATION), 0.01)
	IMPACT_AT = minf(GameState.cfg("effects.orbital_strike.impact_at", IMPACT_AT), 0.95)
	MISSILE_FROM = maxf(minf(GameState.cfg("effects.orbital_strike.missile_from", MISSILE_FROM), IMPACT_AT - 0.05), 0.0)
	RETICLE_RADIUS = GameState.cfg("effects.orbital_strike.reticle_radius", RETICLE_RADIUS)
	IMPACT_Y_RATIO = GameState.cfg("effects.orbital_strike.impact_y_ratio", IMPACT_Y_RATIO)
	_screen = get_viewport().get_visible_rect().size
	_impact_point = Vector2(_screen.x * 0.5, _screen.y * IMPACT_Y_RATIO)
	_build_reticle()
	_build_missile()
	_build_impact_fx()


func _process(delta: float) -> void:
	_t += delta
	var p := _t / DURATION
	if p >= 1.0:
		# 兜底（2026-08-03 审计）：单帧大 delta（窗口失焦恢复/低端机卡顿）可越过 IMPACT_AT 直达 1.0，
		# 必须先补发 struck——它是 main 恢复对局（paused=false + unlock_input）的唯一入口，缺发则软锁
		if not _impacted:
			_impacted = true
			_missile.hide()
			_reticle.hide()
			GameState.play_sfx(GameState.SFX_EXPLOSION_BIG)
			GameState.shake(GameState.cfg("effects.shake.boss_seq_final", 24.0))
			struck.emit()
		finished.emit()
		queue_free()
		return
	if not _impacted and p >= IMPACT_AT:
		_impacted = true
		_missile.hide()
		_reticle.hide()
		GameState.play_sfx(GameState.SFX_EXPLOSION_BIG)
		GameState.shake(GameState.cfg("effects.shake.boss_seq_final", 24.0))
		struck.emit()
	_update_visuals(p)


## 瞄准具：3 圈脉冲环 + 缓慢旋转的十字线，贴在命中点上
func _build_reticle() -> void:
	_reticle = Node2D.new()
	_reticle.position = _impact_point
	add_child(_reticle)
	for i in 3:
		var ring := _make_ring_line(RETICLE_RADIUS, 2.0, CYAN)
		_reticle.add_child(ring)
		_reticle_rings.append(ring)
	_reticle_cross = Node2D.new()
	_reticle.add_child(_reticle_cross)
	var arm := RETICLE_RADIUS * 1.5
	for pts in [
		[Vector2(-arm, 0.0), Vector2(-arm * 0.4, 0.0)],
		[Vector2(arm * 0.4, 0.0), Vector2(arm, 0.0)],
		[Vector2(0.0, -arm), Vector2(0.0, -arm * 0.4)],
		[Vector2(0.0, arm * 0.4), Vector2(0.0, arm)],
	]:
		var seg := Line2D.new()
		seg.width = 2.0
		seg.default_color = CYAN
		seg.points = PackedVector2Array(pts)
		_reticle_cross.add_child(seg)


## 导弹：拖尾线（透明→青渐变）+ 朝下的三角弹体 + 辉光头
func _build_missile() -> void:
	_missile = Node2D.new()
	add_child(_missile)
	_missile_trail = Line2D.new()
	_missile_trail.width = 3.0
	var grad := Gradient.new()
	grad.set_color(0, Color(CYAN, 0.0))
	grad.set_color(1, CYAN)
	_missile_trail.gradient = grad
	_missile_trail.points = PackedVector2Array([Vector2.ZERO, Vector2.ZERO])  # C28：预分配，帧内只写元素
	_missile.add_child(_missile_trail)
	var body := Polygon2D.new()
	body.polygon = PackedVector2Array([Vector2(0.0, 14.0), Vector2(-5.0, -10.0), Vector2(5.0, -10.0)])
	body.color = Color(0.9, 1.0, 0.98)
	_missile.add_child(body)
	var glow := Polygon2D.new()
	glow.polygon = _circle_points(10.0, 16)
	glow.color = Color(CYAN, 0.55)
	_missile.add_child(glow)
	_missile.hide()


## 命中演出节点（初始全透明，struck 后随进度衰减）
func _build_impact_fx() -> void:
	_flash = ColorRect.new()
	_flash.color = Color(CYAN, 0.0)
	_flash.set_anchors_preset(Control.PRESET_FULL_RECT)
	_flash.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_flash)
	var screen := get_viewport().get_visible_rect().size
	_column = Polygon2D.new()
	_column.polygon = PackedVector2Array(
		[
			Vector2(_impact_point.x - 45.0, 0.0),
			Vector2(_impact_point.x + 45.0, 0.0),
			Vector2(_impact_point.x + 45.0, screen.y),
			Vector2(_impact_point.x - 45.0, screen.y),
		]
	)
	_column.color = Color(CYAN, 0.0)
	add_child(_column)
	_ring_outer = _make_ring_line(40.0, 4.0, Color(CYAN, 0.0))
	_ring_outer.position = _impact_point
	add_child(_ring_outer)
	_ring_inner = _make_ring_line(20.0, 2.0, Color(CYAN, 0.0))
	_ring_inner.position = _impact_point
	add_child(_ring_inner)
	for dir in [-1.0, 1.0]:
		var ray := Polygon2D.new()
		ray.polygon = PackedVector2Array(
			[
				Vector2(_impact_point.x, _impact_point.y - 2.0),
				Vector2(_impact_point.x + dir * screen.x * 0.5, _impact_point.y - 14.0),
				Vector2(_impact_point.x + dir * screen.x * 0.5, _impact_point.y + 14.0),
				Vector2(_impact_point.x, _impact_point.y + 2.0),
			]
		)
		ray.color = Color(CYAN, 0.0)
		add_child(ray)
		_rays.append(ray)


func _update_visuals(p: float) -> void:
	if p < IMPACT_AT:
		# 瞄准具：整体淡入 + 三环错相脉冲 + 十字线缓转
		var fade := clampf(p / 0.08, 0.0, 1.0)
		_reticle.modulate = Color(1.0, 1.0, 1.0, fade)
		for i in _reticle_rings.size():
			var pulse := 0.7 + 0.3 * Enemy.sin_fast(TAU * (p * 4.0 + float(i) / 3.0))
			_layout_ring(_reticle_rings[i], RETICLE_RADIUS * pulse)
		_reticle_cross.rotation = p * 1.5
		if p >= MISSILE_FROM:
			_missile.show()
			var mp := (p - MISSILE_FROM) / (IMPACT_AT - MISSILE_FROM)
			var head := Vector2(_impact_point.x, lerpf(MISSILE_START_Y, _impact_point.y, mp * mp))
			_missile.position = head
			# C28：预分配 2 点，set_point_position 原地写（points[i]= 值语义副本不生效）
			_missile_trail.set_point_position(0, Vector2(0.0, MISSILE_START_Y - head.y))
			_missile_trail.set_point_position(1, Vector2.ZERO)
	else:
		# 命中后：q ∈ [0,1] 全程衰减
		var q := (p - IMPACT_AT) / (1.0 - IMPACT_AT)
		var fade_out := 1.0 - q
		_flash.color = Color(CYAN, 0.85 * fade_out * fade_out)
		_column.color = Color(CYAN, 0.55 * fade_out)
		var screen := _screen
		var diag := screen.length()
		_layout_ring(_ring_outer, lerpf(40.0, diag * 0.6, 1.0 - fade_out * fade_out))
		_ring_outer.default_color = Color(CYAN, 0.9 * fade_out)
		_layout_ring(_ring_inner, lerpf(20.0, RETICLE_RADIUS * 3.0, q))
		_ring_inner.default_color = Color(CYAN, 0.8 * fade_out)
		for ray in _rays:
			ray.color = Color(CYAN, 0.5 * fade_out)


func _make_ring_line(radius: float, width: float, color: Color) -> Line2D:
	var ring := Line2D.new()
	ring.width = width
	ring.default_color = color
	ring.closed = true
	# C28：预建点集（长度固定），帧内经 set_point_position 原地改写（零分配、线宽不随 scale 变）
	ring.points = _circle_points(1.0, RING_POINTS)
	_layout_ring(ring, radius)
	return ring


func _layout_ring(ring: Line2D, radius: float) -> void:
	# C28：原地写点集元素（set_point_position 直写内部数组），不重建 PackedVector2Array、
	# 不缩放节点（缩放会连带放大线宽）
	for i in RING_POINTS:
		var a := TAU * float(i) / float(RING_POINTS)
		ring.set_point_position(i, Vector2(cos(a), sin(a)) * radius)


func _circle_points(radius: float, count: int) -> PackedVector2Array:
	var pts := PackedVector2Array()
	pts.resize(count)
	for i in count:
		var a := TAU * float(i) / float(count)
		pts[i] = Vector2(cos(a), sin(a)) * radius
	return pts
