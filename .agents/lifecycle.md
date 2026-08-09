# Lifecycle, Input & Test Discipline

## Overview

Scene lifecycle ordering, input mapping, async/coroutine discipline, and test discipline. Applies to all C# scripts under `csharp/godot/` (style/build rules: `.agents/csharp-conventions.md`).

## Rules

- `Setup()` runs before `_Ready()`; don't rely on `_Ready`-initialized state there — access children via `GetNode<T>("path")`.
- Don't touch existing autoloads/input mappings for unrelated needs. Inputs (`project.godot`): `move_up`/`move_down`/`move_left`/`move_right` (WASD/arrows), `boost` (Shift), `fine_move` (Ctrl), `dash` (Space), `dock` (H), `homecoming` (B), `give_up` (K), `buff_panel` (L), `parry` (F, arcane shield, fairness mech #4), `restart` (R). Joypad defaults bound at runtime internally by GameState (`BindJoypadDefaults()`, private; keyboard only in project.godot; deadzone via `GameState.SetJoyDeadzone()`). PS detect via `GameState.IsPsGuid()` (vendor 054c; ✕○□△/L1-R1 labels).
- Tutorial isolates run state/saves (`csharp/godot/Tutorial.cs`: entry resets run + deletes save; tutorial never reads/writes savegame); restore `Engine.TimeScale = 1` on exit. Keep refs to runtime-created nodes; never rely on auto-generated node names.
- After adding/renaming a `.cs`, run `dotnet build` (zero-warning gate) and keep the `.cs.uid` sidecar with the file — see `.agents/csharp-conventions.md`.
- Async discipline: no bare `async void` lifecycle methods and no awaits that can hang past node/tree exit. In-game waits go through `csharp/godot/Coroutine.cs` (`WaitSeconds`/`WaitPhysicsFrames`/`WaitSignal` — `SceneTree.CreateTimer` + `ToSignal` with `IsInstanceValid` guards and timer fallbacks), never raw `Task.Delay`. Full rules: `.agents/csharp-conventions.md` §Async.
- **Tests drive public test ports, not private internals**: simulate input/state via the target's public test port (`SimulateTouch`/`SimulateDrag` on `VirtualControls`, `SetTestState` on `MetaHealthFX`, `Set*` accessors) — never write `_` private fields or call `_UnhandledInput` directly (A7; C30/Q24 precedents). Injected real input events (`Input.ParseInputEvent`) with mouse/touch positions are transformed window→viewport in headless (not portable) — see `docs/TESTING.md` "Headless Test Environment Notes".
