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

## 3. Shader spec (`assets/shaders/meta_health.gdshader`)

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

### 3.2 fragment (stages + all constants)

```text
uv=SCREEN_UV (Y-normalized [R4]); to_center=uv-vec2(0.5); dist=length(to_center*vec2(1.0,0.5625))*2.0  # edge-mid 1.0, corners 1.15
radial_w=smoothstep(0.25,1.0,dist)  # edge-strong [R2]

1 hit layer (skip if u_lod==1 or (u_chromatic_amount<=0.0001 && u_radial_blur_strength<=0.001)):
   off=normalize(to_center+1e-6)*radial_w*u_chromatic_amount
   col=vec3(a.r,mix(a.g,b.g,0.5),b.b) with a/b = texture(screen_texture, uv+off / uv-off).rgb  # P1-7: 3->2 samples, G=avg ends
   blur if u_radial_blur_strength>0.001 (D4): +3 taps i=1..3 at mix(uv,vec2(0.5), i/4.0*u_radial_blur_strength*radial_w*0.06); col=avg/4.0
2 ripple if u_lod==0 && u_hit_intensity>0.01 [R6]:
   col += vec3(0.4,0.9,1.0) * smoothstep(0.88,0.98,dist) * pow(max(cos(atan(to_center.y,to_center.x)-atan(u_hit_dir.y,u_hit_dir.x)),0.0),3.0) * (sin((dist-u_ripple_phase*1.1)*40.0)*0.5+0.5) * (1.0-u_ripple_phase) * u_hit_intensity
3 implicit [R7]: lum=dot(col,vec3(0.299,0.587,0.114)); col=mix(col,vec3(lum),u_desaturation); col=mix(col,col*vec3(0.85,1.0,1.1),u_hue_cool)
4 vignette [R7]: vig=smoothstep(u_vignette_inner,1.15,dist); col=mix(col,vec3(0.35,0.02,0.05), clamp(u_vignette_strength*(1.0+0.35*u_heartbeat),0.0,0.6)*u_adapt_gain*vig)
5 crack [R3]: field=texture(u_crack_field,uv).rgb; gate=mix(u_crack_spread_min,1.0,field.b)+(field.g-0.5)*u_heal_jitter; on=smoothstep(gate,gate+u_crack_edge_softness,u_crack_progress); line=1.0-smoothstep(0.0,u_crack_width,field.r); mask=line*on*u_crack_density; col=mix(col,u_crack_color.rgb*0.25,mask*0.6)+u_crack_color.rgb*mask*u_crack_glow*u_adapt_gain
COLOR=vec4(col,1.0)
```

Bake shader (D1, once): SubViewport(512x512) + ColorRect; F1/F2 Voronoi over 12 fixed seeds (const array, hash on `uv`); crack line at cell boundaries (F2-F1~0); writes R/G/B per §3.1. `_Ready`: after `RenderingServer.FramePostDraw` -> `GetTexture().GetImage()` -> `ImageTexture`, free SubViewport. One-time readback OK (vs D3 per-frame). headless -> `CpuBakeImage()` (64^2, same formula).

## 4. C# spec

### 4.1 `csharp/godot/MetaHealthFX.cs` (`public partial class MetaHealthFX : CanvasLayer`)

