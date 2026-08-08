using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 开场过场逐镜头截图工具（人工核对用，非常规断言测试）。
/// 需窗口模式运行（headless 为 dummy 渲染，截不到画面）：
///   godot --path . res://test/intro_capture.tscn
/// 把 _shot_durations 拉长到每镜头 8s，在各镜头关键动作展开后截图存 /tmp/intro_shot*.png；
/// 末尾加一张标题定格（48.6s = 6×8 + 0.6，定格段 1.2s 中段）。
/// </summary>
public partial class IntroCapture : Node
{
    private const float SHOT_LEN = 8.0f;

    /// <summary>[距过场启动的秒数, 输出路径]：各镜头取 50–65% 处（关键动作已展开），外加标题定格
    /// 镜头 1 取 51%（dur*0.45 的二次殉爆刚起，冲击波扩散中段）</summary>
    private static readonly (double, string)[] Schedule =
    {
        (4.1, "/tmp/intro_shot1.png"),
        (12.2, "/tmp/intro_shot2.png"),
        (20.0, "/tmp/intro_shot3.png"),
        (28.4, "/tmp/intro_shot4.png"),
        (36.5, "/tmp/intro_shot5.png"),
        (45.0, "/tmp/intro_shot6.png"),
        (48.6, "/tmp/intro_title.png"),
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
            var cine = GD.Load<PackedScene>("res://scenes/intro_cinematic.tscn").Instantiate<IntroCinematic>();
            AddChild(cine);
            // add_child 同帧替换时长表（首镜头延后到帧末启动，见 intro_cinematic._ready）
            // L01（2026-08-03 审查）：set_shot_durations 返回 void，原链式调用为编译错误
            // （A7 重构把 setter 改 void 时未同步工具，窗口模式截图工具已坏）；整表一次传入
            var shots = new float[6];
            for (int i = 0; i < 6; i++)
            {
                shots[i] = SHOT_LEN;
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
            GD.PushError($"INTRO CAPTURE 异常: {e}");
        }
        finally
        {
            TestExit.Quit(0);
        }
    }
}
