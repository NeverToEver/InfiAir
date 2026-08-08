class_name PlayerBuffVisuals
extends Node2D
## M3b：Enemy 迁 C#，sin_fast 静态经脚本资源（gdlint class-load-variable-name：snake_case）
var _enemy_script := load("res://csharp/godot/Enemy.cs")
## Buff 外观反馈：一次性构建全部附件（程序化 Polygon2D/Line2D/Sprite2D，无新增贴图），
## 由 GameState.buffs_changed 信号驱动 refresh() 切换显隐与层数强度。
## 作为 Player 子节点随机体旋转；坐标按基准机体系数 BASE_SHIP_SCALE（0.65，贴图 254px ≈ 165px 翼展
## 三角拦截机，机头朝 -Y）设计，Player 创建本节点时按实际 sprite 缩放等比放大。
## 部位锚点与 scripts/tools/generate_player_sprite.py 头部注释的贴图坐标对应（偏移 × 0.65）。

const COLOR_CYAN := Color(0.45, 0.9, 1.0)
const COLOR_GOLD := Color(1.0, 0.85, 0.35)
const COLOR_ORANGE := Color(1.0, 0.55, 0.2)
const COLOR_GREEN := Color(0.45, 1.0, 0.6)
const COLOR_MAGENTA := Color(0.9, 0.35, 0.7)
const COLOR_STEEL := Color(0.5, 0.65, 0.75)
## 附件几何的基准机体缩放（贴图 254px 时的设计机体系数）
const BASE_SHIP_SCALE := 0.65
## 尾焰染色（乘算基色）：高效推进偏绿 / 燃料再生偏金，双 buff 时色相自然混合
const TINT_EFFICIENT := Color(0.75, 1.1, 0.85)
const TINT_RECOVERY := Color(1.15, 1.05, 0.75)
## 层数视觉封顶：无限叠加 buff 的外观强度只表达到第 5 层
const STACK_VISUAL_CAP := 5

var _player: Player

var _power_glow: Polygon2D
var _rapid_fins: Node2D
var _spread_pods: Array[Node2D] = []
var _pierce_spike: Polygon2D
var _explosive_glow: Polygon2D
var _laser_pod: Node2D
var _armor_ring: Line2D
var _regen_ring: Line2D
var _lifesteal_tips: Node2D
var _shield_hex: Line2D
var _evasion_ghost: Sprite2D
var _dash_fins: Node2D
var _slow_ring: Line2D
var _beacon: Polygon2D


## 由 Player._ready() 调用：构建附件、按当前 buff 刷新、监听后续变更
## A7：测试/诊断白盒断言经公开接口（外观节点 getter）
func power_glow() -> Polygon2D:
	return _power_glow


func rapid_fins() -> Node2D:
	return _rapid_fins


func spread_pods() -> Array[Node2D]:
	return _spread_pods


func pierce_spike() -> Polygon2D:
	return _pierce_spike


func explosive_glow() -> Polygon2D:
	return _explosive_glow


func laser_pod() -> Node2D:
	return _laser_pod


func armor_ring() -> Line2D:
	return _armor_ring


func regen_ring() -> Line2D:
	return _regen_ring


func lifesteal_tips() -> Node2D:
	return _lifesteal_tips


func shield_hex() -> Line2D:
	return _shield_hex


func evasion_ghost() -> Sprite2D:
	return _evasion_ghost


func dash_fins() -> Node2D:
	return _dash_fins


func slow_ring() -> Line2D:
	return _slow_ring


func beacon() -> Polygon2D:
	return _beacon


func init(ship_sprite: Sprite2D, player: Player) -> void:
	_player = player
	_build_all(ship_sprite.texture)
	refresh()
	GameState.buffs_changed.connect(refresh)


func _exit_tree() -> void:
	if GameState.buffs_changed.is_connected(refresh):
		GameState.buffs_changed.disconnect(refresh)


func _process(_delta: float) -> void:
	# 仅 refresh() 判定有脉动件可见时才启用处理；动画全部按时间正弦，无每帧分配
	var t := Time.get_ticks_msec() / 1000.0
	if _regen_ring.visible:
		_regen_ring.modulate.a = 0.2 + 0.35 * absf(_enemy_script.SinFast(t * 2.0))
	if _evasion_ghost.visible:
		_evasion_ghost.modulate.a = 0.1 + 0.18 * absf(_enemy_script.SinFast(t * 3.0))
	if _beacon.visible:
		_beacon.modulate.a = 1.0 if _enemy_script.SinFast(t * 6.0) > 0.0 else 0.15
	if _slow_ring.visible:
		# 软力场环允许节点缩放脉动（线宽 2→2.5 的失真在半透明环上不可辨）
		var k := 1.0 + 0.22 * (0.5 + 0.5 * float(_enemy_script.SinFast(t * 1.5)))
		_slow_ring.scale = Vector2(k, k)


