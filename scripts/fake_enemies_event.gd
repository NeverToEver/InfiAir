class_name FakeEnemiesEvent
extends FogEvent
## 伪敌机事件：生成无伤害/无碰撞的幽灵机群（纯视觉干扰，单机外观/移动见 FakeEnemy）。
## 配置：balance.json fog_events.fake_enemies（count / spawn_interval）。
## 健壮性：_on_start 缓存容器并判空——context 缺 fake_container 时降级空转（不崩，
## 事件仍走完 duration，由 GameEvent.end 幂等清理）。

var _count := 5
var _spawn_interval := 0.8
var _fakes: Array[Node] = []
var _container: Node2D = null


func event_id() -> StringName:
	return &"fake_enemies"


func _on_start() -> void:
	_container = fake_container()
	if _container == null:
		push_warning("FakeEnemiesEvent：context 缺 fake_container，事件降级空转")
		return
	_count = maxi(int(GameState.cfg("fog_events.fake_enemies.count", _count)), 1)
	_spawn_interval = maxf(float(GameState.cfg("fog_events.fake_enemies.spawn_interval", _spawn_interval)), 0.0)
	for i in _count:
		_spawn_fake(i)


## 生成一只幽灵敌机：顶缘随机 x、错峰入场（index × spawn_interval）
func _spawn_fake(index: int) -> void:
	var fake := FakeEnemy.new()
	var view := GameState.view_world_rect()
	fake.position = Vector2(randf_range(view.position.x + 80.0, view.end.x - 80.0), view.position.y - randf_range(20.0, 260.0))
	fake.enter_delay = float(index) * _spawn_interval
	_container.add_child(fake)
	_fakes.append(fake)


func _on_end() -> void:
	for fake in _fakes:
		if is_instance_valid(fake):
			fake.queue_free()  # 幽灵直接消失（事件结束视觉自洽：机群解体）
	_fakes.clear()


## 已生成的伪敌机（测试/诊断；manager.spawned_fakes 委托到本方法）
func spawned_fakes() -> Array:
	return _fakes.duplicate()
