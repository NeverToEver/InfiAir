# InfiAir Design Baseline (DESIGN_BASELINE)

> **玩法设计意图的单一权威**：数值/规则/系统边界定稿在这里，改设计只改这里。
> 系统行为不在此维护——以代码为准；方向决策与已知债务见 `docs/ROADMAP.md`。

## 1. Product & Gameplay

### 1.1 Positioning
Single-player 2D top-down shmup; Godot 4.6.2 .NET + C# (full migration 2026-08-08, zero GDScript), GL Compatibility, 1920×1080 (`canvas_items`/`keep`). **Score-only** (no drops/pickups/equipment). Remade from `airwar-game`, now independent. 2026-09-08 纯街机流：无登录/无排行榜/无局外成长；开机 = 开场过场 → 深空机库标题屏（星空 + 远景实况战场 + 玩家机跃迁飞入悬挂展示 + 铭牌；按任意键开始新的一局 / **C 继续上次出击** / T 教程）→ 开局；对局内分数不显示不记录，仅作隐藏进度引擎（敌机解锁 / Boss 节奏 / 事件门控 / 里程碑→天赋点触发）。**本局存档**（2026-09-10 追加，见 §2.5）：回基地与选择「保存并退出」时落盘、死亡即删档、读档还原进度从新一波开始。

### 1.2 Core Loop
```
auto-fire + waves → milestone/boss talent points → cache spend (talent tree) → 4 rotating bosses + enrage
→ mothership supply/fire platform → return-to-base restock → same run continues
```
Endless (§1.4), no fixed ending; endgame = **inevitable-death curve** (bounded player growth, unbounded enemy pressure).

### 1.3 Scoring & Economy
- `GameState.AddScore(v)`: multiplies difficulty (Easy ×1 / Normal ×2 / Hard ×3); all kills route here.
- **Kill combo**: all kill-score paths (`Enemy.Die` 普通/精英/分裂子机、`FormationStrikeEvent` 编队机) route via `GameState.AddKillScore(base)` — combo+1 + window refresh; kill score × `min(1 + (combo−1)×step, max_mult)` (window 3.0s / step 0.1 / max ×2.0), then difficulty mult as usual. Break: window timeout (no kill in 3s), player hit (`PlayerDamaged`, DDA same source), `ResetRun`. Boss kills (500×scale via `AddBossKill`) / event rewards / graze do NOT combo. 怒首领蜂/虫姬链式得分的温和版: 普通玩家稳态 ×1.2~1.4, 高手封顶 ×2; 受击=降档(DDA)+断连双通道, 均不致命.
- Boss kill: `AddBossKill(scoreScale)` → `AddScore(500 × scoreScale)` (`milestones.boss_kill_base`); advances talent points/RP/BossKills/difficulty.
- RP: earned from boss kills (+5) and mission claims (+3) only; spent at base console, not carried between runs.
- **RefreshPoints**: separate base-only currency — entering base +1 (`base_task.grant_per_visit`), refresh tasks −2 (`base_task.refresh_cost`); no cap, not carried between runs (run save). Task rotation: 3 active slots drawn from 9-mission pool (`MISSION_POOL`, 3 kinds × 3 goals) without replacement; progress routed by `kind` (kill/survive/boss) so rotated ids still advance; completed-but-unclaimed slots kept on refresh.
- Milestones: score thresholds → talent points into the cache pool (no popup; §1.5).

### 1.4 Difficulty & Endless Curve
**Endgame (D1)**: inevitable-death curve. 公式落地 `csharp/core/Progression/ProgressionCurves.cs` 与 `data/balance.json`。
- `mult = 1 + progression.per_boss_kill(0.6) × boss_kills + time`. Time: quantized by `progression.time_step_seconds` (30s), + `progression.per_ten_minutes` (1.5)/10min → `floor(run_time/30) × 0.075`; counts live `run_time` only (tree-pause and non-run scenes excluded); quantization pins HUD/tests.
- No hard cap. `RecomputeDifficulty()` unified (kill + time tier + save-restore); broadcasts `DifficultyChanged`.
- Enemy growth: Boss HP linear × mult (50s-escape DPS check = "can't kill → flees" valve); `enemies.hp_ramp_factor`/`damage_ramp_factor` (k=0.25 HP / 0.20 dmg)/spawn ramp unbounded.
- Survival: `extra_life` cap **10** (HP 100+500=600); card "max 10"; lifesteal ≤10% feedback offset by HP cap + ramp.
- Event units scale: turret/formation HP × `GameState.EnemyHpRamp()`.
- **D2**: Hard-mode buff pacing fastest (×3 score, ×1.5 thresholds) is **intentional**; unchanged.

