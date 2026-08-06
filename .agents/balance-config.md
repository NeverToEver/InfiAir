# Balance & Config

## Overview

Where tunables live, how to read them, and the `world_scale` hull-scaling lever. Applies whenever gameplay numbers or config access are involved.

## Rules

- Change tunables only in `data/balance.json`, not script fallbacks (script defaults must match for missing/corrupt JSON). Prefer `scripts/tools/balance_editor.py`; after edits run `scripts/tools/gen_balance_map.py`, check `docs/BALANCE_MAP.md` "bidirectional lookup" sections for new mismatches.
- Read nested config via `GameState.cfg("player.fuel.drain", default)`. Cache in `_ready()`/init; never per-frame JSON lookup on hot paths.
- `GameState` loads balance.json at startup; missing/unparseable → script defaults.
- Single hull-scaling lever: top-level `world_scale` in balance.json (current 0.4; cached `GameState.world_scale`). Hull-size family (sprite scale, collision radius, muzzle/dock/turret/tow offsets, bullet/explosion/gate/laser fx) stored as **design values** (1.0 baseline) in json/tscn/script fallbacks; entities apply `* world_scale` in `_ready()`/`setup()`. Gameplay-range family (AoE, lock/clear radius, slow ring) & indicators/cutscenes/UI don't scale. Classify new size values; never bypass the lever with literal runtime values.
- Idempotent assignment (`radius = design * world_scale`), never `*=` (sub_resources like CircleShape2D shared across instances compound per instance). Runtime-resized shapes (enemy.tscn — normal vs elite radii differ) need `resource_local_to_scene = true`.
