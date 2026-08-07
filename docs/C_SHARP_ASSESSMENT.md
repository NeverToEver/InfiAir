# C# 混合编译调研与决策（2026-08-05）

> 调研对象：InfiAir（Godot 4.6.2 + 纯 GDScript 2D 弹幕射击）
> 调研问题：引入 C# 与 GDScript 混合编译的收益与风险，是否引入。
> 结论先行：**不引入。维持纯 GDScript。** 理由见 §7；未来触发条件见 §8。

---

## 1. 结论摘要（TL;DR）

| 维度 | 结论 |
| --- | --- |
| 性能收益 | ≈ 0。perf_bench 实测极限压力下平均帧耗时 **1.011ms（等效 989 FPS）**，脚本层远未到瓶颈 |
| 平台推力 | 无。发布目标仅 Linux/Windows；无 Web（C# 不支持 Web 导出的最大限制因此不构成问题，但也没有引入的外部理由） |
| 工程化收益 | 有限。现有 5 层门禁（gdformat + gdlint + warning-as-error + 冒烟 + 47 断言场景，权威计数见 `docs/TESTING.md`）已覆盖 C# 静态类型可防的大部分错误类别 |
| 引入成本 | 确定且横跨三处：CI 重构（换 .NET 版引擎 + dotnet build）、发布链路重构（mono 模板 + dotnet publish）、本地工具链（需安装 .NET SDK、更换编辑器为 .NET 版）；另加双语言长期维护 |
| 架构冲击 | GDScript 与 C# **不可互相继承**（官方限制），现有 `class_name` 体系（Bullet/Enemy/Boss 等）若部分迁移会产生继承断层；热路径跨语言调用为动态派发 + marshalling，有额外开销 |
| 最终决策 | **不引入 C#** |

---

## 2. 背景与调研方法

### 2.1 背景

InfiAir 是 Godot 4.6 + GDScript 的 2D 弹幕射击，自 Python/Pygame 原版移植后已独立演化。项目刚完成 Phase 0 技术债清零（A2–A8 架构债务、test/ 门禁盲区、CI/CD 落地），处于"稳定维护 + 内容演化"阶段。本次调研评估引入 C# 混合编译（GDScript + C# 共存，官方支持）的可行性。

### 2.2 调研方法

- **本地实测**：代码规模统计（`find`/`wc`）、性能基线（`perf_bench.gd` 实跑）、工具链/CI/发布链路逐文件核对（`project.godot`、`export_presets.cfg`、`ci.yml`、`release.yml`、`ROADMAP.md`、`docs/ARCHITECTURE.md` 相关约定）。
- **外部资料**：Godot 4.6 官方文档（C# 基础、跨语言脚本）、Godot 官方论坛/提案、第三方性能对比实测。
- 数据收集日期：2026-08-05。

---

## 3. 项目现状

### 3.1 代码规模与架构

| 目录 | 文件数 | 行数 |
| --- | --- | --- |
| scripts/ | 73 | 22,287 |
| autoload/ | 1 | 1,854 |
| test/ | 54 | 11,512 |
| **合计** | **128** | **35,653** |

- 唯一 autoload：`GameState`（facade，转发 6 个非 autoload 服务：balance/save/sfx/entity_manager/fog_events/event_manager；另挂 `UserDB`）。
- 弹幕热路径已充分优化：`BulletPool` 对象池复用 + 同屏敌弹 500 硬上限（`bullet_pool.gd`）、`BossFire` 纯发射逻辑（RefCounted，A3 拆分）、策略/波次均由配置驱动（`data/balance.json`）。
- 架构债务清零：A2 服务拆分、A3/A4 注册表 + 声明式效果表、A8 PlayerVisuals 拆分全部落地；45 断言场景 0 FAIL（2026-08-05）。

### 3.2 性能基线（实测）

`test/perf_bench.gd`：headless + 物理帧率拉满（1000Hz）测纯 CPU 成本，场景 = main + 200 敌机（混合机型/策略）+ 玩家强制开火 + 每 20 帧一次爆炸 + 每 10 帧一次刷怪 churn。

