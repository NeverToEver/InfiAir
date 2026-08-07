# InfiAir Roadmap

> Single source of truth for project direction (founded 2026-07-24). Update on phase/direction changes + register in `AGENTS.md` doc-sync.

## Snapshot (2026-08-07)

- **Porting alignment closed** (2026-07-24): all original `airwar-game` mechanics remade + aligned (gap list: `docs/archive/PORTING_PARITY.md`; only optional "local leaderboard page" left); independent evolution now — original is reference only.
- **Quality**: 47 assertion scenes 0 FAIL (2026-08-07; 权威计数 `docs/TESTING.md`); autoplay probe + perf bench usable.
- **Audit archive** (est. 2026-07-31): `docs/AUDIT_VAULT.md` proprietary; ten audits (A–L) resolved, no P0; A-series all closed (A8 split 2026-08-03, A5 residual dep convergence 2026-08-07). Fix status/efficacy: vault only.
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

**Restart 2026-08-04 (deferred plans, excl. mobile touch)**:
- **Local accounts** (incl. absorbed "leaderboard page" + "Appendix B entry page"): execution checklist `docs/2026-08-04-local-accounts-plan.md`; **landed** — UserDB/PBKDF2, per-user saves/settings, local leaderboard, welcome entry scene, StartPanel retired (T1-T5)
- **Mothership expansion**: `docs/2026-08-04-mothership-expansion-plan.md` (weapon upgrade tiers); **landed** — milestone-gated gatling/missile upgrade
- **Content evolution**: `docs/2026-08-04-content-evolution-plan.md`; **landed** — 3 buffs (crit_shot/shield/bullet_speed) + Splitter enemy + Heavy Turret elite + 4th boss "Eclipse" (ring-weaving mage)
- **Endless k-value calibration**: `docs/2026-08-04-endless-calibration-plan.md`; **landed** — `progression.per_boss_kill` 0.6 / `per_ten_minutes` 1.5 / `hp_ramp_factor` 0.25 / `damage_ramp_factor` 0.20; 3 × 900s probes 0 anomalies, zero-pressure steady state eliminated (ENDLESS_BALANCE_PLAN §6.1)

- **Local accounts**: spec at commit `7aacd3f` (UserDB/PBKDF2/per-user saves; also PORTING_PARITY Appendix B); reuse on restart.
- **Appendix B standalone entry page**: lightweight suffices; restart only if StartPanel overflows; spec in PORTING_PARITY Appendix B.
- **Packaging**: resumed 2026-07-30, proven 07-31 — presets committed (Linux/X11 + Windows Desktop, embedded pck), `release.sh` → `builds/release/` (gitignored), `packaging/` scripts (Linux user-space + .desktop / Windows per-user + Start menu). Platform validation pending.
- **Online leaderboard**: decided NO (2026-07-20); reversal needs explicit override.
- **Collaboration/release engineering** (ex-Phase 1): presets + commands landed 2026-07-30; **CI / contribution guide / CD fully landed 2026-08-02** — CI (import + smoke + 47 assertion scenes, push/PR), `CONTRIBUTING.md` (+ `SECURITY.md`, templates, `CHANGELOG.md`), manual release workflow (export → tag → GitHub Release; version syncs `config/version`). Versioning: MAJOR.MINOR (current 3.28).
- **Content evolution** (ex-Phase 2): cut 2026-07-30 — leaderboard page, new buffs/enemies/elites/4th boss/mobile touch, mothership expansion, endless k-value calibration; **restarted 2026-08-04 (excl. mobile touch) and fully landed** — see Restart block above (3 buffs, Splitter, Heavy Turret, Eclipse boss, mothership upgrade, calibration); **mobile touch restarted 2026-08-07 and landed** — `VirtualControls` 触屏输入层（虚拟摇杆/按钮，Input action 注入）+ 设置「触控」开关 + `virtual_controls_test`（计划/清单 `docs/archive/2026-08-07-deferred-restart-plan.md` §3）。Remaining cut: leaderboard page (absorbed into local accounts).

## Decisions

- **2026-08-05 — 统一实体管理器**：`EntityRegistry` 演进为 `EntityManager`（`docs/ENTITY_MANAGER.md`，Playwright 调研佐证：真实 Godot 项目 underkingdom 的 Autoload EntityManager + 对象池社区指南）——注册样板收敛（`bind_enemy`/`unbind_enemy` 一行，enemy/boss/turret_battery/formation_craft 四处重复消除）+ 生命周期信号（`entity_registered`/`entity_unregistered`，新功能订阅口）+ 批量操作 API（`for_each_enemy`/`clear_enemies`/`count_enemies`，轨道打击清场/母舰索敌/狂暴齐射/spawner 计数迁移）；池化语义/GameState 转发/autoplay 组↔注册表一致性不变；低频实体不池化（社区共识）。
- **2026-08-05 — 统一事件管理器**：全部随机游戏事件（迷雾 4 + 遭遇 2）收敛进 `GameEventManager`（`GameState.events`，`docs/EVENT_MANAGER.md`）——统一 `EVENT_FACTORIES` 注册表 / `fog|encounter` 分组并发 / 触发策略 / 生命周期 / `event_started/ended` 信号；遭遇事件触发移出 spawner（`ScheduledEventTrigger` 退役），`FogEventManager` 重构为迷雾效果层+API 门面（公开 API 不变，fog 测试零改动）。行为保持：迷雾可与遭遇并行；`spawner.set_process(false)` 仍禁用遭遇自动触发；balance key 零变化。
- **2026-08-05 — 不引入 C# 混合编译**：评估 `docs/C_SHARP_ASSESSMENT.md`（实测 perf_bench 1.011ms/帧 ≈989 FPS 等效，性能无瓶颈；仅 Linux/Windows 平台目标，无 Web/移动推力；跨语言继承禁止 + 热路径动态派发 + CI/发布/本地工具链三重成本 > 收益）。维持纯 GDScript。触发条件（性能瓶颈 / 平台需求 / 团队构成 / 架构重写窗口）见评估文档 §8。

## Maintenance

- Phase completion / direction change → update this file; porting-era gap wording archived (PORTING_PARITY frozen 2026-07-30), never rewritten here.
- New defer/restart decisions → Phase 3 with decision date, not scattered.
