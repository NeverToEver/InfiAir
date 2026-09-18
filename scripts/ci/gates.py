#!/usr/bin/env python3
"""InfiAir 本地门禁统一入口（Windows / Linux / macOS 通用）。

按顺序跑完全部四步：C# 构建零警告 → core 层单测 → 资源导入无警告 → 无头冒烟（主场景开机）。
判定逻辑与口径只有一份（scripts/ci/*.sh + dotnet build），本脚本只做 Windows 侧的调度：
自动发现 bash（Git Bash 优先、WSL 兜底）与 Godot 可执行文件，并按目标 shell 转换路径。
口径见 AGENTS.md「验证门禁」。

用法：
    python3 scripts/ci/gates.py                  # 全部四步
    python3 scripts/ci/gates.py --only smoke     # 只跑指定步（slug 见 --list）
    python3 scripts/ci/gates.py --godot D:\\tools\\godot-mono\\godot-mono.exe

为什么是 Python 而不是 .ps1：Windows PowerShell 5.1 读取无 BOM 脚本时按系统 ANSI 解码，
中文注释会变乱码并直接语法报错（一次普通编辑就会踩），而 Python 3 源码默认 UTF-8；
且单测门禁的 TRX 判定本就依赖 python3，不新增工具链依赖。
"""

from __future__ import annotations

import argparse
import os
import shutil
import signal
import subprocess
import sys
import tempfile
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]

# 步骤表：顺序即 CI 的步骤顺序（.github/workflows/ci.yml），逐条对应，新增或重排须两处同步。
# 构建排第一：编译不过时后续三步的失败都是它的余波，先暴露先修。
STEPS = (
    {"slug": "build", "name": "C# 构建零警告", "kind": "dotnet", "script": "", "godot": False},
    {"slug": "unit_tests", "name": "core 层单测", "kind": "bash", "script": "check_unit_tests.sh", "godot": False},
    {"slug": "import", "name": "资源导入无警告", "kind": "bash", "script": "check_import.sh", "godot": True},
    {"slug": "smoke", "name": "无头冒烟（主场景开机直达标题屏）", "kind": "bash", "script": "check_smoke.sh", "godot": True},
)


# 每步墙钟上限（秒）：任一步挂死时判该步失败并继续跑后续步骤，不再无限等待（此前 subprocess.run
# 无 timeout，CI 靠 job 的 15 分钟兜底、本地只能人工中断）。取值是实测时长的数倍，只作挂死安全阀
# ——墙钟不是判定口径（AGENTS §5 约束的是判定与模拟，不是机器耗时），故不追求贴近实测。
STEP_TIMEOUT = {"build": 900, "unit_tests": 900, "import": 600, "smoke": 600}
DEFAULT_TIMEOUT = 300


def fail(message: str) -> None:
    print(f"[门禁] {message}", file=sys.stderr)
    sys.exit(2)


def find_bash(explicit: str) -> str:
    """bash 发现顺序：显式指定 → BASH 环境变量 → Git for Windows → PATH → /bin/bash。

    Git Bash 优先于 WSL：它用原生 Windows 路径直接访问工作区，而 WSL 只挂 /mnt/<盘符>，
    且 WSL 家目录里可能存在同名旧副本（历史上确实出现过，改错副本无从察觉）。
    """
    candidates: list[str] = []
    if explicit:
        candidates.append(explicit)
    if os.environ.get("BASH"):
        candidates.append(os.environ["BASH"])

    program_files = os.environ.get("ProgramFiles", "")
    program_files_x86 = os.environ.get("ProgramFiles(x86)", "")
    local_app_data = os.environ.get("LOCALAPPDATA", "")
    if os.name == "nt":
        for base in (program_files, program_files_x86, local_app_data):
            if base:
                prefix = "Programs\\Git" if base == local_app_data else "Git"
                candidates.append(str(Path(base) / prefix / "bin" / "bash.exe"))

    on_path = shutil.which("bash")
    if on_path:
        candidates.append(on_path)
    if os.name != "nt":
        candidates.append("/bin/bash")

    for candidate in candidates:
        if candidate and Path(candidate).exists():
            return str(Path(candidate).resolve())
    fail("未找到 bash。门禁脚本为 .sh，请安装 Git for Windows，或用 --bash 指定。")
    return ""  # 不可达（fail 已退出）


