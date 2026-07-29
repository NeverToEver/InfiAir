class_name Player
extends CharacterBody2D
## 玩家战机：WASD 平滑移动，朝鼠标旋转（带瞄准辅助磁吸锁定），全自动开火，
## Shift 消耗燃料加速，Ctrl 微调（×0.35），空格相位冲刺（需解锁 buff，耗 25% 燃料）。

const FIRE_SOUNDS: Array[AudioStream] = [
	preload("res://assets/audio/bullet_fire.wav"),
	preload("res://assets/audio/bullet_fire_b.wav"),
	preload("res://assets/audio/bullet_fire_c.wav"),
]

var MAX_SPEED := 420.0
var ACCEL := 2400.0
var DECEL := 1800.0
var BOOST_MULT := 1.8
var BASE_FIRE_INTERVAL := 0.15
var BULLET_SPEED := 1200.0
var BULLET_SPREAD_DEG := 15.0
## 单发弹伤基底（对齐原作 BULLET_DAMAGE=10 口径；power_shot 每层 ×1.25 乘算）
var BULLET_DAMAGE := 10
var INVINCIBLE_TIME := 1.5
## 出生保护（对齐原作入场动画 60 帧≈1.0s 短路游戏逻辑的等效保护）
var SPAWN_INVINCIBLE_TIME := 1.0
## 受击清弹半径（对齐原作 BULLET_CLEAR_RADIUS=250）
var BULLET_CLEAR_RADIUS := 250.0
## 护甲：固定 ×0.85 减伤（对齐原作 ARMOR_MULTIPLIER，二元 buff 全伤害源生效）
var ARMOR_MULT := 0.85
## 闪避：20% 完全闪避（对齐原作 EVASION_CHANCE，二元 buff 全伤害源生效）
var EVASION_CHANCE := 0.2
## 自我修复 buff：每秒回 2 HP（对齐原作 RegenerationBuff，二元）
var REGEN_PER_SEC := 2.0
var SHAKE_HIT := 12.0

var FUEL_DRAIN := 35.0
var FUEL_REGEN := 20.0
var FUEL_RESTART := 30.0

## 尾焰染色乘区（Buff 外观反馈：高效推进/燃料再生写入，每帧乘算进尾焰基色）
var engine_tint := Color(1.0, 1.0, 1.0)

var DASH_DISTANCE := 200.0
var DASH_TIME := 0.25
var DASH_COOLDOWN := 4.0
var AFTERIMAGE_INTERVAL := 0.08
var DASH_FUEL_RATIO := 0.25  # 冲刺消耗满值燃料的 25%（对齐原作 phase_dash COST_RATIO）

var AIM_RING_RADIUS := 26.0  # 锁定环半径下限
## 瞄准辅助当前档位参数（balance.json player.aim_assist.levels + GameState.aim_assist_level，信号联动刷新）
var _aim_radius := 230.0  # 磁吸/粘滞半径
var _aim_break := 140.0  # 甩动无方向候选且位移超过则脱锁
var _aim_switch := 90.0  # 单帧位移超过则尝试沿甩动方向锥形切换目标
var _aim_cone_dot := 0.42  # 方向切换锥形阈值（夹角余弦）
var _aim_pull := 1.0  # 吸附力度：<1 时准星仅被部分拉向目标（弱档）
var FINE_MOVE_MULT := 0.35  # Ctrl 微调（对齐原作 PRECISION_SPEED_MULT）

var fuel_max: float = 100.0  # 扩容油箱天赋可提升
var _input_locked: bool = false  # 返航过场期间锁定
## 原 Boss 狂暴定身字段（原作 is_controls_locked）：阶段 A 起狂暴改用 _enrage_slow 减速
## （BOSS_REDESIGN §4.3），本字段保留兼容，Boss 不再写它。
var movement_locked: bool = false
## Boss 狂暴减速乘区（§4.3）：TRANSITION+ACTIVE 期间 0.35，与燃料加速/微调相乘；
## dash 不受影响。由 Boss 触发时置位、RELEASE_HOLD 开始/序列中断时复位。
var _enrage_slow: float = 1.0
var _auto_fire_enabled: bool = true  # 冒烟测试可关闭全自动开火

