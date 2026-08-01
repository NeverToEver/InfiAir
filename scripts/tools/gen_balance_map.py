#!/usr/bin/env python3
"""生成 docs/BALANCE_MAP.md（数值位置地图）

扫描 scripts/ 与 autoload/ 下全部 GameState.cfg() 调用点，生成可维护的数值索引：
- 静态键：json 路径、回退默认值表达式、调用位置（文件:行），并标注 json 中是否存在该键
  （缺失 = 走脚本回退，新增/改名时需双写检查）；
- 动态拼接键（如 player.aim_assist.levels.<level>.frame_pad）：单独列出前缀；
- 反查：balance.json 中未被任何静态 cfg() 引用的叶子键（可能经动态键/整段读取使用，
  也可能是死键，人工判断）。

修改数值或新增/改名键后运行：python3 scripts/tools/gen_balance_map.py
"""

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "docs" / "BALANCE_MAP.md"
SCAN_DIRS = [ROOT / "scripts", ROOT / "autoload"]

# 静态调用：GameState.cfg("player.fuel.drain", FUEL_DRAIN)（默认值可能跨行/含嵌套调用）
RE_STATIC = re.compile(r'GameState\.cfg\(\s*"([^"]+)"\s*(?:,\s*(.*?))?\)', re.DOTALL)
# autoload/game_state.gd 内部对 cfg() 的裸调用（无前缀；排除函数定义行）
RE_BARE = re.compile(r'(?<![\w.])cfg\(\s*"([^"]+)"\s*(?:,\s*(.*?))?\)', re.DOTALL)
# 动态调用：GameState.cfg("player.aim_assist.levels." + ...)（字符串后直接跟拼接）
RE_DYNAMIC = re.compile(r'GameState\.cfg\(\s*"([^"]+)"\s*\+')
# 格式化动态键：GameState.cfg("boss.phases.type%d" % boss_type, ...) → 前缀取 % 之前
RE_FORMAT = re.compile(r'GameState\.cfg\(\s*"([^"]*?)%[^"]*"\s*%')
# 前缀变量模式：var base := "player.aim_assist.levels." + String(x) + "."
# → 后续 GameState.cfg(base + "frame_pad", ...)。RE_PREFIX_VAR 捕获 {变量名: 字面前缀}；
# RE_CFG_WITH_VAR 把用该变量作首参的 cfg 调用登记为动态前缀（P1-3 起 aim_frame_layer 采用此写法）。
RE_PREFIX_VAR = re.compile(r'var\s+(\w+)\s*(?::\s*[\w.]*\s*)?=\s*"([^"]+)"\s*\+')
RE_CFG_WITH_VAR = re.compile(r'GameState\.cfg\(\s*(\w+)\s*\+')


def _in_comment(text: str, pos: int) -> bool:
    """匹配点所在行是否以 # 开头（跳过注释里的示例代码）。"""
    line_start = text.rfind("\n", 0, pos) + 1
    return text[line_start:pos].lstrip().startswith("#")


def json_leaves(node: object, prefix: str = "") -> list[str]:
    if isinstance(node, dict):
        return [k for key, v in node.items() for k in json_leaves(v, f"{prefix}{key}.")]
    if isinstance(node, list):
        if node and isinstance(node[0], (dict, list)):
            # 对象数组（如 enemies.types）：每个元素按同构展开，用 [*] 代表下标
            return [k for i, item in enumerate(node) for k in json_leaves(item, f"{prefix}[*].")]
        return [prefix.rstrip(".")]
    return [prefix.rstrip(".")]


def json_get(node: object, path: str) -> bool:
    cur = node
    for part in path.split("."):
        if isinstance(cur, dict) and part in cur:
            cur = cur[part]
        else:
            return False
    return True


