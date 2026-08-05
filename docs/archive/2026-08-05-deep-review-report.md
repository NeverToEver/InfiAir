# InfiAir 全仓库深度 Review 报告(2026-08-05)

> 依据用户指示「goal 模式,对仓库进行深度 review,不设 token 限制」执行;按 `docs/AUDIT_REVIEW_SOP.md` 流程(并行审计 → 分类 → 登记;本批只审计不修复,修复待用户指示)。发现登记:金库 Q 系列。
> 方法:7 路并行只读审计(对局编排/战斗系统/服务层/事件系统/UI 导航文本/测试 CI 工具链/平衡数值内容),每路对照「设计文档 × 代码 × git 历史」三角验证;主控对全部 P1/P2 与部分 P3 亲自复核代码证据。

## 1. 审计范围与基线

- 范围:`scripts/`(75 文件 24218 行)+ `autoload/` + `test/`(54 文件)+ 11 场景 + CI/工具链/文档。
- 基线(本批验证):`--headless --import` 0 error;`smoke_test` PASS exit=0(143 断言)。git 工作树干净(HEAD `d6a1951`)。
- 排除:金库已登记已修项(A/B/C/D/E/F/G/H/I/J/K/L/M/P 系列)、登记遗留(M07-M10/L17/L 系列 P3 类别清单)、合理模式(C17/C19)。

## 2. 发现总览

| 严重度 | 数量 | 摘要 |
| --- | --- | --- |
| P1 严重 | 1 | ring_burst 弹数按增量语义消费绝对值,实际 2× 设计密度 |
| P2 中等 | 9 | 4 型扩容守卫遗漏 ×2、regen 缓存、TaskPool 不足额、统计缺失、fog 总开关失效、BALANCE_MAP 过期、welcome Esc 断链、遭遇计时器跨局继承 |
| P3 轻微 | 20 | 判型/兜底/信号契约/测试质量/CI 语义等 |
| P4 观察 | 20+ | 注释失实、硬编码、性能观察、文档计数(按类别合并) |

## 3. P1 发现

### Q01 ring_burst 难度弹数按增量语义消费绝对值 —— 弹幕密度约 2× 设计值

- **位置**:`scripts/boss.gd:668` × `scripts/boss_attacks.gd:163-173` × `data/balance.json:367-371` × `docs/BOSS_REDESIGN.md:108`
- **类别**:纯 bug(正常对局路径)+ 文档-代码矛盾
- **证据链**(两路独立审计 + 主控复核):
  - `balance.json` `boss.difficulty_scaling.counts` 全部 10 个键中 9 个是增量格式(`fan/homing/cannon/volley` = `[-1,0,1]`、`wall/ring` = `[-2,0,2]`),**唯独 `ring_burst` = `[10, 12, 14]` 是绝对值**。
  - `boss.gd:668` `_attacks.ring_delta = _count_delta("ring_burst", tier)` 直接取档值;`boss_attacks.gd:168` `maxi(6, int(boss.RING_BURST_COUNT) + ring_delta)` 在基准 `RING_BURST_COUNT = 12`(json `boss.ring_burst.count` = 12)上相加。
  - 实际弹数:easy **22** / medium **24** / hard **26** 发;设计(BOSS_REDESIGN §5.6)"Difficulty tier: `counts.ring_burst` [10, 12, 14]" 为每档弹数绝对值。
  - 每局第 4 型 Boss P1/P2 均打 ring_burst(各 3 waves),**正常路径每局必现**,easy 档密度翻倍(10→22),medium/hard 超标 ~100%。
  - 测试盲点:`boss_pattern_test.gd:489` 断言 `>= 12`(medium 档实际 24 发也通过),文案「12 向」与实现不符。
- **修复建议**(需设计拍板方向):① 消费侧改绝对值语义 `maxi(6, ring_delta)`(推荐,与 §5.6 一致);② 或 json 改增量 `[-2, 0, 2]` 与同表其他键统一。补 easy/hard 弹数断言锁死。

## 4. P2 发现

