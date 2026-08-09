# UI, Text & Navigation

## Overview

i18n, UI styling via `UITheme.cs`, pausing pages, back/exit navigation, BGM lifecycle. Applies to all UI pages/overlays and visible text.

## Rules

- All visible text via `Tr("UPPER_SNAKE_CASE_KEY")`; new keys go to `data/translations.csv` zh + en columns, then re-import to build `.translation`. Dynamic text uses `%d`/`%s` placeholder keys.
- Locale switch only via `GameState.SetLocale("zh"/"en")`; UI refreshes on `LocaleChanged`.
- Styling via `csharp/godot/UITheme.cs`: palette tokens, type scale, `MakeLabel()`, `MakeButton()`, `MakeToggleButton()`, `MakeSectionHeader()`, `MakePageShell()` (dim overlay + centered margin + title/subtitle/content/buttons; all modals), `AnimateModalOpen()`, `AddButtonMotion()` (auto on buttons), `MakeBuffSocket()` (category-tinted chamfered socket tile, shared by dock + buff cards) / `MakeBuffTile()` (46×46 socket + ×N badge chip; collapsed row bottom-right: latest 4 + overflow +N; L opens right scroll list; Esc closes via BackNavigator), intro-anim helpers. Widgets: `ChamferedPanel.cs` (optional `InnerFrame` nested outline, used by buff sockets), `SegmentedBar.cs` (partial last segment), `BuffIcons.cs` (19 glyphs + category colors, 2px stroke floor; HUD dock + buff cards), start decor `StartBackdrop.cs`. New pages use `MakePageShell()`, ≤1 primary button; no hand-written colors or Label/Button boilerplate.
- Global skill `game-ui-ux`: cross-engine UI/UX guidance (responsive layout, safe areas, kbd/gamepad focus, screen stack, event-driven HUD); complements `godot-ui-control`. Use when designing/refactoring HUD/menus/overlays; styling follows `UITheme.cs`.
- Pausing UIs (buff/pause/results) need `ProcessMode = ProcessModeEnum.Always` + `GetTree().Paused`.
- Back/exit centralized in `BackNavigator`. Pages don't consume `ui_cancel` (except settings key-capture); **right mouse = fixed back/cancel** (detected by BackNavigator, not rebindable, same route as Esc). New page levels register in `DecideBackAction()` + sync `docs/EXIT_FLOW.md`.
- BGM: set `stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward` only; never set loop begin/end or stop BGM in `_ExitTree()` (leaks playback instances).
