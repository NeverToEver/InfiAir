# 全局退出机制设计（EXIT_FLOW）

统一的"返回/退出"状态机：任何页面按返回键，行为可预测、安全、流畅。
实现：`scripts/back_navigator.gd`（状态机）+ `scripts/exit_confirm.gd`（全局退出确认窗）。

## 1. 页面层级（main 场景）

```
L3 模态:  ExitConfirm（全局退出确认窗，最高优先级）
L2 覆盖:  SettingsUI（opener = 暂停/开始面板）
          BaseUI（基地控制台）/ GameOverUI（结算）/ BuffUI（三选一，阻塞）
          IntroCinematic（开场过场，layer=35；播放中树暂停，Esc/任意键/点击 = 跳过）
          ReturnCinematic（返航过场，layer=35；播放中树暂停，Esc/任意键/点击 = 跳过；
                          结束后树保持暂停落 BaseUI，见 docs/RETURN_HOME_CINEMATIC.md §4）
L1 对局:  Gameplay(HUD) ⇄ PauseUI
          buff 滚动栏（HUD 内覆盖层，L 键展开/收起，不暂停对局；Esc = 收起栏）
L0 顶层:  StartPanel（主界面/大厅）
```

`scenes/tutorial.tscn` 为独立场景：自身即顶层，Esc = 退出教程回主界面（`tutorial.gd` 自处理，不进状态机）。

## 2. 状态机伪代码

所有平台的返回输入统一收敛到 `BackNavigator.go_back()`：

```
func go_back():
    match decide_back_action():            # 纯决策函数，无副作用（供测试覆盖全分支）
        CANCEL_EXIT:          # ExitConfirm 可见
            exit_confirm.cancel()          # 返回 = 取消退出；开始面板可见时焦点还给其主按钮
        SKIP_INTRO:           # 开场过场播放中（Main._intro != null）
            main.skip_intro()              # = 跳过过场直接进对局（任意键/点击由过场自身捕获）
        SKIP_RETURN:          # 返航过场播放中（Main._return != null，优先级同 SKIP_INTRO）
            main.skip_return()             # = 跳过过场直落基地 UI（树保持暂停；任意键/点击同上）
        CAPTURE_PASSTHROUGH:  # 设置改键捕获中
            pass                           # 不消费事件，让 SettingsUI 取消捕获
        CLOSE_SETTINGS:       # 设置页可见
            settings_ui.back()             # 返回 opener（暂停或开始面板）
        RESUME_BASE:          # 基地控制台可见
            base_ui.resume()               # = 继续出击
        IGNORE:               # Buff 三选一（必须做选择）/ 死亡→结算页出现前的中间态 / 其他暂停态
            （吞掉输入）
        TO_MAIN_MENU:         # 结算页可见
            paused = false + reset_run + reload_current_scene   # 回主界面（死亡时已删档）
        CLOSE_BUFF_PANEL:     # buff 滚动栏展开中（HUD 覆盖层，不暂停对局）
            hud.close_buff_panel()       # 返回 = 收起栏（优先于打开暂停）
        RESUME_GAME:          # 暂停面板可见
            pause_ui.close()
        CONFIRM_EXIT:         # 顶层（开始面板）
            exit_confirm.show_confirm(battle=false)
        OPEN_PAUSE:           # 以上皆非 = 战斗中（无覆盖、未暂停）
            pause_ui.open()                # 返回上一级 = 暂停
```

判定顺序即代码顺序：模态（确认窗）→ 过场跳过（开场/返航）→ 设置/基地/阻塞态/结算 → buff 栏 → 暂停 → 顶层 → 战斗。

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
    _on_exit_cleanup()           # hook：停止未播完音效（避免退出时播放实例泄漏）；网络断开等预留
    # 随后淡出黑屏 0.3s（过渡动画）→ get_tree().quit()
