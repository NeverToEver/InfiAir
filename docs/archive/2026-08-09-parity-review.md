# Parity Review 2026 — Python Original (`~/airwar`) vs Godot InfiAir

> Inventory date: 2026-08-09. Scope: feature mechanics, assets, audio, i18n, balance data, and test coverage of the Godot remake (`/home/ubt/InfiAir`, Godot 4.6.2 + full C#, zero GDScript) against the Python original (`~/airwar`). All counts measured from the working tree on 2026-08-09 unless noted. Authoritative test counts: `docs/TESTING.md`.

## Verdict per dimension

| Dimension | Original (Python) | Godot InfiAir | Status |
|---|---|---|---|
| Feature mechanics | 22-item gap list in `docs/archive/PORTING_PARITY.md` | All 22 items **已对齐 (aligned)** or **演进取代 (superseded by evolution)**; see §1 | 已对齐 / 演进超越 |
| Enemy movement modes | 8 strategies (`airwar/entities/movement_strategies.py`: Straight / Sine / Zigzag / Dive / Hover / Spiral / Noise / Aggressive) | 7 strategy classes covering all 8 modes (`csharp/godot/EnemyMoveStrategy.cs`: `HoverMove` merges straight+hover; Sine / Zigzag / Dive / Spiral / Noise / Aggressive) | 已对齐 |
| Buffs | 15 (`airwar/game/systems/reward_system.py` `REWARD_POOL`: 3 health + 6 offense + 2 defense + 4 utility) | 19 (`data/balance.json` `buffs`): all 15 present + 4 new — `crit_shot`, `shield`, `bullet_speed` (2026-08-04) + `efficient_boost` (recovery-side counterpart of `boost_recovery`, known deviation #1) | 演进超越 (+4) |
| Mothership | Magazine (10 rounds) + twin 80° gatling + missiles, 20s stay, ×1/3 score | Aligned (3.1/3.9, A14/A15) + **upgrade system** (2026-08-04, `mothership_upgrade_test`) | 已对齐 / 演进超越 |
| Return-to-base + base + tasks | Mid-run intermission: 4 modules (hangar / payload / repair / mission planning) + RP economy + 3 standing tasks | Aligned (3.2) + **task rotation** (2026-08-05, `base_task_refresh_test`) | 已对齐 |
| Tutorial | 6 stages | Aligned (3.5, `tutorial.tscn` + `tutorial_done` persistence) | 已对齐 |
| Accounts / leaderboard | Welcome scene with login panel (original design; local account system was shelved in the original) | **UserDB local accounts** (login/register/guest/delete + per-user save/settings + local leaderboard, 2026-08-04, gap list #22) | 已对齐 (完整账户版) |
| Godot-only presentation layer | — | Death replay (`DeathReplay`, ghost-bullet 3s replay in `Main.cs`), intro cinematic (6 shots) / return cinematic (7 shots) / mothership shows (`csharp/godot/CinematicFx.cs`), fog events (`FogEventManager`, `docs/FOG_EVENTS.md`), unified event manager (`GameEventManager`, `docs/EVENT_MANAGER.md`), elite turret & formation strike events, buff visual attachments, meta health FX tiers | 演进超越 (original has none) |
| Assets | Runtime procedural drawing, 3 modules (`airwar/utils/_sprites_ships.py` / `_sprites_bullets.py` / `_sprites_common.py`); 4 ship builders (player/enemy/elite/boss) with health-ratio bucket × size caches (brief-口径 ~36 variants, not enumerated per-file) | 15 offline-generated PNGs (14 before this round's `boss_ship_4.png`) via `scripts/tools/generate_player_sprite.py` / `generate_enemy_sprites.py` / `generate_mothership_sprite.py` (see §2) + bullets drawn at runtime from a **shared atlas** (`Bullet.cs`, P0-3, lazily rasterized single shared texture) | 演进超越 (offline deterministic generators) |
| Audio | 3 shipped WAV (bullet_fire x3 round-robin variants) + 2 synthesized (bullet_fire fallback zap + `player_hit` beep), **no music** (`airwar/audio/sound_manager.py`) | 11 WAVs incl. **40.00s looping BGM** (`bgm_loop.wav`) + 10 SFX (`generate_audio.py`, fixed seed 20260720) | 演进超越 |
| i18n | 178 keys per locale (`airwar/locales/zh_CN.json` / `en_US.json`, each 178) | 308 keys measured in `data/translations.csv` (2026-08-09; zh+en bilingual columns; brief-口径 330 not reproducible — see §4) | 演进超越 |
| Balance data | Python constants under `airwar/airwar/config/` (`game_config.py` / `design_tokens.py` / `difficulty_config.py` / `settings.py`; the old `game_constants.py` module was restructured away) | `data/balance.json` — 20 top-level keys = **19 gameplay config sections** + `version` metadata (world_scale/player/enemies/elites/boss/hud/spawner/mothership/buffs/milestones/base_task/progression/difficulty/effects/elite_turret_event/formation_strike_event/fog_events/tutorial/dda) | 演进超越 (data-driven, 19 sections) |
| Tests | pytest — 36 test files / **263 `test_*` functions** (`~/airwar/tests/`) | **55 assertion scenes** (of 64 total: + `autoplay_test` probe + `perf_bench` + 7 screenshot tools; authoritative count in `docs/TESTING.md`) + **75 xUnit tests** in `tests-csharp/` (7 files; `dotnet test` 75/75, verified 2026-08-09) | 演进超越 |

## 1. Feature mechanics — gap list conclusion

The 22-item gap list of `docs/archive/PORTING_PARITY.md` (mothership dock/fire rules, return-to-base + orbital strike, base 4-module + RP + tasks, talent routes, auto-fire, aim assist 3-tier, enemy bullet types, boss flee, aggressive mode #8, 15s enemy lifetime, missing buffs, buff gating, phase dash fuel, Ctrl fine-move, K surrender, difficulty tiers, boss enrage, milestone curve, 6-stage tutorial, control toggles, welcome/accounts) is **closed as of 2026-08-04 (item #22)**: every row is either 已对齐 or superseded by documented evolution (boss redesign, wave pacing, endless difficulty curve — decisions 8–10 in that doc). **Gameplay mechanics: zero remaining gaps.** Evolution supersessions are intentional and recorded in `docs/BOSS_REDESIGN.md`, `docs/ENDLESS_BALANCE_PLAN.md`, and the known-deviations list (§5 of `PORTING_PARITY.md`).

## 2. Asset pipeline — this round's additions (本次补齐)

- **`boss_ship_4.png` — Eclipse (Ⅳ型 月蚀) dedicated sprite.** `csharp/godot/Boss.cs` previously documented "4 型「月蚀」复用 1 型贴图" (2026-08-04); the 4th boss was visually identical to boss 1. This round adds its own sprite to `assets/sprites/` via `generate_enemy_sprites.py` (new `eclipse` builder, `boss_ship_4.png`), bringing the offline-generated sheet from **14 → 15 PNGs**; `Boss.cs` now loads `_bossSprite4` instead of reusing type 1.
- **`scripts/tools/regenerate_all.sh` — unified generator entry.** Deterministically re-runs all 4 offline generators (player → enemy/elite/boss/carrier/turret → mothership → audio) in fixed order with an interpreter probe (`python3` w/ PIL → repo `.venv/bin/python3` fallback); output paths anchored to script location (cwd-independent, R07 rule); `set -euo pipefail`, idempotent, byte-reproducible (fixed seed 20260720 for `generate_audio.py`, pure deterministic drawing for sprites). Companion doc: `scripts/tools/README.md`.

## 3. Conclusion

- **玩法机制零缺口 (zero gameplay-mechanics gap)**: the 22-item parity list is fully closed; where the remake deviates, it is a documented, deliberate evolution (boss redesign, wave pacing, endless curve, damage/HP ramps) — never a regression.
- **Assets**: runtime procedural drawing (original, ~36 cached variants) → 15 offline-generated PNGs + runtime shared bullet atlas (Godot); this round adds the Eclipse boss sprite (`boss_ship_4.png`, the 15th) and the unified regeneration entry (`regenerate_all.sh`).
- **Audio**: no-music 3 WAV + 2 synth (original) → 11 WAV incl. 40s BGM loop.
- **i18n / data / tests**: 178 → 308 keys (zh+en); Python constants → 19-section `balance.json`; pytest 263 fns → 55 assertion scenes + 75 xUnit (authoritative counts: `docs/TESTING.md`).

## 4. Count caveats (measured 2026-08-09)

- `translations.csv` = **308 keys** (csv-parse; 309 logical rows incl. header — `wc -l` reads 331 raw lines because quoted cells embed newlines). A brief-口径 of 330 was not reproducible at write time — if a concurrent i18n task adds keys, re-measure before citing.
- `balance.json` = 20 top-level keys, of which `version` is metadata → **19 gameplay sections** (brief-口径 19 段 holds).
- Original sprite "36 variants" is the brief-口径 aggregate of the runtime cache system (4 builders × health-ratio buckets × sizes); the code does not enumerate a literal list of 36.
- Original config was restructured from `config/game_constants.py` into `airwar/airwar/config/` (game_config / design_tokens / difficulty_config / settings); the parity values cited in `PORTING_PARITY.md` remain traceable there.
