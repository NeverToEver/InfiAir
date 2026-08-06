# Changelog

本项目版本变更记录。格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)。版本号为 MAJOR.MINOR 递增（项目惯例，非完整 SemVer），版本同步点见 `release.sh` 与 `project.godot` `config/version`。**早期版本（≤ 3.22）变更细节见 `git log`**。

## [3.27] - 2026-08-06

### 规范化（2026-08-06，项目安排对齐官方/社区实践，Playwright 调研 + 本机实证）

- **config/version 腐蚀修复（技术修正）**：`project.godot` 版本注释误用 `#`（ConfigFile 仅认 `;`）——引擎加载时注释行与下一行键名熔合，`application/config/version` 对引擎长期不可读（`get_setting` 实测返回空）；改 `;` 注释后恢复读取（探针实证 3.27）
- **project.godot 引擎规范化**：以 `ProjectSettings.save()` 实际输出对齐——`[debug]` 段归位字母序（原置文件尾，编辑器每次重存必产生噪声 diff）；剔除默认值冗余行（`window/stretch/aspect="keep"`、`gdscript/warnings/enable=true`，均为引擎默认值）；规范化后引擎重存零 diff 实证
- **导出配置实证复核（结论：原配置正确，零改动）**：`--export-pack` + 独立 PCK 虚拟文件系统转储逐项核验——`exclude_filter="test/*"` 在 4.6.2 真实生效（R01 结论成立）；`data/balance.json` 作为 JSON 资源随 `all_resources` 自动进包（官方文档"json 需 include_filter"表述对 4.6 已过时）；0 个 .py/.sh/.md/builds 产物泄漏。注：strings 提取法对 PCK v3 不可靠，初审曾误判 test 泄漏与 json 缺失，须以引擎转储为准
- **`.gitattributes` 新增**（官方 VCS 页推荐、仓库缺失）：全文本 LF 规范化 + `*.bat` 检出 CRLF（cmd.exe goto/label 边界）+ `*.sh` 强制 LF
- **`builds/.gdignore`**：导出产物目录退出编辑器文件系统扫描（对齐 `docs/.gdignore` 约定）
- **release.sh 版本回退硬失败化**：sed 取不到 `config/version` 时由静默回退 3.26（产出与 project.godot 脱节包名）改为 stderr 报错退出
- **入口提示词精简（agent-md-refactor 流程，快照 /tmp 留存）**：CLAUDE.md 删除 `## Architecture Essentials` 整节（8 条要点与 `.agents/*` 逐条重复，且体碰描述已随 P0-2 漂移失实——独有信息迁入 collision-view.md：ENRAGE_HP_RATIO 锁血 30% + enemy 事件驱动/boss 有意轮询体碰机制修正）；CLAUDE.md 另修 godot "not on PATH" 失实表述（本机即在 PATH）与 pre-commit 段代码围栏缺失（`#` 标题误渲染）；剔除审计考古注记（start_radar 删除/gdtoolkit R09/run.bat 原实现叙述/world_scale 日期）与 game-ui-ux 技能溯源元数据——提示词载体 207→199 行，零规则丢失逐条核对

### 审计（2026-08-05，R 系列独立审计修复，`docs/archive/2026-08-05-independent-audit-report.md`）

- **发布包净化（R01）**：`export_presets.cfg` 两预设 `exclude_filter="test/*"`——此前全部 45+ 测试场景/脚本随发布包分发（PCK 实锤），重出即净
- **BGM 交叉淡化修复（R02）**：`generate_audio.py` 和弦权重有效区间扩至 `CHORD_DUR+XFade`（原严格截断 → 每 5s 交界零谷塌陷，已烘焙进旧资产）；`bullet_fire` 三变体生成前重置随机种子对齐提交资产（消除生成器-资产漂移）；重生成后仅 `bgm_loop.wav` 变化
- **离线工具链（R03/R08/R09）**：sprite 三生成器输出路径锚定脚本位置（非仓库根运行不再错落盘）；balance_editor 读侧损坏友好 400；release.sh tar/zip 前置检查 + 版本自动读 project.godot；run.bat 版本判定 + 退出码保真；CI gdtoolkit 锁 4.5.0
- **防御纵深（R04-R07/R13/R14）**：Q19/Q23 修复两侧遗漏补齐（池化 spawn 信号流对称、startup_flow 快照顺序）；判型族 10 处（starfield/spawner/WEAK_LOCK/狂暴 interval/hp_mults 正值域/存档负值/bullet 零速）；防御缺口 3 处（锁输入弹反盾残留/volley 进行中守卫/escape 常规攻击清理）；`Bullet.COLLISION_RADIUS` 常量（player 擦弹判定互引）；移动策略 freqs/phases 长度校验
- **清理与文档（R10-R12/R15）**：测试规范 3 处（调试 print/OR 弱断言/InputMap 收尾）；注释失实 6 处；M07-M09 落地（back_navigator CONFIRM_EXIT 死分支/start_panel+start_radar 孤儿脚本/SET_LANGUAGE_ZH+EN 孤儿键）；RING_BURST_COUNT 死数据删除；load_steps/AGENTS 计数/EXIT_FLOW/金库状态表/BALANCE_MAP 同步
- 验证：gdformat/gdlint/import 0 error/18 定向场景/45 断言场景 0 FAIL；生成器实证（音频逐字节 A/B、BALANCE_MAP 469 调用、sprite 零 diff）

