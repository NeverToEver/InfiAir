# Content Evolution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Ship the deferred content evolution (ROADMAP Phase 3, cut 2026-07-30, re-scoped): 3 new buffs (crit_shot / shield / bullet_speed), 1 new enemy (Splitter, death-splits), 1 new elite (Heavy Turret), and the 4th boss ("Eclipse", ring-weaving mage) with full pattern/enrage coverage, switching boss rotation to 4 types. No new systems — every item rides existing extension points (declarative tables, registry rows, strategy classes). Mobile touch stays out (excluded by user).

**Architecture:** All content is "add rows + add keys": buffs go through `BUFF_POOL`/`BUFF_EFFECTS` (+2 special consumption points), enemies/elites through spawner static tables + balance index rows, boss through the type-parameter tables (`boss.gd` TEXTURES/DEFAULT_PATTERNS/SUMMONER_TYPES/…), a new attack id in `boss_attacks.gd` + `ATTACK_TELLS`, a new mover in `boss_movement.gd`, a new enrage handler in `enrage_sequence.gd`, rotation `%3` → `%4` in `spawner.gd`. Tests: extend architecture assertions (`boss_registry_test` loops, `buff_effects_test` size) and add per-content scenario assertions.

**Tech Stack:** Godot 4.6 GDScript; balance.json via `balance_editor.py`; procedural sprites via existing `scripts/tools/generate_*_sprites.py` family (match current asset style).

## Global Constraints

- 5-layer gate green; new `.gd` files gdformat-formatted; every new attack id MUST register in `_attack_handlers` + `ATTACK_TELLS` (boss_registry_test intercepts), every new buff in BUFF_POOL + translations + balance (buff_effects_test/buff_panel_test intercept).
- balance.json edits via `balance_editor.py`; new cfg keys → `gen_balance_map.py`; script defaults double-written with JSON.
- New content must not break: existing tell/fairness conventions (each attack has a unique tell), phase shift cleanup, enrage lock (30% HP), 50s escape, difficulty scaling (`difficulty_scaling.counts` per attack).
- Zh+en text for all new buff names/descs and any new labels.
- Boss count invariant: HP segments 100-70-30%, per-segment pattern loops, 0.6s phase-shift show, enrage 5-substate machine — follow `docs/BOSS_REDESIGN.md` design language.

---

## Part 1 — New buffs

### Task 1.1: `bullet_speed` (pow buff)

**Files:**
- Modify: `scripts/buff_select.gd` (BUFF_POOL row), `data/balance.json` (`buffs.bullet_speed`), `data/translations.csv` (`BUFF_BULLET_SPEED_NAME/DESC` zh+en), `scripts/player.gd` (BUFF_EFFECTS row + apply at fire spawn), `scripts/ui_buff_icons.gd` (color_for class), `test/buff_effects_test.gd` (size 8→9 + cfg assertion)

- [ ] **Step 1: Add buff row** `{"id": &"bullet_speed", "max": 3}`; balance `"bullet_speed": {"factor": 1.2, "max_stacks": 3}`; BUFF_EFFECTS row `{"kind": "pow", "cfg": "buffs.bullet_speed.factor", "default": 1.2}`; fire spawn applies `bullet_speed * pow(factor, stacks)`
- [ ] **Step 2: Tests** buff_effects_test size → 9, add eval assertion; buff_panel_test regression (new buff shows/selectable)
- [ ] **Step 3: Run + commit** `feat: 新 buff 弹速 bullet_speed——弹速 +20%/层(content evolution 1.1)`

### Task 1.2: `crit_shot` (special: chance ×2 damage)