### 1.5 Talent Cache System（2026-09-07 重构，替代旧里程碑三选一）
- **Structure**: 27 nodes in 4 categories × lines — `csharp/core/Talent/TalentTree.cs` is the single structural source. Line order = prerequisite chain (next node needs previous ≥ Lv1). Node caps = `augments.<id>.max_stacks` (json is sole authority; `extra_life` 10). 2026-09-08 作战增幅扩展：新增 8 节点（旧 buff 身份全站退役为「增幅/Augment」——效果桥 `CombatStateService.Augments`、文本键 `AUG_*`、配置段 `augments.*`）：`homing`（制导航弹：出膛弹锁定锥内追踪最近敌机）、`salvo`（齐射重弹：每 N 发 ×3 伤害，层数缩短间隔）、`deflector`（偏导护盾：弹反冷却 ×0.78^eff、反射伤害 ×1.6^eff）、`second_wind`（背水回涌：受击后 3s 每秒 +3 HP/层）、`dash_strike`（相位冲击：冲刺触及敌机 35×层伤害）、`graze_field`（擦弹力场：擦弹环 ×1.2^eff、擦弹分 +5/层）、`score_amp`（战果增幅：击杀分 ×1.08^eff）、`combo_guard`（连击护持：连击窗口 ×1.5^eff）。
- **Points & cache**: milestone +`talent.grant.points_per_milestone`(2), boss kill +`points_per_boss`(1) → ordered cache pool, no popup. Overflow decay: first `safe_threshold`(20) points full value; each excess position −`decay_step`(10%), floor `decay_floor`(10%) — LIFO (newest decay deepest). Spend is LIFO from tail; decayed/partial points never recover. HUD indicator (top-right, 4 states: 0 / 1–20 breathing / 21–29 warn / 30+ danger). Opening is charge-gated: hold `G` (`talent_panel`) or hold the indicator (`talent.panel.charge_time`, bottom-center bar; release/damage/other-modal cancels) → full bar opens `TalentPanel` (tree pauses) with layered choreography (dim → wheel overshoot slide → staggered content → footer; exit reversed+faster, unpause after).
- **Costs**: next level = `cost.base`(2) + level × `cost.increment`(1).
- **Diminishing returns**: per-node `softcap` (`talent.softcaps.*`, default 3); past softcap each level's efficiency = max(`diminishing.floor`(0.25), 1 − `diminishing.step`(0.25)×(k−softcap)) → fractional effective level; multiplicative consumers (Player pow-factors, crit, dash CD, mothership_recall CD) read `TalentEffLevel` (= factor^effLevel); integer-semantics consumers (shield layers, pierce, spread, extra_life HP) keep integer levels.
- **Mechanism A — faction mutex**: offense↔defense; one side's total investment ≥ `mutex.threshold`(5) → opposing nodes' caps −`mutex.cap_reduction`(2), permanent for the run.
- **Mechanism B — focus penalty**: any node ≥ `focus.threshold`(7) → all lower-level nodes' effective levels × (1 − min(`penalty_cap`(0.4), `penalty_per_level`(0.06)×over)); top-level nodes exempt. Footer indicator in panel.
- **Mechanism C — route contract** (`TalentTree.Routes`, base console panel): berserker(offense)/guardian(defense)/ranger(mobility); binding free once, switching costs a reset token (bought at base for `route.reset_token_cost`(6) RP). Core category nodes +`route.bonus_levels`(1) effective level; non-core categories' caps halved (floor `route.cap_floor`(1)). Replaces the retired per-line buff routes.
- **Mechanism D — overcharge**: node at effective cap may +1 level at ×`overcharge.cost_mult`(2) cost, then permanently locked; ≤`overcharge.max_per_run`(3)/run.
- **Persistence**: 无对局存档（2026-09-08 移除）——天赋态每局全新，只在本局内累积。
- Card text via `AUG_%s_DESC` keys (single source).
- Key scaling: `rapid_fire.factor` (interval ×0.75/level), `armor.multiplier`, `evasion.chance`, `regen.heal_per_sec`, `slow_field.factor`, `laser_beam.*` (line segment, not projectile), `explosive.*` (unlock `boss_kills>=3` no longer gates purchase — legacy trait), `mothership_recall.cooldown_factor`.
- Aim assist (`player.aim_assist`): `aim_marked` rolled at birth (`mark_ratio` 0.25); AimFrameLayer brackets, AimCrosshair follows `AimPoint()`; in-frame → `Bullet.HomingTarget` (bounded `HomingTime`); out → straight fire; magnet/weak-track share falloff (full <400px → 0.3 floor at 1400px).
- **准星-光标绑定（2026-09-10 重设计）**：键鼠/手柄下准星 ≡ 系统光标逐像素绑定——`Player.AimPoint()` 物理增量（raw − lastRaw）全量通过；粘滞（stick_factor）/磁吸/右摇杆偏移经 `Viewport.WarpMouse` 反写真实光标（手感 = 光标被阻滞/轻推，世界坐标 → `GetCanvasTransform()` → 视口坐标），下一帧 raw 即新锚点，准星永不与光标脱钩；目标点钳制在可视世界域内（`ViewWorldRect` + 4px 内边距，视角档自适应），光标顶到屏幕边缘不再失控、也不出窗。触屏无系统光标，保持差值累积平滑（VirtualControls 路径不绑定）。

