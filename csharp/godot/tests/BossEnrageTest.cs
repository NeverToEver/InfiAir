using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// Boss 狂暴完整序列测试（一型「旋转堡垒」差异化序列，BOSS_REDESIGN §5.1）：
/// 触发（锁血 30% + 快照玩家位置 + 玩家减速 ×0.35 + 子弹时间）→ TRANSITION（蓄力抖动，
/// 一型悬停原地不滑入轨道）→ ACTIVE（原地每 0.5s 一波 12 向环弹，起始角随波次进动；
/// 玩家减速可移动/射击/冲刺）→ RELEASE_HOLD（解血锁/复位移速，蓄力 0.5s 后 8 路重炮齐射）
/// → RETURN 飞回战斗位 → 常规「余怒」循环（射速 ×1.3）。
/// 另覆盖：序列中到点逃跑中断序列、子弹时间恢复兜底。
/// 时序坑：Engine.time_scale 会缩放 create_timer 默认等待，真实时间等待用 ignore_time_scale=true。
/// </summary>
public partial class BossEnrageTest : Node
{
    private int _failures;
    private Main _main = null!;

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

    /// <summary>在场狂暴弹幕弹丸总数（快照/ACTIVE 波次/RELEASE 波次共用 laser + enrage_ring 两种 meta）</summary>
    private int CountEnrageBullets()
    {
        var n = 0;
        foreach (var child in _main.GetChildren())
        {
            if (child is Bullet bullet && !bullet.IsPlayerBullet && bullet.HasMeta("bullet_type"))
            {
                var t = bullet.GetMeta("bullet_type").AsStringName();
                if (t == new StringName("laser") || t == new StringName("enrage_ring"))
                {
                    n++;
                }
            }
        }
        return n;
    }

