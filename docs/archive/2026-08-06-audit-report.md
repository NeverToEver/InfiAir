# 全项目审核报告（2026-08-06）

> 方法：按 `docs/AUDIT_REVIEW_SOP.md` Phase 1，12 个领域子代理并行审核（GDScript 纪律 / 平衡配置 / 碰撞伤害视图 / UI·i18n·导航 / 性能与对象池 / 持久化安全 / 文档漂移 / 主流程状态机 / 战斗实体 / 测试与 CI / Shell 与工具 / 玩法数值），随后对高危与部分中危发现做了人工逐行复核（全部属实）。Phase 2 分类口径：纯 bug / 设计目标未达成 / 应急补丁痕迹 / 文档与代码矛盾。
> 本报告仅登记发现与定性，未修改任何代码与文档；按 SOP 后续应分类 → 分批修复 → 回填 `docs/AUDIT_VAULT.md`。
> 总体结论：项目成熟度高，历史审计（C/E/G/H/K/L/Q/R 系列）大多真实落地；新问题主要集中在 2026-08-04~06 功能落地波（迷雾事件、分裂者、4 型 Boss、P0-3 子弹视觉改造）的"半链路遗漏"。

## 高危（2）

| # | 类别 | 位置 | 描述 |
| --- | --- | --- | --- |
| H1 | 纯 bug（状态机漏洞，已人工复核） | `scripts/spawner.gd:652` + `scripts/main.gd:710` | **Boss 战中返航 → `clear_pending()` 无条件 `_boss_active = false` → 继续出击后双 Boss 同场**。G01 修复只考虑"预警 2s 窗口、Boss 未生成"的 case；Boss 已生成在战时返航同样可达（返航链明确保留 Boss），而 `_boss_timer` 战时持续增长、`score >= _next_boss_score` 仅击杀才推进——继续出击后 `spawner.gd:432` 门控立即满足，出第二个同型 Boss。此后轮换/休整/狂暴编排（`_enrage_boss` 单槽）全面脱节，可链式出第三只。档案 G01 只记录了反向 case，无测试覆盖。修法建议：按"注册表是否仍有存活 Boss"区分复位条件并补测试。 |
| H2 | 设计目标未达成（内容不可达，已人工复核） | `scripts/spawner.gd:467-475` × `data/balance.json` spawner.unlock_scores | **分裂者（第 5 型敌机）实战中永不生成**。`unlocked_types()` 上界 `mini(5, unlock_scores.size())` = mini(5, **4**) = 4，`ENEMY_TYPES[4]`（`"split": true`）永不入池。`unlock_scores` 自平衡化起就是 4 档，新增 5 型机的提交未扩展它；测试直接注入 `ENEMY_TYPES[4]` 绕过解锁路径，故全绿但实战不可达。附带疑点：子机以 `p_difficulty = 1.0` 生成（`enemy.gd:185`），HP/速度不随对局 ramp，基准语义注释与函数定义不一致。 |

## 中危（8）

