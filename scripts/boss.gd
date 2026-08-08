class_name Boss
extends Area2D
## Boss：4 种轮换（1 重装 / 2 游击 / 3 母舰 / 4 月蚀），HP 分段驱动阶段框架（BOSS_REDESIGN §4.1）：
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
##     RELEASE 16 向慢速环弹 + 在场小怪齐射；
##   4 型「月蚀」（2026-08-04）：ACTIVE 中心悬停微摆 + 双环反向进动（正环/反角环
##     交替成波，每波进动 E4_PRECESSION_DEG），RELEASE 蓄力环阵（E4_RELEASE_RING_COUNT 向）。
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

const BOSS_SPRITE_1: Texture2D = preload("res://assets/sprites/boss_ship_1.png")
const BOSS_SPRITE_2: Texture2D = preload("res://assets/sprites/boss_ship_2.png")
const BOSS_SPRITE_3: Texture2D = preload("res://assets/sprites/boss_ship_3.png")
## 4 型「月蚀」复用 1 型贴图（环弹术士以弹幕形态区分，2026-08-04）
const TEXTURES: Array[Texture2D] = [BOSS_SPRITE_1, BOSS_SPRITE_2, BOSS_SPRITE_3, BOSS_SPRITE_1]
## 猎杀环绕瞬停点（右→上→左→下→右→上，共 6 点；末点为顶部，RELEASE 回底部）
const STALKER_POINT_ANGLES_DEG: Array[float] = [0.0, -90.0, 180.0, 90.0, 0.0, -90.0]
## 模式表脚本默认值（与 balance.json boss.phases.typeN 保持一致，AGENTS.md 约定）：
## 1 型 P1=[5路扇形,追踪弹] P2=[蓄力重炮,7路扇形]；2 型 P1=[3连狙] P2=[冲刺掠过,3连狙]；
## 3 型 P1=[旋转cross+召唤] P2=[编队齐射,弹幕墙]（召唤为独立计时，不在模式表内）；
## 4 型 P1=[ring_burst×3,追踪弹] P2=[ring_burst×3,旋转cross,3连狙]。
const DEFAULT_PATTERNS: Dictionary = {
	1:
	{
		"p1":
		[
			{"attack": &"fan5", "waves": 3, "interval": 1.6},
			{"attack": &"homing", "waves": 2, "interval": 1.6},
		],
		"p2":
		[
			{"attack": &"charged_cannon", "waves": 1, "interval": 2.4},
			{"attack": &"fan7", "waves": 3, "interval": 1.4},
		],
	},
	2:
	{
		"p1": [{"attack": &"sniper3", "waves": 1, "interval": 1.8}],
		"p2":
		[
			{"attack": &"dash_sweep", "waves": 1, "interval": 2.5},
			{"attack": &"sniper3", "waves": 1, "interval": 1.5},
		],
	},
	3:
	{
		"p1": [{"attack": &"cross", "duration": 6.0, "interval": 0.9}],
		"p2":
		[
			{"attack": &"minion_volley", "waves": 1, "interval": 2.0},
			{"attack": &"bullet_wall", "waves": 1, "interval": 1.5},
		],
	},
	4:
	{
		"p1":
		[
			{"attack": &"ring_burst", "waves": 3, "interval": 1.7},
			{"attack": &"homing", "waves": 2, "interval": 1.5},
		],
		"p2":
		[
			{"attack": &"ring_burst", "waves": 3, "interval": 1.4},
			{"attack": &"cross", "duration": 5.0, "interval": 0.8},
			{"attack": &"sniper3", "waves": 1, "interval": 1.6},
		],
	},
}
## A3 机型参数表：数据驱动取代散落的机型特判（新增机型只加表行，不改既有函数）。
## 独立召唤计时（不占模式表）：3 型「母舰」专属（_physics_process 查询）。
const SUMMONER_TYPES: Dictionary = {3: true, 4: false}
## 受击闪白总时长（游击型更短）：_flash_hit 查询。
const HIT_FLASH_BY_TYPE: Dictionary = {1: 0.1, 2: 0.05, 3: 0.1, 4: 0.1}
## 2026-08-07 审计：逃跑警告闪烁与狂暴底色提常量（原每帧构造 Color）
const ESCAPE_BLINK_COLOR := Color(1.8, 1.3, 0.5)
const ENRAGE_BLINK_COLOR := Color(1.5, 0.65, 0.65)
var ENTER_SPEED := 140.0
## 战斗锚线距可见区域顶缘的偏移（small 档 view.position.y=0 时即绝对 y；使用点一律走 fight_anchor_y()）
var FIGHT_Y := 230.0
var STRAFE_MIN_X := 300.0
var STRAFE_MAX_X := 1620.0
## HP 基底（× 类型系数 × 难度乘数；对齐原作首发 Boss ≈12s TTK 量级）
var HP_BASE := 800.0
## 各类型移动速度 / 开火间隔（模式表 interval 缺键时的回退基准）/ 弹速
var STRAFE_SPEEDS: Array[float] = [150.0, 400.0, 60.0, 40.0]
var FIRE_INTERVALS: Array = [1.6, 1.8, 0.9, 1.2]
var FAN_BULLET_SPEED := 380.0
var HOMING_BULLET_SPEED := 300.0
var SNIPER_BULLET_SPEED := 650.0
var CROSS_BULLET_SPEED := 260.0
## 4 型「月蚀」ring_burst 环弹攻击参数（2026-08-04；默认值与 balance.json 双写）
## 2026-08-05 Q01：难度分档为绝对值（counts.ring_burst = [10,12,14]，§5.6），
## R12（2026-08-05 独立审计）：RING_BURST_COUNT 与 json 键 boss.ring_burst.count
## 删除——Q01 后无任何消费方（弹数全走 _count_delta 回退表），死数据
var RING_BURST_SPEED := 340.0
var BULLET_DAMAGE_RING := 14
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
## 阶段转场公平感（2026-08-03 机制三）：切换时清全部活跃弹丸 + 给玩家短暂无敌
var CLEAR_ON_SHIFT := true
var TRANSITION_INVINCIBLE := 1.0
## 狙击 telegraph（§4.2/§5.2）：瞄准线 0.35s（前 0.2s 微跟踪玩家后固定），到点沿线出弹
var SNIPER_AIM_TIME := 0.35
var SNIPER_TRACK_TIME := 0.2
var SNIPER_BURST_INTERVAL := 0.12  # 三连发间隔（§8 设计数值；Q30 入库：boss.phases.attacks.sniper3.burst_interval）
## 一型 P1 纵向下压（§5.1）：每 6s 下压 80px 再回
var PRESS_INTERVAL := 6.0
var PRESS_DEPTH := 80.0
## D05 P2 走位（balance.json boss.movement，公开字段供 BossMovement 读取）
var TYPE1_P2_STRAFE := 200  # 一型 P2 strafe 速度（P1 = STRAFE_SPEEDS[0] 150）
var TYPE1_P2_BOB_AMP := 40.0  # 一型 P2 纵向正弦幅度（±px，围绕锚线）
var TYPE1_P2_BOB_PERIOD := 6.0  # 一型 P2 纵向正弦周期（s）
var TYPE2_P2_DASH_TIME := 0.4  # 二型 P2 冲刺持续（P1 = 0.5）
var TYPE2_P2_REST_TIME := 0.5  # 二型 P2 冲刺休息（P1 = 0.7）
var TYPE3_P1_BOB_MIN := 200.0  # 三型 P1 纵向呼吸下界（锚线下 px）
var TYPE3_P1_BOB_MAX := 280.0  # 三型 P1 纵向呼吸上界（锚线下 px）
var TYPE3_P1_BOB_PERIOD := 9.0  # 三型 P1 纵向呼吸周期（s，与模式循环错开）
var TYPE3_P2_STRAFE := 100  # 三型 P2 strafe 速度（P1 = STRAFE_SPEEDS[2] 60）
var TYPE3_P2_BOB_AMP := 50.0  # 三型 P2 纵向正弦幅度（±px，围绕锚线）
var TYPE3_P2_BOB_PERIOD := 8.0  # 三型 P2 纵向正弦周期（s）
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
	# 2026-08-05 Q28：ring_burst 为绝对值分档（json 缺键时回退此表，与 §5.6 一致）
	"ring_burst": [10, 12, 14],
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
## A3：弹幕发射器（BossFire，纯发射逻辑，Boss/_execute_attack 委托）
var _fire := BossFire.new()
## A3：走位策略（BossMovement，三型移动 + P1 纵向下压，Boss._physics_process 委托）
var _movement := BossMovement.new()
## A3：攻击状态机（BossAttacks，持续型攻击时序 + 分发，Boss._physics_process 委托）
var _attacks := BossAttacks.new()
## A3：狂暴状态机（EnrageSequence，狂暴 5 子状态 + 三型差异化，Boss._enrage 委托）
var _enrage_seq := EnrageSequence.new()
## A5：spawner 依赖注入（spawner._spawn_boss 设置；替代 group 现找）
var _spawner: Node = null
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
var E2_AIM := 0.35
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
## 4 型「月蚀」狂暴：双环反向进动 + 蓄力环阵（boss.enrage.type_4）
var E4_RING_COUNT := 10
var E4_RING_INTERVAL := 0.8
var E4_RING_SPEED := 200.0
var E4_PRECESSION_DEG := 15.0
var E4_RELEASE_RING_COUNT := 20
var E4_RELEASE_RING_SPEED := 130.0
## 4 型「月蚀」中心悬停微摆（boss.movement.type4）
var MOVE4_BOB_AMP := 30.0
var MOVE4_BOB_PERIOD := 2.4
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
var _survival: float = 0.0
var _escape_warned: bool = false
var _escaping: bool = false
var _escape_speed: float = 0.0
## 2026-08-06 审计：逃跑警告期上飘累计偏移——直接 `position.y -= drift*delta` 会被
## 绝对 y 赋值走位（type1 P2 / type3 P2 _move_bob、type4）逐帧覆盖，三型无上飘效果；
## 累计偏移由绝对赋值处（BossMovement）叠加，增量式走位保留直接减
var _escape_drift_offset: float = 0.0
## 母舰召唤减速带：短时减速乘区（仅位移，经 _slow_factor 生效）
var _summon_slow_timer: float = 0.0
var _summon_slow_factor: float = 1.0
## 2026-08-07 审计：slow_field 布尔缓存（对齐 enemy.gd C22——物理帧免每帧 buff_count 字典查询）
var _slow_field_on: bool = false
## 2026-08-07 审计：体碰改信号事件驱动（对齐 enemy.gd P0-2——area_entered/exited 标记
## 重叠状态，替代每物理帧 overlaps_area 空间查询；无敌结束仍重叠会再次命中语义保持）
var _body_contact := false
# 阶段框架与模式表循环（§4.1）
var _fight_phase: int = FightPhase.P1
var _patterns: Dictionary = {}  # {"p1": [...], "p2": [...]}，_ready 从配置载入
var _pattern_index: int = 0
var _pattern_left: float = 0.0  # 当前模式剩余波次（或剩余时长秒）
var _pattern_is_duration: bool = false
var _fire_timer: float = 1.6
## G024：三型普通阶段召唤小怪间隔（balance.json boss.phases.type3.summon_interval 可覆盖）
var _summon_interval := 6.0
var _summon_timer: float = 6.0
var _boss_size := Vector2(328.0, 328.0)  # 贴图有效尺寸（_ready 实测更新，算轨道半径）

