extends Node
## 全局状态与信号总线：分数、击杀、生命、难度乘数、已选 buff。

signal score_changed(new_score: int)
signal health_changed(new_health: float)
signal difficulty_changed(new_multiplier: float)
signal difficulty_selected(difficulty: StringName)
signal milestone_reached(score: int)
signal player_died
## 玩家实际结算受击（无敌/闪避/单帧守卫未结算不发）：Meta HUD 受击层数据源
@warning_ignore("unused_signal")
signal player_damaged(amount: float, from_pos: Vector2)
signal screen_shake(strength: float)
## RP 经济/任务/路线信号：暂无消费方（base_console 拉取驱动），保留 API 供未来事件驱动
signal rp_changed(new_rp: int)
signal mission_completed(id: StringName)
signal route_chosen(line: StringName, buff_id: StringName)
signal key_bindings_changed
signal locale_changed
signal view_zoom_changed(factor: float)
signal window_size_changed(level: StringName)
signal aim_assist_changed(level: StringName)
signal reduce_flash_changed(enabled: bool)
signal mouse_lock_changed(enabled: bool)
## P0-1 手柄设置：右摇杆瞄准灵敏度 + 摇杆死区（profile 持久化；变更时广播供 player 重读）
signal joy_settings_changed(aim_speed: float, deadzone: float)
## buff 层数任何变动（选取/路线合并/存档恢复/重开清空）后发出，驱动外观刷新
signal buffs_changed

## 难度档位表（开始面板选择，profile 持久化；对齐原作 settings.py DIFFICULTY_SETTINGS）
## hp/speed/spawn 为敌机数值与刷怪间隔倍率；score 为分数倍率（add_score 统一乘算）；
## spread_cap 为 spread 弹种敌机同屏上限；milestone 为里程碑阈值倍率
## （原作阈值与分数同倍 ×1/×2/×3，此处按设计取 ×1/×1/×1.5，避免高难 Buff 节奏过稀）；
## regen_delay/regen_rate 为被动回血（对齐原作 settings.py HEALTH_REGEN）：
## 距上次受伤 regen_delay 秒起每秒回 regen_rate HP（原作延迟不重置为疑似 bug，本版受伤即重置）。
var DIFFICULTY_DEFS: Dictionary = {
	&"easy":
	{
		"hp": 0.75,
		"speed": 0.85,
		"spawn": 1.25,
		"score": 1,
		"spread_cap": 1,
		"milestone": 1.0,
		"regen_delay": 3.0,
		"regen_rate": 4.0,
	},
	&"medium":
	{
		"hp": 1.0,
		"speed": 1.0,
		"spawn": 1.0,
		"score": 2,
		"spread_cap": 2,
		"milestone": 1.0,
		"regen_delay": 4.0,
		"regen_rate": 2.0,
	},
	&"hard":
	{
		"hp": 1.5,
		"speed": 1.2,
		"spawn": 0.8,
		"score": 3,
		"spread_cap": 3,
		"milestone": 1.5,
		"regen_delay": 5.0,
		"regen_rate": 0.67,
	},
}
const DIFFICULTY_ORDER: Array[StringName] = [&"easy", &"medium", &"hard"]

# 里程碑阈值曲线（对齐原作 constants.py GameBalanceConstants 算法）：
# 首循环 8 档基础阈值，之后每循环的档差按 ×1.35^cycle 放大（阈值单调不回退）。
const MILESTONE_BASE: Array[int] = [3000, 8000, 15000, 25000, 40000, 55000, 70000, 80000]
const MILESTONE_CYCLE_MULT := 1.35

# ---------------- 全局数值配置中心 ----------------
# A2 阶段 1：balance.json 的加载/查询/纯数值 ramp 已剥离到 BalanceService（组合委托）。
# 缺失/损坏时全部回退脚本默认值；访问统一走 GameState.cfg("分层.路径", 默认值)；
# 热路径在各自 _ready 缓存进成员变量。
const BALANCE_PATH := "res://data/balance.json"

## A2 组合服务（均非 autoload，保持"唯一 autoload：GameState"约定；GameState 委托）
var _balance_service := BalanceService.new()
var _save_manager := SaveManager.new()
var _sfx_player := SfxPlayer.new()
var _registry := EntityRegistry.new()
## 迷雾事件管理器（2026-08-05 任务轮换/迷雾事件系统）：全局单例，挂 GameState 下
## 维持唯一 autoload 约定；对局中概率触发干扰事件（触发纪律/信号解耦见脚本头注释）
var _fog_events := FogEventManager.new()
## 统一游戏事件管理器（docs/EVENT_MANAGER.md）：批量管理全部随机游戏事件（迷雾 +
## 遭遇）；fog 组经迷雾门面接线，encounter 组由 main 注册——见 scripts/event_manager.gd
var _events := GameEventManager.new()
## 2026-08-04 账户系统：本地用户数据库（UserDB，非 autoload，规格 docs/2026-08-04-local-accounts-plan.md）
var _user_db := UserDB.new()
## 生效的里程碑表（默认值见 const，可被 balance.json 覆盖）
var milestone_base: Array[int] = MILESTONE_BASE.duplicate()
var milestone_cycle_mult: float = MILESTONE_CYCLE_MULT
## 全局机体尺寸缩放（balance.json 顶层 world_scale；0.4 = 当前默认观感，2026-07-31 由 1/3 上调）。
## 机体尺寸族数值（贴图 scale/碰撞 radius/机体偏移/随机体特效比例）在 json/tscn/脚本回退中
## 一律存设计值（1.0 基准），实体在 _ready()/setup() 统一乘本系数后应用；游戏性范围族不乘。
## 回退默认值须与 balance.json 一致（损坏/缺键时全局比例不错位）。
var world_scale: float = 0.4


func _load_balance() -> void:
	_balance_service.load(BALANCE_PATH)


## 配置字典是否已加载（缺失/损坏 JSON 时为 false，全部回退脚本默认值；测试/诊断用）
func has_balance() -> bool:
	return not _balance_service.is_empty()


## A7 遗留清理：重新加载并应用 balance.json（测试/诊断注入用；运行时只 _ready 走一次）
func reload_balance() -> void:
	_load_balance()
	_apply_balance()


## 统一配置访问：路径如 "player.fuel.drain"。缺键/类型不符回退 default。委托 BalanceService。
func cfg(path: String, default: Variant) -> Variant:
	return _balance_service.cfg(path, default)


func _apply_balance() -> void:
	# H16（健壮性审核）：world_scale 域校验——0/负值使机体贴图/碰撞归零或镜像翻转，钳制下限
	world_scale = maxf(float(cfg("world_scale", world_scale)), 0.01)
	# C03 修复：milestones.base 须为非空数组，否则下游 milestone_threshold 除零
	# C18：显式转 Array[int]（cfg 返回 Variant，typed 赋值需转换）
	var base: Variant = cfg("milestones.base", MILESTONE_BASE.duplicate())
	var base_arr: Array[int] = []
	if base is Array and not (base as Array).is_empty():
		for v in base:
			# 元素级判型（2026-08-03 审计）：int(v) 对字符串返回 0（阈值全 0 → 里程碑风暴）、
			# 对 Array/Dict 抛运行时错误（启动即崩）；非数字元素跳过，与「损坏回退默认」宣称一致
			if v is int or v is float:
				base_arr.append(maxi(int(v), 1))
	milestone_base = base_arr if not base_arr.is_empty() else MILESTONE_BASE.duplicate()
	# H03（健壮性审核）补全：milestones.cycle_mult 全局域校验——≤0 使阈值曲线平台化，
	# apply_run_save 的 while 里程碑推进永不退出（挂死）。difficulty 子表无 cycle_mult 键
	# （原 _valid_difficulty_defs 内检查恒真为死代码），此处对全局键钳制下限（同 world_scale 款）
	milestone_cycle_mult = maxf(float(cfg("milestones.cycle_mult", MILESTONE_CYCLE_MULT)), 0.01)
	# 难度进程曲线参数：负值会使难度乘数随时间/Boss 击杀下行，钳制 ≥0 保曲线单调不减
	_prog_per_boss_kill = maxf(float(cfg("progression.per_boss_kill", 0.6)), 0.0)
	_prog_per_ten_minutes = maxf(float(cfg("progression.per_ten_minutes", 1.5)), 0.0)
	_prog_time_step_seconds = maxf(float(cfg("progression.time_step_seconds", 30.0)), 0.1)  # H15：=0 除零挂死
	# C03 修复：难度表仅在校验 easy/medium/hard 三子键齐全后覆盖，否则回退脚本默认值
	# （缺子键时 DIFFICULTY_DEFS[difficulty]["score"] 会 KeyError，与"损坏回退默认"宣称冲突）
	var diff: Variant = cfg("difficulty", {})
	if _valid_difficulty_defs(diff):
		DIFFICULTY_DEFS = diff
	# P0-2：回血链数值一次性缓存（热路径禁 cfg 约定）
	_refresh_regen_cache()
	# B 梯队：DDA 降档参数缓存（热路径禁 cfg 约定；=0 时段长无效——钳制下限）
	DDA_DURATION = maxf(float(cfg("dda.duration", DDA_DURATION)), 0.1)
	DDA_FACTOR = maxf(float(cfg("dda.factor", DDA_FACTOR)), 1.0)
	_max_hp_base = maxf(float(cfg("player.max_health", _max_hp_base)), 0.1)  # H15 同款：≤0 使 max_health 归零/负值，玩家秒死
	# 2026-08-03 审计：与 _max_hp_base 钳制对称——负值使 extra_life 叠层反而降血上限（生存轴收紧意图相悖）
	_max_hp_bonus = maxf(float(cfg("buffs.extra_life.max_hp_bonus", _max_hp_bonus)), 0.0)
	# 2026-08-03 审计：吸血比例缓存（击杀帧免 cfg 路径解析，P0-2 同款）
	_lifesteal_fraction = maxf(float(cfg("buffs.lifesteal.max_hp_fraction", 0.1)), 0.0)
	# 基地任务轮换：刷新点数经济（≤0 钳制下限，防免费无限刷新）
	REFRESH_COST = maxi(int(cfg("base_task.refresh_cost", REFRESH_COST)), 1)
	GRANT_PER_VISIT = maxi(int(cfg("base_task.grant_per_visit", GRANT_PER_VISIT)), 0)


