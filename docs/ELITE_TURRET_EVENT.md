# 偶遇精英单位·多自动索敌炮台事件 — 设计文档

> 状态：**已实现**（2026-07-28 落地并通过全量验证，实现细节见文末「实现说明与特性」）。数值均采样自 `data/balance.json` 与现有脚本；新增参数写入 `data/balance.json` 顶层区块 `elite_turret_event`，脚本内仅保留同名回退默认值（对齐项目数值约定）。

---

## 1. 采样分析结果摘要

### 1.1 现有弹药类型清单

| 弹药 | 来源 | 速度 | 伤害 | 行为特征 |
| --- | --- | --- | --- | --- |
| 单发直射弹 | 普通敌机 `enemies.bullet_speed` | 420 | 12 | 直线匀速，红色 Polygon2D 弹丸 |
| 扇形散射弹 | 普通敌机 `enemies.spread_bullet_speed` | 340 | 10 | 以 `spread_fan_step≈0.314rad` 为步进的多弹扇面 |
| 激光长弹 | 普通敌机 `enemies.laser_bullet_speed` | 720 | 20 | 高速直线长弹（视觉上拉伸的弹丸） |
| Boss 扇形弹 | `boss.fan_bullet_speed` | 380 | 14 | 低速大扇面压制 |
| Boss 追踪弹 | `boss.homing_bullet_speed` | 300 | 12 | `homing=true` + `homing_time` 时限内以 `lerp_angle(4.0·dt)` 转向玩家 |
| Boss 狙击弹 | `boss.sniper_bullet_speed` | 650 | 21 | 高速单发精准弹 |
| Boss 十字弹 | `boss.cross_bullet_speed` | 260 | 12 | 低速四向/十字展开 |
| 狂暴快照激光/环弹 | `boss.enrage` | 820 / 240 | 21 / 12 | 狂暴阶段沿预设路径的齐射 |
| 母舰机炮弹 | `mothership.gatling` | 1080 | 8 | 玩家侧扫射弹幕 |
| 母舰导弹 | `mothership.missile` | 600 | 80 + 溅射 20/r80 | 玩家侧多目标追踪 + AoE |
| 激光束（Buff） | `buffs.laser_beam` | —（线段判定） | 10/0.1s tick | 玩家 Buff 武器，非弹丸 |

所有弹丸统一走 `scenes/bullet.tscn` + `GameState.bullet_pool.fire()`，阵营由 `setup()/activate()` 区分；追踪行为由 `bullet.gd` 的 `homing`/`homing_time` 字段实现（转向速率 4.0 rad 级插值）。**本事件弹药全部复用上述敌侧弹种，不新增弹药类型。**

### 1.2 普通敌方单位血量

`balance.json → enemies.types[].hp`（中难度基准值）：

- 范围区间：**55 ~ 130 HP**（四型分别为 75-85 / 55-65 / 110-130 / 65-75）
- 典型值：**约 80 HP**（最常见的 75-85 区间中值）
- 难度系数：`difficulty.hp` 易 ×0.75 / 中 ×1.0 / 难 ×1.5

（参考：精英 150-230；Boss 800 × hp_mults。本事件炮台按“普通单位”量级取血。）

### 1.3 Boss 击杀得分奖励

- 结算入口：`boss.gd._die()` → `GameState.add_boss_kill(score_scale)`
- 得分：`add_score(int(500.0 × score_scale))`，`score_scale` 通常为 1.0 → **基础 500 分**
- 结算方式：`add_score()` 内统一乘难度分数倍率（**易 ×1 / 中 ×2 / 难 ×3**，即实际入账 500 / 1000 / 1500）
- 附带效果（本事件**不**复用）：RP 奖励、boss_kills 计数、难度成长系数

### 1.4 敌舰美术风格关键词

采样自 `scripts/tools/generate_enemy_sprites.py`（晶体棱镜风格生成器）与 `assets/sprites/`：

