#!/usr/bin/env bash
# 卫生门禁：注释日期戳 + 注释语种与术语单一叫法。
# 用法：bash scripts/ci/check_prose_hygiene.sh
#
# 角色边界（AGENTS.md §6）：这是**卫生**门禁，不是质量门禁。
#   它判的是散文形态，改坏了不会让游戏出错——通过它不构成任何完成依据，
#   也不得用它替代行为回归面。它存在的理由只有一条：散文漂移（日期戳堆积、
#   同一概念两种叫法、注释写英文）靠人工巡检收不敛，而机器判这几条几乎零成本。
#
# 判定口径：
#   1) 代码源文件（.cs/.sh/.py）的注释里不得出现 YYYY-MM-DD（注释标记 // 或 # 之后才算，
#      代码字面量不受影响）；docs/ 与 *.md 不扫描——文档里的日期是合法时间线锚点。
#   2) .cs 注释散文用简体中文（标识符/API 名/配置路径/代码片段豁免）。
#   3) .cs 注释里的术语按单一叫法（TERMS 表 = AGENTS.md §9 术语表的机器副本，改动须同步）。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

python3 - <<'PY'
import pathlib, re, sys
from collections import defaultdict

ROOT = pathlib.Path.cwd()
SKIP = {".git", ".godot", "builds", "tools", "bin", "obj", "__pycache__", ".venv"}
EXTS = {".cs", ".sh", ".py"}
CJK = re.compile(r"[\u4e00-\u9fff]")
CS_COMMENT = re.compile(r"^\s*(?:///|//)\s?(.*)$")
DATE = re.compile(r"20[0-9][0-9]-[0-9][0-9]-[0-9][0-9]")
IDENT = re.compile(
    r"`[^`]*`|https?://\S+|cref=\"[^\"]*\"|\b[A-Za-z]+[A-Z][A-Za-z0-9]*\b"
    r"|\b[a-z]+_[a-z0-9_]+\b|\b[A-Z][A-Z0-9_]{2,}\b|\b[A-Za-z0-9_.]+\([^)]*\)"
)
WORD = re.compile(r"\b[A-Za-z]{3,}\b")

# 术语表：规范叫法 -> 散文中的漂移写法（与 AGENTS.md §9 同源；已排除「本局内」这类合法复合词）
TERMS = {
    "本局": r"对局|单局|(?<!本)局内",
    "敌机": r"敌人",
    "增幅": r"\bbuff\b|\bBuff\b|增益",
    "弹反": r"格挡",
    "天赋": r"技能树",
    "键鼠": r"触屏|触控|移动端",
}

stamps, prose, term_hits = [], [], defaultdict(list)
for path in sorted(ROOT.rglob("*")):
    if not path.is_file() or path.suffix not in EXTS:
        continue
    if any(part in SKIP for part in path.parts):
        continue
    rel = str(path.relative_to(ROOT))
    for i, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
        # 日期戳：只在注释标记之后计命中
        dm = DATE.search(line)
        if dm:
            marks = [m for m in (line.find("//"), line.find("#")) if m >= 0]
            if marks and dm.start() > min(marks):
                stamps.append(f"{rel}:{i}  {line.strip()[:90]}")

        m = CS_COMMENT.match(line)
        if not m:
            continue
        text = m.group(1).strip()
        if not text:
            continue
        if CJK.search(text):
            for canon, bad in TERMS.items():
                if re.search(bad, text):
                    term_hits[canon].append(f"{rel}:{i}  {text[:90]}")
            continue
        # 非中文注释：剔除标识符/代码片段/分隔装饰后仍成句，才算「英文散文」
        # 分隔符按单字符剥离：`a/b/c` 这类配置键列表不是散文（键名可无下划线/驼峰，故按「/≥2 且无空格」整体豁免）
        if text.count("/") >= 2 and " " not in text:
            continue
        stripped = re.sub(r"[-=*_~#|+<>/\\]{1,}", " ", IDENT.sub(" ", text))
        if len(WORD.findall(stripped)) >= 2 and "(" not in text and ")" not in text:
            prose.append(f"{rel}:{i}  {text[:90]}")

errors = []
for s in stamps:
    errors.append(f"注释含日期戳（不写变更史与轮次）：{s}")
for canon, hits in sorted(term_hits.items()):
    for h in hits:
        errors.append(f"术语漂移：散文应用「{canon}」——{h}")
for p in prose:
    errors.append(f"英文散文注释（应写中文，标识符/API 名除外）：{p}")

if errors:
    for e in errors[:40]:
        print("::error::" + e)
    print(f"prose-hygiene gate: FAILED（{len(errors)} 项）")
    sys.exit(1)
print("prose-hygiene gate: clean（注释无日期戳、语种与术语单一）")
PY
