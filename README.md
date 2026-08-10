<div align="center">

# InfiAir · 无限空域

**2D 俯视空战射击游戏 · Godot 4.6.2 .NET 版 + C#（.NET 8）全量实现**

[English](./README.en.md) · **中文**

[![Godot](https://img.shields.io/badge/Godot-4.6-478cbf?logo=godot-engine&logoColor=white)](https://godotengine.org/)
[![C#](https://img.shields.io/badge/C%23-100%25-478cbf)](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/)
[![CI](https://github.com/NeverToEver/InfiAir/actions/workflows/ci.yml/badge.svg)](https://github.com/NeverToEver/InfiAir/actions/workflows/ci.yml)
[![Release](https://img.shields.io/badge/Release-v3.28-orange)](https://github.com/NeverToEver/InfiAir/releases)
[![Tests](https://img.shields.io/badge/Tests-assertion%20scenes-brightgreen)](./docs/TESTING.md)

<img src="./docs/screenshots/gameplay.png" alt="InfiAir 游戏画面" width="760">

</div>

## 项目简介

InfiAir 是一款单机得分制街机射击游戏：驾驶战机迎战波次化敌潮，在分数里程碑三选一构筑 Buff，挑战轮换 Boss；随时返航基地中场整备，再杀回同一局——死亡是唯一终局。难度曲线线性无封顶：活得越久、杀得越多，敌潮越强。

重制自 Python/Pygame 项目 [airwar-game](https://github.com/NeverToEver/airwar-game)，现已独立演进。全部贴图与音频均为程序化生成，零外部素材依赖。

技术定位：**全量 C# 工程**（零 GDScript，M1–M7d 迁移完成），纯逻辑与引擎绑定严格分层，热路径遵循每帧零托管分配纪律，测试体系三层（xUnit 单测 + 无头断言场景 + CI 门禁）。

## 技术栈

| 层 | 选型 | 说明 |
|---|---|---|
| 引擎 | Godot 4.6.2 stable（.NET 版） | GL Compatibility 渲染（`renderer/rendering_method`）；标准版引擎无法构建（C# 工程） |
| 语言 | C# / .NET 8 | `TreatWarningsAsErrors` 门禁；`Nullable` / `ImplicitUsings` / `AnalysisLevel=latest` |
| 纯逻辑层 | `csharp/core/`（类库） | 零 Godot 依赖，xUnit 毫秒级直测 |
| 绑定层 | `csharp/godot/`（主工程） | 节点/场景/UI/演出，可引用 core |
| 单测 | xUnit（`tests-csharp/`） | 数值模型/存储/密码派生/任务池/曲线 |
| 集成测试 | Godot 无头断言场景（`test/*_test.tscn`） | `[PASS]/[FAIL]` 自检输出，CI 全量回归 |
| CI | GitHub Actions | 分层门禁（见「测试与 CI」） |

## 架构

### 分层

```
scenes/ + csharp/godot/         Godot 绑定层（节点、场景、UI、演出）
        └─ GameState（唯一 autoload，按域拆 9 个 partial 文件）
             ├─ 8 个非 autoload 服务：BalanceService / SaveManager / SfxPlayer /
             │   EntityManager / FogEventManager / GameEventManager / UserDB / ProgressionInterop
             └─ 委托 csharp/core/ 纯逻辑
csharp/core/                    纯 .NET 类库（零 Godot 依赖）
tests-csharp/                   xUnit 单测（引用 core，不依赖 Godot 运行时）
```

- `GameState`（唯一 autoload）是全局状态/信号总线与门面：~250 个 public 成员（信号、状态、转发），经 `GameState.Instance` typed 访问；Y 系列按域拆分：主壳 + 9 个域 partial（常量/状态/难度/任务/设置/输入/用户/存档/Meta），零行为差异。
- 纯逻辑下沉 core：数值模型（`BalanceModels`）、配置路径解析（`PathResolver`，GDScript `cfg()` 语义镜像）、任务池（`TaskPool`，无放回抽取）、进程曲线（`ProgressionCurves`，位级等价原 GDScript）、存储（`SaveStore` 原子写/损坏隔离、`UserDb` 本地账户 + 密码派生）。
- 互操作壳（`*Interop` + `VariantBridge`）负责 Variant ↔ CLR 双向转换，core 层保持零 Godot 依赖。

### 对局编排（main.tscn）

```text
main.tscn（对局编排）
 ├─ Player（移动 / 瞄准辅助 / 全自动开火 / 燃料 / 相位冲刺 / 激光武器）
 ├─ Spawner（波次化刷怪 + 精英 / Boss / 事件特殊槽调度）
 ├─ Mothership（召唤 → 对接 → 驻留驾驶 → 牵引回收 → 离场 状态机）
 ├─ Boss（4 型轮换 + HP 阶段模式表 + 四型差异化狂暴）
 ├─ EliteTurretEvent / FormationStrikeEvent（精英炮塔 / 轰炸编队事件）
 ├─ IntroCinematic / ReturnCinematic / OrbitalStrike（过场与演出导演）
 ├─ HUD / BuffSelect / BaseConsole / Pause / Settings / GameOver / Welcome（入口）
 ├─ BackNavigator（全局返回 / 退出状态机：PC Esc、手柄 B、Android 返回统一路由）
 └─ GameState（autoload：分数 / HP / Buff / RP / 任务 / 存档 / 设置 / 音效池 / 实体注册表）
```

对局循环：auto-fire + wave 刷怪 → 分数里程碑 Buff 三选一 → 4 个轮转 Boss（P1/P2/狂暴，限时未击杀逃跑）→ 母舰蓄力召唤/驻留驾驶 → 长按 B 返航基地整备 → 轨道打击清场后同一局继续。阶段流转以 Main/Spawner 布尔标志 + 树暂停组合维持（无单一状态源，为已知架构债，见 `docs/AUDIT_VAULT.md`）。

### 核心系统

- **实体管理**：`EntityManager` 对局实体注册表（`Enemies`/`EnemyBullets`）+ O(1) 存在性索引（追踪弹热路径）+ 统一绑定样板（`BindEnemy`/`UnbindEnemy`），替代组查询；敌弹注销 swap-remove O(1)。
- **对象池**：`BulletPool`/`EnemyPool`/`Explosion`——预分配复用 + deferred reparent + 活性复查防同帧复用冲突；热路径每帧零托管分配（`SinFast` 查表、`MoveCtx` 复用、顶点缓冲预分配等纪律）。
- **随机事件系统**：`GameEventManager` 统一编排 fog（迷雾，3 秒间隔掷签）与 encounter（遭遇，每帧轻量计时）两组事件；fog 效果经 `FogEventManager` 门面注入。
- **伤害数值管线**：碰撞 → `EntityDamage.Dispatch` 类型分派（Enemy/Boss/TurretBattery/FormationCraft）→ 实体 `TakeDamage`（`Hp<=0` 早退、同帧守卫、宽限期复核）→ 死亡/回池；暴击/爆炸/溅射/穿透/弹反 buff 在 Bullet 侧乘区实现。
- **数值驱动**：全部可调数值集中 `data/balance.json`，`GameState.Instance.Cfg()` 点路径统一访问、缺键回退代码默认值——调参不改代码；`docs/BALANCE_MAP.md` 由生成器扫描 Cfg 调用点产出（CI 零 diff 闸）；难度/里程碑进程曲线为 core 纯函数（逐位等价原 GDScript）。
- **持久化与安全**：`SaveStore` 原子写（tmp + rename）+ 损坏隔离（`.corrupt` 备份）；每用户存档 `user://savegame_<user>_<hash12>.json`（游客不存档，死亡清档）；`UserDb` 本地账户（自建 PBKDF2-HMAC-SHA256 变体，固定向量测试）+ 本地排行榜；文件名消毒 + sha256[:12] 防路径穿越。
- **UI 设计系统**：`UITheme` 统一色板 token/字号阶梯/组件工厂；文本全信号驱动（无每帧 set_text）、仪表 0.1s 节流 + epsilon 守卫、tween 互斥清理（kill 再建 + meta 缓存）；双语（zh/en）翻译键集中在 `data/translations.csv`。

## 快速开始

**直接玩**：从 [GitHub Releases](https://github.com/NeverToEver/InfiAir/releases) 下载预构建包（Windows / Linux，x86_64），解压即玩，附安装/卸载脚本。macOS 暂无预构建包，请从源码运行。

**从源码运行**（需要 [Godot 4.6 .NET 版](https://godotengine.org/download) 与 .NET 8 SDK——全量 C# 工程，标准版引擎无法构建）：

```bash
git clone https://github.com/NeverToEver/InfiAir.git
cd InfiAir
godot --path .
```

本地开发入口脚本 `./run.sh` 自动探测 .NET 版引擎（godot-mono 优先）。发布构建 `./release.sh`（依赖与引擎严格匹配的 4.6.2 导出模板）。

## 操作

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
| ESC | 暂停 / 逐级返回 / 退出确认 |
| 鼠标右键 | 返回 / 取消（与 Esc 同路由：确认窗取消、设置返回、暂停开/关、顶层退出确认） |

**手柄**：左摇杆移动、右摇杆瞄准（虚拟准星）、A 冲刺 / RB 加速 / LB 微调 / LT 弹反 / X 蓄力母舰 / Y 返航 / L3 Buff 栏 / R3 放弃 / B 返回；右摇杆灵敏度与摇杆死区可在「设置 → 操作模式 → 手柄」调节。PlayStation 手柄自动识别（按钮位置相同，仅标签对应）。

<details>
<summary>完整按键（放弃出击 / 重开 / 改键）</summary>

- **K 长按 3 秒**：放弃当前出击
- **R**：结算 / 暂停时重开
- 全部按键可在「设置 → 控制」自定义（Esc / R 固定，改键持久化）；语言 / 视角缩放 / 窗口大小 / 辅助瞄准档位在「设置 → 操作模式」，显示区另有「鼠标锁定窗口内」开关（默认开启，防准星移出窗口失控，切换窗口自动放行）

</details>

## 玩法速览

- **生命与得分**：100 HP 开局，受击有无敌帧与清弹保护；纯得分制，无掉落拾取，死亡即终局。
- **成长**：分数里程碑三选一 Buff（19 种可叠加：伤害/射速/散射/穿透/爆炸/吸血/护甲/闪避/相位冲刺/激光光束等）；Boss 击毁与基地任务提供 RP，用于维修与补给。
- **节奏**：敌潮随分数解锁新机型与精英，难度随击杀与时长无封顶增长。
- **上手**：启动直达主菜单，首次进入有 6 阶段教程（移动 / 冲刺 / 战斗 / 母舰 / 返航 / Boss）。

## 测试与 CI

三层测试体系（权威计数与场景清单见 [docs/TESTING.md](./docs/TESTING.md)）：

1. **xUnit 纯逻辑单测**（`tests-csharp/`，毫秒级）：数值模型 / 路径解析 / 任务池 / 进程曲线 / 存档原子写 / 用户库与密码派生（含 GDScript 实测固定向量）。
2. **Godot 无头断言场景**（`test/*_test.tscn`，权威计数与清单见 [docs/TESTING.md](./docs/TESTING.md)）：C# 场景脚本自检（`[PASS]/[FAIL]` 输出），覆盖对局编排 / 战斗数值 / Boss 模式表与狂暴 / 事件系统 / 存档往返 / UI 流程 / 引擎错误日志扫描。
3. **CI 分层门禁**（`.github/workflows/ci.yml`，Y 系列规整版）：
   - `fast-gate`（约 8 分钟，全部 push/PR）：C# build（warnings-as-errors）+ xUnit + dotnet format 三工程零 diff → 零 GDScript 门 → 引擎 import 警告门 → main smoke 300 帧 → 全场景编译探针；
   - `full-regression`（约 40 分钟，仅 main push / PR / 手动）：BALANCE_MAP 生成器重跑零 diff 闸 → 全部断言场景全量（权威计数见 docs/TESTING.md，含 flake 重试与引擎错误日志扫描）；
   - 纯文档（`docs/**`、`*.md`）不触发；dotnet SDK / NuGet / Godot 引擎经 actions/cache 缓存；同分支新推送取消旧运行。

健壮性基线：2026-08-10 第六轮（Z 系列）落地——手改存档/配置判型与超大值截断钳制、除零下限钳制、协程悬挂与信号配对防御等 20 处修复；同日第七轮（AA 系列）——Roslynator 静态分析 + 全量逻辑审查，修复 23 处逻辑漏洞（里程碑收敛死循环、meta 预置 buff 信号缺失、教程阶段软锁、存档重复键击穿隔离等）+ 36 处规范化（记录见 [docs/AUDIT_VAULT.md](./docs/AUDIT_VAULT.md)）。

最小本地验证集：

```bash
dotnet build                                 # C# 构建（CI 零警告门禁）
dotnet test tests-csharp/                    # xUnit 纯逻辑单测
godot --headless --import --path .           # 资源导入与脚本解析
godot --headless --path . --quit-after 300   # 运行时冒烟
godot --headless --path . res://test/smoke_test.tscn  # 主流程冒烟（自检全 PASS）
```

## 项目结构

```text
csharp/core/        纯 .NET 类库（零 Godot 依赖）：模型/曲线/存储/任务池/配置解析
csharp/godot/       引擎绑定层：GameState（主壳 + 9 域 partial）+ 8 服务 + 场景脚本 + 实体/事件/UI
tests-csharp/       xUnit 单测
scenes/             场景文件（welcome 入口 / main 对局 / boss / mothership / 过场）
test/               无头断言场景（*_test.tscn，权威计数见 docs/TESTING.md）+ 截图工具
data/               balance.json（数值配置）+ translations.csv（中英双语）
scripts/tools/      离线工具（gen_balance_map.py 等，非运行时依赖）
docs/               架构/设计/审计文档（ARCHITECTURE / TESTING / AUDIT_VAULT 等）
```

## 文档

| 文档 | 内容 |
|------|------|
| [AGENTS.md](./AGENTS.md) | 开发约定总纲：技术栈 / 运行验证 / 架构 / 代码风格 / 测试策略 / CI 门禁 |
| [CONTRIBUTING.md](./CONTRIBUTING.md) | 贡献指南：环境准备 / 开发流程 / PR 检查清单 |
| [CHANGELOG.md](./CHANGELOG.md) | 版本变更记录 |
| [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) | 架构总览：目录职责 / 逐脚本职责 / 服务委托清单 |
| [docs/TESTING.md](./docs/TESTING.md) | 测试策略：权威场景计数 / 断言清单 / 已知失败 / CI 流程 |
| [docs/DESIGN_BASELINE.md](./docs/DESIGN_BASELINE.md) | 设计基线：玩法规则 / 架构口径（修订需走该文档） |
| [docs/BALANCE_MAP.md](./docs/BALANCE_MAP.md) | 数值配置索引（生成器产出，勿手改） |
| [docs/AUDIT_VAULT.md](./docs/AUDIT_VAULT.md) | 代码审计档案（U–AA 系列，专有不可删） |
| [docs/ROADMAP.md](./docs/ROADMAP.md) | 路线图与未来方向（单一事实源） |

## 许可与致谢

**许可证**：游戏代码与程序化生成素材采用 [MIT License](./LICENSE)；内置字体 [Noto Sans SC](https://fonts.google.com/noto/specimen/Noto+Sans+SC) 采用 [SIL Open Font License 1.1](https://openfontlicense.org/)（第三方声明见 [NOTICE](./NOTICE)）。

**致谢**：[airwar-game](https://github.com/NeverToEver/airwar-game)（原作原型）· [Godot-GameTemplate](https://github.com/nezvers/Godot-GameTemplate) · [top-down-shooter-core](https://github.com/quiver-dev/top-down-shooter-core) · [SimpleTopDownShooterTemplate2D](https://github.com/Unchained112/SimpleTopDownShooterTemplate2D) · [Godot-Menus-Template](https://github.com/Maaack/Godot-Menus-Template) · [Godot Engine](https://godotengine.org/) · [Noto Sans SC](https://fonts.google.com/noto/specimen/Noto+Sans+SC)（SIL OFL）

---

业余维护中，欢迎反馈 · Made with Godot 4
