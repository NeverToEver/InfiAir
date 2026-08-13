# Lifecycle, Input & Test Discipline
## Overview
场景生命周期顺序、输入映射、异步/协程纪律与测试纪律；适用于 `csharp/godot/` 下所有 C# 脚本。
## Rules
- `Setup()` 先于 `_Ready()`：`Setup()` 内勿依赖 `_Ready` 初始化的状态，子节点经 `GetNode<T>("path")` 获取。
- 输入映射（`project.godot`）：`move_up`/`boost`/`fine_move`/`dash`/`dock`/`homecoming`/`give_up`/`buff_panel`/`parry`/`restart`；摇杆运行时绑定 `GameState.BindJoypadDefaults()`/`SetJoyDeadzone()`，PS 检测 `GameState.IsPsGuid()`。
- Tutorial（`csharp/godot/Tutorial.cs`）隔离运行状态/存档：进入重置 run 并删除存档，永不读写 savegame；退出恢复 `Engine.TimeScale = 1`。
- 新增/改名 `.cs` 后运行 `dotnet build`（零警告门禁）并保留 `.cs.uid` 同文件——见 `.agents/csharp-conventions.md`。
- 异步纪律：见 `.agents/csharp-conventions.md` §Async。
- **测试只走 public 测试端口**（`SimulateTouch`/`SimulateDrag`/`SetTestState` 模拟输入/状态），禁写私有字段或直调 `_UnhandledInput`；注入的真实输入事件（`Input.ParseInputEvent`）的鼠标/触摸坐标在 headless 下经窗口→视口变换（不可移植）——见 `docs/TESTING.md` “Headless Test Environment Notes”。
