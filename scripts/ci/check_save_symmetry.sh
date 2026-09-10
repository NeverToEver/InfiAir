#!/usr/bin/env bash
# 存档字段对称性门禁：CollectRunDict（写）与 ApplyRunDict（读）必须一一对应。
# 用法：bash scripts/ci/check_save_symmetry.sh
# 为什么需要：单存档位检查点模型下，
#   写而未读 → 字段白写，玩家以为存了其实读不回来；
#   读而未写 → 每次读档都落到默认值，进度静默丢失。两者编译与冒烟都发现不了。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

python3 - <<'PY'
import pathlib, re, sys

path = pathlib.Path("csharp/godot/GameState.RunSave.cs")
src = path.read_text(encoding="utf-8")

collect = re.search(r"return new Godot\.Collections\.Dictionary\s*\{(.*?)\n        \};", src, re.S)
apply = re.search(r"private void ApplyRunDict\(Godot\.Collections\.Dictionary d\)(.*?)\n    \}", src, re.S)
if not collect or not apply:
    print("::error::无法定位 CollectRunDict/ApplyRunDict（结构变了？门禁需同步）")
    sys.exit(1)

written = set(re.findall(r'\["([a-z_]+)"\]\s*=', collect.group(1)))
read = set(re.findall(r'GetValueOrDefault\("([a-z_]+)"', apply.group(1)))
# version 由 LoadRun 单独判定，不参与对称性
written.discard("version")
read.discard("version")

errors = []
for key in sorted(written - read):
    errors.append(f"`{key}` 只写不读：存了但读档不还原（玩家进度静默丢失）")
for key in sorted(read - written):
    errors.append(f"`{key}` 只读不写：读档永远取默认值（该字段从未落盘）")

if errors:
    for e in errors:
        print("::error::" + e)
    print(f"save-symmetry gate: FAILED（写 {len(written)} / 读 {len(read)}）")
    sys.exit(1)
print(f"save-symmetry gate: clean（写读各 {len(written)} 个字段，一一对应）")
PY
