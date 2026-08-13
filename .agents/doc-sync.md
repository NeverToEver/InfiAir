# Doc Sync

## Overview
入口文档: 根 AGENTS.md + 这些 .agents/*（由它链接）; CLAUDE.md 仅入口概览; 结构/配置变更时保持本文件图最新。
## Rules
- 方向/阶段/暂停恢复决策 → docs/ROADMAP.md（单一决策源）; 设计意图/玩法规则/架构基线 → docs/DESIGN_BASELINE.md + 相关设计文档; 返回/退出层级/清理/平台返回 → docs/EXIT_FLOW.md + 回退导航测试。
- 新/改 balance 键或 Cfg() → 跑 python3 scripts/tools/gen_balance_map.py 再生成 docs/BALANCE_MAP.md（生成文件禁手改）。
- Scene Counts 单一权威: docs/TESTING.md（禁在其他文档硬编码计数; 增删 test/*_test.tscn 须同步）。
- docs/AUDIT_VAULT.md（代码审计档案）专有, 禁删/合并: 仅追加新发现、回填修复记录; 任何清理/归档不得移除。
- 完成计划/评审文档: 全文移 docs/archive/ + EXECUTION_LOG.md 登记（date/commit/摘要/关键决策与教训/链接）+ 从 docs/ 删除并更新引用（archive 内链接可断）。
- 结构/命令/测试策略/配置位置变更 → 同步 AGENTS.md + .agents/*; CI/CD 变更: 先可审查工作流 + 发版说明再同步（含 release.sh）; 政策: 仅官方 action/脚本/引擎/模板, 禁第三方。
