# InfiAir Design Baseline (DESIGN_BASELINE)

> **玩法设计意图的单一权威**：数值/规则/系统边界定稿在这里，改设计只改这里。
> 系统行为不在此维护——以代码与测试为准；历史行为规格（BOSS/事件/演出/管理器等）已归档 `docs/archive/`，仅作快照参考。工程约定/门禁/流程见 `AGENTS.md`；方向与已知债务见 `docs/ROADMAP.md`。

## 1. Product & Gameplay

### 1.1 Positioning
Single-player 2D top-down shmup; Godot 4.6.2 .NET + C# (full migration 2026-08-08, zero GDScript), GL Compatibility, 1920×1080 (`canvas_items`/`keep`). **Score-only** (no drops/pickups/equipment). Remade from `airwar-game`, now independent (`docs/archive/PORTING_PARITY.md`).

### 1.2 Core Loop
```
auto-fire + waves → milestone/boss talent points → cache spend (talent tree) → 4 rotating bosses + enrage
→ mothership supply/fire platform → return-to-base restock → same run continues
```
Endless (§1.4), no fixed ending; endgame = **inevitable-death curve** (bounded player growth, unbounded enemy pressure).

### 1.3 Scoring & Economy
- `GameState.AddScore(v)`: multiplies difficulty (Easy ×1 / Normal ×2 / Hard ×3); all kills route here.
- **Kill combo**: all kill-score paths (`Enemy.Die` 普通/精英/分裂子机、`FormationStrikeEvent` 编队机) route via `GameState.AddKillScore(base)` — combo+1 + window refresh; kill score × `min(1 + (combo−1)×step, max_mult)` (window 3.0s / step 0.1 / max ×2.0), then difficulty mult as usual. Break: window timeout (no kill in 3s), player hit (`PlayerDamaged`, DDA same source), `ResetRun`. Boss kills (500×scale via `AddBossKill`) / event rewards / graze do NOT combo. `ComboChanged` signal → HUD combo label. 怒首领蜂/虫姬链式得分的温和版: 普通玩家稳态 ×1.2~1.4, 高手封顶 ×2; 受击=降档(DDA)+断连双通道, 均不致命.
- Boss kill: `AddBossKill(scoreScale)` → `AddScore(500 × scoreScale)` (`milestones.boss_kill_base`); advances talent points/RP/BossKills/difficulty.
- RP: earned from boss kills (+5) and mission claims (+3) only; spent at base console, not carried between runs.
- **RefreshPoints**: separate base-only currency — entering base +1 (`base_task.grant_per_visit`), refresh tasks −2 (`base_task.refresh_cost`); no cap, not carried between runs (run save). Task rotation: 3 active slots drawn from 9-mission pool (`MISSION_POOL`, 3 kinds × 3 goals) without replacement; progress routed by `kind` (kill/survive/boss) so rotated ids still advance; completed-but-unclaimed slots kept on refresh.
- **TechPoints** (meta progression): cross-run currency, independent of RP (RP stays in-run base economy). **Sole settlement = death** (`SettleRun`): battle exit via `ExitConfirm` (deletes save, abandoned not settled) and homecoming do NOT settle — anti-farm; K-key give-up = self-destruct, settles as death: `floor(score/1000) + boss_kills×2 + missions_claimed×1` (`meta.points.*`). Logged-in users only (guests not persisted). Spent at Research Lab (Welcome main menu + BaseConsole panel); effect = new run starts with purchased buff stacks. Upgrades = `meta.upgrades` (8 items, max_level 2–3, independent of buff stacks — `bullet_speed`/`crit_shot` max_level 2 at `buffs.max_stacks` 3; `regen`/`armor`/`slow_field` entries have **no** `max_stacks` key); balance/levels persist in UserDb `meta` field.
- Milestones: score thresholds → talent points into the cache pool (no popup; §1.5).

