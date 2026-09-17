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
#   1) 扫描 csharp/ 下所有 .cs 的**代码段**（先按字符串状态机整体剥掉所有字符串字面量
#      ——普通 "..."、逐字 @"..."、原始 """..."""、字符 'x'——再剥注释；注释与字符串里提到 API 名
#      都不算命中；内插字符串 $"...{expr}..." 的洞是代码，递归进来参与命中，否则本仓库惯用的
#      内插写法会整块漏判），按 (文件, 符号) 统计命中数；
#      字符串必须**整体**剥掉而不能只看 $ / @ 前缀：普通字符串里的 `//`（如 "res://a // b"）
#      会被当行注释起点，同一行其后的墙钟调用整块蒸发——登记量对得上、总命中不为 0，
#      三道老守卫全落空；
#   2) 命中数超出 ALLOW 登记量 → 红（新增未登记的真实时间使用，输出 文件:行）；
#   3) ALLOW 登记量多于实际命中 → 红（登记站点消失，通常是合法用途被改掉）；
#   4) 总命中为 0 → 红（防扫描范围或正则漂移造成假绿）。
# 登记粒度 = (文件, 符号, 计数)：同文件多打一处也会改计数，逼每次改动重新给理由；
# csharp/core/ 出现任何命中即红（纯逻辑层零真实时间，可单测的前提）。
#
# 口径边界（本门禁收哪些量）：墙钟（Time/OS/Stopwatch/Environment.TickCount/DateTime +
# TimeProvider）、帧率与帧计数（FramesPerSecond/FramesDrawn/ProcessDeltaTime/ProcessTime）、
# 机器性能计数器（Performance.Get*Monitor、GC.GetTotal*），以及**玩家机器的显示配置几何读数**
# （DisplayServer 的屏幕缩放 / 可用区 / vsync 实际模式 / 窗口所在屏）。最后这一类不参与模拟与
# 判定，但同属「机器相关、无头环境下与真实桌面分叉」的量：放进清单只为让 core 层用不了它，
# 并使每处使用都要给理由（而非在脚本里写一串豁免说明——豁免说明不会在新增使用时报红）。
# 不收：DisplayServer.GetName（headless 判定，返回驱动名而非机器读数）、DisplayServer.VSyncMode
# 枚举成员、Window.CurrentScreen 属性读取。若将来出现这些之外的机器读数，按同样口径登记。
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
    ("csharp/godot/ProbeHost.TitleNav.cs", "Time.GetTicksMsec", 3,
     "标题屏手柄可达探针的注入时机：标题屏输入守卫本身是 0.5s 真实时间窗（挡上一场景残留按键），"
     "无头 fixed-fps 下帧数与真实时间脱钩（45 帧可能只过百毫秒），等守卫过去必须按墙钟——"
     "度量的是「守卫还剩多久」这一现实世界窗口，探针的判定本身只用焦点落位与场景状态"),
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
    ("csharp/godot/SettingsUi.cs", "DisplayServer.WindowGetVsyncMode", 1,
     "设置页 vsync 实际状态读出（驱动/平台可覆盖开关值）：机器配置读数，仅驱动文案展示，不参与判定"),
    ("csharp/godot/SettingsUi.cs", "DisplayServer.WindowGetCurrentScreen", 1,
     "设置页分辨率档位适配判定要问「当前在哪个屏」：机器配置读数，只决定按钮可用性，不参与模拟"),
    ("csharp/godot/SettingsUi.cs", "DisplayServer.ScreenGetScale", 1,
     "设置页档位适配：逻辑尺寸 × 屏幕缩放与可用区比较——机器配置读数，仅决定按钮可用性"),
    ("csharp/godot/SettingsUi.cs", "DisplayServer.ScreenGetUsableRect", 1,
     "设置页档位适配的可用区上界：机器配置读数，仅决定按钮可用性，不参与模拟"),
    ("csharp/godot/SettingsService.cs", "DisplayServer.ScreenGetScale", 2,
     "窗口物理尺寸换算（应用窗口模式时 1 处、拖拽后回写基线时 1 处）：窗口几何必须按真实屏幕缩放折算，"
     "与游戏世界推进无关"),
    ("csharp/godot/SettingsService.cs", "DisplayServer.ScreenGetUsableRect", 1,
     "超出屏幕可用区的窗口尺寸回缩：机器配置读数，只影响窗口几何"),
    ("csharp/godot/Player.cs", "GetProcessDeltaTime", 1,
     "右摇杆虚拟准星积分：与同函数 Engine.GetProcessFrames 门控配对（渲染帧轴上的缩放 delta）——"
     "非判定，且换成物理 delta 会随帧率错速；--fixed-fps 下恒 1/60。同一处 delta 另经"
     "GameState.RealDelta 反解真实帧长喂鼠标路磁吸输入窗口（手部位移是现实世界位移，不随"
     "Engine.TimeScale 缩放；反解口径单源在 GameFeelService.RealDelta，未新增墙钟读数）"),
)

