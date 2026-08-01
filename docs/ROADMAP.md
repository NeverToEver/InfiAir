# InfiAir 路线图（未来方向与初步计划）

> 2026-07-24 立项。本文是项目方向的单一事实源；阶段调整时更新本文并在 AGENTS.md「文档同步要求」登记。

## 现状快照（2026-07-24）

- **移植对齐收官**：Python/Pygame 原作的全部核心机制已重制并对齐（逐项对照见 `docs/archive/PORTING_PARITY.md` 差距清单，仅剩「本地排行榜页」一个可选项）。
- **质量基线**：29 个无头断言测试场景全绿；长时 autoplay 探针 + 性能基准可用。
- **代码审计档案（2026-07-31 建立）**：`docs/AUDIT_VAULT.md` 为专有审计档案，登记 SOLID 审核发现 A1–A8。**修复状态（2026-08-01 按 git 历史与代码实况订正，见档案 A 系列回填）**：A1 封装穿透 ✅、A2 GameState 四服务拆分 ✅、A3 Boss 四类拆分 ⚠️ 拆分落地但 O 原则未达成（match 仅搬迁）、A4 开闭违反 ⚠️ 部分完成（A4a 敌机策略/A4b 事件触发基类已落地，Boss 分支与 Player buff 未治理）、A5 依赖倒置 ❌、A6 语义化特判 ✅、A7 测试白盒 855 处全清 ✅、A8 Player 组件拆分 ⚠️ 部分完成（PlayerDamage/PlayerDash 已抽，视觉未抽）。2026-08-01 并行复核另登记 B1–B16（见档案）。
- **协作就绪**：隐私隔离审计通过（无密钥/个人信息泄露、git 历史已清洗）、UI 字体替换为 OFL 开源的 NotoSansSC、文档基线（README / AGENTS / PORTING_PARITY / EXIT_FLOW）已与代码逐条核对。

## 方向转变

| 维度 | 过去（~3.13） | 未来 |
| --- | --- | --- |
| 目标 | 逐项对齐原作，消除移植差距 | 以重制版自身路线独立演进；原作仅作数值/设计参考，不再逐行对齐 |
| 开发模式 | 单人开发 | 协作开发（仓库已具备协作者条件） |
| 发布 | 明确暂缓打包 | 2026-07-30 重启打包：导出预设入库 + `release.sh` 双平台导出 + Linux/Windows 安装卸载脚本；CI 与语义化版本流程仍暂缓 |
| 内容 | 机制补全 | 维持现有内容；体验深化与新内容 2026-07-30 已随 Phase 2 一并砍掉，重启需重新立项 |

## 阶段计划

### Phase 0 — 技术债收尾（近期，无新玩法）

- 审计 P2 待办清理（`docs/2026-07-22-audit-fix-plan.md`）：死代码删除（`main.gd` 未用引用、`hud.gd` 恒假分支、零 connect 信号等）、母舰 `_start_release()` 幂等守卫、`profile_corrupt` 损坏档案提示消费。
- 敌机生成路径统一：普通波次目前直接实例化、Boss-3 小怪走 `enemy_pool`，两条路径并存；评估统一到对象池（含 A/B 性能对照，`USE_POOL` 开关已留）。
- **A2 GameState 拆分**（2026-07-31 **已全部完成**，`docs/AUDIT_VAULT.md` A2）：四阶段委托式剥离全部落地——①数值配置读操作 `BalanceService` → ②持久化 `SaveManager` → ③音效池 `SfxPlayer` → ④实体注册表 `EntityRegistry`。GameState 公开 API 保留并转发（注册表用属性 getter/setter），调用方与测试零破坏；29 断言场景全绿、数值地图 0 失配、`game_state.gd` 直接文件 IO 清零。
- **A3 Boss 单类拆分**（2026-07-31 **拆分落地**，`docs/AUDIT_VAULT.md` A3）：1488 行单类拆为门面 Boss + 4 职责类（`BossFire` 弹幕发射 / `BossAttacks` 攻击状态机 / `BossMovement` 移动策略 / `EnrageSequence` 狂暴状态机）；`boss.gd` 瘦身至 802 行，无跨类私有访问复发；29 断言场景全绿。**订正（2026-08-01）**：集中 match 仅是搬迁进 `BossAttacks.execute()`，非查表/工厂取代；机型分支在 BossMovement/EnrageSequence/Boss 残留 7 处，O 原则未达成（见 A3 复核订正）。
- 验收：全部既有测试 0 FAIL；改动条目在审计文档标注完成。

### Phase 3 — 暂缓/已砍项的重启条件（均需用户明确决策）

- **本地账号系统**：完整规格存档于提交 `dcef9b6`（UserDB/PBKDF2/每用户存档隔离），重启时整体复用。
- **附录 B 独立主场景版进入页**：轻量方案已够用；仅在开始面板承载不下新入口时重启，规格在 `docs/archive/PORTING_PARITY.md` 附录 B。
- **打包发布**：2026-07-30 重启、2026-07-31 跑通——`export_presets.cfg`（Linux/X11 + Windows Desktop，嵌入 pck 单文件）入库，`release.sh` 一键导出打包（产物 `builds/release/`，本机 gitignore），`packaging/` 提供双平台安装/卸载脚本（Linux 用户态 + .desktop 入口 / Windows per-user + 开始菜单快捷方式）。安装脚本与实机运行待对应平台验证。
- **联机排行榜**：已决策不做（2026-07-20），如需翻盘须显式推翻该决策。
- **协作与发布工程化**（原 Phase 1）：导出预设入库与导出命令已随打包发布重启落地（2026-07-30）；`CONTRIBUTING.md` / GitHub Actions CI / 语义化版本与 tag 发布说明仍暂缓，重启需用户明确决策。
- **内容演进**（原 Phase 2）：2026-07-30 决策砍掉，含本地排行榜页、新内容候选（新 Buff 品类、新敌机/精英类型、第 4 种 Boss、移动端触屏操控）、母舰玩法扩展、无限段 k 值实机标定（方案已全量落地，见 `docs/ENDLESS_BALANCE_PLAN.md`）；重启任一项须重新立项并先在本文登记。

## 维护约定

- 阶段完成/方向调整 → 更新本文；移植时期的差距口径已随 `docs/archive/PORTING_PARITY.md` 归档（2026-07-30 冻结），不再回写。
- 新增暂缓/重启决策 → 记入 Phase 3 并注明决策日期，不散落在其他文档。