### 1.6 Bosses
- Rotation: Nth boss = type `(N-1)%4+1` via `spawner.SpawnBoss()`.
- Phase tables P1/P2/ENRAGE (`boss.phases.typeN` + telegraph); 4-type enrage (`boss.enrage.type_*`, player slow ×0.35, no freeze); difficulty tiers × once in `_Ready()` (`boss.difficulty_scaling`: count/interval/speed).
- Anchor: `FightY` = offset from view top; all via `FightAnchorY()`.
- Escape: 50s timeout flee; fleeing **no rotation advance, no rest**; bar hidden + reorder.
- Structure: facade `Boss` + `BossFire` (danmaku)/`BossAttacks` (FSM)/`BossMovement` (+P1 press-down)/`EnrageSequence`.

### 1.7 Mothership & Return
- Summon (`dock` H charge): run not paused, input locked + event invincibility. Hanger window → warp gate → DESCEND decelerate → dual-ring slow zone → DOCKING pod (`EnterPod()`) → resupply → RELEASE (`ExitPod()`) → loiter/leave. Values `effects.mothership_summon`.
- Fire platform: GATLING/MISSILE during loiter.
- Return: hold B (`homecoming`, `effects.home_charge_time`) → input lock → spawner stop → recall → `starfield.Warp(18)` → cinematic → base UI (tree paused).
- Base: `BaseConsole.cs` + `DawnStation.cs` skin（2026-09-08 圆盘目录化：左缘轮盘目录 机库/补给/契约/任务 + 「继续出击」叶，右区单面板切换，分类芯片行保键盘可达）；"continue sortie" → orbital strike clear (Boss kept) → entry animation.
- **增幅补给（基地↔增幅联动，2026-09-08）**：补给面板 RP 购置——「增幅缓存」`base.supply.cache_cost_rp`(4) RP → `cache_points`(2) 点直入天赋缓存池（LIFO 衰减口径同里程碑入账）；「超载槽」`overcharge_cost_rp`(8) RP → 本局风险加点上限 +1（至多 `overcharge_slot_max`(2) 档，ResetRun 清零）。
- Entry animation (`player.PlayEntryAnimation()`): dive to bottom-third → slow backward drift; horizontal-only, vertical locked, invincible (no flicker); spawns delayed.

