# 2026-08-02 E 系列修复计划（存量盲区审查登记项落地）

> 审查基线：HEAD `11d6198`（2026-08-02，D 系列全量修复后）。
> 范围：`docs/AUDIT_VAULT.md` 第四轮审核之 **E 系列**（2026-08-02 存量盲区补充审查，登记 15 项只登记未修复；发现-判定-修复追踪单一事实源见本文档，AUDIT_VAULT 同步回填）。
> 验证基线（修复前）：`--headless --import` ✅、`--quit-after 300` ✅、30 个断言场景 0 FAIL（D 系列回归结论）。
> 判定原则：E 系列登记时已给出判定建议（见 `docs/2026-08-02-audit-fix-plan.md` 第四节）；本文档把「修 / 待判定 / 不修」逐项落实为最终处置并执行。
> 关联：修复起效记录回填 `docs/AUDIT_VAULT.md`（E 系列）；数值键无变更（本批不改 balance.json 键），无需重跑 `gen_balance_map.py`。

---

## 一、E 系列处置总览

| 编号 | 严重度 | 类别 | 登记判定建议 | 本次处置 | 位置 |
| --- | --- | --- | --- | --- | --- |
| E01 | P1 | 纯bug（C20 静默回归） | 修 | ✅ 修 | `scripts/bullet.gd:240-246` |
| E02 | P2 | 纯bug（玩家有损） | 修（最优先） | ✅ 修 | `scripts/start_panel.gd:275` / `scripts/tutorial.gd:97` |
| E03 | P2 | 设计目标未达（C03 半堵） | 修 | ✅ 修 | `autoload/game_state.gd:118-125` |
| E04 | P2 | 设计目标未达/一致性 | 修 | ✅ 修 | `scripts/dawn_station.gd:282-286` / `return_cinematic.gd:568,702` |
| E05 | P2 | 纯bug（边缘） | 修 | ✅ 修 | `scripts/mothership.gd:414-432` |
| E06 | P2 | 一致性（D10 未收敛） | 修（一行） | ✅ 修 | `scripts/enemy.gd:469` |
| E07 | P3 | 文档-代码矛盾 | 修（随 E01） | ✅ 修 | `scripts/bullet.gd:230` |
| E08 | P3 | 纯bug（不可达） | 待判定（顺手兜底） | ✅ 修 | `scripts/laser_weapon.gd:66-67` |
| E09 | P3 | 一致性（待判定） | 待判定 | 🟦 登记不修 | `scripts/laser_weapon.gd:13` |
| E10 | P3 | 一致性（低危） | 待判定 | ✅ 修 | `autoload/game_state.gd:947` |
| E11 | P3 | 一致性（C02 元素级缺口） | 待判定 | ✅ 修 | `autoload/game_state.gd:958-959` |
| E12 | P3 | 一致性（存量行为） | 待判定（可改临时文件+rename） | ✅ 修 | `scripts/save_manager.gd:21-28` |
| E13 | P3 | 热路径约定边缘 | 待判定 | 🟦 登记不修 | `scripts/player_damage.gd:64-69` |
| E14 | P3 | 一致性 | 不修（注明安全） | 🟦 登记不修 | `scripts/mothership.gd:171-174` |
| E15 | P3 | 性能轻微 | 不修（登记备查） | 🟦 登记不修 | `scripts/enemy.gd:385` |

**登记不修的判定理由（写入 AUDIT_VAULT）：**
- **E09**：`BEAM_HALF_WIDTH` 乘 ws（0.4）后判定半径 26→10.4px，激光命中显著削弱，属游戏性变更需产品判断；现视觉可接受。登记备查。
- **E13**：缓存被动回血参数会与「难度可中途切换」（`set_difficulty`）语义冲突——切换后缓存过期需信号刷新链路，超低风险修复范围。登记备查。
- **E14**：`beam_pts[i] *= ws` 当前安全（polygon 为节点内联属性非共享 sub_resource），登记注明。
- **E15**：`buff_count(&"slow_field")` 每帧字典 get 无分配，开销极小。登记备查。

