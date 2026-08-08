extends Node
## 性能基准：固定压力场景（main + 200 敌机 + 玩家强制开火 + 每 20 物理帧一次爆炸），
## 跑 1800 物理帧统计平均帧耗时。headless + 高物理频率下帧耗时≈纯 CPU 成本。
## 用法：godot --headless --path . res://test/perf_bench.tscn

const FRAMES := 1800
const ENEMY_COUNT := 200
const EXPLOSION_EVERY := 20  # 每 20 物理帧一次爆炸（标准 60Hz 下≈每秒 3 次）
const FIRE_EVERY := 5  # 每 5 物理帧强制齐射一次（制造子弹分配/回收压力）


func _ready() -> void:
	GameState.delete_save()
	GameState.reset_run()
	# 物理帧率拉满，让循环 CPU 受限，测纯耗时
	Engine.physics_ticks_per_second = 1000
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	await get_tree().process_frame
	await get_tree().process_frame
	var main := get_node("Main")
	var spawner := main.get_node("Spawner")
	spawner.set_process(false)  # 自己控制刷怪节奏
	# 200 只敌机（各机型/策略混合，部分可开火）——L10：注释口径同步（原「30 只」落后于常量）
	for i in ENEMY_COUNT:
		var cfg: Dictionary = spawner.ENEMY_TYPES[i % spawner.ENEMY_TYPES.size()]
		var e := load("res://scenes/enemy.tscn").instantiate() as Enemy
		var strategies: Array = cfg["strategies"]
		e.setup(cfg, strategies[randi() % strategies.size()], 1.0)
		e.position = Vector2(randf_range(60.0, 1860.0), randf_range(-400.0, 800.0))
		main.add_child(e)
	# 玩家强制开火（auto fire 默认开，另外定期手动齐射制造弹丸压力）
	var player: Player = main.get_node("Player")
	var t0 := Time.get_ticks_msec()
	for i in FRAMES:
		await get_tree().physics_frame
		if i % EXPLOSION_EVERY == 0:
			# 随批次 D 重定型：Explosion 已迁 C#（spawn_at→SpawnAt），静态方法经脚本资源调用
			load("res://csharp/godot/Explosion.cs").SpawnAt(main, Vector2(randf_range(200.0, 1700.0), randf_range(200.0, 800.0)), 1.0)
		if i % FIRE_EVERY == 0:
			player.fire(Vector2.UP.rotated(randf_range(-0.6, 0.6)))
		if i % 10 == 0:
			# 敌机生成churn（走 spawner.spawn_minion，优化前后同一代码路径）
			spawner.spawn_minion(Vector2(randf_range(60.0, 1860.0), -60.0))
	var elapsed := Time.get_ticks_msec() - t0
	var avg := float(elapsed) / float(FRAMES)
	print("PERF_RESULT frames=%d total_ms=%d avg_frame_ms=%.3f equivalent_fps=%.1f" % [FRAMES, elapsed, avg, 1000.0 / avg])
	Engine.physics_ticks_per_second = 60
	GameState.delete_save()
	get_tree().quit()
