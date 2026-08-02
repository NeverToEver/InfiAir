# ⚠️ Code Audit Vault (AUDIT VAULT) — Non-removable

> **This document is a proprietary audit vault; deleting it or merging it into any other document is prohibited.**
> Logs all identified code-quality defects, fix guidance, post-fix disposition & effectiveness, and work time & scope.
> Append entries below whenever an audit finding is added/updated; backfill the "fix verification record" once the fix lands.
> This file is registered in `AGENTS.md` as a proprietary document (see AGENTS.md「文档同步要求」); its existence is protected by authoritative conventions.

---

## Audit Metadata (Registration Rules)

- **Non-removability**: `docs/AUDIT_VAULT.md` is registered in `AGENTS.md` as a proprietary document. No cleanup, refactor, or archival operation may delete it; any format adjustment must preserve every registered entry.
- **Entry structure**: each issue records "ID / severity / location / description / fix guidance / fix verification record / registration time & scope".
- **Fix verification record**: once a fix actually lands, backfill the entry with (a) what changed, (b) why it works (mechanism), (c) how effectiveness was verified (tests/runs).

---

# Round 1 Audit (SOLID Compliance, Core Gameplay Logic)

## Work Time & Scope

| Field | Value |
| --- | --- |
| Audit type | SOLID five-principle compliance audit of core gameplay-logic code |
| Work time | 2026-07-31 (single focused session; session started 2026-07-31) |
| Scope | `scripts/main.gd`, `autoload/game_state.gd`, `scripts/player.gd`, `scripts/spawner.gd`, `scripts/enemy.gd`, `scripts/boss.gd`, `scripts/bullet.gd`, `scripts/bullet_pool.gd`, `scripts/enemy_pool.gd` (9 files, ~4758 lines) |
| Method | Per-file read-through + cross-file dependency tracing (who writes whose fields, who calls whose interfaces) |
| Conclusion | Not compliant: S / O / D severely violated, L partially violated. 2 critical, 3 major, 3 moderate |
| Auditor | Claude Code (executed per user instructions) |

---

## 🔴 Critical (Severity 1)

### A1. Cross-class direct writes to private fields — encapsulation fully breached

- **Location**: see table below
- **Description**: several classes directly read/write other classes' underscore-prefixed "private" members (GDScript has no enforced private; `_` is only a convention). Any internal field change ripples to every caller at runtime/compile time, and errors are not type-safe.

| Caller | Write target | Evidence lines |
| --- | --- | --- |
| `main.gd` | `_player._input_locked` / `_player._invincible = 999.0` / `_player._fuel` / `_player.velocity` / `_player._dead` / `_player._die()` | `main.gd:360,361,429,431,353,460` |
| `boss.gd` | `p._enrage_slow = 1.0` / `p._dead` | `boss.gd:1357,1363,1355` |
| `bullet.gd` | `(area.get_parent() as Player)` hard cast to concrete type | `bullet.gd:227` |
| `bullet_pool.gd` | `b._pool` / `b._active` / `b._repooling` | `bullet_pool.gd:32,54,57` |
| `enemy_pool.gd` | `e._pool` / `e._active` / `e._repooling` | `enemy_pool.gd:27,51,54` |
| `main.gd` | `_spawner._elapsed` / `_spawner._event` | `main.gd:354,83` |

- **Why it matters**: if `player.gd`'s `_fuel` were renamed to `_fuel_ratio`, compile errors would simultaneously hit main, spawner, base_console and more. GDScript allows writing any field, and errors only surface at runtime — more dangerous than typed languages.
- **Fix guidance**:
  1. Add **public interface methods** (not open fields) to the penetrated classes: `Player.lock_input()/unlock_input()`, `Player.set_invincible(sec)`, `Player.set_fuel(v)`, `Player.die()`; `Boss.apply_enrage_slow(on: bool)`.
  2. Pools and objects switch to a public contract: the pool only calls `activate()/deactivate()/release()`; the object's `_exit_tree` cleanup is handled by the pool registering the `tree_exited` signal, not by writing `_pool` directly.
  3. `bullet.gd:227` replaces the hard cast with signals or a group contract: route `area.get_parent()` through the unified entry `Player.take_damage_public()`, or have the Hitbox carry the player reference.
  4. Replace incrementally; run `smoke_test` + `pool_reuse_test` + `enemy_combat_test` + `hit_logic_test` after each step.
- **Fix verification record**: ✅ Fixed (fully landed 2026-07-31)
  - **What changed**: added public interface methods to the 9 penetrated classes and replaced all production cross-class access. New interfaces — `Player`: `is_dead()/is_input_locked()/set_invincible()/lock_input()/unlock_input()/set_fuel()/fuel_amount()/die()/apply_enrage_slow()/set_auto_fire()/auto_fire_enabled()/is_dashing()`; `Spawner`: `set_elite_event()/set_formation_event()/set_elapsed()/elapsed()/set_boss_frozen()/set_waves_paused()/is_boss_active()/elite_event()/consume_boss_pending()/trigger_boss()`; `Boss`: `is_in_fight()/is_escaping()/abort_enrage_sequence()`; `Mothership`: `state()/mag_cells()`; `Main`: `is_intro_playing()/is_return_playing()/is_game_over()/is_homecoming()/mothership()`; `SettingsUI`: `capturing_action()`; `HUD`: `show_warning()`; `Bullet`/`Enemy`: `set_pool()/is_active()/set_repooling()` (+ `Bullet.despawn()`, `Enemy.is_exiting()`). The hard cast `(area.get_parent() as Player)` on player hits in `bullet.gd` switched to the `GameState.player_ref` registry reference. Penetrated `_` fields keep their original names (white-box test access unaffected; A7 handled separately).
  - **Why it works**: private state is no longer read/written externally — encapsulation restored; renaming any `_` field now only affects its own class instead of rippling to cross-class callers; pool/object coordination (`_repooling` false-clear protection) goes through public setters with unchanged semantics.
  - **How verified**: `--headless --import` passed; all 29 assertion test scenes `[PASS]` 0 failures (smoke/pool_reuse/hit_logic/enemy_combat/boss_enrage/tutorial/esc_navigation/mothership_summon/elite_turret_event/formation_strike_event/base_system/buff33/view_zoom/i18n/keybind/startup_flow/back_navigation/meta_health_fx/orbital_strike/boss_phase/boss_pattern/wave_pacing/buff_panel/buff_visuals/window_size/difficulty/balance/intro_cinematic/return_cinematic. Note: the original record omitted intro/return_cinematic and miscounted 27; unified to 29 on 2026-08-02). Note: `hit_logic` A21 (pre-existing failure baseline in AGENTS.md) passed this run; judged unrelated to this change, suspected test-order/environment variance, re-checked with no rollback — **2026-08-02 root cause identified: coincidental profile view-zoom tier (see "Pre-existing Failure Baseline Handling Record" below); root cause fixed**.
  - **Remaining**: tests still white-box access `_` private fields (assigned to A7, not mixed into this round); `intro/return_cinematic` internal `root._*` and `explosion.gd` same-class static access are legitimate same-class access, not A1.

---

### A2. GameState god object — 8+ responsibilities in one class

- **Location**: `autoload/game_state.gd` (975 lines)
- **Description**: a single class carries at least 8 unrelated systems: global state/signal bus, balance config center, SFX pool, run save + profile persistence, entity registry, RP economy/missions/talent tree, difficulty tiers/milestone curves, rebindable keys/locale/view/window/aim presets, HP/heal/lifesteal, run progression difficulty curve, buffs. Any change may affect everything.
- **Why it matters**: blast radius = the whole game. Tests hard to isolate (`balance_test` even temporarily overwrites `data/balance.json` itself). Directly causes A5 dependency inversion.
- **Fix guidance** (phased; each phase stays runnable):
  1. **Strip read operations**: move pure functions ("difficulty-tier queries / milestone curves / heal parameters") into a standalone `BalanceService` (holding the `_balance` dict); GameState delegates.
  2. **Strip persistence**: extract `save_run/load_run_data/apply_run_save/load_profile/save_profile` into `SaveManager` (keep the existing save format and corruption-quarantine flow unchanged).
  3. **Strip SFX**: extract `play_sfx/stop_all_sfx/SFX_*` into `SfxPlayer`.
  4. **Strip registry**: extract `enemies/player_ref/player_hitbox/pool references` into `EntityRegistry`.
  5. Run `base_system_test` + `smoke_test` + `startup_flow_test` after each phase.
- **Fix verification record**: ✅ All complete (2026-07-31, phases 1–4 all landed)
  - **Phase 1 done (balance read ops → `BalanceService`)**:
    - **What changed**: new `scripts/balance_service.gd` (`class_name BalanceService`, `RefCounted` composition class, not an autoload) — holds the `_balance` dict, implements `load()` (balance.json parsing), `cfg()` (path query + type tolerance), `enemy_hp_ramp()`/`enemy_damage_ramp()` (pure queries, difficulty multiplier as parameter). `game_state.gd` dropped the `_balance` field and parsing logic; `_load_balance()`/`cfg()`/the two ramps became one-line delegates; new public `has_balance()` query for tests/diagnostics. Composition instead of a new autoload, preserving the "single autoload: GameState" convention.
    - **Why it works**: config loading/queries/pure-value ramps moved out of the 975-line god object into a single-responsibility class; GameState's public API kept verbatim (delegated forwarding), zero changes to callers and remaining tests. Behavior byte-for-byte equivalent.
    - **How verified**: `--headless --import` passed; all 29 assertion scenes green, 0 FAIL; `balance_test`'s original `GameState._balance` white-box access switched to public `has_balance()` (also removing one A7 coupling); `gen_balance_map.py` regenerated the balance map, 0 missing keys, no mismatches.
  - **Phase 2 done (persistence → `SaveManager`)**:
    - **What changed**: new `scripts/save_manager.gd` (`RefCounted`) — `exists()/save()/load()/delete()/quarantine()/sanitize_num()`; `load()` has built-in corruption quarantine and sets `last_was_corrupt`. `game_state.gd` dropped `_quarantine` and all direct `FileAccess`/`DirAccess`; `save_run()/save_profile()` delegate to `save()`, `load_run_data()/load_profile()` delegate to `load()` (corruption flag drives `save_corrupt/profile_corrupt`), `has_save()/delete_save()/save_num()` delegate.
    - **Why it works**: file IO + JSON parsing + corruption quarantine extracted into a single-responsibility class; serialized-field assembly stays in GameState (state responsibility). `startup_flow` corrupted-save quarantine, `base_system` save/RP/missions/tree all green.
  - **Phase 3 done (SFX pool → `SfxPlayer`)**:
    - **What changed**: new `scripts/sfx_player.gd` (`extends Node`, child of GameState) — `build_pool()/play()/stop_all()`; headless short-circuit and pooled-reuse logic moved in. `game_state.gd` dropped `_sfx_players/_sfx_index`; `play_sfx()/stop_all_sfx()` delegate; `SFX_*` audio constants kept (player holds no concrete resources).
    - **Why it works**: SFX pool lifecycle extracted from the 975-line god object; play instances live under the SfxPlayer child node, tree position doesn't affect playback, behavior equivalent.
  - **Phase 4 done (entity registry → `EntityRegistry`)**:
    - **What changed**: new `scripts/entity_registry.gd` (`RefCounted`) — `enemies/player_ref/player_hitbox/bullet_pool/enemy_pool/aim_frame_layer/camera_ref` + `register_enemy()/unregister_enemy()`. `game_state.gd` dropped the original registry fields and add/remove methods, using **property setter/getter forwarding** (`GameState.enemies`, `GameState.player_ref = x` etc. — external syntax unchanged); add/remove delegate to `_registry`.
    - **Why it works**: hot-path cached data moved to an independent class, GameState thinned to a facade; `enemies` has no external writes so it's a read-only getter; the rest forward bidirectionally via getter+setter — zero changes to callers and tests.
  - **Overall verification (phases 2–4 together with phase 1)**: `--headless --import` passed; `--quit-after 300` no runtime errors; **29 assertion scenes all green, 0 FAIL**; direct `FileAccess/DirAccess` in `game_state.gd` zeroed; no residual references to removed symbols (`_sfx_players/_quarantine/_balance`).

---

## 🟠 Major (Severity 2)

### A3. boss.gd single 1475-line class — 3 bosses + attack library + enrage state machine + escape in one class

- **Location**: `scripts/boss.gd`
- **Description**: 3 bosses' differentiated movement, 20+ attack patterns, 5-substate enrage machine (TRANSITION/ACTIVE/RELEASE_HOLD/RETURN), phase-pattern table, escape, telegraph, slow fields, ramming, HP all in one class. `_execute_attack` centralizes a `match` over 9 attack branches (`boss.gd:695`).
- **Fix guidance**:
  1. Extract attacks as **data-driven attack objects/factory** (`BossAttack` interface: `begin()/update(delta)/end()`); the pattern table stores attack-constructor references; `_execute_attack` degrades to table-lookup instantiation.
  2. Extract the enrage sequence into a standalone state-machine class `EnrageSequence` (holds Boss reference + per-type behavior strategies); Boss delegates.
  3. Extract each boss type's movement into strategies (e.g. `BulwarkMove/DashMove/StrafeMove`); the `_update_movement` match degrades to table lookup.
- **Fix verification record**: ✅ Split landed (2026-07-31); O-principle achievement questionable (corrected 2026-08-01, see below)
  - **What changed**: the 1488-line single class split into "facade Boss + 4 responsibility classes" — `BossFire` (128 lines) bullet-pattern launcher; `BossAttacks` (356 lines) persistent-attack state machine + `execute()` dispatch (former `_execute_attack` match); `BossMovement` (79 lines) three-type movement strategies + P1 vertical press-down; `EnrageSequence` (362 lines) 5-substate enrage machine + per-type differentiated ACTIVE + orbit path + HP lock / player slow. `boss.gd` 1488 → 802 lines, keeping config loading, phase framework, HP/hit/escape, entrance, signals, public queries (fight_anchor_y/strafe_range/slow_factor/is_enraged/fight_phase/reset_fire_timer/spawn_minion_at).
  - **Why it works**: three-type movement, 20+ attacks, enrage state machine each got a single-responsibility class; `boss.gd` greatly shrank and became more readable; subclasses interact via injected BossFire/BossAttacks + boss public queries; residual scan confirmed no cross-class private access recurrence (A1 not regressed).
  - **⚠️ 2026-08-01 re-check correction (do not claim O-principle achieved based on this)**: the split is a genuine responsibility migration, but "centralized match dispatch replaced by table lookup/delegation (O principle)" overstates it. Fact check: the original `_execute_attack`'s 10-branch `match attack:` was **moved verbatim** into `BossAttacks.execute()` (neither table lookup nor factory instantiation); per-type branches remain in `BossMovement.update` (1 match) + `EnrageSequence` (4 per-type ifs) + `Boss` (summon, hit-stagger — 2 spots), **7 spots total** — adding a new type still requires editing 7 existing functions. This claim also directly contradicts **A4 "boss attack match / per-type nesting not fixed"**, which reaches the opposite conclusion for the same facts (both are really "the match is still there, just in another file") — vault self-consistency was broken; A4 in this table has been backfilled with the clarification.
  - **How verified**: `--headless --import` passed; `--quit-after 300` no runtime errors; 29 assertion scenes all green 0 FAIL (boss_phase 31 / boss_pattern 55 / boss_enrage 34 / enemy_combat 32 / hit_logic 60 / smoke 128 etc.); test white-box assertions switched to subclass public queries (`boss._enrage_seq.phase()` etc.).
  - **Remaining**: tests still white-box access `boss._attacks`/`boss._enrage_seq` component fields (assigned to A7); `Boss.SweepState`/`EnragePhase` enums still live on Boss for tests to reference.

---

### A4. Centralized match dispatch violates open-closed principle — adding types requires editing existing code

- **Location**: `enemy.gd:340` (8-branch strategy match), `boss.gd:695` (attack match), `boss.gd:1137` (per-type 3-way nesting), `player.gd:324` (inline buff branches), `spawner.gd:201` (two event-trigger inlines)
- **Description**: adding a movement strategy/attack/buff/event means editing existing functions' match or if branches. Editing existing code carries regression risk.
- **Fix guidance**: following the A3 pattern, extract strategies/attacks/events into registerable standalone objects; buff effects become a declarative effect table (buff id → effect object); Player iterates effect objects instead of per-buff ifs.
- **Fix verification record**: ⚠️ **Partially complete (backfilled 2026-08-01 from git history; status table had been left stale)**
  - **Completed sub-items (landed 2026-07-31)**:
    - A4a enemy movement strategies extracted into classes (`cea806e`): `EnemyMoveStrategy` base class + 8 strategy subclasses; `enemy.gd` `_physics_process`'s strategy match delegates to `_strategy.update()`; `_make_strategy()` keeps only a factory match (construction dispatch — acceptable).
    - A4b spawner event-trigger base class (`955f8a5`): `ScheduledEventTrigger` unifies elite/formation trigger strategies; the original spawner's two event inline branches delegate.
  - **Incomplete sub-items**: Boss attack match (now `BossAttacks.execute()` is still a 10-branch match — see A3 correction) and per-type branches (BossMovement/EnrageSequence/Boss, 7 residual spots); `player.gd` buff effects are still functional inline branches (`_refresh_buff_factors` + `pow(factor, GameState.buff_count())` family), not yet a declarative effect table.

