#!/usr/bin/env bash
# balance 反向死键门禁：data/balance.json 的每个叶子键都必须有读取点（正向判定见 check_balance_keys.sh）。
# 用法：bash scripts/ci/check_balance_dead_keys.sh
#
# 抓的静默错误：json 里写了、代码里没人读的键——两种成因看起来一样，都只在玩家/调参者「改了值却没反应」
# 时暴露：(1) 历史遗留的死配置（机制退役后键留下），(2) 键名写错（代码读的那条路径与 json 里的不一致，
# Cfg 静默回退代码默认值）。正向门禁只能判「代码读的键存在」，判不出反向；本门禁补这一半。
#
# 判定口径：把 balance.json 展开成全部点分路径，**只判叶子键**（字典节点本身不是配置项；整表读取的
# 容器由 WHOLE_DICT 声明）。每个叶子键必须至少有一类读取证据：
#   1) 字面证据：该完整点分路径以 `"a.b.c"` 形态出现在 csharp/ 任意 .cs（含 tests；先剥注释——注释里
#      提到键不算读取，注释掉的读取行也不算）里；
#   2) 动态键族：落在 DYNAMIC_FAMILIES 声明的键集里。键族不是「前缀匹配」而是**由取值源生成的具体
#      键集**——中段取自单源（天赋树节点 id / 瞄准档位表 / Boss 类型序号 / SupplyCfg 调用点字面后缀），
#      所以族内写错的键（如 augments.<拼错的节点>.max_stacks）仍然判红，不会被族整块放行；
#   3) 整表读取：落在 WHOLE_DICT 声明的容器子树里（容器路径被 Cfg 整取，子键由代码迭代/查表消费）。
# 未覆盖的叶子键 → 红（输出键路径与「按字面/按族都判不到读取点」）。
#
# 双向与守卫（AGENTS §6 铁律 2：取不到判据必须显式失败）：
#   - 每个键族的取值源必须解析出非空值集，且至少命中一个真实键（族空转 = 判据取不到，红）；
#     键族表为空、WHOLE_DICT 为空、字面证据为零、叶子键总数低于 MIN_LEAVES → 全红（防扫描漂移假绿）；
#   - WHOLE_DICT 的每条路径必须在 balance.json 里真实存在且是字典（路径改名即红）。
#
# 边界：字面证据只要求「键以完整点分路径字面量出现过」——不校验它是否出现在 balance 访问器的实参上
# （正向门禁已管住访问器口径）。若将来出现大量把键名放进变量/配置表的新写法，本门禁看不到那些读取点，
# 会表现为假红而非假绿（假红可修，假绿不可见）。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

python3 - <<'PY'
import json
import pathlib
import re
import sys

ROOT = pathlib.Path.cwd()
SKIP = {".git", ".godot", "builds", "tools", "bin", "obj", "__pycache__", ".venv"}

# 叶子键总数下限（当前实测 738）。扫描面漂移（json 被截断、flatten 写坏、路径改名）时判据会缩水，
# 那种「取不到判据」必须显式失败，不能判 clean。
MIN_LEAVES = 600

