# InfiAir 独立审计报告（2026-08-05 R 系列）

> 依据用户指示「goal 模式，参照上一次审计没涉及的内容进行独立审计，仅提交不推送」执行；按 `docs/AUDIT_REVIEW_SOP.md` 流程（并行审计 → 分类 → 批处理提交 → 迭代修复+即时验证 → 归档回填）。本批聚焦上一轮 Q 系列（2026-08-05 全仓库 deep review，dac5d3f 全量修复）**未涉及的内容**：金库登记遗留项复核、Q 系列 §7 待验证点、资产/场景/发布打包/离线工具链区域、Q 修复批次新代码面。发现登记：金库 R 系列。
> 方法：6 路并行只读审计（金库遗留复核 / 资产与场景 / 发布打包与项目配置 / 离线工具链 / Q 修复批次复核 / 待验证点与数据质量），主控对全部 P2 与关键 P3 亲自读码复核 + 生成器实跑验证（音频重生成逐字节对比、BALANCE_MAP 实跑、sprite 生成器非根目录实跑）。

## 1. 审计范围与基线

- 范围：Q 系列排除项（M07-M10/L13-L18/C17/L 系列 P3 类别清单）+ Q 系列 §7 待验证点 5 项 + 未覆盖区域（`assets/` 26 个 .import、2 shader、`scenes/` 11 场景、`export_presets.cfg`、`release.sh`、`run.bat`、`packaging/`、`scripts/tools/` 6 工具、`project.godot`、CI 工作流）+ dac5d3f 修复批次 30 项复核 + 翻译数据完整性。
- 基线：`--headless --import` 0 error；git 工作树干净（HEAD `dac5d3f`）。
- 排除：金库已登记已修项（A-P 系列）、Q 系列已修项（Q01-Q30/P4）。

## 2. 发现总览

| 严重度 | 数量 | 摘要 |
| --- | --- | --- |
| P2 中等 | 3 | test/ 全量进发布包（PCK 实锤）；BGM 和弦零谷 + 生成器-资产漂移；sprite 生成器 cwd 相对路径 |
| P3 轻微 | 12 | Q19/Q23 修复不完整 ×2、判型族 10 处、防御缺口 3 处、工具链 6 处、测试规范 3 处、注释失实 6 处、死代码清理 3 项、WEAK_LOCK 判型 |
| P4 观察 | 15 | 镜像字面量、数组长度校验、load_steps 计数、文档计数、生成物漂移（BALANCE_MAP 行号）等 |
| 复核误报 | 3 | dist_falloff/aim_frame「除零」（分支覆盖无除零路径）、Q09 手柄死锁残留（已随修复消解）、「12 个场景」计数（实为 11） |

## 3. P2 发现

### R01 test/ 场景与脚本全量进发布包 —— 打包内容泄露

- **位置**：`export_presets.cfg:8-10,36-38`（两 preset `export_filter="all_resources"` + `exclude_filter=""`）
- **类别**：纯 bug（发布产物污染）
- **证据**：现有 3.26 发布包 PCK 目录表明文含 `test/window_size_test.tscn.remap`；PCK 内 50 个 `.godot/exported` 场景（autoplay/smoke/balance/capture/perf_bench 全部测试 + 生产场景）；strings 提取 91 个唯一 `res://test/*.gd` 路径。test/ 无 `.gdignore`（docs/ 有，2026-08-03 修复了 docs 泄露，test/ 遗漏），重新导出仍会泄露。
- **修复**：两 preset `exclude_filter="test/*"`（test/ 需被编辑器/CI 加载，不能用 .gdignore）。

### R02 BGM 和弦交叉淡化零谷 + 生成器-资产漂移

