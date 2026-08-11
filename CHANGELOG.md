# Changelog

本项目版本变更记录。格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)。版本号为 MAJOR.MINOR 递增（项目惯例，非完整 SemVer），版本同步点见 `release.sh` 与 `project.godot` `config/version`。**早期版本（≤ 3.22）变更细节见 `git log`**。版本 3.23 无发布记录（git 历史未见对应 tag/条目，疑似有意跳号，2026-08-06 审计登记）。

## [Unreleased]

### 玩法（2026-08-11，得分/奖励设计审核——击杀连击 + 低血防御保底，`docs/archive/2026-08-11-score-combo-buff-pity-plan.md`）

- **击杀连击计分**（怒首领蜂/虫姬链式得分的温和版）：3s 窗口内连续击杀 → 击杀分 × 连击乘区（第 1 杀 ×1.0 起，每连 +0.1，封顶 ×2.0）；超时或受击断连（受击与 DDA 同源，构成「降档+断连」双通道但均不致命）；Boss 击杀/事件奖励/擦弹不计连击；HUD 新增连击标签（`UI_COMBO_FMT`）。新键 `scoring.combo.*`；新断言场景 `combo_test`
- **低血防御保底**：HP < 50% 时三张 Buff 候选加权偏向防御（extra_life/regen/armor/shield/evasion ×2）且保底至少 1 张防御卡（满血行为不变；防御满层自然失效）。新键 `buffs.dynamic_weight.*`；buff33_test 新增保底断言
- **验证**：`dotnet build` 零警告 + xUnit 111/111 + `dotnet format` 三工程零 diff + 新键 BALANCE_MAP 重跑收录 + 断言场景全量 0 FAIL（含 formation 击坠分契约更新 2000→2240）+ autoplay 探针

### 重构（2026-08-11，SOLID + 空间换时间——帧缓存收敛 + 类型分支注册表化）

- **新增 `FrameCache` 共享帧缓存**（空间换时间）：全场上百实体/子弹每物理帧共享一次 `view_world_rect` 查询，替代 10 处同构样板（Enemy/Boss/EnrageSequence/BossAttacks/TurretBattery/AimFrameLayer/Bullet/FormationStrikeEvent/StrikeCarrier/FormationBomb 各自持有 `_frame/_frameView/_framePlayer` + `CachedView()/CachedPlayer()` 私有缓存）；`CachedPlayer()` 原将 EntityManager O(1) typed 属性包成 `Variant` 再拆包（纯负优化，Enemy.cs:754 每帧每敌机双调用）一并消灭——player 改直读 `GameState.Instance.PlayerRef`，不缓存不装箱，遵守 U07「静态缓存不持 Godot 对象引用」约束
- **OCP 注册表化**：`Enemy.MakeStrategy()` 7 连 if 字符串分支 → 静态工厂注册表（新增移动策略只注册一行，不再改工厂本体，对齐 BossAttacks 攻击注册表既有口径）；敌弹「弹种→速度/伤害/数量」双处 if/else 分支合并为单一 `BulletSpec()` 映射（新增弹种只改一处）
- **验证**：`dotnet build` 零警告 + xUnit 115/115 + `dotnet format` 三工程零 diff + import 0 错误 + main smoke 300 帧 + 断言场景 6 个 317 项 PASS 零 FAIL（enemy_combat/boss_pattern/boss_enrage/formation_strike_event/elite_turret_event/hit_logic）+ autoplay 探针 exit 0（仅既有的分裂者 flushing queries 与 dda_stuck 探针边界两项登记不修，AUDIT_VAULT.md:1794 基线对照）

### 重构（2026-08-11，第二轮：热路径空间换时间 + 样板收敛 + 双源去重，全量行为零变化）

- **热路径空间换时间 4 项**：`FrameCache.DdaFactor()` 每物理帧一次共享缓存（原每敌机每帧 `d / GameState.Instance.DdaFactor()` 方法调用+分支，改为 `d / FrameCache.DdaFactor()`——缓存因子非逆因子，除法逐位等价）；Player `_PhysicsProcess` 帧首单次 `Time.GetTicksMsec()` 供两处视觉更新复用（同帧时钟自洽）；Boss `SummonerTypes` 字典查询字段化 `_isSummoner`（Setup 固化，直实例化默认语义逐位保留）；BossMovement `_movers` 委托类型戳缓存（非法类型回退一型语义逐位保留）
- **样板收敛 3 项**：受击闪白 4 处同构（Enemy/Boss/TurretBattery/FormationCraft）收敛为新静态工具 `FlashFx.Hit/Update`（恢复色/时长逐参注入，Boss 狂暴态 `BaseModulate()` 实时取色与 HitFlashByType 表驱动时长保留）；体碰伤害公式提取 `EnemyFx.RampCollisionDamage`（Enemy/Boss 两处）；buff 布尔缓存 3 处同构（Enemy/Boss slow_field、LaserWeapon laser_beam）收敛为新 `BuffBoolCache`（id 驱动、信号事件驱动、零闭包；Enemy 白盒桥 `_on_buffs_changed`/`_slow_field_on` 与池化重连保留，PoolReuseTest 契约不破）
- **双源数据去重 2 项**：`MILESTONE_BASE` 三处重复单源化（删零消费方的 Constants 死属性，State 初始化收敛到 `BuildMilestoneBase()`/`MilestoneCycleMultValue`）；`BaseConsole.RouteBuffNames` 冗余表删除（改通用翻译键约定 `BUFF_{ID}_NAME`，与 BuffSelect.MakeCard 同构）
- **防漂移断言**：Buff33Test 新增 1d 块——卡池 id 集合 == balance.json `buffs` 键集（双向 diff）+ json 声明 `max_stacks` 与池值一致性（未声明者以池缺省为权威），兜住「json 加新 buff 但卡池漏登记」的成员漂移
- **验证**：`dotnet build` 零警告 + xUnit 115/115 + `dotnet format` 三工程零 diff + import 0 错误 + main smoke 300 帧 + 断言场景 14 个 716 项 PASS 零 FAIL（hit_logic/enemy_combat/boss_pattern/boss_enrage/elite_turret_event/formation_strike_event/pool_reuse/parry/graze/base_system/base_task_refresh/boss_registry/buff33/smoke）+ BALANCE_MAP 生成器重跑（Cfg 调用行号漂移 216 行同步）+ autoplay 探针 480s exit 0 **异常总数 0（0 类）**（较上轮既有的分裂者 flushing queries/dda_stuck 两项登记警告一并消失；boss_registry_test 的退出期 RID leak 经基线 worktree 对照确认为既有、CI 错误扫描口径不含该模式）

### 架构（2026-08-11，第三轮：GameState 拆域试点 + CfgFx 批 1 + 一致性断言，全量行为零变化）

