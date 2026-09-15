#!/usr/bin/env bash
# core 层单测门禁：csharp/tests/InfiAir.Core.Tests（零 Godot 依赖的纯逻辑回归面）。
# 用法：bash scripts/ci/check_unit_tests.sh
# 为什么需要：SaveStore/PathResolver/StickShaper/TalentEconomy/进程曲线承载存档判型、
# 配置回退、输入整形与经济契约——引擎冒烟跑不到这些边界（损坏档判型、死区零向量、
# 曲线溢出钳制），写坏时编译与 300 帧冒烟都不报警，这里秒级先炸。
# 判定：dotnet test 退出码 + TRX 结果文件里的通过数。只看退出码判不了「一个测试都没跑」——
# 测试工程里全被注释掉/改名、或筛选器把用例全滤掉时，dotnet test 同样 rc=0（假绿）。
# TRX 是 XML，不受本机语言与日志格式影响；结果文件缺失/解析失败一律判红（取不到判据必须显式失败）。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

PROJECT=csharp/tests/InfiAir.Core.Tests
if [ ! -d "$PROJECT" ]; then
    echo "::error::测试工程不存在：$PROJECT（路径已动须同步本门禁）"
    exit 1
fi

RESULTS="$(mktemp -d)"
trap 'rm -rf "$RESULTS"' EXIT

dotnet test "$PROJECT" --nologo --logger "trx;LogFileName=core.trx" --results-directory "$RESULTS"
rc=$?

TRX="$RESULTS/core.trx"
if [ ! -f "$TRX" ]; then
    echo "::error::未生成 TRX 结果文件（$TRX）——dotnet test 输出格式或参数漂移？门禁需同步"
    exit 1
fi

# Python 是宿主原生的：Git Bash 的 /tmp 路径它读不到，交给 cygpath 转成宿主路径（Linux 上无此命令，按原样用）
TRX_PATH="$TRX"
if command -v cygpath >/dev/null 2>&1; then
    TRX_PATH="$(cygpath -w "$TRX")"
fi

python3 - "$TRX_PATH" <<'PY'
import sys
import xml.etree.ElementTree as ET

path = sys.argv[1]
try:
    root = ET.parse(path).getroot()
    summary = next(e for e in root.iter() if e.tag.rsplit("}", 1)[-1] == "ResultSummary")
    counters = next(c for c in summary if c.tag.rsplit("}", 1)[-1] == "Counters")
    total, executed, passed = (int(counters.get(k, -1)) for k in ("total", "executed", "passed"))
except (ET.ParseError, AttributeError, TypeError, ValueError, StopIteration, OSError):
    print(f"::error::无法从 TRX 解析通过数（{path}）——结构漂移或路径不可读？门禁需同步")
    sys.exit(1)

if total <= 0:
    print("::error::测试工程里一个用例都没有（total=0）——用例被删/被注释/被筛选器滤空，拒绝判 clean")
    sys.exit(1)
if passed <= 0:
    print(f"::error::通过的用例数为 0（total={total} / executed={executed} / passed={passed}）"
          "——dotnet test 在此情况下仍可能 rc=0，不能只看退出码")
    sys.exit(1)
print(f"unit-tests: {passed}/{total} 通过（executed={executed}）")
PY
judge=$?

if [ "$rc" -ne 0 ]; then
    echo "::error::dotnet test 退出码 $rc（失败用例见上方输出；通过数见上行）"
    exit 1
fi
exit "$judge"
