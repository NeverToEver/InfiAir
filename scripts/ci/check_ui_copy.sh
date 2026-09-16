#!/usr/bin/env bash
# 玩家可见文案门禁（data/translations.csv）。
# 用法：bash scripts/ci/check_ui_copy.sh
# 拦截六类问题——它们都会让玩家直接看到「不该出现的东西」：
#   1) 开发措辞 / 流程术语（例：「（左侧轮盘同步下钻）」「见 ROADMAP」）
#   2) 已移除系统的残留词条（局外成长、排行榜、账户…）
#   3) 术语漂移：玩家文案出现「敌人 / 对局」（AGENTS §9 规范写法「敌机 / 本局」）
#   4) 空字段：zh/en 任一为空，该语言下界面留白
#   5) 缺键：C# 静态 Tr("KEY") 在表中无行 → 游戏直接显示键名本身
#   6) 缺键（动态）：Tr($"PREFIX_{x}") / Tr("PREFIX_" + x) 这类拼接键，取值源展开后逐个判存在
# 事件台词/事件条走「键名字面量」而非 Tr()（CommOverlay.ShowLine、Hud.ShowEventBar 内部再翻），
# 它们的缺键同样显示键名本身，故一并按同一口径判（TEXT_KEY_CALL）。
# 扫描面：目前只扫 csharp/ 的 .cs。scenes/*.tscn 里的硬编码文案不在判定内（需先清掉
# scenes/main.tscn 的硬编码文案再扩面，属另一项改动）。
# 零命中守卫：静态键与动态候选键都是「本门禁的判据来源」，取不到就谈不上「无缺键」——静态两条
# 正则各自的命中集非空、静态键总数不得低于 MIN_STATIC_KEYS、动态候选键总数不得低于
# MIN_DYNAMIC_KEYS（理由见各常量处）；登记表里的每个前缀都必须真的有站点（已登记站点消失判红，
# 与 check_realtime_allowlist.sh 同口径——防改名/搬走后本表变成死数据）。
# 口径边界（刻意排除的形态，不静默放过）：
#   - 含空格或小写以外的非标识符字符的拼接文本（如 Tr("剩 " + n + " 秒")）不认作键形态：与
#     「拼给人读的句子」无法区分，误报率高；本库现无此类形态，出现时按人工项过目。
#   - 键名来自运行期数据（读档字段、玩家输入）且无静态枚举的拼接，本门禁判不到；这类取值源
#     须先落一处静态表再走本判据（单源）。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

python3 - <<'PY'
import csv, json, pathlib, re, sys

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
# 只认完整实参：Tr("KEY") / Tr("KEY", …)，排除 Tr("ACT_" + name) 这类前缀拼接（拼接键走下面第五段）
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

CS_FILES = [p for p in sorted((root / "csharp").rglob("*.cs"))
            if "obj" not in p.parts and "bin" not in p.parts]

used: set[str] = set()
tr_used: set[str] = set()
text_used: set[str] = set()
sources: dict[str, str] = {}
for path in CS_FILES:
    source = path.read_text(encoding="utf-8", errors="ignore")
    sources[path.as_posix()] = source
    tr_used.update(TR_ARG.findall(source))
    for match in TEXT_KEY_CALL.finditer(source):
        text_used.update(group for group in match.groups() if group)
used = tr_used | text_used

# ---------------- 动态拼接键：站点抽取 + 取值源展开 ----------------
# 静态实参正则「Tr("KEY")」抓不到拼接键——键名写错时 Tr 原样返回键名（玩家直接看到
# MENU_HINT_RESUME），而静态面全绿（假绿比没门禁更糟）。这里按「前缀字面段」把取值源
# 展开成候选键逐个判存在：前缀 → 取值源（结构单例 / 能力表 / 配置表，一律取单源，
# 不另抄一份清单），后缀取自站点自身（后缀写错 → 候选键不存在 → 红，指向站点行）。

def read(rel: str) -> str:
    return (root / rel).read_text(encoding="utf-8", errors="ignore")


def block(pattern: str, rel: str, what: str) -> str:
    """按正则取登记表所在代码块；取不到即显式失败（结构改名/搬走后零展开会变成假绿）。"""
    match = re.search(pattern, read(rel), re.S)
    if not match:
        raise LookupError(f"{rel} 里取不到{what}")
    return match.group(1)


def strings_in(text: str) -> list[str]:
    return re.findall(r'new StringName\("([a-z0-9_]+)"\)', text)


