extends Node
## 3.3 战斗补全测试（Buff/母舰/放弃）：laser_beam 光束、mothership_recall 冷却减半、
## boost_recovery 恢复提升、explosive 抽卡 gating、长按 K 放弃出击。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _ready() -> void:
	# 清理持久化状态，保证测试确定性
	GameState.delete_save()
	# L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
	var orig_high_score: int = GameState.high_score
	GameState.high_score = 0
	GameState.save_profile()
	# give_up 输入映射由 project.godot 提供；缺失时运行时补齐（不影响断言语义）
	# R07：补齐动作记录标记，收尾还原（L 系列测试登记遗留——原实现 add 后不清理）
	var added_give_up := false
	if not InputMap.has_action(&"give_up"):
		InputMap.add_action(&"give_up")
		added_give_up = true
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	GameState.login_guest()  # T4：游客会话直接开局（StartPanel 已退役）
	add_child(main_scene.instantiate())
	var main := get_node("Main")
	# 开场面板自显即暂停（冻结背景），先关闭解除
	var player = get_node("Main/Player")  # M3c：Player 迁 C#，类型注解去除
	var hud: CanvasLayer = get_node("Main/HUD")
	var buff_ui: CanvasLayer = get_node("Main/BuffUI")
	var spawner: Node = get_node("Main/Spawner")
	# 全自动开火会干扰激光断言，测试全程禁用
	player.set_auto_fire(false)
	player.set_invincible(999.0)
	spawner.set_process(false)
	await get_tree().process_frame
	await get_tree().process_frame
	for child in main.get_children():
		if is_instance_of(child, load("res://csharp/godot/Enemy.cs")) or child.has_method("TryGraze"):  # M3b：Enemy 迁 C#，is 改内联脚本判定
			child.queue_free()
	await get_tree().process_frame

	# 1. Buff 候选池：新 buff 入池 + explosive gating（boss_kills>=3 才解锁）
	GameState.boss_kills = 0
	var ids: Array = buff_ui.available_buffs().map(func(b: Dictionary) -> StringName: return b["id"])
	_check(&"laser_beam" in ids, "laser_beam 入抽卡候选池")
	_check(&"mothership_recall" in ids, "mothership_recall 入抽卡候选池")
	_check(&"boost_recovery" in ids, "boost_recovery 入抽卡候选池")
	_check(&"explosive" not in ids, "boss_kills<3 时 explosive 不入候选池")
	GameState.boss_kills = 3
	ids = buff_ui.available_buffs().map(func(b: Dictionary) -> StringName: return b["id"])
	_check(&"explosive" in ids, "boss_kills>=3 时 explosive 入候选池")
	GameState.boss_kills = 0

	# 2. laser_beam：无 buff 不触发；获得后触发光束并穿透伤害直线上 2 个敌人
	var laser = player.get_node("LaserWeapon")  # M3c：LaserWeapon 迁 C#，类型注解去除
	await get_tree().create_timer(0.4).timeout
	_check(not laser.active(), "无 laser_beam buff 时激光不触发")
	GameState.add_buff(&"laser_beam")
	# 沿瞄准线布置两个静止高血敌人（不被击毁，避免得分里程碑干扰）
	var aim: Vector2 = (player.get_global_mouse_position() - player.global_position).normalized()
	if aim.length() < 0.5:
		aim = Vector2.UP
	var enemy_scene: PackedScene = load("res://scenes/enemy.tscn")
	var e1 = enemy_scene.instantiate()  # M3b：Enemy 迁 C#，enemy.tscn 实例必为 Enemy，省 as
	e1.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	e1.hp = 9999
	e1.speed = 0.0
	e1.can_shoot = false
	e1.position = player.global_position + aim * 300.0
	main.add_child(e1)
	var e2 = enemy_scene.instantiate()  # M3b：Enemy 迁 C#，enemy.tscn 实例必为 Enemy，省 as
	e2.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	e2.hp = 9999
	e2.speed = 0.0
	e2.can_shoot = false
	e2.position = player.global_position + aim * 600.0
	main.add_child(e2)
	var wait := 0.0
	while not laser.active() and wait < 2.0:
		await get_tree().create_timer(0.1).timeout
		wait += 0.1
	_check(laser.active(), "获得 laser_beam 后触发光束")
	_check(laser.beam().visible, "光束视觉可见")
	await get_tree().create_timer(0.5).timeout
	_check(is_instance_valid(e1) and e1.hp < 9999, "光束对直线上敌人 1 造成伤害")
	_check(is_instance_valid(e2) and e2.hp < 9999, "光束穿透对直线上敌人 2 造成伤害")
	# 3s 持续结束后进入约 8s 冷却
	await get_tree().create_timer(2.8).timeout
	_check(not laser.active(), "3 秒后光束结束")
	_check(laser.cooldown() > 6.0, "光束结束后进入约 8s 冷却")
	# 冷却结束可再次触发（测试直接缩短冷却，不真等 8s）
	laser.set_cooldown(0.05)
	await get_tree().create_timer(0.3).timeout
	_check(laser.active(), "冷却结束后激光可再次触发")
	laser.set_active_time(0.01)
	await get_tree().create_timer(0.2).timeout
	if is_instance_valid(e1):
		e1.queue_free()
	if is_instance_valid(e2):
		e2.queue_free()
	await get_tree().process_frame

	# 3. mothership_recall：每层母舰冷却 ×0.5（基础 60s→30s→15s）
	main.on_mothership_departed(60.0)
	_check(is_equal_approx(main.dock_cooldown(), 60.0), "无 recall 时母舰冷却 60s")
	GameState.add_buff(&"mothership_recall")
	main.on_mothership_departed(60.0)
	_check(is_equal_approx(main.dock_cooldown(), 30.0), "recall 1 层母舰冷却 30s")
	GameState.add_buff(&"mothership_recall")
	main.on_mothership_departed(60.0)
	_check(is_equal_approx(main.dock_cooldown(), 15.0), "recall 2 层母舰冷却 15s")
	_check(main.dock_status_text().contains("母舰冷却"), "母舰状态文本联动冷却值")
	main.set_dock_cooldown(0.0)

	# 4. boost_recovery：恢复速率每层 ×1.5（乘算），且实际回油生效
	_check(is_equal_approx(player.fuel_regen_rate(), 20.0), "无 buff 燃料恢复 20/s")
	GameState.add_buff(&"boost_recovery")
	_check(is_equal_approx(player.fuel_regen_rate(), 30.0), "boost_recovery 1 层恢复 ×1.5")
	GameState.add_buff(&"boost_recovery")
	_check(is_equal_approx(player.fuel_regen_rate(), 45.0), "boost_recovery 2 层乘算 ×2.25")
	player.set_fuel(50.0)
	await get_tree().create_timer(1.0).timeout
	_check(player.fuel_amount() > 80.0, "提升后的恢复速率实际生效（1s 回 45）")

	# 5. 长按 K 放弃出击：蓄力可取消，蓄满 3s 自毁进死亡结算
	# 制造敌弹供死亡回放录制（B 梯队：回放重演死因片段；蓄力 3.3s 期间 main._process
	# 持续采样填充环形缓冲）
	# M3a：Bullet 为 C# 类——instantiate 后不能 as Bullet，直接 untyped（方法/属性 PascalCase）
	var eb = (load("res://scenes/bullet.tscn") as PackedScene).instantiate()
	eb.Setup(Vector2.DOWN, 200.0, 1, false)
	eb.position = Vector2(960.0, 300.0)
	main.add_child(eb)
	await get_tree().process_frame
	await get_tree().process_frame
	Input.action_press("give_up")
	await get_tree().create_timer(1.0).timeout
	_check(main.give_up_charge() > 0.0, "K 蓄力进行中")
	_check(hud.give_up_label().visible, "HUD 显示放弃蓄力进度")
	Input.action_release("give_up")
	await get_tree().create_timer(0.2).timeout
	_check(main.give_up_charge() == 0.0 and not hud.give_up_label().visible, "松开 K 取消蓄力")
	_check(GameState.health == GameState.max_health(), "取消蓄力未自毁")
	Input.action_press("give_up")
	await get_tree().create_timer(3.3).timeout
	Input.action_release("give_up")
	await get_tree().process_frame
	_check(GameState.health == 0.0, "长按 K 3s 自毁")
	_check(player.is_dead(), "自毁后玩家死亡")
	_check(get_node("Main/GameOverUI").visible, "自毁进入死亡结算面板")
	_check(get_tree().paused, "结算时游戏暂停")
	# B 梯队（fair plan §8）：死亡回放演出已挂树（幽灵弹幕重放死因；process_mode=ALWAYS
	# 暂停树中照常播放，播完自毁）
	var replay_node: Node = null
	for child in main.get_children():
		if child is DeathReplay.DeathReplayPlayer:
			replay_node = child
			break
	_check(replay_node != null, "死亡回放演出已启动")
	if replay_node != null:
		await get_tree().create_timer(0.2, true, false, true).timeout  # process_always：暂停树中计时
		_check(is_instance_valid(replay_node), "回放演出播放中（0.2s 后未结束）")

	# L15：还原用户最高分并落盘（收尾不污染用户 profile）
	GameState.high_score = orig_high_score
	GameState.save_profile()
	# R07：还原运行时补齐的输入动作（与补齐对称）
	if added_give_up:
		InputMap.erase_action(&"give_up")
	print("BUFF33 TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
