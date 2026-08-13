# C# Conventions

## Overview
终态纯 C# 工程（零 GDScript，CI 零 GDScript 闸强制；迁移决策依据 `docs/archive/2026-08-08-csharp-assessment.md` §10）。`csharp/godot/*Interop.cs` 为 InfiAir.Core 的 C# 绑定端点，非 GDScript 壳。
## 工程红线
- **类名 = 文件名**（大小写敏感）；节点/Resource 类一律 `partial`（源生成器硬性要求）；一个 Godot 类一个文件，禁止跨文件 partial 拆类（例外: GameState 按域、过场按镜头、AutoplayTest 测试探针按职责拆 partial）；命名空间 `InfiAir`（godot 层）/`InfiAir.Core.*`（core 层），避免"目录名==类名"冲突。
- **`.cs.uid` 必须入库**；改名/移动 .cs 连带移动 sidecar；触碰场景后重存（补 uid）。
- **场景绑定**：`.tscn` 的 ext_resource 指向 `res://csharp/godot/X.cs`；实例化优先 `PackedScene.Instantiate<T>()`；C# 侧 `new X()` 在 NRT 下可能被视为可空（Godot 生成构造器），使用时 `!` 或判空。
- **信号**：C#↔C# 用 C# event（`+=`/`-=`，`_ExitTree` 配对断开——自定义信号不随接收方释放自动断开，弹幕高频链路曾触发 `ObjectDisposedException`）；引擎信号/动态连接用 `Connect(SignalName.X, Callable.From(...))`；`[Signal]` 委托名必须 `XxxEventHandler` 结尾；发射用 `EmitSignal(SignalName.X, ...)` 而非 `Invoke`。
- **热路径红线（每帧零托管分配）**：`_Process` 内禁 StringName/string 构造、`GetNodesInGroup`、LINQ、闭包捕获；属性缓存局部变量；用 `SignalName/MethodName/PropertyName` 常量；池显式进出（C# RefCounted 由 GC 延迟回收，不依赖引用计数）。
## Async（`csharp/godot/Coroutine.cs`）
游戏内计时一律 `SceneTree.CreateTimer` + `ToSignal`（或直接调 `Coroutine.WaitSeconds`/`WaitPhysicsFrames`/`WaitSignal` 封装），禁止裸 `Task.Delay`（线程池恢复，访问 Godot API 线程不安全）；挂起 await 无法取消 → 等待以 SceneTree 计时器兜底 + 恢复后 `GodotObject.IsInstanceValid` 判活；禁止裸 `async void` 生命周期（拆 `async Task` + try/catch）；await 段异常统一 try/catch。
## Directories & Namespaces
- `csharp/core/` → `InfiAir.Core`：纯 .NET 类库，零 Godot 依赖；新逻辑/数据模型/算法/服务优先放这里，便于 xUnit 毫秒级单测。
- `csharp/godot/` → `InfiAir`：Godot 节点/场景绑定/表现/UI/玩法编排——可引用 Core；不得放纯逻辑。
- `csharp/godot/tests/`：场景断言脚本（驱动 `test/*_test.tscn`）。
- `tests-csharp/` → `InfiAir.Core.Tests`：xUnit 测试工程，只测 `InfiAir.Core`。
## Build & Gate
- `Directory.Build.props` 全局 `TreatWarningsAsErrors`；`dotnet build` 零警告是硬门禁（CI "Build & test C#" 步骤即执行此检查）。
- `dotnet test tests-csharp/ --nologo` 须全绿。
- `.editorconfig` 管 C# 风格（4 空格缩进、file-scoped namespace、max_line_length 140）；`dotnet format --verify-no-changes` 三工程零 diff 是 CI 硬门禁。
- CI 零 GDScript 闸：任何新增 `.gd` 文件即失败——禁止回归 GDScript。
- 新增 `.cs` 前先确认它落在哪个 csproj 编译范围：主 `InfiAir.csproj` 显式排除 `csharp/core/**` 与 `tests-csharp/**`（分别由各自 csproj 编译），避免双编译。
- **静态审查工具**：Roslynator CLI 留存于 `tools/roslynator/`（`tools/` 已 gitignore 不入库；完整工具链 `dotnet tool install --tool-path tools/roslynator roslynator.dotnet.cli` 重建），运行需 `dotnet` 在 PATH 且 `DOTNET_ROOT=~/.dotnet`（`~/.dotnet` 为官方 dotnet-install.sh 默认安装目录；Roslynator 经 PATH 启动 dotnet MSBuild host，缺 PATH 时 ForkAndExecProcess 报 No such file or directory）。`roslynator analyze <csproj>` 报告为 info 级建议、非 CI 门禁；应用口径（AA 系列）：CA1854/1846/1866/1869/1861/1859 安全子集可落地，**CA1822（标 static）不应用**——Godot 场景/信号按名连接对 static 方法有运行期解析风险。
## Layer Boundary（最高优先级）
- 分层边界：`InfiAir.Core`（`csharp/core/`）零 Godot 依赖，纯逻辑/数据模型/算法只能放这里；`InfiAir`（`csharp/godot/`）可引用 Core，不得把纯逻辑写进 Godot 壳。
- `csharp/godot/*Interop.cs` 是 InfiAir.Core 类型的 Godot 绑定端点（RefCounted/Node 壳），供场景/测试经引擎 API 触达 Core。
- 热路径（对象池/弹幕/每帧循环）遵守每帧零托管分配红线（见 §工程红线）；新增 Core 能力须配 xUnit，场景级行为配 `test/*_test.tscn` 断言场景。
## Test Layering
- 纯逻辑 → xUnit（`dotnet test`，毫秒级）；场景/集成 → C# 脚本化断言场景：`csharp/godot/tests/*.cs` 驱动 `test/*_test.tscn`（Node + `_Ready` 断言 + `TestExit.Quit(failures)` + 入口 try/catch 保证异常也非零退出）；新增/移除断言场景须同步 `docs/TESTING.md` 的 Scene Counts（计数单一权威，禁止硬编码到其他文档；规则见 `.agents/doc-sync.md`）。
## Reference Sample
首个 Core 样板: `csharp/core/BalanceModels.cs` + `csharp/godot/BalanceInterop.cs` + `test/csharp_interop_test.tscn`，新 Core 代码以此为参照。
