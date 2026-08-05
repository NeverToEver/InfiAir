# Runtime Architecture & Config (ARCHITECTURE)

> On-demand reference for `AGENTS.md`: node tree of `scenes/main.tscn`, per-script duties, stack, key configs, directory roles. **Rules & entry: `AGENTS.md` + `.agents/*`**; test commands: `docs/TESTING.md`.

## Stack

- Engine: Godot 4.6 (standard, no .NET), `GL Compatibility` on desktop/mobile.
- Language: pure GDScript; `scripts/tools/*.py` offline tools (balance editor, doc gen, asset gen; stdlib only — sprite gens need PIL), not runtime deps.
- Assets: `assets/sprites/` PNG, `assets/audio/` WAV, `assets/fonts/NotoSansSC.ttf`.
- Data: `data/balance.json` tunable source (top sections: version/world_scale/player/enemies/elites/boss/hud/spawner/mothership/buffs/milestones/base_task/difficulty/progression/effects/tutorial/elite_turret_event/formation_strike_event/fog_events/dda); canonical Tab JSON since 2026-07-31, maintained by `balance_editor.py`. `data/translations.csv` zh/en source; `.translation` built by Godot import.

## Key Config Files

| File | Purpose |
| --- | --- |
| `project.godot` | Name, entry scene, only autoload, viewport/stretch, input map, renderer. Edit via Godot editor. |
| `data/balance.json` | All tunables. `boss` holds `phases` (pattern tables/telegraph/P2 params), `enrage.type_*`, `difficulty_scaling` (count/interval/speed tiers). Edit via `balance_editor.py`. |
| `data/translations.csv` | Translation keys + `zh`/`en` source. |
| `.gitignore` | `.godot/`, imported `*.translation`, IDE files, exports (`builds/`; `export_presets.cfg` committed since 2026-07-30). |
| `run.sh`/`run.command`/`run.bat` | Launch wrappers. `run.sh`: PATH → `~/.local/bin/godot` → App bundle, warn on old version, args passed through (`--editor` etc.). `run.command` (double-click + terminal, aligned with run.sh since 2026-08-02): candidates incl. `/Applications`+`~/Applications` `Godot*.app`, pick 4.6+, pass engine args, no `exec` (keeps window/output on abnormal exit). |
| `export_presets.cfg` | Linux/X11 + Windows Desktop presets (embedded pck, x86_64); needs matching export templates installed. |
| `release.sh` | Import → dual-platform export → package into `builds/release/` (`VERSION` env sets version). |

No `package.json`/`pyproject.toml`/`requirements*`/`Cargo.toml`/`go.mod`/Makefile/Docker. Packaging resumed 2026-07-30: `export_presets.cfg` committed, `release.sh` one-shot, `packaging/linux/` (user-space install.sh/uninstall.sh[--purge]/infiair.desktop) + `packaging/windows/` (per-user install.bat/uninstall.bat[/purge], Start-menu shortcut). No third-party deps for routine changes.

**Entry (2026-08-04)**: `project.godot` `run/main_scene = res://scenes/welcome.tscn` — accounts (UserDB: PBKDF2 users.json, per-user saves/settings, local leaderboard) + StartPanel retired (merged into welcome). GameOver「回主菜单」and tutorial exit return to welcome. Per-user save path `user://savegame_<user>_<sha256[:12]>.json` (see `docs/archive/2026-08-04-local-accounts-plan.md`).

**Release/CI status (2026-08-02)**: export templates installed (`~/Library/Application Support/Godot/export_templates/4.6.2.stable/`), `release.sh` proven, artifacts `builds/release/InfiAir-<ver>-linux-x86_64.tar.gz` / `-windows-x86_64.zip` (embedded pck + install scripts, gitignored). **Distributed as GitHub Releases attachments (not in repo)**: `gh release create v<ver> builds/release/InfiAir-<ver>-*.{tar.gz,zip}`. macOS can't run Linux/Windows binaries — platform validation needed on those hosts. CI `.github/workflows/ci.yml`: official Godot 4.6.2 stable binary (Linux x86_64, from official Release, no 3rd-party action) + headless import + main smoke + 41 assertion scenes (2026-08-04 count; CI run is authority), push/PR; green = merge gate (see `CONTRIBUTING.md`). CD `.github/workflows/release.yml` (manual): install Godot + templates → smoke → `release.sh` → tag `v<ver>` → GitHub Release with attachments; input version syncs `project.godot` `config/version`.