### 1.8 Events
- **Elite turret** (heavy 30s): carrier backdrop + tracking turrets (own HP/fire/weak lock); reuses enemy bullets. Mutex with Boss (`_bossFrozen`/`_bossPending`); waves paused (`_wavesPaused`), resume after `boss_resume_delay`. 3-node dialogue + comm overlay; reward `reward_score` 500 (× difficulty), timeout = none. Trigger: score ≥`min_score`(800), roll `trigger_chance`(0.35)/`trigger_interval`(45s); `cooldown` 60s.
- **Formation strike** (lowest priority): 3/4/5 (by difficulty) wedge dive → 90° cross → fuse bombs (ring shrinks, AoE hits player only) → exit; full kill = reward. Does **not** freeze Boss but **occupies wave slot** (shared `_wavesPaused`); mutex with turret; `Abort()`-able by return. Trigger: Boss+turret inactive, cooldown done, score ≥`min_score`(500).
- **Fog events** (light interference, independent of spawner chain): probability roll (`fog_events.trigger_chance`/`check_interval`), `first_delay` opening protection, `min_interval` cooldown, explicit `duration` auto-clear, single-event concurrency; effects via signals to Player + manager-owned visuals. 4 events: fake_enemies (no-damage ghost ships), mental_confusion (input inversion + tint), bullet_malfunction (angle jitter / misfire / fire-interval jitter), direction_shift (periodic forced movement vector). Cleared on return and death; no score/economic interaction.
- **Priority chain**: Boss → elite turret → formation strike (encounter 组互斥, 触发门控 = spawner processing); fog 独立触发（不占波次槽、不与 Boss 互斥）。统一注册表在 `GameEventManager`（`GameState.Events`）。

### 1.9 Meta HUD
- Fullscreen FX CanvasLayer layer=1 (above world, below HUD→layer=2); `meta_health.gdshader` + `hint_screen_texture`.
- Pipeline: hit layer (CA + radial blur) → directional ripple → desaturate/cool tint + vignette → cracks (Voronoi baked once; headless 低分辨率).
- FSM: NORMAL/CAUTION/DAMAGED/CRITICAL/DYING (0.75/0.50/0.25/0.20); fast down (tau 0.10), slow up (tau 0.80 + stagger); DYING heartbeat 1.0–1.2Hz, breath ±1.5%, HUD shake ±2px, FOV −6%.
- Explicit (SegmentedBar) + implicit (desaturate/vignette/heartbeat) layers; `reduce_flash`: CA ×0.4, no breath/shake/heartbeat (SFX kept).
- Brightness proxy from registries (bullets ×0.002 + explosions ×0.15), zero GPU readback; LOD1 skips CA/blur/ripple.

### 1.10 Cinematics
- Intro: 6 shots 17.3s, 2.35:1 letterbox, `INTRO_SUB_1..6`; Welcome "New Game"; gate `CurrentScene == Main`; Esc/any key/click skip; tree paused, root `ProcessMode=Always`.
- Return: 7 shots 11.8s, mirrors intro; Esc via `SKIP_RETURN` (1.2s grace `effects.return_skip_grace`); both paths land on base UI (tree paused); BGM −40dB in shot 7.
- Shared factories: `CinematicFx.cs` (`SoftGlow`/`Particles`/`Shockwave`/`Beam`/`RadialStreaks`; zero heap alloc in drive `_Process`), `DawnStation.cs`.

### 1.11 Tutorial
- Standalone `scenes/tutorial.tscn`, self-handles back (not BackNavigator). Aligned with run: stage 1 force-marked targets; stage 4 hold-H → gate → `BeginWarpIn` → dock (hanger skipped). Isolates run state/saves; restore `Engine.TimeScale = 1` on exit.

