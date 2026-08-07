# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**Authoritative conventions: `AGENTS.md` + `.agents/*`** (collision layers, testing, perf, i18n, tuning, GDScript style) — read before changes. This file: entry-level overview only. Direction/plans: `docs/ROADMAP.md`. **`docs/AUDIT_VAULT.md` is a proprietary audit archive — never remove**; consult before core-logic changes.

## Project

InfiAir: 2D top-down shooter, Godot 4.6.2 + GDScript (gl_compatibility, no external plugins) with progressive C# interop — Godot .NET build (`godot-mono`); 2026-08-07: new modules / pure logic / data models in C#, existing GDScript not migrated. Remade from `airwar-game` (Python/Pygame), now independently evolved (`docs/archive/PORTING_PARITY.md`). Vertical-scroll starfield, waves, milestone buff 3-choice, rotating bosses + enrage, mothership resupply, return-to-base restock. Score-only.

- Entry scene `scenes/welcome.tscn` (accounts/login); battle scene `scenes/main.tscn`; 1920×1080 (stretch `canvas_items`/`keep`).
- Only autoload `GameState` (`autoload/game_state.gd`): global state/signal bus, sfx pool, `GameState.cfg()` config access, persistence.
- CI via GitHub Actions. Godot 4.6.2 .NET build (`godot-mono`) — required for the C# project; .NET 8 SDK: `dotnet build` (zero warnings) + `dotnet test tests-csharp/` (xUnit). C#↔GDScript cannot inherit each other; hot paths (pools/bullets) and scene-binding layers stay single-language. Prefer `./run.sh` — auto-detects engine (PATH `godot-mono` → `~/.local/bin/godot-mono` → PATH `godot`/`godot4` → `~/.local/bin/godot` → `/Applications/Godot.app`), warns below 4.6, passes args through.

## Commands

```bash
G=godot-mono   # .NET build — required for C# (fallback: ~/.local/bin/godot-mono or PATH godot)
$G --path .                                    # run locally
$G --headless --import --path .                # import + script parse check
$G --headless --path . --quit-after 300        # 300-frame runtime check
# Headless tests: test/*.tscn self-check via [PASS]/[FAIL] + exit code.
# 48 assertion scenes (57 total); full list & known baseline: docs/TESTING.md
dotnet build                                    # C# compile (TreatWarningsAsErrors: zero warnings)
dotnet test tests-csharp/                       # xUnit pure-logic unit tests
$G --headless --path . res://test/smoke_test.tscn           # main flow — always after changes
$G --headless --path . res://test/base_system_test.tscn     # saves/base/mothership changes
$G --headless --path . res://test/autoplay_test.tscn [-- --autoplay-seconds=480] [-- --seed=N]
$G --headless --fixed-fps 1000 --path . res://test/perf_bench.tscn  # perf (needs --fixed-fps)
# All 48 assertion scenes, one-liner (same selection as CI: test/*_test.tscn minus autoplay probe):
for t in test/*_test.tscn; do
  case "$t" in *autoplay_test.tscn) continue;; esac
  $G --headless --path . "res://$t" || break
done
```

Minimum after changes: `--import`, `--quit-after 300`, `smoke_test.tscn`; add `base_system_test.tscn` when touching saves/base/mothership; add `dotnet build` + `dotnet test tests-csharp/` when touching C#. Screenshots need windowed mode (headless captures nothing): `test/visual_capture.tscn` (game → /tmp/infiair_capture.png), `test/ui_capture.tscn` (UI → /tmp/ui_*.png).

```bash
# Pre-commit gate (6 layers; CI runs all): format + static + C# first, then the above engine checks
python3 -m venv .venv && .venv/bin/pip install gdtoolkit==4.5.0   # one-time; .venv/ gitignored
.venv/bin/gdformat --check autoload/ scripts/ test/        # layer 1: format (w=140)
.venv/bin/gdlint autoload/ scripts/ test/                  # layer 2: style/unused
```
