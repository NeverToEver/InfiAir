<div align="center">

# ✈️ InfiAir

**2D top-down bullet-hell arcade shooter · built entirely in C# (.NET 8) on Godot 4.6.2 .NET**

**English** · [中文](./README.md)

[![Godot](https://img.shields.io/badge/Godot-4.6.2-478cbf?logo=godotengine&logoColor=white)](https://godotengine.org/)
[![C#](https://img.shields.io/badge/C%23-100%25-512BD4?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![Release](https://img.shields.io/github/v/release/NeverToEver/InfiAir?color=orange&label=Release)](https://github.com/NeverToEver/InfiAir/releases)
[![CI](https://github.com/NeverToEver/InfiAir/actions/workflows/ci.yml/badge.svg)](https://github.com/NeverToEver/InfiAir/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](./CONTRIBUTING.md)
[![Discussions](https://img.shields.io/badge/Discussions-Join-8250df?logo=github&logoColor=white)](https://github.com/NeverToEver/InfiAir/discussions)

<p align="center">
  <a href="#-about">About</a> ·
  <a href="#-features">Features</a> ·
  <a href="#-screenshots">Screenshots</a> ·
  <a href="#-quick-start">Quick Start</a> ·
  <a href="#-controls">Controls</a> ·
  <a href="#-tech-stack--architecture">Tech Stack & Architecture</a> ·
  <a href="#-testing--ci">Testing & CI</a> ·
  <a href="#-community--contributing">Community & Contributing</a>
</p>

<img src="./docs/screenshots/gameplay.png" alt="InfiAir gameplay" width="760">

</div>

## 🌟 About

InfiAir is a **single-player, score-driven arcade shooter**: fight wave-based enemy swarms, draft 1-of-3 buffs at score milestones, and take on rotating bosses — then fly home for a mid-run refit and jump back into the same battle. **Death is the only end.** The difficulty curve grows linearly with no cap: the longer you survive and the more you kill, the deadlier the swarm.

Originally a remake of the Python/Pygame project [airwar-game](https://github.com/NeverToEver/airwar-game), it has since evolved independently. **Every sprite and sound is procedurally generated** — zero external asset dependencies. The codebase has been fully migrated to C# (zero GDScript), with a strict separation between pure logic and engine bindings, and a zero-managed-allocation-per-frame discipline on hot paths.

## ✨ Features

- 🔥 **Endless waves + uncapped difficulty** — new enemy classes and elites unlock as your score rises; pressure keeps increasing with kills and time.
- 🃏 **1-of-3 buff drafting** — pick one of three build-defining buffs at score milestones; 19 stackable buffs (damage / fire rate / spread / piercing / explosive / lifesteal / armor / evasion / phase dash / laser beam…).
- 👾 **Rotating bosses & random events** — 4 boss types with HP-phase pattern tables and per-type enrage; fog, encounters, elite turrets, formation strikes, and more.
- 🚀 **Mothership & homecoming refit** — charge-summon the mothership, pilot it while docked, call an orbital strike, then fly home to repair and resupply mid-run.
- 💥 **Combo scoring & defensive pity** — a 3-second combo window rewards aggressive play up to ×2.0; low HP boosts defensive buff odds and guarantees at least one defensive pick.
- 📈 **Meta progression** — spend TechPoints earned at death on research that grants starting buff loadouts (bounded growth that preserves the intended difficulty curve).
- 🔒 **Local accounts & safe saves** — per-user saves, PBKDF2 password derivation, atomic writes, corruption quarantine, and a local leaderboard.
- 🧩 **All-C# codebase** — Godot 4.6.2 .NET + .NET 8; the pure-logic core has zero Godot dependencies.
- ✅ **Three-layer testing & CI gates** — xUnit unit tests + headless assertion scenes + tiered GitHub Actions regression.

## 📸 Screenshots

| Main Menu | Gameplay |
| --- | --- |
| ![Main Menu](./docs/screenshots/start.png) | ![Gameplay](./docs/screenshots/gameplay.png) |

| Base Refit | Mothership |
| --- | --- |
| ![Base](./docs/screenshots/base.png) | ![Mothership](./docs/screenshots/mothership.png) |

## 🚀 Quick Start

### Just play

Grab the latest pre-built package from [GitHub Releases](https://github.com/NeverToEver/InfiAir/releases) (Windows / Linux x86_64) — extract and run, with install/uninstall scripts included. macOS has no pre-built package yet; run from source instead.

> Latest release: **v3.32** (2026-08-16).

### Run from source

You need the **.NET build of [Godot 4.6](https://godotengine.org/download)** (the standard engine build cannot compile this project) and the **.NET 8 SDK**:

```bash
git clone https://github.com/NeverToEver/InfiAir.git
cd InfiAir
./run.sh        # auto-detects godot-mono / godot; you can also use: godot --path .
```

Release builds go through `./release.sh` (requires export templates strictly matching engine 4.6.2 mono).

## 🎮 Controls

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
| ESC / Right mouse button | Pause / back one page / exit confirmation |

**Gamepad**: left stick moves, right stick aims (virtual crosshair); A dash / RB boost / LB fine-move / LT parry / X summon / Y homecoming / L3 buff panel / R3 give up / B back. PlayStation pads are auto-detected. Full key list and rebinding are available in-game under Settings → Controls.

## 🧱 Tech Stack & Architecture

| Layer | Choice | Notes |
|---|---|---|
| Engine | Godot 4.6.2 stable (.NET build) | GL Compatibility renderer; requires the .NET engine build |
| Language | C# / .NET 8 | `TreatWarningsAsErrors` / `Nullable` / `AnalysisLevel=latest` |
| Pure logic | `csharp/core/` | Zero Godot dependency, millisecond xUnit testing |
| Engine bindings | `csharp/godot/` | Nodes / scenes / UI / cinematics; may reference core |
| Unit tests | xUnit (`tests-csharp/`) | Models / storage / password derivation / task pool / progression curves |
| Integration tests | Headless Godot assertion scenes (`test/*_test.tscn`) | Self-checked `[PASS]` / `[FAIL]`, full regression in CI |
| CI | GitHub Actions | Tiered gates (see Testing & CI) |

**Layers at a glance**

```text
scenes/ + csharp/godot/         Godot binding layer (nodes, scenes, UI, cinematics)
        └─ GameState (the only autoload, orchestration facade)
             ├─ 8 domain services: Meta / Missions / Score / RunProgression / Combat / Settings / InputBindings / UserSession
             ├─ 8 base services: BalanceService / SaveManager / SfxPlayer /
             │   EntityManager / FogEventManager / GameEventManager / UserDB / ProgressionInterop
             └─ delegates to csharp/core/ pure logic
csharp/core/                    Pure .NET class library (zero Godot dependency)
tests-csharp/                   xUnit unit tests (reference core, no Godot runtime)
```

> For deep architecture details (GameState split, entity management, object pooling, damage pipeline, persistence & security, UI design system) see [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md).

## 🧪 Testing & CI

Three-layer test suite (authoritative counts and scene lists in [docs/TESTING.md](./docs/TESTING.md)):

1. **xUnit unit tests** (`tests-csharp/`, millisecond): data models / path resolution / task pool / progression curves / save atomicity / user DB & password derivation.
2. **Headless Godot assertion scenes** (`test/*_test.tscn`): run orchestration / combat values / boss pattern tables & enrage / event systems / save round-trips / UI flows / engine error log scanning.
3. **Tiered CI gates** (`.github/workflows/ci.yml`):
   - `fast-gate` (~8 min): C# build (warnings-as-errors) + xUnit + dotnet format zero-diff → zero-GDScript gate → engine import warning gate → main smoke → compile probe for every scene;
   - `full-regression` (~40 min): BALANCE_MAP generator zero-diff → all assertion scenes → engine error log scanning;
   - pure docs changes do not trigger CI; dependencies are cached via actions/cache; new pushes on the same branch cancel older runs.

Minimal local verification set:

```bash
dotnet build                                 # C# build (CI zero-warning gate)
dotnet test tests-csharp/                    # xUnit unit tests
godot --headless --import --path .           # assets & script parsing
godot --headless --path . --quit-after 300   # runtime smoke
godot --headless --path . res://test/smoke_test.tscn  # main-flow smoke (self-checked)
```

## 📁 Project Structure

```text
csharp/core/        Pure .NET class library (zero Godot dependency): models / curves / storage / task pool / config resolution
csharp/godot/       Engine binding layer: GameState + 8 domain services + 8 base services + scene scripts + entities / events / UI
tests-csharp/       xUnit unit tests
scenes/             Scene files (welcome entry / main run / boss / mothership / cinematics)
test/               Headless assertion scenes (*_test.tscn) + capture tools
data/               balance.json (numeric config) + translations.csv (zh/en bilingual)
scripts/tools/      Offline dev tools (gen_balance_map.py etc., not runtime dependencies)
docs/               Architecture / design / audit docs
```

## 🧑‍🤝‍🧑 Community & Contributing

- 🐛 **Report bugs / request features**: open an [Issue](https://github.com/NeverToEver/InfiAir/issues) using the `bug` or `enhancement` templates.
- 💬 **Discuss**: join [GitHub Discussions](https://github.com/NeverToEver/InfiAir/discussions) for gameplay, roadmap, and dev talk.
- 🤝 **Contribute code**: read [CONTRIBUTING.md](./CONTRIBUTING.md) and [AGENTS.md](./AGENTS.md) first, and run the minimal verification set before opening a PR.
- 🛡️ **Security disclosures**: report privately via the process in [SECURITY.md](./SECURITY.md).
- 🗺️ **Roadmap**: direction and plans live in [docs/ROADMAP.md](./docs/ROADMAP.md).

## 📚 Documentation

| Doc | Contents |
|-----|----------|
| [AGENTS.md](./AGENTS.md) | Contributor conventions: tech stack / verification / architecture / code style / test strategy / CI gates |
| [CONTRIBUTING.md](./CONTRIBUTING.md) | Contribution guide: setup / workflow / PR checklist |
| [CHANGELOG.md](./CHANGELOG.md) | Version history |
| [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) | Architecture overview: directory roles / per-script duties / service delegation |
| [docs/TESTING.md](./docs/TESTING.md) | Test strategy: authoritative scene counts / assertion lists / CI flow |
| [docs/DESIGN_BASELINE.md](./docs/DESIGN_BASELINE.md) | Design baseline: gameplay rules / architecture stance |
| [docs/BALANCE_MAP.md](./docs/BALANCE_MAP.md) | Numeric config index (generated, do not hand-edit) |
| [docs/AUDIT_VAULT.md](./docs/AUDIT_VAULT.md) | Code audit archive (proprietary — never delete) |
| [docs/ROADMAP.md](./docs/ROADMAP.md) | Roadmap and future direction (single source of truth) |

## 📄 License & Acknowledgments

**License**: the game code and procedurally generated assets are released under the [MIT License](./LICENSE); the bundled font [Noto Sans SC](https://fonts.google.com/noto/specimen/Noto+Sans+SC) is licensed under the [SIL Open Font License 1.1](https://openfontlicense.org/) (third-party notices in [NOTICE](./NOTICE)).

**Acknowledgments**: [airwar-game](https://github.com/NeverToEver/airwar-game) (original prototype) · [Godot-GameTemplate](https://github.com/nezvers/Godot-GameTemplate) · [top-down-shooter-core](https://github.com/quiver-dev/top-down-shooter-core) · [SimpleTopDownShooterTemplate2D](https://github.com/Unchained112/SimpleTopDownShooterTemplate2D) · [Godot-Menus-Template](https://github.com/Maaack/Godot-Menus-Template) · [Godot Engine](https://godotengine.org/) · [Noto Sans SC](https://fonts.google.com/noto/specimen/Noto+Sans+SC) (SIL OFL)

---

Made with Godot 4 · Maintained as a hobby project — Star / Issue / PR welcome!
