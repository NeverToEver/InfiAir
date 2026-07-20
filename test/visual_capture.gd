extends Node
## 视觉验证：实例化主场景，触发 Boss 警告，N 帧后截图到 /tmp/infiair_capture.png。
## 需窗口模式运行（headless 为 dummy 渲染，截不到画面）：
##   godot --path . res://test/visual_capture.tscn

const FRAMES_BEFORE_SHOT := 100
const SHOT_PATH := "/tmp/infiair_capture.png"


func _ready() -> void:
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	# 触发 Boss 警告横幅，便于截图覆盖该画面
	get_node("Main/Spawner")._trigger_boss()
	for i in FRAMES_BEFORE_SHOT:
		await get_tree().process_frame
	var img := get_viewport().get_texture().get_image()
	img.save_png(SHOT_PATH)
	print("capture saved: ", SHOT_PATH)
	get_tree().quit()
