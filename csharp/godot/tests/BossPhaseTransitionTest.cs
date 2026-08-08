using Godot;

namespace InfiAir.Tests;

/// <summary>
/// Boss 阶段转场公平感测试（2026-08-03 机制三，docs/archive/2026-08-03-combat-fairness-plan.md §4）：
/// P1→P2 / ENRAGE 切换清弹 + 玩家转场无敌（只增不减）、转场瞬间受击不结算、
/// 逃跑期不清弹不给无敌（回归既有逃跑流程）、分段血条段权/段色登记与绘制语义
/// （segment_fill 纯函数）、非 Boss 条默认等分回归、清弹为单次遍历无逐帧轮询。
/// </summary>
public partial class BossPhaseTransitionTest : Node
{
    // M3d：Boss.FightPhase.ENRAGE 无 getter——按 Boss.cs 声明序（P1=0/P2=1/ENRAGE=2）以字面常量等价
    private const int FightEnrage = 2;

    private int _failures;
    private int _phaseSignal = -1;

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

    /// <summary>真实时间等待（不受 time_scale 影响；process_always 保证暂停时也走时）</summary>
    private async Task WaitReal(double sec)
    {
        await ToSignal(GetTree().CreateTimer(sec, true, false, true), SceneTreeTimer.SignalName.Timeout);
    }

    /// <summary>当前场内敌弹（玩家弹排除）</summary>
    private Godot.Collections.Array<Bullet> EnemyBullets()
    {
        var outBullets = new Godot.Collections.Array<Bullet>();
        foreach (var child in GetNode("Main").GetChildren())
        {
            // M3a：Bullet 为 C# 类——is Bullet 判定 + IsPlayerBullet 属性
            if (child is Bullet b && !b.IsPlayerBullet)
            {
                outBullets.Add(b);
            }
        }

        return outBullets;
    }