| 编号 | 位置 | 类别 | 描述(证据) | 修复建议 |
| --- | --- | --- | --- | --- |
| Q02 | `boss.gd:317-324` | 纯 bug(配置损坏路径)/应急补丁痕迹 | **hp_mults 校验与回退数组未随 4 型扩容**:`size() >= 3` 校验与回退 `[1.3, 0.7, 1.6]` 均 3 元素;balance.json 缺键/截断 3 元素时校验通过 → `hp_mults[3]` 越界 → `float(null)=0.0` → `max_hp=0` → `take_damage` 首行 `hp <= 0.0` 早退 → **type4 Boss 出生即免疫伤害**(仅 50s 逃跑兜底),HUD 血条 0/0→NaN。H11 防线被 3 元素回退绕过;M01(2a9c9d5)只修了 `setup()` 的 clampi | 校验与回退数组均扩至 4 元素(`[1.3, 0.7, 1.6, 1.2]`),补「3 元素 + type4」组合断言 |
| Q03 | `boss.gd:605` | 纯 bug(配置损坏路径)/应急补丁痕迹 | **_load_patterns 的 `clampi(boss_type, 1, 3)` 未放开**:DEFAULT_PATTERNS 已有键 4(:79-92)但被钳为 3 → 键 4 表死数据;`boss.phases.type4` 缺失/损坏时月蚀静默回退**三型(母舰)模式表**(cross/召唤/弹幕墙),违背「脚本回退镜像 json」约定 | 改 `clampi(boss_type, 1, 4)`;补 type4 回退断言 |
| Q04 | `game_state.gd:159,598,675-679` × `:1697-1699` | 纯 bug | **regen 缓存重登录不刷新**:`_refresh_regen_cache()` 仅 `_apply_balance`(启动时 difficulty=默认 medium)与 `set_difficulty`(设置页切换)调用;`_apply_settings_dict` 恢复存档 difficulty 后不刷新 → hard 玩家重启后被动回血按 medium(2.0/4.0)而非 hard(0.67/5.0),easy 玩家回血减半。常规重启即中(E13 修复只覆盖两条路径) | `_apply_settings_dict` 设置 difficulty 后补调用;补「profile 恢复 hard 后 rate==0.67」断言 |
| Q05 | `task_pool.gd:30-34` | 纯 bug(经济失真) | **TaskPool 批次耗尽不足额刷新**:`if not out.is_empty(): break` 不跨批补足;排除在场任务使批次提前"耗尽"(全池可用 6 恒 ≥ 需求 3)。Python 逐行模拟:2000 局中 99.3% 出现 ≥1 次不足额(1-2/3 槽),REFRESH_COST 照扣 | 批次耗尽且全池可用候选 ≥ 剩余需求时 `_refill()` 继续;补「多次刷新槽位恒 = MISSION_SLOTS」断言 |
| Q06 | `user_db.gd:119-120` | 设计目标未达 | **game_over_stats 死亡统计缺失**:`total_kills`/`games_played` 全仓仅字段初始化,无写入点(账户计划 Task 2 承诺 `game_over_stats(kills)` 上报);`game_over_ui.gd:75` 只 `record_score()` | 按计划补 GameState 转发 + 结算处调用(游客跳过) |
| Q07 | `event_manager.gd:306-316` × `:198-208` | 纯 bug | **`fog_events.enabled` 总开关在真实对局自动触发路径失效**:`_process` fog 分支只查 `_run_active`/first_delay/cooldown,不查 `FOG_ENABLED`(仅 `can_trigger_group` 检查,生产无人调用);json `enabled=false` 后迷雾照常触发。迁移前旧版同缺陷,金库未登记 | fog 分支头部补 `if not FOG_ENABLED: return` + enabled=false 测试 |
| Q08 | `docs/BALANCE_MAP.md`(生成物) | 文档-代码矛盾 | **BALANCE_MAP.md 未随 2026-08-05 事件管理器重构重跑**:重跑 `gen_balance_map.py` 后 diff = **+228/-217 行**:缺 `event_manager.gd` 区块、残留已迁走的 `fog_event_manager.gd`/spawner 触发器区块、全表行号漂移 | 重跑生成器并提交(本批已恢复原文件) |
| Q09 | `welcome.gd:514-530` × `settings_ui.gd:186-187` × `welcome.tscn:9-11` | 纯 bug(导航断链) | **welcome 设置页打开时 Esc 无法关闭设置页**:`_unhandled_input` 的 ui_cancel 分支无 settings 可见性检查(welcome.tscn 无 BackNavigator,main.tscn:18 才有);设置页非捕获态不消费 ui_cancel → Esc 落到 welcome → 打开**隐藏层中的 `_exit_confirm`**(welcome 已 `visible=false`,exit_confirm 不可见但被 grab_focus)→ 与 EXIT_FLOW「settings back = Esc」承诺矛盾,键盘/手柄玩家 Esc 永远关不掉设置(只能鼠标点返回)。**附注**:审计初期「设置黑屏」推断不成立——Godot 官方文档明确 CanvasLayer.visible 不传播到子 CanvasLayer,设置页渲染正常 | welcome 的 ui_cancel 分支最前检查 settings_ui 可见则调 `settings.back()`;补「设置打开时 Esc」用例 |
| Q10 | `event_manager.gd:149-155` × `main.gd:97-100` | 纯 bug | **遭遇事件触发计时器跨对局继承**:`_encounter_timers` 挂 autoload,`register_encounter` 仅在键缺失时初始化,`set_run_active(true)` 不重置;死亡重开/重进 main 继承上局剩余值(可 ≤0)→ 新局开局即掷签触发精英炮塔/编队(仅受 min_score 门控)。旧 ScheduledEventTrigger 为 spawner 成员每局归零,迁移改变语义未声明 | `set_run_active(true)` 时重置 `_encounter_timers`(与 fog 组 first_delay 复位同构) |

