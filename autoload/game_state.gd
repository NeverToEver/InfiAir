extends Node
## 全局状态与信号总线：分数、击杀、生命、难度乘数、已选 buff。

signal score_changed(new_score: int)
signal health_changed(new_health: float)
signal difficulty_changed(new_multiplier: float)
signal difficulty_selected(difficulty: StringName)
signal milestone_reached(score: int)
signal player_died
## 玩家实际结算受击（无敌/闪避/单帧守卫未结算不发）：Meta HUD 受击层数据源
signal player_damaged(amount: float, from_pos: Vector2)
signal screen_shake(strength: float)
signal rp_changed(new_rp: int)
signal mission_completed(id: StringName)
signal route_chosen(line: StringName, buff_id: StringName)
signal key_bindings_changed
signal locale_changed
signal view_zoom_changed(factor: float)
signal window_size_changed(level: StringName)
signal aim_assist_changed(level: StringName)
signal reduce_flash_changed(enabled: bool)
## buff 层数任何变动（选取/路线合并/存档恢复/重开清空）后发出，驱动外观刷新
signal buffs_changed

## 难度档位表（开始面板选择，profile 持久化；对齐原作 settings.py DIFFICULTY_SETTINGS）
## hp/speed/spawn 为敌机数值与刷怪间隔倍率；score 为分数倍率（add_score 统一乘算）；
## spread_cap 为 spread 弹种敌机同屏上限；milestone 为里程碑阈值倍率
## （原作阈值与分数同倍 ×1/×2/×3，此处按设计取 ×1/×1/×1.5，避免高难 Buff 节奏过稀）；
## regen_delay/regen_rate 为被动回血（对齐原作 settings.py HEALTH_REGEN）：
## 距上次受伤 regen_delay 秒起每秒回 regen_rate HP（原作延迟不重置为疑似 bug，本版受伤即重置）。
var DIFFICULTY_DEFS: Dictionary = {
	&"easy": {
		"label": "易", "hp": 0.75, "speed": 0.85, "spawn": 1.25,
		"score": 1, "spread_cap": 1, "milestone": 1.0,
		"regen_delay": 3.0, "regen_rate": 4.0,
	},
	&"medium": {
		"label": "中", "hp": 1.0, "speed": 1.0, "spawn": 1.0,
		"score": 2, "spread_cap": 2, "milestone": 1.0,
		"regen_delay": 4.0, "regen_rate": 2.0,
	},
	&"hard": {
		"label": "难", "hp": 1.5, "speed": 1.2, "spawn": 0.8,
		"score": 3, "spread_cap": 3, "milestone": 1.5,
		"regen_delay": 5.0, "regen_rate": 0.67,
	},
}
const DIFFICULTY_ORDER: Array[StringName] = [&"easy", &"medium", &"hard"]

# 里程碑阈值曲线（对齐原作 constants.py GameBalanceConstants 算法）：
# 首循环 8 档基础阈值，之后每循环的档差按 ×1.35^cycle 放大（阈值单调不回退）。
const MILESTONE_BASE: Array[int] = [3000, 8000, 15000, 25000, 40000, 55000, 70000, 80000]
const MILESTONE_CYCLE_MULT := 1.35

# ---------------- 全局数值配置中心 ----------------
# data/balance.json 启动时一次加载进 _balance；缺失/损坏时全部回退脚本默认值。
# 访问统一走 GameState.cfg("分层.路径", 默认值)；热路径在各自 _ready 缓存进成员变量。
const BALANCE_PATH := "res://data/balance.json"

var _balance: Dictionary = {}
## 生效的里程碑表（默认值见 const，可被 balance.json 覆盖）
var milestone_base: Array = MILESTONE_BASE.duplicate()
var milestone_cycle_mult: float = MILESTONE_CYCLE_MULT
## 全局机体尺寸缩放（balance.json 顶层 world_scale；1/3 = 当前默认观感）。
## 机体尺寸族数值（贴图 scale/碰撞 radius/机体偏移/随机体特效比例）在 json/tscn/脚本回退中
## 一律存设计值（1.0 基准），实体在 _ready()/setup() 统一乘本系数后应用；游戏性范围族不乘。
var world_scale: float = 0.3333333333333333


func _load_balance() -> void:
	_balance = {}
	if not FileAccess.file_exists(BALANCE_PATH):
		return
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(BALANCE_PATH))
	if parsed is Dictionary:
		_balance = parsed


