class_name MothershipSummonWindow
extends CanvasLayer
## 母舰召唤·机库小窗（左侧竖长画中画通讯屏）：蓄力完成后由 main._summon_mothership() 弹出。
## 时轴（不暂停对局，process_mode 跟随树）：
##   [0, OPEN_TIME)                    面板淡入
##   镜头 1 SHOT_DURATIONS[0]          机库剖面：3 条充能管线依次断开（MS_SEQ_CHARGE）
##   镜头 2 SHOT_DURATIONS[1]          两侧维护机械臂解除链接收回（MS_SEQ_ARMS）
##   镜头 3 SHOT_DURATIONS[2]          母舰弹射出仓 + 穿梭器蓝环点亮（MS_SEQ_LAUNCH）
##   收尾 CLOSE_TIME                   面板淡出，finished 信号并自销毁
## 数值取 balance.json effects.mothership_summon.window，脚本默认值须保持一致。
## skip() 幂等：立即发 finished（供测试/流程直推）。

signal finished

var OPEN_TIME := 0.25
var CLOSE_TIME := 0.25
var _shot_durations: Array[float] = [0.8, 0.6, 0.7]

const CYAN := Color(0.32, 0.93, 0.85)
const WARP_BLUE := Color(0.35, 0.6, 1.0)
const SHIP_TEXTURE: Texture2D = preload("res://assets/sprites/mothership.png")
const PANEL_SIZE := Vector2(560.0, 840.0)  # 左侧竖长画中画
const SHIP_HOME := Vector2(280.0, 380.0)  # 面板局部坐标
const SHOT_KEYS: Array[String] = ["MS_SEQ_CHARGE", "MS_SEQ_ARMS", "MS_SEQ_LAUNCH"]

var _t := 0.0
var _total := 0.0
var _shot_idx := -1
var _done := false

var _panel: ChamferedPanel
var _stage: Node2D  # 面板局部坐标舞台（960×520）
var _ship: Sprite2D
var _ship_trail: Line2D
var _ship_glow: Sprite2D  # 弹射拖首软光（镜头 3 启用）
var _warp_ring: Line2D
var _flash: ColorRect
var _subtitle: Label
var _charge_lines: Array[Line2D] = []
var _charge_sparks: Array[Sprite2D] = []  # 断开点软火花
var _spark_bursts: Array[GPUParticles2D] = []  # 断开瞬间一次性喷发
var _burst_fired: Array[bool] = [false, false, false]
var _arms: Array[Line2D] = []  # [左臂, 右臂]，各 3 点（基座/关节/末端）


func _ready() -> void:
	layer = 24  # 对局世界与 HUD 之上、基地 UI（25）之下（与 OrbitalStrike 同层）
	OPEN_TIME = GameState.cfg("effects.mothership_summon.window.open_time", OPEN_TIME)
	CLOSE_TIME = GameState.cfg("effects.mothership_summon.window.close_time", CLOSE_TIME)
	var durs: Array = GameState.cfg("effects.mothership_summon.window.shot_durations", _shot_durations)
	_shot_durations = [float(durs[0]), float(durs[1]), float(durs[2])]
	_total = OPEN_TIME + _shot_durations[0] + _shot_durations[1] + _shot_durations[2] + CLOSE_TIME
	_build_panel()
	_build_hangar()
	_update(0.0)


## 立即结束（幂等）：测试或外部流程直推召唤序列
## A7：测试/诊断白盒断言经公开接口
func subtitle() -> Label:
	return _subtitle


func skip() -> void:
	if _done:
		return
	_done = true
	finished.emit()
	queue_free()


func _process(delta: float) -> void:
	if _done:
		return
	_t += delta
	if _t >= _total:
		skip()
		return
	_update(_t)


