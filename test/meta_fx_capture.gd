extends Node
## Meta HUD 视觉审计截图（docs/META_HUD_DESIGN.md §7 人工核对）：
## 需窗口模式运行（headless 为 dummy 渲染，截不到画面）：
##   godot --path . res://test/meta_fx_capture.tscn
## 输出 /tmp/meta_fx_{healthy,hit,caution,damaged,dying,settings_modes}.png：
## 满血基准（应与世界原貌一致）/ 受击色差峰+定向波纹 / 各血量档裂纹密度 / DYING 收窄 / 设置页无障碍分区。

const OUT_DIR := "/tmp/"


func _ready() -> void:
	GameState.reduce_flash = false
	add_child((load("res://scenes/main.tscn") as PackedScene).instantiate())
	var main := get_node("Main")
	await get_tree().process_frame
	await get_tree().process_frame
	get_tree().paused = false  # 开始面板路径可能带暂停态
	# 关闭开始面板（无存档时它开场自显会遮挡画面，参照 visual_capture.gd）
	var sp: CanvasLayer = get_node("Main/StartPanel")
	if sp.visible:
		sp.press_new_game()
	var spawner := get_node("Main/Spawner")
	spawner.set_process(false)
	var player: Player = get_node("Main/Player")
	player.set_auto_fire(false)
	# 摆几架静态敌机丰富画面，让后处理有内容可作用
	for i in 5:
		var e := (load("res://scenes/enemy.tscn") as PackedScene).instantiate() as Enemy
		e.setup(spawner.ENEMY_TYPES[i % spawner.ENEMY_TYPES.size()], &"straight", 1.0)
		e.can_shoot = false
		e.speed = 0.0
		e.position = Vector2(500.0 + i * 220.0, 300.0 + (i % 2) * 120.0)
		main.add_child(e)
	player.position = Vector2(960.0, 800.0)
	player.set_since_damage(0.0)  # 关闭被动回血，保持各血量档稳定
	for i in 30:  # 等裂纹距离场烘焙与首帧稳定
		await get_tree().process_frame

	# 1. 满血基准：MetaFX 应完全隐形（早退 + 隐藏全屏 ColorRect）
	GameState.health = 100.0
	GameState.health_changed.emit(100.0)
	await get_tree().create_timer(0.3).timeout
	await _shot("healthy")

	# 2. 受击峰值：25 伤害来自右上方 → 色差峰 + 定向波纹（峰区约 0~50ms，2 帧截图）
	player.set_invincible(0.0)
	player.set_last_hit_frame(-1)
	player.take_damage(25.0, Vector2(1500.0, 350.0))
	for i in 2:
		await get_tree().process_frame
	await _shot("hit")

	# 3. CAUTION（hp 60%，x=0.4）：稀疏边缘裂纹 + 轻度去饱和
	GameState.health = 60.0
	GameState.health_changed.emit(60.0)
	await get_tree().create_timer(0.8).timeout
	await _shot("caution")

	# 4. DAMAGED（hp 40%，x=0.6）：中等裂纹密度，色带转橙
	GameState.health = 40.0
	GameState.health_changed.emit(40.0)
	await get_tree().create_timer(0.8).timeout
	await _shot("damaged")

	# 5. DYING（hp 12%，x=0.88）：密集红裂、晕影收窄、强去饱和、心跳抖动
	GameState.health = 12.0
	GameState.health_changed.emit(12.0)
	await get_tree().create_timer(1.0).timeout
	await _shot("dying")

	# 6. 设置页「操作模式」：无障碍分区（减少闪光开关）
	GameState.health = 100.0
	GameState.health_changed.emit(100.0)
	var settings := get_tree().get_first_node_in_group("settings_ui") as CanvasLayer
	settings.show_settings()
	settings.show_page(&"modes")
	for i in 30:
		await get_tree().process_frame
	await _shot("settings_modes")

	GameState.delete_save()
	get_tree().quit()


func _shot(p_name: String) -> void:
	await get_tree().process_frame
	var img := get_viewport().get_texture().get_image()
	img.save_png(OUT_DIR + "meta_fx_%s.png" % p_name)
	print("capture saved: meta_fx_", p_name, ".png")
