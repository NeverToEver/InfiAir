using Godot;

namespace InfiAir;

/// <summary>
/// 全局返回/退出状态机（设计文档：docs/EXIT_FLOW.md）。
/// 所有平台的"返回"输入统一走 go_back()：PC Esc、鼠标右键与手柄 B 经引擎内置 ui_cancel，
/// Android 系统返回经 NOTIFICATION_WM_GO_BACK_REQUEST。
/// decide_back_action() 为纯决策函数（不执行副作用，供无头测试覆盖全分支）。
/// M5 全量迁移（2026-08-08 自 scripts/back_navigator.gd）。
/// </summary>
public partial class BackNavigator : Node
{
    /// <summary>返回动作枚举（值序 = 声明序；GDScript 测试经 back_actions() 字典访问）。</summary>
    public enum BackAction
    {
        /// <summary>退出确认窗可见：返回 = 取消退出</summary>
        CANCEL_EXIT,
        /// <summary>设置改键捕获中：不处理，让 settings 自己取消捕获</summary>
        CAPTURE_PASSTHROUGH,
        /// <summary>设置页 → 返回 opener（暂停/开始面板）</summary>
        CLOSE_SETTINGS,
        /// <summary>基地控制台 → 继续出击</summary>
        RESUME_BASE,
        /// <summary>开场过场播放中：返回 = 跳过过场</summary>
        SKIP_INTRO,
        /// <summary>返航过场播放中：返回 = 跳过过场</summary>
        SKIP_RETURN,
        /// <summary>buff 滚动栏展开中：返回 = 收起栏（优先于打开暂停）</summary>
        CLOSE_BUFF_PANEL,
        /// <summary>阻塞态（Buff 三选一/其他暂停态）：忽略</summary>
        IGNORE,
        /// <summary>结算页 → 返回主界面</summary>
        TO_MAIN_MENU,
        /// <summary>暂停中 → 继续游戏</summary>
        RESUME_GAME,
        /// <summary>战斗中 → 打开暂停（返回上一级）</summary>
        OPEN_PAUSE,
        // R12（M07 落地，2026-08-05）：CONFIRM_EXIT 已删——决策表无任何状态返回该动作，
        // 顶层退出确认由 welcome 场景自处理（EXIT_FLOW.md 同步）
    }

    private Main _main = null!; // U13：typed
    private Hud _hud = null!; // U13：typed
    private CanvasLayer _buffUi = null!;
    private PauseUi _pauseUi = null!; // U13：typed
    private SettingsUi _settingsUi = null!; // U13：typed
    private CanvasLayer _gameOverUi = null!;
    private BaseConsole _baseUi = null!; // U13：typed
    private ExitConfirm _exitConfirm = null!; // U13：typed

    public override void _Ready()
    {
        _main = GetParent<Main>();
        _hud = GetParent().GetNode<Hud>("HUD");
        _buffUi = GetParent().GetNode<CanvasLayer>("BuffUI");
        _pauseUi = GetParent().GetNode<PauseUi>("PauseUI");
        _settingsUi = GetParent().GetNode<SettingsUi>("SettingsUI");
        _gameOverUi = GetParent().GetNode<CanvasLayer>("GameOverUI");
        _baseUi = GetParent().GetNode<BaseConsole>("BaseUI");
        _exitConfirm = GetParent().GetNode<ExitConfirm>("ExitConfirm");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // 右键 = 返回/取消（惯例：许多游戏以右键作为默认返回触发器）。
        // 固定不参与改键（Esc/R 同类固定）；is_action_pressed 只认 ui_cancel（Esc/手柄 B）。
        // 2026-08-06 审计声明：本实现仅覆盖 main.tscn（BackNavigator 挂 Main 下）；
        // welcome 顶层（无返回目标）右键无效，Esc 走退出确认——文档未声明该例外，此处补注
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        {
            GoBack();
            // 改键捕获态放行（与 ui_cancel 同路由）：不消费事件，让 settings_ui 取消捕获；
            // 顺带统一走 _mark_handled 的 null 防御（3.12 实机退出报错同源）
            if (DecideBackAction() != BackAction.CAPTURE_PASSTHROUGH)
            {
                MarkHandled();
            }

            return;
        }

        if (@event.IsActionPressed("ui_cancel"))
        {
            GoBack();
        }
    }

