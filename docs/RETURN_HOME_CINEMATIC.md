# Return Home Cinematic & Phantom Base UI Design Doc

This document is the single source of truth for the "return-home cinematic + phantom space-station base UI": sampling conclusions, concept design, storyboard, UI overhaul plan, and transition mechanism.
Any cinematic/UI change during implementation must be reflected here. For the symmetry with the intro cinematic, see `docs/INTRO_CINEMATIC.md`.

---

## 0. Pre-Production Sampling Summary (sampled 2026-07-28)

### 0.1 Intro Cinematic Storyboard Structure (source: `scripts/intro_cinematic.gd` + `docs/INTRO_CINEMATIC.md`)

- **Total 17.3s** = 6 shots 16.1s (transitions included in shot durations) + title card 1.2s (0.2 fade-in / 0.8 hold / 0.2 fade-out).
- **Duration table**: `[2.8, 2.5, 2.5, 2.5, 2.8, 3.0]` (director exposes writable `_shot_durations` for tests).

| # | Name | Duration | Camera | Content | Transition |
| --- | --- | --- | --- | --- | --- |
| 1 | Wide push-in | 2.8s | Container scale 0.7→1.0 constant push-in | Ring station "Dawn" explodes in deep space; debris/dust two-layer particles | →2 white flash 0.10s (next shot recovers at 0.28s) |
| 2 | X-ray chain detonation | 2.5s | Fixed | Cold-blue cross-section wireframe, orange-red energy chain-detonates along a polyline at 0.2s/node, nodes tremble | →3 blackout 0.3s |
| 3 | Pilot sprint | 2.5s | Background scrolls opposite | Multi-segment flight-suit figure, two-beat run cycle (phase-driven), red alarm 0.7s breathing | →4 blackout 0.3s |
| 4 | Console emergency boot | 2.5s | Fixed + end tilt back -2° | Three-section avionics console, countdown 0.6s/digit, grip handles | →5 white flash |
| 5 | Ejection tail-chase | 2.8s | Rail rungs accelerate backward + ±6px shake | Player ship sprite rotated 180°×1.4, bright-white engine core, radial speed lines | →6 blackout 0.3s |
| 6 | Wide closing | 3.0s | Ship departs with ease_in | Layered nebula, anamorphic glow, wreck silhouettes, supply-ship formation lights; 0.7s fade-to-black at end | →Title card |
| 7 | Title card | 1.2s | — | InfiAir title fade-in/hold/fade-out | Unified exit `skip()` |

- **Global conventions**: 2.35:1 letterbox (132px black bars top/bottom in design coords); per-shot narrative subtitle cards `INTRO_SUB_1..6` at the bottom (0.3s fade-in, fade out with the transition); director-level handheld drift (shared container, low-frequency sine ±3px + slight rotation, single `_process`, zero allocation); all in 1920×1080 design coords.
- **Technical skeleton**: root scene `CanvasLayer(layer=35, process_mode=Always)`; `get_tree().paused = true` while playing; one-shot `Timer` nodes chain the shots (no `create_timer` coroutines); in-shot animation via `create_tween()` bound to nodes; `skip()` idempotent unified exit; Esc routed via BackNavigator `SKIP_INTRO`.
- **Composition helpers (directly reusable factories)**: `_glow()` (additive glow dot), `_line()`, `_rect_poly()`, `_bg_rect()`, `_particles()` (hard cap ≤96 per emitter), `_kick_shake()` (detonation tremor, refreshed/stacked via a state array).
- **Performance budget**: draw calls <400, live particles ≤400, at most one `_process` per shot with zero allocation, static elements batched.

### 0.2 Original Station "Dawn" Visual Key Points (source: `_build_shot1()`, station center (960,470))

| Piece | Geometry | Color |
| --- | --- | --- |
| Ring main arc | r=260, closed 48-segment Line2D, width 26 | (0.38, 0.45, 0.58) cold steel blue-gray |
| Inner/outer detail rings | r=232 / r=288, 64 segments, width 2 | (0.55, 0.65, 0.8, 0.5) |
| Compartment hatch marks | 16 radial short ticks r244→276, width 3 | (0.18, 0.22, 0.3) |
| Compartment modules ×8 | 64×40 rects at r=260 (tangential) + 70×46 outline + spokes r70→240 width 8 | (0.48, 0.56, 0.68) / outline (0.6, 0.7, 0.85, 0.35) / spokes (0.3, 0.36, 0.48) |
| Central hub | Glow disc r=66 (non-additive) + detail ring r=46 width 2.5 | (0.28, 0.34, 0.45) / (0.5, 0.6, 0.75, 0.6) |
| Breach segment | Angles 0.5–1.2 rad: dark arc overlay (width 30, (0.05,0.05,0.08)) + jagged breach polygon + 3 peeling shards drift outward tumbling | Near-black |
| Narrative identity | `INTRO_SUB_1`: Ring station "Dawn" · core overload → chain-detonated layer by layer in shot 2 → destroyed | — |

**Design keywords**: giant ring + 8 compartments + spokes + central hub; low-saturation cold steel blue-gray; breach in the lower-right quadrant (0.5–1.2 rad). The phantom treatment must preserve this geometry and breach position — they are the player's recognition anchor.

### 0.3 Base UI Current State (source: `scripts/base_console.gd` + `scripts/ui_theme.gd` + `docs/screenshots/base.png`)

