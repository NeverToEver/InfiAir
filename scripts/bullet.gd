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

var _homing_elapsed: float = 0.0
var _pool: Node = null
## 池活跃标记：回收的延迟调用（monitoring=false / reparent）在重激活后必须失效
var _active: bool = false
## 回收 reparent 保护：4.6 实测 reparent 也会触发 _exit_tree，置位期间禁止 forget 误清池清单
var _repooling: bool = false

@onready var _polygon: Polygon2D = $Polygon2D


## 兼容路径：直接实例化时 setup() 后由 _ready() 应用阵营外观。
func setup(
	p_direction: Vector2,
	p_speed: float,
	p_damage: int,
	p_is_player: bool,
	p_homing: bool = false,
	p_homing_time: float = 0.0
) -> void:
	direction = p_direction.normalized()
	speed = p_speed
	# 敌方子弹伤害随对局进程 ramp（Boss 击杀难度乘数驱动，GameState.enemy_damage_ramp）
	damage = p_damage if p_is_player else maxi(1, int(roundf(p_damage * GameState.enemy_damage_ramp())))
	is_player_bullet = p_is_player
	homing = p_homing
	homing_time = p_homing_time


## 池化路径：激活并重置全部状态（含上一任使用者的外观/标记）。
func activate(
	p_direction: Vector2,
	p_speed: float,
	p_damage: int,
	p_is_player: bool,
	p_homing: bool = false,
	p_homing_time: float = 0.0
) -> void:
	setup(p_direction, p_speed, p_damage, p_is_player, p_homing, p_homing_time)
	_active = true
	_homing_elapsed = 0.0
	homing_target = null
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
	EXPLOSIVE_RADIUS = GameState.cfg("buffs.explosive.radius_per_level", EXPLOSIVE_RADIUS)
	EXPLOSIVE_DAMAGE = GameState.cfg("buffs.explosive.damage_per_level", EXPLOSIVE_DAMAGE)
	VISUAL_SCALE = GameState.cfg("effects.bullet_visual_scale", VISUAL_SCALE) * GameState.world_scale
	ENEMY_VISUAL_SCALE = GameState.cfg("effects.enemy_bullet_visual_scale", ENEMY_VISUAL_SCALE) * GameState.world_scale
	# 碰撞半径：设计值 6 × 全局缩放（幂等赋值；池化实例共享 shape 也只写同值）
	(($CollisionShape2D as CollisionShape2D).shape as CircleShape2D).radius = 6.0 * GameState.world_scale
	_apply_faction()


## C24 修复：Polygon2D 懒加载缓存——池化复用生命周期内引用稳定，
## 调用方（boss_fire/enemy/mothership）不再每次发射 get_node("Polygon2D")
var _polygon_cache: Polygon2D = null


func polygon_node() -> Polygon2D:
	if _polygon_cache == null:
		_polygon_cache = get_node_or_null("Polygon2D") as Polygon2D
	return _polygon_cache


func _exit_tree() -> void:
	# 被外部 queue_free（清场/测试/场景重载）时通知池移除引用；
	# 池内 reparent 也会经过此回调（_repooling 置位），不算离开池
	if _pool != null and not _repooling:
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


func _despawn() -> void:
	if _pool != null and is_instance_valid(_pool):
		_pool.release(self)
	else:
		queue_free()


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
				# 距离越近转向越急：螺旋收敛而非恒定半径环绕
				var rate := homing_turn_rate * (1.0 + HOMING_SNAP_RADIUS * 2.0 / dist)
				var target_angle := lerp_angle(direction.angle(), to_target.angle(), rate * delta)
				direction = Vector2.RIGHT.rotated(target_angle)
				rotation = target_angle
	elif homing and _homing_elapsed < homing_time:
		_homing_elapsed += delta
		if GameState.player_ref != null:
			var new_angle := lerp_angle(
				direction.angle(),
				(GameState.player_ref.global_position - global_position).angle(),
				homing_turn_rate * delta
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
	if is_player_bullet:
		if area.is_in_group("enemy"):
			area.take_damage(damage, score_scale)
			# 原作爆炸弹对 Boss 路径完全不触发（无爆炸视觉/溅射），仅直击
			if explosive and not area.is_boss():
				_explode()
			if splash_damage > 0:
				_splash()
			if pierce > 0:
				pierce -= 1
			else:
				_despawn()
	elif area.is_in_group("player_hitbox"):
		# 命中生效才销毁；无敌/单帧已结算/闪避则穿过（对齐原作 single-hit 语义）
		# 补传弹丸位置作伤害源方向（Meta HUD 定向波纹，D8）
		# A1：用注册表引用替代父节点硬强转（Hitbox 由 Player 维护，二者等价）
		var player := GameState.player_ref as Player
		if player != null and player.take_damage(float(damage), global_position):
			_despawn()
