#!/usr/bin/env bash
# 零引用成员门禁：csharp/ 里「已声明但全库无引用」的公开/内部成员必须逐一登记保留理由（双向判定）。
# 用法：bash scripts/ci/check_zero_ref_members.sh
#
# 抓的静默错误（ROADMAP「零引用成员保留面」口径）：新增一个没人引用的成员——编译器、单测、冒烟、
# 全部现有门禁都不会有任何信号，代码面只增不减；反过来，合法保留的白盒读口被「顺手删掉」时也没人
# 知道（这些读口是实机调参时的观察面）。本门禁把「保留」变成需要写理由的显式动作：单源即本脚本
# ALLOW 表，AGENTS/ROADMAP 只给指针不复述清单。
#
# 判定口径：
#   1) 声明面 = csharp/godot/**.cs 与 csharp/core/**.cs（**排除 csharp/tests/**：测试方法由 xUnit
#      运行器反射调用，结构上必然零引用，不是死代码），只取**类型成员级**声明——文件作用域
#      namespace（本仓库全部如此）下花括号深度 1 的行，且以 public / internal / protected internal 打头；
#   2) 引用面 = **全库 .cs**（含 tests）先按 C# 词法剥注释（保留换行与偏移）后的文本。字符串内容
#      刻意保留：`Call("Name")` / `CallGroup("hud","meta_jitter")` 这类字符串派发是真实引用通道，
#      剥掉字符串会把它们误判成死代码。声明面之外任何位置（含另一分部文件、tests）出现该名字，
#      即算被引用；
#   3) 名字出现次数 ≤ 声明数 → 零引用候选；未登记的候选 → 红（输出 文件:行、成员名与声明原文）；
#   4) 登记项不再是候选（成员被删/改名，或已恢复引用）→ 红——防合法保留被顺手删掉又没人知道；
#   5) 候选数低于 MIN_CANDIDATES、或 ALLOW 为空 → 红（扫描范围/正则漂移时判据失效，拒绝判 clean）。
#
# 非候选类别（每一类都显式排除并给出理由，不靠放宽规则把整类悄悄漏掉）：
#   - `public override`：引擎回调（_Ready/_Process/_PhysicsProcess/_Draw/_Input/_UnhandledInput/
#     _EnterTree/_ExitTree/_Notification 等）与虚表实现，引用点在引擎与基类，静态扫描看不到；
#   - `[Signal]` 标注的 delegate：Godot 源生成器消费它生成 `SignalName.X` 与 emit 重载，名字本身
#     不在 C# 代码里再现（本仓库 56 条信号全属此类）；
#   - `[Export]` 标注的成员：Godot 属性系统引用，值由编辑器/`.tscn` 写入，代码侧可以完全零引用；
#   - 条件编译段（#if/#elif/#else/#endif）内的声明：不在构建态判据面内，不判；
#   - 类型声明（class/struct/interface/enum/record）与构造函数/索引器/运算符：不是本门禁的成员面。
#
# 已知边界（宁可漏判也不误报——误报会让门禁被迫放宽）：
#   - 计数按**成员名**聚合：同名重载、同名成员（另一个类里的同名字段）会互相掩盖，真死代码可能漏判；
#   - `<see cref="Name"/>` 文档引用与散文字符串**不算**引用（注释已被剥掉），删除前须自行 grep 文档；
#   - 只判 public/internal/protected internal：private 成员删错也编译不过，不属静默错误面。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

python3 - <<'PY'
import collections
import pathlib
import re
import sys

ROOT = pathlib.Path.cwd()
SKIP = {".git", ".godot", "builds", "tools", "bin", "obj", "__pycache__", ".venv"}
MODIFIERS = ("public", "internal", "protected internal")
TYPE_KEYWORD = re.compile(r"\b(class|struct|interface|enum|record)\b")

# 候选数下限（当前实测 68 条登记）。扫描面漂移（正则写坏、路径改名、csharp/ 被整体挪走）时
# 候选会骤减甚至归零——那种「判据取不到」必须显式失败，不能判 clean。
MIN_CANDIDATES = 45

