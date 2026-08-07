class_name EntityManager
extends RefCounted
## 统一实体管理器（docs/ENTITY_MANAGER.md）：对局实体注册表 + 批量操作 + 生命周期信号。
## 由 A2 阶段 4 的 EntityRegistry 演进（2026-08-05）：数据内核（注册表/热路径索引/特殊引用）
## 保持不变，新增统一注册样板（bind_enemy 一行收敛四处重复）、生命周期信号（新功能订阅口）、
## 批量操作 API（清场/索敌/计数统一入口）。GameState 用属性 setter/getter 转发，外部访问
## 语法不变（调用方零改动）。
##
## 登记维护：
## - 单位（Enemy/Boss/TurretBattery/FormationCraft）在 _ready 调 bind_enemy、_exit_tree 调
##   unbind_enemy（add_to_group("enemy") + 注册/注销 + 信号，一行样板）
## - 池化单位（Enemy）的 reactivate/deactivate 保持直接 register_enemy/unregister_enemy
##   （幂等；与 bind 无冲突，_repooling reparent 语义不变）
## - player 单独缓存 player_ref / player_hitbox
## - bullet_pool / enemy_pool / aim_frame_layer / camera_ref 由各自 _ready/_exit_tree 登记
## - 敌弹注册表 enemy_bullets 由 bullet 维护（death_replay 数据源，P0-1）
## 热路径不变量：has_enemy O(1)；for_each_enemy/clear_enemies/count_enemies 均为
## 非热路径批量入口（消费方手写遍历的收敛点），原位遍历 + 失效实例跳过。

## 实体注册信号（新功能订阅口：如全屏冻结/统计，无需改动单位类）
signal entity_registered(node: Node)
signal entity_unregistered(node: Node)

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
## 触屏虚拟输入层（mobile touch，2026-08-07）：由 Main._ready 创建并登记（参照 aim_frame_layer）
var virtual_controls: VirtualControls = null


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


# ---------------- 统一注册样板（2026-08-05，新单位接入一行） ----------------


## 统一单位绑定：add_to_group("enemy") + register_enemy + entity_registered 信号。
## 单位类 _ready 调一次（对应 _exit_tree 调 unbind_enemy）；幂等（重复绑定安全）。
## 注：池化路径（reactivate/deactivate）保持直接 register/unregister，不受本方法影响；
## 本方法只覆盖 _ready/_exit_tree 的样板（autoplay 组↔注册表一致性不变量保持）。
func bind_enemy(node: Node) -> void:
	node.add_to_group("enemy")
	register_enemy(node)
	entity_registered.emit(node)


## 统一单位解绑：_exit_tree 时调用（unregister + entity_unregistered 信号；组随节点释放自动退出）
func unbind_enemy(node: Node) -> void:
	unregister_enemy(node)
	entity_unregistered.emit(node)


# ---------------- 批量操作 API（清场/索敌/计数统一入口；非热路径） ----------------


## 安全遍历注册表：跳过失效实例；predicate（可选，接收 Node 返回 bool）过滤。
## 原位遍历（热路径消费者如需收集请自行缓冲；清场/索敌/冻结等非热路径适用）。
func for_each_enemy(action: Callable, predicate: Callable = Callable()) -> void:
	for node in enemies:
		if not is_instance_valid(node):
			continue  # 失效实例跳过（注册表可能持有 queue_free 中的节点）
		if predicate.is_valid() and not (predicate.call(node) as bool):
			continue
		action.call(node)


## 批量清除注册表实体：predicate（可选）为「保留项」过滤（返回 true 不清除，如 Boss）。
## 返回清除数。失效实例直接忽略；清除走 queue_free（帧末生效，遍历安全）。
func clear_enemies(predicate: Callable = Callable()) -> int:
	var cleared := 0
	for node in enemies.duplicate():
		if not is_instance_valid(node):
			continue
		if predicate.is_valid() and (predicate.call(node) as bool):
			continue
		node.queue_free()
		cleared += 1
	return cleared


## 计数（谓词可选过滤；失效实例不计）。spread 上限/统计用。
func count_enemies(predicate: Callable = Callable()) -> int:
	var count := 0
	for node in enemies:
		if not is_instance_valid(node):
			continue
		if predicate.is_valid() and not (predicate.call(node) as bool):
			continue
		count += 1
	return count
