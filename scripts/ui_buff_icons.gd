class_name BuffIcons
extends RefCounted
## Buff 程序化字形图标库：16 种 buff 各一个几何字形（24 单位坐标系按尺寸缩放），
## 供 HUD 图标格（ui_theme.make_buff_tile）与 Buff 三选一卡片共用同一套图形语言。
## 分类配色：进攻=ACCENT 青，维生=SUCCESS 绿，通用=ACCENT_GOLD 金。

const _OFFENSE: Array[StringName] = [
	&"power_shot", &"rapid_fire", &"spread_shot", &"piercing", &"explosive", &"laser_beam"
]
const _SUSTAIN: Array[StringName] = [
	&"extra_life", &"regen", &"lifesteal", &"armor", &"evasion"
]
# 其余归入通用：phase_dash / slow_field / efficient_boost / boost_recovery / mothership_recall


## 分类色：进攻青 / 维生绿 / 通用金
static func color_for(id: StringName) -> Color:
	if id in _OFFENSE:
		return UITheme.ACCENT
	if id in _SUSTAIN:
		return UITheme.SUCCESS
	return UITheme.ACCENT_GOLD


## 生成字形控件（px 见方，线宽随尺寸缩放）
static func make_glyph(id: StringName, color: Color, px: float = 22.0) -> Control:
	var glyph := _Glyph.new()
	glyph.glyph_id = id
	glyph.glyph_color = color
	glyph.custom_minimum_size = Vector2(px, px)
	glyph.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return glyph


## 单个字形：_draw 内以 24 单位坐标系绘制，u = size/24 为缩放因子
class _Glyph:
	extends Control
	var glyph_id: StringName
	var glyph_color: Color

	func _draw() -> void:
		var u := size.x / 24.0
		var c := glyph_color
		var w := 2.0 * u
		match glyph_id:
			&"power_shot":
				# 大弹头上指 + 底托线
				draw_colored_polygon(_pts(u, [12, 4, 5, 16, 19, 16]), c)
				_line(u, 7, 20, 17, 20, c, w)
			&"rapid_fire":
				# 三道竖条（高射速）
				for x in [6.0, 12.0, 18.0]:
					_line(u, x, 5, x, 19, c, w)
			&"spread_shot":
				# 同一点三发散射线
				_line(u, 12, 20, 4, 4, c, w)
				_line(u, 12, 20, 12, 3, c, w)
				_line(u, 12, 20, 20, 4, c, w)
			&"extra_life":
				# 菱形急救十字
				draw_polyline(_pts(u, [12, 3, 21, 12, 12, 21, 3, 12, 12, 3]), c, w, true)
				_line(u, 12, 8, 12, 16, c, w)
				_line(u, 8, 12, 16, 12, c, w)
			&"regen":
				# 圆环 + 内十字（持续修复）
				draw_arc(Vector2(12, 12) * u, 9 * u, 0.0, TAU, 24, c, w, true)
				_line(u, 12, 8, 12, 16, c, w)
				_line(u, 8, 12, 16, 12, c, w)
			&"piercing":
				# 箭穿隔板
				_line(u, 3, 12, 20, 12, c, w)
				_line(u, 20, 12, 15, 8, c, w)
				_line(u, 20, 12, 15, 16, c, w)
				_line(u, 10, 6, 10, 18, c, w)
			&"explosive":
				# 八角星芒 + 中心点
				for i in 8:
					var a := TAU * float(i) / 8.0
					var from := Vector2(12, 12) + Vector2(cos(a), sin(a)) * 4.0
					var to := Vector2(12, 12) + Vector2(cos(a), sin(a)) * 9.0
					draw_line(from * u, to * u, c, w, true)
				draw_circle(Vector2(12, 12) * u, 2.0 * u, c)
			&"lifesteal":
				# 血滴：圆弧 + 顶部两侧收拢
				draw_arc(Vector2(12, 14) * u, 5 * u, 0.0, TAU, 20, c, w, true)
				_line(u, 12, 3, 7.5, 11.5, c, w)
				_line(u, 12, 3, 16.5, 11.5, c, w)
			&"armor":
				# 盾形轮廓
				draw_polyline(_pts(u, [5, 4, 19, 4, 19, 11, 12, 20, 5, 11, 5, 4]), c, w, true)
			&"evasion":
				# 双残影方框
				draw_polyline(_pts(u, [4, 8, 4, 18, 14, 18, 14, 8, 4, 8]), Color(c, 0.55), w, true)
				draw_polyline(_pts(u, [10, 5, 20, 5, 20, 15, 10, 15, 10, 5]), c, w, true)
			&"phase_dash":
				# 双箭头快进
				draw_polyline(_pts(u, [4, 4, 11, 12, 4, 20]), c, w, true)
				draw_polyline(_pts(u, [11, 4, 18, 12, 11, 20]), c, w, true)
			&"slow_field":
				# 沙漏（上下横杠 + 交叉斜线）
				_line(u, 6, 4, 18, 4, c, w)
				_line(u, 6, 20, 18, 20, c, w)
				_line(u, 6, 4, 18, 20, c, w)
				_line(u, 18, 4, 6, 20, c, w)
			&"efficient_boost":
				# 上箭头 + 基线（高效推进）
				draw_polyline(_pts(u, [5, 15, 12, 7, 19, 15]), c, w, true)
				_line(u, 7, 20, 17, 20, c, w)
			&"boost_recovery":
				# 闪电（快速充能）
				draw_colored_polygon(_pts(u, [13, 3, 7, 13, 11, 13, 9, 21, 17, 10, 12.5, 10]), c)
			&"mothership_recall":
				# 向下箭头落入托盘（召回）
				_line(u, 12, 3, 12, 13, c, w)
				draw_polyline(_pts(u, [8, 9, 12, 13, 16, 9]), c, w, true)
				draw_polyline(_pts(u, [4, 11, 4, 19, 20, 19, 20, 11]), c, w, true)
			&"laser_beam":
				# 发射镜 + 光束
				draw_polyline(_pts(u, [5, 8.5, 8.5, 12, 5, 15.5, 1.5, 12, 5, 8.5]), c, w, true)
				draw_line(Vector2(10, 12) * u, Vector2(22, 12) * u, c, 3.5 * u, true)
			_:
				# 未登记字形回退：圆环
				draw_arc(Vector2(12, 12) * u, 8 * u, 0.0, TAU, 24, c, w, true)

	## 单位坐标数组 → 像素坐标点列（[x0, y0, x1, y1, ...]）
	func _pts(u: float, flat: Array) -> PackedVector2Array:
		var pts := PackedVector2Array()
		for i in range(0, flat.size(), 2):
			pts.append(Vector2(flat[i] * u, flat[i + 1] * u))
		return pts

	func _line(u: float, x0: float, y0: float, x1: float, y1: float, c: Color, w: float) -> void:
		draw_line(Vector2(x0, y0) * u, Vector2(x1, y1) * u, c, w, true)
