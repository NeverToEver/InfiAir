# 2026-08-02 全量代码审查与修复计划（D 系列）

> 审查基线：`8c6dfff`（2026-07-29 波次化刷怪）→ `HEAD`（2026-08-02），60 提交、195 文件、+14.2k/-3.9k 行。
> 方法：按 `docs/AUDIT_REVIEW_SOP.md` 分区并行审查（6 路只读代理：对局编排/入场动画、UI 系统、Boss/事件/演出、辅助瞄准/弹道、数值一致性、文档-代码-测试三角），主控交叉核验跨区域矛盾。
> 验证基线（修复前）：`--headless --import` ✅、`--quit-after 300` ✅、`smoke_test` 142 PASS / 0 FAIL ✅、`entry_animation_test` 11 PASS / 0 FAIL ✅。
> 判定原则（SOP §3）：先判 bug / 设计意图 / 口径 / 文档-代码矛盾，不盲改平衡；数值语义查函数定义。
> 关联：修复起效记录回填 `docs/AUDIT_VAULT.md`（D 系列）；数值键变更后重跑 `scripts/tools/gen_balance_map.py`。

---

## 一、发现清单（D 系列登记）

### D01【P2 · 纯 bug / 设计目标未达】入场动画"敌机延迟"缺口——返航前排队的 Timer 与预告线不清理
- 位置：`scripts/spawner.gd:459-462,499`（`_queue_enemy`/`_trigger_boss` 挂一次性 Timer + `SpawnTelegraph`）、`scripts/main.gd:652,695-699`（`_start_homecoming`/`_on_orbital_struck` 只做 `set_process(false)` 与清 Enemy/Bullet）
- 证据：`_schedule`（spawner.gd:537）的 Timer 是 spawner 子节点，`set_process(false)` 不影响子 Timer 走时；返航时树暂停冻结它，继续出击后该 Timer 在入场动画窗口（0~0.6s）触发——敌机带预告线在"无敌人"保证期进场；Boss 预警 2s 后同样可在入场动画期间降入并触发血条。约 1/5~1/10 次返航遇到。
- 判定：**修**。返航/清场时登记并释放 spawner 下 pending 的一次性 Timer 与 SpawnTelegraph。
- 验证：新增/调整测试断言"入场动画结束前无敌机进场"覆盖返航前排队的场景；`entry_animation_test` + `smoke_test`。

### D02【P2 · 一致性】`player.entry.invincible` 回退值不一致
- 位置：`data/balance.json:22`（2.1）vs `scripts/player.gd:21`（`ENTRY_INVINCIBLE := 1.65`，注释"= 冲入 + 后撤，不闪烁"）
- 证据：1.65 = 冲入 0.55 + 后撤 1.1 精确和；json 2.1 多 0.45s 缓冲。全仓 363 个 cfg 键逐值核对唯一不一致（两路独立确认）。JSON 损坏时无敌窗口缩水 0.45s；且 2.1 > 动画总长 1.65，动画结束后走 0.45s 闪烁分支（player.gd:555），"不闪烁"承诺只覆盖动画期内。
- 判定：**修**。统一为 2.1 并更正注释（无敌 = 动画时长 + 0.45s 缓冲，缓冲段按普通无敌路径闪烁处理）。
- 验证：`entry_animation_test` + `balance_test`（损坏回退路径）。

### D03【P2 · 纯 bug】Buff 卡片点选热区被文字标签阻断（已主控核验）
- 位置：`scripts/buff_select.gd:188`（`gui_input` 挂在卡片上）+ `scripts/ui_theme.gd:50`（`make_label` 未设 `mouse_filter`，Label 默认 `MOUSE_FILTER_STOP`）
- 证据：Godot 4 中 STOP 子控件消费鼠标事件不上抛父节点。卡片上名称/描述/分类文字（约 40% 面积、最自然点击目标）点选不触发 `_on_card_gui_input`；hover 高亮同样不触发。键盘 Enter/Space 路径正常。
- 判定：**修**。`make_label` 工厂默认 `MOUSE_FILTER_IGNORE`，需拦截的 Label 显式覆盖为 STOP。
- 验证：`buff_panel_test` + 手工/脚本点击模拟。

