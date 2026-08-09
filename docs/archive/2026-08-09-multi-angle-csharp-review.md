# 2026-08-09 C# 分支多角度严格审查报告（V 系列）

> 依据用户指示「对c#分支进行严格的多角度审查」执行（goal 模式）。分支：`feature/csharp-full-migration`。审计登记：`docs/AUDIT_VAULT.md` V 系列。方法遵循 `docs/AUDIT_REVIEW_SOP.md`（并行审计 → 分类 → 分批修复+即时验证 → 归档回填）。

## 1. 背景与目标

U 系列（2026-08-09，迁移保真导向，10 分区 × git 对照）已收官并修复（B1–B5，`b020d0d`）。本次为**第二轮多角度审查**：在保真之上扩展性能、生命周期、安全、测试、文档、Godot API 维度，并验证 U 系列修复无回归、验收声明与实际一致。

## 2. 基线（审查开始前实测）

| 项 | 结果 |
| --- | --- |
| `dotnet build` | 0 警告 0 错误 |
| `dotnet test tests-csharp/` | 75/75 全绿 |
| 全量断言场景 | 55/55（审查前基线） |
| BALANCE_MAP | 行号漂移 +1（Boss.cs 段，月蚀重制后未重跑） |

## 3. 方法与范围

8 路并行只读审查（explore 子代理），分区不重叠：

| 路 | 角度 | 主查 |
|---|---|---|
| 1 | 热路径性能 | GameEventManager/FogEventManager/Bullet/BulletPool/Enemy/EnemyPool/Player/PlayerVisuals/PlayerBuffVisuals/Hud/Starfield/Spawner/PerfBench |
| 2 | 生命周期信号 | Main/Mothership 族/Boss 族/过场/DeathReplay/Coroutine/MetaHealthFX/StrikeCarrier/FormationCraft/TurretBattery |
| 3 | 数据安全 | core/Storage 三件套/SaveManager/UserDB/VariantBridge/GameState 存档段 |
| 4 | 正确性边界 | core Progression/TaskPool/BalanceModels/EnemyMoveStrategy/Boss 族/Player 组件/Bullet/Explosion/LaserWeapon/OrbitalStrike |
| 5 | 测试质量 | csharp/godot/tests 90+ 场景测试/tests-csharp/CI 门禁 |
| 6 | 文档一致性 | ARCHITECTURE/TESTING/AGENTS/DESIGN_BASELINE/BALANCE_MAP/BOSS_REDESIGN/EVENT_MANAGER/ENTITY_MANAGER/C_SHARP_ASSESSMENT |
| 7 | Godot API | csharp/godot 全目录引擎 API 使用 |
| 8 | U 系列回归 | b020d0d diff 逐项核对 + U01–U20 现状 + 残留动态派发 |

## 4. 发现与处理总览

**P1×2（均已修复）**：

| # | 位置 | 问题 | 修复 |
|---|---|---|---|
| V1 | BossAttacks.cs:66-70 + Main.cs:17-19 等 | 静态字段持有 Godot RefCounted（AudioStream/PackedScene/Texture2D）——退出 segfault 风险（UITheme.cs 实测先例，U07 名单外 8 处） | 5 处改实例字段；BossAttacks 改路径 StringName + 播放处 GD.Load（命中资源缓存） |
| V2 | UserDb.cs:480/495 | U15 引入回归：`long.Parse` 超长数字溢出抛异常，手改 users.json 中断登录/榜单流程（原 TryParse 回 0 契约被击穿） | `long.TryParse` 回 0（十六进制/十进制两路径）+ iterations 钳制上限 1e6（防挂死） |

**P2×14（修复 11 项，登记不修 3 项）**：

