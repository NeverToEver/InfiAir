# Shell Scripts

## Overview

Conventions for repo shell scripts: launchers (`run.sh`/`run.command`/`run.bat`), `release.sh`, `packaging/` (dual-platform install/uninstall). Structure from `bentsolheim/public-skills` bash skill v2.0.0 (only shell-maintenance skill in ecosystem), adapted — its "no `set -e`" stance rejected (conflicts with project practice).

## Rules

- Errors: `set -euo pipefail` by default (`release.sh`/`packaging/linux/*.sh` match); launchers `run.sh`/`run.command`/`run.bat` use `set -e` only (2026-08-06 审计口径修正——启动脚本无失败传播链需求，`run.sh` 实为 `set -e` 而非 `-euo pipefail`，文档此前失实)；errors → stderr (`>&2`) with context, non-zero exit. `run.command` (macOS double-click keeps window/output on error) uses explicit `$?` — deliberate exception.
- Structure: arg/multi-function/interactive scripts use `main()` + guard (`[[ "${BASH_SOURCE[0]}" == "${0}" ]] && main "$@"`) + `usage()` heredoc; single-purpose functions, `local` params. Simple scripts (<30 lines, no args, linear) skip main() but keep purpose comment, exit codes, quoted vars.
- Args: `while`+`case`; unknown option → error + `usage()`; support `--help`/`--version`.
- Deps/output: launchers detect engine + version (Godot 4.6+): run.command 用 `version_ok` 候选选型（`version_ok` 函数在 run.command，run.sh 仅警告式检查）；run.bat 探测 `--version` <4.6 警告并保真退出码（`endlocal & exit /b %EXIT_CODE%`；`if errorlevel` 会归零退出码，勿用）；release.sh 的 `VERSION` 自动读取 `project.godot` `config/version`（sed，取不到硬失败报错），打包前 `command -v tar/zip` 前置检查。`command -v` external tools. Colors ok but respect `NO_COLOR`.
- Verify: `bash -n` + actually run (e.g. `./run.command --headless --quit-after 300`).
