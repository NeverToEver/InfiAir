<div align="center">

# 🛩️ InfiAir · 无限空域

**一款 2D 俯视空战射击游戏 —— 使用 Godot 4 + GDScript 构建**

[English](./README.en.md) · **中文**

[![Godot](https://img.shields.io/badge/Godot-4.6-478cbf?logo=godot-engine&logoColor=white)](https://godotengine.org/)
[![GDScript](https://img.shields.io/badge/GDScript-100%25-478cbf)](https://docs.godotengine.org/en/stable/tutorials/scripting/gdscript/)
[![Release](https://img.shields.io/badge/Release-v3.22-orange)](https://github.com/NeverToEver/InfiAir/releases)
[![Tests](https://img.shields.io/badge/Tests-1018%20passed-brightgreen)](#-测试与验证)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey)](#-安装)

<img src="./docs/screenshots/gameplay.png" alt="InfiAir 游戏画面" width="760">

[📦 下载安装](#-安装) · [🚀 从源码运行](#-从源码运行) · [🎮 操作](#-操作) · [📚 文档](#-文档) · [🗺️ 路线图](#️-路线图)

</div>

## 简介

InfiAir（无限空域）是一款单机得分制街机射击游戏：驾驶战机迎战波次化敌潮，在分数里程碑三选一构筑 Buff，挑战轮换 Boss；随时返航基地中场整备，再杀回同一局——死亡是唯一终局。难度曲线线性无封顶：活得越久、杀得越多，敌潮越强。

早期重制自 Python/Pygame 项目 [airwar-game](https://github.com/NeverToEver/airwar-game)，现已脱离原作独立演进。全部贴图与音频均为程序化生成，零外部素材依赖。

## ✨ 特性

**玩法**

- 🔄 **完整出击循环**：刷怪成长 → 里程碑 Buff 三选一 → Boss 轮换战 → 返航整备 → 再次出击
- 🃏 **16 种可叠加 Buff**：伤害 / 射速 / 散射 / 穿透 / 爆炸 / 吸血 / 护甲 / 闪避 / 相位冲刺 / 激光光束……按里程碑构筑成型
- 👾 **3 类 Boss 轮换**：HP 阶段模式表驱动（P1 / P2 / 狂暴 + 前摇预告），三型差异化狂暴序列；限时未击杀则 Boss 逃跑
- 🛰️ **母舰火力平台**：蓄力召唤（穿梭门穿出演出）→ 自动对接 → 弹匣制驻留（WASD 驾驶 + 双炮塔扫射 + 导弹齐射）→ 牵引光束回收
- 💥 **双随机事件**：精英炮塔突袭（30s 限时拆塔、Boss 互斥）与轰炸编队（引信炸弹 + 收缩预警圈、可被返航打断）
- 🏠 **基地中场整备**：返航不终局——机库 / 武器挂载（互斥天赋路线）/ 维修补给（RP 经济）/ 任务规划，整备后回到同一局

**视听与演出**

- 🎬 **双过场演出**：开场 6 镜头出征、返航 7 镜头归舰（曲率充能 / 跃迁端口 / 虚影站对接 / 停机坪降落），随时可跳过
- ❤️ **Meta HUD 血量反馈**：受击色差 / 定向波纹 / 低血裂纹生长 / 晕影心跳，全屏后处理，配「减少闪光」无障碍开关
- 🎯 **街机级可读性**：跟随准星 + 40% 敌机辅助框 + 框内追踪弹（弱 / 中 / 强三档）；判定点闪烁光点，弹幕再密不丢焦点
- 🎛️ **全息 UI 设计系统**：统一色板 / 字号阶梯 / 切角面板 / 逐条淡入动效，全部页面同一骨架
- 🎨 **纯程序化资产**：14 张晶体棱镜风格单位贴图与全部音效 BGM 由脚本合成，可离线重新生成

## 🖼️ 截图

| 主界面 | 游戏画面 | 母舰对接 | 基地整备 |
|--------|----------|----------|----------|
| ![主界面](./docs/screenshots/start.png) | ![游戏画面](./docs/screenshots/gameplay.png) | ![母舰对接](./docs/screenshots/mothership.png) | ![基地整备](./docs/screenshots/base.png) |

## 📦 安装

从 [GitHub Releases](https://github.com/NeverToEver/InfiAir/releases) 下载预构建包（x86_64，嵌入 pck 单文件可执行，含安装 / 卸载脚本）：

- **Windows**：解压 zip 后直接运行 `InfiAir.exe`；或运行 `install.bat` 安装到 `%LOCALAPPDATA%\InfiAir` 并创建开始菜单快捷方式（`uninstall.bat /purge` 连存档一起删除）
- **Linux**：解压 tar.gz 后直接运行 `InfiAir.x86_64`；或运行 `./install.sh` 用户态安装（`~/.local` + 桌面菜单项，`./uninstall.sh --purge` 连存档一起删除）
- **macOS**：暂无预构建包，请[从源码运行](#-从源码运行)

卸载默认保留存档（`user://savegame.json` 对局进度 / `user://profile.json` 最高分与设置）。

## 🚀 从源码运行

需要 [Godot 4.6](https://godotengine.org/download)（标准版即可，无需 .NET）：

```bash
git clone https://github.com/NeverToEver/InfiAir.git
cd InfiAir
godot --path .     # 直接运行；或用 Godot 编辑器打开项目按 F5
```

## 🎮 操作

| 按键 / 输入 | 功能 |
|-------------|------|
| WASD / 方向键 | 移动战机 |
| 鼠标 | 瞄准（跟随准星；约 40% 敌机带青色辅助框，准星入框后出膛弹追踪该敌，弱 / 中 / 强三档可调） |
| — | 武器全自动开火 |
| Shift 长按 | 加速推进（×1.8，消耗燃料） |
| Ctrl 长按 | 微调姿态（速度 ×0.35） |
| 空格 | 相位冲刺（需 Buff 解锁，无敌突进，耗 25% 燃料） |
| H 长按 3 秒 | 蓄力召唤母舰（驻留中 WASD 驾驶母舰；H 长按 2 秒提前离舰，带进度条） |
| B 长按 1.5 秒 | 返航基地中场整备 |
| K 长按 3 秒 | 放弃当前出击 |
| ESC | 全局返回：战斗中暂停（可保存进度）/ 页面逐级返回 / 顶层弹退出确认 |
| R | 结算 / 暂停时重开 |

> 全部按键可在「设置 → 控制」自定义（Esc / R 固定，改键持久化）；语言（中文 / English）、视角缩放三档、窗口大小三档、辅助瞄准三档均在「设置 → 操作模式」，各自独立持久化。

## 🧭 玩法速览

- 100 HP 开局：受击 1.5 秒无敌帧并清除身边 250px 内敌弹；脱战缓慢回血（基地维修、母舰补给可回满）；**纯得分制，无掉落拾取**。
- 敌机 4 机型 × 8 种移动模式按分数阶段解锁，精英 3 型，弹种 single / spread / laser；波次化刷怪（成组入场、锚点悬停相位错开），每 3~4 个普通波一个精英波。
- 里程碑（3000 分起、逐循环 ×1.35）暂停三选一 Buff；Boss 每 1500 分或 80 秒一头，击毁 +500 分。
- 难度乘数线性无封顶：`1 + 0.5 × Boss击杀数 + 每 10 分钟 +1`（30 秒量化一档），敌方 HP / 伤害随之 ramp——终局必死，活得越久分越高。
- RP（征用点数）由 Boss 击杀（+5）与基地任务（+3）获得，用于基地维修 / 充能（各 2 RP）。
- 暂停菜单随时存档（返航自动更新存档），启动时可继续对局；死亡删档。
- 首次进入有欢迎页；开始面板含 6 阶段教程（移动瞄准 / 加速冲刺 / 战斗 / 母舰对接 / 返航基地 / Boss 狂暴）。

## 🏗️ 架构

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

## 📚 文档

| 文档 | 内容 |
|------|------|
| [AGENTS.md](./AGENTS.md) | 开发约定总纲：技术栈 / 运行验证 / 架构 / 代码风格 / 测试策略 |
| [docs/ROADMAP.md](./docs/ROADMAP.md) | 路线图与未来方向（单一事实源） |
| [docs/EXIT_FLOW.md](./docs/EXIT_FLOW.md) | 返回 / 退出流程 |
| [docs/BOSS_REDESIGN.md](./docs/BOSS_REDESIGN.md) | Boss 阶段模式表与狂暴设计 |
| [docs/META_HUD_DESIGN.md](./docs/META_HUD_DESIGN.md) | Meta HUD 血量反馈设计 |
| [docs/ELITE_TURRET_EVENT.md](./docs/ELITE_TURRET_EVENT.md) · [docs/FORMATION_STRIKE_EVENT.md](./docs/FORMATION_STRIKE_EVENT.md) | 双随机事件设计 |
| [docs/INTRO_CINEMATIC.md](./docs/INTRO_CINEMATIC.md) · [docs/RETURN_HOME_CINEMATIC.md](./docs/RETURN_HOME_CINEMATIC.md) | 开场 / 返航过场设计 |
| [docs/ENDLESS_BALANCE_PLAN.md](./docs/ENDLESS_BALANCE_PLAN.md) | 无限段数值曲线方案 |

## ✅ 测试与验证

测试为无头场景脚本（非测试框架），以 `[PASS]` / `[FAIL]` 输出自检：**29 个断言场景共 1018 项断言，全部通过**。最小验证集：

```bash
godot --headless --import --path .          # 资源导入与脚本解析
godot --headless --path . --quit-after 300  # 运行时冒烟
godot --headless --path . res://test/smoke_test.tscn  # 主流程冒烟（128 项）
```

完整 29 场景清单、性能基准（`perf_bench`）、autoplay 自动游玩探针与窗口模式截图工具见 [AGENTS.md](./AGENTS.md#本地运行与验证)。

## 🗺️ 路线图

- ✅ **已完成**：核心单局循环 / 16 种 Buff 构筑 / Boss 与双事件体系 / 母舰对接与基地整备 / 双过场与全息 UI / Meta HUD 血量反馈 / 无限段难度曲线 / 双平台打包发布（v3.22 起经 GitHub Releases 分发）
- 🔭 **未来方向**：内容演进（新 Buff / 新敌机与 Boss 型 / 移动端操控等）暂缓，重启需重新立项；CI 与语义化版本流程规划中
- 迭代历史见 git 提交记录；移植时期档案已归档（冻结）：[docs/archive/PORTING_PARITY.md](./docs/archive/PORTING_PARITY.md)

详见 [docs/ROADMAP.md](./docs/ROADMAP.md)。

## 🤝 参与贡献

欢迎 Issue 和 PR！提交前请确认：

1. 全部无头断言场景通过；
2. 遵循 [AGENTS.md](./AGENTS.md) 中的约定（碰撞层、UI 设计系统、代码风格、测试策略）；
3. 方向类决策（新内容立项、暂缓 / 重启）请先在 [docs/ROADMAP.md](./docs/ROADMAP.md) 登记。

## 🙏 致谢

- 原作原型：[airwar-game](https://github.com/NeverToEver/airwar-game)（Python / Pygame）
- 参考项目：[nezvers/Godot-GameTemplate](https://github.com/nezvers/Godot-GameTemplate)、[quiver-dev/top-down-shooter-core](https://github.com/quiver-dev/top-down-shooter-core)、[Unchained112/SimpleTopDownShooterTemplate2D](https://github.com/Unchained112/SimpleTopDownShooterTemplate2D)、[Maaack/Godot-Menus-Template](https://github.com/Maaack/Godot-Menus-Template)
- 引擎：[Godot Engine](https://godotengine.org/)；字体：[Noto Sans SC](https://fonts.google.com/noto/specimen/Noto+Sans+SC)（SIL OFL）

## 📄 许可证

本项目当前为私有仓库，暂未选择开源许可证；如需使用或分发请先联系作者。

---

<div align="center">

业余维护中，欢迎反馈 · Made with Godot 4

</div>
