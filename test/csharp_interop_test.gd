extends Node
## C# 互操作断言场景：GDScript 加载 C# 绑定类（BalanceInterop）→ 调用其解析
## → 验证跨语言调用路径 + InfiAir.Core 纯逻辑在引擎环境内可用。
## 生产运行时链路零改动（GameState.cfg() 不受影响）。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _ready() -> void:
	# 1. C# 类可加载（.NET 版引擎 + 主程序集内类型）
	var cls: Variant = load("res://csharp/godot/BalanceInterop.cs")
	_check(cls != null, "C# 脚本资源可加载")
	_check(cls is Script, "C# 脚本资源类型为 Script")

	# 2. 实例化并跨语言调用
	var interop = cls.new()
	var res: Variant = interop.call("ParseBalance", GameState.BALANCE_PATH)
	_check(res is Dictionary, "跨语言调用返回 Dictionary")

	# 3. 解析真实 data/balance.json 的抽查值（与 balance_test.gd 同源）
	if res is Dictionary:
		_check(res.get("ok") == true, "balance.json 解析成功")
		_check(res.get("error") == "", "无解析错误")
		_check(res.get("version") == 2, "version = 2")
		_check(res.get("max_speed") == 420, "player.max_speed = 420")
		_check(res.get("mag_cells") == 10, "mothership.mag_cells = 10")

	# 4. 损坏输入 → ok=false 且带错误信息（对齐"损坏回退"语义）
	var broken: Variant = interop.call("ParseBalance", "res://test/does_not_exist.json")
	_check(broken is Dictionary and broken.get("ok") == false, "缺失文件 → ok=false")

	print("CSHARP_INTEROP TEST DONE, failures = ", _failures)
	get_tree().quit(_failures)
