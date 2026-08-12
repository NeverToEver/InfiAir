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
