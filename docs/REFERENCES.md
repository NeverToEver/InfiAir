# 公开素材、许可证与行业实践参考

> 路径：`docs/REFERENCES.md`（找它 / 改它的场合见 `AGENTS.md` §1 路由表）。
> **非纪律存档**：记录联网调研取到的**事实与出处**，供决策引用；不是口径单源——设计口径见 `DESIGN_BASELINE.md`，流程与门禁见 `AGENTS.md`，方向与开放项见 `ROADMAP.md`。
> 引号内文字为对官方页面的**逐字摘录**（链接见 §5）；未能核验的来源一律标注「需人工核验」，不做条款推测。只保留**仍被引用**的结论——过时条目随对应设计 / 决策一并删除（原文见 git 历史）。

## 1 素材来源与许可证事实

口径：「再分发」指随游戏二进制 / 发布包分发。**能零摩擦使用的只有三类**——CC0 1.0 / Unlicense（无署名义务、可商用、可入包、无传染）、OFL 1.1（字体，可按条件嵌入与随包分发）、MIT / ISC / Apache-2.0（图标与代码类，需随副本保留许可与版权声明）。

| 来源 | 许可 | 署名 | 再分发进包 | 传染 | 核验 |
| --- | --- | --- | --- | --- | --- |
| Kenney.nl · Poly Haven · ambientCG | CC0（站点 license 页明示，ambientCG 明示可入游戏） | 不需要 | 允许 | 无 | 官方页已核验 |
| OpenGameArt | 逐条目（CC0 / CC-BY 3.0·4.0 / CC-BY-SA 3.0·4.0 / GPL 2·3 / OGA-BY） | 视条目 | 视条目 | 视条目 | 条目页与 FAQ 已核验 |
| Freesound | 逐条（CC0 / CC-BY 4.0 / CC-BY-NC 4.0；旧库含已退休的 Sampling+ 1.0） | CC0 不需、CC-BY 必需 | 允许（受许可约束） | BY 无、NC 非商用 | 官方 FAQ 已核验 |
| Game-icons.net | CC BY 3.0 | 必需（逐图标作者不同） | 允许 | 无 | about 页已核验 |
| Google Fonts / Noto 系 | SIL OFL 1.1 | 无「署名」义务，但须附版权声明与许可全文 | 允许（含随软件售卖） | 仅覆盖字体衍生物 | OFL 全文与 FAQ 已核验 |
| Godot Shaders | 逐条（CC0 / MIT / GPL v3；**图片与视频不在其中**） | MIT 需保留声明 | 允许（GPL 除外） | GPL v3 有 | FAQ 已核验 |
| Lucide / Feather / Material Symbols | ISC / MIT / Apache-2.0 | 需保留声明 | 允许 | 无 | 仓库许可文件已核验 |
| incompetech（Kevin MacLeod） | CC BY 4.0（另售付费授权） | 必需（站点指定格式，且要求「放在玩家看得见的地方」） | 允许 | 无 | FAQ 已核验 |
| Pixabay / itch.io / Wikimedia Commons / Shadertoy / Sonniss / Musopen / sfxr 原版 | — | — | — | — | **需人工核验**（403 或域名不可达） |
| FreePD.com | 站点已于 2025 年永久关闭 | — | — | — | 公告已核验（**公有领域源也会消失**的现成反例） |

关键条款要点（原文逐字见 §5）：

- **CC0 1.0 §4**：不豁免商标与专利，且「makes no representations or warranties of any kind」——挑选时按「能不能想象它出现在商业产品的商店页上」做二次筛（可识别真人、商标、知名 IP 元素不进）。
- **CC BY 4.0 §3(a)(1)**：共享时须保留作者、版权声明、许可声明、免责声明与作品链接，并**标明是否修改**；§2(a)(6) 明示不构成原作者背书。
- **CC BY-SA 4.0 §3(b)**：改编物必须继续以 BY-SA 分发 → 与「MIT 仓库 + 闭源商业发行」**硬冲突**（OpenGameArt 上大量高质量素材属此类，挑选时逐条看 `License(s):` 字段）。
- **OFL 1.1**：可随任意软件捆绑、再分发与售卖，条件是每份副本含版权声明与许可全文（可作独立文本文件）；字体自身及衍生物须继续以 OFL 分发，但**用字体排出来的作品不受传染**；不得单独售卖字体。**OFL-FAQ 1.10** 虽允许字体嵌进可执行体时省独立许可文本，但明确「strongly recommend against」——本仓库把 OFL 全文平铺进发布包，属更保守形态。

## 2 与本项目纪律的冲突分析

1. **「素材一律程序化生成」+ `NOTICE` 的单源声明**：把下载素材放进 `assets/` 会让 `NOTICE` 的「唯一第三方素材是字体」立刻失真。
2. **素材来源没有自动判据**：「产物来源登记表」判定随 2026-09-18 门禁裁剪退役——新增、失效、未被重跑刷新三种形态只能靠人工核对，「`regenerate_all.sh` 重跑后 `git diff` 为空」的纪律照旧。
3. **玩家可见文案单源**：任何需要署名的许可（CC-BY 类）都要求在玩家可见处署名，而玩家可见文本只能来自 `data/translations.csv`（中英双语）；可通过关于页满足，代价是新文案键。
4. **传染性许可（CC-BY-SA / GPL）无解**：与发布形态直接冲突，不采纳。
5. **发布包合规面**：`release.sh` 会平铺 `LICENSE` / `NOTICE` / `assets/fonts/NotoSansSC-OFL.txt` 并逐件断言；引入任何新第三方素材必须同步扩这三件清单。
6. **合规惯例**：行业通行做法是「人读的 `NOTICE` + 每种许可的全文文件 + 逐条条目（组件 / 版本 / 来源 URL / 版权行 / 许可标识 / 是否修改）」，本仓库现有结构已是该形态；素材类二进制可用 REUSE 规范的 `.license` 伴随文件挂许可（PNG/WAV 无法写注释头）。CC0 虽不要求署名，工程上仍建议留「来源 URL + 抓取日期」（自证合规 + 源站可能消失）。

