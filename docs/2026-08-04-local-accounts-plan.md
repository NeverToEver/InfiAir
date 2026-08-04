# Local Accounts System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the deferred local accounts system: UserDB (PBKDF2 + per-user saves/settings + local leaderboard), welcome scene as the new entry scene (absorbs the deferred "Appendix B standalone entry page" and "local leaderboard page" items), StartPanel retirement, profile.json migration.

**Architecture:** Follows spec `docs/archive/PORTING_PARITY.md` Appendix B (B1–B8, login-system version archived at commit `7aacd3f`). `UserDB` is a RefCounted service held by `GameState` (A2 service pattern — keeps the single-autoload invariant), not a new autoload. `scenes/welcome.tscn` becomes `run/main_scene`; `main.tscn` stays the battle scene, explicitly instanced by tests as today.

**Tech Stack:** Godot 4.6 GDScript, `Crypto.hmac_digest` (self-implemented PBKDF2-HMAC-SHA256), `FileAccess` atomic write (SaveManager pattern), existing ChamferedPanel/UITheme components.

## Global Constraints

- 5-layer gate must stay green: `gdformat --check` (w=140) → `gdlint` → engine warnings (error-level zero tolerance) → compile+smoke → all 46 scenes 0 FAIL. New `.gd` files must be gdformat-formatted.
- GameState remains the only autoload. New `class_name UserDB extends RefCounted` lives in `scripts/`, held + API-forwarded by GameState (A2 pattern, same as SaveManager/SfxPlayer/BalanceService/EntityRegistry).
- All game text zh+en via `data/translations.csv` (`keys,zh,en`); new keys prefix `WELCOME_` / `USER_` / `LEAD_`.
- Do not port original bugs: B7-1..B7-13 list is normative (fix all), B7-table "不移植" row (fcntl lock, remote leaderboard) stays out.
- PBKDF2: 16-byte salt hex, iterations per plan: 100_000 measured at 330 ms on dev machine (create+verify), **exceeds the 300 ms judgement line → landed at 50_000 (~165 ms, commit `pending T1`)**. Stored per-user record; local offline file, not a compat constraint.
- `user://users.json` write = tmp+rename atomic (SaveManager pattern); corrupt → backup `users.json.corrupted.<ts>.bak` + reset to empty; never fcntl.
- Tests may touch `user://` files: each new test cleans its own state first (existing convention).

---

## Task 1: UserDB data layer

**Files:**
- Create: `scripts/user_db.gd`
- Test: `test/user_db_test.tscn` + `test/user_db_test.gd`

**Interfaces:**
- Consumes: nothing (pure file layer; `SaveManager`-style atomic write implemented inline).
- Produces: `class_name UserDB extends RefCounted` with:
  - `create_user(name: String, password: String) -> bool` (rejects reserved `_leaderboard`/`Guest`, name/password len 3..16)
  - `verify_user(name: String, password: String) -> bool`
  - `user_exists(name: String) -> bool`
  - `list_usernames() -> Array[String]` (last_login_order desc, then name asc)
  - `get_last_login_user() -> String` ("" if none)
  - `record_login(name: String) -> void` (last_login_order = global max+1)
  - `get_user_data(name: String) -> Dictionary` / `update_user_data(name: String, data: Dictionary) -> void`
  - `update_high_score(name: String, score: int) -> void` (only-if-higher; score clamped ≥0)
  - `get_user_settings(name: String) -> Dictionary` / `update_user_settings(name: String, settings: Dictionary) -> void`
  - `delete_user(name: String, password: String) -> bool` (verify first; also deletes `user://savegame_<sanitized>_<sha256[:12]>.json` for that user — B7-12)
  - `submit_score(name: String, score: int) -> int` (leaderboard entry `{player_name ≤32, score 负钳0, timestamp UTC ISO}`; cap 10, sort score desc + timestamp asc; returns 1-indexed rank, 0 = not on board)
  - `get_leaderboard() -> Array[Dictionary]`
  - `savefile_for_user(name: String) -> String` (`user://savegame_<sanitized>_<sha256[:12]>.json`, sanitized = lowercase alnum, sha256 first 12 hex — spec B5)