- **Trigger chain**: in-run long-press B (`HOME_CHARGE_TIME` charge) → `Main._start_homecoming()`: lock input → stop spawner → retract mothership → `GameState.save_run()` → `_starfield.warp(18.0)` → white flash (0.5s fade-in + 0.5s hold + 0.3s fade-out) → `_base_ui.show_base()` → tree pauses. Return: `resume_requested` → `_resume_from_base()` triggers the orbital-strike clear animation (`scripts/orbital_strike.gd`; clears on the hit frame and resumes the same run; earlier versions were a silent clear + short white flash, later upgraded to the full animation).
- **Layout**: CanvasLayer > fullscreen dim ColorRect(0.02,0.03,0.08,0.95) > CenterContainer > VBox (title BASE_TITLE font 44 → gold RP balance → two-column HBox separation 20 → primary "Continue Sortie" button 280×52). Left column: hangar (status overview) + repair/supply (repair/recharge buttons); right column: weapon mounts · perk routes (offense/mobility rows of two option buttons each) + mission planning (mission row + claim button). All four are ChamferedPanels (min width 560).
- **Style spec (UITheme tokens)**: panel bg navy (0.039,0.063,0.102,0.78), 1px cyan border (0,0.83,1,0.5), primary accent cyan `#00d4ff`, secondary hologram blue `#0080ff`, value gold `#d8a868`, text `#e0e8f0`/`#8a9bb0`, chamfered right-angle panels; buttons normal transparent+cyan border / hover 12% cyan fill / pressed 25%; font scale 72/40/28/24/18; font NotoSansSC only; animations `animate_open` 200ms fade-in / `stagger_open` 60ms per-child fade-in (both animate only modulate.a, never position).
- **Functional ports (logic reuse targets)**: `_on_repair_pressed` (2RP restores full HP), `_on_recharge_pressed` (2RP full fuel), `_on_route_pressed` (pick one of two perks), `_on_claim_pressed` (claim mission), `_on_resume_pressed` (continue sortie); all data flows through `GameState` (rp/buff_count/choose_route/claim_mission/mission_progress).

### 0.4 Available Effects & Assets Inventory

| Needed effect | Reusable existing assets | Gap |
| --- | --- | --- |
| Warp drive charge/warp | `Starfield.warp(factor)` (star streaks, warp_factor lerps back to 1 each frame); return already uses `warp(18.0)`; shot-5 engine-flame particle recipe (bright-white core layers) | Engine nozzle dark-red→white-hot gradient is new (glow dot + modulate tween suffices) |
| Portal opening | No existing portal; reusable: shockwave expanding-ring recipe (thin ring fast-expand + fade, additive), shot-2 wireframe style, white-flash/blackout transition pieces | Portal body (energy-churning ring + interior view) is new |
| Capture track/tractor beam | `mothership.tscn` `TractorBeam` Polygon2D (soft beam, 8Hz pulse `0.55+0.45·sin`) + docking snap tween (1.5s TRANS_CUBIC EASE_IN_OUT) | Translucent energy track (long flowing-light Line2D) is new |
| Comms/subtitles | Cinematic subtitle-card mechanism (INTRO_SUB_*); `CommOverlay` typewriter comms box (layer=12, reusable with a different border color) | Return subtitle keys RETURN_SUB_1..7 to add (zh/en columns) |
| Protagonist | No sprite; the intro shots 3/4 multi-segment flight-suit figure is procedural (helmet/torso/pelvis/double-jointed limbs, phase-driven `_process` zero allocation) — a walk cycle comes from retuning phase params | Sitting/lying/eyes-closed are new poses, small effort (same rig, different keyframes) |
| Fighter | `assets/sprites/player_ship.png` (dark swept wings, cyan-blue cockpit; already reused as tail-chase/silhouette in intro shots 5/6) | None |
| Apron/lounge | **No existing scene assets at all**; the mothership sprite is the only "large platform" reference | All procedural, built new (interior solid areas defined in §1.3 below) |
| Audio | Only 10 SFX: bgm_loop / buff_pick / bullet_fire×3 / dash / explosion / explosion_big / player_hit / resupply | No footsteps/door/warp-specific sounds; mapping in the §2 audio column (all reuse existing constants + pitch shift, no new assets; offline generation re-evaluated in phase 2) |

---

## 1. Phantom Station "Dawn Echo" Concept Design

### 1.1 External Phantom Effects (constant state, far/mid shots)

Built on the §0.2 station geometry (ring r260 / 8 compartments / spokes / hub / breach 0.5–1.2 rad kept in place), four phantom transforms are applied:

1. **Holographic base** (after the 2026-07-28 brightening pass): all pieces switch to additive (BLEND_MODE_ADD), palette mapped to the hologram cyan `#00d4ff` family — main arc α0.55, compartment modules α0.45, detail rings/spokes/ticks/hub ring α0.35, hub center α0.50. Breathing lives on the station container's `modulate` (0.85↔1.0, 4s period); a separate inner container carries the glitch flicker (one 0.3→1.0 two-step flash every 3.8s, 0.04s per step), the two never overlap.
2. **Scanline glow**: a bright scan band (α0.12 cyan gradient, additive) sweeps down the station at constant speed (3.5s/pass, loops bottom→top); a permanent α0.15 large-radius glow halo rings the outer edge.
3. **Data-stream particles**: two GPUParticles2D sets (within the ≤96/emitter, ≤400 total budget): ① edge dissipation (α0.4); ② internal structure flow (α0.3, ring emission domain approximating radial flow).
4. **Brokenness** (five-piece set added 2026-07-28): a) main-arc breaks — the main ring is drawn in segments per gap table `[[0.5,1.2],[2.4,2.62],[4.9,5.06]]`, leaving 3 gaps (`_build_body` `gaps` param; the destroyed state passes an empty table, visually unchanged); b) compartments drop out one by one — each of the 6 compartments flickers out for 0.1s, phases offset 0.13s; c) breach energy grid brighter/thicker (2.5px, α0.75, flicker 0.3↔0.95, dropout interval 0.5+0.15×(grid%3)s, more frequent), the gap's jagged outline traced with an α0.8 width-2.5 bright line; d) 3 holographic shards at the breach (α0.35, additive) drift outward in a loop; e) whole-station glitch flash (see the inner container in item 1). Imagery: the station is destroyed; its data core is weaving its own memory back together with energy.

