# Event Classification & Unified Event Manager (EVENT_MANAGER)

> 2026-08-05. Establishes the single source of truth for in-game event inventory and the
> unified event manager design. The fog system (`docs/FOG_EVENTS.md`) was the prototype
> (GameEvent lifecycle + registry + manager); this document generalizes the pattern to all
> random in-game events and records the migration.

## 1. Purpose

The game contains many "events" of different shapes. Historically they were managed
inconsistently: fog events under a dedicated manager, random encounters (elite turret /
formation strike) with trigger logic inline in `spawner._process`, and flow/cinematic
sequences orchestrated by `main.gd`. This document:

1. Classifies every in-game event (one authoritative inventory);
2. Defines a **unified event manager** (`GameEventManager`, `GameState.events`) that
   batch-manages all *random* events through one registry / trigger policy / lifecycle /
   signal surface;
3. Records the migration map and the invariants that keep behavior unchanged.

Non-random, scene-bound sequences are **not** moved into the manager (see §3.C) — they are
tied to pause state, scene switching and input locking, which is orchestration, not event
scheduling.

## 2. Classification

| Class | Events | Shape | Managed by |
| --- | --- | --- | --- |
| **A. Fog interference** | `fake_enemies`, `mental_confusion`, `bullet_malfunction`, `direction_shift` | RefCounted `GameEvent` → `FogEvent` | `GameEventManager` fog group (registry/trigger/lifecycle); `FogEventManager` = effects layer + API facade |
| **B. Special encounters** | `elite_turret` (`EliteTurretEvent`), `formation_strike` (`FormationStrikeEvent`) | Node state machines (spawn entities, FSM) | `GameEventManager` encounter group (spawner-processing gate + event-owned cooldown) |
| **C. Flow / cinematic** | intro, return, orbital strike, mothership summon window, boss warning/spawn, boss enrage bullet-time, homecoming, death, give-up | Node sequences w/ pause + input lock | `main.gd` (orchestration) |
| **D. Entity signals** | `Enemy.died`, `Boss.died/enraged/escaped/phase_changed`, `TurretBattery.died`, `FormationCraft.died`, `Mothership.departed`, `Player.entry_finished`, … | per-entity notifications | unchanged (signals) |

### A. Fog interference (4)

Random transient interference on the player. Already batch-managed: registry
(`EVENT_FACTORIES`), weighted pick, `check_interval`/`trigger_chance`, `first_delay`
opening protection, `min_interval` cooldown, explicit `duration` auto-clear, single-event
concurrency, effects via signals (`fog_event_started/ended/fog_direction_shift`).
Fully independent of spawner / boss / encounters (fires during encounters today).

### B. Special encounters (2)

Random encounters that spawn entities and pause ordinary waves:

- `EliteTurretEvent`: FSM `IDLE → CARRIER_ENTER → TURRET_ACTIVE → CARRIER_EXIT → BOSS_DELAY`; freezes Boss scheduling (`_boss_frozen`/`_boss_pending` in spawner), pauses waves, 30 s countdown, reward on all-clear, own cooldown, `abort()` on homecoming.
- `FormationStrikeEvent`: FSM `IDLE → FORMATION_ENTER → FORMATION_TURN → BOMBING_RUN → FORMATION_EXIT`; lowest-priority random event, pauses waves (occupies wave slot), mutually exclusive with Boss + elite event, own cooldown, `abort()` on homecoming.

Trigger policy lives in `GameEventManager` (2026-08-05, §3.3): elite `trigger_interval` 45 s / `trigger_chance` 0.35 / `min_score` 800; formation 40 s / 0.3 / 500, plus each event's `can_trigger()` (cooldown / Boss active / mothership present / elite-active).

### C. Flow / cinematic

Scene-bound sequences: intro/return cinematics (freeze the tree, `process_mode=ALWAYS`),
orbital-strike clear, mothership summon show/warp-in, boss warning + enrage bullet-time,
homecoming, death replay, give-up. All orchestrated by `main.gd` because they share pause
state, input locking and camera/zoom concerns. **Deliberately outside** the random event
manager; their lifecycle is driven by scene flow, not by an event scheduler.

### D. Entity signals

