extends Node
## 统一实体管理器集成测试（docs/ENTITY_MANAGER.md）：
## 场景1 绑定样板：bind_enemy 一行（入 enemy 组 + 注册表 + 幂等），unbind_enemy 对称退出。
## 场景2 生命周期信号：entity_registered / entity_unregistered 各触发一次。
## 场景3 批量 API：count_enemies 计数、for_each_enemy 谓词过滤、clear_enemies 保留项清除
##    （真实敌机池化实例；清除后注册表注销、池回收语义保持）。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _ready() -> void:
	GameState.delete_save()
	GameState.set_difficulty(&"medium")
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	GameState.login_guest()
	add_child(main_scene.instantiate())
	var player: Player = get_node("Main/Player")
	player.set_auto_fire(false)
	player.set_invincible(999.0)
	player.position = Vector2(960.0, 800.0)
	GameState.set_milestone_override(999999)
	await get_tree().process_frame
	await get_tree().process_frame
	var spawner: Node = get_node("Main/Spawner")
	spawner.set_process(false)  # 手动驱动，保证确定性

	# ================= 场景 1：绑定样板 + 幂等 =================
	var probe := Node.new()
	add_child(probe)
	GameState.bind_enemy(probe)
	_check(GameState.enemies.has(probe), "场景1：bind_enemy 后进入注册表")
	_check(probe.is_in_group("enemy"), "场景1：bind_enemy 同步加入 enemy 组")
	GameState.bind_enemy(probe)
	var count := 0
	for n in GameState.enemies:
		if n == probe:
			count += 1
	_check(count == 1, "场景1：重复绑定幂等（注册表单条）")
	GameState.unbind_enemy(probe)
	_check(not GameState.enemies.has(probe), "场景1：unbind_enemy 后退出注册表")
	probe.queue_free()

	# ================= 场景 2：生命周期信号 =================
	var sig_seen: Array[String] = []
	GameState.entity_registered.connect(func(_n: Node) -> void: sig_seen.append("reg"))
	GameState.entity_unregistered.connect(func(_n: Node) -> void: sig_seen.append("unreg"))
	var probe2 := Node.new()
	add_child(probe2)
	GameState.bind_enemy(probe2)
	GameState.unbind_enemy(probe2)
	_check(sig_seen.size() == 2 and sig_seen[0] == "reg" and sig_seen[1] == "unreg", "场景2：注册/解绑信号各触发一次")
	probe2.queue_free()

	# ================= 场景 3：批量 API（真实敌机池化实例） =================
	var config: Dictionary = spawner.ENEMY_TYPES[0]
	var pool := GameState.enemy_pool
	var e1: Enemy = pool.spawn(config, &"straight", 1.0, Vector2(400.0, 500.0))
	var e2: Enemy = pool.spawn(config, &"straight", 1.0, Vector2(520.0, 500.0))
	e2.set_meta("keep", true)  # clear_enemies 保留项标记（模拟 Boss 语义）
	_check(GameState.enemies.has(e1) and GameState.enemies.has(e2), "场景3：池化生成实例注册进注册表")
	_check(GameState.count_enemies(func(e: Node) -> bool: return e is Enemy) == 2, "场景3：count_enemies 计数 = 2")
	var visited: Array = []
	GameState.for_each_enemy(func(e: Node) -> void: visited.append(e), func(e: Node) -> bool: return not e.has_meta("keep"))
	_check(visited.size() == 1 and visited[0] == e1, "场景3：for_each_enemy 谓词过滤（排除保留项）")
	# 失效实例跳过：queue_free 一帧后注册表可能仍持有（帧末释放），先确认 for_each 不崩
	var before := GameState.count_enemies()
	_check(before >= 2, "场景3：清理前注册表 ≥2（P4：基准断言前置，原在清理后检查失去意义）")
	var cleared := GameState.clear_enemies(func(e: Node) -> bool: return e.has_meta("keep"))
	_check(cleared == 1, "场景3：clear_enemies 清除 1 个（保留 keep 项）")
	await get_tree().process_frame
	_check(GameState.count_enemies() == 1, "场景3：清除后注册表仅剩保留项")
	_check(GameState.enemies_has(e2), "场景3：保留项 e2 仍在注册表")
	# 清理遗留：e2 释放（enemy._exit_tree 自动从池清单移除，幂等）
	e2.queue_free()
	await get_tree().process_frame

	print("ENTITY MANAGER TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
