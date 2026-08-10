# Doc Sync

## Overview

Where decisions get recorded and how docs stay consistent. Entry docs: root `AGENTS.md` + these `.agents/*` files (linked from it); `CLAUDE.md` = entry overview only. Keep this file map current on structure/config changes.

## Rules

- Direction/phase/pause-resume decisions → `docs/ROADMAP.md` (single source of truth).
- Design intent / play rules / architecture baseline → `docs/DESIGN_BASELINE.md` + affected design docs.
- Back/exit hierarchy, exit cleanup, platform-back handling → `docs/EXIT_FLOW.md` + run back-nav tests.
- New/renamed balance keys or `Cfg()` changes → run `python3 scripts/tools/gen_balance_map.py` to regenerate `docs/BALANCE_MAP.md` (generated file — don't hand-edit).
- **Assertion-scene counts are a single source of truth**: `docs/TESTING.md` "Scene Counts" (dynamic `ls test/*_test.tscn` guidance). **Never hardcode assertion/total scene counts in other docs** — reference TESTING.md (CI run is the actual gate). When adding/removing `test/*_test.tscn`, update TESTING.md counts + scene list.
- **`docs/AUDIT_VAULT.md` (code audit archive) is proprietary — never delete/merge**: logs all code-quality issues, fix guidance, post-fix efficacy records, work time/areas. Append new findings; backfill fix records + update status summary on landing. No cleanup/archive may remove it.
- Completed plans/review docs: move full text to `docs/archive/`, log entry in `docs/archive/EXECUTION_LOG.md` (date/commit/summary/key decisions & lessons/link), delete from `docs/` top level, update references. Archived internal `docs/xxx` links are pre-archive snapshots, may be broken.
- Structure/commands/test-strategy/config-location changes → keep `AGENTS.md` + `.agents/*` current as the true entry docs for agents; architecture/config → `docs/ARCHITECTURE.md`; test commands/strategy → `docs/TESTING.md`.
- CI/CD changes: reviewable workflows + release notes first, then sync the entry docs + `release.sh`; 政策口径: 仅官方 checkout/upload-artifact/download-artifact/cache action + 官方 dotnet-install.sh 脚本 + 官方 Godot 引擎/模板, 禁止其他第三方依赖.
