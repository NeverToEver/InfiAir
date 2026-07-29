# 轰炸编队事件（Formation Strike Event）设计文档

本文档是「阵列轰炸编队」随机事件的单一事实源：触发优先级、状态机、编队/炸弹行为、数值与测试要点。
实现时改动本事件相关内容必须同步更新本文档。与精英炮塔事件的对位关系见 `docs/ELITE_TURRET_EVENT.md`。

---

## 1. 概念

一支 3/4/5 架攻击机组成的编队（按难度）自屏顶外进入，保持楔形编队**靠近**（下降至玩家活动区上缘），
随后整体**转航向**（编队 90° 转向改为水平横穿），在横穿段逐架**投弹**，投完从侧缘加速离场。
炸弹带引信与落点预警圈，引爆造成 AoE 伤害。全部战机可在投弹前被玩家击坠（击坠有分，全歼有小额奖励）。

定位：**最低优先级**的随机遭遇——它不抢占任何调度权，只在「无 Boss、无精英炮塔事件」的空档期搭车出现；
事件短（约 12s）、不冻结 Boss 调度。重制版新增内容（原作无此机制，不计入移植差距清单）。
**2026-07-29 修订**：事件改为占用波次槽——运行期间暂停普通波次（spawner `_waves_paused` 钩子，结束/打断恢复），
触发时清零 spawner 特殊槽计数（与精英波/Boss 同槽），降低叠加压力。

## 2. 触发与优先级

优先级链（spawner `_process` 每 tick 顺序检查，前者启动则后者本 tick 跳过）：

1. **Boss**（分数/时间门槛，最高优先）
2. **精英炮塔事件**（`elite_turret_event`，30s 重型事件，冻结 Boss + 暂停波次）
3. **轰炸编队事件**（本事件，最低优先）：仅当 ① Boss 未激活（未预警/未在场）② 精英炮塔事件 `is_active() == false`
   ③ 自身 IDLE 且冷却结束 ④ 分数 ≥ `min_score` 时，每隔 `trigger_interval` 秒以 `trigger_chance` 概率掷签启动。

与精英炮塔事件的关键差异（低优先级的具体语义）：

- **不冻结 Boss 调度**：Boss 到期照常触发（警告+入场约 2s+，此时编队已近离场，炸弹有预警圈可躲，叠加风险可控）；
  但**事件期间暂停普通波次**（2026-07-29 修订：占用波次槽，与精英炮塔事件共用 `_waves_paused` 钩子；
  为避免两事件暂停钩子互相提前恢复，精英炮塔事件在编队激活期间不会启动）。
- **可被返航打断**：`Main._start_homecoming()` 调用事件 `abort()`（同母舰收回语义），编队立即解散离场，无结算并恢复波次；已投放的炸弹为独立实体，自然存续（语义同敌弹）。

## 3. 状态机

```
IDLE → FORMATION_ENTER（靠近，时长由位移/approach_speed 推导，约 1.5s）→ FORMATION_TURN（转航向 turn_time 1.2s）
     → BOMBING_RUN（横穿投弹，长度按编队规模 2.6–3.8s）→ FORMATION_EXIT（离场 EXIT_TIME 1.5s）→ IDLE（冷却 cooldown）
```

- **FORMATION_ENTER**：编队锚点自屏顶外 `(x0, view.top - 120)` 垂直下降至接近高度 `approach_y`（view.top + 260），
  各机保持楔形偏移（长机居中，僚机后掠 ±55px 递增）。`x0` 在视野中部 40%–60% 随机。
  进场时 `CommOverlay` 播一句警告台词（`FBQ_WARN`）。
- **FORMATION_TURN**：锚点减速，编队朝向在 1.2s 内从 +y 平滑旋转到 ±x（朝较远侧缘方向），
  各机偏移量随编队朝向旋转（楔形整体转向，僚机划出小弧）。
- **BOMBING_RUN**：编队以 `run_speed` 水平横穿；自转向完成起，每架机按 `bomb_interval` 交错投弹
  （长机先投，僚机依次错开），每架投 `bombs_per_craft` 枚（间隔 0.4s）。投弹点即当前位置正下方。
- **FORMATION_EXIT**：投弹完毕或穿出侧缘后，编队朝侧缘外加速离场（1.5s），离场后回 IDLE 并进入冷却。
- **提前结束**：全部战机被击坠 → 立即结算全歼奖励 → FORMATION_EXIT（剩余节点清理）。

## 4. 实体

### 4.1 编队战机（`scripts/formation_craft.gd`，Area2D）

- 注册 `enemy` 组与 `GameState.enemies`（玩家子弹/激光可命中），死亡/离场时注销（同 TurretBattery 模式）。
- 贴图复用 `assets/sprites/enemy_ship_2.png`（高速机型，视觉贴合"攻击机"），scale 0.9。
- HP = `craft_hp_base` × `GameState.enemy_hp_multiplier()`；击坠得分 `craft_score`（`add_score` 内乘难度倍率）。
- 自身无 AI：位置 = 编队锚点 + 旋转后偏移，rotation = 编队朝向 + PI/2（机头朝航向），由事件 `_process` 驱动。
- 被击坠：`Explosion.spawn_at()` + 击坠音，编队剩余继续；投弹序列跳过已毁机。

