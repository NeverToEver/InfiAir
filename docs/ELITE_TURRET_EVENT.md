# Elite Encounter: Multi Auto-Tracking Turret Battery Event — Design Document

> Status: **Implemented** (shipped 2026-07-28 and passed full verification; implementation details at the end, in "Implementation Notes & Features"). Values sampled from `data/balance.json` and existing scripts; new parameters written to the top-level `elite_turret_event` block of `data/balance.json`, with scripts keeping only same-named fallback defaults (per project value conventions).

---

## 1. Sampled Analysis Summary

### 1.1 Existing Ammunition Types

| Ammunition | Source | Speed | Damage | Behavior |
| --- | --- | --- | --- | --- |
| Direct single shot | normal enemy `enemies.bullet_speed` | 420 | 12 | straight line, constant speed, red Polygon2D bullet |
| Fan spread | normal enemy `enemies.spread_bullet_speed` | 340 | 10 | multi-bullet fan stepped by `spread_fan_step≈0.314rad` |
| Laser long bullet | normal enemy `enemies.laser_bullet_speed` | 720 | 20 | high-speed straight long bullet (visually stretched) |
| Boss fan | `boss.fan_bullet_speed` | 380 | 14 | low-speed wide-fan suppression |
| Boss homing | `boss.homing_bullet_speed` | 300 | 12 | `homing=true`, steers toward the player via `lerp_angle(4.0·dt)` within `homing_time` |
| Boss sniper | `boss.sniper_bullet_speed` | 650 | 21 | high-speed single precise shot |
| Boss cross | `boss.cross_bullet_speed` | 260 | 12 | low-speed 4-way/cross spread |
| Enrage snapshot laser/ring | `boss.enrage` | 820 / 240 | 21 / 12 | enrage-phase volley along a preset path |
| Mothership gatling | `mothership.gatling` | 1080 | 8 | player-side strafing barrage |
| Mothership missile | `mothership.missile` | 600 | 80 + splash 20/r80 | player-side multi-target homing + AoE |
| Laser beam (Buff) | `buffs.laser_beam` | — (segment hit) | 10/0.1s tick | player Buff weapon, not a bullet |

All bullets go through `scenes/bullet.tscn` + `GameState.bullet_pool.fire()`, faction set by `setup()/activate()`; homing is implemented by the `homing`/`homing_time` fields in `bullet.gd` (turn-rate 4.0 rad-class interpolation). **This event reuses the enemy-side bullet types above entirely — no new ammunition type.**

### 1.2 Normal Enemy HP

`balance.json → enemies.types[].hp` (medium-difficulty baseline):

- Range: **48 ~ 112 HP** (the four types are 65-72 / 48-56 / 95-112 / 56-66; re-sampled from balance.json 2026-08-02)
- Typical: **≈65-72 HP** (midpoint of the most common first-type band)
- Difficulty multiplier: `difficulty.hp` easy ×0.75 / medium ×1.0 / hard ×1.5

