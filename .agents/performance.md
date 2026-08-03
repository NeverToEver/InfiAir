# Performance & Object Lifecycle

## Overview

Pooling, hot-path restrictions, and update patterns. Applies to combat spawning, per-frame code, and HUD updates.

## Rules

- Bullets via `GameState.bullet_pool.fire()`; pool ref cleanup handled on exit-tree.
- When editing pools keep `_active`/`_repooling` guards: Godot 4.6 `reparent()` fires `_exit_tree()`; repool reparent must be wrapped in `_repooling` or `forget()` wrongly removes the object from the idle pool. Run `test/pool_reuse_test.tscn` after changes.
- Enemies pooled via `GameState.enemy_pool.spawn()` (waves, boss-3 minions, formations; `USE_POOL=false` degrades to direct instantiation as perf A/B switch). Pooled entities reset/register/emit death in `reactivate()`/`deactivate()`; don't free or bypass pool objects externally. Details: `docs/ARCHITECTURE.md`.
- Hot paths: no per-frame `get_nodes_in_group()` — use `GameState.enemies`/`player_ref`/`player_hitbox` registries. `Enemy` movement uses `Enemy.sin_fast()`/`cos_fast()` lookup tables; no direct trig in `_physics_process()`.
- HUD gauges poll ~0.1s throttled, relayout only on text/value change; prefer `GameState` signal-driven updates.
