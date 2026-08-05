class_name Enemy
extends Area2D
## 普通/精英敌机：straight / sine / zigzag / dive / spiral / noise / hover / aggressive。
## 弹种：single（单发瞄准）/ spread（五向扇形）/ laser（细长高亮快速弹）。
## 入场两阶段：先下降到锚点 anchor_y（hover_band 悬停带内），随后围绕锚点水平机动
## + 垂直小幅浮动（不再净下降）；悬停机动带个体随机相位（错开全波同相位的机械感），
## straight/hover 增加水平慢摇摆、spiral 绕转中心漂移，参数见 balance.json enemies 段；
## 出生 15s 寿命到期后向上或侧方加速离场（不给分、不计击杀）。
## 数值由 spawner 的机型配置表驱动（setup 传入 config Dictionary）。

signal died(enemy: Enemy)

var ENEMY_BULLET_SPEED := 420.0
var SPREAD_BULLET_SPEED := 340.0  # 扇形弹稍慢
var LASER_BULLET_SPEED := 720.0  # laser 简化表现：细长高亮快速弹
## 各弹种伤害（对齐原作 _ENEMY_BULLET_DAMAGE：single 12 / spread 10 / laser 20）
var BULLET_DAMAGE_SINGLE := 12
var BULLET_DAMAGE_SPREAD := 10
var BULLET_DAMAGE_LASER := 20
## 身体撞击伤害（对齐原作 ENEMY_COLLISION_DAMAGE=20；撞击后敌机不自毁继续飞）
var COLLISION_DAMAGE := 20
## 慢速力场：全局敌机移速 ×0.8（对齐原作 slow_factor；原作对普通敌机失效为疑似 bug，本版全生效）
var SLOW_FIELD_FACTOR := 0.8
var SPREAD_FAN_STEP := 0.314159  # 五向扇形步进角（18°）
var LIFETIME := 15.0  # 出生后寿命（对齐原作 900 帧@60fps）
var EXIT_ACCEL := 520.0  # 寿命离场加速度
var AGGR_CHASE_SPEED := 140.0  # aggressive 持续偏向玩家 x 的速度
var FIRE_INTERVAL := 2.2
## 悬停带：锚点 anchor_y 的取值范围（相对可见区域顶缘的偏移，求解锚点时实时加 view.position.y 基线）
var HOVER_BAND := Vector2(150.0, 430.0)
## 悬停机动参数（全部可由 balance.json enemies 段覆盖）：
## 垂直微浮 + 水平慢摇摆（straight/hover）+ spiral 中心漂移，相位按个体随机错开
var HOVER_BOB_AMP := 12.0  # 悬停垂直浮动振幅
var HOVER_BOB_FREQ := 2.0  # 悬停垂直浮动角频率
var HOVER_SWAY_AMP := 34.0  # 悬停水平摇摆振幅（straight/hover）
var HOVER_SWAY_FREQ := 1.2  # 悬停水平摇摆角频率
var SPIRAL_DRIFT_AMP := 56.0  # spiral 悬停期绕转中心水平漂移振幅
var SPIRAL_DRIFT_FREQ := 0.7  # spiral 漂移角频率
var SPIRAL_RADIUS := 50.0
## 敌机 HP 对局进程 ramp 系数：HP ×(1 + 系数×(Boss 击杀难度乘数-1))，对齐同类游戏的敌 HP 线性成长惯例
var HP_RAMP_FACTOR := 0.25  # 与 balance.json enemies.hp_ramp_factor 同步（2026-08-04 深局校准）
var SPEED_RAMP_FACTOR := 0.1  # 敌机速度对局进程 ramp（B12：原硬编码 0.1 无 json 键，现补 enemies.speed_ramp_factor）

## 尾焰软光点（P0-5 副轨，运行时辨识增强）：红/品红低 alpha 软光贴舰尾，尺寸族设计值 ×ws；
## 精英同色稍微光。贴图尾喷口在纹理 +y（enemy.tscn 根节点自带 π 旋转，即世界舰尾方向）
const TAIL_GLOW_RADIUS := 26.0
const TAIL_GLOW_RADIUS_ELITE := 36.0
const TAIL_GLOW_COLOR := Color(1.0, 0.22, 0.38, 0.32)
const TAIL_GLOW_COLOR_ELITE := Color(1.0, 0.25, 0.42, 0.46)

