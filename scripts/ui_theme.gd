class_name UITheme
extends RefCounted
## 全 UI 统一色板与样式 helper（sci-fi 深色青蓝系 + 金色点缀）。
## 各 UI 一律从这里取色/取样式，不再散落硬编码色值。

const PANEL_BG := Color(13.0 / 255.0, 20.0 / 255.0, 34.0 / 255.0, 0.88)
const PANEL_BORDER := Color(80.0 / 255.0, 180.0 / 255.0, 220.0 / 255.0, 0.45)
const ACCENT := Color(0x40c8e0ff)  # 主强调 青
const ACCENT_GOLD := Color(0xd8a868ff)  # 次强调 金（标题/重要数值）
const TEXT := Color(0xe8f0f8ff)  # 文字主
const TEXT_DIM := Color(0x93a4b8ff)  # 文字次
const DANGER := Color(0xe06060ff)  # 危险/警告
const SUCCESS := Color(0x50c88aff)  # 成功
const BTN_NORMAL := Color(0x1a2436ff)
const BTN_HOVER := Color(0x24314aff)
const BTN_PRESSED := Color(0x2b4a5eff)
const DIM_BG := Color(0.0, 0.0, 0.0, 0.6)  # 全屏遮罩


static func make_panel_style(border_width: int = 1, corner_radius: int = 6, margin: float = 14.0) -> StyleBoxFlat:
	var style := StyleBoxFlat.new()
	style.bg_color = PANEL_BG
	style.border_color = PANEL_BORDER
	style.set_border_width_all(border_width)
	style.set_corner_radius_all(corner_radius)
	style.set_content_margin_all(margin)
	return style


## 统一按钮样式：normal/hover/pressed/disabled 三态底 + 文字色。
static func apply_button(button: Button) -> void:
	button.add_theme_stylebox_override("normal", _make_btn_style(BTN_NORMAL))
	button.add_theme_stylebox_override("hover", _make_btn_style(BTN_HOVER))
	button.add_theme_stylebox_override("pressed", _make_btn_style(BTN_PRESSED))
	button.add_theme_stylebox_override("disabled", _make_btn_style(Color(BTN_NORMAL, 0.5)))
	button.add_theme_stylebox_override("focus", StyleBoxEmpty.new())
	button.add_theme_color_override("font_color", TEXT)
	button.add_theme_color_override("font_hover_color", ACCENT)
	button.add_theme_color_override("font_pressed_color", TEXT)
	button.add_theme_color_override("font_disabled_color", Color(TEXT_DIM, 0.5))


static func _make_btn_style(bg: Color) -> StyleBoxFlat:
	var style := StyleBoxFlat.new()
	style.bg_color = bg
	style.border_color = PANEL_BORDER
	style.set_border_width_all(1)
	style.set_corner_radius_all(6)
	style.set_content_margin_all(8.0)
	return style
