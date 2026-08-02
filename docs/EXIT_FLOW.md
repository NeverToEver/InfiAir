# Global Exit Mechanism Design (EXIT_FLOW)

A unified "back/exit" state machine: from any page, pressing back behaves predictably, safely, and smoothly.
Implementation: `scripts/back_navigator.gd` (state machine) + `scripts/exit_confirm.gd` (global exit-confirm dialog).

## 1. Page Hierarchy (main Scene)

```
L3 modal:  ExitConfirm (global exit-confirm dialog, highest priority)
L2 overlay: SettingsUI (opener = pause/start panel)
           BaseUI (base console) / GameOverUI (settlement) / BuffUI (triple-choice, blocking)
           IntroCinematic (opening cinematic, layer=35; tree paused while playing, Esc/any key/click = skip)
           ReturnCinematic (homecoming cinematic, layer=35; tree paused while playing, Esc/any key/click = skip;
                           after it ends the tree stays paused, landing on BaseUI; see docs/RETURN_HOME_CINEMATIC.md §4)
L1 gameplay: Gameplay(HUD) ⇄ PauseUI
           buff scroll bar (HUD overlay, L key expands/collapses, no pause; Esc = collapse)
L0 top-level: StartPanel (main menu/lobby)
```

`scenes/tutorial.tscn` is a standalone scene: itself top-level, Esc exits the tutorial back to the main menu (`tutorial.gd` handles it itself, outside the state machine).

## 2. State Machine Pseudocode

All-platform back input converges on `BackNavigator.go_back()` (PC Esc / **right mouse button** / gamepad `ui_cancel` / Android system back):

```
func go_back():
    match decide_back_action():            # pure decision function, no side effects (tests cover all branches)
        CANCEL_EXIT:          # ExitConfirm visible
            exit_confirm.cancel()          # back = cancel exit; when start panel visible, restore focus to its primary button
        SKIP_INTRO:           # intro cinematic playing (Main._intro != null)
            main.skip_intro()              # = skip cinematic straight into gameplay (any key/click captured by the cinematic itself)
        SKIP_RETURN:          # return cinematic playing (Main._return != null, same priority as SKIP_INTRO)
            main.skip_return()             # = skip cinematic straight to base UI (tree stays paused; any key/click as above)
        CAPTURE_PASSTHROUGH:  # settings key-capture active
            pass                           # don't consume the event; let SettingsUI cancel the capture
        CLOSE_SETTINGS:       # settings page visible
            settings_ui.back()             # back to opener (pause or start panel)
        RESUME_BASE:          # base console visible
            base_ui.resume()               # = resume sortie
        IGNORE:               # buff triple-choice (must choose) / intermediate state before death→settlement / other paused states
            (consume input)
        TO_MAIN_MENU:         # settlement visible
            paused = false + reset_run + reload_current_scene   # back to main menu (save already deleted on death)
        CLOSE_BUFF_PANEL:     # buff scroll bar expanded (HUD overlay, no pause)
            hud.close_buff_panel()       # back = collapse bar (takes priority over opening pause)
        RESUME_GAME:          # pause panel visible
            pause_ui.close()
        CONFIRM_EXIT:         # top level (start panel)
            exit_confirm.show_confirm(battle=false)
        OPEN_PAUSE:           # all above false = in battle (no overlay, not paused)
            pause_ui.open()                # back one level = pause
```

Decision order equals code order: modal (confirm dialog) → cinematic skip (intro/return) → settings/base/blocking/settlement → buff bar → pause → top-level → battle.

### Battle Exit (Double Confirm + Progress-Loss Warning)

```
In battle Esc → pause panel (first confirmation chance)
  → click "Exit game" → ExitConfirm(battle=true) (red warning: exiting loses this run's progress)
    → only "Confirm exit" really exits; "Cancel" / Esc returns to the pause panel
```

### Unified Pre-Exit Cleanup (After ExitConfirm Confirms)

```
func _execute_exit_cleanup(battle):
    GameState.save_profile()     # persist high score/settings/language/keybinds
    if battle:
        GameState.delete_save()  # exiting mid-battle = abandon run (same semantics as death)
    _on_exit_cleanup()           # hook: stop unfinished SFX (avoid playback-instance leak on exit); reserved for network disconnect etc.
    # then fade to black 0.3s (transition) → get_tree().quit()
```

Exiting from the start panel: the run save is **kept**; the next launch can "continue the run".

## 3. Key Mapping Table

