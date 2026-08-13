# Audit-Review SOP

> Process established from the first parallel-audit + fix cycle (2026-08-01; instance: `docs/AUDIT_VAULT.md` B-series + same-batch commits); since adopted by 8+ audit rounds (AA series, 2026-08-10, still runs 8 parallel tracks); scope: multi-subsystem audits with dual authoritative sources (design docs + audit archive).
> Related: findings/fix records → `docs/AUDIT_VAULT.md` (proprietary); direction decisions → `docs/ROADMAP.md`; balance keys → `docs/BALANCE_MAP.md` (generated; rerun generator after key changes).

## 1. Overview

`parallel audit → classify → batched commits → iterative fix + immediate verify → archive backfill`

## 2. Phase 1: Parallel Audit

- Partition by subsystem, no overlap; each agent reads only its own area.
- Each track audits against its design doc — judges true design-goal match vs emergency patch. Example: boss system (A3 split) vs `docs/BOSS_REDESIGN.md` → "structural relocation" (match = file move only, 7 machine-branch residuals), not true O-principle.
- Coordinator cross-checks the doc × code × git triangle (instance: AUDIT_VAULT status table A4/A6/A8 unfixed vs git fixes vs ROADMAP claims all fixed).
- Uniform output: severity, file:line, description, category, evidence; category ∈ {pure bug, design goal unmet, emergency-patch trace, doc-code contradiction}.

## 3. Phase 2: Classify

Easiest to skip — distinguish bug vs design decision; never blind-tune balance; verify against code + docs:

| Verdict | Action | Instance |
| --- | --- | --- |
| True bug | Fix | B1 aim-line leak: no `queue_free` in file |
| Design intent | No code change; backfill in archive | B9 boss HP linear scaling: ENDLESS_BALANCE_PLAN §4 Plan 3 落地注记 "boss linear + 50s escape pressure valve" |
| Calibration (口径) | Comment/unify docs; no behavior change | B11 mothership margin × ws: constant screen-edge distance |
| Doc-code contradiction | Unify doc to code reality | mark_ratio 0.4 vs 0.25: code deliberately 0.25, doc not synced |

### Lessons

- Numeric semantics from function definitions, not comments: B9 looked like "boss multiplier, enemy damping ramp"; `enemy_hp_multiplier()` is a difficulty-tier multiplier (0.75/1/1.5), not a run ramp — fixing by appearance breaks balance.
- "Inconsistency" ≠ bug; may be deliberate layering (boss = pacing anchor, enemies in band).
- Unsatisfiable targets → tune params to approximate + sync docs, don't force-fit.

## 4. Phase 3: Batched Commits

- Docs and code commit separately: batch 1 = doc calibration (stale docs, archive status, generator blind spots); batch 2 = fixes.
- Commit messages: files / why; each fix tagged with its B-series number; label: code fix / design-confirmed-no-change / calibration.

## 5. Phase 4: Iterative Fix + Immediate Verify

- Targeted test after each fix, not at the end; full regression after changes accumulate.
- Let tests falsify assumptions; if falsified, find root cause. Instance (B4): v1 used `is_active()` as target-invalid criterion → `smoke_test` failed; root cause: instanced enemies never pass `reactivate`, `_active` stays false (known gap) → switched to `GameState.enemies` registry membership; rerun passed.
- After balance.json changes, verify minimal diff (no generator/script reformat pollution).

### Minimal verification set

- Every change: `--headless --import` + targeted tests; C# touched → + `dotnet build` (zero warnings) + `dotnet test tests-csharp/` (+ `dotnet format --verify-no-changes`, mirrors CI format gate); balance touched → + `balance_test` (corrupt-fallback); pools/registries → + `pool_reuse_test`; close-out: full assertion-scene set, 0 FAIL (count authoritative in `docs/TESTING.md`) + `--quit-after 300` + short `autoplay_test` (registry / orphans / frame time).
- C# 专项门禁:Roslynator 静态分析 (`PATH=~/.dotnet:$PATH DOTNET_ROOT=~/.dotnet tools/roslynator/roslynator analyze InfiAir.csproj`;AA 系列实践,口径见 `.agents/csharp-conventions.md`).
- CI/headless 错误日志扫描含 `Unhandled exception`(W 系列实践;error-level 零容忍).

## 6. Phase 5: Archive Backfill

Per `docs/AUDIT_VAULT.md`: register (B-series) → fix → backfill record (what / why / how verified).

- Design-intent-no-change entries marked in archive so readers don't re-investigate as bugs (B9/B14).
- After backfill, sync status overview + stale docs; keep archive from becoming a contradiction source.

## 7. Applicability

- **Applies**: multi-file/multi-subsystem audits/refactors; dual authoritative sources; may affect balance or lifecycle.
- **N/A**: single-file fixes (fix + test directly); pure exploration (Explore agent, not full SOP); fast iteration without design docs.