- **GameState 上帝类拆分试点——`MetaService`**（3216 行/10 partial 上帝类第一步实体化）：局外成长域全部职责（科技点结算/升级消费/开局 buff 预置，原 GameState.Meta.cs 232 行）迁入新组合服务 `MetaService : RefCounted`（构造注入 `UserDB`，对齐 BalanceService/SaveManager/EntityManager 服务先例）；GameState.Meta.cs 降为门面 partial（公开成员逐一转发、私有 LoadMeta/LoadMetaConfig 一行包装，内部调用方与 66 场景测试零改动）；信号改「服务内 C# 事件 + GameState 订阅重发」模式——`TechPointsChanged` 3 个发射点/次数/顺序与现状逐位一致（ResearchLab 消费方零改动）；唯一 autoload 约束保持
- **`CfgFx` 类型化读配置批 1**：新静态工具 `CfgFx.Float/Int(path, def, min, max)` 统一 AC4 判型口径 + 集中钳制逻辑（PathResolver 坏类型回退为第一道防线，判型为第二道）；TurretBattery `_Ready` 12 处 Cfg 调用首批迁移（全部无钳制调用 → 默认恒等钳制，min/max 逐一对账零新增/删除）；后续批次（FormationStrikeEvent/Enemy/Player/Boss 共 265 处）逐批推进
- **difficulty 全表一致性断言**：DifficultyTest 新增 9 号断言块——balance.json `difficulty` 节 24 值（easy/medium/hard × 8 键）vs C# 内建默认表全表比对（A7 白盒桥 `BuildDifficultyDefsPublic()`，规避 DIFFICULTY_DEFS 被 ApplyBalance 整表替换后的自比恒等陷阱），防「调参层 vs 回退默认」漂移
- **验证**：`dotnet build` 零警告 + xUnit 115/115 + `dotnet format` 三工程零 diff + import 0 错误 + main smoke 300 帧 + 断言场景 8 个 527 项 PASS 零 FAIL（meta/user_session/difficulty/buff33/elite_turret_event/hit_logic/base_system/smoke）+ BALANCE_MAP 生成器重跑（MetaService/CfgFx/TurretBattery 调用点行号同步）+ autoplay 探针

### 架构（2026-08-11，第四轮：GameState 拆域推进——MissionsService，全量行为零变化）

- **GameState 上帝类拆分推进——`MissionsService`**：RP 经济/基地任务/天赋路线域全部职责（原 GameState.Missions.cs 261 行：状态 Rp/RefreshPoints/Missions/ChosenRoutes/LockedRoutes 与 _taskPool/_missionsByKind，方法 AddRp/SpendRp/InitMissions/ResetMissions/SetKindProgress/ActiveMissionIds/MissionGoal/MissionProgress/IsMissionDone/IsMissionClaimed/ClaimMission/GrantRefreshPoints/CanRefreshMissions/RefreshMissions/ChooseRoute/IsBuffLocked 等 19 项）迁入新组合服务 `MissionsService : RefCounted`（无构造依赖，跨域访问统一经 `GameState.Instance`——MISSION_DEFS/MISSION_POOL/ROUTE_LINES/REFRESH_COST/GRANT_PER_VISIT/BuffCount/Buffs）；GameState.Missions.cs 降为门面 partial（公开成员逐一转发 + 私有 InitMissions/SetKindProgress/MissionDef 一行包装，内部调用方与 66 场景测试零改动）；状态字段与 RpMissionRewardValue 随迁出 State.cs，配置常量保留 GameState 侧；信号改「服务内 C# 事件 + GameState 订阅重发」模式——`RpChanged`/`MissionCompleted`/`RefreshPointsChanged`/`RouteChosen` 4 组发射点/次数/顺序与现状逐位一致（存档恢复/ResetRun 直接赋值路径由 GameState 侧直发同名信号不重复；ChooseRoute 直发 `BuffsChanged` 同 MetaService.ApplyMetaLoadout 口径）；唯一 autoload 约束保持
- **验证**：`dotnet build` 零警告 + xUnit 115/115 + `dotnet format` 三工程零 diff + import 0 错误 + main smoke 300 帧 + base_system_test/base_task_refresh_test/smoke_test 场景 exit 0 零 FAIL + BALANCE_MAP 生成器重跑零 diff

### 架构（2026-08-11，第五轮：GameState 拆域推进——计分/难度/健康三域服务化，全量行为零变化）

- **ScoreService**（计分+里程碑域）：Score/Kills/BossKills/Combo/连击计时/里程碑态迁入新服务；跨域写解耦——`RestoreMilestones(score)` 收拢 Save.cs 直写 `_milestoneCount/_nextMilestone`（含 H03 挂死守卫）；`Tick(delta)` 接管连击断连计时；ScoreChanged/MilestoneReached/ComboChanged 三信号 C# 事件 + 门面重发保序
- **RunProgressionService**（难度+曲线域）：GameState.Difficulty.cs 全量迁入（档位/DDA/倍率惰性缓存/ramp 转发/里程碑曲线/被动回血缓存）；`Tick(delta)` 接管难度时间档重算与 DDA 计时；DifficultyChanged/DifficultySelected 重发保序；`SetMilestoneOverride/SetMilestoneCount` 等计分域钩子归 ScoreService（公开 API 语义不变）
- **CombatStateService**（健康+Buff 域）：Health/Buffs/MaxHealth/LoseHealth/Heal/TryLifesteal/BuffCount/AddBuff/ConsumeBuff 迁出 Settings.cs C 簇（顺手修正 Settings.cs 命名失实，回归「设置+视图」单一职责）；HealthChanged/BuffsChanged 本域发射点事件化，6 处 BuffsChanged 发射点经核对无双发（其余 4 处保持原位直发）；PlayerDied 经 GameState.Instance 直发（MissionsService.ChooseRoute 同款先例）
- **累计**：GameState 3216 行上帝类已拆 5 域（Meta/Missions/Score/RunProgression/Combat），剩余设置+视图（SettingsService）、键位、会话待续
- **验证**：`dotnet build` 零警告 + xUnit 115/115 + `dotnet format` 三工程零 diff + import 0 错误 + main smoke 300 帧 + 断言场景 11 个 PASS 零 FAIL（combo/difficulty/mothership_upgrade/progression_interop/hit_logic/buff33/meta/user_session/base_system/base_task_refresh/smoke）+ BALANCE_MAP 重跑同步（行号漂移 24+18 行）+ autoplay 探针

## [3.28] - 2026-08-10

### 规范化 + 逻辑修复（2026-08-10，AA 系列第七轮审查，`docs/AUDIT_VAULT.md` 登记）

- **逻辑漏洞 23 处**：里程碑 `cycle_mult` ∈ (0,1) 阈值级数收敛 → `AddScore` 主线程死循环（钳制下限 1.0 + 迭代上限兜底，H03 防挂死闭环）；meta 预置 buff 补发 `BuffsChanged`（已购升级新局即生效、HUD 即显）；教程阶段 0 训练靶寿命离场补刷兜底（新手超时软锁修复）；`SaveStore` 重复键 JSON 抛 `ArgumentException` 击穿损坏隔离（dotnet/runtime#71784 规避）；`UserDb.DeleteUser` 落盘失败内存回滚；Welcome 用户名下拉鼠标点选整条路径失效修复（FocusExited 抢焦时序）；Boss `release_hold_duration` / 撤退航母速度 / `boss_score_step` 等配置键下限钳制补齐；弹反扇形过滤基准改机头方向；`AimFrameLayer` 磁吸框沿距负分量修正；存档统计 `SaveInt` 判型钳制；`GdFormat` `%.Nf` 精度守卫等
- **规范化 36 处**（Roslynator 静态分析安全子集）：`TryGetValue` 双查消除 ×4、`AsSpan` ×2、char 重载 ×1、`JsonSerializerOptions` 静态缓存、`BuffIcons` 常量数组 → static readonly ×13（热路径零分配对齐）、私有方法返回类型收窄 ×15
- **测试与数据**：xUnit 108→111（重复键隔离 / 精度溢出 / 删除回滚三回归）；`BaseTaskRefreshTest` Q05 LINQ `Append` 误用修复（任务排除语义恢复，CA1806 命中真实缺陷）；`enemies.fire_interval` 死键清理 + BALANCE_MAP 重跑
- **验证**：`dotnet build` 零警告 + xUnit 111/111 + `dotnet format` 三工程零 diff + 断言场景全量 0 FAIL + autoplay 探针