### 1.12 Exit/Back Navigation
- All back inputs → `BackNavigator.GoBack()` via pure `DecideBackAction()` (confirm → cinematic skip → settings/base/blocking/results → augment dock → pause → top → combat).
- Stack: L3 ExitConfirm → L2 overlays (Settings/Base/GameOver/cinematics) → L1 run (HUD⇄Pause + augment dock)。标题屏 `title.tscn` 为独立场景（2026-09-09 深空机库改版：程序化星空 + 远景实况战场〔敌机编队/远处爆炸/Boss 剪影〕+ 玩家机自远处跃迁飞入右侧悬挂展示〔轮廓背光/尾焰怠速/铭牌卡〕+ 左侧标题区，开场演出 ~2.2s 不阻塞输入；任意键开局 + T 教程；设置页「默认跳过入场动画」开启时开机直达）。
- **圆盘 UI 全覆盖（2026-09-08）**：左缘 `RadialWheel`（圆心锚屏外左侧，卡片沿弧排列；槽距按选项数自适应 `SlotAngleFor`，端点角钳 ±36° 防压 HUD；全容弧面时键盘/滚轮经 `FocusBias` 移动聚焦项）为全站菜单导航面：天赋面板（已有）、暂停、死亡结算、基地目录、设置页导航；`RadialMenuLayer` 为统一开合骨架（dim+轮盘过冲滑入，`SetWheelActive` 同步轮盘/chrome 遮罩显隐——非模态页须显式关遮罩）；方向键旋转 + Enter 确认（GUI 焦点存在时自动让位焦点链）。
- Battle exit: 2nd confirm (progress-loss warning); `ExecuteExitCleanup`: save profile, delete save in battle, stop SFX, fade quit.
- PC Esc / gamepad `ui_cancel` / Android back, one state machine.

### 1.13 Combat Fairness (数值定稿)
- **Grace frames**: enemy bullet in Hitbox defers settlement `player.grace_period` (0.05s); only enemy-bullet→player timing. **离场判定（2026-09-10 修复直击不结算）**：窗口内离场时按弹心相对轨迹段（入口→离场，圆心参考系两端同减抵消玩家移动）最近距 ≤ 核心半径（7×ws = 2.8px）判定——贯穿核心 = 视觉直击，照常吃伤害；仅擦边入框（最近距 > 核心）才免伤。修复前高速弹（420px/s 穿越核心 ~25ms）必在宽限内离场，「离场即免伤」使直击永不结算。到期仍在框内同样结算（不变）。
- **Graze**: ring outside hitbox (`player.graze_radius` 20, gameplay-range family, no world_scale) → `player.graze_score` (10, × difficulty), once/bullet; hitbox area gives none. 玩家受击判定仅经 `Player/Hitbox`（r=7 × world_scale = 2.8）；机身 r=22 不参与碰撞（mask=0）。
- **Phase transitions**: P1→P2 & ENRAGE clear all bullets (incl. formation bombs) + brief invincibility (`boss.phases.transition_invincible` 1.0s, additive only); escape: no clear/invincibility. Boss bar segmented (P1 amber/P2 orange/ENRAGE red; boundaries = phase thresholds; drains left).
- **F parry**: full 360° circle, 0.5s window (windup 0.15/recover 0.15); reflect = mirror y-flip ×2 speed ×1.5 dmg (rounded) as player bullet; hard cooldown 3.0s from effect end (3.8s cycle); all `player.parry.*` in balance.json; LT bound.

---

## 2. 视觉与表现系统（2026-09-10 全面升级：战术琥珀）

### 2.1 色板（单源 `csharp/godot/UITheme.cs`）
全站颜色 token 单源；改此一处 = 全站换色。金属按钮/面板贴图为近白灰度 + 预烘焙倒角，
色相全部由这些 tint 派生（`assets/sprites/ui/metal_streak.png`、`button_plate*.png` 未重绘）。

