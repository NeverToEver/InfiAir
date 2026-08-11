# Elite Turret Encounter Event — Design Doc

> Status: **Implemented** (landed 2026-07-28, full validation passed; details in "Implementation Notes" at end). Values from `data/balance.json` + existing scripts; new params in its top-level `elite_turret_event` block, scripts keep same-named fallback defaults (project numeric convention).

## 3. Turret mechanics & per-difficulty config

### 3.1 Common mechanics
- **Raise**: carrier entry (~2s to hover) → lids open, turrets rise & charge (~1.5s) → **30s countdown starts at charge completion**.
- **Targeting**: per-turret independent rotation toward player (speed-limited rotation — `turn_rate` rad/s cap, per-frame stepped increment, "mechanical turntable"); fire direction = turret heading + random spread, not exact — "**weak lock**".
- **Weak-lock params** (`elite_turret_event.weak_lock`): homing turn rate **1.5** (existing 4.0), `homing_time` only 0.6s; straight ammo +**±7°** muzzle spread; hit-rate target ~50-60% on stationary player, lateral strafing reliably evades.
- **Fire cadence**: per-turret timer, 2.0~2.4s interval (normal `fire_interval` scale); ammo from preset pool rotated **by preset sequence** (below; config may switch to `random`).
- **Destructible**: each turret = independent Area2D (layer 3=enemy, `enemy` group, registered `GameState.Enemies`); hit-flash + individual segmented HP bar (`SegmentedBar` style); destroyed → `Explosion.SpawnAt()` + base ring off.

### 3.2 Per-difficulty config

HP = normal-typical 80 × difficulty `hp` coefficient × run-progression ramp (`GameState.EnemyHpRamp()`: 1.0 + 0.25×(difficulty mult −1), grows with boss kills), rounded; ammo all reused from existing enemy-side ammo types (no new bullet types).

| Difficulty | Turrets | HP each | Ammo sequence (rotation) |
| --- | --- | --- | --- |
| easy | 3 | **60** (80×0.75) | straight (420/12) → fan (340/10, 3-shot) → straight |
| medium | 4 | **80** (typical) | straight → fan → laser (720/20) → weak homing (300/12) |
| hard | 5 | **120** (80×1.5) | fan (5-shot) → laser → weak homing → boss sniper (650/21) → straight |

Config example (`balance.json` new block):

```json
"elite_turret_event": {
	"duration": 30.0,
	"boss_resume_delay": 4.0,
	"turret_hp_base": 80,
	"turret_counts": { "easy": 3, "medium": 4, "hard": 5 },
	"fire_interval": [2.0, 2.4],
	"weak_lock": { "turn_rate": 2.0, "homing_turn_rate": 1.5, "homing_time": 0.6, "spread_deg": 7.0 },
	"reward_score": 500
}
```

## 4. Immersive dialogue system

### 4.1 Line pool
10 lines, zh+en bilingual; keys `ETQ_1`~`ETQ_10` (text lives in `data/translations.csv`). Fail/retreat line: `ETQ_RETREAT` (outside the 10-pool).

### 4.2 Binding & playback
- Event start: draw **3 lines without replacement** from 10, bound in order to 3 progress nodes: ① destroyed ≥ ⌈total/3⌉ (≥1) → line 1; ② ≥ ⌈total×2/3⌉ → line 2; ③ all destroyed → line 3 (before success settlement).
- Event fail (timeout retreat): no bound lines; fixed retreat line instead.
- Presentation: bottom-left comm overlay — hex chamfered avatar frame (reuse `ChamferedPanel`, magenta rim + carrier insignia silhouette) + typewriter subtitle, 3.5s then fade; **game not paused** (`process_mode` follows gameplay); new line overrides unfinished old line; short comm-noise SFX (existing pool).

## 5. Timeline & reward settlement

### 5.1 Timeline
```text
t=0 trigger (§6) → 0-2s carrier descends/hovers (engines ramp, light shake)
→ 2-3.5s turrets rise & charge (monitorable=false) → 3.5s ★ 30s countdown
(HUD top bar + remaining icons) → 3.5-33.5s combat
A: all destroyed (t≤33.5) → success | B: timeout → fail
```

