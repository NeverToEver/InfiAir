using System.Collections.Generic;
using InfiAir.Core.Machines;
using InfiAir.Core.Progression;
using Xunit;

namespace InfiAir.Core.Tests.Progression;

/// <summary>局末结果记录（user://best.json）的合并语义与写读单源测试。
/// 判坏了的表现都在结算页静默：更好的成绩没写进记录、或坏档把记录顶成 NaN 后逐项显示成乱码；
/// 机型标签的两处坏法是「换了机型但读数没变也算刷新记录」（每换一型写一次盘、结算页误报新纪录）
/// 与「旧档缺机型键读不出/读成空串」（机型回流数据与档一起丢掉）。</summary>
public sealed class BestRecordTests
{
    /// <summary>非标准型的标签：断言用（与名册里的 id 同拼写）。</summary>
    private const string Peregrine = "peregrine";

    [Fact]
    public void Merge_TakesPerFieldBest()
    {
        var a = new BestRecord(120.0, 2, 1.8, false, "standard");
        var b = new BestRecord(300.0, 1, 1.2, true, Peregrine);

        var merged = a.Merge(b);

        Assert.Equal(300.0, merged.SurvivedSeconds);
        Assert.Equal(2, merged.BossKills);
        Assert.Equal(1.8, merged.MaxDifficulty);
        Assert.True(merged.GoalAchieved);
        Assert.Equal(Peregrine, merged.MachineId);
    }

    [Fact]
    public void Merge_KeepsGoalAchievedAgainstLaterRuns()
    {
        var achieved = new BestRecord(60.0, 1, 1.0, true, Peregrine);

        var merged = achieved.Merge(BestRecord.Empty);

        Assert.True(merged.GoalAchieved);
        // 更差的一局不换标签：标签描述「最近一次刷新记录的那一局」，不是「最近打过的那一局」
        Assert.Equal(Peregrine, merged.MachineId);
    }

    [Fact]
    public void Merge_SanitizesNonFiniteAndNegativeValues()
    {
        // Math.Max(NaN, x) 会把 NaN 原样传出——不钳的话坏档值一路进记录与显示
        var bad = new BestRecord(double.NaN, -5, double.PositiveInfinity, false, "standard");

        var merged = bad.Merge(new BestRecord(-1.0, 0, double.NaN, false, "standard"));

        Assert.Equal(0.0, merged.SurvivedSeconds);
        Assert.Equal(0, merged.BossKills);
        Assert.Equal(0.0, merged.MaxDifficulty);
    }

    [Fact]
    public void IsImprovedBy_OnlyTrueWhenSomethingIsBetter()
    {
        var best = new BestRecord(120.0, 2, 1.8, false, "standard");

        Assert.False(best.IsImprovedBy(new BestRecord(120.0, 2, 1.8, false, "standard")));
        Assert.False(best.IsImprovedBy(new BestRecord(60.0, 1, 1.2, false, "standard")));
        Assert.True(best.IsImprovedBy(new BestRecord(121.0, 2, 1.8, false, "standard")));
        Assert.True(best.IsImprovedBy(new BestRecord(120.0, 2, 1.8, true, "standard")));
    }

    [Fact]
    public void IsImprovedBy_IgnoresMachineOnlyChange()
    {
        // 落盘门槛：同样读数、只换了机型不算刷新——否则每换一型打一局就写一次 best.json，
        // 结算页还要对一模一样的成绩打「新纪录」
        var best = new BestRecord(120.0, 2, 1.8, false, "standard");
        var sameReadingsOtherMachine = new BestRecord(120.0, 2, 1.8, false, Peregrine);

        Assert.False(best.IsImprovedBy(sameReadingsOtherMachine));
        Assert.Equal("standard", best.Merge(sameReadingsOtherMachine).MachineId);
    }

