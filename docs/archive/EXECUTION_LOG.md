# 执行留档索引（EXECUTION_LOG）

> **定位**：已完成工作的**压缩留档索引**（2026-08-02 建立，替代 `docs/` 顶层 10 份已完成的计划/审核文档）。每项仅保留必要信息：日期 / 主题 / 落地提交 / 摘要 / 关键决策与教训，并链接归档原文（`docs/archive/` 下同名文件，含完整细节，git 历史亦留存全文）。
>
> **维护约定**：新计划/审核工作完成后，将原文档全文移入本目录，并在下方按时间线登记压缩条目（日期、提交哈希、2-4 行摘要、关键决策/教训、原文链接），然后从 `docs/` 顶层删除原文档并更新引用。归档文档内部引用的 `docs/xxx` 路径为归档前快照，不保证在 archive/ 下可点击（与 `PORTING_PARITY.md` 同例）。
>
> **相关**：方向类决策见 `docs/ROADMAP.md`；设计/架构基线见 `docs/DESIGN_BASELINE.md`；审计处置见 `docs/AUDIT_VAULT.md`（专有档案，禁止删除或合并）；本索引与 `PORTING_PARITY.md`（移植对齐历史）共同构成 `docs/archive/` 归档区。

---

## 2026-07-22 · 审计 P0/P1 修复与 P2 技术债清理（第一批）

- **落地**：`4df9e02`（P0/P1 五项修复）；UI 样式重构 `7f0aa42`
- **摘要**：死代码 / 死循环 / 调用错误三类静态审计修复；欢迎页键盘绕过误删存档等五项高危修复；P2 建议项部分落地，其余转入后续审计轮次（C21 对象池清理、D 系列等已覆盖大部分）。
- **原文**：`docs/archive/2026-07-22-audit-fix-plan.md`

## 2026-07-30 · 战斗可读性 / Boss 走位 / 辅助瞄准 / 弹速审计

- **落地**：`fbe33e2`（P0-1~P0-5、P1-1、P1-2）；`315af0d`（P1-3 辅助瞄准算法优化）
- **摘要**：Boss 战斗锚线 `FIGHT_Y` 改 view 顶缘偏移；敌机/精英 scale 与 radius 上调；敌弹视觉放大 ×2.4 提亮；辅助瞄准重设计（准星跟随 + `mark_ratio` 0.25 辅助框 + 框内追踪弹）；玩家弹速 1200→1800（DPS 不变）。
- **关键决策**：P0-3「不动 world_scale（1/3）」于 2026-07-31 被推翻——`world_scale` 上调至 0.4。
- **原文**：`docs/archive/2026-07-30-combat-ux-audit-plan.md`

## 2026-08-01 · C 系列 Godot 最佳实践审计（35 项全量处置）

- **落地**：`174b5a0`→`a900018`→`8f134dc`→`c526d79`→`d4f427c`→`0555b19`→`1b5f424`→`832a167`（修复），回填 `03fa3eb`
- **摘要**：教程协程泄漏（C01）、key_bindings 类型守卫（C02）、难度表子键校验（C03）、子弹位移改物理帧（C04）等 35 项；C34 ⚠️ 部分完成（含设计确认锚点）、C19 🟦 设计确认不改码、C33 🟦 核实无风险不修。
- **原文**：`docs/archive/2026-08-01-godot-best-practice-audit.md`

## 2026-08-01 → 08-02 · 欢迎页移除（开始面板改全遮光标题屏）

- **落地**：`2c16892`（25 项任务全部落地）
- **摘要**：WelcomeScreen 退役；开始面板重设计为全遮光独立标题屏（品牌区 + 切角菜单板 + 装饰雷达/星空）；教程入口守卫；ESC 层级由 BackNavigator + ExitConfirm 接管。
- **原文**：`docs/archive/2026-08-01-remove-welcome-screen-plan.md`

## 2026-08-02 · D 系列全量代码审查修复（30 项处置）

- **落地**：`a4318ea`（代码修复）；`f78363e`（文档同步 D21-D28）
- **摘要**：入场 Timer/预告线清理（D01）、invincible 回退统一（D02）、入场中断复位 abort_entry（D06）、测试 landed 判据（D07）、HUD max_health 缓存（D08）、两处 960 硬编码收敛（D10）、入场数值入配置（D14）等；P3 不修 8 项。
- **关键教训**：D03（Label mouse_filter）经实证证伪——审核判定必须以源码证据为准。
- **原文**：`docs/archive/2026-08-02-audit-fix-plan.md`

## 2026-08-02 · Boss P2 阶段走位升级（D05 落地）