| # | 类别 | 位置 | 描述 |
| --- | --- | --- | --- |
| M1 | 纯 bug（池状态残留，已复核） | `scripts/bullet.gd:269-292` | **子弹池 `self_modulate` 染色残留**：`_apply_faction()` 重置 `scale`/`modulate` 但不复位 `_sprite.self_modulate`。全仓 5 处写入（laser 黄、Boss 重弹橙、致死高亮红 `bullet.gd:308` 等）无一复位为白——P0-3 改造（共享纹理 tint）时丢失了对等重置。laser 为高频弹种，对局中必然出现复用弹带旧 tint。修法一行 + pool_reuse_test 补外观断言。 |
| M2 | 纯 bug（已复核） | `autoload/game_state.gd:1558-1562` × `scripts/save_manager.gd:66-72` | **登录用户损坏存档的 `.corrupt` 备份被二次隔离删除**：`SaveManager.load()` 已 rename 为 `.corrupt` 并返回 `{}`，`load_run_data` 对空字典做档主校验必然不匹配，再调 `quarantine()`——先删掉刚生成的 `.corrupt` 备份，再 rename 已不存在的正本，失败并刷伪警告。净效果：损坏存档彻底消失 + 误导性警告。`startup_flow_test` 每轮 CI 走此路径但从未断言备份存在。加一行 `last_was_corrupt` 判空即可闭合。 |
| M3 | 设计目标未达成（已复核） | `scripts/fake_enemies_event.gd:33` × `scripts/fake_enemy.gd:78` | **伪敌机约 75% 出生即销毁、从未可见**：出生 y = 视野顶 − randf(20, 260)，而出屏销毁余量仅 80px——出生深度 >80 的个体在 `_entered=true` 后第一个物理帧即被 `queue_free`。`count=5` 幽灵机群实际可见约 1-2 只，违背 FOG_EVENTS「错峰入场+降入悬停带」设计；fog_event_test 只断言生成计数，属测试盲区。 |
| M4 | 设计目标未达成 | `scripts/boss.gd:653-691` | **4 型 Boss「月蚀」难度分档残缺**：interval 列表无 `E4_RING_INTERVAL`，speed 列表无 `RING_BURST_SPEED`/`E4_RING_SPEED`/`E4_RELEASE_RING_SPEED`，弹数增量无 `E4_RING_COUNT`/`E4_RELEASE_RING_COUNT`——type4 狂暴参数三档恒定（easy 偏难、hard 偏易）。同类遗漏 E33（type3）已定性并修复，type4 落地时未同步扩展分档表。 |
| M5 | 设计目标未达成（C07 修复半套） | `scripts/starfield.gd:42,56-57` | **zoom>1 时星空不覆盖可见区右/下边缘，每局可见**：星点锚定 (0,0) 铺 `[0, 1920/zoom]×[0, 1080/zoom]`，medium 档可见区 (249,140)..(1671,940) 的右侧/下侧 L 形带完全无星。档案 :965「登记不修」的论证前提（恒覆盖）已被 C07 自身破坏，需重新评估。 |
| M6 | 应急补丁痕迹（Q23 清扫遗漏，已复核） | `test/user_db_test.gd:18-21` | **测试删除本地 `user://users.json` 且无快照还原**——本地跑一次即永久销毁开发者全部账户+用户排行榜。Q23 已修同批 3 个账户测试，user_db_test 被遗漏。 |
| M7 | 纯 bug（测试副作用，L15 盲区） | `test/view_zoom_test.gd:85-94`、`window_size_test.gd:64-72`、`difficulty_test.gd:293-311`、`mouse_lock_test.gd:56-58`、`base_system_test.gd:118-140` | **5 个测试经"部分覆写 profile.json + load_profile"间接清零 pre-login 最高分与高分榜并落盘**，无快照还原。L15 只修直写路径，未覆盖间接清零；且档案 L15 称 base_system 已修与 git 事实不符。 |
| M8 | 文档与代码矛盾（流程反复复发） | `docs/BALANCE_MAP.md`（boss.gd 段） | **BALANCE_MAP 行号漂移第 6 次复发**（a8c97a4 注释改动后未重跑生成器，实测 diff 125+/125- 纯行号 +3，键零变化）。E10/K18/L10/Q08/R15 五次同款均未根治——根因是"改码后重跑生成器"靠人记。建议 CI 加"生成器重跑零 diff"闸或生成物去行号化。 |

## 低危（按主题归并，30 项）

