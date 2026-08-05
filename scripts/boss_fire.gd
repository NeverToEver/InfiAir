class_name BossFire
extends RefCounted
## A3 拆分：Boss 弹幕发射器（docs/AUDIT_VAULT.md A3）。
## 纯发射逻辑，不持 Boss 状态；位置经 boss 参数、出弹点偏移/机体缩放经注入字段。
## Boss / BossAttacks / EnrageSequence 共用本发射器，避免跨类私有访问（A1 约束）。

## 出弹点偏移（Boss._ready 注入：MUZZLE_OFFSET = 设计值 × world_scale）
var muzzle_offset: float = 0.0
## 全局机体缩放（Boss._ready 注入 _ws），用于随机体特效的偏移缩放
var world_scale: float = 1.0

## cross 攻击起始角（随波次进动，BossFire 内维护）
var _cross_angle: float = 0.0


## 面向玩家的方向（player 为空回退 Vector2.DOWN）
static func player_dir(from: Node2D) -> Vector2:
	if GameState.player_ref != null:
		var dir := (GameState.player_ref.global_position - from.global_position).normalized()
		return dir if dir != Vector2.ZERO else Vector2.DOWN  # G026：圆心重合时回退
	return Vector2.DOWN


func fire_fan(boss: Node2D, p_count: int, speed: float, damage: int) -> void:
	var base_dir := player_dir(boss)
	var half := float(p_count - 1) * 0.5
	for i in p_count:
		var dir := base_dir.rotated(deg_to_rad(20.0 * (float(i) - half)))
		var b: Bullet = GameState.bullet_pool.fire(dir, speed, damage, false)
		if b == null:
			continue  # P2-3：同屏敌弹硬上限，跳过本次发射（槽位剩余照常）
		b.position = boss.position + dir * muzzle_offset


func fire_homing(boss: Node2D, p_offset: Vector2, speed: float, damage: int) -> void:
	var b: Bullet = GameState.bullet_pool.fire(Vector2.DOWN, speed, damage, false, true, 1.5)
	if b == null:
		return  # P2-3：同屏敌弹硬上限
	b.position = boss.position + p_offset * world_scale


## 狙击弹：p_dir 为零向量时自机狙（保留旧语义），否则沿 telegraph 锁定方向
func fire_sniper(boss: Node2D, p_dir: Vector2, speed: float, damage: int) -> void:
	var dir := p_dir if p_dir != Vector2.ZERO else player_dir(boss)
	var b: Bullet = GameState.bullet_pool.fire(dir, speed, damage, false)
	if b == null:
		return  # P2-3：同屏敌弹硬上限
	b.position = boss.position + dir * muzzle_offset


func fire_cross(boss: Node2D, speed: float, damage: int) -> void:
	for i in 4:
		var dir := Vector2.RIGHT.rotated(_cross_angle + float(i) * PI / 2.0)
		var b: Bullet = GameState.bullet_pool.fire(dir, speed, damage, false)
		if b == null:
			continue  # P2-3：同屏敌弹硬上限
		b.position = boss.position + dir * muzzle_offset
	_cross_angle += deg_to_rad(15.0)


## 重弹（蓄力重炮/狂暴齐射/猎杀狙击共用）：高亮加粗外观
func fire_heavy(boss: Node2D, p_dir: Vector2, p_speed: float, p_damage: int) -> void:
	var b: Bullet = GameState.bullet_pool.fire(p_dir, p_speed, p_damage, false)
	if b == null:
		return  # P2-3：同屏敌弹硬上限
	b.position = boss.position + p_dir * muzzle_offset
	var poly := b.sprite_node()  # C24：缓存引用，不再每次 get_node
	if poly != null:
		poly.scale = Vector2(2.4, 2.4)
		poly.self_modulate = Color(1.0, 0.6, 0.3)  # P0-3：Sprite2D 无 color，用 self_modulate


