# InfiAir Design Baseline (DESIGN_BASELINE)

> **玩法设计意图的单一权威**：数值/规则/系统边界定稿在这里，改设计只改这里。
> 系统行为不在此维护——以代码为准；方向决策与已知债务见 `docs/ROADMAP.md`。

## 1. Product & Gameplay

### 1.1 Positioning
Single-player 2D top-down shmup; Godot 4.6.2 .NET + C# (full migration 2026-08-08, zero GDScript), GL Compatibility, 1920×1080 (`canvas_items`/`keep`). **平台定稿：PC 桌面专用**（Windows / Linux / macOS），输入面只有键鼠与手柄两路（2026-09-11 触屏虚拟控件全量退役，见 §1.14 与 ROADMAP 决策）。**Score-only** (no drops/pickups/equipment). Remade from `airwar-game`, now independent. 2026-09-08 纯街机流：无登录/无排行榜/无局外成长；开机 = 深空机库标题屏（星空 + 远景实况战场 + 玩家机跃迁飞入悬挂展示 + 铭牌；按任意键开始新的一局 / **C 继续上次出击** / T 教程）→ 开局；对局内分数不显示不记录，仅作隐藏进度引擎（敌机解锁 / Boss 节奏 / 事件门控 / 里程碑→天赋点触发）。**本局存档**（2026-09-10 追加，见 §2.5）：回基地与选择「保存并退出」时落盘、死亡即删档、读档还原进度从新一波开始。

### 1.2 Core Loop
```
manual fire (left mouse / RT; hold or toggle per `settings.json fire_toggle_mode`) + waves → milestone/boss talent points → cache spend (talent tree) → 4 rotating bosses + enrage
→ mothership supply/fire platform → return-to-base restock → same run continues
```
Endless (§1.4), no fixed ending; endgame = **inevitable-death curve** (bounded player growth, unbounded enemy pressure).

### 1.3 Scoring & Economy
- **奖励方向定稿（2026-09-14，人类决断）**：**双轨补偿**——高难敌强，但资源同步增长（对齐 RoR2 `moneyCost × coeff^1.25`）。
  三档难度定位据此明确：easy/medium/hard 改的是敌强度与得分倍率，不改「战力供给」轴；
  hard 的净点数节奏与 medium 相同（3/1.5 = 2 = 2/1），以更高荣誉分与更强敌机作为区分。
- **奖励随难度增长**（2026-09-14）：击杀分 ×`KillScoreFactor`（`reward_scaling.kill_score_ramp_factor`(0.15) 定形，= 1 + 0.15×(D−1)）。
  **统一口径**：精英炮塔与编队结算奖励、fog 存活补偿也走同一个 `KillScoreFactor`（= 1 + 0.15×(D−1)），再乘难度档倍率；不再有「只吃难度档」的事件奖励常量。
  目的：D 抬高敌方 HP 时收入须同步增长，否则单位时间收入被 HP 膨胀稀释（对齐 RoR2「货币同步通胀」双轨惯例）。
  缩放判定在 core `RewardScaling`（单测钉住单调不减与非法输入不倒扣）。
- **擦弹吃难度与连击**：擦弹基础分 30，并入连击乘区（`graze_combo_weight`(1.0)）与难度乘区。
  弹幕系惯例是「擦弹 = 贪分」，不吃乘区会让风险回报失真、不鼓励贴弹。
- **连击窗口 5.0s**：窗口须覆盖最大波间隔（4–7s 的波次节奏），否则跨波必断、×2 不可达。
- **迷雾事件存活补偿**：四类迷雾是纯负反馈（无奖励、easy/hard 同受），故按其难度给一份存活补偿——
  **扛满整段**（自然到期）给 `fog_events.reward_score`(150) × `KillScoreFactor`，再经 `AddScore` 乘难度档倍率；
  **被打断**（返航/死亡/退出标题，经 `FogEvents.EndActive()`）**不发**——打断＝没扛满，「存活」补偿不该兑现
  （否则长按 B 蓄力 1.5s 即可命中 6~8s 的窗口白拿一笔）。回归面：`--fog-probe` 断自然到期**金额相等**、
  `--fog-interrupt-probe` 断打断后增量为 0，两半互补（只判一侧会让「一律发／一律不发」混过）。
- `GameState.AddScore(v)`: multiplies difficulty (easy ×1 / medium ×2 / hard ×3；玩家文案 易/中/难，配置键 `difficulty.<tier>.score`); all kills route here.
- **Kill combo**: all kill-score paths (`Enemy.Die` 普通/精英/分裂子机、`FormationStrikeEvent` 编队机) route via `GameState.AddKillScore(base)` — combo+1 + window refresh; kill score × `min(1 + (combo−1)×step, max_mult)` (window **5.0s** / step 0.1 / max ×2.0), then difficulty mult as usual. Break: window timeout (no kill in 5s), player hit (`PlayerDamaged`, DDA same source), `ResetRun`. Boss kills (500×scale via `AddBossKill`) / event rewards do NOT combo；**擦弹吃连击加权**（`graze_combo_weight` 1.0，2026-09-14 起）。 怒首领蜂/虫姬链式得分的温和版: 普通玩家稳态 ×1.2~1.4, 高手封顶 ×2; 受击=降档(DDA)+断连双通道, 均不致命.
- **遭遇单位同口径**（2026-09-17 裁定）：编队机与精英炮塔的击毁都走 `AddKill()`（计「击杀」数，推进 kill 类任务与进度门）**并且**给一笔击杀分走 `AddKillScore`（吃连击与 `score_amp`）——此前两者都不计击杀数、炮塔连分数都不给，与「所有击杀都经 `AddScore`」冲突。炮塔击杀分取值 `elite_turret_event.turret_score`（**150**，独立于事件档位奖励 `reward_score`）；编队机沿用 `craft_score`（同一处入账，不再由事件编排侧代给）。
- Boss kill: `AddBossKill(scoreScale)` → `AddScore(500 × scoreScale)` (`milestones.boss_kill_base`); advances talent points/RP/BossKills/difficulty.
- RP: earned from boss kills (+5) and mission claims (+3) only; spent at base console, not carried between runs.
- **RefreshPoints**: separate base-only currency — entering base +1 (`base_task.grant_per_visit`), refresh tasks −2 (`base_task.refresh_cost`); no cap, not carried between runs (run save). Task rotation: 3 active slots drawn from 9-mission pool (`MISSION_POOL`, 3 kinds × 3 goals) without replacement; progress routed by `kind` (kill/survive/boss) so rotated ids still advance; completed-but-unclaimed slots kept on refresh.
- Milestones: score thresholds → talent points into the cache pool (no popup; §1.5).

### 1.4 Difficulty & Endless Curve
- **难度映射单源**（2026-09-14 收口）：D → 各敌方量的斜率、上限与地板集中到 core `DifficultyScaling`（+ `DifficultyScalingConfig`），
  服务层只注入配置；曲线形状可被单测/探针断言，不散在 `Enemy`/`Boss`/`Bullet`/`Spawner` 各处。
  现状取值：杂兵 HP 0.40 / 伤害 0.20 / 速度 0.10（**硬顶 ×1.8**）；**Boss HP 0.55（独立斜率，不再等于完整 D）**；
  开火间隔随 D 缩短但保有 **1.2s 地板**；波次间隔走同一函数并保有 `interval_min` 地板；精英数量随 D 增长（每 2.0 D +1，上限 3）。
- **时间项软上限**：`progression.soft_cap_start`(6.0) 之后时间项超出部分按 `tail_speed_factor`(0.5) 折减。
  **D 仍无硬顶**（必死曲线为既定设计），只是让「挂机也会涨」的那条在软上限后放缓——必死点更多由打法而非挂机时长决定。
**Endgame (D1)**: inevitable-death curve. 公式落地 `csharp/core/Progression/`（`DifficultyCurve`/`MilestoneCurve`）与 `data/balance.json`。
- `mult = 1 + progression.per_boss_kill(0.6) × boss_kills + time`. Time: quantized by `progression.time_step_seconds` (30s), + `progression.per_ten_minutes` (1.5)/10min → `floor(run_time/30) × 0.075`; counts live `run_time` only (tree-pause and non-run scenes excluded); quantization pins HUD/tests.
- No hard cap. `RecomputeDifficulty()` unified (kill + time tier + save-restore); broadcasts `DifficultyChanged`.
- Enemy growth: Boss HP `×(1 + boss.hp_ramp_factor(0.55)×(D−1))`（独立斜率，非完整 mult；50s-escape DPS check = "can't kill → flees" valve）; `enemies.hp_ramp_factor`/`damage_ramp_factor` (k=**0.40** HP / 0.20 dmg); 波次间隔有 `interval_min`(2.5s) 地板；速度有 `speed_ramp_cap`(×1.8) 硬顶。取值口径见 §1.4.1。
- Survival: `extra_life` cap **10** (HP 100+500=600); card "max 10"; lifesteal ≤10% feedback offset by HP cap + ramp.
- Event units scale: turret/formation HP × `GameState.EnemyHpRamp()`.
- **D2**: 难度档的分数倍率与里程碑阈值倍率**成对**给出（easy 1/1、medium 2/1、hard 3/1.5）。
  净点数节奏 = 分数倍率 ÷ 里程碑阈值倍率：easy **1×**、medium **2×**、hard **2×**。
  hard 的定位是「以更强的敌机（HP ×1.5 / 速度 ×1.2 / 刷怪间隔 ×0.8）换取同等的成长节奏与更高的荣誉分」，
  不是更快成型（3/1.5 = 2 = medium 的 2/1）。若日后要让 hard 真正最快，须下调 `hard.milestone`（属玩家可感取值，需人类确认）。

