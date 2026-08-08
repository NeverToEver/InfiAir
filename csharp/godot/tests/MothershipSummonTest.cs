using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 母舰召唤序列测试：机库小窗（弹出/字幕镜头/finished 自毁）→ 穿梭门创建与关闭 →
/// 母舰穿梭穿出减速入场（缩放/位置收敛）→ 减速带命中敌机 → DOCKING 火力掩护 →
/// 回收玩家进保护舱（隐藏+关判定）→ STAY 隐藏保持 → RELEASE 出舱恢复。
/// 小窗用真实时轴（~2.6s），穿梭段快进 _state_timer。
/// </summary>
public partial class MothershipSummonTest : Node
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
            gs.LoginGuest();  // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var main = GetNode<Main>("Main");
            var spawner = main.GetNode<Spawner>("Spawner");
            GetTree().Paused = false;  // 全程禁用刷怪与随机事件编排：只验证召唤序列自身
            spawner.SetProcess(false);
            main.Event()!.SetProcess(false);
            main.Formation()!.SetProcess(false);
            main.Player().SetAutoFire(false);

            // ---------- 1. 布置一台靶机（验证减速带与火力目标） ----------
            var tgt = GD.Load<PackedScene>("res://scenes/enemy.tscn").Instantiate<Enemy>();
            tgt.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
            tgt.CanShoot = false;
            tgt.Hp = 9999;  // 不死，保证场内始终有目标
            tgt.Position = new Vector2(1200.0f, 500.0f);
            main.AddChild(tgt);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // ---------- 2. 机库小窗 ----------
            main.SummonMothership();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var window = main.SummonWindow();
            Check(window != null, "小窗：蓄力完成后弹出机库小窗");
            Check(main.Mothership() == null, "小窗：播放期间母舰尚未创建");
            Check(main.Player().IsInputLocked(), "小窗：演出期玩家锁输入");
            Check(main.Player().InvincibleRemaining() > 100.0f, "小窗：演出期事件驱动无敌");
            main.SummonMothership();  // 幂等：播放中重复触发不叠加
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(main.SummonWindow() == window, "小窗：重复触发不叠加第二个窗口");
            Check(window!.Subtitle().Text == Tr("MS_SEQ_CHARGE"), "小窗：镜头 1 字幕（充能管线断开）");
            // 字幕镜头轮替（真实时轴：0.25 开场 + 0.8/0.6/0.7 三镜头）
            await Coroutine.WaitSeconds(this, 1.2);
            Check(window.Subtitle().Text == Tr("MS_SEQ_ARMS"), "小窗：镜头 2 字幕（维护臂解除链接）");
            await Coroutine.WaitSeconds(this, 0.8);
            Check(window.Subtitle().Text == Tr("MS_SEQ_LAUNCH"), "小窗：镜头 3 字幕（弹射·穿梭器启动）");
            // 播完 finished：小窗自毁 + 穿梭门/母舰创建
            for (int i = 0; i < 40; i++)
            {
                await Coroutine.WaitSeconds(this, 0.05);
                if (main.SummonWindow() == null)
                {
                    break;
                }
            }
            Check(main.SummonWindow() == null && !GodotObject.IsInstanceValid(window), "小窗：播完自毁并释放引用");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // ---------- 3. 穿梭门与穿梭入场 ----------
            WarpGate? gate = null;
            foreach (var child in main.GetChildren())
            {
                if (child is WarpGate wg)
                {
                    gate = wg;
                }
            }
            Check(gate != null, "穿梭门：小窗结束后创建");
            var ms = main.Mothership()!;
            Check(ms != null, "穿梭门：母舰已创建");
            Check(ms!.GetState() == Mothership.State.DESCEND, "穿梭入场：DESCEND 态");
            Check(ms!.Scale.X < 1.0f, $"穿梭入场：穿出期缩放小于 1（{ms!.Scale.X:0.00}）");
            ms!.SetStateTimer(ms.WARP_IN_TIME);  // 快进穿梭入场
            await Coroutine.WaitSeconds(this, 0.3);
            Check(ms.GetState() == Mothership.State.DOCKING, "穿梭入场：到位后自动对接");
            Check(ms.Scale.IsEqualApprox(Vector2.One), "穿梭入场：到位缩放收敛为 1");
            Check(ms.Position.DistanceTo(new Vector2(gs.ViewWorldRect().GetCenter().X, ms.HOVER_Y)) < 5.0f, "穿梭入场：停驻点收敛");
            // R07：拆 OR 弱断言（L 系列测试登记遗留）——原三重 OR（null/失效/CLOSING 任一即过）
            // 可空过；gate 已在上方断言非空，此处直接验证状态
            Check(gate!.GetPhase() == WarpGate.Phase.CLOSING, "穿梭门：母舰穿出后关闭");
            Check(tgt.SummonSlowTimer() > 0.0f, "减速带：敌机被施加短时减速");

            // ---------- 4. DOCKING 火力掩护 + 回收进保护舱 ----------
            await Coroutine.WaitSeconds(this, 0.4);
            var dockFire = false;
            foreach (var child in main.GetChildren())
            {
                if (child is Bullet b && b.IsPlayerBullet && b.ScoreScale < 1.0f)
                {
                    dockFire = true;
                }
            }
            Check(dockFire, "火力掩护：DOCKING 态即开火（不耗弹匣）");
            Check(ms.GetMagCells() == ms.MAG_CELLS, "火力掩护：DOCKING 不耗驻留弹匣");
            for (int i = 0; i < 40; i++)
            {
                await Coroutine.WaitSeconds(this, 0.05);
                if (!main.Player().Visible)
                {
                    break;
                }
            }
            Check(!main.Player().Visible, "保护舱：回收完成玩家隐藏");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!main.Player().HitboxEnabled(), "保护舱：受击判定关闭");
            Check(ms.Beam().Visible == false, "保护舱：牵引光束回收后隐藏");

            // ---------- 5. STAY 隐藏保持 → RELEASE 出舱 ----------
            for (int i = 0; i < 40; i++)
            {
                await Coroutine.WaitSeconds(this, 0.05);
                if (ms.GetState() == Mothership.State.STAY)
                {
                    break;
                }
            }
            Check(ms.GetState() == Mothership.State.STAY, "驻留：进入 STAY");
            Check(!main.Player().Visible, "驻留：玩家保持隐藏（保护舱）");
            // E05：H 按住时进度条可见；强制离舰（start_release）必须清掉——修复前 H 按住被强制
            // 离舰（警告到期/弹匣耗尽）进度条残留可见
            var hud = GetTree().GetFirstNodeInGroup("hud") as Hud;
            if (hud != null)
            {
                hud.SetEarlyLeaveCharge(0.5f);
                Check(hud.EarlyLeaveBox().Visible, "E05：前置：提前离舰进度条可见（模拟 H 按住）");
            }
            ms.StartRelease();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(main.Player().Visible, "释放：玩家出舱恢复显示");
            if (hud != null && GodotObject.IsInstanceValid(hud))
            {
                Check(!hud.EarlyLeaveBox().Visible, "E05：start_release 清除提前离舰进度条");
            }
            for (int i = 0; i < 40; i++)
            {
                await Coroutine.WaitSeconds(this, 0.05);
                if (!main.Player().IsInputLocked())
                {
                    break;
                }
            }
            Check(!main.Player().IsInputLocked(), "释放：输入解锁");
            Check(main.Player().InvincibleRemaining() <= 2.0f, "释放：无敌重置为 2s 保护");
            if (main.Mothership() != null)
            {
                main.Mothership()!.QueueFree();
            }

            // ---------- 5b. G011：母舰提前回收（_exit_tree，返航路径）须清除提前离舰进度条 ----------
            var hudG011 = GetTree().GetFirstNodeInGroup("hud") as Hud;
            if (hudG011 != null)
            {
                hudG011.SetEarlyLeaveCharge(0.5f);
                Check(hudG011.EarlyLeaveBox().Visible, "G011：前置：提前离舰进度条可见");
                var ms2 = GD.Load<PackedScene>("res://scenes/mothership.tscn").Instantiate<Mothership>();
                main.AddChild(ms2);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                ms2.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Check(!hudG011.EarlyLeaveBox().Visible, "G011：母舰回收（_exit_tree）清除提前离舰进度条");
            }

            gs.DeleteSave();
            gs.SaveProfile();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"MOTHERSHIP SUMMON TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"MOTHERSHIP SUMMON TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
