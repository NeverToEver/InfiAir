class_name Pickup
extends Area2D
## 精英掉落的拾取物：发光六边形，缓慢下落，玩家 150px 内磁铁吸附。
## effect：0=回 1 命（满命 +50 分）/ 1=满燃料 / 2=+100 分；-1 表示随机。

const FALL_SPEED := 60.0
const MAGNET_RANGE := 150.0
const MAGNET_SPEED := 320.0
const LIVES_CAP := 6.0

var effect: int = -1


func _ready() -> void:
	if effect < 0:
		effect = randi() % 3
	area_entered.connect(_on_area_entered)


func _physics_process(delta: float) -> void:
	var player := _get_player()
	if player != null:
		var to_player: Vector2 = player.global_position - global_position
		if to_player.length() < MAGNET_RANGE:
			position += to_player.normalized() * MAGNET_SPEED * delta
			return
	position.y += FALL_SPEED * delta
	if position.y > 1140.0:
		queue_free()


func _get_player() -> Node2D:
	var players := get_tree().get_nodes_in_group("player")
	return players[0] as Node2D if players.size() > 0 else null


func _on_area_entered(area: Area2D) -> void:
	if not area.is_in_group("player_hitbox"):
		return
	var hud := get_tree().get_first_node_in_group("hud")
	match effect:
		0:
			if GameState.lives >= LIVES_CAP:
				GameState.add_score(50)
				hud.show_popup("+50 分", global_position)
			else:
				GameState.heal(1.0)
				hud.show_popup("生命 +1", global_position)
		1:
			var player := _get_player()
			if player != null:
				player.refill_fuel()
			hud.show_popup("燃料充满", global_position)
		2:
			GameState.add_score(100)
			hud.show_popup("+100 分", global_position)
	GameState.play_sfx(GameState.SFX_BUFF_PICK, -4.0)
	queue_free()