---

## 二、分批修复计划（每批独立提交）

### 批次 1：对局结算与配置健壮性（E01+E07 / E03 / E10 / E11）

#### E01（P1）+ E07（P3）——`scripts/bullet.gd` 母舰溅射 Boss 失效 + 错误注释

**问题**：`_splash()` 用 `node as Enemy` 访问注册表（注册表含 Boss，Boss `extends Area2D` 非 `Enemy` 子类），对 Boss cast 得 null 被跳过，溅射伤害静默丢失（直击 80 仍有效）。`_explode()` 的 Boss 排除为有意设计不可连带；E07 是其注释「注册表全为 Enemy」前提错误。
**改法**：
1. `_splash()`（240-246）：`var e := node as Enemy` 改 Variant 鸭子调用——与 `laser_weapon.gd:154` `_damage_tick`（注释「含 Boss」）同一模式；Enemy/Boss 均实现 `take_damage(amount, score_scale)`。
2. `_explode()`（228-236）：行为不变（`as Enemy` 对 Boss 得 null 恰落在 `e == null` 跳过 = 有意 Boss 排除），修正 230 行注释说明真实语义。
3. 新注释标注 E01/E07 修复说明。
**验证**：`hit_logic_test` / `mothership_summon_test` / `smoke_test`（批次 4 补 Boss 溅射断言）。

#### E03（P2）——`autoload/game_state.gd` 难度表子键校验

**问题**：`_valid_difficulty_defs`（118-125）只校验 easy/medium/hard 是 Dictionary；部分损坏（缺子键）通过后 8 处 `DIFFICULTY_DEFS[difficulty][...]` 访问 KeyError→0（敌方 0 HP 秒死、得分倍率 0），违背「损坏回退默认」宣称。
**改法**：新增常量 `DIFFICULTY_DEF_KEYS`（8 个数值键：hp/speed/spawn/score/spread_cap/milestone/regen_delay/regen_rate），`_valid_difficulty_defs` 对每个难度子字典校验全部数值键存在且为 int/float。label 键已由 D04 改走 `tr()` 不再消费，不纳入校验。balance.json 三档键齐全（已核验），正常路径不受影响。
**验证**：`balance_test`（批次 4 补「缺子键回退默认」用例）。

#### E10（P3）——`autoload/game_state.gd:947` locale 加载守卫

**问题**：`locale = str(parsed.get("locale", "zh"))` 绕过 `set_locale()` 的 zh/en 守卫；手改 "fr" 时 `locale` 变量与 TranslationServer 状态（启动默认 zh）不一致。
**改法**：加 zh/en 白名单守卫，非法值保持默认 zh（不调 `set_locale`，避免 load 阶段触发 `save_profile`/`locale_changed` 副作用）。
**验证**：`startup_flow_test` / `base_system_test`。

#### E11（P3）——`autoload/game_state.gd:958-959` key_bindings 元素判型

**问题**：外层已守卫 Dictionary/Array（C02），数组元素 `int(k)` 未判型；手改字符串 keycode 触发转换错误刷屏（不崩溃）。
**改法**：元素循环加 `k is int or k is float` 守卫，非法元素跳过。
**验证**：`startup_flow_test` / `base_system_test`。

### 批次 2：流程与 UI（E02 / E05 / E06）

#### E02（P2，最优先）——`scripts/start_panel.gd` 教程按钮通关后禁入

