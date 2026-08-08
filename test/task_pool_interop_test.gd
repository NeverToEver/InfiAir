extends Node
## 2026-08-07 断言场景：TaskPoolInterop（C# 绑定壳）——GDScript 经互操作层无放回
## 抽取任务定义，验证 InfiAir.Core.Missions.TaskPool 语义在引擎环境内可用；
## 生产链路已接入（scripts/task_pool.gd 转发，性质断言同 base_task_refresh_test 第 8–9 节）。

var _failures: int = 0
const TP := preload("res://csharp/godot/TaskPool.cs")


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _all_distinct(defs: Array) -> bool:
	var seen: Dictionary = {}
	for def in defs:
		if seen.has(def["id"]):
			return false
		seen[def["id"]] = true
	return true


func _ready() -> void:
	# 1. C# 绑定壳可加载并实例化
	var cls: Variant = load("res://csharp/godot/TaskPoolInterop.cs")
	_check(cls != null, "TaskPoolInterop 脚本资源可加载")
	var interop = cls.new()
	interop.call("SetDefs", GameState.MISSION_POOL)

	# 2. 池满抽取：9 项全部返回且互不重复
	var batch1: Array = interop.call("Draw", 9, [])
	_check(batch1.size() == 9, "池满抽取 9 项全部返回")
	_check(_all_distinct(batch1), "单批无放回（9 项互不重复）")

	# 3. 耗尽后自动重洗续抽
	var batch2: Array = interop.call("Draw", 3, [])
	_check(batch2.size() == 3, "耗尽后自动重洗续抽")

	# 4. 排除项跳过 / 全池排除安全空
	var tiny = cls.new()
	tiny.call("SetDefs", [{"id": &"a", "goal": 1, "kind": &"kill"}, {"id": &"b", "goal": 1, "kind": &"kill"}])
	var excl: Array = tiny.call("Draw", 2, [&"a"])
	_check(excl.size() == 1 and excl[0]["id"] == &"b", "排除项被跳过")
	_check((tiny.call("Draw", 2, [&"a", &"b"]) as Array).is_empty(), "排除覆盖全池时安全返回空")

	# 5. Q05：批次耗尽跨批补足——20 轮刷新槽位恒 = MISSION_SLOTS（与序列无关的性质断言）
	var interop_q05 = cls.new()
	interop_q05.call("SetDefs", GameState.MISSION_POOL)
	var in_field: Array = []
	var all_full := true
	for i in 20:
		var d: Array = interop_q05.call("Draw", GameState.MISSION_SLOTS, in_field)
		if d.size() != GameState.MISSION_SLOTS:
			all_full = false
		in_field = []
		for def in d:
			in_field.append(def["id"])
	_check(all_full, "Q05：20 轮刷新槽位恒 = %d" % GameState.MISSION_SLOTS)

	# 6. 返回原始任务定义引用（id 身份映射：调用方读 def["id"]/["goal"] 不受影响）
	var ref_def: Dictionary = GameState.MISSION_POOL[1]
	var drawn: Array = interop.call("Draw", 9, [])
	var found_identity := false
	for def in drawn:
		if is_same(def, ref_def):
			found_identity = true
	_check(found_identity, "返回池内原始定义对象（is_same 引用同一）")

	# 7. 生产链路转发一致（task_pool.gd 已切 C# 壳；GameState.MISSION_POOL 走真实数据）
	var pool = TP.new()
	pool.defs = GameState.MISSION_POOL
	var prod: Array[Dictionary] = pool.draw(9, [])
	_check(prod.size() == 9 and _all_distinct(prod), "生产链路 TaskPool.draw(9) 全量无重复")
	var prod_excl: Array[Dictionary] = pool.draw(
		2, [&"kill_5", &"kill_15", &"kill_30", &"survive_60", &"survive_180", &"survive_300", &"boss_1"]
	)
	_check(prod_excl.size() == 2, "生产链路排除 7 项后可用 2 项")
	var all_excl: Array[Dictionary] = pool.draw(
		3, [&"kill_5", &"kill_15", &"kill_30", &"survive_60", &"survive_180", &"survive_300", &"boss_1", &"boss_2", &"boss_3"]
	)
	_check(all_excl.is_empty(), "生产链路全池排除返回空（不死循环）")

	print("TASK POOL INTEROP TEST DONE, failures = ", _failures)
	load("res://csharp/godot/TestExit.cs").Quit(_failures)