var strategy: StringName = &"straight"
## 分裂者标记（2026-08-04）：死亡生成 2 小机（config 表行 "split": true 置位；子机取消防无限分裂）
var _split := false
var _type_config: Dictionary = {}
var is_elite: bool = false
var hp: int = 2
var speed: float = 140.0
var can_shoot: bool = false
var score_value: int = 100
var fire_interval: float = FIRE_INTERVAL
var bullet_type: StringName = &"single"
## 悬停锚点 y（spawner 分配；<0 时按 hover_band 自取，保证直接 setup 的用法仍先下降后悬停）
var anchor_y: float = -1.0
## 辅助瞄准「强辅助」标记（P1-1）：setup 按 mark_ratio 掷点，终生稳定；
## 带标记者由 AimFrameLayer 画辅助框，准星入框时玩家出膛弹获得追踪。精英纳入；
## Boss/炮塔/编队战机非 Enemy 类，天然排除。池化 deactivate 复位防残留。
var aim_marked: bool = false

var _time: float = 0.0
var _phase: float = 0.0  # 机动相位（出生/重激活随机化，全波错开避免同相位机械浮动）
var _spawn_x: float = 0.0
var _fire_timer: float = FIRE_INTERVAL
## A4a：移动策略实例（_ready 按 strategy 构建；zigzag/dive/spiral 私有状态已迁入策略类）
var _strategy: EnemyMoveStrategy = null
## C06 修复：移动上下文缓存（每帧复用同一字典，只更新字段值，消除每帧 new Dictionary 分配）
var _move_ctx: Dictionary = {}
var _pool: EnemyPool = null
## 池活跃标记：回收的延迟调用（monitoring=false / reparent）在重激活后必须失效
var _active: bool = false
## 回收 reparent 保护：4.6 实测 reparent 也会触发 _exit_tree，置位期间禁止 forget 误清池清单
var _repooling: bool = false
## P0-2（2026-08-05 审计）：与玩家 Hitbox 重叠状态（area_entered/exited 事件驱动标记）。
## 重叠期每物理帧做 O(1) 守卫重掷（语义与逐帧 overlaps_area 完全等价：无敌结束仍重叠
## 会再次命中、闪避每帧重掷），空间查询从每物理帧 N 次降到事件 0 次。
var _body_contact := false

# 三角函数查表（2048 项循环表 + 线性插值，全敌机共享一份）
const TRIG_SIZE := 2048
static var _sin_table: PackedFloat32Array = []


static func sin_fast(x: float) -> float:
	if _sin_table.is_empty():
		_sin_table.resize(TRIG_SIZE + 1)
		for i in TRIG_SIZE + 1:
			_sin_table[i] = sin(TAU * float(i) / float(TRIG_SIZE))
	var t := fposmod(x, TAU) / TAU * TRIG_SIZE
	var i := int(t)
	return lerpf(_sin_table[i], _sin_table[i + 1], t - i)


static func cos_fast(x: float) -> float:
	return sin_fast(x + PI / 2.0)


var _hovering: bool = false
var _life_timer: float = 0.0
var _exiting: bool = false
var _exit_dir: Vector2 = Vector2.UP
var _exit_speed: float = 0.0
## 母舰召唤减速带：短时减速乘区（仅位移，不影响射速/寿命/计时）
var _summon_slow_timer: float = 0.0
var _summon_slow_factor: float = 1.0
var _slow_field_on: bool = false  # slow_field buff 层数缓存（buffs_changed 刷新，物理帧免每帧字典查询）

@onready var _sprite: Sprite2D = $Sprite2D
@onready var _shape: CollisionShape2D = $CollisionShape2D

## 尾焰软光点实例（_ready 创建，每机 +1 draw call）
var _tail_glow: Sprite2D = null


