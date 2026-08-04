# Mothership Expansion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Ship the deferred mothership expansion (ROADMAP Phase 3 "mothership expansion", cut 2026-07-30, re-scoped here): the mothership's fire platform upgrades with run progress — gatling/missile damage +50% and fire rate +25% once the run crosses the milestone threshold, reinforcing its late-run "fire platform" identity without touching the summon/dock economy.

**Architecture:** Purely additive config + one scaling factor applied at fire time. `mothership.gd` reads a new `mothership.upgrade` balance section at `_ready`; the STAY fire functions (`_update_gatling`/`_update_missiles`) multiply damage/interval by the tier factor derived from the current milestone count. No new states, no HUD change beyond a dock hint line.

**Tech Stack:** Godot 4.6 GDScript; values via `data/balance.json` (edit with `scripts/tools/balance_editor.py`), script defaults double-written (project invariant).

## Global Constraints

- 5-layer gate green; new `.gd` files gdformat-formatted.
- `balance.json` edits via `balance_editor.py` (backup+validate); new cfg keys → re-run `scripts/tools/gen_balance_map.py` to sync `docs/BALANCE_MAP.md`; script defaults must mirror JSON (asserted by audits).
- Tier thresholds/values go in balance (`mothership.upgrade`), NOT hardcoded.
- Zh+en text via translations.csv if any new hint string is added.
- Do not touch: summon charge, dock time (`mag_cells`), early-depart discounts, score ×1/3 — those are tuned economics, out of scope.

---

## Task 1: Upgrade tier configuration + scaling

**Files:**
- Modify: `data/balance.json` (new `mothership.upgrade` section), `scripts/mothership.gd` (`_ready` reads :150-166, fire functions `_update_gatling` :605 / `_update_missiles` :640, `_live_targets` :589 untouched)
- Test: `test/mothership_summon_test.gd` (extend), `test/balance_test.gd` (regression)

**Interfaces:**
- Consumes: `GameState.cfg("mothership.upgrade.*", default)`; milestone count via existing GameState milestone counter (confirm exact accessor during implementation, e.g. `GameState.milestone_count` or equivalent).
- Produces: `Mothership.tier() -> int` (0 or 1): 1 when `milestones >= mothership.upgrade.threshold`; `damage_mult()`/`interval_mult()` returning the tier factor (1.0 at tier 0).

- [ ] **Step 1: Write failing test** (extend `mothership_summon_test.gd` or add `mothership_upgrade_test.tscn` — prefer extension, keep scene count stable)
  - Prefer a focused new test scene `test/mothership_upgrade_test.tscn`: instance main.tscn, login-guest entry, force milestone count ≥ threshold via GameState (setter used by existing milestone tests), summon mothership (test hooks), assert gatling/missile damage values applied to fired bullets carry tier scaling (`damage * 1.5`), interval scaled (`interval * 0.8`)
- [ ] **Step 2: Run, expect FAIL** (no `mothership.upgrade` cfg keys; defaults return 1.0, assertions fail)
- [ ] **Step 3: Implement**
  - `data/balance.json`: add
    ```json
    "upgrade": { "threshold": 5, "damage_mult": 1.5, "interval_mult": 0.8 }
    ```
    under `mothership` (via `balance_editor.py`)
  - `mothership.gd` `_ready`: read `_upgrade_threshold/_upgrade_damage_mult/_upgrade_interval_mult` with script defaults (5 / 1.5 / 0.8) — double-write invariant
  - `tier()`: `GameState milestone counter >= threshold ? 1 : 0`; `damage_mult()`/`interval_mult()` return tier factor or 1.0
  - `_update_gatling`: `damage = base_damage * damage_mult()`; interval `base_interval * interval_mult()` (applied where bullets are spawned :627)
  - `_update_missiles`: same at :657
- [ ] **Step 4: Run** `mothership_upgrade_test` + `mothership_summon_test` + `smoke_test` (mothership assertions) → 0 FAIL
- [ ] **Step 5: Sync map + docs** `python3 scripts/tools/gen_balance_map.py` (regenerate BALANCE_MAP.md), commit
  `feat: 母舰火力随里程碑升级——加特林/导弹 5 里程碑后 ×1.5 伤/+25% 射速(mothership expansion T1)`

## Task 2: Dock hint + docs

**Files:**
- Modify: `data/translations.csv` (new `MS_UPGRADED` zh/en hint shown while docked at tier 1), `scripts/mothership.gd` or `scripts/hud.gd` (dock state line consumer — follow existing dock-status display pattern), `docs/CHANGELOG.md`, `docs/ROADMAP.md` (Phase 3 row → landed)

- [ ] **Step 1: Add `MS_UPGRADED` key** zh/en; show in dock status hint only when `tier() == 1`
- [ ] **Step 2: Docs** CHANGELOG entry + ROADMAP Phase 3 mothership row update
- [ ] **Step 3: Full gate** + commit `docs: 母舰升级档位提示 + CHANGELOG/ROADMAP 登记 [skip ci]` (docs-only → `[skip ci]`)

---

## Acceptance

- Tier factor applied to gatling + missile damage/rate at ≥5 milestones; unchanged below
- `mothership_summon_test` + new `mothership_upgrade_test` + smoke 0 FAIL; BALANCE_MAP.md regenerated
- Manual windowed check: dock hint shows upgrade state
