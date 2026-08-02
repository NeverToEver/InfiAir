class_name ReturnCinematic
extends CanvasLayer
## 返航过场导演：7 镜头时序串联、黑场转场、跳过与整树清理。
## 设计文档（单一事实源）：docs/RETURN_HOME_CINEMATIC.md §2 分镜表。
## 架构镜像 scripts/intro_cinematic.gd；无标题定格——镜头 7 渐暗停在全黑后直接走统一出口，
## 让基地 UI 在黑场下淡入。严禁 await create_timer 协程（退出时协程状态泄漏）。

signal finished

const TRANSITION := 0.3  # 镜头间黑场淡入淡出（含在各镜头时长内）
const OUTRO_FADE := 0.9  # 镜头 7 末尾渐暗到全黑（与闭眼重叠，BGM 同步淡出）
const PLAYER_SHIP: Texture2D = preload("res://assets/sprites/player_ship.png")

## 输入宽限：开播前 SKIP_GRACE 秒内忽略跳过（防实战中 WASD/Shift/Space 持续按键瞬间误触；
## 任意键/点击/Esc 路由统一收敛在 skip() 内受控；effects.return_skip_grace 可调）
var SKIP_GRACE := 1.2

## 每镜头时长（§2 分镜表；七镜头 11.8s = 总和，转场含在内）。测试可改短。
var _shot_durations: Array[float] = [1.6, 1.2, 1.4, 2.2, 1.8, 1.6, 2.0]

## 由 main 注入（_bgm_player 异步创建，可能为 null）：镜头 7 渐暗期淡出到 -40dB
var bgm_player: AudioStreamPlayer = null

var _shot_index := -1
var _current_shot: Node2D = null
var _shot_timer: Timer
var _done := false
var _start_msec := 0  # 开播真实时刻（输入宽限计时基准）
var _drift_t := 0.0  # 导演级手持漂移相位（共享容器，单 _process 零堆分配）
var _seamless_next := false  # 差异化转场：2→3 画面连续（端口内推，不黑场）
var _sub_tween: Tween = null  # 字幕淡入/淡出互斥
var _bgm_tween: Tween = null  # 镜头 7 BGM 淡出（skip 时 kill 并立即置目标音量）

@onready var _shot_root: Node2D = $ShotRoot
@onready var _fade: ColorRect = $Fade
@onready var _subtitle: Label = $Subtitle
@onready var _skip_hint: Label = $SkipHint


## A7：测试/诊断白盒断言经公开接口（过场镜头）
func set_shot_durations(durations: Array) -> void:
	_shot_durations = durations


func shot_index() -> int:
	return _shot_index


func current_shot() -> Node2D:
	return _current_shot


func shot_root() -> Node2D:
	return _shot_root


func subtitle() -> Label:
	return _subtitle


func _ready() -> void:
	SKIP_GRACE = GameState.cfg("effects.return_skip_grace", SKIP_GRACE)
	_start_msec = Time.get_ticks_msec()
	_skip_hint.text = tr("INTRO_SKIP")  # 跳过提示复用开场键
	_skip_hint.add_theme_font_override("font", UITheme.FONT)
	_subtitle.add_theme_font_override("font", UITheme.FONT)
	_shot_timer = Timer.new()
	_shot_timer.one_shot = true
	_shot_timer.timeout.connect(_on_shot_timeout)
	add_child(_shot_timer)
	# 首镜头延后到帧末启动：测试可在 add_child 同帧替换 _shot_durations
	_advance.call_deferred()


## 任意键/鼠标点击跳过；Esc（ui_cancel）放行给 BackNavigator 路由到 Main.skip_return()（公开接口，A7 后经 _skip_return 落地）
func _unhandled_input(event: InputEvent) -> void:
	if _done or event.is_action_pressed("ui_cancel"):
		return
	@warning_ignore("unsafe_property_access")
	var pressed_key: bool = event is InputEventKey and event.pressed and not event.echo
	@warning_ignore("unsafe_property_access")
	var pressed_click: bool = event is InputEventMouseButton and event.pressed
	if pressed_key or pressed_click:
		get_viewport().set_input_as_handled()
		skip()


## 导演级手持漂移：共享容器低频正弦位移/微旋转，零堆分配
func _process(delta: float) -> void:
	_drift_t += delta
	_shot_root.position.x = sin(_drift_t * 0.45) * 3.0
	_shot_root.position.y = cos(_drift_t * 0.38) * 2.5
	_shot_root.rotation = sin(_drift_t * 0.3) * 0.003


## 跳过（幂等）：与自然结束同一出口——停计时、kill 音频 tween 并置目标音量、
## 停在全黑画面、发 finished、整树 queue_free。
## 输入宽限期（开播前 SKIP_GRACE 秒）内直接忽略：任意键/点击与 Esc 路由都经此收敛。
## B15 修复：输入宽限只门控"输入跳过"，不门控"程序化自然结束"（_advance 走 _do_skip(true)，
## 否则未来压缩过场总时长 <SKIP_GRACE 时自然结束会被永久拦截）。
func skip() -> void:
	_do_skip(false)


func _do_skip(bypass_grace: bool) -> void:
	if _done:
		return
	if not bypass_grace and float(Time.get_ticks_msec() - _start_msec) / 1000.0 < SKIP_GRACE:
		return  # 输入宽限期内忽略跳过
	_done = true
	_shot_timer.stop()
	if _bgm_tween != null and _bgm_tween.is_valid():
		_bgm_tween.kill()
	if bgm_player != null:
		bgm_player.volume_db = -40.0
	_fade.color.a = 1.0  # 停留在全黑再发 finished（基地 UI 在黑场下淡入）
	finished.emit()
	queue_free()


func _advance() -> void:
	if _done:
		return
	if _current_shot != null:
		_current_shot.queue_free()
		_current_shot = null
	_shot_index += 1
	if _shot_index >= _shot_durations.size():
		_do_skip(true)  # 自然结束：绕过输入宽限（B15），无标题定格，渐暗已停在全黑，直接走统一出口
		return
	_current_shot = _build_shot(_shot_index)
	_shot_root.add_child(_current_shot)
	var dur: float = _shot_durations[_shot_index]
	_set_subtitle("RETURN_SUB_%d" % (_shot_index + 1))
	if _seamless_next:
		# 2→3 画面连续（端口内推）：不黑场
		_seamless_next = false
		_fade.color.a = 0.0
	else:
		# 黑场淡入（镜头 1 从全黑起，其余承接上一镜头的淡出）
		_fade.color.a = 1.0
		var fade_tween := create_tween()
		fade_tween.tween_property(_fade, "color:a", 0.0, minf(TRANSITION, dur * 0.5))
	_shot_timer.start(dur - _fade_out_time())


func _fade_out_time() -> float:
	var dur: float = _shot_durations[_shot_index]
	if _shot_index == _shot_durations.size() - 1:
		return minf(OUTRO_FADE, dur * 0.5)  # 镜头 7 末尾渐暗
	if _shot_index == 1:
		return 0.0  # 2→3 保持画面连续
	return minf(TRANSITION, dur * 0.5)


func _on_shot_timeout() -> void:
	if _done:
		return
	# 字幕随转场淡出
	if _sub_tween != null and _sub_tween.is_valid():
		_sub_tween.kill()
	_sub_tween = create_tween()
	var t := _fade_out_time()
	_sub_tween.tween_property(_subtitle, "modulate:a", 0.0, t)
	if _shot_index == 1:
		_seamless_next = true
		_advance()
		return
	var fade_tween := create_tween()
	fade_tween.tween_property(_fade, "color:a", 1.0, t)
	fade_tween.tween_callback(_advance)


## 叙事字幕卡：设置文本并淡入（淡出由 _on_shot_timeout 随转场处理）
func _set_subtitle(key: String) -> void:
	if _sub_tween != null and _sub_tween.is_valid():
		_sub_tween.kill()
	_subtitle.text = tr(key)
	_subtitle.modulate.a = 0.0
	_sub_tween = create_tween()
	_sub_tween.tween_property(_subtitle, "modulate:a", 1.0, 0.3)


func _build_shot(i: int) -> Node2D:
	match i:
		0:
			return _build_shot1()
		1:
			return _build_shot2()
		2:
			return _build_shot3()
		3:
			return _build_shot4()
		4:
			return _build_shot5()
		5:
			return _build_shot6()
		_:
			return _build_shot7()


# ---------------- 构图辅助（与 intro_cinematic.gd 同款，项目惯例直接复制） ----------------


class _GlowDot:
	extends Node2D
	var radius := 8.0
	var dot_color := Color.WHITE

	func _draw() -> void:
		draw_circle(Vector2.ZERO, radius, dot_color)


static func _glow(radius: float, color: Color, additive := true) -> Node2D:
	var dot := _GlowDot.new()
	dot.radius = radius
	dot.dot_color = color
	if additive:
		var mat := CanvasItemMaterial.new()
		mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
		dot.material = mat
	return dot


static func _rect_poly(w: float, h: float, color: Color) -> Polygon2D:
	var p := Polygon2D.new()
	p.polygon = PackedVector2Array(
		[
			Vector2(-w * 0.5, -h * 0.5),
			Vector2(w * 0.5, -h * 0.5),
			Vector2(w * 0.5, h * 0.5),
			Vector2(-w * 0.5, h * 0.5),
		]
	)
	p.color = color
	return p


