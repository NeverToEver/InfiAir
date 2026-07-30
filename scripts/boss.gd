class_name Boss
extends Area2D
## Boss：3 种轮换（1 重装 / 2 游击 / 3 母舰），HP 分段驱动阶段框架（BOSS_REDESIGN §4.1）：
## P1（100–70%）→ P2（70–30%）→ ENRAGE（<30%），P1/P2 各为数据驱动的模式表循环
## （模式 = 固定波次或持续时长，播完切下一个；段切换：0.6s 蓄力辉光 + 抖屏 + 变调音效
## + 清自身开火计时）。走位与攻击解耦：每型每阶段一个走位函数，攻击在其上叠加
## （阶段 A 仅一型 P1 纵向下压：每 6s 下压 80px 再回）。
## 狂暴为各型专属序列（§5，子弹时间/TRANSITION/RETURN 框架共用）：
##   1 型「旋转堡垒」：ACTIVE 悬停原地，每 0.5s 一波 12 向环弹（起始角进动），
##     RELEASE 8 路蓄力重炮齐射（有 telegraph）；
##   2 型「猎杀环绕」：ACTIVE 在快照点轨道 4 象限 6 点依次瞬停，每点 0.3s 瞄准线 +
##     单发狙，RELEASE 回轨道底部放 12 向慢速环弹；
##   3 型「倾巢」：ACTIVE 每 1.2s 一波 3 小怪（共 3 波）+ 每 0.9s 一圈 8 向环弹，
##     RELEASE 16 向慢速环弹 + 在场小怪齐射。
## 序列期间锁血在 30% 检查点、玩家移速 ×0.35 减速（替代原作定身，§4.3）；
## RETURN 后回到常规「余怒」循环（射速 ×1.3/移速 ×1.3）。
## 进入战斗 50s 未被击杀则逃跑：最后 3s 逃跑警告 + 上飘（血条倒计时自
## escape.countdown_visible_from 秒起由 HUD 显示），随后加速离场
## （无击杀奖励：不触发 add_boss_kill、不加分、不升难度、轮换计数不推进）。

signal health_changed(current: float, maximum: float)
signal died
signal enraged
## 常规阶段切换（P1→P2、进入 ENRAGE）时发出，HUD 血条短闪
signal phase_changed(new_phase: int)
## 逃跑离场时发出（击毁不会发）；died 在击毁与逃跑离场时都会发出，
## 用于血条隐藏与生成器重排，击杀奖励只在 _die() 结算。
signal escaped

## 狂暴子状态机（对齐原作 BossState 的 4 个 ENRAGE_* 子状态）
enum EnragePhase { NONE, TRANSITION, ACTIVE, RELEASE_HOLD, RETURN }
## 常规阶段（§4.1）：P1/P2 模式表循环，ENRAGE 为狂暴（序列结束后「余怒」沿用 P2 表提速）
enum FightPhase { P1, P2, ENRAGE }
## 冲刺掠过（二型 P2 攻击）子状态
enum SweepState { NONE, AIM, DASH, RETURN }

const TEXTURES: Array[Texture2D] = [
	preload("res://assets/sprites/boss_ship_1.png"),
	preload("res://assets/sprites/boss_ship_2.png"),
	preload("res://assets/sprites/boss_ship_3.png"),
]
## 猎杀环绕瞬停点（右→上→左→下→右→上，共 6 点；末点为顶部，RELEASE 回底部）
const STALKER_POINT_ANGLES_DEG: Array[float] = [0.0, -90.0, 180.0, 90.0, 0.0, -90.0]
## 模式表脚本默认值（与 balance.json boss.phases.typeN 保持一致，AGENTS.md 约定）：
## 1 型 P1=[5路扇形,追踪弹] P2=[蓄力重炮,7路扇形]；2 型 P1=[3连狙] P2=[冲刺掠过,3连狙]；
## 3 型 P1=[旋转cross+召唤] P2=[编队齐射,弹幕墙]（召唤为独立计时，不在模式表内）。
const DEFAULT_PATTERNS: Dictionary = {
	1: {
		"p1": [
			{"attack": &"fan5", "waves": 3, "interval": 1.6},
			{"attack": &"homing", "waves": 2, "interval": 1.6},
		],
		"p2": [
			{"attack": &"charged_cannon", "waves": 1, "interval": 2.4},
			{"attack": &"fan7", "waves": 3, "interval": 1.4},
		],
	},
	2: {
		"p1": [{"attack": &"sniper3", "waves": 1, "interval": 1.8}],
		"p2": [
			{"attack": &"dash_sweep", "waves": 1, "interval": 2.5},
			{"attack": &"sniper3", "waves": 1, "interval": 1.5},
		],
	},
	3: {
		"p1": [{"attack": &"cross", "duration": 6.0, "interval": 0.9}],
		"p2": [
			{"attack": &"minion_volley", "waves": 1, "interval": 2.0},
			{"attack": &"bullet_wall", "waves": 1, "interval": 1.5},
		],
	},
}
var ENTER_SPEED := 140.0
var FIGHT_Y := 230.0
var STRAFE_MIN_X := 300.0
var STRAFE_MAX_X := 1620.0
## HP 基底（× 类型系数 × 难度乘数；对齐原作首发 Boss ≈12s TTK 量级）
var HP_BASE := 800.0
## 各类型移动速度 / 开火间隔（模式表 interval 缺键时的回退基准）/ 弹速
var STRAFE_SPEEDS: Array = [150.0, 400.0, 60.0]
var FIRE_INTERVALS: Array = [1.6, 1.8, 0.9]
var FAN_BULLET_SPEED := 380.0
var HOMING_BULLET_SPEED := 300.0
var SNIPER_BULLET_SPEED := 650.0
var CROSS_BULLET_SPEED := 260.0
## 阶段阈值：P2 = 70%（新增），ENRAGE = 30%（沿用原作）
var PHASE2_HP_RATIO := 0.7
var ENRAGE_HP_RATIO := 0.3
## 「余怒」倍率：射速 ×1.3（原 ×1.5 下调，§5.4）/ 移速 ×1.3
var ENRAGE_RATE_MULT := 1.3
var ENRAGE_SPEED_MULT := 1.3
## 狂暴期玩家减速乘区（替代定身，§4.3）：TRANSITION+ACTIVE 期间移速 ×0.35
var ENRAGE_PLAYER_SLOW := 0.35
## 段切换演出时长（蓄力辉光 + 停火，§4.1）
var PHASE_SHIFT_DURATION := 0.6
## 狙击 telegraph（§4.2/§5.2）：瞄准线 0.35s（前 0.2s 微跟踪玩家后固定），到点沿线出弹
var SNIPER_AIM_TIME := 0.35
var SNIPER_TRACK_TIME := 0.2
## 一型 P1 纵向下压（§5.1）：每 6s 下压 80px 再回
var PRESS_INTERVAL := 6.0
var PRESS_DEPTH := 80.0
## 蓄力重炮（一型 P2，§5.1）：0.6s 蓄力辉光 → 3 发高速重弹（间隔 0.25s，每发 0.15s 短闪光）
var CANNON_CHARGE := 0.6
var CANNON_SHOTS := 3
var CANNON_INTERVAL := 0.25
var CANNON_BULLET_SPEED := 700.0
var CANNON_DAMAGE := 21
var CANNON_FLASH := 0.15
## 冲刺掠过（二型 P2，§5.2）：0.5s 瞄准线 → 高速横穿玩家高度，路径拖 3 枚减速弹
var SWEEP_AIM := 0.5
var SWEEP_SPEED := 900.0
var SWEEP_DROP_COUNT := 3
var SWEEP_DROP_SPEED := 150.0
var SWEEP_DROP_DAMAGE := 12
var SWEEP_RETURN_DURATION := 0.8
## 编队齐射（三型 P2，§5.3）：召唤 4 小怪列横队，0.8s 后齐射一轮自机狙
var VOLLEY_COUNT := 4
var VOLLEY_DELAY := 0.8
var VOLLEY_BULLET_SPEED := 420.0
var VOLLEY_BULLET_DAMAGE := 12
## 弹幕墙（三型 P2，§5.3）：10 路低速扇形墙，留 2 个相邻缺口（缺口方位避开自机 ±30°）
var WALL_COUNT := 10
var WALL_BULLET_SPEED := 220.0
var WALL_DAMAGE := 12
var WALL_ARC_DEG := 150.0
## 难度分档（§4.4，boss.difficulty_scaling）：索引 = [easy, medium, hard]。
## 只作用于 Boss 攻击密度/速度：开火间隔 ×、弹速 ×、弹数 ±（快照弹幕/伤害不动；
## HP 由 setup 经 GameState.enemy_hp_multiplier() 按难度档 0.75/1.0/1.5 乘算）。
var DIFF_INTERVAL_MULT: Array = [1.15, 1.0, 0.85]
var DIFF_SPEED_MULT: Array = [0.9, 1.0, 1.1]
var DIFF_COUNT_DELTAS: Dictionary = {
	"fan": [-1, 0, 1],
	"homing": [-1, 0, 1],
	"cannon": [-1, 0, 1],
	"volley": [-1, 0, 1],
	"wall": [-2, 0, 2],
	"ring": [-2, 0, 2],
	"salvo": [-2, 0, 2],
	"summon": [-1, 0, 1],
	"drops": [-1, 0, 1],
}
var ENRAGE_SNAPSHOT_LASERS := 4
var ENRAGE_SNAPSHOT_RING := 8
var ENRAGE_LASER_SPEED := 820.0  # 高速长弹（表现复用敌弹 laser 型）
var ENRAGE_RING_SPEED := 240.0  # 环形慢弹
## 狂暴序列时序（对齐原作 EnrageConstants @60fps：360/54/42/24/6/42/48 帧）
var ENRAGE_DURATION := 6.0  # TRANSITION+ACTIVE 总时长（360 帧）
var ENRAGE_TRANSITION_DURATION := 0.9  # 54 帧
var ENRAGE_ATTACK_INTERVAL := 0.7  # ACTIVE 每波间隔（42 帧，仅未差异化回退路径用）
var ENRAGE_ATTACK_WINDUP := 0.4  # ACTIVE 起手延迟（24 帧）
var ENRAGE_RELEASE_INTERVAL := 0.1  # RELEASE_HOLD 每波间隔（6 帧，回退路径用）
var ENRAGE_RELEASE_HOLD_DURATION := 0.7  # 42 帧
var ENRAGE_RETURN_DURATION := 0.8  # 48 帧
## 轨道：半径 = max(机体宽,高)×1.5 受屏幕边界约束（原作 PATH_RADIUS_SCALE/MIN_Y 钳制）
var ENRAGE_PATH_RADIUS_SCALE := 1.5
## 出弹点前伸：舰体边缘（原 100 按 r=120 机体定，机体 ÷3 后同步）
var MUZZLE_OFFSET := 100.0  # 出弹点偏移设计值（_ready × world_scale）
var _ws: float = 1.0  # 全局机体缩放缓存（_ready 读取一次）
var ENRAGE_SQUARE_PATH_RATIO := 0.48  # 前 48% 方形路径，后 52% 圆形路径
## RELEASE 弹速 = ACTIVE 弹速 × 原作释放比例（1.35/3.7≈0.365、1.55/3.2≈0.484，回退路径用）
var ENRAGE_RELEASE_LASER_SPEED := 300.0
var ENRAGE_RELEASE_RING_SPEED := 120.0
## 一型狂暴「旋转堡垒」（§5.1，boss.enrage.type_1）
var E1_RING_INTERVAL := 0.5
var E1_RING_COUNT := 12
var E1_RING_SPEED := 240.0
var E1_RING_PRECESSION_DEG := 15.0
var E1_SALVO_CHARGE := 0.5
var E1_SALVO_COUNT := 8
var E1_SALVO_SPEED := 700.0
var E1_SALVO_DAMAGE := 21
## 二型狂暴「猎杀环绕」（§5.2，boss.enrage.type_2）
var E2_POINT_COUNT := 6
var E2_POINT_INTERVAL := 0.8
var E2_AIM := 0.3
var E2_SNIPER_SPEED := 900.0
var E2_SNIPER_DAMAGE := 21
var E2_RELEASE_RING_COUNT := 12
var E2_RELEASE_RING_SPEED := 120.0
## 三型狂暴「倾巢」（§5.3，boss.enrage.type_3）
var E3_SUMMON_INTERVAL := 1.2
var E3_SUMMON_WAVES := 3
var E3_SUMMON_COUNT := 3
var E3_RING_INTERVAL := 0.9
var E3_RING_COUNT := 8
var E3_RING_SPEED := 240.0
var E3_RELEASE_RING_COUNT := 16
var E3_RELEASE_RING_SPEED := 120.0
## 逃跑：进入战斗 50s 未击杀触发，最后 3s 警告 + 上飘（对齐原作 3000/180 帧@60fps）
var ESCAPE_TIME := 50.0
var ESCAPE_WARNING := 3.0
var ESCAPE_DRIFT := 26.0
var ESCAPE_START_SPEED := 120.0
var ESCAPE_ACCEL := 420.0
## 血条下方逃跑倒计时显示起点（剩余 ≤10s，§4.5）
var ESCAPE_COUNTDOWN_FROM := 10.0
## 各弹种伤害（对齐原作 boss_attack.py phase-1：spread 12+2=14 / aim 18+3=21 / wave 12 /
## 快照激光 18+3=21 / 快照环弹 12；homing 为本版弹种取 wave 同档 12）
var BULLET_DAMAGE_FAN := 14
var BULLET_DAMAGE_HOMING := 12
var BULLET_DAMAGE_SNIPER := 21
var BULLET_DAMAGE_CROSS := 12
var BULLET_DAMAGE_SNAPSHOT_LASER := 21
var BULLET_DAMAGE_SNAPSHOT_RING := 12
## 身体撞击伤害（对齐原作 BOSS_COLLISION_DAMAGE=30）
var COLLISION_DAMAGE := 30
## 慢速力场：机体移速 ×0.8（对齐原作 boss 移动 slow_factor）
var SLOW_FIELD_FACTOR := 0.8