## 3 可采纳 / 不可采纳

**可采纳（按性价比；均为提议，未排期）**

| 候选 | 用途 | 落地要做什么 | 代价 |
| --- | --- | --- | --- |
| 新增 OFL 字体（Google Fonts / Noto 系） | 标题字、数字字 | 扩 `NOTICE` + `release.sh` 三件套 | 制度成本近零；体积与渲染一致性需过目 |
| ambientCG / Poly Haven（CC0） | `docs/` 配图与美术参考（不进 `res://`） | 注明来源与抓取日期 | 与 2D 程序化美术基本无关 |
| Kenney（全站 CC0，含 Audio 分类） | 音效 / 音乐候选 | 若入包：扩 `NOTICE`、扩 `release.sh` 清单、改「素材一律程序化生成」口径 | 破政策；判据要重写 |
| Godot Shaders 的 CC0 / MIT 条目 | 着色器参考 | 逐条核验页尾许可、避开 GPL v3 | 本仓库着色器已是 MIT 自有资产，收益有限 |
| jsfxr（移植仓库含 UNLICENSE） | 只作合成算法参考，代码重写为纯 Python | 只读参考、不复制代码 | 收益是思路不是素材 |

**不可采纳**：CC-BY-SA 3.0/4.0（含 OpenGameArt 的 LPC 系）、GPL 2/3 素材与着色器、Freesound 的 CC-BY-NC 与 Sampling+ 1.0、许可未核验的 Shadertoy / Pixabay / Sonniss / itch.io 未标注包、Wikimedia Commons（CC-BY-SA 占比高且叠肖像 / 商标 / 全景自由风险）、任何带第三方权利的 CC0/PD 内容。
**「用 CC0 音效替换程序化合成」的时间账**（本机实测）：`generate_audio.py` 全量合成约 145 秒，其中三首 BGM 占 112 秒——**换掉 BGM 才能省时间，换音效几乎不省。**

## 4 行业实践对照（只列结论与出处；URL 见 §5）

### 4.1 弹幕射击 / 街机

| 实践 | 本项目现状 |
| --- | --- |
| 判定点远小于机体且可见 | **已做到**：判定盒 2.8px、擦弹环内层豁免、判定点有光点与光圈 |
| 命中反馈：顿帧 2–10 帧 + trauma 震动模型（GDC，Eiserloh） | **已做到**：四档顿帧 0.03/0.07/0.11/0.16s、trauma 模型（`core/GameFeel/`） |
| 弹幕不得仅靠色相区分（XAG 103） | **基本做到**：高对比档默认开、给敌弹亮色轮廓；形状编码的另一半属未采纳提议 |
| 难度曲线：单调爬升 + 软上限 + 显式地板 / 硬顶 | **已做到**（曲线形状可单测） |
| 擦弹与连击构成风险回报闭环 | **已做到**：擦弹吃难度乘区与连击加权，单弹只计一次 |
| Boss 转阶段清弹 + 有限无敌 + 超时逃跑阀门 | **已做到** |
| 弹数预算（怒首领蜂约 245 发同屏为参照） | **已做到**：敌弹硬上限 500（约参照的 1.6–2.0 倍），到顶按模拟时间节流告警 |
| 双向 rank（死亡降难度，Battle Garegga 式） | **不建议**：与「必死曲线 + 压力无界」冲突；现有受击喘息已是收窄版 |

### 4.2 无障碍（GAG Basic/Intermediate/Advanced、XAG、APX）

**已做到**：音量三分路、减少闪光、屏幕震动 / 命中顿帧 / 动效强度（0% 即关）、高对比弹体（默认开）、可改键与灵敏度 / 死区、辅助瞄准三档、帧率与垂直同步、练习模式、难度可局中修改、可跳过过场、检查点存档。
**未覆盖（均为提议，未排期）**：长按通道的「切换」替代（GAG Intermediate Motor；本作三条蓄力通道是纯长按）、文本对比度选项（准星自定义已给自由 RGBA，属部分覆盖）、文字界面背后的动效开关（XAG 117，它明确「活跃玩法期间的背景移动不在约束内」）、过场可暂停（XAG 108）、游戏内无障碍特性说明（GAG Basic General）。
**闪烁的量化判据（XAG 118 / WCAG 2.3.1）**：亮度变化 ≥10% 记一次「闪」，**超过约 3 次/秒**、或占屏面积 ≥20%、或低强度长时间持续即判失败；红色闪（`R/(R+G+B) ≥ 0.8`）阈值更低。本项目已落成判据：全屏尺度脉冲单源在 core `FlashBudget`（单测断言频率 < 3Hz 且减闪下振幅为零），一次性闪光走 `AllowsOneShot`；面积与时长两条不在判据面内（本作全屏闪烁均为低占空比、无红色闪烁）。
**已裁定不做、不要再当缺失提**：游戏速度可调、屏幕朗读与字幕、局内计分显示、在线排行榜、移动端与触屏、随存随取。

### 4.3 自动化测试与确定性

