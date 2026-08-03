# 第十轮代码审查报告（2026-08-03，L 系列）——软件工程维度全面评价

> 本报告为一次性审查交付物,记录按软件工程常见审核角度对 InfiAir 全仓库的评价、发现清单与处置结果。发现条目已在 `docs/AUDIT_VAULT.md` 以 L 系列登记（第十轮），本报告与档案互为补充：档案是持久登记，本报告是评价正文。

---

## 1. 审查概述

| 字段 | 值 |
| --- | --- |
| 审查类型 | 全仓库软件工程维度审查（代码质量 / 架构 / 健壮性 / 性能 / 测试 / UI 与输入 / 文档 / 配置构建与工具链 / 安全与持久化 / 提交规范） |
| 工作时间 | 2026-08-03 |
| 审查区域 | `scripts/` 62 文件 + `autoload/game_state.gd`（1409 行）+ `test/` 46 场景 + `scenes/` + `data/` + `project.godot` + `.github/workflows/` + 启动/打包脚本 + `scripts/tools/` + 全部 docs |
| 审查方法 | AgentSwarm 8 路 explore 并行只读审查（对局编排与服务层 / 玩家体系 / 敌机子弹与池 / Boss 体系 / 母舰事件过场 / UI 导航输入 / 测试体系 / 配置构建仓库卫生），每路对照 AUDIT_VAULT 既有登记去重；主控对全部 P1 与关键 P2 逐条读码复核（含 Godot 源码级生命周期验证与生成器复现验证）；修复后逐批跑针对性测试 + 逆验证 |
| 结论 | **P1×3（全部修复并验证）+ P2×15（修复 10、登记待办 5）+ P3×40+（修复 6、登记 30+）**。无 P0 |
| 审核人 | Kimi Code CLI（依据用户指示执行） |

**既有基线**：本项目已完成九轮系统审计（A–K、E 系列，见 `docs/AUDIT_VAULT.md`）。本轮为**独立第十轮**，审查角度从「代码缺陷查找」扩展到「软件工程维度评价」，并与既有登记逐项去重，避免重复报告。

---

## 2. 总体评价

InfiAir 是一个**代码成熟度显著高于同规模独立项目**的 GDScript 代码库（约 1.9 万行脚本 + 46 个自检场景）。九轮审计的修复成果在代码中可见且一致：防御性编程惯例（判型回退、域校验、除零钳制、幂等守卫）已成体系，热路径纪律（无每帧配置查询、查表三角函数、零分配演出）贯彻到位，文档-代码同步意识强（修复均回填起效记录）。

本轮新发现的 P1 集中在**修复自身引入的回归**与**测试维护盲区**——这是成熟项目典型的风险形态：防御惯例在逐文件扩散时偶有漏网（判型只到容器层不到元素层、钳制覆盖弹数键漏掉计时键），以及「修复只覆盖被登记的那条路径」（K03 的死亡路径、E22 的池化复用路径）。未发现架构性缺陷。

各维度评价见 §3，发现清单见 §4。

---

## 3. 分维度评价

### 3.1 代码质量与可维护性 —— 优秀

- **命名与注释**：标识符语义清晰，注释密度高且大量标注修复出处与取舍依据（如 `enemy.gd:256`、`boss.gd:560`），本轮仅发现 2 处注释失实（冲刺无敌冻结、炮台 monitoring 表述）与 1 处死参数残留（均已修复/登记）。
- **复杂度控制**：A3/A4 拆分后 Boss/敌机/事件均职责单一；`BossAttacks` 攻击分发已注册表化（10 攻击 id → Callable），新增攻击只注册不修改分发函数。
- **可改进**：部分「修复族系统性盲区」反复出现（见 §4 登记项），建议后续审计按「键清单」而非「文件清单」复查。

### 3.2 架构与设计 —— 优秀

