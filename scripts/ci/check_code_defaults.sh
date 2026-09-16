#!/usr/bin/env bash
# 代码默认值对账门禁：C# 内「缺档回退默认值」必须等于 balance.json 定稿值。
# 用法：bash scripts/ci/check_code_defaults.sh
#
# 为什么需要：balance.json 是数值单源，但一批「表式」数值被系统性手抄进 C#，当
# Cfg(key, 代码内默认值) 的回退实参。手抄值与 json 定稿值一旦分叉，正常路径（json 完整）
# 读的是 json，看不出代码默认值已经烂了；只有 json 缺失/损坏时才回退到错值——也就是
# 「用错值且不报错」。编译、单测、冒烟都抓不到（它们跑的是 json 完整路径）。今天零不一致
# 靠人工逐值比对维持，本门禁把它变成自动判定。
#
# 判定：登记表里每个代码字面量都必须等于 balance.json 对应点分路径的定稿值；不等即红，
# 输出「文件:行号 + 键路径 + 两侧取值」，直接可操作（改哪一侧都行，但不能只改一侧）。
# 不做后缀放宽（与 check_balance_keys.sh 同口径：前缀写错、靠尾段撞上的键正是要抓的形态）。
#
# 假绿防线（AGENTS.md §6 铁律 2：取不到判据必须显式失败，假绿比没门禁更糟）：
#   1) balance.json 不存在或解析失败 → 红；
#   2) 登记表里的 C# 文件不存在、符号找不到 → 红；
#   3) 每张登记表解析出的条目数必须等于声明的 rows → 不符即红。表被改名、条目被增删、
#      正则漂移都会先在计数上红，不会「零比对」静默判 clean；
#   4) 代码字面量指向的键在 balance.json 不存在 → 红（防登记表键名写错后永远取不到判据）；
#   5) 机型表出现未登记的键 → 红（新键不得悄悄绕开对账）；
#   6) 难度映射参数出现未登记的属性 → 红（同上）。
#
# 扫描口径（与 check_balance_keys.sh / check_prose_hygiene.sh 同款约定）：只读、纯 python3
# 标准库（不依赖 godot / dotnet / 第三方包）；解析只认字面量与显式登记的映射，不做「猜哪个
# 变量是默认值」的启发式——映射不清、或回退实参是符号而非字面量的读取点，一律写进 EXCLUDED
# 并给理由，使覆盖边界是明示的而不是悄悄残缺。
#
# 剥注释与条件编译防线：解析前用 shared 模块 scripts/ci/csharp_lex.py 剥掉注释（含块注释），
# 注释掉的档位/条目不再被解析成对账条目（否则「注释掉一个数值」仍算通过）；登记的文件里出现
# `#if/#elif/#else/#endif` 即红——条件编译段是否编译进产物不可静态判定，按文本对账在此失效。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

python3 - <<'PY'
import json
import math
import pathlib
import re
import sys

ROOT = pathlib.Path.cwd()
MISSING = object()

LEX_DIR = ROOT / "scripts" / "ci"
if not (LEX_DIR / "csharp_lex.py").exists():
    print("::error::scripts/ci/csharp_lex.py 不存在——共用词法剥离模块缺失，取不到判据，拒绝判 clean")
    sys.exit(1)
sys.path.insert(0, str(LEX_DIR))
import csharp_lex


class GateError(Exception):
    # 取不到判据（文件/符号/键/计数不符）——必须转成红，不得静默跳过
    pass


