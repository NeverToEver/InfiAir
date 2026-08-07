# Return Home Cinematic & Phantom Base UI

Single source of truth: return-home cinematic + phantom station base UI (sampling, concept, storyboard, base-UI re-skin, transition). Any cinematic/UI change must sync this doc. Intro symmetry: `docs/INTRO_CINEMATIC.md`.

## 0. Pre-analysis (2026-07-28)

### 0.1 Intro structure (src `scripts/intro_cinematic.gd` + `docs/INTRO_CINEMATIC.md`)
Total 17.3s = 6 shots 16.1s (transitions inside shot times) + title freeze 1.2s (0.2 in / 0.8 hold / 0.2 out). Durations `[2.8, 2.5, 2.5, 2.5, 2.8, 3.0]` (`_shot_durations` writable, for tests).

| # | Shot | Dur | Content | Trans |
| --- | --- | --- | --- | --- |
| 1 | wide push-in | 2.8s | Dawn deep-space explosion, debris/dust 2 layers, scale 0.7→1.0 | →2 flash 0.10s (reclaim next 0.28s) |
| 2 | X-ray chain blast | 2.5s | cold-blue wireframe, energy chain 0.2s/node, tremble | →3 black 0.3s |
| 3 | pilot sprint | 2.5s | multi-segment figure 2-frame run loop (phase-driven), red alert 0.7s | →4 black 0.3s |
| 4 | console boot | 2.5s | 3-zone comm, countdown 0.6s/digit, end tilt −3° | →5 flash |
| 5 | eject chase | 2.8s | rail back + ±6px shake, ship 180°×1.4, white-hot core, speed lines | →6 black 0.3s |
| 6 | wide close | 3.0s | nebula/anamorphic glow/debris/supply dots, ease_in exit, fade black 0.7s | →title |
| 7 | title freeze | 1.2s | InfiAir in/hold/out | `skip()` |

Norms: 2.35:1 letterbox (132px bars); subtitle cards `INTRO_SUB_1..6` (0.3s in, fade with transition); handheld drift (shared container sine ±3px + micro rotation, single `_process` zero-alloc); 1920×1080 design coords. Skeleton: `CanvasLayer(layer=35, process_mode=Always)`; `get_tree().paused = true`; one-shot `Timer` chain (no `create_timer`); in-shot `create_tween()`; `skip()` idempotent exit; Esc via BackNavigator `SKIP_INTRO`. Factories: `_glow()`, `_line()`, `_rect_poly()`, `_bg_rect()`, `_particles()` (≤96/emitter), `_kick_shake()`. Budget: draws <400, live ≤400, ≤1 `_process`/shot.

### 0.2 Dawn station anchors (src `_build_shot1()`, center (960,470))

| Part | Geometry | Color |
| --- | --- | --- |
| main arc | r260, 48-seg closed Line2D w26 | (0.38, 0.45, 0.58) |
| detail rings | r232 / r288, 64 seg, w2 | (0.55, 0.65, 0.8, 0.5) |
| 16 ticks | radial r244→276, w3 | (0.18, 0.22, 0.3) |
| 7 modules | 64×40 @ r260 + 70×46 outline + spokes r70→240 w8 | (0.48,0.56,0.68)/(0.6,0.7,0.85,0.35)/(0.3,0.36,0.48) |
| hub | glow disc r66 (non-add) + ring r46 w2.5 | (0.28,0.34,0.45)/(0.5,0.6,0.75,0.6) |
| breach | 0.5–1.2 rad: dark arc w30 (0.05,0.05,0.08) + jagged poly + 3 shards | near-black |
| story | `INTRO_SUB_1`: Dawn ring station · core overload → destroyed | — |

Keywords: ring + 7 segments + spokes + hub; cold steel blue-gray low-sat; breach right-bottom (0.5–1.2 rad). Phantom keeps geometry + breach (recognition anchor). (8 slots, breach gap 0.4–1.4 rad: one module missing.)