### 5.2 Settlement pseudocode
```csharp
void OnAllTurretsDestroyed()
{
	_comm.ShowLine(_lines[2]);         // bound line 3
	GameState.Instance.AddScore(RewardScore);  // base 500; difficulty mult ×1/×2/×3 → 500/1000/1500
	// no AddBossKill() RP / boss_kills / difficulty growth
	CarrierRetreat(victorious: false);  // damaged retreat (smoke + slow)
	// boss resume: OnCarrierExited → Schedule(BossResumeDelay, OnBossDelayEnd)  // §6
}

void OnEventTimeout()
{
	foreach (var turret in _turrets)
		turret.CeaseFireAndRetract();    // retract; no more ammo
	_comm.ShowLine("ETQ_RETREAT");     // fixed retreat line, no reward
	CarrierRetreat(victorious: true);   // full retreat (accelerated rise + fade)
	// boss resume as above
}

void CarrierRetreat(bool victorious)
{
	// Boss escape param family (start_speed/accel); surviving enemy bullets
	// despawn naturally off-screen (no player bullet_clear)
}
```
Normal wave spawning **paused** during event (aligns with spawner suppression during boss fights); mothership unaffected.

## 6. Boss-event mutex state machine

### 6.1 Constraints
Boss scheduling in `csharp/godot/Spawner.cs` (`boss_score_step=1500` score steps + `boss_time_limit=120s`). Mutex: both never on screen; boss trigger frozen at most once, never accumulated.

### 6.2 State machine
```text
IDLE → [boss ready & event IDLE] → original boss flow
IDLE → [event trigger met] → CARRIER_ENTER → TURRET_ACTIVE (30s)
  → success/fail → CARRIER_EXIT → BOSS_DELAY (4.0s = boss_resume_delay)
  → IDLE; frozen pending boss → trigger once, clear flag (no accumulation)
```

### 6.3 Rules
- **Trigger mutex**: same-frame race → encounter may win (autoload `_Process` runs before main scene `Spawner`); boss trigger is deferred until event end + `boss_resume_delay`, never lost. Event starts only when boss not in warn/enter/fight. **2026-07-29**: also blocked during bombing-formation event — both share spawner `_wavesPaused` wave-pause hook, so one cannot end early-resuming the other's pause.
- **Freeze**: on `CARRIER_ENTER` set `_bossFrozen = true`; if boss score step elapses meanwhile, no boss — set `_bossPending = true` (recorded once; repeat overwrites same flag — no accumulation).
- **Resume**: at `BOSS_DELAY` end: if `_bossPending`, immediately start boss warn flow and clear it; `_bossFrozen` resets. If boss condition not yet due at event end, resume original score-step timer, no compensation.
- **Edge**: crossing boss score steps during event is normal (reward 500~1500); freeze guarantees boss appears only 4s after carrier leaves — no stacked barrages. **Failure also unfreezes**: success/fail don't change mutex recovery, only rewards.
- Test: add `test/elite_turret_event_test.tscn` asserting 30s timer, 3 dialogue nodes, reward crediting (incl. difficulty mult), boss freeze/resume and single-shot no-accumulation semantics.

## Implementation notes (landed 2026-07-28)

> **2026-08-05**: trigger policy moved from `spawner._process` into the unified event manager (`GameEventManager`, `docs/EVENT_MANAGER.md`) — same `elite_turret_event.trigger_*`/`min_score`/`cooldown` balance keys, same mutex semantics (Boss/waves/mothership); event keeps its FSM + spawner hooks. `ScheduledEventTrigger` retired.

### Files