# 登记表：一行 = 一处 C# 缺档回退默认值的来源，及它应对账的 balance 段。
#   file / symbol —— 定位与失败输出用（找不到即红）
#   kind          —— 解析形态（每种形态一个解析器，只认字面量）
#   rows          —— 该处应解析出的标量比对条数（数组按键展开计数）；不符即红
#   desc          —— 输出用的可读名
TABLES = (
    {
        "kind": "difficulty_defs",
        "file": "csharp/godot/GameState.Constants.cs",
        "symbol": "BuildDifficultyDefs",
        "rows": 24,
        "desc": "难度档位表 → difficulty.{easy,medium,hard}.*",
    },
    {
        "kind": "talent_max_stacks",
        "file": "csharp/godot/TalentService.cs",
        "symbol": "MaxLevelFallbacks",
        "rows": 27,
        "desc": "天赋结构上限回退表 → augments.<id>.max_stacks",
    },
    {
        "kind": "milestone_base",
        "file": "csharp/godot/ScoreService.cs",
        "symbol": "BuildMilestoneBase",
        "rows": 8,
        "desc": "里程碑基础阈值 → milestones.base",
    },
    {
        "kind": "unlock_scores",
        "file": "csharp/godot/Spawner.cs",
        "symbol": "UNLOCK_SCORES",
        "rows": 10,
        "desc": "机型解锁分数（属性默认 + ApplyBalance 字面量回退，两处）→ spawner.unlock_scores",
    },
    {
        "kind": "boss_hp_mults",
        "file": "csharp/godot/Boss.cs",
        "symbol": "boss.hp_mults",
        "rows": 8,
        "desc": "Boss 型别 HP 倍率（Cfg 回退实参 + 校验失败回退，两处）→ boss.hp_mults",
    },
    {
        "kind": "boss_strafe_speeds",
        "file": "csharp/godot/Boss.cs",
        "symbol": "StrafeSpeeds",
        "rows": 4,
        "desc": "Boss 走位速度默认表 → boss.strafe_speeds",
    },
    {
        "kind": "boss_fire_intervals",
        "file": "csharp/godot/Boss.cs",
        "symbol": "FireIntervals",
        "rows": 4,
        "desc": "Boss 开火间隔默认表 → boss.fire_intervals",
    },
    {
        "kind": "boss_diff_scaling",
        "file": "csharp/godot/Boss.cs",
        "symbol": "DiffIntervalMult",
        "rows": 36,
        "desc": "Boss 难度分档（开火间隔/弹速/弹数增量）→ boss.difficulty_scaling.*",
    },
    {
        "kind": "scaling_config",
        "file": "csharp/core/Progression/DifficultyScaling.cs",
        "symbol": "DifficultyScalingConfig",
        "rows": 14,
        "desc": "难度映射参数默认值 → enemies.* / spawner.* / boss.hp_ramp_factor / progression.*",
    },
    {
        "kind": "enemy_types",
        "file": "csharp/godot/Spawner.cs",
        "symbol": "BuildEnemyTypes",
        "rows": 45,
        "desc": "普通机型表数值键 → enemies.types[i].*",
    },
    {
        "kind": "elite_types",
        "file": "csharp/godot/Spawner.cs",
        "symbol": "BuildEliteTypes",
        "rows": 36,
        "desc": "精英机型表数值键 → elites.types[i].*",
    },
    {
        "kind": "move_strategy_fields",
        "file": "csharp/godot/EnemyMoveStrategy.cs",
        "symbol": "_hoverBobAmp",
        "rows": 8,
        "desc": "移动策略共享参数字段默认值 → enemies.<key>",
    },
    {
        "kind": "strategy_field_defaults",
        "file": "csharp/godot/EnemyMoveStrategy.cs",
        "symbol": "SineMove",
        "rows": 22,
        "desc": "策略专属参数字段与三元素数组默认值 → enemies.move_strategies.*"
                "（5 个策略类的标量 12 项 + freqs/phases 2×3×2 项）",
    },
    {
        "kind": "augment_effects",
        "file": "csharp/godot/Player.cs",
        "symbol": "AugmentEffects",
        "rows": 8,
        "desc": "声明式增幅效果表的 default 字段 → 同行 cfg 指向的 balance 键",
    },
    {
        "kind": "state_progression_cfg",
        "file": "csharp/godot/GameState.State.cs",
        "symbol": "progression.soft_cap_start",
        "rows": 2,
        "desc": "时间项软上限 Cfg 回退实参 → progression.soft_cap_start / tail_speed_factor",
    },
)

# 刻意不覆盖：登记表之外的读取点，各给理由。列在这里是为了让覆盖边界是明示的——
# 新写法出现时先改这里（或加登记表一行），而不是让它悄悄绕过对账。
EXCLUDED = (
    ("ZigzagMove 的 _zigDir / _zigTimer",
     "折返方向符号与折返计时器状态字段，不是可调配置：_zigTimer 在 Reset() 里由 "
     "_resetFlipMin~_flipInterval 重抽，数值权威是 _flipInterval（已在登记表内）"),
    ("机型表的 texture / strategies / bullet_types / split / elite 键",
     "不在 balance.json（MergeType 只覆盖数值键；贴图/策略/弹种池按设计留在脚本），无定稿值可对账"),
    ("Boss 的截断回退（StrafeSpeeds 不足 3 元素时回退 {150,400,60}）",
     "这是有意的「不完整配置降级」部分表，长度与 json 全表不同，登记进对账会误报；"
     "FireIntervals 不足 3 元素时自取字段默认，与 StrafeSpeeds 同口径"),
    ("Enemy.cs 的 CfgFx.Float(\"enemies.hover_bob_amp\", HoverBobAmp, 0.0f) 等读取点",
     "回退实参是字段符号而非字面量，静态对账需跨字段解析；同组数值已由 EnemyMoveStrategy.cs "
     "的字段字面量覆盖（两处同值，EnemyMoveStrategy 是策略侧唯一权威默认）"),
    ("augments.* 里非表式的中间值",
     "分散在 Bullet/Boss/LaserWeapon/Main/GameState.State 等单点读取（有的内联字面量、有的字段符号），"
     "不构成整表手抄形态；本门禁只覆盖成表的 max_stacks 与 Player.AugmentEffects 的声明式 default"),
    ("enemies.hover_band",
     "两侧回退实参都是表达式（Spawner 用 _hoverBand.X/_hoverBand.Y、Enemy 用 HoverBand.X/HoverBand.Y），"
     "非字面量，取值由字段默认值参与计算"),
)

# 难度映射参数：属性名 → balance 点分路径（显式映射，不做「属性名转蛇形」的启发式猜测）。
PROP_KEYS = {
    "HpRampFactor": "enemies.hp_ramp_factor",
    "DamageRampFactor": "enemies.damage_ramp_factor",
    "SpeedRampFactor": "enemies.speed_ramp_factor",
    "SpeedRampCap": "enemies.speed_ramp_cap",
    "BossHpRampFactor": "boss.hp_ramp_factor",
    "SpawnDifficultyFactor": "spawner.difficulty_factor",
    "WaveIntervalFloor": "spawner.interval_min",
    "FireIntervalFloor": "enemies.fire_interval_floor",
    "ElitePerDifficulty": "spawner.elite_per_difficulty",
    "EliteCountCap": "spawner.elite_count_cap",
    "BossDensityPerDifficulty": "enemies.boss_density_per_difficulty",
    "BossDensityBonusCap": "enemies.boss_density_bonus_cap",
    "DifficultySoftCapStart": "progression.soft_cap_start",
    "DifficultyTailSpeedFactor": "progression.tail_speed_factor",
}