static func _bg_rect(color: Color) -> ColorRect:
	var r := ColorRect.new()
	r.color = color
	r.position = Vector2.ZERO
	r.size = Vector2(1920.0, 1080.0)
	r.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return r


static func _line(points: PackedVector2Array, color: Color, width: float = 2.0) -> Line2D:
	var l := Line2D.new()
	l.points = points
	l.default_color = color
	l.width = width
	return l


## 引爆冲击颤动：随机方向脉冲偏移，0.27s 内衰减回基线（tween 驱动，不加 _process）。
static func _kick_shake(host: Node2D, amp: float, state: Array) -> void:
	if state[0] != null and (state[0] as Tween).is_valid():
		(state[0] as Tween).kill()
	var dir := Vector2(randf_range(-1.0, 1.0), randf_range(-1.0, 1.0))
	if dir.length_squared() < 0.01:
		dir = Vector2.RIGHT
	var st := host.create_tween()
	state[0] = st
	st.tween_property(host, "position", dir.normalized() * amp, 0.04).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
	st.tween_property(host, "position", dir.normalized() * -amp * 0.4, 0.08).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN_OUT)
	st.tween_property(host, "position", Vector2.ZERO, 0.15).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)


## 粒子工厂：全局委托 CinematicFx（同 dict 契约，默认挂软点贴图，scale 语义保持"像素直径"）
static func _particles(cfg: Dictionary) -> GPUParticles2D:
	return CinematicFx.particles(cfg)


# ---------------- 人物构件（复用开场镜头 3 多段式飞行服人物，改姿态/相位） ----------------


## 多段式飞行服驾驶员：骨盆/胸廓/头盔/维生背包/双关节四肢，面朝 +x。
## 返回 {node, hips[2], knees[2], shoulders[2], elbows[2], torso, eyelid}，
## 步行循环由镜头 _process 相位驱动，姿态关键帧直接写关节 rotation。
static func _build_person() -> Dictionary:
	var body_color := Color(0.24, 0.3, 0.4)  # 近侧肢体
	var far_color := Color(0.14, 0.18, 0.26)  # 远侧肢体（深度层次）
	var edge_color := Color(0.55, 0.66, 0.84, 0.7)  # 分件边缘线
	var person := Node2D.new()
	person.name = "Person"
	var hips: Array[Node2D] = []
	var knees: Array[Node2D] = []
	var shoulders: Array[Node2D] = []
	var elbows: Array[Node2D] = []
	# 腿部（远侧先画）：髋→大腿→膝→小腿→飞行靴
	for side_i in [1, 0]:
		var c := body_color if side_i == 0 else far_color
		var hip := Node2D.new()
		hip.position = Vector2(2.0 - 4.0 * side_i, -4.0 + 2.0 * side_i)
		person.add_child(hip)
		var thigh := _rect_poly(6.5, 22.0, c)
		thigh.position = Vector2(0.0, 11.0)
		hip.add_child(thigh)
		thigh.add_child(_line(PackedVector2Array([Vector2(2.6, -9.0), Vector2(2.6, 9.0)]), edge_color, 1.2))
		var knee := Node2D.new()
		knee.position = Vector2(0.0, 22.0)
		hip.add_child(knee)
		var shin := _rect_poly(5.0, 20.0, c)
		shin.position = Vector2(0.0, 10.0)
		knee.add_child(shin)
		var boot := Polygon2D.new()
		boot.polygon = PackedVector2Array([Vector2(-4.0, 16.0), Vector2(7.0, 16.0), Vector2(10.0, 22.0), Vector2(-4.0, 22.0)])
		boot.color = c
		knee.add_child(boot)
		knee.add_child(_line(PackedVector2Array([Vector2(-4.0, 22.5), Vector2(10.0, 22.5)]), edge_color, 1.6))
		hips.append(hip)
		knees.append(knee)
	# 躯干组（胸廓/背包/头盔/手臂随体倾斜）
	var torso_grp := Node2D.new()
	person.add_child(torso_grp)
	var pelvis := Polygon2D.new()
	pelvis.polygon = PackedVector2Array([Vector2(-9.0, -2.0), Vector2(7.0, -4.0), Vector2(9.0, -14.0), Vector2(-7.0, -14.0)])
	pelvis.color = body_color
	torso_grp.add_child(pelvis)
	# 生命维持背包 + 顶部管线 + 青色指示灯
	var backpack := _rect_poly(12.0, 24.0, far_color)
	backpack.position = Vector2(-11.0, -30.0)
	torso_grp.add_child(backpack)
	torso_grp.add_child(_line(PackedVector2Array([Vector2(-11.0, -44.0), Vector2(-11.0, -50.0), Vector2(2.0, -54.0)]), edge_color, 1.4))
	var pack_light := _glow(2.0, Color(0.0, 0.83, 1.0, 0.8))
	pack_light.position = Vector2(-13.0, -24.0)
	torso_grp.add_child(pack_light)
	# 胸廓 + 胸包 + 肩部护甲
	var chest := Polygon2D.new()
	chest.polygon = PackedVector2Array([Vector2(-7.0, -14.0), Vector2(13.0, -16.0), Vector2(17.0, -42.0), Vector2(-3.0, -46.0)])
	chest.color = body_color
	torso_grp.add_child(chest)
	torso_grp.add_child(_line(PackedVector2Array([Vector2(13.0, -16.0), Vector2(17.0, -42.0)]), edge_color, 1.6))
	var chest_pack := _rect_poly(6.0, 9.0, Color(0.3, 0.38, 0.5))
	chest_pack.position = Vector2(11.0, -28.0)
	torso_grp.add_child(chest_pack)
	var shoulder_pad := Polygon2D.new()
	shoulder_pad.polygon = PackedVector2Array([Vector2(-4.0, -52.0), Vector2(10.0, -54.0), Vector2(12.0, -44.0), Vector2(-2.0, -43.0)])
	shoulder_pad.color = Color(0.22, 0.28, 0.38)
	torso_grp.add_child(shoulder_pad)
	# 颈 + 头盔（面罩高光改冷青——返航无暖色）
	var neck := _rect_poly(4.0, 7.0, body_color)
	neck.position = Vector2(8.0, -51.0)
	torso_grp.add_child(neck)
	var helmet := _GlowDot.new()
	helmet.radius = 10.5
	helmet.dot_color = body_color
	helmet.position = Vector2(11.0, -62.0)
	torso_grp.add_child(helmet)
	var visor := _glow(3.5, Color(0.5, 0.9, 1.0, 0.8))
	visor.position = Vector2(19.0, -64.0)
	torso_grp.add_child(visor)
	# 眼睑：面部特写闭合用（初始 scale.y=0 藏于头盔上缘，特写时 0.8s 放下盖住面罩）
	var eyelid := _rect_poly(9.0, 7.0, Color(0.1, 0.13, 0.2))
	eyelid.position = Vector2(17.5, -67.5)
	eyelid.scale = Vector2(1.0, 0.0)
	torso_grp.add_child(eyelid)
	# 手臂（远侧先画）：肩→上臂→肘→前臂→手
	for side_i in [1, 0]:
		var c := body_color if side_i == 0 else far_color
		var shoulder := Node2D.new()
		shoulder.position = Vector2(8.0 - 6.0 * side_i, -42.0 + 2.0 * side_i)
		torso_grp.add_child(shoulder)
		var upper := _rect_poly(5.0, 16.0, c)
		upper.position = Vector2(0.0, 8.0)
		shoulder.add_child(upper)
		var elbow := Node2D.new()
		elbow.position = Vector2(0.0, 16.0)
		shoulder.add_child(elbow)
		var forearm := _rect_poly(4.5, 15.0, c)
		forearm.position = Vector2(0.0, 7.5)
		elbow.add_child(forearm)
		forearm.add_child(_line(PackedVector2Array([Vector2(1.8, -6.0), Vector2(1.8, 6.0)]), edge_color, 1.2))
		var hand := _GlowDot.new()
		hand.radius = 4.0
		hand.dot_color = c
		hand.position = Vector2(0.0, 16.0)
		elbow.add_child(hand)
		shoulders.append(shoulder)
		elbows.append(elbow)
	return {
		"node": person,
		"hips": hips,
		"knees": knees,
		"shoulders": shoulders,
		"elbows": elbows,
		"torso": torso_grp,
		"eyelid": eyelid,
	}


## 直立姿态（步行/站立基准）
static func _pose_stand(p: Dictionary) -> void:
	for i in 2:
		(p["hips"][i] as Node2D).rotation = 0.0
		(p["knees"][i] as Node2D).rotation = 0.05
		(p["shoulders"][i] as Node2D).rotation = 0.1
		(p["elbows"][i] as Node2D).rotation = -0.3
	(p["torso"] as Node2D).rotation = 0.0


## 坐姿：大腿前抬、小腿下垂（坐在休眠床沿）
static func _pose_sit(p: Dictionary) -> void:
	for i in 2:
		(p["hips"][i] as Node2D).rotation = -1.5
		(p["knees"][i] as Node2D).rotation = 1.4
		(p["shoulders"][i] as Node2D).rotation = 0.2
		(p["elbows"][i] as Node2D).rotation = -0.8
	(p["torso"] as Node2D).rotation = 0.1


