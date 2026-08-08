using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 统一实体管理器集成测试（docs/ENTITY_MANAGER.md；M7c 迁移自 test/entity_manager_test.gd）：
/// 场景1 绑定样板：bind_enemy 一行（入 enemy 组 + 注册表 + 幂等），unbind_enemy 对称退出。
/// 场景2 生命周期信号：entity_registered / entity_unregistered 各触发一次。
/// 场景3 批量 API：count_enemies 计数、for_each_enemy 谓词过滤、clear_enemies 保留项清除
///    （真实敌机池化实例；清除后注册表注销、池回收语义保持）。
/// </summary>
public partial class EntityManagerTest : Node
{
    private int _failures;
    private Godot.Collections.Array<Node> _visited = new();

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

    private bool IsEnemy(Node e)
    {
        return e is Enemy;  // 原 GDScript：is_instance_of(e, load("res://csharp/godot/Enemy.cs"))
    }

    private void CollectVisited(Node e)
    {
        _visited.Add(e);
    }

    private bool NotKeep(Node e)
    {
        return !e.HasMeta("keep");
    }

    private bool HasKeep(Node e)
    {
        return e.HasMeta("keep");
    }

    public override void _Ready()
    {
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        GameState? gs = null;
        try
        {
            gs = GetNode<GameState>("/root/GameState");
            gs.DeleteSave();
            gs.SetDifficulty("medium");
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest();
            AddChild(mainScene.Instantiate());
            var player = GetNode<Player>("Main/Player");
            player.SetAutoFire(false);
            player.SetInvincible(999.0f);
            player.Position = new Vector2(960.0f, 800.0f);
            gs.SetMilestoneOverride(999999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var spawner = GetNode<Spawner>("Main/Spawner");
            spawner.SetProcess(false);  // 手动驱动，保证确定性

            // ================= 场景 1：绑定样板 + 幂等 =================
            var probe = new Node();
            AddChild(probe);
            gs.BindEnemy(probe);
            Check(gs.Enemies.Contains(probe), "场景1：bind_enemy 后进入注册表");
            Check(probe.IsInGroup("enemy"), "场景1：bind_enemy 同步加入 enemy 组");
            gs.BindEnemy(probe);
            var count = 0;
            foreach (var n in gs.Enemies)
            {
                if (n == probe)
                {
                    count += 1;
                }
            }
            Check(count == 1, "场景1：重复绑定幂等（注册表单条）");
            gs.UnbindEnemy(probe);
            Check(!gs.Enemies.Contains(probe), "场景1：unbind_enemy 后退出注册表");
            probe.QueueFree();

            // ================= 场景 2：生命周期信号 =================
            var sigSeen = new Godot.Collections.Array<string>();
            void OnRegistered(Node n) => sigSeen.Add("reg");
            void OnUnregistered(Node n) => sigSeen.Add("unreg");
            gs.EntityRegistered += OnRegistered;
            gs.EntityUnregistered += OnUnregistered;
            var probe2 = new Node();
            AddChild(probe2);
            gs.BindEnemy(probe2);
            gs.UnbindEnemy(probe2);
            Check(
                sigSeen.Count == 2 && sigSeen[0] == "reg" && sigSeen[1] == "unreg",
                "场景2：注册/解绑信号各触发一次");
            gs.EntityRegistered -= OnRegistered;
            gs.EntityUnregistered -= OnUnregistered;
            probe2.QueueFree();

            // ================= 场景 3：批量 API（真实敌机池化实例） =================
            var config = spawner.ENEMY_TYPES[0];
            var pool = (EnemyPool)gs.EnemyPool!;  // M3b：enemy_pool 为 GodotObject 承载，转型后 typed 调用
            var e1 = pool.Spawn(config, "straight", 1.0f, new Vector2(400.0f, 500.0f));
            var e2 = pool.Spawn(config, "straight", 1.0f, new Vector2(520.0f, 500.0f));
            e2.SetMeta("keep", true);  // clear_enemies 保留项标记（模拟 Boss 语义）
            Check(gs.Enemies.Contains(e1) && gs.Enemies.Contains(e2), "场景3：池化生成实例注册进注册表");
            Check(gs.CountEnemies(Callable.From<Node, bool>(IsEnemy)) == 2, "场景3：count_enemies 计数 = 2");
            _visited.Clear();
            gs.ForEachEnemy(
                Callable.From<Node>(CollectVisited),
                Callable.From<Node, bool>(NotKeep));
            Check(_visited.Count == 1 && _visited[0] == e1, "场景3：for_each_enemy 谓词过滤（排除保留项）");
            // 失效实例跳过：queue_free 一帧后注册表可能仍持有（帧末释放），先确认 for_each 不崩
            var before = gs.CountEnemies();
            Check(before >= 2, "场景3：清理前注册表 ≥2（P4：基准断言前置，原在清理后检查失去意义）");
            var cleared = gs.ClearEnemies(Callable.From<Node, bool>(HasKeep));
            Check(cleared == 1, "场景3：clear_enemies 清除 1 个（保留 keep 项）");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(gs.CountEnemies() == 1, "场景3：清除后注册表仅剩保留项");
            Check(gs.EnemiesHas(e2), "场景3：保留项 e2 仍在注册表");
            // 清理遗留：e2 释放（enemy._exit_tree 自动从池清单移除，幂等）
            e2.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"ENTITY MANAGER TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"ENTITY MANAGER TEST DONE, failures = {_failures}");
            gs?.DeleteSave();
            TestExit.Quit(_failures);
        }
    }
}
