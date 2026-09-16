"""C# 源文件的门禁共用词法处理：剥注释（保留字符串与偏移）+ 定位条件编译段。

为什么单独抽成模块：多个对账门禁都要「先按 C# 词法剥掉注释、再取字面量」，同一口径抄若干份
必然漂移——漂移的代价是门禁之间判定不一致（一份把注释里的键当代码、另一份不当），而这类不一致
不会报错，只会让某个门禁静默放松。口径来源是 check_balance_keys.sh 的内联实现。

口径（调用方按同一份语义取判据）：
  - 字符串字面量内容**保留**：键名与数值本身就是字符串/数字字面量，剥掉就没得判；注释换成空格。
  - 替换**不改变文本长度与换行位置**，调用方沿用的偏移与行号保持有效。
  - `://` 是协议分隔不是注释起点（`res://`、`https://`）。
  - `/* */` 与 `//` 一律剥（注释掉的写入/读取不得参与对账）。
  - 条件编译（`#if/#elif/#else/#endif`）**不剥也不猜分支**：由 conditional_lines() 定位，
    调用方显式报红——「按文本对账」与「编译进产物的代码」在条件编译下不再等价，
    静默少几行就是把假绿换成漏判（AGENTS §6 铁律 2）。
"""

from __future__ import annotations

import re

DIRECTIVE = re.compile(r"^[ \t]*#[ \t]*(if|elif|else|endif)\b(.*)")


def skip_literal(text: str, i: int) -> int:
    """跳过 text[i] 起的字符串/字符字面量（含 @"..." 逐字串与转义），返回结束后的下标。"""
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


def is_literal_start(text: str, i: int) -> bool:
    """text[i] 是否是一个 C# 字面量的起始（普通串、逐字串、字符字面量）。"""
    if text[i] == '"' or text[i] == "'":
        return True
    return text[i] == "@" and i + 1 < len(text) and text[i + 1] == '"'


def strip_comments(src: str) -> tuple[str, list[bool]]:
    """把注释替换成空格（保留长度与换行），并标出每个字符是否落在字符串字面量内。

    返回 (同长度文本, 字符串掩码)；掩码供调用方区分「字面量里的文本」与「代码」。
    """
    out = list(src)
    in_string = [False] * len(src)
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        start = i
        if is_literal_start(src, i):
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
        if c == "/" and i + 1 < n and src[i + 1] == "*":
            end = src.find("*/", i + 2)
            end = n if end < 0 else end + 2
            for j in range(i, end):               # 保留块内换行，只把非换行字符换成空格
                if src[j] != "\n":
                    out[j] = " "
            i = end
            continue
        i += 1
    return "".join(out), in_string


def conditional_lines(text: str, in_string: list[bool] | None = None) -> list[int]:
    """返回落在条件编译段内（含 `#if`~`#endif` 的指令行本身）的 1 基行号，升序去重。

    text 应是剥过注释的文本（注释里的 `#if` 不算）；in_string 缺省时按 text 现算。
    嵌套按深度计；`#elif`/`#else` 不改变深度（同一段内的分支切换）。
    """
    if in_string is None:
        _, in_string = strip_comments(text)
    lines = text.split("\n")
    marked: list[int] = []
    depth = 0
    pos = 0
    for lineno, line in enumerate(lines, 1):
        m = DIRECTIVE.match(line)
        directive = bool(m) and (pos >= len(in_string) or not in_string[pos])
        if depth > 0 or directive:
            marked.append(lineno)
        if directive:
            kind = m.group(1)
            if kind == "if":
                depth += 1
            elif kind == "endif":
                depth = max(0, depth - 1)
        pos += len(line) + 1                       # +1：换行符
    return marked


def directives(text: str, in_string: list[bool] | None = None) -> list[tuple[int, str]]:
    """返回 (1 基行号, 指令原文) 列表，供报红时指明位置与写法。"""
    if in_string is None:
        _, in_string = strip_comments(text)
    out: list[tuple[int, str]] = []
    pos = 0
    for lineno, line in enumerate(text.split("\n"), 1):
        m = DIRECTIVE.match(line)
        if m and (pos >= len(in_string) or not in_string[pos]):
            out.append((lineno, line.strip()))
        pos += len(line) + 1
    return out