## 平躺姿态：四肢舒展微放松（整体旋转由镜头另控）
static func _pose_lie(p: Dictionary) -> void:
	for i in 2:
		(p["hips"][i] as Node2D).rotation = 0.1
		(p["knees"][i] as Node2D).rotation = 0.12
		(p["shoulders"][i] as Node2D).rotation = 0.15
		(p["elbows"][i] as Node2D).rotation = -0.2
	(p["torso"] as Node2D).rotation = 0.0


## 一次性触发 Timer（随镜头节点销毁，跳过/切镜不残留迟发回调）
static func _once(parent: Node, wait: float, cb: Callable) -> void:
	var t := Timer.new()
	t.one_shot = true
	t.wait_time = maxf(wait, 0.05)
	t.autostart = true
	parent.add_child(t)
	t.timeout.connect(cb)


# ---------------- 镜头 1：曲率充能（2.4s） ----------------
## 深空战机悬停画面中心偏下；喷口辉光暗红 (0.5,0.1,0.05) → 炽白 (1,0.95,0.85) 并放大 1→1.8；
## 机身周围能量粒子向内收束（负速度）；结尾 0.4s 同心细环微缩脉动（空间扭曲前兆）。
func _build_shot1() -> Node2D:
	var dur: float = _shot_durations[0]
	var root := Node2D.new()
	root.name = "Shot1"
	var starfield := Starfield.new()
	starfield.warp(12.0)  # 承接对局 _starfield.warp(18.0) 的星光拉伸（自身 lerp 衰减回 1）
	root.add_child(starfield)
	var neb1 := _glow(480.0, Color(0.08, 0.18, 0.4, 0.05))
	neb1.position = Vector2(360.0, 800.0)
	root.add_child(neb1)
	var neb2 := _glow(420.0, Color(0.1, 0.3, 0.45, 0.05))
	neb2.position = Vector2(1520.0, 280.0)
	root.add_child(neb2)
	# 战机尾部视角悬停（同开场镜头 5 摆位）
	var ship := Sprite2D.new()
	ship.texture = PLAYER_SHIP
	ship.scale = Vector2.ONE * 1.4
	ship.position = Vector2(960.0, 560.0)
	root.add_child(ship)
	# 喷口充能：双层软辉光暗红 → 炽白放大
	for side in [-46.0, 46.0]:
		var halo := CinematicFx.soft_glow(42.0, Color(0.9, 0.4, 0.15, 0.2))
		halo.position = Vector2(960.0 + side, 648.0)
		root.add_child(halo)
		var nozzle := CinematicFx.soft_glow(15.0, Color(1.0, 1.0, 1.0, 0.9))
		nozzle.position = Vector2(960.0 + side, 644.0)
		nozzle.modulate = Color(0.5, 0.1, 0.05)
		root.add_child(nozzle)
		var nozzle_base := nozzle.scale
		var halo_base := halo.scale
		var charge := root.create_tween().set_parallel(true)
		charge.tween_property(nozzle, "modulate", Color(1.0, 0.95, 0.85), dur * 0.8)
		charge.tween_property(nozzle, "scale", nozzle_base * 1.8, dur * 0.8)
		charge.tween_property(halo, "scale", halo_base * 1.5, dur * 0.8)
	# 能量粒子向内收束（负速度朝发射点汇聚）
	var inbound := _particles(
		{
			"amount": 48,
			"lifetime": 1.2,
			"direction": Vector3(0.0, 1.0, 0.0),
			"spread": 180.0,
			"vel_min": -70.0,
			"vel_max": -25.0,
			"scale_min": 1.5,
			"scale_max": 3.0,
			"color": Color(0.6, 0.85, 1.0, 0.5),
		}
	)
	inbound.position = Vector2(960.0, 580.0)
	root.add_child(inbound)
	# 充能峰值（结尾 0.4s）：两道错位同心冲击环掠过观察者（曲率引擎点火感）
	_once(
		root,
		dur - 0.4,
		func() -> void:
			var wave1 := (
				CinematicFx
				. shockwave(
					{
						"radius": 700.0,
						"time": 0.5,
						"ry_ratio": 0.6,
						"color": Color(0.0, 0.83, 1.0, 0.35),
						"core_color": Color(0.7, 0.97, 1.0, 0.7),
						"width": 12.0,
					}
				)
			)
			wave1.position = Vector2(960.0, 570.0)
			root.add_child(wave1)
			_once(
				root,
				0.14,
				func() -> void:
					var wave2 := (
						CinematicFx
						. shockwave(
							{
								"radius": 900.0,
								"time": 0.5,
								"ry_ratio": 0.6,
								"color": Color(0.0, 0.83, 1.0, 0.3),
								"core_color": Color(0.6, 0.95, 1.0, 0.6),
								"width": 10.0,
							}
						)
					)
					wave2.position = Vector2(960.0, 570.0)
					root.add_child(wave2)
			)
	)
	# 结尾 0.4s：画面中心同心细环微缩脉动（空间扭曲）
	for k in 3:
		var ring_points := PackedVector2Array()
		for i in 40:
			var a := TAU * float(i) / 40.0
			ring_points.append(Vector2(cos(a), sin(a)) * (40.0 + 30.0 * k))
		var ring := _line(ring_points, Color(0.6, 0.9, 1.0, 0.0), 2.0)
		ring.closed = true
		ring.position = Vector2(960.0, 480.0)
		var ring_mat := CanvasItemMaterial.new()
		ring_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
		ring.material = ring_mat
		root.add_child(ring)
		var warp_in := root.create_tween()
		warp_in.tween_interval(dur - 0.4 + 0.08 * k)
		warp_in.tween_property(ring, "modulate:a", 0.55, 0.15)
		warp_in.parallel().tween_property(ring, "scale", Vector2.ONE * 0.85, 0.25)
	GameState.play_sfx(GameState.SFX_DASH, -6.0, 0.6)  # 0.6 倍速拉长为充能上升感
	return root


# ---------------- 镜头 2：传送端口撕裂（1.6s） ----------------


class _PortalShot:
	extends Node2D
	var _dots: Array[Node2D] = []  # 环缘能量翻涌：12 个小 glow 点沿椭圆游走
	var _center := Vector2.ZERO
	var _rx := 130.0
	var _ry := 240.0
	var _inner_station: Node2D = null  # 环内虚影站模糊景象（水平弥散抖动）
	var _inner_base_x := 0.0
	var _swirls: Array[Line2D] = []  # 环内旋涡弧 ×2（反向旋转，空间搅动感）
	var _swirl_speeds := PackedFloat32Array()
	var _t := 0.0

	func _process(delta: float) -> void:
		_t += delta
		for i in _dots.size():
			var a := _t * 2.5 + TAU * float(i) / float(_dots.size())
			_dots[i].position = _center + Vector2(cos(a) * _rx, sin(a) * _ry)
		for i in _swirls.size():
			_swirls[i].rotation += _swirl_speeds[i] * delta
		if _inner_station != null:
			_inner_station.position.x = _inner_base_x + sin(_t * 25.0) * 2.0


