class_name Explosion
extends GPUParticles2D
## 一次性爆炸粒子，纯代码构建，发射完毕后自动释放。


static func spawn_at(parent: Node, pos: Vector2, p_scale: float = 1.0) -> void:
	var e := Explosion.new()
	e.position = pos
	e.scale = Vector2.ONE * p_scale
	parent.add_child(e)


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


func _ready() -> void:
	emitting = true