## C03/E03 修复：难度表结构校验——顶层 Dictionary、含 easy/medium/hard 三个子字典，
## 且每个子字典含全部数值键（缺子键时下游 8 处 DIFFICULTY_DEFS[difficulty][...] 访问 KeyError，
## 部分损坏 JSON 通过后敌方 0 HP 秒死/得分倍率 0，违背「损坏回退默认」宣称）。
## label 键已由 D04 改走 tr() 不再消费，不纳入校验。
const DIFFICULTY_DEF_KEYS: Array[String] = [
	"hp",
	"speed",
	"spawn",
	"score",
	"spread_cap",
	"milestone",
	"regen_delay",
	"regen_rate",
]


func _valid_difficulty_defs(diff: Variant) -> bool:
	if not diff is Dictionary:
		return false
	var d: Dictionary = diff
	for key in [&"easy", &"medium", &"hard"]:
		if not d.has(key) or not d[key] is Dictionary:
			return false
		var def: Dictionary = d[key]
		for k in DIFFICULTY_DEF_KEYS:
			var v: Variant = def.get(k)  # 缺键时 get 返回 null，一并落入类型校验
			# L04（2026-08-03 审查）：bool 是 int 子类需显式排除（E21 已修 spawner 同型
			# 遗漏）——"score": false 通过校验后得分倍率恒 0，里程碑永不触发（Buff 系统软锁）
			if ((not v is int) and (not v is float)) or v is bool:
				return false
		# H03（健壮性审核）：数值域校验——milestone ≤ 0 会破坏阈值单调性，
		# 导致 continue_run 的 while 里程碑推进永不退出（挂死）或对局内里程碑风暴。
		# 原 cycle_mult 检查为死代码：difficulty 子表无 cycle_mult 键（get 恒返回默认 1.0），
		# 全局 milestones.cycle_mult 的 >0 域校验已移至 _apply_balance
		if float(def.get("milestone", 1.0)) <= 0.0:
			return false
		# 2026-08-03 审计：hp/speed/spawn/score/spread_cap 负值会使敌机 0 HP 秒死/反向移动/负得分倍率，
		# 与 milestone 同款域校验——任一负值整表回退默认（「损坏回退默认」宣称）
		for k2 in [&"hp", &"speed", &"spawn", &"score", &"spread_cap"]:
			if float(def.get(k2, 0.0)) < 0.0:
				return false
	return true


# RP（征用点数）经济：对齐原作 RequisitionConstants
const RP_BOSS_KILL := 5
const RP_MISSION_REWARD := 3
const RP_REPAIR_COST := 2
const RP_RECHARGE_COST := 2

# 常驻基地任务（对齐原作 base_talent_console 三任务）：
# 初始手牌 = MISSION_DEFS 三项（保持既有 id 语义）；刷新（refresh_missions）从
# MISSION_POOL 无放回重抽 3 个槽位。kind 决定进度来源（kill=击杀数 / survive=存活
# 秒 / boss=Boss 击杀数），goal 为各自目标——任务轮换后 id 变化，进度源按 kind 分发。
const MISSION_DEFS: Array[Dictionary] = [
	{"id": &"kill_5", "name": "战场清扫", "desc": "击杀 5 个敌人", "goal": 5, "kind": &"kill"},
	{"id": &"survive_180", "name": "战场生存", "desc": "存活 180 秒", "goal": 180, "kind": &"survive"},
	{"id": &"boss_1", "name": "主宰之战", "desc": "击杀 1 个 Boss", "goal": 1, "kind": &"boss"},
]

## 基地任务池（TaskPool 数据源，任务轮换随机抽取）：9 项 = 3 类 × 3 档目标
const MISSION_POOL: Array[Dictionary] = [
	{"id": &"kill_5", "name": "战场清扫", "desc": "击杀 5 个敌人", "goal": 5, "kind": &"kill"},
	{"id": &"kill_15", "name": "肃清行动", "desc": "击杀 15 个敌人", "goal": 15, "kind": &"kill"},
	{"id": &"kill_30", "name": "铁雨犁地", "desc": "击杀 30 个敌人", "goal": 30, "kind": &"kill"},
	{"id": &"survive_60", "name": "坚守六十秒", "desc": "存活 60 秒", "goal": 60, "kind": &"survive"},
	{"id": &"survive_180", "name": "战场生存", "desc": "存活 180 秒", "goal": 180, "kind": &"survive"},
	{"id": &"survive_300", "name": "无声防线", "desc": "存活 300 秒", "goal": 300, "kind": &"survive"},
	{"id": &"boss_1", "name": "主宰之战", "desc": "击杀 1 个 Boss", "goal": 1, "kind": &"boss"},
	{"id": &"boss_2", "name": "双王陨落", "desc": "击杀 2 个 Boss", "goal": 2, "kind": &"boss"},
	{"id": &"boss_3", "name": "弑神者", "desc": "击杀 3 个 Boss", "goal": 3, "kind": &"boss"},
]
## 在场任务槽位数（刷新后重抽的目标数量）
const MISSION_SLOTS := 3
## 刷新点数（RefreshPoints）经济：进基地每次 +GRANT_PER_VISIT，刷新任务消耗 REFRESH_COST
## （balance.json base_task 段覆盖；默认 1 点/次进基地、2 点/次刷新 = 攒两次基地换一次刷新）
var refresh_points: int = 0
var REFRESH_COST := 2
var GRANT_PER_VISIT := 1
signal refresh_points_changed(points: int)
## 任务池实例（_init_missions 重建，保证每次对局从全新洗牌序列开始）
var _task_pool: TaskPool = null
## kind -> 池内全部该类型任务 id（进度按 kind 分发，任务轮换后 id 变化仍可推进）
var _missions_by_kind: Dictionary = {}

# 互斥天赋路线：line -> 两个候选 buff（对齐原作 talent_balance_manager）
const ROUTE_LINES: Dictionary = {
	&"offense": [&"spread_shot", &"laser_beam"],
	&"mobility": [&"phase_dash", &"mothership_recall"],
}

const SFX_EXPLOSION: AudioStream = preload("res://assets/audio/explosion.wav")
const SFX_EXPLOSION_BIG: AudioStream = preload("res://assets/audio/explosion_big.wav")
const SFX_PLAYER_HIT: AudioStream = preload("res://assets/audio/player_hit.wav")
const SFX_BUFF_PICK: AudioStream = preload("res://assets/audio/buff_pick.wav")
const SFX_DASH: AudioStream = preload("res://assets/audio/dash.wav")
const SFX_RESUPPLY: AudioStream = preload("res://assets/audio/resupply.wav")
const SFX_HEARTBEAT: AudioStream = preload("res://assets/audio/heartbeat.wav")
const SFX_POOL_SIZE := 6

const SAVE_PATH := "user://savegame.json"
const PROFILE_PATH := "user://profile.json"
## v2：3 命制 lives 字段废弃，改 100 HP 制 health（v1 存档 health 回默认满血）
const PERSIST_VERSION := 2
## 2026-08-04 账户系统：当前用户会话——"" = 未登录（welcome 前/测试兼容，档案走旧 profile.json 路径）、
## "Guest" = 游客（设置仅内存、不存档、不写统计，B7-8）、否则为已登录用户名（档案/存档走 user_db）。
var current_user: String = ""
## profile.json 退役迁移缓存：启动时存在旧 profile 且用户表为空 → 首个注册用户合并后删除（B5）
var _pending_legacy_profile: Dictionary = {}

var high_score: int = 0
## P0-1：手柄默认绑定装配标志（幂等，避免重载重复追加）
var _joypad_bound: bool = false
## P0-1 手柄设置：右摇杆瞄准灵敏度 px/s（默认取 balance player.aim_assist.joy_speed）与摇杆死区
var joy_aim_speed: float = 1400.0
var joy_deadzone: float = 0.5
## PS 布局适配（P0-1 延伸）：SDL 标准位置（JOY_BUTTON_A=底部等）跨 Xbox/PS 一致，
## 仅物理标签不同——按已连接手柄 GUID/名称检测布局，供 UI/文档显示对应标签
signal joy_layout_changed(layout: StringName)
var joy_layout: StringName = &"xbox"  # 默认 Xbox/SDL 标准名；检测到 Sony 手柄切 &"ps"
const XBOX_BUTTON_LABELS: Dictionary = {0: "A", 1: "B", 2: "X", 3: "Y", 4: "LB", 5: "RB", 6: "LS", 7: "RS"}
const PS_BUTTON_LABELS: Dictionary = {0: "✕", 1: "○", 2: "□", 3: "△", 4: "L1", 5: "R1", 6: "L3", 7: "R3"}
## 手柄相关动作清单（死区应用与装配共用）
const JOYPAD_ACTIONS: Array[StringName] = [
	&"move_up",
	&"move_down",
	&"move_left",
	&"move_right",
	&"aim_left",
	&"aim_right",
	&"aim_up",
	&"aim_down",
	&"dash",
	&"boost",
	&"fine_move",
	&"dock",
	&"homecoming",
	&"give_up",
	&"buff_panel",
	&"restart",
	&"parry",
]
## 竞品调研 P0-3：本地高分榜（降序，上限 HIGHSCORE_LIMIT 条，profile 持久化）
var highscores: Array[Dictionary] = []
const HIGHSCORE_LIMIT := 10
var tutorial_done: bool = false

