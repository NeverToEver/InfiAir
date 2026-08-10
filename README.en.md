<div align="center">

# InfiAir

**A 2D top-down space shooter · built entirely in C# (.NET 8) on Godot 4.6.2 .NET**

**English** · [中文](./README.md)

[![Godot](https://img.shields.io/badge/Godot-4.6-478cbf?logo=godot-engine&logoColor=white)](https://godotengine.org/)
[![C#](https://img.shields.io/badge/C%23-100%25-478cbf)](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/)
[![CI](https://github.com/NeverToEver/InfiAir/actions/workflows/ci.yml/badge.svg)](https://github.com/NeverToEver/InfiAir/actions/workflows/ci.yml)
[![Release](https://img.shields.io/badge/Release-v3.28-orange)](https://github.com/NeverToEver/InfiAir/releases)
[![Tests](https://img.shields.io/badge/Tests-assertion%20scenes-brightgreen)](./docs/TESTING.md)

<img src="./docs/screenshots/gameplay.png" alt="InfiAir gameplay" width="760">

</div>

## About

InfiAir is a single-player, score-driven arcade shooter: fight wave-based enemy swarms, draft 1-of-3 buffs at score milestones, and take on rotating bosses — then fly home for a mid-run refit and jump back into the same battle. **Death is the only end.** The difficulty curve grows linearly with no cap: the longer you survive and the more you kill, the deadlier the swarm.

Originally a remake of the Python/Pygame project [airwar-game](https://github.com/NeverToEver/airwar-game), it has since evolved independently. Every sprite and sound is procedurally generated — zero external asset dependencies.

Technical positioning: an **all-C# codebase** (zero GDScript, migration M1–M7d complete) with a strict separation between pure logic and engine bindings, a zero-managed-allocation-per-frame discipline on hot paths, and a three-layer test suite (xUnit unit tests + headless assertion scenes + CI gates).

## Tech Stack

| Layer | Choice | Notes |
|---|---|---|
| Engine | Godot 4.6.2 stable (.NET build) | GL Compatibility renderer (`renderer/rendering_method`); the standard engine build cannot compile this project |
| Language | C# / .NET 8 | `TreatWarningsAsErrors` gate; `Nullable` / `ImplicitUsings` / `AnalysisLevel=latest` |
| Pure logic | `csharp/core/` (class library) | Zero Godot dependency, millisecond xUnit testing |
| Engine bindings | `csharp/godot/` (main project) | Nodes / scenes / UI / cinematics; may reference core |
| Unit tests | xUnit (`tests-csharp/`) | Models / storage / password derivation / task pool / progression curves |
| Integration tests | Headless Godot assertion scenes (`test/*_test.tscn`) | Self-checked `[PASS]` / `[FAIL]` output, full regression in CI |
| CI | GitHub Actions | Tiered gates (see Testing & CI) |

## Architecture

### Layers

```
scenes/ + csharp/godot/         Godot binding layer (nodes, scenes, UI, cinematics)
        └─ GameState (the only autoload; split into 9 partial files by domain)
             ├─ 8 non-autoload services: BalanceService / SaveManager / SfxPlayer /
             │   EntityManager / FogEventManager / GameEventManager / UserDB / ProgressionInterop
             └─ delegates to csharp/core/ pure logic
csharp/core/                    Pure .NET class library (zero Godot dependency)
tests-csharp/                   xUnit unit tests (reference core, no Godot runtime)
```

- `GameState` (the only autoload) is the global state/signal bus and facade: ~250 public members (signals, state, forwards) accessed via `GameState.Instance`; split as a shell + 9 domain partials (constants/state/difficulty/missions/settings/input/users/save/meta) with zero behavioral change.
- Pure logic lives in core: data models (`BalanceModels`), config path resolution (`PathResolver`, mirroring GDScript `cfg()` semantics), the mission task pool (`TaskPool`, draw-without-replacement), progression curves (`ProgressionCurves`, bit-identical to the original GDScript), and storage (`SaveStore` atomic writes / corruption quarantine, `UserDb` local accounts + password derivation).
- Interop shells (`*Interop` + `VariantBridge`) handle Variant ↔ CLR conversion so core stays Godot-free.

### Run orchestration (main.tscn)

```text
main.tscn (run orchestration)
 ├─ Player (movement / aim assist / auto-fire / fuel / phase dash / laser weapon)
 ├─ Spawner (wave-based spawning + elite / boss / event special-slot scheduling)
 ├─ Mothership (state machine: summon → dock → piloted stay → tractor recovery → depart)
 ├─ Boss (4-type rotation + HP-phase pattern table + per-type enrage)
 ├─ EliteTurretEvent / FormationStrikeEvent (elite turret / formation strike events)
 ├─ IntroCinematic / ReturnCinematic / OrbitalStrike (cinematic & set-piece directors)
 ├─ HUD / BuffSelect / BaseConsole / Pause / Settings / GameOver / Welcome (entry)
 ├─ BackNavigator (global back/exit state machine: PC Esc, gamepad B, Android back)
 └─ GameState (autoload: score / HP / buffs / RP / missions / saves / settings / SFX pool / entity registries)
```

Game loop: auto-fire + wave spawning → 1-of-3 buff draft at score milestones → 4 rotating bosses (P1/P2/enrage; un-killed bosses flee) → mothership charge-summon / piloted stay → hold B for homecoming base refit → orbital-strike cleanup, then the same run continues. Phase transitions are maintained by a combination of Main/Spawner boolean flags and tree pausing (no single state source — a known debt, tracked in `docs/AUDIT_VAULT.md`).

### Core systems

- **Entity management**: `EntityManager` run entity registries (`Enemies` / `EnemyBullets`) + an O(1) presence index (homing-bullet hot path) + unified bind boilerplate (`BindEnemy` / `UnbindEnemy`), replacing group queries; enemy-bullet removal is swap-remove O(1).
- **Object pooling**: `BulletPool` / `EnemyPool` / `Explosion` — preallocated reuse + deferred reparent + active-state re-checks against same-frame reuse races; hot paths follow a zero-managed-allocation-per-frame discipline (trig lookup tables, `MoveCtx` reuse, preallocated vertex buffers).
- **Random event system**: `GameEventManager` orchestrates the fog group (3-second interval dice rolls) and encounter group (light per-frame timers); fog effects are injected through the `FogEventManager` facade.
- **Damage pipeline**: collision → `EntityDamage.Dispatch` type dispatch (Enemy / Boss / TurretBattery / FormationCraft) → entity `TakeDamage` (`Hp<=0` early-out, same-frame guards, grace-period re-check) → death / pool release; crit / explosive / splash / pierce / reflect buffs are multiplier stages on the Bullet side.
- **Data-driven tuning**: every tunable lives in `data/balance.json`, accessed via `GameState.Instance.Cfg()` dot-path lookup with per-key fallback to code defaults — tweak the JSON, no code changes. `docs/BALANCE_MAP.md` is generated by scanning Cfg call sites (zero-diff CI gate); difficulty / milestone progression curves are core pure functions (bit-identical to the original GDScript).
- **Persistence & security**: `SaveStore` atomic writes (tmp + rename) + corruption quarantine (`.corrupt` backup); per-user saves `user://savegame_<user>_<hash12>.json` (guests don't save; cleared on death); `UserDb` local accounts (custom PBKDF2-HMAC-SHA256 variant, fixed-vector tests) + local leaderboard; filename sanitization + sha256[:12] against path traversal.
- **UI design system**: `UITheme` shared color tokens / type scale / component factories; text is fully signal-driven (no per-frame set_text), gauges throttle at 0.1s with epsilon guards, tweens are killed-before-recreate with meta caching; bilingual (zh/en) translation keys centralized in `data/translations.csv`.

## Quick Start

**Just play**: grab a pre-built package from [GitHub Releases](https://github.com/NeverToEver/InfiAir/releases) (Windows / Linux, x86_64) — extract and run, with install/uninstall scripts included. macOS has no pre-built package yet; run from source instead.

**Run from source** (requires the .NET build of [Godot 4.6](https://godotengine.org/download) plus the .NET 8 SDK — the project is all C#; the standard engine build cannot compile it):

```bash
git clone https://github.com/NeverToEver/InfiAir.git
cd InfiAir
godot --path .
```

The local dev launcher `./run.sh` auto-detects the .NET engine (godot-mono first). Release builds go through `./release.sh` (requires export templates strictly matching engine 4.6.2).

## Controls

| Input | Action |
|-------|--------|
| WASD / Arrow keys | Move |
| Mouse | Aim (crosshair inside an aim frame → shots home in on that enemy) |
| — | Weapons fire fully automatically |
| Shift (hold) | Boost (drains fuel) |
| Ctrl (hold) | Precision movement |
| Space | Phase dash (requires buff) |
| H (hold) | Charge-summon the mothership (WASD pilots it while docked) |
| B (hold) | Homecoming — base refit |
| ESC | Pause / back one page / exit confirmation |
| Right mouse button | Back / cancel (same routing as ESC: dismiss confirm, close settings, pause toggle, exit confirm at top level) |

**Gamepad**: left stick moves, right stick aims (virtual crosshair); A dash / RB boost / LB fine-move / LT parry / X summon / Y homecoming / L3 buff panel / R3 give up / B back. Aim-stick sensitivity and stick deadzone are adjustable under Settings → Modes → Controller. PlayStation pads are auto-detected (same button positions, different labels).

<details>
<summary>Full key list (abandon / restart / rebinding)</summary>

- **K (hold 3s)**: abandon the sortie
- **R**: restart (on game-over / pause screens)
- All keys are rebindable in Settings → Controls (Esc / R are fixed; bindings persist). Language (中文 / English), view zoom, window size and aim-assist levels live in Settings → Modes; the Display section also has a "Lock Mouse in Window" toggle (on by default, keeps the cursor inside the window to prevent aim loss, auto-released when switching windows). Each setting persists independently.

</details>

## Game Loop

- **Health & score**: start with 100 HP; taking a hit grants invulnerability and clears nearby enemy bullets. Pure score-based — no item drops; death ends the run.
- **Growth**: draft 1-of-3 buffs at score milestones (19 stackable: damage / fire rate / spread / piercing / explosive / lifesteal / armor / evasion / phase dash / laser beam…); boss kills and base missions earn RP for repairs and resupply.
- **Pacing**: new enemy classes and elites unlock as your score rises; difficulty grows with no cap as kills and time add up — survive longer, score higher.
- **Getting started**: the game boots straight to the main menu; a 6-stage tutorial (movement / dash / combat / mothership / homecoming / boss enrage) awaits on first entry.

## Testing & CI

Three-layer test suite (authoritative counts and scene lists in [docs/TESTING.md](./docs/TESTING.md)):

1. **xUnit unit tests** (`tests-csharp/`, millisecond): data models / path resolution / task pool / progression curves / save atomicity / user DB & password derivation (incl. fixed vectors captured from the original GDScript).
2. **Headless Godot assertion scenes** (`test/*_test.tscn`, authoritative count & list in [docs/TESTING.md](./docs/TESTING.md)): C# scene scripts that self-check with `[PASS]` / `[FAIL]` output, covering run orchestration / combat values / boss pattern tables & enrage / event systems / save round-trips / UI flows / engine error log scanning.
3. **Tiered CI gates** (`.github/workflows/ci.yml`, restructured 2026-08-09):
   - `fast-gate` (~8 min, every push/PR): C# build (warnings-as-errors) + xUnit + dotnet format zero-diff across three projects → zero-GDScript gate → engine import warning gate → main smoke (300 frames) → compile probe of every scene;
   - `full-regression` (~40 min, main push / PR / manual only): BALANCE_MAP generator zero-diff gate → all assertion scenes (authoritative count in docs/TESTING.md; incl. flake retry and engine error log scanning);
   - pure docs (`docs/**`, `*.md`) do not trigger CI; dotnet SDK / NuGet / Godot engine are cached via actions/cache; new pushes on the same branch cancel the previous run.

Robustness baseline: sixth robustness pass (Z series, 2026-08-10) landed — type guards & overflow clamps for hand-edited saves/config, division-by-zero lower-bound clamps, coroutine-hang and signal-pairing defenses, ~20 fixes; same-day seventh pass (AA series) — Roslynator static analysis + full logic review, 23 logic fixes (milestone-curve convergence hang, missing BuffsChanged for meta loadout, tutorial stage soft-lock, duplicate-key JSON breaking save quarantine, etc.) + 36 normalization fixes (records in [docs/AUDIT_VAULT.md](./docs/AUDIT_VAULT.md)).

Minimal local verification set:

```bash
dotnet build                                 # C# build (CI zero-warning gate)
dotnet test tests-csharp/                    # xUnit unit tests
godot --headless --import --path .           # assets & script parsing
godot --headless --path . --quit-after 300   # runtime smoke
godot --headless --path . res://test/smoke_test.tscn  # main-flow smoke (self-checked assertions)
```

## Project Structure

```text
csharp/core/        Pure .NET class library (zero Godot dependency): models / curves / storage / task pool / config resolution
csharp/godot/       Engine binding layer: GameState (shell + 9 domain partials) + 8 services + scene scripts + entities / events / UI
tests-csharp/       xUnit unit tests
scenes/             Scene files (welcome entry / main run / boss / mothership / cinematics)
test/               Headless assertion scenes (*_test.tscn, authoritative count in docs/TESTING.md) + windowed capture tools
data/               balance.json (numeric config) + translations.csv (zh/en bilingual)
scripts/tools/      Offline dev tools (gen_balance_map.py etc., not runtime dependencies)
docs/               Architecture / design / audit docs (ARCHITECTURE / TESTING / AUDIT_VAULT etc.)
```

## Documentation

| Doc | Contents |
|-----|----------|
| [AGENTS.md](./AGENTS.md) | Contributor conventions: tech stack / verification / architecture / code style / test strategy / CI gates |
| [CONTRIBUTING.md](./CONTRIBUTING.md) | Contribution guide: setup / workflow / PR checklist |
| [CHANGELOG.md](./CHANGELOG.md) | Version history |
| [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) | Architecture overview: directory roles / per-script duties / service delegation |
| [docs/TESTING.md](./docs/TESTING.md) | Test strategy: authoritative scene counts / assertion lists / known failures / CI flow |
| [docs/DESIGN_BASELINE.md](./docs/DESIGN_BASELINE.md) | Design baseline: gameplay rules / architecture stance (changes go through this doc) |
| [docs/BALANCE_MAP.md](./docs/BALANCE_MAP.md) | Numeric config index (generated, do not hand-edit) |
| [docs/AUDIT_VAULT.md](./docs/AUDIT_VAULT.md) | Code audit archive (U–AA series, proprietary — never delete) |
| [docs/ROADMAP.md](./docs/ROADMAP.md) | Roadmap and future direction (single source of truth) |

## License & Acknowledgments

**License**: the game code and procedurally generated assets are released under the [MIT License](./LICENSE); the bundled font [Noto Sans SC](https://fonts.google.com/noto/specimen/Noto+Sans+SC) is licensed under the [SIL Open Font License 1.1](https://openfontlicense.org/) (third-party notices in [NOTICE](./NOTICE)).

**Acknowledgments**: [airwar-game](https://github.com/NeverToEver/airwar-game) (original prototype) · [Godot-GameTemplate](https://github.com/nezvers/Godot-GameTemplate) · [top-down-shooter-core](https://github.com/quiver-dev/top-down-shooter-core) · [SimpleTopDownShooterTemplate2D](https://github.com/Unchained112/SimpleTopDownShooterTemplate2D) · [Godot-Menus-Template](https://github.com/Maaack/Godot-Menus-Template) · [Godot Engine](https://godotengine.org/) · [Noto Sans SC](https://fonts.google.com/noto/specimen/Noto+Sans+SC) (SIL OFL)

---

Maintained as a hobby project — feedback welcome · Made with Godot 4
