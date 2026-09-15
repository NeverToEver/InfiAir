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
# window_size 只出现在旧档案迁移分支（新键 window_mode 取代之），写入侧早已不产出这个键
# ——这是决策点，改它等于恢复旧键的读写，须连迁移分支一起删。
read = set(re.findall(r'data\s*\.\s*GetValueOrDefault\("([a-z_]+)"', apply.group(1)))
read -= {"window_size"}
# version 由落盘侧 GameState.SaveSettings 在 CollectSettingsDict 之后补写（data["version"] = ...），
# 不在 Collect 字面表内；把它并进写入侧，version 才能真正参与写读对称判定（此前整键豁免＝判不到）。
save = pathlib.Path("csharp/godot/GameState.Save.cs").read_text(encoding="utf-8")
if re.search(r'\["version"\]\s*=', save):
    written.add("version")

# 零命中守卫：正则或结构变了会两边皆空，对称差为空而误判 clean——假绿比没门禁更糟
# （AGENTS §6 铁律 2），取不到判据必须显式失败。
if not written or not read:
    print(f"::error::写读键集为空（写 {len(written)} / 读 {len(read)}）——正则或结构变了？门禁需同步")
    sys.exit(1)

for key in sorted(written - read):
    errors.append(f"`{key}` 只写不读：设置项存了但读档不还原（玩家改动下次启动丢失）")
for key in sorted(read - written):
    errors.append(f"`{key}` 只读不写：读档永远取默认值（该设置从未落盘）")

# 恢复默认必须覆盖全部可持久化设置项：集合关系判定（键 -> ResetToDefaults 触及的字段名）。
# 此前用「触及字段数 ≥15」的魔数粗校验：新增设置项、写读都加、就是不加进 ResetToDefaults 时
# 字段数照旧达标，门禁判 clean，而玩家点「全部恢复默认」后该项仍是旧值（且随后落盘固化旧值）。
# 键到字段名默认按 snake_case → PascalCase 映射；不规则命名在 RESET_ALIASES 里逐一列出。
# 复位改由别处负责、或刻意不复位的键在 RESET_EXEMPT 里列出并给理由（不登记即报「疑似漏项」）。
RESET_ALIASES = {
    "vsync": "VSync",                 # 缩写保留全大写（C# 属性名 VSync）
    "aim_assist": "AimAssistLevel",   # 档位字段带 Level 后缀
    "custom_width": "CustomWindowWidth",    # 字段名带 Window 中缀
    "custom_height": "CustomWindowHeight",
}
RESET_EXEMPT = {
    "tutorial_done": "教程完成度不是偏好设置，复位等于让玩家重看教程——GameState.ResetAllSettings 刻意保留",
    "key_bindings": "键位有独立事实源：GameState.ResetAllSettings 先调 ResetKeyBindings()，不归本服务",
    "difficulty": "难度由 GameState.ResetAllSettings 走 SetDifficulty(medium) 正口复位（校验/落盘/广播），"
                  "不归本服务",
}
touched = set(re.findall(r"^\s+([A-Z][A-Za-z]+)\s*=", reset.group(1), re.M))
# 零命中守卫：ResetToDefaults 被写成空壳（或正则漂移）时 touched 为空，差集同为空——必须先炸
if not touched:
    print("::error::ResetToDefaults 里没解析到任何字段赋值（结构变了或方法被掏空？）——拒绝判 clean")
    sys.exit(1)
persistable = written - {"version"}
for key in sorted(persistable):
    field = RESET_ALIASES.get(key) or "".join(part.capitalize() for part in key.split("_"))
    if field not in touched and key not in RESET_EXEMPT:
        errors.append(f"`{key}` 可持久化但 ResetToDefaults 没重置（映射字段 `{field}` 不在赋值列表里）"
                      "——「全部恢复默认」后仍是旧值；确实不复位就登记进 RESET_EXEMPT 并给理由")

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
