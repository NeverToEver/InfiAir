# AGENTS.md

## Project

InfiAir: single-player 2D top-down shooter; Godot 4.6.2 + C# (.NET 8, 全量迁移完成 2026-08-08, 零 GDScript), GL Compatibility. Remade from Python/Pygame `airwar-game` (`docs/archive/PORTING_PARITY.md`); runs standalone, no runtime dep on the original.

Game loop: auto-fire + wave spawns → milestone buff 3-choice → 4 rotating bosses + enrage → mothership supply/fire platform → return-to-base mid-run restock → same run continues. Score-only; no pickups.

- Entry: `project.godot` `run/main_scene = res://scenes/welcome.tscn` (accounts; main.tscn = battle scene, explicitly instanced by tests).
- Viewport 1920×1080, stretch `canvas_items` / `keep` aspect.
- Only autoload: `GameState` (`csharp/godot/GameState.cs`) — facade over 7 non-autoload services: `csharp/godot/BalanceService.cs`/`SaveManager.cs`/`SfxPlayer.cs`/`EntityManager.cs`（统一实体管理器，`GameState.Enemies`/`BindEnemy`/批量 API；`docs/ENTITY_MANAGER.md`） + `csharp/godot/FogEventManager.cs` (迷雾效果层/API 门面，挂 GameState 下，`GameState.FogEvents` 访问；`docs/FOG_EVENTS.md`) + `csharp/godot/GameEventManager.cs` (统一事件管理器，`GameState.Events`，批量管理全部随机事件——迷雾/遭遇统一注册/触发/信号；`docs/EVENT_MANAGER.md`) + `csharp/godot/UserDB.cs`（本地账户数据库，`GameState` 持有转发）; public API forwarded, callers/tests unaffected. M7 全量迁移 C#（2026-08-08）后 C# 侧统一经 `GameState.Instance` typed 访问（GameStateBridge 已删）。
- Game text: zh+en bilingual (UI default `zh`; new keys fill both `translations.csv` columns). Docs in English; `docs/AUDIT_VAULT.md` + `docs/archive/` in Chinese.
- `CLAUDE.md` = entry overview only; this file wins on conflict.
- Design intent / architecture baseline amended only via `docs/DESIGN_BASELINE.md`.

## Quick Reference

- **Stack:** Godot 4.6.2 .NET 版 (gl_compatibility) + .NET 8 全量 C#（M7 完成 2026-08-08：零 GDScript；热路径(对象池/弹幕)与场景绑定层保持纯 C# 单一实现）; no package manager. `scripts/tools/*.py` = offline tools, not runtime deps.
- **Export templates:** macOS `~/Library/Application Support/Godot/export_templates/4.6.2.stable/`（Linux `~/.local/share/godot/export_templates/`、Windows `%APPDATA%\Godot\export_templates\`）——本地 `./release.sh` 发布构建（导出 Linux/Windows 双平台）依赖，模板版本必须与引擎 4.6.2 stable 严格匹配；缺失时导出报 "No export template found"，需从官方下载器安装。发布也可走手动 GitHub Actions `release.yml`（远端官方模板构建）。
- **Run:** `./run.sh` (auto-locates engine; .NET 版优先: godot-mono → ~/.local/bin/godot-mono → godot → godot4 → ~/.local/bin/godot; 含 C# 工程必须用 .NET 版引擎). Minimal verify: `godot --headless --import --path .` → `godot --headless --path . --quit-after 300` → `res://test/smoke_test.tscn`; add `base_system_test.tscn` when touching saves/base/mothership; C# changes: `dotnet build` (zero warnings) + `dotnet test tests-csharp/`. Full commands: `docs/TESTING.md`.
- **Tunables:** `data/balance.json` via `scripts/tools/balance_editor.py` (top sections incl. `base_task` 刷新任务经济 + `fog_events` 迷雾事件参数); texts: `data/translations.csv`. Details: `docs/ARCHITECTURE.md` + `docs/FOG_EVENTS.md`.
- **CI/CD:** `.github/workflows/ci.yml` (mono 引擎 4.6.2 + Install .NET SDK 8 + dotnet build/test/format 门禁（2026-08-09 全量规范化后加 dotnet format 三工程 verify 零 diff 防回归闸） + headless import + main smoke + assertion scenes（权威计数 `docs/TESTING.md`，禁止硬编码；2026-08-09 V 系列加引擎错误日志扫描 + 场景数硬校验）+ BALANCE_MAP 生成器重跑零 diff 闸（M8，2026-08-06）; push/PR). Release: `export_presets.cfg` + `release.sh` (mono 引擎/模板) → GitHub Releases (not in repo); manual `.github/workflows/release.yml` (mono 引擎/模板 + dotnet build → export → tag `v<ver>` → release, syncs `config/version`). CI/CD changes sync these entry docs + `release.sh`; 政策口径: 仅官方 checkout/upload-artifact action + 官方 dotnet-install.sh 脚本 + 官方 Godot 引擎/模板, 禁止其他第三方依赖.

## Merge Gate & Testing

6 layers: ① zero-GDScript gate (M7d: 任何 `.gd` 文件即失败——全量迁移 C# 后禁止回归 GDScript) → ② C# gate: `dotnet build` (warnings-as-errors: zero warnings) + `dotnet test tests-csharp/` (xUnit) + `dotnet format` 三工程 verify 零 diff（2026-08-09 全量规范化后防回归） → ③ engine warnings (error-level zero tolerance — fails CI import; unsafe/untyped warns = cleanup list) → ④ compile+smoke → ⑤ all 55 assertion scenes. Commands, scene list, side effects, known failures: `docs/TESTING.md` (64 scenes: 55 assertion + `autoplay_test` probe + `perf_bench` + 7 windowed screenshot tools; `starfield_cs_test` 已计入 55).

## Architecture & Directory Roles

`scenes/welcome.tscn` = entry (accounts: login/register/guest/delete + difficulty/tutorial/settings/local leaderboard, `scripts/welcome.gd`); `scenes/main.tscn` = main tree & run container (runtime-created MetaHealthFX/AimFrameLayer/cutscenes/mothership/events); `scripts/main.gd` orchestrates; dynamic entities attach under Main for cleanup/test visibility. `csharp/core/` = 纯 .NET 类库 (零 Godot 依赖: 数据模型/纯逻辑/算法), `csharp/godot/` = Godot 绑定壳 (跨语言互操作层), `tests-csharp/` = xUnit 纯逻辑单测; 混编边界: C#↔GDScript 不可互相继承, 热路径(对象池/弹幕)与场景绑定层禁止跨语言. Full tree, per-script duties, directory roles: `docs/ARCHITECTURE.md`.

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