var boss_type: int = 1
var max_hp: float = 30.0
var hp: float = 30.0
var is_escaped: bool = false

var _in_fight: bool = false
var _enraged: bool = false
var _score_scale: float = 1.0
var _strafe_dir: float = 1.0
var _survival: float = 0.0
var _escape_warned: bool = false
var _escaping: bool = false
var _escape_speed: float = 0.0
## 母舰召唤减速带：短时减速乘区（仅位移，经 _slow_factor 生效）
var _summon_slow_timer: float = 0.0
var _summon_slow_factor: float = 1.0
# 阶段框架与模式表循环（§4.1）
var _fight_phase: int = FightPhase.P1
var _patterns: Dictionary = {}  # {"p1": [...], "p2": [...]}，_ready 从配置载入
var _pattern_index: int = 0
var _pattern_left: float = 0.0  # 当前模式剩余波次（或剩余时长秒）
var _pattern_is_duration: bool = false
var _fire_timer: float = 1.6
# 狙击 telegraph（游击型）：瞄准线由 Boss 在 _physics_process 驱动，用完即毁
var _aim_line: Line2D = null
var _sniper_aim_elapsed: float = -1.0  # <0 = 无进行中的 telegraph
var _sniper_dir: Vector2 = Vector2.DOWN
var _burst_left: int = 0
var _burst_timer: float = 0.0
var _burst_dir: Vector2 = Vector2.ZERO  # 非零 = telegraph 锁定方向的固定方向爆发
# 蓄力重炮（一型 P2）
var _cannon_elapsed: float = -1.0  # <0 = 无进行中的蓄力
var _cannon_shots_left: int = 0
var _cannon_timer: float = 0.0
var _cannon_flashed: bool = false
# 冲刺掠过（二型 P2）
var _sweep_state: int = SweepState.NONE
var _sweep_timer: float = 0.0
var _sweep_dir: float = 1.0
var _sweep_origin := Vector2.ZERO
var _sweep_return_target := Vector2.ZERO
var _sweep_drop_x: Array[float] = []
var _sweep_line: Line2D = null
# 编队齐射（三型 P2）
var _volley_minions: Array[Enemy] = []
var _volley_timer: float = 0.0
# 难度分档弹数增减（§4.4，_apply_difficulty_scaling 写入；扇形/追踪弹在分发处取用）
var _d_fan: int = 0
var _d_homing: int = 0
# 游击型
var _dashing: bool = false
var _move_timer: float = 0.0
# 母舰型
var _summon_timer: float = 6.0
var _cross_angle: float = 0.0
# 一型 P1 纵向下压
var _press_timer: float = 6.0
var _press_offset: float = 0.0
# 狂暴序列状态（计时单位均为游戏秒，随 time_scale 缩放）
var _enrage_phase: int = EnragePhase.NONE
var _enrage_timer: float = 0.0  # TRANSITION+ACTIVE 剩余（progress 驱动轨道）
var _enrage_transition_timer: float = 0.0
var _enrage_release_hold_timer: float = 0.0
var _enrage_return_timer: float = 0.0
var _enrage_attack_timer: float = 0.0
var _enrage_attack_index: int = 0
## 锁血：触发→RELEASE_HOLD 开始前 HP 锁定在 30% 检查点（任何伤害不掉血不死）
var _enrage_health_lock: bool = false
var _enrage_snapshot_target := Vector2.ZERO  # 触发时玩家位置快照（轨道中心）
var _enrage_transition_origin := Vector2.ZERO
var _enrage_return_origin := Vector2.ZERO
var _enrage_return_target := Vector2.ZERO
var _slowed_player: Player = null  # 被施加狂暴减速的玩家（用于精确复位）
var _boss_size := Vector2(328.0, 328.0)  # 贴图有效尺寸（_ready 实测更新，算轨道半径）
# 差异化狂暴各型状态
var _enrage_ring_angle: float = 0.0  # 1 型环弹起始角（随波次进动）
var _enrage_summon_timer: float = 0.0  # 3 型倾巢召唤计时
var _enrage_summon_waves: int = 0  # 3 型已放小怪波数
var _enrage_aim_elapsed: float = -1.0  # 2 型逐点瞄准计时（<0 = 未瞄准）
var _enrage_release_salvo_done: bool = false  # 1/2 型 RELEASE 一次性收尾已结算
var _enrage_release_origin := Vector2.ZERO  # 2 型 RELEASE 回轨道底部起点

@onready var _sprite: Sprite2D = $Sprite2D


## 蓄力辉光圆点（过场 _glow 配方：叠加态圆点 + scale/alpha tween）
class _GlowDot:
	extends Node2D
	var radius := 8.0
	var dot_color := Color.WHITE

	func _draw() -> void:
		draw_circle(Vector2.ZERO, radius, dot_color)


func setup(p_difficulty: float, p_type: int) -> void:
	boss_type = p_type
	# HP 四级乘算：基准 × 型别倍率 × Boss 击杀 ramp × 难度档（与敌机同源 0.75/1.0/1.5）
	max_hp = (
		float(GameState.cfg("boss.hp_base", HP_BASE))
		* float(GameState.cfg("boss.hp_mults", [1.3, 0.7, 1.6])[p_type - 1])
		* p_difficulty
		* GameState.enemy_hp_multiplier()
	)
	hp = max_hp
	# setup() 在 _ready() 之前调用，不能用 @onready 变量
	($Sprite2D as Sprite2D).texture = TEXTURES[p_type - 1]


