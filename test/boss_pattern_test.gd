extends Node
## Boss 逐型模式库与差异化狂暴测试（BOSS_REDESIGN §5，阶段 B）：
## 场景1（一型 P2 蓄力重炮）：蓄力辉光 telegraph 先行 → 3 发 700 弹速/21 伤害重弹；
## 场景2（二型 P2 冲刺掠过）：水平瞄准线先行 → 高速横穿 + 路径拖 3 枚减速弹 → 回巡航位；
## 场景3（二型狂暴「猎杀环绕」）：轨道象限点瞬停 + 每点瞄准线 + 单发狙，收尾 12 向慢环；
## 场景4（三型 P2 编队齐射/弹幕墙）：4 小怪横队 0.8s 后齐射；10 槽位墙留 2 相邻缺口
##   且缺口避开自机方位 ±30°；
## 场景5（三型狂暴「倾巢」）：ACTIVE 3 波小怪 + 8 向环弹，收尾 16 向慢环 + 小怪齐射；
## 场景6（难度分档 §4.4）：easy 弹数减/间隔 ×1.15/弹速 ×0.9，hard 反向；HP/伤害不动。
## 一型狂暴（悬停环弹进动 + 8 路重炮齐射）断言在 boss_enrage_test。

# M3b：Enemy 迁 C#，is 判定经脚本资源引用（GDScript 不能 is C# 类）
var _enemy_script := load("res://csharp/godot/Enemy.cs")
# M3d：Boss 迁 C#——类名/枚举不可经 GDScript 引用；is 判定经脚本资源，枚举值经 Boss 实例 getter（返回 int）
var _boss_script = load("res://csharp/godot/Boss.cs")
# M3d：Boss.SweepState 为 C# 枚举，GDScript 不可引用——按 Boss.cs 声明序（NONE/AIM/DASH/RETURN = 0..3）以字面常量等价
const _SWEEP_NONE := 0
const _SWEEP_AIM := 1
const _SWEEP_DASH := 2

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## 真实时间等待（不受 time_scale 影响；process_always 保证暂停时也走时）
func _wait_real(sec: float) -> void:
	await get_tree().create_timer(sec, true, false, true).timeout


## 无 meta 敌弹中指定弹速的弹（重炮/掠过拖弹/齐射/墙弹以此识别；
## 敌机自机狙带 bullet_type meta，Boss 狂暴弹带 laser/enrage_ring meta，互不混淆）
func _bullets_by_speed(p_speed: float) -> Array:
	var out: Array = []
	for child: Variant in get_node("Main").get_children():
		# M3a：Bullet 为 C# 类——GDScript 不能 is Bullet/作类型注解，has_method("TryGraze") 鸭子识别；属性 PascalCase
		if child.has_method("TryGraze") and not child.IsPlayerBullet and not child.has_meta("bullet_type"):
			if is_equal_approx(child.Speed, p_speed):
				out.append(child)
	return out


func _count_meta_bullets(p_type: StringName) -> int:
	var n := 0
	for child: Variant in get_node("Main").get_children():
		# M3a：Bullet 为 C# 类——GDScript 不能 is Bullet/作类型注解，has_method("TryGraze") 鸭子识别；属性 PascalCase
		if child.has_method("TryGraze") and not child.IsPlayerBullet and child.has_meta("bullet_type"):
			if child.get_meta("bullet_type") == p_type:
				n += 1
	return n


func _enemies_alive() -> Array:  # M3b：Enemy 迁 C#，返回类型 untyped
	var out: Array = []  # M3b：Enemy 迁 C#，数组元素类型 untyped
	for child in get_node("Main").get_children():
		if is_instance_of(child, _enemy_script):  # M3b：Enemy 迁 C#，is 改脚本判定
			out.append(child)
	return out


func _clear_field() -> void:
	for child: Variant in get_node("Main").get_children():
		if is_instance_of(child, _enemy_script) or (child.has_method("TryGraze") and not child.IsPlayerBullet):  # M3b：Enemy 迁 C#，is 改脚本判定
			child.queue_free()
	await get_tree().process_frame