## config 字段：texture, hp(Vector2i), speed(Vector2), score, fire(开火概率),
## fire_interval, scale, radius, bullet_types(弹种池), elite(可选)。
## p_bullet_type 为空时从弹种池随机抽取（spawner 传入已做同屏上限控制的结果）。
## HP 按难度档位缩放（easy ×0.75，medium ×1，hard ×1.5），并随对局进程 ramp：
## ×(1 + hp_ramp_factor ×(Boss 击杀难度乘数-1))；速度按难度档 ×0.85/×1/×1.2 与同一 ramp ×0.1 系数成长。
func setup(config: Dictionary, p_strategy: StringName, p_difficulty: float, p_bullet_type: StringName = &"") -> void:
	strategy = p_strategy
	_type_config = config
	_split = config.get("split", false)
	is_elite = config.get("elite", false)
	# HP 三级乘算：机型区间 × 难度档 × 对局进程 ramp（随 Boss 击杀线性成长）
	hp = maxi(
		1,
		int(
			roundf(
				(
					randf_range(config["hp"].x, config["hp"].y)
					* GameState.enemy_hp_multiplier()
					* (1.0 + GameState.cfg("enemies.hp_ramp_factor", HP_RAMP_FACTOR) * (p_difficulty - 1.0))
				)
			)
		)
	)
	score_value = config["score"]
	can_shoot = randf() < config["fire"]
	fire_interval = config.get("fire_interval", FIRE_INTERVAL)
	var pool: Array = config.get("bullet_types", [&"single"])
	# H07（健壮性审核）：空弹种池回退单发（randi()%0 越界）
	if pool.is_empty():
		pool = [&"single"]
	bullet_type = p_bullet_type if p_bullet_type != &"" else pool[randi() % pool.size()]
	speed = (
		randf_range(config["speed"].x, config["speed"].y)
		* (1.0 + GameState.cfg("enemies.speed_ramp_factor", SPEED_RAMP_FACTOR) * (p_difficulty - 1.0))
		* GameState.enemy_speed_multiplier()
	)
	# setup() 在 _ready() 之前调用，不能用 @onready 变量
	var sprite: Sprite2D = $Sprite2D
	var shape_node: CollisionShape2D = $CollisionShape2D
	sprite.texture = config["texture"]
	# 辅助瞄准标记：按比率掷点（直实例化与池化 reactivate 均过 setup，标记终生稳定）
	aim_marked = randf() < GameState.cfg("player.aim_assist.mark_ratio", 0.25)
	# 机体尺寸族：config 存设计值（1.0 基准），统一乘全局缩放（shape 已 local_to_scene，实例独立）
	var sc: float = config.get("scale", 0.85)
	sprite.scale = Vector2(sc, sc) * GameState.world_scale
	var hit_r: float = config.get("radius", 30.0) * GameState.world_scale
	(shape_node.shape as CircleShape2D).radius = hit_r
	# G07：辅助框半径缓存随 setup 刷新（池化实例复用不同半径机型时 meta 不得过期）
	set_meta("aim_frame_radius", hit_r)


## 分裂者标记（2026-08-04；子机复用 config 后取消，防止无限分裂）
func set_split(enabled: bool) -> void:
	_split = enabled


## 分裂者死亡生成 2 小机：缩放 ×0.6 / HP 半 / 无分数 / 不开火 / 不再分裂
func _spawn_split_minis() -> void:
	var pool := GameState.enemy_pool
	if pool == null:
		return
	for i in 2:
		var mini_enemy := pool.spawn(_type_config, strategy, 1.0, global_position + Vector2(24.0 if i == 0 else -24.0, 0.0))
		if mini_enemy == null or not is_instance_valid(mini_enemy):
			continue
		(mini_enemy.get_node("Sprite2D") as Sprite2D).scale *= 0.6
		mini_enemy.hp = maxi(1, int(roundf(mini_enemy.hp * 0.5)))
		mini_enemy.score_value = 0
		mini_enemy.can_shoot = false
		mini_enemy.set_split(false)


## 对外公开接口（A1 修复）：对象池/生成器/事件读取内部状态，禁止跨类直接写 _ 私有字段
## A6：语义化类型查询（Boss override 返回 true，调用方不依赖具体类型）
func is_boss() -> bool:
	return false


func hovering() -> bool:
	return _hovering


func set_fire_timer(seconds: float) -> void:
	_fire_timer = seconds


func fire_at_player() -> void:
	_fire_at_player()


func set_life_timer(seconds: float) -> void:
	_life_timer = seconds


func set_pool(pool: EnemyPool) -> void:
	_pool = pool


func is_active() -> bool:
	return _active


func set_repooling(value: bool) -> void:
	_repooling = value


func is_exiting() -> bool:
	return _exiting


## 母舰减速带剩余时长（A7 遗留清理：测试/诊断公开查询，替代 _summon_slow_timer 直读）
func summon_slow_timer() -> float:
	return _summon_slow_timer


