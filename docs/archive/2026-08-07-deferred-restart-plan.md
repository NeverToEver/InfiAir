# 搁置项重启计划（2026-08-07，deferred items restart）

> 背景：用户指示「查找最新被明确标记为暂缓推进的事务（非 3.28 发布推迟），确认其中真实未完成项，筛选高价值目标，书写计划文档并以 goal 全量推进」。
> 本文件为本次执行清单（参照 `docs/archive/2026-08-04-{...}-plan.md` 模式）；完成后回填 `docs/AUDIT_VAULT.md` 对应条目、`docs/ROADMAP.md` Phase 3 并归档本文件。
>
> **执行状态（2026-08-07 归档）**：T1-T4 全部落地并验证（S01-S04 回填 AUDIT_VAULT；ROADMAP Phase 3 mobile touch 登记 landed；五层门禁全绿、47 断言场景）。

## 1. 搁置清单盘点（2026-08-07 复核，来源 × 现状 × 处置）

| 来源登记 | 事项 | 复核现状（读码/读档取证） | 价值评估 | 处置 |
| --- | --- | --- | --- | --- |
| ROADMAP Phase 3 | **移动端操控（mobile touch）**——content evolution 唯一剩余 cut，「restart needs re-scoping + registration here」 | 真实未完成；player 输入已全走 Input action 系（`move_*`/`aim_*`/`boost`/`fine_move`/`dash`/`parry`），`aim_point()` 有 `aim_point_override` 注入点 | **高**：平台扩展；输入抽象已集中，注入虚拟输入即可 | **T1 重启立项 + 落地** |
| AUDIT_VAULT L17（R07 复核） | 设置页「操作模式」页溢出 480px 容器（手柄/无障碍段后更甚），面板自适应撑到 ~1150px 超屏 | 真实未完成：`settings_ui.gd:225 _build_modes_page()` 为裸 VBoxContainer，无滚动包裹，段数多（模式/语言/辅助瞄准/显示/鼠标锁/手柄） | **高**：明确 UI 缺陷，窗口模式可实测 | **T2 修复** |
| DESIGN_BASELINE §7.1 | A5 残余：hud/pause_ui 等对 Main/服务的引用仍经组查找，未全量显式注入 | 部分完成（injection 已落地 `bdb0274`）；残留 `get_first_node_in_group("hud"/"settings_ui"/"spawner"/"mothership")` 若干 | 中：架构收尾（DESIGN_BASELINE §7 唯一 open item）；组查找多数为低风险合理模式，需逐点判定 | **T4 收敛（谨慎，行为零变化）** |
| R 系列 #9 | smoke 敏感段补遭遇契约断言（自动遭遇触发暴露面） | 真实未完成（登记待办） | 中：防回归 | **T3a** |
| 2026-08-06 #7 | 死亡清理小窗（L-b）与遭遇互斥（L-d）未加独立测试 | 真实未完成（登记待办；现由 main 流程回归间接覆盖） | 中：防回归 | **T3b** |
| 竞品审计 P2-8 | 俄语第二外语（⏸ 暂缓，若进入俄区市场再评估） | 用户决策未变（无市场信号） | 依赖市场决策 | **不推进**（维持暂缓） |
| M10 / DESIGN_BASELINE §8.2 / §7.3 | INTRO/过场 stage 4 人工验证（低端机重测 + gamepad/mobile 输入检查） | 发布前人工实机项，非代码任务 | 需实机 | **不推进**（T1 落地后并入发布前人工验证项） |
| 2026-08-06 #2 | 里程碑循环档边界增量倒挂（登记待设计拍板） | 疑似有意设计，SOP「不盲调平衡」 | 需设计拍板 | **不推进**（维持登记） |
| M07/M08/M09、L 系列 P3 判型族/防御缺口/工具链/注释族 | 死代码/孤儿/判型等 | **已全部处置**（R06-R12 2026-08-05） | — | 非未完成，排除 |
| C34 | boss_pattern_test 硬编码 | ⚠️ 部分完成（场景 4/5 补注释、其余判定为逻辑验证锚点保留） | 设计确认 | 排除 |

## 2. 目标范围与验收总则

本次 goal 推进 T1（主）+ T2 + T3a + T3b + T4；每项落地后执行定向验证，批次底部全量门禁。

- **五层门禁**：① `gdformat --check`（w=140）→ ② `gdlint` → ③ `--headless --import` 0 error（unsafe/untyped 零容忍）→ ④ `--quit-after 300` 0 error → ⑤ 全量断言场景 0 FAIL（45 场景基线，新增场景后同步 TESTING/README 口径）。
- 新增 `.gd` 必须 gdformat 合规；`translations.csv` 新键双列（zh/en）补齐。
- 新功能带断言场景（`test/*_test.tscn` + `*.gd`，参照既有模式）；改测试收尾 profile 快照还原（Q23 范式）。
- 文档登记：DESIGN_BASELINE（§7/§8）、ROADMAP（Phase 3 状态）、CHANGELOG、AGENTS（如需）、BALANCE_MAP（若动 balance 相关）；本文件完成后归档。
- 不动工作区未提交文件（v3.28 暂缓发布伴随的 `release.sh`/`run.sh`/`run.bat`/`run.command`/`CHANGELOG.md`/`.agents/shell-scripts.md` 修改），避免混入本次批次。

## 3. T1 — mobile touch 移动端操控（重启立项）

