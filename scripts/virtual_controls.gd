class_name VirtualControls
extends CanvasLayer
## 触屏虚拟输入层（mobile touch，docs/archive/2026-08-07-deferred-restart-plan.md §3）：
## 左虚拟摇杆 → move_*，右虚拟摇杆 → aim_*（增量，与手柄右摇杆虚拟准星同语义），
## 虚拟按钮 → boost / fine_move / dash / parry。
## 注入路径：Input.action_press/release（等价 InputEventAction）——player 的
## Input.get_vector / is_action_pressed 读取路径零改动；仅输入目标为 action 状态，
## 与手柄/键鼠事件互不覆盖（无摇杆输入时零注入，桌面零回归）。
## 启用：设置「触控控件」开关（GameState.touch_controls，profile 持久化），
## Main._ready 创建本层并联动开关（touch_controls_changed 信号）。

const LAYER_ID := 1  # HUD(2) 之下、世界之上：不遮挡 HUD，半透明

## 布局（1920×1080 设计坐标；canvas_items stretch 下随窗口缩放）
const MOVE_CENTER := Vector2(240.0, 860.0)
const MOVE_RADIUS := 150.0
const AIM_CENTER := Vector2(1680.0, 860.0)
const AIM_RADIUS := 150.0
## 摇杆命中放宽：基座半径 ×1.4（手指比鼠标粗）
const ZONE_FUDGE := 1.4
## 摇杆死区（归一化，按下起点周围静止区）
const DEADZONE := 0.15

## 虚拟按钮：action -> {center, radius}（屏幕位置常量）
const BUTTONS := {
	&"boost": {"center": Vector2(520.0, 560.0), "radius": 60.0},
	&"fine_move": {"center": Vector2(660.0, 700.0), "radius": 52.0},
	&"dash": {"center": Vector2(1500.0, 620.0), "radius": 62.0},
	&"parry": {"center": Vector2(1780.0, 620.0), "radius": 62.0},
}

const MOVE_ACTIONS: Array[StringName] = [&"move_left", &"move_right", &"move_up", &"move_down"]
const AIM_ACTIONS: Array[StringName] = [&"aim_left", &"aim_right", &"aim_up", &"aim_down"]

var _enabled := false
var _move_touch := -1
var _move_base := Vector2.ZERO
var _move_vec := Vector2.ZERO
var _aim_touch := -1
var _aim_base := Vector2.ZERO
var _aim_vec := Vector2.ZERO
var _buttons := {}  # action(StringName) -> touch index
var _ui: Control


func set_enabled(v: bool) -> void:
	if _enabled == v:
		return
	_enabled = v
	if _ui != null:
		_ui.visible = v
		_ui.queue_redraw()
	if not v:
		_release_all()


func is_enabled() -> bool:
	return _enabled


## 触屏瞄准基准（无鼠标时瞄准点起点）：可见世界中心。
## player.aim_point() 在虚拟控件启用时以它为 raw 基准，右摇杆增量偏移（同手柄语义）。
func base_aim_position() -> Vector2:
	return GameState.view_world_rect().get_center()


## 当前左摇杆向量（0..1，测试/诊断）
func move_vec() -> Vector2:
	return _move_vec


## 当前右摇杆向量（0..1，测试/诊断）
func aim_vec() -> Vector2:
	return _aim_vec


func _ready() -> void:
	layer = LAYER_ID
	process_mode = Node.PROCESS_MODE_ALWAYS  # 暂停菜单打开时也能接收/清理触摸
	# 注入目标 action 必须存在（aim_* 由手柄装配运行时注册，无手柄环境不存在）
	for a: StringName in MOVE_ACTIONS + AIM_ACTIONS + BUTTONS.keys():
		if not InputMap.has_action(a):
			InputMap.add_action(a)
	_ui = Control.new()
	_ui.set_anchors_preset(Control.PRESET_FULL_RECT)
	_ui.mouse_filter = Control.MOUSE_FILTER_IGNORE  # 不拦截下方 GUI/世界
	_ui.draw.connect(_draw_ui)
	_ui.visible = false
	add_child(_ui)


