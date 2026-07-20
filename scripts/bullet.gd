class_name Bullet
extends Area2D
## 直线子弹，玩家弹与敌弹共用此场景，通过 setup()/activate() 区分阵营。
## 正常产弹走 GameState.bullet_pool（对象池复用）；直接实例化（测试）走兼容路径。

var direction: Vector2 = Vector2.DOWN
var speed: float = 900.0
var damage: int = 1
var is_player_bullet: bool = true
var homing: bool = false
var homing_time: float = 0.0
## 穿透剩余次数（玩家弹，穿透弹 buff）
var pierce: int = 0
## 命中产生 AoE 爆炸（玩家弹，爆炸弹 buff）
var explosive: bool = false
## 击毁得分系数（母舰弹丸为 1/3）
var score_scale: float = 1.0

const EXPLOSIVE_RADIUS := 80.0
const SLOW_FIELD_RADIUS := 300.0
const SLOW_FIELD_FACTOR := 0.6

var _homing_elapsed: float = 0.0
var _pool: Node = null

@onready var _polygon: Polygon2D = $Polygon2D


## 兼容路径：直接实例化时 setup() 后由 _ready() 应用阵营外观。
func setup(
	p_direction: Vector2,
	p_speed: float,
	p_damage: int,
	p_is_player: bool,
	p_homing: bool = false,
	p_homing_time: float = 0.0
) -> void:
	direction = p_direction.normalized()
	speed = p_speed
	damage = p_damage
	is_player_bullet = p_is_player
	homing = p_homing
	homing_time = p_homing_time


## 池化路径：激活并重置全部状态（含上一任使用者的外观/标记）。
func activate(
	p_direction: Vector2,
	p_speed: float,
	p_damage: int,
	p_is_player: bool,
	p_homing: bool = false,
	p_homing_time: float = 0.0
) -> void:
	setup(p_direction, p_speed, p_damage, p_is_player, p_homing, p_homing_time)
	_homing_elapsed = 0.0
	pierce = 0
	explosive = false
	score_scale = 1.0
	visible = true
	monitoring = true
	set_process(true)
	_apply_faction()


## 池化回收：停用但保留实例。
func deactivate() -> void:
	visible = false
	set_deferred("monitoring", false)
	set_process(false)
	position = Vector2(-500.0, -500.0)


func _ready() -> void:
	area_entered.connect(_on_area_entered)
	_apply_faction()


func _exit_tree() -> void:
	# 被外部 queue_free（清场/测试/场景重载）时通知池移除引用
	if _pool != null:
		_pool.forget(self)


func _apply_faction() -> void:
	rotation = direction.angle()
	# 重置外观（敌机/Boss 激光长弹、母舰弹的自定义外观）
	scale = Vector2.ONE
	modulate = Color.WHITE
	_polygon.scale = Vector2.ONE
	if has_meta("bullet_type"):
		remove_meta("bullet_type")
	if is_player_bullet:
		collision_layer = 2  # 第 2 层：player_bullet
		collision_mask = 4  # 命中第 3 层：enemy
		_polygon.color = Color(1.0, 0.9, 0.25)
	else:
		collision_layer = 8  # 第 4 层：enemy_bullet
		collision_mask = 1  # 命中第 1 层：player
		_polygon.color = Color(1.0, 0.25, 0.2)


func _despawn() -> void:
	if _pool != null and is_instance_valid(_pool):
		_pool.release(self)
	else:
		queue_free()


func _process(delta: float) -> void:
	if homing and _homing_elapsed < homing_time:
		_homing_elapsed += delta
		if GameState.player_ref != null:
			var new_angle := lerp_angle(
				direction.angle(),
				(GameState.player_ref.global_position - global_position).angle(),
				4.0 * delta
			)
			direction = Vector2.RIGHT.rotated(new_angle)
			rotation = new_angle
	position += direction * speed * _speed_factor() * delta
	if not Rect2(-80.0, -80.0, 2080.0, 1240.0).has_point(position):
		_despawn()


## 慢速力场 buff：玩家 300px 内敌弹减速 40%。
func _speed_factor() -> float:
	if is_player_bullet or GameState.buff_count(&"slow_field") == 0:
		return 1.0
	if GameState.player_ref != null:
		if global_position.distance_to(GameState.player_ref.global_position) < SLOW_FIELD_RADIUS:
			return SLOW_FIELD_FACTOR
	return 1.0


## 爆炸弹 buff：命中时对周围敌人造成 50% AoE 伤害。
func _explode(exclude: Area2D) -> void:
	var aoe_damage := maxi(1, damage / 2)
	for node in GameState.enemies:
		var e := node as Area2D
		if e != exclude and e.global_position.distance_to(global_position) <= EXPLOSIVE_RADIUS:
			e.take_damage(aoe_damage)
	Explosion.spawn_at(get_parent(), global_position, 0.6)
	GameState.play_sfx(GameState.SFX_EXPLOSION, -6.0)


func _on_area_entered(area: Area2D) -> void:
	if is_player_bullet:
		if area.is_in_group("enemy"):
			area.take_damage(damage, score_scale)
			if explosive:
				_explode(area)
			if pierce > 0:
				pierce -= 1
			else:
				_despawn()
	elif area.is_in_group("player_hitbox"):
		area.get_parent().take_damage()
		_despawn()