func _ready() -> void:
	add_to_group("enemy")
	GameState.register_enemy(self)
	# 机体尺寸族：设计值 × 全局缩放（tscn 存 1.0 基准，幂等覆盖）
	_ws = GameState.world_scale
	_sprite.scale = Vector2.ONE * 1.15 * _ws
	(($CollisionShape2D as CollisionShape2D).shape as CircleShape2D).radius = 120.0 * _ws
	MUZZLE_OFFSET = 100.0 * _ws
	# 数值配置缓存（启动一次读入）
	ENTER_SPEED = GameState.cfg("boss.enter_speed", ENTER_SPEED)
	FIGHT_Y = GameState.cfg("boss.fight_y", FIGHT_Y)
	STRAFE_MIN_X = GameState.cfg("boss.strafe_min_x", STRAFE_MIN_X)
	STRAFE_MAX_X = GameState.cfg("boss.strafe_max_x", STRAFE_MAX_X)
	PHASE2_HP_RATIO = GameState.cfg("boss.phase2_hp_ratio", PHASE2_HP_RATIO)
	ENRAGE_HP_RATIO = GameState.cfg("boss.enrage.hp_ratio", ENRAGE_HP_RATIO)
	ENRAGE_RATE_MULT = GameState.cfg("boss.enrage.rate_mult", ENRAGE_RATE_MULT)
	ENRAGE_SPEED_MULT = GameState.cfg("boss.enrage.speed_mult", ENRAGE_SPEED_MULT)
	ENRAGE_PLAYER_SLOW = GameState.cfg("boss.enrage.player_slow", ENRAGE_PLAYER_SLOW)
	ENRAGE_SNAPSHOT_LASERS = GameState.cfg("boss.enrage.snapshot_lasers", ENRAGE_SNAPSHOT_LASERS)
	ENRAGE_SNAPSHOT_RING = GameState.cfg("boss.enrage.snapshot_ring", ENRAGE_SNAPSHOT_RING)
	ENRAGE_LASER_SPEED = GameState.cfg("boss.enrage.laser_speed", ENRAGE_LASER_SPEED)
	ENRAGE_RING_SPEED = GameState.cfg("boss.enrage.ring_speed", ENRAGE_RING_SPEED)
	ENRAGE_DURATION = GameState.cfg("boss.enrage.duration", ENRAGE_DURATION)
	ENRAGE_TRANSITION_DURATION = GameState.cfg("boss.enrage.transition_duration", ENRAGE_TRANSITION_DURATION)
	ENRAGE_ATTACK_INTERVAL = GameState.cfg("boss.enrage.attack_interval", ENRAGE_ATTACK_INTERVAL)
	ENRAGE_ATTACK_WINDUP = GameState.cfg("boss.enrage.attack_windup", ENRAGE_ATTACK_WINDUP)
	ENRAGE_RELEASE_INTERVAL = GameState.cfg("boss.enrage.release_interval", ENRAGE_RELEASE_INTERVAL)
	ENRAGE_RELEASE_HOLD_DURATION = GameState.cfg("boss.enrage.release_hold_duration", ENRAGE_RELEASE_HOLD_DURATION)
	ENRAGE_RETURN_DURATION = GameState.cfg("boss.enrage.return_duration", ENRAGE_RETURN_DURATION)
	ENRAGE_PATH_RADIUS_SCALE = GameState.cfg("boss.enrage.path_radius_scale", ENRAGE_PATH_RADIUS_SCALE)
	ENRAGE_SQUARE_PATH_RATIO = GameState.cfg("boss.enrage.square_path_ratio", ENRAGE_SQUARE_PATH_RATIO)
	ENRAGE_RELEASE_LASER_SPEED = GameState.cfg("boss.enrage.release_laser_speed", ENRAGE_RELEASE_LASER_SPEED)
	ENRAGE_RELEASE_RING_SPEED = GameState.cfg("boss.enrage.release_ring_speed", ENRAGE_RELEASE_RING_SPEED)
	_boss_size = _sprite.texture.get_size() * _sprite.scale
	ESCAPE_TIME = GameState.cfg("boss.escape.time", ESCAPE_TIME)
	ESCAPE_WARNING = GameState.cfg("boss.escape.warning", ESCAPE_WARNING)
	ESCAPE_DRIFT = GameState.cfg("boss.escape.drift", ESCAPE_DRIFT)
	ESCAPE_START_SPEED = GameState.cfg("boss.escape.start_speed", ESCAPE_START_SPEED)
	ESCAPE_ACCEL = GameState.cfg("boss.escape.accel", ESCAPE_ACCEL)
	ESCAPE_COUNTDOWN_FROM = GameState.cfg("boss.escape.countdown_visible_from", ESCAPE_COUNTDOWN_FROM)
	HP_BASE = GameState.cfg("boss.hp_base", HP_BASE)
	STRAFE_SPEEDS = GameState.cfg("boss.strafe_speeds", STRAFE_SPEEDS)
	FIRE_INTERVALS = GameState.cfg("boss.fire_intervals", FIRE_INTERVALS)
	FAN_BULLET_SPEED = GameState.cfg("boss.fan_bullet_speed", FAN_BULLET_SPEED)
	HOMING_BULLET_SPEED = GameState.cfg("boss.homing_bullet_speed", HOMING_BULLET_SPEED)
	SNIPER_BULLET_SPEED = GameState.cfg("boss.sniper_bullet_speed", SNIPER_BULLET_SPEED)
	CROSS_BULLET_SPEED = GameState.cfg("boss.cross_bullet_speed", CROSS_BULLET_SPEED)
	COLLISION_DAMAGE = GameState.cfg("boss.collision_damage", COLLISION_DAMAGE)
	SLOW_FIELD_FACTOR = GameState.cfg("buffs.slow_field.factor", SLOW_FIELD_FACTOR)
	BULLET_DAMAGE_FAN = GameState.cfg("boss.bullet_damage.fan", BULLET_DAMAGE_FAN)
	BULLET_DAMAGE_HOMING = GameState.cfg("boss.bullet_damage.homing", BULLET_DAMAGE_HOMING)
	BULLET_DAMAGE_SNIPER = GameState.cfg("boss.bullet_damage.sniper", BULLET_DAMAGE_SNIPER)
	BULLET_DAMAGE_CROSS = GameState.cfg("boss.bullet_damage.cross", BULLET_DAMAGE_CROSS)
	BULLET_DAMAGE_SNAPSHOT_LASER = GameState.cfg("boss.bullet_damage.snapshot_laser", BULLET_DAMAGE_SNAPSHOT_LASER)
	BULLET_DAMAGE_SNAPSHOT_RING = GameState.cfg("boss.bullet_damage.snapshot_ring", BULLET_DAMAGE_SNAPSHOT_RING)
	PHASE_SHIFT_DURATION = GameState.cfg("boss.phases.phase_shift_duration", PHASE_SHIFT_DURATION)
	SNIPER_AIM_TIME = GameState.cfg("boss.phases.telegraph.sniper_aim", SNIPER_AIM_TIME)
	SNIPER_TRACK_TIME = GameState.cfg("boss.phases.telegraph.sniper_track", SNIPER_TRACK_TIME)
	PRESS_INTERVAL = GameState.cfg("boss.phases.press_interval", PRESS_INTERVAL)
	PRESS_DEPTH = GameState.cfg("boss.phases.press_depth", PRESS_DEPTH)
	# 阶段 B 攻击库参数（boss.phases.attacks.*）
	CANNON_CHARGE = GameState.cfg("boss.phases.attacks.charged_cannon.charge", CANNON_CHARGE)
	CANNON_SHOTS = GameState.cfg("boss.phases.attacks.charged_cannon.shots", CANNON_SHOTS)
	CANNON_INTERVAL = GameState.cfg("boss.phases.attacks.charged_cannon.interval", CANNON_INTERVAL)
	CANNON_BULLET_SPEED = GameState.cfg("boss.phases.attacks.charged_cannon.bullet_speed", CANNON_BULLET_SPEED)
	CANNON_DAMAGE = GameState.cfg("boss.phases.attacks.charged_cannon.damage", CANNON_DAMAGE)
	CANNON_FLASH = GameState.cfg("boss.phases.attacks.charged_cannon.flash", CANNON_FLASH)
	SWEEP_AIM = GameState.cfg("boss.phases.attacks.dash_sweep.aim", SWEEP_AIM)
	SWEEP_SPEED = GameState.cfg("boss.phases.attacks.dash_sweep.speed", SWEEP_SPEED)
	SWEEP_DROP_COUNT = GameState.cfg("boss.phases.attacks.dash_sweep.drop_count", SWEEP_DROP_COUNT)
	SWEEP_DROP_SPEED = GameState.cfg("boss.phases.attacks.dash_sweep.drop_speed", SWEEP_DROP_SPEED)
	SWEEP_DROP_DAMAGE = GameState.cfg("boss.phases.attacks.dash_sweep.drop_damage", SWEEP_DROP_DAMAGE)
	SWEEP_RETURN_DURATION = GameState.cfg("boss.phases.attacks.dash_sweep.return_duration", SWEEP_RETURN_DURATION)
	VOLLEY_COUNT = GameState.cfg("boss.phases.attacks.minion_volley.count", VOLLEY_COUNT)
	VOLLEY_DELAY = GameState.cfg("boss.phases.attacks.minion_volley.delay", VOLLEY_DELAY)
	VOLLEY_BULLET_SPEED = GameState.cfg("boss.phases.attacks.minion_volley.bullet_speed", VOLLEY_BULLET_SPEED)
	VOLLEY_BULLET_DAMAGE = GameState.cfg("boss.phases.attacks.minion_volley.bullet_damage", VOLLEY_BULLET_DAMAGE)
	WALL_COUNT = GameState.cfg("boss.phases.attacks.bullet_wall.count", WALL_COUNT)
	WALL_BULLET_SPEED = GameState.cfg("boss.phases.attacks.bullet_wall.bullet_speed", WALL_BULLET_SPEED)
	WALL_DAMAGE = GameState.cfg("boss.phases.attacks.bullet_wall.damage", WALL_DAMAGE)
	WALL_ARC_DEG = GameState.cfg("boss.phases.attacks.bullet_wall.arc_deg", WALL_ARC_DEG)
	# 差异化狂暴参数（boss.enrage.type_*）
	E1_RING_INTERVAL = GameState.cfg("boss.enrage.type_1.ring_interval", E1_RING_INTERVAL)
	E1_RING_COUNT = GameState.cfg("boss.enrage.type_1.ring_count", E1_RING_COUNT)
	E1_RING_SPEED = GameState.cfg("boss.enrage.type_1.ring_speed", E1_RING_SPEED)
	E1_RING_PRECESSION_DEG = GameState.cfg("boss.enrage.type_1.ring_precession_deg", E1_RING_PRECESSION_DEG)
	E1_SALVO_CHARGE = GameState.cfg("boss.enrage.type_1.salvo_charge", E1_SALVO_CHARGE)
	E1_SALVO_COUNT = GameState.cfg("boss.enrage.type_1.salvo_count", E1_SALVO_COUNT)
	E1_SALVO_SPEED = GameState.cfg("boss.enrage.type_1.salvo_speed", E1_SALVO_SPEED)
	E1_SALVO_DAMAGE = GameState.cfg("boss.enrage.type_1.salvo_damage", E1_SALVO_DAMAGE)
	E2_POINT_COUNT = GameState.cfg("boss.enrage.type_2.point_count", E2_POINT_COUNT)
	E2_POINT_INTERVAL = GameState.cfg("boss.enrage.type_2.point_interval", E2_POINT_INTERVAL)
	E2_AIM = GameState.cfg("boss.enrage.type_2.aim", E2_AIM)
	E2_SNIPER_SPEED = GameState.cfg("boss.enrage.type_2.sniper_speed", E2_SNIPER_SPEED)
	E2_SNIPER_DAMAGE = GameState.cfg("boss.enrage.type_2.sniper_damage", E2_SNIPER_DAMAGE)
	E2_RELEASE_RING_COUNT = GameState.cfg("boss.enrage.type_2.release_ring_count", E2_RELEASE_RING_COUNT)
	E2_RELEASE_RING_SPEED = GameState.cfg("boss.enrage.type_2.release_ring_speed", E2_RELEASE_RING_SPEED)
	E3_SUMMON_INTERVAL = GameState.cfg("boss.enrage.type_3.summon_interval", E3_SUMMON_INTERVAL)
	E3_SUMMON_WAVES = GameState.cfg("boss.enrage.type_3.summon_waves", E3_SUMMON_WAVES)
	E3_SUMMON_COUNT = GameState.cfg("boss.enrage.type_3.summon_count", E3_SUMMON_COUNT)
	E3_RING_INTERVAL = GameState.cfg("boss.enrage.type_3.ring_interval", E3_RING_INTERVAL)
	E3_RING_COUNT = GameState.cfg("boss.enrage.type_3.ring_count", E3_RING_COUNT)
	E3_RING_SPEED = GameState.cfg("boss.enrage.type_3.ring_speed", E3_RING_SPEED)
	E3_RELEASE_RING_COUNT = GameState.cfg("boss.enrage.type_3.release_ring_count", E3_RELEASE_RING_COUNT)
	E3_RELEASE_RING_SPEED = GameState.cfg("boss.enrage.type_3.release_ring_speed", E3_RELEASE_RING_SPEED)
	_press_timer = PRESS_INTERVAL
	DIFF_INTERVAL_MULT = GameState.cfg("boss.difficulty_scaling.interval_mult", DIFF_INTERVAL_MULT)
	DIFF_SPEED_MULT = GameState.cfg("boss.difficulty_scaling.speed_mult", DIFF_SPEED_MULT)
	DIFF_COUNT_DELTAS = GameState.cfg("boss.difficulty_scaling.counts", DIFF_COUNT_DELTAS)
	_load_patterns()
	_apply_difficulty_scaling()
	_start_pattern()