```
PERF_RESULT frames=1800 total_ms=1820 avg_frame_ms=1.011 equivalent_fps=989.0
```

- 平均帧耗时 **1.011ms**，等效 **989 FPS**；60Hz 下帧预算 16.7ms，脚本层占用 ≈ **6%**。
- 该场景已超出正常对局压力（perf_bench 注释：同屏弹峰值 300+，远低于 500 上限），意味着**性能余量一个数量级以上**。
- 结论：当前不存在任何由 GDScript 引起的性能瓶颈；"GDScript 慢"在本项目语境下不构成引入 C# 的理由。

### 3.3 构建、CI 与发布链路

- **CI**（`.github/workflows/ci.yml`）：官方 Godot 4.6.2 **标准版** headless（非 .NET 版）→ gdlint/gdformat 门禁 → 无头导入（warning-as-error）→ 主场景冒烟 → 47 断言场景全量 + 编译探针。
- **发布**（`.github/workflows/release.yml` + `release.sh`）：标准版引擎 + 标准导出模板（`Godot_v4.6.2-stable_export_templates.tpz`）→ Linux/Windows 双平台包 → GitHub Release。CI/CD 政策：不引入第三方依赖，仅官方 checkout action + Godot 二进制/模板。
- **本地**：标准版 4.6.2 引擎（`run.sh` 自动定位）；**未安装 .NET SDK**。

### 3.4 平台目标

`export_presets.cfg` 仅两个预设：Linux/X11、Windows Desktop（均 x86_64，embedded pck）。ROADMAP 记录：移动端触控已 cut（Phase 3 重启需显式决策）；**Web 平台从未列入计划**。

---

## 4. 引入 C# 的收益分析

### 4.1 运行性能

- 纯计算场景（数值/寻路/模拟）C# 可比 GDScript 快数倍；但引擎 API 调用密集时两者接近——瓶颈在引擎侧而非语言侧（Godot 官方论坛共识 [State of GDScript vs C# in Godot 4.0](https://forum.godotengine.org/t/state-of-gdscript-vs-c-performance-in-godot-4-0/5875)）。
- 本项目热路径（子弹/弹幕/碰撞）本质是引擎 API 调用（`instantiate`、`reparent`、`global_position`、物理回调），且实测余量巨大（§3.2）。
- **性能收益评估：≈ 0**。真实对局连 20% 帧预算都用不到，C# 提升无感。

### 4.2 类型安全与工程化

- C# 静态类型 + 编译期检查确实优于 GDScript 的动态检查，但项目已有替代防线：
  - `project.godot` 将 `unsafe_*`/`untyped_*`/`shadowed_*` 等警告设为 error 级（warning-as-error 门禁）；
  - `gdlint` 规则门禁 + `gdformat` 格式门禁；
  - 47 断言场景回归 + 编译探针覆盖 test/ 盲区。
- 在此门禁下，GDScript 的类型错误在 CI 即暴露；C# 带来的增量防护有限。
- **工程化收益评估：低**（对既有稳定代码库）。

### 4.3 工具链与 IDE

- C# 可获得 Rider/VS 的完整重构、调试、性能分析体验，优于 GDScript 的 VSCode 插件。
- 但项目工具链已固化且工作良好：`gdformat`/`gdlint`/`run.sh`/`release.sh` 全部就位，团队（单人主导 + 协作模式）已适应。
- 引入 C# 意味着**同时维护两套语言约定与两套 lint/format 链路**（GDScript 的 gdtoolkit + C# 的 dotnet format/analyzer）。
- **工具链收益评估：中低**（对习惯 GDScript 的现有贡献者甚至是负收益）。

### 4.4 生态与团队

- 社区共识：GDScript 对 Godot 集成最顺（编辑器内体验、教程、示例）；C# 生态侧重工具链而非引擎集成（[GDScript vs C# in Godot](https://chickensoft.games/blog/gdscript-vs-csharp)）。
- 项目贡献门槛会从"会 GDScript"变为"会 GDScript + C#"，协作成本上升。
- **生态/团队收益评估：≈ 0**（无任何项目需求依赖 C# 生态）。