### 1.2 Counterpoint to Intro Shot 1

| | Intro shot 1 (destruction) | Return shot 4 (homecoming) |
| --- | --- | --- |
| Composition | Station centered (960,470), push-in | Same position/structure, **slow pull-back/side move** revealing the whole |
| Palette | Cold base + dark-red→orange-white explosion highlights | Cold base + cyan-blue holographic highlights (no warm colors) |
| Breach | Explosion core, shards ejected outward | Energy grid mending, particles converging |
| Mood | Dissolution | Reconstitution |

### 1.3 Interior Solid Area Breakdown

The phantom station's interior is "solid" to the protagonist (data made manifest); shots 5–7 take place here. Three areas, all procedural and new:

- **Apron** (inner wall of one ring compartment): hexagonal deck platform (dark solid base + cyan glowing edge lines + centerline guide-light strip), background is the phantom station's interior wireframe cross-section (translucent, shot-2 X-ray wireframe language in cyan); a passage gate ends the deck.
- **Passage** (spoke corridor): side-view corridor, ceiling/floor perspective lines + side-wall pipes; overhead sensor-light strip lights node by node as the protagonist advances (cyan-white, 0.15s/node); distant phantom-station structure glows faintly through.
- **Lounge** (central-hub cabin): a small cabin whose centerpiece is the sleep bed (rounded-rect platform + faint holographic mini-screen at the headboard); one warm-toned overhead lamp (the only warm light in the whole scene — the visual anchor of "home"); one bulkhead has an observation window showing the phantom ring slowly rotating outside — reminding the viewer this is still inside the phantom station.

---

## 2. Return Home Cinematic Storyboard (total 11.8s)

Duration table `[1.6, 1.2, 1.4, 2.2, 1.8, 1.6, 2.0]` = 11.8s (transitions inside shot durations; tuned 2026-07-28 from the 16.8s first draft, in-shot keyframes compressed proportionally). Follows all intro global conventions: 2.35:1 letterbox, subtitle cards (new keys `RETURN_SUB_1..7`), handheld drift, Timer chaining, unified `skip()` exit, Esc/any-key/click to skip. All audio maps to existing `GameState.SFX_*` constants (suggested volume/pitch handling in parentheses).