### 文档（2026-08-10，归档 + 精简）

- 过时评审/决策文档归档：`C_SHARP_ASSESSMENT.md`（M7 全量迁移落地，使命完结）与 `PARITY_REVIEW_2026.md`（一次性对齐评审）→ `docs/archive/`（EXECUTION_LOG 登记，ROADMAP/ARCHITECTURE/csharp-conventions 引用同步）
- 工作指引精简：`AGENTS.md` 与 `.agents/csharp-conventions.md` 去迁移期历史叙述（操作性约定全量保留；历史见 git 与 AUDIT_VAULT）

### 美工 + 玩法（2026-08-10，HUD 美化 + 弹反盾全周化）

- **对局 HUD 美化**：左下状态区四仪表（HP/燃料/冲刺/弹反）重排分行消除重叠；角落小标签（分数/生命/难度）下移为骑跨背板的页签式，不再被屏幕边缘裁切；Boss 血条新增切角括号背板（名牌 + 三段血条整体入框、随血条显隐）；buff 坞与「增益 [L]」标签内收 20px 安全边距；Buff 三选一加标题金下划线与「← → 选择 · Enter 确认」提示行（新 i18n 键 `BUFF_HINT`）
- **弹反盾视觉重做**：平涂金扇改三层结构——14 格分段能量盾缘（ADD 辉光、ACTIVE 能量脉动）+ 淡金 ADD 光罩 + 收敛的珍珠流光；弹反命中 0.18s 白金色提亮 + 边缘外扩脉冲（`SetParryFlash`）
- **弹反盾改 360° 全周**（判定 + 视觉，`player.parry.arc_deg` 140→360；配置保留 <360 扇形能力）；新增激活「金光一闪」动画——白金圆环 0.45×→1.5× 缓出扩张 + 淡出 0.32s（`SetParryActivatePulse`，进入 ACTIVE 瞬间触发）；`parry_test` 扇区外用例改写为全周语义，DESIGN_BASELINE/ARCHITECTURE/BALANCE_MAP 同步
- **验证**：`dotnet build` 零警告 + xUnit 108/108 + `dotnet format` 零 diff + parry/graze/smoke/balance/boss 断言场景 0 FAIL + 窗口截图逐张核对（HUD 常态/压力态、三选一、Boss 背板、全周盾三相位）

### 架构（2026-08-08，M7 全量迁移 C#——零 GDScript）
- **全量迁移落地**（M1 脚手架 → M7d 收官，逐里程碑提交）：全部运行时代码 GDScript → C#（Godot 4.6.2 .NET 版 + .NET 8）——M2 服务层、M3 战斗核心（子弹生态/敌机体系/玩家 + 8 组件/Boss + 5 组件）、M4/M5 事件体系与 UI 层、M6 演出编排层、M7 GameState autoload 与全部场景测试；`scripts/*.gd` 与 `autoload/` 全量退役（`scripts/` 仅存 `tools/*.py` 离线工具），代码重组为 `csharp/godot/`（Godot 绑定壳）+ `csharp/core/`（纯 .NET 类库）+ `tests-csharp/`（xUnit）三工程
- **M7d 收口**：`GameStateBridge`/snake_case 桥删除，C# 侧统一 `GameState.Instance` typed 访问；CI 新增零 GDScript 门禁（任何 `.gd` 文件即失败，防回归）；gdtoolkit（gdformat/gdlint）门禁随迁移退役，跨语言混编边界规则随之消解（单一语言；`csharp/godot/*Interop.cs` 保留为 `InfiAir.Core` 绑定端点）
- **验证**：`dotnet build` 零警告 + xUnit 全绿 + 断言场景回归全绿（权威计数见 `docs/TESTING.md`）+ autoplay 探针通过

### 修复（2026-08-09，启动器探测链 .NET 对齐）

- `run.command`/`run.bat` 引擎探测链改 .NET 版优先（`godot-mono` → `Godot_mono.app` / `Godot_v4*mono*.exe` 优先命中，对齐 `run.sh`）：M7 全量 C# 后标准版引擎无法构建项目；修正 `run.bat` 头部「标准版，无需 .NET」失实说明；`.agents/shell-scripts.md` 同步

### 重构（2026-08-09，M7 残留兼容桥清理）

- 全仓使用面审计（直接调用/动态派发/tscn 连接/裸调用四通道）后删除约 400 个零调用 GDScript 兼容桥成员与转发器（snake_case 方法/属性、UPPER_SNAKE 别名、下划线别名、Boss 死转发器、死私有辅助 `ToFloatArray`）；保留桥横幅统一改写为「M7 后保留」口径，空桥段连横幅移除；`DawnStation` 内部引用归位 PascalCase 主名
- 类头/桥段注释全量清扫（Player/Mothership/EliteTurretEvent/GameEvent/IntroCinematic/WarpGate/ReturnCinematic 头注、TurretBattery `SegmentedBar` 误注、DawnStation 枚举注释错位、`.editorconfig` 头部 gdtoolkit 治理残留）
- **验证**：`dotnet build` 零警告 + xUnit 75/75 + `dotnet format` 三工程零 diff + 断言场景回归全绿（权威计数见 `docs/TESTING.md`）+ 引擎错误日志扫描零命中

### 修复（2026-08-09，U 系列 C# 全量审计，AUDIT_VAULT U 系列登记）

- **B1-B5 处置**：信号配对/静态 Godot 资源持活/损坏数据防御/热路径/动态派发 typed 化/桥清理/死代码；另清理 M7d 误提交的 30 个迁移中间版本孤儿文件（无扩展名 .cs 副本）

### 规范化（2026-08-09，dotnet format 全量 + 防回归闸）

- `dotnet format` 三工程（`InfiAir.csproj`/`csharp/core/InfiAir.Core.csproj`/`tests-csharp/InfiAir.Core.Tests.csproj`）全量规范化（whitespace/imports 排序，16 文件纯格式，构建 0 警告 + 单测 75/75）；CI 新增 `dotnet format` 三工程 verify 零 diff 防回归闸

### 美工（2026-08-09，月蚀视觉）

- **4 型 Boss「月蚀」专属贴图与视觉重制**：`generate_enemy_sprites.py` 扩展（敌机生成器输出 12→13 文件），随后重制为「月食之轮」独有设计（暗月盘 + 双正交轨道环刃：剪影可读/攻击器官可视化/焦点引导，与 3 型直线舰船一眼区分）；`docs/BOSS_REDESIGN.md` 同步
- **素材生成统一入口**：`scripts/tools/regenerate_all.sh`（一键重生成 15 贴图 + 11 音频，解释器探测，幂等）+ `scripts/tools/README.md`

### 修复（2026-08-09，V 系列多角度审查批次 1-3）