| 实践 | 本项目现状 |
| --- | --- |
| 固定步长 + 判定只看模拟状态（Godot `--fixed-fps`） | **已做到**：五条确定性硬规则写在 `AGENTS.md` §4 |
| 「输入 + 种子 = 可复现的一局」 | **一半**：表现层随机已确定化，**玩法层随机仍走 `GD.Rand*` 默认序列**（暴击 / 标记 / 刷怪 / Boss 走位 / 迷雾）→ 开放项（登记在 `ROADMAP` 债务区） |
| bot / soak 的判据要简单、能失败、指向具体链路（业界对自动化代理持怀疑，关键在判据而非拟人性） | **策略已变**：长局自动游玩与 soak 随 2026-09-18 门禁裁剪退役；回归面 = core 单测 + 单趟开机冒烟 + 按需手跑 |
| soak 暴露泄漏与性能衰减（Valve 开发者 wiki） | **部分**：对象池化全面，但退出期资源统计走白名单（泄漏类不判）；长时稳定性无自动判据 |

### 4.4 Godot 4 / GL Compatibility

- **已做到**：帧内零 `GetNode` / 零 LINQ / 零容器分配（脚本扫描全库）、热路径配置在 load 期缓存、`FrameCache` 帧级共享缓存、按需重绘纪律、`Callable` / 信号只在构建期绑定。
- **已核验的文档订正**：官方渲染器对照表把 **Glow/SSAO 在 Compatibility 标为支持**——不支持的是 2D/3D HDR 渲染（辉光拿不到 HDR 阈值语义）、compute shaders、`CompositorEffects`、particle trails、MSAA 2D、debanding、decals、DOF、SDFGI、volumetric fog、SSR。本作手写全屏后处理的真实理由是「引擎给不了辉光＋调色＋晕影＋颗粒＋动态分级这一整套组合」。
- **启动期着色器预热**：Compatibility 下 ubershader 与预热机制不适用（只适用 Forward+/Mobile），官方建议在加载期把材质 / 着色器 / 粒子显示一帧；本作当前处理只是钳制推进步长（保状态机不连跳），不是消除卡顿。
- **不建议采纳**：子弹改 `MultiMesh`（官方定位是成千上万实例，本作上限 500 且要保留 `Area2D` 碰撞与对象池语义）、server API 化（同因）、物理 tick 提到 120Hz（会改「帧数＝模拟时长」口径）、第三方遥测 / 崩溃上报 SDK（违反运行时零第三方依赖）。

### 4.5 智能体指令文件与工程规则的治理

背景：`AGENTS.md` 每轮注入上下文，而实测「五周精简 6 次仍回涨」——专项调研「指令膨胀」与「规则只增不减」的业内做法。

| 实践 | 结论与本仓对策 |
| --- | --- |
| **体量上限是官方口径**：Claude Code「每个 CLAUDE.md 目标 <200 行」；Cursor「规则保持在 500 行以下」；Copilot 云端代理提示「不超过 2 页」；`agents.md` 规范本身不给建议 | 纪律文件按字符设上限（中文字符数与 token 直接对应）；正文只留口径与指针，判据复述移出 |
| **「150–200 条指令」是社区分析不是厂商口径**：HumanLayer 实测（前沿模型约能一致遵循 150–200 条）；IFScale 基准显示偏置峰值落在 150–200 条、300 条以上坍缩 | 按字符而非条数设预算（中文条数难机器统计） |
| **指令越多，整体遵循率均匀下降**：Anthropic 的 context rot / 注意力预算（n 个 token 产生 n² 对注意力关系）；Chroma 用 18 个模型实测「输入越长性能越不可靠」；lost-in-the-middle | 「精简 → 回涨 → 再精简」的真实代价是多出的是负担而非知识 |
| **反证据**：ETH Zurich 预印本实测「提供上下文文件总体上不提升任务成功率，且平均增加 20% 以上推理成本」；**只有「指定非标准做法 / 工具」的内容有效** | 正文筛选标准＝「要么是非显然的约定、要么是判定指针」 |
| **渐进披露是主流答案，但本仓不可用**：Claude Code 子目录 CLAUDE.md、Skills 三层、Cursor 四类规则、Cline 路径条件规则 | **已判定不做**：harness 只加载一份工作区 `AGENTS.md`（自 cwd 向上遇到的第一个即停），子目录文件不被载入、还会**遮蔽**根文件；故关键规则留在根文件，细节走指针 |
| **能让机器判的别写进提示词**：Claude Code（指令是指引性的、hooks 是确定性的）、Cursor（风格指南 → linter）、VS Code、HumanLayer（「不要派 LLM 干 linter 的活」） | 本仓纪律绝大多数由 `check_*.sh` 机器判定，提示词只写口径与指针 |
| **膨胀是默认状态、删除有成本**：1867 个仓库 / 247694 条指令生命周期的实测——代理提示词终身 +226%，每次提交净增 4.9 条，**指令越老越难删**；给指令写「为什么」可消除 99.3% 的多余指令 | 对策：体量上限 + **新增门禁必须写明退役条件**（写下去时就把「什么情况下能删」定好） |
| **规则退役是制度化动作**：Kubernetes 弃用政策、Python PEP 387、JDK JEP 182、Bazel `--incompatible_*`、ESLint（只有被替代或存在等价规则时才允许移除） | 采纳最小版：门禁准入写退役条件；不做「无替代不删」 |
| **文档生命周期**：Google 给文档挂 freshness date 并主张「无用就移除或显式标注过期」；GitLab 明文 **「Delete instead of hiding documentation」**；ADR 的 accepted 条目不改写、只被新条目取代 | 本仓纪律（活跃文档只写现状 + 开放项、修完直接删条目、反悔留痕不改旧条目）同族；2026-09-19 起进一步删掉「只读存档」形态（逐字依据见 §4.13） |

