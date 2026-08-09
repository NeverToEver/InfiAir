# Local Run, Verification & Testing (TESTING)

> On-demand reference for `AGENTS.md`: full commands, per-system scenes, screenshots, side effects. **Minimal set & rules: `AGENTS.md`**.

Run at repo root. Engine: .NET build preferred — `godot-mono` (PATH → `~/.local/bin/godot-mono` → `godot`/`godot4` → `~/.local/bin/godot`); the C# project requires the .NET build. `./run.sh` auto-locates.

```bash
./run.sh                                   # run locally
godot --headless --import --path .         # import + script parse
godot --headless --path . --quit-after 300 # main scene, 300 frames
godot --headless --path . res://test/smoke_test.tscn          # minimal smoke
godot --headless --path . res://test/base_system_test.tscn    # saves/RP/tasks/base
dotnet build                               # C# compile (TreatWarningsAsErrors: zero warnings)
dotnet test tests-csharp/                  # xUnit pure-logic unit tests
```

Minimal set: `--import`, `--quit-after 300`, `smoke_test.tscn`; add `base_system_test.tscn` when touching saves/base/mothership; add `dotnet build` + `dotnet test tests-csharp/` when touching `csharp/**` or `tests-csharp/**`; run subsystem scenes when touching that subsystem.

## Scene Counts (authoritative — don't hardcode elsewhere)

