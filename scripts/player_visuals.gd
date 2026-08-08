class_name PlayerVisuals
extends RefCounted
## M3b：Enemy 迁 C#，sin_fast 静态经脚本资源（gdlint class-load-variable-name：snake_case）
var _enemy_script := load("res://csharp/godot/Enemy.cs")
## A8 拆分：玩家视觉职责聚合——尾焰、冲刺残影池、机身色调（弹反金/擦弹金/无敌闪烁）、
## 受击点脉动、弹反盾视觉、擦弹闪光状态。
## 组合委托模式（同 PlayerDamage/PlayerDash/PlayerParry）：不持有节点所有权，经 player
## 传入的节点引用操作；公开接口供 player 帧驱动与外部（player_dash 残影入口）调用。
## 拆分动机：player.gd 视觉与战斗逻辑解耦（AUDIT_VAULT A8；DESIGN_BASELINE §7.1）。

## P1-5：冲刺残影小池（预建复用，替代逐次 new Sprite2D + Tween + queue_free）
const AFTERIMAGE_POOL_SIZE := 4
const AFTERIMAGE_FADE_TIME := 0.3
const AFTERIMAGE_COLOR := Color(0.5, 0.9, 1.0, 0.5)

var _sprite: Sprite2D = null
var _thruster: GPUParticles2D = null
var _hitbox_dot: Polygon2D = null
var _parry_arc: Polygon2D = null
var _parry_shine: Polygon2D = null
var _body_tint_base := Color(1.35, 1.4, 1.55)  # BODY_TINT_BASE 迁入（可视性增强提亮青白）
## 擦弹机身短闪光剩余时长（金色微闪，独立短计时；set_graze_flash 置位、update_frame 递减）
var _graze_flash: float = 0.0
var _afterimage_pool: Array[Sprite2D] = []
var _afterimage_idx: int = 0
var _active_afterimages: Array[Sprite2D] = []


## 初始化：接收节点引用 + 预建残影池。world_root = Main（残影固定世界坐标，不随玩家移动；
## Main 场景构建期 add_child 会报 "busy setting up children"，延迟到帧末执行——原 _ready 逻辑迁移）
func init(
	sprite: Sprite2D,
	thruster: GPUParticles2D,
	hitbox_dot: Polygon2D,
	parry_arc: Polygon2D,
	parry_shine: Polygon2D,
	world_root: Node,
	pool_size: int = AFTERIMAGE_POOL_SIZE,
) -> void:
	_sprite = sprite
	_thruster = thruster
	_hitbox_dot = hitbox_dot
	_parry_arc = parry_arc
	_parry_shine = parry_shine
	for i in pool_size:
		var g := Sprite2D.new()
		g.visible = false
		g.modulate = AFTERIMAGE_COLOR
		world_root.add_child.call_deferred(g)
		_afterimage_pool.append(g)


## 尾焰档位应用（冲刺/加速/巡航/静止五处共用；engine_tint 由 player 传入——buff 外观
## 写入 player.engine_tint，公开字段被 PlayerBuffVisuals/测试访问，留在 player 侧）
func set_thruster(speed_scale: float, amount_ratio: float, alpha: float, engine_tint: Color) -> void:
	_thruster.speed_scale = speed_scale
	_thruster.amount_ratio = amount_ratio
	_thruster.self_modulate = Color(1.0, 1.0, 1.0, alpha) * engine_tint


## 残影生成（player_dash 冲刺时经 player.spawn_afterimage 转发）：复用池节点；
## 同一节点淡出中被再次冲刺命中时 alpha 重置重新淡出
func spawn_afterimage(sprite_texture: Texture2D, sprite_scale: Vector2, gpos: Vector2, rot: float, color: Color = AFTERIMAGE_COLOR) -> void:
	var ghost := _afterimage_pool[_afterimage_idx]
	_afterimage_idx = (_afterimage_idx + 1) % _afterimage_pool.size()
	ghost.texture = sprite_texture
	ghost.scale = sprite_scale
	ghost.global_position = gpos
	ghost.global_rotation = rot
	ghost.modulate = color
	ghost.visible = true
	if not _active_afterimages.has(ghost):
		_active_afterimages.append(ghost)


## 残影淡出推进（player._process 每帧调用；池内每节点 alpha 线性衰减，归零隐藏）
func update_afterimages(delta: float) -> void:
	if _active_afterimages.is_empty():
		return
	var i := 0
	while i < _active_afterimages.size():
		var g: Sprite2D = _active_afterimages[i]
		g.modulate.a -= delta / AFTERIMAGE_FADE_TIME
		if g.modulate.a <= 0.0:
			g.visible = false
			_active_afterimages.remove_at(i)
		else:
			i += 1


## 机身色调四源（优先级从高到低）：弹反金 tint > 擦弹金色微闪 > 无敌帧闪烁 > 常态基底。
## 擦弹闪光在此递减（原 _physics_process 视觉分支）；无敌倒计时递减留在 player（战斗状态）。
## 受击点光点脉动同帧驱动（常亮低频闪烁，提示实际受击判定位置）。
func update_frame(delta: float, parry_tint: float, invincible: float, now_ms: int) -> void:
	if parry_tint > 0.0:
		_sprite.modulate = _body_tint_base.lerp(Color(1.7, 1.25, 0.5), parry_tint)
	elif _graze_flash > 0.0:
		_graze_flash -= delta
		_sprite.modulate = _body_tint_base.lerp(Color(1.7, 1.35, 0.5), 1.0)
	elif invincible > 0.0:
		_sprite.modulate = _body_tint_base
		_sprite.modulate.a = 0.35 + 0.65 * absf(_enemy_script.SinFast(now_ms * 0.02))
	else:
		_sprite.modulate = _body_tint_base
	_hitbox_dot.modulate.a = 0.45 + 0.55 * absf(_enemy_script.SinFast(now_ms * 0.006))


## 擦弹机身金色短闪置位（_on_graze_entered 反馈三件套之一；时长 balance player.graze.flash_time）
func set_graze_flash(time: float) -> void:
	_graze_flash = time


## 盾视觉逐物理帧驱动：WINDUP 小弧展开到全弧（缩放）、ACTIVE 珍珠流光自弧线左端扫至右端、
## RECOVER 保持全弧、IDLE 隐藏；高光带角度按 active 进度线性插值（零 shader 依赖）。
## 参数化（expand/shine 来自 PlayerParry，radius/arc 来自 player 常量）——视觉不感知 parry 组件。
func update_parry_visuals(expand: float, shine: float, radius: float, arc_deg: float) -> void:
	_parry_arc.visible = expand > 0.0
	if not _parry_arc.visible:
		_parry_shine.visible = false
		return
	var scale := 0.3 + 0.7 * expand
	_parry_arc.scale = Vector2.ONE * scale
	_parry_shine.visible = shine > 0.0
	if not _parry_shine.visible:
		return
	var arc := deg_to_rad(arc_deg) * 0.5
	var center_a := -PI / 2.0 - arc + 2.0 * arc * shine
	var w := deg_to_rad(22.0)  # 高光带角宽
	var sp := PackedVector2Array([Vector2.ZERO])
	for i in 5:
		var a := center_a - w + (2.0 * w) * float(i) / 4.0
		sp.append(Vector2(cos(a), sin(a)) * radius * scale)
	_parry_shine.polygon = sp
