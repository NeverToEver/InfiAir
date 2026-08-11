namespace InfiAir.Core.Progression;

/// <summary>
/// 里程碑阈值曲线核心（2026-08-07 自 autoload/game_state.gd milestone_threshold 迁移）：
/// 8 档基础阈值循环，每循环档差按 ×cycleMultiplier^cycle 放大（阈值单调不回退），
/// 最终值 × difficultyMultiplier（难度档倍率）。纯 .NET、零 Godot 依赖 → xUnit 直测。
///
/// 行为与原 GDScript 实现逐位等价：累加顺序与 Math.Pow 调用逐项一致（同一 double
/// 运算序列，结果位级相同）；roundf 的 "half away from zero" 语义用
/// <see cref="Math.Round(double, MidpointRounding)"/> 对齐。
///
/// 迁移收益：apply_run_save 的 while 推进（上限 10000 档）原先每档一次 GDScript 求值，
/// <see cref="CountThresholdsUpTo"/> 改为单次跨语言调用 + O(1)/档 增量推进（档间复用
/// 同一累加序列，不重复外层循环）。
/// </summary>
public static class MilestoneCurve
{
    /// <summary>批量推进的档数上限（对齐原 GDScript apply_run_save 的 ms_cap=10000 挂死守卫）。</summary>
    public const int MaxIterations = 10000;

    /// <summary>
    /// 第 <paramref name="index"/> 次（0 起）里程碑的分数阈值；baseThresholds 为空返回 0。
    /// 循环/余数用 GDScript 整数除法语义（截断；非负域内与 C# 一致）。
    /// </summary>
    public static long Threshold(int index, long[] baseThresholds, double cycleMultiplier, double difficultyMultiplier)
    {
        var n = baseThresholds.Length;
        if (n <= 0)
        {
            return 0;
        }
        int idx = Math.Max(index, 0);
        int cycle = idx / n;
        int step = idx % n;
        double total = 0.0;
        for (int c = 0; c <= cycle; c++)
        {
            // A 审计（对齐原实现）：cycle_mult>1 时 pow 指数增长，极大 cycle 溢出至 inf —— 钳至有限
            double mult = Math.Min(Math.Pow(cycleMultiplier, c), 1e15);
            int lastStep = c == cycle ? step : n - 1;
            double prev = 0.0;
            for (int i = 0; i <= lastStep; i++)
            {
                total += (baseThresholds[i] - prev) * mult;
                prev = baseThresholds[i];
            }
        }
        return ToInt64(total * difficultyMultiplier);
    }

    /// <summary>
    /// 自 0 起连续满足 threshold(i) ≤ score 的档位数（封顶 <see cref="MaxIterations"/>），
    /// 即原 GDScript `while count &lt; cap and threshold(count) &lt;= score: count += 1` 的结果。
    /// 增量推进复用与 <see cref="Threshold"/> 相同的逐项累加序列（步进 + 循环边界换档），
    /// 任意 index 下结果逐位一致，整体 O(档数)。
    /// </summary>
    public static int CountThresholdsUpTo(
        long score, long[] baseThresholds, double cycleMultiplier, double difficultyMultiplier)
    {
        var n = baseThresholds.Length;
        if (n <= 0)
        {
            return 0;
        }
        double mult = Math.Min(Math.Pow(cycleMultiplier, 0), 1e15);
        double total = 0.0;
        int count = 0;
        while (count < MaxIterations)
        {
            int cycle = count / n;
            int step = count % n;
            if (step == 0)
            {
                if (cycle > 0)
                {
                    // 循环边界换档：mult 走与 Threshold 相同的 pow 调用（不增量连乘，避免 ULP 漂移）
                    mult = Math.Min(Math.Pow(cycleMultiplier, cycle), 1e15);
                }
                total += baseThresholds[0] * mult;
            }
            else
            {
                total += (baseThresholds[step] - baseThresholds[step - 1]) * mult;
            }
            if (ToInt64(total * difficultyMultiplier) > score)
            {
                break;
            }
            count++;
        }
        return count;
    }

    /// <summary>GDScript int(roundf(x)) 语义：roundf = half away from zero。
    /// 超 int64 范围在原实现为 UB（A 审计仅钳 mult 防 inf）——此处显式钳制保证确定性
    /// （difficulty_test 极大 index 断言：≥0 且非 int32 哨兵值）。</summary>
    private static long ToInt64(double value)
    {
        if (value >= 9.223372036854776E18)
        {
            return long.MaxValue;
        }
        if (value <= -9.223372036854776E18)
        {
            return long.MinValue;
        }
        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// 难度乘数对局进程曲线核心（2026-08-07 自 autoload/game_state.gd _recompute_difficulty 迁移）：
/// 1 + perBossKill×Boss击杀 + 时间轴累进（每 timeStepSeconds 量化一档，每 10 分钟 +perTenMinutes）。
/// 纯函数：输入即输出，xUnit 直测；与原 GDScript 表达式运算顺序逐位一致。
/// </summary>
public static class DifficultyCurve
{
    /// <summary>返回新难度乘数（是否变化由调用方 is_equal_approx 判定，与本函数无关）。</summary>
    public static double Compute(
        double runTime, double timeStepSeconds, double perTenMinutes, double perBossKill, int bossKills)
    {
        // AB12：0/负值钳制（既有口径）——负 runTime 使 step 为负、难度乘数反向下降；
        // 巨值防御——(long)Math.Floor 对超大 double 为未定义转换（实践得 long.MinValue），
        // 使难度乘数巨负击穿「单调不减」防线；1e6 秒 ≈ 11.6 天远超合理对局时长
        if (runTime <= 0.0)
        {
            return 1.0 + perBossKill * bossKills;
        }
        if (runTime > 1e6)
        {
            runTime = 1e6;
        }

        long step = (long)Math.Floor(runTime / timeStepSeconds);
        return 1.0 + perBossKill * bossKills + step * timeStepSeconds / 600.0 * perTenMinutes;
    }
}
