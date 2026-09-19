using InfiAir.Core.Machines;
using InfiAir.Core.Storage;

namespace InfiAir.Core.Progression;

/// <summary>
/// 本局结果读数（跨局保留的个人最好）。边界见 DESIGN_BASELINE §1.16：只记**局末结果**——
/// 存活时长 / Boss 击杀 / 最高难度档 / 本局目标是否达成 / 机型 id；**不含战力、不含分数**
/// （计分仍只是隐藏进度引擎），故本记录不构成局外成长。
/// 机型 id 是机型玩法层唯一的回流读数（口径见 `DESIGN_BASELINE` §1.16）：它是
/// **一次起飞配置的标签**，不是累积量；与 `settings.json` / `run.json` 的 `machine` 同源同拼写。
/// 写读两侧共用 <see cref="BestRecordCodec"/> 的键名与判型口径，坏值一律回默认而不抛。
/// </summary>
public sealed record BestRecord(
    double SurvivedSeconds,
    int BossKills,
    double MaxDifficulty,
    bool GoalAchieved,
    string MachineId)
{
    /// <summary>空记录：无档 / 版本不符 / 坏档的基线。读数全零、机型归标准型，
    /// 不区分「没打过」与「打了 0 秒」。</summary>
    public static readonly BestRecord Empty = new(0.0, 0, 0.0, false, MachineRoster.StandardId);

    /// <summary>
    /// 逐项取更优：时长 / 击杀 / 难度取较大者，目标达成一旦为真不因后续局回退。
    /// **机型标签＝最近一次刷新记录的那一局的机型**：本局一项读数都没刷新时保持原标签。
    /// 标签因此**不参与改善判定**（<see cref="IsImprovedBy"/> 由 Merge 结果与原记录逐位比较得出）——
    /// 「同样读数、只换了机型」不得触发落盘，否则每换一型打一局就写一次盘。
    /// </summary>
    public BestRecord Merge(BestRecord other) => new(
        Math.Max(Sane(SurvivedSeconds), Sane(other.SurvivedSeconds)),
        Math.Max(Math.Max(BossKills, 0), Math.Max(other.BossKills, 0)),
        Math.Max(Sane(MaxDifficulty), Sane(other.MaxDifficulty)),
        GoalAchieved || other.GoalAchieved,
        NormalizeMachine(ReadingsImprovedBy(other) ? other.MachineId : MachineId));

    /// <summary>本局结果是否刷新了记录（结算页据此打「新纪录」）。逐项比较，含目标首次达成。</summary>
    public bool IsImprovedBy(BestRecord candidate) => Merge(candidate) != this;

    /// <summary>本局读数是否刷新了本记录（四项逐项比较，含目标首次达成）。
    /// 与 <see cref="IsImprovedBy"/> 同一口径，只是**不看机型标签**——两处共用它才不会有
    /// 「换了机型算刷新」这种把标签当成绩的读法。</summary>
    private bool ReadingsImprovedBy(BestRecord other) =>
        Sane(other.SurvivedSeconds) > Sane(SurvivedSeconds)
        || Math.Max(other.BossKills, 0) > Math.Max(BossKills, 0)
        || Sane(other.MaxDifficulty) > Sane(MaxDifficulty)
        || (other.GoalAchieved && !GoalAchieved);

    /// <summary>机型 id 收口（白名单口径，单源在 <see cref="MachineRoster"/>）：未知 / 空串一律归标准型，
    /// 与 `settings.json` / `run.json` 的机型键同一条归一路径。写侧与读侧共用它，
    /// 档案里因此只会出现名册内的 id（旧档缺键＝标准型）。</summary>
    public static string NormalizeMachine(string? id) => MachineRoster.ById(id).Id;

    /// <summary>存活时长格式化为「分:秒」（秒位恒两位；非有限与负数按 0；不截断小时）。</summary>
    public static string FormatDuration(double seconds)
    {
        var total = (long)Math.Floor(Sane(seconds));
        return $"{total / 60}:{total % 60:00}";
    }

    /// <summary>
    /// 读出行（结算页「新纪录 / 历史最好」与标题屏「历史最好」）的格式化实参，顺序与译文里的
    /// 占位符一一对应：存活时长 / Boss 击杀 / 最高难度档。
    ///
    /// 收在 core 是因为**两处读出共用同一套实参**——各写一份时改一处忘另一处，玩家看到的是标题屏与
    /// 结算页对同一份记录显示不同的数；探针也取这里去填模板，判据才与两处读出的实参同源。
    /// 模板本身由 godot 侧经 Tr 给出（译文属翻译表单源，core 不持有文案）。
    /// </summary>
    public static object[] FormatArgs(BestRecord record) => new object[]
    {
        FormatDuration(record.SurvivedSeconds),
        record.BossKills,
        record.MaxDifficulty,
    };

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

    /// <summary>机型 id 的键名，与 `settings.json` / `run.json` 里的机型键**同拼写**
    /// （三处读同一个稳定串，改名要三处一起改）。</summary>
    private const string MachineKey = "machine";

    /// <summary>写侧字段（含版本号）。整数一律写 long、目标达成写 0/1 的 long：值桥只认
    /// long/double/string/bool，而判型读口只认数值族——写 int 会在过桥时抛
    /// 「unsupported CLR value type System.Int32」，为布尔单开读口则等于把同一事实的读取规则写两遍。
    /// 机型写**收口后**的 id：档案里不落名册外的串，读侧的白名单校验这才与写侧对称。</summary>
    public static Dictionary<string, object?> ToFields(BestRecord record) => new()
    {
        [VersionKey] = (long)Version,
        [SurvivedSecondsKey] = record.SurvivedSeconds,
        [BossKillsKey] = (long)record.BossKills,
        [MaxDifficultyKey] = record.MaxDifficulty,
        [GoalAchievedKey] = record.GoalAchieved ? 1L : 0L,
        [MachineKey] = BestRecord.NormalizeMachine(record.MachineId),
    };

    /// <summary>版本是否匹配（不符按无记录处理）。</summary>
    public static bool VersionMatches(IReadOnlyDictionary<string, object?> data) =>
        RunFieldNormalize.ReadInt(data, VersionKey, 0) == Version;

    /// <summary>读侧：逐字段判型 + 钳域。字段缺失/类型不符/非有限数值一律回默认。
    /// 机型键缺失（旧档）/ 判型不符 / 空串 / 名册外串一律归标准型——这就是「旧档缺键可读入、
    /// 不丢已存进度」那一条：其余四项读数照旧读出，只有标签落回基准。</summary>
    public static BestRecord FromFields(IReadOnlyDictionary<string, object?> data) => new(
        BestRecord.Sane(RunFieldNormalize.ReadNum(data, SurvivedSecondsKey, 0.0)),
        Math.Max(RunFieldNormalize.ReadInt(data, BossKillsKey, 0), 0),
        BestRecord.Sane(RunFieldNormalize.ReadNum(data, MaxDifficultyKey, 0.0)),
        RunFieldNormalize.ReadInt(data, GoalAchievedKey, 0) != 0,
        BestRecord.NormalizeMachine(RunFieldNormalize.ReadString(data, MachineKey, MachineRoster.StandardId)));
}
