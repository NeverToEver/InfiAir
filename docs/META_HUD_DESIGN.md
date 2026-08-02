# Meta HUD Health & Hit-Feedback System Design (Implementation Spec)

Status: **ready for implementation**. This document is self-contained to the point of direct coding: every parameter has a named value, every integration point has a file/line anchor, and every decision is final (all decisions favor performance). Acceptance criteria after implementation: §7.

Technical prerequisites:

- Godot 4.6 + GL Compatibility, pure GDScript, design viewport 1920×1080.
- Compatibility has no HDR bloom or Compositor: emulated glow via ADD blending (project `_glow()` convention); fullscreen post-processing via a fullscreen ColorRect with canvas_item shader + `hint_screen_texture` [R4].
- Known Compatibility pitfalls (must handle at implementation): screen texture Y may be flipped — decide at runtime by renderer; no mipmaps, `textureLod` unavailable, hand-written multi-tap blur [R4].

---

## 1. Research Findings (Traceability Baseline)

| # | Reference | Adopted | Rejected |
| --- | --- | --- | --- |
| R1 | Dead Space diegetic UI | "zero visual search" principle: state conveyed passively, no UI hunting | spine-mounted carrier (2D top-down ship too small) → screen-layer carrier instead |
| R2 | Radial chromatic aberration / RGB split (Xor gmshaders, godotshaders) | 3-tap radial chromatic aberration as primary hit effect, strong at edges, weak at center | uniform fullscreen aberration (blurs center view) |
| R3 | Voronoi procedural cracks (godotshaders Impact Glass, GitHub Godot-Glass-Break-Effect) | pre-baked distance field + progress thresholding for cracks; bidirectional crack–dissolve | full-resolution realtime Voronoi |
| R4 | Godot 4 fullscreen post-processing (The Shaggy Dev, official docs, issue #50976) | ColorRect + `hint_screen_texture` pipeline; hand-written multi-tap blur | `textureLod` blur (unreliable on Compatibility). Note: Godot 4.3+ gl_compatibility Y-normalizes screen_texture — sample `SCREEN_UV` directly; the earlier manual Y flip mirrored the world vertically (removed after on-device screenshot audit) |
| R5 | Cyberpunk 2077 glitch aesthetics (known pattern, no primary literature) | jitter affects only the HUD data layer, never the scene | fullscreen glitch (hides bullet-hell readability) |
| R6 | Titanfall 2 helmet HUD (known pattern, no primary literature) | attack direction → directional edge-arc ripple, replaces fullscreen red flash | wide arc covers bullets (ripple limited to 12% edge band) |
| R7 | Helldivers 2 danger vignette (evidenced by official patch) | vignette/desaturation as subliminal-layer HP semantics | vignette too strong hides view → inner radius cap + adaptive compensation |

Pipeline order (§3 shaders follow this):

```text
Screen grab (hint_screen_texture, Y-normalized on Godot 4.3+, sample directly) [R4]
 → Hit layer: radial chromatic aberration + hand-written 4-tap radial blur [R2] (2026-08-02 perf optimization downsampled from 6-tap)
 → Directional ripple overlay (edge arc, ADD) [R6]
 → Desaturation/color shift + vignette [R7]
 → Crack composite (samples pre-baked distance field) [R3]
Render position: CanvasLayer layer=1 (above world, below HUD; main-scene HUD raised to layer=2; below OrbitalStrike 24 and cinematics 35)
```

Transient layers render before persistent ones: the impact modifies the sampled result, then persistent state stacks on top, so chromatic aberration cannot smear the crack edges.

---

## 2. Decision Log (All Favoring Performance)

| # | Decision point | Decision | Rejected alternatives & rationale |
| --- | --- | --- | --- |
| D1 | Crack distance-field source | **Runtime SubViewport single-frame pre-bake** (once at startup, 512², cost 1 frame) | Offline PNG tool: adds an asset pipeline and binary assets, only saves 1 frame; realtime Voronoi: fullscreen per-frame iteration [R3] |
| D2 | LOD implementation | **Single shader + `u_lod` uniform branch** (uniform branch is identical across pixels, GPU cost ≈0) | Dual shaders / frame-sequence assets: double maintenance + VRAM + asset pipeline |
| D3 | Adaptive readability | **Registry proxy brightness**: `bullet_pool active count ×0.002 + active explosion count ×0.15` estimates screen brightness with zero GPU readback | Screen-pixel readback (`get_texture().get_image()`): per-frame readback is a perf disaster on Compatibility; even throttled at 0.25s there is stutter risk |
| D4 | Radial blur | **Hand-written 4-tap centripetal sampling** (2026-08-02 perf optimization downsampled from 6-tap), whole segment skipped when `u_radial_blur_strength < 0.001` | `textureLod`: no mipmaps on Compatibility; half-res intermediate texture: an extra fullscreen copy |
| D5 | Shader parameter updates | **epsilon change detection**: GDScript caches the last value, no `set_shader_parameter` call when change <0.001; `_process` early-outs when everything is stable | Unconditional per-frame upload: uniform updates are cheap, but 16 params × 60fps is pure waste |
| D6 | Breath scaling | **Composed in main.gd**: `zoom = view_zoom_factor × breath_scale()`, applied per-frame only during DYING | MetaFX holding a camera ref directly: breaks layering; unconditional per-frame zoom set: needless dirty marking |
| D7 | Heartbeat audio | **One-shot trigger via the GameState SFX pool**; `generate_audio.py` offline-generates a 0.28s double-pulse WAV | Looping stream: hard cut on start/stop; cannot express heart rate varying with HP |
| D8 | Hit-direction propagation | **`take_damage(amount, from_pos := Vector2.INF)` default parameter**, the four player-hit call sites pass the position, all other callers unchanged | New signal emitted at call sites: direction logic scattered across 4 files |
| D9 | HUD jitter | **Jitter only two controls' position** — `_hp_bar` and the buff chips container (±2px, 80ms burst) | Whole-HUD jitter: dozens of controls displaced per frame |
| D10 | State-machine update | Pure arithmetic per frame (no trig hot path; engine `sin()` ≤3×/frame) + early-out | 0.1s throttle: hurts visual continuity, pulsation jumps |

---

## 3. Shader Spec (`assets/shaders/meta_health.gdshader`)

### 3.1 Uniform Table

| Parameter | Type | Default | Driver | Description | Source |
| --- | --- | --- | --- | --- | --- |
| `screen_texture` | `sampler2D hint_screen_texture, filter_linear` | — | engine | screen grab (Y-normalized on 4.3+, sample `SCREEN_UV`) | [R4] |
| `u_crack_field` | sampler2D | — | D1 pre-bake | R=distance from crack line (0 on line), G=cell hash, B=growth gate (0 edge → 1 center) | [R3] |
| `u_hit_intensity` | float | 0.0 | hit_pulse × ripple_alpha | total hit intensity | [R2] |
| `u_hit_dir` | vec2 | (0,0) | attack direction (world→screen normalized) | directional ripple bearing; zero vector = uniform ring over all edges | [R6] |
| `u_chromatic_amount` | float | 0.0 | pulse envelope | radial aberration offset (0..0.02) | [R2] |
| `u_radial_blur_strength` | float | 0.0 | pulse envelope | skip whole segment when <0.001 (D4) | [R2][R4] |
| `u_ripple_phase` | float | 1.0 | GDScript 0→1 (0.4s) | ripple advance phase | [R6] |
| `u_crack_progress` | float | 0.0 | §4.2 curve | crack spread progress | [R3] |
| `u_crack_color` | vec4 source_color | cyan | state interpolation | crack glow color | [R3] |
| `u_crack_density` | float | 0.0 | state tier cap | crack alpha cap | [R3] |
| `u_crack_spread_min` | float | 0.15 | read from config in `_ready` | growth-gate lower bound (no cracks at full HP) | [R3] |
| `u_crack_edge_softness` | float | 0.08 | read from config in `_ready` | growth-gate activation softness | [R3] |
| `u_crack_width` | float | 0.10 | read from config in `_ready` | crack line half-width (field space) | [R3] |
| `u_heal_jitter` | float | 0.0 | GDScript heal phase 0→0.35→0 | cell offset-dissolve amplitude | [R3] |
| `u_desaturation` | float | 0.0 | damage_x | subliminal-layer desaturation | [R7] |
| `u_hue_cool` | float | 0.0 | damage_x | cool-cyan tint strength | [R7] |
| `u_vignette_strength` | float | 0.0 | damage_x + LOD1 fallback | vignette | [R7] |
| `u_vignette_inner` | float | 0.62 | narrowed to 0.56 during DYING | preserve center view | [R7] |
| `u_heartbeat` | float | 0.0 | heartbeat phase 0→1→0 | vignette pulses with heartbeat | [R7] |
| `u_adapt_gain` | float | 1.0 | D3 proxy brightness | readability compensation | [R7] |
| `u_lod` | int | 0 | setting | 0=full pipeline, 1=skip aberration/blur/ripple (D2) | [R3] |

"Reduce flashes" stays **out of the shader**: GDScript pre-scales parameters before upload (aberration ×0.4, heartbeat/jitter zeroed), so the shader stays branch-free (an extension of the D5 philosophy).

### 3.2 Fragment Pipeline (Ordered Implementation — Executable Version of the Pipeline Order)

```glsl
shader_type canvas_item;
// …uniform 声明如上表…

void fragment() {
    vec2 uv = SCREEN_UV;                                        // 4.3+ 已 Y 归一，勿再手动翻转 [R4]
    vec2 to_center = uv - vec2(0.5);
    // 16:9 修正 + 归一：边中点 dist≈1.0、四角≈1.15，晕影/波纹/色差阈值按此标定
    float dist = length(to_center * vec2(1.0, 0.5625)) * 2.0;
    float radial_w = smoothstep(0.25, 1.0, dist);              // 边缘强、中心弱 [R2]

    // ── 1. 受击层：径向色差 + 4-tap 径向模糊（LOD1 整段跳过，D2/D4；2026-08-02 减采样至 4-tap）──
    vec3 col;
    if (u_lod == 0 && (u_chromatic_amount > 0.0001 || u_radial_blur_strength > 0.001)) {
        vec2 off = normalize(to_center + 1e-6) * radial_w * u_chromatic_amount;
        // P1-7：色差 3 采样 → 2 采样（G 通道取两端平均，色差感不变）
        vec3 a = texture(screen_texture, uv + off).rgb;
        vec3 b = texture(screen_texture, uv - off).rgb;
        col = vec3(a.r, mix(a.g, b.g, 0.5), b.b);
        if (u_radial_blur_strength > 0.001) {                  // D4：手写 4-tap 向心
            vec3 acc = col;
            for (int i = 1; i <= 3; i++) {
                float t = float(i) / 4.0 * u_radial_blur_strength * radial_w * 0.06;
                acc += texture(screen_texture, mix(uv, vec2(0.5), t)).rgb;
            }
            col = acc / 4.0;
        }
    } else {
        col = texture(screen_texture, uv).rgb;
    }

    // ── 2. 定向波纹（边缘 12% 带，ADD，LOD1 跳过）[R6] ──
    if (u_lod == 0 && u_hit_intensity > 0.01) {
        float ang = atan(to_center.y, to_center.x);
        float dir_w = pow(max(cos(ang - atan(u_hit_dir.y, u_hit_dir.x)), 0.0), 3.0);
        float band = smoothstep(0.88, 0.98, dist);             // 只取边缘带
        float wave = sin((dist - u_ripple_phase * 1.1) * 40.0) * 0.5 + 0.5;
        float ripple = band * dir_w * wave * (1.0 - u_ripple_phase) * u_hit_intensity;
        col += vec3(0.4, 0.9, 1.0) * ripple;                   // alpha 已并入 u_hit_intensity（GDScript 侧）
    }

    // ── 3. 暗示层：去饱和 + 冷青色偏 [R7] ──
    float lum = dot(col, vec3(0.299, 0.587, 0.114));
    col = mix(col, vec3(lum), u_desaturation);
    col = mix(col, col * vec3(0.85, 1.0, 1.1), u_hue_cool);

    // ── 4. 晕影 + 心跳脉冲 + 视野收窄（u_adapt_gain 补偿，D3）[R7] ──
    float vig = smoothstep(u_vignette_inner, 1.15, dist);
    float vig_a = clamp(u_vignette_strength * (1.0 + 0.35 * u_heartbeat), 0.0, 0.6) * u_adapt_gain;
    col = mix(col, vec3(0.35, 0.02, 0.05), vig * vig_a);

    // ── 5. 裂纹合成（裂隙线细带 × 生长门：progress 从边缘向中心逐区激活，修复期按单元 hash 错峰）[R3] ──
    vec3 field = texture(u_crack_field, uv).rgb;
    float gate = mix(u_crack_spread_min, 1.0, field.b) + (field.g - 0.5) * u_heal_jitter;
    float on = smoothstep(gate, gate + u_crack_edge_softness, u_crack_progress);
    float line = 1.0 - smoothstep(0.0, u_crack_width, field.r);
    float mask = line * on * u_crack_density;
    col = mix(col, u_crack_color.rgb * 0.25, mask * 0.6);         // 裂隙暗底
    col += u_crack_color.rgb * mask * 0.8 * u_adapt_gain;         // ADD 伪泛光

    COLOR = vec4(col, 1.0);
}
```

Note: an earlier crack scheme thresholded a single R-channel value mixing "distance to crack line" with "radial growth gate"; as the threshold swept up it selected whole cell regions (fullscreen blotches / red-light flooding); splitting into the dual channels R (line distance) × B (growth gate) yields the fine crack network (confirmed and corrected by on-device screenshot audit).

Distance-field bake shader (small standalone shader, used once at D1 startup): SubViewport(512×512) + ColorRect; the fragment computes F1/F2 Voronoi over 12 fixed seed points (constant array, hashed on `uv`) — crack lines lie at cell boundaries (F2−F1≈0) — and writes R=distance from crack line / G=nearest cell hash / B=growth gate (radial, 0 edge → 1 center). In `_ready`, after `RenderingServer.frame_post_draw`, `get_texture().get_image()` retrieves it into an `ImageTexture`, then the SubViewport is released. **One-time operation, readback cost acceptable** (unlike the per-frame readback rejected in D3). Headless dummy rendering uses the `_cpu_bake_image()` CPU-equivalent fallback (64², same formula as the shader).

---

## 4. GDScript Spec

### 4.1 `scripts/meta_health_fx.gd` (New File, `class_name MetaHealthFX extends CanvasLayer`)

```text
layer = 1 (above world, below HUD; main-scene HUD raised to layer=2); process_mode default Pausable (HP is unchanged while paused, freezing the effect is fine; early-out keeps cost ≈0)

const STATE_NORMAL := 0 / STATE_CAUTION := 1 / STATE_DAMAGED := 2 / STATE_CRITICAL := 3 / STATE_DYING := 4
const THRESHOLDS := [0.75, 0.50, 0.25, 0.20]        # 下行边界
const DENSITY_CAPS := [0.0, 0.30, 0.50, 0.75, 1.0]  # 各状态裂纹密度上限

Members:
  _state: int; _damage_x := 0.0 (smoothed value); _target_x := 0.0
  _hit_pulse := 0.0; _hit_dir := Vector2.ZERO (zero vector = uniform ring over all edges); _ripple_t := 2.0 (>1 = no ripple)
  _heal_jitter := 0.0; _heart_phase := -1.0 (<0 = not DYING); _breath := 1.0
  _mat: ShaderMaterial; _last: Dictionary (D5 epsilon cache)
  _cfg: Dictionary (one-time read of effects.meta_health.* in _ready)

_ready():
  1. Fullscreen ColorRect (mouse_filter=IGNORE) + ShaderMaterial (§3)
  2. Static uniforms: u_lod / u_crack_spread_min / u_crack_edge_softness / u_crack_width
  3. _bake_crack_field() (D1)
  4. Connect: GameState.health_changed → _on_health_changed
            GameState.player_damaged → _on_player_damaged
            GameState.player_died → _on_player_died
  5. GameState.meta_fx_lod = _cfg lod (for hud.gd fallback check)

_process(delta):
  Early-out: |_target_x-_damage_x|<0.001 and _hit_pulse<0.001 and _ripple_t>1.0 and _heart_phase<0 and _heal_jitter stable → return
  1. tau = _cfg.smooth.down_tau if descending else up_tau; _damage_x approaches _target_x exponentially
  2. State-transition check → crossing a threshold triggers crack grow overshoot (+0.08, 0.6s decay) or heal-phase _heal_jitter 0→0.35→0 (0.7s)
  3. _hit_pulse exponential decay (tau=0.09); _ripple_t += delta/0.4
  4. DYING: _heart_phase advances by heart rate (1.0→1.2Hz, interpolated by x);
     per beat: _heartbeat pulse envelope (0.3s), _breath = 1 + 0.015*sin(phase), trigger heartbeat SFX (D7),
     HUD jitter burst (calls the hud group node "meta_jitter" method, D9)
     non-DYING: _breath approaches 1, heartbeat envelope fades out over 0.3s (no hard cut)
  5. D3: 0.25s-throttled proxy brightness → u_adapt_gain (clamp 0.8..1.3)
  6. D5: set_shader_parameter after epsilon check

_on_player_damaged(amount: float, from_pos: Vector2):
  r = amount / GameState.max_health()
  _hit_pulse = maxf(_hit_pulse, clampf(r * 2.5, 0.15, 1.0))   # max 池化，防高频低伤累积 [R2]
  _hit_dir = world→screen direction (ripple degrades to uniform edge ring when from_pos==Vector2.INF)
  _ripple_t = 0.0

breath_scale() -> float: for main.gd D6 composition
breath_active() -> bool: _state == STATE_DYING and reduce-flash off
```

### 4.2 HP-to-Crack Mapping Curve (Computed in `_on_health_changed`)

```text
x = 1 − hp/max_hp
_target_x = x
crack_progress = pow(_damage_x, 1.6)            # shader 参数，采样：x=0.25→0.11，0.50→0.33，0.75→0.63，0.90→0.84
Color crossfade (bandwidth 0.08): x<0.25 cyan #35E0FF → <0.5 yellow #FFD23F → <0.8 orange #FF8A3D → ≥0.8 red #FF3B4E
Desaturation = 0.35 × pow(x, 2.0); tint u_hue_cool = 0.6 × x
Vignette = min(0.5, crack_progress × 0.55); on DYING u_vignette_inner 0.62→0.56 (view narrows 6%, 0.3s smoothing)
```

### 4.3 Integration Point Changes (File + Anchor)

| File | Anchor | Change |
| --- | --- | --- |
| after `autoload/game_state.gd:18` | signal area | Add `signal player_damaged(amount: float, from_pos: Vector2)`; add `var meta_fx_lod: int = 1` (default LOD1 fallback; MetaFX sets 0 in `_ready`, back to 1 in `_exit_tree`) and `var reduce_flash: bool = false` (setter persists to profile, modeled on the aim_assist pattern) |
| `scripts/player.gd:527` | `take_damage` | Add `from_pos: Vector2 = Vector2.INF` to the signature (D8); after successful resolution emit `GameState.player_damaged.emit(final_amount, from_pos)` |
| `scripts/bullet.gd:196`、`scripts/enemy.gd:197`、`scripts/boss.gd:1388`、`scripts/formation_bomb.gd:87` | player-hit call sites | Pass the damage source's `global_position` |
| `scripts/main.gd:85,113` | camera zoom | Extract `_apply_camera_zoom()`: `zoom = Vector2.ONE × GameState.view_zoom_factor() × (_meta_fx.breath_scale() if active)` (D6); called per-frame from `_process` only while `breath_active()`; `_ready` creates MetaHealthFX and attaches it as a child |
| `scripts/hud.gd:597-608` | `_update_vignette` | Add `if GameState.meta_fx_lod == 0: return` at the top (LOD0 hands over to MetaFX, `_vignette` alpha stays 0; LOD1 keeps current behavior as fallback, D2) |
| `scripts/hud.gd` `_hp_bar` build site | holographic HpBar | Base plate alpha 0.25, fill segment `CanvasItemMaterial BLEND_MODE_ADD`; add `meta_jitter()` method for D9 (position ±2px, 80ms, only `_hp_bar` and `_buff_flow`) |
| after the `SET_DISPLAY` section in `scripts/settings_ui.gd:214` | new section | `SET_ACCESSIBILITY` section + `SET_REDUCE_FLASH` toggle (`make_section_header`/`make_toggle_button` convention), bound to `GameState.set_reduce_flash()` |
| `data/translations.csv` | new keys | `SET_ACCESSIBILITY` 无障碍/Accessibility; `SET_REDUCE_FLASH` 减少闪光/Reduce flashes |
| `scripts/tools/generate_audio.py` | new function | `heartbeat.wav`: 55Hz sine double pulse (lub-dub), 0.28s, exponential envelope (D7) |

### 4.4 New in `data/balance.json` (`effects.meta_health.*`, script defaults must match)

```json
"meta_health": {
  "pulse": { "scale": 2.5, "min": 0.15, "decay_tau": 0.09 },
  "chromatic": { "base": 0.006, "peak": 0.014 },
  "blur": { "strength": 0.6 },
  "ripple": { "duration": 0.4, "alpha": 0.8 },
  "crack": { "exponent": 1.6, "spread_min": 0.1, "edge_softness": 0.08, "width": 0.03,
             "glow": 0.8, "heal_jitter": 0.35, "grow_overshoot": 0.08, "grow_time": 0.6,
             "density": [0.0, 0.30, 0.50, 0.75, 1.0] },
  "desat": { "max": 0.35, "exponent": 2.0 },
  "vignette": { "max_alpha": 0.5, "inner": 0.62, "dying_shrink": 0.06 },
  "dying": { "threshold": 0.2, "heart_min_hz": 1.0, "heart_max_hz": 1.2,
             "breath": 0.015, "jitter_px": 2.0, "warn_hz": 2.5, "fade": 0.3 },
  "smooth": { "down_tau": 0.10, "up_tau": 0.80 },
  "adapt": { "interval": 0.25, "min": 0.8, "max": 1.3,
             "bullet_weight": 0.002, "explosion_weight": 0.15 },
  "reduce_flash": { "chromatic_scale": 0.4 }
}
```

---

## 5. State Machine & VFX Timing (Behavioral Acceptance Baseline)

State transitions: fast entry on descent (tau=0.10s), slow exit on ascent (tau=0.80s + offset dissolve 0.7s); HitPulse is orthogonal to state; DYING fades in and out over 0.3s, no hard cuts.

| Time | Aberration/blur [R2] | Directional ripple [R6] | Cracks [R3] | DYING critical layer [R5][R7] |
| --- | --- | --- | --- | --- |
| t=0–80ms | peak `0.006+0.014×pulse` | arc wave surges in along the 12% edge band | — | — |
| t=80–300ms | exponential decay (tau≈90ms) | wave crest advances 8% centripetally, alpha decays | — | — |
| t=300–400ms | — | dissipated | — | — |
| t=0–600ms | — | — | cluster growth on threshold crossing (overshoot +8% then decay) | — |
| persistent | — | — | §4.2 curve | heartbeat 1.0–1.2Hz; breath ±1.5%; jitter ±2px/beat; warning border 2.5Hz sine; view narrowed 6% |

Photosensitivity safety: no square waves anywhere in the system; all pulsation ≤2.5Hz. With "reduce flashes" on: aberration ×0.4, breath/jitter/heartbeat visual pulses disabled (SFX kept), warning becomes a static border.

## 6. Explicit vs. Subliminal Layers

- Explicit layer: `$HpBar` keeps the holographic treatment (§4.3), guaranteeing exact values; **no reticle ring** (the area around the reticle is the bullet-reading zone [R2/R7]).
- Subliminal layer: desaturation + cool-cyan tint + heartbeat SFX / breath sync, degradable via "reduce flashes".
- Redundancy: the Meta layer carries "feel", SegmentedBar carries "numbers" — single-point fallback, avoiding Helldivers 2-style vignette controversy [R7].

## 7. Testing & Acceptance

New `test/meta_health_fx_test.tscn/.gd` (headless assertions):

1. `take_damage(10)` → `player_damaged` signal carries amount and from_pos; not emitted during invulnerability frames.
2. max pooling: 10 consecutive hits of `r=0.05`, `_hit_pulse` stays ≤0.15 (no accumulation).
3. Curve sampling: hp=75%/50%/25%/10% → crack_progress ≈ 0.11/0.33/0.63/0.84 (±0.02). (Corrected 2026-08-01: the original 0.72/0.93 was a typo — `pow(0.75,1.6)=0.63`, `pow(0.90,1.6)=0.84`, consistent with the §4.2 samples and the `meta_health_fx_test.gd` assertions.)
4. State machine: hp crossing 0.25 descending enters instantly; after 0.8s of ascending heal, crack_progress falls back and `_heal_jitter` runs the full 0→0.35→0 cycle.
5. DYING: at hp<20%, heart rate within [1.0,1.2]Hz and `breath_scale()` ∈ [0.985,1.015]; with "reduce flashes" on, `breath_active()==false`.
6. LOD1: with `u_lod==1`, hud `_vignette` resumes low-HP pulsation (fallback path, D2).
7. Early-out: after 60 idle frames at full HP, `_process` early-out triggers (instrumented counter shows 0 parameter uploads, D5).

Manual check: `ui_capture.tscn` convention windowed screenshots — hit aberration peak, directional ripple orientation, crack density per HP tier, DYING narrowing and jitter.

Mandatory verification set: `--headless --import`, `--quit-after 300`, `smoke_test.tscn`, the new dedicated test; when player/hud changes touch hit-taking and low HP, additionally run `hit_logic_test.tscn` and `balance_test.tscn`.

## 8. Implementation Order (Each Phase Independently Verifiable)

- P1 hit layer: shader aberration/blur/ripple + D8 signal + MetaFX skeleton → P1 acceptance: tests 1 and 2 + screenshots.
- P2 crack system: D1 pre-bake + state machine + curve → acceptance: tests 3 and 4.
- P3 subliminal & critical layers: vignette/desaturation/heartbeat/breath/jitter/narrowing + main/hud integration → acceptance: test 5 + screenshots.
- P4 wrap-up: accessibility + LOD + D3 adaptive + audio + tests 6 and 7 + update AGENTS.md.

## Appendix: Research Source Index

- [R1] https://blog.csdn.net/ludongguoa/article/details/120773182
- [R2] https://mini.gmshaders.com/p/gm-shaders-mini-chromatic-aberration ；https://godotshaders.com/shader/radial-chromatic-aberration/
- [R3] https://godotshaders.com/shader/impact-glass-shader/ ；https://github.com/Lord0Sanz/Godot-Glass-Break-Effect
- [R4] https://shaggydev.com/2025/04/09/godot-ui-postprocessing-shaders/ ；https://github.com/godotengine/godot/issues/50976
- [R7] Helldivers 2 official patch notes (danger vignette entry, relayed via Deltia's Gaming)
- [R5][R6] Known industry design patterns; no primary technical literature found, only the structural patterns were adopted.
