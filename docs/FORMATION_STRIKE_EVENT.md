# Formation Strike Event Design Document

This document is the single source of truth for the "formation bombing strike" random event: trigger priority, state machine, formation/bomb behavior, values, and test points.
Any change to this event at implementation time must be synced back here. The counterpart relation to the elite turret event is in `docs/ELITE_TURRET_EVENT.md`.

---

## 1. Concept

A formation of 3/4/5 attack craft (by difficulty) enters from above the screen top, holding a wedge formation while **approaching** (descending to the top edge of the player's active area),
then the whole formation **turns heading** (a 90° turn into a horizontal crossing run), **dropping bombs** craft-by-craft along the crossing, and accelerates off the side edge when done.
Bombs carry a fuse and a landing-point warning ring; detonation deals AoE damage. Every craft can be shot down by the player before dropping (downs score points; a full clear gives a small reward).

Positioning: the **lowest-priority** random encounter — it preempts no scheduling authority, only hitching into gaps where "no Boss, no elite turret event" holds;
the event is short (≈12s) and does not freeze Boss scheduling. New content for this game, independently designed.
**2026-07-29 revision**: the event now occupies a wave slot — normal waves pause while it runs (spawner `_waves_paused` hook; restored on end/interrupt),
and triggering zeroes the spawner special-slot counter (same slot as elite waves/Boss) to reduce stacking pressure.

## 2. Trigger & Priority

Priority chain (spawner `_process` checks in order each tick; if the former starts, the latter is skipped for that tick):

1. **Boss** (score/time threshold, highest priority)
2. **Elite turret event** (`elite_turret_event`, 30s heavy event; freezes Boss + pauses waves)
3. **Formation strike event** (this event, lowest priority): starts via a dice roll every `trigger_interval` seconds at `trigger_chance` probability, only when ① Boss not active (no warning/not present) ② elite turret event `is_active() == false` ③ self IDLE and cooldown elapsed ④ score ≥ `min_score`.

Key differences from the elite turret event (the concrete semantics of low priority):

- **Does not freeze Boss scheduling**: an expired Boss triggers normally (warning + entry ≈2s+, by which point the formation is nearly gone; bombs have warning rings to dodge, overlap risk manageable);
  but **normal waves pause during the event** (2026-07-29 revision: occupies the wave slot, sharing the `_waves_paused` hook with the elite turret event;
  to keep the two events' pause hooks from prematurely resuming each other, the elite turret event won't start while the formation is active).
- **Interruptible by homecoming**: `Main._start_homecoming()` calls the event's `abort()` (same semantics as mothership recall); the formation immediately disperses and leaves, no settlement, and waves resume; already-dropped bombs are independent entities that persist naturally (same semantics as enemy bullets).

## 3. State Machine

```
IDLE → FORMATION_ENTER (approach; duration derived from displacement / approach_speed, ≈1.5s) → FORMATION_TURN (heading turn, turn_time 1.2s)
     → BOMBING_RUN (crossing bomb run; length by formation size 2.0/2.8/3.6s for 3/4/5 craft) → FORMATION_EXIT (leave, EXIT_TIME 1.5s) → IDLE (cooldown)
```

- **FORMATION_ENTER**: the formation anchor descends vertically from above the screen top `(x0, view.top - 120)` to approach altitude `approach_y` (view.top + 260);
  craft hold wedge offsets (lead centered, wingmen swept back in ±55px increments). `x0` random in the middle 40%–60% of the view.
  On entry, `CommOverlay` plays a warning line (`FBQ_WARN`).
- **FORMATION_TURN**: the anchor slows; the formation heading smoothly rotates from +y to ±x over 1.2s (toward the farther side edge);
  craft offsets rotate with the heading (the wedge turns as a whole, wingmen tracing small arcs).
- **BOMBING_RUN**: the formation crosses horizontally at `run_speed`; from the turn's completion, each craft drops at a staggered `bomb_interval`
  (lead first, wingmen offset in turn), each dropping `bombs_per_craft` bombs (0.4s apart). The drop point is directly below the current position.
- **FORMATION_EXIT**: after bombing completes or crossing out the side edge, the formation accelerates off the side edge (1.5s), then back to IDLE and into cooldown.
- **Early end**: all craft shot down → settle the full-clear reward immediately → FORMATION_EXIT (remaining nodes cleaned up).

## 4. Entities

### 4.1 Formation Craft (`scripts/formation_craft.gd`, Area2D)

- Registered in the `enemy` group and `GameState.enemies` (hittable by player bullets/laser); unregistered on death/leave (same pattern as TurretBattery).
- Sprite reuses `assets/sprites/enemy_ship_2.png` (high-speed model, visually fitting an "attack craft"), scale 0.9.
- HP = `craft_hp_base` × `GameState.enemy_hp_multiplier()`; down score `craft_score` (`add_score` applies the difficulty multiplier).
- No self-AI: position = formation anchor + rotated offset, rotation = formation heading + PI/2 (nose toward heading), driven by the event's `_process`.
- On down: `Explosion.spawn_at()` + down sound, the rest of the formation continues; the bomb sequence skips destroyed craft.

### 4.2 Bomb (`scripts/formation_bomb.gd`, Area2D)

- Collision layer 4 (`enemy_bullet`) / mask 1 (`player`), but no hit-to-destroy logic: fuse-based.
- On drop, inherits formation horizontal speed ×0.35 + vertical fall at `bomb_fall_speed`; detonates `bomb_fuse` 1.2s later.
- **Warning**: the bomb body pulses with a glow (red-orange, 8Hz) plus a warning ring that shrinks with remaining fuse time (Line2D, radius 0.9×AoE → 0.15×AoE).
- **Detonation**: `Explosion.spawn_at(scale=0.9)` + explosion sound; distance check against `player_hitbox` (≤ `bomb_radius` and the player not invincible →
  `take_damage(bomb_damage)`). AoE damages only the player, not enemies (consistent with enemy-bullet semantics).
- `queue_free` after leaving bounds/detonating. Not affected by player buffs like the slow field (not enemy-registered entities; same semantics as enemy bullets).

## 5. Values (new top-level `formation_strike_event` section in `data/balance.json`; script same-named defaults are the missing-key fallback; the two must stay consistent)

| Key | Default | Description |
| --- | --- | --- |
| `min_score` | 500 | trigger score threshold (below the turret event's 800; visible earlier) |
| `trigger_interval` | 40.0 | dice-roll interval (seconds) |
| `trigger_chance` | 0.30 | per-roll probability |
| `cooldown` | 50.0 | post-event cooldown |
| `craft_counts` | `{easy:3, medium:4, hard:5}` | formation size |
| `craft_hp_base` | 60 | per-craft HP base (× difficulty HP multiplier) |
| `craft_score` | 200 | down base score |
| `approach_speed` | 260.0 | approach descent speed |
| `approach_y` | 260.0 | approach altitude (offset from the view top edge) |
| `turn_time` | 1.2 | heading-turn duration |
| `run_speed` | 340.0 | crossing-run speed |
| `bomb_interval` | 0.8 | per-craft staggered drop interval (raised from 0.35 on 2026-08-01: drop-run duration aligned with the design target, see §3) |
| `bombs_per_craft` | 2 | bombs per craft |
| `bomb_fall_speed` | 300.0 | bomb fall speed |
| `bomb_fuse` | 1.2 | fuse (seconds) |
| `bomb_damage` | 20 | AoE damage (player 100 HP) |
| `bomb_radius` | 120.0 | AoE radius |
| `reward_all_clear` | 200 | full-clear base reward (awarded for a full clear at any active stage; the formation immediately exits early) |

(`EXIT_TIME` 1.5s, same-craft drop interval 0.4s, wedge wingman step 55px are script `const` level; not in balance.json.)

## 6. i18n

One new key (bilingual zh/en columns): `FBQ_WARN`「侦测到轰炸编队，正在接近」/ `"Bomber formation inbound"`.
Reuses `CommOverlay` (same as the elite turret event: layer=12 bottom-left comm overlay).

## 7. Integration Points

- `main.gd _ready()`: creates the `FormationStrikeEvent` node under Main (visible to clearing/test traversal), registered as `spawner._formation`.
- `spawner.gd`: adds the `_formation` reference and trigger check (after the elite turret event check, at the tail of the same `_process`);
  trigger params read `formation_strike_event.*` (injected via `_apply_balance`).
- `main.gd _start_homecoming()`: calls `_formation.abort()` (formation disperses and leaves; no settlement; cooldown still runs).
- Dynamic entities (craft/bombs) all hang under Main; the homecoming clear-out `child is Enemy or child is Bullet` doesn't involve this event's entities
  (handled by `abort()` and the event's own lifecycle). No special-casing for player death (same as the elite turret event; the scene reload clears it after settlement).

## 8. Test Points (`test/formation_strike_event_test.tscn`)

Mirrors the `elite_turret_event_test` structure, real-Timer waits throughout:

1. **Trigger gates**: `can_trigger()` false for Boss active / elite turret event active / cooling down / insufficient score.
2. **State progression** (shortened config forcing `start()`): ENTER→TURN→BOMBING_RUN→EXIT→IDLE reached in order;
   craft registered in `GameState.enemies`; bombs spawn only after the turn; drop count = living craft × `bombs_per_craft` (downed craft skipped).
3. **Bombs**: warning ring exists and shrinks with the fuse; node freed after detonation; player standing within radius takes damage (not while invincible).
4. **Downs**: lethal `take_damage` → unregistered from `GameState.enemies` + score gained; full clear → reward + early EXIT.
5. **Interrupt**: `abort()` → entities cleaned up, back to IDLE, cooldown in effect.
6. **No Timer/node residue** (event tree and Main child counts); end cleans up `user://` persistence.

Regression list: `smoke_test`, `elite_turret_event_test` (regression for the shared scheduler change), `enemy_combat_test`,
`base_system_test`, `--quit-after 300`.

## 9. Documentation Sync

`AGENTS.md` (architecture tree / script list / test list / balance.json top-level sections), this file (single source of truth).
(The event entry was previously registered in `docs/PORTING_PARITY.md`; that document was archived and frozen 2026-07-30, no longer written back.)
