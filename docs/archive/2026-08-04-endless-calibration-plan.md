# Endless k-value Calibration Plan (2026-08-04)

> 被搁置计划重启:ENDLESS_BALANCE_PLAN §7「>15 min calibration deferred」+ DESIGN_BASELINE §8.2 Mid Term「Endless calibration」。本文件为本次执行清单(参照 `docs/archive/2026-08-04-{...}-plan.md` 模式),完成后回填 `ENDLESS_BALANCE_PLAN §6` 并归档。

## 1. 背景

- 无限段五计划(1~5)已于 2026-07-29 全部落地,数值曲线去硬顶(线性 mult,无 ×8 cap),但 **>15 min 深局校准被推迟**:
  - `ENDLESS_BALANCE_PLAN §7`:Calibration (per_boss_kill/per_ten_minutes/ramp feel) edits `progression` in `balance.json`, record to §6。
  - `DESIGN_BASELINE §8.2` Mid Term:tune `progression.*` for deep runs (>15 min);verify via `autoplay_test`(**no "HP-only inflation, zero pressure" steady state**)。
- 旧探针(2026-07-29,300s):0 异常;mult ≈ ×2.5 at 300s(2 kills ×2.0 + 10 tiers ×0.5);无平台期;15 min+ 数据缺失。

## 2. 校准杠杆与当前值

| 杠杆 | 当前值 | 含义 |
| --- | --- | --- |
| `progression.per_boss_kill` | 0.5 | 每击杀 1 Boss,mult +0.5 |
| `progression.per_ten_minutes` | 1.0 | 每 10 分钟,mult +1.0(经 `time_step_seconds` 量化,每档 +0.05) |
| `progression.time_step_seconds` | 30 | 时间轴量化步进 |
| `enemies.hp_ramp_factor` | 0.12 | 敌人 HP ×(1 + 0.12×(mult−1)) |
| `enemies.damage_ramp_factor` | 0.08 | 敌方伤害 ×(1 + 0.08×(mult−1)) |
| `spawner.ramp_time` / `interval_min` | 300 / 2.5 | 波次密度 5 min 到最快,间隔下限 2.5s |
| DDA | 5.0s / ×1.3 | 受击降档,只拉间隔不降收益 |

mult 公式(game_state.gd:837):`1 + per_boss_kill×boss_kills + floor(run_time/30)×(30/600)×per_ten_minutes`

玩家侧固定:DPS cap ×9.5(single)/×38(theor)、HP 600(extra_life 10 层)、lifesteal 10% max、回血被动。

## 3. 校准判据(可量化)

1. **无平台期**:mult 在 15 min 后仍随击杀/时间持续增长(公式保证,探针确认)。
2. **无 zero-pressure 稳态**(DESIGN_BASELINE §8.2):深局中玩家不得长期满血无压——用 SNAP 血量轨迹 + 死亡次数 + DDA 触发率判断。
3. **击杀效率可见下降**:敌人 HP ramp 追上玩家 DPS cap,深局每分钟击杀数较前期下降(敌人更肉),但不得出现「打不死」死局(60 min 内)。
4. **无崩盘悬崖**:难度增速不得让中段(5~15 min)出现连续死亡无法推进。
5. 数值只改 balance.json(设计基线 §8.4 第 2 条),不改代码。

## 4. 执行步骤

1. **基线探针**:`--autoplay-seconds=900 --seed=20260729`(游戏内 15 min,time_scale=2,真实约 8 min);SNAP 采样 mult/血量/击杀/敌人堆积;日志存 /tmp。
2. **数据分析**:按 3 min 窗口聚合 mult 曲线、每分钟击杀数、平均血量、死亡/DDA 次数;定位稳态或悬崖。
3. **调参**:按分析结果调整 `progression.*` 与/或 ramp 系数(单变量对比,先动 `per_boss_kill`/`per_ten_minutes`,再动 ramp feel)。
4. **复跑验证**:同 seed 复跑,对比曲线;命中判据 1~4 则定稿。
5. **回归**:balance/difficulty/boss_enrage/wave_pacing 四专项 + 全量断言场景 0 FAIL。
6. **文档回填**:ENDLESS_BALANCE_PLAN §6 记录新曲线与实测值、DESIGN_BASELINE §8.2 状态、ROADMAP、CHANGELOG、本文件归档 + EXECUTION_LOG 登记。

## 5. 验收标准

- 深局探针(≥900s 游戏内)无 `[ANOMALY]`;SUMMARY 击杀/Boss 击杀/死亡数据完整。
- 15 min 时点满足判据 2~4(血量压力存在、击杀效率下降、无崩盘悬崖);60 min 理论外推无死局。
- 全量断言场景 0 FAIL;gdformat/gdlint 通过。
- balance.json 变更经 `gen_balance_map.py` 核对(BALANCE_MAP 更新)。

## 6. 风险

- 探针是模拟输入,不代表真人操作强度;校准以「曲线形态正确」为目标,手感留给发布前人工验证(登记不变)。
- headless 无渲染成本,时间轴密度压力纯逻辑侧,足够评估数值曲线。
- 调参可能引入回归 → 每个变量独立提交式验证,失败即回退。
