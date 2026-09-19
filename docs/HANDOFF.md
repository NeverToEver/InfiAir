# 交接文档 · 机型玩法层能力差异（P1）

> **本文件是临时交接件**：接手者确认无遗漏后**直接删除**（`AGENTS.md` §7：活跃文档只留六份，草稿不留存；要恢复就从 git 历史取）。
> 仓库 `/Users/xiepeilin/InfiAir`，分支 `main`，Godot 4.7.2 .NET + C#（零 GDScript）。
> 交接时的状态：**工作树干净、门禁五步全绿、本批全部提交并已推送**（提交清单见 §2）。

---

## 0 一页现状

**人类原始指令**（2026-09-19）：「要不要给部分职业延长弹反时间，以及提前解锁冲刺等，来让各个不同的机体更具有其鲜明的特色，不用和普通机体一样一碗水端平却要一样等待很多时间才能够玩得很爽」。
**追加指令**（同日晚）：「选机型的时候不用精确数字，用五维图 / 六维图展示战机能力，更直观，不用比来比去」。

**两条指令都已落地**：

1. **机型差异从两层扩到三层**——①表现层 / ②手感层（§1.18，前一批）之后补上 **③玩法层能力轴**：四条轴（弹反窗 / 弹反循环 / 冲刺冷却 / 加速耗油），五个特种型各「一强 + 一异轴代价」，标准型＝全能力基准。**冲刺改为开局六型一律可用**（不再是天赋解锁奖励）。
2. **选机型面去掉数字**，改成**六维性格指纹图**（雷达图）：中心＝标准型基准、基准环在半半径处，凸起＝强于基准、凹陷＝弱于基准；六轴＝火力 / 机动 / 装甲 / 弹反 / 突进 / 续航，每型都是「两凸两凹」的形状。

**没有待写的功能**。剩下的都是「有条件才做」的登记项与一条本环境做不到的自检（见 §5、§6）。

---

## 1 五分钟上手（纪律与单源指针）

先读 `AGENTS.md`（唯一判定口径：§1 路由表、§2 决策权、§3 完成的定义、§4 测试策略、§5 门禁、§6 提交规范、§8 注释与术语）。要找什么只看这一张表：

| 要做的事 | 去哪 |
| --- | --- |
| 调数值 | `data/balance.json`（元数据 / 范围 / 出处：`scripts/tools/balance_meta.json`；可视化：`python3 scripts/tools/balance_editor.py`） |
| 改玩家可见文案 | `data/translations.csv`（中英双列；缺键玩家看到键名） |
| 查设计定稿取值 | `docs/DESIGN_BASELINE.md`（本批：§1.17 数值层、**§1.18 手感层**、**§1.19 能力轴 + 指纹图**） |
| 查为什么这么定 / 开放债务 | `docs/ROADMAP.md`（一行制决策 + 债务区） |
| 查行业依据 | `docs/REFERENCES.md`（本批引用 §4.15 / §4.16） |
| 跑门禁 | `python3 scripts/ci/gates.py`（`--list` 看步骤清单） |
| 写 Release 正文 | `docs/RELEASE_NOTES.md` |

**本批涉及的单源（改一处即生效，别在别处再写一份）**：

- 机型数值乘区 / 手感档案 / 能力轴取值 → `data/balance.json` 的 `machines.<id>.{...|feel|kit}`（只列差异键，缺键回退 core 默认）
- 机型能力轴的结构（键名 / 域 / 默认表 / 轴元数据）→ `csharp/core/Machines/MachineKit.cs`
- 机型性格指纹的六轴算式与几何 → `csharp/core/Machines/MachineProfile.cs`
- 铭牌 / 指纹图的版式常量 → `csharp/core/MachinePlateLayout.cs`
- 机型名册（含 `TagKey` / `BlurbKey` / `KitBonus` / `KitPenalty`）→ `csharp/core/Machines/MachineRoster.cs`

---