var _fire_cooldown: float = 0.0
var _sound_index: int = 0
var _invincible: float = 0.0
var _last_hit_frame: int = -1  # 本帧已结算受击（A16：单帧至多结算一次）
var _since_damage: float = 999.0  # 距上次受击秒数（被动回血延迟计时）
var _dead: bool = false

var _fuel: float = 100.0
var _fuel_locked: bool = false  # 燃料耗尽后锁定，回到 30% 才解锁

var _dashing: bool = false
var _dash_timer: float = 0.0
var _dash_dir: Vector2 = Vector2.ZERO
var _dash_cooldown: float = 0.0
var _afterimage_timer: float = 0.0

var _aim_lock_target: Node2D = null  # 瞄准辅助锁定的敌人（含 Boss）
var _prev_aim_mouse := Vector2.ZERO
var _aim_mouse_initialized: bool = false
var _aim_ring: Node2D = null  # 锁定环容器（4 段圆弧，旋转+脉动）
var _aim_ring_target: Node2D = null  # 锁定环当前绑定的目标（变化时才重建样式）
var _hitbox_dot: Polygon2D = null
var _hitbox_halo: Line2D = null
var _boost_toggle_on: bool = false  # shift_toggle_mode 下的加速开关
var _fine_toggle_on: bool = false  # ctrl_toggle_mode 下的微调开关

@onready var _sprite: Sprite2D = $Sprite2D
@onready var _audio: AudioStreamPlayer2D = $AudioStreamPlayer2D
@onready var _hitbox: Area2D = $Hitbox
@onready var _thruster: GPUParticles2D = $Thruster


func _ready() -> void:
	add_to_group("player")
	GameState.player_ref = self
	GameState.player_hitbox = $Hitbox
	_load_balance()


