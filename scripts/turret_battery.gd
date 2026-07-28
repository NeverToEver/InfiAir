class_name TurretBattery
extends Area2D
## 精英炮塔事件炮台：航母甲板上升起的独立可摧毁单位（docs/ELITE_TURRET_EVENT.md）。
## 弱锁定索敌：炮塔以限速转向玩家，开火朝向 = 当前朝向 + ±spread_deg 出膛散布；
## 弹药按预设序列轮换（全部复用敌侧弹种，参数读 enemies/boss 配置段）。
## 升起期间不可被攻击（monitoring=false）；被毁时爆炸 + 基座环熄灭（由事件编排处理）。

signal died(turret: TurretBattery)

## 弹药速度/伤害（读 balance.json enemies/boss 段，脚本值为缺键回退）
var SINGLE_SPEED := 420.0
var SPREAD_SPEED := 340.0
var LASER_SPEED := 720.0
var HOMING_SPEED := 300.0
var SNIPER_SPEED := 650.0
var SPREAD_FAN_STEP := 0.314159
var DMG_SINGLE := 12
var DMG_SPREAD := 10
var DMG_LASER := 20
var DMG_HOMING := 12
var DMG_SNIPER := 21

var max_hp: int = 80
var hp: int = 80
## 弹药轮换序列（StringName：single/spread3/spread5/laser/weak_homing/sniper）
var ammo_sequence: Array = [&"single"]
## 开火间隔范围（每座炮台独立计时）
var fire_interval: Vector2 = Vector2(2.0, 2.4)
## 弱锁定参数
var turn_rate: float = 2.0  # 炮塔转向速度上限（rad/s，机械转台感）
var homing_turn_rate: float = 1.5
var homing_time: float = 0.6
var spread_deg: float = 7.0

var _rising: bool = false
var _ceased: bool = false
var _fire_timer: float = 0.0
var _ammo_index: int = 0
## 当前朝向（炮口方向，初始直指下方玩家区域）
var _facing: float = PI / 2.0

@onready var _sprite: Sprite2D = $Sprite2D
@onready var _hp_bar: SegmentedBar = $HpBar


## setup() 在入树/_ready() 之前调用，不能用 @onready 变量
func setup(p_hp: int, p_ammo: Array, p_fire_interval: Vector2, weak_lock: Dictionary) -> void:
	max_hp = maxi(1, p_hp)
	hp = max_hp
	ammo_sequence.clear()
	for a in p_ammo:
		ammo_sequence.append(StringName(a))
	fire_interval = p_fire_interval
	turn_rate = float(weak_lock.get("turn_rate", turn_rate))
	homing_turn_rate = float(weak_lock.get("homing_turn_rate", homing_turn_rate))
	homing_time = float(weak_lock.get("homing_time", homing_time))
	spread_deg = float(weak_lock.get("spread_deg", spread_deg))


func _ready() -> void:
	add_to_group("enemy")
	GameState.register_enemy(self)
	# 数值配置缓存（启动一次读入）
	SINGLE_SPEED = GameState.cfg("enemies.bullet_speed", SINGLE_SPEED)
	SPREAD_SPEED = GameState.cfg("enemies.spread_bullet_speed", SPREAD_SPEED)
	LASER_SPEED = GameState.cfg("enemies.laser_bullet_speed", LASER_SPEED)
	HOMING_SPEED = GameState.cfg("boss.homing_bullet_speed", HOMING_SPEED)
	SNIPER_SPEED = GameState.cfg("boss.sniper_bullet_speed", SNIPER_SPEED)
	SPREAD_FAN_STEP = GameState.cfg("enemies.spread_fan_step", SPREAD_FAN_STEP)
	DMG_SINGLE = GameState.cfg("enemies.bullet_damage.single", DMG_SINGLE)
	DMG_SPREAD = GameState.cfg("enemies.bullet_damage.spread", DMG_SPREAD)
	DMG_LASER = GameState.cfg("enemies.bullet_damage.laser", DMG_LASER)
	DMG_HOMING = GameState.cfg("boss.bullet_damage.homing", DMG_HOMING)
	DMG_SNIPER = GameState.cfg("boss.bullet_damage.sniper", DMG_SNIPER)
	_hp_bar.max_value = 100.0
	_hp_bar.value = 100.0
	_hp_bar.fill_color = Color(1.0, 0.25, 0.75)  # 精英品红
	_fire_timer = randf_range(fire_interval.x, fire_interval.y)


func _exit_tree() -> void:
	GameState.unregister_enemy(self)


