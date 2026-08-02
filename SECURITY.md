# 安全策略（Security Policy）

## 支持范围（Supported Versions）

| 版本 | 支持 |
| --- | --- |
| 最新发布版（GitHub Releases 上的最新 tag） | ✅ 安全更新 |
| 更早版本 | ❌ 请升级到最新版 |

## 安全属性说明

InfiAir 是**纯单机游戏**：无网络通信、无远程服务、无第三方账号体系、无密钥或凭据处理。运行时唯一的外部交互是本地 `user://` 持久化（`savegame.json` / `profile.json`）与可选的离线数值编辑器（`scripts/tools/balance_editor.py`，仅监听 127.0.0.1）。因此攻击面极小，但存档/档案文件的健壮性（损坏隔离、类型守卫）仍是本项目安全相关的关注点。

## 漏洞报告（Reporting a Vulnerability）

请**不要**公开提交漏洞类 issue。报告方式：

- **首选**：GitHub 仓库的 **Security → Report a vulnerability**（私有披露）
- **备选**：开一个私有 issue 并说明「安全相关问题」；请附上复现步骤、影响范围与建议修复方向（如涉及损坏存档的输入构造）

处理预期：维护者会在 7 天内确认并评估；确认的漏洞将按修复优先级纳入后续发布，并在对应版本 CHANGELOG 与 Release notes 中注明。报告者署名以披露者意愿为准。
