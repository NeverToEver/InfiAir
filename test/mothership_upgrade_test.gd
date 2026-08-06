extends Node
## 母舰火力升级测试（2026-08-04 母舰扩展 T1）：里程碑阈值 → 档位判定与倍率、
## balance 配置读取。发射数值路径（int(damage * mult)）随 mothership_summon_test 回归覆盖。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _ready() -> void:
	GameState.logout_user()
	GameState.delete_save()
	GameState.reset_run()
	GameState.login_guest()
	GameState.set_milestone_count(0)
	var main: Node2D = load("res://scenes/main.tscn").instantiate() as Node2D
	add_child(main)
	await get_tree().process_frame
	await get_tree().process_frame
	# 冻结刷怪/事件/自动开火，只验证母舰档位
	var spawner: Node = main.get_node("Spawner")
	spawner.set_process(false)
	main.event().set_process(false)
	main.formation().set_process(false)
	main.player().set_auto_fire(false)
	get_tree().paused = false

	var ms := (load("res://scenes/mothership.tscn") as PackedScene).instantiate() as Mothership
	main.add_child(ms)
	await get_tree().process_frame

	# 1. 未升级档（默认 _ready 已读配置）
	_check(ms.tier() == 0, "里程碑 0：未升级")
	_check(is_equal_approx(ms.damage_mult(), 1.0) and is_equal_approx(ms.interval_mult(), 1.0), "未升级倍率 1.0")

	# 2. 升级档（里程碑 ≥ 阈值 5）
	GameState.set_milestone_count(5)
	_check(ms.tier() == 1, "里程碑 5：升级")
	_check(is_equal_approx(ms.damage_mult(), 1.5) and is_equal_approx(ms.interval_mult(), 0.8), "升级档：伤害 ×1.5 / 射速 +25%")

	# 3. 配置键来自 balance.json（脚本默认值双写兜底）
	_check(int(GameState.cfg("mothership.upgrade.threshold", 0)) == 5, "配置键 mothership.upgrade.threshold")
	_check(float(GameState.cfg("mothership.upgrade.damage_mult", 0.0)) == 1.5, "配置键 mothership.upgrade.damage_mult")
	_check(float(GameState.cfg("mothership.upgrade.interval_mult", 0.0)) == 0.8, "配置键 mothership.upgrade.interval_mult")

	# 4. 发射数值路径：档位伤害按倍率缩放（加特林/导弹共用 damage_mult）
	var base_dmg := ms.GATLING_DAMAGE
	GameState.set_milestone_count(0)
	_check(int(base_dmg * ms.damage_mult()) == base_dmg, "未升级：发射伤害不变")
	GameState.set_milestone_count(5)
	_check(int(base_dmg * ms.damage_mult()) == int(base_dmg * 1.5), "升级：发射伤害 ×1.5")
	GameState.set_milestone_count(0)

	print("MOTHERSHIP UPGRADE TEST DONE, failures = ", _failures)
	ms.queue_free()
	await get_tree().process_frame
	GameState.logout_user()
	GameState.delete_save()
	GameState.reset_run()
	get_tree().quit(_failures)
