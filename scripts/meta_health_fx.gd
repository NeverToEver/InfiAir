class_name MetaHealthFX
extends CanvasLayer
## Meta HUD 血量与受击反馈（docs/META_HUD_DESIGN.md）：全屏后处理承载受击色差/径向模糊、
## 攻击方向定向波纹、低血裂纹生长/错峰消散、去饱和/冷青色偏/晕影与 DYING 心跳/呼吸/抖动。
## layer=1：世界之上、HUD 之下（HUD 在主场景抬至 layer=2；低于 OrbitalStrike 24、过场 35）。
## 性能（§2 决策）：满血静止隐藏全屏 ColorRect + _process 早退（常态零 GPU、≈零 CPU）；
## 参数上传 D5 epsilon 检测；自适应增益 D3 注册表代理亮度（0.25s 节流，零 GPU 回读）。

const STATE_NORMAL := 0
const STATE_CAUTION := 1
const STATE_DAMAGED := 2
const STATE_CRITICAL := 3
const STATE_DYING := 4

## 下行边界（HP 比例，低于即进入下一状态）；末级 DYING 阈值以 balance.json
## effects.meta_health.dying.threshold 为准（_state_for_x 运行时用 cfg 覆盖 0.20，防双源漂移）
const THRESHOLDS: Array[float] = [0.75, 0.50, 0.25, 0.20]
## 各状态裂纹密度上限（NORMAL 无裂纹；balance.json effects.meta_health.crack.density 可覆盖）
const DENSITY_CAPS: Array[float] = [0.0, 0.30, 0.50, 0.75, 1.0]

const META_SHADER: Shader = preload("res://assets/shaders/meta_health.gdshader")
const BAKE_SHADER: Shader = preload("res://assets/shaders/crack_field_bake.gdshader")

## 裂纹发光色带（§4.2 crossfade，带宽 0.08）
const CRACK_CYAN := Color(0x35e0ffff)
const CRACK_YELLOW := Color(0xffd23fff)
const CRACK_ORANGE := Color(0xff8a3dff)
const CRACK_RED := Color(0xff3b4eff)
const COLOR_BAND := 0.08

var _state: int = STATE_NORMAL
var _damage_x: float = 0.0  # 平滑后的损伤度（0=满血，1=空血）
var _target_x: float = 0.0
var _hit_pulse: float = 0.0
var _hit_dir: Vector2 = Vector2.ZERO  # 零向量 = 波纹退化为全边缘均匀环
var _ripple_t: float = 2.0  # >1 表示无波纹
var _grow_boost: float = 0.0  # 跨阈值裂纹生长过冲（+overshoot，grow_time 内回落）
var _heal_t: float = -1.0  # >=0：修复错峰消散进行中（0.7s 全程）
var _heal_jitter: float = 0.0
var _heart_phase: float = -1.0  # <0 表示非 DYING
var _heart_env: float = 0.0  # 心跳脉冲包络（0.3s，减少闪光时视觉置零、音效保留）
var _heart_rate: float = 0.0  # 当前心率 Hz（测试可验）
var _breath: float = 1.0
var _vig_inner: float = 0.62
var _warn_t: float = 0.0  # DYING 警告边框正弦相位
var _mat: ShaderMaterial
var _rect: ColorRect
var _last: Dictionary = {}  # D5 epsilon 缓存（参数名 -> 上次上传值）
var _cfg: Dictionary = {}  # effects.meta_health.* 一次性缓存
var _lod: int = 0
var _adapt_timer: float = 0.0
var _adapt_gain: float = 1.0
var _field_ready: bool = false
var _field_tex: Texture2D = null
var _force_refresh: bool = false  # 减少闪光切换等外部态变化时强制刷新一帧
# 测试插桩（§7 验收）：per-frame 参数上传次数 / 早退命中次数 / DYING 累计心跳次数
var _upload_count: int = 0
var _early_out_count: int = 0
var _heart_beats: int = 0


## A7：测试/诊断白盒断言经公开接口（平滑参数注入统一测试口 + 状态 getter）
## C35：接受无 `_` 前缀的语义键（内部补 `_` 写私有字段），不再与实现字段名强耦合
func set_test_state(state_dict: Dictionary) -> void:
	for k in state_dict.keys():
		if k is String:
			var field := String(k)
			if not field.begins_with("_"):
				field = "_" + field
			set(field, state_dict[k])


