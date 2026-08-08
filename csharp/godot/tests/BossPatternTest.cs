using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// Boss 逐型模式库与差异化狂暴测试（BOSS_REDESIGN §5，阶段 B）——M7c 由 test/boss_pattern_test.gd 迁移：
/// 场景1（一型 P2 蓄力重炮）：蓄力辉光 telegraph 先行 → 3 发 700 弹速/21 伤害重弹；
/// 场景2（二型 P2 冲刺掠过）：水平瞄准线先行 → 高速横穿 + 路径拖 3 枚减速弹 → 回巡航位；
/// 场景3（二型狂暴「猎杀环绕」）：轨道象限点瞬停 + 每点瞄准线 + 单发狙，收尾 12 向慢环；
/// 场景4（三型 P2 编队齐射/弹幕墙）：4 小怪横队 0.8s 后齐射；10 槽位墙留 2 相邻缺口
///   且缺口避开自机方位 ±30°；
/// 场景5（三型狂暴「倾巢」）：ACTIVE 3 波小怪 + 8 向环弹，收尾 16 向慢环 + 小怪齐射；
/// 场景6（难度分档 §4.4）：easy 弹数减/间隔 ×1.15/弹速 ×0.9，hard 反向；HP/伤害不动。
/// 一型狂暴（悬停环弹进动 + 8 路重炮齐射）断言在 boss_enrage_test。
/// 模式与 GDScript 断言场景一致：_Ready fire-and-forget + Check + TestExit.Quit(failures)。
/// </summary>
public partial class BossPatternTest : Node
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

    /// <summary>
    /// GDScript roundf()/round() 语义（四舍五入，远离零）。C# Mathf.Round 为银行家舍入，
    /// 直接替换会在 .5 边界差 1（伤害期望值断言敏感），故显式复刻。
    /// </summary>
    private static int RoundHalfAway(double v)
    {
        return v >= 0.0 ? (int)System.Math.Floor(v + 0.5) : (int)System.Math.Ceiling(v - 0.5);
    }

    /// <summary>真实时间等待（不受 time_scale 影响；process_always 保证暂停时也走时）</summary>
    private async Task WaitReal(double sec)
    {
        await ToSignal(GetTree().CreateTimer(sec, true, false, true), SceneTreeTimer.SignalName.Timeout);
    }

    /// <summary>无 meta 敌弹中指定弹速的弹（重炮/掠过拖弹/齐射/墙弹以此识别；
    /// 敌机自机狙带 bullet_type meta，Boss 狂暴弹带 laser/enrage_ring meta，互不混淆）。</summary>
    private Godot.Collections.Array<Bullet> BulletsBySpeed(float pSpeed)
    {
        var outArr = new Godot.Collections.Array<Bullet>();
        foreach (Node child in GetNode("Main").GetChildren())
        {
            if (child is Bullet b && !b.IsPlayerBullet && !b.HasMeta("bullet_type")
                && Mathf.IsEqualApprox(b.Speed, pSpeed))
            {
                outArr.Add(b);
            }
        }
        return outArr;
    }

    private int CountMetaBullets(StringName pType)
    {
        int n = 0;
        foreach (Node child in GetNode("Main").GetChildren())
        {
            if (child is Bullet b && !b.IsPlayerBullet && b.HasMeta("bullet_type")
                && b.GetMeta("bullet_type").AsStringName() == pType)
            {
                n++;
            }
        }
        return n;
    }

    private Godot.Collections.Array<Node> EnemiesAlive()
    {
        var outArr = new Godot.Collections.Array<Node>();
        foreach (Node child in GetNode("Main").GetChildren())
        {
            if (child is Enemy)
            {
                outArr.Add(child);
            }
        }
        return outArr;
    }

    private async Task ClearField()
    {
        foreach (Node child in GetNode("Main").GetChildren())
        {
            if (child is Enemy || (child is Bullet b && !b.IsPlayerBullet))
            {
                child.QueueFree();
            }
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void CloseBuffUiIfOpen()
    {
        var buffUi = GetNode<BuffSelect>("Main/BuffUI");
        if (buffUi.Visible)
        {
            buffUi.PickBuff("rapid_fire");
        }
        GetTree().Paused = false;
    }

    /// <summary>生成 Boss 并跳过降入；调用方负责击杀/清理。M3d：Boss 迁 C#，返回类型可直接注解。</summary>
    private async Task<Boss?> SpawnTestBoss(int pType)
    {
        var spawner = GetNode<Spawner>("Main/Spawner");
        spawner.SpawnBoss(pType);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Boss? boss = null;
        foreach (Node child in GetNode("Main").GetChildren())
        {
            if (child is Boss b)
            {
                boss = b;
                break;
            }
        }
        if (boss != null)
        {
            boss.Position = new Vector2(boss.Position.X, boss.FightAnchorY()); // 跳过降入（锚线 = view 顶缘 + FIGHT_Y），下一物理帧进入战斗
        }
        return boss;
    }

    /// <summary>强制进入 P2 并换成指定模式表（绕过血量流程，专注攻击断言）。</summary>
    private void ForceP2Patterns(Boss boss, Godot.Collections.Array p2)
    {
        boss.SetPatterns(new Godot.Collections.Dictionary
        {
            ["p1"] = new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { ["attack"] = new StringName("fan5"), ["waves"] = 1, ["interval"] = 0.3 },
            },
            ["p2"] = p2,
        });
        boss.SetFightPhase(boss.GetFightPhaseActive());
        boss.SetPatternIndex(0);
        boss.StartPattern();
        boss.SetFireTimer(0.1f);
    }

    public override void _Ready()
    {
        // 禁止裸 async void 生命周期：拆私有 async Task + fire-and-forget
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
            int origHighScore = gs.HighScore;
            gs.HighScore = 0;
            gs.Difficulty = "medium"; // 场景1-5 弹速/弹数断言基于 medium 基准档
            gs.SaveProfile();
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest(); // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            var main = GetNode<Main>("Main");
            // 开场面板自显即暂停（冻结背景），先关闭解除
            var player = GetNode<Player>("Main/Player");
            player.SetAutoFire(false); // 全程禁用全自动开火，避免误杀 Boss/触发里程碑
            player.SetInvincible(999.0f); // 弹幕/掠过期间不被误伤
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var spawner = GetNode<Spawner>("Main/Spawner");
            spawner.SetProcess(false); // 停掉自动刷怪/Boss 调度，保证确定性
            player.Position = new Vector2(960, 540);

            // ================= 场景 1：一型 P2 蓄力重炮 =================
            var boss1 = await SpawnTestBoss(1);
            Check(boss1 != null, "场景1：Boss 已生成");
            if (boss1 is null)
            {
                throw new System.InvalidOperationException("场景1：Boss 生成失败");
            }
            boss1.CANNON_CHARGE = 0.4f;
            ForceP2Patterns(boss1, new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { ["attack"] = new StringName("charged_cannon"), ["waves"] = 1, ["interval"] = 1.2 },
            });
            // M3d：boss1.attacks().cannon_elapsed() 无 Boss.cs 转发器（BossAttacks 纯 C# 类不可跨语言）——
            // telegraph 起手检测/计时断言移除，待主代理补转发器后恢复（见适配报告）
            // C34：弹速/伤害从 boss 实例常量读取（cfg 覆盖后运行时值），改 JSON 不漂移
            float cannonSpeed = boss1.CANNON_BULLET_SPEED;
            int cannonDmg = boss1.CANNON_DAMAGE;
            Check(BulletsBySpeed(cannonSpeed).Count == 0, "场景1：蓄力期间未出弹（telegraph 先行）");
            int heavyMax = 0;
            for (int i = 0; i < 50; i++)
            {
                await WaitReal(0.05);
                if (!GodotObject.IsInstanceValid(boss1))
                {
                    break;
                }
                int n = BulletsBySpeed(cannonSpeed).Count;
                heavyMax = System.Math.Max(heavyMax, n);
                if (heavyMax >= 3)
                {
                    break;
                }
            }
            Check(heavyMax >= 3, $"场景1：3 发高速重弹（{(int)cannonSpeed} 弹速）");
            bool heavyDmgOk = true;
            foreach (Bullet b in BulletsBySpeed(cannonSpeed))
            {
                if (b.Damage != cannonDmg)
                {
                    heavyDmgOk = false;
                }
            }
            Check(heavyDmgOk, $"场景1：重弹伤害 {cannonDmg}");
            boss1.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            CloseBuffUiIfOpen();
            await ClearField();

            // ================= 场景 2：二型 P2 冲刺掠过 =================
            var boss2 = await SpawnTestBoss(2);
            Check(boss2 != null, "场景2：Boss 已生成");
            if (boss2 is null)
            {
                throw new System.InvalidOperationException("场景2：Boss 生成失败");
            }
            boss2.SWEEP_AIM = 0.3f;
            boss2.SWEEP_RETURN_DURATION = 0.3f;
            ForceP2Patterns(boss2, new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { ["attack"] = new StringName("dash_sweep"), ["waves"] = 1, ["interval"] = 1.2 },
            });
            bool sweepAimed = false;
            for (int i = 0; i < 40; i++)
            {
                await WaitReal(0.05);
                if (!GodotObject.IsInstanceValid(boss2))
                {
                    break;
                }
                // M3d：sweep_line() 无 Boss.cs 转发器，仅保留 AIM 状态判定（见适配报告）
                if (boss2.SweepStateValue() == (int)Boss.SweepState.AIM)
                {
                    sweepAimed = true;
                    break;
                }
            }
            Check(sweepAimed, "场景2：冲刺掠过水平瞄准线 telegraph 先行");
            // C34：弹速/伤害从 boss 实例常量读取，改 JSON 不漂移
            float dropSpeed = boss2.SWEEP_DROP_SPEED;
            Check(BulletsBySpeed(dropSpeed).Count == 0, "场景2：瞄准期间未拖弹");
            bool dashing = false;
            double x0 = 0.0;
            for (int i = 0; i < 40; i++)
            {
                await WaitReal(0.05);
                if (!GodotObject.IsInstanceValid(boss2))
                {
                    break;
                }
                if (boss2.SweepStateValue() == (int)Boss.SweepState.DASH)
                {
                    dashing = true;
                    x0 = boss2.Position.X;
                    break;
                }
            }
            Check(dashing, "场景2：瞄准结束进入高速横穿");
            await WaitReal(0.15);
            if (GodotObject.IsInstanceValid(boss2))
            {
                Check(Mathf.Abs(boss2.Position.X - x0) > 100.0, "场景2：横穿速度 ~900（0.15s 位移 >100px）");
            }
            int drops = 0;
            bool sweepDone = false;
            for (int i = 0; i < 80; i++)
            {
                await WaitReal(0.05);
                if (!GodotObject.IsInstanceValid(boss2))
                {
                    break;
                }
                drops = System.Math.Max(drops, BulletsBySpeed(dropSpeed).Count);
                if (boss2.SweepStateValue() == (int)Boss.SweepState.NONE)
                {
                    sweepDone = true;
                    break;
                }
            }
            Check(drops >= 3, $"场景2：路径等距拖 3 枚减速弹（{(int)dropSpeed} 弹速）");
            int dropDmgExpected = System.Math.Max(1, RoundHalfAway((double)boss2.SWEEP_DROP_DAMAGE * gs.EnemyDamageRamp()));
            bool dropDmgOk = true;
            foreach (Bullet b in BulletsBySpeed(dropSpeed))
            {
                if (b.Damage != dropDmgExpected)
                {
                    dropDmgOk = false;
                }
            }
            Check(dropDmgOk, $"场景2：减速弹伤害 {dropDmgExpected}（×ramp）");
            Check(sweepDone, "场景2：穿屏后回到巡航流程");
            if (GodotObject.IsInstanceValid(boss2))
            {
                Check(Mathf.Abs(boss2.Position.Y - boss2.FightAnchorY()) < 40.0, "场景2：归位回 FIGHT_Y 战斗位");
            }
            boss2.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            CloseBuffUiIfOpen();
            await ClearField();

            // ================= 场景 3：二型狂暴「猎杀环绕」 =================
            var boss3 = await SpawnTestBoss(2);
            Check(boss3 != null, "场景3：Boss 已生成");
            if (boss3 is null)
            {
                throw new System.InvalidOperationException("场景3：Boss 生成失败");
            }
            boss3.ENRAGE_DURATION = 2.0f;
            boss3.ENRAGE_TRANSITION_DURATION = 0.2f;
            boss3.ENRAGE_ATTACK_WINDUP = 0.1f;
            boss3.E2_POINT_INTERVAL = 0.3f;
            boss3.E2_AIM = 0.15f;
            boss3.ENRAGE_RELEASE_HOLD_DURATION = 0.5f;
            boss3.ENRAGE_RETURN_DURATION = 0.4f;
            await WaitReal(0.3);
            boss3.TakeDamage((int)(boss3.MaxHp * 0.75));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(boss3.IsEnraged(), "场景3：血量 <30% 触发狂暴");
            main.SetBulletTime(0.05f);
            bool active3 = false;
            for (int i = 0; i < 40; i++)
            {
                await WaitReal(0.1);
                if (!GodotObject.IsInstanceValid(boss3))
                {
                    break;
                }
                if (boss3.EnragePhaseValue() == (int)Boss.EnragePhase.ACTIVE)
                {
                    active3 = true;
                    break;
                }
            }
            Check(active3, "场景3：TRANSITION 结束进入 ACTIVE");
            // M3d：aim_line()/attack_index() 无 Boss.cs 转发器——瞄准线/瞬停点计数断言移除，待补转发器后恢复（见适配报告）
            int heavy3Max = 0;
            var posSamples = new System.Collections.Generic.List<Vector2>();
            for (int i = 0; i < 30; i++) // ~1.5s 覆盖 ACTIVE
            {
                await WaitReal(0.05);
                if (!GodotObject.IsInstanceValid(boss3))
                {
                    break;
                }
                if (boss3.EnragePhaseValue() != (int)Boss.EnragePhase.ACTIVE)
                {
                    break;
                }
                heavy3Max = System.Math.Max(heavy3Max, BulletsBySpeed(boss3.E2_SNIPER_SPEED).Count);
                posSamples.Add(boss3.GlobalPosition);
            }
            double jumpMax = 0.0;
            for (int i = 0; i < posSamples.Count; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    jumpMax = System.Math.Max(jumpMax, posSamples[i].DistanceTo(posSamples[j]));
                }
            }
            Check(jumpMax > 100.0, "场景3：瞬停点分布在轨道上（采样点分散）");
            Check(heavy3Max >= 2, $"场景3：每点单发狙（900 弹速重弹，峰值同屏 {heavy3Max} 发）");
            bool hold3 = false;
            for (int i = 0; i < 60; i++)
            {
                await WaitReal(0.05);
                if (!GodotObject.IsInstanceValid(boss3))
                {
                    break;
                }
                if (boss3.EnragePhaseValue() == (int)Boss.EnragePhase.RELEASE_HOLD)
                {
                    hold3 = true;
                    break;
                }
            }
            Check(hold3, "场景3：ACTIVE 结束进入 RELEASE_HOLD");
            int ring3Max = 0;
            for (int i = 0; i < 30; i++)
            {
                await WaitReal(0.05);
                if (!GodotObject.IsInstanceValid(boss3))
                {
                    break;
                }
                ring3Max = System.Math.Max(ring3Max, CountMetaBullets(new StringName("enrage_ring")));
                if (ring3Max >= 12)
                {
                    break;
                }
            }
            Check(ring3Max >= 12, "场景3：收尾 12 向慢速环弹");
            if (GodotObject.IsInstanceValid(boss3))
            {
                boss3.TakeDamage(9999);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            CloseBuffUiIfOpen();
            await ClearField();

            // ================= 场景 4：三型 P2 编队齐射 + 弹幕墙 =================
            var boss4 = await SpawnTestBoss(3);
            Check(boss4 != null, "场景4：Boss 已生成");
            if (boss4 is null)
            {
                throw new System.InvalidOperationException("场景4：Boss 生成失败");
            }
            boss4.VOLLEY_DELAY = 0.4f;
            boss4.SetSummonTimer(999.0f); // 屏蔽常规召唤，保持计数纯净
            ForceP2Patterns(boss4, new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { ["attack"] = new StringName("minion_volley"), ["waves"] = 1, ["interval"] = 1.0 },
                new Godot.Collections.Dictionary { ["attack"] = new StringName("bullet_wall"), ["waves"] = 1, ["interval"] = 0.8 },
            });
            bool volleyRow = false;
            for (int i = 0; i < 40; i++)
            {
                await WaitReal(0.05);
                if (!GodotObject.IsInstanceValid(boss4))
                {
                    break;
                }
                int marked = 0;
                foreach (Node e in EnemiesAlive())
                {
                    if (e.HasMeta("hive_volley"))
                    {
                        marked++;
                    }
                }
                if (marked >= 4)
                {
                    volleyRow = true;
                    break;
                }
            }
            Check(volleyRow, "场景4：编队齐射召唤 4 小怪列横队（meta 标记）");
            // C34：420 弹速为小怪齐射（enemy.ENEMY_BULLET_SPEED，与 boss.VOLLEY_BULLET_SPEED 同值）；
            // 改 JSON 需两处同步。此处匹配在场全部 420 弹速弹（enemy 生成），故保留字面量并注明来源。
            int volleyMax = 0;
            for (int i = 0; i < 40; i++)
            {
                await WaitReal(0.05);
                if (!GodotObject.IsInstanceValid(boss4))
                {
                    break;
                }
                volleyMax = System.Math.Max(volleyMax, BulletsBySpeed(420.0f).Count);
                if (volleyMax >= 4)
                {
                    break;
                }
            }
            Check(volleyMax >= 4, "场景4：0.8s 后小怪齐射一轮自机狙（420 弹速普通敌弹）");
            bool volleyDmgOk = true;
            // 伤害随对局进程 ramp（2026-07-29 修订：×enemy_damage_ramp，基准 12）
            int volleyExpected = System.Math.Max(1, RoundHalfAway(12.0 * gs.EnemyDamageRamp()));
            foreach (Bullet b in BulletsBySpeed(420.0f))
            {
                if (b.Damage != volleyExpected)
                {
                    volleyDmgOk = false;
                }
            }
            Check(volleyDmgOk, $"场景4：齐射弹伤害随难度 ramp（基准 12，实测期望 {volleyExpected}）");
            // 弹幕墙：10 槽位留 2 相邻缺口，缺口避开自机方位 ±30°
            // C34：弹速从 boss 实例常量读取，改 JSON 不漂移
            float wallSpeed = boss4.WALL_BULLET_SPEED;
            var wall = new Godot.Collections.Array<Bullet>();
            for (int i = 0; i < 60; i++)
            {
                await WaitReal(0.05);
                if (!GodotObject.IsInstanceValid(boss4))
                {
                    break;
                }
                wall = BulletsBySpeed(wallSpeed);
                if (wall.Count >= 8)
                {
                    break;
                }
            }
            Check(wall.Count == 8, $"场景4：弹幕墙 10 槽位出 8 弹（留 2 缺口，实测 {wall.Count}）");
            if (wall.Count == 8)
            {
                var toPlayer = (player.GlobalPosition - boss4.GlobalPosition).Angle();
                // 槽位占用重建（缺口可能在弧段端部，相邻角差法不可靠）：逐槽比对弹丸方位
                float spacing = Mathf.DegToRad(150.0f) / 9.0f;
                float firstSlot = Vector2.Down.Angle() - Mathf.DegToRad(75.0f);
                var filled = new bool[10];
                foreach (Bullet b in wall)
                {
                    int idx = (int)RoundHalfAway((b.Direction.Angle() - firstSlot) / spacing);
                    if (idx >= 0 && idx < 10)
                    {
                        filled[idx] = true;
                    }
                }
                var missing = new System.Collections.Generic.List<int>();
                for (int i = 0; i < 10; i++)
                {
                    if (!filled[i])
                    {
                        missing.Add(i);
                    }
                }
                Check(missing.Count == 2 && missing[1] == missing[0] + 1,
                    $"场景4：缺口为 2 个相邻槽位（实测缺失槽 [{string.Join(", ", missing)}]）");
                bool gapFar = true;
                foreach (int m in missing)
                {
                    float slotA = firstSlot + spacing * m;
                    if (Mathf.Abs(Mathf.AngleDifference(slotA, toPlayer)) <= Mathf.DegToRad(28.0f))
                    {
                        gapFar = false;
                    }
                }
                Check(gapFar, "场景4：缺口方位避开自机 ±30°（保证可躲）");
            }
            boss4.TakeDamage(9999);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            CloseBuffUiIfOpen();
            await ClearField();

            // ================= 场景 5：三型狂暴「倾巢」 =================
            var boss5 = await SpawnTestBoss(3);
            Check(boss5 != null, "场景5：Boss 已生成");
            if (boss5 is null)
            {
                throw new System.InvalidOperationException("场景5：Boss 生成失败");
            }
            boss5.ENRAGE_DURATION = 2.0f;
            boss5.ENRAGE_TRANSITION_DURATION = 0.2f;
            boss5.ENRAGE_ATTACK_WINDUP = 0.1f;
            boss5.E3_SUMMON_INTERVAL = 0.4f;
            boss5.E3_RING_INTERVAL = 0.4f;
            boss5.ENRAGE_RELEASE_HOLD_DURATION = 0.5f;
            boss5.ENRAGE_RETURN_DURATION = 0.4f;
            boss5.SetSummonTimer(999.0f);
            await WaitReal(0.3);
            boss5.TakeDamage((int)(boss5.MaxHp * 0.75));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(boss5.IsEnraged(), "场景5：血量 <30% 触发狂暴");
            main.SetBulletTime(0.05f);
            bool active5 = false;
            for (int i = 0; i < 40; i++)
            {
                await WaitReal(0.1);
                if (!GodotObject.IsInstanceValid(boss5))
                {
                    break;
                }
                if (boss5.EnragePhaseValue() == (int)Boss.EnragePhase.ACTIVE)
                {
                    active5 = true;
                    break;
                }
            }
            Check(active5, "场景5：TRANSITION 结束进入 ACTIVE");
            // M3d：summon_waves() 无 Boss.cs 转发器——波次计数断言移除，待补转发器后恢复（见适配报告）
            int minionMax = 0;
            int ring5Max = 0;
            for (int i = 0; i < 40; i++) // ~2s 覆盖 ACTIVE
            {
                await WaitReal(0.05);
                if (!GodotObject.IsInstanceValid(boss5))
                {
                    break;
                }
                if (boss5.EnragePhaseValue() != (int)Boss.EnragePhase.ACTIVE)
                {
                    break;
                }
                minionMax = System.Math.Max(minionMax, EnemiesAlive().Count);
                ring5Max = System.Math.Max(ring5Max, CountMetaBullets(new StringName("enrage_ring")));
            }
            Check(minionMax >= 6, $"场景5：小怪波次在场（峰值 {minionMax} 只）");
            Check(ring5Max >= 8, "场景5：自身每 0.9s 一圈 8 向环弹");
            bool hold5 = false;
            for (int i = 0; i < 60; i++)
            {
                await WaitReal(0.05);
                if (!GodotObject.IsInstanceValid(boss5))
                {
                    break;
                }
                if (boss5.EnragePhaseValue() == (int)Boss.EnragePhase.RELEASE_HOLD)
                {
                    hold5 = true;
                    break;
                }
            }
            Check(hold5, "场景5：ACTIVE 结束进入 RELEASE_HOLD");
            int ring5Total = 0;
            int volley5Max = 0;
            // C34：420 同场景 4（小怪齐射 enemy.ENEMY_BULLET_SPEED，与 VOLLEY 同值，改 JSON 两处同步）
            for (int i = 0; i < 30; i++)
            {
                await WaitReal(0.05);
                if (!GodotObject.IsInstanceValid(boss5))
                {
                    break;
                }
                ring5Total = System.Math.Max(ring5Total, CountMetaBullets(new StringName("enrage_ring")));
                volley5Max = System.Math.Max(volley5Max, BulletsBySpeed(420.0f).Count);
                if (ring5Total >= 16 && volley5Max >= 3)
                {
                    break;
                }
            }
            Check(ring5Total >= 16, $"场景5：收尾一次性 16 向慢速环弹（峰值 {ring5Total}）");
            Check(volley5Max >= 3, $"场景5：收尾在场小怪齐射一轮（峰值 {volley5Max} 发）");
            if (GodotObject.IsInstanceValid(boss5))
            {
                boss5.TakeDamage(9999);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            CloseBuffUiIfOpen();
            await ClearField();

            // ================= 场景 6：难度分档（§4.4） =================
            // 分档在 Boss._ready 配置载入后一次性乘算，改难度必须在生成前；基准值均为 medium 档
            gs.Difficulty = "easy";
            var boss6e = await SpawnTestBoss(1);
            Check(boss6e != null, "场景6：easy Boss 已生成");
            if (boss6e is null)
            {
                throw new System.InvalidOperationException("场景6：easy Boss 生成失败");
            }
            Check(boss6e.E1_RING_COUNT == 10, $"场景6：easy 狂暴环弹 12-2=10（实测 {boss6e.E1_RING_COUNT}）");
            Check(boss6e.CANNON_SHOTS == 2, $"场景6：easy 蓄力重炮 3-1=2 发（实测 {boss6e.CANNON_SHOTS}）");
            // M3d：fan_delta/homing_delta/ring_delta 无 Boss.cs 转发器（BossAttacks 纯 C# 类）——弹数分档断言移除，待补转发器后恢复
            double p2IntervalE = boss6e.Patterns()["p2"].AsGodotArray()[0].AsGodotDictionary()["interval"].AsDouble();
            Check(Mathf.Abs(p2IntervalE - 2.4 * 1.15) < 0.01, $"场景6：easy 开火间隔 ×1.15（实测 {p2IntervalE:0.000}）");
            Check(Mathf.Abs((double)boss6e.FAN_BULLET_SPEED - 380.0 * 0.9) < 0.01, $"场景6：easy 弹速 ×0.9（实测 {boss6e.FAN_BULLET_SPEED:0.0}）");
            int hpE = (int)boss6e.MaxHp; // 显式 int()：max_hp 为 float（narrowing_conversion=2 门禁），HP 数值语义为整数
            boss6e.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            gs.Difficulty = "hard";
            var boss6h = await SpawnTestBoss(1);
            Check(boss6h != null, "场景6：hard Boss 已生成");
            if (boss6h is null)
            {
                throw new System.InvalidOperationException("场景6：hard Boss 生成失败");
            }
            Check(boss6h.E1_RING_COUNT == 14, $"场景6：hard 狂暴环弹 12+2=14（实测 {boss6h.E1_RING_COUNT}）");
            Check(boss6h.CANNON_SHOTS == 4, $"场景6：hard 蓄力重炮 3+1=4 发（实测 {boss6h.CANNON_SHOTS}）");
            double p2IntervalH = boss6h.Patterns()["p2"].AsGodotArray()[0].AsGodotDictionary()["interval"].AsDouble();
            Check(Mathf.Abs(p2IntervalH - 2.4 * 0.85) < 0.01, $"场景6：hard 开火间隔 ×0.85（实测 {p2IntervalH:0.000}）");
            Check(Mathf.Abs((double)boss6h.FAN_BULLET_SPEED - 380.0 * 1.1) < 0.01, $"场景6：hard 弹速 ×1.1（实测 {boss6h.FAN_BULLET_SPEED:0.0}）");
            Check(boss6h.MaxHp == (float)(hpE * 2),
                $"场景6：HP 随难度分档 ×0.75/×1.5（hard/easy=2.0，实测 {(int)boss6h.MaxHp}/{hpE}）");
            boss6h.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            gs.Difficulty = "medium";

            Check(Mathf.IsEqualApprox(Engine.TimeScale, 1.0f), "收尾：退出前 time_scale = 1.0");
            Check(Mathf.IsEqualApprox(player.EnrageSlow(), 1.0f), "收尾：退出前玩家减速已复位");
            await ClearField();
            await WaitReal(2.0); // 演出 tween/爆炸序列播完，避免退出时对象泄漏
            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            gs.HighScore = origHighScore;
            gs.SaveProfile();

            // ================= 场景 7：四型「月蚀」ring_burst 环弹 + P2 混合 =================
            await ClearField();
            var boss7 = await SpawnTestBoss(4);
            Check(boss7 != null, "场景7：月蚀已生成");
            if (boss7 is null)
            {
                throw new System.InvalidOperationException("场景7：Boss 生成失败");
            }
            boss7.SetPatterns(new Godot.Collections.Dictionary
            {
                ["p1"] = new Godot.Collections.Array
                {
                    new Godot.Collections.Dictionary { ["attack"] = new StringName("ring_burst"), ["waves"] = 1, ["interval"] = 0.5 },
                },
                ["p2"] = new Godot.Collections.Array(),
            });
            boss7.SetFightPhase(boss7.GetFightPhaseTransition());
            boss7.SetPatternIndex(0);
            boss7.StartPattern();
            boss7.SetFireTimer(0.1f);
            bool ringSeen = false;
            for (int i = 0; i < 40; i++)
            {
                await WaitReal(0.05);
                if (CountMetaBullets(new StringName("enrage_ring")) >= 12)
                {
                    ringSeen = true;
                    break;
                }
            }
            Check(ringSeen, "场景7：ring_burst 12 向全圆环弹（enrage_ring meta）");
            await ClearField();
            // P2：ring_burst + cross + sniper3（telegraph 起手）混合不崩、攻击可触发
            ForceP2Patterns(boss7, new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { ["attack"] = new StringName("ring_burst"), ["waves"] = 1, ["interval"] = 0.5 },
                new Godot.Collections.Dictionary { ["attack"] = new StringName("cross"), ["duration"] = 2.0, ["interval"] = 0.5 },
                new Godot.Collections.Dictionary { ["attack"] = new StringName("sniper3"), ["waves"] = 1, ["interval"] = 0.5 },
            });
            int p2Attacks = 0;
            for (int i = 0; i < 80; i++)
            {
                await WaitReal(0.05);
                if (CountMetaBullets(new StringName("enrage_ring")) >= 12 || CountMetaBullets(new StringName("cross")) >= 4)
                {
                    p2Attacks++;
                    if (p2Attacks >= 2)
                    {
                        break;
                    }
                }
            }
            Check(p2Attacks >= 2, "场景7：P2 ring_burst + cross 轮转触发");

            gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"BOSS PATTERN TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"BOSS PATTERN TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
