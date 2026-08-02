class_name EnemyMoveStrategy
extends RefCounted
## A4a 拆分：敌机移动策略（docs/AUDIT_VAULT.md A4）。
## 各策略自包含的纯位置计算块；共享只读上下文经 ctx 传入（view/mdelta/speed/time/phase/spawn_x/anchor_y/hovering/player），
## 唯一副作用写 enemy.position 与少量公开 setter。不访问 Enemy 私有字段（A1 约束）。
## 共享悬停常量（HOVER_BOB_* 等）经构造 params 注入（Enemy._ready 从 balance 缓存值传入）。

## 共享悬停参数（构造注入，缺省值 = balance.json enemies.* 默认）
var _hover_bob_amp: float = 12.0
var _hover_bob_freq: float = 2.0
var _hover_sway_amp: float = 34.0
var _hover_sway_freq: float = 1.2
var _spiral_drift_amp: float = 56.0
var _spiral_drift_freq: float = 0.7
var _spiral_radius: float = 50.0
var _aggressive_chase_speed: float = 140.0


func _init(params: Dictionary = {}) -> void:
	for k in params.keys():
		if k in [
			"hover_bob_amp", "hover_bob_freq", "hover_sway_amp", "hover_sway_freq",
			"spiral_drift_amp", "spiral_drift_freq", "spiral_radius", "aggressive_chase_speed",
		]:
			set(k, params[k])


func update(_delta: float, _enemy: Enemy, _ctx: Dictionary) -> void:
	pass


## 共享悬停 y：绕锚点垂直微浮（相位随机错开全波机械感）
func _hover_y(ctx: Dictionary) -> float:
	return ctx.anchor_y + Enemy.sin_fast(ctx.time * _hover_bob_freq + ctx.phase) * _hover_bob_amp


## 悬停转换查询：dive 冲刺期例外（基类 false）
func is_diving() -> bool:
	return false


## 悬停转换查询：spiral 以绕转中心为准（基类 -1 = 用 position.y）
func hover_reference_y() -> float:
	return -1.0


## 重激活/出生状态复位（Enemy._ready / reactivate 调用）
func reset(_enemy: Enemy) -> void:
	pass


## straight / hover 合成策略：直线下压 + 悬停水平摇摆（共享逻辑）
class HoverMove:
	extends EnemyMoveStrategy

	func update(_delta: float, enemy: Enemy, ctx: Dictionary) -> void:
		if ctx.hovering:
			enemy.position.y = _hover_y(ctx)
			enemy.position.x = clampf(
				ctx.spawn_x + Enemy.sin_fast(ctx.time * _hover_sway_freq + ctx.phase) * _hover_sway_amp,
				ctx.view.position.x + 40.0,
				ctx.view.end.x - 40.0
			)
		else:
			enemy.position.y += ctx.speed * ctx.mdelta


## sine：横向正弦 + 悬停微浮
class SineMove:
	extends EnemyMoveStrategy

	func update(_delta: float, enemy: Enemy, ctx: Dictionary) -> void:
		enemy.position.x = ctx.spawn_x + Enemy.sin_fast(ctx.time * 3.0 + ctx.phase) * 90.0
		if ctx.hovering:
			enemy.position.y = _hover_y(ctx)
		else:
			enemy.position.y += ctx.speed * ctx.mdelta


## zigzag：折返横移 + 悬停微浮
class ZigzagMove:
	extends EnemyMoveStrategy

	var _zig_dir: float = 1.0
	var _zig_timer: float = 0.7

	func update(delta: float, enemy: Enemy, ctx: Dictionary) -> void:
		_zig_timer -= delta
		if _zig_timer <= 0.0:
			_zig_dir = -_zig_dir
			_zig_timer = 0.7
		enemy.position.x += _zig_dir * ctx.speed * 0.9 * ctx.mdelta
		if enemy.position.x < ctx.view.position.x + 40.0 or enemy.position.x > ctx.view.end.x - 40.0:
			_zig_dir = -_zig_dir
			enemy.position.x = clampf(enemy.position.x, ctx.view.position.x + 40.0, ctx.view.end.x - 40.0)
		if ctx.hovering:
			enemy.position.y = _hover_y(ctx)
		else:
			enemy.position.y += ctx.speed * ctx.mdelta

	func reset(_enemy: Enemy) -> void:
		_zig_dir = 1.0
		_zig_timer = randf_range(0.15, 0.7)