func refresh() -> void:
	var stacks := GameState.buff_count(&"power_shot")
	_power_glow.visible = stacks > 0
	if stacks > 0:
		var k := 1.0 + 0.12 * mini(stacks, STACK_VISUAL_CAP)
		_power_glow.scale = Vector2(k, k)
		_power_glow.modulate.a = 0.45 + 0.09 * mini(stacks, STACK_VISUAL_CAP)

	stacks = GameState.buff_count(&"rapid_fire")
	_rapid_fins.visible = stacks > 0
	if stacks > 0:
		var fin_color := COLOR_CYAN if stacks < 2 else Color(0.3, 0.8, 1.0)
		for fin in _rapid_fins.get_children():
			(fin as Polygon2D).color = fin_color

	stacks = mini(GameState.buff_count(&"spread_shot"), _spread_pods.size())
	for i in _spread_pods.size():
		_spread_pods[i].visible = i < stacks

	_pierce_spike.visible = GameState.buff_count(&"piercing") > 0
	_explosive_glow.visible = GameState.buff_count(&"explosive") > 0
	_laser_pod.visible = GameState.buff_count(&"laser_beam") > 0
	_lifesteal_tips.visible = GameState.buff_count(&"lifesteal") > 0
	_shield_hex.visible = GameState.buff_count(&"armor") > 0
	_evasion_ghost.visible = GameState.buff_count(&"evasion") > 0
	_dash_fins.visible = GameState.buff_count(&"phase_dash") > 0
	_slow_ring.visible = GameState.buff_count(&"slow_field") > 0
	_beacon.visible = GameState.buff_count(&"mothership_recall") > 0

	stacks = GameState.buff_count(&"extra_life")
	_armor_ring.visible = stacks > 0
	if stacks > 0:
		_armor_ring.width = 2.0 + 0.6 * mini(stacks, STACK_VISUAL_CAP)

	_regen_ring.visible = GameState.buff_count(&"regen") > 0

	# 尾焰染色：player 每帧用 engine_tint 乘算基色
	var tint := Color(1.0, 1.0, 1.0)
	if GameState.buff_count(&"efficient_boost") > 0:
		tint *= TINT_EFFICIENT
	if GameState.buff_count(&"boost_recovery") > 0:
		tint *= TINT_RECOVERY
	_player.engine_tint = tint

	set_process(_regen_ring.visible or _evasion_ghost.visible or _beacon.visible or _slow_ring.visible)