- **主交互琥珀** `Accent #FF9F1C`：按钮/焦点/进度/边框；`AccentHot #FFC14D` 受激提亮；`AccentDim` 琥珀 22% 分隔线。
- **数据青** `AccentBlue #38BDF8`：**降级为数据/次要通道**（次级弧、虚影基地皮肤）；不再作主色。
- **稀有金** `AccentGold #E8C170`；**危险红** `Danger #FF3B4E`；**成功绿** `Success #3FD68C`（降饱和）。
- 底：`BgDeep #070A0F` 深炭蓝黑 / `PanelBg` 暗钢 / `PanelSteelTint` 暖暗钢；文字 `#E6EDF3` / `#8A97A6`。
- 暖钢 tint：`SteelTint`(normal) / `SteelTintHover`(受激暖光) / `SteelAccentTint`(主按钮琥珀面)；焦点环取 `AccentHot`。
- 次级局部色板同步收编：`RadialWheel`(Card/Band 暖炭灰)、`DawnStation`(暖钢/全息青虚影)、`AimCrosshair`/`AimFrameLayer`(琥珀)、
  `MothershipSummonWindow`、`TalentFanView`、`VirtualControls`(移动=柔青/瞄准=琥珀)、`TitleScreen`/`Tutorial` 底色、`Hud` Boss 分段、`MetaHealthFX` 裂纹带。
- **弹幕可读性**：玩家弹 = 白热芯 + 琥珀晕；敌弹 = 红/品红（不与琥珀 UI 混同）。玩家机能量/尾焰/激光/残影/增幅附件统一琥珀。

### 2.2 世界层后处理（画面"平/廉"的主要补齐手段）
GL Compatibility 下 Godot `Environment` 辉光/SSAO 不可用，故手写屏幕纹理后处理：

- `assets/shaders/world_grade.gdshader`（单趟）：亮部提取 → 旋转网格环采样柔光（quality 档：单环 4 tap / 双环 8 tap）
  → 暖调色彩分级（gain+lift+S 曲线对比）→ 常量晕影 → 胶片颗粒。
- `csharp/godot/WorldPostFx.cs`（`CanvasLayer` layer=1）：`hint_screen_texture` 全屏 ColorRect。
  **层序靠树序**：世界(layer0) → 增强 → `MetaHealthFX` → HUD(layer2)；Meta 采样含辉光结果。
  接入 `main.tscn`(Main) / `title.tscn` / `tutorial.tscn`（教程 HUD 抬至 layer=2）。
- 性能纪律：设置关闭（`WorldPostFx=false`）整层 `Visible=false`（零 GPU）；`reduce_flash` 颗粒置零 + 辉光减半；
  静态参数启动一次、动态参数 epsilon 检测。
- 数值 `data/balance.json effects.world_post.*`（threshold/intensity/radius/tint/quality/grade/gain/lift/contrast/vignette/grain）。
- **设置开关**：设置页「画面」段 `SET_WORLD_POST_FX`（持久化 `world_post_fx`，默认开）；走 `SettingsService` → `GameState` 信号广播（同 `ReduceFlash` 模式）。

### 2.3 战机与战场表现（纯表现层，**玩法判定零改动**）
- **玩家机贴图**（`scripts/tools/generate_player_sprite.py`）：暖钛/青铜钢甲 + 琥珀能量语言，装甲板/铆接/散热格栅/舱口加密，
  暖描边 + 暖缘光（`sprite_polish.py` 新增可选 outline/rim 覆盖——敌方晶体族用模块默认冷色，输出逐字节不变）。
  **画布 254×254 与头部锚点严格不变**（`PlayerAugmentVisuals` 按 `BaseShipScale=0.65` 对位锚点，叠加件仍贴合）。
- **尾焰**：默认方粒 → `CinematicFx.SoftTexture()` 软点 + 白热→琥珀→暗橙 `GradientTexture1D` 色阶（廉价感主源之一）。
- **机体背光轮廓**：敌/精英/Boss 新增同源贴图副本 + 加性材质 + 阵营染色剪影（`Enemy.UpdateRimGlow` / `Boss.RefreshRimGlow`），贴图未改。
- **星云/亮星/爆炸/激光**：星云改琥珀+青双色、亮星色温重调；爆炸 ramp 深琥珀；玩家激光/枪口/残影/受击指示琥珀。
- **面板铬件**：`ChamferedPanel` 新增四角琥珀刻度（`CornerTicks` 默认开）+ 顶缘全强调色受光线（受激边缘）。