### 0.3 Base UI current (src `scripts/base_console.gd` + `scripts/ui_theme.gd` + `docs/screenshots/base.png`)
- Chain: hold B in-run (`HOME_CHARGE_TIME`) → `Main._start_homecoming()`: lock input, stop spawner, recall mothership, `GameState.save_run()`, `_starfield.warp(18.0)`, white flash (0.5+0.5+0.3s), `_base_ui.show_base()`, tree paused. Return: `resume_requested` → `_resume_from_base()` → orbital strike anim (`scripts/orbital_strike.gd`, clears on hit frame, resumes same run).
- Layout: CanvasLayer > dim ColorRect(0.02,0.03,0.08,0.95) > CenterContainer > VBox: title BASE_TITLE 44 → gold RP → HBox 2 cols sep 20 → primary 280×52. Left: hangar (status) + supply (repair/recharge); right: routes (attack/mobility 2 rows) + missions (list + claim). ChamferedPanel min w560.
- Tokens: panel (0.039,0.063,0.102,0.78), border 1px (0,0.83,1,0.5), accent `#00d4ff`, secondary `#0080ff`, gold `#d8a868`, text `#e0e8f0`/`#8a9bb0`, sizes 72/40/28/24/18, NotoSansSC, `animate_open` 200ms / `stagger_open` 60ms (modulate.a only).
- Ports: `_on_repair_pressed` (2RP full HP), `_on_recharge_pressed` (2RP full fuel), `_on_route_pressed`, `_on_claim_pressed`, `_on_resume_pressed`; data via GameState (rp/buff_count/choose_route/claim_mission/mission_progress).

### 0.4 FX/asset inventory

| Need | Reuse | Gap |
| --- | --- | --- |
| warp charge | `Starfield.warp(factor)` (stretch, lerp back); already `warp(18.0)`; shot-5 nozzle recipe | nozzle red→white gradient new (glow + tween) |
| portal | shockwave-ring recipe, shot-2 wireframe style, flash pieces | portal body new |
| track | `mothership.tscn` `TractorBeam` (8Hz `0.55+0.45·sin`) + dock tween 1.5s TRANS_CUBIC EASE_IN_OUT | energy track new |
| comm | subtitle cards; `CommOverlay` typewriter (layer 12) | `RETURN_SUB_1..7` new (zh+en) |
| pilot | no texture; intro 3/4 procedural rig (phase-driven, zero-alloc) | sit/lie/eyes-closed poses |
| ship | `assets/sprites/player_ship.png` | none |
| deck/lounge | **none**; mothership only reference | all new §1.3 |
| audio | 10 SFX only: bgm_loop / buff_pick / bullet_fire×3 / dash / explosion / explosion_big / player_hit / resupply | map §2 (reuse + pitch; offline gen phase 2) |

## 1. Phantom station "Dawn · Echo"

### 1.1 External phantom FX (constant far/mid)
§0.2 geometry + 4 layers:

1. **Hologram base** (brightened 2026-07-28): all BLEND_MODE_ADD, cyan `#00d4ff` family — arc α0.55, modules α0.45, details/spokes/ticks/hub-ring α0.35, hub core α0.50. Breath on station `modulate` (0.85↔1.0, 4s); separate inner container glitch flash (every 3.8s, 0.3→1.0 two steps 0.04s); independent.
2. **Scan band**: α0.12 cyan ADD sweep vertical 3.5s/pass; outer ring α0.15 halo.
3. **Data particles**: 2 GPUParticles2D (≤96/emitter, ≤400): ①edge escape α0.4; ②inner flow α0.3 (ring emission ≈ radial).
4. **Breakage** (2026-07-28): a) main-arc gaps `[[0.5,1.2],[2.4,2.62],[4.9,5.06]]` via `_build_body` `gaps` param (empty = destroyed look unchanged); b) 7 segments dropout 0.1s, phase 0.13s; c) energy grid 2.5px α0.75, flicker 0.3↔0.95, dropout 0.5+0.15×(grid%3)s; jagged outline α0.8 w2.5; d) 3 shards α0.35 ADD drift out; e) station glitch (see 1). "Destroyed station; data core weaves its memory."

### 1.2 vs intro shot 1

| | Intro 1 (destroy) | Return 4 (return) |
| --- | --- | --- |
| framing | center (960,470) push-in | same position/structure, slow pull-back/side |
| palette | cold + red→orange-white explosion | cold + cyan hologram (no warm) |
| breach | explosion core, shards out | grid mending, particles in |
| mood | disintegration | reconstruction |

