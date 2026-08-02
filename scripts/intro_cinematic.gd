class_name IntroCinematic
extends CanvasLayer
## 开场过场导演：6 镜头时序串联、黑场转场、跳过与整树清理。
## 设计文档（单一事实源）：docs/INTRO_CINEMATIC.md。
## 全部按 1920×1080 设计坐标布局；镜头内连续动画用 tween / Timer 节点 / _process，
## 严禁 await create_timer 协程（退出时协程状态泄漏）。

signal finished

const TRANSITION := 0.3  # 镜头间黑场淡入淡出（含在各镜头时长内）
const OUTRO_FADE := 0.7  # 镜头 6 末尾淡出到标题定格
const TITLE_CARD_IN := 0.2  # 收尾标题定格：淡入
const TITLE_CARD_HOLD := 0.8  # 收尾标题定格：停留
const TITLE_CARD_OUT := 0.2  # 收尾标题定格：淡出（随后走统一出口 skip）
const PLAYER_SHIP: Texture2D = preload("res://assets/sprites/player_ship.png")
## 过场音频统一策略：全部音量下移 + 变调下沉柔和化（避免爆炸/引擎音突兀炸耳）
const AUDIO_VOL_OFFSET := -6.0  # 各音效在原设定基础上统一 -6dB
const AUDIO_PITCH := 0.88  # 变调下沉，音色更闷柔

## 每镜头时长（§2 分镜表；六镜头 16.1s = 总和，转场含在内；+标题定格 1.2s = 总 17.3s）。测试可改短。
var _shot_durations: Array[float] = [2.8, 2.5, 2.5, 2.5, 2.8, 3.0]

var _shot_index := -1
var _current_shot: Node2D = null
var _shot_timer: Timer
var _done := false
var _drift_t := 0.0  # 导演级手持漂移相位（共享容器，单 _process 零堆分配）
var _white_transition := false  # 差异化转场：下一镜头以白闪承接
var _sub_tween: Tween = null  # 字幕淡入/淡出互斥

@onready var _shot_root: Node2D = $ShotRoot
@onready var _fade: ColorRect = $Fade
@onready var _flash: ColorRect = $Flash
@onready var _subtitle: Label = $Subtitle
@onready var _title_card: Control = $TitleCard
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
	_skip_hint.text = tr("INTRO_SKIP")
	_skip_hint.add_theme_font_override("font", UITheme.FONT)
	_subtitle.add_theme_font_override("font", UITheme.FONT)
	(_title_card.get_node("Center/VBox/Title") as Label).add_theme_font_override("font", UITheme.FONT)
	_shot_timer = Timer.new()
	_shot_timer.one_shot = true
	_shot_timer.timeout.connect(_on_shot_timeout)
	add_child(_shot_timer)
	# 首镜头延后到帧末启动：测试可在 add_child 同帧替换 _shot_durations
	_advance.call_deferred()


## 任意键/鼠标点击跳过；Esc（ui_cancel）放行给 BackNavigator 路由到 Main.skip_intro()（公开接口，A7 后经 _skip_intro 落地）
func _unhandled_input(event: InputEvent) -> void:
	if _done or event.is_action_pressed("ui_cancel"):
		return
	var pressed_key: bool = event is InputEventKey and event.pressed and not event.echo
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


## 跳过（幂等）：与自然结束同一出口——停计时、发 finished、整树 queue_free
func skip() -> void:
	if _done:
		return
	_done = true
	_shot_timer.stop()
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
		_play_title_card()  # 自然结束：标题定格后走统一出口
		return
	_current_shot = _build_shot(_shot_index)
	_shot_root.add_child(_current_shot)
	var dur: float = _shot_durations[_shot_index]
	_set_subtitle("INTRO_SUB_%d" % (_shot_index + 1))
	if _white_transition:
		# 白闪承接：黑层保持透明，白闪直接回收
		_white_transition = false
		_fade.color.a = 0.0
		var flash_tween := create_tween()
		flash_tween.tween_property(_flash, "color:a", 0.0, 0.28)
	else:
		# 黑场淡入（镜头 1 从全黑起，其余承接上一镜头的淡出）
		_fade.color.a = 1.0
		var fade_tween := create_tween()
		fade_tween.tween_property(_fade, "color:a", 0.0, minf(TRANSITION, dur * 0.5))
	_shot_timer.start(dur - _fade_out_time())


func _fade_out_time() -> float:
	var dur: float = _shot_durations[_shot_index]
	var t := OUTRO_FADE if _shot_index == _shot_durations.size() - 1 else TRANSITION
	return minf(t, dur * 0.5)


func _on_shot_timeout() -> void:
	if _done:
		return
	# 差异化转场：镜头 1→2（链爆）与 4→5（点火弹射）用白闪，其余黑场
	_white_transition = _shot_index == 0 or _shot_index == 3
	# 字幕随转场淡出
	if _sub_tween != null and _sub_tween.is_valid():
		_sub_tween.kill()
	_sub_tween = create_tween()
	_sub_tween.tween_property(_subtitle, "modulate:a", 0.0, _fade_out_time())
	if _white_transition:
		var flash_tween := create_tween()
		flash_tween.tween_property(_flash, "color:a", 1.0, 0.10)
		flash_tween.tween_callback(_advance)
	else:
		var fade_tween := create_tween()
		fade_tween.tween_property(_fade, "color:a", 1.0, _fade_out_time())
		fade_tween.tween_callback(_advance)


## 收尾标题定格：淡入 → 停留 → 淡出 → skip() 统一出口
func _play_title_card() -> void:
	if _sub_tween != null and _sub_tween.is_valid():
		_sub_tween.kill()
	_subtitle.modulate.a = 0.0
	var tween := create_tween()
	tween.tween_property(_title_card, "modulate:a", 1.0, TITLE_CARD_IN)
	tween.tween_interval(TITLE_CARD_HOLD)
	tween.tween_property(_title_card, "modulate:a", 0.0, TITLE_CARD_OUT)
	tween.tween_callback(skip)


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
		_:
			return _build_shot6()


# ---------------- 构图辅助 ----------------


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
## state[0] 持有上一次颤动 tween：重复触发时杀旧刷新峰值，形成"每爆一下震一下"的连锁叠加；
## host.position 基线必须为 ZERO（挂在镜头自己的 root 上，不与导演手持漂移的 _shot_root 冲突）。
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


## 粒子工厂委托给 CinematicFx（同 cfg 契约）：默认挂共享软点贴图，消除硬边圆点的廉价感；
## scale 语义保持「像素直径」，≤96/发射器的硬性上限不变。
static func _particles(cfg: Dictionary) -> GPUParticles2D:
	return CinematicFx.particles(cfg)


# ---------------- 镜头 1：远景推近（2.8s） ----------------
## 环形空间站深空爆炸：星空底 + 远处淡星云 + 弧段/舱段拼装环体（分块/外廓细节线），
## 容器 0.7→1.0 匀速推近；爆炸核心辉光暗红→橙白放大 + 冲击波扩散环，
## 碎片外抛 + 前景尘埃双层粒子 + 破口剥落碎块。
func _build_shot1() -> Node2D:
	var dur: float = _shot_durations[0]
	var root := Node2D.new()
	root.name = "Shot1"
	root.add_child(Starfield.new())
	# 远处星云（比镜头 6 更淡，只铺层次；软径向光晕消除硬边）
	var neb1 := CinematicFx.soft_glow(520.0, Color(0.2, 0.12, 0.4, 0.08))
	neb1.position = Vector2(1560.0, 260.0)
	root.add_child(neb1)
	var neb2 := CinematicFx.soft_glow(420.0, Color(0.08, 0.18, 0.4, 0.08))
	neb2.position = Vector2(300.0, 840.0)
	root.add_child(neb2)

	# 站体构件抽为 DawnStation 共享构建函数（开场=实体毁灭态，纯提取不改视觉；
	# 返航/基地背景复用虚影态，docs/RETURN_HOME_CINEMATIC.md §5）
	var station := DawnStation.build(DawnStation.Mode.DESTROYED)
	station.position = Vector2(960.0, 470.0)
	station.scale = Vector2.ONE * 0.7
	root.add_child(station)

	# 爆炸核心：破口处叠加辉光，暗红 → 橙白并放大（软光晕；scale tween 以 soft_glow 基准缩放为底）
	var blast_pos := Vector2(cos(0.85), sin(0.85)) * 260.0
	var halo := CinematicFx.soft_glow(150.0, Color(0.6, 0.2, 0.08, 0.35))
	halo.position = blast_pos
	var halo_base := halo.scale
	station.add_child(halo)
	var core := CinematicFx.soft_glow(80.0, Color(1.0, 1.0, 1.0, 0.95))
	core.position = blast_pos
	core.modulate = Color(0.5, 0.1, 0.05)
	var core_base := core.scale
	station.add_child(core)
	var tween := root.create_tween().set_parallel(true)
	tween.tween_property(station, "scale", Vector2.ONE, dur).set_trans(Tween.TRANS_LINEAR)
	tween.tween_property(core, "modulate", Color(1.0, 0.9, 0.7), dur * 0.8)
	tween.tween_property(core, "scale", core_base * 2.4, dur)
	tween.tween_property(halo, "scale", halo_base * 1.8, dur)

	# 舱段模块舷窗灯 ×8：随爆炸吞噬站体，按距破口角距由近到远逐盏熄灭（错峰 tween 延迟贯穿全镜头）
	var light_seq := [1.571, 0.0, 2.356, 5.497, 3.142, 4.712, 3.927, -1.0]  # 舱段角（rad），-1 = 中心毂
	for l_i in light_seq.size():
		var lamp_a: float = light_seq[l_i]
		var lamp := _glow(4.5, Color(1.0, 0.75, 0.4, 0.85))
		lamp.position = Vector2.ZERO if lamp_a < 0.0 else Vector2(cos(lamp_a), sin(lamp_a)) * 260.0
		station.add_child(lamp)
		var lamp_t := root.create_tween()
		lamp_t.tween_interval(dur * (0.22 + 0.07 * l_i))
		lamp_t.tween_property(lamp, "modulate:a", 0.05, 0.35)

	# 冲击波扩散环：爆心薄环急速扩大并淡出（叠加态，错开 0.3s 两波）
	var wave_shake_state: Array = [null]  # 两波主爆颤动共享刷新
	for wave in 2:
		var wave_points := PackedVector2Array()
		for i in 40:
			var a := TAU * float(i) / 40.0
			wave_points.append(Vector2(cos(a), sin(a)) * 60.0)
		var wave_ring := _line(wave_points, Color(1.0, 0.7, 0.35, 0.7), 5.0)
		wave_ring.closed = true
		wave_ring.position = blast_pos
		var wave_mat := CanvasItemMaterial.new()
		wave_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
		wave_ring.material = wave_mat
		wave_ring.scale = Vector2.ONE * 0.2
		station.add_child(wave_ring)
		var wt := root.create_tween().set_parallel(true)
		wt.tween_interval(0.2 + 0.3 * wave)
		wt.chain().tween_property(wave_ring, "scale", Vector2.ONE * 4.5, 0.9).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
		wt.tween_property(wave_ring, "modulate:a", 0.0, 0.8)
		# 起爆同步一次主爆颤动（幅度略大于镜头 2 单节点）
		var kt := root.create_tween()
		kt.tween_interval(0.2 + 0.3 * wave)
		kt.tween_callback(func() -> void: _kick_shake(root, 6.0, wave_shake_state))

	# 二次殉爆（dur*0.45 起）：破口对侧环缘小型闪爆——软闪光核 + 冲击波扩散环 + 颤动 + 递减音量
	var blast2_pos := Vector2(cos(3.8), sin(3.8)) * 240.0
	var boom2 := Timer.new()
	boom2.one_shot = true
	boom2.wait_time = dur * 0.45
	boom2.autostart = true
	root.add_child(boom2)
	boom2.timeout.connect(
		func() -> void:
			var flash2 := CinematicFx.soft_glow(56.0, Color(1.0, 0.85, 0.6, 0.9))
			flash2.position = blast2_pos
			var flash2_base := flash2.scale
			flash2.scale = Vector2.ZERO
			station.add_child(flash2)
			var f2t := root.create_tween()
			f2t.tween_property(flash2, "scale", flash2_base * 1.3, 0.1)
			f2t.tween_property(flash2, "modulate:a", 0.0, 0.4)
			f2t.tween_callback(flash2.queue_free)
			var wave2 := (
				CinematicFx
				. shockwave(
					{
						"radius": 200.0,
						"time": 0.7,
						"color": Color(1.0, 0.6, 0.25, 0.55),
						"core_color": Color(1.0, 0.9, 0.7, 0.85),
						"width": 10.0,
					}
				)
			)
			wave2.position = blast2_pos
			station.add_child(wave2)
			_kick_shake(root, 5.0, wave_shake_state)
			GameState.play_sfx(GameState.SFX_EXPLOSION, -8.0 + AUDIO_VOL_OFFSET, AUDIO_PITCH)
	)

	# 余烬：全镜头持续的橙色慢速上飘细屑（低透明度，燃烧余韵层）
	var embers := _particles(
		{
			"amount": 40,
			"lifetime": 3.2,
			"vel_min": 12.0,
			"vel_max": 40.0,
			"spread": 35.0,
			"scale_min": 3.0,
			"scale_max": 6.0,
			"color": Color(1.0, 0.5, 0.15, 0.3),
		}
	)
	embers.position = Vector2(960.0, 500.0)
	root.add_child(embers)

	# 碎片（高速外抛）+ 尘埃（慢速前景低透明度）
	var debris := _particles(
		{
			"amount": 48,
			"lifetime": 2.2,
			"vel_min": 160.0,
			"vel_max": 420.0,
			"damping_min": 60.0,
			"damping_max": 140.0,
			"scale_min": 3.0,
			"scale_max": 7.0,
			"color": Color(1.0, 0.55, 0.15),
		}
	)
	debris.position = Vector2(960.0, 470.0) + blast_pos * 0.7
	root.add_child(debris)
	var dust := _particles(
		{
			"amount": 40,
			"lifetime": 3.5,
			"vel_min": 20.0,
			"vel_max": 60.0,
			"scale_min": 6.0,
			"scale_max": 12.0,
			"color": Color(0.6, 0.5, 0.4, 0.12),
		}
	)
	dust.position = Vector2(960.0, 540.0)
	root.add_child(dust)
	# 前景碎片层：深色大块残骸横向漂移 + 翻滚（比站体更快的近景视差）
	for k in 4:
		var drift_shard := Polygon2D.new()
		drift_shard.polygon = PackedVector2Array([Vector2(-26.0, -10.0), Vector2(20.0, -18.0), Vector2(30.0, 12.0), Vector2(-12.0, 20.0)])
		drift_shard.color = Color(0.04, 0.04, 0.07)
		drift_shard.position = Vector2(260.0 + 470.0 * k, 180.0 + 700.0 * (k % 2))
		drift_shard.scale = Vector2.ONE * (0.8 + 0.25 * (k % 3))
		root.add_child(drift_shard)
		drift_shard.add_child(_line(PackedVector2Array([Vector2(-26.0, -10.0), Vector2(20.0, -18.0)]), Color(1.0, 0.6, 0.25, 0.4), 2.0))
		# 顶缘暖色软轮廓光（朝向爆心一侧的反射光，把剪影从深空里托出来）
		var shard_rim := CinematicFx.soft_glow(18.0, Color(1.0, 0.55, 0.2, 0.22))
		shard_rim.position = Vector2(0.0, -12.0)
		drift_shard.add_child(shard_rim)
		var spin := root.create_tween().set_loops()
		spin.tween_property(drift_shard, "rotation", drift_shard.rotation + TAU, 9.0 + 3.0 * k)
		var move := root.create_tween().set_loops()
		(
			move
			. tween_property(drift_shard, "position", drift_shard.position + Vector2(140.0 + 60.0 * k, -30.0), 4.0 + k)
			. set_trans(Tween.TRANS_SINE)
			. set_ease(Tween.EASE_IN_OUT)
		)
		move.tween_property(drift_shard, "position", drift_shard.position, 4.0 + k).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	GameState.play_sfx(GameState.SFX_EXPLOSION_BIG, AUDIO_VOL_OFFSET, AUDIO_PITCH)
	return root