- **位置**：`scripts/tools/generate_audio.py:206-214` × `assets/audio/bgm_loop.wav`
- **类别**：纯 bug（已烘焙进资产）
- **证据**：`chord_weight` 有效区间 `[0, CHORD_DUR)` 严格截断——相邻和弦在交界点权重和 = 0（每 5s 一次 pad/bass 塌陷）。旧资产实测 t=5.0 边界 RMS 塌陷（0.27→0.045 相对量级）。修复区间扩至 `CHORD_DUR + XFade` 后交界处权重和恒为 1。
- **顺带发现（生成器-资产漂移）**：HEAD 生成器全量重跑的 bf×3 与提交资产逐字节不同（max 差 ~3000/16bit，~3% RMS）——资产为「random 流起点独立生成」的历史产物，main() 全序列流的 random 位置不同。修复：main() 在 bf×3 前重置 `random.seed(20260720)`（bf 为末段，不影响其他音效），重跑后 bf×3 与资产逐字节一致；仅 bgm_loop.wav 变化（零谷消除，实测 t=5.0 RMS 平滑）。
- **验证**：重生成后 `git status` 仅 `assets/audio/bgm_loop.wav` 变化；独立流/全序列流生成对比实验确认对齐。

### R03 sprite 生成器输出路径 cwd 相对 —— 非仓库根运行落盘错误位置

- **位置**：`generate_enemy_sprites.py:781`、`generate_player_sprite.py:237`、`generate_mothership_sprite.py:228`
- **类别**：纯 bug（工具链口径不一致——同目录其余 3 工具均 `Path(__file__)` 锚定）
- **修复**：三处统一 `os.path.join(os.path.dirname(__file__), "..", "..", "assets", "sprites")`；**验证**：从 `/tmp` 运行三个生成器，落盘绝对路径正确、14 个 PNG 重生成后逐字节不变（git 无 diff）。

## 4. P3 发现与处置