## 模式表载入：配置缺键/损坏时逐项回退脚本默认值（AGENTS.md 约定两者保持一致）
## 注意：cfg 返回的是 GameState 缓存 JSON 的共享引用，必须深拷贝，
## 否则 _apply_difficulty_scaling 的 interval 乘算会污染缓存、叠加到后续 Boss 实例
func _load_patterns() -> void:
	var defaults: Dictionary = DEFAULT_PATTERNS[clampi(boss_type, 1, 3)]
	_patterns = defaults.duplicate(true)
	var cfg_patterns: Variant = GameState.cfg("boss.phases.type%d" % boss_type, defaults)
	if cfg_patterns is Dictionary:
		for key in ["p1", "p2"]:
			var list: Variant = (cfg_patterns as Dictionary).get(key, [])
			if list is Array and not (list as Array).is_empty():
				_patterns[key] = (list as Array).duplicate(true)


## 难度分档统一应用（§4.4）：档位 = GameState.difficulty（easy/medium/hard → 索引 0/1/2），
## 在配置载入后一次性乘算。只作用于 Boss 攻击密度/速度：开火间隔 ×1.15/×1/×0.85、
## 弹速 ×0.9/×1/×1.1、弹数按 boss.difficulty_scaling.counts 逐参数增减；
## telegraph 时长、快照弹幕（main 编排）、HP/伤害、机体移速不动。
func _apply_difficulty_scaling() -> void:
	var tier := GameState.DIFFICULTY_ORDER.find(GameState.difficulty)
	if tier < 0:
		tier = 1
	var interval_mult := float(DIFF_INTERVAL_MULT[clampi(tier, 0, DIFF_INTERVAL_MULT.size() - 1)])
	var speed_mult := float(DIFF_SPEED_MULT[clampi(tier, 0, DIFF_SPEED_MULT.size() - 1)])
	# 开火间隔：模式表 interval + 攻击内部节奏
	for phase_key in _patterns:
		for pattern: Dictionary in _patterns[phase_key]:
			if pattern.has("interval"):
				pattern["interval"] = float(pattern["interval"]) * interval_mult
	for i in FIRE_INTERVALS.size():
		FIRE_INTERVALS[i] = float(FIRE_INTERVALS[i]) * interval_mult
	CANNON_INTERVAL *= interval_mult
	ENRAGE_ATTACK_INTERVAL *= interval_mult
	E1_RING_INTERVAL *= interval_mult
	E2_POINT_INTERVAL *= interval_mult
	E3_SUMMON_INTERVAL *= interval_mult
	E3_RING_INTERVAL *= interval_mult
	# 弹速（不含 main 编排的快照激光/环弹）
	FAN_BULLET_SPEED *= speed_mult
	HOMING_BULLET_SPEED *= speed_mult
	SNIPER_BULLET_SPEED *= speed_mult
	CROSS_BULLET_SPEED *= speed_mult
	CANNON_BULLET_SPEED *= speed_mult
	SWEEP_DROP_SPEED *= speed_mult
	VOLLEY_BULLET_SPEED *= speed_mult
	WALL_BULLET_SPEED *= speed_mult
	E1_RING_SPEED *= speed_mult
	E1_SALVO_SPEED *= speed_mult
	E2_SNIPER_SPEED *= speed_mult
	E2_RELEASE_RING_SPEED *= speed_mult
	E3_RING_SPEED *= speed_mult
	E3_RELEASE_RING_SPEED *= speed_mult
	# 弹数：逐参数分档增减，按攻击语义钳制下限
	_d_fan = _count_delta("fan", tier)
	_d_homing = _count_delta("homing", tier)
	CANNON_SHOTS = maxi(1, CANNON_SHOTS + _count_delta("cannon", tier))
	VOLLEY_COUNT = maxi(1, VOLLEY_COUNT + _count_delta("volley", tier))
	WALL_COUNT = maxi(6, WALL_COUNT + _count_delta("wall", tier))
	SWEEP_DROP_COUNT = maxi(1, SWEEP_DROP_COUNT + _count_delta("drops", tier))
	E1_RING_COUNT = maxi(4, E1_RING_COUNT + _count_delta("ring", tier))
	E3_RING_COUNT = maxi(4, E3_RING_COUNT + _count_delta("ring", tier))
	E2_RELEASE_RING_COUNT = maxi(4, E2_RELEASE_RING_COUNT + _count_delta("ring", tier))
	E3_RELEASE_RING_COUNT = maxi(4, E3_RELEASE_RING_COUNT + _count_delta("ring", tier))
	E1_SALVO_COUNT = maxi(4, E1_SALVO_COUNT + _count_delta("salvo", tier))
	E3_SUMMON_COUNT = maxi(1, E3_SUMMON_COUNT + _count_delta("summon", tier))


## 弹数分档取值：boss.difficulty_scaling.counts[key][tier]，缺键/越界回退 0
func _count_delta(key: String, tier: int) -> int:
	var d: Variant = DIFF_COUNT_DELTAS.get(key, [0, 0, 0])
	if d is Array and not (d as Array).is_empty():
		return int((d as Array)[clampi(tier, 0, (d as Array).size() - 1)])
	return 0


func _exit_tree() -> void:
	GameState.unregister_enemy(self)
	_unlock_player_movement()  # 兜底：离场必复位玩家减速，不留残留


func _base_fire_interval() -> float:
	return float(FIRE_INTERVALS[clampi(boss_type - 1, 0, FIRE_INTERVALS.size() - 1)])


## 慢速力场因子（全局机体移速 ×0.8；与狂暴移速倍率相乘）
## 母舰召唤减速带命中时叠加短时乘区（同语义，仅位移）
func _slow_factor() -> float:
	var f := SLOW_FIELD_FACTOR if GameState.buff_count(&"slow_field") > 0 else 1.0
	if _summon_slow_timer > 0.0:
		f *= _summon_slow_factor
	return f


## 母舰召唤减速带命中：duration 秒内位移速度 ×factor
func apply_slow(duration: float, factor: float) -> void:
	_summon_slow_timer = duration
	_summon_slow_factor = factor


func _base_modulate() -> Color:
	return Color(1.5, 0.65, 0.65) if _enraged else Color.WHITE


## 逃跑剩余秒数（HUD 逃跑倒计时读取口，§4.5）
func escape_remaining() -> float:
	return ESCAPE_TIME - _survival


