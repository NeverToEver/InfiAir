<div align="center">

# ✈️ InfiAir · 无限空域

**2D 俯视弹幕空战 · 单机街机射击 · Godot 4.6.2 .NET + C# 全量实现**

[English](./README.en.md) · **简体中文**

[![Godot](https://img.shields.io/badge/Godot-4.6.2-478cbf?logo=godotengine&logoColor=white)](https://godotengine.org/)
[![C#](https://img.shields.io/badge/C%23-100%25-512BD4?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![Release](https://img.shields.io/github/v/release/NeverToEver/InfiAir?color=orange&label=Release)](https://github.com/NeverToEver/InfiAir/releases)
[![CI](https://github.com/NeverToEver/InfiAir/actions/workflows/ci.yml/badge.svg)](https://github.com/NeverToEver/InfiAir/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](./CONTRIBUTING.md)
[![Discussions](https://img.shields.io/badge/Discussions-Join-8250df?logo=github&logoColor=white)](https://github.com/NeverToEver/InfiAir/discussions)

<p align="center">
  <a href="#-项目简介">项目简介</a> ·
  <a href="#-功能特性">功能特性</a> ·
  <a href="#-截图">截图</a> ·
  <a href="#-快速开始">快速开始</a> ·
  <a href="#-操作">操作</a> ·
  <a href="#-技术栈与架构">技术栈与架构</a> ·
  <a href="#-测试与-ci">测试与 CI</a> ·
  <a href="#-社区与贡献">社区与贡献</a>
</p>

<img src="./docs/screenshots/gameplay.png" alt="InfiAir 游戏画面" width="760">

</div>

## 🌟 项目简介

InfiAir 是一款**单机得分制街机空战射击游戏**：驾驶战机迎战波次化敌潮，在分数里程碑三选一构筑 Buff，挑战轮换 Boss；随时返航基地中场整备，再杀回同一局——**死亡是唯一终局**。难度曲线线性无封顶：活得越久、杀得越多，敌潮越强。

项目重制自 Python/Pygame 作品 [airwar-game](https://github.com/NeverToEver/airwar-game)，现已独立演进。**全部贴图与音频均为程序化生成**，零外部素材依赖。代码库已全量迁移至 C#（零 GDScript），纯逻辑与引擎绑定严格分层，热路径遵循每帧零托管分配纪律。

## ✨ 功能特性

- 🔥 **无尽波次 + 线性无封顶难度**：敌潮随分数解锁新机型与精英，击杀/时长持续推高压力。
- 🃏 **1/3 Buff 构筑**：分数里程碑三选一，19 种可叠加 Buff（伤害 / 射速 / 散射 / 穿透 / 爆炸 / 吸血 / 护甲 / 闪避 / 相位冲刺 / 激光光束等）。
- 👾 **Boss 轮换与事件**：4 型 Boss 轮换、HP 阶段模式表与差异化狂暴；迷雾、遭遇、精英炮塔、轰炸编队等随机事件。
- 🚀 **母舰与返航整备**：蓄力召唤母舰、驻留驾驶、轨道打击清场；返航基地维修补给后再出击。
- 💥 **连击计分与防御保底**：3 秒连击窗口最高 ×2.0 得分；低血时防御 Buff 加权并保底出现。
- 📈 **跨局成长**：死亡结算科技点，用于研究所解锁开局预置 Buff（有界成长，不破坏必死曲线）。
- 🔒 **本地账户与安全存档**：用户级存档、PBKDF2 密码派生、原子写与损坏隔离、本地排行榜。
- 🧩 **全 C# 工程**：Godot 4.6.2 .NET + .NET 8，纯逻辑层零 Godot 依赖，工程可维护性优先。
- ✅ **三层测试与 CI 门禁**：xUnit 单测 + 无头断言场景 + GitHub Actions 分层全量回归。

## 📸 截图

| 主菜单 | 对局中 |
| --- | --- |
| ![主菜单](./docs/screenshots/start.png) | ![对局](./docs/screenshots/gameplay.png) |

| 基地整备 | 母舰 |
| --- | --- |
| ![基地](./docs/screenshots/base.png) | ![母舰](./docs/screenshots/mothership.png) |

## 🚀 快速开始

### 直接玩

从 [GitHub Releases](https://github.com/NeverToEver/InfiAir/releases) 下载最新预构建包（Windows / Linux x86_64），解压即玩，附安装/卸载脚本。macOS 暂无预构建包，请从源码运行。

### 从源码运行

需要 **Godot 4.6 .NET 版**（标准版无法构建本工程）和 **.NET 8 SDK**：

```bash
git clone https://github.com/NeverToEver/InfiAir.git
cd InfiAir
./run.sh        # 自动探测 godot-mono / godot，也可直接用: godot --path .
```

发布构建使用 `./release.sh`，需要与引擎严格匹配的 4.6.2 mono 导出模板。

## 🎮 操作

| 输入 | 功能 |
|------|------|
| WASD / 方向键 | 移动战机 |
| 鼠标 | 瞄准（准星入辅助框 → 出膛弹自动追踪该敌） |
| — | 武器全自动开火 |
| Shift 长按 | 加速推进（消耗燃料） |
| Ctrl 长按 | 微调姿态 |
| 空格 | 相位冲刺（需 Buff 解锁） |
| H 长按 | 蓄力召唤母舰（驻留中 WASD 驾驶） |
| B 长按 | 返航基地 |
| ESC / 鼠标右键 | 暂停 / 逐级返回 / 退出确认 |

**手柄**：左摇杆移动、右摇杆瞄准（虚拟准星）；A 冲刺 / RB 加速 / LB 微调 / LT 弹反 / X 蓄力母舰 / Y 返航 / L3 Buff 栏 / R3 放弃 / B 返回。PlayStation 手柄自动识别。完整键位与改键见游戏内「设置 → 控制」。

## 🧱 技术栈与架构

| 层 | 选型 | 说明 |
|---|---|---|
| 引擎 | Godot 4.6.2 stable（.NET 版） | GL Compatibility 渲染；C# 工程需 .NET 版引擎 |
| 语言 | C# / .NET 8 | `TreatWarningsAsErrors` / `Nullable` / `AnalysisLevel=latest` |
| 纯逻辑层 | `csharp/core/` | 零 Godot 依赖，xUnit 毫秒级直测 |
| 绑定层 | `csharp/godot/` | 节点 / 场景 / UI / 演出，可引用 core |
| 单测 | xUnit（`tests-csharp/`） | 数值模型 / 存储 / 密码派生 / 任务池 / 曲线 |
| 集成测试 | Godot 无头断言场景（`test/*_test.tscn`） | `[PASS]/[FAIL]` 自检，CI 全量回归 |
| CI | GitHub Actions | 分层门禁（详见「测试与 CI」） |

**分层概览**

```text
scenes/ + csharp/godot/         Godot 绑定层（节点、场景、UI、演出）
        └─ GameState（唯一 autoload，编排门面）
             ├─ 8 个域服务：Meta / Missions / Score / RunProgression / Combat / Settings /
             │   InputBindings / UserSession
             ├─ 8 个基础服务：BalanceService / SaveManager / SfxPlayer /
             │   EntityManager / FogEventManager / GameEventManager / UserDB / ProgressionInterop
             └─ 委托 csharp/core/ 纯逻辑
csharp/core/                    纯 .NET 类库（零 Godot 依赖）
tests-csharp/                   xUnit 单测（引用 core，不依赖 Godot 运行时）
```

> 深入架构说明（GameState 拆域、实体管理、对象池、伤害管线、持久化安全、UI 设计系统）见 [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md)。

## 🧪 测试与 CI

三层测试体系（权威计数与场景清单见 [docs/TESTING.md](./docs/TESTING.md)）：

1. **xUnit 纯逻辑单测**（`tests-csharp/`，毫秒级）：数值模型 / 路径解析 / 任务池 / 进程曲线 / 存档原子写 / 用户库与密码派生。
2. **Godot 无头断言场景**（`test/*_test.tscn`）：对局编排 / 战斗数值 / Boss 模式表与狂暴 / 事件系统 / 存档往返 / UI 流程 / 引擎错误日志扫描。
3. **CI 分层门禁**（`.github/workflows/ci.yml`）：
   - `fast-gate`（约 8 分钟）：C# 构建（warnings-as-errors）+ xUnit + dotnet format 零 diff → 零 GDScript 门 → 引擎 import 警告门 → 主流程 smoke → 全场景编译探针；
   - `full-regression`（约 40 分钟）：BALANCE_MAP 生成器零 diff → 全部断言场景全量 → 引擎错误日志扫描；
   - 纯文档改动不触发 CI；依赖经 actions/cache 缓存，同分支新推送取消旧运行。

最小本地验证集：

```bash
dotnet build                                 # C# 构建（CI 零警告门禁）
dotnet test tests-csharp/                    # xUnit 纯逻辑单测
godot --headless --import --path .           # 资源导入与脚本解析
godot --headless --path . --quit-after 300   # 运行时冒烟
godot --headless --path . res://test/smoke_test.tscn  # 主流程冒烟（自检全 PASS）
```

## 📁 项目结构

```text
csharp/core/        纯 .NET 类库（零 Godot 依赖）：模型/曲线/存储/任务池/配置解析
csharp/godot/       引擎绑定层：GameState + 8 域服务 + 8 基础服务 + 场景脚本 + 实体/事件/UI
tests-csharp/       xUnit 单测
scenes/             场景文件（welcome 入口 / main 对局 / boss / mothership / 过场）
test/               无头断言场景（*_test.tscn）+ 截图工具
data/               balance.json（数值配置）+ translations.csv（中英双语）
scripts/tools/      离线工具（gen_balance_map.py 等，非运行时依赖）
docs/               架构/设计/审计文档
```

## 🧑‍🤝‍🧑 社区与贡献

- 🐛 **反馈 Bug / 建议功能**：欢迎开 [Issue](https://github.com/NeverToEver/InfiAir/issues)，已有 `bug` / `enhancement` 模板。
- 💬 **讨论**：欢迎加入 [GitHub Discussions](https://github.com/NeverToEver/InfiAir/discussions)，交流玩法、路线图与开发经验。
- 🤝 **贡献代码**：请先阅读 [CONTRIBUTING.md](./CONTRIBUTING.md) 和 [AGENTS.md](./AGENTS.md)，提交 PR 前运行最小验证集。
- 🛡️ **安全披露**：请通过 [SECURITY.md](./SECURITY.md) 的私有渠道报告安全问题。
- 🗺️ **路线图**：方向与计划见 [docs/ROADMAP.md](./docs/ROADMAP.md)。

## 📚 文档

| 文档 | 内容 |
|------|------|
| [AGENTS.md](./AGENTS.md) | 开发约定总纲：技术栈 / 运行验证 / 架构 / 代码风格 / 测试策略 / CI 门禁 |
| [CONTRIBUTING.md](./CONTRIBUTING.md) | 贡献指南：环境准备 / 开发流程 / PR 检查清单 |
| [CHANGELOG.md](./CHANGELOG.md) | 版本变更记录 |
| [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) | 架构总览：目录职责 / 逐脚本职责 / 服务委托清单 |
| [docs/TESTING.md](./docs/TESTING.md) | 测试策略：权威场景计数 / 断言清单 / CI 流程 |
| [docs/DESIGN_BASELINE.md](./docs/DESIGN_BASELINE.md) | 设计基线：玩法规则 / 架构口径 |
| [docs/BALANCE_MAP.md](./docs/BALANCE_MAP.md) | 数值配置索引（生成器产出，勿手改） |
| [docs/AUDIT_VAULT.md](./docs/AUDIT_VAULT.md) | 代码审计档案（专有，不可删） |
| [docs/ROADMAP.md](./docs/ROADMAP.md) | 路线图与未来方向（单一事实源） |

## 📄 许可与致谢

**许可证**：游戏代码与程序化生成素材采用 [MIT License](./LICENSE)；内置字体 [Noto Sans SC](https://fonts.google.com/noto/specimen/Noto+Sans+SC) 采用 [SIL Open Font License 1.1](https://openfontlicense.org/)（第三方声明见 [NOTICE](./NOTICE)）。

**致谢**：[airwar-game](https://github.com/NeverToEver/airwar-game)（原作原型）· [Godot-GameTemplate](https://github.com/nezvers/Godot-GameTemplate) · [top-down-shooter-core](https://github.com/quiver-dev/top-down-shooter-core) · [SimpleTopDownShooterTemplate2D](https://github.com/Unchained112/SimpleTopDownShooterTemplate2D) · [Godot-Menus-Template](https://github.com/Maaack/Godot-Menus-Template) · [Godot Engine](https://godotengine.org/) · [Noto Sans SC](https://fonts.google.com/noto/specimen/Noto+Sans+SC)（SIL OFL）

---

Made with Godot 4 · 业余维护中，欢迎 Star / Issue / PR 让 InfiAir 变得更好！