func crack_progress() -> float:
	return _crack_progress()


func hit_pulse() -> float:
	return _hit_pulse


func damage_x() -> float:
	return _damage_x


func state() -> int:
	return _state


func heal_jitter() -> float:
	return _heal_jitter


func heart_rate() -> float:
	return _heart_rate


func breath() -> float:
	return _breath


func rect() -> ColorRect:
	return _rect


func upload_count() -> int:
	return _upload_count


func early_out_count() -> int:
	return _early_out_count


func _ready() -> void:
	layer = 1
	_load_cfg()
	_lod = int(_cfg["lod"])
	GameState.meta_fx_lod = _lod  # 供 hud.gd 低血晕影回退判断（D2）
	_rect = ColorRect.new()
	_rect.set_anchors_preset(Control.PRESET_FULL_RECT)
	_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_mat = ShaderMaterial.new()
	_mat.shader = META_SHADER
	_rect.material = _mat
	add_child(_rect)
	_mat.set_shader_parameter("u_lod", _lod)
	_mat.set_shader_parameter("u_crack_spread_min", _cfg["crack_spread_min"])
	_mat.set_shader_parameter("u_crack_edge_softness", _cfg["crack_edge_softness"])
	_mat.set_shader_parameter("u_crack_width", _cfg["crack_width"])
	# K04：crack_glow 死配置键接线——shader ADD 伪泛光强度原为字面 0.8，改由配置驱动
	_mat.set_shader_parameter("u_crack_glow", _cfg["crack_glow"])
	_vig_inner = float(_cfg["vignette_inner"])
	# 启动即对齐当前血量（读档续局/场景重载），不产生过渡演出
	_target_x = 1.0 - clampf(GameState.health / GameState.max_health(), 0.0, 1.0)
	_damage_x = _target_x
	_state = _state_for_x(_damage_x)
	if _state == STATE_DYING and GameState.health > 0.0:
		_heart_phase = 0.0
	_bake_crack_field()
	GameState.health_changed.connect(_on_health_changed)
	GameState.player_damaged.connect(_on_player_damaged)
	GameState.player_died.connect(_on_player_died)
	GameState.reduce_flash_changed.connect(_on_reduce_flash_changed)
	_rect.visible = _damage_x > 0.001


func _exit_tree() -> void:
	# MetaFX 不在场时 hud 低血晕影走回退路径（D2）
	GameState.meta_fx_lod = 1


## 数值配置缓存（启动一次读入；默认值与 balance.json effects.meta_health.* 保持一致）
## H08（健壮性审核）：crack.density 长度/元素校验回退——损坏 JSON 短数组/非数值时
## 用默认档位，防每帧越界索引与 float 转换报错
func _load_density_caps() -> Array:
	var raw: Variant = GameState.cfg("effects.meta_health.crack.density", DENSITY_CAPS.duplicate())
	if raw is Array and raw.size() == DENSITY_CAPS.size():
		for v: Variant in raw:
			if not (v is int or v is float):
				return DENSITY_CAPS.duplicate()
		return raw.duplicate()
	return DENSITY_CAPS.duplicate()


