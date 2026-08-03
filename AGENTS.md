# AGENTS.md

## Project

InfiAir: single-player 2D top-down shooter; Godot 4.6 + GDScript, GL Compatibility. Remade from Python/Pygame `airwar-game` (`docs/archive/PORTING_PARITY.md`); runs standalone, no runtime dep on the original.

Game loop: auto-fire + wave spawns → milestone buff 3-choice → 3 rotating bosses + enrage → mothership supply/fire platform → return-to-base mid-run restock → same run continues. Score-only; no pickups.

- Entry: `project.godot` `run/main_scene = res://scenes/main.tscn`.
- Viewport 1920×1080, stretch `canvas_items` / `keep` aspect.
- Only autoload: `GameState` (`autoload/game_state.gd`) — facade over 4 non-autoload services: `scripts/balance_service.gd`/`save_manager.gd`/`sfx_player.gd`/`entity_registry.gd`; public API forwarded, callers/tests unaffected.
- UI & primary docs in Chinese; **all new game text must be zh+en bilingual**.
- `CLAUDE.md` = entry overview only; this file wins on conflict.
- Design intent / architecture baseline amended only via `docs/DESIGN_BASELINE.md`; sync it when changing either.

## Stack & Config

- Godot 4.6 (gl_compatibility, no .NET), pure GDScript. `scripts/tools/*.py` = offline tools (balance editor, doc gen, asset gen), not runtime deps.
- Tunables: `data/balance.json` (via `scripts/tools/balance_editor.py`); texts: `data/translations.csv`. Details: `docs/ARCHITECTURE.md`.
- No package manager. CI: `.github/workflows/ci.yml` (headless import + main smoke + 37 assertion scenes; push/PR). Release: `export_presets.cfg` + `release.sh` → GitHub Releases (not in repo); manual `.github/workflows/release.yml` (export → tag `v<ver>` → release, syncs `config/version`). CI/CD changes sync this file + `release.sh`; no 3rd-party deps beyond official checkout action + Godot binary/templates.

## Run & Verify

```bash
./run.sh                          # local run (auto-locates engine)
godot --headless --import --path .        # import & script parse
godot --headless --path . --quit-after 300
godot --headless --path . res://test/smoke_test.tscn
godot --headless --path . res://test/base_system_test.tscn  # add when touching saves/base/mothership
```

Minimal set: `--import`, `--quit-after 300`, `smoke_test.tscn`. Merge gate (5 layers, `docs/TESTING.md`): ① `gdformat --check` (w=140) → ② `gdlint` → ③ engine warnings (error-level zero tolerance — fails CI import; unsafe/untyped warns = cleanup list) → ④ compile+smoke → ⑤ all 37 assertion scenes. New `.gd` files must be gdformat-formatted; rule rationale in config comments/`docs/AUDIT_VAULT.md`; relaxing rules syncs configs + this file.

## Runtime Architecture

`scenes/main.tscn` = main tree & run container (Starfield/Camera2D, Player, Spawner, BulletPool/EnemyPool, HUD + page UIs, BackNavigator; runtime-created MetaHealthFX/AimFrameLayer/cutscenes/mothership/events). `scripts/main.gd` orchestrates. Dynamic entities attach under Main for cleanup/test visibility. Full tree & script duties: `docs/ARCHITECTURE.md`.

## Directory Roles

| Path | Role |
| --- | --- |
| `autoload/` | Autoloads; only `game_state.gd`. |
| `scenes/` | Godot `.tscn` scenes. |
| `scripts/` | GDScript logic/UI/effects/pools. |
| `scripts/tools/` | Offline Python tools. |
| `assets/` | Sprites, audio/BGM, fonts, shaders. |
| `data/` | Runtime config (`balance.json`) + translations. |
| `test/` | Headless self-checks, perf bench, autoplay, screenshots (`docs/TESTING.md`). |
| `docs/` | Audit vault, design docs, roadmap, BALANCE_MAP, archive. |
| `packaging/` | Install/uninstall scripts (linux/, windows/). |

