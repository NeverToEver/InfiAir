using Godot;

namespace InfiAir.Tests;

/// <summary>
/// F 键弧光弹反盾测试（2026-08-03 机制四，docs/archive/2026-08-03-combat-fairness-plan.md §5）：
/// 组件级：完整时间轴（WINDUP 前摇无判定 → ACTIVE 有效 → RECOVER 后摇）、硬冷却自流程
/// 结束起算（完整周期 3.8s）、机身 tint 三阶段。
/// 场景级：ACTIVE 弹反属性（转玩家弹/镜面反射 y 取反/×2 速/×1.5 伤）、命中敌机与 Boss、
/// 360° 全周判定（正后方敌弹同样弹反，2026-08-10 盾改全角度）、HUD 能量槽（满格/清空/匀速充能）、池回收与二次激活复位、
/// 与宽限帧/擦弹正交。
/// </summary>
public partial class ParryTest : Node
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

    private Godot.Collections.Array<Bullet> EnemyBullets()
    {
        var outArr = new Godot.Collections.Array<Bullet>();
        foreach (var child in GetNode<Node>("Main").GetChildren())
        {
            if (child is Bullet b && !b.IsPlayerBullet)
            {
                outArr.Add(b);
            }
        }

        return outArr;
    }

    private async System.Threading.Tasks.Task FreeEnemyBullets()
    {
        foreach (var b in EnemyBullets())
        {
            b.QueueFree();
        }

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>清理全部弹丸（含弹反后的玩家弹——防旧弹反弹命中新用例目标）。</summary>
    private async System.Threading.Tasks.Task FreeAllBullets()
    {
        foreach (var child in GetNode<Node>("Main").GetChildren())
        {
            if (child is Bullet)
            {
                child.QueueFree();
            }
        }

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private Enemy MakeEnemy(Godot.Collections.Dictionary config, StringName? strategy = null)
    {
        var e = GD.Load<PackedScene>("res://scenes/enemy.tscn").Instantiate<Enemy>();
        e.Setup(config, strategy is null ? new StringName("straight") : strategy, 1.0f);
        e.CanShoot = false;
        GetNode<Node>("Main").AddChild(e);
        return e;
    }

    private Boss MakeBoss(int pType = 1)
    {
        var boss = GD.Load<PackedScene>("res://scenes/boss.tscn").Instantiate<Boss>();
        boss.Setup(1.0f, pType);
        GetNode<Node>("Main").AddChild(boss);
        boss.SetFireTimer(999.0f);  // 屏蔽开火（须在 add_child 后：_ready 会重置开火计时）
        return boss;
    }

    private void ResetHitState(Player player)
    {
        player.SetInvincible(0.0f);
        player.SetLastHitFrame(-1);
        player.SetSinceDamage(999.0f);
    }

    /// <summary>等待弹反冷却就绪（IDLE 且冷却满；最多 timeout 秒），保证后续 try_parry 一定成功。</summary>
    private async System.Threading.Tasks.Task AwaitParryReady(Player player, float timeout = 5.0f)
    {
        var t = 0.0f;
        while (t < timeout
            && (player.ParryPhase() != PlayerParry.GetPhaseIdle() || player.ParryCooldownRemaining() > 0.0f))
        {
            await Coroutine.WaitSeconds(this, 0.1);
            t += 0.1f;
        }
    }

    /// <summary>启动弹反并等待进入 ACTIVE（最多 1s）。</summary>
    private async System.Threading.Tasks.Task AwaitActive(Player player)
    {
        player.TryParry();
        for (var i = 0; i < 50; i++)
        {
            await Coroutine.WaitSeconds(this, 0.02);
            if (player.ParryPhase() == PlayerParry.GetPhaseActive())
            {
                return;
            }
        }
    }

    public override void _Ready()
    {
        // 禁止裸 async void 生命周期：拆私有 async Task + fire-and-forget
        _ = RunAsync();
    }

    private async System.Threading.Tasks.Task RunAsync()
    {
        try
        {
            var gs = GetNode<GameState>("/root/GameState");
            // ================= 组件级：完整时间轴 =================
            var pp = new PlayerParry();
            pp.Configure(0.8f, 0.5f, 3.0f);
            Check(pp.TryStart(), "时间轴：IDLE 可启动（进入 WINDUP）");
            Check(pp.Phase == PlayerParry.ParryPhase.WINDUP && !pp.TryStart(), "时间轴：流程中不可重复启动");
            pp.Tick(0.1f);
            Check(pp.Phase == PlayerParry.ParryPhase.WINDUP, "时间轴：0.1s 仍在前摇（无判定期）");
            pp.Tick(0.06f);
            Check(pp.Phase == PlayerParry.ParryPhase.ACTIVE, "时间轴：0.16s ≥ 前摇 0.15s 进入 ACTIVE");
            Check(pp.TintStrength() == 1.0f, "时间轴：ACTIVE 金色 tint 保持");
            pp.Tick(0.25f);
            Check(pp.Phase == PlayerParry.ParryPhase.ACTIVE, "时间轴：ACTIVE 0.25s 内保持");
            Check(pp.EnergyRatio() == 0.0f, "能量槽：流程期保持空");
            pp.Tick(0.25f);
            Check(pp.Phase == PlayerParry.ParryPhase.RECOVER, "时间轴：0.5s 有效窗满进入 RECOVER");
            pp.Tick(0.05f);  // RECOVER 中段（0.05/0.15）
            Check(pp.TintStrength() < 1.0f && pp.TintStrength() > 0.3f, "时间轴：RECOVER 后摇金色渐弱");
            pp.Tick(0.1f);
            Check(pp.Phase == PlayerParry.ParryPhase.IDLE, "时间轴：0.15s 后摇满回归 IDLE");
            Check(pp.CooldownRemaining() > 2.9f, "时间轴：硬冷却自 RECOVER 完成起算（完整周期 0.8+3.0）");
            Check(!pp.TryStart(), "冷却：3s 冷却期内不可再次展开");
            pp.Tick(1.5f);
            Check(Mathf.IsEqualApprox(pp.EnergyRatio(), 0.5f), "能量槽：冷却按 3.0s 匀速充能（1.5s 后半格）");
            pp.Tick(1.5f);
            Check(pp.CooldownRemaining() == 0.0f && pp.EnergyRatio() == 1.0f, "冷却：满 3s 回满格");
            Check(pp.TryStart(), "冷却：满 3s 后可再次展开（完整周期 3.8s）");

            // ================= 场景级环境 =================
            gs.DeleteSave();
            // L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
            var origHighScore = gs.HighScore;
            gs.HighScore = 0;
            gs.SaveProfile();
            AddChild(GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate());
            var main = GetNode<Node>("Main");
            var player = GetNode<Player>("Main/Player");
            player.SetAutoFire(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var spawner = GetNode<Spawner>("Main/Spawner");
            spawner.SetProcess(false);
            foreach (var child in main.GetChildren())
            {
                if (child is Enemy || child is Bullet)
                {
                    child.QueueFree();
                }
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            player.Position = new Vector2(960.0f, 800.0f);
            gs.Health = 100.0;
            ResetHitState(player);

            // ================= 场景级：ACTIVE 弹反属性 =================
            var pool = (BulletPool)gs.BulletPool!;
            await AwaitActive(player);
            var pb = pool.Fire(Vector2.Down, 200.0f, 10, false)!;
            pb.Position = new Vector2(960.0f, 770.0f);  // 玩家上方 30px（机头前方扇区内）
            await Coroutine.WaitSeconds(this, 0.1);
            Check(pb.IsPlayerBullet, "弹反：ACTIVE 期盾区敌弹被弹反（转玩家弹）");
            Check(pb.Direction.Y < 0.0f, "弹反：方向镜面反射（y 取反，朝上返回）");
            Check(Mathf.IsEqualApprox(pb.Speed, 400.0f), "弹反：speed ×2.0（200→400）");
            Check(pb.Damage == 15, "弹反：damage = 原 ×1.5 四舍五入（10→15）");
            await FreeAllBullets();  // 清掉反弹的玩家弹（防其命中后续用例目标）

            // ================= 弹反命中普通敌机（1.5 倍伤害结算） =================
            await AwaitParryReady(player);
            var target = MakeEnemy(spawner.ENEMY_TYPES[0]);
            target.Hp = 100;
            target.Position = new Vector2(960.0f, 300.0f);
            await AwaitActive(player);
            var rb = pool.Fire(Vector2.Down, 300.0f, 10, false)!;
            rb.Position = new Vector2(960.0f, 770.0f);
            await Coroutine.WaitSeconds(this, 1.2);  // 弹反后 600px/s 飞 470px ≈ 0.78s（余量防偶发）
            Check(GodotObject.IsInstanceValid(target) && target.Hp == 85, "弹反：命中普通敌机按 15 伤害结算（100→85）");
            target.QueueFree();
            await FreeAllBullets();

            // ================= 弹反命中 Boss =================
            await AwaitParryReady(player);
            var boss = MakeBoss(1);
            boss.Position = new Vector2(960.0f, 300.0f);
            var bossHp0 = boss.Hp;
            await AwaitActive(player);
            var bb = pool.Fire(Vector2.Down, 300.0f, 10, false)!;
            bb.Position = new Vector2(960.0f, 770.0f);
            await Coroutine.WaitSeconds(this, 0.08);  // 弹反发生（盾区进入 1-2 物理帧；Boss strafe 漂移大，不做真实飞行命中）
            Check(bb.IsPlayerBullet && bb.Damage == 15, "弹反：Boss 用例弹已被弹反（1.5 倍伤害）");
            bb.Position = boss.Position;  // 传送重叠命中：结算走玩家弹 enemy 组路径（对齐 hit_logic 传送重叠惯例）
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check(GodotObject.IsInstanceValid(boss) && Mathf.IsEqualApprox(boss.Hp, bossHp0 - 15.0f), "弹反：命中 Boss 同样按 15 伤害结算");
            boss.QueueFree();
            await FreeAllBullets();

            // ================= 360° 全周判定：正后方敌弹同样弹反 =================
            await AwaitParryReady(player);
            await AwaitActive(player);
            var behind = pool.Fire(Vector2.Up, 100.0f, 12, false)!;
            behind.Position = new Vector2(960.0f, 850.0f);  // 玩家正下方 50px（全周盾半径 60 内）
            await Coroutine.WaitSeconds(this, 0.2);
            Check(behind.IsPlayerBullet, "全角度：后方敌弹同样被弹反（360° 判定）");
            await FreeAllBullets();

            // ================= WINDUP/RECOVER 无判定（场景级复核） =================
            await AwaitParryReady(player);
            player.TryParry();
            var wb = pool.Fire(Vector2.Right, 400.0f, 12, false)!;
            wb.Position = new Vector2(930.0f, 745.0f);  // 前摇期进入盾区（y=745 前方 55px），0.15s 内水平穿出
            await Coroutine.WaitSeconds(this, 0.1);
            Check(!wb.IsPlayerBullet, "前摇：WINDUP 期内盾区弹不弹反");
            await Coroutine.WaitSeconds(this, 0.2);  // ACTIVE 已开始；弹已穿出盾区（无进入事件）
            Check(!wb.IsPlayerBullet, "前摇：弹穿出盾区后不弹反（弹反只在进入时刻）");
            await FreeAllBullets();

            // ================= 硬冷却（场景级）：流程结束起 3s 内不可再展开 =================
            await AwaitParryReady(player);
            player.TryParry();
            await Coroutine.WaitSeconds(this, 0.9);  // 流程 0.8s 播完（RECOVER 完成）
            Check(player.ParryPhase() == PlayerParry.GetPhaseIdle(), "冷却：场景级流程结束回归 IDLE");
            Check(!player.TryParry(), "冷却：3s 冷却期内再次展开被拒");
            await Coroutine.WaitSeconds(this, 3.0);
            Check(player.TryParry(), "冷却：满 3s 后可再次展开");
            await Coroutine.WaitSeconds(this, 0.9);  // 本次流程播完（不干扰后续）

            // ================= HUD 能量槽 =================
            var parryBar = GetNode<SegmentedBar>("Main/HUD/ParryBar");
            await AwaitParryReady(player);  // 等冷却结束回满格
            await Coroutine.WaitSeconds(this, 0.2);  // HUD 0.1s 节流刷新
            Check(parryBar.Value == 100.0f, "能量槽：HUD 满格显示（冷却结束）");
            player.TryParry();
            await Coroutine.WaitSeconds(this, 0.15);
            Check(parryBar.Value == 0.0f, "能量槽：按下即清空（HUD 联动）");
            await Coroutine.WaitSeconds(this, 1.4);  // 流程 0.8s 结束 + 冷却 0.6s
            var ratio = player.ParryEnergyRatio();
            Check(ratio > 0.15f && ratio < 0.3f, $"能量槽：流程结束起按 3.0s 匀速充能（约 0.6s ≈ 0.2 格，实测 {ratio:F2}）");

            // ================= 池回收与二次激活复位 =================
            await AwaitParryReady(player);
            await AwaitActive(player);
            var r1 = pool.Fire(Vector2.Down, 300.0f, 10, false)!;
            r1.Position = new Vector2(960.0f, 770.0f);
            await Coroutine.WaitSeconds(this, 1.5);  // 弹反后 600px/s 朝上出界回收
            Check(!r1.IsActive(), "池回收：弹反弹出界后按既有路径回收");
            var r2 = pool.Fire(Vector2.Down, 500.0f, 20, true)!;
            Check(r2 == r1, "池复用：回收弹被复用（同一实例）");
            Check(r2.IsPlayerBullet && r2.Damage == 20 && Mathf.IsEqualApprox(r2.Speed, 500.0f), "池复用：二次激活状态复位（阵营/伤害/速度）");
            r2.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // ================= 与宽限帧/擦弹正交 =================
            gs.Health = 100.0;
            ResetHitState(player);
            await AwaitParryReady(player);
            await AwaitActive(player);
            var sweep = pool.Fire(Vector2.Right, 600.0f, 12, false)!;
            sweep.Position = player.Position + new Vector2(-30.0f, 3.0f);  // 玩家下方 30px（全周盾半径 60 内）水平擦过
            await Coroutine.WaitSeconds(this, 0.2);
            Check(gs.Health == 100.0, "正交：盾展开期受击宽限帧不受影响（弹反无伤）");
            Check(sweep.IsPlayerBullet, "正交：全周盾内弹被弹反（宽限路径照常）");
            await FreeEnemyBullets();

            foreach (var child in main.GetChildren())
            {
                if (child is Bullet)
                {
                    child.QueueFree();
                }
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Coroutine.WaitSeconds(this, 0.6);
            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            gs.HighScore = origHighScore;
            gs.SaveProfile();
            gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"PARRY TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"PARRY TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
