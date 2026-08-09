# 2026-08-09 W 系列审计报告（第三轮：V 系列后增量 + 登记项复查 + CI 盲区）

> 依据用户指示「最新的几份审计报告已经审核出实际问题，你来负责进一步优化审计」执行（goal 模式）。分支：`feature/csharp-full-migration`（HEAD 52dcf67）。审计登记：`docs/AUDIT_VAULT.md` W 系列。方法遵循 `docs/AUDIT_REVIEW_SOP.md`（并行审计 → 分类 → 批处理 → 迭代修复+即时验证 → 归档回填）。

## 1. 背景与目标

U 系列（迁移保真审计，B1–B5 全落地）与 V 系列（多角度审查，P1×2/P2×14 修复）已于 2026-08-09 收官。V 系列收官后又产生 6 个提交，其中 **52dcf67「启动器探测链 .NET 优先对齐 + M7 残留兼容桥全量清理」（61 文件 +193/−1500）** 与 **fe1a186「遭遇配置缓存修复」** 为实质性代码变更，未经独立审计。本轮目标：

1. **增量审计**：52dcf67 桥清理是否正确（误删/残留/语义漂移）、fe1a186 缓存修复是否完整、dbaf64c CI 引擎错误扫描是否有盲区
2. **登记项复查**：全部「登记不修/待办/观察」条目读码验证论证是否仍成立、是否有低成本可修复项
3. **月蚀系统深审**：月蚀（月食之轮）对照 `docs/BOSS_REDESIGN.md` §5.6 设计与实现一致性

## 2. 基线（审计开始前实测）

| 项 | 结果 |
| --- | --- |
| `dotnet build` | 0 警告 0 错误 |
| `dotnet test tests-csharp/` | 75/75 全绿 |
| 工作区 | git 干净 |

## 3. 方法与范围

4 路并行只读审查（explore 子代理）：

| 路 | 范围 | 主查 |
|---|---|---|
| A | 52dcf67 全 diff（61 文件） | 349 被删成员 × 全仓库四通道 grep、动态派发面程序化双向校验、216 保留桥逐一验证调用方、启动器探测链 vs AGENTS.md |
| B | fe1a186 + dbaf64c | 遭遇配置缓存别名语义（测试直写可见性）、CI 引擎错误扫描逻辑与盲区 |
| C | 全部登记不修条目（V 13 + U 5 + 早轮次） | 论证是否仍成立、是否过时、是否有低成本可修复项 |
| D | 月蚀系统（Boss.cs/BossAttacks/BossFire/BossMovement/EnrageSequence） | 对照 BOSS_REDESIGN §5.6：设计匹配、热路径、生命周期、数值健壮性、贴图接线 |

## 4. 审计结论

### 4.1 增量审计（路 A/B）

- **52dcf67 桥清理可信**：删除面全部为纯转发/别名，无行为语义漂移；`dotnet build` 零警告排除 typed 悬空引用；Boss 链动态读取目标全部存活；tscn 无方法连接；零 `.gd`。**声明口径 1 处失实**：声明「保留约 200 个实证在用桥、逐名核实」，实际 216 个保留桥中约 20 个零消费方（误删风险为零、误留无害，登记下轮清理）。
- **fe1a186 修复有效且完整**：根因分析正确（值缓存固化 chance → 测试直写 1.0 不可见 → 70% flaky）；引用缓存（AsGodotDictionary 与 Variant 共享底层字典）使直写可见；全仓仅 EventManagerTest 两处直写且为字段级修改，还原路径兼容；fog 组配置为属性直读无同类问题。
- **dbaf64c CI 扫描有 4 处盲区**（W11–W14 修复）：① 扫描模式全为 GDScript 格式，**C# 未处理异常（`Unhandled exception`）漏网**——Godot 4 中 C# 异常默认不终止应用（godotengine/godot#73515），退出码 0 静默通过，是零 GDScript 项目的主盲区；② main smoke 步骤无扫描；③ compile probe 模式与断言场景不一致（缺 `Nonexistent function`）；④ 上传日志 glob 不覆盖 retry/probe 日志。另场景数硬校验注释声称「防改名/新增掉出 CI」不成立（total 与循环同 glob 同源），实际防御循环内跳过与 glob 空匹配（W15 口径修正）。

### 4.2 登记项复查（路 C）

**3 项论证过时或可低成本修复（已落地）**：

| 登记项 | 原论证 | 复查结论 |
|---|---|---|
| V-12 OrbitalStrike IMPACT_AT 无下限 | H15 只封了另一侧 | `impact_at≤0` 首帧即 struck+清场；改 `Mathf.Clamp(…, 0.05f, 0.95f)` 一行，行为零变化（W1） |
| U-2 MoveType4 bob_period 无钳制 | 同族不一致登记观察 | `≤0` → 周期除零 → 相位 NaN → SinFast 查表越界崩溃；改 `Mathf.Max(…, 0.05f)` 一行（W2，路 D 独立命中同点） |
| V-8 difficulty_scaling 判型缺口 | 当前数据无恙防呆项 | 损坏类型 → AsGodotArray 得空表 → `Clamp(tier,0,Count-1)` 返回 -1 → **越界 SCRIPT ERROR（Boss setup 崩溃）**；改判型+空表守卫 ~10 行（W3） |

**5 项过时（前提已消解，VAULT 补注）**：L-2（Enemy 空间查询已改信号驱动）、P2-2（`_move_ctx` 字典已改 typed 字段）、G019（movement_locked 死字段已删）、E14（beam_pts 已随重写消失）、U 收官「% 格式化 5 份重复」（已收敛单一实现）。