## 竖直长圆环从一点撕开扩到全尺寸（0.5s），环缘 12 个 glow 点快速游走；
## 环内：口腔软辉光垫 + 双旋涡弧反向搅动 + 环缘粒子内流（环形发射域 + 负径向加速度）；
## 环内显现虚影站模糊景象（站体 α0.1 + 水平弥散抖动）；镜头稍推 1.0→1.06。
func _build_shot2() -> Node2D:
	var dur: float = _shot_durations[1]
	var root := _PortalShot.new()
	root.name = "Shot2"
	root.add_child(Starfield.new())
	var push := Node2D.new()  # 推镜容器
	root.add_child(push)
	var push_tween := root.create_tween()
	push_tween.tween_property(push, "scale", Vector2.ONE * 1.06, dur).set_trans(Tween.TRANS_SINE)
	var center := Vector2(1150.0, 430.0)
	root._center = center
	# 端口口腔软辉光垫（椭圆压扁，垫在虚影站之下，随撕裂同步张开）
	var mouth := CinematicFx.soft_glow(32.0, Color(0.0, 0.7, 1.0, 0.15))
	mouth.position = center
	var mouth_scale := Vector2(root._rx * 0.95 / 32.0, root._ry * 0.95 / 32.0)
	mouth.scale = mouth_scale * 0.02
	push.add_child(mouth)
	# 内部景象先露（环张开时透出）：虚影站极简版（小比例 + 低 alpha + 弥散抖动）
	var inner := DawnStation.build(DawnStation.Mode.PHANTOM)
	inner.scale = Vector2.ONE * 0.28
	inner.position = center
	inner.modulate.a = 0.35
	push.add_child(inner)
	root._inner_station = inner
	root._inner_base_x = center.x
	# 端口环：亮芯 + 叠加态外晕，从一点撕开（scale 0.02 → 1，0.5s）
	var ring_points := PackedVector2Array()
	for i in 64:
		var a := TAU * float(i) / 64.0
		ring_points.append(Vector2(cos(a) * root._rx, sin(a) * root._ry))
	var ring_glow := _line(ring_points, Color(0.0, 0.83, 1.0, 0.25), 12.0)
	ring_glow.closed = true
	ring_glow.position = center
	var glow_mat := CanvasItemMaterial.new()
	glow_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	ring_glow.material = glow_mat
	push.add_child(ring_glow)
	var ring := _line(ring_points, Color(0.0, 0.9, 1.0, 0.9), 4.0)
	ring.closed = true
	ring.position = center
	push.add_child(ring)
	for r in [ring_glow, ring]:
		r.scale = Vector2.ONE * 0.02
	# 环内旋涡弧 ×2（各约 200° 弧段，反向旋转，随撕裂同步张开；_process 仅累加 rotation）
	for k in 2:
		var rr := 0.55 + 0.25 * float(k)
		var swirl_points := PackedVector2Array()
		for i in 28:
			var a := -TAU * 100.0 / 360.0 + TAU * 200.0 / 360.0 * float(i) / 27.0
			swirl_points.append(Vector2(cos(a) * root._rx * rr, sin(a) * root._ry * rr))
		var swirl := _line(swirl_points, Color(0.3, 0.9, 1.0, 0.3), 3.0)
		swirl.position = center
		swirl.scale = Vector2.ONE * 0.02
		var swirl_mat := CanvasItemMaterial.new()
		swirl_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
		swirl.material = swirl_mat
		push.add_child(swirl)
		root._swirls.append(swirl)
		root._swirl_speeds.append(2.4 if k == 0 else -1.7)
	var tear := root.create_tween().set_parallel(true)
	tear.tween_property(ring, "scale", Vector2.ONE, 0.5).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
	tear.tween_property(ring_glow, "scale", Vector2.ONE, 0.5).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
	tear.tween_property(mouth, "scale", mouth_scale, 0.5).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
	for swirl in root._swirls:
		tear.tween_property(swirl, "scale", Vector2.ONE, 0.5).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
	# 环缘能量翻涌：12 个小 glow 点（本镜头唯一 _process 逐帧换位）
	for i in 12:
		var dot := _glow(3.5, Color(0.5, 0.95, 1.0, 0.9))
		push.add_child(dot)
		root._dots.append(dot)
	# 环缘内流粒子：环形发射域（参考 DawnStation 数据流配方）+ 负径向加速度拉向环心；
	# 节点 y 压扁成椭圆贴合端口；撕裂近完成（0.45s）才开始发射
	var inflow := _particles(
		{
			"amount": 40,
			"lifetime": 0.9,
			"direction": Vector3(0.0, -1.0, 0.0),
			"spread": 180.0,
			"vel_min": 0.0,
			"vel_max": 15.0,
			"scale_min": 2.0,
			"scale_max": 4.0,
			"color": Color(0.5, 0.9, 1.0, 0.45),
		}
	)
	var inflow_mat := inflow.process_material as ParticleProcessMaterial
	inflow_mat.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_RING
	inflow_mat.emission_ring_axis = Vector3(0.0, 0.0, 1.0)
	inflow_mat.emission_ring_radius = root._rx
	inflow_mat.emission_ring_inner_radius = root._rx * 0.8
	inflow_mat.emission_ring_height = 0.0
	inflow_mat.radial_accel_min = -140.0
	inflow_mat.radial_accel_max = -90.0
	inflow.position = center
	inflow.scale = Vector2(1.0, root._ry / root._rx)
	inflow.emitting = false
	push.add_child(inflow)
	_once(root, 0.45, func() -> void: inflow.emitting = true)
	GameState.play_sfx(GameState.SFX_EXPLOSION, -12.0, 0.5)  # 0.5 倍速低沉撕裂感
	return root


# ---------------- 镜头 3：跃迁匹配剪辑（2.0s） ----------------
## 前半原星域：战机加速冲入端口（scale 1→0.2 入环心），端口急缩成一点 → 白闪 0.10s
## → 后半虚影站星域：同一位置端口再度张开，战机减速飞出（scale 0.2→1，ease_out），
## 端口闭合消散。飞行方向两段保持一致（向右上）；远景先露远处虚影站剪影为镜头 4 铺垫。
func _build_shot3() -> Node2D:
	var dur: float = _shot_durations[2]
	var u := dur / 2.0  # 内部关键帧按基准时长等比缩放（测试短时长表兼容）
	var root := Node2D.new()
	root.name = "Shot3"
	var portal_pos := Vector2(1180.0, 400.0)
	# ---- 前半：原星域冲入 ----
	var part_a := Node2D.new()
	root.add_child(part_a)
	part_a.add_child(Starfield.new())
	# 跃迁隧道放射条纹（以端口为中心，白闪切镜时随 part_a 隐藏）
	var streaks := (
		CinematicFx
		. radial_streaks(
			{
				"count": 26,
				"max_radius": 1000.0,
				"color": Color(0.5, 0.85, 1.0, 0.45),
				"cycle": 1.0,
			}
		)
	)
	streaks.position = portal_pos
	streaks.modulate.a = 0.0
	part_a.add_child(streaks)
	var streaks_in := root.create_tween()
	streaks_in.tween_property(streaks, "modulate:a", 1.0, 0.3 * u)
	var ring_a_points := PackedVector2Array()
	for i in 48:
		var a := TAU * float(i) / 48.0
		ring_a_points.append(Vector2(cos(a) * 90.0, sin(a) * 150.0))
	var ring_a := _line(ring_a_points, Color(0.0, 0.9, 1.0, 0.9), 4.0)
	ring_a.closed = true
	ring_a.position = portal_pos
	part_a.add_child(ring_a)
	var ship_a := Sprite2D.new()
	ship_a.texture = PLAYER_SHIP
	ship_a.position = Vector2(700.0, 720.0)
	ship_a.rotation = (portal_pos - ship_a.position).angle() + PI * 0.5  # 贴图机头朝上，+PI/2 对准航向
	part_a.add_child(ship_a)
	var flame_a := _glow(18.0, Color(1.0, 0.95, 0.85, 0.9))
	flame_a.position = Vector2(-30.0, 0.0)  # 机尾（局部 -x）
	ship_a.add_child(flame_a)
	var dive := root.create_tween().set_parallel(true)
	dive.tween_property(ship_a, "position", portal_pos, 0.8 * u).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)
	dive.tween_property(ship_a, "scale", Vector2.ONE * 0.2, 0.8 * u).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)
	dive.tween_property(flame_a, "scale", Vector2.ONE * 2.0, 0.5 * u)  # 尾焰骤亮
	# 端口急缩成一点（白闪前）
	var close_a := root.create_tween()
	close_a.tween_interval(0.82 * u)
	close_a.tween_property(ring_a, "scale", Vector2.ONE * 0.02, 0.08 * u)
	# ---- 后半：虚影站星域飞出（初始隐藏，白闪后揭示） ----
	var part_b := Node2D.new()
	part_b.visible = false
	root.add_child(part_b)
	part_b.add_child(Starfield.new())
	var neb := _glow(420.0, Color(0.08, 0.2, 0.45, 0.06))
	neb.position = Vector2(420.0, 780.0)
	part_b.add_child(neb)
	# 远处虚影站剪影（α0.15，为镜头 4 铺垫）
	var far_station := DawnStation.build(DawnStation.Mode.PHANTOM)
	far_station.scale = Vector2.ONE * 0.3
	far_station.position = Vector2(1560.0, 300.0)
	far_station.modulate.a = 0.5
	part_b.add_child(far_station)
	var ring_b := _line(ring_a_points, Color(0.0, 0.9, 1.0, 0.9), 4.0)
	ring_b.closed = true
	ring_b.position = portal_pos
	ring_b.scale = Vector2.ONE * 0.02
	part_b.add_child(ring_b)
	var ship_b := Sprite2D.new()
	ship_b.texture = PLAYER_SHIP
	ship_b.scale = Vector2.ONE * 0.2
	ship_b.position = portal_pos
	ship_b.rotation = (Vector2(1520.0, 230.0) - portal_pos).angle() + PI * 0.5
	part_b.add_child(ship_b)
	# 减速拖短粒子尾迹（挂在机尾随 tween 同行）
	var trail := _particles(
		{
			"amount": 40,
			"lifetime": 0.35,
			"direction": Vector3(-1.0, 0.4, 0.0),
			"spread": 20.0,
			"vel_min": 120.0,
			"vel_max": 220.0,
			"scale_min": 2.0,
			"scale_max": 4.0,
			"color": Color(0.6, 0.9, 1.0, 0.7),
		}
	)
	trail.position = Vector2(-26.0, 0.0)
	ship_b.add_child(trail)
	# 白闪转场件（镜头内部，复用开场 1→2 差异化白闪）
	var flash := _bg_rect(Color(1.0, 1.0, 1.0, 0.0))
	root.add_child(flash)
	_once(
		root,
		0.9 * u,
		func() -> void:
			part_a.visible = false
			part_b.visible = true
			# 白闪瞬间端口中心扩散一道冲击环（跃迁能量释放）
			var wave := (
				CinematicFx
				. shockwave(
					{
						"radius": 520.0,
						"time": 0.4,
						"ry_ratio": 0.7,
						"color": Color(0.6, 0.95, 1.0, 0.5),
						"core_color": Color(1.0, 1.0, 1.0, 0.85),
						"width": 14.0,
					}
				)
			)
			wave.position = portal_pos
			root.add_child(wave)
			var ft := root.create_tween()
			ft.tween_property(flash, "color:a", 1.0, 0.05)
			ft.tween_property(flash, "color:a", 0.0, 0.25)
			GameState.play_sfx(GameState.SFX_DASH)  # 白闪瞬间正常速
			var emerge := root.create_tween().set_parallel(true)
			emerge.tween_property(ring_b, "scale", Vector2.ONE, 0.2 * u).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
			emerge.tween_property(ship_b, "scale", Vector2.ONE, 0.7 * u).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
			emerge.tween_property(ship_b, "position", Vector2(1520.0, 230.0), 0.7 * u).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
			var dissolve := root.create_tween()
			dissolve.tween_interval(0.7 * u)
			dissolve.tween_property(ring_b, "modulate:a", 0.0, 0.2 * u)  # 端口闭合消散
	)
	_once(root, 1.2 * u, func() -> void: GameState.play_sfx(GameState.SFX_DASH, -10.0))  # 飞出段尾音
	return root