Entity → owner notification (`died`, `enraged`, `departed`, …). Not "game events" in the
scheduling sense; remain ordinary signals.

## 3. Unified manager — `GameEventManager`

New `scripts/event_manager.gd` (`class_name GameEventManager extends Node`), mounted as a
service child of the `GameState` autoload (`GameState.events`, same convention as
`GameState.fog_events`).

### 3.1 Registry (single source of truth)

`EVENT_FACTORIES: Dictionary` — id → factory `Callable` → event instance, one line per
event. All 6 random events register here:

```gdscript
var EVENT_FACTORIES: Dictionary = {
	&"fake_enemies": func() -> GameEvent: return FakeEnemiesEvent.new(),
	&"mental_confusion": func() -> GameEvent: return ConfusionEvent.new(),
	&"bullet_malfunction": func() -> GameEvent: return BulletMalfunctionEvent.new(),
	&"direction_shift": func() -> GameEvent: return DirectionShiftEvent.new(),
	# 遭遇两条（elite_turret/formation_strike）不在字面量中——
	# 由 main._ready 经 register_encounter() 注入缓存单例（Node，附于 Main 容器下）。
}
```

- Fog events are per-trigger instances (unchanged semantics; each trigger gets a fresh
  event, duration auto-clear).
- Encounter events are **cached singletons** (Nodes attached under the injected Main
  container): they carry cooldown/FSM state across triggers, and tests access them via
  `main.event()` / `main.formation()`.

The manager drives events through a small **duck-typed contract** — no forced base class:

- `GameEvent` (RefCounted) for pure-effect events (fog): `start(ctx, duration)` /
  `tick(delta)` / `end()` / `is_active` / `request_end()`.
- Node events (encounters) keep their Node shape and public API (`is_active()`, `start()`,
  `abort()`, `can_trigger()`, `cooldown_left()`, `State` enums — test surface preserved);
  the manager calls `start()`/`abort()`, polls `is_active()` and respects
  `can_trigger()`/cooldown as the final gate.

### 3.2 Concurrency groups (behavior preservation)

Random events split into two **groups**; concurrency is single-active *within* a group,
parallel *across* groups:

- `"fog"`: the 4 fog events (single-active, as today);
- `"encounter"`: elite + formation (single-active, plus Boss/mothership mutex, as today).

This preserves the current behavior where a fog event can fire while an encounter is
running, and encounters never overlap each other or the Boss.

### 3.3 Trigger policy (unified)

Replaces both the fog `_process` policy (check interval / chance / first_delay /
min_interval) and the spawner `ScheduledEventTrigger` usage. Per-group, per-event config
read from the **existing** balance keys (no `balance.json` format change):

- fog group: `fog_events.enabled/trigger_chance/check_interval/min_interval/first_delay`
  + per-event `weights`/`durations`;
- encounter group: `elite_turret_event.trigger_interval/trigger_chance/min_score`,
  `formation_strike_event.trigger_interval/trigger_chance/min_score`, per-event cooldown.

Encounter auto-trigger is additionally gated by the injected spawner being *processing*
(`set_process(false)` in tests therefore disables encounter auto-trigger exactly as it
does today), and by each event's own `can_trigger()` (cooldown / Boss active / mothership
group / elite-active). Fog auto-trigger is gated by `run_active` (`main._ready` sets it
from `current_scene == self`, unchanged).

### 3.4 Lifecycle & signals

- Fog events: manager `start(ctx, duration)` → per-frame `tick(delta)` → duration
  expiry/`request_end()` → `end()` (GameEvent contract, unchanged).
- Encounter events: manager `start()`; the event self-drives its FSM (`_process`);
  the manager polls `is_active()` and emits `event_ended` on return to IDLE. Cooldown
  bookkeeping stays in the event (`cooldown_left()` / `set_cooldown_left()` — test API
  preserved).
- Unified signals: `event_started(event_id, duration)` (encounter events: duration 0 — FSM self-driven), `event_ended(event_id)`.
  `FogEventManager` re-emits `fog_event_started/ended` + `fog_direction_shift` (unchanged
  consumer surface: `player.gd`).

### 3.5 Mutex & spawner hooks

