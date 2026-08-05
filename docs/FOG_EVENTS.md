# Base Task Rotation & Fog Events (FOG_EVENTS)

> 2026-08-05：对局随机性增强双系统 —— 基地任务轮换（RefreshPoints + TaskPool）与迷雾事件
> （FogEventManager 全局单例，4 种干扰事件）。设计目标：不改变既有分数/Buff/任务经济框架，
> 仅在基地整备与对局中注入可控随机性；事件触发与效果执行经信号解耦。
> 配置：`data/balance.json` `base_task` / `fog_events` 段；文案：`data/translations.csv`。

---

## 1. 基地任务轮换系统

### 1.1 需求

- 基地/准备界面任务列表展示（既有 `base_console` 任务面板）。
- 「刷新任务」：消耗指定点数（RefreshPoints）重新随机抽取任务。
- 点数校验：不足时禁止刷新并给出提示。
- 任务池（TaskPool）随机抽取算法。

### 1.2 数据层（`autoload/game_state.gd`）

```gdscript
# 初始手牌（保持既有 id 语义，测试/存档兼容）
const MISSION_DEFS: Array[Dictionary] = [
	{"id": &"kill_5", "name": "战场清扫", "desc": "击杀 5 个敌人", "goal": 5, "kind": &"kill"},
	{"id": &"survive_180", "name": "战场生存", "desc": "存活 180 秒", "goal": 180, "kind": &"survive"},
	{"id": &"boss_1", "name": "主宰之战", "desc": "击杀 1 个 Boss", "goal": 1, "kind": &"boss"},
]
# 任务池：9 项 = 3 类 × 3 档目标（kind 决定进度来源，goal 各自生效）
const MISSION_POOL: Array[Dictionary] = [/* kill_5/15/30, survive_60/180/300, boss_1/2/3 */]
const MISSION_SLOTS := 3

var refresh_points: int = 0      # RefreshPoints 余额（随存档往返，reset_run 清零）
var REFRESH_COST := 2            # balance.json base_task.refresh_cost（钳制 ≥1）
var GRANT_PER_VISIT := 1         # balance.json base_task.grant_per_visit（进基地发放）
var _task_pool: TaskPool = null  # 每局全新洗牌序列（_init_missions 重建）
```

核心接口：

| 方法 | 语义 |
| --- | --- |
| `grant_refresh_points()` | 进基地 +`GRANT_PER_VISIT`（`base_console.show_base()` 调用） |
| `can_refresh_missions()` | `refresh_points >= REFRESH_COST`（UI 禁用按钮依据） |
| `refresh_missions() -> bool` | 扣点 + 重抽；**保留已完成未领取任务**（不吞待领奖励）；排除全部在场 id（防重号覆盖） |
| `active_mission_ids()` | 在场任务 id（面板渲染用；轮换后 ≠ `MISSION_DEFS`） |
| `_set_kind_progress(kind, value)` | 按 kind 推进全部在场任务（`add_kill`→`kill`、`_process`→`survive`、`add_boss_kill`→`boss`），轮换后 id 变化仍自动推进 |

存档：`save_run` 新增 `refresh_points` 字段；`apply_run_save` 恢复任务集合时**先清空初始手牌**
（存档任务可能含池内非手牌 id，如 `kill_15`），并按「id ∈ MISSION_POOL」校验恢复。

### 1.3 TaskPool 抽取算法（`scripts/task_pool.gd`）

无放回抽取：洗牌索引序列 + 游标推进。

```gdscript
class_name TaskPool
extends RefCounted

var _defs: Array[Dictionary] = []
var _order: Array[int] = []   # 洗牌索引序列（游标只增不减，池耗尽追加新批次）
var _cursor: int = 0

func draw(count: int, exclude_ids: Array[StringName] = []) -> Array[Dictionary]:
	# 单批内不重复；一批耗尽后若已有产出则不再跨批补足（保持无放回语义）；
	# exclude 覆盖全池时安全返回空（防死循环）
```

要点：
- **单次 draw 内不重复**（一次刷新抽到 3 个互异任务）。
- **跨 draw 尽量延迟复用**：一次 draw 消耗完当前批次后，若仍有名额且本批零产出才重洗，
  刷新后短期内不会立刻抽回刚换下的任务。
