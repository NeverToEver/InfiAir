# AGENTS.md

## 项目概览

InfiAir（无限空域）是一个单机 2D 俯视空战射击游戏，使用 **Godot 4.6 + GDScript** 实现，采用 GL Compatibility 渲染器。项目早期重制自 Python/Pygame 项目 `airwar-game`，经大规模扩展开发后已脱离原作框架独立演进；原作仅作历史参考（溯源见 `docs/archive/PORTING_PARITY.md`），本仓库运行时不依赖原作目录。

核心对局循环为：自动射击与波次刷怪 → 分数里程碑 Buff 三选一 → 3 类 Boss 轮换及狂暴阶段 → 母舰补给/火力平台 → 返航基地中场整备 → 回到同一局继续。游戏为纯得分制，没有掉落或拾取物。

- 项目入口：`project.godot` 的 `run/main_scene = res://scenes/main.tscn`。
- 设计视口：1920×1080，`canvas_items` 拉伸，`keep` 宽高比。
- 唯一 autoload：`GameState`（`autoload/game_state.gd`）——全局状态/信号总线门面。A2 起数值读取、持久化、音效池、实体注册表已组合委托给四个非 autoload 服务类（`scripts/balance_service.gd` / `save_manager.gd` / `sfx_player.gd` / `entity_registry.gd`），GameState 公开 API 语法保留并转发，调用方与测试零感知。
- 用户界面和主要文档以中文为主；新增游戏文本必须保持中英双语。
- `CLAUDE.md` 只提供入口级概览并声明本文件为权威约定文档；两者冲突时以本文件为准。
- **设计意图与架构基线的唯一修正文档为 `docs/DESIGN_BASELINE.md`**（产品玩法、技术架构、全局不变量、技术债与未来方向的总纲；专项设计文档提供实现级细节）。改动设计意图/基线时同步维护它。

## 技术栈与配置

- **引擎/语言：** Godot 4.6（gl_compatibility，无 .NET），纯 GDScript。`scripts/tools/` 下的 Python 文件是离线工具（数值管理器、文档生成器、资产生成器），不属于游戏运行时依赖。
- **数据源：** `data/balance.json` 为可调数值源（由 `scripts/tools/balance_editor.py` 维护落盘），`data/translations.csv` 是中英文本源。详细配置与发布交付现状见 `docs/ARCHITECTURE.md`。
- **发布：** 无包管理器；CI 为 GitHub Actions（`.github/workflows/ci.yml`：无头导入 + 主场景冒烟 + 31 断言场景全量回归，push/PR 触发）；发布经 `export_presets.cfg` + `release.sh` 双平台导出，产物以 GitHub Releases 附件分发（不入库）；手动触发发布工作流 `.github/workflows/release.yml`（导出打包 → tag `v<版本>` → 创建 GitHub Release，输入版本自动同步 `project.godot` `config/version`）。CI/CD 步骤增改须同步本文件与 `release.sh`；不为常规修改引入第三方插件/依赖（CI/CD 仅用官方 checkout action + 官方 Godot 二进制与导出模板）。

## 本地运行与验证

```bash
./run.sh                              # 本地运行（自动定位引擎）
godot --headless --import --path .    # 资源导入与脚本解析
godot --headless --path . --quit-after 300
godot --headless --path . res://test/smoke_test.tscn
godot --headless --path . res://test/base_system_test.tscn  # 涉存档/基地/母舰时加跑
```

推荐的最小验证集为：`--headless --import`、`--quit-after 300`、`smoke_test.tscn`。**完整专项测试清单、视觉截图工具与测试策略副作用明细见 `docs/TESTING.md`**。提交/PR 由 GitHub Actions CI（`.github/workflows/ci.yml`）自动跑全量 31 断言场景，CI 全绿是合入门槛。

## 运行时架构

`scenes/main.tscn` 是主节点树和对局容器（Starfield/Camera2D、Player、Spawner、BulletPool/EnemyPool、HUD 及各页面 UI、BackNavigator，以及运行时由 main 创建的 MetaHealthFX/AimFrameLayer/过场/母舰/事件等）。`scripts/main.gd` 是对局编排核心；`GameState` 委托四个服务类（见上）；动态对局实体应挂在 Main 下，以便清场逻辑和测试遍历可见。**完整节点树与逐脚本职责见 `docs/ARCHITECTURE.md`**。