var score: int = 0
var kills: int = 0
var boss_kills: int = 0
## 玩家当前 HP（100 制，对齐原作 MAX_HEALTH；上限见 max_health()）
var health: float = 100.0
var difficulty_multiplier: float = 1.0
## 难度档位（profile 持久化，默认 medium）
var difficulty: StringName = &"medium"
## 设置项：Ctrl 微调 / Shift 加速的模式（false=按住，true=切换；player.gd 侧接入由集成阶段完成）
var ctrl_toggle_mode: bool = false
var shift_toggle_mode: bool = false
## 视角档位（profile 持久化，默认 small=原始视角；相机 zoom = VIEW_ZOOM_LEVELS[view_zoom]）
var view_zoom: StringName = &"small"
## 窗口尺寸档位（profile 持久化，默认 large=1920×1080；尺寸表见 WINDOW_SIZE_LEVELS）
var window_size: StringName = &"large"
## 瞄准辅助强度档位（profile 持久化，默认 medium；常驻不可关，无 off 档；数值见 AIM_ASSIST_ORDER 注释）
var aim_assist_level: StringName = &"medium"
## Meta HUD 当前 LOD（由 MetaHealthFX._ready 从 effects.meta_health.lod 写入；0=MetaFX 接管
## 低血晕影，hud 旧晕影恒 0；非 0=回退路径，hud 保留低血脉动。MetaFX 离场时置 1）
var meta_fx_lod: int = 1
## 无障碍：减少闪光（profile 持久化；开启后色差 ×0.4、禁呼吸/抖动/心跳视觉脉冲，音效保留）
var reduce_flash: bool = false
## 鼠标锁定窗口内（profile 持久化，默认开启；开启后窗口聚焦期间鼠标移出内容区即被拉回，
## 防止准星跟随鼠标出框后位置冻结/跳变；窗口失焦自动放行，不阻碍切换应用）
var mouse_lock: bool = true
## buff id -> 已选层数
var buffs: Dictionary = {}
## 征用点数（基地经济）
var rp: int = 0
## 对局存活秒数（survive_180 任务进度来源）
var run_time: float = 0.0
## 任务 id -> {"progress": int, "claimed": bool}
var missions: Dictionary = {}
## 天赋路线 line -> 所选 buff id
var chosen_routes: Dictionary = {}
## 天赋路线 line -> 被锁定的未选 buff id（不进奖励池）
var locked_routes: Dictionary = {}

var _next_milestone: int = MILESTONE_BASE[0]
var _milestone_count: int = 0

## 难度进程曲线参数（_apply_balance 从 balance.json progression 段读取缓存，热路径免查 JSON）
var _prog_per_boss_kill: float = 0.6
var _prog_per_ten_minutes: float = 1.5
var _prog_time_step_seconds: float = 30.0
var _survive_sec_cached: int = -1  # 任务进度整秒缓存（_process 热路径免每帧字典访问）
## B 梯队（fair plan §8）：DDA 弹幕密度降档——玩家受击后短暂拉长敌弹/波次间隔
## （只拉间隔不降收益，分数公平）；_apply_balance 从 balance.json dda 段缓存
var DDA_DURATION := 5.0
var DDA_FACTOR := 1.3
var _dda_timer: float = 0.0
## 回血链热路径缓存（P0-2）：max_health 基础值 _apply_balance 缓存；regen 档位难度变更时刷新。
## 默认值须与脚本默认 difficulty=medium 档一致（medium: regen_delay=4.0, regen_rate=2.0）。
var _max_hp_base: float = 100.0
var _max_hp_bonus: float = 50.0
var _regen_delay: float = 4.0
var _regen_rate: float = 2.0
## 已计入难度乘数的时间档位（按 time_step_seconds 量化步进，避免连续漂移）
var _difficulty_time_step: int = 0

## 启动计时基准（autoload 最早生命周期点；--startup-time 时由 main 打印分段耗时）
var boot_ticks_msec: int = 0
## 启动/读档时检测到损坏并已隔离备份（开始面板据此提示；读取正常后置回 false）
var save_corrupt: bool = false
var profile_corrupt: bool = false


func _enter_tree() -> void:
	boot_ticks_msec = Time.get_ticks_msec()


## 实体注册表（A2 阶段 4：数据归 EntityRegistry，属性转发保持外部语法不变）。
## 热路径缓存，避免每帧 get_nodes_in_group 分配。
## enemy/boss 在 _ready/_exit_tree 时注册/注销，player 单独缓存引用。
var enemies: Array[Node] = []:
	get:
		return _registry.enemies
## P0-1（2026-08-05 审计）：敌弹注册表转发（death_replay 录制数据源，替代 get_children 遍历）
var enemy_bullets: Array[Bullet] = []:
	get:
		return _registry.enemy_bullets
var player_ref: Node2D = null:
	get:
		return _registry.player_ref
	set(value):
		_registry.player_ref = value
## 玩家受击 Hitbox（player._ready/_exit_tree 维护；敌机/Boss 撞击逐帧轮询用）
var player_hitbox: Area2D = null:
	get:
		return _registry.player_hitbox
	set(value):
		_registry.player_hitbox = value
## 子弹对象池实例（由 bullet_pool.gd 在 _ready 时登记）
var bullet_pool: BulletPool = null:
	get:
		return _registry.bullet_pool
	set(value):
		_registry.bullet_pool = value
## 敌机对象池实例（由 enemy_pool.gd 在 _ready 时登记）
var enemy_pool: EnemyPool = null:
	get:
		return _registry.enemy_pool
	set(value):
		_registry.enemy_pool = value
## 辅助瞄准框覆盖层实例（由 aim_frame_layer.gd 在 _ready 时登记；player._fire 查询框内标记敌）
var aim_frame_layer: AimFrameLayer = null:
	get:
		return _registry.aim_frame_layer
	set(value):
		_registry.aim_frame_layer = value
## 迷雾事件管理器转发（全局单例访问口；挂本节点下，_ready 时 add_child）
var fog_events: FogEventManager:
	get:
		return _fog_events
## 统一事件管理器转发（全局单例访问口；挂本节点下，_ready 时 add_child）
var events: GameEventManager:
	get:
		return _events


func register_enemy(node: Node) -> void:
	_registry.register_enemy(node)


## P0-1：敌弹注册/注销转发（bullet.gd 维护）
func register_enemy_bullet(b: Bullet) -> void:
	_registry.register_enemy_bullet(b)


func unregister_enemy_bullet(b: Bullet) -> void:
	_registry.unregister_enemy_bullet(b)


## G010：注册表存在性判定 O(1)（追踪弹热路径，替代 enemies.has() 线性扫描）
func enemies_has(node: Node) -> bool:
	return _registry.has_enemy(node)


func unregister_enemy(node: Node) -> void:
	_registry.unregister_enemy(node)


func _ready() -> void:
	_load_balance()
	_apply_balance()
	# 常驻音效播放器池：播放节点被 queue_free 时音效也不会中断（SfxPlayer 子节点挂本节点）
	add_child(_sfx_player)
	_sfx_player.build_pool(SFX_POOL_SIZE)
	# 迷雾事件管理器挂载（balance 已在 _apply_balance 就绪；管理器 _ready 读 cfg）
	add_child(_fog_events)
	# 统一事件管理器挂载（fog 组经迷雾门面 wire() 接线；encounter 组由 main._ready 注册）
	add_child(_events)
	_fog_events.wire(_events)
	_capture_default_bindings()
	_init_missions()
	load_profile()
	_maybe_migrate_legacy_profile()  # 账户系统：旧 profile.json 迁移缓存（首个注册用户合并）
	_apply_window_size()  # 无 profile 时 load_profile 不会应用窗口尺寸，这里补一次默认档位
	var tr_zh := load("res://data/translations.zh.translation") as Translation
	var tr_en := load("res://data/translations.en.translation") as Translation
	if tr_zh != null:
		TranslationServer.add_translation(tr_zh)
	if tr_en != null:
		TranslationServer.add_translation(tr_en)
	TranslationServer.set_locale(locale)
	apply_key_bindings()
	_bind_joypad_defaults()
	# PS 布局检测：监听手柄插拔并刷新布局（标签显示用）
	Input.joy_connection_changed.connect(_on_joy_connection_changed)
	_detect_joy_layout()
	_next_milestone = milestone_threshold(0)
	# B 梯队：受击触发 DDA 降档（player_damaged 为减免后信号，Meta HUD 受击层同源）
	player_damaged.connect(_on_player_damaged_dda)


# 暂停（Buff/结算 UI）时不计存活时间
func _process(delta: float) -> void:
	run_time += delta
	# 整秒边界才推进任务（缓存秒值：避免每帧 int(run_time) + missions 字典访问——热路径禁字典约定）
	var survive_sec := int(run_time)
	if survive_sec != _survive_sec_cached:
		_survive_sec_cached = survive_sec
		_set_kind_progress(&"survive", survive_sec)
	# 时间轴难度档：跨过量化步进边界时重算难度乘数（去硬顶曲线的时间分量）
	if int(floorf(run_time / _prog_time_step_seconds)) != _difficulty_time_step:
		if _recompute_difficulty():
			difficulty_changed.emit(difficulty_multiplier)
	# DDA 降档计时（受击触发；暂停时 process 冻结，与对局节奏一致）
	if _dda_timer > 0.0:
		_dda_timer -= delta


func play_sfx(stream: AudioStream, volume_db: float = 0.0, pitch_scale: float = 1.0) -> void:
	# headless 短路与池化复用逻辑在 SfxPlayer（A2 阶段 3）
	_sfx_player.play(stream, volume_db, pitch_scale)


## 退出前停止所有仍在播放的音效：带播未停时 AudioStreamPlayback 会在退出时泄漏
func stop_all_sfx() -> void:
	_sfx_player.stop_all()
	if player_ref != null and is_instance_valid(player_ref):
		var audio: AudioStreamPlayer2D = player_ref.get_node_or_null("AudioStreamPlayer2D")
		if audio != null:
			audio.stop()


func shake(strength: float) -> void:
	screen_shake.emit(strength)


func reset_run() -> void:
	score = 0
	kills = 0
	boss_kills = 0
	buffs.clear()
	health = max_health()
	difficulty_multiplier = 1.0
	_difficulty_time_step = 0
	rp = 0
	run_time = 0.0
	_init_missions()
	refresh_points = 0
	refresh_points_changed.emit(refresh_points)
	chosen_routes.clear()
	locked_routes.clear()
	_milestone_count = 0
	_next_milestone = milestone_threshold(0)
	_dda_timer = 0.0  # A 审计：DDA 计时跨对局残留——旧局受击降档渗透新局
	buffs_changed.emit()


func add_score(points: int) -> void:
	# 难度分数倍率统一在此乘算（easy ×1 / medium ×2 / hard ×3，配置表里的分值不变）
	score += points * score_multiplier()
	score_changed.emit(score)
	if score >= _next_milestone:
		_milestone_count += 1
		_next_milestone = milestone_threshold(_milestone_count)
		milestone_reached.emit(score)


