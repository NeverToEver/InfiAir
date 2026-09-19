#!/usr/bin/env bash
# 离线工具链自测门禁：scripts/tests（数值管理器与分析引擎的纯 Python 测试）。
# 用法：bash scripts/ci/check_tool_tests.sh
# 为什么需要：scripts/tools/ 里的编辑器与分析面板是「改数值」这条路上的判定层——
# 形状校验失效会让界面上看不见的键悄悄写进 balance.json；行尾/数值形态处理回归会把整文件
# diff 翻掉；分析公式与 C# 分叉会让人照着另一套曲线调参。三者都不报错、只在文件与数字上坏，
# C# 构建与游戏冒烟都照常通过，故在此秒级拦住。
# 判定：unittest 退出码 + 用例数（>0）。只看退出码判不了「一个测试都没跑」——测试文件被删/
# 改名、或 discovery 起点写错时 unittest 同样 rc=0（假绿）。
# 破坏验证（本门禁能失败）：① 改 balance_analysis.milestone_threshold 的圈数乘区 →
# MilestoneCurveTests 三条红；② 删掉 balance_editor.check_shape 里的「未知键」分支 →
# CheckShapeTests.test_missing_and_unknown_keys_both_rejected 红。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

TESTS=scripts/tests
if [ ! -d "$TESTS" ]; then
    echo "::error::测试目录不存在：${TESTS}（路径已动须同步本门禁）"
    exit 1
fi

OUTPUT="$(python3 -m unittest discover -s "$TESTS" -t "$TESTS" 2>&1)"
rc=$?
printf '%s\n' "$OUTPUT"

RAN="$(printf '%s\n' "$OUTPUT" | sed -n 's/^Ran \([0-9][0-9]*\) test.*/\1/p' | tail -1)"
if [ -z "$RAN" ]; then
    echo "::error::取不到用例数（unittest 输出格式漂移？）——取不到判据必须显式失败，不能默认判绿"
    exit 1
fi
if [ "$RAN" -le 0 ]; then
    echo "::error::一个用例都没跑（Ran 0 tests）——用例被删/被注释/测试文件改名，拒绝判 clean"
    exit 1
fi
if [ "$rc" -ne 0 ]; then
    echo "::error::工具链自测有失败用例（退出码 ${rc}，共 ${RAN} 条，见上方失败详情）"
    exit 1
fi

echo "tool-tests: ${RAN} 条通过（scripts/tools 的编辑器与分析引擎）"
