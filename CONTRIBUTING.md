# 贡献指南（Contributing Guide）

感谢你愿意为 **InfiAir（无限空域）** 贡献代码！本项目是一个单机 2D 俯视空战射击游戏，基于 **Godot 4.6 .NET 版 + 全量 C#（.NET 8，零 GDScript）** 构建，采用 GL Compatibility 渲染器。全部贴图/音频/着色器均为程序化生成，零外部素材依赖。

> 本指南是贡献流程的入口；**开发约定总纲（技术栈、架构、代码风格、测试策略、文档同步要求）以 `AGENTS.md` 为权威**，首次接手请先读它。设计基线见 `docs/DESIGN_BASELINE.md`，路线图见 `docs/ROADMAP.md`。

---

## 环境准备

- **Godot 4.6.2 .NET 版 + .NET 8 SDK**（全量 C# 工程，标准版引擎无法构建）。macOS 用 `run.command` 或 `run.sh` 自动定位引擎；Linux 用 `run.sh`；Windows 用 `run.bat`。
- **游戏本体无包管理器、零第三方运行时依赖**（仅 `tests-csharp/` 经 NuGet 引入 xUnit，`dotnet test` 自动还原）——装好引擎与 SDK 后克隆即跑。

```bash
git clone https://github.com/NeverToEver/InfiAir.git
cd InfiAir
./run.sh            # 本地运行（自动探测 Godot，参数可透传，如 --editor）
```

## 开发流程

1. **建分支**：`git checkout -b feat/你的改动主题`（或 `fix/`、`docs/`、`test/`、`refactor/`、`perf/`、`chore/`）。
2. **改代码前先看约定**：`AGENTS.md` 的「开发约定」——C# 代码经 dotnet format 三工程规范化（CI 零 diff 防回归闸）；可调数值只改 `data/balance.json`（用 `scripts/tools/balance_editor.py`），**不要只改代码回退值**。
3. **本地验证**（最小必跑集）：

```bash
dotnet build                                 # C# 构建（CI 零警告门禁，需 .NET 8 SDK）
dotnet test tests-csharp/                    # xUnit 纯逻辑单测
godot --headless --import --path .           # 资源导入与脚本解析（需 .NET 版引擎）
godot --headless --path . --quit-after 300   # 运行时冒烟
godot --headless --path . res://test/smoke_test.tscn  # 主流程冒烟（自检断言全 PASS）
```

   涉及子系统时加跑对应专项场景（完整清单见 `docs/TESTING.md`）；改动数值键后重跑 `python3 scripts/tools/gen_balance_map.py` 刷新 `docs/BALANCE_MAP.md`。
4. **提交**：单主题提交，信息遵循项目风格——`类型: 简述——要点列表（日期）`，类型取 `fix`/`feat`/`docs`/`test`/`refactor`/`perf`/`chore`（可参考 `git log --oneline` 近期风格）。
5. **推送并开 PR**：PR 会自动触发 GitHub Actions CI（dotnet build/test/format → 零 GDScript 闸 → 无头导入 + 主场景冒烟 + 断言场景全量回归），**CI 全绿是合入门槛**。

## PR 检查清单

- [ ] C# 改动：`dotnet build` 零警告 + `dotnet test tests-csharp/` 全绿 + `dotnet format` 三工程零 diff
- [ ] 全部既有断言场景 0 FAIL（CI 会自动跑；本地可先行确认）
- [ ] 未破坏 `AGENTS.md`「全局不变量」（碰撞层、world_scale、view_world_rect、cfg、协程纪律、i18n、热路径、池防护）
- [ ] 新增用户可见文本走 `tr("UPPER_SNAKE_CASE_KEY")` 并同步 `data/translations.csv` 中英双列
- [ ] 新增/改名数值键后已重跑 `gen_balance_map.py`
- [ ] 改动设计意图/架构基线时已同步 `docs/DESIGN_BASELINE.md`；方向类决策登记 `docs/ROADMAP.md`
- [ ] 文档同步要求见 `AGENTS.md`「文档同步要求」节（含已完成工作压缩留档至 `docs/archive/EXECUTION_LOG.md` 的约定）

## 测试体系说明

- 场景测试是无头 C# 场景脚本（非单元测试框架；脚本在 `csharp/godot/tests/`，纯逻辑另有 `tests-csharp/` xUnit 单测），每个 `test/*.tscn` 以 `[PASS]`/`[FAIL]` 输出与退出码自检。
- 断言场景（`test/*_test.tscn` 中除 `autoplay_test` 探针外；权威计数见 `docs/TESTING.md`）+ `autoplay_test`（长时异常探针，本地按需跑）+ `perf_bench`（性能基准，需 `--fixed-fps 1000`）+ 窗口模式截图工具。
- 命令、专项场景清单、副作用与既有失败基线见 `docs/TESTING.md`。

## 许可

游戏代码与程序化生成素材为 **MIT License**（见 `LICENSE`）；内置字体 Noto Sans SC 为 **SIL OFL 1.1**（第三方声明见 `NOTICE`）。贡献即表示同意在 MIT 许可下分发你的改动。