### 架构（2026-08-05，统一实体管理器，`docs/ENTITY_MANAGER.md`）

- **统一实体管理器 `EntityManager`**（`EntityRegistry` 演进）：实体注册样板收敛——`bind_enemy`/`unbind_enemy` 一行（`add_to_group("enemy")` + 注册/注销 + `entity_registered`/`entity_unregistered` 生命周期信号，新功能订阅零改动单位类），enemy/boss/turret_battery/formation_craft 四处重复样板消除
- **批量操作 API**：`for_each_enemy`（失效实例跳过 + 谓词过滤）/`clear_enemies`（保留项谓词，如轨道打击保 Boss）/`count_enemies`——main 轨道打击清场、母舰慢速场与索敌（方法引用保 P2 缓冲零分配）、狂暴倾巢齐射、spawner spread 计数统一迁移
- **可行性与收益**（Playwright 调研）：真实 Godot 项目 underkingdom 的 Autoload EntityManager 实战印证模式；对象池社区指南确认低频实体不池化（Godot 4 节点创建快）——Boss/炮塔/编队保持直建，仅敌机/子弹/爆炸池化不变
- 验证：新增 `entity_manager_test`（绑定幂等/组同步/信号/批量谓词过滤/保留项清除）；池化语义、GameState 转发面、autoplay 组↔注册表一致性回归不变；全量 45 断言场景门禁

### 架构（2026-08-05，统一事件管理器，`docs/EVENT_MANAGER.md`）

- **统一事件管理器 `GameEventManager`**（`GameState.events`，挂 GameState 下）：全部随机游戏事件（迷雾 4 + 遭遇 2）收敛进单一注册表 `EVENT_FACTORIES`——统一触发策略（balance key 零变化：`fog_events.*`/`elite_turret_event.trigger_*`/`formation_strike_event.trigger_*`）+ 分组并发（`fog`‖`encounter`，组内单事件并发、组间并行，保持现状行为）+ 统一生命周期 + 信号 `event_started/event_ended`
- **遭遇事件迁移**：精英炮塔/轰炸编队触发策略移出 `spawner._process`（`ScheduledEventTrigger` 退役）；事件保持 Node 状态机（实体生成/FSM 自驱），管理器经鸭子类型契约驱动（`is_active`/`start`/`abort`/`can_trigger`），测试 API（`main.event()`/`main.formation()`/`spawner.elite_event()` 等）零改动；触发门控 = 注入 spawner 处理中（`set_process(false)` 语义与现状一致）
- **迷雾收敛**：`FogEventManager` 重构为迷雾效果层 + API 门面（视觉基座/迷雾信号重发/context 构建保留；公开 API 与配置 var 全部转发到 `GameState.events` fog 组）——`fog_event_test` 零改动通过
- 验证：新增 `event_manager_test`（统一注册表/实例同一性/强制触发/组并发/自动触发门控/跨组并行/门面委托）；全量 44 断言场景 0 FAIL（2026-08-05 回归）；240s autoplay 探针实测遭遇事件经管理器正常触发（37.3s 炮塔 / 177.9s 编队，无 ANOMALY）

### 玩法（2026-08-05，基地任务轮换 + 迷雾事件，`docs/FOG_EVENTS.md`）