## dive：入场冲刺直扑玩家 → 转悬停（冲刺期例外，见 is_diving）
class DiveMove:
	extends EnemyMoveStrategy

	var _dive_target: Vector2 = Vector2.ZERO
	var _dive_timer: float = 0.0

	func update(delta: float, enemy: Enemy, ctx: Dictionary) -> void:
		if _dive_timer > 0.0:
			# 入场冲刺：直扑玩家当前位置（钳制不越过屏幕下缘安全线）
			_dive_timer -= delta
			var dir := (_dive_target - enemy.position).normalized()
			enemy.position += dir * ctx.speed * 1.7 * ctx.mdelta
			enemy.position.y = minf(enemy.position.y, ctx.view.end.y - 200.0)
			if _dive_timer <= 0.0:
				# 冲刺结束后以当前深度与锚点较深者为新锚点，转入悬停（悬停带下界加 view 基线）
				enemy.anchor_y = clampf(maxf(enemy.anchor_y, enemy.position.y), ctx.view.position.y + enemy.HOVER_BAND.x, ctx.view.end.y - 200.0)
		elif ctx.hovering:
			enemy.position.y = _hover_y(ctx)
		else:
			enemy.position.y += ctx.speed * ctx.mdelta

	func is_diving() -> bool:
		return _dive_timer > 0.0

	func reset(enemy: Enemy) -> void:
		_dive_timer = 1.2
		if GameState.player_ref != null:
			_dive_target = GameState.player_ref.global_position
		else:
			_dive_target = Vector2(enemy.position.x, 1200.0)


## spiral：绕转中心下压 → 悬停期中心漂移（悬停转换以中心为准）
class SpiralMove:
	extends EnemyMoveStrategy

	var _center: Vector2 = Vector2.ZERO

	func update(_delta: float, enemy: Enemy, ctx: Dictionary) -> void:
		if not ctx.hovering:
			_center.y += ctx.speed * ctx.mdelta
		else:
			_center.x = clampf(
				ctx.spawn_x + Enemy.sin_fast(ctx.time * _spiral_drift_freq + ctx.phase) * _spiral_drift_amp,
				ctx.view.position.x + 40.0,
				ctx.view.end.x - 40.0
			)
		enemy.position = (
			_center
			+ Vector2(Enemy.cos_fast(ctx.time * 4.0 + ctx.phase), Enemy.sin_fast(ctx.time * 4.0 + ctx.phase)) * _spiral_radius
		)

	func hover_reference_y() -> float:
		return _center.y

	func reset(enemy: Enemy) -> void:
		_center = enemy.position


## noise：三正弦叠加伪噪声横移 + 悬停微浮
class NoiseMove:
	extends EnemyMoveStrategy

	func update(_delta: float, enemy: Enemy, ctx: Dictionary) -> void:
		var vx: float = (
			(
				Enemy.sin_fast(ctx.time * 1.7 + ctx.phase)
				+ Enemy.sin_fast(ctx.time * 2.9 + 1.3 + ctx.phase)
				+ Enemy.sin_fast(ctx.time * 4.3 + 2.1 + ctx.phase)
			)
			/ 3.0 * ctx.speed * 1.2
		)
		enemy.position.x += vx * ctx.mdelta
		enemy.position.x = clampf(enemy.position.x, ctx.view.position.x + 40.0, ctx.view.end.x - 40.0)
		if ctx.hovering:
			enemy.position.y = _hover_y(ctx)
		else:
			enemy.position.y += ctx.speed * ctx.mdelta


## aggressive：追踪性噪声漂移（持续偏向玩家 x）+ 悬停微浮
class AggressiveMove:
	extends EnemyMoveStrategy

	func update(_delta: float, enemy: Enemy, ctx: Dictionary) -> void:
		var vx: float = (
			(
				Enemy.sin_fast(ctx.time * 2.1 + ctx.phase)
				+ Enemy.sin_fast(ctx.time * 3.4 + 1.7 + ctx.phase)
				+ Enemy.sin_fast(ctx.time * 5.3 + 0.6 + ctx.phase)
			)
			/ 3.0 * ctx.speed * 1.1
		)
		var player: Node2D = ctx.player
		if player != null:
			var dx: float = player.global_position.x - enemy.position.x
			vx += clampf(dx, -1.0, 1.0) * _aggressive_chase_speed
		enemy.position.x += vx * ctx.mdelta
		enemy.position.x = clampf(enemy.position.x, ctx.view.position.x + 40.0, ctx.view.end.x - 40.0)
		if ctx.hovering:
			enemy.position.y = _hover_y(ctx)
		else:
			enemy.position.y += ctx.speed * 0.9 * ctx.mdelta
