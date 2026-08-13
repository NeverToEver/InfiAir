# Balance & Config

## Overview

Tunables 存放位置、读取方式与 `world_scale` 机体缩放杆。涉及玩法数值或配置访问时适用。
## Rules
- tunables 只改 `data/balance.json`（脚本默认值兜底一致，缺失/损坏 JSON 时生效）；优先 `scripts/tools/balance_editor.py`；改后跑 `scripts/tools/gen_balance_map.py`，查 `docs/BALANCE_MAP.md` 失配段。
- 读取用 `GameState.Cfg("path", def)`；需类型校验的标量用 `CfgFx.Float/Int(path, def, min, max)`；启动加载 balance.json，缺失/不可解析 → 脚本默认值。
- 单一缩放杆：`world_scale` 0.4 —— 设计值 × world_scale，玩法范围/UI/过场不缩放，新尺寸值分类。幂等赋值（design * world_scale）禁 `*=`（共享 CircleShape2D）；运行时改尺寸需 `resource_local_to_scene = true`。