```

从开始面板退出：对局存档**保留**，下次启动可「继续对局」。

## 3. 键位映射表

| 平台 | 物理输入 | 映射到 | 处理 |
|---|---|---|---|
| PC | Esc | `ui_cancel`（引擎内置） | `BackNavigator._unhandled_input` |
| 手柄 | B / Circle（joy button 1） | `ui_cancel`（引擎内置默认映射） | 同上；A = `ui_accept` 确认，方向键/摇杆走 GUI 焦点导航（焦点样式已可见） |
| 手柄 | 左摇杆 | `move_*`（左摇杆移动） | GameState `_bind_joypad_defaults()` 启动时经 InputMap 运行时装配（`project.godot` 只存键盘，P0-1） |
| 手柄 | 右摇杆 | `aim_x`/`aim_y`（虚拟准星，`player.aim_point`） | 灵敏度/死区在设置页「手柄」分区可调（`joy_aim_speed`/`joy_deadzone`，profile 持久化） |
| 手柄 | A=冲刺 / RB=加速 / LB=微调 / X=母舰蓄力 / Y=返航 / L3=Buff 栏 / R3=放弃 / A=重开 | `dash`/`boost`/`fine_move`/`dock`/`homecoming`/`buff_panel`/`give_up`/`restart` | 同上运行时装配；B 键让位 `ui_cancel`（返回） |
| Android | 系统返回手势 | `NOTIFICATION_WM_GO_BACK_REQUEST` | `BackNavigator._notification` → `go_back()` |

确认窗内：Enter/手柄 A 触发焦点按钮（默认焦点在「取消」，安全侧）；Esc/手柄 B = 取消。

## 4. ExitConfirm 复用组件设计

- 挂载：`scenes/main.tscn`，CanvasLayer layer=40（高于一切 UI），`process_mode=Always`。
- API：
  - `show_confirm(battle: bool = false)` — normal/battle 双模式；battle 换 `UITheme.DANGER` 红字警告。
  - `cancel()` — 关闭（Esc 由 BackNavigator 路由至此）。
  - `_execute_exit_cleanup(battle)` — 退出前清理（测试可直接调用断言副作用）。
- 布局：`ChamferedPanel` + 标题 + 消息 + 「取消」（默认焦点）/「确认退出」（danger 色）；按钮样式走 `UITheme.make_button`；文案 `EXIT_*` 翻译键，监听 `locale_changed` 刷新。
- 复用方式：任何页面需要"确认后退出"只需 `show_confirm()`，清理/过渡/退出进程全部内部封装。

## 5. 平台差异化处理

- **PC**：无差异，Esc 全程可用（当前唯一实机验证平台）。
- **手柄**：依赖引擎内置 `ui_cancel` 默认映射（含 joy button 1）做返回；**移动/动作键/右摇杆瞄准由 `GameState._bind_joypad_defaults()` 在启动时经 InputMap 运行时装配**（`project.godot` 保持键盘单一事实源，P0-1：左摇杆移动、A/RB/LB/X/Y/L3/R3 动作键、右摇杆虚拟准星；B 键让位返回）。灵敏度与摇杆死区在设置页「手柄」分区可调。按钮 focus 样式与 hover 同款高亮，键盘/手柄导航可见。**尚未实机验证**（无导出流程，实机走查登记为发布前验证项）。
- **Android**：系统返回手势接入同一状态机；导出模板配置不在本项目范围内，标注为"映射就绪、未实机验证"。
- **教程场景**：见第 1 节，独立顶层自处理，不进状态机（避免跨场景耦合）。

## 6. 测试

`test/back_navigation_test.tscn`：`decide_back_action()` 全分支覆盖 + 集成路径（Esc→暂停→恢复、设置返回、顶层 Esc→确认窗→取消、战斗退出确认链、清理副作用）。开场过场的 SKIP_INTRO 分支与 Esc 跳过路径由 `test/intro_cinematic_test.tscn` 覆盖（设计见 docs/INTRO_CINEMATIC.md）；返航过场的 SKIP_RETURN 分支（决策 + 真实 Esc 注入 + 跳过后落基地 UI 且树保持暂停）由 `test/return_cinematic_test.tscn` 覆盖（设计见 docs/RETURN_HOME_CINEMATIC.md §4），back_navigation_test 另断言其决策分支。回归：`esc_navigation_test`（真实按键注入）与 `smoke_test` 必须全绿。
