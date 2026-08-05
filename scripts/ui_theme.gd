class_name UITheme
extends RefCounted
## 全 UI 统一色板、字号阶梯与样式工厂（HUD / Sci-Fi FUI：细线、切角、全息青）。
## 各 UI 一律从这里取色/取样式/取控件，不再散落硬编码色值与 Label/Button 样板。

const PANEL_BG := Color(0.039, 0.063, 0.102, 0.78)  # 面板底 藏青
const PANEL_BORDER := Color(0.0, 0.83, 1.0, 0.5)  # 面板边框 青 1px 细线
const ACCENT := Color(0x00d4ffff)  # 主强调青
const ACCENT_BLUE := Color(0x0080ffff)  # 辅助全息蓝
const ACCENT_GOLD := Color(0xd8a868ff)  # 数值金（RP/最高分/新纪录等关键数值）
const ACCENT_DIM := Color(0.0, 0.83, 1.0, 0.22)  # 装饰分隔线/页头短线
const BG_DEEP := Color(0.024, 0.039, 0.067, 0.92)  # 更深面板底（欢迎页/满屏遮罩层）
const TEXT := Color(0xe0e8f0ff)  # 文字主
const TEXT_DIM := Color(0x8a9bb0ff)  # 文字次
const DANGER := Color(0xff3366ff)  # 警报红
const SUCCESS := Color(0x00ff88ff)  # 成功绿
const BTN_NORMAL := Color(0.039, 0.063, 0.102, 0.4)  # 透明底
const BTN_HOVER := Color(0.0, 0.83, 1.0, 0.12)
const BTN_PRESSED := Color(0.0, 0.83, 1.0, 0.25)
const BTN_PRIMARY_BG := Color(0.0, 0.83, 1.0, 0.18)  # 主按钮底（ACCENT 18% alpha）
const DIM_BG := Color(0.006, 0.012, 0.024, 0.84)  # 全屏遮罩：深青黑强压暗，模态层与游戏画面充分分离
const EVENT_MAGENTA := Color(1.0, 0.25, 0.75)  # 随机事件/通讯品红
const WARN_YELLOW := Color(1.0, 0.8, 0.35)  # 蓄力/提示黄
const CHARGE_CYAN := Color(0.5, 0.9, 1.0)  # 蓄力青
const BANNER_DANGER_BG := Color(0.35, 0.06, 0.10, 0.7)  # 警告横幅底

# 虚影基地皮肤 token（docs/RETURN_HOME_CINEMATIC.md §3.2，与现有 token 并存不替换）
const PHANTOM_BG := Color(0.01, 0.03, 0.06, 0.90)  # 基地全屏底（比原 dim 更冷更深）
const PHANTOM_PANEL_BG := Color(0.03, 0.08, 0.12, 0.55)  # 虚影面板底：更透，让背景结构隐约透出
const PHANTOM_BORDER := Color(0.0, 0.83, 1.0, 0.65)  # 虚影面板边框：比 PANEL_BORDER 亮一档
const PHANTOM_SCAN := Color(0.0, 0.83, 1.0, 0.06)  # 扫描线/毛玻璃叠加层

# 字号阶梯：层级靠字号/颜色/透明度区分（字体仅 NotoSansSC.ttf 一款，OFL 开源可分发）
const FONT_DISPLAY := 72  # 超大展示（主标题/结算大数字）
const FONT_TITLE := 40  # 页标题
const FONT_SCORE := 32  # 大数值（得分等）
const FONT_HEADER := 28  # 卡片名/主按钮
const FONT_BODY := 24  # 正文/次按钮
const FONT_HUD_L := 22  # HUD 大字（通讯字幕等）
const FONT_HUD := 20  # HUD 正文
const FONT_CAPTION := 18  # 说明/分组标题/角落提示
const FONT_SMALL := 16  # 小字（芯片/标签）

const FONT: FontFile = preload("res://assets/fonts/NotoSansSC.ttf")

# ---------------- 控件工厂 ----------------


## 统一 Label 工厂：字号走阶梯常量，颜色走色板
static func make_label(
	text: String, size: int = FONT_BODY, color: Color = TEXT, align: HorizontalAlignment = HORIZONTAL_ALIGNMENT_CENTER
) -> Label:
	var label := Label.new()
	label.text = text
	label.horizontal_alignment = align
	label.add_theme_font_override("font", FONT)
	label.add_theme_font_size_override("font_size", size)
	label.add_theme_color_override("font_color", color)
	return label


