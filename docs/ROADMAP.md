# InfiAir Roadmap

> 方向决策与已知债务的单一权威。现状快照只在方向变化时更新；变更史看 git log；设计数值见 `docs/DESIGN_BASELINE.md`；流程与门禁口径见 `AGENTS.md`。

## Current State (2026-09-10)

- 纯街机流落地（2026-09-08 用户指令）：账户/登录、排行榜、分数显示与记录、局外成长（研究所/科技点）全量移除；开机流程 = main.tscn 开机自动播开场过场 → `scenes/title.tscn` 深空机库标题屏（形态详见 DESIGN_BASELINE §1.12）→ 开局。对局内分数保留为隐藏进度引擎（敌机解锁/Boss 节奏/事件门控/里程碑→天赋点）。无尽必死曲线（D1）为既定设计。
- 本局存档回归（2026-09-10，反转 09-08「无对局存档」）：单存档位检查点模型——保存退出/回基地落盘，死亡或放弃重开删档，标题屏可读档续局。口径见 DESIGN_BASELINE §2.5。
- 内容演进：4 Boss 轮换、母舰火力平台、触屏输入、天赋缓存系统（2026-09-07，替代旧里程碑三选一与 line→双 buff 路线，低血保底随之退役）。
- 质量形态：CI 单 job fast-gate 与本地门禁一致（口径见 AGENTS.md）；回归验证 = 人工窗口化实机过目。
- 文档形态（2026-09-10 惯例整改后）：本文件（方向/债务/决策索引）+ `docs/DESIGN_BASELINE.md`（设计定稿）+ `AGENTS.md`（流程/门禁/提交纪律）三份，单源分工。

## 已知债务与开放发现

> 唯一登记处，只保留未关闭项；修复后直接删除（变更史在 git log）；新发现追加在末尾。

- **[低] 视觉层无自动化覆盖**：无头门禁不经过 GPU/shader 管线，UI 布局腐烂可潜伏。现行纪律 = UI/视觉改动窗口化人工过目；可选改进 = 视觉捕获探针场景（需要时临时重建，用后即删）。
- **[低] 专注惩罚触发面窄**：`talent.focus.threshold`(7) 按原始等级判定，现网唯 extra_life（上限 10）可达（其余节点结构上限最高 5，风险加点 +1 后 6 < 7）；若日后放宽节点等级上限需同步重校该阈值与惩罚曲线。
- **[手工·发布前] Cinematic stage 4**：低配机复测 + 手柄/移动端手工项。
- **[手工·发布前] 真机手感验证**：15+ 分钟连续实机游玩（无尽校准与公平性机制的人工验收）。
- **[低] 注释日期戳全量清理**：全库仍有注释含 `2026-0x-xx` 日期戳/审计编号前缀遗留（「去日期戳/历史叙事」纪律前的存量；注释主体语义有效的仅去前缀），渐进处理。
- **[低] GameState.RestartRun 单口缺 AB13 退出确认互斥**：暂停页 R 重开序列仍在 PauseUi 本地（含 ExitConfirm 淡出窗口期守卫——确认期重开会 ReloadCurrentScene 杀掉淡出 tween 致退出静默丢失），GameState.RestartRun 单口无该守卫且当前唯一调用方是结算页（与退出流程互斥，现不可达）；两套同构序列已实际分歧，未来任何暂停态重开入口直调单口即复活该 bug。修法：守卫并入单口，或在单口注释显式声明「不含退出确认互斥，调用方自理」。
- **[低] Explosion._ExitTree 新增 GameState.Instance 依赖**：批次二把静态爆炸池迁 GameState 实例字段后，`_ExitTree` 经 Instance getter 取池（旧静态数组任何拆树序恒安全）；autoload 先于场景节点失效的非常规拆树序（崩溃恢复/编辑器停止）下 getter 抛 InvalidOperationException。常规退出序（CurrentScene 先于 autoload 释放）已核实不触发。
- **[低] EntityManager 双表防线未覆盖第三种分歧**：Unregister 守卫拦「索引缺键」「索引越界」两种；「索引在界但元素不符」仍会 swap-remove 错删数组末元素且 Repair 不介入（正常维护纪律下不可达，防线为纯增量，覆盖面比提交描述窄）。

## Direction Shift

| Dimension | Past (~3.13) | Future |
| --- | --- | --- |
| Goal | per-item parity | independent evolution; original = reference |
| Mode | solo | collaborative (repo ready) |
| Release | packaging deferred | resumed (presets + `release.sh` + scripts); CI 单 fast-gate + 手动 release |
| Content | mechanic completion | keep current; depth/new content cut 2026-07-30 — restart needs re-scoping |
| Meta | cross-run growth (TechPoints tech tree, 2026-08-09 ~ 2026-09-08) | score-only in-run（局外成长随纯街机流移除） |

## Phases

### Phase 0 — Tech-debt finish ✅ closed (2026-08-03)

Spawn path unified to pool, 4-service split, A3/A4 registry + declarative effect table, four fairness mechanics, CI/CD。test/ 门禁盲区修复（compile probe + 计数权威）。A8 PlayerVisuals 拆分为最后一条架构债。（细节见 git 历史与归档审计。）

