<div align="center">

# 🛩️ InfiAir

**A 2D top-down space shooter built with Godot 4 + GDScript**

**English** · [中文](./README.md)

[![Godot](https://img.shields.io/badge/Godot-4.6-478cbf?logo=godot-engine&logoColor=white)](https://godotengine.org/)
[![GDScript](https://img.shields.io/badge/GDScript-100%25-478cbf)](https://docs.godotengine.org/en/stable/tutorials/scripting/gdscript/)
[![Release](https://img.shields.io/badge/Release-v3.22-orange)](https://github.com/NeverToEver/InfiAir/releases)
[![Tests](https://img.shields.io/badge/Tests-1018%20passed-brightgreen)](#-testing)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey)](#-installation)

<img src="./docs/screenshots/gameplay.png" alt="InfiAir gameplay" width="760">

[📦 Installation](#-installation) · [🚀 Run from Source](#-run-from-source) · [🎮 Controls](#-controls) · [📚 Docs](#-documentation) · [🗺️ Roadmap](#️-roadmap)

</div>

## About

InfiAir is a single-player, score-driven arcade shooter: fight wave-based enemy swarms, draft 1-of-3 buffs at score milestones, and take on rotating bosses — then fly home for a mid-run refit and jump back into the same battle. Death is the only end. The difficulty curve grows linearly with no cap: the longer you survive and the more you kill, the deadlier the swarm.

Originally a remake of the Python/Pygame project [airwar-game](https://github.com/NeverToEver/airwar-game), it has since evolved independently. Every sprite and sound is procedurally generated — zero external asset dependencies.

## ✨ Features

**Gameplay**

- 🔄 **A complete sortie loop**: fight waves → draft milestone buffs → rotating bosses → base refit → sortie again
- 🃏 **16 stackable buffs**: damage, fire rate, spread, piercing, explosive, lifesteal, armor, evasion, phase dash, laser beam and more
- 👾 **3 rotating bosses**: driven by an HP-phase pattern table (P1 / P2 / enrage + telegraph wind-ups), each with its own enrage sequence; fail to kill in time and the boss flees
- 🛰️ **Mothership weapons platform**: charge-up summon (warp-gate entrance) → auto-docking → magazine-based stay (WASD piloting + twin-turret sweep + missile volleys) → tractor-beam recovery
- 💥 **Two random events**: the elite turret strike (30s turret-clearing timer, boss-mutex) and the formation bombing run (fused bombs with shrinking warning rings, abortable by homecoming)
- 🏠 **Mid-run base refit**: homecoming does NOT end the run — hangar, weapon hardpoints (mutually exclusive talent routes), repair & resupply (RP economy), and mission planning, then back to the same battle

**Audiovisuals & Presentation**

- 🎬 **Two cinematic directors**: a 6-shot launch intro and a 7-shot homecoming return (warp charge / jump portal / phantom-station docking / landing-pad touchdown), skippable anytime
- ❤️ **Meta HUD health feedback**: hit chromatic aberration, directional ripples, low-HP crack growth, vignette heartbeat — a fullscreen post-process with a "reduce flashes" accessibility toggle
- 🎯 **Arcade-grade readability**: follow-the-cursor crosshair, aim frames on ~40% of enemies with in-frame homing shots (Low / Medium / High), and a pulsing dot on the actual hitbox — never lose your ship in bullet hell
- 🎛️ **Holographic UI design system**: unified color tokens, type scale, chamfered panels and staggered fade-ins — every screen shares one skeleton
- 🎨 **Fully procedural assets**: 14 crystalline-prism unit sprites plus all SFX/BGM synthesized by scripts, regenerable offline

## 🖼️ Screenshots

| Main menu | Gameplay | Mothership dock | Base refit |
|-----------|----------|-----------------|------------|
| ![Main menu](./docs/screenshots/start.png) | ![Gameplay](./docs/screenshots/gameplay.png) | ![Mothership](./docs/screenshots/mothership.png) | ![Base](./docs/screenshots/base.png) |

## 📦 Installation

Grab a pre-built package from [GitHub Releases](https://github.com/NeverToEver/InfiAir/releases) (x86_64, single-file executable with embedded pck, install/uninstall scripts included):

- **Windows**: extract the zip and run `InfiAir.exe` directly; or run `install.bat` to install to `%LOCALAPPDATA%\InfiAir` with a Start Menu shortcut (`uninstall.bat /purge` also removes save data)
- **Linux**: extract the tarball and run `InfiAir.x86_64` directly; or run `./install.sh` for a per-user install (`~/.local` + desktop menu entry, `./uninstall.sh --purge` also removes save data)
- **macOS**: no pre-built package yet — please [run from source](#-run-from-source)

Uninstall keeps your saves by default (`user://savegame.json` run progress, `user://profile.json` high scores and settings).

## 🚀 Run from Source

Requires [Godot 4.6](https://godotengine.org/download) (standard build — no .NET needed):

```bash
git clone https://github.com/NeverToEver/InfiAir.git
cd InfiAir
godot --path .     # run directly, or open the project in the editor and press F5
```

## 🎮 Controls

| Input | Action |
|-------|--------|
| WASD / Arrow keys | Move |
| Mouse | Aim (follow-the-cursor crosshair; ~40% of enemies carry a cyan aim frame — place the crosshair inside to make shots home in, Low / Medium / High adjustable) |
| — | Weapons fire fully automatically |
| Shift (hold) | Boost (×1.8 speed, drains fuel) |
| Ctrl (hold) | Precision movement (speed ×0.35) |
| Space | Phase dash (requires buff; invulnerable, costs 25% fuel) |
| H (hold 3s) | Charge-summon the mothership (WASD pilots the ship while docked; hold H 2s for early undock, with progress bar) |
| B (hold 1.5s) | Homecoming — mid-run base refit |
| K (hold 3s) | Abandon the sortie |
| ESC | Global back: pause in combat (save your run) / back one page / exit confirmation at top level |
| R | Restart (on game-over / pause screens) |

> All keys are rebindable in Settings → Controls (Esc / R are fixed; bindings persist). Language (中文 / English), three view-zoom levels, three window sizes and three aim-assist levels all live in Settings → Modes, each persisted independently.

## 🧭 Game Loop

- Start with 100 HP: taking a hit grants 1.5s of invulnerability and clears enemy bullets within 250px; HP slowly regenerates out of combat (base repairs and mothership resupply restore it fully); **pure score-based — no item drops**.
- 4 enemy classes × 8 movement patterns unlocked progressively by score, 3 elite variants, bullet types single / spread / laser; wave-based spawning (grouped entries, anchor-hovering with staggered phases), one elite wave every 3–4 normal waves.
- Milestones (starting at 3000, scaling ×1.35 per cycle) pause the game for a 1-of-3 buff draft; a boss spawns every 1500 points or 80s, granting +500 points.
- The difficulty multiplier grows linearly with no cap: `1 + 0.5 × boss kills + 1 per 10 minutes` (quantized in 30s steps), ramping enemy HP / damage — the run is eventually lethal, so survive longer to score higher.
- RP (requisition points) come from boss kills (+5) and base missions (+3), spent on base repairs and fuel (2 RP each).
- Save anytime via the pause menu (homecoming also auto-saves); continue from the title panel on next launch. Death deletes the save.
- A welcome screen greets the first launch; the start panel includes a 6-stage tutorial (movement & aim / boost & dash / combat / mothership docking / homecoming & base / boss enrage).

## 🏗️ Architecture

```text
main.tscn (run orchestration)
 ├─ Player (movement / aim assist / auto-fire / fuel / phase dash / laser weapon)
 ├─ Spawner (wave-based spawning + elite / boss / event special-slot scheduling)
 ├─ Mothership (state machine: summon → dock → piloted stay → tractor recovery → depart)
 ├─ Boss (3-type rotation + HP-phase pattern table + per-type enrage)
 ├─ EliteTurretEvent / FormationStrikeEvent (elite turret / formation strike events)
 ├─ IntroCinematic / ReturnCinematic / OrbitalStrike (cinematic & set-piece directors)
 ├─ HUD / BuffSelect / BaseConsole / Pause / Settings / GameOver / StartPanel
 ├─ BackNavigator (global back/exit state machine: PC Esc, gamepad B, Android back)
 └─ GameState (autoload: score / HP / buffs / RP / missions / saves / settings / SFX pool / entity registries)
```

- **Data-driven tuning**: every tunable lives in `data/balance.json`, accessed via `GameState.cfg()` with per-key fallback to script defaults — tweak the JSON, no code changes needed.
- **UI design system**: `scripts/ui_theme.gd` provides color tokens, a type scale and component factories shared by every screen.
- **Performance**: bullet / enemy / explosion object pooling, registries instead of group queries, trig lookup tables, throttled HUD; the `perf_bench` scene measures raw frame time.
- **Collision layers**: `1=player 2=player_bullet 3=enemy 4=enemy_bullet`; bullets resolve damage on their side; hits only count on the r=7 hitbox point.
- **Persistence**: `user://savegame.json` and `user://profile.json` are versioned; corrupted files are quarantined automatically.

## 📚 Documentation

| Doc | Contents |
|-----|----------|
| [AGENTS.md](./AGENTS.md) | Contributor conventions: tech stack / verification / architecture / code style / test strategy |
| [docs/ROADMAP.md](./docs/ROADMAP.md) | Roadmap and future direction (single source of truth) |
| [docs/EXIT_FLOW.md](./docs/EXIT_FLOW.md) | Back / exit flow |
| [docs/BOSS_REDESIGN.md](./docs/BOSS_REDESIGN.md) | Boss phase pattern tables and enrage design |
| [docs/META_HUD_DESIGN.md](./docs/META_HUD_DESIGN.md) | Meta HUD health feedback design |
| [docs/ELITE_TURRET_EVENT.md](./docs/ELITE_TURRET_EVENT.md) · [docs/FORMATION_STRIKE_EVENT.md](./docs/FORMATION_STRIKE_EVENT.md) | Random event design |
| [docs/INTRO_CINEMATIC.md](./docs/INTRO_CINEMATIC.md) · [docs/RETURN_HOME_CINEMATIC.md](./docs/RETURN_HOME_CINEMATIC.md) | Intro / return cinematic design |
| [docs/ENDLESS_BALANCE_PLAN.md](./docs/ENDLESS_BALANCE_PLAN.md) | Endless-mode difficulty curve plan |

## ✅ Testing

Tests are headless scene scripts (no framework) that self-check with `[PASS]` / `[FAIL]` output: **29 assertion scenes, 1018 assertions, all passing**. Minimal verification set:

```bash
godot --headless --import --path .          # assets & script parsing
godot --headless --path . --quit-after 300  # runtime smoke
godot --headless --path . res://test/smoke_test.tscn  # main-flow smoke (128 assertions)
```

The full 29-scene list, the `perf_bench` performance benchmark, the autoplay simulated-play probe and the windowed capture tools are documented in [AGENTS.md](./AGENTS.md).

## 🗺️ Roadmap

- ✅ **Done**: core run loop / 16-buff drafting / bosses & dual random events / mothership docking & base refit / dual cinematics & holographic UI / Meta HUD health feedback / endless difficulty curve / dual-platform release packaging (distributed via GitHub Releases since v3.22)
- 🔭 **Next**: content evolution (new buffs / new enemy & boss types / mobile controls) is deferred and needs re-proposal to restart; CI and semantic versioning are planned
- Iteration history lives in the git log; porting-era archives are frozen in [docs/archive/PORTING_PARITY.md](./docs/archive/PORTING_PARITY.md)

See [docs/ROADMAP.md](./docs/ROADMAP.md) for details.

## 🤝 Contributing

Issues and PRs are welcome! Before submitting, please make sure:

1. All headless assertion scenes pass;
2. You follow the conventions in [AGENTS.md](./AGENTS.md) (collision layers, UI design system, code style, test strategy);
3. Direction-level decisions (new content, defer / restart) are recorded in [docs/ROADMAP.md](./docs/ROADMAP.md) first.

## 🙏 Acknowledgments

- Original prototype: [airwar-game](https://github.com/NeverToEver/airwar-game) (Python / Pygame)
- Reference projects: [nezvers/Godot-GameTemplate](https://github.com/nezvers/Godot-GameTemplate), [quiver-dev/top-down-shooter-core](https://github.com/quiver-dev/top-down-shooter-core), [Unchained112/SimpleTopDownShooterTemplate2D](https://github.com/Unchained112/SimpleTopDownShooterTemplate2D), [Maaack/Godot-Menus-Template](https://github.com/Maaack/Godot-Menus-Template)
- Engine: [Godot Engine](https://godotengine.org/); font: [Noto Sans SC](https://fonts.google.com/noto/specimen/Noto+Sans+SC) (SIL OFL)

## 📄 License

This repository is currently private and has not chosen an open-source license yet; please contact the author before using or redistributing.

---

<div align="center">

Maintained as a hobby project — feedback welcome · Made with Godot 4

</div>
