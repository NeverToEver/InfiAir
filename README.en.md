<div align="center">

# 🛩️ InfiAir

**A 2D top-down space shooter built with Godot 4 + GDScript — a remake of the Python original [airwar-game](https://github.com/NeverToEver/airwar-game)**

**English** · [中文](./README.md)

[![Godot](https://img.shields.io/badge/godot-4.6-478cbf?logo=godot-engine&logoColor=white)](https://godotengine.org/)
[![GDScript](https://img.shields.io/badge/GDScript-100%25-478cbf)](https://docs.godotengine.org/en/stable/tutorials/scripting/gdscript/)
[![Tests](https://img.shields.io/badge/tests-267%20passed-brightgreen)](#verification)
[![Platform](https://img.shields.io/badge/platform-macOS%20%7C%20Windows%20%7C%20Linux-lightgrey)](#getting-started)

<img src="./docs/screenshots/gameplay.png" alt="InfiAir gameplay" width="760">

</div>

---

## Contents

- [✨ Highlights](#-highlights)
- [🖼️ Screenshots](#️-screenshots)
- [🎮 Controls](#-controls)
- [🚀 Getting Started](#-getting-started)
- [🧭 Game Loop](#-game-loop)
- [🏗️ Architecture](#️-architecture)
- [✅ Verification](#-verification)
- [🗺️ Roadmap](#️-roadmap)
- [🤝 Contributing](#-contributing)
- [📄 License](#-license)

## ✨ Highlights

- **A complete sortie loop**: fight waves → pick 1-of-3 buffs at score milestones → rotating bosses → return to base for a mid-run refit → sortie again. Death is the only end.
- **16 stackable buffs**: damage, fire rate, spread, piercing, explosive, lifesteal, armor, evasion, phase dash, slow field, laser beam and more — drafted at score milestones.
- **3 rotating bosses + enrage**: heavy / skirmisher / mothership archetypes; they enrage below 30% HP, and flee the battle if you can't kill them within 50 seconds.
- **Mothership docking**: charge-up summon, tractor-beam docking, a 20-second stay with magazine-fed sweep fire, early undock for cooldown refund — a tactical trade-off between resupply and fire support.
- **Mid-run base refit**: homecoming does NOT end the run. Four base modules — hangar, weapon hardpoints (mutually exclusive talent routes), repair & resupply (RP economy), and mission planning — then you return to the same battle.
- **Fully procedural assets**: all ship sprites are procedurally generated (inherited from the Python original); SFX and BGM are synthesized by `scripts/tools/generate_audio.py`. Zero external asset dependencies.

## 🖼️ Screenshots

| Gameplay | Mothership docking | Base refit |
|----------|--------------------|------------|
| ![Gameplay](./docs/screenshots/gameplay.png) | ![Mothership](./docs/screenshots/mothership.png) | ![Base](./docs/screenshots/base.png) |

## 🎮 Controls

| Input | Action |
|-------|--------|
| WASD / Arrow keys | Move |
| Mouse | Aim (auto aim-assist locks targets within 230px; flick to break lock) |
| — | Weapons fire fully automatically |
| Shift (hold) | Boost (~1.8x speed, drains fuel) |
| Ctrl (hold) | Precision movement (speed ×0.35) |
| Space | Phase dash (requires buff; invulnerable, costs 25% fuel) |
| H (hold 3s) | Charge-summon the mothership (hold H 2s during stay for early undock) |
| B (hold 1.5s) | Homecoming — mid-run base refit |
| K (hold 3s) | Abandon the sortie |
| ESC | Pause (the pause menu holds the only save entry) |
| R | Restart (on game-over / pause screens) |

## 🚀 Getting Started

Requires [Godot 4.6](https://godotengine.org/download) (standard build — no .NET needed).

```bash
git clone https://github.com/NeverToEver/InfiAir.git
cd InfiAir
godot --path .          # run directly, or open the project in the editor and press F5
```

## 🧭 Game Loop

- 3 lives, 1.5s invulnerability on hit; **pure score-based — no item drops**.
- 4 enemy classes × 8 movement patterns, unlocked progressively by score; 3 elite variants; enemy bullets come in single / spread / laser types.
- Every 500 points pauses the game for a 1-of-3 buff draft; bosses spawn every 1500 points or 90s, granting +500 points and raising the difficulty multiplier (`1 + (2^min(kills,10) − 1) × 0.25`, capped at 8x).
- RP (requisition points) come from boss kills (+5) and base missions (+3), spent on repairs and fuel at the base.
- Save anytime via the pause menu; continue from the title panel on next launch. Death deletes the save.

## 🏗️ Architecture

```text
main.tscn (run orchestration)
 ├─ Player (movement / aim assist / auto-fire / fuel / phase dash / laser weapon)
 ├─ Spawner (7 enemy-class config tables + score-gated unlocks + boss rotation)
 ├─ Mothership (7-state machine: descend → hover → dock → stay → release → depart)
 ├─ HUD / BuffSelect / BaseConsole / GameOver / Pause / StartPanel
 └─ GameState (autoload: score / buffs / RP / missions / routes / saves / SFX pool / shake)
```

- Collision layers: `1=player 2=player_bullet 3=enemy 4=enemy_bullet`; bullets resolve damage on their side.
- Run save `user://savegame.json` and profile `user://profile.json` are versioned.
- Tests are headless scene scripts (no framework) — see `AGENTS.md`.

## ✅ Verification

```bash
godot --headless --import --path .          # assets & script parsing
godot --headless --path . --quit-after 300  # runtime smoke
godot --headless --path . res://test/smoke_test.tscn        # main flow — 82 assertions
godot --headless --path . res://test/base_system_test.tscn  # save/RP/missions/routes — 46
godot --headless --path . res://test/enemy_combat_test.tscn # enemies & bosses — 31
godot --headless --path . res://test/buff33_test.tscn       # buffs/mothership/give-up — 29
godot --headless --path . res://test/difficulty_test.tscn   # difficulty/milestones/settings — 52
godot --headless --path . res://test/boss_enrage_test.tscn  # boss enrage — 24
```

267 assertions, all passing.

## 🗺️ Roadmap

- [x] Core run loop (waves / milestone buffs / bosses / game over)
- [x] Game feel (shake / particles / synthesized SFX & BGM / telegraphs)
- [x] 16 buffs + phase dash + fuel system
- [x] Mothership docking (charge summon / magazine stay / sweep fire / early undock)
- [x] Mid-run base refit (4 modules + RP economy + talent routes)
- [x] Combat parity (aim assist / 3 enemy bullet types / boss escape / 8 movement patterns)
- [x] Difficulty selection (easy / normal / hard) & full boss enrage — iteration 3.4
- [x] Performance optimization (bullet/explosion pooling, group-query caching, HUD throttling) — iteration 3.4
- [ ] Tutorial (6 stages) — iteration 3.5
- [ ] Online leaderboard
- [ ] Release builds (deferred)

The item-by-item parity checklist against the original lives in [docs/PORTING_PARITY.md](./docs/PORTING_PARITY.md); task guidance in [docs/TASK_REPORT.md](./docs/TASK_REPORT.md).

## 🤝 Contributing

Issues and PRs are welcome! Before submitting, please make sure:

1. All four headless test suites above pass;
2. You follow the conventions in `AGENTS.md` (collision layers, code style, test strategy);
3. Gameplay changes update the corresponding row in `docs/PORTING_PARITY.md`.

Reference projects: [nezvers/Godot-GameTemplate](https://github.com/nezvers/Godot-GameTemplate), [quiver-dev/top-down-shooter-core](https://github.com/quiver-dev/top-down-shooter-core), [Unchained112/SimpleTopDownShooterTemplate2D](https://github.com/Unchained112/SimpleTopDownShooterTemplate2D).

## 📄 License

This repository is currently private and has not chosen an open-source license yet; please contact the author before using or redistributing.

---

*InfiAir is a Godot remake of airwar-game (Python/Pygame), maintained as a hobby project — feedback welcome.*