## Main Node Tree

```text
Main (scripts/main.gd)
├─ Starfield / Camera2D
├─ Player
├─ Spawner
├─ BulletPool / EnemyPool
├─ HUD (layer=2; layer=1 freed for MetaHealthFX "above world, below HUD")
├─ BuffUI / PauseUI / SettingsUI / GameOverUI / BaseUI
├─ ExitConfirm
├─ BackNavigator
├─ MouseTrap (mouse-in-window lock, always-on process)
├─ MetaHealthFX (runtime-created in _ready, layer=1, fullscreen health/hit FX)
├─ AimFrameLayer (runtime _ready, world coords, aim-assist brackets, registered `GameState.aim_frame_layer`)
├─ IntroCinematic (runtime on new game, layer=35)
├─ ReturnCinematic (runtime on return, layer=35)
├─ OrbitalStrike (runtime on continue sortie, layer=24)
├─ MothershipSummonWindow (runtime on summon complete, layer=24)
├─ WarpGate (runtime at mothership dock point, world coords)
├─ EliteTurretEvent (runtime _ready, registered to unified event manager, mutex)
└─ FormationStrikeEvent (runtime _ready, registered to unified event manager, lowest priority)
```
`scenes/` holds main/player/enemy/boss/bullet/mothership/cinematics/tutorial; same-name behavior scripts in `scripts/`. All dynamic run entities attach under Main (clear logic + test traversal).
FogEventManager is **not** under Main: it's a service child of the `GameState` autoload (global singleton, `GameState.fog_events`), holding its own effect layers (fake-enemy container, confusion overlay CanvasLayer, event banner). The **unified event manager** `GameEventManager` (`GameState.events`, child of GameState, `docs/EVENT_MANAGER.md`, 2026-08-05) batch-manages all random events (fog 4 + encounters 2): one `EVENT_FACTORIES` registry, grouped concurrency (`fog` ‖ `encounter`), unified trigger policy, unified lifecycle + `event_started/ended` signals; fog lifecycle is driven through it (FogEventManager = fog effects layer/API facade), encounter trigger moved out of spawner.

## Per-Script Duties