# 允许清单（单源）：(相对路径, 成员名, 保留理由)。粒度 = (文件, 成员名)，同名重载共用一把钥匙。
# 理由按族归并（同一段代码块声明的同族成员写一次），改理由就等于改判据，别处不复述。
ALLOW = (
    # Boss/Enemy 语义化类型查询：对外公开接口（Bullet 的爆炸分支注释仍以 is_boss 语义解释
    # `area is not Boss`，两个实现是该语义的公开读口）。
    ("csharp/godot/Boss.cs", "IsBoss", "对外公开接口：语义化类型查询（Boss 恒 true），零引用保留面"),
    ("csharp/godot/Enemy.cs", "IsBoss", "对外公开接口：语义化类型查询（非 Boss 恒 false），与 Boss 成对"),
    # 事件基类模板方法门面：子类实现/消费点，注册表键来自 EVENT_FACTORIES，不经过这些口。
    ("csharp/godot/GameEvent.cs", "EventId", "模板方法子类实现点（5 个子类 override；id 单源在 EVENT_FACTORIES）"),
    ("csharp/godot/GameEvent.cs", "GetCtx", "事件基类门面：子类读 context 的宽容访问口（缺键返回 default）"),
    ("csharp/godot/GameEvent.cs", "RequestEnd", "事件基类门面：子类请求提前结束（context 注入回调）"),
    # 统一事件管理器的诊断/外部驱动公开接口（代码内注释声明的「对外公开接口」段）。
    ("csharp/godot/GameEventManager.cs", "ReloadConfig", "配置重载诊断入口（GameState.ReloadBalance 联动的公开口）"),
    ("csharp/godot/GameEventManager.cs", "IsRunActive", "诊断/外部驱动公开接口（本局活跃查询）"),
    ("csharp/godot/GameEventManager.cs", "EventIds", "诊断公开接口：已注册事件 id 列表（EVENT_FACTORIES 为唯一事实源）"),
    ("csharp/godot/GameEventManager.cs", "ActiveEvent", "诊断公开接口：指定分组当前激活事件对象"),
    ("csharp/godot/GameEventManager.cs", "ActiveRemaining", "诊断公开接口：当前 fog 事件剩余时长"),
    # GameState 门面：路径常量读口、探针读口与设置/天赋段成对 API。
    ("csharp/godot/GameState.cs", "BALANCE_PATH", "门面 API：balance 路径单源（私有常量）的公开读口"),
    ("csharp/godot/GameState.cs", "FeelTimeScale", "探针读口（注释声明）：验证顿帧确实压低了时间缩放"),
    ("csharp/godot/GameState.Settings.cs", "ApplyDisplaySettings", "设置段门面 API（启动应用已由 GameState._Ready 直调 SettingsService）"),
    ("csharp/godot/GameState.State.cs", "SETTINGS_PATH", "门面 API：settings 路径单源（私有常量）的公开读口"),
    ("csharp/godot/GameState.Talent.cs", "TalentCap", "天赋门面 API：Level/EffLevel/CapFor 成对读口（上限口径）"),
    # Hud 探针读口（注释声明）。
    ("csharp/godot/Hud.cs", "InfoBannerAlpha", "探针读口（注释声明）：横幅停留被并行语义吞掉时只在此暴露"),
    ("csharp/godot/Hud.cs", "BossBarModulate", "探针读口（注释声明）：减少闪光下阶段切换不得提亮"),
    # Main 实机调参观察面（代码内注释声明的保留理由段）。
    ("csharp/godot/Main.cs", "MetaFx", "实机调参观察面（注释声明成段保留：演出量读数由本类独占编排）"),
    ("csharp/godot/Main.cs", "GiveUpCharge", "实机调参观察面（注释声明成段保留：放弃充能读数）"),
    ("csharp/godot/Main.cs", "SetChargeTime", "实机调参观察面（注释声明成段保留：母舰召唤蓄力白盒写口）"),
    # MetaHealthFX 调参阅数读口（ROADMAP 点名的「Player/MetaHealthFX 调参阅数读口」）+ 状态常量访问口。
    ("csharp/godot/MetaHealthFX.cs", "GetStateNormal", "状态值静态访问口（int 口径，与 STATE_* 常量成对）"),
    ("csharp/godot/MetaHealthFX.cs", "GetStateDying", "状态值静态访问口（int 口径，与 STATE_* 常量成对）"),
    ("csharp/godot/MetaHealthFX.cs", "DamageX", "调参阅数读口（ROADMAP 口径）：血量-裂纹映射输入值"),
    ("csharp/godot/MetaHealthFX.cs", "HealJitter", "调参阅数读口（ROADMAP 口径）：治疗抖动相位"),
    ("csharp/godot/MetaHealthFX.cs", "HeartRate", "调参阅数读口（ROADMAP 口径）：心率曲线输出"),
    ("csharp/godot/MetaHealthFX.cs", "Breath", "调参阅数读口（ROADMAP 口径）：DYING 呼吸相位"),
    ("csharp/godot/MetaHealthFX.cs", "Rect", "调参阅数读口（ROADMAP 口径）：裂纹层绘制矩形"),
    ("csharp/godot/MetaHealthFX.cs", "UploadCount", "调参阅数读口（ROADMAP 口径）：参数上传次数"),
    ("csharp/godot/MetaHealthFX.cs", "EarlyOutCount", "调参阅数读口（ROADMAP 口径）：epsilon 提前退出次数"),
    # Mothership 坞态白盒写口（代码内注释声明成段保留理由）。
    ("csharp/godot/Mothership.cs", "SetStateTimer", "坞态白盒写口（注释声明成段保留：实机调参观察面，仅供诊断）"),
    ("csharp/godot/Mothership.cs", "SetMagCellTimer", "坞态白盒写口（注释声明成段保留：实机调参观察面，仅供诊断）"),
    ("csharp/godot/Mothership.cs", "SetMagCells", "坞态白盒写口（注释声明成段保留：实机调参观察面，仅供诊断）"),
    ("csharp/godot/Mothership.cs", "SetWarnEjectTimer", "坞态白盒写口（注释声明成段保留：实机调参观察面，仅供诊断）"),
    # Player 调参读口/白盒写口（ROADMAP 点名的「Player 调参阅数读口」族：迷雾效果、调参档位、
    # 无敌/冲刺/受击计时——实机核对手段，无头探针不消费）。
    ("csharp/godot/Player.cs", "GetAugmentEffects", "门面 API：声明式增幅效果表公开访问口（外部按 id 遍历效果定义）"),
    ("csharp/godot/Player.cs", "EnrageSlow", "调参阅数读口（ROADMAP 口径）：狂暴对玩家减速倍率"),
    ("csharp/godot/Player.cs", "SetDead", "白盒写口（ROADMAP 口径）：死亡态实机核对"),
    ("csharp/godot/Player.cs", "SetDashCooldown", "白盒写口（ROADMAP 口径）：冲刺冷却实机核对"),
    ("csharp/godot/Player.cs", "SetSinceDamage", "白盒写口（ROADMAP 口径）：受击计时实机核对"),
    ("csharp/godot/Player.cs", "SetLastHitFrame", "白盒写口（ROADMAP 口径）：受击帧号实机核对"),
    ("csharp/godot/Player.cs", "DashCooldownRemaining", "调参阅数读口（ROADMAP 口径）：冲刺冷却剩余"),
    ("csharp/godot/Player.cs", "FogInvertActive", "调参阅数读口（ROADMAP 口径）：迷雾输入反转生效态"),
    ("csharp/godot/Player.cs", "FogBulletJitter", "调参阅数读口（ROADMAP 口径）：迷雾弹道抖动角"),
    ("csharp/godot/Player.cs", "FogMisfireChance", "调参阅数读口（ROADMAP 口径）：迷雾哑火概率"),
    ("csharp/godot/Player.cs", "FogForcedDir", "调参阅数读口（ROADMAP 口径）：迷雾强制位移方向"),
    ("csharp/godot/Player.cs", "FogForcedHold", "调参阅数读口（ROADMAP 口径）：迷雾强制位移保持时长"),
    ("csharp/godot/Player.cs", "BoostToggleActive", "调参阅数读口（ROADMAP 口径）：推进切换态的手感核对"),
    ("csharp/godot/Player.cs", "FineToggleActive", "调参阅数读口（ROADMAP 口径）：微调切换态的手感核对"),
    ("csharp/godot/Player.cs", "SetBoostToggle", "白盒写口（ROADMAP 口径）：推进切换态实机核对"),
    ("csharp/godot/Player.cs", "SetFineToggle", "白盒写口（ROADMAP 口径）：微调切换态实机核对"),
    # PlayerAugmentVisuals 附件节点读口：实机调参时核对附件几何/可见性（ROADMAP 点名的观察面）。
    ("csharp/godot/PlayerAugmentVisuals.cs", "ExplosiveGlow", "增幅附件节点读口（实机调参观察面）"),
    ("csharp/godot/PlayerAugmentVisuals.cs", "LaserPod", "增幅附件节点读口（实机调参观察面）"),
    ("csharp/godot/PlayerAugmentVisuals.cs", "ArmorRing", "增幅附件节点读口（实机调参观察面）"),
    ("csharp/godot/PlayerAugmentVisuals.cs", "RegenRing", "增幅附件节点读口（实机调参观察面）"),
    ("csharp/godot/PlayerAugmentVisuals.cs", "LifestealTips", "增幅附件节点读口（实机调参观察面）"),
    ("csharp/godot/PlayerAugmentVisuals.cs", "ShieldHex", "增幅附件节点读口（实机调参观察面）"),
    ("csharp/godot/PlayerAugmentVisuals.cs", "EvasionGhost", "增幅附件节点读口（实机调参观察面）"),
    ("csharp/godot/PlayerAugmentVisuals.cs", "DashFins", "增幅附件节点读口（实机调参观察面）"),
    ("csharp/godot/PlayerAugmentVisuals.cs", "SlowRing", "增幅附件节点读口（实机调参观察面）"),
    ("csharp/godot/PlayerAugmentVisuals.cs", "Beacon", "增幅附件节点读口（实机调参观察面）"),
    # PlayerParry 相位值静态访问口（代码内注释声明：当前无生产调用方，保留为公开查询口）。
    ("csharp/godot/PlayerParry.cs", "GetPhaseIdle", "相位值静态访问口（注释声明保留为公开查询口）"),
    ("csharp/godot/PlayerParry.cs", "GetPhaseWindup", "相位值静态访问口（注释声明保留为公开查询口）"),
    ("csharp/godot/PlayerParry.cs", "GetPhaseActive", "相位值静态访问口（注释声明保留为公开查询口）"),
    ("csharp/godot/PlayerParry.cs", "GetPhaseRecover", "相位值静态访问口（注释声明保留为公开查询口）"),
    # 天赋 UI/服务读口。
    ("csharp/godot/TalentFanView.cs", "Selected", "选中节点读口（与 SetSelected 成对，供外部读取当前选中）"),
    # UITheme 共享设施：文档单源指针指向它们，删除须连带改文档（归文档 owner）。
    ("csharp/godot/UITheme.cs", "ChamferPoints", "切角语汇单源公开入口（DESIGN_BASELINE 与 Hud 注释均指向它）"),
    ("csharp/godot/UITheme.cs", "AnimateClose", "共享动效设施（DESIGN_BASELINE §2.8 与 ROADMAP 决策点名；模态退场现走 AnimateModalClose）"),
)