func _load_cfg() -> void:
	_cfg = {
		"lod": int(GameState.cfg("effects.meta_health.lod", 0)),
		"pulse_scale": float(GameState.cfg("effects.meta_health.pulse.scale", 2.5)),
		"pulse_min": float(GameState.cfg("effects.meta_health.pulse.min", 0.15)),
		"pulse_decay_tau": maxf(float(GameState.cfg("effects.meta_health.pulse.decay_tau", 0.09)), 0.001),  # H15
		"chromatic_base": float(GameState.cfg("effects.meta_health.chromatic.base", 0.006)),
		"chromatic_peak": float(GameState.cfg("effects.meta_health.chromatic.peak", 0.014)),
		"blur_strength": float(GameState.cfg("effects.meta_health.blur.strength", 0.6)),
		"ripple_duration": maxf(float(GameState.cfg("effects.meta_health.ripple.duration", 0.4)), 0.001),  # H15
		"ripple_alpha": float(GameState.cfg("effects.meta_health.ripple.alpha", 0.8)),
		"crack_exponent": float(GameState.cfg("effects.meta_health.crack.exponent", 1.6)),
		"crack_spread_min": float(GameState.cfg("effects.meta_health.crack.spread_min", 0.10)),
		"crack_edge_softness": float(GameState.cfg("effects.meta_health.crack.edge_softness", 0.08)),
		"crack_width": float(GameState.cfg("effects.meta_health.crack.width", 0.03)),
		"crack_glow": float(GameState.cfg("effects.meta_health.crack.glow", 0.8)),
		"crack_heal_jitter": float(GameState.cfg("effects.meta_health.crack.heal_jitter", 0.35)),
		"crack_grow_overshoot": float(GameState.cfg("effects.meta_health.crack.grow_overshoot", 0.08)),
		"crack_grow_time": maxf(float(GameState.cfg("effects.meta_health.crack.grow_time", 0.6)), 0.001),  # K03：H15 同族遗漏（=0 时 _grow_boost 衰减除零）
		"crack_density": _load_density_caps(),
		"desat_max": float(GameState.cfg("effects.meta_health.desat.max", 0.35)),
		"desat_exponent": float(GameState.cfg("effects.meta_health.desat.exponent", 2.0)),
		"vignette_max_alpha": float(GameState.cfg("effects.meta_health.vignette.max_alpha", 0.5)),
		"vignette_inner": float(GameState.cfg("effects.meta_health.vignette.inner", 0.62)),
		"vignette_dying_shrink": float(GameState.cfg("effects.meta_health.vignette.dying_shrink", 0.06)),
		"dying_threshold": float(GameState.cfg("effects.meta_health.dying.threshold", 0.2)),
		"heart_min_hz": float(GameState.cfg("effects.meta_health.dying.heart_min_hz", 1.0)),
		"heart_max_hz": float(GameState.cfg("effects.meta_health.dying.heart_max_hz", 1.2)),
		"breath": float(GameState.cfg("effects.meta_health.dying.breath", 0.015)),
		"jitter_px": float(GameState.cfg("effects.meta_health.dying.jitter_px", 2.0)),
		"warn_hz": float(GameState.cfg("effects.meta_health.dying.warn_hz", 2.5)),
		"dying_fade": maxf(float(GameState.cfg("effects.meta_health.dying.fade", 0.3)), 0.001),  # H15
		"smooth_down_tau": maxf(float(GameState.cfg("effects.meta_health.smooth.down_tau", 0.10)), 0.001),  # H15
		"smooth_up_tau": maxf(float(GameState.cfg("effects.meta_health.smooth.up_tau", 0.80)), 0.001),  # H15
		"adapt_interval": float(GameState.cfg("effects.meta_health.adapt.interval", 0.25)),
		"adapt_min": float(GameState.cfg("effects.meta_health.adapt.min", 0.8)),
		"adapt_max": float(GameState.cfg("effects.meta_health.adapt.max", 1.3)),
		"adapt_bullet_weight": float(GameState.cfg("effects.meta_health.adapt.bullet_weight", 0.002)),
		"adapt_explosion_weight": float(GameState.cfg("effects.meta_health.adapt.explosion_weight", 0.15)),
		"reduce_flash_chromatic_scale": float(GameState.cfg("effects.meta_health.reduce_flash.chromatic_scale", 0.4)),
	}


func _state_for_x(x: float) -> int:
	var ratio := clampf(1.0 - x, 0.0, 1.0)
	var s := STATE_NORMAL
	for i in THRESHOLDS.size():
		var t: float = THRESHOLDS[i]
		if i == THRESHOLDS.size() - 1:
			t = float(_cfg["dying_threshold"])  # DYING 阈值统一读 cfg（默认 0.2，与常量一致）
		if ratio < t:
			s += 1
	return s


func _on_health_changed(new_health: float) -> void:
	_target_x = 1.0 - clampf(new_health / GameState.max_health(), 0.0, 1.0)


func _on_player_damaged(amount: float, from_pos: Vector2) -> void:
	var r := amount / GameState.max_health()
	# max 池化：高频低伤不累积（R2）
	_hit_pulse = maxf(_hit_pulse, clampf(r * float(_cfg["pulse_scale"]), float(_cfg["pulse_min"]), 1.0))
	if from_pos == Vector2.INF or GameState.player_ref == null:
		_hit_dir = Vector2.ZERO  # 无方向：波纹退化为全边缘均匀环
	else:
		var d: Vector2 = from_pos - (GameState.player_ref as Node2D).global_position
		_hit_dir = d.normalized() if d.length() > 1.0 else Vector2.ZERO
	_ripple_t = 0.0