## 统一配置访问：路径如 "player.fuel.drain"。缺键/类型不符回退 default。
func cfg(path: String, default: Variant) -> Variant:
	var node: Variant = _balance
	for key in path.split("."):
		if node is Dictionary and node.has(key):
			node = node[key]
		else:
			return default
	# 数值宽容：JSON 整数/浮点互通
	if default is int or default is float:
		if node is int or node is float:
			return node
		return default
	if node is Array and default is Array:
		return node
	if typeof(node) == typeof(default):
		return node
	return default


func _apply_balance() -> void:
	world_scale = float(cfg("world_scale", world_scale))
	milestone_base = cfg("milestones.base", MILESTONE_BASE.duplicate())
	milestone_cycle_mult = cfg("milestones.cycle_mult", MILESTONE_CYCLE_MULT)
	_prog_per_boss_kill = float(cfg("progression.per_boss_kill", 0.5))
	_prog_per_ten_minutes = float(cfg("progression.per_ten_minutes", 1.0))
	_prog_time_step_seconds = float(cfg("progression.time_step_seconds", 30.0))
	var diff: Variant = cfg("difficulty", {})
	if diff is Dictionary and not diff.is_empty():
		DIFFICULTY_DEFS = diff

# RP（征用点数）经济：对齐原作 RequisitionConstants
const RP_BOSS_KILL := 5
const RP_MISSION_REWARD := 3
const RP_REPAIR_COST := 2
const RP_RECHARGE_COST := 2

# 常驻基地任务（对齐原作 base_talent_console 三任务）
const MISSION_DEFS: Array[Dictionary] = [
	{"id": &"kill_5", "name": "战场清扫", "desc": "击杀 5 个敌人", "goal": 5},
	{"id": &"survive_180", "name": "战场生存", "desc": "存活 180 秒", "goal": 180},
	{"id": &"boss_1", "name": "主宰之战", "desc": "击杀 1 个 Boss", "goal": 1},
]

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

var high_score: int = 0
var tutorial_done: bool = false
## 欢迎页是否已展示过（profile 持久化：仅装机后首次启动显示欢迎页）
var welcome_seen: bool = false

var _sfx_players: Array[AudioStreamPlayer] = []
var _sfx_index: int = 0

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
var _prog_per_boss_kill: float = 0.5
var _prog_per_ten_minutes: float = 1.0
var _prog_time_step_seconds: float = 30.0
## 已计入难度乘数的时间档位（按 time_step_seconds 量化步进，避免连续漂移）
var _difficulty_time_step: int = 0

## 启动计时基准（autoload 最早生命周期点；--startup-time 时由 main 打印分段耗时）
var boot_ticks_msec: int = 0
## 启动/读档时检测到损坏并已隔离备份（开始面板据此提示；读取正常后置回 false）
var save_corrupt: bool = false
var profile_corrupt: bool = false


func _enter_tree() -> void:
	boot_ticks_msec = Time.get_ticks_msec()

## 实体注册表（热路径缓存，避免每帧 get_nodes_in_group 分配）：
## enemy/boss 在 _ready/_exit_tree 时注册/注销，player 单独缓存引用。
var enemies: Array[Node] = []
var player_ref: Node2D = null
## 玩家受击 Hitbox（player._ready/_exit_tree 维护；敌机/Boss 撞击逐帧轮询用）
var player_hitbox: Area2D = null
## 子弹对象池实例（由 bullet_pool.gd 在 _ready 时登记）
var bullet_pool: BulletPool = null
## 敌机对象池实例（由 enemy_pool.gd 在 _ready 时登记）
var enemy_pool: EnemyPool = null
## 辅助瞄准框覆盖层实例（由 aim_frame_layer.gd 在 _ready 时登记；player._fire 查询框内标记敌）
var aim_frame_layer: AimFrameLayer = null


func register_enemy(node: Node) -> void:
	if not enemies.has(node):
		enemies.append(node)


func unregister_enemy(node: Node) -> void:
	enemies.erase(node)


