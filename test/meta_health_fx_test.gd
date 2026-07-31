extends Node
## Meta HUD 血量/受击反馈测试（docs/META_HUD_DESIGN.md §7）：
## 1 take_damage 信号携带 amount/from_pos、无敌帧期不发射；2 hit_pulse max 池化不累积；
## 3 血量-裂纹曲线采样（pow(x,1.6)：x=0.25/0.50/0.75/0.90 → 0.11/0.33/0.63/0.84，±0.02）；
## 4 状态机下行快入/上行慢出 + 修复期 _heal_jitter 0→0.35→0 全程；
## 5 DYING 心率 [1.0,1.2]Hz、breath_scale ∈ [0.985,1.015]、减少闪光禁呼吸；
## 6 LOD1 时 hud 旧晕影回退、LOD0 移交 MetaFX；7 满血静止 60 帧早退零参数上传（D5）。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## 重置玩家受击状态并关闭被动回血（_since_damage=0 < 4s 延迟，计时窗内不回血）
func _reset_hit_state(player: Player) -> void:
	player.set_invincible(0.0)
	player.set_last_hit_frame(-1)
	player.set_since_damage(0.0)


func _ready() -> void:
	# 清理持久化状态，保证测试确定性（含 reduce_flash 默认关、跳过欢迎页暂停）
	GameState.delete_save()
	GameState.high_score = 0
	GameState.reduce_flash = false
	GameState.welcome_seen = true
	GameState.save_profile()
	add_child((load("res://scenes/main.tscn") as PackedScene).instantiate())
	var main := get_node("Main")
	var player: Player = get_node("Main/Player")
	player.set_auto_fire(false)
	await get_tree().process_frame
	await get_tree().process_frame
	get_tree().paused = false  # 开始面板/欢迎页路径可能带暂停态
	get_node("Main/Spawner").set_process(false)  # 停掉自动刷怪/Boss 调度，保证确定性
	for child in main.get_children():
		if child is Enemy or child is Bullet:
			child.queue_free()
	await get_tree().process_frame
	player.position = Vector2(960.0, 800.0)
	var fx: MetaHealthFX = main._meta_fx
	_check(fx != null, "0：main._ready 创建 MetaHealthFX")
	_check(GameState.meta_fx_lod == 0, "0：LOD0 时 GameState.meta_fx_lod 置 0（hud 移交晕影）")

	# ================= 1：player_damaged 信号 =================
	var records: Array = []
	GameState.player_damaged.connect(func(a: float, p: Vector2) -> void: records.append([a, p]))
	GameState.health = 100.0
	_reset_hit_state(player)
	player.take_damage(10.0, Vector2(400.0, 300.0))
	_check(records.size() == 1, "1：受击发射 player_damaged")
	_check(
		records.size() == 1 and records[0][0] == 10.0 and records[0][1] == Vector2(400.0, 300.0),
		"1：信号携带 amount 与 from_pos"
	)
	player.take_damage(10.0)  # 无敌帧期内
	_check(records.size() == 1, "1：无敌帧期不发射")

	# ================= 2：hit_pulse max 池化 =================
	fx._hit_pulse = 0.0  # 清掉测试 1 的 0.25 残留，隔离验证池化
	for i in 10:
		_reset_hit_state(player)
		GameState.health = 100.0
		player.take_damage(5.0)  # r=0.05 → clamp(0.125, 0.15, 1.0)=0.15
	_check(fx._hit_pulse <= 0.151, "2：连续 10 次 r=0.05 伤害 _hit_pulse 不累积（≤0.15）")
	_check(fx._hit_pulse >= 0.149, "2：max 池化取到单次峰值 0.15")

	# ================= 3：血量-裂纹映射曲线采样 =================
	fx._hit_pulse = 0.0
	var curve_cases: Array = [[0.25, 0.11], [0.50, 0.33], [0.75, 0.63], [0.90, 0.84]]
	var curve_ok := true
	for c in curve_cases:
		fx._damage_x = c[0]
		if absf(fx._crack_progress() - c[1]) > 0.02:
			curve_ok = false
	_check(curve_ok, "3：x=0.25/0.50/0.75/0.90 → crack_progress≈0.11/0.33/0.63/0.84（±0.02）")

	# ================= 4：状态机快入慢出 + 修复错峰消散 =================
	GameState.health = 100.0
	GameState.health_changed.emit(100.0)
	fx._damage_x = 0.0
	fx._state = MetaHealthFX.STATE_NORMAL
	_reset_hit_state(player)
	await get_tree().process_frame
	GameState.lose_health(90.0)  # x 目标 0.9，tau=0.10 快入
	await get_tree().create_timer(0.4).timeout
	_check(fx._damage_x > 0.8, "4：下行快入（0.4s 趋近 x=0.9）")
	_check(fx._state == MetaHealthFX.STATE_DYING, "4：跨过全部阈值进 DYING")
	GameState.heal(999.0)  # 上行慢出 tau=0.80
	var max_jitter := 0.0
	for i in 18:  # 1.8s 观测窗（覆盖末次跨阈值后的 0.7s 消散全程）
		await get_tree().create_timer(0.1).timeout
		max_jitter = maxf(max_jitter, fx._heal_jitter)
	_check(max_jitter > 0.3, "4：修复期 _heal_jitter 经历 0→0.35 峰值")
	_check(fx._heal_jitter < 0.02, "4：0.7s 全程后 _heal_jitter 回落 0")
	_check(fx._crack_progress() < 0.2, "4：上行修复后 crack_progress 回落")

	# ================= 5：DYING 临界层 =================
	GameState.health = 15.0
	GameState.health_changed.emit(15.0)  # x=0.85 → 心率 lerp(1.0,1.2,0.25)=1.05
	_reset_hit_state(player)
	await get_tree().create_timer(0.6).timeout  # 快入趋稳
	_check(fx._state == MetaHealthFX.STATE_DYING, "5：hp<20% 进 DYING")
	_check(fx._heart_rate >= 1.0 and fx._heart_rate <= 1.2, "5：心率 ∈ [1.0,1.2]Hz")
	var bmin := 1.0
	var bmax := 1.0
	var t0 := Time.get_ticks_msec()
	while Time.get_ticks_msec() - t0 < 600:
		await get_tree().process_frame
		bmin = minf(bmin, fx.breath_scale())
		bmax = maxf(bmax, fx.breath_scale())
	_check(bmin >= 0.984 and bmax <= 1.016, "5：breath_scale() ∈ [0.985,1.015]")
	_check(bmax > 1.005 or bmin < 0.995, "5：呼吸确实在摆动")
	_check(fx.breath_active(), "5：DYING 呼吸激活")
	GameState.set_reduce_flash(true)
	_check(not fx.breath_active(), "5：减少闪光后 breath_active()==false")
	GameState.set_reduce_flash(false)

	# ================= 6：LOD1 时 hud 旧晕影回退（D2） =================
	var hud := get_node("Main/HUD")
	GameState.health = 10.0
	GameState.health_changed.emit(10.0)
	fx._set_lod(1)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(hud._vignette.modulate.a > 0.0, "6：LOD1 时 hud 低血晕影回退生效")
	fx._set_lod(0)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(hud._vignette.modulate.a == 0.0, "6：LOD0 时 hud 晕影移交 MetaFX（恒 0）")

	# ================= 7：满血静止早退零参数上传（D5/D10） =================
	GameState.heal(999.0)
	fx._damage_x = 0.0
	fx._target_x = 0.0
	fx._state = MetaHealthFX.STATE_NORMAL
	fx._hit_pulse = 0.0
	fx._ripple_t = 2.0
	fx._heart_phase = -1.0
	fx._heart_env = 0.0
	fx._heal_t = -1.0
	fx._heal_jitter = 0.0
	fx._grow_boost = 0.0
	fx._breath = 1.0
	for i in 5:  # 吸收残留过渡，进入稳态
		await get_tree().process_frame
	fx._upload_count = 0
	fx._early_out_count = 0
	for i in 60:
		await get_tree().process_frame
	_check(fx._upload_count == 0, "7：满血静止 60 帧零参数上传（D5）")
	_check(fx._early_out_count >= 60, "7：早退命中（_process 早退）")
	_check(not fx._rect.visible, "7：满血稳态隐藏全屏 ColorRect（零 GPU）")

	print("META HEALTH FX TEST DONE, failures = ", _failures)
	GameState.delete_save()
	GameState.reduce_flash = false
	GameState.save_profile()
	get_tree().quit(_failures)
