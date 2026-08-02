# Endless-Mode Balance Improvements (ENDLESS_BALANCE_PLAN)

> Initiated 2026-07-29, from the same-day mechanics & balance audit. This document is the single source of truth for balance evolution of the "endless segment past 15 minutes";
> sync `docs/ROADMAP.md` on stage/direction changes and register in AGENTS.md's "Documentation sync requirements".
> Status: **implemented** — 2026-07-29: plans 1–5 fully landed, D1/D2 decisions finalized (see §5/§6).

---

## 1. Audit Findings Summary

The first ~15 minutes are carefully tuned and sound: the loop of score → milestone buff pick-1-of-3 → boss rotation → in-run RP economy is complete, and designs like the 50s boss-flee DPS check, enrage HP-lock, and per-axis caps are mature.

But as an endless mode there is a structural gap: **after the 5th boss kill the game enters a pure steady state — it neither gets harder nor ever ends**.

| Axis | Player | Enemy | Result |
| --- | --- | --- | --- |
| Output | DPS multiplicative, capped ×9.5 (single) ~ ×38 (theoretical) | Normal enemy HP capped ×1.84 | Mid/late-game trash dies in one hit |
| Survival | extra_life +50 HP per stack (99-stack nominal cap) + lifesteal up to 10% cap | Enemy bullet damage constant 12~21 for the whole run | Survival axis inflates infinitely one-sidedly |
| Density | — | Wave interval hard floor 2.5s, wave size hard cap 5 ships | Pressure has a hard ceiling |
| Boss | HP ×8 full scaling, aligned with the player DPS cap | Also capped after the 5th kill | Steady state |

Pre-revision formula: `difficulty_multiplier = min(1 + (2^min(boss_kills,10) − 1) × 0.25, 8)` — hits the ×8 cap at the 5th kill, driven only by boss kills, independent of time. After that a run can never end, theoretically or technically; score attack degrades to pure time investment. (Superseded from 2026-07-29 by the linear + time-axis curves of §4 plans 3/4, see §6.)

## 2. Reference Patterns (Conventions of Mature Titles)

Mature endless/survival games handle "infinity" with only two paradigms; this project currently fits neither:

- **Guaranteed-death curve**: enemy growth eventually outpaces the player, the player must lose, so score has meaning
  (Geometry Wars, classic arcade; enemy density/speed ramp unboundedly, player growth fixed).
