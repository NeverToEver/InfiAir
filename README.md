<div align="center">

# 🛩️ InfiAir · 无限空域

**一款 2D 俯视空战射击游戏 —— 使用 Godot 4 + GDScript 构建，重制自 Python 原作 [airwar-game](https://github.com/NeverToEver/airwar-game)**

[English](./README.en.md) · **中文**

[![Godot](https://img.shields.io/badge/godot-4.6-478cbf?logo=godot-engine&logoColor=white)](https://godotengine.org/)
[![GDScript](https://img.shields.io/badge/GDScript-100%25-478cbf)](https://docs.godotengine.org/en/stable/tutorials/scripting/gdscript/)
[![Tests](https://img.shields.io/badge/tests-586%20passed-brightgreen)](#运行验证)
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
- **3 种 Boss 轮换 + 完整狂暴序列**：重装 / 游击 / 母舰型；血量 <30% 触发狂暴——锁血、子弹时间、环绕轨道攻击、定身弹幕、终结齐射（详见玩法循环）；拖过 50 秒未击杀则 Boss 逃跑。
- **母舰对接火力平台**：长按蓄力召唤（虚影预告），到位自动吸附对接；驻留弹匣制（10 格 × 2s）——双炮塔向上 80° 扫射 + 导弹齐射（≤5 目标），WASD 直接驾驶；余 4 格弹药警告、5s 后强制离舰，也可长按 H 提前离舰（带进度条）按剩余量折算冷却。
- **基地中场整备**：返航不终局——战机库 / 武器挂载（互斥天赋路线）/ 维修补给（RP 经济）/ 任务规划四模块，整备后返回同一局。
- **全息科幻 UI 设计系统**：统一色板与字号阶梯、切角面板、主次按钮层级、逐条淡入动效；全部页面（开始/设置/暂停/结算/Buff/基地）同一骨架。
- **街机级可视性**：战机提亮 + 青色描边辉光，受击判定点（r=7）配闪烁光点，弹幕再密也不丢焦点。
- **纯程序化资产**：贴图程序生成（继承自 Python 原作），音效与 BGM 由 `scripts/tools/generate_audio.py` 合成，零外部素材依赖。

## 🖼️ 截图

| 主界面 | 游戏画面 | 基地整备 |
|--------|----------|----------|
| ![主界面](./docs/screenshots/start.png) | ![游戏画面](./docs/screenshots/gameplay.png) | ![基地整备](./docs/screenshots/base.png) |

## 🎮 操作

| 按键 / 输入 | 功能 |
|-------------|------|
| WASD / 方向键 | 移动战机 |
| 鼠标 | 瞄准（230px 内自动磁吸锁定，甩鼠标脱离） |
| — | 武器全自动开火 |
| Shift 长按 | 加速推进（×1.8，消耗燃料） |
| Ctrl 长按 | 微调姿态（速度 ×0.35） |
| 空格 | 相位冲刺（需 Buff 解锁，无敌突进，耗 25% 燃料） |
| H 长按 3 秒 | 蓄力召唤母舰（驻留中 WASD 驾驶母舰；H 长按 2 秒提前离舰，带进度条） |
| B 长按 1.5 秒 | 返航基地中场整备 |
| K 长按 3 秒 | 放弃当前出击 |
| ESC | 全局返回：战斗中暂停（可保存进度）/ 页面逐级返回 / 顶层弹退出确认 |
| R | 结算 / 暂停时重开 |

> 以上按键均可在「设置 → 控制」中自定义（Esc/R 固定不可改，改键持久化于 `user://profile.json`）。
> 中英双语：「设置 → 操作模式 → 语言 / Language」切换。
> 视角缩放三档（小 1.0 / 中 1.35 / 大 1.7，默认中）与窗口大小三档（1280×720 / 1600×900 / 1920×1080，默认大）在「设置 → 操作模式」切换，两者独立，均持久化。

## 🚀 快速开始

需要 [Godot 4.6](https://godotengine.org/download)（标准版即可，无需 .NET）。

```bash
git clone https://github.com/NeverToEver/InfiAir.git
cd InfiAir
godot --path .          # 直接运行；或用编辑器打开项目按 F5
```

## 🧭 玩法循环

- 100 HP 开局：受击 1.5 秒无敌帧并清除身边 250px 内敌弹；脱战数秒后按难度缓慢回血（基地 2RP 维修、母舰补给均可回满）；**得分制，无掉落拾取**。
- 敌机 4 机型 × 8 种移动模式，按分数阶段解锁；精英 3 型；敌机弹种 single / spread / laser（伤害 12/10/20，身体撞击另有 20 伤）。
- 按里程碑阈值曲线（3000 分起，逐循环放大）暂停三选一 Buff；Boss 每 1500 分或 90 秒刷新，击毁 +500 分并提升难度乘数（`1 + (2^min(击杀,10) − 1) × 0.25`，封顶 8x）。
- Boss 血量 <30% 进入狂暴序列：HP 锁定在 30% 检查点（期间不受伤害）→ 子弹时间 → 环绕我方快照点轨道攻击并定身我方（仍可射击）→ 解锁密集齐射 → 归位后持续狂暴（射速 ×1.5 / 移速 ×1.3）。
- RP（征用点数）由 Boss 击杀（+5）与基地任务（+3）获得，用于基地维修 / 充能（各 2 RP）。
- 暂停菜单「保存进度」可随时存档（返航也会自动更新存档），启动时可继续对局；死亡删档。
- 首次进入有欢迎页；开始面板含「教程」入口：6 阶段新手教学（移动瞄准 / 加速冲刺 / 战斗 / 母舰对接 / 返航基地 / Boss 狂暴），Esc 随时退出，完成后按钮显示「教程 ✓」。

## 🏗️ 架构

```text
main.tscn（对局编排）
 ├─ Player（移动/瞄准辅助/全自动开火/燃料/相位冲刺/激光武器/碰撞点指示）
 ├─ Spawner（敌机 4 型 + 精英 3 型配置表 / 分数阶段解锁 / Boss 轮换调度）
 ├─ Mothership（自动对接状态机：召唤→对接→驻留(驾驶+扫射+导弹)→释放→离场）
 ├─ Boss（3 型轮换 + 狂暴序列状态机：锁血/轨道环绕/定身/齐射/归位）
 ├─ HUD / BuffSelect / BaseConsole / GameOver / Pause / Settings / StartPanel / Welcome
 ├─ BackNavigator（全局返回/退出状态机：PC Esc、手柄 B、Android 返回统一路由）
 └─ GameState（autoload：100 HP 生命/分数/Buff/RP/任务/路线/存档/profile/音效池/震动）
```

- **数值配置中心**：`data/balance.json` 集中全部可调数值，`GameState.cfg()` 统一访问，缺失回退脚本默认值——调参改 JSON 即可。
- **UI 设计系统**：`scripts/ui_theme.gd` 统一色板 token、字号阶梯（72/40/28/24/18）与组件工厂（页面骨架/主次按钮/分组标题/动效），所有页面同一风格。
- **性能**：子弹/敌机/爆炸对象池复用、注册表替代组查询、三角函数查表、HUD 节流；`--fixed-fps` 基准场景可测帧耗时。
- 碰撞层：`1=player 2=player_bullet 3=enemy 4=enemy_bullet`，子弹侧结算伤害；受击判定只看 r=7 判定点。
- 对局存档 `user://savegame.json` 与局外档案 `user://profile.json`（最高分/难度/键位/语言/视角/窗口）均带版本号，损坏自动隔离备份。
- 测试为无头场景脚本（非框架），详见 `AGENTS.md`。

## ✅ 运行验证

```bash
godot --headless --import --path .          # 资源与脚本解析
godot --headless --path . --quit-after 300  # 运行时冒烟
godot --headless --path . res://test/smoke_test.tscn          # 主流程 111 项
godot --headless --path . res://test/hit_logic_test.tscn      # 受击/碰撞对齐 60 项
godot --headless --path . res://test/difficulty_test.tscn     # 难度/里程碑/设置 52 项
godot --headless --path . res://test/base_system_test.tscn    # 存档/RP/任务/路线 46 项
godot --headless --path . res://test/view_zoom_test.tscn      # 视角缩放 43 项
godot --headless --path . res://test/startup_flow_test.tscn   # 启动链路/损坏存档/欢迎页 40 项
godot --headless --path . res://test/boss_enrage_test.tscn    # Boss 狂暴序列 33 项
godot --headless --path . res://test/enemy_combat_test.tscn   # 敌机/Boss 31 项
godot --headless --path . res://test/buff33_test.tscn         # Buff/母舰/放弃 29 项
godot --headless --path . res://test/tutorial_test.tscn       # 新手教程 29 项
godot --headless --path . res://test/balance_test.tscn        # 数值配置中心 25 项
godot --headless --path . res://test/back_navigation_test.tscn # 返回/退出状态机 23 项
godot --headless --path . res://test/window_size_test.tscn    # 窗口大小 17 项
godot --headless --path . res://test/keybind_test.tscn        # 可改键 15 项
godot --headless --path . res://test/pool_reuse_test.tscn     # 对象池复用 12 项
godot --headless --path . res://test/esc_navigation_test.tscn # Esc 导航 11 项
godot --headless --path . res://test/i18n_test.tscn           # 中英双语 9 项
```

另有自动化探针（非断言测试）：

```bash
godot --headless --path . res://test/autoplay_test.tscn  # 模拟人工游玩 ≥8 分钟：全交互覆盖 + 异常监控
godot --path . res://test/ui_capture.tscn                # 窗口模式六界面截图（/tmp/ui_*.png）
```

17 个测试场景共 **586 项断言**，全部通过。

## 🗺️ 路线图

- [x] 核心单局循环（刷怪 / 里程碑 Buff / Boss / 结算）
- [x] 手感与表现（震动 / 粒子 / 合成音效与 BGM / 预告与警告）
- [x] 16 种 Buff + 相位冲刺 + 燃料系统
- [x] 母舰对接（蓄力召唤 / 弹匣驻留 / 扫射护航 / 提前离舰）
- [x] 返航基地中场整备（4 模块 + RP 经济 + 天赋路线）
- [x] 战斗对齐（瞄准辅助 / 敌机三弹型 / Boss 逃跑 / 8 种移动模式）
- [x] 难度选择 + 性能优化（对象池 / 注册表 / HUD 节流） —— 迭代 3.4
- [x] 新手教程（6 阶段） —— 迭代 3.5
- [x] 视角缩放三档 —— 迭代 3.7
- [x] 受击与碰撞专项对齐（r7 判定点 / Boss 撞击 / 弹种伤害） —— 迭代 3.8
- [x] 100 HP 伤害模型与附录 A 全面对齐 —— 迭代 3.9
- [x] 欢迎页 + 启动链路加固 + 全局返回/退出状态机 —— 迭代 3.10
- [x] 对象池复用修复 + autoplay 模拟人工游玩探针 —— 迭代 3.11
- [x] 窗口大小三档 + 母舰提前离舰进度条 —— 迭代 3.12
- [x] UI 设计系统重构（统一骨架 / 主次按钮 / 全页面迁移） —— 迭代 3.13
- [x] Boss 狂暴完整序列（锁血 / 轨道环绕 / 定身 / 齐射）+ 玩家可视性（提亮 / 辉光 / 碰撞点光点） —— 迭代 3.14
- [ ] 打包发布（暂缓）

移植对齐的逐项对照、迭代历史与后续计划见 [docs/PORTING_PARITY.md](./docs/PORTING_PARITY.md)；未来方向与阶段计划见 [docs/ROADMAP.md](./docs/ROADMAP.md)。

## 🤝 参与贡献

欢迎 Issue 和 PR！提交前请确认：

1. 上述无头测试全部通过；
2. 遵循 `AGENTS.md` 中的约定（碰撞层、UI 设计系统、代码风格、测试策略）；
3. 玩法变更请同步更新 `docs/PORTING_PARITY.md` 的对应行。

借鉴的开源项目：[nezvers/Godot-GameTemplate](https://github.com/nezvers/Godot-GameTemplate)、[quiver-dev/top-down-shooter-core](https://github.com/quiver-dev/top-down-shooter-core)、[Unchained112/SimpleTopDownShooterTemplate2D](https://github.com/Unchained112/SimpleTopDownShooterTemplate2D)、[Maaack/Godot-Menus-Template](https://github.com/Maaack/Godot-Menus-Template)。

## 📄 许可证

本项目当前为私有仓库，暂未选择开源许可证；如需使用或分发请先联系作者。

---

*InfiAir 是 airwar-game（Python/Pygame）的 Godot 重制版，业余维护中，欢迎反馈。*