| Platform | Physical input | Mapped to | Handling |
|---|---|---|---|
| PC | Esc | `ui_cancel` (engine built-in) | `BackNavigator._unhandled_input` |
| PC | Right mouse button | Fixed detection (not part of key rebinding) | Same as above — **right click = back/cancel** (convention: confirm-dialog cancel, settings back, pause open/resume, top-level exit confirm) |
| Gamepad | B / Circle (joy button 1) | `ui_cancel` (engine built-in default mapping) | Same as above; A = `ui_accept` confirm, d-pad/stick drive GUI focus navigation (focus styling visible) |
| Gamepad | Left stick | `move_*` (left-stick movement) | GameState `_bind_joypad_defaults()` assembles at startup via InputMap at runtime (`project.godot` stores keyboard only, P0-1) |
| Gamepad | Right stick | `aim_x`/`aim_y` (virtual reticle, `player.aim_point`) | Sensitivity/deadzone adjustable in the settings "Gamepad" section (`joy_aim_speed`/`joy_deadzone`, persisted to profile) |
| Gamepad | A=dash / RB=boost / LB=fine move / X=mothership charge / Y=homecoming / L3=buff bar / R3=give up / A=restart | `dash`/`boost`/`fine_move`/`dock`/`homecoming`/`buff_panel`/`give_up`/`restart` | Same runtime assembly; B key yields to `ui_cancel` (back) |
| Android | System back gesture | `NOTIFICATION_WM_GO_BACK_REQUEST` | `BackNavigator._notification` → `go_back()` |

Inside the confirm dialog: Enter/gamepad A triggers the focused button (default focus on "Cancel", the safe side); Esc/gamepad B = cancel.

## 4. ExitConfirm Reusable Component Design

- Mounting: `scenes/main.tscn`, CanvasLayer layer=40 (above all UI), `process_mode=Always`.
- API:
  - `show_confirm(battle: bool = false)` — normal/battle dual modes; battle switches to the `UITheme.DANGER` red-text warning.
  - `cancel()` — closes (Esc routed here by BackNavigator).
  - `_execute_exit_cleanup(battle)` — pre-exit cleanup (tests may call it directly to assert side effects).
- Layout: `ChamferedPanel` + title + message + "Cancel" (default focus) / "Confirm exit" (danger color); button styling via `UITheme.make_button`; copy from `EXIT_*` translation keys, refreshed on `locale_changed`.
- Reuse: any page needing "confirm-then-exit" just calls `show_confirm()`; cleanup/transition/exit flow are fully encapsulated.

## 5. Platform Differences

- **PC**: no differences, Esc works everywhere (currently the only on-device-verified platform).
- **Gamepad**: relies on the engine built-in `ui_cancel` default mapping (incl. joy button 1) for back; **movement/action keys/right-stick aim are assembled at startup by `GameState._bind_joypad_defaults()` via InputMap at runtime** (`project.godot` keeps keyboard as the single source of truth, P0-1: left-stick movement, A/RB/LB/X/Y/L3/R3 action keys, right-stick virtual reticle; B yields to back). **PlayStation controllers auto-detected** (same SDL positions, label mapping only: A/B/X/Y ↔ ✕/○/□/△, LB/RB ↔ L1/R1; settings page shows the current layout). Sensitivity and stick deadzone adjustable in the settings "Gamepad" section. Button focus uses the same highlight as hover, so keyboard/gamepad navigation is visible. **Not yet on-device-verified** (no export pipeline; on-device walkthrough registered as a pre-release verification item).
- **Android**: system back gesture wired into the same state machine; export template config is out of this project's scope, marked "mapping ready, not on-device-verified".
- **Tutorial scene**: see §1 — standalone top-level self-handling, outside the state machine (avoids cross-scene coupling).

## 6. Testing

`test/back_navigation_test.tscn`: full `decide_back_action()` branch coverage + integration paths (Esc→pause→resume, settings back, top-level Esc→confirm dialog→cancel, battle-exit confirmation chain, cleanup side effects). The intro cinematic's SKIP_INTRO branch and Esc-skip path are covered by `test/intro_cinematic_test.tscn` (design: docs/INTRO_CINEMATIC.md); the return cinematic's SKIP_RETURN branch (decision + real Esc injection + landing on base UI with the tree kept paused after skip) is covered by `test/return_cinematic_test.tscn` (design: docs/RETURN_HOME_CINEMATIC.md §4), and back_navigation_test additionally asserts its decision branch. Regression: `esc_navigation_test` (real key injection) and `smoke_test` must stay fully green.