## Conventions

### GDScript & Lifecycle

- Godot 4 official style: Tab indent, type annotations, `CONSTANT_CASE`, `_` private prefix, `signal.emit()`/`connect()`.
- `setup()` runs before `_ready()`; no `@onready` there — use `$node/path`.
- Don't touch existing autoloads/input mappings for unrelated needs. Inputs (`project.godot`): move, `boost` (Shift), `fine_move` (Ctrl), `dash` (Space), `dock` (H), `homecoming` (B), `give_up` (K), `buff_panel` (L), `parry` (F, arcane shield, fairness mech #4), `restart` (R). Joypad defaults bound at runtime by `GameState._bind_joypad_defaults()` (P0-1: keyboard only in project.godot; deadzone via `set_joy_deadzone()`). PS detect via `GameState.is_ps_guid()` (vendor 054c; ✕○□△/L1-R1 labels).
- Tutorial isolates run state/saves; restore `Engine.time_scale = 1` on exit. Keep refs to runtime-created nodes; never rely on auto-generated node names.
- After adding a `class_name` script, run `godot --headless --import --path .` to refresh class cache, else "Identifier not declared" compile errors break the host scene.
- No `await get_tree().create_timer()` or timer-hung coroutines: unfinished coroutine state leaks on exit along with referenced resources. Use one-shot `Timer` nodes + signals (see `spawner.gd` `_schedule()`).

### Balance & Config

- Change tunables only in `data/balance.json`, not script fallbacks (script defaults must match for missing/corrupt JSON). Prefer `scripts/tools/balance_editor.py`; after edits run `scripts/tools/gen_balance_map.py`, check `docs/BALANCE_MAP.md` "bidirectional lookup" sections for new mismatches.
- Read nested config via `GameState.cfg("player.fuel.drain", default)`. Cache in `_ready()`/init; never per-frame JSON lookup on hot paths.
- `GameState` loads balance.json at startup; missing/unparseable → script defaults.
- Single hull-scaling lever: top-level `world_scale` in balance.json (0.4 since 2026-07-31, from 1/3; cached `GameState.world_scale`). Hull-size family (sprite scale, collision radius, muzzle/dock/turret/tow offsets, bullet/explosion/gate/laser fx) stored as **design values** (1.0 baseline) in json/tscn/script fallbacks; entities apply `* world_scale` in `_ready()`/`setup()`. Gameplay-range family (AoE, lock/clear radius, slow ring) & indicators/cutscenes/UI don't scale. Classify new size values; never bypass the lever with literal runtime values.
- Idempotent assignment (`radius = design * world_scale`), never `*=` (sub_resources like CircleShape2D shared across instances compound per instance). Runtime-resized shapes (enemy.tscn — normal vs elite radii differ) need `resource_local_to_scene = true`.

### Collision, Damage & View

- Layers: 1=`player`, 2=`player_bullet`, 3=`enemy` (incl. boss), 4=`enemy_bullet`. Player bullets resolve vs `enemy` group; enemy bullets/entities vs `player_hitbox` group.
- Player hit only via `Player/Hitbox` Area2D (design r=7 × world_scale → runtime 2.8). Body circle r=22 has no collision use (mask 0) — never use for hit detection.
- Bullets: `scenes/bullet.tscn`, faction in `setup()`; enemy visual scale `effects.enemy_bullet_visual_scale`, player `effects.bullet_visual_scale` (design × world_scale); `Bullet.homing_target` supported (reset in `activate()`). Explosions via `Explosion.spawn_at()`, not ad-hoc particle setups.
- Zoom & window size: independent profile settings. Camera fixed at (960, 540), zoom only; all edge/offscreen/spawn/visibility math via `GameState.view_world_rect()`, never hardcoded 0..1920/0..1080.
- Mouse lock (profile `mouse_lock`, default on): `scripts/mouse_trap.gd` (on Main, `PROCESS_MODE_ALWAYS`) warps mouse inside via `Input.warp_mouse()` while crosshair active (unpaused + cursor hidden) + window focused (`mouse_exited` + per-frame `_process`). Released in non-crosshair states (pause/buff/base/results/cutscene/start) and on focus loss (mouse can leave window, e.g. close via title bar).

