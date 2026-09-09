# InfiAir Roadmap

> 方向决策与已知债务的单一权威。现状快照只在方向变化时更新；变更史看 git log；设计数值见 `docs/DESIGN_BASELINE.md`。

## Current State (2026-09-09)

- 纯街机流落地（2026-09-08 用户指令）：账户/登录、对局存档（继续对局）、排行榜、分数显示与记录、局外成长（研究所/科技点）全量移除；开机流程 = main.tscn 开机自动播开场过场 → `scenes/title.tscn` 深空机库标题屏（形态详见 DESIGN_BASELINE §1.12）→ 开局。对局内分数保留为隐藏进度引擎（敌机解锁/Boss 节奏/事件门控/里程碑→天赋点）。无尽必死曲线（D1）为既定设计。
- 内容演进：4 Boss 轮换、母舰火力平台、触屏输入、天赋缓存系统（2026-09-07，替代旧里程碑三选一与 line→双 buff 路线，低血保底随之退役）。
- 质量形态：CI 单 job fast-gate = 构建零警告 + 资源导入无警告 + 主场景 300 帧运行（2026-09-09 测试资产全量退役后的形态）；回归验证 = 人工窗口化实机过目。
- 文档形态（2026-09-09 大削减后）：仅存本文件与 `docs/DESIGN_BASELINE.md` 两份设计文稿。

## 已知债务与开放发现

> 唯一登记处，只保留未关闭项；修复后直接删除（变更史在 git log）；新发现追加在末尾。

- **[低] 视觉层无自动化覆盖**：无头门禁不经过 GPU/shader 管线，UI 布局腐烂可潜伏（2026-09-07 W1 实证）。现行纪律 = UI/视觉改动窗口化人工过目；可选改进 = 视觉捕获探针场景（已随测试资产移除，需要时重建）。2026-09-09 巡检一轮：临时重建捕获探针，全屏面双语截图 53 张逐张过目（截图存 builds/probe_logs/visual_sweep/），实锤并修复 2 处——天赋缓存读数浮点尾数泄漏（EffectiveCache 经 %s 直格式化 double 显示 26.900000000000034，新增 TalentService.EffectiveCacheText 一位小数/整数显示口径，TalentPanel 标题+底栏与 Hud 悬停提示三处消费）、按钮焦点整板高亮洗白文字（focus 样式盒叠画于状态层之上，改描边环 + hover 字色翻深 TextOnBright，UITheme 单源）；探针用后即删。
- **[低] 专注惩罚触发面窄**：`talent.focus.threshold`(7) 按原始等级判定，现网唯 extra_life（上限 10）可达（2026-09-09 核实：其余节点结构上限最高 5，风险加点 +1 后 6 < 7，原登记「风险加点档可达」不准确）；若日后放宽节点等级上限需同步重校该阈值与惩罚曲线。
- **[手工·发布前] Cinematic stage 4**：低配机复测 + 手柄/移动端手工项。
- **[手工·发布前] 真机手感验证**：15+ 分钟连续实机游玩（无尽校准与公平性机制的人工验收）。
- **[低] 注释日期戳全量清理**：全库约 327 处注释含 `2026-0x-xx` 日期戳/审计编号前缀（c30ef49 自立「去日期戳/历史叙事」纪律后遗留；注释主体语义有效的仅去前缀）。2026-09-09 风格审查批次已订正失实注释 21 处（指向已删文档/旧 snake 名/行号漂移），渐进处理残余。

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

> 摘要登记（为什么）；细节见 git 历史。