- `exclude_ids`：刷新时排除**全部在场 id**（保留任务 + 待换任务），杜绝重号。

### 1.4 UI（`scripts/base_console.gd`）

任务面板底部新增一行：`刷新点数：N`（金）+ `刷新任务（2 点）` 按钮 + 点数不足提示行
（红色，2s 自动隐藏）。点数不足时按钮 `disabled`（双重防线：UI 禁用 + `refresh_missions()`
内部校验返回 false）。

## 2. 迷雾事件系统

### 2.1 类结构

```
GameState (autoload, 唯一 autoload)
└─ FogEventManager (class_name, 服务单例; GameState.fog_events)
   ├─ EVENT_FACTORIES 事件工厂注册表（id → FogEvent 子类构造）
   ├─ 当前事件对象 (FogEvent 子类；start/tick/end 由 manager 统一驱动)
   ├─ FakeEnemy 容器 (Node2D, z=10)     ← 伪敌机实例（FakeEnemiesEvent 挂接）
   ├─ 精神错乱覆盖层 (CanvasLayer layer=2: 全屏紫调 ColorRect；ConfusionEvent 驱动)
   └─ 事件横幅 (CanvasLayer layer=30: 顶部事件名 Label)
Player (scripts/player.gd)  ← 连接 fog_event_started/ended/fog_direction_shift 应用效果
main.gd                    ← set_run_active(true/false); 返航/死亡 end_active()
```

**事件类接口（继承方向：通用 → 迷雾专门化，`scripts/game_event.gd` 基类）：**

```gdscript
class_name GameEvent     # 通用事件基类（纯生命周期接口，零系统耦合）
extends RefCounted

var context: Dictionary = {}   # 执行上下文（编排器注入；浅拷贝隔离；通用键 "request_end"）
var duration: float = 0.0      # 编排器给定的持续时间（秒）
var is_active := false         # 生命周期守卫（end 幂等、tick 仅活跃期派发）

func event_id() -> StringName  # 事件唯一 id（注册表键），必须实现
func start(context, duration)  # 编排器调用：注入上下文 + 置 active + _on_start()（勿覆盖）
func tick(delta)               # 活跃期逐帧派发 _on_tick()（勿覆盖）
func end()                     # 幂等结束：清 active + _on_end()（勿覆盖）
func request_end()             # 宽容性：复杂事件内部目标达成可主动请求提前结束
func get_ctx(key, default)     # 宽容性：简单事件一行读自定义数据（缺键回 default）
func _on_start() / _on_tick(delta) / _on_end()   # 子类实现点（模板方法）
```

```
GameEvent (scripts/game_event.gd，通用事件基底，任意系统可继承)
└─ FogEvent (scripts/fog_event.gd，迷雾专门化层：聚合迷雾上下文访问器)
   ├─ FakeEnemiesEvent (fake_enemies_event.gd)
   ├─ ConfusionEvent     (confusion_event.gd)
   ├─ BulletMalfunctionEvent (bullet_malfunction_event.gd，纯玩家状态占位)
   └─ DirectionShiftEvent    (direction_shift_event.gd，tick 周期脉冲)
```

- 迷雾上下文键约定（FogEventManager 经 context 注入，FogEvent 访问器取用）：
  `fake_container`（Node2D）/`overlay_layer`（CanvasLayer）/`overlay_rect`（ColorRect）/
  `emit_direction_shift`（Callable，转发 `fog_direction_shift` 信号）。
- 玩家状态效果（输入反转/子弹参数）一律走 manager 统一信号，**事件类不触碰 Player**。

### 2.2 触发纪律（`scripts/fog_event_manager.gd`）

```
set_run_active(true)  [main._ready]
  → 开局 first_delay 秒内不触发（保护早期对局）
  → 每 check_interval 秒掷签一次（randf() < trigger_chance）
  → 命中则按 weights 加权抽事件 → _start_event(id)
      - 单事件并发：进行中不再触发（force_trigger 同样被拒）
      - 启动 duration 一次性 Timer → 到期 _end_event()：清效果 + 信号 fog_event_ended
  → 事件结束进入 min_interval 冷却（防事件过于频繁）
树暂停（基地/过场/Buff 选择）随 autoload 继承冻结 → 不触发、效果冻结
```

信号解耦（玩家侧仅连接信号应用效果，无对管理器依赖）：

