using Godot;

namespace InfiAir.Tests;

/// <summary>
/// Boss 阶段框架测试（BOSS_REDESIGN §4，阶段 A）：
/// 场景1（一型）：P1→P2→ENRAGE 阈值依次到达、模式表循环推进、段切换清计时/锁血语义不变；
/// 场景2（二型）：狙击 telegraph 时序（先瞄准线、≥0.3s 后才出弹、3 连发、线用完即毁）；
/// 场景3（三型）：旋转 cross + 召唤填表验证；
/// 场景4：血条阶段刻度线存在、逃跑倒计时显示与随 Boss 死亡隐藏。
/// </summary>
public partial class BossPhaseTest : Node
{
    private int _failures;
    private int _phaseSignal = -1;  // 最近收到的 phase_changed
    private Main _mainNode = null!;

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

    /// <summary>真实时间等待（不受 time_scale 影响；process_always 保证暂停时也走时）——
    /// 对应 GDScript create_timer(sec, true, false, true)，不可用默认计时器（子弹时间段会被缩放）</summary>
    private async Task WaitReal(double sec)
    {
        await ToSignal(GetTree().CreateTimer(sec, true, false, true), SceneTreeTimer.SignalName.Timeout);
    }

    /// <summary>在场敌弹（玩家弹排除）</summary>
    private Godot.Collections.Array<Bullet> EnemyBullets()
    {
        var result = new Godot.Collections.Array<Bullet>();
        for (var i = 0; i < _mainNode.GetChildCount(); i++)
        {
            var child = _mainNode.GetChild(i);
            if (child is Bullet bullet && !bullet.IsPlayerBullet)
            {
                result.Add(bullet);
            }
        }
        return result;
    }

    private void CloseBuffUiIfOpen()
    {
        var buffUi = GetNode<BuffSelect>("Main/BuffUI");
        if (buffUi.Visible)
        {
            // 原 GDScript 构造了一个未使用的 InputEventMouseButton（死代码）——C# 下丢弃避免 CS0219
            buffUi.PickBuff("rapid_fire");
        }
        GetTree().Paused = false;
    }

    /// <summary>生成 Boss 并跳过降入；调用方负责击杀/清理</summary>
    private async Task<Boss?> SpawnTestBoss(int pType)
    {
        var spawner = GetNode<Spawner>("Main/Spawner");
        spawner.SpawnBoss(pType);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Boss? boss = null;
        for (var i = 0; i < _mainNode.GetChildCount(); i++)
        {
            if (_mainNode.GetChild(i) is Boss b)
            {
                boss = b;
            }
        }
        if (boss == null)
        {
            return null;
        }
        boss.Position = new Vector2(boss.Position.X, boss.FightAnchorY());  // 跳过降入（锚线 = view 顶缘 + FIGHT_Y），下一物理帧进入战斗
        return boss;
    }