- **造型**：左右严格镜像对称；几何切面（facet）拼合的晶体机身；锐利多边形轮廓、刀翼/爪翼/叉形机头；机头朝上（场景根 rotation=PI 翻转）
- **配色**：舰体为深紫黑水晶分段面 `HULL_A~D = (22,18,34)~(62,52,92)`；接缝近黑 `(10,8,18)`；棱线描边淡紫 `(150,140,185)`
- **阵营点缀色**：普通=猩红 `(255,72,56)`；精英=品红 `(255,64,190)`；Boss=琥珀/紫罗兰/红宝石
- **光影**：双层绘制——body 实体面 + glow 霓虹层；glow 层高斯模糊成光晕后与本体合成；能量核心=点缀色圆 + 白色高亮内芯；尾部引擎光斑椭圆
- **工艺**：4× 超采样抗锯齿；霓虹线沿翼前缘/结构棱走线

---

## 2. 打击航母视觉重设计

### 2.1 定位

背景式巨型单位（非 Boss，不进入 Boss 轮换），从屏幕上缘之外的深空缓缓降入，悬停于战场后方中上层（参考母舰 `hover_y=270` 的层级感，但更靠后、更大，视觉上占屏宽 60% 以上），作为炮台展开的“舞台”。舰体本体**不可被攻击**（无碰撞层），只有升起的炮台是可摧毁实体。

### 2.2 造型描述

- **整体轮廓**：拉长的六边梭形舰体，纵向长约为 Boss 贴图（410px）的 1.6~1.8 倍；沿用 Boss-3「巨柱」的六边要塞切面语言，但横向展开——中央主舰体为高耸六棱柱，左右各伸出一段阶梯状收缩的“甲板翼台”，翼台顶面即炮台基座平台。
- **舰桥**：主舰体顶部一座三层收分的六边塔楼（塔尖晶体切面，同 boss_3 顶部晶面手法），塔楼正面一条横向品红霓虹“观察缝”。
- **炮台基座**：每侧翼台顶面预留 1~2 个八角形凹槽基座（未升起时为闭合的装甲盖板，接缝线 `SEAM` 色勾边）；升起时盖板沿接缝旋开，炮塔为小型六棱柱 + 单管晶体炮身，炮口内嵌能量核心。
- **舰尾**：三组大型引擎光斑（中央大、两侧小，椭圆辉光），启动与撤退时亮度拉高。

### 2.3 配色与材质要点

- 舰体切面沿用 `HULL_A~D` 深紫黑晶体系，切面数多于普通机、与 Boss 同级，体现“重型装甲”。
- 点缀色采用**精英品红 `(255,64,190)`**（事件定性为“精英遭遇”，与普通猩红、Boss 暖色区分），能量核心用 `ELITE_CORE (215,135,255)`。
- 接缝/棱线/霓虹/光晕工艺与生成器完全一致（`SEAM`/`RIM`/模糊 glow 合成），保证同屏不跳色。
- 甲板装甲板用 `HULL_C/D` 亮面表现水平承力面，垂直面用 `HULL_A/B` 暗面，形成俯视体积感。

### 2.4 关键视觉标识

- **派系徽记**：主舰体正面中央一枚六边形霓虹徽记（品红外框 + 白色内芯，能量核心同款绘制法），与精英单位品红体系呼应，作为“精英舰队旗舰”的识别符号。
- **灯光布局**：沿两侧翼台前缘各一条连续品红霓虹走线（同 boss_1 翼前缘手法）；每个炮台基座一圈八角霓虹环——**基座环即状态灯**：待命暗红 → 升起充能品红高亮 → 炮台被毁对应环熄灭。玩家一眼可读剩余炮台数。
- **事件开场**：舰体自屏幕上方深空淡入 + 下压，引擎光斑渐亮，伴随一次低强度震屏（复用 `effects.shake.mothership=4.0` 量级）。

### 2.5 概念草图关键词（供生成器/美术参考）

`elongated hexagonal prism carrier, stepped flight-deck wing platforms, three-tier hexagonal command tower, octagonal turret wells with armored lids, dark violet crystal facets, magenta neon edge lighting, glowing engine cluster, crystalline prism style, top-down, mirrored symmetry, supersampled`

