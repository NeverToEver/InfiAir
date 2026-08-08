extends Node
## 触屏虚拟输入层测试（mobile touch，docs/archive/2026-08-07-deferred-restart-plan.md §3.1.5）：
## 场景1 挂载与默认禁用（桌面零回归前提）→ 设置开关联动 Main；
## 场景2 左摇杆：按下-拖动注入 move_* action（方向/幅度）→ 释放全清；
## 场景3 右摇杆：拖动注入 aim_* → 触屏模式下 player 瞄准随右摇杆上移（无鼠标基准）；
## 场景4 虚拟按钮：dash/parry/boost/fine_move 按下注入 press、释放 release；
## 场景5 禁用零回归：禁用后注入触摸不再产生任何 action 状态（键鼠/手柄不受影响）。

var _failures: int = 0
const VC_SCRIPT := preload("res://csharp/godot/VirtualControls.cs")


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _wait_real(sec: float) -> void:
	await get_tree().create_timer(sec, true, false, true).timeout


func _touch(idx: int, pressed: bool, pos: Vector2) -> void:
	vc.simulate_touch(idx, pressed, pos)
	await get_tree().process_frame
	await get_tree().process_frame


func _drag(idx: int, pos: Vector2) -> void:
	vc.simulate_drag(idx, pos)
	await get_tree().process_frame
	await get_tree().process_frame


var vc


func _ready() -> void:
	GameState.delete_save()
	GameState.reset_run()
	GameState.set_difficulty(&"medium")
	GameState.login_guest()
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	var main: Node = get_node("Main")
	var player = main.player()  # M3c：Player 迁 C#，不能作类型注解
	player.set_auto_fire(false)
	player.set_invincible(999.0)
	await get_tree().process_frame
	await get_tree().process_frame
	vc = GameState.virtual_controls
	_check(vc != null and is_instance_of(vc, VC_SCRIPT), "场景1: Main 挂载 VirtualControls 并经 GameState 转发")

	# 默认禁用（桌面零回归前提：touch_controls 默认 false）
	_check(not vc.is_enabled(), "场景1: 默认禁用（touch_controls=false）")
	# 设置开关联动 Main（GameState.touch_controls_changed → vc.set_enabled）
	GameState.set_touch_controls(true)
	_check(vc.is_enabled(), "场景1: 设置开关联动启用（touch_controls=true）")
	GameState.set_touch_controls(false)
	_check(not vc.is_enabled(), "场景1: 开关关闭联动禁用")
	vc.set_enabled(true)

	# ---- 场景2：左摇杆 → move_* ----
	await _touch(0, true, VC_SCRIPT.move_center())
	_check(not Input.is_action_pressed("move_right"), "场景2: 中心按下（死区内）不注入移动")
	await _drag(0, VC_SCRIPT.move_center() + Vector2(VC_SCRIPT.move_radius(), 0.0))
	_check(vc.move_vec().distance_to(Vector2(1.0, 0.0)) < 0.05, "场景2: 右推 → move_vec=(1,0)")
	_check(Input.is_action_pressed("move_right"), "场景2: move_right action 注入按下")
	_check(Input.get_action_strength("move_right") > 0.9, "场景2: move_right 强度 ≈1")
	_check(not Input.is_action_pressed("move_left"), "场景2: 反向 action 不注入")
	await _drag(0, VC_SCRIPT.move_center() + Vector2(0.0, -VC_SCRIPT.move_radius()))
	_check(vc.move_vec().distance_to(Vector2(0.0, -1.0)) < 0.05, "场景2: 上推 → move_vec=(0,-1)")
	_check(Input.is_action_pressed("move_up"), "场景2: move_up action 注入按下")
	await _touch(0, false, Vector2.ZERO)
	_check(vc.move_vec() == Vector2.ZERO, "场景2: 释放 → move_vec 归零")
	_check(not Input.is_action_pressed("move_right") and not Input.is_action_pressed("move_up"), "场景2: 释放 → 移动 action 全清")

	# ---- 场景3：右摇杆 → aim_* + 触屏瞄准行为 ----
	var aim_before = player.aim_point()  # M3c：player untyped，动态调用去 :=
	await _touch(1, true, VC_SCRIPT.aim_center())
	await _drag(1, VC_SCRIPT.aim_center() + Vector2(0.0, -VC_SCRIPT.aim_radius()))
	_check(vc.aim_vec().distance_to(Vector2(0.0, -1.0)) < 0.05, "场景3: 上推 → aim_vec=(0,-1)")
	_check(Input.is_action_pressed("aim_up"), "场景3: aim_up action 注入按下")
	await _wait_real(0.5)  # 增量驱动：瞄准点上移
	var aim_after = player.aim_point()  # M3c：player untyped，动态调用去 :=
	_check(aim_after.y < aim_before.y, "场景3: 触屏模式瞄准随右摇杆上移（无鼠标基准）")
	await _touch(1, false, Vector2.ZERO)
	_check(vc.aim_vec() == Vector2.ZERO, "场景3: 释放 → aim_vec 归零")
	_check(not Input.is_action_pressed("aim_up"), "场景3: 释放 → aim action 全清")

	# ---- 场景4：虚拟按钮 ----
	var dash_btn: Dictionary = VC_SCRIPT.buttons()[&"dash"]
	await _touch(2, true, dash_btn["center"])
	_check(Input.is_action_pressed("dash"), "场景4: dash 按钮按下 → action press")
	await _touch(2, false, Vector2.ZERO)
	_check(not Input.is_action_pressed("dash"), "场景4: dash 释放 → action release")
	var parry_btn: Dictionary = VC_SCRIPT.buttons()[&"parry"]
	await _touch(3, true, parry_btn["center"])
	_check(Input.is_action_pressed("parry"), "场景4: parry 按钮按下 → action press")
	await _touch(3, false, Vector2.ZERO)
	var boost_btn: Dictionary = VC_SCRIPT.buttons()[&"boost"]
	await _touch(4, true, boost_btn["center"])
	_check(Input.is_action_pressed("boost"), "场景4: boost 按钮按下 → action press")
	await _touch(4, false, Vector2.ZERO)
	var fine_btn: Dictionary = VC_SCRIPT.buttons()[&"fine_move"]
	await _touch(5, true, fine_btn["center"])
	_check(Input.is_action_pressed("fine_move"), "场景4: fine_move 按钮按下 → action press")
	await _touch(5, false, Vector2.ZERO)
	_check(
		not Input.is_action_pressed("parry") and not Input.is_action_pressed("boost") and not Input.is_action_pressed("fine_move"),
		"场景4: 按钮全部释放后 action 全清"
	)

	# ---- 场景5：禁用零回归 ----
	vc.set_enabled(false)
	await _touch(6, true, VC_SCRIPT.move_center() + Vector2(VC_SCRIPT.move_radius(), 0.0))
	_check(not Input.is_action_pressed("move_right"), "场景5: 禁用后触摸注入不产生 move 状态")
	await _touch(6, false, Vector2.ZERO)
	var dash2: Dictionary = VC_SCRIPT.buttons()[&"dash"]
	await _touch(7, true, dash2["center"])
	_check(not Input.is_action_pressed("dash"), "场景5: 禁用后触摸注入不产生按钮状态")
	await _touch(7, false, Vector2.ZERO)
	# 恢复默认（清理持久化副作用）
	GameState.set_touch_controls(false)

	print("VIRTUAL CONTROLS TEST DONE, failures = ", _failures)
	load("res://csharp/godot/TestExit.cs").Quit(1 if _failures > 0 else 0)
