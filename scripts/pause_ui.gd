extends CanvasLayer
## Esc 暂停面板：继续 / 保存进度 / 设置 / 退出游戏 / 重开提示。
## 「保存进度」是全局唯一主动存档入口；「设置」打开 Ctrl/Shift 模式面板。
## ui_cancel（Esc/手柄 B）的全局返回路由统一在 BackNavigator（见 docs/EXIT_FLOW.md），
## 本面板只提供 open()/close() 供其调用；「退出游戏」走 ExitConfirm 战斗模式二次确认。

var _resume_button: Button
var _save_button: Button
var _settings_button: Button
var _quit_button: Button
var _title_label: Label
var _hint_label: Label
var _plate: ChamferedPanel
var _content: VBoxContainer
var _settings_ui: CanvasLayer  # 惰性绑定（SettingsUI 的 _ready 晚于本节点）


func _ready() -> void:
	visible = false
	GameState.locale_changed.connect(_on_locale_changed)

	var shell := UITheme.make_page_shell("PAUSE_TITLE")
	add_child(shell["root"])
	_plate = shell["panel"]
	_plate.custom_minimum_size = Vector2(560.0, 480.0)
	_title_label = shell["title"]
	_content = shell["content"]

	_resume_button = UITheme.make_button(tr("PAUSE_RESUME"), true)
	_resume_button.custom_minimum_size = Vector2(280.0, 56.0)
	_resume_button.pressed.connect(close)
	_content.add_child(_resume_button)

	_save_button = UITheme.make_button(tr("PAUSE_SAVE"))
	_save_button.custom_minimum_size = Vector2(280.0, 52.0)
	_save_button.pressed.connect(_on_save_pressed)
	_content.add_child(_save_button)

	_settings_button = UITheme.make_button(tr("PAUSE_SETTINGS"))
	_settings_button.custom_minimum_size = Vector2(280.0, 52.0)
	_settings_button.pressed.connect(_on_settings_pressed)
	_content.add_child(_settings_button)

	_quit_button = UITheme.make_button(tr("PAUSE_QUIT"))
	_quit_button.custom_minimum_size = Vector2(280.0, 52.0)
	_quit_button.pressed.connect(_on_quit_pressed)
	_content.add_child(_quit_button)

	_hint_label = UITheme.make_label(tr("PAUSE_HINT"), UITheme.FONT_CAPTION, UITheme.TEXT_DIM)
	_content.add_child(_hint_label)


func _on_locale_changed() -> void:
	_title_label.text = tr("PAUSE_TITLE")
	_resume_button.text = tr("PAUSE_RESUME")
	_hint_label.text = tr("PAUSE_HINT")
	_settings_button.text = tr("PAUSE_SETTINGS")
	_quit_button.text = tr("PAUSE_QUIT")
	if _save_button.text != tr("PAUSE_SAVED"):
		_save_button.text = tr("PAUSE_SAVE")


func open() -> void:
	_save_button.text = tr("PAUSE_SAVE")
	get_tree().paused = true
	visible = true
	UITheme.animate_open(_plate)
	UITheme.stagger_open(_content)
	_resume_button.grab_focus()


func close() -> void:
	visible = false
	get_tree().paused = false


func toggle() -> void:
	if visible:
		close()
	else:
		open()


func _get_settings_ui() -> CanvasLayer:
	if _settings_ui == null:
		_settings_ui = get_tree().get_first_node_in_group("settings_ui") as CanvasLayer
	return _settings_ui


func _on_settings_pressed() -> void:
	if _get_settings_ui() == null:
		return
	visible = false
	_settings_ui.show_settings(self)


func _on_save_pressed() -> void:
	var player := GameState.player_ref as Player  # A5：走注册表，替代 group 现找
	var spawner := get_tree().get_first_node_in_group("spawner")
	var fuel: float = player.fuel_amount() if player != null else 100.0
	var elapsed: float = spawner.elapsed() if spawner != null else 0.0
	GameState.save_run(fuel, elapsed)
	_save_button.text = tr("PAUSE_SAVED")
	# 用信号连接而非协程：退出时挂起的协程函数状态会泄漏
	var timer := Timer.new()
	timer.one_shot = true
	add_child(timer)  # 本节点 process_mode=Always，暂停中仍计时
	timer.timeout.connect(_reset_save_label, CONNECT_ONE_SHOT)
	timer.timeout.connect(timer.queue_free, CONNECT_ONE_SHOT)
	timer.start(1.0)


func _reset_save_label() -> void:
	_save_button.text = tr("PAUSE_SAVE")


func _on_quit_pressed() -> void:
	# 战斗中退出：ExitConfirm 战斗模式二次确认（带进度损失警告）
	var exit_confirm: CanvasLayer = get_parent().get_node("ExitConfirm")
	exit_confirm.show_confirm(true)


func _unhandled_input(event: InputEvent) -> void:
	# ui_cancel（Esc/手柄 B/Android 返回）的全局路由已移交 BackNavigator；
	# 此处只保留暂停中的 R 重开
	if visible and event.is_action_pressed("restart"):
		get_tree().paused = false
		GameState.reset_run()
		get_tree().reload_current_scene()