class Scanner:
    """按 C# 词法跳过字符串/字符字面量，用于花括号深度统计。"""

    def __init__(self, text):
        self.text = text
        self.i = 0

    def skip_literal(self):
        text, i = self.text, self.i
        n = len(text)
        if text.startswith('"""', i):
            end = text.find('"""', i + 3)
            self.i = n if end < 0 else end + 3
            return
        if text[i] == "@" and i + 1 < n and text[i + 1] == '"':
            i += 2
            while i < n:
                if text[i] == '"':
                    if i + 1 < n and text[i + 1] == '"':
                        i += 2
                        continue
                    self.i = i + 1
                    return
                i += 1
            self.i = n
            return
        quote = text[i]
        i += 1
        while i < n:
            if text[i] == "\\":
                i += 2
                continue
            if text[i] == quote:
                self.i = i + 1
                return
            i += 1
        self.i = n


def line_depths(text):
    """返回每行起始处的花括号深度（字符串/字符字面量内的括号不计）。"""
    sc = Scanner(text)
    depths = [0]
    depth = 0
    while sc.i < len(sc.text):
        c = sc.text[sc.i]
        if c == "\n":
            depths.append(depth)
            sc.i += 1
            continue
        if c == '"' or c == "'" or (c == "@" and sc.i + 1 < len(sc.text) and sc.text[sc.i + 1] == '"'):
            sc.skip_literal()
            continue
        if c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
        sc.i += 1
    return depths