### 4.6 准星自定义选项与分享码

| 实践 | 本项目采纳 |
| --- | --- |
| **Valorant 的选项面**：颜色（预设 + 自定义 RGB）+ 内 / 外四线（显隐 / 长度 / 粗细 / 间距 / 不透明度）+ 中心点；职业玩家几乎只调内线 | 核心参数面：size/thickness/gap/alpha/中心点；「外线随射击误差移动」不采纳（本作无后坐力弹散） |
| **CS2 的选项面**多出：风格档、T 形、描边、整体 alpha、负间距 | 采纳 T 形、描边、alpha、gap 全值；「动态扩散」不采纳 |
| **形状语汇**：主流 FPS 是四线十字 + 中心点；本作原生语汇是四角 bracket（军事 FUI 族） | 四档形状 bracket（默认）/ cross / circle / dot；bracket/cross 支持旋转档（45° 即 X 形） |
| **分享码**：CS2 `CSGO-xxxxx-…` ＝ base64 编码全部设置，设置页内粘贴互导 | 同构交互：`INF1-` 前缀 + Crockford Base32 载荷 + CRC-8 + 版本字节（规格见 `DESIGN_BASELINE` §1.5） |
| **多档案**：CS2 / Valorant 均为单套配置 + 分享码备份；「多档案并存」见于社区生成器 | 本地档案簿（种子 4 预设 + 玩家自建），active 索引切换；名字不入码 |

### 4.7 战斗动效升级调研

背景：动效全面升级前的联网复核，**三条硬约束落地，其中一条推翻首版方案**。

| 事实 | 逐字要点 | 对方案的影响 |
| --- | --- | --- |
| **Godot 渲染器对照表**（官方） | 「Glow ✔️ Supported」「Custom post-processing with fullscreen quad ✔️ Supported」；**❌ Not supported**：`CompositorEffects`、`MSAA 2D`、`Compute shaders`、`Debanding`、**`Particle trails`**、`2D HDR Viewport`；Color precision ＝「RGBA8. Low dynamic range」 | 手写全屏后处理是唯一路径；**粒子拖尾在该后端不存在**（敌弹拖尾不能走 `GPUParticles2D`）；无 HDR 语义，亮点在 1.0 截断 |
| **节拍同步**（官方） | 精确播放位置＝`get_playback_position() + AudioServer.get_time_since_last_mix() - AudioServer.get_output_latency()`；「The result may be a bit jittery… **discard it if so**」；长播会与系统时钟漂移，须走声卡时钟 | 呼吸 / 节拍层采到相位后**必须做回退值丢弃**，否则全屏节奏会偶发倒跳 |
| **XAG 118 光敏性**（微软官方） | 「A flash is defined as a **10% change in luminance**」；失败判据三条并列：频率「more than **three per second**」／面积「approximately **20 percent or more**」／「Lower intensity flashing can also cause a failure if it's continued for an extended period」 | **首版方案被推翻**：全屏「每拍亮一下」即便频率合规仍踩**面积**判据（全屏＝100% ≫ 20%）。故全屏层只做**低于 10% 亮度变化**的慢呼吸，拍点一律下放到局部元素 |
| **战斗可读性**（Art Director 视角） | 暗背景上特效核心「never exceeds **80% white**」；亮度应「peaks sharply and decays rapidly」；「the brain processes **color before shape**」——UI 与危险物不得共用色相；**拖尾最致命**：「If a trail lingers for 100ms longer than the active hitbox … the player will roll dodge to avoid what looks like the blade, only to get hit」 | 敌弹只动画**自身四边形内的烘焙尾部**：不新增滞后几何、不新增 draw call，弹头与判定逐位不变 |
| **敌人可读性**（The Level Design Book） | 需要「Unique silhouette so players can discern enemy type at mid-range」与「Design details and animations that **telegraph enemy state, intent, and strengths/weaknesses**」 | 支持「损伤状态分级」而非堆装饰 |
| **弹幕美学**（Danmaku 设计指南） | 有方向弹体天然传达运动方向，圆形无方向弹「do not telegraph their movement direction at all」 | 弹体语言优先于装饰 |

**未能核验**：`shmups.wiki`（弹幕设计社区权威文档的载体）本轮不可达，镜像站返回 Cloudflare 520、`web.archive.org` 亦不可达——弹幕可读性一节采用上述可访问来源，shmup 社区口径待补。

### 4.8 输入回应与交互性调研

| 实践 | 本项目现状 |
| --- | --- |
| **game feel 三层模型**（Swink《Game Feel》：实时控制 → 可预测空间 → polish；顺序不可倒） | 输入路径已无迟滞；缺口在「请求被拒时的回应」 |
| **输入宽容**：input buffering + coyote time，窗口**远小于 150ms** | **已落地**：弹反 / 冲刺输入缓冲 0.1s（`player.input_buffer_window`） |
| **每个动作都要回应**；「silent actions」与「shake/flash on everything」并列手感杀手（Vlambeer 手法表：muzzle flash + hit sound 让每次射击「violent and immediate」） | **已落地**：命中 tick 音 + 弹反专属确认音 + 冷却 / 燃料拒绝回应 |
| **声音承担一半权重**，是低估的通道；高频战斗音效须在声源限频防连发噪声 | 命中音走 `SfxPlayer` 目录表（45ms 最小间隔 + 复音 2 + 音高抖动防梳状滤波） |
| **关键帧 hit-stop**、**绝不让人在想做动作时等待**、动画忌匀速直线（Sakurai「on Creating Games」） | 顿帧见 §2.9；「想做时别丢按键」由输入缓冲直接回应；缓动已在 §2.8 全面使用 |