---

## 5. 风险与成本分析

### 5.1 CI 与本地工具链重构（高、确定）

- CI 需从标准版切换为 **.NET 版引擎**（`Godot_v4.6.2-stable_mono_linux_x86_64.zip`）+ 安装 .NET SDK + 新增 `dotnet build` 门禁；违反项目"CI 仅官方 checkout action + Godot 二进制/模板、无第三方依赖"的既定政策（需修订 AGENTS.md 与 doc-sync 约定）。
- 本地：需安装 .NET SDK（当前**未安装**）、编辑器换成 .NET 版；`run.sh` 引擎定位逻辑需适配。
- Godot 4.6 桌面需 **.NET 8+**（官方文档 [C# basics](https://docs.godotengine.org/en/4.6/tutorials/scripting/c_sharp/c_sharp_basics.html)），Android 需 .NET 9+——引入后版本同步是长期维护负担。

### 5.2 导出与发布链路（中、确定）

- 发布需换 mono 导出模板 + `dotnet publish`/AOT 决策；导出包增大约 **10–20MB**（社区实测 [Will using C# increase exported file size?](https://godotforums.org/d/40484-will-using-c-increase-exported-file-size)），对桌面可接受，但增加发布链路复杂度与失败面。
- `release.sh`、export_presets、release.yml 三处同步改动，与当前"零第三方依赖、链路已证明"的稳定状态冲突。

### 5.3 混编 interop 与继承隔离（中高）

- **官方限制：GDScript 与 C# 不可互相继承**（[Cross-language scripting](https://docs.godotengine.org/en/4.6/tutorials/scripting/cross_language_scripting.html)）。现有 `class_name` 体系（Bullet、Enemy、Boss、BossAttacks 等）若部分迁移 C#，继承关系将被切断，需重设计边界。
- C# → GDScript 的字段/方法访问是**动态派发**（`Get`/`Set`/`Call` + Variant marshalling），丢失静态类型且带运行时开销；热路径（如 `bullet_pool.fire()` → `Bullet.setup()`）若跨语言，会同时失去性能与类型收益。
- 跨语言信号连接仅 `Connect` 字符串形式，无编译期检查。
- 混编可行（官方支持），但"可行"≠"无摩擦"：边界越少越安全，而本项目热路径恰好集中在对象池/弹幕/敌人体系，迁移面即风险面。

### 5.4 测试与门禁（中）

- 47 断言场景 + autoplay + perf_bench 全为 GDScript，CI 中可继续跑；但任何 C# 代码新增 `dotnet build` 前置步骤，测试矩阵复杂度上升。
- 混编后"哪些逻辑在 GDScript、哪些在 C#"本身成为需要持续维护的架构文档负担（对应现有 `.agents/*` 约定体系需扩写）。

### 5.5 长期维护成本（高、确定性最高的一项）

- 双语言 = 双份约定、双份工具链、双份学习曲线、双份调试体验（GDScript 运行时错误 vs C# 编译/运行时错误）。
- 项目正处于"架构债务清零后的稳定期"，ROADMAP 已明示 Phase 3 内容需重新立项——此刻引入新语言属于**用维护成本换取不存在的收益**。
- 唯一官方"强理由"（Web/移动/console 目标）本项目均不适用。

---

## 6. 收益–风险矩阵

| 项 | 收益 | 风险/成本 | 权重（本项目） |
| --- | --- | --- | --- |
| 运行性能 | ≈0（实测 989 FPS 等效） | — | 无影响 |
| 类型安全 | 低（门禁已覆盖大部分） | 边界处失去（动态派发） | 微弱正 |
| IDE/工具链 | 中低（Rider/VS） | 双工具链维护 | 近零 |
| CI | — | 高（换 .NET 引擎 + dotnet 门禁，违反 CI 政策） | 显著负 |
| 发布链路 | — | 中（mono 模板 + 包体 +10~20MB） | 中负 |
| 继承/架构 | — | 中高（跨语言继承禁止，热路径断层） | 显著负 |
| 团队/贡献 | — | 中（双语言门槛） | 中负 |
| 长期维护 | — | 高（双约定双工具链） | 显著负 |

**净值：显著为负。** 不存在收益项能覆盖成本项；关键前提（性能瓶颈、平台需求）均经实测/文档核实为不成立。

---

## 7. 决策与理由

### 决策：不引入 C#。维持纯 GDScript。

理由（按权重排序）：

1. **性能无瓶颈是实测事实**：极限压力场景 1.011ms/帧（§3.2），性能收益 ≈ 0，而"性能"是迁移 C# 最常见也最正当的理由。
2. **无平台推力**：C# 的典型引入动因是 Web 之外的高性能/平台需求；本项目仅桌面双平台，移动端已 cut，Web 无计划（§3.4）。
3. **时机错误**：项目刚完成技术债清零并固化 5 层门禁与 CI/CD（§3.3），处于稳定期；引入 C# 等于主动拆掉刚刚稳定的构建/发布链路。
4. **架构冲击不可小觑**：跨语言继承禁止 + 热路径动态派发（§5.3），与已优化的对象池/弹幕体系直接冲突。
5. **既有工程化保障足够**：warning-as-error + gdlint + 47 断言场景已把 GDScript 的短板（弱类型）压缩到低风险区间（§4.2）。

### 附带建议

- 若追求类型安全增量，优先方向是**强化 GDScript 门禁**（如提高 `untyped_declaration` 至 error 级）而非引入 C#。
- 若未来出现真实性能热点，先做 **GDExtension（C++/Rust）定点热路径**，而非整体引入 C#（社区对"GDScript 慢"的标准解法 [GDScript vs C# in Godot](https://www.oflight.co.jp/en/columns/godot-gdscript-vs-csharp-language-choice-2026)）；GDExtension 不改变 CI 的 GDScript 主线，但同样需要权衡。
- 本决策属方向性决策，已按 doc-sync 约定在 `docs/ROADMAP.md` 登记（见其 "Decisions" 小节）。

---

## 8. 未来触发条件（何时重新评估）

以下任一成立时，重新评估引入 C#（并按 §5 清单补齐前置评估）：

1. **实测性能瓶颈**：真实对局（含渲染）帧耗时稳定越过预算，且 profiler 定位到 GDScript 计算热点（非引擎 API 瓶颈）。
2. **平台需求变化**：新增 console 目标、或需要 C# 生态才可用的平台特性。
3. **团队语言构成变化**：主力贡献者以 C# 为主且明确愿意承担迁移/双语言维护成本。
4. **架构大改窗口**：出现必然的大规模重写（如引擎升级到破坏性版本、玩法系统重构），此时迁移边际成本大幅下降。

---

## 9. 参考来源

- Godot 4.6 官方文档 C# basics（.NET 版本要求）：https://docs.godotengine.org/en/4.6/tutorials/scripting/c_sharp/c_sharp_basics.html
- Godot 官方文档 Cross-language scripting（混编规则、跨语言继承禁止、动态派发）：https://docs.godotengine.org/en/4.6/tutorials/scripting/cross_language_scripting.html
- Godot 论坛：GDScript vs C# 性能本质（API 密集场景两者接近）：https://forum.godotengine.org/t/state-of-gdscript-vs-c-performance-in-godot-4-0/5875
- Godot 论坛：C# 导出包体积增量（+10–20MB）：https://godotforums.org/d/40484-will-using-c-increase-exported-file-size
- Chickensoft：GDScript vs C# in Godot（语言选择与生态）：https://chickensoft.games/blog/gdscript-vs-csharp
- 日/英专栏：GDScript vs C# 2026（"GDScript 慢是分类错误，热点才推 GDExtension"）：https://www.oflight.co.jp/en/columns/godot-gdscript-vs-csharp-language-choice-2026
- 项目内部：`test/perf_bench.gd`（性能基线）、`export_presets.cfg`（平台目标）、`.github/workflows/ci.yml` + `release.yml`（构建链路）、`docs/ROADMAP.md`（阶段与方向）、`project.godot`（警告门禁配置）。
