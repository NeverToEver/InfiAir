# InfiAir 路线图（未来方向与初步计划）

> 2026-07-24 立项。本文是项目方向的单一事实源；阶段调整时更新本文并在 AGENTS.md「文档同步要求」登记。

## 现状快照（2026-08-03 更新）

- **移植对齐收官**（2026-07-24 快照）：Python/Pygame 原作的全部核心机制已重制并对齐（逐项对照见 `docs/archive/PORTING_PARITY.md` 差距清单，仅剩「本地排行榜页」一个可选项）；**收官后进入独立演进，原作仅作历史溯源/数值参考，不再作逐项对齐对象**。
- **质量基线**：37 个无头断言测试场景全绿（0 FAIL，2026-08-03 实测）；长时 autoplay 探针 + 性能基准可用。
- **代码审计档案（2026-07-31 建立）**：`docs/AUDIT_VAULT.md` 为专有审计档案，十轮系统审计（A–L 系列）全部处置、无 P0 遗留；A 系列仅 A5（残余依赖收敛）与 A8（Player 视觉抽离）未收敛。修复状态与起效记录一律以档案为准，本文件不复述细节。
- **协作就绪**：隐私隔离审计通过（无密钥/个人信息泄露、git 历史已清洗）、UI 字体替换为 OFL 开源的 NotoSansSC、文档基线（README / AGENTS / PORTING_PARITY / EXIT_FLOW）已与代码逐条核对。
- **战斗公平感四机制已落地**（2026-08-03，`docs/archive/2026-08-03-combat-fairness-plan.md`；机制与数值定稿见 `docs/DESIGN_BASELINE.md` §1.13）：受击宽限帧（ghost hit 消灭）、擦弹得分（风险-回报技巧轴）、Boss 阶段转场清弹 + 玩家短暂无敌 + 分段血条（惊喜阶段公平感）、F 键弧光弹反盾（主动防御反击，3.8s 决策周期）。全量验证通过（37 断言场景 0 FAIL + 180s autoplay 探针无新异常）；实机手感验证（15 分钟+ 长局）登记为发布前人工项。后续方向（B 档）见计划书 §8：每攻击独特 tell、DDA 弹密度动态降档、死亡回放。

## 方向转变

| 维度 | 过去（~3.13） | 未来 |
| --- | --- | --- |
| 目标 | 逐项对齐原作，消除移植差距 | 以重制版自身路线独立演进；原作仅作数值/设计参考，不再逐行对齐 |
| 开发模式 | 单人开发 | 协作开发（仓库已具备协作者条件） |
| 发布 | 明确暂缓打包 | 2026-07-30 重启打包：导出预设入库 + `release.sh` 双平台导出 + Linux/Windows 安装卸载脚本；CI/CD（五层门禁 + 手动发布工作流）已于 2026-08-02 落地 |
| 内容 | 机制补全 | 维持现有内容；体验深化与新内容 2026-07-30 已随 Phase 2 一并砍掉，重启需重新立项 |

## 阶段计划

### Phase 0 — 技术债收尾（近期，无新玩法）

**已完成（2026-07-31 ~ 08-03，逐项记录见 `docs/AUDIT_VAULT.md` A 系列与 `docs/archive/EXECUTION_LOG.md`）**：敌机生成路径统一池化（`920e5e9`）、A2 GameState 四服务拆分、A3/A4 注册表与声明式效果表收敛（`310e0b9`）、公平感四机制（`b2bc8a5`）、CI/CD 与五层门禁。

**剩余待办**：

