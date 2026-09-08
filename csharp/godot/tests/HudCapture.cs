using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// HUD 布局巡检截图：常态（2 个 buff）与极端（已解锁 buff 满层：AUG_POOL_SIZE=19
/// 池中当前 15 种 distinct 加满，R07 注释修正）两种形态，
/// 每屏截图存 /tmp/hud_&lt;name&gt;.png。需窗口模式运行（headless 为 dummy 渲染截不到画面）：
///   godot --path . res://test/hud_capture.tscn
/// 结束恢复现场：profile 当前值落盘。
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

            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest();  // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 屏蔽里程碑入账，保证缓存指示器读数确定（确定性截图）；击杀计数固定 57
            gs.SetMilestoneOverride(999999999);
            gs.Kills = 57;
            gs.EmitSignal(GameState.SignalName.ScoreChanged, gs.Score);
            // 常态截图缓存 3 点：指示器「可用」呼吸态可见
            gs.Talent.TestGrant(3);

            // 1. 常态：2 个天赋（单/多层各一）
            gs.Talent.TestSetLevel("power_shot", 2);
            gs.Talent.TestSetLevel("armor", 1);
            await Settle();
            Shot("normal");

            // 1b. 统一蓄力条组件（HudChargeBar）：三通道同屏目检风格一致性（仅颜色/文案不同）
            GetNode<Hud>("Main/HUD").SetCharge(Hud.ChargeChannel.Homecoming, 0.45f);
            GetNode<Hud>("Main/HUD").SetCharge(Hud.ChargeChannel.GiveUp, 0.7f);
            GetNode<Hud>("Main/HUD").SetCharge(Hud.ChargeChannel.EarlyLeave, 0.9f);
            await Settle();
            Shot("charges");
            GetNode<Hud>("Main/HUD").SetCharge(Hud.ChargeChannel.Homecoming, -1f);
            GetNode<Hud>("Main/HUD").SetCharge(Hud.ChargeChannel.GiveUp, -1f);
            GetNode<Hud>("Main/HUD").SetCharge(Hud.ChargeChannel.EarlyLeave, -1f);

            // 2. 极端：全部已解锁天赋叠层拉满（19 种 distinct；上限以 augments.<id>.max_stacks 为准）
            gs.Talent.TestSetLevel("power_shot", 5);
            gs.Talent.TestSetLevel("rapid_fire", 4);
            gs.Talent.TestSetLevel("spread_shot", 2);
            gs.Talent.TestSetLevel("extra_life", 10);
            gs.Talent.TestSetLevel("regen", 1);
            gs.Talent.TestSetLevel("piercing", 2);
            gs.Talent.TestSetLevel("explosive", 1);
            gs.Talent.TestSetLevel("lifesteal", 1);
            gs.Talent.TestSetLevel("evasion", 1);
            gs.Talent.TestSetLevel("phase_dash", 3);
            gs.Talent.TestSetLevel("slow_field", 1);
            gs.Talent.TestSetLevel("efficient_boost", 2);
            gs.Talent.TestSetLevel("boost_recovery", 2);
            gs.Talent.TestSetLevel("mothership_recall", 2);
            gs.Talent.TestSetLevel("laser_beam", 1);
            // 等首发激光束（获得即发，3s）播完再截，避免遮挡画面
            await Coroutine.WaitSeconds(this, 3.4);
            Shot("stress");

            // 3. L 展开 buff 滚动栏（已解锁 15 种 distinct 明细行，R07 修正）
            GetNode<Hud>("Main/HUD").ToggleAugmentPanel();
            await Settle();
            Shot("panel");

            // 恢复现场：profile 落盘
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
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hud_{name}.png");
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print("capture saved: " + path);
    }
}
