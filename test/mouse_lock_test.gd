extends Node
## 鼠标锁定窗口内设置项测试：
## 默认开启、切换与信号、profile 持久化往返、旧档兼容、设置页开关 wiring、MouseTrap 边界 clamp 纯函数。
## headless 下窗口事件不可模拟，仅断言数据层/纯函数/UI wiring；结束时恢复默认并落盘，避免污染其他测试进程。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _read_profile() -> Dictionary:
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(GameState.PROFILE_PATH))
	return parsed if parsed is Dictionary else {}


func _write_profile(data: Dictionary) -> void:
	var f := FileAccess.open(GameState.PROFILE_PATH, FileAccess.WRITE)
	f.store_string(JSON.stringify(data))
	f.close()


func _ready() -> void:
	# 确定性起点：清存档，mouse_lock 归位默认
	GameState.delete_save()
	GameState.mouse_lock = true

	# ---------- 1. 默认值 ----------
	_check(GameState.mouse_lock, "mouse_lock 默认开启")

	# ---------- 2. 切换与信号 ----------
	var emitted: Array[bool] = []
	GameState.mouse_lock_changed.connect(func(enabled: bool) -> void: emitted.append(enabled))
	GameState.set_mouse_lock(false)
	_check(not GameState.mouse_lock, "set_mouse_lock(false) 关闭锁定")
	_check(emitted.size() == 1 and emitted[0] == false, "切换发出 mouse_lock_changed 信号")
	GameState.set_mouse_lock(false)
	_check(emitted.size() == 1, "同值重复设置不发信号")
	GameState.set_mouse_lock(true)
	_check(GameState.mouse_lock, "set_mouse_lock(true) 重新开启")
	_check(emitted.size() == 2 and emitted[1] == true, "再次切换发出开启信号")

	# ---------- 3. profile 持久化 ----------
	GameState.set_mouse_lock(false)
	_check(_read_profile().get("mouse_lock", true) == false, "mouse_lock 写入 profile")
	GameState.mouse_lock = true  # 篡改内存（不经 setter，避免写盘）
	GameState.load_profile()
	_check(not GameState.mouse_lock, "mouse_lock 从 profile 恢复")

	# ---------- 4. 旧档兼容 ----------
	_write_profile({"version": 1, "high_score": 0})
	GameState.mouse_lock = false
	GameState.load_profile()
	_check(not GameState.mouse_lock, "旧档（无 mouse_lock 字段）读取保留当前值")

	# ---------- 5. MouseTrap 边界 clamp 纯函数 ----------
	const MOUSE_TRAP: GDScript = preload("res://scripts/mouse_trap.gd")
	_check(
		MOUSE_TRAP._warp_target(Vector2(-50, -50), Vector2i(1920, 1080)) == Vector2(1, 1),
		"clamp 左上越界到 (1,1)",
	)
	_check(
		MOUSE_TRAP._warp_target(Vector2(5000, 5000), Vector2i(1920, 1080)) == Vector2(1919, 1079),
		"clamp 右下越界到 (size-1)",
	)
	_check(
		MOUSE_TRAP._warp_target(Vector2(960, 540), Vector2i(1920, 1080)) == Vector2(960, 540),
		"窗口内点不变",
	)
	_check(
		MOUSE_TRAP._warp_target(Vector2(0, 0), Vector2i(1920, 1080)) == Vector2(1, 1),
		"原点 (0,0) clamp 到边缘内侧 (1,1)",
	)

	print("MOUSE LOCK TEST DONE, failures = ", _failures)
	# 清理：恢复默认并落盘，避免污染其他测试进程
	GameState.mouse_lock = true
	GameState.reset_run()
	GameState.save_profile()
	GameState.delete_save()
	get_tree().quit(_failures)
