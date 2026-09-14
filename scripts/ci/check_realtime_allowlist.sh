#!/usr/bin/env bash
# 真实时间门禁：csharp/ 里的墙钟 / 帧率 / 机器性能读数必须逐一登记（双向判定）。
# 用法：bash scripts/ci/check_realtime_allowlist.sh
#
# 抓的静默错误：AGENTS §5 确定性硬规则第 2 条——判定与模拟只看模拟状态，不拿墙钟 / 帧率 /
# 机器性能当判据。把判定或模拟换成墙钟后，无头固定步长下墙钟与模拟时间脱节：不崩、不报错，
# 只在特定帧率或时间缩放下变形（返航输入宽限的 71e6324 就是这一形态的第一次修）。
# 反向同样要抓：合法墙钟站点被「顺手改成模拟时间」会让节流 / 手感 / 读数悄悄失效——同一处
# 输入宽限在 1.5 个月内被反方向改了三次，本门禁即为此设。
#
# 判定口径（单源在本脚本 ALLOW 表，AGENTS §5 与 DESIGN_BASELINE 只给指针，不复述清单）：
#   1) 扫描 csharp/ 下所有 .cs 的**代码段**（剥字符串与注释——注释里提到 API 名不算命中），
#      按 (文件, 符号) 统计命中数；
#   2) 命中数超出 ALLOW 登记量 → 红（新增未登记的真实时间使用，输出 文件:行）；
#   3) ALLOW 登记量多于实际命中 → 红（登记站点消失，通常是合法用途被改掉）；
#   4) 总命中为 0 → 红（防扫描范围或正则漂移造成假绿）。
# 登记粒度 = (文件, 符号, 计数)：同文件多打一处也会改计数，逼每次改动重新给理由；
# csharp/core/ 出现任何命中即红（纯逻辑层零真实时间，可单测的前提）。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

python3 - <<'PY'
import pathlib, re, sys

ROOT = pathlib.Path.cwd()
SKIP = {".git", ".godot", "builds", "tools", "bin", "obj", "__pycache__", ".venv"}

# 允许清单（单源）：(相对路径, 符号, 计数, 理由)
ALLOW = (
    ("csharp/godot/GameState.cs", "Time.GetTicksMsec", 1,
     "开机耗时基准：autoload 最早生命周期点打点，只供 --startup-time 分段读数，不参与模拟与判定"),
    ("csharp/godot/ProbeHost.cs", "Time.GetTicksMsec", 3,
     "启动耗时打印（1 处）与返航探针的环境前置守卫（2 处：记墙钟起点、算已耗真实毫秒）——"
     "守卫用途是「若 90 帧耗时已达生产宽限则无法判别时间基准，显式报错」，度量的是真实耗时，"
     "非判定输入；探针的判定本身只用帧数与过场状态"),
    ("csharp/godot/ReturnCinematic.cs", "Time.GetTicksMsec", 2,
     "返航过场输入宽限：门控的是玩家物理按键是否还压着 / 输入事件是否还在队列里这一现实世界窗口，"
     "与过场世界推进多少无关；用模拟时间会在时间缩放或回调停摆时被拉伸乃至永不结束（跳过失效）。见 71e6324"),
    ("csharp/godot/TitleScreen.cs", "Time.GetTicksMsec", 2,
     "标题屏输入守卫：挡上一场景残留按键误开局，同样是现实世界（人机）窗口，理由同返航宽限"),
    ("csharp/godot/SfxPlayer.cs", "Time.GetTicksMsec", 1,
     "音效最小触发间隔节流：混音按真实时间轴推进，暂停 / 子弹时间下模拟时间会停摆或被缩放，节流必须按真实毫秒"),
    ("csharp/godot/SettingsUi.cs", "Engine.GetFramesPerSecond", 1,
     "设置页帧率读出：玩家可见的机器性能读数，本身即机器相关量"),
    ("csharp/godot/SettingsUi.cs", "DisplayServer.ScreenGetRefreshRate", 1,
     "设置页刷新率读出：玩家可见的机器性能读数，仅用于文案展示"),
    ("csharp/godot/Player.cs", "GetProcessDeltaTime", 1,
     "右摇杆虚拟准星积分：与同函数 Engine.GetProcessFrames 门控配对（渲染帧轴上的缩放 delta）——"
     "非判定，且换成物理 delta 会随帧率错速；--fixed-fps 下恒 1/60"),
)

