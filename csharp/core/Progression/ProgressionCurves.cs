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

    /// <summary>逐圈推进的硬上限（tamper 存档可传入巨 milestone_count，见 <see cref="Threshold"/>）。</summary>
    private const int MaxCycles = 100_000;

    /// <summary>pow 指数增长的上限（1e15）与 int64 饱和判定阈值。</summary>
    private const double PowClamp = 1e15;
    private const double Int64Saturation = 9.223372036854776E18;

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
            double mult = Math.Min(Math.Pow(cycleMultiplier, c), PowClamp);
            int lastStep = c == cycle ? step : n - 1;
            double prev = 0.0;
            for (int i = 0; i <= lastStep; i++)
            {
                total += (baseThresholds[i] - prev) * mult;
                prev = baseThresholds[i];
            }

            // 饱和早退：阈值表非负 → 每圈增量非负 → total 单调不减；一旦乘上难度倍率越过 int64 上限，
            // 后续项只会更大，ToInt64 必钳 long.MaxValue。tamper 存档把 milestone_count 改到上亿时，
            // 逐圈推进（O(index)）会冻住主线程——此处在曲线饱和后立即收束，结果与跑完等价。
            if (total * difficultyMultiplier >= Int64Saturation)
            {
                break;
            }

            // 绝对圈数兜底：cycleMultiplier ≤ 1 或阈值表非单调时曲线不饱和，逐圈仍是 O(index)，
            // 必须硬停（停在曲线上限处，属 tamper 输入的取值钳制）。
            if (c >= MaxCycles)
            {
                break;
            }
        }
        return ToInt64(total * difficultyMultiplier);
    }

    /// <summary>阈值取 int 档位（引擎消费用）：把 <see cref="Threshold"/> 的 long 结果钳进 int 域。
    /// 手改存档可让 long 阈值越过 int 上限，直接窄化会回绕成负数——负阈值会让分数推进的
    /// while 每帧狂刷档位。此处显式钳上界，低界钳 0（负阈值视作无门槛）。</summary>
    public static int ThresholdInt(int index, long[] baseThresholds, double cycleMultiplier, double difficultyMultiplier)
    {
        var t = Threshold(index, baseThresholds, cycleMultiplier, difficultyMultiplier);
        if (t <= 0L)
        {
            return 0;
        }

        return t >= int.MaxValue ? int.MaxValue : (int)t;
    }

    /// <summary>GDScript int(roundf(x)) 语义：roundf = half away from zero。
    /// 超 int64 范围显式钳制保证确定性（极大 index 下仍 ≥0 且非 int32 哨兵值）。</summary>
    private static long ToInt64(double value)
    {
        if (value >= Int64Saturation)
        {
            return long.MaxValue;
        }
        if (value <= -Int64Saturation)
        {
            return long.MinValue;
        }
        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// 难度乘数本局进程曲线核心：
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
        // 使难度乘数巨负击穿「单调不减」防线；1e6 秒 ≈ 11.6 天远超合理本局时长
        if (runTime <= 0.0)
        {
            return 1.0 + perBossKill * bossKills;
        }
        if (runTime > 1e6)
        {
            runTime = 1e6;
        }

        if (timeStepSeconds <= 0.0 || !double.IsFinite(timeStepSeconds))
        {
            // 量化步长非正时时间项无定义（除零 → inf，double→long 转换越界）：
            // 视作无时间累进，只留 Boss 项（调用方本应域钳，此处自兜一层）
            return 1.0 + perBossKill * bossKills;
        }

        long step = (long)Math.Floor(runTime / timeStepSeconds);
        return 1.0 + perBossKill * bossKills + step * timeStepSeconds / 600.0 * perTenMinutes;
    }
}