def declaration_name(decl):
    """声明的成员名 = 声明文本（已截到 `(`/`{`/`;`/`=` 之前）里的最后一个标识符。"""
    text = decl.strip()
    if text.endswith(">"):                      # 泛型方法名 `Foo<T>` → 剥掉尾部实参表
        depth = 0
        for i in range(len(text) - 1, -1, -1):
            if text[i] == ">":
                depth += 1
            elif text[i] == "<":
                depth -= 1
                if depth == 0:
                    text = text[:i]
                    break
    if re.search(r"\b(operator|this)\b", text):  # 运算符与索引器名字提取不适用
        return None
    tokens = re.findall(r"[A-Za-z_]\w*", text)
    return tokens[-1] if tokens else None


def iter_files(prod_only):
    for path in sorted(ROOT.rglob("*.cs")):
        if any(part in SKIP for part in path.parts):
            continue
        rel = str(path.relative_to(ROOT)).replace("\\", "/")
        if prod_only and rel.startswith("csharp/tests/"):
            continue
        yield rel, path


# 共用词法模块（scripts/ci/csharp_lex.py）：剥注释口径与其它门禁同源，缺失即红。
LEX_DIR = ROOT / "scripts" / "ci"
if not (LEX_DIR / "csharp_lex.py").exists():
    print("::error::scripts/ci/csharp_lex.py 不存在——共用词法剥离模块缺失，取不到判据，拒绝判 clean")
    sys.exit(1)
