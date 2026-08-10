# Boss Behavior Redesign (BOSS_REDESIGN)

Single source of truth for Boss behavior redesign. **Any Boss change must align with this doc; implementation changes write back here.**
Pre-redesign behavior ported from the Python original (`docs/archive/PORTING_PARITY.md`, provenance only); evolution decisions in §7.3.

## 1. Audit (2026-07-28, per `scripts/boss.gd` + `data/balance.json` boss section)

> GDScript-era 锚点（`scripts/boss.gd` 符号/行号）为 2026-07-28 撰写时快照；M7（2026-08-08）全量迁移 C# 后 `scripts/` 已无 `.gd`，现行为见 `csharp/godot/Boss.cs` + `BossFire`/`BossAttacks`/`BossMovement`/`EnrageSequence`。§1.1 = pre-redesign snapshot, **superseded (3 phases landed 2026-07-28, §8)**, kept for diff.

### 1.1 Old Structure
- 3 types: 1 Heavy (strafe 150, 5-way fan/homing alt., 1.6s) / 2 Stalker (dash 400, 3-round sniper 0.12s gap, 1.8s) / 3 Mothership (strafe 60, rotating cross 0.9s + summon 2–3 minions/6s). HP = 800 × [1.3, 0.7, 1.6] × difficulty.
- Enrage (HP<30%): **shared sequence** — HP lock + player freeze, bullet-time 1.2s → square→circle orbit 6s (every 0.7s: 4 lasers + 8 rings) → 0.7s slow barrage → 0.8s return → fire ×1.5 / speed ×1.3.
- Escape: not killed by 50s; 3s warning; no reward.
- Difficulty: only HP × + global spawn params; bullet rhythm/counts/speed unchanged.

### 1.2 Problems
| # | Problem | Evidence |
|---|---|---|
| P1 | Enrage lacks type identity (shared sequence) | `_update_enrage_sequence` no boss_type branch |
| P2 | Metronome: no variation 100%→30%; no pattern loops | `_fire_timer` fixed `FIRE_INTERVALS`; `_fan_next` binary |
| P3 | Heavy attacks zero telegraph (650-speed 3-round 0.12s, 380 fan, homing instant) | `_fire_sniper/_fire_fan/_fire_homing` direct fire |
| P4 | Freezes player ~6s at max density; luck-based (target) | `_lock_player_movement` covers TRANSITION+ACTIVE |
| P5 | Difficulty only HP: hard = sponge; bullets identical | `setup()` HP only; `FIRE_INTERVALS` no difficulty axis |
| P6 | 1-D movement (fixed y=230); tracking x optimal | `_move_strafe/_move_dash` write position.x only |
| P7 | Escape timer invisible (only 3s warning) | `_show_escape_warning` at 47s |
| P8 | HP bar no phase info | `hud.show_boss_bar` continuous only |
| P9 | No climax burst after enrage | RELEASE_HOLD only 0.7s slow barrage |

## 2. Reference Patterns

1. **Variety/phases**: HP segments, own looping pattern group each; bar shows segments; modes force different strategy.
2. **Telegraph/fairness**: high-threat attacks need readable warning (charge/aim line/sound); intensity ∝ threat.
3. **Pressure curve**: end > start intensity; mid alternates tension/rest.
4. **Length discipline**: duration ∝ flow; skilled ×2 faster, novices not ×4 slower (50s escape kept).
5. **Character/identity**: behavior serves type character; enrage = strongest expression; types must differ.

## 3. Goals
- **G1** Type identity (Bulwark/Stalker/Hive); enrage unique per type.
- **G2** Phasing P1 (100–70%) / P2 (70–30%) / ENRAGE (<30%); per-phase pattern loop; switch performance; HP ticks (P8).
- **G3** Speed ≥500 or damage ≥20 ⇒ ≥0.35s telegraph.
- **G4** Enrage freeze → slow ×0.35 (still move/shoot/dash); immobilize removed (§7.3).
- **G5** Difficulty affects patterns (counts/intervals/speeds), not just HP.
- **G6** Escape countdown in bar last 10s (P7).

Kept: 3-type rotation + HP baseline, 50s escape, bullet-time enrage framework (main orchestration), bullet damage baselines, kill/reward chain, pool/registry constraints.

## 4. Common Mechanics

