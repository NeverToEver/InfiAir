using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 召唤序列视觉截图工具（窗口模式运行，headless 为 dummy 渲染截不到画面）：
///   godot --path . res://test/summon_capture.tscn
/// 驱动真实时轴逐段截图到 /tmp/summon_*.png：蓄力中段 → 机库小窗 3 镜头 →
/// 穿梭门/DESCEND → 牵引光束 DOCKING → 驻留 STAY。
/// 不写 user:// 存档/档案（与并行运行的其他测试隔离）；setup 对齐 mothership_summon_test。
/// </summary>
public partial class SummonCapture : Node
{
    private async Task Shot(string path)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);  // 让渲染推进一帧再取帧缓冲
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(path);
        GD.Print("capture saved: " + path);
    }

    public override void _Ready()
    {
        // 禁止裸 async void 生命周期：拆私有 async Task + fire-and-forget
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            var gs = GetNode<GameState>("/root/GameState");
            gs.ResetRun();
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest();  // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var main = GetNode<Main>("Main");
            var spawner = main.GetNode<Spawner>("Spawner");
            GetTree().Paused = false;
            // 全程禁用刷怪与随机事件编排：只拍召唤序列自身
            spawner.SetProcess(false);
            main.Event()!.SetProcess(false);
            main.Formation()!.SetProcess(false);
            main.Player().SetAutoFire(false);

            // 靶机一台：DOCKING 火力掩护的目标（不死、不开火）
            var tgt = GD.Load<PackedScene>("res://scenes/enemy.tscn").Instantiate<Enemy>();  // M3b：Enemy 迁 C#，移除 as 断言
            tgt.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
            tgt.CanShoot = false;
            tgt.Hp = 9999;
            tgt.Position = new Vector2(1200.0f, 500.0f);
            main.AddChild(tgt);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // ---------- 1. 蓄力中段（虚影 + 蓄力特效） ----------
            Input.ActionPress("dock");
            main.SetChargeTime(main.DOCK_CHARGE_TIME * 0.55f);  // 预填到中段，让环收缩/背光可读
            await Coroutine.WaitSeconds(this, 0.35);
            await Shot("/tmp/summon_charge.png");
            Input.ActionRelease("dock");
            main.StopCharging();

            // ---------- 2. 机库小窗 3 镜头（真实时轴 ~2.6s） ----------
            main.SummonMothership();
            await Coroutine.WaitSeconds(this, 0.65);  // 镜头 1 中段（充能管线断开）
            await Shot("/tmp/summon_window1.png");
            await Coroutine.WaitSeconds(this, 0.8);  // 镜头 2 中段（维护臂收回）
            await Shot("/tmp/summon_window2.png");
            await Coroutine.WaitSeconds(this, 0.6);  // 镜头 3 中段（弹射出仓）
            await Shot("/tmp/summon_window3.png");
            for (int i = 0; i < 40; i++)  // 等播完：小窗自毁 + 穿梭门/母舰创建
            {
                await Coroutine.WaitSeconds(this, 0.05);
                if (main.SummonWindow() == null)
                {
                    break;
                }
            }

            // ---------- 3. 穿梭门 + 母舰 DESCEND 前段（舰体尚在门心，前唇遮挡读「穿门」） ----------
            await Coroutine.WaitSeconds(this, 0.14);
            await Shot("/tmp/summon_gate.png");

            // ---------- 4. DOCKING 牵引光束（流环/描边/尘粒 + 火力掩护） ----------
            await Coroutine.WaitSeconds(this, 1.05);  // DESCEND 剩余 + DOCKING 前 0.4s
            await Shot("/tmp/summon_beam.png");

            // ---------- 5. 驻留 STAY（玩家已进保护舱） ----------
            var ms = main.Mothership();  // M4：Mothership 迁 C#，去类型注解
            for (int i = 0; i < 60; i++)
            {
                await Coroutine.WaitSeconds(this, 0.05);
                if (ms == null || !GodotObject.IsInstanceValid(ms) || ms.GetState() == Mothership.State.STAY)
                {
                    break;
                }
            }
            await Coroutine.WaitSeconds(this, 0.3);
            await Shot("/tmp/summon_stay.png");

            // 清理：收回母舰（_exit_tree 恢复玩家出舱），靶机随场景退出
            if (main.Mothership() != null)
            {
                main.Mothership()!.QueueFree();
            }
            GD.Print("[DONE] summon capture finished");
        }
        catch (System.Exception e)
        {
            GD.PushError($"SUMMON CAPTURE 异常: {e}");
        }
        finally
        {
            TestExit.Quit(0);
        }
    }
}
