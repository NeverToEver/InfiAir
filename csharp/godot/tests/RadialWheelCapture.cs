using System.Threading.Tasks;

using Godot;

using InfiAir.Core;

namespace InfiAir.Tests;

/// <summary>
/// 左缘轮盘截图工具：开机物化中段 / 根层全容 / 溢出滚动 / 下钻子层四态。
/// 需窗口模式运行（headless 为 dummy 渲染截不到画面）：
///   godot --path . res://test/radial_wheel_capture.tscn
/// 输出 /tmp/radial_*.png。开机帧用帧计数等待（真实时间过短会截到窗口首帧呈现前的黑帧）。
/// </summary>
public partial class RadialWheelCapture : Node
{
    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        try
        {
            // 暗色背景垫（模拟对局画面底色，截图观感对齐实机）
            var bg = new ColorRect { Color = new Color(0.008f, 0.014f, 0.027f), MouseFilter = Control.MouseFilterEnum.Ignore };
            bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(bg);

            var wheel = new RadialWheel { BackLabel = "BACK" };
            wheel.Position = new Vector2(-260f, 540f); // 圆心在屏幕外，右侧 ~1/4 弧面伸入
            AddChild(wheel);
            await WaitFrames(20); // 热身：等窗口完成首帧呈现，开机帧不再截到黑帧

            // 全容态（宿主页同款槽距压缩）：8 卡全部署，开机涟漪完整可见
            wheel.Load(DemoTree(), RadialWheel.SlotAngleFor(8));
            wheel.PlayBoot(); // 开机物化：扫掠成形 + 卡片交错部署 + 全息闪烁
            await WaitFrames(5); // ≈0.13s：扫掠中段 + 首批卡片部署中
            Shot("radial_boot");
            await Settle(0.7);
            Shot("radial_root");

            // 溢出态（默认槽距）：8 项 × 27° 超出可视半幅，演示滚动/吸附/下钻
            wheel.Load(DemoTree());
            await Settle(0.3);
            wheel.TestScrollBy(2); // 滚动两槽（含松手吸附语义的展示）
            await Settle(0.6);
            Shot("radial_scroll");

            wheel.TestDrill(0); // 下钻 WEAPONS：收缩 → 面包屑 → 子层弹出
            await Settle(1.2);
            Shot("radial_drill");

            // RADIAL_HOLD=1：不退出，保持轮盘打开供实机交互体检（悬停/滚轮/点击下钻）
            if (OS.GetEnvironment("RADIAL_HOLD") != "1")
            {
                GetTree().Quit(0);
            }
        }
        catch (Exception e)
        {
            GD.PushError(e.ToString());
            GetTree().Quit(1);
        }
    }

    private async Task Settle(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    /// <summary>帧计数等待：避开窗口启动期真实时间短、首帧未呈现的空窗（截图黑帧）。</summary>
    private async Task WaitFrames(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private void Shot(string name)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{name}.png");
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"capture saved: {path}");
    }

    private static RadialWheelOption Leaf(string id) => new() { Id = id, Label = id };

    private static RadialWheelOption Node(string id, RadialGlyph glyph, params RadialWheelOption[] children) => new()
    {
        Id = id,
        Label = id,
        Glyph = glyph,
        Children = children,
    };

    private static RadialWheelOption[] DemoTree()
    {
        return new[]
        {
            Node("WEAPONS", RadialGlyph.Hex,
                Node("CANNON", RadialGlyph.Diamond, Leaf("LIGHT"), Leaf("HEAVY"), Leaf("SCATTER")),
                Node("MISSILE", RadialGlyph.Triangle, Leaf("SINGLE"), Leaf("SALVO")),
                Node("TORPEDO", RadialGlyph.Bolt, Leaf("MARK-I")),
                Node("FLARE", RadialGlyph.Star),
                Node("RAILGUN", RadialGlyph.Ring, Leaf("CHARGE")),
                Node("PULSE", RadialGlyph.Cross, Leaf("WIDE"), Leaf("FOCUS"))),
            Node("ENGINE", RadialGlyph.Bolt,
                Node("BOOST", RadialGlyph.Triangle),
                Node("CRUISE", RadialGlyph.Ring),
                Node("VENT", RadialGlyph.Cross)),
            Node("SHIELD", RadialGlyph.Ring, Leaf("REINFORCE"), Leaf("REFLECT")),
            Node("COMMS", RadialGlyph.Chevron, Leaf("HAIL"), Leaf("SILENCE")),
            Node("SAMPLER", RadialGlyph.Diamond, Leaf("CORE"), Leaf("DUST")),
            Node("REPAIR", RadialGlyph.Cross, Leaf("HULL"), Leaf("SYSTEMS")),
            Node("LAUNCH", RadialGlyph.Star, Leaf("PROBE")),
            Node("ABANDON", RadialGlyph.Triangle, Leaf("CONFIRM")),
        };
    }
}