实现时建议直接在 `generate_enemy_sprites.py` 增加 `strike_carrier()` 与 `turret()` 两个绘制函数，复用 `Ship` 类的 facet/seam/rim/neon/energy_core/engine 原语，画布约 1200×700（舰体）与 96×96（炮塔），机头（舰首）朝上。

---

## 3. 炮台机制与各难度配置

### 3.1 通用机制

- **升起动画**：事件触发后航母入场（约 2s 悬停到位）→ 基座盖板旋开、炮塔升起并充能（约 1.5s）→ **充能完毕瞬间开始 30s 倒计时**。
- **索敌**：每座炮台独立以玩家为目标旋转（`lerp_angle` 缓动转向，转向速度设上限，营造“机械转台”感）；开火朝向 = 炮塔当前朝向 + 随机散布角，而非精确指向——即“**弱锁定**”。
- **弱锁定参数（新增配置 `elite_turret_event.weak_lock`）**：
  - 追踪弹转向速率降为 **1.5**（现有追踪弹为 4.0），`homing_time` 仅 0.6s；
  - 直射类弹药附加 **±7°** 出膛散布；
  - 命中率目标：静止玩家约 50-60% 被命中，保持横向机动即可稳定规避。
- **开火节奏**：每座炮台独立计时，间隔 2.0~2.4s（对齐普通敌机 `fire_interval` 量级），弹药从该炮台的预设弹药池中**按预设序列轮换**（序列见下表，落地时可在配置中改为 `random`）。
- **可摧毁性**：每座炮台为独立 Area2D 实体（碰撞层 3=enemy，加入 `enemy` 组并注册 `GameState.enemies`），受击闪白、独立血条（小型分段条，复用 `ui_segmented_bar` 风格）、被毁时 `Explosion.spawn_at()` + 对应基座环熄灭。

### 3.2 各难度配置

血量取普通敌机典型值 80，按难度 `hp` 系数微调并取整；弹药全部复用第 1.1 节敌侧弹种。

| 难度 | 炮台数 | 单台血量 | 弹药池（轮换序列） |
| --- | --- | --- | --- |
| 易 | 3 | **60**（80×0.75） | 单发直射弹(420/12) → 扇形散射弹(340/10，3 发扇面) → 单发直射弹 |
| 中 | 4 | **80**（典型值） | 单发直射弹 → 扇形散射弹 → 激光长弹(720/20) → 弱追踪弹(300/12) |
| 难 | 5 | **120**（80×1.5） | 扇形散射弹(5 发扇面) → 激光长弹 → 弱追踪弹 → Boss 狙击弹(650/21) → 单发直射弹 |

配置落点示例（`balance.json` 新增）：

```json
"elite_turret_event": {
	"duration": 30.0,
	"boss_resume_delay": 4.0,
	"turret_hp_base": 80,
	"turret_counts": { "easy": 3, "medium": 4, "hard": 5 },
	"fire_interval": [2.0, 2.4],
	"weak_lock": { "homing_turn_rate": 1.5, "homing_time": 0.6, "spread_deg": 7.0 },
	"reward_score": 500
}
```

### 3.3 平衡校核

30 秒窗口内玩家基础 DPS ≈ 10 伤 / 0.15s ≈ 67/s：全歼所需总输出为 180（易）/ 320（中）/ 600（难），对应纯命中时间 2.7s / 4.8s / 9.0s。考虑走位与瞄准损耗，中难度约需 1/3 的事件时间专注输出，难度梯度主要由“炮台数量×分散站位”和弹药密度提供，与现有难度曲线（中 ×1、难 ×1.5 HP）一致。

---

## 4. 沉浸式台词系统

### 4.1 台词池（10 句，中英双语；键名 `ETQ_1`~`ETQ_10`，写入 `data/translations.csv`）

