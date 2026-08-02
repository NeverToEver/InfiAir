class_name Player
extends CharacterBody2D
## 玩家战机：WASD 平滑移动，朝准星旋转（辅助瞄准：准星入标记敌框则出膛弹追踪该敌），
## 全自动开火，Shift 消耗燃料加速，Ctrl 微调（×0.35），空格相位冲刺（需解锁 buff，耗 25% 燃料）。

const FIRE_SOUNDS: Array[AudioStream] = [
	preload("res://assets/audio/bullet_fire.wav"),
	preload("res://assets/audio/bullet_fire_b.wav"),
	preload("res://assets/audio/bullet_fire_c.wav"),
]

## 入场动画结束（缓移段收尾）后发出，main 据此恢复敌机生成
signal entry_finished

## 入场动画（开场/返航继续出击后播放）：高速冲入定位到屏幕下 1/3，再向后（下）缓移一小节。
## 数值在 balance.json player.entry（设计值，view_world_rect 适配视角缩放）。
var ENTRY_LAND_RATIO := 0.74  # 冲入定位点 = view 高度占比（下 1/3 区域）
var ENTRY_RUSH_TIME := 0.55  # 高速冲入时长（QUAD EASE_OUT 减速）
var ENTRY_RETREAT_SPEED := 90.0  # 后撤缓移速度（px/s，向下）
var ENTRY_RETREAT_TIME := 1.1  # 后撤时长
var ENTRY_INVINCIBLE := 2.1  # 入场全程无敌（= 冲入 0.55 + 后撤 1.1 + 0.45s 缓冲；缓冲段按普通无敌闪烁路径）
var ENTRY_SPAWN_CLEARANCE := 90.0  # 入场起点屏外偏移（设计值，view 坐标不乘 world_scale）
var ENTRY_RUSH_HS_RATIO := 0.6  # 后撤段水平输入速度系数（× MAX_SPEED）

var MAX_SPEED := 420.0
var ACCEL := 2400.0
var DECEL := 1800.0
var BOOST_MULT := 1.8
var BASE_FIRE_INTERVAL := 0.15
var BULLET_SPEED := 1800.0
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
## A5 阶段1：buff 缩放系数热路径缓存（buffs_changed 驱动刷新，避免每帧/每发射查 cfg）
var _rapid_fire_factor: float = 0.75
var _power_shot_factor: float = 1.25
var _spread_max: int = 3
var _pierce_max: int = 2
var _efficient_factor: float = 0.75
var _boost_recovery_factor: float = 1.5
var _dash_cooldown_stack_factor: float = 0.8  # 冲刺冷却逐层堆叠系数（player.dash.cooldown_stack_factor）

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

var HOMING_TIME := 1.2  # 辅助瞄准追踪时限（≈玩家弹出屏寿命 1920/1800≈1.07s；balance.json player.aim_assist.homing_time）
## 辅助瞄准当前档位参数（balance.json player.aim_assist.levels + GameState.aim_assist_level，信号联动刷新）
var _homing_turn_rate := 5.5  # 准星入标记框时出膛弹的追踪转向速率
var _aim_stick_factor := 0.5  # 准星入标记框时的鼠标灵敏度系数（弱吸附，1.0 = 无吸附）
var _aim_joy_speed := 1400.0  # P0-1：手柄右摇杆虚拟准星速度 px/s（balance player.aim_assist.joy_speed）
## P1-3：框外锥形弱追踪参数（档位；_cone_cos 在 _load_aim_assist_params 内由角度重算）
var _cone_angle_deg := 6.0
var _cone_cos := 0.9945  # cos(6°)（类级初始化限常量表达式，档位切换时重算）
var _cone_strength := 0.45
## P1-3：磁吸档位参数（仅供 aim_assist_params() 聚合返回，计算在 AimFrameLayer 侧）
var _magnet_range := 100.0
var _magnet_strength := 6.0
var _magnet_max_speed := 8.0
## P1-3：磁吸输入阈值与距离衰减全局参数（player.aim_assist.input / falloff，_load_balance 一次缓存）
var _magnet_input_min := 2.0
var _magnet_input_full := 40.0
var _falloff_peak := 400.0
var _falloff_end := 1400.0
var _falloff_min := 0.3
## 平滑瞄准点状态（入框降灵敏度用；每渲染帧推进一次，判定用上一帧平滑点）
var _aim_smooth := Vector2.ZERO
var _aim_last_raw := Vector2.ZERO
var _aim_smoothed_frame := -1
var _aim_initialized := false
var FINE_MOVE_MULT := 0.35  # Ctrl 微调（对齐原作 PRECISION_SPEED_MULT）