# 移动策略共享参数字段 → balance 键（显式映射：字段名与键名并非机械可转）。
MOVE_FIELD_KEYS = {
    "_hoverBobAmp": "enemies.hover_bob_amp",
    "_hoverBobFreq": "enemies.hover_bob_freq",
    "_hoverSwayAmp": "enemies.hover_sway_amp",
    "_hoverSwayFreq": "enemies.hover_sway_freq",
    "_spiralDriftAmp": "enemies.spiral_drift_amp",
    "_spiralDriftFreq": "enemies.spiral_drift_freq",
    "_spiralRadius": "enemies.spiral_radius",
    "_aggressiveChaseSpeed": "enemies.aggressive_chase_speed",
}

# 各移动策略的**策略专属参数字段** → balance 键（键相对 enemies.move_strategies.<类>）。
# 必须带类名限定：`_speedScale` 在四个策略类里同名而取值各不相同（0.9/1.7/1.2/1.1），
# 无类名则映射不成立。字段名与键名也非机械可转（_resetFlipMin → reset_flip_min）。
STRATEGY_FIELD_KEYS = {
    "SineMove": {
        "_amp": "amp",
        "_freq": "freq",
    },
    "ZigzagMove": {
        "_flipInterval": "flip_interval",
        "_speedScale": "speed_scale",
        "_resetFlipMin": "reset_flip_min",
    },
    "DiveMove": {
        "_speedScale": "speed_scale",
        "_duration": "duration",
    },
    "NoiseMove": {
        "_speedScale": "speed_scale",
    },
    "AggressiveMove": {
        "_speedScale": "speed_scale",
        "_hoverSpeedScale": "hover_speed_scale",
    },
}

# 策略类 → balance 里的子键名（类名与键名非机械可转，显式登记；新增策略须在此登记）。
STRATEGY_KEY_PREFIX = {
    "SineMove": "sine",
    "ZigzagMove": "zigzag",
    "DiveMove": "dive",
    "NoiseMove": "noise",
    "AggressiveMove": "aggressive",
}

# 策略类的数组字段（freqs/phases 三元素）：字段 → 键。
STRATEGY_ARRAY_KEYS = {
    "NoiseMove": {"_freqs": "freqs", "_phases": "phases"},
    "AggressiveMove": {"_freqs": "freqs", "_phases": "phases"},
}

# 策略类里**刻意不对账**的字段及理由（明示边界，不是漏判）。
STRATEGY_FIELD_EXCLUDED = {
    "ZigzagMove": {
        "_zigDir": "折返方向符号（初值 1.0 表示朝右，非可调数值）",
        "_zigTimer": "折返计时器状态字段，Reset() 里由 _resetFlipMin~_flipInterval 重抽；"
                     "其数值权威是 _flipInterval，初值仅为字段兜底",
    },
}

# 机型表按数值键对账（hp/speed 是二元素区间，按键展开）；其余键见 EXCLUDED。
TYPED_KEYS = ("hp", "speed", "score", "fire", "fire_interval", "scale", "radius")
TYPED_PAIR_KEYS = ("hp", "speed")
ENEMY_SKIP_KEYS = ("texture", "strategies", "bullet_types", "split")
ELITE_SKIP_KEYS = ("texture", "strategies", "bullet_types", "elite")

VEC2I = re.compile(r"new Vector2I\(\s*(-?\d+(?:\.\d+)?)f?\s*,\s*(-?\d+(?:\.\d+)?)f?\s*\)")
VEC2 = re.compile(r"new Vector2\(\s*(-?\d+(?:\.\d+)?)f?\s*,\s*(-?\d+(?:\.\d+)?)f?\s*\)")
SCALAR = re.compile(r"(-?\d+(?:\.\d+)?)[fFdD]?(?=\s*[,}\]])")
NUMBER_LIST = re.compile(r"-?\d+(?:\.\d+)?")


def lineno(text, index):
    return text.count("\n", 0, index) + 1


def fmt(value):
    if isinstance(value, float) and value == int(value):
        return str(int(value))
    return str(value)


def skip_literal(text, i):
    # 跳过 text[i] 起的 C# 字符串/字符字面量（含 @"..." 逐字串与转义），返回结束下标
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


def match_bracket(text, i):
    # text[i] 是 ( [ { 之一；返回配对下标（跳过字符串字面量），找不到返回 -1
    pairs = {"(": ")", "[": "]", "{": "}"}
    opener = text[i]
    closer = pairs[opener]
    depth = 0
    while i < len(text):
        c = text[i]
        if c == '"' or (c == "@" and i + 1 < len(text) and text[i + 1] == '"') or c == "'":
            i = skip_literal(text, i)
            continue
        if c == opener:
            depth += 1
        elif c == closer:
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return -1


