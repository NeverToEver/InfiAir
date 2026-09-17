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
#   1) 代码源文件（.cs/.sh/.py）的注释里不得出现 YYYY-MM-DD（注释标记之后才算，
#      字符串里出现的日期不受影响）；docs/ 与 *.md 不扫描——文档里的日期是合法时间线锚点。
#   2) 注释散文用简体中文（标识符/API 名/配置路径/代码片段豁免）。这一条对 .cs/.sh/.py 一律生效。
#   3) 注释里的术语按单一叫法（TERMS 表 = AGENTS.md §9 术语表的机器副本，改动须同步）。
#      术语判定先剥「」引用——术语表本身要能讨论被禁写法（「敌人 / 对局」）。
# 注释抽取：逐字符扫描，跳过字符串字面量（.cs 含转义与逐字字符串 ""，.py 含三引号串，.sh 含单双引号）
# 与协议分隔 `://`（`res://` 不是注释起点）；按后缀选注释标记（.cs → //，.sh/.py → #，跳过 shebang）。
# 判定与抽取用同一份注释区间：日期戳不再用「原始行里找 // 或 #」的近似法——字符串里写
# 注释形式的日期片段曾因此误红。
# 英文散文启发式（CODE_SHAPE）说明：注释里的代码片段、配置键列表、命令行用法行逐词无法与英文散文
# 区分，出现括号/运算符/花括号/反引号等代码形状即整体豁免。其中**圆括号也计入豁免**是刻意保留的
# （旧口径只豁免 `(` `)`，现在统一进 CODE_SHAPE）：实测去掉圆括号豁免后，全库只多报一处装饰性分节
# 横线（scripts/tools/tune_sfx.py 的 `---- biquad（RBJ cookbook） ----`），说明这一层筛的是排版
# 装饰而非散文；继续留豁免，避免用「猜哪行是散文」的启发式去误报合法注释。代价是带括号的英文散文
# 会漏判——本门禁是卫生门禁、不产生质量信号（AGENTS §6），宁可漏判也不误报。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

python3 - <<'PY'
import pathlib, re, sys
from collections import defaultdict

ROOT = pathlib.Path.cwd()
# 跳过口径：只锚定仓库根的同名目录——根目录 tools/（roslynator 缓存，gitignore）与 builds/（导出产物）
# 不是跟踪文件；scripts/tools/ 是跟踪的素材生成器与数值编辑器，必须参与扫描。此前按目录名逐级比较，
# 把 scripts/tools/ 整块误跳过（该目录的日期戳与英文注释因此长期无判定）。
ROOT_SKIP = {".git", ".godot", "builds", "tools", "__pycache__", ".venv"}
NESTED_SKIP = {"bin", "obj", "__pycache__", ".venv"}
EXTS = {".cs", ".sh", ".py"}
CJK = re.compile(r"[\u4e00-\u9fff]")
# 字符串字面量（含 \" 转义）与逐字字符串 @"..."（"" 转义）；用于术语判定前剥离引号内容。
STRING = re.compile(r'@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"')
DATE = re.compile(r"20[0-9][0-9]-[0-9][0-9]-[0-9][0-9]")
IDENT = re.compile(
    r"`[^`]*`|https?://\S+|cref=\"[^\"]*\"|\b[A-Za-z]+[A-Z][A-Za-z0-9]*\b"
    r"|\b[a-z]+_[a-z0-9_]+\b|\b[A-Z][A-Z0-9_]{2,}\b|\b[A-Za-z0-9_.]+\([^)]*\)"
)
WORD = re.compile(r"\b[A-Za-z]{3,}\b")
# 代码形状片段：括号 / 方括号 / 花括号 / 反引号、运算符、语句分隔符、CLI 用法行
CODE_SHAPE = re.compile(r"[`{}\[\]()]|=>|==|!=|<=|>=|&&|\|\||[%;]|（|）|^\s*(?:Usage|usage):")
# 「」里的词是提及、不是使用：术语判定前剥掉引用
QUOTED = re.compile(r"「[^」]*」")

# 术语表：规范叫法 -> 散文中的漂移写法（与 AGENTS.md §9 同源；已排除「本局内」这类合法复合词）
TERMS = {
    "本局": r"对局|单局|(?<!本)局内",
    "敌机": r"敌人",
    "增幅": r"\bbuff\b|\bBuff\b|增益",
    "弹反": r"格挡",
    "天赋": r"技能树",
    "弹体": r"弹丸",
    "键鼠": r"触屏|触控|移动端",
}


def literal_end(text, i):
    """跳过 text[i] 起的字符串/字符字面量（含 @"" 逐字串与转义），返回结束后的下标。"""
    n = len(text)
    if text[i] == "@":
        i += 2
        while i < n:
            if text[i] == '"':
                if i + 1 < n and text[i + 1] == '"':
                    i += 2
                    continue
                return i + 1
            i += 1
        return n
    quote = text[i]
    i += 1
    while i < n:
        if text[i] == "\\":
            i += 2
            continue
        if text[i] == quote:
            return i + 1
        i += 1
    return n


