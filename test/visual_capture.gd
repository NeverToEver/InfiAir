extends Node
## 视觉验证工具：加载主场景，跑若干帧后自截屏保存到 /tmp 并退出。
## 运行：godot --path . res://test/visual_capture.tscn（窗口模式，非 headless）

const FRAMES_BEFORE_SHOT := 600
const SHOT_PATH := "/tmp/infiair_visual.png"

var _frame := 0


func _ready() -> void:
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	# 模拟玩家位于屏幕中央、自动交战：直接给 GameState 加些分数看 UI 也行，这里保持自然状态


func _process(_delta: float) -> void:
	_frame += 1
	if _frame == FRAMES_BEFORE_SHOT:
		await RenderingServer.frame_post_draw
		var img := get_viewport().get_texture().get_image()
		img.save_png(SHOT_PATH)
		print("VISUAL_CAPTURE_SAVED:", SHOT_PATH)
		get_tree().quit()