### 4.2 炸弹（`scripts/formation_bomb.gd`，Area2D）

- 碰撞层 4（`enemy_bullet`）/ mask 1（`player`），但不走命中即毁逻辑：引信制。
- 投放时继承编队水平速度 ×0.35 + 垂直下落 `bomb_fall_speed`；引信 `bomb_fuse` 1.2s 后引爆。
- **预警**：弹体带脉冲辉光（红橙，8Hz），外挂一圈随引信剩余时间收缩的警示环（Line2D，半径 0.9×AoE → 0.15×AoE）。
- **引爆**：`Explosion.spawn_at(scale=0.9)` + 爆炸音；对 `player_hitbox` 做距离判定（≤ `bomb_radius` 且玩家非无敌 →
  `take_damage(bomb_damage)`）。AoE 只伤玩家，不伤敌机（与敌方弹丸语义一致）。
- 出界/引爆后 queue_free。不受慢速力场等玩家 buff 影响（非 enemy 注册实体，语义同敌弹）。

## 5. 数值（`data/balance.json` 新增顶层 `formation_strike_event` 段；脚本同名默认值为缺键回退，两者保持一致）

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `min_score` | 500 | 触发分数门槛（低于炮塔事件 800，更早可见） |
| `trigger_interval` | 40.0 | 掷签间隔（秒） |
| `trigger_chance` | 0.30 | 每次掷签概率 |
| `cooldown` | 50.0 | 事件结束冷却 |
| `craft_counts` | `{easy:3, medium:4, hard:5}` | 编队规模 |
| `craft_hp_base` | 60 | 单机 HP 基数（×难度 HP 倍率） |
| `craft_score` | 200 | 击坠基础分 |
| `approach_speed` | 260.0 | 靠近段下降速度 |
| `approach_y` | 260.0 | 接近高度（相对视野上缘偏移） |
| `turn_time` | 1.2 | 转航向时长 |
| `run_speed` | 340.0 | 横穿段速度 |
| `bomb_interval` | 0.35 | 各机投弹交错间隔 |
| `bombs_per_craft` | 2 | 每架投弹数 |
| `bomb_fall_speed` | 300.0 | 炸弹下落速度 |
| `bomb_fuse` | 1.2 | 引信（秒） |
| `bomb_damage` | 20 | AoE 伤害（玩家 100 HP） |
| `bomb_radius` | 120.0 | AoE 半径 |
| `reward_all_clear` | 200 | 全歼的基础奖励分（任一活动阶段全歼即发，编队立即提前 EXIT） |

（`EXIT_TIME` 1.5s、同机投弹间隔 0.4s、楔形僚机步进 55px 为脚本 `const` 常量级，不进 balance.json。）

## 6. i18n

新增 1 个键（中英双列）：`FBQ_WARN`「侦测到轰炸编队，正在接近」/ `"Bomber formation inbound"`。
复用 `CommOverlay`（同精英炮塔事件，layer=12 左下通讯浮层）。

## 7. 接入点

- `main.gd _ready()`：创建 `FormationStrikeEvent` 节点挂 Main 下（清场/测试遍历可见），登记给 `spawner._formation`。
- `spawner.gd`：新增 `_formation` 引用与触发检查（在精英炮塔事件检查之后、同 `_process` 尾部）；
  触发参数读 `formation_strike_event.*`（`_apply_balance` 注入）。
- `main.gd _start_homecoming()`：调 `_formation.abort()`（编队解散离场，无结算，冷却照计）。
- 动态实体（战机/炸弹）一律挂 Main 下；返航清场 `child is Enemy or child is Bullet` 不涉及本事件实体
  （由 `abort()` 与事件自身生命周期负责）。玩家死亡不特判（与精英炮塔事件一致，结算后场景重载自清）。

## 8. 测试要点（`test/formation_strike_event_test.tscn`）

镜像 `elite_turret_event_test` 结构，全程真实 Timer 等待：

1. **触发门槛**：Boss 激活 / 精英炮塔事件 active / 冷却中 / 分数不足 四种情况 `can_trigger()` 为 false。
2. **状态推进**（缩短配置强制 `start()`）：ENTER→TURN→BOMBING_RUN→EXIT→IDLE 依次到达；
   战机注册 `GameState.enemies`；转向后才有炸弹生成；投弹数 = 存活机 × `bombs_per_craft`（击坠机跳过）。
3. **炸弹**：预警环存在且随引信收缩；引爆后节点释放；玩家站半径内引爆掉血（无敌时不掉）。
4. **击坠**：`take_damage` 致死 → `GameState.enemies` 注销 + 得分增加；全歼 → 奖励分 + 提前 EXIT。
5. **打断**：`abort()` → 实体清理、回 IDLE、冷却生效。
6. **无 Timer/节点残留**（事件树与 Main 子节点计数）；结束清理 `user://` 持久化。

回归清单：`smoke_test`、`elite_turret_event_test`（同调度器改动回归）、`enemy_combat_test`、
`base_system_test`、`--quit-after 300`。

## 9. 文档同步

`AGENTS.md`（架构树/脚本清单/测试清单/balance.json 顶层段）、`docs/PORTING_PARITY.md`
（新增内容条目，同炮塔事件口径）、本文件（单一事实源）。
