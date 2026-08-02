# Boss Behavior Redesign (BOSS_REDESIGN)

This document is the single source of truth for the Boss behavior redesign: current-state audit, reference patterns, redesign plan, implementation phases, and test plan.
**Any Boss behavior change must first align with this document; implementation-stage changes are written back here.**
The pre-redesign behavior was ported from the Python original (alignment record archived in `docs/archive/PORTING_PARITY.md`, kept for provenance only);
this redesign evolves independently — see §7.3 for evolution decisions.

---

## 1. Current-State Audit (2026-07-28, based on `scripts/boss.gd` and the boss section of `data/balance.json`)

### 1.1 Current Structure

> This section is a snapshot of the pre-redesign behavior, **already superseded by this redesign (all three phases shipped 2026-07-28, see §8 implementation record)** — kept only as a deviation reference.

- 3-type rotation: 1 Heavy (strafe 150, alternating 5-way fan / homing, 1.6s) / 2 Stalker (dash 400, 3-round sniper at 0.12s interval, 1.8s) /
  3 Mothership (strafe 60, rotating cross 0.9s + summons 2–3 minions every 6s). HP = 800 × [1.3, 0.7, 1.6] × difficulty.
- Enrage (HP<30%): **all three types share one sequence** — HP lock + player movement freeze, bullet time 1.2s → orbit the player snapshot point, square→circle 6s
  (every 0.7s a wave of 4 lasers + 8 ring bullets) → 0.7s dense slow barrage → 0.8s return to position → regular phase fire rate ×1.5 / move speed ×1.3.
- Escape: triggered when not killed within 50s of entry, warning floats up in the last 3s, no reward.
- Three difficulty tiers: only multiply HP and global spawn parameters; **Boss barrage rhythm / bullet count / bullet speed do not scale with difficulty**.

### 1.2 Problem List

| # | Problem | Evidence |
| --- | --- | --- |
| P1 | **Enrage has no type identity**: all three types share one sequence; the climactic phase plays identically and repeats every fight | boss.gd `_update_enrage_sequence` has no boss_type branch |
| P2 | **Regular phase is a metronome**: HP 100%→30% playstyle is zero-change; each type has only 1–2 attacks rotating on fixed intervals, no pattern cycles, no pressure curve | `_fire_timer` fixed `FIRE_INTERVALS`, `_fan_next` binary alternation |
| P3 | **Heavy attacks have zero telegraph**: 650-speed 3-round aimed sniper (0.12s interval), 380 fan, homing — all fire instantly, no windup / aim line / charge telegraph, unfair | `_fire_sniper/_fire_fan/_fire_homing` fire directly |
| P4 | **Enrage freezes player movement ~6s**: strips evasive options at the densest barrage moment, passed only on HP lock and luck, frustrating (old behavior, target of this redesign) | `_lock_player_movement` covers TRANSITION+ACTIVE |
| P5 | **Difficulty only adds HP**: hard = longer sponge fight rather than richer pressure; easy/hard barrages identical | `setup()` only multiplies HP by difficulty; `FIRE_INTERVALS` etc. have no difficulty dimension |
| P6 | **One-dimensional movement**: fixed y=230 horizontal shuttling; optimal play is to sit under the Boss and track x; no positional play, no vertical oscillation | `_move_strafe/_move_dash` only write position.x |
| P7 | **Escape timer invisible**: warning only in the last 3s; low-DPS builds punished without knowing why | `_show_escape_warning` only appears at 47s |
| P8 | **Health bar has no phase info**: players can't perceive mode-switch points (reference §2 variety principle) | `hud.show_boss_bar` only draws a continuous bar |
| P9 | **No final burst at fight end**: after enrage the fight returns to the sped-up metronome until kill/escape; no "last stand" pressure peak before the kill | RELEASE_HOLD only 0.7s slow barrage |

## 2. Reference Patterns (research on comparable projects)

Primary source: Michael Molinari, "Video Game Boss Design For Shmups" (GameDeveloper, 2010),
supplemented by accepted practice from Touhou (spell-card phase structure), Cave shooters (Mushihimesama/ESPGaluda2 closing burst), and Ikaruga (final-stage acceleration).

