# Changelog

本项目版本变更记录。格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)。版本号为 MAJOR.MINOR 递增（项目惯例，非完整 SemVer），版本同步点见 `release.sh` 与 `project.godot` `config/version`。**早期版本（≤ 3.22）变更细节见 `git log`**。

## [Unreleased]

### 玩法（2026-08-04，母舰扩展）

- **母舰火力随里程碑升级**（`docs/2026-08-04-mothership-expansion-plan.md`）：对局里程碑 ≥5 后加特林/导弹伤害 ×1.5、射速 +25%（`mothership.upgrade` 配置段）；驻留状态栏显示「火力升级 ★」提示

### 账户与入口（2026-08-04，本地账户系统）

- **本地用户系统**（`docs/2026-08-04-local-accounts-plan.md`，重启 Phase 3）：`UserDB`（`user://users.json`，PBKDF2-HMAC-SHA256 密码 + 盐、注册/登录/游客/删除、last-login 排序、每用户设置与统计）；删号连带清理该用户存档
- **welcome 主场景**（新 `project.godot` 主场景）：登录面板（用户名/密码/下拉）+ 难度 + 教程 + 设置 + 本地排行榜 overlay + 游客/删除/退出确认模态；ESC 层级 overlay→模态→退出确认；**StartPanel 退役**
- **每用户存档/档案隔离**：存档 `user://savegame_<user>_<hash12>.json`（档主校验，不匹配隔离）；游客不存档、设置仅内存（原版 bug 修复清单 B7 全部落地）；`profile.json` 退役迁移（首个注册用户合并）
- **本地排行榜**（用户维度）：Top10 本地榜（分数降序 + 先到先得），结算页与 welcome overlay 展示
- 新增 3 断言场景（`user_db_test` / `user_session_test` / `welcome_flow_test`）；全量 40 断言场景 0 FAIL

### 工程化（2026-08-02）

- 新增 GitHub Actions **CI**（`.github/workflows/ci.yml`）：无头导入 + 主场景冒烟 + 37 断言场景全量回归，push/PR 触发
- 新增手动触发**发布工作流**（`.github/workflows/release.yml`）：双平台导出打包 → 打 tag → 创建 GitHub Release
- 新增 `CONTRIBUTING.md`（贡献指南）、`SECURITY.md`（安全策略）与 GitHub issue/PR 模板
- `project.godot` 增加 `config/version` 发布版本元数据

### 玩法（2026-08-02）

- **本地高分榜**：结算页本局排名 + 历史 Top5，开始页 Top3（`profile` 持久化）
- **手柄支持**：左摇杆移动 / 右摇杆虚拟准星瞄准 / 动作键（A/RB/LB/X/Y/L3/R3）；设置页「手柄」分区可调右摇杆灵敏度与摇杆死区
- **可读性**：玩家弹白芯描边（敌我弹区分）；致死弹 0.5s 高亮残留（死亡归因）
- **教程可重看**：通关后无存档时教程按钮放行

### 玩法（2026-08-03）

- **战斗公平感四机制**（`docs/archive/2026-08-03-combat-fairness-plan.md`）：受击宽限帧（`player.grace_period`，消灭 ghost hit）、擦弹得分（`player.graze_radius`/`graze_score`，风险-回报技巧轴）、Boss 阶段转场清弹 + 玩家短暂无敌 + 分段血条（`boss.phases.clear_on_shift`/`transition_invincible`/`hud.boss_bar_segments`）、F 键弧光弹反盾（`player.parry.*`，主动防御反击，完整周期 3.8s，手柄 LT）；新增 4 断言场景（grace_period/graze/boss_phase_transition/parry，93 断言）

### 架构与工程（2026-08-03）

- **A3/A4 架构债收尾**：Boss 攻击/移动/狂暴三注册表 + 机型参数表（新增机型/攻击仅需注册一行）、Player buff 声明式效果表 `BUFF_EFFECTS`（新增数值型 buff 只需表加一行）
- **L 系列第十轮全仓库审查**：P1×3/P2×9 修复（池化复用 buff 信号重连回归、截图工具编译错误、autoplay 母舰状态表漂移、判型补全等）
- 新增 2 架构断言场景（`buff_effects_test` 38 断言 / `boss_registry_test` 29 断言）；全量 37 断言场景 0 FAIL

