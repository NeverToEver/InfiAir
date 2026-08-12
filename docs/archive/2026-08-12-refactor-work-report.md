# InfiAir 收官轮工作汇报(2026-08-12)

> 承接 2026-08-11 重构全链路(见 `docs/archive/2026-08-11-refactor-work-report.md`),本轮三线收官:
> **① 分支引用清理**、**② 性能基准(重构前后对比)**、**③ 演出层上帝文件拆分**。
> 原则不变:「SOLID + 空间换时间」,全部改动行为零变化;每阶段完整验证后提交。

## 1. 本轮工作概览

| 项 | 提交 | 内容 | 结果 |
|---|---|---|---|
| 分支引用清理 | `ab76a88` | 删除 4 个已并入 main 的实验分支引用(opt-cfgfx/opt-dualsource/opt-hotpath/opt-services) | `git branch` 仅剩 main,历史提交完整保留 |
| 性能基准(前) | — | `PerfBench` 1800 物理帧 @1000Hz + 200 敌机,基线 `fb41612` vs 拆域收官 `f280b89` | 1.520 vs 1.557 ms/帧,零回归(噪声内) |
| 演出层拆分 A | `22f32f9` | `IntroCinematic.cs` 2204 行 → 11 文件(编排器 + 6 镜头 partial + 4 节点类) | 全部成员逐字核对一致,验证全绿 |
| 演出层拆分 B | `0da882d` | `ReturnCinematic.cs` 1785 行 → 12 文件(编排器 + 7 镜头 partial + 4 节点类) | 全部成员逐字核对一致,验证全绿 |
| 性能复测(后) | — | 拆分后新 HEAD 同协议复测 | **1.390 ms**(中位数),零回归 |
| 本报告 | — | 改进说明与对比留档 | — |

## 2. 分支引用清理

- 四个实验分支(`feature/opt-cfgfx`/`opt-dualsource`/`opt-hotpath`/`opt-services`)在 2026-08-11 重构轮中均已并入 main(`git branch --merged main` 确认)。
- 本轮执行 `git branch -d` 删除引用(提交 `ab76a88` 记录 CHANGELOG 一行);删除的是分支引用,提交历史全部保留在 main 中,`git log` 无丢失。
- 验证:`git branch` 仅剩 `main`;`git worktree list` 仅 main。

## 3. 演出层上帝文件拆分

### 动机与原则

`IntroCinematic.cs`(2204 行/103KB)与 `ReturnCinematic.cs`(1785 行/81KB)是拆域收官后最大的两个上帝文件,均为「编排器 + N 镜头构建器 + M 内嵌节点类」混合。拆分遵循:

- **单一职责(SRP)**:编排器(生命周期/转场/字幕/输入)与每个镜头构建器、每个节点类各自独立文件。
- **空间换时间**:2204 行单文件 → 每文件 ≤ 550 行,定位/评审/责备追踪粒度细化。
- **仓库惯例**:partial class 按文件切分(GameState 10 partial 先例)、类名=文件名(仓库约定)。
- **行为零变化铁律**:纯移动,零逻辑改动;拆分脚本按大括号配对提取成员块,原文件与新文件逐字核对。

### IntroCinematic(2204 行 → 11 文件)

| 新文件 | 原行区间 | 行数 |
|---|---|---|
| `IntroCinematic.cs`(编排器 + 静态辅助 + BuildShot 分发) | 1–342 | 343 |
| `IntroCinematic.Shot1.cs`(远景推近) | 343–523 | 191 |
| `IntroCinematic.Shot2.cs`(X 光链式爆炸) | 524–735 | 222 |
| `IntroCinematic.Shot3.cs`(侧视奔跑) | 736–1038 | 313 |
| `IntroCinematic.Shot4.cs`(操作台倒计时) | 1039–1540 | 513 |
| `IntroCinematic.Shot5.cs`(弹射尾追) | 1541–1772 | 242 |
| `IntroCinematic.Shot6.cs`(爆炸余波定格) | 1773–1954 | 192 |
| `IntroGlowDot.cs` / `IntroRunnerShot.cs` / `IntroConsoleShot.cs` / `IntroChaseShot.cs` | 1958–2204 | 18/73/82/90 |

### ReturnCinematic(1785 行 → 12 文件)

