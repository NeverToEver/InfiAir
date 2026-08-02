extends CanvasLayer
## 开始面板：难度三选一（易/中/难，profile 持久化）+ 继续对局 / 新游戏。
## 无论有无存档，面板显示期间一律暂停游戏（冻结背景，先选再玩）；
## 无存档时开场自显，有存档时由 main 调 show_panel()。
## 布局（正规化标题页）：左上品牌区（DISPLAY 大标题 + accent 短横 + 副标题/副信息），
## 左中切角菜单板（难度区 + 全宽主/次按钮列），右侧装饰雷达（StartRadar 慢扫描），
## 左下角落操作提示；遮罩降低透明度让星空隐约透出。

signal continue_chosen
signal new_game_chosen

var _hint_label: Label
var _corrupt_label: Label
var _high_score_label: Label
var _board_label: Label
var _subtitle_label: Label
var _diff_label: Label
var _continue_button: Button
var _new_button: Button
var _diff_buttons: Dictionary = {}  # StringName -> Button
var _diff_group := ButtonGroup.new()
var _tutorial_button: Button
var _settings_button: Button
var _plate: ChamferedPanel
var _content: VBoxContainer
var _hero: VBoxContainer


func _ready() -> void:
	visible = false
	# 全遮光罩：开始页是独立标题屏，不透出对局画面（避免「暂停后继续玩」的错觉）
	var dim := ColorRect.new()
	dim.color = Color(0.018, 0.03, 0.055, 1.0)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(dim)
	add_child(StartBackdrop.new())  # 装饰星空（界面元素，非游玩背景）

	# 右侧装饰雷达（先加，压在菜单板之下）；绝对定位中心 (1420,480)，与菜单板同水平线。
	# 不用 PRESET_CENTER：其锚点语义会把 position 加成到屏幕中心，导致雷达右缘被裁
	var radar := StartRadar.new()
	radar.position = Vector2(1140.0, 200.0)
	radar.custom_minimum_size = Vector2(560.0, 560.0)
	radar.size = Vector2(560.0, 560.0)
	add_child(radar)

	# 左上品牌区：超大标题 + accent 短横 + 副标题 + 副信息行
	_hero = VBoxContainer.new()
	_hero.set_anchors_preset(Control.PRESET_TOP_LEFT)
	_hero.position = Vector2(140.0, 150.0)
	_hero.custom_minimum_size = Vector2(900.0, 0.0)
	_hero.add_theme_constant_override("separation", 10)
	add_child(_hero)

	var title := UITheme.make_label("InfiAir", UITheme.FONT_DISPLAY, UITheme.ACCENT, HORIZONTAL_ALIGNMENT_LEFT)
	_hero.add_child(title)

	var accent_line := ColorRect.new()
	accent_line.color = UITheme.ACCENT
	accent_line.custom_minimum_size = Vector2(120.0, 4.0)
	accent_line.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN  # 装饰短横，不随容器拉满
	_hero.add_child(accent_line)
	# 标题呼吸微光：短横透明度缓慢起伏（面板 process_mode=Always，暂停态也生效）
	var pulse := accent_line.create_tween().set_loops()
	pulse.tween_property(accent_line, "modulate:a", 0.35, 1.6)
	pulse.tween_property(accent_line, "modulate:a", 1.0, 1.6)

	_subtitle_label = UITheme.make_label("", UITheme.FONT_HEADER, UITheme.TEXT_DIM, HORIZONTAL_ALIGNMENT_LEFT)
	_hero.add_child(_subtitle_label)

	_hint_label = UITheme.make_label("", UITheme.FONT_BODY, UITheme.TEXT_DIM, HORIZONTAL_ALIGNMENT_LEFT)
	_hero.add_child(_hint_label)

	_high_score_label = UITheme.make_label("", UITheme.FONT_BODY, UITheme.ACCENT_GOLD, HORIZONTAL_ALIGNMENT_LEFT)
	_hero.add_child(_high_score_label)

	# P0-3：本地排行榜 Top 3（空榜隐藏）
	_board_label = UITheme.make_label("", UITheme.FONT_CAPTION, UITheme.TEXT_DIM, HORIZONTAL_ALIGNMENT_LEFT)
	_hero.add_child(_board_label)

	# 损坏存档提示（仅 GameState.save_corrupt 时可见）
	_corrupt_label = UITheme.make_label("", UITheme.FONT_CAPTION, UITheme.DANGER, HORIZONTAL_ALIGNMENT_LEFT)
	_hero.add_child(_corrupt_label)

	# 左中菜单板：切角面板，难度区 + 全宽按钮列（顶部避开品牌区，高度贴合内容）
	_plate = ChamferedPanel.new()
	_plate.custom_minimum_size = Vector2(500.0, 400.0)
	_plate.brackets = true
	_plate.set_anchors_preset(Control.PRESET_CENTER_LEFT)
	_plate.position = Vector2(140.0, -95.0)
	add_child(_plate)

	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	# 内容内缩，避开面板边框与括号角标（分组标题此前贴着左上角标）
	margin.add_theme_constant_override("margin_left", 24)
	margin.add_theme_constant_override("margin_right", 24)
	margin.add_theme_constant_override("margin_top", 20)
	margin.add_theme_constant_override("margin_bottom", 20)
	_plate.add_child(margin)

	_content = VBoxContainer.new()
	_content.add_theme_constant_override("separation", 16)
	margin.add_child(_content)

	# 难度三选一（互斥，当前选中高亮），分组标题统一 section header 风格
	var diff_header := UITheme.make_section_header(tr("START_DIFFICULTY"))
	_diff_label = diff_header.get_child(0) as Label
	_content.add_child(diff_header)
	var diff_row := HBoxContainer.new()
	diff_row.add_theme_constant_override("separation", 12)
	_content.add_child(diff_row)
	for d in GameState.DIFFICULTY_ORDER:
		var b := Button.new()
		b.text = tr("DIFF_" + String(d).to_upper())
		b.toggle_mode = true
		b.button_group = _diff_group
		b.custom_minimum_size = Vector2(120.0, 52.0)
		b.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		b.add_theme_font_override("font", UITheme.FONT)
		b.add_theme_font_size_override("font_size", UITheme.FONT_BODY)
		UITheme.apply_button(b)
		b.pressed.connect(_on_difficulty_pressed.bind(d))
		diff_row.add_child(b)
		_diff_buttons[d] = b

	# 按钮列：主按钮（有存档=继续对局，无=开始游戏）primary，其余 secondary，全宽通排
	var buttons := VBoxContainer.new()
	buttons.add_theme_constant_override("separation", 12)
	_content.add_child(buttons)

	# C26：初始化即用 tr()（_refresh_texts 会再覆盖，但避免源码裸中文串）
	_continue_button = UITheme.make_button(tr("START_CONTINUE"), true)
	_continue_button.custom_minimum_size = Vector2(0.0, 64.0)
	_continue_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_continue_button.pressed.connect(_on_continue_pressed)
	buttons.add_child(_continue_button)

	_new_button = UITheme.make_button(tr("START_NEW"))
	_new_button.custom_minimum_size = Vector2(0.0, 56.0)
	_new_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_new_button.pressed.connect(_on_new_game_pressed)
	buttons.add_child(_new_button)

	_tutorial_button = UITheme.make_button(tr("START_TUTORIAL"))
	_tutorial_button.custom_minimum_size = Vector2(0.0, 56.0)
	_tutorial_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_tutorial_button.pressed.connect(_on_tutorial_pressed)
	GameState.locale_changed.connect(func() -> void: _refresh_texts())
	buttons.add_child(_tutorial_button)

	_settings_button = UITheme.make_button("")
	_settings_button.custom_minimum_size = Vector2(0.0, 56.0)
	_settings_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	buttons.add_child(_settings_button)
	_settings_button.pressed.connect(_on_settings_pressed)

	# 右下角落操作提示（避开左下 HUD 仪表区）
	var esc_hint := UITheme.make_label(tr("START_ESC_HINT"), UITheme.FONT_CAPTION, UITheme.TEXT_DIM, HORIZONTAL_ALIGNMENT_RIGHT)
	esc_hint.set_anchors_preset(Control.PRESET_BOTTOM_RIGHT)
	esc_hint.position = Vector2(-260.0, -50.0)
	esc_hint.custom_minimum_size = Vector2(200.0, 0.0)
	add_child(esc_hint)
	GameState.locale_changed.connect(func() -> void: esc_hint.text = tr("START_ESC_HINT"))

	# 无存档时开场自显；有存档时等 main 调 show_panel()
	if not GameState.has_save():
		show_panel()


