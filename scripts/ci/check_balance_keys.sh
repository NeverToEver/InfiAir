#!/usr/bin/env bash
# 配置键门禁：C# 里读取 balance.json 的键必须真实存在。
# 用法：bash scripts/ci/check_balance_keys.sh
# 为什么需要：键名写错时 Cfg() 会静默回退到代码内默认值——数值表改了却不生效、
# 或默认值与设计值悄悄分叉，编译与冒烟都发现不了。
# 判定：字面键必须完整命中 balance.json 的某个「点分路径」（不做后缀放宽——后缀命中会把
#       `player.bullet_speed` 这类前缀写错、只靠尾段撞上的键放绿，正是要抓的静默错误）。
# 只认真正的 balance 访问器：GameState.Cfg / 本层 Cfg / CfgFx.* / CfgVal，以及已确证转发到
# GameState.Cfg 的自定义包装器 CfgF / CfgColor（字符串首参形式与 gs 首参形式）。
# 不纳入：DawnStation.CfgFloat/CfgInt/CfgColor、CinematicFx.CfgFloat/CfgColor——它们读的是
# 本地 cfg 字典 / tscn 派生字典，不是 balance。
# 跳过：注释行、以点结尾或以 + 拼接的前缀（"boss.phases.type" + n）。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

python3 - <<'PY'
import json, pathlib, re, sys

ROOT = pathlib.Path.cwd()
SKIP = {".git", ".godot", "builds", "tools", "bin", "obj", "__pycache__", ".venv"}


def flatten(node, prefix=""):
    out = set()
    if isinstance(node, dict):
        for key, value in node.items():
            path = f"{prefix}{key}"
            out.add(path)
            out |= flatten(value, path + ".")
    return out


balance = json.loads((ROOT / "data" / "balance.json").read_text(encoding="utf-8"))
paths = flatten(balance)

# 捕获键 + 紧跟其后的字符，用于识别字符串拼接。三个分支分别覆盖：既有访问器、包装器字符串首参、
# 包装器 gs 首参（WorldPostFx/DeepSpaceBackdrop 的 CfgF/CfgColor 签名为 (gs, "key", ...)）。
CALL = re.compile(
    r'(?:GameState\.Instance\.Cfg|(?<!\.)\bCfg|CfgVal|CfgFx\.\w+)\s*\(\s*"([^"]+)"(\s*\+)?'
    r'|(?:CfgF|CfgColor)\s*\(\s*"([^"]+)"(\s*\+)?'
    r'|(?:CfgF|CfgColor)\s*\(\s*gs\s*,\s*"([^"]+)"(\s*\+)?'
)
COMMENT = re.compile(r"^\s*//")

missing, dynamic, seen = [], 0, set()
for path in sorted(ROOT.rglob("*.cs")):
    if any(part in SKIP for part in path.parts):
        continue
    rel = str(path.relative_to(ROOT))
    for i, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if COMMENT.match(line):          # 注释里出现的键名不是调用
            continue
        for m in CALL.finditer(line):
            key = m.group(1) or m.group(3) or m.group(5)
            concat = m.group(2) or m.group(4) or m.group(6)
            if concat or key.endswith("."):   # 前缀拼接（"a.b." + n / "a.b" + n）
                dynamic += 1
                continue
            seen.add(key)
            if key not in paths:
                missing.append(f"{rel}:{i}  键 `{key}` 在 balance.json 中不存在（将静默用代码默认值）")

if not seen:
    print("::error::未捕获到任何 balance 字面键（访问器正则或扫描范围变了？门禁需同步）——拒绝判 clean")
    sys.exit(1)
if missing:
    for m in missing[:40]:
        print("::error::" + m)
    print(f"balance-keys gate: FAILED（{len(missing)} 项 / 共 {len(seen)} 个字面键）")
    sys.exit(1)
print(f"balance-keys gate: clean（{len(seen)} 个字面键全部完整命中 balance.json；拼接前缀 {dynamic} 处不参与判定）")
PY
