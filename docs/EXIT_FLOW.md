# Global Exit Mechanism (EXIT_FLOW)

Unified back/exit state machine: any page, predictable/safe/smooth behavior. Impl: `scripts/back_navigator.gd` (FSM) + `scripts/exit_confirm.gd` (global confirm).

## 1. Page Stack (main scene)

```
L3 modal:  ExitConfirm (highest priority)
L2 overlay: SettingsUI (opener = pause/start panel)
            BaseUI / GameOverUI / BuffUI (blocking)
            IntroCinematic (layer=35; tree paused; Esc/any key/click = skip)
            ReturnCinematic (layer=35; tree paused; Esc/any key/click = skip;
                             ends on BaseUI with tree paused, docs/RETURN_HOME_CINEMATIC.md §4)
L1 run:    Gameplay(HUD) ⇄ PauseUI
           buff scroll bar (HUD overlay, L key; not pausing; Esc = close bar)
L0 top:    StartPanel
```

`scenes/tutorial.tscn` standalone: its own top, Esc = exit to main menu (self-handled, outside FSM). Note: with BaseConsole open the tree is paused, tutorial root (process_mode=inherit) gets no input — click "continue sortie" to close (modal behavior, 2026-08-03 audit note).

## 2. State Machine

All platform back inputs converge to `BackNavigator.go_back()` (PC Esc / **right mouse** / gamepad `ui_cancel` / Android system back):

```
func go_back():
    match decide_back_action():            # pure decision fn, no side effects (testable)
        CANCEL_EXIT:          exit_confirm.cancel()       # back = cancel exit; focus back to start button
        SKIP_INTRO:           main.skip_intro()           # skip intro → run (any key/click captured by cinematic)
        SKIP_RETURN:          main.skip_return()          # skip return → base UI (tree stays paused)
        CAPTURE_PASSTHROUGH:  pass                        # settings key-capture: let SettingsUI cancel
        CLOSE_SETTINGS:       settings_ui.back()          # → opener (pause or start panel)
        RESUME_BASE:          base_ui.resume()            # = continue sortie
        IGNORE:               (swallow)                   # BuffUI (must choose) / dying→results interim / other paused
        TO_MAIN_MENU:         paused=false + reset_run + reload_current_scene  # results page (save deleted on death)
        CLOSE_BUFF_PANEL:     hud.close_buff_panel()      # back = close bar (before opening pause)
        RESUME_GAME:          pause_ui.close()
        CONFIRM_EXIT:         exit_confirm.show_confirm(battle=false)  # top level (start panel)
        OPEN_PAUSE:           pause_ui.open()             # in combat with no overlay → pause
```

Judgment order = code order: modal → cinematic skip (intro/return) → settings/base/blocking/results → buff bar → pause → top → combat.

### Battle Exit (2nd confirm + progress-loss warning)

```
Esc in combat → pause panel (1st chance)
  → "Exit game" → ExitConfirm(battle=true) (red warning: run progress lost)
    → "Confirm exit" quits; "Cancel"/Esc back to pause
```

### Pre-Exit Cleanup (after ExitConfirm confirm)

```
func _execute_exit_cleanup(battle):
    GameState.save_profile()     # high score/settings/locale/keybinds
    if battle:
        GameState.delete_save()  # battle exit = abandon run (same semantics as death)
    _on_exit_cleanup()           # hook: stop unfinished SFX (leak prevention); network-reserved
    # fade black 0.3s → get_tree().quit()
```

Exit from start panel: run save **kept**; "continue run" available next start.

## 3. Key Map

| Platform | Physical | Maps to | Handling |
|---|---|---|---|
| PC | Esc | `ui_cancel` (built-in) | `BackNavigator._unhandled_input` |
| PC | right mouse | fixed detect (not rebindable) | same — **right mouse = back/cancel** (confirm cancel, settings back, pause open/close, top exit-confirm) |
| Gamepad | B / Circle (joy button 1) | `ui_cancel` (built-in default) | same; A = `ui_accept` confirm; d-pad/stick via GUI focus nav |
| Gamepad | left stick | `move_*` | `GameState._bind_joypad_defaults()` runtime InputMap assembly (keyboard-only in project.godot, P0-1) |
| Gamepad | right stick | `aim_x`/`aim_y` (virtual cursor, `player.aim_point`) | sensitivity/deadzone in Settings "Gamepad" (`joy_aim_speed`/`joy_deadzone`, profile) |
| Gamepad | A=dash / RB=boost / LB=fine / X=dock / Y=homecoming / L3=buff bar / R3=give up / A=restart | `dash`/`boost`/`fine_move`/`dock`/`homecoming`/`buff_panel`/`give_up`/`restart` | runtime assembly; B yields to `ui_cancel` |
| Android | system back | `NOTIFICATION_WM_GO_BACK_REQUEST` | `BackNavigator._notification` → `go_back()` |

In confirm: Enter/gamepad A triggers focused button (default focus = "Cancel", safe side); Esc/gamepad B = cancel.

## 4. ExitConfirm Component

- Mounted: `scenes/main.tscn`, CanvasLayer layer=40 (above all UI), `process_mode=Always`.
- API: `show_confirm(battle: bool = false)` (normal/battle; battle uses `UITheme.DANGER` red warning); `cancel()` (Esc routed by BackNavigator); `_execute_exit_cleanup(battle)` (directly callable in tests for side-effect asserts).
- Layout: `ChamferedPanel` + title + message + "Cancel" (default focus) / "Confirm exit" (danger); buttons via `UITheme.make_button`; text `EXIT_*` keys, refreshes on `locale_changed`.
- Reuse: any page needing confirm-then-exit calls `show_confirm()`; cleanup/transition/quit fully encapsulated.

## 5. Platform Differences

- **PC**: no difference; Esc everywhere (only verified platform so far).
- **Gamepad**: engine built-in `ui_cancel` (incl. joy button 1); **move/action/right-stick aim assembled at runtime by `_bind_joypad_defaults()`** (project.godot = keyboard single source, P0-1; B yields to back). **PS auto-detect** (same positions, A/B/X/Y ↔ ✕/○/□/△, LB/RB ↔ L1/R1; Settings shows layout). Sensitivity/deadzone in Settings "Gamepad". **Not hardware-verified** (no export flow; on-device walkthrough = pre-release item).
- **Android**: system back → same FSM; export config out of scope — "mapping ready, not device-verified".
- **Tutorial**: §1, standalone top, outside FSM.

## 6. Tests

`test/back_navigation_test.tscn`: full `decide_back_action()` branch coverage + integration (Esc→pause→resume, settings back, top Esc→confirm→cancel, battle exit chain, cleanup side effects). SKIP_INTRO by `test/intro_cinematic_test.tscn` (docs/INTRO_CINEMATIC.md); SKIP_RETURN (decision + real Esc injection + lands on base UI, tree paused) by `test/return_cinematic_test.tscn` (docs/RETURN_HOME_CINEMATIC.md §4); back_navigation_test also asserts its decision branch. Regression: `esc_navigation_test` (real key injection) + `smoke_test` green.