func _ready() -> void:
	_load_balance()
	_apply_balance()
	# 常驻音效播放器池：播放节点被 queue_free 时音效也不会中断
	for i in SFX_POOL_SIZE:
		var p := AudioStreamPlayer.new()
		add_child(p)
		_sfx_players.append(p)
	_capture_default_bindings()
	_init_missions()
	load_profile()
	_apply_window_size()  # 无 profile 时 load_profile 不会应用窗口尺寸，这里补一次默认档位
	var tr_zh := load("res://data/translations.zh.translation") as Translation
	var tr_en := load("res://data/translations.en.translation") as Translation
	if tr_zh != null:
		TranslationServer.add_translation(tr_zh)
	if tr_en != null:
		TranslationServer.add_translation(tr_en)
	TranslationServer.set_locale(locale)
	_apply_key_bindings()
	_next_milestone = milestone_threshold(0)


# 暂停（Buff/结算 UI）时不计存活时间
func _process(delta: float) -> void:
	run_time += delta
	_set_mission_progress(&"survive_180", int(run_time))
	# 时间轴难度档：跨过量化步进边界时重算难度乘数（去硬顶曲线的时间分量）
	if int(floorf(run_time / _prog_time_step_seconds)) != _difficulty_time_step:
		if _recompute_difficulty():
			difficulty_changed.emit(difficulty_multiplier)


func play_sfx(stream: AudioStream, volume_db: float = 0.0, pitch_scale: float = 1.0) -> void:
	# headless dummy 音频驱动不混音：一次性 WAV 播放实例在退出时既不自然结束、
	# stop() 也不释放，必报 ObjectDB 泄漏噪音；无头路径直接不创建播放实例。
	if DisplayServer.get_name() == "headless":
		return
	var p := _sfx_players[_sfx_index]
	_sfx_index = (_sfx_index + 1) % _sfx_players.size()
	p.stream = stream
	p.volume_db = volume_db
	p.pitch_scale = pitch_scale  # 池化复用：每次播放都显式置位，避免上次变调残留
	p.play()


## 退出前停止所有仍在播放的音效：带播未停时 AudioStreamPlayback 会在退出时泄漏
func stop_all_sfx() -> void:
	for p in _sfx_players:
		p.stop()
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
	chosen_routes.clear()
	locked_routes.clear()
	_milestone_count = 0
	_next_milestone = milestone_threshold(0)
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
	difficulty_selected.emit(difficulty)
	save_profile()


func difficulty_label() -> String:
	return tr("DIFF_" + String(difficulty).to_upper())


func score_multiplier() -> int:
	return int(DIFFICULTY_DEFS[difficulty]["score"])


func enemy_hp_multiplier() -> float:
	return float(DIFFICULTY_DEFS[difficulty]["hp"])


func enemy_speed_multiplier() -> float:
	return float(DIFFICULTY_DEFS[difficulty]["speed"])


## 敌方 HP 对局进程 ramp：×(1 + hp_ramp_factor × (难度乘数 − 1))，随 Boss 击杀线性成长
func enemy_hp_ramp() -> float:
	return 1.0 + float(cfg("enemies.hp_ramp_factor", 0.12)) * (difficulty_multiplier - 1.0)


## 敌方伤害对局进程 ramp：×(1 + damage_ramp_factor × (难度乘数 − 1))，
## 统一作用于全部敌方伤害源（敌弹/Boss 弹/撞体/编队炸弹；2026-07-29 无限段修订）
func enemy_damage_ramp() -> float:
	return 1.0 + float(cfg("enemies.damage_ramp_factor", 0.08)) * (difficulty_multiplier - 1.0)


func spawn_interval_multiplier() -> float:
	return float(DIFFICULTY_DEFS[difficulty]["spawn"])


## spread 弹种敌机同屏上限（easy 1 / medium 2 / hard 3）
func spread_enemy_cap() -> int:
	return int(DIFFICULTY_DEFS[difficulty]["spread_cap"])


## 被动回血：距上次受伤 regen_delay 秒起每秒回 regen_rate HP（对齐原作 HEALTH_REGEN）
func passive_regen_delay() -> float:
	return float(DIFFICULTY_DEFS[difficulty]["regen_delay"])


func passive_regen_rate() -> float:
	return float(DIFFICULTY_DEFS[difficulty]["regen_rate"])


# ---------------- 里程碑阈值曲线 ----------------

## 第 index 次（0 起）里程碑的分数阈值：8 档基础阈值循环，档差按 ×1.35^cycle 增长，
## 再乘难度阈值倍率（easy ×1 / medium ×1 / hard ×1.5）。
func milestone_threshold(index: int) -> int:
	var n := milestone_base.size()
	var cycle := maxi(index, 0) / n
	var step := maxi(index, 0) % n
	var total := 0.0
	for c in cycle + 1:
		var mult := pow(milestone_cycle_mult, c)
		var last_step := step if c == cycle else n - 1
		var prev := 0.0
		for i in last_step + 1:
			total += (milestone_base[i] - prev) * mult
			prev = milestone_base[i]
	return int(roundf(total * float(DIFFICULTY_DEFS[difficulty]["milestone"])))