**问题**：教程按钮通关后未禁用；`_on_tutorial_pressed()` 无守卫直接 `change_scene`；`tutorial.gd:97` 无条件 `delete_save()`。对局中存档 → 点「教程 ✓」→ 进行中存档被静默删除。
**改法**：
1. `_refresh_texts()`（240 行处）：`_tutorial_button.disabled = GameState.tutorial_done`。
2. `_on_tutorial_pressed()`（275-277）：加 `if GameState.tutorial_done: return` 守卫（防键盘/程序化调用绕 UI）。
3. `tutorial.gd:97` 的 `delete_save()` 为教程隔离契约（「教程不读写 savegame」），保留。
**验证**：`tutorial_test` / `startup_flow_test`（批次 4 补「通关后按钮禁用」断言）。

#### E05（P2）——`scripts/mothership.gd` 强制离舰清提前离舰进度条

**问题**：H 按住时被强制离舰（警告到期/弹匣耗尽走 `start_release()`），`set_early_leave_charge(-1.0)` 未调（仅 `_early_depart()` 调），进度条残留可见。
**改法**：`start_release()`（640）开头统一清 HUD 提前离舰进度条（`get_tree().get_first_node_in_group("hud")` + `set_early_leave_charge(-1.0)`）。`_early_depart()` 已有清理，不受影响。
**验证**：`mothership_summon_test`（批次 4 补「start_release 清进度条」断言）。

#### E06（P2，一行）——`scripts/enemy.gd:469` 侧方离场 960 硬编码

**问题**：`position.x < 960.0` 硬编码（相机固定 (960,540) 时等价，滚动即错），D10 已收敛同类未收敛此一处。
**改法**：改 `GameState.view_world_rect().get_center().x`（寿命到期一次性调用，无热路径问题）。
**验证**：`enemy_combat_test` / `wave_pacing_test` / `smoke_test`（行为等价，回归验证）。

### 批次 3：表现与持久化（E04 / E08 / E12）

#### E04（P2）——`scripts/dawn_station.gd` PHANTOM 呼吸不覆盖调用方 alpha

**问题**：PHANTOM 工厂 `breathe` tween 写 `station.modulate:a`（0.85↔1.0），覆盖调用方压的站体 alpha（`return_cinematic.gd:568` 0.35、`:702` 0.5 被抬到 0.85-1.0，约 2.5~3 倍）；base_console 用包装节点规避，同工厂两种用法不一致。
**改法**：`_build_phantom`（254）新增呼吸容器 `BreatheRoot`（Node2D），全部视觉（`inner`/PhantomBody）挂其下；4s 慢呼吸 tween（282-286）目标改为容器 `modulate:a`，`station.modulate` 归调用方所有。glitch（288-291，操作 inner）与舱段/网格闪烁（挂 station 的 tween，目标子节点）不受影响；DESTROYED 模式无呼吸不受影响。253 行注释同步更新。
**验证**：`return_cinematic_test` / `intro_cinematic_test` / `base_system_test`（base_console PHANTOM 背景）。

#### E08（P3，兜底）——`scripts/laser_weapon.gd:66-67` buff 归零收束激活光束

**问题**：buff 计数归零早退（`return`）冻结激活态光束——`_end_beam()` 不执行、autofire 卡禁；当前无 buff 移除机制不可达，未来引入即触发。
**改法**：早退前 `if _active: _end_beam()`。
**验证**：`buff33_test` / `smoke_test`。

#### E12（P3）——`scripts/save_manager.gd:21-28` 原子写

**问题**：`save()` 直接 WRITE 覆盖非原子写；写入中途崩溃产生截断 JSON，下次 load 隔离 .corrupt（自愈但丢进度）。
**改法**：先写 `path + ".tmp"` 临时文件，再删旧正本 + `DirAccess.rename_absolute` 替换；失败 push_warning 返回 false（对齐原行为）。最坏情况（删旧后 rename 前崩溃）旧文件丢失但不会产生截断 JSON 被误读（load 返回 {} 无存档，不置 corrupt），优于现状。
**验证**：`base_system_test` / `startup_flow_test`（存档/RP/任务/档案持久化全路径）。

### 批次 4：测试补断言（随各批次代码提交，不单独提交）