---

### A5. Dependency-inversion violation — depending on concretes instead of abstractions

- **Location**: all core files
- **Description**:
  - All entities pull the global singleton directly: `GameState.buff_count()/enemies/player_ref` (Service Locator anti-pattern, no abstraction layer).
  - `bullet.gd:227` `(area.get_parent() as Player)` hard cast to concrete type.
  - `boss.gd:1010,1073` `get_tree().get_first_node_in_group("spawner")` does a group lookup per call to obtain its dependency.
  - Pools and objects call each other back through the private `_pool` field.
- **Fix guidance**:
  1. Separate the acceptable part: GameState as config center + signal bus + registry is an **intentional performance trade-off** (hot path avoids per-frame `get_nodes_in_group`); keep but narrow its interface.
  2. Event dependency injection: the Spawner reference Boss/events need is passed in by the injector at `_ready`/`setup`, avoiding group lookups.
  3. `bullet.gd`'s Player hard cast → signals or an interface.
- **Fix verification record**: ⚠️ **Partially complete (corrected 2026-08-02; previously mis-recorded as "unfixed")**
  - **Landed (2026-07-31 `bdb0274` "A5 dependency injection")**: Boss/elite-turret dependency on Spawner switched to injection — `boss.gd` gained `_spawner` + `set_spawner()`; `spawn_minion_at()`/`_summon_minions()` no longer call `get_first_node_in_group("spawner")`; `elite_turret_event.gd` replaced 3 group lookups the same way (guidance item 2); `bullet.gd`'s Player hard cast landed via A1's `GameState.player_ref` (guidance item 3); guidance item 1 — "GameState as config center + registry" — kept as an intentional performance trade-off.
  - **Not converged**: residual dependency points (`hud`/`pause_ui` etc. referencing Main) are still obtained indirectly via registry/group, not fully switched to explicit injection (see `DESIGN_BASELINE.md` §7.1).

---

## 🟡 Moderate (Severity 3)

### A6. L violation — Enemy vs Boss take_damage polymorphic contract mismatch

- **Location**: `enemy.gd:508` vs `boss.gd:1371`; caller `bullet.gd:216`
- **Description**: the two types' `take_damage` semantics differ (Enemy directly deducts and dies; Boss HP-locks / advances phases / enrages), forcing the caller into an `if area is Boss` special case — the polymorphic interface is incomplete; Boss and Enemy should not share the same call site.
- **Fix guidance**: make player-bullet hit resolution signal-driven ("hit event"), or define a `Hittable` contract (`take_damage(amount, score_scale)`) implemented by Boss/Enemy, removing the caller's type special-case; move the special case into each implementation.
- **Fix verification record**: ✅ **Fixed (2026-07-31 `68fea1e` "A6 semantic special-case"; vault status table left stale, backfilled 2026-08-01)**
  - **What changed**: `Enemy.is_boss()` semantic special-case + `Boss` override; `bullet.gd`'s hard type check `if area is Boss` in hit resolution became `if e.is_boss()` / `if explosive and not area.is_boss()` (semanticized exclusion of Boss from explosion AoE/splash).
  - **Why it works**: the caller no longer depends on concrete types (`Enemy`/`Boss` share the `is_boss()` query), matching the "move type special-case into each implementation" direction. Implemented via semantic branches rather than a full `Hittable` contract — a branch scheme the guidance permits.

---

### A7. Tests directly access private fields heavily — tests tightly coupled to implementation details

- **Location**: `test/*.gd` (many direct accesses of `_input_locked`, `_fire_timer`, `_elapsed` etc.)
- **Description**: tests directly depend on private implementation; the A1 refactor would break all tests at once; `AGENTS.md` currently registers 2 pre-existing failure baselines (`hit_logic_test` A21, `smoke_test` flake).
- **Fix guidance**: tests should prefer public interfaces/signals; where injection is genuinely needed, expose public test hooks via `@export`/`set()` (project already has `aim_point_override`, `_set_milestone_override` precedents).
- **Fix verification record**: ✅ All complete (2026-07-31)
  - **Cleaned (core classes)**: `Player` (added `enrage_slow/set_dead/set_dash_cooldown/reset_combat_state/fire/reset_fire_cooldown/boost_toggle_active/fine_toggle_active/aim_assist_params/hitbox_enabled/dash_cooldown/since_damage/set_boost_toggle/set_fine_toggle` etc.), `Boss` (added `enrage_sequence/attacks/fire_tool/set_fire_timer/patterns/set_pattern_index/start_pattern/base_modulate_color/set_survival/set_in_fight/escape_warned/begin_escape` etc.), `Spawner` (added `spawn_boss/spawn_enemy/wave_size/count_spread_enemies/current_interval/set_* timers` etc.), `Main` (added reference getters for `player/hud/base_ui/pause_ui/meta_fx/event/formation/strike/summon_window/intro/return_cinematic` + action methods `play_intro/skip_intro/play_return/skip_return/start_homecoming/summon_mothership/resume_from_base/stop_charging/continue_run` + `set_*` test hooks). Tests all switched to public interfaces (bulk sed + manual residual fixes); each class's tests green.
  - **Verification**: all 29 assertion scenes 0 FAIL; `--headless --import` clean; during the process fixed 2 illegal assignments produced by sed (`toggle_active() = `, `boss_pending() = `, `set_patterns({` parens) and 1 multiline dict parens issue.
  - **Remaining sub-batch also complete (same session continued)**: event classes (EliteTurretEvent/FormationStrikeEvent added `state()/lines()/turrets()/total()/line_stage()/comm()/crafts()/alive_count()/dropped()/cooldown_left()/set_cooldown_left()` etc.), UI classes (Hud node getters + `toggle_buff_panel()`, BuffSelect `pick_buff()/current_available()`, StartPanel `press_new_game()/press_continue()/dismiss()`, SettingsUI/PauseUI/BaseConsole public action methods), effects (MetaHealthFX `set_test_state()` unified smoothing-parameter test hook + `crack_progress()/hit_pulse()/damage_x()/state()/heart_rate()` getters), cinematics (Cinematic `set_shot_durations()/shot_index()/current_shot()/subtitle()`), misc (Mothership magazine/beam getters & setters, LaserWeapon/Enemy/Bullet/WarpGate/TurretBattery/AimFrameLayer/ExitConfirm/Bullet public queries). Tests all switched to public interfaces; **final 29 assertion scenes green 0 FAIL + autoplay 120s pseudo-live probe 0 anomalies**. During the process fixed multiple sed-produced illegal assignments/substring damage/`"_\1"` backreference escapes (e.g. `ms.state()_timer`, `fx.set_test_state({"_\\1": ...})`).
  - **A7 residual cleanup (2026-07-31 continued, triggered by autoplay audit)**: bulk sed wrongly replaced `en._active` with nonexistent `en.active()` (should be `is_active()`); the autoplay probe `_checks()` threw a runtime error every 500ms and aborted, **silently disabling registry two-way diff-set / registry_stale / player_ref / pool_ref monitoring**; also 3 residual direct `_state` reads (Mothership + two event classes, all having public `state()`). Full re-check cleaned 28 test-side private accesses (balance/boss_enrage/buff_panel/difficulty/elite_turret_event/formation_strike_event/hud_capture/keybind/meta_fx_capture/meta_health_fx_test/mothership_summon_test/pool_reuse_test/smoke_test/startup_flow_test/ui_capture/visual_capture) and 5 game-side cross-class private calls (BackNavigator `_on_back_pressed/_on_resume_pressed/_skip_intro/_skip_return` → `back()/resume()/skip_intro()/skip_return()`, Mothership `e._exiting` → `e.is_exiting()`). Added public interfaces: GameState `reload_balance()/set_milestone_override()/apply_key_bindings()`, MetaHealthFX `set_lod()`, SettingsUI `show_page()`, Mothership `start_release()`, StartPanel `press_settings()`, WelcomeScreen `reset_entry_shown()`, Enemy `summon_slow_timer()`, HUD `early_leave_box()/early_leave_fill()`, BulletPool/EnemyPool `free_count()`. **Verification: 29 assertion scenes green 0 FAIL + autoplay 60s 0 anomalies 0 stderr runtime errors (pre-fix same window had 59 exit-leak warnings)**.

---

### A8. Player is also a mini god object (506 lines, 9 responsibilities)

- **Location**: `scripts/player.gd`
- **Description**: movement, aiming/aim-assist, firing, Dash, fuel, hit mitigation, healing, trail/afterimage visuals, collision clearing all in one class. External code (main/boss) also writes its private fields directly (see A1).
- **Fix guidance**: extract hit mitigation (dodge/armor/lifesteal) and healing into a `DamagePipeline` effect component; Dash into a standalone component; visuals (trail/afterimage/crosshair/hit points) into `PlayerVisuals`.
- **Fix verification record**: ⚠️ **Partially complete (backfilled 2026-08-01 from git history; status table had been left stale)**
  - **Done (2026-07-31 `9174a52` "A8 Player responsibility split")**: hit/heal extracted into `PlayerDamage` component, dash into `PlayerDash` component (properties forwarded through Player, external API unchanged, no A1 penetration).
  - **Not done**: visual responsibilities (trail/afterimage/crosshair/hit points/`PlayerBuffVisuals`) still live on Player; Player still ~697 lines.

---

## Fix Status Overview

| ID | Severity | Status | Registered |
| --- | --- | --- | --- |
| A1 encapsulation breach | Critical | ✅ Fixed | 2026-07-31 |
| A2 god object | Critical | ✅ Fixed | 2026-07-31 |
| A3 boss single class | Major | ⚠️ Split landed, O principle not achieved (corrected 2026-08-01) | 2026-07-31 |
| A4 open-closed violation | Major | ⚠️ Partially complete (A4a/A4b landed; Boss branches and Player buff unaddressed) | 2026-07-31 |
| A5 dependency inversion | Major | ⚠️ Partially complete (DI landed `bdb0274`; GameState config center intentionally kept, corrected 2026-08-02) | 2026-07-31 |
| A6 L violation | Moderate | ✅ Fixed (is_boss semantic special-case, backfilled 2026-08-01) | 2026-07-31 |
| A7 test coupling | Moderate | ✅ Fixed | 2026-07-31 |
| A8 Player bloat | Moderate | ⚠️ Partially complete (PlayerDamage/PlayerDash extracted; visuals not) | 2026-07-31 |

> **Post-fix handling**: after any fix lands, update this table and backfill the entry's "fix verification record" — what changed, why it works, how it was verified (relevant test scenes: `smoke_test` / `base_system_test` / `pool_reuse_test` / `enemy_combat_test` / `hit_logic_test`).

## Pre-existing Failure Baseline Handling Record (A21: hit_logic_test "Player bullets can damage boss during entrance descent")

> This is not an A-series audit entry; it is the pre-existing failure baseline once registered in `docs/TESTING.md` (A21 assertion in PORTING_PARITY appendix A). Because the 2026-08-01 re-check wrongly judged it "self-healed" while the root cause remained, this record exists to prevent the same misjudgment recurring.

- **Registered**: 2026-07-31 (AGENTS.md "pre-existing failure baselines"). Description: `hit_logic_test` A21 assertion "player bullets can damage the boss during entrance descent" fails consistently.
- **2026-08-01 re-check misjudgment**: clean-HEAD rerun passed (hit_logic 20 assertions incl. A21), judged "suspected test-order/environment variance" and marked self-healed — **wrong conclusion**: it only passed because `user://profile.json`'s `view_zoom` happened to be medium/small tier at the time; the failure condition didn't reproduce, and the root cause was never investigated.
- **Root cause (located 2026-08-02)**: A21 placed the Boss and the player bullet at hardcoded absolute coordinates `(960, 100)`, assuming "still descending" there. With `view_zoom=large` (camera zoom 1.7) the visible top edge `view_world_rect().position.y = 222` puts that coordinate outside the visible area — the bullet's next-frame out-of-bounds check `view_world_rect(80)` despawns it via `_despawn()` before it ever collides with the boss, so `hp == max_hp` always holds → assertion fails consistently. Physics collision itself is view-zoom-independent; the failure came purely from test coordinates not adapted to the view-zoom tier.
- **Fix (2026-08-02, `test/hit_logic_test.gd`)**: A21 boss/bullet positions now computed dynamically from the fight anchor line `fight_anchor_y() - 75` (= view top edge + FIGHT_Y - 75 = view top edge + 155): still descending (< anchor line) and always within the `view_world_rect(80)` out-of-bounds check (FIGHT_Y=230, ample margin at any tier). A2's same-type hardcoded `y=150` also changed to `fight_anchor_y() - 80` (same fragile pattern: absolute coordinates depend on view-zoom tier / FIGHT_Y config).
- **Why it works**: the assertion no longer depends on the implicit premise "y=100 is inside the visible area on small tier"; under any view-zoom tier and any FIGHT_Y (>155), both "boss still descending" and "player bullet can hit" are directly guaranteed by the position formula, with no environment state involved.
- **How verified**: view-zoom × difficulty 9-combination matrix (small/medium/large × easy/medium/hard) hit_logic_test all 61 PASS 0 FAIL (large tier necessarily failed pre-fix); 5 consecutive stable runs on each of large/medium tiers; smoke_test 142 PASS, view_zoom_test 0 FAIL, boss_pattern/boss_phase/boss_enrage/enemy_combat/wave_pacing all green, no regressions.
- **Lesson learned**: once a failure baseline is registered, its root cause must be located and confirmed eliminated — a single clean-environment pass is not grounds to mark "self-healed"; shared persistent state like `user://` is a frequent source of test-order-dependent failures; assertions involving view/window/difficulty tiers must not hardcode absolute world coordinates.

## Document Consistency Alignment Record (2026-08-02)

> **Trigger**: user pointed out extremely low document health and demanded a thorough consistency alignment — "mark what's broken, mark what's done". A full cross-check of all 26 documents in `docs/` was performed (3-way parallel read-only verification: feature design docs vs code, plan docs vs git landing, internal vault consistency; core live docs ROADMAP/DESIGN_BASELINE/ARCHITECTURE/EXIT_FLOW/TESTING/README verified personally). Authority baseline: 31 assertion scenes / version 3.26 / measured commit timeline.

**Confirmed and fixed consistency issues (by category)**:

1. **Mis-recorded status (most severe)**: A5 dependency-inversion status — table/entry/ROADMAP/DESIGN_BASELINE all said "unfixed", but `bdb0274` (2026-07-31) had already landed Boss/elite-turret Spawner DI — unified correction to "⚠️ partially complete (injection landed; GameState config center intentionally kept)".
2. **Internal doc contradictions**: `DESIGN_BASELINE` §2.4 still said enemy "two paths coexist", §7.2 marked "pooling unified" (2026-08-02 performance plan) — §2.4 corrected to unified; `RETURN_HOME_CINEMATIC` §6 says 16.8s, §2/§7 say 11.8s; `INTRO_CINEMATIC` §4 letterbox 110px, §2 and tscn 132px; META_HUD doc 6-tap vs shader 4-tap (shader's own comment also stale, fixed along with it).
3. **Four coexisting assertion-scene counts (27/29/30/31)**: AUDIT_VAULT A1 "27"→29 (added missing intro/return_cinematic), D series two spots 29→30, F series 23→25, performance record "27"→31, ROADMAP/DESIGN_BASELINE 29/30→31, plan docs 30→31 etc.; kept counts correct for their own rounds (B/C/E series).
4. **Stale commit hashes**: `dcef9b6` (ROADMAP/DESIGN_BASELINE local-account spec) → `7aacd3f`; `b02be46`/`57c778b` (07-22 plan) → `4df9e02`/`7f0aa42`.
5. **G-series counts**: conclusion "2 P1, 9 P2" → "3 P1, 8 P2" (list and commits measured); batch 2 "P2×9"→"P2×8" (commit-message typo itself); D-series conclusion P3×6→P3×8, baseline 60 commits/195 files → 59/196/+14.6k.
6. **7 plan docs missing/incomplete completion marks**: 2026-07-30 ("not git committed" stale), 2026-08-01 C-series report (zero marks), 2026-08-02 D series, boss-p2 (all 20 checkboxes unchecked), E series, performance plan (added 920e5e9), core-logic (added 4 batch hashes) — all backfilled ✅ status and landing commits; mouse-lock F02 added `c48383f`.
7. **Stale numbers in feature design docs**: ELITE §1.2 enemy HP samples (55~130/80 → 48~112/65-72, elite 150-230→135-210); META_HUD uniform defaults (u_ripple_phase 0.0→1.0, u_crack_spread_min 0.10→0.15, u_crack_width 0.03→0.10), meta_fx_lod default 0→1; BOSS_REDESIGN B5 note appended "fixed".
8. **release.sh**: L4 comment default version 3.25→3.26.

**Unchanged (intentionally kept)**: per-round "point-in-time numbers" kept as historical snapshots (B/C/E/F series assertion-scene counts, plan-doc execution-time counts); "deprecated/superseded" old-behavior snapshots in design docs kept; line-number-anchored references generally drift as code evolves and were not updated line-by-line this round (function names are the source of truth; left for the next targeted maintenance).

**Verification**: `--headless --import` 0 errors (incl. shader-comment revision compiling); `smoke_test` 142 PASS 0 FAIL; `--quit-after 300` 0 errors; `bash -n release.sh` passed. Changed 20 docs + release.sh + shader comments + the A21 fix above: 21 files, +114/-82.

<!-- New findings area: subsequent audit rounds continue numbering here (B1, B2, ...) -->

---

# Round 2 Audit (2026-08-01 parallel gameplay-logic re-check + doc consistency alignment)

## Work Time & Scope

| Field | Value |
| --- | --- |
| Audit type | Re-check of major gameplay-logic issues and contradictions (6 parallel agents: run orchestration / player aiming / spawner & pools / Boss / event presentation / balance consistency) + doc-code consistency alignment |
| Work time | 2026-08-01 |
| Scope | All `scripts/` + `autoload/game_state.gd` + `data/balance.json` + 7 design docs |
| Conclusion | No critical runtime crashes; 2 real leak-class defects, 8 gameplay-impact defects, a batch of doc-code contradictions (aligned and registered). A2/P1-3/the five presentation systems judged to genuinely meet design goals; A3/A4/A8 partially complete (see A-series corrections) |

## Findings

| ID | Severity | Location | Description | Fix guidance |
| --- | --- | --- | --- | --- |
| B1 | Major | `enrage_sequence.gd:237` | Type-2 enrage `_aim_line`: each snap-stop point's `make_aim_line()` creates a Line2D stored in this class's field, with no `queue_free` anywhere in the file; `abort()`/RELEASE/RETURN never clean up, so residual static aim lines stay visible until boss release — ~6 Line2D leaked per type-2 enrage | Fold into a unified lifecycle at creation (reuse `BossAttacks.cancel_aim_line()` or hold in this class and clean at RELEASE/RETURN/abort) |
| B2 | Moderate | `main.gd:291-305,508-513,610-644` | Enrage bullet-time `Engine.time_scale=0.24` is never reset on the three end-state paths (homecoming / death / give-up); the homecoming cinematic plays at 4× slow-motion until orbital strike unpausing self-heals | Reset `time_scale = 1.0` at `_start_homecoming()`/`_on_player_died()`/`_give_up()` entries |
| B3 | Moderate | `spawner.gd:503-513` + `boss.gd:608-611` | Boss escape emits `escaped`+`died` simultaneously; `_on_boss_died` has no `is_escaped` guard, wrongly taking the kill path (advances `_next_boss_score` + grants a rest wave), violating the "escape doesn't advance rotation / no rest wave" contract | `_on_boss_died` checks `boss.is_escaped` first to skip kill-side settlement |
| B4 | Moderate | `bullet.gd:172-190` + `enemy.gd:329-338` | Homing bullets check only `is_instance_valid` for pooled-recycled targets (pool instances stay valid); after the target dies the bullet chases `(-500,-500)`, and after reuse it chases an unrelated new enemy | Invalidate on `not homing_target.is_active()` or registry-membership check |
| B5 | Moderate | `balance_service.gd:40-42` + `boss.gd:420-421,522-523` | `cfg()` returns a shared JSON reference for arrays; `FIRE_INTERVALS[i] *= interval_mult` pollutes the cache in place, compounding across bosses on easy/hard (BOSS_REDESIGN §8.2 fixed a same-type bug but missed this path) | `_apply_difficulty_scaling()` does `duplicate(true)` on `FIRE_INTERVALS` first (same as `_load_patterns`) |
| B6 | Moderate | `game_state.gd:72` | `world_scale` script fallback default 1/3 not raised to json's 0.4; with corrupted JSON the whole game's ship scaling/collision radii systematically misalign | Change fallback default to 0.4 (**already fixed this round, 2026-08-01**) |
| B7 | Moderate | `laser_weapon.gd:74-91` | Laser beam follows raw mouse, diverging from the magnetized/sticky crosshair `aim_point()` (P1-3 magnet-magnified deviation) | `_aim_dir()` uses `_player.aim_point()` |
| B8 | Moderate | `spawner.gd:393-399` | `_count_spread_enemies()` iterates the group (pooled enemies' `deactivate()` doesn't `remove_from_group`), so idle pooled instances inflate the spread on-screen cap → spread types degrade frequently | Iterate the `GameState.enemies` registry instead |
| B9 | Moderate | `boss.gd:252-257` vs `enemy.gd:124-131` | Boss HP ramp multiplies integrally vs enemy damped ramp (0.12/unit); late-game boss HP may be unkillable within the 50s escape window (mult≈5 → Boss-3 hard ≈9600 HP) | Confirm design intent or unify ramp semantics |
| B10 | Moderate | `formation_strike_event.gd:212-222` | BOMBING_RUN bomb cross actually lasts 1.1–1.8s (3/4/5 craft); design doc promises 2.6–3.8s (drop then leave) | Adjust bomb interval / extend cross segment into the design range (gameplay change, needs human decision) |
| B11 | Minor | `mothership.gd:134-136` | Mothership `drive.margin_*` multiplied by `world_scale` while same-family screen-edge values (strafe/hover_band/fight_y) are not — inconsistent categorization | Unify to not multiplying, or add a comment documenting the exception |
| B12 | Minor | `enemy.gd:139` | Enemy speed ramp coefficient 0.1 hardcoded with no json key (HP/damage ramps both have keys) | Add `enemies.speed_ramp_factor` key |
| B13 | Minor | `elite_turret_event.gd:151-165` / `formation_strike_event.gd:163-169` | Event `abort()` doesn't clear already-shown CommOverlay lines; residual lines persist after homecoming resumes | Clear/hide `_comm` inside `abort()` |
| B14 | Minor | `meta_health_fx.gd:188-194` | State boundary uses strict less-than; exactly 20% HP doesn't enter DYING (off by one tier; practically unreachable with continuous float values) | Change boundary to `<=` or document |
| B15 | Minor | `return_cinematic.gd:99-113` | `skip()`'s SKIP_GRACE (1.2s) input grace window also gates programmatic natural end; future cinematics under 1.2s would be permanently blocked | Separate natural end from input skip (`_finish()` independent of `skip()`) |
| B16 | Minor | `main.gd:610-614` | `_give_up()`'s death explosion is created on a paused tree and doesn't play (purely visual) | `_player.die()` before pausing, or mount the explosion with `process_mode=Always` |

## Consistency Alignment Record (doc-code contradictions fixed on 2026-08-01)

| Item | Correction |
| --- | --- |
| AUDIT_VAULT A-series status table | A3/A4/A6/A8 corrected per actual completion (above); ROADMAP/AGENTS synced |
| `docs/archive/2026-07-30-combat-ux-audit-plan.md` | P1-1 mark_ratio 0.4/40% → landed value 0.25 (body/fallback/target state); P0-3 "don't touch world_scale 1/3" gains a note on the 2026-07-31 raise-to-0.4 decision change |
| `docs/META_HUD_DESIGN.md` §7 | Crack-curve acceptance 0.11/0.33/**0.72/0.93** → **0.63/0.84** consistent with §4.2/code |
| `docs/EXIT_FLOW.md` | State-machine pseudocode method names → public interfaces (`main.skip_intro()/skip_return()`, `base_ui.resume()`, `settings_ui.back()`) |
| `AGENTS.md` | GameState description gains A2 composition services; formation event "doesn't pause waves" → "occupies a wave slot, pauses normal waves"; failure baselines (A21 / mothership-kill flake) marked passing |
| Code comments/fallbacks | `game_state.gd` world_scale fallback 1/3→0.4 (incl. comment); `formation_strike_event.gd` class comment; `smoke_test.gd` "40%"→"25%"; `main.gd` two misleading comments |
| `BOSS_REDESIGN.md` §8.2 | duplicate(true) self-decision point gains note that the FIRE_INTERVALS same path missed the copy (B5) |

## Fix Verification Records (2026-08-01 full fix; see that commit)

| ID | Status | What changed / why it works / verification |
| --- | --- | --- |
| B1 | ✅ Fixed | `EnrageSequence` gained `_free_aim_line()`; cleaned at create/launch/RELEASE_HOLD/abort four spots; `BossAttacks.cancel_aim_line()` only clears its own `_aim_line` and can't reach here. Verification: autoplay 60s 0 orphan nodes |
| B2 | ✅ Fixed | `main._reset_global_time_scale()` (clears bullet-time state + `Engine.time_scale=1`) hooked to `_on_player_died`/`_start_homecoming` (give-up covered via player_died) |
| B3 | ✅ Fixed | `boss.died.connect(_on_boss_died.bind(boss))` + `is_escaped` guard; during escape `collision_layer=0` so no "destroyed while escaping" ambiguity. enemy_combat_test "escape doesn't raise difficulty" keeps passing |
| B4 | ✅ Fixed | `Bullet._process` invalidation changed to `GameState.enemies.has(homing_target)` (active enemies are all registered; deactivate unregisters); can't use `is_active()` (directly instantiated enemies are always false). smoke homing segment rerun passed |
| B5 | ✅ Fixed | `FIRE_INTERVALS` `.duplicate()` after fetching from cfg, same as `_load_patterns`; boss_pattern_test easy/hard scenes pass |
| B6 | ✅ Fixed | `game_state.gd` world_scale fallback default 1/3→0.4 (included in the first commit) |
| B7 | ✅ Fixed | `laser_weapon._aim_dir`/trigger judgment uses `_player.aim_point()`; beam now points with the magnetized crosshair. buff33_test passes |
| B8 | ✅ Fixed | `_count_spread_enemies` iterates the `GameState.enemies` registry; idle pooled instances no longer inflate the spread cap. wave_pacing/enemy_combat pass |
| B9 | 🟦 Design confirmed | **Not a defect**: `enemy_hp_multiplier()` is actually the difficulty-tier multiplier (0.75/1/1.5); enemy HP = base × difficulty tier × damped ramp, no stacking; Boss linear scaling + the 50s escape pressure valve is ENDLESS_BALANCE_PLAN D1's explicit design. No code change |
| B10 | ✅ Fixed | `bomb_interval` 0.35→0.8 (json + script fallback); bomb segment 1.1/1.45/1.8s → 2.0/2.8/3.6s (3/4/5 craft); medium/hard land in the design band. formation_strike_event_test passes |
| B11 | 📄 Scope clarification | Comment added: DRIVE_MARGIN × ws is an intentional exception (ship-hull edge visually constant screen distance), categorized in the ship-offset family; no behavior change |
| B12 | ✅ Fixed | New `enemies.speed_ramp_factor=0.1` json key; `enemy.gd` reads via `cfg` (was hardcoded 0.1) |
| B13 | ✅ Fixed | `CommOverlay` gained `clear()`; elite-turret/formation event `abort()` calls it to clear lines |
| B14 | 🟦 Design confirmed | **Not a defect**: design §7 explicitly states "hp<20% enters DYING"; strict `ratio < t` is exactly that semantics; exactly 20% not entering DYING is correct. No code change |
| B15 | ✅ Fixed | `skip()` split into `_do_skip(bypass_grace)`; natural end `_advance` goes through `_do_skip(true)` bypassing the input grace window; future shortened total durations won't be wrongly blocked. return_cinematic_test passes |
| B16 | ✅ Fixed | `Explosion._init()` sets `process_mode=Always` — death explosions generated on a paused tree still play (covers both normal death `player_damage` and `_give_up` paths) |

> Post-fix regression: `--import` / `--quit-after 300` / **29 assertion scenes green 0 FAIL** / autoplay 60s 0 anomalies 0 orphan nodes.

---

# Round 3 Audit (2026-08-01 Godot best practices & syntax audit)

## Work Time & Scope

| Field | Value |
| --- | --- |
| Audit type | Godot 4.x best practices & GDScript syntax compliance (new dimension vs A-series SOLID / B-series gameplay logic) |
| Work time | 2026-08-01 |
| Scope | All 76 `scripts/` scripts + `autoload/game_state.gd` + `scenes/*.tscn` + `test/*.gd` + config/assets (~18k lines) |
| Method | 7-zone parallel audit (run orchestration / player weapons / spawner & enemies / Boss / event presentation / UI / tests) + lead cross-verification + verdict classification (`docs/AUDIT_REVIEW_SOP.md`) |
| Conclusion | No critical crashes; 2 major + 15 moderate + 18 minor, 35 total (production 28 / tests 7). Baseline `--import` and `--quit-after 300` all green; Godot 3.x residual APIs: 0 across the codebase |
| Auditor | Claude Code (executed per user instructions) |
| Full report | `docs/archive/2026-08-01-godot-best-practice-audit.md` |

## Findings (C series, registered for fix)

| ID | Severity | Location | Category | Description |
| --- | --- | --- | --- | --- |
| C01 | Major | `tutorial.gd:276,338` | Coroutine leak | Two `await create_timer` violate the AGENTS.md ban; scene switch mid-tutorial leaves coroutines suspended forever; `_advancing`/`_close_base` never run |
| C02 | Major | `game_state.gd:920` | Pure bug/robustness | `load_profile` key_bindings has no type guard; a hand-edited profile's typed assignment errors at runtime, returns early, and later fields don't load without setting corrupt |
| C03 | Moderate | `game_state.gd:96-105` | Pure bug/robustness | `_apply_balance` validates only top-level types; missing sub-keys/empty arrays cause KeyError/div-zero, violating the claimed corruption fallback |
| C04 | Moderate | `bullet.gd:171-208` | Lifecycle/physics | Area2D moves in `_process` instead of `_physics_process`; physics-step sampling misaligned, high-speed bullet tunneling risk |
| C05 | Moderate | `player.gd:533,538` | Performance/standards | `_physics_process` hot path calls `sin()` directly, violating the lookup-table convention; get_ticks_msec twice per frame |
| C06 | Moderate | `enemy.gd:391-401` | Performance | Per-frame per-enemy 9-key ctx Dictionary + 3 `view_world_rect()` calls, GC pressure at pool scale |
| C07 | Moderate | `starfield.gd:29-43` | Standards | Starfield bounds/wrap hardcode 1920×1080, violating the `view_world_rect()` convention |
| C08 | Moderate | `boss.gd:869` | i18n | Escape warning hardcodes Chinese bypassing tr(); shows Chinese in English environments |
| C09 | Moderate | `boss_movement.gd:45`/`boss_attacks.gd:108,240`/`enrage_sequence.gd:230` | Performance/standards | Four `_physics_process` direct sin() calls violate the lookup-table convention |
| C10 | Moderate | `enrage_sequence.gd:300-307` | Performance | `_path_center` builds a 5-element Array[Vector2] every frame; heap allocation throughout enrage ACTIVE |
| C11 | Moderate | `boss.gd:711-714`/`boss_movement.gd:38-47` | Pure bug | P1→P2 phase switch landing inside the press-down window leaves `_press_offset` residual; the hull stays permanently offset up to 80px below the anchor line |
| C12 | Moderate | `return_cinematic.gd:1357-1362` | Pure bug | Shot-7 push-in under `set_parallel` doesn't delay `tween_interval`; the close-up finishes before the character lies down |
| C13 | Moderate | `comm_overlay.gd:80-86` | Pure bug | Fade-out tween isn't killed by show_line/clear; a new line in the fade window gets pulled back to alpha=0 and hidden |
| C14 | Moderate | `main.gd:113,382`/`boss_attacks.gd:214,228,266`/`boss_movement.gd:66` | Standards | Hardcoded 960.0/±1600 world coordinates bypass `view_world_rect()` (same file 606 is correct) |
| C15 | Minor | `main.gd:419,425` | Lifecycle | `await process_frame` lacks an is_inside_tree guard; freed-before-first-frame → freed add_child |
| C16 | Minor | `game_state.gd:886-887,929-930` | Pure bug | `bool()` string-truthiness trap: hand-edited save strings "false"/"0" become true |
| C17 | Minor | `back_navigator.gd:22-31`/`welcome_screen.gd:33,101,121`/`pause_ui.gd:132` | Node safety | `get_parent().get_node("X")` chained sibling access without null checks, not using unique name % |
| C18 | Minor | `game_state.gd:67`/`spawner.gd:74`/`boss.gd:81-82`/`enemy.gd:72` | Typing | Bare Array/Node without element/concrete types |
| C19 | Minor | `main.gd:10-17`/`game_state.gd:31`/`hud.gd:46-53`/`tutorial.gd:10-11` widespread | Readability | CONSTANT_CASE naming for mutable vars (fallback-default pattern) conflicts with official conventions; judged a project data pattern, kept as-is |
| C20 | Minor | `player_buff_visuals.gd:51`/`bullet.gd:218-233`/`enemy_move_strategy.gd:122` | Typing | Weakly typed returns/params (bare Array, Area2D calling Enemy-only methods, Node2D accessing private members) |
| C21 | Minor | `bullet_pool.gd:11-12` | Lifecycle | `_ready` registers GameState.bullet_pool without `_exit_tree` clearing |
| C22 | Minor | `player.gd:693-697`/`camera_shake.gd:11` | Signals | `_exit_tree` doesn't disconnect signals / connect lacks is_connected guard → duplicate connects on tree re-entry |
| C23 | Minor | `laser_weapon.gd:76`/`boss_attacks.gd:106`/`enrage_sequence.gd:229`/`aim_frame_layer.gd:72` | Performance | Per-frame PackedVector2Array allocation / per-frame get_node_or_null |
| C24 | Minor | `boss_fire.gd:56,84`/`enemy.gd:453-455`/`mothership.gd:561` | Performance | `get_node("Polygon2D"/"MuzzleFlash")` string lookup per shot; cacheable |
| C25 | Minor | `main.gd:633-660,510,624` | Lifecycle | Homecoming/death/give-up end paths don't call `_stop_charging`; charge effect transiently lingers (auto-fixes) |
| C26 | Minor | `start_panel.gd:122,128,134`/`base_console.gd:373` | i18n | Hardcoded Chinese button labels/task format strings bypass tr() |
| C27 | Minor | `ui_chamfered_panel.gd:34`/`start_radar.gd:18` | Performance | `_process` polls and queue_redraws every frame even when hidden/adaptive |
| C28 | Minor | Presentation-class `_process` rebuilds point sets every frame (intro:1375/warp_gate:150-158/summon_window:303-314/orbital_strike:162-190/mothership:517-529) | Performance | Per-frame PackedVector2Array/closure allocation in short presentations |
| C29 | Moderate | `enemy_combat_test.gd:187` | Test standards | Direct read of `_exiting`; public `is_exiting()` already exists (A7 residual) |
| C30 | Moderate | `back_navigation_test.gd:128`/`keybind_test.gd:65,74` | Test standards | Directly calling `_notification`/`_unhandled_input` virtual callbacks bypasses public routing/input pipeline |
| C31 | Minor | `tutorial_test.gd:160` | Test standards | Directly calls private `_exit_tutorial()` |
| C32 | Minor | `base_system_test.gd:81` | Test standards | Directly calls `_init_missions()`; no clean public alternative |
| C33 | Moderate | `test/*.gd` ~120 spots | Test standards | Systematic `await create_timer` deviation from coroutine conventions (some correctly wrapped via `_wait_real`) |
| C34 | Minor | Multiple tests | Test standards | Hardcoded balance.json values (JSON changes drift without error); view_zoom_test:38 hardcodes 1920×1080 |
| C35 | Minor | `meta_health_fx_test.gd:66-154` | Test standards | set_test_state string keys write private fields directly; key names tightly coupled to implementation |

## Verdict Classification Record (2026-08-01)

| Item | Verdict | Reason |
| --- | --- | --- |
| C19 CONSTANT_CASE mutable vars | 🟦 Design confirmed | Project data pattern (AGENTS/CLAUDE explicitly document "script fallback defaults"); large-scale rename = low benefit, high risk; keep as-is |
| `buff_select.gd:157` child.free() | 🟦 Reasonable | stagger_open iterates children right after; queue_free would make old+new cards coexist in the same frame |
| `enemy.tscn` resource_local_to_scene | 🟦 Convention correct | Per AGENTS.md:204; no shared pollution |
| `mothership.gd` six group hud spots | 🟦 Not hot path | All event-driven / one-time cached |
| Component boss param untyped (boss_attacks.gd:70) | 🟦 Acceptable trade-off | Documented A1/A3 trade-off; optimizable, not required |
| Test create_timer | 🟦 Limited leak impact | Quits at teardown; still recommend converging (C33) |

## Fix Verification Records (2026-08-01 full fix; see that batch commit)

| ID | Status | What changed / why it works / verification |
| --- | --- | --- |
| C01 | ✅ Fixed | `tutorial.gd`'s two `await create_timer` → one-shot `Timer` node + `timeout` signal (process_mode=ALWAYS matching original SceneTreeTimer); scene switch mid-tutorial no longer leaves coroutines suspended. Verification: tutorial_test 0 FAIL |
| C02 | ✅ Fixed | `game_state.gd` load_profile key_bindings type guard — non-Dictionary / non-Array sub-values skip that field; no crash, no early return. Verification: startup_flow/base_system 0 FAIL |
| C03 | ✅ Fixed | `_apply_balance` validates difficulty-table easy/medium/hard sub-keys complete (`_valid_difficulty_defs`) + milestones.base non-empty array; corrupted JSON no longer KeyError/div-zero. Verification: balance_test 0 FAIL |
| C04 | ✅ Fixed | `bullet.gd` movement `_process`→`_physics_process`, activate/deactivate set_process→set_physics_process paired; view_zoom_test waits changed to physics_frame. Verification: pool_reuse/hit_logic/enemy_combat/smoke 0 FAIL |
| C05 | ✅ Fixed | `player.gd` invincible blink / hit-point pulse use `Enemy.sin_fast` lookup (`Time.get_ticks_msec()` cached as constant multiplier). Verification: --quit-after 300 no errors |
| C06 | ✅ Fixed | `enemy.gd` movement ctx → `_move_ctx` dict reuse (per-frame field updates only); main-path view reused until out-of-bounds destroy. Verification: enemy_combat/wave_pacing 0 FAIL |
| C07 | ✅ Fixed | `starfield.gd` star bounds/wrap use cached `view_world_rect().size`, no more hardcoded 1920×1080. Verification: view_zoom_test 0 FAIL |
| C08 | ✅ Fixed | `boss.gd` escape warning uses `tr("BOSS_ESCAPE_WARNING")` (zh/en added to translations.csv). Verification: i18n_test 0 FAIL |
| C09 | ✅ Fixed | Four boss-family `_physics_process` direct sin calls → `Enemy.sin_fast` (press-down/telegraph/hunt blink). Verification: boss_pattern/boss_enrage 0 FAIL |
| C10 | ✅ Fixed | `enrage_sequence._path_center` square path → `_square_corner` two-endpoint lerp; per-frame 5-element array eliminated. Verification: boss_enrage 0 FAIL |
| C11 | ✅ Fixed | `BossMovement.reset_press()` zeroes press-down offset on phase switch + `boss._enter_phase` calls it; boss_phase_test adds "hull back on anchor line after P2" assertion (4px tolerance). Verification: boss_phase 0 FAIL |
| C12 | ✅ Fixed | `return_cinematic` shot-7 push-in `set_parallel` → sequential tween + leading interval + `.parallel()` property group; close-up starts after the character lies down. Verification: return_cinematic 0 FAIL |
| C13 | ✅ Fixed | `comm_overlay` caches `_fade_tween`; show_line/clear kill it first; new lines no longer pulled invisible by residual fade-out. Verification: elite_turret/formation_strike 0 FAIL |
| C14 | ✅ Fixed | Hardcoded 960/±1600 world coordinates → `view_world_rect().get_center()` (main charge / dash warning line / strafe direction). Verification: view_zoom/boss_pattern 0 FAIL |
| C15 | ✅ Fixed | `main.gd` adds `is_inside_tree()` guard after await process_frame. Verification: --quit-after 300 + startup_flow 0 FAIL |
| C16 | ✅ Fixed | `game_state.gd` new `save_bool()` safe boolean read; 7 `bool(hand-edited value)` spots all replaced ("false"/"0" strings no longer misread as true). Verification: startup_flow/base_system 0 FAIL |
| C17 | ✅ Fixed | welcome_screen/pause_ui `get_parent().get_node` chains → `get_node_or_null` + null checks. Verification: back_navigation 0 FAIL. **2026-08-02 addendum (D29)**: the registered `back_navigator.gd:22-31` has 8 more bare `get_node("fixed sibling")` spots unchanged — targets are fixed children of main.tscn, low risk, judged a reasonable pattern, not fixed (vault review scope) |
| C18 | ✅ Fixed | milestone_base→Array[int], UNLOCK_SCORES→Array[int], STRAFE_SPEEDS→Array[float] (explicit cfg conversion), enemy._pool→EnemyPool. Verification: --import + full test suite |
| C19 | 🟦 Design confirmed | **Not a defect**: CONSTANT_CASE mutable vars are the project's "script fallback defaults" data pattern (documented in CLAUDE.md); large-scale rename = low benefit, high risk, keep as-is. No code change |
| C20 | ✅ Fixed | spread_pods()→Array[Node2D], bullet explode/splash `as Enemy`, EnemyMoveStrategy 8 update+4 reset params Node2D→Enemy. Verification: --import + full test suite |
| C21 | ✅ Fixed | bullet_pool/enemy_pool add `_exit_tree` clearing GameState global pool registration. Verification: pool_reuse 0 FAIL |
| C22 | ✅ Fixed | player `_exit_tree` explicitly disconnects GameState signals; camera_shake connect adds is_connected guard + `_exit_tree` disconnect. Verification: --quit-after 300 |
| C23 | ✅ Fixed | laser_weapon/boss_attacks/enrage_sequence per-frame PackedVector2Array → preallocated element writes; aim_frame_layer collision-radius meta cached. Verification: boss_pattern/boss_enrage 0 FAIL |
| C24 | ✅ Fixed | `Bullet.polygon_node()` lazy-load cached; boss_fire/enemy no get_node per bullet; mothership `_muzzles` array cached in order. Verification: boss_pattern/mothership_summon 0 FAIL |
| C25 | ✅ Fixed | main homecoming/death end paths add `_stop_charging`; charge effect no longer lingers. Verification: mothership_summon 0 FAIL |
| C26 | ✅ Fixed | start_panel button init `tr()` + base_console task format string → `BASE_MISSION_FMT`. Verification: i18n_test 0 FAIL |
| C27 | ✅ Fixed | ChamferedPanel/StartRadar `_process` adds `is_visible_in_tree()` early-exit. Verification: --quit-after 300 |
| C28 | ✅ Fixed | warp_gate rings/arcs + intro_cinematic structure lines + orbital_strike aim rings/missile trails + summon_window warp rings/trails — point sets preallocated at build, written in place per frame via `set_point_position` (zero allocation, line width doesn't change with scale). mothership `_live_targets` low-frequency function returning an array (~1 shot per 0.13-0.3s, not per frame) kept with a note. Also fixed the same-type regression from c526d79: `points[i]=` is a value-semantics copy that doesn't take effect, and `ring.scale=ONE*radius` inflates line width too — all switched to `set_point_position`. Verification: intro/return/mothership_summon/orbital_strike 0 FAIL |
| C29 | ✅ Fixed | enemy_combat_test `_exiting`→`is_exiting()`. Verification: enemy_combat 0 FAIL |
| C30 | ✅ Fixed | back_navigation_test `_notification`→`go_back()`; keybind_test `_unhandled_input`→`Input.parse_input_event`. Verification: back_navigation/keybind 0 FAIL |
| C31 | ✅ Fixed | tutorial_test `_exit_tutorial`→inject ui_cancel action. Verification: tutorial_test 0 FAIL |
| C32 | ✅ Fixed | base_system_test `_init_missions`→new public `reset_missions()`. Verification: base_system 0 FAIL |
| C33 | 📄 Verified no risk | Verified: all test critical paths that change `time_scale` already use `_wait_real` (create_timer 4-arg ignore_time_scale, boss_*/elite/formation); residual ~118 default-param create_timer all run in time_scale=1 segments (smoke/tutorial/enemy_combat/capture etc.), behavior correct. Judged style consistency rather than functional bug; mechanical replacement's regression risk exceeds benefit, not replaced one by one. |
| C34 | ⚠️ Partially complete | boss_pattern_test scenes 1/2/4 hardcoded bullet speed/damage (700/21/150/12/220) → read boss instance constants (CANNON_BULLET_SPEED/CANNON_DAMAGE/SWEEP_DROP_SPEED/SWEEP_DROP_DAMAGE/WALL_BULLET_SPEED); JSON changes no longer drift. Scenes 4/5's 420 (enemy.ENEMY_BULLET_SPEED, same value as VOLLEY) gain source comments. difficulty/buff33/elite/formation hardcodes judged logical-verification anchors, kept (reading config would reduce tests' independent value). Verification: boss_pattern_test 0 FAIL |
| C35 | ✅ Fixed | MetaHealthFX.set_test_state accepts `_`-prefix-free semantic keys (adds `_` internally when writing private fields); meta_health_fx_test keys all de-prefixed, no longer tightly coupled to implementation field names. Verification: meta_health_fx_test 0 FAIL |

