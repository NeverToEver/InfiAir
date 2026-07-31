class_name EntityRegistry
extends RefCounted
## A2 阶段 4：对局实体注册表剥离（docs/AUDIT_VAULT.md A2）。
## 热路径缓存，避免每帧 get_nodes_in_group 分配。数据归本类；
## GameState 用属性 setter/getter 转发，外部访问语法不变（调用方零改动）。
##
## 登记维护：
## - enemy/boss 在 _ready/_exit_tree 时 register_enemy/unregister_enemy
## - player 单独缓存 player_ref / player_hitbox
## - bullet_pool / enemy_pool / aim_frame_layer / camera_ref 由各自 _ready/_exit_tree 登记

var enemies: Array[Node] = []
var player_ref: Node2D = null
var player_hitbox: Area2D = null
var bullet_pool: BulletPool = null
var enemy_pool: EnemyPool = null
var aim_frame_layer: AimFrameLayer = null
var camera_ref: Camera2D = null


func register_enemy(node: Node) -> void:
	if not enemies.has(node):
		enemies.append(node)


func unregister_enemy(node: Node) -> void:
	enemies.erase(node)