def _guard(values: list[str], what: str) -> list[str]:
    if not values:
        raise LookupError(f"{what}展开出零个取值")
    return values


def talent_node_ids() -> list[str]:
    src = read("csharp/core/Talent/TalentTree.cs")
    ids = [i for m in re.finditer(r"NodeIds\s*=\s*new\[\]\s*\{([^}]*)\}", src)
           for i in re.findall(r'"([a-z0-9_]+)"', m.group(1))]
    return _guard(ids, "天赋树 NodeIds")


def rebindable_actions() -> list[str]:
    return _guard(strings_in(block(
        r"REBINDABLE_ACTIONS[^=]*=\s*new\(\)\s*\{(.*?)\n    \};",
        "csharp/godot/InputBindingsService.cs", "REBINDABLE_ACTIONS 表")), "可改键动作清单")


def difficulty_ids() -> list[str]:
    return _guard(strings_in(block(
        r"DIFFICULTY_ORDER[^=]*=\s*new\(\)\s*\{(.*?)\};",
        "csharp/godot/GameState.Constants.cs", "DIFFICULTY_ORDER 表")), "难度档位表")


def menu_hint_ids() -> list[str]:
    # 轮盘根级选项的 Id（RadialWheelOption.Id 为裸字符串，不是 StringName）
    return _guard(re.findall(r'Id\s*=\s*"([a-z0-9_]+)"', block(
        r"RebuildMenu\(\)\s*\{(.*?)\n    \}",
        "csharp/godot/PauseUi.cs", "RebuildMenu 选项表")), "暂停页轮盘选项 Id")


def fps_cap_words() -> list[str]:
    # 帧率档里的纯数值档（fps30/45/…）按钮文本就是数字，不建文案键；只有词形档走键
    values = [v for v in strings_in(block(
        r"FPS_CAP_ORDER[^=]*=\s*new\(\)\s*\{(.*?)\n    \};",
        "csharp/godot/SettingsService.cs", "FPS_CAP_ORDER 表")) if not re.fullmatch(r"fps\d+", v)]
    return _guard(values, "帧率上限词形档")


def aim_levels() -> list[str]:
    return _guard(strings_in(block(
        r"AIM_ASSIST_ORDER[^=]*=\s*new\(\)\s*\{(.*?)\n    \};",
        "csharp/godot/SettingsService.cs", "AIM_ASSIST_ORDER 表")), "辅助瞄准档位表")


def view_levels() -> list[str]:
    return _guard(strings_in(block(
        r"VIEW_ZOOM_ORDER[^=]*=\s*new\(\)\s*\{(.*?)\n    \};",
        "csharp/godot/SettingsService.cs", "VIEW_ZOOM_ORDER 表")), "视角档位表")


def window_modes() -> list[str]:
    return _guard(strings_in(block(
        r"foreach \(var mode in new\[\]\s*\{(.*?)\}\)",
        "csharp/godot/SettingsUi.cs", "窗口模式选项表")), "窗口模式表")


def mission_ids() -> list[str]:
    return _guard(re.findall(
        r'\["id"\]\s*=\s*new StringName\("([a-z0-9_]+)"\)',
        block(r"BuildMissionPool\(\)\s*=>\s*new\(\)\s*\{(.*?)\n    \};",
              "csharp/godot/GameState.Constants.cs", "MISSION_POOL 定义")), "任务池 id")


def fog_event_ids() -> list[str]:
    # EVENT_FACTORIES 初始字面量＝迷雾事件注册表（遭遇经 register_encounter 注入，不在其内）
    return _guard(re.findall(
        r'\[new StringName\("([a-z0-9_]+)"\)\]\s*=\s*Callable',
        block(r"EVENT_FACTORIES[^=]*=\s*new\(\)\s*\{(.*?)\n    \};",
              "csharp/godot/GameEventManager.cs", "EVENT_FACTORIES 注册表")), "迷雾事件注册表")


def tier_indexes() -> list[str]:
    # DifficultyTier.IndexFor 的值域是 [0, 阈值表长度−1]；阈值表是 balance.json 单源
    thresholds = json.loads(read("data/balance.json"))["progression"]["tier_thresholds"]
    return _guard([str(i) for i in range(len(thresholds))], "难度档位数")


def boss_types() -> list[str]:
    phases = json.loads(read("data/balance.json"))["boss"]["phases"]
    return _guard(sorted(str(int(k[4:])) for k in phases if re.fullmatch(r"type\d+", k)), "Boss 类型表")


