# InfiAir Roadmap

> 方向决策与已知债务的单一权威。现状快照只在方向变化时更新；变更史看 git log 与 `CHANGELOG.md`；工程约定见 `AGENTS.md`；设计数值见 `docs/DESIGN_BASELINE.md`。

## Current State (2026-09-07)

- 内容演进与局外成长全部落地：4 Boss 轮换、母舰火力平台、触屏输入、科技树（TechPoints）、击杀连击 + 低血防御保底。无尽必死曲线（D1）为既定设计。
- 质量形态：xUnit 115 项纯逻辑 + 2 个断言场景（smoke/base）+ CI 单 job fast-gate（2026-08-29 大削减后的形态）。
- 2026-09-07 深度链路修复：设计基线核实无污染；修复 welcome 布局腐烂、RunTime 菜单污染、R 重开不删档、返航后 Boss 绕过入场窗口、精英事件钳制缺口（详见 CHANGELOG [3.33]）。
- 工程纪律同日裁剪：行为规格文档全部退役归档（不为行为写规格），审计留档制度废止，活文档收敛为 5 份（见 `AGENTS.md` 文档表）。

## 已知债务与开放发现

> 唯一登记处。修复后原地划掉并注日期；新发现追加在末尾。

- **[中低] 召唤蓄力/机库小窗窗口期事件可触发**：事件互斥只拦「母舰在场」，蓄力 3s + 小窗 2.6s 内事件掷签命中 → 玩家锁输入 + 999s 无敌、母舰自动火力白拿事件奖励（L13 反向漏出）。需 Main↔GameEventManager 召唤窗口互斥标志，涉及遭遇触发门控时序。
- **[中低] 基地任务绝对计数轮换即完成**：kill/boss/survive 进度为对局绝对值，刷新抽到低门槛任务下一秒瞬领 RP（刷新经济泄漏）。修复需任务实例改为「抽取时快照基线」的相对进度，涉及存档格式。
- **[低] `_wavesPaused` 单布尔双写者**（Spawner × 2 事件）：正确性依赖管理器「encounter 组单活跃」不变量，绕过管理器直启事件会互踩。生产路径不可达。
- **[低] `Main.OnPlayerDied` 不清遭遇事件**：当前依赖「死亡必终局 + 场景重建」兜底；未来加复活/同局续命玩法需补 `EndActive(GROUP_ENCOUNTER)`。
- **[低] 视觉层无自动化覆盖**：全部无头门禁不经过 GPU/shader 管线，UI 布局腐烂可潜伏一个月（2026-09-07 W1 实证）。现行纪律 = UI/视觉改动窗口化人工过目；可选改进 = welcome 页视觉捕获场景（test/ 截图工具族同款）。
- **[手工·发布前] Cinematic stage 4**：低配机复测 + 手柄/移动端手工项 + README 截图核对。
- **[手工·发布前] 真机手感验证**：15+ 分钟连续实机游玩（无尽校准与公平性机制的人工验收）。

## Direction Shift

| Dimension | Past (~3.13) | Future |
| --- | --- | --- |
| Goal | per-item parity | independent evolution; original = reference |
| Mode | solo | collaborative (repo ready) |
| Release | packaging deferred | resumed (presets + `release.sh` + scripts); CI 单 fast-gate + 手动 release |
| Content | mechanic completion | keep current; depth/new content cut 2026-07-30 — restart needs re-scoping |
| Meta | score-only in-run economy | cross-run growth: TechPoints tech tree (2026-08-09) |

## Phases

### Phase 0 — Tech-debt finish ✅ closed (2026-08-03)

Spawn path unified to pool, 4-service split, A3/A4 registry + declarative effect table, four fairness mechanics, CI/CD。test/ 门禁盲区修复（compile probe + 计数权威）。A8 PlayerVisuals 拆分为最后一条架构债。（细节见 git 历史与归档审计。）

### Phase 3 — Deferred/cut (restart needs explicit decision)

- **Local accounts**：landed（UserDB/PBKDF2、per-user 存档/设置、本地排行榜、welcome 入口、StartPanel 退役）。规格 `docs/archive/2026-08-04-local-accounts-plan.md`。
- **Mothership expansion**：landed（里程碑门控的加特林/导弹升级）。
- **Content evolution**：landed（3 buffs、分裂者、重型炮塔、第 4 Boss「月蚀」）+ mobile touch landed（2026-08-07）。剩余未做：独立排行榜页（已并入本地账户）。
- **Endless k-value calibration**：landed（`progression.*` + ramp 因子；3 × 900s 探针零异常）。
- **Online leaderboard**：decided NO（2026-07-20）；反转需显式推翻。

## Decisions

> 摘要登记（为什么）；全文与执行记录在 `docs/archive/` 对应计划文档与 git 历史。

- **2026-08-05 统一实体/事件管理器**：`EntityManager` 注册样板收敛 + 生命周期信号 + 批量 API；`GameEventManager` 收敛全部随机事件（fog‖encounter 分组并发、遭遇触发移出 spawner）。
- **2026-08-08 全量迁移 C#**（反转 2026-08-07 渐进混编）：存量 GDScript 全量迁移，终态零 GDScript、单一语言维护；`csharp/core` 纯逻辑 + `csharp/godot` 绑定层分层由此确立。
- **2026-08-09 局外成长（科技树）**：死亡结算唯一入口防刷点；效果 = 开局预置 buff 层数（复用 buff 计算链，零新属性管道）；每项限级 + 总消费上限，不破坏 D1 必死曲线。
- **2026-08-11 得分/奖励审核**：击杀连击计分（温和版链式得分）+ 低血防御保底；明确不做炸弹资源/掉落物/技能树重构（复杂度预算外）。
- **2026-08-29 测试与 CI 大幅削减**（用户指令）：51 断言场景 + autoplay 探针退役，仅存 smoke/base + perf_bench + 截图工具；CI 收敛单 fast-gate（format/零 GDScript/BALANCE_MAP 零 diff/编译探针闸一并退役）。行为回归 = xUnit + 两场景 + 人工窗口化验证。
- **2026-09-07 工程纪律裁剪**（用户指令「惯例恶心且冗杂」）：13 份行为规格文档退役归档、审计档案/执行留档/SOP 制度废止、活文档收敛 5 份、BALANCE_MAP 去行号（重构不再触发同步）、代码注释禁审计轮次编号。依据：行为规格与代码双源漂移曾消耗专门纠偏轮次（一次 19 文档 40+ 失实）；append-only 审计档案 1900+ 行且持续膨胀。

## Maintenance

- 方向/阶段变化 → 更新本文件对应小节；不在此复述变更细节（CHANGELOG 的职责）。
- 新债务/新发现 → 「已知债务与开放发现」末尾追加；修复后划掉注日期。