### 1.4.1 难度曲线取值定稿（2026-09-14，人类认可）
下列取值经人类认可为**定稿**，反转需显式改值（口径与算法在 core `DifficultyScaling`）：
杂兵 HP 斜率 **0.40** / 敌方伤害 **0.20** / 速度 **0.10 且硬顶 ×1.8**；Boss HP 斜率 **0.55**（独立于杂兵）；
精英数量每 **2.0** D +1（上限 **3**）；敌机开火间隔地板 **1.2s**；波次间隔地板沿用 `spawner.interval_min`(2.5s)；
时间项软上限起点 **6.0** 后按 **0.5** 折减（D 无硬顶，必死曲线不变）。

### 1.4.2 本局达成与命名难度档位（2026-09-14 人类决断）
- **本局达成目标**：Boss 击杀 **10** 只或存活 **20 分钟**，**任一满足即达成**（`progression.goal_boss_kills` / `goal_survive_seconds`）；达成方式为**固定优先级**（Boss 击杀 &gt; 存活），非「先到者」——判定是当前状态的纯函数、不持历史。
  判定在 core `RunGoal`（单测钉住固定优先级、关闭项忽略、进度取更接近者）。**达成不终止本局**，达成后仍可继续打（必死曲线不变）。
  目的：给必死曲线一个「打到哪算赢」的锚点，让失败归因落回玩家自身——行业依据是 Brotato「打过 wave 20 后死亡仍算胜利」。
- **命名难度档位**：D 映射到 6 档命名（巡航 / 接敌 / 高压 / 危险 / 临界 / 绝境），阈值 `progression.tier_thresholds`
  `[1.0, 1.6, 2.4, 3.6, 5.5, 8.0]`；HUD 难度标签显示「难度 xN.NN · <档名> · <难度档设置>」。
  目的：让连续爬升可读可讨论（原只有一个数字）。判定在 core `DifficultyTier`（单测钉住单调不回退）。
- **可见性**：HUD 缓存芯片下方常驻目标进度（Boss 击杀或存活，显示更接近达成的那条）；达成时给信息横幅；
  结算页新增「本局目标：已达成 / 未达成」一行。

### 1.4.3 Boss 阶段门控（2026-09-14 修正）
- **血量单调不增**：受击后血量只钳下界 0，**永不上抬**——血条可见回跳（35% 吃一发到 25% 又跳回 30%）是可读性缺陷。
  判定在 core `BossPhaseGate.ApplyDamage`（单测钉死不变量）。致死一击走 `Die()`、不进狂暴，与本条一致。
- **狂暴的「锁血」语义**：锁血是**狂暴序列期间**的免疫（`EnrageSequence._healthLock`），
  不是「受击时把血抬回」。狂暴触发条件为「未触发过 + 存活 + 血量 ≤ `boss.enrage.hp_ratio` × 上限」。
- **转阶段优先于狂暴**：单发跨 70% + 30% 双线时先转二阶段再判狂暴（状态机缺边会导致跳过 P2 转场）；
  两条线都由 core `BossPhaseGate.ShouldEnterPhase2` / `ShouldEnrage` 判定（含非法输入不触发的护栏）。
- **回归面**：core 单测 8 条（含「多次受击单调不增」「单发跨双线不回血」「致死一击致死」）。

### 1.5 Talent Cache System（2026-09-07 重构，替代旧里程碑三选一）
- **Structure**: 27 nodes in 4 categories × lines — `csharp/core/Talent/TalentTree.cs` is the single structural source. Line order = prerequisite chain (next node needs previous ≥ Lv1). Node caps = `augments.<id>.max_stacks` (json is sole authority; `extra_life` 10)。**节点规模与解锁口径只有 `max_stacks` 一条**——项目没有解锁条件机制（无 `unlock_*` 键），也没有权重池配置；`augments.dynamic_weight.*`（无实现无设计）与 `augments.explosive.unlock_boss_kills`（旧解锁残留）均已删除，不保留。作战增幅扩展的 8 节点（统一称「增幅/Augment」——效果桥 `CombatStateService.Augments`、文本键 `AUG_*`、配置段 `augments.*`）：`homing`（制导航弹：出膛弹锁定锥内追踪最近敌机）、`salvo`（齐射重弹：每 N 发 ×3 伤害，层数缩短间隔）、`deflector`（偏导护盾：弹反冷却 ×0.78^eff、反射伤害 ×1.6^eff）、`second_wind`（背水回涌：受击后 3s 每秒 +3 HP/层）、`dash_strike`（相位冲击：冲刺触及敌机 35×层伤害）、`graze_field`（擦弹力场：擦弹环 ×1.2^eff、擦弹分 +5/层）、`score_amp`（战果增幅：击杀分 ×1.08^eff）、`combo_guard`（连击护持：连击窗口 ×1.5^eff）。
- **Points & cache**: milestone +`talent.grant.points_per_milestone`(2), boss kill +`points_per_boss`(1) → ordered cache pool. Overflow decay: first `safe_threshold`(**30**) points full value; each excess position −`decay_step`(10%), floor `decay_floor`(10%) — LIFO (newest decay deepest). Spend is LIFO from tail. **正向回补**（2026-09-14）：每次花费后已衰减点朝满值抬 `recovery_step`(25%)——避免「超阈值即不可逆」的纯惩罚（叠加隐蔽入口后对新手是纯负面）；回补让「花掉点数」本身成为自救手段（StS 稀有度保底思路）。读档还原不触发回补（快照即真实历史态）。**里程碑达成横幅 + 常驻进度**（2026-09-14）：达成时提示获得点数；HUD 缓存芯片下方常驻目标行内并排显示
  「距下一里程碑 %N」（`ScoreService.MilestoneProgress`，按**本档起点之后**的分数计算，使进度条每档从 0 重走，
  而非直接用 score/threshold——后者因阈值指数增长会越到后期越填不满、读不出进展）。
  玩家要把「打得好」与「点数变多」关联起来，故池须有反馈。HUD indicator (top-right, 5 档色板，**阈值运行时读 SafeThreshold**（当前 30）：0 灰 / 可升级金+呼吸 / ≤safe 琥珀+呼吸 / ≤safe+safe/3 警示黄 / 以上红)。 Opening is charge-gated: hold `G` (`talent_panel`) or hold the indicator (`talent.panel.charge_time`, bottom-center bar; release/damage/other-modal cancels) → full bar opens `TalentPanel` (tree pauses) with layered choreography (dim → wheel overshoot slide → staggered content → footer; exit reversed+faster, unpause after).
- **Costs**: next level = `cost.base`(2) + level × `cost.increment`(1).
- **Diminishing returns**: per-node `softcap` (`talent.softcaps.*`, default 3); past softcap each level's efficiency = max(`diminishing.floor`(0.25), 1 − `diminishing.step`(0.25)×(k−softcap)) → fractional effective level; multiplicative consumers (Player pow-factors, crit, dash CD, mothership_recall CD) read `TalentEffLevel` (= factor^effLevel); integer-semantics consumers (shield layers, pierce, spread, extra_life HP) keep integer levels.
- **Mechanism A — faction mutex**: offense↔defense; one side's total investment ≥ `mutex.threshold`(5) → opposing nodes' caps −`mutex.cap_reduction`(2), permanent for the run.
- **Mechanism B — focus penalty**: any node ≥ `focus.threshold`(7) → all lower-level nodes' effective levels × (1 − min(`penalty_cap`(0.4), `penalty_per_level`(0.06)×over)); top-level nodes exempt. Footer indicator in panel.
- **Mechanism C — route contract** (`TalentTree.Routes`, base console panel): berserker(offense)/guardian(defense)/ranger(mobility); binding free once, switching costs a reset token (bought at base for `route.reset_token_cost`(6) RP). 核心大类中**已投入（Lv≥1）**的节点 +`route.bonus_levels`(1) effective level；未投入（Lv 0）节点有效层级恒为 0——否则绑定路线即凭空获得该大类全部节点的一级效果。其余大类 caps halved (floor `route.cap_floor`(1)). Replaces the retired per-line buff routes.
- **Mechanism D — overcharge**: node at effective cap may +1 level at ×`overcharge.cost_mult`(2) cost, then permanently locked; ≤`overcharge.max_per_run`(3)/run.
- **Persistence**: 天赋态每局全新，只在本局内累积；跨局不持久。本局检查点存档（2026-09-10 追加）对天赋态的快照/还原口径见 §2.5。
- Card text via `AUG_%s_DESC` keys (single source).
- Key scaling: `rapid_fire.factor` (interval ×0.75/level), `armor.multiplier`, `evasion.chance`, `regen.heal_per_sec`, `slow_field.factor`, `laser_beam.*` (line segment, not projectile；`tick_interval` 可配置), `explosive.*`, `dash_strike.tick_interval` (**0.12s，可配置**), `mothership_recall.cooldown_factor`.
- Aim assist (`player.aim_assist`): `aim_marked` rolled at birth (`mark_ratio` 0.25); AimFrameLayer brackets, AimCrosshair follows `AimPoint()`; in-frame → `Bullet.HomingTarget` (bounded `HomingTime`); out → straight fire; magnet/weak-track share falloff (full <400px → 0.3 floor at 1400px).
  - **弱追踪覆盖全部敌机**：`NearestConeTarget` 不按 `aim_marked` 过滤——标记只决定**框显示**与**框内强追踪**，不是弱追踪的准入门槛。锥角/强度随档位（low 6°/0.42、medium 8°/0.52、high 10°/0.62）。
  - **遭遇单位同属瞄准面**（2026-09-17 裁定）：编队机与精英炮塔实现同一个「可瞄准目标」契约（存活 / 世界位置 / 框半宽 / 是否标记），弱追踪、框内强追踪、磁吸、标记框**一律覆盖**；遭遇单位恒为标记目标（框恒显）。理由：遭遇期间它们是屏上唯一可打目标，按类名排除会让瞄准手感随目标类型漂移。框半宽沿用普通敌机的「碰撞半径 + 同一 pad」口径，不另造一套。**Boss 不参与辅助瞄准**（既有例外，刻意留在契约外）。
  - 玩家弹速 `player.bullet_speed`（**2600**，定稿）：纵向 1080 设计高约 0.42s、横向 1920 宽约 0.74s 横穿，远距离目标不必再「等弹到」。
