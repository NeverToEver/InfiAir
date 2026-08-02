# 鼠标锁定窗口内设置项（mouse_lock）——执行计划与追踪（2026-08-02）

> 状态：**已完成**。审计登记见 `docs/AUDIT_VAULT.md` F 系列（F01/F02）；本文件为发现-设计-落地-验证追踪单一事实源。

## 背景

鼠标移出游戏窗口后，Godot 停止向窗口派发鼠标移动事件：`get_global_mouse_position()` 冻结在最后位置，准星（`AimCrosshair` 跟随 `player.aim_point()`）卡在屏幕边缘；移回窗口时位置跳变，平滑增量 `raw - _aim_last_raw` 异常。此前尝试未彻底解决，本次从根上阻止鼠标离开窗口内容区。

## 方案

新增设置项 **`mouse_lock`（鼠标锁定窗口内，默认开启，profile 持久化）**。开启时，只要游戏窗口聚焦且可见，鼠标移出窗口内容区即被 `Input.warp_mouse()` 拉回窗口边缘内侧 1px；窗口失焦（Alt-Tab/点击外部应用）自动放行。不触碰 `AimCrosshair`/`aim_point()` 现有逻辑——从根上消除"鼠标出框"前提。

| 组件 | 变更 |
| --- | --- |
| `autoload/game_state.gd` | `mouse_lock`（默认 true）+ `mouse_lock_changed` 信号 + `set_mouse_lock()`（同值忽略/落盘/广播）+ load/save profile（旧档无字段保留当前值） |
| `scripts/mouse_trap.gd`（新，挂 Main） | 窗口聚焦期间 confine：`mouse_exited` 信号 → `Input.warp_mouse(窗口位置 + clamp(已知位置, 1, size-1))`；`_process` 每帧防御 + 缓存最后已知窗口内位置；失焦放行；`DisplayServer.get_name() == "headless"` 时跳过；`_warp_target()` 纯静态 clamp 函数 |
| `scenes/main.tscn` | Main 下挂 MouseTrap 节点（PROCESS_MODE_ALWAYS） |
| `scripts/settings_ui.gd` | 显示区（窗口大小后）加 mouse_lock 开关（allow_unpress 单开关，对齐 reduce_flash）+ 说明文字；公开 `mouse_lock_button()` |
| `data/translations.csv` | `SET_MOUSE_LOCK` / `SET_MOUSE_LOCK_DESC`（中英双语） |
| `test/mouse_lock_test.gd/.tscn`（新） | 16 项断言：默认开启/切换信号/持久化往返/旧档兼容/clamp 纯函数/设置页按钮 wiring |

### 关键实现细节

- **坐标语义**：Godot 4 的 `Input.warp_mouse()` 接受**屏幕坐标**，warp 目标 = `get_window().get_position()`（窗口左上角屏幕坐标）+ 内容区 clamp 点。
- **触发路径**：`mouse_exited` 信号为主触发；`_process` 每帧用缓存位置防御（覆盖窗口尺寸/位置变化偶发越界）。clamp 到边缘内侧 1px，避免系统判定鼠标仍在窗外造成 exited/warp 循环。
- **生效范围（F02 修正）**：confine 仅在对局准星活跃（未暂停 + `Input.mouse_mode == MOUSE_MODE_HIDDEN`）且窗口聚焦时生效；暂停/Buff/基地/结算/过场/开始页等非准星态与失焦一律放行——暂停后鼠标可自由移出窗口点系统标题栏关闭按钮退出游戏（F02 缺陷修复后）。
- **已知取舍**：拖动标题栏时鼠标位于 OS 装饰区会触发 `mouse_exited` 被拉回，固定尺寸窗口下可接受，用户可在设置中关闭。
- **headless**：`Window.mouse_position` 在 headless 显示服务器不可访问，`_process` 开头按 `DisplayServer.get_name()` 跳过；放行判定抽 `_trap_enabled()` 静态纯函数供 headless 断言。

## 验证

```bash
godot --headless --import --path .                       # 0 错误，.translation 重生成
godot --headless --path . --quit-after 300               # 0 错误（MouseTrap 挂载正常）
godot --headless --path . res://test/mouse_lock_test.tscn    # 16 PASS 0 FAIL
godot --headless --path . res://test/i18n_test.tscn      # 0 FAIL
godot --headless --path . res://test/window_size_test.tscn   # 0 FAIL
```

无头测试覆盖数据层/纯函数/UI wiring；`warp` 运行时行为（macOS 窗口事件）需真机验收：鼠标移出窗口应被拉回边缘内侧、失焦后放行。

## 阶段提交记录

| 提交 | 内容 | 结果 |
| --- | --- | --- |
| `ddea0ad` | GameState mouse_lock 状态/信号/profile 持久化 + 数据层断言 | 9 断言 0 FAIL |
| `c3425e7` | MouseTrap 组件 + main.tscn 挂载 + clamp 纯函数断言 | 13 断言 0 FAIL |
| `c3e4d5c` | 设置页开关 + 中英翻译键 + 按钮 wiring 断言 | 16 断言 0 FAIL |
| `5e15219` | 文档同步（AGENTS/ARCHITECTURE/DESIGN_BASELINE/TESTING/AUDIT_VAULT + 本文件） | — |
| （F02） | 暂停/非准星态放行 confine（_trap_enabled 纯函数 + 7 项放行断言，23 项全绿） | — |
