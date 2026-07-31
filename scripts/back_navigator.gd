extends Node
## 全局返回/退出状态机（设计文档：docs/EXIT_FLOW.md）。
## 所有平台的"返回"输入统一走 go_back()：PC Esc 与手柄 B 经引擎内置 ui_cancel，
## Android 系统返回经 NOTIFICATION_WM_GO_BACK_REQUEST。
## decide_back_action() 为纯决策函数（不执行副作用，供无头测试覆盖全分支）。

enum BackAction {
	CANCEL_EXIT,  # 退出确认窗可见：返回 = 取消退出
	CAPTURE_PASSTHROUGH,  # 设置改键捕获中：不处理，让 settings 自己取消捕获
	CLOSE_SETTINGS,  # 设置页 → 返回 opener（暂停/开始面板）
	RESUME_BASE,  # 基地控制台 → 继续出击
	SKIP_INTRO,  # 开场过场播放中：返回 = 跳过过场
	SKIP_RETURN,  # 返航过场播放中：返回 = 跳过过场
	CLOSE_BUFF_PANEL,  # buff 滚动栏展开中：返回 = 收起栏（优先于打开暂停）
	IGNORE,  # 阻塞态（Buff 三选一/其他暂停态）：忽略
	TO_MAIN_MENU,  # 结算页 → 返回主界面
	RESUME_GAME,  # 暂停中 → 继续游戏
	OPEN_PAUSE,  # 战斗中 → 打开暂停（返回上一级）
	CONFIRM_EXIT,  # 顶层（开始面板/欢迎页）→ 弹出全局退出确认
}

@onready var _main: Node2D = get_parent()
@onready var _hud: CanvasLayer = get_parent().get_node("HUD")
@onready var _buff_ui: CanvasLayer = get_parent().get_node("BuffUI")
@onready var _pause_ui: CanvasLayer = get_parent().get_node("PauseUI")
@onready var _settings_ui: CanvasLayer = get_parent().get_node("SettingsUI")
@onready var _game_over_ui: CanvasLayer = get_parent().get_node("GameOverUI")
@onready var _base_ui: CanvasLayer = get_parent().get_node("BaseUI")
@onready var _start_panel: CanvasLayer = get_parent().get_node("StartPanel")
@onready var _welcome: CanvasLayer = get_parent().get_node("WelcomeScreen")
@onready var _exit_confirm: CanvasLayer = get_parent().get_node("ExitConfirm")


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		go_back()


## Android 系统返回手势：与 Esc/手柄 B 走同一状态机
func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_GO_BACK_REQUEST:
		go_back()


func go_back() -> void:
	var action := decide_back_action()
	match action:
		BackAction.CANCEL_EXIT:
			_exit_confirm.cancel()
			# 焦点还给开始面板主按钮（确认窗打开时抢走了焦点）
			if _start_panel.visible:
				_start_panel.grab_primary_focus()
			_mark_handled()
		BackAction.CAPTURE_PASSTHROUGH:
			pass  # 不 set_input_as_handled，让 settings_ui 取消捕获
		BackAction.CLOSE_SETTINGS:
			_settings_ui._on_back_pressed()
			_mark_handled()
		BackAction.RESUME_BASE:
			_base_ui._on_resume_pressed()
			_mark_handled()
		BackAction.SKIP_INTRO:
			_main._skip_intro()
			_mark_handled()
		BackAction.SKIP_RETURN:
			_main._skip_return()
			_mark_handled()
		BackAction.CLOSE_BUFF_PANEL:
			_hud.close_buff_panel()
			_mark_handled()
		BackAction.IGNORE:
			_mark_handled()
		BackAction.TO_MAIN_MENU:
			get_tree().paused = false
			GameState.reset_run()
			get_tree().reload_current_scene()
			_mark_handled()
		BackAction.RESUME_GAME:
			_pause_ui.close()
			_mark_handled()
		BackAction.OPEN_PAUSE:
			_pause_ui.open()
			_mark_handled()
		BackAction.CONFIRM_EXIT:
			_exit_confirm.show_confirm(false)
			_mark_handled()


## 退出/场景重载途中节点可能已离树，get_viewport() 会返回 null（3.12 实机退出报错修复）
func _mark_handled() -> void:
	var vp := get_viewport()
	if vp != null:
		vp.set_input_as_handled()


## 纯决策：按页面优先级（模态 > 覆盖 > 对局 > 顶层）决定返回动作
func decide_back_action() -> BackAction:
	if _exit_confirm.visible:
		return BackAction.CANCEL_EXIT
	if _main.is_intro_playing():
		return BackAction.SKIP_INTRO  # 过场播放中：Esc = 跳过过场（须在下方暂停 IGNORE 之前）
	if _main.is_return_playing():
		return BackAction.SKIP_RETURN  # 返航过场播放中：Esc = 跳过过场（优先级同 SKIP_INTRO）
	if _settings_ui.visible:
		if _settings_ui.capturing_action() != &"":
			return BackAction.CAPTURE_PASSTHROUGH
		return BackAction.CLOSE_SETTINGS
	if _base_ui.visible:
		return BackAction.RESUME_BASE
	if _buff_ui.visible or (_main.is_game_over() and not _game_over_ui.visible):
		return BackAction.IGNORE
	if _game_over_ui.visible:
		return BackAction.TO_MAIN_MENU
	if _hud.is_buff_panel_open():
		return BackAction.CLOSE_BUFF_PANEL  # buff 滚动栏展开中：先收栏（不暂停对局的 HUD 覆盖层）
	if _pause_ui.visible:
		return BackAction.RESUME_GAME
	if _start_panel.visible or _welcome.visible:
		return BackAction.CONFIRM_EXIT
	if _main.is_homecoming() or get_tree().paused:
		return BackAction.IGNORE  # 其他暂停态不响应
	return BackAction.OPEN_PAUSE
