class_name AimCrosshair
extends Node2D
## 鼠标跟随准星（P1-1 辅助瞄准重设计）：世界坐标 top_level Node2D，挂 Player 下。
## 对局活跃（未暂停、未锁输入、存活）时显示并跟随 Player.aim_point()，同时隐藏系统光标；
## 暂停/Buff/基地/结算/死亡/过场恢复系统光标并隐藏准星——同一条件驱动两处，
## 避免双光标/无光标死角。laser_weapon 光束走原始鼠标，与本准星天然一致。
## 程序化四角 bracket + 中心点（指示器族，不乘 world_scale）。

const HALF_SIZE := 14.0  # bracket 外接半宽
const ARM := 6.0  # bracket 单臂长
const WIDTH := 2.0
const COLOR := Color(0.55, 0.95, 1.0, 0.95)

var _player: Player = null


## Player._load_balance 在 add_child 前调用（top_level 需入树前置位）
func init(p: Player) -> void:
	_player = p
	top_level = true
	z_index = 10  # 世界实体之上（辅助框层 9、敌机/子弹 0），CanvasLayer HUD 之下
	process_mode = Node.PROCESS_MODE_ALWAYS  # 暂停态也要能切回系统光标并隐藏准星


func _exit_tree() -> void:
	# 场景切换/重开兜底：准星消亡时归还系统光标
	if Input.mouse_mode == Input.MOUSE_MODE_HIDDEN:
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE


func _process(_delta: float) -> void:
	var active := (
		_player != null
		and not get_tree().paused
		and not _player._dead
		and not _player._input_locked
	)
	if active:
		global_position = _player.aim_point()
		queue_redraw()
	visible = active
	var want := Input.MOUSE_MODE_HIDDEN if active else Input.MOUSE_MODE_VISIBLE
	if Input.mouse_mode != want:
		Input.mouse_mode = want


func _draw() -> void:
	var pulse := 0.75 + 0.25 * sin(Time.get_ticks_msec() / 1000.0 * 6.0)
	var c := COLOR * Color(1.0, 1.0, 1.0, pulse)
	for sx in [-1.0, 1.0]:
		for sy in [-1.0, 1.0]:
			var corner := Vector2(sx * HALF_SIZE, sy * HALF_SIZE)
			draw_line(corner, corner - Vector2(sx * ARM, 0.0), c, WIDTH, true)
			draw_line(corner, corner - Vector2(0.0, sy * ARM), c, WIDTH, true)
	draw_circle(Vector2.ZERO, 1.6, c)
