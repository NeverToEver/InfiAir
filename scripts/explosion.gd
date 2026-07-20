class_name Explosion
extends GPUParticles2D
## 一次性爆炸粒子（主火花 + 飞散碎片双发射器），纯代码构建，发射完毕自动释放。

var _debris: GPUParticles2D


static func spawn_at(parent: Node, pos: Vector2, p_scale: float = 1.0) -> void:
	var e := Explosion.new()
	e.position = pos
	e.scale = Vector2.ONE * p_scale
	parent.add_child(e)


## Boss 多段爆炸序列：连续小爆炸 + 最终大爆炸 + 震动。
static func spawn_boss_sequence(parent: Node, pos: Vector2) -> void:
	GameState.play_sfx(GameState.SFX_EXPLOSION_BIG)
	GameState.shake(20.0)
	for i in 6:
		if not is_instance_valid(parent):
			return
		var offset := Vector2(randf_range(-130.0, 130.0), randf_range(-90.0, 90.0))
		Explosion.spawn_at(parent, pos + offset, randf_range(0.9, 1.5))
		GameState.shake(8.0)
		await parent.get_tree().create_timer(0.12).timeout
	if is_instance_valid(parent):
		Explosion.spawn_at(parent, pos, 3.0)
		GameState.shake(24.0)


func _init() -> void:
	amount = 24
	lifetime = 0.6
	one_shot = true
	explosiveness = 0.9
	var mat := ParticleProcessMaterial.new()
	mat.direction = Vector3(0.0, -1.0, 0.0)
	mat.spread = 180.0
	mat.initial_velocity_min = 120.0
	mat.initial_velocity_max = 320.0
	mat.gravity = Vector3.ZERO
	mat.damping_min = 60.0
	mat.damping_max = 140.0
	mat.scale_min = 2.0
	mat.scale_max = 5.0
	mat.color = Color(1.0, 0.6, 0.15)
	process_material = mat
	finished.connect(queue_free)

	# 碎片发射器：少量、更大、更慢、寿命更长
	_debris = GPUParticles2D.new()
	_debris.amount = 10
	_debris.lifetime = 0.9
	_debris.one_shot = true
	_debris.explosiveness = 0.85
	var dm := ParticleProcessMaterial.new()
	dm.direction = Vector3(0.0, -1.0, 0.0)
	dm.spread = 180.0
	dm.initial_velocity_min = 200.0
	dm.initial_velocity_max = 420.0
	dm.gravity = Vector3.ZERO
	dm.damping_min = 100.0
	dm.damping_max = 220.0
	dm.scale_min = 3.0
	dm.scale_max = 7.0
	dm.color = Color(0.9, 0.4, 0.1)
	_debris.process_material = dm
	add_child(_debris)


func _ready() -> void:
	emitting = true
	_debris.emitting = true