### 1.3 Internal solid regions (procedural, shots 5–7)
- **Hangar** (ring-segment wall): hex deck (dark solid + cyan glow edges + guide-light strip), X-ray wireframe bg (translucent, cyan), gate at end.
- **Corridor** (spoke): side view, ceiling/floor perspective + wall pipes; ceiling sensor lights node-by-node (cyan-white 0.15s/node); distant phantom glow.
- **Rest room** (hub core): sleep bed (rounded-rect + headboard hologram mini-screen), 1 warm top light (only warm source, "home" anchor), window shows phantom ring rotating slowly.

## 2. Return storyboard (total 11.8s)

Durations `[1.6, 1.2, 1.4, 2.2, 1.8, 1.6, 2.0]` (transitions inside; tuned 2026-07-28 from 16.8s, keyframes scaled). Intro norms apply: letterbox, `RETURN_SUB_1..7`, drift, Timer chain, `skip()`, Esc/any key/click. Audio = existing `GameState.SFX_*` (dB/pitch in parens).

| # | Shot | Dur | Camera/KF | FX | SFX |
| --- | --- | --- | --- | --- | --- |
| 1 | wide: ship hovers center-below; charge — nozzle (0.5,0.1,0.05)→(1,0.95,0.85), scale 1→1.8; particles converge in | 1.6s | fixed; end 0.4s distortion (concentric thin rings + 2 offset shock rings pass) | `Starfield.warp(12.0)` start (continues `warp(18.0)` stretch, decays); ship = player_ship.png tail (180°, intro shot-5 pose); nozzle `CinematicFx.soft_glow()` ×2 + modulate/scale tween; converge `CinematicFx.particles()` inward (negative vel); peak `CinematicFx.shockwave()` ×2 (offset 0.14s, ry 0.6) | SFX_DASH −6dB 0.6× |
| 2 | FX: portal tears — ellipse Line2D cyan α0.9 point→full 0.5s; 12 glow dots circling; interior phantom blur (α0.35 + jitter) + 2 counter-rotating vortex arcs + inflow | 1.2s | push scale 1.0→1.06 | ring = shockwave recipe reversed; dots per-frame (only `_process`, zero-alloc, drives vortices); throat `CinematicFx.soft_glow()` pad α0.15; inflow ring emission (DawnStation recipe) + negative radial accel, y-squash, from 0.45s; interior §1.1 minimal | SFX_EXPLOSION −12dB 0.5× |
| 3 | match cut: ship into portal (scale 1→0.2, flare), closes + flash → hard cut to phantom space: out (0.2→1, ease_out), closes | 1.4s | 0.63s real + 0.10s flash + 0.77s phantom; direction up-right | `CinematicFx.radial_streaks()` (hidden with flash at part_a); `shockwave()` white-cyan 0.4s; flash = intro 1→2 same; bg dim nebula + phantom silhouette α0.15; trail 40 | SFX_DASH 0dB; exit −10dB |
| 4 | wide→mid: station full reveal (§1.1 all 4 layers); energy capture beam pulls ship; 8 nav lights chase | 2.2s | side pan (sine 60px + scale 1.0→1.12) | `CinematicFx.beam()` (glow + core + 3 flow dots, bezier 24 pts); 8 lights r=RING_RADIUS+4, `_CaptureShot` phase pulse; short tail; pull tween TRANS_SINE; scan band | SFX_RESUPPLY −8dB |
| 5 | hangar: land drop 40px ease_out + bounce + dust; lights chase to landing point; engine off 0.3s; canopy up 0.4s + glass highlight; pilot jump down 0.5s | 1.8s | fixed low angle (horizon lower 1/3, elements −60px) | 8 lights by landing distance (tweens at build); highlight alpha Polygon2D slide + fade; pilot = shot-3 rig new poses; dust soft-dot | land SFX_EXPLOSION −18dB; jump SFX_DASH −14dB |
| 6 | side follow: walk corridor; ceiling lights per x (turn on 40px behind, dim 400px behind — lerp-smoothed); stop at door, slides open (2 panels + light leak) | 1.6s | pan, pilot at left 1/3 | walk = shot-3 run slowed (~90px/s, half stride); 12 Line2D lights by x threshold (only `_process`); ribs ×12 + plates + 3 cones ADD α0.05 parallax + floor strip; door 2 Polygon2D | steps SFX_BUFF_PICK −20dB ×6 @0.4s; door SFX_DASH −10dB 0.7× |
| 7 | room: walk to bed; sit→lie (0.6s); stretch; breathing; close-up scale→1.6: eyelids 0.8s; dim 0.9s | 2.0s | mid 0.8s → close-up 0.7s → dim 0.9s overlap | `_RoomShot` (only `_process`): walk 0.6u (limb = `_WalkShot`, weight fade, ends upright) + breath 1.7u (scale.y ±2.5% sine) + 3 star dots; sit/lie/eyelid/push 0.6s KF tweens; dim fullscreen ColorRect; BGM fadeout | lie SFX_RESUPPLY −16dB; BGM → −40dB |