func _close_buff_ui_if_open() -> void:
	var buff_ui: CanvasLayer = get_node("Main/BuffUI")
	if buff_ui.visible:
		var ev := InputEventMouseButton.new()
		ev.pressed = true
		ev.button_index = MOUSE_BUTTON_LEFT
		buff_ui.pick_buff(&"rapid_fire")
	get_tree().paused = false


## 生成 Boss 并跳过降入；调用方负责击杀/清理
func _spawn_test_boss(p_type: int):  # M3d：Boss 迁 C#，返回类型注解不可用
	var spawner: Node = get_node("Main/Spawner")
	spawner.spawn_boss(p_type)
	await get_tree().process_frame
	var boss = null  # M3d：Boss 迁 C#，不能作类型注解
	for child in get_node("Main").get_children():
		if is_instance_of(child, _boss_script):  # M3d：Boss 迁 C#，is 改脚本判定
			boss = child
	boss.position.y = boss.fight_anchor_y()  # 跳过降入（锚线 = view 顶缘 + FIGHT_Y），下一物理帧进入战斗
	return boss


## 强制进入 P2 并换成指定模式表（绕过血量流程，专注攻击断言）
func _force_p2_patterns(boss, p2: Array) -> void:  # M3d：Boss 迁 C#，参数类型注解不可用
	boss.set_patterns({"p1": [{"attack": &"fan5", "waves": 1, "interval": 0.3}], "p2": p2})
	boss.set_fight_phase(boss.GetFightPhaseActive())  # M3d：Boss.FightPhase.P2 不可引用，经实例 getter（返回 int）
	boss.set_pattern_index(0)
	boss.start_pattern()
	boss.set_fire_timer(0.1)