func _physics_process(delta: float) -> void:
	if _summon_slow_timer > 0.0:
		_summon_slow_timer -= delta
	if _escaping:
		# 逃跑离场：向上加速飘出屏幕（不再受弹、不再开火）
		_escape_speed += ESCAPE_ACCEL * delta
		position.y -= _escape_speed * delta
		if position.y < -280.0:
			escaped.emit()
			died.emit()  # 离场通知（血条/生成器重排）；非击毁，无击杀奖励
			queue_free()
		return
	if not _in_fight:
		position.y += ENTER_SPEED * _slow_factor() * delta
		if position.y >= FIGHT_Y:
			_in_fight = true
			health_changed.emit(hp, max_hp)
		return

	# 存活计时：50s 未被击杀则逃跑；最后 3s 警告 + 上飘
	_survival += delta
	if _survival >= ESCAPE_TIME:
		_begin_escape()
		return
	if _survival >= ESCAPE_TIME - ESCAPE_WARNING and not _escape_warned:
		_escape_warned = true
		_show_escape_warning()

	# 狂暴序列接管移动与开火（逃跑计时照常走，序列中到点照样逃跑；撞击判定保留）
	if _enrage_phase != EnragePhase.NONE:
		if _survival >= ESCAPE_TIME - ESCAPE_WARNING:
			_sprite.modulate = (
				Color(1.8, 1.3, 0.5) if int(_survival * 8.0) % 2 == 0 else _base_modulate()
			)
		_update_enrage_sequence(delta)
		_check_body_collision()
		return

	if _survival >= ESCAPE_TIME - ESCAPE_WARNING:
		position.y -= ESCAPE_DRIFT * delta
		_sprite.modulate = (
			Color(1.8, 1.3, 0.5) if int(_survival * 8.0) % 2 == 0 else _base_modulate()
		)

	# 冲刺掠过（二型 P2）接管移动与模式编排；否则走位 + 模式表循环
	if _sweep_state != SweepState.NONE:
		_update_sweep(delta)
	else:
		# 走位与攻击解耦（§4.1）：每型每阶段一个走位函数，攻击在其上叠加
		_update_movement(delta)

		# 模式表循环：波间隔由当前模式给出，波次/时长播完切下一个
		# （狂暴「余怒」射速 ×1.3：计时器流速加快，§5.4）
		_fire_timer -= delta * (ENRAGE_RATE_MULT if _enraged else 1.0)
		if _fire_timer <= 0.0:
			var pattern := _current_pattern()
			_fire_timer = float(pattern.get("interval", _base_fire_interval()))
			_execute_attack(StringName(pattern.get("attack", &"")))
			if not _pattern_is_duration:
				_pattern_left -= 1.0
		if _pattern_is_duration:
			_pattern_left -= delta
		if _pattern_left <= 0.0:
			_advance_pattern()

	# 狙击 telegraph：瞄准线前 0.2s 微跟踪玩家后固定，0.35s 到点沿线出弹（§4.2/§5.2）
	if _sniper_aim_elapsed >= 0.0:
		_sniper_aim_elapsed += delta
		if _sniper_aim_elapsed <= SNIPER_TRACK_TIME:
			_sniper_dir = _player_dir()
			if _aim_line != null:
				_aim_line.points = PackedVector2Array([_sniper_dir * MUZZLE_OFFSET, _sniper_dir * 1200.0])
		if _aim_line != null:
			_aim_line.modulate.a = 0.18 + 0.18 * absf(sin(_sniper_aim_elapsed * 25.0))
		if _sniper_aim_elapsed >= SNIPER_AIM_TIME:
			_cancel_aim_line()
			_sniper_aim_elapsed = -1.0
			_burst_left = 3
			_burst_timer = 0.0
			_burst_dir = _sniper_dir

	# 游击型 3 连发狙击（telegraph 锁定时沿固定方向，否则自机狙）
	if _burst_left > 0:
		_burst_timer -= delta
		if _burst_timer <= 0.0:
			_burst_timer = 0.12
			_burst_left -= 1
			_fire_sniper(_burst_dir)
			if _burst_left == 0:
				_burst_dir = Vector2.ZERO

	# 蓄力重炮（一型 P2）：蓄力 0.6s 后 3 连重弹（每发 0.15s 短蓄力闪光）
	if _cannon_elapsed >= 0.0:
		_cannon_elapsed += delta
		if _cannon_elapsed >= CANNON_CHARGE:
			_cannon_elapsed = -1.0
			_cannon_shots_left = CANNON_SHOTS
			_cannon_timer = 0.0
			_cannon_flashed = true  # 首发的 telegraph 即 0.6s 蓄力辉光
	if _cannon_shots_left > 0:
		_cannon_timer -= delta
		if not _cannon_flashed and _cannon_timer <= CANNON_FLASH:
			_cannon_flashed = true
			_charge_glow(CANNON_FLASH, 90.0 * _ws, Color(1.0, 0.7, 0.3, 0.6))
		if _cannon_timer <= 0.0:
			_cannon_timer = CANNON_INTERVAL
			_cannon_shots_left -= 1
			_cannon_flashed = false
			_fire_heavy(_player_dir(), CANNON_BULLET_SPEED, CANNON_DAMAGE)

	# 编队齐射（三型 P2）：横队小怪 0.8s 后齐射一轮自机狙，随后恢复正常 AI
	if _volley_timer > 0.0:
		_volley_timer -= delta
		if _volley_timer <= 0.0:
			_minion_volley_fire(_volley_minions)
			_volley_minions.clear()

	# 母舰型召唤小怪（独立计时，不占模式表）
	if boss_type == 3:
		_summon_timer -= delta
		if _summon_timer <= 0.0:
			_summon_timer = 6.0
			_summon_minions()

	_check_body_collision()


# ---------------- 阶段框架与模式表（§4.1） ----------------

## 当前模式（ENRAGE「余怒」沿用 P2 表提速）
func _current_pattern() -> Dictionary:
	var list: Array = _patterns["p1" if _fight_phase == FightPhase.P1 else "p2"]
	return list[_pattern_index % list.size()]


## 进入当前模式：初始化波次/时长与首波间隔
func _start_pattern() -> void:
	var pattern := _current_pattern()
	_pattern_is_duration = not pattern.has("waves")
	if _pattern_is_duration:
		_pattern_left = float(pattern.get("duration", 6.0))
	else:
		_pattern_left = float(pattern.get("waves", 1))
	_fire_timer = float(pattern.get("interval", _base_fire_interval()))


func _advance_pattern() -> void:
	var list: Array = _patterns["p1" if _fight_phase == FightPhase.P1 else "p2"]
	_pattern_index = (_pattern_index + 1) % list.size()
	_start_pattern()


## 攻击分发：模式表只存 attack id，波次/间隔编排与本函数无关
func _execute_attack(attack: StringName) -> void:
	match attack:
		&"fan5":
			_fire_fan(maxi(3, 5 + _d_fan))
		&"fan7":
			_fire_fan(maxi(3, 7 + _d_fan))
		&"homing":
			_fire_homing()
		&"homing2":
			var homing_count: int = maxi(1, 2 + _d_homing)
			for i in homing_count:
				_fire_homing(Vector2((float(i) - float(homing_count - 1) * 0.5) * 80.0, 100.0))
		&"sniper3":
			_start_sniper_volley()
		&"cross":
			_fire_cross()
		&"charged_cannon":
			_start_charged_cannon()
		&"dash_sweep":
			_start_dash_sweep()
		&"minion_volley":
			_start_minion_volley()
		&"bullet_wall":
			_fire_bullet_wall()
		_:
			push_warning("[BOSS] 未知攻击 id: %s" % attack)


## P1→P2 段切换：0.6s 蓄力辉光 + 抖屏 + 变调音效 + 清自身开火计时（§4.1），模式表重置循环
func _enter_phase(p_phase: int) -> void:
	_fight_phase = p_phase
	_pattern_index = 0
	_start_pattern()
	_fire_timer = PHASE_SHIFT_DURATION  # 段切换蓄力期停火
	_burst_left = 0
	_burst_dir = Vector2.ZERO
	_cancel_aim_line()
	_sniper_aim_elapsed = -1.0
	_charge_glow(PHASE_SHIFT_DURATION)
	GameState.shake(GameState.cfg("effects.shake.enrage", 16.0) * 0.5)
	GameState.play_sfx(GameState.SFX_EXPLOSION_BIG, -10.0, 0.7)
	phase_changed.emit(p_phase)


# ---------------- 走位（与攻击解耦；阶段 A 仅一型 P1 纵向下压，其余保持现状） ----------------

func _update_movement(delta: float) -> void:
	match boss_type:
		1:
			_move_bulwark(delta)
		2:
			_move_dash(delta)
		3:
			_move_strafe(delta, float(STRAFE_SPEEDS[2]))


## 一型「堡垒」：慢速 strafe + P1 每 6s 纵向下压 80px 再回（§5.1）
func _move_bulwark(delta: float) -> void:
	_move_strafe(delta, float(STRAFE_SPEEDS[0]))
	if _fight_phase == FightPhase.P1:
		_update_press(delta)


## 纵向下压：周期最后 1.6s 窗口内正弦下压再回升（增量式施加，不覆盖逃跑上飘）
func _update_press(delta: float) -> void:
	_press_timer -= delta
	if _press_timer <= 0.0:
		_press_timer = PRESS_INTERVAL
	const PRESS_WINDOW := 1.6
	var elapsed := PRESS_INTERVAL - _press_timer
	var target := 0.0
	if elapsed >= PRESS_INTERVAL - PRESS_WINDOW:
		target = PRESS_DEPTH * sin(PI * (elapsed - (PRESS_INTERVAL - PRESS_WINDOW)) / PRESS_WINDOW)
	position.y += target - _press_offset
	_press_offset = target


## 巡航范围随可见世界区域收窄（zoom=1 时与配置值 STRAFE_MIN_X/MAX_X 一致）
func _strafe_range() -> Vector2:
	var view := GameState.view_world_rect()
	var lo := view.position.x + STRAFE_MIN_X
	var hi := maxf(view.end.x - (1920.0 - STRAFE_MAX_X), lo)
	return Vector2(lo, hi)


func _move_strafe(delta: float, p_speed: float) -> void:
	position.x += _strafe_dir * p_speed * _slow_factor() * (ENRAGE_SPEED_MULT if _enraged else 1.0) * delta
	var bounds := _strafe_range()
	if position.x < bounds.x or position.x > bounds.y:
		_strafe_dir = -_strafe_dir
		position.x = clampf(position.x, bounds.x, bounds.y)


func _move_dash(delta: float) -> void:
	_move_timer -= delta
	if _move_timer <= 0.0:
		_dashing = not _dashing
		_move_timer = 0.5 if _dashing else 0.7
		if _dashing:
			# 偏向屏幕中心方向冲刺，避免长期贴边
			_strafe_dir = signf(960.0 - position.x) if randf() < 0.6 else (-_strafe_dir)
			if _strafe_dir == 0.0:
				_strafe_dir = 1.0
	if _dashing:
		position.x += _strafe_dir * float(STRAFE_SPEEDS[1]) * _slow_factor() * (ENRAGE_SPEED_MULT if _enraged else 1.0) * delta
		var bounds := _strafe_range()
		if position.x < bounds.x or position.x > bounds.y:
			_strafe_dir = -_strafe_dir
			position.x = clampf(position.x, bounds.x, bounds.y)


# ---------------- Telegraph 小函数（§4.2，用完即毁，不走常驻 _process） ----------------

## 蓄力辉光：叠加态圆点 scale/alpha tween，duration 后自毁（过场 _glow 配方）
func _charge_glow(duration: float, radius := -1.0, color := Color(1.0, 0.55, 0.3, 0.55)) -> Node2D:
	if radius < 0.0:
		radius = 70.0 * _ws  # 默认辉光半径设计值 × 全局缩放
	var dot := _GlowDot.new()
	dot.radius = radius
	dot.dot_color = color
	var mat := CanvasItemMaterial.new()
	mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	dot.material = mat
	dot.scale = Vector2.ONE * 0.3
	dot.modulate.a = 0.0
	add_child(dot)
	var tween := dot.create_tween()
	tween.set_parallel(true)
	tween.tween_property(dot, "scale", Vector2.ONE, duration * 0.6)
	tween.tween_property(dot, "modulate:a", 1.0, duration * 0.4)
	tween.chain().tween_property(dot, "modulate:a", 0.0, duration * 0.4)
	tween.tween_callback(dot.queue_free)
	return dot


