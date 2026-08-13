# UI, Text & Navigation
## Overview
i18n、UITheme.cs 样式、暂停页、返回/退出导航、BGM 生命周期; 适用于全部 UI 页面/浮层与可见文本。
## Rules
- 可见文本一律 Tr("UPPER_SNAKE_CASE"); 新 key 填 data/translations.csv zh+en 两列并重导入, 动态文本用 %d/%s 占位 key。Locale 仅经 GameState.SetLocale("zh"/"en") 切换, UI 于 LocaleChanged 刷新。
- 样式统一走 csharp/godot/UITheme.cs: MakeLabel/MakeButton/MakeToggleButton/MakeSectionHeader/MakePageShell/AnimateModalOpen/AddButtonMotion/MakeBuffSocket/MakeBuffTile; 组件 ChamferedPanel/SegmentedBar/BuffIcons/StartBackdrop。新页面用 MakePageShell, ≤1 主按钮。
- 全局技能 game-ui-ux (跨引擎 UI/UX 指引: 响应式布局/安全区/键鼠手柄焦点/屏幕栈/事件驱动 HUD), 互补 godot-ui-control; 设计/重构 HUD/菜单/浮层时使用, 样式以 UITheme.cs 为准。
- 暂停类 UI (buff/暂停/结算) 需 ProcessMode = ProcessModeEnum.Always + GetTree().Paused。
- 返回/退出集中由 BackNavigator 管理; 页面不消费 ui_cancel (设置按键捕获除外), 右键 = 固定返回/取消, 与 Esc 同路由且不可重绑定; 新页面层级在 DecideBackAction() 登记 + 同步 docs/EXIT_FLOW.md。
- BGM 仅设 stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward, 不设 loop begin/end, 禁在 _ExitTree() 内 stop (泄漏播放实例)。
