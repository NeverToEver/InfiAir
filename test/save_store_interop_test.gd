extends Node
## P0-1 断言场景：SaveStoreInterop（C# 绑定壳）——save/load 往返、原子覆盖、
## 损坏隔离（.corrupt + corrupt 标记）、缺失三态；并经 SaveManager 壳验证生产转发路径。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _ready() -> void:
	# 0. C# 绑定壳可加载并实例化
	var cls: Variant = load("res://csharp/godot/SaveStoreInterop.cs")
	_check(cls != null, "SaveStoreInterop 脚本资源可加载")
	var interop = cls.new()
	var path := "user://save_store_interop_test.json"
	interop.call("Delete", path)
	interop.call("Delete", path + ".corrupt")
	interop.call("Delete", path + ".tmp")

	# 1. save/load 往返（含嵌套结构）
	_check(interop.call("Save", path, {"version": 2, "score": 500, "nested": {"k": "v"}, "arr": [1, 2.5, true, null]}) == true, "save 成功")
	_check(interop.call("Exists", path) == true, "save 后正本存在（非孤立 tmp）")
	_check(not FileAccess.file_exists(path + ".tmp"), "原子写不留 tmp")
	var loaded: Variant = interop.call("Load", path)
	_check(loaded is Dictionary and loaded.get("corrupt") == false, "load 无损坏标记")
	if loaded is Dictionary:
		var data: Variant = loaded.get("data")
		_check(data is Dictionary and int((data as Dictionary).get("score", -1)) == 500, "load 数据正确")
		_check((data as Dictionary).get("nested") is Dictionary and (data as Dictionary).get("nested")["k"] == "v", "嵌套结构往返")

	# 2. 覆盖写（原子替换不丢数据）
	_check(interop.call("Save", path, {"score": 999}) == true, "覆盖 save 成功")
	loaded = interop.call("Load", path)
	_check(int((loaded.get("data") as Dictionary).get("score", -1)) == 999, "覆盖后数据正确")
	_check((loaded.get("data") as Dictionary).get("version") == null, "覆盖后旧键消失")

	# 3. 缺失文件 → 空数据不置损坏
	interop.call("Delete", path)
	loaded = interop.call("Load", path)
	_check(loaded is Dictionary and loaded.get("corrupt") == false, "缺失文件不置损坏")
	_check(loaded.get("data") == null, "缺失文件返回空数据")

	# 4. 损坏隔离（损坏 JSON → corrupt 标记 + .corrupt 备份 + 正本移除）
	var f := FileAccess.open(path, FileAccess.WRITE)
	f.store_string("{broken json")
	f.close()
	loaded = interop.call("Load", path)
	_check(loaded.get("corrupt") == true, "损坏文件置 corrupt 标记")
	_check(loaded.get("data") == null, "损坏后按无存档处理")
	_check(FileAccess.file_exists(path + ".corrupt"), "损坏文件隔离为 .corrupt")
	_check(not FileAccess.file_exists(path), "隔离后正本消失")

	# 5. 非对象根 JSON 亦视为损坏（对齐 json.data is Dictionary 判定）
	f = FileAccess.open(path, FileAccess.WRITE)
	f.store_string("[1, 2, 3]")
	f.close()
	loaded = interop.call("Load", path)
	_check(loaded.get("corrupt") == true, "数组根 JSON 亦隔离")
	_check(FileAccess.file_exists(path + ".corrupt"), "数组根 JSON 隔离出 .corrupt")

	# 6. SaveManager 壳（生产转发路径：GameState/base_system_test 同款调用）
	var sm := SaveManager.new()
	_check(sm.save(path, {"score": 123}), "SaveManager 壳 save 成功")
	_check(sm.exists(path), "SaveManager 壳 exists 命中")
	_check(int(sm.load(path).get("score", -1)) == 123, "SaveManager 壳 load 正确")
	_check(not sm.last_was_corrupt, "SaveManager 壳正常读不置损坏")
	f = FileAccess.open(path, FileAccess.WRITE)
	f.store_string("garbage!!")
	f.close()
	_check(sm.load(path).is_empty() and sm.last_was_corrupt, "SaveManager 壳损坏标记透传")
	_check(sm.sanitize_num("x", 1.0) == 1.0 and sm.sanitize_num(5, 1.0) == 5.0, "sanitize_num 语义不变")

	sm.delete(path)
	sm.delete(path + ".corrupt")
	print("SAVE STORE INTEROP TEST DONE, failures = ", _failures)
	load("res://csharp/godot/TestExit.cs").Quit(_failures)
