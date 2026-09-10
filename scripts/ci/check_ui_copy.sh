#!/usr/bin/env bash
# 玩家可见文案门禁（data/translations.csv）。
# 用法：bash scripts/ci/check_ui_copy.sh
# 拦截四类问题——它们都会让玩家直接看到「不该出现的东西」：
#   1) 开发措辞 / 流程术语（例：「（左侧轮盘同步下钻）」「见 ROADMAP」）
#   2) 已移除系统的残留词条（局外成长、排行榜、账户…）
#   3) 空字段：zh/en 任一为空，该语言下界面留白
#   4) 缺键：C# 静态 Tr("KEY") 在表中无行 → 游戏直接显示键名本身
# 动态拼接键（Tr("ACT_" + name)）不参与缺键判定，故只认完整调用实参形态。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

python3 - <<'PY'
import csv, pathlib, re, sys

root = pathlib.Path.cwd()
csv_path = root / "data" / "translations.csv"

# 高信号词表：玩家文案里出现即为泄漏（宽泛词不入表，避免误伤正常文案）
BANNED = re.compile(
    "下钻|轮盘同步|DESIGN_BASELINE|ROADMAP|AGENTS|口径|门禁|幂等|硬编码|占位|"
    "TODO|FIXME|科技点|局外成长|研究所|排行榜|生效上限|结构上限|user://|res://"
)
KEY_OK = re.compile(r"[A-Z0-9_]+")
# 只认完整实参：Tr("KEY") / Tr("KEY", …)，排除 Tr("ACT_" + name) 这类前缀拼接
TR_ARG = re.compile(r'Tr\(\s*"([A-Z0-9_]+)"\s*[,)]')

errors: list[str] = []
rows = list(csv.DictReader(csv_path.open(encoding="utf-8", newline="")))
keys: set[str] = set()
for line_no, row in enumerate(rows, start=2):
    key = (row.get("keys") or "").strip()
    if not KEY_OK.fullmatch(key):
        errors.append(f"第 {line_no} 行键名非法：`{key}`（须为大写字母/数字/下划线）")
        continue
    if key in keys:
        errors.append(f"第 {line_no} 行重复键：`{key}`")
    keys.add(key)
    for lang in ("zh", "en"):
        text = (row.get(lang) or "").strip()
        if not text:
            errors.append(f"第 {line_no} 行 `{key}` 的 {lang} 为空（该语言下界面留白）")
            continue
        hit = BANNED.search(text)
        if hit:
            errors.append(f"第 {line_no} 行 `{key}` 的 {lang} 含开发措辞「{hit.group(0)}」：{text!r}")

used: set[str] = set()
for path in (root / "csharp").rglob("*.cs"):
    parts = path.parts
    if "obj" in parts or "bin" in parts:
        continue
    used.update(TR_ARG.findall(path.read_text(encoding="utf-8", errors="ignore")))
for key in sorted(used - keys):
    errors.append(f"`{key}` 被 C# 引用但表中无此键（界面会显示键名本身）")

if errors:
    for message in errors[:40]:
        print("::error::" + message)
    print(f"ui-copy gate: FAILED（{len(errors)} 项）")
    sys.exit(1)
print(f"ui-copy gate: clean（{len(rows)} 条文案 / {len(used)} 个静态键）")
PY