### 玩法（2026-08-03，B 梯队：公平感延续，fair plan §8）

- **Boss 攻击独特 tell**：9 种攻击起手各有独特音效变体 + 视觉前兆冲击环（`boss_attacks.gd ATTACK_TELLS`），玩家可区分「来的是什么」
- **DDA 弹幕密度降档**：玩家受击后 5s 内敌机开火/波次/Boss 攻击间隔拉长（`dda.duration`/`dda.factor`），**只拉间隔不降收益**（分数公平）
- **死亡回放**：环形缓冲录制最近 3s 敌弹轨迹，死亡后幽灵弹幕重放死因（`death_replay.gd`，暂停结算中照常播放）

### 架构与工程（2026-08-03，Phase 0 收尾）

- **test/ 门禁盲区修复**：`test/` 纳入 `gdformat --check` + `gdlint`（23 文件格式化、18 条静态问题修复）；CI 新增编译探针步骤（逐场景 `--quit-after 2` + 错误 grep，捕获 `--import` 不解析未引用场景的编译错误盲区）+ 断言场景单场景超时
- **A8 PlayerVisuals 拆分**（最后一项架构债）：尾焰/残影池/机身色调/受击点/弹反视觉/擦弹闪光迁出 player.gd（`scripts/player_visuals.gd`，组合委托模式）
- **L 系列待办收敛**：L13 母舰在场期事件互斥（精英炮塔/编队不再被母舰自动火力白嫖发奖）、L14 Boss 段切换 y 平滑过渡（消除 1/4 屏瞬移）、L15 测试 profile 最高分快照还原（20 场景）、L16 smoke 弱断言、L18 发布工作流版本号提交落地
- **P2 清理**：`ACTION_LABELS` 死代码删除、`back_pressed` 死信号登记、`profile_corrupt` 损坏档案开始页提示（新增 `START_PROFILE_CORRUPT` 双语键）
- 全量 37 断言场景 0 FAIL；`gdformat`/`gdlint` 全量（含 test/）全绿

## [3.26] - 2026-08-02

### 性能

- 性能优化全量落地：敌机生成统一池化（`USE_POOL` A/B 开关）、`view_world_rect` 物理帧缓存、受击闪白手动衰减、`sin_fast` 查表清扫、渲染合批；`perf_bench` 约 -8~9%

### 玩法

- Boss P2 阶段走位升级：一型/三型 P2 strafe 提速 + 纵向正弦往复、二型 P2 dash 节奏、三型 P1 锚线下区间呼吸（`boss.movement` 配置段）
- 鼠标锁定窗口内设置项（防准星出框失控；暂停/非准星态与失焦放行）

### 修复

- G 系列核心逻辑 32 项处置（spawner 预警取消复位、Boss 逃跑期免伤、教程入口守卫、注册表 O(1) 索引等）
- E 系列存量盲区修复（母舰溅射对 Boss 生效、教程删档守卫、难度表子键校验等）
- A21 测试失败基线根因修复（入场坐标按战斗锚线动态定位）

### 文档

- 全量文档口径统一（状态误记订正、内部矛盾消除、计数与失效哈希修正）
- 已完成工作压缩留档：`docs/archive/EXECUTION_LOG.md` 索引 + 10 份计划/审核文档归档
- 许可证落地：MIT + 第三方声明（Noto Sans SC / OFL）

## [3.25] - 2026-08-02

### 修复

- D 系列全量代码审查修复（入场 Timer/预告线清理、入场中断复位 `abort_entry`、HUD 缓存、硬编码收敛等）
- E 系列批次修复（教程按钮禁用与入口守卫、提前离舰进度条清理、存档原子写等）

## [3.24] - 2026-08-01

### 修复

- C 系列 Godot 规范审计 35 项处置（教程协程泄漏、存档 key_bindings 类型守卫、难度表校验、子弹位移改物理帧等）
- B 系列业务逻辑修复（狂暴瞄准线泄漏、time_scale 复位、Boss 逃跑结算守卫、追踪弹 stale 引用等）

### UI

- 全界面系统化 uplift：统一模态骨架与动效、Buff 卡片与 HUD 仪表簇重设计

---

早期版本（≤ 3.22，2026-07-31 发布工程化起步）的变更记录见 `git log`；移植对齐时期历史见 `docs/archive/PORTING_PARITY.md`。
