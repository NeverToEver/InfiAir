#!/usr/bin/env bash
# 门禁装配完整性检查：门禁脚本、调度注册、完成标记断言三者必须互相对得上。
# 用法：bash scripts/ci/check_gate_wiring.sh
#
# 为什么需要：门禁自己也会坏，而且坏得比业务代码更安静——这里没有编译期约束，
# 全靠人工记得「加了脚本要同时改三处」。实际踩过的两种形态：
#   1) 新增 scripts/ci/check_xxx.sh 却忘了注册进 gates.py / ci.yml——**该门禁永不执行**，
#      本地与 CI 全绿，判据形同不存在。这是最坏的一种：没有报错、没有日志、没有任何信号。
#   2) 在 check_smoke.sh 里加了新趟次却没有配 expect_marker——该趟只判「退出码 0 + 日志无错误」，
#      而冒烟的设计前提恰恰是「只判不崩不算覆盖」（事件中途停摆同样零错误退出）；
#      漏一条断言就把该趟降级成「不崩即过」。
#
# 判定（任一条取不到判据即红——AGENTS §6 铁律 2）：
#   a) scripts/ci/ 下每个门禁脚本（.sh 与 .py，共用模块除外）都注册在 gates.py 的 STEPS 与 ci.yml
#      里（缺失＝永不执行）；共用模块（如 csharp_lex.py）登记在 HELPER_MODULES 并须真被某个门禁
#      import——只认 *.sh 时新增 .py 门禁两个方向都判不出（未注册判不出、注册了反被误报引用不存在）；
#   b) gates.py 的 STEPS 与 ci.yml 的调用**顺序逐条一致**（集合相等抓不到重排：本地排在冒烟
#      之后的静态门禁，CI 在构建前就红——同一份改动本地与 CI 的失败暴露面不一致，排查顺序错位）；
#      另判 gates.py / ci.yml 登记的每个脚本都真实存在（不存在＝CI 在调用处才报错，本地可能不跑）；
#      再判 gates.py 的 slug 不重名——`--only <slug>` 是跑单步的唯一入口，重名会静默跑错一步；
#   b2) 跑门禁的 CI 步骤不得带容错属性（continue-on-error / if / working-directory）：加了
#      `continue-on-error: true` 或 `if: false` 即得 CI 假绿，而本地 gates.py 仍红——正是本门禁
#      声称要消除的「本地绿与 CI 绿含义分叉」；按 YAML 步骤块解析，判据只落在步骤级键上
#      （`run:` 的 shell 正文里出现 `if` 不算）；
#   c) check_smoke.sh 的每条 run_case 紧随一条 expect_marker（漏断言＝该趟降级为不崩即过），
#      且 run_case 总数不低于 MIN_SMOKE_CASES、每趟帧数参数为正整数——整趟的 run_case 与
#      expect_marker 两行一起删时配对性仍成立，只有趟数下限能抓「覆盖被整趟删掉」；
#      两条语句还必须是趟函数体里的**顶层语句**：只判「语句文本相邻」时，把 expect_marker 包进
#      `if [ -n "${X:-}" ]; then … fi`（或 for/while/case 体）后计数、配对、绑定三项全过，而断言
#      永不执行——该趟静默降级为「不崩即过」，正是本门禁声称要抓的形态。判定按函数体的最小
#      缩进基线：语句缩进深于基线，或同行的语句头不是 run_case/expect_marker 本身，即判非顶层；
#      断言还须与趟次真绑定：expect_marker 的日志实参 = 紧邻 run_case 的日志实参、每个日志
#      token 恰好被一条断言引用、标记的 `[标签]` 与该趟开关/场景派生的一致——否则「把断言改成
#      另一趟的日志 + 另一趟的标记」静态判定全过，真实标记从此无人判（并行分批时另一趟的日志
#      早已写好，运行期也可能照绿）；
#   d) 每条 expect_marker 的标记字符串都能对应到 csharp/ 里的**打印点**——字面命中，或匹配某条
#      含格式占位的打印模板（`[event-probe] %s 全周期完成` 这类：id 由运行期代入，字面量里查不到）。
#      字符串写错则断言永不可能通过，而现场表现只是「一条永远红的门禁」，容易被顺手删掉；
#   e) 构建期配置声明与判据不脱钩：Directory.Build.props 的 TreatWarningsAsErrors 必须为 true
#      （否则「零警告」门禁静默降级为「零 error」）、各 csproj 不得用 false 覆盖；InfiAir.sln 里
#      主工程的 Release|Any CPU 不得映射到 Debug（否则 `dotnet build -c Release` 静默产出带
#      DEBUG/TOOLS 的主程序集，探针类编进正式产物）；
#   e2) export_presets.cfg 的 include/exclude 过滤项都指向仓库内真实存在的路径（含通配项须匹配到
#      至少一个文件）——写错或漏跟改名时该条排除静默失效，而导出照常成功、日志干净。构建产物
#      目录（obj/bin）跳过：干净检出里本就不存在；
#   e3) 上一条只有**正向**判据（列出的路径存在），反向「该排除的都排除了」无人判：新增一个
#      csharp/godot/ProbeHost.Foo.cs 或 csharp/tests 下的新测试文件而不改 exclude 时，它们静默
#      进发布包（AGENTS §5：测试设施不进生产路径），且导出照常成功、日志干净。故按 PACKAGE_RULES
#      逐条从磁盘枚举发布包候选文件，要求每条都被某条 exclude 过滤项覆盖；
#   e4) ci.yml 的引擎钉版：GODOT_VERSION 取自 env，缓存命中时直接软链、`godot --version` 只打印
#      不校验——缓存污染或镜像换版会让全部门禁跑到别的引擎上（判据全变味而全绿）。故要求
#      安装步骤与缓存命中步骤**都**断言版本，且断言与 env.GODOT_VERSION 同源（比较 $GODOT_VERSION
#      而非第二份字面量）；版本字面量不得散落在 run 正文里（改 env 却漏改字面量＝同一事实两处维护，
#      AGENTS §1 单源）；
#   f) regenerate_all.sh 的生成器覆盖：素材门禁判的是「重跑后无漂移」，漏跑一个生成器同样无漂移
#      （那份产物根本没被重写）＝覆盖静默缩小。scripts/tools 下每个 .py 要么被 regenerate_all
#      调用、要么登记进 TOOLS_IGNORE（独立工具，逐条写理由）、要么是被调用生成器 import 的共享
#      模块——新生成器忘挂进 regenerate_all 即红。
#   g) 活跃文档的体积预算（AGENTS §6 铁律 4）：`AGENTS.md` 每轮注入上下文、`ROADMAP.md` 是每次
#      定位方向的第一读，膨胀都是**静默成本**——没有报错、没有日志，只是每轮多付一点，直到读的
#      人开始整段跳过。超预算即红，使「再加一批内容」必须同时是「先删或先归档」或「显式抬高预算」
#      的有意识决定（改数会出现在 diff 里）；另判下限：文件被清空 / 截断时只判上限会平凡通过。
#   h) 门禁命中台账（`docs/GATE_LEDGER.md`）与 gates.py 的步骤登记集一一对应：新增门禁不带一行
#      = 未来提议退役时它没有可查的依据面；台账里的行指向不存在的门禁 = 改名/删除后台账未同步，
#      退役决策会基于不存在的判据。台账的「最近命中有没有记」是人工维护的，本门禁只判行集完整。
# 「取不到判据」防线：脚本集为空、gates.py 登记集为空、ci.yml 调用集为空、CI 步骤解析不出、
# 趟次为 0、帧数非正整数、配置项找不到、发布包候选枚举为空、生成器集合为空、版本断言解析不出、
# 台账解析不出任何行，一律红（AGENTS §6 铁律 2：取不到判据必须显式失败）。
#
# 本门禁是**元门禁**（judge 门禁自身），不判业务行为，故不产生质量信号；
# 但它判的是「判据是否存在」，静默错误的代价与质量门禁同级，故计入质量门禁表。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

python3 - <<'PY'
import pathlib
import re
import sys

ROOT = pathlib.Path.cwd()
CI_DIR = ROOT / "scripts" / "ci"
GATES = CI_DIR / "gates.py"
CI_YML = ROOT / ".github" / "workflows" / "ci.yml"
SMOKE = CI_DIR / "check_smoke.sh"
CSHARP = ROOT / "csharp"
TOOLS_DIR = ROOT / "scripts" / "tools"

