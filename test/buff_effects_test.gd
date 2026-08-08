extends Node
## A4 架构断言测试：声明式 buff 效果表（BUFF_EFFECTS）完整性与求值语义回归——
## 表键集覆盖全部 player 侧 buff、pow/cap 的 cfg 键存在于 balance.json、
## 三类效果（pow 乘算 / cap 堆叠 / bool 启用）求值与重构前公式逐点一致。

# M3b：Enemy 迁 C#，is 判定经脚本资源引用（GDScript 不能 is C# 类）
var _enemy_script := load("res://csharp/godot/Enemy.cs")

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## 按点分路径导航解析后的 JSON 字典（"a.b.c" → data["a"]["b"]["c"]），任一环缺失返回 null
func _json_at(data: Variant, path: String) -> Variant:
	var node: Variant = data
	for key: String in path.split("."):
		if node is Dictionary and node.has(key):
			node = node[key]
		else:
			return null
	return node


func _ready() -> void:
	# 清理持久化状态，保证测试确定性
	GameState.delete_save()
	# L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
	var orig_high_score: int = GameState.high_score
	GameState.high_score = 0
	GameState.save_profile()
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	GameState.login_guest()  # T4：游客会话直接开局（StartPanel 已退役）
	add_child(main_scene.instantiate())
	var main := get_node("Main")
	# 开场面板自显即暂停（冻结背景），先关闭解除
	var player: Player = get_node("Main/Player")
	var spawner: Node = get_node("Main/Spawner")
	player.set_auto_fire(false)
	player.set_invincible(999.0)
	spawner.set_process(false)
	# 关闭辅助瞄准追踪（cone 目标会设 homing_time=1.2，穿透弹绕 e1 螺旋永不达 e2），
	# 保证穿透/溅射测试弹道直线确定
	GameState.aim_frame_layer = null
	await get_tree().process_frame
	await get_tree().process_frame
	for child in main.get_children():
		if is_instance_of(child, _enemy_script) or child.has_method("TryGraze"):  # M3b：Enemy 迁 C#，is 改脚本判定
			child.queue_free()
	await get_tree().process_frame

	# 1. 效果表完整性：键集覆盖全部 player 侧 buff，kind 合法，cfg 键存在于 balance.json
	var effects: Dictionary = player.BUFF_EFFECTS
	_check(effects.size() == 9, "BUFF_EFFECTS 表登记 9 项 player 侧 buff 效果")
	for id: StringName in [
		&"rapid_fire",
		&"power_shot",
		&"efficient_boost",
		&"boost_recovery",
		&"phase_dash",
		&"spread_shot",
		&"piercing",
		&"explosive",
		&"bullet_speed"
	]:
		_check(effects.has(id), "效果表包含 %s" % id)
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(GameState.BALANCE_PATH))
	var pow_cap := 0
	for id: StringName in effects:
		var effect: Dictionary = effects[id]
		_check(effect["kind"] in ["pow", "cap", "bool"], "效果表 %s kind 合法" % id)
		if effect["kind"] == "bool":
			_check(not effect.has("cfg"), "bool 效果 %s 无 cfg 键" % id)
			continue
		pow_cap += 1
		_check(_json_at(parsed, effect["cfg"]) != null, "效果表 %s 的 cfg 键存在于 balance.json" % id)
	_check(pow_cap == 8, "pow/cap 效果 8 项（bool 1 项）")
	# bullet_speed：弹速乘算因子（2026-08-04 新 buff）
	GameState.add_buff(&"bullet_speed")
	_check(is_equal_approx(player.bullet_speed_value(), 1800.0 * 1.2), "bullet_speed 1 层弹速 ×1.2")
	GameState.add_buff(&"bullet_speed")
	_check(is_equal_approx(player.bullet_speed_value(), 1800.0 * pow(1.2, 2)), "bullet_speed 2 层弹速 ×1.2²")
	# crit_shot：概率暴击参数缓存（层数 × 基础概率；选取路径会广播 buffs_changed，此处模拟）
	_check(is_equal_approx(player.crit_chance, 0.0), "无 crit_shot 暴击概率 0")
	GameState.add_buff(&"crit_shot")
	GameState.add_buff(&"crit_shot")
	GameState.buffs_changed.emit()
	_check(is_equal_approx(player.crit_chance, 0.12 * 2.0), "crit_shot 2 层暴击概率 24%")
	_check(is_equal_approx(player.crit_multiplier, 2.0), "crit_shot 暴击倍率 ×2")

	# 2. 乘算因子（pow）求值：与重构前公式逐点一致
	_check(is_equal_approx(player.fire_interval(), 0.15), "无 buff 开火间隔 0.15s")
	GameState.add_buff(&"rapid_fire")
	_check(is_equal_approx(player.fire_interval(), 0.15 * 0.75), "rapid_fire 1 层开火间隔 ×0.75")
	GameState.add_buff(&"rapid_fire")
	GameState.add_buff(&"rapid_fire")
	_check(is_equal_approx(player.fire_interval(), 0.15 * pow(0.75, 3)), "rapid_fire 3 层开火间隔 ×0.75³")
	_check(player.bullet_damage() == 10, "无 power_shot 弹伤 10")
	GameState.add_buff(&"power_shot")
	_check(player.bullet_damage() == 12, "power_shot 1 层弹伤 int(10×1.25)=12")
	GameState.add_buff(&"power_shot")
	_check(player.bullet_damage() == 15, "power_shot 2 层弹伤 int(10×1.25²)=15")
	_check(is_equal_approx(player.fuel_drain_rate(), 35.0), "无 buff 燃料消耗 35/s")
	GameState.add_buff(&"efficient_boost")
	_check(is_equal_approx(player.fuel_drain_rate(), 35.0 * 0.75), "efficient_boost 1 层消耗 ×0.75")
	_check(is_equal_approx(player.fuel_regen_rate(), 20.0), "无 buff 燃料恢复 20/s")
	GameState.add_buff(&"boost_recovery")
	_check(is_equal_approx(player.fuel_regen_rate(), 30.0), "boost_recovery 1 层恢复 ×1.5")
	GameState.add_buff(&"boost_recovery")
	_check(is_equal_approx(player.fuel_regen_rate(), 45.0), "boost_recovery 2 层恢复 ×2.25")

	# 3. phase_dash：首次解锁不缩冷却，之后每层 ×0.8
	_check(not player.dash_unlocked(), "无 phase_dash 冲刺未解锁")
	GameState.add_buff(&"phase_dash")
	_check(player.dash_unlocked(), "phase_dash 1 层解锁冲刺")
	_check(is_equal_approx(player.dash_cooldown_max(), 4.0), "phase_dash 1 层冷却保持 4s")
	GameState.add_buff(&"phase_dash")
	_check(is_equal_approx(player.dash_cooldown_max(), 3.2), "phase_dash 2 层冷却 ×0.8")

	# 4. 堆叠上限（cap）：piercing 1 层子弹穿透直线两敌（此时无 spread，单发直线弹道）
	GameState.add_buff(&"piercing")
	var enemy_scene: PackedScene = load("res://scenes/enemy.tscn")
	var e1 = enemy_scene.instantiate()  # M3b：Enemy 迁 C#，enemy.tscn 实例必为 Enemy，省 as
	e1.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	e1.hp = 9999
	e1.speed = 0.0
	e1.can_shoot = false
	e1.position = player.global_position + Vector2(0.0, -300.0)
	main.add_child(e1)
	var e2 = enemy_scene.instantiate()  # M3b：Enemy 迁 C#，enemy.tscn 实例必为 Enemy，省 as
	e2.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	e2.hp = 9999
	e2.speed = 0.0
	e2.can_shoot = false
	e2.position = player.global_position + Vector2(0.0, -600.0)
	main.add_child(e2)
	player.aim_point_override = player.global_position + Vector2(0.0, -400.0)
	# 降弹速至 600px/s（每 tick 10px << 判定窗口 ~32px）：消除 1800px/s 下每 tick 30px
	# 位移的隧穿（Area2D overlap 快照可能整跳跳过目标判定窗口，实测 ~40% 概率漏判）
	player.BULLET_SPEED = 600.0
	player.set_auto_fire(true)
	await get_tree().create_timer(1.6).timeout
	player.set_auto_fire(false)
	_check(is_instance_valid(e1) and e1.hp < 9999, "穿透弹命中前敌")
	_check(is_instance_valid(e2) and e2.hp < 9999, "piercing 1 层弹穿透命中后敌")

	# 4b. crit_shot（2026-08-04）：真实命中路径——固定 seed 后多发命中出现暴击（×2）与非暴击混合
	for child in main.get_children():
		if is_instance_of(child, _enemy_script) or child.has_method("TryGraze"):  # M3b：Enemy 迁 C#，is 改脚本判定
			child.queue_free()
	await get_tree().process_frame
	GameState.buffs.clear()  # 清掉 §1-§4 累积（rapid_fire/power_shot/crit），保证 16 发 × 10/20 的假设成立
	GameState.add_buff(&"crit_shot")
	GameState.add_buff(&"crit_shot")
	GameState.add_buff(&"crit_shot")
	GameState.buffs_changed.emit()
	_check(is_equal_approx(player.crit_chance, 0.36), "crit_shot 3 层暴击概率 36%")
	var ce = enemy_scene.instantiate()  # M3b：Enemy 迁 C#，enemy.tscn 实例必为 Enemy，省 as
	ce.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	ce.hp = 99999
	ce.speed = 0.0
	ce.can_shoot = false
	ce.position = player.global_position + Vector2(0.0, -200.0)
	main.add_child(ce)
	player.aim_point_override = player.global_position + Vector2(0.0, -200.0)
	player.BULLET_SPEED = 600.0
	player.set_auto_fire(true)
	seed(20260804)
	await get_tree().create_timer(2.4).timeout  # ~16 发（0.15s 间隔）
	player.set_auto_fire(false)
	_check(is_instance_valid(ce) and ce.hp < 99999, "暴击测试：敌机受到伤害")
	if is_instance_valid(ce):
		var dealt := int(99999.0 - ce.hp)
		# 16 发 × (10 或 20)：纯普通 160、纯暴击 320；固定 seed 下应混合出现
		_check(dealt > 160 and dealt < 320, "crit_shot：命中序列出现暴击与非暴击混合（%d 点）" % dealt)

	# 5. 布尔（bool）：explosive 击毁目标溅射侧向近邻（40px，不在弹道上）
	for child in main.get_children():
		if is_instance_of(child, _enemy_script) or child.has_method("TryGraze"):  # M3b：Enemy 迁 C#，is 改脚本判定
			child.queue_free()
	await get_tree().process_frame
	GameState.add_buff(&"explosive")
	var a = enemy_scene.instantiate()  # M3b：Enemy 迁 C#，enemy.tscn 实例必为 Enemy，省 as
	a.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	a.hp = 50
	a.speed = 0.0
	a.can_shoot = false
	a.position = player.global_position + Vector2(0.0, -300.0)
	main.add_child(a)
	var b = enemy_scene.instantiate()  # M3b：Enemy 迁 C#，enemy.tscn 实例必为 Enemy，省 as
	b.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	b.hp = 9999
	b.speed = 0.0
	b.can_shoot = false
	b.position = player.global_position + Vector2(-40.0, -300.0)
	main.add_child(b)
	player.set_auto_fire(true)
	# 弹速 600：A 距玩家 300px 约 0.5s 到达，4 发击毁（0.063s 间隔）约 0.7s，等 1.6s 保险
	await get_tree().create_timer(1.6).timeout
	player.set_auto_fire(false)
	_check(not is_instance_valid(a) or a.hp == 0.0, "explosive 击毁目标 A")
	_check(is_instance_valid(b) and b.hp < 9999, "爆炸溅射命中 40px 外近邻 B")

	# 6. 堆叠上限（cap）：spread_shot 3 层一轮齐射 4 弹
	for child in main.get_children():
		if is_instance_of(child, _enemy_script) or child.has_method("TryGraze"):  # M3b：Enemy 迁 C#，is 改脚本判定
			child.queue_free()
	await get_tree().process_frame
	GameState.add_buff(&"spread_shot")
	GameState.add_buff(&"spread_shot")
	GameState.add_buff(&"spread_shot")
	player.set_auto_fire(true)
	# 轮询至第一轮出现即停（rapid_fire 3 层 0.063s 间隔，第一轮后 3.8 tick 内不再发）：
	# 消除「等固定 tick 数」在开火冷却残留时的首轮未发竞态
	var bullets := 0
	for j in 60:
		bullets = 0
		for child in main.get_children():
			if child.has_method("TryGraze"):
				bullets += 1
		if bullets > 0:
			break
		await get_tree().physics_frame
	player.set_auto_fire(false)
	_check(bullets == 4, "spread_shot 3 层一轮齐射 4 弹")

	# 清理测试实体，避免退出时资源残留；等音效播完再退
	for child in main.get_children():
		if is_instance_of(child, _enemy_script) or child.has_method("TryGraze"):  # M3b：Enemy 迁 C#，is 改脚本判定
			child.queue_free()
	await get_tree().process_frame
	await get_tree().create_timer(0.3).timeout
	# L15：还原用户最高分并落盘（收尾不污染用户 profile）
	GameState.high_score = orig_high_score
	GameState.save_profile()
	print("BUFF_EFFECTS TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
