using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 每屏截图存 /tmp/ui_&lt;name&gt;.png。需窗口模式运行（headless 为 dummy 渲染截不到画面）：
///   godot --path . res://test/ui_capture.tscn
/// 结束恢复现场：profile 当前值落盘。
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

            // 3. 天赋缓存面板（先垫缓存与已购节点，扇形/详情/底栏同屏可见）
            gs.SetMilestoneOverride(999999999);  // 屏蔽后续里程碑入账（确定性截图）
            gs.Talent.TestGrant(12);
            gs.Talent.TestSetLevel("power_shot", 2);
            gs.Talent.TestSetLevel("rapid_fire", 1);
            var talentPanel = GetNode<TalentPanel>("Main/TalentUI");
            // 3a. 蓄力进入（缓速）：按住 G 语义 → 底部进度条 → 满格自动进入；入场编排中间帧留档
            talentPanel.BeginCharge();
            await Coroutine.WaitSeconds(this, 0.3);
            Shot("talent_charge");
            await Coroutine.WaitSeconds(this, 0.4); // 蓄满自动进入
            await Coroutine.WaitSeconds(this, 0.16); // 入场编排中段（dim→轮盘→标题→卡片 stagger）
            Shot("talent_enter");
            await Settle();
            Shot("talent");
            // 3b. 下钻进攻系：树状扇形 + 节点详情卡（轮盘收缩 + 右区退场/进场编排，Settle 覆盖）
            talentPanel.TestDrillCategory(0);
            await Settle();
            talentPanel.TestSelectNode("power_shot");
            await Settle();
            Shot("talent_fan");
            talentPanel.CloseNow();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 4. 暂停面板（继续 primary）
            var pui = GetNode<PauseUi>("Main/PauseUI");
            pui.Open();
            await Settle();
            Shot("pause");
            pui.Close();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 5. 基地控制台（路线契约 + 重置代币；返航过场直接 skip 落基地，截虚影皮肤）
            gs.AddRp(20);
            gs.Talent.TestSetLevel("spread_shot", 1);
            var main = GetNode<Main>("Main");
            main.StartHomecoming();
            // 先真实时间等过 SKIP_GRACE（1.2s）再跳：跳过重试循环按帧计数，高刷无垂直同步下
            // 600 帧可能 <1.2s 真实时间跑完，宽限未过 → 跳过全程被忽略、后续截图全截到过场帧
            await Coroutine.WaitSeconds(this, 1.6);
            // 跳过返航过场：skip() 与自然结束同一出口，重试直到过场引用被 _on_return_finished 置空
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

            // 6. 死亡结算（击杀统计）
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

            // 恢复现场：profile 落盘
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
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ui_{name}.png");
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print("capture saved: " + path);
    }
}