| 用例 | 位置 | 断言 |
| --- | --- | --- |
| E01 Boss 溅射 | `test/mothership_summon_test.gd` | 母舰导弹溅射对 Boss 生效（补 Boss 靶机或复用 hit_logic 方式） |
| E03 缺子键回退 | `test/balance_test.gd` | 难度段子键缺失 → `enemy_hp_multiplier` 回退默认（easy hp 0.75） |
| E02 按钮禁用 | `test/startup_flow_test.gd`（或 tutorial_test） | tutorial_done 后教程按钮 disabled、`_on_tutorial_pressed` 不切场景 |
| E05 进度条清除 | `test/mothership_summon_test.gd` | `start_release()` 后 HUD early_leave 条隐藏 |

### 批次 5：全量回归 + 文档回填

1. `godot --headless --import --path .`、`godot --headless --path . --quit-after 300`、全量 30 断言场景 0 FAIL。
2. `docs/AUDIT_VAULT.md` E 系列回填「修复起效记录」（改了什么/为什么起效/如何验证）并更新第四轮结论；本文档第三节状态表回填。
3. 提交 + `git push`（用户要求：阶段性提交、最后一起推送）。

---

## 三、修复状态追踪（执行中回填）

| 编号 | 状态 | 修复说明 | 起效验证 |
| --- | --- | --- | --- |
| E01 | ✅ 已修复 | `bullet.gd _splash()` `as Enemy` → Variant 鸭子调用（Boss 非 Enemy 子类，cast null 致溅射静默丢失） | hit_logic 新增 Boss 溅射断言 PASS / 全量 0 FAIL |
| E02 | ✅ 已修复 | `start_panel.gd` 教程按钮通关后 disabled + `_on_tutorial_pressed` tutorial_done 守卫 | startup_flow 新增禁用断言 PASS |
| E03 | ✅ 已修复 | `_valid_difficulty_defs` 补 DIFFICULTY_DEF_KEYS 8 数值键存在+类型校验 | balance 新增缺子键回退断言 PASS |
| E04 | ✅ 已修复 | PHANTOM 视觉挂 BreatheRoot，呼吸写容器不覆盖调用方 alpha | return_cinematic/intro/base_system 0 FAIL |
| E05 | ✅ 已修复 | `start_release()` 统一清 HUD 提前离舰进度条 | mothership_summon 新增清除断言 PASS |
| E06 | ✅ 已修复 | 侧方离场 960 → `view_world_rect().get_center().x` | enemy_combat/wave_pacing 0 FAIL |
| E07 | ✅ 已修复 | `_explode()` 注释修正（Boss 由 null 排除为有意设计） | 随 E01 |
| E08 | ✅ 已修复 | 激光 buff 归零早退前 `if _active: _end_beam()` | buff33/smoke 0 FAIL |
| E09 | 🟦 登记不修 | BEAM_HALF_WIDTH 乘 ws 削弱激光判定，需产品判断 | — |
| E10 | ✅ 已修复 | load_profile locale 经 zh/en 白名单守卫 | startup_flow/base_system 0 FAIL |
| E11 | ✅ 已修复 | key_bindings 数组元素 int/float 判型 | startup_flow/base_system 0 FAIL |
| E12 | ✅ 已修复 | `save_manager.save()` 临时文件 + rename 原子写 | base_system/startup_flow 0 FAIL |
| E13 | 🟦 登记不修 | 缓存与难度切换语义冲突，需信号刷新链路 | — |
| E14 | 🟦 登记不修 | 当前安全（内联 polygon 非共享 sub_resource） | — |
| E15 | 🟦 登记不修 | 每帧字典 get 无分配 | — |

> **全量回归（修复后）**：`--headless --import` ✅ / `--quit-after 300` 0 错误 ✅ / **全量 30 断言场景 0 FAIL** ✅（含新增 E01/E02/E03/E05 断言）。