### 状态/逻辑残留
- `event_manager.gd:144-155`：`set_run_active(true)` 漏重置 `_fog_cooldown_left`，次局首个迷雾事件被上局残留冷却额外推迟最晚 12s（Q12 同族遗漏）。
- `main.gd:373-379,560-572`：give_up 与 dock 蓄力相互独立，H+K 同按 3s 同帧完成 → 召唤小窗打开同帧死亡，小窗冻结永驻（`_on_player_died` 无清理，返航路径有）。
- `game_state.gd:592-595` vs `:1647-1651`：里程碑计数运行时（单次 +1）与读档（while 全补）两条路径口径不同，正常对局不可达，潜伏不一致。
- `elite_turret_event.gd:136-143` / `formation_strike_event.gd:126-137` + `main.gd:340`：母舰互斥（L13）只查触发期，事件进行中仍可蓄力召唤母舰清场全额领奖。
- `boss.gd:774-776` × `boss_movement.gd:98-101,138-146`：逃跑警告期上飘被 type1 P2 / type3 P2 / type4 的绝对 y 赋值覆盖，三型无上飘效果（纯视觉）。
- `strike_carrier.gd:32,115` + `elite_turret_event.gd:125`：航母悬停 HOVER_Y=300 与炮塔行锚点为绝对 y，未加 view 基线（D10 同族），非默认视角档偏高 140~222px。
- `mothership.gd:665`：加特林弹 `b.scale = Vector2(0.6,0.6)` 连带缩放碰撞形状（半径 6→3.6×ws），若只要视觉应缩放子 Sprite2D。
- `player_damage.gd:49-52`：护盾吸收分支不写 `last_hit_frame`，同帧"盾+实伤"可双结算，与 A16 单帧守卫口径不对称（概率极低，登记即可）。
- `game_state.gd:1626`：missions 恢复 `"goal"` 用裸 `int()` 未走 `save_num` 判型（R06/R07 判型族同族遗漏）。

### 配置/工具链
- `spawn_telegraph.gd:5,18` vs `spawner.gd:547-549`：预告线视觉寿命 `DURATION` 硬编码 0.6 不读 `spawner.telegraph_duration`，调参时视觉与敌机出现时刻脱钩（默认值一致，现行行为正确）。
- `elite_turret_event.gd:110-112,205`：`ammo_sequences` 只判容器 Dictionary，难度键缺失/非 Array 时 `for a in p_ammo` 崩溃（boss patterns 侧有元素级判型 L07，精英侧没有）。
- `bullet_pool.gd:35`：敌弹硬上限判据 `Bullet.active_count()` 不分阵营，实际敌弹 cap ≈ 500 − 活跃玩家弹（P2-3 口径登记缺漏，无行为影响）。
- `balance_editor.py:283-292`：备份/写盘侧 `OSError` 无兜底，磁盘满时空响应 + 裸 traceback（R08 只给读侧加了友好 400）。

### 持久化/账号
- `user_db.gd:54-73`：`_derive` 并非标准 PBKDF2-HMAC-SHA256（salt 预拷贝致块长 20 字节、双块拼接），与头注/CHANGELOG/ARCHITECTURE 口径不符；自建自验无实际弱化，但不与标准工具互通。
- `user_db.gd:150,172-175,192-193,224`：Q17 修复不完整——用户记录条目级非 Dictionary 仍无守卫（顶层有），手改 users.json 可刷运行期错误。
- `.agents/persistence-security.md:10` 声称 users.json 损坏"通知开始页"，实际 `save_corrupt`/`profile_corrupt` 只接 run save 与 profile.json，users.json 损坏静默重建，玩家视角账户凭空消失。
- `game_state.gd:1413-1418,1430-1436`：账户化前的匿名 `savegame.json` 无迁移、永不可达（旧玩家进行中进度静默失效）。
- `user_db.gd:247-255`：`delete_user` 删存档但遗留 `<save>.corrupt` 备份（磁盘残留，无功能影响）。

### 玩法/数值（需设计拍板，勿盲改）
- `BOSS_REDESIGN.md:41`（G3 ≥0.35s telegraph）vs `:88`（§5.2 表）× `balance.json:613`（E2_AIM=0.3s）：2 型狂暴狙击（900 速/21 伤）telegraph 低于 G3 下限，两文档自相矛盾。
- `game_state.gd:696-713`：里程碑循环档边界增量倒挂（80000→84050 增量 4050，仅为前档增量 40%），buff 节奏锯齿；buff 池拿完后影响有限，登记。
- `DESIGN_BASELINE.md:22` 称 "RP: run economy from kills/score"，实际 RP 仅来自 Boss 击杀 +5 与任务领取 +3。