# ---------------- 镜头 4：虚影站全貌 + 捕获轨道（3.0s） ----------------


class _CaptureShot:
	extends Node2D
	var _samples: PackedVector2Array = PackedVector2Array()  # 捕获轨道弧采样（构建期预计算，供战机定位）
	var _ship: Node2D = null
	var _ship_u := 1.0  # 战机沿轨位置参数（1=远端 → 0=站体端，tween 驱动）
	var _lights: Array[Sprite2D] = []  # 站体环缘航行灯 ×8（慢速追逐明灭）
	var _t := 0.0

	func _sample_at(t: float) -> Vector2:
		var f: float = clampf(t, 0.0, 1.0) * float(_samples.size() - 1)
		var i := int(f)
		if i >= _samples.size() - 1:
			return _samples[_samples.size() - 1]
		return _samples[i].lerp(_samples[i + 1], f - float(i))

	func _process(delta: float) -> void:
		_t += delta
		# 航行灯慢追逐：相位绕环依次点亮（alpha 窄脉冲 + 低常亮底）
		for i in _lights.size():
			var w := sin(_t * 1.5 - TAU * float(i) / 8.0)
			_lights[i].modulate.a = 0.12 + 0.78 * pow(maxf(w, 0.0), 4.0)
		if _ship != null:
			_ship.position = _sample_at(_ship_u)
			var ahead := _sample_at(maxf(_ship_u - 0.02, 0.0))
			# 贴图机头朝 +y 上（player.gd 同款：rotation = 航向角 + PI/2）
			_ship.rotation = (ahead - _ship.position).angle() + PI * 0.5


## 虚影站「曙光·残响」全貌首次完整亮相（§1.1 四层虚影全开）；
## 半透明能量捕获轨道（CinematicFx.beam 分层能量束：辉光层 + 亮芯层 + 3 个循环流光软点）牵引战机滑向停机坪入口；
## 站体环缘 8 盏航行灯慢速追逐明灭；镜头缓慢侧跟（正弦平移 60px + scale 1.0→1.12 缓推）。
func _build_shot4() -> Node2D:
	var dur: float = _shot_durations[3]
	var root := _CaptureShot.new()
	root.name = "Shot4"
	root.add_child(Starfield.new())
	var cam := Node2D.new()  # 侧跟推镜容器
	root.add_child(cam)
	var cam_tween := root.create_tween().set_parallel(true)
	cam_tween.tween_property(cam, "scale", Vector2.ONE * 1.12, dur).set_trans(Tween.TRANS_SINE)
	cam_tween.tween_property(cam, "position", Vector2(-60.0, 0.0), dur).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	# 虚影站全貌（与开场镜头 1 同位同构）
	var station := DawnStation.build(DawnStation.Mode.PHANTOM)
	station.position = Vector2(960.0, 470.0)
	cam.add_child(station)
	# 站体环缘航行灯 ×8（慢速追逐明灭，预建后由 _CaptureShot 相位驱动 alpha）
	for i in 8:
		var a := TAU * float(i) / 8.0
		var lamp := CinematicFx.soft_glow(5.0, Color(0.5, 0.95, 1.0, 1.0))
		lamp.position = Vector2(960.0, 470.0) + Vector2(cos(a), sin(a)) * (DawnStation.RING_RADIUS + 4.0)
		lamp.modulate.a = 0.12
		cam.add_child(lamp)
		root._lights.append(lamp)
	# 捕获轨道：站体边缘 → 战机的二次贝塞尔弧（构建期采样 24 点，_process 零分配）
	var p0 := Vector2(960.0, 470.0) + Vector2(cos(-0.2), sin(-0.2)) * DawnStation.RING_RADIUS
	var p2 := Vector2(1530.0, 660.0)
	var p1 := Vector2(1420.0, 470.0)
	for i in 24:
		var t := float(i) / 23.0
		root._samples.append(p0.lerp(p1, t).lerp(p1.lerp(p2, t), t))
	# 分层能量束（辉光层 + 亮芯层 + 3 个循环流光软点，内部零分配 _process）
	var beam := (
		CinematicFx
		. beam(
			root._samples,
			{
				"color": Color(0.0, 0.83, 1.0),
				"width": 14.0,
				"dot_count": 3,
				"dot_speed": 0.5,
				"dot_radius": 8.0,
				"dot_color": Color(0.6, 0.98, 1.0),
			}
		)
	)
	cam.add_child(beam)
	# 战机沿轨道弧线缓速滑向停机坪入口（TRANS_SINE 吸附感）
	var ship := Sprite2D.new()
	ship.texture = PLAYER_SHIP
	ship.scale = Vector2.ONE * 0.9
	cam.add_child(ship)
	root._ship = ship
	# 机尾短距拖尾（镜头 3 飞出段同款配方，减速滑行弱一档）
	var ship_trail := _particles(
		{
			"amount": 24,
			"lifetime": 0.35,
			"direction": Vector3(-1.0, 0.4, 0.0),
			"spread": 20.0,
			"vel_min": 100.0,
			"vel_max": 180.0,
			"scale_min": 2.0,
			"scale_max": 3.5,
			"color": Color(0.6, 0.9, 1.0, 0.55),
		}
	)
	ship_trail.position = Vector2(-26.0, 0.0)
	ship.add_child(ship_trail)
	var pull := root.create_tween()
	pull.tween_property(root, "_ship_u", 0.06, dur).set_trans(Tween.TRANS_SINE)
	GameState.play_sfx(GameState.SFX_RESUPPLY, -8.0)  # 对接感
	return root