### D04【P2 · 一致性】开始面板难度按钮不走 `tr()`，语言切换不刷新
- 位置：`scripts/start_panel.gd:109`（`GameState.DIFFICULTY_DEFS[d]["label"]` 数据驱动"易/中/难"），`_refresh_texts()`（start_panel.gd:143/158）不触碰难度按钮
- 证据：HUD 难度标签走 `tr("DIFF_EASY"/...)`（game_state.gd:352-353），切 en 后 HUD 显示 "Easy/Med/Hard"、按钮仍中文。`DIFFICULTY_DEFS["label"]` 仅被 start_panel 消费。
- 判定：**修**。按钮文案改 `tr("DIFF_" + String(d).to_upper())` 并在 `_refresh_texts()` 刷新。
- 验证：`back_navigation_test` / `startup_flow_test` + 语言切换路径。

### D05【P2 · 文档-代码矛盾】BOSS_REDESIGN §5 的 P2 走位升级长期未实现且未登记
- 位置：`docs/BOSS_REDESIGN.md §5.1-5.3` vs `scripts/boss_movement.gd:30-39`
- 证据：设计表要求一型 P2「strafe 提速 200 + 纵向往复」、二型 P2「冲刺 0.4s/0.5s」、三型 P2「strafe 100 + 纵向往复」、三型 P1「y 200-280 正弦」。实现仅一型 P1 有纵向（`_update_press` 仅 `FIGHT_P1` 调用），P2 无升级。`git show 3188902^` 确认此差距为阶段 B 落地时就有，非 A3 拆分引入；§8 自决点与档案均未登记。
- 判定：**登记不修（待作者确认）**。设计确认类：改行为属产品级改动，超出本轮范围；回写 BOSS_REDESIGN §8 自决点登记差距，防止后人按文档误判现状。若作者确认为遗漏，后续按表格补实现。
- 验证：文档核对。

### D06【P3 · 纯 bug（边缘）】入场动画中断复位缺口
- 位置：`scripts/player.gd:640-642`（后撤段）、`scripts/main.gd:707-711`（`_start_entry_sequence` 守卫 `if _entry_phase != 0 or _dead: return`）
- 证据：返航（B 1.5s 蓄力）若在入场动画开始后 ~0.15s 内触发，`lock_input()` 使 `_physics_process` 提前 return，后撤段冻结、`_finish_entry` 永不执行；继续出击后新入场动画被守卫跳过，玩家原地续完残余后撤（自愈）。B 与入场起始帧重叠概率极低，但状态机缺少"中断后复位"出口。入场期间长按 K 自毁同理（`_die` 不清 `_entry_phase`、不停 tween）。
- 判定：**修**。入场中返航/自毁路径显式复位入场状态（清 `_entry_phase`、停 tween、`_finish_entry` 兜底）。
- 验证：`entry_animation_test` 补中断路径用例。

### D07【P3 · 测试脆弱性】`entry_animation_test` landed 判据提前 break
- 位置：`test/entry_animation_test.gd:55-70`
- 证据：`landed` 以 `player.position.y <= land_y + 5.0` 提前 break——按 QUAD EASE_OUT 该条件在冲入完成前（t≈0.88）触发，仍在 phase 1；后撤阶段断言余量仅 ~20px（约 2 帧抖动）。不覆盖中断路径。
- 判定：**修**。landed 判定改连续两帧 y≥land_y（确认进入后撤），补"动画结束后 auto_fire 恢复/敌机恢复"交互用例。
- 验证：`entry_animation_test` 3 次连续运行稳定 PASS。

### D08【P3 · 性能约定违反】HUD vignette 每帧查 `GameState.max_health()`
- 位置：`scripts/hud.gd:723`（`_update_vignette` 每帧调 `max_health()`，内部 2 次 cfg JSON 查询 + buff_count，game_state.gd:596-598）
- 证据：违反 AGENTS.md"高频 _process 路径 _ready 缓存，不要每帧查 JSON"。`max_health()` 依赖 extra_life 层数（选取后变化），_ready 缓存会过期。
- 判定：**修**。缓存 `max_health` 值，在 buff 变化/受击信号路径刷新。
- 验证：`smoke_test` + `buff_panel_test`（extra_life 选取后 vignette 阈值正确）。

### D09【P3 · 可访问性边界】退出确认取消后焦点归还不完整
- 位置：`scripts/back_navigator.gd:50`（CANCEL_EXIT 分支只对 `_start_panel.visible` 归还焦点）
- 证据：暂停→退出游戏→确认窗→Esc 取消时，焦点留在已隐藏确认窗的"取消"按钮，暂停面板不重夺焦点。
- 判定：**修**。CANCEL_EXIT 后按来源页面归还焦点（暂停面板补 `_pause_ui.grab_primary_focus()`）。
- 验证：`esc_navigation_test` / `back_navigation_test`。