- **批次 1**：静态 Godot 资源持活/UserDb 溢出回归/Explosion 双计数/BaseConsole 重复 AddChild/死亡回放续局/死测试修复
- **批次 2**：遭遇组热路径缓存/空 StringName 复用 + Boss 发射链 typed 化（20 处动态派发收口，U13 验收失实修正）+ H05/数值钳制 + 死代码/U19 注释清理
- **批次 3**：`gen_balance_map.py` C# 形态增强（未引用键 101→1）+ 重跑 + 文档同步迁移后现状（ARCHITECTURE/DESIGN_BASELINE/EVENT_MANAGER/ENTITY_MANAGER 等）+ V 系列归档
- **遭遇配置缓存修复**：缓存改内层字典引用——`EventManagerTest` 直写 `ENCOUNTER_CONFIG` chance 固定掷签对值缓存不可见（CI 偶发约 70% 失败率根因）

### CI（2026-08-09，审查盲区收口）

- 触发分支扩 `feature/*`；断言场景步骤加引擎错误日志扫描（日志含 SCRIPT ERROR/Parse Error/Compile Error/Nonexistent function 即失败，堵死退出码 0 的静默通过）+ 场景数硬校验（防改名/新增掉出 CI，不硬编码计数）

### 文档（2026-08-09，M7 后全量文档订正）

- 入口/测试/约定/设计文档对齐零 GDScript C# 现状：`scripts/*.gd` → `csharp/godot/*.cs`、`autoload/game_state.gd` → `csharp/godot/GameState.cs`、snake_case API → PascalCase、「GDScript 项目/渐进式 C# 混编」→ 全量 C#（零 GDScript）、gdtoolkit 门禁退役、跨语言边界规则消解；断言/场景计数仍唯一权威于 `docs/TESTING.md`，其余文档只引用不硬编码

### 修复（2026-08-06 全项目审计，`docs/archive/2026-08-06-audit-report.md`）

- **高危**：Boss 战中返航 → 继续出击双 Boss 同场（H1：`clear_pending` 按「存活 Boss 注册表」区分复位条件 + 回归测试）；分裂者（第 5 型敌机）实战永不生成（H2：`unlock_scores` 扩展 5 档 + 子机继承母体难度随对局 ramp）
- **中危**：子弹池 `self_modulate` 染色残留（M1，laser 复用带旧 tint）；损坏存档 `.corrupt` 备份被二次隔离删除（M2）；伪敌机约 75% 出生即销毁（M3）；4 型 Boss 狂暴分档表补齐（M4）；zoom>1 星空右/下边缘无星（M5）；`user_db_test` 销毁本地用户 + 5 测试经 profile 间接清零 pre-login 数据（M6/M7 快照还原范式）；CI 加 BALANCE_MAP 生成器重跑零 diff 闸（M8）
- **低危批量**：give_up 与 dock 同帧完成死亡小窗冻结、遭遇事件进行中禁蓄力召唤母舰、加特林弹仅视觉缩放（不缩碰撞形状）、护盾吸收计入 A16 单帧守卫、Boss 逃跑警告期上飘三型补齐、打击航母悬停/炮塔行锚点加 view 基线、预告线视觉寿命读配置、精英炮塔弹药序列条目级判型、UserDB 条目级守卫与删号清理 `.corrupt`、`E2_AIM` 对齐 G3 telegraph 门限（0.3→0.35s）、里程碑推进改 while 与读档口径一致等
- **测试规范**：键位/profile/用户表快照还原补齐；`boss_phase_test` 生成失败 null 守卫防仓库 balance.json 留损坏态；`_milestone_count` 直写改公开 setter
- **验证**：gdformat / gdlint / import 0 error / 45 断言场景全绿

### 环境适配（2026-08-07，启动脚本引擎探测通用化，不写死个人路径）

- **引擎探测链增加 `godot4` 候选**：`run.sh`/`run.command`/`run.bat` 三个启动器 + `release.sh` 的引擎探测统一扩展——PATH 内 `godot` 之外兼容 `godot4` 命名（多数 Linux 发行版仓库包即以此为名），纯 `command -v`/`where` 探测，无任何个人/本机路径硬编码，其他机器同样生效
- **适配背景**：本机仅安装 `godot4`（4.6.2 stable，`/usr/local/bin`），原 `run.sh` 只认 `godot` 直接报「未找到引擎」；此改动后 `./run.sh` 开箱即用，`release.sh` 本机发布链同样打通
- **文档同步**：`.agents/shell-scripts.md` 引擎候选口径更新（`godot`/`godot4` → `~/.local/bin/godot` → macOS `/Applications`）
- 验证：`bash -n` 三脚本零语法错误；`./run.sh --headless --quit-after 300` 经 godot4 正常启动退出 0

### 工程化（2026-08-07，C# 渐进式混编着陆点落地，`.agents/csharp-conventions.md` §Landing Plan）

- **P1-1 BalanceService 点路径解析核心 → `InfiAir.Core.Config.PathResolver`**（`d0fb9e2`）：纯 .NET 纯函数 + `PathResolverInterop` 薄壳；`scripts/balance_service.gd` 保留 `cfg()` 签名转发（469 处调用点零改动，BALANCE_MAP M8 零 diff）；数值宽容/容器拷贝/typeof 相等语义逐条镜像，kind 标签桥接 StringName 区分
- **P0-1 SaveManager → `InfiAir.Core.Storage.SaveStore`**（`fcb37d1`）：原子写（tmp + rename 回退）/损坏隔离（.corrupt + last_was_corrupt）/System.Text.Json 序列化全量迁移；`scripts/save_manager.gd` 薄壳转发（公开 API 不变）
- **P0-2 UserDb 数据层 → `InfiAir.Core.Storage.UserDb`**（`0acb28b`）：CRUD/登录记录/本地排行榜/名称校验 + 自建 PBKDF2 变体**逐字节等价迁移**（5 组迁移前 GDScript 实测固定向量 + 存量账号验密兼容测试）；`scripts/user_db.gd` 薄壳转发（公开 API/迭代数降档机制不变）；Q17/Q18/Q20 结构守卫保留
- **P1-2 Progression 进程曲线核心 → `InfiAir.Core.Progression`**（`MilestoneCurve` + `DifficultyCurve` 纯函数 + `ProgressionInterop` 薄壳）：里程碑阈值曲线（8 档基础 × cycle_mult^cycle × 难度倍率）与难度进程曲线逐位等价迁移（累加顺序/`Math.Pow` 调用/roundf half-away-from-zero 对齐；极大 index 显式钳制防 int64 溢出 UB）；`apply_run_save` 里程碑定位改 `CountThresholdsUpTo` 单次调用 + O(1)/档 增量推进——实测存档恢复路径 1623µs → 19µs（**提速 ~85×**，score=1e9 档）；`milestone_threshold`/`_recompute_difficulty` 转发，公开签名不变（加分逐档保留 while——`set_milestone_override` 钩子允许阈值脱离曲线，批量推进不适用）
- **P1-3 任务池抽取核心 → `InfiAir.Core.Missions.TaskPool`**（纯逻辑 + `TaskPoolInterop` 薄壳）：洗牌游标无放回抽取（排除项/跨批补足 Q05/全池排除安全空）语义不变；RNG 独立于 GDScript 全局随机源（性质等价、序列不等价，无外部依赖具体序列）；`scripts/task_pool.gd` 薄壳转发（公开签名不变）
- **共享基建**：`csharp/godot/VariantBridge.cs`（Variant↔CLR JSON 兼容树双向转换）；新增 3 个 interop 断言场景（`path_resolver_interop`/`save_store_interop`/`user_db_interop`）；P1-2/P1-3 新增 `progression_interop`/`task_pool_interop` 2 个
- **验证**：dotnet build 零警告 + xUnit 73/73（P1-2/P1-3 新增 19 项）+ 53 断言场景（5 新场景 + 账户/存档/难度/任务回归全绿）+ main 冒烟干净
- **教训**：跨语言单值调用（~5µs/次）稳态开销 > GDScript 自身求值（~2µs）——逐档小计算跨语言为**负收益**，批量计算（档数推进/存档恢复）才有正收益（~85×）；基准验证先行、避免"为迁而迁"（与 `docs/C_SHARP_ASSESSMENT.md` §4.1「API 密集场景两者接近」判断一致）
- **教训**：GDScript 的 `int("0x" + s)` 按**十进制**解析（"0x11" → 11）——PBKDF2 向量生成器首版盐解析被污染，Python/C# 独立复算 + 探针逐字节对比后纠正（向量生成须用 `String.hex_to_int()`）；C# 三元表达式 `cond ? (long)x : (double)y` 因隐式拓宽会把整型统一装箱成 double（PathResolver/SaveStore 各修一处）