# scripts/tools 下的非生成器脚本（豁免 regenerate_all.sh 覆盖判定）：须在实际形态变化时同步。
# 共享模块（被生成器 import，如 sprite_polish.py）不在此登记——只要真被某个被调用的生成器
# import 即自动豁免；这张表只收「独立工具」，不收「漏挂的生成器」。
TOOLS_IGNORE = {
    "balance_editor.py": "本机数值编辑器（起本地服务手动改 balance.json），不是素材生成器",
    "shot_ab.py": "引擎升级的同机 A/B 截图比对工具（需两个引擎，人工流程），不是素材生成器",
}

errors: list[str] = []
MISSING = object()
# 活跃文档的体积预算（AGENTS §6 铁律 4）：`AGENTS.md` 每轮注入上下文，`ROADMAP.md` 是每次定位
# 方向的第一读——两者的膨胀都是**静默成本**（没有报错、没有日志，只是每轮/每次多付一点）。
# 预算只比当前实测略高，取「再加一批内容就会越线」的量级：越线时先删或先归档，或显式抬高本值
# （改数会出现在 diff 里，是一次决定而非漂移）。
SIZE_BUDGETS = (
    (ROOT / "AGENTS.md", 11000, "该文件每轮注入上下文：先删或先精简再加"),
    (ROOT / "docs" / "ROADMAP.md", 28000,
     "通读成本：按 Maintenance 把最旧的决策原文搬进 docs/DECISIONS_ARCHIVE.md"),
)
SIZE_FLOOR_RATIO = 3   # 低于预算的 1/3 视为「文件被清空 / 截断」（只判上限时这种形态会平凡通过）
# 门禁命中台账：与 gates.py 的步骤登记集一一对应（缺行 = 新门禁没有可查的退役依据）
LEDGER = ROOT / "docs" / "GATE_LEDGER.md"
# 结论行要报的计数（在各自的判定段里填充；取不到判据时对应的错误已另报，结论行只在零错误时打印）
package_candidates = 0                # e3：发布包候选文件数（被 exclude 覆盖的那些）
generator_called: set[str] = set()    # f：regenerate_all.sh 实际调用的生成器
size_report: list[str] = []           # g：各活跃文档的体积读数
ledger_rows: set[str] = set()         # h：台账里登记的门禁


def read(path: pathlib.Path):
    if not path.exists():
        errors.append(f"{path.relative_to(ROOT).as_posix()} 不存在——取不到判据，拒绝判 clean")
        return None
    return path.read_text(encoding="utf-8", errors="replace")


# C# 字符串字面量（普通/逐字/原始）与行注释。剥壳只求「注释里的标记不算打印点」，
# 不做完整词法分析：逐字符状态机跳过字符串内部，遇到 // 即丢弃到行尾。
CSHARP_STRING = re.compile(r'@""(?:[^"]|"")*"|"""[\s\S]*?"""|"(?:\\.|[^"\\])*"', re.S)


def strip_comments(src: str) -> str:
    """剥掉行注释与块注释（字符串字面量内部的 // 不动）。"""
    out = []
    i = 0
    n = len(src)
    while i < n:
        ch = src[i]
        if ch == '"':
            m = CSHARP_STRING.match(src, i)
            if m:
                out.append(m.group(0))
                i = m.end()
                continue
            out.append(ch)
            i += 1
            continue
        if ch == "/" and i + 1 < n and src[i + 1] == "/":
            nl = src.find("\n", i)
            i = n if nl < 0 else nl
            continue
        if ch == "/" and i + 1 < n and src[i + 1] == "*":
            end = src.find("*/", i + 2)
            i = n if end < 0 else end + 2
            continue
        out.append(ch)
        i += 1
    return "".join(out)


PRINT_CALL = re.compile(
    r"\b(?:GD\.Print|GD\.PushError|GD\.PushWarning|GdFormat\.Format)\s*\(([\s\S]*?)\)\s*[,;)]"
)


def print_literals(src: str) -> list[str]:
    """打印调用实参里的字符串字面量（剥注释后的源码）。只认这些调用，避免把任意文案
    字符串（如 HUD 标签、日志前缀之外的说明）当成完成标记的打印点。"""
    lits = []
    for m in PRINT_CALL.finditer(src):
        for lm in re.finditer(r'"((?:[^"\\]|\\.)*)"', m.group(1)):
            lits.append(lm.group(1))
    return lits


gates_text = read(GATES)
yml_text = read(CI_YML)
smoke_text = read(SMOKE)
if gates_text is None or yml_text is None:
    print("\n".join(errors))
    sys.exit(1)

# ---------- a/b) 脚本 ↔ 注册（双向） ----------

# 发现面含 .sh 与 .py：只认 *.sh 时新增的 .py 门禁两个方向都判不出——未注册则四处检查全落空，
# 注册了反而被误判成「引用不存在的脚本」。gates.py 是调度器自身，不是被它调度的门禁。
GATE_EXTS = (".sh", ".py")
on_disk_all = {p.name for p in CI_DIR.iterdir() if p.is_file() and p.suffix in GATE_EXTS}
on_disk = on_disk_all - {GATES.name}
# 共用模块（不是门禁步骤）：不要求注册进 STEPS/ci.yml，但必须真被某个门禁 import——
# 「既不注册又没人引用」才是要抓的「永不执行」形态；只登记不引用＝孤儿模块，同样报红。
HELPER_MODULES = {
    "csharp_lex.py": "C# 注释/字符串掩码剥离与条件编译段定位；check_balance_keys / check_balance_dead_keys / "
                     "check_code_defaults / check_save_symmetry / check_settings_symmetry / "
                     "check_zero_ref_members 共用",
}
if not on_disk:
    errors.append(f"{CI_DIR.relative_to(ROOT).as_posix()} 下未发现任何门禁脚本——路径漂移？取不到判据，拒绝判 clean")
    sys.exit(1)

# 顺序口径必须含 `dotnet build` 这一步：它没有 .sh 名字，只比脚本序列会漏掉「把静态门禁挪到
# 构建之后」这种重排——而本门禁要判的正是「本地与 CI 的失败暴露顺序是否分叉」。故两处都提取
# 成同一种 token（脚本名 / `dotnet build`），再逐位比对。
DOTNET = "dotnet build"
# gates.py：每个 STEPS 条目一行，取其 kind 与 script
gates_order = []
for line in gates_text.splitlines():
    if not line.strip().startswith('{"slug"'):
        continue
    kind = re.search(r'"kind"\s*:\s*"([^"]+)"', line)
    script = re.search(r'"script"\s*:\s*"([^"]*)"', line)
    if not kind or not script:
        continue
    gates_order.append(DOTNET if kind.group(1) == "dotnet" else script.group(1))
# ci.yml：脚本调用可能落在 `run: |` 多行块里，故按**字符位置**排序取实际先后
CALL_RE = re.compile(r"scripts/ci/([A-Za-z0-9_]+\.(?:sh|py))")
occurrences = [(m.start(), m.group(1)) for m in CALL_RE.finditer(yml_text)]
occurrences += [(m.start(), DOTNET) for m in re.finditer(r"dotnet build --nologo", yml_text)]
yml_order = [token for _pos, token in sorted(occurrences)]
in_gates = {t for t in gates_order if t != DOTNET}
in_yml = {t for t in yml_order if t != DOTNET}

# 本门禁自己不在 gates.py 的 "script" 形态里被别的步骤引用，但必须被登记（自身也参与断言）
for name in sorted(on_disk - set(HELPER_MODULES)):
    if name not in in_gates:
        errors.append(f"{name} 未注册进 scripts/ci/gates.py 的 STEPS——该门禁永不执行（假绿最坏形态）")
    if name not in in_yml:
        errors.append(f"{name} 未注册进 .github/workflows/ci.yml——CI 不跑该门禁，本地绿与 CI 绿含义不一致")
for name in sorted(in_gates | in_yml):
    if name not in on_disk:
        errors.append(f"注册表引用了不存在的脚本 {name}——路径写错或脚本被删（调用处才报错）")