## 数值配置缓存（启动一次读入，避免每帧 Dictionary 路径查找）
func _load_balance() -> void:
	MAX_SPEED = GameState.cfg("player.max_speed", MAX_SPEED)
	ACCEL = GameState.cfg("player.accel", ACCEL)
	DECEL = GameState.cfg("player.decel", DECEL)
	BOOST_MULT = GameState.cfg("player.boost_mult", BOOST_MULT)
	FINE_MOVE_MULT = GameState.cfg("player.fine_move_mult", FINE_MOVE_MULT)
	BASE_FIRE_INTERVAL = GameState.cfg("player.base_fire_interval", BASE_FIRE_INTERVAL)
	BULLET_SPEED = GameState.cfg("player.bullet_speed", BULLET_SPEED)
	BULLET_SPREAD_DEG = GameState.cfg("player.bullet_spread_deg", BULLET_SPREAD_DEG)
	BULLET_DAMAGE = GameState.cfg("player.bullet_damage", BULLET_DAMAGE)
	INVINCIBLE_TIME = GameState.cfg("player.invincible_time", INVINCIBLE_TIME)
	SPAWN_INVINCIBLE_TIME = GameState.cfg("player.spawn_invincible_time", SPAWN_INVINCIBLE_TIME)
	BULLET_CLEAR_RADIUS = GameState.cfg("player.bullet_clear_radius", BULLET_CLEAR_RADIUS)
	ARMOR_MULT = GameState.cfg("buffs.armor.multiplier", ARMOR_MULT)
	EVASION_CHANCE = GameState.cfg("buffs.evasion.chance", EVASION_CHANCE)
	REGEN_PER_SEC = GameState.cfg("buffs.regen.heal_per_sec", REGEN_PER_SEC)
	SHAKE_HIT = GameState.cfg("effects.shake.player_hit", SHAKE_HIT)
	_invincible = SPAWN_INVINCIBLE_TIME  # 出生保护（受击无敌同见 take_damage）
	fuel_max = GameState.cfg("player.fuel.max", fuel_max)
	_fuel = fuel_max
	FUEL_DRAIN = GameState.cfg("player.fuel.drain", FUEL_DRAIN)
	FUEL_REGEN = GameState.cfg("player.fuel.regen", FUEL_REGEN)
	FUEL_RESTART = GameState.cfg("player.fuel.restart", FUEL_RESTART)
	DASH_DISTANCE = GameState.cfg("player.dash.distance", DASH_DISTANCE)
	DASH_TIME = GameState.cfg("player.dash.time", DASH_TIME)
	DASH_COOLDOWN = GameState.cfg("player.dash.cooldown", DASH_COOLDOWN)
	DASH_FUEL_RATIO = GameState.cfg("player.dash.fuel_ratio", DASH_FUEL_RATIO)
	AFTERIMAGE_INTERVAL = GameState.cfg("player.dash.afterimage_interval", AFTERIMAGE_INTERVAL)
	AIM_RING_RADIUS = GameState.cfg("player.aim_assist.ring_radius", AIM_RING_RADIUS)
	_load_aim_assist_params()
	GameState.aim_assist_changed.connect(_on_aim_assist_level_changed)
	# 瞄准辅助锁定环：4 段圆弧容器，锁定时贴在目标上缓慢旋转（半径按目标重排点，不做节点缩放）
	_aim_ring = Node2D.new()
	_aim_ring.top_level = true
	for i in 4:
		var arc := Line2D.new()
		arc.width = 2.0
		_aim_ring.add_child(arc)
	_layout_aim_ring(AIM_RING_RADIUS)
	_aim_ring.hide()
	add_child(_aim_ring)
	# 可视性增强（深色机体在星空背景上易丢失）：机体提亮 + 青色描边辉光
	# （辉光挂 _sprite 下，跟随旋转/缩放/无敌帧闪烁）
	_sprite.modulate = Color(1.35, 1.4, 1.55)
	var glow := Sprite2D.new()
	glow.texture = _sprite.texture
	glow.scale = Vector2(1.2, 1.2)
	glow.modulate = Color(0.45, 0.9, 1.0, 0.45)
	glow.z_index = -1
	_sprite.add_child(glow)
	# 碰撞点指示：受击判定点（Hitbox r=7）的闪烁小光点 + 淡色光圈（街机 shmup 惯例）
	_hitbox_dot = Polygon2D.new()
	var dot_pts := PackedVector2Array()
	for i in 10:
		var a := TAU * float(i) / 10.0
		dot_pts.append(Vector2(cos(a), sin(a)) * 3.5)
	_hitbox_dot.polygon = dot_pts
	_hitbox_dot.color = Color(0.65, 0.95, 1.0)
	add_child(_hitbox_dot)
	_hitbox_halo = Line2D.new()
	_hitbox_halo.width = 1.5
	_hitbox_halo.default_color = Color(0.5, 0.9, 1.0, 0.45)
	_hitbox_halo.closed = true
	for i in 16:
		var a := TAU * float(i) / 16.0
		_hitbox_halo.add_point(Vector2(cos(a), sin(a)) * 8.0)
	add_child(_hitbox_halo)
	# Buff 外观反馈附件（程序化炮舱/护盾弧/光环/尾焰染色，buffs_changed 信号驱动）
	# 附件几何按 0.6 基准机体系数设计，随实际机体缩放等比放大以贴合部位
	var buff_visuals := PlayerBuffVisuals.new()
	buff_visuals.scale = _sprite.scale / PlayerBuffVisuals.BASE_SHIP_SCALE
	add_child(buff_visuals)
	buff_visuals.init(_sprite, self)


func fire_interval() -> float:
	return BASE_FIRE_INTERVAL * pow(GameState.cfg("buffs.rapid_fire.factor", 0.75), GameState.buff_count(&"rapid_fire"))


func bullet_damage() -> int:
	# power_shot：每层 ×1.25 乘算（对齐原作 PowerShotBuff int(base × 1.25^level)，int() 截断）
	return maxi(1, int(BULLET_DAMAGE * pow(GameState.cfg("buffs.power_shot.factor", 1.25), GameState.buff_count(&"power_shot"))))


func fuel_ratio() -> float:
	return _fuel / fuel_max


func refill_fuel() -> void:
	_fuel = fuel_max
	_fuel_locked = false


func dash_unlocked() -> bool:
	return GameState.buff_count(&"phase_dash") > 0


func dash_cooldown_max() -> float:
	# 首次选择解锁，之后每次选择冷却 -20%（最多 2 次）
	return DASH_COOLDOWN * pow(GameState.cfg("player.dash.cooldown_stack_factor", 0.8), maxi(GameState.buff_count(&"phase_dash") - 1, 0))


func dash_fuel_cost() -> float:
	return fuel_max * DASH_FUEL_RATIO


func dash_ready_ratio() -> float:
	if not dash_unlocked():
		return 0.0
	return 1.0 - clampf(_dash_cooldown / dash_cooldown_max(), 0.0, 1.0)


