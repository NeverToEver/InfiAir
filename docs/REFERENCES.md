# 公开素材、许可证与行业实践参考

> **非纪律存档**（与 `docs/BALANCE_REVIEW.md`、`docs/RELEASE_NOTES.md` 同族）：记录一次联网调研取到的**事实与出处**，供后续决策引用。
> 它不是口径单源——设计口径见 `docs/DESIGN_BASELINE.md`，流程与门禁见 `AGENTS.md`，方向与开放项见 `docs/ROADMAP.md`。
> 调研日期 2026-09-18；所有引号内文字为对官方页面的**逐字摘录**（链接见 §5）。凡未能核验的来源一律标注「需人工核验」，不做条款推测。
> 落地状态：可采纳候选与行业建议已按批次消化——依据被各批次引用后落进 `docs/DESIGN_BASELINE.md` 对应节与 `docs/ROADMAP.md` Decisions（2026-09-19 起「待人类裁定」节已取消，取值按依据直接定稿、可随时改判）。

## 1 素材来源与许可证事实

口径：「再分发」指随游戏二进制/发布包分发。**能零摩擦使用的只有三类**——CC0 1.0 / Unlicense（无署名义务、可商用、可入包、无传染）、OFL 1.1（字体，可按条件嵌入与随包分发）、MIT / ISC / Apache-2.0（图标与代码类，需随副本保留许可与版权声明）。

| 来源 | 许可 | 署名 | 商用 | 再分发进包 | 传染 | 核验 |
| --- | --- | --- | --- | --- | --- | --- |
| Kenney.nl | CC0（站点 FAQ + 每个素材页两处标注） | 不需要 | 允许 | 允许 | 无 | 官方原文已核验 |
| OpenGameArt | 逐条目（CC0 / CC-BY 3.0·4.0 / CC-BY-SA 3.0·4.0 / GPL 2·3 / OGA-BY） | 视条目 | 允许 | 视条目 | 视条目 | 条目页与 FAQ 已核验 |
| Freesound | 逐条（CC0 / CC-BY 4.0 / CC-BY-NC 4.0；旧库另含已退休的 Sampling+ 1.0） | CC0 不需、CC-BY 必需 | CC0/BY 允许，NC 不允许 | 允许（受许可约束） | BY 无、NC 非商用 | 官方 FAQ 已核验 |
| Game-icons.net | CC BY 3.0 | 必需（逐图标作者不同） | 允许 | 允许 | 无 | about 页已核验 |
| Google Fonts / Noto 系 | SIL OFL 1.1 | 无「署名」义务，但须随副本附版权声明与许可全文 | 允许（含随软件售卖） | 允许 | 仅覆盖字体自身衍生物 | OFL 全文与 FAQ 已核验 |
| Poly Haven | CC0（全站） | 不需要 | 允许 | 允许 | 无 | license 页已核验 |
| ambientCG | CC0 1.0（全站） | 不需要 | 允许 | 允许（官方明示可入游戏） | 无 | license 页已核验 |
| Godot Shaders | 逐条（CC0 / MIT / GPL v3；**图片与视频不在其中**） | MIT 需保留声明 | 允许（GPL 除外） | 允许（GPL 除外） | GPL v3 有 | FAQ 已核验 |
| Lucide / Feather / Material Symbols | ISC / MIT / Apache-2.0 | 需保留声明 | 允许 | 允许 | 无 | 仓库许可文件已核验 |
| incompetech（Kevin MacLeod） | CC BY 4.0（另售付费授权） | 必需（站点指定格式，且要求「放在玩家看得见的地方」） | 允许 | 允许 | 无 | FAQ 已核验 |
| incompetech 之外的 Pixabay / itch.io / Wikimedia Commons / Shadertoy / Sonniss / Musopen / sfxr 原版 | — | — | — | — | — | **需人工核验**（403 或域名不可达，卡点见 §5 末） |
| FreePD.com | 站点已于 2025 年永久关闭 | — | — | — | — | 首页公告已核验（**公有领域源也会消失**的现成反例） |

关键条款要点（原文逐字见 §5）：

- **CC0 1.0 §4**：不豁免商标与专利，且「makes no representations or warranties of any kind」——挑选时按「能不能想象它出现在商业产品的商店页上」做二次筛（可识别真人、商标、知名 IP 元素不进）。
- **CC BY 4.0 §3(a)(1)**：共享时须保留作者、版权声明、许可声明、免责声明与作品链接，并**标明是否修改**；§2(a)(6) 明示不构成原作者背书。
- **CC BY-SA 4.0 §3(b)**：改编物必须继续以 BY-SA 分发 → 与「MIT 仓库 + 闭源商业发行」**硬冲突**（OpenGameArt 上大量高质量素材属此类，挑选时逐条看 `License(s):` 字段）。
- **OFL 1.1**：可随任意软件捆绑、再分发与售卖，条件是每份副本含版权声明与许可全文（可作独立文本文件）；字体自身及衍生物须继续以 OFL 分发，但**用字体排出来的作品不受传染**；不得单独售卖字体。
- **OFL-FAQ 1.10**：字体嵌进可执行体时可省独立许可文本，但 FAQ 明确「strongly recommend against」走这条捷径——本仓库把 OFL 全文平铺进发布包，属更保守也更安全的形态。