> Post-fix regression: `--import` / `--quit-after 300` / **29 assertion scenes green 0 FAIL** / autoplay 120s probe.

---

# Round 4 Audit (2026-08-02 full review of recent large changes)

## Work Time & Scope

| Field | Value |
| --- | --- |
| Audit type | Full review of recent 60-commit large changes (entrance transition animation / UI uplift / Boss·event·presentation / aim-assist ballistics / balance consistency / doc-code-test triangle) |
| Work time | 2026-08-02 |
| Scope | Baseline `8c6dfff`→HEAD (59 commits, 196 files, +14.6k/-3.9k lines, unified per 2026-08-02 scope) covering `scripts/` + `autoload/` + `data/` + `docs/` + `test/` |
| Method | 6-zone parallel audit (`docs/AUDIT_REVIEW_SOP.md`) + lead cross-verification + empirical falsification (D03 Label mouse_filter default) |
| Conclusion | No P0/P1; P2×4 fixed + P2×1 doc registration (D05) + P3×9 fixed + P3×8 registered-not-fixed + 8 doc syncs; D03 false positive disproven; post-fix all 30 assertion scenes 0 FAIL |
| Auditor | Kimi Code CLI (executed per user instructions) |
| Full report | `docs/archive/2026-08-02-audit-fix-plan.md` (single source of truth for find-judge-fix tracking) |

## Findings (D series, registered for fix)