@onready var _sprite: Sprite2D = $Sprite2D
## P1-2：受击闪白手动衰减（_physics_process 逐帧 lerp 回 _base_modulate，替代每命中新建 Tween）
var _flash_timer: float = 0.0
var _flash_total: float = 0.1


func setup(p_difficulty: float, p_type: int) -> void:
	# K12：p_type 越界钳制（公开接口）——保护下方 hp_mults[p_type-1] 与 TEXTURES[p_type-1]
	# 双双越界（H11 只校验了数组长度）；轮换扩 4 型（2026-08-04 月蚀）后上限放开为 4
	p_type = clampi(p_type, 1, 4)
	boss_type = p_type
	# HP 四级乘算：基准 × 型别倍率 × Boss 击杀 ramp × 难度档（与敌机同源 0.75/1.0/1.5）
	# H11（健壮性审核）：hp_mults 长度/元素校验——短数组越界得 null→float 0.0 → Boss 免疫伤害静默
	# Q02（2026-08-05）：校验与回退数组随 4 型扩容——原 3 元素校验/回退在 json 缺键/截断时
	# 令 hp_mults[3] 越界 → max_hp=0 → type4 出生即免疫伤害（仅 50s 逃跑兜底）
	var hp_mults_raw: Variant = GameState.cfg("boss.hp_mults", [1.3, 0.7, 1.6, 1.2])
	var hp_mults_valid: bool = hp_mults_raw is Array and hp_mults_raw.size() >= 4
	if hp_mults_valid:
		for v: Variant in hp_mults_raw:
			# R06：正值域校验（L 系列判型族登记遗留）——0/负倍率经 float() 后
			# max_hp≤0 → take_damage 首行早退 → Boss 出生即免疫伤害（与 Q02 同根因）
			if v is bool or not (v is int or v is float) or float(v) <= 0.0:
				hp_mults_valid = false
				break
	var hp_mults: Array = hp_mults_raw if hp_mults_valid else [1.3, 0.7, 1.6, 1.2]
	max_hp = (float(GameState.cfg("boss.hp_base", HP_BASE)) * float(hp_mults[p_type - 1]) * p_difficulty * GameState.enemy_hp_multiplier())
	hp = max_hp
	# setup() 在 _ready() 之前调用，不能用 @onready 变量
	($Sprite2D as Sprite2D).texture = TEXTURES[p_type - 1]


