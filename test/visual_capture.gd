extends Node
## 视觉验证：按 MODE 截图到 /tmp/infiair_capture.png。
## 需窗口模式运行（headless 为 dummy 渲染，截不到画面）：
##   godot --path . res://test/visual_capture.tscn
## MODE: gameplay（默认，Boss 警告画面）/ start_panel（存档开始面板）/ base（基地控制台）/ mothership（母舰对接）

const FRAMES_BEFORE_SHOT := 100
const SHOT_PATH := "/tmp/infiair_capture.png"
const MODE := "gameplay"


func _ready() -> void:
	if MODE == "start_panel":
		GameState.save_run(50.0, 10.0)  # 伪造存档让开始面板出现
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	match MODE:
		"gameplay":
			# 触发 Boss 警告横幅，便于截图覆盖该画面
			get_node("Main/Spawner")._trigger_boss()
			for i in FRAMES_BEFORE_SHOT:
				await get_tree().process_frame
		"start_panel":
			for i in 30:
				await get_tree().process_frame
		"base":
			# 基地控制台界面
			await get_tree().process_frame
			GameState.add_rp(10)
			GameState.add_buff(&"spread_shot")
			get_node("Main")._start_homecoming()
			await get_tree().create_timer(2.0).timeout
		"mothership":
			# 母舰悬停 + 敌机（扫射开火）+ 玩家对接进入驻留（光束 + 弹匣条）
			var main := get_node("Main")
			main._summon_mothership()
			var ms: Mothership = main._mothership
			ms.position = Vector2(960.0, 270.0)
			ms._state = Mothership.State.HOVER
			var spawner := get_node("Main/Spawner")
			var tgt := load("res://scenes/enemy.tscn").instantiate() as Enemy
			tgt.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
			tgt.position = Vector2(1200.0, 500.0)
			main.add_child(tgt)
			await get_tree().create_timer(0.5).timeout
			get_node("Main/Player").position = Vector2(960.0, 430.0)
			await get_tree().create_timer(2.0).timeout
	var img := get_viewport().get_texture().get_image()
	img.save_png(SHOT_PATH)
	print("capture saved: ", SHOT_PATH)
	GameState.delete_save()
	get_tree().quit()
