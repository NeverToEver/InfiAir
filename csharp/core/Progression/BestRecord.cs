using InfiAir.Core.Storage;

namespace InfiAir.Core.Progression;

/// <summary>
/// 本局结果读数（跨局保留的个人最好）。边界见 DESIGN_BASELINE §1.16：只记**局末结果**——
/// 存活时长 / Boss 击杀 / 最高难度档 / 本局目标是否达成；**不含战力、不含分数**（计分仍只是
/// 隐藏进度引擎），故本记录不构成局外成长。
/// 写读两侧共用 <see cref="BestRecordCodec"/> 的键名与判型口径，坏值一律回默认而不抛。
/// </summary>
public sealed record BestRecord(double SurvivedSeconds, int BossKills, double MaxDifficulty, bool GoalAchieved)
{
    /// <summary>空记录：无档 / 版本不符 / 坏档的基线。全零，不区分「没打过」与「打了 0 秒」。</summary>
    public static readonly BestRecord Empty = new(0.0, 0, 0.0, false);

    /// <summary>逐项取更优：时长 / 击杀 / 难度取较大者，目标达成一旦为真不因后续局回退。</summary>
    public BestRecord Merge(BestRecord other) => new(
        Math.Max(Sane(SurvivedSeconds), Sane(other.SurvivedSeconds)),
        Math.Max(Math.Max(BossKills, 0), Math.Max(other.BossKills, 0)),
        Math.Max(Sane(MaxDifficulty), Sane(other.MaxDifficulty)),
        GoalAchieved || other.GoalAchieved);

    /// <summary>本局结果是否刷新了记录（结算页据此打「新纪录」）。逐项比较，含目标首次达成。</summary>
    public bool IsImprovedBy(BestRecord candidate) => Merge(candidate) != this;

    /// <summary>存活时长格式化为「分:秒」（秒位恒两位；非有限与负数按 0；不截断小时）。</summary>
    public static string FormatDuration(double seconds)
    {
        var total = (long)Math.Floor(Sane(seconds));
        return $"{total / 60}:{total % 60:00}";
    }

    /// <summary>非有限与负数一律折成 0——坏档值不得污染记录，也不得让 NaN 传进比较与显示
    /// （<see cref="Math.Max(double, double)"/> 遇 NaN 会把 NaN 原样传出）。</summary>
    public static double Sane(double value) => double.IsFinite(value) && value > 0.0 ? value : 0.0;
}

/// <summary>
/// 记录档案的写读单源：键名、判型、钳域都只写在这里（godot 侧只做文件 IO 与信号接线），
/// 从而不存在「写侧改了键名、读侧没跟」的静默丢档形态。
/// </summary>
public static class BestRecordCodec
{
    /// <summary>档案版本。与本局存档同口径：版本不符按无记录忽略（不隔离、不阻塞开机）。</summary>
    public const int Version = 1;

    private const string VersionKey = "version";
    private const string SurvivedSecondsKey = "survived_seconds";
    private const string BossKillsKey = "boss_kills";
    private const string MaxDifficultyKey = "max_difficulty";
    private const string GoalAchievedKey = "goal_achieved";

    /// <summary>写侧字段（含版本号）。整数一律写 long、目标达成写 0/1 的 long：值桥只认
    /// long/double/string/bool，而判型读口只认数值族——写 int 会在过桥时抛
    /// 「unsupported CLR value type System.Int32」，为布尔单开读口则等于把同一事实的读取规则写两遍。</summary>
    public static Dictionary<string, object?> ToFields(BestRecord record) => new()
    {
        [VersionKey] = (long)Version,
        [SurvivedSecondsKey] = record.SurvivedSeconds,
        [BossKillsKey] = (long)record.BossKills,
        [MaxDifficultyKey] = record.MaxDifficulty,
        [GoalAchievedKey] = record.GoalAchieved ? 1L : 0L,
    };

    /// <summary>版本是否匹配（不符按无记录处理）。</summary>
    public static bool VersionMatches(IReadOnlyDictionary<string, object?> data) =>
        RunFieldNormalize.ReadInt(data, VersionKey, 0) == Version;

    /// <summary>读侧：逐字段判型 + 钳域。字段缺失/类型不符/非有限数值一律回默认。</summary>
    public static BestRecord FromFields(IReadOnlyDictionary<string, object?> data) => new(
        BestRecord.Sane(RunFieldNormalize.ReadNum(data, SurvivedSecondsKey, 0.0)),
        Math.Max(RunFieldNormalize.ReadInt(data, BossKillsKey, 0), 0),
        BestRecord.Sane(RunFieldNormalize.ReadNum(data, MaxDifficultyKey, 0.0)),
        RunFieldNormalize.ReadInt(data, GoalAchievedKey, 0) != 0);
}
