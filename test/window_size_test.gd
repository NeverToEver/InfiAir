extends Node
## 窗口尺寸档位测试：
## 三档映射与切换信号、profile 持久化往返、非法/缺失字段回退、设置页三选按钮 wiring。
## headless 下窗口 API 为 dummy，仅断言数据层；结束时恢复默认档并落盘，避免污染其他测试进程。

var _failures: int = 0

const SETTINGS_SCRIPT: GDScript = preload("res://scripts/settings_ui.gd")


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
	# 确定性起点：清存档，窗口档位归位 large（profile 级，reset_run 不清）
	GameState.delete_save()
	GameState.window_size = &"large"

	# ---------- 1. 档位映射 ----------
	_check(GameState.WINDOW_SIZE_LEVELS[&"small"] == Vector2i(1280, 720), "small 档 = 1280×720")
	_check(GameState.WINDOW_SIZE_LEVELS[&"medium"] == Vector2i(1600, 900), "medium 档 = 1600×900")
	_check(GameState.WINDOW_SIZE_LEVELS[&"large"] == Vector2i(1920, 1080), "large 档 = 1920×1080")
	_check(GameState.WINDOW_SIZE_ORDER == [&"small", &"medium", &"large"], "档位顺序 small/medium/large")

	# ---------- 2. 切换与信号 ----------
	GameState.set_window_size(&"small")
	_check(GameState.window_size == &"small", "set_window_size 切到 small")
	GameState.set_window_size(&"medium")
	_check(GameState.window_size == &"medium", "set_window_size 切到 medium")
	GameState.set_window_size(&"large")
	_check(GameState.window_size == &"large", "set_window_size 切到 large")
	var emitted: Array[StringName] = []
	GameState.window_size_changed.connect(func(l: StringName) -> void: emitted.append(l))
	GameState.set_window_size(&"small")
	_check(emitted.size() == 1 and emitted[0] == &"small", "切换档位发出 window_size_changed 信号")
	GameState.set_window_size(&"small")
	_check(emitted.size() == 1, "同档重复设置不发信号")
	GameState.set_window_size(&"huge")
	_check(GameState.window_size == &"small", "非法档位被忽略")

	# ---------- 3. profile 持久化 ----------
	GameState.set_window_size(&"medium")
	_check(str(_read_profile().get("window_size", "")) == "medium", "窗口档位写入 profile")
	GameState.window_size = &"small"  # 篡改内存（不经 setter，避免写盘）
	GameState.load_profile()
	_check(GameState.window_size == &"medium", "窗口档位从 profile 恢复")
	# 旧档案无 window_size 字段：保留当前值（回默认 large 语义由默认值保证）
	_write_profile({"version": 1, "high_score": 0})
	GameState.window_size = &"small"
	GameState.load_profile()
	_check(GameState.window_size == &"small", "旧档（无 window_size 字段）读取保留当前档位")
	# 非法档位值：忽略并保持当前值
	_write_profile({"version": 1, "high_score": 0, "window_size": "huge"})
	GameState.load_profile()
	_check(GameState.window_size == &"small", "profile 非法档位值被忽略")
	GameState.set_window_size(&"large")

	# ---------- 4. 设置页三选按钮 ----------
	var settings := SETTINGS_SCRIPT.new() as CanvasLayer
	add_child(settings)
	settings.show_settings()
	_check(settings.window_buttons().size() == 3, "设置页窗口大小三选按钮")
	_check(
		(settings.window_buttons()[&"large"] as Button).button_pressed,
		"窗口大小按钮选中态 = 当前档"
	)
	(settings.window_buttons()[&"medium"] as Button).pressed.emit()
	_check(GameState.window_size == &"medium", "窗口大小按钮点击切换档位")
	settings.queue_free()
	GameState.set_window_size(&"large")

	print("WINDOW SIZE TEST DONE, failures = ", _failures)
	# 清理：恢复默认档并落盘，避免污染其他测试进程
	GameState.set_window_size(&"large")
	GameState.reset_run()
	GameState.save_profile()
	GameState.delete_save()
	get_tree().quit(_failures)