### 2.4 过场演出细节（2026-09-10 精修）
「简单处够简单、精细处不够」的补课——对入场/返航过场的乘员与场景追加设计细节（纯表现层，时序/时长/字幕/音效口径全不变）：

- **共享乘员构件 `csharp/godot/CrewFigure.cs`**：把原先两处近乎重复的简笔人物（圆头 + 棍状四肢 + 平板躯干）重建为有设计细节的宇航服——分件头盔（棱面壳/面罩玻璃/框缘/颈环/侧通讯舱/天线信号灯/下颌护板）、分层胸甲 + 背带扣具 + 状态灯排、双筒维生背包（罐体/喷嘴/供气管/压力表/散热格栅）、肩部叠甲 + 铆钉、关节环/护膝胫甲/护腕分指手套/齿纹战术靴、全身边缘走线与琥珀状态灯。**关节契约逐位不变**（返回 {node,hips,knees,shoulders,elbows,torso,eyelid}），步行/握姿/呼吸相位公式与 eye-lid 特写全部照旧；`ReturnCinematic.BuildPerson`（冷青状态灯）与 `IntroCinematic.Shot3`（琥珀）共用，删除两处重复内联。
- **操作台仪表（镜头 4）**：三分区各补精密仪表——圆形读数表（表壳/12 格刻度环/危险区标红/指针/中心轴/状态灯）+ 分段电平条（外框/逐格亮灯/刻度）；面板浮雕板 + 四角螺钉 + 顶缘受光线（先铺浮雕、仪表叠其上）。
- **舱段模块（`DawnStation` 共享）**：环体舱段加舷窗灯带 + 装甲分缝 + 端盖条 + 散热格栅（环站不再是光板矩形块）；返航/基地背景同享。
- **X 光剖面（镜头 2）**：舱室内部补控制台长条/货箱/状态点（确定性布局，无随机），甲板底缘加走线槽 + 铆钉列——蓝图不再是空网格。
- **弹射通道（镜头 5）**：两侧导轨（轨面/暗边/受光棱）+ 轨枕横梁 + 轨端铆灯 + 壁面横向加强肋/铆钉列/警示斜纹——战机压在真实导轨上滑出，而非悬在空走廊。
- **尾焰修正（镜头 5）**：原喷口位置硬编码 `(960±46, y640)` 与贴图真实喷口（锚点 ×1.4 = `960±26.6, y704`）错位约 60px，且尾焰用等宽硬边 `Line2D` 读作灰色矩形块——改为**由贴图锚点推导真实喷口位**，尾焰改软点辉光链（喷口炽芯→沿轴递减半径/亮度→尾端透明，无硬边）+ 喷口外圈热辉；拖影改加性暖色微放大（读作加速辉光而非重影复制）；机身提亮一档。
- **镜头 5 代码重构（去叠加）**：原 ~340 行单方法拆为「编排 `BuildShot5` + 四个分层构建器」——`BuildLaunchCorridor`（走廊结构）/`BuildShipRig`（机体与尾焰）/`BuildSpeedField`（速度场）/`PlayIgnition`（点火时序），辅以 `ExhaustParticles`/`Poly`/`LaunchNozzlePositions` 小工厂。同时收掉叠加过量的层：每喷口原 5 层橙光（主焰粒子 + 白芯粒子 + 柔光柱 + 独立喷口热辉 + 壁面投光）收敛为 3 层（主焰粒子 + 柔光柱含喷口炽芯 + 壁面投光）；走廊去掉与轨枕灯重复的独立铆钉列、6 条任意位置接缝收敛为每壁 1 条；走廊几何/色板提为常量单源（`WallInnerL/R`、`RailL/R`、`Corridor*` 色板）。**渲染结果实测一致（略减噪），时序/时长/音效口径不变。**