| # | 位置 | 问题 | 处理 |
|---|---|---|---|
| V3 | GameEventManager.cs:450/517/742 | 遭遇组每帧 2×Callable.Call 动态派发 + 每帧空 Dictionary 分配 + 无注销路径 | ✅ 配置/实例注册时缓存 + EmptyId 复用 + IsInstanceValid 校验 |
| V4 | Main.cs:679-697 | 读档续局（OnContinueRun）死亡回放不 Begin，播旧缓冲 | ✅ `OnContinueRun` 补 `_replay.Begin()`（ApplyRunSave 后，与 ApplyNewRun 同构） |
| V5 | Explosion.cs:76-77 | U16 修复自身回归：fresh 实例 _liveCount 双计数（_Ready++ 与 SpawnAt++） | ✅ 复位计数移入池取分支 |
| V6 | BaseConsole.cs:111-115 | dim ColorRect 重复 AddChild（引擎 ERROR） | ✅ 删重复行 |
| V7 | CSharpCallTest.cs:44-54 | 三重恒真断言 + 调用已删方法（引擎错误每次 CI 静默通过） | ✅ 断言具体值 + 删重载探针 |
| V8 | BossAttacks/EnrageSequence 20 处 | U13 验收失实：Boss 发射链动态派发仍活 + 13 个 snake 桥 | ✅ 全量 typed 直调 + 删 BossFire snake 桥 + configure 桥；Boss.Fire_* 保留（测试契约） |
| V9 | Bullet.cs:292-296 | H05 dist==0 置 Vector2.Right（90° 突变，与注释矛盾） | ✅ 不动 Direction |
| V10 | LaserWeapon/Player/Boss | R06 同族键未钳制（tick_interval=0 → 960DPS；dash.time=0 → NaN；hp_ratio>1 → 抬血锁血） | ✅ 三处钳制 |
| V11 | CameraShake.cs:20 | 信号 double/监听 float 不一致（U16 漏网） | ✅ 监听侧适配 double |
| V12 | gen_balance_map.py | C# 形态覆盖不全（CfgVal/Resolve/效果表/动态拼接不识别）→ 101 未引用键误列 + 16 条重复 | ✅ 5 个 RE 增强 + 必选前缀去重 + 排除测试目录；未引用 101→1 |
| V13 | BALANCE_MAP.md | Boss.cs 全段行号 +1 漂移（CI M8 闸必红） | ✅ 重跑生成器 |
| V14 | 6 份文档 | ARCHITECTURE/DESIGN_BASELINE/EVENT_MANAGER/ENTITY_MANAGER 全文迁移前状态（.gd 引用/计数 47/51）+ AGENTS 66→64 + TESTING 补 2 场景 | ✅ 同步到迁移后现状 |
| V15 | SaveStore.cs:179-186 | Infinity 无下游守卫（1e999 → 难度乘数负数崩坏对局）+ U09 注释失实 | 登记：核心层钳制涉及数值语义变更，登记观察（损坏隔离对"非有限"失效） |
| V16 | UserDb.cs:94/357 | iterations 无上限（手改挂死） | ✅ 与 V2 合并修复（钳制 1e6） |

**P3/P4 约 20 项**：修复 4（死代码×3、SaveManager 恒真条件、Player 957 常量、U19 注释 7 文件）；登记不修（Explosion 静态池、测试端口 ~101 Set*、autoplay 不进 CI、池 Contains O(n)、GetFightPhaseTransition 名实、Meth* 拼写、float 出口截断、difficulty_scaling 判型、PlayerVisuals 数组、Enemy 体碰派发、Welcome rune 口径、OrbitalStrike IMPACT_AT、sweep 残点注释）。

## 5. 验证（全部真实执行）

- `dotnet build` 0 警告 0 错误；`dotnet test tests-csharp/` 75/75；`dotnet format` 三工程零 diff
- 定向场景：事件 5 个（event_manager 36 / encounter_flow_contract 13 / fog_event 70 / elite_turret_event 59 / formation_strike_event 49 PASS）+ Boss 6 个（registry 35 / phase 41 / pattern 51 / enrage 37 / phase_transition 29 / hit_logic 77 PASS）全 0 FAIL
- **全量断言场景 55/55 全绿，0 flake**
- BALANCE_MAP 生成器重跑幂等（两次结果一致）；未引用键 101→1（仅 `version` 元数据）
- 文档同步后残留扫描：6 文件无「47/51 assertion」「GDScript (existing」「pure GDScript」「no .NET」

## 6. 结论

- **U 系列修复主体可信**：U01–U06/U08/U09/U11/U12/U16 逐项属实；GameState 三服务 typed 化（U13 主体）属实；每帧 StringName 构造已清零。
- **验收声明 4 处偏差**（V8 批次修正）：U10/U13 统计漏计 Boss 发射链 20 处动态派发（本次 typed 化收口）；U07 名单外静态字段 8 处（本次清理 5 处 + Explosion 静态池登记）；U17 漏删死代码 2 处（本次删除）；U19 字段级注释未修（本次清理）。
- **多角度新发现**：热路径残留集中在遭遇组（已收口）；安全面 U15 修复引入溢出回归（已修复）；文档面 ARCHITECTURE 等 4 份主文档停在迁移前（已同步）。
- 无 P0；修复后全量 55/55 绿。审查报告归档，V 系列权威登记在 `docs/AUDIT_VAULT.md`。
