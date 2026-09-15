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

# 键口径：AGENTS §9 规定数值键 snake_case。命中**不**再用键名字符集限定——此前正则写
# `[a-z_]+`，含数字的键（fps_cap2 这类）在写读两侧同时不可见：只写不读、只读不写、可持久化
# 却未被「全部恢复默认」覆盖三种静默错误全不报，而这三样正是本门禁存在的唯一理由。
# 改为「先按 `["任意键"] =` 全量取键、再单独判键名是否合规」：任何键都不会对判定不可见，
# 键名不合规即显式报红（而不是静默漏判）。两侧计数一致＝键字面量与合法命中数交叉对上。
KEY_NAME = re.compile(r"[a-z_][a-z0-9_]*")
ASSIGN_KEY = re.compile(r'\["([^"]*)"\]\s*=')
READ_KEY = re.compile(r'\b(data)\s*\.\s*GetValueOrDefault\("([^"]*)"')
ANY_READ_KEY = re.compile(r'\b\w+\s*\.\s*GetValueOrDefault\("([^"]*)"')
# 交叉断言：键字面量的**出现次数**必须等于正则命中数。取键正则覆盖任意键名后二者天然相等
# （实测写侧 26=26、读侧 27=27），一旦有人把正则收窄回 `[a-z_]+`，字面量计数不动而命中数掉、
# 含数字键又回到「两侧都不可见」的静默态——这条算术不依赖捕获组宽窄，故能兜住那次回退。
assign_literals = collect.group(1).count('["')
read_literals = apply.group(1).count('GetValueOrDefault("')

assigned = ASSIGN_KEY.findall(collect.group(1))
# 只认「data 直接取键」；空串默认值是合法读法（StringName 字段的惯例），不当漏读。
# window_size 只出现在旧档案迁移分支（新键 window_mode 取代之），写入侧早已不产出这个键
# ——这是决策点，改它等于恢复旧键的读写，须连迁移分支一起删。
read_pairs = READ_KEY.findall(apply.group(1))
read = {key for _recv, key in read_pairs}
read -= {"window_size"}
written = set(assigned)

illegal = sorted({key for key in assigned + [k for _r, k in read_pairs] if not KEY_NAME.fullmatch(key)})
# 读侧换接收者（改写成本地变量再取键）会让键整批对判定不可见——与含数字键同族，显式报红。
foreign = sorted(set(ANY_READ_KEY.findall(apply.group(1))) - {k for _r, k in read_pairs})
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
if len(assigned) != assign_literals:
    print(f"::error::写侧键字面量 {assign_literals} 处、取键正则只命中 {len(assigned)} 个"
          "——正则收窄会静默丢掉一部分键（此前 `[a-z_]+` 就是这样漏掉含数字键的），"
          "取键正则必须覆盖任意键名，键名合规性另判")
    sys.exit(1)
if len(read_pairs) != read_literals:
    print(f"::error::读侧取键字面量 {read_literals} 处、取键正则只命中 {len(read_pairs)} 个"
          "——正则收窄会静默丢掉一部分键，取键正则必须覆盖任意键名")
    sys.exit(1)

if illegal:
    errors.append(f"设置键名不合 snake_case（AGENTS §9）：{illegal}"
                  f"（写侧键字面量 {len(assigned)} 条、读侧 {len(read_pairs)} 条，其中 {len(illegal)} 个"
                  "键名不合法）——键名不合法时它在写读与复位三项判定里都取不到，"
                  "须改名或同步本门禁的键口径")
if foreign:
    errors.append(f"读侧出现非 `data.GetValueOrDefault(\"键\", …)` 形态的取键：{foreign}"
                  "——接收者换名后该键对写读对称判定不可见（静默漏判），须回到 data 直取")

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
print(f"settings-symmetry gate: clean（写读各 {len(written)} 字段（键字面量 {len(assigned)} 条，"
      f"键名全部合规）；重置覆盖 {len(touched)} 字段）")
PY
