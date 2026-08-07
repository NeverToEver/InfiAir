# GDScript & Lifecycle

## Overview

Coding style, scene lifecycle ordering, input mapping, and coroutine discipline. Applies to all `.gd` files.

## Rules

- Godot 4 official style: Tab indent, type annotations, `CONSTANT_CASE`, `_` private prefix, `signal.emit()`/`connect()`.
- `setup()` runs before `_ready()`; no `@onready` there — use `$node/path`.
- Don't touch existing autoloads/input mappings for unrelated needs. Inputs (`project.godot`): `move_up`/`move_down`/`move_left`/`move_right` (WASD/arrows), `boost` (Shift), `fine_move` (Ctrl), `dash` (Space), `dock` (H), `homecoming` (B), `give_up` (K), `buff_panel` (L), `parry` (F, arcane shield, fairness mech #4), `restart` (R). Joypad defaults bound at runtime by `GameState._bind_joypad_defaults()` (P0-1: keyboard only in project.godot; deadzone via `set_joy_deadzone()`). PS detect via `GameState.is_ps_guid()` (vendor 054c; ✕○□△/L1-R1 labels).
- Tutorial isolates run state/saves; restore `Engine.time_scale = 1` on exit. Keep refs to runtime-created nodes; never rely on auto-generated node names.
- After adding a `class_name` script, run `godot --headless --import --path .` to refresh class cache, else "Identifier not declared" compile errors break the host scene.
- No `await get_tree().create_timer()` or timer-hung coroutines: unfinished coroutine state leaks on exit along with referenced resources. Use one-shot `Timer` nodes + signals (see `spawner.gd` `_schedule()`).
- **Tests drive public test ports, not private internals**: simulate input/state via the target's public test port (`simulate_touch`/`simulate_drag` on `VirtualControls`, `set_test_state` on `MetaHealthFX`, `set_*` accessors) — never write `_` private fields or call `_unhandled_input` directly (A7; C30/Q24 precedents). Injected real input events (`Input.parse_input_event`) with mouse/touch positions are transformed window→viewport in headless (not portable) — see `docs/TESTING.md` "Headless Test Environment Notes".
