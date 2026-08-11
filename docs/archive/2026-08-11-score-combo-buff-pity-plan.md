# 得分/奖励设计审核与改动计划（2026-08-11）

> 2026-08-11。目标：对游戏设计逻辑做缜密审核，以**业内普遍使用的平衡设计**为参照，增加游戏性的同时控制复杂度（只做加法，不做系统级重构）。

## 1. 审核范围与方法

- 通读 `docs/DESIGN_BASELINE.md` / `docs/BALANCE_MAP.md` / `docs/archive/ENDLESS_BALANCE_PLAN.md` / `data/balance.json` 全量数值，并核验关键实现（计分路径、Buff 池、DDA、HUD、测试登记）。
- 对照维度：STG 得分攻击惯例（怒首领蜂/虫姬/Crimson Clover 链式连击、擦弹）、roguelite 奖励保底惯例（杀戮尖塔稀有卡/吸血鬼幸存者治疗保底）、渐增压力+软性兜底（DDA/逃脱阀）。

## 2. 审核结论

### 2.1 已确认平衡良好、不改（记录结论）

| 项目 | 结论 |
| --- | --- |
| 无尽必死曲线（D1） | `mult = 1 + 0.6×bk + 0.075×⌊t/30⌋`，HP/dmg ramp 0.25/0.20，校准于 2026-08-04，3×900s 探针 0 anomaly——与"敌方增长压过玩家"的行业范式一致 |
| DDA 受击降档 | 受击 5s 内敌弹/波次间隔 ×1.3，只拉间隔不降收益——rubber-band 兜底的标准形态 |
| 50s Boss 逃脱阀 | "打不死就逃"防拖时间，与必死曲线配套 |
| 擦弹得分 | 10×难度，一次/弹——经典风险收益（贪分≈送死），已落地 |
| hard ×1.5 里程碑阈值 | D2 有意为之（避免稀疏 buff 节奏），保持 |
| 19 Buff 数值 | 无致命陷阱项；crit_shot 期望 +36%/3 层略低于 power_shot 但封顶更早，可接受 |

### 2.2 发现的问题与改动

**P1 — 得分系统缺少"击杀链"维度（游戏性缺口）**
- 现状：击杀分 = 固定值 × 难度倍率，击杀之间无关联。纯得分游戏（score-only）缺少 STG 得分攻击最核心的"贪 vs 稳"博弈。
- 行业参照：怒首领蜂 chain、虫姬/Crimson Clover 连击、Ikaruga 连锁——窗口内连续击杀放大得分，受击/断档清空。
- 改动：**击杀连击计分**（见 §3.1）。窗口内连杀 → 击杀分 × 连击乘区；超时/受击断连。受击断连与 DDA 共用同一事件源（受击=降档+断连双通道，均不致命、互相独立）。
- 复杂度评估：一个计时器 + 一个乘区 + HUD 标签 + 一处击杀路径接入，纯加法。

**P2 — Buff 三选一无情境权重（公平感缺口）**
- 现状：候选池均匀洗牌取 3，低血时可能三张全攻——挫败感；防御卡在满血时价值低、低血时又抽不到。
- 行业参照：杀戮尖塔低血出防御倾向、吸血鬼幸存者治疗保底——情境权重/保底是 roguelite 通用平衡手段。
- 改动：**低血防御保底**（见 §3.2）。HP < 50% 时防御类候选加权 ×2，且 3 张候选保证至少 1 张防御（若有可用防御卡）。
- 复杂度评估：仅 BuffSelect 选择逻辑一处。

## 3. 改动设计

### 3.1 击杀连击计分（`scoring.combo` 段，新键）

规则（保持简单，无额外 UI 系统）：

