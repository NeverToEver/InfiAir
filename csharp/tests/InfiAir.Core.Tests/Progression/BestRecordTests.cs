using System.Collections.Generic;
using InfiAir.Core.Progression;
using Xunit;

namespace InfiAir.Core.Tests.Progression;

/// <summary>局末结果记录（user://best.json）的合并语义与写读单源测试。
/// 判坏了的表现都在结算页静默：更好的成绩没写进记录、或坏档把记录顶成 NaN 后逐项显示成乱码。</summary>
public sealed class BestRecordTests
{
    [Fact]
    public void Merge_TakesPerFieldBest()
    {
        var a = new BestRecord(120.0, 2, 1.8, false);
        var b = new BestRecord(300.0, 1, 1.2, true);

        var merged = a.Merge(b);

        Assert.Equal(300.0, merged.SurvivedSeconds);
        Assert.Equal(2, merged.BossKills);
        Assert.Equal(1.8, merged.MaxDifficulty);
        Assert.True(merged.GoalAchieved);
    }

    [Fact]
    public void Merge_KeepsGoalAchievedAgainstLaterRuns()
    {
        var achieved = new BestRecord(60.0, 1, 1.0, true);

        Assert.True(achieved.Merge(BestRecord.Empty).GoalAchieved);
    }

    [Fact]
    public void Merge_SanitizesNonFiniteAndNegativeValues()
    {
        // Math.Max(NaN, x) 会把 NaN 原样传出——不钳的话坏档值一路进记录与显示
        var bad = new BestRecord(double.NaN, -5, double.PositiveInfinity, false);

        var merged = bad.Merge(new BestRecord(-1.0, 0, double.NaN, false));

        Assert.Equal(0.0, merged.SurvivedSeconds);
        Assert.Equal(0, merged.BossKills);
        Assert.Equal(0.0, merged.MaxDifficulty);
    }

    [Fact]
    public void IsImprovedBy_OnlyTrueWhenSomethingIsBetter()
    {
        var best = new BestRecord(120.0, 2, 1.8, false);

        Assert.False(best.IsImprovedBy(new BestRecord(120.0, 2, 1.8, false)));
        Assert.False(best.IsImprovedBy(new BestRecord(60.0, 1, 1.2, false)));
        Assert.True(best.IsImprovedBy(new BestRecord(121.0, 2, 1.8, false)));
        Assert.True(best.IsImprovedBy(new BestRecord(120.0, 2, 1.8, true)));
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
        var record = new BestRecord(725.5, 4, 3.25, true);

        var back = BestRecordCodec.FromFields(BestRecordCodec.ToFields(record));

        Assert.Equal(record, back);
        Assert.True(BestRecordCodec.VersionMatches(BestRecordCodec.ToFields(record)));
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
