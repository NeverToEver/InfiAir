# AGENTS.md

## 项目

InfiAir：单人 2D 纵版射击游戏。Godot 4.6.2 + C#（.NET 8，gl_compatibility），全量 C#、零 GDScript，必须用 .NET 版引擎（godot-mono）。

玩法循环：自动射击 + 波次刷怪 → 里程碑三选一 buff → 4 个轮换 Boss + 狂暴 → 母舰补给/火力平台 → 中途返城补给，同一局持续进行。只有分数结算，没有掉落拾取。

- 入口场景 `scenes/welcome.tscn`（账户/难度/教程/设置/本地排行榜），战斗场景 `scenes/main.tscn` 由测试显式实例化。视口 1920×1080，stretch `canvas_items` + aspect `keep`。本地运行 `./run.sh`。
- 唯一 autoload 是 `GameState`（`csharp/godot/GameState.cs`），各域服务的编排门面；C# 代码统一经 `GameState.Instance` 访问。
- UI 文本中英双语，默认中文。所有可见文本走 `Tr("UPPER_SNAKE_CASE")`，新 key 同时填 `data/translations.csv` 的 zh/en 两列并重新导入。

## 目录

- `csharp/core/` → 命名空间 `InfiAir.Core`：纯 .NET 类库，零 Godot 依赖。数据模型、纯逻辑、算法只放这里，并配 xUnit 测试（`tests-csharp/`，只测 Core）。
- `csharp/godot/` → 命名空间 `InfiAir`：全部运行时代码（节点、场景绑定、UI、玩法编排），可引用 Core；`*Interop.cs` 是 Core 类型的 Godot 绑定端点。
- 数值调参只改 `data/balance.json`，读取走 `GameState.Cfg("path", def)`（需范围校验的标量用 `CfgFx`）。代码里的兜底默认值必须与 json 定稿值一致。改完跑 `python3 scripts/tools/gen_balance_map.py` 重新生成 `docs/BALANCE_MAP.md`（生成文件禁手改）。

## 验证命令

改动后按需跑，全绿才算完：

```bash
dotnet build                                                     # 零警告是硬门禁（TreatWarningsAsErrors）
dotnet test tests-csharp/
dotnet format --verify-no-changes                                # 三个 csproj
godot-mono --headless --import --path .                          # 不许出现引擎警告
godot-mono --headless --path . --quit-after 300                  # 300 帧运行检查
godot-mono --headless --path . res://test/smoke_test.tscn        # 主流程冒烟，改动后必跑
godot-mono --headless --path . res://test/base_system_test.tscn  # 触碰存档/基地/母舰时加跑
```

断言场景只保留 `smoke_test` 与 `base_system_test` 两个。新的行为测试优先写成 xUnit 纯逻辑测试；确需新增断言场景，先在 `docs/TESTING.md` 的 Scene Counts 登记（该文件是场景计数的唯一权威，其他文档不得硬编码计数）。测试只走公开测试端口（`SimulateTouch`/`SimulateDrag`/`SetTestState` 等），禁止直调私有方法或 `_UnhandledInput`。

## 硬性约定

- 类名必须等于文件名（大小写敏感）。Godot 节点/Resource 类一律 `partial`，一个类一个文件；GameState 按域拆 partial 是唯一例外。
- `.cs.uid` sidecar 必须入库；改名或移动 `.cs` 时连带移动。
- C# 类型之间的通信一律用 C# event（`+=` 订阅、`_ExitTree` 配对退订）；引擎信号用 `Connect(SignalName.X, Callable.From(...))` 连接、`EmitSignal(SignalName.X, ...)` 发射。
- 游戏内计时一律走 `SceneTree.CreateTimer` + `ToSignal`（或 `Coroutine.WaitSeconds` 等封装），禁止裸 `Task.Delay` 与裸 `async void`；await 段 try/catch，恢复后用 `IsInstanceValid` 判活。
- `_Process`/`_PhysicsProcess` 热路径零托管分配：不构造 string/StringName、不用 LINQ、不逐帧 `GetNodesInGroup`（改用 `GameState.Enemies`/`PlayerRef` 等注册表）、引用 `SignalName`/`MethodName` 常量。
- 碰撞层：1=player、2=player_bullet、3=enemy（含 boss）、4=enemy_bullet。玩家只能经 `Player/Hitbox` Area2D 受击；命中解析按组（玩家弹对 `enemy` 组，敌弹/敌实体对 `player_hitbox` 组）。
- 相机固定在 (960,540)，只缩放不平移。一切屏幕空间/可见性/生成范围计算用 `GameState.ViewWorldRect()`，禁止硬编码 0..1920/0..1080。
- `world_scale = 0.4` 是唯一缩放杆：设计值 × world_scale 得运行值，玩法范围/UI/过场不缩放。共享 Shape2D 禁止 `*=` 累乘；运行时改尺寸需 `resource_local_to_scene = true`。
- 子弹与波次敌机走 `GameState.BulletPool`/`EnemyPool`，formation 敌机直建直毁。禁止对池化实体直接 `QueueFree` 或绕开池复用。
- UI 样式统一走 `csharp/godot/UITheme.cs` 的工厂方法，新页面用 `MakePageShell`；暂停类 UI（buff/暂停/结算）需 `ProcessMode = Always`；返回/退出统一由 `BackNavigator` 路由，页面不自行消费 `ui_cancel`。
- 持久化只写 `user://`，存档 per-user 并做 owner 校验；损坏 JSON 隔离为 `.corrupt` 文件并在开始屏提示，禁止静默丢弃用户数据。无网络、无凭据、无第三方运行时依赖。

## 文档与流程

- 深入参考：`docs/ARCHITECTURE.md`（场景树与逐脚本职责）、`docs/TESTING.md`（测试策略）、`docs/ROADMAP.md`（方向与决策）、`docs/DESIGN_BASELINE.md`（设计基线，修订唯一入口）。
- 完成的计划/评审文档全文移入 `docs/archive/`，并在 `docs/EXECUTION_LOG.md` 登记。
- **`docs/AUDIT_VAULT.md` 是专有审计档案：永不删除、永不合并，只追加新发现与修复回填。改动核心逻辑前先查它。**
- CI 是单 job fast-gate（C# build+test → import 警告 → 300 帧冒烟 + smoke_test），只允许官方 action 与 Godot 官方引擎/导出模板。
- Roslynator（`tools/roslynator/`，已 gitignore）只做 info 级参考，非门禁；其中 CA1822（标 static）不要应用——Godot 场景/信号按名字连接方法，static 化有运行期解析风险。
