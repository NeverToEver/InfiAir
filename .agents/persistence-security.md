# Persistence & Security

## Overview

Save/profile files, corruption recovery, and the security posture (no networking/credentials). Applies to persistence code and any data the game writes.

## Rules

- Run save `user://savegame.json`, out-of-run profile `user://profile.json`; both managed by GameState with version fields. Profile: high score, local leaderboard, difficulty, keybinds, locale, zoom, window size, tutorial state, joypad params (`joy_aim_speed`/`joy_deadzone`).
- Corrupt JSON isolated as `<file>.corrupt`, notified to start screen via `save_corrupt`/`profile_corrupt`. Don't bypass recovery.
- No networking, third-party plugins, remote services, keys, or credentials. Only local `user://` persistence + offline asset generation. `balance_editor.py` listens on 127.0.0.1 only; not runtime.
- `.gitignore` excludes import cache & exports (`builds/` etc.). `export_presets.cfg` re-committed 2026-07-30 — preset changes must review `release.sh` + `packaging/`. Future CI/deploy additions: reviewable workflows + release notes first, then document in the entry docs.