### 4.9 全息 FUI 面板惯例

| 惯例 | 本项目现状 |
| --- | --- |
| **全息读感三要素：半透明体积 + 像素化 / 扫描质感 + 自发光**（对《禁忌星球》《Strange》《31 区》全息投影的分析：「translucent, volumetric display」「highly pixelated translucent appearance」——是光，不是带金属拉丝的实体面板） | **已落地**：虚影面板关拉丝钢、加发光边缘与扫描线（`DESIGN_BASELINE` §2.15） |
| **发光必须服务信息传达**，不承载信息的发光线条被该站归为「fuigetry」（好看但牺牲可用性 ＝ bad design） | 发光强度克制（外晕 14%/30%、常态扫掠峰值 10%）；周期扫掠 / 显影类动效尊重减少闪光设置，静态扫描线保留 |
| **boot-up 扫掠**是 FUI 面板出场通用语汇；**环境扫描带**表达「实时投影 / 监控屏」 | `HoloBoot` 一次性显影带（0.32s）；常态 6s 周期柔边亮带 + 屏幕级 8s 慢扫描带，均为纯装饰层 |

### 4.10 机体程序化动画调研

| 惯例 | 本项目现状 |
| --- | --- |
| **动画十二原则对程序化运动同样适用**：squash & stretch（「a ball that squashes on impact and stretches as it moves feels physical and alive, while a rigid one feels dead」）、follow-through / overlapping action、timing | **已落地**（§2.16）：冲刺缩放过冲、运动滞后漂移、转向跟随角惯性——全部程序化、只写贴图变换 |
| **Sakurai Motion 系列**：「Overdoing it is just right」（小屏幕上「不足」是常态失败）；「The follow-through decides the impression」；「Idle is the origin of all. Don't waver it」；**响应优先于粘滞**（「carefully interpolating from idle increases a sticky feel」） | 姿态 / 活性只作用于贴图层（判定 / 操控 / 弹道零滞后）；浮动压到 1.6px；过冲只做一次性事件（冲刺起手）不做持续粘滞 |
| **Swink 三层模型**（§4.8 已引）：实时控制是 polish 的前提 | 机体根节点与碰撞体不动是这一批的硬约束 |

### 4.11 机身反馈与损伤状态调研

| 惯例 | 本项目现状 |
| --- | --- |
| **RCS / 微调推力器按所需方向点火**（真实航天器惯例）：「Reaction control systems are capable of providing small amounts of thrust in **any desired direction** or combination of directions」；常用大小推力器组合（vernier）分级响应 | **已落地**（§2.17）：左右 / 机首三枚小喷口按机体本地系加速度**反向**点亮，余量随该轴加速度占比 |
| **状态可读性**（§4.7 已引）与「最亮＝最危险」（§2.12 判据 3） | 损伤状态（重伤档）改为**可见的生命读数**：损伤烟 + 引擎喘振；开火机身光暖向提亮且约 0.2s 衰减完 |
| **muzzle flash 让每次射击「violent and immediate」**（§4.8 已引） | 枪口辉光 + 机身火光：光照落在机体自身，射击的重量不止在枪口 |

### 4.12 难度 / 奖励 / 打击感惯例（平衡审查存证）

| 维度 | 行业参照 | 本项目取值与理由 |
| --- | --- | --- |
| 无尽缩放形状 | RoR2 线性时间项 + 每关 ×1.15；Brotato 超线性三角数（wave40 = 6.30）；VS 逐分钟线性 `hpPerMinute 0.05` | 线性 + 软上限（6.0 后 ×0.5）：形状可接受，缺的是减速带 |
| HP 与伤害缩放比 | RoR2 每级 **+30% HP / +20% dmg**；Brotato `HP ×(1+2.25·EF)` vs `dmg ×(1+EF)`——行业刻意让 HP 缩放缓于伤害、两者都低于玩家成长 | 杂兵 HP 0.40 / 伤害 0.20（2:1）；**Boss 斜率 0.55 独立于杂兵**——原「Boss ＝ 杂兵 4 倍」造成「杂兵无压力、Boss 成墙」的两极 |
| 速度缩放 | Brotato 硬顶 ×2.75；20MTD 精英 +25%。速度是唯一直接破坏可反应性的量 | 硬顶 **×1.8**（取参照下端） |
| 后期压力来源 | RoR2 Director 预算随系数涨；Brotato 每 10 波 +1 精英；20MTD 并发上限不变、靠频率 | 精英数量随 D（每 2.0 D +1、上限 3）+ 开火间隔地板 1.2s——压力应在密度与模式 |
| 难度可读性 | Battle Garegga 内置实时 rank gadget；RoR1 用 10 个命名档位 | 6 档命名难度 + 常驻目标进度 |
| 必死曲线的收束 | Brotato「打过 wave 20 后死亡仍算胜利」 | 「Boss 击杀 10 或存活 20 分钟」达成判定，**达成不终止本局** |
| 降档 / 公平阀门 | Hades God Mode（20% 减伤、每死 +2%、上限 80%）；XAG 108 要求 **≥4 档 + 任意时刻可改且不丢进度** | 受击喘息（开火间隔 ×1.3 / 5s）+ 局中可改难度档；**档位只有 3 档，低于 XAG 建议的 4 档**（已知偏差） |
| 奖励与难度的耦合 | RoR2 货币同步通胀（`baseCost × coeff^1.25`）；StS Ascension 反向 | 双轨补偿：击杀分 × `KillScoreFactor`（1+0.15(D−1)），事件奖励走同一因子 |
| 里程碑 / 点数节奏 | StS 稀有度用**正向保底**；「惩罚 + 隐蔽 + 不可逆」三重叠加对新手是纯负面 | 缓存超额衰减配**正向回补**（`recovery_step` 0.25）+ 安全阈值 30 + 常驻进度读数 |
| 擦弹 / 连锁计分 | DDP 蜜蜂阶梯 100→100,000；Ikaruga 连锁上限 25,600 | 擦弹 30 分且吃难度与连击加权；连击窗口 5.0s 对齐波间隔（4–7s） |
| 事件风险回报 | 经验锚点：**回报应约为惩罚的 3 倍**；VS Curse 自选风险换战利品 | 事件奖励 ≥ 同窗口刷怪收入（炮塔击毁计击杀分 150 + 全歼档奖励）；迷雾是纯负反馈 → 给按难度的存活补偿 |
| 打击感 | trauma 模型（Eiserloh GDC 2016）：`offset = maxOffset × trauma²`、每帧 `Clamp01(trauma + stress)`、线性衰减 1.0–2.0/s、Perlin **连续**噪声；hitstop 惯例 40–250ms（随伤害单调递增） | trauma 参考振幅 24、衰减 1.5/s；顿帧四档 0.03–0.16s，同帧多请求取较大者不叠加 |
| 判定点与弹量 | Touhou6 自机判定 2.5×2.5px、擦弹盒 4–32px；DDP 一屏最大约 245 发 | 判定 2.8px（正统档位，**不改**）；敌弹硬上限 500 |
| 死亡清屏与无敌 | Touhou6 死亡无敌 240 帧 / 清屏保护 90 帧；SDOJ Hyper 2.0s | 受击无敌 1.5s + 清 250px 弹 + 喘息 5s；**三重复合的取舍已收窄**（喘息只作用于敌火间隔） |