- **分层与耦合**：单一 autoload 门面 + 四个组合服务（BalanceService/SaveManager/SfxPlayer/EntityRegistry）的委托模式执行彻底，公开 API 语法零感知转发（A2）；跨类私有字段穿透已在 A1 全部封堵，本轮仅发现 1 处新残留（`formation_bomb.gd` 已在 K08 修复，本轮无新发现）。
- **对象池生命周期**：`_repooling` 双防护 + 延迟 monitoring 关闭 + 注册表去重，是项目最扎实的部分；本轮 P1-1 暴露了「C22 信号断开模式套用到池化类」的隐蔽差异（`_ready` 只执行一次 vs `_exit_tree` 每次 reparent 都触发），已修复并补回归断言。
- **开闭原则**：A3/A4 收敛彻底（攻击/移动/狂暴三注册表 + 机型参数表），`boss_registry_test` 29 断言覆盖。

### 3.3 健壮性与错误处理 —— 良好（防御深度有边角）

- **强项**：损坏 JSON 回退默认的口径覆盖了绝大多数路径；存档原子写 + `.corrupt` 隔离；E07 类软锁（struck 兜底）已封堵。
- **系统性缺口**（本轮核心发现群）：判型「只到容器层、不到元素层」——`boss.gd` 模式表元素、`spawner.gd` 嵌套数组元素、`game_state.gd` 难度表均存在；bool 是 int 子类的判型陷阱（E21 已修 spawner 标量，其余 3 处本轮补齐）；计时/计数类键的域校验覆盖不齐（间隔键、狂暴计时键）。
- **机制交叉盲区**：跨文件契约（母舰火力 × 事件奖励、返航冷却覆盖、DEPART 期按 B）不在任何单一文件的防御范围内，已登记待设计确认（L13/L14）。

### 3.4 性能 —— 优秀

- 热路径零每帧 cfg 查询（启动缓存）、`sin_fast`/`cos_fast` 查表、C28 零分配演出（预建点集 + `set_point_position`）、HUD 信号驱动 + 0.1s 节流、MetaHealthFX 常态早退零 GPU。
- 本轮发现 2 处遗留：`enemy.gd:327` 每帧 `overlaps_area` 空间查询（200 敌 ≈ 1.2 万次/秒，注释自述对齐原作，登记 perf 候选）；`orbital_strike` 每帧 288 次直接三角函数（1.4s 演出，低优先）。

### 3.5 测试体系 —— 良好（覆盖深，维护盲区明显）

- **强项**：37 断言场景按子系统深覆盖，真实输入管线注入、独立进程隔离、`_wait_real` 计时免疫 time_scale/暂停、池复用指针相等断言、注册表双向差集探针（autoplay）、纯函数覆盖。
- **本轮核心发现**：test/ 目录处于「引擎解析正确性」盲区——`intro_capture.gd`/`return_capture.gd` 自 A7 重构起编译错误（链式调用 void 返回值），CI 三道门禁（静态检查只扫 autoload/+scripts/、import 只 grep 特定警告、断言场景白名单）全部无感知；`autoplay_test.gd` 母舰状态表与枚举漂移（7 项 vs 6 态）导致探针误报/漏报。两处 P1 均已修复。
- **体系性缺口**：21 个断言场景清空 profile 高分无快照还原（唯一正确范式在 ui_capture，L15 登记）；`smoke_test` 一处弱断言（分支内断言无前置条件，L16 登记）；`scheduled_event_trigger`/`dawn_station`/`aim_crosshair`/`cinematic_fx` 无直接测试（可接受，行为有间接覆盖）。

### 3.6 UI/UX 与输入 —— 良好

- **强项**：BackNavigator 纯决策函数与 EXIT_FLOW 逐分支一致、四路收敛（右键/Esc/手柄 B/Android）；页面状态机对称复位；locale 刷新路径完整。
- **本轮发现**：焦点链 2 处缺口（`base_console` 打开无焦点初始化——全项目模态页唯一缺失；`settings_ui` 切语言重建后焦点丢失），已修复；「操作模式」页内容高度溢出面板（895px+ > 480px 容器，面板自适应只放大不缩小），登记待窗口模式实测后定布局（L17）。

### 3.7 文档与一致性 —— 良好（收敛中）