- **基地任务轮换**：任务池（`MISSION_POOL` 9 任务 = 3 类 × 3 档）+ `TaskPool` 无放回抽取（洗牌游标，排除在场 id）；刷新点数经济（进基地 +1 / 刷新 −2，`base_task.refresh_cost`/`grant_per_visit`，存档往返）；刷新保留已完成未领取任务（不吞待领奖励）；任务进度改按 `kind` 分发（kill/survive/boss），轮换后 id 变化仍自动推进；基地任务面板渲染 `active_mission_ids()` + 刷新按钮/点数不足提示
- **迷雾事件系统**：全局单例 `FogEventManager`（挂 GameState 下，维持唯一 autoload 约定）——概率触发（`trigger_chance`/`check_interval`）+ 开局保护（`first_delay`）+ MinInterval 冷却 + Duration 到期自动清除 + 单事件并发；信号（`fog_event_started/ended/fog_direction_shift`）解耦触发与效果
- **事件类接口（可扩展基底）**：通用 `GameEvent`（纯生命周期接口：context 注入/幂等 end/重复 start 自愈/浅拷贝隔离，零系统耦合）→ 迷雾专门化 `FogEvent` → 具体事件；`EVENT_FACTORIES` 注册表一行注册即接入；自动触发仅真实对局（`current_scene == main`）开启，测试上下文默认关闭（确定性）
- **事件宽容性**（2026-08-05 调研官方 OOP 实践/社区后落地）：同一接口同时接受简单（仅 `event_id()`）与复杂（`_on_start/_on_tick/_on_end` 全钩子）事件；新增 `request_end()`（复杂事件内部目标达成可主动提前结束，经 context 回调）与 `get_ctx()`（简单事件一行读自定义数据）；fog_event_test §10 新增 12 项宽容性断言（复杂事件 request_end/极简事件全生命周期/get_ctx 缺键降级）
- **4 种干扰事件**：伪敌机（无伤害/无碰撞幽灵机群，`FakeEnemy`）、精神错乱（输入反转 + 全屏变色层）、子弹错误（出膛弹角度偏移/慢速失误弹/射速扰动）、短间隔随机方向（周期强制移动向量偏转）；返航/死亡自动清除
- 健壮性（2026-08-05 审计）：事件类作为后续所有事件的唯一入口——空注册表/非 Callable 防御、Timer 先行防事件 start 抛错挂死、context 缺键降级不崩；`fog_event_test` 新增 §8 事件类生命周期守卫与 §9 编排器防御路径共 14 项断言
- 验证：gdformat/gdlint/import 无新增告警；新增 `base_task_refresh_test`/`fog_event_test` 断言场景，既有 41 场景 0 FAIL 回归通过（base_system/elite_turret/formation/mothership_summon/return_cinematic/hit_logic/grace/parry/buff_effects/i18n 等）

### 性能（2026-08-05，主架构运行效率审计全量执行，`docs/archive/2026-08-05-main-architecture-optimization-report.md`）

- **P0-1 死亡回放录制零分配化**：数据源 `get_children()` 改敌弹注册表（`EntityRegistry.enemy_bullets`），帧缓冲改固定容量环形缓冲（删 `pop_front` O(n) 移位），内层 `[x,y]` 改 `PackedFloat32Array` 复用槽——全对局唯一高危常驻分配链消除
- **P0-2 敌机体碰事件驱动**：`area_entered/exited` 标记重叠 + 重叠期 O(1) 守卫重掷替代每物理帧 N 次 `overlaps_area` 空间查询（无敌/闪避/单帧语义与轮询完全等价）
- **P0-3 子弹渲染合并**：弹体+白芯双 Polygon2D 合并为单 Sprite2D + 共享图集（扫描线光栅化），同阵营同色触发 compat batcher 合批——**窗口实测 181 颗子弹 draw calls 245→38（-85%）**，视觉经像素级核验一致
- **P1**：爆炸/溅射遍历去 `duplicate()` 拷贝（倒序索引）；玩家 spread 循环外提 `pow()`；HUD 仪表 epsilon 守卫；BGM 改 `CACHE_MODE_REUSE`（不再每次进 main 重新解码）+ 裂纹场烘焙延后首帧后；爆炸池回池 reparent 统一 `ExplosionPool` 节点
- **P2**：Meta HUD 自适应增益改注册表/静态计数（`Bullet.active_count`/`Explosion.live_count`）；同屏敌弹显式硬上限 500（仅敌弹，15 处调用方判空）；碰撞 mask 自查通过（无隐形碰撞对）
- 验证：gdformat/gdlint/import/quit-after/smoke 全绿，41 断言场景 0 FAIL；窗口 draw call 实测 + 像素视觉核验；AUDIT_VAULT P 系列登记回填