## 2 已完成（按提交，最新的在下）

| 提交 | 是什么 | 关键文件 / 判据 |
| --- | --- | --- |
| `8478f12` | `refactor:` 删 `PlayerDash` 无人读的冷却字段 | 该字段是「调参写进去改了没反应」的假旋钮；删后同类接错变成编译失败 |
| `855ac4c` | `feat:` 冲刺开局可用，`phase_dash` 转纯强度节点 | `Player.cs`（删解锁判定与 `DashUnlocked`）、`Hud.cs`、`AbilitySocket.cs`（三态→两态）、`Tutorial.cs`（删白送那级）、`TalentService.cs`（删孤儿 `GrantLevel`）、`translations.csv` 两条文案 |
| `2a396bb` | `feat:` 机型能力轴进玩法层，六型各一强一弱 | core `MachineKit.cs`、`GameState.Machine.cs`（`Kit` / `KitFor`）、`Player.cs` 四处消费点、`MachineWheelPanel.cs`、`balance.json` 的 `machines.*.kit`、`MachineKitTests`（15 条） |
| `c924b57` | `feat:` 断油重启阈值 30 → 25 | `player.fuel.restart` 一键；修掉本批新引入的「满投资时加速无响应」 |
| `6e2d6a5` | `feat:` 本局记录带机型（`best.json` 的 `machine`） | core `BestRecord` / `GameState.BestRecord`；**不上屏**（机型层唯一回流数据），旧档缺键归一标准型 |
| `f8fb10c` | `test:` 难度对账补机型能力层与相位冲刺行 | `balance_analysis.py` 新节 + `scripts/tests` 镜像算例（工具此前对本批**零可见**） |
| `90ed55d` | `docs:` 口径并入设计基线 §1.19，登记五条决策与四条债务 | `DESIGN_BASELINE.md` / `ROADMAP.md` / `RELEASE_NOTES.md` / 双 README |
| `35c9ca4` | `refactor:` 铭牌版式下沉 core，补「装得下」与「对中」判据 | `MachinePlateLayout.cs` + `MachinePlateLayoutTests` |
| `a7eb99c` | `feat:` 选机型改六维性格指纹图，不再逐项比数字 | core `MachineProfile.cs`（六轴 + 多边形）、`MachineProfileChart.cs`（`Control`+`_Draw`）、`MachineWheelPanel.cs` 改版、`MachinePlateLayout.cs` 加高到 560×397、`MachineProfileTests`（15 条）、文案 `MACHINE_PROFILE_*` / `MACHINE_BLURB_*` |

**六型定稿取值**（`data/balance.json`；口径与依据见 §1.17 / §1.19）：

| 机型 | 数值加成 | 数值代价 | 能力强化 | 能力代价 |
| --- | --- | --- | --- | --- |
| 标准型 | — | — | 四条轴全基准 | 四条轴全基准 |
| 游隼 | 移速 ×1.15 | 攻击 ×0.90 | 冲刺冷却 ×0.85（4.0→3.4s） | 弹反循环 ×1.10（3.8→4.1s） |
| 壁垒 | 受伤 ×0.85 | 开火间隔 ×1.12 | 弹反窗 ×1.40（0.50→0.70s） | 冲刺冷却 ×1.10（4.0→4.4s） |
| 连弩 | 开火间隔 ×0.85 | 受伤 ×1.12 | 弹反循环 ×0.85（3.8→3.35s） | 加速耗油 ×1.15（续航 2.86→2.48s） |
| 重锤 | 攻击 ×1.20 | 移速 ×0.90 | 加速耗油 ×0.87（续航 2.86→3.28s） | 冲刺冷却 ×1.10（4.0→4.4s） |
| 巨像 | 血量 ×1.20 | 移速 ×0.88 | 加速耗油 ×0.82（续航 2.86→3.48s） | 弹反循环 ×1.15（3.8→4.25s） |

