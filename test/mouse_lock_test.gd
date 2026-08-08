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


## M7（2026-08-06 审计）：profile 快照还原——原测试经 _write_profile 部分覆写
## profile.json + load_profile 间接清零 pre-login 最高分/高分榜并落盘，无快照还原
## （L15 只修直写路径，未覆盖间接清零）；备份/还原防本地数据被永久销毁
var _profile_backup: Dictionary = {}


func _backup_profile() -> void:
	_profile_backup = {}
	for f in [GameState.PROFILE_PATH, GameState.PROFILE_PATH + ".corrupt"]:
		var exists := FileAccess.file_exists(f)
		_profile_backup[f] = {"exists": exists, "content": FileAccess.get_file_as_string(f) if exists else ""}


func _restore_profile() -> void:
	for f in _profile_backup:
		var b: Dictionary = _profile_backup[f]
		if b["exists"]:
			var fh := FileAccess.open(f, FileAccess.WRITE)
			fh.store_string(b["content"])
			fh.close()
		elif FileAccess.file_exists(f):
			DirAccess.remove_absolute(f)


func _ready() -> void:
	# M7：profile 快照（须在任何覆写/落盘前捕获原始 pre-login 最高分与高分榜）
	_backup_profile()
	# 确定性起点：清存档，mouse_lock 归位默认
	GameState.delete_save()
	GameState.mouse_lock = true

	# ---------- 1. 默认值 ----------
	_check(GameState.mouse_lock, "mouse_lock 默认开启")

	# ---------- 2. 切换与信号 ----------
	var emitted: Array[bool] = []
	GameState.MouseLockChanged.connect(func(enabled: bool) -> void: emitted.append(enabled))
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
	const MOUSE_TRAP := preload("res://csharp/godot/MouseTrap.cs")
	_check(
		MOUSE_TRAP.warp_target(Vector2(-50, -50), Vector2i(1920, 1080)) == Vector2(1, 1),
		"clamp 左上越界到 (1,1)",
	)
	_check(
		MOUSE_TRAP.warp_target(Vector2(5000, 5000), Vector2i(1920, 1080)) == Vector2(1919, 1079),
		"clamp 右下越界到 (size-1)",
	)
	_check(
		MOUSE_TRAP.warp_target(Vector2(960, 540), Vector2i(1920, 1080)) == Vector2(960, 540),
		"窗口内点不变",
	)
	_check(
		MOUSE_TRAP.warp_target(Vector2(0, 0), Vector2i(1920, 1080)) == Vector2(1, 1),
		"原点 (0,0) clamp 到边缘内侧 (1,1)",
	)

	# ---------- 6. 设置页开关 wiring ----------
	const SETTINGS_SCRIPT := preload("res://csharp/godot/SettingsUi.cs")
	var settings := SETTINGS_SCRIPT.new() as CanvasLayer
	add_child(settings)
	settings.show_settings()
	var lock_btn := settings.mouse_lock_button() as Button
	_check(lock_btn.button_pressed == GameState.mouse_lock, "设置页鼠标锁定按钮选中态 = 当前设置")
	lock_btn.button_pressed = false
	lock_btn.pressed.emit()
	_check(not GameState.mouse_lock, "鼠标锁定按钮点击关闭")
	lock_btn.button_pressed = true
	lock_btn.pressed.emit()
	_check(GameState.mouse_lock, "鼠标锁定按钮再点开启")
	settings.queue_free()
	GameState.set_mouse_lock(true)

	# ---------- 7. confine 放行判定纯函数（仅对局准星态生效；暂停/非准星态放行） ----------
	_check(
		MOUSE_TRAP.trap_enabled(true, true, true, true, true, true),
		"对局准星活跃态 confine 生效",
	)
	_check(
		not MOUSE_TRAP.trap_enabled(true, true, true, true, false, true),
		"暂停态放行（暂停后可自由移出窗口，如点系统关闭按钮）",
	)
	_check(
		not MOUSE_TRAP.trap_enabled(true, true, true, true, true, false),
		"系统光标可见态放行（菜单/设置/基地等非准星态）",
	)
	_check(
		not MOUSE_TRAP.trap_enabled(false, true, true, true, true, true),
		"设置关闭不 confine",
	)
	_check(
		not MOUSE_TRAP.trap_enabled(true, false, true, true, true, true),
		"窗口不可见不 confine",
	)
	_check(
		not MOUSE_TRAP.trap_enabled(true, true, false, true, true, true),
		"窗口失焦不 confine",
	)
	_check(
		not MOUSE_TRAP.trap_enabled(true, true, true, false, true, true),
		"无窗口尺寸（headless）不 confine",
	)

	# ---------- 8. warp 不引入准星跳变：warp 目标 ≈ 出框前最后位置（位移 ≤ 2px） ----------
	var edge_pos := Vector2(1918, 500)  # 右缘附近出框前最后内部位置
	_check(
		(MOUSE_TRAP.warp_target(edge_pos, Vector2i(1920, 1080)) - edge_pos).length() <= 2.0,
		"右缘出框 warp 位移 ≤ 2px（aim_point 平滑增量≈0，无准星跳变）",
	)
	var edge_top := Vector2(500, 0)  # 上缘第 0 行（clamp 边界）
	_check(
		(MOUSE_TRAP.warp_target(edge_top, Vector2i(1920, 1080)) - edge_top).length() <= 2.0,
		"上缘出框 warp 位移 ≤ 2px（第 0 行仅 1px 回拉）",
	)

	print("MOUSE LOCK TEST DONE, failures = ", _failures)
	# 清理：恢复默认并落盘，避免污染其他测试进程
	GameState.mouse_lock = true
	GameState.reset_run()
	GameState.save_profile()
	GameState.delete_save()
	# M7：还原原始 profile（最高分/高分榜/设置项），防本地数据被清零
	_restore_profile()
	load("res://csharp/godot/TestExit.cs").Quit(_failures)
