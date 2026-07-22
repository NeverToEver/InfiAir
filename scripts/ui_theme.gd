class_name UITheme
extends RefCounted
## 全 UI 统一色板与样式 helper（HUD / Sci-Fi FUI：细线、切角、全息青）。
## 各 UI 一律从这里取色/取样式，不再散落硬编码色值。

const PANEL_BG := Color(0.039, 0.063, 0.102, 0.78)  # 面板底 藏青
const PANEL_BORDER := Color(0.0, 0.83, 1.0, 0.5)  # 面板边框 青 1px 细线
const ACCENT := Color(0x00d4ffff)  # 主强调青
const ACCENT_BLUE := Color(0x0080ffff)  # 辅助全息蓝
const ACCENT_GOLD := Color(0xd8a868ff)  # 数值金（RP/最高分/新纪录等关键数值）
const TEXT := Color(0xe0e8f0ff)  # 文字主
const TEXT_DIM := Color(0x8a9bb0ff)  # 文字次
const DANGER := Color(0xff3366ff)  # 警报红
const SUCCESS := Color(0x00ff88ff)  # 成功绿
const BTN_NORMAL := Color(0.039, 0.063, 0.102, 0.4)  # 透明底
const BTN_HOVER := Color(0.0, 0.83, 1.0, 0.12)
const BTN_PRESSED := Color(0.0, 0.83, 1.0, 0.25)
const DIM_BG := Color(0.0, 0.0, 0.0, 0.6)  # 全屏遮罩


## 兼容旧调用（新界面请用 ChamferedPanel）
static func make_panel_style(border_width: int = 1, corner_radius: int = 0, margin: float = 14.0) -> StyleBoxFlat:
	var style := StyleBoxFlat.new()
	style.bg_color = PANEL_BG
	style.border_color = PANEL_BORDER
	style.set_border_width_all(border_width)
	style.set_corner_radius_all(corner_radius)
	style.set_content_margin_all(margin)
	return style


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