**说明**：本表是 2026-09 平衡审查的结论层（逐行取证、症结排序与当时建议的完整快照在 git：`git show e9a4141:docs/BALANCE_REVIEW.md`）。审查时列为「未确认」的项已全部按上表定稿，反转＝改 `data/balance.json` 对应键一处。

### 4.13 文档与决策记录的治理（2026-09-19 调研）

| 来源 | 逐字要点 | 对文档裁剪的影响 |
| --- | --- | --- |
| **Nygard《Documenting Architecture Decisions》**（2011，ADR 惯例起点） | 「Agile methods are not opposed to documentation, only to valueless documentation… **Large documents are never kept up to date. Small, modular documents have at least a chance at being updated.**」「Nobody ever reads large documents, either.」一条决策记录＝一个决策，「The whole document should be one or two pages long」；被推翻的决策保留但标 superseded | 决策条目压成一行制；批次汇报与验证流水不再进活跃文档；设计参考与决策记录分家 |
| **Diátaxis** | 参考（reference）＝「what a user needs in order to **apply** knowledge… while they are working」，解释（explanation）供 study 用；两者混写「This is bad for the reference, interrupted and obscured by digressions. **But it's bad for the explanation too**」 | `DESIGN_BASELINE` 收敛为纯参考（现行口径 + 取值 + 单源指针）；「为什么这么做 / 那批做了什么」移出 |
| **GitLab 文档工作流** | 「Hiding documentation is problematic… Hidden documentation becomes outdated… Dropped features leave orphaned content that clutters the repository. **If documentation needs to be removed… delete it.**… If it's needed in the future, you can use Git history… to recover the content and add it back.」 | 只读存档**删除**而非标注过期；`REFERENCES` 只保留仍被引用的结论；恢复路径写进决策条目的反转链 |
| **Google 文档最佳实践**（§4.5 已引）与 **ADR 惯例**（MADR / adr.github.io：一条记录一个决策、status 只标 superseded） | 文档挂 freshness date、无用就移除或显式标注过期；决策记录不改写、只被新条目取代 | 活跃文档只写现状 + 开放项；反悔留痕＝加新条目而非改旧条目；体积约束由「一行制 + 历史在 git」维持 |

**落地判定**：本仓「六份活跃文档」结构保持不变；被裁掉的是**叙事层与只读存档**（形式），不是规则与取值（内容）——规则仍在 `AGENTS.md`、取值仍在 `DESIGN_BASELINE`、依据仍在 `REFERENCES.md`、历史在 git。

### 4.14 机体形态层调研（2026-09-19）

主题：**让机体的「轮廓」而不只是光色随状态改变**（`DESIGN_BASELINE` §2.18）。诊断是前几批表现全在同一副机形上加光加色，玩家读不出状态；而这层要动的正是外形。