### D10【P3 · 一致性】两处硬编码 960 未收敛（C14 已改同类）
- 位置：`scripts/spawner.gd:510`、`scripts/elite_turret_event.gd:139`（`Vector2(960.0, ...)`）
- 证据：相机固定在 (960,540) 时数学等价于 `view.get_center().x`，但 C14 已把同类改为 view 基线（boss_movement:73、main:114/617、boss_attacks:273），这两处未收敛。
- 判定：**修**。统一 `GameState.view_world_rect().get_center().x`。
- 验证：`elite_turret_event_test` + `smoke_test`。

### D11【P3 · 观察级】Boss 受击闪白多 tween 竞争
- 位置：`scripts/boss.gd:828-830`（`_flash_hit` 每次受击 `create_tween()` 操作同一 `_sprite.modulate`）
- 证据：狂暴锁血期多弹同时命中时多 tween 竞争（后者覆盖前者），表现为闪白抖动；tween 随 boss 释放自动清理，无泄漏。
- 判定：**登记不修**（观察级，无泄漏无逻辑错误）。
- 验证：无。

### D12【P3 · 一致性】C34 例外：`boss_pattern_test` 场景 3 硬编码弹速 900
- 位置：`test/boss_pattern_test.gd:254`（`_bullets_by_speed(900.0)`）
- 证据：C34（1b5f424）只覆盖场景 1/2/4；场景 3 的 900 既未改读实例常量（可用 `boss3.E2_SNIPER_SPEED`）也无来源注释。balance.json 改 `boss.enrage.type_2.sniper_speed` 会静默漂移。
- 判定：**修**。改读实例常量，对齐 C34 收口口径。
- 验证：`boss_pattern_test`。

### D13【P3 · 一致性/文档-代码矛盾】`homing_time=4.0` 对玩家追踪弹是死参数
- 位置：`scripts/bullet.gd:36,194,220`（注释"追踪时限（≈弹寿命）"）
- 证据：玩家弹速 1800、出屏寿命约 1.07s，出界即 `_despawn()`，`_homing_elapsed` 永远到不了 4.0；敌弹（turret 0.6s / boss 1.5s）才会触达时限。
- 判定：**修（收紧配置）**。玩家弹 `homing_time` 收紧至合理值（1.2s 量级）并修正注释；确认 balance.json 与脚本回退同步。
- 验证：`enemy_combat_test` / `smoke_test` 追踪弹路径。

### D14【P3 · 一致性】入场动画两处硬编码未入配置
- 位置：`scripts/player.gd:664`（起点屏外偏移 90px）、`scripts/player.gd:680`（入场水平速度 0.6 倍率）
- 证据：player.gd:10 注释声明"数值在 balance.json player.entry（设计值）"，同功能 land_ratio/rush_time/retreat_speed/retreat_time/invincible 均已入配置，唯这两处为字面量。
- 判定：**修**。入配置 `player.entry`（`spawn_clearance` / `rush_hspeed_ratio`），同步脚本回退 + 重跑 gen_balance_map.py。
- 验证：`entry_animation_test` + BALANCE_MAP 双向反查。

### D15【P3 · 设计权衡】辅助瞄准磁吸/粘性为每渲染帧增量式，绝对强度随刷新率缩放
- 位置：`scripts/aim_frame_layer.gd:139`、`scripts/player.gd:597`（`magnet_pull` 返回每帧位移量，`_aim_smooth` 每渲染帧累加）
- 证据：60Hz 磁吸最大拉速 480px/s，144Hz 下 1152px/s；输入阈值同帧基准 → 高帧率玩家磁吸更强、锥形绑定更宽松。相对手感因输入同基准而自洽。
- 判定：**登记不修（待判定）**。结构性设计选择，改 delta 归一属手感重构，超出本轮范围；登记防止误当 bug。
- 验证：无。

### D16【P3 · 一致性（维护）】磁吸/距离衰减参数双份默认值
- 位置：`scripts/player.gd:74-82` vs `scripts/aim_frame_layer.gd:17-26`（同值各自声明，各自从同一 balance 键读取）
- 证据：当前值一致、无失配，重复维护有未来漂移风险（已有注释说明分工）。
- 判定：**登记不修**（低成本接受）。
- 验证：无。

