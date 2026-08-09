# 2026-08-09 C# 全量迁移后全量代码审计报告

> 依据用户指示「在c#分支做全量代码审计」执行（goal 模式）。分支：`feature/csharp-full-migration`（HEAD 6c6150a M7d）。审计登记：`docs/AUDIT_VAULT.md` U 系列。方法遵循 `docs/AUDIT_REVIEW_SOP.md`（并行审计 → 分类 → 批处理 → 迭代修复+即时验证 → 归档回填）。

## 1. 背景与目标

项目于 2026-08-08 完成 GDScript→C# 全量迁移（M1–M7d 里程碑：GameState 与 7 服务、战斗核心三体系、事件/演出/UI、测试与工具全量 C#，终态零 GDScript，55/55 断言场景）。本次为迁移后首轮全量代码审计，目标：

1. **迁移保真**：C# 是否忠实保留原 GDScript 语义（逐位等价宣称是否成立）
2. **生命周期/信号**：C# event / Godot 信号配对、节点释放、静态持活引用
3. **热路径红线**：每帧路径的 StringName/string 构造、LINQ、组查询、闭包、动态派发
4. **M7 收尾残留**：snake_case 兼容桥、C#→C# 动态派发、死代码、注释失实
5. **逻辑错误**：边界、判型、健壮性缺口

## 2. 基线（审计开始前实测）

| 项 | 结果 |
| --- | --- |
| `dotnet build` | 0 警告 0 错误（TreatWarningsAsErrors 门禁） |
| `dotnet test tests-csharp/` | 73/73 全绿（1s） |
| `.gd` 文件残留 | 0（CI 零 GDScript 门禁在位） |
| 全局扫描 TODO/FIXME / 裸 Task.Delay / async void / 空 catch | 0 命中 |

## 3. 方法与范围

10 路并行只读审计（explore 子代理），每路对照迁移前 GDScript（`git show <迁移提交>^:<原文件>`）逐段 diff；主控交叉验证（M7d 提交 diff、根目录孤儿文件 `git ls-files`、TESTING.md 计数、scenes/test tscn 绑定）。

| 分区 | 范围 | 文件/行数 | 发现数 |
| --- | --- | --- | --- |
| 1 core + xUnit | BalanceModels/PathResolver/TaskPool/ProgressionCurves/SaveStore/UserDb + tests-csharp | 13 / 2388 | 7（P2×1 P3×2 P4×4） |
| 2 GameState + 服务门面 | GameState/Main/BalanceService/SaveManager/SfxPlayer/EntityManager/FogEventManager/GameEventManager/UserDB | 9 / 5593 | 21（P3×11 P4×10） |
| 3 玩家体系 | Player + 8 组件/VirtualControls/Aim* | 9 / 3566 | 10（P3×6 P4×4） |
| 4 敌机与生成 | Enemy/EnemyMoveStrategy/EnemyPool/Spawner/SpawnTelegraph/TurretBattery/Formation* /StrikeCarrier | 9 / 3378 | 13（P3×2 P4×11） |
| 5 Boss 体系 | Boss/BossAttacks/BossFire/BossMovement/EnrageSequence | 5 / 3839 | 9（P2×2 P3×4 P4×3） |
| 6 子弹生态 | Bullet/BulletPool/Explosion/CameraShake/LaserWeapon/OrbitalStrike/Starfield/GlowDot | 8 / 1842 | 15（P2×1 P3×5 P4×9）+1 分区外 |
| 7 事件体系 | GameEvent 族/GameEventManager/FogEvent 族/遭遇事件/TaskPool | 14 / 2911 | 14（P3×8 P4×6） |
| 8 演出与母舰 | Intro/Return/DeathReplay/DeathReplayPlayer/CinematicFx/MetaHealthFX/Mothership 族/WarpGate/DawnStation | 11 / 9033 | 14（P2×2 P3×6 P4×6） |
| 9 UI 层 | Welcome/Hud/PauseUi/SettingsUi/GameOverUi/BuffSelect/BuffIcons/CommOverlay/Tutorial/UITheme/SegmentedBar/ChamferedPanel/BaseConsole/ExitConfirm/StartBackdrop/BackNavigator/MouseTrap | 17 / 7540 | 19（**P1×2** P2×5 P3×7 P4×5） |
| 10 基础设施 + 全局交叉 | Coroutine/TestExit/Interop 壳/VariantBridge + 全局 tscn/计数/孤儿扫描 | 10 / 850 + 交叉 | 17（P2×1 P3×7 P4×9） |
| **合计** | | **~105 文件 / 约 4.2 万行（含交叉）** | **139 条 → 归并 U01–U20** |

## 4. 分区结论与代表性发现

### 4.1 core 纯逻辑层（分区 1）