Transitions: 1→2 black 0.3s (suspense); 2→3 continuous; 3 internal flash 0.10s; 3→4 black 0.3s; 4→5 black 0.3s; 5→6, 6→7 black 0.3s; 7 end dim 0.9s → base UI directly (no title freeze; its slot = base UI fade-in, §4).

Symmetry: intro "destroy → escape → deep space (tense, warm)" ↔ return "jump → captured → sleep (calm, cold)"; intro1 ↔ return4 station; intro5 (away) ↔ return3 (arrive); intro3/4 indoor ↔ return6/7; intro title ↔ return UI fade-in.

## 3. Base UI re-skin

### 3.1 Principle
**Zero logic change**: `base_console.gd` signals/bindings/GameState API/callbacks untouched. Only ①UITheme phantom tokens + factories; ②`base_console.gd` `_ready()` visuals (dim, panel style, bg + decor layers). Existing tests (`base_system_test` etc.) must stay green.

### 3.2 New tokens (coexist, no replace)

| Token | Value | Use |
| --- | --- | --- |
| `PHANTOM_BG` | (0.01, 0.03, 0.06, 0.90) | fullscreen bg (colder/deeper) |
| `PHANTOM_PANEL_BG` | (0.03, 0.08, 0.12, 0.55) | more transparent, holographic |
| `PHANTOM_BORDER` | (0.0, 0.83, 1.0, 0.65) | border, brighter than PANEL_BORDER |
| `PHANTOM_SCAN` | (0.0, 0.83, 1.0, 0.06) | scanline overlay |
| `PHANTOM_TEXT_FLICKER` | α 0.92–1.0 jitter | decor labels only |

Panel = ChamferedPanel (PHANTOM_PANEL_BG + PHANTOM_BORDER) + scanlines (1px/4px or sweep band, mouse_filter=IGNORE) + frosted ≈ lower alpha + radial glow pad (no blur shader, gl_compatibility). Hover float: ACCENT_DIM line 2px below + boost on hover (no callback change). Icons: 16×16 Line2D glyphs (ship/wrench/cross/flag) left of 4 titles. Flicker: RP + titles only — 3Hz sine 0.92–1.0 + 1px offset 0.06s every 2.7s (tween loop, no `_process`); body static.

### 3.3 Layout (zones/logic reused)
Old: dim → title 44 → gold RP → HBox sep20 → primary. New: PHANTOM_BG + phantom concept layer (ring r≈520 @ (960,540) α0.10 + light particles + 8s/pass band; after dim, before CenterContainer) → title (flicker) → RP (flicker) → HBox sep 140 (120px gap reveals ring axis = "panels around core"; containers/focus unchanged; floating-ring option B rejected) → phantom panels → primary (hologram).

Changes: ①bg layer; ②sep 20→140; ③panel style + scanlines + icons; ④primary + bottom line + 1.5s breath glow (size/pos/callback same); ⑤open: 0.25s hologram boot (α0 + scale 0.98→1.0) before `animate_open`; `stagger_open` 60ms kept.

### 3.4 Ports (design coords, panel top-left after centering)

| Port | New |
| --- | --- |
| hangar `_build_hangar` | (320, 300) |
| supply `_build_supply` | (320, 520) |
| routes `_build_routes` | (1040, 300) |
| missions `_build_missions` | (1040, 520) |
| continue `_on_resume_pressed` | (820, 810), 280×52 |
| title/RP `_refresh` | top center + flicker |

(Coords indicative; real layout from CenterContainer; for screenshot checks.)

## 4. Cinematic → base UI transition

### 4.1 Chain (`main.gd`)

