namespace InfiAir.Core.Progression;

/// <summary>
/// 难度映射参数：难度乘数 D 作用到各敌方量上的全部斜率与上限。
/// 默认值与 data/balance.json 的 enemies.* / spawner.* 段一致（服务层加载时覆盖）。
/// </summary>
public sealed class DifficultyScalingConfig
{
    /// <summary>杂兵/精英 HP 斜率：×(1 + factor×(D−1))。</summary>
    public double HpRampFactor { get; set; } = 0.40;

    /// <summary>全部敌方伤害斜率：×(1 + factor×(D−1))。</summary>
    public double DamageRampFactor { get; set; } = 0.20;

    /// <summary>杂兵/精英速度斜率：×(1 + factor×(D−1))。</summary>
    public double SpeedRampFactor { get; set; } = 0.10;

    /// <summary>速度倍率硬上限：速度是唯一直接破坏可反应性的量，必须有顶。</summary>
    public double SpeedRampCap { get; set; } = 1.80;

    /// <summary>Boss HP 斜率：hp_base × hpMult × (1 + factor×(D−1))。
    /// 原实现直接乘完整 D（等价于 factor=1.0），斜率是杂兵的 4 倍，使 Boss 单点检查过早变成墙。</summary>
    public double BossHpRampFactor { get; set; } = 0.55;

    /// <summary>波次间隔的难度项：间隔 ÷ (1 + factor×(D−1))。</summary>
    public double SpawnDifficultyFactor { get; set; } = 0.15;

    /// <summary>波次间隔下限（秒）。</summary>
    public double WaveIntervalFloor { get; set; } = 2.5;

    /// <summary>敌机开火间隔下限（秒）：后期靠密度加压，但射速不得突破可反应下限。</summary>
    public double FireIntervalFloor { get; set; } = 1.20;

    /// <summary>每多少 D 增派一只精英（0/负 = 关闭，精英数恒为 1）。</summary>
    public double ElitePerDifficulty { get; set; } = 2.0;

    /// <summary>精英数量上限（含基础那只）。</summary>
    public int EliteCountCap { get; set; } = 3;

    /// <summary>D 的软上限起点：D 超过该值后时间项斜率按 TailSpeedFactor 折减（0/负 = 不设软上限）。</summary>
    public double DifficultySoftCapStart { get; set; } = 6.0;

    /// <summary>软上限之后的时间项斜率保留比例（<1 = 更慢）。</summary>
    public double DifficultyTailSpeedFactor { get; set; } = 0.5;
}

/// <summary>
/// 难度映射纯函数：把难度乘数 D 换算成各敌方量。纯 .NET、零 Godot 依赖，可独立单测。
///
/// 存在理由：这些换算原先散在 Enemy/Boss/Bullet/Spawner 四处，各自的斜率与上限口径只有读代码才知道，
/// 「曲线是什么形状」无法被断言——调整任何一处都只能靠实机感受。集中到这里后，曲线成为可测事实，
/// 长局探针（--long-probe）与单测都能直接钉住 t 时刻的量。
/// </summary>
public static class DifficultyScaling
{
    /// <summary>线性 ramp：×(1 + factor×(D−1))（D ≤ 1 时为 1.0，不反向削弱）。</summary>
    private static double Ramp(double difficulty, double factor)
    {
        if (!double.IsFinite(difficulty) || !double.IsFinite(factor) || factor <= 0.0)
        {
            return 1.0;
        }

        var d = difficulty <= 1.0 ? 1.0 : difficulty;
        return 1.0 + factor * (d - 1.0);
    }

    /// <summary>杂兵/精英 HP 乘区。</summary>
    public static double EnemyHpRamp(double difficulty, DifficultyScalingConfig cfg) =>
        Ramp(difficulty, cfg.HpRampFactor);

    /// <summary>全部敌方伤害乘区。</summary>
    public static double EnemyDamageRamp(double difficulty, DifficultyScalingConfig cfg) =>
        Ramp(difficulty, cfg.DamageRampFactor);

