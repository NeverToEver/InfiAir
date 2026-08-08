using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 返航过场逐镜头截图工具（人工核对用，非常规断言测试）。
/// 需窗口模式运行（headless 为 dummy 渲染，截不到画面）：
///   godot --path . res://test/return_capture.tscn
/// 把 _shot_durations 拉长到每镜头 8s，在各镜头中段截图存 /tmp/return_shot*.png；
/// 镜头 7 额外在渐暗末段截一张 7b（应接近全黑）。
/// </summary>
public partial class ReturnCapture : Node
{
    private const float SHOT_LEN = 8.0f;

    /// <summary>[距过场启动的秒数, 输出路径]：镜头 1..6 取 40% 处；镜头 7 取面部特写阶段与渐暗末段
    /// 例外：镜头 5 取 80% 处（8s 拉长时间轴下，40% 时座舱开启/跃下尚未发生）</summary>
    private static readonly (double, string)[] Schedule =
    {
        (3.2, "/tmp/return_shot1.png"),
        (11.2, "/tmp/return_shot2.png"),
        (19.2, "/tmp/return_shot3.png"),
        (27.2, "/tmp/return_shot4.png"),
        (39.7, "/tmp/return_shot5.png"),
        (43.2, "/tmp/return_shot6.png"),
        (54.6, "/tmp/return_shot7.png"),
        (55.9, "/tmp/return_shot7b.png"),
    };

    public override void _Ready()
    {
        // 禁止裸 async void 生命周期：拆私有 async Task + fire-and-forget
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            var cine = GD.Load<PackedScene>("res://scenes/return_cinematic.tscn").Instantiate<ReturnCinematic>();
            AddChild(cine);
            // add_child 同帧替换时长表（首镜头延后到帧末启动，见 return_cinematic._ready）
            // L01（2026-08-03 审查）：set_shot_durations 返回 void，原链式调用为编译错误
            // （A7 重构把 setter 改 void 时未同步工具，窗口模式截图工具已坏）；整表一次传入
            var shots = new Godot.Collections.Array();
            for (int i = 0; i < 7; i++)
            {
                shots.Add(SHOT_LEN);
            }
            cine.SetShotDurations(shots);
            var t = 0.0;
            foreach (var item in Schedule)
            {
                await Coroutine.WaitSeconds(this, item.Item1 - t);
                t = item.Item1;
                GetViewport().GetTexture().GetImage().SavePng(item.Item2);
                GD.Print($"capture saved: {item.Item2} (shot_index={cine.ShotIndex()})");
            }
        }
        catch (System.Exception e)
        {
            GD.PushError($"RETURN CAPTURE 异常: {e}");
        }
        finally
        {
            TestExit.Quit(0);
        }
    }
}