- `scripts/main.gd`: run orchestration — spawns, milestones, boss, mothership summon, return, give-up, BGM, page flow, entry animations (`_start_entry_sequence`).
- `autoload/game_state.gd`: global score/HP/buffs/difficulty/RP/tasks/routes/settings/signals facade. Delegates to `BalanceService`/`SaveManager`/`SfxPlayer`/`EntityRegistry`/`FogEventManager`/`GameEventManager` (A2); `GameState.*` syntax unchanged for callers. Also: **gamepad default binding at runtime** (`_bind_joypad_defaults()`: left stick/action keys/right-stick aim via InputMap, P0-1) + gamepad settings (`joy_aim_speed`/`joy_deadzone` setters, deadzone apply, profile persistence). **基地任务轮换 (2026-08-05, `docs/FOG_EVENTS.md` §1)**: `MISSION_POOL` (9 任务 = 3 类 × 3 档) + `TaskPool` 无放回抽取 + RefreshPoints 经济 (`base_task.refresh_cost`/`grant_per_visit`, 存档往返) + 按 `kind` 分发任务进度 (`_set_kind_progress`: kill/survive/boss 源不依赖具体 id)。
- `scripts/player.gd`: WASD, mouse aim, auto-fire, boost, fine move, dash, hit handling. Buff visuals via child `player_buff_visuals.gd` (procedural pods/shield arc/aura/beacon + engine tint), driven by `GameState.buffs_changed`; visual duties (tail/afterimage pool/body tint/hitbox dot/parry visuals/graze flash) delegated to `player_visuals.gd` (RefCounted composition, A8 2026-08-03). **Fairness (2026-08-03, `docs/archive/2026-08-03-combat-fairness-plan.md`)**: graze ring `GrazeArea` (mask=enemy_bullet, `player.graze_radius` no world_scale, once/bullet) + parry (`player_parry.gd`: WINDUP/ACTIVE/RECOVER FSM + 3.0s cooldown, `player.parry.*`; disc-shape trigger + sector filter in callback, `Bullet.reflect()`). Aim assist (impl: `aim_crosshair.gd`/`aim_frame_layer.gd` + `player.aim_assist` tiers): enemies rolled `aim_marked` by `mark_ratio` (0.25); AimFrameLayer draws brackets; AimCrosshair (child of Player, top_level) follows `aim_point()` and hides system cursor; in-frame `_fire()` writes `Bullet.homing_target` (bounded `homing_time`; close = direct, mid = steer, spiral-converge no orbit), out-of-frame = straight fire. `aim_point()` smooths (in-frame weak stick `stick_factor`, out-of-frame near magnet, cone weak track); shared by aim/fire/crosshair, advanced once per render frame; magnet + weak track share falloff (`player.aim_assist.falloff`: full <400px → linear to 0.3 floor at 1400px).
- `scripts/spawner.gd`: wave spawning + special-slot scheduling. Normal waves grouped with interval ramp; every 3–4 normal waves an elite wave; Boss/elite/events occupy special slots (normal waves paused), rest wave after kill. Waves, boss-3 minions, formations all via `GameState.enemy_pool.spawn()` (2026-08-02; `spawn`/`reactivate` optional `p_bullet_type`; `USE_POOL=false` → direct instantiation, A/B). Encounter event trigger policy moved to `GameEventManager` (2026-08-05); spawner keeps the Boss-freeze/wave-pause mutex hooks (`set_boss_frozen`/`set_waves_paused`) + `notify_event_triggered()` (wave-slot reset).
- `scripts/enemy.gd`/`mothership.gd`/`bullet.gd`/`laser_weapon.gd`: instantiable combat entities/weapons. `bullet.gd` fairness (2026-08-03): grace frames (enemy bullet in player Hitbox defers `player.grace_period`, `area_exited` cancels, single overlaps recheck at expiry), `try_graze()` once-count, `reflect()` (faction flip/mirror/×2 speed/×1.5 dmg).
- `scripts/boss.gd`: phase pattern tables (P1/P2/ENRAGE, `boss.phases.typeN` + telegraph), 4-type enrage (`boss.enrage.type_*`, player slow ×0.35), difficulty tiers × once in `_ready` (`boss.difficulty_scaling`). `FIGHT_Y` = offset from view top; usages via `_fight_anchor_y()`. **Transition fairness (2026-08-03)**: P1→P2 & ENRAGE clear all bullets + brief invincibility (`boss.phases.clear_on_shift`/`transition_invincible`; escape phases exempt). Design: `docs/BOSS_REDESIGN.md`.
- `scripts/bullet_pool.gd`/`enemy_pool.gd`/`explosion.gd`/`starfield.gd`/`camera_shake.gd`/`spawn_telegraph.gd`: pooling + presentation.
- `scripts/hud.gd`/`buff_select.gd`/`base_console.gd`/`settings_ui.gd`/`pause_ui.gd`/`game_over_ui.gd`/`exit_confirm.gd`: pages/overlays. `hud.gd` `SegmentedBar` (2026-08-03: `seg_weights`/`seg_colors`; boss bar per-phase colors, `hud.boss_bar_segments`) + `ParryBar` (`player.parry_energy_ratio`). Entry screen `scripts/welcome.gd` (`scenes/welcome.tscn`, 2026-08-04): brand zone + account panel (login/register/guest/delete + username dropdown) + difficulty + tutorial + settings + local leaderboard + quit; exit confirm self-handled. `start_panel.gd`/`start_radar.gd` retired (StartPanel merged into welcome; `StartBackdrop` decor reused by welcome). `base_console.gd` missions panel (2026-08-05): renders `GameState.active_mission_ids()` (轮换后非固定 `MISSION_DEFS`) + 刷新任务按钮（点数不足禁用并提示，`BASE_NO_REFRESH_POINTS`）；`show_base()` 发放 `GRANT_PER_VISIT` 刷新点。
- `scripts/event_manager.gd` + `scripts/fog_event_manager.gd` + `scripts/game_event.gd`（通用事件基底）/`fog_event.gd`（迷雾专门化层）/`fake_enemy.gd`/`fake_enemies_event.gd`/`confusion_event.gd`/`bullet_malfunction_event.gd`/`direction_shift_event.gd`: unified random-event system (`docs/EVENT_MANAGER.md` + `docs/FOG_EVENTS.md`) — **`GameEventManager`** (child of GameState, `GameState.events`): one `EVENT_FACTORIES` registry (fog 4 + encounters 2), grouped concurrency (`fog` ‖ `encounter`), unified trigger policy (`fog_events.*` / `elite_turret_event.trigger_*` / `formation_strike_event.trigger_*`, balance keys unchanged), unified lifecycle (fog `GameEvent` start/tick/end + duration; encounters Node FSM + `is_active` poll) + signals `event_started/ended`; encounter gate = injected spawner processing (`is_processing`+`can_process`). **`FogEventManager`** = fog effects layer/API facade (visual base: fake-enemy container/overlay/banner; re-emits `fog_event_started/ended` + `fog_direction_shift`; config vars + lifecycle forward to `GameState.events` fog group). Event interface (2026-08-05): **`GameEvent`（纯生命周期接口，零系统耦合）→ `FogEvent`（context 访问器）→ 具体事件**。`scripts/task_pool.gd`: 任务池无放回抽取（洗牌游标，`docs/FOG_EVENTS.md` §1）。
- `scripts/meta_health_fx.gd` + `assets/shaders/meta_health.gdshader` + `crack_field_bake.gdshader`: fullscreen health/hit FX (design `docs/META_HUD_DESIGN.md`) — hit CA/radial blur, directional ripple, low-HP crack growth (Voronoi baked once: windowed GPU 512² / headless CPU 64², **paths must stay equivalent**), desaturate/vignette, DYING heartbeat/breath/HUD shake; full-HP: ColorRect hidden + `_process` early-out (zero GPU ≈, ≈zero CPU). Values `effects.meta_health`; "reduce flash" in Settings.
- `scripts/cinematic_fx.gd` (CinematicFx): shared FX static factory — `soft_glow`, textured `particles` (≤96/emitter), double shockwave ring, layered `beam`, `radial_streaks` (`speed_lines` removed 2026-08-03, zero consumers); drive `_process` zero heap alloc. Shared by intro/return/mothership shows.
- `scripts/intro_cinematic.gd`: intro director (6 shots, new game, `docs/INTRO_CINEMATIC.md`); tree paused, Esc via BackNavigator `SKIP_INTRO`, any key/click skips; both paths → `finished` resume.
- `scripts/return_cinematic.gd` + `dawn_station.gd`: return director (7 shots, hold B, `docs/RETURN_HOME_CINEMATIC.md`); mirrors intro, Esc `SKIP_RETURN`, both paths → base UI (tree stays paused; BGM −40dB in shot 7). `DawnStation` shared factory (destroyed/hologram states; intro shot 1, return shots 2/3/4, base backdrop).
- `scripts/orbital_strike.gd`: continue-sortie clear anim: sights → missiles → impact pillar/rings; tree paused; `struck` frame drives main-registry clear (Boss kept, per-ship explosions) + resume; values `effects.orbital_strike`.
- `scripts/mothership_summon_window.gd` + `warp_gate.gd`: summon show (no pause; input-locked + event invincibility). Charge: main overlays `_charge_fx` (dual shrinking rings/inward particles/backlight); hanger window (pipeline off → arm release → launch + shuttle, layer=24, unified `finished`, `skip()` for tests); then warp gate (soft core + inner swirl + inward particles + lip occluder); mothership DESCEND decelerates (`warp_in_drop`), dual-ring slow zone (`Enemy`/`Boss.apply_slow`), fire cover; DOCKING tows player to pod (`enter_pod()` hides + disables hits, RELEASE `exit_pod()`), resupply/loiter/leave. Values `effects.mothership_summon`.
- `scripts/elite_turret_event.gd`/`strike_carrier.gd`/`turret_battery.gd` (+ `scenes/turret.tscn`)/`comm_overlay.gd`: turret event (`docs/ELITE_TURRET_EVENT.md`) — event FSM (triggered by `GameEventManager`), Boss mutex (`_boss_frozen`/`_boss_pending`/`_waves_paused` hooks in spawner), carrier director, turret entity (weak lock, `enemy` group + `GameState.enemies`), comm overlay bottom-left.
- `scripts/formation_strike_event.gd`/`formation_craft.gd`/`formation_bomb.gd`: formation strike (`docs/FORMATION_STRIKE_EVENT.md`) — lowest-priority random event (triggered by `GameEventManager`; Boss not frozen but **occupies wave slot**, spawner `_waves_paused`, mutex with turret; `abort()`-able by return), craft (`enemy` group + registry), fuse bombs (warning ring shrinks with fuse, AoE hits player only).
- `scripts/back_navigator.gd`: PC Esc / gamepad `ui_cancel` / Android back unified route. Tutorial = standalone `scenes/tutorial.tscn`, self-handles back; aligned with run: `_ready` creates AimFrameLayer, stage 1 force-marked targets, stage 4 hold-H charge → gate → mothership `begin_warp_in` → dock (hanger skipped; entity path same as `main._on_summon_window_finished`).
- `scripts/mouse_trap.gd`: mouse-in-window lock (on Main; `GameState.mouse_lock`, default on, profile-persisted) — while crosshair active (unpaused + cursor hidden) + window focused, mouse leaving content area warped back inside 1px via `Input.warp_mouse()` (`mouse_exited` + per-frame `_process` defense); released on unfocus/pause/non-crosshair (mouse can leave window, e.g. title-bar close); prevents crosshair freeze/jump from `get_global_mouse_position()`; `aim_point()`/`AimCrosshair` untouched.

