class_name Mothership
extends Area2D
## 母舰补给点：从顶部降入悬停，玩家进入对接区后回满生命与燃料，随后离场。

signal resupplied

enum State { DESCEND, HOVER, LEAVE }

const DESCEND_SPEED := 100.0
const LEAVE_SPEED := 140.0
const HOVER_Y := 270.0
const DOCK_INVINCIBLE := 2.0
const LIVES_CAP := 6.0

var _state: State = State.DESCEND


func _ready() -> void:
	($DockZone as Area2D).area_entered.connect(_on_dock_area_entered)


func _physics_process(delta: float) -> void:
	match _state:
		State.DESCEND:
			position.y += DESCEND_SPEED * delta
			if position.y >= HOVER_Y:
				position.y = HOVER_Y
				_state = State.HOVER
		State.HOVER:
			pass
		State.LEAVE:
			position.y -= LEAVE_SPEED * delta
			if position.y < -260.0:
				queue_free()


func _on_dock_area_entered(area: Area2D) -> void:
	if _state != State.HOVER or not area.is_in_group("player_hitbox"):
		return
	_resupply(area.get_parent() as Player)


func _resupply(player: Player) -> void:
	# 回满生命（基础 3 + 额外生命 buff 层数，封顶 6）
	var full := minf(3.0 + GameState.buff_count(&"extra_life"), LIVES_CAP)
	if GameState.lives < full:
		GameState.heal(full - GameState.lives)
	player.refill_fuel()
	player._invincible = maxf(player._invincible, DOCK_INVINCIBLE)
	GameState.play_sfx(GameState.SFX_RESUPPLY)
	GameState.shake(4.0)
	var hud := get_tree().get_first_node_in_group("hud")
	if hud != null:
		hud.show_popup("补给完成", global_position + Vector2(0.0, 120.0))
	_state = State.LEAVE
	resupplied.emit()
