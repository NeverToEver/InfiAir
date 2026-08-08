using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 数值配置中心测试（M7c 样板）：加载、代表值抽查、损坏回退、改值生效（测完恢复 data/balance.json）。
/// 模式与 GDScript 断言场景一致：_Ready fire-and-forget + Check + TestExit.Quit(failures)。
/// </summary>
public partial class BalanceTest : Node
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

    public override void _Ready()
    {
        Run();
    }

    private void Run()
    {
        try
        {
            var gs = GetNode<GameState>("/root/GameState");
            var original = Godot.FileAccess.GetFileAsString(gs.BALANCE_PATH);
            gs.Difficulty = "medium";  // 里程碑倍率确定性

            // 1. 配置已加载（A2 阶段 1：改走公开 has_balance()，_balance 已移入 BalanceService）
            Check(gs.HasBalance(), "balance.json 已加载进内存");

            // 2. 代表值抽查（须与当前 data/balance.json 一致）
            Check(gs.Cfg("player.fuel.drain", 0.0).AsDouble() == 35.0, "燃料消耗 35/s");
            Check(gs.MilestoneThreshold(0) == 3000, "里程碑首档 3000");
            Check(gs.MilestoneCycleMult == 1.35, "里程碑循环倍率 1.35");
            Check(gs.Cfg("mothership.depart_cooldown", 0.0).AsDouble() == 60.0, "母舰冷却 60s");
            Check(gs.Cfg("mothership.mag_cells", 0).AsInt32() == 10, "弹匣 10 格");
            Check(gs.Cfg("mothership.missile.damage", 0).AsInt32() == 80, "母舰导弹直击 80");
            Check(gs.Cfg("boss.hp_mults", new Godot.Collections.Array()).AsGodotArray()[0].AsDouble() == 1.3, "Boss-1 HP 倍率 1.3");
            Check(gs.Cfg("boss.collision_damage", 0).AsInt32() == 30, "Boss 撞击 30");
            Check(gs.Cfg("player.max_speed", 0.0).AsDouble() == 420.0, "玩家满速 420");
            Check(gs.Cfg("player.max_health", 0.0).AsDouble() == 100.0, "玩家 100 HP");
            Check(gs.Cfg("player.bullet_damage", 0).AsInt32() == 10, "玩家弹伤 10");
            Check(gs.Cfg("player.dash.cooldown", 0.0).AsDouble() == 4.0, "冲刺冷却 4s");
            Check(gs.Cfg("enemies.collision_damage", 0).AsInt32() == 20, "敌机撞击 20");
            Check(gs.Cfg("enemies.bullet_damage.laser", 0).AsInt32() == 20, "laser 敌弹 20");
            Check(gs.Cfg("spawner.special_gap_max", 0).AsInt32() == 4, "特殊槽间隔上限 4");
            Check(gs.Cfg("buffs.slow_field.factor", 0.0).AsDouble() == 0.8, "慢速力场移速 ×0.8");
            Check(gs.Cfg("buffs.explosive.damage_per_level", 0).AsInt32() == 30, "爆炸弹固定溅射 30");
            Check(Mathf.IsEqualApprox((float)gs.Cfg("mothership.gatling.score_scale", 0.0).AsDouble(), 1.0f / 3.0f), "母舰击杀 1/3 分");
            Check(gs.Cfg("player.aim_assist.input.magnet_input_min", 0.0).AsDouble() == 2.0, "磁吸输入阈值下限 2.0");
            Check(gs.Cfg("player.aim_assist.falloff.peak", 0.0).AsDouble() == 400.0, "辅助距离衰减峰值 400");
            Check(gs.Cfg("player.aim_assist.levels.high.magnet_range", 0.0).AsDouble() == 130.0, "高档磁吸半径 130");

            // 3. 损坏 JSON → 回退脚本默认值
            var f = Godot.FileAccess.Open(gs.BALANCE_PATH, Godot.FileAccess.ModeFlags.Write);
            f.StoreString("{broken json!!!");
            f.Close();
            gs.ReloadBalance();
            Check(!gs.HasBalance(), "损坏 JSON 被丢弃");
            Check(gs.Cfg("player.fuel.drain", 35.0).AsDouble() == 35.0, "损坏后回退默认值");
            Check(gs.MilestoneThreshold(0) == 3000, "损坏后里程碑回退默认");

            // 4. 改值生效
            f = Godot.FileAccess.Open(gs.BALANCE_PATH, Godot.FileAccess.ModeFlags.Write);
            f.StoreString(Json.Stringify(new Godot.Collections.Dictionary { ["player"] = new Godot.Collections.Dictionary { ["fuel"] = new Godot.Collections.Dictionary { ["drain"] = 50.0 } } }));
            f.Close();
            gs.ReloadBalance();
            Check(gs.Cfg("player.fuel.drain", 0.0).AsDouble() == 50.0, "改值 50 生效");
            Check(gs.Cfg("player.max_speed", 420.0).AsDouble() == 420.0, "缺省键回退默认");

            // 4b. E03：难度段子键缺失 → 整表回退默认（不再 KeyError→0 HP/0 得分倍率）
            f = Godot.FileAccess.Open(gs.BALANCE_PATH, Godot.FileAccess.ModeFlags.Write);
            f.StoreString(Json.Stringify(new Godot.Collections.Dictionary { ["difficulty"] = new Godot.Collections.Dictionary { ["easy"] = new Godot.Collections.Dictionary { ["hp"] = 0.5 } } }));
            f.Close();
            gs.ReloadBalance();
            Check(gs.EnemyHpMultiplier() == 1.0, "E03：难度段缺子键回退默认（medium hp 1.0）");
            Check(gs.ScoreMultiplier() == 2, "E03：难度段缺子键回退默认（medium score 2）");

            // 5. 恢复原文件并恢复生效配置
            f = Godot.FileAccess.Open(gs.BALANCE_PATH, Godot.FileAccess.ModeFlags.Write);
            f.StoreString(original);
            f.Close();
            gs.ReloadBalance();
            Check(gs.Cfg("player.fuel.drain", 0.0).AsDouble() == 35.0, "原文件已恢复");

            // 6. A 审计：cfg 返回 Dictionary/Array 是拷贝而非内部引用（防止误写污染配置真值）
            var diffCfg = gs.Cfg("difficulty", new Godot.Collections.Dictionary());
            Check(diffCfg.VariantType == Variant.Type.Dictionary, "cfg difficulty 返回 Dictionary");
            if (diffCfg.VariantType == Variant.Type.Dictionary)
            {
                diffCfg.AsGodotDictionary().Clear();  // 修改返回值不应影响内部配置
            }
            Check(gs.EnemyHpMultiplier() == 1.0, "A审计：cfg 返回 Dictionary 拷贝隔离（清空不影响配置）");
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"BALANCE TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"BALANCE TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
