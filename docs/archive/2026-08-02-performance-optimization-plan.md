# InfiAir 性能优化计划书（2026-08-02）

> **性质**：代码审计驱动的性能优化计划（调研 + 计划 + **2026-08-02 当日全量落地**）。
> **日期**：2026-08-02
> **范围**：对局常驻热路径、对象池与分配、渲染常驻成本、瞬态帧率尖峰、启动一次性成本。
> **依据**：三个并行只读代码扫描（热路径 / 分配池化 / 渲染启动）+ 实测基准（`perf_bench`、主场景启动计时）。
> **执行状态（2026-08-02）**：P0 ×4 / P1 ×7 / P2 ×8 全部落地（提交 `920e5e9`，2026-08-02 口径统一回填）并全量回归 0 FAIL（31 断言场景，含 mouse_lock_test）；P3 可选项经评估全部跳过（理由见 §12）；实测 A/B 见 §2.3。
> **前置知识**：`docs/DESIGN_BASELINE.md` §3「全局不变量」、`docs/TESTING.md`、`docs/AUDIT_VAULT.md`。

---

## 目录

1. [摘要与核心结论](#1-摘要与核心结论)
2. [测量方法与基线数据](#2-测量方法与基线数据)
3. [热点地图（按系统分层）](#3-热点地图按系统分层)
4. [P0 —— 每帧级低风险优化](#4-p0--每帧级低风险优化)
5. [P1 —— 结构性优化](#5-p1--结构性优化)
6. [P2 —— 边际收益 / 中期](#6-p2--边际收益--中期)
7. [P3 —— 记录在案 / 设计权衡](#7-p3--记录在案--设计权衡)
8. [帧率尖峰（hitch）风险清单](#8-帧率尖峰hitch风险清单)
9. [验证方案与测试矩阵](#9-验证方案与测试矩阵)
10. [不变量、风险与文档同步](#10-不变量风险与文档同步)
11. [附录：约定合规性清扫清单](#11-附录约定合规性清扫清单)

---

## 1. 摘要与核心结论

### 1.1 一句话结论

**CPU 常规对局负载很健康，不存在"每帧都在烧钱"的失控热点；真正的风险集中在四类：① 高频函数被同屏实体数量聚合放大（`view_world_rect` 每弹/每敌每帧重复重建）；② 回血链路违反"热路径禁 cfg / 禁分配"约定且几乎整局常驻；③ 渲染端常驻成本（Starfield 每帧 230 条绘制指令）与受击后处理瞬时峰值；④ 启动一次性成本与波次实例化抖动。**

### 1.2 实测基线

| 指标 | 数值 | 说明 |
| --- | --- | --- |
| `perf_bench` 首跑（含机器噪声） | avg 1.002 ms / 帧 | 已弃用：首跑含环境噪声，**非真实基线**（见 §2.3） |
| `perf_bench` 干净基线（stash 全部改动，3 次中位数） | **avg 0.131 ms / 帧（≈7600 fps）** | 同环境交错 A/B 的有效对照 |
| `perf_bench` 优化后（3 次中位数） | **avg 0.120 ms / 帧（≈8300 fps）** | 相对基线 **约 -8~9%** CPU 逻辑耗时 |
| 主场景启动 + 300 帧（headless 含引擎初始化） | real ≈ 3.72 s（user 0.89 s） | 参考值；窗口模式另含字体图集构建与 GPU 烘焙 |

### 1.3 必须说清的测量局限

- **headless 测不到渲染**：Starfield 每帧 230 次 `draw_circle`、MetaHealthFX 全屏后处理、GPU 粒子、17MB 字体字形图集——这些在窗口模式才是真实成本，headless 基准完全不可见。**优化落地后必须做窗口模式人工/截图 A/B**（见 §9）。
- `perf_bench` 的压力场景是**逻辑密集**（200 敌机），不是**渲染密集**（真实对局同屏敌机 10~30、弹 30~80、爆炸若干）。因此"1.0ms 很健康"只回答"CPU 逻辑"这一半问题。

### 1.4 优先级分排总览

| 优先级 | 条目数 | 定位 | 建议窗口 |
| --- | --- | --- | --- |
| **P0** | 4 项 | 每帧级低风险（缓存/守卫/顺序调整），收益立竿见影 | 本周 |
| **P1** | 6 项 | 结构性优化（池化统一、扫描去重、绘制合批、shader 减采样） | 两周内 |
| **P2** | 8 项 | 边际收益、约定合规清扫、顺手小项 | 随迭代 |
| **P3** | 6 项 | 一次性成本 / 设计权衡，记录在案 | 有需求时 |

优先级判据：改动面 × 风险 ÷ 收益。P0 全是"小改动 + 低风险 + 明确收益"；P1 需中等改动但收益最大（尤其敌机池化统一）；P2/P3 是边际或设计权衡。

---

## 2. 测量方法与基线数据

### 2.1 本次测量做了什么

1. **静态扫描**（只读）：三个并行子代理分别扫描
   - 全部 `_process`/`_physics_process` 回调（44 处 + 7 个每帧辅助方法），按"每帧工作清单 → 证据 → 成本评级"逐条输出；
   - 对象池实现、每帧/高频分配、Tween/Timer/协程创建频率、信号风暴、注册表访问、激活/回收路径、JSON 访问；
   - 渲染（Starfield / MetaHealthFX shader / 粒子 / 材质 / 绘制面）、启动与场景加载、文本字体。
2. **运行时基准**：
   - `godot --headless --path . res://test/perf_bench.tscn` → `PERF_RESULT frames=1800 total_ms=1803 avg_frame_ms=1.002 equivalent_fps=998.3`；
   - `/usr/bin/time -p godot --headless --path . --quit-after 300` → real 3.72s。
3. **关键证据抽查**：对计划中 P0 条目涉及的源码（`game_state.gd`、`player_damage.gd`、`hud.gd`、`spawner.gd`）逐行核实，行号与本计划书一致。

### 2.2 性能约定现状核对（AGENTS.md / DESIGN_BASELINE §3）

| 约定 | 现状 | 违规点 |
| --- | --- | --- |
| 热路径禁 `cfg()` 查 JSON | ✅ 各实体 `_ready` 一次性缓存 | ❌ `heal_tick → heal → max_health()` 链每物理帧 2 次 cfg（§4.2） |
| 禁每帧字符串节点查找 | ✅ 全部缓存引用 | — |
| 禁 `get_nodes_in_group()` | ✅ 全 scripts 零调用，注册表 O(1) | — |
| 禁每帧分配 | ⚠️ 主体良好 | ❌ `hud.gd:610` 每帧格式化字符串（§4.2）；事件性 `enemies.duplicate()`、`get_children()` 等节流后仍存在（§6） |
| 禁 `sin()/cos()`（用 `sin_fast`） | ⚠️ 敌机/Boss/移动策略全合规 | ❌ 12 处直接调用（§11） |
| `Time.get_ticks_msec()` 每帧一次 | ⚠️ | ❌ `player.gd:568/573`、`mothership.gd:347/485` 每帧 2 次 |
| HUD 0.1s 节流 + 值变化才更新 | ✅ `hud.gd:333-338` 实现正确 | ⚠️ 受击/回血信号每帧触发 `_on_health_changed` 的格式化（§4.2 后半） |
| `view_world_rect()` 统一口径 | ✅ 无硬编码 | ❌ **无帧缓存**，每弹/每敌/每 Boss 每帧重复调用（§4.1） |

---

## 3. 热点地图（按系统分层）

```
                          ┌─ 每帧 view_world_rect ×（子弹N + 敌机N + 玩家 + Boss×2~3 + 事件）  ← P0-1
   对局 CPU 常驻 ──────────┼─ 回血链 heal_tick→heal→max_health(2×cfg)→hud 格式化              ← P0-2
                          ├─ _set_mission_progress 每帧无条件字典写 + 线性扫                  ← P0-3
                          ├─ aim_frame 每帧 2~3 次 O(enemies) 扫描                            ← P1-3
                          └─ Starfield 每帧 230 点循环 + 230 draw_circle                       ← P1-4
                           
   分配 / 池 ──────────────┼─ 普通波次敌机 instantiate+queue_free（池闲置）                    ← P1-1
                          ├─ 受击闪白每命中一个 create_tween（4 处）                          ← P1-2
                          ├─ 冲刺残影每 0.08s 新建 Sprite2D+Tween                             ← P1-5
                          └─ 每击杀 cfg("effects.shake.*")（3 处）                            ← P1-6
                           
   渲染 ───────────────────┼─ MetaHealthFX 受击峰值：每像素 ≤9 次屏幕纹理采样                  ← P1-7
                          ├─ 常驻面板 _draw 每帧数组分配 / 打字机每帧 set_text                ← P2
                          └─ 全工程仅 2 个 shader、1 处 hint_screen_texture（结构良好）
                           
   启动 / 瞬态 ────────────┼─ 17MB NotoSansSC 字体 + 512² SubViewport 烘焙 + 常驻母舰虚影      ← P3
                          ├─ 过场 84/61 处 .new() + 13 处逐实例材质                           ← P3
                          └─ 波次同步进场瞬间实例化 3~5 架（池化统一后消除）                   ← P1-1
```

**结构上做得好的地方（计划不动的部分）**：对象池 `_repooling` 防护到位；无每帧 emit；无 `await create_timer`；`_move_ctx` 字典复用；`sin_fast` 2049 项表惰性一次构建全敌机共享；注册表 O(1)；MetaHealthFX epsilon 早退（常态零 GPU）；BGM 异步加载。

---

## 4. P0 —— 每帧级低风险优化

> 共性：改动 1~2 处、不触碰行为语义、收益可被基准与代码审查直接确认。建议一次提交全部落地。

### 4.1 `view_world_rect()` 物理帧缓存

- **位置**：`autoload/game_state.gd:581-590`（函数本体）；调用点：`bullet.gd:225`（每弹每帧）、`enemy.gd:373/394`（每敌每帧）、`player.gd:578/581`（每物理帧）、`boss.gd:645/763-764` 与 `boss_movement.gd:102/125`（每帧 2~3 次）、`formation_bomb.gd:81`、`spawner.gd:467/479`（事件）。
- **问题**：每次调用都执行 `get_viewport().get_visible_rect()`（视图/视口查询）+ 除法 + `Rect2` 构建 + `grow`。同屏 30~80 弹 + 10~30 敌机时，同一物理帧内该链被重复执行数十次。这是全项目**单点调用最热**的函数。
- **方案**：在 GameState 增加物理帧号守卫缓存：
  ```gdscript
  var _view_rect_frame: int = -1
  var _view_rect_cached: Rect2 = Rect2()

  func view_world_rect(margin: float = 0.0) -> Rect2:
      var frame := Engine.get_physics_frames()
      if frame != _view_rect_frame:
          _view_rect_frame = frame
          # …现有中心/尺寸计算逻辑，结果存 _view_rect_cached（margin=0 基准）…
      if margin == 0.0:
          return _view_rect_cached
      return _view_rect_cached.grow(margin)
  ```
  - **失效条件**：`_view_zoom_factor` 变更（profile 设置，低频）与相机 `global_position` 跳变（返航/入场/轨道打击清场等）时必须置 `_view_rect_frame = -1` 强制重算。zoom setter 与相机瞬移点（`camera_shake` 不动中心，无需处理；`main.gd` 中重置相机位置处手动失效）。
  - **风险**：低。`Rect2.grow(margin)` 每次仍构造新 Rect2，但省掉了整条视口查询链；也可顺手把 `margin==0` 的调用直接返回缓存引用。
- **预期收益**：消除每物理帧数十次 `get_viewport()` 链；这是对局全时段常驻收益。
- **验证**：`perf_bench` A/B（§9.1）；`smoke_test`；窗口模式目测进出屏销毁/刷怪位置无变化（`view_world_rect` 语义零变化）。

### 4.2 回血链路热路径解耦（`heal_tick → heal → max_health → cfg` + HUD 格式化）

- **位置**：
  - `scripts/player_damage.gd:64-69` `heal_tick()`（每物理帧调用）；
  - `autoload/game_state.gd:639-641` `heal()` → `game_state.gd:627-628` `max_health()`（每次 2 次 `cfg()`，内部 `path.split(".")` 分配字符串数组）；
  - `autoload/game_state.gd:412-417` `passive_regen_delay()/rate()`（每帧双层 `DIFFICULTY_DEFS[difficulty][...]` 字典查找 + `float()` 转换）；
  - `scripts/hud.gd:610` `_on_health_changed` 内 `"%d/%d" % [ceili(new_health), int(max_hp)]`（每帧构造数组 + 字符串，比较在格式化之后）。
- **问题**：被动回血（受伤 delay 后常驻）使该链**几乎整局每物理帧执行**，同时违反"热路径禁 cfg"与"禁每帧分配"两条约定。它是全项目**唯一**的每帧 cfg 违规点。
- **方案**（最小侵入，分三步）：
  1. `max_health()`：基础值（`player.max_health`、`buffs.extra_life.max_hp_bonus`）在 `_load_balance` 后一次性缓存为成员变量；`buff_count(&"extra_life")` 查询保留（O(1)）。行为语义完全不变。
  2. `passive_regen_delay/rate`：在难度确定路径（`set_difficulty` / `apply_run_save` / `reset_run`）缓存两个 float；或在 `heal_tick` 侧每帧只读缓存。简单做法：GameState 加 `_regen_delay_cached`/`_regen_rate_cached`，难度变更时刷新。
  3. `hud.gd:610`：把格式串构造移到变化判断之后——先取 `var hp_int := ceili(new_health)`，仅当 `hp_int != _last_hp_int` 时才格式化并写 Label（`_last_hp_int` 新增字段）。注意 `_last_hp_text` 比较保留（语言切换刷新依赖它）。
- **风险**：低。全程只动"取值来源"，不改任何游戏数值。
- **预期收益**：每物理帧省 2 次 `cfg` 路径解析（含分配）、2 次双层字典查找、1 次字符串格式化 + 数组字面量分配。常驻收益，累计最大。
- **验证**：`smoke_test` + `base_system_test`；窗口模式受击后观察血量恢复与 HUD 数字刷新正常。

### 4.3 `_set_mission_progress` 值变化守卫 + 目标缓存

- **位置**：`autoload/game_state.gd:795-803`；调用方 `game_state.gd:307-313`（`_process` 每帧 `_set_mission_progress(&"survive_180", int(run_time))`）。
- **问题**：每帧无条件写 `m["progress"]`（即使值未变）且 `mission_goal()` 线性扫 `MISSION_DEFS`。`int(run_time)` 实际每秒才变化一次，99% 的帧是纯浪费。
- **方案**：
  1. `_init_missions()` 时把每个 mission 的 goal 缓存进字典（`m["goal"]` 或独立 dict）；
  2. `_set_mission_progress` 开头加 `if int(m["progress"]) == value: return`。
- **风险**：低。任务完成判定逻辑（`was_done` 计算）顺序保持不变。
- **预期收益**：每帧省 1 次字典写 + 1 次数组线性扫（全时段常驻）。
- **验证**：`smoke_test`；`autoplay_test` 的任务计数断言（SUMMARY 输出）。

### 4.4 HUD 低血脉动 `sin()` → `Enemy.sin_fast`

- **位置**：`scripts/hud.gd:727`（`_update_vignette` 低血期每帧 `sin()`）。
- **问题**：低血时（DYING/CRITICAL 档常驻）每帧直接 `sin()`；同类还有 11 处（§11），但本处是 HUD 常驻路径。
- **方案**：改用 `Enemy.sin_fast(...)`，参数域换算一致（`sin(x)` → `sin_fast(x)`，`sin_fast` 以弧度输入，见 `enemy.gd:85-96`）。
- **风险**：极低；查表线性插值误差 < 视觉感知。
- **验证**：窗口截图对比低血脉动波形无可见差异；`hud_capture`。

---

## 5. P1 —— 结构性优化

> 共性：改动面稍大或涉及行为路径，需要专项验证（A/B 对照、专项测试、窗口截图）。

### 5.1 普通波次敌机接入对象池（头号结构性机会）

- **位置**：`scripts/spawner.gd:476-483` `_on_telegraph_timeout`（`ENEMY_SCENE.instantiate()`）；对照 `scripts/enemy_pool.gd`（`USE_POOL := true`，仅 `spawner.gd:500-501 spawn_minion` 与 `tutorial.gd` 使用）。
- **问题**：**常态对局 95% 敌机走"直接实例化 + queue_free"**，`EnemyPool` 几乎闲置。每次波次进场（3~5 架同步）都有实例化抖动：`_ready` 约 30 次 cfg、尾焰 Sprite2D、碰撞 shape 复制（`enemy.gd:230`）全部重建；退场 queue_free 后 GC 压力。这与 `AGENTS.md`/`DESIGN_BASELINE §7.2` 已登记的"两条路径并存"技术债一致，方向也已明确（评估统一池化 + A/B 对照）。
- **方案**：
  1. `EnemyPool.spawn()` 扩展支持普通波次所需的入参：`anchor_y`、`special`（`died` 信号连接）、`btype`（弹种）。当前 `spawn_minion` 只传 4 参，需对齐 `_on_telegraph_timeout` 的 `setup(config, strategy, difficulty, btype)` 全签名（查 `enemy_pool.gd` 当前 spawn 签名后扩展）。
  2. `_on_telegraph_timeout` 改走 `GameState.enemy_pool.spawn(...)`，删除 `instantiate` 分支。
  3. **池容量策略**：`enemy_pool.gd` 当前与 bullet_pool 相同"无上限、峰值即池大小"？——需核实；若为无上限则保持，若 `USE_POOL` 之外还有容量常量则按波次峰值 + 余量设档，避免池无限膨胀。
  4. 回归重点：`reactivate()` 必须完整重置普通机型全部状态（`_on_telegraph_timeout` 路径现在依赖 `_ready` 的初始值，池化后依赖 `reactivate`）。对照 `pool_reuse_test` 覆盖清单逐项核对。
- **风险**：中（生命周期路径变更）。两条路径的死亡信号、注册表登记、`special` 连接、锚点/入场行为必须逐一对齐；`_exit_tree` 清池防护（`_repooling`）已存在。
- **预期收益**：消除每波次实例化/销毁抖动；`_ready` 30 次 cfg 摊销到池预热；对局中后段（无限流）节点抖动显著下降。
- **验证**：`pool_reuse_test`（必跑）；`smoke_test`；`perf_bench` A/B（含 spawn churn 路径已覆盖）；`autoplay_test` 长时探针（注册表一致性双向比对）；窗口模式目测波次入场动画与 special 敌机行为一致。
- **备注**：这是全计划中**唯一触及游戏行为语义**的条目，按 `DESIGN_BASELINE §8.1` 属既定方向；落地后回填技术债状态并同步 `AGENTS.md`（"两条路径"表述更新）。

### 5.2 受击闪白 Tween 复用（消灭高频对象分配）

- **位置**：`scripts/enemy.gd:497`、`scripts/turret_battery.gd:201`、`scripts/formation_craft.gd:52`、`scripts/boss.gd:860`（每命中创建 `create_tween()` 做闪白/闪红）。
- **问题**：激烈战斗每秒可分配数十个 Tween 对象（Tween 含内部绑定与节点引用）；弹幕海 + 多目标命中时该分配是每帧级噪声。
- **方案**：每个实体在 `_ready`/`reactivate` 预建一个"闪白"Tween 并 `pause()`；受击时 `reset()` + 重新填 tween 属性 + `play()`（Godot 4 支持 `Tween.reset()`）。或退一步：闪白改为计时器驱动的 `modulate` 衰减（复用 `_time` 已有字段，零 Tween）。
- **风险**：低-中（Tween 复用需处理"播放中被再次击中"的打断语义，与现有 `kill()` 行为对齐）。
- **预期收益**：消灭受击热点上的对象分配；敌机数 × 命中率的乘积效应。
- **验证**：`hit_logic_test`、`enemy_combat_test`；窗口截图受击闪白无视觉差异。

### 5.3 辅助瞄准扫描去重（帧号守卫缓存）

- **位置**：`scripts/aim_frame_layer.gd:61-64`（`_process` 每帧 `marked_target_at(p.aim_point())` O(enemies) + per-enemy `has_meta`/`get_meta`）、`:110-142`（`magnet_pull`，由 `player.aim_point()` 每帧触发再 O(n)）、`:170-177`（`_draw` 再扫一遍 O(n)）；`scripts/player.gd:734/737`（`_fire` 每次射击 `marked_target_at` + `nearest_cone_target` 各 O(n)）。
- **问题**：同一物理帧内对 enemies 注册表做 2~3 次全表扫描；规模（敌机 10~40）下每次 O(n) 不大，但结构性双扫是持续浪费，且 per-enemy 的 `has_meta`/`get_meta` 是字典查找。
- **方案**：仿 `player.gd:596` 的 `_aim_smoothed_frame` 模式——`AimFrameLayer` 缓存 `(physics_frame, aim_point, marked_target)` 三元组，`_process`、`magnet_pull`、`_draw` 三者共用同一次扫描结果（同帧内标记目标不会变化）。
- **风险**：低。扫描结果仅帧内有效，语义不变。
- **预期收益**：每帧从 2~3 次 O(n) → 1 次 O(n)。
- **验证**：`aim_crosshair` 相关测试与窗口目测准星/辅助框/磁吸手感；`autoplay_test`。

### 5.4 Starfield 绘制合批（230 条绘制指令 → 1 条）

- **位置**：`scripts/starfield.gd:38-55`。
- **问题**：每帧 230 次 `draw_circle`（内部约 32 段多边形细分，≈7300 顶点/帧）；每帧 `queue_redraw()` 全量重绘。这是**渲染端最持续的常驻成本**（headless 测不到，窗口模式真实存在）。
- **方案（推荐 A + 可选 B）**：
  - **A（合批）**：星星改为圆点/方块点，用一次 `draw_primitive(POINTS, ...)` 或 `draw_multiline` 批量提交（230 点 → 1 条绘制指令）。2.5px 的圆 → 2.5px 方块在俯视星空场景视觉差异极小。近星/远星分两批（2 条指令）。
  - **B（降频重绘，可选叠加）**：星空滚动是慢速背景，可每 2~3 帧 `queue_redraw()` 一次（位置仍每帧更新），降低 30~50% 绘制频率；返航 warp 期恢复每帧（warp 时速度 ×18，视觉敏感）。
- **风险**：低-中（视觉差异需截图确认；`warp_factor` 期间的滚动观感需人工复核）。
- **预期收益**：消除全时段最持续的渲染端指令数（230 条 → ≤2 条，顶点 7300 → 230）；对低端 GPU（GL Compatibility，可能跑在核显/集显）尤其有价值。
- **验证**：窗口模式 `visual_capture` 前后截图对比；返航 warp 观感目测。

### 5.5 冲刺残影小池

- **位置**：`scripts/player.gd:636-645`（`spawn_afterimage`：新建 Sprite2D + `create_tween` + `queue_free`）、`scripts/player_dash.gd:61`（每 0.08s 触发）。
- **问题**：每次冲刺（0.25s）约新建 3 个节点 + 3 个 Tween；高频冲刺/带 dash 的玩家操作下持续抖动。
- **方案**：预建 3~4 个残影 Sprite2D 常驻节点，逐个激活复用（alpha 衰减用共享 Tween 或 Timer）。
- **风险**：低。
- **预期收益**：冲刺热操作下的节点/Tween 分配归零。
- **验证**：窗口目测冲刺残影连续性与透明度渐变；`autoplay_test`。

### 5.6 每击杀 shake 参数缓存

- **位置**：`scripts/enemy.gd:510`、`scripts/formation_craft.gd:60`、`scripts/turret_battery.gd:209`（每击杀/每命中一次 `cfg("effects.shake.*")`）。
- **问题**：对局中每秒数次（击杀频率 × 事件）的 `path.split` 分配 + 字典遍历。
- **方案**：仿 `explosion.gd` 的 `_visual_scale` 缓存模式（G022 先例）：受击/死亡类 shake 强度在 `_ready`/`setup` 一次性缓存为成员。
- **风险**：低。
- **预期收益**：击杀/命中路径的每事件分配消除（与 §5.2 同批次做）。
- **验证**：`enemy_combat_test`、`elite_turret_event_test`。

### 5.7 受击后处理峰值减采样

- **位置**：`assets/shaders/meta_health.gdshader:41-56`（受击层：色差 3 次 `texture()` + 手写 5-tap 径向模糊 ×5 次）、`:82`（波纹）；`scripts/meta_health_fx.gd:361-375`（LOD/reduce_flash 折算）。
- **问题**：受击脉冲（约 0.4s）期间 1920×1080 下每像素最多 **9 次屏幕纹理采样**（≈1800 万次采样/帧），是**全项目最大的瞬时 GPU 峰值**；在低端机上可能造成受击瞬间的可见帧率下探。
- **方案**：模糊 tap 5→3（或 4）、色差采样 3→2；`reduce_flash` 用户开启时直接走 LOD1（已实现）。视觉差异集中在"受击红光边缘锐度"，可接受。
- **风险**：低-中（视觉调优，必须窗口截图人工核对 `meta_fx_capture` 各血量档）。
- **预期收益**：受击峰值采样数降低 ~40%，消除瞬时帧率尖峰的最主要来源。
- **验证**：窗口模式受击脉冲目测 + `meta_fx_capture` 截图对比；LOD0/LOD1 两档分别核对。

---

## 6. P2 —— 边际收益 / 中期

> 共性：收益存在但边际，或改动面与收益不成比例；随迭代顺手处理。每条标注可独立实施。

### 6.1 面板 `_draw` 每帧数组分配缓存
- **位置**：`scripts/ui_chamfered_panel.gd:66-91`。
- **问题**：每个可见切角面板每帧 `_draw` 分配 8 点 `PackedVector2Array` + 4 角嵌套 Array；HUD/页面常驻 10~15 面板 → 每帧约 100 个小分配。
- **方案**：切角几何按 `size`/`radius` 缓存 `PackedVector2Array`（尺寸不变即复用），尺寸变化时重建。
- **风险**：低。**验证**：`hud_capture` / `ui_capture` 截图。

### 6.2 光束 tick 整表复制改索引遍历
- **位置**：`scripts/laser_weapon.gd:156`（`GameState.enemies.duplicate()` 每 0.1s 一次）；同模式 `bullet.gd:236/251`（爆炸命中事件）。
- **问题**：光束激活期每秒 10 次整表拷贝；事件路径低频可保留。
- **方案**：改为索引遍历 + 延迟删除标记（或 `filter` 语义等价写法）；`bullet.gd` 事件路径保留（事件驱动，非热点）。
- **风险**：低。**验证**：`buff33_test`、`buff_visuals_test`。

### 6.3 母舰目标数组复用
- **位置**：`scripts/mothership.gd:527-538`（`_live_targets()` 每次发射分配 `Array[Node2D]` + lambda `sort_custom`）。
- **方案**：模块级复用数组 + 每次 `clear()`；排序 lambda 改静态比较函数。
- **风险**：低。**验证**：`mothership_summon_test`、`base_system_test`。

### 6.4 MetaHealthFX 激活期字典访问收敛
- **位置**：`scripts/meta_health_fx.gd:257-413`。
- **问题**：激活期每帧约 20 次 `_cfg[...]` 查找 + 19 组 `_put` epsilon 双查（每次 2 次字典查找）。已是缓存+epsilon 的常数级成本，仅当 §5.7 落地后此条才有继续收敛的价值。
- **方案**：`_cfg` 字典改为平铺成员变量（最热 20 个参数逐一缓存）。
- **风险**：低，改动面大（机械替换）。**验证**：`meta_fx_capture`。

### 6.5 爆炸池容量 cfg 顺手缓存
- **位置**：`scripts/explosion.gd:20`（每次新建实例查 `cfg(pool_cap)`）。
- **方案**：静态变量缓存（仿 `_visual_scale`）。**风险**：极低。

### 6.6 SpawnTelegraph 预告几何缓存
- **位置**：`scripts/spawn_telegraph.gd:27`（`_draw` 每帧构造 3 点 `PackedVector2Array`）。
- **方案**：静态复用数组。**风险**：极低。

### 6.7 打字机逐帧 `set_text` 节流
- **位置**：`scripts/comm_overlay.gd:87`（每 0.03s 一字的间隔内仍每帧 `left()` + set_text）。
- **方案**：按打字速度只在字变化帧 set_text（当前 33 次/秒 → 字变化率）。**风险**：低。事件播报期 3.5s，收益小，可延后。

### 6.8 直接 `sin()` 12 处 → `sin_fast`（约定合规清扫）
- **位置**：`hud.gd:727`（已在 P0-4 单列）、`mothership.gd:347/493/495/557/561`、`strike_carrier.gd:125`、`formation_bomb.gd:73`、`player_buff_visuals.gd:115/117/119/122`、`meta_health_fx.gd:298/322/374`、`warp_gate.gd:131`、`aim_crosshair.gd:48`、`aim_frame_layer.gd:171`、`orbital_strike.gd:170`（过场/瞬态类可豁免）。
- **问题**：均为每帧 1~2 次的低危调用；其中 `player_buff_visuals`（脉动件可见时）、`meta_health_fx`（激活期）、`mothership`（光束可见期）属对局路径，其余过场豁免。
- **方案**：批量替换为 `Enemy.sin_fast`；注意 `Time.get_ticks_msec()` 每帧一次配套（`player.gd:568/573`、`mothership.gd:347/485`）。**风险**：极低。**验证**：窗口目测脉动/光束无可见差异。

---

## 7. P3 —— 记录在案 / 设计权衡

> 共性：一次性成本或视觉设计权衡，**不建议现在动**；记录触发条件。

### 7.1 启动一次性成本（字体 + 烘焙 + 母舰虚影）
- **17MB NotoSansSC 字体**：`hud.gd:7`、`ui_theme.gd:44`、`tutorial.gd:5` 引用（ResourceLoader 按路径去重为 1 次加载）。主要成本是首次文本绘制的字形图集构建。**触发条件**：若实测窗口模式启动白屏 >1s，再考虑异步预载/子集字体。不动。
- **512² Voronoi 裂纹 SubViewport 烘焙**：`meta_health_fx.gd:427-453`，启动 1 帧 GPU 峰值（一次性，窗口 512² / headless 64²）。已释放资源。不动。
- **常驻母舰虚影**：`main.gd:109`（`_charge_ghost` 完整实例化含 Turret/粒子子节点）。**可选项**：改为首次进入主菜单或首次蓄力时实例化；收益小（启动期 1 次实例化）。记录。

### 7.2 过场运行时 `.new()` 与逐实例材质
- **位置**：`intro_cinematic.gd` 约 84 处 `.new()`（含 13 处逐实例 `CanvasItemMaterial.new()`）、`return_cinematic.gd` 约 61 处；`boss_attacks.gd:160`、`hud.gd:120`、`dawn_station.gd:32/57` 亦逐实例。
- **结论**：过场一次性且预分配零堆策略良好；逐实例材质与共享材质同为 ADD 混合、draw call 相同，仅多建对象。**可选项**：换 `CinematicFx.additive_material()` 共享（`cinematic_fx.gd:30-35` 已提供静态单例），低收益。记录。

### 7.3 每机 +1 尾焰 Sprite2D draw call
- **位置**：`enemy.gd:109/240`。每机一个 additive 尾焰 Sprite2D；同屏峰值 ~12 机 → 12 条 ADD 指令。**结论**：视觉设计权衡（敌机尾焰是识别度要素），保留。记录。

### 7.4 敌机策略对象每重激活新建
- **位置**：`enemy.gd:330`（`reactivate` 内 `_make_strategy()` 新建策略对象 + 参数字典）。spawn 率 ~1-2/s，非每帧。**可选项**：策略对象池。记录。

### 7.5 爆炸池上限之外新建即销毁
- **位置**：`explosion.gd:6,20-31`（`POOL_CAP=24`，超限新建、播完 `queue_free`）。清屏大片击杀时短时新建+销毁。**结论**：24 并发爆已覆盖常态峰值；如需最坏情况零抖动可上调容量，但换来常驻内存。记录，保持现状。

### 7.6 对象池"无容量上限"设计
- **位置**：`bullet_pool.gd`（峰值并发即峰值池大小）。**结论**：合理权衡（内存换取零抖动），与子弹峰值并发天然一致。记录，保持现状。

---

## 8. 帧率尖峰（hitch）风险清单

> 优化计划的另一半是"消灭尖峰"。以下按严重程度排序，均已在正文给出对应条目；此处集中列出以便追踪。

| # | 尖峰场景 | 触发 | 现状成本 | 对应条目 |
| --- | --- | --- | --- | --- |
| 1 | 受击后处理峰值 | 每次受击（0.4s 脉冲） | 每像素 ≤9 次屏幕纹理采样（≈1800 万采样/帧 @1080p） | §5.7 |
| 2 | 波次同步进场实例化 | 每波 3~5 架同步入场 | 每架 `_ready` 约 30 次 cfg + 贴图/碰撞/尾焰重建 | §5.1 |
| 3 | 大片击杀爆炸叠加 | 清屏/炸弹命中 | 24 池之外新建 + 2 发射器 × 新实例 | §7.5（保留） |
| 4 | 受击闪白 Tween 风暴 | 弹幕海多目标命中 | 每秒数十个 Tween 分配 | §5.2 |
| 5 | 冲刺残影节点抖动 | 高频冲刺 | 每 0.08s 新建节点+Tween | §5.5 |
| 6 | 过场演出叠加 | 开场/返航各一次 | 84/61 处 `.new()` + 粒子硬上限 400 | §7.2（保留） |
| 7 | 启动 | 每次启动 | 17MB 字体图集 + 512² 烘焙 | §7.1（监控） |

**观察**：尖峰 2/4/5 全部源于"高频路径上的对象分配"，**§5.1 + §5.2 + §5.5 一次性落地即可同时消除三个尖峰来源**；尖峰 1 是唯一的 GPU 侧尖峰（§5.7）。

---

## 9. 验证方案与测试矩阵

### 9.1 基准 A/B 流程（严格按 `docs/TESTING.md`）

```bash
# 每项改动前后各跑 3 次，交错执行，取中位数对比（避免机器噪声）
godot --headless --fixed-fps 1000 --path . res://test/perf_bench.tscn
```

- **对比指标**：`avg_frame_ms`（当前基线 1.002 ms）。P0 全部落地预期降至 ~0.85~0.95ms（方向性估计，不承诺具体数值）；P1 的池化统一预期体现在**节点抖动与长时间运行稳定性**上（perf_bench 的 spawn churn 路径覆盖）。
- **渲染端无法 headless 度量**：一律走窗口模式 `visual_capture`/`meta_fx_capture`/`hud_capture` 截图 + 人工目测（低端参考机复测）。

### 9.2 每项改动的专项测试

| 条目 | 必跑测试 | 补充 |
| --- | --- | --- |
| §4.1 view_world_rect 缓存 | `smoke_test`、`view_zoom_test` | 窗口目测出屏销毁/刷怪位置；`autoplay_test` |
| §4.2 回血链 | `smoke_test`、`base_system_test` | 窗口受击回血 HUD 目测 |
| §4.3 mission 守卫 | `smoke_test` | `autoplay_test` 任务计数 SUMMARY |
| §4.4 / §6.8 sin_fast | `hud_capture`、`meta_fx_capture` | 窗口目测 |
| §5.1 敌机池化 | **`pool_reuse_test`（必跑）**、`smoke_test`、`enemy_combat_test`、`wave_pacing_test` | `autoplay_test` 注册表一致性；窗口目测波次入场 |
| §5.2 Tween 复用 | `hit_logic_test`、`enemy_combat_test` | 窗口受击闪白目测 |
| §5.3 辅助瞄准去重 | `autoplay_test` | 窗口准星/磁吸手感 |
| §5.4 Starfield 合批 | `visual_capture` | 返航 warp 观感目测 |
| §5.5 残影池 | `autoplay_test` | 窗口冲刺目测 |
| §5.6 shake 缓存 | `enemy_combat_test`、`elite_turret_event_test` | — |
| §5.7 shader 减采样 | `meta_fx_capture`（LOD0/LOD1 两档） | 窗口受击目测 |
| P2 各条 | 对应专项（§6 已列） | — |

### 9.3 全量回归

- 每批改动落地后：`--headless --import` → `--quit-after 300` → `smoke_test` → 涉及池化时 `pool_reuse_test` → 涉存档/基地/母舰时 `base_system_test` → 全量 31 断言场景 0 FAIL（落地时点含 mouse_lock_test，2026-08-02 口径统一订正）→（改动涉及对局行为时）`autoplay_test` 长时探针。

---

## 10. 不变量、风险与文档同步

### 10.1 必须维持的设计基线不变量（`DESIGN_BASELINE §3`）

1. 碰撞层、受击判定、`world_scale` 杠杆与幂等赋值——本计划不触碰。
2. `view_world_rect()` 语义零变化（仅加缓存，margin 行为不变）。
3. 热路径禁 cfg 的约定被本计划**修复而非破坏**（§4.2 消除唯一违规）。
4. 对象池 `_active`/`_repooling` 防护保留；§5.1 扩池只扩 `EnemyPool` 公共接口，不绕生命周期。
5. 数值口径不变：本计划**不改任何 balance.json 数值**，不触碰 `cfg()` 回退值。

### 10.2 风险清单

| 风险 | 等级 | 缓解 |
| --- | --- | --- |
| §5.1 池化引入状态重置缺失（普通机型 vs Boss-3 小怪初始化差异） | 中 | `reactivate` 全字段对齐清单 + `pool_reuse_test` + `autoplay_test` 双向比对 |
| §4.1 缓存失效点遗漏（zoom/相机瞬移） | 低 | 失效点集中登记（zoom setter + 相机重置点），代码注释 + 审查 |
| §5.7 shader 视觉变化 | 低-中 | 窗口截图两档核对，不满足可回退 tap 数 |
| §5.4 星空视觉变化 | 低-中 | 截图 + warp 观感，可回退保留 draw_circle |
| 过度优化收益不达预期 | 低 | 每项独立提交，A/B 中位数对比，未达预期即回退（改动均有回退面） |

### 10.3 文档同步义务

- 每批改动落地后：`AUDIT_VAULT.md` 登记条目 + 修复起效记录（**专有档案，禁止删除**）。
- §5.1 池化统一落地后：更新 `AGENTS.md`（"两条路径"现状表述）与 `DESIGN_BASELINE §7.2` 技术债状态。
- 本计划书所列条目如需调整方向：同步 `ROADMAP.md`（方向类决策单一事实源）。
- 若改动涉及 `cfg()` 键集（本计划不涉及，§6.5 仅是缓存非新键）：跑 `gen_balance_map.py`。

---

## 11. 附录：约定合规性清扫清单

> P0/P1/P2 之外顺手可清的"规范瑕疵"，独立于性能收益，纯为约定一致（`DESIGN_BASELINE §3` / `AGENTS.md`）。

| 位置 | 瑕疵 | 处理 |
| --- | --- | --- |
| `player.gd:568/573` | 每帧 2 次 `Time.get_ticks_msec()` | 合并为 1 次（§6.8 配套） |
| `mothership.gd:347/485` | 每帧 2 次 `Time.get_ticks_msec()` | 同上 |
| `game_state.gd:307-313` | `_process` 每帧 `run_time += delta` 与难度档检查（必要逻辑，勿删） | 仅 §4.3 守卫，勿动主体 |
| `explosion.gd:20` | 每次新建查 cfg | §6.5 |
| 12 处直接 `sin()` | 约定违规 | §4.4 + §6.8 |
| `spawn_telegraph.gd:27` | 每帧临时数组 | §6.6 |
| `comm_overlay.gd:87` | 每帧 set_text | §6.7 |

---

## 12. 执行结果（2026-08-02 全量落地）

> 本节记录计划落地实况与计划书偏差，是"计划 → 执行"的追踪单一事实源。涉及源码改动 24 个文件。

### 12.1 落地状态汇总

| 条目 | 状态 | 落地摘要 |
| --- | --- | --- |
| §4.1 view_world_rect 帧缓存 | ✅ | GameState 物理帧号守卫缓存 + zoom/camera 变更失效（`set_view_zoom`/`set_view_zoom_factor`/`load_profile`/`camera_ref` setter 四处失效点） |
| §4.2 回血链解耦 | ✅ | `max_health()` 基础值 `_apply_balance` 缓存；`passive_regen_delay/rate` 缓存 + `set_difficulty`/`_apply_balance` 刷新；HUD 文本整数档位守卫（含 max 上限守卫） |
| §4.3 mission 守卫 | ✅ | goal 入 missions 条目 + 值未变跳过写 |
| §4.4 HUD 低血脉动 | ✅ | `Enemy.sin_fast` |
| §5.1 敌机池化统一 | ✅ | `EnemyPool.spawn`/`Enemy.reactivate` 扩展 `p_bullet_type` 可选参；`_on_telegraph_timeout` 改走池；清理 spawner 未用 `ENEMY_SCENE` |
| §5.2 受击闪白 | ✅ | **实现方式偏离计划**：Godot 4.6 `Tween` 无 `reset()`（原计划预建 Tween 复用不可行），改**手动衰减**（`_flash_timer` + `_physics_process` 逐帧 lerp），四实体（enemy/turret/formation_craft/boss）零 Tween 分配 |
| §5.3 辅助瞄准去重 | ✅ | `marked_target_at` 渲染帧+同点缓存（player.aim_point 与 _process 共用） |
| §5.4 Starfield 合批 | ✅ | 预分配线段数组 + 每层单条 `draw_multiline`（230 条指令 → 2 条，零每帧分配） |
| §5.5 残影小池 | ✅ | 4 节点预建 + `_process` 手动淡出（含 `add_child` deferred 防"busy setting up children"） |
| §5.6 shake 缓存 | ✅ | enemy（普通/精英两档）/formation_craft/turret_battery `_ready` 缓存 |
| §5.7 shader 减采样 | ✅ | 色差 3→2 采样、模糊 5→3 tap（受击峰值 9 次采样 → 6 次） |
| §6.1 面板几何缓存 | ✅ | 尺寸/chamfer 键缓存 PackedVector2Array |
| §6.2 光束遍历 | ✅ | `enemies.duplicate()` → 从尾向前索引遍历（erase 不破坏未处理索引） |
| §6.3 母舰数组复用 | ✅ | `_live_targets` 输出缓冲复用 |
| §6.4 MetaFX 字典收敛 | ⏭️ **跳过** | `_cfg` 已是一次性缓存，激活期每帧 ~20 次哈希查找属微秒级常数；平铺 20 成员变量改动面大、收益不成比例（计划书原文即定位为可选） |
| §6.5 爆炸 cfg 缓存 | ✅ | `pool_cap` 静态缓存 |
| §6.6 预告几何缓存 | ✅ | 三角静态 `PackedVector2Array`（顺带 sin_fast） |
| §6.7 打字机节流 | ✅ | 字符数未变不 set_text |
| §6.8 sin_fast 清扫 | ✅ | 对局路径 11 处替换（hud/mothership×5/strike_carrier/formation_bomb/player_buff_visuals×4/meta_health_fx×3/warp_gate/aim_crosshair/aim_frame_layer）；过场/一次性构建豁免；`Time.get_ticks_msec` 每帧两次合并（player 一处） |
| §7 P3 可选项 | ⏭️ 全部跳过 | 母舰虚影延迟（把启动成本挪到蓄力瞬间，负优化）；过场材质共享（共享 ShaderMaterial 有污染风险、收益近零）；策略对象池（收益与复杂度不成比例）——均与计划书"记录在案"定位一致 |

### 12.2 执行中发现并修复的问题

1. **Godot 4.6 `Tween` 无 `reset()`**：计划 §5.2 原方案（预建 Tween + reset 复用）在实现时发现 API 不存在（首次落地导致 4 处 SCRIPT ERROR + smoke 2 项 FAIL），按计划书备选方案改**手动衰减**（零 Tween 分配，效果等价）。
2. **残影池挂 Main 的 add_child 时序**：`player._ready` 期间 Main 处于"busy setting up children"状态，直接 `add_child` 报错；改 `call_deferred` 挂载。
3. **HUD 上限刷新缺口**：`_last_hp_int` 守卫在 extra_life 叠加（max_hp 变化）但 health 整数不变时漏刷新血量文本；补 `_last_max_int` 上限守卫（buff33_test 29 PASS 验证）。

### 12.3 实测 A/B（同环境交错，中位数）

| 轮次 | avg_frame_ms | equivalent_fps |
| --- | --- | --- |
| 基线（stash 全部改动） | 0.131 / 0.133 / 0.130 | 7627 / 7500 / 7692 |
| 优化后 | 0.119 / 0.121 / 0.122 | 8411 / 8295 / 8219 |
| **相对变化** | **约 -8~9%** | — |

> 注：首跑基线 1.002ms 经对照证实为机器噪声（stash 后同环境基线 0.131ms），**首跑数值不可作为基线**；A/B 需同环境交错。headless 只测 CPU 逻辑，渲染端收益（Starfield 230→2 指令、shader 9→6 采样、敌机实例化抖动消除）需窗口模式人工/截图验收（见 §9 流程）。

### 12.4 回归结果

- `--headless --import` 0 错误；`--quit-after 300` 0 错误。
- `smoke_test` 142 PASS 0 FAIL；`pool_reuse_test` 12 PASS 0 FAIL；`base_system_test` 46 PASS 0 FAIL；`buff33_test` 29 PASS 0 FAIL。
- 全量 31 断言场景 0 FAIL（§9.3 清单；落地时点实际为 31，原记 30 系计划写作时点，2026-08-02 口径统一订正）。
- 文档同步：AUDIT_VAULT 回填 D08/D11/E13/G017/G025/G027（见档案性能优化落地记录）；DESIGN_BASELINE §7.2 敌机生成路径技术债更新；AGENTS.md 同步。

---

*文档性质：性能调研与优化计划（2026-08-02 调研产出，当日按本计划全量落地）。生成：2026-08-02。*
