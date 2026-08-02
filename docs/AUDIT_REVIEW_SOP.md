# Audit-Fix SOP (AUDIT_REVIEW_SOP)

> A reusable process distilled from one full parallel audit + defect-fix pass (first version 2026-08-01; instance: `docs/AUDIT_VAULT.md` B-series and the two commits of that batch). For audit tasks with a wide blast radius spanning multiple subsystems, where the repo has both an authoritative design doc and an audit archive as dual sources of truth.
>
> Related: finding registration & fix-effectiveness records → `docs/AUDIT_VAULT.md` (proprietary archive); direction decisions → `docs/ROADMAP.md`; balance keys → `docs/BALANCE_MAP.md` (generated file; re-run the generator after key changes).

---

## 1. Process Overview

```
① Parallel audit   → ② Classification   → ③ Batched commits   → ④ Iterative fix + instant verification   → ⑤ Archive backfill
   Find issues          bug / design / wording       docs debt and code debt separated        test each change, tests falsify        register → fix → backfill
```

The gist in one sentence: **parallel division of labor × cross-validation finds issues; classify before fixing (no blind balance changes); commit docs and code in separate batches; let tests falsify assumptions on the spot; close the loop with archive records.**

---

## 2. Phase 1: Parallel Audit (Find Issues)

- **Divide by subsystem, with no overlap**. Example partitions: gameplay orchestration / player & aim assist / spawning & object pools / Boss system / events & shows / balance consistency. Each agent reads only its own area, avoiding multiple lanes re-reading the same file.
- **Each lane compares against its design doc** — this is the key to telling "genuinely met the design goal" from "emergency patch". Examples:
  - Boss system (split as A3) vs `docs/BOSS_REDESIGN.md` → judged a "structural relocation" (match only moved files, 7 machine-type branch remnants), not a genuine O-principle (Open/Closed) achievement;
  - The five show systems vs their design docs → judged genuine, complete implementations.
- **The lead cross-checks in parallel**: agents read code; the lead looks for "doc × code × git history" triangle contradictions. Example: AUDIT_VAULT status table (A4/A6/A8 unfixed) vs git history (fixes already committed) vs ROADMAP (claims all fixed) — three-way contradictions of this kind are invisible to any single lane.
- **Uniform output**: every finding carries severity, file:line, description, category, and evidence. Category is one of four: `pure bug` / `design goal unmet` / `emergency-patch trace` / `doc-code contradiction`.

### Checklist

- [ ] Partition list written; no file-ownership disputes
- [ ] Each lane has its design-doc paths
- [ ] Cross-area doc/code/git triangle contradictions scanned once by the lead
- [ ] Findings logged in the unified format

---

## 3. Phase 2: Classification (Decide Whether to Fix)

**The most-skipped step — first separate "bug" from "design decision"; do not blindly change balance.** Verify each finding against the code + design doc before classifying:

| Verdict | Handling | This instance |
| --- | --- | --- |
| **True bug** | Fix | B1 aim-line leak: no `queue_free` anywhere in the file — confirmed |
| **Design intent** | No code change; backfill conclusion to the archive | B9 Boss HP linear scaling: ENDLESS_BALANCE_PLAN D1 explicitly states "linear Boss + 50s escape pressure valve" |
| **Wording issue** | Add comments / unify docs; no behavior change | B11 mothership margin × ws: the defensible "constant hull-to-screen-edge margin" exception |
| **Doc-code contradiction** | Align docs to code reality | mark_ratio 0.4 vs 0.25: the code was deliberately tuned to 0.25; docs never synced |

### Key Lessons (Always Verify Before Concluding)

- **Value semantics must be checked against the function definition, never judged from comments or appearances.** B9: reading "Boss whole-multiple multiplier, enemy damping ramp" made us want to "unify" — but checking `enemy_hp_multiplier()`'s definition showed it is actually a **difficulty-tier multiplier** (0.75/1/1.5), not a progression ramp; fixing by appearance would have directly broken the balance.
- **"Semantic inconsistency" is not necessarily a bug** — it may be deliberate layered design. Boss linear scaling vs enemy damping is the design choice "the Boss is the pacing anchor, enemies stay within a band".
- **Mathematically unsatisfiable goals** (e.g. a designed value's three tiers cannot all be hit exactly) → prefer tuning parameters to approximate and syncing the docs, rather than forcing compliance.

---

## 4. Phase 3: Batched Commits (Traceable)

- **Docs debt and code debt are committed in two batches**, never mixed:
  1. Batch 1: doc wording unification (stale-doc corrections, archive status fixes, generator blind-spot fixes).
  2. Batch 2: defect fixes.
- **Commit messages itemize everything**: which files changed / why; each fix is tagged with its audit ID (B-series) so it can be traced back to the original finding; clearly distinguish the three classes: "code fix / design confirmed, no change / wording clarification".

---

## 5. Phase 4: Iterative Fix + Instant Verification

- **Run targeted tests right after each fix**, not all at the end. Run the full regression once changes accumulate.
- **Let tests falsify assumptions on the spot; once falsified, don't force-fit — go back to the code for the root cause.** This instance (B4):
  - v1 used `is_active()` as the tracking-target-invalid criterion → `smoke_test` immediately reported "in-bracket fired bullet bound to a tracking target" failing;
  - Investigation: directly-instantiated enemies never go through `reactivate`, so `_active` is always false (a known semantic gap) → switched to a membership check in the `GameState.enemies` registry;
  - Re-run passed. **The test caught a criterion error that reading code alone could not reveal.**
- **After value changes (balance.json), first verify the diff stays minimal**, so generator/script whole-file reformatting doesn't pollute the commit.

### Minimal Verification Set

- After every change: `--headless --import` + the relevant dedicated test.
- Balance values involved: also run `balance_test` (corruption fallback paths).
- Object pools / registry involved: also run `pool_reuse_test`.
- Full wrap-up: **31 assertion scenes all green, 0 FAIL** + `--quit-after 300` + a short `autoplay_test` run (the probe watches registry consistency / orphan nodes / frame cost).

---

## 6. Phase 5: Archive Backfill (Consolidate)

Per `docs/AUDIT_VAULT.md` conventions: register the finding (B-series) → fix → backfill the "fix effectiveness record" (what changed / why it worked / how it was verified).

- Entries confirmed as "design intent, no code change" are clearly marked in the archive, **so later people don't re-investigate them as bugs** (B9/B14 were handled this way).
- After backfilling, sync the status overview and any stale related docs, so the archive doesn't become a contradiction source again.

---

## 7. Anti-Pattern Quick Reference

| Anti-pattern | Right approach |
| --- | --- |
| Single-threaded read-through misses cross-area contradictions | Parallel partitions + lead cross-check |
| "Inconsistency" triggers a balance change | Classify bug / design / wording first; check semantics before deciding |
| Judge value meaning from comments | Look up the function definition and doc formulas |
| Batch all changes, then test once | Run targeted tests after each change |
| Fixes and docs mixed in one commit | Two batches, each with IDs and notes |
| No archive update after fixing | Backfill effectiveness records; keep stale archives from misleading later work |

---

## 8. When It Applies & When It Doesn't

- **Applies**: audits/refactors spanning many files and subsystems; the repo has the "authoritative design doc + audit archive" dual source of truth; changes may affect balance or lifecycles.
- **Doesn't apply**: single-file small fixes (just change and test directly); purely exploratory reading (use an Explore agent rather than the full SOP); fast-iteration periods without design-doc references.
