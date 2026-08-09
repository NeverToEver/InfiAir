# 2026-08-09 C# 核心架构重构报告（Y 系列）

> 承接 X 系列（安全项修复 90e140b）。用户指示：核心架构需同样操作（审计→计划→实施→验证），现状逻辑冗杂、结构健康度不足。
> 方法：3 路并行只读边界侦察（GameState 拆分可行性 / Boss 链 typed 化边界 / UI 编排+格式化+伤害三合一）→ 分五阶段实施，每阶段独立验证。
> 基线：build 0w/0e、test 80/80、55 断言场景全绿（X 系列提交时实测）。

## 一、健康度问题与处置

| # | 债务 | 规模 | 处置 |
|---|---|---|---|
| 1 | 格式化器重复 ×11 | 11 份实现（4 种越界兜底并存）+ 47 处调用点 | **收敛为 core 单一实现** |
| 2 | GameState 上帝类 | 2674 行单文件、~250 public 成员 | **partial 按域拆 9 文件** |
| 3 | Boss 链双命名桥 ×110 + 动态派发 | 8 snake 方法 + 92 UPPER_SNAKE 属性 + 2 snake 属性 + 8 Fire_* 转发；组件每帧 1-11 次 Get/Call | **全部删除 + typed 化** |
| 4 | 伤害分派 switch ×3 | 直击/溅射/激光目标集合一致 | **收敛为 EntityDamage.Dispatch** |
| 5 | UI 编排在 UI 层 | 结算链 6 步在 GameOverUi；存档取值编排在 PauseUi | **下沉 GameState.SettleRun/SaveRun** |

## 二、阶段实施摘要

### 阶段 1 — 格式化器收敛（11 → 1）
- 新增 `csharp/core/Text/GdFormat.cs`（纯 .NET 零 Godot 依赖，xUnit 直测）：语义基准 = 原 Hud 标准版；越界兜底统一 `"?"`。
- **行为修复**：原 11 份实现的 `%.Nf` 解析存在 j 偏移 bug（从 `'.'` 处而非其后扫描数字）——`%.2f` 永不匹配，`UI_DIFF_FMT` 实际渲染为字面「难度 x%.2f · 中」（GDScript 迁移缺陷）；新实现修正偏移（i+2），HUD 难度标签恢复正确渲染。
- 删除 11 份本地实现（Hud/Main/BaseConsole/GameState/Mothership/Intro/Return/Tutorial/SettingsUi×2/BuffSelect.GsFormat）；47 处调用点迁移；断 GameOverUi→BuffSelect 跨类耦合。
- 验证：`dotnet test` 89/89（新增 GdFormatTests 9 用例）；受影响场景 11/11 PASS。

### 阶段 2 — 伤害分派统一
- 新增 `csharp/godot/EntityDamage.cs`：`Dispatch(GodotObject, int, float scoreScale = 1.0f)`——四类（Enemy/Boss/TurretBattery/FormationCraft）switch，未知类型静默跳过。
- 迁移三处（Bullet 直击/溅射 + LaserWeapon 激光）；**保留语义**：`_explode` 的 `is not Enemy` 排除 Boss 过滤留调用侧；激光路径不传 ScoreScale（击杀不加分缩放为既有语义）。
- 验证：5 受影响场景 PASS。

### 阶段 3 — GameState partial 拆分（2674 行 → 9 文件）
- 壳 430 行（信号/服务字段/生命周期/实体转发）+ `GameState.Constants/State/Missions/Difficulty/Settings/Input/Users/Save.cs`（99-544 行/文件）。
- 纯移动零行为差异；`_instance` 静态与 `_Ready` 顺序链留壳；测试零影响（无 InternalsVisibleTo，全公开 API）。
- **工具链修复**：`gen_balance_map.py` 硬编码文件名 `("GameState.cs", "BalanceService.cs")` 不识别切分后的 `GameState.*.cs`（468→452 调用、未引用键 1→38）→ 扩展前缀匹配后恢复 468/1/0。
- 验证：编译 0w/0e + format 零 diff + 5 场景 PASS + BALANCE_MAP 恢复。

### 阶段 4 — Boss 链 typed 化 + 226 桥删除（净删 397 行）
- Boss.cs 桥区（110 桥）全删，原位新增 8 个 PascalCase Fire 转发（测试契约替代，签名与 BossFire 逐参一致）。
- BossMovement/BossAttacks/EnrageSequence：签名 `GodotObject`/`Node2D` → `Boss`，StringName 定义区全删（113 个），~130 处 `Get(Prop)`/`Call(Meth)` → typed 直访；`Call("SpawnMinionAt")` 字符串派发 ×2 → typed；边界外 `summon_waves()` + `world_scale` ×2 桥（零消费方）一并删除。
- 测试：BossPatternTest 26 处 + HitLogicTest 18 处 UPPER_SNAKE/Fire_* → PascalCase（断言语义不动）。
- 验证：build 0w/0e（编译期兜底，StringName 删除后无遗漏）+ format 零 diff + 7 场景 PASS（Boss 5 + hit_logic + csharp_call）。

