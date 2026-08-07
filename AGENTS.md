# AGENTS.md

## Project

InfiAir: single-player 2D top-down shooter; Godot 4.6 + GDScript, GL Compatibility. Remade from Python/Pygame `airwar-game` (`docs/archive/PORTING_PARITY.md`); runs standalone, no runtime dep on the original.

Game loop: auto-fire + wave spawns → milestone buff 3-choice → 4 rotating bosses + enrage → mothership supply/fire platform → return-to-base mid-run restock → same run continues. Score-only; no pickups.

- Entry: `project.godot` `run/main_scene = res://scenes/welcome.tscn` (accounts; main.tscn = battle scene, explicitly instanced by tests).
- Viewport 1920×1080, stretch `canvas_items` / `keep` aspect.
- Only autoload: `GameState` (`autoload/game_state.gd`) — facade over 7 non-autoload services: `scripts/balance_service.gd`/`save_manager.gd`/`sfx_player.gd`/`entity_manager.gd`（统一实体管理器，`GameState.enemies`/`bind_enemy`/批量 API；`docs/ENTITY_MANAGER.md`） + `scripts/fog_event_manager.gd` (迷雾效果层/API 门面，挂 GameState 下，`GameState.fog_events` 访问；`docs/FOG_EVENTS.md`) + `scripts/event_manager.gd` (统一事件管理器，`GameState.events`，批量管理全部随机事件——迷雾/遭遇统一注册/触发/信号；`docs/EVENT_MANAGER.md`) + `scripts/user_db.gd`（本地账户数据库，`GameState` 持有转发，2026-08-06 审计：原清单漏第 7 个服务）; public API forwarded, callers/tests unaffected.
- Game text: zh+en bilingual (UI default `zh`; new keys fill both `translations.csv` columns). Docs in English; `docs/AUDIT_VAULT.md` + `docs/archive/` in Chinese.
- `CLAUDE.md` = entry overview only; this file wins on conflict.
- Design intent / architecture baseline amended only via `docs/DESIGN_BASELINE.md`.

## Quick Reference

- **Stack:** Godot 4.6 (gl_compatibility, no .NET), pure GDScript; no package manager. `scripts/tools/*.py` = offline tools, not runtime deps.
- **Export templates:** macOS `~/Library/Application Support/Godot/export_templates/4.6.2.stable/`（Linux `~/.local/share/godot/export_templates/`、Windows `%APPDATA%\Godot\export_templates\`）——本地 `./release.sh` 发布构建（导出 Linux/Windows 双平台）依赖，模板版本必须与引擎 4.6.2 stable 严格匹配；缺失时导出报 "No export template found"，需从官方下载器安装。发布也可走手动 GitHub Actions `release.yml`（远端官方模板构建）。
- **Run:** `./run.sh` (auto-locates engine). Minimal verify: `godot --headless --import --path .` → `godot --headless --path . --quit-after 300` → `res://test/smoke_test.tscn`; add `base_system_test.tscn` when touching saves/base/mothership. Full commands: `docs/TESTING.md`.
- **Tunables:** `data/balance.json` via `scripts/tools/balance_editor.py` (top sections incl. `base_task` 刷新任务经济 + `fog_events` 迷雾事件参数); texts: `data/translations.csv`. Details: `docs/ARCHITECTURE.md` + `docs/FOG_EVENTS.md`.
- **CI/CD:** `.github/workflows/ci.yml` (headless import + main smoke + assertion scenes（权威计数 `docs/TESTING.md`，禁止硬编码）+ BALANCE_MAP 生成器重跑零 diff 闸（M8，2026-08-06）; push/PR). Release: `export_presets.cfg` + `release.sh` → GitHub Releases (not in repo); manual `.github/workflows/release.yml` (export → tag `v<ver>` → release, syncs `config/version`). CI/CD changes sync these entry docs + `release.sh`; no 3rd-party deps beyond official checkout action + Godot binary/templates.

## Merge Gate & Testing

5 layers: ① `gdformat --check` (w=140) → ② `gdlint` → ③ engine warnings (error-level zero tolerance — fails CI import; unsafe/untyped warns = cleanup list) → ④ compile+smoke → ⑤ all 47 assertion scenes. New `.gd` files must be gdformat-formatted; rule rationale in config comments/`docs/AUDIT_VAULT.md`; relaxing rules syncs configs + these entry docs. Commands, scene list, side effects, known failures: `docs/TESTING.md` (56 scenes: 47 assertion + `autoplay_test` probe + `perf_bench` + 7 windowed screenshot tools).

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