## 目录职责

| 路径 | 内容与职责 |
| --- | --- |
| `autoload/` | 全局 autoload；当前只有 `game_state.gd`。 |
| `scenes/` | Godot `.tscn` 场景与节点组合。 |
| `scripts/` | GDScript 游戏逻辑、UI、表现和池实现。 |
| `scripts/tools/` | 离线 Python 工具（`balance_editor.py` 数值管理器、`gen_balance_map.py` 文档生成、资产生成器）。 |
| `assets/` | 贴图、音效/BGM、字体和着色器。 |
| `data/` | 运行时数值配置（`balance.json`）和翻译资源源文件。 |
| `test/` | 无头场景自检、性能基准、自动游玩和截图工具（命令见 `docs/TESTING.md`）。 |
| `docs/` | 审计档案（`AUDIT_VAULT.md`）、设计文档、路线图、BALANCE_MAP 与 archive 档案。 |
| `packaging/` | 发布包随附的安装/卸载脚本（linux/、windows/）。 |

`scripts/tools/` 明细与全部目录职责详见 `docs/ARCHITECTURE.md`。

## 开发约定

### GDScript 与场景生命周期

- 遵循 Godot 4 官方风格：**Tab 缩进**、类型标注、`CONSTANT_CASE` 常量、私有成员前缀 `_`、`signal_name.emit()` / `signal_name.connect()` 信号语法。
- `setup()` 会在实体被加入场景、执行 `_ready()` 之前调用。此阶段不要依赖 `@onready` 缓存；改用 `$节点路径` 访问子节点。
- 不要修改既有 autoload 或输入映射来完成无关需求。现有输入由 `project.godot` 定义，包括移动、`boost`（Shift）、`fine_move`（Ctrl）、`dash`（Space）、`dock`（H）、`homecoming`（B）、`give_up`（K）、`buff_panel`（L，展开/收起 buff 滚动栏）和 `restart`（R）。**手柄默认绑定（左摇杆移动/动作键/右摇杆瞄准）由 `GameState._bind_joypad_defaults()` 启动时经 InputMap 运行时装配**（P0-1：`project.godot` 只存键盘，手柄不落 project.godot；死区经 `GameState.set_joy_deadzone()` 应用到全部手柄动作）。
- 教程进入时会隔离对局状态和存档，离开时必须恢复 `Engine.time_scale = 1`。运行期创建的节点要保存引用，不能依赖 Godot 自动生成的节点名。
- **新增带 `class_name` 的脚本文件后，必须先 `godot --headless --import --path .` 刷新全局类缓存**，否则引用它的脚本会编译失败（`Identifier "X" not declared`）并连带宿主场景运行时崩坏。
- 延迟回调不要 `await get_tree().create_timer()` 或挂起在任何计时器上的协程：进程退出时未完成的协程函数状态会泄漏，并连带其引用的资源（贴图/音频）。改用一次性 `Timer` 节点 + 信号连接（参考 `spawner.gd` 的 `_schedule()`），Timer 随场景树释放。

### 数值与配置

- **可调游戏数值只修改 `data/balance.json`，不要仅修改脚本回退值。** 脚本内的同名默认值用于缺键/损坏 JSON 回退，新增或调整数值时应保持两者一致。改数值优先用 `scripts/tools/balance_editor.py`（浏览器编辑、校验、备份）；改完运行 `scripts/tools/gen_balance_map.py` 刷新 `docs/BALANCE_MAP.md` 并检查其"双向反查"两节无新增失配条目。
- 统一使用 `GameState.cfg("player.fuel.drain", default)` 查询嵌套配置。高频 `_process`/`_physics_process` 路径必须在 `_ready()` 或初始化阶段读取并缓存，不要每帧查 JSON 字典。
- `GameState` 在启动时加载 `balance.json`，并对缺失或无法解析的配置使用脚本默认值。
- **整体机体缩放只有一个杠杆：`balance.json` 顶层 `world_scale`（当前 0.4，2026-07-31 由 1/3 上调），运行时缓存为 `GameState.world_scale`。** 机体尺寸族数值——贴图 scale、碰撞 radius、muzzle/对接/炮位/牵引等机体偏移、子弹/爆炸/穿梭门/激光判定等随机体特效比例——在 json/tscn/脚本回退三处一律存**设计值**（1.0 基准，即 2026-07 缩放前的原始大小），由实体在 `_ready()`/`setup()` 统一乘 `world_scale` 后应用。游戏性范围族（AoE 半径、锁定/清弹半径、减速环）与指示器/过场/UI 不乘。新增尺寸数值时按此归类，不要绕过杠杆直接写运行值。
- 尺寸应用必须是**幂等赋值**（`radius = 设计值 * world_scale`），严禁 `*=` 累乘：场景 CircleShape2D 等 sub_resource 默认被全部实例共享，累乘会逐实例重复缩放；运行时写半径的场景（enemy.tscn）要给 shape 加 `resource_local_to_scene = true`（普通机与精英半径不同档，共享会互相串改）。

