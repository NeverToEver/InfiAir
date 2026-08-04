class_name Bullet
extends Area2D
## 直线子弹，玩家弹与敌弹共用此场景，通过 setup()/activate() 区分阵营。
## 正常产弹走 GameState.bullet_pool（对象池复用）；直接实例化（测试）走兼容路径。

var direction: Vector2 = Vector2.DOWN
var speed: float = 900.0
var damage: int = 1
var is_player_bullet: bool = true
var homing: bool = false
var homing_time: float = 0.0
## 追踪转向速率（rad 级插值系数；精英炮台弱锁定追踪弹降为 1.5）
var homing_turn_rate: float = 4.0
## 辅助瞄准追踪目标（P1-1，玩家弹专用）：准星入标记框时由 player._fire 写入，
## 优先于 homing 玩家追踪分支；目标失效即直行。池化 activate 复位为 null。
var homing_target: Node2D = null
## 穿透剩余次数（玩家弹，穿透弹 buff）
var pierce: int = 0
## 命中产生 AoE 爆炸（玩家弹，爆炸弹 buff）
var explosive: bool = false
## 导弹溅射（母舰导弹）：命中时对半径内敌人追加固定溅射伤害（含主目标与 Boss）
var splash_damage: int = 0
var splash_radius: float = 0.0
## 击毁得分系数（母舰弹丸为 1/3）
var score_scale: float = 1.0

## 爆炸弹 buff 固定值（对齐原作 bullet_vs_entities.py 单层取值：半径 50、伤害 30；
## 主目标吃直击+溅射两段、不伤 Boss、不伤玩家）
var EXPLOSIVE_RADIUS := 50.0
var EXPLOSIVE_DAMAGE := 30
## 弹丸视觉缩放（设计值 1.3；effects.bullet_visual_scale × world_scale，碰撞半径不变）
var VISUAL_SCALE := 1.3
## 敌弹视觉缩放（设计值 2.4；effects.enemy_bullet_visual_scale × world_scale，P0-4 敌弹可见性）
var ENEMY_VISUAL_SCALE := 2.4
## 辅助瞄准追踪近距收敛半径：低于此距离直取目标（避免低转向档擦弹后绕目标永动圆）
var HOMING_SNAP_RADIUS := 36.0
## 受击宽限帧（2026-08-03 公平感机制一）：敌弹进入玩家 Hitbox 后暂缓结算的窗口（秒），
## 窗口内离开（擦过边缘）不计伤；balance.json player.grace_period（钳制 (0, 0.15]）
var GRACE_PERIOD := 0.05
## 弹反倍率（2026-08-03 公平感机制四）：弹反后返回速度倍率 / 伤害倍率（player.parry.*）
var REFLECT_SPEED_MULT := 2.0
var REFLECT_DAMAGE_MULT := 1.5

var _homing_elapsed: float = 0.0
var _pool: Node = null
## 池活跃标记：回收的延迟调用（monitoring=false / reparent）在重激活后必须失效
var _active: bool = false
## 回收 reparent 保护：4.6 实测 reparent 也会触发 _exit_tree，置位期间禁止 forget 误清池清单
var _repooling: bool = false
## 受击宽限 Timer（机制一）：进入 Hitbox 时启动，窗口内 area_exited 取消；null = 未在宽限期
var _grace_timer: Timer = null
## 宽限结算复核目标（机制一）：Timer 到期时单次 overlaps 查询的 Hitbox 引用
var _grace_hitbox: Area2D = null
## 擦弹单次计数（机制二）：同一敌弹至多计 1 次擦弹分（池化 activate 复位）
var _graze_done: bool = false

@onready var _polygon: Polygon2D = $Polygon2D


## 兼容路径：直接实例化时 setup() 后由 _ready() 应用阵营外观。
func setup(
	p_direction: Vector2, p_speed: float, p_damage: int, p_is_player: bool, p_homing: bool = false, p_homing_time: float = 0.0
) -> void:
	direction = p_direction.normalized()
	# H10（健壮性审核）：零方向弹回退 DOWN（对齐 G026 口径，防静止弹永驻场景）
	if direction == Vector2.ZERO:
		direction = Vector2.DOWN
	speed = p_speed
	# 敌方子弹伤害随对局进程 ramp（Boss 击杀难度乘数驱动，GameState.enemy_damage_ramp）
	damage = p_damage if p_is_player else maxi(1, int(roundf(p_damage * GameState.enemy_damage_ramp())))
	is_player_bullet = p_is_player
	homing = p_homing
	homing_time = p_homing_time


