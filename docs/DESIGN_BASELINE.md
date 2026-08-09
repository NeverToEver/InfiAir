# InfiAir Design Baseline (DESIGN_BASELINE)

> **Status**: sole amendment authority for design intent & architecture conventions. Conflict with system docs (`BOSS_REDESIGN`/`META_HUD_DESIGN`/`ELITE_TURRET_EVENT`/`FORMATION_STRIKE_EVENT`/`INTRO_CINEMATIC`/`RETURN_HOME_CINEMATIC`/`ENDLESS_BALANCE_PLAN`/`EXIT_FLOW`) → this file wins; revise the system doc. Direction/architecture/balance-caliber changes register here + sync `AGENTS.md`; debt fixes backfill here + `docs/AUDIT_VAULT.md`.
>
> **Snapshot (2026-08-07)**: 10 systematic audits (A–L, incl. full SWE review) all resolved, no P0; 55 assertion scenes 0 FAIL; A-series **A8 fixed 2026-08-03** (PlayerVisuals split, §7.1) + **A5 closed 2026-08-07** (residual dep convergence, §7.1); perf optimization + 4 fairness mechanics landed; 2026-08-05: unified entity/event managers + base task rotation & fog events landed; **2026-08-07: mobile touch restarted & landed (VirtualControls 触屏输入层)**; Phase 0 closed (ROADMAP).

## 1. Product & Gameplay

### 1.1 Positioning
Single-player 2D top-down shmup; Godot 4.6.2 .NET + C# (full migration 2026-08-08, zero GDScript), GL Compatibility, 1920×1080 (`canvas_items`/`keep`). **Score-only** (no drops/pickups/equipment). Remade from `airwar-game`, now independent (`docs/archive/PORTING_PARITY.md`).

### 1.2 Core Loop
```
auto-fire + waves → milestone buff 3-choice → 4 rotating bosses + enrage
→ mothership supply/fire platform → return-to-base restock → same run continues
```
Endless (§1.4), no fixed ending; endgame = **inevitable-death curve** (bounded player growth, unbounded enemy pressure).