var fuel_max: float = 100.0  # 燃料上限（balance.json player.fuel.max 覆盖）
var _input_locked: bool = false  # 返航过场期间锁定
## 原 Boss 狂暴定身字段（原作 is_controls_locked）：阶段 A 起狂暴改用 _enrage_slow 减速
## （BOSS_REDESIGN §4.3），本字段保留兼容，Boss 不再写它。
var movement_locked: bool = false
## Boss 狂暴减速乘区（§4.3）：TRANSITION+ACTIVE 期间 0.35，与燃料加速/微调相乘；
## dash 不受影响。由 Boss 触发时置位、RELEASE_HOLD 开始/序列中断时复位。
var _enrage_slow: float = 1.0
var _auto_fire_enabled: bool = true  # 冒烟测试可关闭全自动开火
## A8：受击/回血与冲刺组件（组合委托）
var _damage := PlayerDamage.new()
var _dash := PlayerDash.new()

var _fire_cooldown: float = 0.0
var _sound_index: int = 0
## 入场动画状态（0=无/正常对局，1=高速冲入，2=后撤缓移）
var _entry_phase: int = 0
var _entry_retreat_left: float = 0.0
var _entry_prev_auto_fire: bool = true  # 入场前自动开火状态（结束恢复，不覆盖测试/既有设置）
var _entry_tween: Tween = null  # 冲入 tween（abort_entry 中断时 kill）
## A8：受击/回血状态经属性转发到 PlayerDamage（语法兼容，测试白盒不变）
var _invincible: float = 0.0:
	get:
		return _damage.invincible
	set(value):
		_damage.invincible = value
@warning_ignore("unused_private_class_variable")
var _last_hit_frame: int = -1:  # 本帧已结算受击（A16：单帧至多结算一次）
	get:
		return _damage.last_hit_frame
	set(value):
		_damage.last_hit_frame = value
@warning_ignore("unused_private_class_variable")
var _since_damage: float = 999.0:  # 距上次受击秒数（被动回血延迟计时）
	get:
		return _damage.since_damage
	set(value):
		_damage.since_damage = value
var _dead: bool = false

var _fuel: float = 100.0
var _fuel_locked: bool = false  # 燃料耗尽后锁定，回到 30% 才解锁

## A8：冲刺状态经属性转发到 PlayerDash（语法兼容，测试白盒不变）
var _dashing: bool = false:
	get:
		return _dash.dashing
	set(value):
		_dash.dashing = value
@warning_ignore("unused_private_class_variable")
var _dash_timer: float = 0.0:
	get:
		return _dash.dash_timer
	set(value):
		_dash.dash_timer = value
@warning_ignore("unused_private_class_variable")
var _dash_dir: Vector2 = Vector2.ZERO:
	get:
		return _dash.dash_dir
	set(value):
		_dash.dash_dir = value
@warning_ignore("unused_private_class_variable")
var _dash_cooldown: float = 0.0:
	get:
		return _dash.dash_cooldown
	set(value):
		_dash.dash_cooldown = value
@warning_ignore("unused_private_class_variable")
var _afterimage_timer: float = 0.0:
	get:
		return _dash.afterimage_timer
	set(value):
		_dash.afterimage_timer = value

## 测试瞄准注入点（!=INF 时代替鼠标位置；headless 合成鼠标事件之外的直接注入路径）
var aim_point_override := Vector2.INF
var _crosshair: AimCrosshair = null  # 鼠标跟随准星（P1-1，_load_balance 创建）
var _hitbox_dot: Polygon2D = null
var _hitbox_halo: Line2D = null
var _muzzle_offset: float  # 出弹点偏移（50 × world_scale，_load_balance 缓存）
var _boost_toggle_on: bool = false  # shift_toggle_mode 下的加速开关
var _fine_toggle_on: bool = false  # ctrl_toggle_mode 下的微调开关

@onready var _sprite: Sprite2D = $Sprite2D
@onready var _audio: AudioStreamPlayer2D = $AudioStreamPlayer2D
@onready var _hitbox: Area2D = $Hitbox
@onready var _thruster: GPUParticles2D = $Thruster

## P1-5：冲刺残影小池（预建复用，替代逐次 new Sprite2D + Tween + queue_free）
const AFTERIMAGE_POOL_SIZE := 4
const AFTERIMAGE_FADE_TIME := 0.3
const AFTERIMAGE_COLOR := Color(0.5, 0.9, 1.0, 0.5)
var _afterimage_pool: Array[Sprite2D] = []
var _afterimage_idx: int = 0
var _active_afterimages: Array[Sprite2D] = []