### 搁置项重启（2026-08-07，`docs/archive/2026-08-07-deferred-restart-plan.md`）

- **触屏虚拟操控（mobile touch 重启立项落地）**：新增 `VirtualControls` 触屏输入层——左摇杆移动 / 右摇杆瞄准（增量，同手柄语义）/ boost·fine·dash·parry 虚拟按钮，Input action 注入、键鼠/手柄零回归；设置页「触控」开关（profile 持久化 + `touch_controls_changed` 联动 Main）；player 触屏瞄准基准（无鼠标，可见世界中心）；`virtual_controls_test` 26 断言
- **修复**：设置页「操作模式」页溢出 480px 容器（L17——ChamferedPanel 内容自适应高度钳制 + 内容页滚动容器，窗口实测面板 754px 不超屏、内容可滚动）；母舰 HUD 引用 9 处重复组查找收敛为延迟缓存（A5 残余收敛）
- **测试**：`encounter_flow_contract_test` 13 断言（遭遇自动触发短窗口契约 + 配置锚点 + 事件中禁蓄力 + 死亡清理召唤小窗独立断言，补 R 系列 #9 / 2026-08-06 #7 待办）
- **验证**：gdformat / gdlint / import 0 error / quit-after 300 0 error / 47 断言场景 0 FAIL
### 文档重构（2026-08-07，去歪曲 + 减绕路，`docs/archive/2026-08-07-doc-refactor-plan.md`）

- **计数单一事实源**：TESTING.md 新增「Scene Counts」动态权威计数（`ls test/*_test.tscn | wc -l` − 1）；doc-sync 固化「其他文档禁止硬编码断言数」规则；全仓当前流程描述统一 47（ci.yml 步骤名去硬编码、CONTRIBUTING/C_SHARP_ASSESSMENT/ROADMAP/DESIGN_BASELINE/README 徽章 v3.28 + 47 scenes）
- **状态回填**：AUDIT_VAULT A5/A8 状态表 ✅（与 DESIGN_BASELINE 同步）+ A4/A5/A8 详情划线 + R 复核 #1/#10、C34、L-P3 清单收口
- **知识补齐**：TESTING.md「Headless Test Environment Notes」——输入注入坐标变换陷阱（实测 30×）/ gdtoolkit PEP 668 / translations.csv 重导 .translation / 公开测试口规范；gdscript-lifecycle 补「Tests drive public test ports」约定
- **引用修正**：README 徽章 v3.28、DESIGN_BASELINE/ARCHITECTURE 服务 6→7（补 UserDB）、节点树注册口径（GameEventManager）、注释路径补 archive/ 前缀 ×2、审计编号修正（enemy_pool R04 / boss R06/R12 / back_navigator R12）
- **验证**：残留扫描 0 命中（45 计数/Six services/registered to spawner/缺前缀）+ 五层门禁全绿（47 断言场景 0 FAIL）；零逻辑改动


## [3.27] - 2026-08-06

### 规范化（2026-08-06，项目安排对齐官方/社区实践，Playwright 调研 + 本机实证）

- **config/version 腐蚀修复（技术修正）**：`project.godot` 版本注释误用 `#`（ConfigFile 仅认 `;`）——引擎加载时注释行与下一行键名熔合，`application/config/version` 对引擎长期不可读（`get_setting` 实测返回空）；改 `;` 注释后恢复读取（探针实证 3.27）
- **project.godot 引擎规范化**：以 `ProjectSettings.save()` 实际输出对齐——`[debug]` 段归位字母序（原置文件尾，编辑器每次重存必产生噪声 diff）；剔除默认值冗余行（`window/stretch/aspect="keep"`、`gdscript/warnings/enable=true`，均为引擎默认值）；规范化后引擎重存零 diff 实证
- **导出配置实证复核（结论：原配置正确，零改动）**：`--export-pack` + 独立 PCK 虚拟文件系统转储逐项核验——`exclude_filter="test/*"` 在 4.6.2 真实生效（R01 结论成立）；`data/balance.json` 作为 JSON 资源随 `all_resources` 自动进包（官方文档"json 需 include_filter"表述对 4.6 已过时）；0 个 .py/.sh/.md/builds 产物泄漏。注：strings 提取法对 PCK v3 不可靠，初审曾误判 test 泄漏与 json 缺失，须以引擎转储为准
- **`.gitattributes` 新增**（官方 VCS 页推荐、仓库缺失）：全文本 LF 规范化 + `*.bat` 检出 CRLF（cmd.exe goto/label 边界）+ `*.sh` 强制 LF
- **`builds/.gdignore`**：导出产物目录退出编辑器文件系统扫描（对齐 `docs/.gdignore` 约定）
- **release.sh 版本回退硬失败化**：sed 取不到 `config/version` 时由静默回退 3.26（产出与 project.godot 脱节包名）改为 stderr 报错退出
- **入口提示词精简（agent-md-refactor 流程，快照 /tmp 留存）**：CLAUDE.md 删除 `## Architecture Essentials` 整节（8 条要点与 `.agents/*` 逐条重复，且体碰描述已随 P0-2 漂移失实——独有信息迁入 collision-view.md：ENRAGE_HP_RATIO 锁血 30% + enemy 事件驱动/boss 有意轮询体碰机制修正）；CLAUDE.md 另修 godot "not on PATH" 失实表述（本机即在 PATH）与 pre-commit 段代码围栏缺失（`#` 标题误渲染）；剔除审计考古注记（start_radar 删除/gdtoolkit R09/run.bat 原实现叙述/world_scale 日期）与 game-ui-ux 技能溯源元数据——提示词载体 207→199 行，零规则丢失逐条核对

### 审计（2026-08-05，R 系列独立审计修复，`docs/archive/2026-08-05-independent-audit-report.md`）