| # | Shot type & content | Duration | Camera & keyframes | Effects implementation (reused recipes) | Sound |
| --- | --- | --- | --- | --- | --- |
| 1 | **Wide**: deep space, protagonist fighter hovering slightly below frame center; warp drive charging — nozzle glow dark red (0.5,0.1,0.05) → white-hot (1,0.95,0.85) and scaling 1→1.8; fine energy particles converge inward around the hull | 1.6s | Fixed camera; final 0.4s: space distortion appears at frame center (concentric thin rings micro-pulsing + two offset concentric shock rings sweeping past the viewer) | Starfield reused (opens with `warp(12.0)` continuing the in-run `warp(18.0)` star streaks, naturally decaying back to 1); ship=player_ship.png tail view (rotated 180°, same pose as intro shot 5); nozzle=`CinematicFx.soft_glow()` two-layer soft glow + modulate/scale tween; converging particles=`CinematicFx.particles()` soft-dot texture, inward direction (negative vel); peak shock rings=`CinematicFx.shockwave()` ×2 (offset 0.14s, ry 0.6) | SFX_DASH (-6dB, 0.6× speed stretched into a charging swell) |
| 2 | **Effect shot**: a warp portal tears open ahead of the ship — a tall vertical ring (Line2D ellipse, cyan α0.9) rips from a point to full size (0.5s), energy churns on the rim (12 small glow dots racing around the ring); a blurry phantom-station view appears inside (station wireframe α0.1 + horizontal dispersion jitter) + twin vortex arcs churning in opposite directions + particles streaming inward at the rim | 1.2s | Slight push-in (container scale 1.0→1.06), focused on the portal | Portal ring=shockwave expanding-ring recipe used in reverse (expands but α solidifies); churn=glow dots repositioned per frame along a parametric equation (this shot's only `_process`, zero allocation; the same one also accumulates rotation for the twin vortex arcs); aperture=`CinematicFx.soft_glow()` flattened-ellipse pad (α0.15); inflow particles=ring emission domain (per the DawnStation recipe) + negative radial acceleration pulling to the ring center, node y flattened to the ellipse, emitting from 0.45s; interior view=minimal §1.1 station pieces | SFX_EXPLOSION (-12dB, 0.5× speed for a low tearing feel) |
| 3 | **Match cut**: the ship accelerates from behind the camera into the portal (flame flares, ship scale 1→0.2 diving into the ring center), portal closes (ring collapses to a point of white flash) → hard cut to the phantom-station starfield wide: the same portal re-opens, the ship decelerates out (scale 0.2→1, ease_out), portal closes and dissipates | 1.4s | First 0.63s original starfield, 0.10s white flash (reusing the differentiated transition piece) → last 0.77s phantom-station starfield (scaled to shot duration); flight direction consistent across both halves (up-right) | First half=`CinematicFx.radial_streaks()` warp-tunnel radial streaks (portal center, hidden with part_a on the white flash); at the flash moment a portal-center `CinematicFx.shockwave()` (white-cyan, 0.4s); white-flash transition=same as intro 1→2; far background first shows faint nebula + distant phantom-station silhouette (α0.15, foreshadowing shot 4); ship decelerating drags a short particle trail (soft-dot texture, 40) | SFX_DASH at the flash moment (0dB, normal speed); fly-out segment SFX_DASH (-10dB tail) |
| 4 | **Wide→mid**: the phantom station "Dawn Echo" fully revealed for the first time (all four §1.1 phantom layers active); a translucent energy capture track extends from the station, guiding the ship to the apron entrance; 8 navigation lights on the ring rim chase-flash slowly | 2.2s | Slow lateral tracking (container position sine-shifted 60px + scale 1.0→1.12 ease-in), showing the station's scale | Station=full §1.1 pieces; track=`CinematicFx.beam()` layered energy beam (glow layer + bright-core layer + 3 looping flowing soft dots, reusing the pre-sampled 24-point bezier from build time, zero-allocation internal `_process`); nav lights=8 soft dots around the ring (r=RING_RADIUS+4), `_CaptureShot` phase-driven narrow alpha pulses; short soft-dot trail behind the ship; tractive feel=ship position tweened along the sampled arc (TRANS_SINE); scan band sweeps the station as usual | SFX_RESUPPLY (-8dB, docking feel) |
| 5 | **Apron interior**: the ship lands smoothly along the guide-light centerline (vertical descent 40px, ease_out, slight compression bounce on touchdown + fine dust particles); guide lights chase-light from both sides toward the landing point within the descent window; engine cuts off (nozzle glow shrinks away over 0.3s); canopy opens (canopy polygon flips up over 0.4s, a glass highlight strip slides across the surface on the same path); the protagonist hops from the cockpit and lands (arc jump 0.5s + touchdown dust) | 1.8s | Fixed camera, slight low-angle tilt-up (horizon pressed to the lower third; camera elements shifted down ~60px to fake the tilt) | Apron=§1.3 pieces; guide-light chase=8 lights, staggered alpha tweens ordered by landing-point distance (started at build); highlight strip=small white-alpha Polygon2D on the canopy, slides + fades at the end; protagonist=intro shot-3 multi-segment figure (standing/jumping poses new, limb node tree reused); canopy=movable polygon overlaid on the ship sprite (simplified); dust=soft-dot texture | Landing SFX_EXPLOSION (-18dB, very soft thud); jump-down SFX_DASH (-14dB, short) |
| 6 | **Side tracking**: the protagonist walks across the apron passage; overhead sensor lights light up node by node with position (nodes behind fade slowly after 0.5s); stops at the lounge door at the end, the door slides open (two panels slide apart + light leaks through the gap) | 1.6s | Smooth rail pan (camera container follows the protagonist's x at constant speed; protagonist stays at the left third) | Walk cycle=intro shot-3 run rig, frequency lowered/phase retuned (step speed ~90px/s, stride halved); light strip=12-node Line2D segments lit by the protagonist's x threshold (folded into this shot's only `_process`); bulkhead layering=12 ribs alternating width/tone + small wall panels between ribs (top tick marks) + 3 overhead light cones (additive α0.05, parented to the world container for scroll parallax) + floor reflection strips; door=two Polygon2D slide tweens | Footsteps SFX_BUFF_PICK (-20dB, short ×6 steps, 0.4s interval); door SFX_DASH (-10dB, 0.7× speed) |
| 7 | **Lounge interior**: the door slides open, the protagonist walks straight to the sleep bed; sits → lies down (three pose keyframes: standing/sitting/lying, 0.6s apart); body relaxes (limb-angle micro tweens); torso breathing after lying down; **face close-up** (camera pushes to the head, scale→1.6): eyelid polygons close slowly (0.8s); screen starts dimming at the end of the eye close (0.9s fade-to-black) | 2.0s | Mid shot establishes the room (0.8s) → push to face close-up (0.7s) → dim (0.9s, overlapping the eye-close tail) | Lounge=§1.3 pieces (warm overhead lamp + headboard holographic screen glow + ring slowly rotating outside the window + 3 star dots drifting/wrapping inside the window); this shot's own `_RoomShot` driver (only `_process`, zero allocation): 0.6u walk-in cycle (limb formulas same as `_WalkShot`, weights fade in/out at the window ends, easing back to upright at the end) + post-lying breathing (torso micro scale/offset sine) + star-dot drift; sit/lie/eyelid/push-in still 0.6s keyframe tweens; dim=fullscreen black ColorRect tween (reusing the cinematic fade-out piece); BGM fades out in sync over the dim tail | Lying down SFX_RESUPPLY (-16dB, gentle); during the dim BGM volume_db tween → -40dB |

**Transition table**: 1→2 blackout 0.3s (charge done, portal not yet open — suspense); 2→3 continuous (push into the portal); 3 internal white flash 0.10s (warp); 3→4 blackout 0.3s; 4→5 blackout 0.3s; 5→6, 6→7 blackout 0.3s; 7 ends with a 0.9s dim straight into the base UI (no title card — the intro's title-card slot is taken by the base UI fade-in, see §4).

**Symmetry table vs the intro**: intro "station destroyed → escape → flying into deep space (tense, warm destruction)" ↔ return "warp → captured by the station → falling asleep (calm, cold reconstitution)"; intro shot-1 station ↔ return shot-4 station; intro shot-5 ship accelerating away ↔ return shot-3 ship decelerating in; intro shots 3/4 characters indoors ↔ return shots 6/7 characters indoors; intro title card ↔ return base-UI fade-in.

---

## 3. Base UI Overhaul Plan

### 3.1 Principles

**Zero logic changes**: not a single line moves in `base_console.gd` — signals, event bindings, GameState data interfaces, button callbacks. Changes happen in exactly two places: ①`UITheme` gains a set of "phantom" style tokens and style factories; ②`base_console.gd`'s `_ready()` visual construction (dim base, panel styles, new background and decoration layers). No existing test (`base_system_test` etc.) should break from the logic layer.

### 3.2 New Visual Tokens (UITheme; coexist with existing tokens, nothing replaced)

| New token | Value | Use |
| --- | --- | --- |
| `PHANTOM_BG` | (0.01, 0.03, 0.06, 0.90) | Base fullscreen backdrop (colder/deeper than the original dim) |
| `PHANTOM_PANEL_BG` | (0.03, 0.08, 0.12, 0.55) | Panel background: more transparent (holographic feel), background structure faintly visible through |
| `PHANTOM_BORDER` | (0.0, 0.83, 1.0, 0.65) | Panel border: one step brighter than the original PANEL_BORDER |
| `PHANTOM_SCAN` | (0.0, 0.83, 1.0, 0.06) | Scanline/frosted-glass overlay layer |
| `PHANTOM_TEXT_FLICKER` | α 0.92–1.0 jitter | Data-font instability (decorative labels only; readable body text unaffected) |

New panel material = ChamferedPanel (PHANTOM_PANEL_BG + PHANTOM_BORDER) + two procedural overlay layers: **scanline layer** (a 1px horizontal ColorRect line every 4px inside the panel, or a single looping scan band, PHANTOM_SCAN, mouse_filter=IGNORE); the **frosted-glass feel** is faked by "lower panel alpha + a large gaussian-like glow pad behind the panel" (gl_compatibility has no ready blur shader; a radial-gradient glow behind the panel bottom approximates it — no shader dependency introduced). Button float feel = an ACCENT_DIM shadow line 2px below each button + shadow-line alpha boost on hover (reuses the existing hover mechanism, no callback changes). Icon light-emblems = one 16×16 procedural Line2D glyph left of each of the four panel titles (minimal polylines of fighter/wrench/crosshair/flag, cyan glow), purely decorative new nodes.

Data-font jitter: applied only to the RP balance and panel titles — `modulate.a` sine-jitters between 0.92–1.0 at 3Hz + a 0.06s 1px horizontal offset flash every 2.7s (looping tweens, no `_process`). Body text (mission list, button labels) stays static for readability.

### 3.3 Layout: Original vs New (interaction hotspots and logic fully reused)

```
原布局（现状）                              新布局（虚影版）
┌──────────────────────────────┐          ┌──────────────────────────────┐
│  dim 纯色遮罩                  │          │  虚影站内部线框概念背景层         │
│      基地整备 (44)            │          │  （环体剖面+流动粒子，α0.10）     │
│      RP 余额：10 (金)         │          │      基地整备 (44，抖动)        │
│  ┌─────────┐ ┌─────────┐     │          │   RP 余额：10 (金，抖动)        │
│  │ 战机库   │ │ 武器挂载 │     │          │  ┌─────────┐   ┌─────────┐   │
│  ├─────────┤ ├─────────┤     │    →     │  │◇战机库   │   │◇武器挂载 │   │
│  │ 维修补给 │ │ 任务规划 │     │          │  ├─────────┤ ○ ├─────────┤   │
│  └─────────┘ └─────────┘     │          │  │◇维修补给 │   │◇任务规划 │   │
│  [    继续出击 (主按钮)   ]     │          │  └─────────┘   └─────────┘   │
└──────────────────────────────┘          │  [    继续出击 (全息投影感)  ]    │
                                          └──────────────────────────────┘
```

Changes (all visual/positional data; container hierarchy and signals untouched):

1. **Background layer**: original dim ColorRect(0.02,0.03,0.08,0.95) → two layers: PHANTOM_BG base + a "phantom station interior concept" Node2D layer (ring cross-section wireframe r≈520 centered at (960,540), α0.10, plus a small-dose §1.1 data-stream particles and one 8s/pass slow scan band; the layer sits after the dim, before the CenterContainer).
2. **Two-column HBox separation widened 20→140**: a 120px vertical gap opens between the columns, revealing the ring's axis at the background center (the ○), forming the visual focus of "panels encircling the data core" — a low-risk realization of the requested "ring layout feel": containers stay the original HBox/VBox, hotspots/Tab order/focus chain completely unchanged. (A centered floating ring layout was option B; it needs container restructuring, not adopted — violates the zero-logic-change principle and is purely visual gain.)
3. **Panel re-skin**: the four ChamferedPanels take the §3.2 styles + scanline overlay; title rows gain linear glow icons.
4. **Primary "Continue Sortie" button**: adds a bottom shadow line and 1.5s breathing glow on top of the primary style, strengthening the hologram float feel; size/position/callback unchanged.
5. **Open animation**: `animate_open` gains a 0.25s "hologram boot" before its fade-in — the four panels go from α0 + scale 0.98 → 1.0 (only for this opening; the stagger_open 60ms interval stays).

### 3.4 Reused Port Mapping Table (new-layout coordinates)

| Functional port (logic unchanged) | Original position | New position (design coords, panel top-left, after center-container offset) | Notes |
| --- | --- | --- | --- |
| Hangar `_build_hangar` | Left column, top | (320, 300) | Panel width 560 unchanged |
| Repair/supply `_build_supply` | Left column, bottom | (320, 520) | Repair/recharge button callbacks untouched |
| Weapon mounts · perk routes `_build_routes` | Right column, top | (1040, 300) | Route button rows/lock logic untouched |
| Mission planning `_build_missions` | Right column, bottom | (1040, 520) | Claim button logic untouched |
| Continue sortie `_on_resume_pressed` | Bottom center | (820, 810), 280×52 unchanged | Style only |
| Title/RP `_refresh` | Top center | Top center unchanged | Adds jitter decoration |

(Coordinates are indicative; the implementation still derives them from the CenterContainer+containers' auto layout; the table above is for screenshot verification.)

---

## 4. Cinematic → Base UI Transition Mechanism (pseudocode & timeline)

### 4.1 Trigger-Chain Changes (`main.gd` current → new)

The current `_start_homecoming()` is a three-step hard cut: "warp + white flash → show_base". The new chain replaces the white-flash cut with the full cinematic:

```
func _start_homecoming():                      # 现状骨架全部保留
    _homecoming = true; 锁输入; 停spawner; 收回母舰; save_run()
    _starfield.warp(18.0)                      # 保留：过场镜头1的充能与星光拉伸自然衔接
    _play_return_cinematic()                   # 替换原 _flash_white + show_base

func _play_return_cinematic():                 # 与 _play_intro_cinematic 同构
    _return = RETURN_SCENE.instantiate()
    _return.finished.connect(_on_return_finished)
    add_child(_return)                         # CanvasLayer layer=35, process_mode=Always
    get_tree().paused = true                   # 对局冻结（与开场过场同语义）

func _on_return_finished():                    # 跳过与自然结束同一出口
    _return = null
    _base_ui.show_base()                       # 虚影皮肤版基地 UI（§3）
    # 树保持 paused：基地界面本就是暂停态 UI（现状如此，不改）
    # BGM 在过场镜头7已淡出 → 此处切换为基地氛围音（bgm_loop 低音量变体，-30dB 淡入）
```

### 4.2 Timeline (from long-press B charge completion)

```
t=0.0s   charge done: lock input / stop spawner / save_run / starfield.warp(18) begins
t=0.0s   shot 1 (warp charge 1.6s) — the warp star streaks continue the charge seamlessly
t=1.6s   shot 2 (portal 1.2s)
t=2.8s   shot 3 (warp + arrival 1.4s)
t=4.2s   shot 4 (capture track + phantom station full view 2.2s)
t=6.4s   shot 5 (apron landing 1.8s)
t=8.2s   shot 6 (passage walk 1.6s)
t=9.8s   shot 7 (sleep 2.0s; screen dims 0.9s from t=10.9s + BGM fade-out)
t=11.8s  cinematic finished (or skip() at any moment lands here)
t=11.8s  show_base(): base UI already ready under blackout → hologram boot 0.25s + animate_open 0.2s
t≈12.3s  base UI fully interactive
```

### 4.3 Mechanics Notes

- **No async loading needed**: the cinematic and UI are all procedural, no external assets; calling `show_base()` synchronously in the `finished` callback is fine — the fade-in animation naturally masks the single-frame construction cost.
- **Unified skip semantics**: Esc (BackNavigator gains `SKIP_RETURN`, same priority as `SKIP_INTRO`) / any key / click → `skip()` → straight to `_on_return_finished()`. A BGM fade cut short mid-skip is handled inside `skip()` (kills the audio tween and sets the target volume immediately). **1.2s input grace (guards against live-play key mashing)**: for the first `SKIP_GRACE` seconds (config `effects.return_skip_grace`, default 1.2) `skip()` is ignored outright — in live play, held WASD/Shift/Space keys no longer skip instantly; the grace check is contained in `skip()`, so any-key/click/Esc routing is uniformly gated; the natural end (11.8s) is far beyond the grace and unaffected.
- **Save timing unchanged**: `save_run()` still completes before the cinematic starts (current line 330); playing/skipping/crashing during the cinematic never compromises save integrity.
- **Pause semantics**: the cinematic and base UI keep the tree paused throughout, both layers `process_mode=Always`; `_resume_from_base()` (continue sortie) triggers the orbital-strike animation (`process_mode=Always`, clears on the hit frame and unpauses; that animation was added after this document — its semantics are consistent with the original "fully unchanged" resume path).
- **Configurable skip**: if "don't replay the cinematic on later returns" is ever needed, a flag in `GameState` suffices to take the original white-flash hard-cut path — not implemented here, only a fork point reserved.

---

## 5. Implementation Constraints & Test Points (follow during implementation)

- **i18n**: add keys `RETURN_SUB_1..7`, `RETURN_SKIP` (or reuse `INTRO_SKIP`) to both the zh and en columns of `data/translations.csv`, then re-import.
- **Performance budget**: same as the intro — ≤96 per emitter, ≤400 live particles, at most one zero-allocation `_process` per shot (shots 2/4/6/7 each have one: `_PortalShot`/`_CaptureShot`/`_WalkShot`/`_RoomShot`, director drift shared; the beam/streaks/shockwave drivers inside `CinematicFx` are also zero-allocation), draw calls <400; the phantom-station pieces and the base background layer share one station-build function, avoiding two drifting geometries.
- **Refactor suggestion (at implementation)**: extract the station pieces from the intro's `_build_shot1()` into a shared function (parameterized palette/alpha/breach rendering) reused by the intro, the return cinematic, and the base background — the only existing-code change this design suggests; pure extraction, no behavior change.
- **Tests**: add `test/return_cinematic_test.tscn` mirroring `intro_cinematic_test` (trigger/pause/finished/resume/idempotent skip/no residual Timers/writable duration table advancing shot by shot); regression list: smoke, base_system (verifies zero base-logic change), back/esc_navigation (new SKIP_RETURN routing), quit-after-300; windowed per-shot screenshots for manual review (`test/return_capture.tscn`, 8s/shot stretched timeline, outputs `/tmp/return_shot*.png`) + ui_capture to verify the new base skin.
- **Docs sync**: record the return cinematic's Esc behavior in `docs/EXIT_FLOW.md`; `docs/PORTING_PARITY.md` needs no change (pure presentation layer, no gameplay-parity changes; that document was archived to `docs/archive/` and frozen on 2026-07-30); add one line about the return cinematic to the README gameplay description.

---

## 6. Implementation Log (landed 2026-07-28)

Fully implemented: the shared station factory `scripts/dawn_station.gd` (`DawnStation.build(Mode.DESTROYED/PHANTOM)`; intro shot 1 refactored to reuse it; return shot 4 and the base background layer share it), the return cinematic `scripts/return_cinematic.gd` + `scenes/return_cinematic.tscn` (7 shots, first draft 16.8s with no title card, **compressed to 11.8s on 2026-08-02**, see §7.1; dimming holds at full black before `finished`), `main.gd`'s new trigger chain (`_play_return_cinematic` / `_skip_return` / `_on_return_finished`; save timing and `_resume_from_base` unchanged), BackNavigator `SKIP_RETURN` routing, optional `pitch_scale` on `play_sfx`, the phantom base skin (UITheme PHANTOM_* tokens + base_console visual layer, zero logic changes), `test/return_cinematic_test.tscn` (42 assertions) and `test/return_capture.tscn` (per-shot screenshot tool).

Documented deviations from the §1–§3 design (decided at implementation; if adjusted later, the code wins and this section is updated):

1. §1.1's "scan band adds +0.15 alpha to the pieces it passes" was not implemented per-piece: the additive scan band's tint is visually equivalent, and per-piece detection would need an extra `_process`, violating the zero-allocation budget.
2. Data-stream particles ②'s "radial back-and-forth along spokes" is approximated with a ring emission domain (r80–200) + low-speed random directions (ParticleProcessMaterial has no spoke-aligned/round-trip mode).
3. Shot 2 does not show the ship itself (it is a portal-focused effect shot; the ship connects in shot 3 as it dives in).
4. Shot 3's warp white flash uses an in-shot ColorRect (the director's `_flash` node is reserved for inter-shot transitions).
5. The base button "hover shadow-line boost" was not done (the §3.1 zero-logic-change rule wins; no new signal bindings); the scanline layer uses the static-line-set option (1 draw call per panel); `PHANTOM_TEXT_FLICKER` got no constant (the alpha range is written inline in the jitter function).
6. The skip hint reuses `INTRO_SKIP`; no `RETURN_SKIP` was added.

## 7. Tuning Log (2026-07-28 round 2, three feedback points)

1. **Duration compressed 16.8s→11.8s**: `_shot_durations` → `[1.6, 1.2, 1.4, 2.2, 1.8, 1.6, 2.0]`, in-shot keyframes scaled proportionally (shots 3/5/6/7 auto-scale via `u = dur/baseline`; shot 1's distortion rings stay at the absolute 0.4s tail). Shot 7 dim 1.2s→0.9s (`OUTRO_FADE`), BGM fade in sync; eyelid close moved earlier to 1.5u — at eye-close completion the frame is about half dark.
2. **PHANTOM station brightened + brokenness**: `dawn_station.gd`'s `_build_phantom` rewritten — palette lifted to the `#00d4ff` family (main arc α0.55 / compartments 0.45 / details 0.35); `_build_body` gains a `gaps` param drawing the main arc in segments (new `_ring_arc`; the destroyed state passes an empty table, visually unchanged, regressed with intro_cinematic_test); five-piece brokenness = 3 main-arc gaps / 6 compartments flickering out 0.1s each (phase 0.13s) / energy grid thickened 2.5px α0.75 dropping out more often / 3 holographic shards drifting from the breach / whole-station glitch flash every 3.8s (separate `PhantomBody` inner container; its modulate is written separately from the station container's 0.85↔1.0 breathing, never overlapping). The base background layer (`base_console.gd` parent container α0.12) verified via ui_capture not to steal focus.
3. **Proportion fixes**: shot 5 ship scale 2.8 (visible length ~420px), figure scale 0.55 (~64px tall), ship-to-figure ratio anchored ≈6.6:1 (target 5–8:1); cockpit hide point (960,425), jump apex (935,390), landing point (905,686) with feet on the deck top at 710. Shot 6 door height 280px (frame 540..820, panels 76×272, slide ±85) clearly above the 185px figure; walk distance scales with duration (`_stop_scroll`/`_time_u = dur/2.4`, step speed 90px/s unchanged). Shot 7 bed widened to 260px, the lying figure at 185px fits the bed surface, close-up focus moved to the head (981,744).
4. Verification: windowed `return_capture` passed all 8 per-shot screenshots in one round (no rework) + `ui_capture` base page checked; headless regression return_cinematic 42 / intro_cinematic 40 / smoke 116 / base_system 46 all PASS.

## 8. Tuning Log (2026-07-30 round 3, CinematicFx visual upgrade)

Fully adopted the shared effect tool `scripts/cinematic_fx.gd` (soft radial glow / soft-dot textured particles / shockwave rings / layered energy beams / radial streaks); storyboard structure, duration table, skip semantics, subtitle bindings, and BGM fade all unchanged; all timed elements still normalized per shot via `u = dur/baseline` (shot 1's shock rings keep the absolute 0.4s tail convention).

- **Global**: the `_particles()` factory delegates to `CinematicFx.particles()` (same dict contract); all cinematic particles switch to soft-dot textures (scale semantics stay "pixel diameter"), eliminating hard-edged square particles.
- **Shot 1**: opens with `Starfield.warp(12.0)` continuing the in-run `warp(18.0)` (the in-run starfield freezes with the tree pause; the cinematic starfield keeps stretching and decays naturally); the nozzle's two-layer `_glow` becomes `CinematicFx.soft_glow` (same scale tween, relative to the base scale); at the charge peak (dur−0.4) two `shockwave`s offset by 0.14s (r700/900, ry 0.6, cyan) sweep past the viewer.
- **Shot 2**: the portal aperture gains a flattened-ellipse soft-glow pad (α0.15, under the phantom station, opening with the tear); 2 vortex arcs of ~200° added inside the ring (opposite directions 2.4/−1.7 rad/s, opening with the tear, folded into `_PortalShot._process` as pure rotation accumulation); 40 rim-inflow particles (`EMISSION_SHAPE_RING` emission domain + negative radial acceleration −140/−90 pulling to the ring center, node y flattened ry/rx to the ellipse, emitting from 0.45s as the tear nears completion).
- **Shot 3**: first half, `radial_streaks` at the portal center (26 streaks, max_radius 1000, 0.3u fade-in, hidden with part_a at the white flash); at the flash moment a portal-center `shockwave` (r520, 0.4s, white-cyan); fly-out trail 32→40 particles.
- **Shot 4**: the hand-rolled energy beam (glow Line2D + core Line2D + 2 traveling dots) replaced wholesale by `CinematicFx.beam` (reusing the same pre-sampled 24-point bezier, dot_count 3, dot_speed 0.5); `_CaptureShot` drops the flowing-dot driver and instead drives the 8 nav lights on the ring rim (r=RING_RADIUS+4, narrow alpha pulses `0.12+0.78·max(sin)^4` chasing slowly); a short tail trail added behind the ship (24 particles, weaker than the shot-3 recipe). The ship's track positioning/orientation logic unchanged.
- **Shot 5**: the 8 apron guide lights start dimmed at α0.2 and chase-light in staggered order by landing-point distance within the descent window (tweens started at build, falling back to 0.7 after peak); the canopy opening gains a glass highlight strip on the same path (small white-alpha Polygon2D on the canopy, sliding 0.4u, fading over the last 0.12u).
- **Shot 6**: the 12 bulkhead ribs alternate width (22/14) and tone; 6 small wall panels added between the ribs (with top tick marks); the 3 ADD light cones (α0.05) and floor reflection strips parent to the world container, naturally parallaxing with the scroll.
- **Shot 7**: a dedicated `_RoomShot` driver added (this shot's only `_process`, zero allocation) — 0.6u walk-in cycle (limb formulas same as `_WalkShot`, weights fade in/out at the window ends, easing back to upright at the end; position still driven by the original tween); torso breathing from 1.7u (scale.y ±2.5% + micro-offset sine, weight eased in); 3 star dots drift slowly across the observation window (wrapping inside the frame edge). The sit/lie/eyelid/push-in/BGM-fade keyframes are untouched, line for line; §6 deviation item 4 (the walk-in translation tween) is thereby eliminated.
- **Budget check**: the largest new emitter is 40 particles (shot-2 inflow), ≤96/emitter and ≤400 on-screen live unchanged; still at most one shot-level `_process` per shot (shot 7 adds `_RoomShot` as its one; the internal beam/streaks/shockwave drivers are zero-allocation); no `world_scale`-related values (pure screen space).
- **Verification**: `--headless --quit-after 5` no parse errors; windowed `return_capture` 8s/shot stretched timeline, all 8 screenshots manually reviewed (new elements visible and not stealing focus: portal vortices/inflow, tunnel streaks, energy beam + nav lights, guide-light strip, corridor cones/panels, shot-7 lying close-up and dimming all correct; transient tail pieces like the peak shock rings / flash shockwave / highlight strip are not on screenshot frames, code paths verified against the same existing patterns).

## 9. Audio Policy Note (re-checked and logged 2026-08-02, D18)

- The return cinematic's SFX **keep the §2 per-shot lowered levels** (-6/-8/-10/-12/-14/-16/-18/-20dB + pitch changes), which **differs** from the intro's 08-02 unified `AUDIO_VOL_OFFSET=-6dB` / `AUDIO_PITCH=0.88` policy — a product call: the return SFX are already lowered shot by shot to suit the soft closing mood, so no unification or behavior change this round.
- If the two cinematics' audio policies are ever unified, update all `play_sfx` calls in `scripts/return_cinematic.gd` and revise this section and the §2 shot table accordingly; the intro policy is in `docs/INTRO_CINEMATIC.md`.
