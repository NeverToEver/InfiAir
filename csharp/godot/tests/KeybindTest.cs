using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 可改键系统测试：改键生效、冲突交换、恢复默认、profile 往返、捕获取消、非法拒绝。
/// 2026-08-06 审计：键位快照还原——reset/rebind 自动落盘（save_profile），
/// 开发者自定义键位被覆盖且无快照；备份/还原防本地键位被永久重置。
/// </summary>
public partial class KeybindTest : Node
{
    private int _failures;
    private GameState _gs = null!;
    private Godot.Collections.Dictionary _keyBackup = new();

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

    private static bool ActionHasKey(StringName action, Key keycode)
    {
        foreach (var ev in InputMap.ActionGetEvents(action))
        {
            if (ev is InputEventKey keyEvent && (keyEvent.Keycode == keycode || keyEvent.PhysicalKeycode == keycode))
            {
                return true;
            }
        }

        return false;
    }

    private void BackupKeys()
    {
        _keyBackup = _gs.KeyBindings.Duplicate(true);
    }

    private void RestoreKeys()
    {
        _gs.KeyBindings = _keyBackup.Duplicate(true);
        _gs.ApplyKeyBindings();
        _gs.SaveProfile();
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
            // L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
            var origHighScore = _gs.HighScore;
            _gs.HighScore = 0;
            _gs.SaveProfile();
            // 2026-08-06 审计：键位快照（reset/rebind 自动落盘，开发者自定义键位会被重置）
            BackupKeys();
            _gs.ResetKeyBindings();

            // 1. 改键生效（InputMap 实际变化）
            Check(_gs.RebindAction(new StringName("dash"), (int)Key.J), "改键 dash→J 返回 true");
            Check(ActionHasKey(new StringName("dash"), Key.J), "InputMap 中 dash 已是 J");
            Check(_gs.ActionKeysText(new StringName("dash")) == "J", "键名显示 J");

            // 2. 冲突交换：boost 也用 J → dash 失去 J
            _gs.RebindAction(new StringName("boost"), (int)Key.J);
            Check(ActionHasKey(new StringName("boost"), Key.J), "boost 占用 J");
            Check(!ActionHasKey(new StringName("dash"), Key.J), "冲突键从 dash 移除（允许交换）");

            // 3. 恢复默认
            _gs.ResetKeyBindings();
            Check(ActionHasKey(new StringName("boost"), Key.Shift), "恢复默认后 boost 回到 Shift");
            Check(!ActionHasKey(new StringName("boost"), Key.J), "恢复默认后 J 已清除");

            // 4. profile 持久化往返
            _gs.RebindAction(new StringName("dash"), (int)Key.K);
            _gs.KeyBindings.Clear();
            _gs.LoadProfile();
            var dashBindings = _gs.KeyBindings.GetValueOrDefault(new StringName("dash"), new Godot.Collections.Array()).AsGodotArray();
            Check(dashBindings.Count == 1 && dashBindings[0].AsInt32() == (int)Key.K, "profile 往返保留改键");
            _gs.ApplyKeyBindings();
            Check(ActionHasKey(new StringName("dash"), Key.K), "读档后 InputMap 已应用");

            // 5. 非法动作拒绝
            Check(!_gs.RebindAction(new StringName("restart"), (int)Key.J), "restart 固定不可改");
            Check(!_gs.RebindAction(new StringName("bogus_action"), (int)Key.J), "非法动作拒绝");

            // 6. 捕获态取消（设置 UI 捕获逻辑）
            var settings = new SettingsUi();
            AddChild(settings);
            settings.ShowSettings();
            settings.StartCapture(new StringName("dock"));
            Check(settings.CapturingAction() == new StringName("dock"), "进入捕获态");
            var esc = new InputEventKey { Keycode = Key.Escape, Pressed = true };
            Input.ParseInputEvent(esc);  // C30：走真实输入管线（对齐 esc_navigation_test 黑盒做法）
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var escUp = new InputEventKey { Keycode = Key.Escape, Pressed = false };
            Input.ParseInputEvent(escUp);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(settings.CapturingAction() == new StringName(), "Esc 取消捕获");
            Check(!ActionHasKey(new StringName("dock"), Key.Escape), "取消未写入绑定");

            // 7. 捕获态绑定成功
            settings.StartCapture(new StringName("dock"));
            var jEv = new InputEventKey { Keycode = Key.J, Pressed = true };
            Input.ParseInputEvent(jEv);  // C30：走真实输入管线
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var jUp = new InputEventKey { Keycode = Key.J, Pressed = false };
            Input.ParseInputEvent(jUp);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(ActionHasKey(new StringName("dock"), Key.J), "捕获按键完成绑定");

            // 8. G04：默认绑定冲突——未自定义动作的默认键被占用时解除占用（防同键双动作）
            _gs.ResetKeyBindings();
            _gs.RebindAction(new StringName("dock"), (int)Key.Space);  // dash 默认 Space 未自定义
            Check(ActionHasKey(new StringName("dock"), Key.Space), "dock 占用 Space");
            Check(!ActionHasKey(new StringName("dash"), Key.Space), "G04：默认键被占用后 dash 解除占用（空绑定覆盖默认）");
            _gs.ResetKeyBindings();
            Check(ActionHasKey(new StringName("dash"), Key.Space), "恢复默认后 dash 回到 Space");

            // 收尾：恢复默认并落盘，避免污染其他测试/本机 profile
            _gs.ResetKeyBindings();

            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            _gs.HighScore = origHighScore;
            _gs.SaveProfile();
            // 2026-08-06 审计：还原用户自定义键位（reset_key_bindings 已把默认键位落盘）
            RestoreKeys();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"KEYBIND TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"KEYBIND TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