- **发布包净化（R01）**：`export_presets.cfg` 两预设 `exclude_filter="test/*"`——此前全部 45+ 测试场景/脚本随发布包分发（PCK 实锤），重出即净
- **BGM 交叉淡化修复（R02）**：`generate_audio.py` 和弦权重有效区间扩至 `CHORD_DUR+XFade`（原严格截断 → 每 5s 交界零谷塌陷，已烘焙进旧资产）；`bullet_fire` 三变体生成前重置随机种子对齐提交资产（消除生成器-资产漂移）；重生成后仅 `bgm_loop.wav` 变化
- **离线工具链（R03/R08/R09）**：sprite 三生成器输出路径锚定脚本位置（非仓库根运行不再错落盘）；balance_editor 读侧损坏友好 400；release.sh tar/zip 前置检查 + 版本自动读 project.godot；run.bat 版本判定 + 退出码保真；CI gdtoolkit 锁 4.5.0
- **防御纵深（R04-R07/R13/R14）**：Q19/Q23 修复两侧遗漏补齐（池化 spawn 信号流对称、startup_flow 快照顺序）；判型族 10 处（starfield/spawner/WEAK_LOCK/狂暴 interval/hp_mults 正值域/存档负值/bullet 零速）；防御缺口 3 处（锁输入弹反盾残留/volley 进行中守卫/escape 常规攻击清理）；`Bullet.COLLISION_RADIUS` 常量（player 擦弹判定互引）；移动策略 freqs/phases 长度校验
- **清理与文档（R10-R12/R15）**：测试规范 3 处（调试 print/OR 弱断言/InputMap 收尾）；注释失实 6 处；M07-M09 落地（back_navigator CONFIRM_EXIT 死分支/start_panel+start_radar 孤儿脚本/SET_LANGUAGE_ZH+EN 孤儿键）；RING_BURST_COUNT 死数据删除；load_steps/AGENTS 计数/EXIT_FLOW/金库状态表/BALANCE_MAP 同步
- 验证：gdformat/gdlint/import 0 error/18 定向场景/45 断言场景 0 FAIL；生成器实证（音频逐字节 A/B、BALANCE_MAP 469 调用、sprite 零 diff）

### 架构（2026-08-05，统一实体管理器，`docs/ENTITY_MANAGER.md`）

- **统一实体管理器 `EntityManager`**（`EntityRegistry` 演进）：实体注册样板收敛——`bind_enemy`/`unbind_enemy` 一行（`add_to_group("enemy")` + 注册/注销 + `entity_registered`/`entity_unregistered` 生命周期信号，新功能订阅零改动单位类），enemy/boss/turret_battery/formation_craft 四处重复样板消除
- **批量操作 API**：`for_each_enemy`（失效实例跳过 + 谓词过滤）/`clear_enemies`（保留项谓词，如轨道打击保 Boss）/`count_enemies`——main 轨道打击清场、母舰慢速场与索敌（方法引用保 P2 缓冲零分配）、狂暴倾巢齐射、spawner spread 计数统一迁移
- **可行性与收益**（Playwright 调研）：真实 Godot 项目 underkingdom 的 Autoload EntityManager 实战印证模式；对象池社区指南确认低频实体不池化（Godot 4 节点创建快）——Boss/炮塔/编队保持直建，仅敌机/子弹/爆炸池化不变
- 验证：新增 `entity_manager_test`（绑定幂等/组同步/信号/批量谓词过滤/保留项清除）；池化语义、GameState 转发面、autoplay 组↔注册表一致性回归不变；全量 45 断言场景门禁

### 架构（2026-08-05，统一事件管理器，`docs/EVENT_MANAGER.md`）

- **统一事件管理器 `GameEventManager`**（`GameState.events`，挂 GameState 下）：全部随机游戏事件（迷雾 4 + 遭遇 2）收敛进单一注册表 `EVENT_FACTORIES`——统一触发策略（balance key 零变化：`fog_events.*`/`elite_turret_event.trigger_*`/`formation_strike_event.trigger_*`）+ 分组并发（`fog`‖`encounter`，组内单事件并发、组间并行，保持现状行为）+ 统一生命周期 + 信号 `event_started/event_ended`
- **遭遇事件迁移**：精英炮塔/轰炸编队触发策略移出 `spawner._process`（`ScheduledEventTrigger` 退役）；事件保持 Node 状态机（实体生成/FSM 自驱），管理器经鸭子类型契约驱动（`is_active`/`start`/`abort`/`can_trigger`），测试 API（`main.event()`/`main.formation()`/`spawner.elite_event()` 等）零改动；触发门控 = 注入 spawner 处理中（`set_process(false)` 语义与现状一致）
- **迷雾收敛**：`FogEventManager` 重构为迷雾效果层 + API 门面（视觉基座/迷雾信号重发/context 构建保留；公开 API 与配置 var 全部转发到 `GameState.events` fog 组）——`fog_event_test` 零改动通过
- 验证：新增 `event_manager_test`（统一注册表/实例同一性/强制触发/组并发/自动触发门控/跨组并行/门面委托）；全量 44 断言场景 0 FAIL（2026-08-05 回归）；240s autoplay 探针实测遭遇事件经管理器正常触发（37.3s 炮塔 / 177.9s 编队，无 ANOMALY）

### 玩法（2026-08-05，基地任务轮换 + 迷雾事件，`docs/FOG_EVENTS.md`）

- **基地任务轮换**：任务池（`MISSION_POOL` 9 任务 = 3 类 × 3 档）+ `TaskPool` 无放回抽取（洗牌游标，排除在场 id）；刷新点数经济（进基地 +1 / 刷新 −2，`base_task.refresh_cost`/`grant_per_visit`，存档往返）；刷新保留已完成未领取任务（不吞待领奖励）；任务进度改按 `kind` 分发（kill/survive/boss），轮换后 id 变化仍自动推进；基地任务面板渲染 `active_mission_ids()` + 刷新按钮/点数不足提示
- **迷雾事件系统**：全局单例 `FogEventManager`（挂 GameState 下，维持唯一 autoload 约定）——概率触发（`trigger_chance`/`check_interval`）+ 开局保护（`first_delay`）+ MinInterval 冷却 + Duration 到期自动清除 + 单事件并发；信号（`fog_event_started/ended/fog_direction_shift`）解耦触发与效果
- **事件类接口（可扩展基底）**：通用 `GameEvent`（纯生命周期接口：context 注入/幂等 end/重复 start 自愈/浅拷贝隔离，零系统耦合）→ 迷雾专门化 `FogEvent` → 具体事件；`EVENT_FACTORIES` 注册表一行注册即接入；自动触发仅真实对局（`current_scene == main`）开启，测试上下文默认关闭（确定性）
- **事件宽容性**（2026-08-05 调研官方 OOP 实践/社区后落地）：同一接口同时接受简单（仅 `event_id()`）与复杂（`_on_start/_on_tick/_on_end` 全钩子）事件；新增 `request_end()`（复杂事件内部目标达成可主动提前结束，经 context 回调）与 `get_ctx()`（简单事件一行读自定义数据）；fog_event_test §10 新增 12 项宽容性断言（复杂事件 request_end/极简事件全生命周期/get_ctx 缺键降级）
- **4 种干扰事件**：伪敌机（无伤害/无碰撞幽灵机群，`FakeEnemy`）、精神错乱（输入反转 + 全屏变色层）、子弹错误（出膛弹角度偏移/慢速失误弹/射速扰动）、短间隔随机方向（周期强制移动向量偏转）；返航/死亡自动清除
- 健壮性（2026-08-05 审计）：事件类作为后续所有事件的唯一入口——空注册表/非 Callable 防御、Timer 先行防事件 start 抛错挂死、context 缺键降级不崩；`fog_event_test` 新增 §8 事件类生命周期守卫与 §9 编排器防御路径共 14 项断言
- 验证：gdformat/gdlint/import 无新增告警；新增 `base_task_refresh_test`/`fog_event_test` 断言场景，既有 41 场景 0 FAIL 回归通过（base_system/elite_turret/formation/mothership_summon/return_cinematic/hit_logic/grace/parry/buff_effects/i18n 等）