func _ready() -> void:
	GameState.bind_enemy(self)  # 统一绑定：add_to_group("enemy") + 注册 + entity_registered（docs/ENTITY_MANAGER.md）
	# 数值配置缓存（启动一次读入）
	ENEMY_BULLET_SPEED = GameState.cfg("enemies.bullet_speed", ENEMY_BULLET_SPEED)
	SPREAD_BULLET_SPEED = GameState.cfg("enemies.spread_bullet_speed", SPREAD_BULLET_SPEED)
	LASER_BULLET_SPEED = GameState.cfg("enemies.laser_bullet_speed", LASER_BULLET_SPEED)
	BULLET_DAMAGE_SINGLE = GameState.cfg("enemies.bullet_damage.single", BULLET_DAMAGE_SINGLE)
	BULLET_DAMAGE_SPREAD = GameState.cfg("enemies.bullet_damage.spread", BULLET_DAMAGE_SPREAD)
	BULLET_DAMAGE_LASER = GameState.cfg("enemies.bullet_damage.laser", BULLET_DAMAGE_LASER)
	COLLISION_DAMAGE = GameState.cfg("enemies.collision_damage", COLLISION_DAMAGE)
	SLOW_FIELD_FACTOR = GameState.cfg("buffs.slow_field.factor", SLOW_FIELD_FACTOR)
	SPREAD_FAN_STEP = GameState.cfg("enemies.spread_fan_step", SPREAD_FAN_STEP)
	LIFETIME = GameState.cfg("enemies.lifetime", LIFETIME)
	EXIT_ACCEL = GameState.cfg("enemies.exit_accel", EXIT_ACCEL)
	AGGR_CHASE_SPEED = GameState.cfg("enemies.aggressive_chase_speed", AGGR_CHASE_SPEED)
	FIRE_INTERVAL = GameState.cfg("enemies.fire_interval", FIRE_INTERVAL)
	# H19（健壮性审核）：hover_band 判型回退（对齐 spawner G06 口径，防非数组 _ready 崩溃）
	var band: Variant = GameState.cfg("enemies.hover_band", [HOVER_BAND.x, HOVER_BAND.y])
	if band is Array and band.size() >= 2:
		HOVER_BAND = Vector2(float(band[0]), float(band[1]))
	else:
		HOVER_BAND = Vector2(HOVER_BAND.x, HOVER_BAND.y)
	HOVER_BOB_AMP = GameState.cfg("enemies.hover_bob_amp", HOVER_BOB_AMP)
	HOVER_BOB_FREQ = GameState.cfg("enemies.hover_bob_freq", HOVER_BOB_FREQ)
	HOVER_SWAY_AMP = GameState.cfg("enemies.hover_sway_amp", HOVER_SWAY_AMP)
	HOVER_SWAY_FREQ = GameState.cfg("enemies.hover_sway_freq", HOVER_SWAY_FREQ)
	SPIRAL_DRIFT_AMP = GameState.cfg("enemies.spiral_drift_amp", SPIRAL_DRIFT_AMP)
	SPIRAL_DRIFT_FREQ = GameState.cfg("enemies.spiral_drift_freq", SPIRAL_DRIFT_FREQ)
	SPIRAL_RADIUS = GameState.cfg("enemies.spiral_radius", SPIRAL_RADIUS)
	# 每个实例独立形状，避免共享 sub_resource 半径互相影响
	_shape.shape = _shape.shape.duplicate()
	_spawn_x = position.x
	_phase = randf() * TAU
	_fire_timer = randf_range(1.0, maxf(fire_interval, 1.0))
	_strategy = _make_strategy()
	_strategy.reset(self)
	# 尾焰软光点（P0-5 副轨）：红/品红低 alpha，尺寸族 ×ws，随舰体朝向贴尾；精英同色稍微光。
	# 池化实例 _ready 先于 reactivate 执行（setup 未跑、is_elite 恒 false、texture 为空），
	# 颜色/半径档由 _update_tail_glow 在 reactivate 后按 is_elite 重同步（绝对 scale 重算）
	var glow_radius := TAIL_GLOW_RADIUS_ELITE if is_elite else TAIL_GLOW_RADIUS
	_tail_glow = CinematicFx.soft_glow(glow_radius * GameState.world_scale, TAIL_GLOW_COLOR)
	_tail_glow.show_behind_parent = true
	add_child(_tail_glow)
	_update_tail_glow()
	# P1-6：击杀/精英击杀震动强度一次性缓存（热路径禁 cfg）
	_shake_die_normal = float(GameState.cfg("effects.shake.enemy_die", _shake_die_normal))
	_shake_die_elite = float(GameState.cfg("effects.shake.elite_die", _shake_die_elite))
	# 2026-08-03 审计：slow_field 层数缓存（物理帧免每帧字典查询）；池化复用不重跑 _ready，
	# 初始值即对局开局 buff 状态，后续由 buffs_changed 增量刷新
	_slow_field_on = GameState.buff_count(&"slow_field") > 0
	GameState.buffs_changed.connect(_on_buffs_changed)
	# P0-2（2026-08-05 审计）：体碰改信号事件驱动——area_entered/exited 标记重叠状态
	# （collision_mask=3 已含 player Hitbox 层 1），替代每物理帧 overlaps_area 空间查询
	area_entered.connect(_on_area_entered)
	area_exited.connect(_on_area_exited)