- **准星-光标绑定（2026-09-10 重设计）**：键鼠/手柄下准星 ≡ 系统光标逐像素绑定——`Player.AimPoint()` 物理增量（raw − lastRaw）全量通过；粘滞（stick_factor）/磁吸/右摇杆偏移经 `Viewport.WarpMouse` 反写真实光标（手感 = 光标被阻滞/轻推，世界坐标 → `GetCanvasTransform()` → 视口坐标），下一帧 raw 即新锚点，准星永不与光标脱钩；目标点钳制在可视世界域内（`ViewWorldRect` + 4px 内边距，视角档自适应），光标顶到屏幕边缘不再失控、也不出窗。瞄准只有这一条路径（触屏差值累积随输入退役已删除，`AimPoint` 无第二分支）。

### 1.6 Bosses
- **攻击密度随难度乘数 D 增长（2026-09-14）**：档位分档照旧只表达「选哪档开局」，D 另追加弹数
  每 **3.0** D +1（上限 **4**），作用于多弹道攻击（扇射/追踪/环弹/齐射/弹幕墙）；单体狙击与蓄力炮
  等「少而准」的攻击语义不受影响。判定在 core `DifficultyScaling.BossDensityBonus`。
  目的：弹幕系后期压靠密度/模式，只堆数值会让弹幕「不更密、只更痛」。
- Rotation: Nth boss = type `(N-1)%4+1` via `Spawner.SpawnBossInternal()`.
- Phase tables P1/P2/ENRAGE (`boss.phases.typeN` + telegraph); 4-type enrage (`boss.enrage.type_*`, player slow ×0.35, no freeze); difficulty tiers × once in `_Ready()` (`boss.difficulty_scaling`: count/interval/speed).
- Anchor: `FightY` = offset from view top; all via `FightAnchorY()`.
- Escape: 50s timeout flee; fleeing **no rotation advance, no rest**; bar hidden + reorder.
- Structure: facade `Boss` + `BossFire` (danmaku)/`BossAttacks` (FSM)/`BossMovement` (+P1 press-down)/`EnrageSequence`.

### 1.7 Mothership & Return
- Summon (`dock` H charge): run not paused, input locked + event invincibility. Hanger window → warp gate → DESCEND decelerate → dual-ring slow zone → DOCKING pod (`EnterPod()`) → resupply → RELEASE (`ExitPod()`) → loiter/leave. Values `effects.mothership_summon`.
  - **机库小窗升级（2026-09-14）**：`MothershipSummonWindow` 补实况屏铬件——抬头右端实况标记（闪烁点 + 已播秒数 `MS_SEQ_FEED_FMT`）、底部**相位轨**（充能/脱锁/弹射三节点，`MS_RAIL_*` 短标签，随镜头推进点亮）、机库区**扫描线 + 纵向缓移亮带**（`SummonScanlines` 自绘 1 draw call）。扫描线/亮带挂 `_panel` 且经 `MoveChild` 插在机库底色之后——挂 `_stage` 会被后建的不透明底完全盖住（实测）。`ReduceFlash` 下亮带停中位、实况点不闪。
- Fire platform: GATLING/MISSILE during loiter.
- Return: hold B (`homecoming`, `effects.home_charge_time`) → input lock → spawner stop → recall → `starfield.Warp(18)` → cinematic → base UI (tree paused).
- Base: `BaseConsole.cs` + `DawnStation.cs` skin（2026-09-08 圆盘目录化：左缘轮盘目录 机库/补给/契约/任务 + 「继续出击」叶，右区单面板切换，分类芯片行保键盘可达）；"continue sortie" → orbital strike clear (Boss kept) → entry animation.
- **增幅补给（基地↔增幅联动，2026-09-08）**：补给面板 RP 购置——「增幅缓存」`base.supply.cache_cost_rp`(4) RP → `cache_points`(2) 点直入天赋缓存池（LIFO 衰减口径同里程碑入账）；「超载槽」`overcharge_cost_rp`(8) RP → 本局风险加点上限 +1（至多 `overcharge_slot_max`(2) 档，ResetRun 清零）。
- Entry animation (`player.PlayEntryAnimation()`): dive to bottom-third → slow backward drift; horizontal-only, vertical locked, invincible (no flicker); spawns delayed.

### 1.8 Events
- **Elite turret** (heavy 30s): carrier backdrop + tracking turrets (own HP/fire/weak lock); reuses enemy bullets. Mutex with Boss (`_bossFreezeDepth`/`_bossPending`；冻结为深度计数，两个事件各自持有时先结束者不会提前解冻); waves paused (`_wavesPaused`), resume after `boss_resume_delay`. 3-node dialogue + comm overlay; reward `reward_score` **900**（× difficulty × `KillScoreFactor`（与 fog 同口径）——全歼才给，超时 0 奖励）, timeout = none. Trigger: score ≥`min_score`(800), roll `trigger_chance`(0.35)/`trigger_interval`(45s); `cooldown` 60s。事件条（标题/计数/身份色）与编队共用 `Hud` 一套实现。
- **Formation strike** (lowest priority; 2026-09-11 深化): 3/4/5 (by difficulty) wedge dive → 90° cross（**全程压坡**）→ **波次化投弹** → exit。**核心是一条「三种应对」的攻防**：炸弹可被**击落**（`bomb_hp`，空中引爆＝无地面伤害 + 拦截分）、可被**弹反**（`IParryable` 契约，反射后反向上升并轻量寻敌，命中编队按 `bomb_reflect_damage` 结算）、也可纯**闪避**。
  - **落点可读**：每枚炸弹自带「落点圈」——完整伤害半径的轮廓 + 12 点起收缩的倒计时弧，**圈越少越亮**（最危险的一刻最显眼）。引信到期在弹体当前位置引爆，伤害按距离衰减（中心满伤 → 边缘 `bomb_edge_falloff`），边界即实际生效边界。
  - **结算三分支**（收益上限由玩家操作决定）：**全数拦截**（投出的弹全被空中拆掉 + 编队机一架未坠）→ `reward_intercept` + 逐枚 `reward_per_intercept`；**全歼**（打光编队，含已投弹）→ `reward_all_clear`；放它离场 → 只有击坠得分。被引信引爆的弹**不计**拦截；弹反后出界回收（未命中）同样不计——空中击落或弹反命中单位才算拆掉。
  - **投放必须落在可见区内**（2026-09-17 裁定）：投放点越出可见世界域的弹**不再生成**（越界即跳过这次投放，与「已毁机跳过、时刻表照走」同族，不生成弹也不计入投放数）。理由：屏外弹既不可见也不可交互，其存在只会污染「已拦截 n」的分母、让**全数拦截档结构性不可达**，还会用屏外的爆炸边缘蹭到贴边的玩家；而设计口径「投出的弹全被空中拆掉」因此保持字面成立。代价：计划时刻表与实际投放数不再相等（实测 medium 16→9），档位判定与 HUD 分母一律取实际投放数。
  - **拆弹两条路径同额**（2026-09-17 裁定）：空中击落与弹反命中都是「拆掉一枚威胁」，收益必须同族同额——两路都给 `bomb_score` 走 `AddKillScore`（吃连击与 `score_amp`），并叠加同一笔逐枚 `reward_per_intercept`（经 `AddEventScore`，属事件奖励、不计连击）。原实现只在击落路径给击杀分，于是更难的弹反应对收益反而更低。
  - **投弹节奏**：`volley_batches` 波 × 每波按「到长机的距离排序」依次 `bomb_interval` 错开（长机先投），波间 `volley_gap` 呼吸；同机 `bombs_per_craft` 枚以 0.4s 间隔连投。编队几何与投弹时刻表在 core `FormationPlan`（楔形槽位 / 名次定序 / 时刻表排序，可单测）；引擎侧只按表消费。
  - **演出与可读性**（2026-09-12）：入场前屏顶打**进场预告线**（复用普通波次的 `SpawnTelegraph`，换编队身份色，时长取 `spawner.telegraph_duration`）；投弹前 **0.45s 机腹灯闪烁预警**（落点圈只说明「会炸到哪」，预警灯说明「谁要投」）；顶部**事件条**显示全程进度（入场 + 转弯 + 投弹表末刻 + 离场）与「编队机 x/y · 已拦截炸弹 n」（与精英炮塔共用同一组件，标题/计数文案与身份色由事件注入）；通讯序列 = 接敌警告 → 轰炸段开始的战术提示（可击落 / 可弹反回敬）→ 至多一条进度台词（首次战损或首次拆弹，敌方视角）→ 结算台词。
  - **触发与互斥**：触发策略（`min_score` / `trigger_chance` / `trigger_interval`）只由 `GameEventManager` 读取与判定，事件自身只报「就绪」（空闲 + 冷却结束 + 母舰不在场）；资格合成与计时推进下沉 core `EncounterTrigger`（可单测：分数门槛、资格不足时计时冻结、到点先复位整段再掷签）。事件**同时占用波次槽与 Boss 槽**：运行期暂停普通波次并冻结 Boss 调度，收场（含返航打断）一并解除并补触发一次期间到期的 Boss。`Abort()` 连同在场炸弹一并清除且无结算（已入账的击坠分与拦截分保留）。
  - **反馈即时性**：逐枚拦截分（`reward_per_intercept`）在拦截当帧入账，与被击落编队机的击杀分同口径；事件结算只发「全数拦截 / 全歼 / 清除」的档位奖励与台词（奖励值可为 0，台词不省——玩家要能分清打光了、拦住了与它自己走了）。结算档位奖励经 `KillScoreFactor` 与难度档倍率（同精英炮塔与 fog 补偿口径）。
