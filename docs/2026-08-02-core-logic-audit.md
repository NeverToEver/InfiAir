# 核心逻辑代码审核报告（2026-08-02）

> 状态：**已全量修复（2026-08-02，3 批提交 + 文档回填）**。发现登记见 `docs/AUDIT_VAULT.md` G 系列（第五轮）及其「修复起效记录」；本文件为审核范围、规则、证据与修复优先级追踪单一事实源。

## 审核范围与规则

- **范围**：主要逻辑实现（对局编排/状态、玩家系统、战斗实体、服务与对象池），共 17 文件约 6900 行，按 4 分区并行审核：
  1. 对局编排与状态：`main.gd` / `game_state.gd` / `spawner.gd` / `tutorial.gd`
  2. 玩家系统：`player.gd` / `player_damage.gd` / `player_dash.gd` / `aim_crosshair.gd` / `aim_frame_layer.gd`
  3. 战斗实体：`enemy.gd` / `boss.gd` / `bullet.gd` / `laser_weapon.gd` / `explosion.gd`
  4. 服务与对象池：`balance_service.gd` / `save_manager.gd` / `sfx_player.gd` / `entity_registry.gd` / `bullet_pool.gd` / `enemy_pool.gd` / `mothership.gd`
- **规则**：现代流行标准（正确性 / SOLID / DRY / KISS / Godot 4 热路径性能 / 生命周期安全）+ 项目自身约定（AGENTS.md 强制项：尺寸幂等赋值、无 `await create_timer` 协程、热路径缓存 cfg、`view_world_rect()` 无硬编码、实体注册表、对象池 `_active`/`_repooling` 防护、`tr()` 翻译、公开接口封装 A1）。
- **方法**：4 分区并行人工通读 + 跨文件依赖追踪 + 主控对 P1 级发现逐条亲验证据（读源码确证，非仅采信分区结论）。

## 发现总表（32 项，按严重度排序）