# ---------------- 镜头 2：X光链式爆炸（2.5s） ----------------
## 冷蓝底剖面：甲板为半透明填充带 + 亮边（有厚度层次），舱室隔断分区清晰；
## 橙红能量沿预设折线 0.2s/节点链式点亮（叠加态外发光双线），节点处爆开橙色圆闪，
## 爆炸音 ×3 音量递减。
func _build_shot2() -> Node2D:
	var root := Node2D.new()
	root.name = "Shot2"
	root.add_child(_bg_rect(Color(0.02, 0.05, 0.12)))
	var wire := Color(0.3, 0.6, 1.0, 0.4)
	# 4 层甲板：半透明蓝填充带（厚度）+ 上下亮边
	var deck_ys := [340.0, 520.0, 700.0, 880.0]
	for i in deck_ys.size():
		var y: float = deck_ys[i]
		var band := Polygon2D.new()
		band.polygon = PackedVector2Array(
			[
				Vector2(150.0, y),
				Vector2(1770.0, y),
				Vector2(1770.0, y + 16.0),
				Vector2(150.0, y + 16.0),
			]
		)
		band.color = Color(0.2, 0.45, 0.9, 0.12)
		root.add_child(band)
		root.add_child(_line(PackedVector2Array([Vector2(150.0, y), Vector2(1770.0, y)]), Color(0.45, 0.7, 1.0, 0.7), 2.5))
		root.add_child(_line(PackedVector2Array([Vector2(150.0, y + 16.0), Vector2(1770.0, y + 16.0)]), Color(0.2, 0.4, 0.8, 0.4)))
	# 舱室分区：相邻舱室交替冷蓝微填充，层级更清
	for deck in deck_ys.size() - 1:
		for i in 6:
			if (i + deck) % 2 == 0:
				continue
			var room := Polygon2D.new()
			var rx := 150.0 + 270.0 * i
			room.polygon = PackedVector2Array(
				[
					Vector2(rx, deck_ys[deck] + 16.0),
					Vector2(rx + 270.0, deck_ys[deck] + 16.0),
					Vector2(rx + 270.0, deck_ys[deck + 1]),
					Vector2(rx, deck_ys[deck + 1]),
				]
			)
			room.color = Color(0.15, 0.35, 0.8, 0.05)
			root.add_child(room)
	# 骨架竖线 + 舱室隔断（隔断更粗，分区感）
	for i in 7:
		var x := 150.0 + 270.0 * i
		root.add_child(_line(PackedVector2Array([Vector2(x, 300.0), Vector2(x, 920.0)]), wire))
	for i in 6:
		var x := 285.0 + 270.0 * i
		root.add_child(_line(PackedVector2Array([Vector2(x, 520.0), Vector2(x, 700.0)]), Color(0.4, 0.65, 1.0, 0.55), 3.5))
	# 外框
	var frame := _line(
		PackedVector2Array(
			[
				Vector2(150.0, 300.0),
				Vector2(1770.0, 300.0),
				Vector2(1770.0, 920.0),
				Vector2(150.0, 920.0),
			]
		),
		Color(0.4, 0.7, 1.0, 0.6),
		3.0
	)
	frame.closed = true
	root.add_child(frame)
	# 蛇形扫描线网格底（单条 Line2D 铺满剖面区）+ 循环往复的亮色扫描带
	var scan_points := PackedVector2Array()
	var sy := 304.0
	var scan_left := true
	while sy <= 916.0:
		scan_points.append(Vector2(150.0 if scan_left else 1770.0, sy))
		scan_points.append(Vector2(1770.0 if scan_left else 150.0, sy))
		sy += 6.0
		scan_left = not scan_left
	root.add_child(_line(scan_points, Color(0.4, 0.7, 1.0, 0.05), 1.0))
	var scan_band := _bg_rect(Color(0.4, 0.8, 1.0, 0.06))
	scan_band.size = Vector2(1620.0, 46.0)
	scan_band.position = Vector2(150.0, 300.0)
	root.add_child(scan_band)
	var band_sweep := root.create_tween().set_loops()
	band_sweep.tween_property(scan_band, "position:y", 874.0, 2.6).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	band_sweep.tween_property(scan_band, "position:y", 300.0, 2.6).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)

	# 顶层甲板状态灯带：一排小舷灯（初始青色），链爆经过时逐点转红（重着色在 Timer 步进内完成）
	var deck_lights: Array[_GlowDot] = []
	for d_i in 12:
		var dl := _glow(3.5, Color(0.0, 0.83, 1.0, 0.8)) as _GlowDot
		dl.position = Vector2(220.0 + 134.0 * d_i, 348.0)
		root.add_child(dl)
		deck_lights.append(dl)

	# 链式能量路径：逐节点点亮（折线穿过各层甲板）；外发光 = 更宽更淡的叠加态底线
	var path := PackedVector2Array(
		[
			Vector2(200.0, 700.0),
			Vector2(500.0, 700.0),
			Vector2(500.0, 520.0),
			Vector2(900.0, 520.0),
			Vector2(900.0, 340.0),
			Vector2(1300.0, 340.0),
			Vector2(1300.0, 700.0),
			Vector2(1700.0, 700.0),
		]
	)
	var energy_glow := _line(PackedVector2Array(), Color(1.0, 0.4, 0.1, 0.3), 16.0)
	var glow_mat := CanvasItemMaterial.new()
	glow_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	energy_glow.material = glow_mat
	root.add_child(energy_glow)
	var energy := _line(PackedVector2Array(), Color(1.0, 0.45, 0.15), 6.0)
	root.add_child(energy)
	var step := [0]
	var shake_state: Array = [null]  # 每次引爆刷新一次颤动峰值（连锁震感）
	var timer := Timer.new()
	timer.wait_time = 0.2
	timer.autostart = true  # 构建期未入树，用 autostart（入树后自动启动）
	root.add_child(timer)
	timer.timeout.connect(
		func() -> void:
			if step[0] >= path.size():
				timer.stop()
				return
			var pos: Vector2 = path[step[0]]
			_kick_shake(root, 4.0, shake_state)  # 节点引爆：轻微画面颤动脉冲
			energy.add_point(pos)
			energy.width = 6.0 + step[0] * 1.5
			energy_glow.add_point(pos)
			energy_glow.width = 16.0 + step[0] * 3.0
			# 节点爆闪（软光晕）+ 一次性火花溅射（ textured 软点粒子，播完自毁）
			var flash := CinematicFx.soft_glow(36.0, Color(1.0, 0.55, 0.15, 0.9))
			flash.position = pos
			var flash_base := flash.scale
			flash.scale = Vector2.ZERO
			root.add_child(flash)
			var ft := root.create_tween()
			ft.tween_property(flash, "scale", flash_base * 1.4, 0.12)
			ft.tween_property(flash, "modulate:a", 0.0, 0.35)
			ft.tween_callback(flash.queue_free)
			var sparks := _particles(
				{
					"amount": 24,
					"lifetime": 0.4,
					"one_shot": true,
					"explosiveness": 0.9,
					"spread": 180.0,
					"vel_min": 120.0,
					"vel_max": 300.0,
					"damping_min": 80.0,
					"damping_max": 160.0,
					"scale_min": 2.0,
					"scale_max": 4.0,
					"color": Color(1.0, 0.6, 0.2, 0.95),
				}
			)
			sparks.position = pos
			root.add_child(sparks)
			sparks.finished.connect(sparks.queue_free)
			# 顶层甲板状态灯带：链爆波前经过处由青转红（步进内重着色，零逐帧开销）
			for dl in deck_lights:
				if dl.position.x <= pos.x:
					dl.dot_color = Color(1.0, 0.3, 0.2, 0.9)
					dl.queue_redraw()
			# 冲击波纹：节点爆闪处薄环扩大淡出（仿镜头 1 冲击波）
			var ripple_points := PackedVector2Array()
			for r_i in 16:
				var r_a := TAU * float(r_i) / 16.0
				ripple_points.append(Vector2(cos(r_a), sin(r_a)) * 14.0)
			var ripple := _line(ripple_points, Color(1.0, 0.6, 0.2, 0.6), 3.0)
			ripple.closed = true
			ripple.position = pos
			var ripple_mat := CanvasItemMaterial.new()
			ripple_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
			ripple.material = ripple_mat
			root.add_child(ripple)
			var rt := root.create_tween().set_parallel(true)
			rt.tween_property(ripple, "scale", Vector2.ONE * 3.2, 0.5).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
			rt.tween_property(ripple, "modulate:a", 0.0, 0.45)
			rt.chain().tween_callback(ripple.queue_free)
			if step[0] == 1 or step[0] == 3 or step[0] == 5:
				# 链式三连发：音量逐发递减
				GameState.play_sfx(GameState.SFX_EXPLOSION, -2.0 - 3.0 * (step[0] / 2) + AUDIO_VOL_OFFSET, AUDIO_PITCH)
			step[0] += 1
	)
	return root


