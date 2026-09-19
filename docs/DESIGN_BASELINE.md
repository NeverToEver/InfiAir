# InfiAir Design Baseline (DESIGN_BASELINE)

> 路径：`docs/DESIGN_BASELINE.md`（纯参考；找它 / 改它的场合见 `AGENTS.md` §1 路由表）。
> **玩法设计意图与定稿取值的单一权威**：数值 / 规则 / 系统边界定稿在这里，改设计只改这里。
> 只写现状与开放口径——根因、修法、验证过程、批次汇报属 git 历史；决策的「为什么 + 反转链」在 `docs/ROADMAP.md`；流程与门禁在 `AGENTS.md`；外部依据与出处（行业惯例、许可证）在 `docs/REFERENCES.md`。
> 系统行为不在此维护（以代码为准）。**小节号被代码注释按 `§x.y` 引用**，重排前先 `grep -rn "§" csharp/`；本文件不写变更史与日期。

## 1. Product & Gameplay

### 1.1 Positioning
单人 2D 俯视角弹幕射击（shmup）；Godot 4.7.2 .NET + C#（零 GDScript），GL Compatibility，1920×1080（`canvas_items`/`keep`）。重制自 `airwar-game`，现独立演进。
**平台定稿：PC 桌面专用**（Windows / Linux / macOS）；输入面只有键鼠与手柄两路（触屏虚拟控件 2026-09-11 全量退役，见 §1.14）。
**纯街机流**：无登录 / 无排行榜 / 无局外成长；score-only（无掉落物 / 装备）。分数不显示、不入记录，仅作隐藏进度引擎（敌机解锁 / Boss 节奏 / 事件门控 / 里程碑→天赋点）。
**开机链路**：`scenes/main.tscn` → 深空机库标题屏（`scenes/title.tscn`，形态见 §1.12）→ 新的一局 / **C 继续上次出击** / T 教程 / P 练习。
**本局存档**（2026-09-10 追加，口径见 §2.5）：回基地与选择「保存并退出」时落盘、死亡即删档、读档还原进度并从新一波开始。
**内容边界**：只解冻「记录 / 练习 / 表现」三类元层项（口径见 §1.16）；玩法系统扩展（新 Boss / 遭遇 / 天赋 / 机体）冻结，重启须显式决策（见 ROADMAP）。

### 1.2 Core Loop
```
manual fire (left mouse / RT; hold or toggle per `settings.json fire_toggle_mode`) + waves → milestone/boss talent points → cache spend (talent tree) → 4 rotating bosses + enrage
→ mothership supply/fire platform → return-to-base restock → same run continues
```
Endless（§1.4），无固定结局；终局 = **必死曲线**（玩家成长有界、敌方压力无界）。

### 1.3 Scoring & Economy
- **奖励随难度增长（双轨补偿）**：击杀分与事件奖励一律乘 `KillScoreFactor` = 1 + `reward_scaling.kill_score_ramp_factor`(0.15) × (D−1)，再乘难度档倍率（easy ×1 / medium ×2 / hard ×3，`difficulty.<tier>.score`）；精英炮塔、编队结算、迷雾存活补偿走同一因子（不再有只吃难度档的事件奖励常量）。依据（RoR2 货币同步通胀）见 `REFERENCES.md` §4.12。`GameState.AddScore(v)` 是分数唯一入口。
- **连击**：所有击杀分路径（普通 / 精英 / 分裂子机 / 编队机 / 精英炮塔）经 `AddKillScore(base)` 入账——combo+1 + 刷新窗口；得分 × `min(1 + (combo−1)×step, max_mult)`，窗口 **5.0s** / step **0.1** / 封顶 **×2.0**。断连三路：窗口超时、玩家受击、`ResetRun`。Boss 击杀与事件档位奖励**不计连击**；**擦弹吃难度与连击**（基础分 **30**，`graze_combo_weight` 1.0）——「擦弹 = 贪分」是弹幕系风险回报的支柱。
- **Boss 击杀**：`AddBossKill(scoreScale)` → `AddScore(500 × scoreScale)`（`milestones.boss_kill_base`）；推进天赋点 / RP / BossKills / 难度。
- **遭遇单位同口径**：编队机与精英炮塔的击毁都走 `AddKill()`（计击杀数、推进 kill 类任务与进度门）**并且**给一笔击杀分走 `AddKillScore`（吃连击与 `score_amp`）。炮塔击杀分 `elite_turret_event.turret_score`（**150**，独立于事件档位奖励 `reward_score`）；编队机沿用 `craft_score`（同一处入账）。
- **迷雾存活补偿**：四类迷雾是纯负反馈，故按难度给一份存活补偿——**扛满整段**（自然到期）给 `fog_events.reward_score`(150) × `KillScoreFactor`；**被打断**（返航 / 死亡 / 退出标题，经 `FogEvents.EndActive()`）**不发**（否则长按 B 蓄力即可命中 6~8s 窗口白拿）。两半互补。
- **RP**：只来自 Boss 击杀（+5）与任务领取（+3）；基地控制台消费，不带出本局。
- **RefreshPoints**：基地专用货币——进入基地 +1（`base_task.grant_per_visit`），刷新任务 −2（`base_task.refresh_cost`）；无上限、不带出。任务轮换：3 个活跃槽从 9 条池（`MISSION_POOL`，3 类 × 3 目标）无放回抽取；进度按 `kind`（kill/survive/boss）路由，轮换后仍推进；已完成未领取的槽刷新时保留。
- **里程碑**：分数阈值 → 天赋点入缓存池（无弹窗，见 §1.5）。

### 1.4 Difficulty & Endless Curve
- **难度映射单源**：D → 各敌方量的斜率 / 上限 / 地板集中在 core `DifficultyScaling` + `DifficultyScalingConfig`，服务层只注入配置，不散在 `Enemy`/`Boss`/`Bullet`/`Spawner`。
- **现状取值**（`balance.json`）：杂兵 HP **0.40** / 伤害 **0.20** / 速度 **0.10（硬顶 ×1.8）**；**Boss HP 0.55（独立斜率）**；敌机开火间隔随 D 缩短但保有 **1.2s 地板**；波次间隔走同一函数并保有 `spawner.interval_min`(2.5s) 地板；精英数量每 **2.0** D +1，上限 **3**。
- **时间项软上限**：`progression.soft_cap_start`(6.0) 之后超出部分按 `tail_speed_factor`(0.5) 折减。**D 无硬顶**（必死曲线为既定设计）——软上限只让「挂机也涨」那条放缓。
- **曲线公式**（core `Progression/`）：`mult = 1 + progression.per_boss_kill(0.6) × boss_kills + time`；时间项按 `time_step_seconds`(30s) 量化：`floor(run_time/30) × 0.075`（= `per_ten_minutes` 1.5 / 10min）；只计本局 `run_time`（树暂停与非本局场景不计）。`RecomputeDifficulty()` 统一入口（击杀 + 时间档 + 读档还原），广播 `DifficultyChanged`。
- **敌方成长**：Boss HP `×(1 + boss.hp_ramp_factor(0.55) × (D−1))`（独立于杂兵；50s 逃逸阀门 = DPS 检查）；杂兵/精英 HP `enemies.hp_ramp_factor`(0.40) / 伤害 0.20 / 速度硬顶 `speed_ramp_cap`(×1.8)；事件单位（炮塔 / 编队）HP × `GameState.EnemyHpRamp()`。
- **难度档成对口径**：分数倍率与里程碑阈值倍率成对给出（easy 1/1、medium 2/1、hard 3/1.5）。净点数节奏 = 分数倍率 ÷ 里程碑阈值倍率：easy **1×** / medium **2×** / hard **2×**。hard 的定位是「以更强的敌机（HP ×1.5 / 速度 ×1.2 / 刷怪间隔 ×0.8）换取同等成长节奏与更高荣誉分」，**不是更快成型**；要让 hard 真正最快须下调 `hard.milestone`。
- **生存**：`extra_life` 上限 **10**（HP 100 + 500 = 600）；吸血＝击杀回复 **基础上限 ×5%**（定额 5，core `LifestealHeal`）——伤害是定额，治疗不随 extra_life 抬升的当前上限复利；母舰召回冷却因子 **0.75**/级（60s→45s→34s），满补节奏不短于典型交战周期。
- **本局达成**：Boss 击杀 **10** 只或存活 **20 分钟**，**任一满足即达成**（`progression.goal_boss_kills` / `goal_survive_seconds`）；达成方式取**固定优先级**（Boss 击杀 > 存活）而非先到者——判定是当前状态的纯函数（core `RunGoal`）。**达成不终止本局**。
- **命名难度档位**：D 映射 6 档（巡航 / 接敌 / 高压 / 危险 / 临界 / 绝境），阈值 `progression.tier_thresholds` = `[1.0, 1.6, 2.4, 3.6, 5.5, 8.0]`；HUD 标签显示「难度 xN.NN · <档名> · <难度档设置>」（core `DifficultyTier`，单调不回退）。可见性：HUD 常驻目标进度（显示更接近达成的那条）、达成给横幅、结算页有「本局目标：已达成 / 未达成」一行。
- **Boss 阶段门控**：血量单调不增（受击只钳下界 0、永不上抬，致死一击走 `Die()` 不进狂暴）；「锁血」是**狂暴序列期间**的免疫（`EnrageSequence._healthLock`）而非受击回血，触发＝未触发过 + 存活 + 血量 ≤ `boss.enrage.hp_ratio` × 上限；**转阶段优先于狂暴**（单发跨 70% + 30% 双线时先转二阶段再判狂暴，否则跳过 P2 转场）。两条线由 core `BossPhaseGate` 判定（含非法输入护栏）。