- 敌机击杀（普通/精英/分裂子机/编队机，即所有带击杀分的敌机）→ 连击 +1，刷新窗口计时；击杀分 × 连击乘区后走原 `AddScore` 链路（难度倍率不变）。
- 连击乘区：`mult = min(1 + (combo−1)×step, max_mult)`；combo=1 → ×1.0，combo=10 → ×1.9，combo=11+ → ×2.0 封顶。
- 断连：窗口内无新击杀（超时）、受击（`PlayerDamaged`，与 DDA 同源）、死亡/重开（`ResetRun`）。
- Boss 击杀不计连击（不加也不断；Boss 战间隙天然超时断连）。
- 事件奖励分（精英炮台完成、编队全清）与擦弹分不参与连击（保持现有经济语义）。
- HUD：得分区旁新增连击标签，`×1.0` 以下隐藏；乘区变化即时刷新，断连即隐。
- 数值：`window` 3.0s（约一个波次内的击杀节奏）、`step` 0.1、`max_mult` 2.0——普通玩家稳态约 ×1.2~1.4，高手封顶 ×2；对里程碑节奏影响 ≤30%，不破坏 D1 曲线（玩家成长仍被 19 Buff 上限约束）。

接口：

- `GameState.AddKillScore(int basePoints)`：连击推进 + 乘区计分（击杀路径唯一入口）。
- `GameState.Combo` / `ComboMultiplier()` / `ResetCombo()`（受击、测试用）。
- 信号 `ComboChanged(int combo)`：HUD 消费。

接入点（改一行调用）：`Enemy.cs Die()`（普通/精英/分裂子机）、`FormationStrikeEvent.cs` 编队机击杀。

### 3.2 低血防御保底（`buffs.dynamic_weight` 段，新键）

规则：

- 满血/高血（HP ≥ 50%）：现状均匀洗牌，零变化。
- 低血（HP < 50%）：防御类（`ids`：extra_life/regen/armor/shield/evasion）候选按 `weight`(2.0) 加权展开后再洗牌选 3；若 3 张中无防御卡且候选池存在可用防御卡 → 随机替换 1 张为防御卡（保底）。
- 防御卡全部满层/锁定：保底自然失效，不额外处理。
- 数值：`enabled` true、`hp_ratio` 0.5、`weight` 2.0、`ids` 5 项。

接口：`BuffSelect.SelectCandidates()`（加权+保底选择，供测试直调；`OnMilestoneReached` 改用之）。

### 3.3 不做（复杂度预算外）

炸弹资源、掉落物、技能树重构、Boss 新机制、雾事件奖励化——均为系统级改动，超出"不过多增加复杂性"约束。

## 4. 文件清单

| 文件 | 改动 |
| --- | --- |
| `data/balance.json` | 新增 `scoring.combo`、`buffs.dynamic_weight` |
| `csharp/godot/GameState.cs` | `[Signal] ComboChanged(int)` |
| `csharp/godot/GameState.State.cs` | 连击状态/乘区/计时/`AddKillScore`/`ResetCombo`；`ResetRun` 清零 |
| `csharp/godot/GameState.Difficulty.cs` | 受击断连（`OnPlayerDamagedDda` 处） |
| `csharp/godot/Enemy.cs` | `Die()` 击杀分走 `AddKillScore` |
| `csharp/godot/FormationStrikeEvent.cs` | 编队机击杀走 `AddKillScore` |
| `csharp/godot/BuffSelect.cs` | `SelectCandidates()` 加权+保底 |
| `csharp/godot/Hud.cs` + `scenes/main.tscn` | 连击标签 |
| `data/translations.csv` | `UI_COMBO_FMT` |
| `csharp/godot/tests/ComboTest.cs` + `test/combo_test.tscn` | 新断言场景 |
| `csharp/godot/tests/Buff33Test.cs` | 保底断言 |
| `docs/TESTING.md` | 场景计数 56→57 / 65→66 + 列表 |
| `docs/DESIGN_BASELINE.md` / `docs/ROADMAP.md` | §1.3/§1.5 同步、Snapshot/Decisions 登记 |
| `docs/BALANCE_MAP.md` | 生成器重跑 |

## 5. 验收

- `dotnet build` 零警告 + `dotnet test tests-csharp/` + `dotnet format` 三工程零 diff。
- `gen_balance_map.py` 重跑零 diff（新键收录）。
- `--import` 零错误；新增 `combo_test.tscn`、`buff33_test.tscn` 0 FAIL；`smoke_test` 0 FAIL。
- `autoplay_test` 探针（≥300s 固定 seed）0 `[ANOMALY]`（连击抬升得分后不出现"零压力稳态"回归）。
- 全量断言场景（权威计数 `docs/TESTING.md`）0 FAIL。
