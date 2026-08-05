# Shell Scripts

## Overview

Conventions for repo shell scripts: launchers (`run.sh`/`run.command`/`run.bat`), `release.sh`, `packaging/` (dual-platform install/uninstall). Structure from `bentsolheim/public-skills` bash skill v2.0.0 (only shell-maintenance skill in ecosystem), adapted — its "no `set -e`" stance rejected (conflicts with project practice).

## Rules

- Errors: `set -euo pipefail` by default (existing `release.sh`/`run.sh`/`packaging/linux/*.sh` match); errors → stderr (`>&2`) with context, non-zero exit. `run.command` (macOS double-click keeps window/output on error) uses explicit `$?` — deliberate exception.
- Structure: arg/multi-function/interactive scripts use `main()` + guard (`[[ "${BASH_SOURCE[0]}" == "${0}" ]] && main "$@"`) + `usage()` heredoc; single-purpose functions, `local` params. Simple scripts (<30 lines, no args, linear) skip main() but keep purpose comment, exit codes, quoted vars.
- Args: `while`+`case`; unknown option → error + `usage()`; support `--help`/`--version`.
- Deps/output: launchers detect engine + version (Godot 4.6+, see run.command candidates/`version_ok` — R07: `version_ok` 函数在 run.command，run.sh 仅警告式检查); `command -v` external tools. Colors ok but respect `NO_COLOR`.
- Verify: `bash -n` + actually run (e.g. `./run.command --headless --quit-after 300`).