| 来源 | 逐字要点 | 落地 |
| --- | --- | --- |
| **Valve《Illustrative Rendering in Team Fortress 2》**（NPAR 2007）与 **Riot《Clarity in League》** | Valve：九个兵种「即使只看纯剪影、无任何内部明暗也能区分」，故刻意用边缘光而非黑描边强调剪影；Riot：**剪影是英雄辨识最重要的东西**，排在技能与配色之前 | 形态层的第一判据是**轮廓**：机炮伸缩与翼尖拖尾都改变外形，而非只加亮度（亮度留给已交付的 §2.13/§2.17 层） |
| **The Level Design Book「Enemy design」** | 「独特剪影让玩家在**中距离**（约 10 米外）就辨出敌人类型」+「用细节与动画**预告状态与意图**」 | 形态层要在整机只有一个字符高度的尺度下成立：故取「炮管伸出 20 贴图像素（≈5 世界像素）」这类**远超噪声线**的形变量，并在窗口化截图上逐相位核过 |
| **可变后掠翼**（Variable-sweep wing / F-14 条目）与 **B-36 可收放炮塔**（真实工程惯例） | 可变后掠翼：F-14 的后掠角由大气数据计算机按马赫数在 20°–68° 之间自动调度；B-36 装六座**可收放**遥控炮塔，不用时收进机体 | ① 机炮「平时收起、射击时展开」是真实工程先例，不是科幻装饰；② 冲刺时强制收拢＝「机体几何随速度状态改变」的现成语汇 |
| **翼尖涡流**（Wingtip vortices / NASA Glenn 科普页）与 **War Thunder 官方开发日志**（Sky Guardians / Firebirds） | 维基：「涡核水汽凝结**最常见于大迎角飞行**，例如战斗机做高 G 机动」；NASA：湿度足够时「能在翼尖看到细长的云线」；WT：「机翼涡流/翼尖流场分离表现为机动时机翼拖出的长涡索」「爬升与机动时…出现新的 LERX 蒸气效果」 | 翼尖涡流作为**机动强度**的读数：低于阈值不出现（直线巡航不出），强度同时给长度与亮度——「拉长」比「变亮」更像真蒸气 |
| **红热温色表**（Red heat 条目：黑红约 426℃ 起、经暗红/樱桃红/橙/黄，1315℃ 以上为白）与 **MechWarrior 热量可视化** | 受热发光的颜色-温度对应有物理依据；机甲游戏用热力表 + 红区分区表达热度 | 炮管着色走**暗钢→琥珀→白热**两段插值（`HullRig.HeatTintMix` 出两个混合因子）；**只做读数不做惩罚**——热量不进任何玩法结算 |

**边界说明**：War Thunder 两条属飞行模拟而非弹幕射击，引用的是「翼尖涡流/蒸气 = 机动读数」这一表达惯例本身；MechWarrior 的热量是**资源**（影响射速与停机），本作只借它的颜色表达，不引入过热惩罚（引入即改玩法数值，须先按 §2 定稿）。

### 4.15 初始机型（开局选机体）调研（2026-09-19）

主题：**开局前选一种机体、各给一项不同的加成**（`DESIGN_BASELINE` §1.17）。要证两件事——「选机体」是这一类型的通行做法，以及**单项加成该给多大**。第二件外部没有权威数字，故落点是：形状取行业惯例，**量级锚定本作自己的既有刻度**（增幅一级 / 难度档），不在网上捡数字。

| 来源 | 逐字要点 | 落地 |
| --- | --- | --- |
| **Wikipedia《DoDonPachi》**（Cave，1997 弹幕射击代表作） | 「There are three different ships to choose between, and each ship can be played in Laser or Shot mode.」三型射击形态各不相同：A 型窄流直射、B 型为直升机（侧炮随移动方向旋转）、C 型三向散射 | 开局选机体是弹幕/纵版射击的**基础惯例**，且差异落在**手感与攻击形态**上。本作取「机型 = 一项数值乘区 + 一副专有外观」，不新增武器形态（新增形态属玩法系统扩展，代价远大于本批） |
| 同上（火力 ↔ 机动的反向权衡，Cave 自己的实现） | 「If the fire button is held down, the floating guns combine in front of the ship to produce a vertical beam, **which provides more firepower than standard fire. This also makes the ship move more slowly.**」 | 该类型的正统做法是「火力买机动」——**取舍是设计出来的**。本批按人类要求只给加成不加惩罚，取舍只来自「六选一」的机会成本；故量级必须压在亚一层增幅之下，不能让机型替玩家把这道权衡做掉 |
| **SLYNYRD《Pixelblog-32 Shmup Design Part 2》**（弹幕游戏设计教程） | ① 「Speed … **as speedy movements can be a disadvantage when navigating through tight spaces.**」② 论属性取值的具体数字：「**The specific attributes and quantities are a personal choice.** As the developer, you are free to implement ideas in any manner you like.」 | ① 速度是**双刃剑**（快＝灵活但难走细缝）——支持「移速加成不是纯上位」，故它与其余四项并列成立；② 教程把数值定为开发者自主项——正是本仓库 `AGENTS.md` §2「玩家可感且无客观对错 → 给依据 + 实测定稿」的口径，故量级改锚本作内部刻度（下表） |

**量级锚定（本作自己的刻度，非外部数字）**：

| 锚 | 值 | 说明 |
| --- | --- | --- |
| 一层 `power_shot` 增幅 | ×1.25（+25% 伤害） | 机型加成必须**明显小于一层增幅**：增幅要花本局赚到的天赋点，机型是开局白送 |
| 一层 `rapid_fire` 增幅 | 间隔 ×0.75（+33% 射速） | 同上 |
| 一层 `armor` 增幅 | ×0.85（−15% 受伤） | 壁垒型取同档或以下，且不与护甲共用一条路径（护甲要加点，机型开局就带） |
| 难度档差距 | hard 相对 easy：敌 HP ×1.5 / 移速 ×1.2 / 刷怪间隔 ×0.8 | 机型加成远小于一档难度差——难度档仍是玩家能选的最大单一杠杆 |
| 感知下限 | 既有可读步进（如擦弹 +5 分/层、命中音 45ms 限频） | 加成要「一眼看得出、实测算得出」，不做读不出的装饰 |

**未能核验**：`shmups.wiki`（社区数字图书馆，含 Boghog《Bullet hell shmup 101》等机体/速度讨论）在本环境不可达，故不引用其内容；检索摘要方向（「fast, powerful ships with narrow shots, or slower weaker ships with wide shots」）与上表一致，但**未经页面核验，不作条款推断**。


## 5 参考来源（URL 存档）