## 瞄准线：α0.3 闪烁细线（闪烁由 Boss 在 telegraph 期间驱动），出弹/中断即毁
func _make_aim_line(dir: Vector2, length: float, color := Color(1.0, 0.35, 0.3, 0.9)) -> Line2D:
	var line := Line2D.new()
	line.width = 2.0
	line.default_color = color
	line.modulate.a = 0.3
	line.add_point(dir * MUZZLE_OFFSET)
	line.add_point(dir * length)
	add_child(line)
	return line


func _cancel_aim_line() -> void:
	if _aim_line != null:
		_aim_line.queue_free()
		_aim_line = null


## 狙击 3 连发 telegraph 起手：瞄准线随玩家微跟踪 0.2s 后固定，0.35s 到点沿线出弹
func _start_sniper_volley() -> void:
	if _sniper_aim_elapsed >= 0.0:
		return  # 已有进行中的 telegraph（间隔短于 telegraph 时不叠加）
	_sniper_aim_elapsed = 0.0
	_sniper_dir = _player_dir()
	_aim_line = _make_aim_line(_sniper_dir, 1200.0)


# ---------------- 攻击库（弹种实现；编排全部在模式表） ----------------

func _player_dir() -> Vector2:
	if GameState.player_ref != null:
		return (GameState.player_ref.global_position - global_position).normalized()
	return Vector2.DOWN


func _fire_fan(p_count: int = 5) -> void:
	var base_dir := _player_dir()
	var half := float(p_count - 1) * 0.5
	for i in p_count:
		var dir := base_dir.rotated(deg_to_rad(20.0 * (float(i) - half)))
		var b: Bullet = GameState.bullet_pool.fire(dir, FAN_BULLET_SPEED, BULLET_DAMAGE_FAN, false)
		b.position = position + dir * MUZZLE_OFFSET


func _fire_homing(p_offset := Vector2(0.0, 100.0)) -> void:
	var b: Bullet = GameState.bullet_pool.fire(Vector2.DOWN, HOMING_BULLET_SPEED, BULLET_DAMAGE_HOMING, false, true, 1.5)
	b.position = position + p_offset * _ws


## 狙击弹：p_dir 为零向量时自机狙（保留旧语义），否则沿 telegraph 锁定方向
func _fire_sniper(p_dir := Vector2.ZERO) -> void:
	var dir := p_dir if p_dir != Vector2.ZERO else _player_dir()
	var b: Bullet = GameState.bullet_pool.fire(dir, SNIPER_BULLET_SPEED, BULLET_DAMAGE_SNIPER, false)
	b.position = position + dir * MUZZLE_OFFSET


func _fire_cross() -> void:
	for i in 4:
		var dir := Vector2.RIGHT.rotated(_cross_angle + float(i) * PI / 2.0)
		var b: Bullet = GameState.bullet_pool.fire(dir, CROSS_BULLET_SPEED, BULLET_DAMAGE_CROSS, false)
		b.position = position + dir * MUZZLE_OFFSET
	_cross_angle += deg_to_rad(15.0)


## 重弹（蓄力重炮/狂暴齐射/猎杀狙击共用）：高亮加粗外观
func _fire_heavy(p_dir: Vector2, p_speed: float, p_damage: int) -> void:
	var b: Bullet = GameState.bullet_pool.fire(p_dir, p_speed, p_damage, false)
	b.position = position + p_dir * MUZZLE_OFFSET
	var poly := b.get_node("Polygon2D") as Polygon2D
	poly.scale = Vector2(2.4, 2.4)
	poly.color = Color(1.0, 0.6, 0.3)


## 环弹（差异化狂暴各型共用）：meta=enrage_ring（与快照环弹同标记）
func _fire_ring(p_count: int, p_speed: float, p_damage: int, p_offset: float) -> void:
	for i in p_count:
		var dir := Vector2.RIGHT.rotated(p_offset + TAU * float(i) / float(p_count))
		var b: Bullet = GameState.bullet_pool.fire(dir, p_speed, p_damage, false)
		b.position = position + dir * MUZZLE_OFFSET
		b.set_meta("bullet_type", &"enrage_ring")


## 蓄力重炮（一型 P2）：0.6s 蓄力辉光起手，连发由 _physics_process 驱动
func _start_charged_cannon() -> void:
	if _cannon_elapsed >= 0.0 or _cannon_shots_left > 0:
		return
	_cannon_elapsed = 0.0
	_charge_glow(CANNON_CHARGE)


## 冲刺掠过（二型 P2）：0.5s 水平瞄准线（预警横穿玩家当前高度）起手
func _start_dash_sweep() -> void:
	if _sweep_state != SweepState.NONE:
		return
	_sweep_state = SweepState.AIM
	_sweep_timer = SWEEP_AIM
	var player_x := 960.0
	var dy := 300.0
	if GameState.player_ref != null:
		player_x = GameState.player_ref.global_position.x
		dy = GameState.player_ref.global_position.y - position.y
	_sweep_dir = signf(player_x - position.x)
	if _sweep_dir == 0.0:
		_sweep_dir = 1.0
	_sweep_origin = position
	_cancel_sweep_line()
	_sweep_line = Line2D.new()
	_sweep_line.width = 2.0
	_sweep_line.default_color = Color(1.0, 0.35, 0.3, 0.9)
	_sweep_line.modulate.a = 0.3
	_sweep_line.add_point(Vector2(-1600.0, dy))
	_sweep_line.add_point(Vector2(1600.0, dy))
	add_child(_sweep_line)


## 冲刺掠过驱动：AIM（瞄准线闪烁）→ DASH（高速横穿 + 等距拖 3 枚减速弹）
## → RETURN（smoothstep 飞回巡航位，复用狂暴 RETURN 插值模式）
func _update_sweep(delta: float) -> void:
	match _sweep_state:
		SweepState.AIM:
			_sweep_timer -= delta
			if _sweep_line != null:
				_sweep_line.modulate.a = 0.18 + 0.18 * absf(sin(_sweep_timer * 25.0))
			if _sweep_timer <= 0.0:
				_cancel_sweep_line()
				_sweep_state = SweepState.DASH
				# 拖弹点：横穿路径 1/4、1/2、3/4 处
				var bounds := _strafe_range()
				var end_x := bounds.y if _sweep_dir > 0.0 else bounds.x
				_sweep_drop_x.clear()
				for i in SWEEP_DROP_COUNT:
					_sweep_drop_x.append(lerpf(position.x, end_x, float(i + 1) / float(SWEEP_DROP_COUNT + 1)))
		SweepState.DASH:
			position.x += _sweep_dir * SWEEP_SPEED * _slow_factor() * delta
			while not _sweep_drop_x.is_empty():
				var drop_x: float = _sweep_drop_x[0]
				if (_sweep_dir > 0.0 and position.x >= drop_x) or (_sweep_dir < 0.0 and position.x <= drop_x):
					_sweep_drop_x.remove_at(0)
					var b: Bullet = GameState.bullet_pool.fire(Vector2.DOWN, SWEEP_DROP_SPEED, SWEEP_DROP_DAMAGE, false)
					b.position = position + Vector2(0.0, 60.0) * _ws
				else:
					break
			var bounds := _strafe_range()
			if (_sweep_dir > 0.0 and position.x >= bounds.y) or (_sweep_dir < 0.0 and position.x <= bounds.x):
				position.x = clampf(position.x, bounds.x, bounds.y)
				_sweep_state = SweepState.RETURN
				_sweep_timer = SWEEP_RETURN_DURATION
				_sweep_origin = position
				_sweep_return_target = Vector2(clampf(960.0, bounds.x, bounds.y), FIGHT_Y)
		SweepState.RETURN:
			_sweep_timer -= delta
			var t := clampf(1.0 - _sweep_timer / SWEEP_RETURN_DURATION, 0.0, 1.0)
			var eased := t * t * (3.0 - 2.0 * t)
			position = _sweep_origin.lerp(_sweep_return_target, eased)
			if _sweep_timer <= 0.0:
				_sweep_state = SweepState.NONE
				_fire_timer = float(_current_pattern().get("interval", _base_fire_interval()))


func _cancel_sweep_line() -> void:
	if _sweep_line != null:
		_sweep_line.queue_free()
		_sweep_line = null


## 序列中断清理：瞄准线/拖弹点/状态复位（位置由调用方接管）
func _cancel_sweep() -> void:
	_cancel_sweep_line()
	_sweep_state = SweepState.NONE
	_sweep_drop_x.clear()


## 编队齐射（三型 P2）：召唤 4 小怪列横队（meta 标记），0.8s 后齐射由 _physics_process 驱动
func _start_minion_volley() -> void:
	var spawner := get_tree().get_first_node_in_group("spawner")
	if spawner == null:
		return
	_volley_minions.clear()
	for i in VOLLEY_COUNT:
		var e: Enemy = spawner.spawn_minion(
			position + Vector2((float(i) - float(VOLLEY_COUNT - 1) * 0.5) * 100.0, 110.0) * _ws
		)
		if e != null:
			e.set_meta("hive_volley", true)
			_volley_minions.append(e)
	_volley_timer = VOLLEY_DELAY


## 齐射一轮自机狙（普通敌弹口径；P2 编队与狂暴倾巢收尾共用）
func _minion_volley_fire(minions: Array[Enemy]) -> void:
	if GameState.player_ref == null:
		return
	for e in minions:
		if is_instance_valid(e) and e._active:
			var dir := (GameState.player_ref.global_position - e.global_position).normalized()
			var b: Bullet = GameState.bullet_pool.fire(dir, VOLLEY_BULLET_SPEED, VOLLEY_BULLET_DAMAGE, false)
			b.position = e.position + dir * 40.0 * _ws


## 弹幕墙（三型 P2）：150° 扇形 10 槽位，留 2 个相邻缺口；
## 缺口方位避开自机当前方位 ±30°（无可行槽位时退化为离自机最远的槽，保证理论上可躲）
func _fire_bullet_wall() -> void:
	var arc := deg_to_rad(WALL_ARC_DEG)
	var base := Vector2.DOWN.angle()
	var to_player := _player_dir().angle()
	var min_gap := deg_to_rad(30.0)
	var slot_angle := func(i: int) -> float:
		return base - arc * 0.5 + arc * float(i) / float(WALL_COUNT - 1)
	var candidates: Array[int] = []
	for g in WALL_COUNT - 1:
		if (
			absf(angle_difference(slot_angle.call(g), to_player)) > min_gap
			and absf(angle_difference(slot_angle.call(g + 1), to_player)) > min_gap
		):
			candidates.append(g)
	var gap_start := -1
	if candidates.is_empty():
		var best_dist := -1.0
		for g in WALL_COUNT - 1:
			var d := minf(
				absf(angle_difference(slot_angle.call(g), to_player)),
				absf(angle_difference(slot_angle.call(g + 1), to_player))
			)
			if d > best_dist:
				best_dist = d
				gap_start = g
	else:
		gap_start = candidates[randi() % candidates.size()]
	for i in WALL_COUNT:
		if i == gap_start or i == gap_start + 1:
			continue
		var dir := Vector2.from_angle(slot_angle.call(i))
		var b: Bullet = GameState.bullet_pool.fire(dir, WALL_BULLET_SPEED, WALL_DAMAGE, false)
		b.position = position + dir * MUZZLE_OFFSET


