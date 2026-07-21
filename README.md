<div align="center">

# 🛩️ InfiAir · 无限空域

**一款 2D 俯视空战射击游戏 —— 使用 Godot 4 + GDScript 构建，重制自 Python 原作 [airwar-game](https://github.com/NeverToEver/airwar-game)**

[English](./README.en.md) · **中文**

[![Godot](https://img.shields.io/badge/godot-4.6-478cbf?logo=godot-engine&logoColor=white)](https://godotengine.org/)
[![GDScript](https://img.shields.io/badge/GDScript-100%25-478cbf)](https://docs.godotengine.org/en/stable/tutorials/scripting/gdscript/)
[![Tests](https://img.shields.io/badge/tests-289%20passed-brightgreen)](#运行验证)
[![Platform](https://img.shields.io/badge/platform-macOS%20%7C%20Windows%20%7C%20Linux-lightgrey)](#快速开始)

<img src="./docs/screenshots/gameplay.png" alt="InfiAir 游戏画面" width="760">

</div>

---

## 目录

- [✨ 亮点](#-亮点)
- [🖼️ 截图](#️-截图)
- [🎮 操作](#-操作)
- [🚀 快速开始](#-快速开始)
- [🧭 玩法循环](#-玩法循环)
- [🏗️ 架构](#️-架构)
- [✅ 运行验证](#-运行验证)
- [🗺️ 路线图](#️-路线图)
- [🤝 参与贡献](#-参与贡献)
- [📄 许可证](#-许可证)

## ✨ 亮点

- **完整的出击循环**：刷怪成长 → 里程碑 Buff 三选一 → Boss 轮换战 → 返航基地中场整备 → 再次出击；死亡是唯一终局。
- **16 种 Buff 局内构建**：伤害/射速/散射/穿透/爆炸/吸血/护甲/闪避/相位冲刺/慢速力场/激光光束……按分数里程碑三选一，叠加成型。
- **3 种 Boss 轮换 + 狂暴**：重装 / 游击 / 母舰型，血量 <30% 进入狂暴；输出不足拖过 50 秒 Boss 会直接逃跑。
- **母舰对接火力平台**：长按蓄力召唤、牵引对接、驻留 20 秒弹匣扫射护航、提前离舰冷却打折——补给与火力的战术抉择。
- **基地中场整备**：返航不终局！战机库 / 武器挂载（互斥天赋路线）/ 维修补给（RP 经济）/ 任务规划四大模块，整备完返回同一局继续战斗。
- **纯程序化资产**：全部贴图由程序生成（继承自 Python 原作），音效与 BGM 由 `scripts/tools/generate_audio.py` 合成，零外部素材依赖。

## 🖼️ 截图

| 游戏画面 | 母舰对接 | 基地整备 |
|----------|----------|----------|
| ![游戏画面](./docs/screenshots/gameplay.png) | ![母舰对接](./docs/screenshots/mothership.png) | ![基地整备](./docs/screenshots/base.png) |

## 🎮 操作

| 按键 / 输入 | 功能 |
|-------------|------|
| WASD / 方向键 | 移动战机 |
| 鼠标 | 瞄准（230px 内自动磁吸锁定，甩鼠标脱离） |
| — | 武器全自动开火 |
| Shift 长按 | 加速推进（约 1.8x，消耗燃料） |
| Ctrl 长按 | 微调姿态（速度 ×0.35） |
| 空格 | 相位冲刺（需 Buff 解锁，无敌突进，耗 25% 燃料） |
| H 长按 3 秒 | 蓄力召唤母舰（驻留中长按 H 2s 提前离舰） |
| B 长按 1.5 秒 | 返航基地中场整备 |
| K 长按 3 秒 | 放弃当前出击 |
| ESC | 暂停（暂停菜单可保存进度，全局唯一存档入口） |
| R | 结算 / 暂停时重开 |

## 🚀 快速开始

需要 [Godot 4.6](https://godotengine.org/download)（标准版即可，无需 .NET）。

```bash
git clone https://github.com/NeverToEver/InfiAir.git
cd InfiAir
godot --path .          # 直接运行；或用编辑器打开项目按 F5
```

## 🧭 玩法循环

- 3 条命开局，受击 1.5 秒无敌帧；**得分制，无掉落拾取**。
- 敌机 4 机型 × 8 种移动模式，按分数阶段解锁；精英 3 型；敌机弹种 single / spread / laser。
- 每 500 分里程碑暂停三选一 Buff；Boss 每 1500 分或 90 秒刷新，击毁 +500 分并提升难度乘数（`1 + (2^min(击杀,10) − 1) × 0.25`，封顶 8x）。
- RP（征用点数）由 Boss 击杀（+5）与基地任务（+3）获得，用于基地维修 / 充能。
- 暂停菜单「保存进度」可随时存档，启动时可继续对局；死亡删档。
- 开始面板含「教程」入口：6 阶段新手教学（移动瞄准 / 加速冲刺 / 战斗 / 母舰对接 / 返航基地 / Boss 狂暴），Esc 随时退出，完成后按钮显示「教程 ✓」。

## 🏗️ 架构

```text
main.tscn（对局编排）
 ├─ Player（移动/瞄准辅助/全自动开火/燃料/相位冲刺/激光武器）
 ├─ Spawner（7 机型配置表 + 分数阶段解锁 + Boss 轮换调度）
 ├─ Mothership（7 态状态机：召唤→悬停→对接→驻留→释放→离场）
 ├─ HUD / BuffSelect / BaseConsole / GameOver / Pause / StartPanel
 └─ GameState（autoload：分数/Buff/RP/任务/路线/存档/音效池/震动）
```

- 碰撞层：`1=player 2=player_bullet 3=enemy 4=enemy_bullet`，子弹侧结算伤害。
- 对局存档 `user://savegame.json` 与最高分档案 `user://profile.json` 均带版本号。
- 测试为无头场景脚本（非框架），详见 `AGENTS.md`。

## ✅ 运行验证

```bash
godot --headless --import --path .          # 资源与脚本解析
godot --headless --path . --quit-after 300  # 运行时冒烟
godot --headless --path . res://test/smoke_test.tscn        # 主流程 82 项
godot --headless --path . res://test/base_system_test.tscn  # 存档/RP/任务/路线 46 项
godot --headless --path . res://test/enemy_combat_test.tscn # 敌机/Boss 31 项
godot --headless --path . res://test/buff33_test.tscn       # Buff/母舰/放弃 29 项
godot --headless --path . res://test/difficulty_test.tscn   # 难度/里程碑/设置 52 项
godot --headless --path . res://test/boss_enrage_test.tscn  # Boss 狂暴 24 项
godot --headless --path . res://test/tutorial_test.tscn     # 新手教程 22 项
```

共 289 项断言，全部通过。

## 🗺️ 路线图

- [x] 核心单局循环（刷怪 / 里程碑 Buff / Boss / 结算）
- [x] 手感与表现（震动 / 粒子 / 合成音效与 BGM / 预告与警告）
- [x] 16 种 Buff + 相位冲刺 + 燃料系统
- [x] 母舰对接（蓄力召唤 / 弹匣驻留 / 扫射护航 / 提前离舰）
- [x] 返航基地中场整备（4 模块 + RP 经济 + 天赋路线）
- [x] 战斗对齐（瞄准辅助 / 敌机三弹型 / Boss 逃跑 / 8 种移动模式）
- [x] 难度选择（简单 / 普通 / 困难）与 Boss 狂暴完整版（子弹时间 + 快照弹幕） —— 迭代 3.4
- [x] 性能优化（子弹/爆炸对象池、组查询缓存、HUD 节流） —— 迭代 3.4
- [x] 新手教程（6 阶段，开始面板进入，完成记录 `tutorial_done`） —— 迭代 3.5
- [ ] 联机排行榜
- [ ] 打包发布（暂缓）

移植对齐的逐项对照见 [docs/PORTING_PARITY.md](./docs/PORTING_PARITY.md)，任务指导见 [docs/TASK_REPORT.md](./docs/TASK_REPORT.md)。

## 🤝 参与贡献

欢迎 Issue 和 PR！提交前请确认：

1. 上述 4 套无头测试全部通过；
2. 遵循 `AGENTS.md` 中的约定（碰撞层、代码风格、测试策略）；
3. 玩法变更请同步更新 `docs/PORTING_PARITY.md` 的对应行。

借鉴的开源项目：[nezvers/Godot-GameTemplate](https://github.com/nezvers/Godot-GameTemplate)、[quiver-dev/top-down-shooter-core](https://github.com/quiver-dev/top-down-shooter-core)、[Unchained112/SimpleTopDownShooterTemplate2D](https://github.com/Unchained112/SimpleTopDownShooterTemplate2D)。

## 📄 许可证

本项目当前为私有仓库，暂未选择开源许可证；如需使用或分发请先联系作者。

---

*InfiAir 是 airwar-game（Python/Pygame）的 Godot 重制版，业余维护中，欢迎反馈。*