- **落地**：`237b077`（配置键 boss.movement 11 键）→ `ce1ea1c`（TDD 红测试）→ `4a7d252`（功能实现）→ `1e6183c`（文档回填）
- **摘要**：一型/三型 P2 strafe 提速 + 纵向正弦往复（`_move_bob`）、三型 P1 锚线下 200–280px 区间呼吸（`_move_band`）、二型 P2 dash 节奏 0.4/0.5；`boss.movement` 段入 balance.json + 脚本回退同步。
- **原文**：`docs/archive/2026-08-02-boss-p2-movement-plan.md`

## 2026-08-02 · E 系列存量盲区修复（15 项登记处置）

- **落地**：`c6fae53`（批次 1）→ `12fec33`（批次 2）→ `be66025`（批次 3）→ `193f605`（补断言）→ `de513ef`（回填）
- **摘要**：母舰溅射对 Boss 失效（E01，Variant 鸭子调用修复）、教程删档守卫（E02）、难度表子键校验（E03）、提前离舰进度条清理（E05）等；E09/E13-E15 判定不修。
- **原文**：`docs/archive/2026-08-02-e-series-fix-plan.md`

## 2026-08-02 · 鼠标锁定窗口内（F 系列，F01/F02）

- **落地**：`ddea0ad`（mouse_lock 状态/持久化）→ `c3425e7`（MouseTrap 组件）→ `c3e4d5c`（设置页开关）→ `c48383f`（F02 暂停/非准星态放行）→ `00c3bfb`（warp 跳变核查）
- **摘要**：`mouse_lock` 设置项（默认开启）+ `scripts/mouse_trap.gd`（挂 Main，PROCESS_MODE_ALWAYS）把移出内容区的鼠标拉回边缘内侧，消除准星冻结/跳变；F02 修正 confine 生效范围——仅对局准星活跃且窗口聚焦时生效，暂停后可自由移出窗口。
- **原文**：`docs/archive/2026-08-02-mouse-lock-plan.md`

## 2026-08-02 · 性能优化计划全量落地

- **落地**：`920e5e9`（P0×4 / P1×7 / P2×8；P3 经评估全跳过）
- **摘要**：敌机生成路径统一池化（普通波次经 `EnemyPool.spawn()`，`USE_POOL` 作 A/B 对照）；`view_world_rect` 物理帧缓存（热路径 ~130 次/帧 → 1 次）；受击闪白手动衰减替代 Tween；残影池；`sin_fast` 全量清扫；starfield 绘制合批；HUD 文本节流。perf_bench A/B：0.131→0.120 ms/帧（约 -8~9%）。
- **原文**：`docs/archive/2026-08-02-performance-optimization-plan.md`

## 2026-08-02 · G 系列核心逻辑审核（32 项全量处置）

- **落地**：`cb8511b`（批次 1 P1×3）→ `b7b2cc8`（批次 2 P2×8）→ `ffef641`（批次 3+4）→ `fff1b25`（回填）
- **摘要**：spawner 预警取消复位（G01）、Boss 逃跑期免伤（G02）、教程入口守卫（G03）、改键冲突清理（G04）、注册表 O(1) 存在性索引（G010）等；G017/G019/G025/G031 判定不修。
- **原文**：`docs/archive/2026-08-02-core-logic-audit.md`

## 2026-08-02 · 健壮性专项审核（H 系列，无 P0）

- **落地**：`6a95f41`（批次 1 H01-H03）→ `34bbcc8`（批次 2 H04-H09）→ `efae4ac`（批次 3a H10-H16）→ 本归档提交（批次 3b H17-H20 + 档案回填）
- **摘要**：三路并行扫描发现 P1×3 + P2×6 + P3×20（组）——右摇杆 get_vector 恒零（H01，手柄核心缺陷）、改键擦手柄事件（H02）、milestone 挂死（H03）、BGM 空引用、homing 除零、autofire 死守卫、空池崩溃、配置无域校验批量（判型/钳制/回退）、生命周期守卫组（tween 互斥/软锁复位/悬挂引用）。整体结论：健壮性强，风险集中在手改 balance.json 的判型缺口。
- **原文**：`docs/archive/2026-08-02-robustness-audit.md`（档案回填见 `docs/AUDIT_VAULT.md` 第六轮）

