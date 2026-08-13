using Godot;

namespace InfiAir.Tests;

/// <summary>
/// A4 架构断言测试：声明式 buff 效果表（BUFF_EFFECTS）完整性与求值语义回归——
/// 表键集覆盖全部 player 侧 buff、pow/cap 的 cfg 键存在于 balance.json、
/// 三类效果（pow 乘算 / cap 堆叠 / bool 启用）求值与重构前公式逐点一致。
/// </summary>
public partial class BuffEffectsTest : Node
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

    /// <summary>按点分路径导航解析后的 JSON 字典（"a.b.c" → data["a"]["b"]["c"]），任一环缺失返回 Nil。</summary>
    private static Variant JsonAt(Variant data, string path)
    {
        var node = data;
        foreach (var key in path.Split("."))
        {
            if (node.VariantType == Variant.Type.Dictionary && node.AsGodotDictionary().ContainsKey(key))
            {
                node = node.AsGodotDictionary()[key];
            }
            else
            {
                return default;
            }
        }

        return node;
    }

    /// <summary>清理 Main 下测试实体（Enemy/Bullet 均为 C# 类，typed 判型）。</summary>
    private void FreeTestEntities(Node main)
    {
        foreach (var child in main.GetChildren())
        {
            if (child is Enemy || child is Bullet)
            {
                child.QueueFree();
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
            // 清理持久化状态，保证测试确定性
            gs.DeleteSave();
            // L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
            var origHighScore = gs.HighScore;
            gs.HighScore = 0;
            gs.SaveProfile();
            var mainScene = GD.Load<PackedScene>("res://scenes/main.tscn");
            gs.LoginGuest();  // T4：游客会话直接开局（StartPanel 已退役）
            AddChild(mainScene.Instantiate());
            var main = GetNode<Node>("Main");
            // 开场面板自显即暂停（冻结背景），先关闭解除
            var player = GetNode<Player>("Main/Player");
            var spawner = GetNode<Spawner>("Main/Spawner");
            player.SetAutoFire(false);
            player.SetInvincible(999.0f);
            spawner.SetProcess(false);
            // 关闭辅助瞄准追踪（cone 目标会设 homing_time=1.2，穿透弹绕 e1 螺旋永不达 e2），
            // 保证穿透/溅射测试弹道直线确定
            gs.AimFrameLayer = null;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            FreeTestEntities(main);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 1. 效果表完整性：键集覆盖全部 player 侧 buff，kind 合法，cfg 键存在于 balance.json
            var effects = player.GetBuffEffects();
            Check(effects.Count == 9, "BUFF_EFFECTS 表登记 9 项 player 侧 buff 效果");
            foreach (var id in new[] { "rapid_fire", "power_shot", "efficient_boost", "boost_recovery", "phase_dash", "spread_shot", "piercing", "explosive", "bullet_speed" })
            {
                Check(effects.ContainsKey(id), $"效果表包含 {id}");
            }

            var parsed = Json.ParseString(Godot.FileAccess.GetFileAsString(gs.BALANCE_PATH));
            var powCap = 0;
            foreach (var key in effects.Keys)
            {
                var id = key.AsStringName();
                var effect = effects[key].AsGodotDictionary();
                var kind = effect["kind"].AsString();
                Check(kind == "pow" || kind == "cap" || kind == "bool", $"效果表 {id} kind 合法");
                if (kind == "bool")
                {
                    Check(!effect.ContainsKey("cfg"), $"bool 效果 {id} 无 cfg 键");
                    continue;
                }

                powCap++;
                Check(JsonAt(parsed, effect["cfg"].AsString()).VariantType != Variant.Type.Nil, $"效果表 {id} 的 cfg 键存在于 balance.json");
            }

            Check(powCap == 8, "pow/cap 效果 8 项（bool 1 项）");
            // bullet_speed：弹速乘算因子（2026-08-04 新 buff）
            gs.AddBuff("bullet_speed");
            Check(Mathf.IsEqualApprox(player.BulletSpeedValue(), 1800.0f * 1.2f), "bullet_speed 1 层弹速 ×1.2");
            gs.AddBuff("bullet_speed");
            Check(Mathf.IsEqualApprox(player.BulletSpeedValue(), 1800.0f * Mathf.Pow(1.2f, 2)), "bullet_speed 2 层弹速 ×1.2²");
            // crit_shot：概率暴击参数缓存（层数 × 基础概率；选取路径会广播 buffs_changed，此处模拟）
            Check(Mathf.IsEqualApprox(player.CritChance, 0.0f), "无 crit_shot 暴击概率 0");
            gs.AddBuff("crit_shot");
            gs.AddBuff("crit_shot");
            gs.EmitSignal(GameState.SignalName.BuffsChanged);
            Check(Mathf.IsEqualApprox(player.CritChance, 0.12f * 2.0f), "crit_shot 2 层暴击概率 24%");
            Check(Mathf.IsEqualApprox(player.CritMultiplierValue, 2.0f), "crit_shot 暴击倍率 ×2");

            // 2. 乘算因子（pow）求值：与重构前公式逐点一致
            Check(Mathf.IsEqualApprox(player.FireIntervalValue(), 0.15f), "无 buff 开火间隔 0.15s");
            gs.AddBuff("rapid_fire");
            Check(Mathf.IsEqualApprox(player.FireIntervalValue(), 0.15f * 0.75f), "rapid_fire 1 层开火间隔 ×0.75");
            gs.AddBuff("rapid_fire");
            gs.AddBuff("rapid_fire");
            Check(Mathf.IsEqualApprox(player.FireIntervalValue(), 0.15f * Mathf.Pow(0.75f, 3)), "rapid_fire 3 层开火间隔 ×0.75³");
            Check(player.BulletDamageValue() == 10, "无 power_shot 弹伤 10");
            gs.AddBuff("power_shot");
            Check(player.BulletDamageValue() == 12, "power_shot 1 层弹伤 int(10×1.25)=12");
            gs.AddBuff("power_shot");
            Check(player.BulletDamageValue() == 15, "power_shot 2 层弹伤 int(10×1.25²)=15");
            Check(Mathf.IsEqualApprox(player.FuelDrainRate(), 35.0f), "无 buff 燃料消耗 35/s");
            gs.AddBuff("efficient_boost");
            Check(Mathf.IsEqualApprox(player.FuelDrainRate(), 35.0f * 0.75f), "efficient_boost 1 层消耗 ×0.75");
            Check(Mathf.IsEqualApprox(player.FuelRegenRate(), 20.0f), "无 buff 燃料恢复 20/s");
            gs.AddBuff("boost_recovery");
            Check(Mathf.IsEqualApprox(player.FuelRegenRate(), 30.0f), "boost_recovery 1 层恢复 ×1.5");
            gs.AddBuff("boost_recovery");
            Check(Mathf.IsEqualApprox(player.FuelRegenRate(), 45.0f), "boost_recovery 2 层恢复 ×2.25");

            // 3. phase_dash：首次解锁不缩冷却，之后每层 ×0.8
            Check(!player.DashUnlocked(), "无 phase_dash 冲刺未解锁");
            gs.AddBuff("phase_dash");
            Check(player.DashUnlocked(), "phase_dash 1 层解锁冲刺");
            Check(Mathf.IsEqualApprox(player.DashCooldownMax(), 4.0f), "phase_dash 1 层冷却保持 4s");
            gs.AddBuff("phase_dash");
            Check(Mathf.IsEqualApprox(player.DashCooldownMax(), 3.2f), "phase_dash 2 层冷却 ×0.8");

            // 4. 堆叠上限（cap）：piercing 1 层子弹穿透直线两敌（此时无 spread，单发直线弹道）
            gs.AddBuff("piercing");
            var enemyScene = GD.Load<PackedScene>("res://scenes/enemy.tscn");
            var e1 = enemyScene.Instantiate<Enemy>();
            e1.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
            e1.Hp = 9999;
            e1.Speed = 0.0f;
            e1.CanShoot = false;
            e1.Position = player.GlobalPosition + new Vector2(0.0f, -300.0f);
            main.AddChild(e1);
            var e2 = enemyScene.Instantiate<Enemy>();
            e2.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
            e2.Hp = 9999;
            e2.Speed = 0.0f;
            e2.CanShoot = false;
            e2.Position = player.GlobalPosition + new Vector2(0.0f, -600.0f);
            main.AddChild(e2);
            player.AimPointOverride = player.GlobalPosition + new Vector2(0.0f, -400.0f);
            // 降弹速至 600px/s（每 tick 10px << 判定窗口 ~32px）：消除 1800px/s 下每 tick 30px
            // 位移的隧穿（Area2D overlap 快照可能整跳跳过目标判定窗口，实测 ~40% 概率漏判）
            player.BULLET_SPEED = 600.0f;
            player.SetAutoFire(true);
            await Coroutine.WaitSeconds(this, 1.6);
            player.SetAutoFire(false);
            Check(GodotObject.IsInstanceValid(e1) && e1.Hp < 9999, "穿透弹命中前敌");
            Check(GodotObject.IsInstanceValid(e2) && e2.Hp < 9999, "piercing 1 层弹穿透命中后敌");

            // 4b. crit_shot（2026-08-04）：真实命中路径——固定 seed 后多发命中出现暴击（×2）与非暴击混合
            FreeTestEntities(main);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            gs.Buffs.Clear();  // 清掉 §1-§4 累积（rapid_fire/power_shot/crit），保证 16 发 × 10/20 的假设成立
            gs.AddBuff("crit_shot");
            gs.AddBuff("crit_shot");
            gs.AddBuff("crit_shot");
            gs.EmitSignal(GameState.SignalName.BuffsChanged);
            Check(Mathf.IsEqualApprox(player.CritChance, 0.36f), "crit_shot 3 层暴击概率 36%");
            var ce = enemyScene.Instantiate<Enemy>();
            ce.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
            ce.Hp = 99999;
            ce.Speed = 0.0f;
            ce.CanShoot = false;
            ce.Position = player.GlobalPosition + new Vector2(0.0f, -200.0f);
            main.AddChild(ce);
            player.AimPointOverride = player.GlobalPosition + new Vector2(0.0f, -200.0f);
            player.BULLET_SPEED = 600.0f;
            player.SetAutoFire(true);
            GD.Seed(20260804ul);
            await Coroutine.WaitSeconds(this, 2.4);  // ~16 发（0.15s 间隔）
            player.SetAutoFire(false);
            Check(GodotObject.IsInstanceValid(ce) && ce.Hp < 99999, "暴击测试：敌机受到伤害");
            if (GodotObject.IsInstanceValid(ce))
            {
                var dealt = (int)(99999.0 - ce.Hp);
                // 16 发 × (10 或 20)：纯普通 160、纯暴击 320；固定 seed 下应混合出现
                Check(dealt > 160 && dealt < 320, $"crit_shot：命中序列出现暴击与非暴击混合（{dealt} 点）");
            }

            // 5. 布尔（bool）：explosive 击毁目标溅射侧向近邻（40px，不在弹道上）
            FreeTestEntities(main);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            gs.AddBuff("explosive");
            var a = enemyScene.Instantiate<Enemy>();
            a.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
            a.Hp = 50;
            a.Speed = 0.0f;
            a.CanShoot = false;
            a.Position = player.GlobalPosition + new Vector2(0.0f, -300.0f);
            main.AddChild(a);
            var b = enemyScene.Instantiate<Enemy>();
            b.Setup(spawner.ENEMY_TYPES[0], "straight", 1.0f);
            b.Hp = 9999;
            b.Speed = 0.0f;
            b.CanShoot = false;
            b.Position = player.GlobalPosition + new Vector2(-40.0f, -300.0f);
            main.AddChild(b);
            player.SetAutoFire(true);
            // 弹速 600：A 距玩家 300px 约 0.5s 到达，4 发击毁（0.063s 间隔）约 0.7s，等 1.6s 保险
            await Coroutine.WaitSeconds(this, 1.6);
            player.SetAutoFire(false);
            Check(!GodotObject.IsInstanceValid(a) || a.Hp == 0, "explosive 击毁目标 A");
            Check(GodotObject.IsInstanceValid(b) && b.Hp < 9999, "爆炸溅射命中 40px 外近邻 B");

            // 6. 堆叠上限（cap）：spread_shot 3 层超上限（cap 2）一轮齐射 5 弹
            // （2026-08-13：弹数改奇数序列 1/3/5，每层 +2——偶数弹扇形无中心弹为准星方向负提升）
            FreeTestEntities(main);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            gs.AddBuff("spread_shot");
            gs.AddBuff("spread_shot");
            gs.AddBuff("spread_shot");
            player.SetAutoFire(true);
            // 轮询至第一轮出现即停（rapid_fire 3 层 0.063s 间隔，第一轮后 3.8 tick 内不再发）：
            // 消除「等固定 tick 数」在开火冷却残留时的首轮未发竞态
            var bullets = 0;
            for (var j = 0; j < 60; j++)
            {
                bullets = 0;
                foreach (var child in main.GetChildren())
                {
                    if (child is Bullet)
                    {
                        bullets++;
                    }
                }

                if (bullets > 0)
                {
                    break;
                }

                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            }

            player.SetAutoFire(false);
            Check(bullets == 5, "spread_shot 3 层（cap 2 层）一轮齐射 5 弹");

            // 清理测试实体，避免退出时资源残留；等音效播完再退
            FreeTestEntities(main);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Coroutine.WaitSeconds(this, 0.3);
            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            gs.HighScore = origHighScore;
            gs.SaveProfile();
            gs.DeleteSave();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"BUFF_EFFECTS TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"BUFF_EFFECTS TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
