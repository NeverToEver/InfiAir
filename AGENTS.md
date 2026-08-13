# AGENTS.md

## Project

InfiAir: single-player 2D top-down shooter; Godot 4.6.2 + C# (.NET 8, gl_compatibility), 全量 C#（零 GDScript）。重制自 Python/Pygame `airwar-game`（`docs/archive/PORTING_PARITY.md`）; 独立运行，无原版依赖。

Game loop: auto-fire + wave spawns → milestone buff 3-choice → 4 rotating bosses + enrage → mothership supply/fire platform → return-to-base mid-run restock → same run continues. Score-only; no pickups.

- Entry: `project.godot` `run/main_scene = res://scenes/welcome.tscn`（账户/难度/教程/设置/本地排行榜; 战斗场景 `scenes/main.tscn` 由测试显式实例化）; Viewport 1920×1080, stretch `canvas_items` / aspect `keep`。
- 唯一 autoload: `GameState`（`csharp/godot/GameState.cs`）——编排门面，组合 8 域服务（Meta/Missions/Score/RunProgression/CombatState/Settings/InputBindings/UserSession）与 8 个非 autoload 服务; C# 侧统一经 `GameState.Instance` typed 访问。详见 `docs/ARCHITECTURE.md`。
- Text: zh+en bilingual（UI 默认 zh; 新 key 填 `data/translations.csv` 两列）; docs in English（`docs/AUDIT_VAULT.md` + `docs/archive/` in Chinese）。`CLAUDE.md` = entry overview only; 本文件优先。设计基线仅经 `docs/DESIGN_BASELINE.md` 修订。

## Quick Reference

- **Run:** `./run.sh`（自动定位引擎, .NET 版优先——C# 工程必须 .NET 版）。Minimal verify: `godot --headless --import --path .` → `godot --headless --path . --quit-after 300` → `res://test/smoke_test.tscn`; 触碰 saves/base/mothership 加 `res://test/base_system_test.tscn`; C# 改动: `dotnet build`（零警告）+ `dotnet test tests-csharp/` + `dotnet format --verify-no-changes`（三 csproj）。
- **Tunables:** `data/balance.json`（`scripts/tools/balance_editor.py`）; 文本 `data/translations.csv`。
- **Roslynator:** `tools/roslynator/`（gitignored; 重建: `dotnet tool install --tool-path tools/roslynator roslynator.dotnet.cli`）; 运行需 `dotnet` 在 PATH + `DOTNET_ROOT=~/.dotnet`。口径: `.agents/csharp-conventions.md`。
- **CI/CD:** `ci.yml` 两 job——fast-gate（C# build/test/format → 零 GDScript → import 警告 → smoke 300 帧 → 场景编译探针）覆盖全部 push(main+feature) 与 PR; full-regression（断言场景全量 + BALANCE_MAP 零 diff + 引擎错误日志 + 场景数校验）仅 main push/PR/workflow_dispatch; `paths-ignore: docs/** + *.md`。Release: `export_presets.cfg` + `release.sh`（本地导出需官方 **mono** 导出模板 `4.6.2.stable.mono`, 版本严格匹配; `InfiAir.sln` 必须入库，缺失会静默出空壳包）或手动 `release.yml`（远端官方模板构建）。政策: 仅官方 checkout/upload-artifact/cache action + dotnet-install.sh + Godot 引擎/模板, 禁其他第三方依赖。

## Merge Gate & Testing

6 层（fast-gate 跑 ①-③⑤, full-regression 跑 ④⑥; feature push 仅 fast-gate, main push/PR 全量）: ① C# gate（build warnings-as-errors + xUnit + format 零 diff）② zero-GDScript（任何 `.gd` 即失败）③ engine warnings 零容忍 ④ BALANCE_MAP 重跑零 diff ⑤ compile+smoke（main 300 帧 + 全 test/*.tscn 编译探针）⑥ 断言场景全量（权威计数 `docs/TESTING.md`）。

## Architecture & Directory Roles

`csharp/core/` = 纯 .NET 类库（零 Godot 依赖: 数据模型/纯逻辑/算法）; `csharp/godot/` = Godot 绑定层（全部运行时代码, 可引用 Core）; `tests-csharp/` = xUnit 纯逻辑单测。`welcome.tscn` 入口（账户, `csharp/godot/Welcome.cs`）; `main.tscn` 运行容器（`Main.cs` 编排; 运行时动态实体挂 Main 下, 便于清理/测试可见）。全树与逐脚本职责: `docs/ARCHITECTURE.md`。

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

- Rules & file map: [.agents/doc-sync.md](.agents/doc-sync.md)。
- **`docs/AUDIT_VAULT.md` is proprietary — never delete/merge**。