## 2026-08-02 · 竞品差评调研与主动改进（P0-P2 全批落地）
- **落地**：`cc217c8`（调研报告）→ `9bc735d`（P0-2 玩家弹白芯描边）→ `40716c4`（P0-3 本地高分榜）→ `bb1d027`（P1-6 教程可重看）→ `275d719`（P2-10 致死弹高亮）→ `bcc5ace`（P0-1 手柄运行时装配）→ 本归档提交（审计登记）
- **摘要**：收集 5 个同类游戏（Nova Drift/Raptor/Sky Force/Jamestown/Starward Rogue）44 条 Steam 差评聚类 11 主题（可读性/操控/难度公平/重复感等）；落地 5 项代码改进（玩家弹白芯、本地排行榜、教程可重看、致死弹高亮、手柄支持+右摇杆虚拟准星）+ 设置页手柄分区（右摇杆灵敏度/摇杆死区可调，profile 持久化）；3 项审计结论（难度公平机制完备、音频循环合规、波次节奏参数化）+ 存档说明 + 俄语暂缓。
- **发布前验证项**：手柄实机走查、15 分钟+ 长局公平感、BGM 循环听感（登记于原文 §6）。
- **原文**：`docs/archive/2026-08-02-competitor-review-audit.md`

## 2026-08-03 · 战斗公平感四机制落地（公平感专项）

- **落地**：`b2bc8a5`（四机制全量执行）→ `310e0b9`（A3/A4 架构债收尾）→ 后续 K/L 系列修复提交（口径随各轮登记）
- **摘要**：受击宽限帧（`player.grace_period`，消灭 ghost hit）、擦弹得分（`player.graze_radius`/`graze_score`，风险-回报技巧轴）、Boss 阶段转场清弹 + 玩家短暂无敌 + 分段血条（`boss.phases.clear_on_shift`/`transition_invincible`/`hud.boss_bar_segments`）、F 键弧光弹反盾（`player.parry.*`，前摇 0.15/有效 0.5/后摇 0.15 + 3.0s 硬冷却，完整周期 3.8s）。新增 4 断言场景（grace_period 14 / graze 12 / boss_phase_transition 31 / parry 36）+ 180s autoplay 探针；落地时全量 35 断言场景 0 FAIL。
- **关键决策**：擦弹环与弹反盾半径属游戏性范围族不乘 `world_scale`；弹反盾判定从扇形 shape 改圆盘触发 + 回调内扇形过滤（实测扇形 shape 内外判定不可靠）；擦弹音效暂用 `SFX_BUFF_PICK`、弹反音效暂用 `SFX_DASH` 占位（登记为后续音频项）。
- **发布前验证项**：实机 15 分钟+ 长局手感（擦弹反馈/阶段转场喘息/弹反手感）登记为人工验证项。
- **原文**：`docs/archive/2026-08-03-combat-fairness-plan.md`（定稿语义已浓缩进 `docs/DESIGN_BASELINE.md` §1.13）

## 2026-08-03 · L 系列第十轮全仓库软件工程维度审查

- **落地**：`293d174`（P1×3/P2×9 修复）→ `4fad614`（审查报告与档案登记）
- **摘要**：8 路并行只读审查（对局编排/玩家/敌机池/Boss/母舰事件/UI 导航/测试/配置卫生）对照既有登记去重；结论 P1×3（全部修复并验证）+ P2×15（修复 10、登记待办 5）+ P3×40+（修复 6、登记 30+），无 P0。核心发现：测试维护盲区（test/ 截图工具链式调用编译错误、autoplay 母舰状态表漂移，CI 三道门禁全部无感知）、池化复用 buff 信号重连回归（slow_field 静默失效）、判型只到容器层不到元素层。
- **关键教训**：防御惯例在逐文件扩散时偶有漏网（判型只到容器层、修复只覆盖被登记路径）；test/ 目录处于引擎解析正确性盲区，建议纳入 gdformat/import 门禁。
- **原文**：`docs/archive/2026-08-03-code-review.md`（登记见 `docs/AUDIT_VAULT.md` L 系列）

---

## 2026-08-04 · 被推迟计划重启执行（账户系统 / 母舰扩展 / 内容演化）