func _build_panel() -> void:
	_panel = ChamferedPanel.new()
	# 左侧竖长：贴左缘垂直居中（1920×1080 设计坐标）
	_panel.position = Vector2(24.0, (1080.0 - PANEL_SIZE.y) * 0.5)
	_panel.size = PANEL_SIZE
	_panel.bg_color = Color(0.02, 0.04, 0.08, 0.92)
	_panel.border_color = Color(UITheme.ACCENT, 0.7)
	_panel.bracket_color = UITheme.ACCENT
	_panel.brackets = true
	add_child(_panel)
	_stage = Node2D.new()
	_panel.add_child(_stage)
	# 抬头标题
	var title := UITheme.make_label(tr("MS_SEQ_TITLE"), UITheme.FONT_SMALL, UITheme.TEXT_DIM, HORIZONTAL_ALIGNMENT_LEFT)
	title.position = Vector2(20.0, 10.0)
	_panel.add_child(title)
	# 字幕
	_subtitle = UITheme.make_label("", UITheme.FONT_HUD_L, UITheme.TEXT, HORIZONTAL_ALIGNMENT_CENTER)
	_subtitle.position = Vector2(24.0, PANEL_SIZE.y - 58.0)
	_subtitle.size = Vector2(PANEL_SIZE.x - 48.0, 40.0)
	_panel.add_child(_subtitle)