## 池化路径：激活并重置全部状态（含上一任使用者的外观/标记）。
func activate(
	p_direction: Vector2, p_speed: float, p_damage: int, p_is_player: bool, p_homing: bool = false, p_homing_time: float = 0.0
) -> void:
	setup(p_direction, p_speed, p_damage, p_is_player, p_homing, p_homing_time)
	_active = true
	_homing_elapsed = 0.0
	homing_target = null
	_graze_done = false
	pierce = 0
	explosive = false
	splash_damage = 0
	splash_radius = 0.0
	score_scale = 1.0
	homing_turn_rate = 4.0
	visible = true
	monitoring = true
	set_physics_process(true)  # C04：位移走物理帧，与 Area2D overlap 检测同步
	_apply_faction()


## 池化回收：停用但保留实例。
func deactivate() -> void:
	_active = false
	visible = false
	set_physics_process(false)  # C04：与 activate 的物理帧开关配对
	position = Vector2(-500.0, -500.0)
	_cancel_grace()  # 机制一：回收即停宽限 Timer（清弹/离屏回收防悬挂）
	_deferred_disable_monitoring.call_deferred()


## 对外公开接口（A1 修复）：对象池协调的内部状态封装，禁止跨类直接写 _ 私有字段
func set_pool(pool: Node) -> void:
	_pool = pool


func is_active() -> bool:
	return _active


func set_repooling(value: bool) -> void:
	_repooling = value


func despawn() -> void:
	_despawn()


## A7：测试/诊断白盒断言经公开接口
func explode() -> void:
	_explode()


## E01：测试/诊断经公开接口（与 explode 对称）
func splash() -> void:
	_splash()


## 物理回调内不能直改 monitoring，延迟到帧末；若子弹已被重激活（同帧复用）则跳过
func _deferred_disable_monitoring() -> void:
	if not _active:
		monitoring = false


func _ready() -> void:
	area_entered.connect(_on_area_entered)
	area_exited.connect(_on_area_exited)
	EXPLOSIVE_RADIUS = GameState.cfg("buffs.explosive.radius_per_level", EXPLOSIVE_RADIUS)
	EXPLOSIVE_DAMAGE = GameState.cfg("buffs.explosive.damage_per_level", EXPLOSIVE_DAMAGE)
	VISUAL_SCALE = GameState.cfg("effects.bullet_visual_scale", VISUAL_SCALE) * GameState.world_scale
	ENEMY_VISUAL_SCALE = GameState.cfg("effects.enemy_bullet_visual_scale", ENEMY_VISUAL_SCALE) * GameState.world_scale
	# 机制一：宽限窗口钳制 (0, 0.15]（超长宽限会让「明显该中的弹穿过」，破坏公平感）
	GRACE_PERIOD = clampf(float(GameState.cfg("player.grace_period", GRACE_PERIOD)), 0.001, 0.15)
	# 机制四：弹反倍率（player.parry.reflect_*）
	REFLECT_SPEED_MULT = float(GameState.cfg("player.parry.reflect_speed_mult", REFLECT_SPEED_MULT))
	REFLECT_DAMAGE_MULT = float(GameState.cfg("player.parry.reflect_damage_mult", REFLECT_DAMAGE_MULT))
	# 碰撞半径：设计值 6 × 全局缩放（幂等赋值；池化实例共享 shape 也只写同值）
	(($CollisionShape2D as CollisionShape2D).shape as CircleShape2D).radius = 6.0 * GameState.world_scale
	_apply_faction()


## C24 修复：Polygon2D 懒加载缓存——池化复用生命周期内引用稳定，
## 调用方（boss_fire/enemy/mothership）不再每次发射 get_node("Polygon2D")
var _polygon_cache: Polygon2D = null

## 竞品调研 P0-2：玩家弹白芯高光（描边感，敌我弹可读性）——内芯子节点引用缓存
var _core_cache: Polygon2D = null


