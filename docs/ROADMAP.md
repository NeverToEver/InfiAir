# InfiAir Roadmap

> 方向决策与已知债务的单一权威。现状快照只在方向变化时更新；变更史看 git log 与 `CHANGELOG.md`；工程约定见 `AGENTS.md`；设计数值见 `docs/DESIGN_BASELINE.md`。

## Current State (2026-09-07)

- 内容演进与局外成长全部落地：4 Boss 轮换、母舰火力平台、触屏输入、科技树（TechPoints）、天赋缓存系统（2026-09-07，替代旧里程碑三选一与 line→双 buff 路线，低血保底随之退役）。无尽必死曲线（D1）为既定设计。
- 质量形态：xUnit 115 项纯逻辑 + 2 个断言场景（smoke/base）+ CI 单 job fast-gate（2026-08-29 大削减后的形态）。
- 2026-09-07 深度链路修复：设计基线核实无污染；修复 welcome 布局腐烂、RunTime 菜单污染、R 重开不删档、返航后 Boss 绕过入场窗口、精英事件钳制缺口（详见 CHANGELOG [3.33]）。
- 工程纪律同日裁剪：行为规格文档全部退役归档（不为行为写规格），审计留档制度废止，活文档收敛为 5 份（见 `AGENTS.md` 文档表）。

## 已知债务与开放发现

> 唯一登记处。修复后原地划掉并注日期；新发现追加在末尾。

- **[高·调查中] 间歇性全站输入丢失态（用户实报：welcome 页鼠标/键入全灭、仅方向键有效）**：2026-09-08 排查结论——游戏代码层无责：静态排查 Welcome/RadialWheel/遮罩层无吞输入路径；`ParseInputEvent` 合成事件探针在干净启动下 100% 通过（点击→确认框、聚焦、键入全通）。但同一构建同一序列可**间歇复现引擎级故障态**（连跑采样 0%~80% 失败率随时段波动），指纹：① 所有鼠标**按下**事件（合成与真实 OS SendInput 同）在到达 `Node._Input` 前被引擎丢弃，移动/抬起正常放行 → 按钮永远收不到完整点击；② `ui_*` 方向键焦点导航正常，字符键事件可达 `_Input` 但焦点 LineEdit 不录入；③ 故障态下 `Input.IsMouseButtonPressed(L)`=False、`Tree.Paused`=False、`TimeScale`=1（排除掩码卡死/树暂停）；④ 同机最小 Godot 工程与裸 InfiAir 工程（无 welcome 场景内容）对照均正常 → 与场景内容存在未知交互，未定位到具体组件。环境线索（Windows 11 笔记本 125% DPI、中文 IME、Console 会话）：IME 候选栏拦截字符输入已截图实锤（"zz"→中文候选，引擎零键盘事件）；WinForms 对照测试文本同样未送达 → 不排除系统会话级输入拦截（远程控制工具/触屏 WM_POINTER）参与。下一步：故障态启动时对比成功启动的全量事件日志（`pressed=`/掩码/焦点指纹）；排查 `Viewport::push_input`/`Window::_window_input` 门控（Godot 4.6.2 mono）与会话输入劫持源。诊断方法：临时探针场景实例化 welcome + `Input.ParseInputEvent` 注入 + `Node._Input` 全量事件打印（已删，按此描述可重建）；真实输入对照用 PowerShell SendInput/SendMessage 驱动实机窗口。
  2026-09-08 二轮实锤（探针重建为 `test/input_probe.tscn` + `csharp/godot/tests/InputProbe.cs`，调查中暂存）：**故障态真实输入现场首次完整捕获**——同一次运行内健康启动（合成事件全通、VERDICT 全 1），进入真实输入阶段后连续 10 次真实点击只见 release 不见 press（press 在 `Node._Input` 之前消失），期间窗口焦点无任何转移（无 FocusExited/Entered），键盘事件（q/Backspace/Enter/Esc/方向键）全程正常到达 `_Input`；用户连按 5 次 Esc 后点击**局内自愈**、press+release 恢复成对。结论收敛：与游戏代码无关、与窗口焦点无关；press 丢失点在引擎 WndProc 之前或之外（OS 投递/覆盖窗）；「Esc 治愈」指向 IME 候选窗/覆盖层的模态捕获被解除。
  2026-09-08 三轮（环境枚举）：机器上发现 **`oopz-overlay2.exe`（Oopz 语音开黑的游戏覆盖层）**，持有一个 `Topmost + Layered + Transparent + NoActivate + Visible`、290×1152px 常驻于 (1742,488) 的隐形全高覆盖窗——该类覆盖窗最吻合「吃 press 放行 release」的故障指纹与随时段波动的间歇性（覆盖层状态变化）。双击参数正常（500ms/4px），排除双击转换吞 press。**下一步验证：复现故障时先 `Stop-Process -Name oopz-overlay2` 再点击——恢复即实锤**；游戏内根治不可行（外部进程），可考虑在 welcome 页检测 overlay 注入并提示。