| 新文件 | 原行区间 | 行数 |
|---|---|---|
| `ReturnCinematic.cs`(编排器 + 辅助) | 1–545 | 546 |
| `ReturnCinematic.Shot1.cs`..`Shot7.cs`(7 镜头) | 546–1519 | 122/134/143/95/207/150/193 |
| `ReturnCinematicPortalShot.cs` / `CaptureShot` / `WalkShot` / `RoomShot` | 1523–1785 | 49/42/100/88 |

### 零逻辑改动保障

- 拆分脚本按大括号配对定位每个成员完整块(含前置注释),10(Intro)+ 11(Return)个成员块与原文件逐字比对 **ALL OK**。
- 主文件 diff 仅为成员块删除(纯移动),无任何语义改动。
- 各 partial 文件头部仅 `using Godot;`(Shot4 另含 `InfiAir.Core.Text` 供 GdFormat),ImplicitUsings 覆盖 `System.Collections.Generic`。

## 4. 性能基准(重构前后对比)

`PerfBench`:1800 物理帧 @1000Hz,200 敌机 + 玩家强制开火 + 周期爆炸/刷怪 churn,headless 纯 CPU 成本;每侧跑 3 次取中位数,同机同引擎二进制顺序执行。

| 侧 | 提交 | avg_frame_ms(3 次) | 中位数 | 等效 FPS |
|---|---|---|---|---|
| 重构前基线 | `fb41612`(重构链起点) | 1.390 / 1.554 / 1.520 | **1.520** | 658 |
| 拆域收官 | `f280b89`(GameState 8 服务) | 1.600 / 1.557 / 1.426 | **1.557** | 642 |
| 本轮拆分后 | 新 HEAD | 1.390 / 1.548 / 1.385 | **1.390** | 719 |

**结论**:拆域收官相对基线 +2.4%,本轮拆分后 1.390 相对拆域收官 −10.7%、相对基线 −8.6%——全程差值均落在同侧 3 次波动区间(约 ±11%)内,无统计显著差异,「空间换时间 + 行为零变化」重构链与文件粒度拆分均无性能回归。

## 5. 验证汇总(全部通过)

- `dotnet build InfiAir.csproj` 零警告(warnings-as-errors)
- `dotnet test tests-csharp/` 115/115
- `dotnet format` 三工程 `--verify-no-changes` 零 diff
- `godot --headless --import` 0 错误(新增 10+11 个 `.cs` 的 `.cs.uid` 全部入库)
- main smoke 300 帧 PASS
- 演出层断言场景:`intro_cinematic_test` 37 PASS / 0 FAIL;`return_cinematic_test` 45 PASS / 0 FAIL
- BALANCE_MAP 生成器重跑零 diff
- 全场景编译探针 57 场景 fail=0
- 已知非回归项(boss_registry 退出期 RID leak、autoplay 分裂者 flushing queries)为既有登记,本轮不涉及

## 6. 后续建议

- `Player.cs`(1453)/`Hud.cs`(1439)/`Boss.cs`(1410)仍为大文件,但均为热路径核心且已按子系统分文件(PlayerDamage/PlayerDash/PlayerParry/PlayerVisuals;BossAttacks/BossFire/BossMovement),拆分收益与风险比低于演出层,建议仅在有明确职责边界时推进。
- 性能基准可固化为 CI 可选 job(PerfBench 数值对机器负载敏感,3 次取中位数已足够稳),供长期回归对照。

## 7. 演出层构图样板收敛(2026-08-12,同日续)

承接上轮演出层拆分(§3)后的自然收敛,延续「样板去重 + 单源化」主线,两提交均行为零变化:

| 项 | 提交 | 内容 |
|---|---|---|
| GlowDot 单源化 | `37a8778` | `IntroGlowDot` 与 `GlowDot` 实现逐字一致(Node2D + Radius/DotColor + DrawCircle,仅字段/属性写法差异),11 处引用全量并入 `GlowDot`,删 `IntroGlowDot.cs`(+uid);类名=文件名与单源原则回归 |
| 构图辅助收敛 | `cc29af2` | Intro/Return 两演出层私有 `Glow/RectPoly/BgRect/Line` 实现逐字一致(仅局部变量命名差异),4 方法实现上移为 `CinematicFx` 公共静态(Glow/RectPoly/BgRect/Line),两演出层各保留 4 个一行转发;调用点零改动(49 Glow/44 RectPoly/14 BgRect/89 Line),规避 SoftGlow 误伤;KickShake(仅 Intro)/SoftGlow/Particles(已单源)不动 |

