extends Node
## 迷雾事件系统测试（2026-08-05，docs/FOG_EVENTS.md §2）：
## 管理器挂载与信号 / 单事件并发 / Duration 到期自动清除 / MinInterval 冷却门控 /
## 概率触发（try_trigger）/ 4 种事件效果与玩家侧信号联动（输入反转/子弹偏移/方向脉冲/
## 伪敌机无碰撞）/ 返航清除。
## 加载 main 场景（player 信号联动需要真实 Player 实例）。

var _failures: int = 0


## 宽容性测试用：复杂事件——内部目标达成后主动 request_end 提前结束（2 个 tick）
class _SelfEndTestEvent:
	extends FogEvent

	var ticks := 0

	func event_id() -> StringName:
		return &"_self_end_test"

	func _on_tick(_delta: float) -> void:
		ticks += 1
		if ticks >= 2:
			request_end()


## 宽容性测试用：极简事件——只实现 event_id（无任何钩子），验证最简形态走通全生命周期
class _MinimalTestEvent:
	extends FogEvent

	func event_id() -> StringName:
		return &"_minimal_test"


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## 真实时间等待（不受 time_scale 影响）
func _wait_real(sec: float) -> void:
	await get_tree().create_timer(sec, true, false, true).timeout


## 轮询等事件结束（最多 timeout 秒真实时间）
func _wait_idle(manager: FogEventManager, timeout: float = 5.0) -> bool:
	var left := timeout
	while left > 0.0:
		if manager.active_id() == &"":
			return true
		await _wait_real(0.1)
		left -= 0.1
	return manager.active_id() == &""


func _player_bullets() -> Array:
	var out: Array = []
	for child in get_node("Main").get_children():
		var b = child if is_instance_of(child, load("res://csharp/godot/Bullet.cs")) else null  # 随批次 C 重定型：C# 类不能经类名 as 转换
		if b != null and b.IsPlayerBullet:
			out.append(b)
	return out


