# Meta HUD Health & Hit-Feedback - Implementation Spec

Status: Implemented (landed 2026-08-02; meta_health_fx.gd + meta_health_fx_test + meta_health.gdshader/crack_field_bake.gdshader + effects.meta_health; §7 七项测试 + ui_capture 已验); self-contained (params named, anchors file+line, decisions final, perf-biased). Acceptance: §7.

Premises: Godot 4.6 GL Compatibility, pure GDScript, 1920x1080. No HDR bloom/Compositor: glow via `_glow()`; post-FX = canvas_item shader + `hint_screen_texture` ColorRect [R4]. Pitfalls: screen Y may flip (per-renderer detect); no mipmap/`textureLod` - hand multi-tap blur [R4].

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
- D3 readability: registry proxy `bullet_pool active*0.002 + active explosions*0.15`, no GPU readback; not `get_texture().get_image()` (per-frame disaster in Compatibility; 0.25s-throttled risky)
- D4 blur: hand-written 4-tap centripetal; skip if `u_radial_blur_strength < 0.001`; not `textureLod` (no mipmap), half-res texture (extra copy)
- D5 params: epsilon detection (cache last, skip `set_shader_parameter` if delta<0.001); `_process` early-return; not per-frame upload (16 params x 60fps waste)
- D6 breath zoom: main.gd `zoom = view_zoom_factor * breath_scale()`, DYING only; not MetaFX camera ref, unconditional per-frame zoom
- D7 heartbeat: GameState sfx pool one-shot; `generate_audio.py` offline 0.28s double-pulse WAV; not looping stream (hard cuts; rate-vs-HP inexpressible)
- D8 hit-dir: `take_damage(amount, from_pos := Vector2.INF)`; 4 call sites pass position; not signal at call sites (logic in 4 files)
- D9 jitter: `_hp_bar` + buff chips only (+-2px, 80ms burst); not whole-HUD jitter
- D10 state update: per-frame pure arithmetic (`sin()` <=3x/frame) + early return; not 0.1s throttle

## 3. Shader spec (`assets/shaders/meta_health.gdshader`)

### 3.1 uniforms (`shader_type canvas_item`)

- `screen_texture` sampler2D: engine [R4]; 4.3+ Y-normalized, sample `SCREEN_UV`
- `u_crack_field` sampler2D: D1 pre-bake [R3]; R=line dist, G=cell hash, B=growth gate (0 edge->1 center)
- `u_hit_intensity` float 0.0: hit_pulse x ripple_alpha [R2]
- `u_hit_dir` vec2 (0,0): attack dir world->screen [R6]; 0 = uniform ring
- `u_chromatic_amount` float 0.0: pulse envelope [R2]; CA offset 0..0.02
- `u_radial_blur_strength` float 0.0: pulse envelope [R2][R4]; <0.001 skips (D4)
- `u_ripple_phase` float 1.0: GDScript 0->1 (0.4s) [R6]
- `u_crack_progress` float 0.0: §4.2 curve [R3]
- `u_crack_color` vec4 source_color cyan: state interpolation [R3]
- `u_crack_density` float 0.0: state layer cap [R3]
- `u_crack_spread_min` float 0.15: `_ready` config [R3]; gate floor (full HP = none)
- `u_crack_edge_softness` float 0.08: `_ready` config [R3]
- `u_crack_width` float 0.10: `_ready` config [R3]; line half-width
- `u_heal_jitter` float 0.0: GDScript heal 0->0.35->0 [R3]
- `u_desaturation` float 0.0: damage_x [R7]
- `u_hue_cool` float 0.0: damage_x [R7]; cool cyan
- `u_vignette_strength` float 0.0: damage_x + LOD1 fallback [R7]
- `u_vignette_inner` float 0.62: DYING -> 0.56 [R7]; protects center
- `u_heartbeat` float 0.0: heartbeat phase 0->1->0 [R7]
- `u_adapt_gain` float 1.0: D3 proxy brightness [R7]
- `u_lod` int 0: settings [R3]; 0=full, 1=skip CA/blur/ripple (D2)

"Reduce flash" NOT in shader: GDScript pre-scales (CA x0.4, heartbeat/jitter zeroed); shader branch-free (D5 spirit).

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
5 crack [R3]: field=texture(u_crack_field,uv).rgb; gate=mix(u_crack_spread_min,1.0,field.b)+(field.g-0.5)*u_heal_jitter; on=smoothstep(gate,gate+u_crack_edge_softness,u_crack_progress); line=1.0-smoothstep(0.0,u_crack_width,field.r); mask=line*on*u_crack_density; col=mix(col,u_crack_color.rgb*0.25,mask*0.6)+u_crack_color.rgb*mask*0.8*u_adapt_gain
COLOR=vec4(col,1.0)
```

Bake shader (D1, once): SubViewport(512x512) + ColorRect; F1/F2 Voronoi over 12 fixed seeds (const array, hash on `uv`); crack line at cell boundaries (F2-F1~0); writes R/G/B per §3.1. `_ready`: after `RenderingServer.frame_post_draw` -> `get_texture().get_image()` -> `ImageTexture`, free SubViewport. One-time readback OK (vs D3 per-frame). headless -> `_cpu_bake_image()` (64^2, same formula).

## 4. GDScript spec

### 4.1 `scripts/meta_health_fx.gd` (new; `class_name MetaHealthFX extends CanvasLayer`)

```text
layer = 1 (main HUD -> layer=2); process_mode Pausable (paused -> frozen; early-return cost~0)