func _summon_minions() -> void:
	var spawner := get_tree().get_first_node_in_group("spawner")
	if spawner == null:
		return
	for i in randi_range(2, 3):
		spawner.spawn_minion(position + Vector2(randf_range(-80.0, 80.0), 110.0) * _ws)


## 狂暴快照弹幕：狂暴进入时的一次性齐射（由 main 在子弹时间结束后统一触发）。
## 4 道激光向弹（高速长弹，复用敌弹 laser 型表现）+ 8 方向环形慢弹。
func fire_enrage_snapshot() -> void:
	_fire_enrage_wave(ENRAGE_LASER_SPEED, ENRAGE_RING_SPEED)


## RELEASE_HOLD 密集释放（未差异化回退路径）：同构弹幕但用慢速
func _fire_enrage_release() -> void:
	_fire_enrage_wave(ENRAGE_RELEASE_LASER_SPEED, ENRAGE_RELEASE_RING_SPEED)


func _fire_enrage_wave(laser_speed: float, ring_speed: float) -> void:
	if _escaping:
		return
	var aim := _player_dir()
	var side := aim.orthogonal()
	for i in ENRAGE_SNAPSHOT_LASERS:
		var laser: Bullet = GameState.bullet_pool.fire(aim, laser_speed, BULLET_DAMAGE_SNAPSHOT_LASER, false)
		laser.position = position + aim * MUZZLE_OFFSET + side * (float(i) - 1.5) * 44.0 * _ws
		laser.set_meta("bullet_type", &"laser")
		# 细长高亮快速弹（与敌机 laser 弹同表现，polygon 尖端朝 +x 即飞行方向）
		var poly := laser.get_node("Polygon2D") as Polygon2D
		poly.scale = Vector2(2.2, 0.55)
		poly.color = Color(1.0, 0.85, 0.35)
	for i in ENRAGE_SNAPSHOT_RING:
		var dir := Vector2.RIGHT.rotated(TAU * float(i) / float(ENRAGE_SNAPSHOT_RING))
		var b: Bullet = GameState.bullet_pool.fire(dir, ring_speed, BULLET_DAMAGE_SNAPSHOT_RING, false)
		b.position = position + dir * MUZZLE_OFFSET
		b.set_meta("bullet_type", &"enrage_ring")


## 狂暴序列驱动：TRANSITION（蓄力抖动滑入轨道，1 型悬停原地）→ ACTIVE（各型差异化攻击）
## → RELEASE_HOLD（各型收尾爆发，§5.4 峰值）→ RETURN（飞回战斗位）→ NONE（常规「余怒」循环）
func _update_enrage_sequence(delta: float) -> void:
	match _enrage_phase:
		EnragePhase.TRANSITION:
			_enrage_timer = maxf(_enrage_timer - delta, 0.0)
			_enrage_transition_timer -= delta
			var t := clampf(1.0 - _enrage_transition_timer / ENRAGE_TRANSITION_DURATION, 0.0, 1.0)
			var eased := 1.0 - pow(1.0 - t, 3.0)
			var shake := Vector2(
				Enemy.sin_fast(t * TAU * 7.0) * (1.0 - t) * 13.0,
				Enemy.cos_fast(t * TAU * 5.0) * (1.0 - t) * 8.0
			)
			# 1 型「旋转堡垒」悬停原地，不滑入轨道
			var target_pos := (
				_enrage_transition_origin
				if boss_type == 1
				else _enrage_path_center(_enrage_progress())
			)
			position = _enrage_transition_origin.lerp(target_pos, eased) + shake
			if _enrage_transition_timer <= 0.0:
				_enrage_phase = EnragePhase.ACTIVE
				_enrage_attack_timer = ENRAGE_ATTACK_WINDUP
				_enrage_attack_index = 0
		EnragePhase.ACTIVE:
			_enrage_timer = maxf(_enrage_timer - delta, 0.0)
			match boss_type:
				1:
					_enrage_active_bulwark(delta)
				2:
					_enrage_active_stalker(delta)
				3:
					_enrage_active_hive(delta)
				_:
					position = _enrage_path_center(_enrage_progress())
					_enrage_attack_timer -= delta
					if _enrage_attack_timer <= 0.0:
						_enrage_attack_timer = ENRAGE_ATTACK_INTERVAL
						_enrage_attack_index += 1
						fire_enrage_snapshot()
			if _enrage_timer <= 0.0:
				_begin_release_hold()
		EnragePhase.RELEASE_HOLD:
			_enrage_release_hold_timer -= delta
			match boss_type:
				1:
					# 8 路蓄力重炮齐射（蓄力辉光 telegraph 已在 _begin_release_hold 起手）
					if not _enrage_release_salvo_done:
						_enrage_attack_timer -= delta
						if _enrage_attack_timer <= 0.0:
							_enrage_release_salvo_done = true
							for i in E1_SALVO_COUNT:
								var dir := Vector2.RIGHT.rotated(TAU * float(i) / float(E1_SALVO_COUNT))
								_fire_heavy(dir, E1_SALVO_SPEED, E1_SALVO_DAMAGE)
				2:
					# 回轨道底部放 12 向慢速环弹
					var t := clampf(1.0 - _enrage_release_hold_timer / ENRAGE_RELEASE_HOLD_DURATION, 0.0, 1.0)
					var eased := t * t * (3.0 - 2.0 * t)
					position = _enrage_release_origin.lerp(
						_enrage_snapshot_target + Vector2(0.0, _enrage_path_radius()), eased
					)
					if not _enrage_release_salvo_done and t >= 0.5:
						_enrage_release_salvo_done = true
						_fire_ring(E2_RELEASE_RING_COUNT, E2_RELEASE_RING_SPEED, BULLET_DAMAGE_SNAPSHOT_RING, 0.0)
				3:
					pass  # 16 向环弹 + 小怪齐射已在 _begin_release_hold 一次性结算
				_:
					_enrage_attack_timer -= delta
					if _enrage_attack_timer <= 0.0:
						_enrage_attack_timer = ENRAGE_RELEASE_INTERVAL
						_fire_enrage_release()
			if _enrage_release_hold_timer <= 0.0:
				_begin_return()
		EnragePhase.RETURN:
			_enrage_return_timer -= delta
			var t := clampf(1.0 - _enrage_return_timer / ENRAGE_RETURN_DURATION, 0.0, 1.0)
			var eased := t * t * (3.0 - 2.0 * t)
			position = _enrage_return_origin.lerp(_enrage_return_target, eased)
			if _enrage_return_timer <= 0.0:
				_enrage_phase = EnragePhase.NONE


## 1 型「旋转堡垒」ACTIVE：悬停原地，每 0.5s 一波 12 向环弹（起始角随波次进动）
func _enrage_active_bulwark(delta: float) -> void:
	_enrage_attack_timer -= delta
	if _enrage_attack_timer <= 0.0:
		_enrage_attack_timer = E1_RING_INTERVAL
		_fire_ring(E1_RING_COUNT, E1_RING_SPEED, BULLET_DAMAGE_SNAPSHOT_RING, _enrage_ring_angle)
		_enrage_ring_angle += deg_to_rad(E1_RING_PRECESSION_DEG)
		_enrage_attack_index += 1


## 2 型「猎杀环绕」ACTIVE：轨道 4 象限 6 点依次瞬停，每点 0.3s 瞄准线 + 单发狙
func _enrage_active_stalker(delta: float) -> void:
	if _enrage_aim_elapsed >= 0.0:
		_enrage_aim_elapsed += delta
		_sniper_dir = _player_dir()
		if _aim_line != null:
			_aim_line.points = PackedVector2Array([_sniper_dir * MUZZLE_OFFSET, _sniper_dir * 1200.0])
			_aim_line.modulate.a = 0.18 + 0.18 * absf(sin(_enrage_aim_elapsed * 25.0))
		if _enrage_aim_elapsed >= E2_AIM:
			_cancel_aim_line()
			_enrage_aim_elapsed = -1.0
			_fire_heavy(_sniper_dir, E2_SNIPER_SPEED, E2_SNIPER_DAMAGE)
	_enrage_attack_timer -= delta
	if _enrage_attack_timer <= 0.0 and _enrage_attack_index < E2_POINT_COUNT:
		var angle := deg_to_rad(STALKER_POINT_ANGLES_DEG[_enrage_attack_index % STALKER_POINT_ANGLES_DEG.size()])
		position = (
			_enrage_snapshot_target
			+ Vector2(Enemy.cos_fast(angle), Enemy.sin_fast(angle)) * _enrage_path_radius()
		)
		_enrage_attack_index += 1
		_enrage_attack_timer = E2_POINT_INTERVAL
		_cancel_aim_line()
		_enrage_aim_elapsed = 0.0
		_sniper_dir = _player_dir()
		_aim_line = _make_aim_line(_sniper_dir, 1200.0)


## 3 型「倾巢」ACTIVE：共用轨道环绕 + 每 1.2s 一波 3 小怪（共 3 波）+ 每 0.9s 一圈 8 向环弹
func _enrage_active_hive(delta: float) -> void:
	position = _enrage_path_center(_enrage_progress())
	_enrage_attack_timer -= delta
	if _enrage_attack_timer <= 0.0:
		_enrage_attack_timer = E3_RING_INTERVAL
		_fire_ring(E3_RING_COUNT, E3_RING_SPEED, BULLET_DAMAGE_SNAPSHOT_RING, 0.0)
		_enrage_attack_index += 1
	if _enrage_summon_waves < E3_SUMMON_WAVES:
		_enrage_summon_timer -= delta
		if _enrage_summon_timer <= 0.0:
			_enrage_summon_timer = E3_SUMMON_INTERVAL
			_enrage_summon_waves += 1
			var spawner := get_tree().get_first_node_in_group("spawner")
			if spawner != null:
				for i in E3_SUMMON_COUNT:
					spawner.spawn_minion(position + Vector2(randf_range(-80.0, 80.0), 110.0) * _ws)