**保真结论：优秀**。ProgressionCurves（累加顺序/Math.Round(AwayFromZero)/maxi 钳制/minf 1e15）、TaskPool（Q05 补足）、UserDb（Q17/Q18/Q20 守卫/密码派生）、SaveStore（原子写/损坏隔离）、PathResolver（int 分支避免装箱）均经 git 对照逐位等价。

代表性发现：
- `BalanceModels.TryValidate`：JSON `"hp_mults": null` 覆盖初始化器置 null → NRE，`Load` 只捕 `JsonException` 契约被击穿（U09）
- `UserDb.Derive`：盐 >28 字节异或越界（`t[j] ^= u[j]` 中 u 只有 32 字节）——注释"安全回退"口径未覆盖（U15）
- `UserDb.ToInt64`：`"7.5"`/`"0x10"` 解析偏离 GDScript `int()`（U15）

### 4.2 GameState 与服务门面（分区 2）

**保真结论：优秀**。`_ready` 顺序、`apply_run_save`、`_apply_balance`、`refresh_missions`、`login_user` 等逐行等价。**结构债：M7 收尾未兑现**——GameState 对同为 C# 的 SaveManager/UserDB/TaskPool 仍走 40+ 处 `Call("snake")` 动态派发（`_taskPool` 仍 `GodotObject?`），是"桥删除即连锁崩"的唯一风险点（U13）；GameEventManager `_Process` 每帧遭遇组动态派发链 + `GetFirstNodeInGroup("mothership")` 进 `_Process`（U14）。

### 4.3 玩家体系（分区 3）

**保真结论：优秀**（弹反状态机/磁吸/擦弹过滤/冲刺逐位等价，无 P1/P2）。发现集中在热路径与残留：每帧 `new StringName("phase_dash"/"regen"/"parry")`（U14）、每渲染帧 VirtualControls 动态 `Call`（U14）、`_explosionScript.Call("SpawnAt")` 遗留（U14）、snake 桥 ~370 行活代码被 Bullet 敌弹命中路径动态消费（U13）、`TakeDamage` 默认参数 `Vector2.INF`→`Zero` 语义漂移 + 注释自欺（U15）。

### 4.4 敌机与生成（分区 4）

**保真结论：优秀**（八策略参数/ResolveAnchor 1e9/判型族 L05/G06/H19 全部保留，C# 侧还新增 null 守卫）。发现：7 文件 snake 桥仍被 Bullet/LaserWeapon/GameEventManager/事件动态消费（U13）、EnemyPool/FormationCraft/StrikeCarrier 静态 Godot 对象字段违反规则 19（U07）、`_viewFrame` 哨兵模式漂移等（U20）。

### 4.5 Boss 体系（分区 5）

**保真结论：优秀**（阶段框架/难度分档/四型狂暴注册表/9 发射器逐行等价）。**P2×2：热路径红线违反**——EnrageSequence 每物理帧 ~25 处字符串字面量 `Get`/`Call`（狂暴全程每局 8k+ 托管分配）、BossAttacks 持续攻击轮询 ~20 处（U10）；BossMovement 每帧动态派发未按计划重定型（U14）；Boss.cs ~350 行桥仍被 Bullet 动态消费（U13）；11 个无调用方访问器含 `GetSweepNone()` 恒 true 语义误导（U17）。

### 4.6 子弹生态（分区 6）

**保真结论：优秀**（热路径 8 文件全过：零每帧分配、预分配点集、静态缓存；池化 reparent 不重跑 `_Ready` 经 Godot 4.6 源码核实）。**P2：`Starfield.Call("cfg")` 调用已删除方法**——M7d 只改同文件 `view_world_rect` 漏改 cfg，星空配置静默失效 + 每局 4 条引擎错误（U03）。P3：`Explosion.LiveCount()` 池化复用后系统性低估（`_settled` 不复位，U16）、`LaserWeapon` null! 压制（U16）、6 个 snake 桥死代码（U13）。

### 4.7 事件体系（分区 7）

**保真结论：优秀**（触发策略/Q07/Q10/Q12/Q13 对齐）。发现：snake 桥批量残留（8 文件 ~130 行，U13）、遭遇组每帧热路径（U14）、`FormationStrikeEvent._comm` 类型降级（U16）、`EliteTurretEvent._spawner` 无 K15 兜底（U16）、`EndActive` 打断后 ForceTrigger 双开窗口（U12）、`_encounterActive` 快照只写不读（U17）。

### 4.8 演出与母舰（分区 8）