# 动态键族：(键模板, 读取点, 理由)。模板里 `{取值源}` 是唯一的变量段（由 VALUE_SOURCES 展开成具体
# 值集后拼成正则），末尾 `.*` 表示该前缀之下的整棵子树都由这一处读取消费。模板写成具体键形状而不是
# 「前缀通配」：族内写错的键（如 augments.<拼错的节点>.max_stacks）匹配不上，仍然判红。
DYNAMIC_FAMILIES = (
    ("augments.{talent_nodes}.max_stacks", "TalentService.LoadNodeCaps：\"augments.\" + id + \".max_stacks\"",
     "节点数值上限：id 来自天赋树结构单源（TalentTree.Lines[].NodeIds），代码拼键读取"),
    ("augments.{talent_nodes}.factor", "TalentFanView/TalentPanel：$\"augments.{nodeId}.factor\"",
     "节点增益系数：id 同源天赋树结构，内插拼键读取（部分节点无该键，走调用点默认值）"),
    ("talent.softcaps.{talent_nodes}", "TalentService.LoadNodeCaps：\"talent.softcaps.\" + id",
     "节点软上限：id 同源天赋树结构，代码拼键读取"),
    ("player.aim_assist.levels.{aim_levels}.{aim_params}",
     "AimFrameLayer.LoadLevelParams / Player.LoadAimAssistParams：basePath + \"<参数>\"",
     "瞄准辅助档位参数：档位取自 SettingsService.AIM_ASSIST_ORDER，参数取自读取点的字面后缀"),
    ("boss.phases.type{boss_types}.*", "Boss.LoadPhasePatterns：Cfg(\"boss.phases.type\" + BossType, defaults)",
     "Boss 阶段攻击模式表：类型序号由 Boss 轮换决定，整表读取（p1/p2 数组由代码消费）"),
    ("base.supply.{supply_keys}", "BaseConsole.SupplyCfg：Cfg(\"base.supply.\" + key)",
     "基地补给档：后缀取自 SupplyCfg 调用点的字面实参"),
)

# 整表读取：(容器路径, 读取点, 理由)。容器被 Cfg 整取后，其下子键由代码按键/按序消费——子键不是
# 独立读取面，逐条登记以免「整表读取」被当成死配置。
WHOLE_DICT = (
    ("difficulty", "GameState.State：Cfg(\"difficulty\", …) 整取后按难度档名索引",
     "难度档数值表：键 = 难度档名，字段由 DifficultyScaling 读取"),
    ("boss.difficulty_scaling.counts", "Boss：Cfg(\"boss.difficulty_scaling.counts\", …) 整取后按攻击种类索引",
     "随难度浮动的攻击枚数增量表：键 = 攻击种类名"),
    ("enemies.move_strategies", "BalanceService：Cfg(\"enemies.move_strategies\", …) 整取",
     "敌机移动策略参数表：键 = 策略名"),
    ("elite_turret_event.ammo_sequences", "EliteTurretEvent：Cfg(\"elite_turret_event.ammo_sequences\", …) 整取后按难度档名索引",
     "精英炮塔弹序表：键 = 难度档名"),
    ("elite_turret_event.turret_counts", "EliteTurretEvent：Cfg(\"elite_turret_event.turret_counts\", …) 整取后按难度档名索引",
     "精英炮塔数量表：键 = 难度档名"),
    ("elite_turret_event.weak_lock", "EliteTurretEvent：Cfg(\"elite_turret_event.weak_lock\", …) 整取",
     "弱锁定参数表：字段名固定（homing_time/turn_rate/cone_deg/…），作为一组读取"),
    ("fog_events.weights", "GameEventManager：Cfg(\"fog_events.weights\", …) 整取后按事件 id 索引",
     "迷雾事件权重表：键 = 事件 id"),
    ("fog_events.durations", "GameEventManager：Cfg(\"fog_events.durations\", …) 整取后按事件 id 索引",
     "迷雾事件时长表：键 = 事件 id"),
    ("formation_strike_event.craft_counts", "FormationStrikeEvent：Cfg(\"formation_strike_event.craft_counts\", …) 整取后按难度档名索引",
     "编队机数量表：键 = 难度档名"),
)

TALENT_TREE = "csharp/core/Talent/TalentTree.cs"
SETTINGS_SERVICE = "csharp/godot/SettingsService.cs"
AIM_READERS = ("csharp/godot/AimFrameLayer.cs", "csharp/godot/Player.cs")
BASE_CONSOLE = "csharp/godot/BaseConsole.cs"
BOSS_TYPE_COUNT = 4      # Boss 轮换类型数（boss.phases.type1..type4；单源在 core BossRotation）


def flatten(node, prefix=""):
    """展开 balance.json：路径 -> 值（字典节点也进表，供整表读取判定用）。"""
    out = {}
    if isinstance(node, dict):
        for key, value in node.items():
            path = f"{prefix}{key}"
            out[path] = value
            out.update(flatten(value, path + "."))
    return out


