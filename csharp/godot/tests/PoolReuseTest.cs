using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 对象池复用回归测试（2026-07-22 autoplay 探针发现的泄漏修复）：
/// 4.6 实测 reparent 会触发 _exit_tree，池回收 reparent 曾导致 forget 误清 _free，
/// 子弹/敌机池只进不出（autoplay 中 BulletPool 子节点 90s 涨到 803）。
/// </summary>
public partial class PoolReuseTest : Node
{
    private int _pass;
    private int _failures;

    private void Check(bool cond, string label)
    {
        if (cond)
        {
            _pass++;
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
            gs.DeleteSave();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await TestBulletPool();
            await TestEnemyPool(gs);
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"POOL REUSE TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"[POOL] {_pass} PASS, {_failures} FAIL");
            GD.Print($"POOL REUSE TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }

    private async Task TestBulletPool()
    {
        var main = new Node2D();
        AddChild(main);
        var pool = new BulletPool(); // 随批次 A 重定型：C# 类直接 new（原 GDScript 经脚本资源实例化）
        main.AddChild(pool);
        var b1 = pool.Fire(Vector2.Down, 100.0f, 1, true)!;
        pool.Release(b1);
        Check(pool.FreeCount() == 1, "bullet: release 后 _free=1");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(pool.FreeCount() == 1, "bullet: reparent 后仍在 _free（forget 未误清）");
        Check(b1.GetParent() == pool, "bullet: 闲置弹收回池节点下");
        var b2 = pool.Fire(Vector2.Up, 100.0f, 1, false)!;
        Check(b2 == b1, "bullet: 再次 fire 复用同一实例");
        Check(pool.FreeCount() == 0 && pool.GetChildCount() == 0, "bullet: 复用后池清空");
        // 外部 queue_free 路径仍应 forget
        pool.Release(b2);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        b2.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(pool.FreeCount() == 0, "bullet: 外部销毁后 forget 生效");
        // M1（2026-08-06 审计）：self_modulate 染色残留——laser 黄/Boss 重弹橙/致死高亮红
        // 等 P0-3 写入 sprite.self_modulate 的 tint 在 _apply_faction 无对等复位，池化复用
        // 带旧 tint；模拟染色后回收再复用断言复位白
        var b3 = pool.Fire(Vector2.Down, 100.0f, 1, false)!;
        b3.SpriteNode()!.SelfModulate = new Color(1.0f, 0.85f, 0.35f); // 模拟 laser 染色写入
        pool.Release(b3);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var b4 = pool.Fire(Vector2.Down, 100.0f, 1, false)!;
        Check(b4 == b3 && b4.SpriteNode()!.SelfModulate == Colors.White, "bullet: 复用后 self_modulate 复位白（M1 染色残留）");
        main.QueueFree();
    }

    private async Task TestEnemyPool(GameState gs)
    {
        var main = new Node2D();
        AddChild(main);
        var pool = new EnemyPool(); // M3b：EnemyPool 迁 C#，typed 直接 new（原 GDScript 经脚本资源实例化）
        main.AddChild(pool);
        var config = Spawner.BuildEnemyTypes()[0]; // M6：Spawner 迁 C#，静态方法直调
        var e1 = pool.Spawn(config, new StringName("straight"), 1.0f, new Vector2(100.0f, 100.0f));
        pool.Release(e1);
        Check(pool.FreeCount() == 1, "enemy: release 后 _free=1");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(pool.FreeCount() == 1, "enemy: reparent 后仍在 _free（forget 未误清）");
        Check(e1.GetParent() == pool, "enemy: 闲置敌机收回池节点下");
        Check(!gs.EnemiesHas(e1), "enemy: 回收后注销注册表");
        var e2 = pool.Spawn(config, new StringName("straight"), 1.0f, new Vector2(200.0f, 100.0f));
        Check(e2 == e1, "enemy: 再次 spawn 复用同一实例");
        Check(gs.EnemiesHas(e2), "enemy: 复用后重新注册");
        // L02（2026-08-03 审查）：池化复用后 buff 信号必须重连——_ready 只执行一次而 _exit_tree
        // 每次 reparent 都断开连接，漏重连则 _slow_field_on 缓存冻结在陈旧值（首个回收循环后
        // slow_field buff 对该机静默失效）。白盒断言连接状态与刷新行为（E22 缓存字段；
        // buffs 为进程内存态，退出即清，无需收尾）
        Check(
            gs.IsConnected(GameState.SignalName.BuffsChanged, e2._on_buffs_changed),
            "enemy: 池化复用后 buffs_changed 保持连接（L02 slow_field 回归）"
        );
        gs.AddBuff(new StringName("slow_field"));
        Check(e2._slow_field_on, "enemy: 复用后 buff 变更即时刷新 slow_field 缓存");
        pool.Release(e2);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        main.QueueFree();
    }
}
