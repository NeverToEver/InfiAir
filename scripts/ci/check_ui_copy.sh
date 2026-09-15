#!/usr/bin/env bash
# 玩家可见文案门禁（data/translations.csv）。
# 用法：bash scripts/ci/check_ui_copy.sh
# 拦截五类问题——它们都会让玩家直接看到「不该出现的东西」：
#   1) 开发措辞 / 流程术语（例：「（左侧轮盘同步下钻）」「见 ROADMAP」）
#   2) 已移除系统的残留词条（局外成长、排行榜、账户…）
#   3) 术语漂移：玩家文案出现「敌人 / 对局」（AGENTS §9 规范写法「敌机 / 本局」）
#   4) 空字段：zh/en 任一为空，该语言下界面留白
#   5) 缺键：C# 静态 Tr("KEY") 在表中无行 → 游戏直接显示键名本身
# 动态拼接键（Tr("ACT_" + name)）不参与缺键判定，故只认完整调用实参形态。
# 事件台词/事件条走「键名字面量」而非 Tr()（CommOverlay.ShowLine、Hud.ShowEventBar 内部再翻），
# 它们的缺键同样显示键名本身，故一并按同一口径判（TEXT_KEY_CALL）。
# 扫描面：目前只扫 csharp/ 的 .cs。scenes/*.tscn 里的硬编码文案不在判定内（需先清掉
# scenes/main.tscn 的硬编码文案再扩面，属另一项改动）。
# 零命中守卫：静态键是「本门禁的判据来源」，取不到就谈不上「无缺键」——两个正则各自的命中集都
# 不得为空，且静态键总数不得低于 MIN_STATIC_KEYS（理由见该常量处）。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

python3 - <<'PY'
import csv, pathlib, re, sys

root = pathlib.Path.cwd()
csv_path = root / "data" / "translations.csv"

# 高信号词表：玩家文案里出现即为泄漏（宽泛词不入表，避免误伤正常文案）
# 「敌人 / 对局」= 术语漂移（AGENTS §9 表规范写法为「敌机 / 本局」）——玩家文案是术语唯一
# 没有机器副本的一面，漏了就会随新文案回潮；只锁中文词，英文 side 的 enemy 不受影响。
BANNED = re.compile(
    "下钻|轮盘同步|DESIGN_BASELINE|ROADMAP|AGENTS|口径|门禁|幂等|硬编码|占位|"
    "TODO|FIXME|科技点|局外成长|研究所|排行榜|生效上限|结构上限|user://|res://|"
    "敌人|对局"
)
KEY_OK = re.compile(r"[A-Z0-9_]+")
# 只认完整实参：Tr("KEY") / Tr("KEY", …)，排除 Tr("ACT_" + name) 这类前缀拼接
TR_ARG = re.compile(r'Tr\(\s*"([A-Z0-9_]+)"\s*[,)]')
# 直接吃翻译键的 API（键名再经对应组件内部翻译）：键错/缺行同样是玩家直接看到键名。
# 只列「实参就是翻译键」的 API——吃已翻译文本的（Hud.ShowInfoBanner）不入列，避免误判。
TEXT_KEY_CALL = re.compile(
    r'\.ShowLine\(\s*"([A-Z0-9_]+)"\s*[),]'
    r'|\.ShowEventBar\(\s*"([A-Z0-9_]+)"\s*,\s*"([A-Z0-9_]+)"'
)

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
tr_used: set[str] = set()
text_used: set[str] = set()
for path in (root / "csharp").rglob("*.cs"):
    parts = path.parts
    if "obj" in parts or "bin" in parts:
        continue
    source = path.read_text(encoding="utf-8", errors="ignore")
    tr_used.update(TR_ARG.findall(source))
    for match in TEXT_KEY_CALL.finditer(source):
        text_used.update(group for group in match.groups() if group)
used = tr_used | text_used

# 零命中守卫：判据取不到必须显式失败（AGENTS §6 铁律 2）。
#   - 两个抽取正则各自的命中集非空：任一条静默失效都会让对应面（Tr / 直接吃键的 API）退出判定；
#   - 静态键总数下限 MIN_STATIC_KEYS：单项守卫抓不到「访问器整体改名」这类漂移——把全库 Tr( 改名
#     成 Loc( 后仍能从 ShowLine/ShowEventBar 抓到十几个键，差集照样为空。当前实测 225 个静态键
#     （Tr 213 + 直接吃键 12），取 150：掉到 150 以下意味着三分之一以上的调用点消失，那是重构
#     规模的口径变更，须人工确认后连同门禁一起改，而不是静默放行。
MIN_STATIC_KEYS = 150
if not tr_used:
    print("::error::Tr(\"KEY\") 形态一个键都没抓到（访问器改名或正则漂移？）——拒绝判 clean")
    sys.exit(1)
if not text_used:
    print("::error::直接吃翻译键的 API（ShowLine/ShowEventBar）一个键都没抓到（签名变了？）——拒绝判 clean")
    sys.exit(1)
if len(used) < MIN_STATIC_KEYS:
    print(f"::error::静态键只有 {len(used)} 个（下限 {MIN_STATIC_KEYS}；Tr {len(tr_used)} / "
          f"直接吃键 {len(text_used)}）——访问器被整体改名或扫描面变了？门禁需同步，不要放宽下限")
    sys.exit(1)

for key in sorted(used - keys):
    errors.append(f"`{key}` 被 C# 引用但表中无此键（界面会显示键名本身）")

if errors:
    for message in errors[:40]:
        print("::error::" + message)
    print(f"ui-copy gate: FAILED（{len(errors)} 项）")
    sys.exit(1)
print(f"ui-copy gate: clean（{len(rows)} 条文案 / {len(used)} 个静态键"
      f"（Tr {len(tr_used)} / 直接吃键 {len(text_used)}））")
PY
