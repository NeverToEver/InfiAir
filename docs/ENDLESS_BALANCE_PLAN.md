# 无限流数值改进指引（ENDLESS_BALANCE_PLAN）

> 2026-07-29 立项，源自同日的机制与数值审计。本文是「15 分钟后无限段」数值演进的单一事实源；
> 涉及阶段/方向调整时同步 `docs/ROADMAP.md` 并在 AGENTS.md「文档同步要求」登记。
> 状态：**已实施**——2026-07-29 方案 1~5 全量落地，D1/D2 决策收口（见 §5/§6）。

---

## 1. 审计结论摘要

前 ~15 分钟的体验数值是精心调过的、合理的：分数 → 里程碑 Buff 三选一 → Boss 轮换 → 局内 RP
经济的闭环完整，Boss 50s 逃跑 DPS 检查、狂暴锁血、全轴封顶等设计成熟。

但作为无限流存在结构性缺口：**游戏在第 5 次 Boss 击杀后进入纯稳态，之后既不变难、也没有终点**。

| 轴 | 玩家 | 敌方 | 结果 |
| --- | --- | --- | --- |
| 输出 | DPS 乘算，上限 ×9.5（单体）~ ×38（理论） | 普通敌 HP 封顶 ×1.84 | 中后期小怪秒杀化 |
| 生存 | extra_life 每层 +50 HP（99 层名义上限）+ 吸血回 10% 上限 | 敌弹伤害全程恒定 12~21 | 生存轴单边无限膨胀 |
| 密度 | — | 波间隔硬下限 2.5s、波规模硬上限 5 架 | 压力有硬顶 |
| Boss | HP ×8 全额缩放，与玩家 DPS 上限对齐 | 第 5 杀后同样封顶 | 稳态 |

修订前公式：`difficulty_multiplier = min(1 + (2^min(boss_kills,10) − 1) × 0.25, 8)`——第 5 次击杀即触顶 ×8，
且只由 Boss 击杀驱动、与时间无关。此后一局在理论和技术上都不会结束，score attack 退化为纯时间投入。
（2026-07-29 起被 §4 方案 3/4 的线性 + 时间轴曲线取代，见 §6。）

## 2. 参考模式（成熟项目惯例）

成熟无尽/生存类游戏处理「无限」只有两种范式，本项目目前两条都不占：

- **必死曲线**：敌方成长最终超过玩家，玩家必败，分数才有意义
  （Geometry Wars、传统街机；敌人密度/速度无限 ramp，玩家成长固定）。
- **定时终点**：固定时长后结算或强制终结，期间玩家成长跑赢敌人、享受 power fantasy
  （Vampire Survivors 30 分钟制；终局敌人 HP 按分钟膨胀数千倍，死神强制结算）。

**§5 的范式选择是落地 §4 方案 1/3 的前置决策（2026-07-29 已选定 A 必死曲线）。**

## 3. 问题清单

### P0 — 无限流不可持续

| # | 问题 | 证据 |
| --- | --- | --- |
| 1 | **敌方输出零成长，生存轴无顶对冲**：敌弹伤害恒定；extra_life 后期成为唯一可选卡（其余满层出池），估算每小时净增 750~1000 HP；吸血按上限 10% 回血形成正反馈滚雪球 | `enemies.bullet_damage`、`boss.bullet_damage` 无 ramp 消费；`scripts/buff_select.gd:154-156` 满层出池；`autoload/game_state.gd:498-509` 吸血 |
| 2 | **事件单位不吃难度乘数**：炮塔 HP 恒 80、编队战机 HP 恒 60（只乘难度档），10 分钟后从压力源退化为送分道具 | `scripts/elite_turret_event.gd:127-132`、`scripts/formation_strike_event.gd:115-126` |

### P1 — 曲线形状

| # | 问题 | 证据 |
| --- | --- | --- |
| 3 | mult 公式 2^n 指数：第 4→5 杀从 ×4.75 跳至 ×8（+68%），随后永久封顶，前缓/中断/后平 | `autoload/game_state.gd:470-477` |
| 4 | hard 难度 Buff 节奏反而最快（得分 ×3、里程碑阈值仅 ×1.5），是否意图无文档 | `difficulty.*.score` / `.milestone`、`autoload/game_state.gd:331-343` |
| 5 | 难度只认 Boss 击杀：避战则难度停滞，连杀则陡升；时间/分数轴完全未使用 | `autoload/game_state.gd:470-477` |

