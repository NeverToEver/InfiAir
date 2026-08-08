using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 每屏截图存 /tmp/ui_&lt;name&gt;.png。需窗口模式运行（headless 为 dummy 渲染截不到画面）：
///   godot --path . res://test/ui_capture.tscn
/// 结束恢复现场：删除测试产生的存档，profile 原始值（最高分）还原落盘。
/// </summary>
public partial class UiCapture : Node
{
    private const double SETTLE_SECONDS = 1.2;  // 等 stagger/淡入动效播完（真实时间，暂停中 process_always 仍计时）

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
            // 快照 profile 原始值，结束还原（测试要伪造最高分/新纪录）
            var origHighScore = gs.HighScore;
            gs.HighScore = 12345;
            gs.LoginGuest();  // T4：游客会话直接开局（StartPanel 已退役）
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            AddChild(mainScene.Instantiate());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 1. 入口界面（welcome 登录面板，StartPanel 已退役）
            var wl = GD.Load<PackedScene>("res://scenes/welcome.tscn").Instantiate<CanvasLayer>();
            AddChild(wl);
            await Settle();
            Shot("welcome");
            wl.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 2. 设置页（对局内打开）
            var settings = GetTree().GetFirstNodeInGroup("settings_ui") as SettingsUi;
            settings!.ShowSettings();
            await Settle();
            Shot("settings");
            settings.Back();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 3. Buff 三选一（含层数标记：先垫一层 power_shot 候选必含时可见，随缘即可）
            gs.EmitSignal(GameState.SignalName.MilestoneReached, 100);
            await Settle();
            Shot("buff");
            var buffUi = GetNode<BuffSelect>("Main/BuffUI");
            if (buffUi.Visible)
            {
                // 原 GDScript 遗留无效的 InputEventMouseButton 构造（从未使用，死代码），C# 零警告要求下省略
                buffUi.PickBuff(buffUi.CurrentAvailable()[0].AsGodotDictionary()["id"].AsString());
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 屏蔽后续里程碑触发，避免 Buff UI 与结算叠屏（确定性截图）
            gs.SetMilestoneOverride(999999999);

            // 4. 暂停面板（继续 primary）
            var pui = GetNode<PauseUi>("Main/PauseUI");
            pui.Open();
            await Settle();
            Shot("pause");
            pui.Close();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 5. 基地控制台（四模块 section header；返航过场直接 skip 落基地，截虚影皮肤）
            gs.AddRp(10);
            gs.AddBuff("spread_shot");
            var main = GetNode<Main>("Main");
            main.StartHomecoming();
            // 跳过返航过场：skip() 有 SKIP_GRACE 输入宽限（开播数秒内忽略），宽限期内每帧重试，
            // 直到过场引用被 _on_return_finished 置空（跳过与自然结束同一出口）
            for (int i = 0; i < 600; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var rc = main.ReturnCinematic();
                if (rc != null && GodotObject.IsInstanceValid(rc))
                {
                    rc.Skip();
                }
                else
                {
                    break;
                }
            }
            // 等基地控制台真正可见再截（跳过过场后还有全息启动动效）
            var baseUi = GetNode<BaseConsole>("Main/BaseUI");
            for (int i = 0; i < 120; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (baseUi.Visible)
                {
                    break;
                }
            }
            await Settle();  // 等全息启动 0.25s + animate_open 0.2s 播完
            Shot("base");
            baseUi.Resume();
            GetTree().Paused = false;
            // 等轨道打击动画播完，避免叠入后续截图
            if (main.Strike() != null)
            {
                main.Strike()!.DURATION = 0.3f;
            }
            while (main.Strike() != null)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            // 6. 死亡结算（大分数 + 新纪录标记）；先收掉可能被分数再次触发的 Buff UI 避免叠屏
            if (buffUi.Visible)
            {
                buffUi.PickBuff(buffUi.CurrentAvailable()[0].AsGodotDictionary()["id"].AsString());
            }
            gs.HighScore = 100;  // 压低原纪录，保证「新纪录」标记可见（结尾还原）
            gs.AddScore(8888);
            gs.EmitSignal(GameState.SignalName.PlayerDied);
            // 等结算面板真正可见再截
            for (int i = 0; i < 60; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (GetNode<GameOverUi>("Main/GameOverUI").Visible)
                {
                    break;
                }
            }
            await Settle();
            Shot("gameover");

            // 恢复现场：删测试存档 + 还原 profile 原始值落盘
            gs.DeleteSave();
            gs.HighScore = origHighScore;
            gs.SaveProfile();
            GD.Print("ui capture done");
        }
        catch (System.Exception e)
        {
            GD.PushError($"UI CAPTURE 异常: {e}");
        }
        finally
        {
            TestExit.Quit(0);
        }
    }

    private async Task Settle()
    {
        // 真实时间等待（process_always=true：暂停中也会计时），与帧率无关
        await Coroutine.WaitSeconds(this, SETTLE_SECONDS);
    }

    private void Shot(string name)
    {
        var path = $"/tmp/ui_{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print("capture saved: " + path);
    }
}
