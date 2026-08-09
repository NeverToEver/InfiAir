# Endless Balance Plan (ENDLESS_BALANCE_PLAN)

> 2026-07-29. Single source of truth for post-15-min endless balance; direction changes sync `docs/ROADMAP.md` + AGENTS.md doc-sync. Status: **implemented**: plans 1~5 landed, D1/D2 closed (§5/§6).

## 1. Audit

First ~15 min well-tuned (loop in AGENTS.md; 50s boss escape DPS check, enrage lock). Gap: **after 5th boss kill: steady state — no harder, no end.**

| Axis | Player | Enemy |
| --- | --- | --- |
| Output | DPS cap ×9.5 (single)~×38 (theor.) | Normal HP cap ×1.84 |
| Survival | extra_life +50/stack (99 nominal) + lifesteal 10% | Bullet dmg const 12~21 |
| Density | — | Interval floor 2.5s, size cap 5 |
| Boss | HP ×8 vs DPS cap | Capped after 5th kill |

Old: `difficulty_multiplier = min(1 + (2^min(boss_kills,10) − 1) × 0.25, 8)` — ×8 at 5th kill, kill-only; replaced 2026-07-29 by plans 3/4 (§6).

## 2. Reference Patterns

Two paradigms; project had neither:
- **A. Inevitable death**: enemy growth beats player, loss guaranteed → score matters (Geometry Wars; density/speed ramp ∞, player growth fixed).
- **B. Timed terminus**: fixed duration then forced end (Vampire Survivors 30 min).

**§5 gates plans 1/3 (A chosen).**

## 3. Problems

> 注：§3 行号为 2026-07-29 撰写时锚点（问题均已按 §4 [landed] 落地），改码后可能漂移——以当前代码/`docs/BALANCE_MAP.md` 为准。

- **P0-1 no enemy output growth, survival uncapped**: bullet dmg const; extra_life sole late pick (rest exit pool at max stacks), +750~1000 HP/h; lifesteal 10% max snowballs — `enemies.bullet_damage`/`boss.bullet_damage` unramped; `scripts/buff_select.gd:154-156`; `autoload/game_state.gd:498-509`
- **P0-2 events ignore mult**: turret HP 80, formation fighter HP 60 const; free points after 10 min — `scripts/elite_turret_event.gd:127-132`; `scripts/formation_strike_event.gd:115-126`
- **P1-3** 2^n: 4th→5th kill ×4.75→×8 (+68%), then cap — `autoload/game_state.gd:470-477`
- **P1-4** hard fastest buff cadence (score ×3, milestone ×1.5); intent undocumented — `difficulty.*.score`/`.milestone`; `autoload/game_state.gd:331-343`
- **P1-5** difficulty kill-only: dodge stalls, streak spikes; time/score unused — `autoload/game_state.gd:470-477`
- **P2-6** rapid_fire card "25%", actual interval ×0.75 = +33%/stack — `scripts/player.gd:181-182`; `data/translations.csv:36`
- **P2-7** pool `desc` dead (laser 10 dmg/10s vs 16 dmg/8s) — `scripts/buff_select.gd:4-101` vs `scripts/laser_weapon.gd:10-11`
- **P2-8** explosive per-level scaling unreachable (cap 1) — `scripts/bullet.gd:155-166`
- **P2-9** `player.gd:56` "fuel tank buff" comment unimplemented — `scripts/player.gd:56,124`
- **P2-10** explosive unlock `boss_kills >= 3` hardcoded — `scripts/buff_select.gd:145`

## 4. Plans

> Numbers → `data/balance.json` + `cfg()` fallbacks; k drafts, calibrate.

### Plan 1 — Enemy damage ramp (P0-1) [landed]

- Bullet/body-hit dmg × `(1 + k × (difficulty_multiplier − 1))`, k≈0.08 (×1.56 at mult=8) or time-based; at `enemy.gd`, `boss.gd`, formation bombs.
- extra_life cap (10 stacks / 500 HP) or diminishing (+50×0.9^n); 99 cap nominal (milestone thresholds lock it); tightening lossless.
- Landed: `enemies.damage_ramp_factor`, `GameState.EnemyDamageRamp()`; bullets split by faction in `csharp/godot/Bullet.cs` (all types); body hits (`Enemy.cs`/`Boss.cs`) + formation bombs (`FormationStrikeEvent.cs`) separate.
- Landed extra_life: 99→**10 stacks** (HP 100+500=600) — `buffs.extra_life.max_stacks`=10, pool `max` synced, card "infinitely stackable"→"max 10" (zh+en).

### Plan 2 — Events eat mult (P0-2, one-line) [landed]

- Turret/formation HP × `(1 + enemies.hp_ramp_factor × (mult − 1))`, same as normal enemies (`elite_turret_event.gd:127`, `formation_strike_event.gd:115`).
- Landed: via `GameState.EnemyHpRamp()`.

### Plan 3 — Smooth mult, drop hard cap (P1-3) [landed]