func polygon_node() -> Polygon2D:
	if _polygon_cache == null:
		_polygon_cache = get_node_or_null("Polygon2D") as Polygon2D
	return _polygon_cache


## P0-2：内芯高光节点懒加载（与 polygon_node 同模式，热路径零字符串查找）
func core_node() -> Polygon2D:
	if _core_cache == null:
		_core_cache = get_node_or_null("Polygon2D/Core") as Polygon2D
	return _core_cache


func _exit_tree() -> void:
	# 被外部 queue_free（清场/测试/场景重载）时通知池移除引用；
	# 池内 reparent 也会经过此回调（_repooling 置位），不算离开池
	# K10：与 enemy.gd 对称补 is_instance_valid——池节点先于活跃子弹释放（场景卸载时序）时
	# _pool 为悬空引用，forget 调用会踩已释放对象
	if _pool != null and is_instance_valid(_pool) and not _repooling:
		_pool.forget(self)


func _apply_faction() -> void:
	rotation = direction.angle()
	# 重置外观（敌机/Boss 激光长弹、母舰弹的自定义外观）
	scale = Vector2.ONE
	modulate = Color.WHITE
	_polygon.scale = Vector2.ONE * (VISUAL_SCALE if is_player_bullet else ENEMY_VISUAL_SCALE)
	if has_meta("bullet_type"):
		remove_meta("bullet_type")
	if is_player_bullet:
		collision_layer = 2  # 第 2 层：player_bullet
		collision_mask = 4  # 命中第 3 层：enemy
		_polygon.color = Color(1.0, 0.9, 0.25)
	else:
		collision_layer = 8  # 第 4 层：enemy_bullet
		collision_mask = 1  # 命中第 1 层：player
		_polygon.color = Color(1.0, 0.38, 0.3)
	# P0-2 可读性：玩家弹显示白色高光内芯（金边白芯，满屏弹幕下敌我火力可区分）；敌弹隐藏
	var core := core_node()
	if core != null:
		core.visible = is_player_bullet


func _despawn() -> void:
	if _pool != null and is_instance_valid(_pool):
		_pool.release(self)
	else:
		queue_free()


## P2-10（竞品调研）：致死弹 0.5s 高亮残留（死亡归因）——停位移/关碰撞/红闪高亮，
## 一次性 Timer 到期回收（Timer 随场景树释放，AGENTS 协程纪律）。
func _linger_fatal(duration: float = 0.5) -> void:
	set_physics_process(false)
	monitoring = false
	modulate = Color(2.0, 0.7, 0.7)
	_polygon.color = Color(1.2, 0.35, 0.35)
	var t := Timer.new()
	t.one_shot = true
	t.wait_time = duration
	t.timeout.connect(
		func() -> void:
			_despawn()
			t.queue_free()
	)
	add_child(t)
	t.start()


func _physics_process(delta: float) -> void:
	if homing_target != null:
		# 辅助瞄准追踪（P1-1）：优先于 homing 玩家追踪分支；目标失效/超时限即直行
		# B4 修复：池化敌机 deactivate 后仍是合法节点（is_instance_valid 通过）但已回收，
		# 不判会在 deactivate 驻留期追向 (-500,-500)。用 GameState.enemies 注册表判"在屏"——
		# 活跃敌机（直实例化 _ready 注册 / 池化 reactivate 注册）都在表中，deactivate 已注销。
		# 不能用 is_active()：直实例化敌机从不走 reactivate，_active 恒 false（enemy.gd 语义缺口）。
		# homing_target 恒为 Enemy（标记仅掷给 Enemy，Boss/炮台/编队战机排除）。
		if not is_instance_valid(homing_target):
			homing_target = null
		elif not GameState.enemies_has(homing_target):  # G010：注册表 O(1) 存在性判定（替代 Array.has 线性扫描）
			homing_target = null
		elif _homing_elapsed < homing_time:
			_homing_elapsed += delta
			var to_target: Vector2 = homing_target.global_position - global_position
			var dist := to_target.length()
			if dist > 0.0 and dist <= HOMING_SNAP_RADIUS + speed * delta:
				# 近距直取：恒定转向速率追不上贴脸急转，擦弹会绕目标永动圆；
				# 进入收敛半径后直接对准目标，呈「绕渐小圆后命中」的观感
				direction = to_target / dist
				rotation = direction.angle()
			else:
				# H05（健壮性审核）：dist==0（追踪目标与子弹重合）时保持原向——除零产生
				# inf/NaN 角度会污染 direction 与坐标，直到出界才自愈
				if dist <= 0.0:
					direction = Vector2.RIGHT
					rotation = direction.angle()
				else:
					# 距离越近转向越急：螺旋收敛而非恒定半径环绕
					var rate := homing_turn_rate * (1.0 + HOMING_SNAP_RADIUS * 2.0 / dist)
					var target_angle := lerp_angle(direction.angle(), to_target.angle(), rate * delta)
					direction = Vector2.RIGHT.rotated(target_angle)
					rotation = target_angle
	elif homing and _homing_elapsed < homing_time:
		_homing_elapsed += delta
		if GameState.player_ref != null:
			var new_angle := lerp_angle(
				direction.angle(), (GameState.player_ref.global_position - global_position).angle(), homing_turn_rate * delta
			)
			direction = Vector2.RIGHT.rotated(new_angle)
			rotation = new_angle
	position += direction * speed * delta
	if not GameState.view_world_rect(80.0).has_point(position):
		_despawn()


