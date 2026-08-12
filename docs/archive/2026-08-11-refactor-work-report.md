# InfiAir 业务逻辑重构工作汇报(2026-08-11)

> 本轮工作以「SOLID + 空间换时间」为原则,对游戏业务逻辑进行多轮重构与分支化策略实验。
> 范围:热路径缓存收敛、样板去重、双源数据清理、上帝类拆域试点、类型化读配置全量迁移。
> 全部改动行为零变化(防御性增强与纯重构,无运行时数值/语义变更);每阶段完整验证后提交。

## 1. 工作概览

| 阶段 | 内容 | 结果 |
|---|---|---|
| 基线三轮(合并至 main `164b3a7`) | 帧缓存收敛 + OCP 注册表化 / 样板去重 + 双源清理 / MetaService 拆域 + CfgFx 批 1 | 全量验证通过,36 文件 +948/−880 |
| 四分支实验 | cfgfx / services / hotpath / dualsource 四条独立策略分支并行 | 全部验证全绿 |
| 合并决策 | **opt-cfgfx 评为效果最优**,合并至 main(`36b45c0`) | 合并后全量验证通过 |
| 本报告 | 改进说明与分支对比留档 | — |

## 2. 基线三轮重构(2026-08-11,已合并)

### 第一轮:热路径空间换时间 + OCP

- **`FrameCache` 每物理帧共享缓存**:10 处同构「每帧 view/player 缓存」样板(Enemy/Boss/EnrageSequence/BossAttacks/TurretBattery/AimFrameLayer/Bullet/FormationStrikeEvent/StrikeCarrier/FormationBomb)收敛为 1 个静态类,全场上百实体/子弹的原生查询从每帧 N 次 → 1 次;消灭 `CachedPlayer()` 的 Variant 包拆纯负开销(Enemy.cs:754 每帧每敌机双调用)。遵守 U07:静态缓存仅持纯值类型 `Rect2`。
- **OCP 注册表化**:`Enemy.MakeStrategy()` 7 连 if 字符串分支 → 静态工厂注册表;敌弹「弹种→速度/伤害/数量」双处 if/else → 单一 `BulletSpec()` 映射。
- **调研依据**:Godot 官方性能指南(预计算/缓存=空间换时间)、Godot 社区高热度架构帖(名字→函数映射表)、godot-proposals #7842(零分配 API 共识)。

### 第二轮:样板收敛 + 双源数据去重

- **`FlashFx.Hit/Update`**:4 处受击闪白同构收敛(恢复色/时长逐参注入,Boss 狂暴态 `BaseModulate()` 与 HitFlashByType 表驱动时长保留)。
- **`BuffBoolCache`**(id 驱动、信号事件驱动、零闭包):3 处 buff 布尔缓存收敛;Enemy 白盒桥 `_on_buffs_changed`/`_slow_field_on` 与池化重连保留(PoolReuseTest 契约不破)。
- **`EnemyFx.RampCollisionDamage`**:体碰伤害公式两处收敛。
- **双源去重**:`MILESTONE_BASE` 三处重复单源化(删零消费方死属性);`BaseConsole.RouteBuffNames` 冗余表删除(改通用翻译键约定)。
- **防漂移断言**:Buff33Test 1d 卡池成员资格双向校验(兜住「json 加新 buff 但池漏登记」)。

### 第三轮:GameState 拆域试点 + CfgFx 批 1

- **`MetaService`(上帝类拆分试点)**:局外成长域 232 行迁出为新组合服务(RefCounted,构造注入 UserDB);GameState.Meta.cs 降为门面转发;信号改「服务 C# 事件 + GameState 订阅重发」模式,发射点/次数/顺序逐位一致。验证了后续拆域的完整模式。
- **`CfgFx.Float/Int` 批 1**:类型化读配置工具(AC4 判型口径统一 + 集中钳制),TurretBattery 12 处首批迁移。
- **难度全表一致性断言**:DifficultyTest 24 值全表比对(A7 白盒桥)。

## 3. 四分支策略实验(并行,各分支独立验证)

