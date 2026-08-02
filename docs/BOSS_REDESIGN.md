# Boss 行为重设计文档（BOSS_REDESIGN）

本文档是 Boss 行为重设计的单一事实源：现状审计、参考模式、重设计方案、实施分期与测试计划。
**任何 Boss 行为改动必须先对齐本文档；实施阶段的变更回写本文档。**
重设计前的旧行为移植自 Python 原作（对齐记录已归档于 `docs/archive/PORTING_PARITY.md`，仅作溯源参考）；
本重设计是独立演进，演进决策记录见 §7.3。

---

## 1. 现状审计（2026-07-28，依据 `scripts/boss.gd` 与 `data/balance.json` boss 段）

### 1.1 现状结构

> 本节为重设计前的旧行为快照，**已被本重设计取代（2026-07-28 全三期落地，见 §8 实施记录）**，仅作偏离对照保留。

- 3 型轮换：1 重装（strafe 150，5 路扇形/追踪弹交替，1.6s）/ 2 游击（冲刺 400，3 连狙 0.12s 间隔，1.8s）/
  3 母舰（strafe 60，旋转 cross 0.9s + 每 6s 召唤 2–3 小怪）。HP = 800 × [1.3, 0.7, 1.6] × 难度。
- 狂暴（HP<30%）：**三型共用同一序列**——锁血 + 冻结玩家移动，子弹时间 1.2s → 绕玩家快照点方形→圆形轨道 6s
  （每 0.7s 一波 4 激光+8 环弹）→ 0.7s 密集慢速弹幕 → 0.8s 归位 → 常规阶段射速×1.5/移速×1.3。
- 逃跑：入场 50s 未击杀触发，最后 3s 警告上飘，无奖励。
- 难度三档：只乘 HP 与全局刷怪参数，**Boss 弹幕节奏/弹数/弹速不随难度变化**。

### 1.2 问题清单

| # | 问题 | 证据 |
| --- | --- | --- |
| P1 | **狂暴无型号身份**：三型共用同一序列，最高潮时刻打法完全一致，且每场战斗重复 | boss.gd `_update_enrage_sequence` 无 boss_type 分支 |
| P2 | **常规阶段是节拍器**：HP 100%→30% 打法零变化；每型仅 1–2 个攻击按固定间隔轮转，无模式循环、无压力曲线 | `_fire_timer` 固定 `FIRE_INTERVALS`，`_fan_next` 二元交替 |
| P3 | **重攻击零预警**：650 弹速自机狙 3 连发（0.12s 间隔）、380 扇形、追踪弹全部瞬发，无前摇/瞄准线/蓄力表现，公平性差 | `_fire_sniper/_fire_fan/_fire_homing` 直接出弹 |
| P4 | **狂暴冻结玩家移动约 6s**：弹幕密度最高的时刻剥夺躲避手段，靠锁血和运气通过，挫败感强（旧行为，本次重设计对象） | `_lock_player_movement` 覆盖 TRANSITION+ACTIVE |
| P5 | **难度只加血**：hard = 更长的海绵战而非更丰富的压力，easy/hard 弹幕完全相同 | `setup()` 仅 HP 乘难度；`FIRE_INTERVALS` 等无难度维度 |
| P6 | **走位一维**：固定 y=230 水平往返，玩家蹲正下方跟踪 x 即最优解，无位置博弈、无纵向往复 | `_move_strafe/_move_dash` 只写 position.x |
| P7 | **逃跑计时不可见**：仅最后 3s 警告，低 DPS 构建被惩罚却不知原因 | `_show_escape_warning` 在 47s 才出现 |
| P8 | **血条无阶段信息**：玩家无法感知模式切换点（参考模式 §2 的 variety 原则） | `hud.show_boss_bar` 只画连续血条 |
| P9 | **战斗节奏无收尾爆发**：狂暴结束后回到提速节拍器直至击杀/逃跑，击杀前没有「最后一搏」的压力峰值 | RELEASE_HOLD 仅 0.7s 慢速弹幕 |

## 2. 参考模式（同类项目调研）