## 2 与本项目纪律的冲突分析（含已收口的一条）

1. **「素材一律程序化生成」+ `NOTICE` 的单源声明**：把下载素材放进 `assets/` 会让 `NOTICE` 的「唯一第三方素材是字体」立刻失真。
2. **门禁与纪律之间曾有一条空隙（本批已收口）**：素材门禁原本判的是「生成器确定 + 产物与生成器同步」，一个**已提交、生成器不产出**的手工素材不会被判红（跑完 `regenerate_all.sh` 后 `git status` 依旧干净）。现已补「产物来源登记表」判定：`assets/` 与 `data/` 下每个被跟踪的非 `*.import` 文件都按来源分类（生成物 / 手写源 / 第三方 / 引擎伴随文件），未登记的新增、登记项失效、登记为生成物却没被本次重跑刷新，三种形态都判红。
3. **玩家可见文案单源**：任何需要署名的许可（CC-BY 类）都要求在玩家可见处署名，而本仓库玩家可见文本只能来自 `data/translations.csv` 且「新增玩家可见内容与文案」属人类决策 → 可通过关于页满足，代价是新文案键（中英双语 + 过文案门禁）。
4. **传染性许可（CC-BY-SA / GPL）无解**：与发布形态直接冲突，不采纳。
5. **发布包合规面**：`release.sh` 会平铺 `LICENSE` / `NOTICE` / `assets/fonts/NotoSansSC-OFL.txt` 并逐件断言；引入任何新第三方素材必须同步扩这三件清单（v3.34 不发布的原因正是包内缺授权文本）。
6. **合规惯例**：行业通行做法是「人读的 `NOTICE` + 每种许可的全文文件 + 逐条条目（组件 / 版本 / 来源 URL / 版权行 / 许可标识 / 是否修改）」，本仓库现有结构已是该形态；素材类二进制文件可用 REUSE 规范的 `.license` 伴随文件挂许可（PNG/WAV 无法写注释头）。CC0 虽不要求署名，工程上仍建议留「来源 URL + 抓取日期」（自证合规 + 源站可能消失）。

## 3 可采纳 / 不可采纳

**可采纳（按性价比；均属提议）**

| 候选 | 用途 | 落地要做什么 | 代价 |
| --- | --- | --- | --- |
| 新增 OFL 字体（Google Fonts / Noto 系） | 标题字、数字字 | 扩 `NOTICE` + `release.sh` 三件套 | 制度成本近零；体积与渲染一致性需过目 |
| ambientCG / Poly Haven（CC0） | `docs/` 配图与美术参考（不进 `res://`） | 注明来源与抓取日期 | 与 2D 程序化美术基本无关，仅参考价值 |
| Kenney（全站 CC0，含 Audio 分类） | 音效 / 音乐候选 | 若入包：扩 `NOTICE`、扩 `release.sh` 清单、改「素材一律程序化生成」口径 | 破政策；门禁判据要重写 |
| Godot Shaders 的 CC0 / MIT 条目 | 着色器参考 | 逐条核验页尾许可、避开 GPL v3 | 本仓库着色器已是 MIT 自有资产，收益有限 |
| jsfxr（移植仓库含 UNLICENSE） | 只作合成算法参考，代码重写为纯 Python | 只读参考、不复制代码 | 收益是思路不是素材；原版 sfxr 许可待核验 |

**不可采纳**：CC-BY-SA 3.0/4.0（含 OpenGameArt 的 LPC 系）、GPL 2/3 素材与着色器、Freesound 的 CC-BY-NC 与 Sampling+ 1.0、许可未核验的 Shadertoy / Pixabay / Sonniss / itch.io 未标注包、Wikimedia Commons（CC-BY-SA 占比高且叠肖像/商标/全景自由风险）、以及任何带第三方权利的 CC0/PD 内容。

**「用 CC0 音效替换程序化合成」的时间账（本机实测，Python 3.12.3）**：`generate_audio.py` 全量合成 13 个 wav 墙钟 **145 秒**（那是在多路门禁并行的负载下测的）；把三首 BGM 摘掉后，10 个音效只要 **0.78 秒**——即素材门的耗时几乎全在三首 BGM（112 秒音频）的纯 Python 逐样本合成。**换掉 BGM 才能省时间，换音效几乎不省。**