### 1.5 Talent Cache System
- **Structure**：27 节点 = 4 大类 × 线（`csharp/core/Talent/TalentTree.cs` 是结构单源）。线序 = 前置链（下一节点需上一节点 ≥ Lv1）。节点上限 = `augments.<id>.max_stacks`（json 唯一权威；`extra_life` 10）。**节点规模与解锁口径只有 `max_stacks` 一条**——项目没有解锁条件机制（无 `unlock_*` 键）、没有权重池配置。
- **作战增幅 8 节点**（统一称「增幅 / Augment」：效果桥 `CombatStateService.Augments`、文本键 `AUG_*`、配置段 `augments.*`）：`homing` 制导航弹（出膛弹在锥内追踪最近敌机）、`salvo` 齐射重弹（每 N 发 ×3 伤害，层数缩短间隔）、`deflector` 偏导护盾（弹反冷却 ×0.78^eff、反射伤害 ×1.6^eff）、`second_wind` 背水回涌（受击后 3s 每秒 +3 HP/层）、`dash_strike` 相位冲击（冲刺触及敌机 35×层 伤害）、`graze_field` 擦弹力场（擦弹环 ×1.2^eff、擦弹分 +5/层）、`score_amp` 战果增幅（击杀分 ×1.08^eff）、`combo_guard` 连击护持（连击窗口 ×1.5^eff）。
- **点数与缓存池**：里程碑 +`talent.grant.points_per_milestone`(2)、Boss 击杀 +`points_per_boss`(1) → 有序缓存池。超额衰减：前 `safe_threshold`(**30**) 点满值，其后每位置 −`decay_step`(10%)、地板 `decay_floor`(10%)；LIFO（最新衰减最深），花费从尾部取。**正向回补**：每次花费后已衰减点朝满值抬 `recovery_step`(25%)——花掉点数本身即自救手段；读档还原不触发回补（快照即真实历史态）。
- **里程碑可见性**：HUD 缓存芯片下方常驻「距下一里程碑 %N」（`ScoreService.MilestoneProgress`，按**本档起点之后**的分数算，使进度条每档从 0 重走）+ 达成横幅；芯片 5 档色板按 `SafeThreshold` 运行时取值（0 灰 / 可升级金+呼吸 / ≤safe 琥珀+呼吸 / ≤safe+safe/3 警示黄 / 以上红）。**打开方式**：长按 `G` 或长按芯片蓄力（`talent.panel.charge_time`，松手 / 受击 / 其它模态取消）→ 满条打开 `TalentPanel`（树暂停）。
- **成本与递减**：下一级 = `cost.base`(2) + 当前级 × `cost.increment`(1)；每节点 `softcap`（默认 3）之后每级效率 = max(`diminishing.floor`(0.25), 1 − `diminishing.step`(0.25)×(k−softcap)) → 得分数有效层级，乘法消费方读 `TalentEffLevel`（= factor^effLevel），整数语义消费方（护盾层数 / 穿透 / 散射 / extra_life HP）保持整数级。
- **四套制衡机制**：**A 派系互斥**（offense↔defense，一方投入 ≥ `mutex.threshold`(5) → 对方节点上限 −2，本局永久）；**B 专注惩罚**（任一节点 ≥ `focus.threshold`(7) → 所有更低层级节点有效层级 ×(1 − min(0.4, 0.06×超出))，顶层豁免，面板页脚有指示）；**C 路线契约**（berserker/guardian/ranger；绑定免费一次、切换需重置令牌 `route.reset_token_cost`(6) RP；核心大类**已投入（Lv≥1）**节点 +1 有效层级、未投入节点恒 0、其余大类上限减半地板 1）；**D 风险加点**（已达上限的节点可 +1 级、成本 ×2，之后永久锁定；每局 ≤3）。
- **持久化与文案**：天赋态每局全新、跨局不持久（检查点对天赋态的快照口径见 §2.5）；卡片文案走 `AUG_%s_DESC` 键。**关键缩放键**：`rapid_fire.factor`(间隔 ×0.75/级)、`armor.multiplier`、`evasion.chance`、`regen.heal_per_sec`、`slow_field.factor`、`laser_beam.*`（线段而非弹体）、`explosive.*`、`dash_strike.tick_interval`(**0.12s**)、`mothership_recall.cooldown_factor`。
- **辅助瞄准**（`player.aim_assist`，档位 low/medium/high；**不给「关闭」档**）：`aim_marked` 出生时掷签（`mark_ratio` 0.25），标记只决定**框显示**与**框内强追踪**，不是弱追踪门槛——弱追踪（`NearestConeTarget`）覆盖全部敌机，锥角 / 强度随档位（6°/0.42、8°/0.52、10°/0.62），磁吸与弱追踪共用衰减（<400px 满值 → 1400px 降为 0.3）。**遭遇单位同属瞄准面**：编队机与精英炮塔实现同一「可瞄准目标」契约（存活 / 世界位置 / 框半宽 / 是否标记），弱追踪、框内强追踪、磁吸、标记框一律覆盖且恒为标记目标；框半宽沿用普通敌机的「碰撞半径 + 同一 pad」。**Boss 不参与辅助瞄准**（刻意留在契约外）。玩家弹速 `player.bullet_speed`（**2600**）：纵向 1080 约 0.42s、横向 1920 约 0.74s 横穿。
- **准星自定义**（样式从写死常量升级为玩家档案）——档案字段（范围与默认即定稿；非法读值钳回范围，不入界整档回默认）：`shape` bracket（默认）/ cross / circle / dot；`size` 0.5–3.0（基准＝bracket 外接半宽 14px、circle 半径 12px、cross 线长 12px）；`thickness` 1–6px；`gap` 0–20px；`alpha` 0.2–1.0（默认 0.95）；`rotation` 0–90° 步 15（bracket/cross 专用，45° 即 X 形）；`center_dot`（默认 true）+ `dot_size` 1–6px；`t_shape`（默认 false，cross 去顶线）；`outline`（默认 false，暖黑描边 +2px）；`state_tint`（默认 true；关闭后交战不变色，旋转 / 收拢等非颜色线索保留）；`color` RGBA（默认 255,194,77,242 主题琥珀）。交战反馈与自定义的合成：可攻击 / 锁定混合的**目标色**仍是主题定稿色（用户色只替换常态基色）；交战时长键 `effects.crosshair.*` 属全局手感、不随档案走。
  **持久化**（settings.json 两键）：`crosshair_profiles`（档案数组，缺键回默认）、`crosshair_active`（索引，越界 / 负回 0）；旧档缺键→种子 4 预设（默认括角 / 经典十字 / 圆环 / 净点），**不抬 `SettingsMigration.CurrentVersion`**（新键缺失即默认、老代码忽略新键，双向兼容）；「全部恢复默认」＝重建种子档案并激活 0（自建档不保留）。
  **准星码**：`INF1-` + 20 字符 Crockford Base32（5×4 组），12 字节载荷（版本 / 标志位 / size×20 / 粗细与中心点半字节 / gap / alpha×100 / rotation÷15 / RGBA / CRC-8），编解码与校验单源 core `CrosshairCode`；导入失败（前缀 / 字符集 / 校验和 / 版本过新 / 域界）明确报错不落档，成功＝新建档案并激活，名字不入码。设置页「准星」页：实时预览 + 参数控件 + 档案管理（切换 / 新建副本 / 重命名 / 删除，至少保留 1 档）+ 码的导出与导入。
- **母舰与基地**：驻留期火力平台 GATLING / MISSILE 交替；返航＝长按 B（`effects.home_charge_time`）→ 锁输入 → 停 spawner → 召回 → `starfield.Warp(18)` → 过场 → 基地 UI（树暂停）。基地 = `BaseConsole.cs` + `DawnStation.cs` 皮（左缘轮盘目录：机库 / 补给 / 契约 / 任务 + 「继续出击」叶；右区单面板切换），「继续出击」→ 轨道打击清场（Boss 保留）→ 入场动画。
- **增幅补给（基地↔增幅联动）**：补给面板 RP 购置——「增幅缓存」`cache_cost_rp`(4) RP → `cache_points`(2) 点直入缓存池（LIFO 口径同里程碑入账）；「超载槽」`overcharge_cost_rp`(8) RP → 本局风险加点上限 +1（至多 `overcharge_slot_max`(2) 档，`ResetRun` 清零）。
- **入场动画**（`player.PlayEntryAnimation()`）：俯冲至下三分之一 → 缓慢后漂；仅水平、垂直锁定、无敌（不闪烁）；怪与弹延迟刷新。

### 1.8 Events
- **精英炮塔**（heavy 30s）：母舰背景板 + 追踪炮塔（独立 HP / 开火 / 弱锁定），复用敌弹。与 Boss 互斥（`_bossFreezeDepth`/`_bossPending`，冻结为深度计数——两个事件各自持有时先结束者不提前解冻）；波次暂停（`_wavesPaused`），`boss_resume_delay` 后恢复。3 节点对白 + 通讯浮层；奖励 `reward_score` **900**（× 难度 × `KillScoreFactor`，全歼才给，超时 0）。触发：分数 ≥ `min_score`(800)、掷签 `trigger_chance`(0.35)/`trigger_interval`(45s)；`cooldown` 60s。事件条（标题 / 计数 / 身份色）与编队共用 `Hud` 一套实现。
- **编队突袭**（最低优先级）：3/4/5（按难度）楔形俯冲 → 90° 横转 → 波次化投弹 → 离场。**核心是「三种应对」的攻防**：炸弹可**击落**（`bomb_hp`，空中引爆 = 无地面伤害 + 拦截分）、可**弹反**（`IParryable`，反射后反向上升并轻量寻敌）、也可纯**闪避**。
  - **落点可读**：每枚炸弹自带落点圈（完整伤害半径轮廓 + 12 点起收缩的倒计时弧，**圈越少越亮**）；引信到期在弹体当前位置引爆，伤害按距离衰减（中心满伤 → 边缘 `bomb_edge_falloff`），边界即生效边界。
  - **结算三分支**（收益上限由操作决定）：**全数拦截**（投出的弹全被空中拆掉 + 编队机一架未坠）→ `reward_intercept` + 逐枚 `reward_per_intercept`；**全歼**（打光编队，含已投弹）→ `reward_all_clear`；放它离场 → 只有击坠得分。被引信引爆的弹与弹反后出界回收的弹**不计**拦截。
  - **投放必须落在可见区内**：投放点越出可见世界域的弹**不再生成**（与「已毁机跳过、时刻表照走」同族，也不计入投放数）。**口径：时刻表是「最多投几枚」的上限，实际投放数由可见区决定**；档位判定与 HUD 分母一律取实际投放数（否则全数拦截档结构性不可达，屏外爆炸边缘还会蹭到贴边玩家）。要让计划枚数全部可见须改横穿速度或投放起点。
  - **拆弹两条路径同额**：击落与弹反命中都给 `bomb_score` 走 `AddKillScore`（吃连击与 `score_amp`），并叠加同一笔逐枚 `reward_per_intercept`（`AddEventScore`，属事件奖励、不计连击）。
  - **投弹节奏**：`volley_batches` 波 × 每波按「到长机的距离」排序依次 `bomb_interval` 错开，波间 `volley_gap`；同机 `bombs_per_craft` 枚以 0.4s 连投。编队几何与时刻表单源 core `FormationPlan`（楔形槽位 / 名次定序 / 时刻表排序），引擎侧只按表消费。
  - **演出与可读性**：入场前屏顶打进场预告线（复用 `SpawnTelegraph`，换编队身份色，时长取 `spawner.telegraph_duration`）；投弹前 **0.45s 机腹灯闪烁预警**（落点圈说明「会炸到哪」，预警灯说明「谁要投」）；顶部事件条显示全程进度与「编队机 x/y · 已拦截炸弹 n」；通讯序列 = 接敌警告 → 战术提示 → 至多一条进度台词 → 结算台词。
  - **触发与互斥**：触发策略只由 `GameEventManager` 读取与判定，事件自身只报「就绪」；资格合成与计时推进下沉 core `EncounterTrigger`（含「资格不足时计时冻结、到点先复位再掷签」）。事件**同时占用波次槽与 Boss 槽**；收场（含返航打断）一并解除并补触发一次期间到期的 Boss；`Abort()` 连同在场炸弹一并清除且无结算。
