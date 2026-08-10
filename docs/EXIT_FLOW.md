# Global Exit Mechanism (EXIT_FLOW)

Unified back/exit state machine: any page, predictable/safe/smooth behavior. Impl: `csharp/godot/BackNavigator.cs` (FSM) + `csharp/godot/ExitConfirm.cs` (global confirm).

## 1. Page Stack (main scene)

```
L3 modal:  ExitConfirm (highest priority)
L2 overlay: SettingsUI (opener = pause panel (main) / welcome (settings in welcome scene))
            BaseUI / GameOverUI / BuffUI (blocking)
            IntroCinematic (layer=35; tree paused; Esc/any key/click = skip)
            ReturnCinematic (layer=35; tree paused; Esc/any key/click = skip;
                             ends on BaseUI with tree paused, docs/RETURN_HOME_CINEMATIC.md §4)
L1 run:    Gameplay(HUD) ⇄ PauseUI
           buff scroll bar (HUD overlay, L key; not pausing; Esc = close bar)
L0 top:    welcome scene (accounts entry; Esc hierarchy: settings → leaderboard overlay → research-lab overlay → guest/delete/exit 三 modal → username dropdown → exit confirm, self-handled, outside FSM; impl `Welcome._UnhandledInput`)
```

`scenes/tutorial.tscn` standalone: its own top, Esc = exit to main menu (self-handled, outside FSM). Note: with BaseConsole open the tree is paused, tutorial root (process_mode=inherit) gets no input — click "continue sortie" to close (modal behavior, 2026-08-03 audit note).

## 2. State Machine

All platform back inputs converge to `BackNavigator.GoBack()` (PC Esc / **right mouse** / gamepad `ui_cancel` / Android system back):

```
void GoBack():
    switch (DecideBackAction()) {          // pure decision fn, no side effects (testable)
        CANCEL_EXIT:          _exitConfirm.Cancel()      // back = cancel exit; focus back to pause-resume button
        SKIP_INTRO:           _main.SkipIntro()          // skip intro → run (any key/click captured by cinematic)
        SKIP_RETURN:          _main.SkipReturn()         // skip return → base UI (tree stays paused)
        CAPTURE_PASSTHROUGH:  break                      // settings key-capture: let SettingsUI cancel
        CLOSE_SETTINGS:       _settingsUi.Back()         // → opener (pause or welcome)
        RESUME_BASE:          _baseUi.Resume()           // = continue sortie
        IGNORE:               (swallow)                  // BuffUI (must choose) / dying→results interim / other paused
        TO_MAIN_MENU:         Paused=false → ResetRun() → LogoutUser() → ChangeSceneToFile(welcome.tscn)  // results page (save deleted on death)
        CLOSE_BUFF_PANEL:     _hud.CloseBuffPanel()      // back = close bar (before opening pause)
        RESUME_GAME:          _pauseUi.Close()
        OPEN_PAUSE:           _pauseUi.Open()            // in combat with no overlay → pause
    }
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
void ExecuteExitCleanupInner(bool battle):
    GameState.Instance.SaveProfile()     // high score/settings/locale/keybinds
    if battle:
        GameState.Instance.DeleteSave()  // battle exit = abandon run (same semantics as death)
    OnExitCleanup()                      // hook: stop unfinished SFX (leak prevention); network-reserved
    // fade black 0.3s → GetTree().Quit()
```

Exit from welcome: run save **kept**; "continue run" available next start.

## 3. Key Map

| Platform | Physical | Maps to | Handling |
|---|---|---|---|
| PC | Esc | `ui_cancel` (built-in) | `BackNavigator._UnhandledInput` |
| PC | right mouse | fixed detect (not rebindable) | same — **right mouse = back/cancel** (confirm cancel, settings back, pause open/close) |
| Gamepad | B / Circle (joy button 1) | `ui_cancel` (built-in default) | same; A = `ui_accept` confirm; d-pad/stick via GUI focus nav |
| Gamepad | left stick | `move_*` | `GameState.BindJoypadDefaults()` runtime InputMap assembly (keyboard-only in project.godot, P0-1) |
| Gamepad | right stick | `aim_left`/`aim_right`/`aim_up`/`aim_down` (virtual cursor, `player.AimPoint()`) | sensitivity/deadzone in Settings "Gamepad" (`joy_aim_speed`/`joy_deadzone`, profile) |
| Gamepad | A=dash / RB=boost / LB=fine / X=dock / Y=homecoming / L3=buff bar / R3=give up / LT=parry / A=restart | `dash`/`boost`/`fine_move`/`dock`/`homecoming`/`buff_panel`/`give_up`/`parry`/`restart` | runtime assembly; B yields to `ui_cancel` |
| Android | system back | `NotificationWMGoBackRequest` | `BackNavigator._Notification` → `GoBack()` |

In confirm: Enter/gamepad A triggers focused button (default focus = "Cancel", safe side); Esc/gamepad B = cancel.

## 4. ExitConfirm Component

- Mounted: `scenes/main.tscn`, CanvasLayer layer=40 (above all UI), `process_mode=Always`.
- API: `ShowConfirm(bool battle)` (+ parameterless overload = normal; battle uses `UITheme.Danger` red warning); `Cancel()` (Esc routed by BackNavigator); `ExecuteExitCleanup(battle)` (public — directly callable in tests for side-effect asserts; `ExecuteExitCleanupInner` is the private implementation, A7 public port).
- Layout: `ChamferedPanel` + title + message + "Cancel" (default focus) / "Confirm exit" (danger); buttons via `UITheme.MakeButton`; text `EXIT_*` keys, refreshes on `LocaleChanged`.
- Reuse: any page needing confirm-then-exit calls `ShowConfirm()`; cleanup/transition/quit fully encapsulated.

## 5. Platform Differences

- **PC**: no difference; Esc everywhere (only verified platform so far).
- **Gamepad**: engine built-in `ui_cancel` (incl. joy button 1); **move/action/right-stick aim assembled at runtime by `BindJoypadDefaults()`** (project.godot = keyboard single source, P0-1; B yields to back). **PS auto-detect** (same positions, A/B/X/Y ↔ ✕/○/□/△, LB/RB ↔ L1/R1; Settings shows layout). Sensitivity/deadzone in Settings "Gamepad". **Not hardware-verified** (no export flow; on-device walkthrough = pre-release item).
- **Android**: system back → same FSM; export config out of scope — "mapping ready, not device-verified".
- **Tutorial**: §1, standalone top, outside FSM.

## 6. Tests

`test/back_navigation_test.tscn`: full `DecideBackAction()` branch coverage + integration (Esc→pause→resume, settings back, battle exit chain, cleanup side effects); top-level exit confirm (top Esc→confirm→cancel) covered by `test/welcome_flow_test.tscn` (welcome self-handled, outside FSM). SKIP_INTRO by `test/intro_cinematic_test.tscn` (docs/INTRO_CINEMATIC.md); SKIP_RETURN (decision + real Esc injection + lands on base UI, tree paused) by `test/return_cinematic_test.tscn` (docs/RETURN_HOME_CINEMATIC.md §4); back_navigation_test also asserts its decision branch. Regression: `esc_navigation_test` (real key injection) + `smoke_test` green.
