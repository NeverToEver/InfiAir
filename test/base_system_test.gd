extends Node
## 基地数据层测试：RP 经济、三常驻任务、天赋路线互斥、存档往返。
## 只操作 GameState autoload，不加载 main 场景。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _ready() -> void:
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
	var saved_rp := GameState.rp
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

	print("BASE SYSTEM TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