| 键 | zh | en |
| --- | --- | --- |
| ETQ_1 | “炮台受损？不过是擦伤。继续压制！” | "Turret damage? A scratch. Keep firing!" |
| ETQ_2 | “一座炮台沉默了就慌成这样？废物！” | "One turret down and you panic? Worthless!" |
| ETQ_3 | “那是舰队最贵的火控核心——你在烧钱，虫子！” | "That's the fleet's priciest fire-control core — you're burning money, insect!" |
| ETQ_4 | “损失过半……不可能，火控网络是完美的！” | "Half the battery gone… Impossible. The fire-control grid is flawless!" |
| ETQ_5 | “把那架战机从天上抹掉！现在！” | "Erase that fighter from my sky! Now!" |
| ETQ_6 | “甲板起火？关闭损管，把能量全压进炮塔！” | "Deck fire? Kill damage control, shunt all power to the turrets!" |
| ETQ_7 | “只剩最后一座了……指挥官，请求撤退许可！” | "Only one turret left… Commander, requesting permission to withdraw!" |
| ETQ_8 | “撤退？本舰从不撤退——等等，你在对谁说话？！” | "Withdraw? This ship never retreats — wait, who are you talking to?!" |
| ETQ_9 | “全炮位失联……这不在任何作战手册里。” | "All gun positions silent… this isn't in any manual." |
| ETQ_10 | “记住这张脸，小虫子。下次见面，是你的葬礼。” | "Remember this face, little insect. Next time we meet, it's your funeral." |

### 4.2 绑定与播放逻辑

- 事件开始时从 10 句中**无放回随机抽取 3 句**，按顺序绑定到三个进度节点：
  1. 摧毁数 ≥ ⌈总数/3⌉（至少 1 座）→ 播放第 1 句；
  2. 摧毁数 ≥ ⌈总数×2/3⌉ → 播放第 2 句；
  3. 全部摧毁 → 播放第 3 句（事件成功结算前播放）。
- 事件失败（超时撤退）不播放绑定台词，改播固定撤退台词（可另设 `ETQ_RETREAT`，不在 10 句池内）。
- **呈现形式**：屏幕左下角通讯浮层——六边切角头像框（复用 `ui_chamfered_panel`，品红描边 + 航母徽记剪影）+ 打字机字幕，显示 3.5s 后淡出；不暂停游戏（`process_mode` 跟随对局，不进入暂停态）。新台词顶掉未播完的旧台词。
- 附带一声短促通讯噪音 SFX（复用现有音效池，不新增资产）。

---

## 5. 时间轴与奖励结算

### 5.1 时间轴流程

```text
t=0.0s   事件触发（互斥检查通过，见第 6 节）
t=0.0 → 2.0s   航母自屏幕上方降入、悬停到位（引擎渐亮 + 轻震屏）
t=2.0 → 3.5s   基座盖板旋开，炮塔升起充能（不可被攻击：monitoring=false）
t=3.5s   ★ 30s 倒计时开始（HUD 顶部出现事件计时条 + 剩余炮台数图标）
t=3.5 → 33.5s  炮台开火 / 玩家摧毁炮台 → 进度台词按节点播放
分支 A: 全部炮台摧毁（t ≤ 33.5）→ 成功结算
分支 B: 倒计时归零仍有炮台存活 → 失败结算
```

### 5.2 结算伪代码

```gdscript
func _on_all_turrets_destroyed() -> void:
	_play_commander_line(3)                    # 第 3 句绑定台词
	GameState.add_score(500)                  # 复用 Boss 击杀得分：
	                                          # 基础 500，add_score 内统一乘
	                                          # 难度倍率（×1/×2/×3）→ 500/1000/1500
	# 不复用 add_boss_kill() 的 RP、boss_kills 计数与难度成长
	_carrier_retreat(victorious := false)     # 航母受创撤离（冒烟+慢速）
	_schedule_boss_resume()                   # 见第 6 节

func _on_event_timeout() -> void:
	for turret in _living_turrets:
		turret.cease_fire_and_retract()       # 炮塔收回盖板，弹药不再产生
	_play_retreat_line()                      # 固定撤退台词，无奖励
	_carrier_retreat(victorious := true)      # 航母完整撤离（加速上升淡出）
	_schedule_boss_resume()

func _carrier_retreat(victorious: bool) -> void:
	# 复用 Boss escape 参数族：start_speed/accel 上升离场
	# 存活敌弹保留自然出界销毁（不触发玩家 bullet_clear）
```

事件期间普通波次刷怪**暂停**（对齐 Boss 战期间 spawner 的压制行为），母舰不受影响。