| ID | Severity | Location | Category | Description |
| --- | --- | --- | --- | --- |
| D01 | P2 | `spawner.gd:459-462,499,537` / `main.gd:652,695-699` | Pure bug / design goal unmet | Entrance animation's "enemy delay" only gates the spawner `_process` switch; pre-homecoming queued `_schedule` Timers and SpawnTelegraphs aren't cleared, so after continue, enemies/Boss with telegraphs enter during the entrance window (0~0.6s) |
| D02 | P2 | `balance.json:22` vs `player.gd:21` | Consistency | `player.entry.invincible` json 2.1 vs script fallback 1.65 — the only inconsistency in a full 363-key value-by-value cross-check; corrupted JSON shrinks the invincibility window by 0.45s |
| D03 | P2→disproven | `buff_select.gd:188` / `ui_theme.gd:50` | False positive | Claimed Label default STOP blocks card click hot zones — **empirically Godot 4.6 Label defaults `mouse_filter=IGNORE`, Containers default PASS**; clicks pass through text to the card, no blocking |
| D04 | P2 | `start_panel.gd:109` | Consistency | Difficulty buttons read `DIFFICULTY_DEFS["label"]` (data-driven Chinese) without `tr()`; after switching to en, HUD is English, buttons stay Chinese |
| D05 | P2 | `BOSS_REDESIGN.md §5.1-5.3` vs `boss_movement.gd:30-39` | Doc-code contradiction | P2-phase movement upgrades (type-1 strafe 200+vertical, type-2 dash 0.4/0.5s, type-3 strafe 100+vertical, type-3 P1 vertical sine) unimplemented; the gap predates A3 and was never registered. **Implemented per §5.5 on 2026-08-02 (see fix verification record)** |
| D06 | P3 | `player.gd:640-642` / `main.gd:707-711` | Pure bug (edge) | Pressing B homecoming on the entrance's first frame → input-lock freezes retreat, `_finish_entry` never runs, re-entry skipped by guard; long-press K self-destruct same origin (`_die` doesn't clear entrance state) |
| D07 | P3 | `test/entry_animation_test.gd:55-70` | Test fragility | `landed` criterion `y<=land_y+5` breaks early during the dive-in phase (t≈0.88); retreat assertion margin only ~20px; interrupted path not covered |
| D08 | P3 | `hud.gd:723` | Performance convention | vignette calls `GameState.max_health()` every frame (2 cfg JSON queries internally), violating hot-path caching convention |
| D09 | P3 | `back_navigator.gd:50` | Accessibility | CANCEL_EXIT only returns focus to the start panel; pause→quit→Esc leaves focus on a hidden confirm window |
| D10 | P3 | `spawner.gd:510` / `elite_turret_event.gd:139` | Consistency | Two hardcoded 960s (C14 converged the same family but missed these two) |
| D11 | P3 | `boss.gd:828-830` | Observation-level | Multiple tween competition white-flash during enrage HP lock (no leak) |
| D12 | P3 | `test/boss_pattern_test.gd:254` | Consistency | C34 exception: scene 3 `_bullets_by_speed(900.0)` hardcoded, not read from instance constants |
| D13 | P3 | `player.gd:65` / `bullet.gd:194` | Consistency/doc contradiction | `homing_time=4.0` is a dead parameter for player bullets (off-screen life ≈1.07s); comment "≈ bullet life" inaccurate |
| D14 | P3 | `player.gd:664,680` | Consistency | Entrance start point off-screen offset 90px, retreat horizontal speed 0.6 multiplier hardcoded, not in `player.entry` config |
| D15 | P3 | `aim_frame_layer.gd:139` / `player.gd:597` | Design trade-off | Magnet/sticky incremental per render frame; absolute strength scales with refresh rate (60Hz 480px/s vs 144Hz 1152px/s) |
| D16 | P3 | `player.gd:74-82` / `aim_frame_layer.gd:17-26` | Maintenance | Magnet/distance-decay params have duplicated defaults (currently identical values) |
| D17 | P3 | `orbital_strike.gd:186` / `mothership_summon_window.gd:271` | Code hygiene | Hit segment reads viewport size per frame, cacheable; flash decay framerate-dependent |
| D18 | P3 | `return_cinematic.gd` (14 play_sfx spots) | Consistency (pending) | Return SFX don't apply the 8-02 opening-cinematic unified -6dB/0.88 policy; return shots are already quieter by design — product judgment |
| D19 | P3 | `warp_gate.gd:157` etc. | Consistency | After C28, residual node-scale line-width changes (shrink animations; no regression-level inflation, visually reasonable) |
| D20 | P3 | `data/balance.json.bak` | Maintenance | bak lags multiple sections (missing aim_assist/entry) — recent value changes bypassed the editor, written straight to disk |
| D21 | P3 | `EXIT_FLOW.md:49` | Doc-code contradiction | Pseudocode comment still has leftover "（开始面板 / 欢迎页）" |
| D22 | P3 | `README.md:92` / `README.en.md:92` | Doc-code contradiction | Still describes "first-launch welcome page and 6-stage tutorial" |
| D23 | P3 | `AGENTS.md:104` | Doc-code contradiction | Profile field description still includes "欢迎页/" (welcome_seen removed) |
| D24 | P3 | `DESIGN_BASELINE.md:301` | Doc-code contradiction | §6 persistence has the same D23 wording |
| D25 | P3 | `DESIGN_BASELINE.md:7,292,361,9` | Stale doc | "29 assertion scenes" should be 30; "C-series 35 items fully fixed" conflicts with vault C34 partial / C19, C33 not-fixed |
| D26 | P3 | `ROADMAP.md:9` | Inconsistent scope | "A7 855 spots cleared" vs vault "28 test-side + 5 game-side" |
| D27 | P3 | `2026-08-01-remove-welcome-screen-plan.md` | Process leftover | 25 task checkboxes unchecked, no completion note, "29 assertions" stale |
| D28 | P3 | `translations.csv:103,213` | Consistency | Orphan keys `GO_SCORE` / `UI_KILLS_TAG` (zero references) |
| D29 | P3 | `AUDIT_VAULT.md:350` | Consistency | C17 registration includes back_navigator's 8 bare `get_node("fixed sibling")` spots; fix record only mentions welcome_screen/pause_ui; low risk |
| D30 | P3 | `spawner.gd:123-124` / `scheduled_event_trigger.gd:16` | Consistency | After A4b, elite/formation score-threshold semantics scattered across two spots (behavior correct) |

## Verdict Classification Record (2026-08-02)

| Item | Verdict | Reason |
| --- | --- | --- |
| D03 | 🟥 Disproven | Empirical (headless printed defaults): Godot 4.6 `Label.mouse_filter=IGNORE`, Containers `PASS` — clicks pass through text to the card, no blocking. No code change |
| D05 | ✅ Implemented | P2 movement upgrades were already missing in phase B (`git show 3188902^` line-by-line identical); **landed 2026-08-02 per `BOSS_REDESIGN.md §5.5`** (type-1/type-3 P2 strafe speed-up + vertical sine, type-3 P1 slow press-down/recover, type-2 P2 more frequent dashes); config in `balance.json boss.movement` (11 keys); ENRAGE movement unaffected |
| D11 | 🟦 Observation-level, not fixed | Multiple tween competition white-flash; no leak, no logic error |
| D15 | 🟦 Design trade-off registered | Framerate dependence is a structural choice; delta-normalizing is a feel refactor beyond this round's scope |
| D16 | 🟦 Not fixed | Duplicate defaults currently identical; commented division of labor; accepted at low cost |
| D18 | 🟦 Pending | Return-SFX scope registered in `RETURN_HOME_CINEMATIC.md §9`; unification is a product judgment |
| D19 | 🟦 Not fixed | Line-width change ≤4% (HOLD breathing); no regression-level inflation, visually reasonable |
| D20 | 🟦 Not fixed | bak is the editor's automatic backup artifact; auto-refreshes on next open/save |
| D29 | 🟦 Not fixed | main.tscn fixed-sibling node access low risk; note appended |
| D30 | 🟦 Not fixed | Behavior correct (can_trigger gates before tick) |

## Fix Verification Records (2026-08-02 full fix)