# ---------------- 难度档位 ----------------


## 切换难度档位（非法档位忽略），持久化到 profile 并广播
func set_difficulty(p_difficulty: StringName) -> void:
	if not DIFFICULTY_DEFS.has(p_difficulty) or p_difficulty == difficulty:
		return
	difficulty = p_difficulty
	_refresh_regen_cache()
	difficulty_selected.emit(difficulty)
	save_profile()


func difficulty_label() -> String:
	return tr("DIFF_" + String(difficulty).to_upper())


## B 梯队：受击触发 DDA 降档（重入安全——幂等置位，重复受击刷新计时）
func _on_player_damaged_dda(_amount: float, _from_pos: Vector2) -> void:
	_dda_timer = DDA_DURATION


func score_multiplier() -> int:
	# 2026-08-03 审计回退：曾尝试缓存 _score_multiplier_cache，但 difficulty 是公开字段，
	# 测试/调用方直写不触发 _refresh_regen_cache（白盒契约），缓存会返回旧值；与同族
	# enemy_hp_multiplier/enemy_speed_multiplier/spawn_interval_multiplier 一致保持直接查表
	return int(DIFFICULTY_DEFS[difficulty]["score"])


## B 梯队（fair plan §8）：DDA 降档中（玩家受击后 DDA_DURATION 内）——消费方
## （enemy 开火计时 / spawner 波次间隔 / boss 攻击间隔）乘 dda_factor() 拉长间隔
func dda_active() -> bool:
	return _dda_timer > 0.0


## DDA 降档乘区：active 时返回配置因子（>1 拉长间隔），否则 1.0（热路径零分支常态）
func dda_factor() -> float:
	return DDA_FACTOR if _dda_timer > 0.0 else 1.0


## 测试/诊断：立即结束降档（对齐「测试经公开接口」白盒契约）
func reset_dda() -> void:
	_dda_timer = 0.0


func enemy_hp_multiplier() -> float:
	return float(DIFFICULTY_DEFS[difficulty]["hp"])


func enemy_speed_multiplier() -> float:
	return float(DIFFICULTY_DEFS[difficulty]["speed"])


## 敌方 HP 对局进程 ramp：×(1 + hp_ramp_factor × (难度乘数 − 1))，随 Boss 击杀线性成长。
## 纯查询委托 BalanceService（难度乘数作参数）。
func enemy_hp_ramp() -> float:
	return _balance_service.enemy_hp_ramp(difficulty_multiplier)


## 敌方伤害对局进程 ramp：×(1 + damage_ramp_factor × (难度乘数 − 1))，
## 统一作用于全部敌方伤害源（敌弹/Boss 弹/撞体/编队炸弹；2026-07-29 无限段修订）。
## 纯查询委托 BalanceService（难度乘数作参数）。
func enemy_damage_ramp() -> float:
	return _balance_service.enemy_damage_ramp(difficulty_multiplier)


func spawn_interval_multiplier() -> float:
	return float(DIFFICULTY_DEFS[difficulty]["spawn"])


## spread 弹种敌机同屏上限（easy 1 / medium 2 / hard 3）
func spread_enemy_cap() -> int:
	return int(DIFFICULTY_DEFS[difficulty]["spread_cap"])


## 被动回血：距上次受伤 regen_delay 秒起每秒回 regen_rate HP（对齐原作 HEALTH_REGEN）
## P0-2：档位值在难度变更/重新加载时缓存，热路径免双层字典查找
func passive_regen_delay() -> float:
	return _regen_delay


func passive_regen_rate() -> float:
	return _regen_rate


func _refresh_regen_cache() -> void:
	var def: Variant = DIFFICULTY_DEFS.get(difficulty, {})
	if def is Dictionary:
		_regen_delay = float(def.get("regen_delay", _regen_delay))
		_regen_rate = float(def.get("regen_rate", _regen_rate))


# ---------------- 里程碑阈值曲线 ----------------


## 第 index 次（0 起）里程碑的分数阈值：8 档基础阈值循环，档差按 ×1.35^cycle 增长，
## 再乘难度阈值倍率（easy ×1 / medium ×1 / hard ×1.5）。
## A 审计：极大 index 时 pow 可能溢出至 inf，钳制 mult 上限避免 int(roundf(inf)) UB。
func milestone_threshold(index: int) -> int:
	var n := milestone_base.size()
	if n <= 0:
		return 0
	@warning_ignore("integer_division")
	var cycle := maxi(index, 0) / n
	var step := maxi(index, 0) % n
	var total := 0.0
	for c in cycle + 1:
		# A 审计：cycle_mult >1 时 pow 指数增长，极大 cycle 溢出至 inf；钳至 finite
		# 防止 total 累积为 inf 后 int(roundf(inf)) 行为未定义
		var mult := minf(pow(milestone_cycle_mult, c), 1e15)
		var last_step := step if c == cycle else n - 1
		var prev := 0.0
		for i in last_step + 1:
			total += (milestone_base[i] - prev) * mult
			prev = milestone_base[i]
	return int(roundf(total * float(DIFFICULTY_DEFS[difficulty]["milestone"])))


## 测试钩子（A7 遗留清理，公开化）：直接设定下一个里程碑阈值（不动曲线计数，保证测试确定性）
func set_milestone_override(threshold: int) -> void:
	_next_milestone = threshold


## A7：测试/诊断白盒断言经公开接口
## 当前已触发的里程碑数（2026-08-04 母舰升级档位等消费点）
func milestone_count() -> int:
	return _milestone_count


func next_milestone() -> int:
	return _next_milestone


func recompute_difficulty() -> void:
	_recompute_difficulty()


# ---------------- 设置项（Ctrl/Shift 模式） ----------------


## Ctrl 微调模式：false=按住生效，true=按一下切换；持久化到 profile
func set_ctrl_toggle_mode(enabled: bool) -> void:
	ctrl_toggle_mode = enabled
	save_profile()


## Shift 加速模式：false=按住生效，true=按一下切换；持久化到 profile
func set_shift_toggle_mode(enabled: bool) -> void:
	shift_toggle_mode = enabled
	save_profile()


# ---------------- 视角缩放 ----------------

## 视角档位表（设置页三选，profile 持久化；值为相机 zoom 倍率）。
## zoom>1 时可见世界区域 = 视口 ÷ zoom（以相机位置为中心收窄），
## 所有"屏幕边缘/出屏"逻辑统一走 view_world_rect() 适配。
const VIEW_ZOOM_LEVELS: Dictionary = {&"small": 1.0, &"medium": 1.35, &"large": 1.7}
const VIEW_ZOOM_ORDER: Array[StringName] = [&"small", &"medium", &"large"]

## main 场景相机注册表（main.gd 在 _ready/_exit_tree 维护），供可见区域计算
var camera_ref: Camera2D = null:
	get:
		return _registry.camera_ref
	set(value):
		_registry.camera_ref = value
		_invalidate_view_rect_cache()
## 生效 zoom 倍率缓存（set_view_zoom/load_profile 同步；热路径免查表，须与 small 档一致）
var _view_zoom_factor: float = 1.0
## view_world_rect 物理帧缓存：同帧多次调用免重复视口查询（子弹/敌机/玩家每帧共用一帧结果）。
## zoom 因子或相机注册变更时置 -1 强制重算；相机位置固定 (960,540)，帧内语义不变。
var _view_rect_frame: int = -1
var _view_rect_cached: Rect2 = Rect2()


## 切换视角档位（非法/同档忽略），持久化到 profile 并广播
func set_view_zoom(level: StringName) -> void:
	if not VIEW_ZOOM_LEVELS.has(level) or level == view_zoom:
		return
	view_zoom = level
	_view_zoom_factor = VIEW_ZOOM_LEVELS[level]
	_invalidate_view_rect_cache()
	save_profile()
	view_zoom_changed.emit(_view_zoom_factor)


func view_zoom_factor() -> float:
	return _view_zoom_factor


func set_view_zoom_factor(factor: float) -> void:
	_view_zoom_factor = factor
	_invalidate_view_rect_cache()


# ---------------- 窗口大小 ----------------

## 窗口尺寸档位表（设置页三选，profile 持久化；stretch 等比缩放，仅改窗口物理尺寸）。
## 非 const：Vector2i 构造为非常量表达式（同 spawner.ENEMY_TYPES 先例）。
static var WINDOW_SIZE_LEVELS: Dictionary = {
	&"small": Vector2i(1280, 720),
	&"medium": Vector2i(1600, 900),
	&"large": Vector2i(1920, 1080),
}
const WINDOW_SIZE_ORDER: Array[StringName] = [&"small", &"medium", &"large"]


## 切换窗口尺寸档位（非法/同档忽略）：立即应用窗口，持久化到 profile 并广播
func set_window_size(level: StringName) -> void:
	if not WINDOW_SIZE_LEVELS.has(level) or level == window_size:
		return
	window_size = level
	_apply_window_size()
	save_profile()
	window_size_changed.emit(window_size)


## 应用当前档位到窗口：仅窗口模式生效；headless 为 dummy 渲染直接跳过。
## 档位尺寸按逻辑点定义：高分屏（Retina 等 content scale>1）乘屏幕缩放换算物理像素，
## 否则 1920×1080 档位在 2x 屏上只显示为 960×540 点的小窗；超出当前屏可用区域时等比收缩并居中。
func _apply_window_size() -> void:
	if DisplayServer.get_name() == "headless":
		return
	var win := get_window()
	if win == null or win.mode != Window.MODE_WINDOWED:
		return
	var screen := win.current_screen
	var scale := DisplayServer.screen_get_scale(screen)
	var phys := Vector2i(Vector2(WINDOW_SIZE_LEVELS[window_size]) * scale)
	var usable: Rect2i = DisplayServer.screen_get_usable_rect(screen)
	if phys.x > usable.size.x or phys.y > usable.size.y:
		var fit := minf(float(usable.size.x) / phys.x, float(usable.size.y) / phys.y)
		phys = Vector2i(Vector2(phys) * fit)
	win.size = phys
	@warning_ignore("integer_division")
	win.position = usable.position + (usable.size - phys) / 2