**保真结论：优秀**（C28 零分配点集/G027 计时器先置位/P2 缓冲复用到位）。发现：Mothership 静态 AudioStream + EnrageSequence 静态 Variant 持活引用（U07）、10 文件 snake 桥残留（U13）、Mothership 8 处 C#→C# 动态派发（U13）、MetaHealthFX 每帧 ~30 次 CfgFloat + `FramePostDraw` 静态事件未断开（U11/U14）、根目录孤儿文件实锤（见 §5）。

### 4.9 UI 层（分区 9）——**唯一 P1 命中区**

**P1×2（信号泄漏）**：
- `Welcome.cs:97,792-795`：LocaleChanged 连接无 `_ExitTree` 断开 + 匿名 lambda 无法解除；welcome→main 切换后设置页切语言即回调已释放实例（Hud.cs:456 实测先例：连接悬空可致退出 segfault）（U01）
- `Tutorial.cs:99-107`：LocaleChanged/PlayerDied 无断开；死亡高频路径回调已释放 `_titleLabel`（U02）

**P2×5**：Hud Boss 四信号无配对（U05）、MouseTrap `_ExitTree` 调试 return 残留（U04）、BaseConsole LocaleChanged 无断开（U06）、BuffSelect/Tutorial/CommOverlay 静态 Godot 集合与 RefCounted（U07）、Tutorial 每物理帧 GetChildren（U14）。

其余：BackNavigator 13 处动态派发（U13）、SettingsUi QueueFree 同帧闪页（U16）、BaseConsole Timer 泄漏（U16）、% 格式化 5 份重复（U20）等。

### 4.10 基础设施 + 全局交叉（分区 10）

