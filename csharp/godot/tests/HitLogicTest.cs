using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 受击/碰撞对齐测试（迭代 3.9，PORTING_PARITY 附录 A）：
/// A1 玩家受击小判定点（设计值 r=7 × world_scale）；A2 Boss 身体撞击 30（入场降入期跳过，Boss 不掉血）；
/// A3 狂暴锁血；A4 敌弹按弹种 12/10/20 结算；A5 100 HP 伤害模型（上限/封顶）；
/// A6 敌机撞击 20 且不自毁；A7/A8 闪避 20% 与护甲 ×0.85 全伤害源两段式（去 bug 统一版）；
/// A9 受击清 250px 敌弹；A10 精英碰撞半径与普通同档；A12 爆炸弹 50px/30 固定/主目标吃溅射；
/// A13 慢速力场全局敌机移速 ×0.8（敌弹不受影响）；A16 同帧敌弹只结算第一发且其余保留；
/// A20 出生保护 1.0s（对齐原作入场动画等效）；A21 Boss 入场期可被弹伤（已核实与原作一致）。
/// </summary>
public partial class HitLogicTest : Node
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

    /// <summary>直接实例化 Boss（不经 spawner/main，隔离狂暴子弹时间编排与血条联动）。</summary>
    private Boss MakeBoss(int pType = 1)
    {
        var boss = GD.Load<PackedScene>("res://scenes/boss.tscn").Instantiate<Boss>();
        boss.Setup(1.0f, pType);
        GetNode("Main").AddChild(boss);
        return boss;
    }

    private Enemy MakeEnemy(Godot.Collections.Dictionary config)
    {
        return MakeEnemy(config, new StringName("straight"));
    }

    private Enemy MakeEnemy(Godot.Collections.Dictionary config, StringName strategy)
    {
        var e = GD.Load<PackedScene>("res://scenes/enemy.tscn").Instantiate<Enemy>();
        e.Setup(config, strategy, 1.0f);
        e.CanShoot = false;
        GetNode("Main").AddChild(e);
        return e;
    }

    /// <summary>当前场内敌弹（玩家弹排除）。</summary>
    private Godot.Collections.Array<Bullet> EnemyBullets()
    {
        var result = new Godot.Collections.Array<Bullet>();
        foreach (var child in GetNode("Main").GetChildren())
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

    /// <summary>重置玩家受击状态（无敌/帧标记/被动回血计时），便于逐条断言。</summary>
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
        GameState? gs = null;
        try
        {
            gs = GetNode<GameState>("/root/GameState");

            // 清理持久化状态，保证测试确定性
            gs.DeleteSave();
            // L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
            var origHighScore = gs.HighScore;
            gs.HighScore = 0;
            gs.SaveProfile();
            AddChild(GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate());
            var main = GetNode("Main");
            var player = GetNode<Player>("Main/Player");
            player.SetAutoFire(false);  // 禁用自动开火，避免误伤与意外得分里程碑
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var spawner = GetNode<Spawner>("Main/Spawner");
            spawner.SetProcess(false);  // 停掉自动刷怪/Boss 调度，保证确定性
            foreach (var child in main.GetChildren())
            {
                if (child is Enemy || child is Bullet)
                {
                    child.QueueFree();
                }
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            player.Position = new Vector2(960.0f, 800.0f);
            var pool = (BulletPool)gs.BulletPool!;

            // ================= A1：玩家受击小判定点 =================
            var hb = player.GetNode<CollisionShape2D>("Hitbox/CollisionShape2D").Shape as CircleShape2D;
            Check(hb != null && Mathf.IsEqualApprox(hb.Radius, (float)(7.0 * gs.WorldScale)),
                "A1：玩家受击判定点半径 = 设计值 7 × world_scale");

            // ================= A5：100 HP 伤害模型 =================
            Check(gs.Health == 100.0 && gs.MaxHealth() == 100.0, "A5：初始 100/100 HP");
            ResetHitState(player);
            player.TakeDamage(10.0f, Vector2.Inf);
            Check(gs.Health == 90.0, "A5：受击 -10 HP");
            gs.Heal(999.0);
            Check(gs.Health == 100.0, "A5：heal 封顶 max_health");
            gs.AddBuff("extra_life");
            Check(gs.MaxHealth() == 150.0, "A5：extra_life 每层上限 +50");
            gs.Buffs.Clear();
            Check(gs.MaxHealth() == 100.0, "A5：清 buff 后上限回 100");

            // extra_life 选取：上限 +50 且瞬时 +30 HP（对齐原作 EXTRA_LIFE_HEAL）
            gs.Health = 50.0;
            var ev = new InputEventMouseButton();
            ev.Pressed = true;
            ev.ButtonIndex = MouseButton.Left;
            GetNode<BuffSelect>("Main/BuffUI").PickBuff("extra_life");
            Check(gs.MaxHealth() == 150.0 && gs.Health == 80.0, "A5：extra_life 选取 +50 上限 +30 HP");
            gs.Buffs.Clear();

            // regen buff：二元 +2 HP/s
            gs.Health = 50.0;
            gs.AddBuff("regen");
            await Coroutine.WaitSeconds(this, 0.6);
            Check(gs.Health > 50.5 && gs.Health < 52.5, "A5：regen buff 每秒回 2 HP");
            gs.Buffs.Clear();

            // 被动回血：无 buff 时按难度延迟后回复（本测试环境 medium：4s/2HP）
            gs.Health = 50.0;
            player.SetSinceDamage(999.0f);
            await Coroutine.WaitSeconds(this, 0.6);
            Check(gs.Health > 50.5 && gs.Health < 52.5, "A5：被动回血按速率回复");
            player.SetSinceDamage(0.0f);  // 关闭被动回血，避免干扰后续精确断言

            // lifesteal：击毁回复 10% 上限（每帧至多一次）
            gs.Health = 50.0;
            gs.AddBuff("lifesteal");
            var lsE = MakeEnemy(spawner.ENEMY_TYPES[0]);
            lsE.Hp = 1;
            lsE.Position = new Vector2(960.0f, 400.0f);
            lsE.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(gs.Health == 60.0, "A5：lifesteal 击毁回 10% 上限");
            gs.Buffs.Clear();

            // 存档 v1（3 命制 lives）兼容：血量按满血处理，分数正常恢复
            var f = Godot.FileAccess.Open(gs.SAVE_PATH, Godot.FileAccess.ModeFlags.Write);
            f.StoreString(Json.Stringify(new Godot.Collections.Dictionary
            {
                ["version"] = 1,
                ["lives"] = 2.0,
                ["score"] = 123,
            }));
            f.Close();
            gs.ApplyRunSave(gs.LoadRunData());
            Check(gs.Health == gs.MaxHealth(), "A5：v1 存档血量按满血处理");
            Check(gs.Score == 123, "A5：v1 存档分数兼容");
            gs.Score = 0;
            gs.DeleteSave();

            // ================= A2：Boss 身体撞击 30 =================
            // 入场降入期：与玩家重叠也不扣血（Boss 尚未降入战斗位置）
            gs.Health = 100.0;
            ResetHitState(player);
            var bossEnter = MakeBoss(1);
            // 位置按战斗锚线（view 顶缘 + FIGHT_Y）动态取锚线上方：仍在降入、且位于可见区内
            player.Position = new Vector2(960.0f, bossEnter.FightAnchorY() - 80.0f);
            bossEnter.Position = player.Position;  // 重叠，但仍在降入阶段
            await Coroutine.WaitPhysicsFrames(this, 2);
            Check(!bossEnter.IsInFight(), "A2：Boss 仍处于入场降入阶段");
            Check(gs.Health == 100.0, "A2：入场降入期撞击不扣血");
            bossEnter.QueueFree();
            await Coroutine.WaitPhysicsFrames(this, 1);

            // 进入战斗：撞击玩家 -30 HP，Boss 不掉血不死
            player.Position = new Vector2(960.0f, 800.0f);
            gs.Health = 100.0;
            ResetHitState(player);
            var bossFight = MakeBoss(1);
            bossFight.SetInFight(true);  // 直接置战斗态（重叠事件由传送产生，避开降入时序）
            bossFight.SetFireTimer(999.0f);  // 屏蔽开火，保证场内无杂弹
            bossFight.Position = player.Position;
            await Coroutine.WaitPhysicsFrames(this, 2);
            Check(gs.Health == 70.0, "A2：撞 Boss 身体玩家 -30 HP");
            Check(GodotObject.IsInstanceValid(bossFight) && bossFight.Hp == bossFight.MaxHp,
                "A2：撞击后 Boss 不掉血不死");
            bossFight.QueueFree();
            await Coroutine.WaitPhysicsFrames(this, 1);

            // ================= A6：敌机撞击 20 且不自毁 =================
            gs.Health = 100.0;
            ResetHitState(player);
            player.Position = new Vector2(960.0f, 800.0f);
            var ramE = MakeEnemy(spawner.ENEMY_TYPES[0]);
            ramE.Position = player.Position;
            await Coroutine.WaitPhysicsFrames(this, 2);
            Check(gs.Health == 80.0, "A6：敌机撞击玩家 -20 HP");
            Check(GodotObject.IsInstanceValid(ramE) && ramE.Visible, "A6：撞击后敌机不自毁继续存活");
            Check(gs.Enemies.Contains(ramE), "A6：撞击后敌机仍在注册表（未离场）");
            ramE.QueueFree();
            await Coroutine.WaitPhysicsFrames(this, 1);

            // ================= A7/A8：闪避 20% / 护甲 ×0.85（全伤害源两段式） =================
            // 护甲：固定 ×0.85，无随机
            gs.AddBuff("armor");
            gs.Health = 100.0;
            ResetHitState(player);
            player.TakeDamage(20.0f, Vector2.Inf);
            Check(Mathf.IsEqualApprox(gs.Health, 83.0), "A8：护甲固定 ×0.85（20→17）");
            gs.Buffs.Clear();
            // 闪避：60 次独立判定应至少闪 1 次且不全闪（20% 概率）
            gs.AddBuff("evasion");
            var dodges = 0;
            for (var i = 0; i < 60; i++)
            {
                gs.Health = 100.0;
                ResetHitState(player);
                if (!player.TakeDamage(10.0f, Vector2.Inf))
                {
                    dodges++;
                }
            }
            Check(dodges >= 1, "A7：闪避可触发（60 次至少 1 次）");
            Check(dodges <= 24, "A7：闪避率约 20% 而非全闪");
            gs.Buffs.Clear();
            // 护盾（2026-08-04 新 buff）：每层吸收一次全额伤害——子弹销毁（返回 true）、不掉血、不置无敌
            gs.AddBuff("shield");
            gs.AddBuff("shield");
            gs.Health = 100.0;
            ResetHitState(player);
            Check(player.TakeDamage(20.0f, Vector2.Inf), "护盾：吸收伤害（子弹销毁）");
            Check(Mathf.IsEqualApprox(gs.Health, 100.0), "护盾：吸收后血量不变");
            Check(gs.BuffCount("shield") == 1, "护盾：吸收消耗 1 层");
            Check(player.TakeDamage(20.0f, Vector2.Inf), "护盾：第二层继续吸收");
            Check(gs.BuffCount("shield") == 0, "护盾：两层消耗完毕");
            Check(Mathf.IsEqualApprox(gs.Health, 100.0), "护盾：两层吸收后仍满血");
            ResetHitState(player);
            Check(player.TakeDamage(20.0f, Vector2.Inf), "护盾耗尽：后续受击正常结算");
            Check(Mathf.IsEqualApprox(gs.Health, 80.0), "护盾耗尽：20 伤害正常结算");
            gs.Buffs.Clear();
            // 子弹伤害同样过闪避/护甲（去 bug 统一版：不再仅限撞击）——由上方 take_damage 直接覆盖

            // ================= A3：狂暴锁血 =================
            var boss3 = MakeBoss(1);
            boss3.SetFireTimer(999.0f);
            // 非致死大额伤害：应钳到 30% 阈值并触发狂暴（而非打到阈值以下）
            boss3.TakeDamage((int)(boss3.MaxHp * 0.75f));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(Mathf.IsEqualApprox(boss3.Hp, boss3.MaxHp * boss3.EnrageHpRatio), "A3：非致死伤害钳到 30% 阈值");
            Check(boss3.IsEnraged(), "A3：钳到阈值触发狂暴");
            // 锁血期（触发→RELEASE_HOLD 前）：任何伤害不掉血不死
            boss3.TakeDamage(1);
            Check(Mathf.IsEqualApprox(boss3.Hp, boss3.MaxHp * boss3.EnrageHpRatio), "A3：锁血期伤害不掉血");
            // 序列中断/RELEASE_HOLD 解锁后：小额伤害正常扣血
            boss3.AbortEnrageSequence();
            boss3.TakeDamage(1);
            Check(boss3.Hp < boss3.MaxHp * boss3.EnrageHpRatio, "A3：锁血解除后正常扣血");
            boss3.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // 致死伤害：满血一击直接击杀（不触发狂暴钳制）
            var boss4 = MakeBoss(1);
            boss4.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!GodotObject.IsInstanceValid(boss4), "A3：致死伤害直接击杀");

            // ================= A4：敌弹 damage 按弹种 12/10/20（基准值） =================
            // 伤害随对局进程 ramp（×enemy_damage_ramp），期望按当前 ramp 动态计算（同 boss_pattern 场景4 口径）
            var dmgRamp = gs.EnemyDamageRamp();
            var exp10 = Mathf.Max(1, (int)Mathf.Round(10.0f * dmgRamp));
            var exp12 = Mathf.Max(1, (int)Mathf.Round(12.0f * dmgRamp));
            var exp14 = Mathf.Max(1, (int)Mathf.Round(14.0f * dmgRamp));
            var exp20 = Mathf.Max(1, (int)Mathf.Round(20.0f * dmgRamp));
            var exp21 = Mathf.Max(1, (int)Mathf.Round(21.0f * dmgRamp));
            player.Position = new Vector2(960.0f, 800.0f);
            // laser 弹：配置 damage=20
            var laserE = MakeEnemy(spawner.ELITE_TYPES[1]);
            laserE.bullet_type = new StringName("laser");
            laserE.Position = new Vector2(960.0f, 300.0f);
            laserE.FireAtPlayer();
            Bullet? laserB = null;
            foreach (var b in EnemyBullets())
            {
                if (b.HasMeta("bullet_type") && b.GetMeta("bullet_type").AsStringName() == new StringName("laser"))
                {
                    laserB = b;
                }
            }
            Check(laserB != null && laserB.Damage == exp20, $"A4：laser 敌弹 damage（基准 20，期望 {exp20}）");
            // 命中玩家扣血（传送重叠走真实碰撞管线）
            gs.Health = 100.0;
            ResetHitState(player);
            if (laserB != null)
            {
                laserB.Speed = 0.0f;
                laserB.Position = player.Position;
            }
            await Coroutine.WaitSeconds(this, 0.1);  // 机制一宽限期（0.05s）：停留超窗才结算
            Check(gs.Health == 100.0 - exp20, $"A4：laser 敌弹命中 -{exp20} HP");
            laserE.QueueFree();
            await FreeEnemyBullets();

            // single 弹：配置 damage=12
            var singleE = MakeEnemy(spawner.ENEMY_TYPES[0]);
            singleE.bullet_type = new StringName("single");
            singleE.Position = new Vector2(960.0f, 300.0f);
            singleE.FireAtPlayer();
            Bullet? singleB = null;
            foreach (var b in EnemyBullets())
            {
                if (b.HasMeta("bullet_type") && b.GetMeta("bullet_type").AsStringName() == new StringName("single"))
                {
                    singleB = b;
                }
            }
            Check(singleB != null && singleB.Damage == exp12, $"A4：single 敌弹 damage（基准 12，期望 {exp12}）");
            gs.Health = 100.0;
            ResetHitState(player);
            if (singleB != null)
            {
                singleB.Speed = 0.0f;
                singleB.Position = player.Position;
            }
            await Coroutine.WaitSeconds(this, 0.1);  // 机制一宽限期（0.05s）：停留超窗才结算
            Check(gs.Health == 100.0 - exp12, $"A4：single 敌弹命中 -{exp12} HP");
            singleE.QueueFree();
            await FreeEnemyBullets();

            // spread 弹：配置 damage=10（五向扇形，取任一发）
            var spreadE = MakeEnemy(spawner.ENEMY_TYPES[0]);
            spreadE.bullet_type = new StringName("spread");
            spreadE.Position = new Vector2(960.0f, 300.0f);
            spreadE.FireAtPlayer();
            var spreadDmgOk = false;
            foreach (var b in EnemyBullets())
            {
                if (b.HasMeta("bullet_type") && b.GetMeta("bullet_type").AsStringName() == new StringName("spread"))
                {
                    spreadDmgOk = spreadDmgOk || b.Damage == exp10;
                }
            }
            Check(spreadDmgOk, $"A4：spread 敌弹 damage（基准 10，期望 {exp10}）");
            spreadE.QueueFree();
            await FreeEnemyBullets();

            // Boss 弹种取值（基准）：fan=14，homing=12，狙击=21，cross=12，快照激光=21，快照环弹=12
            var boss5 = MakeBoss(1);
            boss5.Position = new Vector2(960.0f, 300.0f);
            boss5.SetFireTimer(999.0f);  // 屏蔽常规开火，保证弹丸计数纯净
            boss5.FireFan(boss5, 5, boss5.FanBulletSpeed, boss5.BulletDamageFan);
            var fanDmgOk = true;
            var fanCount = 0;
            foreach (var b in EnemyBullets())
            {
                fanCount++;
                if (b.Damage != exp14)
                {
                    fanDmgOk = false;
                }
            }
            Check(fanCount == 5 && fanDmgOk, $"A4：Boss 扇形弹 damage（基准 14，期望 {exp14}）");
            await FreeEnemyBullets();
            boss5.FireHoming(boss5, new Vector2(0.0f, 100.0f), boss5.HomingBulletSpeed,
                boss5.BulletDamageHoming);
            Bullet? homingB = null;
            foreach (var b in EnemyBullets())
            {
                homingB = b;
            }
            Check(homingB != null && homingB.Damage == exp12 && homingB.Homing,
                $"A4：Boss 追踪弹 damage（基准 12，期望 {exp12}）");
            await FreeEnemyBullets();
            boss5.FireCross(boss5, boss5.CrossBulletSpeed, boss5.BulletDamageCross);
            var crossDmgOk = true;
            var crossCount = 0;
            foreach (var b in EnemyBullets())
            {
                crossCount++;
                if (b.Damage != exp12)
                {
                    crossDmgOk = false;
                }
            }
            Check(crossCount == 4 && crossDmgOk, $"A4：Boss 十字弹 damage（基准 12，期望 {exp12}）");
            await FreeEnemyBullets();
            boss5.FireSniper(boss5, Vector2.Zero, boss5.SniperBulletSpeed, boss5.BulletDamageSniper);
            Bullet? sniperB = null;
            foreach (var b in EnemyBullets())
            {
                if (!b.HasMeta("bullet_type"))
                {
                    sniperB = b;
                }
            }
            Check(sniperB != null && sniperB.Damage == exp21, $"A4：Boss 狙击弹 damage（基准 21，期望 {exp21}）");
            await FreeEnemyBullets();
            boss5.FireEnrageSnapshot();
            var snapLaserDmgOk = true;
            var snapRingDmgOk = true;
            var snapLasers = 0;
            var snapRings = 0;
            foreach (var b in EnemyBullets())
            {
                if (b.HasMeta("bullet_type") && b.GetMeta("bullet_type").AsStringName() == new StringName("laser"))
                {
                    snapLasers++;
                    if (b.Damage != exp21)
                    {
                        snapLaserDmgOk = false;
                    }
                }
                else if (b.HasMeta("bullet_type") && b.GetMeta("bullet_type").AsStringName() == new StringName("enrage_ring"))
                {
                    snapRings++;
                    if (b.Damage != exp12)
                    {
                        snapRingDmgOk = false;
                    }
                }
            }
            Check(snapLasers == boss5.EnrageSnapshotLasers && snapLaserDmgOk,
                $"A4：Boss 狂暴快照激光 damage（基准 21，期望 {exp21}）");
            Check(snapRings == boss5.EnrageSnapshotRing && snapRingDmgOk,
                $"A4：Boss 狂暴快照环弹 damage（基准 12，期望 {exp12}）");
            boss5.QueueFree();
            await FreeEnemyBullets();

            // ================= A9：受击清 250px 敌弹 =================
            gs.Health = 100.0;
            ResetHitState(player);
            player.Position = new Vector2(960.0f, 800.0f);
            var nearPositions = new Godot.Collections.Array<Vector2>
            {
                new Vector2(50.0f, 0.0f),
                new Vector2(0.0f, 100.0f),
                new Vector2(-240.0f, 0.0f),
            };
            var nearBullets = new Godot.Collections.Array<Bullet>();
            foreach (var off in nearPositions)
            {
                var nb = pool.Fire(Vector2.Down, 0.0f, 10, false)!;
                nb.Position = player.Position + off;
                nearBullets.Add(nb);
            }
            var farB = pool.Fire(Vector2.Down, 0.0f, 10, false)!;
            farB.Position = player.Position + new Vector2(400.0f, 0.0f);
            // 用一发独立敌弹触发受击
            var hitB = pool.Fire(Vector2.Down, 0.0f, 10, false)!;
            hitB.Position = player.Position;
            await Coroutine.WaitSeconds(this, 0.1);  // 机制一宽限期（0.05s）：停留超窗才结算
            var nearCleared = true;
            foreach (var nb in nearBullets)
            {
                if (nb.Visible)
                {
                    nearCleared = false;
                }
            }
            var a9Exp = 100.0 - Mathf.Max(1, (int)Mathf.Round(10.0f * gs.EnemyDamageRamp()));
            Check(gs.Health == a9Exp, $"A9：触发受击 -{(int)(100.0 - a9Exp)} HP");
            Check(nearCleared, "A9：250px 内敌弹全部清除");
            Check(farB.Visible, "A9：250px 外敌弹保留");

            // ================= A16：同帧多敌弹只结算第一发；无敌期敌弹穿过不销毁 =================
            gs.Health = 100.0;
            ResetHitState(player);
            var b1 = pool.Fire(Vector2.Down, 0.0f, 12, false)!;
            b1.Position = player.Position;
            var b2 = pool.Fire(Vector2.Down, 0.0f, 12, false)!;
            b2.Position = player.Position;
            await Coroutine.WaitSeconds(this, 0.1);  // 机制一宽限期（0.05s）：同帧入窗同点到期，单帧守卫只结算第一发
            var a16Exp = 100.0 - Mathf.Max(1, (int)Mathf.Round(12.0f * gs.EnemyDamageRamp()));
            Check(gs.Health == a16Exp, $"A16：同帧只结算第一发（-{(int)(100.0 - a16Exp)} 而非双倍）");
            // 无敌期内敌弹直接穿过：不结算、不销毁
            var b3 = pool.Fire(Vector2.Down, 300.0f, 12, false)!;
            b3.Position = player.Position + new Vector2(0.0f, -60.0f);
            await Coroutine.WaitPhysicsFrames(this, 2);
            Check(gs.Health == a16Exp, "A16：无敌期内敌弹穿过不结算");
            Check(b3.Visible, "A16：穿过的敌弹不销毁");
            await FreeEnemyBullets();

            // ================= A10：精英碰撞半径与普通机同档 =================
            var eliteR = MakeEnemy(spawner.ELITE_TYPES[0]);
            var eliteShape = eliteR.GetNode<CollisionShape2D>("CollisionShape2D").Shape as CircleShape2D;
            Check(Mathf.IsEqualApprox(eliteShape!.Radius, (float)(38.0 * gs.WorldScale)),
                "A10：精英重甲碰撞半径 = 设计值 38 × world_scale（与普通同档）");
            eliteR.QueueFree();
            var eliteR2 = MakeEnemy(spawner.ELITE_TYPES[1]);
            var eliteShape2 = eliteR2.GetNode<CollisionShape2D>("CollisionShape2D").Shape as CircleShape2D;
            Check(Mathf.IsEqualApprox(eliteShape2!.Radius, (float)(34.0 * gs.WorldScale)),
                "A10：精英游击碰撞半径 = 设计值 34 × world_scale（与普通同档）");
            eliteR2.QueueFree();

            // ================= A12：爆炸弹 50px/固定 30/主目标吃溅射/Boss 不吃 =================
            gs.AddBuff("explosive");
            var tgtA = MakeEnemy(spawner.ENEMY_TYPES[0]);
            tgtA.Hp = 200;
            tgtA.Speed = 0.0f;
            tgtA.Position = new Vector2(960.0f, 400.0f);
            var tgtB = MakeEnemy(spawner.ENEMY_TYPES[0]);
            tgtB.Hp = 200;
            tgtB.Speed = 0.0f;
            tgtB.Position = new Vector2(1000.0f, 400.0f);  // 40px：超出直击判定（10+2）但在爆炸半径 50 内
            var tgtC = MakeEnemy(spawner.ENEMY_TYPES[0]);
            tgtC.Hp = 200;
            tgtC.Speed = 0.0f;
            tgtC.Position = new Vector2(1120.0f, 400.0f);  // 160px 外在半径外
            var exB = pool.Fire(Vector2.Down, 0.0f, 10, true)!;
            exB.Explosive = true;
            exB.Position = tgtA.Position;
            await Coroutine.WaitPhysicsFrames(this, 2);
            Check(tgtA.Hp == 160, "A12：主目标吃直击 10 + 溅射 30 两段");
            Check(tgtB.Hp == 170, "A12：半径内邻机吃溅射 30");
            Check(tgtC.Hp == 200, "A12：半径外敌机不受影响");
            tgtA.QueueFree();
            tgtB.QueueFree();
            tgtC.QueueFree();
            await Coroutine.WaitPhysicsFrames(this, 1);
            // Boss 不吃爆炸 AoE：关碰撞手动触发（Boss r=120 必与子弹重叠，无法走真实碰撞隔离）
            var bossAoe = MakeBoss(1);
            bossAoe.SetFireTimer(999.0f);
            bossAoe.Position = new Vector2(1000.0f, 400.0f);  // 距爆心 40px 在半径内
            var exB2 = pool.Fire(Vector2.Down, 0.0f, 10, true)!;
            exB2.Explosive = true;
            exB2.Monitoring = false;  // 只手动测 AoE，不走碰撞
            exB2.Position = new Vector2(960.0f, 400.0f);
            exB2.Explode();
            Check(bossAoe.Hp == bossAoe.MaxHp, "A12：Boss 不吃爆炸 AoE");
            bossAoe.QueueFree();
            exB2.QueueFree();
            gs.Buffs.Clear();
            await Coroutine.WaitPhysicsFrames(this, 1);

            // ================= E01：导弹溅射对 Boss 生效（C20 静默回归修复） =================
            // 注册表含 Boss（Boss extends Area2D 非 Enemy 子类），as Enemy cast 对 Boss 得 null
            // 曾致母舰导弹溅射静默丢失（直击 80 仍有效）；修复改 Variant 鸭子调用 take_damage
            var splashBoss = MakeBoss(1);
            splashBoss.SetFireTimer(999.0f);
            splashBoss.Position = new Vector2(1000.0f, 400.0f);  // 距爆心 40px 在溅射半径内
            var spB = pool.Fire(Vector2.Down, 0.0f, 10, false)!;
            spB.SplashDamage = 20;
            spB.SplashRadius = 50.0f;
            spB.Monitoring = false;  // 只手动测溅射，不走碰撞
            spB.Position = new Vector2(960.0f, 400.0f);
            spB.Splash();
            Check(splashBoss.Hp == splashBoss.MaxHp - 20, "E01：导弹溅射对 Boss 生效（20 伤害）");
            splashBoss.QueueFree();
            spB.QueueFree();
            await Coroutine.WaitPhysicsFrames(this, 1);

            // ================= A13：慢速力场全局敌机移速 ×0.8（敌弹不受影响） =================
            var slowE1 = MakeEnemy(spawner.ENEMY_TYPES[0]);
            slowE1.Speed = 100.0f;
            slowE1.Position = new Vector2(960.0f, 100.0f);
            await Coroutine.WaitSeconds(this, 0.5);
            var d1 = slowE1.Position.Y - 100.0;
            slowE1.QueueFree();
            gs.AddBuff("slow_field");
            var slowE2 = MakeEnemy(spawner.ENEMY_TYPES[0]);
            slowE2.Speed = 100.0f;
            slowE2.Position = new Vector2(960.0f, 100.0f);
            await Coroutine.WaitSeconds(this, 0.5);
            var d2 = slowE2.Position.Y - 100.0;
            slowE2.QueueFree();
            Check(d1 > 20.0 && d2 < d1 * 0.9 && d2 > d1 * 0.6, "A13：力场全局敌机移速 ×0.8");
            // 敌弹不再被减速（力场已迁出子弹侧）
            var eb = pool.Fire(Vector2.Down, 300.0f, 10, false)!;
            eb.Position = new Vector2(960.0f, 200.0f);
            await Coroutine.WaitSeconds(this, 0.4);
            var bd = eb.Position.Y - 200.0;
            Check(bd > 100.0, "A13：力场下敌弹全速不受影响");
            await FreeEnemyBullets();
            gs.Buffs.Clear();

            // ================= A20：出生保护 1.0s（对齐原作入场动画等效保护） =================
            Check(Mathf.IsEqualApprox(player.SPAWN_INVINCIBLE_TIME, 1.0f), "A20：出生保护 1.0s");
            Check(Mathf.IsEqualApprox(player.INVINCIBLE_TIME, 1.5f), "A20：受击无敌 1.5s（90 帧）");
            // 行为级：新实例化的玩家出生即带 1.0s 保护
            var freshPlayer = GD.Load<PackedScene>("res://scenes/player.tscn").Instantiate<Player>();
            main.AddChild(freshPlayer);
            freshPlayer.SetAutoFire(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(freshPlayer.InvincibleRemaining() > 0.9f && freshPlayer.InvincibleRemaining() <= 1.0f,
                "A20：出生即带 1.0s 保护");
            freshPlayer.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // ================= A5 补充：被动回血受伤重置延迟 =================
            gs.Health = 50.0;
            ResetHitState(player);
            player.TakeDamage(10.0f, Vector2.Inf);
            await Coroutine.WaitSeconds(this, 0.5);
            Check(gs.Health == 40.0, "A5：受击后被动回血延迟重置（0.5s 内不回血）");

            // ================= A5 补充：v2 存档带 extra_life 完整往返 =================
            gs.Buffs.Clear();
            gs.AddBuff("extra_life");
            gs.AddBuff("extra_life");
            gs.Health = 180.0;
            gs.SaveRun(50.0, 10.0);
            gs.ResetRun();
            Check(gs.MaxHealth() == 100.0, "A5：reset 后上限回 100");
            gs.ApplyRunSave(gs.LoadRunData());
            Check(gs.MaxHealth() == 200.0, "A5：v2 存档恢复 extra_life 上限");
            Check(gs.Health == 180.0, "A5：v2 存档血量不被旧上限钳制");
            gs.Buffs.Clear();
            gs.Health = 100.0;
            gs.DeleteSave();

            // ================= A21：Boss 入场期可被玩家弹伤（已核实与原作一致） =================
            var bossEarly = MakeBoss(1);
            bossEarly.SetFireTimer(999.0f);
            // A21 根因修复（2026-08-02）：原硬编码 (960,100) 在 large 视角档下位于可见区外
            // （view 顶缘 222），玩家弹触发 view_world_rect(80) 出界判定被 _despawn() 销毁，
            // 从未命中 Boss → hp == max_hp，断言稳定失败（07-31 登记；08-01 复核通过只是
            // profile 恰为 medium 档的巧合，根因未除，large 档下依旧复现）。
            // 改按战斗锚线动态定位：fight_anchor_y() - 75 = view 顶缘 + 155，仍在降入且
            // 在出界 margin(80) 内（FIGHT_Y=230 时余量充足，任意视角档均成立）。
            var enterPos = new Vector2(960.0f, bossEarly.FightAnchorY() - 75.0f);
            bossEarly.Position = enterPos;
            var pb = pool.Fire(Vector2.Down, 0.0f, 10, true)!;
            pb.Position = bossEarly.Position;
            await Coroutine.WaitPhysicsFrames(this, 2);
            Check(!bossEarly.IsInFight() && bossEarly.Hp < bossEarly.MaxHp,
                "A21：入场降入期玩家弹可伤 Boss（与原作一致）");
            bossEarly.QueueFree();
            await FreeEnemyBullets();
            player.SetInvincible(999.0f);

            // A 审计验证：reset_run 必须清 DDA 计时——触发后 reset_run 应归位
            gs.EmitSignal(GameState.SignalName.PlayerDamaged, 1.0, Vector2.Zero);
            Check(gs.DdaActive(), "DDA：受击后降档激活");
            gs.ResetRun();
            Check(!gs.DdaActive(), "A审计：reset_run 清 DDA 计时（跨对局残留修复）");
            Check(Mathf.IsEqualApprox(gs.DdaFactor(), 1.0), "A审计：reset_run 后 DDA 因子归 1.0");

            // B 梯队（fair plan §8）：DDA 弹幕密度降档——受击触发、激活期因子、到期恢复、间隔拉长
            gs.EmitSignal(GameState.SignalName.PlayerDamaged, 1.0, Vector2.Zero);
            Check(gs.DdaActive(), "DDA：受击后降档激活");
            Check(Mathf.IsEqualApprox(gs.DdaFactor(), gs.DDA_FACTOR), "DDA：激活期返回配置因子");
            var intervalActive = spawner.CurrentInterval();
            gs.ResetDda();
            Check(!gs.DdaActive(), "DDA：计时归零后恢复");
            Check(Mathf.IsEqualApprox(gs.DdaFactor(), 1.0), "DDA：非激活期因子 = 1.0（零开销常态）");
            var intervalNormal = spawner.CurrentInterval();
            // 1.3× 拉长远大于同刻 ramp 增量（毫秒级 elapsed 差异），比较稳定
            Check(intervalActive > intervalNormal,
                $"DDA：激活期波次间隔拉长（{intervalActive:0.00} > {intervalNormal:0.00}）");

            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            gs.HighScore = origHighScore;
            gs.SaveProfile();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"HIT LOGIC TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"HIT LOGIC TEST DONE, failures = {_failures}");
            gs?.DeleteSave();
            TestExit.Quit(_failures);
        }
    }
}