Five applicable principles distilled:

1. **Variety / Phasing**: Boss fights split by HP segments, each segment an independent attack-mode group looping within itself; the health bar is visually segmented so players can anticipate mode-switch points. Different modes must force players to change evasion/output strategy (position, rhythm), not just re-skin.
2. **Telegraph / Fairness**: every high-threat attack must have a readable warning (charge, aim line, sound), intensity proportional to threat. "Looks deadly" must mean "gives a chance to dodge".
3. **Pressure curve**: fight-ending intensity must exceed the opening (Cave-style death burst); mid sections alternate tension and release rather than a constant metronome.
4. **Length discipline**: fight length proportional to the preceding flow; skilled players can be 2× faster, newcomers should not be 4× slower (the project already has the 50s escape fallback; kept).
5. **Character / type identity**: each Boss's attacks, movement, hit feedback, and death scene must serve its "character"; the climactic phase (enrage) is the strongest expression of identity — the three types must differ.

## 3. Redesign Goals

- **G1 Type identity throughout**: each type has its "character" (Bulwark/Stalker/Hive); enrage is a type-unique finale, no shared sequence.
- **G2 Full phasing**: HP 100%→0 split into P1 (100–70%) / P2 (70–30%) / ENRAGE (<30%), each segment an independent pattern-table loop with an explicit transition animation between segments; health bar gains phase ticks (fixes P8).
- **G3 Every heavy attack telegraphed**: all attacks with bullet speed ≥500 or damage ≥20 get a ≥0.35s telegraph (charge glow / aim line / tone).
- **G4 Keep evasion agency**: enrage "movement freeze" becomes "forced slow ×0.35" (the player can still move / shoot / dash); root-in-place semantics removed from the game (evolution decision, §7.3).
- **G5 Difficulty affects modes, not only HP**: pattern parameter tables keyed by difficulty (bullet count / interval / speed tiers); HP multiplier kept.
- **G6 Escape timer visible**: countdown shown in the last 10s of the health bar (fixes P7).

Skeleton kept unchanged: 3-type rotation and HP baseline, 50s escape mechanic, bullet-time enrage framework (main orchestration),
existing bullet-damage baselines, Boss kill/reward settlement chain, object pool and registry technical constraints.

## 4. Common Mechanics

### 4.1 Phase Framework (replacing the fixed metronome)

```
FIGHT (regular)
├─ P1: HP 100–70%, pattern-table loop (2 modes per type)
├─ P2: HP 70–30%, pattern-table loop (2–3 modes per type, incl. 1 telegraphed heavy attack)
│     phase-transition animation: 0.6s charge screen-shake + tone + clear own timers
└─ ENRAGE: HP<30%, type-unique enrage sequence (§5), HP-lock semantics kept (trigger → HP clamped at 30% until finale)
```

- Phase switches driven by `take_damage` thresholds (reusing the existing ENRAGE_HP_RATIO pattern; new P2 threshold 0.7).
- Each "mode" = a duration (4–8s) or a fixed wave count, then switch to the next; in-mode firing rhythm is programmable (no longer a single interval).
- Movement modes decoupled from attack modes: one movement function per type per phase (with a vertical component, fixes P6), attacks layered on top.

### 4.2 Telegraph Spec (reusing existing building blocks)

| Telegraph form | Use | Implementation |
| --- | --- | --- |
| Charge glow | heavy cannon / volley windup (0.4–0.6s) | `_glow` layered dot scale/alpha tween (cinematic recipe) |
| Aim line | sniper / dash path (0.35–0.5s) | Line2D thin line α0.3 flicker, vanishes the instant bullets fire / movement starts |
| Hull reddening + tone | phase switch / enrage opener | existing `_base_modulate` variant + `play_sfx` pitch shift |
| Health-bar phase ticks | mode-switch preview | HUD boss bar draws 2 tick lines (70%/30%) + brief flash on switch |