for helper, reason in sorted(HELPER_MODULES.items()):
    if helper not in on_disk_all:
        errors.append(f"登记的共用模块 {helper} 不存在（{reason}）——引用它的门禁会 import 失败，门禁需同步")
        continue
    stem = helper.rsplit(".", 1)[0]
    import_re = re.compile(rf"\bimport\s+{re.escape(stem)}\b|from\s+{re.escape(stem)}\s+import\b")
    corpus = [
        p.read_text(encoding="utf-8", errors="replace")
        for p in sorted(CI_DIR.iterdir())
        if p.is_file() and p.suffix in GATE_EXTS
    ]
    if not any(import_re.search(t) for t in corpus):
        errors.append(f"共用模块 {helper} 没有任何门禁 import（{reason}）——孤儿模块："
                      "要么删掉，要么它其实是被调度的门禁步骤，须注册进 STEPS 与 ci.yml")

if not in_gates:
    errors.append("gates.py 未解析出任何登记的 .sh——STEPS 结构漂移？取不到判据，拒绝判 clean")
if not in_yml:
    errors.append("ci.yml 未解析出任何 scripts/ci/*.sh 调用——路径漂移？取不到判据，拒绝判 clean")

# 顺序逐条一致：集合相等抓不到重排（本地与 CI 的失败暴露顺序分叉，排查切入点就错了）。
if in_gates and in_yml and gates_order != yml_order:
    for i in range(max(len(gates_order), len(yml_order))):
        g = gates_order[i] if i < len(gates_order) else "<无>"
        y = yml_order[i] if i < len(yml_order) else "<无>"
        if g != y:
            errors.append(
                f"第 {i + 1} 步顺序不一致：gates.py 是 {g}、ci.yml 是 {y}"
                "——同一份改动在本地与 CI 的失败暴露顺序分叉（后置的静态门禁会被构建与冒烟挡住），"
                "两处须同步重排"
            )
            break

# slug 唯一：`--only <slug>` 按 slug 匹配出唯一的步，重名时静默跑到先匹配的那一步
slugs = re.findall(r'"slug"\s*:\s*"([^"]+)"', gates_text)
if not slugs:
    errors.append("gates.py 未解析出任何 slug——取不到判据，拒绝判 clean")
dupes = sorted({s for s in slugs if slugs.count(s) > 1})
for s in dupes:
    errors.append(f"gates.py 的 slug「{s}」重名——`--only {s}` 会静默只跑到先匹配的那一步")

# ---------- b2) 跑门禁的 CI 步骤不得带容错属性 ----------
# 为什么：给任一步加 `continue-on-error: true`（或 `if: false`、`working-directory:`）即得 CI 假绿，
# 而本地 gates.py 仍会红——同一份改动「本地红、CI 绿」，正是本门禁要消除的分叉。只按步骤级 YAML
# 键判：`run: |` 的 shell 正文里出现 `if`（bash 条件）不算，故必须先切成步骤块、再看键缩进。

def yaml_steps(text: str):
    """把 ci.yml 的 steps 切成 [(起始行号, 块内各行)]。只认「steps: 下同缩进的 `- ` 列表项」这一层
    结构——stdlib 无 YAML 解析器，而这里只需要步骤边界与步骤级键。"""
    lines = text.split("\n")
    start = next((i for i, line in enumerate(lines) if re.fullmatch(r"\s*steps:\s*", line)), None)
    if start is None:
        return None
    base = len(lines[start]) - len(lines[start].lstrip())
    end = start + 1
    while end < len(lines):
        line = lines[end]
        if line.strip() and (len(line) - len(line.lstrip())) <= base:
            break
        end += 1
    body = lines[start + 1:end]
    item_indent = next((len(m.group(1)) for m in (re.match(r"^(\s*)-\s", l) for l in body) if m), None)
    if item_indent is None:
        return []
    items = []
    head, block = None, []
    for offset, line in enumerate(body):
        m = re.match(r"^(\s*)-\s", line)
        if m and len(m.group(1)) == item_indent:
            if head is not None:
                items.append((start + 1 + head, block))
            head, block = offset, [line]
        elif head is not None:
            block.append(line)
    if head is not None:
        items.append((start + 1 + head, block))
    return items


def step_keys_and_run(block):
    """返回步骤的 (步骤级键集合, run 正文)。键缩进取块内首个映射行：比它更深的是 `run: |` 正文。"""
    indents = [len(l) - len(l.lstrip()) for l in block[1:] if re.match(r"^\s*[A-Za-z][\w-]*:", l)]
    keys, run_lines, in_run = set(), [], False
    if not indents:
        return keys, ""
    key_indent = min(indents)
    for line in block:
        m = re.match(r"^(\s*)([A-Za-z][\w-]*):(.*)$", line)
        if m and len(m.group(1)) == key_indent:
            keys.add(m.group(2))
            in_run = m.group(2) == "run"
            run_lines.append(m.group(3))
            continue
        if in_run:
            run_lines.append(line)
    return keys, "\n".join(run_lines)


# 只对「跑门禁的步骤」判：run 正文里出现门禁脚本调用或 dotnet build。安装/缓存/上传步骤允许
# `if:`（缓存命中分支、失败时上传日志）——它们不是判据本身。
FORBIDDEN_STEP_KEYS = ("continue-on-error", "if", "working-directory")
GATE_RUN_MARKERS = ("scripts/ci/", DOTNET)
ci_steps = yaml_steps(yml_text)
if not ci_steps:
    errors.append("ci.yml 未解析出任何步骤块（steps 结构漂移？）——取不到容错属性判据，拒绝判 clean")
else:
    declared = len(re.findall(r"^\s*- (?:name|uses):", yml_text, re.M))
    if declared != len(ci_steps):
        errors.append(f"ci.yml 步骤解析数不符：`- name/uses` 有 {declared} 条、步骤块解析出 {len(ci_steps)} 个"
                      "——解析器漂移会让部分步骤不被判，取不到判据")
    gate_steps = 0
    for lineno, block in ci_steps:
        keys, run_text = step_keys_and_run(block)
        if not any(marker in run_text for marker in GATE_RUN_MARKERS):
            continue
        gate_steps += 1
        for key in FORBIDDEN_STEP_KEYS:
            if key in keys:
                errors.append(
                    f"ci.yml 第 {lineno} 行的门禁步骤带 `{key}:`（{block[0].strip()}）——"
                    "容错属性让 CI 该步失败不判红：本地 gates.py 红而 CI 绿，门禁含义分叉（假绿）"
                )
    if gate_steps == 0:
        errors.append("ci.yml 里没有任何步骤在跑门禁（scripts/ci/ 或 dotnet build）——"
                      "路径或命令写法漂移？取不到判据，拒绝判 clean")

# ---------- e) 构建期配置声明与判据不脱钩 ----------
# 判据：AGENTS §6 门禁表写「`dotnet build` 零警告（TreatWarningsAsErrors）」，而这条声明在此前
# 没有任何断言——把 TreatWarningsAsErrors 改成 false、或让主工程 Release 映射到 Debug，构建门禁
# 就静默降级（只把 error 当失败 / 产出带 DEBUG、TOOLS 的主程序集，探针类编进正式产物）。

props = read(ROOT / "Directory.Build.props")
if props is not None:
    m = re.search(r"<TreatWarningsAsErrors>\s*([^<\s]+)\s*</TreatWarningsAsErrors>", props)
    if not m:
        errors.append("Directory.Build.props 未声明 TreatWarningsAsErrors——「dotnet build 零警告」"
                      "取不到判据（缺声明时 MSBuild 默认 false，门禁静默降级为「零 error」）")
    elif m.group(1).lower() != "true":
        errors.append(f"Directory.Build.props 的 TreatWarningsAsErrors={m.group(1)}——"
                      "构建门禁从「零警告」静默降级为「零 error」，与 AGENTS §6 门禁表的声明不符")
csprojs = sorted(p for p in ROOT.rglob("*.csproj") if not any(x in {"obj", "bin"} for x in p.parts))
if not csprojs:
    errors.append("仓库里找不到任何 .csproj——路径漂移？取不到构建配置判据，拒绝判 clean")
for csproj in csprojs:
    csproj_text = read(csproj)
    if csproj_text is None:
        continue
    for m in re.finditer(r"<TreatWarningsAsErrors>\s*([^<\s]+)\s*</TreatWarningsAsErrors>", csproj_text):
        if m.group(1).lower() != "true":
            errors.append(f"{csproj.relative_to(ROOT).as_posix()} 覆盖 TreatWarningsAsErrors={m.group(1)}"
                          "——单个工程关掉零警告即让构建门禁在该工程上静默失效")

