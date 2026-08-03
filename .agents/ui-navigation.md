# UI, Text & Navigation

## Overview

i18n, UI styling via `ui_theme.gd`, pausing pages, back/exit navigation, BGM lifecycle. Applies to all UI pages/overlays and visible text.

## Rules

- All visible text via `tr("UPPER_SNAKE_CASE_KEY")`; new keys go to `data/translations.csv` zh + en columns, then re-import to build `.translation`. Dynamic text uses `%d`/`%s` placeholder keys.
- Locale switch only via `GameState.set_locale("zh"/"en")`; UI refreshes on `locale_changed`.
- Styling via `scripts/ui_theme.gd`: palette tokens, type scale, `make_label()`, `make_button()`, `make_toggle_button()`, `make_section_header()`, `make_page_shell()` (dim overlay + centered margin + title/subtitle/content/buttons; all modals), `animate_modal_open()`, `add_button_motion()` (auto on buttons), `make_buff_tile()` (46×46 glyph + stack badge; collapsed row bottom-right: latest 4 + overflow +N; L opens right scroll list; Esc closes via BackNavigator), intro-anim helpers. Widgets: `ui_chamfered_panel.gd`, `ui_segmented_bar.gd` (partial last segment), `ui_buff_icons.gd` (16 buff glyphs + category colors; HUD dock + buff cards), start decor `start_radar.gd`/`start_backdrop.gd`. New pages use `make_page_shell()`, ≤1 primary button; no hand-written colors or Label/Button boilerplate.
- Global skill `game-ui-ux` (`~/.kimi-code/skills/game-ui-ux/`, from `gamedev-skills/awesome-gamedev-agent-skills`, Apache-2.0): cross-engine UI/UX guidance (responsive layout, resolution/aspect scaling, safe areas, kbd/gamepad focus, screen stack, event-driven HUD); complements `godot-ui-control`. Use when designing/refactoring HUD/menus/overlays; follow `ui_theme.gd`.
- Pausing UIs (buff/pause/results) need `process_mode = Always` + `get_tree().paused`.
- Back/exit centralized in `BackNavigator`. Pages don't consume `ui_cancel` (except settings key-capture); **right mouse = fixed back/cancel** (detected by BackNavigator, not rebindable, same route as Esc). New page levels register in `decide_back_action()` + sync `docs/EXIT_FLOW.md`.
- BGM: set `stream.loop_mode = LOOP_FORWARD` only; never set loop_begin/loop_end or stop BGM in `_exit_tree()` (leaks playback instances).
