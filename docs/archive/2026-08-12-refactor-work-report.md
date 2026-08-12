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