## 测试钩子：直接设定下一个里程碑阈值（不动曲线计数，保证测试确定性）
func _set_milestone_override(threshold: int) -> void:
	_next_milestone = threshold


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
var camera_ref: Camera2D = null
## 生效 zoom 倍率缓存（set_view_zoom/load_profile 同步；热路径免查表，须与 small 档一致）
var _view_zoom_factor: float = 1.0


## 切换视角档位（非法/同档忽略），持久化到 profile 并广播
func set_view_zoom(level: StringName) -> void:
	if not VIEW_ZOOM_LEVELS.has(level) or level == view_zoom:
		return
	view_zoom = level
	_view_zoom_factor = VIEW_ZOOM_LEVELS[level]
	save_profile()
	view_zoom_changed.emit(_view_zoom_factor)


func view_zoom_factor() -> float:
	return _view_zoom_factor


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


## 当前可见世界区域（相机未注册时以 (960,540) 为心），margin 向外扩张。
## 屏幕边缘钳制 / 出屏销毁 / 刷怪位置统一以此为准；zoom=1 时即全屏 1920×1080。
func view_world_rect(margin: float = 0.0) -> Rect2:
	var center := Vector2(960.0, 540.0)
	if camera_ref != null and is_instance_valid(camera_ref):
		center = camera_ref.global_position
	var size := Vector2(1920.0, 1080.0)
	var viewport := get_viewport()
	if viewport != null:
		size = viewport.get_visible_rect().size
	size /= _view_zoom_factor
	return Rect2(center - size * 0.5, size).grow(margin)


func add_kill() -> void:
	kills += 1
	_set_mission_progress(&"kill_5", kills)


func add_boss_kill(score_scale: float = 1.0) -> void:
	boss_kills += 1
	add_score(int(500.0 * score_scale))
	add_rp(RP_BOSS_KILL)
	_set_mission_progress(&"boss_1", boss_kills)
	if _recompute_difficulty():
		difficulty_changed.emit(difficulty_multiplier)


## 难度乘数对局进程曲线（2026-07-29 无限段修订，D1=必死曲线，docs/ENDLESS_BALANCE_PLAN.md）：
## 1 + per_boss_kill×Boss击杀 + 时间轴累进（每 time_step_seconds 量化一档，每 10 分钟 +per_ten_minutes）。
## 线性无封顶：敌方 HP/伤害 ramp 随之无限增长，最终超过玩家固定成长上限。
## 返回乘数是否变化；变化时由调用方广播 difficulty_changed（apply_run_save 统一在末尾广播）。
func _recompute_difficulty() -> bool:
	var step := int(floorf(run_time / _prog_time_step_seconds))
	var new_mult := (
		1.0
		+ _prog_per_boss_kill * boss_kills
		+ step * _prog_time_step_seconds / 600.0 * _prog_per_ten_minutes
	)
	_difficulty_time_step = step
	if is_equal_approx(new_mult, difficulty_multiplier):
		return false
	difficulty_multiplier = new_mult
	return true


## 生命上限：基础 100 + extra_life 每层 +50（对齐原作 EXTRA_LIFE_BONUS_HP）
func max_health() -> float:
	return cfg("player.max_health", 100.0) + cfg("buffs.extra_life.max_hp_bonus", 50) * buff_count(&"extra_life")


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


func try_lifesteal() -> void:
	if buff_count(&"lifesteal") <= 0:
		return
	var frame := Engine.get_physics_frames()
	if frame == _lifesteal_frame:
		return
	_lifesteal_frame = frame
	heal(maxi(1, int(max_health() * cfg("buffs.lifesteal.max_hp_fraction", 0.1))))


func buff_count(id: StringName) -> int:
	return buffs.get(id, 0)


func add_buff(id: StringName) -> void:
	buffs[id] = buff_count(id) + 1
	buffs_changed.emit()


# ---------------- 可改键系统 ----------------