func _ready() -> void:
	# 清理持久化状态，保证测试确定性
	GameState.delete_save()
	# L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
	var orig_high_score: int = GameState.high_score
	GameState.high_score = 0
	GameState.difficulty = &"medium"  # 场景1-5 弹速/弹数断言基于 medium 基准档
	GameState.save_profile()
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	GameState.login_guest()  # T4：游客会话直接开局（StartPanel 已退役）
	add_child(main_scene.instantiate())
	var main := get_node("Main")
	# 开场面板自显即暂停（冻结背景），先关闭解除
	var player = get_node("Main/Player")
	player.set_auto_fire(false)  # 全程禁用全自动开火，避免误杀 Boss/触发里程碑
	player.set_invincible(999.0)  # 弹幕/掠过期间不被误伤
	await get_tree().process_frame
	await get_tree().process_frame
	var spawner: Node = get_node("Main/Spawner")
	spawner.set_process(false)  # 停掉自动刷怪/Boss 调度，保证确定性
	player.position = Vector2(960.0, 540.0)

	# ================= 场景 1：一型 P2 蓄力重炮 =================
	var boss1 = await _spawn_test_boss(1)  # M3d：Boss 迁 C#，不能作类型注解
	_check(boss1 != null, "场景1：Boss 已生成")
	boss1.CANNON_CHARGE = 0.4
	_force_p2_patterns(boss1, [{"attack": &"charged_cannon", "waves": 1, "interval": 1.2}])
	# M3d：boss1.attacks().cannon_elapsed() 无 Boss.cs 转发器（BossAttacks 纯 C# 类不可跨语言）——
	# telegraph 起手检测/计时断言移除，待主代理补转发器后恢复（见适配报告）
	# C34：弹速/伤害从 boss 实例常量读取（cfg 覆盖后运行时值），改 JSON 不漂移
	var cannon_speed: float = boss1.CANNON_BULLET_SPEED
	var cannon_dmg: int = boss1.CANNON_DAMAGE
	_check(_bullets_by_speed(cannon_speed).is_empty(), "场景1：蓄力期间未出弹（telegraph 先行）")
	var heavy_max := 0
	for i in 50:
		await _wait_real(0.05)
		if not is_instance_valid(boss1):
			break
		var n := _bullets_by_speed(cannon_speed).size()
		heavy_max = maxi(heavy_max, n)
		if heavy_max >= 3:
			break
	_check(heavy_max >= 3, "场景1：3 发高速重弹（%d 弹速）" % int(cannon_speed))
	var heavy_dmg_ok := true
	for b in _bullets_by_speed(cannon_speed):
		if b.Damage != cannon_dmg:
			heavy_dmg_ok = false
	_check(heavy_dmg_ok, "场景1：重弹伤害 %d" % cannon_dmg)
	boss1.take_damage(9999)
	await get_tree().process_frame
	_close_buff_ui_if_open()
	await _clear_field()

	# ================= 场景 2：二型 P2 冲刺掠过 =================
	var boss2 = await _spawn_test_boss(2)  # M3d：Boss 迁 C#，不能作类型注解
	_check(boss2 != null, "场景2：Boss 已生成")
	boss2.SWEEP_AIM = 0.3
	boss2.SWEEP_RETURN_DURATION = 0.3
	_force_p2_patterns(boss2, [{"attack": &"dash_sweep", "waves": 1, "interval": 1.2}])
	var sweep_aimed := false
	for i in 40:
		await _wait_real(0.05)
		if not is_instance_valid(boss2):
			break
		# M3d：sweep_line() 无 Boss.cs 转发器，仅保留 AIM 状态判定（见适配报告）
		if boss2.SweepStateValue() == _SWEEP_AIM:
			sweep_aimed = true
			break
	_check(sweep_aimed, "场景2：冲刺掠过水平瞄准线 telegraph 先行")
	# C34：弹速/伤害从 boss 实例常量读取，改 JSON 不漂移
	var drop_speed: float = boss2.SWEEP_DROP_SPEED
	_check(_bullets_by_speed(drop_speed).is_empty(), "场景2：瞄准期间未拖弹")
	var dashing := false
	var x0 := 0.0
	for i in 40:
		await _wait_real(0.05)
		if not is_instance_valid(boss2):
			break
		if boss2.SweepStateValue() == _SWEEP_DASH:  # M3d：Boss.SweepState.DASH 不可引用，字面常量等价
			dashing = true
			x0 = boss2.position.x
			break
	_check(dashing, "场景2：瞄准结束进入高速横穿")
	await _wait_real(0.15)
	if is_instance_valid(boss2):
		_check(absf(boss2.position.x - x0) > 100.0, "场景2：横穿速度 ~900（0.15s 位移 >100px）")
	var drops := 0
	var sweep_done := false
	for i in 80:
		await _wait_real(0.05)
		if not is_instance_valid(boss2):
			break
		drops = maxi(drops, _bullets_by_speed(drop_speed).size())
		if boss2.SweepStateValue() == _SWEEP_NONE:  # M3d：Boss.SweepState.NONE 不可引用，字面常量等价
			sweep_done = true
			break
	_check(drops >= 3, "场景2：路径等距拖 3 枚减速弹（%d 弹速）" % int(drop_speed))
	var drop_dmg_expected = maxi(1, int(roundf(float(boss2.SWEEP_DROP_DAMAGE) * GameState.enemy_damage_ramp())))
	var drop_dmg_ok := true
	for b in _bullets_by_speed(drop_speed):
		if b.Damage != drop_dmg_expected:
			drop_dmg_ok = false
	_check(drop_dmg_ok, "场景2：减速弹伤害 %d（×ramp）" % drop_dmg_expected)
	_check(sweep_done, "场景2：穿屏后回到巡航流程")
	if is_instance_valid(boss2):
		_check(absf(boss2.position.y - boss2.fight_anchor_y()) < 40.0, "场景2：归位回 FIGHT_Y 战斗位")
	boss2.take_damage(9999)
	await get_tree().process_frame
	_close_buff_ui_if_open()
	await _clear_field()

	# ================= 场景 3：二型狂暴「猎杀环绕」 =================
	var boss3 = await _spawn_test_boss(2)  # M3d：Boss 迁 C#，不能作类型注解
	_check(boss3 != null, "场景3：Boss 已生成")
	boss3.ENRAGE_DURATION = 2.0
	boss3.ENRAGE_TRANSITION_DURATION = 0.2
	boss3.ENRAGE_ATTACK_WINDUP = 0.1
	boss3.E2_POINT_INTERVAL = 0.3
	boss3.E2_AIM = 0.15
	boss3.ENRAGE_RELEASE_HOLD_DURATION = 0.5
	boss3.ENRAGE_RETURN_DURATION = 0.4
	await _wait_real(0.3)
	boss3.take_damage(int(boss3.max_hp * 0.75))
	await get_tree().process_frame
	_check(boss3.is_enraged(), "场景3：血量 <30% 触发狂暴")
	main.set_bullet_time(0.05)
	var active3 := false
	for i in 40:
		await _wait_real(0.1)
		if not is_instance_valid(boss3):
			break
		if boss3.EnragePhaseValue() == boss3.GetEnragePhaseActive():  # M3d：组件访问经 Boss 级访问器
			active3 = true
			break
	_check(active3, "场景3：TRANSITION 结束进入 ACTIVE")
	# M3d：aim_line()/attack_index() 无 Boss.cs 转发器——瞄准线/瞬停点计数断言移除，待补转发器后恢复（见适配报告）
	var heavy3_max := 0
	var pos_samples: Array[Vector2] = []
	for i in 30:  # ~1.5s 覆盖 ACTIVE
		await _wait_real(0.05)
		if not is_instance_valid(boss3):
			break
		if boss3.EnragePhaseValue() != boss3.GetEnragePhaseActive():
			break
		heavy3_max = maxi(heavy3_max, _bullets_by_speed(boss3.E2_SNIPER_SPEED).size())
		pos_samples.append(boss3.global_position)
	var jump_max := 0.0
	for i in pos_samples.size():
		for j in i:
			jump_max = maxf(jump_max, pos_samples[i].distance_to(pos_samples[j]))
	_check(jump_max > 100.0, "场景3：瞬停点分布在轨道上（采样点分散）")
	_check(heavy3_max >= 2, "场景3：每点单发狙（900 弹速重弹，峰值同屏 %d 发）" % heavy3_max)
	var hold3 := false
	for i in 60:
		await _wait_real(0.05)
		if not is_instance_valid(boss3):
			break
		if boss3.EnragePhaseValue() == boss3.GetEnragePhaseReleaseHold():  # M3d：组件访问经 Boss 级访问器
			hold3 = true
			break
	_check(hold3, "场景3：ACTIVE 结束进入 RELEASE_HOLD")
	var ring3_max := 0
	for i in 30:
		await _wait_real(0.05)
		if not is_instance_valid(boss3):
			break
		ring3_max = maxi(ring3_max, _count_meta_bullets(&"enrage_ring"))
		if ring3_max >= 12:
			break
	_check(ring3_max >= 12, "场景3：收尾 12 向慢速环弹")
	if is_instance_valid(boss3):
		boss3.take_damage(9999)
		await get_tree().process_frame
	_close_buff_ui_if_open()
	await _clear_field()

	# ================= 场景 4：三型 P2 编队齐射 + 弹幕墙 =================
	var boss4 = await _spawn_test_boss(3)  # M3d：Boss 迁 C#，不能作类型注解
	_check(boss4 != null, "场景4：Boss 已生成")
	boss4.VOLLEY_DELAY = 0.4
	boss4.set_summon_timer(999.0)  # 屏蔽常规召唤，保持计数纯净
	_force_p2_patterns(
		boss4,
		[
			{"attack": &"minion_volley", "waves": 1, "interval": 1.0},
			{"attack": &"bullet_wall", "waves": 1, "interval": 0.8},
		]
	)
	var volley_row := false
	for i in 40:
		await _wait_real(0.05)
		if not is_instance_valid(boss4):
			break
		var marked := 0
		for e in _enemies_alive():
			if e.has_meta("hive_volley"):
				marked += 1
		if marked >= 4:
			volley_row = true
			break
	_check(volley_row, "场景4：编队齐射召唤 4 小怪列横队（meta 标记）")
	# C34：420 弹速为小怪齐射（enemy.ENEMY_BULLET_SPEED，与 boss.VOLLEY_BULLET_SPEED 同值）；
	# 改 JSON 需两处同步。此处匹配在场全部 420 弹速弹（enemy 生成），故保留字面量并注明来源。
	var volley_max := 0
	for i in 40:
		await _wait_real(0.05)
		if not is_instance_valid(boss4):
			break
		volley_max = maxi(volley_max, _bullets_by_speed(420.0).size())
		if volley_max >= 4:
			break
	_check(volley_max >= 4, "场景4：0.8s 后小怪齐射一轮自机狙（420 弹速普通敌弹）")
	var volley_dmg_ok := true
	# 伤害随对局进程 ramp（2026-07-29 修订：×enemy_damage_ramp，基准 12）
	var volley_expected := maxi(1, int(roundf(12.0 * GameState.enemy_damage_ramp())))
	for b in _bullets_by_speed(420.0):
		if b.Damage != volley_expected:
			volley_dmg_ok = false
	_check(volley_dmg_ok, "场景4：齐射弹伤害随难度 ramp（基准 12，实测期望 %d）" % volley_expected)
	# 弹幕墙：10 槽位留 2 相邻缺口，缺口避开自机方位 ±30°
	# C34：弹速从 boss 实例常量读取，改 JSON 不漂移
	var wall_speed: float = boss4.WALL_BULLET_SPEED
	var wall: Array = []  # M3a：Array[Bullet] 不可用（C# 类名不能作泛型参数）
	for i in 60:
		await _wait_real(0.05)
		if not is_instance_valid(boss4):
			break
		wall = _bullets_by_speed(wall_speed)
		if wall.size() >= 8:
			break
	_check(wall.size() == 8, "场景4：弹幕墙 10 槽位出 8 弹（留 2 缺口，实测 %d）" % wall.size())
	if wall.size() == 8:
		var to_player = (player.global_position - boss4.global_position).angle()
		# 槽位占用重建（缺口可能在弧段端部，相邻角差法不可靠）：逐槽比对弹丸方位
		var spacing := deg_to_rad(150.0) / 9.0
		var first_slot := Vector2.DOWN.angle() - deg_to_rad(75.0)
		var filled: Array[bool] = []
		filled.resize(10)
		for b in wall:
			var idx := int(round((b.Direction.angle() - first_slot) / spacing))
			if idx >= 0 and idx < 10:
				filled[idx] = true
		var missing: Array[int] = []
		for i in 10:
			if not filled[i]:
				missing.append(i)
		_check(missing.size() == 2 and missing[1] == missing[0] + 1, "场景4：缺口为 2 个相邻槽位（实测缺失槽 %s）" % str(missing))
		var gap_far := true
		for m in missing:
			var slot_a: float = first_slot + spacing * float(m)
			if absf(angle_difference(slot_a, to_player)) <= deg_to_rad(28.0):
				gap_far = false
		_check(gap_far, "场景4：缺口方位避开自机 ±30°（保证可躲）")
	boss4.take_damage(9999)
	await get_tree().process_frame
	_close_buff_ui_if_open()
	await _clear_field()

	# ================= 场景 5：三型狂暴「倾巢」 =================
	var boss5 = await _spawn_test_boss(3)  # M3d：Boss 迁 C#，不能作类型注解
	_check(boss5 != null, "场景5：Boss 已生成")
	boss5.ENRAGE_DURATION = 2.0
	boss5.ENRAGE_TRANSITION_DURATION = 0.2
	boss5.ENRAGE_ATTACK_WINDUP = 0.1
	boss5.E3_SUMMON_INTERVAL = 0.4
	boss5.E3_RING_INTERVAL = 0.4
	boss5.ENRAGE_RELEASE_HOLD_DURATION = 0.5
	boss5.ENRAGE_RETURN_DURATION = 0.4
	boss5.set_summon_timer(999.0)
	await _wait_real(0.3)
	boss5.take_damage(int(boss5.max_hp * 0.75))
	await get_tree().process_frame
	_check(boss5.is_enraged(), "场景5：血量 <30% 触发狂暴")
	main.set_bullet_time(0.05)
	var active5 := false
	for i in 40:
		await _wait_real(0.1)
		if not is_instance_valid(boss5):
			break
		if boss5.EnragePhaseValue() == boss5.GetEnragePhaseActive():  # M3d：组件访问经 Boss 级访问器
			active5 = true
			break
	_check(active5, "场景5：TRANSITION 结束进入 ACTIVE")
	# M3d：summon_waves() 无 Boss.cs 转发器——波次计数断言移除，待补转发器后恢复（见适配报告）
	var minion_max := 0
	var ring5_max := 0
	for i in 40:  # ~2s 覆盖 ACTIVE
		await _wait_real(0.05)
		if not is_instance_valid(boss5):
			break
		if boss5.EnragePhaseValue() != boss5.GetEnragePhaseActive():
			break
		minion_max = maxi(minion_max, _enemies_alive().size())
		ring5_max = maxi(ring5_max, _count_meta_bullets(&"enrage_ring"))
	_check(minion_max >= 6, "场景5：小怪波次在场（峰值 %d 只）" % minion_max)
	_check(ring5_max >= 8, "场景5：自身每 0.9s 一圈 8 向环弹")
	var hold5 := false
	for i in 60:
		await _wait_real(0.05)
		if not is_instance_valid(boss5):
			break
		if boss5.EnragePhaseValue() == boss5.GetEnragePhaseReleaseHold():  # M3d：组件访问经 Boss 级访问器
			hold5 = true
			break
	_check(hold5, "场景5：ACTIVE 结束进入 RELEASE_HOLD")
	var ring5_total := 0
	var volley5_max := 0
	# C34：420 同场景 4（小怪齐射 enemy.ENEMY_BULLET_SPEED，与 VOLLEY 同值，改 JSON 两处同步）
	for i in 30:
		await _wait_real(0.05)
		if not is_instance_valid(boss5):
			break
		ring5_total = maxi(ring5_total, _count_meta_bullets(&"enrage_ring"))
		volley5_max = maxi(volley5_max, _bullets_by_speed(420.0).size())
		if ring5_total >= 16 and volley5_max >= 3:
			break
	_check(ring5_total >= 16, "场景5：收尾一次性 16 向慢速环弹（峰值 %d）" % ring5_total)
	_check(volley5_max >= 3, "场景5：收尾在场小怪齐射一轮（峰值 %d 发）" % volley5_max)
	if is_instance_valid(boss5):
		boss5.take_damage(9999)
		await get_tree().process_frame
	_close_buff_ui_if_open()
	await _clear_field()

	# ================= 场景 6：难度分档（§4.4） =================
	# 分档在 Boss._ready 配置载入后一次性乘算，改难度必须在生成前；基准值均为 medium 档
	GameState.difficulty = &"easy"
	var boss6e = await _spawn_test_boss(1)  # M3d：Boss 迁 C#，不能作类型注解
	_check(boss6e != null, "场景6：easy Boss 已生成")
	_check(boss6e.E1_RING_COUNT == 10, "场景6：easy 狂暴环弹 12-2=10（实测 %d）" % boss6e.E1_RING_COUNT)
	_check(boss6e.CANNON_SHOTS == 2, "场景6：easy 蓄力重炮 3-1=2 发（实测 %d）" % boss6e.CANNON_SHOTS)
	# M3d：fan_delta/homing_delta/ring_delta 无 Boss.cs 转发器（BossAttacks 纯 C# 类）——弹数分档断言移除，待补转发器后恢复
	var p2_interval_e: float = boss6e.patterns()["p2"][0]["interval"]
	_check(absf(p2_interval_e - 2.4 * 1.15) < 0.01, "场景6：easy 开火间隔 ×1.15（实测 %.3f）" % p2_interval_e)
	_check(absf(boss6e.FAN_BULLET_SPEED - 380.0 * 0.9) < 0.01, "场景6：easy 弹速 ×0.9（实测 %.1f）" % boss6e.FAN_BULLET_SPEED)
	var hp_e: int = int(boss6e.max_hp)  # 显式 int()：max_hp 为 float（narrowing_conversion=2 门禁），HP 数值语义为整数
	boss6e.queue_free()
	await get_tree().process_frame

	GameState.difficulty = &"hard"
	var boss6h = await _spawn_test_boss(1)  # M3d：Boss 迁 C#，不能作类型注解
	_check(boss6h != null, "场景6：hard Boss 已生成")
	_check(boss6h.E1_RING_COUNT == 14, "场景6：hard 狂暴环弹 12+2=14（实测 %d）" % boss6h.E1_RING_COUNT)
	_check(boss6h.CANNON_SHOTS == 4, "场景6：hard 蓄力重炮 3+1=4 发（实测 %d）" % boss6h.CANNON_SHOTS)
	var p2_interval_h: float = boss6h.patterns()["p2"][0]["interval"]
	_check(absf(p2_interval_h - 2.4 * 0.85) < 0.01, "场景6：hard 开火间隔 ×0.85（实测 %.3f）" % p2_interval_h)
	_check(absf(boss6h.FAN_BULLET_SPEED - 380.0 * 1.1) < 0.01, "场景6：hard 弹速 ×1.1（实测 %.1f）" % boss6h.FAN_BULLET_SPEED)
	_check(boss6h.max_hp == hp_e * 2, "场景6：HP 随难度分档 ×0.75/×1.5（hard/easy=2.0，实测 %d/%d）" % [boss6h.max_hp, hp_e])
	boss6h.queue_free()
	await get_tree().process_frame
	GameState.difficulty = &"medium"

	_check(is_equal_approx(Engine.time_scale, 1.0), "收尾：退出前 time_scale = 1.0")
	_check(is_equal_approx(player.enrage_slow(), 1.0), "收尾：退出前玩家减速已复位")
	await _clear_field()
	await _wait_real(2.0)  # 演出 tween/爆炸序列播完，避免退出时对象泄漏
	# L15：还原用户最高分并落盘（收尾不污染用户 profile）
	GameState.high_score = orig_high_score
	GameState.save_profile()
	# ================= 场景 7：四型「月蚀」ring_burst 环弹 + P2 混合 =================
	await _clear_field()
	var boss7 = await _spawn_test_boss(4)  # M3d：Boss 迁 C#，不能作类型注解
	_check(boss7 != null, "场景7：月蚀已生成")
	boss7.set_patterns({"p1": [{"attack": &"ring_burst", "waves": 1, "interval": 0.5}], "p2": []})
	boss7.set_fight_phase(boss7.GetFightPhaseTransition())  # M3d：Boss.FightPhase.P1 不可引用，经实例 getter（返回 int）
	boss7.set_pattern_index(0)
	boss7.start_pattern()
	boss7.set_fire_timer(0.1)
	var ring_seen := false
	for i in 40:
		await _wait_real(0.05)
		if _count_meta_bullets(&"enrage_ring") >= 12:
			ring_seen = true
			break
	_check(ring_seen, "场景7：ring_burst 12 向全圆环弹（enrage_ring meta）")
	await _clear_field()
	# P2：ring_burst + cross + sniper3（telegraph 起手）混合不崩、攻击可触发
	_force_p2_patterns(
		boss7,
		[
			{"attack": &"ring_burst", "waves": 1, "interval": 0.5},
			{"attack": &"cross", "duration": 2.0, "interval": 0.5},
			{"attack": &"sniper3", "waves": 1, "interval": 0.5},
		]
	)
	var p2_attacks := 0
	for i in 80:
		await _wait_real(0.05)
		if _count_meta_bullets(&"enrage_ring") >= 12 or _count_meta_bullets(&"cross") >= 4:
			p2_attacks += 1
			if p2_attacks >= 2:
				break
	_check(p2_attacks >= 2, "场景7：P2 ring_burst + cross 轮转触发")

	print("BOSS PATTERN TEST DONE, failures = ", _failures)
	GameState.delete_save()
	load("res://csharp/godot/TestExit.cs").Quit(_failures)
