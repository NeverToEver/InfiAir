# 2026-08-02 健壮性审核计划（H 系列）

> **性质**：健壮性（鲁棒性）专项审核——区别于既有 A（SOLID）/B（业务）/C（Godot 规范）/D（全量）/G（核心逻辑）各轮，聚焦**崩溃/挂起/状态错乱/数据损坏的真实路径**：空输入、资源加载失败、除零/NaN、节点生命周期、信号重入、幂等、状态机非法转换、池边界、配置无域校验。
> **方法**：按 `docs/AUDIT_REVIEW_SOP.md` 三路并行只读扫描（对局编排+玩家 / 战斗实体+事件 / UI+服务+表现）+ 主控交叉核验（对照既有审计基线去重，G026/C03/E03/G06 等已处理项不重复）。
> **判定原则**：先判 bug / 设计 / 数据依赖防御；配置损坏类统一按「C03/E03/G06 回退契约」口径补判型/钳制。
> **关联**：发现登记 `docs/AUDIT_VAULT.md` H 系列；修复起效记录回填。

---

## 一、发现清单（H 系列，按严重度）

### P1 — 功能性 bug（本次修复优先）

| 编号 | 位置 | 类别 | 描述 | 判定 |
| --- | --- | --- | --- | --- |
| H01 | `player.gd:623` + `game_state.gd:822-823` | 输入边界 | **右摇杆瞄准完全失效**：`Input.get_vector(&"aim_x", &"aim_x", &"aim_y", &"aim_y")` 正负方向传同一动作，`get_vector` 数学上恒为零（`strength(pos) - strength(neg)` 同动作差 0）；装配只有单向 `axis_value=1.0`。base_system_test 只断言动作注册，未测 get_vector 语义，测试全绿掩盖。**P0-1 手柄功能核心缺陷** | **修**（四向独立动作） |
| H02 | `game_state.gd:791-798` | 幂等 | `apply_key_bindings` 用 `action_erase_events` 擦除动作全部事件（**含手柄事件**）后只回填键盘；运行中任何改键/重置键位 → 本会话手柄绑定丢失（重启恢复）。启动时序无碍（apply 先于装配） | **修**（按事件类型过滤擦除） |
| H03 | `game_state.gd:132-144/469-481/1136` | 数值非法 | `_valid_difficulty_defs` 只做类型校验：`milestone ≤ 0` 或 `cycle_mult ≤ 0` 通过 → `milestone_threshold` 恒 0 / 阈值单调性破坏 → continue_run 的 `while` 永不退出**挂死**，或对局内里程碑风暴 | **修**（数值域校验） |

### P2 — 功能性/崩溃（数据依赖或边缘路径）

| 编号 | 位置 | 类别 | 描述 | 判定 |
| --- | --- | --- | --- | --- |
| H04 | `main.gd:438-445` | 资源加载 | BGM 运行时 `ResourceLoader.load` 无 null 判，缺资源即空引用崩溃（其余音频 preload） | **修**（判空回退） |
| H05 | `bullet.gd:243` | 除零/NaN | homing else 分支 `dist == 0` 未判 → `rate=inf` → lerp_angle NaN → 弹坐标 NaN（下一帧自愈但物理异常） | **修**（`dist <= 0` 直行） |
| H06 | `laser_weapon.gd:130,138-139` | 幂等 | `_saved_autofire` 捕获在 `_active=true` 之后为不可达死代码 → `_end_beam` 无条件强开 autofire，破坏入场期/测试关闭的 autofire 状态 | **修**（捕获提前） |
| H07 | `spawner.gd:434/452` + `enemy.gd:139` + `spawner.gd:423` | 空池/越界 | `unlocked_types()` 空池（`unlock_scores` 全正）→ `randi()%0` 崩溃；空弹种池 `randi()%0` 越界 | **修**（空池回退） |
| H08 | `meta_health_fx.gd:367-368` | 判型 | `crack_density` cfg 数组长度/元素无校验 → 越界/float 转换错误刷屏 | **修**（长度+数值校验回退） |
| H09 | `hud.gd:562-573` | tween 语义 | 警告横幅 `set_loops(4)` 使整段循环，首轮末尾 hide 回调即永久隐藏——声称"闪烁 2s"实际只闪 ~0.9s；且无互斥缓存（旧 tween 竞争） | **修**（闪烁循环外置 + kill 互斥，需确认 tween 语义） |