## 机库剖面：深色内场 + 顶部滑轨 + 母舰剪影 + 充能管线 ×3 + 维护臂 ×2（竖向纵深构图）
func _build_hangar() -> void:
	var bg := ColorRect.new()
	bg.color = Color(0.012, 0.025, 0.05, 1.0)
	bg.position = Vector2(14.0, 40.0)
	bg.size = PANEL_SIZE - Vector2(28.0, 110.0)
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_stage.add_child(bg)
	# 后景龙门架剪影（深色结构层，增加纵深）
	var gantry_color := Color(0.03, 0.055, 0.1, 1.0)
	for gx in [110.0, 280.0, 450.0]:
		var strut := Polygon2D.new()
		strut.polygon = PackedVector2Array([
			Vector2(gx - 10.0, 70.0), Vector2(gx + 10.0, 70.0),
			Vector2(gx + 16.0, 700.0), Vector2(gx - 16.0, 700.0),
		])
		strut.color = gantry_color
		_stage.add_child(strut)
	var cross_beam := Polygon2D.new()
	cross_beam.polygon = PackedVector2Array([
		Vector2(40.0, 250.0), Vector2(520.0, 250.0), Vector2(520.0, 262.0), Vector2(40.0, 262.0),
	])
	cross_beam.color = gantry_color
	_stage.add_child(cross_beam)
	# 顶部滑轨（充能管线挂载点）
	for y in [58.0, 66.0]:
		var rail := Line2D.new()
		rail.width = 2.0
		rail.default_color = Color(UITheme.ACCENT, 0.25)
		rail.points = PackedVector2Array([Vector2(40.0, y), Vector2(PANEL_SIZE.x - 40.0, y)])
		_stage.add_child(rail)
	# 顶灯 ×5（软光点 + 淡光锥，沿滑轨排布）
	for lx in [90.0, 190.0, 290.0, 390.0, 490.0]:
		var lamp := CinematicFx.soft_glow(13.0, Color(1.0, 0.85, 0.55, 0.55))
		lamp.position = Vector2(lx, 62.0)
		_stage.add_child(lamp)
		var cone := Polygon2D.new()
		cone.polygon = PackedVector2Array([
			Vector2(lx - 8.0, 68.0), Vector2(lx + 8.0, 68.0),
			Vector2(lx + 42.0, 240.0), Vector2(lx - 42.0, 240.0),
		])
		cone.color = Color(1.0, 0.9, 0.6, 0.045)
		_stage.add_child(cone)
	# 机库地线
	var floor_line := Line2D.new()
	floor_line.width = 2.0
	floor_line.default_color = Color(UITheme.ACCENT, 0.2)
	floor_line.points = PackedVector2Array([Vector2(40.0, 700.0), Vector2(PANEL_SIZE.x - 40.0, 700.0)])
	_stage.add_child(floor_line)
	# 地面警示条纹（黄/暗相间小段，沿地线排布）
	for si in 13:
		var sx := 44.0 + 36.0 * float(si)
		var stripe := Polygon2D.new()
		stripe.polygon = PackedVector2Array([
			Vector2(sx, 704.0), Vector2(sx + 28.0, 704.0),
			Vector2(sx + 24.0, 712.0), Vector2(sx - 4.0, 712.0),
		])
		stripe.color = Color(0.85, 0.7, 0.15, 0.4) if si % 2 == 0 else Color(0.05, 0.07, 0.12, 0.9)
		_stage.add_child(stripe)
	# 充能管线 ×3（顶部 → 舰体挂点）+ 断开软火花与一次性喷发
	var attach := [Vector2(-35.0, -42.0), Vector2(0.0, -48.0), Vector2(35.0, -42.0)]
	for i in 3:
		var anchor := Vector2(180.0 + 100.0 * float(i), 62.0)
		var line := Line2D.new()
		line.width = 3.0
		line.default_color = CYAN
		line.points = PackedVector2Array([anchor, SHIP_HOME + attach[i]])
		_stage.add_child(line)
		_charge_lines.append(line)
		var spark := CinematicFx.soft_glow(16.0, Color(CYAN, 0.0))
		spark.position = SHIP_HOME + attach[i]
		_stage.add_child(spark)
		_charge_sparks.append(spark)
		var burst := CinematicFx.particles({
			"amount": 14, "lifetime": 0.45, "explosiveness": 1.0, "one_shot": true,
			"direction": Vector3(0.0, -1.0, 0.0), "spread": 70.0,
			"vel_min": 40.0, "vel_max": 130.0,
			"scale_min": 1.5, "scale_max": 3.5, "color": Color(0.7, 1.0, 0.95, 0.9),
		})
		burst.position = SHIP_HOME + attach[i]
		burst.emitting = false
		_stage.add_child(burst)
		_spark_bursts.append(burst)
	# 维护机械臂 ×2（基座/关节/末端三点折线，末端初始咬合舰体两侧）
	var arm_defs := [
		[Vector2(60.0, 700.0), Vector2(140.0, 600.0), SHIP_HOME + Vector2(-68.0, 22.0)],
		[Vector2(500.0, 700.0), Vector2(420.0, 600.0), SHIP_HOME + Vector2(68.0, 22.0)],
	]
	for pts in arm_defs:
		var arm := Line2D.new()
		arm.width = 6.0
		arm.default_color = Color(0.55, 0.65, 0.75)
		arm.points = PackedVector2Array(pts)
		_stage.add_child(arm)
		_arms.append(arm)
	# 母舰剪影（缩略比例：机库全景中的小舰体）
	_ship = Sprite2D.new()
	_ship.texture = SHIP_TEXTURE
	_ship.scale = Vector2(0.32, 0.32)
	_ship.position = SHIP_HOME
	_stage.add_child(_ship)
	# 弹射拖尾（镜头 3 启用）
	_ship_trail = Line2D.new()
	_ship_trail.width = 6.0
	var grad := Gradient.new()
	grad.set_color(0, Color(WARP_BLUE, 0.0))
	grad.set_color(1, Color(WARP_BLUE, 0.8))
	_ship_trail.gradient = grad
	_ship_trail.points = PackedVector2Array([Vector2.ZERO, Vector2.ZERO])  # C28：预分配，帧内只写元素
	_stage.add_child(_ship_trail)
	# 拖首软光（镜头 3 随弹射点亮，贴在舰尾）
	_ship_glow = CinematicFx.soft_glow(30.0, Color(WARP_BLUE, 0.0))
	_ship_glow.position = SHIP_HOME + Vector2(0.0, 26.0)
	_stage.add_child(_ship_glow)
	# 穿梭器光环（镜头 3 点亮）
	_warp_ring = Line2D.new()
	_warp_ring.width = 3.0
	_warp_ring.closed = true
	_warp_ring.default_color = Color(WARP_BLUE, 0.0)
	_warp_ring.points = _circle_points(1.0, 48)  # C28：预建单位点集，帧内仅写 scale
	_stage.add_child(_warp_ring)
	# 镜头 3 起步白闪
	_flash = ColorRect.new()
	_flash.color = Color(1.0, 1.0, 1.0, 0.0)
	_flash.position = Vector2(14.0, 40.0)
	_flash.size = PANEL_SIZE - Vector2(28.0, 110.0)
	_flash.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_stage.add_child(_flash)