| File | Content |
| --- | --- |
| `data/balance.json → elite_turret_event` | all tunables: duration/entry/raise/resume delays, trigger (`min_score=800`, `trigger_interval=45s`, `trigger_chance=0.35`, `cooldown=60s`), `turret_hp_base=80`, `turret_counts` (3/4/5), `fire_interval=[2.0,2.4]`, `weak_lock`, `ammo_sequences`, `reward_score=500`, `carrier`. Fallbacks in scripts. |
| `data/translations.csv` | `ETQ_1`~`ETQ_10`, `ETQ_RETREAT`, `ETV_TITLE`, `ETV_TURRETS` (zh+en; re-import → `.translation`). No new pages → `docs/EXIT_FLOW.md` unchanged. |
| `scripts/tools/generate_enemy_sprites.py` | `strike_carrier()` (1200×700) + `turret()` (96×96), `Ship` primitives; `TURRET_WELLS` = runtime `StrikeCarrier.Sockets` 1:1. Outputs `strike_carrier.png`, `elite_turret.png` to `assets/sprites/`. |
| `csharp/godot/StrikeCarrier.cs` (`public partial class StrikeCarrier`) | ENTER (2s ease-out descent + fade-in + settle shake) → HOVER (±6px) → RETREAT (Boss escape params: start_speed 120 / accel 420; damaged ×0.55 + darken + deck explosions). No collision layer. 5 base rings = Line2D status lights (magenta charging / off destroyed; standby dark-red baked). |
| `csharp/godot/TurretBattery.cs` + `scenes/turret.tscn` (`public partial class TurretBattery`) | Area2D layer 3 (enemy), `enemy` group + `GameState.Enemies` (auto-hit by player-side fire). Rise = TRANS_BACK scale-in, `Monitoring=false` (K09: `Monitorable=false` too — monitoring doesn't stop area_entered); attackable after `Activate()`. Magenta SegmentedBar (8 segments), hit-flash; destroyed → `Explosion.SpawnAt()` + shake + ring off. Timeout → `CeaseFireAndRetract()` + self-free. |
| `csharp/godot/CommOverlay.cs` (`public partial class CommOverlay`) | CanvasLayer layer=12: ChamferedPanel magenta rim + typewriter (30ms/char), holds 3.5s then 0.5s fade; new line overrides old; no pause; SFX = `bullet_fire_c.wav` via `GameState.PlaySfx`. |
| `csharp/godot/EliteTurretEvent.cs` (`public partial class EliteTurretEvent`) | `IDLE → CARRIER_ENTER →（raise）→ TURRET_ACTIVE → CARRIER_EXIT → BOSS_DELAY → IDLE`; created by `Main._Ready()` under Main, registered to GameEventManager (`GameState.Events.RegisterEncounter()`, 2026-08-05); 3-of-10 draw, 3-node binding, 30s countdown (0.1s throttled HUD), settlement, boss unfreeze/retrigger. |
| `csharp/godot/Hud.cs` | Event bar (top center, below boss HP bar): `ETV_TITLE` + 30-segment magenta SegmentedBar + `ETV_TURRETS` remaining (on change only); `ShowEventBar`/`UpdateEventBar`/`HideEventBar`; refreshes on locale switch. |
| `csharp/godot/Spawner.cs` | New `_bossFrozen`/`_bossPending`/`_wavesPaused` + `_event` ref; boss check during freeze records pending only (no accumulation); event check after boss check (boss priority); waves paused while `_wavesPaused`. |
| `csharp/godot/Main.cs` | `_Ready` creates `EliteTurretEvent` under Main (visible to cleanup/test traversal), registers `_spawner.SetEliteEvent(_event)`. |
| `csharp/godot/Bullet.cs` | New `HomingTurnRate` field (default 4.0, reset in `Activate()`); weak homing = 1.5; hardcoded 4.0 replaced by field read. |
| `test/elite_turret_event_test.tscn` | 60 assertions (2026-08-07). |

### Deviations from draft (final behavior)
- **Trigger**: score ≥ 800, then 35% chance per 45s, 60s cooldown after event end — all in `elite_turret_event` config; same-frame race → encounter may win (AB20: autoload `_Process` precedes main scene `Spawner`; boss deferred, never lost).
- **Wave pause window**: `CARRIER_ENTER` → `CARRIER_EXIT` (existing spawner doesn't suppress waves during boss fights); boss freeze held until `BOSS_DELAY` end.
- **Ring light**: standby dark-red baked into texture (5 bases); charge/destroy = runtime Line2D overlay; no separate lid parts (raise = TRANS_BACK scale-in).
- **Dialogue boundary**: line 2 requires "before all destroyed" (mutex with line 3); cross-node last hit (splash multi-kill) → new line overrides old.
