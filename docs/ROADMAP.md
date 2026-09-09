# InfiAir Roadmap

> 方向决策与已知债务的单一权威。现状快照只在方向变化时更新；变更史看 git log；设计数值见 `docs/DESIGN_BASELINE.md`。

## Current State (2026-09-09)

- 纯街机流落地（2026-09-08 用户指令）：账户/登录、对局存档（继续对局）、排行榜、分数显示与记录、局外成长（研究所/科技点）全量移除；开机流程 = main.tscn 开机自动播开场过场 → `scenes/title.tscn` 黑屏标题屏（按任意键开始 / T 教程）→ 开局。对局内分数保留为隐藏进度引擎（敌机解锁/Boss 节奏/事件门控/里程碑→天赋点）。无尽必死曲线（D1）为既定设计。
- 内容演进：4 Boss 轮换、母舰火力平台、触屏输入、天赋缓存系统（2026-09-07，替代旧里程碑三选一与 line→双 buff 路线，低血保底随之退役）。
- 质量形态：CI 单 job fast-gate = 构建零警告 + 资源导入无警告 + 主场景 300 帧运行（2026-09-09 测试资产全量退役后的形态）；回归验证 = 人工窗口化实机过目。
- 2026-09-07 深度链路修复：设计基线核实无污染；修复 welcome 布局腐烂、RunTime 菜单污染、R 重开不删档、返航后 Boss 绕过入场窗口、精英事件钳制缺口（详见 git log）。
- 文档形态（2026-09-09 大削减后）：仅存本文件与 `docs/DESIGN_BASELINE.md` 两份设计文稿。

## 已知债务与开放发现

> 唯一登记处。修复后原地划掉并注日期；新发现追加在末尾。

- **[高·调查中] 间歇性全站输入丢失态（用户实报：welcome 页鼠标/键入全灭、仅方向键有效）**：2026-09-08 排查结论——游戏代码层无责：静态排查 Welcome/RadialWheel/遮罩层无吞输入路径；`ParseInputEvent` 合成事件探针在干净启动下 100% 通过（点击→确认框、聚焦、键入全通）。但同一构建同一序列可**间歇复现引擎级故障态**（连跑采样 0%~80% 失败率随时段波动），指纹：① 所有鼠标**按下**事件（合成与真实 OS SendInput 同）在到达 `Node._Input` 前被引擎丢弃，移动/抬起正常放行 → 按钮永远收不到完整点击；② `ui_*` 方向键焦点导航正常，字符键事件可达 `_Input` 但焦点 LineEdit 不录入；③ 故障态下 `Input.IsMouseButtonPressed(L)`=False、`Tree.Paused`=False、`TimeScale`=1（排除掩码卡死/树暂停）；④ 同机最小 Godot 工程与裸 InfiAir 工程（无 welcome 场景内容）对照均正常 → 与场景内容存在未知交互，未定位到具体组件。环境线索（Windows 11 笔记本 125% DPI、中文 IME、Console 会话）：IME 候选栏拦截字符输入已截图实锤（"zz"→中文候选，引擎零键盘事件）；WinForms 对照测试文本同样未送达 → 不排除系统会话级输入拦截（远程控制工具/触屏 WM_POINTER）参与。下一步：故障态启动时对比成功启动的全量事件日志（`pressed=`/掩码/焦点指纹）；排查 `Viewport::push_input`/`Window::_window_input` 门控（Godot 4.6.2 mono）与会话输入劫持源。诊断方法：临时探针场景实例化 welcome + `Input.ParseInputEvent` 注入 + `Node._Input` 全量事件打印（已删，按此描述可重建）；真实输入对照用 PowerShell SendInput/SendMessage 驱动实机窗口。
  2026-09-08 二轮实锤（探针重建为 `test/input_probe.tscn` + `csharp/godot/tests/InputProbe.cs`，调查中暂存）：**故障态真实输入现场首次完整捕获**——同一次运行内健康启动（合成事件全通、VERDICT 全 1），进入真实输入阶段后连续 10 次真实点击只见 release 不见 press（press 在 `Node._Input` 之前消失），期间窗口焦点无任何转移（无 FocusExited/Entered），键盘事件（q/Backspace/Enter/Esc/方向键）全程正常到达 `_Input`；用户连按 5 次 Esc 后点击**局内自愈**、press+release 恢复成对。结论收敛：与游戏代码无关、与窗口焦点无关；press 丢失点在引擎 WndProc 之前或之外（OS 投递/覆盖窗）；「Esc 治愈」指向 IME 候选窗/覆盖层的模态捕获被解除。
  2026-09-08 三轮（环境枚举）：机器上发现 **`oopz-overlay2.exe`（Oopz 语音开黑的游戏覆盖层）**，持有一个 `Topmost + Layered + Transparent + NoActivate + Visible`、290×1152px 常驻于 (1742,488) 的隐形全高覆盖窗——该类覆盖窗最吻合「吃 press 放行 release」的故障指纹与随时段波动的间歇性（覆盖层状态变化）。双击参数正常（500ms/4px），排除双击转换吞 press。**下一步验证：复现故障时先 `Stop-Process -Name oopz-overlay2` 再点击——恢复即实锤**；游戏内根治不可行（外部进程），可考虑在标题屏/设置页检测 overlay 注入并提示。
  2026-09-08 附注：探针场景（`test/input_probe.tscn` + `csharp/godot/tests/InputProbe.cs`）随 welcome 场景移除一并删除（探针依赖 welcome 测试钩子）；按上方诊断方法描述可在新入口场景（标题屏/设置页）重建。
