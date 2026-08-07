# 文档重构计划（2026-08-07，doc refactor：去歪曲 + 减绕路）

> 背景：用户指示「本次工作中发现的文档问题重整——制作一份真实且可减少 Agent 跑弯路的文档重构，goal 全量推进」。
> 触发：S 系列（mobile touch 等）落地过程中触达的文档问题——断言场景数散布 10+ 处靠人记（M8 同根因复发）、AUDIT_VAULT 状态表与 DESIGN_BASELINE 不同步（A5/A8）、headless 输入注入坐标陷阱无任何提示（调试多轮）、gdtoolkit 本地安装无 PEP 668 指引、translations.csv 重导机制未集中说明。
> 方法：4 路并行只读盘点（计数扫描 / AUDIT_VAULT 状态核对 / 入口文档引用真实性 / 测试环境知识缺口）+ 人工复核。本文件为执行清单；完成后回填 AUDIT_VAULT（T 系列）并归档。
>
> **执行状态（2026-08-07 归档）**：D01-D15 全部落地（T01-T10 回填 AUDIT_VAULT；含盘点遗漏的 boss:334/569 R06、user_db.gd 路径、ELITE_TURRET_EVENT:173 口径）；残留扫描 0 命中；五层门禁全绿（47 断言场景 0 FAIL）；零逻辑改动。

## 1. 盘点结论（问题清单，来源×现状×处置）

| 编号 | 位置 | 问题 | 处置 |
| --- | --- | --- | --- |
| D01 | `.github/workflows/ci.yml:107` | 步骤名 "Run assertion scenes (45)" 过期（实际 47），与 :3 注释 47 自相矛盾 | 去硬编码：步骤名改 "Run assertion scenes"（无数字，CI 以 `test/*_test.tscn` 实跑为权威） |
| D02 | `CONTRIBUTING.md:48`、`docs/C_SHARP_ASSESSMENT.md:15,65,88,129,167`、`docs/ROADMAP.md:8`、`docs/DESIGN_BASELINE.md:264` | 当前流程描述的断言场景数过期（45） | 统一为「47（权威：docs/TESTING.md / CI run）」 |
| D03 | `README.md:12-13` / `README.en.md:12-13,137` | Release 徽章 v3.27 过期（config/version=3.28）+ Tests 徽章 "45 scenes" 过期（47）；en :137 "45-scene" 与 :127 自相矛盾 | 徽章 v3.28 / 47 scenes；en 统一 |
| D04 | `docs/TESTING.md` 顶部 | 无动态权威计数指引（静态硬编码 47/56 + 历史注记）——漂移复发根因 | 顶部加「权威计数」段：`ls test/*_test.tscn | wc -l` − 1（autoplay 探针）= 断言场景数；`ls test/*.tscn | wc -l` = 总场景数；注明 CI run is authority、数字仅信息性 |
| D05 | `docs/TESTING.md:17-69` | 子系统场景清单漏 `virtual_controls_test` / `encounter_flow_contract_test` | 补两行 + 注明「清单可能滞后，以 ls 为准」 |
| D06 | `AGENTS.md:22` | CI/CD bullet 硬编码 47 无来源指引 | 补「（权威：docs/TESTING.md，改 test/ 后以 ls 为准）」 |
| D07 | `docs/AUDIT_VAULT.md:191,194` | 状态表 A5/A8 仍 ⚠️（DESIGN_BASELINE §7.1 已 ✅：A5 closed 2026-08-07 / A8 split 2026-08-03）——文档间不同步现成反例 | 状态表回填 ✅ + 引用 S04/:1061 |
| D08 | `docs/AUDIT_VAULT.md:142,179,122` | A5/A8/A4 详情遗留句未划（"未收敛" / "未完成：视觉驻留" / "残留 7 处 match"） | 补划线/注记（已收敛/已拆分/随 A3 收敛） |
| D09 | `docs/AUDIT_VAULT.md:1319,1328,404,1022-1031` | R 复核 #1（L17 已修未注）、#10（孤儿 ctex 已消解）、C34（可收口）、L-P3 类别清单（已收口） | 逐条收口注记 |
| D10 | `scripts/enemy_pool.gd:47`、`scripts/boss.gd:118`、`scripts/back_navigator.gd:19` | 代码注释审计编号误标（R07 → R04/R12） | 修正编号 |
| D11 | `docs/DESIGN_BASELINE.md:115`、`docs/ARCHITECTURE.md:59` | 「Six non-autoload services」/ 委托清单漏第 7 个（UserDB）——与 AGENTS.md 7 服务口径冲突 | 改 Seven + 补 UserDB 条目 |
| D12 | `docs/DESIGN_BASELINE.md:109` | 节点树注释 "registered to spawner" 过期（实际 GameEventManager） | 改口径（与 :63/ARCHITECTURE 一致） |
| D13 | `autoload/game_state.gd:103` | 注释文档路径缺 `archive/` 前缀 | 补前缀 |
| D14 | `docs/TESTING.md` / `.agents/gdscript-lifecycle.md` | 测试环境知识缺口：①headless `parse_input_event` 注入坐标变换陷阱（实测 30×）；②gdtoolkit PEP 668 说明；③translations.csv 改后重导 `.translation`（gitignored）机制；④模拟输入走公开测试口规范 | TESTING.md 新增「headless 测试环境注意事项」小节收纳①-④；gdscript-lifecycle.md 补测试口约定 |
| D15 | `.agents/doc-sync.md` | 无「断言场景数单一事实源」规则（漂移复发根因，M8 同族） | 加规则：计数以 docs/TESTING.md 动态命令为权威，全仓禁止散布硬编码断言数；改 test/ 后同步 |

