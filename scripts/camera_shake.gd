extends Camera2D
## 屏幕震动：监听 GameState.screen_shake，随机偏移 + 指数衰减。
## process_mode 需为 Always（场景文件中设置），保证暂停时震动也能衰减结束。

var DECAY := 6.0

var _strength: float = 0.0


func _ready() -> void:
	# C22：is_connected 守卫，相机重入树（场景重载/重挂）不重复连接
	if not GameState.screen_shake.is_connected(_on_screen_shake):
		GameState.screen_shake.connect(_on_screen_shake)
	DECAY = maxf(GameState.cfg("effects.shake.decay", DECAY), 0.001)  # H15：decay=0 震动永不衰减


func _exit_tree() -> void:
	if GameState.screen_shake.is_connected(_on_screen_shake):
		GameState.screen_shake.disconnect(_on_screen_shake)


func _process(delta: float) -> void:
	if _strength > 0.1:
		offset = Vector2(randf_range(-1.0, 1.0), randf_range(-1.0, 1.0)) * _strength
		_strength = lerpf(_strength, 0.0, DECAY * delta)
	else:
		_strength = 0.0
		offset = Vector2.ZERO


func _on_screen_shake(strength: float) -> void:
	_strength = maxf(_strength, strength)
