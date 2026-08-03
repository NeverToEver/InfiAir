class_name FormationBomb
extends Area2D
## 轰炸编队事件·炸弹（docs/FORMATION_STRIKE_EVENT.md 第 4.2 节）：
## 引信制下落弹（不走命中即毁）：投放时继承编队水平速度 ×0.35 + 垂直下落，
## 引信倒计时期间弹体脉冲辉光（8Hz）、警示环随剩余引信收缩（0.9×AoE → 0.15×AoE），
## 引爆对 player_hitbox 做距离判定（无敌/闪避由 Player.take_damage 语义处理），
## 只伤玩家不伤敌机（与敌方弹丸语义一致）；出界/引爆后 queue_free。

const RING_SEGMENTS := 24

## 投放参数（事件 setup 注入；数值源 formation_strike_event.*）
var velocity: Vector2 = Vector2(0.0, 300.0)
var fuse: float = 1.2
var damage: int = 20
var aoe_radius: float = 120.0

var _fuse_left: float = 1.2
var _t: float = 0.0  # 脉冲相位
var _body: Polygon2D
var _ring: Line2D


## A7：测试/诊断白盒断言经公开接口
func ring() -> Line2D:
	return _ring


## setup() 在入树/_ready() 之前调用
func setup(p_velocity: Vector2, p_fuse: float, p_damage: int, p_radius: float) -> void:
	velocity = p_velocity
	fuse = p_fuse
	damage = p_damage
	aoe_radius = p_radius


func _init() -> void:
	collision_layer = 8  # 第 4 层：enemy_bullet（语义同敌弹，但不接命中即毁）
	# collision_mask = 1（第 1 层 player）：纯语义文档——引信制炸弹对 player_hitbox 用距离判定结算
	# （见 _detonate），无 body_entered/area_entered 碰撞信号连接，此掩码不参与任何命中判定
	collision_mask = 1
	_body = Polygon2D.new()
	# 机体尺寸族：设计值 × world_scale（AoE 半径 aoe_radius 为游戏性范围，不缩）
	var ws: float = GameState.world_scale
	_body.polygon = PackedVector2Array(
		[
			Vector2(-7.0, -12.0) * ws,
			Vector2(7.0, -12.0) * ws,
			Vector2(9.0, 6.0) * ws,
			Vector2(0.0, 14.0) * ws,
			Vector2(-9.0, 6.0) * ws,
		]
	)
	_body.color = Color(1.0, 0.45, 0.15)
	add_child(_body)
	var shape := CollisionShape2D.new()
	var circle := CircleShape2D.new()
	circle.radius = 10.0 * ws
	shape.shape = circle
	add_child(shape)
	# 警示环：单位圆一次构建，运行时只改 scale（半径 0.9×AoE → 0.15×AoE），零堆分配
	_ring = Line2D.new()
	var pts := PackedVector2Array()
	for i in RING_SEGMENTS + 1:
		var a := float(i) * TAU / float(RING_SEGMENTS)
		pts.append(Vector2(cos(a), sin(a)))
	_ring.points = pts
	_ring.width = 3.0
	_ring.default_color = Color(1.0, 0.3, 0.15, 0.85)
	add_child(_ring)


func _ready() -> void:
	_fuse_left = fuse
	_ring.scale = Vector2.ONE * aoe_radius * 0.9


func _process(delta: float) -> void:
	_t += delta
	position += velocity * delta
	# 脉冲辉光（红橙 8Hz）
	_body.modulate.a = 0.55 + 0.45 * absf(Enemy.sin_fast(_t * PI * 8.0))
	_fuse_left -= delta
	if _fuse_left <= 0.0:
		_detonate()
		return
	# 警示环随引信剩余时间收缩
	var frac := clampf(_fuse_left / fuse, 0.0, 1.0)
	_ring.scale = Vector2.ONE * aoe_radius * lerpf(0.15, 0.9, frac)
	if not GameState.view_world_rect(80.0).has_point(position):
		queue_free()


## 引爆：爆炸 + 音效 + 震屏；对 player_hitbox 距离判定（≤ AoE 半径才结算，
## 无敌/单帧已结算/闪避由 Player.take_damage 返回 false 挡掉）
func _detonate() -> void:
	Explosion.spawn_at(get_parent(), global_position, 0.9)
	GameState.play_sfx(GameState.SFX_EXPLOSION)
	GameState.shake(GameState.cfg("effects.shake.enemy_die", 5.0))
	var hitbox := GameState.player_hitbox
	var player := GameState.player_ref as Player
	if hitbox != null and is_instance_valid(hitbox) and player != null:
		if hitbox.global_position.distance_to(global_position) <= aoe_radius:
			# K08：A1 同款遗漏——原 (hitbox.get_parent() as Player) 硬强转，Player 节点结构变动即
			# null 调用崩溃；改经注册表引用（与 bullet.gd 命中结算同口径）
			player.take_damage(float(damage), global_position)
	queue_free()