| 编号 | 位置 | 类别 | 描述 | 处置 |
| --- | --- | --- | --- | --- |
| R04 | `enemy_pool.gd:46-47` | 纯 bug（Q19 修复不完整） | Q19 只修回收侧：spawn 侧 `reparent` 时 `_repooling=false` → `_exit_tree` 走 `unbind_enemy` → 每次池化 spawn 发无配对 `entity_unregistered`（reactivate 只 register 不发信号，ENTITY_MANAGER §4.2 矛盾仍存） | ✅ 修复：spawn 侧 reparent 包 `set_repooling(true/false)`（与 `_reparent_deferred` 对称） |
| R05 | `startup_flow_test.gd:86-87` | 纯 bug（Q23 修复不完整） | `delete_save()` 在 `_backup_user_files()` **之前** → savegame.json 快照为空，还原后进行中存档仍缺失（Q23 登记「三测试全部还原」言过其实） | ✅ 修复：快照移到 `_ready` 首行（捕获全部改动路径之前的状态） |
| R06 | `starfield.gd:29-30`、`spawner.gd:546`、`elite_turret_event.gd:119`、`boss.gd:560-597`（interval×6）、`boss.gd:323-331`（hp_mults 正值域）、`game_state.gd:1579-1581`、`bullet.gd:140` | 纯 bug（判型/域校验同族，L 系列登记遗留） | starfield count 无判型（负值负尺寸 resize）；telegraph_duration 无下限（0/负立即触发）；WEAK_LOCK 裸 cfg（非 Dictionary 时消费方崩溃）；狂暴 interval 0/负 → 每帧攻击风暴；hp_mults 0/负 → max_hp≤0 → Boss 免疫伤害（Q02 同根因的缺失分支）；存档 score/kills/boss_kills 负值；bullet 零速弹永驻视野 | ✅ 修复：判型/钳制与既有防护族（G06/H11/E04/L04/Q02）口径一致，默认值/回退值逐位不变，行为零变化 |
| R07 | `player.gd:666-667`、`boss_attacks.gd:428`、`boss.gd:1012` | 防御缺口（L 系列登记遗留） | ①锁输入期（母舰召唤/返航过场）`_physics_process` 早退使弹反盾 monitoring 停留在锁定前值，ACTIVE 期锁定则盾全程被动生效；②`_start_minion_volley` 无进行中守卫，待发期重复触发清空重召；③`_begin_escape` 只 abort 狂暴序列，常规攻击（瞄准线/蓄力/齐射计时）残留 | ✅ 修复：①锁输入时强制关盾 monitoring（解锁后由 phase 驱动恢复）；②`_volley_timer > 0` 早退；③补 `_attacks.cancel_all()` |
| R08 | `balance_editor.py:276` | 纯 bug（L 系列登记遗留） | 读侧 `json.loads` 无 try——balance.json 损坏时裸 traceback 500（Q/P4 只修了 gen_balance_map 侧） | ✅ 修复：读失败友好 400 |
| R09 | `release.sh:34,38`、`run.bat`、`ci.yml:33` | 工具链（L 系列登记遗留） | release.sh tar/zip 无前置检查（缺工具时 set -e 中止 + stage 残留无诊断）+ 版本号不自动读 project.godot；run.bat 无版本判定 + `if errorlevel 1 pause` 使退出码归零；gdtoolkit 未锁版本 | ✅ 修复：`command -v` 前置检查 + `VERSION` 自动读 project.godot；run.bat 探测 `--version` <4.6 警告 + `endlocal & exit /b %EXIT_CODE%` 保真退出码；`gdtoolkit==4.5.0` |
| R10 | `tutorial_test.gd:79`、`mothership_summon_test.gd:84`、`buff33_test.gd:24-25` | 测试规范（L 系列登记遗留） | 调试 print 残留；穿梭门三重 OR 弱断言（null/失效/CLOSING 任一即过）；InputMap.add_action 无收尾 | ✅ 修复：删 print；拆为状态断言（gate 已有存在性断言）；`added_give_up` 标记收尾 erase |
| R11 | `turret_battery.gd:6`、`boss_movement.gd:97`、`elite_turret_event.gd:199`、`hud_capture.gd:2,34,60`、`autoplay_test.gd:5`、`README.md:80` | 文档-代码矛盾（L 系列登记遗留） | monitoring 表述未随 K09 改 monitorable；「战斗期独占 y」未含逃跑警告期；「负数钳 0 防负循环」表述失实；「全 16 种 buff」实为 15 distinct/池 19；手柄键表漏 LT 弹反 | ✅ 修复：全部同步 |
| R12 | `boss.gd:113-117,590` + `balance.json:647`、`back_navigator.gd:19,94-96`、`scripts/start_panel.gd`、`scripts/start_radar.gd`、`translations.csv:171-172` | 死代码/孤儿（M07/M08/M09 落地） | RING_BURST_COUNT 与 json 键 `boss.ring_burst.count` 无消费方（Q01 后死数据）；CONFIRM_EXIT 枚举+分支决策表无可达路径；start_panel/start_radar 退役后零引用；SET_LANGUAGE_ZH/EN 零 tr() 引用 | ✅ 修复：删除（EXIT_FLOW.md 状态机清单同步；BALANCE_MAP 重跑 469 静态调用 0 缺失） |

## 5. P4 观察与处置

- **修复**：`Bullet.COLLISION_RADIUS := 6.0` 常量（Q §7 待验证点 5：player.gd:1007 擦弹环形带与 bullet.gd:230 碰撞半径镜像字面量，双改风险解除，互相引用）；`enemy_move_strategy` freqs/phases 数组长度 ≥3 校验（Q02 同族未推广）；`main.tscn load_steps 20→18`、`player.tscn 7→8`（陈旧计数）；`AGENTS.md:25` 53→54；`shell-scripts.md:12` version_ok 归属修正（在 run.command）；`release.yml` L18 旧注释清理；`AUDIT_VAULT` L 系列状态表同步（L13/L14/L15/L16/L18 标 ✅）；BALANCE_MAP 行号漂移重跑（Q08 同款：dac5d3f 改码后未重跑，diff 24+24 行）。
- **登记观察不修**：mothership 时轴 30+ 键 0 值（手改 json 触发、改动面大收益低，维持 L 系列登记）；boss_fire 20°/15° 弹幕几何魔法数（几何常量非平衡数值，入库收益低）；SCORE_CAP 乘法后钳制（倍率 ≥1e13 理论溢出，现实量级防御成立）；smoke `== +33` 精确断言（受控条件确定性成立，TESTING.md 已登记 flake 基线）；遭遇自动触发暴露面（各测试已核实安全，建议后续补契约断言）；`.godot/imported` 孤儿 ctex×4（.gitignore 排除，无害）；gen_balance_map 惰性正则局限/balance_editor 新增键放行（工具启发式已知局限）；builds/ 旧产物过期（3.26 含 docs 截图已随 .gdignore 修复，test/ 泄露随 R01 修复，重出即净）。

