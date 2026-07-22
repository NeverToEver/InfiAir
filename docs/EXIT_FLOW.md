# 全局退出机制设计（EXIT_FLOW）

统一的"返回/退出"状态机：任何页面按返回键，行为可预测、安全、流畅。
实现：`scripts/back_navigator.gd`（状态机）+ `scripts/exit_confirm.gd`（全局退出确认窗）。

## 1. 页面层级（main 场景）

```
L3 模态:  ExitConfirm（全局退出确认窗，最高优先级）
L2 覆盖:  SettingsUI（opener = 暂停/开始面板）
          BaseUI（基地控制台）/ GameOverUI（结算）/ BuffUI（三选一，阻塞）
L1 对局:  Gameplay(HUD) ⇄ PauseUI
L0 顶层:  StartPanel（主界面/大厅）⇐ WelcomeScreen（仅首次启动）
```

`scenes/tutorial.tscn` 为独立场景：自身即顶层，Esc = 退出教程回主界面（`tutorial.gd` 自处理，不进状态机）。

## 2. 状态机伪代码

所有平台的返回输入统一收敛到 `BackNavigator.go_back()`：

```
func go_back():
    match decide_back_action():            # 按页面优先级 L3 → L0 判定
        CANCEL_EXIT:          # ExitConfirm 可见
            exit_confirm.cancel()          # 返回 = 取消退出，焦点还给开始面板主按钮
        CAPTURE_PASSTHROUGH:  # 设置改键捕获中
            pass                           # 不消费事件，让 SettingsUI 取消捕获
        CLOSE_SETTINGS:       # 设置页可见
            settings._on_back_pressed()    # 返回 opener（暂停或开始面板）
        RESUME_BASE:          # 基地控制台可见
            base_ui._on_resume_pressed()   # = 继续出击
        IGNORE:               # Buff 三选一 / 过场
            （吞掉输入，必须做选择）
        TO_MAIN_MENU:         # 结算页可见
            reset_run + reload_current_scene   # 回主界面（死亡时已删档）
        RESUME_GAME:          # 暂停面板可见
            pause_ui.close()
        OPEN_PAUSE:           # 战斗中（无覆盖、未暂停）
            pause_ui.open()                # 返回上一级 = 暂停
        CONFIRM_EXIT:         # 顶层（开始面板 / 欢迎页）
            exit_confirm.show_confirm(battle=false)
```

### 战斗中退出（二次确认 + 进度损失提示）

```
战斗中 Esc → 暂停面板（第一次确认机会）
  → 点「退出游戏」→ ExitConfirm(battle=true)（红色警告：退出将丢失本局进度）
    → 「确认退出」才真正退出；「取消」/ Esc 返回暂停面板
```

### 退出前统一清理（ExitConfirm 确认后）

```
func _execute_exit_cleanup(battle):
    GameState.save_profile()     # 最高分/设置/语言/键位落盘
    if battle:
        GameState.delete_save()  # 战斗中退出 = 放弃对局（与死亡语义一致）
    _on_exit_cleanup()           # hook：网络断开等（本项目无网络，预留单点）
    # 随后淡出黑屏 0.3s（过渡动画）→ get_tree().quit()
```

从开始面板退出：对局存档**保留**，下次启动可「继续对局」。

## 3. 键位映射表

| 平台 | 物理输入 | 映射到 | 处理 |
|---|---|---|---|
| PC | Esc | `ui_cancel`（引擎内置） | `BackNavigator._unhandled_input` |
| 手柄 | B / Circle（joy button 1） | `ui_cancel`（引擎内置默认映射） | 同上；A = `ui_accept` 确认，方向键/摇杆走 GUI 焦点导航（焦点样式已可见） |
| Android | 系统返回手势 | `NOTIFICATION_WM_GO_BACK_REQUEST` | `BackNavigator._notification` → `go_back()` |

确认窗内：Enter/手柄 A 触发焦点按钮（默认焦点在「取消」，安全侧）；Esc/手柄 B = 取消。

## 4. ExitConfirm 复用组件设计

- 挂载：`scenes/main.tscn`，CanvasLayer layer=40（高于一切 UI），`process_mode=Always`。
- API：
  - `show_confirm(battle: bool = false)` — normal/battle 双模式；battle 换 `UITheme.DANGER` 红字警告。
  - `cancel()` — 关闭（Esc 由 BackNavigator 路由至此）。
  - `_execute_exit_cleanup(battle)` — 退出前清理（测试可直接调用断言副作用）。
- 布局：`ChamferedPanel` + 标题 + 消息 + 「取消」（默认焦点）/「确认退出」（danger 色），复用 `UITheme.apply_button`；文案 `EXIT_*` 翻译键，监听 `locale_changed`。
- 复用方式：任何页面需要"确认后退出"只需 `show_confirm()`，清理/过渡/退出进程全部内部封装。

## 5. 平台差异化处理

- **PC**：无差异，Esc 全程可用（当前唯一实机验证平台）。
- **手柄**：依赖引擎内置 `ui_cancel` 默认映射（含 joy button 1），零配置；按钮焦点样式（hover 同款高亮）保证键盘/手柄导航可见。未实机验证（无导出流程）。
- **Android**：系统返回手势经 `NOTIFICATION_WM_GO_BACK_REQUEST` 接入同一状态机，无需额外逻辑；导出模板配置不在本项目范围内（无发布流程），标注为"映射就绪、未实机验证"。
- **教程场景**：独立顶层，Esc 直接回主界面，不进状态机（避免跨场景耦合）。

## 6. 测试

`test/back_navigation_test.tscn`：`decide_back_action()` 全分支覆盖 + 集成路径（Esc→暂停→恢复、设置返回、顶层 Esc→确认窗→取消、战斗退出确认链、清理副作用）。回归：`esc_navigation_test`（真实按键注入）与 `smoke_test` 必须全绿。