func _exit_tree() -> void:
	if _enabled:
		_release_all()


func _input(event: InputEvent) -> void:
	if not _enabled:
		return
	if event is InputEventScreenTouch:
		_on_touch(event.index, event.pressed, event.position)
	elif event is InputEventScreenDrag:
		_on_drag(event.index, event.position)


func _on_touch(idx: int, pressed: bool, pos: Vector2) -> void:
	if pressed:
		var zone := _hit_zone(pos)
		if zone.is_empty():
			return
		match zone["type"]:
			&"move":
				if _move_touch == -1:
					_move_touch = idx
					_move_base = pos
					_update_move_vec(pos)
			&"aim":
				if _aim_touch == -1:
					_aim_touch = idx
					_aim_base = pos
					_update_aim_vec(pos)
			&"button":
				var action: StringName = zone["action"]
				if not _buttons.has(action):
					_buttons[action] = idx
					_inject(action, 1.0)
					_ui.queue_redraw()
	else:
		if idx == _move_touch:
			_move_touch = -1
			_move_vec = Vector2.ZERO
			_inject_move(Vector2.ZERO)  # 清残留
			_ui.queue_redraw()
		elif idx == _aim_touch:
			_aim_touch = -1
			_aim_vec = Vector2.ZERO
			_inject_aim(Vector2.ZERO)
			_ui.queue_redraw()
		else:
			for action: StringName in _buttons.duplicate():
				if _buttons[action] == idx:
					_buttons.erase(action)
					_inject(action, 0.0)
					_ui.queue_redraw()


func _on_drag(idx: int, pos: Vector2) -> void:
	if idx == _move_touch:
		_update_move_vec(pos)
	elif idx == _aim_touch:
		_update_aim_vec(pos)


func _hit_zone(pos: Vector2) -> Dictionary:
	for action: StringName in BUTTONS:
		var b: Dictionary = BUTTONS[action]
		if pos.distance_to(b["center"]) <= float(b["radius"]):
			return {"type": &"button", "action": action}
	if pos.distance_to(MOVE_CENTER) <= MOVE_RADIUS * ZONE_FUDGE:
		return {"type": &"move"}
	if pos.distance_to(AIM_CENTER) <= AIM_RADIUS * ZONE_FUDGE:
		return {"type": &"aim"}
	return {}


func _update_move_vec(pos: Vector2) -> void:
	_move_vec = _stick_vec(_move_base, pos, MOVE_RADIUS)
	_inject_move(_move_vec)
	_ui.queue_redraw()


func _update_aim_vec(pos: Vector2) -> void:
	_aim_vec = _stick_vec(_aim_base, pos, AIM_RADIUS)
	_inject_aim(_aim_vec)
	_ui.queue_redraw()


## 摇杆向量：位移/半径归一化（死区截断、限幅 1）；超半径时基座跟随手指由调用方
## _update_* 的 base 重锚处理，此处只算当前向量。
func _stick_vec(base: Vector2, pos: Vector2, radius: float) -> Vector2:
	var v := pos - base
	var dist := v.length()
	var nv := v / radius if dist > 1.0 else Vector2.ZERO
	if nv.length() < DEADZONE:
		return Vector2.ZERO
	return nv.limit_length(1.0)


func _inject_move(v: Vector2) -> void:
	# 静止零注入（触屏设备无键盘，player 读到的即本层注入；桌面不开本层零回归）
	if v == Vector2.ZERO:
		for a: StringName in MOVE_ACTIONS:
			_inject(a, 0.0)
		return
	_inject(&"move_left", maxf(-v.x, 0.0))
	_inject(&"move_right", maxf(v.x, 0.0))
	_inject(&"move_up", maxf(-v.y, 0.0))
	_inject(&"move_down", maxf(v.y, 0.0))


