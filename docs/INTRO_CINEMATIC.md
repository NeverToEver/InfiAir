# Intro Cinematic (Design & Decision Doc)

Single source of truth for the intro cinematic: storyboard, tech design, phases, DoD acceptance, progress log. Any intro change must sync the progress log (§7) and acceptance checkboxes (§6).

## 1. Goal & Trigger

- 6-shot hard-SF intro (station explosion → pilot sprint → cockpit launch → eject → departure), sets up the backstory.
- Trigger: welcome "New Game" (`scripts/welcome.gd:392` `_on_new_game_pressed` → `_goto_main()` → Main `_apply_new_run()`); seamless entry into the run after play or skip.
- No trigger: "Continue Run", tutorial, test scenes (tests instance main.tscn as child; `get_tree().current_scene != Main` blocks it).
- Skip: Esc (via BackNavigator), any key or click → straight into run.
- Implementation: fully procedural 2D (Polygon2D / Line2D / GPUParticles2D / Tween / Label); no video files, no new deps — per offline-assets policy.

## 2. Storyboard

Total 17.3s = six shots 16.1s (incl. transitions + 0.7s end fade) + title card 1.2s (0.2s in + 0.8s hold + 0.2s out). Each shot = in-code node tree, director-built/switched. 2.35:1 letterbox (132px bars top/bottom, design coords); per-shot subtitle card (`INTRO_SUB_1..6`, zh+en, fade-in 0.3s, readable ≥1.2s).

| # | Shot | Dur | Visual implementation | SFX |
| --- | --- | --- | --- | --- |
| 1 | Far push-in | 2.8s | Starfield bg; ring station = Polygon2D arcs + bay rects (DawnStation destroyed); scale 0.7→1.0; blast core = soft_glow dark-red→orange-white + grow; GPUParticles2D ×3 (debris/dust FG/embers); 8 bay lights off by angle to breach; 2nd detonation dur*0.45 (flash + CinematicFx.shockwave double ring + shake + decaying vol); FG wreck warm rim | `GameState.SFX_*` explosion + decaying 2nd blast |
| 2 | X-ray chain | 2.5s | cold-blue base (0.02,0.05,0.12); Line2D wireframe (4 decks + frames + bulkheads); chain = preset polyline lights up (orange-red, widening); node soft_glow flash (0→1.4→fade) + one-shot 24 sparks, 0.2s/node; 12 top LEDs cyan→red w/ wavefront (Timer-step recolor) | 3 short blasts |
| 3 | Pilot sprint | 2.5s | side corridor: perspective lines + yellow stripes scroll back; pilot = multi-segment flightsuit figure (2-joint limbs, 2-phase run, `_process` phase-driven); red fullscreen ColorRect 6Hz sine; steam = white soft-dot particles up; 5 volumetric cones; FG struts ×4 at -1600px/s (> mid -900); speed lines | optional low-freq alarm (P2) |
| 4 | Console launch | 2.5s | cockpit FG frame; 3-zone flight deck (buttons + slider/knob/lever) hi-freq blink; main = red Label 3→2→1 + tr("INTRO_WARNING") flash + INTRO_LOG_1..4 logs; left radar (ring + sweep + 2 blips w/ afterglow, `_ConsoleShot._process` zero-alloc); status LEDs; 2 grips; end 0.5s lean-back (rotation -3°) + white glow | countdown end → shot-5 engine |
| 5 | Eject chase | 2.8s | ship texture (tail view, 1.4×, center-low, modulate (0.85,0.85,0.92) + warm rim, shot-6 silhouette ratio); warm-up ~0.3s (scaled by dur): amount_ratio 0→1 + glow pop + white flash; rails = perspective walls + slanted lines; flames = twin jets + white core + side tongues (soft-dot, one size step larger); rail sparks; speed lines + radial lines; ±6px shake | engine/accel (existing) |
| 6 | Wide closure | 3.0s | nebula = 3 large soft_glow (purple/blue, drift); star = top-right soft_glow + anamorphic flare (wide pulsing bar); planet arc = 64-seg huge radius + dark fill + squashed cyan atmosphere band; wreck = bottom-left dark polygon + ember flickers; ship = small silhouette accel top-right (ease_in); fleet = 2×3 dots + trails; last 0.7s fade black | none (BGM takes over) |
| 7 | Title card | 1.2s | fullscreen CenterContainer + title Label (UITheme.FONT_DISPLAY/ACCENT) + accent dash; modulate.a=0; director tween (0.2 in → 0.8 hold → 0.2 out → shared skip exit) | none |

