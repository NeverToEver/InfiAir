<div align="center">

# 🛩️ InfiAir · 无限空域

**一款 2D 俯视空战射击游戏 —— 使用 Godot 4 + GDScript 构建**（早期重制自 Python 项目 [airwar-game](https://github.com/NeverToEver/airwar-game)，现已独立演进）

[English](./README.en.md) · **中文**

[![Godot](https://img.shields.io/badge/godot-4.6-478cbf?logo=godot-engine&logoColor=white)](https://godotengine.org/)
[![GDScript](https://img.shields.io/badge/GDScript-100%25-478cbf)](https://docs.godotengine.org/en/stable/tutorials/scripting/gdscript/)
[![Tests](https://img.shields.io/badge/tests-893%20passed-brightgreen)](#运行验证)
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
- **3 种 Boss 轮换 + 阶段化狂暴**：重装 / 游击 / 母舰型，HP 阶段模式表驱动（P1/P2/狂暴 + telegraph 前摇预告）；血量低于阈值进入**三型差异化狂暴**——专属攻击序列、狂暴期玩家减速 ×0.35 而非定身；拖过限时未击杀则 Boss 逃跑（详见玩法循环）。
- **母舰对接火力平台**：长按蓄力召唤（虚影预告），到位自动吸附对接；驻留弹匣制（10 格 × 2s）——双炮塔向上 80° 扫射 + 导弹齐射（≤5 目标），WASD 直接驾驶；余 4 格弹药警告、5s 后强制离舰，也可长按 H 提前离舰（带进度条）按剩余量折算冷却。
- **精英炮塔事件**：偶遇精英打击航母自深空降入——按难度升起 3/4/5 座弱锁定索敌炮台（限速转台 + 出膛散布 + 弱追踪弹），30 秒限时全歼；指挥官通讯台词随摧毁进度播放，基座环即状态灯；与 Boss 调度严格互斥，全歼 +500 分（乘难度倍率）。
- **轰炸编队事件**：楔形编队突入投弹——引信制炸弹下落，预警圈随引信同步收缩，AoE 只伤玩家；全歼编队有小额奖励。最低优先级随机事件：不冻结 Boss、不暂停波次，可被返航打断。
- **基地中场整备**：返航不终局——战机库 / 武器挂载（互斥天赋路线）/ 维修补给（RP 经济）/ 任务规划四模块，整备后返回同一局；继续出击时播放**轨道打击清场动画**（瞄准具锁定 → 导弹下落 → 光柱清场，Boss 保留）。
- **返航过场与虚影基地**：长按 B 触发 16.8s 七镜头返航过场——曲率充能、跃迁端口、虚影站「曙光·残响」捕获对接、停机坪降落、归舱入眠（与开场过场对位，Esc/任意键可跳过）；基地控制台为全息虚影皮肤（半透明面板 + 扫描线 + 数据抖动），过场渐暗后无缝淡入。
- **全息科幻 UI 设计系统**：统一色板与字号阶梯、切角面板、主次按钮层级、逐条淡入动效；全部页面（开始/设置/暂停/结算/Buff/基地）同一骨架。
- **街机级可视性**：战机提亮 + 青色描边辉光，受击判定点（r=7）配闪烁光点，弹幕再密也不丢焦点。
- **纯程序化资产**：全部 13 张单位贴图（含玩家机与母舰）程序化生成并精细化——装甲板细分、晶簇细节、晶体能量核、二级霓虹描边、喷管环，敌方单位为晶体棱镜风格；由 `generate_enemy_sprites.py` / `generate_player_sprite.py` / `generate_mothership_sprite.py` 生成（均在 `scripts/tools/`）；音效与 BGM 由 `generate_audio.py` 合成，零外部素材依赖。

## 🖼️ 截图

| 主界面 | 游戏画面 | 基地整备 |
|--------|----------|----------|
| ![主界面](./docs/screenshots/start.png) | ![游戏画面](./docs/screenshots/gameplay.png) | ![基地整备](./docs/screenshots/base.png) |

## 🎮 操作

| 按键 / 输入 | 功能 |
|-------------|------|
| WASD / 方向键 | 移动战机 |
| 鼠标 | 瞄准（跟随准星；约 40% 敌机带青色辅助框，准星入框后出膛弹追踪该敌，弱/中/强三档可调） |
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
> 视角缩放三档（小 1.0 / 中 1.35 / 大 1.7，默认小）与窗口大小三档（1280×720 / 1600×900 / 1920×1080，默认大）在「设置 → 操作模式」切换，两者独立，均持久化。
> 辅助瞄准三档（弱 / 中 / 强，默认中）同在「设置 → 操作模式」：辅助常驻不可关闭，仅调强度（辅助框大小 / 追踪转向速度），持久化。

## 🚀 快速开始

需要 [Godot 4.6](https://godotengine.org/download)（标准版即可，无需 .NET）。

```bash
git clone https://github.com/NeverToEver/InfiAir.git
cd InfiAir
godot --path .          # 直接运行；或用编辑器打开项目按 F5
```

## 🧭 玩法循环

- 100 HP 开局：受击 1.5 秒无敌帧并清除身边 250px 内敌弹；脱战数秒后按难度缓慢回血（基地 2RP 维修、母舰补给均可回满）；**得分制，无掉落拾取**。
- 敌机 4 机型 × 8 种移动模式，按分数阶段解锁；精英 3 型；敌机弹种 single / spread / laser（伤害 12/10/20，身体撞击另有 20 伤）。波次化刷怪：普通波成组入场（均分槽位、锚点悬停且机动相位错开），每 3~4 个普通波一个精英波。
- 轰炸编队事件（最低优先级随机遭遇）：楔形编队入场投弹，引信制炸弹下落、预警圈同步收缩，AoE 只伤玩家；不冻结 Boss、不暂停波次，可被返航打断；全歼有小额奖励。
- 按里程碑阈值曲线（3000 分起，逐循环放大）暂停三选一 Buff；Boss 每 1500 分或 90 秒刷新，击毁 +500 分并提升难度乘数（`1 + (2^min(击杀,10) − 1) × 0.25`，封顶 8x）。
- Boss 按 HP 阶段模式表行动（P1/P2/狂暴，切换带 telegraph 前摇预告）；血量低于阈值触发三型差异化狂暴序列：狂暴期玩家减速 ×0.35（仍可移动射击）、Boss 专属攻击模式；归位后持续狂暴；拖过限时未击杀则 Boss 逃跑。
- 精英炮塔事件（分数 ≥800 后随机遭遇）：航母入场 → 炮台升起充能 → 30s 倒计时；事件期间普通波次暂停、Boss 触发冻结（结束后补触发一次）；全歼得 500 基础分（×难度倍率），超时无奖励。
- RP（征用点数）由 Boss 击杀（+5）与基地任务（+3）获得，用于基地维修 / 充能（各 2 RP）。
- 暂停菜单「保存进度」可随时存档（返航也会自动更新存档），启动时可继续对局；死亡删档。基地整备后「继续出击」触发轨道打击清场动画（瞄准具 → 导弹 → 光柱，Boss 保留），随后返回同一局。
- 首次进入有欢迎页；开始面板含「教程」入口：6 阶段新手教学（移动瞄准 / 加速冲刺 / 战斗 / 母舰对接 / 返航基地 / Boss 狂暴），Esc 随时退出，完成后按钮显示「教程 ✓」。

## 🏗️ 架构

```text
main.tscn（对局编排）
 ├─ Player（移动/瞄准辅助/全自动开火/燃料/相位冲刺/激光武器/碰撞点指示）
 ├─ Spawner（波次化刷怪：普通波成组入场 / 精英特殊槽 / Boss 与事件调度）
 ├─ Mothership（自动对接状态机：召唤→对接→驻留(驾驶+扫射+导弹)→释放→离场）
 ├─ Boss（3 型轮换 + HP 阶段模式表 + 三型差异化狂暴状态机）
 ├─ EliteTurretEvent（精英炮塔事件：航母导演/炮台实体/通讯浮层，与 Boss 互斥）
 ├─ FormationStrikeEvent（轰炸编队事件：编队导演/引信炸弹/预警圈，最低优先级随机事件）
 ├─ IntroCinematic / ReturnCinematic（开场 / 返航过场导演，运行时实例化）
 ├─ OrbitalStrike（继续出击时的轨道打击清场动画：瞄准具→导弹→光柱）
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
godot --headless --path . res://test/smoke_test.tscn          # 主流程 118 项
godot --headless --path . res://test/hit_logic_test.tscn      # 受击/碰撞对齐 60 项
godot --headless --path . res://test/elite_turret_event_test.tscn # 精英炮塔事件 57 项
godot --headless --path . res://test/boss_pattern_test.tscn   # Boss 阶段模式表 55 项
godot --headless --path . res://test/difficulty_test.tscn     # 难度/里程碑/设置 52 项
godot --headless --path . res://test/formation_strike_event_test.tscn # 轰炸编队事件 47 项
godot --headless --path . res://test/base_system_test.tscn    # 存档/RP/任务/路线 46 项
godot --headless --path . res://test/view_zoom_test.tscn      # 视角缩放 43 项
godot --headless --path . res://test/startup_flow_test.tscn   # 启动链路/损坏存档/欢迎页 40 项
godot --headless --path . res://test/boss_enrage_test.tscn    # Boss 狂暴序列 34 项
godot --headless --path . res://test/enemy_combat_test.tscn   # 敌机/Boss 32 项
godot --headless --path . res://test/boss_phase_test.tscn     # Boss 阶段切换 31 项
godot --headless --path . res://test/buff_visuals_test.tscn   # Buff 外观反馈 30 项
godot --headless --path . res://test/buff33_test.tscn         # Buff/母舰/放弃 29 项
godot --headless --path . res://test/tutorial_test.tscn       # 新手教程 29 项
godot --headless --path . res://test/return_cinematic_test.tscn # 返航过场 27 项
godot --headless --path . res://test/intro_cinematic_test.tscn # 开场过场 25 项
godot --headless --path . res://test/balance_test.tscn        # 数值配置中心 25 项
godot --headless --path . res://test/back_navigation_test.tscn # 返回/退出状态机 25 项
godot --headless --path . res://test/window_size_test.tscn    # 窗口大小 17 项
godot --headless --path . res://test/keybind_test.tscn        # 可改键 15 项
godot --headless --path . res://test/orbital_strike_test.tscn # 轨道打击清场 13 项
godot --headless --path . res://test/pool_reuse_test.tscn     # 对象池复用 12 项
godot --headless --path . res://test/wave_pacing_test.tscn    # 波次节奏 11 项
godot --headless --path . res://test/esc_navigation_test.tscn # Esc 导航 11 项
godot --headless --path . res://test/i18n_test.tscn           # 中英双语 9 项
```

另有自动化探针与工具（非断言测试）：

```bash
godot --headless --path . res://test/autoplay_test.tscn  # 模拟人工游玩 ≥8 分钟：全交互覆盖 + 异常监控
godot --headless --fixed-fps 1000 --path . res://test/perf_bench.tscn  # 纯帧耗时性能基准
godot --path . res://test/ui_capture.tscn                # 窗口模式六界面截图（/tmp/ui_*.png）
godot --path . res://test/visual_capture.tscn            # 窗口模式游戏画面截图（/tmp/infiair_capture.png）
godot --path . res://test/return_capture.tscn            # 窗口模式返航过场逐镜头截图（/tmp/return_shot*.png）
```

26 个测试场景共 **893 项断言**，全部通过。

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
- [x] 瞄准辅助重做（方向锥形切换 + 弱/中/强三档常驻可调 + 自适应锁定环）+ 敌方单位晶体棱镜风格贴图（程序生成） —— 迭代 3.15
- [x] 精英炮塔事件（打击航母 + 弱锁定炮台 + 通讯台词 + Boss 互斥） —— 迭代 3.16
- [x] Boss 行为重设计（HP 阶段模式表 + telegraph 前摇 + 三型差异化狂暴 + 难度分档） —— 迭代 3.17
- [x] 开场 / 返航过场与虚影基地（双七镜头导演 + 全息基地皮肤） —— 迭代 3.18
- [x] Buff 外观反馈 + 玩家战机重设计 + 游戏内 UI 优化 —— 迭代 3.19
- [x] 波次化刷怪（成组入场 / 锚点悬停相位错开） + 轰炸编队事件 —— 迭代 3.20
- [x] 轨道打击清场动画 + 全单位贴图精细化 + 初始弹速提升 —— 迭代 3.21
- [x] 战斗可读性审计（Boss/悬停带视角适配 + 敌机放大提亮 + 敌弹可见性 + 瞄准辅助重设计：准星 + 40% 辅助框 + 框内追踪 + 弹速 1800） —— 迭代 3.22
- [ ] 打包发布（暂缓）

未来方向与阶段计划见 [docs/ROADMAP.md](./docs/ROADMAP.md)；移植时期的逐项对照与迭代历史已归档（冻结不再维护）：[docs/archive/PORTING_PARITY.md](./docs/archive/PORTING_PARITY.md)。

## 🤝 参与贡献

欢迎 Issue 和 PR！提交前请确认：

1. 上述无头测试全部通过；
2. 遵循 `AGENTS.md` 中的约定（碰撞层、UI 设计系统、代码风格、测试策略）；
3. 方向类决策（新内容立项、暂缓/重启）请先在 `docs/ROADMAP.md` 登记。

借鉴的开源项目：[nezvers/Godot-GameTemplate](https://github.com/nezvers/Godot-GameTemplate)、[quiver-dev/top-down-shooter-core](https://github.com/quiver-dev/top-down-shooter-core)、[Unchained112/SimpleTopDownShooterTemplate2D](https://github.com/Unchained112/SimpleTopDownShooterTemplate2D)、[Maaack/Godot-Menus-Template](https://github.com/Maaack/Godot-Menus-Template)。

## 📄 许可证

本项目当前为私有仓库，暂未选择开源许可证；如需使用或分发请先联系作者。

---

*InfiAir 早期重制自 airwar-game（Python/Pygame），现已独立演进；业余维护中，欢迎反馈。*