```
func _start_homecoming():                      # current skeleton kept
    _homecoming=true; lock input; stop spawner; recall mothership; save_run()
    _starfield.warp(18.0)                      # feeds shot-1 charge/stretch
    _play_return_cinematic()                   # replaces _flash_white + show_base

func _play_return_cinematic():                 # like _play_intro_cinematic
    _return = RETURN_SCENE.instantiate(); _return.finished.connect(_on_return_finished)
    add_child(_return)                         # CanvasLayer layer=35, Always
    get_tree().paused = true

func _on_return_finished():                    # skip & natural end same exit
    _return = null; _base_ui.show_base()       # §3 skin; tree stays paused
    # BGM: shot-7 fadeout → base ambience (bgm_loop quiet, −30dB in)
```

### 4.2 Timeline (from charge complete)
```
t=0.0   charge done: lock/spawner/save_run/warp(18)
t=0.0   shot1 1.6s | t=1.6 shot2 1.2s | t=2.8 shot3 1.4s | t=4.2 shot4 2.2s
t=6.4   shot5 1.8s | t=8.2 shot6 1.6s | t=9.8 shot7 2.0s (dim 0.9s + BGM out from 10.9)
t=11.8  finished (or skip() anytime) → show_base(): boot 0.25s + animate_open 0.2s
t≈12.3  fully operable
```

### 4.3 Mechanics
- No async load (all procedural); sync `show_base()` in `finished`.
- Skip: Esc (BackNavigator `SKIP_RETURN`, priority = `SKIP_INTRO`) / any key / click → `skip()` → `_on_return_finished()`; `skip()` kills BGM tween, sets target volume. **Grace `SKIP_GRACE` s (config `effects.return_skip_grace`, default 1.2)**: `skip()` ignored within — held WASD/Shift/Space don't skip; check inside `skip()`, all routes gated; natural end unaffected.
- Save timing: `save_run()` before cinematic (current line 746); skip/crash safe.
- Pause: tree paused through cinematic + UI (both Always); `_resume_from_base()` → orbital strike (Always, clears on hit frame, unpauses).
- Future "skip after first view": GameState flag → old flash path; not implemented, fork reserved.

## 5. Constraints & tests
- i18n: `RETURN_SUB_1..7`, `RETURN_SKIP` (or reuse `INTRO_SKIP`), zh+en in `data/translations.csv`, re-import.
- Budget: ≤96/emitter, ≤400 live, ≤1 `_process`/shot zero-alloc (shots 2/4/6/7: `_PortalShot`/`_CaptureShot`/`_WalkShot`/`_RoomShot`; drift shared; CinematicFx internals zero-alloc), draws <400; station parts + base bg share one build fn.
- Refactor: extract `_build_shot1()` station into shared fn (parametrized color/alpha/breach) — only existing-code change, pure extraction.
- Tests: new `test/return_cinematic_test.tscn` mirror `intro_cinematic_test` (trigger/pause/finished/resume/skip-idempotent/Timer residue/`_shot_durations` stepping); regression: smoke, base_system, back/esc_navigation (`SKIP_RETURN`), quit-after-300; `test/return_capture.tscn` (8s/shot, `/tmp/return_shot*.png`) + ui_capture.
- Docs: `docs/EXIT_FLOW.md` register Esc; `docs/PORTING_PARITY.md` no change (archived `docs/archive/`, frozen 2026-07-30); README +1 line.

## 6. Implementation record (landed 2026-07-28)
Done: `scripts/dawn_station.gd` (`DawnStation.build(Mode.DESTROYED/PHANTOM)`; intro shot1 refactored; return shot4 + base bg share), `scripts/return_cinematic.gd` + `scenes/return_cinematic.tscn` (7 shots; 16.8s→11.8s same-day 2026-07-28 (§7); dim to full black then finished), `main.gd` chain (`_play_return_cinematic` / `_skip_return` / `_on_return_finished`; save + `_resume_from_base` unchanged), BackNavigator `SKIP_RETURN`, `play_sfx` optional `pitch_scale`, base phantom skin (UITheme PHANTOM_* + visual layer, zero logic), `test/return_cinematic_test.tscn` (42) + `test/return_capture.tscn`.

Deviations from §1–§3 (adjustments follow code; back-write here):
1. Scan band per-component α+0.15 skipped: ADD band ≈ equivalent; would need extra `_process`.
2. Particles ② spoke to-and-fro ≈ ring emission (r80–200) + slow random (ParticleProcessMaterial lacks spoke mode).
3. Shot 2: no ship (FX shot; enters shot 3).
4. Shot 3 flash: in-shot ColorRect (director `_flash` for inter-shot).
5. Button hover-line boost skipped (zero-logic first); scanline = static set (1 draw/panel); `PHANTOM_TEXT_FLICKER` inline, not constant.
6. Skip hint reuses `INTRO_SKIP`; no `RETURN_SKIP`.