## 对外公开接口（A1 修复）：HUD/教程读取状态或中止序列，禁止跨类直接写 _ 私有字段
func is_in_fight() -> bool:
	return _in_fight


func is_escaping() -> bool:
	return _escaping


func abort_enrage_sequence() -> void:
	_abort_enrage_sequence()


## 狂暴态查询（A3：BossMovement/BossAttacks/EnrageSequence 经公开接口交互）
func is_enraged() -> bool:
	return _enraged


## A6：语义化类型查询（调用方不再依赖 `is Boss` 具体类型）
func is_boss() -> bool:
	return true


func fight_phase() -> int:
	return _fight_phase


## A3：模式循环计时复位（BossAttacks 冲刺掠过 RETURN 结束调用）
func reset_fire_timer() -> void:
	_fire_timer = float(_current_pattern().get("interval", _base_fire_interval()))


## A7：测试/诊断白盒断言经公开接口（命名语义化）
func enrage_sequence() -> EnrageSequence:
	return _enrage_seq


func attacks() -> BossAttacks:
	return _attacks


func fire_tool() -> BossFire:
	return _fire


func set_fire_timer(seconds: float) -> void:
	_fire_timer = seconds


func fire_timer() -> float:
	return _fire_timer


func set_fight_phase(p_phase: int) -> void:
	_fight_phase = p_phase


func set_summon_timer(seconds: float) -> void:
	_summon_timer = seconds


func set_patterns(pattern_dict: Dictionary) -> void:
	_patterns = pattern_dict


func patterns() -> Dictionary:
	return _patterns


func set_pattern_index(index: int) -> void:
	_pattern_index = index


func pattern_index() -> int:
	return _pattern_index


func start_pattern() -> void:
	_start_pattern()


func base_modulate_color() -> Color:
	return _base_modulate()


func set_survival(seconds: float) -> void:
	_survival = seconds


func set_in_fight(fighting: bool) -> void:
	_in_fight = fighting


func escape_warned() -> bool:
	return _escape_warned


func begin_escape() -> void:
	_begin_escape()


## A3：编队小怪召唤（BossAttacks/EnrageSequence 经公开接口调用；A5 改注入 spawner）
func set_spawner(spawner: Node) -> void:
	_spawner = spawner


func spawn_minion_at(pos: Vector2) -> Variant:  # M3b：Enemy 迁 C#，返回类型改 Variant（调用方 untyped 接收）
	if _spawner == null:
		return null
	return _spawner.spawn_minion(pos)