### 美工（2026-08-05，buff 槽位与图标重构）

- **buff 槽位 socket 化**（纯视觉，无逻辑链路改动）：`ChamferedPanel` 新增可选 `inner_frame`（外轮廓内缩 3px 的嵌套切角细线，默认关）；`UITheme.make_buff_socket()` 统一槽位工厂——分类色描边（0.7）+ 同色内框（0.28）+ 面板底向分类色微倾 16% 底，HUD 图标坞瓦片（46×46）与 Buff 三选一卡片图标位（76×76）共用同一套槽位语言
- **×N 层数徽标芯片化**：坞瓦片右下角由裸文字改为切角小芯片（深底 + 分类色描边 + 金色数字，与滚动栏明细行 ×N 同色）；收起态 +N 溢出格同步 socket 样式（淡色底 + 内框）
- **字形小尺寸可读性**：`ui_buff_icons.gd` 线宽加 2px 下限（`maxf(2.0u, 2.0)`），HUD 瓦片 26px 下不糊、卡片大尺寸随缩放自然放大；19 字形设计不变
- 验证：gdformat/gdlint/import 无新增告警，41 断言场景 0 FAIL（buff_panel/buff33/buff_visuals/buff_effects 重点回归），hud_capture（常态/极端/展开三形态）+ ui_capture 窗口截图人工核验

### 平衡（2026-08-04，无限段深局校准，`docs/archive/2026-08-04-endless-calibration-plan.md`）

- **无限段深局校准落地**（ENDLESS_BALANCE_PLAN §6.1，此前 >15 min 校准 deferred）：`progression.per_boss_kill` 0.5→**0.6**、`per_ten_minutes` 1.0→**1.5**（时间档 +0.075/30s）、`enemies.hp_ramp_factor` 0.12→**0.25**、`damage_ramp_factor` 0.08→**0.20**
- 三轮 900s 深局探针（seed 20260729）验证：基线确认 zero-pressure 稳态（27 min 0 死亡、血量长期满、击杀率不降）→ 定稿后 diff 1.38→6.33 @27min 无平台期、HP min 40–69 持续压力、DDA 15–29% 窗口、0 死亡无崩盘、全程 0 `[ANOMALY]`
- `difficulty_test` 进程曲线断言同步（2 杀 ×2.2；65s 两档 +0.15 → 2.35）；全量 41 断言场景 0 FAIL

### 修复（2026-08-04，A 审计 + CI）

- **A 审计稳健性 ×5**：`reset_run` 清 DDA 计时（跨对局降档残留）；`milestone_threshold` pow 溢出钳制 + 空表守卫；`apply_run_save` 里程碑定位迭代上限（异常配置大分数读档防挂死）；`cfg()` Array/Dictionary 返回浅拷贝（防误写污染配置真值）；`SaveManager` 原子写 rename 优先（rename 失败不再丢正本）
- **CI 编译探针修复**：恢复 `autoplay_test.gd` 被误注释的 `_handle_pause_ui`/`_do_menu_return`（适配 StartPanel 退役：重进 main 启动自动读档）；`visual_capture.gd` 补回 `FRAMES_BEFORE_SHOT` 常量

### 修复（2026-08-04，M06 图标字形）

- **新 buff 专属字形 + 分类色**（`scripts/ui_buff_icons.gd`，M06 遗留落地）：`crit_shot`/`shield`/`bullet_speed` 不再走回退圆环——补几何字形（暴击=十字准星+中心点、护盾=圆盾外环+菱形脊、弹速=水平弹头+三条速度线），分类色归位（暴击/弹速→进攻青、护盾→维生绿），16→19 字形全覆盖；HUD 图标格与 Buff 三选一卡片共用

### 玩法（2026-08-04，内容演化，`docs/archive/2026-08-04-content-evolution-plan.md`）

- **新 buff ×3**：暴击 `crit_shot`（12%/层 ×2 伤害，真实命中路径测试）、护盾 `shield`（每层吸收一次全额伤害，`GameState.consume_buff` 层消耗 API）、弹速 `bullet_speed`（+20%/层，声明式 pow 表）
- **新敌机 分裂者**：死亡分裂 2 小机（×0.6 缩放 / HP 半 / 无分数 / 不再分裂）；**新精英 重装炮台**（最高 HP 慢速弹幕机）
- **第 4 号 Boss「月蚀」**：环弹术士——`ring_burst` 全圆环弹 + 中心悬停微摆；狂暴「月蚀」双环反向进动 + 蓄力环阵；轮换扩 4 型（`spawner` `%4`）；架构断言与场景测试全量扩展（`boss_registry_test` 10 攻击/4 机型、`boss_pattern_test` 场景7、`boss_enrage_test` 场景5）

