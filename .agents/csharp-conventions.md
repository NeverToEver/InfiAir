# C# Conventions

## Overview

渐进式 C# 混编工程约定(2026-08-07 决策,触发 `docs/C_SHARP_ASSESSMENT.md` §8 触发条件 3:团队语言构成变化)。存量 GDScript 不迁移;新模块/纯逻辑/数据模型/算法用 C#;热路径与场景绑定层禁止跨语言。适用于所有 `.cs` 文件及 GDScript↔C# 互操作代码。

## Directories & Namespaces

- `csharp/core/` → `InfiAir.Core`:纯 .NET 类库,零 Godot 依赖;新逻辑/数据模型/算法优先放这里,便于 xUnit 毫秒级单测。
- `csharp/godot/` → `InfiAir`:Godot 节点/场景绑定薄壳,可引用 Core;不得放纯逻辑。
- `tests-csharp/` → `InfiAir.Core.Tests`:xUnit 测试工程,只测 `InfiAir.Core`。

## Build & Gate

- `Directory.Build.props` 全局 `TreatWarningsAsErrors`;`dotnet build` 零警告是硬门禁(CI "Build & test C#" 步骤即执行此检查)。
- `dotnet test tests-csharp/ --nologo` 须全绿。
- `.editorconfig` 管 C# 风格(4 空格缩进、file-scoped namespace、max_line_length 140);`dotnet format --verify-no-changes` 为可选校验。
- 新增 `.cs` 前先确认它落在哪个 csproj 编译范围:主 `InfiAir.csproj` 显式排除 `csharp/core/**` 与 `tests-csharp/**`(分别由各自 csproj 编译),避免双编译。

## GDScript ↔ C# Boundary(最高优先级)

- 禁止跨语言继承:GDScript `class_name` 与 C# 类互不能继承(官方限制)。
- 互操作仅走引擎 API + 信号 + 动态派发:GDScript 侧 `load("res://csharp/godot/X.cs").new()` 后调实例方法;参数/返回值用 Godot 友好类型(`Godot.Collections.Dictionary`/`Array`),避免 `out` 参数。
- 热路径(对象池/弹幕/每帧循环)禁止跨语言调用。
- 每个新增 C# 绑定类须配 GDScript 断言场景(`test/*_test.tscn`)验证互操作。

## Language Choice

- 用 C#:纯逻辑/数据模型/算法/服务。
- 用 GDScript:场景绑定/表现/UI/快速迭代玩法。

## Test Layering

- 纯逻辑 → xUnit(`dotnet test`,毫秒级)。
- 场景/集成 → `test/*_test.tscn` 断言场景。
- 新增/移除断言场景须同步 `docs/TESTING.md` 的 Scene Counts(计数单一权威,禁止硬编码到其他文档;规则见 `.agents/doc-sync.md`)。

## Reference Sample

- 首个混编样板:`csharp/core/BalanceModels.cs`(balance.json 类型化模型)+ `csharp/godot/BalanceInterop.cs`(RefCounted 绑定壳)+ `test/csharp_interop_test.gd`(跨语言断言场景);新 C# 代码以此为参照。
