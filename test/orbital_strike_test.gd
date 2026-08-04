extends Node
## 轨道打击清场动画测试：触发/_resume_from_base 接线/命中清场（Boss 保留、弹丸清除、
## 逐机爆炸）/恢复对局（解除暂停、解锁输入、播战机入场动画、spawner 延迟恢复）/动画自销毁。
## 缩短 DURATION 后用真实 Timer 等待推进时轴。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _ready() -> void:
	GameState.delete_save()
	GameState.reset_run()
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	GameState.login_guest()  # T4：游客会话直接开局（StartPanel 已退役）
	add_child(main_scene.instantiate())
	await get_tree().process_frame
	await get_tree().process_frame

	var main := get_node("Main")
	var spawner := main.get_node("Spawner")
	# 全程禁用刷怪与随机事件编排：本测试只验证轨道打击自身，排除波次/事件注册新敌人的时序干扰
	spawner.set_process(false)
	main.event().set_process(false)
	main.formation().set_process(false)

	# ---------- 1. 布置战场：3 台普通敌机 + 1 发弹丸 ----------
	for i in 3:
		var cfg: Dictionary = spawner.ENEMY_TYPES[i % spawner.ENEMY_TYPES.size()]
		var e := load("res://scenes/enemy.tscn").instantiate() as Enemy
		e.setup(cfg, (cfg["strategies"] as Array)[0], 1.0)
		e.position = Vector2(400.0 + i * 400.0, 300.0)
		main.add_child(e)
	await get_tree().process_frame
	var b: Bullet = GameState.bullet_pool.fire(Vector2.DOWN, 400.0, 10, false)
	b.position = Vector2(960.0, 700.0)
	await get_tree().process_frame
	_check(GameState.enemies.size() == 3, "布置：3 台敌机已注册")

	# ---------- 2. 模拟基地状态触发继续出击 ----------
	get_tree().paused = true
	main.set_homecoming(true)
	main.player().lock_input()
	main.player().set_invincible(999.0)  # 驻留态无敌
	spawner.set_process(false)
	main.resume_from_base()
	await get_tree().process_frame
	var strike: OrbitalStrike = main.strike()  # get_node 返回 Variant，不能用 := 推断
	_check(strike != null, "触发：轨道打击节点已创建")
	_check(get_tree().paused, "动画期间树保持暂停")
	main.resume_from_base()  # 幂等：播放中重复触发不叠加
	await get_tree().process_frame
	_check(main.strike() == strike, "幂等：重复触发不叠加第二个动画")

	# ---------- 3. 缩短时轴推进到命中 ----------
	strike.DURATION = 0.6
	var struck_fired := [false]
	strike.struck.connect(func() -> void: struck_fired[0] = true)
	var reached_impact := false
	for i in 60:
		await get_tree().create_timer(0.05).timeout
		if struck_fired[0]:
			reached_impact = true
			break
	_check(reached_impact, "时轴：struck 信号在命中帧发出")
	# queue_free 在帧尾执行：让出两帧再断言清场结果
	await get_tree().process_frame
	await get_tree().process_frame
	_check(not get_tree().paused, "命中：树恢复非暂停")
	_check(GameState.enemies.is_empty(), "命中：敌机全部清除（残留 %d 台）" % GameState.enemies.size())
	_check(not main.player().is_input_locked(), "命中：玩家输入解锁")
	_check(main.player().is_entry_playing(), "命中：播战机入场动画（替代原地无敌闪现）")
	var inv: float = main.player().invincible_remaining()
	_check(inv > 0.0 and inv <= main.player().ENTRY_INVINCIBLE, "命中：驻留无敌被入场动画接管")
	_check(not spawner.is_processing(), "命中：敌机生成延迟（入场动画期间暂停）")
	_check(not main.is_homecoming(), "命中：homecoming 标志复位")
	_check(
		not is_instance_valid(b),
		"命中：既有弹丸被清除（valid=%s parent=%s）" % [is_instance_valid(b), b.get_parent().name if is_instance_valid(b) else "-"]
	)
	# 等入场动画结束（约 1.65s），敌机生成恢复
	var t_entry := 0.0
	while main.player().is_entry_playing() and t_entry < 3.0:
		await get_tree().create_timer(0.1).timeout
		t_entry += 0.1
	_check(spawner.is_processing(), "入场动画结束：spawner 恢复")

	# ---------- 4. 动画播完自销毁 ----------
	for i in 60:
		await get_tree().create_timer(0.05).timeout
		if main.strike() == null:
			break
	await get_tree().process_frame
	_check(main.strike() == null and not is_instance_valid(strike), "收尾：动画自销毁并释放引用")

	GameState.delete_save()
	GameState.save_profile()
	print("[DONE] failures=%d" % _failures)
	get_tree().quit(1 if _failures > 0 else 0)
