extends Node
## 2026-08-07 断言场景：ProgressionInterop（C# 绑定壳）——GDScript 经互操作层计算
## 里程碑阈值/难度进程曲线，验证 InfiAir.Core.Progression 语义在引擎环境内可用；
## 生产链路已接入（GameState.milestone_threshold/_recompute_difficulty 转发，
## 本场景直测绑定壳 + 抽查值与 difficulty_test/balance_test 同源）。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _ready() -> void:
	# 1. C# 绑定壳可加载并实例化
	var cls: Variant = load("res://csharp/godot/ProgressionInterop.cs")
	_check(cls != null, "ProgressionInterop 脚本资源可加载")
	var interop = cls.new()
	var base: Array[int] = GameState.milestone_base.duplicate()
	var cycle_mult: float = GameState.milestone_cycle_mult

	# 2. 里程碑阈值：首循环 = 基础表（difficulty_test/balance_test 同源值）
	_check(interop.call("MilestoneThreshold", 0, base, cycle_mult, 1.0) == 3000, "首档 3000")
	_check(interop.call("MilestoneThreshold", 1, base, cycle_mult, 1.0) == 8000, "次档 8000")
	_check(interop.call("MilestoneThreshold", 7, base, cycle_mult, 1.0) == 80000, "首循环末档 80000")

	# 3. 循环增长（×1.35^cycle）
	_check(interop.call("MilestoneThreshold", 8, base, cycle_mult, 1.0) == 84050, "循环档 80000+3000×1.35")
	_check(interop.call("MilestoneThreshold", 9, base, cycle_mult, 1.0) == 90800, "循环档 +5000×1.35")
	_check(interop.call("MilestoneThreshold", 15, base, cycle_mult, 1.0) == 188000, "第二循环末 80000+80000×1.35")

	# 4. 难度阈值倍率（hard ×1.5）
	_check(interop.call("MilestoneThreshold", 0, base, cycle_mult, 1.5) == 4500, "阈值倍率 ×1.5 首档 4500")
	_check(interop.call("MilestoneThreshold", 7, base, cycle_mult, 1.5) == 120000, "阈值倍率 ×1.5 末档 120000")
	_check(interop.call("MilestoneThreshold", 8, base, cycle_mult, 1.5) == 126075, "阈值倍率 ×1.5 循环档 126075")

	# 5. 极大 index 不溢出 UB（A 审计口径：≥0 且非 int32 哨兵值）
	var mt_huge: Variant = interop.call("MilestoneThreshold", 99999, base, cycle_mult, 1.0)
	_check(int(mt_huge) >= 0 and int(mt_huge) != 2147483647, "极大 index 钳制不溢出")

	# 6. 批量推进：CountThresholdsUpTo 与逐档求值一致（含 10000 档挂死守卫）
	_check(interop.call("CountThresholdsUpTo", 0, base, cycle_mult, 1.0) == 0, "score=0 无里程碑")
	_check(interop.call("CountThresholdsUpTo", 2999, base, cycle_mult, 1.0) == 0, "2999 未达首档")
	_check(interop.call("CountThresholdsUpTo", 3000, base, cycle_mult, 1.0) == 1, "3000 触发首档")
	_check(interop.call("CountThresholdsUpTo", 84049, base, cycle_mult, 1.0) == 8, "84049 越过 80000 共 8 档")
	_check(interop.call("CountThresholdsUpTo", 84050, base, cycle_mult, 1.0) == 9, "84050 越过第 9 档 84050 共 9 档")
	_check(interop.call("CountThresholdsUpTo", 1_000_000_000, [100], 1.0, 1.0) == 10000, "曲线收敛场景封顶 10000（挂死守卫）")

	# 7. 难度进程曲线（1 + per_boss×kills + 时间轴累进）
	_check(is_equal_approx(float(interop.call("DifficultyMultiplier", 0.0, 30.0, 1.5, 0.6, 0)), 1.0), "难度曲线：开局 ×1.0")
	_check(is_equal_approx(float(interop.call("DifficultyMultiplier", 61.0, 30.0, 1.5, 0.6, 0)), 1.15), "难度曲线：2 档时间累进 +0.15")
	_check(is_equal_approx(float(interop.call("DifficultyMultiplier", 300.0, 30.0, 1.5, 0.6, 2)), 2.95), "难度曲线：2 Boss + 10 档")

	# 8. 生产链路转发一致（GameState 已切 C# 壳）
	_check(GameState.milestone_threshold(0) == 3000, "生产链路 milestone_threshold(0) = 3000")
	_check(GameState.milestone_threshold(8) == 84050, "生产链路 milestone_threshold(8) = 84050")
	_check(GameState.milestone_threshold(15) == 188000, "生产链路 milestone_threshold(15) = 188000")
	GameState.set_difficulty(&"hard")
	_check(GameState.milestone_threshold(8) == 126075, "生产链路 hard 档阈值倍率生效")
	GameState.set_difficulty(&"medium")
	_check(GameState.milestone_threshold(8) == 84050, "生产链路切回 medium 恢复 ×1")

	print("PROGRESSION INTEROP TEST DONE, failures = ", _failures)
	load("res://csharp/godot/TestExit.cs").Quit(_failures)
