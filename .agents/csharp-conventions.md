# C# Conventions

## Overview

**全量迁移（2026-08-08 起，分支 `feature/csharp-full-migration`）**：用户指令反转"存量 GDScript 不迁移"边界——存量约 3.7 万行 GDScript 全量迁移 C#，终态零 GDScript、零 interop 壳、单一语言维护。实施计划见 `docs/C_SHARP_ASSESSMENT.md` §10 + 会话计划（M1–M7 里程碑，每批门禁全绿）。本文件为迁移期与终态的工程约定；旧"渐进式混编"边界（§GDScript ↔ C# Boundary）在 M7 前仍然适用（过渡态），M7 后删除并替换为纯 C# 约定。

## 全量迁移操作约定（2026-08-08 起，所有批次强制执行）

- **批次纪律**：批间串行（依赖），批内单系统一次到位（Moonjump 教训：避免同一系统长期双语言混写）；每批结束门禁全绿（见 §Build & Gate + 计划 §5）+ 更新 `docs/TESTING.md` 计数。
- **公共 API 冻结**：迁移期间 GameState facade 与各服务公开签名不变——GDScript 断言场景持续作回归，是"每批可测"的机制。
- **类名 = 文件名**（大小写敏感）；节点/Resource 类一律 `partial`（源生成器硬性要求）；一个 Godot 类一个文件，禁止跨文件 partial 拆类；命名空间 `InfiAir`（godot 层）/`InfiAir.Core.*`（core 层），避免"目录名==类名"冲突。
- **`.cs.uid` 必须入库**；改名/移动 .cs 连带移动 sidecar；批次完成后重存被触碰场景（补 uid）；`.gd`/`.gd.uid` 随迁移删除。
- **场景绑定**：`.tscn` 的 ext_resource 切到 `res://csharp/godot/X.cs`；实例化优先 `PackedScene.Instantiate<T>()`；C# 侧 `new X()` 在 NRT 下可能被视为可空（Godot 生成构造器），使用时 `!` 或判空。
- **Async（`csharp/godot/Coroutine.cs`）**：游戏内计时一律 `CreateTimer` + `ToSignal`，禁止裸 `Task.Delay`（线程池恢复，访问 Godot API 线程不安全）；挂起 await 无法取消 → 等待以 SceneTree 计时器兜底 + 恢复后 `GodotObject.IsInstanceValid` 判活；禁止裸 `async void` 生命周期（拆 `async Task` + try/catch）；await 段异常统一 try/catch。
- **信号**：C#↔C# 用 C# event（`+=`/`-=`，`_ExitTree` 配对断开——自定义信号不随接收方释放自动断开，弹幕"发射→命中→释放"链条高频触发 `ObjectDisposedException`）；跨语言用 `Connect(SignalName.X, Callable.From(...))`；`[Signal]` 委托名必须 `XxxEventHandler` 结尾；发射用 `EmitSignal(SignalName.X, ...)` 而非 `Invoke`。
- **热路径红线（每帧零托管分配）**：`_Process` 内禁 StringName/string 构造、`GetNodesInGroup`、LINQ、闭包捕获；属性缓存局部变量；用 `SignalName/MethodName/PropertyName` 常量；池显式进出（C# RefCounted 由 GC 延迟回收，不依赖引用计数）；跨语言调用禁止进入每帧热路径。
- **跨语言过渡（2026-08-08 M3a 实跑验证的规则）**：
  - **GDScript 不能以类名引用 C# 类**：类型注解（`: Bullet`）、标识符、静态成员按类名访问全部解析失败——被迁移类的所有 GDScript 类型注解改 untyped，调用一律走实例或脚本资源；
  - C# 实例方法/属性：PascalCase，GDScript 可经引用调用（属性 getter 已验证）；
  - C# **静态方法**：`load("res://csharp/godot/X.cs").Method(args)` 可调（已验证）；**静态属性/常量经脚本资源不可访问**（实测 "Invalid access"）→ 需转静态方法（如 `GetCollisionRadius()`）或实例访问器；
  - `load("...cs").new()` 返回无类型 Variant，GDScript 赋值禁用 `:=` 推断；
  - C#→GDScript 仅动态派发 `Call()/Get()/Set()`（snake_case 名；调用点注释"随批次 X 重定型"）；跨语言互不消费 async（边界一律信号 + `ToSignal`）。
- **GDScript 调用点适配**：被迁移类的 class_name 类型注解在 GDScript 调用点改 untyped（如 `: Starfield` → `:=`），snake_case 动态调用改 PascalCase（`origin()` → `Origin()`）；随调用方自身迁移批次重定型。
- **新 C# 测试/断言**：纯逻辑 → `tests-csharp/` xUnit；场景级断言 → C# 脚本化断言场景（Node + `_Ready` 断言 + `GetTree().Quit(failures)` + 入口 try/catch 保证异常也非零退出）。

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
> **落地状态(2026-08-07)**:P0-1/P0-2/P1-1/P1-2/P1-3 已全部落地(逐条见下);P2-1 触发条件未满足(资产加载分散但规模小),维持待启动。

