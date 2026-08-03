# Elite Turret Encounter Event — Design Doc

> Status: **Implemented** (landed 2026-07-28, full validation passed; details in "Implementation Notes" at end). Values from `data/balance.json` + existing scripts; new params in its top-level `elite_turret_event` block, scripts keep same-named fallback defaults (project numeric convention).

## 1. Sampled analysis summary

### 1.1 Existing ammo types

| Ammo | Source | Speed | Dmg | Behavior |
| --- | --- | --- | --- | --- |
| Straight | normal `enemies.bullet_speed` | 420 | 12 | linear, red |
| Fan | normal `enemies.spread_bullet_speed` | 340 | 10 | fan, `spread_fan_step≈0.314rad` |
| Laser | normal `enemies.laser_bullet_speed` | 720 | 20 | fast linear long |
| Boss fan | `boss.fan_bullet_speed` | 380 | 14 | slow wide suppression |
| Boss homing | `boss.homing_bullet_speed` | 300 | 12 | `homing=true`; `lerp_angle(4.0·dt)` within `homing_time` |
| Boss sniper | `boss.sniper_bullet_speed` | 650 | 21 | fast precise single |
| Boss cross | `boss.cross_bullet_speed` | 260 | 12 | slow 4-way/cross |
| Enrage volley | `boss.enrage` | 820 / 240 | 21 / 12 | preset-path volleys (enrage) |
| Mothership gatling | `mothership.gatling` | 1080 | 8 | player-side strafing |
| Mothership missile | `mothership.missile` | 600 | 80 + splash 20/r80 | multi-homing + AoE |
| Laser beam (buff) | `buffs.laser_beam` | — (line check) | 10/0.1s tick | player buff weapon, not a bullet |

All bullets via `scenes/bullet.tscn` + `GameState.bullet_pool.fire()`; faction in `setup()/activate()`; homing via `bullet.gd` `homing`/`homing_time` (4.0 rad-class lerp). **Event reuses enemy-side ammo only; no new bullet types.**

### 1.2 Normal enemy HP

`balance.json → enemies.types[].hp` (medium baseline):

- Range **48 ~ 112 HP** (types 65-72 / 48-56 / 95-112 / 56-66, resampled 2026-08-02); typical **~65-72 HP**
- `difficulty.hp`: easy ×0.75 / medium ×1.0 / hard ×1.5
- Ref: elite 135-210; Boss 800 × hp_mults; turrets = normal-unit tier HP.

### 1.3 Boss kill score

- `boss.gd._die()` → `GameState.add_boss_kill(score_scale)` → `add_score(int(500.0 × score_scale))`; base **500** (score_scale usually 1.0)
- `add_score()` applies difficulty mult **×1 / ×2 / ×3** → credited 500 / 1000 / 1500
- Not reused by event: RP reward, boss_kills count, difficulty growth

### 1.4 Art style keywords (from `scripts/tools/generate_enemy_sprites.py` + `assets/sprites/`)

- Mirror-symmetric facet-cut crystal hull; sharp poly outlines, blade/claw wings, fork nose; nose up (root rotation=PI)
- Hull `HULL_A~D = (22,18,34)~(62,52,92)`; seams near-black `(10,8,18)`; rim light purple `(150,140,185)`
- Accent: normal crimson `(255,72,56)`; elite magenta `(255,64,190)`; boss amber/violet/ruby
- Two-layer draw (body + blur glow); energy core = accent circle + white core; tail engine ellipse; 4× AA; neon along edges/ridges

## 2. Strike carrier visual redesign

### 2.1 Role
Background-scale giant (not Boss, not in boss rotation); descends from off-screen top, hovers rear upper-mid (`hover_y=270` tier, ≥60% screen width) as deployment "stage". Hull **not attackable** (no collision layer); only raised turrets destructible.