- **Timed endpoint**: settlement or forced termination after a fixed duration, during which the player's growth outruns enemies and delivers power fantasy
  (Vampire Survivors' 30-minute format; endgame enemy HP inflates thousands-fold per minute, Death forces settlement).

**The paradigm choice in §5 is a prerequisite for plans 1/3 in §4 (2026-07-29: A guaranteed-death curve selected).**

## 3. Issue List

### P0 — Endless Mode Unsustainable

| # | Issue | Evidence |
| --- | --- | --- |
| 1 | **Enemy output has zero growth, survival axis has no cap to offset it**: enemy bullet damage is constant; extra_life becomes the only pickable card late-game (others leave the pool at max stacks), estimated +750~1000 HP net per hour; lifesteal healing at the 10% cap snowballs into a positive feedback loop | `enemies.bullet_damage`, `boss.bullet_damage` never consume a ramp; `scripts/buff_select.gd:154-156` max-stack pool removal; `autoload/game_state.gd:498-509` lifesteal |
| 2 | **Event units ignore the difficulty multiplier**: turret HP constant 80, formation fighter HP constant 60 (only multiplied by difficulty tier); after 10 minutes they degrade from pressure sources into free points | `scripts/elite_turret_event.gd:127-132`, `scripts/formation_strike_event.gd:115-126` |

### P1 — Curve Shape

| # | Issue | Evidence |
| --- | --- | --- |
| 3 | The 2^n exponential mult formula: the 4th→5th kill jumps ×4.75 → ×8 (+68%), then caps forever — gentle start / abrupt mid-section / flat tail | `autoload/game_state.gd:470-477` |
| 4 | On hard difficulty buff pacing is paradoxically the fastest (score ×3, milestone thresholds only ×1.5); whether that is intentional is undocumented | `difficulty.*.score` / `.milestone`, `autoload/game_state.gd:331-343` |
| 5 | Difficulty only tracks boss kills: dodging fights stalls difficulty, kill streaks spike it; the time/score axes are entirely unused | `autoload/game_state.gd:470-477` |

### P2 — Config & Copy Decay

| # | Issue | Evidence |
| --- | --- | --- |
| 6 | rapid_fire card says "fire rate +25%", actually interval ×0.75 = +33%/stack | `scripts/player.gd:181-182`, `data/translations.csv:36` |
| 7 | `desc` in the `buff_select.gd` pool is dead text and stale (laser says 10 dmg/10s, actual 16 dmg/8s) | `scripts/buff_select.gd:4-101` vs `scripts/laser_weapon.gd:10-11` |
| 8 | explosive's per-level scaling is unreachable (stack cap locked at 1) | `scripts/bullet.gd:155-166` |
| 9 | The comment at `player.gd:56` mentions a "fuel-tank expansion perk" that has no implementation, likely leftover from an early version | `scripts/player.gd:56,124` |
| 10 | explosive's unlock gate `boss_kills >= 3` is hardcoded, not in balance.json | `scripts/buff_select.gd:145` |

## 4. Improvement Plans (Ranked by Cost-Benefit)

> All numbers go into `data/balance.json`, with script `cfg()` fallbacks kept in sync; the k values in formulas are drafts, to be calibrated by balance tests at implementation.

### Plan 1 — Enemy Damage Ramp (P0-1 Core, Cheapest Way to Restore Tension) [Fully Landed 2026-07-29]

- Enemy bullet/ram damage multiplied by `(1 + k × (difficulty_multiplier − 1))`, suggested k ≈ 0.08 (×1.56 at mult=8);
  or a slow rise by in-run time. Consumption points: `enemy.gd`, `boss.gd`, formation bombs.
- Pair with a real extra_life cap (suggested 10 stacks / 500 total HP) or diminishing returns
  (+50×0.9^n per stack). The current 99-stack cap is already locked by exponential milestone thresholds — it is nominal, so tightening it directly costs no gameplay.
- **Implementation record (2026-07-29, damage ramp)**: the damage ramp landed with k=0.08 — new key `enemies.damage_ramp_factor`,
  `GameState.enemy_damage_ramp()`; enemy bullets split uniformly by faction in `bullet.gd` (covers all enemy/Boss/turret bullet types),
  with ramming (`enemy.gd`/`boss.gd`) and formation bombs (`formation_strike_event.gd`) wired separately.
- **Implementation record (2026-07-29, extra_life tightening)**: cap 99→**10 stacks** (total HP 100+500=600 cap) —
  new key `buffs.extra_life.max_stacks`=10, pool `max` synced, card copy "可无限叠加" → "最多 10 层" (both zh/en columns).
  The survival-axis positive feedback is offset jointly by the tightened HP cap and the unbounded damage ramp of plans 3/4.

### Plan 2 — Event Units Take the Difficulty Multiplier (P0-2, One-Line Change) [Landed 2026-07-29]

- Turret/formation-fighter HP multiplied by `(1 + enemies.hp_ramp_factor × (mult − 1))`, same formula as normal enemies
  (`elite_turret_event.gd:127`, `formation_strike_event.gd:115`).
- **Implementation record (2026-07-29)**: landed, uniformly routed through the new `GameState.enemy_hp_ramp()`.

### Plan 3 — Smoothed Mult Curve, No Hard Cap (P1-3) [Landed 2026-07-29]

- Replace `2^n` with linear or logarithmic (e.g. `1 + 0.5 × boss_kills`);
- Replace the ×8 cap with slow growth (e.g. `8 + 0.2 × (bk − 5)`), keeping a sustained pressure channel for the late game.
- Depends on the §5 paradigm decision: "guaranteed-death" requires removing the hard cap; "timed endpoint" may keep it.
- **Implementation record (2026-07-29)**: D1 chose guaranteed-death, adopting a **fully cap-free linear** scheme —
  `mult = 1 + progression.per_boss_kill(0.5) × boss_kills + time-axis component` (plan 4), computed uniformly in
  `GameState._recompute_difficulty()` (kill-triggered + time-tier-triggered + save-restore recompute);
  the old `2^n + ×8 cap` formula is retired. As mult grows without bound: boss HP scales up in step (the 50s DPS check naturally becomes
  a "flee if you can't kill it" pressure valve), `enemies.hp_ramp`/`damage_ramp`/spawn-interval ramp all gain unbounded pressure channels,
  and player growth (DPS ×9.5, HP 600) is fixed, so the guaranteed-death curve holds.

### Plan 4 — Time/Score Difficulty Factor (P1-5) [Landed 2026-07-29]

- E.g. `mult = f(boss_kills) + elapsed / 600`, low weight suffices to close the "stall by avoiding fights" loophole.
- **Implementation record (2026-07-29)**: the time component steps **quantized** by `progression.time_step_seconds`(30s),
  +`progression.per_ten_minutes`(1.0) every 10 minutes — i.e. `floor(run_time/30) × 0.05`;
  quantization avoids continuous drift (stable HUD, test-pinnable tiers); counts only in-run survival time `run_time` (tree pause excluded),
  so avoiding fights still pressures steadily. New top-level config block `progression` (per_boss_kill/per_ten_minutes/time_step_seconds),
  cached by `_apply_balance()`, recomputed on tier crossing in `_process` and broadcasting `difficulty_changed`.

### Plan 5 — Copy & Config Cleanup (P2, Independent, Can Go First) [Landed 2026-07-29]

- Change the rapid_fire description to 33% (both zh/en columns in translations.csv);
- Delete or update the dead `desc` fields in the `buff_select.gd` pool;
- Move the explosive unlock gate into `balance.json` (e.g. `buffs.explosive.unlock_boss_kills`).
- **Implementation record (2026-07-29)**: all three done — `BUFF_RAPID_FIRE_DESC` changed to 33% in both zh/en;
  all 16 dead `desc` fields deleted from the pool (card text now goes only through the `BUFF_%s_DESC` translation keys — single source of truth);
  `buffs.explosive.unlock_boss_kills`=3 added to config. P2-8/P2-9 cleaned up the same day:
  explosive's unreachable per-level scaling removed (fixed-value wording now matches the card),
  and the dead "fuel-tank expansion perk" comment in `player.gd` replaced with a config-override note.

## 5. Decision Log (Finalized 2026-07-29)

| # | Decision | Conclusion | Impact |
| --- | --- | --- | --- |
| D1 | **Endgame paradigm** | **A. Guaranteed-death curve**: achieved by plans 1+3 removing the hard cap; no new settlement flow needed; fits the arcade score-attack positioning | Plan 3 adopts full cap removal (×8 cap retired); B (timed endpoint) not adopted |
| D2 | Is the hard ×1.5 milestone intentional? | **Yes, deliberate design**: the DIFFICULTY_DEFS comment in `game_state.gd` states "avoid too-sparse buff pacing on high difficulty" | Registered as decision 10 in `docs/archive/PORTING_PARITY.md`; numbers unchanged |

## 6. Implementation Records & Acceptance

- Status: **fully implemented** (2026-07-29) — plans 1 (incl. extra_life tightening), 2, 3, 4, 5 all landed;
  D1/D2 decisions finalized (§5).
- Acceptance (2026-07-29): all 26 assertion scenes 0 FAIL (incl. `--headless --import`, `--quit-after 300`);
  assertions synced: smoke (difficulty multiplier now a dynamic expectation), difficulty (new progression curve §4b five assertions + interval section pins time tiers),
  enemy_combat (flee section pins time tiers), hit_logic (A4 enemy bullet damage fully dynamic expectation, same convention as boss_pattern).
- Long-run probe (2026-07-29, `--autoplay-seconds=300 --seed=20260729`): **0 exceptions**;
  at 300s difficulty multiplier ≈ ×2.5 (2 kills ×2.0 + 10 time tiers ×0.5), pressure keeps rising with time, no steady-state plateau;
  deeper endless segments (>15 min) on-device calibration deferred (§7).
- Mandatory for balance changes: `balance_test.tscn`, `difficulty_test.tscn`, `boss_enrage_test.tscn`,
  `wave_pacing_test.tscn`; after landing, use `autoplay_test.tscn` long-run probes to confirm the late game no longer hits
  the "HP inflates one-sidedly, pressure to zero" steady state (watch boss-kill count and survival time in SUMMARY).
- Run `i18n_test.tscn` when touching P2 copy.

## 7. Maintenance Conventions

- This document covers only "endless-segment balance evolution"; new buffs/new enemies and similar content still go through Phase 2 of `docs/ROADMAP.md`.
- Later calibration (per_boss_kill / per_ten_minutes / ramp coefficient feel-tuning on device) edits the `progression` block
  of `balance.json` directly and appends records to §6 of this document.