- 9 轮审计的「文档口径统一」尚未收敛干净：断言场景计数 31/35/37 残留 6 处（README×2、README.en×2、CONTRIBUTING×2、CHANGELOG×1、TESTING×1、ci.yml 注释×1，本轮全部统一为 37）；BALANCE_MAP 行号漂移 6 处（生成器未随脚本重跑，本轮重跑修正并给生成器补了效果表键扫描）。
- 文档-代码矛盾本轮共发现 5 处（炮台注释、perf_bench 注释、hud_capture 注释、镜头字幕淡出、AGENTS 与 run.sh 的 set -e 表述），已修 2 处、登记 3 处。

### 3.8 配置 / 构建 / CI / 工具链 —— 良好

- 数值三方一致性执行极严（生成器复现除 6 处行号外零差异）；balance_editor 原子落盘 + 递归校验 + 仅绑 127.0.0.1；资产生成器确定性可复现；CI 按退出码判定 + 失败日志 artifact。
- **流程性缺口**（登记待办）：`release.yml` 的版本同步只改 CI 工作区不 commit，tag 指向的提交 `config/version` 永远滞后（L18）；gdtoolkit 未锁版本（CI 漂移风险）；`run.bat` 无版本判定；`run.sh` 与 `run.command` 引擎候选/版本策略不一致；`release.sh` 缺 `command -v zip` 前置检查；PIL 依赖未在文档声明；`balance_editor.py` 保存路径损坏 JSON 时 500 裸 traceback。
- **本轮改进**：`gen_balance_map.py` 新增声明式效果表 `"cfg": "…"` 键扫描（7 键纳入缺失检测，误报死键消除）。

### 3.9 安全与持久化 —— 良好

- 存档/档案均带版本字段 + 损坏隔离 + 逐字段判型白名单；本地 `user://` 持久化无外部交互；`balance_editor.py` 仅监听 127.0.0.1。
- 未发现注入、路径穿越、凭据泄漏类问题；git 跟踪清单无敏感文件（agent 检查确认）。

### 3.10 提交规范与仓库卫生 —— 良好

- 提交信息遵循 conventional commits 且携带审计编号（`fix: K 系列…`），粒度合理；CHANGELOG 维护中。
- 轻微：`CHANGELOG.md` [Unreleased] 段存在 CI 计数陈旧（本轮已修）；`README.md:161`「CI 规划中」表述陈旧（L31 登记）；`run.sh` 与 AGENTS.md 声称的 `set -euo pipefail` 不符（L24 登记）。

---

## 4. 发现清单与处置

### 4.1 P1（严重，3 项，全部修复并验证）

| 编号 | 位置 | 问题 | 处置 |
| --- | --- | --- | --- |
| L01a | `test/intro_capture.gd:29`、`test/return_capture.gd:30` | 截图工具 `set_shot_durations().append()` 链式调用 void 返回值——A7 重构（2026-07-31）把 setter 改 void 时未同步工具，**编译错误已坏且 CI 三道门禁无感知**（静态检查不扫 test/、import 不解析未引用场景） | ✅ 修复：整表一次传入 `set_shot_durations(shots)`；场景加载验证无 Compile Error |
| L01b | `test/autoplay_test.gd:38-39` | 探针母舰状态表 7 项 vs `Mothership.State` 实际 6 态（无 HOVER）——整体错位一档：真实 STAY 被配 10s 阈值（驻留期必误报 mothership_stuck）、RELEASE 被配 70s（卡死漏报）、日志把 DOCKING 打印为 HOVER | ✅ 修复：重排 6 项 `[20000,10000,10000,70000,10000,30000]` + 下标越界守卫 |
| L02 | `scripts/enemy.gd:393-397`（`_exit_tree`）/ `reactivate()` | **slow_field 静默失效回归**：`_ready` 每实例仅首次入树执行一次（Godot node.cpp `ready_first` 守卫），而 `_exit_tree` 在每次 reparent（池化 release→pool、spawn→Main 均触发）断开 `buffs_changed` 连接且无重连——首个回收循环后 `_slow_field_on` 冻结在陈旧值，之后玩家获得 slow_field buff 时所有被复用过的敌机不受减速。E22 修复（2026-08-03）引入，`hit_logic_test` 只用全新实例未覆盖 | ✅ 修复：`reactivate()` 补幂等重连 + 立即刷新缓存；`pool_reuse_test` 补 2 条池化复用断言；**逆验证**：临时移除修复→2 断言 FAIL，恢复→PASS |