# ---------------- 瞄准辅助强度 ----------------

## 强度档位表（设置页三选，profile 持久化；辅助瞄准常驻、刻意不提供关闭档）。
## 各档数值（辅助框内边距 frame_pad/追踪转向速率 homing_turn_rate）在 balance.json player.aim_assist.levels。
const AIM_ASSIST_ORDER: Array[StringName] = [&"low", &"medium", &"high"]


## 切换瞄准辅助强度档位（非法/同档忽略），持久化到 profile 并广播
func set_aim_assist_level(level: StringName) -> void:
	if not AIM_ASSIST_ORDER.has(level) or level == aim_assist_level:
		return
	aim_assist_level = level
	save_profile()
	aim_assist_changed.emit(level)


## 无障碍·减少闪光：开关持久化到 profile 并广播（Meta HUD 据此折算色差/禁脉冲）
func set_reduce_flash(enabled: bool) -> void:
	if enabled == reduce_flash:
		return
	reduce_flash = enabled
	save_profile()
	reduce_flash_changed.emit(enabled)


## 鼠标锁定窗口内：开关持久化到 profile 并广播（MouseTrap 据此决定是否拉回出框鼠标）
func set_mouse_lock(enabled: bool) -> void:
	if enabled == mouse_lock:
		return
	mouse_lock = enabled
	save_profile()
	mouse_lock_changed.emit(enabled)


## P0-1 手柄设置 setter：右摇杆瞄准灵敏度（200..4000 px/s）。
## K06：只更新内存 + 广播（灵敏度不影响 InputMap 死区）；持久化由设置页 drag_ended 统一
## 提交——原实现每步全量原子写盘，滑杆拖动（数十次 value_changed）放大为磁盘写风暴
func set_joy_aim_speed(value: float) -> void:
	joy_aim_speed = clampf(value, 200.0, 4000.0)
	joy_settings_changed.emit(joy_aim_speed, joy_deadzone)


## P0-1 手柄设置 setter：摇杆死区（0.05..0.90，应用至全部手柄动作的 InputMap deadzone）。
## K06：立即应用死区（InputMap 全局生效，base_system_test 契约）+ 广播；不自动写盘
func set_joy_deadzone(value: float) -> void:
	joy_deadzone = clampf(value, 0.05, 0.9)
	for a in JOYPAD_ACTIONS:
		if InputMap.has_action(a):
			InputMap.action_set_deadzone(a, joy_deadzone)
	joy_settings_changed.emit(joy_aim_speed, joy_deadzone)


## 手柄设置持久化：设置页滑杆 drag_ended 调用一次（setter 不再自动写盘，防拖动写风暴）
func persist_joy_settings() -> void:
	save_profile()


## 当前可见世界区域（相机未注册时以 (960,540) 为心），margin 向外扩张。
## 屏幕边缘钳制 / 出屏销毁 / 刷怪位置统一以此为准；zoom=1 时即全屏 1920×1080。
## 物理帧内缓存（P0-1）：同一物理帧内多次调用（每弹/每敌/玩家/Boss）共享一次视口查询。
func view_world_rect(margin: float = 0.0) -> Rect2:
	if margin == 0.0:
		return _cached_view_rect()
	return _cached_view_rect().grow(margin)


func _invalidate_view_rect_cache() -> void:
	_view_rect_frame = -1


func _cached_view_rect() -> Rect2:
	var frame := Engine.get_physics_frames()
	if frame != _view_rect_frame:
		_view_rect_frame = frame
		var center := Vector2(960.0, 540.0)
		if camera_ref != null and is_instance_valid(camera_ref):
			center = camera_ref.global_position
		var size := Vector2(1920.0, 1080.0)
		var viewport := get_viewport()
		if viewport != null:
			size = viewport.get_visible_rect().size
		size /= _view_zoom_factor
		_view_rect_cached = Rect2(center - size * 0.5, size)
	return _view_rect_cached


func add_kill() -> void:
	kills += 1
	_set_kind_progress(&"kill", kills)


func add_boss_kill(score_scale: float = 1.0) -> void:
	boss_kills += 1
	# G012：加分基准入 balance.json（milestones.boss_kill_base；击杀低频，非热路径可直查）
	add_score(int(cfg("milestones.boss_kill_base", 500.0) * score_scale))
	add_rp(RP_BOSS_KILL)
	_set_kind_progress(&"boss", boss_kills)
	if _recompute_difficulty():
		difficulty_changed.emit(difficulty_multiplier)


## 难度乘数对局进程曲线（2026-07-29 无限段修订，D1=必死曲线，docs/ENDLESS_BALANCE_PLAN.md）：
## 1 + per_boss_kill×Boss击杀 + 时间轴累进（每 time_step_seconds 量化一档，每 10 分钟 +per_ten_minutes）。
## 线性无封顶：敌方 HP/伤害 ramp 随之无限增长，最终超过玩家固定成长上限。
## 返回乘数是否变化；变化时由调用方广播 difficulty_changed（apply_run_save 统一在末尾广播）。
func _recompute_difficulty() -> bool:
	var step := int(floorf(run_time / _prog_time_step_seconds))
	var new_mult := 1.0 + _prog_per_boss_kill * boss_kills + step * _prog_time_step_seconds / 600.0 * _prog_per_ten_minutes
	_difficulty_time_step = step
	if is_equal_approx(new_mult, difficulty_multiplier):
		return false
	difficulty_multiplier = new_mult
	return true


## 生命上限：基础 100 + extra_life 每层 +50（对齐原作 EXTRA_LIFE_BONUS_HP）
## P0-2：基础值 _apply_balance 缓存，热路径免 cfg 路径解析（extra_life 层数查询 O(1)）
func max_health() -> float:
	return _max_hp_base + _max_hp_bonus * buff_count(&"extra_life")


func lose_health(amount: float = 1.0) -> void:
	health = maxf(health - amount, 0.0)
	health_changed.emit(health)
	if health <= 0.0:
		player_died.emit()


## 治疗（单点封顶 max_health，调用侧不再各自判断）
func heal(amount: float) -> void:
	health = minf(health + amount, max_health())
	health_changed.emit(health)


## 吸血 buff：击杀回复 int(上限 × 10%)（对齐原作 LIFESTEAL_FRACTION），每帧至多结算一次
var _lifesteal_frame: int = -1
## 吸血比例缓存（P0-2 同款：_apply_balance 刷新，击杀帧免 cfg 路径解析）
var _lifesteal_fraction: float = 0.1


func try_lifesteal() -> void:
	if buff_count(&"lifesteal") <= 0:
		return
	var frame := Engine.get_physics_frames()
	if frame == _lifesteal_frame:
		return
	_lifesteal_frame = frame
	heal(maxi(1, int(max_health() * _lifesteal_fraction)))


func buff_count(id: StringName) -> int:
	return buffs.get(id, 0)


func add_buff(id: StringName) -> void:
	buffs[id] = buff_count(id) + 1
	buffs_changed.emit()


## 消耗一层 buff（护盾等一次性层；无剩余层返回 false；层数变动广播 buffs_changed）
func consume_buff(id: StringName) -> bool:
	if buff_count(id) <= 0:
		return false
	buffs[id] = buff_count(id) - 1
	buffs_changed.emit()
	return true


# ---------------- 可改键系统 ----------------

const REBINDABLE_ACTIONS: Array[StringName] = [
	&"move_up",
	&"move_down",
	&"move_left",
	&"move_right",
	&"boost",
	&"fine_move",
	&"dash",
	&"dock",
	&"homecoming",
	&"give_up",
	&"buff_panel",
	&"parry",
]

## action -> Array[int]（keycode，最多 2 个）；restart/pause 固定不可改
var key_bindings: Dictionary = {}
var _default_bindings: Dictionary = {}


func _capture_default_bindings() -> void:
	_default_bindings.clear()
	for a in REBINDABLE_ACTIONS:
		_default_bindings[a] = _get_action_keycodes(a)


func _get_action_keycodes(action: StringName) -> Array[int]:
	var out: Array[int] = []
	for ev in InputMap.action_get_events(action):
		if ev is InputEventKey:
			@warning_ignore("unsafe_property_access")
			var k: int = ev.keycode if ev.keycode != 0 else ev.physical_keycode
			out.append(k)
			if out.size() >= 2:
				break
	return out


## 用 key_bindings（含 profile 覆盖）刷新 InputMap
func apply_key_bindings() -> void:
	# H02（健壮性审核）：只擦除键盘事件，保留手柄事件——action_erase_events 会连
	# _bind_joypad_defaults 装配的手柄绑定一起清掉（改键后本会话手柄失效）
	for a in REBINDABLE_ACTIONS:
		for ev in InputMap.action_get_events(a):
			if ev is InputEventKey:
				InputMap.action_erase_event(a, ev)
		for k: int in key_bindings.get(a, _default_bindings.get(a, [])):
			var ev := InputEventKey.new()
			ev.keycode = k
			InputMap.action_add_event(a, ev)