## slow_field 缓存刷新（2026-08-03 审计，热路径禁字典约定）
func _on_buffs_changed() -> void:
	_slow_field_on = GameState.buff_count(&"slow_field") > 0


## A4a：按 strategy 构建移动策略实例（共享悬停常量从 balance 缓存值注入，行为逐字节等价）
func _make_strategy() -> EnemyMoveStrategy:
	var params := {
		"hover_bob_amp": HOVER_BOB_AMP,
		"hover_bob_freq": HOVER_BOB_FREQ,
		"hover_sway_amp": HOVER_SWAY_AMP,
		"hover_sway_freq": HOVER_SWAY_FREQ,
		"spiral_drift_amp": SPIRAL_DRIFT_AMP,
		"spiral_drift_freq": SPIRAL_DRIFT_FREQ,
		"spiral_radius": SPIRAL_RADIUS,
		"aggressive_chase_speed": AGGR_CHASE_SPEED,
	}
	match strategy:
		&"sine":
			return EnemyMoveStrategy.SineMove.new(params)
		&"zigzag":
			return EnemyMoveStrategy.ZigzagMove.new(params)
		&"dive":
			return EnemyMoveStrategy.DiveMove.new(params)
		&"spiral":
			return EnemyMoveStrategy.SpiralMove.new(params)
		&"noise":
			return EnemyMoveStrategy.NoiseMove.new(params)
		&"aggressive":
			return EnemyMoveStrategy.AggressiveMove.new(params)
	return EnemyMoveStrategy.HoverMove.new(params)  # straight / hover


## 尾焰光点同步：颜色/半径档按精英标记、位置贴纹理尾缘（经 sprite.scale 自动 ×ws 并跟随机型 scale）。
## 池化重激活（新机型贴图/scale）后由 reactivate 再调一次；半径经绝对 scale 重算
## （soft_glow 以 scale = radius×ws / (SOFT_TEX_SIZE*0.5) 表达半径，幂等赋值不累积）。
func _update_tail_glow() -> void:
	if _tail_glow == null:
		return
	_tail_glow.modulate = TAIL_GLOW_COLOR_ELITE if is_elite else TAIL_GLOW_COLOR
	var glow_radius := TAIL_GLOW_RADIUS_ELITE if is_elite else TAIL_GLOW_RADIUS
	_tail_glow.scale = Vector2.ONE * (glow_radius * GameState.world_scale / (CinematicFx.SOFT_TEX_SIZE * 0.5))
	var tex_h := 190.0
	if _sprite.texture != null:
		tex_h = _sprite.texture.get_height()
	_tail_glow.position = Vector2(0.0, tex_h * 0.5 * _sprite.scale.y * 0.85)


## anchor_y 未由 spawner 分配时自取（首个物理帧惰性调用，取最终出生位置）：
## 出生点下方一段距离，钳入悬停带；深位出生（悬停带之下）不悬停，持续下降出屏销毁。
## 悬停带为相对可见区域顶缘的偏移，求解时实时加 view 基线（支持中途切视角档）
func _resolve_anchor() -> void:
	if anchor_y < 0.0:
		var band_top := GameState.view_world_rect().position.y + HOVER_BAND.x
		var band_bottom := GameState.view_world_rect().position.y + HOVER_BAND.y
		if position.y > band_bottom:
			anchor_y = 1.0e9
		else:
			anchor_y = clampf(position.y + randf_range(120.0, 240.0), band_top, band_bottom)


