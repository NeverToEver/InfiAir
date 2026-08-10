# Meta HUD Health & Hit-Feedback - Implementation Spec

Status: Implemented (landed 2026-08-02; `csharp/godot/MetaHealthFX.cs` + `test/meta_health_fx_test.tscn` (`csharp/godot/tests/MetaHealthFxTest.cs`) + meta_health.gdshader/crack_field_bake.gdshader + effects.meta_health; §7 七项测试 + ui_capture 已验; M7 2026-08-08: GDScript → C#); self-contained (params named, anchors file+line, decisions final, perf-biased). Acceptance: §7.

Premises: Godot 4.6 GL Compatibility, pure C# (M7 2026-08-08: zero GDScript), 1920x1080. No HDR bloom/Compositor: glow via `CinematicFx.SoftGlow()`; post-FX = canvas_item shader + `hint_screen_texture` ColorRect [R4]. Pitfalls: screen Y may flip (per-renderer detect); no mipmap/`textureLod` - hand multi-tap blur [R4].

## 1. Research (traceability)

- R1 Dead Space: zero-visual-search; screen-layer carrier (2D body too small for spine UI).
- R2 gmshaders/godotshaders: 3-tap radial CA, edge-strong; not uniform CA.
- R3 Impact Glass/Godot-Glass-Break-Effect: pre-baked field + progress threshold, bidirectional; not realtime Voronoi.
- R4 Shaggy Dev/docs/#50976: ColorRect + `hint_screen_texture`, hand blur; not `textureLod`. 4.3+ Y-normalizes screen_texture -> `SCREEN_UV` (manual flip mirrored; removed).
- R5 Cyberpunk 2077 (public pattern): HUD-data-layer jitter only.
- R6 Titanfall 2 (public pattern): attack dir -> edge-arc ripple (12% band); not red flash.
- R7 Helldivers 2 (patch evidence): vignette/desat implicit HP layer; inner cap + adaptive.

Render: CanvasLayer layer=1 (above world, below HUD; main HUD -> layer=2; below OrbitalStrike 24, cutscene 35). Instant layers first: hit alters sampling, persistent overlays it.

## 2. Decisions

- D1 crack field: runtime SubViewport single-frame pre-bake (once, 512^2, 1 frame) [R3]; not offline PNG (+asset pipeline, saves 1 frame), realtime Voronoi (per-frame)
- D2 LOD: single shader + `u_lod` uniform branch (pixel-uniform, cost~0); not dual shaders/frames (2x maintenance + VRAM + pipeline)
- D3 readability: registry proxy `BulletPool active*0.002 + active explosions*0.15`, no GPU readback; not `GetTexture().GetImage()` (per-frame disaster in Compatibility; 0.25s-throttled risky)
- D4 blur: hand-written 4-tap centripetal; skip if `u_radial_blur_strength < 0.001`; not `textureLod` (no mipmap), half-res texture (extra copy)
- D5 params: epsilon detection (cache last, skip `SetShaderParameter` if delta<0.001); `_Process` early-return; not per-frame upload (16 params x 60fps waste)
- D6 breath zoom: Main.cs `zoom = ViewZoomFactor() * BreathScale()`, DYING only; not MetaFX camera ref, unconditional per-frame zoom
- D7 heartbeat: GameState sfx pool one-shot; `generate_audio.py` offline 0.28s double-pulse WAV; not looping stream (hard cuts; rate-vs-HP inexpressible)
- D8 hit-dir: `TakeDamage(amount, fromPos = Vector2.Inf)`; 4 call sites pass position; not signal at call sites (logic in 4 files)
- D9 jitter: `_hpBar` + buff chips only (+-2px, 80ms burst); not whole-HUD jitter
- D10 state update: per-frame pure arithmetic (`sin()` <=3x/frame) + early return; not 0.1s throttle

## 3. Shader spec — source of truth: `assets/shaders/meta_health.gdshader` (code = spec; decision refs R2/R3/R4/R6/R7 per uniform below)

### 3.1 uniforms (`shader_type canvas_item`)

- `screen_texture` sampler2D: engine [R4]; 4.3+ Y-normalized, sample `SCREEN_UV`
- `u_crack_field` sampler2D: D1 pre-bake [R3]; R=line dist, G=cell hash, B=growth gate (0 edge->1 center)
- `u_hit_intensity` float 0.0: hit_pulse x ripple_alpha [R2]
- `u_hit_dir` vec2 (0,0): attack dir world->screen [R6]; 0 = uniform ring
- `u_chromatic_amount` float 0.0: pulse envelope [R2]; CA offset 0..0.02
- `u_radial_blur_strength` float 0.0: pulse envelope [R2][R4]; <0.001 skips (D4)
- `u_ripple_phase` float 1.0: C# 0->1 (0.4s) [R6]
- `u_crack_progress` float 0.0: §4.2 curve [R3]
- `u_crack_color` vec4 source_color cyan: state interpolation [R3]
- `u_crack_glow` float 0.8: crack ADD pseudo-glow strength (`effects.meta_health.crack.glow`) [R3]
- `u_crack_density` float 0.0: state layer cap [R3]
- `u_crack_spread_min` float 0.15: `_Ready` config [R3]; gate floor (full HP = none)
- `u_crack_edge_softness` float 0.08: `_Ready` config [R3]
- `u_crack_width` float 0.10: `_Ready` config [R3]; line half-width
- `u_heal_jitter` float 0.0: C# heal 0->0.35->0 [R3]
- `u_desaturation` float 0.0: `_damageX` [R7]
- `u_hue_cool` float 0.0: `_damageX` [R7]; cool cyan
- `u_vignette_strength` float 0.0: `_damageX` + LOD1 fallback [R7]
- `u_vignette_inner` float 0.62: DYING -> 0.56 [R7]; protects center
- `u_heartbeat` float 0.0: heartbeat phase 0->1->0 [R7]
- `u_adapt_gain` float 1.0: D3 proxy brightness [R7]
- `u_lod` int 0: settings [R3]; 0=full, 1=skip CA/blur/ripple (D2)

"Reduce flash" NOT in shader: C# pre-scales (CA x0.4, heartbeat/jitter zeroed); shader branch-free (D5 spirit).

### 3.2 fragment

Implementation in `meta_health.gdshader` (stage order: hit layer [R2] → ripple [R6] → implicit desat/hue [R7] → vignette [R7] → crack [R3]; `u_lod==1` skips CA/blur/ripple, D2). Bake shader `assets/shaders/crack_field_bake.gdshader` (D1: SubViewport 512² single-frame pre-bake, headless `CpuBakeImage()` 64²). "Reduce flash" NOT in shader: C# pre-scales (CA x0.4, heartbeat/jitter zeroed); shader branch-free (D5 spirit).

## 4. C# spec

### 4.1 `csharp/godot/MetaHealthFX.cs` (`public partial class MetaHealthFX : CanvasLayer`)

Interface (impl in code; config `effects.meta_health.*`):
- `layer = 1` (below main HUD layer=2); process_mode Pausable
- States `STATE_NORMAL/CAUTION/DAMAGED/CRITICAL/DYING = 0..4`; `THRESHOLDS = [0.75, 0.50, 0.25, 0.20]` (down-crossing); `DENSITY_CAPS = [0.0, 0.30, 0.50, 0.75, 1.0]`
- `OnPlayerDamaged(float amount, Vector2 fromPos)`: hit-pulse max-pool [R2]; `fromPos == Vector2.Inf` → uniform ring [R6]
- `BreathScale() -> float` (Main D6 composition) / `BreathActive() -> bool` (DYING && HP>0 && !ReduceFlash)
- `_Ready`: ColorRect + ShaderMaterial (§3), crack-field pre-bake (D1), connect GameState signals, `MetaFxLod` from cfg; `_Process`: early-return + epsilon upload (D5)
- HP→crack mapping (`OnHealthChanged`) & state machine in code (§4.2 params from cfg)

### 4.2 HP->crack mapping (in `OnHealthChanged`)

In code: `x = 1 - hp/max_hp`; `crack_progress = pow(_damageX, 1.6)`; color crossfade (bandwidth 0.08: cyan #35E0FF → yellow #FFD23F → orange #FF8A3D → red #FF3B4E); desat `0.35·pow(x,2)`; `u_hue_cool = 0.6·x`; vignette `min(0.5, crack_progress·0.55)`, DYING inner 0.62→0.56 (0.3s) — all params from `effects.meta_health.*` (§4.4).

### 4.3 Integration points

- `csharp/godot/GameState.cs` signal zone: +`PlayerDamaged(float amount, Vector2 fromPos)`; +`MetaFxLod = 1` (LOD1 fallback; MetaHealthFX `_Ready`->cfg lod, `_ExitTree`->1); +`ReduceFlash = false` (`SetReduceFlash` setter persists profile)
- `csharp/godot/PlayerDamage.cs` (2026-08-03 A8 split; emit lives here) / `csharp/godot/Player.cs` `TakeDamage()`: +`fromPos = Vector2.Inf` overload (D8); after resolution emit `GameState.PlayerDamaged(finalAmount, fromPos)` (Player.cs delegates to `_damage.TakeDamage()`)
- `csharp/godot/Bullet.cs` `OnGraceTimeout()` / `csharp/godot/Enemy.cs` `TryBodyCollision()` / `csharp/godot/Boss.cs` `CheckBodyCollision()` / `csharp/godot/FormationBomb.cs` `Detonate()` (player-hit sites): pass `GlobalPosition`
- `csharp/godot/Main.cs` `ApplyCameraZoom()` camera zoom: `zoom = Vector2.One x GameState.ViewZoomFactor() x (_metaFx.BreathScale() if active)` (D6); per-frame if `BreathActive()`; `_Ready` adds MetaHealthFX child
- `csharp/godot/Hud.cs` `UpdateVignette()`: top `if (GameState.MetaFxLod == 0) return` (LOD0 -> MetaFX, `_vignette` 0; LOD1 keeps current, D2)
- `csharp/godot/Hud.cs` `_hpBar` build: holographic: base alpha 0.25, fill `CanvasItemMaterial` BlendMode Add; +`MetaJitter()` (D9: +-2px, 80ms, only `_hpBar`+`_buffDockWrap`)
- `csharp/godot/SettingsUi.cs` `BuildModesPage()` after `SET_DISPLAY`: +`SET_ACCESSIBILITY` section + `SET_REDUCE_FLASH` toggle (`UITheme.MakeSectionHeader`/`UITheme.MakeToggleButton`), bound `GameState.SetReduceFlash()`
- `data/translations.csv`: `SET_ACCESSIBILITY` 无障碍/Accessibility; `SET_REDUCE_FLASH` 减少闪光/Reduce flashes
- `scripts/tools/generate_audio.py`: +`heartbeat.wav` - 55Hz sine double-pulse (lub-dub), 0.28s, exp envelope (D7)

### 4.4 `data/balance.json` additions (`effects.meta_health.*`)

Full key set with defaults in `data/balance.json` under `effects.meta_health.*` (lod / pulse / chromatic / blur / ripple / crack / desat / vignette / dying / smooth / adapt / reduce_flash); shader statics wired in `_Ready`.

## 5. State machine & VFX timing

Transitions: down fast-in, up slow-out (taus/stagger §4.1/§4.4); HitPulse orthogonal; DYING 0.3s fades.

Timeline: 0-80ms CA peak `0.006+0.014*pulse` + ripple influx; 80-300ms exp decay, crest +8%; 300-400ms done; 0-600ms overshoot +8%; persistent: §4.2 + DYING (heartbeat 1.0-1.2Hz, breath +-1.5%, jitter +-2px/beat, warning 2.5Hz, 6% narrowing).

Photosensitivity: no square waves; pulses <=2.5Hz. Reduce-flash: CA x0.4, visual pulses off (sfx kept), static border.

## 6. Layer rules

Explicit: `$HpBar` holographic (§4.3), exact fallback; **no crosshair ring** (bullet-dodge zone [R2/R7]).

## 7. Tests & acceptance

New `test/meta_health_fx_test.tscn` (+ `csharp/godot/tests/MetaHealthFxTest.cs`) (headless):

1. `TakeDamage(10)` -> signal carries amount & fromPos; none in invuln frames.
2. max-pool: 10x `r=0.05` -> `_hitPulse` <= 0.15.
3. hp=75/50/25/10% -> crack_progress ~ 0.11/0.33/0.63/0.84 (+-0.02). (2026-08-01: 0.72/0.93 typo; `pow(0.75,1.6)=0.63`, `pow(0.90,1.6)=0.84`.)
4. 0.25 down -> instant fast-in; up -> after 0.8s crack_progress falls, `_healJitter` 0->0.35->0.
5. hp<20% -> rate in [1.0,1.2]Hz, `BreathScale()` in [0.985,1.015]; reduce-flash -> `BreathActive()==false`.
6. `u_lod==1` -> hud `_vignette` low-HP pulse restored (fallback, D2).
7. full HP static 60 frames -> `_Process` early-return hits (0 param uploads, D5).

Manual: `ui_capture.tscn` shots - CA peak, ripple bearing, per-tier crack density, DYING narrowing/jitter.

Verify: `--headless --import`, `--quit-after 300`, `smoke_test.tscn`, new test; player/hud changes + `hit_logic_test.tscn`, `balance_test.tscn`.

## 8. Implementation order

- P1 hit layer: CA/blur/ripple shader + D8 + MetaFX skeleton -> tests 1, 2 + shots.
- P2 crack system: D1 pre-bake + state machine + curve -> tests 3, 4.
- P3 implicit & critical layers: vignette/desat/heartbeat/breath/jitter/narrow + main/hud -> test 5 + shots.
- P4 wrap-up: accessibility + LOD + D3 adaptive + audio + tests 6, 7 + update AGENTS.md.