sys.path.insert(0, str(LEX_DIR))
import csharp_lex


def stripped_text(path):
    text, _ = csharp_lex.strip_comments(path.read_text(encoding="utf-8", errors="replace"))
    return text


errors: list[str] = []
# (相对路径, 行号, 成员名, 声明原文, 是否可直接判死代码)。声明数与候选都按成员名聚合：同名重载、
# 分部声明、override 与基类声明共享一把钥匙——名字级计数下，override 声明是**声明**而不是引用，
# 否则「有子类 override 就没人在用」的虚方法会整类漏判。
members: list[tuple[str, int, str, str, bool]] = []
for rel, path in iter_files(prod_only=True):
    text = stripped_text(path)
    depths = line_depths(text)
    if depths[-1] != 0:
        errors.append(
            f"{rel} 花括号深度在文件末尾为 {depths[-1]}（应为 0）——词法剥壳或条件编译段失衡，"
            "成员级判据不可信，本门禁拒绝判 clean"
        )
        continue
    lines = text.split("\n")
    raw = path.read_text(encoding="utf-8", errors="replace").split("\n")
    conditional = set(csharp_lex.conditional_lines(text))
    for idx in range(len(lines)):
        if depths[idx] != 1 or (idx + 1) in conditional:
            continue
        stripped = lines[idx].lstrip()
        if not stripped.startswith(MODIFIERS):
            continue
        # 声明可能跨行（属性/字段的 `{` 或 `=` 落在下一行）：拼到首个深度 0 终结符为止
        parts, cursor = [], idx
        while cursor < len(lines) and cursor < idx + 30:
            current = " ".join(parts + [lines[cursor].strip()]).strip()
            stop = None
            i, n = 0, len(current)
            while i < n:
                c = current[i]
                if c == '"' or c == "'" or (c == "@" and i + 1 < n and current[i + 1] == '"'):
                    i = csharp_lex.skip_literal(current, i)
                    continue
                if c in "({;":
                    stop = i
                    break
                if c == "=" and not (i + 1 < n and current[i + 1] == "="):
                    stop = i
                    break
                i += 1
            if stop is not None:
                parts.append(current[:stop])
                break
            parts.append(current)
            cursor += 1
        decl = " ".join(part.strip() for part in parts)
        if not decl or TYPE_KEYWORD.search(decl):
            continue
        name = declaration_name(decl)
        if not name:
            continue
        # 可直接判死代码的面：非 override（引擎回调/虚表实现）、非 [Signal] 委托（源生成器消费）、
        # 非 [Export]（Godot 属性系统引用）——这些声明仍计入声明数（见 members 注释）。
        eligible = True
        if re.search(r"\boverride\b", decl):
            eligible = False
        lookback = [lines[k].strip() for k in range(max(0, idx - 3), idx) if lines[k].strip()]
        if re.search(r"\bdelegate\b", decl) and any(line.startswith("[Signal]") for line in lookback):
            eligible = False
        if any(line.startswith("[Export") for line in lookback):
            eligible = False
        members.append((rel, idx + 1, name, raw[idx].strip() if idx < len(raw) else "", eligible))

