using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 擦弹得分测试（2026-08-03 公平感机制二，docs/archive/2026-08-03-combat-fairness-plan.md §3）：
/// 单弹进入擦弹环计 1 次分、同一弹反复进出只计 1 次、受击区（受击盒内）不计擦弹、
/// 难度倍率入账（中 ×2）、弹池复用后擦弹标志复位、宽限帧擦过既计分又无伤、
/// 弹反后弹经过玩家不计擦弹（层排除）。
/// </summary>
public partial class GrazeTest : Node
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
    private Godot.Collections.Array<Bullet> EnemyBullets(Main main)
    {
        var outArr = new Godot.Collections.Array<Bullet>();
        foreach (var child in main.GetChildren())
        {
            if (child is Bullet b && !b.IsPlayerBullet)
            {
                outArr.Add(b);
            }
        }
        return outArr;
    }

    private async Task FreeEnemyBullets(Main main)
    {
        foreach (var b in EnemyBullets(main))
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
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            AddChild(mainScene.Instantiate());
            var main = GetNode<Main>("Main");
            var player = main.Player();
            player.SetAutoFire(false); // 禁用自动开火，避免误伤与意外得分里程碑
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var spawner = main.GetNode<Spawner>("Spawner");
            spawner.SetProcess(false); // 停掉自动刷怪/Boss 调度，保证确定性
            foreach (var child in main.GetChildren())
            {
                if (child is Enemy || child is Bullet)
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            player.Position = new Vector2(960.0f, 800.0f);
            gs.Score = 0;
            var bulletPool = (BulletPool)gs.BulletPool!;

            // ================= 用例 1 + 4：单弹进入擦弹环 → 计 1 次分（中难度 ×2 = 20） =================
            Check(gs.ScoreMultiplier() == 2, "用例4：当前难度 medium 分数倍率 ×2");
            gs.Score = 0;
            ResetHitState(player);
            var g1 = bulletPool.Fire(Vector2.Down, 100.0f, 10, false)!;
            g1.Position = new Vector2(960.0f, 760.0f); // 距玩家 40px（环 r=20），0.2s 后入环
            await Coroutine.WaitSeconds(this, 0.45);
            Check(gs.Score == 20, "用例1：单弹进入擦弹环计分（10 × 难度倍率 2 = 20）");
            await FreeEnemyBullets(main);

            // ================= 用例 2：同一弹反复进出环 → 只计 1 次（_graze_done 生效） =================
            gs.Score = 0;
            ResetHitState(player);
            player.Position = new Vector2(960.0f, 800.0f);
            var g2 = bulletPool.Fire(Vector2.Down, 0.0f, 10, false)!;
            g2.Position = new Vector2(960.0f, 785.0f); // 环内、受击盒外（距玩家 15px）
            await Coroutine.WaitSeconds(this, 0.1);
            Check(gs.Score == 20, "用例2：单弹进入环计 1 次分");
            player.Position = new Vector2(1150.0f, 800.0f); // 弹在环外（距 190px）
            await Coroutine.WaitSeconds(this, 0.1);
            player.Position = new Vector2(960.0f, 800.0f); // 弹再次进入环
            await Coroutine.WaitSeconds(this, 0.1);
            Check(gs.Score == 20, "用例2：同一弹反复进出环只计 1 次");
            await FreeEnemyBullets(main);

            // ================= 用例 3：弹进入受击区（< 受击盒）→ 不计擦弹，走受击流程 =================
            gs.Score = 0;
            gs.Health = 100.0;
            ResetHitState(player);
            player.Position = new Vector2(960.0f, 800.0f);
            var g3 = bulletPool.Fire(Vector2.Down, 0.0f, 12, false)!;
            g3.Position = player.Position; // 直接生成在受击盒内（area_entered 时刻已深入）
            await Coroutine.WaitSeconds(this, 0.1);
            Check(gs.Score == 0, "用例3：弹进入受击区不计擦弹");
            Check(gs.Health == 88.0, "用例3：受击流程正常（两 Area 互不干扰）");
            await FreeEnemyBullets(main);

            // ================= 用例 5：弹池复用后擦弹标志复位 → 可再次擦弹 =================
            gs.Score = 0;
            ResetHitState(player);
            var g5 = bulletPool.Fire(Vector2.Down, 0.0f, 10, false)!;
            g5.Position = new Vector2(960.0f, 785.0f);
            await Coroutine.WaitSeconds(this, 0.1);
            Check(gs.Score == 20, "用例5：池弹擦弹计分");
            g5.Despawn(); // 回收进池
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var g5b = bulletPool.Fire(Vector2.Down, 0.0f, 10, false)!;
            g5b.Position = new Vector2(960.0f, 785.0f);
            await Coroutine.WaitSeconds(this, 0.1);
            Check(g5b == g5, "用例5：池复用取回同一实例");
            Check(gs.Score == 40, "用例5：池复用后擦弹标志复位可再计分");
            await FreeEnemyBullets(main);

            // ================= 用例 6：宽限帧擦过弹 → 既擦弹（计分）又无伤（宽限） =================
            gs.Score = 0;
            gs.Health = 100.0;
            ResetHitState(player);
            var g6 = bulletPool.Fire(Vector2.Right, 600.0f, 12, false)!;
            g6.Position = player.Position + new Vector2(-30.0f, 3.0f); // 水平弹道与环/受击盒边缘带相交
            await Coroutine.WaitSeconds(this, 0.2);
            Check(gs.Score == 20, "用例6：宽限帧擦过弹既计擦弹分");
            Check(gs.Health == 100.0, "用例6：宽限帧擦过弹无伤");
            await FreeEnemyBullets(main);

            // ================= 用例 7：弹反后弹经过玩家 → 不计擦弹（转玩家弹，层排除） =================
            gs.Score = 0;
            ResetHitState(player);
            var g7 = bulletPool.Fire(Vector2.Down, 200.0f, 10, false)!;
            g7.Position = new Vector2(960.0f, 900.0f); // 玩家下方 100px
            g7.Reflect(); // 弹反路径：转玩家弹 + 反射朝上，将穿过玩家
            await Coroutine.WaitSeconds(this, 0.6);
            Check(gs.Score == 0, "用例7：弹反后弹经过玩家不计擦弹");
            await FreeEnemyBullets(main);

            foreach (var child in main.GetChildren())
            {
                if (child is Bullet)
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Coroutine.WaitSeconds(this, 0.6); // 演出/粒子播完，避免退出时对象泄漏

            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            gs.HighScore = origHighScore;
            gs.SaveProfile();
            gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"GRAZE TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"GRAZE TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