- `2^n`→linear/log (e.g. `1 + 0.5 × boss_kills`); ×8→slow growth (e.g. `8 + 0.2 × (bk − 5)`).
- §5-gated: A must drop cap; B may keep.
- Landed: D1 = A; **cap-free linear** `mult = 1 + progression.per_boss_kill(0.5) × boss_kills + time term` (plan 4); `GameState.RecomputeDifficulty()` (kill + time-tier + save-restore); `2^n + ×8` dropped. Boss HP scales uncapped (50s DPS check → escape valve); `enemies.hp_ramp`/`damage_ramp`/spawn ramps ∞; player fixed (DPS ×9.5, HP 600). (2026-07-29 落地值;2026-08-04 校准定稿见 §6.1)

### Plan 4 — Time/score factor (P1-5) [landed]

- e.g. `mult = f(boss_kills) + elapsed / 600`; low weight plugs dodge-stall.
- Landed: time **quantized** — `progression.time_step_seconds`(30s) + `progression.per_ten_minutes`(1.0)/10 min = `floor(run_time/30) × 0.05`; stable HUD, pinnable tests; in-run `run_time` only (pause excluded); dodging still pressures. New `progression` section, cached `ApplyBalance()`, recomputed on tier crossing in `_Process`, broadcast `DifficultyChanged`. (2026-07-29 落地值;2026-08-04 校准定稿见 §6.1)

### Plan 5 — Text/config cleanup (P2) [landed]

- Landed: `BUFF_RAPID_FIRE_DESC` 33% zh/en (translations.csv); 16 dead `desc` deleted (cards only via `BUFF_%s_DESC` — single source of truth); `buffs.explosive.unlock_boss_kills`=3. P2-8/9: per-level scaling removed (fixed matches card); `Player.cs` dead comment → config-override note.

## 5. Decisions

| # | Decision | Conclusion | Impact |
| --- | --- | --- | --- |
| D1 | Paradigm | **A. Inevitable death**: plans 1+3 cap-drop suffices, no new settlement flow, fits arcade score-attack | Plan 3 cap-free (×8 dropped); B rejected |
| D2 | hard ×1.5 intent | **Intentional**: DIFFICULTY_DEFS comment "avoid sparse hard buff cadence" | decision 10 in `docs/archive/PORTING_PARITY.md`; values unchanged |

## 6. Implementation & Acceptance

- 26 assertion scenes 0 FAIL (`--headless --import`, `--quit-after 300`); synced: smoke (dynamic mult), difficulty (+§4b 5 assertions + tier pinning), enemy_combat (escape pinning), hit_logic (A4 dmg dynamic, boss_pattern basis).
- Probe `--autoplay-seconds=300 --seed=20260729`: **0 anomalies**; mult ≈ ×2.5 at 300s (2 kills ×2.0 + 10 tiers ×0.5); no plateau; >15 min calibration deferred (§7).
- Number changes → run `balance_test.tscn`, `difficulty_test.tscn`, `boss_enrage_test.tscn`, `wave_pacing_test.tscn`, then `autoplay_test.tscn` probe (SUMMARY kills/time).
- P2 text → `i18n_test.tscn`.

### 6.1 Deep-run calibration (2026-08-04, `docs/archive/2026-08-04-endless-calibration-plan.md`)

**Landed values** (balance.json, cfg fallbacks in `game_state.gd:158-160` unchanged — only numbers):

| Key | Old | New | Rationale |
| --- | --- | --- | --- |
| `progression.per_boss_kill` | 0.5 | **0.6** | Boss-kill contribution +20% |
| `progression.per_ten_minutes` | 1.0 | **1.5** | Time-axis +50% (0.075/30s tier) |
| `enemies.hp_ramp_factor` | 0.12 | **0.25** | HP ×(1+0.25×(mult−1)); kill-efficiency must visibly drop in deep runs |
| `enemies.damage_ramp_factor` | 0.08 | **0.20** | Damage ×(1+0.20×(mult−1)); bullet 12→~19 at mult 4 |

**Baseline probe (pre-calibration, 900s real, seed 20260729, ~27 min game time)**: diff 1.15→4.20, 0 deaths, 23 hits, HP mostly full (138–150 avg, min 80–150) — **zero-pressure steady state confirmed** (the exact failure mode §8.2 warns about); 1/9 boss kills (50s escape valve).

**Calibration iterations** (same probe command, same seed):

| Round | Values (per_boss/per_ten/hp_ramp/dmg_ramp) | Result |
| --- | --- | --- |
| 1 | 0.6/1.2/0.20/0.15 | HP pressure up (min 65–100) but late-run still full HP, kills not dropping → insufficient |
| 2 (final) | **0.6/1.5/0.25/0.20** | diff 1.38→6.33 @27min (no plateau); HP min 40–69 sustained from 6 min on, DDA 15–29% windows, 0 deaths (no cliff); hits spread across whole run (83→888s) |

**Acceptance**: 3 × 900s probes 0 `[ANOMALY]`; 45 assertion scenes 0 FAIL; difficulty_test curve pins updated (2 kills ×2.2, 65s two tiers +0.15 → 2.35); gdformat/gdlint clean. Manual feel check (15+ min real play) remains a pre-release item.

## 7. Maintenance

- Covers endless-section balance only; new buffs/enemies → `docs/ROADMAP.md` Phase 2.
- **Calibration done 2026-08-04** (§6.1): progression/ramp values tuned for >15 min runs via autoplay probes. Future re-calibration: edit `progression`/ramp factors in `balance.json`, record to §6.1.