### 性能（2026-08-05，主架构运行效率审计全量执行，`docs/archive/2026-08-05-main-architecture-optimization-report.md`）

- **P0-1 死亡回放录制零分配化**：数据源 `get_children()` 改敌弹注册表（`EntityRegistry.enemy_bullets`），帧缓冲改固定容量环形缓冲（删 `pop_front` O(n) 移位），内层 `[x,y]` 改 `PackedFloat32Array` 复用槽——全对局唯一高危常驻分配链消除
- **P0-2 敌机体碰事件驱动**：`area_entered/exited` 标记重叠 + 重叠期 O(1) 守卫重掷替代每物理帧 N 次 `overlaps_area` 空间查询（无敌/闪避/单帧语义与轮询完全等价）
- **P0-3 子弹渲染合并**：弹体+白芯双 Polygon2D 合并为单 Sprite2D + 共享图集（扫描线光栅化），同阵营同色触发 compat batcher 合批——**窗口实测 181 颗子弹 draw calls 245→38（-85%）**，视觉经像素级核验一致
- **P1**：爆炸/溅射遍历去 `duplicate()` 拷贝（倒序索引）；玩家 spread 循环外提 `pow()`；HUD 仪表 epsilon 守卫；BGM 改 `CACHE_MODE_REUSE`（不再每次进 main 重新解码）+ 裂纹场烘焙延后首帧后；爆炸池回池 reparent 统一 `ExplosionPool` 节点
- **P2**：Meta HUD 自适应增益改注册表/静态计数（`Bullet.active_count`/`Explosion.live_count`）；同屏敌弹显式硬上限 500（仅敌弹，15 处调用方判空）；碰撞 mask 自查通过（无隐形碰撞对）
- 验证：gdformat/gdlint/import/quit-after/smoke 全绿，41 断言场景 0 FAIL；窗口 draw call 实测 + 像素视觉核验；AUDIT_VAULT P 系列登记回填

### 美工（2026-08-05，buff 槽位与图标重构）

- **buff 槽位 socket 化**（纯视觉，无逻辑链路改动）：`ChamferedPanel` 新增可选 `inner_frame`（外轮廓内缩 3px 的嵌套切角细线，默认关）；`UITheme.make_buff_socket()` 统一槽位工厂——分类色描边（0.7）+ 同色内框（0.28）+ 面板底向分类色微倾 16% 底，HUD 图标坞瓦片（46×46）与 Buff 三选一卡片图标位（76×76）共用同一套槽位语言
- **×N 层数徽标芯片化**：坞瓦片右下角由裸文字改为切角小芯片（深底 + 分类色描边 + 金色数字，与滚动栏明细行 ×N 同色）；收起态 +N 溢出格同步 socket 样式（淡色底 + 内框）
- **字形小尺寸可读性**：`ui_buff_icons.gd` 线宽加 2px 下限（`maxf(2.0u, 2.0)`），HUD 瓦片 26px 下不糊、卡片大尺寸随缩放自然放大；19 字形设计不变
- 验证：gdformat/gdlint/import 无新增告警，41 断言场景 0 FAIL（buff_panel/buff33/buff_visuals/buff_effects 重点回归），hud_capture（常态/极端/展开三形态）+ ui_capture 窗口截图人工核验

### 平衡（2026-08-04，无限段深局校准，`docs/archive/2026-08-04-endless-calibration-plan.md`）

- **无限段深局校准落地**（ENDLESS_BALANCE_PLAN §6.1，此前 >15 min 校准 deferred）：`progression.per_boss_kill` 0.5→**0.6**、`per_ten_minutes` 1.0→**1.5**（时间档 +0.075/30s）、`enemies.hp_ramp_factor` 0.12→**0.25**、`damage_ramp_factor` 0.08→**0.20**
- 三轮 900s 深局探针（seed 20260729）验证：基线确认 zero-pressure 稳态（27 min 0 死亡、血量长期满、击杀率不降）→ 定稿后 diff 1.38→6.33 @27min 无平台期、HP min 40–69 持续压力、DDA 15–29% 窗口、0 死亡无崩盘、全程 0 `[ANOMALY]`
- `difficulty_test` 进程曲线断言同步（2 杀 ×2.2；65s 两档 +0.15 → 2.35）；全量 41 断言场景 0 FAIL

### 修复（2026-08-04，A 审计 + CI）

- **A 审计稳健性 ×5**：`reset_run` 清 DDA 计时（跨对局降档残留）；`milestone_threshold` pow 溢出钳制 + 空表守卫；`apply_run_save` 里程碑定位迭代上限（异常配置大分数读档防挂死）；`cfg()` Array/Dictionary 返回浅拷贝（防误写污染配置真值）；`SaveManager` 原子写 rename 优先（rename 失败不再丢正本）
- **CI 编译探针修复**：恢复 `autoplay_test.gd` 被误注释的 `_handle_pause_ui`/`_do_menu_return`（适配 StartPanel 退役：重进 main 启动自动读档）；`visual_capture.gd` 补回 `FRAMES_BEFORE_SHOT` 常量

### 修复（2026-08-04，M06 图标字形）

- **新 buff 专属字形 + 分类色**（`scripts/ui_buff_icons.gd`，M06 遗留落地）：`crit_shot`/`shield`/`bullet_speed` 不再走回退圆环——补几何字形（暴击=十字准星+中心点、护盾=圆盾外环+菱形脊、弹速=水平弹头+三条速度线），分类色归位（暴击/弹速→进攻青、护盾→维生绿），16→19 字形全覆盖；HUD 图标格与 Buff 三选一卡片共用

### 玩法（2026-08-04，内容演化，`docs/archive/2026-08-04-content-evolution-plan.md`）

- **新 buff ×3**：暴击 `crit_shot`（12%/层 ×2 伤害，真实命中路径测试）、护盾 `shield`（每层吸收一次全额伤害，`GameState.consume_buff` 层消耗 API）、弹速 `bullet_speed`（+20%/层，声明式 pow 表）
- **新敌机 分裂者**：死亡分裂 2 小机（×0.6 缩放 / HP 半 / 无分数 / 不再分裂）；**新精英 重装炮台**（最高 HP 慢速弹幕机）
- **第 4 号 Boss「月蚀」**：环弹术士——`ring_burst` 全圆环弹 + 中心悬停微摆；狂暴「月蚀」双环反向进动 + 蓄力环阵；轮换扩 4 型（`spawner` `%4`）；架构断言与场景测试全量扩展（`boss_registry_test` 10 攻击/4 机型、`boss_pattern_test` 场景7、`boss_enrage_test` 场景5）

### 玩法（2026-08-04，母舰扩展）

- **母舰火力随里程碑升级**（`docs/archive/2026-08-04-mothership-expansion-plan.md`）：对局里程碑 ≥5 后加特林/导弹伤害 ×1.5、射速 +25%（`mothership.upgrade` 配置段）；驻留状态栏显示「火力升级 ★」提示

### 账户与入口（2026-08-04，本地账户系统）