### 4.3 Enrage Player Slow (replacing root)

- During TRANSITION+ACTIVE, `Player.movement_locked` is no longer used; replaced by `player._enrage_slow = 0.35`
  (movement-speed multiplier, stacking with fuel boost / fine move; dash remains usable as the active escape tool).
- Unlock timing unchanged (RELEASE_HOLD start); `_exit_tree` fallback reset (reusing the `_unlock_player_movement` fallback pattern).
- Companion change: ACTIVE orbit bullet speed/density recalibrated for "player evades at 0.35 speed" (§5 gives per-type baselines).

### 4.4 Difficulty Tiers

Pattern parameter tables gain a difficulty dimension (easy/medium/hard columns) affecting: bullet count ±1/±2, fire interval ×1.15/×1/×0.85,
bullet speed ×0.9/×1/×1.1. The Boss kill ramp multiplier on HP is unchanged; since the 2026-07-29 balance revision, Boss HP is additionally
×0.75/×1/×1.5 by difficulty tier (`GameState.enemy_hp_multiplier()`, same source as regular enemies, keeping the "wave HP tier derives Boss HP" convention).

### 4.5 Escape Timer Visible

While the health bar is present, once survival ≥40s a countdown text appears below the bar (10→0, red flashing); at 50s the escape logic is unchanged.

## 5. Per-Type Design

### 5.1 Type 1 "Bulwark" (Heavy) — character: mobile gun platform, frontal suppression

| Phase | Movement | Attack modes (loop) |
| --- | --- | --- |
| P1 | slow strafe (150) + 80px vertical press-down every 6s then return (first positional game) | ① 5-way fan ×3 waves (existing fan) ② homing ×2 (existing homing) |
| P2 | strafe faster (200) + vertical oscillation | ③ **charged cannon**: 0.6s charge glow → 3 high-speed heavy shots (700 speed, 21 damage, 0.25s interval) ④ denser 5-way fan (7-way) |
| ENRAGE | **rotating bulwark**: bullet-time framework kept; ACTIVE becomes hovering in place, rotating clockwise, a 12-way rotating ring wave every 0.5s (start angle precesses per wave); finale an 8-way charged-cannon volley (telegraphed) |

### 5.2 Type 2 "Stalker" (Striker) — character: assassin, one-on-one pressure

| Phase | Movement | Attack modes (loop) |
| --- | --- | --- |
| P1 | existing dash (400, 0.5s/0.7s rhythm) | ① **3-round sniper with aim line**: 0.35s aim-line lock (line micro-tracks the player for 0.2s then fixes) → 3 rounds (existing sniper) |
| P2 | dash more frequent (0.4s/0.5s) | ② **dash sweep**: 0.5s aim line → high-speed horizontal pass at the player's altitude (body-contact hit kept; drops 3 slow bullets along the path) ③ 3-round sniper |
| ENRAGE | **hunt orbit**: bullet-time framework kept; ACTIVE becomes sequential freeze-stops at 4 quadrant points of the player snapshot orbit, 0.3s aim line + single lethal sniper at each (900 speed, 21 damage), 6 points total; finale returns to the orbit bottom and releases a 12-way slow ring |

### 5.3 Type 3 "Hive" (Mothership) — character: commander, numbers over finesse

| Phase | Movement | Attack modes (loop) |
| --- | --- | --- |
| P1 | very slow strafe (60) + slow press/recover (y 200–280 sine) | ① rotating cross (existing) ② summon 2–3 minions every 6s (existing) |
| P2 | strafe 100 + vertical oscillation | ③ **formation volley**: summons 4 minions in a row, one aimed volley 0.8s later (minion normal bullets, 420 speed) ④ **bullet wall**: 10-way slow fan wall (2 gaps, 220 speed, 12 damage) |
| ENRAGE | **swarm out**: bullet-time framework kept; ACTIVE releases a wave of 3 minions every 1.2s (3 waves total) + own 8-way ring every 0.9s; finale one 16-way slow ring + all living minions volley simultaneously |