(Reference: elite 135-210; Boss 800 × hp_mults. This event's turrets take HP at "normal unit" magnitude.)

### 1.3 Boss Kill Score Reward

- Settlement entry: `boss.gd._die()` → `GameState.add_boss_kill(score_scale)`
- Score: `add_score(int(500.0 × score_scale))`, `score_scale` normally 1.0 → **base 500 points**
- Settlement: `add_score()` internally applies the difficulty score multiplier (**easy ×1 / medium ×2 / hard ×3**, i.e. 500 / 1000 / 1500 credited)
- Side effects (this event does **not** reuse): RP reward, boss_kills counter, difficulty growth multiplier

### 1.4 Enemy Ship Art Style Keywords

Sampled from `scripts/tools/generate_enemy_sprites.py` (crystal-prism style generator) and `assets/sprites/`:

- **Shape**: strictly mirrored left-right symmetry; crystal hull assembled from geometric facets; sharp polygonal silhouette, blade/claw wings, fork nose; nose up (scene root flipped with rotation=PI)
- **Colors**: hull in dark violet-black crystal segments `HULL_A~D = (22,18,34)~(62,52,92)`; seams near-black `(10,8,18)`; edge lines light violet `(150,140,185)`
- **Faction accent colors**: normal = scarlet `(255,72,56)`; elite = magenta `(255,64,190)`; Boss = amber/violet/ruby
- **Lighting**: two-layer draw — body solid facets + glow neon layer; the glow layer is Gaussian-blurred into a halo and composited with the hull; energy core = accent-color circle + white bright core; tail engine glow ellipses
- **Craft**: 4× supersampled anti-aliasing; neon lines run along wing leading edges / structural edges

---

## 2. Strike Carrier Visual Redesign

### 2.1 Positioning

A background-scale giant unit (not a Boss, not in the Boss rotation) that descends slowly from deep space above the screen top edge and hovers at the upper-middle rear of the battlefield (referencing the mothership's `hover_y=270` layering, but farther back and larger, visually covering 60%+ of the screen width), serving as the "stage" for the turret deployment. The hull itself **cannot be attacked** (no collision layer); only the raised turrets are destructible entities.

### 2.2 Silhouette Description

- **Overall silhouette**: an elongated hexagonal spindle hull, ~1.6–1.8× the longitudinal length of the Boss sprite (410px); reuses the Boss-3 "giant pillar" hexagonal-fortress facet language but expanded laterally — a tall hexagonal prism central hull with a stepped, tapered "deck wing platform" extending from each side; the platform tops are the turret well decks.
- **Bridge**: a three-tier tapered hexagonal tower atop the central hull (crystal-faceted spire, same technique as boss_3's top facets), with a horizontal magenta neon "observation slit" across the tower front.
- **Turret wells**: each wing platform top carries 1–2 octagonal recessed wells (closed armored lids when retracted, outlined in seam `SEAM` color); when raising, the lids swing open along the seams; the turret = small hexagonal prism + single-barrel crystal gun with an energy core embedded in the muzzle.
- **Stern**: three large engine glows (center large, sides small, elliptical halos), brightening on startup and retreat.

### 2.3 Color & Material Notes

- Hull facets reuse the `HULL_A~D` dark violet-black crystal system, with more facets than normal craft (Boss-class), conveying "heavy armor".
- Accent color is **elite magenta `(255,64,190)`** (the event is framed as an "elite encounter", distinct from normal scarlet and Boss warm colors); energy cores use `ELITE_CORE (215,135,255)`.
- Seam/edge/neon/halo craft matches the generator exactly (`SEAM`/`RIM`/blurred-glow composite) so colors don't clash on screen.
- Deck armor plates use the bright `HULL_C/D` facets for horizontal load surfaces and the dark `HULL_A/B` facets for vertical surfaces, giving top-down volume.

### 2.4 Key Visual Signatures

- **Faction emblem**: a hexagonal neon emblem at the center of the central hull front (magenta frame + white core, same drawing method as the energy cores), echoing the elite magenta system as the "elite fleet flagship" identifier.
- **Lighting layout**: a continuous magenta neon line along each wing platform leading edge (same technique as boss_1's wing edges); each turret well has an octagonal neon ring — **the well ring is the status light**: standby dim red → raised/charging magenta bright → the ring extinguishes when the turret is destroyed. Players can read the remaining turret count at a glance.
- **Event opener**: the hull fades in from deep space above the screen + presses down, engine glows brighten, with one low-intensity screen shake (reusing the `effects.shake.mothership=4.0` magnitude).

### 2.5 Concept Sketch Keywords (for generator/art reference)

`elongated hexagonal prism carrier, stepped flight-deck wing platforms, three-tier hexagonal command tower, octagonal turret wells with armored lids, dark violet crystal facets, magenta neon edge lighting, glowing engine cluster, crystalline prism style, top-down, mirrored symmetry, supersampled`

At implementation time, add `strike_carrier()` and `turret()` drawing functions directly to `generate_enemy_sprites.py`, reusing the `Ship` class's facet/seam/rim/neon/energy_core/engine primitives; canvas ~1200×700 (hull) and 96×96 (turret), nose (bow) up.

---

## 3. Turret Mechanics & Per-Difficulty Config

### 3.1 Common Mechanics

- **Raise animation**: after the event triggers, the carrier enters (≈2s to hover into place) → well lids swing open, turrets raise and charge (≈1.5s) → **the 30s countdown starts the moment charging completes**.
- **Tracking**: each turret independently rotates toward the player (`lerp_angle` eased turning with a capped turn speed for a "mechanical turntable" feel); firing direction = current turret orientation + random spread, not exact aim — i.e. "**weak lock**".
- **Weak-lock parameters (new config `elite_turret_event.weak_lock`)**:
  - homing turn rate reduced to **1.5** (existing homing: 4.0), `homing_time` only 0.6s;
  - direct-fire ammo gains **±7°** muzzle spread;
  - hit-rate target: a stationary player gets hit ~50-60%; sustained lateral maneuvering dodges reliably.
- **Firing rhythm**: each turret times independently, 2.0~2.4s interval (aligned with the normal enemy `fire_interval` magnitude), ammo **cycled through a preset sequence** from the turret's preset ammo pool (sequences below; can be changed to `random` in config at implementation).
- **Destructibility**: each turret is an independent Area2D entity (collision layer 3=enemy, in the `enemy` group and registered in `GameState.enemies`), flashes white on hit, has an independent health bar (small segmented bar, reusing the `ui_segmented_bar` style), and on destruction does `Explosion.spawn_at()` + extinguishes the matching well ring.

### 3.2 Per-Difficulty Config

HP takes the normal enemy typical value 80, adjusted and rounded by the per-difficulty `hp` multiplier; all ammo reuses the enemy-side types of §1.1.

| Difficulty | Turrets | HP each | Ammo pool (cycle sequence) |
| --- | --- | --- | --- |
| Easy | 3 | **60** (80×0.75) | direct single shot (420/12) → fan spread (340/10, 3-bullet fan) → direct single shot |
| Medium | 4 | **80** (typical) | direct single shot → fan spread → laser long bullet (720/20) → weak homing (300/12) |
| Hard | 5 | **120** (80×1.5) | fan spread (5-bullet fan) → laser long bullet → weak homing → Boss sniper (650/21) → direct single shot |

Example config block (new in `balance.json`):

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

### 3.3 Balance Check

Player base DPS over the 30s window ≈ 10 dmg / 0.15s ≈ 67/s: total output for a full clear is 180 (easy) / 320 (medium) / 600 (hard), corresponding to pure-hit time 2.7s / 4.8s / 9.0s. Accounting for movement and aiming loss, medium needs about 1/3 of the event time focused on output; the difficulty gradient comes mainly from "turret count × spread-out positioning" and ammo density, consistent with the existing difficulty curve (medium ×1, hard ×1.5 HP).

---

## 4. Immersive Dialogue System

### 4.1 Dialogue Pool (10 lines, bilingual zh/en; keys `ETQ_1`~`ETQ_10`, written to `data/translations.csv`)

| Key | zh | en |
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

### 4.2 Binding & Playback Logic

- At event start, **draw 3 lines randomly without replacement** from the 10 and bind them in order to three progress checkpoints:
  1. destroyed count ≥ ⌈total/3⌉ (at least 1) → play line 1;
  2. destroyed count ≥ ⌈total×2/3⌉ → play line 2;
  3. all destroyed → play line 3 (played before the event's success settlement).
- On event failure (timeout retreat), the bound lines are not played; a fixed retreat line plays instead (can be set separately as `ETQ_RETREAT`, outside the 10-line pool).
- **Presentation**: bottom-left comm overlay — hexagonal chamfered portrait frame (reusing `ui_chamfered_panel`, magenta outline + carrier-emblem silhouette) + typewriter subtitle, fading out 3.5s after display; does not pause the game (`process_mode` follows gameplay, not the pause state). A new line replaces an unfinished old one.
- Accompanied by a short comm-noise SFX (reusing the existing SFX pool; no new assets).

---

## 5. Timeline & Reward Settlement

### 5.1 Timeline Flow

```text
t=0.0s   event triggers (mutex check passes, see §6)
t=0.0 → 2.0s   carrier descends from above the screen top, hovers into place (engines brighten + light screen shake)
t=2.0 → 3.5s   well lids swing open, turrets raise and charge (untargetable: monitoring=false)
t=3.5s   ★ 30s countdown starts (event timer bar + remaining-turret icons appear at the HUD top)
t=3.5 → 33.5s   turrets fire / player destroys turrets → progress lines play at the checkpoints
Branch A: all turrets destroyed (t ≤ 33.5) → success settlement
Branch B: countdown hits zero with turrets still alive → failure settlement
```

### 5.2 Settlement Pseudocode

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

During the event, normal wave spawning is **paused** (aligned with spawner suppression during Boss fights); the mothership is unaffected.

---

## 6. Mutex State Machine vs. Boss Events

### 6.1 Design Constraints

Boss scheduling lives in `spawner.gd` (`boss_score_step=1500` score step + `boss_time_limit=90s`). Mutex requirement: the two never share the screen; a pending Boss trigger is frozen at most once, not accumulated.

### 6.2 State Machine

```text
              ┌────────────────────────────────────────┐
              ▼                                        │
   ┌──── IDLE ────┐   turret event trigger conditions  ┌───────┴───────┐
   │ (normal boss  │ ───────────────────────▶          │ CARRIER_ENTER │
   │  scheduling)  │                                   └───────┬───────┘
   └──────┬───────┘                                           │ entry + raise
          │ boss trigger conditions ready                     │ complete
          │ and event system IDLE                             ▼
          ▼                                        ┌── TURRET_ACTIVE ──┐
   (original Boss flow)                            │ 30s countdown      │
                                                   └──────┬───────┘
                                                          │ success/failure
                                                          ▼
                                                 ┌── CARRIER_EXIT ──┐
                                                 │ retreat animation │
                                                 └──────┬───────┘
                                                        ▼
                                                 ┌── BOSS_DELAY ──┐
                                                 │ fixed 4.0s       │
                                                 │ (boss_resume_    │
                                                 │  delay)          │
                                                 └──────┬───────┘
                                                        ▼
                                                 back to IDLE; if a frozen
                                                 Boss trigger exists → trigger
                                                 once immediately and clear the
                                                 freeze flag (no accumulation)
```

### 6.3 Rules

- **Trigger mutex**: when the turret event's trigger check races the Boss trigger check in the same frame, Boss wins (Boss is a score-milestone promise); the turret event may start only when the Boss is not in "warning/entry/fighting". **2026-07-29 addendum**: the turret event also does not start while the formation-strike event is active — both events share the spawner `_waves_paused` wave-pause hook, so one ending cannot prematurely resume the other's pause.
- **Freeze logic**: on entering `CARRIER_ENTER`, `_boss_frozen = true`; if the spawner's Boss score step expires during this period, the Boss is not triggered; instead `_boss_pending = true` (recorded once; repeated expirations overwrite the same flag — no accumulation).
- **Resume logic**: when the event leaves `BOSS_DELAY`: if `_boss_pending` is true, start the Boss warning flow immediately and clear `_boss_pending`; `_boss_frozen` resets at the same time. If the Boss conditions haven't expired when the event ends, restore the original score-step timer without any compensation.
- **Edge case**: crossing the Boss score step during the event is normal (event rewards 500–1500 points); the freeze flag ensures the Boss only appears 4s after the carrier leaves, avoiding two large units stacking barrages on screen.
- **Failure unfreezes too**: success/failure does not change the mutex resume path, only the reward.
- At implementation, add `test/elite_turret_event_test.tscn`: asserts the 30s timing, the three dialogue checkpoints, reward crediting (incl. difficulty multiplier), Boss freeze/resume, and the single-shot non-accumulation semantics.

---

## Appendix: Implementation Checklist (implementation-phase reference)

1. Add the `elite_turret_event` block to `data/balance.json` (scripts keep same-named fallback defaults).
2. Add `ETQ_1`~`ETQ_10`, `ETQ_RETREAT`, and event HUD text keys to `data/translations.csv` (bilingual zh/en).
3. Add `strike_carrier()` / `turret()` generator functions to `scripts/tools/generate_enemy_sprites.py`, outputting PNGs to `assets/sprites/`.
4. Add `scripts/strike_carrier.gd` (entry/hover/retreat director), `scripts/turret_battery.gd` (turret entity: tracking/firing/hit), and the comm-overlay UI (reusing `ui_theme.gd` + `ui_chamfered_panel.gd`).
5. Add the event trigger check and Boss freeze/resume hooks to `scripts/spawner.gd`; register event orchestration in `scripts/main.gd`.
6. Add `test/elite_turret_event_test.tscn`; if the back-navigation stack gains new pages, sync `docs/EXIT_FLOW.md` (this design has no new pages; likely not needed).

---

## Implementation Notes & Features (shipped 2026-07-28)

### File Landing Points

| Asset/File | Content |
| --- | --- |
| `data/balance.json → elite_turret_event` | All tunable parameters: duration / entry / raise / resume intervals, trigger conditions (`min_score=800`, `trigger_interval=45s`, `trigger_chance=0.35`, `cooldown=60s`), `turret_hp_base=80`, `turret_counts` (easy 3/medium 4/hard 5), `fire_interval=[2.0,2.4]`, `weak_lock`, `ammo_sequences` (per-difficulty ammo cycle), `reward_score=500`, `carrier` (hover height / retreat params / screen shake). Scripts keep same-named fallback defaults. |
| `data/translations.csv` | `ETQ_1`~`ETQ_10`, `ETQ_RETREAT`, `ETV_TITLE`, `ETV_TURRETS` (bilingual zh/en; Godot re-import generates `.translation`). |
| `scripts/tools/generate_enemy_sprites.py` | Adds `strike_carrier()` (1200×700) and `turret()` (96×96) drawing functions, reusing `Ship` primitives; the `TURRET_WELLS` well coordinate table aligns 1:1 with the runtime `StrikeCarrier.SOCKETS`. Outputs `assets/sprites/strike_carrier.png`, `elite_turret.png`. |
| `scripts/strike_carrier.gd` (`class_name StrikeCarrier`) | Carrier director: ENTER (2s ease-in descent + fade-in + arrival screen shake) → HOVER (±6px slow float) → RETREAT (reuses the Boss escape parameter family: start_speed 120 / accel 420; damaged ×0.55 slow + darkening + deck explosion points). No collision layer; the hull can't be attacked. The 5 octagonal well rings are Line2D status lights: charging magenta bright / destroyed extinguished (standby dim-red rings baked directly into the sprite). |
| `scripts/turret_battery.gd` + `scenes/turret.tscn` (`class_name TurretBattery`) | Turret entity: Area2D collision layer 3 (enemy), registered in the `enemy` group and `GameState.enemies` (auto-hittable by player bullets / mothership fire / explosion-buff bullets). Raise animation is a TRANS_BACK scale-in during which `monitoring=false` (untargetable); attackable only after charging completes and `activate()` is called. Independent health bar is a magenta SegmentedBar (8 segments), flashes white on hit; on destruction `Explosion.spawn_at()` + screen shake + well ring extinguishes. On timeout, `cease_fire_and_retract()` stops fire, retracts, and self-frees. |
| `scripts/comm_overlay.gd` (`class_name CommOverlay`) | Bottom-left comm overlay (CanvasLayer layer=12): ChamferedPanel magenta outline + typewriter subtitle (30ms/char), stays 3.5s after finishing then 0.5s fade-out; a new line replaces the old; doesn't pause the game; accompanied by a short comm sound (reusing `bullet_fire_c.wav` via the `GameState.play_sfx` SFX pool). |
| `scripts/elite_turret_event.gd` (`class_name EliteTurretEvent`) | Event orchestration state machine: `IDLE → CARRIER_ENTER → (raise) → TURRET_ACTIVE → CARRIER_EXIT → BOSS_DELAY → IDLE`. Created by `main.gd._ready` under Main and registered with the spawner. Handles line drawing (3 of 10 without replacement), three-checkpoint line binding, the 30s countdown (0.1s throttled HUD refresh), reward settlement, and Boss unfreeze/re-trigger. |
| `scripts/hud.gd` | New event timer bar (top-center, below the Boss health bar): `ETV_TITLE` title + 30-segment magenta SegmentedBar countdown + `ETV_TURRETS` remaining-turret count (updated only on change); `show_event_bar/update_event_bar/hide_event_bar`; refreshes on locale switch. |
| `scripts/spawner.gd` | Adds `_boss_frozen`/`_boss_pending`/`_waves_paused` and the `_event` reference; during the event freeze the Boss trigger check only records pending (repeated expirations overwrite the same flag, no accumulation); the event trigger check runs after the Boss check (Boss priority); normal waves pause while `_waves_paused`. |
| `scripts/main.gd` | `_ready` creates `EliteTurretEvent` under Main (visible to clearing/test traversal) and registers `_spawner._event`. |
| `scripts/bullet.gd` | Adds the `homing_turn_rate` field (default 4.0, reset in `activate()`); weak-homing bullets set to 1.5; the previously hardcoded 4.0 now reads this field. |
| `test/elite_turret_event_test.tscn` | 45 assertions (see below). |

### Deviations from the Design Draft

- **Trigger conditions**: the draft gave no concrete trigger parameters; implementation uses "after score ≥ 800, a 35% chance check every 45s, 60s cooldown after the event ends", all tunable in the `elite_turret_event` config block. Same-frame races are resolved by spawner check order, guaranteeing Boss priority.
- **Wave pause**: the draft said "aligned with spawner suppression during Boss fights", but the existing spawner doesn't suppress waves during Boss fights; implementation follows the design intent: normal waves pause during the event (from `CARRIER_ENTER`), resume from `CARRIER_EXIT`, and the Boss freeze holds until `BOSS_DELAY` ends.
- **Well-ring status lights**: standby dim-red rings are baked into the carrier sprite (all 5 wells); charging/extinguished states are runtime Line2D ring overlays; no independent lid parts for the "swing-open lids" (raising is shown via the turret TRANS_BACK scale-in).
- **Dialogue-checkpoint boundary**: line 2 requires triggering "before all destroyed" (mutually exclusive with line 3); if the final hit spans checkpoints (e.g. explosion-buff splash multi-kill), the new line simply replaces the old one.

### Feature List (player-visible)

- A giant strike carrier descends above the battlefield (≈60%+ of screen width), magenta elite livery, hexagonal faction emblem, engine glows, light entry screen shake.
- 3/4/5 turrets (by difficulty) rise from the deck wells and charge, well rings light up; once charged, a 30s event timer bar + remaining-turret count appears at the HUD top.
- Turrets track with weak lock: speed-limited mechanical-turntable turning, ±7° muzzle spread on direct-fire bullets, homing turn rate reduced to 1.5 tracking only 0.6s; ammo cycles through per-difficulty preset sequences (direct / 3- or 5-bullet fan / laser long / weak homing / Boss sniper).
- Destroy progress drives bottom-left commander comm lines (typewriter subtitle + comm sound), with a closing line on full clear.
- Full clear rewards 500 base points (`add_score` applies the difficulty multiplier → 500/1000/1500); the carrier retreats damaged, smoking and slow; on timeout, turrets retract, the fixed retreat line plays, and the carrier retreats intact and accelerated — no reward.
- The event and Boss are strictly mutually exclusive: during the event an expired Boss trigger is recorded once and re-triggered 4s after the carrier leaves; no stacked barrages.

### Verification Results (2026-07-28)

- `test/elite_turret_event_test.tscn`: 45/45 PASS — covers state-machine flow, medium 4 turrets/80 HP, untargetable during raise, independent firing rhythm, weak-homing params (1.5/0.6s), three-checkpoint lines (⌈N/3⌉/⌈2N/3⌉/full clear), reward 500×2=1000 credited, timeout retreat line with no reward, Boss freeze/pending single-shot non-accumulation/re-trigger, and no trigger during cooldown.
- Full regression: `--headless --import`, `--quit-after 300`, smoke/base_system/enemy_combat/buff33/difficulty/boss_enrage/hit_logic/balance/pool_reuse/i18n/keybind/startup_flow/back_navigation/esc_navigation/view_zoom/window_size/intro_cinematic/tutorial all 0 failures.
- Autoplay probe 150s (seed=20260728): 0 anomalies, 0 orphan nodes, event node registry normal; the ObjectDB leak warning on exit matches the HEAD baseline exactly (existing probe behavior, not introduced here).
- Manual windowed-screenshot check: carrier composition, well-ring status lights, turret health bars, event timer bar, and comm overlay all match the design.
