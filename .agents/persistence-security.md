# Persistence & Security

## Overview

Save/profile files, corruption recovery, and the security posture (no networking/credentials). Applies to persistence code and any data the game writes.

## Rules

- Logged-in run save `user://savegame_<sanitized>_<sha256[:12]>.json` (per-user, owner-checked); user table / per-user settings & stats / local leaderboard in `user://users.json`; guests don't save (memory only). Pre-registration sessions still read/write legacy `user://profile.json` (compat path); first registration migrates + merges it into the new account, then deletes it.
- Corrupt JSON isolated as `<file>.corrupt`, notified to start screen via `GameState.SaveCorrupt`/`ProfileCorrupt`/`UserDbCorrupt` flags. Don't bypass recovery. Note (2026-08-06 audit, resolved 2026-08-10): `SaveCorrupt`/`ProfileCorrupt` cover the run save and `profile.json`; a corrupt `users.json` is rebuilt as an empty DB (`UserDb.EnsureLoaded()` in `csharp/core/Storage/UserDb.cs`) — now surfaced via `UserDbCorrupt` + start-screen notice (`Welcome.cs`), so accounts vanishing is no longer silent.
- No networking, third-party plugins, remote services, keys, or credentials. Only local `user://` persistence + offline asset generation. `balance_editor.py` listens on 127.0.0.1 only; not runtime.
- `.gitignore` excludes import cache & exports (`builds/` etc.); `.gitattributes` (2026-08-06, per official VCS page) normalizes EOL — `text=auto eol=lf`, `*.bat` checkout CRLF, `*.sh` forced LF; `builds/.gdignore` keeps export outputs out of the editor filesystem. `export_presets.cfg` re-committed 2026-07-30 — preset changes must review `release.sh` + `packaging/`. Future CI/deploy additions: reviewable workflows + release notes first, then document in the entry docs.