def is_wsl(bash: str) -> bool:
    """按内核标识判定 WSL（盘符挂载为 /mnt/<drive>）；Git Bash/MSYS 为 /<drive>。"""
    try:
        kernel = subprocess.run(
            [bash, "-c", "uname -r"], capture_output=True, text=True, timeout=60
        ).stdout
    except (OSError, subprocess.SubprocessError):
        return False
    return "microsoft" in kernel.lower() or "wsl" in kernel.lower()


def to_shell_path(path: Path | str, wsl: bool) -> str:
    """Windows 路径 → 目标 shell 可用的 POSIX 路径。"""
    text = str(path).replace("\\", "/")
    if len(text) > 2 and text[1] == ":" and text[2] == "/":
        drive, rest = text[0].lower(), text[3:]
        return f"/mnt/{drive}/{rest}" if wsl else f"/{drive}/{rest}"
    return text


def find_godot(explicit: str) -> str:
    """Godot 发现顺序（与 run.bat 同一探测口径）：显式指定 → GODOT → PATH → 常见安装位置。"""
    candidates: list[str] = []
    if explicit:
        if not Path(explicit).exists():
            fail(f"指定的 Godot 不存在：{explicit}")
        candidates.append(explicit)
    if os.environ.get("GODOT"):
        candidates.append(os.environ["GODOT"])

    # PATH：.NET 版优先（标准版打不开含 C# 的工程）
    for name in ("godot-mono", "godot", "godot4"):
        found = shutil.which(name)
        if found:
            candidates.append(found)

    home = Path(os.environ.get("USERPROFILE") or Path.home())
    local_app_data = os.environ.get("LOCALAPPDATA", "")
    if local_app_data:
        candidates.append(str(Path(local_app_data) / "Godot" / "Godot.exe"))
    candidates.append(str(home / "Godot" / "Godot.exe"))
    downloads = home / "Downloads"
    if downloads.is_dir():
        candidates += [str(p) for p in sorted(downloads.glob("Godot_v4*mono*.exe"))]
        candidates += [str(p) for p in sorted(downloads.glob("Godot_v4*.exe"))]

    for candidate in candidates:
        if candidate and Path(candidate).exists():
            return str(Path(candidate).resolve())
    return ""


def preflight(bash: str) -> list[str]:
    """bash 侧最小依赖探活（python3 缺失会让单测门禁的通过数判定以难懂的方式失败）。"""
    probe = subprocess.run(
        [bash, "-c", "command -v python3 || command -v python"],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=60,
    )
    found = {line.strip().split("/")[-1] for line in probe.stdout.splitlines() if line.strip()}
    missing = [] if any(name.startswith("python") for name in found) else ["python3"]
    return missing


def kill_tree(proc: subprocess.Popen) -> None:
    """杀掉子进程及其整棵进程树。

    门禁脚本会派生引擎与 dotnet 子进程：只杀直接子进程（bash）时挂死的引擎会留在后台，
    本地表现是「门禁判失败但引擎还在跑、日志还在长」——超时安全阀就失效了。
    Windows 用 taskkill /T；POSIX 上子进程以新会话启动，按进程组杀。
    """
    if os.name == "nt":
        subprocess.run(["taskkill", "/F", "/T", "/PID", str(proc.pid)], capture_output=True)
        return
    try:
        os.killpg(os.getpgid(proc.pid), signal.SIGKILL)
    except (OSError, ProcessLookupError):
        proc.kill()


def run_with_timeout(cmd: list[str], timeout: int, cwd: str | None = None) -> tuple[int, str, bool]:
    """跑命令并取 (退出码, 输出, 是否超时)。超时即杀进程树；退出码置 -1（非 0，判失败）。"""
    kwargs: dict = {}
    if os.name != "nt":
        kwargs["start_new_session"] = True       # 独立进程组，超时可整组杀
    proc = subprocess.Popen(
        cmd, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        text=True, encoding="utf-8", errors="replace", **kwargs,
    )
    try:
        stdout, stderr = proc.communicate(timeout=timeout)
        code = proc.returncode if proc.returncode is not None else -1
        return code, (stdout or "") + (stderr or ""), False
    except subprocess.TimeoutExpired:
        kill_tree(proc)
        try:
            # 回收管道给 5 秒：进程树虽已杀，但子进程可能把管道句柄留着（Windows 上尤其明显），
            # 等满 30 秒只是白等——判据（该步失败 + 日志尾）在杀进程时就已确定
            stdout, stderr = proc.communicate(timeout=5)
        except subprocess.TimeoutExpired:
            proc.kill()
            stdout, stderr = "", ""
        return -1, (stdout or "") + (stderr or ""), True