## P0-1（竞品调研）：手柄默认绑定运行时装配——project.godot 保持键盘单一事实源，
## 手柄左摇杆移动/动作键/右摇杆瞄准在此追加（InputMap.action_add_event），
## 与 keybind 改键系统（只改键盘事件）互不覆盖；一次装配幂等。
func _bind_joypad_defaults() -> void:
	if _joypad_bound:
		return
	_joypad_bound = true
	# 左摇杆移动（轴 0=x、1=y；axis_value 负=上/左）
	_add_joy_axis(&"move_up", 1, -1.0)
	_add_joy_axis(&"move_down", 1, 1.0)
	_add_joy_axis(&"move_left", 0, -1.0)
	_add_joy_axis(&"move_right", 0, 1.0)
	# 动作键（B=ui_cancel 已被引擎默认占用，返航让位 Y）
	_add_joy_button(&"dash", 0)  # A
	_add_joy_button(&"boost", 5)  # RB
	_add_joy_button(&"fine_move", 4)  # LB
	_add_joy_button(&"dock", 2)  # X
	_add_joy_button(&"homecoming", 3)  # Y（长按返航）
	_add_joy_button(&"give_up", 7)  # R3（长按放弃）
	_add_joy_button(&"buff_panel", 6)  # L3（展开/收起 buff 栏）
	_add_joy_button(&"restart", 0)  # A（结算/暂停重开）
	_add_joy_axis(&"parry", 4, -1.0)  # LT 左扳机（弧光弹反盾，轴 4 负向按下；阈值经 deadzone）
	# 右摇杆瞄准（player.aim_point 经 Input.get_vector 读取四向动作，虚拟准星）。
	# H01（健壮性审核）：必须装配正负两个独立动作——get_vector(pos, neg) 取 strength 差值，
	# 同一动作正负双向传会恒为零（右摇杆瞄准完全失效）
	_add_joy_axis(&"aim_left", 2, -1.0)
	_add_joy_axis(&"aim_right", 2, 1.0)
	_add_joy_axis(&"aim_up", 3, -1.0)
	_add_joy_axis(&"aim_down", 3, 1.0)
	# 应用已持久化的摇杆死区（不触发 save/广播，启动装配专用）
	for a in JOYPAD_ACTIONS:
		if InputMap.has_action(a):
			InputMap.action_set_deadzone(a, joy_deadzone)


func _add_joy_axis(action: StringName, axis: int, value: float) -> void:
	if not InputMap.has_action(action):
		InputMap.add_action(action)
	var ev := InputEventJoypadMotion.new()
	ev.axis = axis
	ev.axis_value = value
	InputMap.action_add_event(action, ev)


func _add_joy_button(action: StringName, button: int) -> void:
	if not InputMap.has_action(action):
		InputMap.add_action(action)
	var ev := InputEventJoypadButton.new()
	ev.button_index = button
	InputMap.action_add_event(action, ev)


## PS 布局适配：手柄插拔时重检布局
func _on_joy_connection_changed(_device: int, _connected: bool) -> void:
	_detect_joy_layout()


## 检测已连接手柄的布局：SDL GUID vendor = 0x054c（LE "4c05"）为 Sony（DualShock/DualSense），
## 名称含 PlayStation 特征词兜底；其余保持 Xbox/SDL 标准布局（位置语义一致）。
func _detect_joy_layout() -> void:
	var found: StringName = &""
	for d in Input.get_connected_joypads():
		if is_ps_guid(Input.get_joy_guid(d)):
			found = &"ps"
			break
		var name := Input.get_joy_name(d).to_lower()
		if "dualshock" in name or "dualsense" in name or "playstation" in name:
			found = &"ps"
			break
	if found != &"" and found != joy_layout:
		joy_layout = found
		joy_layout_changed.emit(joy_layout)
	elif found == &"" and joy_layout != &"xbox":
		# 2026-08-03 审计：全部手柄拔出时回落 Xbox/SDL 布局，防 PS 标签残留误导设置页
		joy_layout = &"xbox"
		joy_layout_changed.emit(joy_layout)


## Sony 手柄 GUID 判定（SDL GUID：vendor 0x054c 小端序为 "4c05"；PS4/PS5/DualShock/DualSense）
func is_ps_guid(guid: String) -> bool:
	return guid.begins_with("030000004c05")


## 手柄按钮的物理标签（按当前布局）：PS 用 ✕○□△/L1/R1…，Xbox/SDL 用 A/B/X/Y/LB/RB…
func joy_button_label(button: int) -> String:
	return (
		str(PS_BUTTON_LABELS.get(button, XBOX_BUTTON_LABELS.get(button, str(button))))
		if joy_layout == &"ps"
		else str(XBOX_BUTTON_LABELS.get(button, str(button)))
	)


## 改键：清除该动作现有键设新键；冲突键从占用者移除（允许交换）
## G04：冲突清理同时扫默认绑定——未自定义动作的默认键被占用时置空绑定覆盖默认，
## 避免 apply_key_bindings 从默认表重灌同键造成两动作冲突
func rebind_action(action: StringName, keycode: int) -> bool:
	if action not in REBINDABLE_ACTIONS:
		return false
	for a in REBINDABLE_ACTIONS:
		if a == action:
			continue
		var effective: Array = key_bindings.get(a, _default_bindings.get(a, []))
		if effective.is_empty():
			continue  # 空绑定 = 该动作无键，不占用任何键
		if effective.has(keycode):
			if a in key_bindings:
				(key_bindings[a] as Array).erase(keycode)
			else:
				key_bindings[a] = []  # 默认键被占用：空绑定覆盖默认，解除占用
	key_bindings[action] = [keycode]
	apply_key_bindings()
	save_profile()
	key_bindings_changed.emit()
	return true


func reset_key_bindings() -> void:
	key_bindings = _default_bindings.duplicate(true)
	apply_key_bindings()
	save_profile()
	key_bindings_changed.emit()


func action_keys_text(action: StringName) -> String:
	var keys: Array = key_bindings.get(action, _default_bindings.get(action, []))
	if keys.is_empty():
		return tr("SET_UNBOUND")
	var parts: Array[String] = []
	for k in keys:
		parts.append(OS.get_keycode_string(k))
	return " / ".join(parts)


# ---------------- 语言（中英双语） ----------------

## 当前语言（"zh"/"en"，profile 持久化）
var locale: String = "zh"


func set_locale(p_locale: String) -> void:
	if p_locale != "zh" and p_locale != "en":
		return
	locale = p_locale
	TranslationServer.set_locale(p_locale)
	save_profile()
	locale_changed.emit()


# ---------------- RP 经济 / 基地任务 / 天赋路线 ----------------


func add_rp(amount: int) -> void:
	rp += amount
	rp_changed.emit(rp)


## 余额不足返回 false 且不扣减
func spend_rp(amount: int) -> bool:
	if rp < amount:
		return false
	rp -= amount
	rp_changed.emit(rp)
	return true


func _init_missions() -> void:
	missions.clear()
	for def in MISSION_DEFS:
		# P0-3：goal 一次性缓存进条目，_set_mission_progress 免每帧线性扫 MISSION_POOL
		missions[def["id"]] = {"progress": 0, "claimed": false, "goal": int(def["goal"])}
	# 任务轮换：每局从全新洗牌序列开始（初始手牌固定 MISSION_DEFS，刷新才随机）
	_task_pool = TaskPool.new(MISSION_POOL)
	_rebuild_kind_index()


## kind -> 池内 id 索引重建（MISSION_POOL 为 const，仅 _init_missions 调用一次）
func _rebuild_kind_index() -> void:
	_missions_by_kind.clear()
	for def in MISSION_POOL:
		var kind: StringName = def["kind"]
		if not _missions_by_kind.has(kind):
			_missions_by_kind[kind] = []
		(_missions_by_kind[kind] as Array).append(def["id"])


## C32 修复：公开任务重置口（仅清任务进度，不清 rp/buffs——比 reset_run 副作用小，
## 供测试/调用方在保留状态的前提下重置 missions）
func reset_missions() -> void:
	_init_missions()


func _set_mission_progress(id: StringName, value: int) -> void:
	if not missions.has(id):
		return
	var m: Dictionary = missions[id]
	# P0-3：survive 类每帧触发但整秒才变化一次，未变化跳过字典写与完成判定
	if int(m["progress"]) == value:
		return
	var goal := int(m.get("goal", 0))
	var was_done: bool = int(m["progress"]) >= goal
	m["progress"] = value
	if not was_done and value >= goal:
		mission_completed.emit(id)


## 按 kind 推进全部该类型在场任务的进度（任务轮换后 id 变化，进度源按 kind 分发；
## 已不在场的 id 由 _set_mission_progress 的 missions.has 守卫自动跳过）
func _set_kind_progress(kind: StringName, value: int) -> void:
	for id in _missions_by_kind.get(kind, []):
		_set_mission_progress(id, value)


## 在场任务 id 列表（base_console 任务面板据此渲染；任务轮换后不再等于 MISSION_DEFS）
func active_mission_ids() -> Array[StringName]:
	var out: Array[StringName] = []
	for id in missions:
		out.append(id)
	return out


## 任务定义查询（MISSION_POOL 无命中返回 {}，供 goal/存档恢复校验共用）
func _mission_def(id: StringName) -> Dictionary:
	for def in MISSION_POOL:
		if def["id"] == id:
			return def
	return {}


func mission_goal(id: StringName) -> int:
	return int(_mission_def(id).get("goal", 0))


func mission_progress(id: StringName) -> int:
	return int(missions.get(id, {}).get("progress", 0))


func is_mission_done(id: StringName) -> bool:
	return missions.has(id) and mission_progress(id) >= mission_goal(id)


func is_mission_claimed(id: StringName) -> bool:
	return bool(missions.get(id, {}).get("claimed", false))


## 领取已完成任务的 +3RP，每任务每局限领一次
func claim_mission(id: StringName) -> bool:
	if not is_mission_done(id) or is_mission_claimed(id):
		return false
	missions[id]["claimed"] = true
	add_rp(RP_MISSION_REWARD)
	return true


# ---------------- 基地任务轮换（RefreshPoints 经济 + TaskPool 重抽） ----------------


## 进基地发放刷新点数（amount < 0 用 GRANT_PER_VISIT 档位值；base_console.show_base 调用）
func grant_refresh_points(amount: int = -1) -> void:
	refresh_points += GRANT_PER_VISIT if amount < 0 else amount
	refresh_points_changed.emit(refresh_points)


## 刷新资格校验（点数不足禁止刷新；UI 据此禁用按钮并提示）
func can_refresh_missions() -> bool:
	return refresh_points >= REFRESH_COST


## 刷新任务：消耗 RefreshPoints 重抽任务（槽位数 MISSION_SLOTS）。
## 已完成未领取的任务保留（防止刷新吞掉待领奖励），其余槽位从任务池无放回重抽
## （排除在场 id，避免与保留槽位重号）。余额不足返回 false 且不扣减。
func refresh_missions() -> bool:
	if not can_refresh_missions():
		return false
	if _task_pool == null:
		_init_missions()  # 防御：池未初始化（异常时序）时重建
	refresh_points -= REFRESH_COST
	refresh_points_changed.emit(refresh_points)
	# 收集保留条目（已完成未领取）与在场 id（重抽排除全部在场 id：
	# 既防抽回刚换下的任务，也防与保留任务重号覆盖其进度）
	var kept: Dictionary = {}
	var exclude: Array[StringName] = []
	for id in missions:
		if is_mission_done(id) and not is_mission_claimed(id):
			kept[id] = missions[id]
		exclude.append(id)
	var drawn := _task_pool.draw(MISSION_SLOTS - kept.size(), exclude)
	missions.clear()
	for id in kept:
		missions[id] = kept[id]
	for def in drawn:
		missions[def["id"]] = {"progress": 0, "claimed": false, "goal": int(def["goal"])}
	return true