Transitions (differentiated): 1→2, 4→5 = 0.10s white flash (ColorRect 0→1, next recovers 0.28s); others = 0.3s blackout (0→1 / next 1→0); shot 6: 0.7s fade → title → fade-out → run. Handheld drift: shared container low-freq sine ±3px + micro rotation, single `_process`, zero heap alloc.

## 3. Technical Design

### 3.1 File list

| File | Duty |
| --- | --- |
| `scenes/intro_cinematic.tscn` | root: CanvasLayer (layer=35, process_mode=Always) + black bg + skip hint |
| `scripts/intro_cinematic.gd` | director: timeline, build/switch/destroy shots, skip, `finished` signal |
| `scripts/main.gd` | in `_apply_new_run()`: play only when `get_tree().current_scene == self`; `get_tree().paused = true` during play, restored on finish/skip |
| `scripts/back_navigator.gd` | new `BackAction.SKIP_INTRO`: Esc = skip (before base/Buff branches, before paused-IGNORE branch) |
| `data/translations.csv` | new keys: `INTRO_SKIP`, `INTRO_WARNING`, `INTRO_SUB_1..6` (zh+en), `INTRO_ZONE_PROP/NAV/WPN` (zone plates), `INTRO_LOG_1..4` (shot-4 logs) |
| `scripts/cinematic_fx.gd` | CinematicFx: soft_glow, soft-dot particle factory, shockwave double ring; `_particles` + glows r≥10 delegated/reused; LEDs r≤4 keep `_GlowDot` |
| `test/intro_cinematic_test.tscn/.gd` | headless self-check (§5) |
| `test/intro_capture.tscn/.gd` | windowed per-shot screenshot tool (8s/shot, /tmp/intro_shot1..6.png + title) |
| `docs/EXIT_FLOW.md` | registers return behavior (Esc = skip intro) |

### 3.2 Key decisions

- **Timing**: no `await get_tree().create_timer()` coroutines (AGENTS.md: leaks on exit); one-shot `Timer` + signals chain shots; in-shot animation via `create_tween()` (node-bound) + `_process()`.
- **Pause**: `get_tree().paused = true` during play (run frozen at frame 0); root `process_mode = Always`; `_play_intro_cinematic()` sets pause directly (StartPanel retired 2026-08-04, no `_dismiss()` path).
- **Skip**: `skip()` idempotent — stop all Timers, emit `finished`, queue_free; main restores `paused = false` in `finished` callback. Skip and natural end share one exit; no duplicate cleanup.
- **Esc routing**: BackNavigator `decide_back_action()` → `SKIP_INTRO`; `go_back()` → `Main._skip_intro()`; other keys/clicks via `_unhandled_input`. Start panel hidden + paused → existing branches can't misfire (before paused-IGNORE branch).
- **Test gate**: `get_tree().current_scene == self` (normal launch only); tests call `Main._play_intro_cinematic()` directly.
- **Reuse**: Starfield via `scripts/starfield.gd`; explosion/fire per `scripts/explosion.gd` additive palette; UITheme tokens; SFX via `GameState.play_sfx()` existing constants, no new audio; **audio policy**: volumes shifted by `AUDIO_VOL_OFFSET` (-6dB) + pitch `AUDIO_PITCH` (0.88).
- **Viewport**: 1920×1080 design coords (CanvasLayer stretches via canvas_items; never runtime window size).
- **Cleanup**: whole tree queue_free after `finished`; no Timer/tween/particle leftovers; `Engine.time_scale` untouched.

## 4. Phases

- **P1 (delivered)**: doc + director framework (timeline/transitions/skip/cleanup) + 6-shot layers + hookup + Esc/click skip + i18n keys + headless test + EXIT_FLOW.md/AGENTS.md sync.
- **P2 (done later)**: visual polish — density, deck thickness, volumetric light/motion blur, color grading; per-shot SFX balance; shots 3/4 detail.
- **P3 (delivered)**: polish + perf budget:
  - letterbox 132px (`intro_cinematic.tscn` LetterboxTop 0–132 / LetterboxBottom 948–1080, consistent w/ §2 and RETURN doc §0.1); skip hint in top bar
  - per-shot subtitle cards (zh+en); multi-layer parallax (shot1 starfield + FG debris, shot6 nebula); transitions (1→2, 4→5 white flash, rest blackout); handheld drift (low-freq sine + micro rotation)
  - per-shot extras: shot2 scan grid + node ripples; shot3 rotating light + visor highlight; shot4 HUD + top strip + knuckles; shot5 white-hot core + strobe dots + radial lines; shot6 anamorphic flare + fleet trails; optional title card (if added, total ≤25s — sync duration metric)
  - Perf: emitter ≤96, alive ≤400, ≤1 `_process`/shot zero-alloc, merge static elements, tween over per-frame code; sample draw calls/objects/frame time into §7.