## 显示面板并暂停游戏：开场自显与存档恢复共用此路径。
## 先校验存档（损坏的已被 GameState 隔离，has_save 随之变 false），再把主按钮聚焦，
## 保证键盘-only 链路（Enter/Space 直接触发主按钮）可达。
func show_panel() -> void:
	if GameState.has_save():
		GameState.load_run_data()  # 校验用：损坏存档在此被隔离备份
	var has_save := GameState.has_save()
	_refresh_texts()
	_continue_button.visible = has_save
	_refresh_difficulty_buttons()
	get_tree().paused = true
	visible = true
	UITheme.animate_open(_plate)
	UITheme.stagger_open(_content)
	UITheme.animate_open(_hero)
	UITheme.stagger_open(_hero)
	if has_save:
		_continue_button.grab_focus()
	else:
		_new_button.grab_focus()


## 主按钮重获焦点（退出确认窗取消后由 BackNavigator 调用）
func grab_primary_focus() -> void:
	if _continue_button.visible:
		_continue_button.grab_focus()
	else:
		_new_button.grab_focus()


## A7：测试/诊断经公开接口（动作包装）
func press_new_game() -> void:
	_on_new_game_pressed()


func press_continue() -> void:
	_on_continue_pressed()


## A7 遗留清理：公开动作方法（测试/诊断走公开接口，替代 _on_settings_pressed 直调）
func press_settings() -> void:
	_on_settings_pressed()