## Directory Roles

| Path | Content |
| --- | --- |
| `autoload/` | Only `game_state.gd`. |
| `scenes/` | Godot `.tscn` scenes. |
| `scripts/` | GDScript logic/UI/presentation/pools. |
| `scripts/tools/` | Offline Python (stdlib): `balance_editor.py` (browser partition edit + highlight + server-side structural/type validation + atomic save + auto `.bak` — tune values with it, then minimal test set); `gen_balance_map.py` (regenerates `docs/BALANCE_MAP.md`: all `cfg()` call-site index + json/script dual-write reverse check — run after new/renamed keys); `generate_audio.py` (regenerates committed WAVs); `generate_enemy_sprites.py`/`generate_player_sprite.py`/`generate_mothership_sprite.py` (regenerate unit sprites, PIL, supersample + glow double-layer). |
| `assets/` | Sprites, audio/BGM, fonts, shaders (Meta HUD + crack bake). |
| `data/` | Runtime config + translation sources. |
| `test/` | Headless `.tscn + .gd` self-checks, perf bench, autoplay, screenshot tools. |
| `docs/` | EXIT_FLOW, AUDIT_REVIEW_SOP (parallel-audit methodology), ROADMAP, DESIGN_BASELINE, ENDLESS_BALANCE_PLAN, BALANCE_MAP (generated), system design docs, screenshots; `docs/archive/` frozen history (`PORTING_PARITY.md` + `EXECUTION_LOG.md` + originals). |
| `.agents/` | Agent entry docs linked from `AGENTS.md` (conventions, shell scripts, doc sync). |
| `packaging/` | `linux/` (install.sh/uninstall.sh/infiair.desktop), `windows/` (install.bat/uninstall.bat). |