def block_of(text, pattern):
    # pattern 必须以其左花括号结尾，返回该块的内部区间；取不到返回 None。
    # 锚点含左花括号是刻意的——只写方法名会先在调用点（Foo();）命中、抓错块，
    # 于是「解析不到判据」被误当成结构漂移。
    m = re.search(pattern, text)
    if not m:
        return None
    open_idx = m.end() - 1
    if text[open_idx] != "{":
        return None
    close_idx = match_bracket(text, open_idx)
    if close_idx < 0:
        return None
    return open_idx + 1, close_idx


def parse_number(token):
    return float(token.strip().replace("_", "").rstrip("fFdD"))


def parse_number_list(fragment, label):
    # 数值字面量整体解析（区间/数组都按出现顺序展开）；一个都没解析到即红——
    # 形态漂移时宁可失败，也不静默产生一张空表把「零比对」当成 clean
    values = [parse_number(t) for t in NUMBER_LIST.findall(fragment)]
    if not values:
        raise GateError(f"{label} 未解析到数值字面量（形态漂移？）")
    return values


def resolve(tree, path):
    node = tree
    for part in path.split("."):
        if isinstance(node, dict) and part in node:
            node = node[part]
        elif isinstance(node, list) and part.isdigit() and int(part) < len(node):
            node = node[int(part)]
        else:
            return MISSING
    return node


def same(code_value, json_value):
    # json 侧才代表定稿值；bool/None/容器不参与数值对账（登记表只登记标量）
    if isinstance(json_value, bool) or json_value is None:
        return False
    if isinstance(json_value, (int, float)):
        return math.isclose(code_value, float(json_value), rel_tol=0.0, abs_tol=1e-9)
    if isinstance(json_value, str):
        return False
    return False


def p_difficulty_defs(entry, text, problems):
    del entry, problems
    body = block_of(text, r"(?<![\w.])BuildDifficultyDefs\(\)\s*=>\s*new\(\)\s*\{")
    if body is None:
        raise GateError("BuildDifficultyDefs() 未找到（符号改名？）")
    start, end = body
    block = text[start:end]
    marks = list(re.finditer(r'\[new StringName\("(\w+)"\)\]\s*=\s*new Godot\.Collections\.Dictionary\s*\{', block))
    if not marks:
        raise GateError("未解析到任何难度分档（[new StringName(...)] 结构漂移？）")
    rows = []
    for m in marks:
        open_idx = block.index("{", m.start())
        close_idx = match_bracket(block, open_idx)
        if close_idx < 0:
            raise GateError(f"档 `{m.group(1)}` 的花括号不配对")
        inner = block[open_idx + 1:close_idx]
        base = start + open_idx + 1
        hit = False
        for km in re.finditer(r'\["(\w+)"\]\s*=\s*(-?\d+(?:\.\d+)?)', inner):
            hit = True
            rows.append({
                "path": f"difficulty.{m.group(1)}.{km.group(1)}",
                "value": parse_number(km.group(2)),
                "line": lineno(text, base + km.start()),
            })
        if not hit:
            raise GateError(f"档 `{m.group(1)}` 未解析到任何键值（结构漂移？）")
    return rows


def p_talent_max_stacks(entry, text, problems):
    del problems
    body = block_of(text, rf"(?<![\w.]){entry['symbol']}\s*=\s*new\(\)\s*\{{")
    if body is None:
        raise GateError(f"{entry['symbol']} 字典初始化未找到（符号改名？）")
    start, end = body
    block = text[start:end]
    rows = []
    for km in re.finditer(r'\["(\w+)"\]\s*=\s*(\d+)', block):
        rows.append({
            "path": f"augments.{km.group(1)}.max_stacks",
            "value": float(km.group(2)),
            "line": lineno(text, start + km.start()),
        })
    return rows


def p_milestone_base(entry, text, problems):
    del entry, problems
    body = block_of(text, r"(?<![\w.])BuildMilestoneBase\(\)\s*=>\s*new\(\)\s*\{")
    if body is None:
        raise GateError("BuildMilestoneBase() 未找到（符号改名？）")
    start, end = body
    values = parse_number_list(text[start:end], "milestones.base")
    return [
        {"path": f"milestones.base.{i}", "value": v, "line": lineno(text, start)}
        for i, v in enumerate(values)
    ]


def p_unlock_scores(entry, text, problems):
    del entry, problems
    sites = (
        ("属性默认值",
         r"UNLOCK_SCORES\s*\{\s*get;\s*set;\s*\}\s*=\s*new\(\)\s*\{([^}]*)\}"),
        ("ApplyBalance 字面量回退",
         r"UNLOCK_SCORES\s*=\s*usArr\.Count\s*>\s*0\s*\?\s*usArr\s*:\s*"
         r"new Godot\.Collections\.Array<int>\s*\{([^}]*)\}"),
    )
    rows = []
    for label, pattern in sites:
        m = re.search(pattern, text)
        if not m:
            raise GateError(f"`{label}` 未找到（字面量表被改写/删除？）")
        values = parse_number_list(m.group(1), f"spawner.unlock_scores（{label}）")
        for i, v in enumerate(values):
            rows.append({
                "path": f"spawner.unlock_scores.{i}",
                "value": v,
                "line": lineno(text, m.start(1)),
            })
    return rows