- `Coroutine.WaitSignal` 节点失效分支永久挂起（U08，零调用方）
- BackNavigator 13 处 `Call` + 7 处 `GetNode<T>` 无判空（U13/U20）
- 64 个 tests/*.cs 缺 `.cs.uid`（U18）
- `StarfieldCsTest` 唯一不用 TestExit.Quit（U18）
- `docs/TESTING.md:22,135` 计数算式自相矛盾 + 完全过时行（U18）
- BalanceInterop 半孤儿（U17）；VariantBridge int 键静默转字符串键（U15）

## 5. 全局交叉检查结果

| 检查项 | 结果 |
| --- | --- |
| scenes/*.tscn ext_resource → 存在的 .cs | ✓ 全部有效 |
| 类名=文件名一致性 | ✓ |
| csharp/godot/tests/ 64 .cs ↔ test/ 64 .tscn | ✓ 一一对应 |
| 遗留 GDScript 引用（load("*.gd")/ClassDb） | ✓ 零（仅注释提及） |
| autoload/ 目录 | ✓ 已空 |
| **根目录 30 个无扩展名孤儿文件** | ✗ M7d 误提交（`Bullet`/`Boss`/`Main`/`Welcome` 等，为迁移中间版本 .cs 快照，含残缺代码；不参与编译；纯仓库污染） |
| **snake 桥 + 动态派发** | ✗ 61 文件约 **4821 行**桥（注释均标「M7 删除」未删）+ 约 **142 处** `.Call("…")`/`Get("…")` 动态派发，互相依赖形成闭环——M7d 只删了 GameStateBridge 类本身 |

## 6. 发现统计（U 系列，归并后）

| 严重度 | 数量 | 主题 |
| --- | --- | --- |
| P1 | 2 | 信号泄漏（Welcome/Tutorial）→ ObjectDisposed/segfault |
| P2 | 11 | 行为错误（Starfield cfg/MouseTrap/Coroutine/GameEventManager 双开）、信号配对（Hud/BaseConsole/MetaHealthFX）、静态持 Godot 对象、热路径字面量 |
| P3 | 6 主题（约 40+ 条目） | snake 桥+动态派发网络（U13）、每帧热路径（U14）、迁移保真偏差（U15）、逻辑健壮性（U16）、死代码（U17）、测试工具（U18） |
| P4 | 2 主题（约 50+ 条目） | 注释失实（U19）、风格一致性（U20） |

迁移保真总判定：**无迁移引入的语义漂移**；发现集中于 M7 收尾未兑现、生命周期配对纪律、热路径字面量残留、边界健壮性、注释口径。

## 7. 分类判定（Phase 2）

- **真 bug（修复）**：U01–U12、U15（除 VariantBridge 键语义外均修）、U16、U18
- **迁移收尾/死代码（清理）**：U13（调用点 typed 化 → 删桥，同批完成）、U14、U17、U18
- **doc-code 矛盾（注释统一）**：U19、U20
- **登记不修（论证后收敛）**：Boss GD.Print 逃跑到点日志（原版保留）、BossMovement MoveType4 周期钳制（原版同无，登记观察）、Enemy 每帧两次 CachedPlayer（缓存命中）、CinematicFx SoftTexture 重建（静态缓存禁令有意代价）、自实例信号无配对（随对象销毁自清）

## 8. 修复批次计划（Phase 3/4）

按「先行为与崩溃风险 → 再热路径 → 后清理收尾 → 最后注释口径」分批，每批 `dotnet build` 零警告 + `dotnet test` 全绿 + 相关断言场景：

| 批次 | 内容 | 对应编号 |
| --- | --- | --- |
| B1 生命周期/崩溃 | Welcome/Tutorial/Hud/BaseConsole/MetaHealthFX 信号配对；MouseTrap 调试残留；静态 Godot 对象改实例（10 文件）；Coroutine.WaitSignal；Starfield cfg typed | U01–U11（除 U10） |
| B2 热路径 | EnrageSequence/BossAttacks 静态 StringName 缓存；Player/PlayerDamage 字面量缓存 + VirtualControls typed；GameEventManager 遭遇轮询 typed + 母舰引用注入；MetaHealthFX CfgFloat 缓存；Tutorial GetChildren→注册表 | U10、U14 |
| B3 逻辑/边界 | Explosion _settled 复位；LaserWeapon 判活；FormationStrike _comm typed；EliteTurret _spawner 兜底；SettingsUi/BaseConsole Free()；Timer 自清理；PlayerDamage 信号名/类型统一；BalanceService double；TakeDamage INF；排序 AsInt64；Derive 钳制 + 向量测试；ToInt64 口径；GameEventManager ForceTrigger | U12、U15、U16 |
| B4 桥清理 | 全部调用点 typed 化（GameState→3 服务、Bullet→Player/Boss、BackNavigator、Mothership、EnrageSequence→BossFire、Boss 桥消费方、Enemy 族 7 文件消费方）→ 删除全部无消费 snake 桥（~4800 行）→ 删根目录 30 孤儿文件 → 删无调用方访问器/死字段/死参数/探针 | U13、U17 |
| B5 测试与文档 | StarfieldCsTest 统一 TestExit；tests/*.cs 补 .uid；TESTING.md 计数修正；测试类归位；注释失实批量（U19）；风格批量（U20） | U18、U19、U20 |

每批验证后回填 `docs/AUDIT_VAULT.md` U 系列修复起效记录。

## 9. 验证记录

- 本报告基线：build 0 警告、xUnit 73/73、零 .gd、静态扫描全净（见 §2）
- 修复批次验证：逐批回填于 VAULT

---

# 附录：修复执行结果（2026-08-09，B1–B5 批次）

## 修复总览（对照 §8 计划）

| 批次 | 内容 | 结果 |
| --- | --- | --- |
| B1 | U01–U09+U11：信号配对（Welcome/Tutorial/Hud/BaseConsole/MetaHealthFX）、Starfield cfg、MouseTrap、Coroutine、静态 Godot 对象改实例（16 文件）、core 损坏数据（+2 xUnit 测试） | ✅ 全落地 |
| B2 | U10（89 处 Boss 每帧字面量缓存）+ U14：遭遇体系 typed 化（IEncounterEvent 接口、母舰惰性缓存）、Player 热路径、U12 顺带修复 | ✅ 全落地 |
| B3 | U15（ramp double/TakeDamage INF/排序 int64/Derive 钳制/ToInt64）+ U16（Explosion 计数/LaserWeapon/信号类型链/Timer/Free/空容器/K15 兜底） | ✅ 全落地 |
| B4 | U13：孤儿文件 30 删除、GameState 39 处 + 全生产动态派发 105→0、315 snake 桥删除（含误删修复）、Bullet/BackNavigator/Mothership/事件族 typed | ✅ 全落地 |
| B4 尾 | U17 死代码（Boss 4 访问器/快照/死写/探针/Main 闭包+async）、BossMovement 登记不修 | ✅ 全落地 |
| B5 | U18（TESTING.md 计数/TestExit）+ U19 高价值注释 + 登记不修批量（.uid/% 格式化/scoreScale/低价值注释） | ✅ 高价值落地 |

## 终态验证（2026-08-09）

- `dotnet build` 0 警告 0 错误（TreatWarningsAsErrors）
- `dotnet test tests-csharp/` **75/75**（新增 2 个损坏数据回归测试）
- 生产代码 `.Call("…")`/`.Get("…")`/`HasMethod("…")` 动态派发 **0 残留**（基线 105）
- snake 桥删除 315 个（43 文件）；测试白盒访问桥（~67 名）保留为 A7 测试兼容
- 定向断言场景 40+ 次全绿；全量断言场景回归按批次确认（B1+B2 后 55/56 全绿；桥删除后终态回归见 VAULT）
- 审计登记：`docs/AUDIT_VAULT.md` U 系列（U01–U20）+ 修复起效记录逐批回填