## 撞击结算（P0-2 信号驱动版）：重叠标记置位期每物理帧调用——take_damage 守卫
## （无敌/闪避/单帧）与逐帧轮询完全等价：无敌结束仍重叠会再次命中、闪避每帧重掷。
## 无 _active 守卫：直实例化敌机（测试/瞬发路径）_active 恒 false（enemy.gd 语义缺口），
## 守卫会拦截其体碰；陈旧调用由 deactivate 复位 _body_contact + set_physics_process(false) 防住。
func _try_body_collision() -> void:
	var player := GameState.player_ref as Player
	if player == null:
		return
	# 撞体伤害随对局进程 ramp（与敌弹同一系数）；补传撞体位置作伤害源方向（D8）；
	# is_dead 守卫由 take_damage 内部处理（对齐原实现）
	player.take_damage(maxi(1, int(roundf(COLLISION_DAMAGE * GameState.enemy_damage_ramp()))), global_position)


## P0-2：进入玩家 Hitbox → 标记重叠并立即结算（守卫放行则命中，否则下物理帧重掷）
func _on_area_entered(area: Area2D) -> void:
	if not area.is_in_group("player_hitbox"):
		return  # 玩家弹等其他 Area 忽略
	_body_contact = true
	_try_body_collision()


## P0-2：离开玩家 Hitbox → 清除重叠标记（停止每帧重掷）
func _on_area_exited(area: Area2D) -> void:
	if area.is_in_group("player_hitbox"):
		_body_contact = false


## 池化复用：全状态重置（spawner 经 EnemyPool 调用；直接实例化走 _ready 初始化）
func reactivate(
	config: Dictionary,
	p_strategy: StringName,
	p_difficulty: float,
	p_bullet_type: StringName = &"",
) -> void:
	# L02（2026-08-03 审查）：池化复用重连 buff 信号——_ready 只执行一次，而 _exit_tree
	# 在每次 reparent（release→pool / spawn→Main）都断开连接；不重连则 _slow_field_on
	# 冻结在陈旧值，首个回收循环后 slow_field buff 对该机静默失效。连接后立即刷新缓存。
	if not GameState.buffs_changed.is_connected(_on_buffs_changed):
		GameState.buffs_changed.connect(_on_buffs_changed)
	_on_buffs_changed()
	_active = true
	_time = 0.0
	_hovering = false
	_exiting = false
	_life_timer = 0.0
	_exit_speed = 0.0
	_summon_slow_timer = 0.0
	_summon_slow_factor = 1.0
	_score_scale = 1.0
	visible = true
	monitoring = true
	set_physics_process(true)
	# P0-2：重叠标记复位（池化复用——上一任使用者的重叠状态不得残留到新激活）
	_body_contact = false
	_sprite.modulate = Color.WHITE
	_flash_timer = 0.0  # P1-2：闪白计时复位（池化复用）
	GameState.register_enemy(self)
	setup(config, p_strategy, p_difficulty, p_bullet_type)
	_update_tail_glow()
	_spawn_x = position.x
	_phase = randf() * TAU
	_fire_timer = randf_range(1.0, maxf(fire_interval, 1.0))
	anchor_y = -1.0
	# A4a：策略重激活复位（zigzag 相位/ dive 冲刺目标/ spiral 绕转中心由策略类持有）
	_strategy = _make_strategy()
	_strategy.reset(self)


## 池化回收：停用但保留实例
func deactivate() -> void:
	_active = false
	aim_marked = false  # 辅助瞄准标记复位，防池残留串到下一任使用者
	visible = false
	set_physics_process(false)
	# P0-2：重叠标记复位（回收后 area_exited 未必投递，防陈旧重叠状态残留）
	_body_contact = false
	GameState.unregister_enemy(self)
	for c in died.get_connections():
		died.disconnect(c["callable"])
	position = Vector2(-500.0, -500.0)
	_deferred_disable_monitoring.call_deferred()


## 物理回调内不能直改 monitoring，延迟到帧末；若敌机已被重激活（同帧复用）则跳过
func _deferred_disable_monitoring() -> void:
	if not _active:
		monitoring = false


func _despawn() -> void:
	if _pool != null and is_instance_valid(_pool):
		_pool.release(self)
	else:
		queue_free()


