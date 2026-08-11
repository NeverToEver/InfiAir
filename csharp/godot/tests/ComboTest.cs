using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 击杀连击计分测试（2026-08-11，docs/archive/2026-08-11-score-combo-buff-pity-plan.md §3.1）：
/// 首杀乘区 1.0、连击推进与乘区入账、乘区封顶、Boss 击杀不计连击、
/// 受击断连、重开清零、窗口超时断连。
/// </summary>
public partial class ComboTest : Node
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

    public override void _Ready()
    {
        // 禁止裸 async void 生命周期：拆私有 async Task + fire-and-forget
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        var gs = GetNode<GameState>("/root/GameState");
        try
        {
            // 清理持久化状态，保证测试确定性
            gs.DeleteSave();
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            AddChild(mainScene.Instantiate());
            var main = GetNode<Main>("Main");
            var player = main.Player();
            player.SetAutoFire(false); // 禁用自动开火，避免误伤与意外得分里程碑
            player.SetInvincible(999.0f);
            var spawner = main.GetNode<Spawner>("Spawner");
            spawner.SetProcess(false); // 停掉自动刷怪/Boss 调度，保证确定性
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (var child in main.GetChildren())
            {
                if (child is Enemy || child is Bullet)
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            gs.ResetRun();
            // 里程碑阈值推远：AddBossKill 高分跨档会弹 Buff 面板暂停树，干扰后续断言
            gs.SetMilestoneOverride(999999);
            Check(gs.ScoreMultiplier() == 2, "中难度分数倍率 ×2");

            // ================= 用例 1：首杀 combo=1、乘区 1.0、难度倍率照常 =================
            gs.Score = 0;
            gs.AddKillScore(100);
            Check(gs.Combo == 1, "用例1：首杀连击 = 1");
            Check(Mathf.IsEqualApprox(gs.ComboMultiplier(), 1.0), "用例1：首杀乘区 1.0（不放大）");
            Check(gs.Score == 200, "用例1：击杀分 = 100 × 1.0 × 难度 2");

            // ================= 用例 2：连击推进与乘区入账 =================
            gs.AddKillScore(100); // combo 2 → ×1.1
            Check(gs.Combo == 2, "用例2：连击推进到 2");
            Check(Mathf.IsEqualApprox(gs.ComboMultiplier(), 1.1), "用例2：二连击乘区 ×1.1");
            Check(gs.Score == 200 + (int)(100 * 1.1) * 2, "用例2：乘区放大后入账（110 × 2 = 220）");

            // ================= 用例 3：乘区封顶 ×2.0 =================
            for (int i = 0; i < 20; i++)
            {
                gs.AddKillScore(1);
            }

            Check(Mathf.IsEqualApprox(gs.ComboMultiplier(), 2.0), "用例3：连击乘区封顶 ×2.0");
            Check(gs.Combo == 22, "用例3：连击计数继续增长（乘区封顶不断链）");

            // ================= 用例 4：Boss 击杀不计连击（不加也不断） =================
            var beforeBoss = gs.Combo;
            gs.AddBossKill();
            Check(gs.Combo == beforeBoss, "用例4：Boss 击杀不计连击");

            // ================= 用例 5：受击断连（与 DDA 同源信号） =================
            gs.AddKillScore(100);
            Check(gs.Combo == beforeBoss + 1, "用例5：受击前连击递增");
            gs.EmitSignal(GameState.SignalName.PlayerDamaged, 10.0f, Vector2.Zero);
            Check(gs.Combo == 0, "用例5：受击断连归零");
            Check(gs.ComboTimeLeft() == 0.0, "用例5：断连后窗口计时归零");

            // ================= 用例 6：重开（ResetRun）清零 =================
            gs.AddKillScore(100);
            Check(gs.Combo > 0, "用例6：重开前连击有效");
            gs.ResetRun();
            Check(gs.Combo == 0, "用例6：ResetRun 连击清零");

            // ================= 用例 7：窗口超时断连（最后，含 3.3s 等待） =================
            gs.AddKillScore(100);
            Check(gs.Combo == 1 && gs.ComboTimeLeft() > 0.0, "用例7：超时前连击有效");
            await Coroutine.WaitSeconds(this, 3.3); // 窗口 3.0s，留 0.3s 余量
            Check(gs.Combo == 0, "用例7：窗口超时断连");

            foreach (var child in main.GetChildren())
            {
                if (child is Bullet)
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Coroutine.WaitSeconds(this, 0.6); // 演出/粒子播完，避免退出时对象泄漏

            gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"COMBO TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"COMBO TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
