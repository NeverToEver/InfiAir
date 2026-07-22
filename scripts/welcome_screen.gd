extends CanvasLayer
## 欢迎页：进游戏第一屏（仅装机后首次启动显示，welcome_seen 持久化在 profile；
## static 标记兜底进程内死亡重开/教程返回不重复出现）。
## 星空背景上展示标题/最高分/操作提示，按任意键或点击后进入开始面板
## （难度/继续对局选择）。显示期间暂停游戏；跳过时完全不影响 start_panel
## 既有的自显/读档恢复流程。

const FONT: FontFile = preload("res://assets/fonts/msyh.ttc")

static var _entry_shown: bool = false  # reload_current_scene 不重置 static，保证死亡重开不再迎

var _subtitle_label: Label
var _high_score_label: Label
var _controls_label: Label
var _prompt_label: Label


func _ready() -> void:
	if _entry_shown or GameState.welcome_seen:
		visible = false
		return
	_entry_shown = true
	_build_ui()
	_refresh_texts()
	GameState.locale_changed.connect(_refresh_texts)
	get_tree().paused = true
	visible = true
	# 开始面板此时可能已自显（无存档）：先藏起，欢迎页关闭后再正式 show_panel()
	get_parent().get_node("StartPanel").visible = false


func _build_ui() -> void:
	var dim := ColorRect.new()
	dim.color = Color(0.008, 0.016, 0.047, 0.72)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(dim)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	var vbox := VBoxContainer.new()
	vbox.alignment = BoxContainer.ALIGNMENT_CENTER
	vbox.add_theme_constant_override("separation", 20)
	center.add_child(vbox)

	var title := Label.new()
	title.text = "InfiAir"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_override("font", FONT)
	title.add_theme_font_size_override("font_size", 120)
	title.add_theme_color_override("font_color", UITheme.ACCENT)
	title.add_theme_color_override("font_shadow_color", Color(UITheme.ACCENT_BLUE, 0.45))
	title.add_theme_constant_override("shadow_offset_x", 0)
	title.add_theme_constant_override("shadow_offset_y", 6)
	vbox.add_child(title)

	_subtitle_label = _make_label(32, UITheme.TEXT_DIM)
	vbox.add_child(_subtitle_label)

	var spacer := Control.new()
	spacer.custom_minimum_size = Vector2(0.0, 30.0)
	vbox.add_child(spacer)

	_high_score_label = _make_label(28, UITheme.ACCENT_GOLD)
	vbox.add_child(_high_score_label)

	_controls_label = _make_label(20, UITheme.TEXT_DIM)
	vbox.add_child(_controls_label)

	var spacer2 := Control.new()
	spacer2.custom_minimum_size = Vector2(0.0, 50.0)
	vbox.add_child(spacer2)

	_prompt_label = _make_label(30, UITheme.ACCENT)
	vbox.add_child(_prompt_label)
	# 呼吸闪烁提示（CanvasLayer 为 Always，暂停中 tween 照常播放）
	var tween := _prompt_label.create_tween().set_loops()
	tween.tween_property(_prompt_label, "modulate:a", 0.25, 0.8)
	tween.tween_property(_prompt_label, "modulate:a", 1.0, 0.8)


func _make_label(font_size: int, color: Color) -> Label:
	var label := Label.new()
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.add_theme_font_override("font", FONT)
	label.add_theme_font_size_override("font_size", font_size)
	label.add_theme_color_override("font_color", color)
	return label


func _refresh_texts() -> void:
	_subtitle_label.text = tr("WELCOME_SUBTITLE")
	_controls_label.text = tr("WELCOME_CONTROLS")
	_prompt_label.text = tr("WELCOME_PROMPT")
	_high_score_label.visible = GameState.high_score > 0
	_high_score_label.text = tr("WELCOME_HIGH_SCORE") % GameState.high_score


func _unhandled_input(event: InputEvent) -> void:
	if not visible:
		return
	# Esc/手柄 B 在顶层 = 全局退出确认（BackNavigator 处理），不算"任意键"
	if event.is_action_pressed("ui_cancel"):
		return
	# 退出确认窗打开期间不响应其他按键
	if get_parent().get_node("ExitConfirm").visible:
		return
	var pressed := false
	if event is InputEventKey:
		pressed = event.pressed and not event.echo
	elif event is InputEventMouseButton:
		pressed = event.pressed
	if pressed:
		get_viewport().set_input_as_handled()
		dismiss()


## 关闭欢迎页并进入开始面板（测试也走此入口，替代原 start_panel 自显路径）。
## 首次展示后写入 welcome_seen，之后启动直达开始面板。
func dismiss() -> void:
	if not visible:
		return
	visible = false
	GameState.welcome_seen = true
	GameState.save_profile()
	var start_panel: CanvasLayer = get_parent().get_node("StartPanel")
	start_panel.show_panel()
