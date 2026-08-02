# Local Run, Verification & Testing

> This is an on-demand reference doc for `AGENTS.md`: full local run commands, subsystem test scenes, visual screenshot tools, and test-strategy side-effect details. **Minimum must-run set and behavior rules: see `AGENTS.md`**.

Run from the project root. This dev machine has `~/.local/bin/godot` available; if `godot` is already on PATH you can substitute the command directly. `./run.sh` locates the engine automatically.

```bash
# Local run
./run.sh
godot --path .

# Asset import and script parsing
godot --headless --import --path .

# Start the main scene and run 300 frames
godot --headless --path . --quit-after 300

# Minimal must-run main-flow smoke test
godot --headless --path . res://test/smoke_test.tscn

# Save, RP, mission, and base-resupply data layer
godot --headless --path . res://test/base_system_test.tscn
```

The recommended minimal verification set: `--headless --import`, `--quit-after 300`, `smoke_test.tscn`. Add `base_system_test.tscn` when saves, the base, or the mothership are involved; run the dedicated scenes below when the corresponding subsystem is involved.

## Dedicated Test Scenes

```bash
# Gameplay mechanics & balance
godot --headless --path . res://test/enemy_combat_test.tscn
godot --headless --path . res://test/wave_pacing_test.tscn
godot --headless --path . res://test/buff33_test.tscn
godot --headless --path . res://test/buff_visuals_test.tscn
godot --headless --path . res://test/difficulty_test.tscn
godot --headless --path . res://test/boss_enrage_test.tscn
godot --headless --path . res://test/boss_phase_test.tscn
godot --headless --path . res://test/boss_pattern_test.tscn
godot --headless --path . res://test/hit_logic_test.tscn
godot --headless --path . res://test/balance_test.tscn
godot --headless --path . res://test/elite_turret_event_test.tscn
godot --headless --path . res://test/buff_panel_test.tscn
godot --headless --path . res://test/formation_strike_event_test.tscn
godot --headless --path . res://test/orbital_strike_test.tscn
godot --headless --path . res://test/mothership_summon_test.tscn
godot --headless --path . res://test/meta_health_fx_test.tscn

# Settings, startup, navigation & tutorial
godot --headless --path . res://test/keybind_test.tscn
godot --headless --path . res://test/i18n_test.tscn
godot --headless --path . res://test/view_zoom_test.tscn
godot --headless --path . res://test/window_size_test.tscn
godot --headless --path . res://test/mouse_lock_test.tscn
godot --headless --path . res://test/startup_flow_test.tscn
godot --headless --path . res://test/entry_animation_test.tscn
godot --headless --path . res://test/back_navigation_test.tscn
godot --headless --path . res://test/esc_navigation_test.tscn
godot --headless --path . res://test/intro_cinematic_test.tscn
godot --headless --path . res://test/tutorial_test.tscn

# Object pools & performance
godot --headless --path . res://test/pool_reuse_test.tscn
godot --headless --fixed-fps 1000 --path . res://test/perf_bench.tscn

# Autoplay anomaly probe (default real time 480 s; not a normal assertion test)
godot --headless --path . res://test/autoplay_test.tscn -- --autoplay-seconds=480 --seed=20260722
```

Headless frame rate does not equal real time; time-dependent tests should wait on real timers / physics frames — see existing test implementations. Visual tests cannot use the headless dummy renderer:

## Visual Screenshots (Windowed Mode)

```bash
# Windowed: gameplay frame, writes /tmp/infiair_capture.png
godot --path . res://test/visual_capture.tscn

# Windowed: UI pages, writes /tmp/ui_*.png
godot --path . res://test/ui_capture.tscn

# Windowed: homecoming cinematic shot by shot (8 s/shot stretched timeline), writes /tmp/return_shot*.png
godot --path . res://test/return_capture.tscn

# Windowed: intro cinematic shot by shot (8 s/shot stretched timeline), writes /tmp/intro_shot*.png
godot --path . res://test/intro_capture.tscn

# Windowed: full mothership-summon sequence (charge / window / warp gate / tow / stay), writes /tmp/summon_*.png
godot --path . res://test/summon_capture.tscn

# Windowed: Meta HUD HP / hit feedback per HP tier, writes /tmp/meta_fx_*.png
godot --path . res://test/meta_fx_capture.tscn

# Windowed: HUD normal / extreme (all buffs maxed) layout, writes /tmp/hud_*.png
godot --path . res://test/hud_capture.tscn
```

## Unified Check Flow (Pre-Commit / CI Gate)

Five-layer automated checks, all run automatically by CI (`.github/workflows/ci.yml`); reproduce them locally with the commands below:

```bash
# 1. Format: GDScript formatting consistency (line width 140, gdformatrc)
gdformat --check autoload/ scripts/
# 2. Static: style / unused params / naming rules (.gdlintrc rule trade-offs)
gdlint autoload/ scripts/
# 3. Engine compile warnings: zero tolerance at error level (CI fails on "Warning treated as error");
#    warn level (unsafe_cast / untyped declarations, etc.) is visible in the editor script status bar; continuous-improvement list in AUDIT_VAULT
godot --headless --import --path .
# 4. Compile + runtime smoke
godot --headless --path . --quit-after 300
godot --headless --path . res://test/smoke_test.tscn
# 5. Full assertion run: all 31 scenes one by one (excluding the long autoplay_test probe); any FAIL → non-zero exit code
```

- **Tool install** (one-time; into the project-local `.venv/`, excluded by `.gitignore` and never committed; reuses pip's local wheel cache, so no re-download): `python3 -m venv .venv && .venv/bin/pip install gdtoolkit`, then use `.venv/bin/gdformat` / `.venv/bin/gdlint`.
- **Rule trade-offs**: the rationale for `gdformatrc` / `.gdlintrc` / the `project.godot` `[debug]` section is in the in-file comments and `docs/AUDIT_VAULT.md` → "GDScript engine warning tiers"; newly disabled/loosened rules must be synced across all three configs and `AGENTS.md`.
- **Layer division of labor**: gdformat (format) → gdlint (style) → engine warnings (compile-time latent issues) → import/smoke (compile + boot) → assertion scenes (runtime behavior).

## CI Execution (GitHub Actions)

`.github/workflows/ci.yml` runs automatically on push/PR: GDScript static checks (`gdlint` + `gdformat --check`, see §1-2 above) → engine-warning gate (the import step greps "Warning treated as error", see §3 above) → main-scene smoke (`--quit-after 300`) → all 31 assertion scenes (`test/*_test.tscn` excluding the long `autoplay_test` probe), run per scene with exit codes validated; any failure fails the job and uploads the failure-log artifacts. The engine is the official Godot 4.6.2 stable headless binary (Linux x86_64, downloaded from the official Release); no third-party actions (gdtoolkit installed via pip). A fully green CI is the merge gate; the commands above reproduce it locally.

## Test Strategy & Side Effects

Tests are not a unit-test framework; each `test/*.tscn` boots a GDScript scene that self-checks via `[PASS]`/`[FAIL]` output and exit code. `test/` holds 40 scenes in total: 31 assertion scenes, plus `autoplay_test` (probe), `perf_bench` (performance benchmark), and `visual_capture` / `ui_capture` / `return_capture` / `intro_capture` / `summon_capture` / `meta_fx_capture` / `hud_capture` (windowed screenshot tools).

- Tests may read/write `user://savegame.json` and `user://profile.json`. New tests should call `GameState.delete_save()` first and clean up or restore any persistence state they create at the end, so runs stay repeatable.
- `test/balance_test.gd` temporarily **overwrites the project-local** `data/balance.json` to exercise the corruption and fallback paths, then restores the original file. Do not run it while hand-editing that file, and do not assume the file is intact after interrupting it.
- `test/autoplay_test.tscn` is a long-running autoplay and `[ANOMALY]` invariant-monitoring probe; not all issues manifest as regular assertion failures. Registry consistency is checked via two-way comparison of the "enemy" group set (includes turret / formation-craft registrants, skipping the pooled deferred-recycle window); it also covers the buff-card confirm animation path (10% real three-param selection), the stuck-timer exemption during the homecoming cinematic, enrage-slow reset, buff-stack capping, and event/Boss phase counts (SUMMARY output).
- `test/perf_bench.tscn` must run with `--fixed-fps 1000`; headless default frame-rate behavior is unsuitable for directly comparing raw frame cost. For performance A/B, interleave runs and use the median.
- After UI changes, eyeball windowed screenshots manually; headless produces no usable game screenshots.
- **Known failure baselines**: `smoke_test`'s "mothership-kill 1/3 score" check used to fail intermittently (re-run passed); re-verified green on 2026-08-01 and should be treated as self-healed — if it recurs, check recent changes first. `hit_logic_test`'s A21 "player bullets can hit the Boss during the entry-descend phase" was logged as a stable failure baseline (2026-07-31); the 2026-08-01 green run turned out to be a coincidence of the `user://profile.json` view tier (medium); root cause unresolved — **root-caused and fixed on 2026-08-02**: the test hardcoded absolute coordinates `(960,100)`, and under `view_zoom=large` (visible-area top edge y=222) player bullets were destroyed by the `view_world_rect(80)` out-of-bounds check, never reaching the Boss; now positioned dynamically from the fight anchor line `fight_anchor_y()`, with a 9-combination matrix (view tier × difficulty) + multiple consecutive runs all green. Root cause and fix record: `docs/AUDIT_VAULT.md` → "Existing failure baseline disposition record (A21)". **A21 is no longer a failure baseline**; re-run `hit_logic_test` after any change touching view tiers or the Boss anchor line.