def comments(text, ext):
    """产出 (行号, 注释文本)。shebang 不是散文；.py 的三引号串整体跳过。
    .sh 按 shell 语义取词首 `#`——`here-doc` 里的内容照常参与（嵌在 .sh 里的 Python 注释同样是
    入库散文，按同一口径判）；边界：here-doc 内 Python 字符串里的词首 `#` 会被当成注释，
    本仓库无此写法，真出现的话报错可读、改写法即可。"""
    out = []
    i, n = 0, len(text)
    lineno = 1
    while i < n:
        c = text[i]
        if c == "\n":
            lineno += 1
            i += 1
            continue
        if ext == ".cs":
            if c == '"' or (c == "@" and i + 1 < n and text[i + 1] == '"'):
                j = literal_end(text, i)
                lineno += text.count("\n", i, j)
                i = j
                continue
            if c == "/" and i + 1 < n and text[i + 1] == "/":
                if i > 0 and text[i - 1] == ":":       # res:// / https:// 的协议分隔，不是注释
                    i += 2
                    continue
                j = i
                while j < n and text[j] == "/":
                    j += 1
                end = text.find("\n", j)
                end = n if end < 0 else end
                out.append((lineno, text[j:end]))
                i = end
                continue
            i += 1
            continue
        if ext == ".py":
            if c in "\"'":
                if text.startswith(c * 3, i):
                    j = text.find(c * 3, i + 3)
                    j = n if j < 0 else j + 3
                else:
                    j = literal_end(text, i)
                lineno += text.count("\n", i, j)
                i = j
                continue
            if c == "#":
                if i == 0 and text.startswith("#!", i):   # shebang 不是散文
                    j = text.find("\n", i)
                    i = n if j < 0 else j
                    continue
                j = text.find("\n", i)
                j = n if j < 0 else j
                out.append((lineno, text[i + 1:j]))
                i = j
                continue
            i += 1
            continue
        # .sh：'...' 无转义、\"...\" 有转义；\# 是转义井号不是注释
        if c == "'" or c == '"':
            i = literal_end(text, i)
            continue
        if c == "\\":
            i += 2
            continue
        if c == "#":
            # shell 语义：`#` 只在词首才是注释——`a#b` 与参数展开 `${var#pat}` 里的 `#` 是普通字符
            prev = text[i - 1] if i > 0 else "\n"
            if prev not in " \t\n;&|()<>":
                i += 1
                continue
            if i == 0 and text.startswith("#!", i):
                j = text.find("\n", i)
                i = n if j < 0 else j
                continue
            j = text.find("\n", i)
            j = n if j < 0 else j
            out.append((lineno, text[i + 1:j]))
            i = j
            continue
        i += 1
    return out


stamps, prose, term_hits = [], [], defaultdict(list)
files = 0
comment_counts = defaultdict(int)
for path in sorted(ROOT.rglob("*")):
    if not path.is_file() or path.suffix not in EXTS:
        continue
    rel = path.relative_to(ROOT)
    parts = rel.parts
    if parts[0] in ROOT_SKIP or any(p in NESTED_SKIP for p in parts[1:]):
        continue
    files += 1
    rels = str(rel).replace("\\", "/")
    for lineno, raw in comments(path.read_text(encoding="utf-8", errors="replace"), path.suffix):
        if not raw.strip():
            continue
        comment_counts[path.suffix] += 1
        body = raw.strip()
        if DATE.search(body):
            stamps.append(f"{rels}:{lineno}  {body[:90]}")
        plain = STRING.sub("", body)
        if CJK.search(plain):
            plain = QUOTED.sub("", plain)
            for canon, bad in TERMS.items():
                if re.search(bad, plain):
                    term_hits[canon].append(f"{rels}:{lineno}  {plain[:90]}")
            continue
        # 分隔符按单字符剥离：`a/b/c` 这类配置键列表不是散文（键名可无下划线/驼峰，故按「/≥2 且无空格」整体豁免）
        if body.count("/") >= 2 and " " not in body:
            continue
        if CODE_SHAPE.search(body):
            continue
        stripped = re.sub(r"[-=*_~#|+<>/\\]{1,}", " ", IDENT.sub(" ", body))
        if len(WORD.findall(stripped)) >= 2:
            prose.append(f"{rels}:{lineno}  {body[:90]}")

# 零命中守卫：取不到判据必须显式失败（AGENTS §6 铁律 2）。注释抽取若整体失效，三类判定会全部静默
# 判 clean——按后缀分别守卫，任何一个后缀抽不到注释都说明该后缀的标记正则漂移了。
if files == 0:
    print("::error::未扫描到任何代码源文件——后缀集或扫描范围漂移？门禁需同步")
    sys.exit(1)
idle = sorted(ext for ext in EXTS if comment_counts[ext] == 0)
if idle:
    for ext in idle:
        print(f"::error::{ext} 一处注释都没抽到（注释标记正则漂移？）——拒绝判 clean")
    print(f"prose-hygiene gate: FAILED（{len(idle)} 个后缀零命中）")
    sys.exit(1)

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
print(f"prose-hygiene gate: clean（{files} 个源文件 / {sum(comment_counts.values())} 处注释"
      f"（cs {comment_counts['.cs']} / sh {comment_counts['.sh']} / py {comment_counts['.py']}）："
      "无日期戳、语种与术语单一）")
PY