func _ready() -> void:
	add_to_group("player")
	GameState.player_ref = self
	GameState.player_hitbox = $Hitbox
	_load_balance()
	_refresh_buff_factors()
	GameState.buffs_changed.connect(_refresh_buff_factors)
	# P0-1：手柄右摇杆瞄准灵敏度由设置页驱动（joy_settings_changed 广播重读）
	_aim_joy_speed = GameState.joy_aim_speed
	GameState.joy_settings_changed.connect(_on_joy_settings_changed)
	# P1-5：冲刺残影小池预建（消灭每次冲刺的节点+Tween 新建/销毁抖动）。
	# 挂 Main 下（残影固定在世界坐标，不随玩家移动）；Main 场景构建期 add_child 会报
	# "busy setting up children"，延迟到帧末执行
	for i in AFTERIMAGE_POOL_SIZE:
		var g := Sprite2D.new()
		g.visible = false
		g.modulate = AFTERIMAGE_COLOR
		get_parent().add_child.call_deferred(g)
		_afterimage_pool.append(g)


## P1-5：残影逐帧淡出（渲染帧；池节点 alpha 归零即隐藏回收，零分配）
func _process(delta: float) -> void:
	_update_afterimages(delta)


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
	ENTRY_LAND_RATIO = float(GameState.cfg("player.entry.land_ratio", ENTRY_LAND_RATIO))
	ENTRY_RUSH_TIME = float(GameState.cfg("player.entry.rush_time", ENTRY_RUSH_TIME))
	ENTRY_RETREAT_SPEED = float(GameState.cfg("player.entry.retreat_speed", ENTRY_RETREAT_SPEED))
	ENTRY_RETREAT_TIME = float(GameState.cfg("player.entry.retreat_time", ENTRY_RETREAT_TIME))
	ENTRY_INVINCIBLE = float(GameState.cfg("player.entry.invincible", ENTRY_INVINCIBLE))
	ENTRY_SPAWN_CLEARANCE = float(GameState.cfg("player.entry.spawn_clearance", ENTRY_SPAWN_CLEARANCE))
	ENTRY_RUSH_HS_RATIO = float(GameState.cfg("player.entry.rush_hspeed_ratio", ENTRY_RUSH_HS_RATIO))
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
	# A8：配置注入受击/回血与冲刺组件（缓存值从 balance 覆盖后传入）
	_damage.configure(INVINCIBLE_TIME, ARMOR_MULT, EVASION_CHANCE, REGEN_PER_SEC, SHAKE_HIT)
	_dash.configure(DASH_DISTANCE, DASH_TIME, DASH_COOLDOWN, AFTERIMAGE_INTERVAL)
	# P1-3：磁吸输入阈值与距离衰减全局参数（非档位，仅 _ready 一次缓存）
	_magnet_input_min = float(GameState.cfg("player.aim_assist.input.magnet_input_min", _magnet_input_min))
	_magnet_input_full = float(GameState.cfg("player.aim_assist.input.magnet_input_full", _magnet_input_full))
	_falloff_peak = float(GameState.cfg("player.aim_assist.falloff.peak", _falloff_peak))
	_falloff_end = float(GameState.cfg("player.aim_assist.falloff.end", _falloff_end))
	_falloff_min = float(GameState.cfg("player.aim_assist.falloff.min", _falloff_min))
	_load_aim_assist_params()
	GameState.aim_assist_changed.connect(_on_aim_assist_level_changed)
	# 机体尺寸族：tscn 存设计值（1.0 基准），此处统一乘全局缩放并幂等覆盖
	var ws: float = GameState.world_scale
	_sprite.scale = Vector2.ONE * 0.65 * ws
	(($CollisionShape2D as CollisionShape2D).shape as CircleShape2D).radius = 22.0 * ws
	(($Hitbox/CollisionShape2D as CollisionShape2D).shape as CircleShape2D).radius = 7.0 * ws
	_thruster.position = Vector2(0.0, 70.0 * ws)
	var thruster_mat := _thruster.process_material as ParticleProcessMaterial
	thruster_mat.scale_min = 2.5 * ws
	thruster_mat.scale_max = 5.5 * ws
	_muzzle_offset = 50.0 * ws
	# 鼠标跟随准星（P1-1）：top_level 世界坐标节点，跟随 aim_point()，
	# 可见性与系统光标隐藏由 AimCrosshair 自身按对局活跃条件统一驱动
	_crosshair = AimCrosshair.new()
	_crosshair.init(self)
	add_child(_crosshair)
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


## 对外公开接口（A1 修复）：封装内部状态，禁止跨类直接写 _ 私有字段
func is_dead() -> bool:
	return _dead


