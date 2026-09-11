#!/usr/bin/env bash
# core 层单测门禁：csharp/tests/InfiAir.Core.Tests（零 Godot 依赖的纯逻辑回归面）。
# 用法：bash scripts/ci/check_unit_tests.sh
# 为什么需要：SaveStore/PathResolver/StickShaper/TalentEconomy/进程曲线承载存档判型、
# 配置回退、输入整形与经济契约——引擎冒烟跑不到这些边界（损坏档判型、死区零向量、
# 曲线溢出钳制），写坏时编译与 300 帧冒烟都不报警，这里秒级先炸。
# 判定：dotnet test 退出码。测试工程不入 InfiAir.sln——游戏构建与打包不耦合测试工具链。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

exec dotnet test csharp/tests/InfiAir.Core.Tests --nologo