### P3 — 防御性/数据依赖（按回退契约批量处理）

| 编号 | 位置 | 类别 | 描述 | 判定 |
| --- | --- | --- | --- | --- |
| H10 | `bullet.gd:257` / `boss_attacks.gd:316` | 零向量 | 零方向弹无回退（enemy/BossFire 已有 G026 口径，此处漏）→ 静止弹永驻 | **修**（`setup` 统一回退 DOWN） |
| H11 | `boss.gd:271,438-447` | 判型 | `hp_mults` 短数组 → Boss HP=0 免疫伤害静默；`STRAFE_SPEEDS` 短数组越界；`fire_intervals` 非数组 `.duplicate()` 崩溃 | **修**（C18/G06 判型模式） |
| H12 | `enrage_sequence.gd:311-312` | 除零 | `ENRAGE_SQUARE_PATH_RATIO=0` → inf 轨道 NaN | **修**（钳制 (0,1]） |
| H13 | `elite_turret_event.gd:101-102` / `mothership_summon_window.gd:50-51` | 判型 | `fire_interval`/`shot_durations` 非数组/短数组崩溃 | **修**（G06 判型） |
| H14 | `mothership.gd:368/683` | 悬挂引用 | `_warp_gate` 调用无 `is_instance_valid` 守卫（同文件其余引用均有） | **修**（判空） |
| H15 | `scheduled_event_trigger.gd:19-24` / `camera_shake.gd:25` / `hud.gd:93,98` / `meta_health_fx.gd:279-334` / `main.gd:587` / `game_state.gd:347,701` | 配置无域 | `interval/decay/POLL_INTERVAL/pulse_period/tau/duration/dock_charge_time/time_step_seconds ≤ 0` → 除零/节流失效/永不衰减 | **修**（读取后 clamp 下限） |
| H16 | `game_state.gd:100` | 配置无域 | `world_scale=0/负` 无校验 → 机体归零/镜像 | **修**（>0 钳制） |
| H17 | `exit_confirm.gd:127-130` | 协程 | `await tween.finished` 违反 AGENTS 协程纪律（场景卸载时泄漏） | **修**（tween_callback） |
| H18 | `game_state.gd:1110-1118` | 状态恢复 | missions 恢复丢 `goal` 键 → `mission_completed` 恢复后永久哑火（潜伏，当前信号无消费者） | **修**（保留 goal） |
| H19 | `enemy.gd:220-221` | 判型 | `hover_band` 读入未判型/判长（spawner 已有 G06，此处漏） | **修**（对齐 G06） |
| H20 | `buff_select.gd:274` / `tutorial.gd:294` / `ui_theme.gd:288` / `base_console.gd:352,421` / `settings_ui.gd:443` / `cinematic_fx.gd:182` / `return_cinematic.gd:1196` | 生命周期/边界 | 一组防御缺口：closing tween 释放软锁（不可达）、教程失败态阶段推进、按钮 tween 竞争、`ROUTE_BUFF_NAMES` 越界、负治疗、`_pages` 空、点列<2、镜头时长 0 除零 | **修**（逐项守卫） |

---

## 二、修复批次

1. **批次 1（P1×3）**：H01 右摇杆四向动作 + H02 改键过滤擦除 + H03 milestone 域校验；补测试（四向动作装配断言、改键保留手柄事件断言）。
2. **批次 2（P2×6）**：H04-H09（BGM 判空 / homing 除零 / autofire 捕获 / 空池回退 / crack_density 校验 / hud 警告 tween 修正）。
3. **批次 3（P3×11 组）**：H10-H20 数据防御与生命周期守卫批量。

> 修复后全量回归：31 断言场景 0 FAIL；AUDIT_VAULT H 系列回填「修复起效记录」；若发现需设计确认项回填判定。