```text
layer = 1 (main HUD -> layer=2); process_mode Pausable (paused -> frozen; early-return cost~0)

STATE_NORMAL/CAUTION/DAMAGED/CRITICAL/DYING = 0..4
THRESHOLDS = [0.75, 0.50, 0.25, 0.20]          # down-crossing
DENSITY_CAPS = [0.0, 0.30, 0.50, 0.75, 1.0]    # per-state crack cap

_state; _damageX 0.0 (smoothed); _targetX 0.0; _hitPulse 0.0; _hitDir ZERO (0=uniform ring); _rippleT 2.0 (>1=none); _healJitter 0.0; _heartPhase -1.0 (<0=not DYING); _breath 1.0; _mat; _last (D5 cache); _cfg (effects.meta_health.*)

_Ready(): ColorRect(mouse_filter=IGNORE)+ShaderMaterial (§3); statics: u_lod/u_crack_spread_min/u_crack_edge_softness/u_crack_width; crack-field pre-bake (D1, `DeferBake()`/`OnBakeFrame`); connect GameState.HealthChanged/PlayerDamaged/PlayerDied -> OnHealthChanged/OnPlayerDamaged/OnPlayerDied; MetaFxLod = _cfg lod (hud).

_Process(delta): early return if |_targetX-_damageX|<0.001 && _hitPulse<0.001 && _rippleT>1.0 && _heartPhase<0 && _healJitter stable
  1. tau=down_tau|up_tau; _damageX exp->_targetX
  2. threshold cross -> crack overshoot (+0.08, 0.6s) or heal jitter 0->0.35->0 (0.7s)
  3. _hitPulse exp decay (tau=0.09); _rippleT += delta/0.4
  4. DYING: _heartPhase @ 1.0->1.2Hz (by x); per beat: _heartbeat env (0.3s), _breath=1+0.015*sin(phase), sfx (D7), hud jitter ("MetaJitter", D9); else _breath->1, 0.3s fade
  5. D3: 0.25s proxy -> u_adapt_gain (clamp 0.8..1.3)
  6. D5: epsilon -> SetShaderParameter

OnPlayerDamaged(float amount, Vector2 fromPos): r=amount/GameState.MaxHealth(); _hitPulse=Mathf.Max(_hitPulse, Mathf.Clamp(r*2.5, 0.15f, 1.0f))  # max-pool [R2]; _hitDir=world->screen (fromPos==Vector2.Inf -> uniform ring); _rippleT=0.0

BreathScale() -> float: Main.cs D6 composition
BreathActive() -> bool: _state == STATE_DYING && GameState.Health > 0 && !GameState.ReduceFlash
```

### 4.2 HP->crack mapping (in `OnHealthChanged`)

```text
x = 1 - hp/max_hp; _targetX = x
crack_progress = pow(_damageX, 1.6)            # x=0.25->0.11, 0.50->0.33, 0.75->0.63, 0.90->0.84
color crossfade (bandwidth 0.08): x<0.25 cyan #35E0FF -> <0.5 yellow #FFD23F -> <0.8 orange #FF8A3D -> >=0.8 red #FF3B4E
desat = 0.35 * pow(x, 2.0); u_hue_cool = 0.6 * x
vignette = min(0.5, crack_progress * 0.55); DYING: u_vignette_inner 0.62->0.56 (6% narrowing, 0.3s)
```

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

### 4.4 `data/balance.json` additions (`effects.meta_health.*`; defaults must match)

```json
"meta_health":{"lod":0,"pulse":{"scale":2.5,"min":0.15,"decay_tau":0.09},"chromatic":{"base":0.006,"peak":0.014},"blur":{"strength":0.6},"ripple":{"duration":0.4,"alpha":0.8},"crack":{"exponent":1.6,"spread_min":0.1,"edge_softness":0.08,"width":0.03,"glow":0.8,"heal_jitter":0.35,"grow_overshoot":0.08,"grow_time":0.6,"density":[0.0,0.30,0.50,0.75,1.0]},"desat":{"max":0.35,"exponent":2.0},"vignette":{"max_alpha":0.5,"inner":0.62,"dying_shrink":0.06},"dying":{"threshold":0.2,"heart_min_hz":1.0,"heart_max_hz":1.2,"breath":0.015,"jitter_px":2.0,"warn_hz":2.5,"fade":0.3},"smooth":{"down_tau":0.10,"up_tau":0.80},"adapt":{"interval":0.25,"min":0.8,"max":1.3,"bullet_weight":0.002,"explosion_weight":0.15},"reduce_flash":{"chromatic_scale":0.4}}
```

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