### P0 — 高优先价值(近期落地)✅ 已落地(2026-08-07)

- **P0-1 SaveManager → C#**(`InfiAir.Core.Storage.SaveStore` + `csharp/godot/SaveStoreInterop.cs` 薄壳)✅
  - 迁移:原子写(临时文件 + rename 回退)、损坏隔离(.corrupt + `last_was_corrupt`)、JSON 序列化(System.Text.Json)
  - 接入:GameState 转发目标换 C# 壳,公开签名不变;存档数据模型仍由 GameState 组装(`scripts/save_manager.gd` 薄壳,类名/API 不变)
  - 验证:xUnit(原子替换/回退路径/损坏隔离)+ `base_system_test` + interop 断言场景(`test/save_store_interop_test.tscn`)
- **P0-2 UserDb 数据层 → C#**(`InfiAir.Core.Storage.UserDb` + `csharp/godot/UserDbInterop.cs` 薄壳)✅
  - 迁移:用户 CRUD/登录记录/本地排行榜/名称校验;**密码派生逐字节等价迁移**(自建 PBKDF2 变体,System.Security.Cryptography 对齐)+ 既有账号兼容测试(固定向量对照 `tests-csharp/UserDbPasswordTests.cs`)
  - 接入:GameState 转发换壳(`scripts/user_db.gd` 薄壳,公开 API 不变);`iterations` 测试降档机制保留
  - 验证:xUnit(CRUD/校验/榜单/损坏回退/密码向量)+ 账户断言场景(`user_db_test`/`user_session_test`/`welcome_flow_test` 原样通过)+ interop(`test/user_db_interop_test.tscn`,含存量 GDScript 账号固定向量验密)
  - 约束:密码算法"保持实现不动以免破坏既有账号"注释口径已随迁移重写并配套固定向量兼容测试

### P1 — 中价值(按节奏推进)✅ 已落地(2026-08-07)

- **P1-1 BalanceService 点路径解析核心 → C#**(`InfiAir.Core.Config.PathResolver` 纯函数 + `csharp/godot/PathResolverInterop.cs` 薄壳)✅
  - GDScript 壳保留 `cfg()` 签名转发 → 469 处调用点不变 → BALANCE_MAP 生成器(M8)零影响
  - 验证:xUnit(`tests-csharp/PathResolverTests.cs`)+ M8 零 diff + `balance_test` + interop(`test/path_resolver_interop_test.tscn`)
  - 注:`PathResolver.Resolve` 的 int 结果须用 if/else 分支返回(C# 三元会因 long→double 隐式拓宽把整型统一装箱成 double)
- **P1-2 进程曲线核心 → C#**(`InfiAir.Core.Progression.MilestoneCurve` + `DifficultyCurve` 纯函数 + `csharp/godot/ProgressionInterop.cs` 薄壳)✅
  - 迁移:里程碑阈值曲线(8 档基础 × cycle_mult^cycle × 难度倍率)与难度进程曲线;apply_run_save 的 while 推进(上限 10000 档)换 `CountThresholdsUpTo` 单次调用 + O(1)/档 增量推进——存档恢复不再逐档跨语言往返
  - 逐位等价:累加顺序/Math.Pow 调用/roundf half-away-from-zero(经 Math.Round(AwayFromZero))与原 GDScript 一致;极大 index 显式钳制防 int64 溢出 UB(原实现 UB 无契约)
  - 接入:GameState.milestone_threshold/_recompute_difficulty 转发,公开签名不变
  - 验证:xUnit(`tests-csharp/ProgressionCurvesTests.cs`)+ `difficulty_test`/`balance_test` + interop(`test/progression_interop_test.tscn`)
- **P1-3 任务池抽取核心 → C#**(`InfiAir.Core.Missions.TaskPool` 纯逻辑 + `csharp/godot/TaskPoolInterop.cs` 薄壳)✅
  - 迁移:洗牌游标无放回抽取(排除项/跨批补足/全池排除安全空);RNG 独立于 GDScript 全局随机源(性质等价、序列不等价——无外部依赖具体序列)
  - 接入:`scripts/task_pool.gd` 薄壳转发,公开签名不变
  - 验证:xUnit(`tests-csharp/TaskPoolTests.cs`)+ `base_task_refresh_test` + interop(`test/task_pool_interop_test.tscn`)

### P2 — 新能力(按内容规模需求启动)

- **P2-1 AssetCatalog 资产资源管理**(`InfiAir.Core.Assets` 新服务):清单驱动(asset manifest)的加载/缓存/引用计数/卸载策略;触发条件 = 资产加载分散管理成本上升(当前 preload 分散但规模小,暂不做)
- **明确不做**:EventManager 迁移(与 fog 效果层耦合深)、DDA 迁移(调用点深)、任何热路径迁移(边界禁令)

### 着陆点通用规则

- 每个着陆点 = `InfiAir.Core` 纯逻辑 + `csharp/godot` 薄壳 + xUnit + 对应断言场景(参照 Reference Sample)
- 行为等价是硬要求:既有数据文件/账号/配置语义不得破坏;涉及既有数据兼容的迁移必须配固定向量测试
