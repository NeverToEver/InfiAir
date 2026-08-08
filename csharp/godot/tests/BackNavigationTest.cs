using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 返回/退出状态机测试：decide_back_action 全分支覆盖 + 真实 Esc 注入的集成路径。
/// 设计文档：docs/EXIT_FLOW.md。TO_MAIN_MENU 分支只断言决策不执行（会重载测试场景）。
/// </summary>
public partial class BackNavigationTest : Node
{
    private int _failures;

    private void Check(bool cond, string label)
    {
        if (cond)
        {
            GD.Print("[PASS] " + label);
        }
        else
        {
            _failures++;
            GD.PushError("[FAIL] " + label);
        }
    }

    private async Task PressEsc()
    {
        var ev = new InputEventKey();
        ev.Keycode = Key.Escape;
        ev.Pressed = true;
        Input.ParseInputEvent(ev);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var up = new InputEventKey();
        up.Keycode = Key.Escape;
        up.Pressed = false;
        Input.ParseInputEvent(up);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>右键 = 返回/取消（惯例）：模拟按下-释放</summary>
    private async Task PressRmb()
    {
        var ev = new InputEventMouseButton();
        ev.ButtonIndex = MouseButton.Right;
        ev.Pressed = true;
        Input.ParseInputEvent(ev);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var up = new InputEventMouseButton();
        up.ButtonIndex = MouseButton.Right;
        up.Pressed = false;
        Input.ParseInputEvent(up);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    public override void _Ready()
    {
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            var gs = GetNode<GameState>("/root/GameState");
            gs.DeleteSave();
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest(); // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var main = GetNode<Main>("Main");
            var nav = main.GetNode<BackNavigator>("BackNavigator");
            var pauseUi = main.GetNode<PauseUi>("PauseUI");
            var settingsUi = main.GetNode<SettingsUi>("SettingsUI");
            var baseUi = main.GetNode<BaseConsole>("BaseUI");
            var buffUi = main.GetNode<CanvasLayer>("BuffUI");
            var gameOverUi = main.GetNode<CanvasLayer>("GameOverUI");
            var exitConfirm = main.GetNode<ExitConfirm>("ExitConfirm");

            // ---------- 1. 对局层：Esc ⇄ 暂停（顶层退出确认已由 welcome_flow_test 覆盖） ----------
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(nav.DecideBackAction() == BackNavigator.BackAction.OPEN_PAUSE, "战斗中：决策=打开暂停");
            await PressEsc();
            Check(pauseUi.Visible && GetTree().Paused, "战斗中 Esc：打开暂停");
            Check(nav.DecideBackAction() == BackNavigator.BackAction.RESUME_GAME, "暂停中：决策=继续游戏");
            await PressEsc();
            Check(!pauseUi.Visible && !GetTree().Paused, "暂停中 Esc：恢复游戏");

            // ---------- 2b. 战斗中右键：打开暂停（惯例） ----------
            await PressRmb();
            Check(pauseUi.Visible && GetTree().Paused, "战斗中右键：打开暂停");
            await PressRmb();
            Check(!pauseUi.Visible && !GetTree().Paused, "暂停中右键：恢复游戏");

            // ---------- 3. 设置页：返回 opener + 改键捕获态放行 ----------
            pauseUi.Open();
            pauseUi.OpenSettings();
            Check(settingsUi.Visible && nav.DecideBackAction() == BackNavigator.BackAction.CLOSE_SETTINGS, "设置页：决策=返回 opener");
            settingsUi.StartCapture(new StringName("dash"));
            Check(nav.DecideBackAction() == BackNavigator.BackAction.CAPTURE_PASSTHROUGH, "改键捕获中：决策=放行");
            await PressEsc();
            Check(settingsUi.CapturingAction() == new StringName("") && settingsUi.Visible, "捕获中 Esc：取消捕获留在设置页");
            await PressEsc();
            Check(!settingsUi.Visible && pauseUi.Visible, "设置页 Esc：返回暂停面板");

            // ---------- 4. 战斗退出链：暂停 → 退出游戏 → battle 确认窗 ----------
            pauseUi.Quit();
            Check(exitConfirm.Visible && exitConfirm.BattleMode(), "暂停「退出游戏」：battle 模式确认窗");
            Check(exitConfirm.MsgLabel().Text == Tr("EXIT_BATTLE_MSG"), "battle 模式显示进度损失警告");
            await PressEsc();
            Check(!exitConfirm.Visible && pauseUi.Visible, "battle 确认窗 Esc：取消回到暂停");
            pauseUi.Close();

            // ---------- 5. 覆盖/阻塞态决策（不执行动作，仅断言决策） ----------
            main.PlayReturn();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(main.ReturnCinematic() != null && nav.DecideBackAction() == BackNavigator.BackAction.SKIP_RETURN, "返航过场：决策=跳过过场");
            // skip() 有 SKIP_GRACE（1.2s）误触宽限期，期内忽略跳过；先等宽限期结束（真实时间，树已暂停）
            await Coroutine.WaitSeconds(this, 1.3);
            main.SkipReturn();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(baseUi.Visible && nav.DecideBackAction() == BackNavigator.BackAction.RESUME_BASE, "返航过场跳过后：基地控制台决策=继续出击");
            baseUi.Resume(); // 恢复对局态，避免影响后续分支断言
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            buffUi.Visible = true;
            Check(nav.DecideBackAction() == BackNavigator.BackAction.IGNORE, "Buff 三选一：决策=忽略");
            buffUi.Visible = false;
            baseUi.Visible = true;
            Check(nav.DecideBackAction() == BackNavigator.BackAction.RESUME_BASE, "基地控制台：决策=继续出击");
            baseUi.Visible = false;
            main.SetGameOver(true);
            gameOverUi.Visible = true;
            Check(nav.DecideBackAction() == BackNavigator.BackAction.TO_MAIN_MENU, "结算页：决策=返回主界面");
            gameOverUi.Visible = false;
            main.SetGameOver(false);

            // ---------- 6. 退出前清理副作用（游客不存档，切真实用户验证） ----------
            if (!gs.UserExists("nav_user"))
            {
                gs.CreateUser("nav_user", "pass123");
            }
            gs.LoginUser("nav_user");
            gs.SaveRun(50.0, 10.0);
            exitConfirm.ExecuteExitCleanup(true);
            Check(!gs.HasSave(), "战斗中退出清理：对局存档删除（放弃进度）");
            gs.SaveRun(50.0, 10.0);
            exitConfirm.ExecuteExitCleanup(false);
            Check(gs.HasSave(), "主界面退出清理：对局存档保留（可继续对局）");
            gs.LogoutUser();

            // ---------- 7. Android 返回手势走同一状态机 ----------
            pauseUi.Open();
            nav.GoBack(); // C30：走公开路由（_notification 仅一行转发 go_back，语义等价）
            Check(!pauseUi.Visible && !GetTree().Paused, "Android 返回通知：与 Esc 同一路由");

            gs.DeleteSave();
            gs.SaveProfile();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"BACK NAVIGATION TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"BACK NAVIGATION TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