### UI, Text & Navigation

- All visible text via `tr("UPPER_SNAKE_CASE_KEY")`; new keys go to `data/translations.csv` zh + en columns, then re-import to build `.translation`. Dynamic text uses `%d`/`%s` placeholder keys.
- Locale switch only via `GameState.set_locale("zh"/"en")`; UI refreshes on `locale_changed`.
- Styling via `scripts/ui_theme.gd`: palette tokens, type scale, `make_label()`, `make_button()`, `make_toggle_button()`, `make_section_header()`, `make_page_shell()` (dim overlay + centered margin + title/subtitle/content/buttons; all modals), `animate_modal_open()`, `add_button_motion()` (auto on buttons), `make_buff_tile()` (46×46 glyph + stack badge; collapsed row bottom-right: latest 4 + overflow +N; L opens right scroll list; Esc closes via BackNavigator), intro-anim helpers. Widgets: `ui_chamfered_panel.gd`, `ui_segmented_bar.gd` (partial last segment), `ui_buff_icons.gd` (16 buff glyphs + category colors; HUD dock + buff cards), start decor `start_radar.gd`/`start_backdrop.gd`. New pages use `make_page_shell()`, ≤1 primary button; no hand-written colors or Label/Button boilerplate.
- Global skill `game-ui-ux` (`~/.kimi-code/skills/game-ui-ux/`, from `gamedev-skills/awesome-gamedev-agent-skills`, Apache-2.0): cross-engine UI/UX guidance (responsive layout, resolution/aspect scaling, safe areas, kbd/gamepad focus, screen stack, event-driven HUD); complements `godot-ui-control`. Use when designing/refactoring HUD/menus/overlays; follow `ui_theme.gd`.
- Pausing UIs (buff/pause/results) need `process_mode = Always` + `get_tree().paused`.
- Back/exit centralized in `BackNavigator`. Pages don't consume `ui_cancel` (except settings key-capture); **right mouse = fixed back/cancel** (detected by BackNavigator, not rebindable, same route as Esc). New page levels register in `decide_back_action()` + sync `docs/EXIT_FLOW.md`.
- BGM: set `stream.loop_mode = LOOP_FORWARD` only; never set loop_begin/loop_end or stop BGM in `_exit_tree()` (leaks playback instances).

### Performance & Object Lifecycle

- Bullets via `GameState.bullet_pool.fire()`; pool ref cleanup handled on exit-tree.
- When editing pools keep `_active`/`_repooling` guards: Godot 4.6 `reparent()` fires `_exit_tree()`; repool reparent must be wrapped in `_repooling` or `forget()` wrongly removes the object from the idle pool. Run `test/pool_reuse_test.tscn` after changes.
- Enemies pooled via `GameState.enemy_pool.spawn()` (waves, boss-3 minions, formations; `USE_POOL=false` degrades to direct instantiation as perf A/B switch). Pooled entities reset/register/emit death in `reactivate()`/`deactivate()`; don't free or bypass pool objects externally. Details: `docs/ARCHITECTURE.md`.
- Hot paths: no per-frame `get_nodes_in_group()` — use `GameState.enemies`/`player_ref`/`player_hitbox` registries. `Enemy` movement uses `Enemy.sin_fast()`/`cos_fast()` lookup tables; no direct trig in `_physics_process()`.
- HUD gauges poll ~0.1s throttled, relayout only on text/value change; prefer `GameState` signal-driven updates.