def p_boss_hp_mults(entry, text, problems):
    del entry, problems
    sites = (
        ("Cfg 回退实参",
         r'Cfg\(\s*"boss\.hp_mults"\s*,\s*new Godot\.Collections\.Array\s*\{([^}]*)\}'),
        ("校验失败回退",
         r"hpMultsValid\s*\?\s*hpMultsArr\s*:\s*new Godot\.Collections\.Array\s*\{([^}]*)\}"),
    )
    rows = []
    for label, pattern in sites:
        m = re.search(pattern, text)
        if not m:
            raise GateError(f"`{label}` 未找到（字面量表被改写/删除？）")
        values = parse_number_list(m.group(1), f"boss.hp_mults（{label}）")
        for i, v in enumerate(values):
            rows.append({
                "path": f"boss.hp_mults.{i}",
                "value": v,
                "line": lineno(text, m.start(1)),
            })
    return rows


def property_array_rows(text, prop, key_path, problems):
    del problems
    pattern = rf"public\s+(?:Godot\.Collections\.Array|float|double|int)[\w<>.]*\s+{prop}\s*\{{\s*get;\s*set;\s*\}}\s*=\s*new\(\)\s*\{{([^}}]*)\}}"
    m = re.search(pattern, text)
    if not m:
        raise GateError(f"{prop} 属性初始化未找到（符号改名？）")
    values = parse_number_list(m.group(1), key_path)
    return [
        {"path": f"{key_path}.{i}", "value": v, "line": lineno(text, m.start(1))}
        for i, v in enumerate(values)
    ]


def p_boss_strafe_speeds(entry, text, problems):
    del entry
    return property_array_rows(text, "StrafeSpeeds", "boss.strafe_speeds", problems)


def p_boss_fire_intervals(entry, text, problems):
    del entry
    return property_array_rows(text, "FireIntervals", "boss.fire_intervals", problems)


def p_boss_diff_scaling(entry, text, problems):
    del entry
    rows = property_array_rows(text, "DiffIntervalMult", "boss.difficulty_scaling.interval_mult", problems)
    rows += property_array_rows(text, "DiffSpeedMult", "boss.difficulty_scaling.speed_mult", problems)
    body = block_of(text, r"(?<![\w.])DiffCountDeltas\s*\{\s*get;\s*set;\s*\}\s*=\s*new\(\)\s*\{")
    if body is None:
        raise GateError("DiffCountDeltas 未找到（符号改名？）")
    start, end = body
    block = text[start:end]
    hits = list(re.finditer(r'\["(\w+)"\]\s*=\s*new Godot\.Collections\.Array\s*\{([^}]*)\}', block))
    if not hits:
        raise GateError("DiffCountDeltas 未解析到任何分档数组（结构漂移？）")
    for m in hits:
        values = parse_number_list(m.group(2), f"boss.difficulty_scaling.counts.{m.group(1)}")
        for i, v in enumerate(values):
            rows.append({
                "path": f"boss.difficulty_scaling.counts.{m.group(1)}.{i}",
                "value": v,
                "line": lineno(text, start + m.start()),
            })
    return rows


def p_scaling_config(entry, text, problems):
    del entry
    pattern = r"public\s+(?:double|int)\s+(\w+)\s*\{\s*get;\s*set;\s*\}\s*=\s*(-?\d+(?:\.\d+)?);"
    hits = list(re.finditer(pattern, text))
    if not hits:
        raise GateError("DifficultyScalingConfig 未解析到任何属性默认值（结构漂移？）")
    parsed = {}
    rows = []
    for m in hits:
        name = m.group(1)
        if name not in PROP_KEYS:
            raise GateError(f"属性 `{name}` 未登记 balance 键——新增默认值必须先加进登记表（防悄悄绕开对账）")
        parsed[name] = m.group(2)
        rows.append({
            "path": PROP_KEYS[name],
            "value": parse_number(m.group(2)),
            "line": lineno(text, m.start()),
        })
    idle = sorted(set(PROP_KEYS) - set(parsed))
    if idle:
        raise GateError(f"登记表里的属性 {', '.join(idle)} 在难度映射类里找不到（改名/删除？门禁需同步）")

    # 交叉校验一：BalanceService 把 json 键注入这些属性，键名/属性名必须与登记表一致
    service = ROOT / "csharp/godot/BalanceService.cs"
    if not service.exists():
        raise GateError("csharp/godot/BalanceService.cs 不存在——注入点消失，门禁需同步")
    service_text = service.read_text(encoding="utf-8")
    block = block_of(service_text, r"(?<![\w.])_scaling\s*=\s*new DifficultyScalingConfig\s*\{")
    if block is None:
        raise GateError("BalanceService 的 _scaling 初始化未找到（符号改名？）")
    start, end = block
    inject = list(re.finditer(
        r"(\w+)\s*=\s*(?:Mathf\.Max\(\s*(?:\(int\)\s*)?)?Cfg\(\s*\"([\w.]+)\"\s*,\s*_scaling\.(\w+)\s*\)",
        service_text[start:end]))
    if len(inject) != 12:
        raise GateError(f"BalanceService 注入点解析到 {len(inject)} 处，期望 12 处（登记表需同步）")
    for m in inject:
        prop, key, ref = m.group(1), m.group(2), m.group(3)
        if prop != ref or PROP_KEYS.get(prop) != key:
            problems.append(
                f"csharp/godot/BalanceService.cs:{lineno(service_text, start + m.start())}  "
                f"注入 `{prop} = Cfg(\"{key}\", _scaling.{ref})` 与登记表映射（{PROP_KEYS.get(prop)}）不一致"
                "——键名或属性名写错会让运行期静默用默认值")

    # 交叉校验二：难度映射参数里有 12 个由 BalanceService 注入 json，另 2 个（时间项软上限）
    # 走 RunProgressionService.ApplySoftCapParams，其 Cfg 回退实参在 GameState.State.cs。
    # 这条把「登记表里的键」与「真实注入点」对上，防登记表与实际读取点悄悄脱钩。
    injected_keys = {m.group(2) for m in inject}
    leftover = set(PROP_KEYS.values()) - injected_keys
    expected_leftover = {"progression.soft_cap_start", "progression.tail_speed_factor"}
    if leftover != expected_leftover:
        raise GateError(
            f"登记表里未被 BalanceService 注入的键是 {sorted(leftover)}，期望 {sorted(expected_leftover)}"
            "——映射表与实际注入点脱钩，门禁需同步")
    return rows