- **[中低] 召唤蓄力/机库小窗窗口期事件可触发**：事件互斥只拦「母舰在场」，蓄力 3s + 小窗 2.6s 内事件掷签命中 → 玩家锁输入 + 999s 无敌、母舰自动火力白拿事件奖励（L13 反向漏出）。需 Main↔GameEventManager 召唤窗口互斥标志，涉及遭遇触发门控时序。
- **[中低] 基地任务绝对计数轮换即完成**：kill/boss/survive 进度为对局绝对值，刷新抽到低门槛任务下一秒瞬领 RP（刷新经济泄漏）。修复需任务实例改为「抽取时快照基线」的相对进度，涉及存档格式。
- **[低] `_wavesPaused` 单布尔双写者**（Spawner × 2 事件）：正确性依赖管理器「encounter 组单活跃」不变量，绕过管理器直启事件会互踩。生产路径不可达。
- **[低] `Main.OnPlayerDied` 不清遭遇事件**：当前依赖「死亡必终局 + 场景重建」兜底；未来加复活/同局续命玩法需补 `EndActive(GROUP_ENCOUNTER)`。
- **[低] 视觉层无自动化覆盖**：全部无头门禁不经过 GPU/shader 管线，UI 布局腐烂可潜伏一个月（2026-09-07 W1 实证）。现行纪律 = UI/视觉改动窗口化人工过目；可选改进 = welcome 页视觉捕获场景（test/ 截图工具族同款）。
- **[低] 天赋描述文案沿用三选一时代措辞**：`BUFF_*_DESC`（19×2 键）仍是「最多 X 层」式的随机抽取措辞，与天赋面板的等级/收益递减语义有出入；需一轮文案订正。
- **[低] 专注惩罚触发面窄**：`talent.focus.threshold`(7) 实际仅 extra_life(上限10) 与风险加点档可达；若日后放宽节点等级上限需同步重校该阈值与惩罚曲线。
- **[低] v2 存档升 v3 天赋态归零**：设计决定不设兼容层——旧档读入后天赋为全新态（extra_life 归零 → 血上限回落基础值），属预期行为但玩家可感知；是否需要一次性补偿点数由方向决定。
- **[手工·发布前] Cinematic stage 4**：低配机复测 + 手柄/移动端手工项 + README 截图核对。
- **[手工·发布前] 真机手感验证**：15+ 分钟连续实机游玩（无尽校准与公平性机制的人工验收）。
- **[低] 设置/基地页轮盘聚焦项与右区面板初始章节不同步**：轮盘开后聚焦停在弧面居中槽，右面板默认第一个 tab（设置页「操作模式 vs 控制」、基地页「任务规划 vs 战机库」读法冲突）；基地页引线锚点（当前目录面板左缘中点）会落进面板装饰空域。宿主页联动接线遗留——打开时面板跟随聚焦项或轮盘聚焦指定项二选一。

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
- **2026-09-07 天赋缓存系统重构**（用户设计文档指令；反转 2026-08-11「不做技能树重构」）：旧里程碑三选一（BuffSelect）与 line→双 buff 路线全量删除、无兼容层。天赋 = 19 个既有 buff 节点组织成树（id 复用，全部效果消费端零改动），里程碑/Boss 点数入缓存池（LIFO 溢出衰减、不弹窗），G 键/HUD 指示器开面板自主加点；四机制 = 派系互斥 / 专注惩罚 / 路线契约（基地绑定，切换耗 RP 购置的重置代币）/ 风险加点。范围映射：设计文档的「局外」在本作落地为「本局存档周期」（死亡结算删档）；跨局持久成长仍归科技树，meta 升级 = 开局预置层级。数值唯一来源 `balance.json talent` 段。

## Maintenance

- 方向/阶段变化 → 更新本文件对应小节；不在此复述变更细节（CHANGELOG 的职责）。
- 新债务/新发现 → 「已知债务与开放发现」末尾追加；修复后划掉注日期。