| 分支 | 优化策略 | 提交 | 变更规模 | 核心成果 |
|---|---|---|---|---|
| `feature/opt-cfgfx` | 类型化读配置**全量迁移** | 5 | **197 处** Cfg 调用迁移 | 判型双口径(直读 vs VariantType 判型)统一为 AC4 单口径;钳制逻辑集中;CfgFx 覆盖 FormationStrikeEvent 15 / Enemy 21 / Player 46 / Boss 115 处;Boss `_Ready` 126 处数值铺陈抽取 `LoadBalance()`;**BALANCE_MAP 生成器扩展**(追踪 CfgFx.Float/Int,修复批 1 后 ~200 键误报未引用的盲区,未引用键回基线 1);SlowFieldFactor 无钳 vs Enemy 钳 [0,1] 差异显式决策留档 |
| `feature/opt-services` | 上帝类拆域推进 | 1 | +373/−236 | **`MissionsService`**:RP/任务/路线域(状态 Rp/RefreshPoints/Missions/ChosenRoutes/LockedRoutes + 14 方法)迁出 GameState;GameState.Missions.cs 降为门面转发(19 公开成员);4 组信号(RpChanged/MissionCompleted/RefreshPointsChanged/RouteChosen+BuffsChanged)C# 事件 + 门面重发,保序逐位一致;存档/ResetRun 直接赋值路径不破 |
| `feature/opt-hotpath` | 热路径微优化 | 2 | ~20 行 | MetaHealthFX 每帧必用 3 键(`smooth_down_tau`/`smooth_up_tau`/`crack_exponent`)字典查找字段化(每帧 4 次 → 0);GameState `_Process` 与 Settings 的 `(int)Mathf.Floor(RunTime/step)` 简化为 `(int)(…)`(RunTime 恒 ≥0,截断等价 floor,省每帧原生调用);其余残点自查记录不做 |
| `feature/opt-dualsource` | 双源防漂移断言 | 1 | +64 测试 | Buff33Test 1e:Player cap 效果表默认值 vs json `max_stacks` vs 卡池 `max` 三方一致性断言(补齐 1d 只覆盖两方的缺口);其余双源点核实:路线名/里程碑已单源跳过,机型表已被 gen_balance_map 键级对照覆盖跳过 |

## 4. 效果对比与合并决策

| 维度 | opt-cfgfx | opt-services | opt-hotpath | opt-dualsource |
|---|---|---|---|---|
| 迁移/改动规模 | 197 处(最大) | 1 域服务化 | ~20 行 | 测试 64 行 |
| 结构改进 | 判型口径统一 + 钳制集中 + 铺陈消解 + 工具修复 | 上帝类再减一域 | 每帧 4 次字典查询→0 | 防漂移覆盖补全 |
| 系统性 | 全量(4 文件 + 工具) | 单域 | 单点 | 单点 |
| 验证 | 每批全绿 + 机械核对 115/115 零 mismatch | 全绿 | 全绿 | 全绿 |
| 风险 | 中(已核销) | 中 | 极低 | 极低 |

**判定:opt-cfgfx 为效果最优**——规模最大、系统性最强(终结 Cfg 直读/判型双口径并存,钳制逻辑单点集中,126 处 Boss 铺陈消解),并产出 BALANCE_MAP 工具改进副产品;197 处迁移经脚本逐条机械核对(min/max 零 mismatch)。**已合并至 main**(fast-forward 至 `36b45c0`),合并后全量验证通过:build 零警告、xUnit 115/115、format 三工程零 diff、import 0 错误、main smoke 300 帧、enemy_combat/boss_pattern/formation_strike_event/hit_logic 场景 exit 0。

**其余分支保留待定**(不冲突、可后续合并或参考):`feature/opt-services`(上帝类拆域第 2 域,建议下一轮合入)、`feature/opt-hotpath`(小而稳,建议合入)、`feature/opt-dualsource`(防漂移,建议合入)。

## 5. 验证汇总(全部通过)

- `dotnet build InfiAir.csproj` 零警告(warnings-as-errors)
- `dotnet test tests-csharp/` 115/115
- `dotnet format` 三工程 `--verify-no-changes` 零 diff
- `godot --headless --import` 0 错误(新 .cs 的 .cs.uid 全部入库)
- main smoke 300 帧、断言场景批次(716 + 527 + 四分支各自场景)PASS 零 FAIL
- autoplay 480s 探针 exit 0 且异常总数 0(三轮基线各自一次)
- BALANCE_MAP 生成器重跑零 diff(含 CfgFx 追踪扩展)