func is_input_locked() -> bool:
	return _input_locked


func set_invincible(seconds: float) -> void:
	_damage.set_invincible(seconds)


func invincible_remaining() -> float:
	return _damage.invincible_remaining()


## A7：测试/诊断白盒断言经公开接口（命名语义化，非纯测试专用）
func enrage_slow() -> float:
	return _enrage_slow


func set_dead(dead: bool) -> void:
	_dead = dead


func set_dash_cooldown(seconds: float) -> void:
	_dash.dash_cooldown = seconds


func reset_combat_state() -> void:
	_damage.last_hit_frame = -1
	_damage.since_damage = 999.0


func set_since_damage(seconds: float) -> void:
	_damage.since_damage = seconds


func set_last_hit_frame(frame: int) -> void:
	_damage.last_hit_frame = frame


func dash_cooldown() -> float:
	return _dash.cooldown_remaining()


func since_damage() -> float:
	return _damage.since_damage


func fire(aim: Vector2) -> void:
	_fire(aim)


func reset_fire_cooldown() -> void:
	_fire_cooldown = 0.0


func boost_toggle_active() -> bool:
	return _boost_toggle_on


func fine_toggle_active() -> bool:
	return _fine_toggle_on


func set_boost_toggle(enabled: bool) -> void:
	_boost_toggle_on = enabled


func set_fine_toggle(enabled: bool) -> void:
	_fine_toggle_on = enabled


func aim_assist_params() -> Dictionary:
	return {
		"homing_turn_rate": _homing_turn_rate,
		"stick_factor": _aim_stick_factor,
		"magnet_range": _magnet_range,
		"magnet_strength": _magnet_strength,
		"magnet_max_speed": _magnet_max_speed,
		"magnet_input_min": _magnet_input_min,
		"magnet_input_full": _magnet_input_full,
		"cone_angle_deg": _cone_angle_deg,
		"cone_strength": _cone_strength,
		"falloff_peak": _falloff_peak,
		"falloff_end": _falloff_end,
		"falloff_min": _falloff_min,
	}


## P1-3 距离衰减曲线（公开供测试；_fire 弱追踪与 AimFrameLayer 磁吸共用同一形状）：
## d ≤ peak 全辅助；d ≥ end 平坦下限；中间线性。参数来自 player.aim_assist.falloff.*
func aim_dist_falloff(d: float) -> float:
	return dist_falloff_curve(d, _falloff_peak, _falloff_end, _falloff_min)


## G018：距离衰减分段纯函数（单实现——Player.aim_dist_falloff 与 AimFrameLayer 磁吸共用，
## 改一侧不再破坏另一侧一致性）
static func dist_falloff_curve(d: float, peak: float, end: float, min_v: float) -> float:
	if d <= peak:
		return 1.0
	if d >= end:
		return min_v
	return lerpf(1.0, min_v, (d - peak) / (end - peak))


func hitbox_enabled() -> bool:
	return _hitbox.monitoring


func lock_input() -> void:
	_input_locked = true


func unlock_input() -> void:
	_input_locked = false


func set_fuel(value: float) -> void:
	_fuel = clampf(value, 0.0, fuel_max)


func fuel_amount() -> float:
	return _fuel


func die() -> void:
	_die()


func apply_enrage_slow(factor: float) -> void:
	_enrage_slow = factor


func set_auto_fire(enabled: bool) -> void:
	_auto_fire_enabled = enabled


func auto_fire_enabled() -> bool:
	return _auto_fire_enabled


func is_dashing() -> bool:
	return _dash.is_dashing()


## A5 阶段1：刷新 buff 缩放系数缓存（_ready 初始 + buffs_changed 信号驱动）
func _refresh_buff_factors() -> void:
	_rapid_fire_factor = GameState.cfg("buffs.rapid_fire.factor", _rapid_fire_factor)
	_power_shot_factor = GameState.cfg("buffs.power_shot.factor", _power_shot_factor)
	_spread_max = int(GameState.cfg("buffs.spread_shot.max_stacks", _spread_max))
	_pierce_max = int(GameState.cfg("buffs.piercing.max_stacks", _pierce_max))
	_efficient_factor = GameState.cfg("buffs.efficient_boost.factor", _efficient_factor)
	_boost_recovery_factor = GameState.cfg("buffs.boost_recovery.factor", _boost_recovery_factor)
	_dash_cooldown_stack_factor = GameState.cfg("player.dash.cooldown_stack_factor", _dash_cooldown_stack_factor)


