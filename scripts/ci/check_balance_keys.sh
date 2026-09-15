#!/usr/bin/env bash
# 配置键门禁：C# 里读取 balance.json 的键必须真实存在。
# 用法：bash scripts/ci/check_balance_keys.sh
# 为什么需要：键名写错时 Cfg() 会静默回退到代码内默认值——数值表改了却不生效、
# 或默认值与设计值悄悄分叉，编译与冒烟都发现不了。
# 判定：字面键必须完整命中 balance.json 的某个「点分路径」（不做后缀放宽——后缀命中会把
#       `player.bullet_speed` 这类前缀写错、只靠尾段撞上的键放绿，正是要抓的静默错误）。
#
# 扫描口径（先剥注释、再按整个调用实参取键——多行调用不会整条漏掉）：
#   - 接收者任意：`gs.Cfg(...)` / `GameState.Instance.Cfg(...)` / `_balanceService.Cfg(...)` 一视同仁
#     （此前用 `(?<!\.)\bCfg` 只认无限定名，带接收者的调用整块漏判）；
#   - 已知读 balance 的包装器在 BALANCE_ACCESSORS 白名单里逐一列出并给理由（含键实参位置规则）；
#   - 明确不读 balance 的同名族包装器在 NON_BALANCE 里列出（避免未登记告警误报）；
#   - 出现既不在白名单也不在豁免表、实参却带字面字符串的 Cfg 族调用 → 红（防新包装器悄悄绕开判定）；
#   - 白名单里每个访问器都必须真判到至少一处字面键 → 红（某一族访问器整体停止命中时，光看总数
#     看不出来：本次修的正是「总数 566 个键照旧全部命中，却整族漏判」这一形态）。
# 跳过：注释行、字符串里的同名文本、以点结尾或以 + 拼接的前缀（"boss.phases.type" + n）。
# 已知边界：调用枚举本身与接收者无关（结构上保证不会退回「只认无限定名」），但若有人把大量字面键
#   改写成变量/拼接键，本门禁只能看到判到的那些——动态键无法静态判定，计数在输出里明示。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

python3 - <<'PY'
import json, pathlib, re, sys

ROOT = pathlib.Path.cwd()
SKIP = {".git", ".godot", "builds", "tools", "bin", "obj", "__pycache__", ".venv"}

# 白名单：调用名 -> (键实参规则, 理由)。规则取值：
#   "first"        第一个实参是键：Cfg(path, default)
#   "first-or-gs"  第一个实参是字面键，或第一个实参是 gs 时第二个实参是键（(gs, key, ...) 签名）
#   "prefixed:<p>" 第一个实参是相对段，真实键 = <p> + 该段（前缀拼接包装器）
BALANCE_ACCESSORS = {
    "Cfg": ("first", "balance 主访问器 BalanceService.Cfg(path, default)，GameState.Instance.Cfg 转发到它"),
    "CfgVal": ("first", "MetaHealthFX.LoadCfg 的静态包装器，转发 GameState.Instance.Cfg(path, def)"),
    "CfgF": ("first-or-gs",
             "DeepSpaceBackdrop/WorldPostFx 的 (gs, key, def) 与 RumbleService 的 (path, fallback) 均转发 gs.Cfg"),
    "CfgCount": ("first-or-gs", "DeepSpaceBackdrop 的 (gs, key, def) 转发 gs.Cfg（判型 + [0,256] 钳制）"),
    "CfgColor": ("first-or-gs",
                 "ShipEnergyFx.CfgColor(key, def) 与 WorldPostFx.CfgColor(gs, key, def) 转发 gs.Cfg；"
                 "DawnStation/CinematicFx 的 (cfg, key, def) 版本读本地字典，首参既非字面键也非 gs，不命中"),
    "CfgFx.Float": ("first", "CfgFx 判型助手转发 GameState.Instance.Cfg(path, def)"),
    "CfgFx.Int": ("first", "CfgFx 判型助手转发 GameState.Instance.Cfg(path, def)"),
    "SupplyCfg": ("prefixed:base.supply.",
                  "BaseConsole.SupplyCfg(key, fallback) 转发 GameState.Instance.Cfg(\"base.supply.\" + key, fallback)"),
}

