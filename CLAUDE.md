# CLAUDE.md

Entry overview for Claude Code. **权威约定: `AGENTS.md` + `.agents/*`** — read before changes. Direction/plans: `docs/ROADMAP.md`。**`docs/AUDIT_VAULT.md` is proprietary — never remove**; consult before core-logic changes. Full commands & assertion-scene list: `docs/TESTING.md`。

## Project

InfiAir: 2D top-down shooter; Godot 4.6.2 + C# (.NET 8, zero GDScript, gl_compatibility), .NET 版引擎（`godot-mono`）必需。Entry `scenes/welcome.tscn`（账户）; battle `scenes/main.tscn`; 1920×1080（stretch `canvas_items`/`keep`）。唯一 autoload `GameState`; 纯逻辑 `csharp/core/`（零 Godot 依赖, xUnit 于 `tests-csharp/`）, 游戏代码 `csharp/godot/`。

## Commands

```bash
G=godot-mono   # C# 工程必须用 .NET 版引擎（回退: ~/.local/bin/godot-mono / PATH godot）
$G --headless --import --path .                              # import + 脚本解析
$G --headless --path . --quit-after 300                      # 300 帧运行检查
$G --headless --path . res://test/smoke_test.tscn            # 主流程 — 改动后必跑
$G --headless --path . res://test/base_system_test.tscn      # saves/base/mothership 改动加跑
dotnet build && dotnet test tests-csharp/ && dotnet format --verify-no-changes   # C# 改动三件套
```
