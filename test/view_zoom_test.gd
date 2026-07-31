extends Node
## 视角缩放测试：
## 三档映射与切换信号、view_world_rect 可见区域计算、profile 持久化往返、
## 设置页三选按钮 wiring、main 场景相机 zoom 应用与震动 offset 兼容、
## 玩家边缘钳制 / 敌机与子弹出屏销毁 / 刷怪位置与预告线 / 敌机悬停锚点 /
## Boss 巡航范围与战斗锚线随档收窄。
## 结束时恢复 medium 档并落盘，避免污染其他测试进程。

var _failures: int = 0

const ENEMY_SCENE: PackedScene = preload("res://scenes/enemy.tscn")
const BULLET_SCENE: PackedScene = preload("res://scenes/bullet.tscn")
const BOSS_SCENE: PackedScene = preload("res://scenes/boss.tscn")
const SETTINGS_SCRIPT: GDScript = preload("res://scripts/settings_ui.gd")


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _read_profile() -> Dictionary:
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(GameState.PROFILE_PATH))
	return parsed if parsed is Dictionary else {}


func _write_profile(data: Dictionary) -> void:
	var f := FileAccess.open(GameState.PROFILE_PATH, FileAccess.WRITE)
	f.store_string(JSON.stringify(data))
	f.close()


## 期望可见区域（相机固定 (960,540)，视口 1920×1080）
func _expect_rect(factor: float) -> Rect2:
	var size := Vector2(1920.0, 1080.0) / factor
	return Rect2(Vector2(960.0, 540.0) - size * 0.5, size)


func _rect_close(a: Rect2, b: Rect2, tol: float = 0.5) -> bool:
	return (
		a.position.distance_to(b.position) < tol
		and a.size.distance_to(b.size) < tol
	)