---

## 6. 与 Boss 事件互斥的状态机

### 6.1 设计约束

Boss 调度在 `spawner.gd`（`boss_score_step=1500` 分数步进 + `boss_time_limit=90s`）。互斥要求：两者不同屏；Boss 触发被冻结至多一次，不累积。

### 6.2 状态机

```text
              ┌────────────────────────────────────────┐
              ▼                                        │
   ┌──── IDLE ────┐   满足炮台事件触发条件      ┌───────┴───────┐
   │ (boss 正常调度)│ ───────────────────────▶  │ CARRIER_ENTER  │
   └──────┬───────┘                             └───────┬───────┘
          │ boss 触发条件就绪                            │ 入场+升起完成
          │ 且事件系统 IDLE                              ▼
          ▼                                     ┌── TURRET_ACTIVE ──┐
   (原 Boss 流程)                                │ 30s 倒计时         │
                                                └──────┬───────┘
                                                       │ 成功/失败
                                                       ▼
                                              ┌── CARRIER_EXIT ──┐
                                              │ 撤退动画          │
                                              └──────┬───────┘
                                                     ▼
                                              ┌── BOSS_DELAY ──┐
                                              │ 固定间隔 4.0s   │
                                              │ (boss_resume_delay)
                                              └──────┬───────┘
                                                     ▼
                                              回 IDLE；若存在被冻结的
                                              Boss 触发 → 立即触发一次
                                              并清除冻结标记（不累积）
```

### 6.3 规则说明

- **触发互斥**：炮台事件的触发检查与 Boss 触发检查同帧竞争时，Boss 优先（Boss 是分数里程碑承诺）；只有 Boss 未处于「预警/入场/战斗中」时才允许炮台事件启动。**2026-07-29 补充**：轰炸编队事件激活期间炮台事件亦不启动——两事件共用 spawner `_waves_paused` 波次暂停钩子，避免一方结束时提前恢复另一方的暂停。
- **冻结逻辑**：事件进入 `CARRIER_ENTER` 时置 `_boss_frozen = true`；此期间 spawner 的 Boss 分数步进若到期，不触发 Boss，而是置 `_boss_pending = true`（仅记录一次，重复到期覆盖为同一标记——不累积）。
- **恢复逻辑**：事件进入 `BOSS_DELAY` 结束时：若 `_boss_pending` 为真，立即启动 Boss 预警流程并清除 `_boss_pending`；`_boss_frozen` 同时复位。若事件结束时 Boss 条件尚未到期，则恢复原分数步进计时，不产生任何补偿。
- **边界**：事件期间玩家跨越 Boss 分数步进是常态（事件奖励 500~1500 分），冻结标记保证 Boss 在航母离场 4s 后才登场，避免两个大型单位同屏叠加弹幕。
- **失败同样解冻**：成功/失败不影响互斥恢复路径，仅影响奖励。
- 落地时在 `test/` 增加 `elite_turret_event_test.tscn`：断言 30s 计时、三个台词节点、奖励入账（含难度倍率）、Boss 冻结/恢复与单次不累积语义。

---

## 附：落地清单（实现阶段参考）

1. `data/balance.json` 新增 `elite_turret_event` 区块（脚本内保留同名回退默认值）。
2. `data/translations.csv` 新增 `ETQ_1`~`ETQ_10`、`ETQ_RETREAT`、事件 HUD 文本键（中英双语）。
3. `scripts/tools/generate_enemy_sprites.py` 新增 `strike_carrier()` / `turret()` 生成函数，产出 PNG 至 `assets/sprites/`。
4. 新增 `scripts/strike_carrier.gd`（入场/悬停/撤退导演）、`scripts/turret_battery.gd`（炮塔实体：索敌/开火/受击）、通讯浮层 UI（复用 `ui_theme.gd` + `ui_chamfered_panel.gd`）。
5. `scripts/spawner.gd` 增加事件触发检查与 Boss 冻结/恢复钩子；`scripts/main.gd` 登记事件编排。
6. 新增 `test/elite_turret_event_test.tscn`；返回层级若有新页面需同步 `docs/EXIT_FLOW.md`（本设计无新页面，预计不需要）。