### 碰撞、伤害与视角

- 逻辑碰撞层约定：1=`player`、2=`player_bullet`、3=`enemy`（含 Boss）、4=`enemy_bullet`。玩家子弹以 `enemy` 组结算；敌方子弹和敌方实体以 `player_hitbox` 组结算。
- 玩家受击只使用 `Player/Hitbox` 的 Area2D（设计值 r=7 × world_scale，当前运行值 2.8）。`CharacterBody2D` 本体的半径 22 圆没有碰撞用途（mask 为 0），不得用于受击判定。
- 子弹使用 `scenes/bullet.tscn`，由 `setup()` 区分阵营；敌阵营视觉缩放用 `effects.enemy_bullet_visual_scale`、玩家弹用 `effects.bullet_visual_scale`（均设计值 × world_scale），玩家弹可写入 `Bullet.homing_target` 追踪（字段在 `activate()` 重置清单内）。爆炸应使用 `Explosion.spawn_at()`，而非为每次爆炸随意构建新的粒子方案。
- 视角缩放和窗口尺寸是相互独立的 profile 设置。相机固定在 `(960, 540)` 并只调整 `zoom`；所有屏幕边缘、出界、刷怪和可见区域计算必须使用 `GameState.view_world_rect()`，不要硬编码 `0..1920` 或 `0..1080`（Boss 战斗锚线 `_fight_anchor_y()`、敌机悬停带/入场锚点基线均已按此适配）。
- **鼠标锁定窗口内**（profile 设置 `mouse_lock`，默认开启）：`scripts/mouse_trap.gd`（挂 Main，`PROCESS_MODE_ALWAYS`）在**对局准星活跃（未暂停且系统光标隐藏）且窗口聚焦**时把移出内容区的鼠标经 `Input.warp_mouse()` 拉回边缘内侧（`mouse_exited` 信号触发 + `_process` 每帧防御），从根上避免鼠标出框导致准星 `get_global_mouse_position()` 冻结/跳变；暂停/Buff/基地/结算/过场/开始页等非准星态与窗口失焦一律放行，保证暂停后鼠标可自由移出窗口（如点系统标题栏关闭按钮退出游戏）。

### UI、文本与导航