| 信号 | 消费方 | 效果 |
| --- | --- | --- |
| `fog_event_started(id, duration)` | Player | 按 id 置位（输入反转 / 子弹偏移参数） |
| `fog_event_ended(id)` | Player | 对应效果复位 |
| `fog_direction_shift(dir, hold)` | Player | `_fog_forced_dir` + `_fog_forced_hold`（hold 秒覆盖移动向量） |

### 2.3 四种事件效果

| 事件 id | 表现 | 实现 |
| --- | --- | --- |
| `fake_enemies` | 无伤害/无碰撞的幽灵敌机（纯视觉干扰） | `FakeEnemy`（Node2D + 敌机贴图 + 幽灵闪烁），不入 `enemy` 组、不进 `GameState.enemies` 注册表、无碰撞体 → 玩家子弹穿过、不参与任何对局系统；错峰入场（`spawn_interval`）+ 降入悬停带水平摇摆；事件结束统一清除 |
| `mental_confusion` | 输入方向反转 + 屏幕变色 | Player 侧：`input_dir = -input_dir`；管理器侧：全屏紫调呼吸覆盖层（layer=2，HUD 之下数值可读） |
| `bullet_malfunction` | 子弹随机角度偏移 / 射速异常 | Player `_fire`：每发出膛弹旋转 ±`jitter_deg`；`misfire_chance` 概率出 0.45× 慢速失误弹；开火间隔 ×(1±`interval_jitter`) |
| `direction_shift` | 短间隔随机方向 | `DirectionShiftEvent`（tick 累计 `shift_interval`）周期经 `manager.emit_direction_shift()` 发射随机单位方向脉冲；Player hold 秒内移动向量被替换 |

### 2.4 默认平衡参数（`data/balance.json` `fog_events` 段）

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `enabled` | `true` | 总开关 |
| `trigger_chance` | `0.35` | 每次检查的触发概率 |
| `check_interval` | `3.0` | 概率检查间隔（秒） |
| `min_interval` | `12.0` | 事件间最小间隔（上一事件结束起算） |
| `first_delay` | `25.0` | 开局保护（前 N 秒不触发） |
| `weights.*` | 各 `1.0` | 四种事件加权随机 |
| `durations.*` | 8/6/7/6 | fake_enemies 8s / mental_confusion 6s / bullet_malfunction 7s / direction_shift 6s |
| `fake_enemies.count` | `5` | 伪敌机数量 |
| `fake_enemies.spawn_interval` | `0.8` | 错峰入场间隔 |
| `bullet_malfunction.jitter_deg` | `20.0` | 出膛弹最大偏转（±20°） |
| `bullet_malfunction.misfire_chance` | `0.15` | 慢速失误弹概率 |
| `bullet_malfunction.interval_jitter` | `0.3` | 开火间隔随机扰动比例 |
| `direction_shift.shift_interval` | `0.7` | 方向脉冲周期 |
| `direction_shift.hold_time` | `0.3` | 单次强制方向时长 |

调参建议：高难/长局可把 `trigger_chance` 提到 0.45、`min_interval` 降到 8s，干扰密度随对局进程
上升；`first_delay` 建议 ≥ 20s，避免开局面包期与 Boss 入场节奏叠加。

### 2.5 生命周期与清理

- `main._ready` → `set_run_active(get_tree().current_scene == self)`：**仅真实对局（main 为
  current_scene）开启自动触发**——测试以子节点实例化 main.tscn 时保持关闭，防止随机迷雾
  事件破坏测试断言确定性（2026-08-05 健壮性审计：曾致 elite_turret_event_test 得分断言
  偶发失败）；需测迷雾的用例显式 `set_run_active(true)`。
- `main._exit_tree` → `set_run_active(false)`（强制结束）。
- `main._start_homecoming` / `_on_player_died` → `end_active()`：返航/死亡清伪敌机、覆盖层、
  玩家效果信号复位，继续出击后干净开局。
