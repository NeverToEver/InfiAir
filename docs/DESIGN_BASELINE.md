# InfiAir Design Baseline (DESIGN_BASELINE)

> **Document status**: This is the **single authoritative document** for this project's design intent and architectural conventions. When reviewing technical debt, assessing the impact of changes, or planning future work, treat this document as the authority; the specialized design docs (`BOSS_REDESIGN` / `META_HUD_DESIGN` / `ELITE_TURRET_EVENT` / `FORMATION_STRIKE_EVENT` / `INTRO_CINEMATIC` / `RETURN_HOME_CINEMATIC` / `ENDLESS_BALANCE_PLAN` / `EXIT_FLOW`) provide implementation-level detail per subsystem. In case of conflict, this document takes precedence and the specialized docs must be revised to match.
>
> **Maintenance convention**: Any adjustment to direction/architecture/balance conventions must be registered here and synced with the "Document Sync Requirements" section of `AGENTS.md`; after technical-debt fixes, backfill status here and sync `docs/AUDIT_VAULT.md`.
>
> **Status snapshot (revised 2026-08-02)**: C-series (Godot best practices & syntax conventions) 35 items processed and closed (incl. design confirmations to leave code unchanged / verified no risk; see the vault); 31 headless assertion scenes 0 FAIL (1113 assertions); A-series SOLID audit leftovers A3/A4/A5/A8 partially unresolved (see §7); performance optimization plan fully landed (enemy spawn path unified through pooling, see §7.2 and `docs/archive/2026-08-02-performance-optimization-plan.md` §12).

---

## Table of Contents

