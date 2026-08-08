using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// Esc 导航真实输入回归（M7c 迁移自 test/esc_navigation_test.gd）：注入真实按键事件走完整输入管线
/// （区别于 smoke 的直接调用）。核心覆盖：树暂停后 Esc 路由必须仍可达（process_mode=Always 的 pause_ui），
/// 以及「暂停 → 设置 → 改键 → Esc 逐级返回 → 恢复游戏」全链路（用户报告的卡死路径）。
/// </summary>
public partial class EscNavigationTest : Node
{
    private int _failures;
    private int _frames;
    // 2026-08-06 审计：键位快照还原——reset/rebind 自动落盘（save_profile），
    // 开发者自定义键位被覆盖且无快照；备份/还原防本地键位被永久重置
    private Godot.Collections.Dictionary _keyBackup = new();
    private GameState? _gs;

    public override void _Process(double delta)
    {
        _frames += 1;
        if (_frames > 600)  // 看门狗：卡死时带状态退出
        {
            GD.PushError($"[WATCHDOG] stuck, paused={(GetTree().Paused ? "true" : "false")}");
            TestExit.Quit(2);
        }
    }

    private void Check(bool cond, string msg)
    {
        if (cond)
        {
            GD.Print("[PASS] " + msg);
        }
        else
        {
            _failures += 1;
            GD.PushError("[FAIL] " + msg);
        }
    }

    private void BackupKeys()
    {
        _keyBackup = _gs!.KeyBindings.Duplicate(true);  // Godot 4.6.2：Duplicate 返回 Dictionary
    }

    private void RestoreKeys()
    {
        _gs!.KeyBindings = _keyBackup.Duplicate(true);
        _gs.ApplyKeyBindings();
        _gs.SaveProfile();
    }

    private async Task PressKey(Key keycode)
    {
        var ev = new InputEventKey { Keycode = keycode, Pressed = true };
        Input.ParseInputEvent(ev);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var up = new InputEventKey { Keycode = keycode, Pressed = false };
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
            _gs = GetNode<GameState>("/root/GameState");
            _gs.DeleteSave();
            // 2026-08-06 审计：键位快照（结尾 reset_key_bindings 自动落盘，防开发者键位被重置）
            BackupKeys();
            var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
            AddChild(main);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var settingsUi = main.GetNode<SettingsUi>("SettingsUI");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var pauseUi = main.GetNode<PauseUi>("PauseUI");

            // 1. Esc → 暂停（未暂停时注入，main/pause 均可收）
            await PressKey(Key.Escape);
            Check(pauseUi.Visible && GetTree().Paused, "Esc 打开暂停面板");

            // 2. 暂停中 Esc → 恢复（核心回归：暂停后 INHERIT 节点收不到输入，路由须在 Always 节点）
            await PressKey(Key.Escape);
            Check(!pauseUi.Visible && !GetTree().Paused, "暂停中 Esc 恢复游戏");

            // 3. 暂停 → 设置 → 改键 dash=T → Esc 逐级返回（用户报告的卡死路径）
            await PressKey(Key.Escape);
            pauseUi.OpenSettings();
            Check(settingsUi.Visible, "设置面板打开");
            settingsUi.StartCapture("dash");
            await PressKey(Key.T);
            Check(settingsUi.CapturingAction() == new StringName(), "绑定后退出捕获态");
            Check(_gs.ActionKeysText("dash") == "T", "dash 已绑定 T");
            await PressKey(Key.Escape);
            Check(!settingsUi.Visible, "Esc 后设置面板关闭");
            Check(pauseUi.Visible, "Esc 后回到暂停面板");
            await PressKey(Key.Escape);
            Check(!pauseUi.Visible && !GetTree().Paused, "再 Esc 恢复游戏");

            // 4. 捕获态 Esc 取消 → 再 Esc 逐级退出
            await PressKey(Key.Escape);
            pauseUi.OpenSettings();
            settingsUi.StartCapture("dash");
            await PressKey(Key.Escape);  // 取消捕获
            Check(settingsUi.Visible && settingsUi.CapturingAction() == new StringName(), "捕获中 Esc 取消但留在设置页");
            await PressKey(Key.Escape);  // 退出设置 → 回暂停
            Check(!settingsUi.Visible && pauseUi.Visible, "取消后再 Esc 回暂停面板");
            await PressKey(Key.Escape);  // 恢复游戏
            Check(!GetTree().Paused, "最终 Esc 恢复游戏");

            _gs.ResetKeyBindings();
            _gs.DeleteSave();
            // 2026-08-06 审计：还原用户自定义键位（reset_key_bindings 已把默认键位落盘）
            RestoreKeys();
        }
        catch (System.Exception e)
        {
            _failures += 1;
            GD.PushError($"ESC NAVIGATION TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"[DONE] failures={_failures}");
            TestExit.Quit(_failures > 0 ? 1 : 0);
        }
    }
}
