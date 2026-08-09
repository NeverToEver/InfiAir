# Entity Classification & Unified Entity Manager (ENTITY_MANAGER)

> 2026-08-05. Single source of truth for the in-game entity inventory and the unified
> entity manager design. The event manager (`docs/EVENT_MANAGER.md`) is the sibling pattern;
> this document generalizes batch management to game **entities** (units/effects) so new
> units and features plug in with near-zero boilerplate.
>
> 2026-08-08 全量迁移 C# 后:文中 GDScript 文件与行号锚点(§2 迁移前基线快照)已失效,保留;API 名同义 PascalCase(如 `bind_enemy` → `BindEnemy`、`GameState.enemies` → `GameState.Enemies`)。
> 另:§4.4 批量 API 实际落在 GameState 门面(`ForEachEnemy`/`ClearEnemies`/`CountEnemies`,谓词为 Callable——spawner 计数经 `CountEnemies` 使用;主清场/母舰/狂暴遍历在 C# 侧直接迭代 `GameState.Enemies`,无语义变化);`EntityManager`(`csharp/godot/EntityManager.cs`)保持注册表内核 + `BindEnemy`/`UnbindEnemy` + `EntityRegistered`/`EntityUnregistered` 信号。

## 1. Purpose

Entities today are registered manually (`add_to_group("enemy")` + `GameState.register_enemy`)
in every unit class, pooling exists only for the plain enemy scene, and bulk operations
(clear / target / slow / count) are hand-written traversals with per-type casts scattered
across consumers. Adding a new unit type or a new system that acts on all units means
repeating boilerplate and hunting every traversal site. This document:

1. Classifies the current entity inventory and management surface (one authoritative map);
2. Records the feasibility/benefit findings from a browser research pass (real Godot
   project patterns + pooling guidance);
3. Defines the **unified `EntityManager`** (evolution of `EntityRegistry`) that centralizes
   registration, lifecycle signals, pooling hooks and bulk operations;
4. Gives the migration map and invariants that keep behavior and tests unchanged.

## 2. Pre-migration entity inventory (snapshot, 2026-08-05)

> 写作当日迁移即落地（§4–§6）——§2 为迁移前基线快照：注册样板现为 `bind_enemy`/`unbind_enemy` 一行（§2.1 列出现行行号），注册表为 `EntityManager`（§2.3）。

### 2.1 Registered units (in `GameState.enemies` + `enemy` group)

| Unit | Class | Pool | Registered by |
| --- | --- | --- | --- |
| Enemy (normal/elite/splitter/minion) | `scripts/enemy.gd` (Area2D) | `EnemyPool` (enemy.tscn only) | `bind_enemy` in `_ready` 245 / `unbind_enemy` in `_exit_tree` 472; pooling paths keep `register_enemy` 425 / `unregister_enemy` 445 |
| Boss (4 rotating types) | `scripts/boss.gd` (Area2D, **not** an Enemy subclass) | none | `bind_enemy` in `_ready` 459 → `unbind_enemy` in `_exit_tree` 718 |
| TurretBattery (elite-turret event) | `scripts/turret_battery.gd` (Area2D) | none | `bind_enemy` in `_ready` 75 → `unbind_enemy` in `_exit_tree` 106 |
| FormationCraft (formation event) | `scripts/formation_craft.gd` (Area2D) | none | `bind_enemy` in `_ready` 44 → `unbind_enemy` in `_exit_tree` 61 |

All four share the one-line pattern `GameState.bind_enemy(self)` in `_ready` and
`GameState.unbind_enemy(self)` in `_exit_tree` — `add_to_group("enemy")` + registry
registration + `entity_registered`/`entity_unregistered` signals in one call (pre-migration:
3-line boilerplate `add_to_group` + `register_enemy`/`unregister_enemy`); Enemy additionally
keeps pooling paths `register_enemy`/`unregister_enemy` (reactivate/deactivate) + `_pool.forget`/`_repooling` guard.

### 2.2 Other run entities (not registered)

Player (special refs `player_ref`/`player_hitbox`), Mothership (group `mothership`), 
StrikeCarrier, FakeEnemy (deliberately unregistered), SpawnTelegraph, FormationBomb,
Explosion (own static pool), Bullet (own `BulletPool` + enemy-bullet registry for death
replay). These are **not** `enemies`-registry members by design (fake enemies must not be
hit/cleared; bombs/carriers are event-owned).

### 2.3 Registry + pools (pre-migration)

- `scripts/entity_registry.gd` (`EntityRegistry`, RefCounted) — **evolved into
  `scripts/entity_manager.gd` (`EntityManager`) the same day (2026-08-05, §4)**: `enemies` + O(1) `_enemy_set`
  (`has_enemy`, hot path for homing), `enemy_bullets` (death-replay data source), special
  refs (`player_ref`/`player_hitbox`/`bullet_pool`/`enemy_pool`/`aim_frame_layer`/
  `camera_ref`). GameState forwards every member — callers unchanged.
- Pools: `EnemyPool` (enemy.tscn only, `USE_POOL=true` hardcoded), `BulletPool`
  (`MAX_ENEMY_ACTIVE=500`), `Explosion` (embedded static pool, cap 24).
- Consumers of the registry: `spawner` (spread cap count), `bullet`/`laser_weapon`
  (homing/targeting), `mothership` (slow field, live targets), `main` (orbital-strike
  clear), `enrage_sequence` (hive volley), `aim_frame_layer` (brackets), `death_replay`.

### 2.4 Cost of adding a new unit / feature today

- New unit class: scene + `class_name` + the 3-line register boilerplate + (if pooled) pool
  wiring. Registry membership is the only thing that makes bullets/homing/mothership/clear
  work — but the boilerplate is copy-paste per class.
- New "affects all units" feature (e.g. freeze): no unified hook — must edit every unit's
  `_process`/`_physics_process`, or write a new traversal with type casts (and units like
  FormationBomb/FakeEnemy are not in the registry at all, so any registry-based traversal
  silently misses them).

## 3. Feasibility & benefit research (browser pass, 2026-08-05)

Sources examined with Playwright:

- **underkingdom-godot-cc (GitHub, real Godot project)** `docs/systems/entity-manager.md`:
  an Autoload `EntityManager` singleton owns all entities — `spawn_enemy(id, pos)` /
  `spawn_npc(...)` / `remove_entity(e)` / queries (`get_entities_at`, `get_enemies_by_behavior`,
  `has_enemy_definition`), unified turn processing (`process_entity_turns` duck-typed loop),
  `clear_entities()`, save/restore, and the player reference. Confirms the manager pattern is
  mature and used in shipped-style Godot projects.
- **uhiyama-lab "The Complete Guide to Object Pooling"** (Godot 4): a `PoolManager` Autoload
  keyed by scene path (`get_object("res://bullet.tscn")`) with per-pool dictionaries;
  documents the traps (reuse does **not** call `_ready()` → init must live in `spawn()`/`reset()`;
  tree ops during physics must be deferred; pool-exhaustion fallback; deactivation must stop
  processing + collisions) and the **adoption decision**: pool only short-lived high-frequency
  objects (bullets/effects); Godot 4 node creation is fast, so low-frequency entities should
  NOT be pooled. Our `EnemyPool` already follows the traps (reactivate/deactivate,
  `_reparent_deferred`, instantiate fallback).
- **Godot official "Spawning monsters"** tutorial + community threads: no built-in entity
  manager; a central spawn/manager node is the community-standard organization.
- Godot proposal #14752 ("Add Built-in Object Pool Nodes") shows pooling is an ongoing
  community need (not in engine yet).

**Conclusion — feasible and beneficial**: the manager pattern is proven in real Godot
projects; our project already has the data kernel (`EntityRegistry`) + working pools; the
increment is *registration boilerplate convergence + lifecycle signals + bulk-operation
APIs*, with **no new pooling for low-frequency entities** (Boss/turrets/carriers stay
direct-spawn, per the adoption guidance).

## 4. Unified `EntityManager` design

### 4.1 Scope

`EntityRegistry` evolves into `EntityManager` (`scripts/entity_manager.gd`,
`class_name EntityManager`), still a RefCounted service child of GameState (same pattern,
no tree-signal auto-registration — pooled `reparent` fires `_exit_tree` and would break
registration semantics; registration stays explicit but becomes a **one-liner**).
GameState keeps forwarding every member; external callers unchanged.

### 4.2 Registration convergence (new units = one line)

```gdscript
## 统一单位绑定：add_to_group("enemy") + register_enemy + entity_registered 信号。
## 单位类 _ready 调一次（_exit_tree 调 unbind_enemy）；池化路径（reactivate/deactivate）
## 保持原 register/unregister（幂等），不受影响。
func bind_enemy(node: Node) -> void
func unbind_enemy(node: Node) -> void
```

Replaces the identical 3-line boilerplate in `enemy.gd` / `boss.gd` / `turret_battery.gd` /
`formation_craft.gd`. Group + registry stay in sync (autoplay probe invariant preserved).

### 4.3 Lifecycle signals (new features subscribe, no unit edits)

```gdscript
signal entity_registered(node: Node)
signal entity_unregistered(node: Node)
```

A future "freeze all units" or stat tracker subscribes once; no need to touch unit classes.

### 4.4 Bulk operations (clear / target / count unified)

```gdscript
## 安全遍历注册表（跳过失效实例；谓词可选过滤），供索敌/慢速/冻结等统一入口
func for_each_enemy(action: Callable, predicate: Callable = Callable()) -> void
## 批量清除（轨道打击/清屏统一入口；predicate 过滤保留项，如 Boss）
func clear_enemies(predicate: Callable = Callable()) -> int   # 返回清除数
## 计数（spread 上限/统计）
func count_enemies(predicate: Callable = Callable()) -> int
```

Consumers migrate from hand-written loops: `main._on_orbital_struck` (keep-Boss clear),
`mothership._live_targets` / `_deploy_slow_field`, `enrage_sequence` hive volley, `spawner`
spread count. All predicates take a `Node` and return `bool`; invalid instances are skipped
(registry may hold queued-for-free nodes).

### 4.5 Pools & special refs

`enemy_pool` / `bullet_pool` / `player_ref` / `player_hitbox` / `aim_frame_layer` /
`camera_ref` remain on the manager (as today). **No generic pool is introduced** —
low-frequency units stay direct-spawn (section 3 adoption guidance); adding a pooled unit
type later = registering a `PooledScene` entry, not a new class.

### 4.6 Invariants

- `GameState.enemies` / `enemies_has` / `register_enemy` / `unregister_enemy` /
  `register_enemy_bullet` / `unregister_enemy_bullet` semantics unchanged (O(1) hot paths,
  idempotent, death-replay data source).
- `add_to_group("enemy")` always alongside registry registration (autoplay consistency).
- Pooled `reparent` (`_repooling` guard) unaffected; `bind_enemy` only in `_ready`/
  `_exit_tree`, pooling paths keep their own register/unregister calls.
- Boss is **not** an Enemy subclass — registry stays `Array[Node]`; consumers use
  predicates/duck-typing as today.

## 5. Migration map

| File | Change |
| --- | --- |
| `scripts/entity_registry.gd` | evolves to `EntityManager`: + `bind_enemy`/`unbind_enemy`, signals `entity_registered`/`entity_unregistered`, `for_each_enemy`/`clear_enemies`/`count_enemies` |
| `scripts/enemy.gd` / `boss.gd` / `turret_battery.gd` / `formation_craft.gd` | `_ready`/`_exit_tree` boilerplate → `bind_enemy`/`unbind_enemy` |
| `scripts/main.gd` | orbital-strike clear → `clear_enemies(keep Boss)` |
| `scripts/mothership.gd` / `enrage_sequence.gd` / `spawner.gd` | loop consumers → `for_each_enemy`/`count_enemies` |
| `autoload/game_state.gd` | `EntityRegistry` → `EntityManager`; forwarding unchanged |
| tests | new `entity_manager_test` (bind/unbind idempotence, signals, bulk ops, keep-Boss clear); existing registry/pool tests unchanged |

## 6. Test strategy

- `pool_reuse_test` / `boss_registry_test` / `orbital_strike_test` / `elite_turret` /
  `formation` / `enemy_combat` / `autoplay` (registry↔group consistency) must pass unchanged.
- New `entity_manager_test`: bind/unbind one-liner idempotence, `entity_registered`/
  `entity_unregistered` signals, `for_each_enemy` invalid-instance skip, `clear_enemies`
  predicate (keep Boss), `count_enemies`.
- Gate: the layers of `docs/TESTING.md` (C# build/test/format → zero-GDScript → import warnings
  → BALANCE_MAP zero-diff → compile+smoke → all assertion scenes).