    /// <summary>Android 系统返回手势：与 Esc/手柄 B 走同一状态机。</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationWMGoBackRequest)
        {
            GoBack();
        }
    }

    /// <summary>公开路由：所有返回输入统一入口（测试 C30 直接调用）。</summary>
    public void GoBack()
    {
        var action = DecideBackAction();
        switch (action)
        {
            case BackAction.CANCEL_EXIT:
                _exitConfirm.Cancel();
                // 焦点还给来源页面（确认窗打开时抢走了焦点）：暂停面板恢复按钮
                if (_pauseUi.Visible)
                {
                    _pauseUi.GrabPrimaryFocus();
                }

                MarkHandled();
                break;
            case BackAction.CAPTURE_PASSTHROUGH:
                break; // 不 set_input_as_handled，让 settings_ui 取消捕获
            case BackAction.CLOSE_SETTINGS:
                _settingsUi.Back();
                MarkHandled();
                break;
            case BackAction.RESUME_BASE:
                _baseUi.Resume();
                MarkHandled();
                break;
            case BackAction.SKIP_INTRO:
                _main.SkipIntro();
                MarkHandled();
                break;
            case BackAction.SKIP_RETURN:
                _main.SkipReturn();
                MarkHandled();
                break;
            case BackAction.CLOSE_BUFF_PANEL:
                _hud.CloseBuffPanel();
                MarkHandled();
                break;
            case BackAction.IGNORE:
                MarkHandled();
                break;
            case BackAction.TO_MAIN_MENU:
                // 账户系统（2026-08-04）：结算页回主菜单 = 回 welcome 主场景（welcome 重进时全量重置）
                GetTree().Paused = false;
                GameState.Instance.ResetRun();
                GameState.Instance.LogoutUser();
                GetTree().ChangeSceneToFile("res://scenes/welcome.tscn");
                MarkHandled();
                break;
            case BackAction.RESUME_GAME:
                _pauseUi.Close();
                MarkHandled();
                break;
            case BackAction.OPEN_PAUSE:
                _pauseUi.Open();
                MarkHandled();
                break;
        }
    }

    /// <summary>退出/场景重载途中节点可能已离树，get_viewport() 会返回 null（3.12 实机退出报错修复）。</summary>
    private void MarkHandled()
    {
        var vp = GetViewport();
        if (vp != null)
        {
            vp.SetInputAsHandled();
        }
    }

    /// <summary>纯决策：按页面优先级（模态 &gt; 覆盖 &gt; 对局 &gt; 顶层）决定返回动作。</summary>
    public BackAction DecideBackAction()
    {
        if (_exitConfirm.Visible)
        {
            return BackAction.CANCEL_EXIT;
        }

        if (_main.IsIntroPlaying())
        {
            return BackAction.SKIP_INTRO; // 过场播放中：Esc = 跳过过场（须在下方暂停 IGNORE 之前）
        }

        if (_main.IsReturnPlaying())
        {
            return BackAction.SKIP_RETURN; // 返航过场播放中：Esc = 跳过过场（优先级同 SKIP_INTRO）
        }

        if (_settingsUi.Visible)
        {
            if (_settingsUi.CapturingAction() != new StringName())
            {
                return BackAction.CAPTURE_PASSTHROUGH;
            }

            return BackAction.CLOSE_SETTINGS;
        }

        if (_baseUi.Visible)
        {
            return BackAction.RESUME_BASE;
        }

        if (_buffUi.Visible || (_main.IsGameOver() && !_gameOverUi.Visible))
        {
            return BackAction.IGNORE;
        }

        if (_gameOverUi.Visible)
        {
            return BackAction.TO_MAIN_MENU;
        }

        if (_hud.IsBuffPanelOpen())
        {
            return BackAction.CLOSE_BUFF_PANEL; // buff 滚动栏展开中：先收栏（不暂停对局的 HUD 覆盖层）
        }

        if (_pauseUi.Visible)
        {
            return BackAction.RESUME_GAME;
        }

        if (_main.IsHomecoming() || GetTree().Paused)
        {
            return BackAction.IGNORE; // 其他暂停态不响应
        }

        return BackAction.OPEN_PAUSE;
    }

    // ---------------- GDScript 鸭子调用兼容桥（M5 过渡，M7 删除） ----------------
    // 测试（back_navigation_test/buff_panel_test/intro_cinematic_test/return_cinematic_test/
    // autoplay_test）经实例调用 go_back/decide_back_action；原 nav.BackAction 枚举引用（C# 枚举
    // GDScript 不可访问）改为 back_actions() 字典访问（名称 → int，声明序）。

    public void go_back() => GoBack();

    public int decide_back_action() => (int)DecideBackAction();


    /// <summary>BackAction 枚举值字典（名称 → int，声明序；测试 act.XXX 改经此访问）。</summary>
    public static Godot.Collections.Dictionary BackActions() => new()
    {
        ["CANCEL_EXIT"] = (int)BackAction.CANCEL_EXIT,
        ["CAPTURE_PASSTHROUGH"] = (int)BackAction.CAPTURE_PASSTHROUGH,
        ["CLOSE_SETTINGS"] = (int)BackAction.CLOSE_SETTINGS,
        ["RESUME_BASE"] = (int)BackAction.RESUME_BASE,
        ["SKIP_INTRO"] = (int)BackAction.SKIP_INTRO,
        ["SKIP_RETURN"] = (int)BackAction.SKIP_RETURN,
        ["CLOSE_BUFF_PANEL"] = (int)BackAction.CLOSE_BUFF_PANEL,
        ["IGNORE"] = (int)BackAction.IGNORE,
        ["TO_MAIN_MENU"] = (int)BackAction.TO_MAIN_MENU,
        ["RESUME_GAME"] = (int)BackAction.RESUME_GAME,
        ["OPEN_PAUSE"] = (int)BackAction.OPEN_PAUSE,
    };
}