LEX_DIR = ROOT / "scripts" / "ci"
if not (LEX_DIR / "csharp_lex.py").exists():
    print("::error::scripts/ci/csharp_lex.py 不存在——共用词法剥离模块缺失，取不到判据，拒绝判 clean")
    sys.exit(1)
sys.path.insert(0, str(LEX_DIR))
import csharp_lex

errors: list[str] = []


def code_text(rel):
    """目标文件的剥注释文本；文件缺失即取不到判据（返回 None 由调用方判红）。"""
    path = ROOT / rel
    if not path.exists():
        errors.append(f"{rel} 不存在——取值源文件缺失，键族判据取不到，拒绝判 clean")
        return None
    return csharp_lex.strip_comments(path.read_text(encoding="utf-8", errors="replace"))[0]


def value_source(name):
    """解析中段取值集（单源在代码里）；解析不出非空集即判红（不静默空转）。"""
    if name == "talent_nodes":
        text = code_text(TALENT_TREE)
        if text is None:
            return []
        ids = []
        for block in re.findall(r"NodeIds\s*=\s*new\[\]\s*\{([^}]*)\}", text):
            ids += re.findall(r'"([a-z0-9_]+)"', block)
        if not ids:
            errors.append(f"{TALENT_TREE} 未解析出任何 NodeIds——天赋树节点取值源取不到，拒绝判 clean")
        return sorted(set(ids))
    if name == "aim_levels":
        text = code_text(SETTINGS_SERVICE)
        if text is None:
            return []
        block = re.search(r"AIM_ASSIST_ORDER\s*\{[^}]*\}([\s\S]{0,400}?);", text)
        levels = re.findall(r'new StringName\("([a-z0-9_]+)"\)', block.group(1)) if block else []
        if not levels:
            errors.append(f"{SETTINGS_SERVICE} 未解析出 AIM_ASSIST_ORDER 档位——取值源取不到，拒绝判 clean")
        return levels
    if name == "aim_params":
        params = []
        for rel in AIM_READERS:
            text = code_text(rel)
            if text is None:
                continue
            params += re.findall(r"basePath\s*\+\s*\"([a-z0-9_]+)\"", text)
        if not params:
            errors.append("瞄准档位参数的读取点未解析出任何 `basePath + \"<参数>\"` 字面后缀——"
                          "取值源取不到，拒绝判 clean")
        return sorted(set(params))
    if name == "supply_keys":
        text = code_text(BASE_CONSOLE)
        if text is None:
            return []
        keys = re.findall(r"SupplyCfg\(\s*\"([a-z0-9_]+)\"", text)
        if not keys:
            errors.append(f"{BASE_CONSOLE} 未解析出任何 SupplyCfg 字面后缀——取值源取不到，拒绝判 clean")
        return sorted(set(keys))
    if name == "boss_types":
        return [str(i) for i in range(1, BOSS_TYPE_COUNT + 1)]
    errors.append(f"未知的取值源名 `{name}`——键族登记表结构漂移，拒绝判 clean")
    return []


balance_path = ROOT / "data" / "balance.json"
if not balance_path.exists():
    print("::error::data/balance.json 不存在——取不到判据，拒绝判 clean")
    sys.exit(1)
balance = json.loads(balance_path.read_text(encoding="utf-8"))
paths = flatten(balance)
leaves = sorted(path for path, value in paths.items() if not isinstance(value, dict))
if len(leaves) < MIN_LEAVES:
    print(f"::error::balance.json 只展开出 {len(leaves)} 个叶子键（下限 {MIN_LEAVES}）——"
          "json 被截断或 flatten 漂移；取不到判据，拒绝判 clean")
    sys.exit(1)

corpus = []
for path in sorted(ROOT.rglob("*.cs")):
    if any(part in SKIP for part in path.parts):
        continue
    corpus.append(csharp_lex.strip_comments(path.read_text(encoding="utf-8", errors="replace"))[0])
if not corpus:
    print("::error::csharp/ 下未收集到任何 .cs——路径漂移？取不到判据，拒绝判 clean")
    sys.exit(1)
code = "\n".join(corpus)
literal_hits = {leaf for leaf in leaves if f'"{leaf}"' in code}
if not literal_hits:
    print("::error::没有任何叶子键以字面量形态出现在 csharp/——取值口径或扫描范围漂移，拒绝判 clean")
    sys.exit(1)