### 4.1 Phase Framework
FIGHT: P1 100–70% (2 patterns/type) → P2 70–30% (2–3 patterns, 1 telegraphed heavy); switch: 0.6s charge screen-shake + pitch + clear timers; ENRAGE <30%: per-type sequence (§5), HP lock kept (clamped 30% trigger→finish).
- Switch via `TakeDamage` thresholds (`EnrageHpRatio` pattern; P2 threshold `Phase2HpRatio` 0.7).
- Pattern = duration (4–8s) or fixed wave count; fire rhythm programmable.
- Movement decoupled from attacks; one movement fn per type/phase (vertical incl., P6).

### 4.2 Telegraph Spec (reuse existing parts)
| Form | Use | Implementation |
|---|---|---|
| Charge glow | heavy windup (0.4–0.6s) | `ChargeGlow` additive scale/alpha tween (GlowDot) |
| Aim line | sniper/dash path (0.35–0.5s) | Line2D α0.3 flicker; gone at fire instant |
| Redden + pitch | switch/enrage start | `BaseModulate` variant + `GameState.PlaySfx` pitch |
| HP ticks | switch preview | 2 ticks (70%/30%) + flash |

### 4.3 Enrage Slow (replaces freeze)
- TRANSITION+ACTIVE: `Player.MovementLocked` unused; `player.ApplyEnrageSlow(0.35)` (× boost/fine-move; dash usable).
- Unlock at RELEASE_HOLD start; `_ExitTree` fallback reset (`EnrageSequence.UnlockPlayer()`).
- ACTIVE orbit speed/density recalibrated for 0.35-speed dodge (§5).

### 4.4 Difficulty Tiers
Pattern params per tier (easy/medium/hard): counts ±1/±2, interval ×1.15/×1/×0.85, speed ×0.9/×1/×1.1 (details §8.3). HP kill-ramp unchanged; since 2026-07-29 Boss HP ×0.75/×1/×1.5 (`GameState.EnemyHpMultiplier()`, same as enemies).

### 4.5 Escape Countdown
Bar shown + ≥40s: countdown under bar (10→0, red flicker); escape at 50s unchanged.

## 5. Per-Type Design

### 5.1 Type 1 "Bulwark" (Heavy) — mobile turret
| Phase | Movement | Attacks (loop) |
|---|---|---|
| P1 | strafe 150 + press 80px down/6s and back | ① 5-way fan ×3 (existing) ② homing ×2 (existing) |
| P2 | strafe 200 + vertical bob | ③ **charged cannon**: 0.6s glow → 3 shots (700, dmg 21, 0.25s gap) ④ 7-way fan |
| ENRAGE | **Rotating fortress**: bullet-time kept; ACTIVE hovers, clockwise rot., 12-way ring/0.5s (start angle precesses); finish: 8-way cannon salvo (telegraphed) |

### 5.2 Type 2 "Stalker" — assassin, 1v1
| Phase | Movement | Attacks (loop) |
|---|---|---|
| P1 | dash 400, 0.5s/0.7s | ① **sniper + aim line**: 0.35s lock (tracks 0.2s then fixed) → 3 shots (existing) |
| P2 | dash 0.4s/0.5s | ② **dash sweep**: 0.5s aim line → cross at player height (body-hit stays; drops 3 slow bullets) ③ sniper |
| ENRAGE | **Hunt circle**: bullet-time kept; ACTIVE teleport-stops at 4 quadrant points, each 0.35s aim line + 1 snipe (900, dmg 21), 6 points; finish: orbit bottom, 12-way slow ring |

### 5.3 Type 3 "Hive" (Mothership) — commander
| Phase | Movement | Attacks (loop) |
|---|---|---|
| P1 | strafe 60 + press/rise (y 200–280 sine) | ① rotating cross (existing) ② summon 2–3 minions/6s (existing) |
| P2 | strafe 100 + vertical bob | ③ **minion volley**: 4 minions; 0.8s later homing volley (420 speed) ④ **bullet wall**: 10-way fan, 2 gaps, 220, dmg 12 |
| ENRAGE | **Swarm out**: bullet-time kept; ACTIVE: 3×3 minions/1.2s + 8-way ring/0.9s; finish: 16-way slow ring + all minions volley |

### 5.4 Climax Burst (principle 3)
ENRAGE RELEASE_HOLD = "last stand" peak (above); then RETURN → "afterburn": fire ×1.3 (down from ×1.5).

### 5.6 Type 4 "Eclipse" (2026-08-04) — ring-weaving mage

4th boss (content evolution 3.1): **stationary center-weaver** — no strafe, small vertical sine bob at anchor (amp 30px / period 2.4s / `boss.movement.type4`). Distinguishes via bullet geometry, not movement.