# 真实时间 / 帧率 / 机器性能 API 模式 → 规范符号（qualifier 归一，便于登记）
FIXED = (
    (re.compile(r"\bEngine\.GetFramesPerSecond\b|\bGetFramesPerSecond\b"), "Engine.GetFramesPerSecond"),
    (re.compile(r"\bEngine\.GetProcessTime\b"), "Engine.GetProcessTime"),
    (re.compile(r"\bEngine\.GetFramesDrawn\b"), "Engine.GetFramesDrawn"),
    (re.compile(r"(?:\bEngine\.)?\bGetProcessDeltaTime\b"), "GetProcessDeltaTime"),
    (re.compile(r"\bOS\.GetTicks(?:Msec|Usec)?\b"), "OS.GetTicks"),
    (re.compile(r"\bOS\.(?:GetUnixTime\w*|GetDatetime\w*)\b"), "OS 日历时间"),
    (re.compile(r"\bDisplayServer\.ScreenGetRefreshRate\b"), "DisplayServer.ScreenGetRefreshRate"),
    (re.compile(r"\bDateTime\b|\bDateTimeOffset\b|\bTimeProvider\b"), "System.DateTime 家族"),
    (re.compile(r"\bStopwatch\b|\bEnvironment\.TickCount\b"), "System.Diagnostics 墙钟"),
)
# Godot Time 单例全是墙钟 / 日历量：`Time.<任意方法>` 一律命中（引擎新增方法无需改本正则）
TIME_CALL = re.compile(r"\bTime\.([A-Za-z_]\w*)")


def code_only(line: str) -> str:
    """返回剥掉字符串字面量与注释后的代码段；`res://` / `http://` 的协议分隔不算注释起点。"""
    out, i, n = [], 0, len(line)
    while i < n:
        c = line[i]
        if c == '"':
            if i > 0 and line[i - 1] == "@":  # 逐字字符串："" 为转义引号
                i += 1
                while i < n:
                    if line[i] == '"':
                        if i + 1 < n and line[i + 1] == '"':
                            i += 2
                            continue
                        i += 1
                        break
                    i += 1
                out.append('""')
                continue
            i += 1
            while i < n:
                if line[i] == "\\":
                    i += 2
                    continue
                if line[i] == '"':
                    i += 1
                    break
                i += 1
            out.append('""')  # 字符串内容不参与命中
            continue
        if c == "/" and i + 1 < n and line[i + 1] == "/":
            if i > 0 and line[i - 1] == ":":  # 协议分隔，不是注释起点
                out.append("//")
                i += 2
                continue
            break  # 行尾注释及之后不参与命中
        out.append(c)
        i += 1
    return "".join(out)


hits: dict[tuple[str, str], list[tuple[int, str]]] = {}
for path in sorted(ROOT.rglob("*.cs")):
    if any(part in SKIP for part in path.parts):
        continue
    rel = str(path.relative_to(ROOT)).replace("\\", "/")
    for i, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
        code = code_only(line)
        if not code.strip():
            continue
        for m in TIME_CALL.finditer(code):
            hits.setdefault((rel, f"Time.{m.group(1)}"), []).append((i, line.strip()))
        for pat, canon in FIXED:
            if pat.search(code):
                hits.setdefault((rel, canon), []).append((i, line.strip()))

expected: dict[tuple[str, str], int] = {}
for rel, canon, count, _reason in ALLOW:
    expected[(rel, canon)] = expected.get((rel, canon), 0) + count

errors: list[str] = []
for rel in sorted({key[0] for key in expected}):
    if not (ROOT / rel).exists():
        errors.append(f"允许清单指向的文件不存在：{rel}（路径已动须同步本门禁）")

total = sum(len(v) for v in hits.values())
if total == 0:
    print("::error::未捕获到任何真实时间 API 命中（扫描范围或正则漂移？门禁需同步）——拒绝判 clean")
    sys.exit(1)

for key, lines in sorted(hits.items()):
    allow = expected.get(key, 0)
    if len(lines) > allow:
        note = "未登记" if allow == 0 else f"超出登记量（登记 {allow} 处）"
        for lineno, text in lines:
            errors.append(f"{key[0]}:{lineno}  {key[1]} {note}：{text[:90]}")
for key, allow in sorted(expected.items()):
    got = len(hits.get(key, []))
    if got < allow:
        errors.append(
            f"{key[0]}  登记站点消失：{key[1]} 登记 {allow} 处、实际 {got} 处"
            "（合法墙钟被改成模拟时间？确认后更新 ALLOW）"
        )

if errors:
    for e in errors[:40]:
        print("::error::" + e)
    print(f"realtime-allowlist gate: FAILED（{len(errors)} 项）")
    sys.exit(1)
print(f"realtime-allowlist gate: clean（{total} 处真实时间命中全部登记，覆盖 {len(hits)} 个 (文件, 符号) 组合）")
PY