### 玩法（2026-08-04，母舰扩展）

- **母舰火力随里程碑升级**（`docs/archive/2026-08-04-mothership-expansion-plan.md`）：对局里程碑 ≥5 后加特林/导弹伤害 ×1.5、射速 +25%（`mothership.upgrade` 配置段）；驻留状态栏显示「火力升级 ★」提示

### 账户与入口（2026-08-04，本地账户系统）

- **本地用户系统**（`docs/archive/2026-08-04-local-accounts-plan.md`，重启 Phase 3）：`UserDB`（`user://users.json`，PBKDF2-HMAC-SHA256 密码 + 盐、注册/登录/游客/删除、last-login 排序、每用户设置与统计）；删号连带清理该用户存档
- **welcome 主场景**（新 `project.godot` 主场景）：登录面板（用户名/密码/下拉）+ 难度 + 教程 + 设置 + 本地排行榜 overlay + 游客/删除/退出确认模态；ESC 层级 overlay→模态→退出确认；**StartPanel 退役**
- **每用户存档/档案隔离**：存档 `user://savegame_<user>_<hash12>.json`（档主校验，不匹配隔离）；游客不存档、设置仅内存（原版 bug 修复清单 B7 全部落地）；`profile.json` 退役迁移（首个注册用户合并）
- **本地排行榜**（用户维度）：Top10 本地榜（分数降序 + 先到先得），结算页与 welcome overlay 展示
- 新增 3 断言场景（`user_db_test` / `user_session_test` / `welcome_flow_test`）；全量 40 断言场景 0 FAIL（后随母舰升级测试增至 41）

### 工程化（2026-08-02）

- 新增 GitHub Actions **CI**（`.github/workflows/ci.yml`）：无头导入 + 主场景冒烟 + 37 断言场景全量回归，push/PR 触发
- 新增手动触发**发布工作流**（`.github/workflows/release.yml`）：双平台导出打包 → 打 tag → 创建 GitHub Release
- 新增 `CONTRIBUTING.md`（贡献指南）、`SECURITY.md`（安全策略）与 GitHub issue/PR 模板
- `project.godot` 增加 `config/version` 发布版本元数据

### 玩法（2026-08-02）

- **本地高分榜**：结算页本局排名 + 历史 Top5，开始页 Top3（`profile` 持久化）
- **手柄支持**：左摇杆移动 / 右摇杆虚拟准星瞄准 / 动作键（A/RB/LB/X/Y/L3/R3）；设置页「手柄」分区可调右摇杆灵敏度与摇杆死区
- **可读性**：玩家弹白芯描边（敌我弹区分）；致死弹 0.5s 高亮残留（死亡归因）
- **教程可重看**：通关后无存档时教程按钮放行

### 玩法（2026-08-03）

- **战斗公平感四机制**（`docs/archive/2026-08-03-combat-fairness-plan.md`）：受击宽限帧（`player.grace_period`，消灭 ghost hit）、擦弹得分（`player.graze_radius`/`graze_score`，风险-回报技巧轴）、Boss 阶段转场清弹 + 玩家短暂无敌 + 分段血条（`boss.phases.clear_on_shift`/`transition_invincible`/`hud.boss_bar_segments`）、F 键弧光弹反盾（`player.parry.*`，主动防御反击，完整周期 3.8s，手柄 LT）；新增 4 断言场景（grace_period/graze/boss_phase_transition/parry，93 断言）

### 架构与工程（2026-08-03）

- **A3/A4 架构债收尾**：Boss 攻击/移动/狂暴三注册表 + 机型参数表（新增机型/攻击仅需注册一行）、Player buff 声明式效果表 `BUFF_EFFECTS`（新增数值型 buff 只需表加一行）
- **L 系列第十轮全仓库审查**：P1×3/P2×9 修复（池化复用 buff 信号重连回归、截图工具编译错误、autoplay 母舰状态表漂移、判型补全等）
- 新增 2 架构断言场景（`buff_effects_test` 38 断言 / `boss_registry_test` 29 断言）；全量 37 断言场景 0 FAIL