## 4 行业实践对照（只列结论与出处）

### 4.1 弹幕射击 / 街机

| 实践 | 本项目现状 |
| --- | --- |
| 判定点远小于机体、且判定点可见 | **已做到**：判定盒 2.8px、擦弹环内层豁免、判定点有光点与光圈（`Player.cs`；`balance.json player.graze_radius`） |
| 命中反馈：顿帧 2–10 帧 + trauma 震动模型（GDC《Juicing Your Cameras With Math》） | **已做到且取值已定稿**：四档顿帧 0.03/0.07/0.11/0.16s、trauma 模型（`core/GameFeel/`，`--feel-probe` 断复位） |
| 弹幕不得仅靠色相区分（XAG 103） | **基本做到**：高对比档**默认开**，给敌弹亮色轮廓（亮度编码优先于色相）；两档仍共用同一多边形——形状编码的另一半是待裁定条目「敌弹独立形状」（见 ROADMAP） |
| 难度曲线：单调爬升 + 软上限 + 显式地板/硬顶 | **已做到**（同类里的上乘：曲线形状可单测，`--long-probe` 断单调/有顶/斜率独立/软上限） |
| 擦弹与连击构成风险回报闭环 | **已做到**：擦弹吃难度乘区与连击加权，单弹只计一次 |
| Boss 转阶段清弹 + 有限无敌 + 超时逃跑阀门 | **已做到**（`--boss-probe` 覆盖阶段机与狂暴复位） |
| 弹数预算（怒首领蜂约 245 发同屏为参照） | **已做到**：敌弹硬上限 500、到顶按模拟时间节流告警；本作上限约为该参照的 1.6–2.0 倍 |
| 双向 rank（死亡降难度，Battle Garegga 式） | **不建议**：与「必死曲线 + 压力无界」冲突；现有受击喘息已是收窄版 |

### 4.2 无障碍（GAG Basic/Intermediate/Advanced、XAG、APX）

**已做到**：音量三分路、减少闪光、屏幕震动强度（0% 即关）、命中顿帧强度、高对比弹体、可改键与灵敏度/死区、辅助瞄准三档、帧率与垂直同步、练习模式（无失败练习）、难度可局中修改、可跳过的返场过场、检查点存档。

**未覆盖（全部已登记为提议）**：长按通道的「切换」替代（GAG Intermediate Motor；本作三条蓄力通道是纯长按）、文本对比度与准星配色选项（GAG Intermediate Vision）、文字界面背后的动效开关（XAG 117，注意它明确「活跃玩法期间的背景移动不在约束内」）、过场可暂停（XAG 108）、游戏内无障碍特性说明（GAG Basic General）。

**闪烁的量化判据（XAG 118 / WCAG 2.3.1）**：亮度变化 ≥10% 记一次「闪」，**超过约 3 次/秒**、或占屏面积 ≥20%、或低强度长时间持续即判失败；红色闪（`R/(R+G+B) ≥ 0.8`）阈值更低。本项目已把这条落成判据：全屏尺度脉冲源单源在 core `FlashBudget`，单测断言频率 < 3Hz 且减闪下振幅为零（两半互补），探针另按正反两半采样低燃料警戒脉冲；**一次性闪光**（能力槽就绪脉冲、血条掉段闪、Boss 阶段闪）无频率、不进频率表，但抑制处置单源在同处（`AllowsOneShot`），由设置趟（就绪脉冲）与 Boss 趟（掉段闪 / 阶段闪）按正反两半各采样一次。面积与时长那两条不在判据面内（本作全屏闪烁均为低占空比、无红色闪烁）。

**已裁定不做、不要再当缺失提**：游戏速度可调（速度即难度、与必死曲线耦合，三档难度 + 辅助瞄准覆盖同一诉求）、屏幕朗读与字幕（无配音与叙事对白）、局内计分显示、在线排行榜、移动端与触屏、随存随取（惩罚性存档是设计的一部分）。

### 4.3 自动化测试与确定性

| 实践 | 本项目现状 |
| --- | --- |
| 固定步长 + 判定只看模拟状态（Godot `--fixed-fps` 禁用实时同步） | **已做到且严于多数同类**：五条确定性硬规则 + 真实时间允许清单双向锁 |
| 「输入 + 种子 = 可复现的一局」 | **一半**：表现层随机已确定化（星场/残景/特效），**玩法层随机仍走 `GD.Rand*` 默认序列**（暴击、标记、刷怪、Boss 走位、迷雾触发）→ 种子注入是「实现者可做」的开放项 |
| bot/soak 的判据要简单、能失败、指向具体链路（arXiv 2202.12777：业界对自动化代理持怀疑，关键在判据而非拟人性） | **已做到**：`--autoplay-probe` 判「整局零击杀 / 存活却 60 模拟秒无进展 / 跑局中的引擎错误」，每 10 模拟秒一条遥测；长局 soak（900 秒）走人工项 |
| soak 暴露泄漏与性能衰减（Valve 开发者 wiki） | **部分**：对象池化全面、有「池化复用后缓存仍连着」的探针，但退出期资源统计走白名单（泄漏类不判） |

