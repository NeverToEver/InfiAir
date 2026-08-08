using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 敌机/Boss 战斗行为测试：弹种（single/spread/laser）与机型约束、spread 同屏上限、
/// aggressive 追踪收敛、敌机 15s 寿命离场、Boss 50s 逃跑无奖励。
/// </summary>
public partial class EnemyCombatTest : Node
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

    /// <summary>生成测试敌机（默认停火，位置/弹种由调用方覆盖；pDifficulty 供 H2 难度继承断言）。</summary>
    private Enemy SpawnTestEnemy(Godot.Collections.Dictionary config, StringName strategy, float pDifficulty = 1.0f)
    {
        var e = GD.Load<PackedScene>("res://scenes/enemy.tscn").Instantiate<Enemy>();
        e.Setup(config, strategy, pDifficulty);
        e.CanShoot = false;
        GetNode<Main>("Main").AddChild(e);
        return e;
    }

    /// <summary>当前场内敌弹（玩家弹排除）。</summary>
    private Godot.Collections.Array<Bullet> EnemyBullets()
    {
        var outArr = new Godot.Collections.Array<Bullet>();
        foreach (Node child in GetNode<Main>("Main").GetChildren())
        {
            if (child is Bullet b && !b.IsPlayerBullet)
            {
                outArr.Add(b);
            }
        }

        return outArr;
    }

    private void FreeEnemyBullets()
    {
        foreach (var b in EnemyBullets())
        {
            b.QueueFree();
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
            // 清理持久化状态，保证测试确定性
            gs.DeleteSave();
            // L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
            var origHighScore = gs.HighScore;
            gs.HighScore = 0;
            gs.SaveProfile();
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest(); // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            var main = GetNode<Main>("Main");
            // 开场面板自显即暂停（冻结背景），先关闭解除
            var player = GetNode<Player>("Main/Player");
            player.SetAutoFire(false); // 禁用自动开火，避免误伤与意外得分里程碑
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var spawner = GetNode<Spawner>("Main/Spawner");
            spawner.SetProcess(false); // 停掉自动刷怪/Boss 调度，保证确定性
            player.SetInvincible(999.0f); // 弹幕流弹不干扰流程
            player.Position = new Vector2(960.0f, 800.0f);

            // 1. 弹种配置表约束：普通机仅 single/spread，精英仅 spread/laser
            var normalPoolOk = true;
            foreach (var t in spawner.ENEMY_TYPES)
            {
                foreach (var bt in t["bullet_types"].AsGodotArray())
                {
                    var btName = bt.AsStringName();
                    if (btName != "single" && btName != "spread")
                    {
                        normalPoolOk = false;
                    }
                }
            }

            Check(normalPoolOk, "普通机型弹种池仅 single/spread");
            var elitePoolOk = true;
            foreach (var t in spawner.ELITE_TYPES)
            {
                foreach (var bt in t["bullet_types"].AsGodotArray())
                {
                    var btName = bt.AsStringName();
                    if (btName != "spread" && btName != "laser")
                    {
                        elitePoolOk = false;
                    }
                }
            }

            Check(elitePoolOk, "精英机型弹种池仅 spread/laser");

            // 2. spread 敌机发射五向扇形弹
            var spreadE = SpawnTestEnemy(spawner.ENEMY_TYPES[0], "straight");
            spreadE.bullet_type = "spread";
            spreadE.CanShoot = true;
            spreadE.fire_interval = 5.0f; // 只放一轮
            spreadE.SetFireTimer(0.1f);
            spreadE.Position = new Vector2(960.0f, 300.0f);
            await Coroutine.WaitSeconds(this, 0.5);
            var fan = new Godot.Collections.Array<Bullet>();
            foreach (var b in EnemyBullets())
            {
                if (b.HasMeta("bullet_type") && b.GetMeta("bullet_type").AsStringName() == "spread")
                {
                    fan.Add(b);
                }
            }

            Check(fan.Count == 5, "spread 敌机一次发射 5 向弹");
            if (fan.Count == 5)
            {
                var angles = new Godot.Collections.Array<float>();
                foreach (var b in fan)
                {
                    angles.Add(b.Direction.Angle());
                }

                angles.Sort();
                var fanOk = true;
                for (int i = 0; i < 4; i++)
                {
                    if (Mathf.Abs(angles[i + 1] - angles[i] - spreadE.SPREAD_FAN_STEP) > 0.02f)
                    {
                        fanOk = false;
                    }
                }

                Check(fanOk, "spread 弹向以瞄准方向为中心均匀扇形展开");
                Check(
                    Mathf.IsEqualApprox(fan[0].Speed, spreadE.SPREAD_BULLET_SPEED) && fan[0].Speed < spreadE.ENEMY_BULLET_SPEED,
                    "spread 弹速稍慢于普通弹");
            }

            spreadE.QueueFree();
            FreeEnemyBullets();

            // 3. laser 弹：细长高亮快速弹
            var laserE = SpawnTestEnemy(spawner.ELITE_TYPES[1], "straight");
            laserE.bullet_type = "laser";
            laserE.CanShoot = true;
            laserE.fire_interval = 5.0f;
            laserE.SetFireTimer(0.1f);
            laserE.Position = new Vector2(960.0f, 300.0f);
            await Coroutine.WaitSeconds(this, 0.4);
            var lasers = new Godot.Collections.Array<Bullet>();
            foreach (var b in EnemyBullets())
            {
                if (b.HasMeta("bullet_type") && b.GetMeta("bullet_type").AsStringName() == "laser")
                {
                    lasers.Add(b);
                }
            }

            Check(lasers.Count == 1, "laser 敌机发射单发弹");
            if (lasers.Count == 1)
            {
                Check(lasers[0].Speed > laserE.ENEMY_BULLET_SPEED, "laser 弹速显著更快");
                Check(lasers[0].SpriteNode()!.Scale.X > 1.5f, "laser 弹细长化表现");
            }

            laserE.QueueFree();
            FreeEnemyBullets();

            // 4. 抽取约束：普通机只出 single/spread，精英只出 spread/laser
            var normalPickOk = true;
            foreach (var t in spawner.ENEMY_TYPES)
            {
                for (int i = 0; i < 20; i++)
                {
                    var bt = spawner.PickBulletType(t);
                    if (bt != "single" && bt != "spread")
                    {
                        normalPickOk = false;
                    }
                }
            }

            Check(normalPickOk, "普通机抽取弹种仅 single/spread");
            var elitePickOk = true;
            foreach (var t in spawner.ELITE_TYPES)
            {
                for (int i = 0; i < 20; i++)
                {
                    var bt2 = spawner.PickBulletType(t);
                    if (bt2 != "spread" && bt2 != "laser")
                    {
                        elitePickOk = false;
                    }
                }
            }

            Check(elitePickOk, "精英抽取弹种仅 spread/laser");

            // 5. spread 同屏上限 2：超限退化（普通→single，精英→laser）
            var cap1 = SpawnTestEnemy(spawner.ENEMY_TYPES[0], "straight");
            cap1.bullet_type = "spread";
            cap1.Position = new Vector2(500.0f, 300.0f);
            var cap2 = SpawnTestEnemy(spawner.ENEMY_TYPES[0], "straight");
            cap2.bullet_type = "spread";
            cap2.Position = new Vector2(1400.0f, 300.0f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(spawner.CountSpreadEnemies() == 2, "同屏 spread 敌机计数");
            var capNormalOk = true;
            for (int i = 0; i < 10; i++)
            {
                if (spawner.PickBulletType(spawner.ENEMY_TYPES[1]) != "single")
                {
                    capNormalOk = false;
                }
            }

            Check(capNormalOk, "spread 同屏上限 2：普通机退化为 single");
            var capEliteOk = true;
            for (int i = 0; i < 10; i++)
            {
                if (spawner.PickBulletType(spawner.ELITE_TYPES[2]) != "laser")
                {
                    capEliteOk = false;
                }
            }

            Check(capEliteOk, "spread 同屏上限 2：精英退化为 laser");
            cap1.QueueFree();
            cap2.QueueFree();

            // 6. aggressive：噪声漂移 + 持续偏向玩家 x 的下行
            player.Position = new Vector2(400.0f, 900.0f);
            var agg = SpawnTestEnemy(spawner.ENEMY_TYPES[3], "aggressive");
            agg.Position = new Vector2(1400.0f, 200.0f);
            var aggX0 = agg.Position.X;
            var aggY0 = agg.Position.Y;
            await Coroutine.WaitSeconds(this, 2.0);
            Check(agg.Position.X < aggX0 - 150.0f, "aggressive 持续偏向玩家 x 收敛");
            Check(agg.Position.Y > aggY0, "aggressive 保持下行");
            agg.QueueFree();

            // 7. 敌机 15s 寿命：到期向上/侧方加速离场，不给分不计击杀
            var scoreBeforeLife = gs.Score;
            var killsBeforeLife = gs.Kills;
            var lifeE = SpawnTestEnemy(spawner.ENEMY_TYPES[0], "straight");
            lifeE.Position = new Vector2(960.0f, 300.0f);
            lifeE.SetLifeTimer(14.8f);
            await Coroutine.WaitSeconds(this, 0.4);
            Check(lifeE.IsExiting(), "敌机 15s 寿命到期进入离场");
            var exitP0 = lifeE.Position;
            await Coroutine.WaitSeconds(this, 0.4);
            Check(
                lifeE.Position.Y < exitP0.Y - 20.0f || Mathf.Abs(lifeE.Position.X - exitP0.X) > 20.0f,
                "离场向上或侧方加速");
            await Coroutine.WaitSeconds(this, 3.0);
            Check(!GodotObject.IsInstanceValid(lifeE), "离场后销毁");
            Check(gs.Score == scoreBeforeLife && gs.Kills == killsBeforeLife, "离场不给分不计击杀");

            // 8. Boss 逃跑：50s 未击杀 → 最后 3s 警告 + 上飘 → 离场无奖励
            spawner.SpawnBoss(1);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Boss? boss = null;
            foreach (Node child in main.GetChildren())
            {
                if (child is Boss b)
                {
                    boss = b;
                    break;
                }
            }

            Check(boss != null, "Boss 已生成（逃跑测试）");
            boss!.Position = new Vector2(boss.Position.X, boss.FightAnchorY()); // 跳过降入（锚线 = view 顶缘 + FIGHT_Y）
            await Coroutine.WaitSeconds(this, 0.3);
            Check(boss.IsInFight(), "Boss 进入战斗（逃跑计时开始）");
            var killsBeforeBoss = gs.BossKills;
            var scoreBeforeBoss = gs.Score;
            // 钉住时间轴难度档：后续约 4s 真实等待不得跨过 30s 量化边界造成偶发漂移
            gs.RunTime = 0.0;
            gs.RecomputeDifficulty();
            var diffBefore = gs.DifficultyMultiplier;
            var escapedFlag = false;
            boss.Escaped += () => escapedFlag = true; // M3d：C# [Signal] 以 PascalCase 注册（escaped → Escaped）
            boss.SetSurvival(boss.EscapeTime - boss.EscapeWarning - 0.5f); // 距警告 0.5s
            var warnY0 = boss.Position.Y;
            await Coroutine.WaitSeconds(this, 0.8);
            Check(boss.EscapeWarned(), "逃跑前 3s 触发逃跑警告");
            Check(boss.Position.Y < warnY0 - 3.0f, "警告期间上飘");
            boss.SetSurvival(boss.EscapeTime - 0.05f); // 距逃跑 0.05s
            await Coroutine.WaitSeconds(this, 0.3);
            Check(boss.IsEscaped, "Boss 50s 未被击杀触发逃跑");
            // G02：逃跑期 take_damage 必须无效（激光/溅射按注册表+距离判定绕碰撞层，防补刀致奖励失真）
            var hpAfterEscape = boss.Hp;
            boss.TakeDamage(1000, 1.0f);
            Check(boss.Hp == hpAfterEscape, "G02：逃跑期 take_damage 无效（防补刀致死触发击杀奖励）");
            await Coroutine.WaitSeconds(this, 2.5);
            Check(escapedFlag, "Boss 离场发出 escaped 信号");
            Check(!spawner.IsBossActive(), "Boss 逃跑解除波次/事件占用（可再触发）");
            Check(!GodotObject.IsInstanceValid(boss), "Boss 离场销毁");
            Check(gs.BossKills == killsBeforeBoss, "逃跑不加 boss_kills（轮换不推进）");
            Check(gs.Score == scoreBeforeBoss, "逃跑无 500 分奖励");
            Check(gs.DifficultyMultiplier == diffBefore, "逃跑不升难度");
            Check(!GetNode<Control>("Main/HUD/BossBar").Visible, "逃跑后 Boss 血条隐藏");
            // 轮换计数未推进：下一只仍为同型（boss_kills 未变）
            spawner.SpawnBoss();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Boss? bossNext = null;
            foreach (Node child in main.GetChildren())
            {
                if (child is Boss b)
                {
                    bossNext = b;
                    break;
                }
            }

            Check(bossNext != null && bossNext.BossType == 1, "逃跑后轮换计数未推进（仍为同型 Boss）");
            if (bossNext != null)
            {
                bossNext.QueueFree();
            }

            FreeEnemyBullets();

            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            gs.HighScore = origHighScore;
            gs.SaveProfile();
            // 分裂者（2026-08-04）：死亡分裂 2 小机——缩放 ×0.6 / HP 半 / 无分数 / 不再分裂
            var splitE = SpawnTestEnemy(spawner.ENEMY_TYPES[4], "straight");
            splitE.Position = new Vector2(960.0f, 400.0f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var scoreBefore = gs.Score;
            splitE.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var minis = new Godot.Collections.Array<Enemy>();
            foreach (var e in gs.Enemies)
            {
                if (GodotObject.IsInstanceValid(e) && e != splitE && e is Enemy enemy && enemy.ScoreValue == 0)
                {
                    minis.Add(enemy);
                }
            }

            Check(minis.Count == 2, "分裂者死亡生成 2 小机");
            // 相对断言防难度倍率环境污染（不依赖绝对分数）；子机分数 0 由后续"子机死亡不再计分"覆盖
            Check(gs.Score > scoreBefore, "母体正常计分（子机分数 0 不额外计）");
            foreach (var m in minis)
            {
                Check(m.GetNode<Sprite2D>("Sprite2D").Scale.X < 0.5f, "子机缩放 ×0.6");
            }

            Check(minis.Count > 0 && minis[0].Hp >= 20 && minis[0].Hp <= 50, "子机 HP 减半（约 40-46）");
            var scoreAfterSplit = gs.Score;
            foreach (var m in minis)
            {
                m.TakeDamage(9999);
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(gs.Score == scoreAfterSplit, "子机死亡不再计分");

            // H2（2026-08-06 审计）：分裂者经解锁路径实战可达——unlock_scores 扩展 5 档后
            // 分数 ≥2500 入池（原 4 档上界 mini(5,4) 截断，ENEMY_TYPES[4] 永不入池，
            // 测试直注入绕过解锁路径故全绿但实战不可达）；取样前固定分数保证确定性
            gs.Score = 0;
            var poolLow = spawner.UnlockedTypes();
            gs.Score = 2500;
            var poolHigh = spawner.UnlockedTypes();
            gs.Score = 0;
            Check(!poolLow.Contains(spawner.ENEMY_TYPES[4]), "H2：分数 <2500 分裂者未解锁");
            Check(poolHigh.Contains(spawner.ENEMY_TYPES[4]), "H2：分数 ≥2500 分裂者入池（实战可达）");
            Check(poolLow.Count == 1 && poolHigh.Count == 5, "H2：解锁池随分数扩到 5 型（0 分仅型 1，2500 分全 5 型）");

            // H2：子机继承母体难度——原硬编码 p_difficulty=1.0 使子机 HP/速度不随对局 ramp，
            // 深局分裂者子机 HP 与「母体半血」语义脱节；2.0 档子机 HP 显著高于 1.0 基准（≥50）
            var rampE = SpawnTestEnemy(spawner.ENEMY_TYPES[4], "straight", 2.0f);
            rampE.Position = new Vector2(960.0f, 400.0f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            rampE.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var hardMinis = new Godot.Collections.Array<Enemy>();
            foreach (var e in gs.Enemies)
            {
                if (GodotObject.IsInstanceValid(e) && e != rampE && e is Enemy enemy && enemy.ScoreValue == 0)
                {
                    hardMinis.Add(enemy);
                }
            }

            Check(
                hardMinis.Count > 0 && hardMinis[0].Hp >= 50,
                $"H2：子机继承母体难度（2.0 档 HP 随 ramp 提高，实测 {(hardMinis.Count > 0 ? (int)hardMinis[0].Hp : -1)}）");
            foreach (var m in hardMinis)
            {
                m.TakeDamage(9999);
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var left = 0;
            foreach (var e in gs.Enemies)
            {
                if (GodotObject.IsInstanceValid(e))
                {
                    left++;
                }
            }

            Check(left == 0, "子机死亡不再分裂");

            gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"ENEMY COMBAT TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"ENEMY COMBAT TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