- **Assertion scenes** = `ls test/*_test.tscn | wc -l` − 1 (`autoplay_test` probe) → **55** (2026-08-08 M7c 全量迁移 C# 后).
- **Total scenes** = `ls test/*.tscn | wc -l` → **64** (55 assertion + `autoplay_test` + `perf_bench` + 7 screenshot tools; `starfield_cs_test` 计入 assertion).
- Rule: CI gates on the actual `test/*_test.tscn` files — the numbers above are informational. **Other docs must not hardcode assertion counts**; reference this file (rule in `.agents/doc-sync.md`). When adding/removing test scenes, update the counts here.

## Subsystem Scenes

```bash
# Mechanics & config
godot --headless --path . res://test/enemy_combat_test.tscn
godot --headless --path . res://test/wave_pacing_test.tscn
godot --headless --path . res://test/buff33_test.tscn
godot --headless --path . res://test/buff_visuals_test.tscn
godot --headless --path . res://test/buff_effects_test.tscn  # declarative effect table (architecture assertion)
godot --headless --path . res://test/difficulty_test.tscn
godot --headless --path . res://test/boss_enrage_test.tscn
godot --headless --path . res://test/boss_phase_test.tscn
godot --headless --path . res://test/boss_pattern_test.tscn
godot --headless --path . res://test/boss_registry_test.tscn  # boss registry / 4 types (architecture assertion)
godot --headless --path . res://test/hit_logic_test.tscn
# Fairness (2026-08-03; docs/archive/2026-08-03-combat-fairness-plan.md)
godot --headless --path . res://test/grace_period_test.tscn
godot --headless --path . res://test/graze_test.tscn
godot --headless --path . res://test/boss_phase_transition_test.tscn
godot --headless --path . res://test/parry_test.tscn
godot --headless --path . res://test/balance_test.tscn
godot --headless --path . res://test/elite_turret_event_test.tscn
godot --headless --path . res://test/buff_panel_test.tscn
godot --headless --path . res://test/formation_strike_event_test.tscn
godot --headless --path . res://test/base_task_refresh_test.tscn  # task rotation (2026-08-05; docs/FOG_EVENTS.md §1)
godot --headless --path . res://test/fog_event_test.tscn          # fog events (2026-08-05; docs/FOG_EVENTS.md §2)
godot --headless --path . res://test/event_manager_test.tscn      # unified event manager (2026-08-05; docs/EVENT_MANAGER.md)
godot --headless --path . res://test/entity_manager_test.tscn     # unified entity manager (2026-08-05; docs/ENTITY_MANAGER.md)
godot --headless --path . res://test/encounter_flow_contract_test.tscn  # encounter auto-trigger contract / mutex / death window cleanup (2026-08-07; docs/archive/2026-08-07-deferred-restart-plan.md §5)
godot --headless --path . res://test/orbital_strike_test.tscn
godot --headless --path . res://test/mothership_summon_test.tscn
godot --headless --path . res://test/mothership_upgrade_test.tscn  # mothership upgrades (2026-08-04)
godot --headless --path . res://test/meta_health_fx_test.tscn
# Settings / startup / navigation / tutorial
godot --headless --path . res://test/keybind_test.tscn
godot --headless --path . res://test/virtual_controls_test.tscn    # touch controls (2026-08-07; docs/archive/2026-08-07-deferred-restart-plan.md §3)
godot --headless --path . res://test/i18n_test.tscn
godot --headless --path . res://test/view_zoom_test.tscn
godot --headless --path . res://test/window_size_test.tscn
godot --headless --path . res://test/user_db_test.tscn
godot --headless --path . res://test/user_session_test.tscn
godot --headless --path . res://test/welcome_flow_test.tscn  # accounts (2026-08-04)
godot --headless --path . res://test/mouse_lock_test.tscn
godot --headless --path . res://test/startup_flow_test.tscn
godot --headless --path . res://test/entry_animation_test.tscn
godot --headless --path . res://test/back_navigation_test.tscn
godot --headless --path . res://test/esc_navigation_test.tscn
godot --headless --path . res://test/intro_cinematic_test.tscn
godot --headless --path . res://test/return_cinematic_test.tscn  # return cinematic (homecoming, 7 shots)
godot --headless --path . res://test/tutorial_test.tscn
# Pools & perf
godot --headless --path . res://test/pool_reuse_test.tscn
godot --headless --path . res://test/starfield_cs_test.tscn  # Starfield.cs 迁移样板断言 (M1, C# 脚本化)
godot --headless --fixed-fps 1000 --path . res://test/perf_bench.tscn
# C# interop (2026-08-07; GDScript→C# cross-language call, loads res://csharp/godot/BalanceInterop.cs)
godot --headless --path . res://test/csharp_interop_test.tscn
godot --headless --path . res://test/csharp_call_test.tscn        # C# 动态派发调用探针 (M1/M7c, PascalCase 同名)
godot --headless --path . res://test/path_resolver_interop_test.tscn  # P1-1 cfg 路径解析壳
godot --headless --path . res://test/save_store_interop_test.tscn     # P0-1 存档壳
godot --headless --path . res://test/user_db_interop_test.tscn        # P0-2 账户壳
godot --headless --path . res://test/progression_interop_test.tscn    # 进程曲线壳（里程碑/难度）
godot --headless --path . res://test/task_pool_interop_test.tscn      # 任务池壳（无放回抽取）
# Autoplay anomaly probe (~480s real time; not a normal assertion test)
godot --headless --path . res://test/autoplay_test.tscn -- --autoplay-seconds=480 --seed=20260722
```

## Headless Test Environment Notes

- **Headless FPS ≠ real time**; time-dependent tests wait on real timers/physics frames (see existing tests). Visual tests need windowed mode (headless renders nothing).
- **Injected input coordinates are transformed** (2026-08-07 S01, measured 30×): `Input.parse_input_event()` events go through window→viewport transform. Headless window size ≠ design resolution (1920×1080), so mouse/touch positions injected in design coordinates arrive scaled (e.g. `240,860` → `7200,25800`) and are not portable across environments. Keyboard / `InputEventAction` carry no position and are unaffected. **Position-sensitive assertions: drive the target's public test port instead** — `simulate_touch`/`simulate_drag` (`VirtualControls`), `set_test_state` (`MetaHealthFX`), etc. (precedents: AUDIT_VAULT S01/C35; never write private fields — A7).
- **gdtoolkit (gdformat/gdlint) install**: in-project `.venv/` (gitignored), see Tools below. Ubuntu 23.04+ / Debian 12+ system pip is PEP 668-protected (`externally-managed-environment`) — use `.venv` (which also isolates from the system), do **not** `--break-system-packages`. CI runner installs bare `pip` (no PEP 668).
- **`translations.csv` → `.translation`**: runtime loads `data/translations.zh/en.translation` (gitignored, generated by import). After editing `translations.csv`, re-run `godot --headless --import` to regenerate; fresh clones have no `.translation` until the first `--import`.

## Screenshots (windowed)

```bash
godot --path . res://test/visual_capture.tscn     # game frame → /tmp/infiair_capture.png
godot --path . res://test/ui_capture.tscn         # UI pages → /tmp/ui_*.png
godot --path . res://test/return_capture.tscn     # return shots → /tmp/return_shot*.png
godot --path . res://test/intro_capture.tscn      # intro shots → /tmp/intro_shot*.png
godot --path . res://test/summon_capture.tscn     # summon sequence → /tmp/summon_*.png
godot --path . res://test/meta_fx_capture.tscn    # meta HUD tiers → /tmp/meta_fx_*.png
godot --path . res://test/hud_capture.tscn        # HUD normal/all-buffs → /tmp/hud_*.png
```

## Unified Check Flow (pre-commit / CI gate)

Six layers; CI (`.github/workflows/ci.yml`) runs all; reproduce locally:

```bash
gdformat --check autoload/ scripts/ test/     # 1. format (width 140, gdformatrc)
gdlint autoload/ scripts/ test/               # 2. static (style/unused/.gdlintrc)
dotnet build --nologo                          # 3. C# compile (TreatWarningsAsErrors: zero warnings)
dotnet test tests-csharp/ --nologo             #    xUnit pure-logic unit tests
dotnet format --verify-no-changes --no-restore #    C# format gate (root project = csharp/godot)
dotnet format csharp/core/InfiAir.Core.csproj --verify-no-changes --no-restore
dotnet format tests-csharp/InfiAir.Core.Tests.csproj --verify-no-changes --no-restore
godot --headless --import --path .             # 4. warnings: error-level zero tolerance
                                               #    ("Warning treated as error" fails CI);
                                               #    warn-level (unsafe/untyped) = AUDIT_VAULT list
godot --headless --path . --quit-after 300     # 5. compile + runtime smoke
godot --headless --path . res://test/smoke_test.tscn
# 6. all 55 assertion scenes (excl. autoplay probe); any FAIL → non-zero exit
```

- **Tools** (one-time, in-project `.venv/`, gitignored): `python3 -m venv .venv && .venv/bin/pip install gdtoolkit==4.5.0` (版本与 `ci.yml` 对齐, 2026-08-05 R09) → `.venv/bin/gdformat`/`.venv/bin/gdlint`. (PEP 668 系统保护环境必须用 `.venv`——见「Headless Test Environment Notes」.)
- **Rule rationale**: `gdformatrc`/`.gdlintrc`/`project.godot` `[debug]` comments + `docs/AUDIT_VAULT.md`; new disables/relaxes sync those configs + `AGENTS.md`.
- Layers: format → style → C# build/test → engine warnings → compile/start → runtime behavior.

## CI

push/PR: Install .NET SDK 8 (official `dotnet-install.sh`) → **dotnet build (warnings-as-errors) + dotnet test tests-csharp/** (xUnit pure-logic) → **dotnet format gate** (三工程 `--verify-no-changes` 零 diff, 2026-08-09 全量规范化后防回归) → zero-GDScript gate (M7d: 任何 .gd 即失败) → warning gate (import grep) → main smoke → **compile probe** (every `test/*.tscn` with `--quit-after 2`; catches Parse/Compile/SCRIPT ERROR that `--import` misses, e.g. screenshot tools) → all 55 assertion scenes (`test/*_test.tscn` minus `autoplay_test`; 2026-08-04: + `user_db_test`/`user_session_test`/`welcome_flow_test`/`mothership_upgrade_test`; 2026-08-05: + `base_task_refresh_test`/`fog_event_test`/`event_manager_test`/`entity_manager_test`; 2026-08-07: + `encounter_flow_contract_test`/`virtual_controls_test`/`csharp_interop_test`/`path_resolver_interop_test`/`save_store_interop_test`/`user_db_interop_test`/`progression_interop_test`/`task_pool_interop_test`) with exit-code checks + per-scene 300s timeout; any failure fails job + uploads logs. 2026-08-09 (V 系列): + 引擎错误日志扫描（退出码 0 但日志含 SCRIPT ERROR/Parse Error/Compile Error/Nonexistent function 即失败——死测试曾静默通过）+ 场景数硬校验（run != 发现数-1 即失败，堵住改名/新增静默掉出 CI；不硬编码计数）。Engine: official Godot 4.6.2 stable **mono** headless (Linux x86_64, official Release); deps policy: official checkout/upload-artifact actions + official `dotnet-install.sh` + official Godot engine/templates only. Green = merge gate.

## Strategy & Side Effects

Not a unit framework: each `test/*.tscn` runs its C# test script, self-checks `[PASS]`/`[FAIL]` + exit code. **64 scenes: 55 assertions + `autoplay_test` + `perf_bench` + 7 screenshot tools.** Pure-logic unit tests live in `tests-csharp/` (xUnit, `dotnet test tests-csharp/`). (2026-08-09 U18: 计数算式统一——8 screenshot tools 实为 7，55+1+1+7=64)

- Tests may touch `user://` saves (`savegame_<user>_<hash>.json` / `users.json` / `profile.json`): new tests `GameState.delete_save()` first + clean/restore own state.
- `balance_test.gd` temporarily **overwrites** in-repo `data/balance.json` (corruption/fallback) then restores — don't edit that file concurrently; don't assume it intact after interruption.
- `autoplay_test`: long probe with `[ANOMALY]` invariants (not ordinary assertions); registry bidirectional check vs `enemy` group (incl. turret/formation, skipping pooled deferred-recycle), buff-confirm anim path (10% real roll), return-cinematic stall exemption, enrage-slow reset, buff caps, event/boss phase counts (SUMMARY).
- `perf_bench` needs `--fixed-fps 1000`; interleave runs + medians for A/B.
- UI changes: human-check windowed screenshots (headless produces none).
- **Known-failure baseline**: `smoke_test` "mothership kill 1/3 score" flaked (rerun passes; re-verified 2026-08-01, self-healed). `hit_logic_test` A21 was a stable baseline (2026-07-31); 2026-08-01 found `user://profile.json` zoom coincidence; **root-caused + fixed 2026-08-02**: test hardcoded `(960,100)`; at `view_zoom=large` (visible top y=222) player bullets died to `view_world_rect(80)` out-of-bounds before reaching the boss; now positioned via `fight_anchor_y()`; 9-combo (zoom×difficulty) green. Record: `docs/AUDIT_VAULT.md` A21. **A21 no longer a baseline** — rerun `hit_logic_test` after zoom-tier/boss-anchor changes.