### 2.2 Shape & color
- Elongated hex spindle, ~1.6-1.8× Boss sprite (410px); Boss-3 "pillar" hex-fortress facets spread horizontally: central tall hex prism + stepped shrinking "deck wing platforms" (tops = turret bases); bridge = three-tier tapered hex tower (boss_3 style) + horizontal magenta neon "observation slit".
- Turret wells: 1-2 octagonal recessed bases per platform, armored lids when stowed (`SEAM` outline); lids rotate open on raise; turret = small hex prism + single crystal barrel, energy core in muzzle. Tail: three engine glows (big center, small sides).
- `HULL_A~D` dark violet facets (Boss-tier = "heavy armor"); accent = **elite magenta `(255,64,190)`**; core `ELITE_CORE (215,135,255)`; seam/rim/neon/glow identical to generator (`SEAM`/`RIM`). Deck plates `HULL_C/D` bright (horizontal), vertical faces `HULL_A/B` dark → top-down volume.

### 2.3 Key visual markers
- Faction emblem: central hex neon insignia (magenta rim + white core) = "elite fleet flagship" ID.
- Lights: magenta neon along wing-platform leading edges (boss_1 style); each base ring = octagonal neon status light — **standby dark red → charging magenta bright → destroyed = ring off**; remaining turrets readable at a glance.
- Event opening: fade-in from top + descent, engine glows ramp, one low-intensity screen shake (reuse `effects.shake.mothership=4.0` scale).

## 3. Turret mechanics & per-difficulty config

### 3.1 Common mechanics
- **Raise**: carrier entry (~2s to hover) → lids open, turrets rise & charge (~1.5s) → **30s countdown starts at charge completion**.
- **Targeting**: per-turret independent rotation toward player (`lerp_angle` eased, capped turn speed = "mechanical turntable"); fire direction = turret heading + random spread, not exact — "**weak lock**".
- **Weak-lock params** (`elite_turret_event.weak_lock`): homing turn rate **1.5** (existing 4.0), `homing_time` only 0.6s; straight ammo +**±7°** muzzle spread; hit-rate target ~50-60% on stationary player, lateral strafing reliably evades.
- **Fire cadence**: per-turret timer, 2.0~2.4s interval (normal `fire_interval` scale); ammo from preset pool rotated **by preset sequence** (below; config may switch to `random`).
- **Destructible**: each turret = independent Area2D (layer 3=enemy, `enemy` group, registered `GameState.enemies`); hit-flash + individual segmented HP bar (`ui_segmented_bar` style); destroyed → `Explosion.spawn_at()` + base ring off.

### 3.2 Per-difficulty config

HP = normal-typical 80 × difficulty `hp` coefficient, rounded; ammo all reused from §1.1.

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
	"weak_lock": { "homing_turn_rate": 1.5, "homing_time": 0.6, "spread_deg": 7.0 },
	"reward_score": 500
}
```

### 3.3 Balance check
30s window: player DPS ≈ 10/0.15s ≈ 67/s; wipe total 180/320/600 → pure-hit 2.7s / 4.8s / 9.0s; medium needs ~1/3 of event time. Gradient = turret count × spread + ammo density (consistent: ×1/×1.5 HP).

## 4. Immersive dialogue system

### 4.1 Line pool
10 lines, zh+en bilingual; keys `ETQ_1`~`ETQ_10` (text lives in `data/translations.csv`). Fail/retreat line: `ETQ_RETREAT` (outside the 10-pool).

### 4.2 Binding & playback
- Event start: draw **3 lines without replacement** from 10, bound in order to 3 progress nodes: ① destroyed ≥ ⌈total/3⌉ (≥1) → line 1; ② ≥ ⌈total×2/3⌉ → line 2; ③ all destroyed → line 3 (before success settlement).
- Event fail (timeout retreat): no bound lines; fixed retreat line instead.
- Presentation: bottom-left comm overlay — hex chamfered avatar frame (reuse `ui_chamfered_panel`, magenta rim + carrier insignia silhouette) + typewriter subtitle, 3.5s then fade; **game not paused** (`process_mode` follows gameplay); new line overrides unfinished old line; short comm-noise SFX (existing pool).

## 5. Timeline & reward settlement

### 5.1 Timeline
```text
t=0 trigger (§6) → 0-2s carrier descends/hovers (engines ramp, light shake)
→ 2-3.5s turrets rise & charge (monitorable=false) → 3.5s ★ 30s countdown
(HUD top bar + remaining icons) → 3.5-33.5s combat
A: all destroyed (t≤33.5) → success | B: timeout → fail
```

### 5.2 Settlement pseudocode
```gdscript
func _on_all_turrets_destroyed() -> void:
	_play_commander_line(3)            # bound line 3
	GameState.add_score(500)          # base 500; difficulty mult ×1/×2/×3 → 500/1000/1500
	# no add_boss_kill() RP / boss_kills / difficulty growth
	_carrier_retreat(victorious := false)  # damaged retreat (smoke + slow)
	_schedule_boss_resume()           # §6