- **落地**：账户系统 `5668c38`（UserDB 数据层）→`75e23bf`（会话/存档隔离）→`0aa8973`（welcome 主场景）→`f946f48`（测试适配）→`e301997`（i18n+文档）；母舰 `c0be71e`；内容演化 `19776b2`（3 buff）→`9ca35bf`（分裂者/重装精英）→`d83b138`（第 4 Boss 月蚀）→`88dcdd7`（文档）
- **摘要**：ROADMAP Phase 3 重启（移动触控除外）：①本地账户系统——UserDB（PBKDF2-HMAC-SHA256 50k 迭代，实测 100k=330ms 超 300ms 判定线降档）、每用户存档/设置隔离、profile.json 迁移、本地排行榜、welcome 主场景（StartPanel 退役）；②母舰火力随里程碑升级（≥5 里程碑伤害 ×1.5/射速 +25%）；③内容演化——crit_shot/shield/bullet_speed 三 buff、分裂者敌机、重装炮台精英、第 4 Boss「月蚀」（ring_burst 环弹/双环狂暴）
- **关键决策**：UserDB 做成非 autoload 服务（A2 模式，保持单 autoload 约定）；本地排行榜与入口页被账户系统吸收（不重复立项）；PBKDF2 迭代数存用户记录（测试可降档）；双模式档案（未登录走旧 profile.json 兼容路径，测试过渡期不红）
- **教训**：GDScript 字符串乘法不支持（`"a"*17` → `.repeat()`）；`PackedByteArray.hex/from_hex` 与 `Crypto.hash` 非跨版本 API（自实现）；批量文本替换易误删 emit/缩进（add_buff 的 buffs_changed.emit 被插入错位致 buff_panel/buff_visuals 大面积失败）；balance.json 尾随逗号与闭合行匹配（用 `indent='\t'` 重建保持格式）；狂暴子弹时间拉伸测试序列 4 倍（采样窗口须按 time_scale 折算）
- **原文**：`docs/archive/2026-08-04-{local-accounts,mothership-expansion,content-evolution}-plan.md`

## 2026-08-04 · 无限段深局校准（ENDLESS_BALANCE_PLAN §7 deferred 项）

- **落地**：本次提交（balance.json 4 值 + difficulty_test 断言同步 + 文档回填）
- **摘要**：>15 min 深局校准落地——基线探针确认 zero-pressure 稳态（27 min 游戏内 0 死亡、血量长期满、击杀率不降、9 出 1 杀 Boss）；三轮 900s 探针迭代定稿：`per_boss_kill` 0.5→0.6、`per_ten_minutes` 1.0→1.5、`hp_ramp_factor` 0.12→0.25、`damage_ramp_factor` 0.08→0.20。定稿形态：diff 1.38→6.33 @27min 无平台期、HP min 40–69 持续压力、DDA 15–29% 窗口、0 死亡无崩盘、全程 0 ANOMALY
- **关键决策**：只动 balance.json 数值不动代码（设计基线 §8.4）；玩家侧成长不动，纯敌方/曲线杠杆；per_boss_kill 保持 ≤0.6 防 Boss 逃逸阀恶性循环；回归以探针曲线形态为准，手感留人工发布前验证
- **教训**：探针 `--autoplay-seconds` 是**真实时间**预算（time_scale=2 → 游戏内约 2 倍），900s ≈ 27 min 游戏内；窗口聚合按 t_game 跨局混叠失真，深局结论须以单局连续段/事件时点为准；difficulty_test 断言与 progression 数值强耦合，改数必须同步（0.6→2 杀 ×2.2；1.5→65s 两档 +0.15）
- **原文**：`docs/archive/2026-08-04-endless-calibration-plan.md`（记录回填 `ENDLESS_BALANCE_PLAN §6.1`）

*本索引由 2026-08-02 文档口径统一期间建立（详见 `docs/AUDIT_VAULT.md`「文档口径统一处置记录」）。*

## 2026-08-05 · 全仓库深度 review（Q 系列，只审计未修复）

- **落地**：未落地（本批只读审计 + 登记，修复待指示）；审查文档与金库登记
- **摘要**：7 路并行只读审计（对局编排/战斗系统/服务层/事件系统/UI 导航文本/测试 CI 工具链/平衡数值内容），三角验证（设计文档 × 代码 × git 历史）；无 P0，P1×1（ring_burst 弹数按增量语义消费绝对值 → 实际 22/24/26 发 vs 设计 10/12/14）+ P2×9（hp_mults/_load_patterns 4 型守卫遗漏、regen 缓存重登录不刷新、TaskPool 99.3% 不足额、死亡统计缺失、fog 总开关失效、BALANCE_MAP 过期、welcome 设置页 Esc 断链、遭遇计时器跨局继承）+ P3×20 + P4×20+
- **关键决策**：三类结构性缺口——①2026-08-04 内容演化（4 型扩容）伴生遗漏（Q01-Q03/Q28 同根因）；②2026-08-05 统一事件/实体管理器迁移语义残留（Q07/Q10/Q12/Q13/Q19）；③账户批次新测试约定回退（Q23-Q25 破坏性/直调私有）；「设置黑屏」推断经官方文档证伪（CanvasLayer.visible 不传播到子 CanvasLayer），Esc 断链成立
- **原文**：`docs/archive/2026-08-05-deep-review-report.md`（登记：`docs/AUDIT_VAULT.md` Q 系列）