### 5.4 Common Final Burst (principle 3)

Each type's ENRAGE RELEASE_HOLD stage is that type's "last stand" peak (already in the tables above), followed by RETURN;
the regular loop then enters "lingering rage": fire rate ×1.3 (down from ×1.5 because enrage itself is now stronger).

### 5.5 P2 Movement Upgrade Implementation Design (2026-08-02, D05 shipped)

> Background: the P2 upgrades in the §5.1-5.3 per-type movement tables were unimplemented since Phase B (registered as D05 on the 2026-08-02 review).
> This round implements them per the tables; the vertical component is uniformly **sine oscillation**, reusing the project's existing `EnemyMoveStrategy` sine form
> (`anchor + Enemy.sin_fast(time * freq + phase) * amp`) and `BossMovement._update_press`'s
> incremental y-application pattern — no new movement primitives.

#### Research & References (2026-08-02)

- **BulletML movement primitives** ([official reference](http://www.asahi-net.or.jp/~cs8k-cyu/bulletml/bulletml_ref_e.html)):
  movement = composition of incremental `changeDirection / changeSpeed / accel` (linear transition to the target within term frames) —
  sine/oscillation derives from incremental adjustments; no standalone "waveform" concept. This project's `_update_press` "apply the
  `target - _press_offset` delta per frame" is the same paradigm (already present; reused).
- **In-project `EnemyMoveStrategy`** (`scripts/enemy_move_strategy.gd`): `SineMove`/`HoverMove` express sine via
  `Enemy.sin_fast(time * freq + phase) * amp` (lookup table, zero allocation; C05/C09 closure convention).
  Boss vertical oscillation reuses the same family rather than reinventing the wheel.
- **Danmaku design principles** ([The Anatomy of a Shmup](https://www.gamedeveloper.com/design/the-anatomy-of-a-shmup),
  [Danmaku Design Discussion](https://www.shrinemaiden.org/forum/index.php?topic=6649.0)):
  ① movement decoupled from the barrage (Boss movement keeps its own rhythm, not tied to bullet intervals); ② phase-upgrade pressure comes from
  "more movement freedom" (P1 one-axis → P2 two-axis); ③ vertical oscillation amplitude/period stay small and slow so as not to squeeze player space
  (`strafe_range` horizontal band unchanged; vertical only swings near the anchor line).

#### Semantic Interpretation

- **§5.3 Type 3 P1 "y 200–280 sine"**: interpreted as **periodic press-down/recover** (isomorphic with §5.1 Type 1 P1's "press down 80px every 6s then return",
  but larger amplitude, longer period, slower) — the hull sines down from the anchor line into the **band 200–280px below the anchor** (press depth center 240,
  trajectory swing ±40, 9s slow-breath period), then slowly recovers. Rationale: the anchor line itself sits at `view.position.y + FIGHT_Y(230)`;
  if 200–280 were absolute coordinates they would nearly coincide with the anchor line (no press effect); "press/recover" is periodic-motion semantics, starting
  from the anchor (target eases from 0) to avoid phase-switch/entry jumps, isomorphic with `_update_press`'s incremental style.
- **Type 1 P2 "vertical oscillation"**: two-direction sine around the anchor line ±40px (distinct from P1's one-direction periodic press), period 6s
  (same feel as P1's `press_interval`); phase starts at 0 on phase switch (sin 0 = 0, seamless with `reset_press()`).
- **Type 3 P2 "vertical oscillation"**: two-direction sine around the anchor line ±50px, period 8s (slower pitching for the mothership).

#### Parameter Table (new config keys, `boss.movement` section of balance.json)

| Key | Default | Semantics |
| --- | --- | --- |
| `type1_p2_strafe` | 200 | Type 1 P2 horizontal strafe speed (P1 = `strafe_speeds[0]` 150) |
| `type1_p2_bob_amp` | 40 | Type 1 P2 vertical sine amplitude (±px, around the anchor line) |
| `type1_p2_bob_period` | 6 | Type 1 P2 vertical sine period (s) |
| `type2_p2_dash_time` | 0.4 | Type 2 P2 dash duration (P1 = 0.5) |
| `type2_p2_rest_time` | 0.5 | Type 2 P2 dash rest (P1 = 0.7) |
| `type3_p1_bob_min` | 200 | Type 3 P1 press-depth band lower bound (px below anchor) |
| `type3_p1_bob_max` | 280 | Type 3 P1 press-depth band upper bound (px below anchor) |
| `type3_p1_bob_period` | 9 | Type 3 P1 press/recover period (s; offset from the pattern loop to avoid a rigid rhythm) |
| `type3_p2_strafe` | 100 | Type 3 P2 horizontal strafe speed (P1 = `strafe_speeds[2]` 60) |
| `type3_p2_bob_amp` | 50 | Type 3 P2 vertical sine amplitude (±px, around the anchor line) |
| `type3_p2_bob_period` | 8 | Type 3 P2 vertical sine period (s) |

All are **gameplay-range family** (not multiplied by `world_scale`); movement coordinates are based on the `fight_anchor_y()` / `strafe_range()` view baseline.

#### Implementation Notes

- `BossMovement` gains `_move_bob(delta, boss, amp, period)` (P2 vertical sine oscillation: directly sets
  `position.y = fight_anchor_y() + sin(phase)*amp` — called only after `_in_fight`; entry/escape/enrage sequences all early-return without interfering;
  `fight_anchor_y()` evaluated per frame supports mid-fight view-tier switches) and
  `_move_band(delta, boss, y_lo, y_hi, period)` (Type 3 P1 slow press/recover: isomorphic with `_update_press`;
  the target is a pure offset starting from 0, no initial jump); phase accumulated via `_bob_phase` (`TAU * delta / period`),
  and `reset_press()` zeroes the phase on phase switch.
- Type 1 `update()`: P2 branch `_move_strafe(type1_p2_speed)` + `_move_bob(amp, period)`;
  P1 branch keeps `_update_press` (one-direction periodic press).
- Type 2 `update()`: dash rhythm takes 0.5/0.7 (P1) or 0.4/0.5 (P2) by `fight_phase`.
- Type 3 `update()`: P1 adds `_move_band(200, 280, 9)` (strafe 60 unchanged); P2 uses `_move_strafe(100)` + `_move_bob(50, 8)`.
- All speeds go through the `slow_factor()` / `_enrage_speed_mult()` multipliers (consistent with existing movement).
- Config read cached once as instance fields in `Boss._ready` and injected into `_movement` (A5 dependency-injection pattern);
  new keys also sync script fallback defaults (AGENTS.md consistency convention).

#### Test Impact

- `boss_phase_test` scene 1 (Type 1) C11 "returns to the anchor line after P2 switch" assertion: with sine in P2, at the switch instant
  sin 0 = 0 still returns to the anchor line, but **y starts deviating over the following frames** — the assertion would fail if it checked after waiting several frames;
  it must change to "y within anchor ±amp after the phase switch".
- New assertions: Type 1 P2 vertical y fluctuation (both y > anchor and y < anchor within sampled frames); Type 2 P2 dash rhythm
  (0.4/0.5 period verification); Type 3 P1 y ∈ [anchor+200, anchor+280] (sampled band breathing).
- Movement-value assertions prefer instance constants / config reads (C34 convention), no hardcoding.

## 6. Values & Config (balance.json boss section restructure)

- Existing keys untouched (compat/fallback); new `boss.phases` section: per-type per-phase pattern table (duration/waves/count/speed/interval × three difficulty columns),
  telegraph durations, vertical movement parameters. Script defaults match JSON (AGENTS.md convention).
- New `boss.enrage.player_slow` (0.35) and `boss.enrage.type_*` per-type enrage parameter subsections; the original `boss.enrage.*`
  common timing (bullet time / orbit radius etc.) is kept.
- New `boss.escape.countdown_visible_from` (10.0).

## 7. Implementation Phases, Testing & Compatibility

### 7.1 Phases

- **Phase A (framework first)**: Boss state machine refactored to phase-table driven (P1/P2/ENRAGE switching + pattern loops + telegraph mechanism +
  health-bar ticks + escape countdown + slow replaces root); all three types fill the tables with **existing attacks** (sniper gains an aim line); enrage keeps the shared sequence for now.
  Exit criteria: full regression green + new phase assertion tests green.
- **Phase B (per-type mode library)**: implement the §5 per-type P2 new attacks and differentiated enrages (charged cannon / dash sweep / formation volley / bullet wall / type-3 exclusive enrage).
- **Phase C (values & verification)**: difficulty-tier parameters, TTK/pressure calibration (autoplay 480s probe + manual play), performance re-measurement.

### 7.2 Test Plan

- New `test/boss_phase_test.tscn`: phase-threshold switching, pattern-loop advancement, telegraph timing (line before bullets),
  slow application and reset (incl. Boss death/escape/depart fallbacks), escape countdown display, difficulty-tier values.
- Refactor `test/boss_enrage_test.tscn`: root assertions → slow assertions; Phase B adds per-type differentiation assertions.
- Regression: `hit_logic_test`, `difficulty_test`, `smoke_test`, `autoplay_test` ([ANOMALY] probe), `--quit-after 300`.

### 7.3 Evolution Decision Record (previously registered in PORTING_PARITY, archived 2026-07-30)

1. Enrage player root → forced slow ×0.35 (P4; root semantics no longer kept).
2. Shared 3-type enrage sequence → per-type enrage (P1).
3. Fixed metronome → HP-phase pattern tables (P2).
4. Instant sniper/fan → telegraph windup (P3).
5. Difficulty multiplies only HP → tiered pattern parameters (P5).
6. `FIGHT_Y` absolute anchor (y=230 hardcoded) → offset from the visible-area top edge (2026-07-30 combat UX audit P0-1): new
   `_fight_anchor_y()` = `GameState.view_world_rect().position.y + FIGHT_Y`; all three use sites
   (entry stop line per-frame evaluation, P2 dash RETURN, enrage RETURN) unified onto it, supporting mid-fight view-tier switches;
   aligned with `_strafe_range()`'s view-margin handling, behavior bit-identical at zoom=1.

### 7.4 Compatibility Constraints

- Save data contains no Boss state (save_run stores only score/fuel/time); on continue the Boss re-enters per scheduling — no migration issues.
- Elite-turret / formation-strike event Boss-mutex hooks (`_boss_frozen/_boss_pending`) untouched.
- Bullet pool / registry / performance budget (draw calls, zero heap allocation on hot paths) follow AGENTS.md constraints;
  telegraph nodes are freed with their bullets, not resident in `_process`.

---

## 8. Implementation Record (2026-07-28, all three phases shipped)

### 8.1 Phase Completion

- **Phase A (framework)**: `scripts/boss.gd` refactored to the FightPhase (P1/P2/ENRAGE) phase framework + pattern table `_patterns`
  (`boss.phases.typeN` config + DEFAULT_PATTERNS fallback); telegraph primitives (`_charge_glow` charge glow /
  `_make_aim_line` aim line); enrage root replaced with the `_enrage_slow = 0.35` slow; HUD health bar 70%/30% phase ticks;
  escape countdown below the bar (10→0 once alive ≥40s).
- **Phase B (per-type mode library)**: the three types' P2 new attacks (charged_cannon / dash_sweep /
  minion_volley / bullet_wall) + per-type differentiated enrage (`_update_enrage_sequence`
  dispatched by boss_type, params `boss.enrage.type_*`); `spawner.spawn_minion(pos) -> Enemy` returns the instance.
- **Phase C (values & verification)**: difficulty tiers `_apply_difficulty_scaling()` (see §8.3);
  value verification §8.4; doc write-back (this section, AGENTS.md).

### 8.2 Self-Decided Points During Implementation (where the design doc was silent)

- Phase A: cease-fire duration during the phase-switch animation = the new mode's first wave interval; post-enrage "lingering rage" reuses the P2 pattern table (fire rate ×1.3);
  a cross-phase one-shot triggering enrage → enrage takes priority; the type-3 summon timer is independent of the pattern table; the escape countdown is plain numeric text (no translation key).
- Phase B: ring damage keeps the existing baseline 12; type-2 enrage aim line tracks continuously (not lock-then-fix); type 1 TRANSITION
  jitters in place instead of an orbit entry; the type-3 finale RELEASE settles in one go; pattern-table timing pauses during dash_sweep;
  the bullet-wall arc center is fixed downward (not rotating with the player's position; only the gap avoids the player ±30°).
- Phase C: tiers applied once after `_ready` config load (not per-frame lookup); snapshot barrage (main orchestration 4 lasers + 8 rings),
  telegraph durations, craft movement speed, HP/damage not tiered; bullet-count lower clamp wall 6 / ring 4 / others 1 (fan min 3).
- Fix: `_load_patterns` must `duplicate(true)` deep-copy the shared JSON arrays returned by cfg,
  otherwise tiered interval multiplication pollutes the GameState config cache and stacks onto subsequent Boss instances (caught by boss_pattern_test scene 6).
  **2026-08-01 review supplement**: the same pollution persists in `FIRE_INTERVALS` (`boss.gd:420-421` reads the shared array,
  `:522-523` does in-place `[i] *= interval_mult`, compounding across Bosses under easy/hard) — `_apply_difficulty_scaling`
  must `duplicate(true)` `FIRE_INTERVALS` first as well; registered as AUDIT_VAULT B5. **Fixed 2026-08-01**
  (`boss.gd` `.duplicate()` after fetch; see the AUDIT_VAULT B5 fix-effect record; boss_pattern_test easy/hard pass).
- Movement simplification (**2026-08-02 review registration, D05**): the P2 upgrades required by the §5.1-5.3 per-type movement tables (Type 1 P2 "strafe 200 + vertical oscillation", Type 2 P2 "dash 0.4s/0.5s", Type 3 P2 "strafe 100 + vertical oscillation", Type 3 P1 "y 200-280 sine") were not yet implemented — `boss_movement.gd` has a vertical component only for Type 1 P1 (`_update_press` only called in `FIGHT_P1`), Type 2 dash has no phase distinction, Type 3 none. `git show 3188902^` confirms the gap existed when Phase B landed (not introduced by the A3 split). **Fixed the same day, 2026-08-02, per §5.5 (see above)**; this registration becomes an implementation record.

### 8.3 Difficulty Tier Implementation (§4.4)

- Config: `boss.difficulty_scaling` (`interval_mult` [1.15, 1.0, 0.85], `speed_mult` [0.9, 1.0, 1.1],
  `counts` per-parameter [easy, medium, hard] deltas: fan/homing/cannon/volley/summon/drops ±1, wall/ring/salvo ±2).
- Implementation: `Boss._apply_difficulty_scaling()` applies at the end of `_ready` — pattern-table interval,
  FIRE_INTERVALS, CANNON/ENRAGE/E1/E2/E3 internal rhythms ×interval_mult; all attack bullet speeds ×speed_mult
  (excluding snapshot speed and craft movement speed SWEEP_SPEED); bullet counts adjusted by counts with a lower clamp; fan/homing2 take `_d_fan/_d_homing` at the `_execute_attack`
  dispatch site. Tier = `GameState.DIFFICULTY_ORDER.find(GameState.difficulty)`, unknown falls back to medium.

### 8.4 Value Calibration Verification Results

- Assertion tests: `boss_phase_test` / `boss_enrage_test` / `boss_pattern_test` (incl. easy/hard tier scenes) all green;
  regression list (hit_logic / difficulty / smoke / enemy_combat / elite_turret_event /
  formation_strike_event / base_system / `--quit-after 300` / `--import`) all green.
- autoplay 480s probe and perf_bench results are in the respective commit notes/reports; TTK and feel belong to manual-play calibration,
  and without manual-play conditions, the probe showing no [ANOMALY] and the frame-time magnitude stand in.
