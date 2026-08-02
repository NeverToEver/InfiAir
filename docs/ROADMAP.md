# InfiAir Roadmap (Future Directions & Initial Plans)

> Initiated 2026-07-24. This document is the single source of truth for project direction; when phases change, update this document and register in the "Document Sync Requirements" section of `AGENTS.md`.

## Current Snapshot (2026-07-24)

- **Porting alignment closed out** (2026-07-24 snapshot): all core mechanics of the Python/Pygame original rewritten and aligned (item-by-item comparison in the gap list of `docs/archive/PORTING_PARITY.md`; only the optional "local leaderboard page" remains); **post-closeout the project evolves independently — the original serves only as historical / balance reference, no longer an item-by-item alignment target**.
- **Quality baseline**: 31 headless assertion test scenes all green (1113 assertions, incl. `entry_animation_test`/`mouse_lock_test`); long-run autoplay probe + performance benchmark available.
- **Code audit vault (established 2026-07-31)**: `docs/AUDIT_VAULT.md` is the proprietary audit vault registering SOLID review findings A1–A8. **Fix status (corrected 2026-08-01 per git history and current code, see A-series backfill in the vault; A5 re-corrected 2026-08-02)**: A1 encapsulation breach ✅, A2 GameState four-service split ✅, A3 Boss four-class split ⚠️ split landed but O principle not achieved (match only relocated), A4 open/closed violation ⚠️ partially done (A4a enemy strategies / A4b event trigger base class landed; Boss branches and Player buffs unmanaged), A5 dependency inversion ⚠️ partially done (Boss/event Spawner dependency injection landed; GameState as config center + registry kept as intentional performance trade-off), A6 semantic special-casing ✅, A7 test white-boxing fully cleaned ✅ (vault baseline: 28 test-side + 5 game-side; 855 is the sed batch-replacement count), A8 Player component split ⚠️ partially done (PlayerDamage/PlayerDash extracted, visuals not). A parallel re-review on 2026-08-01 additionally registered B1–B16 (see vault).
- **Collaboration ready**: privacy isolation audit passed (no secrets/personal-info leaks, git history scrubbed), UI font replaced with OFL-licensed NotoSansSC, documentation baselines (README / AGENTS / PORTING_PARITY / EXIT_FLOW) verified item-by-item against code.

## Direction Changes

| Dimension | Past (~3.13) | Future |
| --- | --- | --- |
| Goal | Align with the original item-by-item, eliminate porting gaps | Evolve independently along the remake's own route; original only as balance/design reference, no longer line-by-line alignment |
| Dev mode | Solo development | Collaborative development (repo ready for contributors) |
| Distribution | Packaging explicitly deferred | 2026-07-30 packaging restarted: export presets committed + `release.sh` dual-platform export + Linux/Windows install/uninstall scripts; CI and semantic versioning still deferred |
| Content | Mechanic completion | Keep current content; experience deepening & new content cut with Phase 2 on 2026-07-30, restart requires new approval |

## Phase Plan

### Phase 0 — Technical-debt closeout (near term, no new gameplay)

- Audit P2 backlog cleanup (`docs/archive/2026-07-22-audit-fix-plan.md`): dead code removal (unused references in `main.gd`, always-false branches in `hud.gd`, zero-connect signals etc.), mothership `_start_release()` idempotency guard, `profile_corrupt` corrupt-profile prompt consumption. **Status (2026-08-02)**: several items already covered by later audit rounds (C21 pool `_exit_tree` registration cleanup, several D-series items); remaining items still listed in `docs/DESIGN_BASELINE.md` §7.3.
- Enemy spawn path unification: normal waves instantiate directly vs Boss-3 minions through `enemy_pool`, two paths coexist. **✅ Unified (2026-08-02, with performance optimization plan `920e5e9`)**: normal waves now uniformly pooled via `GameState.enemy_pool.spawn()`, `USE_POOL` switch kept as A/B comparison (see `docs/archive/2026-08-02-performance-optimization-plan.md`).
- **A2 GameState split** (2026-07-31 **fully complete**, `docs/AUDIT_VAULT.md` A2): all four stages of delegated extraction landed — ①balance config reads `BalanceService` → ②persistence `SaveManager` → ③SFX pool `SfxPlayer` → ④entity registry `EntityRegistry`. GameState public API kept and forwarded (registries via property getters/setters); callers and tests unaffected; 29 assertion scenes all green, balance map 0 mismatches, direct file IO in `game_state.gd` down to zero.
- **A3 Boss single-class split** (2026-07-31 **split landed**, `docs/AUDIT_VAULT.md` A3): 1488-line single class split into facade Boss + 4 responsibility classes (`BossFire` bullet patterns / `BossAttacks` attack state machine / `BossMovement` movement strategies / `EnrageSequence` enrage state machine); `boss.gd` slimmed to 802 lines, no cross-class private-access regressions; 29 assertion scenes all green. **Correction (2026-08-01)**: the central match was only relocated into `BossAttacks.execute()`, not replaced by table/factory; 7 type branches remain across BossMovement/EnrageSequence/Boss, O principle not achieved (see A3 re-review correction).
- Acceptance: all existing tests 0 FAIL; changed items marked complete in the audit vault.

### Phase 3 — Restart conditions for deferred/cut items (all require explicit user decision)

- **Local account system**: full spec archived in commit `7aacd3f` (login system project, UserDB/PBKDF2/per-user save isolation, written into porting plan appendix B; spec also in `docs/archive/PORTING_PARITY.md` appendix B); fully reusable on restart.
- **Appendix B standalone main-scene entry page**: the lightweight approach suffices; only restart if the start panel can no longer hold new entries; spec in `docs/archive/PORTING_PARITY.md` appendix B.
- **Packaging & distribution**: restarted 2026-07-30, ran through 2026-07-31 — `export_presets.cfg` (Linux/X11 + Windows Desktop, embedded pck single file) committed, `release.sh` one-click export & package (artifacts in `builds/release/`, gitignored locally), `packaging/` provides dual-platform install/uninstall scripts (Linux user-level + .desktop entry / Windows per-user + Start Menu shortcut). Install scripts and real-machine runs await platform verification.
- **Online leaderboard**: decided against (2026-07-20); reversing requires explicitly overturning that decision.
- **Collaboration & release engineering** (formerly Phase 1): export presets committed and export commands landed with the packaging restart (2026-07-30); **CI / contribution guide / versioned releases (CD) all landed 2026-08-02** — CI: `.github/workflows/ci.yml` (headless import + main-scene smoke + full 31-assertion-scene regression, push/PR triggered); contribution guide: `CONTRIBUTING.md` at repo root (plus `SECURITY.md`, issue/PR templates, `CHANGELOG.md`); manually-triggered release workflow `.github/workflows/release.yml` (dual-platform export & package → tag `v<version>` → create GitHub Release; input version auto-syncs `project.godot` `config/version`). Version numbering keeps the MAJOR.MINOR increment convention (currently 3.26).
- **Content evolution** (formerly Phase 2): cut by decision on 2026-07-30, incl. local leaderboard page, new-content candidates (new buff categories, new enemy/elite types, 4th Boss, mobile touch controls), mothership gameplay extensions, endless-segment k-value field calibration (plan fully landed, see `docs/ENDLESS_BALANCE_PLAN.md`); restarting any item requires new approval and registration here first.

## Maintenance Conventions

- Phase completion / direction change → update this document; porting-era gap baselines archived with `docs/archive/PORTING_PARITY.md` (frozen 2026-07-30), no longer written back.
- New defer/restart decisions → record in Phase 3 with decision dates, not scattered in other documents.
