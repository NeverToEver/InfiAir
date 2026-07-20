class_name Player
extends CharacterBody2D
## 玩家战机：WASD 平滑移动，朝鼠标旋转，全自动开火（对齐原作 auto_fire），
## Shift 消耗燃料加速，空格相位冲刺（需解锁 buff）。

const FIRE_SOUNDS: Array[AudioStream] = [
	preload("res://assets/audio/bullet_fire.wav"),
	preload("res://assets/audio/bullet_fire_b.wav"),
	preload("res://assets/audio/bullet_fire_c.wav"),
]
const BULLET_SCENE: PackedScene = preload("res://scenes/bullet.tscn")

const MAX_SPEED := 420.0
const ACCEL := 2400.0
const DECEL := 1800.0
const BOOST_MULT := 1.8
const BASE_FIRE_INTERVAL := 0.15
const BULLET_SPEED := 900.0
const BULLET_SPREAD_DEG := 15.0
const INVINCIBLE_TIME := 1.5

const FUEL_DRAIN := 35.0
const FUEL_REGEN := 20.0
const FUEL_RESTART := 30.0

const DASH_DISTANCE := 200.0
const DASH_TIME := 0.25
const DASH_COOLDOWN := 4.0
const AFTERIMAGE_INTERVAL := 0.08

var fuel_max: float = 100.0  # 扩容油箱天赋可提升
var _input_locked: bool = false  # 返航过场期间锁定
var _auto_fire_enabled: bool = true  # 冒烟测试可关闭全自动开火

var _fire_cooldown: float = 0.0
var _sound_index: int = 0
var _invincible: float = INVINCIBLE_TIME  # 出生保护
var _regen_accum: float = 0.0
var _dead: bool = false

var _fuel: float = 100.0
var _fuel_locked: bool = false  # 燃料耗尽后锁定，回到 30% 才解锁

var _dashing: bool = false
var _dash_timer: float = 0.0
var _dash_dir: Vector2 = Vector2.ZERO
var _dash_cooldown: float = 0.0
var _afterimage_timer: float = 0.0

@onready var _sprite: Sprite2D = $Sprite2D
@onready var _audio: AudioStreamPlayer2D = $AudioStreamPlayer2D
@onready var _hitbox: Area2D = $Hitbox
@onready var _thruster: GPUParticles2D = $Thruster
@onready var _slow_ring: Node2D = $SlowFieldRing


func _ready() -> void:
	add_to_group("player")


func fire_interval() -> float:
	return BASE_FIRE_INTERVAL * pow(0.75, GameState.buff_count(&"rapid_fire"))


func bullet_damage() -> int:
	return 1 + GameState.buff_count(&"power_shot")


func fuel_ratio() -> float:
	return _fuel / fuel_max


func refill_fuel() -> void:
	_fuel = fuel_max
	_fuel_locked = false


func dash_unlocked() -> bool:
	return GameState.buff_count(&"phase_dash") > 0


func dash_cooldown_max() -> float:
	# 首次选择解锁，之后每次选择冷却 -20%（最多 2 次）
	return DASH_COOLDOWN * pow(0.8, maxi(GameState.buff_count(&"phase_dash") - 1, 0))


func dash_ready_ratio() -> float:
	if not dash_unlocked():
		return 0.0
	return 1.0 - clampf(_dash_cooldown / dash_cooldown_max(), 0.0, 1.0)


func fuel_drain_rate() -> float:
	return FUEL_DRAIN * pow(0.75, GameState.buff_count(&"efficient_boost"))


func _physics_process(delta: float) -> void:
	if _dead or _input_locked:
		return
	var input_dir := Input.get_vector("move_left", "move_right", "move_up", "move_down")

	# 相位冲刺
	_dash_cooldown = maxf(_dash_cooldown - delta, 0.0)
	if dash_unlocked() and Input.is_action_just_pressed("dash") and _dash_cooldown <= 0.0 and not _dashing:
		_start_dash(input_dir)
	if _dashing:
		_dash_move(delta)
		return

	# 燃料与加速
	var want_boost := Input.is_action_pressed("boost")
	if _fuel_locked and _fuel >= FUEL_RESTART:
		_fuel_locked = false
	var boosting := want_boost and not _fuel_locked and _fuel > 0.0
	if boosting:
		_fuel = maxf(_fuel - fuel_drain_rate() * delta, 0.0)
		if _fuel <= 0.0:
			_fuel_locked = true
	else:
		_fuel = minf(_fuel + FUEL_REGEN * delta, fuel_max)

	var boost := BOOST_MULT if boosting else 1.0
	var target := input_dir * MAX_SPEED * boost
	var rate := ACCEL if input_dir != Vector2.ZERO else DECEL
	velocity = velocity.move_toward(target, rate * delta)
	move_and_slide()
	position = position.clamp(Vector2(40.0, 40.0), Vector2(1880.0, 1040.0))

	# 尾焰：加速变长变亮，静止减弱
	if boosting and input_dir != Vector2.ZERO:
		_thruster.speed_scale = 1.7
		_thruster.amount_ratio = 1.0
		_thruster.self_modulate = Color(1.0, 1.0, 1.0, 1.0)
	elif input_dir != Vector2.ZERO:
		_thruster.speed_scale = 1.0
		_thruster.amount_ratio = 0.8
		_thruster.self_modulate = Color(1.0, 1.0, 1.0, 0.85)
	else:
		_thruster.speed_scale = 0.6
		_thruster.amount_ratio = 0.35
		_thruster.self_modulate = Color(1.0, 1.0, 1.0, 0.6)

	var aim := get_global_mouse_position() - global_position
	if aim.length() > 1.0:
		# 贴图机头朝上，需 +90° 偏移
		rotation = aim.angle() + PI / 2.0

	_fire_cooldown -= delta
	if _auto_fire_enabled and _fire_cooldown <= 0.0 and aim.length() > 1.0:
		_fire(aim.normalized())
		_fire_cooldown = fire_interval()

	# 慢速力场环显示
	_slow_ring.visible = GameState.buff_count(&"slow_field") > 0

	# 无敌帧闪烁
	if _invincible > 0.0:
		_invincible -= delta
		_sprite.modulate.a = 0.35 + 0.65 * absf(sin(Time.get_ticks_msec() / 1000.0 * 20.0))
	else:
		_sprite.modulate.a = 1.0

	# Regen buff：每 2s 回 0.5 命 / 层
	var regen_stacks := GameState.buff_count(&"regen")
	if regen_stacks > 0:
		_regen_accum += delta
		if _regen_accum >= 2.0:
			_regen_accum -= 2.0
			GameState.heal(0.5 * regen_stacks)


