extends Node
## 难度 / 里程碑 / 设置测试（迭代 3.4-A）：
## 三档难度下敌机 HP/速度缩放、分数倍率 ×1/×2/×3、spread 同屏上限 1/2/3、
## 刷怪间隔倍率、里程碑阈值曲线（8 档基础 + 循环 ×1.35 + 难度倍率）、
## 难度 profile 持久化往返、Ctrl/Shift 模式标志序列化（对局存档 + profile）。
## 不加载 main 场景；spawner 以脚本实例挂载（停 process），敌机仅采样 setup 数值。

var _failures: int = 0

const SpawnerScript: GDScript = preload("res://scripts/spawner.gd")
const ENEMY_SCENE: PackedScene = preload("res://scenes/enemy.tscn")


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## 采样 count 架敌机（不入树，只取 setup 后的 hp/speed）
func _sample_batch(config: Dictionary, count: int) -> Array[Enemy]:
	var out: Array[Enemy] = []
	for i in count:
		var e := ENEMY_SCENE.instantiate() as Enemy
		e.setup(config, &"straight", 1.0)
		out.append(e)
	return out


func _free_batch(batch: Array[Enemy]) -> void:
	for e in batch:
		e.free()


func _avg_hp(batch: Array[Enemy]) -> float:
	var total := 0.0
	for e in batch:
		total += e.hp
	return total / batch.size()


func _read_profile() -> Dictionary:
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(GameState.PROFILE_PATH))
	return parsed if parsed is Dictionary else {}


func _write_profile(data: Dictionary) -> void:
	var f := FileAccess.open(GameState.PROFILE_PATH, FileAccess.WRITE)
	f.store_string(JSON.stringify(data))
	f.close()


