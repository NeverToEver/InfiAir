namespace InfiAir.Core.Progression;

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
        // 非有限（NaN/±∞）与巨值防御——NaN 使 runTime<=0 与 >1e6 皆假，(long)Math.Floor(NaN)
        // 得 long.MinValue，使难度乘数巨负击穿「单调不减」防线。两者一律按无时间累进处理，
        // 只留 Boss 项（NaN 与 1e6 秒 ≈ 11.6 天同属越界输入）。
        if (!double.IsFinite(runTime) || runTime <= 0.0)
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

        // 档数饱和：比值越过 long 域（微小 timeStep × 长 runTime，如 1e-14 与 1e6s ≈ 1e20）
        // 时 double→long 直接转换回绕成 long.MinValue，难度乘数巨负、曲线单调性反转；
        // 且回绕点之后档数不再随 runTime 增长，无论如何都已远超时间项的任何实际意义。
        const double MaxStep = (double)long.MaxValue;
        var steps = runTime / timeStepSeconds;
        var step = steps >= MaxStep ? long.MaxValue : (long)Math.Floor(steps);
        return 1.0 + perBossKill * bossKills + step * timeStepSeconds / 600.0 * perTenMinutes;
    }
}