sln = read(ROOT / "InfiAir.sln")
if sln is not None:
    projects = re.findall(r'Project\("\{[^"]+\}"\)\s*=\s*"([^"]*)",\s*"([^"]*)",\s*"\{([^}]+)\}"', sln)
    main = [guid for _name, path, guid in projects if path.replace("\\", "/") == "InfiAir.csproj"]
    if len(main) != 1:
        errors.append(f"InfiAir.sln 里主工程 InfiAir.csproj 的条目解析出 {len(main)} 条（期望 1 条）"
                      "——取不到 Release 映射判据，拒绝判 clean")
    else:
        guid = main[0]
        for label, pattern in (("ActiveCfg", r"\.ActiveCfg\s*=\s*([^\r\n]+)"),
                               ("Build.0", r"\.Build\.0\s*=\s*([^\r\n]+)")):
            m = re.search(rf"\{{{guid}\}}\.Release\|Any CPU{pattern}", sln)
            if m is None:
                errors.append(f"InfiAir.sln 找不到主工程 Release|Any CPU 的 {label} 映射"
                              "——取不到判据，拒绝判 clean（缺映射时该配置不构建该工程）")
                continue
            value = m.group(1).strip()
            if "Debug" in value:
                errors.append(
                    f"InfiAir.sln 的主工程 Release|Any CPU.{label} = {value}——"
                    "`dotnet build -c Release InfiAir.sln` 会静默产出带 DEBUG/TOOLS 的主程序集"
                    "（#if DEBUG || TOOLS 的探针类编进正式产物），须映射回 Release|Any CPU")
            elif not value.startswith("Release"):
                errors.append(f"InfiAir.sln 的主工程 Release|Any CPU.{label} = {value}——"
                              "配置映射既不是 Release 也不是 Debug，构建口径不可判，门禁需同步")

# ---------- e2) 导出预设的过滤项不得指向不存在的路径 ----------
# 判据：过滤项写错（或改了名没跟着改）时 Godot 不报错——该条排除静默失效，导出照常成功、
# 日志干净，而「探针场景/源码不进发布包」的声明已经名存实亡（本项目实际发生过：
# ProbeHost.Play.cs 改名为 ProbeHost.Autoplay.cs 后过滤器仍写着旧名）。
# 跳过 */obj/* 与 */bin/*：那是构建产物目录，干净检出里本就不存在，断言它们等于制造假红。
presets_text = read(ROOT / "export_presets.cfg")
if presets_text is None:
    errors.append("找不到 export_presets.cfg——取不到导出过滤判据，拒绝判 clean")
else:
    filter_entries = 0
    exclude_entries: list[str] = []
    for lineno, raw in enumerate(presets_text.split("\n"), 1):
        m = re.match(r'\s*(include_filter|exclude_filter|encryption_include_filters|'
                     r'encryption_exclude_filters)\s*=\s*"(.*)"\s*$', raw)
        if not m:
            continue
        if m.group(1) == "exclude_filter":
            exclude_entries += [e.strip() for e in m.group(2).split(",") if e.strip()]
        for entry in (e.strip() for e in m.group(2).split(",")):
            if not entry:
                continue
            if re.search(r"(^|/)(obj|bin)/", entry):
                continue
            filter_entries += 1
            pat = entry[6:] if entry.startswith("res://") else entry
            if any(ch in pat for ch in "*?["):
                if not list(ROOT.glob(pat)):
                    errors.append(
                        f"export_presets.cfg:{lineno} 的过滤项 {entry} 匹配不到任何文件——"
                        "该条排除静默失效（路径写错，或目标已改名/删除）")
            elif not (ROOT / pat).exists():
                errors.append(
                    f"export_presets.cfg:{lineno} 的过滤项 {entry} 指向不存在的路径——"
                    "该条排除静默失效；文件改名或删除时须同步这里")
    if filter_entries == 0:
        errors.append("export_presets.cfg 一条过滤项都没解析出来——格式或路径漂移？"
                      "取不到判据，拒绝判 clean")

    # ---------- e3) 反向：发布包里不得留下测试设施 ----------
    # 上面的正向判据只管「列出的路径存在」，管不到「该排除的漏了」：新增一个
    # csharp/godot/ProbeHost.Foo.cs（该项目刚因分部文件踩过坑）或 csharp/tests 下的新文件而不改
    # exclude 时，它们静默进发布包（AGENTS §5：测试设施不进生产路径），导出照常成功、日志干净。
    # 故按 PACKAGE_RULES 从磁盘枚举候选，逐个要求被某条 exclude 过滤项覆盖。
    # 维护口径：新增测试设施落在别的路径下时，必须在此登记一条规则（规则匹配不到任何文件也判红，
    # 防规则随改名失效后本判定空转）。
    PACKAGE_EXCLUDE_RULES = [
        ("csharp/**/ProbeHost*.cs", "探针宿主源码（含分部文件）：测试设施不得进发布包"),
        ("scenes/probe_host.tscn", "探针宿主场景：测试设施不得进发布包"),
        # 整棵 tests 树（源码 / .uid / csproj）：uid 是 Godot 导入产物、会随工程入包，
        # 只挡 *.cs 会给「测试工程以别的后缀散进发布包」留口子
        ("csharp/tests/**/*", "core 层单测工程整棵（测试设施，不入主 sln）"),
    ]

    def glob_candidates(pattern: str) -> list[str]:
        """枚举候选文件（返回仓库相对路径）。`**` 跨目录层级（pathlib 语义：`*` 不跨 `/`）；
        候选是否被覆盖由 exclude_matcher 按 Godot 过滤项语义判（那里的 `*` 与 `**` 都跨分隔符）。
        路径一律按 relative_to(ROOT) 归一：macOS 上 ROOT 是 /private/tmp/… 而 glob 可能给出
        /tmp/… 别名（/tmp 是符号链接），比对绝对路径字符串会得到假「未覆盖」或假覆盖。
        obj/bin 下的构建产物跳过（与 e2 同口径）：它们不进发布包、干净检出里也不存在，
        本机刚构建过的树上把它们计入候选只会让条数与失败输出变噪。"""
        return sorted(p.relative_to(ROOT).as_posix() for p in ROOT.glob(pattern)
                      if p.is_file() and not any(x in {"obj", "bin"} for x in p.parts))

    def exclude_matcher(pattern: str):
        pat = pattern[6:] if pattern.startswith("res://") else pattern
        return re.compile("^" + re.escape(pat).replace(r"\*\*", ".*").replace(r"\*", ".*") + "$")

    uncovered: list[str] = []
    candidates_seen: set[str] = set()
    for pattern, why in PACKAGE_EXCLUDE_RULES:
        candidates = glob_candidates(pattern)
        if not candidates:
            errors.append(
                f"发布包反向判据的规则 {pattern}（{why}）匹配不到任何文件——路径漂移（改名/搬目录）"
                "会让该条判定静默空转，须同步规则，别删断言")
            continue
        matchers = [exclude_matcher(e) for e in exclude_entries]
        for rel in candidates:
            candidates_seen.add(rel)
            if not any(m.match(rel) for m in matchers):
                uncovered.append(f"{rel}（规则 {pattern}：{why}）")
    package_candidates = len(candidates_seen)
    if not candidates_seen:
        errors.append("发布包反向判据一条候选文件都没枚举到——取不到判据，拒绝判 clean")
    for rel in sorted(set(uncovered)):
        errors.append(
            f"测试设施 {rel} 没有被 export_presets.cfg 的任何 exclude 过滤项覆盖——"
            "它会静默进发布包（AGENTS §5），而导出照常成功、日志干净：须在该文件的 exclude_filter "
            "里补一条（或把目标移出发布包路径）"
        )

