class_name Player
extends CharacterBody2D
## 玩家战机：WASD 平滑移动，朝鼠标旋转，按住左键自动开火，Shift 加速。

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

var _fire_cooldown: float = 0.0
var _sound_index: int = 0
var _invincible: float = INVINCIBLE_TIME  # 出生保护
var _regen_accum: float = 0.0
var _dead: bool = false

@onready var _sprite: Sprite2D = $Sprite2D
@onready var _audio: AudioStreamPlayer2D = $AudioStreamPlayer2D
@onready var _hitbox: Area2D = $Hitbox


func _ready() -> void:
	add_to_group("player")


func fire_interval() -> float:
	return BASE_FIRE_INTERVAL * pow(0.75, GameState.buff_count(&"rapid_fire"))


func bullet_damage() -> int:
	return 1 + GameState.buff_count(&"power_shot")


func _physics_process(delta: float) -> void:
	if _dead:
		return
	var input_dir := Input.get_vector("move_left", "move_right", "move_up", "move_down")
	var boost := BOOST_MULT if Input.is_action_pressed("boost") else 1.0
	var target := input_dir * MAX_SPEED * boost
	var rate := ACCEL if input_dir != Vector2.ZERO else DECEL
	velocity = velocity.move_toward(target, rate * delta)
	move_and_slide()
	position = position.clamp(Vector2(40.0, 40.0), Vector2(1880.0, 1040.0))

	var aim := get_global_mouse_position() - global_position
	if aim.length() > 1.0:
		# 贴图机头朝上，需 +90° 偏移
		rotation = aim.angle() + PI / 2.0

	_fire_cooldown -= delta
	if Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT) and _fire_cooldown <= 0.0:
		_fire(aim.normalized())
		_fire_cooldown = fire_interval()

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


func _fire(aim: Vector2) -> void:
	var spread := mini(GameState.buff_count(&"spread_shot"), 3)
	var count := 1 + spread
	for i in count:
		var offset := deg_to_rad(BULLET_SPREAD_DEG * (float(i) - float(spread) / 2.0))
		var b := BULLET_SCENE.instantiate()
		b.setup(aim.rotated(offset), BULLET_SPEED, bullet_damage(), true)
		b.position = position + aim.rotated(offset) * 50.0
		get_parent().add_child(b)
	_audio.stream = FIRE_SOUNDS[_sound_index]
	_sound_index = (_sound_index + 1) % FIRE_SOUNDS.size()
	_audio.play()


func take_damage(amount: float = 1.0) -> void:
	if _dead or _invincible > 0.0:
		return
	_invincible = INVINCIBLE_TIME
	GameState.lose_life(amount)
	if GameState.lives <= 0.0:
		_die()


func _die() -> void:
	_dead = true
	hide()
	_hitbox.set_deferred("monitoring", false)
	set_physics_process(false)
	Explosion.spawn_at(get_parent(), position, 2.0)
