# 2026-08-05 InfiAir 主架构运行效率优化报告（官方文档 + 社区最佳实践审计）

> **文档目的**：审计 InfiAir 主架构（`scenes/main.tscn` / `scripts/main.gd` / `GameState` facade / 实体注册表 / 对象池 / 实体生命周期）的运行效率，对照 Godot 官方文档与社区主流架构实践，识别效率瓶颈并给出分级、可落地的优化建议。
>
> **日期**：2026-08-05（今日）
>
> **方法**：① 两个并行子代理分别完成本地代码深度审计与社区主流实践调研；② Playwright MCP 逐页阅读 Godot 官方文档（Performance / Best Practices 系列，共 10 个页面 + 官方 bullet_shower demo 源码）；③ 本地运行 `perf_bench` 获取实测基线。全程只读，未修改任何代码。
>
> **版本口径**：项目 Godot 4.6.2（GL Compatibility）；官方 stable 文档当前为 4.7，本文引用结论在 4.6 同样成立（差异处已注明）。

---

## 目录

1. [摘要与核心结论](#1-摘要与核心结论)
2. [测量方法与实测基线](#2-测量方法与实测基线)
3. [现状主架构画像（含正面清单）](#3-现状主架构画像含正面清单)
4. [官方最佳实践对照](#4-官方最佳实践对照)
5. [社区主流架构实践对照](#5-社区主流架构实践对照)
6. [效率瓶颈清单（含代码证据）](#6-效率瓶颈清单含代码证据)
7. [分级优化建议](#7-分级优化建议)
8. [架构演进方向（更高并发时的路线图）](#8-架构演进方向更高并发时的路线图)
9. [验证方案](#9-验证方案)
10. [参考资料](#10-参考资料)

---

## 1. 摘要与核心结论

### 1.1 一句话结论

**InfiAir 的主架构在 CPU 逻辑侧非常健康（实测 200 敌机 + 强制开火的压力场景下平均每物理帧 ≈1.01–1.03ms，远低于 16.7ms 的 60fps 预算），且已落地大量官方推荐的正向模式（对象池、实体注册表、信号驱动、`cfg()` 热路径缓存、合批绘制）；真正的优化机会集中在三处：① 死亡回放录制的每帧 `get_children()` + `pop_front()` O(n) 分配链（唯一的高危常驻热点）；② 渲染端每颗子弹 2 个 Polygon2D 的 draw call 规模（GL Compatibility 渲染器下 draw call 成本更高）；③ 启动一次性路径（MetaHealthFX 裂纹场 GPU 回读 + 3.5MB WAV BGM 逐次解码）。**

### 1.2 与社区/官方对照后的总体判断

- 官方文档与社区对"大量同类对象的 2D 游戏"给出的路径是分层的：**节点 + 对象池（300–500 对象量级）→ Manager 批量迭代 + 合并绘制（数千量级）→ MultiMesh（单 draw call）→ Server API（最大自由度、手写量最大）**。InfiAir 峰值约 300+ 子弹，正处在第一层，**不需要**下探到 Server/MultiMesh；当前阶段把第一层内的三个热点修掉即可。
- 官方 demo（`2d/bullet_shower`）用 Server API 管理 500 颗子弹（全部子弹一个 `_draw()` + `PhysicsServer2D` RID），README 明言"比节点+实例化高效得多，但需要更多编程、更不直观"；社区实测（官方论坛 Bullet Hell 长帖）显示 Server API 实现不当反而掉帧。**结论：先榨干节点层，再用低层 API。**
- `GameState` 唯一 autoload + 非 autoload 服务委托（Balance/Save/Sfx/EntityRegistry）落在官方"autoload 适用判据"与社区"防 God object"共识的优解区，**架构方向无需更改**，只需注意不把对局临时态塞进 GameState。

### 1.3 优先级分排总览

| 优先级 | 条目 | 定位 | 预期收益 |
| --- | --- | --- | --- |
| **P0** | 3 项 | 每帧级热点（回放录制分配链 / 敌机体碰轮询 / 子弹渲染合并） | 消除唯一高危常驻分配 + 每物理帧 N 次空间查询 + 渲染 draw call 减半 |
| **P1** | 5 项 | 结构性/启动路径（duplicate 拷贝 / 循环外提 / epsilon 守卫 / BGM 格式 / 烘焙延后） | 消除事件性 O(n) 拷贝与启动 stall |
| **P2** | 4 项 | 边际收益与约定清扫 | 微 CPU / 可维护性 |
| **长期** | 3 档 | 并发翻倍时的架构演进路线 | 见 §8 |

---

## 2. 测量方法与实测基线

### 2.1 工具

项目自带 `test/perf_bench.tscn`（`scripts/tools` 无依赖）：main + **200 敌机**（各机型/策略混合）+ 玩家强制开火（每 5 物理帧齐射）+ 每 20 物理帧一次爆炸 + 每 10 物理帧一次 `spawn_minion` churn，跑 1800 物理帧统计平均耗时；headless + 物理帧率拉满（1000Hz），测得的 ≈纯 CPU 逻辑成本。

### 2.2 今日实测（本机，Godot 4.6.2 headless，2026-08-05）

| 运行 | avg 帧耗时 | 等效 FPS |
| --- | --- | --- |
| 第 1 次 | 1.033 ms | 968.3 |
| 第 2 次 | 1.009 ms | 991.2 |
| 第 3 次 | 1.024 ms | 976.1 |

**今日 3 次运行稳定在 ≈1.01–1.03ms/帧，可作为当前基线。** 对照历史文档 `docs/archive/2026-08-02-performance-optimization-plan.md` §12.3 的"干净基线 0.131ms/帧（stash 全部改动，3 次中位数）"，差异约 8 倍。差异原因未完全定位（当日 A/B 采用 stash 对照、机器负载、引擎运行环境均可能有影响；`perf_bench` 压力参数 ENEMY_COUNT=200 自 2026-07-31 起未变），但今日数值多次复现、可重复，报告中以今日实测为准，并提示**任何优化改动都必须按 §9 的"同环境交错 A/B"流程对比，而非与历史数字直接比**。

### 2.3 测量局限（必须说清）

- **headless 测不到渲染**：每颗子弹 2 个 Polygon2D 的 draw call、Starfield 合批、MetaHealthFX 全屏后处理、GPU 粒子、字体字形图集——这些在窗口模式才是真实成本。优化渲染端后必须做**窗口模式人工/截图 A/B**。
- `perf_bench` 压力场景是**逻辑密集**（200 敌机）而非**渲染密集**（真实对局同屏敌机 10–30、弹 30–80）。"1.0ms 很健康"只回答 CPU 逻辑这一半问题。

---

## 3. 现状主架构画像（含正面清单）

### 3.1 主场景树（`scenes/main.tscn`）

```
Main (scripts/main.gd)                      ← Node2D，运行时容器
├─ Starfield (Node2D)                        ← 每帧 queue_redraw + draw_multiline 合批（230 星 → 2 指令）★
├─ Camera2D (camera_shake.gd)                ← process_mode=Always
├─ Player (player.tscn)
├─ Spawner (Node)
├─ BulletPool / EnemyPool (Node)             ← 对象池；闲置回池节点下
├─ HUD (CanvasLayer, layer=2)
├─ BuffUI / PauseUI / SettingsUI / GameOverUI / BaseUI / ExitConfirm (CanvasLayer, Always)
├─ BackNavigator / MouseTrap (Node, Always)
└─ 运行时创建：MetaHealthFX / AimFrameLayer / Intro·ReturnCinematic / OrbitalStrike /
   MothershipSummonWindow / WarpGate / EliteTurretEvent / FormationStrikeEvent / 母舰虚影等
```

所有动态实体直挂 Main（深度 1），清场/测试遍历靠 `get_children()`——**这是"清理成本最低"的编排**，与官方"同一 parent 生命周期对齐"原则一致（见 §4.6）。

### 3.2 已落地的正向模式（审计确认，保持即可）

| 模式 | 位置 | 说明 |
| --- | --- | --- |
| 实体注册表替代 group 查询 | `autoload/game_state.gd` → `scripts/entity_registry.gd` | `enemies` 数组 + `enemies_has()` O(1) dict；全仓无热路径 `get_nodes_in_group` |
| 对象池完整 | `bullet_pool.gd` / `enemy_pool.gd` / `explosion.gd` | 子弹/敌机/爆炸均走池；`release` 幂等 + `call_deferred` reparent（物理回调内改树安全） |
| 信号驱动 + 节流轮询 | `hud.gd`（0.1s 仪表轮询）、`GameState.buffs_changed` | HUD 非逐帧轮询；仪表写入有 0.1s 节流 |
| 热路径禁 cfg / 禁分配 | 各实体 `_ready` 缓存 + `buffs_changed` 增量刷新 | `cfg()` 仅低频调用（`balance_service.cfg` 每次 `path.split(".")` 分配，非热路径可接受） |
| 合批绘制 | `starfield.gd`、`aim_frame_layer.gd`、`meta_health_fx.gd` | 单节点 `_draw` 画全部对象；Meta HUD idle 早退 + 满血隐藏（常态零 GPU）★ |
| 视图区域物理帧缓存 | `game_state._cached_view_rect()` | 每弹/每敌/玩家共享一帧一次视口查询 |
| 唯一 autoload facade | `autoload/game_state.gd` | 委托 4 个非 autoload 服务，符合官方 autoload 判据 |
| 依赖注入 | `main.gd:88` `_event.set_spawner(_spawner)` 等 | A5 模式：事件侧不再 group 现找 |
| 低层对象组合 | `player_visuals.gd`（RefCounted） | 符合官方"避免节点过度使用"建议 |

---

## 4. 官方最佳实践对照

本节为 Playwright MCP 逐页阅读 Godot 官方文档（stable，4.7 口径）后的要点摘录与项目对照。**"✅ 已符合 / ⚠️ 部分符合 / ❌ 待改进"** 标记为本次审计结论。

### 4.1 General optimization tips（通用优化原则）

- **官方**：瓶颈数学——先 profile 定位最慢处，优化后再 profile（"Profile → 优化 → 回到 1"循环）；设计期就考虑性能（数据局部性、预计算、把计算移出循环）；不是所有优化都值得做（Knuth：97% 的时候别过早优化）。
- **对照**：项目已把"热路径禁分配/禁 cfg/缓存"作为全局不变量执行（AGENTS.md 约定），`perf_bench` 提供了测量手段。⚠️ 建议把 §2.3 的"headless 测不到渲染"补齐为"窗口模式 A/B"例行流程（见 §9）。

### 4.2 Optimization using Servers（低层服务器 API）

- **官方**：场景系统可完全绕过；`RenderingServer.canvas_item_create()` + `canvas_item_add_texture_rect()` 直接创建精灵，`Transform2D` 可无限次设置；`PhysicsServer2D.body_create()` 创建碰撞体；**强烈警告：不要每帧向服务器查询返回值（会强制同步、stall 掉帧）**。
- **官方 demo（bullet_shower）**：500 颗子弹全部无节点——每颗一个 `Bullet` 内部类（position/speed/body RID），`_physics_process` 批量推进 + `body_set_state`，`_draw()` 里 `draw_texture` 循环一次画完；子弹间 `collision_mask=0`。
- **对照**：InfiAir 目前 300+ 量级不需要（社区也证实 Server 实现不当更慢，§5.1）。❌ 若未来并发翻倍，最低成本路径是"子弹**渲染**走 RenderingServer canvas item 批量画 + 碰撞留场景树"，而非全量 Server。

### 4.3 CPU optimization（CPU 优化）

- **官方**：每个节点都有成本，`_process`/`_physics_process` 沿树传播；**更少的节点 + 每个节点做更多 → 更好**；从场景树移除节点（`remove_child`）有时比暂停/隐藏更有效；物理：简化碰撞形状、移出视野的对象移出物理、物理 tick 率可降、固定步长插值（physics interpolation）是廉价平滑方案。
- **对照**：InfiAir 物理 tick 60Hz 保持默认（弹幕游戏不宜降）；2D 插值对 GL Compatibility 收益有限（子代理审计：保持 off 合理）。⚠️ "移除优于隐藏"对回池对象有争议（社区反例见 §5.3），项目"隐藏 + 禁处理"路线正确。

### 4.4 GPU optimization（GPU 优化 / 2D batching）

- **官方**：2D batching 把相似项合成单 draw call；**复用材质与 shader**（2 万对象 100 材质 ≫ 2 万对象 2 万材质）；Compatibility（OpenGL）下 draw call 比 Vulkan 贵；透明对象不能靠 Z-buffer，必须画家序 + 逐层填充，重叠透明区成本高。
- **对照**：项目正是 GL Compatibility。⚠️ 子弹每颗 2 个 Polygon2D（§6-4）在 compat 下是明显的 draw call 放大点；共享材质/图集对项目收益大于 Vulkan 项目。

### 4.5 Scene organization（场景组织）

- **官方**：游戏应有"入口点"——`Main` 节点 + `main.gd` 主控（InfiAir 完全一致）；随后 World 与 GUI 分家（InfiAir 的 Main 承担了 World 职责，GUI 用 CanvasLayer 分层，等效成立）；**场景组织按关系而非空间**——"删父必删子"才用父子关系，否则兄弟/独立分支；依赖注入被明确推荐；子系统在 SceneTree 里拥有自己的区段。
- **对照**：✅ 全挂 Main = 生命周期对齐 + 清理成本最低，社区亦认可（§5.6）；依赖注入已是项目惯例（A5）。

### 4.6 Autoloads versus regular nodes（Autoload 对比）

- **官方**：autoload 适用判据（三条同时满足）——数据全部内部持有 + 需要全局可访问 + 需要独立于场景存在；autoload 不一定非要"单例"，可以实例化多份；避免全局状态的手段：场景内自持、`class_name` 静态函数/静态变量（4.1+）、Resource 共享数据。
- **对照**：✅ `GameState` 满足判据（全局分数/HP/buff/难度 + 不写每帧逻辑 + 独立于场景）。⚠️ 注意不把"对局临时态"（当前波次、敌人存活数、事件互斥标志）塞进 GameState——main.gd 作为 run 编排者是天然重置点（§5.4 社区警告）。

### 4.7 Godot interfaces（对象间通信）

- **官方**：引用获取成本排序——`@onready var child = $Child`（快、缓存）≫ `$Child` 动态查找 ≫ `get_node("Child")`；鸭子类型属性访问背后是 ClassDB HashMap 查找链（GDScript 慢的根源）；通信优先级：信号（只响应）→ 方法调用（开始行为）→ Callable 属性 → 节点引用 → NodePath。
- **对照**：✅ 项目广泛使用 `@onready`/成员缓存 + 信号 + 依赖注入，符合官方排序。

### 4.8 When and how to avoid using nodes for everything（避免节点过度使用）

- **官方**：Node 便宜但有上限；大量同类数据可用 `Object`/`RefCounted`/`Resource` 承载（更轻、无树成本）。
- **对照**：✅ `player_visuals.gd`（RefCounted 组合）是示范；`DeathReplay` 已是 RefCounted。❌ 反向案例：子弹每颗 2 个 Polygon2D 子节点（§6-4）——视觉层可合并，不必每颗都建子节点。

### 4.9 Data preferences（数据结构选择）

- **官方**：Array 是连续内存，**头部插入/删除 O(n)**（建议倒转数组后从尾部操作）；Dictionary 是 HashMap，O(1) 存取、Fastest 插入；Object 属性访问最慢（继承链查询）。
- **对照**：❌ `death_replay.gd:43` `_frames.pop_front()` 正是官方点名的"头部删除 O(n)"反模式（§6-2）。

---

## 5. 社区主流架构实践对照

（调研来源见 §10-2；以下为与项目直接相关的结论）

### 5.1 弹幕射击性能：节点+池化是 300–500 发量级的公认可行解

官方论坛 [Bullet Hell Optimization](https://forum.godotengine.org/t/bullet-hell-optimization/129732)（2025-12）发帖人实测：子弹用 Area2D+物理+动画+粒子 → **160 发掉帧**；改"纯 Sprite2D + 对象池 + Manager 统一 `distance_squared_to` 碰撞" → **300–400 发稳 60fps**。关键共识：① 不要让每颗子弹各自挂 `_process` 脚本，逻辑集中到 Manager 批量迭代；② 循环内不写 var、展开函数调用；③ 距离检查嵌套裁剪；④ 能用原生物理就交给原生物理；⑤ 先 Profiler 再优化。反面：发帖人试过 Server API，100 发就掉帧且一帧双命中——**Server 不是银弹**。

**对项目**：InfiAir 当前"每颗子弹一个 bullet.gd"在 300+ 量级与社区验证区间一致；若目标并发继续上调，下一步是"Manager 批量迭代 + 视觉合并"而非 Server（见 §8）。

### 5.2 碰撞对（collision pairs）是弹幕游戏的隐藏瓶颈

官方论坛 [Collision Pairs](https://forum.godotengine.org/t/collision-pairs-optimizing-performance-of-bullet-hell-enemy-hell-games/35027)：性能监视器里 Physics 2D → Collision pairs 飙到 5k–25k+ 就是每帧上万次碰撞检查；解法按优先级：**碰撞 mask 按碰撞体类型细分**（仅此一项 2–4 倍收益）→ 远距离禁用碰撞 → 屏外禁用 → 敌数上限。

**对项目**：子弹 Hitbox / 敌 hurtbox / 玩家 hitbox / GrazeArea / Parry 各自 layer/mask 已细分（GrazeArea 只 mask `enemy_bullet`），方向正确。建议自查是否存在"能碰但代码里再 `is_in_group` 过滤"的隐形碰撞对，以及 Boss 战/事件阶段对远距离敌人禁用碰撞的可能性。

### 5.3 对象池：常驻树内休眠是主流，remove_child 是反模式

官方论坛 [Issue with enemy object pool and _on_body_entered](https://forum.godotengine.org/t/issue-with-enemy-object-pool-and-on-body-entered/114772)（herrspaten）：**"把池化对象从场景树移除违背了对象池初衷——add_child/remove_child 本身就是昂贵操作，创建对象并不昂贵"**；推荐 hide + `PROCESS_MODE_DISABLED` + 禁碰撞休眠。帖子同时给出真实 bug：池对象复活时物理宽相位刚激活的瞬间会幽灵触发远处的 `_on_body_entered`——**复活时先放好位置、再 `set_deferred` 启用碰撞/监控**。

**对项目**：`bullet_pool.gd` 已用 `call_deferred` reparent（回池），方向上接近主流。⚠️ 两条直接可用的教训：① 复活完整重置状态（子弹的 `has_grazed`/`has_hit_player`/`reflect()` 派系翻转等一次性标记必须在复用清零——社区 3.1 称"漏重置 = 玄学 bug 头号来源"）；② 复活后第一帧不误伤的测试要覆盖。

### 5.4 Autoload：实用主义主流，防 God object、防对局临时态泄漏

官方论坛 [Autoloads vs Composition](https://forum.godotengine.org/t/autoloads-vs-composition/78845)：autoload 的代价是**全局名字空间污染，不是性能**（不写 `_process` 就不伤性能）；反对者称"autoload 导致可扩展性问题"，支持者称"Godot 给的是工具不是教条"。社区高共识反模式：不存场景局部状态进 autoload（跨场景残留=泄漏）、autoload 控制在 5–10 个以内、不制造循环依赖。

**对项目**：`GameState` facade（唯一 autoload + 委托非 autoload 服务）是社区认可的"轻量 DI + 单入口"折中。⚠️ 后续新服务继续走"非 autoload + GameState 转发"，不要新增第二个 autoload；事件互斥标志（`_boss_frozen`/`_waves_paused`）留在事件系统内部，符合社区建议。

### 5.5 事件总线：远距离解耦的定点工具，不是默认方案

GDQuest（事件总线教程鼻祖）：autoload 事件总线（只发信号、无逻辑）解决远距离节点解耦，但**明确警告别到处用**——所有信号堆在一个对象里，追踪要搜遍全代码库，几十个信号还能忍，再多失控。febucci：优先直接信号连接，connect 在 `_ready`、disconnect 在 `_exit_tree` 防幽灵连接。

**对项目**：已用 `GameState.buffs_changed` 等信号驱动 HUD/视觉层，符合主流。GameState 信号已较多（约 20 个），**不建议继续堆**；若未来要扩展事件互斥的广播，按域拆 `CombatEvents`/`UIEvents` 独立总线。

### 5.6 场景组织：扁平树 + 生命周期统一被认可，不为美观加层级

官方论坛/文档共识：Main 总控 + 子系统分区（项目现状）；社区对"Main 单容器"的宽容度高于对深树；**容器节点只在"对齐生命周期"时有价值**（如清场整组移除），"别为组织而组织"。

**对项目**：全挂 Main 与官方"生命周期对齐"原则一致，不必为美观加 Bullets/Enemies 容器节点——除非未来出现"只清子弹不清敌机"的明确需求。

### 5.7 GDScript 性能细节：类型化、循环外提、Packed 数组、尾部增删

GDQuest/Zylann/社区一致：`@onready` 缓存节点；循环不变量外提（`pow()` 移出循环）；一维数组 > 多维数组（≈45%）、迭代器 > 下标（≈60%）；`push_back`/`pop_back` 尾部操作；`match` 比 if 链慢；`distance_squared_to` 替代 `distance_to`；类型化数组 `Array[Bullet]`/`PackedVector2Array` 连续内存最快；每帧分配对象/数组/字典是 GC 尖峰来源。

**对项目**：`death_replay` 每帧分配内层 `[x,y]` 数组 + `pop_front` 是多重反模式叠加（§6-2）；`player.gd` spread 循环内重算 `_buff_scale`（含 `pow()`）是"循环外提"的直接靶点（§6-5）；`bullet.gd` 爆炸时 `enemies.duplicate()` 是"每帧/事件分配"靶点（§6-7）。

### 5.8 GL Compatibility 渲染器：modulate 差异会裂批次

GitHub [godot#85320](https://github.com/godotengine/godot/issues/85320)：Compatibility 渲染器下 CanvasItem 使用不同 modulate 显著掉性能（batcher 对 modulate 分组能力弱）。官方 4.3 GPU 文档同样强调 compat 下 draw call 更贵、2D batching 更有价值。

**对项目**：子弹颜色变化、敌人 tint、血条分段变色在 compat 下都是潜在批次分裂点。建议：确认对几百个子弹的 modulate 修改是否**每帧**发生（若是，优先改共享材质/着色器参数）；HUD 分段条的变色保持低频。

### 5.9 社区共识 vs 分歧（小结）

**高度一致（可直接采信）**：高 churn 对象必须池化且常驻树内休眠；池对象复活必须完整重置状态；每帧不要 `get_node`/`get_nodes_in_group`；先 Profiler 后优化；热路径类型化 + 循环外提 + 尾部增删；autoload 不写每帧逻辑就不伤性能、别用成 God object；碰撞 mask 按类型细分防碰撞对爆炸；compat 渲染器下 draw call 更贵、合并绘制收益更大。

**有争议**：节点+池化 vs Server vs MultiMesh（结论：与实现质量强相关，官方顺序是"先榨干节点层"）；池对象树内 vs 树外（主流 = 树内休眠）；autoload vs DI（务实派多数，项目 facade 折中已被认可）；事件总线（够用则不扩）。

---

## 6. 效率瓶颈清单（含代码证据）

> 严重度：**高** = 每帧或每物理帧常驻成本 / **中** = 事件性但规模大 / **低** = 微优化。行号以当前 main 分支为准。

### 6-1【高】死亡回放录制：每渲染帧 `get_children()` 分配 + 全子节点 cast

- **位置**：`scripts/main.gd:375`（`_replay.record(get_children())`）+ `scripts/death_replay.gd:33-44`
- **问题**：对局全程（`_apply_new_run` 起 `_replay.begin()`）每渲染帧：① `get_children()` 新建 Array（峰值弹幕期 300+ 元素）；② 对每个 Main 子节点 `as Bullet` cast；③ 对每颗敌弹 append 内层 `[x, y]` 数组（每帧每弹一次小数组分配）。这是**全对局唯一的高危常驻分配链**。
- **影响**：CPU/GC；且与 6-2 叠加。

### 6-2【高】死亡回放缓冲：`Array.pop_front()` O(n) 整表移位

- **位置**：`scripts/death_replay.gd:42-44`
- **问题**：帧缓冲满后 `_frames.pop_front()` —— Godot Array 头部删除是 O(n)（官方 data_preferences 点名反模式）；每次 memmove 约 180 元素（MAX_FRAMES=180）。内层 `[x,y]` 数组也是逐弹分配。
- **影响**：每帧 memmove + 分配。修复：固定容量**环形缓冲**（取模写入）+ 内层用 `PackedFloat32Array`/复用缓冲。

### 6-3【中】敌机体碰：每物理帧每敌 1 次 `overlaps_area` 空间查询

- **位置**：`scripts/enemy.gd:351-357`（`_check_body_collision`）
- **问题**：注释明示"对齐原作逐帧轮询"；敌机 Area2D `collision_mask=3` 已含玩家层，**可改用 `area_entered`/`body_entered` 信号事件驱动**，删除常驻查询。代价：需保留"重叠期间每帧重掷闪避"语义（现有 `take_damage` 守卫：无敌/闪避/单帧已支持）。
- **影响**：每物理帧 N 次空间查询（N=在屏敌数，perf_bench 压力 200 只）。属"碰撞对/空间查询"类社区点名隐藏瓶颈。

### 6-4【中】子弹渲染：每颗 2 个 Polygon2D = 2 draw call

- **位置**：`scenes/bullet.tscn`（Polygon2D + Core 两子节点）
- **问题**：峰值 300 弹 = 600 CanvasItem/draw call；GL Compatibility 下 draw call 更贵（官方 GPU 文档 + godot#85320）。官方 bullet_shower 用单节点 `_draw()` 一次画完 500 弹。
- **影响**：渲染端主要放大点（headless 测不到，需窗口模式验证）。

### 6-5【低】玩家 spread 循环内重算恒定值

- **位置**：`scripts/player.gd:870-880`（`_fire`）
- **问题**：spread 循环内每颗弹重算 `_buff_scale(&"bullet_speed", …)`（含 `pow()`）与 `bullet_damage()`——同帧内为恒定值，可提出循环（社区"循环不变量外提"直接靶点）。
- **影响**：微 CPU，自动开火高频下累积。

### 6-6【低】HUD 仪表每 0.1s 无条件写 setter

- **位置**：`scripts/hud.gd:372-377`（燃料/冲刺/弹反条轮询）
- **问题**：每 0.1s 无条件调用 setter（内部触发 `queue_redraw`），即使值未变。加 epsilon 守卫（值变化才写）可减少无意义 CanvasItem 重绘。
- **影响**：微 CPU/重绘；低优先级但改动极小。

### 6-7【中】爆炸/溅射时 `enemies.duplicate()` 整表拷贝

- **位置**：`scripts/bullet.gd:288,303`（`GameState.enemies.duplicate()`）
- **问题**：每发爆炸/溅射对整张敌人注册表浅拷贝（O(n) + 分配）+ 距离计算。`laser_weapon.gd:159-169` 已有倒序索引遍历同款已验证模式。
- **影响**：事件性 O(n)，爆炸 buff 触发时（perf_bench 每 20 帧一次爆炸即此路径）。

### 6-8【中】启动路径一次性成本：Meta FX 烘焙 GPU 回读 + BGM WAV 解码

- **位置**：`scripts/meta_health_fx.gd:437-463`（SubViewport 512² 渲染 + `get_image()` 回读，一次性 pipeline stall）+ `scripts/main.gd:437-464`（BGM 3.5MB WAV `CACHE_MODE_IGNORE` 每次进 main 重新 load + decode）
- **问题**：启动首帧有明显一次性成本；BGM 建议转 OGG（≈400KB）或改默认缓存。
- **影响**：启动时间；对"welcome → main"场景切换的感知延迟。

### 6-9【低】MetaHealthFX 自适应增益扫描每 0.25s `get_parent().get_children()`

- **位置**：`scripts/meta_health_fx.gd:353-363`
- **问题**：4 次/秒树遍历扫描。可改为注册表计数（active bullet/explosion 计数维护）。
- **影响**：微 CPU；低优先级。

### 6-10【低】敌机 `_move_ctx` 每物理帧每敌 8 个 String 键字典写入

- **位置**：`scripts/enemy.gd:465-473`
- **问题**：字典已复用（C06 好模式），但可进一步改成员字段/局部变量直传。
- **影响**：每物理帧每敌 8 次 dict hash；**收益最低的一项，可保持现状**（子代理原话）。

### 6-11【观察】爆炸池回收不 reparent 统一池节点

- **位置**：`scripts/explosion.gd:131-135`
- **问题**：池化爆炸 `_on_finished` 只 `visible=false` 不 reparent 回池节点——隐藏爆炸节点堆积在各 parent（多为 Main）下，放大 Main 子节点数（进一步放大 6-1 的遍历成本）。上限 24 有界，低危。

### 6-12【观察】同屏弹量无显式硬上限

- **问题**：现靠 DDA 降档 + 出屏 margin 回收间接控制，无硬 cap。建议保持/收紧（社区"敌数上限"共识），并可做成按硬件 FPS 动态记录峰值。

---

## 7. 分级优化建议

> 每项含：改动位置 / 做法 / 预期收益 / 风险与验证。优先级判据 = 改动面 × 风险 ÷ 收益。

### P0（低风险高收益，建议尽快落地）

**P0-1 死亡回放录制：注册表采样 + 环形缓冲**
- 位置：`scripts/main.gd:375`、`scripts/death_replay.gd`
- 做法：① 录制数据源从 `get_children()` 改为维护的敌弹数组（EntityRegistry 扩展"enemy bullets"注册，或 BulletPool 维护 active 敌弹 Array）；② 帧缓冲改固定容量环形缓冲（索引取模写入），删除 `pop_front()`；③ 内层 `[x,y]` 改 `PackedFloat32Array`（每帧复用缓冲，零分配）。
- 预期收益：消除全对局每渲染帧 1 次 Array 分配 + 300 次 cast + 每帧 O(180) 移位 + 每弹小数组分配。
- 风险：低。`death_replay` 是纯录制/重放（重放只读快照），数据格式变化不影响其他系统。验证：`death_replay_test`（如有）+ perf_bench A/B + 死亡重放目测。

**P0-2 敌机体碰：信号事件驱动替代每帧 `overlaps_area`**
- 位置：`scripts/enemy.gd:351-357`
- 做法：`area_entered`/`body_entered` 连接替代 `_check_body_collision` 轮询（collision_mask 已含 player 层）；保持"重叠期间每帧重掷闪避"语义（复用 `take_damage` 守卫：无敌/闪避/单帧）。
- 预期收益：每物理帧省 N 次空间查询（N=在屏敌数）。
- 风险：中。行为语义需专项测试（`enemy_combat_test`、体碰伤害、闪避重掷、复活后第一帧不误伤——社区 §5.3 教训）；建议 `set_deferred` 启用碰撞监控。

**P0-3 子弹渲染合并：单 CanvasItem 或共享贴图**
- 位置：`scenes/bullet.tscn`、`scripts/bullet.gd` 视觉部分
- 做法（三选一，按改动面递增）：① 弹体+白芯合并为单个 Polygon2D（draw call 减半）；② 改用共享图集 Sprite2D（让 compat batcher 生效）；③ 弹幕量大时用单节点 `_draw()` 批量画全部子弹（bullet_shower 模式）。
- 预期收益：600→300（或更少）CanvasItem/draw call；compat 下收益显著。
- 风险：中（视觉回归）。验证：窗口模式截图 A/B（§9.3）；视觉差异目测（子弹外观/颜色/反射态）。

### P1（结构性 / 启动路径）

**P1-1 爆炸/溅射遍历去拷贝**：`scripts/bullet.gd:288,303` 改倒序索引遍历（复用 `laser_weapon.gd:159-169` 已验证模式），消除每发爆炸 O(n) 拷贝。低风险，`enemy_combat_test` 覆盖。

**P1-2 玩家 spread 循环外提**：`scripts/player.gd:869-880` 将 `_buff_scale(&"bullet_speed")` 与 `bullet_damage()` 提出循环。低风险，`buff_effects_test`/`autoplay` 覆盖。

**P1-3 HUD 仪表 epsilon 守卫**：`scripts/hud.gd:372-377` 值变化才写 setter。低风险，HUD 截图目测。

**P1-4 启动路径：BGM 转 OGG + 烘焙延后**：`main.gd:452-464` BGM 转 OGG（3.5MB→≈400KB，`scripts/tools/generate_audio.py` 链）或改默认缓存；`meta_health_fx.gd:437-463` 裂纹场烘焙延后到后台帧（或复用 SubViewport 避免重建）。中风险（音频质量/烘焙路径等价性——AGENTS.md 明确"paths must stay equivalent"），验证：`base_system_test` + 启动计时 + 音频目测。

**P1-5 爆炸池回收 reparent 统一池节点**：`scripts/explosion.gd:131-135`，减少 Main 子节点堆积（同时降低 6-1 遍历成本）。低风险。

### P2（边际收益 / 约定清扫）

- **P2-1** MetaHealthFX 自适应增益扫描改注册表计数（`meta_health_fx.gd:353-363`）。
- **P2-2** 敌机 `_move_ctx` 字典改成员字段直传（`enemy.gd:465-473`）——**收益最低，可保持现状**。
- **P2-3** 同屏弹量显式硬上限（结合 DDA），池容量按实测峰值 1.2–1.5 倍预热（当前 300+ 峰值，建议确认 BulletPool 容量）。
- **P2-4** 碰撞 mask 自查（§5.2）：确认每类碰撞体 layer/mask 精确，无"能碰但代码过滤"的隐形碰撞对；Boss/事件阶段远距离敌人禁用碰撞的可行性评估。

### 明确不建议（本阶段）

- **Server API / MultiMesh 重写子弹系统**：300+ 量级不需要（社区实证 Server 实现不当更慢；官方顺序"先榨干节点层"）。见 §8 的触发条件。
- **新增第二个 autoload / 事件总线扩张**：GameState facade 已是优解，信号已够用。
- **为美观加深场景树**（Bullets/Enemies 容器节点）：除非出现"只清子弹不清敌机"的明确需求。
- **物理 tick 率下调 / physics interpolation**：弹幕游戏保持 60Hz 默认，compat 下插值收益有限。

---

## 8. 架构演进方向（更高并发时的路线图）

当前 300+ 子弹量级不需要，但记录触发条件与顺序（社区共识：**先榨干节点层，再用低层 API**）：

| 档位 | 触发条件 | 方案 | 参考 |
| --- | --- | --- | --- |
| ① | 并发持续 >500 且渲染瓶颈经 Profiler 证实 | 子弹逻辑集中到 Manager/池系统批量迭代（`Array[Bullet]` + `PackedVector2Array` 位置缓冲），每弹脚本退化为纯数据；视觉单节点 `_draw()` 批量绘制 | 官方论坛 Bullet Hell 帖；官方 bullet_shower |
| ② | 数千量级 | 视觉层 `MultiMesh2D`（每帧灌 instance transform，单 draw call）；逻辑仍对象数组 | godot-proposals#957（2D shmup 作者实战）；Ezcha"百万对象" |
| ③ | 数千+ 且需要精细控制 | RenderingServer canvas item + PhysicsServer2D RID 全量管理（bullet_shower 完整模式）；**注意**：不每帧向 Server 查询返回值（官方明确警告 stall） | 官方 using_servers 文档 |

额外说明：官方 demo 的 `PhysicsServer2D.body_set_collision_mask(body, 0)`（子弹间不互相碰撞）与"碰撞 mask 细分"（§5.2）是任何档位都适用的低成本收益。

---

## 9. 验证方案

1. **perf_bench A/B（必做，按 `docs/TESTING.md` §基准流程）**：每项改动前后各跑 3 次、交错执行、取中位数对比；**不要与历史数字直接比**（§2.2 的差异说明），只比同环境 A/B。改动量小时用 `Time.get_ticks_usec()` 手动计时热点函数（官方推荐）。
2. **窗口模式人工/截图 A/B**：headless 测不到渲染，渲染端改动（P0-3）必须在窗口模式目测 + 截图对比（项目 `test/` 有截图工具场景）。
3. **回归测试**：全量 41 断言场景（`docs/TESTING.md`）；`enemy_combat_test`（体碰语义）、`death_replay` 相关测试、`base_system_test`（启动路径）、`buff_effects_test`/`autoplay_test`（P1-2）。
4. **专项风险点**：池对象复活后第一帧不误伤（§5.3 幽灵事件）；P1-4 的裂纹场"窗口 GPU 512² / headless CPU 64² 路径等价"不变量（AGENTS.md 明示）。

---

## 10. 参考资料

### 10-1 官方文档（Playwright MCP 逐页阅读，stable 4.7 口径；结论适用于 4.6）

- [Performance 索引](https://docs.godotengine.org/en/stable/tutorials/performance/index.html)
- [General optimization tips](https://docs.godotengine.org/en/stable/tutorials/performance/general_optimization.html)
- [Optimization using Servers](https://docs.godotengine.org/en/stable/tutorials/performance/using_servers.html)
- [CPU optimization](https://docs.godotengine.org/en/stable/tutorials/performance/cpu_optimization.html)
- [GPU optimization](https://docs.godotengine.org/en/stable/tutorials/performance/gpu_optimization.html)
- [Scene organization](https://docs.godotengine.org/en/stable/tutorials/best_practices/scene_organization.html)
- [Autoloads versus regular nodes](https://docs.godotengine.org/en/stable/tutorials/best_practices/autoloads_versus_regular_nodes.html)
- [Godot interfaces](https://docs.godotengine.org/en/stable/tutorials/best_practices/godot_interfaces.html)
- [When and how to avoid using nodes for everything](https://docs.godotengine.org/en/stable/tutorials/best_practices/node_alternatives.html)
- [Data preferences](https://docs.godotengine.org/en/stable/tutorials/best_practices/data_preferences.html)
- [官方 demo：2d/bullet_shower（源码级阅读）](https://github.com/godotengine/godot-demo-projects/tree/master/2d/bullet_shower)

### 10-2 社区来源（子代理调研）

- 官方论坛：[Bullet Hell Optimization](https://forum.godotengine.org/t/bullet-hell-optimization/129732)、[Collision Pairs](https://forum.godotengine.org/t/collision-pairs-optimizing-performance-of-bullet-hell-enemy-hell-games/35027)、[Autoloads vs Composition](https://forum.godotengine.org/t/autoloads-vs-composition/78845)、[Enemy object pool + _on_body_entered](https://forum.godotengine.org/t/issue-with-enemy-object-pool-and-on-body-entered/114772)、[programming efficiency（Zylann）](https://forum.godotengine.org/t/programming-efficiency/25148)
- GDQuest：[Optimizing GDScript](https://gdquest.com/tutorial/godot/gdscript/optimization-code/)、[Events bus singleton](https://gdquest.com/tutorial/godot/design-patterns/event-bus-singleton/)
- GitHub：[godot#85320（compat modulate 批次分裂）](https://github.com/godotengine/godot/issues/85320)、[godot-proposals#957（2D MultiMesh 弹幕）](https://github.com/godotengine/godot-proposals/issues/957)
- 独立博客：[Object Pooling（uhiyama-lab）](https://uhiyama-lab.com/en/notes/godot/godot-object-pooling-basics/)、[Rendering a Million Objects（Ezcha）](https://ezcha.net/news/5-16-26-rendering-a-million-objects-in-godot)、[Signals & Event Bus（febucci）](https://blog.febucci.com/2024/12/godot-signals-architecture/)

### 10-3 项目内参考

- `docs/archive/2026-08-02-performance-optimization-plan.md`（前序性能优化计划，P0-P2 已落地，含基线 A/B 方法）
- `docs/ARCHITECTURE.md`（主节点树与脚本职责）
- `docs/TESTING.md`（测试矩阵与基准流程）
- `test/perf_bench.gd`（本次实测工具）

---

> **审计范围声明**：本报告基于两个并行子代理的静态代码审计 + Playwright 对官方文档/demo 的逐页阅读 + 社区一手来源调研 + 本机 `perf_bench` 实测。行号引用以当前 main 分支为准；未运行引擎窗口模式渲染测量（headless 局限见 §2.3）。所有优化建议均标注了预期收益、风险与验证路径，落地顺序建议从 P0 三项开始。