### D17【P3 · 代码卫生】轨道打击每帧取视口尺寸 / 母舰召唤帧率依赖时间基准
- 位置：`scripts/orbital_strike.gd:186-187`（命中段每帧 `get_viewport().get_visible_rect().size`，`_ready` 已取过 `screen` 可复用）、`scripts/mothership_summon_window.gd:271`（`_update(t)` 每帧 `get_process_delta_time()` 与 `_process` delta 重复）
- 判定：**修**（低风险）。复用缓存 / 用 delta 参数。
- 验证：`orbital_strike_test` / `mothership_summon_test`。

### D18【P3 · 一致性（待判定）】返航过场音效未应用 8-02 统一音频策略
- 位置：`scripts/return_cinematic.gd`（14 处 `play_sfx`，-6/-10/-12/-14/-16/-18dB 等既有值）；对照 `scripts/intro_cinematic.gd`（8-02 统一 `AUDIO_VOL_OFFSET=-6dB`/`AUDIO_PITCH=0.88`，5008489）
- 证据：5008489 只统一开场过场并同步 INTRO_CINEMATIC.md；RETURN_HOME_CINEMATIC.md 无音频约定。返航音效本就各自压低，是否统一属产品判断。
- 判定：**登记待判定**。不改行为；在 RETURN_HOME_CINEMATIC.md 补一句音频口径说明，防止后人误判。
- 验证：文档核对。

### D19【P3 · 一致性】节点 scale 线宽缩放残留（C28 收口后）
- 位置：`scripts/warp_gate.gd:157-159`（`_swirls[i].scale`/`_lip.scale`）、`scripts/formation_bomb.gd:80`（`_ring.scale` 0.15~0.9）、`scripts/main.gd:331`（`_charge_rings[i].scale` 0.7~2.2）
- 证据：C28 已修 `_ring`/`_arcs` 等中环 scale 放大回归；剩余处 scale ∈ [0,1] 收缩、HOLD 呼吸 1.04 时线宽放大 4% 可忽略，均无回归级放大，视觉合理。
- 判定：**登记不修**（视觉合理）。
- 验证：无。

### D20【P3 · 维护】`data/balance.json.bak` 过期
- 位置：`data/balance.json.bak`
- 证据：缺失 `enemies.speed_ramp_factor`、`player.aim_assist` 段、`player.entry` 段；`formation_strike_event.bomb_interval` 为旧值 0.35 vs json 0.8。bak 是 `balance_editor.py` 编辑前自动备份，不参与运行时；落后说明近期改动绕过编辑器直接落盘。
- 判定：**登记不修**（下次编辑器打开保存自动刷新）。流程提醒：改 balance.json 后同步脚本回退值这一步偶有遗漏（D02 同源）。
- 验证：无。

### D21【P3 · 文档-代码矛盾】EXIT_FLOW 伪代码残留"欢迎页"
- 位置：`docs/EXIT_FLOW.md:49`（`CONFIRM_EXIT: # 顶层（开始面板 / 欢迎页）`）
- 证据：2c16892 只更新第 1 节 L0 层级图（:17）与 back_navigator 枚举注释，漏第 2 节伪代码；移除计划文档:282 也只列了 :17，计划自身遗漏。行为正确（back_navigator.gd:117-118 已只查 `_start_panel.visible`）。
- 判定：**修**（删" / 欢迎页"）。
- 验证：文档核对。

### D22【P3 · 文档-代码矛盾】README 仍描述首启欢迎页流程
- 位置：`README.md:92`、`README.en.md:92`（"首次进入有欢迎页与 6 阶段教程"）
- 证据：欢迎页已删、启动直达主菜单；2c16892 未触碰 README 中英版。
- 判定：**修**。
- 验证：文档核对。

### D23【P3 · 文档-代码矛盾】AGENTS.md profile 字段描述含"欢迎页"
- 位置：`AGENTS.md:104`（"profile 保存最高分、难度、键位、语言、视角、窗口尺寸、欢迎页/教程状态等"）
- 证据：`welcome_seen` 已从 save/load 删除（2c16892），AGENTS.md 未同步。
- 判定：**修**（删"欢迎页/"）。
- 验证：文档核对。

### D24【P3 · 文档-代码矛盾】DESIGN_BASELINE §6 持久化含"欢迎页"
- 位置：`docs/DESIGN_BASELINE.md:301`
- 证据：同 D23；2c16892 只改了 :119/:141/:144 三处，漏 :301。
- 判定：**修**。
- 验证：文档核对。