def parse_typed_value(tail):
    t = tail.lstrip()
    for pattern in (VEC2I, VEC2):
        m = pattern.match(t)
        if m:
            return [float(m.group(1)), float(m.group(2))]
    m = SCALAR.match(t)
    if m:
        return [float(m.group(1))]
    return None


def parse_typed_table(method, container, skip_keys, text):
    body = block_of(text, rf"(?<![\w.]){method}\(\)\s*\{{")
    if body is None:
        raise GateError(f"{method}() 未找到（符号改名？）")
    start, end = body
    block = text[start:end]
    rows = []
    index = -1
    for m in re.finditer(r"\bnew\(\)\s*\{", block):
        open_idx = block.index("{", m.start())
        close_idx = match_bracket(block, open_idx)
        index += 1
        if close_idx < 0:
            raise GateError(f"{container}.types[{index}] 的花括号不配对")
        inner = block[open_idx + 1:close_idx]
        base = start + open_idx + 1
        fields = {}
        for km in re.finditer(r'\["(\w+)"\]\s*=', inner):
            fields[km.group(1)] = (inner[km.end():], base + km.end())
        for key in sorted(fields):
            if key not in TYPED_KEYS and key not in skip_keys:
                raise GateError(
                    f"{container}.types[{index}] 出现未登记的键 `{key}`——新键不得悄悄绕开对账，"
                    "确认要纳入就加进登记表，否则写进 EXCLUDED")
        absent = [k for k in TYPED_KEYS if k not in fields]
        if absent:
            raise GateError(
                f"{container}.types[{index}] 缺键 {', '.join(absent)}——解析不到判据，拒绝判 clean")
        for key in TYPED_KEYS:
            tail, pos = fields[key]
            values = parse_typed_value(tail)
            if values is None:
                raise GateError(f"{container}.types[{index}].{key} 的值不是可解析的数值字面量（形态漂移？）")
            if key in TYPED_PAIR_KEYS and len(values) != 2:
                raise GateError(f"{container}.types[{index}].{key} 不是二元素区间（形态漂移？）")
            for i, v in enumerate(values):
                suffix = f".{i}" if key in TYPED_PAIR_KEYS else ""
                rows.append({
                    "path": f"{container}.types.{index}.{key}{suffix}",
                    "value": v,
                    "line": lineno(text, pos),
                })
    if index < 0:
        raise GateError(f"{method}() 未解析到任何机型条目（结构漂移？）")
    return rows


def p_enemy_types(entry, text, problems):
    del entry, problems
    return parse_typed_table("BuildEnemyTypes", "enemies", ENEMY_SKIP_KEYS, text)


def p_elite_types(entry, text, problems):
    del entry, problems
    return parse_typed_table("BuildEliteTypes", "elites", ELITE_SKIP_KEYS, text)


def p_move_strategy_fields(entry, text, problems):
    del entry, problems
    declarations = list(re.finditer(r"protected\s+float\s+(\w+)\s*=\s*(-?\d+(?:\.\d+)?)[fFdD]?\s*;", text))
    names = {m.group(1) for m in declarations}
    if names != set(MOVE_FIELD_KEYS):
        extra = sorted(names - set(MOVE_FIELD_KEYS))
        absent = sorted(set(MOVE_FIELD_KEYS) - names)
        raise GateError(
            f"共享参数字段集与登记表不符（未登记 {extra} / 找不到 {absent}）——"
            "新增字段必须登记，否则它的缺档默认值无人对账")
    rows = []
    for m in declarations:
        rows.append({
            "path": MOVE_FIELD_KEYS[m.group(1)],
            "value": parse_number(m.group(2)),
            "line": lineno(text, m.start()),
        })
    return rows