- **Fog events** (light interference, independent of spawner chain): probability roll (`fog_events.trigger_chance`/`check_interval`), `first_delay` opening protection, `min_interval` cooldown, explicit `duration` auto-clear, single-event concurrency; effects via signals to Player + manager-owned visuals. 4 events: fake_enemies (no-damage ghost ships), mental_confusion (input inversion + tint), bullet_malfunction (angle jitter / misfire / fire-interval jitter), direction_shift (periodic forced movement vector). Cleared on return and death; 存活补偿 `fog_events.reward_score` 150 × `KillScoreFactor`（与击杀/事件奖励同口径）.
- **Priority chain**: Boss → elite turret → formation strike (encounter 组互斥, 触发门控 = spawner processing；互斥判定单源在 `GameEventManager`，事件不再自查 Boss/同类事件); fog 独立触发（不占波次槽、不与 Boss 互斥）。统一注册表在 `GameEventManager`（`GameState.Events`），遭遇启动只走 `StartEncounter`（自动触发与 `--event-probe` 同一入口）。
- **确定性口径**：遭遇/手感/长局探针一律**关闭迷雾随机事件**（迷雾按概率触发会污染其余探针的模拟态）；迷雾自身由 `--fog-probe` 专趟覆盖，且同样走生产资格/门槛（不绕开 `GameEventManager` 的触发判定）。

### 1.9 Meta HUD
- Fullscreen FX CanvasLayer layer=1 (above world, below HUD→layer=2); `meta_health.gdshader` + `hint_screen_texture`.
- Pipeline: hit layer (CA + radial blur) → directional ripple → desaturate/cool tint + vignette → cracks (Voronoi baked once; headless 低分辨率).
- FSM: NORMAL/CAUTION/DAMAGED/CRITICAL/DYING (0.75/0.50/0.25/0.20); fast down (tau 0.10), slow up (tau 0.80 + stagger); DYING heartbeat 1.0–1.2Hz, breath ±1.5%, HUD shake ±2px, FOV −6%.
- Explicit (SegmentedBar) + implicit (desaturate/vignette/heartbeat) layers; `reduce_flash`: CA ×0.4, no breath/shake/heartbeat (SFX kept).
- Brightness proxy from registries (bullets ×0.002 + explosions ×0.15), zero GPU readback; LOD1 skips CA/blur/ripple.

### 1.9.1 左下集成仪表盘（2026-09-13 追加）
把左下角从「四条同形横条 + 文字标签」改为**按元素真实特性选形态**的单面板仪表带，两排：上排生命（主读数，独占整行）、下排燃料量槽＋两枚充能槽＋弹仓格＋坞态指示灯（枚举状态与充能槽同处下排：两者都是「一瞥判断可用性」的离散指示，与上排的连续主读数不同族）。

- **形态判据**（形态差异必须表达「量是什么样的量」，不是装饰堆砌——这是与「技术展示品」的分界）：
  - **生命 → 分段横条（保持原形态）**：主生存资源、最常扫视，占上排整行、字号最大最亮，是整块面板的视觉主导。
  - **燃料 → `FuelTank` 消耗量槽**：**持续消耗的预算**，玩家只问「还剩多少、够不够」→ 容器液位即读数（液位高度＝存量）。侧缘 4 段刻度给量程参照（不写数字）。液面起伏幅度受双向钳制（`TankLiquid.WaveAmplitude`）：不超过液层厚度（波谷不越内腔底，防填充多边形自交、三角化失败整块不画）也不超过液位上方余量（波峰不越内腔顶，防绘制溢出内腔）；满油与见底附近波幅自动收窄至零。
  - **冲刺 / 弹反 → `AbilitySocket` 充能槽**（**同一构件两个实例**）：二者是同一类量——**充满即可用、用掉清空、冷却回充**的循环充能，不是需要读刻度的连续量 → 环形冷却（动作游戏 / MOBA 能力冷却通用语汇：一眼只回答「能不能用」）。三态即读数：充能中＝环按进度填充＋字形压暗、就绪＝字形点亮＋满环＋一次性外扩脉冲、未解锁＝整体压暗＋锁定横杠。**不用指针表盘**：本盘没有任何「需要读速率的量」，指针会读成模拟量测，诱导玩家读数而非判断可用性。
  - **弹仓 → `CartridgeStrip` 分立格**：离散计数 → 独立药槽（连续条会读成比例，而玩家需要的是「还能打几发」）。
  - **母舰坞态 → `AnnunciatorLamp` 指示灯**：枚举状态 → 灯。配色语义取自 **14 CFR 29.1322** 三级分类（一手法规）——红＝warning（须立即处置，此项目前唯一用途＝弹仓见底）、琥珀＝caution（蓄力/下降/在场/冷却）、绿＝safe operation（就绪待命）。
- **形状语汇单源**：所有方形构件（量槽、充能槽、面板）一律走 `UITheme.ChamferPoints` 的切角八边形，全盘只有一套方形语言——「每元素一套隐喻」正是读作展示品的根因。
- **液体表现的克制纪律**：**静止几乎不动、变化时才动**。常驻液面只有约 2px 低频起伏（0.32Hz，只做「是液体」的材质暗示），数值变化时叠加一次衰减晃动读作惯性；**不做气泡**（无信息量的装饰）。弹幕游戏里任何常驻动效都会抢视线。
- **单源与文件**：`FuelTank.cs` / `AbilitySocket.cs` / `AnnunciatorLamp.cs` / `CartridgeStrip.cs`（均 `Control` + `_Draw` 程序化绘制，零贴图零 shader）；装配与位置单源在 `Hud.BuildInstrumentCluster`（原 tscn 的 FuelBar/DashBar/ParryBar + 三个标签节点退役）。母舰灯态由 `Main.DockStateValue` 单源给出，与坞态文本同分支，不在 HUD 重推。**燃料低量警戒线单源在 core `FuelGauge.WarnRatio`**（液色与刻度着色同阈值，不在 HUD 与构件各写一份）。
- **无障碍**：全套动效（液面起伏、就绪脉冲、灯态呼吸、低量脉动）按 `ReduceFlash` 递减或冻结；闪烁频率全部低于 WCAG 2.3.1 阈值。
- **文案**：三个小标题复用既有翻译键（`UI_FUEL`/`UI_DASH`/`UI_PARRY`），**不新增任何玩家可见文案或数字**（数值只用形状/颜色/液位编码）。
- **成本**：仅液槽常态逐帧（每帧 ≤12 点液面 + 4 条刻度线，控件 34×52）；充能槽只在充能追赶与就绪脉冲期间推进，静止 `SetProcess(false)`。

### 1.10 Cinematics
- Return: 7 shots 11.8s, 2.35:1 letterbox; Esc via `RETURN_SKIP` (1.2s grace `effects.return_skip_grace`); lands on base UI (tree paused); BGM −40dB in shot 7.
- Shared factories: `CinematicFx.cs` (`SoftGlow`/`Particles`/`Shockwave`/`Beam`/`RadialStreaks`; zero heap alloc in drive `_Process`), `DawnStation.cs`（全息虚影态）。

