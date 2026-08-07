using Godot;
using InfiAir.Core.Progression;

namespace InfiAir;

/// <summary>
/// 2026-08-07 绑定壳：GDScript → C# 进程曲线桥（InfiAir.Core.Progression 纯函数）。
/// GDScript 侧（autoload/game_state.gd）milestone_threshold / _recompute_difficulty /
/// apply_run_save 的里程碑批量推进转发至此；公开语义与原 GDScript 实现逐位等价。
/// </summary>
public partial class ProgressionInterop : RefCounted
{
    /// <summary>第 index 次里程碑阈值（milestone_base 为已校验的 Array[int]，循环 × cycle_mult 放大）。</summary>
    public long MilestoneThreshold(long index, Godot.Collections.Array baseThresholds, double cycleMultiplier, double difficultyMultiplier)
    {
        return MilestoneCurve.Threshold(CheckedIndex(index), ToLongArray(baseThresholds), cycleMultiplier, difficultyMultiplier);
    }

    /// <summary>
    /// 自 0 起连续满足阈值 ≤ score 的档位数（封顶 10000，对齐原 apply_run_save 的 ms_cap）。
    /// 单次调用替代原 while 循环（每档一次跨语言往返 → 一次往返 + O(1)/档 增量推进）。
    /// </summary>
    public long CountThresholdsUpTo(long score, Godot.Collections.Array baseThresholds, double cycleMultiplier, double difficultyMultiplier)
    {
        return MilestoneCurve.CountThresholdsUpTo(score, ToLongArray(baseThresholds), cycleMultiplier, difficultyMultiplier);
    }

    /// <summary>难度进程曲线：1 + per_boss×kills + 时间轴累进（每 10 分钟 +per_ten）。</summary>
    public double DifficultyMultiplier(double runTime, double timeStepSeconds, double perTenMinutes, double perBossKill, long bossKills)
    {
        return DifficultyCurve.Compute(runTime, timeStepSeconds, perTenMinutes, perBossKill, CheckedIndex(bossKills));
    }

    /// <summary>milestone_base 为 GameState 校验后的非负 int 数组；防御性钳制防越界。</summary>
    private static int CheckedIndex(long value)
    {
        return (int)Math.Clamp(value, 0, int.MaxValue);
    }

    private static long[] ToLongArray(Godot.Collections.Array values)
    {
        var result = new long[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            // 前置校验在 GDScript 侧（milestone_base 元素级判型后才传入），此处直接转换
            result[i] = values[i].AsInt64();
        }
        return result;
    }
}
