# AGENTS.md

## Project

InfiAir: single-player 2D top-down shooter; Godot 4.6.2 + C# (.NET 8, 零 GDScript), GL Compatibility. Remade from Python/Pygame `airwar-game` (`docs/archive/PORTING_PARITY.md`); runs standalone, no runtime dep on the original.

Game loop: auto-fire + wave spawns → milestone buff 3-choice → 4 rotating bosses + enrage → mothership supply/fire platform → return-to-base mid-run restock → same run continues. Score-only; no pickups.

- Entry: `project.godot` `run/main_scene = res://scenes/welcome.tscn` (accounts; main.tscn = battle scene, explicitly instanced by tests).
- Viewport 1920×1080, stretch `canvas_items` / `keep` aspect.
- Only autoload: `GameState` (`csharp/godot/GameState.cs`) — facade over 8 non-autoload services: `BalanceService.cs` / `SaveManager.cs` / `SfxPlayer.cs` / `EntityManager.cs`（统一实体管理器；`docs/ENTITY_MANAGER.md`）/ `FogEventManager.cs`（迷雾效果层门面；`docs/FOG_EVENTS.md`）/ `GameEventManager.cs`（统一事件管理器；`docs/EVENT_MANAGER.md`）/ `UserDB.cs`（本地账户）/ `ProgressionInterop.cs`（进程曲线桥），均在 `csharp/godot/`。C# 侧统一经 `GameState.Instance` typed 访问。**GameState 拆域收官 (2026-08-11)**：另组合 8 个域服务（`MetaService`/`MissionsService`/`ScoreService`/`RunProgressionService`/`CombatStateService`/`SettingsService`/`InputBindingsService`/`UserSessionService`，RefCounted + 构造注入 + 信号 C# 事件 + 门面重发），GameState 收敛为编排门面；详见 `docs/ARCHITECTURE.md`。
- Game text: zh+en bilingual (UI default `zh`; new keys fill both `translations.csv` columns). Docs in English; `docs/AUDIT_VAULT.md` + `docs/archive/` in Chinese.
- `CLAUDE.md` = entry overview only; this file wins on conflict.
- Design intent / architecture baseline amended only via `docs/DESIGN_BASELINE.md`.

## Quick Reference

- **Stack:** Godot 4.6.2 .NET 版 (gl_compatibility) + .NET 8 全量 C#（热路径(对象池/弹幕)与场景绑定层为纯 C# 单一实现）; no package manager. `scripts/tools/*.py` = offline tools, not runtime deps.
- **Export templates:** `~/.local/share/godot/export_templates/4.6.2.stable/`（macOS `~/Library/Application Support/Godot/export_templates/`、Windows `%APPDATA%\Godot\export_templates\`）——本地 `./release.sh` 发布构建（Linux/Windows 双平台）依赖，模板版本必须与引擎严格匹配；缺失时导出报 "No export template found"。发布也可走手动 GitHub Actions `release.yml`（远端官方模板构建）。
- **Run:** `./run.sh` (auto-locates engine; .NET 版优先: godot-mono → ~/.local/bin/godot-mono → godot → godot4 → ~/.local/bin/godot → macOS /Applications/Godot.app; 含 C# 工程必须用 .NET 版引擎). Minimal verify: `godot --headless --import --path .` → `godot --headless --path . --quit-after 300` → `res://test/smoke_test.tscn`; add `base_system_test.tscn` when touching saves/base/mothership; C# changes: `dotnet build` (zero warnings) + `dotnet test tests-csharp/` + `dotnet format --verify-no-changes` (three csproj). Full commands: `docs/TESTING.md`.
- **Tunables:** `data/balance.json` via `scripts/tools/balance_editor.py`; texts: `data/translations.csv`. Details: `docs/ARCHITECTURE.md`.
- **Static analysis:** Roslynator CLI 本地留存于 `tools/roslynator/`（已 gitignore，仅本地；`dotnet tool install --tool-path tools/roslynator roslynator.dotnet.cli` 可重建）。用法 `DOTNET_ROOT=~/.dotnet tools/roslynator/roslynator analyze InfiAir.csproj`；应用口径见 `.agents/csharp-conventions.md` §Build & Gate。
- **CI/CD:** `.github/workflows/ci.yml` 分两 job——**fast-gate**（~8min：C# build/test/format 三工程门禁 → 零 GDScript 门 → import 引擎警告门 → main smoke 300 帧 → 全场景编译探针）覆盖全部 push（main + feature/*）与 PR；**full-regression**（断言场景全量 + BALANCE_MAP 重跑零 diff 闸 + 引擎错误日志扫描 + 场景数硬校验 + flake 重试；权威计数 `docs/TESTING.md`）仅 main push / PR / workflow_dispatch；`paths-ignore: docs/** + *.md`（纯文档不触发）；dotnet SDK/NuGet/Godot mono 引擎经 actions/cache 缓存；同分支新推送取消旧运行. Release: `export_presets.cfg` + `release.sh` → GitHub Releases (not in repo); manual `release.yml` (dotnet build → export → tag `v<ver>` → release, syncs `config/version`). CI/CD changes sync these entry docs + `release.sh`; 政策口径: 仅官方 checkout/upload-artifact/cache action + 官方 dotnet-install.sh 脚本 + 官方 Godot 引擎/模板, 禁止其他第三方依赖.