### 4.4 Godot 4 / GL Compatibility

- **已做到**：帧内零 `GetNode`/零 LINQ/零容器分配（脚本扫描全库）、热路径配置在 load 期缓存、`FrameCache` 帧级共享缓存、按需重绘纪律、`Callable`/信号只在构建期绑定、`GD.Load` 的生命周期取舍有注释说明。
- **已核验的文档订正**：官方渲染器对照表把 **Glow/SSAO 在 Compatibility 标为支持**——不支持的是 2D/3D HDR 渲染（辉光拿不到 HDR 阈值语义）、compute shaders、`CompositorEffects`、particle trails、MSAA 2D、debanding、decals、depth of field、SDFGI、volumetric fog、SSR。本作手写全屏后处理的真实理由是「引擎给不了辉光＋调色＋晕影＋颗粒＋动态分级这一整套组合」（口径已订正到 `DESIGN_BASELINE` §2.2 与该 shader 头注释）。
- **启动期着色器预热**：Compatibility 下 ubershader 与预热机制不适用（只适用 Forward+/Mobile），官方建议在加载期把材质/着色器/粒子显示一帧；本作实测单帧 > 1.4s，当前处理只是钳制推进步长（保状态机不连跳），不是消除卡顿 → 已登记提议。
- **不建议采纳**：把子弹改成 `MultiMesh`（官方定位是成千上万实例，本作敌弹上限 500 且要保留 `Area2D` 碰撞与既有对象池语义）、server API 化（同因）、把物理 tick 提到 120Hz（会改「帧数＝模拟时长」的既有口径）、接入第三方遥测/崩溃上报 SDK（违反运行时零第三方依赖）。

### 4.5 智能体指令文件与工程规则的复杂度治理（2026-09-18 调研）

背景：本仓纪律单源 `AGENTS.md` 每轮注入智能体上下文；实测两日内从 15369 涨到 18119 字符（+18%），`ROADMAP` 同期 34108 → 43208（+27%），而五周内已精简过 6 次——每次「删表述、不删规则」，几周后又涨回。故专项调研「指令膨胀」与「规则只增不减」两件事的业内做法。