func _ready() -> void:
	GameState.bind_enemy(self)  # 统一绑定（docs/ENTITY_MANAGER.md）
	# 机体尺寸族：设计值 × 全局缩放（tscn 存 1.0 基准，幂等覆盖）
	_ws = GameState.world_scale
	_sprite.scale = Vector2.ONE * 1.15 * _ws
	(($CollisionShape2D as CollisionShape2D).shape as CircleShape2D).radius = 120.0 * _ws
	MUZZLE_OFFSET = 100.0 * _ws
	_fire.muzzle_offset = MUZZLE_OFFSET
	_fire.world_scale = _ws
	_attacks.configure(_fire, _ws)
	_enrage_seq.configure(_fire, _attacks, _ws)
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
	# H12（健壮性审核）：square_path_ratio 钳制 (0,1]——0 会除零产生 inf 轨道 NaN
	ENRAGE_SQUARE_PATH_RATIO = clampf(float(GameState.cfg("boss.enrage.square_path_ratio", ENRAGE_SQUARE_PATH_RATIO)), 0.05, 1.0)
	ENRAGE_RELEASE_LASER_SPEED = GameState.cfg("boss.enrage.release_laser_speed", ENRAGE_RELEASE_LASER_SPEED)
	ENRAGE_RELEASE_RING_SPEED = GameState.cfg("boss.enrage.release_ring_speed", ENRAGE_RELEASE_RING_SPEED)
	_boss_size = _sprite.texture.get_size() * _sprite.scale
	ESCAPE_TIME = GameState.cfg("boss.escape.time", ESCAPE_TIME)
	ESCAPE_WARNING = GameState.cfg("boss.escape.warning", ESCAPE_WARNING)
	ESCAPE_DRIFT = GameState.cfg("boss.escape.drift", ESCAPE_DRIFT)
	ESCAPE_START_SPEED = GameState.cfg("boss.escape.start_speed", ESCAPE_START_SPEED)
	ESCAPE_ACCEL = GameState.cfg("boss.escape.accel", ESCAPE_ACCEL)
	# 2026-08-07 审计：slow_field 缓存初始值 + buffs_changed 增量刷新（对齐 enemy.gd C22）
	_slow_field_on = GameState.buff_count(&"slow_field") > 0
	GameState.buffs_changed.connect(_on_buffs_changed)
	# 2026-08-07 审计：体碰信号事件驱动（对齐 enemy.gd P0-2；collision_mask=3 已含 player Hitbox 层 1）
	area_entered.connect(_on_area_entered)
	area_exited.connect(_on_area_exited)
	ESCAPE_COUNTDOWN_FROM = GameState.cfg("boss.escape.countdown_visible_from", ESCAPE_COUNTDOWN_FROM)
	HP_BASE = GameState.cfg("boss.hp_base", HP_BASE)
	# C18：cfg 返回 Variant，显式转 Array[float] 再赋 typed 变量
	var ss: Variant = GameState.cfg("boss.strafe_speeds", STRAFE_SPEEDS)
	var ss_arr: Array[float] = []
	if ss is Array:
		for v in ss:
			ss_arr.append(float(v))
	STRAFE_SPEEDS = ss_arr if ss_arr.size() >= 3 else [150.0, 400.0, 60.0]  # H11：不足 3 元素回退默认
	# B5 修复：cfg 对数组返回共享 JSON 引用，_apply_difficulty_scaling 会就地乘算
	# FIRE_INTERVALS[i]——不拷贝会污染全局缓存、easy/hard 下跨 Boss 复合叠加
	# （同 _load_patterns 的 duplicate(true)，见 BOSS_REDESIGN §8.2）。
	# H11：非数组类型时回退默认（原 .duplicate() 对非数组直接崩溃）
	var fi_raw: Variant = GameState.cfg("boss.fire_intervals", FIRE_INTERVALS)
	FIRE_INTERVALS = fi_raw.duplicate(true) if fi_raw is Array else FIRE_INTERVALS.duplicate(true)
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
	CLEAR_ON_SHIFT = bool(GameState.cfg("boss.phases.clear_on_shift", CLEAR_ON_SHIFT))
	TRANSITION_INVINCIBLE = float(GameState.cfg("boss.phases.transition_invincible", TRANSITION_INVINCIBLE))
	SNIPER_AIM_TIME = GameState.cfg("boss.phases.telegraph.sniper_aim", SNIPER_AIM_TIME)
	SNIPER_TRACK_TIME = GameState.cfg("boss.phases.telegraph.sniper_track", SNIPER_TRACK_TIME)
	SNIPER_BURST_INTERVAL = GameState.cfg("boss.phases.attacks.sniper3.burst_interval", SNIPER_BURST_INTERVAL)
	PRESS_INTERVAL = GameState.cfg("boss.phases.press_interval", PRESS_INTERVAL)
	PRESS_DEPTH = GameState.cfg("boss.phases.press_depth", PRESS_DEPTH)
	TYPE1_P2_STRAFE = int(GameState.cfg("boss.movement.type1_p2_strafe", TYPE1_P2_STRAFE))
	TYPE1_P2_BOB_AMP = float(GameState.cfg("boss.movement.type1_p2_bob_amp", TYPE1_P2_BOB_AMP))
	TYPE1_P2_BOB_PERIOD = float(GameState.cfg("boss.movement.type1_p2_bob_period", TYPE1_P2_BOB_PERIOD))
	TYPE2_P2_DASH_TIME = float(GameState.cfg("boss.movement.type2_p2_dash_time", TYPE2_P2_DASH_TIME))
	TYPE2_P2_REST_TIME = float(GameState.cfg("boss.movement.type2_p2_rest_time", TYPE2_P2_REST_TIME))
	TYPE3_P1_BOB_MIN = float(GameState.cfg("boss.movement.type3_p1_bob_min", TYPE3_P1_BOB_MIN))
	TYPE3_P1_BOB_MAX = float(GameState.cfg("boss.movement.type3_p1_bob_max", TYPE3_P1_BOB_MAX))
	TYPE3_P1_BOB_PERIOD = float(GameState.cfg("boss.movement.type3_p1_bob_period", TYPE3_P1_BOB_PERIOD))
	TYPE3_P2_STRAFE = int(GameState.cfg("boss.movement.type3_p2_strafe", TYPE3_P2_STRAFE))
	TYPE3_P2_BOB_AMP = float(GameState.cfg("boss.movement.type3_p2_bob_amp", TYPE3_P2_BOB_AMP))
	TYPE3_P2_BOB_PERIOD = float(GameState.cfg("boss.movement.type3_p2_bob_period", TYPE3_P2_BOB_PERIOD))
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
	# R06：interval 类键钳下限（L 系列判型族登记遗留）——0/负值使狂暴攻击每帧触发风暴
	E1_RING_INTERVAL = maxf(float(GameState.cfg("boss.enrage.type_1.ring_interval", E1_RING_INTERVAL)), 0.05)
	E1_RING_COUNT = GameState.cfg("boss.enrage.type_1.ring_count", E1_RING_COUNT)
	E1_RING_SPEED = GameState.cfg("boss.enrage.type_1.ring_speed", E1_RING_SPEED)
	E1_RING_PRECESSION_DEG = GameState.cfg("boss.enrage.type_1.ring_precession_deg", E1_RING_PRECESSION_DEG)
	E1_SALVO_CHARGE = maxf(float(GameState.cfg("boss.enrage.type_1.salvo_charge", E1_SALVO_CHARGE)), 0.05)
	E1_SALVO_COUNT = GameState.cfg("boss.enrage.type_1.salvo_count", E1_SALVO_COUNT)
	E1_SALVO_SPEED = GameState.cfg("boss.enrage.type_1.salvo_speed", E1_SALVO_SPEED)
	E1_SALVO_DAMAGE = GameState.cfg("boss.enrage.type_1.salvo_damage", E1_SALVO_DAMAGE)
	E2_POINT_COUNT = GameState.cfg("boss.enrage.type_2.point_count", E2_POINT_COUNT)
	E2_POINT_INTERVAL = maxf(float(GameState.cfg("boss.enrage.type_2.point_interval", E2_POINT_INTERVAL)), 0.05)
	E2_AIM = GameState.cfg("boss.enrage.type_2.aim", E2_AIM)
	E2_SNIPER_SPEED = GameState.cfg("boss.enrage.type_2.sniper_speed", E2_SNIPER_SPEED)
	E2_SNIPER_DAMAGE = GameState.cfg("boss.enrage.type_2.sniper_damage", E2_SNIPER_DAMAGE)
	E2_RELEASE_RING_COUNT = GameState.cfg("boss.enrage.type_2.release_ring_count", E2_RELEASE_RING_COUNT)
	E2_RELEASE_RING_SPEED = GameState.cfg("boss.enrage.type_2.release_ring_speed", E2_RELEASE_RING_SPEED)
	E3_SUMMON_INTERVAL = maxf(float(GameState.cfg("boss.enrage.type_3.summon_interval", E3_SUMMON_INTERVAL)), 0.05)
	# G024：三型普通阶段召唤间隔入配置（对齐狂暴 E3 键）
	_summon_interval = float(GameState.cfg("boss.phases.type3.summon_interval", _summon_interval))
	_summon_timer = _summon_interval
	E3_SUMMON_WAVES = GameState.cfg("boss.enrage.type_3.summon_waves", E3_SUMMON_WAVES)
	E3_SUMMON_COUNT = GameState.cfg("boss.enrage.type_3.summon_count", E3_SUMMON_COUNT)
	E3_RING_INTERVAL = maxf(float(GameState.cfg("boss.enrage.type_3.ring_interval", E3_RING_INTERVAL)), 0.05)
	E3_RING_COUNT = GameState.cfg("boss.enrage.type_3.ring_count", E3_RING_COUNT)
	E3_RING_SPEED = GameState.cfg("boss.enrage.type_3.ring_speed", E3_RING_SPEED)
	E3_RELEASE_RING_COUNT = GameState.cfg("boss.enrage.type_3.release_ring_count", E3_RELEASE_RING_COUNT)
	E3_RELEASE_RING_SPEED = GameState.cfg("boss.enrage.type_3.release_ring_speed", E3_RELEASE_RING_SPEED)
	# 4 型「月蚀」（2026-08-04）
	RING_BURST_SPEED = float(GameState.cfg("boss.ring_burst.bullet_speed", RING_BURST_SPEED))
	BULLET_DAMAGE_RING = int(GameState.cfg("boss.bullet_damage.ring", BULLET_DAMAGE_RING))
	MOVE4_BOB_AMP = float(GameState.cfg("boss.movement.type4.bob_amp", MOVE4_BOB_AMP))
	MOVE4_BOB_PERIOD = float(GameState.cfg("boss.movement.type4.bob_period", MOVE4_BOB_PERIOD))
	E4_RING_COUNT = int(GameState.cfg("boss.enrage.type_4.ring_count", E4_RING_COUNT))
	E4_RING_INTERVAL = maxf(float(GameState.cfg("boss.enrage.type_4.ring_interval", E4_RING_INTERVAL)), 0.05)
	E4_RING_SPEED = float(GameState.cfg("boss.enrage.type_4.ring_speed", E4_RING_SPEED))
	E4_PRECESSION_DEG = float(GameState.cfg("boss.enrage.type_4.precession_deg", E4_PRECESSION_DEG))
	E4_RELEASE_RING_COUNT = int(GameState.cfg("boss.enrage.type_4.release_ring_count", E4_RELEASE_RING_COUNT))
	E4_RELEASE_RING_SPEED = float(GameState.cfg("boss.enrage.type_4.release_ring_speed", E4_RELEASE_RING_SPEED))
	_movement.sync_press_timer(PRESS_INTERVAL)
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
	# Q03（2026-08-05）：clampi 随 4 型扩容放开——原钳为 3 时 DEFAULT_PATTERNS 键 4 死数据、
	# type4 配置损坏时静默回退三型（母舰）模式表，违背「脚本回退镜像 json」约定
	var defaults: Dictionary = DEFAULT_PATTERNS[clampi(boss_type, 1, 4)]
	_patterns = defaults.duplicate(true)
	var cfg_patterns: Variant = GameState.cfg("boss.phases.type%d" % boss_type, defaults)
	if cfg_patterns is Dictionary:
		for key in ["p1", "p2"]:
			var list: Variant = (cfg_patterns as Dictionary).get(key, [])
			# L07（2026-08-03 审查）：元素级判型（G06 只判容器层）——混入非 Dictionary 元素
			# 时 _current_pattern() typed 返回运行时类型错误、pattern.has 崩溃；坏元素跳过，
			# 全坏时保留脚本默认表（「损坏回退默认」口径）；深拷贝同样逐元素隔离共享 JSON
			if list is Array:
				var cleaned: Array = []
				for pattern in list:
					if pattern is Dictionary:
						cleaned.append((pattern as Dictionary).duplicate(true))
				if not cleaned.is_empty():
					_patterns[key] = cleaned


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
	# 2026-08-03 审计：三型普通阶段召唤间隔随难度分档（对齐 §8.3「各内部节奏 ×interval_mult」）；
	# 同步首唤计时，否则第一个召唤用 _ready 时的未分档间隔
	_summon_interval *= interval_mult
	_summon_timer = _summon_interval
	E3_RING_INTERVAL *= interval_mult
	# 2026-08-06 审计 M4：4 型「月蚀」狂暴分档补齐（E33 同族遗漏）——interval/speed/count
	# 三表原无 type4 行，狂暴参数三档恒定（easy 偏难、hard 偏易）；与 1/2/3 型同款乘区
	E4_RING_INTERVAL *= interval_mult
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
	# M4：4 型普通阶段 ring_burst 环弹速 + 狂暴双环/蓄力环阵弹速随难度档（对齐 §4.4 全弹速分档）
	RING_BURST_SPEED *= speed_mult
	E4_RING_SPEED *= speed_mult
	E4_RELEASE_RING_SPEED *= speed_mult
	# 弹数：逐参数分档增减，按攻击语义钳制下限（A3：增量迁入 BossAttacks）；
	# ring_burst 例外：counts.ring_burst 为每档弹数绝对值（Q01），直接写入 ring_delta
	_attacks.fan_delta = _count_delta("fan", tier)
	_attacks.homing_delta = _count_delta("homing", tier)
	_attacks.ring_delta = _count_delta("ring_burst", tier)
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
	# M4：4 型狂暴弹数分档（ring 增量 [-2,0,2]，同 1/3 型环弹口径；下限 4 防越界）
	E4_RING_COUNT = maxi(4, E4_RING_COUNT + _count_delta("ring", tier))
	E4_RELEASE_RING_COUNT = maxi(4, E4_RELEASE_RING_COUNT + _count_delta("ring", tier))