### 阶段 5 — UI 编排下沉
- `GameState.SettleRun()`：DeleteSave → RecordScore → RecordGameOver → SubmitHighscore 原子链 + `(NewRecord, Rank)` 快照；GameOverUi.OnPlayerDied 收敛为单次调用（UI 文本/SFX 留 UI 层；PlayerDied 订阅者角色与同步时序不变）。
- `GameState.SaveRun()` 无参版：内部 PlayerRef→FuelAmount() + group "spawner"→Elapsed()，缺节点兜底 100/0；PauseUi.OnSavePressed 收敛；两参版保留（Main 返航存档持实例直传 + 测试契约）。
- 验证：6 受影响场景 PASS（smoke/buff33/back_navigation/startup_flow/user_session/base_system）。

## 三、行为差异显式清单

| 差异 | 性质 | 说明 |
|---|---|---|
| `%.Nf` 解析修复 | Bug 修复 | 原 11 份实现 j 偏移 bug → HUD 难度标签从字面「x%.2f」恢复正确渲染 |
| 越界兜底统一 `"?"` | 行为收紧 | 原 4 种并存（Hud 抛 IndexOutOfRange 为缺陷）→ 主流 6 份的 "?" 语义 |
| `%d` 收到非数字参数仍抛 FormatException | 保持 | 与原实现一致，不吞异常 |
| 其余全部 | 零差异 | 纯转发删除/纯移动/逐位等价收敛 |

## 四、明确不做（deferred）
- **任务域服务化试点**（MissionService）：收益不确定（987 处链式引用不变、热路径 +1 层、~30 私有字段连带），partial 拆分已提供干净域边界，留待评估。
- **阶段状态机单一化**（Main/Spawner 布尔 → 枚举）：行为风险最高（树暂停隐式门 + 多条件守卫 + UI Visible 交叉），需单独设计轮。
- 两池 `_free.Contains` O(n)（上限 500 实际影响小）、Explosion/VirtualControls 静态持有（U07 规则擦边）、GameState 服务化深度下沉。

## 五、验证清单（实测，全量场景结果见文末追加）

| 门禁 | 结果 |
|---|---|
| `dotnet build`（warnings-as-errors） | ✅ 0 warning / 0 error |
| `dotnet test tests-csharp/` | ✅ 89/89 |
| `dotnet format` 三工程 `--verify-no-changes` | ✅ 零 diff |
| 受影响断言场景（Y1-Y5 分阶段） | ✅ 11 + 5 + 5 + 7 + 6 全 PASS |
| `gen_balance_map.py` | ✅ 468 静态调用 / 3 动态 / 1 未引用 / 0 缺失（工具修复后） |
| 全量断言场景 | 见文末追加 |


---

## 六、全量断言场景回归（实测追加）

**55/55 全 PASS，0 失败**（含 autoplay 跳过口径外的全部 `*_test.tscn`；每个场景 exit 0 + 引擎错误日志扫描零命中）：

back_navigation / balance / base_system / base_task_refresh / boss_enrage / boss_pattern / boss_phase / boss_phase_transition / boss_registry / buff33 / buff_effects / buff_panel / buff_visuals / csharp_call / csharp_interop / difficulty / elite_turret_event / encounter_flow_contract / enemy_combat / entity_manager / entry_animation / esc_navigation / event_manager / fog_event / formation_strike_event / grace_period / graze / hit_logic / i18n / intro_cinematic / keybind / meta_health_fx / mothership_summon / mothership_upgrade / mouse_lock / orbital_strike / parry / path_resolver_interop / pool_reuse / progression_interop / return_cinematic / save_store_interop / smoke / starfield_cs / startup_flow / task_pool_interop / tutorial / user_db_interop / user_db / user_session / view_zoom / virtual_controls / wave_pacing / welcome_flow / window_size

最终门禁汇总：

| 门禁 | 结果 |
|---|---|
| `dotnet build`（warnings-as-errors） | ✅ 0 warning / 0 error |
| `dotnet test tests-csharp/` | ✅ 89/89 |
| `dotnet format` 三工程 `--verify-no-changes` | ✅ 零 diff |
| 受影响断言场景（Y1-Y5 分阶段 34 个） | ✅ 全 PASS |
| **全量断言场景（Y6）** | ✅ **55/55，0 失败** |
| `gen_balance_map.py` | ✅ 468 静态调用 / 3 动态 / 1 未引用 / 0 缺失（工具前缀匹配修复后） |
