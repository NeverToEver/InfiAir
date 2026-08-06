class_name BulletPool
extends Node
## 子弹对象池（挂在 Main 下）：复用 bullet.tscn 实例，避免高频 instantiate/free。
## 活跃弹挂在 Main 下（保持清场/测试遍历可见），闲置弹收回池节点下。

const BULLET_SCENE: PackedScene = preload("res://scenes/bullet.tscn")
## P2-3（2026-08-05 审计）：同屏敌弹显式硬上限——防极端场景（Boss 狂暴+多事件叠加）
## 弹数失控。阈值 500 远高于 perf_bench 实测 300+ 峰值，正常对局永不触发；
## 仅限制敌弹（弹幕主力），玩家火力（射速自限）不受影响。
## 2026-08-06 审计口径：active_count() 为全阵营活跃弹总数，实际敌弹 cap ≈ 500 − 活跃
## 玩家弹（玩家弹至多数十发，偏差可忽略；上限仍以总量计，语义登记于此）
const MAX_ENEMY_ACTIVE := 500

var _free: Array[Bullet] = []


func _ready() -> void:
	GameState.bullet_pool = self


## C21 修复：场景卸载时清空全局注册，避免 GameState.bullet_pool 悬空
func _exit_tree() -> void:
	if GameState.bullet_pool == self:
		GameState.bullet_pool = null


## 闲置实例数（A7 遗留清理：测试/诊断公开查询，替代 _free 直读）
func free_count() -> int:
	return _free.size()


## 取一枚子弹并激活（参数同 Bullet.setup）。敌弹超硬上限时返回 null（调用方判空跳过）。
func fire(
	p_direction: Vector2, p_speed: float, p_damage: int, p_is_player: bool, p_homing: bool = false, p_homing_time: float = 0.0
) -> Bullet:
	# P2-3（2026-08-05 审计）：同屏敌弹显式硬上限（防极端场景失控；玩家弹永不限制）
	if not p_is_player and Bullet.active_count() >= MAX_ENEMY_ACTIVE:
		return null
	var b: Bullet = null
	while not _free.is_empty():
		b = _free.pop_back()
		if is_instance_valid(b):
			break
		b = null
	if b == null:
		b = BULLET_SCENE.instantiate()
		b.set_pool(self)
		get_parent().add_child(b)  # 活跃弹挂 Main 下
	elif b.get_parent() != get_parent():
		# 闲置弹从池节点挂回 Main
		b.reparent(get_parent())
	b.activate(p_direction, p_speed, p_damage, p_is_player, p_homing, p_homing_time)
	return b


## 回收：重置状态并移回池节点下（不销毁）。
## reparent 延迟到空闲时执行，避免在物理回调（area_entered）内改场景树；
## 若子弹在延迟执行前已被重激活（同帧复用）则跳过，防止过期延迟调用覆盖新激活。
## 幂等：受击清弹与命中销毁可能在同一回调内重复触发回收。
func release(b: Bullet) -> void:
	if not is_instance_valid(b) or _free.has(b):
		return
	b.deactivate()
	_free.append(b)
	_reparent_deferred.call_deferred(b)


func _reparent_deferred(b: Bullet) -> void:
	if is_instance_valid(b) and not b.is_active():
		# 4.6 实测 reparent 会触发 b._exit_tree，置位防 forget 把子弹误清出 _free
		b.set_repooling(true)
		b.reparent(self)
		b.set_repooling(false)


## 子弹被外部 queue_free（清场/测试）时从池清单移除，防止悬空引用。
func forget(b: Bullet) -> void:
	_free.erase(b)