## 7. Tuning (2026-07-28 round 2)
1. 16.8s→11.8s: `_shot_durations` `[1.6, 1.2, 1.4, 2.2, 1.8, 1.6, 2.0]`; KFs scaled (`u = dur/base`; shot1 ring absolute 0.4s). Shot7 dim 1.2→0.9s (`OUTRO_FADE`), BGM synced; eyelid at 1.5u, ~50% dark at close.
2. Brighten + breakage: `_build_phantom` rewritten (`#00d4ff` family: arc 0.55 / mod 0.45 / detail 0.35); `_build_body` `gaps` param + `_ring_arc` (empty = destroyed; intro_cinematic_test regression); five-piece: 3 gaps / **7 segments** (8 slots minus 1 breached 0.4–1.4 rad) 0.1s phase 0.13s / grid 2.5px α0.75 / 3 shards / glitch 3.8s (`PhantomBody` inner; breath on station container; modulates independent). Base bg (α0.12) ui_capture-checked.
3. Proportions: shot5 ship 2.8 (~420px), person 0.55 (~64px), ≈6.6:1 (target 5–8:1); hide (960,425), apex (935,390), land (905,686), deck top 710. Shot6 door 280px (frame 540..820, panels 76×272, slide ±85) vs person 185px; walk via `_stop_scroll` / `_time_u = dur/2.4`, 90px/s. Shot7 bed 260px, lying 185px, focus (981,744).
4. Verify: return_capture 8 shots + ui_capture pass; headless return_cinematic 42 / intro_cinematic 40 / smoke 116 / base_system 46 PASS.

## 8. Tuning (2026-07-30 round 3, CinematicFx)
Adopt `scripts/cinematic_fx.gd` (soft_glow / soft-dot particles / shockwave / beam / radial_streaks); storyboard/durations/skip/subtitles/BGM unchanged; `u = dur/base` kept (shot1 ring absolute 0.4s).
- Global: `_particles()` → `CinematicFx.particles()` (same dict contract); soft-dot textures (scale = pixel diameter).
- Shot1: `warp(12.0)` start (game frozen by pause; stretch decays); nozzle → `soft_glow`; peak (dur−0.4) 2 `shockwave` offset 0.14s (r700/900, ry 0.6, cyan).
- Shot2: throat pad α0.15; 2 vortex arcs ~200° (2.4 / −1.7 rad/s, in `_PortalShot._process`); inflow 40 (`EMISSION_SHAPE_RING`, accel −140/−90, y-squash, from 0.45s).
- Shot3: `CinematicFx.radial_streaks` 26, max_radius 1000, fade 0.3u; `shockwave` r520 0.4s white-cyan; trail 32→40.
- Shot4: → `CinematicFx.beam` (bezier 24 pts, dot_count 3, dot_speed 0.5); `_CaptureShot` → 8 nav lights (r=RING_RADIUS+4, `0.12+0.78·max(sin)^4`); tail 24.
- Shot5: lights α0.2 → chase by distance (fall 0.7 after peak); glass highlight slide 0.4u, fade 0.12u.
- Shot6: ribs ×12 width 22/14 + 6 plates; 3 cones ADD α0.05 + floor strip parallax.
- Shot7: `_RoomShot` (only `_process`): walk 0.6u (limb = `_WalkShot`, fade at ends) + breath 1.7u (scale.y ±2.5% sine) + 3 star dots; §6 item 4 resolved.
- Budget: max new emitter 40; ≤96 / ≤400; ≤1 `_process`/shot; no `world_scale`.
- Verify: `--headless --quit-after 5` clean; 8 screenshots reviewed (transients validated by code path).

## 9. Audio policy (2026-08-02, D18)
- Return SFX **keep per-shot levels of §2** (−6/−8/−10/−12/−14/−16/−18/−20dB + pitch) — deliberately **not unified** with intro `AUDIO_VOL_OFFSET=-6dB` / `AUDIO_PITCH=0.88` (product judgment; no change).
- To unify later: edit all `play_sfx` in `scripts/return_cinematic.gd` + back-write §9 & §2; intro policy in `docs/INTRO_CINEMATIC.md`.