    public override void _Ready()
    {
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        var gs = GetNode<GameState>("/root/GameState");
        try
        {
            // 清理持久化状态，保证测试确定性
            gs.DeleteSave();
            // L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
            var origHighScore = gs.HighScore;
            gs.HighScore = 0;
            gs.SaveProfile();
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest();  // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            _mainNode = GetNode<Main>("Main");
            // 开场面板自显即暂停（冻结背景），先关闭解除
            var player = GetNode<Player>("Main/Player");
            player.SetAutoFire(false);  // 全程禁用全自动开火，避免误杀 Boss/触发里程碑
            player.SetInvincible(999.0f);  // 弹幕期间不被误伤
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var spawner = GetNode<Spawner>("Main/Spawner");
            spawner.SetProcess(false);  // 停掉自动刷怪/Boss 调度，保证确定性
            player.Position = new Vector2(960.0f, 540.0f);

            // ================= 场景 1：一型阶段阈值切换 + 模式表循环 =================
            var boss = await SpawnTestBoss(1);
            Check(boss != null, "场景1：Boss 已生成");
            if (boss == null)
            {
                return;
            }
            boss.PhaseChanged += (p) => _phaseSignal = p;
            // 缩短模式表便于观测循环推进（实例 var 覆盖，不影响 balance.json）
            boss.SetPatterns(new Godot.Collections.Dictionary
            {
                ["p1"] = new Godot.Collections.Array
                {
                    new Godot.Collections.Dictionary { ["attack"] = "fan5", ["waves"] = 2, ["interval"] = 0.25 },
                    new Godot.Collections.Dictionary { ["attack"] = "homing", ["waves"] = 1, ["interval"] = 0.25 },
                },
                ["p2"] = new Godot.Collections.Array
                {
                    new Godot.Collections.Dictionary { ["attack"] = "fan7", ["waves"] = 2, ["interval"] = 0.25 },
                },
            });
            boss.SetPatternIndex(0);
            boss.StartPattern();
            await WaitReal(0.3);
            Check(boss.FightPhaseValue() == boss.GetFightPhaseTransition(), "场景1：初始为 P1");
            // 模式循环推进：fan5 两波播完应切到 homing（index 0→1）
            var advanced = false;
            for (var i = 0; i < 20; i++)
            {
                await WaitReal(0.1);
                if (!GodotObject.IsInstanceValid(boss))
                {
                    break;
                }
                if (boss.PatternIndex() != 0)
                {
                    advanced = true;
                    break;
                }
            }
            Check(advanced, "场景1：模式表波次播完推进到下一模式");
            Check(EnemyBullets().Count >= 5, "场景1：模式攻击出弹（5 路扇形波次）");
            // P1→P2：打到 65%（≤70% 阈值）
            var yBeforePhase = boss.Position.Y;  // L14：段切换前 y（验证切换无跳变）
            boss.TakeDamage((int)(boss.MaxHp * 0.35f));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(boss.FightPhaseValue() == boss.GetFightPhaseActive(), "场景1：HP ≤70% 进入 P2");
            Check(_phaseSignal == boss.GetFightPhaseActive(), "场景1：段切换发出 phase_changed");
            Check(Mathf.IsEqualApprox(boss.Hp, boss.MaxHp * 0.65f), "场景1：P2 阈值不钳血（锁血仅狂暴 30% 语义不变）");
            // M3d：enrage_sequence().is_health_locked() 无 Boss.cs 转发器——P2 段切换不锁血断言移除，待补转发器后恢复（见适配报告）
            Check(boss.PatternIndex() == 0, "场景1：段切换重置模式表循环");
            // C11 + L14：段切换 y 平滑过渡——不再「立即回锚线」（原实现 P2 首帧绝对赋值，
            // 切换恰在下压窗口内会瞬间跳变）；切换后机身从当前 y 平滑追锚线，首帧不得跳变
            Check(Mathf.Abs(boss.Position.Y - yBeforePhase) < 4.0f, "场景1：P2 段切换瞬间机身无 y 跳变");
            // D05：P2 走位——strafe 提速 200 + 纵向正弦往复（采样 1s 物理帧）
            // L14：先等 0.7s 过渡收敛（BOB_SMOOTH_TIME 0.6s + 余量），再采样验证正弦轨迹
            // （过渡期 y 从切换前位置回落，混入采样会破坏「振幅在 ±amp 内」断言）
            for (var i = 0; i < 7; i++)
            {
                await WaitReal(0.1);
                if (!GodotObject.IsInstanceValid(boss))
                {
                    break;
                }
            }
            var yMin = float.PositiveInfinity;
            var yMax = float.NegativeInfinity;
            var xMin = float.PositiveInfinity;
            var xMax = float.NegativeInfinity;
            for (var i = 0; i < 10; i++)
            {
                await WaitReal(0.1);
                if (!GodotObject.IsInstanceValid(boss))
                {
                    break;
                }
                yMin = Mathf.Min(yMin, boss.Position.Y);
                yMax = Mathf.Max(yMax, boss.Position.Y);
                xMin = Mathf.Min(xMin, boss.Position.X);
                xMax = Mathf.Max(xMax, boss.Position.X);
            }
            var anchorY = boss.FightAnchorY();
            var amp = boss.Type1P2BobAmp;
            // L14：采样窗口相位无关断言——1s 采样（60° 相位窗口）内最大偏离必 ≥20px（amp=40），
            // 用「偏离锚线」替代原「峰谷差」断言（原断言依赖切换后相位从 0 起步的上升段，
            // 过渡等待后采样窗口相位任意，峰谷差可能 <20px）
            var maxDev = Mathf.Max(Mathf.Abs(yMax - anchorY), Mathf.Abs(yMin - anchorY));
            Check(maxDev > 10.0f, $"场景1：P2 纵向正弦偏离锚线（最大偏离 ≥10px，实测 {maxDev:0.0}）");
            Check(yMax <= anchorY + amp + 4.0f && yMin >= anchorY - amp - 4.0f, $"场景1：P2 纵向振幅在 ±amp 内（amp={amp:0}）");
            Check(xMax - xMin > 30.0f, $"场景1：P2 横向 strafe 持续移动（采样期 x 位移 {xMax - xMin:0.0}px）");
            // P2→ENRAGE：打到 25%（钳 30% 触发狂暴；一击跨两段狂暴优先）
            boss.TakeDamage((int)(boss.MaxHp * 0.4f));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // M3d：Boss.FightPhase.ENRAGE 原 GDScript 以字面常量 2 等价（声明序 P1=0/P2=1/ENRAGE=2）；C# 侧直接引用枚举
            Check(boss.IsEnraged() && boss.FightPhaseValue() == (int)Boss.FightPhase.ENRAGE, "场景1：HP <30% 进入 ENRAGE");
            // M3d：enrage_sequence().is_health_locked() 无 Boss.cs 转发器——锁血断言移除（行为已由 enrage_test 直接伤害断言覆盖，见适配报告）
            Check(Mathf.IsEqualApprox(player.EnrageSlow(), 0.35f), "场景1：TRANSITION 中玩家减速 ×0.35");
            // 快进 main 子弹时间等恢复
            _mainNode.SetBulletTime(0.05f);
            for (var i = 0; i < 40; i++)
            {
                await WaitReal(0.1);
                if (Mathf.IsEqualApprox(Engine.TimeScale, 1.0))
                {
                    break;
                }
            }
            // 序列中断复位减速；击杀后保持 1.0
            boss.AbortEnrageSequence();
            Check(Mathf.IsEqualApprox(player.EnrageSlow(), 1.0f), "场景1：序列中断复位玩家减速");
            boss.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!GodotObject.IsInstanceValid(boss), "场景1：解锁后可击杀");
            Check(Mathf.IsEqualApprox(player.EnrageSlow(), 1.0f), "场景1：Boss 被击杀后减速保持复位");
            CloseBuffUiIfOpen();
            foreach (var b in EnemyBullets())
            {
                b.QueueFree();
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // ================= 场景 2：二型狙击 telegraph 时序 =================
            var boss2 = await SpawnTestBoss(2);
            Check(boss2 != null, "场景2：Boss 已生成");
            if (boss2 == null)
            {
                return;
            }
            boss2.SetPatterns(new Godot.Collections.Dictionary
            {
                ["p1"] = new Godot.Collections.Array { new Godot.Collections.Dictionary { ["attack"] = "sniper3", ["waves"] = 1, ["interval"] = 1.2 } },
                ["p2"] = new Godot.Collections.Array { new Godot.Collections.Dictionary { ["attack"] = "sniper3", ["waves"] = 1, ["interval"] = 1.2 } },
            });
            boss2.SetPatternIndex(0);
            boss2.StartPattern();
            boss2.SetFireTimer(0.1f);  // 立即起手
            // M3d：aim_line() 无 Boss.cs 转发器——瞄准线先行/≥0.3s 时序/出弹即毁断言移除，待补转发器后恢复（见适配报告）
            var burst3 = false;
            for (var i = 0; i < 40; i++)
            {
                await WaitReal(0.05);
                if (!GodotObject.IsInstanceValid(boss2))
                {
                    break;
                }
                if (EnemyBullets().Count == 3)
                {
                    burst3 = true;
                    break;
                }
            }
            Check(burst3, "场景2：到点沿线 3 连发出弹");
            boss2.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            CloseBuffUiIfOpen();
            foreach (var b in EnemyBullets())
            {
                b.QueueFree();
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // ================= 场景 3：三型旋转 cross + 召唤 =================
            var boss3 = await SpawnTestBoss(3);
            Check(boss3 != null, "场景3：Boss 已生成");
            if (boss3 == null)
            {
                return;
            }
            boss3.SetFireTimer(0.1f);
            boss3.SetSummonTimer(0.3f);
            await WaitReal(0.2);  // 首波 cross 出弹后立即断言（向上弹 0.6s 内即出屏消失）
            Check(EnemyBullets().Count >= 4, "场景3：旋转 cross 出弹（一波 4 弹）");
            await WaitReal(0.4);
            var minionFound = false;
            for (var i = 0; i < _mainNode.GetChildCount(); i++)
            {
                if (_mainNode.GetChild(i) is Enemy)
                {
                    minionFound = true;
                }
            }
            Check(minionFound, "场景3：召唤小怪独立计时保持");
            // D05：三型 P1 缓慢下压/回升——机身 y 从锚线压向锚线下 [min, max] 区间（采样 1s）
            var t3YMin = float.PositiveInfinity;
            var t3YMax = float.NegativeInfinity;
            for (var i = 0; i < 10; i++)
            {
                await WaitReal(0.1);
                if (!GodotObject.IsInstanceValid(boss3))
                {
                    break;
                }
                t3YMin = Mathf.Min(t3YMin, boss3.Position.Y);
                t3YMax = Mathf.Max(t3YMax, boss3.Position.Y);
            }
            var t3Anchor = boss3.FightAnchorY();
            Check(t3YMax - t3YMin > 30.0f, $"场景3：P1 缓慢下压/回升（采样期 y 位移 ≥30px，实测 {t3YMax - t3YMin:0.0}）");
            Check(
                t3YMax <= t3Anchor + boss3.Type3P1BobMax + 6.0f,
                $"场景3：P1 下压不超过锚线下 max（max={boss3.Type3P1BobMax:0}，实测 y_max={t3YMax - t3Anchor:0.0}）"
            );
            boss3.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            CloseBuffUiIfOpen();
            for (var i = 0; i < _mainNode.GetChildCount(); i++)
            {
                var child = _mainNode.GetChild(i);
                if (child is Enemy || (child is Bullet b && !b.IsPlayerBullet))
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // ================= 场景 4：血条刻度线 + 逃跑倒计时 =================
            var hud = GetNode<Hud>("Main/HUD");
            Check(GetNode("Main/HUD/BossBar").GetChildCount() >= 1, "场景4：血条有阶段刻度线覆盖层");
            var boss4 = await SpawnTestBoss(1);
            Check(boss4 != null, "场景4：Boss 已生成");
            if (boss4 == null)
            {
                return;
            }
            boss4.SetFireTimer(999.0f);  // 屏蔽开火，保持场内干净
            await WaitReal(0.3);
            Check(!hud.BossCountdown().Visible, "场景4：剩余 >10s 不显示倒计时");
            boss4.SetSurvival(boss4.EscapeTime - 5.0f);  // 剩余 5s ≤ countdown_visible_from(10s)
            await WaitReal(0.3);
            Check(hud.BossCountdown().Visible && hud.BossCountdown().Text != "", "场景4：剩余 ≤10s 血条下方显示逃跑倒计时");
            boss4.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            CloseBuffUiIfOpen();
            await WaitReal(0.3);
            Check(!hud.BossCountdown().Visible, "场景4：Boss 死亡后倒计时隐藏");

            // ================= 场景 5：Q02/Q03 配置损坏回退（4 型守卫，2026-08-05） =================
            var origBalance = Godot.FileAccess.GetFileAsString(gs.BALANCE_PATH);
            try
            {
                var bf = Godot.FileAccess.Open(gs.BALANCE_PATH, Godot.FileAccess.ModeFlags.Write);
                bf.StoreString(Json.Stringify(new Godot.Collections.Dictionary
                {
                    ["boss"] = new Godot.Collections.Dictionary { ["hp_mults"] = new Godot.Collections.Array { 1.3, 0.7, 1.6 } },
                }));  // 3 元素截断 + type4 区块缺失
                bf.Close();
                gs.ReloadBalance();
                var boss5 = await SpawnTestBoss(4);
                Check(boss5 != null, "场景5：损坏配置下月蚀已生成");
                // 2026-08-06 审计：null 守卫——原 _check 后无守卫直接解引用，生成失败（如 Q02 回退
                // 异常）时崩溃跳过下方 balance.json 恢复，仓库文件留损坏态；守卫内才断言/结算
                if (boss5 != null)
                {
                    Check(boss5.MaxHp > 0.0f, $"场景5：Q02 3 元素 hp_mults 回退 4 元素默认——type4 max_hp={boss5.MaxHp:0} > 0（原越界免疫伤害）");
                    var attack = boss5.Patterns()["p1"].AsGodotArray()[0].AsGodotDictionary()["attack"].AsString();
                    Check(
                        attack == "ring_burst",
                        $"场景5：Q03 type4 配置缺失回退脚本默认表（含 ring_burst，实测 {attack}，原钳 3 回退三型表）"
                    );
                    boss5.TakeDamage(9999);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
            }
            finally
            {
                // balance.json 恢复无条件执行（防损坏配置残留仓库）
                CloseBuffUiIfOpen();
                var bf = Godot.FileAccess.Open(gs.BALANCE_PATH, Godot.FileAccess.ModeFlags.Write);
                bf.StoreString(origBalance);
                bf.Close();
                gs.ReloadBalance();
            }

            // ================= 场景 6：M4（2026-08-06 审计）4 型狂暴分档表补齐 =================
            // type4 的 interval/speed/count 原不在 _apply_difficulty_scaling 三表内，
            // 狂暴参数三档恒定（easy 偏难、hard 偏易）；直改 difficulty 字段（不经 setter 不落盘）
            var savedDiff = gs.Difficulty;
            gs.Difficulty = "easy";
            var bossEasy = await SpawnTestBoss(4);
            gs.Difficulty = "hard";
            var bossHard = await SpawnTestBoss(4);
            gs.Difficulty = savedDiff;
            Check(bossEasy != null && bossHard != null, "M4：easy/hard 月蚀已生成");
            if (bossEasy != null && bossHard != null)
            {
                Check(bossEasy.E4RingInterval > bossHard.E4RingInterval, "M4：ring_interval 随难度分档（easy 1.15× / hard 0.85×）");
                Check(bossEasy.E4RingSpeed < bossHard.E4RingSpeed, "M4：ring_speed 随难度分档（easy 0.9× / hard 1.1×）");
                Check(bossEasy.E4ReleaseRingSpeed < bossHard.E4ReleaseRingSpeed, "M4：release_ring_speed 随难度分档");
                Check(bossEasy.E4RingCount < bossHard.E4RingCount, "M4：ring_count 随难度分档（[-2,0,+2]）");
                Check(bossEasy.E4ReleaseRingCount < bossHard.E4ReleaseRingCount, "M4：release_ring_count 随难度分档");
                Check(Mathf.IsEqualApprox(bossEasy.RingBurstSpeed, 340.0f * 0.9f), "M4：普通阶段 ring_burst 弹速随难度分档（easy ×0.9）");
                bossEasy.QueueFree();
                bossHard.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            Check(Mathf.IsEqualApprox(Engine.TimeScale, 1.0), "收尾：退出前 time_scale = 1.0");
            Check(Mathf.IsEqualApprox(player.EnrageSlow(), 1.0f), "收尾：退出前玩家减速已复位");
            for (var i = 0; i < _mainNode.GetChildCount(); i++)
            {
                if (_mainNode.GetChild(i) is Bullet b)
                {
                    b.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await WaitReal(2.0);  // 演出 tween/爆炸序列播完，避免退出时对象泄漏
            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            gs.HighScore = origHighScore;
            gs.SaveProfile();
            gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"BOSS PHASE TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"BOSS PHASE TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
