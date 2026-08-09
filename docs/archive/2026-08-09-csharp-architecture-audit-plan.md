# 2026-08-09 C# 逻辑架构审计报告与优化计划

> 依据 C# 游戏逻辑架构专家角色流程执行（Phase 0 环境侦察 → Phase 1 逻辑梳理 → Phase 2 优化计划 → Phase 3 实施 → Phase 4 验证）。
> 本轮为独立架构审计（区别于 U 系列迁移保真审计 / W 系列引擎日志审计），聚焦逻辑架构、热路径与债务，并落地一批低风险修复。
> 方法：5 路并行只读子代理（核心循环/战斗实体/Boss+事件/数据持久化/UI 辅助），三角验证（代码 × 文档 × 调用方 grep）。

## Phase 0 — 环境侦察

| 项 | 结果 |
|---|---|
| 工程 | `InfiAir.csproj`（Godot.NET.Sdk/4.6.2, net8.0, `TreatWarningsAsErrors`）+ `csharp/core/InfiAir.Core.csproj`（纯 .NET 零 Godot 依赖）+ `tests-csharp/InfiAir.Core.Tests.csproj`（xUnit 17.11.1/2.9.2） |
| 分层 | `csharp/core/` 纯逻辑 6 文件；`csharp/godot/` 绑定层 ~110 文件；`tests-csharp/` 7 测试文件 |
| 入口 | `scenes/welcome.tscn`；唯一 autoload `GameState`（2840 行 facade，转发 8 服务：BalanceService/SaveManager/SfxPlayer/EntityManager/FogEventManager/GameEventManager/UserDB/ProgressionInterop） |
| `dotnet build` | ✅ 0 warning / 0 error |
| `dotnet test tests-csharp/` | ✅ 75/75 通过（183ms） |
| CI 门禁 | 零 GDScript / dotnet format 三工程零 diff / 引擎警告 / smoke / 64 场景编译探针 / 55 断言场景 / BALANCE_MAP 生成器零 diff |

## Phase 1 — 逻辑梳理（5 域摘要）

### 1. 核心循环与状态流转
阶段状态机**无单一状态源**：分散在 Main/Spawner/Boss/各 UI 的布尔标志 + 树暂停（Paused）组合；Main 的 `_gameOver/_homecoming/_charging` 与 Spawner 的 `_bossActive/_bossPending/_wavesPaused` 互不隶属，靠入口守卫（如 `canCharge` 7 条件连乘 Main.cs:377-383）维持不变量。协程 = 纯 C# async/await + SceneTreeTimer（生产仅 Main 2 处 await，均带判活守卫；Coroutine.cs 仅测试域使用）。

### 2. 战斗实体层
伤害管线两主链（玩家弹→敌 / 敌弹→玩家）守卫完备：`Hp<=0` 早退、A16 单帧受击守卫、已回收弹 `_active` 守卫、注册表倒序遍历防突变、deferred reparent + `IsActive()` 复查防同帧复用。池化（BulletPool/EnemyPool/Explosion）热路径零托管分配。

### 3. Boss 体系与随机事件
Boss 全链 ~72 状态字段（本体 21 + 组件 51），P1→P2→ENRAGE 阈值 else-if 链；5 子状态狂暴序列 + 4 型注册表分发。事件系统无每帧掷签：fog 3s 间隔掷签、encounter 每帧 O(2) 轻量计时；fog 按次实例化、遭遇单例挂 Main 随场景清理，无确认泄漏。

### 4. 数据/持久化/互操作
core 层优秀（纯函数/注入路径种子，xUnit 直测）；互操作壳薄转发。存档原子写（tmp+rename+损坏隔离）；用户库 PBKDF2 50k 迭代 ~165ms 阻塞主线程（已文档化降档决策）。热路径（追踪弹 `HasEnemy` O(1)、ramp 因子 Load 缓存）设计正确。

### 5. UI 与场景辅助层
工程质量高：文本全信号驱动（无每帧 set_text）、节点 `_Ready` 缓存、tween 互斥清理（kill 再建 + meta 缓存）、C22/U07 断开惯例成体系。UI 编排逻辑（结算链/存档）在 UI 层为已知债务。

### 关键问题清单（P0–P3，本轮处置标注）