# ---------------- 镜头 5：停机坪降落（1.8s） ----------------
## 战机沿引导灯带中线垂直下落 40px 降落（ease_out + 落地下压回弹 + 细尘）；引擎熄火
## （喷口辉光 0.3s 缩没）；座舱盖上翻 0.4s；主角跃下落地（弧线 0.5s + 落地微尘）。
## 固定机位低角度仰拍（机位元素整体下移 ~60px）。
## 舰人比例按现实战机/驾驶员锚定：舰可见长约 420px（scale 2.8）≈ 人物 64px（scale 0.55）的 6.6 倍。
func _build_shot5() -> Node2D:
	var dur: float = _shot_durations[4]
	var u := dur / 1.8
	var root := Node2D.new()
	root.name = "Shot5"
	root.add_child(_bg_rect(Color(0.01, 0.02, 0.05)))
	# 虚影站内部线框剖面（半透明青色调，沿用 X 光线框语言）
	for i in 3:
		var y := 380.0 + 140.0 * i
		root.add_child(_line(PackedVector2Array([Vector2(150.0, y), Vector2(1770.0, y)]), Color(0.0, 0.6, 1.0, 0.08)))
	for i in 7:
		var x := 150.0 + 270.0 * i
		root.add_child(_line(PackedVector2Array([Vector2(x, 340.0), Vector2(x, 860.0)]), Color(0.0, 0.6, 1.0, 0.06)))
	# 六边形甲板平台：深色实体底 + 青色发光边界 + 中线引导灯带（低角度：整体下移 60px）
	var deck := Polygon2D.new()
	deck.polygon = PackedVector2Array(
		[
			Vector2(-320.0, 0.0),
			Vector2(-220.0, -70.0),
			Vector2(220.0, -70.0),
			Vector2(320.0, 0.0),
			Vector2(220.0, 70.0),
			Vector2(-220.0, 70.0),
		]
	)
	deck.color = Color(0.05, 0.07, 0.10)
	deck.position = Vector2(960.0, 780.0)
	root.add_child(deck)
	var deck_edge := _line(deck.polygon, Color(0.0, 0.83, 1.0, 0.5), 2.0)
	deck_edge.closed = true
	var deck_edge_mat := CanvasItemMaterial.new()
	deck_edge_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	deck_edge.material = deck_edge_mat
	deck.add_child(deck_edge)
	# 中线引导灯带 ×8：初始压暗，降落窗口内由两侧向落点依次追逐点亮
	var guides: Array[Node2D] = []
	for i in 8:
		var guide := _glow(3.0, Color(0.0, 0.83, 1.0, 0.7))
		guide.position = Vector2(-210.0 + 60.0 * i, 0.0)
		guide.modulate.a = 0.2
		deck.add_child(guide)
		guides.append(guide)
	var chase_order := [0, 7, 1, 6, 2, 5, 3, 4]  # 由远及近指向甲板中心落点
	for j in 8:
		var g: Node2D = guides[chase_order[j]]
		var gt := root.create_tween()
		gt.tween_interval(0.05 * u + 0.075 * u * float(j))
		gt.tween_property(g, "modulate:a", 1.0, 0.08 * u)
		gt.tween_property(g, "modulate:a", 0.7, 0.3 * u)
	# 甲板尽头通道闸门
	var gate := _rect_poly(120.0, 160.0, Color(0.06, 0.08, 0.12))
	gate.position = Vector2(1450.0, 730.0)
	root.add_child(gate)
	var gate_frame := _line(
		PackedVector2Array(
			[
				Vector2(1390.0, 650.0),
				Vector2(1510.0, 650.0),
				Vector2(1510.0, 810.0),
				Vector2(1390.0, 810.0),
			]
		),
		Color(0.0, 0.83, 1.0, 0.35),
		2.0
	)
	gate_frame.closed = true
	root.add_child(gate_frame)
	# 战机（载体含机身/喷口/座舱盖）：scale 2.8，尾部落于甲板顶面（y=710）
	var ship := Node2D.new()
	ship.position = Vector2(960.0, 460.0)
	root.add_child(ship)
	var hull := Sprite2D.new()
	hull.texture = PLAYER_SHIP
	hull.scale = Vector2.ONE * 2.8
	ship.add_child(hull)
	var engine := _glow(20.0, Color(1.0, 0.95, 0.85, 0.9))
	engine.position = Vector2(0.0, 195.0)
	ship.add_child(engine)
	var canopy := Polygon2D.new()
	canopy.polygon = PackedVector2Array([Vector2(-20.0, 0.0), Vector2(20.0, 0.0), Vector2(12.0, -36.0), Vector2(-12.0, -36.0)])
	canopy.color = Color(0.15, 0.35, 0.5, 0.85)
	canopy.position = Vector2(0.0, -80.0)
	ship.add_child(canopy)
	# 主角（scale 0.55 ≈ 64px 高，初始藏于座舱）
	var person := _build_person()
	var pnode: Node2D = person["node"]
	pnode.scale = Vector2.ONE * 0.55
	pnode.position = Vector2(960.0, 425.0)
	pnode.visible = false
	_pose_stand(person)
	root.add_child(pnode)
	# 降落：垂直下落 40px（ease_out）→ 下压回弹 + 细尘 + 熄火 + 开舱 + 跃下
	var land := root.create_tween()
	land.tween_property(ship, "position:y", 500.0, 0.9 * u).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
	land.tween_property(ship, "scale:y", 0.94, 0.1 * u)
	land.tween_property(ship, "scale:y", 1.0, 0.12 * u)
	_once(
		root,
		0.9 * u,
		func() -> void:
			GameState.play_sfx(GameState.SFX_EXPLOSION, -18.0)  # 落地极轻闷响
			var dust := _particles(
				{
					"amount": 24,
					"lifetime": 0.6,
					"one_shot": true,
					"explosiveness": 0.9,
					"direction": Vector3(0.0, -1.0, 0.0),
					"spread": 140.0,
					"vel_min": 40.0,
					"vel_max": 110.0,
					"scale_min": 2.0,
					"scale_max": 5.0,
					"color": Color(0.5, 0.7, 0.9, 0.3),
				}
			)
			dust.position = Vector2(960.0, 705.0)
			root.add_child(dust)
			var off := root.create_tween()
			off.tween_property(engine, "scale", Vector2.ZERO, 0.3 * u)  # 引擎熄火
	)
	_once(
		root,
		1.0 * u,
		func() -> void:
			var open := root.create_tween()
			open.tween_property(canopy, "rotation", -1.3, 0.4 * u)  # 座舱盖上翻
			# 玻璃高光条随开舱滑过舱面（与上翻同程 0.4u）
			var shine := Polygon2D.new()
			shine.polygon = PackedVector2Array([Vector2(-2.0, -4.0), Vector2(4.0, -4.0), Vector2(-2.0, -32.0), Vector2(-8.0, -32.0)])
			shine.color = Color(1.0, 1.0, 1.0, 0.0)
			var shine_mat := CanvasItemMaterial.new()
			shine_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
			shine.material = shine_mat
			shine.position = Vector2(-10.0, 0.0)
			canopy.add_child(shine)
			var slide := root.create_tween()
			slide.tween_property(shine, "position:x", 12.0, 0.4 * u)
			var fade := root.create_tween()
			fade.tween_property(shine, "color:a", 0.4, 0.08 * u)
			fade.tween_property(shine, "color:a", 0.0, 0.12 * u).set_delay(0.26 * u)  # 滑到末段同步消隐
	)
	_once(
		root,
		1.15 * u,
		func() -> void:
			GameState.play_sfx(GameState.SFX_DASH, -14.0)  # 跃下短促音
			pnode.visible = true
			var jump := root.create_tween()
			jump.tween_property(pnode, "position", Vector2(935.0, 390.0), 0.25 * u).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
			jump.tween_property(pnode, "position", Vector2(905.0, 686.0), 0.25 * u).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)
			jump.tween_callback(
				func() -> void:
					# 落地微尘 + 屈膝缓冲后站直
					var land_dust := _particles(
						{
							"amount": 16,
							"lifetime": 0.5,
							"one_shot": true,
							"explosiveness": 0.9,
							"direction": Vector3(0.0, -1.0, 0.0),
							"spread": 150.0,
							"vel_min": 30.0,
							"vel_max": 80.0,
							"scale_min": 1.5,
							"scale_max": 3.5,
							"color": Color(0.5, 0.7, 0.9, 0.25),
						}
					)
					land_dust.position = Vector2(905.0, 706.0)
					root.add_child(land_dust)
					for i in 2:
						(person["knees"][i] as Node2D).rotation = 0.9
					var stand := root.create_tween()
					for i in 2:
						stand.parallel().tween_property(person["knees"][i], "rotation", 0.05, 0.3 * u)
			)
	)
	return root


# ---------------- 镜头 6：通道步行 + 舱门（1.6s） ----------------


class _WalkShot:
	extends Node2D
	var _world: Node2D  # 走廊世界容器（跟随主角 x 匀速平移，主角固定在画面左 1/3）
	var _person: Dictionary
	var _lights: Array[Line2D] = []  # 顶部感应灯带分段（随主角位置逐节点亮起，身后缓灭）
	var _light_x: Array[float] = []
	var _door_l: Polygon2D = null
	var _door_r: Polygon2D = null
	var _door_leak: ColorRect = null
	var _door_opened := false
	var _walking := true
	var _scrolled := 0.0
	var _stop_scroll := 140.0  # 走 ~1.6s（90px/s）抵达舱门前停步（构建时按镜头时长等比缩放）
	var _time_u := 1.0  # 内部关键帧时间缩放（舱门滑开时长随镜头时长压缩）
	var _phase := 0.0
	var _bob_base_y := 0.0

	func _process(delta: float) -> void:
		var person_node: Node2D = _person["node"]
		if _walking:
			_scrolled += 90.0 * delta
			_world.position.x = -_scrolled
			_phase += delta * 6.0  # 步行循环（奔跑构件降频、步幅减半）
			if _scrolled >= _stop_scroll:
				_walking = false
				_open_door()
		var person_world_x := 640.0 + _scrolled
		for i in _lights.size():
			var target := 0.08
			if _light_x[i] < person_world_x - 40.0:
				target = 0.85  # 脚下已过：亮起
			if _light_x[i] < person_world_x - 400.0:
				target = 0.15  # 身后 0.5s 级缓灭
			_lights[i].default_color.a = lerpf(_lights[i].default_color.a, target, 5.0 * delta)
		# 肢体相位驱动（零堆分配）；停步后缓回直立
		var k := 1.0 if _walking else 0.0
		for i in 2:
			var p: float = _phase + PI * float(i)
			var hip := _person["hips"][i] as Node2D
			var knee := _person["knees"][i] as Node2D
			var shoulder := _person["shoulders"][i] as Node2D
			var elbow := _person["elbows"][i] as Node2D
			hip.rotation = lerpf(hip.rotation, sin(p) * 0.5 * k, 12.0 * delta)
			knee.rotation = lerpf(knee.rotation, 0.05 + maxf(0.0, sin(p - 1.8)) * 0.7 * k, 12.0 * delta)
			shoulder.rotation = lerpf(shoulder.rotation, 0.1 - sin(p) * 0.35 * k, 12.0 * delta)
			elbow.rotation = lerpf(elbow.rotation, -(0.3 + 0.4 * k + sin(p + 0.8) * 0.15 * k), 12.0 * delta)
		person_node.position.y = _bob_base_y + 1.5 * (0.5 + 0.5 * cos(_phase * 2.0)) * k

	func _open_door() -> void:
		if _door_opened:
			return
		_door_opened = true
		GameState.play_sfx(GameState.SFX_DASH, -10.0, 0.7)  # 舱门滑开 0.7 倍速
		var tween := create_tween().set_parallel(true)
		tween.tween_property(_door_l, "position:x", _door_l.position.x - 85.0, 0.5 * _time_u)
		tween.tween_property(_door_r, "position:x", _door_r.position.x + 85.0, 0.5 * _time_u)
		tween.tween_property(_door_leak, "color:a", 0.6, 0.5 * _time_u)  # 门缝光线泄出