### Phase 3 — Deferred/cut (restart needs explicit decision)

- **Local accounts**：landed 后于 2026-09-08 全量移除（纯街机流，见 Decisions）。
- **Mothership expansion**：landed（里程碑门控的加特林/导弹升级）。
- **Content evolution**：landed（3 buffs、分裂者、重型炮塔、第 4 Boss「月蚀」）+ mobile touch landed（2026-08-07）。（独立排行榜页未做；排行榜体系 2026-09-08 随账户移除。）
- **Endless k-value calibration**：landed（`progression.*` + ramp 因子；3 × 900s 探针零异常）。
- **Online leaderboard**：decided NO（2026-07-20）；反转需显式推翻。

## Decisions

> 摘要登记（决策 + 为什么 + 反转链），一行一条；细节见 git 历史与 DESIGN_BASELINE。

- **2026-08-05 统一实体/事件管理器**：EntityManager/GameEventManager 收敛注册样板、生命周期信号与随机事件分组并发。
- **2026-08-08 全量迁移 C#**（反转 2026-08-07 渐进混编）：终态零 GDScript、单一语言维护；csharp/core 与 csharp/godot 分层由此确立。
- **2026-08-09 局外成长（科技树）**：死亡结算唯一入口防刷点；效果 = 开局预置 buff 层数，复用 buff 计算链零新属性管道；不破坏 D1 必死曲线。
- **2026-08-11 得分/奖励审核**：连击计分温和版 + 低血防御保底；明确不做炸弹/掉落物/技能树重构（复杂度预算外）。
- **2026-08-29 测试与 CI 大幅削减**：51 断言场景 + autoplay 探针退役，CI 收敛单 fast-gate；行为回归 = xUnit + 两场景 + 人工窗口化验证。
- **2026-09-07 工程纪律裁剪**（「惯例恶心且冗杂」）：13 份行为规格退役归档、审计档案/SOP 废止、活文档收敛 5 份、BALANCE_MAP 去行号、注释禁审计轮次编号。依据：双源漂移曾消耗专门纠偏轮次；审计档案 1900+ 行持续膨胀。
- **2026-09-07 天赋缓存系统重构**（反转 2026-08-11「不做技能树重构」）：19 个既有 buff 节点组织成树，里程碑/Boss 点数入缓存池（LIFO 衰减），G 蓄力自主加点；四机制 = 派系互斥/专注惩罚/路线契约/风险加点。
- **2026-09-08 纯街机流**：移除登录/账户、对局存档、排行榜、分数显示与记录、局外成长；计分引擎保留为隐藏进度引擎。动机：免登录直玩、降低维护面。
- **2026-09-09 测试与文档大削减**（lean-reset）：测试资产全量退役；文档收敛为 DESIGN_BASELINE + ROADMAP 两份；CI 门禁 = 构建零警告 + 导入无警告 + 300 帧冒烟。
- **2026-09-10 TalentPanel 不接入 RadialMenuLayer**（「天赋应该有独立的视觉效果」）：天赋是元层特殊页，独立视觉形态是设计特征而非重复债务；四张导航页共用基类口径不变。
- **2026-09-10 视觉全面升级（战术琥珀）**：深炭蓝黑底 + 琥珀主交互 + 青降为数据通道；世界层手写屏幕纹理后处理补 GL Compatibility 无 Environment 辉光；**硬约束：玩法判定零改动**。口径见 DESIGN_BASELINE §2。
- **2026-09-10 过场演出细节精修**：共享乘员构件 CrewFigure 替换两处重复简笔人物 + 操作台/舱段/弹射通道细节补课；纯表现层，时序/字幕/音效口径不变。口径见 DESIGN_BASELINE §2.4。
- **2026-09-10 本局存档（反转 2026-09-08「无对局存档」）**：单存档位 `user://run.json`；存档 = 可继续的检查点（写入/覆盖、终结删档、非破坏性三规则）。口径见 DESIGN_BASELINE §2.5。
- **2026-09-10 性能设置（帧率上限 + 垂直同步）**：帧率六档（默认 60）+ VSync 开关（默认开）入设置页性能段。口径见 DESIGN_BASELINE §2.6。
- **2026-09-10 工程惯例整改**（「惯例过于繁杂冗余、拖慢进度、智能体协作困难」）：重建 AGENTS.md 为流程/门禁/提交纪律单源；ROADMAP Decisions 收敛一行索引制（细节归 git 历史，执行自身 Maintenance 规则）；提交信息立规精简（主题 ≤72 字 + 可选一段为什么，不写根因/修法/验证）；删除 .zcode/plans 历史计划。

## Maintenance

- 方向/阶段变化 → 更新本文件对应小节；不在此复述变更细节（git log 的职责）。
- 新债务/新发现 → 「已知债务与开放发现」末尾追加；修复后直接删除条目。
- Decisions 新增条目只写一行「决策 + 为什么 + 反转链」；实现细节、坑、验证过程一律不写（归 git 历史与 DESIGN_BASELINE）。
- 事实单源：流程/门禁口径只在 AGENTS.md 声明，本文件只给指针；设计口径只在 DESIGN_BASELINE.md 声明。
