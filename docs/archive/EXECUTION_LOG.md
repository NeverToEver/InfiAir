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

## 2026-08-05 · Q 系列全量修复（deep review 批次落地）

- **落地**：本次提交（Q01-Q30 全量 + P4 合理项；五层门禁全绿后提交，仅 commit 不 push）
- **摘要**：按用户指示「goal 模式，最新报告，全量修复，仅提交」执行 2026-08-05 deep review 报告全部登记项。P1×1 + P2×9 + P3×20 全部修复落地（Q22 复核为审计误报——`size_flags_vertical` 自 2026-07-30 已具备，无改动）；P4 注释失实/文档计数/硬编码坐标/性能观察（orbital 单位圆缓存）/边界防御（progress 钳 0、score 上限、全零权重、ConfusionEvent 空转降级、reload_balance 联动）/工具链（gen_balance_map 报错、release.yml 重复版本与主分支版本同步、测试配置还原）等合理项修复；strafe_range 1920 复核为设计宽度常量保留。
- **关键决策**：需设计拍板项按报告推荐方向执行——Q01 消费侧改绝对值（与 §5.6 一致，easy 22→10/medium 24→12/hard 26→14 发）、Q27 直接绝对赋值（与 `_move_bob` 同模式，MOVE4_SPEED 键随修移除）、Q15 approach_speed 下限钳制、Q29/Q30 全量入库（`enemies.move_strategies` + `sniper3.burst_interval`，缺键回退脚本默认=现值行为逐字节等价）。
- **修复过程中发现的测试/实现缺陷**：Q24 走真实输入管线后暴露「输入框内 Enter 被 LineEdit 消费 → 键盘登录断链」（补 text_submitted 连接，B7-13 承诺真正达成）；welcome_flow_test 场景 8/9 重建实例未释放导致残留 SettingsUI 抢占 group（Q09 修复验证中暴露）；GDScript lambda 捕获 int 为值拷贝（Q13 计数改数组承载）；Q04 断言构造需直写档避免 set_difficulty 覆盖 profile。
- **教训**：审计报告行号与修复后代码漂移（Q30 编辑误缩进 sniper3 连发块致 3 连发只出 1 发，boss_phase 场景 2 捕获）；P4「硬编码 1920」类项需对照断言语义再改（strafe_range 固定边距是设计，view_zoom_test 锁死）；测试进程共享 user:// 目录（Q23 快照还原后仍需独立进程隔离认知）。
- **原文**：`docs/archive/2026-08-05-deep-review-report.md`（修复登记：`docs/AUDIT_VAULT.md` Q 系列「修复登记」段）


---

## 2026-08-09 · X 系列 C# 逻辑架构审计 + 修复（第四轮）

- **落地**：未提交（改动待提交：18 修改文件 + 1 新归档文档 + AUDIT_VAULT X 系列登记 + BALANCE_MAP 生成物）
- **摘要**：按 C# 架构专家角色流程四阶段执行（Phase 0 环境侦察 → Phase 1 五路并行逻辑梳理 → Phase 2 优化计划 → Phase 3 实施 → Phase 4 验证）。新 P0×1（TaskPool 重复 id 定义下无限 Refill 挂死）、P1×3（母舰减速带 Boss 回归修复——W6 只更注释未补行为 / Welcome.ShowMsg 跨场景回调 / 敌弹注销 O(n)→O(1) swap-remove）、P2×7（Coroutine 判活三处 / Boss 狂暴时序键钳制 / EventFor 死引用防御 / EnemyHpRamp 缓存化 / Welcome 模态 typed 化 / 双轨 API 36 方法删除 / PlayerVisuals 预分配）；测试 75→80；30 受影响断言场景全绿。
- **关键决策**：`EnemyHpRamp` 加显式难度重载（首版误用全局 DifficultyMultiplier，被 enemy_combat_test H2 捕获——公共 API 参数语义不能以生产路径等价推断，测试/分裂子机显式传参路径必须保持）；VariantBridge xUnit 单测因 tests-csharp 零 Godot 依赖纪律 defer（由 interop 场景间接覆盖）；BALANCE_MAP 重跑随改动提交；服务数文档漂移修正（7→8 补 ProgressionInterop）。
- **教训**：Edit 大块删除对空行数量敏感（Main.cs 桥区两次 old_string 失配后改用 python3 行号删除 + 断言校验）；「语义等价」声明必须覆盖全部调用方；B7 类「优化替换」需先跑受影响断言场景再宣布完成。
- **原文**：`docs/archive/2026-08-09-csharp-architecture-audit-plan.md`（登记：`docs/AUDIT_VAULT.md` X 系列）


---

## 2026-08-09 · Y 系列 C# 核心架构重构（第五轮）

