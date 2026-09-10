# AGENTS.md — 智能体与开发者入口

> 流程与纪律的唯一权威。设计意图见 `docs/DESIGN_BASELINE.md`；方向/债务/决策索引见 `docs/ROADMAP.md`；运行方式见 `README.md`。此处不复述它们的内容，只给指针。

## 项目形态

- Godot 4.6 .NET + C#（全量 C#，零 GDScript），GL Compatibility，1920×1080。
- 分层：`csharp/core/` 纯逻辑（不依赖 Godot）/ `csharp/godot/` 绑定层（场景脚本、UI、服务）。
- 数值单源 `data/balance.json`；颜色单源 `csharp/godot/UITheme.cs`。

## 验证门禁（提交前必过）

```bash
dotnet build                          # C# 构建，零警告（TreatWarningsAsErrors）
bash scripts/ci/check_comment_stamps.sh  # 源码注释无日期戳
bash scripts/ci/check_language_style.sh  # 注释语种 + 散文术语单一叫法
bash scripts/ci/check_ui_copy.sh      # 玩家可见文案：无开发措辞/无缺键/无空字段
bash scripts/ci/check_import.sh       # 资源导入无警告
bash scripts/ci/check_smoke.sh        # 主场景 300 帧无头冒烟
./release.sh                          # 打包发布（仅发布时需要）
```

CI 单 fast-gate 与上述一致（`.github/workflows/ci.yml`）。UI/视觉改动另需窗口化实机人工过目——无头门禁不覆盖 GPU/shader 管线。

## 文档纪律

- 事实单源：任何事实只写一份，其余位置用指针引用，禁止复述。门禁口径只在本文件声明。
- `ROADMAP.md`：Current State 只在方向变化时更新；Decisions 只登记一行「决策 + 为什么」（细节在 git 历史）；债务修复后直接删除条目。
- `DESIGN_BASELINE.md`：只写设计意图定稿；系统行为以代码为准，不在此维护。
- 代码注释：禁日期戳与审计轮次编号，只写代码本身无法表达的约束。

## 语言风格

- 散文（注释 / 文档 / 提交信息）一律简体中文；标识符、类型名、引擎 API（`Area2D`、`GD.Load`）、
  配置路径、动作名、外部产品名保留英文。门禁只判散文，不要求翻译标识符。
- 术语单一叫法——同一概念全库只写一种：

  | 用 | 不用 |
  | --- | --- |
  | 本局（合法复合词：本局内） | 对局 / 单局 / 局内 |
  | 敌机 / 敌弹 | 敌人 / 弹丸 |
  | 增幅 | buff / 增益 |
  | 天赋 | 技能树 |
  | 弹反 | 格挡 |
  | 通用弹写「弹体」，按阵营写「玩家弹 / 母舰弹」 | 弹丸（语义分裂，勿用） |
  | 黎明站（地点专名）/ 基地（功能语境） | — |

- 本表与「验证门禁」的 `check_language_style.sh` 一一对应：术语表改动须同步脚本，否则门禁与纪律脱节。
- 玩家可见文案另有独立口径与门禁（`check_ui_copy.sh`），两者互不替代。

## 提交信息

- 主题行 ≤72 字：`类型: 是什么`（feat/fix/perf/refactor/docs 等）。
- 正文可选一段「为什么」（动机、反转的决策）。
- 不写：根因推演、修法细节、验证过程——根因与修法在 diff 和代码里，验证有门禁脚本背书。