const REBINDABLE_ACTIONS: Array[StringName] = [
	&"move_up", &"move_down", &"move_left", &"move_right",
	&"boost", &"fine_move", &"dash", &"dock", &"homecoming", &"give_up", &"buff_panel",
]
const ACTION_LABELS: Dictionary = {
	&"move_up": "上移", &"move_down": "下移", &"move_left": "左移", &"move_right": "右移",
	&"boost": "加速", &"fine_move": "微调", &"dash": "相位冲刺",
	&"dock": "召唤母舰", &"homecoming": "返航", &"give_up": "放弃出击", &"buff_panel": "增益面板",
}

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
			var k: int = ev.keycode if ev.keycode != 0 else ev.physical_keycode
			out.append(k)
			if out.size() >= 2:
				break
	return out


## 用 key_bindings（含 profile 覆盖）刷新 InputMap
func _apply_key_bindings() -> void:
	for a in REBINDABLE_ACTIONS:
		InputMap.action_erase_events(a)
		for k: int in key_bindings.get(a, _default_bindings.get(a, [])):
			var ev := InputEventKey.new()
			ev.keycode = k
			InputMap.action_add_event(a, ev)


## 改键：清除该动作现有键设新键；冲突键从占用者移除（允许交换）
func rebind_action(action: StringName, keycode: int) -> bool:
	if action not in REBINDABLE_ACTIONS:
		return false
	for a in key_bindings.keys():
		if a != action and (key_bindings[a] as Array).has(keycode):
			(key_bindings[a] as Array).erase(keycode)
	key_bindings[action] = [keycode]
	_apply_key_bindings()
	save_profile()
	key_bindings_changed.emit()
	return true


func reset_key_bindings() -> void:
	key_bindings = _default_bindings.duplicate(true)
	_apply_key_bindings()
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
		missions[def["id"]] = {"progress": 0, "claimed": false}


func _set_mission_progress(id: StringName, value: int) -> void:
	if not missions.has(id):
		return
	var m: Dictionary = missions[id]
	var goal := mission_goal(id)
	var was_done: bool = int(m["progress"]) >= goal
	m["progress"] = value
	if not was_done and value >= goal:
		mission_completed.emit(id)


func mission_goal(id: StringName) -> int:
	for def in MISSION_DEFS:
		if def["id"] == id:
			return int(def["goal"])
	return 0


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


# ---------------- 对局存档（user://savegame.json） ----------------

func save_run(fuel: float, elapsed: float) -> void:
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
		"missions": missions.duplicate(true),
		"chosen_routes": chosen_routes.duplicate(),
		"locked_routes": locked_routes.duplicate(),
		"ctrl_toggle_mode": ctrl_toggle_mode,
		"shift_toggle_mode": shift_toggle_mode,
	}
	var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	if f == null:
		push_warning("InfiAir: 无法写入对局存档 %s（错误 %d）" % [SAVE_PATH, FileAccess.get_open_error()])
		return
	f.store_string(JSON.stringify(data))
	f.close()


func has_save() -> bool:
	return FileAccess.file_exists(SAVE_PATH)


func load_run_data() -> Dictionary:
	save_corrupt = false
	if not has_save():
		return {}
	var f := FileAccess.open(SAVE_PATH, FileAccess.READ)
	if f == null:
		return {}
	var text := f.get_as_text()
	f.close()
	# 用 JSON 实例解析（parse_string 会把损坏内容打成 ERROR 级日志，噪音大）
	var json := JSON.new()
	if json.parse(text) == OK and json.data is Dictionary:
		return json.data
	# 损坏存档：隔离备份后按无存档处理（否则「继续对局」每次点了都无反应，形成死路径）
	_quarantine(SAVE_PATH)
	save_corrupt = true
	return {}


## 损坏文件隔离：重命名为 <path>.corrupt（已有备份则先删），给玩家留排查余地
func _quarantine(path: String) -> void:
	var backup := path + ".corrupt"
	if FileAccess.file_exists(backup):
		DirAccess.remove_absolute(backup)
	var err := DirAccess.rename_absolute(path, backup)
	if err != OK:
		push_warning("InfiAir: 无法备份损坏文件 %s（错误 %d）" % [path, err])


