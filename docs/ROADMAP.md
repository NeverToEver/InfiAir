# InfiAir Roadmap

> Single source of truth for project direction (founded 2026-07-24). Update on phase/direction changes + register in `AGENTS.md` doc-sync.

## Snapshot (2026-08-03)

- **Porting alignment closed** (2026-07-24): all original `airwar-game` mechanics remade + aligned (gap list: `docs/archive/PORTING_PARITY.md`; only optional "local leaderboard page" left); independent evolution now — original is reference only.
- **Quality**: 37 assertion scenes 0 FAIL (2026-08-03); autoplay probe + perf bench usable.
- **Audit archive** (est. 2026-07-31): `docs/AUDIT_VAULT.md` proprietary; ten audits (A–L) resolved, no P0; A-series open: A5 (residual dep convergence) + A8 (Player visual split). Fix status/efficacy: vault only.
- **Collaboration ready**: privacy audit passed (no keys/PII, history cleaned); UI font → OFL NotoSansSC; doc baseline (README/AGENTS/PORTING_PARITY/EXIT_FLOW) line-checked vs code.
- **Four fairness mechanics landed** (2026-08-03, `docs/archive/2026-08-03-combat-fairness-plan.md`; values final in `DESIGN_BASELINE.md` §1.13): hit grace frames, graze scoring, boss transition clear + brief invincibility + segmented bar, F parry shield (3.8s cycle). Validation: 37 scenes 0 FAIL + 180s autoplay no new anomalies; on-device feel (15+ min run) = pre-release manual item. **B-tier landed 2026-08-03**: per-attack tells, DDA density downshift (score-fair), death replay (3s ghost replay). Next: on-device feel validation.
- **Phase 0 closed** (2026-08-03): test/ gate blind spot fixed (test/ into gdformat/gdlint, CI compile probe + per-scene timeout; L15/L16), L18 release.yml version commit, P2 cleanup (ACTION_LABELS/back_pressed/profile_corrupt toast), L13 mothership×event mutex, L14 boss phase-shift y smooth transition, **A8 PlayerVisuals split** (last architecture debt). Records: `AUDIT_VAULT.md` Phase 0 batch + `docs/archive/EXECUTION_LOG.md`.

## Direction Shift

| Dimension | Past (~3.13) | Future |
| --- | --- | --- |
| Goal | per-item parity | independent evolution; original = reference |
| Mode | solo | collaborative (repo ready) |
| Release | packaging deferred | resumed 2026-07-30 (presets + `release.sh` + scripts); CI/CD 2026-08-02 (5-layer gate + manual release) |
| Content | mechanic completion | keep current; depth/new content cut 2026-07-30 — restart needs re-scoping |

## Phases

### Phase 0 — Tech-debt finish (near term, no new gameplay)

**Done (2026-07-31~08-03; items in `AUDIT_VAULT.md` A-series + `archive/EXECUTION_LOG.md`)**: spawn path unified to pool (`920e5e9`), A2 4-service split, A3/A4 registry + declarative effect table (`310e0b9`), four fairness mechanics (`b2bc8a5`), CI/CD + 5-layer gate.

**Closed 2026-08-03 (Phase 0 batch, `AUDIT_VAULT.md`)**:
- **test/ gate blind spot**: test/ into `gdformat --check` + `gdlint` (23 files formatted, 18 lint issues fixed); CI compile probe step (every `test/*.tscn` `--quit-after 2` + error grep) + per-scene 300s timeout — L01a/L01b-type blind spots can no longer linger. L15 profile snapshot/restore (20 scenes), L16 weak assertion fixed.
- **L18**: release.yml commits `config/version` before tagging (tag carries the version commit).
- **P2 cleanup**: `ACTION_LABELS` dead dict removed, `back_pressed` dead signal documented (E13 precedent), `profile_corrupt` toast consumed in start panel (+`START_PROFILE_CORRUPT` key).
- **L13**: mothership in-field mutex for elite-turret/formation events (group registration; charge ghost excluded from group).
- **L14**: boss P1→P2 phase shift y smooth transition (0.6s ease-out to anchor; `reset_press` clears type-3 band too; boss_phase_test assertions updated).
- **A8**: `PlayerVisuals` extracted (RefCounted composition, same as PlayerDamage/PlayerDash/PlayerParry) — tail/afterimage/body tint/hitbox dot/parry visuals/graze flash out of player.gd. Last open architecture debt.

**Acceptance**: all tests 0 FAIL (37 scenes; boss_enrage one-off flake reruns clean); items marked done in audit docs.

### Phase 3 — Deferred/cut (restart needs explicit decision)

- **Local accounts**: spec at commit `7aacd3f` (UserDB/PBKDF2/per-user saves; also PORTING_PARITY Appendix B); reuse on restart.
- **Appendix B standalone entry page**: lightweight suffices; restart only if StartPanel overflows; spec in PORTING_PARITY Appendix B.
- **Packaging**: resumed 2026-07-30, proven 07-31 — presets committed (Linux/X11 + Windows Desktop, embedded pck), `release.sh` → `builds/release/` (gitignored), `packaging/` scripts (Linux user-space + .desktop / Windows per-user + Start menu). Platform validation pending.
- **Online leaderboard**: decided NO (2026-07-20); reversal needs explicit override.
- **Collaboration/release engineering** (ex-Phase 1): presets + commands landed 2026-07-30; **CI / contribution guide / CD fully landed 2026-08-02** — CI (import + smoke + 37 scenes, push/PR), `CONTRIBUTING.md` (+ `SECURITY.md`, templates, `CHANGELOG.md`), manual release workflow (export → tag → GitHub Release; version syncs `config/version`). Versioning: MAJOR.MINOR (current 3.26).
- **Content evolution** (ex-Phase 2): cut 2026-07-30 — leaderboard page, new buffs/enemies/elites/4th boss/mobile touch, mothership expansion, endless k-value calibration (plan landed, `ENDLESS_BALANCE_PLAN.md`); restart needs re-scoping + registration here.

## Maintenance

- Phase completion / direction change → update this file; porting-era gap wording archived (PORTING_PARITY frozen 2026-07-30), never rewritten here.
- New defer/restart decisions → Phase 3 with decision date, not scattered.