## 弹数分档取值：boss.difficulty_scaling.counts[key][tier]，缺键/越界回退 0
func _count_delta(key: String, tier: int) -> int:
	var d: Variant = DIFF_COUNT_DELTAS.get(key, [0, 0, 0])
	if d is Array and not (d as Array).is_empty():
		return int((d as Array)[clampi(tier, 0, (d as Array).size() - 1)])
	return 0


func _exit_tree() -> void:
	GameState.unbind_enemy(self)  # 统一解绑（docs/ENTITY_MANAGER.md）
	_enrage_seq.unlock_player()  # 兜底：离场必复位玩家减速，不留残留（A3 归 EnrageSequence）


func _base_fire_interval() -> float:
	# B 梯队（fair plan §8）：DDA 降档拉长 Boss 攻击间隔（不降弹数/收益，分数公平）
	return float(FIRE_INTERVALS[clampi(boss_type - 1, 0, FIRE_INTERVALS.size() - 1)]) * GameState.dda_factor()


## 慢速力场因子（全局机体移速 ×0.8；与狂暴移速倍率相乘）
## 母舰召唤减速带命中时叠加短时乘区（同语义，仅位移）
func slow_factor() -> float:
	var f := SLOW_FIELD_FACTOR if _slow_field_on else 1.0
	if _summon_slow_timer > 0.0:
		f *= _summon_slow_factor
	return f