func fire_interval() -> float:
	return BASE_FIRE_INTERVAL * pow(_rapid_fire_factor, GameState.buff_count(&"rapid_fire"))


func bullet_damage() -> int:
	# power_shot：每层 ×1.25 乘算（对齐原作 PowerShotBuff int(base × 1.25^level)，int() 截断）
	return maxi(1, int(BULLET_DAMAGE * pow(_power_shot_factor, GameState.buff_count(&"power_shot"))))


func fuel_ratio() -> float:
	return _fuel / fuel_max


func refill_fuel() -> void:
	_fuel = fuel_max
	_fuel_locked = false


func dash_unlocked() -> bool:
	return GameState.buff_count(&"phase_dash") > 0


func dash_cooldown_max() -> float:
	# 首次选择解锁，之后每次选择冷却 -20%（最多 2 次）；系数经 buffs_changed 缓存（热路径免查 cfg）
	return DASH_COOLDOWN * pow(_dash_cooldown_stack_factor, maxi(GameState.buff_count(&"phase_dash") - 1, 0))


func dash_fuel_cost() -> float:
	return fuel_max * DASH_FUEL_RATIO


func dash_ready_ratio() -> float:
	if not dash_unlocked():
		return 0.0
	return 1.0 - clampf(_dash.cooldown_remaining() / dash_cooldown_max(), 0.0, 1.0)


func fuel_drain_rate() -> float:
	return FUEL_DRAIN * pow(_efficient_factor, GameState.buff_count(&"efficient_boost"))


func fuel_regen_rate() -> float:
	# boost_recovery buff：恢复速率每层 ×1.5（乘算）
	return FUEL_REGEN * pow(_boost_recovery_factor, GameState.buff_count(&"boost_recovery"))


func _physics_process(delta: float) -> void:
	if _dead or _input_locked:
		return
	# 入场动画接管移动/输入（开场/继续出击后），无敌照常倒计时但不闪烁
	if _entry_phase != 0:
		_entry_physics(delta)
		return
	var input_dir := Input.get_vector("move_left", "move_right", "move_up", "move_down")
	# Boss 狂暴锁定：移动/冲刺冻结（原作 controls_locked 语义），瞄准与开火照常
	if movement_locked:
		input_dir = Vector2.ZERO
		velocity = Vector2.ZERO
		_dashing = false

	# 相位冲刺（燃料不足 25% 满值时禁用；A8 委托 PlayerDash）
	_dash.tick_cooldown(delta)
	if (
		dash_unlocked()
		and not movement_locked
		and Input.is_action_just_pressed("dash")
		and _dash.cooldown_remaining() <= 0.0
		and not _dash.is_dashing()
		and _fuel >= dash_fuel_cost()
	):
		_dash.start(input_dir, self)
	if _dash.is_dashing():
		_dash.update_move(delta, self)
		# 冲刺尾焰（视觉保留 player 侧）
		_thruster.speed_scale = 1.7
		_thruster.amount_ratio = 1.0
		_thruster.self_modulate = Color(1.0, 1.0, 1.0, 1.0) * engine_tint
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
	position = clamp_to_view(position)

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

	var aim := aim_point() - global_position
	if aim.length() > 1.0:
		# 贴图机头朝上，需 +90° 偏移
		rotation = aim.angle() + PI / 2.0

	_fire_cooldown -= delta
	if _auto_fire_enabled and _fire_cooldown <= 0.0 and aim.length() > 1.0:
		_fire(aim.normalized())
		_fire_cooldown = fire_interval()

	# 慢速力场已改为全局敌机移速（A13），不再有玩家侧环视觉

	# 无敌帧闪烁
	var now_ms := Time.get_ticks_msec()  # 每帧一次（P2：闪烁与光点共用）
	if _invincible > 0.0:
		_invincible -= delta
		_sprite.modulate.a = 0.35 + 0.65 * absf(Enemy.sin_fast(now_ms * 0.02))
	else:
		_sprite.modulate.a = 1.0

	# 碰撞点光点脉动（常亮低频闪烁，提示实际受击判定位置）
	_hitbox_dot.modulate.a = 0.45 + 0.55 * absf(Enemy.sin_fast(now_ms * 0.006))

	# 回血（A8 委托 PlayerDamage）：regen buff 固定 +2 HP/s；无 buff 时被动回血
	_damage.heal_tick(delta)


## 屏幕边缘钳制：随可见世界区域收窄（zoom=1 时即 40..1880 / 40..1040）
func clamp_to_view(p: Vector2) -> Vector2:
	var view := GameState.view_world_rect()
	return p.clamp(view.position + Vector2(40.0, 40.0), view.end - Vector2(40.0, 40.0))


