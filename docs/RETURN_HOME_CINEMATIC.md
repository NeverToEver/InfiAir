# Return Home Cinematic & Phantom Base UI

Single source of truth: return-home cinematic + phantom station base UI (sampling, concept, storyboard, base-UI re-skin, transition). Any cinematic/UI change must sync this doc. Intro symmetry: `docs/INTRO_CINEMATIC.md`.

## 0. Pre-analysis (2026-07-28, design-time research — all landed; status refs only)

### 0.1 Intro structure (design source: `docs/INTRO_CINEMATIC.md` §2)
6 shots 17.3s + title 1.2s; norms all reused by return cinematic (§2): 2.35:1 letterbox 132px, subtitle cards `INTRO_SUB_1..6`, handheld drift, `CanvasLayer(layer=35, process_mode=Always)`, paused tree, one-shot `Timer` chain, in-shot `create_tween()`, idempotent `skip()`, Esc via BackNavigator `SKIP_INTRO`, budget draws <400 / live ≤400 / ≤1 `_process`/shot.

### 0.2 Dawn station anchors (src: `csharp/godot/DawnStation.cs` `Build(Mode)`, center (960,470))
Ring + 7 segments + spokes + hub; cold steel blue-gray low-sat; breach right-bottom 0.5–1.2 rad (8 slots, breach gap 0.4–1.4 rad: one module missing). Phantom keeps geometry + breach (recognition anchor, §1.1); exact geometry/colors in `DawnStation.cs`.

### 0.3 Base UI current (src: `csharp/godot/BaseConsole.cs` + `csharp/godot/UITheme.cs`; screenshot `docs/screenshots/base.png`)
Chain: hold B in-run (`effects.home_charge_time` 1.5, `data/balance.json`) → `Main._start_homecoming()` (lock/spawner/save/warp/flash/show_base, tree paused); return via `resume_requested` → orbital strike (`csharp/godot/OrbitalStrike.cs`), same run. Layout/tokens/ports implemented as of §0.3 original; re-skin in §3 (zero logic change).

### 0.4 FX/asset inventory
All needs landed (storyboard §2 + implementation §6–§8); audio policy §9 (reuse + pitch).

## 1. Phantom station "Dawn · Echo"

### 1.1 External phantom FX (constant far/mid)
§0.2 geometry + 4 layers:

1. **Hologram base** (brightened 2026-07-28): all BLEND_MODE_ADD, cyan `#00d4ff` family — arc α0.55, modules α0.45, details/spokes/ticks/hub-ring α0.35, hub core α0.50. Breath on station `modulate` (0.85↔1.0, 4s); separate inner container glitch flash (every 3.8s, 0.3→1.0 two steps 0.04s); independent.
2. **Scan band**: α0.12 cyan ADD sweep vertical 3.5s/pass; outer ring α0.15 halo.
3. **Data particles**: 2 GPUParticles2D (≤96/emitter, ≤400): ①edge escape α0.4; ②inner flow α0.3 (ring emission ≈ radial).
4. **Breakage** (2026-07-28): a) main-arc gaps `[[0.5,1.2],[2.4,2.62],[4.9,5.06]]` via `DawnStation.BuildBody` `gaps` param (empty = destroyed look unchanged); b) 7 segments dropout 0.1s, phase 0.13s; c) energy grid 2.5px α0.75, flicker 0.3↔0.95, dropout 0.5+0.15×(grid%3)s; jagged outline α0.8 w2.5; d) 3 shards α0.35 ADD drift out; e) station glitch (see 1). "Destroyed station; data core weaves its memory."

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
| 1 | wide: ship hovers center-below; charge — nozzle (0.5,0.1,0.05)→(1,0.95,0.85), scale 1→1.8; particles converge in | 1.6s | fixed; end 0.4s distortion (concentric thin rings + 2 offset shock rings pass) | `Starfield.Warp(12.0)` start (continues `Warp(18.0)` stretch, decays); ship = player_ship.png tail (180°, intro shot-5 pose); nozzle `CinematicFx.SoftGlow()` ×2 + modulate/scale tween; converge `CinematicFx.Particles()` inward (negative vel); peak `CinematicFx.Shockwave()` ×2 (offset 0.14s, ry 0.6) | SFX_DASH −6dB 0.6× |
| 2 | FX: portal tears — ellipse Line2D cyan α0.9 point→full 0.5s; 12 glow dots circling; interior phantom blur (α0.35 + jitter) + 2 counter-rotating vortex arcs + inflow | 1.2s | push scale 1.0→1.06 | ring = shockwave recipe reversed; dots per-frame (only `_process`, zero-alloc, drives vortices); throat `CinematicFx.SoftGlow()` pad α0.15; inflow ring emission (DawnStation recipe) + negative radial accel, y-squash, from 0.45s; interior §1.1 minimal | SFX_EXPLOSION −12dB 0.5× |
| 3 | match cut: ship into portal (scale 1→0.2, flare), closes + flash → hard cut to phantom space: out (0.2→1, ease_out), closes | 1.4s | 0.63s real + 0.10s flash + 0.77s phantom; direction up-right | `CinematicFx.RadialStreaks()` (hidden with flash at part_a); `Shockwave()` white-cyan 0.4s; flash = intro 1→2 same; bg dim nebula + phantom silhouette α0.15; trail 40 | SFX_DASH 0dB; exit −10dB |
| 4 | wide→mid: station full reveal (§1.1 all 4 layers); energy capture beam pulls ship; 8 nav lights chase | 2.2s | side pan (sine 60px + scale 1.0→1.12) | `CinematicFx.Beam()` (glow + core + 3 flow dots, bezier 24 pts); 8 lights r=RING_RADIUS+4, `ReturnCinematicCaptureShot` phase pulse; short tail; pull tween TRANS_SINE; scan band | SFX_RESUPPLY −8dB |
| 5 | hangar: land drop 40px ease_out + bounce + dust; lights chase to landing point; engine off 0.3s; canopy up 0.4s + glass highlight; pilot jump down 0.5s | 1.8s | fixed low angle (horizon lower 1/3, elements −60px) | 8 lights by landing distance (tweens at build); highlight alpha Polygon2D slide + fade; pilot = shot-3 rig new poses; dust soft-dot | land SFX_EXPLOSION −18dB; jump SFX_DASH −14dB |
| 6 | side follow: walk corridor; ceiling lights per x (turn on 40px behind, dim 400px behind — lerp-smoothed); stop at door, slides open (2 panels + light leak) | 1.6s | pan, pilot at left 1/3 | walk = shot-3 run slowed (~90px/s, half stride); 12 Line2D lights by x threshold (only `_process`); ribs ×12 + plates + 3 cones ADD α0.05 parallax + floor strip; door 2 Polygon2D | steps SFX_BUFF_PICK −20dB ×6 @0.4s; door SFX_DASH −10dB 0.7× |
| 7 | room: walk to bed; sit→lie (0.6s); stretch; breathing; close-up scale→1.6: eyelids 0.8s; dim 0.9s | 2.0s | mid 0.8s → close-up 0.7s → dim 0.9s overlap | `ReturnCinematicRoomShot` (only `_process`): walk 0.6u (limb = `ReturnCinematicWalkShot`, weight fade, ends upright) + breath 1.7u (scale.y ±2.5% sine) + 3 star dots; sit/lie/eyelid/push 0.6s KF tweens; dim fullscreen ColorRect; BGM fadeout | lie SFX_RESUPPLY −16dB; BGM → −40dB |

Transitions: 1→2 black 0.3s (suspense); 2→3 continuous; 3 internal flash 0.10s; 3→4 black 0.3s; 4→5 black 0.3s; 5→6, 6→7 black 0.3s; 7 end dim 0.9s → base UI directly (no title freeze; its slot = base UI fade-in, §4).

Symmetry: intro "destroy → escape → deep space (tense, warm)" ↔ return "jump → captured → sleep (calm, cold)"; intro1 ↔ return4 station; intro5 (away) ↔ return3 (arrive); intro3/4 indoor ↔ return6/7; intro title ↔ return UI fade-in.

## 3. Base UI re-skin

### 3.1 Principle
**Zero logic change**: `BaseConsole.cs` signals/bindings/GameState API/callbacks untouched. Only ①UITheme phantom tokens + factories; ②`BaseConsole.cs` `_Ready()` visuals (dim, panel style, bg + decor layers). Existing tests (`base_system_test` etc.) must stay green.

### 3.2 New tokens (coexist, no replace)