---

## 实现说明与特性（2026-07-28 落地）

### 文件落点

| 资产/文件 | 内容 |
| --- | --- |
| `data/balance.json → elite_turret_event` | 全部可调参数：时长/入场/升起/恢复间隔、触发条件（`min_score=800`、`trigger_interval=45s`、`trigger_chance=0.35`、`cooldown=60s`）、`turret_hp_base=80`、`turret_counts`（易 3/中 4/难 5）、`fire_interval=[2.0,2.4]`、`weak_lock`、`ammo_sequences`（按难度的弹药轮换序列）、`reward_score=500`、`carrier`（悬停高度/撤退参数/震屏）。脚本内保留同名回退默认值。 |
| `data/translations.csv` | `ETQ_1`~`ETQ_10`、`ETQ_RETREAT`、`ETV_TITLE`、`ETV_TURRETS`（中英双语，Godot 重新导入生成 `.translation`）。 |
| `scripts/tools/generate_enemy_sprites.py` | 新增 `strike_carrier()`（1200×700）与 `turret()`（96×96）绘制函数，复用 `Ship` 原语；`TURRET_WELLS` 基座坐标表与运行时 `StrikeCarrier.SOCKETS` 一一对齐。产出 `assets/sprites/strike_carrier.png`、`elite_turret.png`。 |
| `scripts/strike_carrier.gd`（`class_name StrikeCarrier`） | 航母导演：ENTER（2s 缓出降入 + 淡入 + 到位震屏）→ HOVER（±6px 慢浮动）→ RETREAT（复用 Boss escape 参数族：start_speed 120 / accel 420，受创 ×0.55 慢速 + 变暗 + 甲板爆点）。无碰撞层，本体不可被攻击。5 个八角基座环为 Line2D 状态灯：充能品红高亮 / 被毁熄灭（待命暗红环直接烘焙在贴图上）。 |
| `scripts/turret_battery.gd` + `scenes/turret.tscn`（`class_name TurretBattery`） | 炮台实体：Area2D 碰撞层 3（enemy），注册 `enemy` 组与 `GameState.enemies`（玩家弹/母舰火力/爆炸弹 buff 自动可命中）。升起动画 TRANS_BACK 缩放入场，期间 `monitoring=false` 不可受击；充能完毕 `activate()` 后才可被攻击。独立血条为品红 SegmentedBar（8 段），受击闪白，被毁 `Explosion.spawn_at()` + 震屏 + 基座环熄灭。超时 `cease_fire_and_retract()` 停火收回并自释放。 |
| `scripts/comm_overlay.gd`（`class_name CommOverlay`） | 左下角通讯浮层（CanvasLayer layer=12）：ChamferedPanel 品红描边 + 打字机字幕（30ms/字），播完停留 3.5s 后 0.5s 淡出；新台词顶掉旧台词；不暂停游戏；附带短促通讯音（复用 `bullet_fire_c.wav`，经 `GameState.play_sfx` 音效池）。 |
| `scripts/elite_turret_event.gd`（`class_name EliteTurretEvent`） | 事件编排状态机：`IDLE → CARRIER_ENTER →（升起）→ TURRET_ACTIVE → CARRIER_EXIT → BOSS_DELAY → IDLE`。由 `main.gd._ready` 创建挂 Main 下并登记给 spawner。负责台词抽取（10 选 3 无放回）、三节点台词绑定、30s 倒计时（0.1s 节流刷新 HUD）、奖励结算与 Boss 解冻/补触发。 |
| `scripts/hud.gd` | 新增事件计时条（顶部居中 Boss 血条下方）：`ETV_TITLE` 标题 + 30 段品红 SegmentedBar 倒计时 + `ETV_TURRETS` 剩余炮台数（仅变化时更新）；`show_event_bar/update_event_bar/hide_event_bar`；语言切换同步刷新。 |
| `scripts/spawner.gd` | 新增 `_boss_frozen`/`_boss_pending`/`_waves_paused` 与 `_event` 引用；Boss 触发检查在事件冻结期只记 pending（重复到期覆盖同一标记，不累积）；事件触发检查在 Boss 检查之后（Boss 优先）；`_waves_paused` 期间普通波次暂停。 |
| `scripts/main.gd` | `_ready` 创建 `EliteTurretEvent` 挂 Main 下（清场/测试遍历可见），登记 `_spawner._event`。 |
| `scripts/bullet.gd` | 新增 `homing_turn_rate` 字段（默认 4.0，`activate()` 重置），弱追踪弹设为 1.5；原硬编码 4.0 改为读该字段。 |
| `test/elite_turret_event_test.tscn` | 45 项断言（见下）。 |