def run_step(step: dict, bash: str, wsl: bool, godot_shell: str, log_dir: Path) -> tuple[bool, Path]:
    slug = step["slug"]
    log_file = log_dir / f"{slug}.log"
    root_shell = to_shell_path(REPO_ROOT, wsl)
    timeout = STEP_TIMEOUT.get(slug, DEFAULT_TIMEOUT)

    if step["kind"] == "dotnet":
        code, output, timed_out = run_with_timeout(
            ["dotnet", "build", "--nologo"], timeout, cwd=str(REPO_ROOT)
        )
    else:
        # 两个环境变量在命令行内联赋值（而非继承）：
        #   GODOT           —— WSL 不继承 Windows 进程环境变量（除登记进 WSLENV 的），
        #                      父进程 set 会让引擎两步退化成 "godot: command not found"
        #   PYTHONUTF8/IOENCODING —— Git Bash 下 python3 是 Windows 版，stdio 默认按本机
        #                      ANSI 编码输出，中文门禁文案会变乱码；固定 UTF-8 使本地与 CI 一致
        # 解释器按后缀选：门禁发现面是 .sh 与 .py，两者同构运行。
        runner = "python3" if step["script"].endswith(".py") else "bash"
        command = f"cd '{root_shell}' && PYTHONUTF8=1 PYTHONIOENCODING=utf-8 "
        if step["godot"]:
            command += f"GODOT='{godot_shell}' "
        command += f"{runner} scripts/ci/{step['script']}"
        if step["godot"]:
            command += f" '{to_shell_path(log_dir / f'{slug}.engine.log', wsl)}'"
        code, output, timed_out = run_with_timeout([bash, "-c", command], timeout)

    if timed_out:
        tail = "\n".join(output.splitlines()[-30:])
        output += (
            f"\n[门禁] 步骤超时：{step['name']} 超过 {timeout}s 未退出，已杀掉进程树（含引擎/dotnet "
            f"子进程）——判该步失败，继续跑后续步骤。日志尾：\n{tail}\n"
        )

    log_file.write_text(output, encoding="utf-8")
    if output:
        print(output, end="" if output.endswith("\n") else "\n")
    print(f"   日志：{log_file}")
    return code == 0, log_file