- **P1**: `ring_burst` (360° ring, 12 shots @340, `boss.ring_burst`) alternating `homing`
- **P2**: `ring_burst` + `cross` + `sniper3` (0.35s telegraph — same shared skeleton)
- **Enrage "Lunar Eclipse"** (`boss.enrage.type_4`): TRANSITION hover (same as type1) → ACTIVE counter-rotating double ring (forward ring at +angle, reverse at -angle, precess 15°/wave, 10 shots each @200) → RELEASE 20-shot ring volley (direct fire, no charge telegraph — W 系列 2026-08-09 口径修正) → RETURN; shared afterburn ×1.3
- Rotation `Spawner.cs` `BossKills % 4 + 1`; `hp_mults` 1.2; tell: `AttackTells["ring_burst"]` (fire-A pitch 1.4, magenta ring); sprite `boss_ship_4.png` (dedicated "Wheel of Lunar Eclipse" — dark moon disc + twin orthogonal orbit rings, 2026-08-09; previously reused boss_ship_1)
- Difficulty tier: `counts.ring_burst` [10, 12, 14]

### 5.5 P2 Movement Upgrade (2026-08-02, D05 landed)
> §5.1-5.3 P2 movements were unimplemented since phase B (D05, 2026-08-02). Landed per table; vertical = **sine bob**, reusing `EnemyMoveStrategy` (`anchor + Enemy.sin_fast(time * freq + phase) * amp`, LUT zero alloc, C05/C09) and `BossMovement._update_press` incremental-y pattern — no new primitives.

Semantics: type3 P1 "y 200–280 sine" = periodic press/rise into anchor+200~280px (center 240, swing ±40, period 9s), from anchor (target from 0, no jump); type1 P2 = ±40px, period 6s (≈ `press_interval`), phase 0 on switch (`reset_press()`); type3 P2 = ±50px, period 8s.

#### Params (new keys, `boss.movement`)
| Key | Default | Meaning |
|---|---|---|
| `type1_p2_strafe` | 200 | type1 P2 strafe (P1 = `strafe_speeds[0]` 150) |
| `type1_p2_bob_amp` | 40 | type1 P2 bob amp (±px) |
| `type1_p2_bob_period` | 6 | type1 P2 bob period (s) |
| `type2_p2_dash_time` | 0.4 | type2 P2 dash (P1 = 0.5) |
| `type2_p2_rest_time` | 0.5 | type2 P2 rest (P1 = 0.7) |
| `type3_p1_bob_min` | 200 | type3 P1 press-depth lower bound |
| `type3_p1_bob_max` | 280 | type3 P1 press-depth upper bound |
| `type3_p1_bob_period` | 9 | type3 P1 press/rise period (s) |
| `type3_p2_strafe` | 100 | type3 P2 strafe (P1 = `strafe_speeds[2]` 60) |
| `type3_p2_bob_amp` | 50 | type3 P2 bob amp |
| `type3_p2_bob_period` | 8 | type3 P2 bob period (s) |

All **gameplay-range** (no `world_scale`); coords from `fight_anchor_y()` / `strafe_range()`.

#### Implementation
- `BossMovement._move_bob(delta, boss, amp, period)`: `position.y = fight_anchor_y() + sin(phase)*amp`; after `_in_fight` only (entry/escape/enrage early-return); `fight_anchor_y()` per-frame (mid-fight zoom). `_move_band(delta, boss, y_lo, y_hi, period)`: type3 P1 press/rise, `_update_press`-isomorphic, target from 0. Phase via `_bob_phase` (`TAU * delta / period`); `reset_press()` zeroes on switch.
- Type1: P2 = `_move_strafe(type1_p2_speed)` + `_move_bob`; P1 keeps `_update_press`. Type2: dash per `fight_phase` 0.5/0.7 or 0.4/0.5. Type3: P1 adds `_move_band(200, 280, 9)`; P2 = `_move_strafe(100)` + `_move_bob(50, 8)`.
- Speeds via `slow_factor()` / `_enrage_speed_mult()`. Config cached in `Boss._ready`, injected into `_movement` (A5 DI); script fallbacks mirror (AGENTS.md).

#### Test Impact
- `boss_phase_test` scene 1 (type1) C11: sin 0 = 0 lands on anchor, y drifts next frames — assert "y within anchor ±amp after switch" (multi-frame wait fails).
- New: type1 P2 y fluctuation; type2 P2 dash rhythm (0.4/0.5); type3 P1 y ∈ [anchor+200, anchor+280]. Use config reads/instance constants (C34).