func _ready() -> void:
	# 确定性起点：清存档，视角档位归位 medium（profile 级，reset_run 不清）
	GameState.delete_save()
	GameState.view_zoom = &"medium"
	GameState._view_zoom_factor = 1.35

	# ---------- 1. 档位映射与切换 ----------
	GameState.set_view_zoom(&"small")
	_check(GameState.view_zoom == &"small" and GameState.view_zoom_factor() == 1.0, "small 档 zoom=1.0")
	GameState.set_view_zoom(&"medium")
	_check(GameState.view_zoom_factor() == 1.35, "medium 档 zoom=1.35")
	GameState.set_view_zoom(&"large")
	_check(GameState.view_zoom_factor() == 1.7, "large 档 zoom=1.7")
	var emitted: Array[float] = []
	GameState.view_zoom_changed.connect(func(f: float) -> void: emitted.append(f))
	GameState.set_view_zoom(&"small")
	_check(emitted.size() == 1 and emitted[0] == 1.0, "切换档位发出 view_zoom_changed 信号")
	GameState.set_view_zoom(&"small")
	_check(emitted.size() == 1, "同档重复设置不发信号")
	GameState.set_view_zoom(&"huge")
	_check(GameState.view_zoom == &"small", "非法档位被忽略")

	# ---------- 2. 可见区域计算 ----------
	_check(_rect_close(GameState.view_world_rect(), _expect_rect(1.0)), "small 可见区域 = 全屏 1920×1080")
	GameState.set_view_zoom(&"medium")
	_check(_rect_close(GameState.view_world_rect(), _expect_rect(1.35)), "medium 可见区域 ≈ 1422×800")
	GameState.set_view_zoom(&"large")
	_check(_rect_close(GameState.view_world_rect(), _expect_rect(1.7)), "large 可见区域 ≈ 1131×635")
	GameState.set_view_zoom(&"small")
	_check(
		_rect_close(GameState.view_world_rect(80.0), Rect2(-80.0, -80.0, 2080.0, 1240.0)),
		"margin 外扩与旧子弹边界一致"
	)

	# ---------- 3. profile 持久化 ----------
	GameState.set_view_zoom(&"large")
	_check(str(_read_profile().get("view_zoom", "")) == "large", "视角档位写入 profile")
	GameState.view_zoom = &"small"  # 篡改内存（不经 setter，避免写盘）
	GameState._view_zoom_factor = 1.0
	GameState.load_profile()
	_check(GameState.view_zoom == &"large" and GameState.view_zoom_factor() == 1.7, "视角档位从 profile 恢复")
	# 旧档案无 view_zoom 字段：保留当前值
	_write_profile({"version": 1, "high_score": 0})
	GameState.view_zoom = &"small"
	GameState._view_zoom_factor = 1.0
	GameState.load_profile()
	_check(GameState.view_zoom == &"small", "旧档（无 view_zoom 字段）读取保留当前档位")
	# 非法档位值：忽略并保持当前值
	_write_profile({"version": 1, "high_score": 0, "view_zoom": "huge"})
	GameState.load_profile()
	_check(GameState.view_zoom == &"small", "profile 非法档位值被忽略")
	GameState.set_view_zoom(&"medium")

	# ---------- 4. 设置页三选按钮 ----------
	var settings := SETTINGS_SCRIPT.new() as CanvasLayer
	add_child(settings)
	settings.show_settings()
	_check(settings._zoom_buttons.size() == 3, "设置页视角三选按钮")
	_check(
		(settings._zoom_buttons[&"medium"] as Button).button_pressed,
		"视角按钮选中态 = 当前档"
	)
	(settings._zoom_buttons[&"large"] as Button).pressed.emit()
	_check(GameState.view_zoom == &"large", "视角按钮点击切换档位")
	settings.queue_free()
	GameState.set_view_zoom(&"medium")

	# ---------- 5. main 场景：相机应用 + 震动兼容 ----------
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	var welcome: CanvasLayer = get_node("Main/WelcomeScreen")
	if welcome.visible:
		welcome.dismiss()
	var start_panel: CanvasLayer = get_node("Main/StartPanel")
	if start_panel.visible:
		start_panel._on_new_game_pressed()
	var player: Player = get_node("Main/Player")
	player._auto_fire_enabled = false
	player._invincible = 999.0
	var spawner: Node = get_node("Main/Spawner")
	spawner.set_process(false)
	await get_tree().process_frame
	await get_tree().process_frame
	var camera: Camera2D = get_node("Main/Camera2D")
	_check(camera.zoom.distance_to(Vector2(1.35, 1.35)) < 0.001, "相机默认应用 medium 档 zoom=1.35")
	_check(GameState.camera_ref == camera, "相机注册到 GameState.camera_ref")
	GameState.set_view_zoom(&"small")
	_check(camera.zoom == Vector2.ONE, "切 small 相机 zoom=1.0")
	GameState.set_view_zoom(&"large")
	_check(camera.zoom.distance_to(Vector2(1.7, 1.7)) < 0.001, "切 large 相机 zoom=1.7")
	# 震动只写 offset：zoom 不受影响，衰减结束后 offset 归零
	GameState.shake(20.0)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(camera.offset != Vector2.ZERO, "震动产生 offset")
	_check(camera.zoom.distance_to(Vector2(1.7, 1.7)) < 0.001, "震动期间 zoom 保持 1.7")
	await get_tree().create_timer(1.0).timeout
	_check(camera.offset == Vector2.ZERO, "震动衰减后 offset 归零")
	_check(camera.zoom.distance_to(Vector2(1.7, 1.7)) < 0.001, "震动结束后 zoom 仍为 1.7")

	# ---------- 6. 玩家边缘钳制随档收窄（large：x 435.3..1484.7 / y 262.4..817.6） ----------
	var view_large := GameState.view_world_rect()
	var lo := view_large.position + Vector2(40.0, 40.0)
	var hi := view_large.end - Vector2(40.0, 40.0)
	player.velocity = Vector2.ZERO
	player.position = Vector2(0.0, 0.0)
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(player.position.distance_to(lo) < 2.0, "large 档玩家钳制左上 = 可见区域 +40")
	player.position = Vector2(9999.0, 9999.0)
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(player.position.distance_to(hi) < 2.0, "large 档玩家钳制右下 = 可见区域 -40")

	# ---------- 7. 敌机出屏销毁随档收窄 ----------
	# y=1000：small 销毁线 1140 存活；large 销毁线 ≈917.6 应销毁
	GameState.set_view_zoom(&"small")
	var e := ENEMY_SCENE.instantiate() as Enemy
	e.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	e.can_shoot = false
	e.hp = 9999
	e.position = Vector2(600.0, 1000.0)
	get_node("Main").add_child(e)
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(is_instance_valid(e), "small 档 y=1000 敌机存活（销毁线 1140）")
	GameState.set_view_zoom(&"large")
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(not is_instance_valid(e), "large 档 y=1000 敌机出屏销毁（销毁线 ≈917.6）")

	# ---------- 8. 子弹出屏销毁随档收窄 ----------
	# x=100：small 边界 -80 存活；large 边界 ≈315 应销毁
	GameState.set_view_zoom(&"small")
	var b := BULLET_SCENE.instantiate() as Bullet
	b.setup(Vector2.RIGHT, 400.0, 1, true)
	b.position = Vector2(100.0, 500.0)
	get_node("Main").add_child(b)
	await get_tree().process_frame
	_check(is_instance_valid(b), "small 档 x=100 子弹存活（边界 -80）")
	GameState.set_view_zoom(&"large")
	await get_tree().process_frame
	_check(not is_instance_valid(b), "large 档 x=100 子弹出屏销毁（边界 ≈315）")

	# ---------- 9. 刷怪位置/预告线/悬停锚点随档收窄（当前为 large 档） ----------
	spawner._spawn_enemy()  # 异步：先挂预告线，0.6s 后出机
	await get_tree().create_timer(0.2).timeout
	var tel: SpawnTelegraph = null
	for child in get_node("Main").get_children():
		if child is SpawnTelegraph:
			tel = child
	_check(tel != null, "入场预告线已生成")
	if tel != null:
		_check(
			absf(tel.position.y - GameState.view_world_rect().position.y) < 1.0,
			"预告线贴在可见区域顶部"
		)
		var view := GameState.view_world_rect()
		_check(
			tel.position.x > view.position.x and tel.position.x < view.end.x,
			"预告线 x 在可见区域内"
		)
	await get_tree().create_timer(0.7).timeout
	var spawned: Enemy = null
	for child in get_node("Main").get_children():
		if child is Enemy:
			spawned = child
	_check(spawned != null, "敌机已刷出")
	if spawned != null:
		var view := GameState.view_world_rect()
		_check(
			spawned.position.x > view.position.x + 30.0 and spawned.position.x < view.end.x - 30.0,
			"刷怪 x 在可见区域内（60px 边距）"
		)
		_check(
			absf(spawned.position.y - (view.position.y - 60.0)) < 100.0,
			"刷怪 y 在可见区域顶上方"
		)
		_check(
			spawned.anchor_y >= view.position.y,
			"large 档刷怪锚点 ≥ 可见顶（spawner 分配加 view 基线）"
		)
		spawned.queue_free()
	await get_tree().process_frame
	# 锚点 fallback：spawner 未分配时 _resolve_anchor 自取，钳入「view 顶 + 悬停带」
	var e_fb := ENEMY_SCENE.instantiate() as Enemy
	e_fb.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	e_fb.can_shoot = false
	e_fb.hp = 9999
	e_fb.position = Vector2(600.0, GameState.view_world_rect().position.y + 10.0)
	get_node("Main").add_child(e_fb)
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(
		e_fb.anchor_y >= GameState.view_world_rect().position.y + e_fb.HOVER_BAND.x,
		"large 档敌机自取锚点 ≥ 可见顶 + 悬停带顶缘偏移"
	)
	e_fb.queue_free()
	await get_tree().process_frame

	# ---------- 10. Boss 出场位置与巡航范围 ----------
	spawner._spawn_boss(1)
	var boss: Boss = null
	for child in get_node("Main").get_children():
		if child is Boss:
			boss = child
	_check(boss != null, "Boss 已生成")
	if boss != null:
		# 出场 y 在降入移动前断言（不等帧，避免 ENTER_SPEED 位移干扰）
		_check(
			absf(boss.position.y - (GameState.view_world_rect().position.y - 160.0)) < 1.0,
			"Boss 出场 y 在可见区域顶上方"
		)
		boss.queue_free()
	await get_tree().process_frame
	var range_boss := BOSS_SCENE.instantiate() as Boss
	GameState.set_view_zoom(&"small")
	var small_range := range_boss.strafe_range()
	_check(small_range == Vector2(300.0, 1620.0), "small 档 Boss 巡航范围 = 配置 300..1620")
	_check(
		absf(range_boss.fight_anchor_y() - range_boss.FIGHT_Y) < 0.001,
		"small 档 Boss 战斗锚线 = FIGHT_Y（view.position.y=0 行为不变）"
	)
	GameState.set_view_zoom(&"large")
	var large_range := range_boss.strafe_range()
	var expect_lo := GameState.view_world_rect().position.x + 300.0
	var expect_hi := GameState.view_world_rect().end.x - 300.0
	_check(
		absf(large_range.x - expect_lo) < 1.0 and absf(large_range.y - expect_hi) < 1.0,
		"large 档 Boss 巡航范围随可见区域收窄"
	)
	var view_anchor := GameState.view_world_rect()
	var anchor_large := range_boss.fight_anchor_y()
	_check(
		absf(anchor_large - (view_anchor.position.y + range_boss.FIGHT_Y)) < 0.001,
		"large 档 Boss 战斗锚线 = 可见顶 + FIGHT_Y"
	)
	_check(
		anchor_large > view_anchor.position.y and anchor_large < view_anchor.end.y,
		"large 档 Boss 战斗锚线落在可见区域内"
	)
	range_boss.free()
	# ---------- 11. 母舰召唤位置（小窗演出直推，母舰穿梭入场于停驻点） ----------
	var main := get_node("Main")
	main._summon_mothership()
	_check(main._summon_window != null, "召唤小窗已弹出")
	if main._summon_window != null:
		main._summon_window.skip()  # 幂等直推：finished → main 开穿梭门并实例化母舰
	_check(main._mothership != null, "母舰已召唤")
	if main._mothership != null:
		_check(
			absf(main._mothership.position.x - GameState.view_world_rect().get_center().x) < 1.0,
			"母舰出场 x = 可见区域中心"
		)
		var warp_drop: float = GameState.cfg("effects.mothership_summon.warp_in_drop", 260.0)
		_check(
			absf(main._mothership.position.y - (GameState.cfg("mothership.hover_y", 270.0) - warp_drop * GameState.world_scale)) < 1.0,
			"母舰出场 y = 停驻点上方 warp_in_drop × world_scale（穿梭滑入起点）"
		)
		main._mothership.queue_free()
	await get_tree().process_frame

	print("VIEW ZOOM TEST DONE, failures = ", _failures)
	# 清理：恢复 medium 档并落盘，避免污染其他测试进程
	GameState.set_view_zoom(&"medium")
	GameState.reset_run()
	GameState.save_profile()
	GameState.delete_save()
	get_tree().quit(_failures)