func fuel_drain_rate() -> float:
	return FUEL_DRAIN * pow(GameState.cfg("buffs.efficient_boost.factor", 0.75), GameState.buff_count(&"efficient_boost"))


func fuel_regen_rate() -> float:
	# boost_recovery buff：恢复速率每层 ×1.5（乘算）
	return FUEL_REGEN * pow(GameState.cfg("buffs.boost_recovery.factor", 1.5), GameState.buff_count(&"boost_recovery"))


func _physics_process(delta: float) -> void:
	if _dead or _input_locked:
		# 输入锁定（母舰停靠等）期间 _resolve_aim_point 不执行，锁定目标可能已被
		# 母舰火力击杀；惰性校验被跳过会导致锁定环残留，需在早退前主动校验
		if _aim_ring != null and _aim_ring.visible:
			if not is_instance_valid(_aim_lock_target):
				_aim_lock_target = null
				_aim_ring_target = null
				_aim_ring.hide()
		return
	var input_dir := Input.get_vector("move_left", "move_right", "move_up", "move_down")
	# Boss 狂暴锁定：移动/冲刺冻结（原作 controls_locked 语义），瞄准与开火照常
	if movement_locked:
		input_dir = Vector2.ZERO
		velocity = Vector2.ZERO
		_dashing = false

	# 相位冲刺（燃料不足 25% 满值时禁用）
	_dash_cooldown = maxf(_dash_cooldown - delta, 0.0)
	if (
		dash_unlocked()
		and not movement_locked
		and Input.is_action_just_pressed("dash")
		and _dash_cooldown <= 0.0
		and not _dashing
		and _fuel >= dash_fuel_cost()
	):
		_start_dash(input_dir)
	if _dashing:
		_dash_move(delta)
		return

	# 燃料与加速（shift_toggle_mode：按一下切换开/关）
	if GameState.shift_toggle_mode and Input.is_action_just_pressed("boost"):
		_boost_toggle_on = not _boost_toggle_on
	var want_boost := _boost_toggle_on if GameState.shift_toggle_mode else Input.is_action_pressed("boost")
	if movement_locked:
		want_boost = false  # 锁定期间加速同样冻结（不耗燃料）
	if _fuel_locked and _fuel >= FUEL_RESTART:
		_fuel_locked = false
	var boosting := want_boost and not _fuel_locked and _fuel > 0.0
	if boosting:
		_fuel = maxf(_fuel - fuel_drain_rate() * delta, 0.0)
		if _fuel <= 0.0:
			_fuel_locked = true
	else:
		_fuel = minf(_fuel + fuel_regen_rate() * delta, fuel_max)

	var boost := BOOST_MULT if boosting else 1.0
	# Ctrl 微调：移速 ×0.35（ctrl_toggle_mode：按一下切换开/关）
	if GameState.ctrl_toggle_mode and Input.is_action_just_pressed("fine_move"):
		_fine_toggle_on = not _fine_toggle_on
	var fine_on := _fine_toggle_on if GameState.ctrl_toggle_mode else Input.is_action_pressed("fine_move")
	var fine := FINE_MOVE_MULT if fine_on else 1.0
	var target := input_dir * MAX_SPEED * boost * fine * _enrage_slow
	var rate := ACCEL if input_dir != Vector2.ZERO else DECEL
	velocity = velocity.move_toward(target, rate * delta)
	move_and_slide()
	position = _clamp_to_view(position)

	# 尾焰：加速变长变亮，静止减弱
	if boosting and input_dir != Vector2.ZERO:
		_thruster.speed_scale = 1.7
		_thruster.amount_ratio = 1.0
		_thruster.self_modulate = Color(1.0, 1.0, 1.0, 1.0) * engine_tint
	elif input_dir != Vector2.ZERO:
		_thruster.speed_scale = 1.0
		_thruster.amount_ratio = 0.8
		_thruster.self_modulate = Color(1.0, 1.0, 1.0, 0.85) * engine_tint
	else:
		_thruster.speed_scale = 0.6
		_thruster.amount_ratio = 0.35
		_thruster.self_modulate = Color(1.0, 1.0, 1.0, 0.6) * engine_tint

	var aim := _resolve_aim_point() - global_position
	if aim.length() > 1.0:
		# 贴图机头朝上，需 +90° 偏移
		rotation = aim.angle() + PI / 2.0

	_fire_cooldown -= delta
	if _auto_fire_enabled and _fire_cooldown <= 0.0 and aim.length() > 1.0:
		_fire(aim.normalized())
		_fire_cooldown = fire_interval()

	# 慢速力场已改为全局敌机移速（A13），不再有玩家侧环视觉

	# 无敌帧闪烁
	if _invincible > 0.0:
		_invincible -= delta
		_sprite.modulate.a = 0.35 + 0.65 * absf(sin(Time.get_ticks_msec() / 1000.0 * 20.0))
	else:
		_sprite.modulate.a = 1.0

	# 碰撞点光点脉动（常亮低频闪烁，提示实际受击判定位置）
	_hitbox_dot.modulate.a = 0.45 + 0.55 * absf(sin(Time.get_ticks_msec() / 1000.0 * 6.0))

	# 回血：regen buff 固定 +2 HP/s（二元，对齐原作 RegenerationBuff）；
	# 无 buff 时被动回血——距上次受伤 delay 秒起按难度速率回复（原作延迟不重置为疑似 bug，本版受伤即重置）
	_since_damage += delta
	if GameState.buff_count(&"regen") > 0:
		GameState.heal(REGEN_PER_SEC * delta)
	elif _since_damage >= GameState.passive_regen_delay():
		GameState.heal(GameState.passive_regen_rate() * delta)


