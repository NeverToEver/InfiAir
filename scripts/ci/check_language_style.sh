#!/usr/bin/env bash
# 语言风格门禁：注释语种 + 散文术语单一叫法。
# 用法：bash scripts/ci/check_language_style.sh
# 口径见 AGENTS.md「语言风格」。只查注释散文，豁免标识符/配置路径/动作名/代码片段/分隔线——
# 门禁的目标是「同一概念别写出两种叫法」，不是把英文 API 名翻译成中文。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

python3 - <<'PY'
import pathlib, re, sys
from collections import defaultdict

ROOT = pathlib.Path.cwd()
SKIP = {".git", ".godot", "builds", "tools", "bin", "obj", "__pycache__", ".venv"}
CJK = re.compile(r"[\u4e00-\u9fff]")
COMMENT = re.compile(r"^\s*(?:///|//)\s?(.*)$")
IDENT = re.compile(
    r"`[^`]*`|https?://\S+|cref=\"[^\"]*\"|\b[A-Za-z]+[A-Z][A-Za-z0-9]*\b"
    r"|\b[a-z]+_[a-z0-9_]+\b|\b[A-Z][A-Z0-9_]{2,}\b|\b[A-Za-z0-9_.]+\([^)]*\)"
)
WORD = re.compile(r"\b[A-Za-z]{3,}\b")

# 术语表：规范叫法 -> 散文中的漂移写法（已排除「本局内」这类合法复合词）
TERMS = {
    "本局": r"对局|单局|(?<!本)局内",
    "敌机": r"敌人",
    "增幅": r"\bbuff\b|\bBuff\b|增益",
    "弹反": r"格挡",
    "天赋": r"技能树",
}

prose, term_hits = [], defaultdict(list)
for path in sorted(ROOT.rglob("*.cs")):
    if any(part in SKIP for part in path.parts):
        continue
    rel = str(path.relative_to(ROOT))
    for i, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        m = COMMENT.match(line)
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
for canon, hits in sorted(term_hits.items()):
    for h in hits:
        errors.append(f"术语漂移：散文应用「{canon}」——{h}")
for p in prose:
    errors.append(f"英文散文注释（应写中文，标识符/API 名除外）：{p}")

if errors:
    for e in errors[:40]:
        print("::error::" + e)
    print(f"language-style gate: FAILED（{len(errors)} 项）")
    sys.exit(1)
print("language-style gate: clean（注释语种与术语单一叫法）")
PY