| 实践 | 本项目现状 |
| --- | --- |
| **指令文件的体量上限是官方口径**：Claude Code 官方「每个 CLAUDE.md 目标 <200 行」；Cursor 官方「规则保持在 500 行以下」；GitHub Copilot 云端代理生成提示「不超过 2 页」。`agents.md` 规范本身不给体量建议 | **本节起有护栏**：`AGENTS.md` 设 11000 字符预算，由 `check_gate_wiring.sh` 双向断言（超预算红、被截断也红）。按字符而非行数——本文件中文字符数与 token 成本直接对应，行长与本数无关 |
| **「150–200 条指令」是社区分析不是厂商口径**：HumanLayer 的实测分析（前沿思考模型约能一致遵循 150–200 条；Claude Code 系统提示自身约占 50 条）。IFScale 基准侧面印证：指令偏置峰值落在 150–200 条，300 条以上坍缩为均匀失效，500 条时最强前沿模型仅 68% 准确率——注意它测的是偏置峰值而非遵循上限 | 按字符数设预算，不按条数（中文条数难以机器统计）；本次外移后正文规则条数显著下降，门禁判据全部落到脚本注释 |
| **指令越多，整体遵循率均匀下降**：Anthropic 官方称 context rot / 注意力预算（n 个 token 产生 n² 对注意力关系，「每引入一个新 token 都在消耗预算」）；Chroma 用 18 个模型实测「随输入变长性能越来越不可靠」；经典结论「相关信息在中间时性能显著下降」（lost-in-the-middle） | 这正是「精简 → 回涨 → 再精简」循环的真实代价：多出的不是知识而是负担。预算门禁把这点变成可失败判据 |
| **反证据（重要）**：ETH Zurich 预印本在 SWE-bench 与真实仓库问题上实测「提供上下文文件总体上不提升任务成功率，且平均增加 20% 以上推理成本」，模型自动生成的概览类文件约等于没有甚至略差；**只有「指定非标准做法 / 工具」的内容有效** | 本仓纪律几乎全属有效区（非标准做法 + 判定指针：门禁脚本、探针口径、单源表）；被外移的恰是「复述已有判定」的那部分（门禁表）。这条反证据支持继续按「要么是非显然的约定、要么是判定指针」两条来筛正文 |
| **渐进披露（progressive disclosure）是主流答案**：Claude Code 支持子目录 CLAUDE.md（只在该目录文件被读取时载入）；Skills 三层（元数据常驻 → SKILL.md 按需 → 附件再按需）；Cursor 四类规则（always / 按 glob 自动附着 / 模型按描述自取 / 手动 @）；Cline 路径条件规则（「只给你当下需要的那一页」） | **本仓已判定不做**（2026-09-18）：核实 harness 语义＝只加载一份工作区 `AGENTS.md`（自 cwd 向上遇到的第一个即停），子目录级文件不会被载入、还会**遮蔽**根文件；故关键规则必须留在根文件，细节一律走指针（`gates.py --list` / 各脚本注释 / `ProbeHost*.cs`）。换 harness 或 harness 支持按目录加载时重开该判定 |
| **按需加载会漏读，且官方明确列举**：指令「消失」的三个原因——只在对话里给过、位于尚未重新载入的嵌套文件、路径作用域规则未匹配到文件 | 故关键规则（决策权、完成定义、确定性硬规则）一律留在常驻层，只把「可自行查阅」的判据细节放指针后面 |
| **「能机检的别写进提示词」已是多家官方共识**：Claude Code（指令是指引性的、hooks 是确定性的，模型本来就会做对的「删掉它或转成 hook」）；Cursor（整篇风格指南→改用 linter）；VS Code（跳过标准 linter/formatter 已强制的约定）；HumanLayer（「永远不要派 LLM 去干 linter 的活」） | **本仓已在有效侧**：绝大多数纪律由 `check_*.sh` 机器判定，提示词只写口径与指针。本次进一步把「判据复述」从提示词里移出 |
| **膨胀是默认状态、删除有成本**：对 1867 个仓库 / 247694 条指令生命周期的实测——代理提示词终身 +226%，每次提交净增 4.9 条，**指令越老越难删**；给指令写「为什么」的注释可消除 99.3% 的多余指令 | 对策两条：体积预算（使增长必须伴随删除或显式抬预算）＋ 新门禁准入要求写明**退役条件**（写下去的时候就把「什么情况下可以删」定好，抵消极难删的老化效应） |
| **规则退役是制度化动作**：Kubernetes 弃用政策（弃用后须继续工作 N 个月/N 个版本）、Python PEP 387（弃用期至少两年、不得无通知删除）、JDK JEP 182、Bazel `--incompatible_*`（破坏性变更前置一个发布期）、ESLint（只有被替代或存在等价插件规则时才允许移除） | 采纳最小版：`AGENTS.md` §6 准入门槛加「写明退役条件」。不做 ESLint 式「无替代不删」——那会锁死规则集，适合公共 API 不适合内部门禁 |
| **测试与验证要有成本预算，而不是「能跑就行」**：Bazel 按测试尺寸给超时与资源（small 1 分钟/20MB、medium 5 分钟/100MB、large 15 分钟/300MB）；Google 约 80/15/5 配比且 large 测试隔离出开发者工作流 | 本仓已有：`gates.py` 每步墙钟上限、冒烟每趟上限、全跑 ≤200 秒预算（实测 154s）。缺口＝本地对 md-only 改动仍跑全量（CI 本就不触发）→ 本次改为 `--only gate_wiring` |
| **测试影响分析（TIA）**：Meta 的预测式选测在 monorepo 上把测试基础设施成本降 2 倍，仍报出 >95% 的个体失败与 >99.9% 的坏变更；微软 TIA 明确「无法理解变更时**回退跑全量**」 | 本仓的等价物是 `--only <slug>` + 分层验证；安全网＝CI 对任何非 md 改动仍跑全量，拿不准按全量走（已写进 §4） |
| **「规则必须自己挣得起成本」**：Google 用 **effective false positive**（工程师看到告警后没采取任何正向行动即算）衡量静态分析是否值得保留；豁免申请频发被视为「该规则需要澄清或修订」的信号；SWE Book 另称「CI 100% 绿和 100% 可用率一样昂贵」，反对「CI 不绿不许提交」的硬政策 | 本仓已落地配对机制：`docs/GATE_LEDGER.md` 记每道门禁「最近一次拦到真缺陷」，与 §6 准入门槛要求的**退役条件**配对使用（行集由门禁断言，缺行即红）；台账明写「没有命中记录不等于可删」，低频高价值面按风险保留 |
| **文档生命周期**：Google 给文档挂 freshness date（记录 owner 与最近复审，超期当 bug 跟踪），主张「文档无用就移除或显式标注过期并指向新处」；GitLab 明文规则 **「Delete instead of hiding documentation」**（隐藏会过期、会留孤儿，该删就删、靠 Git 历史回捞）；ADR 的 accepted 条目不改写、只被新条目取代（单页为宜） | 本仓同族纪律（活跃文档只写现状 + 开放项、修完直接删条目、反悔留痕不改旧条目）已补上缺口：`ROADMAP` 决策史按周期分层（当前周期留正文、更早原文进 `docs/DECISIONS_ARCHIVE.md`），且 `ROADMAP` 与 `AGENTS.md` 都有体积预算，**超预算即触发归档**而非放宽容忍 |