## 屏幕边缘钳制：随可见世界区域收窄（zoom=1 时即 40..1880 / 40..1040）
func _clamp_to_view(p: Vector2) -> Vector2:
	var view := GameState.view_world_rect()
	return p.clamp(view.position + Vector2(40.0, 40.0), view.end - Vector2(40.0, 40.0))


func _start_dash(input_dir: Vector2) -> void:
	_dashing = true
	_dash_timer = DASH_TIME
	_fuel = maxf(_fuel - dash_fuel_cost(), 0.0)
	if input_dir != Vector2.ZERO:
		_dash_dir = input_dir.normalized()
	else:
		_dash_dir = (get_global_mouse_position() - global_position).normalized()
		if _dash_dir == Vector2.ZERO:
			_dash_dir = Vector2.UP
	_dash_cooldown = dash_cooldown_max()
	_afterimage_timer = 0.0
	GameState.play_sfx(GameState.SFX_DASH)


## 读取当前强度档位参数（balance.json player.aim_assist.levels.<level>，缺键回退脚本默认）
func _load_aim_assist_params() -> void:
	var base := "player.aim_assist.levels." + String(GameState.aim_assist_level) + "."
	_aim_radius = GameState.cfg(base + "radius", _aim_radius)
	_aim_break = GameState.cfg(base + "break_dist", _aim_break)
	_aim_switch = GameState.cfg(base + "switch_dist", _aim_switch)
	_aim_cone_dot = GameState.cfg(base + "cone_dot", _aim_cone_dot)
	_aim_pull = GameState.cfg(base + "pull", _aim_pull)


func _on_aim_assist_level_changed(_level: StringName) -> void:
	_load_aim_assist_params()


## 瞄准辅助（对齐原作 aim_assist_system，强度三档可调、常驻不可关）：
## 准星 radius 内磁吸最近敌人（含 Boss）并粘滞保持；单帧位移 ≥switch 时先沿甩动方向
## 锥形切换目标，无方向候选且位移 ≥break 才脱锁；pull<1（弱档）时准星仅部分被拉向目标。
## 返回本帧瞄准点：锁定时为目标中心（弱档为鼠标与目标的 pull 混合），否则为原始鼠标位置。
func _resolve_aim_point() -> Vector2:
	var mouse := get_global_mouse_position()
	var movement := Vector2.ZERO
	if _aim_mouse_initialized:
		movement = mouse - _prev_aim_mouse
	_prev_aim_mouse = mouse
	_aim_mouse_initialized = true
	var move_len := movement.length()
	if move_len >= _aim_switch:
		var switched := _target_in_direction(mouse, movement)
		if switched != null:
			_aim_lock_target = switched
		elif move_len >= _aim_break:
			_aim_lock_target = null
	if not is_instance_valid(_aim_lock_target):
		_aim_lock_target = null
	elif mouse.distance_to(_aim_lock_target.global_position) > _aim_radius:
		_aim_lock_target = null
	if _aim_lock_target == null:
		_aim_lock_target = _nearest_enemy_to(mouse)
	if _aim_lock_target != null:
		_update_aim_ring(_aim_lock_target)
		var target_pos: Vector2 = _aim_lock_target.global_position
		if _aim_pull >= 1.0:
			return target_pos
		return mouse.lerp(target_pos, _aim_pull)
	_aim_ring.hide()
	_aim_ring_target = null
	return mouse