# ---------------- 镜头 3：驾驶员冲刺（2.5s） ----------------


class _RunnerShot:
	extends Node2D
	var _scrollers: Array[Polygon2D] = []  # 警示条纹/墙肋/舱门框，反向滚动表现冲刺
	var _hip_pivots: Array[Node2D] = []  # 髋：大腿前后摆幅
	var _knee_pivots: Array[Node2D] = []  # 膝：摆动相屈膝、支撑相伸展
	var _shoulder_pivots: Array[Node2D] = []  # 肩：与对侧腿反相摆动
	var _elbow_pivots: Array[Node2D] = []  # 肘：保持弯曲微振
	var _bob_node: Node2D  # 人物整体随步频上下起伏
	var _bob_base_y := 0.0
	var _red: ColorRect  # 应急灯全屏红闪
	var _speed_lines: Array[Line2D] = []
	var _fg_struts: Array[Polygon2D] = []  # 前景近景支杆（比中景更快的反向视差，-1600px/s 回卷）
	var _t := 0.0

	func _process(delta: float) -> void:
		_t += delta
		for s in _scrollers:
			s.position.x -= 900.0 * delta
			if s.position.x < -160.0:
				s.position.x += 2240.0
		# 前景支杆：近景快速横扫（-1600px/s，快于中景 -900），出界回卷
		for fs in _fg_struts:
			fs.position.x -= 1600.0 * delta
			if fs.position.x < -200.0:
				fs.position.x += 2520.0
		# 两拍跑步循环：双腿反相、手臂与对侧腿反相、躯干 2 倍频起伏（就地写 rotation/position，零堆分配）
		# 关节符号约定（人物朝 +x）：正 rotation = 肢尖向 -x（后），负 = 向 +x（前）
		var run_phase := _t * 11.0
		for i in 2:
			var p := run_phase + PI * float(i)
			_hip_pivots[i].rotation = sin(p) * 0.72
			# 膝只向后弯：摆动相（p≈1.6..4.7）脚跟踢向臀部，触地前基本伸直
			_knee_pivots[i].rotation = 0.08 + maxf(0.0, sin(p - 1.8)) * 1.35
			_shoulder_pivots[i].rotation = 0.1 - sin(p) * 0.6
			# 肘只向前弯：前臂保持朝前（奔跑摆臂姿态）
			_elbow_pivots[i].rotation = -(1.0 + sin(p + 0.8) * 0.25)
		# bob 最低点对齐支撑相中点（腿在重心正下方），腾空相最高
		_bob_node.position.y = _bob_base_y + 2.6 * (0.5 + 0.5 * cos(run_phase * 2.0))
		_red.color.a = 0.08 + 0.08 * maxf(0.0, sin(_t * TAU * 6.0))  # 6Hz 呼吸闪烁
		for sl in _speed_lines:
			sl.position.x -= 2200.0 * delta
			if sl.position.x < -320.0:
				sl.position.x = 2200.0 + randf() * 400.0
				sl.position.y = randf() * 1080.0


