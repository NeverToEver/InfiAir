# Balance & Config

## Overview

Where tunables live, how to read them, and the `world_scale` hull-scaling lever. Applies whenever gameplay numbers or config access are involved.

## Rules

- Change tunables only in `data/balance.json`, not script fallbacks (script defaults must match for missing/corrupt JSON). Prefer `scripts/tools/balance_editor.py`; after edits run `scripts/tools/gen_balance_map.py`, check `docs/BALANCE_MAP.md` mismatch sections ("json 中存在但脚本未静态引用的键" / "脚本引用但 json 缺失的键") for new mismatches.
- Read nested config via `GameState.Cfg("player.fuel.drain", default)`. Cache in `_Ready()`/init; never per-frame JSON lookup on hot paths. **Type-checked reads (2026-08-11, `CfgFx`)**: scalar keys needing判型+钳制 use `CfgFx.Float/Int(path, def, min, max)` — single判型口径 (PathResolver bad-type fallback first, 判型 second); new scalar reads prefer CfgFx over raw `Cfg().AsDouble/AsInt64` (see `docs/ARCHITECTURE.md` CfgFx).
- `GameState` loads balance.json at startup; missing/unparseable → script defaults.
- Single hull-scaling lever: top-level `world_scale` in balance.json (current 0.4; cached `GameState.WorldScale`). Hull-size family (sprite scale, collision radius, muzzle/dock/turret/tow offsets, bullet/explosion/gate/laser fx) stored as **design values** (1.0 baseline) in json/tscn/script fallbacks; entities apply `* world_scale` in `_Ready()`/`Setup()`. Gameplay-range family (AoE, lock/clear radius, slow ring) & indicators/cutscenes/UI don't scale. Classify new size values; never bypass the lever with literal runtime values.
- Idempotent assignment (`radius = design * world_scale`), never `*=` (sub_resources like CircleShape2D shared across instances compound per instance). Runtime-resized shapes (enemy.tscn — normal vs elite radii differ) need `resource_local_to_scene = true`.
