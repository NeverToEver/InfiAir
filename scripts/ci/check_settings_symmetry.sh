#!/usr/bin/env bash
# 设置域对称性门禁：CollectSettingsDict（写）与 ApplySettingsDict（读）必须一一对应，
# 且设置页声明引用的翻译键必须真实存在。
# 用法：bash scripts/ci/check_settings_symmetry.sh
# 为什么需要：settings.json 与门禁可见的存档不同——写而未读 = 玩家改了设置下次启动回到默认，
# 读而未写 = 每次读档落默认值静默覆盖（与存档丢进度同类，但编译与冒烟都发现不了）；
# 设置项文案键写错则界面直接显示键名本身。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

python3 - <<'PY'
import pathlib, re, sys

errors = []

service = pathlib.Path("csharp/godot/SettingsService.cs").read_text(encoding="utf-8")
collect = re.search(r"CollectSettingsDict\(\)\s*=>\s*new\(\)\s*\{(.*?)\n    \};", service, re.S)
apply = re.search(r"public void ApplySettingsDict\(Godot\.Collections\.Dictionary data\)(.*?)\n    \}", service, re.S)
reset = re.search(r"public void ResetToDefaults\(\)(.*?)\n    \}", service, re.S)
if not collect or not apply or not reset:
    print("::error::无法定位 CollectSettingsDict/ApplySettingsDict/ResetToDefaults（结构变了？门禁需同步）")
    sys.exit(1)

written = set(re.findall(r'\["([a-z_]+)"\]\s*=', collect.group(1)))
# 只认「data 直接取键」；空串默认值是合法读法（StringName 字段的惯例），不当漏读。
# 显式豁免：window_size 只出现在旧档案迁移分支（新键 window_mode 取代之），写入侧早已不产出
# 这个键——这是决策点，改它等于恢复旧键的读写，须连迁移分支一起删。
read = set(re.findall(r'data\s*\.\s*GetValueOrDefault\("([a-z_]+)"', apply.group(1)))
read -= {"window_size"}
# version 为文件版本号，tutorial_done/key_bindings 的读写在别处（GameState.Save/RunSave），不参与对称性
EXEMPT = {"version"}
written -= EXEMPT
read -= EXEMPT

for key in sorted(written - read):
    errors.append(f"`{key}` 只写不读：设置项存了但读档不还原（玩家改动下次启动丢失）")
for key in sorted(read - written):
    errors.append(f"`{key}` 只读不写：读档永远取默认值（该设置从未落盘）")

# 恢复默认必须覆盖全部可持久化设置项之外的「玩家可调项」：粗校验——ResetToDefaults 至少
# 触及多个字段，且不能只改 Locale（防止有人把它写成空壳后仍然「通过」）
touched = set(re.findall(r"^\s+([A-Z][A-Za-z]+)\s*=", reset.group(1), re.M))
if len(touched) < 15:
    errors.append(f"ResetToDefaults 只重置 {len(touched)} 个字段（疑似漏项）：{sorted(touched)}")

# 设置页文案键存在性：页表与各分组标题引用的 SET_* 键必须在 translations.csv 中。
# 以 "_" 结尾的是拼接前缀（"SET_AIM_" + level），按静态键判会误报，一并跳过。
ui = pathlib.Path("csharp/godot/SettingsUi.cs").read_text(encoding="utf-8")
csv_text = pathlib.Path("data/translations.csv").read_text(encoding="utf-8")
known = {line.split(",", 1)[0] for line in csv_text.splitlines() if line and not line.startswith("keys,")}
prefixes = [k for k in set(re.findall(r'"(SET_[A-Z0-9_]+)"', ui)) if k.endswith("_")]
for key in sorted(set(re.findall(r'"(SET_[A-Z0-9_]+)"', ui)) - set(prefixes)):
    if key not in known:
        errors.append(f"设置页引用 `{key}` 但 translations.csv 无此行（界面显示键名本身）")
for prefix in sorted(prefixes):
    if not any(k.startswith(prefix) for k in known):
        errors.append(f"设置页拼接前缀 `{prefix}` 在 translations.csv 无任何匹配行")

if errors:
    for e in errors[:40]:
        print("::error::" + e)
    print(f"settings-symmetry gate: FAILED（写 {len(written)} / 读 {len(read)} / 重置 {len(touched)} 字段）")
    sys.exit(1)
print(f"settings-symmetry gate: clean（写读各 {len(written)} 字段；重置覆盖 {len(touched)} 字段）")
PY