## 2. 处置原则

- **去硬编码 vs 历史快照**：当前流程描述（门禁/CI/验收/特性）统一引用权威源（docs/TESTING.md / CI run）；历史时点快照（CHANGELOG 各版条目、C_SHARP_ASSESSMENT:49、ENDLESS_BALANCE_PLAN 验收记录）按项目惯例保留不动。
- **AUDIT_VAULT 专有文档不可删除**：只做状态回填/划线/注记追加。
- **行为零变化**：本次为文档重构 + 3 处代码注释编号修正，不改任何逻辑；代码注释改动经 gdformat/gdlint/import 验证。
- 不动工作区 v3.28 暂缓发布伴随文件（`release.sh`/`run.*`/`.agents/shell-scripts.md` 未提交修改）；README 徽章 v3.28 属本次文档真实性修复（config/version 已是 3.28）。

## 3. 执行步骤

1. TESTING.md：顶部权威计数段 + 场景清单补 2 项 + 「headless 测试环境注意事项」小节（D04/D05/D14）
2. 全仓计数统一（D01-D03、D06）：ci.yml 步骤名、CONTRIBUTING、C_SHARP_ASSESSMENT、ROADMAP、DESIGN_BASELINE、README×2 徽章/清单
3. AUDIT_VAULT 状态回填与收口（D07-D09）
4. 代码注释修正（D10、D13）
5. DESIGN_BASELINE / ARCHITECTURE 服务口径与节点树注释（D11、D12）
6. doc-sync.md 规则固化（D15）+ gdscript-lifecycle.md 测试口约定
7. 验证：gdformat/gdlint（注释改动）/import/quit-after 300/全量断言场景；文档残留扫描（45 计数、过期引用）

## 4. 验证

- 全仓（排除 archive/AUDIT_VAULT 历史快照）「45 assertion / 45 断言」残留 0；「Six non-autoload」残留 0；`docs/2026-08-04-local-accounts-plan.md` 缺 archive 前缀残留 0。
- 五层门禁：gdformat --check / gdlint / import 0 error / quit-after 300 0 error / 全量 47 断言场景 0 FAIL。
- 文档登记：AUDIT_VAULT T 系列 + CHANGELOG + doc-sync 规则。
