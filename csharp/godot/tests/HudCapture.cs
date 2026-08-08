using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// HUD 布局巡检截图：常态（2 个 buff）与极端（已解锁 buff 满层：BUFF_POOL_SIZE=19
/// 池中当前 15 种 distinct 加满，R07 注释修正）两种形态，
/// 每屏截图存 /tmp/hud_&lt;name&gt;.png。需窗口模式运行（headless 为 dummy 渲染截不到画面）：
///   godot --path . res://test/hud_capture.tscn
/// 结束恢复现场：删除测试产生的存档，profile 按备份内容还原落盘（R07：注释修正——
/// 原「原始值还原」措辞失实，实际为删除测试存档 + save_profile 落盘当前值）。
/// </summary>
public partial class HudCapture : Node
{
    private const double SETTLE_SECONDS = 0.6;  // 等重建/淡入动效播完（真实时间）

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
            gs.DeleteSave();

            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest();  // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 屏蔽里程碑触发，避免 Buff UI 叠屏（确定性截图）
            gs.SetMilestoneOverride(999999999);
            gs.AddScore(12340);
            gs.Kills = 57;
            gs.EmitSignal(GameState.SignalName.ScoreChanged, gs.Score);

            // 1. 常态：2 个 buff（单/多层各一）
            gs.AddBuff("power_shot");
            gs.AddBuff("power_shot");
            gs.AddBuff("armor");
            await Settle();
            Shot("normal");

            // 2. 极端：全部已解锁 buff 叠层拉满（BUFF_POOL_SIZE=19 池中 15 种 distinct，R07 修正）
            for (int i = 0; i < 3; i++)
            {
                gs.AddBuff("power_shot");  // 共 5 层
            }
            for (int i = 0; i < 4; i++)
            {
                gs.AddBuff("rapid_fire");
            }
            for (int i = 0; i < 3; i++)
            {
                gs.AddBuff("spread_shot");
            }
            for (int i = 0; i < 10; i++)
            {
                gs.AddBuff("extra_life");
            }
            gs.AddBuff("regen");
            for (int i = 0; i < 2; i++)
            {
                gs.AddBuff("piercing");
            }
            gs.AddBuff("explosive");
            gs.AddBuff("lifesteal");
            gs.AddBuff("evasion");
            for (int i = 0; i < 3; i++)
            {
                gs.AddBuff("phase_dash");
            }
            gs.AddBuff("slow_field");
            gs.AddBuff("efficient_boost");
            gs.AddBuff("boost_recovery");
            gs.AddBuff("mothership_recall");
            gs.AddBuff("laser_beam");
            // 等首发激光束（获得即发，3s）播完再截，避免遮挡画面
            await Coroutine.WaitSeconds(this, 3.4);
            Shot("stress");

            // 3. L 展开 buff 滚动栏（已解锁 15 种 distinct 明细行，R07 修正）
            GetNode<Hud>("Main/HUD").ToggleBuffPanel();
            await Settle();
            Shot("panel");

            // 恢复现场：删测试存档 + 还原 profile 原始值落盘
            gs.DeleteSave();
            gs.SaveProfile();
            GD.Print("hud capture done");
        }
        catch (System.Exception e)
        {
            GD.PushError($"HUD CAPTURE 异常: {e}");
        }
        finally
        {
            TestExit.Quit(0);
        }
    }

    private async Task Settle()
    {
        // 真实时间等待，与帧率无关
        await Coroutine.WaitSeconds(this, SETTLE_SECONDS);
    }

    private void Shot(string name)
    {
        var path = $"/tmp/hud_{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print("capture saved: " + path);
    }
}