## 进度驱动全部视觉（t 秒）
func _update(t: float) -> void:
	# 面板淡入/淡出
	var fade_in := clampf(t / OPEN_TIME, 0.0, 1.0)
	var fade_out := clampf((_total - t) / CLOSE_TIME, 0.0, 1.0)
	_panel.modulate.a = minf(fade_in, fade_out)
	# 镜头定位
	var st := t - OPEN_TIME
	var idx := 0
	while idx < _shot_durations.size() and st >= _shot_durations[idx]:
		st -= _shot_durations[idx]
		idx += 1
	if idx >= _shot_durations.size():
		return  # 收尾淡出段：保持末帧画面
	if idx != _shot_idx:
		_shot_idx = idx
		_subtitle.text = tr(SHOT_KEYS[idx])
		if idx == 2:
			GameState.play_sfx(GameState.SFX_DASH, -4.0, 0.6)
			_flash.color = Color(WARP_BLUE, 0.55)
			# 弹射起步冲击环（出仓点，一次性自毁）
			var launch_sw := CinematicFx.shockwave({
				"radius": 170.0, "time": 0.55,
				"color": Color(WARP_BLUE, 0.5), "core_color": Color(0.85, 0.95, 1.0, 0.9),
				"width": 9.0,
			})
			launch_sw.position = SHIP_HOME
			_stage.add_child(launch_sw)
	var p := clampf(st / _shot_durations[idx], 0.0, 1.0)
	match idx:
		0:
			_update_charge_lines(p)
		1:
			_update_arms(p)
		2:
			_update_launch(p)
	_flash.color.a = maxf(_flash.color.a - 2.2 * get_process_delta_time(), 0.0)


## 镜头 1：3 条充能管线依次断开（挂点回缩 + 软火花闪灭 + 断开瞬间喷发）
func _update_charge_lines(p: float) -> void:
	for i in 3:
		var at := 0.15 + 0.3 * float(i)
		var lp := clampf((p - at) / 0.25, 0.0, 1.0)
		var line := _charge_lines[i]
		var anchor: Vector2 = line.points[0]
		var tip: Vector2 = line.points[1]
		line.set_point_position(1, tip.lerp(anchor, lp * lp))
		line.default_color = Color(CYAN, 1.0 - 0.6 * lp)
		var spark := _charge_sparks[i]
		spark.modulate = Color(CYAN, 0.9 * sin(PI * lp)) if lp > 0.0 else Color(CYAN, 0.0)
		if lp > 0.0 and not _burst_fired[i]:
			_burst_fired[i] = true
			_spark_bursts[i].restart()


## 镜头 2：维护臂解除链接，末端+关节同步收回基座
func _update_arms(p: float) -> void:
	var e := 1.0 - (1.0 - p) * (1.0 - p)  # ease-out
	for arm in _arms:
		var base: Vector2 = arm.points[0]
		var joint: Vector2 = arm.points[1]
		var tip: Vector2 = arm.points[2]
		arm.set_point_position(2, tip.lerp(base, e))
		arm.set_point_position(1, joint.lerp(base, e * 0.6))
		arm.default_color = Color(0.55, 0.65, 0.75, 1.0 - 0.7 * e)


## 镜头 3：母舰加速弹出（向上出仓）+ 穿梭器蓝环扩散 + 拖首软光
func _update_launch(p: float) -> void:
	var e := p * p  # ease-in 加速
	_ship.position = SHIP_HOME + Vector2(0.0, -560.0 * e)
	# C28：点集已预分配，经 set_point_position 原地写（points[i]= 是值语义副本不生效）
	_ship_trail.set_point_position(0, _ship.position + Vector2(0.0, 26.0))
	_ship_trail.set_point_position(1, _ship.position + Vector2(0.0, 26.0 + 220.0 * e))
	_ship_trail.default_color = Color(WARP_BLUE, 0.8 * p)
	_ship_glow.position = _ship.position + Vector2(0.0, 26.0)
	_ship_glow.modulate = Color(WARP_BLUE, 0.75 * p)
	_ship_glow.scale = Vector2.ONE * (30.0 / 32.0) * (0.6 + 0.9 * e)
	var ring_p := clampf(p / 0.6, 0.0, 1.0)
	_layout_warp_ring(lerpf(20.0, 200.0, ring_p))
	_warp_ring.position = _ship.position
	_warp_ring.default_color = Color(WARP_BLUE, 0.9 * (1.0 - ring_p))


func _circle_points(radius: float, count: int) -> PackedVector2Array:
	var pts := PackedVector2Array()
	pts.resize(count)
	for i in count:
		var a := TAU * float(i) / float(count)
		pts[i] = Vector2(cos(a), sin(a)) * radius
	return pts


## C28：原地写穿梭器环点集（预分配数组 + set_point_position，零分配、线宽不随 scale 变）
func _layout_warp_ring(radius: float) -> void:
	for i in 48:
		var a := TAU * float(i) / 48.0
		_warp_ring.set_point_position(i, Vector2(cos(a), sin(a)) * radius)
