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
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

python3 - <<'PY'
import pathlib, re, sys

path = pathlib.Path("csharp/godot/GameState.RunSave.cs")
src = path.read_text(encoding="utf-8")

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

written = set(re.findall(r'\["([a-z_]+)"\]\s*=', collect.group(1)))
read = set(re.findall(r'GetValueOrDefault\("([a-z_]+)"', apply.group(1)))
# version 由 LoadRun 单独判定，不参与对称性
written.discard("version")
read.discard("version")

# 零命中守卫：正则或结构变了会两边皆空，`written - read`/`read - written` 都为空而误判 clean
# ——假绿比没门禁更糟（AGENTS §6 铁律 2），取不到判据必须显式失败。
if not written or not read:
    print(f"::error::写读键集为空（写 {len(written)} / 读 {len(read)}）——正则或结构变了？门禁需同步")
    sys.exit(1)

errors = []
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
print(f"save-symmetry gate: clean（写读各 {len(written)} 个字段，一一对应；与冻结基线一致，"
      f"RunSaveVersion={code_version}）")
PY