func _on_player_died() -> void:
	# 死亡即停心跳/呼吸（包络不再触发新拍），裂纹/去饱和定格作死亡底衬
	_heart_phase = -1.0
	_heart_env = 0.0


func _on_reduce_flash_changed(_enabled: bool) -> void:
	_force_refresh = true


## DYING 呼吸缩放（main.gd D6 组合相机 zoom 用）
func breath_scale() -> float:
	return _breath


func breath_active() -> bool:
	return _state == STATE_DYING and GameState.health > 0.0 and not GameState.reduce_flash


## 血量-裂纹映射曲线（§4.2；测试采样点不含生长过冲）
func _crack_progress() -> float:
	return pow(_damage_x, float(_cfg["crack_exponent"]))


## 裂纹颜色 crossfade（带宽 0.08）：青 → 黄 → 橙 → 红
func _crack_color(x: float) -> Color:
	var c := CRACK_CYAN
	c = _blend_band(c, CRACK_YELLOW, x, 0.25)
	c = _blend_band(c, CRACK_ORANGE, x, 0.50)
	c = _blend_band(c, CRACK_RED, x, 0.80)
	return c


func _blend_band(a: Color, b: Color, x: float, edge: float) -> Color:
	return a.lerp(b, smoothstep(edge - COLOR_BAND * 0.5, edge + COLOR_BAND * 0.5, x))


