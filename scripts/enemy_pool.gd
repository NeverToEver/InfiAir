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


func spawn(config: Dictionary, strategy: StringName, p_difficulty: float, pos: Vector2) -> Enemy:
	var e: Enemy = null
	if USE_POOL:
		while not _free.is_empty():
			e = _free.pop_back()
			if is_instance_valid(e):
				break
			e = null
	if e == null:
		e = ENEMY_SCENE.instantiate()
		e._pool = self
		get_parent().add_child(e)
	elif e.get_parent() != get_parent():
		e.reparent(get_parent())
	e.position = pos
	e.reactivate(config, strategy, p_difficulty)
	return e


## 回收：重置状态并移回池节点下（不销毁）。reparent 延迟到空闲时执行。
func release(e: Enemy) -> void:
	if not is_instance_valid(e):
		return
	if not USE_POOL:
		e.queue_free()
		return
	e.deactivate()
	_free.append(e)
	e.reparent.call_deferred(self)


## 被外部 queue_free（清场/测试/场景重载）时从池清单移除。
func forget(e: Enemy) -> void:
	_free.erase(e)