func _build_all(ship_texture: Texture2D) -> void:
	# 机头炮口金色辉光（power_shot）
	_power_glow = _make_circle(14.0, Color(COLOR_GOLD, 0.55))
	_power_glow.position = Vector2(0.0, -74.0)
	add_child(_power_glow)

	# 引擎舱散热鳍（rapid_fire）
	_rapid_fins = Node2D.new()
	var fin_l := _make_poly(PackedVector2Array([Vector2(-2, -12), Vector2(-10, 10), Vector2(2, 10)]), COLOR_CYAN)
	fin_l.position = Vector2(-18.0, 50.0)
	_rapid_fins.add_child(fin_l)
	var fin_r := _make_poly(PackedVector2Array([Vector2(2, -12), Vector2(10, 10), Vector2(-2, 10)]), COLOR_CYAN)
	fin_r.position = Vector2(18.0, 50.0)
	_rapid_fins.add_child(fin_r)
	add_child(_rapid_fins)

	# 翼面挂架炮舱（spread_shot，每层 1 个，左右交替后居中）
	var pod_positions: Array[Vector2] = [Vector2(-40.0, 16.0), Vector2(40.0, 16.0), Vector2(0.0, -56.0)]
	for pod_pos in pod_positions:
		var pod := Node2D.new()
		pod.position = pod_pos
		var body := _make_poly(PackedVector2Array([Vector2(-7, -10), Vector2(7, -10), Vector2(7, 10), Vector2(-7, 10)]), COLOR_STEEL)
		pod.add_child(body)
		var barrel := _make_poly(PackedVector2Array([Vector2(-2, -22), Vector2(2, -22), Vector2(2, -10), Vector2(-2, -10)]), COLOR_CYAN)
		pod.add_child(barrel)
		add_child(pod)
		_spread_pods.append(pod)

	# 机头穿甲尖刺（piercing）
	_pierce_spike = _make_poly(PackedVector2Array([Vector2(0, -102), Vector2(6, -74), Vector2(-6, -74)]), Color(0.55, 0.95, 1.0))
	add_child(_pierce_spike)

	# 机腹弹舱辉光（explosive，压底不盖机体）
	_explosive_glow = _make_circle(18.0, Color(COLOR_ORANGE, 0.45))
	_explosive_glow.position = Vector2(0.0, 34.0)
	_explosive_glow.z_index = -1
	add_child(_explosive_glow)

	# 背部激光发射基座（laser_beam，座舱后方脊线）
	_laser_pod = Node2D.new()
	_laser_pod.position = Vector2(0.0, 10.0)
	var pod_body := _make_poly(
		PackedVector2Array([Vector2(-6, -11), Vector2(6, -11), Vector2(6, 11), Vector2(-6, 11)]), Color(0.35, 0.45, 0.55)
	)
	_laser_pod.add_child(pod_body)
	var lens := _make_circle(3.5, Color(0.6, 0.95, 1.0))
	lens.position = Vector2(0.0, -11.0)
	_laser_pod.add_child(lens)
	add_child(_laser_pod)

	# 装甲环（extra_life，层数加粗）
	_armor_ring = _make_ring(78.0, 2.0, Color(0.6, 0.85, 1.0, 0.55))
	add_child(_armor_ring)

	# 呼吸光环（regen）
	_regen_ring = _make_ring(88.0, 2.0, Color(COLOR_GREEN, 0.4))
	add_child(_regen_ring)

	# 翼尖三角（lifesteal）
	_lifesteal_tips = Node2D.new()
	var tip_l := _make_poly(PackedVector2Array([Vector2(-10, 0), Vector2(0, -7), Vector2(0, 7)]), COLOR_MAGENTA)
	tip_l.position = Vector2(-72.0, 46.0)
	_lifesteal_tips.add_child(tip_l)
	var tip_r := _make_poly(PackedVector2Array([Vector2(10, 0), Vector2(0, -7), Vector2(0, 7)]), COLOR_MAGENTA)
	tip_r.position = Vector2(72.0, 46.0)
	_lifesteal_tips.add_child(tip_r)
	add_child(_lifesteal_tips)

	# 六边形护盾弧（armor）
	_shield_hex = _make_ring(96.0, 2.0, Color(0.5, 0.9, 1.0, 0.3), 6)
	add_child(_shield_hex)

	# 残像覆盖层（evasion）：独立 Sprite2D，不占用主 sprite 的无敌帧 alpha
	_evasion_ghost = Sprite2D.new()
	_evasion_ghost.texture = ship_texture
	_evasion_ghost.scale = Vector2.ONE * (BASE_SHIP_SCALE + 0.02)
	_evasion_ghost.modulate = Color(0.6, 0.95, 1.0, 0.2)
	_evasion_ghost.z_index = -1
	add_child(_evasion_ghost)

	# 尾部相位鳍（phase_dash）
	_dash_fins = Node2D.new()
	var dfin_l := _make_poly(PackedVector2Array([Vector2(-8, 12), Vector2(0, -6), Vector2(2, 12)]), Color(0.4, 0.8, 1.0))
	dfin_l.position = Vector2(-14.0, 64.0)
	_dash_fins.add_child(dfin_l)
	var dfin_r := _make_poly(PackedVector2Array([Vector2(8, 12), Vector2(0, -6), Vector2(-2, 12)]), Color(0.4, 0.8, 1.0))
	dfin_r.position = Vector2(14.0, 64.0)
	_dash_fins.add_child(dfin_r)
	add_child(_dash_fins)

	# 慢速力场环（slow_field，半径脉动在 _process）
	_slow_ring = _make_ring(104.0, 2.0, Color(0.55, 0.8, 1.0, 0.35))
	add_child(_slow_ring)

	# 机顶信标（mothership_recall，座舱前方）
	_beacon = _make_circle(4.0, Color(1.0, 0.4, 0.35))
	_beacon.position = Vector2(0.0, -36.0)
	add_child(_beacon)

	# 初始全部隐藏，由 refresh() 统一驱动
	for node: Node2D in [
		_power_glow,
		_rapid_fins,
		_pierce_spike,
		_explosive_glow,
		_laser_pod,
		_armor_ring,
		_regen_ring,
		_lifesteal_tips,
		_shield_hex,
		_evasion_ghost,
		_dash_fins,
		_slow_ring,
		_beacon,
	]:
		node.hide()
	for pod in _spread_pods:
		pod.hide()


func _make_circle(radius: float, color: Color, segments: int = 16) -> Polygon2D:
	var pts := PackedVector2Array()
	for i in segments:
		var a := TAU * float(i) / float(segments)
		pts.append(Vector2(cos(a), sin(a)) * radius)
	return _make_poly(pts, color)


func _make_poly(points: PackedVector2Array, color: Color) -> Polygon2D:
	var poly := Polygon2D.new()
	poly.polygon = points
	poly.color = color
	return poly


func _make_ring(radius: float, width: float, color: Color, segments: int = 28) -> Line2D:
	var ring := Line2D.new()
	ring.width = width
	ring.default_color = color
	ring.closed = true
	for i in segments:
		var a := TAU * float(i) / float(segments)
		ring.add_point(Vector2(cos(a), sin(a)) * radius)
	return ring