def main() -> int:
    parser = argparse.ArgumentParser(
        description="InfiAir 本地门禁统一入口（口径见 AGENTS.md）",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument("--bash", default="", help="显式指定 bash（默认：Git Bash 优先，WSL 兜底）")
    parser.add_argument("--godot", default="", help="显式指定 Godot 可执行文件（默认：GODOT → PATH → 常见安装位置）")
    parser.add_argument("--only", default="",
                        help="只跑指定步骤，逗号分隔（例：smoke,ui_copy）。注意：只跑引擎步时不包含 build，"
                             "改了 C# 却没带上 build 会跑在旧程序集上——判绿也是旧代码的绿")
    parser.add_argument("--list", action="store_true", help="列出全部步骤 slug 后退出")
    parser.add_argument("--log-dir", default="",
                        help="门禁日志目录（默认系统临时目录下 infiair-gates/<仓库名>-<pid>，逐次运行独占）")
    args = parser.parse_args()

    if args.list:
        for step in STEPS:
            print(f"{step['slug']:<16} {step['name']}")
        return 0

    # 编码：交互式控制台交给 Python 的 Windows 控制台写入（UTF-16 直写，中文不受代码页影响）；
    # 重定向/管道时固定 UTF-8，使日志文件与 CI 捕获都是 UTF-8，不随本机 locale 漂移
    if not sys.stdout.isatty():
        try:
            sys.stdout.reconfigure(encoding="utf-8", errors="replace")  # type: ignore[union-attr]
        except (AttributeError, ValueError):
            pass
    try:  # 控制台编码不足以表示门禁输出字符时降级为替换，不因打印而中断
        sys.stdout.reconfigure(errors="replace")  # type: ignore[union-attr]
    except (AttributeError, ValueError):
        pass

    if not (REPO_ROOT / "AGENTS.md").exists():
        fail(f"仓库根定位失败：{REPO_ROOT}")

    selected = [s.strip() for s in args.only.split(",") if s.strip()]
    steps = STEPS
    if selected:
        known = {s["slug"] for s in STEPS}
        unknown = [s for s in selected if s not in known]
        if unknown:
            fail(f"未知步骤：{', '.join(unknown)}（用 --list 查看）")
        steps = tuple(s for s in STEPS if s["slug"] in selected)

    bash = find_bash(args.bash)
    wsl = is_wsl(bash)
    godot = find_godot(args.godot)
    godot_shell = to_shell_path(godot, wsl) if godot else ""
    # 日志目录逐次运行独占：默认目录若固定，同机并行的两份门禁（多个 worktree、或本地与编辑器
    # 里同时跑）会互相覆盖同名的步骤日志，判红的那一份可能被另一份的绿日志盖掉——汇总与日志
    # 自相矛盾，排查时先怀疑人生（实测撞到过：步骤日志全绿、汇总却 FAIL）。
    log_dir = (Path(args.log_dir) if args.log_dir
               else Path(tempfile.gettempdir()) / "infiair-gates" / f"{REPO_ROOT.name}-{os.getpid()}")
    log_dir.mkdir(parents=True, exist_ok=True)

    print(f"[门禁] 仓库：{REPO_ROOT}")
    print(f"[门禁] bash：{bash}（{'WSL：路径按 /mnt 挂载转换' if wsl else 'Git Bash/原生：路径按 /<盘符> 转换'}）")
    print(f"[门禁] Godot：{godot or '未找到（引擎两步将判失败；用 --godot 或 GODOT 指定）'}")
    missing = preflight(bash)
    if missing:
        fail(
            f"bash 侧缺少 {', '.join(missing)}：门禁给不出可信结论（取不到判据必须显式失败）。"
            "请安装缺失工具，或用 --bash 指向工具链完整的 bash（推荐 Git for Windows）。"
        )
    print(f"[门禁] 日志目录：{log_dir}")

    started = time.time()
    results: list[tuple[str, bool, Path]] = []
    for step in steps:
        print(f"\n== {step['name']}")
        if step["godot"] and not godot_shell:
            log_file = log_dir / f"{step['slug']}.log"
            log_file.write_text(
                "未找到 Godot 可执行文件：本步无法执行。请安装 Godot .NET 版，或用 --godot / GODOT 指定。\n",
                encoding="utf-8",
            )
            print(f"   ！！未执行（判失败）：未找到 Godot 可执行文件（日志：{log_file}）")
            results.append((step["name"], False, log_file))
            continue
        if step["kind"] == "dotnet" and not shutil.which("dotnet"):
            # 与 godot 同款预检：缺 dotnet 时 subprocess.run 抛 FileNotFoundError，
            # 栈回溯会中断整个 main()——build 之后的步骤一步都不跑、汇总表也不打。
            # 判失败并继续后续步骤，失败面与失败原因都可读（AGENTS §6 铁律 2）。
            log_file = log_dir / f"{step['slug']}.log"
            log_file.write_text(
                "未找到 dotnet：本步无法执行。请安装 .NET 8 SDK，或确认它在 PATH 上。\n",
                encoding="utf-8",
            )
            print(f"   ！！未执行（判失败）：未找到 dotnet 可执行文件（日志：{log_file}）")
            results.append((step["name"], False, log_file))
            continue
        ok, log_file = run_step(step, bash, wsl, godot_shell, log_dir)
        if not ok:
            print(f"   ！！失败：{step['name']}（日志：{log_file}）")
        results.append((step["name"], ok, log_file))

    elapsed = time.time() - started
    failed = [name for name, ok, _ in results if not ok]
    print("\n---------------- 门禁汇总 ----------------")
    for name, ok, log_file in results:
        print(f"  {'PASS' if ok else 'FAIL'}  {name}")
    if not failed:
        print(f"[门禁] 全部通过（{len(results)} 步，用时 {elapsed:.1f}s）")
        return 0
    print(f"[门禁] 失败 {len(failed)}/{len(results)} 步（用时 {elapsed:.1f}s）：{'、'.join(failed)}")
    for name, ok, log_file in results:
        if not ok:
            print(f"  失败：{name} —— 日志：{log_file}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