**六维指纹实算值**（相对标准型的倍数；中心 1.00、刻度带 [0.60, 1.40]，括号内为归一强度）：

| 机型 | 火力 | 机动 | 装甲 | 弹反 | 突进 | 续航 |
| --- | --- | --- | --- | --- | --- | --- |
| 标准型 | 1.000 (0.50) | 1.000 (0.50) | 1.000 (0.50) | 1.000 (0.50) | 1.000 (0.50) | 1.000 (0.50) |
| 游隼 | 0.900 (0.38) | 1.150 (0.69) | 1.000 (0.50) | 0.927 (0.41) | 1.176 (0.72) | 1.000 (0.50) |
| 重锤 | 1.200 (0.75) | 0.900 (0.38) | 1.000 (0.50) | 1.000 (0.50) | 0.909 (0.39) | 1.149 (0.69) |
| 连弩 | 1.176 (0.72) | 1.000 (0.50) | 0.893 (0.37) | 1.134 (0.67) | 1.000 (0.50) | 0.870 (0.34) |
| 壁垒 | 0.893 (0.37) | 1.000 (0.50) | 1.176 (0.72) | 1.400 (1.00) | 0.909 (0.39) | 1.000 (0.50) |
| 巨像 | 1.000 (0.50) | 0.880 (0.35) | 1.200 (0.75) | 0.894 (0.37) | 1.000 (0.50) | 1.220 (0.77) |

---

## 3 验证与当前基线读数（复制即用）

```bash
dotnet build InfiAir.sln                                          # 0 Warning / 0 Error（TreatWarningsAsErrors）
dotnet test csharp/tests/InfiAir.Core.Tests/InfiAir.Core.Tests.csproj   # 769 条通过
bash scripts/ci/check_tool_tests.sh                              # 82 条通过
python3 scripts/tools/balance_analysis.py                        # 看「机型能力层」节
python3 scripts/ci/gates.py                                      # 五步：build / unit_tests / tool_tests / import / smoke
godot-mono --headless --path . --fixed-fps 60 --quit-after 300   # 冒烟：开机直达标题屏
```

**最近一次实测**：门禁 **5/5 通过**（约 9–11s），core 单测 **769 条**、工具链自测 **82 条**，冒烟打 `[boot] 标题屏就绪`。

**本批已做过的破坏验证**（接手者可复跑以确认判据真的会红）：

- 删一条 `MACHINE_TAG_*` 文案键 → `MachineRosterTests.CopyKeysExistInBothLanguages` 红；
- 把铭牌板宽改回 460 → `MachinePlateLayoutTests` 的「内容框装得下最长行」红；
- `MachineKitTests` 的 15 条（域 / 回退 / 六型两两不同 / 异轴 / K1 / K2 / K3 / 比值带 / 亚一层 / 设计映射 / balance 对账与键白名单）各给了变异方式；
- `MachineProfileTests` 的 15 条（六轴取值逐格 / 两两不同 / 归一与钳制 / 顶点角度与闭合 / 退化护栏）。

**判据分布**：`csharp/tests/InfiAir.Core.Tests/Machines/{MachineKitTests,MachineProfileTests,MachineRosterTests}.cs`、`csharp/tests/InfiAir.Core.Tests/MachinePlateLayoutTests.cs`、`scripts/tests/test_balance_analysis.py`。

---

## 4 本批**没做**、但值得知道的一条自检

**指纹图与铭牌版式没有窗口化过目**（执行环境无窗口；只做了字体实测 + 几何判据 + 无头冒烟）。