| 级别 | 问题 | 位置 | 处置 |
|---|---|---|---|
| P1 | 母舰减速带对 Boss 失效（typed 化回归，原 duck-typing 含 Boss；Boss.ApplySlow 成死代码） | Mothership.cs:734 ↔ Boss.cs:832 | **本轮修复** |
| P1 | Welcome.ShowMsg 树级 Timer 跨场景回调触碰已释放 `_msgLabel` | Welcome.cs:279-287 | **本轮修复** |
| P1 | `UnregisterEnemyBullet` O(n) 线性移除（敌弹消亡高频） | EntityManager.cs:85-91 | **本轮修复** |
| P2 | `Coroutine.WaitSignal` 无超时永久挂起 / `WaitSeconds` 离树 NRE | Coroutine.cs:47-82/:24 | **本轮修复** |
| P2 | Boss 狂暴三时序键无下限钳制（0 值除零 inf） | Boss.cs:407-413 + EnrageSequence.cs:266/319 | **本轮修复** |
| P2 | `EventFor` fallback 返回已释放实例，调用侧无判活 | GameEventManager.cs:762-778/:526/:621 | **本轮修复** |
| P2 | 敌机生成绕缓存直查 Cfg ×3（hp_ramp 有缓存 API 未用） | Enemy.cs:226 | **本轮修复** |
| P2 | Welcome 模态/按钮引用存 Variant Dictionary（15 处强转） | Welcome.cs:46/55-57 | **本轮修复** |
| P2 | 双轨 API 残留：GameState 21 静态 GetXxx + Main 15 snake 桥（零调用方，每次重建集合） | GameState.cs:2703-2835 / Main.cs:1084-1131 | **本轮删除** |
| P3 | `PlayerVisuals.UpdateParryVisuals` 每物理帧 `new Vector2[6]` | PlayerVisuals.cs:178 | **本轮修复** |
| P2 | 伤害分派 switch ×3（Bullet/LaserWeapon） | Bullet.cs:433/378、LaserWeapon.cs:258 | deferred（战斗核心，需更大断言面） |
| P2 | `%` 格式化器重复 4+ 份 | Hud/BuffSelect/SettingsUi/Tutorial | deferred（需逐份语义比对） |
| P2 | UI 编排逻辑在 UI 层（结算链/存档） | GameOverUi.cs:113 / PauseUi.cs:160 | deferred（行为风险） |
| P2 | GameState 上帝类（~250 public 成员） | GameState.cs | deferred（唯一 autoload 有意设计） |
| P2 | 两池 `_free.Contains` O(n) | BulletPool.cs:94 / EnemyPool.cs:84 | deferred（上限 500，实际影响小） |
| P2 | GdFormat 三份重复 / 平衡树双存储 / 静态帧缓存同构 ×3 / Boss 链 ~110 双命名桥（已登记） | 多处 | deferred |
| P3 | 体碰/召唤/弹窗字符串 Call 残留；Hud FillColor 无守卫；MouseTrap 每帧 GetWindow | Enemy.cs:593 / Boss.cs:1247 / Mothership.cs:598 / Hud.cs:569 / MouseTrap.cs:62 | deferred |
| 测试缺口 | VariantBridge 无 xUnit 单测（互操作咽喉） | csharp/godot/VariantBridge.cs | **本轮补齐** |
| 测试缺口 | UserDb iterations 钳制/非法 hex 盐；TaskPool 重复 id；ProgressionCurves 边界 | tests-csharp/ | **本轮补齐** |

### 已排除（各域核实）
里程碑曲线单调性（cycle_mult 下限 0.01 下逐档非负）；PlayerDied/Died 信号无重入（监听方均纯状态更新）；EnemyBullets 注册表固定实例无包装失效；事件系统无泄漏；生产协程无跨场景竞态；无每帧 GC 分配源。

## Phase 2 — 优化计划（本轮执行范围）

### A 组 — P1 修复
1. 母舰减速带恢复 Boss 生效：`DeploySlowField` 分派 `is Enemy || is Boss`，接通 `Boss.ApplySlow`。
2. ShowMsg 回调加 `IsInstanceValid(_msgLabel)` 判活。
3. `UnregisterEnemyBullet` 改 swap-remove + 索引表（`Dictionary<GodotObject,int>`），先核实消费方无顺序依赖（已核实：生产仅倒序/只读）。

### B 组 — P2 安全项
4. Coroutine 双修复（无超时兜底 tcs 完成 + `IsInsideTree` 判活）。
5. Boss enrage 三时序键下限钳制 ≥0.05。
6. GameEventManager `EventFor` 调用侧 `IsInstanceValid` 校验。
7. Enemy.cs:226 改走 `GameState.Instance.EnemyHpRamp()`（已核实语义等价：`DifficultyMultiplier` 对局内恒定；缺键默认一致）；**改后重跑 `gen_balance_map.py`**。
8. Welcome 模态/按钮 Dictionary → typed 私有类 + `List<Button>`。
9. 删除 GameState 21 静态 `GetXxx` + Main 15 snake 桥（已 grep 零调用方）。
10. PlayerVisuals `Vector2[6]` 预分配。

### C 组 — 测试补齐
11. VariantBridge xUnit（`InternalsVisibleTo`）。
12. UserDb 钳制/非法盐用例。
13. TaskPool 重复 id / ProgressionCurves 边界用例。