| ID | Status | What changed / why it works / verification |
| --- | --- | --- |
| D01 | ✅ Fixed | `spawner.gd` adds `_pending_timers`/`_pending_telegraphs` registration + `_on_pending_timer_fired` deregistration + `clear_pending()`; `_queue_enemy`/`_schedule` register; `main.gd` homecoming path calls `clear_pending()`. After continue, no enemies/Boss enter during the entrance window. Verification: entry_animation_test 13 PASS + smoke 142 PASS + full regression 0 FAIL |
| D02 | ✅ Fixed | `player.gd` fallback 1.65→2.1 with comment aligned (= dive-in 0.55 + retreat 1.1 + 0.45s buffer; buffer follows normal invincible-blink path). Verification: balance_test 28 PASS (corruption fallback path) |
| D03 | 🟥 Disproven, not fixed | See verdict classification: Label defaults IGNORE; the original mechanism claim doesn't hold |
| D04 | ✅ Fixed | `start_panel.gd` difficulty buttons use `tr("DIFF_"+String(d).to_upper())`; `_refresh_texts()` refreshes together (same scope as HUD difficulty_label). Verification: startup_flow_test 36 PASS + back_navigation_test 24 PASS |
| D05 | ✅ Fixed | `BOSS_REDESIGN §5.5` landed — `boss_movement.gd` adds `_move_bob` (P2 vertical sine, directly sets y; only called after `_in_fight`, entrance/escape/enrage early-exit doesn't interfere; `fight_anchor_y()` evaluated per frame supports view-tier switching) and `_move_band` (type-3 P1 slow press-down/recover; `_update_press` isomorphic target starts at 0, no jump); type-1/type-3 P2 strafe speed-up + sine, type-2 P2 dash 0.4/0.5, type-3 P1 press 200–280 range/9s; `boss.movement` 11 keys in balance.json + script fallback synced + BALANCE_MAP regenerated; ENRAGE movement unchanged. Fixed 2 self-introduced issues during implementation: `var target := boss.fight_anchor_y()+...` couldn't infer the type (boss is Variant; changed to explicit float); incremental application produced an initial jump (changed to directly set y / band starts at 0). Verification: boss_phase_test 37 PASS (32 original + 5 new: type-1 P2 sine oscillation/amplitude/strafe, type-3 P1 press/upper bound); boss_pattern 55 / boss_enrage 34 / smoke 142 / all 30 assertion scenes 0 FAIL |
| D06 | ✅ Fixed | `player.gd` adds `abort_entry()` (resets phase/restores auto_fire/kills tween) + `_entry_tween` member; `main.gd` homecoming calls `abort_entry()`; `_die()` calls `abort_entry()`. Homecoming/self-destruct on the entrance's first frame no longer leaves a stale phase. Verification: entry_animation_test 13 PASS |
| D07 | ✅ Fixed | `entry_animation_test` landed criterion → "8 consecutive frames in the anchor-line neighborhood" (excludes dive-in phase where y≥land_y always holds as false arrival); added 2 assertions "auto_fire paused during entrance / restored after". Verification: 13 PASS stable across consecutive runs |
| D08 | ✅ Fixed | `hud.gd` adds `_cached_max_hp`, refreshed at `_rebuild_buff_dock()` start (buffs_changed signal already connected; extra_life layer changes refresh); `_update_vignette`/`_on_health_changed` read the cache. Hot path avoids 2 cfg JSON queries. Verification: smoke 142 + buff_panel 16 PASS |
| D09 | ✅ Fixed | `back_navigator.gd` CANCEL_EXIT branch adds `_pause_ui.visible → grab_primary_focus()` (pause→quit→Esc returns focus to the pause panel). Verification: esc_navigation 11 + back_navigation 24 PASS |
| D10 | ✅ Fixed | `spawner.gd` Boss entrance anchor, `elite_turret_event.gd` carrier entrance anchor → `view_world_rect().get_center().x` (C14 convergence scope). Verification: elite_turret_event 57 + smoke 142 PASS |
| D11 | 🟦 Not fixed | See verdict classification |
| D12 | ✅ Fixed | `boss_pattern_test` scene 3 `_bullets_by_speed(900.0)`→`boss3.E2_SNIPER_SPEED`; C34 convergence completed. Verification: boss_pattern 55 PASS |
| D13 | ✅ Fixed | `player.gd` `HOMING_TIME` 4.0→1.2 (≈ off-screen life 1.07s) with comment fixed; `balance.json` `player.aim_assist.homing_time` synced 1.2. Verification: enemy_combat 32 + smoke 142 PASS |
| D14 | ✅ Fixed | Entrance hardcodes into config: new `player.entry.spawn_clearance=90` / `rush_hspeed_ratio=0.6` (json + script fallback synced); `gen_balance_map.py` rerun, bidirectional lookup clean. Verification: entry_animation_test 13 PASS |
| D15 | 🟦 Not fixed | See verdict classification |
| D16 | 🟦 Not fixed | See verdict classification |
| D17 | ✅ Fixed | `orbital_strike.gd` viewport size cached `_screen` at `_ready`, hit segment reuses; `mothership_summon_window.gd` `_update(t, delta)` uses the delta param instead of `get_process_delta_time()`. Verification: orbital_strike 15 + mothership_summon 28 PASS |
| D18 | 📄 Registered | `RETURN_HOME_CINEMATIC.md §9` audio-scope note appended (keeps each shot's existing lower values; unification would require code changes + writing back here) |
| D19 | 🟦 Not fixed | See verdict classification |
| D20 | 🟦 Not fixed | See verdict classification |
| D21 | ✅ Fixed | `EXIT_FLOW.md:49` pseudocode removes " / 欢迎页" |
| D22 | ✅ Fixed | `README.md` / `README.en.md:92` welcome-page description → "launch goes straight to main menu; first entry has a 6-stage tutorial" |
| D23 | ✅ Fixed | `AGENTS.md:104` profile fields remove "欢迎页/" (tutorial state kept; `tutorial_done` still in use) |
| D24 | ✅ Fixed | `DESIGN_BASELINE.md:301` §6 removes "欢迎页/" likewise |
| D25 | ✅ Fixed | `DESIGN_BASELINE.md` three spots 29→30 + "fully fixed"→"handled & closed" (aligned with vault C34/C19/C33 reality); snapshot date updated 2026-08-02 |
| D26 | ✅ Fixed | `ROADMAP.md:9` A7 scope unified (vault scope 28+5; the 855 note was a sed-replacement count) |
| D27 | ✅ Fixed | Welcome-page-removal plan doc: all 25 checkboxes checked + header completion note (2026-08-02, commit 2c16892) + "29 assertions" corrected |
| D28 | ✅ Fixed | `translations.csv` deleted two orphan keys `GO_SCORE` / `UI_KILLS_TAG`. Verification: i18n_test 9 PASS |
| D29 | 🟦 Not fixed | See verdict classification (C17 entry annotated that back_navigator is a reasonable pattern) |
| D30 | 🟦 Not fixed | See verdict classification |

> Post-fix regression: `--import` / `--quit-after 300` / **30 assertion scenes green 0 FAIL** / perf_bench rc=0 / autoplay probe fully run (480s, 3 runs, 0 deaths, 0 orphans, peak frame cost 7.43ms) — 1 occasional `score_stagnant` (score stagnation during boss focus + escape-window race; this run had 0 homecomings, no intersection with D-series change paths; judged a pre-existing probe flake, not introduced here).

### E series (2026-08-02 supplementary review of uncovered legacy areas — registered only, not fixed)

> Supplementary review of legacy blind spots D-series didn't treat as primary subjects (enemy system / presentation·effects·mothership / system services·misc, 28 scripts), 3-way parallel + lead verification. **Per user instruction, registered only, not fixed**; recommendations for later decisions; full report in `docs/archive/2026-08-02-audit-fix-plan.md` §4.

| ID | Severity | Location | Category | Description | Recommendation |
| --- | --- | --- | --- | --- | --- |
| E01 | P1 | `bullet.gd:240-246` | Pure bug (C20 silent regression) | Mothership splash `_splash()` `as Enemy` cast fails for Boss (registry contains Boss; Boss isn't an Enemy subclass), splash damage silently lost; `_explode` Boss exclusion is intentional design | **Fix** |
| E02 | P2 | `start_panel.gd:275`/`tutorial.gd:97` | Pure bug (player-harming) | Tutorial button not disabled after completion; re-entering tutorial calls `delete_save()` silently deleting an in-progress run | **Fix (highest priority)** |
| E03 | P2 | `game_state.gd:118-125` | Design goal unmet (C03 half-plugged) | Difficulty table only validates the three tiers are Dictionaries, not their sub-keys; partial corruption → KeyError → 0 HP/0 score | **Fix** |
| E04 | P2 | `dawn_station.gd:282-286`/`return_cinematic.gd:568,702` | Design goal unmet/consistency | PHANTOM breathing tween overrides callers' `modulate.a` (0.35/0.5 raised to 0.85-1.0); base_console works around it with a wrapper node — inconsistent usage | **Fix** |
| E05 | P2 | `mothership.gd:414-432` | Pure bug (edge) | Forced undock while H held doesn't clear the HUD early-undock progress bar; residual visible | **Fix** |
| E06 | P2 | `enemy.gd:469` | Consistency (D10 not converged) | Side exit `position.x < 960.0` hardcoded | **Fix (one line)** |
| E07 | P3 | `bullet.gd:230` | Doc-code contradiction | `_explode` C20 comment "registry is all Enemy" premise wrong (same origin as E01) | **Fix (with E01)** |
| E08 | P3 | `laser_weapon.gd:66-67` | Pure bug (unreachable) | Buff-zero early-return freezes an active beam; no buff-removal mechanism currently | Pending (cheap safety) |
| E09 | P3 | `laser_weapon.gd:13` | Consistency (pending) | `BEAM_HALF_WIDTH` not multiplied by ws (`ENEMY_HIT_RADIUS` is) | Pending |
| E10 | P3 | `game_state.gd:947` | Consistency (low risk) | `locale` bypasses `set_locale()` guard; hand-edited values leave state inconsistent | Pending |
| E11 | P3 | `game_state.gd:958-959` | Consistency (C02 element-level gap) | key_bindings array elements `int(k)` untyped-checked | Pending |
| E12 | P3 | `save_manager.gd:21-28` | Consistency (legacy) | `save()` non-atomic write; crash mid-write truncates JSON losing progress (self-heals to .corrupt) | Pending |
| E13 | P3 | `player_damage.gd:64-69` | Hot-path edge | `heal_tick()` nested dict queries every physics frame | Pending |
| E14 | P3 | `mothership.gd:171-174` | Consistency | `beam_pts[i] *= ws` literally violates idempotency convention (currently safe: not a shared sub_resource) | Not fixed (note as safe) |
| E15 | P3 | `enemy.gd:385` | Minor performance | Per-frame `buff_count(&"slow_field")` dict get | Not fixed (registered for reference) |

## E Series Fix Verification Records (2026-08-02 full disposition)

> All landed per registered recommendations; fix batches see `docs/archive/2026-08-02-e-series-fix-plan.md` (single source of truth for find-judge-fix tracking).

| ID | Status | What changed / why it works / verification |
| --- | --- | --- |
| E01 | ✅ Fixed | `bullet.gd _splash()`'s `as Enemy` → Variant duck call `take_damage(amount, score_scale)` — registry contains Boss (not an Enemy subclass), `as Enemy` cast yields null, so splash 20 damage was silently lost; same pattern as `laser_weapon._damage_tick` "includes Boss". `_explode()` Boss exclusion unchanged by design. Verification: hit_logic_test adds "splash deals 20 damage to Boss" assertion PASS |
| E02 | ✅ Fixed | `start_panel.gd` tutorial button `disabled` after completion + `_on_tutorial_pressed()` adds `tutorial_done` guard — re-entering tutorial triggered `tutorial._ready`'s unconditional `delete_save()`, silently deleting an in-progress run. Verification: startup_flow_test adds "button disabled after completion" assertion PASS |
| E03 | ✅ Fixed | `game_state.gd _valid_difficulty_defs` adds `DIFFICULTY_DEF_KEYS` 8 numeric keys existence+type validation — missing sub-keys previously let downstream 8 `DIFFICULTY_DEFS[difficulty][...]` KeyError→0 (enemy 0 HP instant-death / score multiplier 0). Verification: balance_test adds "missing difficulty sub-key falls back to defaults" assertion PASS |
| E04 | ✅ Fixed | `dawn_station.gd` PHANTOM visuals all mounted on a `BreatheRoot` breathing container; 4s slow breathing writes container `modulate:a` instead of the station body — callers pressing `station.modulate.a` (return_cinematic 0.35/0.5) no longer raised 2.5~3×, unified with base_console wrapper usage. Verification: return_cinematic/intro_cinematic/base_system 0 FAIL |
| E05 | ✅ Fixed | `mothership.start_release()` entry uniformly clears the HUD early-undock progress bar — H-held forced undock (warning expiry/magazine depleted) left a residual visible bar. Verification: mothership_summon_test adds "visible before + cleared by start_release" assertion PASS |
| E06 | ✅ Fixed | `enemy.gd` side exit `position.x < 960.0` → `view_world_rect().get_center().x` (same convergence as D10). Verification: enemy_combat/wave_pacing 0 FAIL |
| E07 | ✅ Fixed | `bullet.gd _explode()` comment corrected — registry contains Enemy and Boss; Boss excluded via `as Enemy` null is intentional design (not "registry is all Enemy"). Verified with E01 |
| E08 | ✅ Fixed | `laser_weapon.gd` buff-zero early-return now `if _active: _end_beam()` first — prevents a future buff-removal mechanism from freezing an active beam / jamming autofire. Verification: buff33/smoke 0 FAIL |
| E09 | 🟦 Registered, not fixed | Multiplying `BEAM_HALF_WIDTH` by ws (0.4) shrinks hit from 26→10.4px, significantly weakening laser hits — a gameplay change needing product judgment; current visuals acceptable |
| E10 | ✅ Fixed | `game_state.gd load_profile` locale guarded by zh/en whitelist — hand-edited illegal values keep default zh, consistent with TranslationServer state; avoids calling `set_locale` (no load-time `save_profile`/`locale_changed` side effects). Verification: startup_flow/base_system 0 FAIL |
| E11 | ✅ Fixed | `game_state.gd load_profile` key_bindings array elements int/float type-checked — hand-edited string keycodes skip directly, no more `int()` conversion error spam (C02 outer guard's element-level completion). Verification: startup_flow/base_system 0 FAIL |
| E12 | ✅ Fixed | `save_manager.gd save()` writes a `.tmp` file first then replaces the real file (atomic write; prevents truncated-JSON progress loss on crash mid-write); worst case (crash between delete-old and rename) = missing main file → `load()` returns {} no save, no corrupt flag — better than current (truncated → quarantined .corrupt → progress lost + corruption popup). Verification: base_system/startup_flow 0 FAIL |
| E13 | 🟦 Registered, not fixed | Caching passive-heal params conflicts with "difficulty switchable mid-run" semantics (cache would need a signal-refresh chain on switch); ultra-low-risk scope |
| E14 | 🟦 Registered, not fixed | `beam_pts[i] *= ws` currently safe (polygon is an in-node inline property, not a shared sub_resource) |
| E15 | 🟦 Registered, not fixed | Per-frame `buff_count(&"slow_field")` dict get allocates nothing; negligible overhead |

> Post-fix regression: `--headless --import` / `--quit-after 300` 0 errors / **all 30 assertion scenes 0 FAIL** (incl. new E01/E02/E03/E05 assertions).

### F series (2026-08-02 mouse leaving window freezes crosshair — registered + fixed)

> When the mouse leaves the game window mid-run, Godot stops dispatching mouse-motion events; `get_global_mouse_position()` freezes at the last position — the crosshair sticks at the screen edge and jumps on re-entry; previous attempts didn't fully solve it. This round adds a "lock mouse inside window" setting (`mouse_lock`, default on, persisted in profile), eliminating the out-of-window premise at the root. Execution-plan archive: `docs/archive/2026-08-02-mouse-lock-plan.md`.

| ID | Severity | Location | Category | Description | Recommendation |
| --- | --- | --- | --- | --- | --- |
| F01 | P2 | `player.gd:585-605` (`aim_point()`) | Pure bug (input edge) | After the mouse leaves the window `get_global_mouse_position()` freezes, crosshair sticks at the edge; the smooth increment `raw - _aim_last_raw` jumps on re-entry. Earlier fix wasn't complete | **Fix** |

## F Series Fix Verification Records (2026-08-02)

| ID | Status | What changed / why it works / verification |
| --- | --- | --- |
| F01 | ✅ Fixed | New `mouse_lock` setting (default on, profile-persisted) + `scripts/mouse_trap.gd` (mounted on Main, `PROCESS_MODE_ALWAYS`): while the window is focused, the `mouse_exited` signal fires and `Input.warp_mouse()` pulls the mouse back 1px inside the edge (`_process` per-frame defensive fallback); unfocused = released, doesn't block app switching. Eliminates the "mouse out of window → position frozen" premise at the root; `aim_point()`/`AimCrosshair` logic untouched. Settings page「显示/Display」toggle + bilingual description. Verification: mouse_lock_test 13 assertions 0 FAIL + full assertion-scene regression 0 FAIL (`warp` window-event behavior needs real-machine acceptance) |

> Post-fix regression: `--headless --import` / `--quit-after 300` 0 errors / **all 31 assertion scenes 0 FAIL** (incl. new mouse_lock_test).

### F02 (2026-08-02 confine not released on pause — defect introduced by F01 fix, registered + fixed)

> Post-F01 field feedback: while paused, MouseTrap still confines under `PROCESS_MODE_ALWAYS`, trapping the mouse inside the window's content area — can't reach the system title-bar close button to exit the game. Design flaw: confine applied too broadly (all focused states); it should be limited to the "in-run crosshair state".

| ID | Severity | Location | Category | Description | Recommendation |
| --- | --- | --- | --- | --- | --- |
| F02 | P2 | `scripts/mouse_trap.gd:48-56` (`_trap_active`) | Pure bug (interaction blocking) | Paused states (pause/buff/base/settlement) still confine the mouse; can't move it out of the window to click the system title-bar close button to exit | **Fix** |

## F02 Fix Verification Record (2026-08-02)

| ID | Status | What changed / why it works / verification |
| --- | --- | --- |
| F02 | ✅ Fixed | `_trap_active()` adds two release conditions — "not paused" + "system cursor hidden (crosshair state)" (`AimCrosshair` restores the system cursor in pause/non-crosshair states; the two conditions corroborate each other, independent of processing order), and extracts a `_trap_enabled()` static pure function for assertions. Confine is now limited to in-run crosshair active + window focused: after pausing the mouse moves freely out of the window (title-bar × to exit unobstructed); in-run crosshair state pulls back as usual. Verification: mouse_lock_test adds 7 release-decision assertions (23 all green 0 FAIL) + smoke_test 0 FAIL |

> **F02 verification note (2026-08-02, does warp cause crosshair jitter?)**: conclusion — **no**. The warp target always uses `_last_known_pos` (last in-window position before exiting, frozen after exit), displacement ≤1-2px; while the mouse is outside the window `get_global_mouse_position()` is already frozen at the last internal position, so post-warp reads are continuous and `aim_point()`'s smooth increment ≈0. Warp actually clamps the "tens-of-px position jump on re-entry" inside the edge. The only edge cases are single 1px pull-backs at left/top edge column/row 0 (right/bottom last column is inside the clamp range, no trigger). Verification: mouse_lock_test adds 2 "warp displacement ≤2px" assertions (25 all green 0 FAIL).

> Post-fix regression: `--headless --import` / `--quit-after 300` 0 errors / mouse_lock_test 25 assertions 0 FAIL (13 base + F02 7 release decisions + 2 warp-displacement checks, unified per 2026-08-02 scope) / smoke_test 0 FAIL.

# Round 5 Audit (2026-08-02 full review of core logic)

## Work Time & Scope

| Field | Value |
| --- | --- |
| Audit type | Core logic implementation code review (modern mainstream standards + project conventions), 4-zone parallel + lead P1 personal verification |
| Work time | 2026-08-02 |
| Scope | Run orchestration/state (main/game_state/spawner/tutorial), player system (player/player_damage/player_dash/aim_crosshair/aim_frame_layer), combat entities (enemy/boss/bullet/laser_weapon/explosion), services & pools (balance_service/save_manager/sfx_player/entity_registry/bullet_pool/enemy_pool/mothership), 17 files ~6900 lines |
| Method | Zone read-throughs + cross-file dependency tracing + P1 evidence personally verified (reading source to confirm) |
| Conclusion | 3 P1, 8 P2, 21 P3; overall quality high; no coroutine violations / signal reconnects / pool-guard gaps (2026-08-02 scope unified: P1=G01–G03, P2=G04–G011) |
| Auditor | Kimi Code (executed per user instructions) |

### G series (2026-08-02 core-logic full review — registered only, not fixed)

> Full report (scope/rules/evidence/fix priority) in `docs/archive/2026-08-02-core-logic-audit.md`. Recommendations for later decisions.

| ID | Severity | Location | Category | Description | Recommendation |
| --- | --- | --- | --- | --- | --- |
| G01 | P1 | `spawner.gd:501-506,563-572` | Pure bug (whole-run paralysis) | Homecoming during the boss-warning 2s window: `clear_pending()` only stops the Timer without resetting `_boss_active`; no other reset path → after continue, the wave/Boss/event three guards are permanently frozen, the whole run idles; the comment "later re-triggered per gate is expected" doesn't match reality (D01 scope doesn't hold) | **Fix** |
| G02 | P1 | `boss.gd:828`/`laser_weapon.gd:152-158`/`bullet.gd:250-257` | Pure bug (reward distortion) | Boss escape `_begin_escape` only sets collision layer 0 blocking Area2D paths; laser `_damage_tick`/splash `_splash` judge by registry+distance bypassing collision layers → finishing blow during the escape window → `add_boss_kill` scores/raises difficulty, contradicting the :905 comment and the same `_escaping` guard pattern as `fire_enrage_snapshot` | **Fix** |
| G03 | P1 | `tutorial.gd:97`/`start_panel.gd:244-247,282-288` | Player-harming (E02 completion) | Tutorial `_ready` unconditionally `delete_save()`; E02's guard only blocks `tutorial_done`, missing "has save and tutorial not completed" → clicking the tutorial button silently deletes an in-progress run | **Fix** |
| G04 | P2 | `game_state.gd:706-716` | Logic bug | `rebind_action` conflict cleanup only scans `key_bindings`, not `_default_bindings`: an uncustomized action whose default key gets rebound conflicts with another action on the same key | **Fix** |
| G05 | P2 | `tutorial.gd:310-311` | Hot-path performance | Stage 2 calls `max_health()` ×2 every physics frame (2 cfg + split allocations internally) | **Fix** (_ready cache) |
| G06 | P2 | `spawner.gd:160-161,178-185` | Robustness | `_apply_balance` nested structures (hover_band/types) untyped-checked; hand-edited structural corruption crashes with out-of-bounds, inconsistent with C03/E03 fallback scope | **Fix** |
| G07 | P2 | `aim_frame_layer.gd:74-80` | Pooled-reuse mismatch | `frame_half_size` first-call caches the collision radius into meta; if a pooled instance gets reused by a different-radius type the frame size / in-frame judgment goes stale (currently the only pooled path always reuses the same radius, not triggered) | Pending |
| G08 | P2 | `boss.gd:640` | Project convention | Escape exit `position.y < -280.0` hardcoded, violating the view_world_rect convention (enemy already relativized) | **Fix** |
| G09 | P2 | `bullet.gd:60`/`balance_service.gd:57` | Hot-path performance | Every enemy bullet spawn runs `enemy_damage_ramp()` JSON query (split+iterate), 30+/s under bullet-hell pressure | **Fix** (startup cache) |
| G010 | P2 | `bullet.gd:197`/`entity_registry.gd:12` | Performance | `GameState.enemies.has()` is an O(N) linear scan per homing bullet per frame | Pending |
| G011 | P2 | `mothership.gd:670-676`/`main.gd:665` | UI residual (E05 completion) | Homecoming `queue_free` of the mothership doesn't clear the HUD early-undock progress bar (`set_early_leave_charge(-1.0)` is the only hide entry) | **Fix** |
| G012 | P3 | `game_state.gd:595` | Magic number | `add_boss_kill` score base 500.0 hardcoded, not in balance | Fix |
| G013 | P3 | `game_state.gd:900-906` | Boundary condition | `apply_run_save` restores buff stacks unclamped; hand-edited saves can overflow max_health | Fix |
| G014 | P3 | `tutorial.gd:156,179,204,220` | Consistency | Tutorial 4 hardcoded world coordinates (960/600/300), D10 not synced | Fix |
| G015 | P3 | `tutorial.gd:320,329` | Performance | During charge, per-physics-frame tr()+Label assignment exceeds the HUD 0.1s throttle convention | Fix |
| G016 | P3 | `aim_frame_layer.gd:41-43` | Signal cleanup | `_exit_tree` doesn't explicitly disconnect `aim_assist_changed` (C22 pattern comparison) | Fix |
| G017 | P3 | `aim_crosshair.gd:48`/`aim_frame_layer.gd:172` | Performance | `_draw` calls `sin()` directly every frame (not via sin_fast lookup) | Not fixed |
| G018 | P3 | `player.gd:368-373`/`aim_frame_layer.gd:163-168` | Duplicated code | Distance-decay piecewise function has two implementations (`aim_dist_falloff`/`_dist_falloff`); editing one side and forgetting the other breaks consistency | Pending |
| G019 | P3 | `player.gd:481-484` | Boundary condition | `movement_locked` sets `_dashing=false` directly, interrupting dash; dash_timer residual / cooldown fuel not refunded | Pending |
| G020 | P3 | `laser_weapon.gd:136-137,147` | Defensive gap | `_start_beam` unconditionally overwrites `_saved_autofire` (currently unreachable, E08 family) | Pending |
| G021 | P3 | `laser_weapon.gd:96` | Dead code | `_aim_dir(_start)` parameter unused | Fix |
| G022 | P3 | `explosion.gd:24`/`enemy.gd:237` | Performance | `spawn_at` cfg query per call; Enemy builds the same material per craft | Fix |
| G023 | P3 | `explosion.gd:59-61` | Lifecycle | With invalid parent, calls queue_free on a timer already destroyed with its parent (UAF-risk line; branch unreachable) | Pending |
| G024 | P3 | `boss.gd:255,705` | Magic number | Type-3 normal-phase summon interval 6.0 hardcoded, no balance key | Fix |
| G025 | P3 | Multiple files (enemy/bullet/boss/boss_movement/boss_attacks) | Hot-path performance | Repeated per-entity per-frame `view_world_rect()` (~130/frame) | Pending |
| G026 | P3 | `enemy.gd:430`/`boss_fire.gd:19` | Boundary condition | Shooter and player centers coincide → zero-vector bullet direction; bullet never destroyed | Pending |
| G027 | P3 | `mothership.gd:543,577` | Hot-path performance | `_live_targets` empty-target branch allocates an array + full-table scan every frame (C28 scope only held when targets exist) | Fix |
| G028 | P3 | `sfx_player.gd:25-26` | Defensiveness | `play()` has no empty-pool guard (out-of-bounds/div-zero when build_pool not called) | Fix |
| G029 | P3 | `balance_service.gd:36-39` | Type robustness | `cfg()` numeric branch returns JSON nodes as-is; hand-edited int fields drift to float (inconsistent with C18 explicit conversion) | Fix |
| G030 | P3 | `mothership.gd:588` | Naming consistency | Missiles reuse `GATLING_SCORE_SCALE` score coefficient; semantic mixing | Fix |
| G031 | P3 | `mothership.gd:182-184` | Resource sharing | Two turrets share one ParticleProcessMaterial writing scale (idempotent same-value safe; E14 family) | Not fixed (note as safe) |
| G032 | P3 | `mothership.gd:168-170`/`mothership.tscn:22` | Comment mismatch | Script comment says "tscn stores 1.0 baseline"; tscn actually 1.25 and script hardcodes 1.25*ws | Fix |

> Post-fix regression scope: after fix batches land, execute per priority in `docs/archive/2026-08-02-core-logic-audit.md`, backfilling this table's "fix verification record" entry by entry.

## G Series Fix Verification Records (2026-08-02 full disposition)

> Fix batches: batch 1 (P1×3, cb8511b), batch 2 (P2×8, b7b2cc8; commit-message title "P2×9" is a typo — body actually lists G04–G011, 8 items, unified per 2026-08-02 scope), batches 3+4 (P3+pending, ffef641). Full audit report in `docs/archive/2026-08-02-core-logic-audit.md`.

| ID | Status | What changed / why it works / verification |
| --- | --- | --- |
| G01 | ✅ Fixed | `spawner.clear_pending()` resets `_boss_active=false` — after homecoming during the warning 2s window, the wave/Boss/event three guards are no longer permanently frozen (D01 "re-trigger per gate" scope restored). Verification: difficulty_test adds 2 assertions PASS |
| G02 | ✅ Fixed | `boss.take_damage` entry adds `_escaping` interception — laser/splash judge by registry+distance bypassing collision layers; prevents finishing-blow-during-escape triggering add_boss_kill reward distortion (aligned with fire_enrage_snapshot's same guard). Verification: enemy_combat_test adds "take_damage ineffective during escape" assertion PASS |
| G03 | ✅ Fixed | Tutorial button disabled condition widened to `tutorial_done or has_save` (E02 completes the "has save, not completed" path), preventing tutorial._ready's unconditional delete_save from deleting an in-progress run. Verification: startup_flow_test adds 1 assertion PASS |
| G04 | ✅ Fixed | `rebind_action` conflict cleanup scans default bindings — when an uncustomized action's default key is occupied, the binding is cleared to override the default, preventing one key, two actions. Verification: keybind_test adds 3 assertions PASS |
| G05 | ✅ Fixed | Tutorial stage-2 HP-lock cached `_max_hp` (one _ready read; no more 2 cfg per physics frame). Verification: tutorial_test 0 FAIL |
| G06 | ✅ Fixed | spawner `hover_band`/`_merge_type` nested-structure type checks (corrupted JSON falls back to defaults, aligned with C03/E03). Verification: wave_pacing 0 FAIL |
| G07 | ✅ Fixed | `Enemy.setup` refreshes `aim_frame_radius` meta on every activation — pooled instances reused by different-radius types no longer go stale. Verification: enemy_combat/pool_reuse 0 FAIL |
| G08 | ✅ Fixed | Boss escape exit baseline → `view_world_rect().position.y - 280.0` (280 hardcode removed; enemy same scope). Verification: enemy_combat 0 FAIL |
| G09 | ✅ Fixed | `BalanceService` ramp factor cached at load (no more path.split + dict-iterate JSON query per enemy bullet). Verification: enemy_combat/wave_pacing 0 FAIL |
| G010 | ✅ Fixed | `EntityRegistry` adds `_enemy_set` O(1) membership index + `GameState.enemies_has()`; homing per-frame judgment switched (no more Array.has linear scan). Verification: pool_reuse/hit_logic/buff33 0 FAIL |
| G011 | ✅ Fixed | `mothership._exit_tree` hides the HUD early-undock progress bar (E05 only covered start_release; the homecoming recycle path leaked it). Verification: mothership_summon_test adds 2 assertions PASS |
| G012 | ✅ Fixed | `add_boss_kill` score base 500.0 into balance (`milestones.boss_kill_base`, in BALANCE_MAP). Verification: balance_test 0 FAIL |
| G013 | ✅ Fixed | `apply_run_save` buff-stack restore clamped ≥0 (hand-edited negative stacks no longer break buff_count logic). Verification: startup_flow 0 FAIL |
| G014 | ✅ Fixed | Tutorial 4 hardcoded world coordinates → `view_world_rect()` baselines (960/600/300 converged, aligned with D10). Verification: tutorial_test 0 FAIL |
| G015 | ✅ Fixed | Tutorial charge-percentage text throttled to 0.1s (aligned with HUD-meter convention; no per-physics-frame tr()+Label assignment). Verification: tutorial_test 0 FAIL |
| G016 | ✅ Fixed | `aim_frame_layer._exit_tree` explicitly disconnects `aim_assist_changed` (aligned with player C22 pattern). Verification: smoke 0 FAIL |
| G017 | 🟦 Not fixed | One `sin()` per frame in `_draw` is negligible magnitude (not a hot-path bottleneck) |
| G018 | ✅ Fixed | Distance decay extracted to `Player.dist_falloff_curve` single static implementation (player/aim_frame_layer shared; editing one side no longer breaks the other). Verification: smoke/buff33 0 FAIL |
| G019 | 🟦 Registered, not fixed | `movement_locked` freezing movement/dash is a **dead-field path** (no code anywhere writes true; always false, unreachable); enrage movement constraint is implemented via `apply_enrage_slow` ×0.35 slowdown. 2026-08-02 scope correction: enrage design evolved independently (BOSS_REDESIGN §4.3), no longer attributed to "aligned with original controls_locked" |
| G020 | ✅ Fixed | `_start_beam` records `_saved_autofire` only when inactive (defensive; unreachable under the current `_active` latch). Verification: buff33 0 FAIL |
| G021 | ✅ Fixed | `_aim_dir` unused parameter removed (prevents misleading callers). Verification: buff33 0 FAIL |
| G022 | ✅ Fixed | Explosion visual-ratio static cache + `CinematicFx.additive_material` material static sharing (N crafts N copies → 1). Verification: enemy_combat/smoke 0 FAIL |
| G023 | ✅ Fixed | Removed the `_boss_seq_step` invalid-parent branch's queue_free on a timer already destroyed with its parent (UAF-risk line). Verification: enemy_combat 0 FAIL |
| G024 | ✅ Fixed | Type-3 normal summon interval into config (`boss.phases.type3.summon_interval`, in BALANCE_MAP). Verification: boss_pattern 0 FAIL |
| G025 | 🟦 Registered, not fixed | Per-frame repeated view_world_rect() (~130) single-call overhead negligible; global caching has wide impact, low benefit |
| G026 | ✅ Fixed | Enemy/formation bullets `(player-from).normalized()` zero-vector fallback DOWN (prevents zero-direction bullets never destroyed when centers coincide). Verification: enemy_combat/boss_pattern 0 FAIL |
| G027 | ✅ Fixed | Mothership gatling/missiles set interval first, then check empty (empty target no longer allocates array + scans registry per physics frame). Verification: mothership_summon 0 FAIL |
| G028 | ✅ Fixed | `SfxPlayer.play()` empty-pool guard (prevents out-of-bounds/div-zero when build_pool not called). Verification: headless full run 0 errors |
| G029 | ✅ Fixed | `BalanceService.cfg()` numeric branch converts explicitly per default's type (JSON float no longer drifts typed int fields, aligned with C18). Verification: balance_test 0 FAIL |
| G030 | ✅ Fixed | Missile score coefficient independent `MISSILE_SCORE_SCALE` constant (was reusing GATLING, semantic mixing). Verification: mothership_summon 0 FAIL |
| G031 | 🟦 Not fixed | Two turrets sharing one ParticleProcessMaterial writing scale is idempotent same-value safe (E14 family note holds) |
| G032 | ✅ Fixed | Mothership comment corrected (tscn actually stores 1.25 baseline) + named `SHIP_SCALE` constant. Verification: mothership_summon 0 FAIL |

> Post-fix regression: `--headless --import` 0 errors / BALANCE_MAP refreshed (2 new keys, 0 missing; only pre-existing `version` unreferenced) / batch-related assertion scenes (difficulty/enemy_combat/startup_flow/keybind/mothership_summon/tutorial/pool_reuse/boss_phase/boss_pattern/balance/buff33/hit_logic/wave_pacing/smoke) all 0 FAIL.

# Performance Optimization Landing Record (2026-08-02, full)

> Fully landed per `docs/archive/2026-08-02-performance-optimization-plan.md` (P0×4 / P1×7 / P2×8), touching 24 source files (git counts 27 files incl. 3 docs; unified per 2026-08-02 scope). This section backfills intersections with existing audit entries; full landing summary, A/B data and regression in plan §12. Touched: `game_state.gd` (view_world_rect frame cache / heal-chain cache / mission guards), `spawner.gd`+`enemy_pool.gd`+`enemy.gd` (enemy pooling unified), `enemy/boss/turret_battery/formation_craft` (hit white-flash manual decay), `player.gd` (afterimage pool / single ticks read), `aim_frame_layer.gd` (scan cache), `starfield.gd` (draw batching), `hud.gd` (text-tier guards / sin_fast), `meta_health.gdshader` (downsampling), P2 items.

| ID | Previous status | Backfill | What changed / why it works / verification |
| --- | --- | --- | --- |
| D08 | ✅ Fixed | ✅ Supplement | On top of existing `_cached_max_hp`, deeper links cut: `max_health()` base value cached in `_apply_balance`, `passive_regen_delay/rate` refreshed on difficulty change — the whole `heal_tick→heal→max_health` chain no longer hits cfg every physics frame (the repo's only per-frame cfg violation eliminated). Verification: smoke 142 / buff33 29 PASS |
| D11 | 🟦 Observation-level, not fixed | ✅ **Fixed** | Hit white-flash multi-tween competition → **manual decay** (`_flash_timer` + per-frame lerp in `_physics_process`, four entities enemy/boss/turret_battery/formation_craft) — eliminates a Tween allocation + competition per hit; Godot 4.6 Tween has no `reset()`, so no prebuilt reuse. Verification: hit_logic 61 / enemy_combat 33 / smoke 142 PASS |
| E13 | 🟦 Registered, not fixed | ✅ **Fixed** | `heal_tick` per-physics-frame nested dict queries — `passive_regen_delay/rate` cached + refreshed on `set_difficulty`/`_apply_balance` (the original "cache stale on mid-run difficulty switch" concern resolved by the refresh chain). Verification: smoke 142 / base_system 46 PASS |
| G017 | 🟦 Not fixed | ✅ **Fixed** | `aim_crosshair`/`aim_frame_layer` per-frame direct sin() → `Enemy.sin_fast` (with an 11-spot batch sweep of run paths; cinematics/one-time builds exempt). Verification: i18n / mouse_lock / smoke 0 FAIL |
| G025 | 🟦 Registered, not fixed | ✅ **Fixed** | Per-frame repeated `view_world_rect()` (~130) — `GameState` physics-frame-number guard cache (bullets×N/enemies×N/player/Boss share one viewport query per frame; 4-point invalidation on zoom/camera change). Verification: view_zoom 50 / smoke 142 / perf_bench A/B -8~9% |
| G027 | ✅ Fixed | ✅ Supplement | On top of existing empty-target early-exit, `_live_targets()` output buffer reused (no new Array per shot). Verification: mothership_summon 32 PASS |

> Regression: `--headless --import` / `--quit-after 300` 0 errors; smoke 142 / pool_reuse 12 / base_system 46 / all 31 assertion scenes 0 FAIL (actually 31 at landing incl. mouse_lock_test; the recorded 27 was a scope typo, corrected 2026-08-02); perf_bench same-environment A/B median 0.131→0.120 ms/frame (~-8~9% CPU logic time).

---

# Round 6 Audit (2026-08-02 robustness focus)

## Work Time & Scope

| Field | Value |
| --- | --- |
| Audit type | Robustness focus — crash/hang/state-corruption/corrupted-data paths (empty input, resource load failure, div-zero/NaN, node lifecycle, signal re-entry, idempotency, illegal state-machine transitions, pool boundaries, config without domain validation) |
| Work time | 2026-08-02 |
| Scope | All `scripts/` + `autoload/game_state.gd` (3-way parallel: run orchestration+player / combat entities+events / UI+services+presentation) |
| Method | 3-way parallel read-only scan + lead cross-verification (deduplicated against A–G series baselines; already-handled items like G026/C03/E03/G06 not repeated) + verdict classification |
| Conclusion | No P0; P1×3 + P2×6 + P3×20 (group); overall robustness strong (pool double-guards / registry dedup / Timer-over-coroutines / idempotency guards covering most re-entry and lifecycle paths); real risk concentrated in type-check gaps on hand-edited balance.json corruption and a few zero-value/div-zero edges |
| Auditor | Kimi Code CLI (executed per user instructions) |
| Full report | `docs/archive/2026-08-02-robustness-audit.md` (find-judge-fix tracking) |

## H Series Fix Verification Records (2026-08-02 full landing, 3 commit batches)

| ID | Severity | What changed / why it works / verification |
| --- | --- | --- |
| H01 | P1 | Right-stick aiming → four independent actions (`aim_left/right/up/down`, axis 2/3 ±1) — `Input.get_vector` on the same action for both signs always yields zero, P0-1 virtual crosshair completely dead; base_system_test adds four-direction action/axis-event assertions |
| H02 | P1 | `apply_key_bindings` erases only keyboard events per event type (`action_erase_event` single event) — the original `action_erase_events` also cleared gamepad events, killing the gamepad for the session after rebind/reset; tests add assertions that gamepad events survive rebind/reset |
| H03 | P1 | Difficulty-tier `milestone/cycle_mult ≤ 0` numeric domain validation — a constant-zero threshold makes continue_run's milestone while-loop never exit (hang); difficulty_test/balance_test regression |
| H04 | P2 | BGM runtime `ResourceLoader.load` null-check degrade (push_warning + return) — missing resource no longer null-ref crashes |
| H05 | P2 | homing tracking `dist <= 0` keeps original direction — div-zero produced inf/NaN angles polluting bullet coordinates |
| H06 | P2 | laser `_saved_autofire` capture moved before `_active=true` — dead-guard fix; `_end_beam` no longer force-enables autofire unconditionally |
| H07 | P2 | spawner `unlocked_types`/`_pick_bullet_type`/enemy bullet-type empty-pool fallback to first type/single shot — `randi()%0` crash protection |
| H08 | P2 | meta_health `crack.density` length + element validation, fallback to default tier — out-of-bounds index / float-conversion error protection |
| H09 | P2 | hud warning-banner blink moved fade-out+hide out of the loop + tween mutex cache — old implementation wrapped the fade in set_loops, permanently hidden at first loop's end (claimed 2s, actually 0.9s) |
| H10 | P3 | bullet `setup`/boss_attacks formation volley zero-direction bullets uniformly fall back DOWN (G026 family) |
| H11 | P3 | boss `hp_mults` length + element validation / `STRAFE_SPEEDS` short-array fallback / `fire_intervals` non-array type check — Boss HP=0 immune-to-damage silence and _ready crash protection |
| H12 | P3 | enrage `square_path_ratio` clamped to (0.05, 1.0] — zero value div-zero produced inf orbit NaN |
| H13 | P3 | elite `fire_interval`/mothership_summon `shot_durations` type + length checks with fallback (G06 scope) |
| H14 | P3 | mothership `_warp_gate` call `is_instance_valid` null check (dangling reference at scene-unload timing) |
| H15 | P3 | Config clamp batch: event intervals / shake decay / HUD polling & low-HP cycle / time step / charge duration / meta_health duration keys (tau/duration/fade) — ≤0 div-zero / throttle-broken / never-decay protection |
| H16 | P3 | `world_scale` domain validation clamped ≥0.01 — 0/negative zeroes or mirrors the ship |
| H17 | P3 | exit_confirm fade-out exit → `tween_callback(get_tree().quit)` — replaces `await tween.finished` suspended coroutine (AGENTS coroutine discipline) |
| H18 | P3 | missions save restore keeps the `goal` key — whole-replacement lost goal, permanently silencing `mission_completed` (latent) |
| H19 | P3 | enemy `hover_band` type + length checks with fallback (aligned with spawner G06) |
| H20 | P3 | Lifecycle/boundary guard group: buff_select rebuild resets `_closing` soft-lock, tutorial failure/finished states block stage advance, ui_theme button tween mutex kill, base_console route buff-name missing-key fallback + negative-heal clamp, settings_ui `_pages` empty defense, cinematic_fx <2-point defense, return_cinematic zero shot-duration div-zero defense |

> Post-fix regression: `--headless --import` 0 errors; batch-related assertion scenes (smoke/tutorial/buff33/base_system/return_cinematic/back_navigation/i18n/boss_pattern/mothership_summon/elite_turret_event/meta_health_fx/enemy_combat/difficulty/balance) all 0 FAIL.

## GDScript Engine Warning Tiers & Continuous Improvement List (2026-08-02, chore/gdscript-warnings branch)

> Deployed Godot 4's compiler warning system in `project.godot` `[debug]` section (`debug/gdscript/warnings/*`), three tiers:

| Tier | Config | Disposition |
| --- | --- | --- |
| **error (zero-tolerance, CI gate)** | unused variables/private fields/signals, variable & built-in shadowing, integer division, redundant await, annotation order — 25 classes | CI import fails on "Warning treated as error"; all existing errors fixed (incl. this round's 20 InputEvent type annotations, 2 shadow renames, 6 subclass-referenced-field annotations) |
| **warn (editor-GUI visible)** | unsafe_cast / unsafe_method_access / unsafe_property_access / untyped_declaration / untyped_variable / unsafe_line — 6 classes | **Continuous-improvement list 202 items**: unsafe_method 91 (calling subclass methods on Variant/Node; mostly safe after type checks), unsafe_cast 54 (`as` is a checked cast; failure returns null, no crash), untyped 66 (`for x in dict/array` iteration variables; blindly typing risks runtime assertions — per-spot container-type confirmation needed), unsafe_property 33 (InputEvent etc.; 20 type-annotation items fixed, rest pending). **Fix path**: per-spot type convergence or `@warning_ignore` declaring the type-check guarantees safety; visible in the editor script status bar |
| **ignore (confirmed conflict with project style)** | inferred_declaration (`:=` is Godot official recommendation), return_value_discarded (Tween chain standard style) | Project-style conflicts; disabled |

**Verification**: after config finalization `--headless --import` 0 errors; after InputEvent type annotations meta_health_fx/smoke/base_system/back_navigation regression pass.

---

# Round 7 Audit (2026-08-02 full-pipeline static analysis & fixes)

## Work Time & Scope

| Field | Value |
| --- | --- |
| Audit type | Whole-game pipeline deep syntax & logic probe (static gates + 9 sub-agents parallel deep reads + lead re-check) |
| Work time | 2026-08-02 |
| Scope | `scripts/` 61 files + `autoload/game_state.gd`, 9 groups in parallel by pipeline (player/combat/enemies/Boss/mothership homecoming/events/UI/core orchestration/cinematics) |
| Method | After static gates (gdformat/gdlint/engine import+smoke) all green, 9 sub-agents read deeply in parallel (cross-checking balance.json keys, signal pairing, pool lifecycle, hot paths, world_scale idempotency); lead read-verified P1 findings; finally full 31-assertion-scene regression |
| Conclusion | No P0; P1×2 + P2×17 + P3×~20 (group). Of these: 2 P2 verified no fix needed (formation bomb protective pod / mouse warp coordinates pending real-machine test), 2 conflicts with existing "registered-not-fixed" decisions reverted (E09/E15), 1 P3 dead-marker verified as test contract rather than dead code (hive_volley) |
| Auditor | Kimi Code CLI (executed per user instructions) |

## I Series Findings & Disposition (full fix, 5 commit batches 025b393/18b5ad8/89ee243/ecb9d33/6bbbf8b)

| ID | Severity | Location | Category | Description | Disposition |
| --- | --- | --- | --- | --- | --- |
| I01 | P1 | `bullet.gd:295` | Pure bug (runtime error + damage amplification) | Explosive-bullet hit branch duck-calls `is_boss()` on enemy-group Area2D; TurretBattery/FormationCraft (extends Area2D) lack this method → runtime error interrupts the function → bullet not destroyed, double hit | ✅ Fixed (batch 1): dual condition `not area.has_method("is_boss") or not area.is_boss()` — no method (turret/formation craft) or false return (normal enemy) both explode; Boss/elite true don't. Verification: hit_logic_test A12 all PASS |
| I02 | P1 | `formation_strike_event.gd:222` | Pure bug (gameplay-rhythm distortion) | `_begin_run` loop k-outer/i-inner generates a non-sorted timeline `[0,0.8,1.6,2.4,0.4,...]`; `_process_drops` greedily consumes by monotonic `_state_time` → second wave's bombs pile onto the same final frame (4 craft = 4 bombs same frame), contradicting the "wingmen staggered by bomb_interval" design intent | ✅ Fixed (batch 1): loop transposed to i-outer/k-inner, timeline monotonic. Verification: formation_strike_event_test 0 FAIL |
| I03 | P2 | `player.gd:199` | Signal cleanup | `_exit_tree` misses disconnecting `joy_settings_changed` (buffs_changed/aim_assist_changed both disconnect); re-entering the tree reconnects → callbacks run twice | ✅ Fixed (batch 2): symmetric disconnect added. Verification: smoke 0 FAIL |
| I04 | P2 | `laser_weapon.gd:29` | Size-family consistency | `BEAM_HALF_WIDTH` not multiplied by world_scale while `ENEMY_HIT_RADIUS` is (two levers in one hit formula) | 🟦 Registered, not fixed: AUDIT_VAULT E09 existing decision (26→10.4px after multiplying would significantly weaken laser hits — gameplay change needing product judgment); this round's change reverted |
| I05 | P2 | `spawner.gd:501` | Resource-list management | `_pending_telegraphs` dangling references only grow (SpawnTelegraph self-destroys at 0.6s but is never removed from the array); accumulates into homecoming over long runs | ✅ Fixed (batch 1): telegraph connects `tree_exited` auto-erase, symmetric with `_pending_timers`. Verification: enemy_combat/wave_pacing 0 FAIL |
| I06 | P2 | `enemy.gd:383` | Lifecycle defense | `_exit_tree`'s `_pool.forget(self)` lacks `is_instance_valid(_pool)` (asymmetric with `_despawn`); pool released before instance → dangling-reference access | ✅ Fixed (batch 1): null check added. Verification: pool_reuse 0 FAIL |
| I07 | P2 | `orbital_strike.gd` | Config without domain validation (potential soft-lock) | `IMPACT_AT>=1.0` → finished fires before struck → main never receives `_on_orbital_struck` → tree stays paused + player input-locked **permanently stuck**; `DURATION=0`/`MISSILE_FROM>=IMPACT_AT` same family | ✅ Fixed (batch 3): timeline clamps (duration≥0.01, impact_at≤0.95, missile_from<impact_at). Verification: orbital_strike_test 0 FAIL |
| I08 | P2 | `boss_fire.gd` | Div-zero/NaN | `fire_ring`/`fire_enrage_wave`/`fire_bullet_wall` read bullet/wall counts directly from cfg (e.g. ENRAGE_SNAPSHOT_*) without lower-bound clamp; a mis-written 0 → `float(count-1)` div-zero NaN directions | ✅ Fixed (batch 3): entry `maxi(2, count)` clamp. Verification: boss_pattern/boss_enrage 0 FAIL |
| I09 | P2 | `turret.tscn:7` | Resource sharing | CircleShape2D radius written at runtime by `turret_battery.gd` but missing `resource_local_to_scene=true` (explicit AGENTS.md convention; enemy.tscn fixed the same type) — all instances writing the same value, not yet exposed | ✅ Fixed (batch 3): added `resource_local_to_scene = true`. Verification: elite_turret_event 0 FAIL |
| I010 | P2 | `mothership.gd:462-480` | Same-frame double trigger | STAY-state warning expiry `start_release()` and `_early_timer` expiry `_early_depart()` double-enter in the same frame → second start_release repeats the release presentation + resets timing | ✅ Fixed (batch 3): `start_release` idempotent guard (`_state != STAY` early exit). Verification: mothership_summon 0 FAIL |
| I011 | P2 | `buff_select.gd:99` | Latent soft-lock | `_on_locale_changed` in the `_closing` branch only resets pause without `visible=false`/modulate reset → panel residue + run not paused + `if visible: return` permanently skips milestones | ✅ Fixed (batch 2): close semantics completed + grab_focus after rebuild. Verification: buff_panel/buff33 0 FAIL |
| I012 | P2 | `formation_bomb.gd:93` | Semantic verification | Formation-bomb AoE deals damage directly bypassing monitoring; suspected penetrating the mothership protective pod | 🟦 Verified, no fix needed: mothership `_start_docking` immediately `set_invincible(999.0)` covering the whole dock period; take_damage's invincible check immunizes that damage |
| I013 | P2 | `hud.gd:556` | Tween race | `_show_warning` fade-out tween and label-blink t2 not included in the `_warning_tween` mutex; a second warning is suppressed by the old fade-out's hide() early, killing the whole segment | ✅ Fixed (batch 2): blink/fade co-managed; kill current active tween on stage switch. Verification: smoke/buff_panel 0 FAIL |
| I014 | P2 | `main.gd:519` | Flow omission | "Continue run" restores data but lacks `_start_entry_sequence()` (both intro and homecoming-continue have it), contradicting ARCHITECTURE/D01 comment declarations | ✅ Fixed (batch 4): entrance transition sequence added (is_connected guard idempotent). Verification: entry_animation/startup_flow 0 FAIL |
| I015 | P2 | `game_state.gd:132` | Hang (H03 completion) | Global `milestones.cycle_mult` lacks monotonicity validation; ≤0 threshold platforms out → `apply_run_save`'s milestone while never exits (hang); the original H03 check lived in the difficulty sub-table (no cycle_mult key) — dead code | ✅ Fixed (batch 4): global key `maxf(...,0.01)` domain validation + dead code removed. Verification: difficulty/balance 0 FAIL |
| I016 | P2 | `game_state.gd:1246/1314` | Type-check gap | `load_profile`'s high_score/date call `int()` directly, not following the save_num type-check convention; hand-edited illegal types break the load chain | ✅ Fixed (batch 4): `int(save_num(...))`. Verification: startup_flow/base_system 0 FAIL |
| I017 | P2 | `tutorial.gd:262` | Dangling reference | `_mothership` reference not nulled after release (main.gd:636 has a tree_exited-nulling pattern); stage-3 null check relies on released-object `==null` semantics | ✅ Fixed (batch 4): `tree_exited` nulling connection added. Verification: tutorial_test 0 FAIL |
| I018 | P2 | `cinematic_fx.gd:202` | Latent crash | BeamFlow `_sample_at` lacks an empty-array guard; `points.size()<2` → negative-index out-of-bounds (current caller passes 24 points, unreachable) | ✅ Fixed (batch 4): `_samples.is_empty()` early exit. Verification: return_cinematic/intro_cinematic 0 FAIL |
| I019 | P2 | `mouse_trap.gd:84/94` | Coordinate semantics pending real-machine test | `Input.warp_mouse` window-relative vs global-screen coordinate semantics differ across platforms; correctness of adding `win.get_position()` needs a windowed-environment test | 🟦 Pending: comment contains the landed research conclusion (accepts screen coordinates); headless can't test; registered for windowed-environment verification |
| I020 | P3 | `enemy.gd:405` | Hot-path cache | `_physics_process` queries `buff_count(&"slow_field")` dict every frame | 🟦 Registered, not fixed: AUDIT_VAULT E15 existing decision (negligible overhead); this round's change reverted |
| I021 | P3 | `enemy.gd:247` | Visual tier mismatch | Elite tail-glow point: at `_ready`, `is_elite` always false (pooled instance's setup hasn't run), radius always takes the normal tier | ✅ Fixed (batch 1): radius tier moved into `_update_tail_glow`, recomputed absolutely by is_elite scale (idempotent). Verification: enemy_combat/pool_reuse 0 FAIL |
| I022 | P3 | `enemy.gd` (2 spots) | Param inversion | `randf_range(1.0, fire_interval)` errors on inverted params when fire_interval<1.0 | ✅ Fixed (batch 1): `maxf(fire_interval,1.0)` clamp |
| I023 | P3 | `spawner.gd:428` | Out of bounds | `unlocked_types` indexes `UNLOCK_SCORES[i]` by craft-type table; short array → out-of-bounds crash | ✅ Fixed (batch 1): loop upper bound `mini` clamp |
| I024 | P3 | `boss_movement.gd:30` | Dead code | `reset_press`'s `_press_timer = _press_timer` self-assignment no-op; comment hints at retention semantics | ✅ Fixed (batch 3): self-assignment removed, comment corrected |
| I025 | P3 | `boss_attacks.gd:319` | Dead-marker verification | `hive_volley` meta suspected write-only | 🟦 Verified not dead code: `boss_pattern_test.gd:304` scene-4 assertions depend on that meta count; kept |
| I026 | P3 | `mothership_summon_window.gd:315-340` | Framerate-dependent distortion | Interpolation baseline reads the current value after last frame's `set_point_position` (not the original endpoints), producing framerate-dependent cumulative drift | ✅ Fixed (batch 3): original endpoints cached at build. Verification: mothership_summon 0 FAIL |
| I027 | P3 | `dawn_station.gd:250` | Tween idle-spin | Destruction-state shards `set_loops()` target a fixed value; the loop replay completes instantly, shards frozen at first-loop end | ✅ Fixed (batch 3): round-trip segment (drift out → return). Verification: return_cinematic 0 FAIL |
| I028 | P3 | `scheduled_event_trigger.gd:22` | No domain validation | `_chance` not clamped to [0,1]; out-of-range always triggers / never triggers | ✅ Fixed (batch 3): `clampf` |
| I029 | P3 | `main.gd:69-70/314` | Div-zero/window | `HOME_CHARGE_TIME`/`GIVE_UP_HOLD_TIME` no div-zero clamp (H15 only clamped DOCK); H charging doesn't check the summon window | ✅ Fixed (batch 4): `maxf(...,0.01)` + `_summon_window == null` guard |
| I030 | P3 | `game_state.gd:133-143/1176` | Defense/comment | `_prog_per_*` negative values, `_max_hp_base` no lower bound; add_buff comment claims a max_stacks clamp that doesn't exist | ✅ Fixed (batch 4): negative/lower-bound clamps + comment corrected |
| I031 | P3 | `meta_health_fx.gd:329/204` | Dual config source | Heartbeat fade-out hardcodes 0.3s (should read `dying_fade`); DYING threshold THRESHOLDS hardcodes 0.20 alongside cfg dual source | ✅ Fixed (batch 2): unified `_cfg` reads. Verification: meta_health_fx 0 FAIL |
| I032 | P3 | `settings_ui.gd:447` | Guard ineffective | `_on_locale_changed` writes node text first, then `if _pages.is_empty(): return` — guard position ineffective | ✅ Fixed (batch 2): guard moved to function's first line |
| I033 | P3 | `cinematic_fx.gd:83/257` | Boundary/wrap | `ring_points(n=0)` reads unwritten elements; SpeedLine diagonal dir doesn't wrap (current caller only passes DOWN, unreachable) | ✅ Fixed (batch 4): `n<=1` early exit + per-component wrap |
| I034 | P3 | Multiple files (player_dash etc.) | Hot-path cache | `dash_cooldown_max()` queried per call on the HUD polling path (A5 cache pattern didn't cover it) | ✅ Fixed (batch 2): merged into buffs_changed cache. Verification: buff_panel 0 FAIL |
| I035 | P3 | `warp_gate.gd:163` | Stale comment | Comment claims attachments use set_point_position and don't scale with scale; `_swirls/_lip` actually use node scale | ✅ Fixed (batch 3): comment corrected (implementation unchanged) |
| I036 | P3 | `formation_bomb.gd:38` | Dead config | `collision_mask=1` with no collision-signal connection | ✅ Fixed (batch 1): comment added explaining it's kept as semantic documentation |

## Test Infrastructure Addendum (2026-08-02)

- **Problem**: `test/*.tscn` aren't referenced by the main scene → `--import` doesn't parse test/ scripts; when 8e370f9e's warning gate (narrowing_conversion/unused_variable error-level) conflicts with existing test/ code, **the scene silently hangs on launch** (banner prints then idles, 0% CPU, no error output); full regression blocked 30min+ by a single scene (CI ran the same type of cancellation).
- **Fixed** (batch 5 6bbbf8b): `boss_pattern_test:457` narrowing explicit `int()` (Boss.max_hp is float, introduced 2026-07-28), `wave_pacing_test:31` unused variable removed. Post-fix all 31 assertion scenes 0 FAIL.
- **Suggestions (pending)**: ① include `test/` in `gdformat --check` scope (currently only `autoload/ scripts/`; test/ long unformatted); ② add a per-scene timeout to the CI assertion-scene loop to prevent one hung scene blocking the whole job (currently relies on job-level 30min timeout as backstop).

> Post-fix regression (2026-08-02 evening): `--headless --import` 0 errors / `--quit-after 300` 0 errors / **all 31 assertion scenes 0 FAIL** (pre-fix boss_pattern/wave_pacing compile errors hung; hit_logic A12's has_method semantic defect fixed; all green is the post-fix final scope).
