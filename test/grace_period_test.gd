extends Node
## 受击宽限帧测试（2026-08-03 公平感机制一，docs/archive/2026-08-03-combat-fairness-plan.md §2）：
## 敌弹进入玩家 Hitbox 暂缓结算——窗口内离开（擦过边缘）不计伤（ghost hit 消灭）、
## 停留超窗结算且只一次、窗口边界两侧语义、既有受击流程（无敌/清弹/致死高亮）回归、
## 无敌期守卫不变、窗口期回收无悬挂 Timer。

var _failures: int = 0
var _bullet_script: Script = load("res://csharp/godot/Bullet.cs")  # 随批次 A 重定型：C# 类不能经类名 is 判定


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## 当前场内敌弹（玩家弹排除）
func _enemy_bullets() -> Array:  # 随批次 A 重定型：C# 类不能作元素类型注解
	var out: Array = []
	for child in get_node("Main").get_children():
		if is_instance_of(child, _bullet_script) and not child.IsPlayerBullet:  # 随批次 A 重定型：C# 类不能经类名 is 判定
			out.append(child)
	return out


func _free_enemy_bullets() -> void:
	for b in _enemy_bullets():
		b.queue_free()
	await get_tree().process_frame


## 重置玩家受击状态（无敌/帧标记/被动回血计时），便于逐条断言
func _reset_hit_state(player: Player) -> void:
	player.set_invincible(0.0)
	player.set_last_hit_frame(-1)
	player.set_since_damage(999.0)