- **本地用户系统**（`docs/archive/2026-08-04-local-accounts-plan.md`，重启 Phase 3）：`UserDB`（`user://users.json`，PBKDF2-HMAC-SHA256 密码 + 盐、注册/登录/游客/删除、last-login 排序、每用户设置与统计）；删号连带清理该用户存档
- **welcome 主场景**（新 `project.godot` 主场景）：登录面板（用户名/密码/下拉）+ 难度 + 教程 + 设置 + 本地排行榜 overlay + 游客/删除/退出确认模态；ESC 层级 overlay→模态→退出确认；**StartPanel 退役**
- **每用户存档/档案隔离**：存档 `user://savegame_<user>_<hash12>.json`（档主校验，不匹配隔离）；游客不存档、设置仅内存（原版 bug 修复清单 B7 全部落地）；`profile.json` 退役迁移（首个注册用户合并）
- **本地排行榜**（用户维度）：Top10 本地榜（分数降序 + 先到先得），结算页与 welcome overlay 展示
- 新增 3 断言场景（`user_db_test` / `user_session_test` / `welcome_flow_test`）；全量 40 断言场景 0 FAIL（后随母舰升级测试增至 41）

### 工程化（2026-08-02）

- 新增 GitHub Actions **CI**（`.github/workflows/ci.yml`）：无头导入 + 主场景冒烟 + 37 断言场景全量回归，push/PR 触发
- 新增手动触发**发布工作流**（`.github/workflows/release.yml`）：双平台导出打包 → 打 tag → 创建 GitHub Release
- 新增 `CONTRIBUTING.md`（贡献指南）、`SECURITY.md`（安全策略）与 GitHub issue/PR 模板
- `project.godot` 增加 `config/version` 发布版本元数据

### 玩法（2026-08-02）

- **本地高分榜**：结算页本局排名 + 历史 Top5，开始页 Top3（`profile` 持久化）
- **手柄支持**：左摇杆移动 / 右摇杆虚拟准星瞄准 / 动作键（A/RB/LB/X/Y/L3/R3）；设置页「手柄」分区可调右摇杆灵敏度与摇杆死区
- **可读性**：玩家弹白芯描边（敌我弹区分）；致死弹 0.5s 高亮残留（死亡归因）
- **教程可重看**：通关后无存档时教程按钮放行

### 玩法（2026-08-03）

- **战斗公平感四机制**（`docs/archive/2026-08-03-combat-fairness-plan.md`）：受击宽限帧（`player.grace_period`，消灭 ghost hit）、擦弹得分（`player.graze_radius`/`graze_score`，风险-回报技巧轴）、Boss 阶段转场清弹 + 玩家短暂无敌 + 分段血条（`boss.phases.clear_on_shift`/`transition_invincible`/`hud.boss_bar_segments`）、F 键弧光弹反盾（`player.parry.*`，主动防御反击，完整周期 3.8s，手柄 LT）；新增 4 断言场景（grace_period/graze/boss_phase_transition/parry，93 断言）

### 架构与工程（2026-08-03）

- **A3/A4 架构债收尾**：Boss 攻击/移动/狂暴三注册表 + 机型参数表（新增机型/攻击仅需注册一行）、Player buff 声明式效果表 `BUFF_EFFECTS`（新增数值型 buff 只需表加一行）
- **L 系列第十轮全仓库审查**：P1×3/P2×9 修复（池化复用 buff 信号重连回归、截图工具编译错误、autoplay 母舰状态表漂移、判型补全等）
- 新增 2 架构断言场景（`buff_effects_test` 38 断言 / `boss_registry_test` 29 断言）；全量 37 断言场景 0 FAIL

### 玩法（2026-08-03，B 梯队：公平感延续，fair plan §8）

- **Boss 攻击独特 tell**：9 种攻击起手各有独特音效变体 + 视觉前兆冲击环（`boss_attacks.gd ATTACK_TELLS`），玩家可区分「来的是什么」
- **DDA 弹幕密度降档**：玩家受击后 5s 内敌机开火/波次/Boss 攻击间隔拉长（`dda.duration`/`dda.factor`），**只拉间隔不降收益**（分数公平）
- **死亡回放**：环形缓冲录制最近 3s 敌弹轨迹，死亡后幽灵弹幕重放死因（`death_replay.gd`，暂停结算中照常播放）

### 架构与工程（2026-08-03，Phase 0 收尾）

- **test/ 门禁盲区修复**：`test/` 纳入 `gdformat --check` + `gdlint`（23 文件格式化、18 条静态问题修复）；CI 新增编译探针步骤（逐场景 `--quit-after 2` + 错误 grep，捕获 `--import` 不解析未引用场景的编译错误盲区）+ 断言场景单场景超时
- **A8 PlayerVisuals 拆分**（最后一项架构债）：尾焰/残影池/机身色调/受击点/弹反视觉/擦弹闪光迁出 player.gd（`scripts/player_visuals.gd`，组合委托模式）
- **L 系列待办收敛**：L13 母舰在场期事件互斥（精英炮塔/编队不再被母舰自动火力白嫖发奖）、L14 Boss 段切换 y 平滑过渡（消除 1/4 屏瞬移）、L15 测试 profile 最高分快照还原（20 场景）、L16 smoke 弱断言、L18 发布工作流版本号提交落地
- **P2 清理**：`ACTION_LABELS` 死代码删除、`back_pressed` 死信号登记、`profile_corrupt` 损坏档案开始页提示（新增 `START_PROFILE_CORRUPT` 双语键）
- 全量 37 断言场景 0 FAIL；`gdformat`/`gdlint` 全量（含 test/）全绿

## [3.26] - 2026-08-02

### 性能

- 性能优化全量落地：敌机生成统一池化（`USE_POOL` A/B 开关）、`view_world_rect` 物理帧缓存、受击闪白手动衰减、`sin_fast` 查表清扫、渲染合批；`perf_bench` 约 -8~9%

### 玩法

- Boss P2 阶段走位升级：一型/三型 P2 strafe 提速 + 纵向正弦往复、二型 P2 dash 节奏、三型 P1 锚线下区间呼吸（`boss.movement` 配置段）
- 鼠标锁定窗口内设置项（防准星出框失控；暂停/非准星态与失焦放行）

### 修复

- G 系列核心逻辑 32 项处置（spawner 预警取消复位、Boss 逃跑期免伤、教程入口守卫、注册表 O(1) 索引等）
- E 系列存量盲区修复（母舰溅射对 Boss 生效、教程删档守卫、难度表子键校验等）
- A21 测试失败基线根因修复（入场坐标按战斗锚线动态定位）

### 文档

- 全量文档口径统一（状态误记订正、内部矛盾消除、计数与失效哈希修正）
- 已完成工作压缩留档：`docs/archive/EXECUTION_LOG.md` 索引 + 10 份计划/审核文档归档
- 许可证落地：MIT + 第三方声明（Noto Sans SC / OFL）

## [3.25] - 2026-08-02

### 修复

- D 系列全量代码审查修复（入场 Timer/预告线清理、入场中断复位 `abort_entry`、HUD 缓存、硬编码收敛等）
- E 系列批次修复（教程按钮禁用与入口守卫、提前离舰进度条清理、存档原子写等）

## [3.24] - 2026-08-01

### 修复

- C 系列 Godot 规范审计 35 项处置（教程协程泄漏、存档 key_bindings 类型守卫、难度表校验、子弹位移改物理帧等）
- B 系列业务逻辑修复（狂暴瞄准线泄漏、time_scale 复位、Boss 逃跑结算守卫、追踪弹 stale 引用等）

### UI

- 全界面系统化 uplift：统一模态骨架与动效、Buff 卡片与 HUD 仪表簇重设计

---

早期版本（≤ 3.22，2026-07-31 发布工程化起步）的变更记录见 `git log`；移植对齐时期历史见 `docs/archive/PORTING_PARITY.md`。