## 侧视走廊：天花板/地面透视线 + 舱壁管线；顶部感应灯带 12 节点随主角行进亮起；
## 尽头休息室舱门双扇滑开 + 门缝泄光。主角步行 ~90px/s，镜头跟随（主角固定左 1/3）。
func _build_shot6() -> Node2D:
	var dur: float = _shot_durations[5]
	var root := _WalkShot.new()
	root.name = "Shot6"
	root._stop_scroll = 140.0 * (dur / 2.4)  # 步行距离按镜头时长等比压缩（步速 90px/s 不变）
	root._time_u = dur / 2.4
	root.add_child(_bg_rect(Color(0.02, 0.02, 0.05)))
	var world := Node2D.new()
	root.add_child(world)
	root._world = world
	# 天花板/地面透视线 + 舱壁管线（远景透出虚影站结构微光）
	world.add_child(_line(PackedVector2Array([Vector2(0.0, 340.0), Vector2(2600.0, 340.0)]), Color(0.16, 0.22, 0.32, 0.7), 3.0))
	world.add_child(_line(PackedVector2Array([Vector2(0.0, 820.0), Vector2(2600.0, 820.0)]), Color(0.16, 0.22, 0.32, 0.7), 3.0))
	# 舱壁肋板 ×12：宽窄/深浅交替，破除等距重复感
	for i in 12:
		var wide := i % 2 == 0
		var rib := _rect_poly(22.0 if wide else 14.0, 480.0, Color(0.07, 0.09, 0.13) if wide else Color(0.05, 0.065, 0.10))
		rib.position = Vector2(120.0 + 220.0 * i, 580.0)
		world.add_child(rib)
	# 肋板间舱壁小壁板（带顶部刻线，通道纵深细节）
	for i in 6:
		var px := 230.0 + 440.0 * i
		var panel := _rect_poly(84.0, 56.0, Color(0.06, 0.08, 0.12))
		panel.position = Vector2(px, 560.0)
		world.add_child(panel)
		world.add_child(_line(PackedVector2Array([Vector2(px - 42.0, 531.0), Vector2(px + 42.0, 531.0)]), Color(0.2, 0.3, 0.42, 0.5), 1.5))
	for pipe in [[360.0, 6.0], [382.0, 4.0]]:
		world.add_child(_line(PackedVector2Array([Vector2(0.0, pipe[0]), Vector2(2600.0, pipe[0])]), Color(0.2, 0.26, 0.36), pipe[1]))
	# 顶灯光锥 ×3（叠加态低 alpha，挂在世界容器随滚动视差）
	for cx in [500.0, 1200.0, 1900.0]:
		var lamp_cone := Polygon2D.new()
		lamp_cone.polygon = PackedVector2Array(
			[
				Vector2(cx - 26.0, 356.0),
				Vector2(cx + 26.0, 356.0),
				Vector2(cx + 150.0, 820.0),
				Vector2(cx - 150.0, 820.0),
			]
		)
		lamp_cone.color = Color(0.6, 0.9, 1.0, 0.05)
		var cone_mat := CanvasItemMaterial.new()
		cone_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
		lamp_cone.material = cone_mat
		world.add_child(lamp_cone)
	# 地面反光提示条（灯带光在地板上的长条泛光）
	var floor_hint := _line(PackedVector2Array([Vector2(0.0, 812.0), Vector2(2600.0, 812.0)]), Color(0.5, 0.8, 1.0, 0.10), 2.5)
	var floor_hint_mat := CanvasItemMaterial.new()
	floor_hint_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	floor_hint.material = floor_hint_mat
	world.add_child(floor_hint)
	# 远处结构微光（虚影站内部）
	var far_glow := _glow(300.0, Color(0.0, 0.5, 1.0, 0.06))
	far_glow.position = Vector2(2300.0, 540.0)
	world.add_child(far_glow)
	# 顶部感应灯带：12 节点分段（初始暗，随主角 x 阈值点亮）
	for i in 12:
		var lx := 200.0 + 200.0 * i
		var seg := _line(PackedVector2Array([Vector2(lx - 80.0, 352.0), Vector2(lx + 80.0, 352.0)]), Color(0.6, 0.95, 1.0, 0.08), 5.0)
		var seg_mat := CanvasItemMaterial.new()
		seg_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
		seg.material = seg_mat
		world.add_child(seg)
		root._lights.append(seg)
		root._light_x.append(lx)
	# 尽头休息室舱门：门框 + 左右双扇门片 + 门缝泄光（门高 280px，明显高于 185px 人物）
	var door_x := 920.0
	var frame := _line(
		PackedVector2Array(
			[
				Vector2(door_x - 76.0, 540.0),
				Vector2(door_x + 76.0, 540.0),
				Vector2(door_x + 76.0, 820.0),
				Vector2(door_x - 76.0, 820.0),
			]
		),
		Color(0.0, 0.83, 1.0, 0.4),
		2.5
	)
	frame.closed = true
	world.add_child(frame)
	var leak := ColorRect.new()
	leak.color = Color(0.6, 0.95, 1.0, 0.0)
	leak.position = Vector2(door_x - 66.0, 546.0)
	leak.size = Vector2(132.0, 268.0)
	leak.mouse_filter = Control.MOUSE_FILTER_IGNORE
	world.add_child(leak)
	root._door_leak = leak
	var door_l := _rect_poly(76.0, 272.0, Color(0.10, 0.13, 0.18))
	door_l.position = Vector2(door_x - 39.0, 676.0)
	world.add_child(door_l)
	root._door_l = door_l
	var door_r := _rect_poly(76.0, 272.0, Color(0.10, 0.13, 0.18))
	door_r.position = Vector2(door_x + 39.0, 676.0)
	world.add_child(door_r)
	root._door_r = door_r
	# 主角步行（固定画面左 1/3，世界反向平移）
	var person := _build_person()
	var pnode: Node2D = person["node"]
	pnode.scale = Vector2.ONE * 1.6
	pnode.position = Vector2(640.0, 746.0)
	_pose_stand(person)
	root.add_child(pnode)
	root._person = person
	root._bob_base_y = pnode.position.y
	# 脚步声 ×6（0.4s 间隔，极轻短促）
	var steps := [0]
	var step_timer := Timer.new()
	step_timer.wait_time = 0.4
	step_timer.autostart = true
	root.add_child(step_timer)
	step_timer.timeout.connect(
		func() -> void:
			steps[0] += 1
			if steps[0] > 6 or not root._walking:
				step_timer.stop()
				return
			GameState.play_sfx(GameState.SFX_BUFF_PICK, -20.0)
	)
	return root


# ---------------- 镜头 7：休息室入睡（2.0s） ----------------


class _RoomShot:
	extends Node2D
	var _person: Dictionary  # 人物关节（走入步行循环 + 躺后呼吸共用）
	var _time_u := 1.0  # u = dur/3.0（镜头内部关键帧缩放）
	var _phase := 0.0
	var _t := 0.0
	var _bob_base_y := 0.0
	var _breathe_w := 0.0  # 呼吸权重（躺下完成后缓入，避免突变）
	var _stars: Array[Sprite2D] = []  # 观察窗外漂移星点（窗框内回卷）
	var _star_vel := PackedVector2Array()
	var _star_bounds := Rect2()

	func _process(delta: float) -> void:
		_t += delta
		var u := _time_u
		if _t < 0.6 * u:
			# 走入步行循环（窗口两端权重淡入淡出，结束自动缓回直立；肢体公式同 _WalkShot）
			_phase += delta * 16.0
			var k := clampf(minf(_t, 0.6 * u - _t) / maxf(0.12 * u, 0.001), 0.0, 1.0)  # H20：镜头时长 0 除零防御
			var person_node: Node2D = _person["node"]
			for i in 2:
				var p: float = _phase + PI * float(i)
				var hip := _person["hips"][i] as Node2D
				var knee := _person["knees"][i] as Node2D
				var shoulder := _person["shoulders"][i] as Node2D
				var elbow := _person["elbows"][i] as Node2D
				hip.rotation = lerpf(hip.rotation, sin(p) * 0.5 * k, 12.0 * delta)
				knee.rotation = lerpf(knee.rotation, 0.05 + maxf(0.0, sin(p - 1.8)) * 0.7 * k, 12.0 * delta)
				shoulder.rotation = lerpf(shoulder.rotation, 0.1 - sin(p) * 0.35 * k, 12.0 * delta)
				elbow.rotation = lerpf(elbow.rotation, -(0.3 + 0.4 * k + sin(p + 0.8) * 0.15 * k), 12.0 * delta)
			person_node.position.y = _bob_base_y + 1.5 * (0.5 + 0.5 * cos(_phase * 2.0)) * k
		elif _t > 1.7 * u:
			# 躺下后的呼吸起伏：躯干微小缩放/位移正弦
			_breathe_w = lerpf(_breathe_w, 1.0, 1.5 * delta)
			var torso := _person["torso"] as Node2D
			var b := sin(_t * 1.5) * _breathe_w
			torso.scale = Vector2(1.0, 1.0 + 0.025 * b)
			torso.position = Vector2(0.0, 0.6 * b)
		# 观察窗外星点缓慢漂移（越界回卷，始终留在窗框内）
		for i in _stars.size():
			var s := _stars[i]
			s.position += _star_vel[i] * delta
			if s.position.x < _star_bounds.position.x:
				s.position.x += _star_bounds.size.x
			elif s.position.x > _star_bounds.end.x:
				s.position.x -= _star_bounds.size.x
			if s.position.y < _star_bounds.position.y:
				s.position.y += _star_bounds.size.y
			elif s.position.y > _star_bounds.end.y:
				s.position.y -= _star_bounds.size.y


