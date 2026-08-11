# Event Classification & Unified Event Manager (EVENT_MANAGER)

> 2026-08-05. Establishes the single source of truth for in-game event inventory and the
> unified event manager design. The fog system (`docs/FOG_EVENTS.md`) was the prototype
> (GameEvent lifecycle + registry + manager); this document generalizes the pattern to all
> random in-game events and records the migration.
>
> 2026-08-08 全量迁移 C# 后:API 名同义 PascalCase(文中 §3 起已按 C# 更新;各 Manager 底部仍留
> snake_case 兼容桥)。

## 1. Purpose

The game contains many "events" of different shapes. Historically they were managed
inconsistently: fog events under a dedicated manager, random encounters (elite turret /
formation strike) with trigger logic inline in `Spawner._Process`, and flow/cinematic
sequences orchestrated by `Main` (`csharp/godot/Main.cs`). This document:

1. Classifies every in-game event (one authoritative inventory);
2. Defines a **unified event manager** (`GameEventManager`, `GameState.Events`) that
   batch-manages all *random* events through one registry / trigger policy / lifecycle /
   signal surface;
3. Records the invariants that keep behavior unchanged.

Non-random, scene-bound sequences are **not** moved into the manager (see §2.C) — they are
tied to pause state, scene switching and input locking, which is orchestration, not event
scheduling.

## 2. Classification

| Class | Events | Shape | Managed by |
| --- | --- | --- | --- |
| **A. Fog interference** | `fake_enemies`, `mental_confusion`, `bullet_malfunction`, `direction_shift` | RefCounted `GameEvent` → `FogEvent` | `GameEventManager` fog group (registry/trigger/lifecycle); `FogEventManager` = effects layer + API facade |
| **B. Special encounters** | `elite_turret` (`EliteTurretEvent`), `formation_strike` (`FormationStrikeEvent`) | Node state machines (spawn entities, FSM) | `GameEventManager` encounter group (spawner-processing gate + event-owned cooldown) |
| **C. Flow / cinematic** | intro, return, orbital strike, mothership summon window, boss warning/spawn, boss enrage bullet-time, homecoming, death, give-up | Node sequences w/ pause + input lock | `Main` (orchestration) |
| **D. Entity signals** | `Enemy.Died`, `Boss.Died/Enraged/Escaped/PhaseChanged`, `TurretBattery.Died`, `FormationCraft.Died`, `Mothership.Departed`, `Player.EntryFinished`, … | per-entity notifications | unchanged (signals) |

### A. Fog interference (4)

Random transient interference on the player. Already batch-managed: registry
(`EVENT_FACTORIES`), weighted pick, `check_interval`/`trigger_chance`, `first_delay`
opening protection, `min_interval` cooldown, explicit `duration` auto-clear, single-event
concurrency, effects via signals (`FogEventStarted`/`FogEventEnded`/`FogDirectionShift`).
Fully independent of spawner / boss / encounters (fires during encounters today).

### B. Special encounters (2)

Random encounters that spawn entities and pause ordinary waves:

- `EliteTurretEvent`: FSM `IDLE → CARRIER_ENTER → TURRET_ACTIVE → CARRIER_EXIT → BOSS_DELAY`; freezes Boss scheduling (`_bossFrozen`/`_bossPending` in spawner), pauses waves, 30 s countdown, reward on all-clear, own cooldown, `Abort()` on homecoming.
- `FormationStrikeEvent`: FSM `IDLE → FORMATION_ENTER → FORMATION_TURN → BOMBING_RUN → FORMATION_EXIT`; lowest-priority random event, pauses waves (occupies wave slot), mutually exclusive with Boss + elite event, own cooldown, `Abort()` on homecoming.

Trigger policy lives in `GameEventManager` (2026-08-05, §3.3): elite `trigger_interval` 45 s / `trigger_chance` 0.35 / `min_score` 800; formation 40 s / 0.3 / 500, plus each event's `CanTrigger()` (cooldown / Boss active / mothership present / elite-active).

### C. Flow / cinematic

Scene-bound sequences: intro/return cinematics (freeze the tree, `process_mode=ALWAYS`),
orbital-strike clear, mothership summon show/warp-in, boss warning + enrage bullet-time,
homecoming, death replay, give-up. All orchestrated by `Main` (`csharp/godot/Main.cs`) because they share pause
state, input locking and camera/zoom concerns. **Deliberately outside** the random event
manager; their lifecycle is driven by scene flow, not by an event scheduler.

### D. Entity signals

Entity → owner notification (`Died`, `Enraged`, `Departed`, …). Not "game events" in the
scheduling sense; remain ordinary signals.

## 3. Unified manager — `GameEventManager`

New `csharp/godot/GameEventManager.cs` (`GameEventManager : Node`), held as a
service child of the `GameState` autoload (`GameState.Events`, same convention as
`GameState.FogEvents`).

### 3.1 Registry (single source of truth)

`EVENT_FACTORIES: Dictionary` — id → factory `Callable` → event instance, one line per
event. All 6 random events register here:

```csharp
public Godot.Collections.Dictionary EVENT_FACTORIES { get; set; } = new()
{
    [new StringName("fake_enemies")] = Callable.From(() => new FakeEnemiesEvent()),
    [new StringName("mental_confusion")] = Callable.From(() => new ConfusionEvent()),
    [new StringName("bullet_malfunction")] = Callable.From(() => new BulletMalfunctionEvent()),
    [new StringName("direction_shift")] = Callable.From(() => new DirectionShiftEvent()),
    // 遭遇两条（elite_turret/formation_strike）不在字面量中——
    // 由 Main._Ready 经 RegisterEncounter() 注入缓存单例（Node，附于 Main 容器下）。
};
```

- Fog events are per-trigger instances (unchanged semantics; each trigger gets a fresh
  event, duration auto-clear).
- Encounter events are **cached singletons** (Nodes attached under the injected Main
  container): they carry cooldown/FSM state across triggers, and tests access them via
  `Main.Event()` / `Main.Formation()`.

The manager drives events through a small **duck-typed contract** — no forced base class:

- `GameEvent` (RefCounted) for pure-effect events (fog): `Start(ctx, duration)` /
  `Tick(delta)` / `End()` / `IsActive` / `RequestEnd()`.
- Node events (encounters) keep their Node shape and public API (`IsActive()`, `Start()`,
  `Abort()`, `CanTrigger()`, `CooldownLeft()`, `State` enums — test surface preserved);
  the manager calls `Start()`/`Abort()`, polls `IsActive()` and respects
  `CanTrigger()`/cooldown as the final gate.

### 3.2 Concurrency groups (behavior preservation)

Random events split into two **groups**; concurrency is single-active *within* a group,
parallel *across* groups:

- `"fog"`: the 4 fog events (single-active, as today);
- `"encounter"`: elite + formation (single-active, plus Boss/mothership mutex, as today).

This preserves the current behavior where a fog event can fire while an encounter is
running, and encounters never overlap each other or the Boss.

### 3.3 Trigger policy (unified)

Replaces both the fog `_Process` policy (check interval / chance / first_delay /
min_interval) and the spawner `ScheduledEventTrigger` usage. Per-group, per-event config
read from the **existing** balance keys (no `balance.json` format change):

- fog group: `fog_events.enabled/trigger_chance/check_interval/min_interval/first_delay`
  + per-event `weights`/`durations`;
- encounter group: `elite_turret_event.trigger_interval/trigger_chance/min_score`,
  `formation_strike_event.trigger_interval/trigger_chance/min_score`, per-event cooldown.

Encounter auto-trigger is additionally gated by the injected spawner being *processing*
(`SetProcess(false)` in tests therefore disables encounter auto-trigger exactly as it
does today), and by each event's own `CanTrigger()` (cooldown / Boss active / mothership
group / elite-active). Fog auto-trigger is gated by run-active (`Main._Ready` sets it via
`SetRunActive(CurrentScene == this)`, unchanged).

### 3.4 Lifecycle & signals

- Fog events: manager `Start(ctx, duration)` → per-frame `Tick(delta)` → duration
  expiry/`RequestEnd()` → `End()` (GameEvent contract, unchanged).
- Encounter events: manager `Start()`; the event self-drives its FSM (`_Process`);
  the manager polls `IsActive()` and emits `EventEnded` on return to IDLE. Cooldown
  bookkeeping stays in the event (`CooldownLeft()` / `SetCooldownLeft()` — test API
  preserved).
- Unified signals: `EventStarted(event_id, duration)` (encounter events: duration 0 — FSM self-driven), `EventEnded(event_id)`.
  `FogEventManager` re-emits `FogEventStarted`/`FogEventEnded` + `FogDirectionShift` (unchanged
  consumer surface: `csharp/godot/Player.cs`).

### 3.5 Mutex & spawner hooks

Trigger-time coordination lives in the manager (boss-active check, waves mutex via the
injected spawner); the *during-event* hooks (elite: `SetBossFrozen(true)` +
`SetWavesPaused(true)`; formation: `SetWavesPaused(true)`) stay inside the events
calling the injected spawner, because wave/boss state transitions are interior to the
event FSMs (e.g. waves resume at `CARRIER_EXIT`, Boss unfreezes at `BOSS_DELAY` end).

### 3.6 Public API (tests / diagnostics)

`SetRunActive` / `IsRunActive` / `ForceTrigger(id)` / `TryTriggerGroup(group)` /
`EndActive(group)` / `EndAll()` / `ActiveId(group)` / `ActiveEvent(group)` /
`Event(id)` / `EventIds()` / `GroupOf(id)` / `CanTriggerGroup(group)` /
`SetCooldownLeft(seconds)` / `CooldownLeft()` / `SetFirstDelayLeft(seconds)` /
`SetEncounterTimerRemaining(id, seconds)` / `EncounterTimerRemaining(id)` /
`ActiveRemaining()` / signals `EventStarted(event_id, duration)` / `EventEnded(event_id)`.

## 5. Behavior preservation invariants

- Fog may fire during encounters; encounters never overlap each other; elite encounter
  freezes Boss while active, formation strike does not (Boss fires on schedule; AC28);
  mothership presence blocks encounters (L13).
- `Spawner.SetProcess(false)` disables encounter auto-trigger (manager gates on spawner
  processing) — same as today.
- `Main.Event()` / `Main.Formation()` / `Spawner.EliteEvent()` / `Spawner.FormationEvent()`
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

Gate: the layers of `docs/TESTING.md` (C# build/test/format → zero-GDScript → import warnings
→ BALANCE_MAP zero-diff → compile+smoke → all assertion scenes; authoritative count lives there).