## 侧视走廊：透视线 + 天花板管道/舱门框结构 + 黄色警示条纹反向滚动；
## 多段式飞行服驾驶员（骨盆/胸廓/头盔/维生背包/双关节四肢）+ 暖色边缘光 + 双层残影，两拍奔跑循环；
## 五条顶部锥形体积光带，前景近景支杆快速横扫（-1600px/s 强视差），红色应急灯 6Hz 闪烁，
## 蒸汽上飘，加密速度线，低频警报脉冲。
func _build_shot3() -> Node2D:
	var root := _RunnerShot.new()
	root.name = "Shot3"
	root.add_child(_bg_rect(Color(0.02, 0.02, 0.04)))
	# 天花板/地面透视带 + 向右侧灭点收敛的走廊线
	root.add_child(_line(PackedVector2Array([Vector2(0.0, 200.0), Vector2(1920.0, 430.0)]), Color(0.16, 0.22, 0.32, 0.7), 3.0))
	root.add_child(_line(PackedVector2Array([Vector2(0.0, 880.0), Vector2(1920.0, 650.0)]), Color(0.16, 0.22, 0.32, 0.7), 3.0))
	root.add_child(_line(PackedVector2Array([Vector2(0.0, 540.0), Vector2(1920.0, 540.0)]), Color(0.1, 0.14, 0.22, 0.5)))
	var ceil := Polygon2D.new()
	ceil.polygon = PackedVector2Array([Vector2(0.0, 0.0), Vector2(1920.0, 0.0), Vector2(1920.0, 430.0), Vector2(0.0, 200.0)])
	ceil.color = Color(0.05, 0.06, 0.09)
	root.add_child(ceil)
	var floor_poly := Polygon2D.new()
	floor_poly.polygon = PackedVector2Array([Vector2(0.0, 880.0), Vector2(1920.0, 650.0), Vector2(1920.0, 1080.0), Vector2(0.0, 1080.0)])
	floor_poly.color = Color(0.06, 0.07, 0.1)
	root.add_child(floor_poly)
	# 天花板管道：双管沿顶棚走向 + 管节环
	for pipe in [[150.0, 380.0, 8.0], [178.0, 408.0, 5.0]]:
		root.add_child(_line(PackedVector2Array([Vector2(0.0, pipe[0]), Vector2(1920.0, pipe[1])]), Color(0.2, 0.26, 0.36), pipe[2]))
	for i in 5:
		var joint := _GlowDot.new()
		joint.radius = 7.0
		joint.dot_color = Color(0.24, 0.3, 0.42)
		var jx := 200.0 + 400.0 * i
		joint.position = Vector2(jx, 150.0 + jx * 230.0 / 1920.0)
		root.add_child(joint)
	# 顶部体积光：五条锥形光带（叠加态，上窄下宽）
	for i in 5:
		var cone := Polygon2D.new()
		var cx := 320.0 + 320.0 * i
		cone.polygon = PackedVector2Array(
			[
				Vector2(cx - 50.0, 60.0),
				Vector2(cx + 50.0, 60.0),
				Vector2(cx + 170.0, 950.0),
				Vector2(cx - 170.0, 950.0),
			]
		)
		cone.color = Color(1.0, 0.85, 0.6, 0.05)
		var cone_mat := CanvasItemMaterial.new()
		cone_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
		cone.material = cone_mat
		root.add_child(cone)
	# 顶部旋转警灯光锥：红色叠加态，锚点往复扫掠
	for i in 2:
		var beacon := Polygon2D.new()
		beacon.polygon = PackedVector2Array([Vector2(0.0, 0.0), Vector2(-70.0, 760.0), Vector2(70.0, 760.0)])
		beacon.color = Color(1.0, 0.12, 0.1, 0.08)
		beacon.position = Vector2(640.0 + 640.0 * i, 40.0)
		var beacon_mat := CanvasItemMaterial.new()
		beacon_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
		beacon.material = beacon_mat
		root.add_child(beacon)
		var beacon_sweep := root.create_tween().set_loops()
		beacon_sweep.tween_property(beacon, "rotation", 0.55, 1.1).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
		beacon_sweep.tween_property(beacon, "rotation", -0.55, 1.1).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	# 黄色警示条纹（地面）+ 深色墙肋 + 舱门框：加入反向滚动列表
	for i in 14:
		var stripe := Polygon2D.new()
		stripe.polygon = PackedVector2Array(
			[
				Vector2(-40.0, 0.0),
				Vector2(-10.0, 0.0),
				Vector2(-26.0, 14.0),
				Vector2(-56.0, 14.0),
			]
		)
		stripe.color = Color(0.9, 0.75, 0.1, 0.75)
		stripe.position = Vector2(80.0 + 160.0 * i, 872.0)
		root.add_child(stripe)
		root._scrollers.append(stripe)
	for i in 9:
		var rib := _rect_poly(26.0, 420.0, Color(0.09, 0.11, 0.16))
		rib.position = Vector2(120.0 + 240.0 * i, 400.0)
		root.add_child(rib)
		root._scrollers.append(rib)
	for i in 3:
		var door_x := 400.0 + 720.0 * i
		var door := _rect_poly(150.0, 460.0, Color(0.13, 0.17, 0.24))
		door.position = Vector2(door_x, 420.0)
		root.add_child(door)
		root._scrollers.append(door)
		var door_inner := _rect_poly(118.0, 428.0, Color(0.05, 0.06, 0.09))
		door_inner.position = door.position
		root.add_child(door_inner)
		root._scrollers.append(door_inner)
	# 蒸汽：墙壁管道泄漏，白色半透明上飘
	var steam := _particles(
		{
			"amount": 32,
			"lifetime": 1.8,
			"vel_min": 50.0,
			"vel_max": 110.0,
			"spread": 30.0,
			"scale_min": 5.0,
			"scale_max": 11.0,
			"color": Color(0.9, 0.95, 1.0, 0.2),
			"additive": false,
		}
	)
	steam.position = Vector2(1500.0, 320.0)
	root.add_child(steam)
	# 驾驶员背光（叠加态暖光，把剪影从暗舱里托出来）
	var backlight := CinematicFx.soft_glow(170.0, Color(1.0, 0.6, 0.25, 0.16))
	backlight.position = Vector2(880.0, 520.0)
	root.add_child(backlight)
	# 驾驶员：多段式飞行服人物（骨盆/胸廓/头盔/维生背包/双关节四肢），两拍奔跑由 _process 相位驱动
	var body_color := Color(0.24, 0.3, 0.4)  # 近侧肢体
	var far_color := Color(0.14, 0.18, 0.26)  # 远侧肢体（深度层次）
	var edge_color := Color(0.55, 0.66, 0.84, 0.7)  # 分件边缘线
	var chest_points := PackedVector2Array([Vector2(-7.0, -14.0), Vector2(13.0, -16.0), Vector2(17.0, -42.0), Vector2(-3.0, -46.0)])
	# 双层残影（动态模糊）：胸廓/头盔淡影，拖在奔跑反方向
	for k in [2, 1]:
		var ghost := Node2D.new()
		ghost.position = Vector2(880.0 - 40.0 * k, 566.0 + 5.0 * k)
		ghost.scale = Vector2.ONE * 2.3
		ghost.rotation = 0.3
		ghost.modulate = Color(1.0, 1.0, 1.0, 0.06 + 0.05 * (2 - k))
		root.add_child(ghost)
		var g_torso := Polygon2D.new()
		g_torso.polygon = chest_points
		g_torso.color = body_color
		ghost.add_child(g_torso)
		var g_head := _GlowDot.new()
		g_head.radius = 10.5
		g_head.dot_color = body_color
		g_head.position = Vector2(11.0, -62.0)
		ghost.add_child(g_head)
	var pilot := Node2D.new()
	pilot.position = Vector2(880.0, 566.0)
	pilot.scale = Vector2.ONE * 2.3
	root.add_child(pilot)
	root._bob_node = pilot
	root._bob_base_y = pilot.position.y
	# 腿部（远侧先画）：髋→大腿→膝→小腿→飞行靴（靴底加厚线）
	for side_i in [1, 0]:
		var c := body_color if side_i == 0 else far_color
		var hip := Node2D.new()
		hip.position = Vector2(2.0 - 4.0 * side_i, -4.0 + 2.0 * side_i)
		pilot.add_child(hip)
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
		root._hip_pivots.append(hip)
		root._knee_pivots.append(knee)
	# 躯干组：绕骨盆前倾 0.3rad（胸廓/背包/头盔/手臂随体倾斜）
	var torso_grp := Node2D.new()
	torso_grp.rotation = 0.3
	pilot.add_child(torso_grp)
	var pelvis := Polygon2D.new()
	pelvis.polygon = PackedVector2Array([Vector2(-9.0, -2.0), Vector2(7.0, -4.0), Vector2(9.0, -14.0), Vector2(-7.0, -14.0)])
	pelvis.color = body_color
	torso_grp.add_child(pelvis)
	# 生命维持背包（背部方块结构 + 顶部管线 + 青色指示灯）与腰侧挂点
	var backpack := _rect_poly(12.0, 24.0, far_color)
	backpack.position = Vector2(-11.0, -30.0)
	torso_grp.add_child(backpack)
	torso_grp.add_child(_line(PackedVector2Array([Vector2(-11.0, -44.0), Vector2(-11.0, -50.0), Vector2(2.0, -54.0)]), edge_color, 1.4))
	var pack_light := _glow(2.0, Color(0.0, 0.83, 1.0, 0.8))
	pack_light.position = Vector2(-13.0, -24.0)
	torso_grp.add_child(pack_light)
	var pouch := _rect_poly(5.0, 7.0, far_color)
	pouch.position = Vector2(-9.0, -8.0)
	torso_grp.add_child(pouch)
	# 胸廓 + 胸包 + 前缘分件线 + 肩部护甲
	var chest := Polygon2D.new()
	chest.polygon = chest_points
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
	# 颈 + 头盔（面罩高光 + 暖色边缘光）
	var neck := _rect_poly(4.0, 7.0, body_color)
	neck.position = Vector2(8.0, -51.0)
	torso_grp.add_child(neck)
	var helmet := _GlowDot.new()
	helmet.radius = 10.5
	helmet.dot_color = body_color
	helmet.position = Vector2(11.0, -62.0)
	torso_grp.add_child(helmet)
	var helmet_rim := CinematicFx.soft_glow(12.0, Color(1.0, 0.6, 0.3, 0.3))
	helmet_rim.position = Vector2(13.0, -64.0)
	torso_grp.add_child(helmet_rim)
	var visor := _glow(3.5, Color(0.5, 0.9, 1.0, 0.8))
	visor.position = Vector2(19.0, -64.0)
	torso_grp.add_child(visor)
	# 躯干暖色边缘光（胸廓描边副本，叠加态微偏移）
	var rim_torso := Polygon2D.new()
	rim_torso.polygon = chest_points
	rim_torso.color = Color(1.0, 0.6, 0.3, 0.3)
	rim_torso.position = Vector2(2.0, -2.0)
	var rim_mat := CanvasItemMaterial.new()
	rim_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	rim_torso.material = rim_mat
	torso_grp.add_child(rim_torso)
	# 手臂（远侧先画）：肩→上臂→肘→前臂→手，与对侧腿反相摆动
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
		root._shoulder_pivots.append(shoulder)
		root._elbow_pivots.append(elbow)
	# 速度线（加密）
	for i in 14:
		var sl := _line(PackedVector2Array([Vector2.ZERO, Vector2(-160.0 - randf() * 140.0, 0.0)]), Color(0.8, 0.9, 1.0, 0.28), 2.0)
		sl.position = Vector2(randf() * 2200.0, randf() * 1080.0)
		root.add_child(sl)
		root._speed_lines.append(sl)
	# 前景近景支杆 ×4：宽斜边深色半透明立柱，-1600px/s 反向横扫回卷（近景强视差，在红闪之下受染色）
	for fg_i in 4:
		var fg := Polygon2D.new()
		fg.polygon = PackedVector2Array(
			[
				Vector2(-90.0, -620.0),
				Vector2(70.0, -620.0),
				Vector2(130.0, 620.0),
				Vector2(-30.0, 620.0),
			]
		)
		fg.color = Color(0.01, 0.015, 0.03, 0.6)
		fg.position = Vector2(300.0 + 630.0 * fg_i, 540.0)
		root.add_child(fg)
		root._fg_struts.append(fg)
	# 红色应急灯全屏闪烁
	var red := _bg_rect(Color(1.0, 0.1, 0.1, 0.0))
	root.add_child(red)
	root._red = red
	# 低频警报脉冲（既有命中音压低至 -14dB 基础，叠加过场音量策略，0.7s 间隔）
	var alarm := Timer.new()
	alarm.wait_time = 0.7
	alarm.autostart = true
	root.add_child(alarm)
	alarm.timeout.connect(func() -> void: GameState.play_sfx(GameState.SFX_PLAYER_HIT, -14.0 + AUDIO_VOL_OFFSET, AUDIO_PITCH))
	return root


# ---------------- 镜头 4：操作台紧急启动（2.5s） ----------------


class _ConsoleShot:
	extends Node2D
	var _hands: Array[Node2D] = []  # 双手剪影：倒计时前在按钮簇上快速点按
	var _targets: Array[Vector2] = []
	var _cells: Array[Polygon2D] = []  # 可点按按钮（闪烁重着色 + 双手目标池）
	var _handles: Array[Vector2] = []
	var _open_shapes: Array[Polygon2D] = []  # 张开手形（点按态）
	var _grip_shapes: Array[Polygon2D] = []  # 扣合手形（抓把手态）
	var _grabbing := false  # 倒计时结束：双手猛抓两侧把手
	var _retarget := [0.0, 0.15]
	var _press := [0.0, 0.0]  # 按下-抬起起伏剩余时长
	var _radar_sweep: Line2D  # 左副屏雷达扫掠针（rotation 随时间推进）
	var _radar_blips: Array[Sprite2D] = []  # 雷达回波亮点（扫过点亮、余晖衰减）
	var _radar_blip_angles := PackedFloat32Array()
	var _radar_blip_e := PackedFloat32Array()  # 回波余晖能量 0..1
	var _radar_angle := 0.0

	func _process(delta: float) -> void:
		for h in _hands.size():
			var hand := _hands[h]
			if _grabbing:
				hand.position = hand.position.lerp(_handles[h], 9.0 * delta)
			else:
				_retarget[h] -= delta
				if _retarget[h] <= 0.0:
					_retarget[h] = randf_range(0.12, 0.28)
					_targets[h] = (_cells[randi() % _cells.size()] as Polygon2D).position
				# 到位后触发一次按下-抬起起伏（指尖压向台面再弹回）
				if _press[h] <= 0.0 and hand.position.distance_squared_to(_targets[h]) < 100.0:
					_press[h] = 0.16
				var dip := 0.0
				if _press[h] > 0.0:
					_press[h] -= delta
					dip = 5.0 * sin(PI * clampf(1.0 - _press[h] / 0.16, 0.0, 1.0))
				hand.position = hand.position.lerp(_targets[h] + Vector2(0.0, dip), 16.0 * delta)
		# 雷达：扫掠针匀速旋转；扫过回波角时点亮，余晖按 0.7/s 衰减（预计算角度，零堆分配）
		_radar_angle = wrapf(_radar_angle + delta * 3.6, 0.0, TAU)
		_radar_sweep.rotation = _radar_angle
		for b in _radar_blips.size():
			var b_diff: float = absf(wrapf(_radar_angle - _radar_blip_angles[b] + PI, 0.0, TAU) - PI)
			if b_diff < 0.2:
				_radar_blip_e[b] = 1.0
			else:
				_radar_blip_e[b] = maxf(0.0, _radar_blip_e[b] - delta * 0.7)
			_radar_blips[b].modulate.a = 0.12 + 0.88 * _radar_blip_e[b]