### P2 — 配置与文案腐烂

| # | 问题 | 证据 |
| --- | --- | --- |
| 6 | rapid_fire 卡面写「射速提升 25%」，实际间隔 ×0.75 = +33%/层 | `scripts/player.gd:181-182`、`data/translations.csv:36` |
| 7 | `buff_select.gd` 池内 `desc` 为死文本且过时（laser 写 10 伤/10s，实际 16 伤/8s） | `scripts/buff_select.gd:4-101` vs `scripts/laser_weapon.gd:10-11` |
| 8 | explosive 的 per-level 缩放不可达（层数上限锁 1） | `scripts/bullet.gd:155-166` |
| 9 | `player.gd:56` 注释提及「扩容油箱天赋」但无实现，疑似早期版本遗留 | `scripts/player.gd:56,124` |
| 10 | explosive 解锁门槛 `boss_kills >= 3` 硬编码，未进 balance.json | `scripts/buff_select.gd:145` |

## 4. 改进方案（按性价比排序）

> 数值一律进 `data/balance.json`，脚本 `cfg()` 回退值同步；公式中的 k 值均为初稿，落地时以平衡测试标定。

### 方案 1 — 敌方伤害 ramp（P0-1 核心，最低成本恢复张力）【2026-07-29 已全量落地】

- 敌弹/撞体伤害乘 `(1 + k × (difficulty_multiplier − 1))`，建议 k ≈ 0.08（mult=8 时 ×1.56）；
  或按局内时间缓升。消费点：`enemy.gd`、`boss.gd`、编队炸弹。
- 配合 extra_life 设实质上限（建议 10 层 / 总 HP 500 封顶）或改递减收益
  （每层 +50×0.9^n）。当前 99 层上限实际被里程碑指数阈值锁死，属虚设，直接收紧无体验损失。
- **实施记录（2026-07-29，伤害 ramp）**：伤害 ramp 已按 k=0.08 落地——新键 `enemies.damage_ramp_factor`、
  `GameState.enemy_damage_ramp()`；敌弹在 `bullet.gd` 按阵营统一分流（覆盖敌机/Boss/炮塔全部弹种），
  撞体（`enemy.gd`/`boss.gd`）与编队炸弹（`formation_strike_event.gd`）单独接入。
- **实施记录（2026-07-29，extra_life 收紧）**：上限 99→**10 层**（总 HP 100+500=600 封顶）——
  新键 `buffs.extra_life.max_stacks`=10、池内 `max` 同步、卡面文案「可无限叠加」→「最多 10 层」（中英双列）。
  生存轴正反馈由收紧后的 HP 上限 + 方案 3/4 的无限伤害 ramp 共同对冲。

### 方案 2 — 事件单位吃难度乘数（P0-2，一行级改动）【2026-07-29 已落地】

- 炮塔/编队战机 HP 乘 `(1 + enemies.hp_ramp_factor × (mult − 1))`，与普通敌机同口径
  （`elite_turret_event.gd:127`、`formation_strike_event.gd:115`）。
- **实施记录（2026-07-29）**：已落地，统一走新增的 `GameState.enemy_hp_ramp()`。

### 方案 3 — mult 曲线平滑化与去硬顶（P1-3）【2026-07-29 已落地】

- `2^n` 改为线性或对数（如 `1 + 0.5 × boss_kills`）；
- ×8 封顶改缓增长（如 `8 + 0.2 × (bk − 5)`），给后期留持续加压通道。
- 依赖 §5 范式决策：选「必死曲线」则必须去硬顶；选「定时终点」可保留封顶。
- **实施记录（2026-07-29）**：D1 选定必死曲线，采用**完全去硬顶**的线性方案——
  `mult = 1 + progression.per_boss_kill(0.5) × boss_kills + 时间轴分量`（方案 4），
  `GameState._recompute_difficulty()` 统一计算（击杀触发 + 时间档触发 + 存档恢复重算），
  旧 `2^n + ×8 封顶` 公式废弃。随 mult 无顶增长：Boss HP 同步放大（50s DPS 检查自然转化为
  「打不死则逃跑」的压力阀）、`enemies.hp_ramp`/`damage_ramp`/刷怪间隔 ramp 全部获得无限加压通道，
  玩家成长（DPS ×9.5、HP 600）固定，必死曲线成立。

