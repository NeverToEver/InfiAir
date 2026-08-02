# InfiAir 竞品差评调研与主动改进报告

> **日期**：2026-08-02
> **性质**：主动改进调研（非缺陷审计）——通过收集同类 2D 俯视空战/射击类游戏的差评，提取玩家普遍痛点，对照 InfiAir 现状识别改进机会。**本报告为调研产出，改进建议落地后按 `AGENTS.md` 文档同步要求登记，落地完成后全文归档至 `docs/archive/` 并在 `EXECUTION_LOG.md` 登记压缩条目。**
> **数据源**：Steam 商店公开评价 API（`store.steampowered.com/appreviews`），近期差评（`filter=recent`），多语言（含简体中文/英文/俄文等），共 **5 个同类游戏、44 条差评样本**。评价引述保留原文（原文为英文/俄文/西文的附中文释义）。
> **范围说明**：所选游戏为「2D 俯视空战/太空射击、得分制/构筑成长、波次或关卡推进」与 InfiAir 玩法相近者；纯 3D（如 Ace Combat）与纯街机弹幕移植（如 Cave 系）不作为主样本。

---

## 1. 竞品清单与差评样本

| 游戏 | Steam appid | 与 InfiAir 的相似点 | 差评样本 |
| --- | --- | --- | --- |
| **Nova Drift** | 858210 | 太空射击 + 局内构筑（Buff/武器 mod）+ 分数驱动无限模式 | 12 条（中/英/俄） |
| **Raptor: Call of The Shadows (2015)** | 336060 | 经典 2D 俯视空战、单机任务制 | 12 条（英/法/西/俄/捷） |
| **Sky Force Reloaded** | 667600 | 2D 俯视射击、战机成长升级、得分/收集制 | 8 条（中/英/西） |
| **Starward Rogue** | 410820 | 太空射击 + 弹幕 + roguelike 构筑 | 8 条（英/俄） |
| **Jamestown+** | 377950 | 经典垂直卷轴 shmup、多难度推进 | 8 条（英/瑞） |

