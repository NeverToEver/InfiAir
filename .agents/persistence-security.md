# Persistence & Security

## Overview
存档/档案、损坏恢复与安全态势（无网络/无凭据）；适用于持久化代码及游戏写入的任何数据。

## Rules
- 登录局存档 `user://savegame_<sanitized>_<sha256[:12]>.json`（per-user、owner 校验）；账户/设置/统计/本地排行榜存 `user://users.json`；访客不存档（仅内存）。注册前仍读写旧路径 `user://profile.json`（兼容），首次注册迁移合并后删除。
- corrupt JSON 隔离为 `<file>.corrupt`，经 `GameState.SaveCorrupt`/`ProfileCorrupt`/`UserDbCorrupt` 三 flag 通知开始屏；`users.json` 损毁时 `UserDb.EnsureLoaded()`（`csharp/core/Storage/UserDb.cs`）重建为空库并在 `Welcome.cs` 提示——不再静默丢账户。
- 无网络/第三方/凭据：仅 `user://` 与离线工具（`balance_editor.py` 仅监听 127.0.0.1，非运行时）。`.gitignore` 排除导入缓存与导出（`builds/` 等）；`.gitattributes` 规范 EOL——`text=auto eol=lf`、`*.bat` CRLF、`*.sh` LF；`builds/.gdignore`。preset 变更须 review `release.sh` + `packaging/`。