func _ready() -> void:
	GameState.delete_save()
	GameState.reset_run()
	GameState.set_difficulty(&"medium")
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	GameState.login_guest()
	add_child(main_scene.instantiate())
	var player: Player = get_node("Main/Player")
	player.set_auto_fire(false)
	player.set_invincible(999.0)
	player.position = Vector2(960.0, 800.0)
	await get_tree().process_frame
	await get_tree().process_frame
	var spawner: Node = get_node("Main/Spawner")
	spawner.set_process(false)  # 手动驱动，保证确定性
	var manager: FogEventManager = GameState.fog_events

	# 1. 管理器挂载与对局活跃开关（测试上下文 main 非 current_scene，自动触发默认关闭；
	# 本用例需要，显式开启——真实对局由 main._ready 自动开启）
	_check(manager != null and manager.get_parent() == GameState, "初始化：FogEventManager 挂 GameState 下")
	manager.set_run_active(true)
	_check(manager.is_run_active(), "初始化：run_active 开启（测试显式）")
	_check(manager.event_ids().size() == 4, "初始化：4 种迷雾事件注册")
	# 测试期间禁用概率自动触发（全部走 force_trigger/try_trigger 显式路径，保证确定性）
	manager.TRIGGER_CHANCE = 0.0

	# 2. 单事件并发 + 强制触发 + 信号联动
	# 2a. fake_enemies：伪敌机生成，且无伤害/无碰撞（不入注册表/不入组/无碰撞体）
	_check(manager.force_trigger(&"fake_enemies"), "强制触发 fake_enemies")
	_check(manager.active_id() == &"fake_enemies", "事件进行中 active_id 正确")
	_check(not manager.force_trigger(&"mental_confusion"), "事件进行中不可触发新事件（单并发）")
	_check(manager.spawned_fakes().size() == int(GameState.cfg("fog_events.fake_enemies.count", 5)), "伪敌机生成数量 = count 档位")
	# 2026-08-06 审计 M3：出生深度（顶缘上 20~260px）不得触发 280px 出屏销毁余量——
	# 原 80px 余量使约 75% 个体首个物理帧即被销毁；等待出生销毁窗口后断言全部存活
	await _wait_real(0.2)
	var fakes_alive := true
	for fake in manager.spawned_fakes():
		if not is_instance_valid(fake) or not fake.is_inside_tree():
			fakes_alive = false
	_check(fakes_alive, "伪敌机出生后全部存活（出生深度在出屏销毁余量内）")
	var fake_clean := true
	for fake in manager.spawned_fakes():
		if not is_instance_valid(fake) or fake.is_in_group("enemy") or GameState.enemies.has(fake):
			fake_clean = false
		if fake is Area2D:
			fake_clean = false
	_check(fake_clean, "伪敌机不入 enemy 组/不进敌机注册表/无碰撞体（纯视觉幽灵）")
	# 玩家子弹穿过伪敌机：伪敌机不是 Area2D，无 overlap 结算（结构上保证）
	manager.end_active()  # 非协程：同步结束事件（伪敌机 queue_free 需下一帧生效）
	await get_tree().process_frame
	_check(manager.spawned_fakes().is_empty(), "事件结束伪敌机统一清除")
	_check(manager.cooldown_left() > 0.0, "事件结束进入 MinInterval 冷却")

	# 2b. mental_confusion：输入反转 + 变色覆盖层
	manager.set_cooldown_left(0.0)
	_check(manager.force_trigger(&"mental_confusion"), "强制触发 mental_confusion")
	_check(player.fog_invert_active(), "精神错乱：玩家输入反转标记生效")
	_check(manager.active_remaining() > 0.0, "事件有明确剩余时长（Duration 驱动）")
	manager.end_active()  # 非协程：同步结束事件（伪敌机 queue_free 需下一帧生效）
	await get_tree().process_frame
	_check(not player.fog_invert_active(), "精神错乱结束：输入反转复位")

	# 2c. bullet_malfunction：子弹偏移/射速异常参数注入 + 出膛弹轨迹偏移
	manager.set_cooldown_left(0.0)
	_check(manager.force_trigger(&"bullet_malfunction"), "强制触发 bullet_malfunction")
	_check(is_equal_approx(player.fog_bullet_jitter(), 20.0), "子弹错误：角度偏移档位 20°（balance.json）")
	_check(is_equal_approx(player.fog_misfire_chance(), 0.15), "子弹错误：失误弹概率 0.15")
	player.position = Vector2(960.0, 800.0)
	var deviated := 0
	var misfired := 0
	var shot_dir := Vector2.UP
	for i in 40:
		player.fire(shot_dir)
	await get_tree().process_frame
	for b in _player_bullets():
		if absf(angle_difference(b.Direction.angle(), shot_dir.angle())) > 0.1:
			deviated += 1
		if b.Speed < 0.8 * 1800.0:
			misfired += 1
	_check(deviated >= 1, "子弹错误：40 发出膛弹至少 1 发轨迹偏移（20° 抖动生效）")
	_check(misfired >= 1, "子弹错误：40 发出膛弹至少 1 发失误慢速弹")
	manager.end_active()  # 非协程：同步结束事件（伪敌机 queue_free 需下一帧生效）
	await get_tree().process_frame
	_check(player.fog_bullet_jitter() == 0.0 and player.fog_misfire_chance() == 0.0, "子弹错误结束：偏移/失误参数复位")

	# 2d. direction_shift：短间隔随机方向脉冲（开始即脉冲 + 周期刷新）
	manager.set_cooldown_left(0.0)
	_check(manager.force_trigger(&"direction_shift"), "强制触发 direction_shift")
	_check(player.fog_forced_hold() > 0.0, "方向偏转：事件开始立即收到脉冲（hold > 0）")
	await _wait_real(0.9)  # shift_interval=0.7s：越过一次脉冲周期
	_check(player.fog_forced_hold() > 0.0, "方向偏转：周期脉冲持续刷新 hold")
	_check(player.fog_forced_dir().length() > 0.99, "方向偏转：强制方向为单位向量")
	manager.end_active()  # 非协程：同步结束事件（伪敌机 queue_free 需下一帧生效）
	await get_tree().process_frame
	_check(player.fog_forced_hold() == 0.0, "方向偏转结束：强制方向复位")

	# 3. Duration 到期自动清除（压缩时长，不动 balance.json）
	manager.EVENT_DURATIONS[&"fake_enemies"] = 0.5
	manager.set_cooldown_left(0.0)
	manager.force_trigger(&"fake_enemies")
	_check(await _wait_idle(manager, 3.0), "Duration 到期自动结束事件")
	await get_tree().process_frame
	_check(manager.spawned_fakes().is_empty(), "到期后伪敌机自动清除")
	_check(manager.cooldown_left() > 0.0, "到期后进入冷却")

	# 4. MinInterval 冷却门控（压缩时长）
	manager.MIN_INTERVAL = 0.3
	manager.set_cooldown_left(0.3)
	manager.set_first_delay_left(0.0)
	_check(not manager.can_trigger(), "冷却期内不可触发")
	await _wait_real(0.5)
	_check(manager.cooldown_left() <= 0.0 and manager.can_trigger(), "冷却结束后恢复可触发")

	# 5. 概率触发路径（try_trigger，确定性：chance=1.0）
	manager.TRIGGER_CHANCE = 1.0
	manager.set_cooldown_left(0.0)
	_check(manager.try_trigger(), "概率触发：chance=1 时掷签必触发")
	_check(manager.active_id() != &"", "概率触发后事件进行中")
	manager.end_active()  # 非协程：同步结束事件（伪敌机 queue_free 需下一帧生效）
	# chance=0 永不触发
	manager.TRIGGER_CHANCE = 0.0
	manager.set_cooldown_left(0.0)
	_check(not manager.try_trigger(), "概率触发：chance=0 时不触发")
	_check(manager.active_id() == &"", "未触发则无进行中事件")
	manager.TRIGGER_CHANCE = float(GameState.cfg("fog_events.trigger_chance", 0.35))  # 还原档位

	# 6. 非活跃态（对局结束）自动清除 + 不再自动触发
	manager.set_cooldown_left(0.0)
	manager.force_trigger(&"mental_confusion")
	_check(player.fog_invert_active(), "触发后反转生效")
	manager.set_run_active(false)
	_check(manager.active_id() == &"", "run_active=false 立即结束进行中事件")
	_check(not player.fog_invert_active(), "非活跃结束事件：玩家效果复位")
	_check(not manager.can_trigger(), "非活跃态不可触发")

	# 7. 返航清除：进行中事件被 main 清除
	manager.set_run_active(true)
	# 2026-08-06 审计（Q12 同族遗漏）：重新激活对局时 fog 冷却必须清零——上局事件
	# 结束残留的 _fog_cooldown_left 会额外推迟新局首个迷雾事件（最晚 12s）
	manager.set_cooldown_left(5.0)
	manager.set_run_active(false)
	manager.set_run_active(true)
	_check(manager.cooldown_left() == 0.0, "重新激活对局时 fog 冷却清零")
	manager.set_first_delay_left(0.0)
	manager.set_cooldown_left(0.0)
	manager.force_trigger(&"bullet_malfunction")
	_check(player.fog_bullet_jitter() > 0.0, "返航前子弹错误参数生效")
	get_node("Main").start_homecoming()
	await get_tree().process_frame
	_check(manager.active_id() == &"", "返航清除进行中的迷雾事件")
	_check(player.fog_bullet_jitter() == 0.0, "返航后玩家效果复位")
	await _wait_real(1.4)  # 越过过场输入宽限
	get_node("Main").skip_return()
	await get_tree().process_frame
	get_node("Main").base_ui().resume()
	await get_tree().process_frame
	# 等待轨道打击命中解冻（struck → 恢复对局）——后续 §8-10 断言依赖 _process 正常运行
	for i in 40:
		if not get_tree().paused:
			break
		await _wait_real(0.1)
	_check(not get_tree().paused, "收尾：轨道打击后对局已解冻（_process 正常运行）")

	_check(is_equal_approx(Engine.time_scale, 1.0), "收尾：time_scale = 1.0")
	await _wait_real(0.5)  # 让特效 tween 播完，避免退出时对象泄漏

	# 8. 事件类健壮性（GameEvent 生命周期守卫 + context 防御路径，2026-08-05 审计）
	var ge := FakeEnemiesEvent.new()
	_check(ge.event_id() == &"fake_enemies", "事件类：event_id 正确")
	_check(not ge.is_active, "事件类：初始未活跃")
	ge.start({}, 0.5)  # 空 context（缺 fake_container 键）：降级空转不崩
	_check(ge.is_active, "事件类：start 后活跃")
	_check(ge.spawned_fakes().is_empty(), "事件类：缺容器时伪敌机零生成（防御路径）")
	var c1 := Node2D.new()
	ge.start({"fake_container": c1}, 0.5)  # 重复 start：先清理旧状态再重启（不叠加）
	_check(ge.spawned_fakes().size() == int(GameState.cfg("fog_events.fake_enemies.count", 5)), "事件类：重复 start 自愈后正常生成（无叠加）")
	var before_tick := ge.spawned_fakes().size()
	ge.tick(0.1)
	_check(ge.spawned_fakes().size() == before_tick, "事件类：tick 不重复生成（生命周期由 _on_* 钩子驱动）")
	ge.end()
	ge.end()
	_check(not ge.is_active, "事件类：end 幂等")
	ge.tick(0.1)
	_check(not ge.is_active, "事件类：未活跃 tick 不派发")
	ge.start({"fake_container": c1}, -1.0)
	_check(ge.duration == 0.0, "事件类：负 duration 钳制为 0")
	# context 浅拷贝隔离：编排器改原字典不影响已注入的事件
	var ctx_iso: Dictionary = {"fake_container": c1}
	ge.start(ctx_iso, 1.0)
	ctx_iso["fake_container"] = Node2D.new()
	_check(ge.fake_container() == c1, "事件类：context 浅拷贝隔离编排器后续修改")
	ge.end()
	c1.queue_free()

	# 9. 编排器健壮性（空注册表/非 Callable 条目防御，2026-08-05 审计）
	var saved_factories: Dictionary = manager.EVENT_FACTORIES
	manager.EVENT_FACTORIES = {}
	manager.set_cooldown_left(0.0)
	manager.set_first_delay_left(0.0)
	manager.TRIGGER_CHANCE = 1.0
	_check(not manager.try_trigger(), "编排器：空注册表 try_trigger 安全返回 false（不越界不触发）")
	_check(manager.active_id() == &"", "编排器：空注册表不触发任何事件")
	_check(not manager.force_trigger(&"fake_enemies"), "编排器：空注册表 force_trigger 拒绝未注册 id")
	manager.EVENT_FACTORIES = saved_factories
	manager.TRIGGER_CHANCE = 1.0
	_check(manager.try_trigger(), "编排器：注册表恢复后可正常触发（chance=1）")
	manager.TRIGGER_CHANCE = 0.0
	manager.end_active()
	await get_tree().process_frame

	# 10. 事件宽容性（简单/复杂事件同一接口，2026-08-05 调研后设计）
	# 10a. 复杂事件：内部目标达成可主动 request_end 提前结束（不等 duration）
	manager.EVENT_FACTORIES[&"_self_end_test"] = func() -> FogEvent: return _SelfEndTestEvent.new()
	manager.set_cooldown_left(0.0)
	manager.set_first_delay_left(0.0)
	_check(manager.force_trigger(&"_self_end_test"), "宽容性：复杂事件（request_end）注册并可触发")
	var self_end_event := manager.active_event() as _SelfEndTestEvent
	_check(self_end_event != null and self_end_event.is_active, "宽容性：复杂事件进行中")
	for i in 3:
		await get_tree().process_frame
	_check(manager.active_id() == &"", "宽容性：复杂事件 2 tick 后主动 request_end 提前结束")
	_check(manager.active_event() == null, "宽容性：结束后事件对象已清理")
	manager.EVENT_FACTORIES.erase(&"_self_end_test")

	# 10b. 极简事件：只实现 event_id 也能走通 start→duration→end 全生命周期
	manager.EVENT_FACTORIES[&"_minimal_test"] = func() -> FogEvent: return _MinimalTestEvent.new()
	manager.EVENT_DURATIONS[&"_minimal_test"] = 0.3
	manager.set_cooldown_left(0.0)
	_check(manager.force_trigger(&"_minimal_test"), "宽容性：极简事件（仅 event_id）注册并可触发")
	_check(manager.active_id() == &"_minimal_test", "宽容性：极简事件进行中")
	_check(await _wait_idle(manager, 3.0), "宽容性：极简事件按 duration 自然结束")
	manager.EVENT_FACTORIES.erase(&"_minimal_test")
	manager.EVENT_DURATIONS.erase(&"_minimal_test")

	# 10c. 宽容性辅助：get_ctx 缺键回默认 / request_end 缺回调降级（不崩、按 duration 继续）
	var ctx_ev := _MinimalTestEvent.new()
	ctx_ev.start({}, 1.0)
	_check(ctx_ev.get_ctx(&"missing", 42) == 42, "宽容性：get_ctx 缺键返回 default")
	_check(ctx_ev.get_ctx(&"missing") == null, "宽容性：get_ctx 无 default 返回 null")
	ctx_ev.request_end()  # 无 request_end 回调：push_warning 降级，不崩
	_check(ctx_ev.is_active, "宽容性：request_end 缺回调时事件继续（按 duration 结束）")
	ctx_ev.end()
	_check(manager.active_id() == &"", "宽容性：测试事件清理后编排器空闲")

	print("FOG EVENT TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
