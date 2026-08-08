using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 触屏虚拟输入层测试（mobile touch，docs/archive/2026-08-07-deferred-restart-plan.md §3.1.5）：
/// 场景1 挂载与默认禁用（桌面零回归前提）→ 设置开关联动 Main；
/// 场景2 左摇杆：按下-拖动注入 move_* action（方向/幅度）→ 释放全清；
/// 场景3 右摇杆：拖动注入 aim_* → 触屏模式下 player 瞄准随右摇杆上移（无鼠标基准）；
/// 场景4 虚拟按钮：dash/parry/boost/fine_move 按下注入 press、释放 release；
/// 场景5 禁用零回归：禁用后注入触摸不再产生任何 action 状态（键鼠/手柄不受影响）。
/// </summary>
public partial class VirtualControlsTest : Node
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

    /// <summary>真实时间等待（忽略 time_scale，等价 GDScript create_timer(sec, true, false, true)）</summary>
    private async Task WaitReal(double sec)
    {
        await ToSignal(GetTree().CreateTimer(sec, true, false, true), SceneTreeTimer.SignalName.Timeout);
    }

    private async Task Touch(VirtualControls vc, int idx, bool pressed, Vector2 pos)
    {
        vc.SimulateTouch(idx, pressed, pos);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Drag(VirtualControls vc, int idx, Vector2 pos)
    {
        vc.SimulateDrag(idx, pos);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
            var vcObj = gs.VirtualControls as VirtualControls;
            Check(vcObj != null, "场景1: Main 挂载 VirtualControls 并经 GameState 转发");
            var vc = vcObj!;

            // 默认禁用（桌面零回归前提：touch_controls 默认 false）
            Check(!vc.IsEnabled(), "场景1: 默认禁用（touch_controls=false）");
            // 设置开关联动 Main（GameState.touch_controls_changed → vc.set_enabled）
            gs.SetTouchControls(true);
            Check(vc.IsEnabled(), "场景1: 设置开关联动启用（touch_controls=true）");
            gs.SetTouchControls(false);
            Check(!vc.IsEnabled(), "场景1: 开关关闭联动禁用");
            vc.SetEnabled(true);

            // ---- 场景2：左摇杆 → move_* ----
            await Touch(vc, 0, true, VirtualControls.move_center());
            Check(!Input.IsActionPressed("move_right"), "场景2: 中心按下（死区内）不注入移动");
            await Drag(vc, 0, VirtualControls.move_center() + new Vector2(VirtualControls.move_radius(), 0.0f));
            Check(vc.MoveVec().DistanceTo(new Vector2(1.0f, 0.0f)) < 0.05f, "场景2: 右推 → move_vec=(1,0)");
            Check(Input.IsActionPressed("move_right"), "场景2: move_right action 注入按下");
            Check(Input.GetActionStrength("move_right") > 0.9f, "场景2: move_right 强度 ≈1");
            Check(!Input.IsActionPressed("move_left"), "场景2: 反向 action 不注入");
            await Drag(vc, 0, VirtualControls.move_center() + new Vector2(0.0f, -VirtualControls.move_radius()));
            Check(vc.MoveVec().DistanceTo(new Vector2(0.0f, -1.0f)) < 0.05f, "场景2: 上推 → move_vec=(0,-1)");
            Check(Input.IsActionPressed("move_up"), "场景2: move_up action 注入按下");
            await Touch(vc, 0, false, Vector2.Zero);
            Check(vc.MoveVec() == Vector2.Zero, "场景2: 释放 → move_vec 归零");
            Check(!Input.IsActionPressed("move_right") && !Input.IsActionPressed("move_up"), "场景2: 释放 → 移动 action 全清");

            // ---- 场景3：右摇杆 → aim_* + 触屏瞄准行为 ----
            var aimBefore = player.AimPoint();
            await Touch(vc, 1, true, VirtualControls.aim_center());
            await Drag(vc, 1, VirtualControls.aim_center() + new Vector2(0.0f, -VirtualControls.aim_radius()));
            Check(vc.AimVec().DistanceTo(new Vector2(0.0f, -1.0f)) < 0.05f, "场景3: 上推 → aim_vec=(0,-1)");
            Check(Input.IsActionPressed("aim_up"), "场景3: aim_up action 注入按下");
            await WaitReal(0.5); // 增量驱动：瞄准点上移
            var aimAfter = player.AimPoint();
            Check(aimAfter.Y < aimBefore.Y, "场景3: 触屏模式瞄准随右摇杆上移（无鼠标基准）");
            await Touch(vc, 1, false, Vector2.Zero);
            Check(vc.AimVec() == Vector2.Zero, "场景3: 释放 → aim_vec 归零");
            Check(!Input.IsActionPressed("aim_up"), "场景3: 释放 → aim action 全清");

            // ---- 场景4：虚拟按钮 ----
            var dashBtn = VirtualControls.buttons()[new StringName("dash")].AsGodotDictionary();
            await Touch(vc, 2, true, dashBtn["center"].AsVector2());
            Check(Input.IsActionPressed("dash"), "场景4: dash 按钮按下 → action press");
            await Touch(vc, 2, false, Vector2.Zero);
            Check(!Input.IsActionPressed("dash"), "场景4: dash 释放 → action release");
            var parryBtn = VirtualControls.buttons()[new StringName("parry")].AsGodotDictionary();
            await Touch(vc, 3, true, parryBtn["center"].AsVector2());
            Check(Input.IsActionPressed("parry"), "场景4: parry 按钮按下 → action press");
            await Touch(vc, 3, false, Vector2.Zero);
            var boostBtn = VirtualControls.buttons()[new StringName("boost")].AsGodotDictionary();
            await Touch(vc, 4, true, boostBtn["center"].AsVector2());
            Check(Input.IsActionPressed("boost"), "场景4: boost 按钮按下 → action press");
            await Touch(vc, 4, false, Vector2.Zero);
            var fineBtn = VirtualControls.buttons()[new StringName("fine_move")].AsGodotDictionary();
            await Touch(vc, 5, true, fineBtn["center"].AsVector2());
            Check(Input.IsActionPressed("fine_move"), "场景4: fine_move 按钮按下 → action press");
            await Touch(vc, 5, false, Vector2.Zero);
            Check(
                !Input.IsActionPressed("parry") && !Input.IsActionPressed("boost") && !Input.IsActionPressed("fine_move"),
                "场景4: 按钮全部释放后 action 全清"
            );

            // ---- 场景5：禁用零回归 ----
            vc.SetEnabled(false);
            await Touch(vc, 6, true, VirtualControls.move_center() + new Vector2(VirtualControls.move_radius(), 0.0f));
            Check(!Input.IsActionPressed("move_right"), "场景5: 禁用后触摸注入不产生 move 状态");
            await Touch(vc, 6, false, Vector2.Zero);
            var dash2 = VirtualControls.buttons()[new StringName("dash")].AsGodotDictionary();
            await Touch(vc, 7, true, dash2["center"].AsVector2());
            Check(!Input.IsActionPressed("dash"), "场景5: 禁用后触摸注入不产生按钮状态");
            await Touch(vc, 7, false, Vector2.Zero);
            // 恢复默认（清理持久化副作用）
            gs.SetTouchControls(false);
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"VIRTUAL CONTROLS TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"VIRTUAL CONTROLS TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