def p_strategy_field_defaults(entry, text, problems):
    """策略专属参数：按类块解析字段初值与三元素数组，逐条对账。

    类块用花括号配对切出，避免同名 `_speedScale` 跨类串味（四个策略类里取值各异）。
    字段集必须与登记表**完全相等**：新字段不登记即红——否则新参数的手抄默认值无人对账。
    """
    del entry, problems
    if set(STRATEGY_FIELD_KEYS) | set(STRATEGY_ARRAY_KEYS) | set(STRATEGY_FIELD_EXCLUDED) != set(STRATEGY_KEY_PREFIX):
        raise GateError(
            "策略类登记面与 STRATEGY_KEY_PREFIX 不一致（有类未登记子键名）——取不到判据")
    rows = []
    for cls, mapping in STRATEGY_FIELD_KEYS.items():
        prefix = f"enemies.move_strategies.{STRATEGY_KEY_PREFIX[cls]}"
        declared = re.search(rf"public\s+sealed\s+class\s+{cls}\s*:\s*EnemyMoveStrategy", text)
        if declared is None:
            raise GateError(f"策略类 {cls} 未找到（改名/删除？）——取不到判据")
        brace = text.index("{", declared.end())
        close = match_bracket(text, brace)
        if close < 0:
            raise GateError(f"策略类 {cls} 的花括号不配对——取不到判据")
        body = text[brace:close]

        scalars = {}
        for m in re.finditer(r"private\s+float\s+(\w+)\s*=\s*(-?\d+(?:\.\d+)?)[fFdD]?\s*;", body):
            scalars[m.group(1)] = (parse_number(m.group(2)), brace + m.start())
        arrays = {}
        for m in re.finditer(r"private\s+readonly\s+float\[\]\s+(\w+)\s*=\s*\{([^}]*)\}", body):
            arrays[m.group(1)] = (
                [parse_number(x) for x in NUMBER_LIST.findall(m.group(2))],
                brace + m.start(),
            )

        excluded = STRATEGY_FIELD_EXCLUDED.get(cls, {})
        expected_scalar = set(mapping) | set(excluded)
        if set(scalars) != expected_scalar:
            extra = sorted(set(scalars) - expected_scalar)
            absent = sorted(expected_scalar - set(scalars))
            raise GateError(
                f"{cls} 的标量字段集与登记表不符（未登记 {extra} / 找不到 {absent}）——"
                "新增字段必须登记（或写进 STRATEGY_FIELD_EXCLUDED 并给理由）")
        expected_array = set(STRATEGY_ARRAY_KEYS.get(cls, {}))
        if set(arrays) != expected_array:
            extra = sorted(set(arrays) - expected_array)
            absent = sorted(expected_array - set(arrays))
            raise GateError(
                f"{cls} 的数组字段集与登记表不符（未登记 {extra} / 找不到 {absent}）——同上")

        for field, key in mapping.items():
            value, idx = scalars[field]
            rows.append({"path": f"{prefix}.{key}", "value": value,
                         "line": lineno(text, idx)})
        for field, key in STRATEGY_ARRAY_KEYS.get(cls, {}).items():
            values, idx = arrays[field]
            if len(values) != 3:
                raise GateError(
                    f"{cls}.{field} 解析出 {len(values)} 个元素，期望 3——"
                    "数组长度漂移，取不到逐位判据")
            for i, v in enumerate(values):
                rows.append({"path": f"{prefix}.{key}.{i}", "value": v,
                             "line": lineno(text, idx)})
    return rows


def p_augment_effects(entry, text, problems):
    del entry, problems
    body = block_of(text, r"(?<![\w.])AugmentEffects\s*=\s*new\(\)\s*\{")
    if body is None:
        raise GateError("AugmentEffects 未找到（符号改名？）")
    start, end = body
    block = text[start:end]
    entries = list(re.finditer(r"new Godot\.Collections\.Dictionary\s*\{", block))
    rows = []
    for m in entries:
        open_idx = block.index("{", m.start())
        close_idx = match_bracket(block, open_idx)
        if close_idx < 0:
            raise GateError("AugmentEffects 条目的花括号不配对")
        inner = block[open_idx + 1:close_idx]
        cfg = re.search(r'\["cfg"\]\s*=\s*"([\w.]+)"', inner)
        default = re.search(r'\["default"\]\s*=\s*(-?\d+(?:\.\d+)?)', inner)
        if cfg and not default:
            raise GateError(f"增幅效果条目 `{cfg.group(1)}` 有 cfg 无 default——形态漂移，拒绝放绿")
        if default and not cfg:
            raise GateError("增幅效果条目有 default 无 cfg——取不到对应键，拒绝放绿")
        if not cfg:
            continue    # bool 类条目（如 explosive）只有 kind，没有可对账的默认值
        rows.append({
            "path": cfg.group(1),
            "value": parse_number(default.group(1)),
            "line": lineno(text, start + open_idx + 1 + cfg.start()),
        })
    if len(entries) != 9:
        raise GateError(f"AugmentEffects 解析到 {len(entries)} 个条目，期望 9 个（登记表需同步）")
    return rows


def p_state_progression_cfg(entry, text, problems):
    del entry, problems
    hits = list(re.finditer(
        r'Cfg\(\s*"(progression\.(?:soft_cap_start|tail_speed_factor))"\s*,\s*(-?\d+(?:\.\d+)?)\)',
        text))
    if len(hits) != 2:
        raise GateError(f"软上限 Cfg 实参解析到 {len(hits)} 处，期望 2 处（字面量被改写/删除？）")
    return [
        {"path": m.group(1), "value": parse_number(m.group(2)), "line": lineno(text, m.start())}
        for m in hits
    ]