- **迷雾事件**（轻干扰，独立于 spawner 链）：概率掷签（`fog_events.trigger_chance`/`check_interval`）、`first_delay` 开局保护、`min_interval` 冷却、显式 `duration` 自动清除、组内单活跃。4 类（按权重抽取，无优先级链）：`fake_enemies` 无伤害幽灵机、`mental_confusion` 输入反转 + 染色、`bullet_malfunction` 角度抖动 / 哑火 / 开火间隔抖动、`direction_shift` 周期性强制位移向量。返航与死亡清除；存活补偿见 §1.3。
- **优先级链**：Boss → 精英炮塔 → 编队突袭（遭遇组互斥，触发门控 = spawner processing；互斥判定单源在 `GameEventManager`）；迷雾独立触发（不占波次槽、不与 Boss 互斥）。统一注册表在 `GameEventManager`（`GameState.Events`），遭遇启动只走 `StartEncounter`。
- **确定性口径**：迷雾按概率触发（首延迟后每 3s 掷签），会污染同一局的模拟态——需要确定性回放的场合要么关掉自动触发，要么走可注入取值源。

### 1.9 Meta HUD
- 全屏 FX CanvasLayer layer=1（世界之上、HUD layer=2 之下）；`meta_health.gdshader` + `hint_screen_texture`。
- 管线：受击层（CA + 径向模糊）→ 方向性涟漪 → 去饱和冷调 + 晕影 → 裂纹（Voronoi 预烘一次；headless 用低分辨率）。
- 状态机：NORMAL/CAUTION/DAMAGED/CRITICAL/DYING（0.75/0.50/0.25/0.20）；快下（tau 0.10）、慢回（tau 0.80 + 错峰）；DYING 心跳 1.0–1.2Hz、呼吸 ±1.5%、HUD 抖动 ±2px、FOV −6%。
- 显式（`SegmentedBar`）与隐式（去饱和 / 晕影 / 心跳）两层；`reduce_flash`：CA ×0.4、停呼吸 / 抖动 / 心跳（音效保留）。
- 亮度代理取自注册表（弹 ×0.002 + 爆炸 ×0.15），零 GPU 回读；LOD1 跳过 CA / 模糊 / 涟漪。

### 1.9.1 左下集成仪表盘
左下角＝**按元素真实特性选形态**的单面板仪表带，两排：上排生命（主读数，独占整行）、下排燃料量槽 + 两枚充能槽 + 弹仓格 + 坞态指示灯。**形态判据**：形态差异必须表达「量是什么样的量」，这是与「技术展示品」的分界。

- **生命 → 分段横条**：主生存资源、最常扫视；占上排整行、字号最大最亮，是整块面板的视觉主导。
- **燃料 → `FuelTank` 量槽**：持续消耗的预算，只回答「还剩多少」→ 液位即读数（侧缘 4 段刻度给量程、不写数字）。液面起伏双向钳制（`TankLiquid.WaveAmplitude`）：不越内腔底（防填充多边形自交、三角化失败整块不画）也不越液位上方余量；满油与见底附近波幅收窄至零。
- **冲刺 / 弹反 → `AbilitySocket` 充能槽**（同一构件两个实例）：充满即可用、用掉清空、冷却回充 → 环形冷却（一眼回答「能不能用」）。三态即读数：充能中＝环按进度填充 + 字形压暗、就绪＝点亮 + 满环 + 一次性外扩脉冲、未解锁＝压暗 + 锁定横杠。**不用指针表盘**（本盘没有需要读速率的量）。
- **弹仓 → `CartridgeStrip` 分立格**：离散计数 → 独立药槽（连续条会读成比例）。
- **母舰坞态 → `AnnunciatorLamp` 灯**：配色语义取自 **14 CFR 29.1322** 三级分类——红 warning（此项目前唯一用途＝弹仓见底）/ 琥珀 caution（蓄力 / 下降 / 在场 / 冷却）/ 绿 safe operation（就绪待命）。
- **形状语汇单源**：所有方形构件一律走 `UITheme.ChamferPoints` 切角八边形——「每元素一套隐喻」正是读作展示品的根因。
- **液体纪律**：**静止几乎不动、变化时才动**——常驻液面约 2px 低频起伏（0.32Hz），数值变化时叠一次衰减晃动；不做气泡。
- **单源**：`FuelTank` / `AbilitySocket` / `AnnunciatorLamp` / `CartridgeStrip`（`Control` + `_Draw` 程序化绘制，零贴图零 shader），装配与位置单源在 `Hud.BuildInstrumentCluster`；母舰灯态 `Main.DockStateValue`；**燃料低量警戒线 core `FuelGauge.WarnRatio`**（液色与刻度同阈值）。
- **无障碍**：全套动效按 `ReduceFlash` 递减或冻结；**全屏尺度脉冲频率与减闪处置单源在 core `FlashBudget`**（单测断言频率 < 3Hz 且减闪下振幅为零），**一次性闪光**（能力槽就绪脉冲 / 血条掉段闪 / Boss 阶段闪）无频率、不进频率表，但抑制处置同样单源（`OneShotFlashId` / `AllowsOneShot`，两半互补：减闪下一律抑制、非减闪下一律放行），站点不留本地布尔副本。
- **文案**：三个小标题复用既有键（`UI_FUEL`/`UI_DASH`/`UI_PARRY`），**不新增玩家可见文案或数字**（数值只用形状 / 颜色 / 液位编码）。成本纪律：仅液槽常态逐帧（≤12 点液面 + 4 条刻度线）；充能槽静止 `SetProcess(false)`。

### 1.10 Cinematics
- 返航：7 镜头 11.8s，2.35:1 信箱；Esc 走 `RETURN_SKIP`（1.2s 宽限 `effects.return_skip_grace`）；落在基地 UI（树暂停）；第 7 镜头 BGM −40dB。
- 共享工厂：`CinematicFx.cs`（`SoftGlow`/`Particles`/`Shockwave`/`Beam`/`RadialStreaks`；驱动 `_Process` 零堆分配）、`DawnStation.cs`（全息虚影态）。

### 1.11 Tutorial
独立场景 `scenes/tutorial.tscn`，自管返回（不走 `BackNavigator`）；隔离本局态与存档，退出还原 `Engine.TimeScale = 1`。**七阶段**（实战与母舰停靠之间是弹反教学段），与正局对齐：阶段 1 强制标记靶、阶段 5 走生产停靠链。

- **阶段表单源在 core**（`csharp/core/Tutorial/`）：七阶段的标题键 / 目标键 / 目标形态 / 目标计数 / 需插值的动作名一处写定（`TutorialCurriculum`），阶段内进度与达成判据在 `TutorialProgress`；godot 层只做适配（刷怪布局、信号接线、取值来源）。
- **文案跟随绑定与数值**：每一处「按某个键」取**实际绑定**（`GameState.ActionKeyText`；鼠标开火与手柄扳机是固定绑定按定值写）；目标行数字由 core 计数经文案补参给出，蓄力秒数取 `effects.home_charge_time`、狂暴阈值取 `boss.enrage.hp_ratio` 读数——改键与改平衡值文案自动跟。
- **不卡死**：进度记在 `settings.json tutorial_stage`（进阶段写、完成清零；标题屏在该值 > 0 时把入口提示换成「继续教程」，`tutorial_done` 语义不变）；死亡后短暂提示并重开**当前**阶段（清场、重置进度、重刷目标）；长按 `give_up` 1 秒跳过当前阶段（屏上常驻提示），Esc 随时退出。
- **敌机配置与正局同源**：均经 `Spawner.MergeTypeInto` / `MergeTypesInto` 把 `enemies.types` 覆盖进默认表，不得直读未合并的默认表；读取面只许 `Spawner` 内部调用。
- **弹反教学段（阶段 4）**：弹反是唯一防御机制，首局玩家没有理由知道它存在。段内保有固定数射击型靶机，敌弹朝玩家发射、可弹反；窗口内成功弹反 **N=2** 次即过关（沿用实战段锁血口径不判负），靶机被击落或离场按保有数补刷。盾的几何判据在 core `Combat/ParryShield`（半径与弧边都取等号）；逐帧扫描补判留在 `Player._PhysicsProcess`——`area_entered` 只在进入重叠那一刻投递一次，而重叠自「盾半径＋弹体半径」起算，落在两半径之间的弹只被拒一次就再无机会。
- **增幅 / 天赋轻量触点（阶段 6 基地段）**：返航开基地后，目标行点明增幅与天赋面板的键，实际打开一次增幅面板即过关；教程内自建同款面板（读生产增幅数据），零新经济链。
- **按键提示设备感知**：目标行与跳过提示的键标签按**最近使用的输入设备**取档（口径见 §1.14）；设备切换时重渲染的是**当前正在显示的那一行**（阶段目标行 / 后续目标行 / 蓄力替换行三分支）。
- **回归面**：教程不进无头门禁（门禁只跑生产 `main.tscn` 开机链路）；改动后按需手跑 `godot --headless --path . --fixed-fps 60 --quit-after <帧数> --scene res://scenes/tutorial.tscn`。