### 文档/翻译
- `README.md:36` / `README.en.md:36`：特性清单停留"双随机事件"，迷雾事件系统（2026-08-05 上线）未收录。
- `README.md:136` / `README.en.md:136`："45 场景清单见 AGENTS.md#quick-reference"指向错误，实际在 `docs/TESTING.md:19-69`。
- `data/translations.csv`：`START_HAS_SAVE`/`START_NO_SAVE`/`START_SUBTITLE`/`START_TUTORIAL_DONE` 4 键全仓零引用（R12 死代码清理漏删 csv 键）。
- `game_state.gd:233`：注释称翻译键族 `TASK_*`，实际为 `MISSION_*`。
- `AGENTS.md` Quick Reference："6 个非 autoload 服务"清单漏第 7 个服务 `UserDB`（`game_state.gd:102`）。
- `.github/workflows/ci.yml:120` 实际使用 `actions/upload-artifact@v4`，与"仅官方 checkout action"表述矛盾（实质仍无第三方 action）。
- `CHANGELOG.md:3`：3.23 版本断档无说明（疑似有意跳号，无法区分跳号与漏记）。
- `back_navigator.gd:37` × `welcome.gd:539`："右键=固定返回"只在 BackNavigator（main.tscn）实现，welcome 顶层右键无效——文档未声明该例外。

### 测试规范（L15/Q23/A7 同族回潮，均仅影响本地 dev profile）
- `keybind_test.gd:28,100`、`esc_navigation_test.gd:84`、`base_system_test.gd:177`：reset/rebind 自动落盘，开发者自定义键位被重置且无快照。
- `smoke_test.gd:859` 等 6 处：持久化设置结尾"恢复默认值"而非用户原值。
- `mothership_upgrade_test.gd:21,43,54,56,58`：直写私有字段 `GameState._milestone_count` ×5。
- `boss_phase_test.gd:274-279`：`_check(boss5 != null)` 后无守卫解引用，生成失败时跳过 284-287 行的 balance.json 恢复 → 仓库文件留损坏态。

### Shell/音频工具
- `generate_audio.py:254-270`：琶音"跨越接缝"分支恒为死代码且语义写反（实测无害）。
- `generate_audio.py:272-275` × `main.gd:476`：50ms 软起音淡入被烘焙进资产，与"40s 无缝循环"声称相悖（圈首 ≈5dB/50ms 凹陷）。
- `.agents/shell-scripts.md:9` 声称 run.sh 有 `set -euo pipefail`，实际仅 `set -e`。
- `release.sh:16-17,24`：GODOT 兜底链断裂无诊断（直接 command not found）；无 `--help`（违反自身约定）；tar/zip 前置检查位于两次导出之后。
- `fake_enemy.gd:70,74,76`：`_physics_process` 每帧直接 `sin()` ×3 未走查表（量级与档案 G017 判不修相当，登记即可）。
- `enemy_pool.gd:49-51` / `explosion.gd:56-57`：池化 spawn 侧 reparent 可在物理回调链内同步执行，与池自身防护口径矛盾（实测无恙，口径登记）。

## 复核与排除说明

- 高危 H1/H2、中危 M1/M2/M3/M6 已由协调者逐行人工复核属实（读码取证，未运行游戏）。
- 各领域代理报告与 `docs/AUDIT_VAULT.md` 已登记项做了查重；档案已记录且未复发者（如 welcome.gd:225 计时器取舍、boss_movement 无类型参数、laser BEAM_HALF_WIDTH 未乘 ws、`_explode` 对炮台零 AoE 等）未收入本报告。
- 数值类发现遵循 SOP「不盲调平衡」原则：凡疑似有意设计（里程碑锯齿、G3 telegraph、子机 mult=1 基准）一律标注"需设计拍板"。

## 建议处置顺序

1. **立即修**（真 bug 且有玩家可见后果）：H2（分裂者不可达）、M1（弹染色残留）、M2（损坏备份被删）、M3（伪敌机秒删）。
2. **修 + 补测试**：H1（双 Boss，需按存活 Boss 注册表区分 clear_pending 复位条件）、M6/M7（测试副作用快照范式补齐）。
3. **设计拍板后处理**：M4（type4 分档表扩容）、M5（星空重布或回退）、G3 telegraph 矛盾、里程碑锯齿。
4. **流程堵漏**：M8（CI 加 BALANCE_MAP 零 diff 闸）。
5. **低危批量顺手清理 + 回填 AUDIT_VAULT**：死键/死数据/注释失实/口径登记类。
