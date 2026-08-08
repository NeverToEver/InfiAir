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
	var pool = load("res://csharp/godot/BulletPool.cs").new()  # 随批次 A 重定型：C# 类不能以类名 new
	main.add_child(pool)
	var b1 = pool.Fire(Vector2.DOWN, 100.0, 1, true)
	pool.Release(b1)
	_check(pool.FreeCount() == 1, "bullet: release 后 _free=1")
	await get_tree().process_frame
	await get_tree().process_frame
	_check(pool.FreeCount() == 1, "bullet: reparent 后仍在 _free（forget 未误清）")
	_check(b1.get_parent() == pool, "bullet: 闲置弹收回池节点下")
	var b2 = pool.Fire(Vector2.UP, 100.0, 1, false)
	_check(b2 == b1, "bullet: 再次 fire 复用同一实例")
	_check(pool.FreeCount() == 0 and pool.get_child_count() == 0, "bullet: 复用后池清空")
	# 外部 queue_free 路径仍应 forget
	pool.Release(b2)
	await get_tree().process_frame
	b2.queue_free()
	await get_tree().process_frame
	_check(pool.FreeCount() == 0, "bullet: 外部销毁后 forget 生效")
	# M1（2026-08-06 审计）：self_modulate 染色残留——laser 黄/Boss 重弹橙/致死高亮红
	# 等 P0-3 写入 sprite.self_modulate 的 tint 在 _apply_faction 无对等复位，池化复用
	# 带旧 tint；模拟染色后回收再复用断言复位白
	var b3 = pool.Fire(Vector2.DOWN, 100.0, 1, false)
	b3.SpriteNode().self_modulate = Color(1.0, 0.85, 0.35)  # 模拟 laser 染色写入
	pool.Release(b3)
	await get_tree().process_frame
	await get_tree().process_frame
	var b4 = pool.Fire(Vector2.DOWN, 100.0, 1, false)
	_check(b4 == b3 and b4.SpriteNode().self_modulate == Color.WHITE, "bullet: 复用后 self_modulate 复位白（M1 染色残留）")
	main.queue_free()


func _test_enemy_pool() -> void:
	var main := Node2D.new()
	add_child(main)
	var pool = load("res://csharp/godot/EnemyPool.cs").new()  # M3b：EnemyPool 迁 C#，经脚本资源实例化（不能以类名 new）
	main.add_child(pool)
	var config: Dictionary = preload("res://scripts/spawner.gd").ENEMY_TYPES[0]
	var e1 = pool.spawn(config, &"straight", 1.0, Vector2(100, 100))
	pool.release(e1)
	_check(pool.free_count() == 1, "enemy: release 后 _free=1")
	await get_tree().process_frame
	await get_tree().process_frame
	_check(pool.free_count() == 1, "enemy: reparent 后仍在 _free（forget 未误清）")
	_check(e1.get_parent() == pool, "enemy: 闲置敌机收回池节点下")
	_check(not GameState.enemies.has(e1), "enemy: 回收后注销注册表")
	var e2 = pool.spawn(config, &"straight", 1.0, Vector2(200, 100))
	_check(e2 == e1, "enemy: 再次 spawn 复用同一实例")
	_check(GameState.enemies.has(e2), "enemy: 复用后重新注册")
	# L02（2026-08-03 审查）：池化复用后 buff 信号必须重连——_ready 只执行一次而 _exit_tree
	# 每次 reparent 都断开连接，漏重连则 _slow_field_on 缓存冻结在陈旧值（首个回收循环后
	# slow_field buff 对该机静默失效）。白盒断言连接状态与刷新行为（E22 缓存字段；
	# buffs 为进程内存态，退出即清，无需收尾）
	_check(
		GameState.buffs_changed.is_connected(e2._on_buffs_changed),
		"enemy: 池化复用后 buffs_changed 保持连接（L02 slow_field 回归）",
	)
	GameState.add_buff(&"slow_field")
	_check(e2._slow_field_on, "enemy: 复用后 buff 变更即时刷新 slow_field 缓存")
	pool.release(e2)
	await get_tree().process_frame
	main.queue_free()