**许可证**：`creativecommons.org/publicdomain/zero/1.0/legalcode.{en,txt}`、`creativecommons.org/licenses/by/4.0/legalcode.txt`、`creativecommons.org/licenses/by-sa/4.0/legalcode.txt`、`opensource.org/license/ofl-1-1`、`openfontlicense.org/ofl-faq/`、`spdx.dev/learn/handling-license-info/`、`reuse.software/spec-3.3/`
**素材站**：`kenney.nl/{support,assets/category:Audio}`、`opengameart.org/content/faq`、`freesound.org/help/faq/`、`game-icons.net/about.html`、`polyhaven.com/license`、`ambientcg.com/license`、`godotshaders.com/faq/`、`incompetech.com/music/royalty-free/faq.html`、`freepd.com`、`lucide.dev/license`、`github.com/{feathericons/feather,google/material-design-icons,chr15m/jsfxr}`
**玩法与无障碍**：`gameaccessibilityguidelines.com/{basic,intermediate,advanced}`、`learn.microsoft.com/en-us/gaming/accessibility/guidelines` 与 `.../xbox-accessibility-guidelines/{103,108,110,116,117,118}`、`riskofrain2.wiki.gg/wiki/Difficulty`、`brotato.wiki.spellsandguns.com/Endless_Mode`、`slaythespire.wiki.gg/wiki/Cards`、`en.wikipedia.org/wiki/{DoDonPachi,Battle_Garegga,Reaction_control_system}`、`github.com/GensokyoClub/th06`、`gdcvault.com/play/1023146`、`roystan.net/articles/camera-shake.html`、`hardcoregaming101.net/{battle-garegga,dodonpachi,touhou}`、`psychologyofgames.com/2017/12/using-psychology-to-design-leveling-systems/`、`tboi.com/devil-room`
**手感与动效**：`egmatic.com/blog/how-to-make-your-game-feel-good`、`en.senkohome.com/sakurai-game-dev-{specification,motion}/`、`youtube.com/@sora_sakurai_en`、`bugnet.io/blog/animation-principles-every-game-developer-should-know`、`gamedesignskills.com/game-design/game-feel/`、`book.leveldesignbook.com/process/combat/enemy`、`sparen.github.io/ph3tutorials/ddsga2.html`、`mystpixel.com/posts/5-visual-clutter-culprits-ruining-your-indie-game-s-combat-readability/`、`scifiinterfaces.com/tag/{hologram,glow}`
**机体形态与热读数（§4.14）**：`steamcdn-a.akamaihd.net/apps/valve/2007/NPAR07_IllustrativeRenderingInTeamFortress2.pdf`、`leagueoflegends.com/en-us/news/dev/clarity-in-league/`、`en.wikipedia.org/wiki/{Variable-sweep_wing,Grumman_F-14_Tomcat,Convair_B-36_Peacemaker,Wingtip_vortices,Red_heat,Thermal_radiation,Twelve_basic_principles_of_animation}`、`warthunder.com/en/news/{7705-development-f-14a-tomcat-into-the-danger-zone-en,8137-development-new-effects-for-aviation-in-the-sky-guardians-update-en,9195-development-firebirds-effects-improvements-to-aviation-en}`、`www1.grc.nasa.gov/beginners-guide-to-aeronautics/downwash-effects-on-lift/`、`skeletoncodemachine.com/p/mech-week-heat`、`wiki.mechlinglegends.net/index.php?title=Heat`
**初始机型与机体差异（§4.15）**：`en.wikipedia.org/wiki/DoDonPachi`、`slynyrd.com/blog/2021/2/15/pixelblog-32-shmup-design-part-2`
**准星与 FPS 惯例**：`ign.com/wikis/valorant/The_Best_Valorant_Crosshair_Guide`、`totalcsgo.com`、`dmarket.com`、`prosettings.net`
**测试与引擎**：`arxiv.org/abs/2202.12777`、`developer.valvesoftware.com/wiki/Soak_testing`、`docs.godotengine.org/en/stable/tutorials/{editor/command_line_tutorial,performance/*,rendering/renderers,audio/sync_with_audio,scripting/c_sharp/*}`
**规则 / 文档治理**：`code.claude.com/docs/en/{memory,best-practices,skills}`、`cursor.com/docs/context/rules`、`docs.cline.bot/features/cline-rules`、`code.visualstudio.com/docs/copilot/customization/custom-instructions`、`agents.md`、`anthropic.com/engineering/effective-context-engineering-for-ai-agents`、`humanlayer.dev/blog/writing-a-good-claude-md`、`trychroma.com/research/context-rot`、`arxiv.org/abs/{2507.11538,2602.11988,2307.03172,2608.11095}`、`diataxis.fr/`、`cognitect.com/blog/2011/11/15/documenting-architecture-decisions`、`adr.github.io/`、`martinfowler.com/bliki/ArchitectureDecisionRecord.html`、`docs.gitlab.com/development/documentation/{workflow,styleguide}.html`、`kubernetes.io/docs/reference/using-api/deprecation-policy/`、`peps.python.org/pep-0387/`、`bazel.build/release/backward-compatibility`

**未能核验（需人工核验，不作任何条款推断）**：Pixabay（403）、itch.io（域名不可达）、Wikimedia Commons（域名不可达）、Shadertoy（403）、Sonniss GDC 音效包（403）、Musopen（403）、sfxr 原版 drpetter.se（403）、Font Awesome Free（需 JS）、`fonts.google.com`（超时；仓库内已含该字体 OFL 全文与 NOTICE，可视为等价证据）、`shmups.wiki` 与 `web.archive.org`（不可达）。
