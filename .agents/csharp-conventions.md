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

## Landing Plan(着陆点路线图,2026-08-07 评估)

> 目标:C# 承担"更清晰的资源管理和性能调度"。评估矩阵与依据:候选模块对照纯逻辑/可单测/非热路径/边界清晰四判据(详见 `docs/C_SHARP_ASSESSMENT.md` §7 边界)。**登记于 ROADMAP Decisions 2026-08-07 条目**;实现是独立批次,本路线图只定方向与约束。

### P0 — 高优先价值(近期落地)

- **P0-1 SaveManager → C#**(`InfiAir.Core.Storage.SaveStore` + `csharp/godot` 薄壳)
  - 迁移:原子写(临时文件 + rename 回退)、损坏隔离(.corrupt + `last_was_corrupt`)、JSON 序列化(System.Text.Json)
  - 接入:GameState 转发目标换 C# 壳,公开签名不变;存档数据模型仍由 GameState 组装
  - 验证:xUnit(原子替换/回退路径/损坏隔离)+ `base_system_test` + interop 断言场景
- **P0-2 UserDb 数据层 → C#**(`InfiAir.Core.Storage.UserDb` + 薄壳)
  - 迁移:用户 CRUD/登录记录/本地排行榜/名称校验;**密码派生逐字节等价迁移**(自建 PBKDF2 变体,System.Security.Cryptography 对齐)+ 既有账号兼容测试(固定向量对照)
  - 接入:GameState 转发换壳;`iterations` 测试降档机制保留
  - 验证:xUnit(CRUD/校验/榜单/损坏回退/密码向量)+ 账户断言场景 + interop
  - 约束:密码算法"保持实现不动以免破坏既有账号"注释口径必须随迁移重写并配套兼容测试

### P1 — 中价值(按节奏推进)

- **P1-1 BalanceService 点路径解析核心 → C#**(`InfiAir.Core.Config.PathResolver` 纯函数)
  - GDScript 壳保留 `cfg()` 签名转发 → 469 处调用点不变 → BALANCE_MAP 生成器(M8)零影响
  - 验证:xUnit + M8 零 diff + `balance_test`

### P2 — 新能力(按内容规模需求启动)

- **P2-1 AssetCatalog 资产资源管理**(`InfiAir.Core.Assets` 新服务):清单驱动(asset manifest)的加载/缓存/引用计数/卸载策略;触发条件 = 资产加载分散管理成本上升(当前 preload 分散但规模小,暂不做)
- **明确不做**:EventManager 迁移(与 fog 效果层耦合深)、DDA 迁移(调用点深)、任何热路径迁移(边界禁令)

### 着陆点通用规则

- 每个着陆点 = `InfiAir.Core` 纯逻辑 + `csharp/godot` 薄壳 + xUnit + 对应断言场景(参照 Reference Sample)
- 行为等价是硬要求:既有数据文件/账号/配置语义不得破坏;涉及既有数据兼容的迁移必须配固定向量测试
