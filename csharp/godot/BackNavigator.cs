using Godot;

namespace InfiAir;

/// <summary>
/// 全局返回/退出状态机。
/// 所有平台的"返回"输入统一走 go_back()：PC Esc 与手柄 B 经引擎内置 ui_cancel，
/// 鼠标右键为独立固定手势（非 ui_cancel），Android 系统返回经 NOTIFICATION_WM_GO_BACK_REQUEST。
/// decide_back_action() 为纯决策函数（不执行副作用，全分支可无头驱动）。
/// </summary>
public partial class BackNavigator : Node
{
    /// <summary>返回动作枚举（值序 = 声明序）。</summary>
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
        CLOSE_AUG_PANEL,
        /// <summary>天赋面板打开中：返回 = 关闭面板（模态暂停态，优先于暂停路由）</summary>
        CLOSE_TALENT,
        /// <summary>阻塞态（其他暂停态）：忽略</summary>
        IGNORE,
        /// <summary>结算页 → 返回主界面</summary>
        TO_MAIN_MENU,
        /// <summary>暂停中 → 继续游戏</summary>
        RESUME_GAME,
        /// <summary>战斗中 → 打开暂停（返回上一级）</summary>
        OPEN_PAUSE,
        // R12（M07 落地，2026-08-05）：CONFIRM_EXIT 已删——决策表无任何状态返回该动作，
        // 顶层退出确认由入口场景自处理
    }

    private Main _main = null!; // U13：typed
    private Hud _hud = null!; // U13：typed
    private TalentPanel _talentUi = null!;
    private PauseUi _pauseUi = null!; // U13：typed
    private SettingsUi _settingsUi = null!; // U13：typed
    private GameOverUi _gameOverUi = null!; // U13：typed
    private BaseConsole _baseUi = null!; // U13：typed
    private ExitConfirm _exitConfirm = null!; // U13：typed

    public override void _Ready()
    {
        _main = GetParent<Main>();
        _hud = GetParent().GetNode<Hud>("HUD");
        _talentUi = GetParent().GetNode<TalentPanel>("TalentUI");
        _pauseUi = GetParent().GetNode<PauseUi>("PauseUI");
        _settingsUi = GetParent().GetNode<SettingsUi>("SettingsUI");
        _gameOverUi = GetParent().GetNode<GameOverUi>("GameOverUI");
        _baseUi = GetParent().GetNode<BaseConsole>("BaseUI");
        _exitConfirm = GetParent().GetNode<ExitConfirm>("ExitConfirm");
    }

    public override void _Input(InputEvent @event)
    {
        // 右键 = 返回/取消（惯例：许多游戏以右键作为默认返回触发器）。挂 _Input（先于 GUI 相位）——
        // 面板/按钮 MouseFilter=STOP 会吞掉落在其上的右键，_UnhandledInput 永远收不到
        // （点在面板内右键返回失灵）。固定手势不参与改键（Esc/R 同类固定）。
        // 改键捕获态：DecideBackAction = CAPTURE_PASSTHROUGH → GoBack 无副作用且不消费事件，
        // 由 SettingsUi._Input 取消捕获（两处 _Input 无论先后序均幂等）。
        // 本实现仅覆盖 main.tscn（BackNavigator 挂 Main 下），其余场景各自处理返回
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        {
            GoBack();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // ui_cancel（Esc/手柄 B）：键盘/手柄事件不经 GUI 相位、无面板吞事件路径，留 _UnhandledInput
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

    /// <summary>公开路由：所有返回输入统一入口。</summary>
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
            case BackAction.CLOSE_AUG_PANEL:
                _hud.CloseAugmentPanel();
                MarkHandled();
                break;
            case BackAction.CLOSE_TALENT:
                _talentUi.Close();
                MarkHandled();
                break;
            case BackAction.IGNORE:
                MarkHandled();
                break;
            case BackAction.TO_MAIN_MENU:
                // 结算页回标题屏（title.tscn）：ExitToTitle 内含 ResetRun（全新一局）
                GameState.Instance.ExitToTitle();
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

        if (_talentUi.Visible)
        {
            return BackAction.CLOSE_TALENT; // 天赋面板打开中：Esc = 关闭面板（对局随之恢复）
        }

        if (_main.IsGameOver() && !_gameOverUi.Visible)
        {
            return BackAction.IGNORE;
        }

        if (_gameOverUi.Visible)
        {
            return BackAction.TO_MAIN_MENU;
        }

        if (_hud.IsAugmentPanelOpen())
        {
            return BackAction.CLOSE_AUG_PANEL; // buff 滚动栏展开中：先收栏（不暂停对局的 HUD 覆盖层）
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
}
