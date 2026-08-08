using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 遭遇流程契约测试（2026-08-07，登记待办补齐：AUDIT_VAULT R 系列 #9 + 2026-08-06 批次 #7）：
/// T3a 自动触发短窗口契约：smoke 式测试窗口（数秒）内自动遭遇保持惰性——interval
///   （精英 45s / 编队 40s）≫ 窗口，断言无事件启动 + 计时未归零 + interval 配置下界
///   契约锚点（防未来调小到测试窗口内污染断言）。
/// T3b 独立断言（原由 main 流程回归间接覆盖）：
///   L-d 遭遇事件进行中禁止蓄力（can_charge 事件互斥，防母舰清场全额领奖挂机收益）；
///   L-b 死亡路径清理召唤小窗（give_up 与 dock 蓄力同帧完成时小窗不永驻）。
/// </summary>
public partial class EncounterFlowContractTest : Node
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

    /// <summary>真实时间等待（不受 time_scale 影响）</summary>
    private async Task WaitReal(double seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds, true, false, true), SceneTreeTimer.SignalName.Timeout);
    }

    /// <summary>注入 action 按住 seconds 秒再释放（真实输入管线：InputEventAction → Input.parse_input_event）</summary>
    private async Task HoldAction(StringName action, double seconds)
    {
        var down = new InputEventAction();
        down.Action = action;
        down.Pressed = true;
        Input.ParseInputEvent(down);
        await WaitReal(seconds);
        var up = new InputEventAction();
        up.Action = action;
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
            gs.ResetRun();
            gs.SetDifficulty("medium");
            gs.LoginGuest();
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            AddChild(mainScene.Instantiate());
            var main = GetNode<Main>("Main");
            var player = main.Player();
            player.SetAutoFire(false);
            player.SetInvincible(999.0f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var events = gs.Events;

            // ---- T3a：自动遭遇短窗口契约（spawner 保持处理中，自动触发路径活跃） ----
            Check(events.ActiveId(events.GROUP_ENCOUNTER) == new StringName(""), "T3a: 开局无遭遇事件");
            Check(events.EncounterTimerRemaining("elite_turret") > 0.0f, "T3a: 开局精英计时未归零");
            Check(events.EncounterTimerRemaining("formation_strike") > 0.0f, "T3a: 开局编队计时未归零");
            await WaitReal(3.0);
            Check(events.ActiveId(events.GROUP_ENCOUNTER) == new StringName(""), "T3a: 3s 短窗口内无自动遭遇触发（interval ≫ 窗口）");
            Check(events.EncounterTimerRemaining("elite_turret") > 0.0f, "T3a: 3s 后精英计时仍 >0（未归零触发）");
            Check(events.EncounterTimerRemaining("formation_strike") > 0.0f, "T3a: 3s 后编队计时仍 >0（未归零触发）");
            // 契约锚点：interval 配置下界（防未来把自动触发窗口调进测试运行时长内）
            Check((float)gs.Cfg("elite_turret_event.trigger_interval", 45.0).AsDouble() >= 20.0f, "T3a: 精英 trigger_interval >= 20s（配置契约锚点）");
            Check((float)gs.Cfg("formation_strike_event.trigger_interval", 40.0).AsDouble() >= 20.0f, "T3a: 编队 trigger_interval >= 20s（配置契约锚点）");

            // ---- T3b / L-d：遭遇事件进行中禁止蓄力 ----
            Check(events.ForceTrigger("elite_turret"), "L-d: 强制触发精英事件成功");
            Check(events.ActiveId(events.GROUP_ENCOUNTER) != new StringName(""), "L-d: 遭遇事件进行中");
            await HoldAction("dock", 0.4);
            Check(!main.Charging(), "L-d: 事件进行中蓄力被拒（can_charge 事件互斥）");
            events.EndActive(events.GROUP_ENCOUNTER);
            await WaitReal(0.3); // 等事件清理/撤离

            // ---- T3b / L-b：死亡路径清理召唤小窗 ----
            main.SummonMothership();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(main.SummonWindow() != null, "L-b: 召唤小窗已打开");
            gs.EmitSignal(GameState.SignalName.PlayerDied);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(main.SummonWindow() == null, "L-b: 死亡路径清理召唤小窗（不永驻）");
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"ENCOUNTER FLOW CONTRACT TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"ENCOUNTER FLOW CONTRACT TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