### Shell Scripts

- Root scripts: `run.sh`/`run.command`/`run.bat` (launch; same arg protocol, pass engine args like `--editor`), `release.sh`, `packaging/` (dual-platform install/uninstall). Structure from `bentsolheim/public-skills` bash skill v2.0.0 (only shell-maintenance skill in ecosystem), adapted — its "no `set -e`" stance rejected (conflicts with project practice):
  - Errors: `set -euo pipefail` by default (existing `release.sh`/`run.sh`/`packaging/linux/*.sh` match); errors → stderr (`>&2`) with context, non-zero exit. `run.command` (macOS double-click keeps window/output on error) uses explicit `$?` — deliberate exception.
  - Structure: arg/multi-function/interactive scripts use `main()` + guard (`[[ "${BASH_SOURCE[0]}" == "${0}" ]] && main "$@"`) + `usage()` heredoc; single-purpose functions, `local` params. Simple scripts (<30 lines, no args, linear) skip main() but keep purpose comment, exit codes, quoted vars.
  - Args: `while`+`case`; unknown option → error + `usage()`; support `--help`/`--version`.
  - Deps/output: launchers detect engine + version (Godot 4.6+, see run.sh candidates/`version_ok`); `command -v` external tools. Colors ok but respect `NO_COLOR`.
  - Verify: `bash -n` + actually run (e.g. `./run.command --headless --quit-after 300`).

## Testing

Each `test/*.tscn` self-checks via `[PASS]`/`[FAIL]` + exit code (not a unit framework). 46 scenes: 37 assertion scenes + `autoplay_test` (probe), `perf_bench`, 7 windowed screenshot tools. Commands, list, side effects, known failures: `docs/TESTING.md`.

## Persistence & Security

- Run save `user://savegame.json`, out-of-run profile `user://profile.json`; both managed by GameState with version fields. Profile: high score, local leaderboard, difficulty, keybinds, locale, zoom, window size, tutorial state, joypad params (`joy_aim_speed`/`joy_deadzone`).
- Corrupt JSON isolated as `<file>.corrupt`, notified to start screen via `save_corrupt`/`profile_corrupt`. Don't bypass recovery.
- No networking, third-party plugins, remote services, keys, or credentials. Only local `user://` persistence + offline asset generation. `balance_editor.py` listens on 127.0.0.1 only; not runtime.
- `.gitignore` excludes import cache & exports (`builds/` etc.). `export_presets.cfg` re-committed 2026-07-30 — preset changes must review `release.sh` + `packaging/`. Future CI/deploy additions: reviewable workflows + release notes first, then document here.

## Doc Sync

- Direction/phase/pause-resume decisions → `docs/ROADMAP.md` (single source of truth).
- Design intent / play rules / architecture baseline → `docs/DESIGN_BASELINE.md` + affected design docs.
- Back/exit hierarchy, exit cleanup, platform-back handling → `docs/EXIT_FLOW.md` + run back-nav tests.
- New/renamed balance keys or `cfg()` changes → run `python3 scripts/tools/gen_balance_map.py` to regenerate `docs/BALANCE_MAP.md`.
- **`docs/AUDIT_VAULT.md` (code audit archive) is proprietary — never delete/merge**: logs all code-quality issues, fix guidance, post-fix efficacy records, work time/areas. Append new findings; backfill fix records + update status summary on landing. No cleanup/archive may remove it.
- Completed plans/review docs: move full text to `docs/archive/`, log entry in `docs/archive/EXECUTION_LOG.md` (date/commit/summary/key decisions & lessons/link), delete from `docs/` top level, update references. Archived internal `docs/xxx` links are pre-archive snapshots, may be broken.
- Structure/commands/test-strategy/config-location changes → keep this file current as the true entry doc for agents; architecture/config → `docs/ARCHITECTURE.md`; test commands/strategy → `docs/TESTING.md`.