func dismiss() -> void:
	_dismiss()


func new_button() -> Button:
	return _new_button


func continue_button() -> Button:
	return _continue_button


func tutorial_button() -> Button:
	return _tutorial_button


func corrupt_label() -> Label:
	return _corrupt_label


func _dismiss() -> void:
	visible = false
	get_tree().paused = false


func _refresh_texts() -> void:
	var has_save := GameState.has_save()
	_subtitle_label.text = tr("START_SUBTITLE")
	_hint_label.text = tr("START_HAS_SAVE") if has_save else tr("START_NO_SAVE")
	_high_score_label.visible = GameState.high_score > 0
	_high_score_label.text = tr("WELCOME_HIGH_SCORE") % GameState.high_score
	# P0-3：开始页榜单 Top 3（空榜隐藏）
	var board := GameState.highscores_text(3)
	_board_label.visible = board != ""
	_board_label.text = tr("START_BOARD") + "\n" + board
	_corrupt_label.visible = GameState.save_corrupt
	_corrupt_label.text = tr("START_SAVE_CORRUPT")
	_continue_button.text = tr("START_CONTINUE")
	_new_button.text = tr("START_NEW") if has_save else tr("START_BEGIN")
	_tutorial_button.text = tr("START_TUTORIAL_DONE") if GameState.tutorial_done else tr("START_TUTORIAL")
	# E02/G03 + P1-6：有进行中存档时禁用教程按钮——重进教程 tutorial._ready 会无条件
	# delete_save()，静默删掉进行中存档；**已通关且无存档时放行**（P1-6 重看教程，
	# delete_save 删空档无副作用）。禁用 + _on_tutorial_pressed 守卫双保险
	_tutorial_button.disabled = GameState.has_save()
	_settings_button.text = tr("START_SETTINGS")
	_diff_label.text = tr("START_DIFFICULTY")
	# D04：难度按钮文案走翻译键（与 HUD difficulty_label 同口径），locale 切换时一并刷新
	for d in _diff_buttons:
		(_diff_buttons[d] as Button).text = tr("DIFF_" + String(d).to_upper())
	# 主按钮层级：有存档=继续对局 primary，无存档=开始游戏 primary
	if has_save:
		UITheme.apply_primary_button(_continue_button)
		UITheme.apply_button(_new_button)
		_new_button.add_theme_font_size_override("font_size", UITheme.FONT_BODY)
	else:
		UITheme.apply_primary_button(_new_button)


func _refresh_difficulty_buttons() -> void:
	for d in _diff_buttons:
		(_diff_buttons[d] as Button).set_pressed_no_signal(GameState.difficulty == d)


func _on_difficulty_pressed(d: StringName) -> void:
	GameState.set_difficulty(d)


func _on_continue_pressed() -> void:
	_dismiss()
	continue_chosen.emit()


func _on_new_game_pressed() -> void:
	GameState.delete_save()
	_dismiss()
	new_game_chosen.emit()


func _on_tutorial_pressed() -> void:
	# E02/G03：存在进行中存档时禁入教程（UI 已禁用按钮，此处兜底防键盘/程序化调用）——
	# 重进会触发 tutorial._ready 的 delete_save()，静默删掉进行中存档。
	# P1-6：已通关且无存档时放行（重看教程，删空档无副作用）
	if GameState.has_save():
		return
	get_tree().paused = false
	get_tree().change_scene_to_file("res://scenes/tutorial.tscn")


func _on_settings_pressed() -> void:
	var settings := get_tree().get_first_node_in_group("settings_ui")
	if settings != null:
		# 开始面板 layer 高于设置面板，必须先隐藏自己，否则会挡住设置页
		visible = false
		settings.show_settings(self)