## 选择天赋路线：该线两个 buff 的层数合并到所选 buff，另一个锁定不进奖励池。
## line/buff 非法或该线没有任何层数时返回 false。
func choose_route(line: StringName, buff_id: StringName) -> bool:
	if not ROUTE_LINES.has(line):
		return false
	var options: Array = ROUTE_LINES[line]
	if buff_id not in options:
		return false
	var other: StringName = options[0] if options[1] == buff_id else options[1]
	var total := buff_count(buff_id) + buff_count(other)
	if total <= 0:
		return false
	buffs[buff_id] = total
	buffs.erase(other)
	chosen_routes[line] = buff_id
	locked_routes[line] = other
	route_chosen.emit(line, buff_id)
	buffs_changed.emit()
	return true


## 奖励池抽取时排除锁定 buff
func is_buff_locked(buff_id: StringName) -> bool:
	return buff_id in locked_routes.values()


# ---------------- 用户会话（2026-08-04 账户系统） ----------------


## 登录已有用户：载入其设置/最高分并即时生效（locale 即时 set_locale——B7-11）
func login_user(name: String) -> void:
	if not _user_db.user_exists(name):
		return
	current_user = name
	_user_db.record_login(name)
	_load_session_settings()
	TranslationServer.set_locale(locale)
	apply_key_bindings()
	_apply_window_size()
	_invalidate_view_rect_cache()


## 游客进入：设置仅内存、不存档、不写统计（B7-8）；保留当前内存值（启动 profile 值视作游客会话）
func login_guest() -> void:
	current_user = "Guest"


## 退出：登录用户落盘设置；游客丢弃（内存）；复位未登录
func logout_user() -> void:
	if current_user != "" and current_user != "Guest":
		save_profile()
	current_user = ""


func is_guest() -> bool:
	return current_user == "Guest"


## 当前会话存档路径：登录用户 = 每用户文件；未登录 = 旧单文件；游客无路径（不存档）
func _save_path_for_current() -> String:
	if current_user == "":
		return SAVE_PATH
	if is_guest():
		return ""
	return _user_db.savefile_for_user(current_user)


## 载入当前会话档案：登录用户 → user_db settings + 统计；游客/未登录 → 保留内存（游客不落盘）
func _load_session_settings() -> void:
	if current_user == "" or is_guest():
		return
	_apply_settings_dict(_user_db.get_user_settings(current_user))
	high_score = int(_user_db.get_user_data(current_user).get("high_score", 0))


## profile.json 退役迁移（B5）：启动时存在旧 profile 且用户表为空 → 缓存待首个注册用户合并
func _maybe_migrate_legacy_profile() -> void:
	if not _pending_legacy_profile.is_empty():
		return
	if _save_manager.exists(PROFILE_PATH) and _user_db.list_usernames().is_empty():
		var parsed := _save_manager.load(PROFILE_PATH)
		if not parsed.is_empty():
			_pending_legacy_profile = parsed


## 注册用户（转发 user_db.create_user）；成功后合并旧 profile 迁移数据并删除 profile.json（B5）
func create_user(name: String, password: String) -> bool:
	if not _user_db.create_user(name, password):
		return false
	if not _pending_legacy_profile.is_empty():
		var legacy := _pending_legacy_profile.duplicate()
		_pending_legacy_profile = {}
		_user_db.update_high_score(name, int(save_num(legacy.get("high_score", 0), 0.0)))
		legacy.erase("high_score")
		legacy.erase("version")
		legacy.erase("highscores")
		_user_db.update_user_settings(name, legacy)
		_save_manager.delete(PROFILE_PATH)
	return true


## 用户数据库转发（A2 组合服务；供 welcome 登录面板使用）
func verify_user(name: String, password: String) -> bool:
	return _user_db.verify_user(name, password)


func user_exists(name: String) -> bool:
	return _user_db.user_exists(name)


func list_usernames() -> Array[String]:
	return _user_db.list_usernames()


func get_last_login_user() -> String:
	return _user_db.get_last_login_user()


func delete_user(name: String, password: String) -> bool:
	return _user_db.delete_user(name, password)


func get_leaderboard() -> Array:
	return _user_db.get_leaderboard()


func get_user_settings(name: String) -> Dictionary:
	return _user_db.get_user_settings(name)


func update_user_settings(name: String, settings: Dictionary) -> void:
	_user_db.update_user_settings(name, settings)


func get_user_data(name: String) -> Dictionary:
	return _user_db.get_user_data(name)


func user_db_savefile_for(name: String) -> String:
	return _user_db.savefile_for_user(name)


# ---------------- 对局存档（登录用户 = user://savegame_<user>_<hash12>.json；游客不存档） ----------------


func save_run(fuel: float, elapsed: float) -> void:
	if is_guest():
		return  # 游客不存档（B7-8）
	var path := _save_path_for_current()
	if path == "":
		return
	var data := {
		"version": PERSIST_VERSION,
		"score": score,
		"kills": kills,
		"health": health,
		"fuel": fuel,
		"boss_kills": boss_kills,
		"difficulty_multiplier": difficulty_multiplier,
		"buffs": buffs.duplicate(),
		"elapsed": elapsed,
		"rp": rp,
		"refresh_points": refresh_points,
		"missions": missions.duplicate(true),
		"chosen_routes": chosen_routes.duplicate(),
		"locked_routes": locked_routes.duplicate(),
		"ctrl_toggle_mode": ctrl_toggle_mode,
		"shift_toggle_mode": shift_toggle_mode,
	}
	if current_user != "":
		data["username"] = current_user
	# A2 阶段 2：文件 IO 委托 SaveManager
	_save_manager.save(path, data)


func has_save() -> bool:
	if is_guest():
		return false
	return _save_manager.exists(_save_path_for_current())


func load_run_data() -> Dictionary:
	save_corrupt = false
	var path := _save_path_for_current()
	if path == "" or not _save_manager.exists(path):
		return {}
	var data := _save_manager.load(path)
	if _save_manager.last_was_corrupt:
		# 损坏存档已由 SaveManager 隔离备份，按无存档处理（不留死路径）
		save_corrupt = true
	if current_user != "" and String(data.get("username", "")) != current_user:
		# B5 读档校验：档主不匹配（手改/旧匿名档）→ 隔离备份按无存档处理
		_save_manager.quarantine(path)
		save_corrupt = true
		return {}
	return data


## 存档数值字段安全读取：手改存档的非法类型（字符串/数组/字典等）回默认值
func save_num(v: Variant, default: float) -> float:
	return _save_manager.sanitize_num(v, default)


## C16 修复：布尔字段安全读取——仅接受真 bool（GDScript 的 bool("false") 为 true，
## 手改存档写字符串 "false"/"0" 会被误读为开；与 save_num 同款判型回退）
func save_bool(v: Variant, default: bool) -> bool:
	return v if v is bool else default


func apply_run_save(data: Dictionary) -> void:
	# 逐字段判型：语法合法但结构非法的存档（手改）不崩，异常字段回默认值
	score = int(save_num(data.get("score", 0), 0.0))
	kills = int(save_num(data.get("kills", 0), 0.0))
	boss_kills = int(save_num(data.get("boss_kills", 0), 0.0))
	difficulty_multiplier = save_num(data.get("difficulty_multiplier", 1.0), 1.0)
	buffs.clear()
	var saved_buffs: Variant = data.get("buffs", {})
	if saved_buffs is Dictionary:
		for key in saved_buffs.keys():
			if saved_buffs[key] is int or saved_buffs[key] is float:
				# G013：层数钳制 ≥0（手改存档负层数会破坏 buff_count 逻辑；超大值属手改作弊。
				# 注：add_buff 本身无 max_stacks 钳制——上限约束在 buff_select 选取侧检查
				# （buffs.<id>.max_stacks），此处仅保下限防负层数，不改存档恢复行为）
				buffs[StringName(key)] = maxi(int(saved_buffs[key]), 0)
	buffs_changed.emit()
	# 血量在 buffs 恢复之后再处理（max_health() 依赖 extra_life 层数）
	# v1（3 命制 lives）存档不回迁血量，按满血开；v2 起读 health
	if int(save_num(data.get("version", 1), 1.0)) >= 2:
		health = clampf(save_num(data.get("health", max_health()), max_health()), 0.0, max_health())
	else:
		health = max_health()
	run_time = save_num(data.get("elapsed", 0.0), 0.0)
	# 难度乘数按曲线从 boss_kills + run_time 重算（旧档的 difficulty_multiplier 字段仅作读入兼容）
	_recompute_difficulty()
	rp = int(save_num(data.get("rp", 0), 0.0))
	# 任务轮换：刷新点数随存档往返（手改负值钳制 ≥0）
	refresh_points = maxi(int(save_num(data.get("refresh_points", 0), 0.0)), 0)
	refresh_points_changed.emit(refresh_points)
	_init_missions()
	# 任务轮换：先清空初始手牌再恢复存档任务——存档集合可能含池内非手牌 id
	# （如 kill_15），不清空会使初始手牌未在存档中的 id（survive_180/boss_1）残留
	missions.clear()
	var saved_missions: Variant = data.get("missions", {})
	if saved_missions is Dictionary:
		for key in saved_missions.keys():
			var id := StringName(key)
			# 任务轮换：恢复条件从「初始手牌包含」放宽为「id 属于任务池」——
			# 轮换后的任务（如 kill_15）不在初始手牌，必须能随存档恢复
			if _mission_def(id).is_empty() or not saved_missions[key] is Dictionary:
				continue
			var m: Dictionary = saved_missions[key]
			var claimed: Variant = m.get("claimed", false)
			# H18（健壮性审核）：恢复保留 goal 键——整体替换会丢 goal 致
			# mission_completed 判定 progress >= 0 恒真而永久哑火（潜伏）
			missions[id] = {
				"progress": int(save_num(m.get("progress", 0), 0.0)),
				"claimed": claimed if claimed is bool else false,
				"goal": int(m.get("goal", mission_goal(id))),
			}
	chosen_routes.clear()
	var saved_chosen: Variant = data.get("chosen_routes", {})
	if saved_chosen is Dictionary:
		for key in saved_chosen.keys():
			if saved_chosen[key] is String or saved_chosen[key] is StringName:
				chosen_routes[StringName(key)] = StringName(saved_chosen[key])
	locked_routes.clear()
	var saved_locked: Variant = data.get("locked_routes", {})
	if saved_locked is Dictionary:
		for key in saved_locked.keys():
			if saved_locked[key] is String or saved_locked[key] is StringName:
				locked_routes[StringName(key)] = StringName(saved_locked[key])
	# 设置项随存档往返（旧存档无字段时保留当前值）
	ctrl_toggle_mode = save_bool(data.get("ctrl_toggle_mode", ctrl_toggle_mode), ctrl_toggle_mode)
	shift_toggle_mode = save_bool(data.get("shift_toggle_mode", shift_toggle_mode), shift_toggle_mode)
	# 里程碑曲线：恢复到大于当前分数的第一档
	# A 审计：原 while 无上界——若 milestone_base 被手改为非单调或 cycle_mult 极小
	# （钳 ≥0.01），阈值增量收敛至有限值，大分数时 while 永不退出（挂死）。
	# 迭代上限 10000 足以覆盖任何合理分数（cycle_mult=1.01 时 10000 档阈值已超百亿）
	_milestone_count = 0
	var ms_cap := 10000
	while _milestone_count < ms_cap and milestone_threshold(_milestone_count) <= score:
		_milestone_count += 1
	_next_milestone = milestone_threshold(_milestone_count)
	score_changed.emit(score)
	health_changed.emit(health)
	difficulty_changed.emit(difficulty_multiplier)
	rp_changed.emit(rp)