# ---------- e4) ci.yml 的引擎钉版必须有断言 ----------
# 判据：GODOT_VERSION 是钉版的单一事实源，但此前缓存命中路径只是软链 + 打印版本——缓存污染
# （同 key 里躺着别的版本）或镜像换版时，整条门禁链跑到另一个引擎上而全绿。故要求两个安装步骤
# 都断言版本、断言与 env 同源、且版本字面量不散落在 run 正文里。
if yml_text is not None:
    ver_m = re.search(r'^\s*GODOT_VERSION\s*:\s*"?([0-9][0-9.]*)"?\s*$', yml_text, re.M)
    if not ver_m:
        errors.append("ci.yml 未声明 env.GODOT_VERSION——取不到引擎钉版判据，拒绝判 clean")
    else:
        expected = ver_m.group(1)
        # 判据按**行**取（不按步骤块）：`case … esac` 落在 `run: |` 正文里，而步骤块解析只用于
        # 容错属性判定——版本断言的存在性与"是否配非零退出"在行层面即可判且不受块边界影响。
        yml_lines = yml_text.split("\n")
        # 断言窗口 6 行：`godot --version` 之后是 v=… / echo / case / 通配分支 / 兜底分支（含 exit 1）；
        # 窗口过长时相邻两次读数会把同一行算两遍，输出里出现重复错误行
        ASSERT_WINDOW = 6
        version_lines = [i for i, line in enumerate(yml_lines, 1) if "godot --version" in line]
        if len(version_lines) < 2:
            errors.append(
                f"ci.yml 只有 {len(version_lines)} 处读 `godot --version`（期望 2 处：缓存未命中的"
                "安装步骤与缓存命中的软链步骤）——两条路径都得走版本断言，缺一处即该路径无判据")
        # 窗口去重：两处读数落在同一断言块内时（同一步骤里 `echo "$(godot --version)"` 之类）
        # 只判一次，避免同一处缺陷打出两条错误行
        checked_until = 0
        for lineno in version_lines:
            if lineno <= checked_until:
                continue
            checked_until = lineno + ASSERT_WINDOW - 1
            window = "\n".join(yml_lines[lineno - 1:lineno - 1 + ASSERT_WINDOW])
            if not re.search(r'case\s+"\$v"\s+in', window):
                errors.append(
                    f"ci.yml:{lineno} 读 `godot --version` 处没有版本断言（其后 {ASSERT_WINDOW} 行内找不到 "
                    "`case \"$v\" in`）——缓存污染或镜像换版会让全部门禁跑到另一个引擎上："
                    "判据全变味而全绿；断言须与 env.GODOT_VERSION 同源")
                continue
            if not re.search(r"\bexit\s+[1-9]", window):
                errors.append(
                    f"ci.yml:{lineno} 的版本断言分支里没有非零退出（其后 {ASSERT_WINDOW} 行内找不到 "
                    "`exit 1`）——不匹配时步骤照样成功，断言形同注释")
        if f"\"{expected}\"" in yml_text:
            errors.append(
                f"ci.yml 里出现版本字面量 \"{expected}\"——引擎版本只写一处（env.GODOT_VERSION），"
                "本门禁自己也不得手抄一份（同一事实两处维护）")
        gate_lines = set()
        if ci_steps:
            for lineno, block in ci_steps:
                _keys, run_text = step_keys_and_run(block)
                if any(marker in run_text for marker in GATE_RUN_MARKERS):
                    gate_lines.update(range(lineno, lineno + len(block)))
        for lineno, line in enumerate(yml_lines, 1):
            if lineno not in gate_lines:
                continue
            for lit in re.findall(r"\b4\.[0-9]+(?:\.[0-9]+)?", re.sub(r"\$GODOT_VERSION", "", line)):
                errors.append(
                    f"ci.yml:{lineno} 的门禁步骤 run 正文里出现版本字面量 {lit}——"
                    "版本以 ${GODOT_VERSION} 代入，写死即同一事实两处维护")

# ---------- f) regenerate_all.sh 的生成器覆盖 ----------
# 判据：素材门禁判「重跑后无漂移」，漏跑一个生成器同样无漂移（那份产物根本没被重写）＝覆盖静默
# 缩小，而门禁照绿。两个方向都判：regenerate_all 调的脚本必须存在；scripts/tools 下的每个 .py
# 要么被调用、要么登记为豁免工具、要么是共享模块（被某个被调用的生成器 import）。
if TOOLS_DIR.is_dir():
    regen_text = read(TOOLS_DIR / "regenerate_all.sh")
    if regen_text is None:
        errors.append("找不到 scripts/tools/regenerate_all.sh——取不到生成器覆盖判据，拒绝判 clean")
    else:
        # 先剥反引号内的文本：注释里提到某个脚本名不算「调用了它」
        regen_code = re.sub(r"`[^`]*`", "", regen_text)
        generator_called = set(re.findall(r"\$SCRIPT_DIR/([A-Za-z0-9_.-]+\.py)", regen_code))
        called = generator_called
        tools_py = {p.name for p in TOOLS_DIR.iterdir() if p.is_file() and p.suffix == ".py"}
        tools_text = {
            p.name: p.read_text(encoding="utf-8", errors="replace")
            for p in TOOLS_DIR.iterdir() if p.is_file() and p.suffix == ".py"
        }
        if not tools_py or not called:
            errors.append(
                f"scripts/tools 有 {len(tools_py)} 个 .py、regenerate_all.sh 调用 {len(called)} 个——"
                "任一侧为空即解析漂移，取不到覆盖判据，拒绝判 clean")
        for name in sorted(called):
            if name not in tools_py:
                errors.append(f"regenerate_all.sh 调用了不存在的 {name}——素材门禁在调用处才报错")
        for name in sorted(tools_py - called):
            if name in TOOLS_IGNORE:
                continue
            if any(re.search(rf"\bimport\s+{re.escape(name[:-3])}\b|"
                             rf"from\s+{re.escape(name[:-3])}\s+import\b", tools_text.get(caller, ""))
                   for caller in sorted(called) if caller in tools_text):
                continue
            errors.append(
                f"scripts/tools/{name} 既没被 regenerate_all.sh 调用，也不在被调用生成器 import 的"
                "共享模块名单里——漏跑一个生成器与「重跑后无漂移」同形：该产物根本没被重写，"
                "覆盖静默缩小而素材门禁照绿；新生成器须同时挂进 regenerate_all.sh，"
                "或登记进本脚本的 TOOLS_IGNORE 并写明理由")

# ---------- c) 冒烟趟次 ↔ 完成标记断言 ----------