### 1.12 Exit/Back Navigation
- 所有返回输入 → `BackNavigator.GoBack()`，经纯函数 `DecideBackAction()`（确认 → 过场跳过 → 设置/基地/阻塞页/结算 → 增幅坞 → 暂停 → 顶层 → 战斗）。
- 栈：L3 退出确认 → L2 覆盖层（设置 / 基地 / 结算 / 过场）→ L1 本局（HUD ⇄ 暂停 + 增幅坞）。标题屏 `title.tscn` 为独立场景：程序化星空 + 远景实况战场（敌机编队 / 远处爆炸 / Boss 剪影）+ 玩家机自远处跃迁飞入右侧悬挂展示（轮廓背光 / 尾焰怠速 / 铭牌卡）+ 左侧标题区，开场演出约 2.2s 不阻塞输入。
- **圆盘 UI 全覆盖**：左缘 `RadialWheel`（圆心锚屏外左侧，卡片沿弧排列；槽距按选项数自适应 `SlotAngleFor`，端点角钳 ±36° 防压 HUD；全容弧面时键盘 / 滚轮经 `FocusBias` 移动聚焦项）是全站菜单导航面——天赋面板、暂停、死亡结算、基地目录、设置页导航；`RadialMenuLayer` 为统一开合骨架（`SetWheelActive` 同步轮盘与 chrome 遮罩显隐，非模态页须显式关遮罩）。方向键旋转 + 确认键按下（GUI 焦点存在时自动让位焦点链）。
- 战斗退出：二次确认（丢进度警告）；`ExecuteExitCleanup`：存设置、停音效、淡出退出。**战斗退出不删档**——删档只发生在死亡与放弃重开（存档语义单源见 §2.5）。
- Esc / 手柄 `ui_cancel` 同一状态机。

### 1.13 Combat Fairness
- **受击喘息窗口**：玩家受击后 `dda.duration`(5s) 内，敌机与 Boss 的**开火间隔** × `dda.factor`(1.3)（只做「减弱打你的火力」）。**波次间隔不吃该因子**（整局推进不该被喘息拖慢）；「扩为真自适应」明确不做。
- **Grace frames**：敌弹在判定框内时推迟结算 `player.grace_period`(0.05s)，只作用于「敌弹→玩家」时序。**离场判定**：窗口内离场按弹心相对轨迹段（入口→离场；圆心参考系两端同减抵消玩家移动）最近距 ≤ 核心半径（7 × `world_scale` = 2.8px）判定——贯穿核心＝视觉直击照常吃伤害，仅擦边入框才免伤（高速弹穿越核心约 25ms，「离场即免伤」会让直击永不结算）；到期仍在框内同样结算。
- **擦弹**：判定框外有擦弹环（`player.graze_radius` 20，gameplay-range 家族、不吃 `world_scale`）→ `graze_score`(**30**，× 难度 ramp × 连击加权)，每弹一次，框内不给分。玩家受击判定仅经 `Player/Hitbox`（r = 7 × world_scale = 2.8）；机身 r=22 不参与碰撞（mask=0）。
- **阶段转场**：P1→P2 与狂暴都清全场弹（含编队炸弹）+ 短暂无敌（`boss.phases.transition_invincible` 1.0s，只加不减）；逃逸不清弹不给无敌。Boss 血条分段（P1 琥珀 / P2 橙 / ENRAGE 红，自左消耗），**段界由 `boss.phase2_hp_ratio` / `boss.enrage.hp_ratio` 派生**（core `BossBarSegments`），不得另存硬编码——改阈值而段界不跟，玩家从血条读出的阶段边界就是假的。
- **弹反（F）**：360° 圆，窗口 0.5s（`player.parry.active_time`，总时长 0.8s＝前摇 0.15 + 窗口 + 收招 0.15）；反射 = 镜像 y 翻转 ×2 速度（`reflect_speed_mult`）×1.5 伤害（`reflect_damage_mult`，四舍五入）转为玩家弹；硬冷却 3.0s 自效果结束起算（3.8s 循环）；键位 `LT`。

### 1.14 Input Surface（PC 专用）
- **两路输入：键鼠 + 手柄**。**移动端不做**（产品范围决定，非技术限制）：触屏输入全量退役——虚拟摇杆 / 按钮层、触屏瞄准基准与差值累积分支、触屏开火键、设置开关、Android 返回手势路径均不存在，`Player.AimPoint()` 只剩光标绑定一条路径。工程面不留触屏分支，重新引入须先推翻本条。
- **键盘**：`project.godot` 的 `[input]` 是可改键动作的唯一默认源；改键 / 恢复默认只擦写 `InputEventKey`（冲突键从占用者移除，占用者其余绑定保留）。开火不进可改键表——键盘侧无绑定，由 `EnsureFireBinding` 运行时装配鼠标左键。
- **手柄**：`BindJoypadDefaults` 运行时装配（左摇杆移动 / 右摇杆瞄准 / A 冲刺 / RB 加速 / LB 微调 / X 停靠 / Y 返航 / R3 放弃 / L3 增幅面板 / LT 弹反 / RT 开火），`project.godot` 不承载手柄事件；死区走 `settings.json joy_deadzone`；PS 布局只改标签不改位置语义。
- **轮盘 UI**：方向键 / 摇杆旋转、确认键按下；GUI 焦点存在时方向键让位焦点链。**标题屏入口手柄可达**：底部「教程 / 练习」为**可聚焦按钮**（dpad / 摇杆移焦点、A 确认；点击入口自身区域激活该入口，其余区域与键盘 T/P/C/任意键照旧开局）。
- **按键提示设备感知**：提示里的键标签按**最近使用的输入设备**取档——键鼠档取键名（`ActionKeyText`），手柄档取按钮 / 扳机 / 摇杆标签。档位判定下沉 core（最近者胜，默认键鼠），设备变更经 `InputDeviceChanged` 广播、已渲染的行即时重取；无手柄绑定的固定键（天赋面板 G）回落键名；摇杆轴事件带 0.2 噪声门限（漂移常在 0.05–0.15，无门限会把提示档一路掰回手柄档），零位移鼠标事件不计。同一取值口供 HUD 增幅面板标签与设置页「操作速查」（速查键名不再含字面量）。

### 1.15 Settings
- **信息架构：五页**，左缘轮盘与左侧导航同源（同一份页表 `SettingsUi._pageDefs` 驱动页目录 / 内容构建 / 文案，增页只改一处）：
  - **控制**：可改键表（动作清单唯一事实源 = `GameState.REBINDABLE_ACTIONS`）· 手柄分组（布局 / 灵敏度 / 死区）· 重置区（恢复默认按键 / 全部恢复默认）。
  - **游戏**：难度 · 开火方式 · Ctrl/Shift 模式 · 辅助瞄准档位。
  - **显示与性能**：视角 · 窗口模式 · 分辨率 · 帧率上限 · 垂直同步（含实际状态读出）· 当前帧率与显示器刷新率 · 鼠标锁定 · 画面增强。
  - **音频**：主音量 · 音乐 · 音效。
  - **辅助与关于**：减少闪光 · 屏幕震动强度 · 命中顿帧强度 · 动效强度 · 高对比弹体 · 版本与操作速查。
- **生效时机**：一律即时生效（无「应用」按钮）；滑杆拖动即时改值，持久化只在 **`DragEnded` 与离开设置页时各一次**（逐帧全量原子写盘是磁盘写风暴）。
- **持久化版本**：`settings.json` 带 `version`（当前 4），读入时真读。版本一致直读、低于当前按旧键名迁移、**高于当前按「整份档按空档读入」回退**（每字段取默认值，不拿当前语义去读未来同名键；告警照发、不改写文件）；判定在 core `SettingsMigration`。
- **破坏性动作必二次确认**：恢复默认按键、全部恢复默认都经确认弹窗，文案写清后果。
- **状态可见**：开关下方给出当前状态读出（垂直同步实际值可能被驱动覆盖，与偏好不一致时明示）；屏幕震动强度与命中顿帧强度 0% 都单独文案说明「已关闭」，避免 0% 读成「最小」。
- **无障碍定稿范围**：减少闪光（频闪）× 屏幕震动强度（运动）× 命中顿帧强度（瞬时定格）× 可改键 × 音量三分路。屏幕朗读（XAG 106）与字幕**不做**——本作无配音与叙事对白。
- **有意例外**：辅助瞄准不给「关闭」档（常驻机制，仅弱 / 中 / 强三档）；改键表不含开火 / 天赋面板 / 暂停 / 重开（开火键盘侧无绑定，天赋面板与重开为固定键，暂停走引擎 `ui_cancel`）。
- **默认值取向**：中立、低风险（亮度 / 音量类不做极端档；震动 / 闪光 / 顿帧默认开启但一键可关；语言默认简体中文）。

### 1.16 发布阶段新增口径（记录 / 练习 / 表现）
玩家可见内容只解冻「记录 / 练习 / 表现」三类元层项；玩法系统扩展（Boss / 遭遇 / 天赋 / 机体）冻结。实现细节以代码为准。

