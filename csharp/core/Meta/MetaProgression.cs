namespace InfiAir.Core.Meta;

/// <summary>
/// 局外成长（Meta Progression）升级定义：单个科技树项（2026-08-09 计划 M1）。
/// Id 与 balance.json buffs.&lt;id&gt; 对齐——升级效果 = 新局开局预置该 buff 层数
/// （消费方在 GameState.Meta 与 ResetRun，见 docs/archive/2026-08-09-meta-progression-plan.md）。
/// 纯 .NET、零 Godot 依赖 → xUnit 直测。
///
/// 防御性构造（对齐 core 既有 U15/Q17 判型风格）：maxLevel ≤0 视为 1、baseCost 负值钳 0、
/// costGrowth 非有限或 ≤1 视为 1.0（价格不递减、不失控通胀；NaN 显式回退——
/// Math.Max(NaN, x) 返回 NaN，需先行判 NaN）。
/// </summary>
public sealed class UpgradeDef
{
    public string Id { get; }
    public int MaxLevel { get; }
    public double BaseCost { get; }
    public double CostGrowth { get; }

    public UpgradeDef(string id, int maxLevel, double baseCost, double costGrowth)
    {
        Id = id;
        MaxLevel = Math.Max(maxLevel, 1);
        BaseCost = NormalizeNonNegative(baseCost);
        CostGrowth = double.IsNaN(costGrowth) || costGrowth <= 1.0 ? 1.0 : costGrowth;
    }

    private static double NormalizeNonNegative(double v)
    {
        return double.IsNaN(v) ? 0.0 : Math.Max(v, 0.0);
    }
}

/// <summary>
/// 局外成长纯逻辑核心（2026-08-09 计划 M1）：科技点结算公式 + 升级费用曲线 + 上限判定。
///
/// 设计约束：
/// - 每项限级（UpgradeDef.MaxLevel）→ 总点数消费有上限，玩家终将毕业；敌人无界增长 →
///   不破坏 D1 必死曲线（bounded player growth, unbounded enemy pressure）。
/// - 结算唯一入口为死亡（SettleRun），本核心只做纯函数计算，不感知结算时机。
/// - 全部输入防御性钳制：负值钳 0、除零回退 1、越界/溢出钳 long.MaxValue——
///   手改配置/存档不崩、不产生负值货币（对齐 core 判型防御风格）。
/// </summary>
public static class MetaProgression
{
    /// <summary>long.MaxValue 数值上限（与 MilestoneCurve.ToInt64 同款显式钳制）。</summary>
    private const double LongMax = 9.223372036854776E18;

    /// <summary>第 <paramref name="level"/> 级（1-based，升到该级）的升级费用：
    /// base_cost × growth^(level-1)，half-away-from-zero 四舍五入取整。
    /// level ≤0 或超出 MaxLevel 回 0（不可再升）；计算溢出/NaN 钳 long.MaxValue。</summary>
    public static long CostForLevel(UpgradeDef? def, int level)
    {
        if (def == null || level <= 0 || level > def.MaxLevel)
        {
            return 0;
        }

        double raw = def.BaseCost * Math.Pow(def.CostGrowth, level - 1);
        if (double.IsNaN(raw) || raw >= LongMax)
        {
            return long.MaxValue;
        }

        return (long)Math.Round(raw, MidpointRounding.AwayFromZero);
    }

    /// <summary>升至 <paramref name="targetLevel"/> 的累计费用（1..target 各级之和，逐级累加防溢出）。
    /// target ≤0 回 0；超出 MaxLevel 按 MaxLevel 封顶计算。</summary>
    public static long TotalCostToLevel(UpgradeDef? def, int targetLevel)
    {
        if (def == null)
        {
            return 0;
        }

        int capped = Math.Min(Math.Max(targetLevel, 0), def.MaxLevel);
        long total = 0;
        for (int level = 1; level <= capped; level++)
        {
            total = ClampAdd(total, CostForLevel(def, level));
        }

        return total;
    }

    /// <summary>当前等级是否还可升级：def 非空且 currentLevel ∈ [0, MaxLevel)。</summary>
    public static bool CanUpgrade(UpgradeDef? def, int currentLevel)
    {
        return def != null && currentLevel >= 0 && currentLevel < def.MaxLevel;
    }

    /// <summary>本局死亡结算科技点：floor(score / scoreDivisor) + bossKills×bossKillBonus +
    /// missionsClaimed×missionBonus。负值输入钳 0；scoreDivisor ≤0 视为 1（防除零）；
    /// 乘法/加法溢出逐级钳 long.MaxValue（不抛、不溢出回绕）。</summary>
    public static long PointsForRun(
        long score, long bossKills, long missionsClaimed,
        long scoreDivisor, long bossKillBonus, long missionBonus)
    {
        long divisor = scoreDivisor <= 0 ? 1 : scoreDivisor;
        long byScore = Math.Max(score, 0) / divisor; // 商不溢出（divisor ≥ 1）
        long byBoss = ClampMul(Math.Max(bossKills, 0), Math.Max(bossKillBonus, 0));
        long byMission = ClampMul(Math.Max(missionsClaimed, 0), Math.Max(missionBonus, 0));
        return ClampAdd(ClampAdd(byScore, byBoss), byMission);
    }

    private static long ClampAdd(long a, long b)
    {
        return long.MaxValue - a < b ? long.MaxValue : a + b;
    }

    private static long ClampMul(long a, long b)
    {
        if (a == 0 || b == 0)
        {
            return 0;
        }

        return a > long.MaxValue / b ? long.MaxValue : a * b;
    }
}