## Merge Gate & Testing

6 layers (CI order：fast-gate 跑 ①-③⑤，full-regression 跑 ④⑥——feature push 仅 fast-gate，main push/PR 全量): ① C# gate: `dotnet build` (warnings-as-errors) + `dotnet test tests-csharp/` (xUnit) + `dotnet format` 三工程 verify 零 diff → ② zero-GDScript gate (任何 `.gd` 文件即失败) → ③ engine warnings (error-level zero tolerance) → ④ BALANCE_MAP 生成器重跑零 diff 闸 → ⑤ compile+smoke (main 300 frames + 全 test/*.tscn 编译探针) → ⑥ all assertion scenes（权威计数 `docs/TESTING.md`；含引擎错误日志扫描 + 场景数硬校验）. Commands, scene list, side effects, known failures: `docs/TESTING.md`.

## Architecture & Directory Roles

`scenes/welcome.tscn` = entry (accounts: login/register/guest/delete + difficulty/tutorial/settings/local leaderboard, `csharp/godot/Welcome.cs`); `scenes/main.tscn` = main tree & run container (runtime-created MetaHealthFX/AimFrameLayer/cutscenes/mothership/events); `csharp/godot/Main.cs` orchestrates; dynamic entities attach under Main for cleanup/test visibility. `csharp/core/` = 纯 .NET 类库 (零 Godot 依赖: 数据模型/纯逻辑/算法), `csharp/godot/` = Godot 绑定层 (全部游戏运行时代码，可引用 Core), `tests-csharp/` = xUnit 纯逻辑单测; 纯逻辑下沉 `csharp/core/`, 热路径保持每帧零托管分配 (约定见 `.agents/csharp-conventions.md`). Full tree, per-script duties: `docs/ARCHITECTURE.md`.

## Conventions

Global invariants: collision layers / `world_scale` / `ViewWorldRect()` / `Cfg()` / coroutine discipline / i18n / hot paths / pool guards. Details:

- [C# Conventions](.agents/csharp-conventions.md)
- [Lifecycle, Input & Test Discipline](.agents/lifecycle.md)
- [Balance & Config](.agents/balance-config.md)
- [Collision, Damage & View](.agents/collision-view.md)
- [UI, Text & Navigation](.agents/ui-navigation.md)
- [Performance & Object Lifecycle](.agents/performance.md)
- [Shell Scripts](.agents/shell-scripts.md)
- [Persistence & Security](.agents/persistence-security.md)

## Doc Sync

- Rules & file map (ROADMAP / DESIGN_BASELINE / EXIT_FLOW / BALANCE_MAP / AUDIT_VAULT / archive): [.agents/doc-sync.md](.agents/doc-sync.md).
- **`docs/AUDIT_VAULT.md` is proprietary — never delete/merge** (see doc-sync link).
