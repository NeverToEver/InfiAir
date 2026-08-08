extends Node
## Buff 外观反馈测试：PlayerBuffVisuals 附件随 buffs_changed 信号显隐/强度刷新，
## 覆盖 16 种 buff 的外观映射、层数强度、天赋路线合并与重开清空。

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
	# L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
	var orig_high_score: int = GameState.high_score
	GameState.high_score = 0
	GameState.save_profile()
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	GameState.login_guest()  # T4：游客会话直接开局（StartPanel 已退役）
	add_child(main_scene.instantiate())
	var main := get_node("Main")
	# 开场面板自显即暂停（冻结背景），先关闭解除
	var player: Player = get_node("Main/Player")
	var spawner: Node = get_node("Main/Spawner")
	# 关闭自动开火与刷怪，避免对局逻辑干扰外观断言
	player.set_auto_fire(false)
	player.set_invincible(999.0)
	spawner.set_process(false)
	await get_tree().process_frame
	await get_tree().process_frame
	for child in main.get_children():
		if child is Enemy or child.has_method("IsActive"):
			child.queue_free()
	await get_tree().process_frame

	# 0. 初始状态：外观节点存在且全部附件隐藏、尾焰染色为白
	var visuals: PlayerBuffVisuals = null
	for child in player.get_children():
		if child is PlayerBuffVisuals:
			visuals = child
	_check(visuals != null, "玩家挂有 PlayerBuffVisuals 外观节点")
	if visuals == null:
		# L15：还原用户最高分并落盘（收尾不污染用户 profile；本文件有两条退出路径，均还原）
		GameState.high_score = orig_high_score
		GameState.save_profile()
		print("BUFF VISUALS TEST DONE, failures = ", _failures)
		GameState.delete_save()
		get_tree().quit(_failures)
		return
	_check(
		(
			not visuals.power_glow().visible
			and not visuals.shield_hex().visible
			and not visuals.regen_ring().visible
			and not visuals.beacon().visible
		),
		"初始无 buff 全部附件隐藏"
	)
	_check(visuals.spread_pods().size() == 3 and not visuals.spread_pods()[0].visible, "散射炮舱初始隐藏")
	_check(player.engine_tint == Color(1.0, 1.0, 1.0), "初始尾焰染色为白")

	# 1. 火力类外观：逐 buff 显隐 + 层数强度
	GameState.add_buff(&"power_shot")
	_check(visuals.power_glow().visible, "power_shot 机头金色辉光可见")
	var glow_scale_1: float = visuals.power_glow().scale.x
	for i in 4:
		GameState.add_buff(&"power_shot")
	_check(visuals.power_glow().scale.x > glow_scale_1, "power_shot 层数提升辉光强度")
	GameState.add_buff(&"rapid_fire")
	_check(visuals.rapid_fins().visible, "rapid_fire 散热鳍可见")
	GameState.add_buff(&"spread_shot")
	_check(visuals.spread_pods()[0].visible and not visuals.spread_pods()[1].visible, "spread_shot 1 层 1 个炮舱")
	GameState.add_buff(&"spread_shot")
	GameState.add_buff(&"spread_shot")
	_check(visuals.spread_pods()[2].visible, "spread_shot 3 层 3 个炮舱")
	GameState.add_buff(&"piercing")
	_check(visuals.pierce_spike().visible, "piercing 穿甲尖刺可见")
	GameState.add_buff(&"explosive")
	_check(visuals.explosive_glow().visible, "explosive 机腹辉光可见")
	GameState.add_buff(&"laser_beam")
	_check(visuals.laser_pod().visible, "laser_beam 发射基座可见")

	# 2. 生存类外观
	GameState.add_buff(&"extra_life")
	_check(visuals.armor_ring().visible, "extra_life 装甲环可见")
	var ring_width_1: float = visuals.armor_ring().width
	GameState.add_buff(&"extra_life")
	_check(visuals.armor_ring().width > ring_width_1, "extra_life 层数加粗装甲环")
	GameState.add_buff(&"regen")
	_check(visuals.regen_ring().visible, "regen 呼吸光环可见")
	GameState.add_buff(&"lifesteal")
	_check(visuals.lifesteal_tips().visible, "lifesteal 翼尖可见")
	GameState.add_buff(&"armor")
	_check(visuals.shield_hex().visible, "armor 护盾弧可见")
	GameState.add_buff(&"evasion")
	_check(visuals.evasion_ghost().visible, "evasion 残像覆盖层可见")

	# 3. 机动/系统类外观
	GameState.add_buff(&"phase_dash")
	_check(visuals.dash_fins().visible, "phase_dash 相位鳍可见")
	GameState.add_buff(&"slow_field")
	_check(visuals.slow_ring().visible, "slow_field 力场环可见")
	GameState.add_buff(&"mothership_recall")
	_check(visuals.beacon().visible, "mothership_recall 信标可见")

	# 4. 尾焰染色：高效推进偏绿、叠加燃料再生偏金
	GameState.add_buff(&"efficient_boost")
	_check(player.engine_tint.g > player.engine_tint.r, "efficient_boost 尾焰偏绿")
	var tint_r_eff: float = player.engine_tint.r
	GameState.add_buff(&"boost_recovery")
	_check(player.engine_tint.r > tint_r_eff and player.engine_tint.b < player.engine_tint.g, "叠加 boost_recovery 尾焰转金")

	# 5. 天赋路线合并：spread(3)+laser(1) 合入 laser_beam，散射炮舱隐藏、基座保留
	GameState.choose_route(&"offense", &"laser_beam")
	_check(GameState.buff_count(&"laser_beam") == 4, "路线合并层数叠加")
	_check(not visuals.spread_pods()[0].visible, "路线锁定后散射炮舱隐藏")
	_check(visuals.laser_pod().visible, "路线所选 buff 外观保留")

	# 6. 重开清空：reset_run 发 buffs_changed，全部附件隐藏、染色复位
	GameState.reset_run()
	_check(
		(
			not visuals.power_glow().visible
			and not visuals.shield_hex().visible
			and not visuals.slow_ring().visible
			and not visuals.laser_pod().visible
		),
		"重开后全部附件隐藏"
	)
	_check(player.engine_tint == Color(1.0, 1.0, 1.0), "重开后尾焰染色复位")

	# 7. 存档恢复：apply_run_save 恢复 buffs 后外观同步
	GameState.apply_run_save({"version": 2, "buffs": {"armor": 1, "spread_shot": 2}})
	_check(visuals.shield_hex().visible, "存档恢复 armor 护盾弧可见")
	_check(
		visuals.spread_pods()[0].visible and visuals.spread_pods()[1].visible and not visuals.spread_pods()[2].visible,
		"存档恢复 spread_shot 2 层 2 个炮舱"
	)

	# L15：还原用户最高分并落盘（收尾不污染用户 profile；本文件有两条退出路径，均还原）
	GameState.high_score = orig_high_score
	GameState.save_profile()
	print("BUFF VISUALS TEST DONE, failures = ", _failures)
	GameState.reset_run()
	GameState.delete_save()
	get_tree().quit(_failures)