## 6. Values & Config (boss section refactor)
- Keep existing keys; add `boss.phases` (per-type pattern tables: duration/waves/counts/speed/interval × 3 difficulties, telegraph durations, vertical params). Script defaults mirror JSON.
- Add `boss.enrage.player_slow` (0.35), `boss.enrage.type_*`; existing `boss.enrage.*` common timing kept.
- Add `boss.escape.countdown_visible_from` (10.0).

## 7. Phases, Tests & Compatibility

### 7.1 Phases
- **A (framework)**: phase-table state machine (P1/P2/ENRAGE + pattern loop + telegraph + HP ticks + countdown + slow-replaces-freeze); tables filled with existing attacks (sniper + aim line); enrage stays shared. Exit: regression + new asserts green.
- **B (library)**: §5 P2 attacks + differentiated enrage (charged cannon / dash sweep / minion volley / bullet wall).
- **C (values)**: difficulty tiers, TTK calibration (autoplay 480s probe + manual), perf re-bench.

### 7.2 Tests
- New `test/boss_phase_test.tscn`: phase thresholds, pattern-loop progress, telegraph timing (line before bullets), slow apply/reset (incl. death/escape fallback), countdown, tier values.
- Rework `test/boss_enrage_test.tscn`: freeze → slow asserts; B adds per-type asserts.
- Regression per §8.4.

### 7.3 Evolution Decisions (formerly PORTING_PARITY, archived 2026-07-30)
1. Freeze → slow ×0.35 (P4; freeze dropped).
2. Shared enrage → per-type (P1). 3. Metronome → phase tables (P2). 4. Instant → telegraph (P3). 5. HP-only → pattern tiers (P5).
6. `FIGHT_Y` absolute (y=230) → visible-area-top offset (2026-07-30 UX audit P0-1): `fight_anchor_y()` = `GameState.view_world_rect().position.y + FIGHT_Y`; 3 use sites (entry stop-line, P2 dash RETURN, enrage RETURN) unified; `_strafe_range()` margins aligned; zoom=1 bit-identical.

### 7.4 Compatibility
- Saves hold no Boss state (save_run: score/fuel/time); re-enters by schedule; no migration.
- Event mutex hooks (`_bossFrozen`/`_bossPending`) untouched.
- Pool/registry/perf budget per AGENTS.md; telegraph nodes freed with bullets, no resident `_Process`.

---

## 8. Implementation Log (2026-07-28; all landed)

### 8.1 Completion
A (FightPhase framework + `_patterns` + telegraphs + slow) / B (P2 attacks + per-type enrage) / C (difficulty tiers, §8.3) all landed 2026-07-28; per-part details & validation (boss_phase/enrage/pattern_test + regression §8.4) in git log.

### 8.2 Self-Decided Points
- A: switch cease-fire = new pattern's first-wave interval; afterburn reuses P2 (×1.3); cross-phase hit → enrage wins; type3 summon timer independent; countdown plain text (no key).
- B: ring dmg baseline 12; type2 aim line tracks throughout; type1 TRANSITION jitters; type3 RELEASE one-shot; dash_sweep pauses pattern timer; wall arc fixed downward, gaps avoid player ±30°.
- C: tiers applied once in `_ready`; snapshot bullets (4 lasers + 8 rings), telegraph durations, body speed, HP/damage untiered; floors: wall 6 / ring 4 / others 1 (fan 3).
- Fix (`duplicate(true)` cfg arrays, 2026-08-01, AUDIT_VAULT B5, boss_pattern_test easy/hard pass) + movement-simplification gap (2026-08-02, D05, landed per §5.5): details in git log.

### 8.3 Difficulty Tiers Landed (§4.4)
`boss.difficulty_scaling`: `interval_mult` [1.15, 1.0, 0.85] × pattern intervals / FIRE_INTERVALS / CANNON / ENRAGE / E1-E3; `speed_mult` [0.9, 1.0, 1.1] × attack speeds (not snapshot, not SWEEP_SPEED); `counts` ±1 (fan/homing/cannon/volley/summon/drops) / ±2 (wall/ring/salvo) with floors (wall 6 / ring 4 / others 1, fan 3); applied once end of `_ready` (`_apply_difficulty_scaling`, `duplicate(true)`); tier = `DIFFICULTY_ORDER.find(difficulty)`, unknown → medium. Details & verification: git log.

### 8.4 Calibration Validation
- `boss_phase_test` / `boss_enrage_test` / `boss_pattern_test` (incl. easy/hard) green; regression (hit_logic / difficulty / smoke / enemy_combat / elite_turret_event / formation_strike_event / base_system / `--quit-after 300` / `--import`) green.
- autoplay 480s probe & perf_bench in commit notes; TTK/feel = manual; else: probe no [ANOMALY] + frame-time magnitude.