## 环弹（差异化狂暴各型共用）：meta=enrage_ring（与快照环弹同标记）
func fire_ring(boss: Node2D, p_count: int, p_speed: float, p_damage: int, p_offset: float) -> void:
	var count := maxi(2, p_count)  # H15：cfg 直读为 0 时 float(i)/float(p_count) 除零 NaN 方向
	for i in count:
		var dir := Vector2.RIGHT.rotated(p_offset + TAU * float(i) / float(count))
		var b: Bullet = GameState.bullet_pool.fire(dir, p_speed, p_damage, false)
		if b == null:
			continue  # P2-3：同屏敌弹硬上限
		b.position = boss.position + dir * muzzle_offset
		b.set_meta("bullet_type", &"enrage_ring")


## 快照激光 + 环形慢弹（狂暴进入一次性齐射 / RELEASE 回退路径共用）
func fire_enrage_wave(
	boss: Node2D, laser_speed: float, ring_speed: float, laser_damage: int, ring_damage: int, laser_count: int, ring_count: int
) -> void:
	# H15：count 下限钳制——cfg 直读为 0/负时防空齐射、float(i)/float(ring_count) 除零 NaN 方向
	var lasers := maxi(2, laser_count)
	var rings := maxi(2, ring_count)
	var aim := player_dir(boss)
	var side := aim.orthogonal()
	for i in lasers:
		var laser: Bullet = GameState.bullet_pool.fire(aim, laser_speed, laser_damage, false)
		if laser == null:
			continue  # P2-3：同屏敌弹硬上限
		laser.position = boss.position + aim * muzzle_offset + side * (float(i) - 1.5) * 44.0 * world_scale
		laser.set_meta("bullet_type", &"laser")
		# 细长高亮快速弹（与敌机 laser 弹同表现，polygon 尖端朝 +x 即飞行方向）
		var poly := laser.sprite_node()  # C24：缓存引用，不再每次 get_node
		if poly != null:
			poly.scale = Vector2(2.2, 0.55)
			poly.self_modulate = Color(1.0, 0.85, 0.35)  # P0-3：Sprite2D 无 color，用 self_modulate
	for i in rings:
		var dir := Vector2.RIGHT.rotated(TAU * float(i) / float(rings))
		var b: Bullet = GameState.bullet_pool.fire(dir, ring_speed, ring_damage, false)
		if b == null:
			continue  # P2-3：同屏敌弹硬上限
		b.position = boss.position + dir * muzzle_offset
		b.set_meta("bullet_type", &"enrage_ring")


## 弹幕墙（三型 P2）：arc_deg 度扇形 count 槽位，留 2 个相邻缺口；
## 缺口方位避开自机当前方位 ±30°（无可行槽位时退化为离自机最远的槽，保证理论上可躲）
func fire_bullet_wall(boss: Node2D, count: int, speed: float, damage: int, arc_deg: float) -> void:
	var slots := maxi(2, count)  # H15：cfg 直读为 0/1 时 float(count-1) 除零 NaN 方向
	var arc := deg_to_rad(arc_deg)
	var base := Vector2.DOWN.angle()
	var to_player := player_dir(boss).angle()
	var min_gap := deg_to_rad(30.0)
	var slot_angle := func(i: int) -> float: return base - arc * 0.5 + arc * float(i) / float(slots - 1)
	var candidates: Array[int] = []
	for g in slots - 1:
		if (
			absf(angle_difference(slot_angle.call(g), to_player)) > min_gap
			and absf(angle_difference(slot_angle.call(g + 1), to_player)) > min_gap
		):
			candidates.append(g)
	var gap_start := -1
	if candidates.is_empty():
		var best_dist := -1.0
		for g in slots - 1:
			var d := minf(absf(angle_difference(slot_angle.call(g), to_player)), absf(angle_difference(slot_angle.call(g + 1), to_player)))
			if d > best_dist:
				best_dist = d
				gap_start = g
	else:
		gap_start = candidates[randi() % candidates.size()]
	for i in slots:
		if i == gap_start or i == gap_start + 1:
			continue
		var dir := Vector2.from_angle(slot_angle.call(i))
		var b: Bullet = GameState.bullet_pool.fire(dir, speed, damage, false)
		if b == null:
			continue  # P2-3：同屏敌弹硬上限
		b.position = boss.position + dir * muzzle_offset
