using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 受击宽限帧测试（2026-08-03 公平感机制一，docs/archive/2026-08-03-combat-fairness-plan.md §2）：
/// 敌弹进入玩家 Hitbox 暂缓结算——窗口内离开（擦过边缘）不计伤（ghost hit 消灭）、
/// 停留超窗结算且只一次、窗口边界两侧语义、既有受击流程（无敌/清弹/致死高亮）回归、
/// 无敌期守卫不变、窗口期回收无悬挂 Timer。
/// </summary>
public partial class GracePeriodTest : Node
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

    /// <summary>当前场内敌弹（玩家弹排除）</summary>
    private Godot.Collections.Array<Bullet> EnemyBullets()
    {
        var result = new Godot.Collections.Array<Bullet>();
        foreach (var child in GetNode<Main>("Main").GetChildren())
        {
            if (child is Bullet b && !b.IsPlayerBullet)
            {
                result.Add(b);
            }
        }
        return result;
    }

    private async Task FreeEnemyBullets()
    {
        foreach (var b in EnemyBullets())
        {
            b.QueueFree();
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>重置玩家受击状态（无敌/帧标记/被动回血计时），便于逐条断言</summary>
    private void ResetHitState(Player player)
    {
        player.SetInvincible(0.0f);
        player.SetLastHitFrame(-1);
        player.SetSinceDamage(999.0f);
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

            // 清理持久化状态，保证测试确定性
            gs.DeleteSave();
            // L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
            var origHighScore = gs.HighScore;
            gs.HighScore = 0;
            gs.SaveProfile();
            AddChild(GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate());
            var main = GetNode<Main>("Main");
            var player = GetNode<Player>("Main/Player");
            player.SetAutoFire(false);  // 禁用自动开火，避免误伤与意外得分里程碑
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var spawner = GetNode<Spawner>("Main/Spawner");
            spawner.SetProcess(false);  // 停掉自动刷怪/Boss 调度，保证确定性
            var pool = (BulletPool)gs.BulletPool! ?? throw new InvalidOperationException("bullet_pool 未登记（main 实例化后）");
            foreach (var child in main.GetChildren())
            {
                if (child is Enemy || child is Bullet)
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            player.Position = new Vector2(960.0f, 800.0f);

            // ================= 用例 1：切向快速穿过（停留 < 窗口）→ 无伤（area_exited 取消 Timer） =================
            gs.Health = 100.0;
            ResetHitState(player);
            Bullet edgeB = pool.Fire(Vector2.Right, 600.0f, 12, false)!;
            edgeB.Position = player.Position + new Vector2(-30.0f, 3.0f);  // 水平弹道与 Hitbox 边缘带相交（y 偏移 3px）
            await Coroutine.WaitSeconds(this, 0.2);
            Check(gs.Health == 100.0, "用例1：切向快速穿过（停留 << 窗口）不计伤");
            await FreeEnemyBullets();

            // ================= 用例 2：停留 ≥ 窗口 → 受击 1 次且只 1 次 =================
            gs.Health = 100.0;
            ResetHitState(player);
            Bullet stayB = pool.Fire(Vector2.Down, 0.0f, 12, false)!;
            stayB.Position = player.Position;
            await Coroutine.WaitSeconds(this, 0.1);
            Check(gs.Health == 88.0, "用例2：停留 ≥ 窗口结算一次（-12）");
            await Coroutine.WaitSeconds(this, 0.2);
            Check(gs.Health == 88.0, "用例2：只结算一次（Timer 一次性 + 受击无敌）");
            await FreeEnemyBullets();

            // ================= 用例 3：窗口边界两侧——< 窗口无伤 / ≥ 窗口有伤 =================
            gs.Health = 100.0;
            ResetHitState(player);
            Bullet bdry = pool.Fire(Vector2.Down, 0.0f, 12, false)!;
            bdry.Position = player.Position;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);  // ≈0.033s < 0.05s 窗口
            Check(gs.Health == 100.0, "用例3：窗口内（<0.05s）未结算");
            await Coroutine.WaitSeconds(this, 0.1);
            Check(gs.Health == 88.0, "用例3：停留超窗（≥0.05s）结算");
            await FreeEnemyBullets();

            // ================= 用例 4：宽限结算后仍走既有受击流程（无敌计时 + 清弹） =================
            gs.Health = 100.0;
            ResetHitState(player);
            Bullet near1 = pool.Fire(Vector2.Down, 0.0f, 10, false)!;
            near1.Position = player.Position + new Vector2(100.0f, 0.0f);  // 受击清弹半径 250 内
            Bullet hitB = pool.Fire(Vector2.Down, 0.0f, 10, false)!;
            hitB.Position = player.Position;
            await Coroutine.WaitSeconds(this, 0.1);
            Check(gs.Health == 90.0, "用例4：宽限结算走既有受击链路（-10）");
            Check(player.InvincibleRemaining() > 1.0f, "用例4：结算后受击无敌计时生效");
            Check(!near1.Visible, "用例4：结算后 250px 内敌弹被清（既有清弹语义）");
            await FreeEnemyBullets();

            // ================= 用例 5：无敌期内弹进入 → 不结算（take_damage 守卫回归） =================
            gs.Health = 100.0;
            ResetHitState(player);
            player.SetInvincible(1.0f);
            Bullet invB = pool.Fire(Vector2.Down, 0.0f, 12, false)!;
            invB.Position = player.Position;
            await Coroutine.WaitSeconds(this, 0.12);
            Check(gs.Health == 100.0, "用例5：无敌期内停留超窗也不结算");
            Check(invB.Visible, "用例5：无敌期弹不被销毁（穿过语义不变）");
            await FreeEnemyBullets();
            player.SetInvincible(0.0f);

            // ================= 用例 6：窗口期内弹被清弹/离屏回收 → 无悬挂 Timer =================
            gs.Health = 100.0;
            ResetHitState(player);
            Bullet reapB = pool.Fire(Vector2.Down, 0.0f, 12, false)!;
            reapB.Position = player.Position;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);  // 已进入宽限期（Timer 启动）
            reapB.Despawn();  // 受击清弹/离屏回收同款路径
            await Coroutine.WaitSeconds(this, 0.12);
            Check(gs.Health == 100.0, "用例6：窗口期回收后无悬挂结算");
            await FreeEnemyBullets();

            // ================= 用例 4b：致死结算 → 死亡流程 + 既有清弹语义（无悬挂） =================
            // 注：受击清弹（clear_nearby_enemy_bullets，250px）会回收结算弹自身——既有语义
            // （计划书 §2.3 明确预期「玩家受击清弹」despawn 宽限期弹），P2-10 致死高亮残留
            // 仅覆盖清弹半径外等罕见路径；此处验证致死结算与回收链路本身。
            gs.Health = 12.0;
            ResetHitState(player);
            player.SetSinceDamage(0.0f);  // 关闭被动回血（对齐 hit_logic A5 语义），保证致死精确
            Bullet fatalB = pool.Fire(Vector2.Down, 0.0f, 12, false)!;
            fatalB.Position = player.Position;
            await Coroutine.WaitSeconds(this, 0.1);
            Check(gs.Health == 0.0 && player.IsDead(), "用例4b：宽限结算致死归零并进入死亡流程");
            Check(!fatalB.IsActive(), "用例4b：结算弹按既有受击清弹语义回收");
            await Coroutine.WaitSeconds(this, 0.7);
            Check(!fatalB.IsActive() && !fatalB.Visible, "用例4b：回收后状态保持（无悬挂 Timer/重入）");

            // 复活玩家供清场收尾（复用既有公开接口）
            player.SetDead(false);
            player.Show();
            player.SetPhysicsProcess(true);
            gs.Health = 100.0;
            foreach (var child in main.GetChildren())
            {
                if (child is Bullet)
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Coroutine.WaitSeconds(this, 0.6);  // 演出/高亮 tween 播完，避免退出时对象泄漏

            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            gs.HighScore = origHighScore;
            gs.SaveProfile();
            gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"GRACE PERIOD TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"GRACE PERIOD TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
