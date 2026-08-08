extends Node
## P1-1 断言场景：PathResolverInterop（C# 绑定壳）——GDScript 经互操作层解析配置路径，
## 验证 InfiAir.Core.Config.PathResolver 语义（数值宽容/容器拷贝/缺键回退）在引擎环境内可用；
## 生产链路已接入（BalanceService.cfg 转发，本场景直测绑定壳 + 真实 balance.json 抽查）。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _ready() -> void:
	# 1. C# 绑定壳可加载并实例化
	var cls: Variant = load("res://csharp/godot/PathResolverInterop.cs")
	_check(cls != null, "PathResolverInterop 脚本资源可加载")
	var interop = cls.new()

	# 2. 合成树：嵌套/数值宽容/容器拷贝/类型判定
	(
		interop
		. call(
			"SetData",
			{
				"player": {"max_speed": 420, "fuel": {"drain": 35.0}},
				"boss": {"hp_mults": [1.3, 1.5, 1.8, 2.2]},
				"flag": true,
				"label": "str",
			}
		)
	)
	_check(interop.call("Resolve", "player.max_speed", 0) == 420, "嵌套整型路径解析")
	_check(interop.call("Resolve", "player.fuel.drain", 0) == 35, "float 节点 + int 默认 → 截断")
	_check(interop.call("Resolve", "player.max_speed", 0.0) == 420.0, "int 节点 + float 默认 → 拓宽")
	_check(interop.call("Resolve", "player.missing", 7) == 7, "缺键回退默认")
	_check(interop.call("Resolve", "flag", false) == true, "bool 节点原样返回")
	_check(interop.call("Resolve", "label", "d") == "str", "string 节点原样返回")
	_check(interop.call("Resolve", "label", &"d") == "d", "StringName 默认与 String 节点类型不符 → 回退")
	var arr: Variant = interop.call("Resolve", "boss.hp_mults", [])
	_check(arr is Array and (arr as Array).size() == 4, "Array 默认返回容器")
	if arr is Array:
		(arr as Array).clear()  # 修改返回值不应影响配置树
	_check((interop.call("Resolve", "boss.hp_mults", []) as Array).size() == 4, "容器拷贝隔离（清空不影响）")

	# 3. 真实 data/balance.json 抽查（与 balance_test.gd 同源值）
	var raw: Variant = JSON.parse_string(FileAccess.get_file_as_string(GameState.BALANCE_PATH))
	_check(raw is Dictionary, "balance.json 可解析")
	if raw is Dictionary:
		interop.call("SetData", raw)
		_check(interop.call("Resolve", "version", 0) == 2, "真实文件 version = 2")
		_check(interop.call("Resolve", "player.max_speed", 0) == 420, "真实文件 player.max_speed = 420")
		_check(interop.call("Resolve", "mothership.mag_cells", 0) == 10, "真实文件 mothership.mag_cells = 10")
		_check(interop.call("Resolve", "mothership.missile.damage", 0) == 80, "真实文件 母舰导弹 80")

	# 4. 与生产转发路径一致（BalanceService.cfg 已切 C# 壳）
	_check(GameState.cfg("player.fuel.drain", 0.0) == 35.0, "生产链路 cfg 转发正常（float）")
	_check(GameState.cfg("mothership.mag_cells", 0) == 10, "生产链路 cfg 转发正常（int）")
	var diff_cfg: Variant = GameState.cfg("difficulty", {})
	_check(diff_cfg is Dictionary, "生产链路 cfg 返回 Dictionary")
	if diff_cfg is Dictionary:
		(diff_cfg as Dictionary).clear()
	_check(GameState.enemy_hp_multiplier() == 1.0, "生产链路 cfg 容器拷贝隔离")

	print("PATH RESOLVER INTEROP TEST DONE, failures = ", _failures)
	load("res://csharp/godot/TestExit.cs").Quit(_failures)
