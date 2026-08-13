# Performance & Object Lifecycle

## Overview
对象池、热路径限制与更新模式；适用于战斗生成、每帧代码与 HUD 更新。

## Rules
- 子弹经 `GameState.BulletPool.Fire()`；池引用清理在 exit-tree 处理。
- 改池保持 `SetRepooling` 双向包裹（EnemyPool spawn/release、BulletPool release；否则 `_ExitTree` 误发 `EntityUnregistered` 或误清闲置池）；改池后跑 `test/pool_reuse_test.tscn` + `test/entity_manager_test.tscn`。
- Enemies: waves、boss-3 小兵走 `GameState.EnemyPool.Spawn()`；formation 直建直毁（`new FormationCraft()` + `QueueFree()`）。池化实体在 `Reactivate()`/`Deactivate()` 重置/注册/发死亡；禁止外部 free 或绕过池。详见 `docs/ARCHITECTURE.md`。
- 热路径: 禁逐帧 `GetNodesInGroup()`——用 `GameState.Enemies`/`PlayerRef`/`PlayerHitbox` 注册表；`Enemy` 移动用 `SinFast()`/`CosFast()` 查表，`_PhysicsProcess()` 内禁裸三角。
- HUD 仪表 ~0.1s 节流轮询，仅文本/数值变化时重排；优先 `GameState` 信号驱动。
