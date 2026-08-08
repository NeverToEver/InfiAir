using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 开场过场测试（docs/INTRO_CINEMATIC.md §5）：直接触发/skip 路径/时序路径/门禁路径，
/// 以及 BackNavigator 的 SKIP_INTRO 决策与真实 Esc 注入。全程真实 Timer 等待。
/// </summary>
public partial class IntroCinematicTest : Node
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

    private int CountTimers(Node node)
    {
        var n = 0;
        foreach (var c in node.GetChildren())
        {
            if (c is Godot.Timer)
            {
                n++;
            }
            n += CountTimers(c);
        }
        return n;
    }

    private async Task PressEsc()
    {
        var ev = new InputEventKey { Keycode = Key.Escape, Pressed = true };
        Input.ParseInputEvent(ev);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var up = new InputEventKey { Keycode = Key.Escape, Pressed = false };
        Input.ParseInputEvent(up);
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
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest();  // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var main = GetNode<Main>("Main");
            var nav = main.GetNode<BackNavigator>("BackNavigator");
            var act = BackNavigator.BackActions();  // 枚举经字典访问（原 GDScript 经实例访问为 Variant）

            // ---------- 1. 门禁路径：测试场景（current_scene != Main）实例化不自动播过场 ----------
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(main.Intro() == null, "门禁：测试场景实例化 main 不播过场");
            Check(!GetTree().Paused, "门禁：未残留暂停");

            // ---------- 2. 直接触发：过场节点存在、树暂停 ----------
            var timerBaseline = CountTimers(GetTree().Root);
            main.PlayIntro();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var intro = main.Intro();
            Check(intro != null, "直接触发：过场节点存在");
            Check(GetTree().Paused, "过场播放期间树暂停");

            // ---------- 3. skip() 路径：销毁、finished、恢复非暂停、无 Timer 残留 ----------
            var finishedFired = new[] { false };
            intro!.Finished += () => finishedFired[0] = true;
            main.SkipIntro();
            intro.Skip();  // 幂等：重复调用不重复发信号
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(finishedFired[0], "skip：finished 信号发出（且仅一次）");
            Check(main.Intro() == null && !GodotObject.IsInstanceValid(intro), "skip：过场已销毁");
            Check(!GetTree().Paused, "skip：树恢复非暂停");
            Check(CountTimers(GetTree().Root) == timerBaseline, "skip：无残留 Timer");

            // ---------- 4. 时序路径：短时长推进 6 镜头，节点创建/销毁与最终 finished ----------
            main.PlayIntro();
            var intro2 = main.Intro()!;
            var shortDurations = new float[] { 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f };
            intro2.SetShotDurations(shortDurations);
            var finished2 = new[] { false };
            intro2.Finished += () => finished2[0] = true;
            var seenShots = new System.Collections.Generic.List<string>();
            for (int expected = 0; expected < 6; expected++)
            {
                var reached = false;
                for (int i = 0; i < 40; i++)
                {
                    await Coroutine.WaitSeconds(this, 0.05);
                    if (!GodotObject.IsInstanceValid(intro2) || intro2.ShotIndex() >= expected)
                    {
                        reached = true;
                        break;
                    }
                }
                Check(reached, $"时序：推进到镜头 {expected + 1}");
                if (reached && GodotObject.IsInstanceValid(intro2) && intro2.CurrentShot() != null)
                {
                    var shotName = intro2.CurrentShot()!.Name;
                    if (!seenShots.Contains(shotName))
                    {
                        seenShots.Add(shotName);
                    }
                    Check(intro2.ShotRoot().GetChildCount() == 1, $"时序：镜头 {expected + 1} 旧节点已销毁（仅当前镜头在场）");
                    Check(intro2.Subtitle().Text != "", $"时序：镜头 {expected + 1} 叙事字幕已设置");
                }
            }
            // 收尾标题定格额外 1.8s（§2），等待窗口放宽
            for (int i = 0; i < 80; i++)
            {
                await Coroutine.WaitSeconds(this, 0.05);
                if (finished2[0])
                {
                    break;
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(finished2[0], "时序：6 镜头播完发出 finished");
            Check(seenShots.Count == 6, "时序：6 个镜头节点依次创建（Shot1..Shot6）");
            Check(main.Intro() == null && !GetTree().Paused, "时序：播完销毁并恢复非暂停");
            Check(CountTimers(GetTree().Root) == timerBaseline, "时序：播完无残留 Timer");

            // ---------- 5. Esc 路由：播放中决策 = SKIP_INTRO，真实 Esc 注入跳过 ----------
            main.PlayIntro();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var intro3 = main.Intro();
            Check(nav.DecideBackAction() == (BackNavigator.BackAction)act["SKIP_INTRO"].AsInt32(), "过场播放中：决策 = SKIP_INTRO");
            await PressEsc();
            Check(main.Intro() == null && !GodotObject.IsInstanceValid(intro3), "Esc：经 BackNavigator 跳过过场");
            Check(!GetTree().Paused, "Esc 跳过后树恢复非暂停");

            // ---------- 6. 任意键跳过（过场自身 _unhandled_input） ----------
            main.PlayIntro();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var intro4 = main.Intro();
            var ev = new InputEventKey { Keycode = Key.A, Pressed = true };
            Input.ParseInputEvent(ev);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(main.Intro() == null && !GodotObject.IsInstanceValid(intro4), "任意键：过场自身捕获跳过");
            Check(!GetTree().Paused, "任意键跳过后树恢复非暂停");

            // ---------- 7. 鼠标点击跳过（与任意键同一出口） ----------
            main.PlayIntro();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var intro5 = main.Intro();
            var click = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true };
            Input.ParseInputEvent(click);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(main.Intro() == null && !GodotObject.IsInstanceValid(intro5), "鼠标点击：过场自身捕获跳过");
            Check(!GetTree().Paused, "点击跳过后树恢复非暂停");

            gs.DeleteSave();
            gs.SaveProfile();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"INTRO CINEMATIC TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"INTRO CINEMATIC TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
