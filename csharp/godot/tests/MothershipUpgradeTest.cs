using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 母舰火力升级测试（2026-08-04 母舰扩展 T1）：里程碑阈值 → 档位判定与倍率、
/// balance 配置读取。发射数值路径（int(damage * mult)）随 mothership_summon_test 回归覆盖。
/// </summary>
public partial class MothershipUpgradeTest : Node
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
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            var gs = GetNode<GameState>("/root/GameState");
            gs.LogoutUser();
            gs.DeleteSave();
            gs.ResetRun();
            gs.LoginGuest();
            gs.SetMilestoneCount(0);
            var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
            AddChild(main);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // 冻结刷怪/事件/自动开火，只验证母舰档位
            var spawner = main.GetNode<Spawner>("Spawner");
            spawner.SetProcess(false);
            main.Event()!.SetProcess(false);
            main.Formation()!.SetProcess(false);
            main.Player().SetAutoFire(false);
            GetTree().Paused = false;

            var ms = GD.Load<PackedScene>("res://scenes/mothership.tscn").Instantiate<Mothership>();  // M4：Mothership 迁 C#，typed 实例化
            main.AddChild(ms);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 1. 未升级档（默认 _ready 已读配置）
            Check(ms.Tier() == 0, "里程碑 0：未升级");
            Check(Mathf.IsEqualApprox(ms.DamageMult(), 1.0f) && Mathf.IsEqualApprox(ms.IntervalMult(), 1.0f), "未升级倍率 1.0");

            // 2. 升级档（里程碑 ≥ 阈值 5）
            gs.SetMilestoneCount(5);
            Check(ms.Tier() == 1, "里程碑 5：升级");
            Check(Mathf.IsEqualApprox(ms.DamageMult(), 1.5f) && Mathf.IsEqualApprox(ms.IntervalMult(), 0.8f), "升级档：伤害 ×1.5 / 射速 +25%");

            // 3. 配置键来自 balance.json（脚本默认值双写兜底）
            Check(gs.Cfg("mothership.upgrade.threshold", 0).AsInt32() == 5, "配置键 mothership.upgrade.threshold");
            Check(gs.Cfg("mothership.upgrade.damage_mult", 0.0).AsDouble() == 1.5, "配置键 mothership.upgrade.damage_mult");
            Check(gs.Cfg("mothership.upgrade.interval_mult", 0.0).AsDouble() == 0.8, "配置键 mothership.upgrade.interval_mult");

            // 4. 发射数值路径：档位伤害按倍率缩放（加特林/导弹共用 damage_mult）
            var baseDmg = ms.GATLING_DAMAGE;
            gs.SetMilestoneCount(0);
            Check((int)(baseDmg * ms.DamageMult()) == baseDmg, "未升级：发射伤害不变");
            gs.SetMilestoneCount(5);
            Check((int)(baseDmg * ms.DamageMult()) == (int)(baseDmg * 1.5), "升级：发射伤害 ×1.5");
            gs.SetMilestoneCount(0);

            ms.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            gs.LogoutUser();
            gs.DeleteSave();
            gs.ResetRun();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"MOTHERSHIP UPGRADE TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"MOTHERSHIP UPGRADE TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