### D 组 — Phase 4 验证
`dotnet build` + `dotnet test` + `dotnet format` 三工程 verify + Godot headless import/smoke + 受影响断言场景 + `gen_balance_map.py` 零 diff。

### Deferred（附理由）
见上表 deferred 行；Boss 链动态派发与双命名桥已登记（收益低不修）；伤害分派统一与格式化器收敛需更大语义核对面；UI 编排下沉涉及行为风险。

## 合规
不引入第三方依赖；删除项均先 grep 确认零调用方；Cfg 改动重跑 BALANCE_MAP 生成器；改动后同步本报告（实施日志/验证清单追加于文末）。


---

# Phase 3 — 实施日志（2026-08-09）

按 A→B→C 顺序实施，每项改动摘要：

| 项 | 改动 | 文件 | 说明 |
|---|---|---|---|
| A1 | 母舰减速带恢复 Boss | `Mothership.cs` | `is Enemy` 分派扩展 `\|\| is Boss`，接通 `Boss.ApplySlow`/`_summonSlowTimer` 死代码（W6 只更正注释未补行为） |
| A2 | ShowMsg 跨场景判活 | `Welcome.cs` | 回调补 `IsInstanceValid(_msgLabel)`（SceneTreeTimer 不随场景释放） |
| A3 | 敌弹注销 O(1) | `EntityManager.cs` | swap-remove + `_enemyBulletIndex` 索引表双维护（消费方倒序/只读已核实） |
| X4 | TaskPool 重复 id 挂死（新 P0） | `TaskPool.cs` | usable 改 id 级去重（原实现重复 id 定义下无限 Refill） |
| B4 | Coroutine 判活 | `Coroutine.cs` | WaitSeconds/WaitPhysicsFrames 补 `IsInsideTree`；WaitSignal 源/节点失效提前返回 false |
| B5 | 狂暴时序键钳制 | `Boss.cs` | enrage duration/transition_duration/return_duration ≥0.05（0 值除零 inf） |
| B6 | EventFor 死引用防御 | `GameEventManager.cs` | Tick 跳过死引用；Poll 按「不活跃」自愈复位 |
| B7 | hp_ramp 缓存化 | `Enemy.cs` + `GameState.cs` | 首版替换为无参 `EnemyHpRamp()` 被 enemy_combat_test H2 捕获（pDifficulty 显式参数 ≠ 全局 DifficultyMultiplier）→ 加显式重载 `EnemyHpRamp(pDifficulty)` 后全绿 |
| B8 | Welcome typed 化 | `Welcome.cs` | `ModalParts`（Layer/Ok/Cancel）+ `Dictionary<StringName, Button>`，删 15 处强转 |
| B9 | 双轨 API 删除 | `GameState.cs` / `Main.cs` | 21 静态 `GetXxx()` + 15 snake 桥（四通道 grep 零调用方） |
| B10 | PlayerVisuals 预分配 | `PlayerVisuals.cs` | `Vector2[6]` 预分配字段复用 |
| C | 测试补齐 | `tests-csharp/` ×3 | +5 用例（TaskPool×2 / Progression×2 / UserDb×1，75→80） |
| 文档 | 服务数漂移修正 | `AGENTS.md` / `CLAUDE.md` | 7→8 服务（补 ProgressionInterop） |

# Phase 4 — 验证清单（实测）

| 门禁 | 结果 |
|---|---|
| `dotnet build`（warnings-as-errors） | ✅ 0 warning / 0 error |
| `dotnet test tests-csharp/` | ✅ 80/80（+5 新用例，2s） |
| `dotnet format` 三工程 `--verify-no-changes` | ✅ 零 diff |
| `godot --headless --import --path .` | ✅ exit 0，0 引擎错误 |
| 受影响断言场景 | ✅ 30/30（首批 26：25 过 + enemy_combat 修复后过；复测 4：wave_pacing / balance / csharp_interop / pool_reuse） |
| `gen_balance_map.py` 重跑 | ✅ 468 静态调用 / 0 缺失键 / 生成物已更新随改动提交 |
| 零 GDScript | ✅ 无新增 .gd |

**验证过程记录**：首批批量 26 场景捕获 `enemy_combat_test` 失败（H2 子机继承母体难度，2.0 档 HP 实测 44）——B7 首版（无参 `EnemyHpRamp()`）把显式 `pDifficulty` 换成全局 `DifficultyMultiplier` 导致语义漂移（子代理「语义等价」结论仅对 Spawner 生产路径成立，未覆盖测试/分裂子机的显式传参路径）；修复（显式难度重载）+ 复测全绿。本报告 Phase 2 表格据此保留 B7 但注明参数语义约束。