- **本局记录（`user://best.json`）**：跨局保留的**局末结果读数**——存活时长 / Boss 击杀 / 最高难度档 / 本局目标是否达成（标题屏与结算页各一行）。边界：**不含战力、不含分数**（计分仍只是隐藏进度引擎），故不构成局外成长；与 `run.json` 分区，死亡删档不影响它，读取三态口径同本局存档；写入＝本局终结（死亡 / 放弃重开），仅在实际有改善且盘上记录可读时落盘；字段编解码单源 core `BestRecordCodec`（写读共用同一批键名）。
- **练习模式**：从标题屏与结算页进入，可直选 Boss 型别（4 型）、起始难度档与遭遇事件。边界：**独立于本局**——不写 `run.json`、不写本局记录、不推进里程碑与难度、不影响任何进度门；Boss 与遭遇一律走生产触发链（资格与门槛仍须真正通过）。三处进度写入点（`SaveRun` / `DeleteRunSave` / `RecordRunResult`）各一条 `PracticeActive` 早退，「练习不落盘、死亡不删档」是结构性的；练习局的 RunTime / 难度 / 里程碑照常在内存推进（手感与正局一致），只是永不落盘。
- **音乐分层**：Boss 战与基地备有独立曲目（`bgm_boss.wav` / `bgm_base.wav`，同一程序化管线、可复现）；曲目选择下沉 core 并单测钉住，`MusicDirector` 只做播放与淡入淡出。**标题屏无 BGM**（无音频节点），只有画面运动。
- **高对比弹体**：辅助页开关（**默认开**），给敌弹加**形状 / 描边**编码（不靠色相），落实「关键信息不得仅由固定颜色传达」。边界：纯表现层、玩法判定零改动；「游戏速度可调」**不做**（速度即难度、与必死曲线耦合），登记为有意例外。敌弹轮廓取**亮色**——本作背景中位亮度 9/255，深色描边等于没画，无障碍编码必须是亮度编码；关闭开关时贴图逐位退回原档（映射在 core `BulletAppearance.SkinFor`，单测钉住）。

---

## 2. 视觉与表现系统（战术琥珀）

> 本节所有条目的共同硬约束：**纯表现层，玩法判定零改动**。闪烁 / 提亮类动效一律按 `ReduceFlash` 递减或停用；新增振幅受「动效强度」（§2.12）缩放。

### 2.1 色板（`csharp/godot/UITheme.cs` = UI 调色板单源）
**UI 调色板（按钮 / 面板 / HUD / 文字 / 全息线光）唯一来源是 `UITheme.cs`**；改此一处 = 全站 UI 换色。**场景文件（`scenes/*.tscn`）内嵌的 VFX / 粒子 / 自发光配色以场景文件为准**（编辑器内嵌资源，代码无法统一注入）；C# 侧若需与这些颜色一致，必须引用 `UITheme` 常量而非再写字面值。金属按钮 / 面板贴图为近白灰度 + 预烘焙倒角，色相全部由 tint 派生（`assets/sprites/ui/metal_streak.png`、`button_plate*.png` 未重绘）。

- **主交互琥珀** `Accent #FF9F1C`：按钮 / 焦点 / 进度 / 边框；`AccentHot #FFC14D` 受激提亮；`AccentDim` 琥珀 22% 分隔线。
- **全息琥珀** `Holo #FFC861` / `HoloPale #FFE6BF`：仪表 / 舱段 / 站体 holo 线光与热读数。原「数据青」通道退役（冷青与琥珀主色互相打架），全站收归暖族。
- **稀有金** `AccentGold #E8C170`；**危险红** `Danger #FF3B4E`；**成功色** `Success #C2D16B`（暖橄榄金）。
- 底：`BgDeep` 暖炭黑 / `PanelBg` 暖炭 / `PanelSteelTint` 暖暗钢；文字 `#EEE7DC` / `#9C9184`。面板垂直渐变与金属 / 输入框 / 滚动条 tint 一律暖偏。
- 暖钢 tint：`SteelTint` / `SteelTintHover` / `SteelAccentTint`；焦点环取 `AccentHot`。
- 次级局部色板同步收编：`RadialWheel`、`DawnStation`、`AimCrosshair`/`AimFrameLayer`、`MothershipSummonWindow`、`TalentFanView`、`TitleScreen`/`Tutorial` 底色、`Hud` Boss 分段、`MetaHealthFX` 裂纹带、返航过场与玩家侧（母舰 / 跃迁门 / 轨道打击）同属暖族。
- **弹幕可读性**：玩家弹 = 白热芯 + 琥珀晕；敌弹 = 红 / 品红（不与琥珀 UI 混同）。玩家机能量 / 尾焰 / 激光 / 残影 / 增幅附件统一琥珀；Boss 预警色（telegraph）保留多色编码，属玩法信号不作统一。

### 2.2 世界层后处理
GL Compatibility 后端官方对照表把 Glow/SSAO 标为**支持**（不支持 2D/3D HDR 渲染、compute shaders、`CompositorEffects`、particle trails、MSAA 2D、debanding 等，对照与出处见 `REFERENCES.md` §4.7）；引擎给不了「辉光＋调色＋晕影＋颗粒＋动态战斗分级」这一整套组合，故整套手写屏幕纹理后处理（**是否改用引擎辉光属观感决策，未纳入当前口径**）：

- `assets/shaders/world_grade.gdshader`（单趟）：亮部提取 → 旋转网格环采样柔光（quality 档：单环 4 tap / 双环 8 tap）→ 暖调分级（gain+lift+S 曲线对比）→ 常量晕影 → 胶片颗粒。
- `csharp/godot/WorldPostFx.cs`（`CanvasLayer` layer=1）：`hint_screen_texture` 全屏 ColorRect。**层序靠树序**：世界(layer0) → 增强 → `MetaHealthFX` → HUD(layer2)；Meta 采样含辉光结果。接入 `main.tscn` / `title.tscn` / `tutorial.tscn`（教程 HUD 抬至 layer=2）。
- 性能纪律：关闭时整层 `Visible=false`（零 GPU）；`reduce_flash` 颗粒置零 + 辉光减半；静态参数启动一次、动态参数 epsilon 检测。
- 数值 `data/balance.json effects.world_post.*`；设置页「画面」段开关 `SET_WORLD_POST_FX`（持久化 `world_post_fx`，默认开），走 `SettingsService` → `GameState` 信号广播。

### 2.3 战机与战场表现
暖钛 / 青铜钢甲 + 琥珀能量语言的战机贴图（`scripts/tools/generate_player_sprite.py`，暖描边与暖缘光走 `sprite_polish.py` 的可选覆盖，敌方晶体族用模块默认冷色、输出逐字节不变）；尾焰走 `CinematicFx.SoftTexture()` 软点 + 白热→琥珀→暗橙色阶；敌 / 精英 / Boss 用同源贴图副本 + 加性材质 + 阵营染色剪影（贴图未改）；爆炸 ramp 深琥珀、玩家激光 / 枪口 / 残影 / 受击指示琥珀；`ChamferedPanel` 四角琥珀刻度 + 顶缘受光线。
**不变式**：玩家机**画布 254×254 与头部锚点严格不变**（`PlayerAugmentVisuals` 按 `BaseShipScale=0.65` 对位锚点）。

### 2.4 过场演出细节
**共享乘员构件 `CrewFigure.cs`**：宇航服分件（头盔 / 面罩 / 颈环 / 胸甲 / 维生背包 / 肩甲 / 护膝 / 手套 / 战术靴 / 状态灯）；**关节契约逐位不变**（返回 `{node,hips,knees,shoulders,elbows,torso,eyelid}`，步行 / 握姿 / 呼吸相位公式与眼睑特写照旧）。**舱段模块**（`DawnStation` 共享）：环体舱段加舷窗灯带 + 装甲分缝 + 端盖条 + 散热格栅，返航 / 基地背景同享。

### 2.5 本局存档
单存档位 `user://run.json`（与 `settings.json` 分区；复用 `SaveManager`/`SaveStore` 原子写 + 损坏隔离）。跨局的局末结果记录 `user://best.json` 见 §1.16——两者分区，死亡删档不影响它。

- **模型：存档 = 可继续的检查点**，三条规则覆盖全部边界：
  1. **写入 / 覆盖**：退出确认「保存并退出」+ **回到基地**（`Main.OnReturnFinished`）自动落盘，新档覆盖旧档。
  2. **终结删档**：**死亡**（订阅自身 `PlayerDied`）与**「放弃重开」**（`RestartRun`）——检查点作废，不可读档回滚（保住必死曲线、防 save-scum）。**删档失败不静默**：死亡路径若删除失败，兜底把 `run.json` 覆写为墓碑内容（版本号置不可读态）。
  3. **非破坏性**：标题屏「**新的一局**」**不删旧档**（一次误触不抹进度）；旧档保留到被新档覆盖或被本局终结清除。`ExitToTitle` 同样不删（Tutorial 走此口）。
- **读档**：标题屏 `HasRunSave()` 为真时显示 `C — 继续上次出击`；`C` 置 `GameState.PendingLoadRun`，读档成功则跳过 `ResetRun`，否则回退全新一局（保留旧档）。读取结果**三态区分**：无存档 / 损坏隔离（移为 `.corrupt`，不显示假承诺）/ 暂时不可读（文件存在但读不出）——后者按「稍后重试」处理（不当作没存档、也不删档）。
- **粒度**：**还原本局进度，战场从新一波开始**——持久化 score/kills/boss_kills/combo/milestone_count、run_time/difficulty_multiplier/dda_timer/difficulty_time_step、health/augments、talent(levels/overcharged/route/reset_tokens/bonus_overcharge_slots/cache_values)、missions(rp/refresh_points/条目/last_kind_value)；**不持久化**敌机 / 子弹 / Boss 位置、波次计时、回血延迟、TaskPool 游标、无敌 / 受击帧守卫。连击数持久化，读档给予**满连击窗口**（存的是段位，不是剩余时间）。`dda_timer` 属持久化项（键写读成对）；若改判为不还原，须连键一起从写侧移除。
- **还原顺序**（`GameState.RunSave.cs`）：talent → combat（先恢复 `extra_life` 层级才有正确 MaxHealth）→ score → progress → missions；各服务末尾补发既有信号驱动 HUD 与 Player 增幅件重建。**`DifficultyChanged` 尤其不能漏**——HUD 难度标签只在信号与语言变更时刷新（无轮询），漏发会停在旧值直到下一次时间档跨步（最长 30s）。
- **健壮性**：档案带 `version`（=1），不符按无存档忽略（不隔离不阻塞开机）；损坏 JSON 走既有隔离。**JSON 往返会把 `StringName` 键退化为 `String`**，还原时对 `augments`/`missions` 键做 StringName 归一化（否则查表落空、增幅与任务进度静默失效）；所有字段判型读取，非法回默认。
- **还原必须过白名单与上限**（手改档是可达输入面）：`missions` 只收 `MISSION_POOL` 里的 id（goal 取池内定稿值——读存档里的 goal 会让查池得 0、`IsMissionDone` 恒真，可反复领取 RP）；`talent_overcharged` 只收已知节点、去重并按本局过载名额截断；`augments` 只收天赋树已知节点且层级 ≤ 上限（**风险加点节点例外**：层级 `max_stacks+1` 是正当形态，必须保留）；`key_bindings` 只收 `REBINDABLE_ACTIONS` 里的动作名。过滤策略下沉 core 纯函数。