Trigger-time coordination lives in the manager (boss-active check, waves mutex via the
injected spawner); the *during-event* hooks (elite: `set_boss_frozen(true)` +
`set_waves_paused(true)`; formation: `set_waves_paused(true)`) stay inside the events
calling the injected spawner, because wave/boss state transitions are interior to the
event FSMs (e.g. waves resume at `CARRIER_EXIT`, Boss unfreezes at `BOSS_DELAY` end).

### 3.6 Public API (tests / diagnostics)

`set_run_active` / `is_run_active` / `force_trigger(id)` / `try_trigger_group(group)` /
`end_active(group)` / `end_all()` / `active_id(group)` / `active_event(group)` /
`event(id)` / `event_ids()` / `group_of(id)` / `can_trigger_group(group)` /
`set_cooldown_left(seconds)` / `cooldown_left()` / `set_first_delay_left(seconds)` /
`set_encounter_timer_remaining(id, seconds)` / `active_remaining()` / signals
`event_started(event_id, duration)` / `event_ended(event_id)`.

## 4. Migration map

| File | Change |
| --- | --- |
| `scripts/event_manager.gd` | **new**: `GameEventManager` (registry/groups/trigger/lifecycle/signals/API) |
| `scripts/fog_event_manager.gd` | keep class + full public API as the **fog effects layer + facade**: visual layers (fake container / overlay / banner), fog signals re-emitted from manager signals, accessors (`spawned_fakes`, `fake_container`, `overlay_*`, `emit_direction_shift`), config vars (`TRIGGER_CHANCE`, `CHECK_INTERVAL`, `MIN_INTERVAL`, `FIRST_DELAY`, `WEIGHTS`, `EVENT_DURATIONS`, `ENABLED`, `EVENT_FACTORIES`) proxied to manager fog-group config / shared registry; lifecycle forwards to `GameState.events` |
| `scripts/spawner.gd` | remove encounter trigger checks + `ScheduledEventTrigger` fields; keep accessors (`set_elite_event`/`elite_event`/`set_formation_event`/`formation_event`) + Boss/wave mutex hooks; add `notify_event_triggered()` (wave-slot reset) |
| `scripts/main.gd` | replace direct event creation with `GameState.events` wiring (container = self, spawner ref, run_active); `event()`/`formation()` forward to manager; homecoming/death use `GameState.events.end_all()` |
| `autoload/game_state.gd` | mount `GameState.events` (GameEventManager child) |
| `scripts/scheduled_event_trigger.gd` | retired (logic absorbed by manager) |
| tests | `fog_event_test` untouched (facade); encounter tests untouched if APIs preserved; mothership/orbital/summon tests keep `main.event().set_process(false)` |

## 5. Behavior preservation invariants

- Fog may fire during encounters; encounters never overlap each other/Boss; mothership
  presence blocks encounters (L13).
- `spawner.set_process(false)` disables encounter auto-trigger (manager gates on spawner
  processing) — same as today.
- `main.event()` / `main.formation()` / `spawner.elite_event()` / `spawner.formation_event()`
  keep returning the same instances.
- Event-owned state machines, cooldown and spawner hooks are untouched.
- `balance.json` keys unchanged (only the reader moves).

## 6. Adding a new event

1. New `GameEvent` subclass (RefCounted, pure effect) or Node event (spawns entities /
   state machine); fog-only events extend `FogEvent` (accessors via context).
2. Register one line in `GameEventManager.EVENT_FACTORIES` + assign `group` + trigger
   config (interval/chance/min_score) in the manager.
3. Optional per-event context accessors via an intermediate base class (`FogEvent` is the
   example); optional banner/comm text via translations.
4. Balance keys under the event's own `balance.json` section (reader added in manager).

## 7. Test strategy

- `fog_event_test.tscn` must pass **unchanged** (facade compatibility contract).
- `elite_turret_event_test.tscn` / `formation_strike_event_test.tscn`: unchanged unless a
  migration API drops; every assertion keeps its semantics.
- Mothership / orbital / summon / autoplay scenes: unchanged.
- New coverage: manager-level assertions (unified registry of 6 ids, group concurrency
  fog‖encounter, unified `event_started/ended` broadcast, spawner-processing gate).
- Gate: the 5 layers of `docs/TESTING.md` (format → lint → import warnings → compile+smoke
  → all 47 assertion scenes).