    /// <summary>杂兵/精英速度乘区（带上限）。</summary>
    public static double EnemySpeedRamp(double difficulty, DifficultyScalingConfig cfg)
    {
        var ramp = Ramp(difficulty, cfg.SpeedRampFactor);
        return cfg.SpeedRampCap > 0.0 ? Math.Min(ramp, Math.Max(cfg.SpeedRampCap, 1.0)) : ramp;
    }

    /// <summary>Boss HP 乘区（斜率独立于杂兵；原实现等价 factor=1.0）。</summary>
    public static double BossHpRamp(double difficulty, DifficultyScalingConfig cfg) =>
        Ramp(difficulty, cfg.BossHpRampFactor);

    /// <summary>Boss HP 绝对值：hp_base × 类型倍率 × 难度档倍率 × Boss ramp。</summary>
    public static double BossHp(double hpBase, double typeMult, double tierMult, double difficulty, DifficultyScalingConfig cfg) =>
        hpBase * typeMult * tierMult * BossHpRamp(difficulty, cfg);

    /// <summary>波次间隔：基础间隔 ÷ 难度项，钳下限（基础间隔 ≤0 时返回下限）。</summary>
    public static double WaveInterval(double baseInterval, double difficulty, DifficultyScalingConfig cfg)
    {
        var floor = cfg.WaveIntervalFloor > 0.0 ? cfg.WaveIntervalFloor : 0.0;
        if (!double.IsFinite(baseInterval) || baseInterval <= 0.0)
        {
            return floor;
        }

        var divisor = Ramp(difficulty, cfg.SpawnDifficultyFactor);
        var interval = baseInterval / (divisor > 0.0 ? divisor : 1.0);
        return interval < floor ? floor : interval;
    }

    /// <summary>敌机开火间隔：基础间隔 ÷ 难度项，钳下限（密度可升，射速不得突破可反应下限）。</summary>
    public static double FireInterval(double baseInterval, double difficulty, DifficultyScalingConfig cfg)
    {
        var floor = cfg.FireIntervalFloor > 0.0 ? cfg.FireIntervalFloor : 0.05;
        if (!double.IsFinite(baseInterval) || baseInterval <= 0.0)
        {
            return floor;
        }

        var divisor = Ramp(difficulty, cfg.SpawnDifficultyFactor);
        var interval = baseInterval / (divisor > 0.0 ? divisor : 1.0);
        return interval < floor ? floor : interval;
    }

    /// <summary>精英波数量：每 ElitePerDifficulty 点 D 增派一只，钳 [1, EliteCountCap]。
    /// 配置关闭（≤0）或上限 &lt;1 时恒为 1——至少一只，否则精英波会空转。</summary>
    public static int EliteCount(double difficulty, DifficultyScalingConfig cfg)
    {
        if (cfg.ElitePerDifficulty <= 0.0 || cfg.EliteCountCap <= 1 || !double.IsFinite(difficulty))
        {
            return 1;
        }

        var extra = (int)Math.Floor((difficulty - 1.0) / cfg.ElitePerDifficulty);
        if (extra < 0)
        {
            extra = 0;
        }

        return Math.Min(1 + extra, cfg.EliteCountCap);
    }

    /// <summary>
    /// 难度乘数的时间项软上限：时间项超过 softCapStart 之后按 tailSpeedFactor 折减其超出部分。
    /// 只折时间项（Boss 击杀项不动）——它才是「挂机也会涨」的那条，也是必死时点方差的主要来源。
    /// 配置关闭（start ≤0 或 factor ≥1 或 factor ≤0）时原样返回。
    /// </summary>
    public static double SoftCappedTimeTerm(double timeTerm, DifficultyScalingConfig cfg)
    {
        if (!double.IsFinite(timeTerm) || timeTerm <= 0.0)
        {
            return 0.0;
        }

        var start = cfg.DifficultySoftCapStart;
        var factor = cfg.DifficultyTailSpeedFactor;
        if (start <= 0.0 || factor <= 0.0 || factor >= 1.0 || timeTerm <= start)
        {
            return timeTerm;
        }

        return start + (timeTerm - start) * factor;
    }
}