# 豁免表：同名 Cfg 族但不读 balance 的调用（各给理由；不登记即报「未登记」）
NON_BALANCE = {
    "CfgFloat": "DawnStation/CinematicFx 的本地 cfg 字典包装器；MetaHealthFX.CfgFloat 读 _cfg 缓存"
                "（其键由同文件 CfgVal 调用判定）",
    "CfgInt": "DawnStation/CinematicFx 本地 cfg 字典包装器（tscn/本地字典派生，非 balance）",
    "CfgBool": "DawnStation/CinematicFx 本地 cfg 字典包装器（tscn/本地字典派生，非 balance）",
    "CfgVector3": "DawnStation/CinematicFx 本地 cfg 字典包装器（tscn/本地字典派生，非 balance）",
    "EncounterTriggerCfg": "GameEventManager 的遭遇触发参数记录类型构造，不是配置访问器",
}


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


def strip_comments(src):
    """把 // 注释替换成空格（保留换行与偏移），并标出每个字符是否落在字符串字面量内。"""
    out = list(src)
    in_string = [False] * len(src)
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        start = i
        if c == '"' or (c == "@" and i + 1 < n and src[i + 1] == '"') or c == "'":
            i = skip_literal(src, i)
            for j in range(start, i):
                in_string[j] = True
            continue
        if c == "/" and i + 1 < n and src[i + 1] == "/":
            if i > 0 and src[i - 1] == ":":       # res:// 协议分隔，不是注释
                i += 2
                continue
            while i < n and src[i] != "\n":
                out[i] = " "
                i += 1
            continue
        i += 1
    return "".join(out), in_string


def skip_literal(text, i):
    """跳过 text[i] 起的字符串/字符字面量（含 @"" 与转义），返回结束后的下标。"""
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


CALL = re.compile(r'(?:(?P<recv>[A-Za-z_][\w.]*)\s*\.\s*)?(?P<name>[A-Za-z_]\w*)\s*\(')
PURE_LIT = re.compile(r'^"((?:\\.|[^"\\])*)"$')
GCFG = re.compile(r"cfg", re.I)


def match_paren(text, open_idx):
    """返回与 text[open_idx]=='(' 配对的 ')' 下标；跳过字符串与字符字面量。"""
    depth, i, n = 0, open_idx, len(text)
    while i < n:
        c = text[i]
        if c == '"' or (c == "@" and i + 1 < n and text[i + 1] == '"') or c == "'":
            i = skip_literal(text, i)
            continue
        if c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return -1


def split_args(text):
    """按顶层逗号切分实参（跳过字符串、括号与花括号嵌套）。"""
    args, cur, depth, i, n = [], [], 0, 0, len(text)
    while i < n:
        c = text[i]
        if c == '"' or (c == "@" and i + 1 < n and text[i + 1] == '"') or c == "'":
            j = skip_literal(text, i)
            cur.append(text[i:j])
            i = j
            continue
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        if c == "," and depth == 0:
            args.append("".join(cur).strip())
            cur = []
        else:
            cur.append(c)
        i += 1
    if "".join(cur).strip():
        args.append("".join(cur).strip())
    return args


def key_of(args, rule):
    """按规则取字面键。返回键串；None = 动态（变量/拼接）；"" = 本调用不参与判定。"""
    if not args:
        return ""
    if rule.startswith("prefixed:"):
        lit = PURE_LIT.match(args[0])
        if lit:
            return rule.split(":", 1)[1] + lit.group(1)
        return None if '"' in args[0] else ""
    lit = PURE_LIT.match(args[0])
    if lit:
        return lit.group(1)
    if rule == "first-or-gs" and args[0] == "gs" and len(args) > 1:
        second = PURE_LIT.match(args[1])
        if second:
            return second.group(1)
        return None if '"' in args[1] else ""
    # 前缀拼接（basePath + "x"）或变量键 —— 动态，不参与判定；无字面串即非键实参（包装器定义/传变量）
    return None if '"' in args[0] else ""