## 统一按钮工厂。primary=true：ACCENT 底（18% alpha）+ 亮边框 + 较大字号（主操作）；
## 默认 secondary：现有透明底样式（次级操作）。
static func make_button(text: String, primary: bool = false) -> Button:
	var button := Button.new()
	button.text = text
	button.add_theme_font_override("font", FONT)
	if primary:
		apply_primary_button(button)
	else:
		button.add_theme_font_size_override("font_size", FONT_BODY)
		apply_button(button)
	add_button_motion(button)
	return button


## 主按钮样式（动态切换主次层级时可重复调用）
static func apply_primary_button(button: Button) -> void:
	button.add_theme_font_size_override("font_size", FONT_HEADER)
	button.add_theme_stylebox_override("normal", _make_btn_style(BTN_PRIMARY_BG, ACCENT))
	button.add_theme_stylebox_override("hover", _make_btn_style(Color(ACCENT, 0.3), ACCENT))
	button.add_theme_stylebox_override("pressed", _make_btn_style(Color(ACCENT, 0.42), ACCENT))
	button.add_theme_stylebox_override("disabled", _make_btn_style(Color(BTN_PRIMARY_BG, 0.5), Color(ACCENT, 0.4)))
	# 焦点样式与 hover 一致：键盘导航时焦点可见
	button.add_theme_stylebox_override("focus", _make_btn_style(Color(ACCENT, 0.3), ACCENT))
	button.add_theme_color_override("font_color", ACCENT)
	button.add_theme_color_override("font_hover_color", TEXT)
	button.add_theme_color_override("font_pressed_color", TEXT)
	button.add_theme_color_override("font_disabled_color", Color(TEXT_DIM, 0.5))


## 互斥选项按钮（设置页档位列：toggle + ButtonGroup）
static func make_toggle_button(text: String, group: ButtonGroup) -> Button:
	var button := Button.new()
	apply_button(button)
	button.text = text
	button.toggle_mode = true
	button.button_group = group
	button.custom_minimum_size = Vector2(110.0, 48.0)
	button.add_theme_font_override("font", FONT)
	button.add_theme_font_size_override("font_size", FONT_BODY)
	add_button_motion(button)
	return button


## Buff 字形槽（socket）：ChamferedPanel 瓦片，分类色描边 + 同色内框 + 淡色底，
## 中央程序化字形（BuffIcons，与 Buff 三选一卡片同一套图形语言）。
## HUD 图标坞（make_buff_tile）与三选一卡片图标位共用，保证两处视觉一致。
static func make_buff_socket(id: StringName, tile_px: float = 46.0) -> Control:
	var color: Color = BuffIcons.color_for(id)
	var panel := ChamferedPanel.new()
	panel.chamfer = maxf(tile_px * 0.15, 4.0)
	panel.padding = 0.0
	panel.custom_minimum_size = Vector2(tile_px, tile_px)
	# 底 = 藏青面板底向分类色微倾（16%）：暗底不变，色相暗示分类
	panel.bg_color = PANEL_BG.lerp(Color(color, PANEL_BG.a), 0.16)
	panel.border_color = Color(color, 0.7)
	panel.inner_frame = true
	panel.inner_frame_color = Color(color, 0.28)
	panel.mouse_filter = Control.MOUSE_FILTER_IGNORE

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	center.add_child(BuffIcons.make_glyph(id, color, tile_px * 0.57))  # 46→26px / 76→43px，留白一致
	panel.add_child(center)
	return panel