## 当前瞄准点（世界坐标）：测试注入点优先，否则平滑鼠标位置（与准星/开火同一来源）。
## 弹道规则（P1-1）：默认朝瞄准点直射；准星入标记敌框时出膛弹获得对该敌的追踪修正。
## 入框弱吸附：平滑点在标记框内时鼠标增量按档位 stick_factor 降灵敏度，帮助定住准星；
## 判定用上一帧平滑点（即玩家看到的准星位置），与框高亮/追踪绑定自洽，出框即恢复全速。
func aim_point() -> Vector2:
	if aim_point_override != Vector2.INF:
		return aim_point_override
	var raw := get_global_mouse_position()
	# H01（健壮性审核）：右摇杆虚拟准星——四向独立动作（get_vector 语义：差值驱动）；
	# 摇杆增量驱动瞄准点（键鼠不受影响；松开即停）
	var joy := Input.get_vector(&"aim_left", &"aim_right", &"aim_up", &"aim_down")
	if joy.length_squared() > 0.01:
		raw += joy * _aim_joy_speed * get_process_delta_time()
	var frame := Engine.get_process_frames()
	if frame != _aim_smoothed_frame:  # 每渲染帧只推一次（准星/框层/开火多处取值）
		_aim_smoothed_frame = frame
		var factor := 1.0
		var magnet := Vector2.ZERO
		if _aim_initialized and GameState.aim_frame_layer != null:
			# 粘性判定结果复用（一次 marked_target_at）：入框走降灵敏度，框外近距走磁吸
			var sticky: Enemy = GameState.aim_frame_layer.marked_target_at(_aim_smooth)
			if sticky != null:
				factor = _aim_stick_factor
			else:
				magnet = GameState.aim_frame_layer.magnet_pull(_aim_smooth, raw - _aim_last_raw)
		_aim_smooth = raw if not _aim_initialized else _aim_smooth + (raw - _aim_last_raw) * factor + magnet
		_aim_last_raw = raw
		_aim_initialized = true
	return _aim_smooth


## 读取当前强度档位参数（balance.json player.aim_assist.levels.<level>，缺键回退脚本默认）
func _load_aim_assist_params() -> void:
	var base := "player.aim_assist.levels." + String(GameState.aim_assist_level) + "."
	_homing_turn_rate = GameState.cfg(base + "homing_turn_rate", _homing_turn_rate)
	_aim_stick_factor = GameState.cfg(base + "stick_factor", _aim_stick_factor)
	HOMING_TIME = GameState.cfg("player.aim_assist.homing_time", HOMING_TIME)
	# P1-3：锥形弱追踪与磁吸档位参数（同 base 路径、同档位信号刷新）
	_cone_angle_deg = float(GameState.cfg(base + "cone_angle_deg", _cone_angle_deg))
	_cone_cos = cos(deg_to_rad(_cone_angle_deg))
	_cone_strength = float(GameState.cfg(base + "cone_strength", _cone_strength))
	_magnet_range = float(GameState.cfg(base + "magnet_range", _magnet_range))
	_magnet_strength = float(GameState.cfg(base + "magnet_strength", _magnet_strength))
	_magnet_max_speed = float(GameState.cfg(base + "magnet_max_speed", _magnet_max_speed))


func _on_aim_assist_level_changed(_level: StringName) -> void:
	_load_aim_assist_params()


## P0-1：手柄设置变更（右摇杆灵敏度）重读
func _on_joy_settings_changed(aim_speed: float, _deadzone: float) -> void:
	_aim_joy_speed = aim_speed


func spawn_afterimage() -> void:
	# P1-5：循环复用池节点；同一节点淡出中被再次冲刺命中时 alpha 重置重新淡出
	var ghost := _afterimage_pool[_afterimage_idx]
	_afterimage_idx = (_afterimage_idx + 1) % AFTERIMAGE_POOL_SIZE
	ghost.texture = _sprite.texture
	ghost.scale = _sprite.scale
	ghost.global_position = global_position
	ghost.global_rotation = rotation
	ghost.modulate = AFTERIMAGE_COLOR
	ghost.visible = true
	if not _active_afterimages.has(ghost):
		_active_afterimages.append(ghost)


## P1-5：残影淡出推进（_process 每帧调用；池内每节点 alpha 线性衰减，归零隐藏）
func _update_afterimages(delta: float) -> void:
	if _active_afterimages.is_empty():
		return
	var i := 0
	while i < _active_afterimages.size():
		var g: Sprite2D = _active_afterimages[i]
		g.modulate.a -= delta / AFTERIMAGE_FADE_TIME
		if g.modulate.a <= 0.0:
			g.visible = false
			_active_afterimages.remove_at(i)
		else:
			i += 1