func _start_dash(input_dir: Vector2) -> void:
	_dashing = true
	_dash_timer = DASH_TIME
	if input_dir != Vector2.ZERO:
		_dash_dir = input_dir.normalized()
	else:
		_dash_dir = (get_global_mouse_position() - global_position).normalized()
		if _dash_dir == Vector2.ZERO:
			_dash_dir = Vector2.UP
	_dash_cooldown = dash_cooldown_max()
	_afterimage_timer = 0.0
	GameState.play_sfx(GameState.SFX_DASH)


func _dash_move(delta: float) -> void:
	_dash_timer -= delta
	_afterimage_timer -= delta
	if _afterimage_timer <= 0.0:
		_afterimage_timer = AFTERIMAGE_INTERVAL
		_spawn_afterimage()
	velocity = _dash_dir * (DASH_DISTANCE / DASH_TIME)
	move_and_slide()
	position = position.clamp(Vector2(40.0, 40.0), Vector2(1880.0, 1040.0))
	# 冲刺时尾焰拉满
	_thruster.speed_scale = 1.7
	_thruster.amount_ratio = 1.0
	_thruster.self_modulate = Color(1.0, 1.0, 1.0, 1.0)
	if _dash_timer <= 0.0:
		_dashing = false
		GameState.play_sfx(GameState.SFX_DASH, -3.0)


func _spawn_afterimage() -> void:
	var ghost := Sprite2D.new()
	ghost.texture = _sprite.texture
	ghost.scale = _sprite.scale
	ghost.global_position = global_position
	ghost.global_rotation = rotation
	ghost.modulate = Color(0.5, 0.9, 1.0, 0.5)
	get_parent().add_child(ghost)
	var tween := ghost.create_tween()
	tween.tween_property(ghost, "modulate:a", 0.0, 0.3)
	tween.tween_callback(ghost.queue_free)


func _fire(aim: Vector2) -> void:
	var spread := mini(GameState.buff_count(&"spread_shot"), 3)
	var pierce := mini(GameState.buff_count(&"piercing"), 2)
	var explosive := GameState.buff_count(&"explosive") > 0
	var count := 1 + spread
	for i in count:
		var offset := deg_to_rad(BULLET_SPREAD_DEG * (float(i) - float(spread) / 2.0))
		var b := BULLET_SCENE.instantiate()
		b.setup(aim.rotated(offset), BULLET_SPEED, bullet_damage(), true)
		b.pierce = pierce
		b.explosive = explosive
		b.position = position + aim.rotated(offset) * 50.0
		get_parent().add_child(b)
	_audio.stream = FIRE_SOUNDS[_sound_index]
	_sound_index = (_sound_index + 1) % FIRE_SOUNDS.size()
	_audio.play()


func take_damage(amount: float = 1.0) -> void:
	if _dead or _invincible > 0.0 or _dashing:
		return
	# 闪避 buff：完全闪避，概率 1-(0.85^n)
	var evasion_stacks := GameState.buff_count(&"evasion")
	if evasion_stacks > 0 and randf() < 1.0 - pow(0.85, evasion_stacks):
		return
	# 护甲 buff：每层 25% 概率伤害减半
	var armor_stacks := GameState.buff_count(&"armor")
	if armor_stacks > 0 and randf() < 0.25 * armor_stacks:
		amount *= 0.5
	_invincible = INVINCIBLE_TIME
	GameState.play_sfx(GameState.SFX_PLAYER_HIT)
	GameState.shake(12.0)
	GameState.lose_life(amount)
	if GameState.lives <= 0.0:
		_die()


func _die() -> void:
	_dead = true
	hide()
	_hitbox.set_deferred("monitoring", false)
	set_physics_process(false)
	Explosion.spawn_at(get_parent(), position, 2.0)
