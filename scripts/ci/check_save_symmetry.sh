#!/usr/bin/env bash
# 存档字段对称性门禁：CollectRunDict（写）与 ApplyRunDict（读）必须一一对应，
# 且持久化键集对「冻结基线」保持不变（增删改名都要显式决策）。
# 用法：bash scripts/ci/check_save_symmetry.sh
# 为什么需要：单存档位检查点模型下，
#   写而未读 → 字段白写，玩家以为存了其实读不回来；
#   读而未写 → 每次读档都落到默认值，进度静默丢失。两者编译与冒烟都发现不了。
# 对称性判不了「键改名」：写读同时把 boss_kills 改成 boss_kill_total，两边集合仍相等，门禁判
# 「一一对应」，而旧档该字段读不回、静默归零（违反「旧档可读入不丢进度」不变式）。因此增设
# FROZEN_KEYS + FROZEN_VERSION 冻结基线：
#   - 实际键集与冻结集不一致 → 红：改名 / 增删字段必须是个决策——bump RunSaveVersion（版本不符
#     时 LoadRun 直接返回 false，旧档即被拒），或补旧档迁移分支；确认后同步 FROZEN_KEYS；
#   - 键集一致但 RunSaveVersion 与 FROZEN_VERSION 不同 → 红：bump 版本会让旧档读不回，
#     同样要求显式确认（同步 FROZEN_VERSION 即视为确认，理由写进提交正文）。
# FROZEN_KEYS 来源：本门禁上线时按当前 CollectRunDict 实况生成一次，之后即为冻结基线。
#
# 取键前先按 C# 词法剥注释（共用模块 scripts/ci/csharp_lex.py）：注释掉的写入/读取行不能再参与
# 对账——「把旧读取行留在注释里、实现改成读常量」这类改法会让键集看上去没变，而运行期每次读档
# 都落默认值，正是本门禁存在的理由的反面。条件编译段无法静态对账（`#if false` 包住的写入在文本
# 里照旧命中），捕获块内出现指令即红，不静默少几行。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

python3 - <<'PY'
import pathlib, re, sys

LEX_DIR = pathlib.Path("scripts/ci")
if not (LEX_DIR / "csharp_lex.py").exists():
    print("::error::scripts/ci/csharp_lex.py 不存在——共用词法剥离模块缺失，取不到判据，拒绝判 clean")
    sys.exit(1)
sys.path.insert(0, str(LEX_DIR))
import csharp_lex

path = pathlib.Path("csharp/godot/GameState.RunSave.cs")
src, in_string = csharp_lex.strip_comments(path.read_text(encoding="utf-8"))

# 冻结基线（上线时按 CollectRunDict 实况生成一次）：改这行 = 承认改了存档键集，
# 必须在同一提交里说明是 bump RunSaveVersion 还是补了迁移分支。
FROZEN_KEYS = {
    "augments", "boss_kills", "combo", "dda_timer", "difficulty_multiplier", "difficulty_time_step",
    "health", "kills", "last_kind_value", "milestone_count", "missions", "refresh_points", "rp",
    "run_time", "score", "talent_bonus_overcharge_slots", "talent_cache_values", "talent_levels",
    "talent_overcharged", "talent_reset_tokens", "talent_route",
}
FROZEN_VERSION = 1

collect = re.search(r"return new Godot\.Collections\.Dictionary\s*\{(.*?)\n        \};", src, re.S)
apply = re.search(r"private void ApplyRunDict\(Godot\.Collections\.Dictionary d\)(.*?)\n    \}", src, re.S)
version = re.search(r"private const int RunSaveVersion = (\d+);", src)
if not collect or not apply or not version:
    print("::error::无法定位 CollectRunDict/ApplyRunDict/RunSaveVersion（结构变了？门禁需同步）")
    sys.exit(1)

# 条件编译防线：捕获块（写入块 / 读取块 / 版本常量）内出现 #if/#elif/#else/#endif 即红。
# 「按文本对账」的前提是这段文本都编译进产物；`#if false` 包住一行写入时文本照旧命中，
# 门禁判 clean 而运行期该字段不再落盘——须把该段拆出或登记，不得静默按文本判。
conditional = set(csharp_lex.conditional_lines(src, in_string))


def cond_hits(match):
    lo = src.count("\n", 0, match.start()) + 1
    hi = src.count("\n", 0, match.end()) + 1
    return [n for n in sorted(conditional) if lo <= n <= hi]


# 键口径与设置对称门禁同款：AGENTS §9 规定数值键 snake_case。命中**不**再用键名字符集限定
# ——此前正则写 `[a-z_]+`，含数字的键在写读两侧同时不可见：只写不读 / 只读不写两种静默错误
# 全不报（键名进不了 written 也进不了 read）。改为「先按 `["任意键"] =` 全量取键、再单独判
# 键名是否合规」：任何键都不会对判定不可见，键名不合规即显式报红。两侧计数一致＝交叉对上。
KEY_NAME = re.compile(r"[a-z_][a-z0-9_]*")
ASSIGN_KEY = re.compile(r'\["([^"]*)"\]\s*=')
READ_KEY = re.compile(r'\b(d)\s*\.\s*GetValueOrDefault\("([^"]*)"')
ANY_READ_KEY = re.compile(r'\b\w+\s*\.\s*GetValueOrDefault\("([^"]*)"')
# 交叉断言：键字面量的**出现次数**必须等于正则命中数（实测写侧 21=21、读侧 21=21）。取键正则
# 覆盖任意键名后二者天然相等；一旦有人把正则收窄回 `[a-z_]+`，字面量计数不动而命中数掉，含数字
# 键又回到两侧都不可见的静默态——这条算术不依赖捕获组宽窄，故能兜住那次回退。
assign_literals = collect.group(1).count('["')
read_literals = apply.group(1).count('GetValueOrDefault("')

