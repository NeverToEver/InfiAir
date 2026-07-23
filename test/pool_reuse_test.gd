extends Node
## 对象池复用回归测试（2026-07-22 autoplay 探针发现的泄漏修复）：
## 4.6 实测 reparent 会触发 _exit_tree，池回收 reparent 曾导致 forget 误清 _free，
## 子弹/敌机池只进不出（autoplay 中 BulletPool 子节点 90s 涨到 803）。

var _pass := 0
var _fail := 0


func _ready() -> void:
	GameState.delete_save()
	await get_tree().process_frame
	await _test_bullet_pool()
	await _test_enemy_pool()
	print("[POOL] %d PASS, %d FAIL" % [_pass, _fail])
	get_tree().quit(1 if _fail > 0 else 0)


func _check(cond: bool, name: String) -> void:
	if cond:
		_pass += 1
		print("[PASS] ", name)
	else:
		_fail += 1
		printerr("[FAIL] ", name)


func _test_bullet_pool() -> void:
	var main := Node2D.new()
	add_child(main)
	var pool := BulletPool.new()
	main.add_child(pool)
	var b1 := pool.fire(Vector2.DOWN, 100.0, 1, true)
	pool.release(b1)
	_check(pool._free.size() == 1, "bullet: release 后 _free=1")
	await get_tree().process_frame
	await get_tree().process_frame
	_check(pool._free.size() == 1, "bullet: reparent 后仍在 _free（forget 未误清）")
	_check(b1.get_parent() == pool, "bullet: 闲置弹收回池节点下")
	var b2 := pool.fire(Vector2.UP, 100.0, 1, false)
	_check(b2 == b1, "bullet: 再次 fire 复用同一实例")
	_check(pool._free.is_empty() and pool.get_child_count() == 0, "bullet: 复用后池清空")
	# 外部 queue_free 路径仍应 forget
	pool.release(b2)
	await get_tree().process_frame
	b2.queue_free()
	await get_tree().process_frame
	_check(pool._free.is_empty(), "bullet: 外部销毁后 forget 生效")
	main.queue_free()


func _test_enemy_pool() -> void:
	var main := Node2D.new()
	add_child(main)
	var pool := EnemyPool.new()
	main.add_child(pool)
	var config: Dictionary = preload("res://scripts/spawner.gd").ENEMY_TYPES[0]
	var e1 := pool.spawn(config, &"straight", 1.0, Vector2(100, 100))
	pool.release(e1)
	_check(pool._free.size() == 1, "enemy: release 后 _free=1")
	await get_tree().process_frame
	await get_tree().process_frame
	_check(pool._free.size() == 1, "enemy: reparent 后仍在 _free（forget 未误清）")
	_check(e1.get_parent() == pool, "enemy: 闲置敌机收回池节点下")
	_check(not GameState.enemies.has(e1), "enemy: 回收后注销注册表")
	var e2 := pool.spawn(config, &"straight", 1.0, Vector2(200, 100))
	_check(e2 == e1, "enemy: 再次 spawn 复用同一实例")
	_check(GameState.enemies.has(e2), "enemy: 复用后重新注册")
	pool.release(e2)
	await get_tree().process_frame
	main.queue_free()
