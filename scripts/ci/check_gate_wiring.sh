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
# 判定（四条，任一条取不到判据即红——AGENTS §6 铁律 2）：
#   a) scripts/ci/ 下每个 .sh 都注册在 gates.py 的 STEPS 与 ci.yml 里（缺失＝永不执行）；
#   b) gates.py 的 STEPS 与 ci.yml 的调用**顺序逐条一致**（集合相等抓不到重排：本地排在冒烟
#      之后的静态门禁，CI 在构建前就红——同一份改动本地与 CI 的失败暴露面不一致，排查顺序错位）；
#      另判 gates.py / ci.yml 登记的每个脚本都真实存在（不存在＝CI 在调用处才报错，本地可能不跑）；
#      再判 gates.py 的 slug 不重名——`--only <slug>` 是跑单步的唯一入口，重名会静默跑错一步；
#   c) check_smoke.sh 的每条 run_case 紧随一条 expect_marker（漏断言＝该趟降级为不崩即过），
#      且 run_case 总数不低于 MIN_SMOKE_CASES、每趟帧数参数为正整数——整趟的 run_case 与
#      expect_marker 两行一起删时配对性仍成立，只有趟数下限能抓「覆盖被整趟删掉」；
#   d) 每条 expect_marker 的标记字符串都能对应到 csharp/ 里的**打印点**——字面命中，或匹配某条
#      含格式占位的打印模板（`[event-probe] %s 全周期完成` 这类：id 由运行期代入，字面量里查不到）。
#      字符串写错则断言永不可能通过，而现场表现只是「一条永远红的门禁」，容易被顺手删掉。
# 「取不到判据」防线：脚本集为空、gates.py 登记集为空、ci.yml 调用集为空、趟次为 0、帧数非正整数，
# 一律红（AGENTS §6 铁律 2：取不到判据必须显式失败）。
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

errors: list[str] = []
MISSING = object()


def read(path: pathlib.Path):
    if not path.exists():
        errors.append(f"{path.relative_to(ROOT).as_posix()} 不存在——取不到判据，拒绝判 clean")
        return None
    return path.read_text(encoding="utf-8", errors="replace")


gates_text = read(GATES)
yml_text = read(CI_YML)
smoke_text = read(SMOKE)
if gates_text is None or yml_text is None:
    print("\n".join(errors))
    sys.exit(1)

# ---------- a/b) 脚本 ↔ 注册（双向） ----------

on_disk = {p.name for p in CI_DIR.glob("*.sh")}
if not on_disk:
    errors.append(f"{CI_DIR.relative_to(ROOT).as_posix()} 下未发现任何 .sh——路径漂移？取不到判据，拒绝判 clean")
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
occurrences = [(m.start(), m.group(1)) for m in re.finditer(r"scripts/ci/([A-Za-z0-9_]+\.sh)", yml_text)]
occurrences += [(m.start(), DOTNET) for m in re.finditer(r"dotnet build --nologo", yml_text)]
yml_order = [token for _pos, token in sorted(occurrences)]
in_gates = {t for t in gates_order if t != DOTNET}
in_yml = {t for t in yml_order if t != DOTNET}

# 本门禁自己不在 gates.py 的 "script" 形态里被别的步骤引用，但必须被登记（自身也参与断言）
for name in sorted(on_disk):
    if name not in in_gates:
        errors.append(f"{name} 未注册进 scripts/ci/gates.py 的 STEPS——该门禁永不执行（假绿最坏形态）")
    if name not in in_yml:
        errors.append(f"{name} 未注册进 .github/workflows/ci.yml——CI 不跑该门禁，本地绿与 CI 绿含义不一致")
for name in sorted(in_gates | in_yml):
    if name not in on_disk:
        errors.append(f"注册表引用了不存在的脚本 {name}——路径写错或脚本被删（调用处才报错）")

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

# ---------- c) 冒烟趟次 ↔ 完成标记断言 ----------

if smoke_text is not None:
    # 先吃掉反斜杠续行，再按语句看顺序：每条 run_case 之后必须紧跟一条 expect_marker
    joined = re.sub(r"\\\n", " ", smoke_text)
    stmts = []
    for lineno, raw in enumerate(joined.split("\n"), 1):
        s = raw.strip()
        if s.startswith("run_case "):
            stmts.append(("run_case", s, lineno))
        elif s.startswith("expect_marker "):
            stmts.append(("expect_marker", s, lineno))

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
    # 会造成「加覆盖反而红」的反向激励。当前实测 16 趟（check_smoke.sh 的 run_case 条数）。
    MIN_SMOKE_CASES = 18
    if len(cases) < MIN_SMOKE_CASES:
        errors.append(
            f"{SMOKE.relative_to(ROOT).as_posix()} 只解析出 {len(cases)} 趟 run_case（下限 "
            f"{MIN_SMOKE_CASES}）——整趟的 run_case 与 expect_marker 一起删时配对性判定仍成立，"
            "门禁会静默判 clean；确认是有意缩减覆盖后同步本常量，不要放宽下限"
        )

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

    # ---------- d) 标记字符串必须真有打印点 ----------

    def csharp_sources():
        out = []
        if CSHARP.is_dir():
            for p in CSHARP.rglob("*.cs"):
                if any(part in {"obj", "bin", "__pycache__"} for part in p.parts):
                    continue
                out.append(p.read_text(encoding="utf-8", errors="replace"))
        return out

    sources = csharp_sources()
    if not sources:
        errors.append("csharp/ 下未收集到任何 .cs——路径漂移？取不到判据，拒绝判 clean")

    # 含格式占位的打印模板 → 正则：id/计数等由运行期代入，字面量里查不到完整标记
    literal_re = re.compile(r'"((?:[^"\\]|\\.)*)"')
    placeholder_re = re.compile(r"%[-+0-9.#]*[sdfgxX]")
    templates = []
    for src in sources:
        for lit in literal_re.findall(src):
            if "%" not in lit or "]" not in lit:
                continue
            if not placeholder_re.search(lit):
                continue
            patterns = []
            pos = 0
            for m in placeholder_re.finditer(lit):
                patterns.append(re.escape(lit[pos:m.start()]))
                patterns.append(".*")
                pos = m.end()
            patterns.append(re.escape(lit[pos:]))
            templates.append(re.compile("".join(patterns)))

    if not templates:
        errors.append("csharp/ 里未解析出任何含占位的打印模板——取不到判据，拒绝判 clean")

    for stmt in markers:
        # expect_marker "label" "$log" "marker"  → 取最后一个引号串
        found = re.findall(r'"([^"]*)"', stmt)
        if not found:
            errors.append(f"expect_marker 语句取不到标记字符串：{stmt[:80]}")
            continue
        marker = found[-1]
        if "$" in marker:
            continue  # 变量拼出的标记无法静态判定，跳过（本表内不存在）
        if any(marker in src for src in sources):
            continue
        if any(t.fullmatch(marker) for t in templates):
            continue
        errors.append(
            f"完成标记「{marker}」在 csharp/ 里找不到打印点（字面命中和格式模板都不匹配）"
            "——字符串写错则该断言永不可能通过"
        )

if errors:
    for e in errors:
        print(f"::error::{e}")
    print(f"gate-wiring gate: FAILED（{len(errors)} 项）")
    sys.exit(1)

print(
    f"gate-wiring gate: clean（{len(on_disk)} 个门禁脚本全部注册进 gates.py 与 ci.yml；"
    f"冒烟 {len(cases)} 趟各自配对完成标记断言，标记均有对应打印点）"
)
PY