### D25【P3 · 文档过期】DESIGN_BASELINE 断言场景数 29→30、"C 系列 35 项全量修复"表述过宽
- 位置：`docs/DESIGN_BASELINE.md:7/:292/:361`（"29 个断言场景"）、`:9`（"C 系列 35 项已全量修复"）
- 证据：8-02 新增 `entry_animation_test` 后实际 30 个断言场景；C34 ⚠️ 部分完成、C19 设计确认不改码、C33 已核实无风险不修——"全量修复"与 AUDIT_VAULT 实况矛盾。
- 判定：**修**。29→30；"35 项全量修复"改"35 项已处理（收尾）"。
- 验证：文档核对。

### D26【P3 · 口径不一致】ROADMAP A7 数字口径
- 位置：`docs/ROADMAP.md:9`（"A7 测试白盒 855 处全清"）vs `docs/AUDIT_VAULT.md:162`（"测试侧 28 处 + 游戏侧 5 处"）
- 证据：855 或为 sed 批量总替换数，与档案口径不一致，方向一致。
- 判定：**修**（统一为档案口径或补注）。
- 验证：文档核对。

### D27【P3 · 流程遗留】欢迎页移除计划文档 checkbox 未勾
- 位置：`docs/2026-08-01-remove-welcome-screen-plan.md`（25 个 task checkbox 全未勾选，无"已完成"标记）
- 证据：8-02 已执行完毕；开头"全量 29 断言场景"执行时已是 30。
- 判定：**修**（加完成注记与日期，勾选全部 checkbox，29→30 表述修正）。
- 验证：文档核对。

### D28【P3 · 一致性】翻译孤儿键 2 个
- 位置：`data/translations.csv:103`（`GO_SCORE`"得分：%d"）、`:213`（`UI_KILLS_TAG`"击杀"/"KILLS"）
- 证据：scripts/autoload/test 零引用；原结算页得分格式已改 `UI_SCORE_TAG` + 数字直显（game_over_ui.gd:26-29）。
- 判定：**修**（删两键，保持 csv 三列完整）。
- 验证：`i18n_test` + `smoke_test`。

### D29【P3 · 一致性】AUDIT_VAULT C17 记录与 back_navigator 现状
- 位置：`docs/AUDIT_VAULT.md:350`（C17 修复记录）vs `scripts/back_navigator.gd:22-31`（8 处 `@onready get_parent().get_node("固定兄弟")` 裸调用）
- 证据：C17 登记含 back_navigator:22-31，但修复记录只提 welcome_screen/pause_ui；pause_ui.gd:143 已改 `get_node_or_null`，back_navigator 仍裸调用。访问对象是 main.tscn 固定子节点，风险低。
- 判定：**登记不修**（合理模式）或补注；本轮补注说明。
- 验证：无。

### D30【P3 · 一致性】A4b 事件分数门槛不对称
- 位置：`scripts/spawner.gd:123-124`、`scripts/scheduled_event_trigger.gd:16` vs `scripts/formation_strike_event.gd`
- 证据：elite 的 min_score(800) 在 `ScheduledEventTrigger.tick()` 内拦截；formation 构造传 min_score=0、分数门槛(500)仍留在 `FormationStrikeEvent.can_trigger()`。行为正确（can_trigger 先于 tick 门控），但两事件分数门槛语义分散两处、不对称。
- 判定：**登记不修**（行为正确）。
- 验证：无。

---

## 二、修复计划（分批执行）

### 批次 1：文档批（无行为影响，先修）
| 编号 | 修改 | 验证 |
| --- | --- | --- |
| D21 | EXIT_FLOW.md:49 删" / 欢迎页" | 文档核对 |
| D22 | README.md / README.en.md:92 欢迎页描述修正 | 文档核对 |
| D23 | AGENTS.md:104 profile 字段删"欢迎页/" | 文档核对 |
| D24 | DESIGN_BASELINE.md:301 删"欢迎页"表述 | 文档核对 |
| D25 | DESIGN_BASELINE.md 29→30、全量修复→已处理 | 文档核对 |
| D26 | ROADMAP.md:9 A7 口径统一（补注） | 文档核对 |
| D27 | 移除欢迎页计划文档勾选 + 完成注记 + 29→30 | 文档核对 |
| D28 | translations.csv 删 GO_SCORE / UI_KILLS_TAG | i18n_test |
| D05/D18 | BOSS_REDESIGN §8 登记走位差距；RETURN_HOME_CINEMATIC 补音频口径 | 文档核对 |
| D29 | AUDIT_VAULT C17 补注 back_navigator 评估 | 文档核对 |

