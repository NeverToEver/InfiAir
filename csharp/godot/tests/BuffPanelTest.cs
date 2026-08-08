using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// buff 滚动栏测试：收起态单行（最新 4 格 + 溢出 +N）、L 键展开/关闭、
/// Esc 经 BackNavigator 优先关栏、改语言刷新。运行：
///   godot --headless --path . res://test/buff_panel_test.tscn
/// </summary>
public partial class BuffPanelTest : Node
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
            var origLocale = gs.Locale;
            gs.DeleteSave();
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest(); // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var main = GetNode<Main>("Main");
            var hud = main.Hud();
            var nav = main.GetNode<BackNavigator>("BackNavigator");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // 屏蔽里程碑三选一叠屏
            gs.SetMilestoneOverride(999999999);

            // ---------- 1. 无 buff：标签隐藏，L 不展开 ----------
            Check(!hud.BuffTag().Visible, "无 buff：收起态标签隐藏");
            Check(!hud.IsBuffPanelOpen(), "初始：滚动栏关闭");
            await PressL();
            Check(!hud.IsBuffPanelOpen(), "无 buff：按 L 不展开滚动栏");

            // ---------- 2. 3 个 buff：单行全展示，无溢出 ----------
            gs.AddBuff(new StringName("power_shot"));
            gs.AddBuff(new StringName("armor"));
            gs.AddBuff(new StringName("regen"));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(hud.BuffDock().GetChildCount() == 3, "3 个 buff：收起态 3 格");
            Check(hud.BuffTag().Visible && hud.BuffTag().Text == "增益 [L]", "3 个 buff：标签带 [L] 提示");

            // ---------- 3. 6 个 buff：最新 4 格 + 溢出 +2 ----------
            gs.AddBuff(new StringName("piercing"));
            gs.AddBuff(new StringName("evasion"));
            gs.AddBuff(new StringName("slow_field"));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(hud.BuffDock().GetChildCount() == 5, "6 个 buff：收起态 4 格 + 溢出格");
            var overflow = hud.BuffOverflowLabel();
            Check(overflow != null && overflow.Text == "+2", "溢出格计数 +2");

            // ---------- 4. L 展开滚动栏：全量明细 ----------
            await PressL();
            Check(hud.IsBuffPanelOpen(), "按 L：滚动栏展开");
            Check(hud.BuffRows().GetChildCount() == 6, "滚动栏 6 行明细");

            // ---------- 5. Esc 路由：优先关栏而非打开暂停 ----------
            Check(nav.DecideBackAction() == BackNavigator.BackAction.CLOSE_BUFF_PANEL, "栏展开：Esc 决策=关栏");
            nav.GoBack();
            Check(!hud.IsBuffPanelOpen(), "Esc 执行：滚动栏关闭");
            Check(!main.PauseUi().Visible, "Esc 关栏后未误开暂停");

            // ---------- 6. L 再次开关 ----------
            await PressL();
            Check(hud.IsBuffPanelOpen(), "按 L：再次展开");
            await PressL();
            Check(!hud.IsBuffPanelOpen(), "按 L：再次关闭");

            // ---------- 7. 语言切换刷新 ----------
            gs.SetLocale("en");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(hud.BuffTag().Text == "BUFFS [L]", "en：标签刷新");
            Check(hud.BuffPanelTitle().Text == "Active Buffs", "en：滚动栏标题刷新");
            gs.SetLocale("zh");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // ---------- 8. 清理 ----------
            gs.DeleteSave();
            gs.SetLocale(origLocale);
            gs.SaveProfile();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"BUFF PANEL TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"BUFF PANEL TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }

    private async Task PressL()
    {
        var ev = new InputEventKey { Keycode = Key.L, Pressed = true };
        Input.ParseInputEvent(ev);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var up = new InputEventKey { Keycode = Key.L, Pressed = false };
        Input.ParseInputEvent(up);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