STATE_NORMAL/CAUTION/DAMAGED/CRITICAL/DYING = 0..4
THRESHOLDS = [0.75, 0.50, 0.25, 0.20]          # down-crossing
DENSITY_CAPS = [0.0, 0.30, 0.50, 0.75, 1.0]    # per-state crack cap

_state; _damage_x 0.0 (smoothed); _target_x 0.0; _hit_pulse 0.0; _hit_dir ZERO (0=uniform ring); _ripple_t 2.0 (>1=none); _heal_jitter 0.0; _heart_phase -1.0 (<0=not DYING); _breath 1.0; _mat; _last (D5 cache); _cfg (effects.meta_health.*)

_ready(): ColorRect(mouse_filter=IGNORE)+ShaderMaterial (§3); statics: u_lod/u_crack_spread_min/u_crack_edge_softness/u_crack_width; _bake_crack_field() (D1); connect GameState.health_changed/player_damaged/player_died -> _on_health_changed/_on_player_damaged/_on_player_died; meta_fx_lod = _cfg lod (hud).

_process(delta): early return if |_target_x-_damage_x|<0.001 && _hit_pulse<0.001 && _ripple_t>1.0 && _heart_phase<0 && _heal_jitter stable
  1. tau=down_tau|up_tau; _damage_x exp->_target_x
  2. threshold cross -> crack overshoot (+0.08, 0.6s) or heal jitter 0->0.35->0 (0.7s)
  3. _hit_pulse exp decay (tau=0.09); _ripple_t += delta/0.4
  4. DYING: _heart_phase @ 1.0->1.2Hz (by x); per beat: _heartbeat env (0.3s), _breath=1+0.015*sin(phase), sfx (D7), hud jitter ("meta_jitter", D9); else _breath->1, 0.3s fade
  5. D3: 0.25s proxy -> u_adapt_gain (clamp 0.8..1.3)
  6. D5: epsilon -> set_shader_parameter

_on_player_damaged(amount: float, from_pos: Vector2): r=amount/GameState.max_health(); _hit_pulse=maxf(_hit_pulse, clampf(r*2.5, 0.15, 1.0))  # max-pool [R2]; _hit_dir=world->screen (from_pos==Vector2.INF -> uniform ring); _ripple_t=0.0