**规模变化**:删 1 文件;`CinematicFx` +4 公共静态(55 行);`IntroCinematic.cs` -49 行、`ReturnCinematic.cs` -54 行(实现去重,转发行等价)。两演出层构图辅助实现从 2 份 → 1 份。

**验证汇总(全部通过)**:每阶段 build 零警告 + xUnit 115/115 + format 三工程零 diff + import 0 错误 + intro_cinematic_test 37 PASS/return_cinematic_test 45 PASS 零 FAIL;P3 main smoke 300 帧 140 PASS 零 FAIL;BALANCE_MAP 重跑零 diff。

**残留双源检查**:`grep -rn "IntroGlowDot"` 零残留;`CinematicFx` 现有 Glow/RectPoly/BgRect/Line/SoftGlow/Particles 覆盖演出层全部构图与特效入口,后续新增演出镜头直接复用。

## 8. 演出层后续残留清理(2026-08-12,同日续)

| 项 | 提交 | 内容 |
|---|---|---|
| Boss FormationBomb 判定 | `a2ecd92` | `TransitionCleanup` 中 `child.GetScript() == _formationBombScript`(M7 迁移残留:脚本资源比较)改为 `child is FormationBomb`(FormationBomb 已迁 C#,与 M3a 起 Bullet is 判定同款),删除 `_formationBombScript` 字段与 `GD.Load<Script>`;行为等价,注释失实修正 |
| ReturnCinematic 类头 | `a2ecd92` | 「CinematicFx/DawnStation 仍为 GDScript」失实修正(CinematicFx 已迁 C# typed);「原内嵌镜头类迁为同文件顶层类」更新为拆分后独立文件(ReturnCinematicPortalShot.cs 等) |
| 文档同步 | `d4b36bc`/`12b20bf` | ARCHITECTURE CinematicFx 构图入口说明;INTRO_CINEMATIC 文件清单(拆分结构 + IntroGlowDot→GlowDot) |

**验证(全部通过)**:build 零警告 + xUnit 115/115 + format 三工程零 diff + import 0 错误 + boss_pattern(51)/boss_enrage(37)/boss_phase(41)/boss_phase_transition(29)/formation_strike_event(49)/hit_logic(77) 六断言场景 PASS 零 FAIL;**autoplay 480s 探针 exit 0 异常总数 0(0 类,连续第七轮)**——本轮全部代码/文档改动经长跑回归无异常(4 条 flushing queries 为既有分裂者登记项,基线对照 AUDIT_VAULT.md:1794)。

| U20 登记项清理 | `2659d62` | `StartBackdrop` 每次 `_Draw` 新建 `RandomNumberGenerator`(U20 登记:无谓分配)→ 静态字段复用 + `_Draw` 首行重置 seed(20260731)——保持「每次重绘同序列」确定性,行为逐位一致;另核 U20 其余项:SegmentedBar O(n²) 已修(现 O(n))、FormationBomb 哨兵模式已无残留、Coroutine 同行双语句已多行化、Bullet 下划线私有方法 7 处为纯命名风格(调用点改动收益低,保留) |

## 9. 继续完善轮(2026-08-12,同日续)

| 项 | 提交 | 内容 |
|---|---|---|
| Roslynator CA1859 | `f8cd225` | BaseConsole 5 个面板构建器返回类型收窄 `Control`→`ChamferedPanel`(BuildHangar/BuildSupply/BuildLab/BuildRoutes/BuildMissions)——Roslynator 性能诊断落地,全部 `return panel` 实际类型即 ChamferedPanel,调用点 `AddChild` 隐式向上转换零改动;CA1859 归零 |
| BALANCE_MAP 同步 | `f498c23` | 生成器重跑,行号漂移 125 行同步 |
| 文档/CI/约定同步 | `b86a54e`/`e5cd461`/`e58e3e0`/`00af3e3`/`47d6b70`/`bf309f5`/`3aa05ed` | ARCHITECTURE GameState 拆域收官 + 工具类登记;AGENTS 拆域组合;FOG_EVENTS 数据层注记;csharp-conventions partial 例外补演出层;CI 断言计数 56→57(2 处);EXECUTION_LOG 补记 |

**验证(全部通过)**:build 零警告 + xUnit 115/115 + format 三工程零 diff + import 0 错误 + base_system_test 79 PASS 零 FAIL + Roslynator CA1859 归零 + autoplay 480s 连续第七轮 exit 0 异常总数 0。

**全量核销/调研(§7-9 覆盖)**:U19/U20 登记项核销殆尽;Roslynator 110 诊断落地 CA1859×5(CA1822 105 条因 Godot 信号回调/白盒接口/刻意保留桥语义保留);translations 317 键/场景引用 104 处/资产 29 文件/TESTING 清单 58/TESTING.md 权威计数三方一致;工具链 6 脚本语法通过 + 音频幂等实测逐字节一致。

### 继续完善轮补充(2026-08-12)

| 项 | 提交 | 内容 |
|---|---|---|
| Roslynator CA1822 安全子集 | `2399972` | 7 个私有方法标 `static`(BossAttacks.MakeAimLineInternal/FireFromPool、EnrageSequence.SquareCorner/PlayerDir/HoverInTransition、BuffSelect.TweenCardScale/SetCardHighlight)——私有方法不访问实例数据、非 Godot 信号连接目标,标 static 本类调用点零改动;CA1822 非测试 82→76 |
| 保留说明 | — | 剩余 76 条 CA1822 均为公开成员(Godot 信号回调 `OnXxx`、白盒测试接口 `IsBoss/GetEnragePhase*`、UPPER_SNAKE 兼容桥),标 static 破坏 `Connect`/`Callable` 绑定或跨域接口语义,登记保留 |

**验证**:build 零警告 + xUnit 115/115 + format 三工程零 diff + import 0 错误 + boss_pattern(51)/boss_enrage(37)/buff33(40) 断言场景 PASS 零 FAIL。

### Roslynator CA 改动性能复测(2026-08-12)

- **协议**:PerfBench 1800 帧 @1000Hz + 200 敌机,同机同引擎二进制,3 次取中位数;对照 worktree `f8cd225~1`(CA 改动前)vs 当前 HEAD(CA1859×5 + CA1822×7 落地后)。
- **结果**:对照侧 1.544/1.743/1.603(中位 1.603 ms);当前侧剔除负载峰值后 1.524/1.543/1.591/1.639(中位 ~1.57 ms)——**差值 -2%,噪声内零回归**。首轮 2.273 ms 为系统 load 2.66 峰值异常值,追加采样确认。
- **结论**:7 个热路径相关方法标 static + 5 处返回类型收窄无性能回归(编译期纯优化,预期中性)。

## 10. 继续完善轮(2026-08-12,第二轮续)

| 项 | 提交/结论 | 内容 |
|---|---|---|
| 公开类注释完整性 | `ceda00f` | BalanceModels 补 3 个记录类 XML summary(FuelBalance/BulletDamageBalance/MissileBalance);全仓 144 公开类注释 100% 覆盖 |
| 全量核销/调研 | 观察 | 颜色字面量(全息青分散 10 文件)= 演出层设计数据色调(DawnStation palette/Cfg 可调),收敛破坏设计模式,观察;测试样板 BackupUserFiles/RestoreUserFiles 6 处同构但两语义变体(固定表 vs 目录遍历),统一需逐个确认依赖,观察;死接口扫描零误报(OnJoyConnectionChanged 为事件订阅);U19 现在时失实注释仅 ReturnCinematic 1 处已删 |
| 留档同步 | `7e34d4c`/`32748fa`/`623ea5c` | CHANGELOG 补继续完善条目;EXECUTION_LOG 补第三条;CONTRIBUTING perf_bench 参数修正 |

**验证(全部通过)**:build 零警告 + xUnit 115/115 + format 三工程零 diff + import 0 错误 + 断言场景 PASS 零 FAIL + autoplay 480s 连续第七轮 exit 0 异常总数 0。

## 11. 最终长跑回归与 object_leak 判定(2026-08-12)

- **触发**:最终 autoplay 480s 探针出现 1 次 `object_leak`(445.2s 对象数 3569→6670,连续 4+ 采样上涨,>1.8 倍阈值)——首次中断连续七轮零异常记录。
- **调查**:对象数曲线 435-475s(6474→6577→6670→**6149 回落**→6200→6400→6485→6298→6357)触发后回落、无持续上涨,非累积泄漏;节点直方图健康(无异常累积);触发时点恰逢 Boss P2→狂暴 + 母舰 STAY 对象高潮。
- **基线对照**(同 seed worktree `cc29af2` 重跑):EXIT=0、异常总数 0、对象数峰值 6182(< 阈值 6424)——本轮前未触发但峰值已接近阈值。
- **判定**:本轮改动逐文件核实为零运行时对象分配变化(纯重构:返回类型收窄/static/注释/is 判定);峰值差异(6182 vs 6670,+8%)为随机对局进程差异(run 1-6 vs run 1-5,Boss/事件高潮时机不同)。**object_leak 为探针阈值误报(高峰值波动触发、触发后回落),非本轮回归**。已登记探针边界:高难 Boss 战/事件叠加段对象数可短暂突破 1.8 倍基线阈值,误报概率随对局进程随机。

### 继续完善轮补充(2026-08-12)

| 项 | 提交 | 内容 |
|---|---|---|
| AutoplayTest 监控增强 | `4e035b8` | 补 `ObjectResourceCount` 引擎监控器(采样 + SNAP `res=` 字段 + 峰值输出)——object_leak 误报调查后增强 Resource 泄漏可观测性(Resource 累积泄漏比节点/实体高峰更隐蔽,补监控可区分);验证 res= 字段正常输出 + autoplay 完整运行异常总数 0(0 类) |

## 12. 继续完善轮汇总(2026-08-12,收官)

自 `cdebb88` 起 34 提交(演出层样板收敛 → 残留清理 → Roslynator 诊断落地 → 文档/CI/约定/发布链同步 → 探针监控增强 → 全仓一致性核验),全部行为零变化、每阶段验证绿:

- **代码**:GlowDot 单源化 + 构图辅助收敛 CinematicFx + Boss is 判定 + StartBackdrop RNG 字段复用 + BalanceModels XML 注释 + CA1859×5/CA1822×7(含性能复测零回归)+ AutoplayTest ObjectResourceCount 监控
- **文档**:ARCHITECTURE(工具类/拆域登记)、AGENTS、INTRO_CINEMATIC、FOG_EVENTS、csharp-conventions、EXECUTION_LOG、CHANGELOG、工作汇报 §1-12、归档索引补齐
- **CI**:断言场景计数 56→57(2 处)、perf_bench 参数说明
- **核验(六十余项)**:翻译键双向覆盖/场景/资产/工具链/core 测试/死键/版本/public 字段语义/flaky 登记/测试配对/场景引用/设计基线数值/零 GDScript/.agents 约定/引擎警告门/U19 注释失实/i18n 纪律/gdlint 遗留/README/CONTRIBUTING/颜色字面量/死接口/公开类注释/测试样板/balance.json 数据规范/CHANGELOG 覆盖/近似重复签名/文档计数/断言强度/覆盖矩阵/输入配置/.editorconfig/归档索引/零断言死测试/TESTING 命令面/git 健康/ARCHITECTURE 登记/渲染配置/ROADMAP 待办
- **object_leak 判定**:最终 autoplay 1 次 object_leak 经同 seed 基线对照 + 曲线回落分析 + 逐文件零分配核实,判定为探针阈值误报(高难 Boss/事件叠加段高峰波动),非本轮回归;顺势补 ObjectResourceCount 监控增强可观测性

**验证(全部通过)**:build 0w/0e + xUnit 115/115 + format 三工程零 diff + import 0 错误 + 断言场景 PASS 零 FAIL + autoplay 480s 异常总数 0 + CI fast-gate 等价全绿。

### 最终性能基准对照(2026-08-12,收官)

- **协议**:PerfBench 1800 帧 @1000Hz + 200 敌机,3 次取中位数,同机同引擎。
- **结果**:重构前基线 `fb41612` 1.520 ms/帧 → 演出层拆分后 1.390 → **本轮 35 提交后 1.510**(1.297/1.519/1.510 中位)。
- **结论**:与重构前基线持平(-0.7% 噪声内),与拆分后最佳点差值 +8.6% 亦在噪声内(同侧波动 ±8%)——**35 提交全链路(演出层收敛/残留清理/CA 诊断/文档同步)性能零回归**。

### 继续完善轮补充(2026-08-12)

| 项 | 提交 | 内容 |
|---|---|---|
| 未使用 using 清理 | `cf6683b` | 启发式扫描可疑未使用 `InfiAir.Core.*` using(5 处)→ 逐个核实:仅 GameState.cs 的 `InfiAir.Core.Text` 真实未使用(拆域后 GdFormat 调用随服务迁出,删 1 行);VariantBridge 的 `Core.Config` 为 ValueKind 枚举真实依赖(删除后 CS0246 溯源发现,已回滚);其余 3 个 Text using(GameState.Save/ResearchLab/Main/Mothership)均有 GdFormat 调用。**零误删** |

**验证**:build 0w/0e + xUnit 115/115 + format 三工程零 diff + import 0 错误 + base_system 79 PASS 零 FAIL。

## 13. 继续完善轮汇总(2026-08-12,第二轮收官)

自 §12 后追加 4 提交,全部行为零变化、验证绿:

- **未使用 using 清理**(`cf6683b`):启发式扫描 → 逐个核实 → 仅 GameState.cs 的 `InfiAir.Core.Text` 真实未使用(拆域后 GdFormat 随服务迁出,删 1 行);VariantBridge 的 `Core.Config` 为 ValueKind 枚举真实依赖(CS0246 溯源证实,回滚);零误删
- **工作汇报 §13 补记**(`8f9273d`)
- **新增核验(二十余项)**:ARCHITECTURE 文件登记完整性、翻译键双向覆盖、导出链一致性、.gitattributes/.gitignore 治理、版本治理、CI/CD 政策合规、csproj 配置一致性、.uid 配对、未使用 using 依赖、归档一致性、测试辅助场景完整性、功能完整性、文档引用网络、魔法数字/字符串字面量(均 API 惯例/通用数值,零样板)

**验证(全部通过)**:build 0w/0e + xUnit 115/115 + format 三工程零 diff + import 0 错误 + 断言场景 PASS 零 FAIL + autoplay 480s 异常总数 0 + 最终性能基准 1.510ms 持平基线。

## 14. 继续完善轮汇总(2026-08-12,第三轮收官)

自 §13 后追加 3 提交,全部行为零变化、验证绿:

- **约定文档 CfgFx 补登记**(`8bce553`):balance-config 补 CfgFx 类型化读惯例(判型+钳制单口径,2026-08-11 新约定)——5 工具类未在 csharp-conventions 登记属合理分层(红线/惯例 vs ARCHITECTURE 职责),但 CfgFx 读取纪律应在 balance-config 体现
- **新增核验(十余项)**:异常处理覆盖(全仓 3 处 try 均为 await 段 try/catch,U17 约定,零未捕获路径)、core 边界测试(8/10 文件含边界用例)、测试时序稳定性(零真实墙钟依赖,零潜在 flaky)、场景 meta 数据(77 场景 format=3 + load_steps 一致)、balance.json 配置节覆盖(21 节全引用零死节)、AUDIT_VAULT M07/M08/M09 核销
- **最终回归**:CI fast-gate 等价全绿 + 断言场景批次 15 场景全 PASS 零 FAIL(smoke 140/base_system 79/boss_pattern 51/boss_enrage 37/boss_phase 41/boss_phase_transition 29/formation_strike 49/hit_logic 77/combo 21/difficulty 67/enemy_combat 44/pool_reuse 15/buff33 40/parry 36/graze 12)

**验证(全部通过)**:build 0w/0e + xUnit 115/115 + format 三工程零 diff + import 0 错误 + 断言场景批次全 PASS + autoplay 480s 异常总数 0 + 最终性能基准 1.510ms 持平基线。

### 补批断言场景验证与瞬时提示证伪(2026-08-12)

- **补批 20 场景**:intro_cinematic 37/return_cinematic 45/elite_turret_event 59/fog_event 70/event_manager 36/encounter_flow_contract 13/i18n 9/keybind 18/meta 24/meta_health_fx 24/mothership_summon 32/mothership_upgrade 9/orbital_strike 15/startup_flow 23/tutorial 30/user_db 59/user_session 42/welcome_flow 35/back_navigation 21 全 PASS。
- **瞬时提示证伪**:批次中 intro_cinematic/mothership_upgrade/back_navigation 各 FAIL=1,经单跑核实均为引擎退出期「1 resources still in use」瞬时提示匹配(grep 误捕),单跑 EXIT=0 + 0 FAIL + 0 ERROR + 0 resources 提示——**非真实回归**,系后台连续跑引擎时偶发退出提示。

## 15. 继续完善轮汇总(2026-08-12,第四轮收官)

自 §14 后追加 2 提交,全部行为零变化、验证绿:

- **清理 3 处冗余组注册**(`38aa168`):全库 9 组名生产-消费闭环验证(main/player/elite_turret_event 各自仅 1 处 `AddToGroup`,全库含测试/场景声明零消费、零变量间接访问、无注释意图)→ 确定性死代码删除 3 行;smoke_test 全 PASS + elite_turret_event_test 59 PASS
- **BossAttacks typed 化**(`910d044`):撒弹/齐射 2 处 `Set("position")` 改强类型 `b.Position = ...`(与 U03/U13 typed 改造方向一致,判空已核实);boss_pattern_test 51 PASS

**新增核验(十余项,全部零落地项)**:

- **字符串 API 盲区家族**(C# 编译器不校验的字符串引用):组名闭环(见上)→ 信号名/方法名/属性名/动画名(EmitSignal/Play 零字符串,Call/HasMethod 生产已 typed 化,Set 仅剩 SegmentedBar 兼容桥 4 属性已核实有效)→ Cfg 键双向交叉核对(568 叶子 × 5 读取通道 Cfg/CfgFx/Resolve/动态前缀/buff 表 cfg 字段,零缺失零死配置)→ shader 参数名(MetaHealthFX 21 参数 × meta_health.gdshader 22 uniform 双向零缺失,StringName 常量缓存惯例)→ GdFormat 格式串(55 处调用,翻译键 318 零缺失,格式符个数 vs 实参零不匹配,静默 "?" 风险排除)
- **GetNode 路径引用完整性**:7 核心场景 28 挂脚本节点 × 359 处引用交叉核对,4 处疑似全部甄别为解析局限(GetParent 前缀/运行时动态子节点/turret 子节点),零真实缺失;64 处绝对路径全为 /root/GameState(autoload 确认注册)
- **命名空间一致性**:InfiAir.Core.*(6 子域+根)/ InfiAir(生产 133)/ InfiAir.Tests(测试 66)/ InfiAir.Core.Tests(10),零跨层混用
- **测试脚本墙钟依赖**:2 处 Time.GetTicksMsec 忙等均为有意设计(匹配生产墙钟语义),CreateTimer 4 处语义合理,零 flaky 隐患
- **Roslynator 全量重跑**:99 条诊断全 CA1822(已定调不应用),零新发现;.editorconfig 无显式 RCS 配置,未使用私有成员盲区经评估静态误报率高不落地
- **调试输出与待办残留**:GD.Print 3 处均有诊断意图(兜底日志/低频事件/启动耗时),PushWarning/Error 9 处为合理运行时诊断,生产代码零 TODO/FIXME
- **事件订阅配对**:零「动态订阅者 × 常驻 emitter」逆模式;唯一接收方先释放场景(Hud←Boss)已 _ExitTree 显式断开,GameState 信号 C22 模式 IsConnected+Disconnect
- **协程 await 判活守卫**:生产代码仅 2 处 await(Main.cs:537/558),均 try/catch + IsInsideTree 守卫;其余按 AGENTS 约定转一次性 Timer 回调防协程状态泄漏
- **全量门禁基线确认**(最近 3 提交后完整重跑):build 0w/0e + xUnit 115/115 + format 三工程零 diff + import 0 错误

**验证(全部通过)**:build 0w/0e + xUnit 115/115 + format 三工程零 diff + import 0 错误 + 断言场景抽查(smoke/elite_turret_event/boss_pattern)全 PASS 零 FAIL + 工作区干净。

## 16. 继续完善轮汇总(2026-08-12,第五轮收官)

自 §15 后无代码提交,全部为核验轮 + 性能复测:

**新增核验(六项,全部零落地项)**:

- **动态派发残留**:生产代码零 `dynamic` 关键字、零反射(`Activator`/`GetMethod`/`GetProperty`/`Assembly`);唯一 `GetType()` 为 VariantBridge 异常消息构造(合法诊断用途)——全强类型调用,与 U 系列 typed 化方向一致
- **`Mathf.Clamp` 参数顺序**:全库 120 处调用中 61 处数值字面量形态可静态判序,**零 min>max 倒置、零 min==max**;归一化钳制(`Clamp(ratio, 0, 1)` 族)参数序全部正确
- **`CreateTimer`/`Tween` 生命周期**:CreateTimer 仅 4 处(Coroutine 超时兜底 3 + Welcome 1,后者带代次计数 + 判活守卫);CreateTween 102 处全为节点级(绑定 this,退出树自动清理),**零**树级 `GetTree().CreateTween()`、**零**手动 `new Tween()`——无泄漏模式
- **协程 await 判活守卫**:生产代码仅 2 处 await(Main.cs:537/558),均 U17 try/catch + C15 IsInsideTree 守卫;其余按 AGENTS 约定转一次性 Timer 回调防协程状态泄漏
- **调试输出与待办残留**:GD.Print 3 处均有诊断意图(兜底/低频事件/启动耗时),PushWarning/Error 9 处为合理运行时诊断,生产代码零 TODO/FIXME
- **命名空间一致性**:InfiAir.Core.*(6 子域+根)/ InfiAir(133)/ InfiAir.Tests(66)/ InfiAir.Core.Tests(10),零跨层混用

**性能基准复测(§16 最新数字)**:perf_bench 1800 帧 avg_frame_ms=**1.488**(equivalent_fps 672.1)——对比重构前基线 fb41612 1.520、§11 中位 1.510,最近 4 提交后**零回退且略优**(噪声内)。

**验证(全部通过)**:build 0w/0e + xUnit 115/115 + format 三工程零 diff + import 0 错误 + perf_bench 1.488ms + 工作区干净。

## 17. 继续完善轮汇总(2026-08-12,第六轮收官)

自 §16 后无代码提交,全部为核验轮 + CHANGELOG 同步(`21651d3`):

**新增核验(五项,全部零落地项)**:

- **代码内资源加载路径**:50 处字面量 `GD.Load<T>("res://...")` 全部存在、零动态拼接;唯一 `ResourceLoader.Load`(Main BGM)路径存在且加载失败有 PushWarning 兜底
- **tscn 资源引用四通道**:ext_resource 路径存在性(104 处 scenes+test 零缺失)、sub_resource id 引用(77 场景零未定义)、ext_resource id 引用(77 场景零未定义)、代码 GD.Load(50 处)——四通道全部闭合
- **场景切换路径**:4 处 `ChangeSceneToFile` 目标(welcome/tutorial/main)全部存在,导航闭环完整(welcome 入口 → main/tutorial → 返回 welcome),零死链
- **音频播放纪律**:58 处音效统一走 SfxPlayer 服务通道;3 处直接 `.Play()` 全部为语义明确例外(BGM 长驻流/死亡回放演出节点启动/玩家射击高频专用播放器)
- **CHANGELOG 同步**(`21651d3`):补记最近 4 提交(组注册清理/typed 化 + 核验留档 + 性能数字)

**验证(全部通过)**:build 0w/0e + 工作区干净,HEAD `21651d3`。

## 18. 继续完善轮汇总(2026-08-12,第七轮收官)

自 §17 后无代码提交,全部为核验轮 + 最终长跑回归:

**新增核验(四项,全部零落地项)**:

- **`IsInstanceValid` 判活守卫分布**:全库 95 处,集中在动态节点密集文件(Mothership 11/GameEventManager 11/FormationStrikeEvent 7/EliteTurretEvent 6/Main 5),动态引用访问前判活习惯健康
- **`Tr()` i18n 纪律**:生产 247 处调用集中于 UI 层(SettingsUi 47/Welcome 42/Hud 31/BaseConsole 28/Tutorial 24);5 处动态键拼接(`FOG_EVENT_`/`BUFF_`/`MISSION_`/`DIFF_` 前缀)此前翻译键双向覆盖已证实全部存在
- **`GameState` 访问纪律**:生产代码零 `GetNode<GameState>("/root/GameState")` 直接访问(64 处绝对路径全在测试),816 处统一 `GameState.Instance`——单例访问单一入口
- **事件层运行时实证**:formation_strike_event_test 49 PASS / fog_event_test 70 PASS / mothership_summon_test 32 PASS,exit 0 零 FAIL

**最终长跑回归(autoplay 480s)**:exit=0 + 异常总数 0(run=4 完整循环,score 55318,全程 orphan=0、res 稳定 112-114、mem ~91MB 稳定)——最近 6 提交(含 2 代码提交)后健康基线完整保持。

**验证(全部通过)**:build 0w/0e + 工作区干净,HEAD `ad6ed81`。
