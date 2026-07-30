class_name FormationCraft
extends Area2D
## 轰炸编队事件·编队战机（docs/FORMATION_STRIKE_EVENT.md 第 4.1 节）：
## 楔形编队成员，注册 enemy 组与 GameState.enemies（玩家子弹/激光可命中）。
## 自身无 AI：位置/朝向由 FormationStrikeEvent._process 按编队锚点驱动。
## 被击坠：爆炸 + 注销注册表，击坠得分由事件编排结算。

signal died(craft: FormationCraft)

const TEXTURE: Texture2D = preload("res://assets/sprites/enemy_ship_2.png")

var max_hp: int = 60
var hp: int = 60

var _sprite: Sprite2D


## setup() 在入树/_ready() 之前调用
func setup(p_hp: int) -> void:
	max_hp = maxi(1, p_hp)
	hp = max_hp


func _init() -> void:
	collision_layer = 4  # 第 3 层：enemy（玩家子弹以 enemy 组结算）
	collision_mask = 0
	_sprite = Sprite2D.new()
	_sprite.texture = TEXTURE
	_sprite.scale = Vector2.ONE * 0.9 * GameState.world_scale  # 设计值 0.9 × 全局缩放
	add_child(_sprite)
	var shape := CollisionShape2D.new()
	var circle := CircleShape2D.new()
	circle.radius = 26.0 * GameState.world_scale
	shape.shape = circle
	add_child(shape)


func _ready() -> void:
	add_to_group("enemy")
	GameState.register_enemy(self)


func _exit_tree() -> void:
	GameState.unregister_enemy(self)


func take_damage(amount: int, _score_scale: float = 1.0) -> void:
	if hp <= 0:
		return
	hp -= amount
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