### 4.6 准星自定义选项与分享码（2026-09-18 调研）

背景：人类委托给准星引入成熟的自定义系统（大小/颜色/范围/形状 + 多档案持久化 + 跨设备复刻的准星码），要求先查业内主流可调选项。

| 实践 | 本项目采纳 |
| --- | --- |
| **Valorant 的选项面**是「颜色（预设 + 自定义 RGB）+ 内/外四线（显隐/长度/粗细/间距/不透明度）+ 中心点（显隐/大小）」；职业玩家几乎只调内线 | 采纳为核心参数面：size/thickness/gap/alpha/中心点（含大小）；「外线随射击误差移动」类动态项不采纳（本作无后坐力弹散，交战反馈走交战状态机） |
| **CS2 的选项面**在此基础上多出：风格档（静态/动态）、T 形（去一段线）、描边（drawoutline + 粗细）、整体 alpha、负间距 | 采纳 T 形、描边、alpha、gap 全值；「动态扩散」不采纳（无弹散误差可表达） |
| **形状语汇**：主流 FPS 是四线十字 + 中心点/圆点；本作原生语汇是四角 bracket（军事 FUI 族） | 形状档 = bracket（默认，守 FUI 语汇）/ cross（经典）/ circle / dot 四档；bracket/cross 支持旋转档（45° 即 X 形），circle/dot 旋转无意义 |
| **分享码**：CS2 `CSGO-xxxxx-xxxxx-…`＝base64 编码的全部准星设置，设置页内「Share or Import」粘贴互导 | 采纳同构交互：`INF1-` 前缀 + Crockford Base32 载荷 + CRC-8 校验 + 版本字节；格式规格见 `DESIGN_BASELINE`（本文档不复制） |
| **多档案**：CS2/Valorant 均为单套配置 + 分享码备份；「多档案并存」见于社区准星生成器与部分竞技游戏配置档 | 人类明确要多档案：本地档案簿（种子 4 预设 + 玩家自建），active 索引切换；名字不入码（跨设备复刻样式，名字本地化） |

来源：`ign.com/wikis/valorant/The_Best_Valorant_Crosshair_Guide`、`redbull.com`（Valorant 职业准星 11 项）、`totalcsgo.com`（cl_crosshair 命令全集与生成器）、`cybershoke.net`、`xplay.gg`、`dmarket.com`（CS2 分享码导入与 dot/gap 负间距实践）、`prosettings.net`（职业配置库）


### 4.7 战斗动效升级调研（2026-09-18 复核，联网可达后重取）

背景：人类委托「战斗动效全面升级（呼吸灯 / 舰船流光 / 趣味小动效 / 背景与界面互动）」。上一轮调研受网络限制，多条结论无一手出处；本轮复核后**三条硬约束落地**，其中一条推翻首版方案。