- 已核过的：最长文案行的像素宽（`NotoSansSC` 实测，写进 `MachinePlateLayout` 常量）、板心对齐、板顶不压机体实心像素、板底不压 note 与提示行、图表盒与文本区装得进板内、标签矩形被控件二次钳制。
- **没核过的**：实际观感（六边形在 253px 盒里的粗细观感、深色背景上的对比度、中英两档的实际断行长相）。
- 若有窗口条件，值得做的动作：开标题屏 → **M** → 逐型转一圈，看四件事：① 六个轴名是否都完整显示且在盒内；② 机型名 + 性格标签、性格文案两处都不折行；③ 板不压机体、note、提示行；④ 切到英文再看一遍。
- 按 `AGENTS.md` §3「视觉 / UI 布局」与 ROADMAP 的「观感由工程判据随批收口、不设人工验收环节」，这**不是阻塞项**：玩家反馈是唯一返工触发源。发现问题时的旋钮全在 `MachinePlateLayout`（版式）与 `MachineProfileTable`（刻度带）。

---

## 5 待办（**都已登记，触发条件未到就不要主动开工**）

`docs/ROADMAP.md` 的「已知债务与开放发现」区已登记四条与本批相关：

| 级别 | 是什么 | 收口条件 | 触发时机 |
| --- | --- | --- | --- |
| 中 | **教程内没有一次「有代价地使用冲刺」的时机**（阶段 2 教了、阶段 3 是零风险锁血场） | 阶段 3 放一次只有冲刺能过的窄缝齐射，缝隙几何下沉 core 并可单测 | 有人反馈「教程教完不知道冲刺什么时候用」 |
| 低 | **机型能力轴的感知下限是推断值、未实测**（0.15 / 0.30 / 0.40 / 0.30s） | 练习场逐型自测 + 2AFC（**加成侧与代价侧分别测**，自变量含手柄 / 键鼠），测完校下限常量一处 | 第一次实机自测 |
| 低 | **easy 档首个里程碑 55.7s**（medium 20.8s / hard 18.6s）：净点数节奏 easy 1× 是**有意口径** | 玩家反馈「轻松档起手太慢」时改 `difficulty.easy.milestone` 一处（并同步分析工具的回归带） | 该反馈出现 |
| 低 | **六型是四轴集的容量上限**（四轴 × 五型是鸽笼，必有一条轴承担两次强化） | 加第 7 型之前先加第 5 条轴，或显式接受同轴同值 | 要加新机型时 |

**另外两条「审查提过、本批有意未做」**（备查，不建议主动开工；要做先按 §2 定稿）：

1. **机型 × 满级增幅的组合矩阵判据**：把「机型能力轴 × {`phase_dash` 满级、`deflector` 满级、`efficient_boost` 满级}」的组合断言成「不进燃料钉死区、冲刺冷却 ≥ 燃料地板、弹反窗 ≤ 流程时长」。本批只在 `balance_analysis.py` 里做了防回归体检（综合容错跨度 ≤ 1.325×），core 侧没有这条判据——因为它的输入（增幅因子）在 `Player` 里。
   现存的已知组合效应：**游隼满投资时冲刺冷却 1.3175s 已低于燃料地板 1.50s**，即它的机型收益在高投资段被燃料吃掉一部分（不是坏，是「冷却再短也冲不出来」）。
2. **标量化上界进 core 单测**：现在只在 Python 体检里（`scripts/tools/balance_analysis.py` 的跨度判据）。要进 core 得先把「输出 × 有效生命 ÷ (1 − 冲刺免伤占空比)」这套算式下沉。

---

## 6 已知坑（接手者最容易踩的十处）