### 2.6 性能设置
- **帧率上限** `fps_cap`：**九档** `30 / 45 / 60 / 120 / 144 / 165 / 180 / 240 / 不限制`（默认 **60**）——档表单源 `SettingsService.FPS_CAP_LEVELS`/`FPS_CAP_ORDER`（键 `fps30/…/unlimited`，`unlimited` 值为 0）。生效值写入 `Engine.MaxFps`。
- **垂直同步** `vsync`：默认**开**。生效值写入 `DisplayServer.WindowSetVsyncMode`（headless 跳过窗口 API）。开启时实际帧率再受显示器刷新率钳制；关闭可降输入延迟。
- **应用时机**：`GameState._Ready` → `LoadSettings()` 后显式调用 `ApplyDisplaySettings()`（无设置文件时 load 不应用，故补默认档）；设置页切换即时生效 + 落盘 + 广播 `DisplaySettingsChanged`。档位非白名单值忽略、保持默认。

---

### 2.7 战场纵深与材质表现
纵深背景（`DeepSpaceBackdrop.cs`，z=−5）：远景行星 / 残月、中层残骸带、近景尘埃三层视差，贴图走确定性 PIL 管线（`generate_backdrop_sprites.py`）。**材质级能量层**：机体材质 shader `ship_energy.gdshader`，生成器额外导出 `*_glow.png` 发光遮罩（R=霓虹走线 / B=引擎喷口，基础贴图逐字节不变），运行时 additive 叠加。爆炸四件套（白炽核心闪帧 / 装甲碎片 / 烟尾 / 双层冲击环）；弹体加宽出弹尾渐变拖尾 + 双层辉光（玩家弹呼吸脉冲、敌弹只提亮不脉冲——可读性优先）；Boss P2 变身碎片炸散 + 能量层切狂暴配色；标题字标由 `generate_logo.py` 生成。

**星野**：星云贴图 = core `NebulaField` 能量场（环面无缝值噪声云 + 脊状细丝 + 暗尘带；**无缝性与确定性由单测钉住**——贴图接缝是冒烟与截图都不判的静默坏点）；冷青层降为点缀（alpha 0.55，暖琥珀主导）；星点逐星亮度与色温差分（暖琥珀 12% / 冷蓝白 6%，每层 1 次 draw）；亮星三层（光环 + 微旋衍射芒 + 软核）。**星数 / 种子 / 滚动与战况耦合语义不变，闪烁仍走 `FlashBudget`**。数值落 `effects` 段（`backdrop` / `explosion` / `ship_energy` / `thruster_core` / `boss_p2_transform` 等）。

### 2.8 动效与可读性强化
动效常量一律 `private const` 就地定义（不新增 balance 键）；一切闪烁 / 提亮脉冲按 `ReduceFlash` 递减或停用。

- **共享动效设施**（`UITheme`）：`FadeIn` / `AnimateClose` / `AnimateModalClose` / `PunchScale` 工厂；HUD 硬编码色收归 token。**模态退场契约**：`AnimateModalClose` 调用当帧即断开输入处理与鼠标命中、置遮罩穿透，仅把视觉渐隐留给 tween——残影不截获已交还下一层的输入（重新打开时调用方须复位 `SetProcessInput` 与遮罩 `MouseFilter`）。
- **常驻动效语汇**：界面（暂停描述卡、设置页切页、破坏性确认统一页壳模态、退出确认、结算统计行、通讯浮层）一律淡入 / 错峰 / 弹一下；标题屏「任意键」确认动效后再切场景（0.2s，重复输入由 `_started` 挡回）；标题战场与基地的装饰动效（编队机尾焰与翼尖灯、Boss 巡航漂移、天赋扇形悬停、基地分类滑动、黎明站怠速自转、教程阶段横幅）不改步骤逻辑、时序与文案。
- **HUD 可读性**（`Hud` / `SegmentedBar` / `HudChargeBar` / `HudDamageArcs`）：血条残影段（仅玩家血条）、屏幕边缘方向受击弧（仅显示）、低油量 / 就绪 / 满蓄力脉冲、事件条与 Boss 血条淡入淡出与掉段补闪、增幅格高亮、击杀数弹跳。**未新增任何玩家可见文字或数值**（局内计分显示已裁，不复活连击 / 任务计数）。
- **战斗即时特效**（`CombatVfx` + `VisualFxDirector`）：击杀环、Boss 阶段冲击波与狂暴放射爆发、弹反金环 + 碎片、冲刺发射环 + 反向拖尾、直击火花与暴击星芒（暴击复用既有单次 RNG 结果，不重掷）。轻量特效静态在活计数封顶 24、总监侧封顶 10，超限跳过；特效根按 `world_scale` 缩放、tween 自毁。
- **动态分级**（`world_grade.gdshader` / `meta_health.gdshader`）：`Engine.TimeScale < 1`（狂暴子弹时间）驱动暖调增对比 + 晕影升温的平滑热档；重击（`ScreenShake` 强度阈值）脉冲泛光 / 晕影；冲刺驱动既有径向模糊通道的速度模糊；高质量档加二级宽晕。**中性态逐位等于原静态调色**（uniform 为 0 时不产生永久观感偏移），额外采样仅在档位非零时发生，空闲零 GPU 路径不变。

### 2.9 打击感层（命中顿帧与震动 trauma）
- **取值定稿**：四档顿帧 **0.03 / 0.07 / 0.11 / 0.16s**、冻结倍率 **0.06**、trauma 参考振幅 **24**、衰减 **1.5/s**。反转＝改值或由玩家在辅助页调强度。
- **时间缩放合成器**（`GameFeelService`）：`Engine.TimeScale` 原有唯一写入者是 Boss 狂暴子弹时间。两路各自直写会互相覆盖（顿帧把子弹时间顶回 1.0，或反之），故合成收口——Main 只上报演出倍率（`SetEnrageTimeScale`），实际写入 = 演出倍率 × 顿帧倍率；终态复位统一走 `ResetTimeScale`。**暂停一并复位**：`SetTreePaused(true)` 走单一复位口连冻结与演出倍率一起清（只清顿帧会让整棵 Always UI 以 0.24 倍速播放，表现为「菜单卡死」）；恢复后 Main 在慢速段续报倍率。
- **命中顿帧分档**：普通命中 / 暴击（复用既有单次暴击 RNG 结果）/ 击杀 / 重击（玩家受击）四档，时长在 `effects.hit_stop.*`（**`freeze_scale` 是冻结期间的时间倍率，不是强度**——求严格 >0，取 0 会让帧长恒零、剩余时长永远推不完而冻死）。同帧多次请求取**较大者**不叠加。推进用**反解出的真实帧长**（`delta ÷ 上一帧写入的倍率`）。
- **震动 trauma 模型**（Eiserloh 惯例，出处见 `REFERENCES.md` §4.12）：震源只累加创伤值，位移 = 最大振幅 × `trauma²`（`CameraShake` 每帧采样）；小额近乎无感、大额才猛烈，高频抖动不叠成持续晃动。衰减与顿帧同时序按真实帧长推进。震源表在 `effects.shake.*`，语义是**创伤增量**（非像素振幅），另有 `recovery`（衰减速率）与 `reference`（归一化参考振幅）。
- **设置项**：辅助页「命中顿帧强度」（0..1，0 = 完全关闭；按比例**缩时长**而非改冻结倍率——倍率逼近 0 会让帧长趋零、采样与输入一起失真），与「减少闪光」「屏幕震动强度」并列，`HitStopScale` 落 settings.json。**三项互不连坐**：屏幕震动强度只作用在**运动**（trauma 与像素位移），广播给画面增强层的震源强度取原始值——滑块关掉震动不该连带关掉重击泛光。
- **单源**：时序与 trauma 数学在 `csharp/core/GameFeel/`（`HitStopTimeline`/`TraumaShake`），单测钉住（含「零真实帧长不得结束顿帧」）。

### 2.10 时间基准：模拟时间与真实时间
**两套时钟，各管一类量，不得混用。** 模拟时间＝受 `Engine.TimeScale` 缩放的 `delta` / `Engine.GetPhysicsFrames` / `GetProcessFrames`（固定步长下帧数＝模拟时长，可重复）；真实时间＝墙钟 `Time.GetTicksMsec` 等。