## 5. P3 发现

| 编号 | 位置 | 类别 | 描述 |
| --- | --- | --- | --- |
| Q11 | `welcome.gd:210-217` | 纯 bug | `_show_msg` 消息互踩:旧 SceneTreeTimer `time_left=0` 不能取消,回调无条件清空文本 → 2s 内连发消息被下一帧清掉 |
| Q12 | `event_manager.gd:132-144` | 设计目标未达 | fog `first_delay` 开局保护「每进程一次」而非「每局一次」(`activate_fog` 仅 wire 调一次);同进程第二局开局 ~3s 即可触发迷雾,与 FOG_EVENTS §2.2 每局语义不符 |
| Q13 | `event_manager.gd:248-260,378-387` | 信号契约 | 遭遇事件 abort 路径 `event_ended` 可能双发且发在事件仍活跃时(FSM 未回 IDLE,轮询重新登记);当前无消费者 |
| Q14 | `formation_strike_event.gd:68` | 约定违反 | `CRAFT_COUNTS` 直赋无判型(K14 只修了精英侧),配置损坏为非 Dictionary 时 `:150` 崩溃 |
| Q15 | `formation_strike_event.gd:192-194` | 防御缺口 | 编队事件无超时兜底(精英有 30s 倒计时):`approach_speed ≤ 0`(无 clamp)时永驻 FORMATION_ENTER + `_waves_paused` 常驻 → 普通波次与 Boss 调度全冻结 |
| Q16 | `elite_turret_event.gd:194-201` | 边界 | `turret_counts` 无上限钳制,>5 时 `SOCKETS[i]` 越界崩溃(StrikeCarrier.SOCKETS 固定 5 槽) |
| Q17 | `user_db.gd:110,129-137,232` | 防御缺口 | users.json 结构守卫薄弱:`_users` 非 Dictionary/条目非 Dictionary 时 `users.has()`/`rec.get` 直接报错,与 GameState 层元素级守卫口径不一致 |
| Q18 | `user_db.gd:83-87` | 边界 | `_hex_decode` 奇数长度 hex 越界 + `-1` append 到 PackedByteArray(手改 salt/password 触发) |
| Q19 | `enemy.gd:453` × `:412` | 信号契约 | 池化 reparent 无条件 `unbind_enemy` 误发 `entity_unregistered`,而 `reactivate` 只 `register_enemy` 不发信号——信号流不对称,与 ENTITY_MANAGER §4.2「池化路径不受影响」矛盾(当前无消费者,埋雷) |
| Q20 | `user_db.gd:292-300` × `welcome.gd:481-491` | 防御缺口 | 排行榜渲染无判型:手改 users.json 非 Dictionary 条目 → sort 类型错误崩;字符串 score 静默转 0 |
| Q21 | `welcome.gd:474-497` | 焦点 | 排行榜 overlay 打开无 grab_focus(welcome 唯一不聚焦的模态),键盘焦点停留被遮挡按钮,Enter 重复打开 |
| Q22 | `hud.gd:852-856` | 设计目标未达 | buff 滚动明细栏 ScrollContainer 未设 `size_flags_vertical = SIZE_EXPAND_FILL` → 内容超出不滚动、面板被撑大(buff ≥15 种即触发,19 种全解锁可达) |
| Q23 | `startup_flow_test.gd:48-52,174-175` / `welcome_flow_test.gd:35-36,145` / `user_session_test.gd:59-60,171` | 测试质量 | 3 个账户批次测试 `_wipe_user_files()` 删除 profile.json/users.json/全部存档且**不还原**——本地跑测试永久销毁开发者账户表(L15 快照范式未推广,同批其余 21 个测试已还原) |
| Q24 | `welcome_flow_test.gd:26-31,140` | 测试质量 | 直调 `_welcome._unhandled_input(ev)` 绕过输入管线——C30 已修复模式在同一批次新测试回归(esc_navigation_test 已用 `Input.parse_input_event`) |
| Q25 | `user_session_test.gd:54,88-89,97` | 测试质量 | 直读写私有 `_pending_legacy_profile`/直调 `_maybe_migrate_legacy_profile()`(A7 残留,无公开 API) |
| Q26 | `.github/workflows/ci.yml:67-77` | CI | 编译探针非零退出处理是死代码:GH Actions `bash -e` 下任一场景非零直接中止步骤,`::error::`/日志上传全不执行;本地(无 `-e`)与 CI 语义相反(124=挂起:本地放行/CI 失败) |
| Q27 | `boss_movement.gd:95-99` | 设计目标未达 | 月蚀中心微摆振幅被 `move_toward` 速度上限压缩 ~一半(正弦峰值 78.5px/s > MOVE4_SPEED 40px/s → 实际 ±15px 而非 ±30px,波形低通失真) |
| Q28 | `boss.gd:177-186,683` | 回退一致性 | `DIFF_COUNT_DELTAS` 脚本回退表缺 `ring_burst` 键(json 缺键时三档恒 12 不分档)——与 Q01 同批,随修 |
| Q29 | `enemy_move_strategy.gd:88-229` | 平衡/约定 | 移动策略参数部分入库部分硬编码:sine 振幅 90/频率 3.0、zigzag 0.7/0.9/0.15、dive 1.7/1.2、noise 谐波、下移 0.9 均无 json 键;平衡调整无法全经 balance.json 表达 |
| Q30 | `boss_attacks.gd:239` | 平衡/约定 | sniper3 三连发间隔 0.12s 硬编码(§8 设计数值),同族 `charged_cannon.interval` 有 json 键——入库不一致 |