### 与设计稿的偏差说明

- **触发条件**：设计稿未给出具体触发参数，落地取「分数 ≥ 800 后每 45s 一次 35% 概率判定，事件结束后 60s 冷却」，参数全部入 `elite_turret_event` 配置块可调。同帧竞争由 spawner 检查顺序保证 Boss 优先。
- **波次暂停**：设计稿称"对齐 Boss 战期间 spawner 的压制行为"，但现有 spawner 在 Boss 战期间并不压制波次；落地按设计意图实现为事件期间（`CARRIER_ENTER` 起）暂停普通波次，`CARRIER_EXIT` 起恢复，Boss 冻结保留到 `BOSS_DELAY` 结束。
- **基座环状态灯**：待命暗红环烘焙进航母贴图（5 个基座全量），充能/熄灭由运行时 Line2D 环覆盖实现；未做"盖板旋开"的独立盖板部件（以炮塔 TRANS_BACK 缩放入场表现升起）。
- **台词节点边界**：第 2 句要求"全部摧毁前"触发（与第 3 句互斥）；若最后一击跨节点（如爆炸弹溅射多杀），新台词直接顶掉旧台词。

### 特性清单（玩家可见）

- 战场上方巨型打击航母降入（约屏宽 60%+），品红精英涂装、六边派系徽记、引擎光斑，入场轻震屏。
- 按难度 3/4/5 座炮台自甲板基座升起充能，基座环点亮；充能完毕 HUD 顶部出现 30s 事件计时条 + 剩余炮台数。
- 炮台弱锁定索敌：限速机械转台转向，直射弹 ±7° 出膛散布，追踪弹转向速率降至 1.5 且仅追踪 0.6s；弹药按难度预设序列轮换（直射/3 或 5 发扇面/激光长弹/弱追踪/Boss 狙击）。
- 摧毁进度驱动左下角指挥官通讯台词（打字机字幕 + 通讯音），全歼再播收尾台词。
- 全歼奖励 500 基础分（`add_score` 统一乘难度倍率 → 500/1000/1500），航母受创冒烟慢速撤离；超时则炮台收回、播放固定撤退台词、航母完整加速撤离，无奖励。
- 事件与 Boss 严格互斥：事件期间 Boss 到期仅记录一次，航母离场 4s 后补触发，不产生叠加弹幕。

### 验证结果（2026-07-28）

- `test/elite_turret_event_test.tscn`：45/45 PASS——覆盖状态机流转、中难度 4 台/80 HP、升起期不可受击、独立开火节奏、弱追踪参数（1.5/0.6s）、三节点台词（⌈N/3⌉/⌈2N/3⌉/全歼）、奖励 500×2=1000 入账、超时撤退台词与无奖励、Boss 冻结/pending 单次不累积/补触发、冷却期不可触发。
- 全量回归：`--headless --import`、`--quit-after 300`、smoke/base_system/enemy_combat/buff33/difficulty/boss_enrage/hit_logic/balance/pool_reuse/i18n/keybind/startup_flow/back_navigation/esc_navigation/view_zoom/window_size/intro_cinematic/tutorial 全部 0 失败。
- 自动游玩探针 150s（seed=20260728）：0 异常、0 孤儿节点，事件节点注册表正常；退出时的 ObjectDB 泄漏警告与 HEAD 基线完全一致（既有探针行为，非本次引入）。
- 窗口模式截图人工核对：航母构图、基座环状态灯、炮塔血条、事件计时条、通讯浮层均符合设计。
