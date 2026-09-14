namespace InfiAir.Core.Progression;

/// <summary>
/// 奖励缩放参数：难度乘数 D 如何反哺得分收入。默认值与 data/balance.json 一致。
/// </summary>
public sealed class RewardScalingConfig
{
    /// <summary>击杀分随 D 的增长斜率：×(1 + factor×(D−1))。
    /// 非零的理由：D 抬高敌方 HP 但击杀分固定 → 单位时间收入被 HP 膨胀稀释，
    /// 形成「越到后期越难攒点」的复利式劣势（RoR2 用 `moneyCost × coeff^1.25` 双轨对冲）。</summary>
    public double KillScoreRampFactor { get; set; } = 0.15;

    /// <summary>擦弹分是否吃连击乘区（0 = 不吃，1 = 与击杀同乘区）。</summary>
    public double GrazeComboWeight { get; set; } = 1.0;

    /// <summary>擦弹分随 D 的增长斜率。</summary>
    public double GrazeDifficultyFactor { get; set; } = 0.15;
}

/// <summary>
/// 奖励缩放纯函数：把难度乘数 D 与连击状态换算成得分。
/// 纯 .NET、零 Godot 依赖，可独立单测。
///
/// 存在理由：原实现里「敌方 HP/伤害随 D 涨、击杀分与事件奖励却是常量」，
/// 单位时间收入随 D 上升而下降——与行业惯例（RoR2 货币同步通胀 / StS 高难资源更少的明确单轨）
/// 都不同，是「越打越难攒点」的直接成因。集中到这里后，奖励曲线可被单测与探针钉住。
/// </summary>
public static class RewardScaling
{
    /// <summary>击杀分难度乘区（单调不减；D ≤ 1 时为 1.0，不倒扣）。</summary>
    public static double KillScoreFactor(double difficulty, RewardScalingConfig cfg) =>
        DifficultyRamp.Linear(difficulty, cfg.KillScoreRampFactor);

    /// <summary>
    /// 擦弹得分：基础分 × 难度乘区 × 连击加权。
    /// 连击加权 = 1 + (comboMult − 1) × weight——weight=0 时纯固定分（原行为），
    /// =1 时与击杀完全同乘区。至少返回 0（非负），非法输入按基础分处理。
    /// </summary>
    public static double GrazeScore(double baseScore, double comboMult, double difficulty, RewardScalingConfig cfg)
    {
        if (!double.IsFinite(baseScore) || baseScore <= 0.0)
        {
            return 0.0;
        }

        var mult = comboMult;
        if (!double.IsFinite(mult) || mult < 1.0)
        {
            mult = 1.0;
        }

        var weight = double.IsFinite(cfg.GrazeComboWeight) ? Math.Clamp(cfg.GrazeComboWeight, 0.0, 1.0) : 0.0;
        var comboFactor = 1.0 + (mult - 1.0) * weight;
        var value = baseScore * DifficultyRamp.Linear(difficulty, cfg.GrazeDifficultyFactor) * comboFactor;
        return value > 0.0 ? value : 0.0;
    }
}
