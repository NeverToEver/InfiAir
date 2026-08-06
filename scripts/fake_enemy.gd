class_name FakeEnemy
extends Node2D
## 伪敌机（迷雾事件·fake_enemies）：无伤害/无碰撞的幽灵敌机，纯视觉干扰。
## 复用敌机贴图 + 幽灵闪烁（半透明青白调），不注册 GameState.enemies、不入 "enemy" 组、
## 无碰撞形状——玩家子弹直接穿过（不结算、不消耗穿透），不参与任何对局系统
## （辅助瞄准标记/击杀/分数/波次上限/注册表一致性均不受影响）。
## 行为：可选入场延迟（错峰）→ 自屏幕顶降入 → 悬停带内水平摇摆，事件结束统一移除。

const FAKE_TEXTURES: Array[Texture2D] = [
	preload("res://assets/sprites/enemy_ship_1.png"),
	preload("res://assets/sprites/enemy_ship_2.png"),
]

## 幽灵外观：半透明青白调 + 正弦闪烁
const GHOST_TINT := Color(0.75, 0.9, 1.0, 0.55)
const FLICKER_AMPLITUDE := 0.18
const FLICKER_FREQ := 10.0
## 悬停带（相对可见区域顶缘偏移，对齐 enemies.hover_band 量级）
const HOVER_BAND := Vector2(150.0, 430.0)
const DESCEND_SPEED := 180.0
const SWAY_AMP := 30.0
const SWAY_FREQ := 1.2

## 错峰入场延迟（FogEventManager 按 spawn_interval 分配）
var enter_delay := 0.0

var _sprite: Sprite2D = null
var _t := 0.0
var _entered := false
var _hover_y := 0.0
var _start_x := 0.0


func _ready() -> void:
	_start_x = position.x
	_sprite = Sprite2D.new()
	_sprite.texture = FAKE_TEXTURES[randi() % FAKE_TEXTURES.size()]
	_sprite.scale = Vector2.ONE * randf_range(0.55, 0.68) * GameState.world_scale
	_sprite.modulate = GHOST_TINT
	add_child(_sprite)
	if enter_delay > 0.0:
		visible = false
		var t := Timer.new()
		t.one_shot = true
		t.wait_time = enter_delay
		t.timeout.connect(_on_delay_done.bind(t))
		add_child(t)
		t.start()
	else:
		_on_delay_done(null)


func _on_delay_done(t: Timer) -> void:
	if t != null and is_instance_valid(t):
		t.queue_free()
	if not is_instance_valid(self) or is_queued_for_deletion():
		return
	visible = true
	_entered = true
	# 锚点 = 出生点下方 120~260，钳入悬停带（对齐 Enemy._resolve_anchor 量级）
	var view := GameState.view_world_rect()
	_hover_y = clampf(position.y + randf_range(120.0, 260.0), view.position.y + HOVER_BAND.x, view.position.y + HOVER_BAND.y)


func _physics_process(delta: float) -> void:
	if not _entered:
		return
	_t += delta
	# 幽灵闪烁（alpha 正弦，视觉干扰）
	_sprite.modulate.a = GHOST_TINT.a + sin(_t * FLICKER_FREQ) * FLICKER_AMPLITUDE
	if position.y < _hover_y:
		position.y = minf(position.y + DESCEND_SPEED * delta, _hover_y)
		# 下降期同步水平微摆（错开全波机械感）
		position.x = _start_x + sin(_t * SWAY_FREQ * 0.5) * SWAY_AMP * 0.5
	else:
		position.x = _start_x + sin(_t * SWAY_FREQ) * SWAY_AMP
	# 出屏销毁兜底（正常路径由 FogEventManager 在事件结束时统一移除，此路径防事件异常残留）。
	# 2026-08-06 审计 M3：余量对齐最大出生深度（事件侧出生 y = 视野顶 − randf(20,260)）——
	# 原 80px 余量使约 75% 个体出生即被销毁（幽灵机群实际可见 1-2 只，违背错峰入场设计）
	if not GameState.view_world_rect(280.0).has_point(position):
		queue_free()