func _ready() -> void:
	# 清理持久化状态，保证测试确定性
	GameState.delete_save()
	# L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
	var orig_high_score: int = GameState.high_score
	GameState.high_score = 0
	GameState.save_profile()
	add_child((load("res://scenes/main.tscn") as PackedScene).instantiate())
	var main := get_node("Main")
	var player: Player = get_node("Main/Player")
	player.set_auto_fire(false)  # 禁用自动开火，避免误伤与意外得分里程碑
	await get_tree().process_frame
	await get_tree().process_frame
	var spawner: Node = get_node("Main/Spawner")
	spawner.set_process(false)  # 停掉自动刷怪/Boss 调度，保证确定性
	for child in main.get_children():
		if is_instance_of(child, load("res://csharp/godot/Enemy.cs")) or is_instance_of(child, _bullet_script):  # M3b：Enemy 迁 C#，is 判定经脚本资源
			child.queue_free()
	await get_tree().process_frame
	player.position = Vector2(960.0, 800.0)

	# ================= 用例 1：切向快速穿过（停留 < 窗口）→ 无伤（area_exited 取消 Timer） =================
	GameState.health = 100.0
	_reset_hit_state(player)
	var edge_b = GameState.bullet_pool.Fire(Vector2.RIGHT, 600.0, 12, false)
	edge_b.position = player.position + Vector2(-30.0, 3.0)  # 水平弹道与 Hitbox 边缘带相交（y 偏移 3px）
	await get_tree().create_timer(0.2).timeout
	_check(GameState.health == 100.0, "用例1：切向快速穿过（停留 << 窗口）不计伤")
	await _free_enemy_bullets()

	# ================= 用例 2：停留 ≥ 窗口 → 受击 1 次且只 1 次 =================
	GameState.health = 100.0
	_reset_hit_state(player)
	var stay_b = GameState.bullet_pool.Fire(Vector2.DOWN, 0.0, 12, false)
	stay_b.position = player.position
	await get_tree().create_timer(0.1).timeout
	_check(GameState.health == 88.0, "用例2：停留 ≥ 窗口结算一次（-12）")
	await get_tree().create_timer(0.2).timeout
	_check(GameState.health == 88.0, "用例2：只结算一次（Timer 一次性 + 受击无敌）")
	await _free_enemy_bullets()

	# ================= 用例 3：窗口边界两侧——< 窗口无伤 / ≥ 窗口有伤 =================
	GameState.health = 100.0
	_reset_hit_state(player)
	var bdry = GameState.bullet_pool.Fire(Vector2.DOWN, 0.0, 12, false)
	bdry.position = player.position
	await get_tree().physics_frame
	await get_tree().physics_frame  # ≈0.033s < 0.05s 窗口
	_check(GameState.health == 100.0, "用例3：窗口内（<0.05s）未结算")
	await get_tree().create_timer(0.1).timeout
	_check(GameState.health == 88.0, "用例3：停留超窗（≥0.05s）结算")
	await _free_enemy_bullets()

	# ================= 用例 4：宽限结算后仍走既有受击流程（无敌计时 + 清弹） =================
	GameState.health = 100.0
	_reset_hit_state(player)
	var near1 = GameState.bullet_pool.Fire(Vector2.DOWN, 0.0, 10, false)
	near1.position = player.position + Vector2(100.0, 0.0)  # 受击清弹半径 250 内
	var hit_b = GameState.bullet_pool.Fire(Vector2.DOWN, 0.0, 10, false)
	hit_b.position = player.position
	await get_tree().create_timer(0.1).timeout
	_check(GameState.health == 90.0, "用例4：宽限结算走既有受击链路（-10）")
	_check(player.invincible_remaining() > 1.0, "用例4：结算后受击无敌计时生效")
	_check(not near1.visible, "用例4：结算后 250px 内敌弹被清（既有清弹语义）")
	await _free_enemy_bullets()

	# ================= 用例 5：无敌期内弹进入 → 不结算（take_damage 守卫回归） =================
	GameState.health = 100.0
	_reset_hit_state(player)
	player.set_invincible(1.0)
	var inv_b = GameState.bullet_pool.Fire(Vector2.DOWN, 0.0, 12, false)
	inv_b.position = player.position
	await get_tree().create_timer(0.12).timeout
	_check(GameState.health == 100.0, "用例5：无敌期内停留超窗也不结算")
	_check(inv_b.visible, "用例5：无敌期弹不被销毁（穿过语义不变）")
	await _free_enemy_bullets()
	player.set_invincible(0.0)

	# ================= 用例 6：窗口期内弹被清弹/离屏回收 → 无悬挂 Timer =================
	GameState.health = 100.0
	_reset_hit_state(player)
	var reap_b = GameState.bullet_pool.Fire(Vector2.DOWN, 0.0, 12, false)
	reap_b.position = player.position
	await get_tree().physics_frame  # 已进入宽限期（Timer 启动）
	reap_b.Despawn()  # 受击清弹/离屏回收同款路径
	await get_tree().create_timer(0.12).timeout
	_check(GameState.health == 100.0, "用例6：窗口期回收后无悬挂结算")
	await _free_enemy_bullets()

	# ================= 用例 4b：致死结算 → 死亡流程 + 既有清弹语义（无悬挂） =================
	# 注：受击清弹（clear_nearby_enemy_bullets，250px）会回收结算弹自身——既有语义
	# （计划书 §2.3 明确预期「玩家受击清弹」despawn 宽限期弹），P2-10 致死高亮残留
	# 仅覆盖清弹半径外等罕见路径；此处验证致死结算与回收链路本身。
	GameState.health = 12.0
	_reset_hit_state(player)
	player.set_since_damage(0.0)  # 关闭被动回血（对齐 hit_logic A5 语义），保证致死精确
	var fatal_b = GameState.bullet_pool.Fire(Vector2.DOWN, 0.0, 12, false)
	fatal_b.position = player.position
	await get_tree().create_timer(0.1).timeout
	_check(GameState.health == 0.0 and player.is_dead(), "用例4b：宽限结算致死归零并进入死亡流程")
	_check(not fatal_b.IsActive(), "用例4b：结算弹按既有受击清弹语义回收")
	await get_tree().create_timer(0.7).timeout
	_check(not fatal_b.IsActive() and not fatal_b.visible, "用例4b：回收后状态保持（无悬挂 Timer/重入）")

	# 复活玩家供清场收尾（复用既有公开接口）
	player.set_dead(false)
	player.show()
	player.set_physics_process(true)
	GameState.health = 100.0
	for child in main.get_children():
		if is_instance_of(child, _bullet_script):  # 随批次 A 重定型：C# 类不能经类名 is 判定
			child.queue_free()
	await get_tree().process_frame
	await get_tree().create_timer(0.6).timeout  # 演出/高亮 tween 播完，避免退出时对象泄漏

	# L15：还原用户最高分并落盘（收尾不污染用户 profile）
	GameState.high_score = orig_high_score
	GameState.save_profile()
	print("GRACE PERIOD TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