- **2026-08-05 统一实体/事件管理器**：`EntityManager` 注册样板收敛 + 生命周期信号 + 批量 API；`GameEventManager` 收敛全部随机事件（fog‖encounter 分组并发、遭遇触发移出 spawner）。
- **2026-08-08 全量迁移 C#**（反转 2026-08-07 渐进混编）：存量 GDScript 全量迁移，终态零 GDScript、单一语言维护；`csharp/core` 纯逻辑 + `csharp/godot` 绑定层分层由此确立。
- **2026-08-09 局外成长（科技树）**：死亡结算唯一入口防刷点；效果 = 开局预置 buff 层数（复用 buff 计算链，零新属性管道）；每项限级 + 总消费上限，不破坏 D1 必死曲线。
- **2026-08-11 得分/奖励审核**：击杀连击计分（温和版链式得分）+ 低血防御保底；明确不做炸弹资源/掉落物/技能树重构（复杂度预算外）。
- **2026-08-29 测试与 CI 大幅削减**（用户指令）：51 断言场景 + autoplay 探针退役，仅存 smoke/base + perf_bench + 截图工具；CI 收敛单 fast-gate（format/零 GDScript/BALANCE_MAP 零 diff/编译探针闸一并退役）。行为回归 = xUnit + 两场景 + 人工窗口化验证。
- **2026-09-07 工程纪律裁剪**（用户指令「惯例恶心且冗杂」）：13 份行为规格文档退役归档、审计档案/执行留档/SOP 制度废止、活文档收敛 5 份、BALANCE_MAP 去行号（重构不再触发同步）、代码注释禁审计轮次编号。依据：行为规格与代码双源漂移曾消耗专门纠偏轮次（一次 19 文档 40+ 失实）；append-only 审计档案 1900+ 行且持续膨胀。
- **2026-09-08 纯街机流**（用户指令）：移除登录/账户（users.json）、对局存档（继续对局）、本地排行榜、分数显示与记录（HUD 分数/连击标签、最高分、结算名次/榜单）、局外成长（研究所/科技点/开局预置）。对局内计分引擎保留（不显示不记录）：驱动敌机解锁门、Boss 生成节奏、事件 min_score、里程碑→天赋点入账——天赋点获取不受影响。新开机流程：过场（可跳过/设置可关）→ 标题屏（任意键开始 / T 教程）→ 开局；死亡结算只呈现击杀统计。设置持久化收敛单文件 `user://settings.json`（难度三档移入设置页）。动机：免登录直玩、降低多系统维护面。
- **2026-09-07 天赋缓存系统重构**（用户设计文档指令；反转 2026-08-11「不做技能树重构」）：旧里程碑三选一（BuffSelect）与 line→双 buff 路线全量删除、无兼容层。天赋 = 19 个既有 buff 节点组织成树（id 复用，全部效果消费端零改动），里程碑/Boss 点数入缓存池（LIFO 溢出衰减、不弹窗），G 键/HUD 指示器开面板自主加点；四机制 = 派系互斥 / 专注惩罚 / 路线契约（基地绑定，切换耗 RP 购置的重置代币）/ 风险加点。范围映射：设计文档的「局外」在本作落地为「本局存档周期」（死亡结算删档）；跨局持久成长仍归科技树，meta 升级 = 开局预置层级。数值唯一来源 `balance.json talent` 段。
- **2026-09-09 测试与文档大削减**（用户指令，新分支 lean-reset）：测试资产全量退役——xUnit 工程、断言/截图/性能场景、运行时纯测试端口（TestExit、白盒 getter/setter、测试 setter 链、零调用 snake 桥）；文档收敛为 DESIGN_BASELINE + ROADMAP 两份（AGENTS/CLAUDE/README/CHANGELOG/CONTRIBUTING/SECURITY/TESTING/BALANCE_MAP/archive/截图 全删，gen_balance_map.py 随 BALANCE_MAP 退役）；CI 门禁 = 构建零警告 + 导入无警告 + 300 帧运行。行为回归 = 人工窗口化实机过目。动机：按纯街机流新基线重启开发，卸下历史流程包袱。
- **2026-09-10 TalentPanel 不接入 RadialMenuLayer**（用户指令「天赋应该有独立的视觉效果」）：天赋面板自管轮盘骨架（dim/holder/入场编排）保留，不并入 Settings/BaseConsole/GameOver 共用的圆盘页基类——天赋是元层特殊页（蓄力开启、混合焦点页、右区 stagger 编排），独立视觉形态是设计特征而非重复债务；四张导航页共用基类的口径不变。轮盘圆心坐标常量仍走 `RadialMenuLayer.WheelRest` 单源（物理布局口径，不绑定视觉风格）。

## Maintenance

- 方向/阶段变化 → 更新本文件对应小节；不在此复述变更细节（git log 的职责）。
- 新债务/新发现 → 「已知债务与开放发现」末尾追加；修复后直接删除条目。