- [ ] **Step 1: Write failing test** `test/user_db_test.gd` (new tscn, standard harness: `[PASS]`/`[FAIL]` prints + `get_tree().quit(code)`)
  - register → exists → verify ok; wrong password fails; duplicate name fails; short name/password (<3) and long (>16) rejected
  - reserved names `_leaderboard` and `Guest` rejected
  - list_usernames order after two record_login calls (recent first)
  - stats: `update_high_score` only-if-higher; `update_user_data` merges
  - delete_user: wrong password fails; right password deletes + removes that user's savegame file (create `savefile_for_user("tester")` first, assert gone)
  - leaderboard: 12 submissions → cap 10, score desc order, submit_score rank correct (1-indexed), negative score clamped 0
  - corrupt `users.json` (write garbage) → load → file quarantined to `.corrupted.*.bak` + db resets empty
  - Use `user://` root (tests already own user:// state); clean `users.json` + baks at start.
- [ ] **Step 2: Run test, expect FAIL** `godot --headless --path . res://test/user_db_test.tscn` (SCRIPT ERROR: class not found)
- [ ] **Step 3: Implement** `scripts/user_db.gd`
  - `_path := "user://users.json"`, structure `{"_users": {name: {...}}, "_leaderboard": [...]}`
  - PBKDF2-HMAC-SHA256 inline: salt `Crypto.generate_random_bytes(16).hex()`, `password_hex = password.to_utf8_buffer().hex()`, iterate `hmac_digest(HashingContext.HASH_SHA256, key=password_hex, msg=prev)` 100k times, store `password=result.hex(), salt=`; verify with `Crypto.constant_time_compare` (spec: constant-time compare)
  - atomic write: write `users.json.tmp`, then replace (SaveManager lines 24-38 pattern); corrupt load → `DirAccess.rename_absolute` to `users.json.corrupted.<unix_ts>.bak`, reset `{"_users":{}, "_leaderboard":[]}` and write it back
  - store timestamp via `Time.get_datetime_string_from_system(true)` (UTC ISO)
- [ ] **Step 4: Run test, expect PASS + exit 0**
- [ ] **Step 5: Commit** `git add scripts/user_db.gd test/user_db_test.tscn test/user_db_test.gd && git commit -m "feat: 本地账户数据层——UserDB(PBKDF2/users.json/排行榜/删号连档清理) + user_db_test"`
  - (project commit style is English; keep this message English, see existing history) → `feat: UserDB data layer — PBKDF2 users.json, per-user save paths, local leaderboard, delete cascades save (accounts plan T1)`

## Task 2: GameState session, per-user saves/settings, profile migration

**Files:**
- Modify: `autoload/game_state.gd` (profile API :1296-1390, record_score :1394, submit_highscore :1404, highscores_text :1423, save_run :1173, has_save :1195, load_run_data :1199, apply_run_save :1221, delete_save :1287, locale :1060-1069, _ready :393-417, `SAVE_PATH`/`PROFILE_PATH` :230-231, fields :235-296)
- Modify: `scripts/balance_service.gd` (only if `cfg()` needs a users section — not expected)
- Test: `test/base_system_test.gd` (extend), `test/user_session_test.tscn` (new)

**Interfaces:**
- Consumes: `UserDB` API from Task 1.
- Produces (GameState additions):
  - `current_user: String` ("" = none, "Guest" = guest session)
  - `login_user(name: String) -> void` / `login_guest() -> void` / `logout_user() -> void`
  - session-scoped settings accessors: `session_setting(key) -> Variant` / `session_set_setting(key, value) -> void` (login user → persisted to user settings; guest → memory only)
  - save API stays the same signature but routes to `user_db.savefile_for_user(current_user)`; `has_save()`/`load_run_data()`/`delete_save()` same; `load_run_data` validates `username` field matches current user (spec B5)
  - `record_score`/`submit_highscore`/`highscores_text` route to `user_db` (Guest submits as "Guest" — spec B7-8)
  - `game_over_stats(total_kills) -> void`: on death settle — login user only: `update_user_data` total_kills += kills, games_played += 1 (spec B5)

- [ ] **Step 1: Extend tests** `test/user_session_test.tscn` (new): profile.json migration — pre-create legacy `user://profile.json` (high_score/difficulty/locale/key_bindings/view_zoom/tutorial_done), login first user → legacy values merged into that user's settings, profile.json removed; second user starts fresh; guest session: settings memory-only (no file write), save_run refuses (no savegame file created), death stats not written
- [ ] **Step 2: Run, expect FAIL** (GameState has no login API yet)
- [ ] **Step 3: Implement in game_state.gd**
  - Add `var _user_db := UserDB.new()` + `var current_user: String = ""`; forward (A2 pattern) a documented subset: `create_user/verify_user/user_exists/list_usernames/get_last_login_user/record_login/delete_user/submit_score/get_leaderboard/user_db_savefile_for`
  - `_ready`: after `load_profile()`, run legacy migration if `PROFILE_PATH` exists and `list_usernames().is_empty()` → stash legacy dict in `_pending_legacy_profile` (merged at first `create_user` success), then delete profile.json
  - profile fields (:235-296) become session-backed: getters fall back to `session_setting`; `save_profile()` becomes `session_set_setting` fan-out (login users only; guest = memory)
  - `save_run/load_run_data/delete_save/has_save`: if `current_user == ""` → no-op/false (welcome gates entry, but guard anyway); else route to `_user_db.savefile_for_user(current_user)`, save adds `username` field, load rejects mismatch (treat as corrupt → quarantine)
  - `record_score`: login user only → `_user_db.update_high_score`; `submit_highscore(score)` → `_user_db.submit_score(current_user or "Guest", score)`; `highscores_text()` reads `_user_db.get_leaderboard()`
  - locale (`set_locale` :1063): login user → persist via settings + immediate `set_locale` (B7-11); guest → in-memory only
- [ ] **Step 4: Run user_session_test + base_system_test + existing subsystem tests, expect 0 FAIL**
- [ ] **Step 5: Commit** `feat: 每用户会话/存档/设置隔离 + profile.json 迁移 + 死亡统计(accounts plan T2)`

## Task 3: welcome scene, startup flow, StartPanel retirement

**Files:**
- Create: `scenes/welcome.tscn`, `scripts/welcome.gd`
- Modify: `project.godot` (`run/main_scene` → welcome.tscn), `scripts/main.gd` (:22, :97-98, :128-129 StartPanel wiring), `scripts/back_navigator.gd` (:29, :60-61, :84-87, :129-130), `scripts/game_over_ui.gd` (TO_MAIN_MENU target → welcome), `scripts/tutorial.gd` (:423-433 exit → welcome; :99 delete_save semantics), `scripts/settings_ui.gd` (`show_settings(opener)` contract :421/:533-537), `scripts/exit_confirm.gd` (focus-return target)
- Test: `test/welcome_flow_test.tscn` (new), `test/startup_flow_test.tscn` (rewrite: profile-corrupt toast + welcome assertions)

**Interfaces:**
- Consumes: GameState session API (Task 2).
- Produces: `welcome.gd` signals `start_game(difficulty)`, `continue_game()`, `open_tutorial()`, `open_settings()`, public test hooks `press_login/press_register/press_guest/press_delete/press_difficulty/press_leaderboard/press_new_game/press_continue/username_line/password_line/leaderboard_overlay`, `grab_primary_focus()`.

- [ ] **Step 1: Build welcome scene + script** (layout B2, behavior B3, overlay B6, modals B7-1..3/B7-5/B7-6, focus ring username→password→difficulty, ENTER dispatch, ESC ladder per EXIT_FLOW: overlay→confirm→exit app)
  - Left panel: username LineEdit + dropdown, password LineEdit (`secret = true`), Login/Register, Guest entry, Delete user, Settings
  - Right panel: Tutorial CTA (✓ when done), difficulty radio (easy/med/hard, persisted per user), Leaderboard ghost button, key hints, local high-score line
  - Overlays: leaderboard (mask + 520×580 panel, top-10 rows, × close, ESC closes), guest-confirm modal, delete-confirm modal — all mouse+keyboard modal (B7-3)
  - Login success → `GameState.login_user` → `change_scene_to_file(main.tscn)`; guest → `login_guest()` → main
  - Tutorial exit returns to welcome (`change_scene_to_file(welcome.tscn)`); GameOver「回主菜单」 → welcome (not reload); welcome ESC = ExitConfirm (app quit)
  - All text `tr("WELCOME_*")`/`tr("USER_*")` (keys added in Task 5 — use fallback key names meanwhile so tests don't block)
- [ ] **Step 2: Rewire** main.gd (drop StartPanel node refs), back_navigator (top-level = CONFIRM_EXIT still; TO_MAIN_MENU → change_scene_to_file(welcome.tscn)), game_over_ui (menu button target), tutorial (`_exit_tutorial` → welcome), settings_ui (opener contract — welcome passes itself; on close, focus back to welcome), exit_confirm (focus return to welcome)
- [ ] **Step 3: Write welcome_flow_test** — focus ring cycle, last-login prefilled + focus on password, login success → main.tscn, wrong credentials error, register flow keeps username (B7-9), guest confirm modal (default focus 返回, B7-5), delete flow (password required before confirm), ESC ladder, leaderboard overlay opens/refreshes/closes, overlay modal blocks clicks
- [ ] **Step 4: Run** `welcome_flow_test` + `startup_flow_test` (rewritten for welcome) + `back_navigation_test` + `esc_navigation_test` → 0 FAIL
- [ ] **Step 5: Commit** `feat: welcome 主场景——登录/游客/注册/删除/排行榜 overlay/难度/教程,StartPanel 退役(accounts plan T3)`

## Task 4: Existing test adaptation

**Files:**
- Modify: `test/smoke_test.gd` (:28-31 entry template → instantiate main.tscn + login-guest helper), `test/esc_navigation_test.gd` (:50), `test/i18n_test.gd` (:63-69), `test/back_navigation_test.gd`, `test/autoplay_test.gd` (:280-305), `test/visual_capture.tscn`, `test/ui_capture.tscn`, `test/hud_capture.tscn`, `test/meta_fx_capture.tscn`, `test/summon_capture.tscn`, `test/intro_capture.tscn`, `test/return_capture.tscn`

**Interfaces:**
- Consumes: `GameState.login_guest()` (Task 2) + `start_panel` replacement helper.
- Produces: a shared entry idiom `test/_entry.gd`-style helper (or per-test 2-line block): `GameState.login_guest(); var main = load("res://scenes/main.tscn").instantiate()` — guest keeps tests stateless (no savegame/profile pollution).

- [ ] **Step 1: Replace entry templates** in all tests listed — guest login first, then instance main.tscn directly (no StartPanel press_new_game)
- [ ] **Step 2: Run full gate locally** — `gdformat --check autoload/ scripts/ test/` + `gdlint` + `godot --headless --import --path .` + `godot --headless --path . --quit-after 300` + every `test/*.tscn` — fix fallout until 0 FAIL
- [ ] **Step 3: Commit** `test: 测试入口适配 welcome 会话(游客直进 main) + 全量回归 0 FAIL(accounts plan T4)`

## Task 5: i18n + docs sync

**Files:**
- Modify: `data/translations.csv` (WELCOME_*: title/hints/high-score/tutorial/difficulty/leaderboard; USER_*: login/register/guest/delete/password/errors/success; LEAD_*: overlay title/rows/footer/rank), `test/i18n_test.gd` (key coverage), `docs/archive/PORTING_PARITY.md` (#22 row → aligned, B1/B5 status), `docs/ARCHITECTURE.md` (entry scene, autoload note unchanged), `docs/EXIT_FLOW.md` (welcome ladder), `docs/AGENTS.md` (Entry line), `docs/TESTING.md` (scene count 46→N, welcome_flow/user_session tests), `docs/CHANGELOG.md`

- [ ] **Step 1: Add all new keys** zh+en; extend i18n_test coverage
- [ ] **Step 2: Sync docs** per file list; update `docs/ROADMAP.md` Phase 3 rows (accounts/leaderboard/entry-page → landed, decision date)
- [ ] **Step 3: Run** `i18n_test` + full gate; commit `docs: 账户系统落地文档同步(PORTING_PARITY #22/EXIT_FLOW/ARCHITECTURE/AGENTS/TESTING/CHANGELOG/ROADMAP) [skip ci]`

---

## Acceptance

- 5-layer gate green locally; 46+ scenes 0 FAIL (37 assertion + autoplay probe + perf_bench + 7 captures + new welcome_flow/user_session/user_db)
- Dual-user isolation: A's save/settings/high-score don't leak into B; guest leaves zero `user://` writes
- Leaderboard: submit → rank, cap 10, survives restart; delete user cascades savegame (B7-12)
- Manual windowed check: welcome layout, dropdown, modals, ESC ladder (B7-1..3), locale switch immediate (B7-11)
