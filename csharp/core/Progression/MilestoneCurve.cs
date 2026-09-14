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

    /// <summary>逐圈推进的硬上限（非有限倍率等不收敛输入的最后兜底，见 <see cref="Threshold"/>）。
    /// 取 int.MaxValue：int 索引的 cycle 恒 ≤ int.MaxValue（baseThresholds 至少 1 项），
    /// 故合法 int 调用者永不被截断；旧值 100_000 会把平坦曲线的大 index 门槛静默截到约一半。</summary>
    private const int MaxCycles = int.MaxValue;

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

        // 非有限倍率是 Math.Pow / 饱和判定的失控输入（NaN 参与比较恒假，循环永不早退）：
        // 视作 1.0=不放大（调用方本应域钳，此处自兜一层，保住下方的挂死保护）。
        if (!double.IsFinite(cycleMultiplier) || cycleMultiplier <= 0.0)
        {
            cycleMultiplier = 1.0;
        }

        if (!double.IsFinite(difficultyMultiplier))
        {
            difficultyMultiplier = 1.0;
        }

        int idx = Math.Max(index, 0);
        int cycle = idx / n;
        int step = idx % n;

        // 平坦曲线（cycle_mult == 1）闭式求值：每圈贡献恒为表末值 b[n-1]（内层累加即 b[lastStep]×mult），
        // 故 total = cycle×b[n-1] + b[step]。必须闭式，不能逐圈——手改存档可把 milestone_count 顶到
        // int.MaxValue，平坦曲线不饱和，逐圈是 O(index) 的主线程挂死。long 溢出由 double 域 + ToInt64 兜住。
        if (cycleMultiplier == 1.0)
        {
            double flat = ((double)cycle * baseThresholds[n - 1]) + baseThresholds[step];
            return ToInt64(flat * difficultyMultiplier);
        }

        double total = 0.0;
        for (int c = 0; c <= cycle; c++)
        {
            // cycle_mult>1 时 pow 指数增长，极大 cycle 溢出至 inf——必须钳至有限值（1e15）
            double mult = Math.Min(Math.Pow(cycleMultiplier, c), PowClamp);

            // 收缩曲线（cycle_mult<1，异常配置）：pow 下溢到 0 后每圈贡献恒为 0，立即收束，结果与跑完等价。
            if (mult <= 0.0)
            {
                break;
            }

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

            // 绝对圈数兜底（挂死保护）：上限取 int.MaxValue，int 索引的 cycle 恒 ≤ int.MaxValue，
            // 故合法调用者永不被截断；此处理论上只兜「表全零且 cycle_mult>1」这类不产生增量的退化配置。
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