func _inject_aim(v: Vector2) -> void:
	if v == Vector2.ZERO:
		for a: StringName in AIM_ACTIONS:
			_inject(a, 0.0)
		return
	_inject(&"aim_left", maxf(-v.x, 0.0))
	_inject(&"aim_right", maxf(v.x, 0.0))
	_inject(&"aim_up", maxf(-v.y, 0.0))
	_inject(&"aim_down", maxf(v.y, 0.0))


func _inject(action: StringName, strength: float) -> void:
	if strength > 0.0:
		Input.action_press(action, strength)
	else:
		Input.action_release(action)


## 禁用兜底：全部动作释放 + 触摸状态复位（防止残留注入状态污染后续会话）
func _release_all() -> void:
	for a: StringName in MOVE_ACTIONS + AIM_ACTIONS:
		Input.action_release(a)
	for action: StringName in BUTTONS:
		Input.action_release(action)
	_buttons.clear()
	_move_vec = Vector2.ZERO
	_aim_vec = Vector2.ZERO
	_move_touch = -1
	_aim_touch = -1


## 测试/诊断口：以设计坐标（1920×1080 系）直接驱动触摸状态机。
## 绕过窗口→视口坐标变换（Input.parse_input_event 注入的真实事件经变换，headless 下
## 窗口与设计分辨率不同、不可移植；真实设备的视口坐标变换是 Godot 标准行为，
## 区域判定语义与真实 _input 一致）。遵守启用状态（禁用时零注入，桌面零回归）。
func simulate_touch(idx: int, pressed: bool, pos: Vector2) -> void:
	if not _enabled:
		return
	_on_touch(idx, pressed, pos)


## 测试/诊断口：设计坐标拖动（见 simulate_touch 注释）
func simulate_drag(idx: int, pos: Vector2) -> void:
	if not _enabled:
		return
	_on_drag(idx, pos)


## 半透明绘制：摇杆基座/手柄 + 按钮圆 + 首字母标签（ASCII，不依赖字体资源）
func _draw_ui() -> void:
	if not _enabled:
		return
	_draw_stick(MOVE_CENTER, MOVE_RADIUS, _move_vec, Color(0.3, 0.8, 1.0, 0.35))
	_draw_stick(AIM_CENTER, AIM_RADIUS, _aim_vec, Color(1.0, 0.6, 0.2, 0.35))
	for action: StringName in BUTTONS:
		var b: Dictionary = BUTTONS[action]
		var c: Vector2 = b["center"]
		var r: float = b["radius"]
		var lit := _buttons.has(action)
		var col := Color(0.3, 0.8, 1.0, 0.5) if lit else Color(0.3, 0.8, 1.0, 0.28)
		_ui.draw_circle(c, r, col)
		_ui.draw_arc(c, r, 0.0, TAU, 32, Color(0.7, 0.95, 1.0, 0.6), 2.0)
		_ui.draw_string(
			ThemeDB.fallback_font,
			c + Vector2(-7.0, 6.0),
			_button_label(action),
			HORIZONTAL_ALIGNMENT_CENTER,
			-1.0,
			18,
			Color(1.0, 1.0, 1.0, 0.85)
		)


func _draw_stick(center: Vector2, radius: float, vec: Vector2, col: Color) -> void:
	_ui.draw_circle(center, radius, Color(col, col.a * 0.6))
	_ui.draw_arc(center, radius, 0.0, TAU, 48, Color(col, col.a * 1.4), 2.0)
	var knob := center + vec * radius * 0.7
	_ui.draw_circle(knob, radius * 0.35, Color(0.9, 0.98, 1.0, 0.7))


func _button_label(action: StringName) -> String:
	match action:
		&"boost":
			return "B"
		&"fine_move":
			return "F"
		&"dash":
			return "D"
		&"parry":
			return "P"
	return ""