**Files:**
- Modify: `scripts/buff_select.gd` (pool row), `data/balance.json` (`buffs.crit_shot`), `data/translations.csv` (BUFF_CRIT_SHOT_NAME/DESC), bullet-vs-enemy damage settlement (follow current damage path — likely `scripts/enemy.gd` `take_damage` or bullet side; apply at player-bullet hits only), `scripts/ui_buff_icons.gd`, `test/buff_effects_test.gd` / `test/enemy_combat_test.gd` (new assertion: with N stacks, hit has `chance` to deal ×2 — assert via forced RNG seed or statistical over 100 hits)

- [ ] **Step 1: Pool row** `{"id": &"crit_shot", "max": 3}`; balance `"crit_shot": {"chance": 0.12, "multiplier": 2.0, "max_stacks": 3}`
- [ ] **Step 2: Implement** at player-bullet hit settlement: roll `randf() < chance * stacks` → damage ×2; visual: reuse hit-flash + spawn a crit ping (small existing effect; keep minimal)
- [ ] **Step 3: Tests + commit** `feat: 新 buff 暴击 crit_shot——12%/层 ×2 伤(content evolution 1.2)`

### Task 1.3: `shield` (special: absorbs one hit per layer)

**Files:**
- Modify: `scripts/buff_select.gd` (pool row), `data/balance.json` (`buffs.shield`), `data/translations.csv` (BUFF_SHIELD_NAME/DESC), `scripts/player_damage.gd` (damage intake :45-48 area — shield layers checked before armor/evasion), `scripts/ui_buff_icons.gd`, maybe `scripts/player_buff_visuals.gd` (optional small ring visual), `test/hit_logic_test.gd` (new assertions: hit consumes shield instead of HP; 0 damage while shielded; shield persists across hits until consumed)

- [ ] **Step 1: Pool row** `{"id": &"shield", "max": 2}`; balance `"shield": {"max_stacks": 2}`
- [ ] **Step 2: Implement** `player_damage.gd`: on damage intake, if shield layers >0 → decrement, damage = 0 (still triggers hit-stop/flash? — decide: minimal, no iframe change); layers restored only via buff pick
- [ ] **Step 3: Tests + commit** `feat: 新 buff 护盾 shield——每层吸收一次伤害(content evolution 1.3)`

## Part 2 — New enemy + elite

### Task 2.1: Splitter enemy (death splits into 2 minis)

**Files:**
- Modify: `scripts/spawner.gd` (ENEMY_TYPES row + split flag), `data/balance.json` (`enemies.types[4]` index row — array index alignment is critical), `scripts/enemy.gd` (`die()` :558 branch: on split flag spawn 2 minis at 0.6 scale / half hp, no score, no extra kill), asset (new or reused sprite — match existing generated style), `test/enemy_combat_test.gd` (new assertions: split spawns 2 minis, minis die normally, score awarded once)

- [ ] **Step 1: Balance + table row** (via balance_editor); spawner row with `"split": true`; unlock score: reuse existing `UNLOCK_SCORES` tier (1500+)
- [ ] **Step 2: Implement die() branch** — spawn minis via enemy pool (reuse `_queue_enemy`-adjacent path or direct pool spawn; follow existing minion-summon precedent in boss/summoner code)
- [ ] **Step 3: Tests + commit** `feat: 新敌机 分裂者——死亡分裂 2 小机(content evolution 2.1)`

### Task 2.2: Heavy Turret elite

**Files:**
- Modify: `scripts/spawner.gd` (ELITE_TYPES row: high HP/slow speed, spread+laser bullet mix, hover strategy), `data/balance.json` (`elites.types[3]` index row), asset, `test/enemy_combat_test.gd` or `test/wave_pacing_test.gd` (elite slot spawns new type; existing elite logic is generic — regression + one assertion)

- [ ] **Step 1: Balance + table row** (elite slot frequency unchanged — reuse elite wave path)
- [ ] **Step 2: Regression + commit** `feat: 新精英 重装炮台——高血量慢速弹幕机(content evolution 2.2)`

## Part 3 — 4th boss "Eclipse" (ring-weaving mage)