# 真实时间 / 帧率 / 机器性能 API 模式 → 规范符号（qualifier 归一，便于登记）
FIXED = (
    (re.compile(r"\bEngine\.GetFramesPerSecond\b|\bGetFramesPerSecond\b"), "Engine.GetFramesPerSecond"),
    (re.compile(r"\bEngine\.GetProcessTime\b"), "Engine.GetProcessTime"),
    (re.compile(r"\bEngine\.GetFramesDrawn\b"), "Engine.GetFramesDrawn"),
    (re.compile(r"(?:\bEngine\.)?\bGetProcessDeltaTime\b"), "GetProcessDeltaTime"),
    (re.compile(r"\bOS\.GetTicks(?:Msec|Usec)?\b"), "OS.GetTicks"),
    (re.compile(r"\bOS\.GetSystemTime\w*\b"), "OS.GetSystemTime"),
    (re.compile(r"\bOS\.(?:GetUnixTime\w*|GetDatetime\w*)\b"), "OS 日历时间"),
    # 机器性能计数器：监控项读数随机器与负载变化，不得进判定
    (re.compile(r"\bPerformance\.(?:GetMonitor|GetCustomMonitor)\b"), "Performance.GetMonitor"),
    (re.compile(r"\bGC\.(?:GetTotalMemory|GetTotalAllocatedBytes)\b"), "GC.GetTotalMemory"),
    # 玩家机器的显示配置几何读数（见文件头「口径边界」）
    (re.compile(r"\bDisplayServer\.ScreenGetScale\b"), "DisplayServer.ScreenGetScale"),
    (re.compile(r"\bDisplayServer\.ScreenGetUsableRect\b"), "DisplayServer.ScreenGetUsableRect"),
    (re.compile(r"\bDisplayServer\.WindowGetVsyncMode\b"), "DisplayServer.WindowGetVsyncMode"),
    (re.compile(r"\bDisplayServer\.WindowGetCurrentScreen\b"), "DisplayServer.WindowGetCurrentScreen"),
    (re.compile(r"\bDisplayServer\.ScreenGetRefreshRate\b"), "DisplayServer.ScreenGetRefreshRate"),
    (re.compile(r"\bDateTime\b|\bDateTimeOffset\b|\bTimeProvider\b"), "System.DateTime 家族"),
    # Environment.TickCount / TickCount64：尾部 `\w*` 而非 `\b`——`TickCount64` 的 `6` 是词字符，
    # 旧写法 `\bEnvironment\.TickCount\b` 对 64 位版本永不成立（漏判整类）
    (re.compile(r"\bStopwatch\b|\bEnvironment\.TickCount\w*\b"), "System.Diagnostics 墙钟"),
)
# Godot Time 单例全是墙钟 / 日历量：`Time.<任意方法>` 一律命中（引擎新增方法无需改本正则）
TIME_CALL = re.compile(r"\bTime\.([A-Za-z_]\w*)")


