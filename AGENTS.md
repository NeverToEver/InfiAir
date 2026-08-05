# AGENTS.md

## Project

InfiAir: single-player 2D top-down shooter; Godot 4.6 + GDScript, GL Compatibility. Remade from Python/Pygame `airwar-game` (`docs/archive/PORTING_PARITY.md`); runs standalone, no runtime dep on the original.

Game loop: auto-fire + wave spawns → milestone buff 3-choice → 4 rotating bosses + enrage → mothership supply/fire platform → return-to-base mid-run restock → same run continues. Score-only; no pickups.

- Entry: `project.godot` `run/main_scene = res://scenes/welcome.tscn` (2026-08-04 accounts; main.tscn = battle scene, explicitly instanced by tests).
- Viewport 1920×1080, stretch `canvas_items` / `keep` aspect.
- Only autoload: `GameState` (`autoload/game_state.gd`) — facade over 5 non-autoload services: `scripts/balance_service.gd`/`save_manager.gd`/`sfx_player.gd`/`entity_registry.gd` + `scripts/fog_event_manager.gd` (迷雾事件全局单例，挂 GameState 下，`GameState.fog_events` 访问；`docs/FOG_EVENTS.md`); public API forwarded, callers/tests unaffected.
- Game text: zh+en bilingual (UI default `zh`; new keys fill both `translations.csv` columns). Docs in English; `docs/AUDIT_VAULT.md` + `docs/archive/` in Chinese.
- `CLAUDE.md` = entry overview only; this file wins on conflict.
- Design intent / architecture baseline amended only via `docs/DESIGN_BASELINE.md`.

## Quick Reference

- **Stack:** Godot 4.6 (gl_compatibility, no .NET), pure GDScript; no package manager. `scripts/tools/*.py` = offline tools, not runtime deps.
- **Run:** `./run.sh` (auto-locates engine). Minimal verify: `godot --headless --import --path .` → `godot --headless --path . --quit-after 300` → `res://test/smoke_test.tscn`; add `base_system_test.tscn` when touching saves/base/mothership. Full commands: `docs/TESTING.md`.
- **Tunables:** `data/balance.json` via `scripts/tools/balance_editor.py` (top sections incl. `base_task` 刷新任务经济 + `fog_events` 迷雾事件参数); texts: `data/translations.csv`. Details: `docs/ARCHITECTURE.md` + `docs/FOG_EVENTS.md`.
- **CI/CD:** `.github/workflows/ci.yml` (headless import + main smoke + 43 assertion scenes; push/PR). Release: `export_presets.cfg` + `release.sh` → GitHub Releases (not in repo); manual `.github/workflows/release.yml` (export → tag `v<ver>` → release, syncs `config/version`). CI/CD changes sync these entry docs + `release.sh`; no 3rd-party deps beyond official checkout action + Godot binary/templates.

## Merge Gate & Testing

5 layers: ① `gdformat --check` (w=140) → ② `gdlint` → ③ engine warnings (error-level zero tolerance — fails CI import; unsafe/untyped warns = cleanup list) → ④ compile+smoke → ⑤ all 41 assertion scenes. New `.gd` files must be gdformat-formatted; rule rationale in config comments/`docs/AUDIT_VAULT.md`; relaxing rules syncs configs + these entry docs. Commands, scene list, side effects, known failures: `docs/TESTING.md` (50 scenes: 41 assertion + `autoplay_test` probe + `perf_bench` + 7 windowed screenshot tools).

## Architecture & Directory Roles

`scenes/welcome.tscn` = entry (accounts: login/register/guest/delete + difficulty/tutorial/settings/local leaderboard, `scripts/welcome.gd`); `scenes/main.tscn` = main tree & run container (runtime-created MetaHealthFX/AimFrameLayer/cutscenes/mothership/events); `scripts/main.gd` orchestrates; dynamic entities attach under Main for cleanup/test visibility. Full tree, per-script duties, directory roles: `docs/ARCHITECTURE.md`.

## Conventions

Global invariants: collision layers / `world_scale` / `view_world_rect()` / `cfg()` / coroutine discipline / i18n / hot paths / pool guards. Details:

- [GDScript & Lifecycle](.agents/gdscript-lifecycle.md)
- [Balance & Config](.agents/balance-config.md)
- [Collision, Damage & View](.agents/collision-view.md)
- [UI, Text & Navigation](.agents/ui-navigation.md)
- [Performance & Object Lifecycle](.agents/performance.md)
- [Shell Scripts](.agents/shell-scripts.md)
- [Persistence & Security](.agents/persistence-security.md)

## Doc Sync

- Rules & file map (ROADMAP / DESIGN_BASELINE / EXIT_FLOW / BALANCE_MAP / AUDIT_VAULT / archive): [.agents/doc-sync.md](.agents/doc-sync.md).
- **`docs/AUDIT_VAULT.md` is proprietary — never delete/merge** (see doc-sync link).
