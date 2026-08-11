# Formation Strike Event

Single source of truth; sync this doc on change. Counterpart: `docs/ELITE_TURRET_EVENT.md`.

## 1. Concept & Priority

**Lowest-priority** encounter: no Boss + no elite turret event; ~12s.

(2026-07-29): occupies a wave slot: pauses normal waves (spawner `_wavesPaused` hook; restore on end/interrupt); trigger resets spawner special-slot counter (same as elite/Boss). **2026-08-05**: trigger policy moved to unified event manager (`GameEventManager`, `docs/EVENT_MANAGER.md`) — same `formation_strike_event.trigger_*`/`min_score`/`cooldown` balance keys, same priority/mutex chain.

(spawner `_process` per tick; first active skips rest → 2026-08-05 起由统一事件管理器按注册序检查):

1. **Boss** (score/time thresholds, highest)
2. **Elite turret event** (`elite_turret_event`, 30s heavy; freezes Boss + pauses waves)
3. **Formation strike event** (lowest): ① no Boss (unwarned/absent) ② elite turret `IsActive() == false` ③ IDLE, cooldown done ④ score ≥ `min_score`; roll `trigger_interval`s @ `trigger_chance`.

- **No Boss freeze**: Boss fires on schedule (~2s+ warning); pauses waves (shares `_wavesPaused` hook; elite turret event can't start while formation active).
- **Homecoming**: `Main.StartHomecoming()` → `Abort()`; disband, no settlement, waves resume; bombs persist.

## 2. State Machine

- FORMATION_ENTER: descend `(x0, view.top - 120)` → `approach_y` (view.top + 260) (~1.5s); wedge offsets (lead centered, wingmen `±WING_STEP×step` — 内翼 ±55px / 外翼 ±110px 递增); `x0` central 40%–60%; `CommOverlay` → `FBQ_WARN`.
- FORMATION_TURN: accelerate to crossing speed (speed lerps `ApproachSpeed` → `RunSpeed` over `turn_time` 1.2s — `FormationStrikeEvent.cs`); heading +y → ±x (farther side); offsets rotate with heading.
- BOMBING_RUN: cross at `run_speed` (2.0/2.8/3.6s for 3/4/5 = 投弹时刻表末弹时刻 (n−1)×`bomb_interval` + 0.4s，非横穿时长——横穿约 5.6s); from turn end, craft drop staggered `bomb_interval` (lead first) × `bombs_per_craft` (0.4s gap), straight below.
- FORMATION_EXIT: after bombing/crossing side edge, accelerate off-side (`EXIT_TIME` 1.5s) → IDLE + cooldown.
- Early end: all destroyed → all-clear reward → FORMATION_EXIT (cleanup).

## 3. Entities

- Craft `csharp/godot/FormationCraft.cs` (Area2D): `enemy` group + `GameState.Enemies`; deregistered on death/exit.
- Sprite `assets/sprites/enemy_ship_2.png`, scale 0.9.
- HP = `craft_hp_base` × `GameState.EnemyHpMultiplier()` × `GameState.EnemyHpRamp()` (基准 × 难度档 × 对局进程 ramp,与普通敌机同口径); kill score `craft_score` — 击坠经 `AddKillScore` (2026-08-11 c3ca549 连击改造: 连击 +1 并刷新窗口, `craft_score` × 连击乘区后照常过难度倍率, 第 1 杀乘区 1.0 不放大; AC25 同步). 全歼奖励 `reward_all_clear` 直接 `AddScore`, 不计连击.
- No own AI: pos = anchor + rotated offset; rotation = heading + PI/2; `_Process` drives.
- Killed: `Explosion.SpawnAt()` + SFX; bomb sequence skips destroyed craft.

- Bomb `csharp/godot/FormationBomb.cs` (Area2D): layer 4 (`enemy_bullet`) / mask 1 (`player`); fuse-based, no hit-to-destroy.
- Drop: ×0.35 formation h-speed + fall `bomb_fall_speed`; detonate after `bomb_fuse`.
- Warning: glow (red-orange, 8Hz) + shrinking ring (Line2D, 0.9×AoE → 0.15×AoE).
- Detonation: `Explosion.SpawnAt(scale: 0.9)` + SFX; distance vs `GameState.PlayerHitbox` (≤ `bomb_radius`, not invulnerable → `TakeDamage(bomb_damage, GlobalPosition)`); player-only.
- queue_free on out-of-bounds/detonation; immune to slow fields.

## 4. Balance & i18n (`formation_strike_event` in `data/balance.json`; same-name script defaults as fallback, in sync)

| Key | Default | Notes |
| --- | --- | --- |
| `min_score` | 500 | trigger threshold (turret 800) |
| `trigger_interval` | 40.0 | roll interval |
| `trigger_chance` | 0.30 | roll chance |
| `cooldown` | 50.0 | post-event |
| `craft_counts` | `{easy:3, medium:4, hard:5}` | formation size |
| `craft_hp_base` | 60 | HP base (× difficulty mult) |
| `craft_score` | 200 | kill base score（击坠经 `AddKillScore` 计连击 × 乘区；全歼奖励不计连击） |
| `approach_speed` | 260.0 | approach speed |
| `approach_y` | 260.0 | approach height (view-top offset) |
| `turn_time` | 1.2 | turn duration |
| `run_speed` | 340.0 | crossing speed |
| `bomb_interval` | 0.8 | craft stagger (2026-08-01: 0.35→0.8; §2) |
| `bombs_per_craft` | 2 | bombs per craft |
| `bomb_fall_speed` | 300.0 | fall speed |
| `bomb_fuse` | 1.2 | fuse |
| `bomb_damage` | 20 | AoE damage (player 100 HP) |
| `bomb_radius` | 120.0 | AoE radius |
| `reward_all_clear` | 200 | all-clear bonus (any stage; early EXIT) |

(`ExitTime` 1.5s, same-craft gap 0.4s `BombStagger`, wedge step 55px `WingStep` = consts.)

i18n: 1 key (zh/en): `FBQ_WARN` 「侦测到轰炸编队，正在接近」/ "Bomber formation inbound". Reuses `CommOverlay` (layer=12).

## 5. Integration Points

- `Main._Ready()`: create `FormationStrikeEvent` under Main → `spawner.SetFormationEvent(_formation)`.
- `csharp/godot/Spawner.cs`: `_formation` ref + trigger gates (`IsBossActive()` / elite `IsActive()`, polled by `GameEventManager`, §1); params `formation_strike_event.*` read by the event via `Cfg()` (same-name fallbacks).
- `Main.StartHomecoming()`: `_events.EndActive(GROUP_ENCOUNTER)` (`Main.cs`) — Abort 由管理器内部派发 (`GameEventManager.cs` `EndActive` → `IEncounterEvent.Abort()`; no settlement; cooldown counts).
- Craft/bomb under Main; field-clear (`OnOrbitalStruck`) sweeps registered enemies + `child is Bullet || child is FormationBomb`; `Abort()` owns event-lifecycle cleanup. Player death: no special case.

## 6. Tests (`test/formation_strike_event_test.tscn`)

Mirrors `elite_turret_event_test` — 断言细节（CanTrigger 门控 / FSM 全状态迁移 / 炸弹 AoE /
Abort 清理）见测试文件；regression: `smoke_test`, `elite_turret_event_test`,
`enemy_combat_test`, `base_system_test`, `--quit-after 300`。

## 7. Docs

`AGENTS.md` (arch/script/test lists, balance.json top sections) + this file. Formerly `docs/PORTING_PARITY.md`; archived 2026-07-30; no writeback.
