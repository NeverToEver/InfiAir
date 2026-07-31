class_name LaserWeapon
extends Node2D
## 激光束武器（laser_beam buff，对齐原作 LaserBuff + LASER_DURATION=180 帧）：
## 挂载于 Player 节点下，GameState.buff_count(&"laser_beam") > 0 时启用。
## 就绪即自动触发：3s 持续光束替换普通子弹（禁用玩家自动开火），光束为
## 穿透性直线，线上敌人每 0.1s 结算 16 伤害；结束后进入 8s 冷却再次触发。

var BEAM_DURATION := 3.0
var COOLDOWN := 8.0
var TICK_INTERVAL := 0.1
var TICK_DAMAGE := 16
var BEAM_LENGTH := 2400.0
var BEAM_HALF_WIDTH := 26.0
var ENEMY_HIT_RADIUS := 30.0  # 敌机碰撞半径设计值（spawner 机型配置基准 30，× world_scale 生效）
const SFX_BEAM: AudioStream = preload("res://assets/audio/bullet_fire_c.wav")

var _active: bool = false
var _active_time: float = 0.0
var _cooldown: float = 0.0
var _tick_timer: float = 0.0
var _saved_autofire: bool = true

var _player: Player
var _beam: Line2D
var _glow: GPUParticles2D


func _ready() -> void:
	_player = get_parent() as Player
	BEAM_DURATION = GameState.cfg("buffs.laser_beam.duration", BEAM_DURATION)
	COOLDOWN = GameState.cfg("buffs.laser_beam.cooldown", COOLDOWN)
	TICK_INTERVAL = GameState.cfg("buffs.laser_beam.tick_interval", TICK_INTERVAL)
	TICK_DAMAGE = GameState.cfg("buffs.laser_beam.tick_damage", TICK_DAMAGE)
	BEAM_LENGTH = GameState.cfg("buffs.laser_beam.length", BEAM_LENGTH)
	BEAM_HALF_WIDTH = GameState.cfg("buffs.laser_beam.half_width", BEAM_HALF_WIDTH)
	ENEMY_HIT_RADIUS = GameState.cfg("buffs.laser_beam.hit_radius", ENEMY_HIT_RADIUS) * GameState.world_scale
	# 光束与末端光晕用 top_level 全局坐标，避免随机身旋转
	_beam = Line2D.new()
	_beam.top_level = true
	_beam.width = 14.0
	_beam.default_color = Color(0.55, 0.9, 1.0, 0.9)
	_beam.begin_cap_mode = Line2D.LINE_CAP_ROUND
	_beam.end_cap_mode = Line2D.LINE_CAP_ROUND
	_beam.visible = false
	add_child(_beam)
	_glow = GPUParticles2D.new()
	_glow.top_level = true
	_glow.amount = 24
	_glow.lifetime = 0.3
	var mat := ParticleProcessMaterial.new()
	mat.direction = Vector3.ZERO
	mat.spread = 180.0
	mat.gravity = Vector3.ZERO
	mat.initial_velocity_min = 40.0
	mat.initial_velocity_max = 120.0
	mat.scale_min = 1.5
	mat.scale_max = 3.0
	mat.color = Color(0.6, 0.95, 1.0, 0.9)
	_glow.process_material = mat
	_glow.emitting = false
	add_child(_glow)


func _physics_process(delta: float) -> void:
	if GameState.buff_count(&"laser_beam") <= 0:
		return
	_cooldown = maxf(_cooldown - delta, 0.0)
	if _active:
		_active_time -= delta
		_tick_timer -= delta
		if _active_time <= 0.0 or _player.is_dead() or _player.is_input_locked():
			_end_beam()
			return
		var start := _player.global_position
		var end := start + _aim_dir(start) * BEAM_LENGTH
		_beam.points = PackedVector2Array([start, end])
		_glow.position = end
		if _tick_timer <= 0.0:
			_tick_timer += TICK_INTERVAL
			_damage_tick(start, end)
	elif _cooldown <= 0.0 and not _player.is_dead() and not _player.is_input_locked():
		if (get_global_mouse_position() - _player.global_position).length() > 1.0:
			_start_beam()


func _aim_dir(start: Vector2) -> Vector2:
	var aim := get_global_mouse_position() - start
	if aim.length() <= 1.0:
		# 贴图机头朝上，机身 rotation 对应 Vector2.UP 方向
		return Vector2.UP.rotated(_player.rotation)
	return aim.normalized()


func _start_beam() -> void:
	_active = true
	_active_time = BEAM_DURATION
	_tick_timer = 0.0
	_beam.visible = true
	_glow.emitting = true
	# 光束期间替换普通子弹
	_saved_autofire = _player.auto_fire_enabled()
	_player.set_auto_fire(false)
	GameState.play_sfx(SFX_BEAM, -6.0)


func _end_beam() -> void:
	_active = false
	_beam.visible = false
	_glow.emitting = false
	_cooldown = COOLDOWN
	if is_instance_valid(_player):
		_player.set_auto_fire(_saved_autofire)


## 穿透结算：光束线段两侧的敌人（含 Boss）都吃伤害，不打断
## （遍历副本防 take_damage→die→注销注册表造成的遍历中突变）
func _damage_tick(start: Vector2, end: Vector2) -> void:
	for node in GameState.enemies.duplicate():
		if not is_instance_valid(node):
			continue
		var pos := (node as Node2D).global_position
		if _dist_to_segment(pos, start, end) <= BEAM_HALF_WIDTH + ENEMY_HIT_RADIUS:
			node.take_damage(TICK_DAMAGE)


static func _dist_to_segment(p: Vector2, a: Vector2, b: Vector2) -> float:
	var ab := b - a
	var t := clampf((p - a).dot(ab) / ab.length_squared(), 0.0, 1.0)
	return (p - (a + ab * t)).length()