### Task 3.1: Boss type row + attack + mover + enrage handler

**Files:**
- Modify: `scripts/boss.gd` (TEXTURES + entry, `DEFAULT_PATTERNS[4]`, `SUMMONER_TYPES`/`HIT_FLASH_BY_TYPE` keys, `STRAFE_SPEEDS`/`FIRE_INTERVALS` 4th elements), `scripts/boss_attacks.gd` (new attack id `ring_burst` handler + `ATTACK_TELLS` entry; possibly `ring_weave` for P2 — keep to one new id if patterns can reuse existing cross/sniper3/homing), `scripts/boss_movement.gd` (`_movers` add `4: _move_type4` — center hover with small sine bob, no strafe), `scripts/enrage_sequence.gd` (3 registries add `4:` — type_4 "lunar eclipse": stationary center, counter-rotating double ring + charged ring volley), `data/balance.json` (`boss.hp_mults`→4 entries, `strafe_speeds`/`fire_intervals`→4, `boss.phases.type4`, `boss.movement.type4_*`, `boss.enrage.type_4`, `difficulty_scaling.counts.ring_burst`), `scripts/spawner.gd` (`%3+1` → `%4+1` :585), asset `assets/sprites/boss4.png` (procedural, match current boss art)

**Design (register in BOSS_REDESIGN.md):** Eclipse — slow center-weaver; P1 `ring_burst` (player-centered ring) + homing alternation; P2 `ring_burst` double-ring + cross + sniper3 (0.35s telegraph); enrage "lunar eclipse": snapshot + counter-rotating double ring + 8-way charged ring volley; escape/HP segments/phase shift identical to shared skeleton.

- [ ] **Step 1: Write failing architecture test first** — extend `boss_registry_test.gd` loops 1..3 → 1..4 (movers, enrage handlers, SUMMONER_TYPES, HIT_FLASH, DEFAULT_PATTERNS, strafe/fire arrays) + register `ring_burst` + tell; expect FAIL
- [ ] **Step 2: Implement rows** (boss.gd/movement/attacks/enrage/spawner/balance/asset) until registry test PASS
- [ ] **Step 3: Scenario tests** — `boss_pattern_test.gd` new scenes: Eclipse P1 ring+homing; P2 double-ring+cross+sniper (tell timing); `boss_phase_test.gd` scene: thresholds + phase shift cleanup; `boss_enrage_test.gd` scene: type-4 enrage sequence (lock 30%, double ring, charged volley, return, ×1.3 after)
- [ ] **Step 4: Run full boss suite + smoke + wave_pacing, 0 FAIL**
- [ ] **Step 5: Commit** `feat: 第 4 号 Boss 月蚀——环弹术士(ring_burst/双环狂暴/轮换扩 4 型) + 全量回归(content evolution 3.1)`

## Part 4 — Docs

**Files:**
- Modify: `docs/BOSS_REDESIGN.md` (Eclipse design + params), `docs/CHANGELOG.md`, `docs/ROADMAP.md` (Phase 3 content row → landed), `docs/BALANCE_MAP.md` (regenerated), `docs/ARCHITECTURE.md` (buff/enemy/boss tables updated), maybe `docs/DESIGN_BASELINE.md` (new buffs/boss registered in relevant sections)

- [ ] **Step 1: Regenerate BALANCE_MAP** via `gen_balance_map.py`; write docs
- [ ] **Step 2: Commit** `docs: 内容演化落地文档同步(BOSS_REDESIGN/CHANGELOG/ROADMAP/BALANCE_MAP/ARCHITECTURE) [skip ci]`

---

## Acceptance

- All new content playable in a real run (manual windowed check): buffs pickable with zh/en text; Splitter spawns/splits; elite appears in special slots; Eclipse appears in rotation (4th slot), P1/P2/enrage/escape behave, unique tell visible
- Full gate: 46+ scenes 0 FAIL; architecture assertions extended and green
