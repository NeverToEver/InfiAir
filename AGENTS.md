# AGENTS.md

工程纪律单一权威（约定/门禁/流程都在这里，别处不再重复）。设计数值与规则见 `docs/DESIGN_BASELINE.md`，方向决策与已知债务见 `docs/ROADMAP.md`，测试命令与场景计数见 `docs/TESTING.md`。

## 项目

InfiAir：单人 2D 俯视射击（top-down shmup；玩家全屏自由移动 + 鼠标/触屏瞄准，非底部锁定纵版）。Godot 4.6.2 + C#（.NET 8，gl_compatibility，必须用 godot-mono），全量 C#、零 GDScript。

玩法循环：自动射击 + 波次刷怪 → 里程碑/Boss 掉天赋点入缓存池（不弹窗）→ 天赋面板（G 键/HUD 指示器）自主加点 → 4 个轮换 Boss + 狂暴 → 母舰补给/火力平台 → 中途返城补给，同一局持续进行，无尽必死曲线，只有分数结算（无掉落拾取）。

- 入口场景 `scenes/welcome.tscn`（账户/难度/教程/设置/排行榜），战斗场景 `scenes/main.tscn` 由测试显式实例化。视口 1920×1080，stretch `canvas_items` + aspect `keep`。本地运行 `./run.sh`（Windows `run.bat`，macOS 双击 `run.command`；三者同一参数协议，透传引擎参数）。
- 唯一 autoload 是 `GameState`（`csharp/godot/GameState*.cs` 按域拆 partial），各域服务编排门面，C# 统一经 `GameState.Instance` 访问。
- main 场景树速览：`Starfield / Camera2D / Player / Spawner / BulletPool / EnemyPool / HUD / BuffUI / PauseUI / SettingsUI / GameOverUI / BaseUI / ExitConfirm / BackNavigator / MouseTrap / VirtualControls / MetaHealthFX / AimFrameLayer / IntroCinematic / ReturnCinematic / OrbitalStrike / MothershipSummonWindow / EliteTurretEvent / FormationStrikeEvent`。动态运行时实体一律挂在 Main 下。
- UI 文本中英双语，默认中文。所有可见文本走 `Tr("UPPER_SNAKE_CASE")`，新 key 同时填 `data/translations.csv` zh/en 两列并重新导入。禁止硬编码中文可见文本。

## 目录

- `csharp/core/` → `InfiAir.Core`：纯 .NET 类库，零 Godot 依赖。数据模型/纯逻辑/算法只放这里，配 xUnit 测试（`tests-csharp/`，只测 Core）。
- `csharp/godot/` → `InfiAir`：全部运行时代码（节点/场景绑定/UI/玩法编排），可引用 Core；`*Interop.cs` 是 Core 类型的绑定端点。
- `data/balance.json`：数值唯一来源。调参只改它；代码兜底默认值必须与 json 定稿值一致；改动后跑 `python3 scripts/tools/gen_balance_map.py` 重新生成 `docs/BALANCE_MAP.md`（生成物禁手改；仅键增删/改名需要重跑，普通重构不用）。

## 验证

改动后按需跑；提交前至少跑「最小循环」。CI 是单 job fast-gate（build+test → import 警告 → 300 帧 + smoke），只允许官方 action 与官方引擎/导出模板。

```bash
# 最小循环
dotnet build                                                     # 零警告硬门禁（TreatWarningsAsErrors）
dotnet test tests-csharp/
godot-mono --headless --import --path .                          # 不许出现引擎警告
godot-mono --headless --path . res://test/smoke_test.tscn        # 主流程冒烟
# 按需
godot-mono --headless --path . --quit-after 300                  # 300 帧运行检查（CI 必跑）
godot-mono --headless --path . res://test/base_system_test.tscn  # 触碰存档/基地/母舰时
dotnet format --verify-no-changes                                # 本地提交前；裸跑有工作区歧义，显式指定 csproj
```

提交信息风格：`类型: 简述——要点列表`（类型取 fix/feat/docs/test/refactor/perf/chore；单主题提交），对齐 `git log` 现行风格；分支/PR 清单/发布流程见 `CONTRIBUTING.md`（人类贡献者入口）。

**无头门禁看不见渲染画面**（结构盲区：welcome 页布局腐烂一个月无人发现即为例证）。任何 UI/视觉/布局改动必须窗口化实机过目再交付；截图工具清单见 `docs/TESTING.md`。

## 硬性约定（代码）

