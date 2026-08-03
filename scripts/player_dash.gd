class_name PlayerDash
extends RefCounted
## A8 拆分：玩家相位冲刺（docs/AUDIT_VAULT.md A8）。
## Dash 状态机与计时；经 Player 属性转发（_dashing 等语法不变）与公开方法交互。
## 需要解锁 buff（Player.dash_unlocked）且耗 25% 满值燃料。

var dashing: bool = false
var dash_timer: float = 0.0
var dash_dir: Vector2 = Vector2.ZERO
var dash_cooldown: float = 0.0
var afterimage_timer: float = 0.0

var DASH_DISTANCE := 200.0
var DASH_TIME := 0.25
var DASH_COOLDOWN := 4.0
var AFTERIMAGE_INTERVAL := 0.08


func configure(distance: float, time: float, cooldown: float, afterimage_interval: float) -> void:
	DASH_DISTANCE = distance
	DASH_TIME = time
	DASH_COOLDOWN = cooldown
	AFTERIMAGE_INTERVAL = afterimage_interval


func is_dashing() -> bool:
	return dashing


func cooldown_remaining() -> float:
	return dash_cooldown


## 冷却递减（Player._physics_process 每帧调用）
func tick_cooldown(delta: float) -> void:
	dash_cooldown = maxf(dash_cooldown - delta, 0.0)


## 启动冲刺（Player 门面已校验 unlock/冷却/未冲刺/燃料；扣 25% 满值燃料）
func start(input_dir: Vector2, player: Player) -> void:
	dashing = true
	dash_timer = DASH_TIME
	player.set_fuel(maxf(player.fuel_amount() - player.dash_fuel_cost(), 0.0))
	if input_dir != Vector2.ZERO:
		dash_dir = input_dir.normalized()
	else:
		# K04：无方向输入时向虚拟准星方向冲刺（aim_point 为键鼠+右摇杆统一平滑点）——
		# 原实现取真实鼠标位置，纯手柄玩家鼠标停在任意处，冲刺方向与机头/瞄准无关
		dash_dir = (player.aim_point() - player.global_position).normalized()
		if dash_dir == Vector2.ZERO:
			dash_dir = Vector2.UP
	dash_cooldown = player.dash_cooldown_max()
	afterimage_timer = 0.0
	GameState.play_sfx(GameState.SFX_DASH)


## 冲刺移动驱动（残影生成/位移/回弹；尾焰由 Player 侧保留视觉）
func update_move(delta: float, player: Player) -> void:
	dash_timer -= delta
	afterimage_timer -= delta
	if afterimage_timer <= 0.0:
		afterimage_timer = AFTERIMAGE_INTERVAL
		player.spawn_afterimage()
	player.velocity = dash_dir * (DASH_DISTANCE / DASH_TIME)
	player.move_and_slide()
	player.position = player.clamp_to_view(player.position)
	if dash_timer <= 0.0:
		dashing = false
		GameState.play_sfx(GameState.SFX_DASH, -3.0)
