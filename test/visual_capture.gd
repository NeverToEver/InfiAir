extends Node
## 视觉验证：按 MODE 截图到 /tmp/infiair_capture.png。
## 需窗口模式运行（headless 为 dummy 渲染，截不到画面）：
##   godot --path . res://test/visual_capture.tscn
## MODE: gameplay（默认，Boss 警告画面）/ start_panel（存档开始面板）/ talent（天赋台）

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
		"talent":
			await get_tree().process_frame
			get_node("Main")._start_homecoming()
			await get_tree().create_timer(2.0).timeout
	var img := get_viewport().get_texture().get_image()
	img.save_png(SHOT_PATH)
	print("capture saved: ", SHOT_PATH)
	GameState.delete_save()
	get_tree().quit()