### 批次 2：代码批 P2（实质缺陷）
| 编号 | 修改 | 验证 |
| --- | --- | --- |
| D01 | spawner 登记 pending Timer/SpawnTelegraph，返航/清场时释放 | entry_animation_test + smoke_test |
| D02 | player.gd 回退值 2.1 + 注释更正 | entry_animation_test + balance_test |
| D03 | ui_theme.make_label 默认 MOUSE_FILTER_IGNORE + 需拦截者显式 STOP | buff_panel_test |
| D04 | start_panel 难度按钮 tr() + _refresh_texts | startup_flow_test / back_navigation_test |

### 批次 3：代码批 P3（低风险修复）
| 编号 | 修改 | 验证 |
| --- | --- | --- |
| D06 | 入场中断复位（返航/自毁清 _entry_phase + 停 tween） | entry_animation_test |
| D07 | entry_animation_test landed 判据 + 补交互用例 | entry_animation_test ×3 |
| D08 | hud vignette 缓存 max_health + 信号刷新 | smoke_test / buff_panel_test |
| D09 | back_navigator CANCEL_EXIT 焦点归还 | esc_navigation_test |
| D10 | spawner.gd:510 / elite_turret_event.gd:139 用 get_center().x | elite_turret_event_test |
| D12 | boss_pattern_test 场景 3 改读实例常量 | boss_pattern_test |
| D13 | bullet homing_time 收紧 + 注释 | enemy_combat_test / smoke_test |
| D14 | 入场硬编码入配置（spawn_clearance / rush_hspeed_ratio）+ 回退值 + 重跑 map | entry_animation_test + BALANCE_MAP |
| D17 | orbital_strike 复用 screen / mothership 用 delta 参数 | orbital_strike_test / mothership_summon_test |

### 判定不修（登记，不产生代码改动）
D11、D15、D16、D19、D20、D30；D05/D18 仅文档登记。

---

## 三、修复状态追踪（执行中回填）

| 编号 | 状态 | 修复说明 | 起效验证 |
| --- | --- | --- | --- |
| D01 | ✅ 已修复 | spawner 登记 pending Timer/SpawnTelegraph + `clear_pending()`，main 返航调用 | entry_animation 13 / smoke 142 / 全量 0 FAIL |
| D02 | ✅ 已修复 | player 回退值 2.1 + 注释对齐 | balance_test 28 |
| D03 | 🟥 证伪不修 | Label 默认 mouse_filter=IGNORE（实证），原机制判断不成立 | 实证打印 |
| D04 | ✅ 已修复 | 难度按钮 tr() + _refresh_texts 刷新 | startup_flow 36 / back_navigation 24 |
| D05 | ✅ 已修复 | BOSS_REDESIGN §5.5 落地——_move_bob/_move_band、三型 P1 缓慢下压回升、二型 P2 冲刺更频；配置 boss.movement 11 键 | boss_phase 37 / 全量 0 FAIL |
| D06 | ✅ 已修复 | abort_entry() + 返航/自毁调用 | entry_animation 13 |
| D07 | ✅ 已修复 | landed 判据改邻域连续 8 帧 + auto_fire 2 断言 | entry_animation 13（13=11+2） |
| D08 | ✅ 已修复 | hud 缓存 max_health + buffs_changed 刷新 | smoke 142 / buff_panel 16 |
| D09 | ✅ 已修复 | CANCEL_EXIT 补暂停面板焦点归还 | esc 11 / back_navigation 24 |
| D10 | ✅ 已修复 | spawner/elite_turret 锚点改 get_center().x | elite_turret 57 / smoke 142 |
| D11 | 🟦 不修 | 观察级，无泄漏（判定分类记录） | — |
| D12 | ✅ 已修复 | 场景 3 改读 boss3.E2_SNIPER_SPEED（C34 收口） | boss_pattern 55 |
| D13 | ✅ 已修复 | HOMING_TIME 4.0→1.2（json+回退同步） | enemy_combat 32 / smoke 142 |
| D14 | ✅ 已修复 | spawn_clearance/rush_hspeed_ratio 入配置 + map 重跑 | entry_animation 13 / BALANCE_MAP 反查干净 |
| D15 | 🟦 不修 | 设计权衡登记（帧率依赖结构性） | — |
| D16 | 🟦 不修 | 双份默认值接受（已注释分工） | — |
| D17 | ✅ 已修复 | orbital 缓存 _screen；mothership 用 delta 参数 | orbital 15 / mothership 28 |
| D18 | 📄 已登记 | RETURN_HOME_CINEMATIC §9 音频口径说明 | 文档核对 |
| D19 | 🟦 不修 | 线宽 ≤4% 视觉合理 | — |
| D20 | 🟦 不修 | bak 编辑器备份产物，打开保存自动刷新 | — |
| D21 | ✅ 已修复 | EXIT_FLOW.md:49 删" / 欢迎页" | 文档核对 |
| D22 | ✅ 已修复 | README 中英欢迎页描述修正 | 文档核对 |
| D23 | ✅ 已修复 | AGENTS.md profile 字段删"欢迎页/" | 文档核对 |
| D24 | ✅ 已修复 | DESIGN_BASELINE §6 同步 | 文档核对 |
| D25 | ✅ 已修复 | DESIGN_BASELINE 29→30 + 全量修复→已处理 | 文档核对 |
| D26 | ✅ 已修复 | ROADMAP A7 口径统一 | 文档核对 |
| D27 | ✅ 已修复 | 计划文档勾选 + 完成注记 | 文档核对 |
| D28 | ✅ 已修复 | 删 GO_SCORE/UI_KILLS_TAG 孤儿键 | i18n_test 9 |
| D29 | 🟦 不修 | C17 补注（back_navigator 合理模式） | 文档核对 |
| D30 | 🟦 不修 | A4b 门槛不对称登记（行为正确） | — |