- 类名必须等于文件名（大小写敏感）；Godot 节点/Resource 类一律 `partial`，一个类一个文件；GameState 按域拆 partial 是唯一例外。`.cs.uid` sidecar 入库，随 `.cs` 连带移动。
- C# 类型间通信用 C# event（`+=` 订阅、`_ExitTree` 配对退订、重连前 `IsConnected` 守卫）；引擎信号用 `Connect(SignalName.X, Callable.From(...))` / `EmitSignal(SignalName.X, ...)`。
- 计时一律 `SceneTree.CreateTimer` + `ToSignal`（或 `Coroutine.WaitSeconds` 等封装：await 段 try/catch、恢复后 `IsInstanceValid` 判活）；长生命周期对象的一次性延时用 Timer 节点 + Timeout（`Spawner.Schedule` 先例）。禁裸 `Task.Delay`、裸 `async void`、`await ProcessFrame` 后不判活。
- `_Process`/`_PhysicsProcess` 热路径零托管分配：不构造 string/StringName、不用 LINQ、不逐帧 `GetNodesInGroup`（用 `GameState.Enemies`/`PlayerRef` 等注册表）、引用 `SignalName`/`MethodName` 常量；三角函数用 `Enemy.SinFast()/CosFast()` 查表；`Time.GetTicksMsec()` 每帧取一次；`Cfg()` 结果在 `_Ready()/Setup()` 缓存，禁止每帧查。
- `Setup()` 先于 `_Ready()`（池化实体约定）；节点引用用 `GetNode<T>("path")`；集合用 `Godot.Collections.Array<T>` 等 typed 形态，不裸 `Variant`/`GodotObject`。
- 碰撞层：1=player、2=player_bullet、3=enemy（含 boss）、4=enemy_bullet。玩家只能经 `Player/Hitbox` Area2D 受击；命中解析按组（玩家弹对 `enemy` 组，敌弹/敌实体对 `player_hitbox` 组）。
- 相机固定 (960,540) 只缩放不平移；一切屏幕空间/可见性/生成范围计算走 `GameState.ViewWorldRect()`，禁止硬编码 0..1920/0..1080（过场演出用 1920×1080 设计坐标是有意例外）。
- `world_scale = 0.4` 是唯一缩放杆：Hull 尺寸族（贴图/碰撞半径/枪口等偏移）设计值 × world_scale，赋值必须幂等（`radius = design × ws`），共享 Shape2D 禁 `*=` 累乘，运行时改尺寸需 `resource_local_to_scene = true`；玩法范围/UI/过场不缩放（`mothership.drive.margin_*` × ws 是有意例外）。
- 子弹与波次敌机走 `GameState.BulletPool`/`EnemyPool`，formation 敌机直建直毁；禁止对池化实体直接 `QueueFree` 或绕开池复用。
- UI 样式统一走 `csharp/godot/UITheme.cs` 工厂方法，新页面用 `MakePageShell`；暂停类 UI 需 `ProcessMode = Always`；返回/退出统一 `BackNavigator` 路由，页面不自行消费 `ui_cancel`。
- 持久化只写 `user://`，存档 per-user + owner 校验；损坏 JSON 隔离为 `.corrupt` 并在开始屏提示，禁止静默丢弃用户数据。无网络/无凭据/无第三方运行时依赖。
- **注释只写代码本身表达不了的约束**（为什么这样、防什么坑），不写流水账、不署名、不带审计轮次编号（`L01`/`AC8`/`W4` 式标签废止——存量注释不必清理，但不再新增）。

## 测试

- 行为测试优先写 xUnit 纯逻辑测试（`tests-csharp/`）；断言场景只保留 `smoke_test` + `base_system_test`，确需新增先在 `docs/TESTING.md` 的 Scene Counts 登记（该文件是场景计数唯一权威）。
- 测试只走公开测试端口（如 `SetMilestoneOverride`/`TestExit`），禁止直调私有方法或 `_UnhandledInput`；测试碰 `user://` 存档前先 `DeleteSave()` 并自清理。
- Roslynator（`tools/roslynator/`，已 gitignore）只做 info 级参考，非门禁；其中 CA1822（标 static）不要应用——Godot 场景/信号按名字连接方法，static 化有运行期解析风险。

## 文档（只保留 5 份活文档）

| 文件 | 唯一职责 |
| --- | --- |
| `AGENTS.md`（本文件） | 工程纪律：约定/门禁/流程 |
| `docs/DESIGN_BASELINE.md` | 玩法设计意图：数值/规则/系统边界 |
| `docs/ROADMAP.md` | 方向决策 + 已知债务与开放发现 |
| `docs/TESTING.md` | 测试命令/场景计数/截图工具 |
| `docs/BALANCE_MAP.md` | 生成物：数值键索引（禁手改） |

纪律：

1. **不为"行为"写规格文档。** 行为以代码与测试为准。历史行为规格（Boss/事件/演出/管理器/架构速览等 13 份）已归档 `docs/archive/`——它们曾需要专门的纠偏轮次追平漂移（一次 19 份文档 40+ 处失实），不再维护、不再新增引用。
2. **每类信息只有一个家**（上表）。写东西前先问它属于哪个家；不属于任何家 = 不写。审计档案/执行留档/审计 SOP 制度已废止：提交信息 + CHANGELOG 就是变更史；已知债务与开放发现登记在 `docs/ROADMAP.md`。
3. 改约定改本文件，改设计数值改 DESIGN_BASELINE，改方向改 ROADMAP——不交叉复制。`CLAUDE.md` 保持纯指针。README 文档表只列活文档。
4. `CONTRIBUTING.md`/`README`/`SECURITY.md`/`CHANGELOG.md` 是仓库标准门面（贡献流程入口/项目门面/安全政策/变更史），不占活文档席位、不承载约定；与活文档冲突时以对应的家为权威。