### 1.3 Scoring & Economy
- `GameState.add_score(v)`: multiplies difficulty (Easy ×1 / Normal ×2 / Hard ×3); all kills route here.
- Boss kill: `add_boss_kill(score_scale)` → `add_score(500 × score_scale)`; advances RP/boss_kills/difficulty.
- RP: earned from boss kills (+5) and mission claims (+3) only (2026-08-06 audit: baseline claimed "run economy from kills/score" — kills don't grant RP; spent at base console, not carried between runs).
- **RefreshPoints (2026-08-05, `docs/FOG_EVENTS.md` §1)**: separate base-only currency — entering base +1 (`base_task.grant_per_visit`), refresh tasks −2 (`base_task.refresh_cost`); no cap, not carried between runs (run save). Task rotation: 3 active slots drawn from 9-mission pool (`MISSION_POOL`, 3 kinds × 3 goals) without replacement; progress routed by `kind` (kill/survive/boss) so rotated ids still advance; completed-but-unclaimed slots kept on refresh.
- Milestones: score thresholds → buff 3-choice (`buff_select`); covered by `buff33_test`/`buff_panel_test`.

### 1.4 Difficulty & Endless Curve (single source `docs/ENDLESS_BALANCE_PLAN.md`)
**Endgame (D1)**: inevitable-death curve.
- `mult = 1 + progression.per_boss_kill(0.6) × boss_kills + time`. Time: quantized by `progression.time_step_seconds` (30s), + `progression.per_ten_minutes` (1.5)/10min → `floor(run_time/30) × 0.075`; counts live `run_time` only (tree-pause excluded); quantization pins HUD/tests.
- No hard cap (old `2^n + ×8` removed). `_recompute_difficulty()` unified (kill + time tier + save-restore); broadcasts `difficulty_changed`.
- Enemy growth: Boss HP linear × mult (50s-escape DPS check = "can't kill → flees" valve); `enemies.hp_ramp_factor`/`damage_ramp_factor` (k=0.25 HP / 0.20 dmg, 2026-08-04 校准)/spawn ramp unbounded.
- Survival: `extra_life` cap 99→**10** (HP 100+500=600); card "unlimited"→"max 10"; lifesteal ≤10% feedback offset by HP cap + ramp.
- Event units scale: turret/formation HP × `GameState.enemy_hp_ramp()`.
- **D2**: Hard-mode buff pacing fastest (×3 score, ×1.5 thresholds) is **intentional**; unchanged.
- New top-level `progression`; script `cfg()` fallbacks match json.

### 1.5 Buffs
- 19 buffs (`ui_buff_icons` 19 glyphs + category colors), via milestone 3-choice, stackable to `buffs.*.max_stacks` (extra_life: 10).
- Card text via `BUFF_%s_DESC` keys (single source).
- Key scaling: `rapid_fire.factor` (interval ×0.75 = +33%/stack), `armor.multiplier`, `evasion.chance`, `regen.heal_per_sec`, `slow_field.factor`, `laser_beam.*` (line segment, not projectile), `explosive.*` (unlock `boss_kills>=3`), `mothership_recall.cooldown_factor`.
- Aim assist (`player.aim_assist`): `aim_marked` rolled at birth (`mark_ratio` 0.25); AimFrameLayer brackets, AimCrosshair follows `aim_point()`; in-frame → `Bullet.homing_target` (bounded `homing_time`); out → straight fire; magnet/weak-track share falloff (full <400px → 0.3 floor at 1400px).

### 1.6 Bosses (single source `docs/BOSS_REDESIGN.md`)
- Rotation: Nth boss = type `(N-1)%4+1` via `spawner._spawn_boss()`.
- Phase tables P1/P2/ENRAGE (`boss.phases.typeN` + telegraph); 4-type enrage (`boss.enrage.type_*`, player slow ×0.35, no freeze); difficulty tiers × once in `_ready` (`boss.difficulty_scaling`: count/interval/speed).
- Anchor: `FIGHT_Y` = offset from view top; all via `fight_anchor_y()`.
- Escape: 50s timeout flee; fleeing **no rotation advance, no rest** (B3); bar hidden + reorder.
- Structure: facade `Boss` + `BossFire` (danmaku)/`BossAttacks` (FSM)/`BossMovement` (+P1 press-down)/`EnrageSequence`. A3/A4-converged: 3 registries + type param tables, no per-type branches.

### 1.7 Mothership & Return
- Summon (`dock` H charge): run not paused, input locked + event invincibility. Hanger window → warp gate → DESCEND decelerate → dual-ring slow zone → DOCKING pod (`enter_pod()`) → resupply → RELEASE (`exit_pod()`) → loiter/leave. Values `effects.mothership_summon`.
- Fire platform: GATLING/MISSILE during loiter.
- Return: hold B (`homecoming`, `effects.home_charge_time`) → input lock → spawner stop → recall → `save_run()` → `starfield.warp(18)` → cinematic → base UI (tree paused).
- Base: `BaseConsole.cs` + `DawnStation.cs` skin; "continue sortie" → orbital strike clear (Boss kept) → entry animation.
- Entry animation (`player.play_entry_animation()`, `player.entry`): dive to bottom-third → slow backward drift; horizontal-only, vertical locked, invincible (no flicker); spawns delayed. Replaces old stand-still entry.

### 1.8 Events
**Elite turret** (`docs/ELITE_TURRET_EVENT.md`, heavy 30s): carrier backdrop + tracking turrets (own HP/fire/weak lock); reuses enemy bullets. Mutex with Boss (`_boss_frozen`/`_boss_pending`); waves paused (`_waves_paused`), resume after `BOSS_DELAY`. 3-node dialogue (`ETQ_1..10`, 10-choose-3) + comm overlay; reward `reward_score` 500 (× difficulty), timeout = none. Trigger: score ≥`min_score`(800), roll `trigger_chance`(0.35)/`trigger_interval`(45s); `cooldown` 60s.

**Formation strike** (`docs/FORMATION_STRIKE_EVENT.md`, lowest priority): 3/4/5 (by difficulty) wedge dive → 90° cross → fuse bombs (ring shrinks, AoE hits player only) → exit; full kill = reward. Does **not** freeze Boss but **occupies wave slot** (shared `_waves_paused`; mutex with turret); `abort()`-able by return. Trigger: Boss+turret inactive, cooldown done, score ≥`min_score`(500).

**Fog events** (`docs/FOG_EVENTS.md` §2, 2026-08-05, light interference, independent of spawner chain): global singleton `FogEventManager` (child of GameState) — probability roll (`fog_events.trigger_chance`/`check_interval`), `first_delay` opening protection, `min_interval` cooldown after each event, explicit `duration` auto-clear, single-event concurrency; effects via signals (`fog_event_started/ended/fog_direction_shift`) to Player + manager-owned visuals. 4 events: fake_enemies (no-damage/no-collision ghost ships), mental_confusion (input inversion + full-screen tint), bullet_malfunction (bullet angle jitter / misfire / fire-interval jitter), direction_shift (periodic forced movement vector). Cleared on return (`end_active`) and death; no score/economic interaction.

**Priority chain** (spawner `_process` tick → 2026-08-05 起由统一事件管理器 `GameEventManager` 按注册序检查, `docs/EVENT_MANAGER.md`): Boss → elite turret → formation strike. Fog events trigger from the manager independently (no wave-slot occupancy, no Boss mutex).

**Unified event manager** (`docs/EVENT_MANAGER.md`, 2026-08-05): all random events (fog 4 + encounters 2) share one `EVENT_FACTORIES` registry, grouped concurrency (`fog`‖`encounter` — fog may fire during encounters, encounters never overlap each other/Boss), unified trigger policy (balance keys unchanged) and signals `event_started/event_ended`. Encounter trigger gate = injected spawner processing; fog gate = `run_active` (real run only). `FogEventManager` = fog effects layer/API facade (public API unchanged).

### 1.9 Meta HUD (single source `docs/META_HUD_DESIGN.md`)
- Fullscreen FX CanvasLayer layer=1 (above world, below HUD→layer=2); `meta_health.gdshader` + `hint_screen_texture`.
- Pipeline: hit layer (CA + 6-tap radial blur) → directional ripple (edge 12%) → desaturate/cool tint + vignette → cracks (Voronoi baked once: windowed GPU 512² / headless CPU 64²).
- FSM: NORMAL/CAUTION/DAMAGED/CRITICAL/DYING (0.75/0.50/0.25/0.20); fast down (tau 0.10), slow up (tau 0.80 + stagger); DYING heartbeat 1.0–1.2Hz, breath ±1.5%, HUD shake ±2px, FOV −6%.
- Explicit (SegmentedBar) + implicit (desaturate/vignette/heartbeat) layers; `reduce_flash`: CA ×0.4, no breath/shake/heartbeat (SFX kept).
- Brightness proxy from registries (bullets ×0.002 + explosions ×0.15), zero GPU readback; LOD1 skips CA/blur/ripple.

### 1.10 Cinematics
- Intro (`docs/INTRO_CINEMATIC.md`): 6 shots 17.3s, 2.35:1 letterbox, `INTRO_SUB_1..6`; Welcome "New Game"; gate `current_scene == Main`; Esc/any key/click skip; tree paused, root `process_mode=Always`. Shots 1–6 done (P1–P3); **P4 leftover: low-spec retest + gamepad/mobile check (manual)**.
- Return (`docs/RETURN_HOME_CINEMATIC.md`): 7 shots 11.8s, mirrors intro; Esc via `SKIP_RETURN` (1.2s grace `effects.return_skip_grace`); both paths land on base UI (tree paused); BGM −40dB in shot 7.
- Shared factories: `CinematicFx.cs` (soft_glow/particles/shockwave/beam/radial_streaks; `speed_lines` removed 2026-08-03; zero heap alloc in drive `_process`), `DawnStation.cs`.

### 1.11 Tutorial
- Standalone `scenes/tutorial.tscn`, self-handles back (not BackNavigator). Aligned with run: `_ready` creates AimFrameLayer; stage 1 force-marked targets; stage 4 hold-H → gate → `begin_warp_in` → dock (hanger skipped). Isolates run state/saves; restore `Engine.time_scale = 1` on exit.

### 1.12 Exit/Back Navigation (single source `docs/EXIT_FLOW.md`)
- All back inputs → `BackNavigator.go_back()` via pure `decide_back_action()` (confirm → cinematic skip → settings/base/blocking/results → buff bar → pause → top → combat).
- Stack: L3 ExitConfirm → L2 overlays (Settings/Base/GameOver/Buff/cinematics) → L1 run (HUD⇄Pause + buff bar) → L0 Welcome (accounts entry).
- Battle exit: 2nd confirm (progress-loss warning); `_execute_exit_cleanup`: save profile, delete save in battle, stop SFX, fade quit.
- PC Esc / gamepad `ui_cancel` / Android back, one state machine.

### 1.13 Combat Fairness (values final here; impl/verification: `docs/archive/2026-08-03-combat-fairness-plan.md`)
- **Grace frames**: enemy bullet in Hitbox defers settlement `player.grace_period` (0.05s); out within window = no damage (kills ghost hits); only enemy-bullet→player timing; `take_damage` guards untouched.
- **Graze**: ring outside hitbox (`player.graze_radius` 20, gameplay-range family, no world_scale) → `player.graze_score` (10, × difficulty), once/bullet; no buff/talent links; hitbox area gives none.
- **Phase transitions**: P1→P2 & ENRAGE clear all bullets (incl. formation bombs) + brief invincibility (`boss.phases.transition_invincible` 1.0s, additive only); escape: no clear/invincibility. Boss bar segmented (`hud.boss_bar_segments` 3: P1 amber/P2 orange/ENRAGE red; boundaries = phase thresholds; drains left).
- **F parry**: 140° forward sector, 0.5s window (windup 0.15/recover 0.15); reflect = mirror y-flip ×2 speed ×1.5 dmg (rounded) as player bullet; hard cooldown 3.0s from effect end (3.8s cycle); all `player.parry.*` in balance.json; LT bound.

## 2. Technical Architecture

### 2.1 Stack
Godot 4.6.2 .NET (`GL Compatibility`); **full C# since 2026-08-08 (M1–M7d, zero GDScript)**; `scripts/tools/` offline Python (stdlib; sprite gens need PIL); assets PNG/WAV/NotoSansSC.ttf (OFL); no HDR bloom — emissive via ADD fake glow (`_glow()`); post FX via canvas_item shader + `hint_screen_texture`; only autoload `GameState`.

### 2.2 Main Node Tree (`scenes/main.tscn`)
```
Main (csharp/godot/Main.cs)
├─ Starfield / Camera2D ├─ Player ├─ Spawner
├─ BulletPool / EnemyPool
├─ HUD (layer=2) / BuffUI / PauseUI / SettingsUI / GameOverUI / BaseUI
├─ ExitConfirm ├─ BackNavigator ├─ MouseTrap
├─ VirtualControls (runtime _ready, touch input layer, `GameState.TouchControls` switch)
├─ MetaHealthFX (runtime _ready, layer=1) ├─ AimFrameLayer (runtime _ready, world)
├─ IntroCinematic / ReturnCinematic (layer=35, on-demand)
├─ OrbitalStrike (layer=24) ├─ MothershipSummonWindow (layer=24) + WarpGate (world)
└─ EliteTurretEvent / FormationStrikeEvent (registered to GameEventManager via `GameState.Events.RegisterEncounter()`, 2026-08-05)
```
**Convention**: all dynamic run entities under Main (clear logic + test traversal). Same-name behavior scripts in `csharp/godot/`. (Entry scene `scenes/welcome.tscn` is separate, not part of the Main tree — 2026-08-04 accounts.)

### 2.3 Duties & Services (A2 baseline)
- `GameState` facade: score/HP/buffs/difficulty/RP/tasks/routes/settings/signals; public API delegated.
- Seven non-autoload services (keeps "only autoload"): `BalanceService` (RefCounted: `load/cfg/enemy_hp_ramp/enemy_damage_ramp`), `SaveManager` (RefCounted: `exists/save/load/delete/quarantine/sanitize_num`; corruption → `last_was_corrupt`), `SfxPlayer` (Node child of GameState: `build_pool/play/stop_all`; headless short-circuit; `SFX_*` consts kept), `EntityManager` (RefCounted, 2026-08-05 evolved from EntityRegistry, `docs/ENTITY_MANAGER.md`: `Enemies/PlayerRef/PlayerHitbox/BulletPool/EnemyPool/AimFrameLayer/CameraRef/VirtualControls` + `BindEnemy`/`UnbindEnemy` one-line registration + `EntityRegistered`/`EntityUnregistered` signals + `ForEachEnemy`/`ClearEnemies`/`CountEnemies` bulk APIs), `FogEventManager` (fog effects layer/API facade, `GameState.FogEvents`, `docs/FOG_EVENTS.md`), `GameEventManager` (unified random-event manager, `GameState.Events`, `docs/EVENT_MANAGER.md`), `UserDB` (local account database: users/PBKDF2/per-user saves & settings/leaderboard, `GameState` forwards, 2026-08-06 audit registered 7th service, `docs/archive/2026-08-04-local-accounts-plan.md`).
- Hot paths: no per-frame `get_nodes_in_group`; use registries.

### 2.4 Pools & Registries
- Bullets: `GameState.bullet_pool.fire()`; Area2D, move in `_physics_process` (C04); faction in `setup()/activate()`; `activate()` resets homing/visual fields.
- Enemies: unified pool (2026-08-02, `920e5e9`) — waves/boss-3 minions/formations via `enemy_pool.spawn()` (`USE_POOL=false` → direct instantiation, A/B). `reactivate()/deactivate()` reset/register/emit death; don't assume pooled while `USE_POOL=false` exists.
- Guards: `_active` (deferred) + `_repooling` (wrap reparent vs `_exit_tree` mis-clear); never free/bypass pool objects externally.
- Explosions: `Explosion.spawn_at()`, pooled (`pool_cap`), `process_mode=Always` (plays under paused tree).

### 2.5 Input & Settings
- Inputs in `project.godot` (move/`boost`/`fine_move`/`dash`/`dock`/`homecoming`/`give_up`/`buff_panel`/`restart`/`parry` F); rebindable, in profile. **Gamepad runtime binding (P0-1)**: `_bind_joypad_defaults()` adds left stick, A/RB/LB/X/Y/L3/R3, LT (`parry`), right-stick aim (`aim_x`/`aim_y` → virtual cursor via `player.aim_point`); deadzone via `set_joy_deadzone()`. **PS detect** (GUID vendor 054c; ✕○□△/L1-R1 labels; `joy_button_label()`).
- Settings: difficulty/keybinds/locale/zoom/window/aim tier/`reduce_flash`/`mouse_lock` (default on; warps mouse inside while crosshair active + focused; released on pause/non-crosshair/unfocus)/gamepad (`joy_aim_speed`/`joy_deadzone`)/volume; locale via `set_locale()`, UI on `locale_changed`.
- Zoom & window size: two independent profiles.

### 2.6 Layers
| layer | content |
| --- | --- |
| 1 | MetaHealthFX |
| 2 | HUD |
| 12 | CommOverlay |
| 24 | OrbitalStrike / MothershipSummonWindow |
| 35 | Intro/Return Cinematic |
| 40 | ExitConfirm |

Pausing UIs `process_mode = Always` + `get_tree().paused`; BGM `loop_mode = LOOP_FORWARD` only (no stop in `_exit_tree`).

## 3. Global Invariants
> "Laws". Any change (fix/refactor/feature) must preserve them.

### 3.1 Collision & Damage
- Layers 1=`player` 2=`player_bullet` 3=`enemy`(+boss) 4=`enemy_bullet`. Player bullets vs `enemy` group; enemy bullets/entities vs `player_hitbox` group.
- Player hit only via `Player/Hitbox` Area2D (r=7 × `world_scale` → 2.8); body circle r=22 no collision use (mask=0).
- Damage via `take_damage(amount, from_pos := Vector2.INF)`; emits `player_damaged` (D8).

### 3.2 world_scale
- Only lever: top-level `world_scale` (**0.4**), cached `GameState.world_scale`.
- Hull-size family (sprite scale, collision radius, muzzle/dock/turret/tow offsets, per-hull fx): **design values** (1.0) in json/tscn/script fallbacks; `* world_scale` in `_ready()/setup()`.
- Gameplay-range family (AoE, lock/clear radius, slow ring) + indicators/cutscenes/UI: no scaling.
- **Idempotent assignment** (`radius = design × world_scale`), never `*=` (shared sub_resources compound); runtime-resized shapes need `resource_local_to_scene = true`.
- Exception: `mothership.DRIVE_MARGIN` × world_scale intentional (B11).

### 3.3 Viewport
- Camera (960, 540), zoom only; all edge/offscreen/spawn/visibility via `GameState.view_world_rect()`; **never hardcode 1920×1080/960/±1600**. `fight_anchor_y()` + hover bands follow. Cinematics in 1920×1080 design coords (intentional).

### 3.4 cfg
- `GameState.cfg("path.to.key", default)`; script fallbacks match json for missing/corrupt.
- **No per-frame cfg() on hot paths** — cache in `_ready()/setup()`.
- Tunables only in balance.json (`balance_editor.py`); after changes run `gen_balance_map.py`.

### 3.5 Trig & Hot Paths
- No direct sin/cos in `_physics_process` — `Enemy.sin_fast()/cos_fast()`.
- No per-frame allocation: preallocated point arrays + in-place writes (`points[i]=` value copies don't work), reuse `PackedVector2Array`, lazy-cache node refs.
- Cache hot fields in `_ready`; `Time.get_ticks_msec()` once per frame.

### 3.6 Coroutines
- **No `await get_tree().create_timer()` / timer-hung coroutines** (leak on exit with resources).
- Delays: one-shot `Timer` + `timeout` signal (`spawner._schedule()`); freed with tree.
- No `await process_frame` then unguarded access without `is_inside_tree()`.

### 3.7 Style & Typing
- Godot 4 style: Tab, typed, `CONSTANT_CASE`, `_` private, `signal.emit()/connect()`.
- `setup()` before `_ready()`; no `@onready` there — `$node/path`.
- New `class_name` → `--headless --import` to refresh cache.
- Typed: `Array[int]`, `EnemyPool`, `enemy: Enemy`; bare `Array`/`Node` converge (C18/C20 mostly done).
- **Exception (C19)**: `CONSTANT_CASE` vars for mutable script fallback defaults — data pattern, keep.

### 3.8 Signals & Lifecycle
- Connect via Callable; re-enter guarded by `is_connected`; `_exit_tree` disconnects/cleans.
- `get_parent().get_node("X")` chains null-check (`get_node_or_null`) or unique name `%X`; no per-frame string lookups.
- Pool `_exit_tree` clears GameState pool registrations.

### 3.9 i18n
- Text via `tr("UPPER_SNAKE_CASE_KEY")`; new keys in translations.csv zh+en, re-import; dynamic via `%d`/`%s`; locale via `set_locale()` + `locale_changed`.
- **No hardcoded Chinese user-visible strings** (C08/C26 cleaned).

### 3.10 Object Lifecycle
- Tutorial: isolate state/saves; restore `Engine.time_scale = 1` on exit.
- Keep refs to runtime-created nodes.
- Mothership/cinematic nodes: idempotent `skip()/abort()`, unified finish signals.

## 4. Data & Balance

### 4.1 balance.json
Sections (Tab canonical JSON, `balance_editor.py` + auto `.bak`): `version`/`world_scale`/`player`/`enemies`/`elites`/`boss`/`hud`/`spawner`/`mothership`/`buffs`/`milestones`/`base_task`/`progression`/`difficulty`/`effects`/`elite_turret_event`/`formation_strike_event`/`fog_events`/`tutorial`/`dda`.
- `difficulty`: score ×1/×2/×3, HP ×0.75/×1/×1.5, thresholds.
- `progression`: per_boss_kill / per_ten_minutes / time_step_seconds.
- `boss.phases`/`boss.enrage.type_*`/`boss.difficulty_scaling`.
- `effects.*`: starfield/shake/meta_health/mothership_summon/orbital_strike/explosion.

### 4.2 Access & Docs
- `cfg()` index + json/script dual-write reverse check: `docs/BALANCE_MAP.md` (**generated**; regenerate after key changes).
- Tune via `balance_editor.py`.

## 5. Testing Baseline
> Full commands: `docs/TESTING.md`. Not a unit framework; `[PASS]/[FAIL]` + exit code.
- Minimal: `--import`, `--quit-after 300`, `smoke_test.tscn`; + `base_system_test.tscn` for saves/base/mothership.
- Full: 55 assertion scenes (per CI run; list in `docs/TESTING.md`).
- `perf_bench` needs `--fixed-fps 1000`; `autoplay_test` long probe.
- Side effects: tests may touch `user://` saves; new tests `GameState.delete_save()` first + clean up; `balance_test` overwrites balance.json (corruption/fallback) then restores — no concurrent manual edits.
- Visual: windowed screenshots, human check; `visual/ui/return/intro/summon/meta_fx/hud` capture.

## 6. Persistence & Security
- Per-user run save `user://savegame_<user>_<sha256[:12]>.json` (owner-checked, `docs/archive/2026-08-04-local-accounts-plan.md`) + user table/settings/leaderboard `user://users.json` (UserDB) + `profile.json` only for not-logged-in/compatibility (merged into first registered user), versioned, GameState/UserDB-managed.
- Corruption → `<file>.corrupt` + `save_corrupt`/`profile_corrupt` flags; don't bypass recovery.
- Robustness: key_bindings type guard (C02); difficulty subkeys + `milestones.base` validation (C03); bool-safe reads (C16).
- No network/plugins/remote/secrets; `balance_editor.py` 127.0.0.1 only.
- Release: `export_presets.cfg` + `release.sh` → `builds/release/` (gitignored) via GitHub Releases; `packaging/` scripts.

## 7. Known Tech Debt
> Legend: ✅ fixed / ⚠️ partial / ❌ open. Full registry + efficacy: `docs/AUDIT_VAULT.md` (proprietary, never delete/merge).

### 7.1 Architecture (A-series)
| ID | Item | Status |
| --- | --- | --- |
| A3 | Boss attack match → registries; per-type → data-driven (2026-08-03) | ✅ 3 registries + param tables; new type = registration (O) |
| A4 | OCP: Player.cs buffs → declarative effect table (2026-08-03) | ✅ `BUFF_EFFECTS` (pow/cap/bool); new numeric buff = 1 row |
| A5 | DIP: Boss/events → Spawner via injection, not group lookup | ✅ injection landed (`bdb0274`); **2026-08-07 residual convergence**: mothership HUD refs → `_hud()` lazy cache (9 sites); remaining group lookups (welcome/pause_ui/event classes) judged reasonable pattern (R12 precedent), behavior zero-change |
| A8 | Player visual duties (trail/afterimage/crosshair/hit point/PlayerBuffVisuals) still in Player (~697 lines) | ✅ `PlayerVisuals` extracted 2026-08-03 (`PlayerVisuals.cs`, RefCounted composition): tail/afterimage pool/body tint/hitbox dot/parry visuals/graze flash delegated; `spawn_afterimage`/`engine_tint` public API kept; ~120 lines out of Player.cs |

### 7.2 Style/Perf
| ID | Item | Status |
| --- | --- | --- |
| C34 | `boss_pattern_test` reads instance constants; hardcoded assertions in difficulty/buff33/elite/formation kept as anchors | ⚠️ (partially design-confirmed) |
| spawn path | waves vs boss-3 minions two paths | ✅ unified 2026-08-02: waves via `EnemyPool.spawn()` (+ optional `p_bullet_type`); `USE_POOL` kept for A/B; smoke 142/pool_reuse 12/enemy_combat 33 PASS |

### 7.3 Phase Leftovers (ROADMAP Phase 0)
- ~~Dead code: `main.gd` unused refs, `hud.gd` false branch, zero-connect signals~~ ✅ verified covered/fixed: `_buff_ui` gone, `_tag_labels` in use, `ACTION_LABELS` removed, dead signals documented (E13), `toggle()` used by tests; `_start_release()` guard landed (I010).
- ~~`profile_corrupt` toast consumption~~ ✅ start panel shows profile-corrupt notice (`START_PROFILE_CORRUPT`).
- Cinematic stage 4 (low-spec/gamepad-mobile/README).

## 8. Future Directions
> Direction decisions: `docs/ROADMAP.md` (single source). Here: breakdown + landing points.

### 8.1 Near Term (debt finish, no new gameplay)
1. A3 ✅ + A4 ✅ landed (2026-08-03, 0 FAIL); **A8 ✅ landed 2026-08-03** (PlayerVisuals split, §7.1). Keep §3 invariants + 0 FAIL.
2. Spawn path unification ✅ (2026-08-02); `USE_POOL` kept.
3. Dead-code cleanup + guards (§7.3) ✅ 2026-08-03 (Phase 0 batch).

### 8.2 Mid Term
- Endless calibration: ✅ **done 2026-08-04** — `progression.*` + ramp factors tuned for deep runs (>15 min), recorded in `ENDLESS_BALANCE_PLAN §6.1`; verified via 3 × 900s autoplay probes (0 anomalies; no "HP-only inflation, zero pressure" steady state — HP min 40–69 sustained, 0 deaths).
- Cinematic stage 4.

### 8.3 Deferred/Cut (restart needs explicit decision; ROADMAP Phase 3)
Local accounts (spec at `7aacd3f`), standalone entry page (Appendix B), online leaderboard (decided no), collaboration/release engineering (done: CONTRIBUTING/CI/semver), content evolution (buffs/enemies/elites/boss/mothership — **mobile touch restarted & landed 2026-08-07**, `docs/archive/2026-08-07-deferred-restart-plan.md` §3).

### 8.4 Every Change Must
1. Preserve §3 invariants.
2. Tunables only in balance.json + `gen_balance_map.py` + minimal set.
3. New features register in §8 + `ROADMAP.md`; system docs carry specs.
4. 0 FAIL (55 assertion + autoplay; 权威计数 `docs/TESTING.md`); visual changes screenshot-checked.
5. Debt fixes backfill `AUDIT_VAULT.md`.

---
*Sole amendment baseline for design intent & architecture. Reviewer: per user instruction · Generated: 2026-08-01.*
