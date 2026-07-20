class_name Bullet
extends Area2D
## 直线子弹，玩家弹与敌弹共用此场景，通过 setup() 区分阵营。

var direction: Vector2 = Vector2.DOWN
var speed: float = 900.0
var damage: int = 1
var is_player_bullet: bool = true
var homing: bool = false
var homing_time: float = 0.0

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
	position += direction * speed * delta
	if not Rect2(-80.0, -80.0, 2080.0, 1240.0).has_point(position):
		queue_free()


func _on_area_entered(area: Area2D) -> void:
	if is_player_bullet:
		if area.is_in_group("enemy"):
			area.take_damage(damage)
			queue_free()
	elif area.is_in_group("player_hitbox"):
		area.get_parent().take_damage()
		queue_free()
