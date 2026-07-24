extends CanvasLayer
## 全局退出确认窗（复用组件，设计见 docs/EXIT_FLOW.md）。
## normal/battle 双模式：battle 模式显示进度损失警告（战斗中退出路径：
## 暂停 →「退出游戏」→ 本窗，构成二次确认）。确认后统一执行退出前清理：
## profile 落盘 → 战斗中删档（放弃对局）→ 资源 hook → 淡出 0.3s → quit。
## Esc/手柄 B 取消由 BackNavigator 路由到 cancel()。


var _msg_label: Label
var _ok_button: Button
var _cancel_button: Button
var _plate: ChamferedPanel
var _battle: bool = false
var _exiting: bool = false


func _ready() -> void:
	visible = false
	var dim := ColorRect.new()
	dim.color = UITheme.DIM_BG
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(dim)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	_plate = ChamferedPanel.new()
	_plate.custom_minimum_size = Vector2(560.0, 320.0)
	_plate.brackets = true
	center.add_child(_plate)

	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	_plate.add_child(margin)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 24)
	vbox.alignment = BoxContainer.ALIGNMENT_CENTER
	margin.add_child(vbox)

	var title := UITheme.make_label(tr("EXIT_TITLE"), UITheme.FONT_TITLE, UITheme.ACCENT)
	vbox.add_child(title)

	_msg_label = UITheme.make_label("", UITheme.FONT_BODY, UITheme.TEXT)
	vbox.add_child(_msg_label)

	var row := HBoxContainer.new()
	row.alignment = BoxContainer.ALIGNMENT_CENTER
	row.add_theme_constant_override("separation", 24)
	vbox.add_child(row)

	_cancel_button = _make_button(tr("EXIT_CANCEL"))
	_cancel_button.pressed.connect(cancel)
	row.add_child(_cancel_button)

	_ok_button = _make_button(tr("EXIT_OK"))
	_ok_button.add_theme_color_override("font_color", UITheme.DANGER)
	_ok_button.add_theme_color_override("font_hover_color", UITheme.DANGER)
	_ok_button.pressed.connect(_on_ok_pressed)
	row.add_child(_ok_button)

	GameState.locale_changed.connect(_refresh_texts)


func _make_button(text: String) -> Button:
	var button := UITheme.make_button(text)
	button.custom_minimum_size = Vector2(200.0, 56.0)
	return button


## 打开确认窗；battle=true 时显示进度损失警告（战斗中退出路径）
func show_confirm(battle: bool = false) -> void:
	_battle = battle
	_refresh_texts()
	visible = true
	UITheme.animate_open(_plate)
	# 默认焦点在「取消」（安全侧），防止误按 Enter 直接退出
	_cancel_button.grab_focus()


func _refresh_texts() -> void:
	_msg_label.text = tr("EXIT_BATTLE_MSG") if _battle else tr("EXIT_MSG")
	_msg_label.add_theme_color_override(
		"font_color", UITheme.DANGER if _battle else UITheme.TEXT
	)
	_ok_button.text = tr("EXIT_OK")
	_cancel_button.text = tr("EXIT_CANCEL")


## 取消退出（Esc/手柄 B 由 BackNavigator 路由到这里）
func cancel() -> void:
	if _exiting:
		return
	visible = false


func _on_ok_pressed() -> void:
	if _exiting:
		return
	_exiting = true
	_execute_exit_cleanup(_battle)
	_fade_and_quit()


## 退出前统一清理（测试可直接调用断言副作用）：
## 档案落盘；战斗中退出 = 放弃对局（删档，与死亡语义一致）；开始面板退出保留存档
func _execute_exit_cleanup(battle: bool) -> void:
	GameState.save_profile()
	if battle:
		GameState.delete_save()
	_on_exit_cleanup()


## 退出前资源/连接清理 hook：本项目无网络代码，保留单点以便将来接入（网络断开等）
func _on_exit_cleanup() -> void:
	pass


## 短暂过渡动画（淡出黑屏 0.3s）后退出，避免突兀切进程
func _fade_and_quit() -> void:
	var fade_layer := CanvasLayer.new()
	fade_layer.layer = 90
	add_child(fade_layer)
	var fade := ColorRect.new()
	fade.color = Color(0.0, 0.0, 0.0, 0.0)
	fade.set_anchors_preset(Control.PRESET_FULL_RECT)
	fade_layer.add_child(fade)
	var tween := create_tween()
	tween.tween_property(fade, "color:a", 1.0, 0.3)
	await tween.finished
	get_tree().quit()