- 所有用户可见文本使用 `tr("UPPER_SNAKE_CASE_KEY")`。新增键必须同步写入 `data/translations.csv` 的 `zh` 和 `en` 列；让 Godot 重新导入后生成 `.translation`。动态文本使用带 `%d`/`%s` 占位符的翻译键。
- 语言切换必须经 `GameState.set_locale("zh" / "en")`，并使 UI 监听 `locale_changed` 后刷新文本。
- 页面样式使用 `scripts/ui_theme.gd`：色板 token、字号阶梯、`make_label()`、`make_button()`、`make_toggle_button()`、`make_section_header()`、`make_page_shell()`（页面骨架：dim 强遮罩 + 居中 margin + 标题/副标题/内容/按钮区，模态页均经它组合）、`animate_modal_open()`（模态统一打开动效）、`add_button_motion()`（按钮 hover/按压微动效，按钮工厂自动挂载）、`make_buff_tile()`（HUD buff 图标格：46×46 字形瓦片 + 层数徽标；右下角收起态单行（最新 4 格 + 溢出 +N），L 展开右缘滚动明细栏，Esc 经 BackNavigator 优先收栏）和开场动画工具；可复用构件还有 `scripts/ui_chamfered_panel.gd`（切角面板）、`scripts/ui_segmented_bar.gd`（分段条形仪表，末段按比例部分填充）、`scripts/ui_buff_icons.gd`（BuffIcons：16 种 buff 程序化字形 + 分类配色，HUD 图标坞与 Buff 三选一卡片共用）与开始页装饰 `scripts/start_radar.gd` / `scripts/start_backdrop.gd`。新页面以 `make_page_shell()` 组合，单页最多一个 primary 主按钮；不要散落手写色值和重复 Label/Button 样板。
- **全局技能 `game-ui-ux`**（`~/.kimi-code/skills/game-ui-ux/`，来自 `gamedev-skills/awesome-gamedev-agent-skills`，Apache-2.0）提供跨引擎游戏 UI/UX 设计指导（锚点/容器响应式布局、分辨率与宽高比缩放、安全区、键鼠/手柄焦点导航、屏幕栈、事件驱动而非轮询的 HUD 更新），与 `godot-ui-control` 互补。设计/重构 HUD、菜单或覆盖层时按需调用，并遵循本项目 `ui_theme.gd` 约定。
- Buff、暂停、结算等暂停态 UI 必须设置 `process_mode = Always`，并通过 `get_tree().paused` 管理暂停。
- 返回/退出集中在 `BackNavigator`。除设置页的改键捕获态外，页面不要自行消费 `ui_cancel`；新增页面层级必须在 `decide_back_action()` 中登记，并同步 `docs/EXIT_FLOW.md`。
- BGM 循环只设置 `stream.loop_mode = LOOP_FORWARD`；不要显式设置 `loop_begin`/`loop_end` 或在 `_exit_tree()` 停止 BGM，否则可能造成播放实例泄漏。

### 性能与对象生命周期

- 子弹生产统一使用 `GameState.bullet_pool.fire()`；外部 `queue_free()` 后的池引用清理由子弹退出树逻辑处理。
- 修改对象池时必须保留 `_active` 与 `_repooling` 防护。Godot 4.6 的 `reparent()` 会触发 `_exit_tree()`；回收 reparent 必须由 `_repooling` 包裹，否则 `forget()` 会将对象错误地从空闲池移除。修改后运行 `test/pool_reuse_test.tscn`。
- 敌机统一走对象池（`GameState.enemy_pool.spawn()`，含普通波次、Boss-3 小怪与编队机；`USE_POOL=false` 时退化为直接实例化，作性能 A/B 对照开关）。池化实体的 `reactivate()`/`deactivate()` 负责状态重置、注册表和死亡信号；不要把池对象外部随意释放或绕过其生命周期。架构细节见 `docs/ARCHITECTURE.md`。
- 热路径不能反复 `get_nodes_in_group()`；使用 `GameState.enemies`、`GameState.player_ref` 和 `GameState.player_hitbox` 注册表。`Enemy` 移动计算使用 `Enemy.sin_fast()` / `Enemy.cos_fast()` 的查表实现，避免在 `_physics_process()` 直接调用三角函数。
- HUD 仪表类轮询按约 0.1 秒节流，且只在文本/格子值变化时更新布局；优先通过 `GameState` 信号驱动状态更新。

### Shell 脚本与启动包装

- 项目根脚本：`run.sh` / `run.command` / `run.bat`（本地启动；`run.sh` 与 `run.command` 同一参数协议，可透传引擎参数，如 `--editor`、`--headless --quit-after 300`）、`release.sh`（发布构建）、`packaging/`（双平台安装/卸载）。**结构规范提炼自 `bentsolheim/public-skills` 的 `bash` skill（v2.0.0，生态中唯一 shell 维护类 skill），按项目现状适配如下**（其"禁 `set -e`"立场与项目主流实践冲突，未采纳）：
  - **错误处理**：新脚本默认 `set -euo pipefail`（既有 `release.sh`/`run.sh`/`packaging/linux/*.sh` 均为该风格，属主流实践）；错误信息走 stderr（`>&2`）并带上下文，返回非零退出码。`run.command` 因 macOS 双击需"异常退出保留窗口与输出"，用显式 `$?` 检查而非 `set -e`——有意的特例。
  - **结构**：带参数/多函数/交互的普通脚本用 `main()` + 底部 guard clause（`[[ "${BASH_SOURCE[0]}" == "${0}" ]] && main "$@"`，保证可被 source 而不执行）+ `usage()` heredoc（描述/选项/示例/依赖）；函数单一职责、参数用 `local`。简单脚本（<30 行、无参数、线性、输出供程序消费）无需 main 包装，但保留目的注释、退出码与变量加引号。
  - **参数解析**：`while` + `case` 结构；未知选项报错并调 `usage()`；支持 `--help` / `--version`。
  - **依赖与输出**：启动类脚本须做引擎探测与版本判定（本项目要求 Godot 4.6+，参考 `run.sh`/`run.command` 的候选列表与 `version_ok` 模式）；依赖外部工具先 `command -v` 检查。交互输出可带颜色但须尊重 `NO_COLOR`。
  - **校验**：改完 shell 脚本至少 `bash -n` 语法检查 + 实际跑通（如 `./run.command --headless --quit-after 300` 无头自检）。

