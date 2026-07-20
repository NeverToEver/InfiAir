extends CanvasLayer
## 设置界面：Ctrl 微调模式 / Shift 加速模式 各支持「按住 / 切换」两种方式
## （对齐原作 scenes/settings_scene.py），返回按钮回到暂停面板。
## 模式标志存 GameState（ctrl_toggle_mode / shift_toggle_mode），写入 profile.json。

signal back_pressed

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")

var _ctrl_hold: Button
var _ctrl_toggle: Button
var _shift_hold: Button
var _shift_toggle: Button
var _ctrl_group := ButtonGroup.new()
var _shift_group := ButtonGroup.new()


func _ready() -> void:
	add_to_group("settings_ui")
	visible = false
	var dim := ColorRect.new()
	dim.color = Color(0.0, 0.0, 0.0, 0.6)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(dim)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 28)
	center.add_child(vbox)

	var title := Label.new()
	title.text = "设置"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_override("font", FONT)
	title.add_theme_font_size_override("font_size", 48)
	vbox.add_child(title)

	var ctrl_pair := _make_mode_row(vbox, "Ctrl 微调模式", _ctrl_group)
	_ctrl_hold = ctrl_pair[0]
	_ctrl_toggle = ctrl_pair[1]
	_ctrl_hold.pressed.connect(_on_ctrl_mode.bind(false))
	_ctrl_toggle.pressed.connect(_on_ctrl_mode.bind(true))

	var shift_pair := _make_mode_row(vbox, "Shift 加速模式", _shift_group)
	_shift_hold = shift_pair[0]
	_shift_toggle = shift_pair[1]
	_shift_hold.pressed.connect(_on_shift_mode.bind(false))
	_shift_toggle.pressed.connect(_on_shift_mode.bind(true))

	var back_button := Button.new()
	back_button.text = "返回"
	back_button.custom_minimum_size = Vector2(200.0, 52.0)
	back_button.add_theme_font_override("font", FONT)
	back_button.add_theme_font_size_override("font_size", 26)
	back_button.pressed.connect(_on_back_pressed)
	vbox.add_child(back_button)


## 一行设置：标签 + 「按住 / 切换」互斥按钮对，返回 [按住按钮, 切换按钮]
func _make_mode_row(parent: Container, label_text: String, group: ButtonGroup) -> Array[Button]:
	var row := HBoxContainer.new()
	row.alignment = BoxContainer.ALIGNMENT_CENTER
	row.add_theme_constant_override("separation", 16)
	parent.add_child(row)
	var label := Label.new()
	label.text = label_text
	label.custom_minimum_size = Vector2(240.0, 0.0)
	label.add_theme_font_override("font", FONT)
	label.add_theme_font_size_override("font_size", 26)
	row.add_child(label)
	var hold := _make_mode_button("按住", group)
	var toggle := _make_mode_button("切换", group)
	row.add_child(hold)
	row.add_child(toggle)
	return [hold, toggle]


func _make_mode_button(text: String, group: ButtonGroup) -> Button:
	var b := Button.new()
	b.text = text
	b.toggle_mode = true
	b.button_group = group
	b.custom_minimum_size = Vector2(110.0, 48.0)
	b.add_theme_font_override("font", FONT)
	b.add_theme_font_size_override("font_size", 24)
	return b


## 打开面板并按当前 GameState 标志刷新选中态
func show_settings() -> void:
	_ctrl_hold.set_pressed_no_signal(not GameState.ctrl_toggle_mode)
	_ctrl_toggle.set_pressed_no_signal(GameState.ctrl_toggle_mode)
	_shift_hold.set_pressed_no_signal(not GameState.shift_toggle_mode)
	_shift_toggle.set_pressed_no_signal(GameState.shift_toggle_mode)
	visible = true


func _on_ctrl_mode(toggle_mode: bool) -> void:
	GameState.set_ctrl_toggle_mode(toggle_mode)


func _on_shift_mode(toggle_mode: bool) -> void:
	GameState.set_shift_toggle_mode(toggle_mode)


func _on_back_pressed() -> void:
	visible = false
	back_pressed.emit()
