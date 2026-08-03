# Changelog

本项目版本变更记录。格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)。版本号为 MAJOR.MINOR 递增（项目惯例，非完整 SemVer），版本同步点见 `release.sh` 与 `project.godot` `config/version`。**早期版本（≤ 3.22）变更细节见 `git log`**。

## [Unreleased]

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