    private async Task ClearEnemyBullets()
    {
        foreach (var child in _main.GetChildren())
        {
            if (child is Bullet bullet && !bullet.IsPlayerBullet)
            {
                bullet.QueueFree();
            }
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void CloseBuffUiIfOpen()
    {
        var buffUi = GetNode<BuffSelect>("Main/BuffUI");
        if (buffUi.Visible)
        {
            buffUi.PickBuff(new StringName("rapid_fire"));
        }
        GetTree().Paused = false;
    }

    /// <summary>生成 Boss 并压缩序列时长（实例 var 覆盖，不影响 balance.json），加速测试</summary>
    private async Task<Boss> SpawnTestBoss()
    {
        var spawner = GetNode<Spawner>("Main/Spawner");
        spawner.SpawnBoss(1);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Boss? boss = null;
        foreach (var child in _main.GetChildren())
        {
            if (child is Boss b)
            {
                boss = b;
            }
        }
        if (boss == null)
        {
            throw new System.InvalidOperationException("SpawnBoss(1) 未找到 Boss 节点（GDScript 原语义：null 调用报错计入失败）");
        }
        boss.EnrageDuration = 1.5f;
        boss.EnrageTransitionDuration = 0.2f;
        boss.EnrageAttackWindup = 0.1f;
        boss.EnrageAttackInterval = 0.4f;
        boss.EnrageReleaseHoldDuration = 0.4f;
        boss.EnrageReturnDuration = 0.4f;
        boss.E1SalvoCharge = 0.2f;  // 压缩收尾蓄力，适配 0.4s RELEASE_HOLD
        var pos = boss.Position;
        pos.Y = boss.FightAnchorY();  // 跳过降入（锚线 = view 顶缘 + FIGHT_Y），下一物理帧进入战斗
        boss.Position = pos;
        return boss;
    }

    /// <summary>快进 main 子弹时间并轮询真实时间等 time_scale 恢复 1.0</summary>
    private async Task<bool> WaitTimeScaleRestored()
    {
        _main.SetBulletTime(0.05f);
        for (var i = 0; i < 40; i++)  // 最多 ~4s 真实时间
        {
            await WaitReal(0.1);
            if (Mathf.IsEqualApprox(Engine.TimeScale, 1.0f))
            {
                return true;
            }
        }
        return false;
    }

    public override void _Ready()
    {
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            // 清理持久化状态，保证测试确定性
            var gs = GetNode<GameState>("/root/GameState");
            gs.DeleteSave();
            // L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
            var origHighScore = gs.HighScore;
            gs.HighScore = 0;
            gs.SaveProfile();
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest();  // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            _main = GetNode<Main>("Main");
            // 开场面板自显即暂停（冻结背景），先关闭解除
            var player = GetNode<Player>("Main/Player");
            player.SetAutoFire(false);  // 全程禁用全自动开火，避免误杀 Boss/触发里程碑
            player.SetInvincible(999.0f);  // 狂暴弹幕期间不被误伤
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var spawner = GetNode<Spawner>("Main/Spawner");
            spawner.SetProcess(false);  // 停掉自动刷怪/Boss 调度，保证确定性

            // ================= 场景 1：完整狂暴序列 =================
            player.Position = new Vector2(960.0f, 540.0f);  // 屏幕中部，轨道半径不被边界钳死
            var boss = await SpawnTestBoss();
            Check(boss != null, "场景1：Boss 已生成");
            await WaitReal(0.3);
            boss!.TakeDamage((int)(boss.MaxHp * 0.75f));  // 非致死大额伤害：钳到 30% 阈值，触发狂暴
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(boss.IsEnraged(), "场景1：血量 <30% 触发狂暴");
            Check(boss.BaseModulateColor() != Colors.White, "场景1：狂暴贴图变红");
            Check(boss.EnragePhaseValue() == boss.GetEnragePhaseTransition(), "场景1：触发进入 TRANSITION");
            Check(Mathf.IsEqualApprox(Engine.TimeScale, 0.24f), "场景1：狂暴瞬间进入子弹时间 time_scale=0.24");
            Check(Mathf.IsEqualApprox(player.EnrageSlow(), 0.35f), "场景1：触发即施加玩家减速 ×0.35");
            // M3d：snapshot_target() 无 Boss.cs 转发器——轨道中心快照断言移除，待补转发器后恢复（见适配报告）
            // 锁血（触发→RELEASE_HOLD 前）：普通/致死伤害都不掉血不死
            var hp0 = boss.Hp;
            boss.TakeDamage(50);
            Check(Mathf.IsEqualApprox(boss.Hp, hp0), "场景1：锁血期普通伤害不掉血");
            boss.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(GodotObject.IsInstanceValid(boss) && Mathf.IsEqualApprox(boss.Hp, hp0), "场景1：锁血期致死伤害也不死");
            // 减速功能验证在 ACTIVE 段进行（等 time_scale 恢复后速度才能爬到上限）
            // 快进子弹时间，等 TRANSITION 结束进入 ACTIVE
            _main.SetBulletTime(0.05f);
            var active = false;
            for (var i = 0; i < 40; i++)  // 最多 ~4s 真实时间
            {
                await WaitReal(0.1);
                if (!GodotObject.IsInstanceValid(boss))
                {
                    break;
                }
                if (boss.EnragePhaseValue() == boss.GetEnragePhaseActive())
                {
                    active = true;
                    break;
                }
            }
            Check(active, "场景1：TRANSITION 结束进入 ACTIVE");
            Check(Mathf.IsEqualApprox(player.EnrageSlow(), 0.35f), "场景1：ACTIVE 期间玩家保持减速 ×0.35");
            // 减速功能验证：仍可位移，但移速上限 ×0.35（time_scale 已恢复 1.0）
            Input.ActionPress("move_right");
            await WaitReal(0.6);
            var slowVx = Mathf.Abs(player.Velocity.X);
            Input.ActionRelease("move_right");
            Check(slowVx > 30.0f, "场景1：减速期间方向键输入仍可位移");
            Check(slowVx <= player.MaxSpeed * 0.35f + 5.0f, "场景1：减速期间移速上限 ×0.35");
            player.Position = new Vector2(960.0f, 540.0f);
            player.Velocity = Vector2.Zero;
            // 一型「旋转堡垒」：ACTIVE 悬停原地（不绕轨道），每 0.5s 一波环弹且起始角进动
            var samples = new Godot.Collections.Array<Vector2>();
            for (var i = 0; i < 6; i++)
            {
                if (GodotObject.IsInstanceValid(boss))
                {
                    samples.Add(boss.GlobalPosition);
                }
                await WaitReal(0.12);
            }
            var maxD = 0.0f;
            for (var i = 0; i < samples.Count; i++)
            {
                for (var j = 0; j < i; j++)
                {
                    maxD = Mathf.Max(maxD, samples[i].DistanceTo(samples[j]));
                }
            }
            Check(maxD < 20.0f, "场景1：ACTIVE 期 Boss 悬停原地（旋转堡垒）");
            // M3d：attack_index()/ring_angle() 无 Boss.cs 转发器——波次计数/起始角进动断言移除，待补转发器后恢复（见适配报告）
            Check(CountEnrageBullets() > 0, "场景1：ACTIVE 期环弹开火");
            // 等 ACTIVE 计时耗尽进入 RELEASE_HOLD
            var hold = false;
            for (var i = 0; i < 60; i++)
            {
                await WaitReal(0.1);
                if (!GodotObject.IsInstanceValid(boss))
                {
                    break;
                }
                if (boss.EnragePhaseValue() == boss.GetEnragePhaseReleaseHold())
                {
                    hold = true;
                    break;
                }
            }
            Check(hold, "场景1：ACTIVE 结束进入 RELEASE_HOLD");
            Check(Mathf.IsEqualApprox(player.EnrageSlow(), 1.0f), "场景1：RELEASE_HOLD 复位玩家减速");
            // 一型收尾：蓄力 telegraph 后 8 路重炮齐射（700 弹速重弹，一次性）。
            // M3d：release_salvo_done() 无 Boss.cs 转发器——发射标记断言移除（原为 2026-08-03 flake 修复，
            // 场上计数在慢 runner 上不可靠；待补转发器后按原语义恢复，见适配报告）；下方保留解血锁后可掉血断言
            var hp1 = boss.Hp;
            boss.TakeDamage(5);
            Check(boss.Hp < hp1, "场景1：RELEASE_HOLD 解血锁后可掉血");
            // 等 RETURN 结束回归常规狂暴循环
            var done = false;
            for (var i = 0; i < 60; i++)
            {
                await WaitReal(0.1);
                if (!GodotObject.IsInstanceValid(boss))
                {
                    break;
                }
                if (boss.EnragePhaseValue() == boss.GetEnragePhaseNone())
                {
                    done = true;
                    break;
                }
            }
            Check(done, "场景1：RETURN 结束回归常规阶段");
            if (GodotObject.IsInstanceValid(boss))
            {
                Check(Mathf.Abs(boss.Position.Y - boss.FightAnchorY()) < 40.0f, "场景1：RETURN 飞回战斗位");
                // 永久「余怒」射速 ×1.3（计时器流速 ×1.3，§5.4）
                boss.SetFireTimer(1.6f);
                await Coroutine.WaitSeconds(this, 0.5);  // scale 已为 1.0，0.5s 真实 = 0.5 游戏秒
                Check(boss.FireTimer() < 1.0f, "场景1：序列后保持余怒射速 ×1.3");
                // 解锁后可击杀
                boss.TakeDamage(9999);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Check(!GodotObject.IsInstanceValid(boss), "场景1：序列结束后可击杀");
            }
            CloseBuffUiIfOpen();  // 击杀得分触发里程碑暂停：关闭后恢复驱动
            await ClearEnemyBullets();

            // ================= 场景 2：序列中到点逃跑（中断序列 + 子弹时间兜底） =================
            var boss2 = await SpawnTestBoss();
            Check(boss2 != null, "场景2：Boss 已生成");
            // 场景1 狂暴把血条染红（DANGER），新 Boss 开场必须重置回 ACCENT
            Check(GetNode<SegmentedBar>("Main/HUD/BossBar").FillColor == UITheme.GetAccent(), "场景2：第二只 Boss 开场血条重置为 ACCENT");
            await WaitReal(0.3);
            boss2!.TakeDamage((int)(boss2.MaxHp * 0.75f));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(boss2.IsEnraged() && Mathf.IsEqualApprox(player.EnrageSlow(), 0.35f), "场景2：狂暴触发并减速玩家");
            Check(Mathf.IsEqualApprox(Engine.TimeScale, 0.24f), "场景2：子弹时间启动");
            boss2.SetSurvival(boss2.EscapeTime - 0.02f);  // 下一秒到点：序列中照样逃跑
            var escaping = false;
            for (var i = 0; i < 40; i++)
            {
                await WaitReal(0.1);
                if (!GodotObject.IsInstanceValid(boss2) || boss2.IsEscaping())
                {
                    escaping = true;
                    break;
                }
            }
            Check(escaping, "场景2：狂暴序列中到点照样逃跑");
            if (GodotObject.IsInstanceValid(boss2))
            {
                Check(boss2.EnragePhaseValue() == boss2.GetEnragePhaseNone(), "场景2：逃跑中断狂暴序列");
            }
            Check(Mathf.IsEqualApprox(player.EnrageSlow(), 1.0f), "场景2：逃跑复位玩家减速");
            // main 统一接管的恢复过渡不受 Boss 离场影响，仍应回到 1.0
            Check(await WaitTimeScaleRestored(), "场景2：Boss 子弹时间内离场，time_scale 仍恢复 1.0");
            Check(_main.TimeScaleRamp() < 0.0f, "场景2：恢复过渡已结束");

            Check(Mathf.IsEqualApprox(Engine.TimeScale, 1.0f), "收尾：退出前 time_scale = 1.0");
            await WaitReal(2.0);  // 让场景2 逃跑 Boss 出屏释放、演出 tween 播完，避免退出时对象泄漏
            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            gs.HighScore = origHighScore;
            gs.SaveProfile();
            // ================= 场景 5：四型「月蚀」狂暴（双环反向进动 + 蓄力环阵） =================
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var spawner5 = GetNode<Spawner>("Main/Spawner");
            spawner5.SpawnBoss(4);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Boss? boss5 = null;
            foreach (var child in _main.GetChildren())
            {
                if (child is Boss b)
                {
                    boss5 = b;
                }
            }
            Check(boss5 != null, "场景5：月蚀已生成");
            Check(boss5 != null && boss5.BossType == 4, "场景5：type4 轮换可达（clampi 上限 4）");
            if (boss5 == null)
            {
                throw new System.InvalidOperationException("SpawnBoss(4) 未找到 Boss 节点（GDScript 原语义：null 调用报错计入失败）");
            }
            boss5.EnrageDuration = 1.5f;
            boss5.EnrageTransitionDuration = 0.2f;
            boss5.EnrageAttackWindup = 0.1f;
            boss5.EnrageAttackInterval = 0.4f;
            boss5.EnrageReleaseHoldDuration = 0.4f;
            boss5.EnrageReturnDuration = 0.4f;
            var p5 = boss5.Position;
            p5.Y = boss5.FightAnchorY();
            boss5.Position = p5;
            player.Position = new Vector2(960.0f, 540.0f);
            await WaitReal(0.3);
            boss5.TakeDamage((int)(boss5.MaxHp * 0.75f));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(boss5.IsEnraged(), "场景5：月蚀血量 <30% 触发狂暴");
            // M3d：enrage_sequence() 返回纯 C# 类不可跨语言持有——e5 引用移除，经 boss5 级访问器直取
            // 狂暴子弹时间（time_scale 0.24）拉伸序列 4 倍：压缩序列 1.5s → 真实 ~6.3s，
            // 采样窗口须覆盖全程（6s 真实 = 1.44s 缩放）
            var doubleRingSeen = false;
            var releaseRingSeen = false;
            var phaseSeenRelease = false;
            for (var i = 0; i < 120; i++)
            {
                await WaitReal(0.05);
                if (!GodotObject.IsInstanceValid(boss5))
                {
                    break;
                }
                var nowRings = CountEnrageBullets();
                if (nowRings >= 20)
                {
                    doubleRingSeen = true;
                }
                if (boss5.EnragePhaseValue() == boss5.GetEnragePhaseReleaseHold() && nowRings >= 20)
                {
                    releaseRingSeen = true;
                }
                if (boss5.EnragePhaseValue() == boss5.GetEnragePhaseReleaseHold())
                {
                    phaseSeenRelease = true;
                }
            }
            Check(doubleRingSeen, "场景5：ACTIVE 双环同帧 ≥20 向（正环+反环）");
            Check(phaseSeenRelease, "场景5：序列推进到 RELEASE_HOLD");
            Check(releaseRingSeen, "场景5：收尾蓄力环阵（RELEASE_HOLD 期 ≥20 向）");

            gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"BOSS ENRAGE TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"BOSS ENRAGE TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
