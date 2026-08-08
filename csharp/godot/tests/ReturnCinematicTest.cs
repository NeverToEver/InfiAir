using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 返航过场测试（docs/RETURN_HOME_CINEMATIC.md §5）：直接触发/skip 路径/时序路径，
/// 以及 BackNavigator 的 SKIP_RETURN 决策与真实 Esc 注入。全程真实 Timer 等待。
/// 输入宽限（SKIP_GRACE 1.2s，防实战按键误触）：开播 1.2s 内任意键/点击/Esc 不跳过，
/// 各跳过路径断言前须先等 1.4s 真实时间越过宽限。
/// 与开场过场测试的关键差异：finished 后基地 UI 可见且树**保持暂停**（无标题定格）；
/// 每轮触发后须先 resume() 恢复（触发轨道打击动画，缩短时轴等其收尾）。
/// M7c：自 test/return_cinematic_test.gd 迁移（B09）。
/// </summary>
public partial class ReturnCinematicTest : Node
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

    private static int CountTimers(Node node)
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

    /// <summary>从基地整备恢复对局态：resume() 触发轨道打击动画（命中帧清场并解除暂停），
    /// 缩短时轴并等待播完，避免后续断言落在动画/暂停窗口内</summary>
    private async Task RestoreFromBase(BaseConsole baseUi)
    {
        if (baseUi.Visible)
        {
            baseUi.Resume();
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var main = baseUi.GetParent<Main>();
        if (main.Strike() is OrbitalStrike strike)
        {
            strike.DURATION = 0.4f;
        }
        var t = 0.0f;
        while (main.Strike() != null && t < 3.0f)
        {
            await Coroutine.WaitSeconds(this, 0.1);
            t += 0.1f;
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    public override void _Ready()
    {
        // 禁止裸 async void 生命周期：拆私有 async Task + fire-and-forget（约定 §Async）
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        GameState? gs = null;
        try
        {
            gs = GetNode<GameState>("/root/GameState");
            gs.DeleteSave();
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            AddChild(mainScene.Instantiate());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var main = GetNode<Main>("Main");
            var nav = main.GetNode<BackNavigator>("BackNavigator");
            var baseUi = main.GetNode<BaseConsole>("BaseUI");

            // ---------- 1. 直接触发：过场节点存在、树暂停 ----------
            var timerBaseline = CountTimers(GetTree().Root);
            main.PlayReturn();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var ret = main.ReturnCinematic();
            Check(ret != null, "直接触发：返航过场节点存在");
            Check(GetTree().Paused, "过场播放期间树暂停");
            Check(!baseUi.Visible, "过场播放期间基地 UI 未显");

            // ---------- 2. 输入宽限 + skip() 路径 ----------
            var finishedFired = 0;
            ret!.Finished += () => finishedFired++;
            // 输入宽限（开播 1.2s 内，防实战 WASD/Shift 持续按键误触）：任意键/点击不跳过
            var graceKey = new InputEventKey { Keycode = Key.A, Pressed = true };
            Input.ParseInputEvent(graceKey);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(GodotObject.IsInstanceValid(ret) && main.ReturnCinematic() == ret, "宽限期内任意键：过场不跳过（节点仍在）");
            Check(finishedFired == 0, "宽限期内任意键：finished 未发出");
            var graceClick = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true };
            Input.ParseInputEvent(graceClick);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(GodotObject.IsInstanceValid(ret) && main.ReturnCinematic() == ret, "宽限期内鼠标点击：过场不跳过");
            // 越过宽限后：销毁、finished 一次、基地 UI 可见且树仍暂停、无 Timer 残留
            await Coroutine.WaitSeconds(this, 1.4);
            main.SkipReturn();
            ret.Skip();  // 幂等：重复调用不重复发信号
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(finishedFired == 1, "skip：finished 信号发出且仅一次");
            Check(main.ReturnCinematic() == null && !GodotObject.IsInstanceValid(ret), "skip：过场已销毁");
            Check(baseUi.Visible, "skip：基地 UI 可见（关键差异：无标题定格，直接落基地）");
            Check(GetTree().Paused, "skip：树保持暂停（基地界面本就是暂停态 UI）");
            Check(CountTimers(GetTree().Root) == timerBaseline, "skip：无残留 Timer");
            await RestoreFromBase(baseUi);

            // ---------- 3. 时序路径：短时长推进 7 镜头，节点创建/销毁与最终 finished ----------
            main.PlayReturn();
            var ret2 = main.ReturnCinematic();
            var shortDurations = new Godot.Collections.Array { 0.3, 0.3, 0.3, 0.3, 0.3, 0.3, 0.3 };
            ret2!.SetShotDurations(shortDurations);
            var finished2 = false;
            ret2.Finished += () => finished2 = true;
            var seenShots = new System.Collections.Generic.List<string>();
            for (var expected = 0; expected < 7; expected++)
            {
                var reached = false;
                for (var i = 0; i < 40; i++)
                {
                    await Coroutine.WaitSeconds(this, 0.05);
                    if (!GodotObject.IsInstanceValid(ret2) || ret2.ShotIndex() >= expected)
                    {
                        reached = true;
                        break;
                    }
                }
                Check(reached, $"时序：推进到镜头 {expected + 1}");
                if (reached && GodotObject.IsInstanceValid(ret2) && ret2.CurrentShot() != null)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);  // 等 _advance() 旧镜头 queue_free 落定再数子节点（防抖）
                    if (!GodotObject.IsInstanceValid(ret2) || ret2.CurrentShot() == null)
                    {
                        continue;
                    }
                    var shotName = ret2.CurrentShot()!.Name.ToString();
                    if (!seenShots.Contains(shotName))
                    {
                        seenShots.Add(shotName);
                    }
                    Check(ret2.ShotRoot().GetChildCount() == 1, $"时序：镜头 {expected + 1} 旧节点已销毁（仅当前镜头在场）");
                    Check(ret2.Subtitle().Text != "", $"时序：镜头 {expected + 1} 叙事字幕已设置");
                }
            }
            // 镜头 7 末尾渐暗后 finished（渐暗含在时长内），等待窗口放宽
            for (var i = 0; i < 80; i++)
            {
                await Coroutine.WaitSeconds(this, 0.05);
                if (finished2)
                {
                    break;
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(finished2, "时序：7 镜头播完发出 finished");
            Check(seenShots.Count == 7, "时序：7 个镜头节点依次创建（Shot1..Shot7）");
            Check(main.ReturnCinematic() == null, "时序：播完销毁");
            Check(baseUi.Visible, "时序：播完基地 UI 可见");
            Check(GetTree().Paused, "时序：播完树保持暂停");
            Check(CountTimers(GetTree().Root) == timerBaseline, "时序：播完无残留 Timer");
            await RestoreFromBase(baseUi);

            // ---------- 4. Esc 路由：播放中决策 = SKIP_RETURN，真实 Esc 注入跳过 ----------
            main.PlayReturn();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var ret3 = main.ReturnCinematic();
            Check(nav.DecideBackAction() == BackNavigator.BackAction.SKIP_RETURN, "过场播放中：决策 = SKIP_RETURN");
            await Coroutine.WaitSeconds(this, 1.4);  // 越过输入宽限后 Esc 才生效
            await PressEsc();
            Check(main.ReturnCinematic() == null && !GodotObject.IsInstanceValid(ret3), "Esc：经 BackNavigator 跳过过场");
            Check(baseUi.Visible && GetTree().Paused, "Esc 跳过后基地 UI 可见且树仍暂停");
            await RestoreFromBase(baseUi);

            // ---------- 5. 任意键跳过（过场自身 _unhandled_input，需越过输入宽限） ----------
            main.PlayReturn();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var ret4 = main.ReturnCinematic();
            await Coroutine.WaitSeconds(this, 1.4);
            var anyKey = new InputEventKey { Keycode = Key.A, Pressed = true };
            Input.ParseInputEvent(anyKey);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(main.ReturnCinematic() == null && !GodotObject.IsInstanceValid(ret4), "任意键：过场自身捕获跳过");
            Check(baseUi.Visible && GetTree().Paused, "任意键跳过后基地 UI 可见且树仍暂停");
            await RestoreFromBase(baseUi);

            // ---------- 6. 鼠标点击跳过（与任意键同一出口，需越过输入宽限） ----------
            main.PlayReturn();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var ret5 = main.ReturnCinematic();
            await Coroutine.WaitSeconds(this, 1.4);
            var click = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true };
            Input.ParseInputEvent(click);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(main.ReturnCinematic() == null && !GodotObject.IsInstanceValid(ret5), "鼠标点击：过场自身捕获跳过");
            Check(baseUi.Visible && GetTree().Paused, "点击跳过后基地 UI 可见且树仍暂停");
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"RETURN CINEMATIC TEST 异常: {e}");
        }
        finally
        {
            gs?.DeleteSave();
            gs?.SaveProfile();
            GD.Print($"RETURN CINEMATIC TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