## 6. 后续建议

1. 合入 `feature/opt-services`/`opt-hotpath`/`opt-dualsource` 三个保留分支(互补、非冲突)。
2. GameState 拆域继续推进:ScoreService + RunProgressionService(需先解 Save.cs:227-228 与 Settings.cs AddBossKill 两个跨域写)、CombatStateService(健康/Buff,顺手修正 Settings.cs 命名失实)、SettingsService(机械量大,收尾)。
3. Save.cs 整块不拆(全状态聚合恢复的天然上帝方法),只做 `ApplySaved(data)` 收敛。
4. 双源单源化(buffs/enemies/difficulty)被「损坏回退默认」约束否决,防漂移走断言(已覆盖主要项)。

## 7. 第五轮拆域进展(2026-08-11,续)

后续「继续推进 GameState 拆域(ScoreService 等)」已完成:

| 阶段 | 提交 | 内容 |
|---|---|---|
| 前置合并 | `fb071cb` | feature/opt-services(MissionsService)并入 main,无冲突 |
| 阶段 1 | `0843a2e` + `370665a` | **ScoreService**(计分+里程碑)+ **RunProgressionService**(难度+曲线)同期抽取:跨域写解耦(`RestoreMilestones` 收拢 Save.cs 直写、AddBossKill 编排化)、`Tick(delta)` 接管 _Process 计时段、5 信号事件化重发保序 |
| 阶段 1 尾 | `107be45` | BALANCE_MAP 行号漂移 24 行同步 |
| 阶段 2 | `ae2eb10` | **CombatStateService**(健康+Buff):Settings.cs C 簇迁出,回归「设置+视图」单一职责;HealthChanged/BuffsChanged 本域事件化,6 处 BuffsChanged 发射点无双发核对 |
| 阶段 2 尾 | `ab856ef` | BALANCE_MAP 行号漂移 18 行同步 |

**累计效果**:GameState(3216 行/10 partial)已拆 5 域服务(Meta/Missions/Score/RunProgression/Combat),门面转发 + 信号重发模式成熟;唯一 autoload 约束保持;66 场景测试经门面零改动。验证:每阶段 build 0 警告 + xUnit 115/115 + format 三工程零 diff + import 0 错误 + smoke 300 帧 + 11 断言场景 PASS 零 FAIL;autoplay 480s 探针 exit 0。剩余:SettingsService(设置+视图,收尾轮)、InputBindingsService、UserSession。

## 8. 第六轮拆域收官(2026-08-11,续)

「SettingsService 收尾轮 + 合并剩余保留分支」已完成:

| 项 | 提交 | 内容 |
|---|---|---|
| 合并 opt-dualsource | `75607aa` 前序 | Buff33Test 1e cap 三方一致性断言,干净合并 |
| 合并 opt-hotpath | `75607aa` | MetaHealthFX 3 键字段化(每帧 4 次字典查找→0)+ Floor 除法简化——Floor 目标代码已随第五轮迁入 RunProgressionService,重定位至 Tick/RecomputeDifficultyInternal 两处 |
| SettingsService 服务体 | `cd1d003` | 设置 setter 簇 + 视图簇(ViewWorldRect 物理帧缓存逐字搬迁)+ 状态字段 11 项迁入;ApplySettingsDict/CollectSettingsDict 持久化桥自 Save.cs 迁入 |
| 门面转发 + 信号重发 | `eedb6a5` | GameState.Settings.cs/State.cs 门面转发;Save.cs/Users.cs 桥委托;8 设置信号事件化重发保序 |
| BALANCE_MAP | `3f7e618` | 行号漂移 44 行同步 |

**GameState 上帝类拆域收官**:3216 行/10 partial → 6 域服务全部迁出(Meta/Missions/Score/RunProgression/Combat/Settings),GameState 收敛为编排门面(组合持有 6 服务 + 信号重发 + Cfg 中心 + 实体注册表)。唯一 autoload 约束保持;66 场景测试经门面零改动;每阶段 build 0 警告 + xUnit 115/115 + format 零 diff + import 0 错误 + 设置域场景 exit 0;autoplay 480s 探针 exit 0 异常总数 0。
