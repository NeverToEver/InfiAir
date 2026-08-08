extends Node
## 基地数据层测试：RP 经济、三常驻任务、天赋路线互斥、存档往返。
## 只操作 GameState autoload，不加载 main 场景。

var _failures: int = 0
const SM := preload("res://csharp/godot/SaveManager.cs")


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## M7（2026-08-06 审计）：profile 快照还原——base_system 的高分榜段直写
## GameState.highscores + save_profile 清零落盘（L15 档案称已修与 git 事实不符），
## 备份/还原防本地 pre-login 最高分与高分榜被永久销毁
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


## 2026-08-06 审计：键位快照还原（H02 段 rebind/reset 自动落盘，防开发者键位被重置）
var _key_backup: Dictionary = {}


func _backup_keys() -> void:
	_key_backup = GameState.key_bindings.duplicate(true)


func _restore_keys() -> void:
	GameState.key_bindings = _key_backup.duplicate(true)
	GameState.apply_key_bindings()
	GameState.save_profile()


func _ready() -> void:
	# M7：profile 快照（须在任何覆写/落盘前捕获原始 pre-login 最高分与高分榜）
	_backup_profile()
	# 键位快照（H02 改键段自动落盘）
	_backup_keys()
	# 清理持久化状态，保证测试确定性
	GameState.delete_save()
	GameState.reset_run()

	# 1. 初始状态
	_check(GameState.rp == 0, "初始 RP 为 0")
	_check(GameState.mission_progress(&"kill_5") == 0, "初始任务进度为 0")
	_check(not GameState.is_mission_done(&"boss_1"), "初始任务未完成")

	# 2. Boss 击杀 +5RP，并推进 boss_1 任务
	GameState.add_boss_kill()
	_check(GameState.rp == 5, "Boss 击杀 +5RP")
	_check(GameState.is_mission_done(&"boss_1"), "boss_1 任务完成")
	_check(GameState.mission_progress(&"boss_1") == 1, "boss_1 进度为 1")

	# 3. 领取奖励 + 重复领奖拒绝
	_check(GameState.claim_mission(&"boss_1"), "领取 boss_1 奖励成功")
	_check(GameState.rp == 8, "任务奖励 +3RP 入账")
	_check(not GameState.claim_mission(&"boss_1"), "重复领奖被拒绝")
	_check(GameState.rp == 8, "重复领奖不重复入账")
	_check(not GameState.claim_mission(&"kill_5"), "未完成任务不能领奖")

	# 4. kill_5：击杀计数到 5
	for i in 5:
		GameState.add_kill()
	_check(GameState.mission_progress(&"kill_5") == 5, "kill_5 进度追踪击杀数")
	_check(GameState.is_mission_done(&"kill_5"), "kill_5 任务完成")
	_check(GameState.claim_mission(&"kill_5"), "领取 kill_5 奖励成功")
	_check(GameState.rp == 11, "RP 累计正确")

	# 5. survive_180：对局存活秒数（用真实时间等待跨过 180s 阈值）
	GameState.run_time = 179.9
	await get_tree().create_timer(0.3).timeout
	_check(GameState.mission_progress(&"survive_180") >= 180, "survive_180 进度按存活秒数推进")
	_check(GameState.is_mission_done(&"survive_180"), "survive_180 任务完成")
	_check(GameState.claim_mission(&"survive_180"), "领取 survive_180 奖励成功")
	_check(GameState.rp == 14, "三任务 RP 全部入账")

	# 6. spend_rp 余额校验
	_check(not GameState.spend_rp(99), "余额不足 spend_rp 返回 false")
	_check(GameState.rp == 14, "余额不足不扣减")
	_check(GameState.spend_rp(GameState.RP_REPAIR_COST), "维修消费 2RP 成功")
	_check(GameState.rp == 12, "消费后余额正确")

	# 7. 天赋路线：合并层数 + 锁定未选 buff
	GameState.add_buff(&"spread_shot")
	GameState.add_buff(&"spread_shot")
	GameState.add_buff(&"laser_beam")
	_check(not GameState.choose_route(&"offense", &"phase_dash"), "不属于该线的 buff 被拒绝")
	_check(not GameState.choose_route(&"bad_line", &"spread_shot"), "非法路线名被拒绝")
	_check(not GameState.choose_route(&"mobility", &"phase_dash"), "零层数路线被拒绝")
	_check(GameState.choose_route(&"offense", &"spread_shot"), "路线选择成功")
	_check(GameState.buff_count(&"spread_shot") == 3, "同线层数合并到所选 buff")
	_check(GameState.buff_count(&"laser_beam") == 0, "未选 buff 层数清零")
	_check(GameState.is_buff_locked(&"laser_beam"), "未选 buff 被锁定")
	_check(not GameState.is_buff_locked(&"spread_shot"), "所选 buff 不锁定")
	_check(not GameState.is_buff_locked(&"phase_dash"), "未选路线的线不锁定")
	_check(GameState.chosen_routes.get(&"offense") == &"spread_shot", "路线选择已记录")

	# 8. 存档往返：rp / 路线 / 任务进度全保留
	GameState.save_run(50.0, GameState.run_time)
	var saved_rp = GameState.rp
	GameState.rp = 0
	GameState.buffs.clear()
	GameState.reset_missions()
	GameState.chosen_routes.clear()
	GameState.locked_routes.clear()
	GameState.apply_run_save(GameState.load_run_data())
	_check(GameState.rp == saved_rp, "存档恢复 RP")
	_check(GameState.mission_progress(&"kill_5") == 5, "存档恢复任务进度")
	_check(GameState.is_mission_claimed(&"boss_1"), "存档恢复任务已领取标记")
	_check(not GameState.claim_mission(&"boss_1"), "恢复后已领取任务仍拒绝重复领奖")
	_check(GameState.buff_count(&"spread_shot") == 3, "存档恢复合并后的层数")
	_check(GameState.chosen_routes.get(&"offense") == &"spread_shot", "存档恢复路线选择")
	_check(GameState.is_buff_locked(&"laser_beam"), "存档恢复锁定 buff")
	_check(GameState.mission_progress(&"survive_180") >= 180, "存档恢复存活进度")

	# 9. reset_run 清零新状态
	GameState.reset_run()
	_check(GameState.rp == 0, "reset_run 清零 RP")
	_check(GameState.mission_progress(&"boss_1") == 0, "reset_run 清零任务进度")
	_check(not GameState.is_mission_claimed(&"boss_1"), "reset_run 清零领取标记")
	_check(GameState.chosen_routes.is_empty() and GameState.locked_routes.is_empty(), "reset_run 清零路线")
	_check(not GameState.is_buff_locked(&"laser_beam"), "reset_run 解除锁定")

	# 9b. A 审计：SaveManager 原子写——save 后正本存在、数据正确、重复 save（覆盖）不丢
	var sm = SM.new()
	var test_path := "user://audit_save_test.json"
	sm.delete(test_path)
	_check(sm.save(test_path, {"version": 2, "score": 500}), "A审计：save 成功")
	_check(sm.exists(test_path), "A审计：save 后正本存在（rename 成功，非孤立 tmp）")
	var loaded := sm.load(test_path)
	_check(int(loaded.get("score", -1)) == 500, "A审计：save/load 数据正确（500）")
	# 覆盖写（原实现先删正本再 rename 致 rename 失败丢数据；修复后原子覆盖）
	_check(sm.save(test_path, {"version": 2, "score": 999}), "A审计：覆盖 save 成功")
	loaded = sm.load(test_path)
	_check(int(loaded.get("score", -1)) == 999, "A审计：覆盖后数据正确（999）")
	# 损坏隔离不影响正本
	sm.delete(test_path)

	# 10. 本地高分榜（P0-3）：排序 / 同分排后 / 上限截断 / 持久化往返
	GameState.highscores.clear()
	GameState.save_profile()
	_check(GameState.submit_highscore(0) == 0, "高分榜：0 分不入榜")
	_check(GameState.submit_highscore(100) == 1, "高分榜：首条排第 1")
	_check(GameState.submit_highscore(50) == 2, "高分榜：低分排第 2")
	_check(GameState.submit_highscore(80) == 2, "高分榜：中间分插入第 2")
	_check(GameState.submit_highscore(100) == 2, "高分榜：同分新条目排后")
	_check(GameState.highscores.size() == 4, "高分榜：条目数正确")
	_check(int(GameState.highscores[0]["score"]) == 100, "高分榜：榜首为最高分")
	_check(int(GameState.highscores[1]["score"]) == 100, "高分榜：同分按先到先得排前")
	_check(GameState.highscores_text(3) == "1. 100\n2. 100\n3. 80", "高分榜：榜单文本 Top3")
	for i in range(100):
		GameState.submit_highscore(200 - i)
	_check(GameState.highscores.size() == GameState.HIGHSCORE_LIMIT, "高分榜：上限截断")
	_check(GameState.submit_highscore(1) == 0, "高分榜：超出上限的分数不入榜")
	var first_score := int(GameState.highscores[0]["score"])
	_check(first_score == 200, "高分榜：截断后榜首不变")
	GameState.save_profile()
	GameState.load_profile()
	_check(GameState.highscores.size() == GameState.HIGHSCORE_LIMIT, "高分榜：持久化往返条目数一致")
	_check(int(GameState.highscores[0]["score"]) == first_score, "高分榜：持久化往返榜首一致")
	GameState.highscores.clear()
	GameState.save_profile()

	# 11. 手柄默认绑定（P0-1 竞品调研）：运行时装配 + 右摇杆四向动作（H01 修正）
	_check(
		(
			InputMap.has_action(&"aim_left")
			and InputMap.has_action(&"aim_right")
			and InputMap.has_action(&"aim_up")
			and InputMap.has_action(&"aim_down")
		),
		"H01：右摇杆四向瞄准动作已注册",
	)
	var aim_events := InputMap.action_get_events(&"aim_right")
	var has_aim_axis := false
	for ev in aim_events:
		if ev is InputEventJoypadMotion and ev.axis == 2 and ev.axis_value == 1.0:
			has_aim_axis = true
	_check(has_aim_axis, "H01：右摇杆动作含正确轴事件（axis 2/+1）")
	var has_move_joy := false
	for ev in InputMap.action_get_events(&"move_up"):
		if ev is InputEventJoypadMotion:
			has_move_joy = true
	_check(has_move_joy, "P0-1：移动动作含手柄摇杆绑定")
	var has_dash_joy := false
	for ev in InputMap.action_get_events(&"dash"):
		if ev is InputEventJoypadButton:
			has_dash_joy = true
	_check(has_dash_joy, "P0-1：动作键含手柄按钮绑定")

	# H02（健壮性审核）：改键只擦除键盘事件，手柄事件保留
	GameState.rebind_action(&"dash", KEY_M)
	var dash_events_after := InputMap.action_get_events(&"dash")
	var dash_joy_kept := false
	for ev in dash_events_after:
		if ev is InputEventJoypadButton:
			dash_joy_kept = true
	_check(dash_joy_kept, "H02：改键后手柄事件保留")
	GameState.reset_key_bindings()
	var dash_events_reset := InputMap.action_get_events(&"dash")
	var dash_joy_reset := false
	for ev in dash_events_reset:
		if ev is InputEventJoypadButton:
			dash_joy_reset = true
	_check(dash_joy_reset, "H02：重置键位后手柄事件保留")

	# 12. 手柄设置（P0-1 设置页）：默认值 / setter 应用死区 / 持久化往返
	_check(GameState.joy_deadzone == 0.5 and GameState.joy_aim_speed >= 200.0, "P0-1：手柄设置默认值（死区 0.5 / 灵敏度≥200）")
	GameState.set_joy_deadzone(0.7)
	_check(is_equal_approx(InputMap.action_get_deadzone(&"move_up"), 0.7), "P0-1：死区 setter 应用至 InputMap")
	GameState.set_joy_aim_speed(1800.0)
	GameState.save_profile()
	GameState.load_profile()
	_check(GameState.joy_aim_speed == 1800.0, "P0-1：瞄准灵敏度持久化往返")
	_check(GameState.joy_deadzone == 0.7, "P0-1：死区持久化往返")
	GameState.set_joy_deadzone(0.5)
	GameState.set_joy_aim_speed(GameState.cfg("player.aim_assist.joy_speed", 1400.0))
	GameState.save_profile()  # K06：setter 不再自动写盘，收尾恢复默认值须显式落盘（否则 profile 留存 0.7/1800 污染后续场景）

	# 13. PS 布局适配（P0-1 延伸）：GUID 判定纯函数 + 按钮标签映射（默认 Xbox / 切 PS）
	_check(GameState.is_ps_guid("030000004c050000c405000000010000"), "P0-1：Sony GUID 判定（vendor 054c）")
	_check(not GameState.is_ps_guid("030000005e0400008e02000000010000"), "P0-1：非 Sony GUID 不误判")
	_check(GameState.joy_button_label(0) == "A" and GameState.joy_button_label(5) == "RB", "P0-1：Xbox 布局标签映射")
	var saved_layout = GameState.joy_layout
	GameState.joy_layout = &"ps"
	_check(GameState.joy_button_label(0) == "✕" and GameState.joy_button_label(4) == "L1", "P0-1：PS 布局标签映射（✕/L1）")
	GameState.joy_layout = saved_layout

	print("BASE SYSTEM TEST DONE, failures = ", _failures)
	GameState.delete_save()
	# M7：还原原始 profile（最高分/高分榜/设置项），防本地数据被清零
	_restore_profile()
	# 还原用户自定义键位（H02 改键段已把测试键位落盘）
	_restore_keys()
	load("res://csharp/godot/TestExit.cs").Quit(_failures)