## 驾驶舱前景框架（含舱壁缝线）+ 两侧仪表副屏（玻璃高光；左副屏带雷达距圈/扫掠针/回波亮点）+ 三分区航电控制台
## （斜切梯形台体：推进区按钮簇+节流阀滑槽 / 导航区旋钮+按钮排 / 武器区拨杆开关，LED 指示排+分区铭牌）+
## 4 指+拇指手形剪影带按下起伏地点按 + 主屏红色倒计时 3→2→1（bezel 边框）与警告行闪烁 + 两侧金属把手；
## 倒计时结束五指扣合把手，结尾 0.5s 整体后仰 -3° + 短促震动 + 屏幕白光渐强。
func _build_shot4() -> Node2D:
	var dur: float = _shot_durations[3]
	var root := _ConsoleShot.new()
	root.name = "Shot4"
	root.add_child(_bg_rect(Color(0.02, 0.03, 0.05)))
	# 舱体前景框架（左右楔 + 顶梁）
	var frame_color := Color(0.05, 0.06, 0.09)
	var left_wedge := Polygon2D.new()
	left_wedge.polygon = PackedVector2Array([Vector2(0.0, 0.0), Vector2(280.0, 0.0), Vector2(180.0, 1080.0), Vector2(0.0, 1080.0)])
	left_wedge.color = frame_color
	root.add_child(left_wedge)
	var right_wedge := Polygon2D.new()
	right_wedge.polygon = PackedVector2Array([Vector2(1920.0, 0.0), Vector2(1640.0, 0.0), Vector2(1740.0, 1080.0), Vector2(1920.0, 1080.0)])
	right_wedge.color = frame_color
	root.add_child(right_wedge)
	var top_beam := Polygon2D.new()
	top_beam.polygon = PackedVector2Array([Vector2(0.0, 0.0), Vector2(1920.0, 0.0), Vector2(1920.0, 120.0), Vector2(0.0, 170.0)])
	top_beam.color = frame_color
	root.add_child(top_beam)
	# 舱壁缝线（框架上的细结构线，增加舱内细节密度）
	var seam_color := Color(0.14, 0.17, 0.24)
	for i in 4:
		root.add_child(
			_line(
				PackedVector2Array([Vector2(40.0 + 50.0 * i, 200.0 + 180.0 * i), Vector2(210.0 - 20.0 * i, 240.0 + 180.0 * i)]),
				seam_color,
				1.5
			)
		)
		root.add_child(
			_line(
				PackedVector2Array([Vector2(1880.0 - 50.0 * i, 200.0 + 180.0 * i), Vector2(1710.0 + 20.0 * i, 240.0 + 180.0 * i)]),
				seam_color,
				1.5
			)
		)
	root.add_child(_line(PackedVector2Array([Vector2(0.0, 150.0), Vector2(1920.0, 105.0)]), seam_color, 1.5))
	# 顶梁青色灯带：宽淡底光 + 窄亮芯
	var strip_glow := _line(PackedVector2Array([Vector2(280.0, 138.0), Vector2(1640.0, 112.0)]), Color(0.0, 0.83, 1.0, 0.12), 8.0)
	var strip_mat := CanvasItemMaterial.new()
	strip_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	strip_glow.material = strip_mat
	root.add_child(strip_glow)
	root.add_child(_line(PackedVector2Array([Vector2(280.0, 138.0), Vector2(1640.0, 112.0)]), Color(0.0, 0.83, 1.0, 0.35), 2.5))
	# 两侧仪表副屏：青色小屏 + 迷你波形/柱状线
	for side in [[330.0, 250.0], [1390.0, 250.0]]:
		var sub_screen := _rect_poly(200.0, 130.0, Color(0.02, 0.1, 0.14))
		sub_screen.position = Vector2(side[0] + 100.0, side[1] + 65.0)
		root.add_child(sub_screen)
		var sub_border := _line(
			PackedVector2Array(
				[
					Vector2(side[0], side[1]),
					Vector2(side[0] + 200.0, side[1]),
					Vector2(side[0] + 200.0, side[1] + 130.0),
					Vector2(side[0], side[1] + 130.0),
				]
			),
			UITheme.ACCENT_DIM,
			2.0
		)
		sub_border.closed = true
		root.add_child(sub_border)
		# 副屏玻璃高光斜纹
		var sub_glass := Polygon2D.new()
		sub_glass.polygon = PackedVector2Array(
			[
				Vector2(side[0] + 40.0, side[1]),
				Vector2(side[0] + 80.0, side[1]),
				Vector2(side[0] + 36.0, side[1] + 130.0),
				Vector2(side[0] + 6.0, side[1] + 130.0),
			]
		)
		sub_glass.color = Color(1.0, 1.0, 1.0, 0.05)
		var sub_glass_mat := CanvasItemMaterial.new()
		sub_glass_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
		sub_glass.material = sub_glass_mat
		root.add_child(sub_glass)
		var wave_points := PackedVector2Array()
		for w in 9:
			wave_points.append(Vector2(side[0] + 18.0 + 20.0 * w, side[1] + 65.0 + sin(float(w) * 1.4) * 32.0))
		root.add_child(_line(wave_points, Color(0.0, 0.83, 1.0, 0.7), 2.0))
		for bar in 4:
			var b := _rect_poly(14.0, 20.0 + 14.0 * bar, Color(0.0, 0.83, 1.0, 0.4))
			b.position = Vector2(side[0] + 30.0 + 30.0 * bar, side[1] + 118.0 - 7.0 * bar)
			root.add_child(b)
	# 左副屏雷达：静态距圈 + 旋转扫掠针（叠加态）+ 2 枚回波亮点（扫过点亮，_ConsoleShot._process 驱动）
	var radar_c := Vector2(430.0, 315.0)
	var radar_ring_pts := PackedVector2Array()
	for rr in 24:
		var ra := TAU * float(rr) / 24.0
		radar_ring_pts.append(radar_c + Vector2(cos(ra), sin(ra)) * 52.0)
	var radar_ring := _line(radar_ring_pts, Color(0.0, 0.83, 1.0, 0.3), 1.5)
	radar_ring.closed = true
	root.add_child(radar_ring)
	var sweep := _line(PackedVector2Array([Vector2.ZERO, Vector2(52.0, 0.0)]), Color(0.4, 0.95, 1.0, 0.8), 2.5)
	sweep.position = radar_c
	var sweep_mat := CanvasItemMaterial.new()
	sweep_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	sweep.material = sweep_mat
	root.add_child(sweep)
	root._radar_sweep = sweep
	for b_a in [0.9, 3.8]:
		var blip := CinematicFx.soft_glow(4.0, Color(0.5, 1.0, 1.0, 0.9))
		blip.position = radar_c + Vector2(cos(b_a), sin(b_a)) * 34.0
		root.add_child(blip)
		root._radar_blips.append(blip)
		root._radar_blip_angles.append(b_a)
		root._radar_blip_e.append(0.0)
	# 控制台：斜切梯形台体 + 三功能分区（推进/导航/武器），控制件成组分布（构图避开底部 letterbox）
	var console_body := Polygon2D.new()
	console_body.polygon = PackedVector2Array(
		[Vector2(320.0, 700.0), Vector2(1600.0, 700.0), Vector2(1700.0, 1080.0), Vector2(220.0, 1080.0)]
	)
	console_body.color = Color(0.08, 0.1, 0.14)
	root.add_child(console_body)
	# 台面沿口高光 + 侧棱线 + 台面缝线
	root.add_child(_line(PackedVector2Array([Vector2(320.0, 700.0), Vector2(1600.0, 700.0)]), UITheme.PANEL_BORDER, 2.0))
	root.add_child(_line(PackedVector2Array([Vector2(320.0, 700.0), Vector2(220.0, 1080.0)]), Color(0.14, 0.17, 0.24), 1.5))
	root.add_child(_line(PackedVector2Array([Vector2(1600.0, 700.0), Vector2(1700.0, 1080.0)]), Color(0.14, 0.17, 0.24), 1.5))
	root.add_child(_line(PackedVector2Array([Vector2(340.0, 1000.0), Vector2(1580.0, 1000.0)]), Color(0.14, 0.17, 0.24), 1.5))
	# 分区隔线（随台体透视微斜）
	for zx in [770.0, 1150.0]:
		root.add_child(_line(PackedVector2Array([Vector2(zx, 720.0), Vector2(zx - 20.0, 1000.0)]), Color(0.18, 0.22, 0.3), 1.5))
	# 分区铭牌条：暗底 + 顶部 accent 细线 + 小字
	for zd in [[450.0, "INTRO_ZONE_PROP"], [960.0, "INTRO_ZONE_NAV"], [1380.0, "INTRO_ZONE_WPN"]]:
		var plate := _rect_poly(110.0, 22.0, Color(0.03, 0.12, 0.16))
		plate.position = Vector2(zd[0], 737.0)
		root.add_child(plate)
		root.add_child(
			_line(PackedVector2Array([Vector2(zd[0] - 55.0, 726.0), Vector2(zd[0] + 55.0, 726.0)]), Color(0.0, 0.83, 1.0, 0.4), 1.6)
		)
		var plate_label := UITheme.make_label(tr(zd[1]), UITheme.FONT_CAPTION, UITheme.ACCENT)
		plate_label.add_theme_font_size_override("font_size", 15)
		plate_label.position = Vector2(zd[0] - 55.0, 726.0)
		plate_label.size = Vector2(110.0, 22.0)
		plate_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		root.add_child(plate_label)
	# LED 指示排（每区一排，青/红交替）
	for lz in [400.0, 900.0, 1250.0]:
		for l_i in 5:
			var led := _GlowDot.new()
			led.radius = 3.0
			led.dot_color = (Color(0.0, 0.83, 1.0, 0.85) if l_i % 2 == 0 else Color(0.9, 0.2, 0.25, 0.85))
			led.position = Vector2(lz + 14.0 * l_i, 766.0)
			root.add_child(led)
	var cells: Array[Polygon2D] = []
	# 按钮簇：背板 + rows×cols 小按钮（闪烁重着色，也是双手点按目标池）+ 板角状态 LED
	var cluster := func(cx: float, cy: float, cols: int, rows: int) -> void:
		var plate := _rect_poly(28.0 * cols + 16.0, 26.0 * rows + 14.0, Color(0.04, 0.05, 0.08))
		plate.position = Vector2(cx, cy)
		root.add_child(plate)
		for row in rows:
			for col in cols:
				var btn := _rect_poly(22.0, 18.0, Color(0.0, 0.3, 0.4, 0.5))
				btn.position = Vector2(cx - 14.0 * (cols - 1) + 28.0 * col, cy - 13.0 * (rows - 1) + 26.0 * row)
				root.add_child(btn)
				cells.append(btn)
		for led_i in 2:  # 簇板顶边两角状态灯（静态，青/红各一）
			var cl_led := _GlowDot.new()
			cl_led.radius = 2.5
			cl_led.dot_color = (Color(0.0, 0.83, 1.0, 0.9) if led_i == 0 else Color(0.9, 0.2, 0.25, 0.9))
			cl_led.position = Vector2(cx + 14.0 * cols * (-1.0 + 2.0 * led_i), cy - 13.0 * rows)
			root.add_child(cl_led)
	cluster.call(450.0, 855.0, 4, 2)  # 推进区按钮簇
	cluster.call(960.0, 950.0, 4, 1)  # 导航区按钮排
	cluster.call(1445.0, 855.0, 2, 2)  # 武器区按钮簇
	root._cells = cells
	# 推进区：双节流阀滑槽（轨道 + 刻度 + 手柄 + 红色标识线）
	for s_i in 2:
		var sx := 640.0 + 60.0 * s_i
		root.add_child(_line(PackedVector2Array([Vector2(sx, 790.0), Vector2(sx, 930.0)]), Color(0.2, 0.25, 0.34), 4.0))
		for tick in 4:
			root.add_child(
				_line(
					PackedVector2Array([Vector2(sx + 8.0, 802.0 + 32.0 * tick), Vector2(sx + 16.0, 802.0 + 32.0 * tick)]),
					Color(0.3, 0.36, 0.46, 0.6),
					1.2
				)
			)
		var handle := _rect_poly(22.0, 12.0, Color(0.5, 0.55, 0.62))
		handle.position = Vector2(sx, 830.0 + 44.0 * s_i)
		root.add_child(handle)
		root.add_child(
			_line(PackedVector2Array([Vector2(sx - 8.0, 830.0 + 44.0 * s_i), Vector2(sx + 8.0, 830.0 + 44.0 * s_i)]), UITheme.DANGER, 2.0)
		)
	# 导航区：双旋钮（底座圆 + 刻度环 + 指针）
	for k_i in 2:
		var kx := 880.0 + 160.0 * k_i
		var knob := _GlowDot.new()
		knob.radius = 22.0
		knob.dot_color = Color(0.05, 0.07, 0.1)
		knob.position = Vector2(kx, 855.0)
		root.add_child(knob)
		var knob_ring_points := PackedVector2Array()
		for p_i in 20:
			var p_a := TAU * float(p_i) / 20.0
			knob_ring_points.append(Vector2(kx + cos(p_a) * 27.0, 855.0 + sin(p_a) * 27.0))
		var knob_ring := _line(knob_ring_points, Color(0.3, 0.38, 0.5), 2.0)
		knob_ring.closed = true
		root.add_child(knob_ring)
		var pointer_a := -0.6 + 1.9 * k_i
		root.add_child(
			_line(
				PackedVector2Array([Vector2(kx, 855.0), Vector2(kx + cos(pointer_a) * 19.0, 855.0 + sin(pointer_a) * 19.0)]),
				UITheme.ACCENT,
				3.0
			)
		)
	# 武器区：三拨杆开关（槽位 + 拨杆 + 状态灯头）
	for t_i in 3:
		var tx := 1230.0 + 70.0 * t_i
		var slot := _rect_poly(10.0, 34.0, Color(0.03, 0.04, 0.06))
		slot.position = Vector2(tx, 855.0)
		root.add_child(slot)
		var lever_up := t_i != 1
		var tip_y := 843.0 if lever_up else 867.0
		root.add_child(_line(PackedVector2Array([Vector2(tx, 855.0), Vector2(tx + 6.0, tip_y)]), Color(0.6, 0.65, 0.72), 4.0))
		var tip := _glow(3.0, Color(0.9, 0.2, 0.25, 0.9) if lever_up else Color(0.0, 0.83, 1.0, 0.9))
		tip.position = Vector2(tx + 6.0, tip_y)
		root.add_child(tip)
	var blink := Timer.new()
	blink.wait_time = 0.09
	blink.autostart = true  # 构建期未入树，用 autostart
	root.add_child(blink)
	blink.timeout.connect(
		func() -> void:
			var palette := [
				Color(0.0, 0.83, 1.0, 0.9),
				Color(1.0, 0.6, 0.15, 0.9),
				Color(0.0, 0.3, 0.4, 0.4),
				Color(0.9, 0.2, 0.25, 0.9),
			]
			for k in 6:
				(cells[randi() % cells.size()] as Polygon2D).color = palette[randi() % palette.size()]
	)
	# 主屏：bezel 边框底板 + 红底倒计时 + 进度环/扫描弧 + 警告行闪烁 + 滚动状态日志
	var bezel := _rect_poly(560.0, 360.0, Color(0.04, 0.05, 0.08))
	bezel.position = Vector2(960.0, 380.0)
	root.add_child(bezel)
	var screen := _rect_poly(520.0, 320.0, Color(0.15, 0.03, 0.05))
	screen.position = Vector2(960.0, 380.0)
	root.add_child(screen)
	var screen_border := _line(
		PackedVector2Array(
			[
				Vector2(700.0, 220.0),
				Vector2(1220.0, 220.0),
				Vector2(1220.0, 540.0),
				Vector2(700.0, 540.0),
			]
		),
		UITheme.DANGER,
		3.0
	)
	screen_border.closed = true
	root.add_child(screen_border)
	# 主屏玻璃高光斜纹
	var glass := Polygon2D.new()
	glass.polygon = PackedVector2Array([Vector2(780.0, 220.0), Vector2(890.0, 220.0), Vector2(800.0, 540.0), Vector2(710.0, 540.0)])
	glass.color = Color(1.0, 1.0, 1.0, 0.05)
	var glass_mat := CanvasItemMaterial.new()
	glass_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	glass.material = glass_mat
	root.add_child(glass)
	# 倒计时外圈：静态进度环 + 每秒一圈的红色扫描弧
	var ring_points := PackedVector2Array()
	for r_i in 48:
		var r_a := TAU * float(r_i) / 48.0
		ring_points.append(Vector2(960.0 + cos(r_a) * 95.0, 312.0 + sin(r_a) * 95.0))
	var cd_ring := _line(ring_points, Color(1.0, 0.25, 0.25, 0.35), 3.0)
	cd_ring.closed = true
	root.add_child(cd_ring)
	var arc_points := PackedVector2Array()
	for a_i in 12:
		var a_a := -PI * 0.5 + 1.0 * float(a_i) / 12.0
		arc_points.append(Vector2(cos(a_a) * 95.0, sin(a_a) * 95.0))
	var cd_arc := _line(arc_points, Color(1.0, 0.45, 0.4, 0.9), 5.0)
	cd_arc.position = Vector2(960.0, 312.0)
	root.add_child(cd_arc)
	var arc_sweep := root.create_tween().set_loops()
	arc_sweep.tween_property(cd_arc, "rotation", TAU, 0.6).set_trans(Tween.TRANS_LINEAR)
	var countdown := UITheme.make_label("3", UITheme.FONT_DISPLAY, UITheme.DANGER)
	countdown.position = Vector2(860.0, 252.0)
	countdown.size = Vector2(200.0, 120.0)
	countdown.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	root.add_child(countdown)
	var warning := UITheme.make_label(tr("INTRO_WARNING"), UITheme.FONT_HEADER, UITheme.DANGER)
	warning.position = Vector2(760.0, 395.0)
	warning.size = Vector2(400.0, 44.0)
	warning.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	root.add_child(warning)
	# 滚动状态日志：INTRO_LOG_1..4 四条 i18n 键，Timer 回调只换 text（零逐帧分配）
	var log_lines: Array[Label] = []
	for li in 3:
		var log_line := UITheme.make_label(tr("INTRO_LOG_%d" % (li + 1)), UITheme.FONT_CAPTION, Color(1.0, 0.55, 0.5))
		log_line.position = Vector2(720.0, 452.0 + 26.0 * li)
		log_line.size = Vector2(480.0, 24.0)
		root.add_child(log_line)
		log_lines.append(log_line)
	var remain := [3]
	var count_timer := Timer.new()
	count_timer.wait_time = 0.6
	count_timer.autostart = true
	root.add_child(count_timer)
	count_timer.timeout.connect(
		func() -> void:
			remain[0] -= 1
			if remain[0] <= 0:
				count_timer.stop()
				countdown.text = "!"
			else:
				countdown.text = str(remain[0])
	)
	var warn_blink := Timer.new()
	warn_blink.wait_time = 0.4
	warn_blink.autostart = true
	root.add_child(warn_blink)
	warn_blink.timeout.connect(func() -> void: warning.visible = not warning.visible)
	# 日志轮换：0.7s 步进，INTRO_LOG_1..4 四条键在三行间滚动
	var log_step := [0]
	var log_timer := Timer.new()
	log_timer.wait_time = 0.7
	log_timer.autostart = true
	root.add_child(log_timer)
	log_timer.timeout.connect(
		func() -> void:
			log_step[0] = (log_step[0] + 1) % 4
			for li in 3:
				log_lines[li].text = tr("INTRO_LOG_%d" % ((log_step[0] + li) % 4 + 1))
	)
	# 两侧金属把手
	var handle_l := _rect_poly(36.0, 160.0, Color(0.5, 0.55, 0.62))
	handle_l.position = Vector2(380.0, 560.0)
	root.add_child(handle_l)
	var handle_r := _rect_poly(36.0, 160.0, Color(0.5, 0.55, 0.62))
	handle_r.position = Vector2(1540.0, 560.0)
	root.add_child(handle_r)
	root._handles = [handle_l.position, handle_r.position]
	# 双手：4 指+拇指手形剪影（前臂自下方伸入），点按带按下-抬起起伏；结尾五指扣合把手
	for h in 2:
		var hand := Node2D.new()
		hand.position = Vector2(700.0 + 500.0 * h, 940.0)
		# 前臂（斜向下方伸出画面）
		var forearm := _rect_poly(18.0, 260.0, Color(0.16, 0.2, 0.28))
		forearm.position = Vector2(-40.0 + 80.0 * h, 130.0)
		forearm.rotation = 0.35 - 0.7 * h
		hand.add_child(forearm)
		# 张开手形：腕→掌背→4 指节阶梯→拇指侧缘
		var open_shape := Polygon2D.new()
		open_shape.polygon = PackedVector2Array(
			[
				Vector2(-9.0, 12.0),
				Vector2(-10.0, -6.0),
				Vector2(-8.0, -18.0),
				Vector2(-5.0, -20.0),
				Vector2(-4.0, -10.0),
				Vector2(-1.0, -22.0),
				Vector2(0.0, -11.0),
				Vector2(3.0, -23.0),
				Vector2(4.0, -11.0),
				Vector2(7.0, -20.0),
				Vector2(8.0, -6.0),
				Vector2(13.0, -1.0),
				Vector2(11.0, 5.0),
				Vector2(7.0, 6.0),
				Vector2(9.0, 12.0),
			]
		)
		open_shape.color = Color(0.2, 0.25, 0.34)
		hand.add_child(open_shape)
		# 扣合手形：握拳剪影（指节凹槽线朝把手外侧，初始隐藏）
		var grip_shape := Polygon2D.new()
		grip_shape.polygon = PackedVector2Array(
			[
				Vector2(-14.0, -26.0),
				Vector2(14.0, -26.0),
				Vector2(22.0, -18.0),
				Vector2(22.0, 18.0),
				Vector2(14.0, 26.0),
				Vector2(-14.0, 26.0),
				Vector2(-22.0, 18.0),
				Vector2(-22.0, -18.0),
			]
		)
		grip_shape.color = open_shape.color
		var groove_sign := -1.0 + 2.0 * h  # 左手凹槽在 -x 侧，右手镜像
		for g_i in 3:
			var groove := _line(
				PackedVector2Array([Vector2(groove_sign * 22.0, -10.0 + 10.0 * g_i), Vector2(groove_sign * 9.0, -10.0 + 10.0 * g_i)]),
				Color(0.1, 0.13, 0.19),
				2.0
			)
			grip_shape.add_child(groove)
		grip_shape.visible = false
		hand.add_child(grip_shape)
		var palm_rim := CinematicFx.soft_glow(15.0, Color(0.0, 0.83, 1.0, 0.15))
		hand.add_child(palm_rim)
		root.add_child(hand)
		root._hands.append(hand)
		root._targets.append(hand.position)
		root._open_shapes.append(open_shape)
		root._grip_shapes.append(grip_shape)
	# 结尾 0.5s：双手抓把手 + 整体后仰 + 短促震动 + 屏幕白光渐强
	var white := _bg_rect(Color(1.0, 1.0, 1.0, 0.0))
	root.add_child(white)
	var end_timer := Timer.new()
	end_timer.one_shot = true
	end_timer.wait_time = maxf(dur - 0.5, 0.1)
	end_timer.autostart = true
	root.add_child(end_timer)
	end_timer.timeout.connect(
		func() -> void:
			root._grabbing = true
			for h_i in 2:
				root._open_shapes[h_i].visible = false
				root._grip_shapes[h_i].visible = true  # 换握拳剪影扣上把手（不动前臂朝向）
			var tween := root.create_tween().set_parallel(true)
			tween.tween_property(root, "rotation", deg_to_rad(-3.0), 0.5)
			tween.tween_property(white, "color:a", 0.9, 0.5)
			# 顿悟瞬间的短促震动：±5px 快速抖动 6 次
			var shake := root.create_tween()
			for s_i in 6:
				shake.tween_property(root, "position", Vector2(randf_range(-5.0, 5.0), randf_range(-5.0, 5.0)), 0.06)
			shake.tween_property(root, "position", Vector2.ZERO, 0.08)
	)
	return root