### 1.4 Difficulty & Endless Curve
**Endgame (D1)**: inevitable-death curve. 公式落地 `csharp/core/Progression/ProgressionCurves.cs` 与 `data/balance.json`。
- `mult = 1 + progression.per_boss_kill(0.6) × boss_kills + time`. Time: quantized by `progression.time_step_seconds` (30s), + `progression.per_ten_minutes` (1.5)/10min → `floor(run_time/30) × 0.075`; counts live `run_time` only (tree-pause and non-run scenes excluded); quantization pins HUD/tests.
- No hard cap. `RecomputeDifficulty()` unified (kill + time tier + save-restore); broadcasts `DifficultyChanged`.
- Enemy growth: Boss HP linear × mult (50s-escape DPS check = "can't kill → flees" valve); `enemies.hp_ramp_factor`/`damage_ramp_factor` (k=0.25 HP / 0.20 dmg)/spawn ramp unbounded.
- Survival: `extra_life` cap **10** (HP 100+500=600); card "max 10"; lifesteal ≤10% feedback offset by HP cap + ramp.
- **Meta-growth boundedness**: tech-tree upgrades capped per item with a finite total point sink → players eventually graduate; enemy pressure stays unbounded → D1 preserved. Meta only shifts opening strength, never the "death is the only end" rule.
- Event units scale: turret/formation HP × `GameState.EnemyHpRamp()`.
- **D2**: Hard-mode buff pacing fastest (×3 score, ×1.5 thresholds) is **intentional**; unchanged.

### 1.5 Talent Cache System（2026-09-07 重构，替代旧里程碑三选一）
- **Structure**: 19 nodes (= the 19 legacy buff ids; all effect consumers unchanged) in 4 categories × lines — `csharp/core/Talent/TalentTree.cs` is the single structural source. Line order = prerequisite chain (next node needs previous ≥ Lv1). Node caps = `buffs.<id>.max_stacks` (json is sole authority; `extra_life` 10).
- **Points & cache**: milestone +`talent.grant.points_per_milestone`(2), boss kill +`points_per_boss`(1) → ordered cache pool, no popup. Overflow decay: first `safe_threshold`(20) points full value; each excess position −`decay_step`(10%), floor `decay_floor`(10%) — LIFO (newest decay deepest). Spend is LIFO from tail; decayed/partial points never recover. HUD indicator (top-right, 4 states: 0 / 1–20 breathing / 21–29 warn / 30+ danger). Opening is charge-gated: hold `G` (`talent_panel`) or hold the indicator (`talent.panel.charge_time`, bottom-center bar; release/damage/other-modal cancels) → full bar opens `TalentPanel` (tree pauses) with layered choreography (dim → wheel overshoot slide → staggered content → footer; exit reversed+faster, unpause after).
- **Costs**: next level = `cost.base`(2) + level × `cost.increment`(1).
- **Diminishing returns**: per-node `softcap` (`talent.softcaps.*`, default 3); past softcap each level's efficiency = max(`diminishing.floor`(0.25), 1 − `diminishing.step`(0.25)×(k−softcap)) → fractional effective level; multiplicative consumers (Player pow-factors, crit, dash CD, mothership_recall CD) read `TalentEffLevel` (= factor^effLevel); integer-semantics consumers (shield layers, pierce, spread, extra_life HP) keep integer levels.
- **Mechanism A — faction mutex**: offense↔defense; one side's total investment ≥ `mutex.threshold`(5) → opposing nodes' caps −`mutex.cap_reduction`(2), permanent for the run.
- **Mechanism B — focus penalty**: any node ≥ `focus.threshold`(7) → all lower-level nodes' effective levels × (1 − min(`penalty_cap`(0.4), `penalty_per_level`(0.06)×over)); top-level nodes exempt. Footer indicator in panel.
- **Mechanism C — route contract** (`TalentTree.Routes`, base console panel): berserker(offense)/guardian(defense)/ranger(mobility); binding free once, switching costs a reset token (bought at base for `route.reset_token_cost`(6) RP). Core category nodes +`route.bonus_levels`(1) effective level; non-core categories' caps halved (floor `route.cap_floor`(1)). Replaces the retired per-line buff routes.
- **Mechanism D — overcharge**: node at effective cap may +1 level at ×`overcharge.cost_mult`(2) cost, then permanently locked; ≤`overcharge.max_per_run`(3)/run.
- **Persistence**: v3 run save `talent` dict (cache point values / levels / overcharged / route / tokens). No compat layer — v2 saves start with fresh talent state. Meta tech upgrades still mean "start with N stacks": applied via `Talent.ApplyStartingLoadout` as starting levels.
- Card text via `BUFF_%s_DESC` keys (single source).
- Key scaling: `rapid_fire.factor` (interval ×0.75/level), `armor.multiplier`, `evasion.chance`, `regen.heal_per_sec`, `slow_field.factor`, `laser_beam.*` (line segment, not projectile), `explosive.*` (unlock `boss_kills>=3` no longer gates purchase — legacy trait), `mothership_recall.cooldown_factor`.
- Aim assist (`player.aim_assist`): `aim_marked` rolled at birth (`mark_ratio` 0.25); AimFrameLayer brackets, AimCrosshair follows `AimPoint()`; in-frame → `Bullet.HomingTarget` (bounded `HomingTime`); out → straight fire; magnet/weak-track share falloff (full <400px → 0.3 floor at 1400px).