func _process(delta: float) -> void:
	# 早退（D10）：全部动态量稳定时零参数上传；满血时连全屏 ColorRect 也隐藏（零 GPU）
	var idle := (
		absf(_target_x - _damage_x) < 0.001
		and _hit_pulse < 0.001
		and _ripple_t > 1.0
		and _heart_phase < 0.0
		and _heart_env < 0.001
		and _heal_t < 0.0
		and _grow_boost < 0.001
		and absf(_breath - 1.0) < 0.001
	)
	if idle and not _force_refresh:
		_early_out_count += 1
		if _damage_x < 0.001 and _rect.visible:
			_rect.visible = false
		return
	if not _rect.visible:
		_rect.visible = true

	# 1. 损伤度指数趋近：下行快入（tau=0.10）、上行慢出（tau=0.80）
	var down := _target_x > _damage_x
	var tau := float(_cfg["smooth_down_tau"] if down else _cfg["smooth_up_tau"])
	_damage_x += (_target_x - _damage_x) * (1.0 - exp(-delta / tau))

	# 2. 状态跃迁：下行跨阈值 → 裂纹生长过冲；上行跨阈值 → 修复错峰消散（0.7s）
	var new_state := _state_for_x(_damage_x)
	if new_state > _state:
		_grow_boost = float(_cfg["crack_grow_overshoot"])
	elif new_state < _state:
		_heal_t = 0.0
	_state = new_state
	_grow_boost = move_toward(_grow_boost, 0.0, float(_cfg["crack_grow_overshoot"]) / float(_cfg["crack_grow_time"]) * delta)
	if _heal_t >= 0.0:
		_heal_t += delta / 0.7
		if _heal_t >= 1.0:
			_heal_t = -1.0
			_heal_jitter = 0.0
		else:
			_heal_jitter = float(_cfg["crack_heal_jitter"]) * Enemy.sin_fast(PI * _heal_t)

	# 3. HitPulse 指数衰减与波纹推进（与状态正交）
	_hit_pulse *= exp(-delta / float(_cfg["pulse_decay_tau"]))
	_ripple_t += delta / float(_cfg["ripple_duration"])

	# 4. DYING 临界层：心跳（1.0→1.2Hz 随 x 插值）/呼吸/抖动/警告脉动；进出均 0.3s 淡出无硬切
	if _state == STATE_DYING and GameState.health > 0.0:
		if _heart_phase < 0.0:
			_heart_phase = 0.0
		var threshold_x := 1.0 - float(_cfg["dying_threshold"])
		_heart_rate = lerpf(
			float(_cfg["heart_min_hz"]),
			float(_cfg["heart_max_hz"]),
			clampf((_damage_x - threshold_x) / maxf(1.0 - threshold_x, 0.01), 0.0, 1.0)
		)
		var prev := _heart_phase
		_heart_phase += delta * _heart_rate
		if floorf(_heart_phase) > floorf(prev):
			_heart_beats += 1
			_heart_env = 1.0
			GameState.play_sfx(GameState.SFX_HEARTBEAT, -8.0)  # D7：单发触发，音效不受减少闪光影响
			if not GameState.reduce_flash:
				get_tree().call_group("hud", "meta_jitter", float(_cfg["jitter_px"]))  # D9
		_heart_env = maxf(_heart_env - delta / float(_cfg["dying_fade"]), 0.0)
		_breath = 1.0 + float(_cfg["breath"]) * Enemy.sin_fast(_heart_phase * TAU)
		_warn_t += delta
	else:
		_heart_phase = -1.0
		_heart_env = maxf(_heart_env - delta / float(_cfg["dying_fade"]), 0.0)
		_breath = move_toward(_breath, 1.0, delta * float(_cfg["breath"]) / float(_cfg["dying_fade"]))
		_warn_t = 0.0
	# DYING 视野收窄 6%（0.3s 平滑）
	var vig_inner_target := float(_cfg["vignette_inner"])
	if _state == STATE_DYING and GameState.health > 0.0:
		vig_inner_target -= float(_cfg["vignette_dying_shrink"])
	_vig_inner = move_toward(_vig_inner, vig_inner_target, float(_cfg["vignette_dying_shrink"]) / float(_cfg["dying_fade"]) * delta)

	# 5. D3 自适应可读性：注册表代理亮度（活跃弹数/爆炸数），0.25s 节流，零 GPU 回读
	_adapt_timer -= delta
	if _adapt_timer <= 0.0:
		_adapt_timer = float(_cfg["adapt_interval"])
		var bullets := 0
		var explosions := 0
		for child in get_parent().get_children():
			if child is Bullet:
				if child.is_active():
					bullets += 1
			elif child is Explosion:
				if child.visible:
					explosions += 1
		var proxy := bullets * float(_cfg["adapt_bullet_weight"]) + explosions * float(_cfg["adapt_explosion_weight"])
		_adapt_gain = clampf(1.0 - proxy, float(_cfg["adapt_min"]), float(_cfg["adapt_max"]))

	# 6. 参数合成（§4.2 曲线；「减少闪光」在传参前折算，shader 零分支）
	var x := _damage_x
	var progress := minf(_crack_progress() + _grow_boost, 1.0)
	var pulse := _hit_pulse
	var chromatic := 0.0
	if pulse > 0.001:
		chromatic = float(_cfg["chromatic_base"]) + float(_cfg["chromatic_peak"]) * pulse
		if GameState.reduce_flash:
			chromatic *= float(_cfg["reduce_flash_chromatic_scale"])
	var blur := float(_cfg["blur_strength"]) * pulse
	var ripple_on := _ripple_t <= 1.0
	var caps: Array = _cfg["crack_density"]
	var density: float = float(caps[mini(_state, caps.size() - 1)])
	if not _field_ready:
		density = 0.0  # 距离场未烘焙完成前不出裂纹（避免空采样全屏闪）
	var vig_strength := minf(float(_cfg["vignette_max_alpha"]), _crack_progress() * 0.55)
	if _state == STATE_DYING and GameState.health > 0.0 and not GameState.reduce_flash:
		# 警告边框 2.5Hz 正弦（减少闪光时改静态，正弦折叠在 GDScript 侧）
		vig_strength *= 1.0 + 0.25 * Enemy.sin_fast(_warn_t * TAU * float(_cfg["warn_hz"]))
	var heartbeat := 0.0 if GameState.reduce_flash else _heart_env

	# 7. D5 epsilon 检测上传（变化 <0.001 不上传）
	_put(&"u_hit_intensity", pulse * float(_cfg["ripple_alpha"]) if ripple_on else 0.0)
	_put(&"u_hit_dir", _hit_dir)
	_put(&"u_chromatic_amount", chromatic)
	_put(&"u_radial_blur_strength", blur)
	_put(&"u_ripple_phase", clampf(_ripple_t, 0.0, 1.0))
	_put(&"u_crack_progress", progress)
	_put(&"u_crack_color", _crack_color(x))
	_put(&"u_crack_density", density)
	_put(&"u_heal_jitter", _heal_jitter)
	_put(&"u_desaturation", float(_cfg["desat_max"]) * pow(x, float(_cfg["desat_exponent"])))
	_put(&"u_hue_cool", 0.6 * x)
	_put(&"u_vignette_strength", vig_strength)
	_put(&"u_vignette_inner", _vig_inner)
	_put(&"u_heartbeat", heartbeat)
	_put(&"u_adapt_gain", _adapt_gain)
	_force_refresh = false


