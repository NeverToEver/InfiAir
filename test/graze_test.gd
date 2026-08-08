extends Node
## 擦弹得分测试（2026-08-03 公平感机制二，docs/archive/2026-08-03-combat-fairness-plan.md §3）：
## 单弹进入擦弹环计 1 次分、同一弹反复进出只计 1 次、受击区（受击盒内）不计擦弹、
## 难度倍率入账（中 ×2）、弹池复用后擦弹标志复位、宽限帧擦过既计分又无伤、
## 弹反后弹经过玩家不计擦弹（层排除）。

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
func _reset_hit_state(player) -> void:  # M3c：Player 迁 C#，参数类型注解去除
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
	var player = get_node("Main/Player")  # M3c：Player 迁 C#，类型注解去除
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
	GameState.score = 0

	# ================= 用例 1 + 4：单弹进入擦弹环 → 计 1 次分（中难度 ×2 = 20） =================
	_check(GameState.score_multiplier() == 2, "用例4：当前难度 medium 分数倍率 ×2")
	GameState.score = 0
	_reset_hit_state(player)
	var g1 = GameState.bullet_pool.Fire(Vector2.DOWN, 100.0, 10, false)
	g1.position = Vector2(960.0, 760.0)  # 距玩家 40px（环 r=20），0.2s 后入环
	await get_tree().create_timer(0.45).timeout
	_check(GameState.score == 20, "用例1：单弹进入擦弹环计分（10 × 难度倍率 2 = 20）")
	await _free_enemy_bullets()

	# ================= 用例 2：同一弹反复进出环 → 只计 1 次（_graze_done 生效） =================
	GameState.score = 0
	_reset_hit_state(player)
	player.position = Vector2(960.0, 800.0)
	var g2 = GameState.bullet_pool.Fire(Vector2.DOWN, 0.0, 10, false)
	g2.position = Vector2(960.0, 785.0)  # 环内、受击盒外（距玩家 15px）
	await get_tree().create_timer(0.1).timeout
	_check(GameState.score == 20, "用例2：单弹进入环计 1 次分")
	player.position = Vector2(1150.0, 800.0)  # 弹在环外（距 190px）
	await get_tree().create_timer(0.1).timeout
	player.position = Vector2(960.0, 800.0)  # 弹再次进入环
	await get_tree().create_timer(0.1).timeout
	_check(GameState.score == 20, "用例2：同一弹反复进出环只计 1 次")
	await _free_enemy_bullets()

	# ================= 用例 3：弹进入受击区（< 受击盒）→ 不计擦弹，走受击流程 =================
	GameState.score = 0
	GameState.health = 100.0
	_reset_hit_state(player)
	player.position = Vector2(960.0, 800.0)
	var g3 = GameState.bullet_pool.Fire(Vector2.DOWN, 0.0, 12, false)
	g3.position = player.position  # 直接生成在受击盒内（area_entered 时刻已深入）
	await get_tree().create_timer(0.1).timeout
	_check(GameState.score == 0, "用例3：弹进入受击区不计擦弹")
	_check(GameState.health == 88.0, "用例3：受击流程正常（两 Area 互不干扰）")
	await _free_enemy_bullets()

	# ================= 用例 5：弹池复用后擦弹标志复位 → 可再次擦弹 =================
	GameState.score = 0
	_reset_hit_state(player)
	var g5 = GameState.bullet_pool.Fire(Vector2.DOWN, 0.0, 10, false)
	g5.position = Vector2(960.0, 785.0)
	await get_tree().create_timer(0.1).timeout
	_check(GameState.score == 20, "用例5：池弹擦弹计分")
	g5.Despawn()  # 回收进池
	await get_tree().process_frame
	var g5b = GameState.bullet_pool.Fire(Vector2.DOWN, 0.0, 10, false)
	g5b.position = Vector2(960.0, 785.0)
	await get_tree().create_timer(0.1).timeout
	_check(g5b == g5, "用例5：池复用取回同一实例")
	_check(GameState.score == 40, "用例5：池复用后擦弹标志复位可再计分")
	await _free_enemy_bullets()

	# ================= 用例 6：宽限帧擦过弹 → 既擦弹（计分）又无伤（宽限） =================
	GameState.score = 0
	GameState.health = 100.0
	_reset_hit_state(player)
	var g6 = GameState.bullet_pool.Fire(Vector2.RIGHT, 600.0, 12, false)
	g6.position = player.position + Vector2(-30.0, 3.0)  # 水平弹道与环/受击盒边缘带相交
	await get_tree().create_timer(0.2).timeout
	_check(GameState.score == 20, "用例6：宽限帧擦过弹既计擦弹分")
	_check(GameState.health == 100.0, "用例6：宽限帧擦过弹无伤")
	await _free_enemy_bullets()

	# ================= 用例 7：弹反后弹经过玩家 → 不计擦弹（转玩家弹，层排除） =================
	GameState.score = 0
	_reset_hit_state(player)
	var g7 = GameState.bullet_pool.Fire(Vector2.DOWN, 200.0, 10, false)
	g7.position = Vector2(960.0, 900.0)  # 玩家下方 100px
	g7.Reflect()  # 弹反路径：转玩家弹 + 反射朝上，将穿过玩家
	await get_tree().create_timer(0.6).timeout
	_check(GameState.score == 0, "用例7：弹反后弹经过玩家不计擦弹")
	await _free_enemy_bullets()

	for child in main.get_children():
		if is_instance_of(child, _bullet_script):  # 随批次 A 重定型：C# 类不能经类名 is 判定
			child.queue_free()
	await get_tree().process_frame
	await get_tree().create_timer(0.6).timeout  # 演出/粒子播完，避免退出时对象泄漏

	# L15：还原用户最高分并落盘（收尾不污染用户 profile）
	GameState.high_score = orig_high_score
	GameState.save_profile()
	print("GRAZE TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
