<div align="center">

# 🛩️ InfiAir · 无限空域

**一款 2D 俯视空战射击游戏 —— Godot 4 + GDScript 构建**

[English](./README.en.md) · **中文**

[![Godot](https://img.shields.io/badge/Godot-4.6-478cbf?logo=godot-engine&logoColor=white)](https://godotengine.org/)
[![GDScript](https://img.shields.io/badge/GDScript-100%25-478cbf)](https://docs.godotengine.org/en/stable/tutorials/scripting/gdscript/)
[![CI](https://github.com/NeverToEver/InfiAir/actions/workflows/ci.yml/badge.svg)](https://github.com/NeverToEver/InfiAir/actions/workflows/ci.yml)
[![Release](https://img.shields.io/badge/Release-v3.26-orange)](https://github.com/NeverToEver/InfiAir/releases)
[![Tests](https://img.shields.io/badge/Tests-1113%20passed-brightgreen)](#-开发者信息)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey)](#-快速开始)

<img src="./docs/screenshots/gameplay.png" alt="InfiAir 游戏画面" width="760">

[🚀 快速开始](#-快速开始) · [🎮 操作](#-操作) · [🧭 玩法速览](#-玩法速览) · [📁 开发者信息](#-开发者信息)

</div>

## 简介

InfiAir 是一款单机得分制街机射击游戏：驾驶战机迎战波次化敌潮，在分数里程碑三选一构筑 Buff，挑战轮换 Boss；随时返航基地中场整备，再杀回同一局——**死亡是唯一终局**。难度曲线线性无封顶：活得越久、杀得越多，敌潮越强。

早期重制自 Python/Pygame 项目 [airwar-game](https://github.com/NeverToEver/airwar-game)，现已独立演进。全部贴图与音频均为程序化生成，零外部素材依赖。

## ✨ 特性

**玩法**

- 🔄 **完整出击循环** —— 刷怪成长 → 里程碑 Buff → Boss 战 → 返航整备 → 再次出击
- 🃏 **16 种可叠加 Buff** —— 伤害 / 射速 / 散射 / 穿透 / 爆炸 / 吸血 / 护甲 / 闪避 / 相位冲刺 / 激光光束……
- 👾 **3 类轮换 Boss** —— HP 阶段模式表（P1 / P2 / 狂暴）驱动，限时未击杀会逃跑
- 🛰️ **母舰火力平台** —— 蓄力召唤 → 自动对接 → 驻留驾驶（WASD + 双炮塔 + 导弹）→ 牵引回收
- 💥 **双随机事件** —— 精英炮塔突袭与轰炸编队，见缝插针的节奏挑战
- 🏠 **基地中场整备** —— 维修 / 补给 / 天赋路线 / 任务领奖，整备后回到同一局

**视听**

- 🎬 **双过场演出** —— 开场 6 镜头出征、返航 7 镜头归舰，随时可跳过
- ❤️ **血量反馈 HUD** —— 受击色差 / 定向波纹 / 低血裂纹 / 晕影心跳，带「减少闪光」无障碍开关
- 🎯 **辅助瞄准** —— 跟随准星 + 敌机辅助框 + 框内追踪弹（弱 / 中 / 强三档）
- 🎨 **纯程序化资产** —— 贴图 / 音效 / BGM 全部脚本合成，零外部素材

## 🖼️ 截图

| 主界面 | 游戏画面 | 母舰对接 | 基地整备 |
|--------|----------|----------|----------|
| ![主界面](./docs/screenshots/start.png) | ![游戏画面](./docs/screenshots/gameplay.png) | ![母舰对接](./docs/screenshots/mothership.png) | ![基地整备](./docs/screenshots/base.png) |

## 🚀 快速开始

**直接玩**：从 [GitHub Releases](https://github.com/NeverToEver/InfiAir/releases) 下载预构建包（Windows / Linux，x86_64），解压即玩，附安装 / 卸载脚本。macOS 暂无预构建包，请从源码运行。

**从源码运行**（需要 [Godot 4.6](https://godotengine.org/download)，标准版即可）：

```bash
git clone https://github.com/NeverToEver/InfiAir.git
cd InfiAir
godot --path .
```

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
| ESC | 暂停 / 逐级返回 / 退出确认 |

**手柄**：左摇杆移动、右摇杆瞄准（虚拟准星）、A 冲刺 / RB 加速 / LB 微调 / X 蓄力母舰 / Y 返航 / L3 Buff 栏 / R3 放弃 / B 返回；右摇杆灵敏度与摇杆死区可在「设置 → 操作模式 → 手柄」调节。

<details>
<summary>完整按键（放弃出击 / 重开 / 改键）</summary>

- **K 长按 3 秒**：放弃当前出击
- **R**：结算 / 暂停时重开
- 全部按键可在「设置 → 控制」自定义（Esc / R 固定，改键持久化）；语言 / 视角缩放 / 窗口大小 / 辅助瞄准档位在「设置 → 操作模式」，显示区另有「鼠标锁定窗口内」开关（默认开启，防准星移出窗口失控，切换窗口自动放行）

</details>

## 🧭 玩法速览

- **生命与得分**：100 HP 开局，受击有无敌帧与清弹保护；纯得分制，无掉落拾取，死亡即终局。
- **成长**：分数里程碑三选一 Buff；Boss 击毁与基地任务提供 RP，用于维修与补给。
- **节奏**：敌潮随分数解锁新机型与精英，难度随击杀与时长无封顶增长——活得越久分越高。
- **存档**：本局进度自动保存（`user://savegame.json`），死亡即清档；返航自动更新；个人设置 / 最高分 / 本地榜单存 `user://profile.json`。损坏存档自动隔离备份，不阻塞启动。
- **上手**：启动直达主菜单，首次进入有 6 阶段教程（移动 / 冲刺 / 战斗 / 母舰 / 返航 / Boss）。

## 📁 开发者信息

<details>
<summary>🏗️ 架构</summary>

```text
main.tscn（对局编排）
 ├─ Player（移动 / 瞄准辅助 / 全自动开火 / 燃料 / 相位冲刺 / 激光武器）
 ├─ Spawner（波次化刷怪 + 精英 / Boss / 事件特殊槽调度）
 ├─ Mothership（召唤 → 对接 → 驻留驾驶 → 牵引回收 → 离场 状态机）
 ├─ Boss（3 型轮换 + HP 阶段模式表 + 三型差异化狂暴）
 ├─ EliteTurretEvent / FormationStrikeEvent（精英炮塔 / 轰炸编队事件）
 ├─ IntroCinematic / ReturnCinematic / OrbitalStrike（过场与演出导演）
 ├─ HUD / BuffSelect / BaseConsole / Pause / Settings / GameOver / StartPanel
 ├─ BackNavigator（全局返回 / 退出状态机：PC Esc、手柄 B、Android 返回统一路由）
 └─ GameState（autoload：分数 / HP / Buff / RP / 任务 / 存档 / 设置 / 音效池 / 实体注册表）
```

- **数值驱动**：全部可调数值集中在 `data/balance.json`，`GameState.cfg()` 统一访问、缺键回退脚本默认值——调参不改代码。
- **UI 设计系统**：`scripts/ui_theme.gd` 统一色板 token、字号阶梯与组件工厂，所有页面同一风格。
- **性能**：子弹 / 敌机 / 爆炸对象池，注册表替代组查询，三角函数查表，HUD 节流；`perf_bench` 基准场景可测纯帧耗时。
- **碰撞分层**：`1=player 2=player_bullet 3=enemy 4=enemy_bullet`，子弹侧结算伤害；受击只看 r=7 判定点。
- **持久化**：`user://savegame.json` 与 `user://profile.json` 均带版本号，损坏自动隔离备份。

</details>

<details>
<summary>✅ 测试与验证（31 场景 / 1113 断言）</summary>

测试为无头场景脚本（非测试框架），以 `[PASS]` / `[FAIL]` 输出自检。最小验证集：

```bash
godot --headless --import --path .          # 资源导入与脚本解析
godot --headless --path . --quit-after 300  # 运行时冒烟
godot --headless --path . res://test/smoke_test.tscn  # 主流程冒烟（142 项）
```

完整 31 场景清单、性能基准（`perf_bench`）、autoplay 自动游玩探针与窗口模式截图工具见 [AGENTS.md](./AGENTS.md#本地运行与验证)。

</details>

<details>
<summary>📚 文档</summary>

| 文档 | 内容 |
|------|------|
| [AGENTS.md](./AGENTS.md) | 开发约定总纲：技术栈 / 运行验证 / 架构 / 代码风格 / 测试策略 |
| [CONTRIBUTING.md](./CONTRIBUTING.md) | 贡献指南：环境准备 / 开发流程 / PR 检查清单 |
| [CHANGELOG.md](./CHANGELOG.md) | 版本变更记录 |
| [SECURITY.md](./SECURITY.md) | 安全策略与漏洞报告 |
| [docs/ROADMAP.md](./docs/ROADMAP.md) | 路线图与未来方向（单一事实源） |
| [docs/EXIT_FLOW.md](./docs/EXIT_FLOW.md) | 返回 / 退出流程 |
| [docs/BOSS_REDESIGN.md](./docs/BOSS_REDESIGN.md) | Boss 阶段模式表与狂暴设计 |
| [docs/META_HUD_DESIGN.md](./docs/META_HUD_DESIGN.md) | Meta HUD 血量反馈设计 |
| [docs/ELITE_TURRET_EVENT.md](./docs/ELITE_TURRET_EVENT.md) · [docs/FORMATION_STRIKE_EVENT.md](./docs/FORMATION_STRIKE_EVENT.md) | 双随机事件设计 |
| [docs/INTRO_CINEMATIC.md](./docs/INTRO_CINEMATIC.md) · [docs/RETURN_HOME_CINEMATIC.md](./docs/RETURN_HOME_CINEMATIC.md) | 开场 / 返航过场设计 |
| [docs/ENDLESS_BALANCE_PLAN.md](./docs/ENDLESS_BALANCE_PLAN.md) | 无限段数值曲线方案 |

</details>

<details>
<summary>🗺️ 路线图 / 🤝 贡献 / 🙏 致谢 / 📄 许可证</summary>

**路线图**：内容演进（新 Buff / 新敌机与 Boss 型 / 移动端操控等）暂缓，重启需重新立项；CI 与语义化版本流程规划中。详见 [docs/ROADMAP.md](./docs/ROADMAP.md)。

**贡献**：欢迎 Issue 和 PR！提交前请确认：全部无头断言场景通过；遵循 [AGENTS.md](./AGENTS.md) 中的约定；方向类决策（新内容立项、暂缓 / 重启）请先在 [docs/ROADMAP.md](./docs/ROADMAP.md) 登记。

**致谢**：[airwar-game](https://github.com/NeverToEver/airwar-game)（原作原型）· [Godot-GameTemplate](https://github.com/nezvers/Godot-GameTemplate) · [top-down-shooter-core](https://github.com/quiver-dev/top-down-shooter-core) · [SimpleTopDownShooterTemplate2D](https://github.com/Unchained112/SimpleTopDownShooterTemplate2D) · [Godot-Menus-Template](https://github.com/Maaack/Godot-Menus-Template) · [Godot Engine](https://godotengine.org/) · [Noto Sans SC](https://fonts.google.com/noto/specimen/Noto+Sans+SC)（SIL OFL）

**许可证**：游戏代码与程序化生成素材采用 [MIT License](./LICENSE)；内置字体 [Noto Sans SC](https://fonts.google.com/noto/specimen/Noto+Sans+SC) 采用 [SIL Open Font License 1.1](https://openfontlicense.org/)（第三方声明见 [NOTICE](./NOTICE)）。

</details>

---

<div align="center">

业余维护中，欢迎反馈 · Made with Godot 4

</div>