## 序列进度 0→1（TRANSITION 起算，ACTIVE 结束到 1；对齐原作 enrage_progress）
func _enrage_progress() -> float:
	return clampf(1.0 - _enrage_timer / ENRAGE_DURATION, 0.0, 1.0)


## 轨道半径：max(机体宽,高)×1.5，受屏幕边界约束（对齐原作 enrage_path_radius，下限 24）
func _enrage_path_radius() -> float:
	var base := maxf(_boss_size.x, _boss_size.y) * ENRAGE_PATH_RADIUS_SCALE
	var view := GameState.view_world_rect()
	var half := _boss_size * 0.5
	var max_radius := maxf(24.0, minf(
		minf(
			_enrage_snapshot_target.x - view.position.x - half.x,
			view.end.x - _enrage_snapshot_target.x - half.x
		),
		minf(
			_enrage_snapshot_target.y - view.position.y - half.y,
			view.end.y - _enrage_snapshot_target.y - half.y
		)
	))
	return minf(base, max_radius)


## 轨道中心：前 48% 方形路径（底→左→顶→右→底），后 52% 圆形路径（底部起顺接）
func _enrage_path_center(progress: float) -> Vector2:
	progress = clampf(progress, 0.0, 1.0)
	var radius := _enrage_path_radius()
	var c := _enrage_snapshot_target
	if progress <= ENRAGE_SQUARE_PATH_RATIO:
		var sp := progress / ENRAGE_SQUARE_PATH_RATIO
		var segment := mini(3, int(sp * 4.0))
		var local := sp * 4.0 - float(segment)
		var points: Array[Vector2] = [
			c + Vector2(0.0, radius),
			c + Vector2(-radius, 0.0),
			c + Vector2(0.0, -radius),
			c + Vector2(radius, 0.0),
			c + Vector2(0.0, radius),
		]
		return points[segment].lerp(points[segment + 1], local)
	var cp := (progress - ENRAGE_SQUARE_PATH_RATIO) / (1.0 - ENRAGE_SQUARE_PATH_RATIO)
	var angle := PI / 2.0 + cp * TAU
	return c + Vector2(Enemy.cos_fast(angle), Enemy.sin_fast(angle)) * radius


## ACTIVE 计时耗尽：进入释放阶段——解血锁、复位玩家减速 + 各型收尾爆发起手（§5.4 峰值）
func _begin_release_hold() -> void:
	_enrage_phase = EnragePhase.RELEASE_HOLD
	_enrage_release_hold_timer = ENRAGE_RELEASE_HOLD_DURATION
	_enrage_health_lock = false
	_unlock_player_movement()
	_cancel_aim_line()
	_enrage_aim_elapsed = -1.0
	_enrage_release_salvo_done = false
	match boss_type:
		1:
			_enrage_attack_timer = E1_SALVO_CHARGE
			_charge_glow(E1_SALVO_CHARGE)
		2:
			_enrage_release_origin = position
		3:
			_fire_ring(E3_RELEASE_RING_COUNT, E3_RELEASE_RING_SPEED, BULLET_DAMAGE_SNAPSHOT_RING, 0.0)
			_hive_volley_all_minions()
		_:
			_enrage_attack_timer = 0.0  # 回退路径：立即放第一波


## 倾巢收尾：全部在场小怪齐射一轮自机狙
func _hive_volley_all_minions() -> void:
	var minions: Array[Enemy] = []
	for e in GameState.enemies:
		if e is Enemy and (e as Enemy)._active:
			minions.append(e)
	_minion_volley_fire(minions)


## RELEASE_HOLD 结束：0.8s 飞回战斗位（x 钳回巡航范围、y 回 FIGHT_Y）
func _begin_return() -> void:
	_enrage_phase = EnragePhase.RETURN
	_enrage_return_timer = ENRAGE_RETURN_DURATION
	_enrage_return_origin = position
	var bounds := _strafe_range()
	_enrage_return_target = Vector2(clampf(position.x, bounds.x, bounds.y), FIGHT_Y)


## 序列中断（逃跑/死亡/离场/教程收尾）：清状态 + 解血锁 + 复位减速 + 清 telegraph，幂等
func _abort_enrage_sequence() -> void:
	_enrage_phase = EnragePhase.NONE
	_enrage_health_lock = false
	_cancel_aim_line()
	_sniper_aim_elapsed = -1.0
	_enrage_aim_elapsed = -1.0
	_burst_left = 0
	_burst_dir = Vector2.ZERO
	_cancel_sweep()
	_cannon_elapsed = -1.0
	_cannon_shots_left = 0
	_volley_timer = 0.0
	_volley_minions.clear()
	_unlock_player_movement()


## 狂暴期玩家减速（替代原作 is_controls_locked 定身，§4.3）：移速 ×0.35，
## 仍可瞄准/射击/冲刺；TRANSITION+ACTIVE 有效
func _lock_player_movement() -> void:
	var p := GameState.player_ref
	if p != null and not p._dead:
		_slowed_player = p
		p._enrage_slow = ENRAGE_PLAYER_SLOW


func _unlock_player_movement() -> void:
	if _slowed_player != null:
		if is_instance_valid(_slowed_player):
			_slowed_player._enrage_slow = 1.0
		_slowed_player = null


## 狂暴锁血（对齐原作 boss_sub_state.py compute_take_damage）：致死伤害直接击杀；
## 否则未狂暴时最多把 HP 打到阈值（触发狂暴）；锁血期（触发→RELEASE_HOLD 前）
## 任何伤害不掉血不死；RELEASE_HOLD 解锁后正常扣血可击杀。
## 阶段框架（§4.1）：同阈值驱动 P1→P2（70%）；一击跨两段时狂暴优先（锁血语义不变）。
func take_damage(amount: int, score_scale: float = 1.0) -> void:
	if hp <= 0.0:
		return  # 已死亡待释放（同帧多发命中防重复结算）
	if _enrage_health_lock:
		_flash_hit()  # 锁血期：仅受击闪白反馈，不掉血不死（致死也不死）
		return
	hp -= float(amount)
	_score_scale = score_scale
	if hp > 0.0 and not _enraged and hp < max_hp * ENRAGE_HP_RATIO:
		hp = max_hp * ENRAGE_HP_RATIO
	health_changed.emit(hp, max_hp)
	_flash_hit()
	if hp <= 0.0:
		_die()
	elif not _enraged and hp <= max_hp * ENRAGE_HP_RATIO:
		_enrage()
	elif _fight_phase == FightPhase.P1 and hp <= max_hp * PHASE2_HP_RATIO:
		_enter_phase(FightPhase.P2)


## 受击闪白（锁血期复用）
func _flash_hit() -> void:
	_sprite.modulate = Color(2.0, 2.0, 2.0)
	var tween := create_tween()
	# 游击型受击硬直（闪白）更短
	tween.tween_property(_sprite, "modulate", _base_modulate(), 0.05 if boss_type == 2 else 0.1)


## 身体撞击（对齐原作 boss_vs_player.py 逐帧轮询）：入场降入与逃跑离场阶段不判定；
## 玩家 -30 HP（受击无敌帧节流连撞，无敌结束仍重叠会再次命中），Boss 不掉血、不自毁。
func _check_body_collision() -> void:
	var hb := GameState.player_hitbox
	if hb != null and overlaps_area(hb):
		# 撞体伤害随对局进程 ramp（与 Boss 弹同一系数）；补传撞体位置作伤害源方向（D8）
		(GameState.player_ref as Player).take_damage(
			maxi(1, int(roundf(COLLISION_DAMAGE * GameState.enemy_damage_ramp()))),
			global_position
		)


func _enrage() -> void:
	_enraged = true
	_fight_phase = FightPhase.ENRAGE
	# 中断进行中的常规攻击/telegraph，启动狂暴序列：锁血 30% 检查点 + 快照玩家位置 + 玩家减速
	_cancel_aim_line()
	_sniper_aim_elapsed = -1.0
	_burst_left = 0
	_burst_dir = Vector2.ZERO
	_cancel_sweep()
	_cannon_elapsed = -1.0
	_cannon_shots_left = 0
	_volley_timer = 0.0
	_volley_minions.clear()
	_enrage_ring_angle = 0.0
	_enrage_summon_waves = 0
	_enrage_summon_timer = E3_SUMMON_INTERVAL
	_enrage_aim_elapsed = -1.0
	_enrage_release_salvo_done = false
	_enrage_health_lock = true
	_enrage_phase = EnragePhase.TRANSITION
	_enrage_timer = ENRAGE_DURATION
	_enrage_transition_timer = ENRAGE_TRANSITION_DURATION
	_enrage_transition_origin = position
	_enrage_snapshot_target = (
		GameState.player_ref.global_position
		if GameState.player_ref != null
		else GameState.view_world_rect().get_center()
	)
	_lock_player_movement()
	_sprite.modulate = _base_modulate()
	GameState.shake(GameState.cfg("effects.shake.enrage", 16.0))
	GameState.play_sfx(GameState.SFX_EXPLOSION_BIG, -6.0)
	phase_changed.emit(FightPhase.ENRAGE)
	enraged.emit()


func _die() -> void:
	_abort_enrage_sequence()
	GameState.add_boss_kill(_score_scale)
	# 吸血 buff：Boss 击杀同样触发（对齐原作 boss_manager 路径，每帧至多一次）
	GameState.try_lifesteal()
	Explosion.spawn_boss_sequence(get_parent(), global_position)
	died.emit()
	queue_free()


## 逃跑警告：复用 HUD 警告横幅（不可用时退化为 print），最后 3s 机身闪烁见 _physics_process
func _show_escape_warning() -> void:
	var hud := get_tree().get_first_node_in_group("hud")
	if hud != null and hud.has_method("_show_warning"):
		hud._show_warning("⚠ Boss 试图逃离战场 ⚠")
	else:
		print("[BOSS] 逃跑警告：Boss 即将逃离战场")


## 50s 未被击杀：逃跑（无 add_boss_kill / 加分 / 难度提升 / 轮换推进）
func _begin_escape() -> void:
	_abort_enrage_sequence()  # 序列中断：解血锁 + 复位减速 + 清 telegraph
	_escaping = true
	is_escaped = true
	_escape_speed = ESCAPE_START_SPEED
	collision_layer = 0  # 离场阶段不再受弹
	collision_mask = 0
	_sprite.modulate = _base_modulate()
	print("[BOSS] 存活 %ds 未被击杀，逃离战场（无击杀奖励）" % int(ESCAPE_TIME))