    [Fact]
    public void Merge_KeepsTheMachineOfTheImprovingRun()
    {
        var best = new BestRecord(120.0, 2, 1.8, false, "standard");

        // 读数刷新 → 标签换成刷新那一局的机型（只刷新 Boss 击杀也一样）
        Assert.Equal(Peregrine, best.Merge(new BestRecord(300.0, 2, 1.8, false, Peregrine)).MachineId);
        Assert.Equal(Peregrine, best.Merge(new BestRecord(120.0, 3, 1.8, false, Peregrine)).MachineId);

        // 机型标签本身一律收口：坏串不得顺着 Merge 进档案
        Assert.Equal("standard", best.Merge(new BestRecord(300.0, 2, 1.8, false, "no_such_machine")).MachineId);
    }

    [Theory]
    [InlineData(0.0, "0:00")]
    [InlineData(59.9, "0:59")]
    [InlineData(600.0, "10:00")]
    [InlineData(3600.0, "60:00")]
    [InlineData(-5.0, "0:00")]
    [InlineData(double.NaN, "0:00")]
    public void FormatDuration_IsMinuteSecondWithFloor(double seconds, string expected)
    {
        Assert.Equal(expected, BestRecord.FormatDuration(seconds));
    }

    [Fact]
    public void Codec_RoundTripsAllFields()
    {
        var record = new BestRecord(725.5, 4, 3.25, true, Peregrine);

        var back = BestRecordCodec.FromFields(BestRecordCodec.ToFields(record));

        Assert.Equal(record, back);
        Assert.Equal(Peregrine, back.MachineId);
        Assert.True(BestRecordCodec.VersionMatches(BestRecordCodec.ToFields(record)));
    }

    [Fact]
    public void Codec_WritesTheMachineKeySpelledLikeTheRunSave()
    {
        // 键名与 settings.json / run.json 同拼写：三处读同一个稳定串，改名要三处一起改
        Assert.Equal(Peregrine, BestRecordCodec.ToFields(new BestRecord(1.0, 0, 1.0, false, Peregrine))["machine"]);
    }

    [Fact]
    public void Codec_MissingMachineKeyKeepsTheStoredReadings()
    {
        // 旧档（加机型字段之前写的 best.json）：缺 machine 键必须可读入、不丢已存进度
        var legacy = new Dictionary<string, object?>
        {
            ["version"] = 1L,
            ["survived_seconds"] = 725.5,
            ["boss_kills"] = 4L,
            ["max_difficulty"] = 3.25,
            ["goal_achieved"] = 1L,
        };

        var record = BestRecordCodec.FromFields(legacy);

        Assert.Equal(725.5, record.SurvivedSeconds);
        Assert.Equal(4, record.BossKills);
        Assert.Equal(3.25, record.MaxDifficulty);
        Assert.True(record.GoalAchieved);
        Assert.Equal(MachineRoster.StandardId, record.MachineId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no_such_machine")]
    [InlineData("Peregrine")] // 大小写不同＝名册外（Ordinal 白名单，不做模糊匹配）
    public void Codec_UnknownMachineIdFallsBackToStandard(string machine)
    {
        var data = new Dictionary<string, object?> { ["version"] = 1L, ["machine"] = machine };

        Assert.Equal(MachineRoster.StandardId, BestRecordCodec.FromFields(data).MachineId);
    }

    [Fact]
    public void Codec_HostileFieldsFallBackToDefaults()
    {
        // 手改档是可达输入面：字符串/字典/NaN/非数值版本一律回默认，不抛
        var hostile = new Dictionary<string, object?>
        {
            ["version"] = 1L,
            ["survived_seconds"] = "no",
            ["boss_kills"] = new Dictionary<string, object?>(),
            ["max_difficulty"] = double.NaN,
            ["goal_achieved"] = "yes",
            ["machine"] = 42L,
        };

        Assert.Equal(BestRecord.Empty, BestRecordCodec.FromFields(hostile));
    }

    [Fact]
    public void Codec_VersionMismatchIsRejected()
    {
        var data = BestRecordCodec.ToFields(BestRecord.Empty);
        data["version"] = BestRecordCodec.Version + 1;

        Assert.False(BestRecordCodec.VersionMatches(data));
        Assert.False(BestRecordCodec.VersionMatches(new Dictionary<string, object?>()));
    }
}