**全量回归（修复后）**：29 断言场景 0 FAIL + perf_bench rc=0 + `--quit-after 300` 0 error；autoplay 探针完整跑（480s、3 对局、0 死亡）：孤儿节点 0、帧耗时峰值 7.43ms（基线 6.90ms），**1 个 score_stagnant 偶发**——Boss 战专注期（玩家血量 43 龟缩躲弹）分数 60s 停滞 + Boss 逃跑空窗瞬间触发；该 run 返航 0 次、异常点与 D 系列改动路径（返航/入场/Timer 清理）无交集，判定为既有探针偶发（同 smoke 偶发基线），非本次改动引入。

---

## 四、E 系列登记（2026-08-02 存量盲区补充审查，只登记未修复）

> 背景：D 系列聚焦近期 60 提交改动；本批补充审查**未改动存量盲区**（敌人体系 / 演出·特效·母舰 / 系统服务·杂项，共 28 脚本），3 路并行 + 主控交叉核验。主控已核实全部 P1/P2 关键发现（C20 diff、教程按钮守卫、难度校验实况）。
> 处置：登记时按用户指示**只登记不修复**；判定建议供后续决策。**2026-08-02 已按判定建议全量处置**（E01-E08/E10-E12 修复，E09/E13-E15 登记不修），修复批次与验证见 `docs/2026-08-02-e-series-fix-plan.md`，回填见 `docs/AUDIT_VAULT.md` E 系列。

### E 系列发现清单