### 3.1 立项范围（re-scope）

原 content evolution cut 项「mobile touch」无既定规格，本次按「桌面键鼠/手柄零回归 + 触屏设备可用」立项，范围限定为本环境可全量实现并验证的部分：

1. **虚拟输入层**：新 `scripts/virtual_controls.gd`（Control 层，仅触屏设备/模拟器启用）——
   - 左虚拟摇杆：映射 `move_left/right/up/down`（读触摸起点/当前点差分，`Input.parse_input_event` 注入 `InputEventAction` 或等价路径；与 `get_vector` 语义一致：纯方向 + 归一化幅度）。
   - 右虚拟摇杆：映射 `aim_*` 增量（复用既有右摇杆虚拟准星语义：增量驱动，松开即停，`_aim_joy_speed` 可调）。
   - 虚拟按钮：`boost`/`fine_move`/`dash`/`parry`（触摸按下注入 `is_action_pressed`/`just_pressed` 语义）。
   - 启用判定：`DisplayServer` 触摸能力/`Input` 仿真标记；无触屏设备时整层隐藏零开销。
2. **触摸瞄准**：`aim_point()` 无鼠标来源时（触屏+右摇杆），触摸点/最近敌人磁吸兜底（复用 `AimFrameLayer` 磁吸）；`aim_point_override` 注入点复用。
3. **UI 适配**：welcome/设置等既有导航已满足焦点约定（L08 全项目模态聚焦），触摸下按钮天然可点；仅核对无鼠标依赖路径（如 mouse_trap、悬停）在触屏会话的退化。
4. **i18n**：新增键（摇杆/按钮说明、设置页「触控」段开关）zh/en 双列。
5. **测试**：新断言场景 `test/virtual_controls_test.tscn`——注入 `InputEventScreenTouch`/`ScreenDrag` 模拟：摇杆方向/幅度映射、按钮按下/释放、右摇杆瞄准增量、触屏禁用时不拦截桌面输入（回归）。
6. **文档**：本计划 §3 + 落地后 ROADMAP Phase 3 登记（mobile touch landed）+ DESIGN_BASELINE §8 登记 + README 特性清单。

### 3.2 明确不做（边界）

- 真机手感/低端机性能：登记为发布前人工验证项（并入 M10 stage 4）。
- 触屏原生 UI 重排（仅叠加虚拟层）；不改变 1920×1080 固定视口。
- 不做 Web/移动导出目标（维持 Linux/Windows 双平台发布，C_SHARP_ASSESSMENT 结论不变）。

## 4. T2 — 设置页 modes 页溢出（L17）

- 方案：`make_page_shell` 或 settings_ui 侧给 body 增加最大高度约束 + ScrollContainer 包裹 modes 页内容（`_build_modes_page` 返回值包 `ScrollContainer`，`size_flags_vertical = EXPAND_FILL`，内容可滚动）；同时压缩页内节奏（separation 14→10）减轻滚动量。controls/about 页若同时超限一并处理。
- 验收：窗口模式实测（`test/window_size_test.tscn` 同款截图路径或 `/tmp` 截图目检）面板不超过视口、滚动可达全部控件；手柄焦点（L08）在滚动容器内可用（ScrollContainer 需 `focus_mode` 链保持）。

## 5. T3 — 测试待办补齐

- **T3a**：smoke 敏感段补遭遇契约断言——在 `smoke_test.gd`（或独立遭遇断言场景）断言「自动遭遇（精英/编队）触发时段不与母舰驻留/返航关键段重叠」（R 系列 #9 描述：各长跑测试已核实安全，契约化防回归）。
- **T3b**：死亡清理小窗（main.gd `_on_player_died` 段 L-b）与遭遇互斥（L-d：事件进行中禁蓄力）独立断言——构造 give_up/dock 同帧 + 事件进行中蓄力场景，断言小窗清理与蓄力被拒。

## 6. T4 — A5 残余依赖收敛（谨慎）

- 逐点盘点 `get_first_node_in_group(...)` 消费点（hud/settings_ui/spawner/mothership 等），区分：
  - **收敛**：可注入且改动面小、行为零变化者（如 Mothership 对 HUD 的重复查找 → 成员缓存/注入）。
  - **保留**：Godot 组查找为合理模式的低风险点（R 系列 #3 先例：back_navigator 裸 get_node 维持）。
- 验收：行为零变化 + 全量门禁绿 + DESIGN_BASELINE §7.1 状态更新（⚠️ → ✅ 或注明收敛范围）。

## 7. 执行顺序

1. T2（小而明确，先清 UI 缺陷）→ 2. T3a/T3b（测试契约）→ 3. T4（架构收尾）→ 4. T1（主目标，占大头：虚拟输入层 → 触摸瞄准 → UI/i18n → 测试 → 文档）。
   批次底部全量门禁；每项即时定向验证（参照 AUDIT_REVIEW_SOP）。

## 8. 验证

- 全量断言场景 0 FAIL（45 → 46+ 含新场景）；`--headless --import` / `--quit-after 300` 0 error；gdformat/gdlint 全绿。
- T1 模拟触摸断言通过 + 桌面输入回归断言通过（键鼠/手柄行为零变化）。
- T2 窗口截图实测不溢出。
- 文档同步：ROADMAP / DESIGN_BASELINE / AUDIT_VAULT 对应条目回填；CHANGELOG 条目。