func _exit_tree() -> void:
	GameState.unbind_enemy(self)  # 统一解绑：注销 + entity_unregistered（docs/ENTITY_MANAGER.md）
	# L02（2026-08-03 审查）：buff 信号断开（C22 模式）；池化 reparent 复用由
	# reactivate() 对称重连（_ready 只执行一次，断开后必须重连，见 reactivate 注释）
	if GameState.buffs_changed.is_connected(_on_buffs_changed):
		GameState.buffs_changed.disconnect(_on_buffs_changed)
	# 池内 reparent 也会经过此回调（_repooling 置位），不算离开池
	# is_instance_valid 防护与 _despawn 对称：池对象先于实例释放的时序下 _pool 已失效
	if _pool != null and is_instance_valid(_pool) and not _repooling:
		_pool.forget(self)


func _physics_process(delta: float) -> void:
	_time += delta
	_update_flash(delta)
	if _exiting:
		# 寿命离场：向上或侧方加速，离场不给分、不计击杀
		_exit_speed += EXIT_ACCEL * delta
		position += _exit_dir * _exit_speed * delta
		var exit_view := GameState.view_world_rect()
		if position.y < exit_view.position.y - 150.0 or position.x < exit_view.position.x - 150.0 or position.x > exit_view.end.x + 150.0:
			_despawn()
		return
	_life_timer += delta
	if _life_timer >= LIFETIME:
		_begin_lifetime_exit()
		return
	if anchor_y < 0.0:
		_resolve_anchor()  # 惰性解析：取首个物理帧的最终出生位置
	# 慢速力场：全局移速 ×0.8（仅移动位移，不影响射速/寿命/计时）
	# 2026-08-03 审计：层数经 buffs_changed 缓存为布尔（每敌每帧字典查询违规）
	var slow_mult := SLOW_FIELD_FACTOR if _slow_field_on else 1.0
	# 母舰召唤减速带：短时乘区叠加（同语义，仅位移）
	if _summon_slow_timer > 0.0:
		_summon_slow_timer -= delta
		slow_mult *= _summon_slow_factor
	var mdelta := delta * slow_mult
	var view := GameState.view_world_rect()
	# A4a：移动委托移动策略（straight/sine/zigzag/dive/spiral/noise/hover/aggressive）
	# C06：复用 _move_ctx 字典（字段原地更新），避免每个敌机每物理帧新建 Dictionary
	_move_ctx["view"] = view
	_move_ctx["mdelta"] = mdelta
	_move_ctx["speed"] = speed
	_move_ctx["time"] = _time
	_move_ctx["phase"] = _phase
	_move_ctx["spawn_x"] = _spawn_x
	_move_ctx["anchor_y"] = anchor_y
	_move_ctx["hovering"] = _hovering
	_move_ctx["player"] = GameState.player_ref
	if _strategy != null:
		_strategy.update(delta, self, _move_ctx)
	# 到达锚点转入悬停机动（dive 冲刺期除外；spiral 以绕转中心为准）
	if not _hovering:
		var diving: bool = _strategy != null and _strategy.is_diving()
		var ref_y: float = _strategy.hover_reference_y() if _strategy != null else -1.0
		if ref_y < 0.0:
			ref_y = position.y
		if not diving and ref_y >= anchor_y:
			_hovering = true

	if can_shoot:
		# B 梯队（fair plan §8）：DDA 降档拉长开火间隔（dda_factor 除入计时 = 间隔 ×因子；
		# 只拉间隔不降收益，分数公平）
		_fire_timer -= delta / GameState.dda_factor()
		if _fire_timer <= 0.0:
			_fire_timer = fire_interval
			_fire_at_player()

	# P0-2（2026-08-05 审计）：仅重叠标记期做 O(1) 守卫重掷（原每物理帧 overlaps_area 空间查询）
	if _body_contact:
		_try_body_collision()

	# C06：复用主路径已取的 view（391 行），避免同帧重复 view_world_rect()
	if position.y > view.end.y + 60.0:
		_despawn()


func _fire_at_player() -> void:
	if GameState.player_ref == null:
		return
	var base_dir := (GameState.player_ref.global_position - global_position).normalized()
	if base_dir == Vector2.ZERO:
		base_dir = Vector2.DOWN  # G026：与玩家圆心重合时回退，防零方向弹永不销毁
	match bullet_type:
		&"spread":
			# 五向扇形弹：以瞄准方向为中心 ±2 步展开
			for i in 5:
				_spawn_enemy_bullet(base_dir.rotated(SPREAD_FAN_STEP * float(i - 2)), SPREAD_BULLET_SPEED, &"spread")
		&"laser":
			_spawn_enemy_bullet(base_dir, LASER_BULLET_SPEED, &"laser")
		_:
			_spawn_enemy_bullet(base_dir, ENEMY_BULLET_SPEED, &"single")