## 存档数值字段安全读取：手改存档的非法类型（字符串/数组/字典等）回默认值
func save_num(v: Variant, default: float) -> float:
	return float(v) if v is int or v is float else default


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
				buffs[StringName(key)] = int(saved_buffs[key])
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
	_init_missions()
	var saved_missions: Variant = data.get("missions", {})
	if saved_missions is Dictionary:
		for key in saved_missions.keys():
			if missions.has(StringName(key)) and saved_missions[key] is Dictionary:
				var m: Dictionary = saved_missions[key]
				var claimed: Variant = m.get("claimed", false)
				missions[StringName(key)] = {
					"progress": int(save_num(m.get("progress", 0), 0.0)),
					"claimed": claimed if claimed is bool else false,
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
	ctrl_toggle_mode = bool(data.get("ctrl_toggle_mode", ctrl_toggle_mode))
	shift_toggle_mode = bool(data.get("shift_toggle_mode", shift_toggle_mode))
	# 里程碑曲线：恢复到大于当前分数的第一档
	_milestone_count = 0
	while milestone_threshold(_milestone_count) <= score:
		_milestone_count += 1
	_next_milestone = milestone_threshold(_milestone_count)
	score_changed.emit(score)
	health_changed.emit(health)
	difficulty_changed.emit(difficulty_multiplier)
	rp_changed.emit(rp)


func delete_save() -> void:
	if has_save():
		DirAccess.remove_absolute(SAVE_PATH)


# ---------------- 局外档案（user://profile.json） ----------------

## 局外档案：最高分 + 难度档位 + 设置项（旧版 talents/talent_points 字段读取时忽略；
## 旧档案缺少新字段时保留当前内存值，保证兼容；损坏文件隔离备份后按默认值继续）
func load_profile() -> void:
	profile_corrupt = false
	if not FileAccess.file_exists(PROFILE_PATH):
		return
	var json := JSON.new()
	if json.parse(FileAccess.get_file_as_string(PROFILE_PATH)) != OK or not json.data is Dictionary:
		_quarantine(PROFILE_PATH)
		profile_corrupt = true
		return
	var parsed: Dictionary = json.data
	high_score = int(parsed.get("high_score", 0))
	tutorial_done = bool(parsed.get("tutorial_done", false))
	welcome_seen = bool(parsed.get("welcome_seen", false))
	locale = str(parsed.get("locale", "zh"))
	key_bindings.clear()
	var saved_keys: Dictionary = parsed.get("key_bindings", {})
	for a in saved_keys.keys():
		var keys: Array[int] = []
		for k: Variant in saved_keys[a]:
			keys.append(int(k))
		key_bindings[StringName(a)] = keys
	var saved_difficulty := StringName(parsed.get("difficulty", ""))
	if DIFFICULTY_DEFS.has(saved_difficulty):
		difficulty = saved_difficulty
	ctrl_toggle_mode = bool(parsed.get("ctrl_toggle_mode", ctrl_toggle_mode))
	shift_toggle_mode = bool(parsed.get("shift_toggle_mode", shift_toggle_mode))
	var saved_zoom := StringName(parsed.get("view_zoom", ""))
	if VIEW_ZOOM_LEVELS.has(saved_zoom):
		view_zoom = saved_zoom
		_view_zoom_factor = VIEW_ZOOM_LEVELS[saved_zoom]
	var saved_window := StringName(parsed.get("window_size", ""))
	if WINDOW_SIZE_LEVELS.has(saved_window):
		window_size = saved_window
		_apply_window_size()
	var saved_aim := StringName(parsed.get("aim_assist", ""))
	if AIM_ASSIST_ORDER.has(saved_aim):
		aim_assist_level = saved_aim
	reduce_flash = bool(parsed.get("reduce_flash", reduce_flash))


func save_profile() -> void:
	var data := {
		"version": PERSIST_VERSION,
		"high_score": high_score,
		"tutorial_done": tutorial_done,
		"welcome_seen": welcome_seen,
		"key_bindings": key_bindings,
		"locale": locale,
		"difficulty": String(difficulty),
		"ctrl_toggle_mode": ctrl_toggle_mode,
		"shift_toggle_mode": shift_toggle_mode,
		"view_zoom": String(view_zoom),
		"window_size": String(window_size),
		"aim_assist": String(aim_assist_level),
		"reduce_flash": reduce_flash,
	}
	var f := FileAccess.open(PROFILE_PATH, FileAccess.WRITE)
	if f == null:
		push_warning("InfiAir: 无法写入档案 %s（错误 %d）" % [PROFILE_PATH, FileAccess.get_open_error()])
		return
	f.store_string(JSON.stringify(data))
	f.close()


## 记录最高分，破纪录返回 true
func record_score() -> bool:
	if score > high_score:
		high_score = score
		save_profile()
		return true
	return false
