# Performance & Object Lifecycle

## Overview

Pooling, hot-path restrictions, and update patterns. Applies to combat spawning, per-frame code, and HUD updates.

## Rules

- Bullets via `GameState.BulletPool.Fire()`; pool ref cleanup handled on exit-tree.
- When editing pools keep the `_repooling` guard: Godot 4.6 `Reparent()` fires `_ExitTree()`; **池化 reparent** must be wrapped in `SetRepooling(true/false)`（EnemyPool 双向——spawn→Main 的 `Spawn()` 与 release→pool 的 `ReparentDeferred()`；BulletPool 在 release 侧）——否则 `_ExitTree` 误走 `UnbindEnemy` 发无配对 `EntityUnregistered`（R04 补齐 spawn 侧），或 `Forget()` 把对象误清出闲置池。Run `test/pool_reuse_test.tscn` + `test/entity_manager_test.tscn` after changes.
- Enemies pooled via `GameState.EnemyPool.Spawn()` (waves, boss-3 minions, formations; `EnemyPool.UsePool` is `const true` — the `USE_POOL=false` direct-instantiation A/B branch was removed in the C# migration). Pooled entities reset/register/emit death in `Reactivate()`/`Deactivate()`; don't free or bypass pool objects externally. Details: `docs/ARCHITECTURE.md`.
- Hot paths: no per-frame `GetNodesInGroup()` — use `GameState.Enemies`/`PlayerRef`/`PlayerHitbox` registries. `Enemy` movement uses `Enemy.SinFast()`/`CosFast()` lookup tables; no direct trig in `_PhysicsProcess()`.
- HUD gauges poll ~0.1s throttled, relayout only on text/value change; prefer `GameState` signal-driven updates.