# ---------------- 镜头 5：弹射尾追视角（2.8s） ----------------


class _ChaseShot:
	extends Node2D
	var _shake_root: Node2D
	var _struts: Array[Array] = []  # [Line2D, side(0=左壁 1=右壁)]：壁面斜向结构线，透视收缩向后流动
	var _wall_lights: Array[Array] = []  # [_GlowDot, side]：壁面防撞灯，随结构线同款透视滚动
	var _speed_lines: Array[Line2D] = []
	var _edge_lines: Array[Array] = []  # [Line2D, side_sign]：边缘放射速度线，回卷保持同侧
	var _rail_speed := 400.0

	func _process(delta: float) -> void:
		_shake_root.position = Vector2(randf_range(-6.0, 6.0), randf_range(-6.0, 6.0))
		_rail_speed += 2600.0 * delta
		for pair in _struts:
			var strut: Line2D = pair[0]
			var y: float = strut.points[0].y + _rail_speed * delta
			if y > 1200.0:
				y -= 1560.0
			# 透视：越远（靠上）壁面越窄，结构线随之收缩
			var ty: float = clampf((y + 50.0) / 1180.0, 0.0, 1.2)
			var inner_x: float
			var outer_x: float
			if pair[1] == 0:
				inner_x = lerpf(700.0, 620.0, ty)
				outer_x = lerpf(430.0, 250.0, ty)
			else:
				inner_x = lerpf(1220.0, 1300.0, ty)
				outer_x = lerpf(1490.0, 1670.0, ty)
			# C28：创建时已预分配 2 点，set_point_position 原地写（points[i]= 是值语义副本不生效）
			strut.set_point_position(0, Vector2(inner_x, y))
			strut.set_point_position(1, Vector2(outer_x, y + 70.0))
		for lamp_pair in _wall_lights:
			var lamp: _GlowDot = lamp_pair[0]
			var ly: float = lamp.position.y + _rail_speed * delta
			if ly > 1200.0:
				ly -= 1560.0
			var lty: float = clampf((ly + 50.0) / 1180.0, 0.0, 1.2)
			# 灯点贴在壁面中线，随透视收缩
			if lamp_pair[1] == 0:
				lamp.position.x = lerpf(565.0, 435.0, lty)
			else:
				lamp.position.x = lerpf(1355.0, 1485.0, lty)
			lamp.position.y = ly
		for sl in _speed_lines:
			sl.position.y += 2600.0 * delta
			if sl.position.y > 1300.0:
				sl.position.y = -200.0 - randf() * 300.0
				sl.position.x = randf() * 1920.0
		for edge_pair in _edge_lines:
			var el: Line2D = edge_pair[0]
			el.position.y += 2600.0 * delta
			if el.position.y > 1300.0:
				el.position.y = -260.0
				el.position.x = randf() * 230.0 if edge_pair[1] < 0.0 else 1690.0 + randf() * 230.0