## slow_field 缓存刷新（2026-08-07 审计：对齐 enemy.gd C22，buffs_changed 增量刷新）
func _on_buffs_changed() -> void:
	_slow_field_on = GameState.buff_count(&"slow_field") > 0


## 母舰召唤减速带命中：duration 秒内位移速度 ×factor
func apply_slow(duration: float, factor: float) -> void:
	_summon_slow_timer = duration
	_summon_slow_factor = factor


func _base_modulate() -> Color:
	return ENRAGE_BLINK_COLOR if _enraged else Color.WHITE


## 逃跑剩余秒数（HUD 逃跑倒计时读取口，§4.5）
func escape_remaining() -> float:
	return ESCAPE_TIME - _survival


## 逃跑警告期上飘偏移（BossMovement 绝对 y 赋值走位叠加用；未进警告期返回 0）
func escape_drift_offset() -> float:
	return _escape_drift_offset if _survival >= ESCAPE_TIME - ESCAPE_WARNING else 0.0


func _physics_process(delta: float) -> void:
	_update_flash(delta)
	if _summon_slow_timer > 0.0:
		_summon_slow_timer -= delta
	if _escaping:
		# 逃跑离场：向上加速飘出屏幕（不再受弹、不再开火）
		_escape_speed += ESCAPE_ACCEL * delta
		position.y -= _escape_speed * delta
		if position.y < GameState.view_world_rect().position.y - 280.0:  # G08：出界基线对齐 view_world_rect
			escaped.emit()
			died.emit()  # 离场通知（血条/生成器重排）；非击毁，无击杀奖励
			queue_free()
		return
	if not _in_fight:
		position.y += ENTER_SPEED * slow_factor() * delta
		if position.y >= fight_anchor_y():  # 逐帧求值，支持战斗中途切视角档
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
	if _enrage_seq.is_active():
		if _survival >= ESCAPE_TIME - ESCAPE_WARNING:
			_sprite.modulate = (ESCAPE_BLINK_COLOR if int(_survival * 8.0) % 2 == 0 else _base_modulate())
		_enrage_seq.update(delta, self)
		_check_body_collision()
		return

	if _survival >= ESCAPE_TIME - ESCAPE_WARNING:
		# 上飘双路径：增量式走位（type1 P1/type3 P1）直接减；绝对 y 赋值走位
		# （type1 P2/type3 P2/type4）经 _escape_drift_offset 累计后在 BossMovement 叠加
		position.y -= ESCAPE_DRIFT * delta
		_escape_drift_offset += ESCAPE_DRIFT * delta
		_sprite.modulate = (ESCAPE_BLINK_COLOR if int(_survival * 8.0) % 2 == 0 else _base_modulate())

	# 冲刺掠过（二型 P2）接管移动与模式编排；否则走位 + 模式表循环
	if _attacks.is_sweep_active():
		_attacks.update(delta, self)
	else:
		# 走位与攻击解耦（§4.1）：A3 委托 BossMovement
		_movement.update(delta, self)

		# 模式表循环：波间隔由当前模式给出，波次/时长播完切下一个
		# （狂暴「余怒」射速 ×1.3：计时器流速加快，§5.4）
		_fire_timer -= delta * (ENRAGE_RATE_MULT if _enraged else 1.0)
		if _fire_timer <= 0.0:
			var pattern := _current_pattern()
			_fire_timer = float(pattern.get("interval", _base_fire_interval()))
			_attacks.execute(StringName(pattern.get("attack", &"")), self)
			if not _pattern_is_duration:
				_pattern_left -= 1.0
		if _pattern_is_duration:
			_pattern_left -= delta
		if _pattern_left <= 0.0:
			_advance_pattern()

		# 持续型攻击轮询（狙击 telegraph / 3 连发 / 蓄力重炮 / 编队齐射 / 冲刺掠过）
		_attacks.update(delta, self)

	# 母舰型召唤小怪（独立计时，不占模式表；机型参数表驱动）
	if bool(SUMMONER_TYPES.get(boss_type, false)):
		_summon_timer -= delta
		if _summon_timer <= 0.0:
			_summon_timer = _summon_interval  # G024：间隔入配置
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


