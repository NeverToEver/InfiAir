extends Node
## 鼠标锁定窗口内（mouse_lock 设置项运行组件，挂 Main）：
## 对局准星活跃（未暂停且系统光标隐藏）且窗口聚焦时，鼠标移出内容区即被
## Input.warp_mouse() 拉回边缘内侧，从根上消除"鼠标出框 → get_global_mouse_position
## 冻结 → 准星失控"的前提；暂停/Buff/基地/结算/过场/开始页等非准星态（AimCrosshair
## 恢复系统光标）与窗口失焦一律放行——暂停后鼠标可自由移出窗口（如点系统标题栏
## 关闭按钮退出游戏）。
## Godot 4 的 Input.warp_mouse 接受屏幕坐标：warp 目标 = 窗口左上角屏幕坐标 + 内容区 clamp 点。
## warp 目标恒取"出框前最后窗口内位置"（_last_known_pos），位移 ≤ 1-2px；且鼠标在窗口外时
## get_global_mouse_position() 本就冻结在最后内部位置，warp 后读值连续——不引入准星跳变，
## 反而把"移回窗口时的位置跳变"钳在边缘内侧（原无 confine 时可有数十 px 跳变）。
## 已知取舍：拖动标题栏时鼠标位于 OS 装饰区会触发 mouse_exited 被拉回（固定尺寸窗口下
## 可接受，用户可在设置中关闭本功能规避）。

var _focused: bool = false
## 最后已知窗口内容区内的鼠标位置（每帧缓存；移出后 Window.mouse_position 不再更新，
## 供 mouse_exited 时生成 warp 目标；从未进入窗口内时为负，此时不拉回）
var _last_known_pos := Vector2(-1.0, -1.0)


func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS  # 暂停时也维持位置缓存与防御；放行判定在 _trap_active
	var win := get_window()
	win.mouse_exited.connect(_on_mouse_exited)
	win.focus_exited.connect(_on_focus_exited)
	win.focus_entered.connect(_on_focus_entered)
	_focused = win.has_focus()


func _process(_delta: float) -> void:
	if DisplayServer.get_name() == "headless":
		return  # headless 无真实鼠标/窗口事件，confine 逻辑全部跳过
	var win := get_window()
	var mp: Vector2 = win.mouse_position
	if mp.x >= 0.0 and mp.y >= 0.0 and mp.x < win.size.x and mp.y < win.size.y:
		_last_known_pos = mp
	_trap_if_out_of_bounds()


func _on_mouse_exited() -> void:
	_trap()


func _on_focus_exited() -> void:
	_focused = false  # 失焦放行：鼠标可自由移出切换应用


func _on_focus_entered() -> void:
	_focused = true


## 生效条件：设置开启 + 对局准星态（未暂停且系统光标隐藏）+ 窗口可见 + 聚焦 + 有内容尺寸。
## 暂停/Buff/基地/结算/过场/开始页（AimCrosshair 均恢复系统光标）与失焦一律放行，
## 鼠标可自由移出窗口（如点系统标题栏关闭按钮退出游戏）。
func _trap_active() -> bool:
	var win := get_window()
	return _trap_enabled(
		GameState.mouse_lock,
		win.visible,
		_focused,
		win.size.x > 0 and win.size.y > 0,
		not get_tree().paused,
		Input.mouse_mode == Input.MOUSE_MODE_HIDDEN,
	)


## confine 放行判定纯函数（可测）：仅对局准星活跃（未暂停 + 系统光标隐藏）时生效；
## 暂停/非准星态必须放行，否则暂停后鼠标无法移出窗口点系统关闭按钮退出游戏
static func _trap_enabled(
	mouse_lock: bool,
	window_visible: bool,
	focused: bool,
	has_size: bool,
	not_paused: bool,
	mouse_hidden: bool,
) -> bool:
	return mouse_lock and window_visible and focused and has_size and not_paused and mouse_hidden


func _trap() -> void:
	if not _trap_active() or _last_known_pos.x < 0.0 or _last_known_pos.y < 0.0:
		return
	var win := get_window()
	Input.warp_mouse(Vector2(win.get_position()) + _warp_target(_last_known_pos, win.size))


## 每帧防御：已知位置经 clamp 改变（窗口尺寸/位置变化等偶发越界）时即时拉回
func _trap_if_out_of_bounds() -> void:
	if not _trap_active() or _last_known_pos.x < 0.0 or _last_known_pos.y < 0.0:
		return
	var win := get_window()
	var target := _warp_target(_last_known_pos, win.size)
	if target != _last_known_pos:
		Input.warp_mouse(Vector2(win.get_position()) + target)


## warp 目标：已知窗口内位置 clamp 到内容区边缘内侧 1px（窗口相对坐标）。
## 避免系统判定鼠标仍在窗外造成 exited/warp 循环；窗口最小边假设 ≥ 2px。
static func _warp_target(known_pos: Vector2, win_size: Vector2i) -> Vector2:
	return known_pos.clamp(Vector2.ONE, Vector2(win_size - Vector2i.ONE))