- **P4 (registered leftover)**: low-end retest + gamepad/mobile input check (manual); README line done (README.md:42); gamepad skip: only B=ui_cancel via BackNavigator, other buttons don't skip

## 5. Testing (P1 deliverable)

`test/intro_cinematic_test.tscn` (headless, [PASS]/[FAIL]):

1. `Main._play_intro_cinematic()`: node exists, tree paused.
2. `skip()`: destroyed, `finished` emitted, tree unpaused, no Timer leftovers.
3. Timeline: durations → very short (`_shot_durations` writable), advance 6 shots, assert per-shot create/destroy + final `finished`; real Timers, no mock.
4. Gate: in test scene (`current_scene != Main`) `_on_new_game_pressed()` doesn't trigger.
5. Regression: `smoke_test`, `startup_flow_test`, `back_navigation_test`, `esc_navigation_test`, `ui_capture` green.

## 6. Acceptance (DoD)

### Phase 1 — all checked

- [x] New Game → cinematic → auto-run (frame-0 intact) (`intro_cinematic_test` asserts trigger/pause/finished/resume)
- [x] Continue Run / tutorial don't play (`_on_continue_run()` unchanged; gate `current_scene == self`)
- [x] Esc / any key / click skip → immediate run, no pause leftover (3 paths asserted)
- [x] 6 shots per §2, total 17.3s±0.5s (16.1s + title 1.2s), no flash-through (2.8+2.5+2.5+2.5+2.8+3.0=16.1s; transitions inside shot durations; `finished` window covers title)
- [x] No node/Timer/tween leaks (Timer count → baseline, instance destroyed)
- [x] `INTRO_SKIP`/`INTRO_WARNING` zh+en (zh via screenshots; en same tr() path)
- [x] `intro_cinematic_test` + §5 regression green (smoke / startup_flow / back_navigation / esc_navigation / ui_capture)
- [x] ≥1 screenshot/shot checked (/tmp/intro_shot1..6.png, 2026-07-27: all in place, no blanks/overlaps/overflow)

### Phase 2 — [x] 2026-07-27 (/tmp/intro_p2_shot1..6.png: per-shot polish verified; global vignette + cold tone; no overlap/overflow/occlusion)

### Phase 3 / 4

