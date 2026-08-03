extends Node
## 开场过场逐镜头截图工具（人工核对用，非常规断言测试）。
## 需窗口模式运行（headless 为 dummy 渲染，截不到画面）：
##   godot --path . res://test/intro_capture.tscn
## 把 _shot_durations 拉长到每镜头 8s，在各镜头关键动作展开后截图存 /tmp/intro_shot*.png；
## 末尾加一张标题定格（48.6s = 6×8 + 0.6，定格段 1.2s 中段）。

const SHOT_LEN := 8.0

## [距过场启动的秒数, 输出路径]：各镜头取 50–65% 处（关键动作已展开），外加标题定格
## 镜头 1 取 51%（dur*0.45 的二次殉爆刚起，冲击波扩散中段）
const SCHEDULE := [
	[4.1, "/tmp/intro_shot1.png"],
	[12.2, "/tmp/intro_shot2.png"],
	[20.0, "/tmp/intro_shot3.png"],
	[28.4, "/tmp/intro_shot4.png"],
	[36.5, "/tmp/intro_shot5.png"],
	[45.0, "/tmp/intro_shot6.png"],
	[48.6, "/tmp/intro_title.png"],
]


func _ready() -> void:
	var cine: IntroCinematic = load("res://scenes/intro_cinematic.tscn").instantiate()
	add_child(cine)
	# add_child 同帧替换时长表（首镜头延后到帧末启动，见 intro_cinematic._ready）
	# L01（2026-08-03 审查）：set_shot_durations 返回 void，原链式调用为编译错误
	# （A7 重构把 setter 改 void 时未同步工具，窗口模式截图工具已坏）；整表一次传入
	var shots: Array[float] = []
	for i in 6:
		shots.append(SHOT_LEN)
	cine.set_shot_durations(shots)
	var t := 0.0
	for item in SCHEDULE:
		await get_tree().create_timer(item[0] - t).timeout
		t = item[0]
		get_viewport().get_texture().get_image().save_png(item[1])
		print("capture saved: %s (shot_index=%d)" % [item[1], cine.shot_index()])
	get_tree().quit(0)