| 事实 | 逐字要点 | 对方案的影响 |
| --- | --- | --- |
| **Godot 渲染器对照表**（官方） | 「Glow ✔️ Supported」「Custom post-processing with fullscreen quad ✔️ Supported」；**❌ Not supported**：`CompositorEffects`、`MSAA 2D`、`Compute shaders`、`Debanding`、**`Particle trails`**、`Particle SDF collision`、`2D HDR Viewport`、`HDR output`；Color precision ＝「RGBA8. Low dynamic range, medium precision.」 | 手写全屏后处理是唯一路径（已如此）；**粒子拖尾在该后端不存在**——敌弹拖尾不能走 `GPUParticles2D`；无 HDR 语义，亮点在 1.0 截断 |
| **节拍同步**（官方 Sync the gameplay with audio and music） | 精确播放位置＝`get_playback_position() + AudioServer.get_time_since_last_mix() - AudioServer.get_output_latency()`；「The result may be a bit jittery due how multiple threads work. Just check that the value is not less than in the previous frame (**discard it if so**)」；长时间播放会与系统时钟漂移，须走声卡时钟（即 `get_playback_position` 路径） | 呼吸/节拍层采到相位后**必须做回退值丢弃**，否则全屏节奏会偶发倒跳 |
| **XAG 118 光敏性**（微软官方当前版） | 「A flash is defined as a **10% change in luminance** (where 100% is the maximum luminance of a white screen). The darker luminance value should be **below 0.8**.」失败判据三条并列：频率「approximately **more than three per second**」／面积「approximately **20 percent or more**」／「Lower intensity flashing can also cause a failure if it's continued for an extended period of time」；红闪阈值更低；「All games should be tested … regardless of whether the game includes intentional flashing」 | **首版方案被推翻**：全屏「每拍亮一下」即使 2.5Hz 合规频率，仍会踩**面积**判据（全屏＝100% ≫ 20%）。故全屏层只能做**低于 10% 亮度变化**的慢呼吸（低于「闪」的定义线，面积判据随之不适用），拍点一律下放到局部元素 |
| **战斗可读性**（Art Director 视角，2026） | 特效核心在暗背景上「never exceeds **80% white**」；亮度应「peaks sharply and decays rapidly」而非线性；「The player's eye … the brain processes **color before shape**」——UI 与危险物不得共用色相；**拖尾最致命**：「If a trail lingers for 100ms longer than the active hitbox … the player will roll dodge to avoid what looks like the blade, only to get hit」且会盖住下一段前摇，宁取 1–2 帧拉伸 | 敌弹只动画**自身四边形内的烘焙尾部**：不新增滞后几何、不新增 draw call，弹头与判定逐位不变 |
| **敌人可读性**（The Level Design Book） | 需要「Unique silhouette so players can discern enemy type at mid-range」与「Design details and animations that **telegraph enemy state, intent, and strengths/weaknesses**」 | 支持「损伤状态分级」而非堆装饰 |
| **弹幕美学**（Danmaku 设计指南） | 有方向弹体天然传达运动方向，圆形无方向弹「do not telegraph their movement direction at all」 | 弹体语言优先于装饰 |

**未能核验（需人工核验，不作条款推断）**：`shmups.wiki`（社区权威弹幕设计文档 Boghog's bullet hell shmup 101 的载体）本轮 TCP 层不可达，其镜像站返回 Cloudflare 520、`web.archive.org` 亦不可达；`old.reddit.com` 的同文转载要求登录。故弹幕可读性一节采用的是上述可访问来源，shmup 社区口径仍待补。
**已核验但未采纳**：把敌弹改 `MultiMesh`（官方定位为成千上万实例，本作需保留 `Area2D` 碰撞与对象池语义，见 §4.4）。

来源：`docs.godotengine.org/en/stable/tutorials/rendering/renderers.html`、`.../tutorials/audio/sync_with_audio.html`、`learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/118`、`mystpixel.com/posts/5-visual-clutter-culprits-ruining-your-indie-game-s-combat-readability/`、`book.leveldesignbook.com/process/combat/enemy`、`sparen.github.io/ph3tutorials/ddsga2.html`（检索入口：`html.duckduckgo.com`）

### 4.8 输入回应与交互性调研（2026-09-19）

| 实践 | 本项目现状 |
| --- | --- |
| **game feel 三层模型**（Steve Swink《Game Feel》：实时控制 → 可预测空间 → polish；顺序不可倒——控制不即时，polish 只是把迟滞放大） | 输入路径已无迟滞（直接 `_PhysicsProcess` 读 `IsActionJustPressed`）；polish 五轮已收口；缺口在「请求被拒时的回应」 |
| **输入宽容**：input buffering（早按的键在动作就绪瞬间生效）+ coyote time，窗口**远小于 150ms** | **本轮落地**：弹反/冲刺输入缓冲 0.1s（`player.input_buffer_window`；coyote time 是平台跳语境，本作无此形态，不做） |
| **每个动作都要回应**；「silent actions」与「shake/flash on everything」并列手感杀手（Vlambeer 手法表：muzzle flash + hit sound 让每次射击「violent and immediate」） | **本轮落地**：命中 tick 音 + 弹反专属确认音 + 冷却/燃料拒绝回应；开火/冲刺/击杀/受击/事件反馈此前已成套（§2.7–§2.13） |
| **声音承担一半权重**，是最被低估的通道；高频战斗音效须在声源限频防连发噪声 | 命中音走 SfxPlayer 目录表（45ms 最小间隔 + 复音 2 + 音高抖动防梳状滤波），与既有目录纪律一致 |
| **关键帧 hit-stop**、**绝不让人在想做动作时等待**、动画忌匀速直线（Sakurai「on Creating Games」频道，按主题重构见 en.senkohome.com） | 顿帧已收口（§2.9）；「想做时别丢按键」由输入缓冲直接回应；缓动已在 §2.8 全面使用 |