func _spawn_enemy_bullet(dir: Vector2, bullet_speed: float, p_type: StringName) -> void:
	var dmg := BULLET_DAMAGE_SINGLE
	if p_type == &"spread":
		dmg = BULLET_DAMAGE_SPREAD
	elif p_type == &"laser":
		dmg = BULLET_DAMAGE_LASER
	var b: Bullet = GameState.bullet_pool.fire(dir, bullet_speed, dmg, false)
	if b == null:
		return  # P2-3：同屏敌弹硬上限
	b.position = position
	b.set_meta("bullet_type", p_type)
	if p_type == &"laser":
		# 细长高亮快速弹（polygon 尖端朝 +x，即飞行方向）
		var poly := b.sprite_node()  # C24：缓存引用，不再每次 get_node
		if poly != null:
			poly.scale = Vector2(2.2, 0.55)
			poly.self_modulate = Color(1.0, 0.85, 0.35)  # P0-3：Sprite2D 无 color，用 self_modulate


## 寿命到期：向上或侧方加速离场（停火，不给分、不计击杀）。
func _begin_lifetime_exit() -> void:
	_exiting = true
	can_shoot = false
	if randf() < 0.5:
		_exit_dir = Vector2(randf_range(-0.6, 0.6), -1.0).normalized()  # 向上
	else:
		# 就近侧方（略带上行），从较近的一侧离场
		# E06 修复：960 硬编码改视口中心（D10 同类口径；相机滚动时取当前可视中心）
		_exit_dir = Vector2(1.0 if position.x < GameState.view_world_rect().get_center().x else -1.0, randf_range(-0.4, 0.0)).normalized()
	_exit_speed = speed


## 母舰召唤减速带命中：duration 秒内位移速度 ×factor（与慢速力场乘算叠加）
func apply_slow(duration: float, factor: float) -> void:
	_summon_slow_timer = duration
	_summon_slow_factor = factor


var _score_scale: float = 1.0
## P1-2：受击闪白手动衰减计时（替代每命中新建 Tween；_physics_process 逐帧 lerp 回本色）
var _flash_timer: float = 0.0
const FLASH_TIME := 0.1
## P1-6：击杀震动强度缓存（_ready 一次性读入，热路径禁 cfg）
var _shake_die_normal := 5.0
var _shake_die_elite := 9.0


func take_damage(amount: int, score_scale: float = 1.0) -> void:
	if hp <= 0:
		return  # 已死亡待回收（同帧多发命中防重复结算）
	hp -= amount
	_score_scale = score_scale
	_sprite.modulate = Color(2.0, 2.0, 2.0)  # 受击闪白
	_flash_timer = FLASH_TIME
	if hp <= 0:
		die()


## P1-2：受击闪白手动衰减（替代 Tween；线性 lerp 回本色，零分配）
func _update_flash(delta: float) -> void:
	if _flash_timer <= 0.0:
		return
	_flash_timer -= delta
	if _flash_timer <= 0.0:
		_sprite.modulate = Color.WHITE
	else:
		_sprite.modulate = _sprite.modulate.lerp(Color.WHITE, delta / FLASH_TIME)


func die() -> void:
	# 分裂者（2026-08-04）：死亡生成 2 小机（子机独立结算，母体分数照常给）
	if _split:
		_spawn_split_minis()
	# 母舰弹丸击毁只给 1/3 分（向下取整）
	GameState.add_score(int(score_value * _score_scale))
	GameState.add_kill()
	# 吸血 buff：击毁回复 10% 生命上限（每帧至多一次，对齐原作 LIFESTEAL_FRACTION）
	GameState.try_lifesteal()
	GameState.play_sfx(GameState.SFX_EXPLOSION_BIG if is_elite else GameState.SFX_EXPLOSION)
	GameState.shake(_shake_die_elite if is_elite else _shake_die_normal)
	Explosion.spawn_at(get_parent(), global_position, 1.5 if is_elite else 1.0)
	died.emit(self)
	_despawn()
