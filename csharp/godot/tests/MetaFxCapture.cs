using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// Meta HUD 视觉审计截图（docs/META_HUD_DESIGN.md §7 人工核对）：
/// 需窗口模式运行（headless 为 dummy 渲染，截不到画面）：
///   godot --path . res://test/meta_fx_capture.tscn
/// 输出 /tmp/meta_fx_{healthy,hit,caution,damaged,dying,settings_modes}.png：
/// 满血基准（应与世界原貌一致）/ 受击色差峰+定向波纹 / 各血量档裂纹密度 / DYING 收窄 / 设置页无障碍分区。
/// </summary>
public partial class MetaFxCapture : Node
{
    private const string OUT_DIR = "/tmp/";

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
            gs.ReduceFlash = false;
            AddChild(GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate());
            var main = GetNode<Main>("Main");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var spawner = GetNode<Spawner>("Main/Spawner");
            spawner.SetProcess(false);
            var player = GetNode<Player>("Main/Player");
            player.SetAutoFire(false);
            // 摆几架静态敌机丰富画面，让后处理有内容可作用
            for (int i = 0; i < 5; i++)
            {
                var e = GD.Load<PackedScene>("res://scenes/enemy.tscn").Instantiate<Enemy>();  // M3b：Enemy 迁 C#，移除 as 断言
                e.Setup(spawner.ENEMY_TYPES[i % spawner.ENEMY_TYPES.Count], "straight", 1.0f);
                e.CanShoot = false;
                e.Speed = 0.0f;
                e.Position = new Vector2(500.0f + i * 220.0f, 300.0f + (i % 2) * 120.0f);
                main.AddChild(e);
            }
            player.Position = new Vector2(960.0f, 800.0f);
            player.SetSinceDamage(0.0f);  // 关闭被动回血，保持各血量档稳定
            for (int i = 0; i < 30; i++)  // 等裂纹距离场烘焙与首帧稳定
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            // 1. 满血基准：MetaFX 应完全隐形（早退 + 隐藏全屏 ColorRect）
            gs.Health = 100.0;
            gs.EmitSignal(GameState.SignalName.HealthChanged, 100.0);
            await Coroutine.WaitSeconds(this, 0.3);
            await Shot("healthy");

            // 2. 受击峰值：25 伤害来自右上方 → 色差峰 + 定向波纹（峰区约 0~50ms，2 帧截图）
            player.SetInvincible(0.0f);
            player.SetLastHitFrame(-1);
            player.TakeDamage(25.0f, new Vector2(1500.0f, 350.0f));
            for (int i = 0; i < 2; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await Shot("hit");

            // 3. CAUTION（hp 60%，x=0.4）：稀疏边缘裂纹 + 轻度去饱和
            gs.Health = 60.0;
            gs.EmitSignal(GameState.SignalName.HealthChanged, 60.0);
            await Coroutine.WaitSeconds(this, 0.8);
            await Shot("caution");

            // 4. DAMAGED（hp 40%，x=0.6）：中等裂纹密度，色带转橙
            gs.Health = 40.0;
            gs.EmitSignal(GameState.SignalName.HealthChanged, 40.0);
            await Coroutine.WaitSeconds(this, 0.8);
            await Shot("damaged");

            // 5. DYING（hp 12%，x=0.88）：密集红裂、晕影收窄、强去饱和、心跳抖动
            gs.Health = 12.0;
            gs.EmitSignal(GameState.SignalName.HealthChanged, 12.0);
            await Coroutine.WaitSeconds(this, 1.0);
            await Shot("dying");

            // 6. 设置页「操作模式」：无障碍分区（减少闪光开关）
            gs.Health = 100.0;
            gs.EmitSignal(GameState.SignalName.HealthChanged, 100.0);
            var settings = GetTree().GetFirstNodeInGroup("settings_ui") as SettingsUi;
            settings!.ShowSettings();
            settings.ShowPage(new StringName("modes"));
            for (int i = 0; i < 30; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await Shot("settings_modes");

            gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            GD.PushError($"META FX CAPTURE 异常: {e}");
        }
        finally
        {
            TestExit.Quit(0);
        }
    }

    private async Task Shot(string pName)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(OUT_DIR + $"meta_fx_{pName}.png");
        GD.Print("capture saved: meta_fx_" + pName + ".png");
    }
}