- [x] P3 polish verified 2026-07-27 (/tmp/intro_p3_shot1..7.png: 132px bars + cards per shot; shot1 FG debris parallax, shot2 scan grid + band + ripples, shot3 light cone + visor highlight, shot4 progress ring + scan arc + logs + strip + knuckles, shot5 white-hot core + strobe dots + radial lines, shot6 nebula drift + anamorphic flare + fleet trails; title ok; subtitles don't cover subjects)
- [x] P3 perf met (§7): draw calls peak 296 < 400; objects 315; CPU 0.20ms no >4ms spike; particles shot5 40×2+32×2+24×2=192 ≤ 400, emitter ≤96; ≤1 `_process`/shot zero-alloc (1/2/6 none, 3/4/5 one each); `intro_cinematic_test` (40 asserts) + smoke + back/esc_navigation + quit-after-300 green
- [ ] P4 leftover (manual: low-end retest + gamepad/mobile check)

## 7. Progress Log

Append a new entry on every change.

| Date | Phase | Entry | Status |
| --- | --- | --- | --- |
| 2026-07-27 | P1 | doc created | done |
| 2026-07-27 | P1 | impl: director + 6-shot layers + hookup + skip + tests; §6 P1 checked | done |
| 2026-07-27 | P2 | polish per shot (density/shockwave, deck thickness/glow, pilot readability, hands/sub-screen/tension, wall flow/trail/flames, silhouette/debris) + vignette + SFX balance; §6 P2 checked | done |
| 2026-07-27 | P3 | letterbox, subtitle cards, parallax, white-flash transitions (1→2, 4→5), drift, per-shot extras, title card (total then 24.8s); §6 P3 checked | done |
| 2026-07-27 | P3 | duration cut 24.8→17.3s: 2.8/2.5/2.5/2.5/2.8/3.0 + title 1.2s; sub-rhythms — shot2 chain 0.25→0.2s/node, shot4 countdown 1.0→0.6s/digit (scan arc synced), shot3 alarm 1.0→0.7s, shot5 engine 1.6→1.1s, shot1 shockwave 0.3/0.7→0.2/0.5s, subtitle fade 0.5→0.3s, blackout 0.4→0.3s, white flash 0.12/0.35→0.10/0.28s, end fade 1.0→0.7s; others untouched; tests green | done |
| 2026-07-27 | P3 | shot3/4 refinement: shot3 → multi-segment flightsuit figure (helmet/neck/ribcage/pelvis/2-joint limbs/boots + chest pack/life pack/shoulder pads/waist mounts), 2-phase run (hip ±0.72rad, knee back-kick 1.3rad, shoulder anti-phase, elbow bent, torso bob 2×, zero-alloc, lean 0.3rad); shot4 → slanted 3-zone deck (PROP: button cluster + dual throttle slots; NAV: dual knobs + button row; WPN: 3 levers + button cluster; LED row + plates INTRO_ZONE_PROP/NAV/WPN) + bezel/glass highlight + 4-finger+thumb hands w/ press motion + grip-fist ending; fixed shot-5 engine Timer on director → lambda capture-after-free on fast-forward (moved to shot root); perf shot3 97dc/115objs, shot4 137/195 <400; regression green | done |
| 2026-07-27 | P3 | shot3 run-cycle fix: 3 sign/phase errors — ① knee `0.08+max(0,sin(p-1.8))*1.35` (was kicking forward); ② elbow `-(1.0+sin(p+0.8)*0.25)` (forearm forward); ③ bob `0.5+0.5cos(2·run_phase)` (lowest at mid-stance); facing (+x) vs bg (-x), shoulder anti-phase, lean 0.3rad untouched; tests green | done |
| 2026-07-27 | P3 | detonation shake: static `_kick_shake(host, amp, state)` — 3 tween pulses (amp peak 0.04s → 40% rebound 0.08s → baseline 0.15s, total 0.27s); state[0] kills old tween for chained refresh; on shot root (baseline ZERO), no clash w/ `_shot_root` drift, zero `_process` cost; shot2 amp=4.0/node, shot1 amp=6.0; peak ≈3.5px, exact (0,0) at chain end | done |
| 2026-07-30 | P3 | visual upgrade: `_particles` → CinematicFx.particles (soft-dot, one size step larger); r≥10 glows → soft_glow (r≤4 `_GlowDot`); per-shot enrichment per §2 (ember layer, 8 bay lights, dur*0.45 2nd detonation, wreck rim, sparks, 12 LEDs → red, struts ×4, cones 3→5, INTRO_LOG_1..4 rotation, radar sweep, warm-up, rail sparks, ship dim + rim, planet arc, fleet 2×3, star soft_glow); added test/intro_capture; shot5 alive peak 248 ≤400, shot1 128, emitter ≤96; 40 asserts exit 0; perf maintained | done |

### P3 perf sampling (2026-07-27)

Windowed 5-frame peaks (draw calls / objects); CPU = headless `--fixed-fps 1000`, 200-frame peak/shot. Sampling scene deleted after.

| Shot | Draw calls | Objects | CPU frame peak |
| --- | --- | --- | --- |
| 1 | 296 | 315 | 0.18ms |
| 2 | 48 | 69 | 0.01ms |
| 3 | 75 | 93 | 0.03ms |
| 4 | 93 | 154 | 0.01ms |
| 5 | 56 | 73 | 0.08ms |
| 6 | 271 | 287 | 0.20ms |
| Title | 6 | 20 | — |

Budget: draw calls <400 ✓ (296); alive particles ≤400 ✓ (shot5 192; emitter ≤96); ≤1 `_process`/shot zero-alloc ✓ (1/2/6 none); CPU no >4ms spike ✓ (0.20ms). Note: windowed Performance counters read 0 when occluded; sample on screenshot frames (frame_post_draw + pixel readback).