    private async Task FreeEnemyBullets()
    {
        foreach (var b in EnemyBullets())
        {
            b.QueueFree();
        }

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>生成 Boss 并跳过降入；调用方负责击杀/清理</summary>
    private async Task<Boss?> SpawnTestBoss(int pType)
    {
        var spawner = GetNode<Spawner>("Main/Spawner");
        spawner.SpawnBoss(pType);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Boss? boss = null;
        foreach (var child in GetNode("Main").GetChildren())
        {
            if (child is Boss b)
            {
                boss = b;
            }
        }

        if (boss != null)
        {
            // 跳过降入（锚线 = view 顶缘 + FIGHT_Y），下一物理帧进入战斗
            boss.Position = new Vector2(boss.Position.X, boss.FightAnchorY());
        }

        return boss;
    }

    public override void _Ready()
    {
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        var gs = GetNode<GameState>("/root/GameState");
        // L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
        var origHighScore = gs.HighScore;
        try
        {
            // 清理持久化状态，保证测试确定性
            gs.DeleteSave();
            gs.HighScore = 0;
            gs.SaveProfile();
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest(); // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            var main = GetNode<Main>("Main");
            var player = GetNode<Player>("Main/Player");
            player.SetAutoFire(false); // 全程禁用全自动开火，避免误杀 Boss/触发里程碑
            player.SetInvincible(999.0f); // 用例间兜底（各用例自行重置）
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var spawner = GetNode<Spawner>("Main/Spawner");
            spawner.SetProcess(false); // 停掉自动刷怪/Boss 调度，保证确定性
            player.Position = new Vector2(960.0f, 540.0f);

            // ================= 场景 1：P1→P2 切换清弹 + phase_changed + 持续攻击复位 + 转场无敌 =================
            var boss = await SpawnTestBoss(1);
            Check(boss != null, "场景1：Boss 已生成");
            boss!.PhaseChanged += p => _phaseSignal = p;
            boss!.SetPatterns(new Godot.Collections.Dictionary
            {
                ["p1"] = new Godot.Collections.Array
                {
                    new Godot.Collections.Dictionary { ["attack"] = new StringName("sniper3"), ["waves"] = 1, ["interval"] = 0.5 },
                },
                ["p2"] = new Godot.Collections.Array
                {
                    new Godot.Collections.Dictionary { ["attack"] = new StringName("fan5"), ["waves"] = 1, ["interval"] = 1.6 },
                },
            });
            boss!.SetFireTimer(0.1f);
            player.SetInvincible(0.0f);
            player.SetLastHitFrame(-1);
            var pool = (BulletPool)gs.BulletPool!;
            var pb1 = pool.Fire(Vector2.Down, 0.0f, 10, false)!;
            pb1.Position = new Vector2(400.0f, 200.0f);
            var pb2 = pool.Fire(Vector2.Down, 0.0f, 10, false)!;
            pb2.Position = new Vector2(600.0f, 300.0f);
            // P1→P2：打到 65%（≤70% 阈值）
            boss!.TakeDamage((int)(boss.MaxHp * 0.35f));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(boss!.FightPhaseValue() == boss.GetFightPhaseActive(), "场景1：HP ≤70% 进入 P2");
            Check(_phaseSignal == boss.GetFightPhaseActive(), "场景1：段切换发出 phase_changed");
            Check(EnemyBullets().Count == 0, "场景1：转场清弹——活跃敌弹数归零");
            Check(player.InvincibleRemaining() > 0.8f, $"场景1：转场给玩家短暂无敌（{boss.TransitionInvincible}s）");

            // ================= 场景 2：转场瞬间玩家受击 → 无敌期内不结算 =================
            gs.Health = 100.0;
            player.SetLastHitFrame(-1);
            var hitB = pool.Fire(Vector2.Down, 0.0f, 12, false)!;
            hitB.Position = player.Position;
            await Coroutine.WaitSeconds(this, 0.2);
            Check(gs.Health == 100.0, "场景2：转场无敌期内受击不结算");
            Check(hitB.Visible, "场景2：无敌期弹不销毁（穿过语义）");
            await FreeEnemyBullets();

            // ================= 场景 3：ENRAGE 触发（<30%）→ 同样清弹 + 无敌 =================
            player.SetInvincible(0.0f);
            player.SetLastHitFrame(-1);
            gs.Health = 100.0;
            var eb1 = pool.Fire(Vector2.Down, 0.0f, 10, false)!;
            eb1.Position = new Vector2(400.0f, 200.0f);
            var eb2 = pool.Fire(Vector2.Down, 0.0f, 10, false)!;
            eb2.Position = new Vector2(600.0f, 300.0f);
            boss!.TakeDamage((int)(boss.MaxHp * 0.4f)); // P2 内打到 25% → 钳 30% 触发狂暴
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(boss!.IsEnraged() && boss.FightPhaseValue() == FightEnrage, "场景3：HP <30% 进入 ENRAGE");
            Check(EnemyBullets().Count == 0, "场景3：ENRAGE 转场清弹");
            Check(player.InvincibleRemaining() > 0.8f, "场景3：ENRAGE 转场给玩家无敌");
            // 快进 main 子弹时间等恢复（仿 boss_phase_test）
            main.SetBulletTime(0.05f);
            for (int i = 0; i < 40; i++)
            {
                await WaitReal(0.1);
                if (Mathf.IsEqualApprox(Engine.TimeScale, 1.0f))
                {
                    break;
                }
            }
            boss!.AbortEnrageSequence();
            boss!.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!GodotObject.IsInstanceValid(boss), "场景3：狂暴序列解除后击杀清理");
            await FreeEnemyBullets();

            // ================= 场景 4：逃跑期（50s 超时）→ 不清弹、不给无敌 =================
            var boss4 = await SpawnTestBoss(1);
            Check(boss4 != null, "场景4：Boss 已生成");
            boss4!.SetFireTimer(999.0f); // 屏蔽开火，保持场内干净
            player.SetInvincible(0.0f);
            player.SetLastHitFrame(-1);
            var escB1 = pool.Fire(Vector2.Down, 0.0f, 10, false)!;
            escB1.Position = new Vector2(400.0f, 200.0f);
            boss4!.SetSurvival(boss4.EscapeTime); // 直接到 50s 点
            await Coroutine.WaitSeconds(this, 0.2); // 等物理帧过降入 + 到点判定（position 同步延迟一帧）
            Check(boss4!.IsEscaping(), "场景4：50s 到点进入逃跑流程");
            Check(EnemyBullets().Count == 1, "场景4：逃跑不清弹（敌弹保留）");
            Check(player.InvincibleRemaining() == 0.0f, "场景4：逃跑不给玩家无敌");
            await Coroutine.WaitSeconds(this, 1.5); // Boss 上飘离场（escaped/died 清理）
            await FreeEnemyBullets();

            // ================= 场景 5：分段血条——绘制语义（segment_fill 纯函数）+ HUD 登记 =================
            var w = new Godot.Collections.Array { 0.3, 0.4, 0.3 };
            Check(
                Mathf.IsEqualApprox(SegmentedBar.SegmentFill(1.0f, w, 0), 0.0f) && Mathf.IsEqualApprox(SegmentedBar.SegmentFill(1.0f, w, 2), 0.0f),
                "场景5：满血全部段未消耗（全亮）"
            );
            Check(Mathf.IsEqualApprox(SegmentedBar.SegmentFill(0.85f, w, 0), 0.5f), "场景5：P1 段消耗度 (1-0.85)/0.3（P1 段消耗后暗化）");
            Check(Mathf.IsEqualApprox(SegmentedBar.SegmentFill(0.85f, w, 1), 0.0f), "场景5：P2 段未消耗（当前段高亮语义）");
            Check(Mathf.IsEqualApprox(SegmentedBar.SegmentFill(0.85f, w, 2), 0.0f), "场景5：ENRAGE 段未消耗");
            Check(Mathf.IsEqualApprox(SegmentedBar.SegmentFill(0.7f, w, 0), 1.0f), "场景5：HP=70% P1 段全暗");
            Check(Mathf.IsEqualApprox(SegmentedBar.SegmentFill(0.5f, w, 1), 0.5f), "场景5：P2 段消耗度 (0.7-0.5)/0.4");
            Check(Mathf.IsEqualApprox(SegmentedBar.SegmentFill(0.2f, w, 2), 1.0f / 3.0f), "场景5：ENRAGE 段消耗度 (0.3-0.2)/0.3");
            Check(Mathf.IsEqualApprox(SegmentedBar.SegmentFill(0.0f, w, 2), 1.0f), "场景5：HP=0 ENRAGE 段全暗");
            // HUD 登记：spawner 出场 Boss 时已调 show_boss_bar（场景 1-4 同路径）
            var boss5 = (await SpawnTestBoss(1))!;
            boss5.SetFireTimer(999.0f);
            var bb = GetNode<SegmentedBar>("Main/HUD/BossBar");
            Check(
                bb.SegWeights.Count == 3 && Mathf.IsEqualApprox((float)bb.SegWeights[0].AsDouble(), 0.3f) && Mathf.IsEqualApprox((float)bb.SegWeights[2].AsDouble(), 0.3f),
                "场景5：BossBar 段权 [0.3,0.4,0.3] 登记"
            );
            Check(bb.SegColors.Count == 3 && bb.Segments == 3, "场景5：段色 3 段 + 段数 3 登记");
            boss5.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await FreeEnemyBullets();

            // ================= 场景 6：非 Boss 场景——HP/燃料/dash 条分段不变（默认等分） =================
            var hpBar = GetNode<SegmentedBar>("Main/HUD/HpBar");
            var fuelBar = GetNode<SegmentedBar>("Main/HUD/FuelBar");
            var dashBar = GetNode<SegmentedBar>("Main/HUD/DashBar");
            Check(
                hpBar.SegWeights.Count == 0 && fuelBar.SegWeights.Count == 0 && dashBar.SegWeights.Count == 0,
                "场景6：非 Boss 条默认等分（seg_weights 空，绘制走既有逻辑）"
            );

            // ================= 场景 7：清弹为单次遍历——切换后新弹不被自动清（无逐帧轮询） =================
            var boss7 = (await SpawnTestBoss(1))!;
            boss7.SetFireTimer(999.0f);
            player.SetInvincible(0.0f);
            player.SetLastHitFrame(-1);
            var n1 = pool.Fire(Vector2.Down, 0.0f, 10, false)!;
            n1.Position = new Vector2(400.0f, 200.0f);
            boss7.TakeDamage((int)(boss7.MaxHp * 0.35f)); // P1→P2 转场清弹
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(EnemyBullets().Count == 0, "场景7：切换瞬间清弹");
            var n2 = pool.Fire(Vector2.Down, 0.0f, 10, false)!;
            n2.Position = new Vector2(400.0f, 200.0f);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check(EnemyBullets().Count == 1, "场景7：切换后新弹保留（清弹为单次遍历，无逐帧轮询）");
            boss7.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await FreeEnemyBullets();

            Check(Mathf.IsEqualApprox(Engine.TimeScale, 1.0f), "收尾：退出前 time_scale = 1.0");
            foreach (var child in GetNode("Main").GetChildren())
            {
                if (child is Bullet)
                {
                    child.QueueFree();
                }
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await WaitReal(2.0); // 演出 tween/爆炸序列播完，避免退出时对象泄漏
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"BOSS PHASE TRANSITION TEST 异常: {e}");
        }
        finally
        {
            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            gs.HighScore = origHighScore;
            gs.SaveProfile();
            GD.Print($"BOSS PHASE TRANSITION TEST DONE, failures = {_failures}");
            gs.DeleteSave();
            TestExit.Quit(_failures);
        }
    }
}