- ~~**[中低] 召唤蓄力/机库小窗窗口期事件可触发**~~：已修（2026-09-09）——`GameState.SummonInProgress` 旗帜由 Main 逐帧维护（蓄力/小窗期 true，_Ready/_ExitTree 复位），`GameEventManager` 遭遇触发门控读取；窗口期事件不掷签，L13 反向漏出封闭。无头全流程探针实测蓄力+小窗期 encounter 触发 0 次。
- ~~**[中低] 基地任务绝对计数轮换即完成**~~：已修（2026-09-09）——任务条目改相对口径：抽取时快照该 kind 对局绝对计数为 `baseline`（`MissionsService._lastKindValue` 逐次上报缓存），进度 = 绝对值 − 基线；初始手牌 baseline=0，保留的已完成未领取任务基线不动。无头探针实测 50 杀刷新后新任务 progress=0、再 1 杀=1（探针用后即删）。
- ~~**[低] `PlayerDied` 信号发射先于 `player.Die()`**（PlayerDamage.cs）~~：已修（2026-09-09）——发射点自 `CombatStateService.LoseHealth` 迁入 `Player.DieInternal`（`_dead` 置位+死亡结算完成后），回调内 `IsDead()` 恒 true；`DieInternal` 补幂等守卫（同帧二次致死不重复结算/双发）。探针实测发射 1 次且回调内 `IsDead()==true`（探针用后即删）。
- ~~**[低] `_wavesPaused` 单布尔双写者**~~：已修（2026-09-09）——改计数口径（Start +1 / ResumeWaves −1 钳底 0），双写者互踩不再提前解禁。
- ~~**[低] `Main.OnPlayerDied` 不清遭遇事件**~~：已修（2026-09-09）——死亡路径补 `EndActive(GROUP_ENCOUNTER)` + spawner `SetProcess(false)`/`ClearPending`，不再依赖「结算 UI 同帧暂停树」的巧合安全。
- **[低] 视觉层无自动化覆盖**：全部无头门禁不经过 GPU/shader 管线，UI 布局腐烂可潜伏一个月（2026-09-07 W1 实证）。现行纪律 = UI/视觉改动窗口化人工过目；可选改进 = 视觉捕获探针场景（2026-09-09 已随测试资产移除，需要时可重建）。
- ~~**[低] 天赋描述文案沿用三选一时代措辞**~~：已修（2026-09-09）——`AUG_*_DESC` 18×2 键订正（债务登记时称 BUFF_*，键已更名）：去「最多 X 层/可叠/max stacks」随机抽取措辞，改「每级」等级语义；上限数值不再入文案（去双源——天赋面板详情卡 TALENT_LV_FULL_FMT 已显示生效/结构上限）；salvo 间隔（7−2/级→5 发起）/second_wind(3 HP/s×级）/dash_strike(35×级）/deflector(×0.78/×1.6)/graze(×1.2、+5) 等数值逐一对照 balance.json 与 Player.RefreshAugmentFactors 核实。
- **[低] 专注惩罚触发面窄**：`talent.focus.threshold`(7) 实际仅 extra_life(上限10) 与风险加点档可达；若日后放宽节点等级上限需同步重校该阈值与惩罚曲线。
- ~~[低] v2 存档升 v3 天赋态归零~~：随对局存档系统移除（2026-09-08）失效。
- **[手工·发布前] Cinematic stage 4**：低配机复测 + 手柄/移动端手工项。
- **[手工·发布前] 真机手感验证**：15+ 分钟连续实机游玩（无尽校准与公平性机制的人工验收）。
- ~~**[低] 设置/基地页轮盘聚焦项与右区面板初始章节不同步**~~：已修（2026-09-09）——取「轮盘聚焦指定项」方案（沿用暂停/结算页 FocusOption(0) 先例）：SettingsUi.ShowSettings/BaseConsole.ShowBase 开页聚焦首项（控制/战机库），与默认面板对齐；基地页引线锚点自「当前目录面板左缘中点」（恰与切角面板装饰性中位拼板缝 h*0.5 重合）改锚「当前分类标题」标签，VisiblePage() 死代码随删。探针实测开页聚焦 controls/hangar + 锚点 = _categoryLabel（探针用后即删）。
- **2026-09-09 游玩问题修复批次**（无头全流程探针定位+验证，探针用后即删）：① ~~暂停→退出游戏→取消退出软锁~~（ExitConfirm 增 `Canceled` 事件，PauseUi 取消后恢复可见+轮盘活性）；② ~~设置页返回暂停页轮盘永久失活~~（`PauseUi.GrabPrimaryFocus` 恢复 `SetWheelActive(true)`）；③ ~~标题屏同帧多输入双重切场景/T 组合键目的地错~~（一次性 `_started` 守卫）；④ ~~暂停/结算页轮盘键盘导航全灭~~——根因：TalentPanel 隐藏后其轮盘子节点 `Visible` 标志仍为 true，在 `_UnhandledInput` 相位抢吞 `ui_*`（`RadialWheel` 键盘导航移入 `_Input` 先 GUI 相位 + 输入门控改 `IsVisibleInTree()`；SettingsUi/BaseConsole 混合页轮盘设 `KeyboardEnabled=false` 让位页面焦点链）；⑤ ~~暂停/结算轮盘开页默认聚焦弧面中点槽~~（4 项停在「重新出击」，Esc 后误 Enter 直接重开）——`FocusOption(0)` 开页聚焦首项；⑥ 标题屏→开局路径补幂等 `ResetRun`（不再依赖上游约定）；⑦ 死代码清理（`RestoreHealth`/`RestoreMilestones`/`MilestoneMult` 内部桥，随存档系统删除后零调用）。
- ~~**[低] 教程基地开启的 ~1.2s 窗口内 Esc 失灵**~~：已修（2026-09-09）——补 Always 态返回路由：`TutorialEscRouter`（OpenBase 时挂载、CloseBase 时释放，ProcessMode=Always）暂停树期间代收 `ui_cancel` 转发 `ExitTutorial`；探针实测暂停树下路由节点收到 `_UnhandledInput`（探针用后即删）。

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

- ~~**Local accounts**~~：landed 后于 2026-09-08 全量移除（纯街机流，见 Decisions）。
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

## Maintenance

- 方向/阶段变化 → 更新本文件对应小节；不在此复述变更细节（git log 的职责）。
- 新债务/新发现 → 「已知债务与开放发现」末尾追加；修复后划掉注日期。
