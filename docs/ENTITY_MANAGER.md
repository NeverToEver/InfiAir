# Entity Classification & Unified Entity Manager (ENTITY_MANAGER)

> 2026-08-05. Single source of truth for the in-game entity inventory and the unified
> entity manager design. The event manager (`docs/EVENT_MANAGER.md`) is the sibling pattern;
> this document generalizes batch management to game **entities** (units/effects) so new
> units and features plug in with near-zero boilerplate.
>
> 2026-08-08 全量迁移 C# 后:文中 GDScript 文件与行号锚点(§2 迁移前基线快照)已失效,保留;API 名同义 PascalCase(如 `bind_enemy` → `BindEnemy`、`GameState.enemies` → `GameState.Enemies`)。
> 另:§4.4 批量 API 实际落在 GameState 门面(`ForEachEnemy`/`ClearEnemies`/`CountEnemies`,谓词为 Callable——spawner 计数经 `CountEnemies` 使用;主清场/母舰/狂暴遍历在 C# 侧直接迭代 `GameState.Enemies`,无语义变化);`EntityManager`(`csharp/godot/EntityManager.cs`)保持注册表内核 + `BindEnemy`/`UnbindEnemy` + `EntityRegistered`/`EntityUnregistered` 信号。

## 2. Pre-migration entity inventory (snapshot, 2026-08-05)

> 写作当日迁移即落地（§4–§6）——§2 为迁移前基线快照：注册样板现为 `bind_enemy`/`unbind_enemy` 一行（§2.1 列出迁移时行号），注册表为 `EntityManager`（§2.3）。

### 2.1 Registered units (in `GameState.enemies` + `enemy` group)

| Unit | Class | Pool | Registered by |
| --- | --- | --- | --- |
| Enemy (normal/elite/splitter/minion) | `scripts/enemy.gd` (Area2D) | `EnemyPool` (enemy.tscn only) | C#: `BindEnemy` in `_ready` (`csharp/godot/Enemy.cs:128`); pooling paths keep `RegisterEnemy`/`UnregisterEnemy` (`Enemy.cs:330/354`)。GDScript 行号（245/472/425/445）已失效 |
| Boss (4 rotating types) | `scripts/boss.gd` (Area2D, **not** an Enemy subclass) | none | GDScript 行号（459/718）已失效 |
| TurretBattery (elite-turret event) | `scripts/turret_battery.gd` (Area2D) | none | C#: `BindEnemy` in `_ready` (`TurretBattery.cs:119`) → `UnbindEnemy` in `_exit_tree` (`TurretBattery.cs:157`)。GDScript 行号（75/106）已失效 |
| FormationCraft (formation event) | `scripts/formation_craft.gd` (Area2D) | none | C#: `BindEnemy` in `_ready` (`FormationCraft.cs:51`) → `UnbindEnemy` in `_exit_tree` (`FormationCraft.cs:83`)。GDScript 行号（44/61）已失效 |

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
low-frequency units stay direct-spawn; adding a pooled unit
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

## 6. Test strategy

Gate: the layers of `docs/TESTING.md` (C# build/test/format → zero-GDScript → import warnings
→ BALANCE_MAP zero-diff → compile+smoke → all assertion scenes).
