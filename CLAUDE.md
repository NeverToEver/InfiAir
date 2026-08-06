# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**Authoritative conventions: `AGENTS.md` + `.agents/*`** (collision layers, testing, perf, i18n, tuning, GDScript style) — read before changes. This file: entry-level overview only. Direction/plans: `docs/ROADMAP.md`. **`docs/AUDIT_VAULT.md` is a proprietary audit archive — never remove**; consult before core-logic changes.

## Project

InfiAir: 2D top-down shooter, Godot 4.6 + GDScript (gl_compatibility, no external plugins). Remade from `airwar-game` (Python/Pygame), now independently evolved (`docs/archive/PORTING_PARITY.md`). Vertical-scroll starfield, waves, milestone buff 3-choice, rotating bosses + enrage, mothership resupply, return-to-base restock. Score-only.

- Entry scene `scenes/welcome.tscn` (accounts/login); battle scene `scenes/main.tscn`; 1920×1080 (stretch `canvas_items`/`keep`).
- Only autoload `GameState` (`autoload/game_state.gd`): global state/signal bus, sfx pool, `GameState.cfg()` config access, persistence.
- No build system/package manager; CI via GitHub Actions. Godot 4.6 only (4.6.2 std, no .NET); binary `~/.local/bin/godot` (not on PATH). Prefer `./run.sh` — auto-detects PATH → `~/.local/bin/godot` → `/Applications/Godot.app`, validates version, passes args through.

## Commands

```bash
G=~/.local/bin/godot
$G --path .                                    # run locally
$G --headless --import --path .                # import + script parse check
$G --headless --path . --quit-after 300        # 300-frame runtime check
# Headless tests: test/*.tscn self-check via [PASS]/[FAIL] + exit code.
# 45 assertion scenes (54 total); full list & known baseline: docs/TESTING.md
$G --headless --path . res://test/smoke_test.tscn           # main flow — always after changes
$G --headless --path . res://test/base_system_test.tscn     # saves/base/mothership changes
$G --headless --path . res://test/autoplay_test.tscn [-- --autoplay-seconds=480] [-- --seed=N]
$G --headless --fixed-fps 1000 --path . res://test/perf_bench.tscn  # perf (needs --fixed-fps)
# All 45 assertion scenes, one-liner (same selection as CI: test/*_test.tscn minus autoplay probe):
for t in test/*_test.tscn; do
  case "$t" in *autoplay_test.tscn) continue;; esac
  $G --headless --path . "res://$t" || break
done
```

Minimum after changes: `--import`, `--quit-after 300`, `smoke_test.tscn`; add `base_system_test.tscn` when touching saves/base/mothership. Screenshots need windowed mode (headless captures nothing): `test/visual_capture.tscn` (game → /tmp/infiair_capture.png), `test/ui_capture.tscn` (UI → /tmp/ui_*.png).

# Pre-commit gate (5 layers; CI runs all): format + static first, then the above engine checks
python3 -m venv .venv && .venv/bin/pip install gdtoolkit==4.5.0   # one-time; .venv/ gitignored（版本与 ci.yml R09 对齐）
.venv/bin/gdformat --check autoload/ scripts/ test/        # layer 1: format (w=140)
.venv/bin/gdlint autoload/ scripts/ test/                  # layer 2: style/unused

## Architecture Essentials

- `main.gd` orchestrates: spawns, milestone buffs, boss schedule, mothership charge, return timer.
- Config: all tunables in `data/balance.json`, read via `GameState.cfg("player.fuel.drain", default)`; **tune JSON only, never script constants** (script vars are fallbacks, must match JSON); cache in `_ready()`, no per-frame cfg.
- Collision layers: 1=player 2=player_bullet 3=enemy(+boss) 4=enemy_bullet; bullets resolve damage; player hit only via `Hitbox` Area2D (r=7 × `world_scale` 0.4 → 2.8); enemy/boss ramming via per-frame `overlaps_area`; pre-enrage non-lethal damage clamped to 30% (`boss.gd` `ENRAGE_HP_RATIO`).
- `setup()` runs before `_ready()` — no `@onready` there; use `$node/path`.
- Zoom & window size: independent profiles; camera fixed (960, 540), zoom only; edge/spawn math via `GameState.view_world_rect()`, never hardcode 1920×1080.
- Perf: pools (`GameState.bullet_pool.fire()` / `enemy_pool.spawn()`); no per-frame `get_nodes_in_group` (use `GameState.enemies`/`player_ref`); no direct sin/cos in `_physics_process` (use `Enemy.sin_fast/cos_fast`).
- i18n: all text via `tr("KEY")`; new keys in `data/translations.csv` zh+en (re-import after); locale switch via `GameState.set_locale()`.
- Pausing UI: `process_mode = Always`; BGM: `loop_mode = LOOP_FORWARD` only (no `stop()` in `_exit_tree` — leaks instances).