## Buff 图标格：46×46 socket 瓦片（make_buff_socket），层数 >1 时右下角叠一枚
## 切角 ×N 徽标芯片（深底 + 分类色描边 + 金色数字，与滚动栏明细行 ×N 同色）。
## 网格坞内高密度排布用，避免长文芯片互相遮挡。
static func make_buff_tile(id: StringName, stacks: int) -> Control:
	var panel := make_buff_socket(id)
	if stacks > 1:
		var color: Color = BuffIcons.color_for(id)
		var chip := ChamferedPanel.new()
		chip.chamfer = 4.0
		chip.padding = 0.0
		chip.custom_minimum_size = Vector2(24.0, 16.0)
		chip.bg_color = Color(BG_DEEP, 0.95)
		chip.border_color = Color(color, 0.6)
		chip.mouse_filter = Control.MOUSE_FILTER_IGNORE
		chip.set_anchors_preset(Control.PRESET_BOTTOM_RIGHT)
		chip.position = Vector2(-26.0, -18.0)  # 右下 2px 内缩，芯片留在瓦片内
		var badge := make_label("×%d" % stacks, 12, ACCENT_GOLD, HORIZONTAL_ALIGNMENT_CENTER)
		badge.set_anchors_preset(Control.PRESET_FULL_RECT)
		badge.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		badge.mouse_filter = Control.MOUSE_FILTER_IGNORE
		chip.add_child(badge)
		panel.add_child(chip)
	return panel


## 分组标题：小号 CAPTION 标题（左对齐）+ 下方 1px 分隔线（ACCENT_DIM）
static func make_section_header(text: String) -> Control:
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 6)
	box.add_child(make_label(text, FONT_CAPTION, ACCENT, HORIZONTAL_ALIGNMENT_LEFT))
	var line := ColorRect.new()
	line.color = ACCENT_DIM
	line.custom_minimum_size = Vector2(0.0, 1.0)
	line.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	box.add_child(line)
	return box


## 页面骨架：遮罩 dim + CenterContainer + ChamferedPanel(brackets)
## + 页头（标题 + accent 装饰短线 + 分隔线）+ 内容 VBox（separation 16，纵向填满居中）。
## 返回 {"root", "dim", "panel", "margin", "title", "content"}：root 挂到 CanvasLayer 下，内容加进 content。
static func make_page_shell(title_key: String) -> Dictionary:
	var dim := ColorRect.new()
	dim.color = DIM_BG
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	dim.add_child(center)

	var panel := ChamferedPanel.new()
	panel.brackets = true
	center.add_child(panel)

	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	# 内容内缩，避开面板边框与括号角标（ChamferedPanel.padding 只管扩尺寸，不管内容偏移）
	margin.add_theme_constant_override("margin_left", 24)
	margin.add_theme_constant_override("margin_right", 24)
	margin.add_theme_constant_override("margin_top", 20)
	margin.add_theme_constant_override("margin_bottom", 20)
	panel.add_child(margin)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 16)
	margin.add_child(vbox)

	# 页头：标题 + accent 装饰短线 + 通栏分隔线
	var header := VBoxContainer.new()
	header.add_theme_constant_override("separation", 8)
	vbox.add_child(header)
	var title := make_label(TranslationServer.translate(title_key), FONT_TITLE, ACCENT)
	header.add_child(title)
	var accent_line_wrap := HBoxContainer.new()
	accent_line_wrap.alignment = BoxContainer.ALIGNMENT_CENTER
	header.add_child(accent_line_wrap)
	var accent_line := ColorRect.new()
	accent_line.color = ACCENT
	accent_line.custom_minimum_size = Vector2(64.0, 3.0)
	accent_line_wrap.add_child(accent_line)
	var divider := ColorRect.new()
	divider.color = ACCENT_DIM
	divider.custom_minimum_size = Vector2(0.0, 1.0)
	divider.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(divider)

	var content := VBoxContainer.new()
	content.add_theme_constant_override("separation", 16)
	content.alignment = BoxContainer.ALIGNMENT_CENTER
	# 纵向填满页头之下的剩余空间并居中：消除面板底部空荡，按钮组居中构图
	content.size_flags_vertical = Control.SIZE_EXPAND_FILL
	vbox.add_child(content)

	return {"root": dim, "dim": dim, "panel": panel, "margin": margin, "title": title, "content": content}


# ---------------- 动效 ----------------


## 模态统一打开动效：遮罩 150ms 淡入 + 面板 200ms 淡入 + 内容错峰淡入（可选）。
## 各模态页面 open()/show 时调用，替代散落的 animate_open 单面板调用。
static func animate_modal_open(dim: Control, panel: Control, content: Control = null) -> void:
	dim.modulate.a = 0.0
	var dim_tween := dim.create_tween()
	dim_tween.tween_property(dim, "modulate:a", 1.0, 0.15)
	animate_open(panel)
	if content != null:
		stagger_open(content)