### 1.6 Bosses
- Rotation: Nth boss = type `(N-1)%4+1` via `spawner.SpawnBoss()`.
- Phase tables P1/P2/ENRAGE (`boss.phases.typeN` + telegraph); 4-type enrage (`boss.enrage.type_*`, player slow ×0.35, no freeze); difficulty tiers × once in `_Ready()` (`boss.difficulty_scaling`: count/interval/speed).
- Anchor: `FightY` = offset from view top; all via `FightAnchorY()`.
- Escape: 50s timeout flee; fleeing **no rotation advance, no rest**; bar hidden + reorder.
- Structure: facade `Boss` + `BossFire` (danmaku)/`BossAttacks` (FSM)/`BossMovement` (+P1 press-down)/`EnrageSequence`.

### 1.7 Mothership & Return
- Summon (`dock` H charge): run not paused, input locked + event invincibility. Hanger window → warp gate → DESCEND decelerate → dual-ring slow zone → DOCKING pod (`EnterPod()`) → resupply → RELEASE (`ExitPod()`) → loiter/leave. Values `effects.mothership_summon`.
- Fire platform: GATLING/MISSILE during loiter.
- Return: hold B (`homecoming`, `effects.home_charge_time`) → input lock → spawner stop → recall → `SaveRun()` → `starfield.Warp(18)` → cinematic → base UI (tree paused).
- Base: `BaseConsole.cs` + `DawnStation.cs` skin; "continue sortie" → orbital strike clear (Boss kept) → entry animation.
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
- All back inputs → `BackNavigator.GoBack()` via pure `DecideBackAction()` (confirm → cinematic skip → settings/base/blocking/results → buff bar → pause → top → combat).
- Stack: L3 ExitConfirm → L2 overlays (Settings/Base/GameOver/Buff/cinematics) → L1 run (HUD⇄Pause + buff bar) → L0 Welcome (accounts entry).
- Battle exit: 2nd confirm (progress-loss warning); `ExecuteExitCleanup`: save profile, delete save in battle, stop SFX, fade quit.
- PC Esc / gamepad `ui_cancel` / Android back, one state machine.

### 1.13 Combat Fairness (数值定稿)
- **Grace frames**: enemy bullet in Hitbox defers settlement `player.grace_period` (0.05s); out within window = no damage; only enemy-bullet→player timing.
- **Graze**: ring outside hitbox (`player.graze_radius` 20, gameplay-range family, no world_scale) → `player.graze_score` (10, × difficulty), once/bullet; hitbox area gives none. 玩家受击判定仅经 `Player/Hitbox`（r=7 × world_scale = 2.8）；机身 r=22 不参与碰撞（mask=0）。
- **Phase transitions**: P1→P2 & ENRAGE clear all bullets (incl. formation bombs) + brief invincibility (`boss.phases.transition_invincible` 1.0s, additive only); escape: no clear/invincibility. Boss bar segmented (P1 amber/P2 orange/ENRAGE red; boundaries = phase thresholds; drains left).
- **F parry**: full 360° circle, 0.5s window (windup 0.15/recover 0.15); reflect = mirror y-flip ×2 speed ×1.5 dmg (rounded) as player bullet; hard cooldown 3.0s from effect end (3.8s cycle); all `player.parry.*` in balance.json; LT bound.

---
*玩法设计意图修订唯一入口；历史修订轨迹见 git 历史与 CHANGELOG。*