### 方案 4 — 引入时间/分数难度因子（P1-5）【2026-07-29 已落地】

- 如 `mult = f(boss_kills) + elapsed / 600`，权重低即可，堵「避战停滞」漏洞。
- **实施记录（2026-07-29）**：时间分量按 `progression.time_step_seconds`(30s) **量化步进**、
  每 10 分钟 +`progression.per_ten_minutes`(1.0)——即 `floor(run_time/30) × 0.05`，
  量化避免连续漂移（HUD 稳定、测试可钉档）；只计对局存活时间 `run_time`（树暂停不计），
  避战同样持续加压。新顶层配置段 `progression`（per_boss_kill/per_ten_minutes/time_step_seconds），
  `_apply_balance()` 缓存，`_process` 跨档时重算并广播 `difficulty_changed`。

### 方案 5 — 文案与配置清理（P2，独立可先做）【2026-07-29 已落地】

- rapid_fire 描述改 33%（translations.csv 中英双列）；
- 删除或更新 `buff_select.gd` 池内死 `desc` 字段；
- explosive 解锁门槛入 `balance.json`（如 `buffs.explosive.unlock_boss_kills`）。
- **实施记录（2026-07-29）**：三项全做——`BUFF_RAPID_FIRE_DESC` 中英改 33%；
  池内 16 条死 `desc` 字段全删（卡片文本只走 `BUFF_%s_DESC` 翻译键，单一事实源）；
  `buffs.explosive.unlock_boss_kills`=3 入配置。P2-8/P2-9 同日顺手清理：
  explosive 不可达的 per-level 缩放删除（固定值口径与卡面一致），
  `player.gd` 「扩容油箱天赋」死注释改为配置覆盖说明。

## 5. 决策记录（2026-07-29 收口）

| # | 决策 | 结论 | 影响 |
| --- | --- | --- | --- |
| D1 | **终局范式** | **A. 必死曲线**：方案 1+3 去硬顶即达成，无需新增结算流程；符合街机 score attack 定位 | 方案 3 采用完全去硬顶（×8 封顶废弃）；B（定时终点）不采用 |
| D2 | hard 里程碑 ×1.5 是否意图 | **是，有意设计**：`game_state.gd` DIFFICULTY_DEFS 注释载明「避免高难 Buff 节奏过稀」 | 已登记 `docs/archive/PORTING_PARITY.md` 决策 10，数值不动 |

## 6. 实施记录与验收

- 状态：**已全部实施**（2026-07-29）——方案 1（含 extra_life 收紧）、2、3、4、5 全量落地；
  D1/D2 决策收口（§5）。
- 验收（2026-07-29）：全部 26 个断言场景 0 FAIL（含 `--headless --import`、`--quit-after 300`）；
  断言同步：smoke（难度乘数改动态期望）、difficulty（新增进程曲线 §4b 五断言 + 间隔段钉时间档）、
  enemy_combat（逃跑段钉时间档）、hit_logic（A4 敌弹伤害全段改动态期望，同 boss_pattern 口径）。
- 长时探针（2026-07-29，`--autoplay-seconds=300 --seed=20260729`）：**异常 0**；
  300s 处难度乘数 ≈ ×2.5（2 杀 ×2.0 + 10 时间档 ×0.5），压力随时间持续上升、无稳态平台；
  更深无限段（>15 分钟）的实机标定留待后续（§7）。
- 数值类改动必跑：`balance_test.tscn`、`difficulty_test.tscn`、`boss_enrage_test.tscn`、
  `wave_pacing_test.tscn`；落地后用 `autoplay_test.tscn` 长时探针验证后期不再出现
  「HP 单边膨胀、压力归零」的稳态（关注 SUMMARY 中 Boss 击杀数与存活时长）。
- 改动 P2 文案时运行 `i18n_test.tscn`。

## 7. 维护约定

- 本文只覆盖「无限段数值演进」；新 Buff/新敌机等内容立项仍走 `docs/ROADMAP.md` Phase 2。
- 后续标定（per_boss_kill / per_ten_minutes / ramp 系数的实机手感微调）直接改 `balance.json`
  `progression` 段并在本文 §6 追加记录。