def main() -> None:
    balance = json.loads((ROOT / "data" / "balance.json").read_text(encoding="utf-8"))

    static_calls: list[tuple[str, int, str, str]] = []  # file, line, key, default
    dynamic_prefixes: set[str] = set()
    for d in SCAN_DIRS:
        for gd in sorted(d.rglob("*.gd")):
            rel = gd.relative_to(ROOT)
            text = gd.read_text(encoding="utf-8")
            patterns = [RE_STATIC]
            if gd.name in ("game_state.gd", "balance_service.gd"):
                # autoload 内部裸 cfg() 调用 + BalanceService（A2 剥离后裸 cfg() 承载在服务类）
                patterns.append(RE_BARE)
            for pat in patterns:
                for m in pat.finditer(text):
                    if _in_comment(text, m.start()) or text[max(0, m.start() - 5):m.start()].endswith("func "):
                        continue
                    line = text.count("\n", 0, m.start()) + 1
                    default = re.sub(r"\s+", " ", (m.group(2) or "—").strip())
                    if len(default) > 60:
                        default = default[:57] + "..."
                    static_calls.append((str(rel), line, m.group(1), default))
            for m in RE_DYNAMIC.finditer(text):
                if not _in_comment(text, m.start()):
                    dynamic_prefixes.add(m.group(1))
            for m in RE_FORMAT.finditer(text):
                if not _in_comment(text, m.start()):
                    dynamic_prefixes.add(m.group(1))
            # 前缀变量模式：先收集 var xxx := "前缀" + …，再把 cfg(xxx + …) 记为动态前缀
            prefix_vars: dict[str, str] = {}
            for m in RE_PREFIX_VAR.finditer(text):
                if not _in_comment(text, m.start()):
                    prefix_vars[m.group(1)] = m.group(2)
            for m in RE_CFG_WITH_VAR.finditer(text):
                if not _in_comment(text, m.start()) and m.group(1) in prefix_vars:
                    dynamic_prefixes.add(prefix_vars[m.group(1)])

    referenced = {key for _, _, key, _ in static_calls}
    missing_in_json = [(f, ln, k, d) for f, ln, k, d in static_calls if not json_get(balance, k)]

    def covered_by_dynamic(leaf: str) -> bool:
        return any(leaf.startswith(p) for p in dynamic_prefixes)

    # 整段读取的键（cfg 拿到 dict/list 后内部叶子不再单独引用）：若叶子的任一祖先被引用则视为已覆盖
    def covered_by_ancestor(leaf: str) -> bool:
        parts = leaf.replace("[*].", "").split(".")
        for i in range(1, len(parts)):
            if ".".join(parts[:i]) in referenced:
                return True
        return False

    unreferenced = [
        leaf for leaf in json_leaves(balance)
        if leaf.replace("[*]", "0") not in referenced
        and leaf not in referenced
        and not covered_by_dynamic(leaf)
        and not covered_by_ancestor(leaf)
    ]

    lines: list[str] = []
    lines.append("# BALANCE_MAP — 数值位置地图")
    lines.append("")
    lines.append("> 本文件由 `python3 scripts/tools/gen_balance_map.py` 扫描生成，请勿手改；")
    lines.append("> 新增/改名数值键或调整 cfg() 调用后重新运行生成器。")
    lines.append("")
    lines.append("## 怎么改数值")
    lines.append("")
    lines.append("- 运行时数值的唯一来源是 `data/balance.json`；推荐用 `python3 scripts/tools/balance_editor.py` 在浏览器里编辑（改动高亮、类型校验、自动备份）。")
    lines.append("- 脚本侧的 `GameState.cfg(\"键路径\", 回退值)` 仅在 json 缺键/损坏时兜底；新增或调整数值按 AGENTS.md 约定保持 json 与回退值一致。")
    lines.append("- 高频 `_process` 路径的数值在 `_ready()`/`_load_balance()` 一次缓存，不要每帧查。")
    lines.append("")
    lines.append("## 静态 cfg() 调用点（按文件分组）")
    lines.append("")
    cur_file = None
    for f, ln, key, default in static_calls:
        if f != cur_file:
            if cur_file is not None:
                lines.append("")
            cur_file = f
            lines.append(f"### `{f}`")
            lines.append("")
            lines.append("| 行 | json 键路径 | 脚本回退值 |")
            lines.append("| --- | --- | --- |")
        lines.append(f"| {ln} | `{key}` | `{default}` |")
    lines.append("")
    lines.append("## 动态拼接键前缀")
    lines.append("")
    for p in sorted(dynamic_prefixes):
        lines.append(f"- `{p}…`")
    lines.append("")
    lines.append("## json 中存在但脚本未静态引用的键")
    lines.append("")
    lines.append("（经动态键或整段读取覆盖的不列出；剩下的请人工判断是否为死键）")
    lines.append("")
    for leaf in sorted(set(unreferenced)):
        lines.append(f"- `{leaf}`")
    lines.append("")
    lines.append("## 脚本引用但 json 缺失的键（走回退值，建议补进 json 或确认为有意兜底）")
    lines.append("")
    seen = set()
    for f, ln, key, default in missing_in_json:
        if key in seen:
            continue
        seen.add(key)
        lines.append(f"- `{key}`（`{f}:{ln}`，回退 `{default}`）")
    lines.append("")

    OUT.write_text("\n".join(lines), encoding="utf-8")
    print(f"[gen_balance_map] {len(static_calls)} 静态调用, {len(dynamic_prefixes)} 动态前缀, "
          f"{len(set(unreferenced))} 未引用键, {len(seen)} 缺失键 → {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
