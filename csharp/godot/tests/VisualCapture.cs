using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 视觉验证：按 MODE 截图到 /tmp/infiair_capture.png。
/// 需窗口模式运行（headless 为 dummy 渲染，截不到画面）：
///   godot --path . res://test/visual_capture.tscn
/// MODE: gameplay（默认，Boss 警告画面）/ hud（常态对局 HUD：buff 芯片 + 低血晕影）/
/// boss_fight（Boss 名牌 + 血条 + 狂暴态）/ welcome（登录面板）/ base（基地控制台）/
/// mothership（母舰驻留）/ summon（召唤机库小窗）/ settings（设置页）/ exit_confirm（暂停面板 + 战斗退出确认窗）
/// </summary>
public partial class VisualCapture : Node
{
    private const int FRAMES_BEFORE_SHOT = 100;
    private const string SHOT_PATH = "/tmp/infiair_capture.png";
    private static readonly string MODE = "gameplay";
    private static readonly string FORCE_LOCALE = "";  // "en" 时强制英文截图

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
            if (FORCE_LOCALE != "")
            {
                gs.SetLocale(FORCE_LOCALE);
            }
            if (MODE == "welcome")
            {
                // 登录面板截图（welcome 主场景，非对局画面）
                gs.LoginGuest();
                var wl = GD.Load<PackedScene>("res://scenes/welcome.tscn").Instantiate<CanvasLayer>();
                AddChild(wl);
                for (int i = 0; i < 30; i++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                var img = GetViewport().GetTexture().GetImage();
                img.SavePng(SHOT_PATH);
                GD.Print("saved: " + SHOT_PATH);
                return;
            }
            gs.LoginGuest();  // T4：游客会话直接开局（StartPanel 已退役）
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            AddChild(mainScene.Instantiate());
            switch (MODE)
            {
                case "exit_confirm":
                    {
                        // 暂停面板 + 战斗退出确认窗（battle 模式进度损失警告）
                        var pui = GetNode<PauseUi>("Main/PauseUI");
                        pui.Open();
                        pui.Quit();
                        for (int i = 0; i < 30; i++)
                        {
                            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        }
                        break;
                    }
                case "gameplay":
                    {
                        // 触发 Boss 警告横幅，便于截图覆盖该画面
                        GetNode<Spawner>("Main/Spawner").TriggerBoss();
                        for (int i = 0; i < FRAMES_BEFORE_SHOT; i++)
                        {
                            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        }
                        break;
                    }
                case "hud":
                    {
                        // 常态对局 HUD：垫 buff 让芯片区可见 + 压低 HP 展示低血晕影脉动
                        gs.AddBuff("power_shot");
                        gs.AddBuff("power_shot");
                        gs.AddBuff("spread_shot");
                        gs.AddBuff("armor");
                        gs.AddBuff("laser_beam");
                        gs.Health = 18.0;
                        for (int i = 0; i < FRAMES_BEFORE_SHOT; i++)
                        {
                            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        }
                        break;
                    }
                case "boss_fight":
                    {
                        // Boss 名牌 + 血条 + 狂暴态（打掉 75% HP 触发狂暴，名牌整行转红）
                        GetNode<Spawner>("Main/Spawner").TriggerBoss();
                        Boss? boss = null;  // M3d：Boss 迁 C#，去类型注解
                        for (int i = 0; i < 1800; i++)  // 等降入完成进入战斗（上限 30s，窗口低帧率冗余）
                        {
                            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                            foreach (var e in gs.Enemies)
                            {
                                if (e is Boss b && b.IsInFight())  // M3d：Boss 迁 C#，is 经脚本资源、as 简化
                                {
                                    boss = b;
                                }
                            }
                            if (boss != null)
                            {
                                break;
                            }
                        }
                        if (boss != null)
                        {
                            boss.TakeDamage((int)(boss.MaxHp * 0.75f));
                            for (int i = 0; i < 120; i++)  // 狂暴转场演出一段后截图
                            {
                                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                            }
                        }
                        break;
                    }
                case "base":
                    {
                        // 基地控制台界面
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        gs.AddRp(10);
                        gs.AddBuff("spread_shot");
                        GetNode<Main>("Main").StartHomecoming();
                        await Coroutine.WaitSeconds(this, 2.0);
                        break;
                    }
                case "settings":
                    {
                        // 设置页（控制分区改键表）
                        (GetTree().GetFirstNodeInGroup("settings_ui") as SettingsUi)!.ShowSettings();
                        for (int i = 0; i < 30; i++)
                        {
                            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        }
                        break;
                    }
                case "mothership":
                    {
                        // 母舰召唤序列（小窗跳过）→ 穿梭入场快进 → 自动对接 + 敌机（驻留扫射/导弹）
                        var main = GetNode<Main>("Main");
                        main.SummonMothership();
                        main.SummonWindow()!.Skip();
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        var ms = main.Mothership()!;  // M4：Mothership 迁 C#，去类型注解
                        ms.SetStateTimer(ms.WARP_IN_TIME);  // 快进穿梭入场，到位触发自动对接
                        var spawner = GetNode<Spawner>("Main/Spawner");
                        var tgt = GD.Load<PackedScene>("res://scenes/enemy.tscn").Instantiate<Enemy>();  // M3b：Enemy 迁 C#，enemy.tscn 根脚本即 Enemy，不能经类名 as（untyped）
                        tgt.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
                        tgt.Position = new Vector2(1200.0f, 500.0f);
                        main.AddChild(tgt);
                        await Coroutine.WaitSeconds(this, 2.8);  // 对接 1.5s + 补给 0.5s → 驻留
                        break;
                    }
                case "summon":
                    {
                        // 召唤机库小窗演出镜头 1（充能管线断开）
                        var main = GetNode<Main>("Main");
                        main.SummonMothership();
                        await Coroutine.WaitSeconds(this, 0.65);
                        break;
                    }
            }
            var img2 = GetViewport().GetTexture().GetImage();
            img2.SavePng(SHOT_PATH);
            GD.Print("capture saved: " + SHOT_PATH);
            gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            GD.PushError($"VISUAL CAPTURE 异常: {e}");
        }
        finally
        {
            TestExit.Quit(0);
        }
    }
}