if not DYNAMIC_FAMILIES:
    print("::error::动态键族登记表为空——反向判据只剩字面证据，拒绝判 clean")
    sys.exit(1)
if not WHOLE_DICT:
    print("::error::整表读取登记表为空——反向判据不完整，拒绝判 clean")
    sys.exit(1)

family_covered = set()
PLACEHOLDER = re.compile(r"\{([a-z_]+)\}")
for template, reader, reason in DYNAMIC_FAMILIES:
    if not reason.strip() or not reader.strip():
        errors.append(f"键族 `{template}` 缺读取点或理由——登记必须写清为什么")
        continue
    sources = PLACEHOLDER.findall(template)
    if not sources and not template.endswith(".*"):
        errors.append(f"键族 `{template}` 既无取值源占位也无 `.*`——模板空转，判据取不到")
        continue
    resolved = {}
    failed = False
    for source in sources:
        values = value_source(source)
        if not values:
            failed = True                                # 取值源缺失已判红
        resolved[source] = values
    if failed:
        continue
    # 模板 → 具体键正则：占位替换成 (值1|值2|…)，末尾 `.*` 变成该前缀之下的子树匹配
    pattern = ""
    for chunk in re.split(r"(\{[a-z_]+\})", template):
        if PLACEHOLDER.fullmatch(chunk):
            values = resolved[chunk[1:-1]]
            pattern += "(" + "|".join(re.escape(v) for v in values) + ")"
        elif chunk.endswith(".*"):
            pattern += re.escape(chunk[:-2]) + r"(\..*)?$"
        else:
            pattern += re.escape(chunk)
    matcher = re.compile("^" + pattern)
    expected = {key for key in paths if matcher.match(key)}
    if not expected:
        errors.append(f"键族 `{template}` 生成不出任何键（取值源 {resolved}）——登记项空转，判据取不到")
        continue
    if not (expected & set(paths)):
        errors.append(f"键族 `{template}` 在 balance.json 里一个键都匹配不到——取值源或模板已过时，门禁需同步")
        continue
    family_covered |= expected

whole_covered = set()
for container, reader, reason in WHOLE_DICT:
    if container not in paths:
        errors.append(f"整表读取登记的容器 `{container}` 在 balance.json 里不存在——路径改名即失效，门禁需同步")
        continue
    if not isinstance(paths[container], dict):
        errors.append(f"整表读取登记的 `{container}` 不是字典（叶子键不能被整表读取）——门禁需同步")
        continue
    if not reason.strip():
        errors.append(f"整表读取登记 `{container}` 缺理由——登记必须写清为什么")
        continue
    whole_covered |= {key for key in paths if key.startswith(container + ".")}

dead = [leaf for leaf in leaves if leaf not in literal_hits
        and leaf not in family_covered and leaf not in whole_covered]

if errors:
    for e in errors[:40]:
        print("::error::" + e)
    print(f"balance-dead-keys gate: FAILED（{len(errors)} 项登记/守卫错误）")
    sys.exit(1)
if dead:
    for key in dead[:60]:
        print(f"::error::balance 死键 `{key}`（值 {json.dumps(paths[key], ensure_ascii=False)[:40]}）："
              "按字面与按族都判不到读取点——键名写错（代码读的路径与 json 不一致，Cfg 静默回退默认值）"
              "或配置已退役；确认无人读后删除该键，确在动态读取则登记进 DYNAMIC_FAMILIES/WHOLE_DICT")
    print(f"balance-dead-keys gate: FAILED（{len(dead)} 个叶子键无读取点）")
    sys.exit(1)
print(
    f"balance-dead-keys gate: clean（{len(leaves)} 个叶子键全部有读取证据：字面 {len(literal_hits)}、"
    f"键族 {len(family_covered & set(leaves))}（{len(DYNAMIC_FAMILIES)} 族）、"
    f"整表 {len(whole_covered & set(leaves))}（{len(WHOLE_DICT)} 个容器））"
)
PY