## 爆炸弹 buff：命中时对周围敌人造成固定 AoE 伤害（主目标同吃，Boss 除外）。
## 原作公式为 半径/伤害 ×层数；本作 explosive 锁 1 层（PORTING_PARITY #13「近似一次性」），
## 2026-07-29 清理不可达的 per-level 缩放，取固定值（P2-8）。
## 遍历副本防 take_damage→die→注销注册表造成的遍历中突变。
## E07 修正：注册表含 Enemy 与 Boss（Boss extends Area2D 非 Enemy 子类），as Enemy 对 Boss
## cast 得 null 恰落在 e == null 跳过——Boss 排除为有意设计（与 _on_area_entered 直击路径一致）。
func _explode() -> void:
	for node in GameState.enemies.duplicate():
		var e := node as Enemy  # C20：静态类型化访问 is_boss/take_damage（Boss 由 null 排除）
		if e == null or e.is_boss():
			continue
		if e.global_position.distance_to(global_position) <= EXPLOSIVE_RADIUS:
			e.take_damage(EXPLOSIVE_DAMAGE)
	Explosion.spawn_at(get_parent(), global_position, 0.6)
	GameState.play_sfx(GameState.SFX_EXPLOSION, -6.0)


## 导弹溅射（母舰导弹）：半径内全部敌人（含主目标与 Boss）追加固定伤害，×1/3 分随 score_scale。
## E01 修复：注册表含 Boss（Boss 非 Enemy 子类），as Enemy 对 Boss cast 得 null 使溅射伤害
## 静默丢失（直击 80 仍有效）；改 Variant 鸭子调用 take_damage(amount, score_scale)——
## Enemy/Boss 均实现同签名（与 laser_weapon._damage_tick「含 Boss」同模式）。
func _splash() -> void:
	for node in GameState.enemies.duplicate():
		if not (node is Area2D):
			continue
		if node.global_position.distance_to(global_position) <= splash_radius:
			node.take_damage(splash_damage, score_scale)
	Explosion.spawn_at(get_parent(), global_position, 0.8)
	GameState.play_sfx(GameState.SFX_EXPLOSION, -6.0)