breath_scale() -> float: main.gd D6 composition
breath_active() -> bool: _state == STATE_DYING && reduce-flash off
```

### 4.2 HP->crack mapping (in `_on_health_changed`)

```text
x = 1 - hp/max_hp; _target_x = x
crack_progress = pow(_damage_x, 1.6)            # x=0.25->0.11, 0.50->0.33, 0.75->0.63, 0.90->0.84
color crossfade (bandwidth 0.08): x<0.25 cyan #35E0FF -> <0.5 yellow #FFD23F -> <0.8 orange #FF8A3D -> >=0.8 red #FF3B4E
desat = 0.35 * pow(x, 2.0); u_hue_cool = 0.6 * x
vignette = min(0.5, crack_progress * 0.55); DYING: u_vignette_inner 0.62->0.56 (6% narrowing, 0.3s)
```

### 4.3 Integration points

- `autoload/game_state.gd` signal zone (`player_damaged`, line 12): +`signal player_damaged(amount: float, from_pos: Vector2)`; +`var meta_fx_lod: int = 1` (LOD1 fallback; `_ready`->0, `_exit_tree`->1); +`var reduce_flash: bool = false` (setter persists profile, aim_assist)
- `scripts/player.gd` `take_damage()` (line 891): +`from_pos: Vector2 = Vector2.INF` (D8); after resolution emit `GameState.player_damaged.emit(final_amount, from_pos)`
- `scripts/bullet.gd` `_on_grace_timeout()` (line 381) / `scripts/enemy.gd` `_check_body_collision()` (line 355) / `scripts/boss.gd` `_check_body_collision()` (line 960) / `scripts/formation_bomb.gd` `_detonate()` (line 105) (player-hit sites): pass `global_position`
- `scripts/main.gd` `_apply_camera_zoom()` (100-101/300) camera zoom: extract `_apply_camera_zoom()`: `zoom = Vector2.ONE x GameState.view_zoom_factor() x (_meta_fx.breath_scale() if active)` (D6); per-frame if `breath_active()`; `_ready` adds MetaHealthFX child
- `scripts/hud.gd` `_update_vignette()` (755-772): top `if GameState.meta_fx_lod == 0: return` (LOD0 -> MetaFX, `_vignette` 0; LOD1 keeps current, D2)
- `scripts/hud.gd` `_hp_bar` build: holographic: base alpha 0.25, fill `CanvasItemMaterial BLEND_MODE_ADD`; +`meta_jitter()` (D9: +-2px, 80ms, only `_hp_bar`+`_buff_flow`)
- `scripts/settings_ui.gd` `_build_modes_page()` (line 326) after `SET_DISPLAY`: +`SET_ACCESSIBILITY` section + `SET_REDUCE_FLASH` toggle (`make_section_header`/`make_toggle_button`), bound `GameState.set_reduce_flash()`
- `data/translations.csv`: `SET_ACCESSIBILITY` 无障碍/Accessibility; `SET_REDUCE_FLASH` 减少闪光/Reduce flashes
- `scripts/tools/generate_audio.py`: +`heartbeat.wav` - 55Hz sine double-pulse (lub-dub), 0.28s, exp envelope (D7)

### 4.4 `data/balance.json` additions (`effects.meta_health.*`; defaults must match)

```json
"meta_health":{"pulse":{"scale":2.5,"min":0.15,"decay_tau":0.09},"chromatic":{"base":0.006,"peak":0.014},"blur":{"strength":0.6},"ripple":{"duration":0.4,"alpha":0.8},"crack":{"exponent":1.6,"spread_min":0.1,"edge_softness":0.08,"width":0.03,"glow":0.8,"heal_jitter":0.35,"grow_overshoot":0.08,"grow_time":0.6,"density":[0.0,0.30,0.50,0.75,1.0]},"desat":{"max":0.35,"exponent":2.0},"vignette":{"max_alpha":0.5,"inner":0.62,"dying_shrink":0.06},"dying":{"threshold":0.2,"heart_min_hz":1.0,"heart_max_hz":1.2,"breath":0.015,"jitter_px":2.0,"warn_hz":2.5,"fade":0.3},"smooth":{"down_tau":0.10,"up_tau":0.80},"adapt":{"interval":0.25,"min":0.8,"max":1.3,"bullet_weight":0.002,"explosion_weight":0.15},"reduce_flash":{"chromatic_scale":0.4}}
```

## 5. State machine & VFX timing

Transitions: down fast-in, up slow-out (taus/stagger §4.1/§4.4); HitPulse orthogonal; DYING 0.3s fades.

Timeline: 0-80ms CA peak `0.006+0.014*pulse` + ripple influx; 80-300ms exp decay, crest +8%; 300-400ms done; 0-600ms overshoot +8%; persistent: §4.2 + DYING (heartbeat 1.0-1.2Hz, breath +-1.5%, jitter +-2px/beat, warning 2.5Hz, 6% narrowing).

Photosensitivity: no square waves; pulses <=2.5Hz. Reduce-flash: CA x0.4, visual pulses off (sfx kept), static border.

## 6. Layer rules

Explicit: `$HpBar` holographic (§4.3), exact fallback; **no crosshair ring** (bullet-dodge zone [R2/R7]).

## 7. Tests & acceptance

New `test/meta_health_fx_test.tscn/.gd` (headless):

1. `take_damage(10)` -> signal carries amount & from_pos; none in invuln frames.
2. max-pool: 10x `r=0.05` -> `_hit_pulse` <= 0.15.
3. hp=75/50/25/10% -> crack_progress ~ 0.11/0.33/0.63/0.84 (+-0.02). (2026-08-01: 0.72/0.93 typo; `pow(0.75,1.6)=0.63`, `pow(0.90,1.6)=0.84`.)
4. 0.25 down -> instant fast-in; up -> after 0.8s crack_progress falls, `_heal_jitter` 0->0.35->0.
5. hp<20% -> rate in [1.0,1.2]Hz, `breath_scale()` in [0.985,1.015]; reduce-flash -> `breath_active()==false`.
6. `u_lod==1` -> hud `_vignette` low-HP pulse restored (fallback, D2).
7. full HP static 60 frames -> `_process` early-return hits (0 param uploads, D5).

Manual: `ui_capture.tscn` shots - CA peak, ripple bearing, per-tier crack density, DYING narrowing/jitter.

Verify: `--headless --import`, `--quit-after 300`, `smoke_test.tscn`, new test; player/hud changes + `hit_logic_test.tscn`, `balance_test.tscn`.

## 8. Implementation order

- P1 hit layer: CA/blur/ripple shader + D8 + MetaFX skeleton -> tests 1, 2 + shots.
- P2 crack system: D1 pre-bake + state machine + curve -> tests 3, 4.
- P3 implicit & critical layers: vignette/desat/heartbeat/breath/jitter/narrow + main/hud -> test 5 + shots.
- P4 wrap-up: accessibility + LOD + D3 adaptive + audio + tests 6, 7 + update AGENTS.md.