主要来源：Michael Molinari《Video Game Boss Design For Shmups》（GameDeveloper，2010），
辅以 Touhou（符卡阶段制）、Cave 系（虫姬/ESPGaluda2 收尾爆发）、Ikaruga（终章加速）的公认做法。

提炼五条适用原则：

1. **Variety / 阶段化**：Boss 战按 HP 分段，每段一个独立攻击模式组并在组内循环；血条可视化分段，
   玩家能预见模式切换点。不同模式应迫使玩家改变躲避/输出策略（换位置、换节奏），而非换皮。
2. **Telegraph / 公平性**：一切高威胁攻击必须有可读的预警（蓄力、瞄准线、音效），强度与威胁成正比。
   「看起来致命」必须「给机会躲」。
3. **压力曲线**：战斗结尾强度必须高于开头（Cave 式临死爆发）；中间段落张弛交替而非恒定节拍。
4. **Length / 时长纪律**：Boss 战时长与前置流程成比例；熟练玩家可以快一倍，懵懂玩家不应慢四倍
   （本项目已有 50s 逃跑兜底，保留）。
5. **Character / 型号身份**：每个 Boss 的攻击、移动、受击反馈、死亡演出都应服务于它的「角色」，
   高潮阶段（狂暴）是身份最强表达处——三型必须差异化。

## 3. 重设计目标

- **G1 型号身份贯穿全程**：三型各有「角色」（堡垒/猎手/蜂巢），狂暴为各型独有收尾，不再共用序列。
- **G2 全程阶段化**：HP 100%→0 划分为 P1（100–70%）/ P2（70–30%）/ ENRAGE（<30%）三段，
  每段独立模式表循环，段间有明确切换演出；血条加阶段刻度（解决 P8）。
- **G3 一切重攻击有预警**：所有弹速 ≥500 或伤害 ≥20 的攻击配 ≥0.35s telegraph（蓄力辉光/瞄准线/音调）。
- **G4 保留躲避主动权**：狂暴期「冻结移动」改为「强制减速 ×0.35」（玩家仍可位移/射击/冲刺）；
  定身语义从本作移除（演进决策，见 §7.3）。
- **G5 难度影响模式而非只 HP**：模式参数表按难度取（弹数/间隔/弹速分档），HP 倍率保留。
- **G6 逃跑计时可见**：血条最后 10s 显示逃跑倒计时（解决 P7）。

保留不动的骨架：3 型轮换与 HP 基准、50s 逃跑机制、子弹时间狂暴演出框架（main 编排）、
既有弹种伤害基准、Boss 击杀/奖励结算链路、对象池与注册表技术约束。

## 4. 通用机制

### 4.1 阶段框架（替代固定节拍器）

```
FIGHT（常规）
├─ P1：HP 100–70%，模式表循环（每型 2 个模式）
├─ P2：HP 70–30%，模式表循环（每型 2–3 个模式，含 1 个带 telegraph 的重攻击）
│     段切换演出：0.6s 蓄力抖屏 + 音调 + 清自身计时
└─ ENRAGE：HP<30%，各型独有狂暴序列（§5），锁血语义保留（触发→收尾前 HP 钳在 30%）
```

- 阶段切换由 `take_damage` 阈值驱动（沿用现有 ENRAGE_HP_RATIO 模式，新增 P2 阈值 0.7）。
- 每个「模式」= 持续时间（4–8s）或固定波次数，播完切下一个；模式内开火节奏可编程（不再单一间隔）。
- 走位模式与攻击模式解耦：每型每阶段一个走位函数（含纵向分量，解决 P6），攻击在其上叠加。

### 4.2 Telegraph 规范（复用现有构件）

| 预警形式 | 用途 | 实现 |
| --- | --- | --- |
| 蓄力辉光 | 重炮/齐射前摇（0.4–0.6s） | `_glow` 叠加态圆点 scale/alpha tween（过场配方） |
| 瞄准线 | 狙击/冲刺路径（0.35–0.5s） | Line2D 细线 α0.3 闪烁，出弹/启动瞬间消失 |
| 机身泛红 + 音调 | 段切换/狂暴起手 | 现有 `_base_modulate` 变体 + `play_sfx` 变调 |
| 血条阶段刻度 | 模式切换预告 | HUD boss bar 画 2 道刻度线（70%/30%）+ 切换时短闪 |