### 1.11 Tutorial
- Standalone `scenes/tutorial.tscn`, self-handles back (not BackNavigator). Aligned with run: stage 1 force-marked targets; stage 4 hold-H → gate → `BeginWarpIn` → dock (hanger skipped). Isolates run state/saves; restore `Engine.TimeScale = 1` on exit.
- 敌机配置**与正局同源**：均经 `Spawner.MergeTypeInto` / `MergeTypesInto` 把 `enemies.types` 覆盖进默认表（教程经 `BuildMergedEnemyTypes()[0]`），不得直读未合并的 `BuildEnemyTypes()` 默认表——那条路径绕过 balance，改数值时教程静默不跟。读取面由 `check_code_defaults.sh` 兜住（只许 `Spawner` 内部调用）。
- 教程是独立于 `Main` 的生产入口，冒烟单独一趟直开 `tutorial.tscn` 覆盖（断 `[tutorial] 场景就绪`）。

### 1.12 Exit/Back Navigation
- All back inputs → `BackNavigator.GoBack()` via pure `DecideBackAction()` (confirm → cinematic skip → settings/base/blocking/results → augment dock → pause → top → combat).
- Stack: L3 ExitConfirm → L2 overlays (Settings/Base/GameOver/cinematics) → L1 run (HUD⇄Pause + augment dock)。标题屏 `title.tscn` 为独立场景（2026-09-09 深空机库改版：程序化星空 + 远景实况战场〔敌机编队/远处爆炸/Boss 剪影〕+ 玩家机自远处跃迁飞入右侧悬挂展示〔轮廓背光/尾焰怠速/铭牌卡〕+ 左侧标题区，开场演出 ~2.2s 不阻塞输入；任意键开局 + T 教程。）
- **圆盘 UI 全覆盖（2026-09-08）**：左缘 `RadialWheel`（圆心锚屏外左侧，卡片沿弧排列；槽距按选项数自适应 `SlotAngleFor`，端点角钳 ±36° 防压 HUD；全容弧面时键盘/滚轮经 `FocusBias` 移动聚焦项）为全站菜单导航面：天赋面板（已有）、暂停、死亡结算、基地目录、设置页导航；`RadialMenuLayer` 为统一开合骨架（dim+轮盘过冲滑入，`SetWheelActive` 同步轮盘/chrome 遮罩显隐——非模态页须显式关遮罩）；方向键旋转 + Enter 确认（GUI 焦点存在时自动让位焦点链）。
- Battle exit: 2nd confirm (progress-loss warning); `ExecuteExitCleanup`: save settings, stop SFX, fade quit. **战斗退出不删档**——删档只发生在死亡与放弃重开（存档语义单源见 §2.5）。
- Esc / gamepad `ui_cancel`, one state machine.

### 1.13 Combat Fairness (数值定稿)
- **受击喘息窗口口径（2026-09-14 人类决断收窄）**：玩家受击后 `dda.duration`(5s) 内，敌机与 Boss 的
  **开火间隔** × `dda.factor`(1.3)（只做「直接缓解」：减弱打你的火力）。
  **波次间隔不吃该因子**——整局推进速度不该被「免伤 + 清 250px 弹 + 降档」三重喘息拖慢，
  与「压力无界」的设计意图相悖。候选「扩为真自适应」明确不做（须先推翻本条）。
- **Grace frames**: enemy bullet in Hitbox defers settlement `player.grace_period` (0.05s); only enemy-bullet→player timing. **离场判定（2026-09-10 修复直击不结算）**：窗口内离场时按弹心相对轨迹段（入口→离场，圆心参考系两端同减抵消玩家移动）最近距 ≤ 核心半径（7×ws = 2.8px）判定——贯穿核心 = 视觉直击，照常吃伤害；仅擦边入框（最近距 > 核心）才免伤（高速弹穿越核心约 25ms，若「离场即免伤」会让直击永不结算）。到期仍在框内同样结算（不变）。
- **Graze**: ring outside hitbox (`player.graze_radius` 20, gameplay-range family, no world_scale) → `player.graze_score` (**30**，× 难度 ramp × 连击加权 `graze_combo_weight`), once/bullet; hitbox area gives none. 玩家受击判定仅经 `Player/Hitbox`（r=7 × world_scale = 2.8）；机身 r=22 不参与碰撞（mask=0）。
- **Phase transitions**: P1→P2 & ENRAGE clear all bullets (incl. formation bombs) + brief invincibility (`boss.phases.transition_invincible` 1.0s, additive only); escape: no clear/invincibility. Boss bar segmented (P1 amber/P2 orange/ENRAGE red; boundaries = phase thresholds; drains left) —— **段界由 `boss.phase2_hp_ratio` / `boss.enrage.hp_ratio` 派生**（`[1−p2, p2−e, e]` 与刻度 `[p2, e]`，core `BossBarSegments`），不得另存一份硬编码：改阈值而段界不跟，玩家从血条读出的阶段边界就是假的。
- **F parry**: full 360° circle, 0.5s window (windup 0.15/recover 0.15); reflect = mirror y-flip ×2 speed ×1.5 dmg (rounded) as player bullet; hard cooldown 3.0s from effect end (3.8s cycle); all `player.parry.*` in balance.json; LT bound.

### 1.14 Input Surface（PC 专用，定稿）
- **两路输入：键鼠 + 手柄**。**移动端不做**（2026-09-11 产品范围决定，非技术限制）：触屏输入全量退役——虚拟摇杆/按钮层、触屏瞄准基准与差值累积分支、触屏开火键、设置开关、Android 返回手势路径均不再存在，`Player.AimPoint()` 只剩光标绑定一条路径。工程面不留任何触屏分支，重新引入须先推翻本条（见 ROADMAP 决策）。
- **键盘**：`project.godot` 的 `[input]` 是可改键动作的唯一默认源；改键/恢复默认只擦写 `InputEventKey`（`InputBindingsService.ApplyKeyBindings`），冲突键从占用者移除。开火不进可改键表——它在键盘侧没有绑定（`EnsureFireBinding` 运行时装配鼠标左键，与手柄同层装配）。
- **手柄**：`BindJoypadDefaults` 运行时装配（左摇杆移动 / 右摇杆瞄准 / A 冲刺 / RB 加速 / LB 微调 / X 停靠 / Y 返航 / R3 放弃 / L3 增幅面板 / LT 弹反 / RT 开火），`project.godot` 不承载手柄事件；摇杆死区统一走 `settings.json joy_deadzone`；PS 布局只改标签不改位置语义。
- **轮盘 UI 的输入面**：方向键/摇杆旋转、确认键按下；GUI 焦点存在时方向键让位焦点链（键盘导航先于 GUI 相位）。

### 1.15 Settings（设置页，2026-09-11 规范化）
- **信息架构：五页**，左缘轮盘与左侧导航同源（同一份页表 `SettingsUi._pageDefs` 驱动页目录/内容构建/文案，增页只改一处）。分组与顺序：
  - **控制 Controls**：可改键表（动作清单唯一事实源 = `GameState.REBINDABLE_ACTIONS`）· 手柄分组（布局/灵敏度/死区）· 重置区（恢复默认按键 / 全部恢复默认）。
  - **游戏 Gameplay**：难度 · 开火方式 · Ctrl/Shift 模式 · 辅助瞄准档位。
  - **显示与性能 Display & Performance**：视角 · 窗口模式 · 分辨率 · 帧率上限 · 垂直同步（含实际状态读出）· 当前帧率与显示器刷新率 · 鼠标锁定 · 画面增强。
  - **音频 Audio**：主音量 · 音乐 · 音效。
  - **辅助与关于 Accessibility & About**：减少闪光 · 屏幕震动强度 · 命中顿帧强度 · 版本与操作速查。
- **生效时机**：一律即时生效（无需「应用」按钮）；滑杆拖动即时改值（apply），持久化只在 **`DragEnded` 与离开设置页时各一次**——拖动过程中逐帧全量原子写盘是磁盘写风暴。
- **持久化版本**：`settings.json` 带 `version`（当前 4），**读入时真读**。版本与当前一致直读、低于当前按旧键名迁移、**高于当前按逐字段默认值回退**（不猜未来字段语义、不拒绝加载文件、不改写格式）；判定在 core `SettingsMigration`（可单测）。**「逐字段回退」的落地口径**（2026-09-17 收口）：版本高于当前时整份档**按空档读入**——每个字段都取默认值，不拿当前语义去读未来档里的同名键（这正是该分支存在的唯一理由；只对缺失键回退而照读已知键，等于把未来语义当当前语义用）。告警照发，文件在下次落盘前不动。
- **破坏性动作必二次确认**：恢复默认按键、全部恢复默认都经 `ConfirmationDialog`，确认文案写清后果（会丢什么、什么会保留）。
- **状态可见**：开关下方给出当前状态读出（垂直同步的实际生效值可能被驱动覆盖，与偏好不一致时明示）；屏幕震动强度与命中顿帧强度 0% 都单独文案说明「已关闭」，避免玩家把 0% 读成「最小」。
- **无障碍定稿范围**：减少闪光（频闪）× 屏幕震动强度（运动）× 命中顿帧强度（瞬时定格）× 可改键 × 音量三分路。屏幕朗读（XAG 106）与字幕**不做**——本作无配音与叙事对白，无朗读对象。
- **有意例外**：辅助瞄准不给「关闭」档（常驻机制，仅弱/中/强三档），口径见 §1.5；改键表不含开火 / 天赋面板 / 暂停 / 重开（开火在键盘侧无绑定，天赋面板与重开为固定键，暂停走引擎 `ui_cancel`，见 §1.14 与 §1.12）。
- **默认值取向**：中立、低风险（亮度/音量类不做极端档、震动/闪光/顿帧默认开启但一键可关、语言默认简体中文）。

