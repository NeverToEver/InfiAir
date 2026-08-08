extends Node
## 基地任务轮换测试（2026-08-05，docs/FOG_EVENTS.md §1）：
## 初始手牌 / TaskPool 无放回抽取算法 / RefreshPoints 经济与点数校验 /
## 刷新重抽（排除在场 id、保留已完成未领取）/ 按 kind 分发进度 / 存档往返 / reset_run 复位。
## 只操作 GameState autoload 与 TaskPool，不加载 main 场景。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _active_ids() -> Array[StringName]:
	return GameState.active_mission_ids()


func _ids_in_pool(ids: Array[StringName]) -> bool:
	for id in ids:
		var found := false
		for def in GameState.MISSION_POOL:
			if def["id"] == id:
				found = true
				break
		if not found:
			return false
	return true


func _ready() -> void:
	GameState.delete_save()
	GameState.reset_run()

	# 1. 初始手牌：固定三任务（保持既有 id 语义）
	_check(GameState.refresh_points == 0, "初始刷新点数为 0")
	_check(_active_ids().size() == 3, "初始在场任务数为 3")
	_check(
		GameState.missions.has(&"kill_5") and GameState.missions.has(&"survive_180") and GameState.missions.has(&"boss_1"),
		"初始手牌为 kill_5/survive_180/boss_1"
	)
	_check(GameState.REFRESH_COST == 2 and GameState.GRANT_PER_VISIT == 1, "刷新经济档位：2 点/次刷新，1 点/次进基地（balance.json）")

	# 2. 点数校验：余额不足禁止刷新且不扣减
	_check(not GameState.can_refresh_missions(), "初始点数不足，刷新资格为 false")
	_check(not GameState.refresh_missions(), "点数不足 refresh_missions 返回 false")
	_check(GameState.refresh_points == 0, "点数不足刷新不扣减")
	GameState.grant_refresh_points()
	_check(GameState.refresh_points == 1, "进基地发放 1 刷新点")
	_check(not GameState.can_refresh_missions(), "1 点仍不足（成本 2 点）")
	GameState.grant_refresh_points()
	_check(GameState.refresh_points == 2, "两次进基地累计 2 点")
	_check(GameState.can_refresh_missions(), "点数足够，刷新资格为 true")

	# 3. 刷新重抽：消耗点数、槽位数保持 3、id 互异且来自任务池、排除在场 id
	var before: Array[StringName] = _active_ids()
	_check(GameState.refresh_missions(), "点数足够刷新成功")
	_check(GameState.refresh_points == 0, "刷新消耗 2 点")
	var after: Array[StringName] = _active_ids()
	_check(after.size() == 3, "刷新后在场任务数仍为 3")
	_check(_ids_in_pool(after), "刷新后任务全部来自任务池")
	var seen: Dictionary = {}
	var distinct := true
	for id in after:
		if seen.has(id):
			distinct = false
		seen[id] = true
	_check(distinct, "刷新后任务 id 互不重复")
	var all_replaced := true
	for id in before:
		if after.has(id):
			all_replaced = false
	_check(all_replaced, "刷新排除在场 id（无重复任务）")

	# 4. 按 kind 分发进度：kill/boss 类任务进度 = 击杀数/Boss 击杀数（轮换后 id 变化仍推进）
	# 构造确定性在场集合（绕过随机刷新），验证进度按 kind 分发、goal 各自生效
	GameState.reset_missions()
	GameState.missions = {
		&"kill_15": {"progress": 0, "claimed": false, "goal": 15},
		&"survive_60": {"progress": 0, "claimed": false, "goal": 60},
		&"boss_2": {"progress": 0, "claimed": false, "goal": 2},
	}
	GameState.add_kill()
	GameState.add_kill()
	GameState.add_kill()
	_check(GameState.mission_progress(&"kill_15") == 3, "kill 类任务进度 = 击杀数（3/15）")
	_check(not GameState.is_mission_done(&"kill_15"), "goal=15 的 kill 任务 3 杀未完成")
	GameState.add_kill()
	GameState.add_kill()
	_check(GameState.mission_progress(&"kill_15") == 5, "kill 类任务进度随击杀推进（5/15）")
	GameState.run_time = 61.0
	await get_tree().create_timer(0.2, true, false, true).timeout  # 真实时间等 GameState._process 推进整秒边界
	_check(GameState.is_mission_done(&"survive_60"), "survive 任务按存活秒推进（61s ≥ goal 60）")
	GameState.add_boss_kill()
	_check(GameState.mission_progress(&"boss_2") == 1, "boss 类任务进度 = Boss 击杀数（1/2）")
	_check(not GameState.is_mission_done(&"boss_2"), "goal=2 的 boss 任务 1 杀未完成")
	GameState.add_boss_kill()
	_check(GameState.is_mission_done(&"boss_2"), "goal=2 的 boss 任务 2 杀完成")
	_check(
		(
			GameState.mission_goal(&"kill_15") == 15
			and GameState.mission_goal(&"survive_300") == 300
			and GameState.mission_goal(&"boss_3") == 3
		),
		"任务池新 id 的 goal 正确"
	)

	# 5. 保留已完成未领取任务：刷新不吞待领奖励
	GameState.reset_run()  # 清零击杀计数（第 4 节累计了 5 杀 2 Boss，避免进度断言串扰）
	GameState.add_kill()  # 5 次击杀（reset 后从 0 起）
	for i in 4:
		GameState.add_kill()
	_check(GameState.is_mission_done(&"kill_5") and not GameState.is_mission_claimed(&"kill_5"), "kill_5 已完成未领取")
	GameState.grant_refresh_points()
	GameState.grant_refresh_points()
	_check(GameState.refresh_missions(), "刷新成功")
	_check(GameState.missions.has(&"kill_5"), "已完成未领取任务保留")
	_check(GameState.mission_progress(&"kill_5") == 5 and not GameState.is_mission_claimed(&"kill_5"), "保留任务进度与领取标记不变")
	var other_count := 0
	for id in _active_ids():
		if id != &"kill_5":
			other_count += 1
	_check(other_count == 2, "其余两个槽位被重抽（槽位总数仍为 3）")

	# 6. 存档往返：refresh_points 与轮换后的任务集合保留
	GameState.save_run(50.0, GameState.run_time)
	var saved_ids: Array[StringName] = _active_ids()
	var saved_points := GameState.refresh_points
	GameState.refresh_points = 0
	GameState.reset_missions()
	GameState.apply_run_save(GameState.load_run_data())
	_check(GameState.refresh_points == saved_points, "存档恢复刷新点数")
	var restored_ids: Array[StringName] = _active_ids()
	var restored_ok := restored_ids.size() == saved_ids.size()
	for id in saved_ids:
		if not restored_ids.has(id):
			restored_ok = false
	_check(restored_ok, "存档恢复轮换后的任务集合")
	_check(GameState.mission_progress(&"kill_5") == 5, "存档恢复轮换任务进度")
	# 恢复后 kind 进度仍可推进（轮换 id 不依赖初始手牌）
	var boss_id: StringName = &""
	for id in restored_ids:
		for def in GameState.MISSION_POOL:
			if def["id"] == id and def["kind"] == &"boss":
				boss_id = id
	if boss_id != &"":
		GameState.add_boss_kill()
		_check(GameState.mission_progress(boss_id) == 1, "恢复后 boss 类任务进度按 kind 推进（%s）" % boss_id)

	# 7. reset_run 复位：刷新点数清零、任务回初始手牌
	GameState.reset_run()
	_check(GameState.refresh_points == 0, "reset_run 清零刷新点数")
	_check(_active_ids().size() == 3, "reset_run 任务槽位数复位")
	_check(
		GameState.missions.has(&"kill_5") and GameState.missions.has(&"survive_180") and GameState.missions.has(&"boss_1"),
		"reset_run 回初始手牌"
	)

	# 8. TaskPool 算法单元测试：无放回（单批内不重复、跨批不连续重复）+ 排除项
	var pool := TaskPool.new(GameState.MISSION_POOL)
	var batch1 := pool.draw(9, [])
	_check(batch1.size() == 9, "TaskPool：池满抽取 9 项全部返回")
	var b1_seen: Dictionary = {}
	var b1_distinct := true
	for def in batch1:
		if b1_seen.has(def["id"]):
			b1_distinct = false
		b1_seen[def["id"]] = true
	_check(b1_distinct, "TaskPool：单批无放回（9 项互不重复）")
	var batch2 := pool.draw(3, [])
	_check(batch2.size() == 3, "TaskPool：耗尽后自动重洗续抽")
	var tiny_pool := TaskPool.new([{"id": &"a", "goal": 1, "kind": &"kill"}, {"id": &"b", "goal": 1, "kind": &"kill"}])
	var excl := tiny_pool.draw(2, [&"a"])
	_check(excl.size() == 1 and excl[0]["id"] == &"b", "TaskPool：排除项被跳过")
	var all_excl := tiny_pool.draw(2, [&"a", &"b"])
	_check(all_excl.is_empty(), "TaskPool：排除覆盖全池时安全返回空（不死循环）")

	# 9. Q05（2026-08-05）：批次耗尽跨批补足——固定种子 20 轮刷新槽位恒 = MISSION_SLOTS
	# （原实现「本批已产出即 break」在排除在场任务时提前耗尽，模拟 14% 刷新不足额、99.3% 对局命中）
	seed(20260805)
	var pool_q05 := TaskPool.new(GameState.MISSION_POOL)
	var in_field: Array[StringName] = []
	var q05_all_full := true
	for i in 20:
		var d := pool_q05.draw(GameState.MISSION_SLOTS, in_field)
		if d.size() != GameState.MISSION_SLOTS:
			q05_all_full = false
		in_field.clear()
		for def in d:
			in_field.append(def["id"])
	_check(q05_all_full, "Q05：20 轮刷新槽位恒 = %d（原实现不足额 1-2/3 槽）" % GameState.MISSION_SLOTS)
	seed(0)

	print("BASE TASK REFRESH TEST DONE, failures = ", _failures)
	GameState.delete_save()
	load("res://csharp/godot/TestExit.cs").Quit(_failures)