func _ready() -> void:
	# 确定性起点：清存档，内存状态归位（难度/模式为 profile 级，reset_run 不清）
	GameState.delete_save()
	GameState.difficulty = &"medium"
	GameState.ctrl_toggle_mode = false
	GameState.shift_toggle_mode = false

	# ---------- 1. 分数倍率 ×1/×2/×3（add_score 统一乘算） ----------
	var score_cases: Array[Array] = [[&"easy", 100], [&"medium", 200], [&"hard", 300]]
	for case in score_cases:
		GameState.reset_run()
		GameState.difficulty = case[0]
		GameState._set_milestone_override(999999999)  # 屏蔽里程碑干扰
		GameState.add_score(100)
		_check(
			GameState.score == int(case[1]),
			"难度 %s 分数倍率：+100 → %d" % [GameState.difficulty_label(), int(case[1])]
		)

	# ---------- 2. 敌机 HP/速度缩放（同 seed 对比，randf 序列对齐） ----------
	var normal_cfg: Dictionary = SpawnerScript.ENEMY_TYPES[0]  # hp 75-85, speed 140-180
	var elite_cfg: Dictionary = SpawnerScript.ELITE_TYPES[0]  # hp 210-230, speed 90-110
	seed(1001)
	GameState.difficulty = &"easy"
	var easy_batch := _sample_batch(normal_cfg, 30)
	seed(1001)
	GameState.difficulty = &"medium"
	var med_batch := _sample_batch(normal_cfg, 30)
	seed(1001)
	GameState.difficulty = &"hard"
	var hard_batch := _sample_batch(normal_cfg, 30)
	# 同 seed 下同一次 randf 抽中的速度按比例缩放（精确关系）
	var speed_ratio_ok := true
	var hp_mono_ok := true
	for i in 30:
		if absf(easy_batch[i].speed / med_batch[i].speed - 0.85) > 0.001:
			speed_ratio_ok = false
		if absf(hard_batch[i].speed / med_batch[i].speed - 1.2) > 0.001:
			speed_ratio_ok = false
		if not (easy_batch[i].hp <= med_batch[i].hp and med_batch[i].hp <= hard_batch[i].hp):
			hp_mono_ok = false
	_check(speed_ratio_ok, "敌机速度按难度缩放：easy ×0.85 / hard ×1.2")
	_check(hp_mono_ok, "敌机 HP 单调：easy ≤ medium ≤ hard")
	var avg_e := _avg_hp(easy_batch)
	var avg_m := _avg_hp(med_batch)
	var avg_h := _avg_hp(hard_batch)
	_check(avg_e < avg_m and avg_m < avg_h, "敌机 HP 均值随难度递增")
	_check(absf(avg_e / avg_m - 0.75) < 0.1, "敌机 HP 均值比 ≈ ×0.75（easy）")
	_check(absf(avg_h / avg_m - 1.5) < 0.1, "敌机 HP 均值比 ≈ ×1.5（hard）")
	_free_batch(easy_batch)
	_free_batch(med_batch)
	_free_batch(hard_batch)
	# 精英大 HP 池：三档区间互不重叠（easy 158-173 / medium 210-230 / hard 315-345）
	seed(2002)
	GameState.difficulty = &"easy"
	var elite_e := _sample_batch(elite_cfg, 30)
	seed(2002)
	GameState.difficulty = &"medium"
	var elite_m := _sample_batch(elite_cfg, 30)
	seed(2002)
	GameState.difficulty = &"hard"
	var elite_h := _sample_batch(elite_cfg, 30)
	var max_e := 0
	var min_m := 999999
	var max_m := 0
	var min_h := 999999
	for i in 30:
		max_e = maxi(max_e, elite_e[i].hp)
		min_m = mini(min_m, elite_m[i].hp)
		max_m = maxi(max_m, elite_m[i].hp)
		min_h = mini(min_h, elite_h[i].hp)
	_check(max_e < min_m, "精英 HP easy 上限 < medium 下限（×0.75 生效）")
	_check(max_m < min_h, "精英 HP medium 上限 < hard 下限（×1.5 生效）")
	_free_batch(elite_e)
	_free_batch(elite_m)
	_free_batch(elite_h)

	# ---------- 3. spread 同屏上限 1/2/3 ----------
	GameState.difficulty = &"easy"
	_check(GameState.spread_enemy_cap() == 1, "spread 上限 easy=1")
	GameState.difficulty = &"medium"
	_check(GameState.spread_enemy_cap() == 2, "spread 上限 medium=2")
	GameState.difficulty = &"hard"
	_check(GameState.spread_enemy_cap() == 3, "spread 上限 hard=3")
	var spawner: Node = SpawnerScript.new()
	add_child(spawner)
	spawner.set_process(false)  # 只用其抽取/计数逻辑，不自动刷怪
	var spread_fighters: Array[Enemy] = []
	for i in 3:
		var e := ENEMY_SCENE.instantiate() as Enemy
		e.setup(normal_cfg, &"straight", 1.0)
		e.bullet_type = &"spread"
		e.can_shoot = false
		e.position = Vector2(400.0 + 400.0 * i, 300.0)
		spread_fighters.append(e)
	# easy（上限 1）：1 架在场即退化
	GameState.difficulty = &"easy"
	add_child(spread_fighters[0])
	await get_tree().process_frame
	_check(spawner._count_spread_enemies() == 1, "spread 敌机同屏计数为 1")
	var easy_degenerate := true
	for i in 20:
		if spawner._pick_bullet_type(SpawnerScript.ENEMY_TYPES[1]) != &"single":
			easy_degenerate = false
	_check(easy_degenerate, "spread 上限 1（easy）：普通机退化为 single")
	# medium（上限 2）：1 架在场未满可出 spread，2 架满则退化
	GameState.difficulty = &"medium"
	var saw_spread_m := false
	for i in 40:
		if spawner._pick_bullet_type(SpawnerScript.ENEMY_TYPES[1]) == &"spread":
			saw_spread_m = true
	_check(saw_spread_m, "spread 上限 2（medium）：1 架在场仍可出 spread")
	add_child(spread_fighters[1])
	await get_tree().process_frame
	var med_degenerate := true
	var med_elite_degenerate := true
	for i in 20:
		if spawner._pick_bullet_type(SpawnerScript.ENEMY_TYPES[1]) != &"single":
			med_degenerate = false
		if spawner._pick_bullet_type(SpawnerScript.ELITE_TYPES[2]) != &"laser":
			med_elite_degenerate = false
	_check(med_degenerate, "spread 上限 2（medium）：满 2 架普通机退化为 single")
	_check(med_elite_degenerate, "spread 上限 2（medium）：满 2 架精英退化为 laser")
	# hard（上限 3）：2 架在场仍可出 spread，3 架满则退化
	GameState.difficulty = &"hard"
	var saw_spread_h := false
	for i in 40:
		if spawner._pick_bullet_type(SpawnerScript.ENEMY_TYPES[1]) == &"spread":
			saw_spread_h = true
	_check(saw_spread_h, "spread 上限 3（hard）：2 架在场仍可出 spread")
	add_child(spread_fighters[2])
	await get_tree().process_frame
	var hard_degenerate := true
	for i in 20:
		if spawner._pick_bullet_type(SpawnerScript.ENEMY_TYPES[1]) != &"single":
			hard_degenerate = false
	_check(hard_degenerate, "spread 上限 3（hard）：满 3 架普通机退化为 single")
	for e in spread_fighters:
		if is_instance_valid(e):
			e.queue_free()
	await get_tree().process_frame

	# ---------- 4. 刷怪间隔倍率 ×1.25/×1/×0.8 ----------
	spawner._elapsed = 0.0
	GameState.difficulty = &"easy"
	var iv_easy: float = spawner._current_interval()
	GameState.difficulty = &"medium"
	var iv_medium: float = spawner._current_interval()
	GameState.difficulty = &"hard"
	var iv_hard: float = spawner._current_interval()
	_check(absf(iv_easy - 8.75) < 0.01, "波次间隔 easy ×1.25（7.0s → 8.75s）")
	_check(absf(iv_medium - 7.0) < 0.01, "波次间隔 medium ×1（7.0s 不变）")
	_check(absf(iv_hard - 5.6) < 0.01, "波次间隔 hard ×0.8（7.0s → 5.6s）")
	_check(iv_easy > iv_medium and iv_medium > iv_hard, "波次间隔随难度递减（越难越密）")

	# ---------- 5. 里程碑阈值曲线 ----------
	GameState.difficulty = &"medium"
	var base_thresholds: Array[int] = [3000, 8000, 15000, 25000, 40000, 55000, 70000, 80000]
	var first_cycle_ok := true
	for i in base_thresholds.size():
		if GameState.milestone_threshold(i) != base_thresholds[i]:
			first_cycle_ok = false
	_check(first_cycle_ok, "里程碑首循环 8 档：3000→80000")
	_check(GameState.milestone_threshold(8) == 84050, "循环增长：第 9 档 80000+3000×1.35")
	_check(GameState.milestone_threshold(9) == 90800, "循环增长：第 10 档 +5000×1.35")
	_check(GameState.milestone_threshold(15) == 188000, "循环增长：第二循环末 80000+80000×1.35")
	_check(GameState.milestone_threshold(16) > GameState.milestone_threshold(15), "循环阈值单调不回退")
	GameState.difficulty = &"easy"
	_check(GameState.milestone_threshold(0) == 3000, "阈值难度倍率 easy ×1（首档 3000）")
	_check(GameState.milestone_threshold(7) == 80000, "阈值难度倍率 easy ×1（末档 80000）")
	GameState.difficulty = &"hard"
	_check(GameState.milestone_threshold(0) == 4500, "阈值难度倍率 hard ×1.5（首档 4500）")
	_check(GameState.milestone_threshold(7) == 120000, "阈值难度倍率 hard ×1.5（末档 120000）")
	_check(GameState.milestone_threshold(8) == 126075, "阈值难度倍率 hard ×1.5（循环档同步缩放）")

	# _next_milestone 机制：reset 后从首档开始，触发后沿曲线推进
	GameState.difficulty = &"medium"
	GameState.reset_run()
	_check(GameState._next_milestone == 3000, "reset_run 后下一里程碑为 3000")
	var fired := [0]
	GameState.milestone_reached.connect(func(_s: int) -> void: fired[0] += 1)
	GameState.add_score(1500)  # ×2 = 3000，触发第 1 档
	_check(fired[0] == 1 and GameState._next_milestone == 8000, "到达 3000 触发里程碑并推进 8000")
	GameState.add_score(2500)  # ×2 = 5000，累计 8000
	_check(fired[0] == 2 and GameState._next_milestone == 15000, "到达 8000 触发里程碑并推进 15000")
	GameState._set_milestone_override(100)
	GameState.add_score(50)  # ×2 = 100，触发 override
	_check(fired[0] == 3 and GameState._next_milestone == 25000, "override 阈值触发后回到曲线档位")
	GameState.difficulty = &"hard"
	GameState.reset_run()
	_check(GameState._next_milestone == 4500, "hard 档 reset_run 后下一里程碑为 4500")

	# 存档恢复：按分数定位曲线档位
	GameState.difficulty = &"medium"
	GameState.reset_run()
	GameState._set_milestone_override(999999999)
	GameState.add_score(5000)  # ×2 = 10000，处于 8000~15000 之间
	GameState.save_run(50.0, 1.0)
	GameState.apply_run_save(GameState.load_run_data())
	_check(GameState._next_milestone == 15000, "存档恢复后里程碑定位到 15000")

	# ---------- 6. 难度持久化（profile 往返 + 旧档兼容） ----------
	GameState.set_difficulty(&"hard")
	_check(StringName(_read_profile().get("difficulty", "")) == &"hard", "难度写入 profile")
	GameState.difficulty = &"easy"  # 篡改内存（不经 setter，避免写盘）
	GameState.load_profile()
	_check(GameState.difficulty == &"hard", "难度从 profile 恢复")
	# 旧档案无 difficulty 字段：不覆盖当前值、不报错
	_write_profile({"version": 1, "high_score": 0})
	GameState.difficulty = &"easy"
	GameState.load_profile()
	_check(GameState.difficulty == &"easy", "旧档（无 difficulty 字段）读取保留当前难度")
	# 非法档位：忽略并保持当前值
	_write_profile({"version": 1, "high_score": 0, "difficulty": "nightmare"})
	GameState.load_profile()
	_check(GameState.difficulty == &"easy", "非法难度值被忽略")
	GameState.set_difficulty(&"medium")
	_check(GameState.difficulty == &"medium", "难度恢复 medium")

	# ---------- 7. Ctrl/Shift 模式标志序列化 ----------
	_check(not GameState.ctrl_toggle_mode and not GameState.shift_toggle_mode, "设置模式默认均为按住")
	# 对局存档往返
	GameState.reset_run()
	GameState.ctrl_toggle_mode = true
	GameState.shift_toggle_mode = true
	GameState.save_run(50.0, 1.0)
	GameState.ctrl_toggle_mode = false
	GameState.shift_toggle_mode = false
	GameState.apply_run_save(GameState.load_run_data())
	_check(GameState.ctrl_toggle_mode, "Ctrl 切换模式随对局存档往返")
	_check(GameState.shift_toggle_mode, "Shift 切换模式随对局存档往返")
	# 旧存档无字段：保持当前值不报错
	GameState.apply_run_save({})
	_check(GameState.ctrl_toggle_mode and GameState.shift_toggle_mode, "旧存档（无模式字段）恢复保持当前值")
	# profile 往返
	GameState.set_ctrl_toggle_mode(true)
	GameState.set_shift_toggle_mode(true)
	var profile := _read_profile()
	_check(
		bool(profile.get("ctrl_toggle_mode", false)) and bool(profile.get("shift_toggle_mode", false)),
		"设置模式写入 profile"
	)
	GameState.ctrl_toggle_mode = false
	GameState.shift_toggle_mode = false
	GameState.load_profile()
	_check(GameState.ctrl_toggle_mode and GameState.shift_toggle_mode, "设置模式从 profile 恢复")
	# reset_run 不清难度与设置模式（profile 级偏好）
	GameState.difficulty = &"hard"
	GameState.reset_run()
	_check(
		GameState.difficulty == &"hard" and GameState.ctrl_toggle_mode,
		"reset_run 保留难度与设置模式"
	)

	# ---------- 8. Boss 触发最小间隔（BOSS_MIN_INTERVAL，防分数暴涨期连出 Boss） ----------
	GameState.difficulty = &"medium"
	GameState.reset_run()
	GameState._set_milestone_override(999999999)
	spawner._boss_active = false
	spawner._boss_frozen = false
	spawner._boss_pending = false
	spawner._next_boss_score = spawner.BOSS_SCORE_STEP
	GameState.score = spawner.BOSS_SCORE_STEP  # 分数已跨步进（直接赋值，避开倍率/里程碑）
	spawner._boss_timer = 10.0  # 距上次 Boss 仅 10s（模拟 Boss 刚死、分数立刻跨步进）
	spawner.set_process(true)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(not spawner._boss_active, "Boss 最小间隔：分数跨步进但间隔 <80s 不触发")
	spawner._boss_timer = spawner.BOSS_MIN_INTERVAL + 1.0
	await get_tree().process_frame
	_check(spawner._boss_active, "Boss 最小间隔：越过 80s 后分数触发生效")
	# 清理：停主循环并撤掉已排程的 Boss 降入 Timer（不真正生成 Boss）
	spawner.set_process(false)
	for c in spawner.get_children():
		if c is Timer:
			c.queue_free()
	spawner._boss_active = false
	spawner._boss_timer = 0.0
	GameState.score = 0

	print("DIFFICULTY TEST DONE, failures = ", _failures)
	# 清理：恢复默认并落盘，避免污染其他测试进程
	GameState.difficulty = &"medium"
	GameState.ctrl_toggle_mode = false
	GameState.shift_toggle_mode = false
	GameState.reset_run()
	GameState.save_profile()
	GameState.delete_save()
	get_tree().quit(_failures)