---

## 2. 视觉与表现系统（2026-09-10 全面升级：战术琥珀）

### 2.1 色板（`csharp/godot/UITheme.cs` = UI 调色板单源）
**UI 调色板（按钮/面板/HUD/文字/全息线光等界面元素）唯一来源是 `UITheme.cs`**；改此一处 = 全站 UI 换色。
**场景文件（`scenes/*.tscn`）内嵌的 VFX/粒子/自发光配色以场景文件为准**——它们是编辑器内嵌资源，代码无法统一注入（如 `player.tscn`/`boss.tscn`/`enemy.tscn` 的 `tint`、`mothership.tscn` 的粒子色）；C# 侧若需与这些颜色一致，必须引用 `UITheme` 常量而非再写一份字面值。
金属按钮/面板贴图为近白灰度 + 预烘焙倒角，
色相全部由这些 tint 派生（`assets/sprites/ui/metal_streak.png`、`button_plate*.png` 未重绘）。

- **主交互琥珀** `Accent #FF9F1C`：按钮/焦点/进度/边框；`AccentHot #FFC14D` 受激提亮；`AccentDim` 琥珀 22% 分隔线。
- **全息琥珀** `Holo #FFC861` / `HoloPale #FFE6BF`：仪表/舱段/站体 holo 线光与热读数。原「数据青」通道退役——
  冷青与琥珀主色互相打架（基地控制台整页青、天赋列冷绿、面板冷蓝灰底），全站收归暖族。
- **稀有金** `AccentGold #E8C170`；**危险红** `Danger #FF3B4E`；**成功色** `Success #C2D16B`（暖橄榄金，替代冷绿）。
- 底：`BgDeep` 暖炭黑 / `PanelBg` 暖炭 / `PanelSteelTint` 暖暗钢；文字 `#EEE7DC` / `#9C9184`（暖白/暖灰）。
  面板垂直渐变与金属/输入框/滚动条 tint 一律暖偏（冷偏会在琥珀主题里留下蓝灰底）。
- 暖钢 tint：`SteelTint`(暖青铜灰) / `SteelTintHover`(受激暖光) / `SteelAccentTint`(主按钮琥珀面)；焦点环取 `AccentHot`。
- 次级局部色板同步收编：`RadialWheel`(Card/Band 暖炭灰)、`DawnStation`(暖钢/全息琥珀虚影)、`AimCrosshair`/`AimFrameLayer`(琥珀)、
  `MothershipSummonWindow`、`TalentFanView`、`TitleScreen`/`Tutorial` 底色、`Hud` Boss 分段、`MetaHealthFX` 裂纹带。
  返航过场（黎明站/舱室）与玩家侧（母舰/跃迁门/轨道打击）同属暖族。
- **弹幕可读性**：玩家弹 = 白热芯 + 琥珀晕；敌弹 = 红/品红（不与琥珀 UI 混同）。玩家机能量/尾焰/激光/残影/增幅附件统一琥珀；
  Boss 预警色（telegraph）保留多色编码，属玩法信号不作统一。

### 2.2 世界层后处理（画面"平/廉"的主要补齐手段）
GL Compatibility 下 Godot `Environment` 辉光/SSAO 不可用，故手写屏幕纹理后处理：

- `assets/shaders/world_grade.gdshader`（单趟）：亮部提取 → 旋转网格环采样柔光（quality 档：单环 4 tap / 双环 8 tap）
  → 暖调色彩分级（gain+lift+S 曲线对比）→ 常量晕影 → 胶片颗粒。
- `csharp/godot/WorldPostFx.cs`（`CanvasLayer` layer=1）：`hint_screen_texture` 全屏 ColorRect。
  **层序靠树序**：世界(layer0) → 增强 → `MetaHealthFX` → HUD(layer2)；Meta 采样含辉光结果。
  接入 `main.tscn`(Main) / `title.tscn` / `tutorial.tscn`（教程 HUD 抬至 layer=2）。
- 性能纪律：设置关闭（`WorldPostFx=false`）整层 `Visible=false`（零 GPU）；`reduce_flash` 颗粒置零 + 辉光减半；
  静态参数启动一次、动态参数 epsilon 检测。
- 数值 `data/balance.json effects.world_post.*`（threshold/intensity/radius/tint/quality/grade/gain/lift/contrast/vignette/grain）。
- **设置开关**：设置页「画面」段 `SET_WORLD_POST_FX`（持久化 `world_post_fx`，默认开）；走 `SettingsService` → `GameState` 信号广播（同 `ReduceFlash` 模式）。

### 2.3 战机与战场表现（纯表现层，**玩法判定零改动**）
- **玩家机贴图**（`scripts/tools/generate_player_sprite.py`）：暖钛/青铜钢甲 + 琥珀能量语言，装甲板/铆接/散热格栅/舱口加密，
  暖描边 + 暖缘光（`sprite_polish.py` 新增可选 outline/rim 覆盖——敌方晶体族用模块默认冷色，输出逐字节不变）。
  **画布 254×254 与头部锚点严格不变**（`PlayerAugmentVisuals` 按 `BaseShipScale=0.65` 对位锚点，叠加件仍贴合）。
- **尾焰**：默认方粒 → `CinematicFx.SoftTexture()` 软点 + 白热→琥珀→暗橙 `GradientTexture1D` 色阶（廉价感主源之一）。
- **机体背光轮廓**：敌/精英/Boss 新增同源贴图副本 + 加性材质 + 阵营染色剪影（`Enemy.UpdateRimGlow` / `Boss.RefreshRimGlow`），贴图未改。
- **星云/亮星/爆炸/激光**：星云改琥珀+青双色、亮星色温重调；爆炸 ramp 深琥珀；玩家激光/枪口/残影/受击指示琥珀。
- **面板铬件**：`ChamferedPanel` 新增四角琥珀刻度（`CornerTicks` 默认开）+ 顶缘全强调色受光线（受激边缘）。

### 2.4 过场演出细节（2026-09-10 精修）
「简单处够简单、精细处不够」的补课——对返航过场的乘员与场景追加设计细节（纯表现层，时序/时长/字幕/音效口径全不变）：

- **共享乘员构件 `csharp/godot/CrewFigure.cs`**：把原先重复的简笔人物（圆头 + 棍状四肢 + 平板躯干）重建为有设计细节的宇航服——分件头盔（棱面壳/面罩玻璃/框缘/颈环/侧通讯舱/天线信号灯/下颌护板）、分层胸甲 + 背带扣具 + 状态灯排、双筒维生背包（罐体/喷嘴/供气管/压力表/散热格栅）、肩部叠甲 + 铆钉、关节环/护膝胫甲/护腕分指手套/齿纹战术靴、全身边缘走线与状态灯。**关节契约逐位不变**（返回 {node,hips,knees,shoulders,elbows,torso,eyelid}），步行/握姿/呼吸相位公式与 eye-lid 特写全部照旧；`ReturnCinematic.BuildPerson` 调用。
- **舱段模块（`DawnStation` 共享）**：环体舱段加舷窗灯带 + 装甲分缝 + 端盖条 + 散热格栅（环站不再是光板矩形块）；返航/基地背景同享。

### 2.5 本局存档（2026-09-10 追加）
反转 2026-09-08「无对局存档」：新增**单存档位**本局存档 `user://run.json`（与 `settings.json` 分区；复用 `SaveManager`/`SaveStore` 原子写 + 损坏隔离）。

- **模型：存档 = 可继续的检查点**，三条规则覆盖全部边界：
  1. **写入/覆盖**：退出确认「保存并退出」+ **回到基地**（母舰坞修/返航，`Main.OnReturnFinished`）自动落盘，新档覆盖旧档。
  2. **终结删档**：玩家**死亡**（`GameState._Ready` 订阅自身 `PlayerDied`）与**「放弃重开」**（`GameState.RestartRun`）——本局终结，检查点一并作废，不可读档回滚（保住必死曲线、防 save-scum；也堵住「读档→暂停重开→再退出重进」的无限回滚）。**删档失败不静默**：死亡路径若删除失败，兜底把 `run.json` **覆写为墓碑内容**（版本号置为不可读态），保证「不可读档回滚」承诺不因 IO 失败而失效。
  3. **非破坏性**：标题屏选「**新的一局**」**不删旧档**——一次误触不会抹掉进度；旧档保留到被新档覆盖或被本局终结清除。`ExitToTitle` 同样不删（Tutorial 走此口，删档会误伤；且「回标题保留检查点」语义正确）。