PARSERS = {
    "difficulty_defs": p_difficulty_defs,
    "talent_max_stacks": p_talent_max_stacks,
    "milestone_base": p_milestone_base,
    "unlock_scores": p_unlock_scores,
    "boss_hp_mults": p_boss_hp_mults,
    "boss_strafe_speeds": p_boss_strafe_speeds,
    "boss_fire_intervals": p_boss_fire_intervals,
    "boss_diff_scaling": p_boss_diff_scaling,
    "scaling_config": p_scaling_config,
    "enemy_types": p_enemy_types,
    "elite_types": p_elite_types,
    "move_strategy_fields": p_move_strategy_fields,
    "strategy_field_defaults": p_strategy_field_defaults,
    "augment_effects": p_augment_effects,
    "state_progression_cfg": p_state_progression_cfg,
}


def main():
    balance_path = ROOT / "data" / "balance.json"
    if not balance_path.exists():
        print("::error::data/balance.json 不存在——数值单源缺失，取不到判据，拒绝判 clean")
        return 1
    try:
        balance = json.loads(balance_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        print(f"::error::data/balance.json 解析失败（{exc}）——取不到判据，拒绝判 clean")
        return 1

    problems = []
    judged = 0
    for entry in TABLES:
        if entry["kind"] not in PARSERS:
            problems.append(f"登记表项 {entry['kind']} 没有解析器——门禁需同步")
            continue
        path = ROOT / entry["file"]
        if not path.exists():
            problems.append(
                f"{entry['file']} 不存在（{entry['desc']}）——登记表指向的文件被改名/删除，门禁需同步")
            continue
        text, in_string = csharp_lex.strip_comments(path.read_text(encoding="utf-8"))
        hits = csharp_lex.directives(text, in_string)
        if hits:
            where = "、".join(f"第 {n} 行 `{d}`" for n, d in hits[:5])
            problems.append(
                f"{entry['file']} 含条件编译指令（{where}）——登记文件按文本对账，"
                "条件编译段是否编译进产物不可静态判定（`#if false` 包住的档位/条目在文本里照旧命中），"
                "该表取不到判据；须去掉条件编译或把该段拆出单独文件后重新登记")
            continue
        try:
            rows = PARSERS[entry["kind"]](entry, text, problems)
        except GateError as exc:
            problems.append(f"{entry['file']}:{entry['symbol']}  {exc}")
            continue
        if len(rows) != entry["rows"]:
            problems.append(
                f"{entry['file']}:{entry['symbol']}  解析出 {len(rows)} 个缺档默认值，登记表期望 "
                f"{entry['rows']} 个（{entry['desc']}）——表被改名/条目增删/正则漂移；"
                "条目数不符即红，防「零比对」静默判 clean")
            continue
        for row in rows:
            json_value = resolve(balance, row["path"])
            if json_value is MISSING:
                problems.append(
                    f"{entry['file']}:{row['line']}  键 `{row['path']}` 在 balance.json 不存在"
                    "——登记表键名写错或 json 键被删/改名，取不到判据")
            elif not same(row["value"], json_value):
                problems.append(
                    f"{entry['file']}:{row['line']}  `{row['path']}`：代码默认 {fmt(row['value'])} "
                    f"!= balance 定稿值 {fmt(json_value)}（改一侧而非两侧就是静默分叉）")
            judged += 1

    # 默认机型表的读取面收口（教程单源的回归面）。
    # BuildEnemyTypes/BuildEliteTypes 返回**未经 balance 覆盖的原始默认表**，只有 Spawner 自己的
    # merge 流程（ApplyBalance / BuildMergedEnemyTypes）该读它；外部直读即「绕过单源」，表现是
    # json 调值后该路径静默不跟（教程曾直读默认表，正局跟随、教程不跟随）。
    # 这条能失败的判据是关键：只看取值，绕过与不绕过在今天完全相同（默认表 == json 定稿值），
    # 唯把读取面收成「只许 Spawner 内部」才能在有人改回去时立刻红。
    for symbol in ("BuildEnemyTypes", "BuildEliteTypes"):
        readers = []
        for p in ROOT.joinpath("csharp").rglob("*.cs"):
            if any(part in {"obj", "bin", "__pycache__"} for part in p.parts):
                continue
            rel = p.relative_to(ROOT).as_posix()
            for i, line in enumerate(p.read_text(encoding="utf-8").splitlines(), 1):
                if symbol in line.split("//", 1)[0]:
                    readers.append((rel, i))
        if not readers:
            problems.append(
                f"csharp/ 内找不到 `{symbol}` 的任何引用——符号被改名/删除，"
                "默认机型表读取面判据失效，门禁需同步")
            continue
        for rel, i in readers:
            if rel == "csharp/godot/Spawner.cs":
                continue
            problems.append(
                f"{rel}:{i}  直读 `{symbol}`——该表未经 balance 覆盖，属绕过单源"
                "（应经 Spawner.BuildMergedEnemyTypes 取合并后的配置）")
            judged += 1

    if problems:
        for p in problems[:60]:
            print("::error::" + p)
        print(f"code-defaults gate: FAILED（{len(problems)} 项 / 已比对 {judged} 个缺档默认值）")
        return 1

    if judged == 0:
        print("::error::一个缺档默认值都没比对到——登记表或解析器整体失效，拒绝判 clean")
        return 1
    print(
        f"code-defaults gate: clean（{len(TABLES)} 张登记表 / {judged} 个缺档默认值全部等于 "
        f"balance.json 定稿值；另有 {len(EXCLUDED)} 处刻意不覆盖，理由见脚本内 EXCLUDED 表）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
PY