| Token | Value | Use |
| --- | --- | --- |
| `PhantomBg` | (0.01, 0.03, 0.06, 0.90) | fullscreen bg (colder/deeper) |
| `PhantomPanelBg` | (0.03, 0.08, 0.12, 0.55) | more transparent, holographic |
| `PhantomBorder` | (0.0, 0.83, 1.0, 0.65) | border, brighter than PANEL_BORDER |
| `PhantomScan` | (0.0, 0.83, 1.0, 0.06) | scanline overlay |
| text flicker (inline in `BaseConsole.cs`, not a constant — §6 deviation 5) | α 0.92–1.0 jitter | decor labels only |

Panel = ChamferedPanel (PhantomPanelBg + PhantomBorder) + scanlines (1px/4px or sweep band, mouse_filter=IGNORE) + frosted ≈ lower alpha + radial glow pad (no blur shader, gl_compatibility). Hover float: AccentDim line 2px below + boost on hover (no callback change). Icons: 16×16 Line2D glyphs (ship/wrench/cross/flag) left of 4 titles. Flicker: RP + titles only — 3Hz sine 0.92–1.0 + 1px offset 0.06s every 2.7s (tween loop, no `_process`); body static.

### 3.3 Layout (zones/logic reused)
Old: dim → title 44 → gold RP → HBox sep20 → primary. New: PhantomBg + phantom concept layer (ring r≈520 @ (960,540) α0.10 + light particles + 8s/pass band; after dim, before CenterContainer) → title (flicker) → RP (flicker) → HBox sep 140 (120px gap reveals ring axis = "panels around core"; containers/focus unchanged; floating-ring option B rejected) → phantom panels → primary (hologram).

Changes: ①bg layer; ②sep 20→140; ③panel style + scanlines + icons; ④primary + bottom line + 1.5s breath glow (size/pos/callback same); ⑤open: 0.25s hologram boot (α0 + scale 0.98→1.0) before `AnimateOpen`; `StaggerOpen` 60ms kept.

### 3.4 Ports (design coords, panel top-left after centering)

| Port | New |
| --- | --- |
| hangar `BuildHangar` | (320, 300) |
| supply `BuildSupply` | (320, 520) |
| routes `BuildRoutes` | (1040, 300) |
| missions `BuildMissions` | (1040, 520) |
| continue `OnResumePressed` | (820, 810), 280×52 |
| title/RP `Refresh` | top center + flicker |

(Coords indicative; real layout from CenterContainer; for screenshot checks.)

## 4. Cinematic → base UI transition

### 4.1 Chain (`csharp/godot/Main.cs`)