1. **冲刺冷却乘区的唯一落点**是 `Player._dashCooldownMax`（置值与 HUD 充能环分母同源）。`PlayerDash` 里那个同名字段已被删除（`8478f12`）——所以现在**接错会编译失败**，这是故意的。
2. **弹反窗与弹反冷却必须自 cfg 基准重算**（`_parryWindowBase` / `_parryCooldownBase`）。乘在 `_parry.Duration` / `ActiveTime` 的**当前属性**上，会在换机重跑时一次次往上叠乘。
3. **`Player.LoadBalance` 跑在 `Player._Ready`，早于读档换机**（`Main._Ready` → `ApplyRunDict` → `MachineChanged`）。任何与机型相关的量**不能只落在 `LoadBalance`**，否则「标题屏改过偏好又点继续出击」时会停在错档（症状：同一台机「窗错、冷却对」）。
4. **域收口口径**：越界**取边界值**、非有限值（NaN/Inf）才回 1.0。反过来写（越界回 1.0）会把 >1.0 的代价静默抹平——那正是 `MachineKitTable.Sanitize` 要防的坏法。
5. **弹反窗的域上限是 1.5**：`ParryTimeline` 把 `ActiveTime` 钳到流程时长 0.8s，乘区进 [1.6, 2.0] 是死区（改了没反应）。设计上限更低（1.40）。
6. **铭牌 / 指纹图的尺寸常量单源在 core `MachinePlateLayout`**；字体度量部分是 `NotoSansSC` 的**实测像素值**——改字号、改文案、换字体都要重量并改常量，否则英文行会静默折行或越界。
7. **指纹图的刻度带 [0.60, 1.40] 不要重排**：越界钳到边界（顶格本身就是读数），重排会让同一张图在两个版本里的长度不可比。
8. **多边形退化**：六个顶点全部重合时引擎**静默不画**（不报错）；`MachineProfileChart` 有 `MinDrawStrength` 兜底，真正防自交的是 core 的固定角度顺序（写错画序才会打 `Invalid polygon data, triangulation failed.`）。
9. **冒烟抓不到「值不对」**：300 帧只会跑到四处消费点（默认机型是标准型、乘区恒等），接错/漏乘都不报错。**本批的回归面全靠 core 单测**。
10. **`data/translations.csv` 有多行字段**（例如 `TUT_S2_OBJ` 是一条记录跨两行）：改文案时别破坏行结构，工具链自测会查行尾与形态。

---

## 7 反转链（怎么撤）

- **整个 P1 机型玩法层**：删 `machines.*.kit.*` 与 `csharp/core/Machines/MachineKit.cs`、`MachineSpec` 的 `KitBonus` / `KitPenalty`、四处消费点，即回「③玩法层冻结」态（§1.19 全文可删）。
- **冲刺开局可用 + `phase_dash` 纯强度**：恢复 `Player` 的解锁判定与 HUD 的 `SetLocked`（`git show 855ac4c^:csharp/godot/AbilitySocket.cs` 取回三态绘制）、指数换回 `Mathf.Max(eff−1, 0)`、教程阶段 2 恢复白送一级。
- **六维指纹图**：删 `MachineProfile.cs` / `MachineProfileChart.cs` 与铭牌里的图与文案行，`MachinePlateLayout` 常量回 `35c9ca4^` 一版；特性演出的偏移未动，无需复原。
- **断油阈值**：`player.fuel.restart` 改回 30。**记录机型**：删 codec 的 `machine` 键与 `BestRecord` 的机型字段。
- 每条决策的完整反转链都写在 `docs/ROADMAP.md` 的 Decisions 区（一行制 + 反转链）。

---

## 8 接手后的第一批动作（checklist）

1. `git log --oneline -10` + `git status` 确认与本文件一致；`python3 scripts/ci/gates.py` 复跑一次拿到基线。
2. 读 `docs/DESIGN_BASELINE.md` §1.17 → §1.19（三层口径连起来读一遍）与 `docs/ROADMAP.md` 的债务区。
3. 按 §5 的表格判断当前是否有「触发条件已到」的待办；没有就**保持现状**（玩家反馈是唯一返工触发源）。
4. 有新需求时：先改单源（`balance.json` / `MachineKit.cs` / `MachineProfile.cs` / `MachinePlateLayout.cs`），再同批改文案与判据，最后 `DESIGN_BASELINE` + `RELEASE_NOTES` + `ROADMAP` 三处文档同步，门禁全绿后按 `AGENTS.md` §6 拆分提交。
5. 确认无遗漏后**删除本文件**（`rm docs/HANDOFF.md`，同批提交）。