### 玩法（2026-08-03，B 梯队：公平感延续，fair plan §8）

- **Boss 攻击独特 tell**：9 种攻击起手各有独特音效变体 + 视觉前兆冲击环（`boss_attacks.gd ATTACK_TELLS`），玩家可区分「来的是什么」
- **DDA 弹幕密度降档**：玩家受击后 5s 内敌机开火/波次/Boss 攻击间隔拉长（`dda.duration`/`dda.factor`），**只拉间隔不降收益**（分数公平）
- **死亡回放**：环形缓冲录制最近 3s 敌弹轨迹，死亡后幽灵弹幕重放死因（`death_replay.gd`，暂停结算中照常播放）

### 架构与工程（2026-08-03，Phase 0 收尾）

- **test/ 门禁盲区修复**：`test/` 纳入 `gdformat --check` + `gdlint`（23 文件格式化、18 条静态问题修复）；CI 新增编译探针步骤（逐场景 `--quit-after 2` + 错误 grep，捕获 `--import` 不解析未引用场景的编译错误盲区）+ 断言场景单场景超时
- **A8 PlayerVisuals 拆分**（最后一项架构债）：尾焰/残影池/机身色调/受击点/弹反视觉/擦弹闪光迁出 player.gd（`scripts/player_visuals.gd`，组合委托模式）
- **L 系列待办收敛**：L13 母舰在场期事件互斥（精英炮塔/编队不再被母舰自动火力白嫖发奖）、L14 Boss 段切换 y 平滑过渡（消除 1/4 屏瞬移）、L15 测试 profile 最高分快照还原（20 场景）、L16 smoke 弱断言、L18 发布工作流版本号提交落地
- **P2 清理**：`ACTION_LABELS` 死代码删除、`back_pressed` 死信号登记、`profile_corrupt` 损坏档案开始页提示（新增 `START_PROFILE_CORRUPT` 双语键）
- 全量 37 断言场景 0 FAIL；`gdformat`/`gdlint` 全量（含 test/）全绿

## [3.26] - 2026-08-02

### 性能

- 性能优化全量落地：敌机生成统一池化（`USE_POOL` A/B 开关）、`view_world_rect` 物理帧缓存、受击闪白手动衰减、`sin_fast` 查表清扫、渲染合批；`perf_bench` 约 -8~9%

### 玩法

- Boss P2 阶段走位升级：一型/三型 P2 strafe 提速 + 纵向正弦往复、二型 P2 dash 节奏、三型 P1 锚线下区间呼吸（`boss.movement` 配置段）
- 鼠标锁定窗口内设置项（防准星出框失控；暂停/非准星态与失焦放行）

### 修复

- G 系列核心逻辑 32 项处置（spawner 预警取消复位、Boss 逃跑期免伤、教程入口守卫、注册表 O(1) 索引等）
- E 系列存量盲区修复（母舰溅射对 Boss 生效、教程删档守卫、难度表子键校验等）
- A21 测试失败基线根因修复（入场坐标按战斗锚线动态定位）

### 文档

- 全量文档口径统一（状态误记订正、内部矛盾消除、计数与失效哈希修正）
- 已完成工作压缩留档：`docs/archive/EXECUTION_LOG.md` 索引 + 10 份计划/审核文档归档
- 许可证落地：MIT + 第三方声明（Noto Sans SC / OFL）

## [3.25] - 2026-08-02

### 修复

- D 系列全量代码审查修复（入场 Timer/预告线清理、入场中断复位 `abort_entry`、HUD 缓存、硬编码收敛等）
- E 系列批次修复（教程按钮禁用与入口守卫、提前离舰进度条清理、存档原子写等）

## [3.24] - 2026-08-01

### 修复

- C 系列 Godot 规范审计 35 项处置（教程协程泄漏、存档 key_bindings 类型守卫、难度表校验、子弹位移改物理帧等）
- B 系列业务逻辑修复（狂暴瞄准线泄漏、time_scale 复位、Boss 逃跑结算守卫、追踪弹 stale 引用等）

### UI

- 全界面系统化 uplift：统一模态骨架与动效、Buff 卡片与 HUD 仪表簇重设计

---

早期版本（≤ 3.22，2026-07-31 发布工程化起步）的变更记录见 `git log`；移植对齐时期历史见 `docs/archive/PORTING_PARITY.md`。