def etq_indexes() -> list[str]:
    # 台词池按循环上限枚举（EliteTurretEvent.Start：for i < N 的循环体内 pool.Add("ETQ_" + (i + 1))）
    bound = re.search(r'for \(var i = 0; i < (\d+); i\+\+\)\s*\{\s*pool\.Add\("ETQ_" \+ \(i \+ 1\)\)',
                      read("csharp/godot/EliteTurretEvent.cs"))
    if not bound:
        raise LookupError("EliteTurretEvent 里取不到台词池循环上限")
    return _guard([str(i) for i in range(1, int(bound.group(1)) + 1)], "精英炮塔台词池")


# 前缀 → (取值源说明, 取值源)。新增拼接键族必须在此登记取值源（单源），否则本门禁判红。
DYNAMIC_SOURCES: dict[str, tuple[str, object]] = {
    "AUG_": ("天赋树结构单例 TalentTree 的节点 id", talent_node_ids),
    "ACT_": ("GameState.REBINDABLE_ACTIONS 可改键动作清单", rebindable_actions),
    "DIFF_": ("GameState.DIFFICULTY_ORDER 难度档位表", difficulty_ids),
    "MENU_HINT_": ("PauseUi 轮盘选项 Id", menu_hint_ids),
    "SET_FPS_": ("SettingsService.FPS_CAP_ORDER（纯数值档不建键）", fps_cap_words),
    "SET_AIM_": ("SettingsService.AIM_ASSIST_ORDER 档位表", aim_levels),
    "SET_VIEW_": ("SettingsService.VIEW_ZOOM_ORDER 档位表", view_levels),
    "SET_WINDOW_MODE_": ("SettingsUi 窗口模式选项表", window_modes),
    "MISSION_": ("GameState.MISSION_POOL 任务池 id", mission_ids),
    "FOG_EVENT_": ("GameEventManager.EVENT_FACTORIES 迷雾事件注册表", fog_event_ids),
    "DIFF_TIER_": ("balance.json progression.tier_thresholds 档位数", tier_indexes),
    "BOSS_TYPE_": ("balance.json boss.phases.typeN 的类型号", boss_types),
    "ETQ_": ("EliteTurretEvent 台词池循环上限", etq_indexes),
}

value_cache: dict[str, list[str] | None] = {}


def expanded_values(prefix: str) -> list[str] | None:
    """前缀对应的取值（失败时登记错误并返回 None，不抛——一次跑完给出全部问题）。"""
    if prefix in value_cache:
        return value_cache[prefix]
    try:
        value_cache[prefix] = DYNAMIC_SOURCES[prefix][1]()  # type: ignore[operator]
    except (LookupError, OSError, KeyError, ValueError, TypeError) as exc:
        errors.append(f"动态拼接键前缀 `{prefix}` 的取值源取不到判据：{exc}"
                      f"（单源搬家/改名后本门禁须同步，不得静默零展开）")
        value_cache[prefix] = None
    return value_cache[prefix]


def paren_arg(text: str, open_index: int) -> str | None:
    """取 `(` 起的配对实参文本（识别字符串与嵌套括号）。"""
    depth = 0
    in_str = False
    escaped = False
    for i in range(open_index, len(text)):
        ch = text[i]
        if in_str:
            if escaped:
                escaped = False
            elif ch == "\\":
                escaped = True
            elif ch == '"':
                in_str = False
            continue
        if ch == '"':
            in_str = True
        elif ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
            if depth == 0:
                return text[open_index + 1:i]
    return None


def key_template_pieces(arg: str) -> list[str] | None:
    """动态实参 → 字面段（洞之间/两侧）。非键形态（拼给玩家读的句子等）返回 None。"""
    if arg.startswith('$"'):
        if arg.count("{") != 1 or not arg.endswith('"'):
            return None
        pieces = re.split(r"\{[^{}]*\}", arg[2:-1])
    else:
        pieces = re.findall(r'"([^"]*)"', arg)
    if not pieces or not pieces[0]:
        return None
    # 键形态＝全大写/数字/下划线组成的前缀段（含空格或小写的拼接文本视为普通文本，不判）
    return pieces if KEY_OK.fullmatch(pieces[0]) else None