## 6. P4 观察项(按类别合并)

- **注释失实/文档口径**:`main.gd:43-44` B2 已修复仍宣称「终态路径不复位 time_scale」;`balance_service.gd:47-52` cfg() 注释「隔离可变性」实为单层浅拷贝(嵌套仍共享);`TESTING.md:111` 场景总数 53 vs 实际 54;`autoplay_test.gd:70` BUFF_POOL_SIZE 16 vs 实际 19;`comm_overlay.gd:97` 台词 ~5.5s vs 文档「3.5s then fade」;`main.tscn` 7 处中文初始文本(运行时被 hud 覆盖,约定盲区);`game_state.gd MISSION_DEFS` 内嵌中文 name/desc 双源(显示全走 tr(),字段无消费)。
- **硬编码坐标**:`boss.gd:869-873` strafe_range 含 `1920.0 - STRAFE_MAX_X`(C14 同族残留);`base_console.gd:121,125` 慢扫描带 1920×1080(纯装饰)。
- **性能观察**:`orbital_strike.gd:219-224` 命中段每帧重算 96 次常数 cos/sin(未走查表);`welcome.gd:138-183` 用户名下拉每次击键重建且不按输入过滤;`game_state.gd:872-877` set_joy_deadzone 拖动全量遍历 17 动作;`sfx_player.gd` 密集爆炸覆盖重播截断(设计权衡);`welcome.gd` 排行榜非登录路径。
- **边界/防御(手改触发)**:`user_db` hex 解码;`game_state.gd:1599` missions progress 无 ≥0 钳制;`game_state.gd:189-214` difficulty score 无上限(1e308 溢出,同 A 审计 milestone 族);`game_state.gd:1462-1463` delete_user 后 current_user 残留(无幽灵存档路径,已逐路径验证);`event_manager.gd:398-406` 全零权重退化为恒选首个;`event_manager.gd:434-435` ConfusionEvent 缺键降级与 event_started 信号不同步;`welcome.gd:281-288` 删除用户后 current_user 残留。
- **工具链/CI**:`gen_balance_map.py:69` 裸异常无友好报错;`release.yml:56-69,93-94` 重复触发同版本必失败 + 同步提交只上 tag 不上 main(主分支 config/version 永久滞后);`event_manager_test.gd:96` 直写生产配置表且不还原(A7 同族);`smoke_test.gd:428-429` 母舰击杀 `== +33` 精确相等断言(已知 flake 基线);`entity_manager_test.gd:74-80` 基准断言顺序弱化。
- **reload_balance 联动**:`game_state.gd:121-123` 重载不刷事件管理器配置(诊断路径脱节)。