## 尾部视角：玩家机贴图置于画面中心偏下（调暗 + 暖色软轮廓光，对齐镜头 6 剪影光比）+ 双层橙色拖影（动态模糊）；
## 点火预热 ~0.3s（尾焰/喷口辉光从 0 升起 + 白闪脉冲，时轴随镜头时长缩放）；
## 轨道 = 两侧透视收缩的壁面 + 斜向结构线加速向后流动（取代横档「梯子」）；
## 尾部双火焰 + 亮白内芯 + 机身两侧舔舐火焰舌 + 双壁轨道电火花；全屏速度线 + 容器 ±6px 震动；引擎音持续。
func _build_shot5() -> Node2D:
	var dur: float = _shot_durations[4]
	var root := _ChaseShot.new()
	root.name = "Shot5"
	var shake_root := Node2D.new()
	root.add_child(shake_root)
	root._shake_root = shake_root
	shake_root.add_child(_bg_rect(Color(0.02, 0.02, 0.05)))
	# 轨道壁面：两侧梯形壁板（透视向顶部收缩）+ 内外棱线
	var wall_color := Color(0.09, 0.11, 0.15)
	var left_wall := Polygon2D.new()
	left_wall.polygon = PackedVector2Array([Vector2(430.0, -50.0), Vector2(700.0, -50.0), Vector2(620.0, 1130.0), Vector2(250.0, 1130.0)])
	left_wall.color = wall_color
	shake_root.add_child(left_wall)
	var right_wall := Polygon2D.new()
	right_wall.polygon = PackedVector2Array(
		[Vector2(1220.0, -50.0), Vector2(1490.0, -50.0), Vector2(1670.0, 1130.0), Vector2(1300.0, 1130.0)]
	)
	right_wall.color = wall_color
	shake_root.add_child(right_wall)
	var edge_color := Color(0.35, 0.42, 0.55)
	shake_root.add_child(_line(PackedVector2Array([Vector2(700.0, -50.0), Vector2(620.0, 1130.0)]), edge_color, 4.0))
	shake_root.add_child(_line(PackedVector2Array([Vector2(430.0, -50.0), Vector2(250.0, 1130.0)]), Color(edge_color, 0.5), 3.0))
	shake_root.add_child(_line(PackedVector2Array([Vector2(1220.0, -50.0), Vector2(1300.0, 1130.0)]), edge_color, 4.0))
	shake_root.add_child(_line(PackedVector2Array([Vector2(1490.0, -50.0), Vector2(1670.0, 1130.0)]), Color(edge_color, 0.5), 3.0))
	# 壁面斜向结构线：加速度向画面下方流动（初始等距铺满）
	for side in [0, 1]:
		for i in 7:
			var strut := _line(PackedVector2Array([Vector2.ZERO, Vector2.ONE]), Color(0.25, 0.31, 0.42), 5.0)
			var y := -320.0 + 210.0 * i
			strut.points = PackedVector2Array([Vector2(0.0, y), Vector2(1.0, y)])
			shake_root.add_child(strut)
			root._struts.append([strut, side])
	# 壁面防撞灯点流：红/青交替，随结构线同款透视向后流动（数组预建，_process 零分配）
	for side in [0, 1]:
		for i in 6:
			var lamp := _GlowDot.new()
			lamp.radius = 5.0
			lamp.dot_color = (Color(1.0, 0.35, 0.2, 0.85) if i % 2 == 0 else Color(0.0, 0.83, 1.0, 0.85))
			var lamp_mat := CanvasItemMaterial.new()
			lamp_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
			lamp.material = lamp_mat
			lamp.position = Vector2(0.0, -320.0 + 210.0 * i)
			shake_root.add_child(lamp)
			root._wall_lights.append([lamp, side])
	# 机身暖色软轮廓光（托底背光，对齐镜头 6 剪影光比；在拖影/机身之下）
	var ship_rim := CinematicFx.soft_glow(120.0, Color(1.0, 0.5, 0.2, 0.2))
	ship_rim.position = Vector2(960.0, 600.0)
	shake_root.add_child(ship_rim)
	# 战机双层拖影（动态模糊：橙色淡影拖在机尾方向）
	for k in [2, 1]:
		var ghost_ship := Sprite2D.new()
		ghost_ship.texture = PLAYER_SHIP
		ghost_ship.scale = Vector2.ONE * 1.4
		ghost_ship.position = Vector2(960.0, 560.0 + 30.0 * k)
		ghost_ship.modulate = Color(1.0, 0.6, 0.3, 0.05 + 0.05 * (2 - k))
		shake_root.add_child(ghost_ship)
	# 战机（尾部视角：机头朝远方/画面上方，发动机喷口朝向镜头；整体调暗对齐镜头 6 剪影调性）
	var ship := Sprite2D.new()
	ship.texture = PLAYER_SHIP
	ship.scale = Vector2.ONE * 1.4
	ship.position = Vector2(960.0, 560.0)
	ship.modulate = Color(0.85, 0.85, 0.92)
	shake_root.add_child(ship)
	# 尾部主火焰（橙红、短寿命、向下拖尾；软点贴图边缘衰减，尺寸加大一档补偿）；点火预热期由 amount_ratio 0→1 升起
	var engines: Array[GPUParticles2D] = []
	for side in [-46.0, 46.0]:
		var flame := _particles(
			{
				"amount": 40,
				"lifetime": 0.35,
				"direction": Vector3(0.0, 1.0, 0.0),
				"spread": 18.0,
				"vel_min": 380.0,
				"vel_max": 560.0,
				"scale_min": 6.0,
				"scale_max": 12.0,
				"color": Color(1.0, 0.45, 0.1, 0.95),
			}
		)
		flame.position = Vector2(960.0 + side, 640.0)
		shake_root.add_child(flame)
		engines.append(flame)
	# 亮白内芯：叠在橙红外焰内，更短射程、更高温度感
	for side in [-46.0, 46.0]:
		var core_flame := _particles(
			{
				"amount": 24,
				"lifetime": 0.22,
				"direction": Vector3(0.0, 1.0, 0.0),
				"spread": 10.0,
				"vel_min": 300.0,
				"vel_max": 420.0,
				"scale_min": 3.0,
				"scale_max": 6.0,
				"color": Color(1.0, 0.95, 0.85, 1.0),
			}
		)
		core_flame.position = Vector2(960.0 + side, 636.0)
		shake_root.add_child(core_flame)
		engines.append(core_flame)
	# 机身两侧舔舐火焰舌（斜外下方向、更短寿命，包住机身两侧）
	for side in [-1.0, 1.0]:
		var lick := _particles(
			{
				"amount": 32,
				"lifetime": 0.28,
				"direction": Vector3(0.35 * side, 1.0, 0.0),
				"spread": 25.0,
				"vel_min": 200.0,
				"vel_max": 380.0,
				"scale_min": 4.5,
				"scale_max": 9.0,
				"color": Color(1.0, 0.5, 0.12, 0.8),
			}
		)
		lick.position = Vector2(960.0 + 62.0 * side, 590.0)
		shake_root.add_child(lick)
		engines.append(lick)
	# 点火预热：前 ~0.3s（时轴随镜头时长缩放）尾焰 amount_ratio 0→1 + 喷口软辉光从 0 弹起 + 白闪脉冲
	var pre_roll := dur * 0.11
	var ignite := root.create_tween().set_parallel(true)
	for e in engines:
		e.amount_ratio = 0.0
		ignite.tween_property(e, "amount_ratio", 1.0, pre_roll).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
	for side in [-46.0, 46.0]:
		var nozzle := CinematicFx.soft_glow(26.0, Color(1.0, 0.55, 0.15, 0.75))
		nozzle.position = Vector2(960.0 + side, 632.0)
		var nozzle_base := nozzle.scale
		nozzle.scale = Vector2.ZERO
		shake_root.add_child(nozzle)
		ignite.tween_property(nozzle, "scale", nozzle_base, pre_roll).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)
	var ignite_flash := _bg_rect(Color(1.0, 1.0, 1.0, 0.55))
	shake_root.add_child(ignite_flash)
	var ignite_flash_t := root.create_tween()
	ignite_flash_t.tween_property(ignite_flash, "color:a", 0.0, dur * 0.07)
	# 轨道电火花：两侧壁轨各一发射器，火花顺轨向 +y 高速喷洒（textured 软点，≤32/侧）
	for side in [0, 1]:
		var rail_spark := _particles(
			{
				"amount": 28,
				"lifetime": 0.35,
				"direction": Vector3(0.0, 1.0, 0.0),
				"spread": 22.0,
				"vel_min": 500.0,
				"vel_max": 850.0,
				"damping_min": 100.0,
				"damping_max": 220.0,
				"scale_min": 2.5,
				"scale_max": 4.5,
				"color": Color(1.0, 0.75, 0.35, 0.9),
			}
		)
		rail_spark.position = Vector2(645.0 if side == 0 else 1275.0, 760.0)
		shake_root.add_child(rail_spark)
	# 全屏速度线
	for i in 10:
		var sl := _line(PackedVector2Array([Vector2.ZERO, Vector2(0.0, 180.0 + randf() * 160.0)]), Color(0.7, 0.85, 1.0, 0.18), 2.0)
		sl.position = Vector2(randf() * 1920.0, randf() * 1300.0 - 200.0)
		shake_root.add_child(sl)
		root._speed_lines.append(sl)
	# 左右边缘放射状速度线（斜向，集中在壁外边缘区，回卷保持同侧）
	for i in 8:
		var side_sign := -1.0 if i % 2 == 0 else 1.0
		var edge_sl := _line(
			PackedVector2Array([Vector2.ZERO, Vector2((34.0 + randf() * 40.0) * side_sign, 220.0 + randf() * 120.0)]),
			Color(0.7, 0.85, 1.0, 0.22),
			2.5
		)
		edge_sl.position = Vector2(randf() * 230.0 if side_sign < 0.0 else 1690.0 + randf() * 230.0, randf() * 1300.0 - 260.0)
		shake_root.add_child(edge_sl)
		root._edge_lines.append([edge_sl, side_sign])
	GameState.play_sfx(GameState.SFX_DASH, AUDIO_VOL_OFFSET, AUDIO_PITCH)
	# 引擎音持续：1.1s 后压低 6dB 补一发，覆盖镜头后段
	var engine := Timer.new()
	engine.one_shot = true
	engine.wait_time = 1.1
	engine.autostart = true
	root.add_child(engine)  # 随镜头销毁：跳过/切镜后不残留迟发回调
	engine.timeout.connect(
		func() -> void:
			if is_instance_valid(root):
				GameState.play_sfx(GameState.SFX_DASH, -6.0 + AUDIO_VOL_OFFSET, AUDIO_PITCH)
	)
	return root


