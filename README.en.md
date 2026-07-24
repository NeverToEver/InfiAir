<div align="center">

# 🛩️ InfiAir

**A 2D top-down space shooter built with Godot 4 + GDScript — a remake of the Python original [airwar-game](https://github.com/NeverToEver/airwar-game)**

**English** · [中文](./README.md)

[![Godot](https://img.shields.io/badge/godot-4.6-478cbf?logo=godot-engine&logoColor=white)](https://godotengine.org/)
[![GDScript](https://img.shields.io/badge/GDScript-100%25-478cbf)](https://docs.godotengine.org/en/stable/tutorials/scripting/gdscript/)
[![Tests](https://img.shields.io/badge/tests-586%20passed-brightgreen)](#verification)
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
- **3 rotating bosses + a full enrage sequence**: heavy / skirmisher / mothership archetypes; below 30% HP a boss enrages — HP lock, bullet time, an orbiting attack run, rooting barrage, and a final release salvo (see Game Loop); fail to kill within 50 seconds and the boss flees.
- **Mothership docking**: charge-up summon with a ghost preview and automatic point-snap docking; magazine-based stay (10 cells × 2s) — twin turrets sweeping an upward 80° fan, missile volleys (up to 5 targets), direct WASD piloting; a 4-cell ammo warning precedes forced undock 5s later, or hold H to undock early (with progress bar) for a cooldown refund.
- **Mid-run base refit**: homecoming does NOT end the run — four base modules (hangar / weapon hardpoints with mutually exclusive talent routes / repair & resupply with an RP economy / mission planning), then you return to the same battle.
- **Holographic sci-fi UI design system**: unified color tokens and type scale, chamfered panels, primary/secondary button hierarchy, staggered fade-in motion — every screen (start / settings / pause / game over / buff draft / base) shares one skeleton.
- **Arcade-grade visibility**: brightened player ship with a cyan rim glow, plus a pulsing dot on the actual r=7 hitbox — never lose your ship in bullet hell.
- **Fully procedural assets**: sprites are procedurally generated (inherited from the Python original); SFX and BGM are synthesized by `scripts/tools/generate_audio.py`. Zero external asset dependencies.

## 🖼️ Screenshots

| Main menu | Gameplay | Base refit |
|-----------|----------|------------|
| ![Main menu](./docs/screenshots/start.png) | ![Gameplay](./docs/screenshots/gameplay.png) | ![Base](./docs/screenshots/base.png) |

## 🎮 Controls

| Input | Action |
|-------|--------|
| WASD / Arrow keys | Move |
| Mouse | Aim (auto aim-assist locks targets within 230px; flick to break lock) |
| — | Weapons fire fully automatically |
| Shift (hold) | Boost (×1.8 speed, drains fuel) |
| Ctrl (hold) | Precision movement (speed ×0.35) |
| Space | Phase dash (requires buff; invulnerable, costs 25% fuel) |
| H (hold 3s) | Charge-summon the mothership (WASD pilots the ship while docked; hold H 2s for early undock, with progress bar) |
| B (hold 1.5s) | Homecoming — mid-run base refit |
| K (hold 3s) | Abandon the sortie |
| ESC | Global back: pause in combat (save your run) / back one page / exit confirmation at top level |
| R | Restart (on game-over / pause screens) |

> All keys are rebindable in Settings → Controls (Esc/R are fixed; bindings persist in `user://profile.json`).
> UI language: English / 中文 via Settings → Modes → Language.
> Three view zoom levels (1.0 / 1.35 / 1.7, default Medium) and three window sizes (1280×720 / 1600×900 / 1920×1080, default Large) — independent settings, both persisted.

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
- Boss enrage below 30% HP: HP locks at the 30% checkpoint (invulnerable during the sequence) → bullet time → the boss orbits your snapshot position while rooting you in place (you can still shoot) → unlock + dense release barrage → returns to the fight permanently enraged (fire rate ×1.5 / speed ×1.3).
- RP (requisition points) come from boss kills (+5) and base missions (+3), spent on base repairs and fuel (2 RP each).
- Save anytime via the pause menu (homecoming also updates the save automatically); continue from the title panel on next launch. Death deletes the save.
- A welcome screen greets the first launch; the start panel includes a tutorial entry: 6 stages (movement & aim / boost & dash / combat / mothership docking / homecoming & base / boss enrage); Esc quits anytime, and the button shows "Tutorial ✓" once completed.

## 🏗️ Architecture

```text
main.tscn (run orchestration)
 ├─ Player (movement / aim assist / auto-fire / fuel / phase dash / laser weapon / hitbox indicator)
 ├─ Spawner (4 enemy + 3 elite config tables / score-gated unlocks / boss rotation)
 ├─ Mothership (auto-dock state machine: summon → dock → stay (piloting + sweep + missiles) → release → depart)
 ├─ Boss (3-type rotation + enrage sequence state machine: HP lock / orbit / root / barrage / return)
 ├─ HUD / BuffSelect / BaseConsole / GameOver / Pause / Settings / StartPanel / Welcome
 ├─ BackNavigator (global back/exit state machine: PC Esc, gamepad B, Android back — one route)
 └─ GameState (autoload: 100-HP health / score / buffs / RP / missions / routes / saves / profile / SFX pool / shake)
```

- **Balance config**: `data/balance.json` holds all tunable values, accessed via `GameState.cfg()` with per-key fallback to script defaults — tweak the JSON, no code changes needed.
- **UI design system**: `scripts/ui_theme.gd` provides color tokens, a type scale (72/40/28/24/18), and component factories (page skeleton / primary & secondary buttons / section headers / motion) shared by every screen.
- **Performance**: bullet/enemy/explosion object pooling, registries instead of group queries, trig lookup tables, throttled HUD; a `--fixed-fps` benchmark scene measures raw frame time.
- Collision layers: `1=player 2=player_bullet 3=enemy 4=enemy_bullet`; bullets resolve damage on their side; hits only count on the r=7 hitbox point.
- Run save `user://savegame.json` and profile `user://profile.json` (high score / difficulty / keybinds / locale / view / window size) are versioned; corrupted files are quarantined automatically.
- Tests are headless scene scripts (no framework) — see `AGENTS.md`.

## ✅ Verification

```bash
godot --headless --import --path .          # assets & script parsing
godot --headless --path . --quit-after 300  # runtime smoke
godot --headless --path . res://test/smoke_test.tscn          # main flow — 111 assertions
godot --headless --path . res://test/hit_logic_test.tscn      # hit & collision parity — 60
godot --headless --path . res://test/difficulty_test.tscn     # difficulty/milestones/settings — 52
godot --headless --path . res://test/base_system_test.tscn    # save/RP/missions/routes — 46
godot --headless --path . res://test/view_zoom_test.tscn      # view zoom — 43
godot --headless --path . res://test/startup_flow_test.tscn   # startup/corrupt saves/welcome — 40
godot --headless --path . res://test/boss_enrage_test.tscn    # boss enrage sequence — 33
godot --headless --path . res://test/enemy_combat_test.tscn   # enemies & bosses — 31
godot --headless --path . res://test/buff33_test.tscn         # buffs/mothership/give-up — 29
godot --headless --path . res://test/tutorial_test.tscn       # tutorial — 29
godot --headless --path . res://test/balance_test.tscn        # balance config — 25
godot --headless --path . res://test/back_navigation_test.tscn # back/exit state machine — 23
godot --headless --path . res://test/window_size_test.tscn    # window size — 17
godot --headless --path . res://test/keybind_test.tscn        # key rebinding — 15
godot --headless --path . res://test/pool_reuse_test.tscn     # object pool reuse — 12
godot --headless --path . res://test/esc_navigation_test.tscn # Esc navigation — 11
godot --headless --path . res://test/i18n_test.tscn           # i18n zh/en — 9
```

Plus automated probes (not assertion tests):

```bash
godot --headless --path . res://test/autoplay_test.tscn  # ≥8 min simulated human play: full interaction coverage + anomaly monitors
godot --path . res://test/ui_capture.tscn                # windowed six-screen UI capture (/tmp/ui_*.png)
```

**586 assertions across 17 test scenes, all passing.**

## 🗺️ Roadmap

- [x] Core run loop (waves / milestone buffs / bosses / game over)
- [x] Game feel (shake / particles / synthesized SFX & BGM / telegraphs)
- [x] 16 buffs + phase dash + fuel system
- [x] Mothership docking (charge summon / magazine stay / sweep fire / early undock)
- [x] Mid-run base refit (4 modules + RP economy + talent routes)
- [x] Combat parity (aim assist / 3 enemy bullet types / boss escape / 8 movement patterns)
- [x] Difficulty selection + performance pass (pooling / registries / HUD throttling) — iteration 3.4
- [x] Tutorial (6 stages) — iteration 3.5
- [x] Three-level view zoom — iteration 3.7
- [x] Hit & collision parity pass (r7 hitbox / boss body collision / per-type bullet damage) — iteration 3.8
- [x] 100-HP damage model & full Appendix-A parity — iteration 3.9
- [x] Welcome screen + startup hardening + global back/exit state machine — iteration 3.10
- [x] Object-pool reuse fix + autoplay simulated-play probe — iteration 3.11
- [x] Window size levels + mothership early-undock progress bar — iteration 3.12
- [x] UI design system refactor (unified skeleton / button hierarchy / all screens migrated) — iteration 3.13
- [x] Full boss enrage sequence (HP lock / orbit attack / root / barrage) + player visibility (brighten / rim glow / hitbox dot) — iteration 3.14
- [ ] Release builds (deferred)

The item-by-item parity checklist against the original, the iteration history, and the roadmap live in [docs/PORTING_PARITY.md](./docs/PORTING_PARITY.md).

## 🤝 Contributing

Issues and PRs are welcome! Before submitting, please make sure:

1. All headless test suites above pass;
2. You follow the conventions in `AGENTS.md` (collision layers, UI design system, code style, test strategy);
3. Gameplay changes update the corresponding row in `docs/PORTING_PARITY.md`.

Reference projects: [nezvers/Godot-GameTemplate](https://github.com/nezvers/Godot-GameTemplate), [quiver-dev/top-down-shooter-core](https://github.com/quiver-dev/top-down-shooter-core), [Unchained112/SimpleTopDownShooterTemplate2D](https://github.com/Unchained112/SimpleTopDownShooterTemplate2D), [Maaack/Godot-Menus-Template](https://github.com/Maaack/Godot-Menus-Template).

## 📄 License

This repository is currently private and has not chosen an open-source license yet; please contact the author before using or redistributing.

---

*InfiAir is a Godot remake of airwar-game (Python/Pygame), maintained as a hobby project — feedback welcome.*