### 4.2 P2（中等，15 项：修复 10 / 登记待办 5）

**已修复：**

| 编号 | 位置 | 问题 | 处置 |
| --- | --- | --- | --- |
| L03 | `scripts/player.gd:1048-1058` `_die` | K03 补全遗漏：死亡路径关 Hitbox/弹反盾但漏关 GrazeArea——尸体位置敌弹飞过仍计擦弹分/出特效/播音效（`_on_graze_entered` 由物理引擎驱动，无 `_dead` 守卫；死亡无重生路径，关闭无副作用） | ✅ 补 `$GrazeArea.monitoring = false`（与 `enter_pod` 对称） |
| L04 | `autoload/game_state.gd:181` `_valid_difficulty_defs` | bool 判型漏网（bool 是 int 子类）：`"score": false` 通过校验 → 得分倍率恒 0 → 里程碑永不触发（Buff 系统软锁）；`"hp": false` → 敌机 0 HP 秒死。E21 已修 spawner 同型 | ✅ 判型统一追加 `not v is bool` |
| L05 | `scripts/spawner.gd:188-189,220-223` | 元素级判型缺失：`unlock_scores` 元素 Dict 崩溃/字符串静默 0（全机型开局解锁）；`_merge_type` 的 hp/speed 嵌套数组同型（敌机 0 HP 秒死）。E04/G06 只修了容器层 | ✅ 元素级 `is int/float and not is bool` 判型，坏值跳过 |
| L06 | `scripts/spawner.gd:176-183` `_apply_balance` | 波次间隔键无下限钳制：`wave_interval_start ≤ 0` 时 `_current_interval` 的 clampf 上界 ≤0 返回负值 → `_wave_timer` 恒 ≤0 → 每帧刷一波（预告线/Timer 无界增长挂死） | ✅ `maxf(…, 0.05)` 钳制间隔/ramp 键 |
| L07 | `scripts/boss.gd:566-571` `_load_patterns` | 模式表只判数组层、元素未判型：`[0, "x"]` 混入时 `_current_pattern()` typed 返回运行时类型错误、`pattern.has()` 崩溃（战斗中途 SCRIPT ERROR） | ✅ 元素级 `is Dictionary` 判型 + 逐元素深拷贝，全坏回退默认表 |
| L08 | `scripts/base_console.gd:297` `show_base` / `scripts/settings_ui.gd:491` `_on_locale_changed` | 焦点链缺口：基地控制台打开无 `grab_focus()`（全项目模态页唯一缺失——手柄玩家进基地后方向键+Enter 无法操作）；设置页切语言重建后焦点停留已 free 按钮（键盘/手柄导航中断） | ✅ 两处补齐 grab_focus；顺带补 `resume_button` locale 刷新（L30） |
| L09 | `scripts/tools/gen_balance_map.py` | 声明式效果表 `"cfg": "…"` 键为扫描盲区：7 个效果表键不参与缺失键检测（拼错/改名不报），`player.dash.cooldown_stack_factor` 被误列疑似死键 | ✅ 新增 `RE_EFFECT_CFG` 正则纳入扫描；重跑后未引用键仅剩 `version` |
| L10 | `docs/BALANCE_MAP.md` | 行号漂移 6 处（93837ba 同批改脚本未重跑生成器；tutorial.gd×4、game_state.gd×2） | ✅ 重跑生成器（432 静态调用、0 缺失键、双向反查一致） |
| L11 | 文档计数口径 | 「31/35/37」断言场景计数残留：README×2、README.en×2、CONTRIBUTING×2、CHANGELOG×1、TESTING.md:117、ci.yml:3 注释 | ✅ 全部统一为 37 |
| L12 | `test/perf_bench.gd:2,24` | 基准注释「30 只敌机」落后于常量 200（压力场景口径失实） | ✅ 注释同步为 200 |