## 7. 待验证可疑点(证据不足,未定性)

1. **Q09 焦点细节**:Godot 4.6 对不可见 Control `grab_focus()` 的实际行为(成功→焦点被隐藏按钮抢占,手柄方向键死锁;失败→焦点保留设置页)——影响 Q09 是否从「导航断链」升级为「手柄死锁」,建议 windowed 实机复核或补行为断言。
2. **遭遇自动触发对长跑测试的暴露面**:管理器遭遇门控 = `_spawner.is_processing() and can_process()`(不依赖 `_run_active`),实例化 main.tscn 且未 `set_process(false)`、真实运行 ≥40s 且 score ≥ 阈值的测试可被随机触发(破坏断言确定性);现有测试均靠纪律停 spawner,无契约断言。
3. **Q27 MOVE4 微摆**:±15px 实际振幅的视觉可接受度需 windowed 目检;若 40px/s 趋近是有意设计,§5.6 需注明实际振幅。
4. **smoke `== +33` 精确相等断言**:物理帧结算 + 精确相等形态,慢 runner 理论可 flake(TESTING.md 已登记 flake 自愈基线,本轮未复现)。
5. **`player.gd:1007` `6.0 * world_scale` 与 `bullet.gd:230` 弹碰撞半径 6.0 镜像字面量**:双改风险,未定级。

## 8. 分类判定(按 AUDIT_REVIEW_SOP Phase 2)

- **真 bug(修复,无需设计拍板)**:Q02/Q03/Q04/Q05/Q07/Q09/Q10/Q11/Q13/Q14/Q16/Q17/Q18/Q19/Q20/Q22/Q23/Q24/Q25/Q26 及 P4 各项。
- **需设计拍板**:Q01(绝对值 vs 增量,两条修复方向;但「消费与文档矛盾」本身是确定的);Q27(压缩振幅是否可接受);Q15/Q30/Q29(入库 vs 登记为有意保留)。
- **设计目标未达(计划未完成)**:Q06(账户计划 Task 2)、Q12(FOG_EVENTS §2.2 每局语义)。
- **文档-代码矛盾(重跑/同步生成物)**:Q08(BALANCE_MAP)、P4 注释口径项。
- 本批无「设计确认不改码」项。

## 9. 修复优先级建议

1. **随下一个内容/平衡批次**:Q01(正常路径密度翻倍)+ Q28(同批)+ Q02/Q03(4 型守卫,一处根因:M01 扩容只改 setup 钳制)。
2. **随下个 bug 修复批次**:Q04(常规玩家重启即中)、Q05(99.3% 概率经济失真)、Q07(总开关失效)、Q09(Esc 断链)、Q10(跨局继承)。
3. **随下个测试批次**:Q23(破坏性测试,先于任何本地全量回归)、Q24/Q25(约定回退)。
4. **低风险即修**:Q08(重跑生成器)、Q21、Q22、Q26、Q11。
5. **登记观察**:Q06(计划项)、Q29/Q30(平衡入库决策)、P4 各项。

## 10. 总体评价

仓库整体质量高,与金库 A-M/P 系列记录高度自洽:组合委托拆分、热路径注册表化、查表三角函数全覆盖、池化对称性、配置防御纵深(判型/钳制/回退)均为系统性沉淀;git 历史与金库逐条对应。本批 50 项发现集中在三类结构性缺口:①**2026-08-04 内容演化(4 型扩容)的伴生遗漏**(Q01-Q03/Q28 同根因);②**2026-08-05 统一事件管理器/实体管理器迁移的语义残留**(Q07/Q10/Q12/Q13/Q19,迁移忠实但每局/每进程语义与信号契约未对齐);③**2026-08-04 账户批次新测试的约定回退**(Q23-Q25)。无 P0。