assigned = ASSIGN_KEY.findall(collect.group(1))
read_pairs = READ_KEY.findall(apply.group(1))
written = set(assigned)
read = {key for _recv, key in read_pairs}
# version 由 LoadRun 单独判定，不参与对称性
written.discard("version")
read.discard("version")
illegal = sorted({key for key in written | read if not KEY_NAME.fullmatch(key)})
# 读侧换接收者（改写成本地变量再取键）会让键整批对判定不可见——与含数字键同族，显式报红。
foreign = sorted(set(ANY_READ_KEY.findall(apply.group(1))) - {k for _r, k in read_pairs})

# 零命中守卫：正则或结构变了会两边皆空，`written - read`/`read - written` 都为空而误判 clean
# ——假绿比没门禁更糟（AGENTS §6 铁律 2），取不到判据必须显式失败。
if not written or not read:
    print(f"::error::写读键集为空（写 {len(written)} / 读 {len(read)}）——正则或结构变了？门禁需同步")
    sys.exit(1)
if len(assigned) != assign_literals:
    print(f"::error::写侧键字面量 {assign_literals} 处、取键正则只命中 {len(assigned)} 个"
          "——正则收窄会静默丢掉一部分键（此前 `[a-z_]+` 就是这样漏掉含数字键的），"
          "取键正则必须覆盖任意键名，键名合规性另判")
    sys.exit(1)
if len(read_pairs) != read_literals:
    print(f"::error::读侧取键字面量 {read_literals} 处、取键正则只命中 {len(read_pairs)} 个"
          "——正则收窄会静默丢掉一部分键，取键正则必须覆盖任意键名")
    sys.exit(1)

errors = []
for label, match in (("CollectRunDict 写入块", collect), ("ApplyRunDict 读取块", apply),
                     ("RunSaveVersion 常量", version)):
    hits = cond_hits(match)
    if hits:
        errors.append(f"{label} 落在条件编译段内（第 {', '.join(str(n) for n in hits)} 行有 "
                      "#if/#elif/#else/#endif）——该段是否编译进产物不可静态判定，"
                      "键集对账在此失效；须把条件编译去掉或把该段拆出单独文件")
if illegal:
    errors.append(f"存档键名不合 snake_case（AGENTS §9）：{illegal}"
                  f"（写侧键字面量 {len(assigned)} 条、读侧 {len(read_pairs)} 条，其中 {len(illegal)} 个"
                  "键名不合法）——键名不合法时它在写读对称判定里取不到，须改名或同步本门禁的键口径")
if foreign:
    errors.append(f"读侧出现非 `d.GetValueOrDefault(\"键\", …)` 形态的取键：{foreign}"
                  "——接收者换名后该键对写读对称判定不可见（静默漏判），须回到 d 直取")
for key in sorted(written - read):
    errors.append(f"`{key}` 只写不读：存了但读档不还原（玩家进度静默丢失）")
for key in sorted(read - written):
    errors.append(f"`{key}` 只读不写：读档永远取默认值（该字段从未落盘）")

code_version = int(version.group(1))
added, removed = sorted(written - FROZEN_KEYS), sorted(FROZEN_KEYS - written)
if added or removed:
    detail = []
    if added:
        detail.append(f"新增 {added}")
    if removed:
        detail.append(f"删除或改名 {removed}")
    errors.append(
        f"持久化键集与冻结基线不一致（{'；'.join(detail)}）——存档字段改名会让旧档该字段静默归零。"
        "处置：bump RunSaveVersion（旧档将被拒，等于丢进度，须人类确认）或补旧档迁移分支；"
        "确认后同步 FROZEN_KEYS，并在提交正文写明理由")
if code_version != FROZEN_VERSION:
    errors.append(f"RunSaveVersion = {code_version}，冻结基线为 {FROZEN_VERSION}"
                  "——bump 版本会让旧档读不回（LoadRun 版本不符即 false）；有意为之则同步 FROZEN_VERSION，"
                  "并在提交正文写明理由")

if errors:
    for e in errors:
        print("::error::" + e)
    print(f"save-symmetry gate: FAILED（写 {len(written)} / 读 {len(read)} / 冻结基线 {len(FROZEN_KEYS)}）")
    sys.exit(1)
print(f"save-symmetry gate: clean（写读各 {len(written)} 个字段（键字面量 {len(assigned)} 条，"
      f"键名全部合规），一一对应；与冻结基线一致，RunSaveVersion={code_version}）")
PY