**登记待办（需行为/设计决策或窗口模式实测，不在本轮擅自改动）：**

| 编号 | 位置 | 问题 | 建议 |
| --- | --- | --- | --- |
| L13 | `spawner.gd:394,401` × `mothership.gd:586-597` | 母舰驻留/对接期精英炮塔与编队事件仍可触发，且母舰自动火力（玩家弹阵营）可摧毁事件单位并全额发奖——玩家进保护舱零参与挂机收益 | 事件 `can_trigger` 增加母舰在场检查，或母舰火力排除事件单位（设计决策） |
| L14 | `boss.gd:780` × `boss_movement.gd:112-130` | 段切换瞬间 y 垂直跳变：三型 P1 `_move_band` 增量式偏移（可达 ~280px）→ P2 `_move_bob` 绝对赋值锚线，`reset_press()` 只清 press_offset/bob_phase 不清 band_offset/不补偿当前 y | 段切换时从当前 y 平滑过渡到锚线（行为修改，需 boss 测试验证） |
| L15 | `test/` 21 个断言场景 | 测试清空 profile 高分数据无快照还原（正确范式仅 ui_capture） | 测试开头快照 profile 文件、结尾还原（批量改动） |
| L16 | `test/smoke_test.gd:796-800` | 弱断言：分支内断言 `wb2` 无 `!= null` 前置，弹未生成时用例静默通过 | 补前置 `_check(wb2 != null)` |
| L17 | `scripts/settings_ui.gd:53,77` × `ui_chamfered_panel.gd:39-54` | 「操作模式」页内容 895px+ 溢出 480px 容器，面板被自适应撑出屏幕且不回落（自适应只放大不缩小） | 页容器加 ScrollContainer 或压缩垂直节奏，窗口模式实测像素后定方案 |
| L18 | `.github/workflows/release.yml:56-59` | 版本同步不落地：sed 只改 CI 工作区不 commit，tag 指向的提交 `config/version` 永远滞后于实际版本 | tag 前 commit 版本号改动，或改写 AGENTS.md 口径 |

### 4.3 P3（轻微，40+ 项：修复 6 / 登记 30+）

**已修复**：L30 `resume_button` locale 刷新（随 L08）；L10 相关注释修正。

**登记清单（节选，完整登记见 AUDIT_VAULT L 系列）：**

- **判型/域校验同族遗漏**：`starfield.gd:29-42` far/near_count 无域校验（负值报错/超大 OOM）；`game_state.gd:1203,1221` `apply_run_save` boss_kills/elapsed 负值不钳制（难度曲线下行）；`spawner.gd:509` telegraph_duration 无钳制；`elite_turret_event.gd:119` WEAK_LOCK 无判型；`formation_strike_event.gd:68` craft_counts 无判型；`boss.gd:566-571` hp_mults 只判型不判值域；`enrage_sequence.gd:240` 狂暴计时键无数值域钳制（0 值每帧触发/哑火）；`mothership.gd:385` 时轴键 0 值 NaN。
- **注释失实/文档-代码矛盾**：`turret_battery.gd:6` 类注释仍称 monitoring=false（实为 monitorable）；`hud_capture.gd` 声称还原 profile 实际未还原；`AGENTS.md` 称 run.sh 为 `set -euo pipefail` 实际 `set -e`；`README.md:79` 手柄键位表漏 LT 弹反键；`README.md:161`「CI 规划中」表述陈旧。
- **防御缺口（低概率）**：`player.gd:480-485` dist_falloff 除零（peak==end → NaN）；`aim_frame_layer.gd:166-170` 零距离除零（依赖调用方兜底）；`bullet.gd:64-67` 零速度弹永不销毁；`player.gd:600-622` 返航锁输入期弹反盾 monitoring 残留；`boss_attacks.gd:363-372` volley 无进行中守卫；`formation_strike_event.gd:189` APPROACH_SPEED=0 事件卡死无超时兜底。
- **性能遗留**：`enemy.gd:327` 每帧 overlaps_area 空间查询（perf 候选）；`orbital_strike.gd:219-224` 每帧 288 次直接三角函数；`player.gd:1041-1045` 弹反高光每帧重建点集；`player.gd:742` 右摇杆虚拟准星位移依赖 get_process_delta_time 上下文（高刷屏待实证）。
- **测试侧**：`tutorial_test.gd:75` 调试残留 print；`mothership_summon_test.gd:88` 三支 OR 弱断言；`buff33_test.gd:22` 运行时 InputMap 补动作无收尾清理；`hud_capture.gd` 注释失实；test/ 23 个文件 gdformat 不合规（CI 盲区，建议纳入门禁）；22 处括号内尾随空格。
- **工具链**：`run.bat:16-20` 无版本判定；`run.sh`/`run.command` 引擎候选与版本策略不一致；`release.sh:38` zip 无前置检查；贴图/音频生成器 PIL 依赖未声明；`balance_editor.py:276` 损坏 JSON 500 裸异常；`ci.yml:33` gdtoolkit 未锁版本；`boss_fire.gd:28,50` 弹幕几何魔法数；`spawner.gd:548` Boss 预警 2.0s 硬编码；`enemy_move_strategy.gd` 机动参数硬编码；`bullet.gd:323` 爆炸弹对炮台/编队机零 AoE（注释口径待确认）；`return_cinematic.gd:151` 镜头 2→3 字幕 0 时长瞬隐；`main.gd:684-686` 返航覆盖提前离舰冷却折扣（行为决策）。

