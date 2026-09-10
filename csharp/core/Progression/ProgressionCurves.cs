namespace InfiAir.Core.Progression;

/// <summary>
/// 里程碑阈值曲线核心：
/// 8 档基础阈值循环，每循环档差按 ×cycleMultiplier^cycle 放大（阈值单调不回退），
/// 最终值 × difficultyMultiplier（难度档倍率）。纯 .NET、零 Godot 依赖，可独立单测。
///
/// 行为与 Godot 引擎 64 位浮点运算逐位等价：累加顺序与 Math.Pow 调用逐项一致（同一 double
/// 运算序列，结果位级相同）；roundf 的 "half away from zero" 语义用
/// <see cref="Math.Round(double, MidpointRounding)"/> 对齐。
/// </summary>
public static class MilestoneCurve
{
    /// <summary>里程碑批量推进的档数上限（while 逐档推进的挂死守卫，超限直接 break）。</summary>
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
            // cycle_mult>1 时 pow 指数增长，极大 cycle 溢出至 inf——必须钳至有限值（1e15）
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

    /// <summary>GDScript int(roundf(x)) 语义：roundf = half away from zero。
    /// 超 int64 范围显式钳制保证确定性（极大 index 下仍 ≥0 且非 int32 哨兵值）。</summary>
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
/// 难度乘数对局进程曲线核心：
/// 1 + perBossKill×Boss击杀 + 时间轴累进（每 timeStepSeconds 量化一档，每 10 分钟 +perTenMinutes）。
/// 纯函数：输入即输出，零 Godot 依赖可独立单测；与引擎 64 位浮点表达式运算顺序逐位一致。
/// </summary>
public static class DifficultyCurve
{
    /// <summary>返回新难度乘数（是否变化由调用方 is_equal_approx 判定，与本函数无关）。</summary>
    public static double Compute(
        double runTime, double timeStepSeconds, double perTenMinutes, double perBossKill, int bossKills)
    {
        // 0/负值钳制——负 runTime 使 step 为负、难度乘数反向下降；
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
