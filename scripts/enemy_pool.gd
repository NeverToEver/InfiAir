class_name EnemyPool
extends Node
## 敌机对象池（挂在 Main 下，模式同 BulletPool）：复用 enemy.tscn 实例。
## 活跃敌机挂 Main 下（清场/测试遍历可见），闲置收回池节点下。
## USE_POOL=false 时退化为纯 instantiate/free（性能 A/B 对照开关）。

const ENEMY_SCENE: PackedScene = preload("res://scenes/enemy.tscn")
const USE_POOL := true

var _free: Array[Enemy] = []


func _ready() -> void:
	GameState.enemy_pool = self


## C21 修复：场景卸载时清空全局注册，避免 GameState.enemy_pool 悬空
func _exit_tree() -> void:
	if GameState.enemy_pool == self:
		GameState.enemy_pool = null


## 闲置实例数（A7 遗留清理：测试/诊断公开查询，替代 _free 直读）
func free_count() -> int:
	return _free.size()


func spawn(
	config: Dictionary,
	strategy: StringName,
	p_difficulty: float,
	pos: Vector2,
	p_bullet_type: StringName = &"",
) -> Enemy:
	var e: Enemy = null
	if USE_POOL:
		while not _free.is_empty():
			e = _free.pop_back()
			if is_instance_valid(e):
				break
			e = null
	if e == null:
		e = ENEMY_SCENE.instantiate()
		e.set_pool(self)
		get_parent().add_child(e)
	elif e.get_parent() != get_parent():
		# R07（2026-08-05 独立审计）：Q19 只修了回收侧——spawn 侧 reparent 同样触发
		# e._exit_tree，置位防 unbind_enemy 误发 entity_unregistered（与 _reparent_deferred 对称）
		e.set_repooling(true)
		e.reparent(get_parent())
		e.set_repooling(false)
	e.position = pos
	e.reactivate(config, strategy, p_difficulty, p_bullet_type)
	return e


## 回收：重置状态并移回池节点下（不销毁）。reparent 延迟到空闲时执行；
## 若敌机在延迟执行前已被重激活（同帧复用）则跳过。幂等防重复回收。
func release(e: Enemy) -> void:
	if not is_instance_valid(e) or _free.has(e):
		return
	if not USE_POOL:
		e.queue_free()
		return
	e.deactivate()
	_free.append(e)
	_reparent_deferred.call_deferred(e)


func _reparent_deferred(e: Enemy) -> void:
	if is_instance_valid(e) and not e.is_active():
		# 4.6 实测 reparent 会触发 e._exit_tree，置位防 forget 把敌机误清出 _free
		e.set_repooling(true)
		e.reparent(self)
		e.set_repooling(false)


## 被外部 queue_free（清场/测试/场景重载）时从池清单移除。
func forget(e: Enemy) -> void:
	_free.erase(e)
