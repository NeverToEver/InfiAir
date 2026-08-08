using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 入场衔接动画测试：开场/继续出击后战机入场（高速冲入→向后缓移），
/// 期间仅左右可调/上下锁定，敌机生成延迟，动画结束恢复正常流程与敌机生成。
/// </summary>
public partial class EntryAnimationTest : Node
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
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            var gs = GetNode<GameState>("/root/GameState");
            gs.DeleteSave();
            gs.ResetRun();
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest(); // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var main = GetNode<Main>("Main");
            var player = main.Player();
            var spawner = main.GetNode<Spawner>("Spawner");
            var rect = gs.ViewWorldRect();
            var landY = rect.Position.Y + rect.Size.Y * player.ENTRY_LAND_RATIO;

            // ---------- 1. 触发入场序列：动画启动 + 敌机延迟 + 起点在屏下外 ----------
            main.StartEntrySequence();
            Check(player.IsEntryPlaying(), "入场动画启动");
            Check(!spawner.IsProcessing(), "入场期间敌机生成暂停（延迟）");
            Check(player.Position.Y > rect.End.Y, "入场起点在屏幕下外");

            // ---------- 2. 阶段 1 冲入：锁输入，tween 驱动定位到下 1/3 ----------
            var x0 = player.Position.X;
            Input.ActionPress("move_left");
            Input.ActionPress("move_up");
            await WaitPhysics(10);
            Check(player.Position.X == x0, "冲入阶段左右输入无效（锁输入）");
            Input.ActionRelease("move_left");
            Input.ActionRelease("move_up");
            // 轮询进入后撤：y 先抵达定位线邻域（冲入 EASE_OUT 末端单帧即过线）并连续停驻上升。
            // 注意 y >= land_y 在冲入期间（起点屏下外）恒成立，不能作为到达判据；用邻域连续帧确认 phase 2。
            var landed = false;
            var settleFrames = 0;
            for (var i = 0; i < 120; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                var y = player.Position.Y;
                if (y >= landY - 2.0f && y <= landY + 40.0f)
                {
                    settleFrames += 1;
                    if (settleFrames >= 8)
                    {
                        landed = true;
                        break;
                    }
                }
                else
                {
                    settleFrames = 0;
                }
            }

            Check(landed, "冲入定位到屏幕下 1/3");
            Check(!player.AutoFireEnabled(), "入场期间自动开火暂停");

            // ---------- 3. 阶段 2 后撤：仅左右可调、上下锁定 ----------
            Check(player.IsEntryPlaying(), "后撤阶段动画仍在播放");
            var xr0 = player.Position.X;
            Input.ActionPress("move_right");
            await WaitPhysics(10);
            Input.ActionRelease("move_right");
            Check(player.Position.X > xr0 + 5.0f, "后撤阶段左右可调");
            var yBeforeUp = player.Position.Y;
            Input.ActionPress("move_up");
            await WaitPhysics(10);
            Input.ActionRelease("move_up");
            Check(player.Position.Y > yBeforeUp, "后撤阶段上下锁定（按上仍自动后移）");

            // ---------- 4. 动画结束：恢复正常流程 + 敌机生成恢复 ----------
            // 轮询等待结束（rush 用 idle tween、retreat 用 physics，起点有帧率漂移，不用固定帧数）
            var endFrames = 0;
            while (player.IsEntryPlaying() && endFrames < 200)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                endFrames += 1;
            }

            Check(!player.IsEntryPlaying(), "入场动画结束");
            Check(spawner.IsProcessing(), "动画结束后敌机生成恢复");
            Check(player.AutoFireEnabled(), "入场动画结束后自动开火恢复");
            var expectedY = landY + player.ENTRY_RETREAT_SPEED * player.ENTRY_RETREAT_TIME;
            Check(Mathf.Abs(player.Position.Y - expectedY) < 25.0f, "入场终点接近正常站位");

            gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"ENTRY ANIMATION TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"ENTRY ANIMATION TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }

    private async Task WaitPhysics(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }
}