## 测试策略

每个 `test/*.tscn` 启动相应 GDScript 场景，并以 `[PASS]`/`[FAIL]` 输出和退出码自检（非单元测试框架）。`test/` 下共 40 个场景：31 个断言场景，外加 `autoplay_test`（探针）、`perf_bench`（性能基准）与 7 个窗口模式截图工具。**运行命令、专项场景清单、副作用与既有失败基线见 `docs/TESTING.md`**。

## 持久化与安全边界

- 对局存档为 `user://savegame.json`，局外档案为 `user://profile.json`；二者由 `GameState` 管理并带版本字段。profile 保存最高分、本地高分榜、难度、键位、语言、视角、窗口尺寸、教程状态、手柄参数（`joy_aim_speed`/`joy_deadzone`）等。
- 损坏 JSON 会被隔离为 `<file>.corrupt`，并通过 `save_corrupt`/`profile_corrupt` 标记通知开始界面。不要绕过该恢复流程。
- 当前未发现网络通信、第三方插件、远程服务、密钥或凭据文件。除本地 `user://` 持久化和离线资源生成外，游戏没有外部交互。离线数值管理器 `balance_editor.py` 只监听 127.0.0.1，不属游戏运行时。
- `.gitignore` 排除导入缓存和导出产物（`builds/` 等）；`export_presets.cfg` 已随打包发布重启入库（2026-07-30），修改预设需同步审查 `release.sh` 与 `packaging/`。若未来增加 CI/自动部署，先补齐可审查的工作流与发布说明，再把它写入本文件。

## 文档同步要求

- 调整项目方向、阶段计划或暂缓/重启决策时，更新 `docs/ROADMAP.md`（方向类决策的单一事实源）。
- 调整设计意图、玩法规则或架构基线时，更新 `docs/DESIGN_BASELINE.md`（设计基线唯一修正文档）并同步受影响专项设计文档。
- 调整页面返回层级、退出清理或平台返回处理时，更新 `docs/EXIT_FLOW.md` 并运行返回导航测试。
- 新增/改名数值键或调整 `cfg()` 调用后，运行 `python3 scripts/tools/gen_balance_map.py` 重新生成 `docs/BALANCE_MAP.md`。
- **`docs/AUDIT_VAULT.md`（代码审计档案）为专有文档，禁止删除或合并**：登记全部已发现的代码质量错误、修复指引、修复后的处理与起效记录、工作时间与区域。新审计发现追加登记；修复落地后在对应条目回填「修复起效记录」并更新状态总览。任何清理/归档操作不得移除本文件。
- **已完成工作压缩留档**：计划/审核文档落地完成后，全文移入 `docs/archive/`，并在 `docs/archive/EXECUTION_LOG.md` 登记压缩条目（日期 / 落地提交 / 摘要 / 关键决策与教训 / 原文链接），然后从 `docs/` 顶层删除原文档并更新各引用。归档文档内部 `docs/xxx` 引用为归档前快照，不保证可点击（与 `docs/archive/PORTING_PARITY.md` 同例）。
- 修改工程结构、运行命令、测试策略、配置位置或本文件所述约定时，同步维护本 `AGENTS.md`，使其保持面向首次接手项目的代理的真实入口文档；架构/配置细节改动同步维护 `docs/ARCHITECTURE.md`，测试命令/策略改动同步维护 `docs/TESTING.md`。