## 休眠床 + 床头全息屏微光 + 顶部暖调小灯（全场景唯一暖光源）+ 观察窗外环体缓转 + 漂移星点；
## 主角走入 → 坐下 → 躺下（三姿态 0.6s 间隔）→ 镜头推近面部特写（scale→1.6）→
## 眼睑 0.8s 闭合；闭眼瞬间画面渐暗（导演 0.9s 淡黑）+ BGM 淡出到 -40dB。
func _build_shot7() -> Node2D:
	var dur: float = _shot_durations[6]
	var u := dur / 3.0
	var root := _RoomShot.new()
	root.name = "Shot7"
	root._time_u = u
	root.add_child(_bg_rect(Color(0.02, 0.02, 0.04)))
	var room := Node2D.new()  # 推近面部特写的运镜容器
	root.add_child(room)
	# 舱室结构：地面线 + 舱壁
	room.add_child(_line(PackedVector2Array([Vector2(300.0, 840.0), Vector2(1620.0, 840.0)]), Color(0.16, 0.22, 0.32, 0.7), 3.0))
	room.add_child(_line(PackedVector2Array([Vector2(300.0, 300.0), Vector2(300.0, 840.0)]), Color(0.10, 0.14, 0.22, 0.5)))
	room.add_child(_line(PackedVector2Array([Vector2(1620.0, 300.0), Vector2(1620.0, 840.0)]), Color(0.10, 0.14, 0.22, 0.5)))
	# 观察窗：窗外虚影环体缓慢旋转轮廓（提醒此处仍在虚影站内）+ 远景星点漂移
	root._star_bounds = Rect2(392.0, 412.0, 316.0, 156.0)  # 窗框内缘留边
	for i in 3:
		var star := CinematicFx.soft_glow(2.0, Color(0.85, 0.92, 1.0, 0.7))
		star.position = Vector2(
			randf_range(root._star_bounds.position.x, root._star_bounds.end.x),
			randf_range(root._star_bounds.position.y, root._star_bounds.end.y)
		)
		room.add_child(star)
		root._stars.append(star)
		root._star_vel.append(Vector2(randf_range(-14.0, -6.0), randf_range(-3.0, 3.0)))
	var window_frame := _line(
		PackedVector2Array(
			[
				Vector2(380.0, 400.0),
				Vector2(720.0, 400.0),
				Vector2(720.0, 580.0),
				Vector2(380.0, 580.0),
			]
		),
		Color(0.0, 0.83, 1.0, 0.35),
		2.5
	)
	window_frame.closed = true
	room.add_child(window_frame)
	var ring_outside := Node2D.new()
	ring_outside.position = Vector2(550.0, 490.0)
	room.add_child(ring_outside)
	var out_points := PackedVector2Array()
	for i in 48:
		var a := TAU * float(i) / 48.0
		out_points.append(Vector2(cos(a), sin(a)) * 150.0)
	var out_ring := _line(out_points, Color(0.0, 0.6, 1.0, 0.12), 14.0)
	out_ring.closed = true
	ring_outside.add_child(out_ring)
	for i in 4:
		var a := TAU * float(i) / 4.0
		ring_outside.add_child(
			_line(PackedVector2Array([Vector2(cos(a), sin(a)) * 40.0, Vector2(cos(a), sin(a)) * 140.0]), Color(0.0, 0.6, 1.0, 0.10), 4.0)
		)
	var spin := root.create_tween().set_loops()
	spin.tween_property(ring_outside, "rotation", TAU, 20.0).set_trans(Tween.TRANS_LINEAR)
	# 顶部暖调小灯（全场景唯一暖光源，「家」的视觉锚点）
	var lamp := _rect_poly(30.0, 10.0, Color(0.2, 0.16, 0.1))
	lamp.position = Vector2(1100.0, 320.0)
	room.add_child(lamp)
	var warm := _glow(130.0, Color(1.0, 0.75, 0.45, 0.22))
	warm.position = Vector2(1100.0, 340.0)
	room.add_child(warm)
	var cone := Polygon2D.new()
	cone.polygon = PackedVector2Array(
		[
			Vector2(1060.0, 330.0),
			Vector2(1140.0, 330.0),
			Vector2(1240.0, 840.0),
			Vector2(960.0, 840.0),
		]
	)
	cone.color = Color(1.0, 0.8, 0.5, 0.05)
	var cone_mat := CanvasItemMaterial.new()
	cone_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	cone.material = cone_mat
	room.add_child(cone)
	# 休眠床：圆角平台（260 长，可躺下 185px 人物）+ 床头全息小屏微光
	var pod := _rect_poly(260.0, 26.0, Color(0.08, 0.10, 0.14))
	pod.position = Vector2(1080.0, 786.0)
	room.add_child(pod)
	room.add_child(_line(PackedVector2Array([Vector2(950.0, 773.0), Vector2(1210.0, 773.0)]), Color(0.0, 0.83, 1.0, 0.4), 2.0))
	var pillow := _rect_poly(50.0, 12.0, Color(0.12, 0.15, 0.2))
	pillow.position = Vector2(980.0, 766.0)
	room.add_child(pillow)
	var holo := _rect_poly(44.0, 32.0, Color(0.0, 0.83, 1.0, 0.15))
	holo.position = Vector2(946.0, 700.0)
	var holo_mat := CanvasItemMaterial.new()
	holo_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	holo.material = holo_mat
	room.add_child(holo)
	var holo_glow := _glow(40.0, Color(0.0, 0.83, 1.0, 0.1))
	holo_glow.position = Vector2(946.0, 700.0)
	room.add_child(holo_glow)
	# 主角：步行循环走入（_RoomShot 驱动肢体，位置仍由 tween 推进）→ 床沿坐下 → 平躺
	var person := _build_person()
	var pnode: Node2D = person["node"]
	pnode.scale = Vector2.ONE * 1.6
	pnode.position = Vector2(560.0, 766.0)
	_pose_stand(person)
	room.add_child(pnode)
	root._person = person
	root._bob_base_y = pnode.position.y
	var walk_in := root.create_tween()
	walk_in.tween_property(pnode, "position:x", 990.0, 0.6 * u).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	_once(
		root,
		0.7 * u,
		func() -> void:
			# 坐下（床沿）：关节 0.3s 换姿态 + 重心下移
			var sit := root.create_tween().set_parallel(true)
			for i in 2:
				sit.tween_property(person["hips"][i], "rotation", -1.5, 0.3 * u)
				sit.tween_property(person["knees"][i], "rotation", 1.4, 0.3 * u)
				sit.tween_property(person["shoulders"][i], "rotation", 0.2, 0.3 * u)
				sit.tween_property(person["elbows"][i], "rotation", -0.8, 0.3 * u)
			sit.tween_property(pnode, "position", Vector2(1010.0, 756.0), 0.3 * u)
	)
	_once(
		root,
		1.3 * u,
		func() -> void:
			GameState.play_sfx(GameState.SFX_RESUPPLY, -16.0)  # 躺下轻柔音
			# 躺下：整体后倒 -90° 卧上休眠床（床面 y≈762）+ 四肢舒展微调
			var lie := root.create_tween().set_parallel(true)
			lie.tween_property(pnode, "rotation", -PI * 0.5, 0.4 * u).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
			lie.tween_property(pnode, "position", Vector2(1080.0, 762.0), 0.4 * u).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
			for i in 2:
				lie.tween_property(person["hips"][i], "rotation", 0.1, 0.5 * u)
				lie.tween_property(person["knees"][i], "rotation", 0.12, 0.5 * u)
				lie.tween_property(person["shoulders"][i], "rotation", 0.15, 0.5 * u)
				lie.tween_property(person["elbows"][i], "rotation", -0.2, 0.5 * u)
	)
	# 面部特写：镜头推近至头部（scale→1.6，聚焦平躺后的头盔位置 ≈(981,744)）
	# C12 修复：set_parallel 下前置 tween_interval 不延迟并行成员（特写提前完成）；
	# 改顺序 tween + 前置 interval，scale/position 两属性经 parallel() 同时推进
	var push_in := root.create_tween()
	push_in.tween_interval(1.5 * u)
	push_in.set_parallel(true)
	push_in.tween_property(room, "scale", Vector2.ONE * 1.6, 1.0 * u).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	(
		push_in
		. tween_property(room, "position", Vector2(960.0, 540.0) - Vector2(981.0, 744.0) * 1.6, 1.0 * u)
		. set_trans(Tween.TRANS_SINE)
		. set_ease(Tween.EASE_IN_OUT)
	)
	# 眼睑缓缓闭合（与渐暗重叠：闭眼完成时画面约五成暗）
	var blink := root.create_tween()
	blink.tween_interval(1.5 * u)
	blink.tween_property(person["eyelid"], "scale:y", 1.0, 0.8 * u)
	# 渐暗期 BGM 同步淡出到 -40dB（skip 时由 skip() kill 并立即置位）
	if bgm_player != null:
		_bgm_tween = create_tween()
		_bgm_tween.tween_interval(dur - _fade_out_time())  # 与画面渐暗同步起淡
		_bgm_tween.tween_property(bgm_player, "volume_db", -40.0, _fade_out_time())
	return root
