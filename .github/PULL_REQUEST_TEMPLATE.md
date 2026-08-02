# Pull Request

感谢贡献！提交前请确认（详见 `CONTRIBUTING.md` 与 `AGENTS.md`）：

## 变更摘要（Summary）

<!-- 简述改动内容与动机，遵循提交信息风格：类型: 简述——要点 -->

## 验证（Verification）

- [ ] 本地最小验证集通过：`godot --headless --import --path .` + `godot --headless --path . --quit-after 300` + `res://test/smoke_test.tscn`
- [ ] 涉及子系统时已跑对应专项测试场景（`docs/TESTING.md` 清单）
- [ ] 未破坏 `AGENTS.md`「全局不变量」（碰撞层 / world_scale / view_world_rect / cfg / 协程纪律 / i18n / 热路径 / 池防护）
- [ ] 新增/改名数值键后已重跑 `python3 scripts/tools/gen_balance_map.py`
- [ ] 新增用户可见文本已同步 `data/translations.csv` 中英双列
- [ ] 文档同步（`docs/DESIGN_BASELINE.md` / `docs/ROADMAP.md` / `AGENTS.md` 等，见「文档同步要求」）
- [ ] CI（GitHub Actions）全绿

## 变更类型（Type of change）

- [ ] fix（缺陷修复）  [ ] feat（新功能）  [ ] docs（文档）
- [ ] test（测试）     [ ] refactor（重构）  [ ] perf（性能）  [ ] chore（杂项）

## 关联（Related）

<!-- 关闭的 issue 编号、审计条目编号（如 G010）、设计文档引用等 -->