- **读档**：标题屏 `HasRunSave()` 为真时显示 `C — 继续上次出击`；`C` 置 `GameState.PendingLoadRun`，`Main._Ready` 返回标题分支读档成功则跳过 `ResetRun`（`LoadRun` 内部先 ResetRun 再按存档还原），否则回退全新一局（保留旧档）。读取结果**三态区分**：**无存档 / 损坏隔离（移为 `.corrupt`，不显示假承诺）/ 暂时不可读（文件存在但读不出，如被占用）**——暂时不可读按「稍后重试」处理（不当作没有存档、也不删档），只有确认无档或损坏才按无存档走。
- **粒度**：**还原本局进度，战场从新一波开始**——持久化 score/kills/boss_kills/combo/milestone_count、run_time/difficulty_multiplier/dda_timer/difficulty_time_step、health/augments、talent(levels/overcharged/route/reset_tokens/bonus_overcharge_slots/cache_values)、missions(rp/refresh_points/条目/last_kind_value)。**不持久化**敌机/子弹/Boss 位置、波次计时、回血延迟、TaskPool 洗牌游标、玩家无敌/受击帧守卫。**`dda_timer` 属持久化项**（键写读成对）——受击喘息是有界量（≤`DDA_DURATION`），带过读档边界的影响在噪声内；若日后改判为不还原，须连键一起从写侧移除（只留键不读会掉出写读对称判定，属改存档格式）。
  - **连击（combo）**：连击数持久化，读档时恢复并给予**满连击窗口**（与「连击窗口计时本身不持久化」自洽——存档的是连击段位，不是剩余时间）。
- **还原顺序**（`GameState.RunSave.cs.ApplyRunDict`）：talent → combat（先恢复 extra_life 层级才有正确 MaxHealth）→ score → progress → missions；各服务 `RestoreRunState` 末尾补发既有信号（`AugmentsChanged/TalentsChanged/CacheChanged/RpChanged/…`）驱动 HUD 与 Player 增幅件重建。
- **健壮性**：档案带 `version`（=1），不符按无存档忽略（不隔离不阻塞开机）；损坏 JSON 走既有隔离（且 `HasRunSave` 会触发该自愈，无需人工清理）；**JSON 往返会把 `StringName` 键退化为 `String`**，还原时对 `augments`/`missions` 键做 StringName 归一化（否则查表落空、增幅与任务进度静默失效）；所有字段判型读取，非法回默认。
- **还原必须过白名单与上限**（2026-09-17 收口）：手改档是可达输入面，「判型 + 钳域」不足以防静默错值，还原一律按**已知集合**过滤——`missions` 只收 `MISSION_POOL` 里的 id（goal 取池内定稿值，不读存档里的 goal，否则查池得 0 会让 `IsMissionDone` 恒真、可反复领取 RP）；`talent_overcharged` 只收已知节点、去重并按本局过载名额上限截断；`augments` 只收天赋树已知节点且层级 ≤ 该节点上限（**风险加点节点例外**：层级 `max_stacks+1` 是其正当形态，读档必须保留，否则双倍价买的那一级静默消失）；`key_bindings` 只收 `REBINDABLE_ACTIONS` 里的动作名（否则未知动作名被永久回写）。过滤策略下沉 core 纯函数并配单测。
- **还原末尾补发驱动信号**：各服务 `RestoreRunState` 末尾补发既有信号（`AugmentsChanged/TalentsChanged/CacheChanged/RpChanged/DifficultyChanged/…`）。`DifficultyChanged` 尤其不能漏——HUD 难度标签只在信号与语言变更时刷新（无轮询），漏发会让难度读数停在 x1.00 直到下一次时间档跨步（最长 30s）。

### 2.6 性能设置（2026-09-10 追加）
设置页「显示与性能」页的**性能**段（`SettingsService` + `SettingsUi`，settings.json 持久化）：

- **帧率上限** `fps_cap`：**九档** `30 / 45 / 60 / 120 / 144 / 165 / 180 / 240 / 不限制`（默认 **60**）——档表单源 `SettingsService.FPS_CAP_LEVELS`/`FPS_CAP_ORDER`（键 `fps30/fps45/fps60/fps120/fps144/fps165/fps180/fps240/unlimited`，`unlimited` 值为 0）。生效值写入 `Engine.MaxFps`。30/45 供低配机与省电场景。
- **垂直同步** `vsync`：开关，默认**开**。生效值写入 `DisplayServer.WindowSetVsyncMode`（headless 跳过窗口 API）。开启时实际帧率再受显示器刷新率钳制；关闭可降输入延迟（竞技向），配合帧率上限使用。
- **应用时机**：`GameState._Ready` → `LoadSettings()` 后显式调用 `ApplyDisplaySettings()`（无设置文件时 load 不应用，故补默认档）；设置页切换即时生效 + 落盘 + 广播 `DisplaySettingsChanged`。档位非白名单值忽略、保持默认。

---
### 2.7 战场纵深与材质表现（2026-09-11 追加）
针对「战斗画面大面积死黑、特效词汇单一、机体无材质感」的第二轮美术升级，仍为纯表现层、**玩法判定零改动**：

- **纵深背景**（`DeepSpaceBackdrop.cs`，z=-5 介于星空与实体之间）：远景行星/残月、中层空间站残骸带、近景高速尘埃三层视差，贴图走确定性 PIL 管线（`generate_backdrop_sprites.py`）；星云改暖琥珀 + 冷青双通道并提密度，星点分档加密——深空基调不变，只是不再死黑。
- **材质级能量层**：机体首次引入材质 shader（`ship_energy.gdshader`）——生成器额外导出 `*_glow.png` 发光遮罩（R=霓虹走线 / B=引擎喷口，基础贴图逐字节不变），运行时 additive 叠加，走线流动 + 喷口呼吸 + 阵营染色；玩家尾焰叠白芯/琥珀/红外三层喷口。
- **打击感**：爆炸四件套（白炽核心闪帧 / 装甲碎片多边形 / 烟尾 / 双层冲击环）+ Boss 多段错拍；受击白闪叠加缩放回弹；相机微旋转分量。
- **弹体能量化**：弹体图集加宽出弹尾渐变拖尾 + 双层辉光，玩家弹呼吸脉冲（相位错开），敌弹只提亮不脉冲（可读性优先）；开火口闪光、激光三线结构（白芯/主线/外辉光）。
- **Boss P2 变身**：切换瞬间装甲碎片炸散 + 能量层切狂暴配色（品红→白炽）+ 持续光环粒子；贴图沿用 `_p2` 帧不重绘。
- **标题品牌化**：`generate_logo.py` 确定性生成字标 + 切角徽章（呼应 ChamferedPanel 与机体剪影），文字 Label 退役。
- 数值全部落 `data/balance.json` effects 段（`backdrop` / `explosion` / `ship_energy` / `thruster_core` / `boss_p2_transform` 等）；world_grade 辉光阈值/强度随新亮点上调。

### 2.8 动效与可读性强化（2026-09-13 追加）
第三轮视觉升级，补齐「界面无动效、状态变化硬切、事件缺反馈」三层缺口，仍为纯表现层、**玩法判定零改动**。
动效常量一律 `private const` 就地定义（不新增 balance 键）；一切闪烁/提亮脉冲按 `ReduceFlash` 递减或停用。

- **共享动效设施**（`UITheme`）：新增 `FadeIn` / `AnimateClose` / `AnimateModalClose` / `PunchScale` 四个工厂，并把散落 HUD 的硬编码色收归 token（`HudBossHp` / `HudBossHpP2` / `DangerVignette` / `TickWhite` / `TrackWhite` / `ShadowBlack` / `SheenWhite` / `AimAmber*`）。
  **模态退场契约**：`AnimateModalClose` 在调用当帧即断开输入处理与鼠标命中、置遮罩穿透，仅把视觉渐隐留给 tween —— 退回/暂停链的焦点交接仍同步，残影不截获已交还给下一层的输入（重新打开时调用方须复位 `SetProcessInput` 与遮罩 `MouseFilter`）。
- **Meta 界面动效**：暂停页焦点描述卡改交叉淡入 + 弹一下（原先每次焦点变化硬切显隐）；设置页五页切换改淡出/淡入 + 行错峰（页指针与焦点同步更新）；破坏性确认弹窗由引擎默认 `ConfirmationDialog` 换成统一页壳模态（全站唯一视觉语言缺口消除）；退出确认补面板缩放归位 + 按钮行错峰；结算页统计行错峰淡入；通讯浮层补滑入/强调脉冲/保持期扫描线（打字机与停留时长口径不变）。
- **Meta 屏幕动效**：标题屏「任意键」加确认动效后再切场景（0.2s，重复输入仍由 `_started` 挡回）；标题战场编队机补尾焰 + 翼尖航行灯、Boss 巡航补垂向漂移与压坡；天赋扇形补悬停缩放 + 依赖线提亮 + 加点闪光；基地控制台分类切换改定向滑动、任务/路线行错峰、继续出击走退场动画；黎明站补 ~300s 环体怠速自转并把冷灰/窗灯收归 token；教程补阶段横幅、目标更新动效与完成面板入场（步骤逻辑/时序/文案不变）。
- **HUD 可读性**（`Hud` / `SegmentedBar` / `HudChargeBar` / 新增 `HudDamageArcs`）：血条加「残影段」（掉血后滞后回追，仅 `EnableGhost` 的玩家血条）；新增屏幕边缘方向受击弧（消费既有 `PlayerDamaged(amount, fromPos)`，仅显示）；低油量脉冲 / 冲刺与弹反就绪脉冲 / 蓄力条满闪；事件条与 Boss 血条改淡入淡出、Boss 掉段补闪；增幅格新增弹出高亮；击杀数补弹跳。**未新增任何玩家可见文字或数值**（本作已裁局内计分显示，不复活连击/任务计数）。
- **战斗即时特效**（新增 `CombatVfx` + `VisualFxDirector`）：击杀环（敌机 `Died`）、Boss 常规阶段冲击波与狂暴放射爆发（`PhaseChanged` / `Enraged`）、弹反金环 + 碎片、冲刺发射环 + 反向拖尾、直击火花与暴击星芒（`Bullet` 直击/暴击分支，暴击复用既有单次 RNG 结果不重掷）。轻量特效静态在活计数封顶 24、总监侧封顶 10，超限跳过；特效根按 `world_scale` 缩放、tween 自毁。
- **动态分级**（`world_grade.gdshader` / `meta_health.gdshader`）：`Engine.TimeScale < 1`（狂暴子弹时间）驱动暖调增对比 + 晕影升温的平滑热档，重击（`ScreenShake` 强度阈值）脉冲泛光/晕影，冲刺驱动既有径向模糊通道的速度模糊；高质量档加二级宽晕。**中性态逐位等于原静态调色**（uniform 为 0 时不产生任何永久观感偏移），额外采样仅在档位非零时发生，空闲零 GPU 路径不变。
- **人工过目**：以上为窗口化过目项（无头门禁只保证不崩与全周期标记）。

