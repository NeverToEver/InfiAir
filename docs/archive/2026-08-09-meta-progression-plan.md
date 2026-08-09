# 局外成长（Meta Progression）执行记录 — 2026-08-09

> 状态：**landed**（M1–M5 全绿）· 计划日 2026-08-09 · 设计修订登记 `docs/DESIGN_BASELINE.md` §1.3/§1.4/§8.2 + `docs/ROADMAP.md` 决策条目。

## 1. 背景

Steam 同类调研（Vampire Survivors / Brotato / 20 Minutes Till Dawn / Geometry Wars 3，Playwright 实测商店页）：
同类对 InfiAir 最大的差距是**跨局持久成长**（"死亡也有收获"的钩子）。本期实现最成熟的
VS 式**数值科技树**。

## 2. 设计

- **货币**：科技点（TechPoints），独立于局内 RP（RP 维持基地经济不动）。
- **结算**：死亡结算唯一入口（`GameState.SettleRun()` → `SettleTechPoints()`）；放弃出击（ExitConfirm 删档）/返航不结算——防刷点，每局至多一次。
  公式：`floor(score/score_divisor) + boss_kills×boss_kill_bonus + missions_claimed×mission_bonus`（`balance.json` `meta.points.*`）。
- **效果**：升级 = 新局开局预置 buff 层数（`ApplyMetaLoadout`，`Main.ApplyNewRun` 调用；tutorial/存档恢复路径不经过）——完全复用 Buffs 计算链，零新属性管道。
- **有界性**：每项限级（`meta.upgrades.*.max_level` 2–3，≤ `buffs.<id>.max_stacks` 一半）+ 总消费上限 → 玩家终将毕业、敌人无界增长 → D1 必死曲线保持。
- **范围**：仅登录用户（B7-8 游客不持久化）；手改 users.json `meta` 非法值判型回默认（U15/Q17 风格）。

## 3. 实现批次（门禁全绿）

| 批次 | 内容 | 验证 |
|---|---|---|
| M1 | `csharp/core/Meta/MetaProgression.cs`（UpgradeDef + 费用曲线 + 结算公式 + 防御钳制）+ `balance.json` `meta` 节 + `tests-csharp/MetaProgressionTests.cs`（11 项） | build 零警告 + xUnit 101 绿 + gen_balance_map 收录 |
| M2 | `UserDb` `meta` 字段（创建默认 + Get/UpdateUserMeta + 判型）+ `tests-csharp/UserDbMetaTests.cs`（7 项） | xUnit 108 绿 |
| M3 | `GameState.Meta.cs`（会话档案/配置缓存/结算/消费/预置）+ `SettleRun` 接入 + `ApplyBalance` 缓存 + 会话加载/登出清空 + `Main.ApplyNewRun` 预置 + `TechPointsChanged` 信号 | build + import + 300 帧冒烟 + smoke_test 140 PASS + BALANCE_MAP 零 diff |
| M4 | 研究所 UI：`ResearchLab.cs` 组件 + Welcome 主菜单入口（登录用户可见）+ BaseConsole 第五面板 + `META_*` 双语键 | welcome_flow 33 PASS + base_system 79 PASS |
| M5 | `test/meta_test.tscn` + `MetaProgressionTest.cs`（24 断言：游客隔离/结算/消费/满级/预置/持久化/放弃不结算/判型）+ 文档同步 | meta_test 24 PASS + dotnet format 零 diff |

## 4. 测试矩阵

- xUnit：价格曲线 / 结算公式 / 上限防御 / UserDb meta 往返与判型（新增 18 项，总 108）。
- 断言场景：`meta_test`（结算→加点→预置→重登往返→游客隔离→放弃不结算→手改判型）。
- 回归：既有断言场景保持 0 FAIL。

## 5. 后续（可选二期，未排期）

新 buff / 新机体作为科技树解锁节点（解锁制挂靠）；科技点曲线初值经 autoplay 校准（>15min 深局手感）。