# ---------------- 镜头 6：远景收束（3.0s） ----------------
## 星云叠加圆 + 右上恒星背光；画面底部行星弧线 + 青蓝大气辉光带压底；
## 左下残骸剪影与余烬闪烁（外加缓慢翻滚的漂浮碎片）；
## 战机 = 深色剪影 + 引擎亮斑，向右上加速驶离；补给舰编队两列三行光点同向跟随；
## 末尾 0.7s 由导演淡黑衔接标题定格。
func _build_shot6() -> Node2D:
	var dur: float = _shot_durations[5]
	var root := Node2D.new()
	root.name = "Shot6"
	root.add_child(Starfield.new())
	# 星云（大半径低透明度叠加圆，软径向光晕）
	var nebula1 := CinematicFx.soft_glow(430.0, Color(0.35, 0.15, 0.5, 0.13))
	nebula1.position = Vector2(1380.0, 320.0)
	root.add_child(nebula1)
	var nebula2 := CinematicFx.soft_glow(340.0, Color(0.1, 0.2, 0.5, 0.17))
	nebula2.position = Vector2(420.0, 780.0)
	root.add_child(nebula2)
	var nebula3 := CinematicFx.soft_glow(260.0, Color(0.1, 0.4, 0.45, 0.1))
	nebula3.position = Vector2(1050.0, 850.0)
	root.add_child(nebula3)
	# 星云缓慢异向漂移（远景视差呼吸感，往返循环）
	var nebulae: Array[Node2D] = [nebula1, nebula2, nebula3]
	var neb_dirs := [Vector2(60.0, 24.0), Vector2(-70.0, 30.0), Vector2(50.0, -36.0)]
	for n_i in 3:
		var neb_tween := root.create_tween().set_loops()
		(
			neb_tween
			. tween_property(nebulae[n_i], "position", nebulae[n_i].position + neb_dirs[n_i], 7.0 + 2.0 * n_i)
			. set_trans(Tween.TRANS_SINE)
			. set_ease(Tween.EASE_IN_OUT)
		)
		neb_tween.tween_property(nebulae[n_i], "position", nebulae[n_i].position, 7.0 + 2.0 * n_i).set_trans(Tween.TRANS_SINE).set_ease(
			Tween.EASE_IN_OUT
		)
	# 行星弧线（画面底部）：巨大半径圆弧（圆心远在屏下）+ 暗色星体填充 + 青蓝大气辉光带
	var limb_pts := PackedVector2Array()
	limb_pts.resize(64)
	for p_i in 64:
		var la := -PI * 0.5 + (float(p_i) / 63.0 - 0.5) * 1.0
		limb_pts[p_i] = Vector2(960.0, 3350.0) + Vector2(cos(la), sin(la)) * 2480.0
	var planet := Polygon2D.new()
	planet.polygon = limb_pts + PackedVector2Array([Vector2(2200.0, 1200.0), Vector2(-280.0, 1200.0)])
	planet.color = Color(0.02, 0.04, 0.09)
	root.add_child(planet)
	root.add_child(_line(limb_pts, Color(0.25, 0.5, 0.75, 0.55), 3.0))
	var atmo := CinematicFx.soft_glow(90.0, Color(0.3, 0.65, 0.9, 0.22))
	atmo.position = Vector2(960.0, 880.0)
	atmo.scale *= Vector2(16.0, 0.9)  # 横向拉扁成大气光带
	root.add_child(atmo)
	# 恒星背光（右上强辉光，软径向光晕）
	var star_halo := CinematicFx.soft_glow(230.0, Color(1.0, 0.9, 0.7, 0.12))
	star_halo.position = Vector2(1620.0, 180.0)
	root.add_child(star_halo)
	var star := CinematicFx.soft_glow(80.0, Color(1.0, 0.95, 0.85, 0.75))
	star.position = Vector2(1620.0, 180.0)
	root.add_child(star)
	# 恒星横向 anamorphic 光晕：宽蓝白亮条 + 短竖条（叠加态），缓慢脉动
	var flare_mat := CanvasItemMaterial.new()
	flare_mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	var flare_h := _rect_poly(600.0, 3.0, Color(0.6, 0.8, 1.0, 0.35))
	flare_h.position = star.position
	flare_h.material = flare_mat
	root.add_child(flare_h)
	var flare_v := _rect_poly(4.0, 90.0, Color(0.6, 0.8, 1.0, 0.2))
	flare_v.position = star.position
	flare_v.material = flare_mat
	root.add_child(flare_v)
	var flare_pulse := root.create_tween().set_loops()
	flare_pulse.tween_property(flare_h, "scale:x", 1.15, 1.8).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	flare_pulse.tween_property(flare_h, "scale:x", 1.0, 1.8).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	# 左下燃烧残骸剪影 + 橙色余烬闪烁
	var wreck := Polygon2D.new()
	wreck.polygon = PackedVector2Array(
		[
			Vector2(0.0, 1080.0),
			Vector2(0.0, 830.0),
			Vector2(150.0, 790.0),
			Vector2(280.0, 850.0),
			Vector2(420.0, 810.0),
			Vector2(540.0, 900.0),
			Vector2(560.0, 1080.0),
		]
	)
	wreck.color = Color(0.03, 0.03, 0.05)
	root.add_child(wreck)
	# 残骸顶缘余烬反光轮廓（否则在暗角下不可读）
	(
		root
		. add_child(
			_line(
				PackedVector2Array(
					[
						Vector2(0.0, 830.0),
						Vector2(150.0, 790.0),
						Vector2(280.0, 850.0),
						Vector2(420.0, 810.0),
						Vector2(540.0, 900.0),
					]
				),
				Color(1.0, 0.5, 0.15, 0.35),
				3.0
			)
		)
	)
	for pos in [Vector2(140.0, 860.0), Vector2(300.0, 900.0), Vector2(430.0, 870.0), Vector2(220.0, 950.0)]:
		var ember := _glow(6.0 + randf() * 4.0, Color(1.0, 0.5, 0.15, 0.8))
		ember.position = pos
		root.add_child(ember)
		var et := root.create_tween().set_loops()
		et.tween_property(ember, "modulate:a", 0.15, 0.4 + randf() * 0.3)
		et.tween_property(ember, "modulate:a", 1.0, 0.4 + randf() * 0.3)
	# 残骸上方缓慢翻滚的漂浮碎片（深色剪影 + 边缘微光）
	for k in 3:
		var shard := Polygon2D.new()
		shard.polygon = PackedVector2Array([Vector2(-14.0, -6.0), Vector2(12.0, -10.0), Vector2(16.0, 8.0), Vector2(-8.0, 10.0)])
		shard.color = Color(0.05, 0.05, 0.08)
		shard.position = Vector2(520.0 + 130.0 * k, 760.0 - 90.0 * k)
		root.add_child(shard)
		var shard_edge := _line(PackedVector2Array([Vector2(-14.0, -6.0), Vector2(12.0, -10.0)]), Color(1.0, 0.6, 0.25, 0.55), 2.5)
		shard.add_child(shard_edge)
		var st := root.create_tween().set_parallel(true).set_loops()
		st.tween_property(shard, "rotation", shard.rotation + TAU, 7.0 + 2.0 * k)
		st.tween_property(shard, "position", shard.position + Vector2(30.0, -46.0), 4.5 + k).set_trans(Tween.TRANS_SINE).set_ease(
			Tween.EASE_IN_OUT
		)
	# 战机剪影：深色机身 + 引擎亮斑，向右上加速驶离（ease_in）
	var ship := Node2D.new()
	ship.position = Vector2(760.0, 640.0)
	ship.rotation = Vector2(720.0, -360.0).angle()  # 机头对准航向
	ship.scale = Vector2.ONE * 1.3
	root.add_child(ship)
	var fuselage := Polygon2D.new()
	fuselage.polygon = PackedVector2Array(
		[
			Vector2(26.0, 0.0),
			Vector2(-4.0, -6.0),
			Vector2(-22.0, -18.0),
			Vector2(-14.0, -4.0),
			Vector2(-14.0, 4.0),
			Vector2(-22.0, 18.0),
			Vector2(-4.0, 6.0),
		]
	)
	fuselage.color = Color(0.1, 0.13, 0.2)
	ship.add_child(fuselage)
	# 恒星背光边缘光：机身上缘（朝恒星一侧）暖色描边
	(
		ship
		. add_child(
			_line(
				PackedVector2Array(
					[
						Vector2(26.0, 0.0),
						Vector2(-4.0, -6.0),
						Vector2(-22.0, -18.0),
					]
				),
				Color(1.0, 0.85, 0.6, 0.6),
				2.0
			)
		)
	)
	var canopy := _glow(3.0, Color(0.4, 0.7, 1.0, 0.6))
	canopy.position = Vector2(8.0, 0.0)
	ship.add_child(canopy)
	var engine_glow := _glow(9.0, Color(1.0, 0.6, 0.2, 0.9))
	engine_glow.position = Vector2(-16.0, 0.0)
	ship.add_child(engine_glow)
	var engine_flicker := root.create_tween().set_loops()
	engine_flicker.tween_property(engine_glow, "scale", Vector2.ONE * 1.5, 0.12)
	engine_flicker.tween_property(engine_glow, "scale", Vector2.ONE, 0.12)
	var ship_tween := root.create_tween()
	ship_tween.tween_property(ship, "position", Vector2(1480.0, 280.0), dur).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)
	# 补给舰编队：两列 × 三行光点阵列同向跟随（各带引擎拖尾短线，作为光点子节点随 tween 同行）
	for i in 6:
		var dot := _glow(4.0, Color(0.6, 0.8, 1.0, 0.8))
		dot.position = Vector2(420.0 + 70.0 * (i % 2), 800.0 + 52.0 * (i / 2))
		dot.add_child(_line(PackedVector2Array([Vector2.ZERO, Vector2(-18.0, 10.0)]), Color(0.5, 0.75, 1.0, 0.45), 2.0))
		root.add_child(dot)
		var dt := root.create_tween()
		dt.tween_property(dot, "position", dot.position + Vector2(240.0, -130.0), dur)
	return root
