class_name Bullet
extends Area2D
## 直线子弹，玩家弹与敌弹共用此场景，通过 setup() 区分阵营。

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

const EXPLOSIVE_RADIUS := 80.0
const SLOW_FIELD_RADIUS := 300.0
const SLOW_FIELD_FACTOR := 0.6

var _homing_elapsed: float = 0.0

@onready var _polygon: Polygon2D = $Polygon2D


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


func _ready() -> void:
	rotation = direction.angle()
	if is_player_bullet:
		collision_layer = 2  # 第 2 层：player_bullet
		collision_mask = 4  # 命中第 3 层：enemy
		_polygon.color = Color(1.0, 0.9, 0.25)
	else:
		collision_layer = 8  # 第 4 层：enemy_bullet
		collision_mask = 1  # 命中第 1 层：player
		_polygon.color = Color(1.0, 0.25, 0.2)
	area_entered.connect(_on_area_entered)


func _process(delta: float) -> void:
	if homing and _homing_elapsed < homing_time:
		_homing_elapsed += delta
		var players := get_tree().get_nodes_in_group("player")
		if players.size() > 0:
			var target: Node2D = players[0]
			var new_angle := lerp_angle(
				direction.angle(), (target.global_position - global_position).angle(), 4.0 * delta
			)
			direction = Vector2.RIGHT.rotated(new_angle)
			rotation = new_angle
	position += direction * speed * _speed_factor() * delta
	if not Rect2(-80.0, -80.0, 2080.0, 1240.0).has_point(position):
		queue_free()


## 慢速力场 buff：玩家 300px 内敌弹减速 40%。
func _speed_factor() -> float:
	if is_player_bullet or GameState.buff_count(&"slow_field") == 0:
		return 1.0
	var players := get_tree().get_nodes_in_group("player")
	if players.size() > 0:
		var player := players[0] as Node2D
		if global_position.distance_to(player.global_position) < SLOW_FIELD_RADIUS:
			return SLOW_FIELD_FACTOR
	return 1.0


## 爆炸弹 buff：命中时对周围敌人造成 50% AoE 伤害。
func _explode(exclude: Area2D) -> void:
	var aoe_damage := maxi(1, damage / 2)
	for node in get_tree().get_nodes_in_group("enemy"):
		var e := node as Area2D
		if e != exclude and e.global_position.distance_to(global_position) <= EXPLOSIVE_RADIUS:
			e.take_damage(aoe_damage)
	Explosion.spawn_at(get_parent(), global_position, 0.6)
	GameState.play_sfx(GameState.SFX_EXPLOSION, -6.0)


func _on_area_entered(area: Area2D) -> void:
	if is_player_bullet:
		if area.is_in_group("enemy"):
			area.take_damage(damage)
			if explosive:
				_explode(area)
			if pierce > 0:
				pierce -= 1
			else:
				queue_free()
	elif area.is_in_group("player_hitbox"):
		area.get_parent().take_damage()
		queue_free()