| 编号 | 严重度 | 位置 | 类别 | 描述 | 判定建议 |
| --- | --- | --- | --- | --- | --- |
| G01 | P1 | `spawner.gd:501-506` `_trigger_boss` + `:563-572` `clear_pending` | 纯 bug（整局瘫痪） | Boss 预警 2s 窗口内返航：`clear_pending()` 只停 Timer 不复位 `_boss_active`，且无任何其他写 `_boss_active=false` 的路径 → continue 后 `_process` 三处守卫（波次/328、Boss 门/342、事件/353）永久冻结，整局空转无怪无 Boss；`:562` 注释"之后按分数/时间门控再触发属预期"与实际不符 | **修**：`clear_pending()` 复位 `_boss_active=false`（或置位推迟到 `_spawn_boss`） |
| G02 | P1 | `boss.gd:828-834` `take_damage` / `laser_weapon.gd:152-158` / `bullet.gd:250-257` | 纯 bug（玩家有损→奖励失真） | Boss 逃跑期 `_begin_escape`（:906-912）只置 `collision_layer/mask=0` 挡 Area2D 重叠；激光 `_damage_tick` 与导弹 `_splash` 按注册表+距离判定**绕过碰撞层** → 逃跑窗口（~1.3s）内可被补刀致死 → `_die()` 触发 `add_boss_kill` 加分/升难度，与 :905 注释「逃跑无 add_boss_kill/加分/难度提升」及 `fire_enrage_snapshot`(:797)/`_fire_enrage_release`(:808) 同款 `_escaping` 防护模式矛盾 | **修**：`take_damage` 开头加 `if _escaping: return`（`_die` 兜底） |
| G03 | P1 | `tutorial.gd:97` / `start_panel.gd:244-247,282-288` | 玩家有损（E02 补全） | 教程 `_ready` 无条件 `delete_save()`；E02 守卫只拦 `tutorial_done==true`，**漏「有进行中存档（has_save）且未通关教程」路径**——该态下教程按钮仍启用，点入即静默删掉返航生成的存档 | **修**：`_on_tutorial_pressed` 加 `has_save()` 守卫（或教程入口改隔离而非删除） |
| G04 | P2 | `game_state.gd:706-716` `rebind_action` | 逻辑 bug | 冲突键清理只遍历已自定义 `key_bindings`，不扫 `_default_bindings`：dash 未自定义（默认 Space）时把 dock 改绑 Space → `apply_key_bindings` 从默认表重灌 Space → 两动作同键冲突 | **修**：冲突清理同时扫 `_default_bindings` |
| G05 | P2 | `tutorial.gd:310-311` | 热路径性能 | 阶段 2 每物理帧调 `GameState.max_health()` 两次（内部 2 次 `cfg()` 含 `path.split(".")` 分配），违反「热路径 _ready 缓存」约定 | **修**：_ready 缓存 max_hp |
| G06 | P2 | `spawner.gd:160-161,178-185` | 健壮性 | `_apply_balance` 对 `hover_band`/`enemies.types`/`elites.types` 嵌套结构无判型，手改 JSON 结构损坏时 `band[0]`/`src["hp"][0]` 越界崩溃，与 C03/E03「损坏 JSON 回退默认」口径不一致 | **修**（沿用 `_valid_difficulty_defs` 式判型） |
| G07 | P2 | `aim_frame_layer.gd:74-80` | 池化复用失配 | `frame_half_size()` 首调后把碰撞半径永久缓存进 meta `aim_frame_radius`，`Enemy.setup()` 每次激活重写 shape 半径、`deactivate()` 不复位 meta——同一池化实例被不同半径机型复用则框尺寸/入框判定过期（当前唯一池化路径 `spawn_minion` 恒同半径，未触发但随时可踩） | 待判定（建议 deactivate 清 meta 或缓存键并入半径） |
| G08 | P2 | `boss.gd:640` | 项目约定 | 逃跑离场判定硬编码 `position.y < -280.0`，违反「出界计算必须 `view_world_rect()`」约定（enemy.gd:370-375 已相对化） | **修**：改 `view_world_rect().position.y - 280.0` |
| G09 | P2 | `bullet.gd:60` + `balance_service.gd:57` | 热路径性能 | 每发敌弹创建时 `enemy_damage_ramp()` → `cfg()` 做 `path.split(".")`+字典遍历，弹幕压力下每秒 30+ 次 JSON 查询；Enemy/Boss 其余配置均已 _ready 缓存 | **修**：ramp 因子启动缓存一次 |
| G010 | P2 | `bullet.gd:197` + `entity_registry.gd:12` | 性能 | `GameState.enemies` 为 `Array[Node]`，`has(homing_target)` 每追踪弹每物理帧 O(N) 线性扫描（注册表还混入 turret/formation_craft） | 待判定（侧 Dictionary 索引或 `is_inside_tree` 判定） |
| G011 | P2 | `mothership.gd:670-676` `_exit_tree` / `main.gd:665` | UI 残留（E05 补全） | 返航提前 `queue_free()` 母舰时不清 HUD 提前离舰进度条（`hud.set_early_leave_charge(-1.0)` 是唯一隐藏入口）；驻留期蓄力 H 中按 B 返航 → 进度条以冻结比例残留跨基地可见 | **修**：`_exit_tree` 补隐藏调用（判空） |
| G012 | P3 | `game_state.gd:595` | 魔法数字 | `add_boss_kill` 加分基准 `500.0` 硬编码，未入 balance.json | 修（入 balance 或收敛 const + 同步 BALANCE_MAP） |
| G013 | P3 | `game_state.gd:900-906` | 边界条件 | `apply_run_save` 恢复 buffs 层数无钳制（手改存档可写负/超大，`extra_life` 溢出放大 max_health） | 修（clamp ≥0 / 上限） |
| G014 | P3 | `tutorial.gd:156,179,204,220` | 一致性 | 教程内 4 处硬编码世界坐标（960/600/300），D10 已收敛同类 960 为 `view_world_rect().get_center().x` | 修（对齐 D10 口径） |
| G015 | P3 | `tutorial.gd:320,329` | 性能 | 阶段 3/4 蓄力期间每物理帧 `tr()`+`%`+Label 赋值，比 HUD「仪表 0.1s 节流」约定更频 | 修（0.1s 节流） |
| G016 | P3 | `aim_frame_layer.gd:41-43` | 信号清理 | `_exit_tree` 未像 player.gd（C22 模式）显式断开 `aim_assist_changed`，节点未 free 重新入树会重复连接（当前靠 free 自动清理未触发） | 修 |
| G017 | P3 | `aim_crosshair.gd:48` / `aim_frame_layer.gd:172` | 性能 | `_draw` 每渲染帧直接 `sin()`（脉冲/频闪），项目约定热路径用 `sin_fast` 查表 | 不修（每帧各 1 次量级可忽略） |
| G018 | P3 | `player.gd:368-373` vs `aim_frame_layer.gd:163-168` | 重复代码 | 同形状「峰值-末端-下限」分段距离衰减双实现（`Player.aim_dist_falloff` / `AimFrameLayer._dist_falloff`），改一侧忘另一侧破坏一致性 | 待判定（可抽公共函数） |
| G019 | P3 | `player.gd:481-484` | 边界条件（死字段） | `movement_locked` 时直接 `_dashing = false` 中断冲刺（复核：全项目无任何写 true 的路径，恒 false 不可达；狂暴期移动约束由 `apply_enrage_slow` ×0.35 减速实现——2026-08-02 口径：狂暴独立演进，非原作对齐） | 登记不修 |
| G020 | P3 | `laser_weapon.gd:136-137,147` | 防御性缺口 | `_start_beam` 无条件覆写 `_saved_autofire`：若 autofire 已 false 期间二次进入（当前不可达——`_active` 门闩+cooldown），二次保存 false → 恢复后 autofire 永久关闭 | 待判定（E08 同类不可达缺口） |
| G021 | P3 | `laser_weapon.gd:96` | 死代码 | `_aim_dir(_start)` 参数 `_start` 声明未使用，误导调用方 | 修（删参数） |
| G022 | P3 | `explosion.gd:24` + `enemy.gd:237` | 性能 | `spawn_at` 每次爆炸 `cfg("effects.explosion_visual_scale")` JSON 查询（中频）；`Enemy._ready` 每机新建 CanvasItemMaterial（`soft_texture()` 已共享、material 未共享） | 修（低风险优化） |
| G023 | P3 | `explosion.gd:59-61` | 生命周期 | `_boss_seq_step` 中 `parent` 无效时对已释放的子节点调 `queue_free()`（timer 与 parent 同生共死，分支实际不可达，属 UAF 危险行） | 待判定（改 `parent == null` 语义或直接 return） |
| G024 | P3 | `boss.gd:255,705` | 魔法数字 | 三型普通阶段召唤间隔 `_summon_timer = 6.0` 硬编码重置，balance.json 无对应键（狂暴 `E3_SUMMON_INTERVAL` 有键） | 修（补键并缓存） |
| G025 | P3 | `enemy.gd:391` / `bullet.gd:225` / `boss.gd:647,759,764` / `boss_movement.gd:120` / `boss_attacks.gd:217` | 热路径性能 | 每实体每物理帧重复 `view_world_rect()`（Rect2 构造+viewport 查询），同屏 30 敌机+100 弹时每帧 ~130 次（C06 只收敛 enemy 单帧内调用） | 待判定（GameState 每帧缓存 view） |
| G026 | P3 | `enemy.gd:430` / `boss_fire.gd:19` | 边界条件 | 射手与玩家圆心完全重合时 `(player - from).normalized()` 得零向量 → 子弹零方向飞行永不销毁 | 待判定（加 `base_dir == ZERO` 防御） |
| G027 | P3 | `mothership.gd:543,577` | 热路径性能 | `_live_targets()` 空目标分支每物理帧分配数组+全表扫描（`_gatling_timer`/`_missile_timer` 在置位前 `is_empty() → return`），与 C28 注记「每 0.13-0.3s 开火」口径只在有目标时成立 | 修（先置位再判空） |
| G028 | P3 | `sfx_player.gd:25-26` | 防御性 | `play()` 无池空守卫，`build_pool()` 未调用（如测试直接 `new()`）时 index 越界/除零 | 修（`is_empty(): return`） |
| G029 | P3 | `balance_service.gd:36-39` | 类型健壮性 | `cfg()` 数值宽容分支原样返回 JSON 节点，手改 `"mag_cells": 10.5` 等使类型化 int 字段漂移为 float（与 C18 显式 `int()` 模式不一致） | 修（按 default 类型显式转换） |
| G030 | P3 | `mothership.gd:588` | 命名一致性 | 导弹路径复用 `GATLING_SCORE_SCALE` 得分系数，语义命名不一致，分别调参时易误改 | 修（独立常量或注明共用） |
| G031 | P3 | `mothership.gd:182-184` | 资源共享 | _ready 直写双炮塔共享 `ParticleProcessMaterial` 的 scale（当前幂等同值安全，与 E14 `beam_pts *= ws` 同族；若改相对赋值会跨实例重复缩放） | 不修（沿用 E14 注明安全口径） |
| G032 | P3 | `mothership.gd:168-170` + `mothership.tscn:22` | 注释不符 | 脚本注释宣称「tscn 存 1.0 基准」，tscn 实际 `scale = (1.25, 1.25)` 且脚本硬编码 `1.25 * ws` 覆盖——注释与实现不符，1.25 未收敛 | 修（修正注释或提具名常量） |