## 6. 复核结论（Q 系列 §7 待验证点）

1. **Q09 焦点细节**：已定论——修复后 welcome 的 ui_cancel 分支优先于 exit_confirm，隐藏层 grab_focus 路径不可达，无手柄死锁残留（全部 grab_focus 调用点目标在调用时刻可见）。
2. **遭遇自动触发暴露面**：各长跑测试已逐一核实（smoke spawner 处理累计 ~6-8s ≪ 40s；perf_bench 2 帧后停；autoplay 主动计数+互斥契约），无断言确定性破坏。
3. **Q27 实机目检**：随修复消解（绝对赋值 ±30px 确定性），人工项已登记。
4. **smoke ==+33**：受控条件（spawner 停/清弹/无敌/单次命中）下取整确定性成立，非 flake 源。
5. **6.0 镜像字面量**：语义真镜像，双改风险成立 → R13 常量化修复。

## 7. 复核误报登记

- **dist_falloff/aim_frame「除零」**（L 系列判型族清单项）：数学复核——`d <= peak` 与 `d >= end` 两分支覆盖全部定义域，`peak == end` 时 lerpf 不可达，无除零路径；配置损坏（NaN）才可达，属理论级，登记不修。
- **Q09「手柄死锁」**：不成立（见 §6.1）。
- **R2「12 个场景」**：scenes/ 实为 11 个，无第 12 个（非缺陷，计数澄清）。
- **PIL 依赖未声明**（L 系列清单项）：已在 ARCHITECTURE.md:8,86 / DESIGN_BASELINE.md:97 声明，失效。
- **run.sh/run.command 策略不一致**：已收敛（对齐提交 + shell-scripts.md 声明 deliberate exception），失效。

## 8. 分类判定（按 AUDIT_REVIEW_SOP Phase 2）

- **真 bug（修复）**：R01-R15 全部（P4 观察除外）。
- **登记不修（论证后收敛）**：§5 观察项 + §7 误报项 + L17（窗口实测前维持待办）+ M10（人工验证项保留）+ C17（合理模式维持）。
- **本批无「需设计拍板」项**（Q29/Q30 类入库决策本批未触及新区域）。

## 9. 验证

- 五层门禁：gdformat --check（128 文件）+ gdlint 全绿；`--headless --import` 0 error；18 个定向场景全 PASS（startup_flow/mothership_summon/tutorial/buff33/entity_manager/pool_reuse/boss_pattern/boss_phase/boss_enrage/boss_phase_transition/difficulty/i18n/esc_navigation/base_task_refresh/event_manager/user_session/welcome_flow/smoke）；全量 45 断言场景 0 FAIL（见提交记录）。
- 生成器实证：BALANCE_MAP 实跑重生成（469 静态调用、0 缺失键）；音频全量重生成后 `git status` 仅 bgm_loop.wav 变化（bf×3 逐字节对齐资产）；sprite 三生成器从 /tmp 实跑落盘正确且资产零变化；release.sh `bash -n` + Python 工具 `py_compile` 通过。
- 测试过程中修复的测试自身问题：buff33 `_added_give_up` 命名违反 gdlint function-variable-name（改 `added_give_up`）。
