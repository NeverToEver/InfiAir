extends Camera2D
## 屏幕震动：监听 GameState.screen_shake，随机偏移 + 指数衰减。
## process_mode 需为 Always（场景文件中设置），保证暂停时震动也能衰减结束。

const DECAY := 6.0

var _strength: float = 0.0


func _ready() -> void:
	GameState.screen_shake.connect(_on_screen_shake)


func _process(delta: float) -> void:
	if _strength > 0.1:
		offset = Vector2(randf_range(-1.0, 1.0), randf_range(-1.0, 1.0)) * _strength
		_strength = lerpf(_strength, 0.0, DECAY * delta)
	else:
		_strength = 0.0
		offset = Vector2.ZERO


func _on_screen_shake(strength: float) -> void:
	_strength = maxf(_strength, strength)
