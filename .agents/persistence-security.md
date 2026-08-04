# Persistence & Security

## Overview

Save/profile files, corruption recovery, and the security posture (no networking/credentials). Applies to persistence code and any data the game writes.

## Rules

- Logged-in run save `user://savegame_<sanitized>_<sha256[:12]>.json` (per-user, owner-checked); user table / per-user settings & stats / local leaderboard in `user://users.json`; guests don't save (memory only). Legacy `user://profile.json` retired after migration (merged into first registered user, then deleted).
- Corrupt JSON isolated as `<file>.corrupt`, notified to start screen via `save_corrupt`/`profile_corrupt`. Don't bypass recovery.
- No networking, third-party plugins, remote services, keys, or credentials. Only local `user://` persistence + offline asset generation. `balance_editor.py` listens on 127.0.0.1 only; not runtime.
- `.gitignore` excludes import cache & exports (`builds/` etc.). `export_presets.cfg` re-committed 2026-07-30 — preset changes must review `release.sh` + `packaging/`. Future CI/deploy additions: reviewable workflows + release notes first, then document in the entry docs.