| 编号 | 严重度 | 位置 | 类别 | 描述与证据 | 判定建议 |
| --- | --- | --- | --- | --- | --- |
| E01 | P1 | `scripts/bullet.gd:240-246` | 纯bug（C20 静默回归） | 母舰导弹溅射 `_splash()` 对 Boss 失效：C20 把 `node as Area2D` 改为 `as Enemy`（注释"注册表全为 Enemy"前提错误——注册表含 Boss，Boss `extends Area2D` 非 Enemy），cast 得 null 被跳过。注释明确"含主目标与 Boss"，溅射 20 伤害静默丢失（直击 80 仍有效）。`_explode` 的 Boss 排除为有意设计，修复时不可连带 | **修** |
| E02 | P2 | `scripts/start_panel.gd:275` + `scripts/tutorial.gd:97` | 纯bug（玩家有损路径） | 教程按钮通关后未禁用/隐藏；`_on_tutorial_pressed()` 无 `tutorial_done`/`has_save` 守卫直接 change_scene；`tutorial.gd:97` 无条件 `delete_save()`。对局中存档 → 开始面板点「教程 ✓」→ 进行中存档被静默删除 | **修（最优先）** |
| E03 | P2 | `autoload/game_state.gd:118-125` | 设计目标未达（C03 半堵） | `_valid_difficulty_defs` 只校验 easy/medium/hard 是 Dictionary，不校验子键；部分损坏（缺 hp/score 等）通过后 8 处 `DIFFICULTY_DEFS[difficulty][...]` 访问 KeyError→0：敌方 0 HP 秒死、得分倍率 0 | **修** |
| E04 | P2 | `scripts/dawn_station.gd:282-286` + `scripts/return_cinematic.gd:568,702` | 设计目标未达/一致性 | PHANTOM 工厂对 `station.modulate.a` 循环呼吸（0.85↔1.0），return_cinematic 两处压站体 alpha（0.35/0.5）被 tween 抬高 2.5~3 倍；base_console 用包装节点规避（同工厂两种用法不一致） | **修** |
| E05 | P2 | `scripts/mothership.gd:414-432` | 纯bug（边缘） | H 按住时被强制离舰（警告到期/弹匣耗尽走 `start_release()`），`_hud.set_early_leave_charge(-1.0)` 未调，提前离舰进度条残留可见 | **修** |
| E06 | P2 | `scripts/enemy.gd:469` | 一致性（D10 同类未收敛） | 侧方离场方向 `position.x < 960.0` 硬编码；相机固定 (960,540) 时等价，滚动即错 | **修（一行）** |
| E07 | P3 | `scripts/bullet.gd:230` | 文档-代码矛盾 | `_explode` C20 注释"注册表全为 Enemy"与事实不符（含 Boss；as Enemy 对 Boss 恰为 null 行为巧合正确）——同 E01 错误前提，掩盖了真实回归 | **修（与 E01 同批）** |
| E08 | P3 | `scripts/laser_weapon.gd:66-67` | 纯bug（当前不可达） | buff 计数归零早退冻结激活态光束（`_end_beam` 不执行、autofire 卡禁）；当前无 buff 移除机制不可达，未来引入即触发 | 待判定（顺手兜底：早退前 `if _active: _end_beam()`） |
| E09 | P3 | `scripts/laser_weapon.gd:13` | 一致性（待判定） | `BEAM_HALF_WIDTH := 26.0` 判定半宽未乘 ws（`ENEMY_HIT_RADIUS` 已乘）；AGENTS 约定激光判定随机体特效比例应乘 ws | 待判定（现视觉可接受可不修） |
| E10 | P3 | `autoload/game_state.gd:947` | 一致性（低危） | `locale = str(parsed.get("locale", "zh"))` 绕过 `set_locale()` 守卫：手改 "fr" 时 `locale` 变量与 TranslationServer 状态不一致 | 待判定 |
| E11 | P3 | `autoload/game_state.gd:958-959` | 一致性（C02 元素级缺口） | key_bindings 外层类型已守卫，数组元素 `int(k)` 未判型：手改字符串 keycode 报转换错误刷屏（不崩溃） | 待判定 |
| E12 | P3 | `scripts/save_manager.gd:21-28` | 一致性（存量行为） | `save()` 直接 WRITE 覆盖非原子写；写入中途崩溃产生截断 JSON，下次 load 隔离 .corrupt（自愈但丢进度） | 待判定（可改临时文件+rename） |
| E13 | P3 | `scripts/player_damage.gd:64-69` | 热路径约定边缘 | `heal_tick()` 每物理帧调 `passive_regen_delay()/passive_regen_rate()`（嵌套字典查找）+ buff_count；可在 configure() 缓存难度参数 | 待判定 |
| E14 | P3 | `scripts/mothership.gd:171-174` | 一致性 | `beam_pts[i] *= ws` 累乘写回 polygon，字面违反幂等约定；当前安全（polygon 为节点内联属性非共享 sub_resource） | 不修（注明安全）或改幂等措辞 |
| E15 | P3 | `scripts/enemy.gd:385` | 性能约定轻微 | `_physics_process` 每帧 `GameState.buff_count(&"slow_field")`（字典 get 无分配，开销极小） | 不修（登记备查） |

### 判定分类说明

- **必须修（P1/P2 行为）**：E01（溅射回归）、E02（删档路径，最优先）、E03（难度校验）、E04（视觉回归）、E05（HUD 残留）、E06（硬编码）、E07（注释，随 E01）。
- **待判定**：E08-E13——均为低危/不可达/存量行为，修复收益小或需产品判断。
- **不修登记**：E14（当前安全）、E15（开销极小）。
- **核实通过（不列发现）**：对象池双防护/注册表成对增删、`_move_ctx` 复用与查表三角、A2 四服务委托完整、A8 组件转发、A4b 触发语义等价、C01/C13/C16/C27 无回退、enemy 数值回退与 balance.json 逐项一致。