## 与既有登记的关系（去重说明）

- 分区发现中「`player_damage.heal_tick` 每帧查询」与 **E13** 重复（已登记"登记不修"：缓存与难度中途切换语义冲突）——不重复登记。
- 「激光判定尺度混用（`BEAM_HALF_WIDTH` 未乘 ws）」与 **E09** 重复（已登记"登记不修"：乘 ws 后显著削弱激光命中，属游戏性变更需产品判断）——不重复登记。
- 「`ParticleProcessMaterial` 共享写 scale」与 **E14** 同族（E14 判定"当前安全、注明口径"成立）——G031 轻量保留。

## 总体结论与修复优先级建议

整体工程质量高：A1 公开接口封装、A2 服务委托、对象池 `_active`/`_repooling` 防护、Timer 替代协程、`sin_fast` 查表、`view_world_rect()` 收敛等均落实到位，未发现 `await create_timer` 违规、信号重复连接或池防护缺失。

**建议修复优先级**（**已于 2026-08-02 全量落地**，3 批提交：P1×3 → P2×9 → P3+待判定）：
1. **P1 立即修**：G01（Boss 预警返航整局空转）、G02（Boss 逃跑期补刀奖励失真）、G03（教程入口静默删档）——批次 1
2. **P2 顺手**：G04（改键冲突）、G08（硬编码 -280）、G09（敌弹每发 JSON 查询）、G011（进度条残留）、G05/G06（热路径/判型）——批次 2
3. **P3 累积待办**：按上表判定建议，优先 G013（存档层数钳制）、G014（教程坐标）、G016（信号断开）、G029（cfg 类型）——批次 3+4
4. **待判定项**：G07/G010/G018-G020/G023/G026 已按上表建议修复；G019（锁定期冻结冲刺）/G025（view 每帧微优化）/G017/G031 判定不修并登记说明

> 注：D01 审计记录宣称「Boss 预警取消后按门控再触发」实际因 G01 的 `_boss_active` 卡死而不成立，修复 G01 后该口径自然恢复（已随批次 1 恢复成立）。