### 2.5 本局存档（2026-09-10 追加）
反转 2026-09-08「无对局存档」：新增**单存档位**本局存档 `user://run.json`（与 `settings.json` 分区；复用 `SaveManager`/`SaveStore` 原子写 + 损坏隔离）。

- **模型：存档 = 可继续的检查点**，三条规则覆盖全部边界：
  1. **写入/覆盖**：退出确认「保存并退出」+ **回到基地**（母舰坞修/返航，`Main.OnReturnFinished`）自动落盘，新档覆盖旧档。
  2. **终结删档**：玩家**死亡**（`GameState._Ready` 订阅自身 `PlayerDied`）与**「放弃重开」**（`GameState.RestartRun`）——本局终结，检查点一并作废，不可读档回滚（保住必死曲线、防 save-scum；也堵住「读档→暂停重开→再退出重进」的无限回滚）。
  3. **非破坏性**：标题屏选「**新的一局**」**不删旧档**——一次误触不会抹掉进度；旧档保留到被新档覆盖或被本局终结清除。`ExitToTitle` 同样不删（Tutorial 走此口，删档会误伤；且「回标题保留检查点」语义正确）。
- **读档**：标题屏 `HasRunSave()`（**校验可读性**：文件存在 + `version` 匹配；损坏档被既有隔离逻辑移为 `.corrupt` 并返回 false，不显示假承诺）为真时显示 `C — 继续上次出击`；`C` 置 `GameState.PendingLoadRun`，`Main._Ready` 返回标题分支读档成功则跳过 `ResetRun`（`LoadRun` 内部先 ResetRun 再按存档还原），否则回退全新一局（保留旧档）。
- **粒度**：**还原局内进度，战场从新一波开始**——持久化 score/kills/boss_kills/combo/milestone_count、run_time/difficulty_multiplier/dda_timer/difficulty_time_step、health/augments、talent(levels/overcharged/route/reset_tokens/bonus_overcharge_slots/cache_values)、missions(rp/refresh_points/条目/last_kind_value)。**不持久化**敌机/子弹/Boss 位置、波次计时、连击窗口、DDA 剩余、回血延迟、TaskPool 洗牌游标、玩家无敌/受击帧守卫。
- **还原顺序**（`GameState.RunSave.cs.ApplyRunDict`）：talent → combat（先恢复 extra_life 层级才有正确 MaxHealth）→ score → progress → missions；各服务 `RestoreRunState` 末尾补发既有信号（`AugmentsChanged/TalentsChanged/CacheChanged/RpChanged/…`）驱动 HUD 与 Player 增幅件重建。
- **健壮性**：档案带 `version`（=1），不符按无存档忽略（不隔离不阻塞开机）；损坏 JSON 走既有隔离（且 `HasRunSave` 会触发该自愈，无需人工清理）；**JSON 往返会把 `StringName` 键退化为 `String`**，还原时对 `augments`/`missions` 键做 StringName 归一化（否则查表落空、增幅与任务进度静默失效）；所有字段判型读取，非法回默认。

### 2.6 性能设置（2026-09-10 追加）
设置页「操作模式」页新增**性能**段（`SettingsService` + `SettingsUi`，settings.json 持久化）：

- **帧率上限** `fps_cap`：六档 `60 / 120 / 144 / 165 / 180 / 240`（默认 **60**）。生效值写入 `Engine.MaxFps`。此前项目**未设任何帧率上限与垂直同步**（实测未锁帧约 680fps），故新增该控制。
- **垂直同步** `vsync`：开关，默认**开**。生效值写入 `DisplayServer.WindowSetVsyncMode`（headless 跳过窗口 API）。开启时实际帧率再受显示器刷新率钳制；关闭可降输入延迟（竞技向），配合帧率上限使用。
- **应用时机**：`GameState._Ready` → `LoadSettings()` 后显式调用 `ApplyDisplaySettings()`（无设置文件时 load 不应用，故补默认档）；设置页切换即时生效 + 落盘 + 广播 `DisplaySettingsChanged`。档位非白名单值忽略、保持默认。

---
*玩法设计意图修订唯一入口；历史修订轨迹见 git 历史。*