---

## 5. 修复与验证记录

### 5.1 改动文件（20 个）

源码 8：`enemy.gd`、`player.gd`、`spawner.gd`、`boss.gd`、`base_console.gd`、`settings_ui.gd`、`game_state.gd`、`gen_balance_map.py`；测试 6：`pool_reuse_test.gd`（+2 断言）、`intro_capture.gd`、`return_capture.gd`、`autoplay_test.gd`、`perf_bench.gd`；文档/配置 6：`docs/BALANCE_MAP.md`（重跑）、`docs/TESTING.md`、`README.md`、`README.en.md`、`CONTRIBUTING.md`、`CHANGELOG.md`、`ci.yml` 注释。

### 5.2 验证矩阵

| 验证项 | 结果 |
| --- | --- |
| `godot --headless --import --path .` | 0 error / 0 warning（门禁干净） |
| `--quit-after 300` 主场景冒烟 | 0 错误 |
| `smoke_test` | PASS（57s） |
| `pool_reuse_test` | **14 PASS 0 FAIL**（含 2 条新 L02 断言；逆验证：移除修复→FAIL，恢复→PASS） |
| `enemy_combat` / `difficulty` / `graze` | PASS |
| `boss_pattern` / `boss_phase` / `boss_registry` / `base_system` / `i18n` / `back_navigation` / `entry_animation` | PASS |
| `intro_capture.tscn` / `return_capture.tscn` 加载 | 无 Compile Error（窗口模式实截待人工核对） |
| `autoplay_test.tscn`（--quit-after 10 编译探针） | 无 Parse/Compile Error |
| `perf_bench` | 正常出 PERF_RESULT（1800 帧 / avg 1.003ms） |
| `gdformat --check` + `gdlint`（autoload/ + scripts/，CI 口径） | 全绿 |

---

## 6. 后续建议

1. **测试门禁盲区（最高优先）**：将 `test/` 纳入 `gdformat --check` 与 import 门禁的 SCRIPT ERROR grep，或 CI 增加 `--check-only` 全量编译步骤——本轮两个 P1 均因该盲区长期潜伏。
2. **「键清单」式复查**：判型/钳制/查表惯例已稳定，建议后续审计按 `data/balance.json` 全键清单逐键核对消费方与域校验，而非按文件通读。
3. **行为性待办收敛**：L13（母舰×事件互斥）、L14（段切换 y 跳变）、L18（release 版本同步）三项涉及设计/CI 决策，建议列入 `docs/ROADMAP.md` 或专项计划处理。
4. **测试副作用治理**：L15 的 profile 快照还原应作为测试收尾统一范式推广。