来源：`egmatic.com/blog/how-to-make-your-game-feel-good`（2026-07，game feel 手法表与三层模型综述）、`en.senkohome.com/sakurai-game-dev-specification/`（Sakurai 频道 18 期按主题重构；原频道 youtube.com/@sora_sakurai_en）、`gamedesignskills.com/game-design/game-feel/`（Swink 三层模型的通识转述）

## 5 参考来源（本次实际可访问）

**许可证**：`creativecommons.org/publicdomain/zero/1.0/legalcode.{en,txt}`、`creativecommons.org/licenses/by/4.0/legalcode.txt`、`creativecommons.org/licenses/by-sa/4.0/legalcode.txt`、`opensource.org/license/ofl-1-1`、`openfontlicense.org/ofl-faq/`、`spdx.dev/learn/handling-license-info/`、`reuse.software/spec-3.3/`、`wiki.creativecommons.org/wiki/Recommended_practices_for_attribution`
**素材站**：`kenney.nl/support`、`kenney.nl/assets/category:Audio`、`opengameart.org/content/faq`、`freesound.org/help/faq/`、`game-icons.net/about.html`、`polyhaven.com/license`、`ambientcg.com/license`、`godotshaders.com/faq/`、`incompetech.com/music/royalty-free/faq.html`、`freepd.com`、`lucide.dev/license`、`github.com/feathericons/feather`、`github.com/google/material-design-icons`、`github.com/chr15m/jsfxr`
**玩法与无障碍**：`hardcoregaming101.net/battle-garegga`、`.../dodonpachi`、`.../touhou`、`gdcvault.com/play/1023470`、`hardingfpa.com`、`gameaccessibilityguidelines.com/{basic,intermediate,advanced}`、`learn.microsoft.com/en-us/gaming/accessibility/guidelines` 与 `.../xbox-accessibility-guidelines/{103,108,110,116,117,118}`、`accessible.games/accessible-player-experiences/`、`egmatic.com/blog/how-to-make-your-game-feel-good`、`en.senkohome.com/sakurai-game-dev-specification/`、`youtube.com/@sora_sakurai_en`、`gamedesignskills.com/game-design/game-feel/`
**测试与引擎**：`arxiv.org/abs/2202.12777`、`.../2208.07811`、`.../2107.12061`、`developer.valvesoftware.com/wiki/Soak_testing`、`docs.godotengine.org/en/stable/tutorials/{editor/command_line_tutorial,performance/*,rendering/renderers,physics/interpolation/index,scripting/c_sharp/*}`
**智能体指令与规则治理**：`code.claude.com/docs/en/{memory,best-practices,skills}`、`cursor.com/docs/context/rules`、`docs.cline.bot/features/cline-rules`、`code.visualstudio.com/docs/copilot/customization/custom-instructions`、`docs.github.com/en/copilot/how-tos/configure-custom-instructions/add-repository-instructions`、`agents.md`、`github.com/openai/codex`（`codex-rs/core/gpt_5_2_prompt.md`）、`github.com/anthropics/skills`（skill-creator）、`anthropic.com/engineering/effective-context-engineering-for-ai-agents`、`humanlayer.dev/blog/writing-a-good-claude-md`、`trychroma.com/research/context-rot`、`arxiv.org/abs/2507.11538`（IFScale）、`.../2602.11988`（ETH Zurich 上下文文件实测，预印本）、`.../2307.03172`（lost-in-the-middle）、`.../2404.02060`、`.../2608.11095`（指令生命周期实测，预印本）、`martinfowler.com/bliki/ArchitectureDecisionRecord.html`、`martinfowler.com/articles/continuousIntegration.html`、`kubernetes.io/docs/reference/using-api/deprecation-policy/`、`peps.python.org/pep-0387/`、`openjdk.org/jeps/182`、`bazel.build/{release/backward-compatibility,reference/be/common-definitions}`、`eslint.org/docs/latest/use/rule-deprecation`、`docs.gitlab.com/ee/development/documentation/{workflow,styleguide}.html`、`abseil.io/resources/swe-book/html/{ch08,ch10,ch11,ch20,ch23}.html`、`arxiv.org/abs/1810.05286`（Meta 预测式选测）、`learn.microsoft.com/en-us/azure/devops/pipelines/{test/test-impact-analysis,test/flaky-test-management}`

**未能核验（需人工核验，不作任何条款推断）**：Pixabay（403）、itch.io（域名不可达）、Wikimedia Commons（域名不可达）、Shadertoy（403）、Sonniss GDC 音效包（403）、Musopen（403）、sfxr 原版 drpetter.se（403）、Font Awesome Free（需 JS）、`fonts.google.com`（超时；但仓库内已含该字体 OFL 全文与 NOTICE，可视为等价证据）。
