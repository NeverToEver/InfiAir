extends Node
## 返航过场逐镜头截图工具（人工核对用，非常规断言测试）。
## 需窗口模式运行（headless 为 dummy 渲染，截不到画面）：
##   godot --path . res://test/return_capture.tscn
## 把 _shot_durations 拉长到每镜头 8s，在各镜头中段截图存 /tmp/return_shot*.png；
## 镜头 7 额外在渐暗末段截一张 7b（应接近全黑）。

const SHOT_LEN := 8.0

## [距过场启动的秒数, 输出路径]：镜头 1..6 取 40% 处；镜头 7 取面部特写阶段与渐暗末段
## 例外：镜头 5 取 80% 处（8s 拉长时间轴下，40% 时座舱开启/跃下尚未发生）
const SCHEDULE := [
	[3.2, "/tmp/return_shot1.png"],
	[11.2, "/tmp/return_shot2.png"],
	[19.2, "/tmp/return_shot3.png"],
	[27.2, "/tmp/return_shot4.png"],
	[39.7, "/tmp/return_shot5.png"],
	[43.2, "/tmp/return_shot6.png"],
	[54.6, "/tmp/return_shot7.png"],
	[55.9, "/tmp/return_shot7b.png"],
]


func _ready() -> void:
	var cine: ReturnCinematic = load("res://scenes/return_cinematic.tscn").instantiate()
	add_child(cine)
	# add_child 同帧替换时长表（首镜头延后到帧末启动，见 return_cinematic._ready）
	cine._shot_durations = []
	for i in 7:
		cine._shot_durations.append(SHOT_LEN)
	var t := 0.0
	for item in SCHEDULE:
		await get_tree().create_timer(item[0] - t).timeout
		t = item[0]
		get_viewport().get_texture().get_image().save_png(item[1])
		print("capture saved: %s (shot_index=%d)" % [item[1], cine._shot_index])
	get_tree().quit(0)