## 子项依次 60ms 间隔淡入（只动 modulate.a，不动 position——容器布局会覆盖 position）
static func stagger_open(container: Control) -> void:
	var i := 0
	for child in container.get_children():
		if not (child is Control) or not child.visible:
			continue
		child.modulate.a = 0.0
		var tween := child.create_tween()
		tween.tween_interval(0.06 * i)
		tween.tween_property(child, "modulate:a", 1.0, 0.18)
		i += 1


# ---------------- 基础样式 ----------------


## 统一按钮样式：切角系（直角）——normal 透明底+青边框，
## hover 底 rgba(0,212,255,0.12)+边框提亮，pressed 底 rgba(0,212,255,0.25)。
static func apply_button(button: Button) -> void:
	button.add_theme_stylebox_override("normal", _make_btn_style(BTN_NORMAL, PANEL_BORDER))
	button.add_theme_stylebox_override("hover", _make_btn_style(BTN_HOVER, ACCENT))
	button.add_theme_stylebox_override("pressed", _make_btn_style(BTN_PRESSED, ACCENT))
	button.add_theme_stylebox_override("disabled", _make_btn_style(Color(BTN_NORMAL, 0.5), Color(PANEL_BORDER, 0.4)))
	# 焦点样式与 hover 一致：键盘导航（Tab/方向键 + Enter）时焦点可见
	button.add_theme_stylebox_override("focus", _make_btn_style(BTN_HOVER, ACCENT))
	button.add_theme_color_override("font_color", TEXT)
	button.add_theme_color_override("font_hover_color", ACCENT)
	button.add_theme_color_override("font_pressed_color", TEXT)
	button.add_theme_color_override("font_disabled_color", Color(TEXT_DIM, 0.5))


## 虚影面板材质（§3.2）：更透的全息底 + 亮一档边框（仅基地控制台使用）
static func apply_phantom_panel(panel: ChamferedPanel) -> void:
	panel.bg_color = PHANTOM_PANEL_BG
	panel.border_color = PHANTOM_BORDER


static func _make_btn_style(bg: Color, border: Color) -> StyleBoxFlat:
	var style := StyleBoxFlat.new()
	style.bg_color = bg
	style.border_color = border
	style.set_border_width_all(1)
	style.set_corner_radius_all(0)
	style.set_content_margin_all(8.0)
	return style


## 面板打开微动效：200ms 淡入（不做位移动画——容器布局会覆盖 position）
static func animate_open(control: Control) -> void:
	control.modulate.a = 0.0
	var tween := control.create_tween()
	tween.tween_property(control, "modulate:a", 1.0, 0.2)


## 按钮微动效：hover/焦点 1.02 倍放大、按下 0.98 回弹（pivot 居中，只动 scale 不动布局）。
## 幅度刻意收小：全宽按钮放大过多会溢出面板边框。
## 由 make_button/make_toggle_button 统一挂载；键盘焦点与鼠标 hover 表现一致。
static func add_button_motion(button: Button) -> void:
	var update_pivot := func() -> void: button.pivot_offset = button.size * 0.5
	button.resized.connect(update_pivot)
	update_pivot.call()
	button.mouse_entered.connect(func() -> void: _motion_tween(button, 1.02))
	button.mouse_exited.connect(func() -> void: _motion_tween(button, 1.0))
	button.focus_entered.connect(func() -> void: _motion_tween(button, 1.02))
	button.focus_exited.connect(func() -> void: _motion_tween(button, 1.0))
	button.button_down.connect(func() -> void: _motion_tween(button, 0.98))
	button.button_up.connect(func() -> void: _motion_tween(button, 1.0))


static func _motion_tween(button: Button, target: float) -> void:
	if not is_instance_valid(button):
		return
	# H20（健壮性审核）：互斥——快速进出按钮时旧 tween kill 再建，防同属性竞争抖动
	if button.has_meta("motion_tween"):
		var old: Tween = button.get_meta("motion_tween")
		if old != null and old.is_valid():
			old.kill()
	var tween := button.create_tween()
	button.set_meta("motion_tween", tween)
	tween.tween_property(button, "scale", Vector2(target, target), 0.08)