评价抓取链接示例：`https://store.steampowered.com/appreviews/858210?json=1&language=all&review_type=negative&filter=recent`（[Nova Drift 商店页](https://store.steampowered.com/app/858210) / [Raptor 2015 商店页](https://store.steampowered.com/app/336060) / [Sky Force Reloaded 商店页](https://store.steampowered.com/app/667600) / [Starward Rogue 商店页](https://store.steampowered.com/app/410820) / [Jamestown+ 商店页](https://store.steampowered.com/app/377950)）

---

## 2. 差评主题聚类（跨游戏共性痛点）

### 2.1 弹幕/战场可读性与可视性（出现频率最高，4/5 游戏命中）

- **Sky Force Reloaded**："Bullet visibility is very bad occasionally; your plane can be hidden by foreground objects (clouds etc.)"；另一条："You completely lose track of your ship … you usually only find out where you are when you hear the explosion"（飞机被云层/前景遮挡、找不到自己）。
- **Starward Rogue**："very hard to tell apart your bullets and the enemy's bullets, even with fairly basic weapons … under the 100s of overlapping effects"（己方弹与敌弹难区分、特效堆叠淹没目标）；"why is seeing your hit dot a conditional thing?!"（受击判定点不可见）。
- **Nova Drift**："美术倒是挺好"（视觉好但可读性仍被控）；俄语差评："Попадаются тёмные комнаты, где вообще ничего толком не видать"（黑暗房间什么都看不见）。

> **共性结论**：射击类玩家的**第一反感点是"看不清"**——找不到自己、分不清敌我弹、看不见受击点。视觉华丽但可读性差 = 差评直接来源。

### 2.2 操控与输入方案（Nova Drift 重灾区，命中率极高）

- **Nova Drift**（4 条集中差评）："操作纯反人类"；"市面上那么多经典的操作方式不学不抄…感觉自己跟装了假肢一样"；"左摇杆控制移动和射击方向，左扳机移动，右扳机射击，右摇杆不用，有通用的最优解控制方案不用"；"鼠标左键移动右键瞄准射击，那要左手干嘛要键盘干嘛"。
- **Nova Drift**（UI/导航）："按 B 不能返回，要移动到返回菜单才行"。
- **Sky Force Reloaded**："I wish you could control the plane with the mouse as well"。
- **Starward Rogue**："Controls are pretty wonky"。

> **共性结论**：**非常规操控方案 + 无自定义 + 导航别扭**是劝退型差评。玩家期望：主流操作方案（键鼠/双摇杆）+ 完整改键 + 一致的后退导航。

### 2.3 难度曲线与"公平感"（跨游戏高频，4/5 命中）

- **Nova Drift**："You get randomly insta killed by some mob with insane modifiers"（随机被带变态词缀的怪秒杀）；"前期没经验…前几个小时给你送经验然后突然有一局变难"（难度突变）。
- **Sky Force Reloaded**："The last few stages are too hard … basically require you to memorize everything or you'll get caught by cheap hits"（后期需背板、廉价受击）。
- **Starward Rogue**："balance is non-existing"；"amount of bullet sponge enemies"（子弹海绵敌人）。
- **Jamestown+**："Having to beat stage 1-3 on Difficult setting to be able to progress was the nail in the coffin"（高难度门槛强制）。

> **共性结论**：难度高不是问题，**"无预警的突变、随机性致死、背板要求、子弹海绵"**才是差评点。玩家要的是"死亡可归因"（死得明白）。

### 2.4 重复感 / 刷度 / 内容多样性（4/5 命中）

- **Sky Force Reloaded**："Requires excessive grinding"；"Replaying the exact same stage over and over just to grind a little bit of money … exhausting"。
- **Jamestown+**："to progress you have to keep replaying the previous stages on the higher difficulties, which lengthens the experience far more than it should and makes the game boring"。
- **Nova Drift**："The run variety was the same 3 things everytime and I found the optimal way to play on like my second run"（第二局就找到最优解）。
- **Starward Rogue**："suffers from the same one-two issue combo as most roguelikes: balance is non-existing, yet there's not enough difference between different runs"。

> **共性结论**：重复不是原罪，**"无变化的重复 + 单一最优解"**才是。随机/构筑系统必须真正改变每一局体验。

### 2.5 成长/进度系统期待（2/5，shmup 玩家尤其在意）

- **Jamestown+**："There are no upgrades or power ups! … Was expecting to be able to upgrade shield, speed weapon etc but no, nothing like that"（无升级无强化 = 失望）。
- **Sky Force Reloaded**："the game just doesn't develop much … upgrades are just extras and don't really change the formula"（升级不改变玩法公式）。

> **共性结论**：纯弹幕硬核向可以无成长，但**面向休闲/中度玩家的俯视射击，成长系统是核心期待**——且升级必须"改变玩法"而非纯数值。

### 2.6 存档与进度保存（Raptor 2015 重灾区）

- **Raptor 2015**（5 条集中差评）："Every time you save your game, it makes a new save … if you save a few times … you get to a point where the game tells you that you must delete a save before you can save. Now all the progress you made is lost"；"deleting all saves means erasing your current game"；"you save your game and never know which version is the last one"。
- **Sky Force Reloaded**：换电脑转移存档后"gold and all settings statistics were completely wiped out"（进度部分丢失）。

> **共性结论**：**存档系统的混乱（多存档、上限、覆盖歧义、转移丢档）是差评灾难**。玩家要的是"无感自动保存、进度永不意外丢失"。

### 2.7 技术稳定性（2/5）

- **Raptor 2015**："Mouse input is just outright non-functional"；"unexpected crashes"；成就 bug（"Bugged all over the place"）。
- **Jamestown+**："game crashes on boot"（启动即崩溃）。

> **共性结论**：输入失效、启动崩溃、成就 bug 直接摧毁口碑——**基础稳定性是合格线而非加分项**。

### 2.8 音频与演出（3/5）

- **Raptor 2015**："atrocious musics (your ears WILL bleed)"；"pieces of the iconic midi music that just abruptly end and loop"（MIDI 截断循环）；"Terrible sound quality compared to original"。
- **Sky Force Reloaded**："the music … doesn't feel like it exists to really be exciting … It's the most background VGM music I think I've ever heard"（BGM 平淡无记忆点）。
- **Jamestown+**："sound design is quite weak"。

> **共性结论**：**程序化/廉价音频的辨识度不足**是常见差评点；音乐截断循环这类技术性问题更是直接扣分。

### 2.9 教程与上手（2/5）

- **Starward Rogue**："Such a lengthy and tedious tutorial that doesn't even teach you what you need to know"（冗长且教不到点）；"you can skip text just by walking fast and even miss parts of it"（跳过机制损坏教程）。
- **Nova Drift**：非常规操作却无解释（隐含）。

> **共性结论**：教程要么**短而教到点**，要么**可随时重看**；被跳过机制破坏的教程比没有更糟。

### 2.10 本地化（1/5，但俄语市场信号明确）

- **Starward Rogue**："без русского перевода … критично знание английского"（无俄语翻译，俄语玩家明确拒绝）。

### 2.11 商业化争议（1/5）

- **Nova Drift**："DLC upgrades in a ROGUELIKE of all things is atrocious, especially after increasing the base price"（roguelike 内购升级 + 涨价 = 强烈反感）。

> InfiAir 为免费开源（MIT），此条无直接风险，但**警示"内容变体不能拆成付费墙"**。

---

## 3. InfiAir 现状对照与差距识别

| 差评主题 | InfiAir 现状 | 差距评估 |
| --- | --- | --- |
| **弹幕可读性** | ✅ 敌弹视觉放大 ×2.4 提亮（`effects.enemy_bullet_visual_scale`）、玩家弹金色/敌弹红色、辅助瞄准框、Meta HUD 受击反馈 | ⚠️ **低**：基础好；高密度/全 buff 满屏时敌我弹混叠仍缺"最后一层"区分（如玩家弹描边/辉光强度档） |
| **操控与输入** | ✅ 鼠标瞄准 + WASD 主流方案、完整改键（settings→控制）、BackNavigator 全导航（Esc 逐级返回） | ⚠️ **中**：**手柄"映射就绪但未实机验证"（EXIT_FLOW §5）**——Nova Drift 差评证明输入方案是劝退级差评来源；键鼠 UI 交互（改键/设置）的手柄可达性未验证 |
| **难度公平感** | ✅ 3 档难度、必死曲线（可预期）、Boss 50s 逃跑压力阀、telegraph 前摇、出生无敌/受击无敌 | ⚠️ **中**：随机性（敌机类型/词缀 ramp/Buff 三选一）是否可能构成"不可避死亡"未做过系统性审计；高 ramp 段敌弹密度峰值需实测可避性 |
| **重复感/多样性** | ✅ 3 类 Boss 轮换、双随机事件（精英炮塔/轰炸编队）、16 种 Buff 构筑、难度 ramp | ⚠️ **中**：纯得分制无限流天然重复；局外 meta 缺失（无成就/本地排行榜）——"第二局最优解"风险需通过构筑深度对冲 |
| **成长系统** | ✅ Buff 三选一（改变玩法而非纯数值，如追踪弹/爆炸/激光）、RP 补给、基地整备 | ✅ **低**：构筑深度已是卖点 |
| **存档安全** | ✅ 单存档自动保存 + 损坏隔离（`.corrupt`）+ 返航自动更新 | ✅ **低**：规避了 Raptor 式存档灾难；但"存档在哪/如何备份"对玩家不可见 |
| **技术稳定性** | ✅ 31 断言场景 CI 全绿、存档类型守卫、损坏回退 | ✅ **低**：工程化保障充分 |
| **音频辨识度** | ⚠️ 全部程序化生成 BGM/音效 | ⚠️ **中**：Sky Force"BGM 平淡"差评是程序化音乐的固有风险；循环截断类技术问题需核对 |
| **教程** | ✅ 6 阶段独立教程（移动/冲刺/战斗/母舰/返航/Boss）、正局逻辑对齐 | ⚠️ **低**：注意"冗长"差评教训——教程可跳过性/回看入口待确认 |
| **本地化** | ✅ 中英双语 | ⚠️ **低**：俄语等第二外语可选 |
| **商业化** | ✅ 免费开源 MIT | ✅ 无风险 |

---

## 4. 改进建议（按优先级）

### P0 — 影响核心体验，建议优先实施

**1. 手柄支持实机验证与完善**
- **来源**：Nova Drift 4 条操控差评（"反人类操控/不学经典方案"）+ Starward/Sky Force 操控差评——输入方案是射击类差评最大来源；InfiAir 目前手柄仅"映射就绪未实机验证"。
- **建议**：① 导出前完成手柄实机验证（Windows/Linux 双平台）；② 校验 `ui_cancel`/`ui_accept` 与全部页面（设置/改键/结算/Buff 三选一）的手柄可达性（焦点导航已做样式，需验证全流程）；③ 若手柄映射与键鼠存在不可调冲突，提供第二套预设并允许改键（复用现有 `keybind` 系统）。
- **验证**：手柄实测走完 新游戏→对局→暂停→设置→改键→返航→结算 全流程；`back_navigation_test` 已覆盖决策分支。

**2. 高密度弹幕下的敌我可读性终层强化**
- **来源**：Sky Force（飞机被遮挡/找不到自己）+ Starward Rogue（敌我弹难区分/hit dot 条件显示）——可读性是跨游戏第一差评主题。
- **建议**：① 玩家弹加**恒定描边/辉光**（对比度分层，与敌弹红色系彻底解耦）；② 敌弹按密度/ramp 段提供**透明度降档**（满屏时自动降低杂弹透明度，保留威胁弹高亮）——可挂 `effects` 配置；③ 确认玩家受击判定点（r=7 小点）在满特效下始终可见（现有 Meta HUD 裂纹/波纹已覆盖受击反馈）。
- **验证**：`meta_health_fx_test` + 窗口截图在高 Buff 满屏态人工核对（`hud_capture` 已具备满层截图）。

**3. 局外进度感（成就/本地排行榜）补强**
- **来源**：Sky Force/Jamestown"重复刷+无变化"差评 + Nova Drift"run 单一最优解"——纯得分制缺少局外目标会放大重复感；本地排行榜曾是移植差距项（ROADMAP 已砍，可重启）。
- **建议**：① 重启**本地排行榜**（低投入：本机 top 分数持久化 + 开始页展示，见 PORTING_PARITY 差距清单与 ROADMAP Phase 3）；② 条件允许时加**成就集**（击杀数/Boss 全清/无伤波次/构筑组合）——注意 Raptor 成就 bug 教训：做了必须可验证（CI 覆盖）。
- **验证**：新增 `score_rank_test` 类断言场景；成就入 EXIT_FLOW/BALANCE_MAP 相关文档。

### P1 — 显著改善，建议近期实施

**4. 难度"公平感"审计（随机致死检查）**
- **来源**：Nova Drift"随机被秒杀/后期突变" + Sky Force"背板/廉价受击"。
- **建议**：① 审计高 ramp 段（15 分钟+）敌机生成组合是否存在"不可避弹幕"（交叉火力/追踪弹 + 减速场组合）；② Boss 弹幕密度峰值对照 telegraph 前摇时长验证"可反应时间"；③ 确保死亡可归因：受击方向指示（Meta HUD 波纹已有）+ 击杀回放非必需。
- **验证**：`autoplay_test` 长时探针 + 手动实机 15 分钟+ 对局；`enemy_damage_ramp`/`hp_ramp` 曲线复查。

**5. 音频辨识度与循环技术核对**
- **来源**：Sky Force"BGM 平淡" + Raptor"MIDI 截断循环" + Jamestown"音效弱"。
- **建议**：① 核对全部 BGM/音效的 `loop_mode` 与循环点（AGENTS 已约定只设 `LOOP_FORWARD`，需逐条核对无截断听感）；② 评估 BGM 动态分层（低血量/Boss 战切换 intensity 层，程序化生成可支持）；③ 关键事件（Boss 出现/母舰召唤/返航）增加可辨识音效主题。
- **验证**：`generate_audio.py` 再生 + 实机试听；BGM 循环测试（无头不适用，人工核对）。

**6. 教程"教到点"与可回看**
- **来源**：Starward"教程冗长且教不到点/跳过机制破坏教程"。
- **建议**：① 检查 6 阶段教程是否存在被移动速度/跳过键跳段漏教的情况；② 提供"重看教程"入口（开始面板 → 设置或主菜单）；③ 每阶段结束给一句话目标小结。
- **验证**：`tutorial_test` + 实机走完教程全流程。

### P2 — 打磨项，按资源排期

**7. 存档可见性说明**：在设置页或文档说明存档位置（`user://savegame.json`）与"自动保存"行为（Raptor 存档混乱差评的镜像预防——InfiAir 已无感自动保存，只差玩家告知）。
**8. 第二外语（俄语）**：若进入俄区市场，追加 `translations.csv` 俄语列（当前架构支持多列，成本低）。
**9. 波次节奏评估**：Sky Force"滚动慢没激情"差评的启示——InfiAir 为固定视口（无卷轴），节奏依赖敌潮密度/事件插入；可评估休整波与高压波的节奏对比度（`spawner` 波次间隔 ramp 已配置化，微调成本低）。
**10. 死亡归因强化**：受击时屏幕方向波纹已有（Meta HUD D8），可评估增加"致死弹来源高亮"（最后一击弹丸残留 0.5s 高亮）——低成本高反馈。

---

## 5. 结论

同类游戏差评共性问题按命中率排序：**战场可读性 > 操控/输入 > 难度公平感 > 重复/多样性 > 成长系统 > 存档 > 技术稳定 > 音频 > 教程 > 本地化**。

InfiAir 在**存档安全、技术稳定、成长构筑、操控方案（键鼠）**四项上已明显优于差评暴露的问题（工程化测试与自动保存设计正确）；**主要改进机会集中在**：

1. **手柄支持从"映射就绪"到"实机验证"**（输入是差评重灾区，InfiAir 最后一块输入拼图）；
2. **高密度弹幕的敌我可读性终层**（玩家弹描边/敌弹密度降档）；
3. **局外进度感**（本地排行榜重启 + 成就），对冲无限流的重复感；
4. 难度公平感审计与音频辨识度提升（P1）。

*调研方法说明：Steam 公开评价 API 抓取近期差评（每游戏 8-12 条），多语言采样；样本量有限（44 条），结论为方向性判断而非统计结论。评价引述为原文节选，不代表本报告立场。*