func _nearest_enemy_to(point: Vector2) -> Node2D:
	var best: Node2D = null
	var best_sq := _aim_radius * _aim_radius
	for e in GameState.enemies:
		if not e is Node2D:
			continue
		var d_sq: float = point.distance_squared_to((e as Node2D).global_position)
		if d_sq <= best_sq:
			best_sq = d_sq
			best = e
	return best


## 沿甩动方向的锥形目标切换（对齐原作 AIM_ASSIST_DIRECTION_CONE_DOT）：
## 以当前锁定目标（无则原始准星）为原点，取位移方向夹角余弦最大的其他敌人。
func _target_in_direction(mouse: Vector2, movement: Vector2) -> Node2D:
	var origin := mouse
	if is_instance_valid(_aim_lock_target):
		origin = _aim_lock_target.global_position
	var dir := movement.normalized()
	var best: Node2D = null
	var best_dot := _aim_cone_dot
	for e in GameState.enemies:
		if not e is Node2D or e == _aim_lock_target:
			continue
		var to: Vector2 = (e as Node2D).global_position - origin
		if to.length() < 1.0:
			continue
		var dot := to.normalized().dot(dir)
		if dot > best_dot:
			best_dot = dot
			best = e
	return best


## 锁定环：跟随目标，半径按目标碰撞体自适应（下限 AIM_RING_RADIUS），
## 普通机青色 / 精英金色 / Boss 红色；每物理帧缓慢旋转并脉动（仅锁定时被调用）。
func _update_aim_ring(target: Node2D) -> void:
	_aim_ring.global_position = target.global_position
	if _aim_ring_target != target:
		_aim_ring_target = target
		var color := Color(0.4, 0.9, 1.0, 0.9)
		if target is Boss:
			color = Color(1.0, 0.45, 0.4, 0.95)
		elif target is Enemy and (target as Enemy).is_elite:
			color = Color(1.0, 0.85, 0.35, 0.95)
		for arc in _aim_ring.get_children():
			(arc as Line2D).default_color = color
		_layout_aim_ring(_lock_ring_radius(target))
	_aim_ring.rotation += 0.025
	_aim_ring.modulate.a = 0.7 + 0.3 * absf(sin(Time.get_ticks_msec() / 1000.0 * 5.0))
	_aim_ring.show()


## 重排 4 段圆弧到指定半径（每段 60°、间隔 30°；线宽恒定 2，不用节点缩放避免线宽失真）
func _layout_aim_ring(radius: float) -> void:
	for i in 4:
		var arc := _aim_ring.get_child(i) as Line2D
		arc.clear_points()
		var a0 := PI / 2.0 * float(i) + deg_to_rad(15.0)
		var a1 := PI / 2.0 * float(i + 1) - deg_to_rad(15.0)
		for j in 8:
			var a := lerpf(a0, a1, float(j) / 7.0)
			arc.add_point(Vector2(cos(a), sin(a)) * radius)


func _lock_ring_radius(target: Node2D) -> float:
	var r := 0.0
	var shape_node := target.get_node_or_null("CollisionShape2D") as CollisionShape2D
	if shape_node != null and shape_node.shape is CircleShape2D:
		r = (shape_node.shape as CircleShape2D).radius * maxf(target.scale.x, 0.5)
	return maxf(r + 10.0, AIM_RING_RADIUS)


func _dash_move(delta: float) -> void:
	_dash_timer -= delta
	_afterimage_timer -= delta
	if _afterimage_timer <= 0.0:
		_afterimage_timer = AFTERIMAGE_INTERVAL
		_spawn_afterimage()
	velocity = _dash_dir * (DASH_DISTANCE / DASH_TIME)
	move_and_slide()
	position = _clamp_to_view(position)
	# 冲刺时尾焰拉满
	_thruster.speed_scale = 1.7
	_thruster.amount_ratio = 1.0
	_thruster.self_modulate = Color(1.0, 1.0, 1.0, 1.0) * engine_tint
	if _dash_timer <= 0.0:
		_dashing = false
		GameState.play_sfx(GameState.SFX_DASH, -3.0)