- **落地**：未提交（改动待提交：Y1-Y5 五阶段 + 工具修复 + 文档）
- **摘要**：按用户指示对核心架构执行同 X 系列流程（侦察→计划→实施→验证）。格式化器 11 份收敛为 core 单一实现（**修复 %.Nf j 偏移迁移缺陷**——HUD 难度标签原渲染为字面「x%.2f」）；伤害分派 3 处 switch 收敛 EntityDamage；GameState 2674 行上帝类按域拆 9 个 partial 文件（零行为差异）；Boss 链 226 个双命名桥/动态派发删除 + typed 化（净删 397 行）；UI 结算/存档编排下沉 SettleRun/SaveRun；gen_balance_map 硬编码文件名工具修复（GameState.*.cs 前缀匹配）。
- **关键决策**：Boss Fire_* 桥以 8 个 PascalCase 转发替代（测试契约 HitLogicTest 依赖）；激光不传 ScoreScale 与 _explode 排除 Boss 为既有语义显式保留；PlayerDied 信号 3 订阅者同步时序约束下 UI 编排下沉走「UI 仍订阅信号」方案；任务域服务化与状态机枚举化论证后 defer。
- **教训**：partial 切分脚本模板头与原头冲突（双重类声明/孤儿花括号——壳文件三修）；gen_balance_map 的裸 Cfg 统计按文件名白名单匹配，结构拆分必须同步工具链；「语义等价」机械替换由编译器兜底（StringName 删除后遗漏即编译错误），但工具链行为需独立验证（BALANCE_MAP 计数突变是哨兵）。
- **原文**：`docs/archive/2026-08-09-csharp-architecture-refactor-plan.md`（登记：`docs/AUDIT_VAULT.md` Y 系列）


## 2026-08-09 · 局外成长（Meta Progression / 科技树）

- **落地**：未提交（改动待提交：M1-M5 五批次 + 文档同步）
- **摘要**：Steam 同类调研（Vampire Survivors / Brotato / 20 Minutes Till Dawn / Geometry Wars 3）后落地跨局成长——新增科技点货币（**死亡结算唯一入口** `SettleRun`，放弃/返航不结算防刷点）；效果 = 新局开局预置已购 buff 层数（`ApplyMetaLoadout` 挂 `Main.ApplyNewRun`，复用 buff 计算链零新属性管道）；研究所 UI（Welcome 主菜单入口 + BaseConsole 第五面板）；仅登录用户（B7-8）。core 纯逻辑 `MetaProgression` + `UserDb` meta 字段 + `GameState.Meta` partial + `meta_test` 断言场景（24 断言）+ xUnit 新增 18 项（总 108）。
- **关键决策**：预置挂点选 `Main.ApplyNewRun` 而非 `ResetRun`（tutorial 复用 ResetRun，预置会污染教学流程）；科技点独立于 RP（RP 维持局内基地经济不动）；每项限级 + 总消费上限 → 玩家终将毕业、敌人无界增长 → D1 必死曲线保持；meta 配置键走 Cfg 静态调用 → BALANCE_MAP 生成器自然收录（M3 后未引用键 28→1）。
- **原文**：`docs/archive/2026-08-09-meta-progression-plan.md`


## 2026-08-10 · 过时审计/评审文档归档（C# 评估 + Parity 评审）

- **落地**：随 AA 系列批次提交（见 `docs/AUDIT_VAULT.md` AA 系列登记）
- **摘要**：两份已完成使命的评审/决策文档移出 `docs/` 顶层——`C_SHARP_ASSESSMENT.md`（C# 混编调研→渐进引入→全量迁移决策，M1–M7 已全部落地，评估使命完结）与 `PARITY_REVIEW_2026.md`（2026-08-09 一次性对齐评审，结论「玩法机制零缺口」已固化）；ROADMAP/ARCHITECTURE/csharp-conventions 引用同步指向归档路径（CHANGELOG 历史条目按「归档前快照」惯例不动）。
- **原文**：`docs/archive/2026-08-08-csharp-assessment.md`、`docs/archive/2026-08-09-parity-review.md`

## 2026-08-10 · ENDLESS_BALANCE_PLAN 归档（已实施完成）

