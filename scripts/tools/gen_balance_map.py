#!/usr/bin/env python3
"""生成 docs/BALANCE_MAP.md（数值位置地图）

扫描 scripts/、autoload/（历史路径，现已无源文件）与 csharp/ 下全部 GameState.cfg()/GameState.Instance.Cfg() 调用点（M7d 后实际命中 C# 侧），生成可维护的数值索引：
- 静态键：json 路径、回退默认值表达式、调用位置（文件:行），并标注 json 中是否存在该键
  （缺失 = 走脚本回退，新增/改名时需双写检查）；
- 动态拼接键（如 player.aim_assist.levels.<level>.frame_pad）：单独列出前缀；
- 反查：balance.json 中未被任何静态 cfg() 引用的叶子键（可能经动态键/整段读取使用，
  也可能是死键，人工判断）。

修改数值或新增/改名键后运行：python3 scripts/tools/gen_balance_map.py
"""

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "docs" / "BALANCE_MAP.md"
SCAN_DIRS = [ROOT / "scripts", ROOT / "autoload", ROOT / "csharp"]  # M7d：零 GDScript 后扫 C# 调用点

# 静态调用：GameState.Instance.Cfg("player.fuel.drain", FUEL_DRAIN)（默认值可能跨行/含嵌套调用）。
# V 系列：前缀改必选——消除与 RE_BARE 对豁免文件裸调用的双重计数（原 GameState.cs 段 16 条重复）
RE_STATIC = re.compile(r'GameState\.Instance\.Cfg\(\s*"([^"]+)"\s*(?:,\s*(.*?))?\)', re.DOTALL)
RE_STATIC_GD = re.compile(r'GameState\.cfg\(\s*"([^"]+)"\s*(?:,\s*(.*?))?\)', re.DOTALL)
# autoload/game_state.gd 内部对 cfg() 的裸调用（无前缀；排除函数定义行）
RE_BARE = re.compile(r'(?<![\w.])(?:cfg|Cfg)\(\s*"([^"]+)"\s*(?:,\s*(.*?))?\)', re.DOTALL)
# V 系列：MetaHealthFX.CfgVal 私有助手（内部转发 GameState.Instance.Cfg）——37 处 meta_health 键
RE_CFG_VAL = re.compile(r'CfgVal\(\s*"([^"]+)"\s*(?:,\s*(.*?))?\)', re.DOTALL)
# V 系列：BalanceService._interop.Resolve（PathResolver 核心解析）——hp/damage ramp 键
RE_RESOLVE = re.compile(r'\.Resolve\(\s*"([^"]+)"\s*(?:,\s*(.*?))?\)', re.DOTALL)
# 动态调用：GameState.cfg("player.aim_assist.levels." + ...)（字符串后直接跟拼接；C# 侧 [Cc]fg 大小写变体）
RE_DYNAMIC = re.compile(r'GameState\.Instance\.(?:[Cc]fg|CfgVal)\(\s*"([^"]+)"\s*\+')
# 格式化动态键：GameState.cfg("boss.phases.type%d" % boss_type, ...) → 前缀取 % 之前
RE_FORMAT = re.compile(r'GameState\.Instance\.(?:[Cc]fg|CfgVal)\(\s*"([^"]*?)%[^"]*"\s*%')
# 前缀变量模式：var base := "player.aim_assist.levels." + String(x) + "."
# → 后续 GameState.cfg(base + "frame_pad", ...)。RE_PREFIX_VAR 捕获 {变量名: 字面前缀}；
# RE_CFG_WITH_VAR 把用该变量作首参的 cfg 调用登记为动态前缀（P1-3 起 aim_frame_layer 采用此写法）
RE_PREFIX_VAR = re.compile(r'var\s+(\w+)\s*(?::\s*[\w.]*\s*)?=\s*"([^"]+)"\s*\+')
RE_CFG_WITH_VAR = re.compile(r'GameState\.Instance\.(?:[Cc]fg|CfgVal)\(\s*(\w+)\s*\+')
# 声明式效果表（player.gd BUFF_EFFECTS 等）："cfg": "buffs.rapid_fire.factor" 字符串键。
# L09（2026-08-03 审查）：A3 收敛声明式效果表后此类键不经 GameState.cfg 调用，
# 原扫描全盲区——7 个效果表键不参与缺失键检测（拼错/改名不报）、被消费键误列疑似死键
RE_EFFECT_CFG = re.compile(r'"cfg"\s*:\s*"([^"]+)"')
# V 系列：C# 效果表形态 ["cfg"] = "buffs.rapid_fire.factor"（Player.cs BuffEffects 等 14 键）
RE_EFFECT_CFG_CS = re.compile(r'\["cfg"\]\s*=\s*"([^"]+)"')


def _in_comment(text: str, pos: int) -> bool:
    """匹配点所在行是否以 # 开头（跳过注释里的示例代码）。"""
    line_start = text.rfind("\n", 0, pos) + 1
    stripped = text[line_start:pos].lstrip()
    return stripped.startswith("#") or stripped.startswith("//")  # M7d：支持 C# // 注释


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
    try:
        balance = json.loads((ROOT / "data" / "balance.json").read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError) as exc:
        # P4（2026-08-05）：损坏/缺失 balance.json 时友好报错退出（原裸异常无上下文）
        print(f"[gen_balance_map] ERROR: data/balance.json 读取或解析失败: {exc}")
        sys.exit(1)

    static_calls: list[tuple[str, int, str, str]] = []  # file, line, key, default
    dynamic_prefixes: set[str] = set()
    for d in SCAN_DIRS:
        for src in sorted(list(d.rglob("*.gd")) + list(d.rglob("*.cs"))):
            if "tests" in src.parts:
                continue  # V 系列：跳过测试目录（故意回退用例会误报缺失键；引用不代表生产消费）
            rel = src.relative_to(ROOT)
            text = src.read_text(encoding="utf-8")
            patterns = [RE_STATIC, RE_EFFECT_CFG, RE_EFFECT_CFG_CS, RE_CFG_VAL, RE_RESOLVE]
            if src.suffix == ".gd" and src.name in ("game_state.gd", "balance_service.gd"):
                # autoload 内部裸 cfg() 调用 + BalanceService（A2 剥离后裸 cfg() 承载在服务类）
                patterns.append(RE_BARE)
            elif src.suffix == ".cs" and (src.name in ("GameState.cs", "BalanceService.cs") or src.name.startswith("GameState.")):
                # C# 侧内部裸 Cfg() 调用（M7d；Y 系列 2026-08-09：GameState 按域拆分为
                # GameState.*.cs partial 文件后裸 Cfg 调用分散——文件名前缀匹配）
                patterns.append(RE_BARE)
            if src.suffix == ".gd":
                patterns.append(RE_STATIC_GD)
            for pat in patterns:
                for m in pat.finditer(text):
                    if _in_comment(text, m.start()) or text[max(0, m.start() - 5):m.start()].endswith("func "):
                        continue
                    line = text.count("\n", 0, m.start()) + 1
                    # 效果表形态（两变体）仅一个捕获组（键），无默认值列
                    default = "—" if pat in (RE_EFFECT_CFG, RE_EFFECT_CFG_CS) else re.sub(r"\s+", " ", (m.group(2) or "—").strip())
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
    lines.append("- 代码侧的 `GameState.Instance.Cfg(\"键路径\", 回退值)` 仅在 json 缺键/损坏时兜底；新增或调整数值按 AGENTS.md 约定保持 json 与回退值一致。")
    lines.append("- 高频 `_Process` 路径的数值在 `_Ready()`/`LoadBalance()` 一次缓存，不要每帧查。")
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