func _spawn_afterimage() -> void:
	var ghost := Sprite2D.new()
	ghost.texture = _sprite.texture
	ghost.scale = _sprite.scale
	ghost.global_position = global_position
	ghost.global_rotation = rotation
	ghost.modulate = Color(0.5, 0.9, 1.0, 0.5)
	get_parent().add_child(ghost)
	var tween := ghost.create_tween()
	tween.tween_property(ghost, "modulate:a", 0.0, 0.3)
	tween.tween_callback(ghost.queue_free)


func _fire(aim: Vector2) -> void:
	var spread := mini(GameState.buff_count(&"spread_shot"), GameState.cfg("buffs.spread_shot.max_stacks", 3))
	var pierce := mini(GameState.buff_count(&"piercing"), GameState.cfg("buffs.piercing.max_stacks", 2))
	var explosive := GameState.buff_count(&"explosive") > 0
	var count := 1 + spread
	for i in count:
		var offset := deg_to_rad(BULLET_SPREAD_DEG * (float(i) - float(spread) / 2.0))
		var b: Bullet = GameState.bullet_pool.fire(aim.rotated(offset), BULLET_SPEED, bullet_damage(), true)
		b.pierce = pierce
		b.explosive = explosive
		b.position = position + aim.rotated(offset) * 50.0
	_audio.stream = FIRE_SOUNDS[_sound_index]
	_sound_index = (_sound_index + 1) % FIRE_SOUNDS.size()
	_audio.play()


## 受击结算（100 HP 制）。返回 true = 本帧实际结算（调用方据此决定子弹是否销毁；
## 无敌/单帧已结算/闪避返回 false，敌弹直接穿过不销毁，对齐原作 single-hit 语义）。
## 减免两段式（去 bug 统一版：原作闪避仅敌机撞击、护甲不含敌机撞击且多层零收益，
## 均疑似接线 bug——本版闪避与护甲对全部伤害源生效）：先 20% 闪避，再护甲 ×0.85。
func take_damage(amount: float = 1.0) -> bool:
	if _dead or _invincible > 0.0 or _dashing:
		return false
	# A16：单帧至多结算一次受击（敌弹/敌机撞/Boss 撞共用）
	if Engine.get_physics_frames() == _last_hit_frame:
		return false
	# 闪避 buff：20% 完全免伤（不置无敌、不清弹）
	if GameState.buff_count(&"evasion") > 0 and randf() < EVASION_CHANCE:
		return false
	# 护甲 buff：固定 ×0.85 减伤
	if GameState.buff_count(&"armor") > 0:
		amount *= ARMOR_MULT
	_last_hit_frame = Engine.get_physics_frames()
	_since_damage = 0.0
	_invincible = INVINCIBLE_TIME
	GameState.play_sfx(GameState.SFX_PLAYER_HIT)
	GameState.shake(SHAKE_HIT)
	GameState.lose_health(amount)
	_clear_nearby_enemy_bullets()
	if GameState.health <= 0.0:
		_die()
	return true


## 受击连锁：清除 250px 内全部敌弹（对齐原作 BULLET_CLEAR_RADIUS；无分无特效）
func _clear_nearby_enemy_bullets() -> void:
	for child in get_parent().get_children():
		var b := child as Bullet
		if b != null and not b.is_player_bullet:
			if b.global_position.distance_to(global_position) <= BULLET_CLEAR_RADIUS:
				b._despawn()


func _die() -> void:
	_dead = true
	_enrage_slow = 1.0  # 死亡/重生路径兜底：狂暴减速必复位（Boss 侧另有解锁与离场兜底）
	hide()
	_aim_ring.hide()
	_hitbox.set_deferred("monitoring", false)
	set_physics_process(false)
	Explosion.spawn_at(get_parent(), position, 2.0)


func _exit_tree() -> void:
	if GameState.player_ref == self:
		GameState.player_ref = null
	if GameState.player_hitbox == _hitbox:
		GameState.player_hitbox = null