- **落地**：本次文档批次（无代码改动——公式与数值已随 2026-08-04 深局校准与 M 系列全部落地：`ProgressionCurves.cs` + `balance.json` `progression.*`/`enemies.*_ramp_factor`/`buffs.extra_life.max_stacks=10`，`DifficultyTest` 钉住）
- **摘要**：无限段深局平衡计划使命完结，全文移入归档；文档核查批次同步：断言场景计数 55→56 漂移修正（TESTING.md/ci.yml）、GameState partial 清单补 Meta、`profile.json` 兼容路径与 UserDbCorrupt 现状回写（.agents/persistence-security.md）、give-up 结算语义澄清（DESIGN_BASELINE）等约 30 处事实修正 + 事件/演出文档已完成段落裁剪
- **关键决策**：唯一遗留开放项「人工手感验证（15+ min real play）」登记至 `docs/ROADMAP.md` pending，不再随本计划跟踪
- **原文**：`docs/archive/ENDLESS_BALANCE_PLAN.md`（引用已同步：DESIGN_BASELINE §1.4 / ROADMAP / ARCHITECTURE 改指归档路径）

## 2026-08-11 · 得分/奖励设计审核（击杀连击 + 低血防御保底）

- **落地**：未提交（改动待提交：GameState 连击 + BuffSelect 保底 + HUD 标签 + combo_test 断言场景 + 文档同步）
- **摘要**：对照业内普遍平衡设计（怒首领蜂/虫姬链式连击、杀戮尖塔/吸血鬼幸存者情境保底）做全量设计审核——确认不变项（D1 必死曲线/DDA/逃脱阀/擦弹/hard ×1.5）后落地两项低复杂度改动：①**击杀连击计分** `scoring.combo`（window 3.0s / step 0.1 / 封顶 ×2.0）：所有击杀路径统一走 `GameState.AddKillScore`，乘区放大后照常过难度倍率；受击（与 DDA 同源 `PlayerDamaged`）/超时/重开断连；Boss 击杀/事件奖励/擦弹不计连击；HUD 连击标签 + `ComboChanged` 信号。②**低血防御保底** `buffs.dynamic_weight`：HP<50% 时防御类（extra_life/regen/armor/shield/evasion）加权 ×2 且三张候选保底 ≥1 张防御卡（满血行为不变，防御满层自然失效）。新断言场景 `combo_test`（17 断言）+ buff33 保底 5 断言；场景计数 56→57 / 65→66。
- **关键决策**：连击上限 ×2.0 温和化——避免高手得分通胀打穿里程碑节奏、破坏 D1 曲线；连击计数封顶后继续增长（不断链）；受击断连与 DDA 共用事件源构成"受击=降档+断连"双通道但均不致命；保底用"加权+替换"语义（可测确定性断言）；不做炸弹/掉落/技能树重构（复杂度预算外）。
- **教训**：BALANCE_MAP 生成器只匹配 `GameState.Instance.Cfg(` 前缀——`gs.Cfg(` 局部变量写法不入静态调用表（BuffSelect 首版漏收录，改直调后重跑收录）；防御满层测试首版只加 1 层（extra_life 上限 10/shield 2 未满）断言假失败——满层测试须按 `max_stacks` 循环加层。
- **原文**：`docs/archive/2026-08-11-score-combo-buff-pity-plan.md`

## 2026-08-11 · AB 系列对抗性复审修复（23 项全量落地，4 批提交）

- **落地**：`5018a7e`（批次1 AB1）→ `45cded1`（批次2 AB11/AB12/AB13）→ `3617d37`（批次3 AB2-AB10/AB14-AB17/AB18）→ `4f277ba`（批次4 AB19-AB23）→ `97e2f25`（docs 回填，[skip ci]）
- **摘要**：第八轮全量对抗性复审 23 项发现全量修复——P1 激光 active 返航哑火（Player.OverrideEntryAutoFire 同步捕获值）；数据安全三件（DeleteUser 先提交 users.json 再删文件、elapsed 巨值双保险钳制、退出确认窗口屏蔽 R 键）；钳制/判型族一次收口（dynamic_weight 子键判型、Boss interval/E2PointCount/Spawner 族/Mothership/GameEventManager 条目判型与遭遇态复位、touch_controls 恢复广播、SpreadEnemyCap/Starfield/榜单钳制）；P3 文档质量（血量比例保序、注释订正、竞态优先级文档同步、孤儿注释删除、boss_bar_segments 死配置删除、键盘调整摇杆滑杆落盘）。
- **关键决策**：AB20 竞态「文档同步为实际行为」而非改跨 autoload 顺序（风险高收益微）；AB22 选删除配置键方案（段数恒由权重数组决定）；AB17 core 归一化不复用 `ToInt64`（double 分支 unchecked 转换 1e300 溢出未定义，独立安全转换）。
- **验证**：dotnet build 0w/0e + xUnit 111→113（AB11 存档幸存 / AB12 巨值钳制 / AB17 榜单钳制）+ format 三工程零 diff + 零 GDScript + import 0 错误 + main smoke 300 帧 + 全量断言场景 57/57 + BALANCE_MAP 重跑零 diff。
- **原文**：`docs/archive/2026-08-11-ab-series-fix-guide.md`