### 2.9 打击感层（命中顿帧与震动 trauma）（2026-09-14 追加，取值已定稿）
取值经人类认可为定稿：四档 **0.03 / 0.07 / 0.11 / 0.16s**、冻结倍率 **0.06**、
trauma 参考振幅 **24**、衰减 **1.5/s**。反转需显式改值（或由玩家在辅助页调整强度）。

打击感由两层构成：命中定格（顿帧）与 trauma 震动模型。「数值上难、观感上平」的成因正是缺这两层。
纯表现层，**玩法判定零改动**（顿帧与 trauma 的请求一律在伤害/击杀结算**之后**发出，不参与任何判定与随机序列）。

- **时间缩放合成器**（`GameFeelService`）：`Engine.TimeScale` 原有唯一写入者是 Boss 狂暴子弹时间（Main 编排演出）。
  两路各自直写会互相覆盖（顿帧把子弹时间顶回 1.0、或子弹时间把顿帧顶掉），故合成收口——Main 只上报
  自己的演出倍率（`SetEnrageTimeScale`），实际写入 = 演出倍率 × 顿帧倍率。终态复位统一走 `ResetTimeScale`。
  **暂停一并复位**：`SetTreePaused(true)` 走单一复位口连冻结与演出倍率一起清（只清顿帧会让整棵 Always UI
  以 0.24 倍速播放——狂暴子弹时间内开暂停页/设置页/天赋面板的表现是「菜单卡死」）；恢复后 Main 在慢速段
  续报倍率，演出从暂停点继续，不被吃掉。
- **命中顿帧分档**：普通命中 / 暴击（`Bullet` 直击分支，复用既有单次暴击 RNG 结果不重掷）/ 击杀（`Enemy.Die`）/
  重击（玩家受击）四档，时长在 `balance.json effects.hit_stop.*`（**`freeze_scale` 是冻结期间的时间倍率，
  不是强度**——求严格 >0，取 0 会让帧长恒零、剩余时长永远推不完而冻死）。同帧多次请求取**较大者**不叠加。
  推进用**反解出的真实帧长**（`delta ÷ 上一帧写入的倍率`）——按缩放 delta 推进会随冻结一起变慢乃至不归零。
- **震动 trauma 模型**（Eiserloh 惯例）：震源只累加创伤值，位移 = 最大振幅 × `trauma^2`（`CameraShake` 每帧采样）；
  小额冲击近乎无感、大额才猛烈，高频抖动不再叠加成持续晃动。衰减与顿帧同时序按真实帧长推进。
  震源表在 `effects.shake.*`，语义是**创伤增量**（不是像素振幅），另有 `recovery`（衰减速率）
  与 `reference`（归一化参考振幅，缺省与 `boss_seq_final` 同量纲）。
- **设置项**：辅助页「命中顿帧强度」（0..1，0 = 完全关闭；按比例**缩时长**而非改冻结倍率——倍率逼近 0
  会让帧长趋零、采样与输入一起失真），与「减少闪光」「屏幕震动强度」并列，`HitStopScale` 落 settings.json。
  **三项互不连坐**（2026-09-17 收口）：屏幕震动强度只作用在**运动**（trauma 与像素位移），广播给画面增强层的
  震源强度取原始值——两者的语义分别是「运动」与「频闪」，滑块关掉震动不该连带关掉重击泛光（否则把强度调到
  1/3 以下时重击脉冲完全消失，而「减少闪光」明明是另一项设置）。
- **回归面**：`--feel-probe`（冒烟第七趟）请求顿帧与震动后断言「时间缩放被压低 → 自行复位 → trauma 归零」；
  顿帧写坏的表现是**画面永久定格**，无头下不崩也不报错，只有完成标记抓得住。
  时序与 trauma 数学下沉 `csharp/core/GameFeel/`（`HitStopTimeline`/`TraumaShake`）并由单测钉住（含「零真实帧长不得结束顿帧」）。
- **人工过目**：四档时长与 trauma 量级的实际手感为窗口化过目项。

### 2.10 时间基准：模拟时间与真实时间（2026-09-15 立规）

**两套时钟，各管一类量，不得混用。** 模拟时间＝受 `Engine.TimeScale` 缩放的 `delta` / `Engine.GetPhysicsFrames` / `GetProcessFrames`（固定步长下帧数＝模拟时长，可重复）；真实时间＝墙钟 `Time.GetTicksMsec` 等（随机器与真实耗时走）。判定与模拟**一律**模拟时间（AGENTS §5 第 2 条）；真实时间只在下列「引擎之外的现象」上合法，且必须逐点登记在 `scripts/ci/check_realtime_allowlist.sh` 的 `ALLOW` 表（单源，本文件不复述清单）。

- **为何要两套而不能只留一套**：把它们统一成模拟时间，会让「人机窗口 / 混音节流 / 性能读数」这类本属现实世界的量，在时间缩放或回调停摆时被拉伸乃至永不结束——返航输入宽限就是这样：`750490b` 为修「实战按住 WASD 秒跳过过场」特意引入 1.2s 常数的真实时间宽限，`71e6324` 又实证了「测试按模拟帧等待会与墙钟宽限脱节致跳过被静默忽略」。反过来把它们统一成真实时间，则判定会依赖机器速度，探针不可复现。**所以是两套，不是一套。**
- **划线标准（一句话）**：问这个计时点等待 / 限制的是「引擎之外的现实世界」（人的手、输入队列、声波、机器耗时）还是「游戏世界内部已推进多少」——前者真实时间，后者模拟时间。常数若以「人类秒」为单位（1.2s 宽限、0.5s 守卫、250ms 节流），通常就是真实时间的信号。
- **各基准归属**：真实时间＝返航过场输入宽限 / 标题屏输入守卫、音效最小触发间隔、开机耗时读数、设置页帧率与刷新率读出、打击感的真实帧长推进（`HitStopTimeline`「零真实帧长不得结束顿帧」即此）；模拟时间＝帧级去重与缓存（`FrameCache` 等 `GetPhysicsFrames` 族）、事件/波次编排时间轴、表现层脉动相位、右摇杆准星积分（`GetProcessFrames` 门控 + `GetProcessDeltaTime` 配对）。
- **维护方式**：门禁双向判定——新增未登记的真实时间使用判红，**已登记站点消失也判红**（防合法墙钟被「顺手改成模拟时间」，正是同一处宽限在 1.5 个月内被反方向改了三次的形态）。改动这些位置时先改 `ALLOW` 再改代码。`csharp/core/` 出现任何真实时间命中即红（纯逻辑层可单测的前提）。
- **回归面**：`--return-probe`（冒烟第十趟）走生产蓄力链（长按 `homecoming` 蓄满，不直调过场）触发返航后分三段判定——宽限内跳过被忽略；推进 90 帧（1.5 模拟秒，越过生产宽限 1.2s）后**仍**被忽略，这一条是真实时间基准的判别式（若宽限被误按模拟时间计，此刻会放行）；越过宽限后跳过生效且收尾落基地并保持树暂停。探针**不等真实时间**（`--fixed-fps` 下与帧数脱节），另用墙钟做环境过慢的前置守卫，慢到无法判别时显式报错而非静默放过。

### 2.11 数值单源的两道判据（2026-09-15）

balance.json 是数值唯一来源，但一批「表式」数值被手抄进 C# 当 `Cfg(key, 代码内默认值)` 的回退实参——json 完整时读 json，看不出代码默认值已分叉，只有 json 缺失/损坏才回退到错值。两道门禁分工：

- `check_balance_keys.sh` 判**键存在**（键名写错会静默回退默认值）；`check_code_defaults.sh` 判**取值一致**（15 张登记表 256 个缺档默认值 == json 定稿值，含移动策略专属参数与三元素数组）。两侧改一侧不改即红。
- 登记表逐张声明预期条目数与 `文件:符号`，取不到判据（文件/符号/键缺、条目数不符、零比对）一律红——防「表被改名后零比对静默判 clean」。
- 同一事实的**读取面**也收口：未经 balance 覆盖的默认表（`Spawner.BuildEnemyTypes` / `BuildEliteTypes`）只许 `Spawner` 内部读取，外部直读即红（教程曾如此，是「正局跟随、教程不跟」的根因）。

*玩法设计意图修订唯一入口；历史修订轨迹见 git 历史。*