- **划线标准（一句话）**：问这个计时点等待 / 限制的是「引擎之外的现实世界」（人的手、输入队列、声波、机器耗时）还是「游戏世界内部已推进多少」——前者真实时间，后者模拟时间。常数若以「人类秒」为单位（1.2s 宽限、0.5s 守卫、250ms 节流），通常是真实时间的信号。口径单源在 `AGENTS.md` §4，本文件不复述。
- **两套都要的理由**：所有计时统一成模拟时间，会让「人机窗口 / 混音节流 / 性能读数」在时间缩放或回调停摆时被拉伸乃至永不结束（返航输入宽限即此：按住 WASD 秒跳过过场的实例）；统一成真实时间则判定依赖机器速度、探针不可复现。
- **各基准归属**：真实时间＝返航过场输入宽限 / 标题屏输入守卫、音效最小触发间隔、开机耗时读数、设置页帧率与刷新率读出、打击感的真实帧长推进、**音乐播放位置（节拍相位，仅视觉层消费、绝不进判定）**；模拟时间＝帧级去重与缓存（`FrameCache` 等 `GetPhysicsFrames` 族）、事件 / 波次编排时间轴、表现层脉动相位、右摇杆准星积分（`GetProcessFrames` 门控 + `GetProcessDeltaTime` 配对）。
- **维护方式**：**无自动判据**——原「真实时间允许清单」门禁随探针宿主退役（2026-09-18），新增计时点须按上述标准人工核对归属；`csharp/core/` 出现任何真实时间命中都应视为缺陷（纯逻辑层可单测的前提）。

### 2.11 数值单源的两道判据
- **代码默认值不得与 json 分叉**：一批「表式」数值以 `Cfg(key, 代码内默认值)` 的回退实参手抄进 C#——json 完整时读 json，看不出代码默认值已分叉，只有 json 缺失 / 损坏才回退到错值。改动任一侧（json 定稿值 / 表内默认值）必须**同时**改另一侧。
- **读取面收口**：未经 balance 覆盖的默认表（`Spawner.BuildEnemyTypes` / `BuildEliteTypes`）只许 `Spawner` 内部读取，外部直读即违规（教程曾如此，是「正局跟随、教程不跟」的根因）。
- 上述两条**无自动门禁**（原对账脚本随 2026-09-18 门禁裁剪退役），改数值或改动读取面时人工核对。

### 2.12 动效语言与节奏层
主题是**节奏与响应**：屏上最常被注视的三样（敌弹、背景、舰体能流）此前要么不动、要么各按自己的相位漂移。

**六条准入判据**（逐批过；前两条来自 XAG 118 官方定义，是硬约束不是口味，出处见 `REFERENCES.md` §4.7）：

1. **全屏层不得构成「闪」**：XAG 118 把「闪」定义为 ≥10% 亮度变化，且失败判据里**面积（约 20% 屏）与频率（约 3 次/秒）并列**。占屏 ≥20% 的元素只允许**峰谷亮度差 <10% 的慢呼吸**；**拍点脉冲一律下放到局部元素**（弹体尾部 / 舰体流光 / 仪表 / 亮星，屏占远低于 20%）。
2. **全屏周期脉冲频率**按 §1.9.1 登记 `core FlashBudget`（<3Hz、减闪归零）；新增行的频率取自 balance 的 `effects.motion.breath_hz_max`，改它越阈值即单测判红（BPM 三档另由整拍频率单测钉住）。
3. **最亮＝最危险**：特效核心不得超 80% 白，背景层 ≤25%；新特效不得改变判定几何、不得遮盖弹体轮廓与前摇预告——**拖尾不得比判定活得久**。
4. **单一同步源**：全屏尺度的同步脉动只由 `WorldPostFx` 一处实现（`world_grade.gdshader`，解析式零额外采样），其余元素只做相位从属；常驻动效继续遵守 §1.9.1 的「静止几乎不动、变化时才动」。
5. **节拍基准**：相位取自音乐播放位置（属 §2.10 的真实时间面），整拍一脉冲（战斗曲 120BPM＝2Hz、Boss 曲 150BPM＝2.5Hz、基地曲 84BPM，均 <3Hz）；**半拍只允许局部元素**。无音乐时（加载失败 / 无头 / 曲目缺失）降级为模拟时间按默认 BPM 推进，保证无头确定性不变。
6. **可减弱**：本轮新增的一切动效统一受「动效强度」缩放，0 ＝ 回到本批次之前的画面；与「减少闪光」「屏幕震动强度」「命中顿帧强度」**互不连坐**。

**动效强度（设置项）**：键 `fx_intensity`（settings.json，0..1，默认 **1.0**，步进 5%），落设置页「辅助与关于」页，形态与屏幕震动强度 / 命中顿帧强度同族（0% 文案明示「已关闭」）。语义＝**缩放振幅，不改频率**；取值口与钳制单源在设置服务，消费方一律经它取。

**数值与单源**：新增数值一律落 `data/balance.json effects.motion.*`（呼吸频率上下限与振幅、弹尾拍点增益、三档 BPM、入场与速度线等）；节拍相位、拍点包络与呼吸频率映射等算式下沉 `csharp/core/Visual/` 并配单测。

**现行构成**：节奏底座（`Visual/Rhythm` + `FlashBudget` 全屏呼吸行）· 全屏呼吸与拍点（`VisualRhythm` 取样音乐播放位置，`world_grade` 单点实现）· 敌弹尾部能量动效（`bullet_energy.gdshader`，只作用于低 alpha 尾段）· 舰体事件流光（擦弹 / 弹反 / 受击 / 连击里程碑各扫一次，经 `AllowsOneShot`）· 敌机损伤分级（能量层乘子）· 入场落位与编队相位波 · 背景战况响应（速度线、星野滚动随难度、行星自转）· 界面互动性（控件皮肤、UI 音效族、HUD 数值滚动）。

### 2.13 机体姿态与物理反馈层
主题是**机体物理感**（横移侧倾 / 开火后坐 / 受击位移 / 击毁残骸）；判据沿用 §2.12 六条，各动效均为局部元素，不触碰全屏硬线。**四构成**（只写贴图节点 Rotation/Position/Scale，机体根节点与判定几何不动；振幅受 `fx_intensity` 缩放，0 ＝ 本批之前的画面）：

① 玩家机姿态：横移侧倾（瞄准角之上叠加 banking，机头朝移动方向偏，指数平滑）+ 开火后坐（贴图向机尾微退、指数回位；连发间隔短于 3τ 时表现为持续微沉）。
② 敌机与 Boss 姿态：敌机速度走帧间差分（移动策略直写 Position），速度方向明确时贴图转向移动方向、低于 `enemy_face_min_speed` 按比例回正机头；Boss 横移缓慢侧倾（根节点转 π，符号与玩家机相反）。
③ 受击推挤：沿弹道方向微移回弹（世界方向经 `GlobalRotation` 逆变换到贴图本地，同一式通用于根转 π 的敌机 / Boss 与炮塔）；可选契约 `IPushableDamage`，玩家弹直击经 `EntityDamage.Dispatch` 重载传入方向；溅射 / 激光不推挤，入场中不推。
④ 爆炸照明与残骸：大半径低亮度暖光斑随爆淡出（峰值 alpha <0.15、约 13% 屏宽，低于 XAG 118 的 20% 面积线）；**不对单位本体做 Modulate 提亮**（该面已有受击白闪 / 损伤分级 / 入场色三路写者）。elite/Boss 档击毁留燃烧残骸（慢旋缓坠、烧红渐暗到炭黑），片数随 `fx_intensity` 缩放。

**数值与单源**：`effects.motion.*`（`player_bank_*` / `player_recoil_*` / `enemy_face_*` / `boss_bank_*` / `hit_push_*` / `explosion_light_*` / `wreck_*`）；姿态算式（侧倾目标角、帧率无关指数逼近、后坐力与推挤包络、朝向跟随的根旋转换算与钳制）单源 core `Visual/BodyPose.cs` 并配单测。振幅与时长为定稿取值（弹幕系通行量级 + XAG 118 面积线约束）。

### 2.14 输入回应层
主题是**输入回应**：补「玩家自己的请求被系统拒绝时」的回应——「命中无声（silent actions）」与「丢按键」是手感杀手（出处见 `REFERENCES.md` §4.8）。输入缓冲只把「就绪前一小窗内的按下」延到就绪帧生效，不改冷却 / 燃料 / 伤害任何数值。**三构成**：

① 命中与弹反确认音：玩家弹命中敌机补轻量金属 tick（频次最高的交互，此前只有击杀有爆炸音）；弹反成功改用专属确认音（唯一防御机制，音色须与「移动」区分）。两枚音效纯合成（零随机流）；`SfxId` 追加 `Hit` / `ParrySuccess`（五张表下标对齐）。命中音最小间隔 **45ms** + 复音上限 **2** 防逐帧刷屏，拦截在 `SfxPlayer` 目录表。
② 动作拒绝回应：弹反 / 冲刺在冷却中或燃料不足时按下——对应能力槽播一次**否认脉冲**（**向内收拢**的危险色弧，与就绪脉冲的外扩互为反向手势；登记 `FlashBudget.OneShotFlashId.AbilityDenyPulse`，减少闪光下不播，抑制后环形冷却读数仍在）+ 低音 `UiDeny`；反馈经 Player 挂暂存标志、HUD 每帧轮询消费。
③ 输入缓冲：冷却收尾 ≤`player.input_buffer_window`（**0.1s**）内的弹反 / 冲刺按下在就绪帧自动生效（判定单源 core `Combat/InputBuffer.cs`）；**只缓冲时机类锁定（冷却），不缓冲资源类锁定**——燃料不足时按下即拒绝（「燃料一到就自走一格」是意外行为），缓冲触发时重验燃料并取当前输入方向。

**取值与单源**：缓冲窗口 0.1s、命中音 45ms / 复音 2 为定稿取值；窗口改 `balance.json` 一处即调，音效限频改 `SfxPlayer` 目录表一行即调，否认脉冲时长为就地 const。

### 2.15 全息面板投影感
基地控制台「虚影皮肤」的全息面板从「半透明钢板」改为「投影光」语汇：全息读感是**半透明体积 + 像素 / 扫描质感 + 自发光**，是光不是拉丝金属（出处见 `REFERENCES.md` §4.9）。交互逻辑、页面结构、操作路径零改动。**三构成**：