1. [Product & Gameplay Design Baseline](#1-product--gameplay-design-baseline)
2. [Technical Architecture Baseline](#2-technical-architecture-baseline)
3. [Global Invariants & Development Conventions](#3-global-invariants--development-conventions)
4. [Data-Driven Design & Balance System](#4-data-driven-design--balance-system)
5. [Testing & Verification Baseline](#5-testing--verification-baseline)
6. [Persistence & Security Boundaries](#6-persistence--security-boundaries)
7. [Known Technical Debt](#7-known-technical-debt)
8. [Future Work Directions](#8-future-work-directions)

---

## 1. Product & Gameplay Design Baseline

### 1.1 Product Positioning

Standalone 2D top-down shoot 'em up / score attack, Godot 4.6 + GDScript, GL Compatibility renderer, design viewport 1920×1080 (`canvas_items` stretch, `keep` aspect). **Score-only**: no drops, no pickups, no equipment; score is the only progress currency. Early rewrite of the Python/Pygame `airwar-game`; has since evolved independently (historical alignment records archived and frozen in `docs/archive/PORTING_PARITY.md`).

### 1.2 Core Gameplay Loop

```
Auto-fire & wave spawning → score-milestone 3-choose-1 buff → 3-type Boss rotation with enrage phases
→ mothership resupply / fire platform → homecoming base mid-run refit → continue the same run
```

A single run extends indefinitely (endless mode, see §1.4) with no fixed level endpoint; the endgame paradigm is a **doom curve** (player growth is bounded, enemy pressure is unbounded — the player is eventually defeated, which is what makes score meaningful).

### 1.3 Scoring & Economy

- **Score**: `GameState.add_score(v)`, internally **scaled by difficulty multiplier** (Easy ×1 / Normal ×2 / Hard ×3). Kills of normal enemies, Bosses and event units all flow through it.
- **Boss kills**: `GameState.add_boss_kill(score_scale)` → `add_score(500 × score_scale)`, also advancing RP / boss_kills counter / difficulty growth.
- **RP (in-run economy)**: accumulates from kills/score; spent on mothership resupply; does not persist across runs.
- **Milestones**: hitting a score milestone triggers a 3-choose-1 buff (`buff_select`); buff cards covered by `buff33_test` and `buff_panel_test`.

### 1.4 Difficulty & Endless Curve (single source of truth `docs/ENDLESS_BALANCE_PLAN.md`)

**Endgame paradigm (decision D1, closed 2026-07-29)**: doom curve — player growth has a hard cap, enemy growth is unbounded.

- **Difficulty multiplier**: `mult = 1 + progression.per_boss_kill(0.5) × boss_kills + time component`.
  - **Time component**: quantized steps of `progression.time_step_seconds` (30s); every 10 minutes + `progression.per_ten_minutes` (1.0), i.e. `floor(run_time / 30) × 0.05`; counts only in-run survival time `run_time` (tree pauses excluded); quantization avoids HUD drift and lets tests pin values.
  - **Hard cap fully removed**: the old `2^n + ×8 cap` formula is abandoned. `GameState._recompute_difficulty()` computes uniformly (kill-triggered + time-tier-triggered + save-restore recompute), broadcasting `difficulty_changed` across tiers.
- **Enemy growth ceiling**: Boss HP scales linearly with mult (the 50s escape DPS check naturally becomes a "fail to kill → escape" pressure valve); `enemies.hp_ramp_factor` / `enemies.damage_ramp_factor` (k=0.08) / spawn-interval ramp all get unbounded pressure channels.
- **Survival axis tightened**: `extra_life` cap 99→**10 stacks** (total HP 100+500=600 capped); card text "stacks infinitely"→"max 10 stacks"; the positive feedback of vampirism capped at 10% heal is offset jointly by the HP cap and the unbounded damage ramp.
- **Event units take difficulty**: turret/formation fighter HP multiplied by `GameState.enemy_hp_ramp()`, unified convention.
- **Decision D2**: hard difficulty has the fastest buff cadence (score ×3, milestone threshold only ×1.5) — **intentional design** (avoids overly sparse buff cadence on high difficulty); balance unchanged.
- New top-level `progression` section; script `cfg()` fallback values match the json.

### 1.5 Buff System

- 16 buffs (`ui_buff_icons` procedural glyphs + category colors), gained via milestone 3-choose-1, most stack (stack caps from `buffs.*.max_stacks`; extra_life excepted, tightened to 10).
- Card text goes through `BUFF_%s_DESC` translation keys (single source of truth); dead `desc` text in the pool removed.
- Key scaling factors: `buffs.rapid_fire.factor` (fire interval ×0.75 = +33%/stack, matches card copy), `armor.multiplier`, `evasion.chance`, `regen.heal_per_sec`, `slow_field.factor`, `laser_beam.*` (line-segment damage, not projectiles), `explosive.*` (unlock gate `boss_kills>=3` in config), `mothership_recall.cooldown_factor`, etc.
- **Aim assist** (`player.aim_assist`): enemies roll `aim_marked` per `mark_ratio` (0.25); AimFrameLayer draws bracket frames; AimCrosshair follows `aim_point()`; when the crosshair is inside a bracket, new shots write `Bullet.homing_target` tracking (limited by `homing_time`), otherwise they fire straight at the crosshair; magnet/weak tracking share a distance falloff (full assist within 400px → linear decay to 0.3 floor at 1400px).

### 1.6 Boss System (single source of truth `docs/BOSS_REDESIGN.md`)

- **3-type rotation**: the Nth Boss = type `(N-1) % 3 + 1`, rotated by kill count in `spawner._spawn_boss()`.
- **Table-driven phase patterns**: P1/P2/ENRAGE, pattern tables `boss.phases.typeN` + telegraph windups; three differentiated enrages (`boss.enrage.type_*`); enrage slows the player ×0.35 instead of freezing; difficulty tiers multiplied once in `_ready` (`boss.difficulty_scaling`: projectile count/interval/speed three tiers).
- **Fight anchor line**: `FIGHT_Y` is an offset from the view top edge; all usages go through `_fight_anchor_y()`.
- **Escape mechanic**: 50s timeout escape (DPS-check pressure valve); escape **does not advance rotation or grant respite** (B3 contract); HP bar hidden + spawner re-schedules.
- **Implementation structure**: facade `Boss` + 4 responsibility classes `BossFire` (bullet patterns) / `BossAttacks` (attack state machine) / `BossMovement` (movement + P1 push-down) / `EnrageSequence` (enrage state machine). **Known leftover**: the central `match` was only relocated; 7 per-type branches remain (see §7 A3/A4).

### 1.7 Mothership & Homecoming

- **Mothership summon**: triggered after charging (`dock` H); the run does not pause; input locked + event-driven invulnerability during the sequence. Hangar mini-window → warp gate → mothership DESCEND decelerating pass-through → dual-ring deceleration bands → DOCKING tow-recovery of the player into the pod (`player.enter_pod()`) → resupply → RELEASE (`exit_pod()`) → loiter/leave. Values in `effects.mothership_summon`.
- **Fire platform**: GATLING strafing / MISSILE targeted strikes while loitering — fire support.
- **Homecoming**: hold B (`homecoming`, charged by `effects.home_charge_time`) → input locked → spawner stopped → mothership recalled → `save_run()` → `starfield.warp(18)` → homecoming cinematic → base UI landing (tree stays paused).
- **Base refit**: `base_console.gd`, holographic station skin (`dawn_station.gd`); "Continue Sortie" triggers an orbital-strike sweep (`orbital_strike`; Bosses exempt, fighters explode one by one), then plays the fighter entry animation to resume the run.
- **Entry transition animation**: played after the intro cinematic finishes and after the "Continue Sortie" sweep (`player.play_entry_animation()`, values `player.entry`) — high-speed dive to the lower third of the screen → gentle backward (downward) drift; only left/right adjustable during it, vertical locked, fully invulnerable (no flashing); enemy spawning delayed until the animation ends; replaces the old "in-place invulnerable flicker" entry.

### 1.8 Event System

**Elite Turret Event** (`docs/ELITE_TURRET_EVENT.md`, heavy 30s event):
- A strike carrier descends from the screen top as the stage backdrop, raising multiple auto-targeting turret emplacements (each with its own HP bar / fire / weak tracking); score-only, ammunition fully reuses the enemy-side bullet types, no new ammunition types.
- **Mutually exclusive with Boss**: Boss trigger frozen at most once without accumulation (`_boss_frozen`/`_boss_pending`); normal waves paused during the event (`_waves_paused`); resumed via `BOSS_DELAY` after the event ends.
- Three-node enemy dialogue (`ETQ_1..10`, 3 of 10 without replacement) + bottom-left comm overlay (`comm_overlay`); reward `reward_score` 500 (scaled by difficulty multiplier); timeout failure grants no reward.
- Trigger: score ≥`min_score`(800), roll every `trigger_interval`(45s) at `trigger_chance`(0.35), then `cooldown`(60s).

**Bombing Formation Event** (`docs/FORMATION_STRIKE_EVENT.md`, lowest-priority random event):
- 3/4/5 attackers (by difficulty) dive in a wedge formation → turn 90° to cross the screen → drop fuse-fused bombs one by one (landing warning rings shrink with the fuse; AoE only hurts the player) → exit. Full wipe grants a reward.
- **Does not freeze the Boss** (the Boss fires on schedule; overlap controlled by warning rings), but **occupies the wave slot** (normal waves paused at runtime, shares `_waves_paused`, mutually exclusive with the Elite Turret event); interruptible by homecoming `abort()`.
- Trigger: Boss not active + Elite Turret not active + cooldown done + score ≥`min_score`(500).

**Priority chain** (checked in order every tick in spawner `_process`; if the earlier one launches, this tick is skipped): Boss (highest) → Elite Turret → Bombing Formation (lowest).

### 1.9 Meta HUD Health & Hit Feedback (single source of truth `docs/META_HUD_DESIGN.md`)

- Fullscreen post-processing CanvasLayer layer=1 (above the world, below the HUD; HUD raised to layer=2), `meta_health.gdshader` + `hint_screen_texture`.
- Pipeline: hit layer (radial chromatic aberration + hand-written 6-tap radial blur) → directional ripples (edge 12% band) → desaturation / cool-cyan tint + vignette → crack compositing (Voronoi distance field prebaked once; window SubViewport GPU 512² / headless CPU 64² equivalent fallback).
- Health state machine: NORMAL/CAUTION/DAMAGED/CRITICAL/DYING (thresholds 0.75/0.50/0.25/0.20); fast entry on descent (tau 0.10s), slow exit on ascent (tau 0.80s + staggered dissipation); DYING heartbeat 1.0–1.2Hz, breathing ±1.5%, HUD shake ±2px, view narrowing 6%.
- Explicit layer (SegmentedBar health bar, value fallback) + implicit layer (desaturation/vignette/heartbeat, degradable via "Reduce Flashing"); with `reduce_flash` on: chromatic aberration ×0.4, breathing/shake/heartbeat visual pulses disabled (SFX kept).
- Adaptive readability: registry-driven brightness proxy (bullet active count ×0.002 + explosion count ×0.15), zero GPU readback; LOD1 downgrade skips aberration/blur/ripples.

### 1.10 Cinematics

- **Intro cinematic** (`docs/INTRO_CINEMATIC.md`): 6 shots, 17.3s hard sci-fi intro (station destroyed → escape → heading into deep space), 2.35:1 letterbox, subtitle cards `INTRO_SUB_1..6`; triggered by "New Game" on the start panel; gate `current_scene == Main` (no trigger from continue-run/tutorial/tests); skip via Esc/any key/click. Tree paused during playback, root `process_mode=Always`. Phases 1–3 implemented; **phase 4 (low-end retest / gamepad-mobile adaptation / README notes) not done**.
- **Homecoming cinematic** (`docs/RETURN_HOME_CINEMATIC.md`): 7 shots, 11.8s (jump → captured → falling asleep), mirrors the intro architecture; Esc skips via `SKIP_RETURN` (1.2s input grace against accidental skip, `effects.return_skip_grace`); both play-through and skip land on the base UI with the tree paused; BGM fades out during shot 7 darkening.
- **Shared factories**: `cinematic_fx.gd` (soft_glow/particles/shockwave/beam/speed_lines/radial_streaks, zero heap allocation in driver classes), `dawn_station.gd` (station destroyed / holographic ghost state factories; shared by intro shot 1, homecoming shots 2/3/4 and the base backdrop).

### 1.11 Tutorial

- Standalone scene `scenes/tutorial.tscn`, self-handles return (Esc exits the tutorial back to the main screen, not through the BackNavigator state machine).
- Aligned with real-run logic: `_ready` creates AimFrameLayer (aim assist works in the tutorial); phase 1 forces a marked target; phase 4 long-press H charges → warp gate → mothership `begin_warp_in` → docking resupply (hangar mini-window omitted, same entity path as main).
- Isolates run state and save data on entry; must restore `Engine.time_scale = 1` on exit.

### 1.12 Exit / Back Navigation (single source of truth `docs/EXIT_FLOW.md`)

- All platform back inputs converge on `BackNavigator.go_back()`, dispatched through the pure decision function `decide_back_action()` (confirm dialog → cinematic skip → settings/base/blocked/game over → buff bar → pause → top → in-combat).
- Page hierarchy: L3 modal ExitConfirm → L2 overlays (Settings/Base/GameOver/Buff/cinematics) → L1 gameplay (HUD⇄Pause + buff scroll bar) → L0 StartPanel.
- Exiting mid-combat requires a second confirmation (red warning: progress lost); after confirm, `_execute_exit_cleanup`: saves profile, deletes the in-run save, stops unfinished SFX, fades out and exits.
- Platforms: PC Esc / gamepad `ui_cancel` / Android system back gesture, same state machine.

---

## 2. Technical Architecture Baseline

### 2.1 Tech Stack

- **Engine**: Godot 4.6 (standard, no .NET); `project.godot` declares `4.6` + `GL Compatibility`; both desktop and mobile use `gl_compatibility`.
- **Language**: pure GDScript; `scripts/tools/` holds offline Python tools (stdlib; the texture generator additionally needs PIL), not runtime dependencies.
- **Assets**: `assets/sprites/` PNG, `assets/audio/` WAV, `assets/fonts/NotoSansSC.ttf` (OFL open source).
- **Rendering**: no HDR bloom/Compositor; emissive uses ADD-blended fake bloom (`_glow()` convention); fullscreen post-processing via canvas_item shader + `hint_screen_texture`.
- **Single autoload**: `GameState` (`autoload/game_state.gd`).

### 2.2 Main Node Tree (`scenes/main.tscn`)

```
Main (scripts/main.gd)
├─ Starfield / Camera2D
├─ Player
├─ Spawner
├─ BulletPool / EnemyPool
├─ HUD (layer=2) / BuffUI / PauseUI / SettingsUI / GameOverUI / BaseUI
├─ StartPanel / ExitConfirm
├─ BackNavigator
├─ MetaHealthFX (created in _ready at runtime, layer=1)
├─ AimFrameLayer (created in _ready at runtime, world coordinates)
├─ IntroCinematic / ReturnCinematic (layer=35, instanced on demand at runtime)
├─ OrbitalStrike (layer=24, Continue Sortie sweep)
├─ MothershipSummonWindow (layer=24) + WarpGate (world coordinates)
└─ EliteTurretEvent / FormationStrikeEvent (created in _ready, registered to the spawner)
```

**Convention**: all dynamic run entities hang under Main so sweep logic and test traversal can see them. `scenes/` holds the main scene, player, enemies, Boss, bullets, mothership, cinematic and tutorial scenes; same-named behavior scripts live in `scripts/`.

### 2.3 Responsibility & Service Split (A2 split baseline)

- **GameState facade**: global score/HP/buff/difficulty/RP/task/route/settings + signal bus; public API delegated and forwarded — callers and tests unaffected.
- Four **non-autoload composed service classes** (keeping the "single autoload" convention):
  - `BalanceService` (RefCounted): holds `_balance`; `load()/cfg()/enemy_hp_ramp()/enemy_damage_ramp()`.
  - `SaveManager` (RefCounted): `exists/save/load/delete/quarantine/sanitize_num`; corruption isolation sets `last_was_corrupt`.
  - `SfxPlayer` (Node, child of GameState): `build_pool/play/stop_all`, headless short-circuit; `SFX_*` constants kept.
  - `EntityRegistry` (RefCounted): registers/removes `enemies/player_ref/player_hitbox/bullet_pool/enemy_pool/aim_frame_layer/camera_ref`.
- **Key point**: avoid per-frame `get_nodes_in_group` on hot paths; use the registries `GameState.enemies` / `player_ref` / `player_hitbox`.

### 2.4 Object Pools & Registries

- **Bullets**: unified through `GameState.bullet_pool.fire()`; `Bullet` is an Area2D, moves in `_physics_process` (C04), side set by `setup()/activate()`; `activate()` resets the tracking/visual-field list.
- **Enemies**: **unified through the object pool** (2026-08-02, performance optimization plan `920e5e9`): normal waves, Boss-3 minions and formation fighters all go through `GameState.enemy_pool.spawn()` (`USE_POOL=false` falls back to direct instantiation as a performance A/B switch). Pooled entities use `reactivate()/deactivate()` for state reset, registry add/remove and death signals; don't treat "all enemies pooled" as the current fact — the `USE_POOL=false` comparison mode still instantiates directly.
- **Guards**: pooled entities must keep `_active` (late guard) and `_repooling` (wraps reparent to prevent `_exit_tree` misfires); `reactivate()/deactivate()` handle state reset, registry add/remove and death signals; outsiders must not bypass the lifecycle to free pooled objects.
- **Explosions**: unified through `Explosion.spawn_at()`, reuse the object pool (`pool_cap` config), `process_mode=Always` (death explosions still play under a paused tree).

### 2.5 Input & Settings

- Input map defined in `project.godot` (move/`boost`/`fine_move`/`dash`/`dock`/`homecoming`/`give_up`/`buff_panel`/`restart`); don't modify existing mappings for unrelated needs; keys rebindable (`keybind`), persisted in profile. **Gamepad defaults assembled at runtime** (P0-1): `GameState._bind_joypad_defaults()` appends left-stick move/action keys (A/RB/LB/X/Y/L3/R3) and right-stick aim actions (`aim_x`/`aim_y`, virtual crosshair driven incrementally from `player.aim_point`) at startup via InputMap; deadzone applied to all gamepad actions via `set_joy_deadzone()`. **PS gamepad auto-detection** (GUID vendor 054c; identical layout, only labels map to ✕○□△/L1-R1; `joy_button_label()` for UI display).
- Settings: difficulty, keybindings, language, view zoom, window size, aim-assist tier, `reduce_flash`, `mouse_lock` (mouse locked inside the window, on by default: only while the in-combat crosshair is active and the window is focused, the mouse that exits the content area is warped back to the inner edge, preventing crosshair loss of control; released during pause/non-crosshair states and on focus loss), gamepad params (`joy_aim_speed` right-stick aim sensitivity, `joy_deadzone` stick deadzone; sliders in the "Gamepad" section of the settings page), SFX/music volume; language switching via `GameState.set_locale()`, UI refreshes listening to `locale_changed`.
- View zoom and window size are **two independent** profile settings.

### 2.6 Rendering & Visual Layers

| layer | Content |
| --- | --- |
| 1 | MetaHealthFX (fullscreen post-processing, above world, below HUD) |
| 2 | HUD |
| 12 | CommOverlay (comm overlay) |
| 24 | OrbitalStrike / MothershipSummonWindow |
| 35 | Intro/Return Cinematic |
| 40 | ExitConfirm |

Pause-state UI always uses `process_mode = Always`, pause managed via `get_tree().paused`; BGM loops only set `loop_mode = LOOP_FORWARD` (no BGM stop in `_exit_tree`).

---

## 3. Global Invariants & Development Conventions

> These are the "laws" of this codebase. Any change (fix, refactor, new feature) must preserve them; violating them counts as breaking the design baseline.

### 3.1 Collision & Damage

- **Collision layers**: 1=`player`, 2=`player_bullet`, 3=`enemy` (incl. Boss), 4=`enemy_bullet`.
- Player bullets resolve against the `enemy` group; enemy bullets and enemy bodies resolve against the `player_hitbox` group.
- **Player hits only count via the `Player/Hitbox` Area2D** (design r=7 × `world_scale`, current runtime value 2.8); the `CharacterBody2D` body's radius-22 circle has no collision purpose (mask=0) and must not be used for hit detection.
- Player damage goes through the unified `take_damage(amount, from_pos := Vector2.INF)`, emitting the `player_damaged` signal (D8 directional feedback).

### 3.2 Craft Scaling (world_scale Lever)

- **Single lever**: top-level `world_scale` in `balance.json` (currently **0.4**), cached at runtime as `GameState.world_scale`.
- **Craft-size family** (texture scale, collision radius, muzzle/dock/turret/tow offsets, effect scales tied to the craft) is stored as **design values** (1.0 baseline) in json/tscn/script fallbacks, multiplied by `world_scale` uniformly in the entity's `_ready()/setup()`.
- **Gameplay-range family** (AoE radii, lock/clear-bullet radii, deceleration rings) and indicators/cinematics/UI are **not multiplied**.
- **Idempotent assignment** (`radius = design value × world_scale`), never `*=` accumulation (shared sub_resources would rescale per instance); scenes that write radius at runtime must set `resource_local_to_scene = true`.
- **Exception**: `mothership.DRIVE_MARGIN` multiplied by `world_scale` is an intentional exception (constant visual screen margin for the hull edge, B11).

### 3.3 Viewport & Coordinates

- Camera fixed at `(960, 540)`, only `zoom` changes; all screen-edge/out-of-bounds/spawning/visible-area computations must use `GameState.view_world_rect()`, **never hardcode 1920×1080 / 960 / ±1600**.
- Boss fight anchor line `_fight_anchor_y()`, enemy hover band / entry anchor baseline all adapted accordingly.
- Cinematics are laid out in 1920×1080 design coordinates (fixed camera; intentional exception).

### 3.4 Balance Access (cfg)

- Unified via `GameState.cfg("dotted.path", default)`; missing/corrupt JSON falls back to script defaults, and both must stay consistent.
- **No per-frame cfg() on hot paths**: read into cache once in `_ready()/setup()`; high-frequency `_process/_physics_process` must not query the JSON dict.
- Tweakable values change only in `data/balance.json` (via `scripts/tools/balance_editor.py`), never script fallbacks; after changes run `gen_balance_map.py` to refresh `docs/BALANCE_MAP.md`.

### 3.5 Trigonometry & Hot Paths

- No direct `sin()/cos()` in `_physics_process`; use the `Enemy.sin_fast()/cos_fast()` lookup tables.
- No per-frame allocation on hot paths: pre-allocated effect point sets written in place (value-semantic copies via `points[i]=` don't take effect), `PackedVector2Array` reuse, lazily-cached node references.
- High-frequency fields cached in `_ready`; `Time.get_ticks_msec()` fetched once per frame and reused.

### 3.6 Coroutine Discipline

- **No `await get_tree().create_timer()` / timer-suspended coroutines** (coroutine state leaks on process exit and drags referenced resources along).
- Delayed callbacks use a **one-shot `Timer` node + `timeout` signal** (see `spawner._schedule()`); the Timer is freed with the scene tree.
- No `await get_tree().process_frame` followed by out-of-tree access without an `is_inside_tree()` guard.

### 3.7 GDScript Style & Typing

- Godot 4 official style: Tab indentation, type annotations, `CONSTANT_CASE` constants, `_` private prefix, `signal.emit()/connect()`.
- `setup()` is called before `_ready()`; don't rely on `@onready`, use `$node_path`.
- After adding a script with `class_name`, must run `--headless --import` to refresh the global class cache, or referencing scripts fail to compile.
- Annotate concrete types: `Array[int]`, `EnemyPool`, `enemy: Enemy`; bare `Array`/`Node` kept minimal (C18/C20 largely cleaned).
- **Known convention exception (C19, design-confirmed)**: `CONSTANT_CASE` naming is used for mutable script fallback default vars — a project data pattern; kept as-is, not treated as a violation.

### 3.8 Signals & Lifecycle Safety

- Connect with Callable; guard re-entry into the tree with `is_connected`; explicitly disconnect/clean up registrations in `_exit_tree`.
- Chained `get_parent().get_node("X")` access must null-check (`get_node_or_null`) or use unique names via `%X`; no per-frame string node lookups on hot paths (lazy-load cache).
- Pool `_exit_tree` clears the GameState global pool registrations to prevent dangling references on scene unload.

### 3.9 i18n

- All user-visible text goes through `tr("UPPER_SNAKE_CASE_KEY")`; new keys sync both `zh` and `en` columns in `data/translations.csv`; re-import to generate `.translation`.
- Dynamic text uses `%d`/`%s` placeholders; language switching via `GameState.set_locale()`, UI listens to `locale_changed`.
- **No hardcoded Chinese user-visible strings** (C08/C26 cleaned).

### 3.10 Object Lifecycle

- Tutorial isolates run state and save data on entry, restores `Engine.time_scale = 1` on exit.
- Runtime-created nodes keep references; don't rely on Godot-generated node names.
- Mothership/cinematic presentation nodes `skip()/abort()` are idempotent, unified exit signals.

---

## 4. Data-Driven Design & Balance System

### 4.1 `data/balance.json`

Top-level sections (Tab indentation, canonical JSON without inline objects; written by `balance_editor.py` with automatic `.bak` backup):
`world_scale` / `player` / `enemies` / `elites` / `boss` / `spawner` / `mothership` / `buffs` / `milestones` / `difficulty` / `progression` / `effects` / `tutorial` / `elite_turret_event` / `formation_strike_event`.

Key sections:
- `difficulty`: difficulty-tier multipliers (score ×1/×2/×3, HP ×0.75/×1/×1.5, milestone thresholds).
- `progression`: doom curve (per_boss_kill / per_ten_minutes / time_step_seconds).
- `boss.phases` / `boss.enrage.type_*` / `boss.difficulty_scaling`: Boss phase pattern tables, three differentiated enrages, difficulty-tier tables.
- `effects.*`: starfield, shake, meta_health, mothership_summon, orbital_strike, explosion and other presentation values.
- `elite_turret_event.*` / `formation_strike_event.*`: event parameters.

### 4.2 Balance Access & Docs

- All `cfg()` call-site indexes and json/script dual-write alignment reverse lookups live in `docs/BALANCE_MAP.md` (**generated file**; re-generate with `gen_balance_map.py` after changing keys).
- Tune values with `balance_editor.py` first (browser editing, validation, backup).

---

## 5. Testing & Verification Baseline

> Full command list in `docs/TESTING.md`. Tests are not a unit-test framework: `test/*.tscn` starts a GDScript scene that self-checks via `[PASS]/[FAIL]` output and exit code.

- **Minimal required set**: `--headless --import`, `--quit-after 300`, `smoke_test.tscn`; add `base_system_test.tscn` when save/base/mothership is involved.
- **Full assertions**: 31 assertion scenes (currently all green 0 FAIL, 1113 assertions); run per-subsystem as needed (boss/events/cinematics/object pools/i18n/navigation etc.).
- **Special scenes**: `perf_bench` must run with `--fixed-fps 1000`; `autoplay_test` is a long-running anomaly probe (registry consistency dual comparison, animation paths, hang timers, buff caps, phase counting).
- **Test side effects**: tests may read/write `user://savegame.json` / `profile.json`; new tests should call `GameState.delete_save()` first and clean up their own persistence; `balance_test` overwrites `data/balance.json` to verify corruption fallback then restores it — don't hand-edit concurrently.
- **Visual verification**: windowed screenshots manually reviewed (headless has no usable screenshots); `visual/ui/return/intro/summon/meta_fx/hud` capture tools.

---

## 6. Persistence & Security Boundaries

- **Run save** `user://savegame.json`, **out-of-run profile** `user://profile.json`; both managed by GameState with version fields; profile stores high score / local leaderboard / difficulty / keybindings / language / view / window size / tutorial state / gamepad params.
- **Corruption isolation**: corrupt JSON quarantined as `<file>.corrupt` with `save_corrupt`/`profile_corrupt` flags notifying the start screen; don't bypass the recovery flow.
- **Robustness**: `load_profile` type-guards key_bindings (C02); `_apply_balance` validates difficulty sub-keys and `milestones.base` non-empty (C03); safe boolean reads prevent `bool("false")→true` (C16).
- **No external interaction**: no network/plugins/remote services/secrets; the offline `balance_editor.py` only listens on 127.0.0.1.
- **Distribution**: `export_presets.cfg` + `release.sh` dual-platform export, artifacts in `builds/release/` (gitignored), distributed via GitHub Releases (not committed); `packaging/` provides dual-platform install/uninstall scripts.

---

## 7. Known Technical Debt

> Legend: ✅ Fixed / ⚠️ Partial / ❌ Not fixed. Full registry and fix-effectiveness records in `docs/AUDIT_VAULT.md` (proprietary vault; forbidden to delete/merge).

### 7.1 Architectural Debt (A-series Legacy)

| ID | Item | Status | Impact & Fix Direction |
| --- | --- | --- | --- |
| A3 | Boss split into facade + 4 responsibility classes, but the central `match` was only verbatim-relocated into `BossAttacks.execute()` (10 branches); 7 per-type branches remain | ⚠️ | New types still require editing existing functions; O principle not achieved. Direction: replace the central match with table/factory, converge type branches into data-driven |
| A4 | Open/closed: Boss attack match + type branches unmanaged; `player.gd` buffs are still functional inline branches (`_refresh_buff_factors` + `pow(factor, buff_count)` family), not a declarative effect table | ⚠️ | A4a (enemy strategies) / A4b (event trigger base class) landed; remaining are Boss and Player buffs. Direction: declarative buff effect table |
| A5 | Dependency inversion: Boss/event dependencies on Spawner should be injected, not group lookups | ⚠️ | **Injection landed (corrected 2026-08-02, `bdb0274`)**: Boss/Elite Turret receive references via `set_spawner()`, replacing group lookups; GameState as config center + registry is an intentional performance trade-off, kept. Direction: converge remaining dependencies |
| A8 | Player responsibility split: damage/dash extracted into components, **visual responsibilities (trail/ghost/crosshair/hit point/PlayerBuffVisuals) still live in Player** (~697 lines) | ⚠️ | Direction: extract a `PlayerVisuals` component |

### 7.2 Style & Performance Legacy

| ID | Item | Status |
| --- | --- | --- |
| C34 | `boss_pattern_test` bullet speed/damage now reads instance constants; `difficulty/buff33/elite/formation` hardcoded assertions kept as logic-verification anchors | ⚠️ (partially design-confirmed) |
| Enemy spawn path | Normal waves instantiate directly vs Boss-3 minions through the object pool, two paths coexist | ✅ **Unified (2026-08-02)**: normal waves pooled via `EnemyPool.spawn()` (`spawn`/`reactivate` extended with optional `p_bullet_type`), `USE_POOL` switch kept as A/B comparison; regression smoke 142 / pool_reuse 12 / enemy_combat 33 PASS |

### 7.3 Phase Legacy (ROADMAP Phase 0 Backlog)

- Dead-code cleanup: unused references in `main.gd`, always-false branches in `hud.gd`, zero-connect signals etc. (see `docs/archive/2026-07-22-audit-fix-plan.md`).
- Mothership `_start_release()` idempotency guard; `profile_corrupt` corrupt-profile prompt consumption.
- Cinematic phase 4: low-end retest, gamepad/mobile input adaptation, README cinematic notes (`INTRO_CINEMATIC`).

---

## 8. Future Work Directions

> Direction-type decisions single source of truth: `docs/ROADMAP.md`. This section indexes direction breakdowns and landing points.

### 8.1 Near Term (technical-debt closeout, no new gameplay)

1. **Architectural debt convergence** (§7.1): A4 Boss/Player buff declarative effect table, A8 Player visual component extraction, A3 type-branch convergence. All changes must preserve every §3 invariant + full tests 0 FAIL.
2. **Enemy spawn path unification**: ✅ **landed (2026-08-02, with the performance optimization plan)** — normal waves now uniformly pooled via `EnemyPool.spawn()` (§7.2 status update), `USE_POOL` switch kept as A/B comparison.
3. **Dead-code cleanup** and idempotency guards (§7.3).

### 8.2 Mid Term (experience deepening)

- **Endless-segment field calibration**: fine-tune deep-run (>15 min) feel of `progression.per_boss_kill / per_ten_minutes / ramp` coefficients, editing `balance.json` directly and appending records to `ENDLESS_BALANCE_PLAN §6`; use the `autoplay_test` long-run probe to verify no late-game "one-sided HP inflation, zero pressure" steady state.
- **Cinematic wrap-up**: `INTRO_CINEMATIC` phase 4 (low-end retest / gamepad-mobile adaptation / README).

### 8.3 Deferred / Cut (restart requires explicit user decision; registered in `ROADMAP.md` Phase 3)

- Local account system (spec archived in commit `7aacd3f`), standalone main-scene entry page (appendix B), online leaderboard (decided against), collaboration & release engineering (CONTRIBUTING/CI/semantic versioning), content evolution (new Buffs/enemies/elites/Bosses/mobile touch/mothership extensions).

### 8.4 Mandatory Rules for Future Changes

1. Preserve all §3 global invariants (collision layers/world_scale/view_world_rect/cfg/coroutines/i18n/hot paths/pool guards).
2. Tweakable values change only in `balance.json`; run `gen_balance_map.py` and the minimal verification set.
3. New features register direction in this §8 and `ROADMAP.md`; specialized design docs carry implementation-level specs.
4. Fixes/new code: full tests 0 FAIL (31 assertions + autoplay probe), visual changes reviewed via windowed screenshots.
5. Technical-debt fixes backfill "fix-effectiveness records" in `AUDIT_VAULT.md`.

---

*Document nature: single correction baseline for design intent and architectural conventions. Reviewer: executed per user instructions · Generated: 2026-08-01.*