### 4.3 狂暴期玩家减速（替代定身）

- TRANSITION+ACTIVE 期间 `Player.movement_locked` 不再使用；改为 `player._enrage_slow = 0.35`
  （移动速度乘区，与燃料加速/微调相乘；dash 可用，是脱锁的主动技）。
- 解锁时机不变（RELEASE_HOLD 开始）；`_exit_tree` 兜底复位（沿用 `_unlock_player_movement` 的兜底模式）。
- 配套：ACTIVE 轨道弹速/密度按「玩家可 0.35 速躲避」重新校准（§5 各型给出基准）。

### 4.4 难度分档

模式参数表增加难度维度（easy/medium/hard 三列），作用于：弹数 ±1/±2、开火间隔 ×1.15/×1/×0.85、
弹速 ×0.9/×1/×1.1。HP 的 Boss 击杀 ramp 乘数不变；2026-07-29 平衡修订起 Boss HP 另按难度档
×0.75/×1/×1.5（`GameState.enemy_hp_multiplier()`，与敌机同源，沿用「波次 HP 分档推导 Boss HP」口径）。

### 4.5 逃跑计时可见

血条存在期间，存活 ≥40s 起在血条下方显示倒计时文本（10→0，红色闪烁），到 50s 逃跑逻辑不变。

## 5. 逐型设计

### 5.1 一型「堡垒 Bulwark」（重装）——角色：移动炮台，正面压制

| 段 | 走位 | 攻击模式（循环） |
| --- | --- | --- |
| P1 | 慢速 strafe（150）+ 每 6s 纵向下压 80px 再回（第一次位置博弈） | ① 5 路扇形 ×3 波（现有 fan）② 追踪弹 ×2（现有 homing） |
| P2 | strafe 提速（200）+ 纵向往复 | ③ **蓄力重炮**：0.6s 蓄力辉光 → 3 发高速重弹（700 弹速，伤害 21，间隔 0.25s）④ 5 路扇形加密（7 路） |
| ENRAGE | **旋转堡垒**：子弹时间框架保留；ACTIVE 改为悬停原地顺时针旋转，每 0.5s 一波 12 向旋转环弹（起始角随波次进动），收尾 8 路蓄力重炮齐射（有 telegraph） |

### 5.2 二型「猎手 Stalker」（游击）——角色：刺客，单挑压迫

| 段 | 走位 | 攻击模式（循环） |
| --- | --- | --- |
| P1 | 现有冲刺（400，0.5s/0.7s 节奏） | ① **3 连狙加瞄准线**：0.35s 瞄准线锁定（线随玩家微跟踪 0.2s 后固定）→ 3 连发（现有 sniper） |
| P2 | 冲刺更频（0.4s/0.5s） | ② **冲刺掠过**：0.5s 瞄准线 → 高速横穿玩家所在高度（机身撞击判定保留，路径拖 3 枚减速弹）③ 3 连狙 |
| ENRAGE | **猎杀环绕**：子弹时间框架保留；ACTIVE 改为在玩家快照点轨道 4 个象限点依次瞬停，每点 0.3s 瞄准线 + 单发致命狙（弹速 900，伤害 21），共 6 点；收尾回到轨道底部释放 12 向慢速环弹 |

### 5.3 三型「蜂巢 Hive」（母舰）——角色：指挥官，以多打少

| 段 | 走位 | 攻击模式（循环） |
| --- | --- | --- |
| P1 | 极慢 strafe（60）+ 缓慢下压/回升（y 200–280 正弦） | ① 旋转 cross（现有）② 每 6s 召唤 2–3 小怪（现有） |
| P2 | strafe 100 + 纵向往复 | ③ **编队齐射**：召唤 4 小怪列横队，0.8s 后齐射一轮自机狙（小怪普通弹，弹速 420）④ **弹幕墙**：10 路低速扇形墙（留 2 个缺口，弹速 220，伤害 12） |
| ENRAGE | **倾巢**：子弹时间框架保留；ACTIVE 每 1.2s 放一波 3 小怪（共 3 波）+ 自身每 0.9s 一圈 8 向环弹；收尾一次性 16 向慢速环弹 + 全部在场小怪齐射 |