## 入场动画（开场/返航继续出击后由 main 调用）：高速冲入定位到屏幕下 1/3，再向后缓移一小节。
## 阶段 1 冲入锁输入（tween 驱动）；阶段 2 后撤仅左右可调、上下锁定；全程无敌（不闪烁）。
## 结束发 entry_finished。动画进行中/已死亡时重复调用忽略。
func play_entry_animation() -> void:
	if _entry_phase != 0 or _dead:
		return
	var rect := GameState.view_world_rect()
	var land_y := rect.position.y + rect.size.y * ENTRY_LAND_RATIO
	_entry_phase = 1
	_entry_retreat_left = ENTRY_RETREAT_TIME
	set_invincible(ENTRY_INVINCIBLE)
	velocity = Vector2.ZERO
	_dashing = false
	_entry_prev_auto_fire = _auto_fire_enabled
	_auto_fire_enabled = false
	position = Vector2(rect.get_center().x, rect.end.y + ENTRY_SPAWN_CLEARANCE)
	# 高功率引擎冲入（尾焰拉满；减速后随拖尾渐隐）
	_thruster.speed_scale = 2.0
	_thruster.amount_ratio = 1.0
	_thruster.self_modulate = Color(1.0, 1.0, 1.0, 1.0) * engine_tint
	_entry_tween = create_tween()
	_entry_tween.tween_property(self, "position:y", land_y, ENTRY_RUSH_TIME).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
	_entry_tween.tween_callback(_on_entry_landed)


## 入场动画进行中（main/测试查询，替代私有直读）
func is_entry_playing() -> bool:
	return _entry_phase != 0


## 中断入场动画（返航/自毁等流程接管时调用）：复位状态机并静默收尾，不发 entry_finished
## （避免误恢复敌机生成）。D06：入场起始帧内返航会经 lock_input 冻结后撤推进、
## _finish_entry 永不执行，此出口防止继续出击后新入场被 _entry_phase != 0 守卫跳过。
func abort_entry() -> void:
	if _entry_phase == 0:
		return
	_entry_phase = 0
	_auto_fire_enabled = _entry_prev_auto_fire
	if _entry_tween != null and _entry_tween.is_valid():
		_entry_tween.kill()
		_entry_tween = null
	velocity = Vector2.ZERO
	_thruster.speed_scale = 1.0
	_thruster.amount_ratio = 0.35


func _on_entry_landed() -> void:
	if _entry_phase == 1:
		_entry_phase = 2


## 入场物理分支（_physics_process 在最前调用）：阶段 1 由 tween 驱动不吃输入；
## 阶段 2 仅左右可调（上下锁定），Y 自动后移，尾焰渐隐，计时收尾。
func _entry_physics(delta: float) -> void:
	if _invincible > 0.0:
		_invincible -= delta  # 入场期间无敌照常倒计时（不闪烁）
	if _entry_phase == 1:
		return
	var input_x := Input.get_axis("move_left", "move_right")
	velocity = Vector2(input_x * MAX_SPEED * ENTRY_RUSH_HS_RATIO, ENTRY_RETREAT_SPEED)
	move_and_slide()
	position = clamp_to_view(position)
	_entry_retreat_left -= delta
	# 拖尾渐隐（冲入尾焰回收到静止档）
	_thruster.speed_scale = move_toward(_thruster.speed_scale, 0.6, delta * 2.0)
	_thruster.amount_ratio = move_toward(_thruster.amount_ratio, 0.35, delta * 1.2)
	if _entry_retreat_left <= 0.0:
		_finish_entry()


func _finish_entry() -> void:
	_entry_phase = 0
	_auto_fire_enabled = _entry_prev_auto_fire
	velocity = Vector2.ZERO
	entry_finished.emit()


