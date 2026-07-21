extends Node
## 数值配置中心测试：加载、代表值抽查、损坏回退、改值生效（测完恢复 data/balance.json）。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _ready() -> void:
	var original: String = FileAccess.get_file_as_string(GameState.BALANCE_PATH)
	GameState.difficulty = &"medium"  # 里程碑倍率确定性

	# 1. 配置已加载
	_check(not GameState._balance.is_empty(), "balance.json 已加载进内存")

	# 2. 代表值抽查（须与当前 data/balance.json 一致）
	_check(GameState.cfg("player.fuel.drain", 0.0) == 35.0, "燃料消耗 35/s")
	_check(GameState.milestone_threshold(0) == 3000, "里程碑首档 3000")
	_check(GameState.milestone_cycle_mult == 1.35, "里程碑循环倍率 1.35")
	_check(GameState.cfg("mothership.depart_cooldown", 0.0) == 60.0, "母舰冷却 60s")
	_check(GameState.cfg("mothership.mag_cells", 0) == 10, "弹匣 10 格")
	_check(GameState.cfg("mothership.missile.damage", 0) == 80, "母舰导弹直击 80")
	_check(float(GameState.cfg("boss.hp_mults", [])[0]) == 1.3, "Boss-1 HP 倍率 1.3")
	_check(GameState.cfg("boss.collision_damage", 0) == 30, "Boss 撞击 30")
	_check(GameState.cfg("player.max_speed", 0.0) == 420.0, "玩家满速 420")
	_check(GameState.cfg("player.max_health", 0.0) == 100.0, "玩家 100 HP")
	_check(GameState.cfg("player.bullet_damage", 0) == 10, "玩家弹伤 10")
	_check(GameState.cfg("player.dash.cooldown", 0.0) == 4.0, "冲刺冷却 4s")
	_check(GameState.cfg("enemies.collision_damage", 0) == 20, "敌机撞击 20")
	_check(GameState.cfg("enemies.bullet_damage.laser", 0) == 20, "laser 敌弹 20")
	_check(GameState.cfg("spawner.elite_chance_cap", 0.0) == 0.25, "精英率上限 0.25")
	_check(GameState.cfg("buffs.slow_field.factor", 0.0) == 0.8, "慢速力场移速 ×0.8")
	_check(GameState.cfg("buffs.explosive.damage_per_level", 0) == 30, "爆炸弹固定溅射 30")
	_check(is_equal_approx(GameState.cfg("mothership.gatling.score_scale", 0.0), 1.0 / 3.0), "母舰击杀 1/3 分")

	# 3. 损坏 JSON → 回退脚本默认值
	var f := FileAccess.open(GameState.BALANCE_PATH, FileAccess.WRITE)
	f.store_string("{broken json!!!")
	f.close()
	GameState._load_balance()
	GameState._apply_balance()
	_check(GameState._balance.is_empty(), "损坏 JSON 被丢弃")
	_check(GameState.cfg("player.fuel.drain", 35.0) == 35.0, "损坏后回退默认值")
	_check(GameState.milestone_threshold(0) == 3000, "损坏后里程碑回退默认")

	# 4. 改值生效
	f = FileAccess.open(GameState.BALANCE_PATH, FileAccess.WRITE)
	f.store_string(JSON.stringify({"player": {"fuel": {"drain": 50.0}}}))
	f.close()
	GameState._load_balance()
	GameState._apply_balance()
	_check(GameState.cfg("player.fuel.drain", 0.0) == 50.0, "改值 50 生效")
	_check(GameState.cfg("player.max_speed", 420.0) == 420.0, "缺省键回退默认")

	# 5. 恢复原文件并恢复生效配置
	f = FileAccess.open(GameState.BALANCE_PATH, FileAccess.WRITE)
	f.store_string(original)
	f.close()
	GameState._load_balance()
	GameState._apply_balance()
	_check(GameState.cfg("player.fuel.drain", 0.0) == 35.0, "原文件已恢复")

	print("BALANCE TEST DONE, failures = ", _failures)
	get_tree().quit(_failures)
