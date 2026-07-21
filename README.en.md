<div align="center">

# 🛩️ InfiAir

**A 2D top-down space shooter built with Godot 4 + GDScript — a remake of the Python original [airwar-game](https://github.com/NeverToEver/airwar-game)**

**English** · [中文](./README.md)

[![Godot](https://img.shields.io/badge/godot-4.6-478cbf?logo=godot-engine&logoColor=white)](https://godotengine.org/)
[![GDScript](https://img.shields.io/badge/GDScript-100%25-478cbf)](https://docs.godotengine.org/en/stable/tutorials/scripting/gdscript/)
[![Tests](https://img.shields.io/badge/tests-446%20passed-brightgreen)](#verification)
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
- **Mothership docking**: charge-up summon with automatic point-snap docking — twin turrets sweeping an upward 80° fan, homing-free missile volleys (up to 5 targets), and direct WASD control of the mothership itself while docked. A low-ammo warning precedes a forced undock, or hold H to undock early for a cooldown refund.
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
| H (hold 3s) | Charge-summon the mothership (WASD pilots the ship while docked; hold H 2s for early undock) |
| B (hold 1.5s) | Homecoming — mid-run base refit |
| K (hold 3s) | Abandon the sortie |
| ESC | Pause (the pause menu holds the only save entry) |
| R | Restart (on game-over / pause screens) |

> Three view zoom levels (Small 1.0 / Medium 1.35 / Large 1.7, default Medium): Settings → Modes → View (persisted in `user://profile.json`; the HUD lives on a CanvasLayer and is unaffected by camera zoom).

## 🚀 Getting Started

Requires [Godot 4.6](https://godotengine.org/download) (standard build — no .NET needed).

```bash
git clone https://github.com/NeverToEver/InfiAir.git
cd InfiAir
godot --path .          # run directly, or open the project in the editor and press F5
```

## 🧭 Game Loop

- Start with 100 HP: taking a hit grants 1.5s of invulnerability and clears enemy bullets within 250px; HP slowly regenerates after a few seconds out of combat (rate varies by difficulty), and can be fully restored by base repairs (2 RP) or mothership resupply; **pure score-based — no item drops**.
- 4 enemy classes × 8 movement patterns, unlocked progressively by score; 3 elite variants; enemy bullets come in single / spread / laser types (12/10/20 damage, plus 20 for body collisions).
- Milestone thresholds (starting at 3000, scaling up per cycle) pause the game for a 1-of-3 buff draft; bosses spawn every 1500 points or 90s, granting +500 points and raising the difficulty multiplier (`1 + (2^min(kills,10) − 1) × 0.25`, capped at 8x).
- RP (requisition points) come from boss kills (+5) and base missions (+3), spent on repairs and fuel at the base.
- Save anytime via the pause menu; continue from the title panel on next launch. Death deletes the save.
- The start panel includes a tutorial entry: a 6-stage tutorial (movement & aim / boost & dash / combat / mothership docking / homecoming & base / boss enrage); Esc quits anytime, and the button shows "Tutorial ✓" once completed.

## 🏗️ Architecture

```text
main.tscn (run orchestration)
 ├─ Player (movement / aim assist / auto-fire / fuel / phase dash / laser weapon)
 ├─ Spawner (7 enemy-class config tables + score-gated unlocks + boss rotation)
 ├─ Mothership (auto-dock state machine: summon → dock → stay (piloting + sweep + missiles) → release → depart)
 ├─ HUD / BuffSelect / BaseConsole / GameOver / Pause / StartPanel
 └─ GameState (autoload: 100-HP health / score / buffs / RP / missions / routes / saves / SFX pool / shake)
```

- Collision layers: `1=player 2=player_bullet 3=enemy 4=enemy_bullet`; bullets resolve damage on their side.
- Balance config: `data/balance.json` holds all tunable values (player/enemies/boss/spawner/mothership/buffs/milestones/difficulty/effects), loaded once at startup via `GameState.cfg()` with per-key fallback to script defaults — tweak the JSON, no code changes needed.
- Run save `user://savegame.json` and profile `user://profile.json` are versioned.
- Tests are headless scene scripts (no framework) — see `AGENTS.md`.

## ✅ Verification

```bash
godot --headless --import --path .          # assets & script parsing
godot --headless --path . --quit-after 300  # runtime smoke
godot --headless --path . res://test/smoke_test.tscn        # main flow — 95 assertions
godot --headless --path . res://test/base_system_test.tscn  # save/RP/missions/routes — 46
godot --headless --path . res://test/enemy_combat_test.tscn # enemies & bosses — 31
godot --headless --path . res://test/buff33_test.tscn       # buffs/mothership/give-up — 29
godot --headless --path . res://test/difficulty_test.tscn   # difficulty/milestones/settings — 52
godot --headless --path . res://test/boss_enrage_test.tscn  # boss enrage — 23
godot --headless --path . res://test/tutorial_test.tscn     # tutorial — 19
godot --headless --path . res://test/balance_test.tscn      # balance config — 25
godot --headless --path . res://test/keybind_test.tscn      # key rebinding — 15
godot --headless --path . res://test/i18n_test.tscn         # i18n zh/en — 9
godot --headless --path . res://test/view_zoom_test.tscn    # view zoom — 43
godot --headless --path . res://test/hit_logic_test.tscn    # hit & collision parity — 59
```

446 assertions, all passing.

## 🗺️ Roadmap

- [x] Core run loop (waves / milestone buffs / bosses / game over)
- [x] Game feel (shake / particles / synthesized SFX & BGM / telegraphs)
- [x] 16 buffs + phase dash + fuel system
- [x] Mothership docking (charge summon / magazine stay / sweep fire / early undock)
- [x] Mid-run base refit (4 modules + RP economy + talent routes)
- [x] Combat parity (aim assist / 3 enemy bullet types / boss escape / 8 movement patterns)
- [x] Difficulty selection (easy / normal / hard) & full boss enrage — iteration 3.4
- [x] Performance optimization (bullet/explosion pooling, group-query caching, HUD throttling) — iteration 3.4
- [x] Tutorial (6 stages, via the start panel; completion recorded in profile) — iteration 3.5
- [x] Three-level view zoom (small / medium / large) — iteration 3.7
- [x] Hit & collision parity pass (r7 hitbox / boss body collision / enrage HP lock / per-type bullet damage) — iteration 3.8
- [x] 100-HP damage model & full Appendix-A parity (hit-rule chain / mothership auto-dock + missiles + piloting / combat number review) — iteration 3.9
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