if smoke_text is not None:
    # 逐行吃掉反斜杠续行后按语句看顺序：每条 run_case 之后必须紧跟一条 expect_marker。
    # 语句文本用「去首尾空白」形态（续行拼成一条，与下方绑定判据的实参正则同源）；行号取**首行**
    # 的原始行号（拼接后的行号会落到续行上，失败信息指不动位置）。缩进单独留一份 line_indent：
    # 续行让「按下标找原文」错位，故首行缩进在此固化（可达性判定要用）。
    # `if` 之类的控制关键字后必须带空白：`ifx()` 这类函数名不得被误判成包裹
    CMD_PREFIX = re.compile(r"^(?:run_case|expect_marker)\s")
    smoke_lines = smoke_text.split("\n")
    stmts = []
    wrapped_lines: list[int] = []
    pending, pending_line = None, 0
    for lineno, raw in enumerate(smoke_lines, 1):
        stripped = raw.strip()
        if pending is not None:
            head = stripped[:-1].rstrip() if stripped.endswith("\\") else stripped
            pending += " " + head
            if not stripped.endswith("\\"):
                stmts.append(("run_case", pending, pending_line))
                pending = None
            continue
        if stripped.startswith("#") or not stripped:
            continue
        if not CMD_PREFIX.match(stripped) and re.search(r"\b(?:run_case|expect_marker)\s", stripped):
            # 语句不在行首：`if …; then run_case …` / `… && expect_marker …` 这类包裹即断言不可达
            # （行首不是调用本身），报红并指位置，别让它静默落进「未解析」的黑洞
            wrapped_lines.append(lineno)
            continue
        if stripped.startswith("run_case ") or stripped.startswith("expect_marker "):
            if stripped.endswith("\\"):
                pending, pending_line = stripped[:-1].rstrip(), lineno
            else:
                stmts.append(("run_case" if stripped.startswith("run_case ") else "expect_marker",
                              stripped, lineno))
        # 其余命令行（函数定义、条件、赋值、调度）不参与趟次判定

    def stmt_indent(lineno: int) -> int:
        raw = smoke_lines[lineno - 1]
        return len(raw) - len(raw.lstrip())

    for lineno in wrapped_lines:
        errors.append(
            f"{SMOKE.relative_to(ROOT).as_posix()}:{lineno} run_case/expect_marker 不在行首"
            f"（{smoke_lines[lineno - 1].strip()[:60]}）——被 if/for/… 或 && 包裹时"
            "该语句可能永不执行：趟次静默降级为「不崩即过」，而计数、配对与绑定判定全过"
        )

    # 趟函数体（首行行号 → 函数名）：可达性判定的作用域
    case_fns = []
    for m in re.finditer(r"^(smoke_[A-Za-z0-9_]+)\(\)\s*\{", smoke_text, re.M):
        body_end = smoke_text.find("\n}", m.end())
        case_fns.append((m.group(1), smoke_text.count("\n", 0, m.start()) + 1,
                         len(smoke_lines) if body_end < 0 else smoke_text.count("\n", 0, body_end) + 1))

    def fn_of(lineno: int):
        for name, start, end in case_fns:
            if start <= lineno <= end:
                return name
        return None

    fn_min_indent: dict[str, int] = {}
    for _kind, _stmt, lineno in stmts:
        name = fn_of(lineno)
        if name is not None:
            fn_min_indent[name] = min(fn_min_indent.get(name, 1 << 30), stmt_indent(lineno))
    for name, base in sorted(fn_min_indent.items()):
        if base > 2:
            errors.append(
                f"趟函数 {name} 的 run_case/expect_marker 最小缩进是 {base} 空格（顶层应为函数体基线）"
                "——整趟被包进条件/循环块时两条断言永不执行，而这不会让任何静态计数变化"
            )
    for kind, stmt, lineno in stmts:
        name = fn_of(lineno)
        if name is None:
            errors.append(f"{SMOKE.relative_to(ROOT).as_posix()}:{lineno} 的 {kind} 不在任何趟函数体里"
                          "——顶层散语句不参与调度，断言可能永不执行（请放回对应的 smoke_* 函数）")
            continue
        if stmt_indent(lineno) != fn_min_indent.get(name, 0):
            errors.append(
                f"{SMOKE.relative_to(ROOT).as_posix()}:{lineno} 的 {kind} 缩进 "
                f"{stmt_indent(lineno)} 空格、趟函数 {name} 的基线是 {fn_min_indent.get(name, 0)} 空格——"
                "该语句嵌在 if/for/while/case 块里，条件不成立时永不执行：该趟静默降级为"
                "「退出码 0 + 日志无错误」＝只判不崩，而计数、配对与绑定判定全过"
            )

    cases = [s for kind, s, _ln in stmts if kind == "run_case"]
    markers = [s for kind, s, _ln in stmts if kind == "expect_marker"]
    if not cases:
        errors.append("check_smoke.sh 未解析出任何 run_case——脚本结构漂移？取不到判据，拒绝判 clean")
    if not markers:
        errors.append("check_smoke.sh 未解析出任何 expect_marker——取不到判据，拒绝判 clean")
    if len(cases) != len(markers):
        errors.append(
            f"check_smoke.sh 的 run_case 有 {len(cases)} 条、expect_marker 有 {len(markers)} 条——"
            "数目不等即有趟次漏断言（该趟降级为「退出码 0 + 无错误」＝只判不崩）"
        )

    # 趟数下限：配对性判定抓不到「整趟两行一起删」——run_case 与 expect_marker 同删时配对关系
    # 仍成立、条数仍相等，覆盖静默消失而门禁判 clean。取**下限**而非精确值：新增趟是被鼓励的
    # 覆盖增量、不构成静默错误（新趟漏配 expect_marker 由上面的配对性判定抓），把新增也判红
    # 会造成「加覆盖反而红」的反向激励。
    # 维护口径：本常量随 check_smoke.sh 的趟数在**同一个提交**里同步（加一趟就加一、删一趟须在
    # 提交正文写明理由并同时下调）——下限过期即等于没下限：曾把它留在 24 而实际 25 趟，整趟删除
    # 后元门禁照判 clean，正是本行声称要抓的形态。
    MIN_SMOKE_CASES = 26
    if len(cases) < MIN_SMOKE_CASES:
        errors.append(
            f"{SMOKE.relative_to(ROOT).as_posix()} 只解析出 {len(cases)} 趟 run_case（下限 "
            f"{MIN_SMOKE_CASES}）——整趟的 run_case 与 expect_marker 一起删时配对性判定仍成立，"
            "门禁会静默判 clean；确认是有意缩减覆盖后同步本常量，不要放宽下限"
        )

    # ---------- c2) 调度数组 SMOKE_CASES ↔ 趟函数 ----------
    # 上一节的计数只数「语句文本」，而真正决定哪些趟被执行的是末尾的 SMOKE_CASES 数组。
    # 保留 run_case/expect_marker 两行、只从数组里删一行时：语句计数不变、配对性成立、下限守卫
    # 也落空，而该趟根本不执行——门禁自报的趟数与实际执行趟数静默分叉（元门禁自己判 clean）。
    defined_case_fns = set()
    for m in re.finditer(r"^(smoke_[A-Za-z0-9_]+)\(\)\s*\{", smoke_text, re.M):
        defined_case_fns.add(m.group(1))

    # 数组正文取到**配平的那一个**右括号为止：既支持末尾一项换行收尾的形态，也支持
    # `SMOKE_CASES=(smoke_a smoke_b)` 这类单行写法——只认「行首 )」会把后者判成「取不到判据」
    # 而红（假红：数组明明在、趟数也对），推动的却是把写法改回多行，而不是修真问题。注释先剥离，
    # 否则 `smoke_old # 已并入 smoke_b)` 这类括号会带偏配平计数。
    arr_m = re.search(r"SMOKE_CASES=\(", smoke_text)
    if not arr_m:
        errors.append("check_smoke.sh 未找到 SMOKE_CASES=(...) 调度数组——取不到判据，拒绝判 clean")
    else:
        body: list[str] = []
        depth, pos = 1, arr_m.end()
        while pos < len(smoke_text) and depth > 0:
            ch = smoke_text[pos]
            if ch == "(":
                depth += 1
            elif ch == ")":
                depth -= 1
                if depth == 0:
                    break
            body.append(ch)
            pos += 1
        arr_body = re.sub(r"(?m)#.*$", "", "".join(body))
        smoke_cases = [tok for tok in arr_body.split() if tok]
        if depth > 0:
            errors.append("SMOKE_CASES=(...) 的右括号不配平——数组解析取不到判据，拒绝判 clean")
        if not smoke_cases:
            errors.append("SMOKE_CASES 为空——没有任何趟会被执行，拒绝判 clean")
        for name in smoke_cases:
            if name not in defined_case_fns:
                errors.append(f"SMOKE_CASES 引用了未定义的趟函数 {name}（调度即失败或空跑）")
        # 含 run_case 的趟函数必须全部在调度数组里——漏一个就等于整趟覆盖静默消失
        for name in sorted(defined_case_fns):
            body = re.search(rf"^{name}\(\)\s*\{{(.*?)^\}}", smoke_text, re.M | re.S)
            if body and "run_case " in body.group(1) and name not in smoke_cases:
                errors.append(
                    f"趟函数 {name} 定义了 run_case 却不在 SMOKE_CASES 里——该趟永不执行，"
                    "而语句计数、配对性与下限守卫都仍成立，正是「整趟覆盖静默消失」的漏判形态"
                )
        if len(smoke_cases) != len(cases):
            errors.append(
                f"SMOKE_CASES 有 {len(smoke_cases)} 项、run_case 语句有 {len(cases)} 条——"
                "两者分叉即有趟次被定义却未调度（或调度了未定义项）"
            )

    # 每趟的日志路径与用户目录（run_case 第 3/5 实参）此前不参与任何判定：复制粘贴漏改时
    # 两趟共写同一日志、共用同一用户目录——并行下完成标记可能来自另一趟，且破坏 check_smoke.sh
    # 自陈的「每趟各自临时用户目录、先清空重建」隔离不变式（AGENTS §5 不依赖外部残留状态）。
    arg_re = re.compile(r'"((?:[^"\\]|\\.)*)"|(\S+)')
    seen_log: dict[str, str] = {}
    seen_userdir: dict[str, str] = {}
    for stmt in cases:
        toks = [q if q else b for q, b in arg_re.findall(stmt)]
        # run_case "label" frames log scene userdir [开关...]
        if len(toks) < 6:
            errors.append(f"run_case 实参不足 6 项（label/frames/log/scene/userdir）：{stmt[:80]}")
            continue
        label, log_tok, userdir_tok = toks[1], toks[3], toks[5]
        if not userdir_tok:
            errors.append(
                f"趟次「{label}」未给用户目录（第 5 实参为空）——不隔离的趟会读走开发者配置、"
                "写坏开发者当前存档，且同目录重跑会读到上一趟的残留（AGENTS §5）"
            )
        else:
            if userdir_tok in seen_userdir:
                errors.append(
                    f"趟次「{label}」与「{seen_userdir[userdir_tok]}」共用用户目录 {userdir_tok}"
                    "——破坏各趟隔离（AGENTS §5 不依赖外部残留状态）"
                )
            else:
                seen_userdir[userdir_tok] = label
        if log_tok in seen_log:
            errors.append(
                f"趟次「{label}」与「{seen_log[log_tok]}」共用日志 {log_tok}"
                "——并行下两进程写同一文件，完成标记可能来自另一趟"
            )
        else:
            seen_log[log_tok] = label

    # 逐条配对：run_case 语句之后紧邻的语句必须是 expect_marker。帧数参数另判正整数：
    # 帧数被掏空（空串或非数字）时该趟只跑 0 帧，完成标记必然缺失，却可能因退出码 0 而静默过。
    for i, (kind, stmt, lineno) in enumerate(stmts):
        if kind != "run_case":
            continue
        head = re.match(r'^run_case\s+"([^"]*)"\s+(\S+)', stmt)
        frames_tok = head.group(2) if head else ""
        if not re.fullmatch(r"\d+", frames_tok) or int(frames_tok) <= 0:
            errors.append(
                f"{SMOKE.relative_to(ROOT).as_posix()}:{lineno} 趟次「"
                f"{head.group(1) if head else stmt[:60]}」的帧数参数不是正整数"
                f"（帧数位取到 `{frames_tok}`，可能是帧数被掏空或参数顺序变了）"
                "——帧数被掏空时该趟只跑 0 帧，只判「不崩」抓不到"
            )
        nxt = stmts[i + 1] if i + 1 < len(stmts) else None
        if nxt is None or nxt[0] != "expect_marker":
            label = re.search(r'"([^"]*)"', stmt)
            errors.append(
                f"check_smoke.sh 的趟次「{label.group(1) if label else stmt[:60]}」后面没有紧跟 expect_marker"
                "——该趟只判退出码与日志错误，事件中途停摆同样是零错误退出"
            )

    # 断言与趟次必须**真绑定**：此前只判「紧跟其后」——把某趟的 expect_marker 改成另一趟的日志 +
    # 另一趟的标记字符串，静态判定全过（那串标记在 csharp/ 里确有打印点），而本趟自己的真实标记
    # 从此无人判；并行分批时另一趟的日志早已写好，运行期也可能照绿。绑定判据四条：
    #   (1) expect_marker 的日志实参 = 紧邻 run_case 的日志实参；
    #   (2) 每个日志 token 恰好被一条断言引用（引用错趟时必然出现 0 引用与 2 引用）；
    #   (3) 趟次命名一致：日志与用户目录同 stem（$PROBE_LOG_BASE.<stem>.log / .userdata）；
    #   (4) 标记的 `[标签]` 由该趟开关派生（`--xxx-probe` → `[xxx-probe]`），开关取值的 id 必须
    #       出现在标记里——标签与趟次不同源时，断言可能判到别的探针的打印点。
    MARKER_TAG_ALIAS = {
        "event-probe-death": "event-probe",     # 死亡趟复用 event-probe 的打印点
        "event-probe-killall": "event-probe",   # 击杀趟的收尾标记同样打在 event-probe 标签下
        "practice-probe": "practice",           # 落位标记打在练习宿主（生产节点）身上，探针宿主随切场景释放
    }
    # 无探针开关的趟（跑生产场景）：标记标签无法从开关派生，逐条登记（未登记即红，逼出决策）
    NO_SWITCH_MARKER_TAGS = {
        "": ("boot", "生产 main.tscn 直跑：入口标记是开机交接打出的 [boot]"),
        "res://scenes/tutorial.tscn": ("tutorial", "教程独立场景：标记在 Tutorial._Ready 末尾打印"),
    }
    LOG_STEM = re.compile(r"^\$\{?PROBE_LOG_BASE\}?\.([A-Za-z0-9_]+)\.log$")
    USERDIR_STEM = re.compile(r"^\$\{?PROBE_LOG_BASE\}?\.([A-Za-z0-9_]+)\.userdata$")
    seen_label: dict[str, int] = {}
    marker_log_counts: dict[str, int] = {}
    for stmt in markers:
        toks = [q if q else b for q, b in arg_re.findall(stmt)]
        if len(toks) >= 3:
            marker_log_counts[toks[2]] = marker_log_counts.get(toks[2], 0) + 1
    for stmt in cases:
        toks = [q if q else b for q, b in arg_re.findall(stmt)]
        if len(toks) >= 6:
            marker_log_counts.setdefault(toks[3], 0)
    for log_tok, count in sorted(marker_log_counts.items()):
        if count > 1:
            errors.append(f"{count} 条 expect_marker 都引用日志 {log_tok}——同一份日志被断言多次时，"
                          "必然有趟次的真实标记无人判（断言与趟次没有一一对应）")
        elif count == 0:
            errors.append(f"日志 {log_tok} 没有任何 expect_marker 引用——该趟的完成标记无人判"
                          "（断言被改指到别趟日志时就是这一形态）")

    for i, (kind, stmt, lineno) in enumerate(stmts):
        if kind != "run_case":
            continue
        nxt = stmts[i + 1] if i + 1 < len(stmts) else None
        if nxt is None or nxt[0] != "expect_marker":
            continue                                    # 配对缺失已在上面报过
        case_toks = [q if q else b for q, b in arg_re.findall(stmt)]
        mark_toks = [q if q else b for q, b in arg_re.findall(nxt[1])]
        if len(case_toks) < 6 or len(mark_toks) < 4:
            errors.append(f"第 {lineno} 行的 run_case / expect_marker 实参不足，取不到绑定判据：{stmt[:60]}")
            continue
        label, case_log, scene, userdir = case_toks[1], case_toks[3], case_toks[4], case_toks[5]
        mark_log, marker = mark_toks[2], mark_toks[3]
        if label in seen_label:
            errors.append(f"第 {lineno} 行趟次标签「{label}」与第 {seen_label[label]} 行重复"
                          "——标签是失败输出的定位依据，复制粘贴漏改时两趟的失败无法区分")
        seen_label[label] = lineno
        if mark_log != case_log:
            errors.append(
                f"{SMOKE.relative_to(ROOT).as_posix()}:{lineno} 趟次「{label}」的断言抓的是 {mark_log}、"
                f"该趟写的是 {case_log}——断言与趟次不同源：判的是另一趟的完成标记，本趟的真实标记"
                "无人判（并行分批时另一趟的日志早已写好，运行期也可能照绿）")
        log_stem = LOG_STEM.match(case_log)
        userdir_stem = USERDIR_STEM.match(userdir)
        if log_stem and (not userdir_stem or userdir_stem.group(1) != log_stem.group(1)):
            errors.append(
                f"{SMOKE.relative_to(ROOT).as_posix()}:{lineno} 趟次「{label}」的日志 stem（{log_stem.group(1)}）"
                f"与用户目录（{userdir}）不一致——两者不是同一趟的产物时，隔离目录与被断言的日志对不上")
        if "$" in marker:
            # 变量拼出的标记静态判不出（本表内不存在这种写法）——**显式失败**而不是静默跳过：
            # 跳过即「取不到判据仍判 clean」，该标记的打印点从此无人判（AGENTS §6 铁律 2）
            errors.append(
                f"{SMOKE.relative_to(ROOT).as_posix()}:{lineno} 趟次「{label}」的完成标记含 `$`"
                f"（{marker}）——变量拼出的标记无法静态判定，本门禁在此取不到判据；"
                "请把标记写成字面量，或登记进静态可判的表格并写明理由"
            )
            continue
        tag_match = re.match(r"^\[([a-z0-9-]+)\]", marker)
        if not tag_match:
            errors.append(f"完成标记「{marker}」不带 `[标签]` 前缀——标签与趟次开关的对应关系取不到判据，"
                          "须写成 `[<探针标签>] …` 形态")
            continue
        tag = tag_match.group(1)
        switches = case_toks[6:]
        allowed = None
        if switches:
            allowed = set()
            for sw in switches:
                switch = sw.lstrip("-").split("=", 1)[0]
                allowed.add(switch)
                allowed.add(MARKER_TAG_ALIAS.get(switch, switch))
            for sw in switches:
                if "=" in sw and sw.split("=", 1)[1] not in marker:
                    errors.append(
                        f"{SMOKE.relative_to(ROOT).as_posix()}:{lineno} 趟次「{label}」的开关 {sw} 指定了 "
                        f"`{sw.split('=', 1)[1]}`，标记「{marker}」里却没有——开关与断言不同源时，"
                        "改开关却忘改断言会静默判到别的打印点")
        else:
            entry = NO_SWITCH_MARKER_TAGS.get(scene)
            if entry is None:
                errors.append(
                    f"{SMOKE.relative_to(ROOT).as_posix()}:{lineno} 趟次「{label}」没有探针开关"
                    f"（场景 {scene or '生产 main.tscn'}），标记标签无法派生——登记进 NO_SWITCH_MARKER_TAGS，"
                    "写清该入口打什么标记（并注明理由）")
            else:
                allowed = {entry[0]}
        if allowed is not None and tag not in allowed:
            errors.append(
                f"{SMOKE.relative_to(ROOT).as_posix()}:{lineno} 趟次「{label}」的完成标记标签 [{tag}] 与该趟"
                f"开关/场景派生出的标签不符（应为 {'/'.join(sorted(allowed))}）——标签与趟次不同源时，"
                "断言可能判到别的探针的打印点")

    # ---------- d) 标记字符串必须真有打印点 ----------

    def csharp_sources():
        out = []
        if CSHARP.is_dir():
            for p in CSHARP.rglob("*.cs"):
                if any(part in {"obj", "bin", "__pycache__"} for part in p.parts):
                    continue
                out.append(strip_comments(p.read_text(encoding="utf-8", errors="replace")))
        return out

    sources = csharp_sources()
    if not sources:
        errors.append("csharp/ 下未收集到任何 .cs——路径漂移？取不到判据，拒绝判 clean")

    # 判据落在**打印调用的实参**上，而不是「全库字符串包含」：此前按包含判定把注释里的标记、
    # 别的日志文案里恰好包含的标记、以及另一趟的字面量（例如把编队趟的标记换成某条 fog 趟的
    # 错误文案）都算作「有打印点」——而该断言在其对应趟次永不可能通过，现场表现是「一条永远
    # 红的门禁」，正是本条声称要抓的形态。故先剥注释，再只取打印调用实参里的字符串字面量。
    print_lits: set[str] = set()
    templates = []
    placeholder_re = re.compile(r"%[-+0-9.#]*[sdfgxX]")
    for src in sources:
        for lit in print_literals(src):
            print_lits.add(lit)
            if "%" not in lit or "]" not in lit or not placeholder_re.search(lit):
                continue
            patterns = []
            pos = 0
            for m in placeholder_re.finditer(lit):
                patterns.append(re.escape(lit[pos:m.start()]))
                patterns.append(".*")
                pos = m.end()
            patterns.append(re.escape(lit[pos:]))
            templates.append(re.compile("".join(patterns)))

    if not print_lits:
        errors.append("csharp/ 里未从打印调用中解析出任何字符串——取不到判据，拒绝判 clean")
    if not templates:
        errors.append("csharp/ 里未解析出任何含占位的打印模板——取不到判据，拒绝判 clean")

    for _kind, stmt, lineno in stmts:
        if _kind != "expect_marker":
            continue
        # expect_marker "label" "$log" "marker"  → 取最后一个引号串
        found = re.findall(r'"([^"]*)"', stmt)
        if not found:
            errors.append(f"expect_marker 语句取不到标记字符串：{stmt[:80]}")
            continue
        marker = found[-1]
        if "$" in marker:
            # 同上：静默跳过一次，就会在下面「标记必须有打印点」这一节里被当成通过——
            # 断言的实际取值无人判，属「取不到判据」形态
            errors.append(
                f"{SMOKE.relative_to(ROOT).as_posix()}:{lineno} 的完成标记含 `$`（{marker}）——"
                "无法判定它在 csharp/ 里有没有打印点（取不到判据），请写成字面量"
            )
            continue
        # 运行时判定是 `grep -qF`（子串匹配），故标记是某条打印实参的子串即成立
        # （如 `[boss-probe] 阶段机全周期完成` 命中 `...（P1→P2→狂暴→击杀，BossKills=%d）`）。
        if any(marker in lit for lit in print_lits):
            continue
        if any(t.fullmatch(marker) for t in templates):
            continue
        errors.append(
            f"完成标记「{marker}」在 csharp/ 的打印调用实参里找不到（子串命中与格式模板都不匹配）"
            "——字符串写错则该断言永不可能通过；只出现在注释或非打印文案里都不算打印点"
        )