if not members:
    print("::error::未解析出任何类型成员级声明——扫描范围或深度口径漂移，拒绝判 clean")
    sys.exit(1)

word_count = collections.Counter()
for rel, path in iter_files(prod_only=False):
    for token in re.findall(r"[A-Za-z_]\w*", stripped_text(path)):
        word_count[token] += 1

decl_count = collections.Counter(name for _rel, _ln, name, _raw, _ok in members)
candidates: dict[tuple[str, str], list[tuple[int, str]]] = {}
for rel, lineno, name, raw, eligible in members:
    if not eligible or word_count[name] > decl_count[name]:
        continue
    candidates.setdefault((rel, name), []).append((lineno, raw))

expected = {}
for rel, name, reason in ALLOW:
    if not reason.strip():
        errors.append(f"允许清单条目 {rel}::{name} 缺保留理由——登记必须写清为什么保留")
    file = ROOT / rel
    if not file.exists():
        errors.append(f"允许清单指向的文件不存在：{rel}（路径已动须同步本门禁）")
    expected.setdefault((rel, name), 0)
    expected[(rel, name)] += 1

if not expected:
    print("::error::允许清单为空——零引用成员全部判红等于没有判定面，拒绝判 clean")
    sys.exit(1)
if len(candidates) < MIN_CANDIDATES:
    print(f"::error::零引用候选只有 {len(candidates)} 条（下限 {MIN_CANDIDATES}）——扫描面漂移或成员被"
          "批量删除；确认是有意缩减后同步本常量，不要放宽下限")
    sys.exit(1)

for key, sites in sorted(candidates.items()):
    if key in expected:
        continue
    for lineno, raw in sites:
        errors.append(
            f"{key[0]}:{lineno} 零引用成员 `{key[1]}`：{raw[:100]}——"
            "新增零引用成员必须在注释里写明保留理由并登记进本门禁 ALLOW 表，否则视为死代码删除"
        )
for key in sorted(expected):
    if key not in candidates:
        declared = decl_count.get(key[1], 0)
        note = "成员已被删除或改名" if declared == 0 else f"已恢复引用（名字出现 {word_count[key[1]]} 次）"
        errors.append(
            f"{key[0]}  登记项消失：`{key[1]}` 不再是零引用候选（{note}）——"
            "合法保留被顺手删掉、或保留理由已过时；确认后同步本门禁 ALLOW 表"
        )

if errors:
    for e in errors[:40]:
        print("::error::" + e)
    print(f"zero-ref-members gate: FAILED（{len(errors)} 项 / {len(candidates)} 条零引用候选）")
    sys.exit(1)
print(
    f"zero-ref-members gate: clean（{len(members)} 个成员级声明里 {len(candidates)} 条零引用候选全部登记，"
    f"覆盖 {len(expected)} 条 ALLOW 条目）"
)
PY
