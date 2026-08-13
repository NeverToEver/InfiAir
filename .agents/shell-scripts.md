# Shell Scripts

## Overview
仓库 shell 脚本约定：启动器（`run.sh`/`run.command`/`run.bat`）、`release.sh`、`packaging/`（双平台安装/卸载）。
## Rules
- Errors: 默认 `set -euo pipefail`（`release.sh`/`packaging/linux/*.sh` 符合）；启动器从简——`run.sh` 仅 `set -e`，`run.command`/`run.bat` 用显式退出码；错误 → stderr（`>&2`）带上下文 + 非零退出。
- Deps/engine: 启动器统一 .NET/mono 版引擎优先；run.sh 候选链 `godot-mono` → `~/.local/bin/godot-mono` → PATH `godot`/`godot4` → `~/.local/bin/godot` → `/Applications/Godot.app`，<4.6 仅警告后尝试继续；run.bat 保真退出码（`endlocal & exit /b %EXIT_CODE%`，勿用 `if errorlevel`）；release.sh `VERSION` 自 `project.godot` `config/version` 用 sed 读取（取不到硬失败），打包前 `command -v tar/zip` 前置检查。
- Verify: `bash -n` + 实跑（如 `./run.command --headless --quit-after 300`）。