class _Scanner:
    """按 C# 词法把源码切成「代码段」与「字符串 / 注释段」。

    字符串字面量**整体**剥掉（不参与命中）——这是本门禁最要紧的一步：普通字符串里的 `//`
    若被当注释起点，同一行其后的墙钟调用会整块蒸发（普通字符串里写 res 协议加 `//` 再接未登记
    墙钟即静默逃过）。内插字符串的洞是代码，递归扫描后保留，否则本仓库惯用的内插写法会整类漏判。
    """

    def __init__(self, text: str) -> None:
        self.text = text
        self.i = 0

    def code(self, stop_at=None, emit=None) -> str:
        """扫到 stop_at（内插洞的收尾括号）或文本末尾，返回剥掉字符串与注释后的代码段。

        `emit` 是外层代码段的收集器：内插洞里的表达式属于**外层**代码（`$"{expr}"` 的
        expr 不因外层是字符串而消失），所以要回填给调用方，不能随字符串一起丢掉。
        """
        text, out = self.text, []
        sink = out.append if emit is None else emit
        while self.i < len(text):
            c = text[self.i]
            if stop_at is not None and text.startswith(stop_at, self.i):
                return "".join(out)
            if text.startswith("//", self.i):
                nl = text.find("\n", self.i)
                if nl < 0:
                    break
                self.i = nl + 1      # 注释里的换行要还给代码，否则会把两行粘在一起
                sink("\n")
                continue
            if text.startswith("/*", self.i):
                end = text.find("*/", self.i + 2)
                self.i = len(text) if end < 0 else end + 2
                sink(" ")
                continue
            if c == '"':
                if text.startswith('"""', self.i):    # 原始字符串：整体剥掉，含其中的 `//`
                    self._raw()
                else:
                    self._regular(False)
                sink(" ")
                continue
            if c in "@$":
                if self._prefixed_string(c, sink):
                    sink(" ")
                    continue
                sink(c)              # 只是裸 @ 或 $（标识符前缀），按普通代码处理
                self.i += 1
                continue
            if c == "'":              # 字符字面量：'/' 与 '"' 这类值必须整体剥掉，否则污染代码段
                self._char()
                sink(" ")
                continue
            sink(c)
            self.i += 1
        return "".join(out)

    def _advance_escaped(self) -> None:
        """跳过当前字符；反斜杠转义连跳两位。"""
        if self.text[self.i] == "\\":
            self.i += 2
        else:
            self.i += 1

    def _regular(self, verbatim: bool) -> None:
        self.i += 1                  # 开引号
        while self.i < len(self.text):
            c = self.text[self.i]
            if not verbatim and c == "\\":
                self._advance_escaped()
                continue
            if c == '"':
                if verbatim and self.text.startswith('""', self.i):   # 逐字串里 "" 是转义引号
                    self.i += 2
                    continue
                self.i += 1
                return
            self.i += 1

    def _quote_run(self) -> int:
        """self.i 处连续引号的个数（原始字符串的开 / 闭引号串）。"""
        j = self.i
        while j < len(self.text) and self.text[j] == '"':
            j += 1
        return j - self.i

    def _raw(self) -> None:
        opening = self._quote_run()
        end = self.text.find('"' * opening, self.i + opening)
        self.i = len(self.text) if end < 0 else end + opening

    def _char(self) -> None:
        self.i += 1
        while self.i < len(self.text):
            if self.text[self.i] == "\\":
                self._advance_escaped()
                continue
            if self.text[self.i] == "'":
                self.i += 1
                return
            self.i += 1

    def _prefixed_string(self, first: str, emit) -> bool:
        """处理 @ 或 $ 打头的字符串；只是普通标识符前缀时返回 False 且不移动游标。

        内插洞里的表达式递归按代码扫（本仓库惯用内插墙钟调用，整块剥掉会漏判），并回填 emit。
        """
        verbatim = first == "@"
        interp = first == "$"
        j, n = self.i, len(self.text)
        while j < n and self.text[j] in "@$":
            verbatim = verbatim or self.text[j] == "@"
            interp = interp or self.text[j] == "$"
            j += 1
        if j >= n or self.text[j] != '"':
            return False
        self.i = j
        if not interp:               # 仅 @ 前缀：逐字字符串，洞不是代码，整体剥掉
            self._regular(verbatim=True)
            return True
        opening = self._quote_run()
        if opening >= 3:             # 内插原始字符串
            if verbatim:
                self._raw()
            else:
                self._raw_interpolated(emit)
            return True
        self._regular_interpolated(verbatim, emit)
        return True

    def _regular_interpolated(self, verbatim: bool, emit) -> None:
        self.i += 1                  # 开引号
        while self.i < len(self.text):
            c = self.text[self.i]
            if not verbatim and c == "\\":
                self._advance_escaped()
                continue
            if c == '"':
                if verbatim and self.text.startswith('""', self.i):
                    self.i += 2
                    continue
                self.i += 1
                return
            if c == "{":
                if self.text.startswith("{{", self.i):    # {{ 是字面花括号
                    self.i += 2
                    continue
                self._hole("}", emit)
                continue
            self.i += 1

    def _hole(self, closing: str, emit) -> None:
        self.i += 1                  # 洞的开括号
        if emit is not None:
            emit(" ")                # 洞两侧留白，避免与相邻代码粘连成新标识符
        hole = self.code(stop_at=closing, emit=emit)
        if emit is not None:
            emit(hole)
            emit(" ")
        if self.i < len(self.text):
            self.i += 1              # 洞的收尾括号

    def _raw_interpolated(self, emit) -> None:
        opening = self._quote_run()
        self.i += opening            # 开引号串
        holes = "$" * max(opening - 1, 1)
        curlies = "{" * opening
        while self.i < len(self.text):
            c = self.text[self.i]
            if c == "$" and self.text.startswith(holes + "{", self.i):
                if self.text.startswith(holes + curlies, self.i):
                    self.i += len(holes) + len(curlies)    # 内插原始串里的字面花括号
                    continue
                self.i += len(holes)
                self._hole("}", emit)
                continue
            if c == '"' and self._quote_run() >= opening:
                self.i += self._quote_run()
                return
            self.i += 1


def code_only(text: str) -> str:
    """返回剥掉字符串字面量（含普通 / 逐字 / 原始字符串与字符字面量，内插洞保留为代码）与注释后的代码段。"""
    return _Scanner(text).code()


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
    if rel.startswith("csharp/core/"):
        errors.append(f"允许清单里出现 core 层条目 {rel}——纯逻辑层零真实时间是硬约束，不得登记放行")
# core 层零真实时间：这是「纯逻辑可单测」的前提（AGENTS §5）。守卫做成结构性的——直接把 core 里的
# 命中判红，而不是「因为 ALLOW 里没登记所以红」：否则往 ALLOW 加一条 core 条目就能静默合法化，
# 声明与判据分叉。
core_hits = sorted(key for key in hits if key[0].startswith("csharp/core/"))
for rel, canon in core_hits:
    for lineno, text in hits[(rel, canon)]:
        errors.append(f"{rel}:{lineno}  {canon} 出现在 core 层（纯逻辑零真实时间，判定才可单测）：{text[:90]}")

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