- **A8**：Player 视觉职责抽 `PlayerVisuals` 组件（`docs/DESIGN_BASELINE.md` §7.1，唯一未收敛架构债）。
- **L 系列行为/流程待办**（2026-08-03 第十轮审查登记，详见 `docs/archive/2026-08-03-code-review.md`）：**L13** 母舰驻留/对接期精英炮塔与编队事件互斥（进保护舱挂机收益，设计决策）；**L14** Boss 段切换 y 垂直跳变平滑过渡（三型 P1 band → P2 bob，行为修改）；**L18** `release.yml` 版本号同步落地（sed 只改 CI 工作区不 commit，tag 指向提交的 `config/version` 永远滞后）。
- **test/ 门禁盲区**（L 系列最高优先建议）：test/ 未纳入 `gdformat --check` 与 import 门禁，两次 P1（截图工具链式调用编译错误、autoplay 母舰状态表漂移）均因该盲区长期潜伏；L15 profile 快照还原、L16 smoke 弱断言随批处置。
- 审计 P2 待办清理（`docs/archive/2026-07-22-audit-fix-plan.md`）：死代码删除（`main.gd` 未用引用、`hud.gd` 恒假分支、零 connect 信号等）、母舰 `_start_release()` 幂等守卫、`profile_corrupt` 损坏档案提示消费。**状态（2026-08-02）**：多项已被后续审计轮次覆盖处置（C21 对象池 `_exit_tree` 清注册、D 系列若干项），未处置项仍见 `docs/DESIGN_BASELINE.md` §7.3。
- 验收：全部既有测试 0 FAIL；改动条目在审计文档标注完成。

### Phase 3 — 暂缓/已砍项的重启条件（均需用户明确决策）

- **本地账号系统**：完整规格存档于提交 `7aacd3f`（登录系统立项，UserDB/PBKDF2/每用户存档隔离，写入移植计划附录 B；`docs/archive/PORTING_PARITY.md` 附录 B 亦有规格），重启时整体复用。
- **附录 B 独立主场景版进入页**：轻量方案已够用；仅在开始面板承载不下新入口时重启，规格在 `docs/archive/PORTING_PARITY.md` 附录 B。
- **打包发布**：2026-07-30 重启、2026-07-31 跑通——`export_presets.cfg`（Linux/X11 + Windows Desktop，嵌入 pck 单文件）入库，`release.sh` 一键导出打包（产物 `builds/release/`，本机 gitignore），`packaging/` 提供双平台安装/卸载脚本（Linux 用户态 + .desktop 入口 / Windows per-user + 开始菜单快捷方式）。安装脚本与实机运行待对应平台验证。
- **联机排行榜**：已决策不做（2026-07-20），如需翻盘须显式推翻该决策。
- **协作与发布工程化**（原 Phase 1）：导出预设入库与导出命令已随打包发布重启落地（2026-07-30）；**CI / 贡献指南 / 版本发布（CD）已于 2026-08-02 全量落地**——CI：`.github/workflows/ci.yml`（无头导入 + 主场景冒烟 + 37 断言场景全量回归，push/PR 触发）；贡献指南：仓库根 `CONTRIBUTING.md`（另有 `SECURITY.md`、issue/PR 模板、`CHANGELOG.md`）；手动触发发布工作流 `.github/workflows/release.yml`（双平台导出打包 → tag `v<版本>` → 创建 GitHub Release，输入版本自动同步 `project.godot` `config/version`）。版本号沿用 MAJOR.MINOR 递增惯例（当前 3.26）。
- **内容演进**（原 Phase 2）：2026-07-30 决策砍掉，含本地排行榜页、新内容候选（新 Buff 品类、新敌机/精英类型、第 4 种 Boss、移动端触屏操控）、母舰玩法扩展、无限段 k 值实机标定（方案已全量落地，见 `docs/ENDLESS_BALANCE_PLAN.md`）；重启任一项须重新立项并先在本文登记。

## 维护约定

- 阶段完成/方向调整 → 更新本文；移植时期的差距口径已随 `docs/archive/PORTING_PARITY.md` 归档（2026-07-30 冻结），不再回写。
- 新增暂缓/重启决策 → 记入 Phase 3 并注明决策日期，不散落在其他文档。