sites: list[tuple[str, int, str, str]] = []
for path in CS_FILES:
    source = sources[path.as_posix()]
    rel = path.relative_to(root).as_posix()
    for match in re.finditer(r"\bTr\(", source):
        arg = paren_arg(source, match.end() - 1)
        if arg is None:
            errors.append(f"{rel}: 第 {source.count(chr(10), 0, match.start()) + 1} 行 Tr( 的实参括号不配对"
                          "（本门禁取不到判据，须同步）")
            continue
        arg = arg.strip()
        if not (arg.startswith('$"') or "+" in arg):
            continue
        pieces = key_template_pieces(arg)
        if pieces is None:
            if arg.startswith('$"') or re.fullmatch(r'(?:"[^"]*"\s*\+?\s*)+$', arg):
                errors.append(f"{rel}: 第 {source.count(chr(10), 0, match.start()) + 1} 行动态拼接键形态无法展开："
                              f"{arg}——多插值/无前缀字面段须先给出静态取值源")
            continue
        line = source.count("\n", 0, match.start()) + 1
        sites.append((rel, line, pieces[0], pieces[1] if len(pieces) > 1 else ""))
    # Tr() 之外的键形态拼接（如事件台词池 pool.Add("ETQ_" + (i + 1))）：同样必须登记取值源
    for line_no, line in enumerate(source.splitlines(), 1):
        if "Tr(" in line:
            continue
        for lit in re.findall(r'"([A-Z][A-Z0-9_]*_)"\s*\+', line):
            sites.append((rel, line_no, lit, ""))

candidates: dict[str, tuple[str, int]] = {}
for rel, line, prefix, suffix in sites:
    if prefix not in DYNAMIC_SOURCES:
        errors.append(f"{rel}:{line} 出现未登记的动态拼接键前缀 `{prefix}`——"
                      "本门禁取不到取值源（须在 DYNAMIC_SOURCES 登记单源，或确认它不是翻译键）")
        continue
    for value in expanded_values(prefix) or []:
        candidates.setdefault(prefix + value.upper() + suffix, (rel, line))

for prefix, (description, _) in DYNAMIC_SOURCES.items():
    hit_count = sum(1 for _, _, site_prefix, _ in sites if site_prefix == prefix)
    if hit_count == 0:
        errors.append(f"动态拼接键前缀 `{prefix}` 在 csharp/ 下取不到任何站点（取值源：{description}）——"
                      "站点被改名/搬走或本表成了死数据，须同步本门禁，不得静默放行")

# 零命中守卫：判据取不到必须显式失败（AGENTS §6 铁律 2）。
#   - 两个抽取正则各自的命中集非空：任一条静默失效都会让对应面（Tr / 直接吃键的 API）退出判定；
#   - 静态键总数下限 MIN_STATIC_KEYS：单项守卫抓不到「访问器整体改名」这类漂移——把全库 Tr( 改名
#     成 Loc( 后仍能从 ShowLine/ShowEventBar 抓到十几个键，差集照样为空。当前实测 223 个静态键
#     （Tr 213 + 直接吃键 10），取 150：掉到 150 以下意味着三分之一以上的调用点消失，那是重构
#     规模的口径变更，须人工确认后连同门禁一起改，而不是静默放行。
#   - 动态候选键总数下限 MIN_DYNAMIC_KEYS：同款理由——前缀正则集体漂移（取值源取空、站点正则
#     失配）会让动态面安静退出判定。当前实测 124 个候选键（13 个前缀族），取 100。
MIN_STATIC_KEYS = 150
MIN_DYNAMIC_KEYS = 100
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
if len(candidates) < MIN_DYNAMIC_KEYS:
    print(f"::error::动态拼接键候选只有 {len(candidates)} 个（下限 {MIN_DYNAMIC_KEYS}）"
          f"——站点抽取或取值源展开失效？门禁需同步，不要放宽下限")
    sys.exit(1)

for key in sorted(used - keys):
    errors.append(f"`{key}` 被 C# 引用但表中无此键（界面会显示键名本身）")
for key, (rel, line) in sorted(candidates.items()):
    if key not in keys:
        errors.append(f"`{key}` 由 {rel}:{line} 的动态拼接键展开而来，但表中无此键"
                      "（界面会显示键名本身）")

if errors:
    for message in errors[:40]:
        print("::error::" + message)
    print(f"ui-copy gate: FAILED（{len(errors)} 项）")
    sys.exit(1)
print(f"ui-copy gate: clean（{len(rows)} 条文案 / {len(used)} 个静态键"
      f"（Tr {len(tr_used)} / 直接吃键 {len(text_used)}）/ {len(candidates)} 个动态拼接键"
      f"（{len(sites)} 个站点））")
PY