func _on_area_entered(area: Area2D) -> void:
	# 2026-08-03 审计：同物理帧重复命中守卫——monitoring 关闭延迟到帧末，首命中已回收的
	# 弹（池化 deactivate 或直实例化 queue_free）仍会收到后续 area_entered；已消失的弹不再结算
	if not _active and (_pool != null or is_queued_for_deletion()):
		return
	if is_player_bullet:
		if area.is_in_group("enemy"):
			# crit_shot 暴击（2026-08-04）：层数 × 基础概率判定，命中 ×倍率伤害（玩家侧缓存经 player_ref）
			var hit_damage := damage
			var p_ref := GameState.player_ref as Player
			if p_ref != null and p_ref.crit_chance > 0.0 and randf() < p_ref.crit_chance:
				hit_damage = int(damage * p_ref.crit_multiplier)
			area.take_damage(hit_damage, score_scale)
			# 原作爆炸弹对 Boss 路径完全不触发（无爆炸视觉/溅射），仅直击；
			# TurretBattery/FormationCraft 为 Area2D 无 is_boss() 方法：先查方法存在（防命中时报错+子弹不销毁），
			# 再取返回值语义——Boss/精英 is_boss()==true 不爆炸，普通 Enemy 返回 false 爆炸
			if explosive and (not area.has_method("is_boss") or not area.is_boss()):
				_explode()
			if splash_damage > 0:
				_splash()
			if pierce > 0:
				pierce -= 1
			else:
				_despawn()
	elif area.is_in_group("player_hitbox"):
		# 机制一（2026-08-03）：受击宽限帧——进入 Hitbox 不立即结算，暂缓 GRACE_PERIOD 秒，
		# 窗口内离开（area_exited 取消）视为擦过边缘不计伤；停留超窗才走既有结算链路。
		# 消灭「回放里明明躲过却被判死」的 ghost hit；take_damage 内部守卫（无敌/闪避/单帧）零改动。
		_start_grace_check(area)


## 机制一：弹离开玩家 Hitbox（窗口内擦过）→ 取消宽限 Timer，不计伤，弹继续飞行
func _on_area_exited(area: Area2D) -> void:
	if area.is_in_group("player_hitbox"):
		_cancel_grace()


## 机制一：启动宽限窗口（事件驱动，无候选表/零逐帧轮询）。
## 一次性 Timer 挂子弹下随场景释放（AGENTS 协程纪律）；同一弹重复进入忽略（已挂窗口）。
func _start_grace_check(hitbox: Area2D) -> void:
	if _grace_timer != null and not _grace_timer.is_stopped():
		return
	_grace_hitbox = hitbox
	if _grace_timer == null:
		_grace_timer = Timer.new()
		_grace_timer.one_shot = true
		_grace_timer.timeout.connect(_on_grace_timeout)
		add_child(_grace_timer)
	_grace_timer.wait_time = GRACE_PERIOD
	_grace_timer.start()


func _cancel_grace() -> void:
	if _grace_timer != null:
		_grace_timer.stop()
	_grace_hitbox = null  # 2026-08-03 审计：回收弹不携带旧 Hitbox 悬空引用（与 _cancel_grace 成对）


## 宽限到期：单次 overlaps 复核——仍与 Hitbox 重叠才结算（每颗进入的弹至多 1 次查询，
## 非逐帧轮询）；已离开（窗口边界情形）放弃结算，弹按原路径继续飞行。
func _on_grace_timeout() -> void:
	if not _active or _grace_hitbox == null or not is_instance_valid(_grace_hitbox):
		return
	var hitbox := _grace_hitbox
	_grace_hitbox = null
	if not overlaps_area(hitbox):
		return
	# 既有受击结算链路（含无敌/闪避/单帧守卫、受击清弹、致死高亮 _linger_fatal）
	var player := GameState.player_ref as Player
	if player != null and player.take_damage(float(damage), global_position):
		# P2-10（竞品调研）：致死一击弹丸高亮残留，让玩家看清"是什么杀了自己"
		if player.is_dead():
			_linger_fatal()
		else:
			_despawn()


## 机制二（2026-08-03）：擦弹单次计数——同一敌弹至多计 1 次（池化 activate 复位）。
## 返回 true 表示本次计入（调用方据此加分与触发反馈）。
func try_graze() -> bool:
	if _graze_done:
		return false
	_graze_done = true
	return true


## 机制四（2026-08-03）：弧光弹反——敌弹被盾区弹反：转玩家弹、镜面反射
## （以盾法线=机头前方为对称轴，即 2D 下 direction.y 取反）、×REFLECT_SPEED_MULT 返回、
## 伤害 ×REFLECT_DAMAGE_MULT；追踪语义终止（反射后直行）。O(1) 阵营翻转，零池新增。
## 弹反后不可能再伤害玩家（转玩家弹层），并取消受击宽限（防反射瞬间同帧重叠误结算）。
func reflect() -> void:
	is_player_bullet = true
	direction = Vector2(direction.x, -direction.y)
	speed *= REFLECT_SPEED_MULT
	damage = maxi(1, int(roundf(damage * REFLECT_DAMAGE_MULT)))
	homing = false
	homing_target = null
	_cancel_grace()
	_apply_faction()