# g) 活跃文档的体积预算（两侧都判：只判上限时，文件被清空/截断会平凡通过）
for budget_path, budget, budget_hint in SIZE_BUDGETS:
    budget_text = read(budget_path)
    if budget_text is None:
        continue
    budget_size = len(budget_text)
    size_report.append(f"{budget_path.name} {budget_size}/{budget}")
    if budget_size > budget:
        errors.append(
            f"{budget_path.relative_to(ROOT).as_posix()} 已 {budget_size} 字符，超出预算 {budget}"
            f"——{budget_hint}；确有必要则显式抬高预算，并在提交正文说明为什么值得"
        )
    elif budget_size < budget // SIZE_FLOOR_RATIO:
        errors.append(
            f"{budget_path.relative_to(ROOT).as_posix()} 只有 {budget_size} 字符"
            f"（不足预算 {budget} 的三分之一）——文件被清空或路径漂移？取不到判据，拒绝判 clean"
        )

# h) 门禁命中台账与步骤登记集一一对应（AGENTS §6 准入门槛：新门禁要写退役条件，而退役条件
#    要靠台账判；新门禁不带一行 = 未来提议退役时它没有可查的依据面）
ledger_text = read(LEDGER)
if ledger_text is not None:
    for row in re.findall(r"(?m)^\|([^|]+)\|", ledger_text):
        cell = row.strip().strip("`").strip()
        if cell and cell != "门禁" and not set(cell) <= {"-", ":", " "}:
            ledger_rows.add(cell)
    if not ledger_rows:
        errors.append(
            f"{LEDGER.relative_to(ROOT).as_posix()} 未解析出任何门禁行——表格结构漂移？"
            "取不到判据，拒绝判 clean"
        )
    for step in gates_order:
        if step not in ledger_rows:
            errors.append(
                f"门禁 {step} 在 {LEDGER.relative_to(ROOT).as_posix()} 里没有对应行"
                "——新增门禁必须同提交带上台账行（退役条件的唯一依据面）"
            )
    for row_name in sorted(ledger_rows - set(gates_order)):
        errors.append(
            f"{LEDGER.relative_to(ROOT).as_posix()} 里的「{row_name}」不是 gates.py 登记的门禁"
            "——门禁改名/删除后台账未同步，退役决策会基于不存在的判据"
        )

if errors:
    for e in errors:
        print(f"::error::{e}")
    print(f"gate-wiring gate: FAILED（{len(errors)} 项）")
    sys.exit(1)

print(
    f"gate-wiring gate: clean（{len(on_disk - set(HELPER_MODULES))} 个门禁脚本全部注册进 gates.py 与 ci.yml，"
    f"{len(ci_steps) if ci_steps else 0} 个 CI 步骤无容错属性；另有 {len(HELPER_MODULES)} 个共用模块被门禁 import）；"
    f"冒烟 {len(cases)} 趟各自配对完成标记断言（下限 {MIN_SMOKE_CASES}）、标记均有对应打印点；"
    f"发布包候选 {package_candidates} 个（ProbeHost/探针场景/tests 树）全被 exclude 覆盖；"
    f"{'、'.join(size_report)} 字符（预算内）；台账 {len(ledger_rows)} 行与登记集一致；"
    f"regenerate_all 覆盖 {len(generator_called)} 个生成器"
)
PY