**其余全部保持登记**：Explosion 静态池（架构级）、~101 Set* 测试端口（认可模式）、autoplay 不进 CI、池 Contains O(n)、GetFightPhaseTransition 名实不符、Meth* 拼写、GameState float 出口截断、PlayerVisuals 弹反数组、Enemy 体碰动态派发、Welcome rune 口径、sweep 残点注释、Boss GD.Print 逃跑到点日志、CinematicFx SoftTexture、自实例信号无配对、CachedPlayer 两次调用等。

### 4.3 月蚀系统深审（路 D）

**设计匹配总体合格**：P1/P2/TRANSITION/ACTIVE 双环反向进动/RELEASE 20 发/轮换/hp_mults/tell/贴图接线全部与 §5.6 一致，无应急补丁痕迹；生命周期无自持节点、Abort/Die 链完整；FireRing 弹数 `Max(2,…)`、E4RingInterval 0.05 下限在位；贴图路径拼写正确。测试覆盖充分（Enrage 场景 5 / Pattern 场景 7 / Phase 场景 5-6 / Registry 交叉校验）。

发现（均已处置）：

| # | 位置 | 问题 | 处置 |
|---|---|---|---|
| D1 | BOSS_REDESIGN.md:106 | 「charged ring volley」无蓄力实现（1 型有 E1SalvoCharge+ChargeGlow） | W10 文档口径修正（如需蓄力表现另立设计任务） |
| D2 | BossMovement.cs:183 | MoveType4 周期除零无保护（MoveBob 同文件有 0.01 保护） | W2 修（Boss.cs 读值处钳 0.05） |
| D3 | Boss.cs:410/414、EnrageSequence.cs:266/451 | 狂暴 duration 类键无下限钳制（配置损坏才可达） | 登记不修 |
| D4 | BossFire.cs:133/155/175 | SetMeta 每发弹 StringName+装箱（发射路径非每帧红线） | 登记（随热路径批次） |
| D5 | Boss.cs:54 | StalkerPointAnglesDeg 死字段（活跃副本在 EnrageSequence） | W4 删 |
| D6 | BossMovement.cs:10-11 | 头注释「1 次/物理帧」失实（实际 7-11 次） | W7 修注释 |
| D7 | EnrageSequence.cs:421 | 注释字段名 `_ring_angle`（1 型字段）误导 | W8 修注释 |
| D8 | Boss.cs:141 + balance.json | StrafeSpeeds[3]=40 无消费方（Q27 后 4 型不 strafe） | 登记（删需同步 json+BALANCE_MAP） |

### 4.4 测试注释清理（路 A 交叉发现）

10 处「M3d：…无 Boss.cs 转发器——断言移除，**待补转发器后恢复**」注释（BossPatternTest ×5 / BossPhaseTest ×2 / BossEnrageTest ×3）——52dcf67 已正式删除这些转发器，「待补」承诺与删除方向矛盾且删除前注释同样失实。全部改为「转发器已随 52dcf67 删除，断言永久移除」，并标注保留断言覆盖点（W5，纯注释无行为影响）。

## 5. 修复清单（W1 单批，2026-08-09）

| # | 文件 | 修改 |
|---|---|---|
| W1 | csharp/godot/OrbitalStrike.cs | IMPACT_AT 补下限钳制 0.05–0.95 |
| W2 | csharp/godot/Boss.cs | Move4BobPeriod 读值补 ≥0.05 钳制 |
| W3 | csharp/godot/Boss.cs | difficulty_scaling 三读判型 + 空表守卫 |
| W4 | csharp/godot/Boss.cs | 删 StalkerPointAnglesDeg 死字段 |
| W5 | csharp/godot/tests/Boss{Patern,Phase,Enrage}Test.cs ×10 | 「待补转发器」注释 → 「永久移除」 |
| W6 | csharp/godot/Mothership.cs | duck-typing 注释 → typed 现实 |
| W7 | csharp/godot/BossMovement.cs | 头注释动态派发频率修正 |
| W8 | csharp/godot/EnrageSequence.cs | 注释字段名修正 |
| W9 | csharp/godot/GameEventManager.cs | ReloadConfig 遭遇组口径澄清 |
| W10 | docs/BOSS_REDESIGN.md | §5.6 charged → direct fire 口径 |
| W11 | .github/workflows/ci.yml | 三处扫描补 `Unhandled exception` |
| W12 | .github/workflows/ci.yml | main smoke 补引擎错误扫描 |
| W13 | .github/workflows/ci.yml | compile probe 模式统一 |
| W14 | .github/workflows/ci.yml | 日志上传 glob 补 retry/probe |
| W15 | .github/workflows/ci.yml | 场景数校验注释口径修正 |

## 6. 验证（全部真实执行）

- `dotnet build` 0 警告 0 错误；`dotnet test tests-csharp/` 75/75；`dotnet format` 三工程零 diff
- main smoke 300 帧：exit 0、0 引擎错误
- 定向断言场景 7 个（boss_enrage / boss_pattern / boss_phase / boss_phase_transition / boss_registry / orbital_strike / smoke）：0 FAIL、0 引擎错误（新扫描模式实测）
- 工作区 git 干净，无生成物污染

## 7. 结论

- **52dcf67/fe1a186/dbaf64c 三提交主体可信**，方向安全；声明口径 2 处失实（保留桥逐名核实、场景数校验能力）已修正。
- **登记项复查产出 3 项真实防御缺口修复**（其中 difficulty_scaling 为崩溃级），论证后 12 项保持登记。
- **CI 盲区收口**：C# 异常静默通过是零 GDScript 时代最大漏报面，三处扫描统一后与引擎错误零容忍门禁闭环。
- 无 P0/P1/P2；全部修复为防御缺口、注释口径与 CI 增强，正常数据路径行为零变化。
- 报告归档，W 系列权威登记在 `docs/AUDIT_VAULT.md`。