func _fire(aim: Vector2) -> void:
	var spread := mini(GameState.buff_count(&"spread_shot"), _spread_max)
	var pierce := mini(GameState.buff_count(&"piercing"), _pierce_max)
	var explosive := GameState.buff_count(&"explosive") > 0
	# 辅助瞄准（P1-1）：准星在某标记敌框内 → 本轮出膛弹全部获得对该敌的追踪修正。
	# P1-3：框外但目标在瞄准锥角内 → 弱追踪（转向率随角距与距离渐变，锥缘/远距退化为直射）
	var homing_target: Enemy = null
	var homing_rate := _homing_turn_rate
	if GameState.aim_frame_layer != null:
		homing_target = GameState.aim_frame_layer.marked_target_at(aim_point())
		if homing_target == null:
			var aim_dir := aim.normalized()
			homing_target = GameState.aim_frame_layer.nearest_cone_target(global_position, aim_dir, _cone_cos)
			if homing_target != null:
				var dot := aim_dir.dot((homing_target.global_position - global_position).normalized())
				var ang_t := clampf((dot - _cone_cos) / (1.0 - _cone_cos), 0.0, 1.0)
				homing_rate = (
					_homing_turn_rate
					* _cone_strength
					* ang_t
					* aim_dist_falloff(global_position.distance_to(homing_target.global_position))
				)
				if homing_rate <= 0.0:
					homing_target = null  # 锥缘/远距退化为直射
	var count := 1 + spread
	for i in count:
		var offset := deg_to_rad(BULLET_SPREAD_DEG * (float(i) - float(spread) / 2.0))
		var b: Bullet = GameState.bullet_pool.fire(aim.rotated(offset), BULLET_SPEED, bullet_damage(), true)
		b.pierce = pierce
		b.explosive = explosive
		if homing_target != null:
			b.homing_target = homing_target
			b.homing_time = HOMING_TIME
			b.homing_turn_rate = homing_rate
		b.position = position + aim.rotated(offset) * _muzzle_offset
	_audio.stream = FIRE_SOUNDS[_sound_index]
	_sound_index = (_sound_index + 1) % FIRE_SOUNDS.size()
	_audio.play()


## 受击结算（100 HP 制）。返回 true = 本帧实际结算（调用方据此决定子弹是否销毁；
## 无敌/单帧已结算/闪避返回 false，敌弹直接穿过不销毁，对齐原作 single-hit 语义）。
## 减免两段式（去 bug 统一版：原作闪避仅敌机撞击、护甲不含敌机撞击且多层零收益，
## 均疑似接线 bug——本版闪避与护甲对全部伤害源生效）：先 20% 闪避，再护甲 ×0.85。
## from_pos（D8）：伤害源世界坐标，供 Meta HUD 定向波纹；Vector2.INF = 无方向（均匀环）。
func take_damage(amount: float = 1.0, from_pos: Vector2 = Vector2.INF) -> bool:
	# A8：受击结算（减免两段式/单帧守卫/无敌/闪避/清弹/死亡）委托 PlayerDamage
	return _damage.take_damage(amount, from_pos, self)


## 受击连锁：清除 250px 内全部敌弹（对齐原作 BULLET_CLEAR_RADIUS；无分无特效）。
## A8：PlayerDamage 经公开入口调用。
func clear_nearby_enemy_bullets() -> void:
	for child in get_parent().get_children():
		var b := child as Bullet
		if b != null and not b.is_player_bullet:
			if b.global_position.distance_to(global_position) <= BULLET_CLEAR_RADIUS:
				b.despawn()


func _die() -> void:
	_dead = true
	abort_entry()  # D06：入场期间自毁（长按 K）时复位入场状态机，防 tween/相位滞留
	_enrage_slow = 1.0  # 死亡/重生路径兜底：狂暴减速必复位（Boss 侧另有解锁与离场兜底）
	hide()
	_hitbox.set_deferred("monitoring", false)
	set_physics_process(false)
	Explosion.spawn_at(get_parent(), position, 2.0)


## 进入母舰保护舱（召唤回收）：隐藏机体 + 关闭受击判定，不置 _dead；
## 无敌与输入锁由母舰对接流程管理，position 仍由母舰驱动（驻留钉在对接点）
func enter_pod() -> void:
	hide()
	_hitbox.set_deferred("monitoring", false)


## 离开保护舱（释放抛下时调用）：恢复显示与受击判定；准星随对局活跃条件自动重现
func exit_pod() -> void:
	if _dead:
		return
	show()
	_hitbox.set_deferred("monitoring", true)


func _exit_tree() -> void:
	# C22：显式断开 GameState 信号连接（重入树不重复连接；与 PlayerBuffVisuals 模式对齐）
	if GameState.buffs_changed.is_connected(_refresh_buff_factors):
		GameState.buffs_changed.disconnect(_refresh_buff_factors)
	if GameState.aim_assist_changed.is_connected(_on_aim_assist_level_changed):
		GameState.aim_assist_changed.disconnect(_on_aim_assist_level_changed)
	if GameState.joy_settings_changed.is_connected(_on_joy_settings_changed):
		GameState.joy_settings_changed.disconnect(_on_joy_settings_changed)
	if GameState.player_ref == self:
		GameState.player_ref = null
	if GameState.player_hitbox == _hitbox:
		GameState.player_hitbox = null