missing, unregistered, dynamic, seen = [], [], [], {}
judged_calls = 0
exercised = set()
for path in sorted(ROOT.rglob("*.cs")):
    if any(part in SKIP for part in path.parts):
        continue
    rel = str(path.relative_to(ROOT)).replace("\\", "/")
    text, in_string = strip_comments(path.read_text(encoding="utf-8"))
    for m in CALL.finditer(text):
        if in_string[m.start()]:                 # 字符串里的同名文本不是调用
            continue
        recv = m.group("recv") or ""
        name = m.group("name")
        short_recv = recv.split(".")[-1] if recv else ""
        lookup = f"{short_recv}.{name}" if f"{short_recv}.{name}" in BALANCE_ACCESSORS else name
        if lookup not in BALANCE_ACCESSORS and lookup not in NON_BALANCE:
            # 未登记的 Cfg 族调用：只对「实参里带字面字符串」的调用报警，避开 LoadCfg() 这类无参同名方法
            if GCFG.search(name):
                close = match_paren(text, m.end() - 1)
                if close > 0 and any(PURE_LIT.match(a) for a in split_args(text[m.end():close])):
                    lineno = text.count("\n", 0, m.start()) + 1
                    called = f"{recv}.{name}" if recv else name
                    unregistered.append(
                        f"{rel}:{lineno}  `{called}` 未登记为 balance 访问器或豁免项"
                        "（新包装器？读 balance 就登记进 BALANCE_ACCESSORS，不读就给理由进 NON_BALANCE）")
            continue
        if lookup in NON_BALANCE:
            continue
        close = match_paren(text, m.end() - 1)
        if close < 0:
            continue
        key = key_of(split_args(text[m.end():close]), BALANCE_ACCESSORS[lookup][0])
        if key == "":
            continue
        lineno = text.count("\n", 0, m.start()) + 1
        if key is None or key.endswith("."):     # 变量键 / 前缀拼接（"a.b." + n）
            dynamic.append((rel, lineno, lookup))
            continue
        judged_calls += 1
        exercised.add(lookup)
        seen[key] = seen.get(key, 0) + 1
        if key not in paths:
            missing.append(f"{rel}:{lineno}  {lookup} 的键 `{key}` 在 balance.json 中不存在（将静默用代码默认值）")

if not seen:
    print("::error::未捕获到任何 balance 字面键（访问器正则或扫描范围变了？门禁需同步）——拒绝判 clean")
    sys.exit(1)
# 每族访问器都得真判到键：数量级漂移（某一族整体不再命中）时总数看不出异常，
# 「总数照旧全绿、整族漏判」正是本次修的形态（带接收者调用整块漏判仍判 clean）。
idle = sorted(set(BALANCE_ACCESSORS) - exercised)
if idle:
    for name in idle:
        print(f"::error::白名单访问器 `{name}` 一处字面键都没判到——正则/命名漂移或该包装器已删除；"
              "门禁需同步，不要留着不生效的登记项")
    print(f"balance-keys gate: FAILED（{len(idle)} 个白名单访问器零命中）")
    sys.exit(1)
if unregistered:
    for u in unregistered[:20]:
        print("::error::" + u)
    print(f"balance-keys gate: FAILED（{len(unregistered)} 个未登记的 Cfg 族调用）")
    sys.exit(1)
if missing:
    for m in missing[:40]:
        print("::error::" + m)
    print(f"balance-keys gate: FAILED（{len(missing)} 项 / {judged_calls} 处字面键调用、{len(seen)} 个键）")
    sys.exit(1)
print(f"balance-keys gate: clean（{judged_calls} 处字面键调用 / {len(seen)} 个字面键全部完整命中 balance.json；"
      f"拼接或变量键 {len(dynamic)} 处不参与判定）")
PY