① 发光边缘：`ChamferedPanel` 新增 opt-in `HoloEdge`（切角轮廓双层外晕 + 内缘体积亮线），同时**退役钢板受光语汇**（顶缘受光带 / 底侧阴影 / 拼板缝 / 铆钉——「被顶部光源打亮」与「自发光投影」语义冲突）。默认关，钢板面板（页壳 / socket / 弹仓）外观不变；切角点序收敛回 `UITheme.FillChamferPoints` 单源。
② 投影材质与扫掠：`ApplyPhantomPanel` 关拉丝钢填充、开 HoloEdge 与内容裁剪（基地页板与母舰召唤小窗同族）；`HoloPanelFx`＝静态扫描线（每 4px 一条，无闪烁）+ 周期下扫柔边亮带（tween 只动 position/alpha，不触发重绘）；`HoloBoot` 叠一次性显影带（0.32s 自顶向底）。
③ 减少闪光统一口径：基地既有装饰动效（标题数据抖动 3Hz、慢扫描带 8s）统一受 `ReduceFlash` 约束——停播并复位静息态，恢复时整程重启不跳变；扫掠 / 显影带天生受控。可见性两层合成（页面切换走 Control 显隐、整层显隐走 CanvasLayer 手工驱动），非活跃时不留冻结的半程亮带。

**取值定稿**（依据＝行业惯例 + 实机截图比对；改动位置＝`HoloPanelFx`/`ChamferedPanel` 常量一处）：

| 项 | 值 | 依据 | 代价 |
| --- | --- | --- | --- |
| 外晕双层 | 8px@14% + 4px@30% | 6px@10% 实机读不出「发光」 | 边缘外溢 ~4px，面板间距 ≥4px 不蹭邻件 |
| 内缘体积线 | 22% alpha | 菲涅尔式内缘增亮，弱于核心线一档 | 无 |
| 常态扫掠带 | 峰值 10%、6s 一程 + 3s 空窗、渐变 0.9s | 慢到读作环境投影噪声而非事件 | 每页一条循环 tween（隐藏 / 抑制即杀） |
| 显影带 | 峰值 35%、0.32s、高钳 24–48px | 一次性事件须比常态亮一档才读得出「正在成形」 | 开页 0.32s 内压内容一次 |
| 常态带高 | 面板高 8%，钳 56–110px | 太窄读成硬线，太宽洗掉内容 | 无 |

### 2.16 机体活性与运动跟随
主题是**连续运动的活体感**（机体对加速、转向、悬停、冲刺起手的连续反应）。依据（出处见 `REFERENCES.md` §4.10）：动画十二原则对程序化运动同样适用，「follow-through 决定印象」「idle 是基准不可晃」，且**响应优先于粘滞**——全部只写贴图节点变换与尾焰参数，机体根节点、判定几何、操控与弹道零改动。**五构成**（受 `fx_intensity` 缩放振幅、不改频率；算式单源 core `BodyPose`）：

① 运动滞后漂移：贴图朝加速度反方向漂移（加速时被甩在后面），逐轴钳制 + 指数平滑回中（`BodyPose.LagTargetPx`；加速度取 Player 帧间速度差分换算到机体本地系）。
② 转向跟随：贴图旋转相对瞄准角带可回弹角惯性——甩准星时短暂落后再追上、静止时逐帧回正（`BodyPose.SwayAfter`；累积-衰减-钳制）。
③ 悬停浮动：慢速微幅正弦沉浮（幅度远小于滞后量级）。
④ 冲刺弹跳：冲刺起手缩放 1→1+amp→1 抛物线过冲回拉（`BodyPose.PopScale`；渲染帧推进，以 Init 捕获的设计缩放为基准）。
⑤ 尾焰油门平滑：三态档位（怠速 / 巡航 / 加速）切换由硬跳改指数平滑 + 升档瞬间短促增亮 kick（就地 const）；**降档不 kick**；rate ≤ 0 或动效强度 0 直取目标（＝旧硬切换行为）。

**取值定稿**（依据＝动画十二原则 + Sakurai Motion（§4.10）+ 量级锚定 §2.13 姿态键；改 `effects.motion.player_{lag,sway,bob,dash_pop,thruster}_*` 一处即调）：

| 项 | 值 | 依据 | 代价 |
| --- | --- | --- | --- |
| 滞后漂移 | 4px / 回中 10s⁻¹ | 与后坐力 2.5px 同量级再强一档（连续量 vs 事件量） | 贴图中心偏离判定点 ≤4px；判定不动 |
| 转向跟随 | 0.18rad / 回正 10s⁻¹ | 与侧倾 0.14rad 同量级再强一档；>0.2rad 会让人误读弹道方向 | 甩准星时枪口辉光与贴图短暂错位 ≤0.18rad，弹道判定不变 |
| 悬停浮动 | 1.6px / 0.6Hz | 幅度 < 滞后一半；频率贴着呼吸下限（0.5Hz）——「活着」但不成「晃」 | 无 |
| 冲刺弹跳 | +6% / 0.16s | 过冲量低于尾焰外层直径的视觉噪声线；时长约为后坐力 3τ 的一半 | 冲刺前 0.16s 贴图放大 ≤6%；残影首两帧继承 |
| 尾焰油门平滑 | 9s⁻¹ + kick 35%/0.15s | 收敛 ~0.25s 读作「拉动油门」而非迟滞 | 档位切换 ~0.25s 到目标态；尾焰是装饰层，不占输入面 |

### 2.17 机身反馈与损伤状态
主题是**「机体在被驱动、在被伤害」的读数**。依据见 `REFERENCES.md` §4.11：RCS / 微调推力器「按所需方向点火」（真实航天器惯例）、状态可读性（损伤要一眼读出）、muzzle flash 让每次射击「violent and immediate」。只写贴图节点及其子节点的变换 / 调制与尾焰 / 烟粒子参数，机体根节点、判定几何、操控与弹道零改动。**六构成**（受 `fx_intensity` 缩放）：

① 开火机身光：每次开火机体被枪口火光照亮一瞬（暖向提亮，与后坐力共用计时、~3τ≈0.21s 衰减）——射击的重量落在机体上，而非只有枪口辉光。
② 速度伸缩：沿机头拉伸 / 压缩（前飞拉伸、倒退压缩，交叉轴按 0.5 体积补偿）。
③ 受击压缩：中弹瞬间贴图横向鼓、纵向扁，指数回弹（0.7 交叉轴补偿）。
④ 损伤状态：重伤档（受击帧等级 2）开启**损伤烟**（暗灰软点自机身后方飘散）与**引擎喘振**（尾焰 alpha 不规则抖动）——「引擎失稳」是可见的生存读数。
⑤ 机动喷口：左右 / 机首三枚 RCS 小喷口按机体本地系加速度反向点火（向右加速 → 左喷口亮；制动 → 机首反推亮），余量随该轴加速度占比。
⑥ 航行灯：左舷红 / 右舷绿慢呼吸常亮（错相）+ 尾部白色双闪——机体「通电待机」的常驻生命感。

**数值与单源**：`effects.motion.player_{fire_light,stretch,hit_squash,sputter,damage_smoke,nozzle}_*` 十键；算式（速度伸缩符号与钳制、交叉轴体积补偿、确定性喘振）单源 core `BodyPose`（受击与开火的时间包络复用既有 `RecoilFactor`）。

**取值定稿**（依据＝§4.11 惯例 + 量级锚定 §2.13/§2.16 键）：

| 项 | 值 | 依据 | 代价 |
| --- | --- | --- | --- |
| 开火机身光 | +35% / τ0.07s | 亮一瞬即可读，与「最亮＝最危险」不冲突（局部、非危险色相、~0.2s 衰减完） | 连发时机身维持微亮（7 发/s 下不到 3τ 即再置位）——读数即「持续射击」 |
| 速度伸缩 | ±8% / 10s⁻¹ | 按 §2.16 冲刺弹跳（+6%）略强——连续量压过一次性量才读得出 | 前飞时贴图较判定长 8%（判定不动）；残影继承 |
| 受击压缩 | 12% / τ0.10s | 与爆炸 / 受击类反馈同量级（§2.13 推挤 3px 同档）；τ 与后坐力同级 | 中弹瞬间贴图形变 ≤12%，≤0.3s 回位 |
| 损伤烟 | 排放比 0.6（仅重伤档） | 状态可读性：重伤要「看上去就危险」；0.6 而非 1.0 留档位差 | 烟雾遮挡机体下缘一部分（ZIndex −2 垫在机体后）；重挡 12 粒 ×1.1s |
| 引擎喘振 | ±35% @7Hz | 幅度同油门 kick（35%）、频率取弹幕节奏量级；确定性双正弦（无随机源） | 重伤档尾焰亮度持续波动 |

**航行灯与机动喷口为就地 const 档**（周期 1.8s 双闪、呼吸 0.38Hz、喷口 13 与舷灯 10 为贴图像素——§2.8 口径：小动效不新增 balance 键），仅喷口亮度上限与航行灯亮度上限随动效强度缩放（键 `player_nozzle_alpha`，航行灯取 fx 直乘）。

**挂点单位口径**（本层与 §2.18 的机身挂点共用，单源注释在 `PlayerVisuals.MakeHullLight`）：挂在贴图节点下的挂点，**局部单位就等于贴图像素**（`0.65 × world_scale` 已含在贴图缩放里），照 `generate_player_sprite.py` 的锚点注释写贴图像素即可；**再乘一次 `world_scale` 会把世界缩放叠加两遍**。反向提醒：`_sprite.Position` 与挂在 `Thruster` 等不缩放父节点下的量是**世界像素**，那侧必须显式乘 `world_scale` 才随机体等比——两处坐标系看着像、写法相反，混用不报错。

**挂点坐标单源**：全部挂点（机动喷口 / 航行灯 / 损伤烟，§2.18 的机炮与散热口）收在 core `PlayerHullLayout`，引擎层只换算与写节点，不在调用点重写第二份数值。护栏＝该表同名单测：每个挂点在出厂 `world_scale = 0.4` 下的世界直径须 ≥ 2px、两盏舷灯间距须 ≥ 20px（破坏验证：把舷灯写回叠加两遍的旧值，两条判据分别红在「只有 0.52px」与「相距仅 1.66px」）。

---

*玩法设计意图修订唯一入口；历史修订轨迹见 git 历史。*