## D5：epsilon 变化检测后上传；上传计数供测试插桩
func _put(pname: StringName, value: Variant) -> void:
	var prev: Variant = _last.get(pname)
	if prev != null and _same_param(prev, value):
		return
	_last[pname] = value
	_mat.set_shader_parameter(pname, value)
	_upload_count += 1


func _same_param(a: Variant, b: Variant) -> bool:
	if a is float and b is float:
		return absf(a - b) < 0.001
	if a is Vector2 and b is Vector2:
		return (a - b).length_squared() < 0.000001
	if a is Color and b is Color:
		return absf(a.r - b.r) < 0.004 and absf(a.g - b.g) < 0.004 and absf(a.b - b.b) < 0.004
	return a == b


## 测试钩子（A7 遗留清理，公开化）：切换 LOD（正常路径由 _ready 从 effects.meta_health.lod 读取）
func set_lod(v: int) -> void:
	_lod = v
	GameState.meta_fx_lod = v
	_mat.set_shader_parameter("u_lod", v)
	_last.erase(&"u_lod")


# ---------------- 裂纹距离场预烘焙（D1） ----------------


## 运行时 SubViewport 单帧烘焙（启动一次，512²，成本 1 帧）；headless dummy 渲染走 CPU 回退。
func _bake_crack_field() -> void:
	if DisplayServer.get_name() == "headless":
		_apply_crack_field_image(_cpu_bake_image(64))
		return
	var vp := SubViewport.new()
	vp.size = Vector2i(512, 512)
	vp.disable_3d = true
	vp.render_target_update_mode = SubViewport.UPDATE_ONCE
	var rect_node := ColorRect.new()
	rect_node.set_anchors_preset(Control.PRESET_FULL_RECT)
	var mat := ShaderMaterial.new()
	mat.shader = BAKE_SHADER
	rect_node.material = mat
	vp.add_child(rect_node)
	add_child(vp)
	# 一次性信号回调而非 await 协程：进程退出时挂起协程会泄漏函数状态
	RenderingServer.frame_post_draw.connect(_on_bake_frame.bind(vp), CONNECT_ONE_SHOT)


func _on_bake_frame(vp: SubViewport) -> void:
	if not is_instance_valid(vp):
		return
	var img := vp.get_texture().get_image()
	vp.queue_free()
	if img == null or img.is_empty():
		img = _cpu_bake_image(64)
	_apply_crack_field_image(img)


func _apply_crack_field_image(img: Image) -> void:
	_field_tex = ImageTexture.create_from_image(img)
	_mat.set_shader_parameter("u_crack_field", _field_tex)
	_field_ready = true


## CPU 等价回退（headless / 回读失败兜底）：与 crack_field_bake.gdshader 公式一致。
func _cpu_bake_image(size: int) -> Image:
	var seeds: Array[Vector2] = []
	for i in 12:
		seeds.append(Vector2(_fract(sin(float(i) * 12.9898) * 43758.5453), _fract(sin(float(i) * 78.233) * 43758.5453)))
	var img := Image.create(size, size, false, Image.FORMAT_RGBA8)
	for y in size:
		for x in size:
			var uv := Vector2((float(x) + 0.5) / size, (float(y) + 0.5) / size)
			var p := Vector2(uv.x * 1.7778, uv.y)
			var f1 := 10.0
			var f2 := 10.0
			var h := 0.0
			for s0 in seeds:
				var s := Vector2(s0.x * 1.7778, s0.y)
				var d := p.distance_to(s)
				if d < f1:
					f2 = f1
					f1 = d
					h = _fract(sin(s.dot(Vector2(12.9898, 78.233))) * 43758.5453)
				elif d < f2:
					f2 = d
			var border := f2 - f1
			var radial: float = ((uv - Vector2(0.5, 0.5)) * Vector2(1.7778, 1.0)).length()
			var gate: float = 1.0 - clampf(radial, 0.0, 1.0)  # 生长门：边缘 0（最先蔓延）→ 中心 1
			img.set_pixel(x, y, Color(clampf(border * 2.5, 0.0, 1.0), h, gate, 1.0))
	return img


## GLSL fract 等价（x - floor(x)），GDScript 无内建 fractf
static func _fract(v: float) -> float:
	return v - floorf(v)