### 5.4 通用收尾爆发（原则 3）

每型 ENRAGE 的 RELEASE_HOLD 阶段为该型「最后一搏」峰值（上表已含），随后 RETURN 归位，
常规循环进入「余怒」态：射速 ×1.3（原 ×1.5 下调，因为狂暴本体已强化）。

### 5.5 P2 走位升级实现设计（2026-08-02，D05 落地）

> 背景：§5.1-5.3 逐型走位表的 P2 阶段升级自阶段 B 起未实现（2026-08-02 复核登记为 D05）。
> 本轮按表格补实现，纵向分量统一为**正弦往复**，复用项目内 `EnemyMoveStrategy` 既有正弦形态
> （`anchor + Enemy.sin_fast(time * freq + phase) * amp`）与 `BossMovement._update_press` 的
> 增量式 y 施加模式——不新建走位原语。

#### 调研与引用（2026-08-02）

- **BulletML 移动原语**（[官方参考](http://www.asahi-net.or.jp/~cs8k-cyu/bulletml/bulletml_ref_e.html)）：
  移动 = 增量式 `changeDirection / changeSpeed / accel`（term 帧内线性过渡到目标值）的组合——
  正弦/往复由增量调整派生，无需独立"波形"概念。本项目 `_update_press` 的"每帧施加
  `target - _press_offset` 差值"即同一范式（已存在，复用）。
- **项目内 `EnemyMoveStrategy`**（`scripts/enemy_move_strategy.gd`）：`SineMove`/`HoverMove` 用
  `Enemy.sin_fast(time * freq + phase) * amp` 表达正弦（查表零分配，C05/C09 收口口径）。
  Boss 纵向往复直接同族复用，不重复造轮子。
- **Danmaku 设计原则**（[The Anatomy of a Shmup](https://www.gamedeveloper.com/design/the-anatomy-of-a-shmup)、
  [Danmaku Design Discussion](https://www.shrinemaiden.org/forum/index.php?topic=6649.0)）：
  ① 走位与弹幕解耦（Boss 移动自成节奏，不跟弹幕间隔绑死）；② 阶段升级的压迫感来自"移动自由度
  增加"（P1 单向 → P2 双向）；③ 纵向往复幅度与周期保持小幅度慢节奏，避免挤压玩家活动空间
  （`strafe_range` 横向区间不变，纵向只在锚线邻域摆动）。

#### 语义解读

- **§5.3 三型 P1「y 200–280 正弦」**：解读为**周期下压/回升**（同 §5.1 一型 P1「每 6s 下压 80px 再回」同构，
  幅度更大、周期更长、更缓慢）——机身从锚线正弦下压到**锚线下 200px~280px 区间**（下压深度中心 240、
  轨迹邻域摆动 ±40、周期 9s 慢呼吸）再缓慢回升。理由：锚线本身在 `view.position.y + FIGHT_Y(230)`，
  若 200-280 为绝对坐标将与锚线几乎重合（无下压效果）；"下压/回升"为周期动作语义，且从锚线起步
  （target 从 0 渐变）避免段切换/入场跳变，与 `_update_press` 增量式同构。
- **一型 P2「纵向往复」**：围绕锚线 ±40px 双向正弦（区别于 P1 的单向周期下压），周期 6s
  （与 P1 `press_interval` 同节奏感），段切换相位从 0 起（sin 0 = 0，与 `reset_press()` 衔接无跳变）。
- **三型 P2「纵向往复」**：围绕锚线 ±50px 双向正弦，周期 8s（母舰更慢的俯仰）。

#### 参数表（新配置键，balance.json `boss.movement` 段）

| 键 | 默认值 | 语义 |
| --- | --- | --- |
| `type1_p2_strafe` | 200 | 一型 P2 横向 strafe 速度（P1 = `strafe_speeds[0]` 150） |
| `type1_p2_bob_amp` | 40 | 一型 P2 纵向正弦幅度（±px，围绕锚线） |
| `type1_p2_bob_period` | 6 | 一型 P2 纵向正弦周期（s） |
| `type2_p2_dash_time` | 0.4 | 二型 P2 冲刺持续（P1 = 0.5） |
| `type2_p2_rest_time` | 0.5 | 二型 P2 冲刺休息（P1 = 0.7） |
| `type3_p1_bob_min` | 200 | 三型 P1 下压深度区间下界（锚线下 px） |
| `type3_p1_bob_max` | 280 | 三型 P1 下压深度区间上界（锚线下 px） |
| `type3_p1_bob_period` | 9 | 三型 P1 下压/回升周期（s，与模式循环错开避免节奏死板） |
| `type3_p2_strafe` | 100 | 三型 P2 横向 strafe 速度（P1 = `strafe_speeds[2]` 60） |
| `type3_p2_bob_amp` | 50 | 三型 P2 纵向正弦幅度（±px，围绕锚线） |
| `type3_p2_bob_period` | 8 | 三型 P2 纵向正弦周期（s） |

全部为**游戏性范围族**（不乘 `world_scale`），走位坐标系基于 `fight_anchor_y()` / `strafe_range()` view 基线。

#### 实现要点

- `BossMovement` 新增 `_move_bob(delta, boss, amp, period)`（P2 纵向正弦往复：直接设置
  `position.y = fight_anchor_y() + sin(phase)*amp`——`_in_fight` 后才被调用，入场/逃跑/狂暴序列均
  早退不干扰；`fight_anchor_y()` 逐帧求值支持战斗中切视角档）与
  `_move_band(delta, boss, y_lo, y_hi, period)`（三型 P1 缓慢下压/回升：`_update_press` 同构，
  target 为纯偏移从 0 起步，无初始跳变）；相位经 `_bob_phase` 累计（`TAU * delta / period`），
  段切换 `reset_press()` 归零相位。
- 一型 `update()`：P2 分支 `_move_strafe(type1_p2_speed)` + `_move_bob(amp, period)`；
  P1 分支维持 `_update_press`（单向周期下压）。
- 二型 `update()`：dash 节奏按 `fight_phase` 取 0.5/0.7（P1）或 0.4/0.5（P2）。
- 三型 `update()`：P1 加 `_move_band(200, 280, 9)`（strafe 60 不变）；P2 用 `_move_strafe(100)` +
  `_move_bob(50, 8)`。
- 速度全部经 `slow_factor()` / `_enrage_speed_mult()` 乘区（与既有走位一致）。
- 配置读取在 `Boss._ready` 统一缓存为实例字段并注入 `_movement`（A5 依赖注入模式），
  新增键同步脚本回退默认值（AGENTS.md 一致性约定）。

#### 测试影响

- `boss_phase_test` 场景 1（一型）C11「P2 段切换后回到锚线」断言：P2 有正弦后，切换瞬间
  sin 0 = 0 仍回锚线，但**后续若干帧 y 开始偏离**——断言若在切换后等待多帧再检查会失配，
  需改为"段切换后 y 在锚线 ±amp 范围内"。
- 新增断言：一型 P2 纵向 y 波动（采样帧内出现 y > anchor 与 y < anchor）；二型 P2 dash 节奏
  （0.4/0.5 周期验证）；三型 P1 y ∈ [anchor+200, anchor+280]（采样验证区间呼吸）。
- 走位数值断言尽量用实例常量/配置读值（C34 口径），不硬编码。

## 6. 数值与配置（balance.json boss 段重构）

- 保留现有键不动（兼容/回退），新增 `boss.phases` 段：每型每阶段的模式表（时长/波次/弹数/弹速/间隔 ×难度三列）、
  telegraph 时长、纵向走位参数。脚本默认值与 JSON 保持一致（AGENTS.md 约定）。
- 新增 `boss.enrage.player_slow`（0.35）、`boss.enrage.type_*` 各型狂暴参数子段；原 `boss.enrage.*`
  公共时序（子弹时间/轨道半径等）保留。
- 新增 `boss.escape.countdown_visible_from`（10.0）。

## 7. 实施分期、测试与兼容

### 7.1 分期

- **阶段 A（框架先行）**：Boss 状态机重构为阶段表驱动（P1/P2/ENRAGE 切换 + 模式循环 + telegraph 机制 +
  血条刻度 + 逃跑倒计时 + 减速替代定身）；三型先用**现有攻击**填表（狙击加瞄准线），狂暴暂保持共用序列。
  出口标准：全部回归测试绿 + 新阶段断言测试绿。
- **阶段 B（逐型模式库）**：实现 §5 各型 P2 新攻击与差异化狂暴（蓄力重炮/冲刺掠过/编队齐射/弹幕墙/三型专属狂暴）。
- **阶段 C（数值与验证）**：难度分档参数、TTK/压力校准（autoplay 480s 探针 + 人工游玩）、性能复测。

### 7.2 测试计划

- 新增 `test/boss_phase_test.tscn`：阶段阈值切换、模式循环推进、telegraph 时序（先线后弹）、
  减速生效与复位（含 Boss 死亡/逃跑/离场兜底）、逃跑倒计时显示、难度分档取值。
- 重构 `test/boss_enrage_test.tscn`：定身断言改减速断言；阶段 B 再补三型差异化断言。
- 回归：`hit_logic_test`、`difficulty_test`、`smoke_test`、`autoplay_test`（[ANOMALY] 探针）、`--quit-after 300`。

### 7.3 演进决策记录（曾登记于 PORTING_PARITY，2026-07-30 已归档）

1. 狂暴期玩家定身 → 强制减速 ×0.35（P4，定身语义不再保留）。
2. 三型共用狂暴序列 → 各型专属狂暴（P1）。
3. 固定节拍器 → HP 阶段模式表（P2）。
4. 瞬发狙/扇形 → telegraph 前摇（P3）。
5. 难度只乘 HP → 模式参数分档（P5）。
6. `FIGHT_Y` 绝对锚点（y=230 写死）→ 距可见区域顶缘偏移（2026-07-30 战斗 UX 审计 P0-1）：新增
   `_fight_anchor_y()` = `GameState.view_world_rect().position.y + FIGHT_Y`，三处使用点
   （入场停线逐帧求值、P2 冲刺 RETURN、狂暴 RETURN）统一改走它，支持战斗中途切视角档；
   与 `_strafe_range()` 的 view 边距处理对齐，zoom=1 时行为逐位不变。

### 7.4 兼容约束

- 存档不含 Boss 状态（save_run 只存分数/燃料/时间），继续对局时 Boss 按调度重新入场，无迁移问题。
- 精英炮塔事件/轰炸编队事件的 Boss 互斥钩子（`_boss_frozen/_boss_pending`）不动。
- 子弹池/注册表/性能预算（draw call、零堆分配热路径）沿用 AGENTS.md 约束；
  telegraph 节点随出弹销毁，不走常驻 `_process`。

---

## 8. 实施记录（2026-07-28，三期全部落地）

### 8.1 分期完成情况

- **阶段 A（框架）**：`scripts/boss.gd` 重构为 FightPhase（P1/P2/ENRAGE）阶段框架 + 模式表 `_patterns`
  （`boss.phases.typeN` 配置 + DEFAULT_PATTERNS 回退）；telegraph 构件（`_charge_glow` 蓄力辉光 /
  `_make_aim_line` 瞄准线）；狂暴期定身改 `_enrage_slow = 0.35` 减速；HUD 血条 70%/30% 阶段刻度；
  血条下逃跑倒计时（存活 ≥40s 起 10→0）。
- **阶段 B（逐型模式库）**：三型 P2 新攻击（蓄力重炮 charged_cannon / 冲刺掠过 dash_sweep /
  编队齐射 minion_volley / 弹幕墙 bullet_wall）+ 三型差异化狂暴（`_update_enrage_sequence`
  按 boss_type 分发，参数 `boss.enrage.type_*`）；`spawner.spawn_minion(pos) -> Enemy` 返回实例。
- **阶段 C（数值与验证）**：难度分档 `_apply_difficulty_scaling()`（见 §8.3）；
  数值验证见 §8.4；文档回写（本节、AGENTS.md）。

### 8.2 实施中的自决点（设计文档未明确处）

- 阶段 A：段切换演出期间停火时长 = 新模式首波 interval；狂暴后「余怒」沿用 P2 模式表（射速 ×1.3）；
  跨段一击直接触发狂暴时狂暴优先；三型召唤计时与模式表相互独立；逃跑倒计时为纯数字文本（无翻译键）。
- 阶段 B：环弹伤害沿用既有基准 12；二型狂暴瞄准线全程跟踪（非锁定后固定）；一型 TRANSITION
  原地抖动替代轨道入场；三型收尾 RELEASE 一次性结算；dash_sweep 期间模式表计时暂停；
  弹幕墙弧心固定向下（不随玩家方位旋转，仅缺口避开玩家 ±30°）。
- 阶段 C：分档在 `_ready` 配置载入后一次性乘算（非每帧查档）；快照弹幕（main 编排 4 激光 + 8 环）、
  telegraph 时长、机体移速、HP/伤害不分档；弹数钳制下限 wall 6 / ring 4 / 其余 1（fan 下限 3）。
- 修复：`_load_patterns` 对 cfg 返回的共享 JSON 数组必须 `duplicate(true)` 深拷贝，
  否则分档 interval 乘算会污染 GameState 配置缓存、叠加到后续 Boss 实例（boss_pattern_test 场景 6 捕获）。
  **2026-08-01 复核补充**：同类污染仍存在于 `FIRE_INTERVALS`（`boss.gd:420-421` 读共享数组、
  `:522-523` 原地 `[i] *= interval_mult`，easy/hard 下跨 Boss 复合叠加）——`_apply_difficulty_scaling`
  对 `FIRE_INTERVALS` 需同样先 `duplicate(true)`，已登记 AUDIT_VAULT B5。
- 走位简化（**2026-08-02 复核登记，D05**）：§5.1-5.3 逐型走位表要求的 P2 阶段升级（一型 P2「strafe 提速 200 + 纵向往复」、二型 P2「冲刺 0.4s/0.5s」、三型 P2「strafe 100 + 纵向往复」、三型 P1「y 200-280 正弦」）至今未实现——`boss_movement.gd` 仅一型 P1 有纵向分量（`_update_press` 仅 `FIGHT_P1` 调用）、二型 dash 无阶段区分、三型无纵向。经 `git show 3188902^` 核实此差距为阶段 B 落地时即有（非 A3 拆分引入）。**2026-08-02 同日已按 §5.5 落地修复（见上）**，本登记转为实现记录。

### 8.3 难度分档落地（§4.4）

- 配置：`boss.difficulty_scaling`（`interval_mult` [1.15, 1.0, 0.85]、`speed_mult` [0.9, 1.0, 1.1]、
  `counts` 逐参数 [easy, medium, hard] 增减量：fan/homing/cannon/volley/summon/drops ±1，wall/ring/salvo ±2）。
- 实现：`Boss._apply_difficulty_scaling()` 在 `_ready` 末尾统一乘算——模式表 interval、
  FIRE_INTERVALS、CANNON/ENRAGE/E1/E2/E3 各内部节奏 ×interval_mult；全部攻击弹速 ×speed_mult
  （不含快照弹速与机体移速 SWEEP_SPEED）；弹数按 counts 增减并钳下限；fan/homing2 在 `_execute_attack`
  分发处取 `_d_fan/_d_homing`。档位 = `GameState.DIFFICULTY_ORDER.find(GameState.difficulty)`，未知回退 medium。

### 8.4 数值校准验证结果

- 断言测试：`boss_phase_test` / `boss_enrage_test` / `boss_pattern_test`（含 easy/hard 分档场景）全绿；
  回归清单（hit_logic / difficulty / smoke / enemy_combat / elite_turret_event /
  formation_strike_event / base_system / `--quit-after 300` / `--import`）全绿。
- autoplay 480s 探针与 perf_bench 结果见当次提交说明/报告；TTK 与手感属人工游玩校准范畴，
  无人工游玩条件时以探针无 [ANOMALY] 与帧耗时量级为准。
