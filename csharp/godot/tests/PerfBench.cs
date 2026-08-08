using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 性能基准：固定压力场景（main + 200 敌机 + 玩家强制开火 + 每 20 物理帧一次爆炸），
/// 跑 1800 物理帧统计平均帧耗时。headless + 高物理频率下帧耗时≈纯 CPU 成本。
/// 用法：godot --headless --path . res://test/perf_bench.tscn
/// </summary>
public partial class PerfBench : Node
{
    private const int FRAMES = 1800;
    private const int ENEMY_COUNT = 200;
    private const int EXPLOSION_EVERY = 20;  // 每 20 物理帧一次爆炸（标准 60Hz 下≈每秒 3 次）
    private const int FIRE_EVERY = 5;  // 每 5 物理帧强制齐射一次（制造子弹分配/回收压力）

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
            gs.DeleteSave();
            gs.ResetRun();
            // 物理帧率拉满，让循环 CPU 受限，测纯耗时
            Engine.PhysicsTicksPerSecond = 1000;
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            AddChild(mainScene.Instantiate());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var main = GetNode<Main>("Main");
            var spawner = main.GetNode<Spawner>("Spawner");
            spawner.SetProcess(false);  // 自己控制刷怪节奏
            // 200 只敌机（各机型/策略混合，部分可开火）——L10：注释口径同步（原「30 只」落后于常量）
            for (int i = 0; i < ENEMY_COUNT; i++)
            {
                var cfg = spawner.ENEMY_TYPES[i % spawner.ENEMY_TYPES.Count];
                var e = GD.Load<PackedScene>("res://scenes/enemy.tscn").Instantiate<Enemy>();  // M3b：Enemy 迁 C#，移除 as 断言
                var strategies = cfg["strategies"].AsGodotArray();
                e.Setup(cfg, strategies[(int)(GD.Randi() % (uint)strategies.Count)].AsString(), 1.0f);
                e.Position = new Vector2((float)GD.RandRange(60.0f, 1860.0f), (float)GD.RandRange(-400.0f, 800.0f));
                main.AddChild(e);
            }
            // 玩家强制开火（auto fire 默认开，另外定期手动齐射制造弹丸压力）
            var player = main.GetNode<Player>("Player");  // M3c：Player 迁 C#，不能作类型注解
            var t0 = Time.GetTicksMsec();
            for (int i = 0; i < FRAMES; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                if (i % EXPLOSION_EVERY == 0)
                {
                    // 随批次 D 重定型：Explosion 已迁 C#（spawn_at→SpawnAt），静态方法经脚本资源调用
                    Explosion.SpawnAt(main, new Vector2((float)GD.RandRange(200.0f, 1700.0f), (float)GD.RandRange(200.0f, 800.0f)), 1.0f);
                }
                if (i % FIRE_EVERY == 0)
                {
                    player.Fire(Vector2.Up.Rotated((float)GD.RandRange(-0.6f, 0.6f)));
                }
                if (i % 10 == 0)
                {
                    // 敌机生成churn（走 spawner.spawn_minion，优化前后同一代码路径）
                    spawner.SpawnMinion(new Vector2((float)GD.RandRange(60.0f, 1860.0f), -60.0f));
                }
            }
            var elapsed = Time.GetTicksMsec() - t0;
            var avg = (double)elapsed / FRAMES;
            GD.Print($"PERF_RESULT frames={FRAMES} total_ms={elapsed} avg_frame_ms={avg:0.000} equivalent_fps={1000.0 / avg:0.0}");
            Engine.PhysicsTicksPerSecond = 60;
            gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            GD.PushError($"PERF BENCH 异常: {e}");
        }
        finally
        {
            TestExit.Quit(0);
        }
    }
}
