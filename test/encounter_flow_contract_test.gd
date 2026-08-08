extends Node
## 遭遇流程契约测试（2026-08-07，登记待办补齐：AUDIT_VAULT R 系列 #9 + 2026-08-06 批次 #7）：
## T3a 自动触发短窗口契约：smoke 式测试窗口（数秒）内自动遭遇保持惰性——interval
##   （精英 45s / 编队 40s）≫ 窗口，断言无事件启动 + 计时未归零 + interval 配置下界
##   契约锚点（防未来调小到测试窗口内污染断言）。
## T3b 独立断言（原由 main 流程回归间接覆盖）：
##   L-d 遭遇事件进行中禁止蓄力（can_charge 事件互斥，防母舰清场全额领奖挂机收益）；
##   L-b 死亡路径清理召唤小窗（give_up 与 dock 蓄力同帧完成时小窗不永驻）。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## 真实时间等待（不受 time_scale 影响）
func _wait_real(sec: float) -> void:
	await get_tree().create_timer(sec, true, false, true).timeout


## 注入 action 按住 seconds 秒再释放（真实输入管线：InputEventAction → Input.parse_input_event）
func _hold_action(action: StringName, seconds: float) -> void:
	var down := InputEventAction.new()
	down.action = action
	down.pressed = true
	Input.parse_input_event(down)
	await _wait_real(seconds)
	var up := InputEventAction.new()
	up.action = action
	up.pressed = false
	Input.parse_input_event(up)
	await get_tree().process_frame


func _ready() -> void:
	GameState.delete_save()
	GameState.reset_run()
	GameState.set_difficulty(&"medium")
	GameState.login_guest()
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	var main: Node = get_node("Main")
	var player = main.player()
	player.set_auto_fire(false)
	player.set_invincible(999.0)
	await get_tree().process_frame
	await get_tree().process_frame
	var events = GameState.events

	# ---- T3a：自动遭遇短窗口契约（spawner 保持处理中，自动触发路径活跃） ----
	_check(events.active_id(events.GROUP_ENCOUNTER) == &"", "T3a: 开局无遭遇事件")
	_check(events.encounter_timer_remaining(&"elite_turret") > 0.0, "T3a: 开局精英计时未归零")
	_check(events.encounter_timer_remaining(&"formation_strike") > 0.0, "T3a: 开局编队计时未归零")
	await _wait_real(3.0)
	_check(events.active_id(events.GROUP_ENCOUNTER) == &"", "T3a: 3s 短窗口内无自动遭遇触发（interval ≫ 窗口）")
	_check(events.encounter_timer_remaining(&"elite_turret") > 0.0, "T3a: 3s 后精英计时仍 >0（未归零触发）")
	_check(events.encounter_timer_remaining(&"formation_strike") > 0.0, "T3a: 3s 后编队计时仍 >0（未归零触发）")
	# 契约锚点：interval 配置下界（防未来把自动触发窗口调进测试运行时长内）
	_check(float(GameState.cfg("elite_turret_event.trigger_interval", 45.0)) >= 20.0, "T3a: 精英 trigger_interval >= 20s（配置契约锚点）")
	_check(float(GameState.cfg("formation_strike_event.trigger_interval", 40.0)) >= 20.0, "T3a: 编队 trigger_interval >= 20s（配置契约锚点）")

	# ---- T3b / L-d：遭遇事件进行中禁止蓄力 ----
	_check(events.force_trigger(&"elite_turret"), "L-d: 强制触发精英事件成功")
	_check(events.active_id(events.GROUP_ENCOUNTER) != &"", "L-d: 遭遇事件进行中")
	await _hold_action(&"dock", 0.4)
	_check(not main.charging(), "L-d: 事件进行中蓄力被拒（can_charge 事件互斥）")
	events.end_active(events.GROUP_ENCOUNTER)
	await _wait_real(0.3)  # 等事件清理/撤离

	# ---- T3b / L-b：死亡路径清理召唤小窗 ----
	main.summon_mothership()
	await get_tree().process_frame
	_check(main.summon_window() != null, "L-b: 召唤小窗已打开")
	GameState.PlayerDied.emit()
	await get_tree().process_frame
	await get_tree().process_frame
	_check(main.summon_window() == null, "L-b: 死亡路径清理召唤小窗（不永驻）")

	print("ENCOUNTER FLOW CONTRACT TEST DONE, failures = ", _failures)
	load("res://csharp/godot/TestExit.cs").Quit(1 if _failures > 0 else 0)