func delete_save() -> void:
	var path := _save_path_for_current()
	if path != "":
		_save_manager.delete(path)


# ---------------- 局外档案（登录用户 = user_db settings；游客仅内存；未登录 = 旧 profile.json 兼容路径） ----------------


## 局外档案：最高分 + 难度档位 + 设置项（旧版 talents/talent_points 字段读取时忽略；
## 旧档案缺少新字段时保留当前内存值，保证兼容；损坏文件隔离备份后按默认值继续）
func load_profile() -> void:
	profile_corrupt = false
	if current_user != "":
		return  # 会话模式下档案由登录流程管理（_load_session_settings）
	var parsed := _save_manager.load(PROFILE_PATH)
	if _save_manager.last_was_corrupt:
		profile_corrupt = true
		return
	if parsed.is_empty():
		return
	high_score = int(save_num(parsed.get("high_score", 0), 0.0))  # save_num 判型：手改档案字符串等非法类型回默认
	_apply_settings_dict(parsed)
	# P0-3：高分榜判型加载（手改档案的元素级守卫，对齐 E11）——非法条目跳过、排序截断
	highscores.clear()
	var saved_highscores: Variant = parsed.get("highscores", [])
	if saved_highscores is Array:
		for entry: Variant in saved_highscores:
			if not entry is Dictionary:
				continue
			var s: Variant = entry.get("score", 0)
			if not (s is int or s is float):
				continue
			highscores.append({"score": int(s), "date": int(save_num(entry.get("date", 0), 0.0))})  # E11 同款：date 走 save_num 判型
		highscores.sort_custom(func(a: Dictionary, b: Dictionary) -> bool: return int(a["score"]) > int(b["score"]))
		if highscores.size() > HIGHSCORE_LIMIT:
			highscores.resize(HIGHSCORE_LIMIT)


## 设置字段应用（profile.json 与 user_db settings 共用；含键位/窗口/视图缓存副作用，对齐原 load_profile）
func _apply_settings_dict(data: Dictionary) -> void:
	tutorial_done = save_bool(data.get("tutorial_done", tutorial_done), tutorial_done)
	# E10：locale 加载经 zh/en 白名单守卫（对齐 set_locale）——手改非法值保持当前语言，
	# 避免 locale 变量与 TranslationServer 状态不一致
	var saved_locale := str(data.get("locale", locale))
	if saved_locale == "zh" or saved_locale == "en":
		locale = saved_locale
	# C02 修复：key_bindings 手改档案的类型守卫——非 Dictionary / 子值非 Array 时跳过该字段，
	# 不崩溃、不提前返回（其余字段照常加载）；typed 赋值在运行期校验失败会抛错并丢后续字段。
	key_bindings.clear()
	var saved_keys: Variant = data.get("key_bindings", {})
	if saved_keys is Dictionary:
		for a in saved_keys.keys():
			var raw: Variant = saved_keys[a]
			if not raw is Array:
				continue
			var keys: Array[int] = []
			for k: Variant in raw:
				# E11：元素级判型（C02 外层守卫的补全）——手改字符串 keycode 直接跳过，
				# 不再 int() 转换错误刷屏（不崩溃但不干净）
				if (not k is int) and (not k is float):
					continue
				keys.append(int(k))
			key_bindings[StringName(a)] = keys
	var saved_difficulty := StringName(data.get("difficulty", ""))
	if DIFFICULTY_DEFS.has(saved_difficulty):
		difficulty = saved_difficulty
	ctrl_toggle_mode = save_bool(data.get("ctrl_toggle_mode", ctrl_toggle_mode), ctrl_toggle_mode)
	shift_toggle_mode = save_bool(data.get("shift_toggle_mode", shift_toggle_mode), shift_toggle_mode)
	var saved_zoom := StringName(data.get("view_zoom", ""))
	if VIEW_ZOOM_LEVELS.has(saved_zoom):
		view_zoom = saved_zoom
		_view_zoom_factor = VIEW_ZOOM_LEVELS[saved_zoom]
		_invalidate_view_rect_cache()
	var saved_window := StringName(data.get("window_size", ""))
	if WINDOW_SIZE_LEVELS.has(saved_window):
		window_size = saved_window
		_apply_window_size()
	var saved_aim := StringName(data.get("aim_assist", ""))
	if AIM_ASSIST_ORDER.has(saved_aim):
		aim_assist_level = saved_aim
	reduce_flash = save_bool(data.get("reduce_flash", reduce_flash), reduce_flash)
	mouse_lock = save_bool(data.get("mouse_lock", mouse_lock), mouse_lock)
	# P0-1 手柄设置：灵敏度默认取 balance player.aim_assist.joy_speed，死区默认 0.5
	var joy_speed: Variant = data.get("joy_aim_speed", cfg("player.aim_assist.joy_speed", joy_aim_speed))
	if joy_speed is float or joy_speed is int:
		joy_aim_speed = clampf(float(joy_speed), 200.0, 4000.0)
	var joy_dz: Variant = data.get("joy_deadzone", joy_deadzone)
	if joy_dz is float or joy_dz is int:
		joy_deadzone = clampf(float(joy_dz), 0.05, 0.9)


## 当前设置字段收集（profile.json 与 user_db settings 共用；统计类字段不在此列）
func _collect_settings_dict() -> Dictionary:
	return {
		"tutorial_done": tutorial_done,
		"key_bindings": key_bindings,
		"locale": locale,
		"difficulty": String(difficulty),
		"ctrl_toggle_mode": ctrl_toggle_mode,
		"shift_toggle_mode": shift_toggle_mode,
		"view_zoom": String(view_zoom),
		"window_size": String(window_size),
		"aim_assist": String(aim_assist_level),
		"reduce_flash": reduce_flash,
		"mouse_lock": mouse_lock,
		"joy_aim_speed": joy_aim_speed,
		"joy_deadzone": joy_deadzone,
	}


func save_profile() -> void:
	if is_guest():
		return  # 游客设置仅内存（B7-8）
	if current_user != "":
		_user_db.update_user_settings(current_user, _collect_settings_dict())
		return
	var data := _collect_settings_dict()
	data["version"] = PERSIST_VERSION
	data["high_score"] = high_score
	data["highscores"] = highscores
	_save_manager.save(PROFILE_PATH, data)


## 记录最高分，破纪录返回 true（登录用户写 user_db；游客仅内存；未登录写旧 profile.json）
func record_score() -> bool:
	if score > high_score:
		high_score = score
		if is_guest():
			return true
		if current_user != "":
			_user_db.update_high_score(current_user, score)
		else:
			save_profile()
		return true
	return false


## 提交本局分数入本地榜，返回名次（1-based；未上榜返回 0）。
## 同分新条目排后（先到先得）；超出上限的分数不入榜。登录/游客走 user_db 排行榜（游客以 "Guest" 提交，B7-8）。
func submit_highscore(run_score: int) -> int:
	if current_user != "":
		return _user_db.submit_score(current_user, run_score)
	if run_score <= 0:
		return 0
	var rank := 1
	for e in highscores:
		if int(e["score"]) >= run_score:
			rank += 1
		else:
			break
	if rank > HIGHSCORE_LIMIT:
		return 0
	highscores.insert(rank - 1, {"score": run_score, "date": int(Time.get_unix_time_from_system())})
	if highscores.size() > HIGHSCORE_LIMIT:
		highscores.resize(HIGHSCORE_LIMIT)
	save_profile()
	return rank


## 榜单文本（供结算页/开始页展示）："1. 12345\n2. 9876..."；空榜返回空串
func highscores_text(limit: int = 5) -> String:
	if current_user != "":
		var board := _user_db.get_leaderboard()
		if board.is_empty():
			return ""
		var lines: Array[String] = []
		for i in mini(limit, board.size()):
			lines.append("%d. %d" % [i + 1, int(board[i]["score"])])
		return "\n".join(lines)
	if highscores.is_empty():
		return ""
	var lines: Array[String] = []
	for i in mini(limit, highscores.size()):
		lines.append("%d. %d" % [i + 1, int(highscores[i]["score"])])
	return "\n".join(lines)