- 事件到期（Duration Timer）自动 `_end_event()`：`active_event.end()`（幂等）+ 信号复位。
- 健壮性（2026-08-05 审计，事件类为后续所有事件的唯一入口）：`GameEvent` 重复 start 自愈
  （先清理再重启）、end 幂等、tick 仅活跃期派发、context 浅拷贝隔离；`FogEvent` 访问器缺键
  返回 null，子类 `_on_start` 判空降级（空转/自行 end，不崩）；编排器结束 Timer 先行启动
  （事件 start 抛错也不会永久挂死）、空注册表/非 Callable 条目防御、`try_trigger` 落空返回
  false；新增 14 项健壮性断言（fog_event_test §8/§9）。

### 2.6 扩展新事件（事件类接口用法）

**宽容性（2026-08-05，调研 Godot 官方最佳实践 + 社区后设计）**：同一接口同时接受简单与复杂事件
——官方 OOP 实践（松耦合/依赖注入/Callable 注入/鸭子类型接口）与社区状态效果系统共识
（Logic=RefCounted / Data=context / Manager=Node 三层分离）已验证当前架构。事件形态无最小
负担：

- **简单事件**：只实现 `event_id()`（如 `BulletMalfunctionEvent`），效果在 player.gd 信号
  match 里；或一行 `get_ctx(&"key", 默认值)` 读自定义数据。零生命周期代码。
- **复杂事件**：`_on_start`（经 `fake_container()`/`overlay_layer()` 挂视觉、读 balance 子
  参数）+ `_on_tick`（周期效果）+ `_on_end`（幂等清理）；内部目标达成可 `request_end()`
  主动提前结束（经 context `"request_end"` 回调，编排器注入；缺回调降级按 duration 结束）。

新增一个迷雾事件的完整成本：

1. **新建 `FogEvent` 子类**（如 `scripts/my_event.gd`）：
   - 纯视觉/实体/周期事件（推荐接口化收益最大）：实现 `event_id()` + `_on_start()`
     （经 `fake_container()`/`overlay_layer()` 等访问器挂接视觉，读自己的 balance 子参数）+
     可选 `_on_tick()`（周期效果，勿自持 Timer）+ `_on_end()`（幂等清理）。示例参照
     `fake_enemies_event.gd` / `confusion_event.gd` / `direction_shift_event.gd`。
   - 纯玩家状态事件（如新 debuff）：只需 `event_id()`（参照 `bullet_malfunction_event.gd`），
     效果在 player.gd 的 `_on_fog_event_started/ended` 各加一个 match 分支。
2. **注册**：`FogEventManager.EVENT_FACTORIES` 加一行
   `&"my_event": func() -> FogEvent: return MyEvent.new()`。
3. **配置与文案**（可选）：`fog_events.durations`/`weights` 与子参数段（balance.json）、
   `FOG_EVENT_MY_EVENT_NAME`（translations.csv）；`gen_balance_map.py` 重新生成
   `docs/BALANCE_MAP.md`。

**非迷雾系统复用事件机制**：直接继承 `GameEvent`（不经过 FogEvent），即可获得统一的
start/tick/end 生命周期与幂等守卫；系统专属上下文经自己的中间层基类提供访问器（参照
`FogEvent`），编排器按 `GameEvent` 生命周期契约驱动即可。

约束：事件类不直接触碰 Player（状态效果走编排器统一信号）；`end()` 幂等由 `GameEvent`
守卫（返航/死亡会在 duration 前提前调用）；duration 计时由编排器统一负责。

## 3. 测试

- `test/base_task_refresh_test.tscn`：初始手牌 / 点数校验 / 重抽语义 / kind 进度 / 保留已完成
  未领取 / 存档往返 / reset_run / TaskPool 算法（无放回、排除项、全排除防死循环）。
- `test/fog_event_test.tscn`：管理器挂载与信号 / 单事件并发 / Duration 到期自动清除 /
  MinInterval 冷却门控 / 概率触发（try_trigger）/ 4 种效果与玩家信号联动 / 返航清除 /
  事件类健壮性（§8 生命周期守卫 + §9 编排器防御）/ 事件宽容性（§10：复杂事件 request_end
  主动提前结束、极简事件仅 event_id 走通全生命周期、get_ctx 缺键回默认）。
- 确定性：测试内 `TRIGGER_CHANCE = 0.0`（防随机触发干扰断言），全部走 `force_trigger` /
  `try_trigger` 显式路径；时长断言用实例变量覆盖（`EVENT_DURATIONS`/`MIN_INTERVAL` 等），不动
  `balance.json`。