## P1→P2 段切换：0.6s 蓄力辉光 + 抖屏 + 变调音效 + 清自身开火计时（§4.1），模式表重置循环
func _enter_phase(p_phase: int) -> void:
	_fight_phase = p_phase
	_pattern_index = 0
	_start_pattern()
	_fire_timer = PHASE_SHIFT_DURATION  # 段切换蓄力期停火
	# C11 修复：段切换归零一型纵向下压偏移，避免 P2 以残留下压永久停在锚线下方
	_movement.reset_press()
	# L14：段切换 y 平滑过渡——P1 增量式下压（一型 press / 三型 band）当前偏移未补偿，
	# P2 绝对赋值锚线会 1/4 屏瞬移；从当前 y 平滑追锚线（过渡期由 _move_bob 收敛）
	_movement.begin_bob_smooth(position.y)
	_attacks.cancel_all()
	_transition_cleanup()  # 机制三：转场清弹 + 玩家短暂无敌（公平感喘息）
	_attacks.charge_glow(self, PHASE_SHIFT_DURATION)
	GameState.shake(GameState.cfg("effects.shake.enrage", 16.0) * 0.5)
	GameState.play_sfx(GameState.SFX_EXPLOSION_BIG, -10.0, 0.7)
	phase_changed.emit(p_phase)


## 阶段转场公平感清理（2026-08-03 机制三）：清全部活跃弹丸（含编队炸弹，复用
## main._on_orbital_struck 同款遍历）+ 给玩家短暂无敌——喘息 + 「阶段边界」明确信号，
## 避免「惊喜阶段」后被残余弹幕压制。逃跑期不走本路径（_begin_escape 不经阶段切换）。
## 低频（一局数次）直接遍历可接受，无逐帧轮询。无敌只增不减（不覆盖受击 1.5s 等更长无敌）。
func _transition_cleanup() -> void:
	if not CLEAR_ON_SHIFT:
		return
	for child in get_parent().get_children():
		if is_instance_of(child, load("res://csharp/godot/Bullet.cs")) or child is FormationBomb:  # M3a 起 Bullet 为 C# 类，不能经类名 is 判定
			child.queue_free()
	var player := GameState.player_ref as Player
	if player != null and player.invincible_remaining() < TRANSITION_INVINCIBLE:
		player.set_invincible(TRANSITION_INVINCIBLE)


# ---------------- 走位（与攻击解耦；阶段 A 仅一型 P1 纵向下压，其余保持现状） ----------------


## 战斗锚线 y：FIGHT_Y 为距可见区域顶缘的偏移，调用时实时取 view 基线
## （与 strafe_range() 边距处理对齐；zoom=1 时 view.position.y=0，锚线 = FIGHT_Y 本身）。
## A3：走位实现在 BossMovement，此处保留只读查询供子类/内部使用。
func fight_anchor_y() -> float:
	return GameState.view_world_rect().position.y + FIGHT_Y


## 巡航范围随可见世界区域收窄（zoom=1 时与配置值 STRAFE_MIN_X/MAX_X 一致）。
## 右缘边距 = 设计宽 1920 − STRAFE_MAX_X = 300px，随 view.end.x 平移保持（view_zoom_test
## 断言 large 档 hi = view.end.x − 300；2026-08-05 P4 复核后保留原语义，1920 为设计宽度常量）
func strafe_range() -> Vector2:
	var view := GameState.view_world_rect()
	var lo := view.position.x + STRAFE_MIN_X
	var hi := maxf(view.end.x - (1920.0 - STRAFE_MAX_X), lo)
	return Vector2(lo, hi)


func _summon_minions() -> void:
	if _spawner == null:
		return
	for i in randi_range(2, 3):
		_spawner.spawn_minion(position + Vector2(randf_range(-80.0, 80.0), 110.0) * _ws)


## 狂暴快照弹幕：狂暴进入时的一次性齐射（由 main 在子弹时间结束后统一触发）。
## 4 道激光向弹（高速长弹，复用敌弹 laser 型表现）+ 8 方向环形慢弹。委托 BossFire。
func fire_enrage_snapshot() -> void:
	if _escaping:
		return
	_fire.fire_enrage_wave(
		self,
		ENRAGE_LASER_SPEED,
		ENRAGE_RING_SPEED,
		BULLET_DAMAGE_SNAPSHOT_LASER,
		BULLET_DAMAGE_SNAPSHOT_RING,
		ENRAGE_SNAPSHOT_LASERS,
		ENRAGE_SNAPSHOT_RING
	)