func _on_event_timeout() -> void:
	for turret in _living_turrets:
		turret.cease_fire_and_retract()    # retract; no more ammo
	_play_retreat_line()              # fixed retreat line, no reward
	_carrier_retreat(victorious := true)   # full retreat (accelerated rise + fade)
	_schedule_boss_resume()

func _carrier_retreat(victorious: bool) -> void:
	# Boss escape param family (start_speed/accel); surviving enemy bullets
	# despawn naturally off-screen (no player bullet_clear)
```
Normal wave spawning **paused** during event (aligns with spawner suppression during boss fights); mothership unaffected.

## 6. Boss-event mutex state machine

### 6.1 Constraints
Boss scheduling in `spawner.gd` (`boss_score_step=1500` score steps + `boss_time_limit=90s`). Mutex: both never on screen; boss trigger frozen at most once, never accumulated.

### 6.2 State machine
```text
IDLE → [boss ready & event IDLE] → original boss flow
IDLE → [event trigger met] → CARRIER_ENTER → TURRET_ACTIVE (30s)
  → success/fail → CARRIER_EXIT → BOSS_DELAY (4.0s = boss_resume_delay)
  → IDLE; frozen pending boss → trigger once, clear flag (no accumulation)
```

### 6.3 Rules
- **Trigger mutex**: same-frame race → boss wins (score-milestone promise); event starts only when boss not in warn/enter/fight. **2026-07-29**: also blocked during bombing-formation event — both share spawner `_waves_paused` wave-pause hook, so one cannot end early-resuming the other's pause.
- **Freeze**: on `CARRIER_ENTER` set `_boss_frozen = true`; if boss score step elapses meanwhile, no boss — set `_boss_pending = true` (recorded once; repeat overwrites same flag — no accumulation).
- **Resume**: at `BOSS_DELAY` end: if `_boss_pending`, immediately start boss warn flow and clear it; `_boss_frozen` resets. If boss condition not yet due at event end, resume original score-step timer, no compensation.
- **Edge**: crossing boss score steps during event is normal (reward 500~1500); freeze guarantees boss appears only 4s after carrier leaves — no stacked barrages. **Failure also unfreezes**: success/fail don't change mutex recovery, only rewards.
- Test: add `test/elite_turret_event_test.tscn` asserting 30s timer, 3 dialogue nodes, reward crediting (incl. difficulty mult), boss freeze/resume and single-shot no-accumulation semantics.

## Implementation notes (landed 2026-07-28)

### Files

| File | Content |
| --- | --- |
| `data/balance.json → elite_turret_event` | all tunables: duration/entry/raise/resume delays, trigger (`min_score=800`, `trigger_interval=45s`, `trigger_chance=0.35`, `cooldown=60s`), `turret_hp_base=80`, `turret_counts` (3/4/5), `fire_interval=[2.0,2.4]`, `weak_lock`, `ammo_sequences`, `reward_score=500`, `carrier`. Fallbacks in scripts. |
| `data/translations.csv` | `ETQ_1`~`ETQ_10`, `ETQ_RETREAT`, `ETV_TITLE`, `ETV_TURRETS` (zh+en; re-import → `.translation`). No new pages → `docs/EXIT_FLOW.md` unchanged. |
| `scripts/tools/generate_enemy_sprites.py` | `strike_carrier()` (1200×700) + `turret()` (96×96), `Ship` primitives; `TURRET_WELLS` = runtime `StrikeCarrier.SOCKETS` 1:1. Outputs `strike_carrier.png`, `elite_turret.png` to `assets/sprites/`. |
| `scripts/strike_carrier.gd` (`class_name StrikeCarrier`) | ENTER (2s ease-out descent + fade-in + settle shake) → HOVER (±6px) → RETREAT (Boss escape params: start_speed 120 / accel 420; damaged ×0.55 + darken + deck explosions). No collision layer. 5 base rings = Line2D status lights (magenta charging / off destroyed; standby dark-red baked). |
| `scripts/turret_battery.gd` + `scenes/turret.tscn` (`class_name TurretBattery`) | Area2D layer 3 (enemy), `enemy` group + `GameState.enemies` (auto-hit by player-side fire). Rise = TRANS_BACK scale-in, `monitoring=false` (K09: `monitorable=false` too — monitoring doesn't stop area_entered); attackable after `activate()`. Magenta SegmentedBar (8 segments), hit-flash; destroyed → `Explosion.spawn_at()` + shake + ring off. Timeout → `cease_fire_and_retract()` + self-free. |
| `scripts/comm_overlay.gd` (`class_name CommOverlay`) | CanvasLayer layer=12: ChamferedPanel magenta rim + typewriter (30ms/char), holds 3.5s then 0.5s fade; new line overrides old; no pause; SFX = `bullet_fire_c.wav` via `GameState.play_sfx`. |
| `scripts/elite_turret_event.gd` (`class_name EliteTurretEvent`) | `IDLE → CARRIER_ENTER →（raise）→ TURRET_ACTIVE → CARRIER_EXIT → BOSS_DELAY → IDLE`; created by `main.gd._ready` under Main, registered to spawner; 3-of-10 draw, 3-node binding, 30s countdown (0.1s throttled HUD), settlement, boss unfreeze/retrigger. |
| `scripts/hud.gd` | Event bar (top center, below boss HP bar): `ETV_TITLE` + 30-segment magenta SegmentedBar + `ETV_TURRETS` remaining (on change only); `show_event_bar/update_event_bar/hide_event_bar`; refreshes on locale switch. |
| `scripts/spawner.gd` | New `_boss_frozen`/`_boss_pending`/`_waves_paused` + `_event` ref; boss check during freeze records pending only (no accumulation); event check after boss check (boss priority); waves paused while `_waves_paused`. |
| `scripts/main.gd` | `_ready` creates `EliteTurretEvent` under Main (visible to cleanup/test traversal), registers `_spawner._event`. |
| `scripts/bullet.gd` | New `homing_turn_rate` field (default 4.0, reset in `activate()`); weak homing = 1.5; hardcoded 4.0 replaced by field read. |
| `test/elite_turret_event_test.tscn` | 45 assertions (below). |

### Deviations from draft (final behavior)
- **Trigger**: score ≥ 800, then 35% chance per 45s, 60s cooldown after event end — all in `elite_turret_event` config; spawner check order → boss priority on same-frame race.
- **Wave pause window**: `CARRIER_ENTER` → `CARRIER_EXIT` (existing spawner doesn't suppress waves during boss fights); boss freeze held until `BOSS_DELAY` end.
- **Ring light**: standby dark-red baked into texture (5 bases); charge/destroy = runtime Line2D overlay; no separate lid parts (raise = TRANS_BACK scale-in).
- **Dialogue boundary**: line 2 requires "before all destroyed" (mutex with line 3); cross-node last hit (splash multi-kill) → new line overrides old.

### Validation (2026-07-28)
- `test/elite_turret_event_test.tscn`: **45/45 PASS** — state transitions, medium 4×80 HP, unattackable during raise, independent cadence, weak homing (1.5/0.6s), 3-node dialogue (⌈N/3⌉/⌈2N/3⌉/all), reward 500×2=1000, timeout no reward, boss freeze/pending single-shot/retrigger, cooldown blocks.
- Full regression: `--headless --import`, `--quit-after 300`, smoke/base_system/enemy_combat/buff33/difficulty/boss_enrage/hit_logic/balance/pool_reuse/i18n/keybind/startup_flow/back_navigation/esc_navigation/view_zoom/window_size/intro_cinematic/tutorial all 0 failures.
- Autoplay 150s (seed=20260728): 0 errors, 0 orphans, event registry OK; ObjectDB leak warning = HEAD baseline (pre-existing).
- Windowed screenshots manually verified: carrier composition, ring status lights, turret HP bar, event bar, overlay.
