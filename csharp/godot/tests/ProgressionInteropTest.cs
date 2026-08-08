using Godot;

namespace InfiAir.Tests;

/// <summary>
/// 2026-08-07 断言场景：ProgressionInterop（C# 绑定壳）——经互操作层计算
/// 里程碑阈值/难度进程曲线，验证 InfiAir.Core.Progression 语义在引擎环境内可用；
/// 生产链路已接入（GameState.milestone_threshold/_recompute_difficulty 转发，
/// 本场景直测绑定壳 + 抽查值与 difficulty_test/balance_test 同源）。
/// </summary>
public partial class ProgressionInteropTest : Node
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

            // 1. C# 绑定壳可加载并实例化
            var script = GD.Load<Script>("res://csharp/godot/ProgressionInterop.cs");
            Check(script != null, "ProgressionInterop 脚本资源可加载");
            ProgressionInterop interop = new()!; // Godot 生成构造器在 NRT 下视为可空，断言非空后取值
            var baseThresholds = Variant.From(gs.MilestoneBase).AsGodotArray().Duplicate();
            var cycleMult = gs.MilestoneCycleMult;

            // 2. 里程碑阈值：首循环 = 基础表（difficulty_test/balance_test 同源值）
            Check(interop.MilestoneThreshold(0, baseThresholds, cycleMult, 1.0) == 3000, "首档 3000");
            Check(interop.MilestoneThreshold(1, baseThresholds, cycleMult, 1.0) == 8000, "次档 8000");
            Check(interop.MilestoneThreshold(7, baseThresholds, cycleMult, 1.0) == 80000, "首循环末档 80000");

            // 3. 循环增长（×1.35^cycle）
            Check(interop.MilestoneThreshold(8, baseThresholds, cycleMult, 1.0) == 84050, "循环档 80000+3000×1.35");
            Check(interop.MilestoneThreshold(9, baseThresholds, cycleMult, 1.0) == 90800, "循环档 +5000×1.35");
            Check(interop.MilestoneThreshold(15, baseThresholds, cycleMult, 1.0) == 188000, "第二循环末 80000+80000×1.35");

            // 4. 难度阈值倍率（hard ×1.5）
            Check(interop.MilestoneThreshold(0, baseThresholds, cycleMult, 1.5) == 4500, "阈值倍率 ×1.5 首档 4500");
            Check(interop.MilestoneThreshold(7, baseThresholds, cycleMult, 1.5) == 120000, "阈值倍率 ×1.5 末档 120000");
            Check(interop.MilestoneThreshold(8, baseThresholds, cycleMult, 1.5) == 126075, "阈值倍率 ×1.5 循环档 126075");

            // 5. 极大 index 不溢出 UB（A 审计口径：≥0 且非 int32 哨兵值）
            var mtHuge = interop.MilestoneThreshold(99999, baseThresholds, cycleMult, 1.0);
            Check(mtHuge >= 0 && mtHuge != 2147483647, "极大 index 钳制不溢出");

            // 6. 批量推进：CountThresholdsUpTo 与逐档求值一致（含 10000 档挂死守卫）
            Check(interop.CountThresholdsUpTo(0, baseThresholds, cycleMult, 1.0) == 0, "score=0 无里程碑");
            Check(interop.CountThresholdsUpTo(2999, baseThresholds, cycleMult, 1.0) == 0, "2999 未达首档");
            Check(interop.CountThresholdsUpTo(3000, baseThresholds, cycleMult, 1.0) == 1, "3000 触发首档");
            Check(interop.CountThresholdsUpTo(84049, baseThresholds, cycleMult, 1.0) == 8, "84049 越过 80000 共 8 档");
            Check(interop.CountThresholdsUpTo(84050, baseThresholds, cycleMult, 1.0) == 9, "84050 越过第 9 档 84050 共 9 档");
            Check(interop.CountThresholdsUpTo(1_000_000_000, new Godot.Collections.Array { 100 }, 1.0, 1.0) == 10000,
                "曲线收敛场景封顶 10000（挂死守卫）");

            // 7. 难度进程曲线（1 + per_boss×kills + 时间轴累进）
            Check(Mathf.IsEqualApprox((float)interop.DifficultyMultiplier(0.0, 30.0, 1.5, 0.6, 0), 1.0f), "难度曲线：开局 ×1.0");
            Check(Mathf.IsEqualApprox((float)interop.DifficultyMultiplier(61.0, 30.0, 1.5, 0.6, 0), 1.15f), "难度曲线：2 档时间累进 +0.15");
            Check(Mathf.IsEqualApprox((float)interop.DifficultyMultiplier(300.0, 30.0, 1.5, 0.6, 2), 2.95f), "难度曲线：2 Boss + 10 档");

            // 8. 生产链路转发一致（GameState 已切 C# 壳）
            Check(gs.MilestoneThreshold(0) == 3000, "生产链路 milestone_threshold(0) = 3000");
            Check(gs.MilestoneThreshold(8) == 84050, "生产链路 milestone_threshold(8) = 84050");
            Check(gs.MilestoneThreshold(15) == 188000, "生产链路 milestone_threshold(15) = 188000");
            gs.SetDifficulty("hard");
            Check(gs.MilestoneThreshold(8) == 126075, "生产链路 hard 档阈值倍率生效");
            gs.SetDifficulty("medium");
            Check(gs.MilestoneThreshold(8) == 84050, "生产链路切回 medium 恢复 ×1");
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"PROGRESSION INTEROP TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"PROGRESSION INTEROP TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