## 狂暴序列驱动：TRANSITION（蓄力抖动滑入轨道，1 型悬停原地）→ ACTIVE（各型差异化攻击）
## → RELEASE_HOLD（各型收尾爆发，§5.4 峰值）→ RETURN（飞回战斗位）→ NONE（常规「余怒」循环）
## 序列中断（逃跑/死亡/离场/教程收尾）：清状态 + 解血锁 + 复位减速 + 清 telegraph，幂等
func _abort_enrage_sequence() -> void:
	_enrage_seq.abort()


## 狂暴期玩家减速（替代原作 is_controls_locked 定身，§4.3）：移速 ×0.35，
## 仍可瞄准/射击/冲刺；TRANSITION+ACTIVE 有效
## 任何伤害不掉血不死；RELEASE_HOLD 解锁后正常扣血可击杀。
## 阶段框架（§4.1）：同阈值驱动 P1→P2（70%）；一击跨两段时狂暴优先（锁血语义不变）。
func take_damage(amount: int, score_scale: float = 1.0) -> void:
	if _escaping:
		return  # G02：逃跑期不再受任何伤害——激光 _damage_tick/溅射 _splash 按注册表+距离判定
		# 绕开 collision_layer=0，此处统一拦截，防逃跑窗口内补刀致死触发击杀奖励
		#（2026-08-03 审计：同模式防护在 EnrageSequence._release_fallback，旧 Boss._fire_enrage_release 已删）
	if hp <= 0.0:
		return  # 已死亡待释放（同帧多发命中防重复结算）
	if _enrage_seq.is_health_locked():
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


## 受击闪白（锁血期复用；P1-2 手动衰减替代 Tween，高频命中零分配）
func _flash_hit() -> void:
	_sprite.modulate = Color(2.0, 2.0, 2.0)
	# 游击型受击硬直（闪白）更短（机型参数表驱动）
	_flash_total = float(HIT_FLASH_BY_TYPE.get(boss_type, 0.1))
	_flash_timer = _flash_total


## P1-2：受击闪白逐帧衰减（lerp 回基地色调，狂暴态 _base_modulate 实时取色）
func _update_flash(delta: float) -> void:
	if _flash_timer <= 0.0:
		return
	_flash_timer -= delta
	if _flash_timer <= 0.0:
		_sprite.modulate = _base_modulate()
	else:
		_sprite.modulate = _sprite.modulate.lerp(_base_modulate(), delta / _flash_total)


## 身体撞击（对齐原作 boss_vs_player.py 逐帧轮询语义）：入场降入与逃跑离场阶段不判定；
## 玩家 -30 HP（受击无敌帧节流连撞，无敌结束仍重叠会再次命中），Boss 不掉血、不自毁。
## 2026-08-07 审计：重叠状态由 area_entered/exited 事件驱动标记（collision_mask=3 已含
## player Hitbox 层 1），此处仅 O(1) 标记守卫（替代原每物理帧 overlaps_area 空间查询）
func _check_body_collision() -> void:
	if _body_contact:
		# 撞体伤害随对局进程 ramp（与 Boss 弹同一系数）；补传撞体位置作伤害源方向（D8）
		(GameState.player_ref as Player).take_damage(
			maxi(1, int(roundf(COLLISION_DAMAGE * GameState.enemy_damage_ramp()))), global_position
		)


## 2026-08-07 审计：体碰重叠标记（对齐 enemy.gd P0-2；判定交回 _physics_process 守卫——
## 入场降入期不经过守卫，保持"入场期不判定"语义）
func _on_area_entered(area: Area2D) -> void:
	if not area.is_in_group("player_hitbox"):
		return
	_body_contact = true


## 2026-08-07 审计：离开玩家 Hitbox → 清除重叠标记（停止每帧重掷）
func _on_area_exited(area: Area2D) -> void:
	if area.is_in_group("player_hitbox"):
		_body_contact = false


func _enrage() -> void:
	_enraged = true
	_fight_phase = FightPhase.ENRAGE
	# 中断进行中的常规攻击/telegraph，启动狂暴序列：锁血 30% 检查点 + 快照玩家位置 + 玩家减速
	# （狂暴数据初始化 + 锁血 + 玩家减速委托 EnrageSequence，A3）
	_attacks.cancel_all()
	_transition_cleanup()  # 机制三：ENRAGE 转场同款清弹 + 玩家短暂无敌
	_enrage_seq.begin(
		self, GameState.player_ref.global_position if GameState.player_ref != null else GameState.view_world_rect().get_center(), _boss_size
	)
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
	load("res://csharp/godot/Explosion.cs").SpawnBossSequence(get_parent(), global_position)  # M3a 起 Explosion 为 C#，静态方法经脚本资源调用
	died.emit()
	queue_free()


## 逃跑警告：复用 HUD 警告横幅（不可用时退化为 print），最后 3s 机身闪烁见 _physics_process
func _show_escape_warning() -> void:
	var hud := get_tree().get_first_node_in_group("hud")
	if hud != null and hud.has_method("show_warning"):
		hud.show_warning(tr("BOSS_ESCAPE_WARNING"))
	else:
		print("[BOSS] 逃跑警告：Boss 即将逃离战场")


## 50s 未被击杀：逃跑（无 add_boss_kill / 加分 / 难度提升 / 轮换推进）
func _begin_escape() -> void:
	_abort_enrage_sequence()  # 序列中断：解血锁 + 复位减速 + 清 telegraph
	_attacks.cancel_all()  # R07：常规攻击中断（瞄准线/蓄力/齐射计时/拖弹点），防逃跑期残留攻击继续结算
	_escaping = true
	is_escaped = true
	_escape_speed = ESCAPE_START_SPEED
	collision_layer = 0  # 离场阶段不再受弹
	collision_mask = 0
	_body_contact = false  # 2026-08-07 审计：逃跑期监控关闭，重叠标记复位防残留
	_sprite.modulate = _base_modulate()
	print("[BOSS] 存活 %ds 未被击杀，逃离战场（无击杀奖励）" % int(ESCAPE_TIME))