## 升起充能动画（盖板旋开炮塔升起，约 rise_time 秒；期间不可被攻击）
func rise(duration: float) -> void:
	_rising = true
	monitoring = false
	scale = Vector2.ZERO
	modulate.a = 0.0
	var tween := create_tween()
	tween.set_parallel(true)
	tween.tween_property(self, "scale", Vector2.ONE, duration).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)
	tween.tween_property(self, "modulate:a", 1.0, duration * 0.6)


## 充能完毕（由事件编排在倒计时开始时调用）：可被攻击、开始开火
func activate() -> void:
	_rising = false
	monitoring = true


## 超时撤退：停火并收回盖板（弹药不再产生）
func cease_fire_and_retract() -> void:
	if _ceased or hp <= 0:
		return
	_ceased = true
	monitoring = false
	var tween := create_tween()
	tween.set_parallel(true)
	tween.tween_property(self, "scale", Vector2.ZERO, 0.8).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_IN)
	tween.tween_property(self, "modulate:a", 0.0, 0.8)
	tween.chain().tween_callback(queue_free)


func _physics_process(delta: float) -> void:
	if _rising or _ceased or hp <= 0:
		return
	# 弱锁定索敌：限速转向玩家（lerp_angle 缓动 + rad/s 上限，机械转台感）
	if GameState.player_ref != null:
		var target := (GameState.player_ref.global_position - global_position).angle()
		var max_step := turn_rate * delta
		var diff := wrapf(target - _facing, -PI, PI)
		_facing += clampf(diff, -max_step, max_step)
		_sprite.rotation = _facing - PI / 2.0  # 贴图炮口朝上（-Y），旋转到朝向
	_fire_timer -= delta
	if _fire_timer <= 0.0:
		_fire_timer = randf_range(fire_interval.x, fire_interval.y)
		_fire_current_ammo()


## 开火朝向 = 炮塔当前朝向 + ±spread_deg 出膛散布（非精确指向）
func _fire_dir() -> Vector2:
	return Vector2.RIGHT.rotated(_facing + deg_to_rad(randf_range(-spread_deg, spread_deg)))


func _fire_current_ammo() -> void:
	if ammo_sequence.is_empty():
		return
	var ammo: StringName = ammo_sequence[_ammo_index % ammo_sequence.size()]
	_ammo_index += 1
	match ammo:
		&"spread3":
			_fire_fan(3)
		&"spread5":
			_fire_fan(5)
		&"laser":
			_spawn_bullet(_fire_dir(), LASER_SPEED, DMG_LASER, &"laser")
		&"weak_homing":
			var dir := _fire_dir()
			var b: Bullet = GameState.bullet_pool.fire(dir, HOMING_SPEED, DMG_HOMING, false, true, homing_time)
			b.homing_turn_rate = homing_turn_rate
			b.position = global_position + dir * 40.0
			b.set_meta("bullet_type", &"homing")
		&"sniper":
			_spawn_bullet(_fire_dir(), SNIPER_SPEED, DMG_SNIPER, &"sniper")
		_:
			_spawn_bullet(_fire_dir(), SINGLE_SPEED, DMG_SINGLE, &"single")


## 扇形散射：以开火朝向为中心 ±(n-1)/2 步展开
func _fire_fan(count: int) -> void:
	var center := _fire_dir()
	var half := (count - 1) / 2.0
	for i in count:
		_spawn_bullet(center.rotated(SPREAD_FAN_STEP * (float(i) - half)), SPREAD_SPEED, DMG_SPREAD, &"spread")


func _spawn_bullet(dir: Vector2, bullet_speed: float, dmg: int, p_type: StringName) -> void:
	var b: Bullet = GameState.bullet_pool.fire(dir, bullet_speed, dmg, false)
	b.position = global_position + dir * 40.0
	b.set_meta("bullet_type", p_type)
	if p_type == &"laser":
		# 细长高亮快速弹（与敌机 laser 弹同表现，polygon 尖端朝 +x 即飞行方向）
		var poly := b.get_node("Polygon2D") as Polygon2D
		poly.scale = Vector2(2.2, 0.55)
		poly.color = Color(1.0, 0.85, 0.35)


func take_damage(amount: int, _score_scale: float = 1.0) -> void:
	if hp <= 0 or _rising or _ceased:
		return
	hp -= amount
	_hp_bar.value = clampf(float(hp) / float(max_hp), 0.0, 1.0) * 100.0
	_sprite.modulate = Color(2.0, 2.0, 2.0)  # 受击闪白
	var tween := create_tween()
	tween.tween_property(_sprite, "modulate", Color.WHITE, 0.1)
	if hp <= 0:
		die()


func die() -> void:
	GameState.play_sfx(GameState.SFX_EXPLOSION)
	GameState.shake(GameState.cfg("effects.shake.enemy_die", 5.0))
	Explosion.spawn_at(get_parent(), global_position, 1.0)
	died.emit(self)
	queue_free()