```
void StartHomecomingInternal()                 # current skeleton kept
    _homecoming=true; lock input; stop spawner; recall mothership; GameState.SaveRun(...)
    _starfield.Warp(18.0)                      # feeds shot-1 charge/stretch
    PlayReturnCinematic()                      # replaces _flash_white + ShowBase

void PlayReturnCinematic()                     # like PlayIntroCinematic
    _return = ReturnScene.Instantiate<ReturnCinematic>(); _return.Finished += OnReturnFinished
    AddChild(_return)                          # CanvasLayer layer=35, Always
    GetTree().Paused = true

void OnReturnFinished()                        # skip & natural end same exit
    _return = null; _baseUi.ShowBase()         # §3 skin; tree stays paused
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
- Skip: Esc (BackNavigator `SKIP_RETURN`, priority = `SKIP_INTRO`) / any key / click → `Skip()` → `OnReturnFinished()`; `Skip()` kills BGM tween, sets target volume. **Grace `SKIP_GRACE` s (config `effects.return_skip_grace`, default 1.2)**: `Skip()` ignored within — held WASD/Shift/Space don't skip; check inside `Skip()`, all routes gated; natural end unaffected.
- Save timing: `SaveRun()` before cinematic (`Main.cs:947`); skip/crash safe.
- Pause: tree paused through cinematic + UI (both Always); `ResumeFromBaseInternal()` → orbital strike (`csharp/godot/OrbitalStrike.cs`; Always, clears on hit frame, unpauses).
- Future "skip after first view": GameState flag → old flash path; not implemented, fork reserved.

## 5. Constraints & tests
- i18n: `RETURN_SUB_1..7`, `RETURN_SKIP` (or reuse `INTRO_SKIP`), zh+en in `data/translations.csv`, re-import.
- Budget: ≤96/emitter, ≤400 live, ≤1 `_process`/shot zero-alloc (shots 2/4/6/7: `ReturnCinematicPortalShot`/`ReturnCinematicCaptureShot`/`ReturnCinematicWalkShot`/`ReturnCinematicRoomShot`; drift shared; CinematicFx internals zero-alloc), draws <400; station parts + base bg share one build fn.
- Refactor: extract intro `BuildShot1()` station into shared fn (parametrized color/alpha/breach) — only existing-code change, pure extraction.
- Tests: new `test/return_cinematic_test.tscn` mirror `intro_cinematic_test` (trigger/pause/finished/resume/skip-idempotent/Timer residue/`_shot_durations` stepping); regression: smoke, base_system, back/esc_navigation (`SKIP_RETURN`), quit-after-300; `test/return_capture.tscn` (8s/shot, `/tmp/return_shot*.png`) + ui_capture.
- Docs: `docs/EXIT_FLOW.md` register Esc; `docs/PORTING_PARITY.md` no change (archived `docs/archive/`, frozen 2026-07-30); README +1 line.

## 6. Implementation record (landed 2026-07-28)

> §6–§8 are pre-M7 GDScript-era records (`scripts/dawn_station.gd` / `return_cinematic.gd` / `main.gd` / `cinematic_fx.gd`); since M7 (2026-08-08) implementation lives in `csharp/godot/ReturnCinematic.cs` / `DawnStation.cs` / `CinematicFx.cs` (`scripts/` is zero-`.gd`). Per-shot details & commit history: git log.

Landed 2026-07-28: 7 shots + `scenes/return_cinematic.tscn` (16.8s→11.8s, §7) + `Main` chain (`_play_return_cinematic` / `_skip_return` / `_on_return_finished`; save + `_resume_from_base` unchanged) + BackNavigator `SKIP_RETURN` + `play_sfx` optional `pitch_scale` + base phantom skin (UITheme PHANTOM_* + visual layer, zero logic) + `test/return_cinematic_test.tscn` (42) + `test/return_capture.tscn`.

Deviations from §1–§3 (adjustments follow code; back-write here):
1. Scan band per-component α+0.15 skipped: ADD band ≈ equivalent; would need extra `_process`.
2. Particles ② spoke to-and-fro ≈ ring emission (r80–200) + slow random (ParticleProcessMaterial lacks spoke mode).
3. Shot 2: no ship (FX shot; enters shot 3).
4. Shot 3 flash: in-shot ColorRect (director `_flash` for inter-shot).
5. Button hover-line boost skipped (zero-logic first); scanline = static set (1 draw/panel); `PHANTOM_TEXT_FLICKER` inline, not constant.
6. Skip hint reuses `INTRO_SKIP`; no `RETURN_SKIP`.

## 7. Tuning (2026-07-28 round 2)
16.8s→11.8s (`_shot_durations` `[1.6, 1.2, 1.4, 2.2, 1.8, 1.6, 2.0]`, KFs scaled) + brighten/breakage (cyan family + 3 gaps / 7 segments / grid / 3 shards / glitch) + shot5/6/7 proportions; verified (return_capture 8 shots + ui_capture; headless return_cinematic 42 / intro_cinematic 40 / smoke 116 / base_system 46 PASS). Details in git log.

## 8. Tuning (2026-07-30 round 3, CinematicFx)
Adopt `CinematicFx` (soft_glow / soft-dot particles / shockwave / beam / radial_streaks) per-shot; storyboard/durations/skip/subtitles/BGM unchanged; budget kept (max new emitter 40, ≤96/≤400, ≤1 `_process`/shot, no `world_scale`); verify: `--headless --quit-after 5` clean + 8 screenshots reviewed. Details in git log.

## 9. Audio policy (2026-08-02, D18)
- Return SFX **keep per-shot levels of §2** (−6/−8/−10/−12/−14/−16/−18/−20dB + pitch) — deliberately **not unified** with intro `AUDIO_VOL_OFFSET=-6dB` / `AUDIO_PITCH=0.88` (product judgment; no change).
- To unify later: edit all `PlaySfx` calls in `csharp/godot/ReturnCinematic.cs` + back-write §9 & §2; intro policy in `docs/INTRO_CINEMATIC.md`.
