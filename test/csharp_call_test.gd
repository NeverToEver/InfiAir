extends Node
## M1 跨语言调用探针：GDScript → C#（Starfield.cs）动态派发命名/参数/返回值验证。
## 全量迁移期 C# 方法以 PascalCase 注册进引擎，GDScript 动态调用须同名（禁止 snake_case）。

var _starfield_script := load("res://csharp/godot/Starfield.cs")

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _ready() -> void:
	var script := _starfield_script
	_check(script != null, "Starfield.cs 可加载")
	var sf = script.new()  # load().new() 返回无类型 Variant，不能用 := 推断
	_check(sf != null, "Starfield.cs 可 new")
	# 动态派发：PascalCase 方法名 + 参数 + 返回值（跨语言唯一可靠通道）
	sf.Warp(12.0)
	_check(sf.Origin() is Vector2, "Origin() 返回 Vector2")
	_check(sf.AreaSize() is Vector2, "AreaSize() 返回 Vector2")
	_check(sf.WarpFactor == 12.0, "C# 属性 getter 动态读取（WarpFactor）")
	_check(sf.StaticProbe() == 42, "C# 静态方法经实例调用")
	_check(_starfield_script.StaticProbe() == 42, "C# 静态方法经脚本资源调用")
	# 重载注册探针（可选参数方法不注册——实测）
	var ov_ok := true
	sf.ProbeOverload(1.0)
	sf.ProbeOverload(1.0, true)
	_check(ov_ok, "C# 重载方法可调（2 种签名）")
	print("CSHARP CALL TEST DONE, failures = ", _failures)
	get_tree().quit(_failures)
