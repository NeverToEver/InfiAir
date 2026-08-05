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
## G010：enemies 的 O(1) 存在性索引（追踪弹每帧 has 判定，Array 线性扫描热路径开销）
var _enemy_set: Dictionary = {}  # node -> true
## P0-1（2026-08-05 审计）：敌弹注册表——death_replay 录制数据源（替代每帧 get_children + cast）
var enemy_bullets: Array[Bullet] = []
var _enemy_bullet_set: Dictionary = {}  # node -> true
var player_ref: Node2D = null
var player_hitbox: Area2D = null
var bullet_pool: BulletPool = null
var enemy_pool: EnemyPool = null
var aim_frame_layer: AimFrameLayer = null
var camera_ref: Camera2D = null


func register_enemy(node: Node) -> void:
	if not enemies.has(node):
		enemies.append(node)
	_enemy_set[node] = true


func unregister_enemy(node: Node) -> void:
	enemies.erase(node)
	_enemy_set.erase(node)


## G010：注册表存在性判定 O(1)（替代 enemies.has() 线性扫描；语义同注册表包含，deactivate 即移除）
func has_enemy(node: Node) -> bool:
	return _enemy_set.has(node)


## P0-1：敌弹登记（幂等）。维护点：bullet._apply_faction（activate/_ready/reflect 阵营翻转）、
## bullet.deactivate（回收）、bullet._exit_tree（外部销毁）。reflect 翻转阵营自动切换注册。
func register_enemy_bullet(b: Bullet) -> void:
	if not enemy_bullets.has(b):
		enemy_bullets.append(b)
	_enemy_bullet_set[b] = true


## P0-1：敌弹注销（幂等）
func unregister_enemy_bullet(b: Bullet) -> void:
	enemy_bullets.erase(b)
	_enemy_bullet_set.erase(b)
